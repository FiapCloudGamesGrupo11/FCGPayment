# FCGPayment - Microsserviço de Processamento de Pagamentos

## Descrição

O **FCGPayment** é um microsserviço responsável por processar pagamentos de compras de jogos na plataforma **FIAP Cloud Games (FCG)**. Ele consome eventos de pedidos do microsserviço FCGCatalog através de RabbitMQ, valida as transações, simula a comunicação com gateways de pagamento e publica eventos de confirmação de pagamento para outros microsserviços.

Este microsserviço utiliza **RabbitMQ** para comunicação assíncrona orientada por eventos, processando pedidos em tempo real com regras de negócio sofisticadas para validação e simulação de pagamentos.

---

## Funcionalidades Principais

- **Processamento de Pagamentos**: Valida e processa pedidos de compra de forma assíncrona
- **Consumo de Eventos**: Escuta fila `order-placed` para novos pedidos
- **Publicação de Eventos**: Publica resultados em exchange `payment.exchange` para Catalog e Notification
- **Tratamento de Erros**: Implementa retry logic e tratamento robusto de falhas
- **Logging Detalhado**: Rastreamento completo do processamento de cada transação

---

##  Dependências

- **RabbitMQ.Client**: Cliente para comunicação com RabbitMQ
- **Swashbuckle.AspNetCore**: Geração automática da documentação Swagger/OpenAPI

---

## Como Executar

### Pré-requisitos

- .NET 8.0 SDK ou superior instalado
- Docker e Docker Compose (para execução containerizada)
- RabbitMQ em execução (ou use o docker-compose fornecido)

### Opção 1: Executar com Docker Compose

```bash
# Na raiz do projeto FCGPayment
docker-compose up
```

Este comando inicia:
- **RabbitMQ** na porta 5672 (AMQP) e 15672 (Management UI)
- **FCGPayment API** na porta 8090 e 8091

Verifique se os containers estão saudáveis:

```bash
docker-compose ps
```

### Opção 2: Executar Localmente

1. **Certifique-se de que o RabbitMQ está em execução**:

```bash
# Use Docker apenas para RabbitMQ
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

2. **Configure as variáveis de ambiente** em `appsettings.Development.json`:

```json
{
  "RabbitMQ": {
    "Url": "localhost:5672",
    "User": "guest",
    "Password": "guest",
    "OrderExchange": "order.exchange",
    "PaymentExchange": "payment.exchange",
    "OrderPlacedQueue": "order-placed",
    "PaymentProcessedQueue": "payment-processed",
    "OrderPlacedRoutingKey": "order.placed",
    "PaymentProcessedRoutingKey": "payment.processed"
  }
}
```

3. **Restaure as dependências e execute a API**:

```bash
cd PaymentsAPI
dotnet restore
dotnet run
```

A API estará disponível em:
- **HTTP**: http://localhost:8090
- **HTTPS**: https://localhost:8091

---

## Acessar a API

### Documentação Swagger

- **Swagger UI**: http://localhost:8090/swagger
- **OpenAPI JSON**: http://localhost:8090/swagger/v1/swagger.json

### RabbitMQ Management UI

- **URL**: http://localhost:15672
- **Usuário**: guest
- **Senha**: guest

---

## Eventos

### 1. OrderPlacedEvent (Consumido)

**Origem**: FCGCatalog  
**Fila**: `order-placed`  
**Exchange**: `order.exchange`  
**Routing Key**: `order.placed`

**Payload**:
```json
{
  "orderId": "ORD_ABC123XYZ",
  "userId": "USR_1234",
  "gameId": "g_elden_ring",
  "amount": 249.90,
  "price": 249.90,
  "createdAt": "2026-07-12T18:30:00Z",
  "paymentDetails": {
    "paymentMethod": "CREDIT_CARD",
    "cardNumber": "1234-5678-9012-7890",
    "cvv": "123",
    "expirationDate": "12/29"
  }
}
```

**Métodos de Pagamento Suportados**:
- `CREDIT_CARD`: Cartão de Crédito
- `PIX`: PIX
- `BOLETO`: Boleto Bancário

---

### 2. PaymentProcessedEvent (Publicado)

**Destino**: FCGCatalog, FCGNotification  
**Exchange**: `payment.exchange`  
**Exchange Type**: Fanout (broadcast)  
**Routing Key**: `payment.processed`

**Payload**:
```json
{
  "orderId": "ORD_ABC123XYZ",
  "paymentId": "PAY_12345ABCDE",
  "userId": "USR_1234",
  "gameId": "g_elden_ring",
  "amount": 249.90,
  "status": "Approved",
  "reason": null,
  "processedAt": "2026-07-12T18:30:01.500Z"
}
```

**Status Possíveis**:
- `Approved`: Pagamento foi aprovado
- `Rejected`: Pagamento foi rejeitado

---

## Estrutura do Projeto

```
FCGPayment/
├── PaymentsAPI/
│   ├── BackgroundServices/
│   │   └── RabbitListenerService.cs    # Listener assíncrono de mensagens
│   ├── Events/
│   │   ├── OrderPlacedEvent.cs         # Evento de pedido criado
│   │   └── PaymentProcessedEvent.cs    # Evento de pagamento processado
│   ├── Services/
│   │   └── PaymentService.cs           # Lógica de processamento de pagamento
│   ├── Program.cs                       # Configuração da aplicação
│   ├── PaymentsAPI.csproj               # Arquivo de projeto
│   ├── PaymentsAPI.http                 # Requisições HTTP para teste
│   ├── appsettings.json                 # Configurações padrão
│   └── appsettings.Development.json     # Configurações de desenvolvimento
├── PaymentsAPI.Simulator/
│   ├── PaymentsAPI.Simulator.csproj      # Projeto do simulador
│   └── Program.cs                        # Implementação do simulador
├── Dockerfile                           # Build do container
├── docker-compose.yml                   # Orquestração de containers
└── README.md                            # Este arquivo
```

---

## Configuração do RabbitMQ

### appsettings.json

```json
{
  "RabbitMQ": {
    "Url": "localhost:5672",
    "User": "guest",
    "Password": "guest",
    "OrderExchange": "order.exchange",
    "PaymentExchange": "payment.exchange",
    "OrderPlacedQueue": "order-placed",
    "PaymentProcessedQueue": "payment-processed",
    "OrderPlacedRoutingKey": "order.placed",
    "PaymentProcessedRoutingKey": "payment.processed"
  }
}
```

### Variáveis de Ambiente

Você pode sobrescrever as configurações via variáveis de ambiente:

```bash
export RabbitMQ__Url=rabbitmq:5672
export RabbitMQ__User=guest
export RabbitMQ__Password=guest
export RabbitMQ__OrderExchange=order.exchange
export RabbitMQ__PaymentExchange=payment.exchange
export RabbitMQ__OrderPlacedQueue=order-placed
export RabbitMQ__PaymentProcessedQueue=payment-processed
export RabbitMQ__OrderPlacedRoutingKey=order.placed
export RabbitMQ__PaymentProcessedRoutingKey=payment.processed
```

### Topologia RabbitMQ

```
┌─────────────────────────────────────────────────────────────┐
│                     RabbitMQ Topology                        │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  INPUTS (Consumed by PaymentsAPI)                          │
│  ┌──────────────────────────────────────────────────┐       │
│  │ Exchange: order.exchange (Topic)                 │       │
│  │ Queue: order-placed                              │       │
│  │ Routing Key: order.placed                        │       │
│  │ Consumer: RabbitListenerService                  │       │
│  └──────────────────────────────────────────────────┘       │
│                                                              │
│  OUTPUTS (Published by PaymentsAPI)                        │
│  ┌──────────────────────────────────────────────────┐       │
│  │ Exchange: payment.exchange (Fanout)              │       │
│  │ Routing Key: payment.processed                   │       │
│  │ Consumers: FCGCatalog, FCGNotification           │       │
│  └──────────────────────────────────────────────────┘       │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

