using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using PaymentsAPI.Events;
using PaymentsAPI.Messaging;
using PaymentsAPI.Services;
using NewRelic.Api.Agent;

namespace PaymentsAPI.BackgroundServices;

public class RabbitListenerService : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitListenerService> _logger;
    private readonly IPaymentNotificationPublisher _notificationPublisher;
    private IConnection? _connection;
    private IChannel? _channel;

    private readonly string _rabbitUrl;
    private readonly string _rabbitUser;
    private readonly string _rabbitPort;
    private readonly string _rabbitPass;
    private readonly string _orderExchange;
    private readonly string _paymentExchange;
    private readonly string _orderPlacedQueue;
    private readonly string _paymentProcessedQueue;
    private readonly string _orderPlacedRoutingKey;
    private readonly string _paymentProcessedRoutingKey;

    public RabbitListenerService(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IPaymentNotificationPublisher notificationPublisher,
        ILogger<RabbitListenerService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _notificationPublisher = notificationPublisher;
        _logger = logger;

        // Recuperando configurações do appsettings
        _rabbitUrl = _configuration["RabbitMQ:Host"] ?? "rabbitmq";
        _rabbitUser = _configuration["RabbitMQ:Username"] ?? "guest";
        _rabbitPort = _configuration["RabbitMQ:Port"] ?? "5672";
        _rabbitPass = _configuration["RabbitMQ:Password"] ?? "guest";
        _orderExchange = _configuration["RabbitMQ:OrderExchange"] ?? "order.exchange";
        _paymentExchange = _configuration["RabbitMQ:PaymentExchange"] ?? "payment.exchange";
        _orderPlacedQueue = _configuration["RabbitMQ:OrderPlacedQueue"] ?? "order.placed.payment-service";
        _paymentProcessedQueue = _configuration["RabbitMQ:PaymentProcessedQueue"] ?? "payment.processed.order-service";
        _orderPlacedRoutingKey = _configuration["RabbitMQ:OrderPlacedRoutingKey"] ?? "order.placed";
        _paymentProcessedRoutingKey = _configuration["RabbitMQ:PaymentProcessedRoutingKey"] ?? "payment.processed";
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await InitializeRabbitMQ();
        await base.StartAsync(cancellationToken);
    }

    private async Task InitializeRabbitMQ()
    {
        try
        {
            _logger.LogInformation("🔌 Conectando ao RabbitMQ em: {Url} e {Port}...", _rabbitUrl, _rabbitPort);

            var factory = new ConnectionFactory
            {
                UserName = _rabbitUser,
                Password = _rabbitPass
            };

            if (Uri.TryCreate(_rabbitUrl, UriKind.Absolute, out var uri))
            {
                factory.Uri = uri;
            }
            else
            {
                factory.HostName = _rabbitUrl;
            }

            _connection = await factory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true));

            // Configurando Topologia (Exchanges, Queues e Bindings)
            _logger.LogInformation(" Configurando topologia do RabbitMQ...");
            
            // _channel.ExchangeDeclare(_orderExchange, "", durable: true);
            await _channel.ExchangeDeclareAsync(_paymentExchange, ExchangeType.Fanout, durable: true);

            await _channel.QueueDeclareAsync(_orderPlacedQueue, durable: true, exclusive: false, autoDelete: false);
            await _channel.QueueDeclareAsync(_paymentProcessedQueue, durable: true, exclusive: false, autoDelete: false);
            await _channel.QueueBindAsync(_paymentProcessedQueue, _paymentExchange, _paymentProcessedRoutingKey);
            // _channel.QueueDeclare(_paymentProcessedQueue, durable: true, exclusive: false, autoDelete: false);

            // _channel.QueueBind(_orderPlacedQueue, _orderExchange, _orderPlacedRoutingKey);
            // _channel.QueueBind(_paymentProcessedQueue, _paymentExchange, _paymentProcessedRoutingKey);

            await _channel.BasicQosAsync(prefetchSize: 0,prefetchCount: 1,global: false);

            _logger.LogInformation(" Conexão com RabbitMQ estabelecida e topologia configurada com sucesso!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao inicializar o RabbitMQ. O serviço não iniciará até que a conexão esteja disponível.");
            throw;
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.ThrowIfCancellationRequested();

        if (_channel == null)
        {
            _logger.LogWarning("Canal do RabbitMQ não inicializado. O processamento de eventos não começará.");
            return Task.CompletedTask;
        }

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += ProcessMessageAsync;

        _channel.BasicConsumeAsync(queue: _orderPlacedQueue, autoAck: false, consumer: consumer);
        _logger.LogInformation("[RabbitListenerService] Iniciando consumo da fila: '{Queue}'", _orderPlacedQueue);

        return Task.CompletedTask;
    }

    [Transaction]
    private async Task ProcessMessageAsync(object model, BasicDeliverEventArgs ea)
    {
        if (_channel == null)
            return;

        var transaction = NewRelic.Api.Agent.NewRelic
            .GetAgent()
            .CurrentTransaction;

        if (ea.BasicProperties.Headers is { Count: > 0 } headers)
        {
            transaction.AcceptDistributedTraceHeaders(
                headers,
                GetHeaderValues,
                TransportType.Queue);
        }

        var body = ea.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);

        _logger.LogInformation(
            "[RabbitListenerService] Nova mensagem recebida da fila '{Queue}'",
            _orderPlacedQueue);

        try
        {
            var orderEvent = JsonSerializer.Deserialize<OrderPlacedEvent>(
                message,
                SerializerOptions);

            if (orderEvent == null || string.IsNullOrEmpty(orderEvent.OrderId))
            {
                throw new JsonException(
                    "Mensagem inválida recebida: objeto nulo ou sem ID.");
            }

            using var scope = _serviceProvider.CreateScope();

            var paymentService =
                scope.ServiceProvider.GetRequiredService<IPaymentService>();

            var deliveries = scope.ServiceProvider.GetRequiredService<PaymentDeliveryService>();
            await deliveries.DeliverAsync(
                orderEvent,
                () => paymentService.ProcessPaymentAsync(orderEvent),
                PublishPaymentProcessed,
                result => _notificationPublisher.PublishAsync(result, CancellationToken.None));

            // Confirma a mensagem somente depois das duas publicações.
            await _channel.BasicAckAsync(
                ea.DeliveryTag,
                multiple: false);

            _logger.LogInformation(
                "[RabbitListenerService] Mensagem do pedido {OrderId} confirmada.",
                orderEvent.OrderId);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "[RabbitListenerService] Mensagem inválida descartada.");

            await _channel.BasicAckAsync(
                ea.DeliveryTag,
                multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[RabbitListenerService] Erro ao processar o evento.");

            // Avoid a tight retry loop while a destination is unavailable.
            await Task.Delay(TimeSpan.FromSeconds(5));
            await _channel.BasicNackAsync(
                ea.DeliveryTag,
                multiple: false,
                requeue: true);
        }
    }

    private static IEnumerable<string> GetHeaderValues(
        IDictionary<string, object?> headers,
        string key)
    {
        if (!headers.TryGetValue(key, out var value) || value == null)
            return Array.Empty<string>();

        if (value is byte[] bytes)
            return new[] { Encoding.UTF8.GetString(bytes) };

        return new[] { value.ToString() ?? string.Empty };
    }

    private async Task PublishPaymentProcessed(PaymentProcessedEvent resultEvent)
    {
        if (_channel == null) throw new InvalidOperationException("Canal RabbitMQ indisponível.");

        _logger.LogInformation(" [RabbitListenerService] Publicando PaymentProcessedEvent para o Pedido: {OrderId} (Status: {Status})", resultEvent.OrderId, resultEvent.Status);

        var json = JsonSerializer.Serialize(resultEvent);
        var body = Encoding.UTF8.GetBytes(json);

        var headers = new Dictionary<string, object?>();

        NewRelic.Api.Agent.NewRelic.GetAgent()
            .CurrentTransaction
            .InsertDistributedTraceHeaders(
                headers,
                (carrier, key, value) => carrier[key] = value);

        var properties = new BasicProperties
        {
            MessageId = resultEvent.PaymentId,
            Persistent = true,
            ContentType = "application/json",
            Headers = headers
        };

        await _channel.BasicPublishAsync(
            exchange: _paymentExchange,
            routingKey: _paymentProcessedRoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: body
        );

        _logger.LogInformation(" [RabbitListenerService] Evento publicado no exchange '{Exchange}' com a routing key '{RoutingKey}'", _paymentExchange, _paymentProcessedRoutingKey);
    }

    public override void Dispose()
    {
        _channel?.CloseAsync();
        _connection?.CloseAsync();
        _logger.LogInformation(" Conexões com o RabbitMQ fechadas de forma limpa.");
        base.Dispose();
    }
}
