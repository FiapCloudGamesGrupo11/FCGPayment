# Resultado persistente e retomada de entregas

`PaymentDeliveryService` grava em disco o resultado da simulacao por `OrderId`
antes de publicar qualquer evento. Registra separadamente o sucesso no RabbitMQ
e no SQS. Quando o pedido volta pela fila, reutiliza o resultado e publica apenas
o destino ainda pendente. O ACK do pedido original acontece apos as duas entregas.

O listener habilita publisher confirms no RabbitMQ e publica com `mandatory=true`.
A confirmacao local do envio e gravada somente depois da confirmacao do broker.
O evento usa o mesmo `PaymentId` nas tentativas. Uma falha de entrega provoca NACK
com requeue apos cinco segundos, evitando um ciclo de repeticao sem espera.

## Armazenamento

- Configuracao: `PaymentDelivery__Directory`.
- Docker integrado e standalone: `/app/payment-data`, em volume nomeado.
- Kubernetes local: mesmo caminho, PVC `payment-data`, uma replica com `Recreate`.
- Execucao direta: `payment-data` na pasta de saida da aplicacao.

O nome do arquivo e um hash SHA-256 do OrderId. O arquivo guarda apenas o resultado
e os estados de entrega, sem numero de cartao, CVV ou validade. Escritas usam arquivo
temporario, flush e substituicao; um bloqueio exclusivo por pedido impede duas
execucoes concorrentes sobre o mesmo diretorio. Um registro danificado causa erro
e exige investigacao, em vez de gerar outro pagamento silenciosamente.

## Limites

Este e um registro persistente de entregas para o simulador local, nao uma Outbox
transacional em banco. A fila original e responsavel por acionar as novas tentativas.
Sem ela nao ha um worker independente que percorra os arquivos pendentes.

A entrega continua sendo pelo menos uma vez: se o processo cair depois que o broker
aceitou a mensagem, mas antes de registrar o sucesso no disco, a mensagem pode ser
reenviada. Os consumidores devem deduplicar pelo PaymentId. Para um gateway real,
use tambem OrderId como chave de idempotencia no provedor: uma queda entre cobrar e
salvar o resultado nao pode ser resolvida apenas por um arquivo local.

Nao remova o volume de pagamentos nem aumente o numero de replicas sem migrar o
registro para armazenamento transacional compartilhado. Remover os volumes com
`docker compose down -v` remove o historico. As mensagens de origem tambem precisam
ser preservadas no RabbitMQ para retomar entregas pendentes.

## Testar

```powershell
dotnet test PaymentsAPI.Tests/PaymentsAPI.Tests.csproj
```

Os testes cobrem indisponibilidade de SQS/RabbitMQ, reinicio do coordenador,
pedido ja concluido, reutilizacao inconsistente de OrderId e registro corrompido.
Para validacao integrada, interrompa o SQS apos o provisionamento, envie uma compra,
restabeleca-o e confira a entrega com o PaymentId original. Prefira parar/iniciar o
container, sem recria-lo, porque o LocalStack deste projeto nao preserva seu estado.
