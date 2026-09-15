using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using PaymentsAPI.Events;

namespace PaymentsAPI.Messaging;

public sealed class SqsPaymentNotificationPublisher : IPaymentNotificationPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAmazonSQS _sqs;
    private readonly string _queueName;
    private readonly ILogger<SqsPaymentNotificationPublisher> _logger;

    public SqsPaymentNotificationPublisher(
        IAmazonSQS sqs,
        IConfiguration configuration,
        ILogger<SqsPaymentNotificationPublisher> logger)
    {
        _sqs = sqs;
        _logger = logger;
        _queueName = configuration["AWS:PaymentNotificationQueue"]
            ?? "notification-payment-processed";
    }

    public async Task PublishAsync(
        PaymentProcessedEvent paymentProcessed,
        CancellationToken cancellationToken = default)
    {
        var queue = await _sqs.GetQueueUrlAsync(_queueName, cancellationToken);
        var body = JsonSerializer.Serialize(paymentProcessed, SerializerOptions);

        var response = await _sqs.SendMessageAsync(
            new SendMessageRequest
            {
                QueueUrl = queue.QueueUrl,
                MessageBody = body
            },
            cancellationToken);

        _logger.LogInformation(
            "PaymentProcessedEvent {PaymentId} enviado para a fila SQS {Queue}. MessageId: {MessageId}",
            paymentProcessed.PaymentId,
            _queueName,
            response.MessageId);
    }
}