---

## Fluxo Completo de Pagamento

```
1. Usuário compra um jogo no FCGCatalog
   ↓
2. FCGCatalog publica OrderPlacedEvent
   ↓
3. Evento vai para exchange: order.exchange
   ↓
4. RabbitMQ roteia para fila: order-placed
   ↓
5. PaymentsAPI (RabbitListenerService) consome a mensagem
   ↓
6. PaymentService valida e processa o pagamento
   ├─→ Cartão válido? → Aprovado
   ├─→ PIX/Boleto > R$500? → Rejeitado
   └─→ Outra situação? → Aprovado
   ↓
7. PublishPaymentProcessed() publica PaymentProcessedEvent
   ↓
8. Evento vai para exchange: payment.exchange (Fanout)
   ↓
9. FCGCatalog consome → Atualiza status do pedido
   FCGNotification consome → Envia email de confirmação
   ↓
10. Fluxo concluído com sucesso
```

---

## Testando a Aplicação

### 1. Acessar o RabbitMQ Management UI

- URL: http://localhost:15672
- Usuário: `guest`
- Senha: `guest`

**O que fazer**:
- Monitorar filas em tempo real
- Ver mensagens na fila
- Acompanhar o processamento

---

### 2. Testar com o Swagger

1. Acesse http://localhost:8090/swagger
2. Explore os endpoints disponíveis (se houver)

---

### 4. Monitorar Logs

```bash
# Se executando com Docker Compose
docker-compose logs -f paymentsapi

# Se executando localmente
dotnet run --project PaymentsAPI

```
---

## Relacionamento com Outros Microsserviços

### FCGCatalog
- **Produz**: `OrderPlacedEvent`
- **Consome**: `PaymentProcessedEvent`
- **Uso**: Atualiza status de pedidos baseado em resultado de pagamento

### FCGNotification
- **Consome**: `PaymentProcessedEvent`
- **Uso**: Envia email de confirmação quando pagamento é aprovado

### FCGUser
- **Integração**: Indireta (via eventos)


---

**Última atualização**: 2026-07-12  
**Versão**: 1.0.0  
