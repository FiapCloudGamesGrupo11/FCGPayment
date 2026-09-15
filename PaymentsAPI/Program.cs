using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using PaymentsAPI.BackgroundServices;
using PaymentsAPI.Messaging;
using PaymentsAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Adiciona serviços ao contêiner do DI
builder.Services.AddControllers();

// Registra os serviços do Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Registra nosso serviço de pagamento e o Listener do RabbitMQ
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddSingleton<IAmazonSQS>(_ =>
{
    var region = builder.Configuration["AWS:Region"] ?? "us-east-1";
    var serviceUrl = builder.Configuration["AWS:ServiceUrl"];

    if (!string.IsNullOrWhiteSpace(serviceUrl))
    {
        return new AmazonSQSClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = region
            });
    }

    return new AmazonSQSClient(RegionEndpoint.GetBySystemName(region));
});
builder.Services.AddSingleton<IPaymentNotificationPublisher, SqsPaymentNotificationPublisher>();
builder.Services.AddHostedService<RabbitListenerService>();

var app = builder.Build();

// Configura o pipeline de requisições HTTP
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "PaymentsAPI v1");
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
