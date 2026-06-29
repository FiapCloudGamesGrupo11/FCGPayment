using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace PaymentsAPI.Simulator;

class Program
{
    private static readonly string RabbitUrl = "amqp://guest:guest@localhost:5672";
    private static readonly string OrderExchange = "order.exchange";
    private static readonly string OrderPlacedRoutingKey = "order.placed";

    // Jogos simulados
    private static readonly (string Id, string Name, decimal Price)[] Games = new[]
    {
        ("g_elden_ring", "Elden Ring", 249.90m),
        ("g_gta_v", "Grand Theft Auto V", 69.90m),
        ("g_cyberpunk", "Cyberpunk 2077", 199.90m),
        ("g_indie_celeste", "Celeste", 36.99m),
        ("g_premium_setup", "Premium Collector Bundle", 850.00m) // Acima de 500 reais falha em PIX/Boleto
    };

    static void Main(string[] args)
    {
        Console.WriteLine(" [Simulador de Pedidos] Iniciando simulação de compra...");

        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(RabbitUrl) };
            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            // Declarando a exchange de pedidos
            channel.ExchangeDeclare(OrderExchange, ExchangeType.Topic, durable: true);

            // Escolhe um jogo aleatório
            var random = new Random();
            var game = Games[random.Next(Games.Length)];
            
            var orderId = "ord_" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();
            var userId = "usr_" + random.Next(1000, 9999);

            // Seleciona método de pagamento aleatório
            string[] methods = { "CREDIT_CARD", "PIX", "BOLETO" };
            var paymentMethod = methods[random.Next(methods.Length)];

            // 25% de chance de simular o cartão final 4444 (para testar rejeição de limite)
            var simulateRejectionCard = random.Next(100) < 25;
            var cardNumber = paymentMethod == "CREDIT_CARD"
                ? (simulateRejectionCard ? "1234-5678-9012-4444" : "1234-5678-9012-7890")
                : null;

            // Monta o payload do evento
            var orderEvent = new
            {
                orderId = orderId,
                userId = userId,
                gameId = game.Id,
                amount = game.Price,
                createdAt = DateTime.UtcNow,
                paymentDetails = new
                {
                    paymentMethod = paymentMethod,
                    cardNumber = cardNumber,
                    cvv = paymentMethod == "CREDIT_CARD" ? "123" : null,
                    expirationDate = paymentMethod == "CREDIT_CARD" ? "12/29" : null
                }
            };

            Console.WriteLine($" [Simulador de Pedidos] Novo Pedido Criado:");
            Console.WriteLine($"   - ID do Pedido: {orderEvent.orderId}");
            Console.WriteLine($"   - Usuário: {orderEvent.userId}");
            Console.WriteLine($"   - Jogo comprado: {game.Name} (ID: {game.Id})");
            Console.WriteLine($"   - Valor: R$ {orderEvent.amount}");
            Console.WriteLine($"   - Forma de Pagamento: {orderEvent.paymentDetails.paymentMethod} {(cardNumber != null ? $"(Cartão: {cardNumber})" : "")}");

            var json = JsonSerializer.Serialize(orderEvent);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";

            // Publica o evento
            channel.BasicPublish(
                exchange: OrderExchange,
                routingKey: OrderPlacedRoutingKey,
                basicProperties: properties,
                body: body
            );

            Console.WriteLine($" [Simulador de Pedidos] Evento 'OrderPlacedEvent' publicado na exchange '{OrderExchange}'!");
            Thread.Sleep(500); // Aguarda a mensagem ser despachada
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($" Erro no simulador de pedidos: {ex.Message}");
        }
    }
}
