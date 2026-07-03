using System.Text.Json.Serialization;

namespace PaymentsAPI.Events;

public class OrderPlacedEvent
{
    [JsonPropertyName("OrderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("UserId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("GameId")]
    public string GameId { get; set; } = string.Empty;

    [JsonPropertyName("Amount")]
    public decimal Amount { get; set; }
    
    [JsonPropertyName("Price")]
    public decimal Price { get; set; }

    [JsonPropertyName("CreatedAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("PaymentDetails")]
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
