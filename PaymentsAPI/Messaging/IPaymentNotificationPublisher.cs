using PaymentsAPI.Events;

namespace PaymentsAPI.Messaging;

public interface IPaymentNotificationPublisher
{
    Task PublishAsync(
        PaymentProcessedEvent paymentProcessed,
        CancellationToken cancellationToken = default);
}
