using System.Text.Json.Serialization;

namespace PaymentsAPI.Events;

public class PaymentProcessedEvent
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("paymentId")]
    public string PaymentId { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty; // "Approved" ou "Rejected"

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("processedAt")]
    public DateTime ProcessedAt { get; set; }
}
