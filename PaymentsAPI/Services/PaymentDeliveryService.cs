using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PaymentsAPI.Events;

namespace PaymentsAPI.Services;

/// <summary>
/// Durable delivery journal for the local, single-replica payment simulator.
/// RabbitMQ redelivery retries only destinations not yet recorded as delivered.
/// </summary>
public sealed class PaymentDeliveryService
{
    private readonly string _directory;

    public PaymentDeliveryService(IConfiguration configuration)
    {
        _directory = Path.GetFullPath(configuration["PaymentDelivery:Directory"]
            ?? Path.Combine(AppContext.BaseDirectory, "payment-data"));
        Directory.CreateDirectory(_directory);
    }

    public async Task DeliverAsync(
        OrderPlacedEvent order,
        Func<Task<PaymentProcessedEvent>> process,
        Func<PaymentProcessedEvent, Task> publishRabbit,
        Func<PaymentProcessedEvent, Task> publishSqs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(order.OrderId);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(order.OrderId)));
        var path = Path.Combine(_directory, key + ".json");

        // A competing consumer fails and retries rather than processing concurrently.
        // Keep lock files: deleting them introduces a race with other processes.
        await using var lease = new FileStream(Path.Combine(_directory, key + ".lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        DeliveryState state;
        if (File.Exists(path))
        {
            try
            {
                state = JsonSerializer.Deserialize<DeliveryState>(await File.ReadAllTextAsync(path))
                    ?? throw new InvalidDataException("Registro de pagamento vazio.");
            }
            catch (JsonException exception)
            {
                // Do not classify damaged storage as an invalid incoming event and ACK it.
                throw new InvalidDataException("Registro de pagamento inválido.", exception);
            }

            if (state.Result.OrderId != order.OrderId || state.Result.UserId != order.UserId ||
                state.Result.GameId != order.GameId || state.Result.Amount != order.Amount)
                throw new InvalidOperationException("OrderId reutilizado com dados diferentes.");
        }
        else
        {
            state = new DeliveryState { Result = await process() };
            await SaveAsync(path, state);
        }

        if (!state.RabbitDelivered)
        {
            await publishRabbit(state.Result);
            state.RabbitDelivered = true;
            await SaveAsync(path, state);
        }

        if (!state.SqsDelivered)
        {
            await publishSqs(state.Result);
            state.SqsDelivered = true;
            await SaveAsync(path, state);
        }
    }

    private static async Task SaveAsync(string path, DeliveryState state)
    {
        // Replace only a fully written file; a crash must not truncate the last state.
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create,
            FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, state);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }

    public sealed class DeliveryState
    {
        public PaymentProcessedEvent Result { get; set; } = new();
        public bool RabbitDelivered { get; set; }
        public bool SqsDelivered { get; set; }
    }
}
