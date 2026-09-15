using Microsoft.Extensions.Configuration;
using PaymentsAPI.Events;
using PaymentsAPI.Services;
using Xunit;

namespace PaymentsAPI.Tests;

public sealed class PaymentDeliveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fcg-payment-tests", Guid.NewGuid().ToString("N"));
    private readonly OrderPlacedEvent _order = new() { OrderId = "order-1", UserId = "user-1", GameId = "game-1", Amount = 50 };
    private int _processed;

    private PaymentDeliveryService CreateService() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["PaymentDelivery:Directory"] = _directory }).Build());

    private Task<PaymentProcessedEvent> Process()
    {
        _processed++;
        return Task.FromResult(new PaymentProcessedEvent
        {
            OrderId = _order.OrderId!, UserId = _order.UserId, GameId = _order.GameId,
            Amount = _order.Amount, PaymentId = "payment-" + _processed, Status = "Approved"
        });
    }

    [Fact]
    public async Task SqsFailure_RestartRetriesOnlySqsWithOriginalPayment()
    {
        var rabbitCalls = 0;
        var sqsIds = new List<string>();
        Task Rabbit(PaymentProcessedEvent result) { rabbitCalls++; return Task.CompletedTask; }
        Task FailedSqs(PaymentProcessedEvent result) { sqsIds.Add(result.PaymentId); throw new IOException("SQS offline"); }

        await Assert.ThrowsAsync<IOException>(() => CreateService().DeliverAsync(_order, Process, Rabbit, FailedSqs));
        await CreateService().DeliverAsync(_order, Process, Rabbit, result =>
        {
            sqsIds.Add(result.PaymentId);
            return Task.CompletedTask;
        });

        Assert.Equal(1, _processed);
        Assert.Equal(1, rabbitCalls);
        Assert.Equal(new[] { "payment-1", "payment-1" }, sqsIds);
    }

    [Fact]
    public async Task RabbitFailure_RetriesDeliveryWithoutProcessingAgain()
    {
        var sqsCalls = 0;
        Task Sqs(PaymentProcessedEvent result) { sqsCalls++; return Task.CompletedTask; }
        await Assert.ThrowsAsync<IOException>(() => CreateService().DeliverAsync(_order, Process,
            _ => throw new IOException("Rabbit offline"), Sqs));
        Assert.Equal(0, sqsCalls);
        await CreateService().DeliverAsync(_order, Process, _ => Task.CompletedTask, Sqs);
        Assert.Equal(1, _processed);
        Assert.Equal(1, sqsCalls);
    }

    [Fact]
    public async Task CompletedOrder_RedeliveryPublishesNothing()
    {
        var calls = 0;
        Task Publish(PaymentProcessedEvent result) { calls++; return Task.CompletedTask; }
        await CreateService().DeliverAsync(_order, Process, Publish, Publish);
        await CreateService().DeliverAsync(_order, Process, Publish, Publish);
        Assert.Equal(1, _processed);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ReusedOrderIdWithDifferentAmount_IsRejected()
    {
        await CreateService().DeliverAsync(_order, Process, _ => Task.CompletedTask, _ => Task.CompletedTask);
        _order.Amount++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().DeliverAsync(
            _order, Process, _ => Task.CompletedTask, _ => Task.CompletedTask));
        Assert.Equal(1, _processed);
    }

    [Fact]
    public async Task CorruptJournal_DoesNotReprocessOrDiscardAsInvalidMessage()
    {
        await CreateService().DeliverAsync(_order, Process, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await File.WriteAllTextAsync(Directory.GetFiles(_directory, "*.json").Single(), "broken");
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateService().DeliverAsync(
            _order, Process, _ => Task.CompletedTask, _ => Task.CompletedTask));
        Assert.Equal(1, _processed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
