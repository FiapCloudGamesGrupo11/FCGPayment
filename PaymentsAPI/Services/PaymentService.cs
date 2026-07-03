using PaymentsAPI.Events;

namespace PaymentsAPI.Services;

public interface IPaymentService
{
    Task<PaymentProcessedEvent> ProcessPaymentAsync(OrderPlacedEvent order);
}

public class PaymentService : IPaymentService
{
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(ILogger<PaymentService> logger)
    {
        _logger = logger;
    }

    public async Task<PaymentProcessedEvent> ProcessPaymentAsync(OrderPlacedEvent order)
    {
        _logger.LogInformation(" [PaymentService] Processando pagamento para o Pedido: {OrderId} no valor de R$ {Amount}...", order.OrderId, order.Amount);

        // Simula tempo de resposta do gateway de pagamento (1.5 segundos)
        await Task.Delay(1500);

        var paymentId = "pay_" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();
        var status = "Approved";
        string? reason = null;

        // Regras de Simulação:
        // 1. Pagamentos com cartão terminado em '4444' serão simulados como REJEITADOS por saldo insuficiente.
        // 2. Pagamentos acima de R$ 500,00 com PIX/BOLETO serão simulados como REJEITADOS por limite diário excedido.
        if (string.Equals(order.PaymentDetails.PaymentMethod, "CREDIT_CARD", StringComparison.OrdinalIgnoreCase) && 
            order.PaymentDetails.CardNumber?.EndsWith("4444") == true)
        {
            status = "Rejected";
            reason = "Cartão recusado pelo emissor: Saldo insuficiente.";
        }
        else if ((string.Equals(order.PaymentDetails.PaymentMethod, "PIX", StringComparison.OrdinalIgnoreCase) || 
                  string.Equals(order.PaymentDetails.PaymentMethod, "BOLETO", StringComparison.OrdinalIgnoreCase)) && 
                 order.Amount > 500)
        {
            status = "Rejected";
            reason = "Transação negada: Limite de valor diário excedido para este método de pagamento.";
        }

        if (status == "Approved")
        {
            _logger.LogInformation(" [PaymentService] Pagamento {PaymentId} APROVADO para o Pedido {OrderId}.", paymentId, order.OrderId);
        }
        else
        {
            _logger.LogWarning(" [PaymentService] Pagamento {PaymentId} REJEITADO para o Pedido {OrderId}. Motivo: {Reason}", paymentId, order.OrderId, reason);
        }

        return new PaymentProcessedEvent
        {
            OrderId = order.OrderId,
            PaymentId = paymentId,
            UserId = order.UserId,
            GameId = order.GameId,
            Amount = order.Amount,
            Status = status,
            Reason = reason,
            ProcessedAt = DateTime.UtcNow
        };
    }
}
