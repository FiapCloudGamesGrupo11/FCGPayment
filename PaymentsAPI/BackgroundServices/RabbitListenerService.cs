using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using PaymentsAPI.Events;
using PaymentsAPI.Services;

namespace PaymentsAPI.BackgroundServices;

public class RabbitListenerService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitListenerService> _logger;
    private IConnection? _connection;
    private IModel? _channel;

    private readonly string _rabbitUrl;
    private readonly string _orderExchange;
    private readonly string _paymentExchange;
    private readonly string _orderPlacedQueue;
    private readonly string _paymentProcessedQueue;
    private readonly string _orderPlacedRoutingKey;
    private readonly string _paymentProcessedRoutingKey;

    public RabbitListenerService(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<RabbitListenerService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Recuperando configurações do appsettings
        _rabbitUrl = _configuration["RabbitMQ:Url"] ?? "amqp://guest:guest@localhost:5672";
        _orderExchange = _configuration["RabbitMQ:OrderExchange"] ?? "order.exchange";
        _paymentExchange = _configuration["RabbitMQ:PaymentExchange"] ?? "payment.exchange";
        _orderPlacedQueue = _configuration["RabbitMQ:OrderPlacedQueue"] ?? "order.placed.payment-service";
        _paymentProcessedQueue = _configuration["RabbitMQ:PaymentProcessedQueue"] ?? "payment.processed.order-service";
        _orderPlacedRoutingKey = _configuration["RabbitMQ:OrderPlacedRoutingKey"] ?? "order.placed";
        _paymentProcessedRoutingKey = _configuration["RabbitMQ:PaymentProcessedRoutingKey"] ?? "payment.processed";

        InitializeRabbitMQ();
    }

    private void InitializeRabbitMQ()
    {
        try
        {
            _logger.LogInformation("🔌 Conectando ao RabbitMQ em: {Url}...", _rabbitUrl);

            var factory = new ConnectionFactory
            {
                Uri = new Uri(_rabbitUrl),
                AutomaticRecoveryEnabled = true, // Reconexão automática em caso de falha na rede
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Configurando Topologia (Exchanges, Queues e Bindings)
            _logger.LogInformation(" Configurando topologia do RabbitMQ...");
            
            _channel.ExchangeDeclare(_orderExchange, ExchangeType.Topic, durable: true);
            _channel.ExchangeDeclare(_paymentExchange, ExchangeType.Topic, durable: true);

            _channel.QueueDeclare(_orderPlacedQueue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueDeclare(_paymentProcessedQueue, durable: true, exclusive: false, autoDelete: false);

            _channel.QueueBind(_orderPlacedQueue, _orderExchange, _orderPlacedRoutingKey);
            _channel.QueueBind(_paymentProcessedQueue, _paymentExchange, _paymentProcessedRoutingKey);

            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            _logger.LogInformation(" Conexão com RabbitMQ estabelecida e topologia configurada com sucesso!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " Erro ao inicializar o RabbitMQ. O serviço tentará novamente ao iniciar.");
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.ThrowIfCancellationRequested();

        if (_channel == null)
        {
            _logger.LogWarning("⚠️ Canal do RabbitMQ não inicializado. O processamento de eventos não começará.");
            return Task.CompletedTask;
        }

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            _logger.LogInformation("[RabbitListenerService] Nova mensagem recebida da fila '{Queue}'", _orderPlacedQueue);

            try
            {
                // 1. Fazer o parse do evento
                var orderEvent = JsonSerializer.Deserialize<OrderPlacedEvent>(message);
                if (orderEvent == null || string.IsNullOrEmpty(orderEvent.OrderId))
                {
                    throw new JsonException("Mensagem inválida recebida: Objeto desserializado está nulo ou sem ID.");
                }

                // 2. Processar o pagamento
                // IPaymentService é registrado como Scoped, portanto criamos um escopo para resolvê-lo
                using (var scope = _serviceProvider.CreateScope())
                {
                    var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();
                    var resultEvent = await paymentService.ProcessPaymentAsync(orderEvent);

                    // 3. Publicar o resultado
                    PublishPaymentProcessed(resultEvent);
                }

                // 4. Enviar confirmação de recebimento (Ack)
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
                _logger.LogInformation(" [RabbitListenerService] Mensagem do Pedido {OrderId} confirmada (ACK).\n", orderEvent.OrderId);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, " [RabbitListenerService] Falha de desserialização. Descartando mensagem (ACK de descarte)...");
                _channel.BasicAck(ea.DeliveryTag, multiple: false); // Descarta mensagem malformada
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [RabbitListenerService] Erro ao processar o evento. Reencaminhando mensagem para a fila...");
                // Reencaminha a mensagem (Requeue = true) em caso de erro temporário de infraestrutura
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        _channel.BasicConsume(queue: _orderPlacedQueue, autoAck: false, consumer: consumer);
        _logger.LogInformation("[RabbitListenerService] Iniciando consumo da fila: '{Queue}'", _orderPlacedQueue);

        return Task.CompletedTask;
    }

    private void PublishPaymentProcessed(PaymentProcessedEvent resultEvent)
    {
        if (_channel == null) return;

        _logger.LogInformation(" [RabbitListenerService] Publicando PaymentProcessedEvent para o Pedido: {OrderId} (Status: {Status})", resultEvent.OrderId, resultEvent.Status);

        var json = JsonSerializer.Serialize(resultEvent);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true; // Torna a mensagem persistente no disco
        properties.ContentType = "application/json";

        _channel.BasicPublish(
            exchange: _paymentExchange,
            routingKey: _paymentProcessedRoutingKey,
            basicProperties: properties,
            body: body
        );

        _logger.LogInformation(" [RabbitListenerService] Evento publicado no exchange '{Exchange}' com a routing key '{RoutingKey}'", _paymentExchange, _paymentProcessedRoutingKey);
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        _logger.LogInformation(" Conexões com o RabbitMQ fechadas de forma limpa.");
        base.Dispose();
    }
}
