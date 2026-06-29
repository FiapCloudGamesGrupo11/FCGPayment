using System.Text.Json.Serialization;

namespace PaymentsAPI.Events;

public class OrderPlacedEvent
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("paymentDetails")]
    public PaymentDetailsDto PaymentDetails { get; set; } = new();
}

public class PaymentDetailsDto
{
    [JsonPropertyName("paymentMethod")]
    public string PaymentMethod { get; set; } = string.Empty; // "CREDIT_CARD", "PIX", "BOLETO"

    [JsonPropertyName("cardNumber")]
    public string? CardNumber { get; set; }

    [JsonPropertyName("cvv")]
    public string? Cvv { get; set; }

    [JsonPropertyName("expirationDate")]
    public string? ExpirationDate { get; set; }
}
