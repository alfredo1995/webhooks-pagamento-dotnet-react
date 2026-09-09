<img width="1568" height="669" alt="sabemi-webhooks-demo" src="https://github.com/user-attachments/assets/e147ed8f-e4f8-4293-9f58-c398cdc8d40b" />


# Webhooks de Pagamento — .NET 8 + React

Serviço que recebe notificações de pagamento de um banco parceiro, garante que a
mesma transação nunca seja processada duas vezes, executa a regra de negócio fora
do ciclo da requisição e expõe tudo em um painel administrativo autenticado.

Desafio técnico. O enunciado pedia: endpoint de webhook, autenticação por
header, idempotência, duas tabelas (log de eventos brutos e status do contrato),
processamento pesado em background com resposta rápida, e um dashboard com
filtros por status e por contrato, destacando visualmente os eventos com erro.

O que está aqui vai além disso: entrega confiável com outbox e fila distribuída,
retentativa automática com backoff, dead-letter queue, autenticação e trilha de
auditoria no painel, rate limiting por parceiro, rotação de segredo e
rastreamento distribuído da ponta ao fim.

---

## Sumário

- [Como rodar](#como-rodar)
- [Visão geral](#visão-geral)
- [O endpoint de webhook](#o-endpoint-de-webhook)
- [Segurança da borda](#segurança-da-borda)
- [Idempotência](#idempotência)
- [Entrega confiável: outbox, fila e reivindicação](#entrega-confiável-outbox-fila-e-reivindicação)
- [Falha, retentativa e dead-letter queue](#falha-retentativa-e-dead-letter-queue)
- [Acesso ao painel e trilha de auditoria](#acesso-ao-painel-e-trilha-de-auditoria)
- [Observabilidade](#observabilidade)
- [Modelo de dados](#modelo-de-dados)
- [Painel administrativo](#painel-administrativo)
- [API](#api)
- [Testes](#testes)
- [Decisões técnicas](#decisões-técnicas)
- [Estrutura de pastas](#estrutura-de-pastas)
- [O que ficou de fora](#o-que-ficou-de-fora)

---

## Como rodar

### Docker Compose (não exige .NET nem Node instalados)

```bash
cp .env.example .env    # senhas e segredos; fica fora do git
docker compose up --build
```

Sobe SQL Server, RabbitMQ, Jaeger, API e painel. As migrations são aplicadas no
startup.

**Nenhum segredo está em arquivo versionado** — nem no `docker-compose.yml`, nem
no `appsettings.json`, que declara as chaves sem valor de propósito. Todas vêm do
`.env`, e as obrigatórias são declaradas com `${VAR:?}`: sem o arquivo, o compose
recusa a subir em vez de seguir com valor em branco. O `.env.example` documenta
cada chave e é o único dos dois que vai para o repositório. Em produção nada
disso vira arquivo: as mesmas variáveis saem de um cofre (Key Vault, Secrets
Manager) injetado no ambiente.

A API também valida a configuração no `ValidateOnStart`. Um segredo faltando
derruba a subida dizendo **qual chave** falta e **onde defini-la**, em vez de
deixar a aplicação de pé para falhar depois, longe da causa — o login estourando
ao assinar com chave vazia, ou o parceiro tomando `401` sem motivo aparente.

| Variável | O que alimenta |
| --- | --- |
| `MSSQL_SA_PASSWORD` | senha do `sa` e a connection string da API |
| `RABBITMQ_USER` / `RABBITMQ_PASS` | broker e o consumidor da API |
| `JWT_CHAVE_ASSINATURA` | assinatura do token do painel (mínimo 32 caracteres) |
| `PAINEL_ADMIN_*` / `PAINEL_OPERADOR_*` | login e senha dos dois papéis |
| `WEBHOOK_API_KEY` / `WEBHOOK_SEGREDO` | credenciais do banco parceiro |

| O quê | Onde | Acesso |
| --- | --- | --- |
| **Painel** | <http://localhost:3000> | `PAINEL_ADMIN_LOGIN` / `PAINEL_ADMIN_SENHA` (ou o par `PAINEL_OPERADOR_*`) |
| Swagger | <http://localhost:5090/swagger> | — |
| Health check | <http://localhost:5090/health> | — |
| Traces (Jaeger) | <http://localhost:16686> | — |
| RabbitMQ | <http://localhost:15672> | `RABBITMQ_USER` / `RABBITMQ_PASS` |
| SQL Server | `localhost,1434` | `sa` / `MSSQL_SA_PASSWORD` |

Os dois usuários existem para mostrar a diferença de papel: **operador** consulta;
**administrador** também reprocessa a dead-letter queue e lê a trilha de auditoria.

### Enviar webhooks

O corpo precisa ir assinado, então há scripts prontos que calculam o HMAC:

```bash
# um evento
./tools/enviar-webhook.sh TX-001 CT-1000 250.00 CONFIRMADO

# cenário completo: pagamentos, reenvio duplicado, payload inválido,
# estorno acima do saldo (que vira retentativa e depois DLQ) e requisição sem ApiKey
./tools/simular-fluxo.sh
```

No Windows:

```powershell
./tools/enviar-webhook.ps1 -IdTransacao TX-001 -IdContrato CT-1000 -Valor 250.00
```

Os três scripts leem `WEBHOOK_API_KEY` e `WEBHOOK_SEGREDO` do mesmo `.env` que
alimenta o compose. É o que faz a troca de segredo ser um lugar só: com a chave
embutida no script, girar o segredo devolveria `401` sem nenhuma pista do motivo.

### Limpar a base

Depois de uma bateria de testes manuais o painel fica cheio de transação de
ensaio. Para devolvê-lo a um estado apresentável, sem derrubar o banco nem
reaplicar migrations:

```bash
./tools/limpar-base.sh              # zera tudo
./tools/limpar-base.sh --erros      # só os eventos com erro e a dead-letter queue
```

No Windows, `./tools/limpar-base.ps1` e `./tools/limpar-base.ps1 -SomenteErros`.

O modo `--erros` não toca no status dos contratos de propósito: falha de
validação nunca chegou a mover saldo, e falha de processamento foi barrada antes
de aplicar — zerar o consolidado ali inventaria uma correção que não aconteceu.

### Desenvolvimento local

Fora do compose ninguém injeta as variáveis, então os segredos entram pelo
*user-secrets* do .NET — que grava fora da árvore do projeto e por isso não tem
como vazar em commit. Um script carrega os valores do `.env`:

```bash
./tools/configurar-segredos-locais.sh    # ou .ps1 no Windows

docker compose up -d sqlserver           # só o banco
dotnet run --project src/Sabemi.Pagamentos.Api

cd web && npm install && npm run dev     # painel em http://localhost:5173
```

Sem RabbitMQ no ar, o padrão do `appsettings.json` é a fila em memória
(`Processamento:Fila = Memoria`), então a aplicação sobe sozinha com o banco.

O Vite faz proxy de `/api` para a API, o mesmo papel que o nginx cumpre em
produção — o front usa caminhos relativos nos dois ambientes.

### Testes e verificação

```bash
dotnet test              # backend
cd web && npm test       # painel
```

Sem o SDK do .NET instalado:

```bash
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test SabemiPagamentos.sln
```

Para rodar a bateria inteira de uma vez — as mesmas seis etapas do arquivo de
CI, na mesma configuração `Release`:

```bash
./tools/verificar.sh              # ou ./tools/verificar.ps1 no Windows
./tools/verificar.sh --rapido     # pula o build das imagens Docker
```

O script detecta se o SDK do .NET está instalado e, quando não está, roda os
testes no contêiner oficial. Termina com código de saída diferente de zero se
qualquer etapa falhar, então serve como *pre-push hook*.

> **Sobre o CI.** O workflow em `.github/workflows/ci.yml` está configurado como
> `workflow_dispatch` — sob demanda, e não a cada push. A conta que hospeda este
> repositório está com os GitHub Actions bloqueados por uma pendência de
> cobrança, e um job que nunca chega a receber runner marcaria todo commit com um
> ✗ que descreve a conta, não o código. O `verificar.sh` executa exatamente as
> mesmas etapas. Para voltar ao gatilho automático, basta devolver `push` e
> `pull_request` ao `on:` do workflow.

---

## Visão geral

```
   banco parceiro
        │  POST /webhooks/pagamento  (X-Api-Key + X-Signature)
        ▼
┌───────────────────┐
│ rate limit        │  balde por parceiro, antes de qualquer trabalho caro
└─────────┬─────────┘
          ▼
┌───────────────────┐
│ autenticação      │  ApiKey + HMAC do corpo bruto, com janela de rotação
└─────────┬─────────┘
          ▼
┌───────────────────────────────────┐
│ uma transação                     │   responde 202 em milissegundos
│  ├─ log de eventos (idempotência) │  ────────────────────────────────▶ banco parceiro
│  └─ mensagem de outbox (+trace)   │
└─────────┬─────────────────────────┘
          ▼
┌───────────────────┐      ┌──────────────────┐
│ publicador outbox │ ───▶ │ fila             │  RabbitMQ ou Channel
└───────────────────┘      └────────┬─────────┘
                                    ▼
                       ┌───────────────────────┐
                       │ worker                │  reivindica, aplica a regra (2s)
                       └───────────┬───────────┘
                    falhou?        │        ok
              ┌────────────────────┴─────────────┐
              ▼                                  ▼
   ┌─────────────────────┐            ┌───────────────────┐
   │ backoff exponencial │            │ status do contrato│
   │ ou dead-letter queue│            └─────────┬─────────┘
   └──────────┬──────────┘                      │
              │        ┌──────────────────┐     │
              └──────▶ │ painel React     │ ◀───┘  JWT + trilha de auditoria
                       └──────────────────┘
```

Quatro projetos, com dependências apontando sempre para dentro:

| Camada | Responsabilidade |
| --- | --- |
| `Domain` | Entidades, regras e as interfaces de repositório/fila. Zero dependência de framework |
| `Application` | Casos de uso: recebimento, processamento, retentativa, reprocessamento e consultas |
| `Infrastructure` | EF Core + SQL Server, outbox, adapters de fila (Channel e RabbitMQ), workers |
| `Api` | HTTP: rate limit, autenticação, autorização, auditoria, controllers, ProblemDetails |

---

## O endpoint de webhook

```http
POST /webhooks/pagamento
X-Api-Key: <WEBHOOK_API_KEY do .env>
X-Signature: sha256=<hmac do corpo, com WEBHOOK_SEGREDO>
Content-Type: application/json

{
  "id_transacao": "TX-001",
  "id_contrato": "CT-2026-0001",
  "valor": 1250.90,
  "data_pagamento": "2026-03-01T12:00:00Z",
  "status": "CONFIRMADO"
}
```

| Situação | HTTP | O que acontece |
| --- | --- | --- |
| Payload válido | `202 Accepted` | Gravado e anunciado no outbox; processa em background |
| `id_transacao` repetido | `200 OK` | Nada é reprocessado; responde `resultado: "Duplicado"` |
| Payload inválido | `400 Bad Request` | **Gravado no log** e exibido no painel como erro |
| ApiKey ou assinatura inválida | `401 Unauthorized` | Recusado sem gravar |
| Acima do teto do parceiro | `429 Too Many Requests` | Recusado com `Retry-After` |

O campo `status` aceita as variações que aparecem na prática — `CONFIRMADO`,
`PAGO`, `liquidado`, `PENDENTE`, `FALHA`, `ESTORNADO`, `chargeback` — e `valor`
pode vir como número ou string (`"125.90"`), porque contrato de terceiro raramente
é tão uniforme quanto a documentação promete.

**Payload inválido é gravado, não descartado.** O requisito de mostrar erros de
validação no painel só se sustenta se o evento existir em algum lugar. Um `400`
que não deixa rastro tornaria a tela de erros permanentemente vazia.

---

## Segurança da borda

### Duas verificações independentes

- **`X-Api-Key`** identifica *quem* está chamando.
- **`X-Signature`** (`sha256=<hex>`, HMAC-SHA256 do corpo com o segredo
  compartilhado) prova que o corpo não foi adulterado e que o chamador conhece o
  segredo.

Só a ApiKey seria fraco: ela viaja inteira em todo request e vaza em qualquer log
de header mal configurado. A assinatura é calculada sobre os **bytes exatos**
recebidos — por isso o corpo é lido cru e preservado antes de qualquer model
binding; reserializar o objeto mudaria o hash por espaçamento ou ordem de chaves.

As comparações de ApiKey e de assinatura usam `CryptographicOperations.FixedTimeEquals`,
e a busca do parceiro percorre a lista inteira mesmo depois de encontrar — sem
isso, o tempo de resposta revelaria informação sobre a chave.

Requisição não autenticada **não entra no log de eventos**: o log existe para
auditar o parceiro, e aceitar corpo de origem desconhecida transformaria a tabela
em vetor de inundação.

### Rotação de segredo com janela de duas chaves

Rotacionar um segredo compartilhado tem um problema de ordem: o parceiro não troca
a chave no mesmo instante em que nós trocamos. Se só a chave nova valesse, toda
rotação exigiria uma janela combinada de indisponibilidade — e na prática isso
vira "ninguém rotaciona".

```json
"Parceiros": [{
  "ApiKey": "chave-nova",          "Segredo": "segredo-novo",
  "ApiKeyAnterior": "chave-velha", "SegredoAnterior": "segredo-velho",
  "ChavesAnterioresValidasAte": "2026-03-15T00:00:00Z"
}]
```

Durante a janela as duas gerações são aceitas, **e de forma independente**: o
parceiro pode ter trocado a ApiKey e ainda não o segredo. Toda requisição aceita
pela chave antiga sai no log como aviso, então dá para saber se o parceiro já
migrou antes de a janela fechar. Sem a data, "aceitar as duas" viraria permanente
e o ganho da rotação se perderia — por isso ela é obrigatória para a chave
anterior valer.

### Rate limiting por parceiro

A partição é a **ApiKey recebida**, lida do header antes de qualquer validação.
Limitar por IP não serviria: parceiro grande sai de vários endereços, e um único
NAT juntaria parceiros diferentes no mesmo balde. Limitar depois de autenticar
também não — a conferência do HMAC é justamente o trabalho que uma rajada tornaria
caro, então o limite tem que vir antes dela.

O algoritmo é **token bucket**: o parceiro pode gastar o balde de uma vez numa
rajada legítima — reentrega acumulada depois de uma instabilidade, por exemplo —
e depois volta ao ritmo de reposição. Uma janela fixa recusaria essa rajada mesmo
com a média dentro do limite.

Chave desconhecida cai em uma partição única e apertada (10/min). Assim uma
varredura com chaves aleatórias não ganha um balde novo a cada tentativa, que é
como um limitador por chave se torna inútil contra quem está atacando.

O login do painel tem política própria, por IP e com janela fixa: ali não existe
rajada legítima a preservar.

---

## Idempotência

Duas camadas, e a segunda é a que realmente garante:

1. **Consulta prévia** por `id_transacao` resolve o caso comum — o banco
   reenviando após um timeout — sem custo de exceção.
2. **Índice único** em `id_transacao` resolve o caso real de corrida: duas
   entregas simultâneas passam pela consulta antes de qualquer uma gravar. Quem
   perde a corrida recebe a violação de constraint, que a infraestrutura traduz
   em `TransacaoDuplicadaException` e o caso de uso trata como duplicidade.

Sem o índice, a checagem sozinha seria apenas uma otimização com aparência de
garantia. Há um teste de integração que dispara **seis requisições simultâneas**
da mesma transação e verifica que exatamente uma retorna `202`, cinco retornam
`200`, e o contrato é creditado uma única vez.

A tradução do erro de constraint vive na infraestrutura porque o formato depende
do provider — SQL Server devolve os códigos 2601/2627, SQLite devolve
`UNIQUE constraint failed`. Deixar isso vazar amarraria a regra de idempotência a
um banco específico.

---

## Entrega confiável: outbox, fila e reivindicação

### O outbox existe por causa de uma fresta

Publicar na fila dentro do recebimento criaria duas escritas sem transação comum.
Se o commit no banco falhasse depois do publish, o worker processaria um evento
que não existe; se o publish falhasse depois do commit, o evento ficaria gravado e
nunca processado.

Por isso o evento e a mensagem vão no **mesmo `SaveChanges`**, ou seja, na mesma
transação. Só existe um desfecho: os dois ou nenhum. Um publicador em background
leva para a fila o que ficou pendente e marca a mensagem como publicada **depois**
de o broker aceitar.

A entrega é *pelo menos uma vez*, nunca *exatamente uma vez*. Se o broker aceitar
e o processo cair antes de marcar a mensagem, ela será publicada de novo.
Perseguir exatamente-uma-vez aqui seria caro e inútil — quem resolve a duplicidade
é a reivindicação, que já precisa existir por causa da reentrega do próprio broker.

### A reivindicação é um UPDATE condicional

```sql
UPDATE EventosWebhook
   SET Status = Processando, Tentativas = Tentativas + 1, ReivindicadoEmUtc = @agora
 WHERE Id = @id
   AND (Status = Recebido
     OR (Status = AguardandoRetentativa AND ProximaTentativaEmUtc <= @agora)
     OR (Status = Processando           AND ReivindicadoEmUtc   <= @agora - @lease))
```

Zero linhas afetadas significa "outro worker chegou primeiro, ou não é hora": a
entrega é descartada. Ler o status em memória, decidir e depois gravar deixaria uma
janela entre decidir e reivindicar — a mesma armadilha da checagem de duplicidade
sem índice único.

A regra mora no domínio (`EventoWebhook.Reivindicar`); o repositório a espelha
como operação atômica. O terceiro caso da condição é o **lease**: um evento que
ficou em `Processando` além do prazo volta a ser reivindicável, senão a queda de
uma instância o prenderia para sempre.

### Fila em memória ou RabbitMQ, sem tocar em caso de uso

`Processamento:Fila` escolhe o adapter:

| Valor | Quando | Como funciona |
| --- | --- | --- |
| `Memoria` | uma instância; padrão local | `BoundedChannel` com `FullMode.Wait` — contrapressão honesta, não um buffer infinito |
| `RabbitMq` | mais de uma instância | fila durable, mensagem persistente, `prefetch` limitado, ack manual |

A camada de aplicação só conhece `IFilaProcessamento`. Trocar por Service Bus ou
Kafka é escrever um adapter novo — nenhum caso de uso muda.

No consumidor RabbitMQ **o ack acontece sempre**, inclusive quando o processamento
quebra. Parece contraintuitivo, mas o estado do evento vive no banco, não na
mensagem: falha de negócio já virou retentativa agendada, e falha de infraestrutura
deixa o evento com o lease vencido, que o supervisor devolve para a fila. Usar
`nack` com requeue traria a mesma mensagem de volta em milissegundos, num laço
quente que ignoraria o backoff que existe justamente para evitar isso.

### O supervisor de reentrega

Um `BackgroundService` varre periodicamente três situações que, do ponto de vista
do evento, dizem a mesma coisa — *isto deveria estar acontecendo e não está*:

- retentativa cujo horário chegou;
- evento com lease de processamento vencido (a instância que o pegou caiu);
- recebimento que nunca foi consumido.

Ele publica direto na fila, sem passar pelo outbox: aqui não há escrita nova a
casar com a publicação, e o próprio supervisor já é a rede de segurança — uma
mensagem perdida volta a ser elegível no ciclo seguinte. No pior caso um evento é
anunciado duas vezes, e a reivindicação descarta a segunda.

---

## Falha, retentativa e dead-letter queue

Os dois tipos de erro continuam separados de propósito:

- **`Invalido`** — o payload é ruim. Reprocessar daria exatamente o mesmo erro,
  então esse evento nunca entra na fila e não é reprocessável.
- **`Falha`** — a execução quebrou. Isso *é* candidato a retentativa.

O ciclo de uma falha de execução:

```
Processando ──falhou──▶ AguardandoRetentativa ──horário chegou──▶ Processando
                              │                                        │
                              │ orçamento esgotado                     │ ok
                              ▼                                        ▼
                        Falha + carta na DLQ                     Processado
```

**Backoff exponencial com teto e jitter.** Com base 5 s e fator 2, os intervalos
são 5, 10, 20, 40 e 80 segundos — tempo suficiente para uma dependência reiniciar,
sem transformar erro permanente em espera de horas. O teto existe porque, passado
certo ponto, insistir mais devagar não aumenta a chance de sucesso: só atrasa o
diagnóstico.

O **jitter é derivado do próprio identificador do evento**, e não de um gerador
aleatório. Mil eventos que falharam juntos ainda se espalham na janela — que é o
objetivo —, e o intervalo continua reproduzível: o mesmo evento sempre recebe o
mesmo agendamento, o que torna o comportamento testável e o diagnóstico possível.

**A dead-letter queue é uma tabela, não uma fila do broker.** Dois motivos.
Primeiro, ela precisa sobreviver à troca de broker — hoje RabbitMQ, amanhã Service
Bus — e a verdade sobre "o que falhou de vez" não pode morar em infraestrutura
substituível. Segundo, o valor de uma DLQ está em ser consultável: o operador
filtra, lê o motivo e reprocessa pelo painel, coisa que uma fila de mensagens não
oferece.

Reprocessar é ação de **administrador** e passa pelo mesmo outbox do recebimento —
se a mensagem fosse direto para o broker, fora da transação, um erro no commit
deixaria a carta marcada como reprocessada e o evento parado. O contador de
tentativas volta a zero de propósito: quem reprocessa à mão já corrigiu a causa e
espera um orçamento novo. O histórico da rodada anterior não se perde, porque a
carta não é apagada — ela é marcada como reprocessada, com quem mandou.

**Evento em retentativa conta como pendente, não como erro.** Ele ainda pode
terminar bem, e classificá-lo como erro encheria o alerta do painel de falhas que
o próprio sistema está resolvendo sozinho. O painel tem cartões separados para
"Em retentativa" e "Na dead-letter": o primeiro é o que a máquina ainda tenta, o
segundo é o que espera uma pessoa.

---

## Acesso ao painel e trilha de auditoria

### Autenticação

O painel exige token. Há dois modos, e a configuração escolhe entre eles:

| Configuração | Comportamento |
| --- | --- |
| `Autenticacao:Authority` preenchido | A API só **valida** tokens do IdP corporativo. É o modo de produção: SSO de verdade, nenhuma senha do nosso lado, e `POST /api/auth/login` responde `501` |
| `Authority` vazio | A API emite tokens HS256 a partir da lista local de usuários. Existe para que o painel seja demonstrável sem um Keycloak no ar |

Dois papéis: **operador** consulta; **administrador** também reprocessa a DLQ e lê
a trilha de auditoria. Ler a trilha é restrito porque ela guarda os filtros usados
nas consultas — saber o que foi investigado é, em si, informação sensível.

A comparação de login e senha percorre a lista inteira e usa `FixedTimeEquals`,
pelo mesmo motivo da ApiKey: sair mais cedo quando o login não existe
transformaria o tempo de resposta em um oráculo de quais usuários são válidos.

No navegador o token fica em `sessionStorage`, não em `localStorage`: ele morre com
a aba. Um painel operacional é usado em turno, muitas vezes em máquina
compartilhada, e não há ganho em manter credencial válida depois que a pessoa
fechou a janela.

### Trilha de auditoria

Autenticar diz quem pode entrar; a trilha diz o que a pessoa olhou depois de
entrar — e só a segunda responde à pergunta que aparece quando um dado vaza.

Cada requisição a `/api` grava usuário, papel, método, rota, **query string**, IP,
status HTTP e o `traceId` do OpenTelemetry. A query string não é detalhe:
"consultou eventos" não diferencia quem abriu a lista de quem varreu o contrato de
um cliente específico.

Duas decisões de implementação:

- **`/api/metricas` fica de fora** (configurável em `Auditoria:RotasIgnoradas`).
  Não é por volume: a trilha existe para responder "quem viu o dado de quem", e um
  endpoint que só devolve contagens agregadas não expõe dado de ninguém. Registrar
  o polling do painel a cada três segundos afogaria as consultas que importam.
- **A gravação usa um escopo de DI próprio.** Usar o `DbContext` da requisição
  faria a trilha compartilhar unidade de trabalho com o caso de uso, e um
  `SaveChanges` da auditoria poderia arrastar junto alterações que o caso de uso
  ainda não quis gravar.

Falha ao auditar não derruba a requisição — ela já terminou —, mas sai no log como
erro. Auditoria silenciosamente quebrada é pior do que auditoria ausente: cria a
impressão de que existe registro.

---

## Observabilidade

Traces e métricas em OpenTelemetry, exportados por OTLP para o Jaeger do compose
(<http://localhost:16686>).

**O trace não termina no `202`.** O `traceparent` da requisição HTTP é gravado na
mensagem de outbox, viaja nos headers da mensagem no RabbitMQ e é restaurado como
pai da atividade de processamento. Abrir um trace no Jaeger mostra, em uma linha
do tempo só: a chamada do banco parceiro, a publicação, o consumo e o
processamento em background — incluindo as retentativas e a ida para a DLQ.

Sem esse campo, o trabalho em background apareceria como um trace solto, sem
ligação com a chamada que o provocou, que é o modo mais comum de instrumentar
processamento assíncrono pela metade.

A instrumentação mora na **camada de aplicação**, não na borda HTTP. Instrumentar
só o controller mostraria apenas um `202` rápido e esconderia todo o trabalho que
acontece depois da resposta, que é exatamente onde as coisas quebram.
`ActivitySource` e `Meter` são BCL: a aplicação não referencia OpenTelemetry, e
quem escolhe o exportador é a composição na API.

Métricas publicadas:

| Métrica | O que conta |
| --- | --- |
| `sabemi.eventos.recebidos` | webhooks aceitos, duplicados e inválidos (por parceiro e resultado) |
| `sabemi.eventos.processados` | eventos concluídos com sucesso |
| `sabemi.eventos.retentados` | falhas que geraram nova tentativa agendada |
| `sabemi.eventos.dead_letter` | eventos que esgotaram o orçamento |
| `sabemi.outbox.publicadas` | mensagens entregues ao broker |
| `sabemi.eventos.duracao` | histograma do tempo da regra de negócio |

Com `Observabilidade:EndpointOtlp` vazio a aplicação continua instrumentada, mas
não exporta — é o padrão de desenvolvimento local.

---

## Modelo de dados

**`EventosWebhook`** — o log de eventos brutos.

Guarda o payload original inteiro e nunca o reescreve. Os campos extraídos
(`IdContrato`, `Valor`, `DataPagamento`, `StatusPagamento`) são apenas uma
projeção para consulta; se amanhã a interpretação mudar, ou se um evento precisar
ser auditado, a verdade continua no `PayloadBruto`.

Estados: `Recebido` → `Processando` → `Processado`, com `Invalido` (reprovado na
validação), `AguardandoRetentativa` (falhou, tem nova tentativa agendada) e
`Falha` (esgotou as tentativas) como os outros desfechos. `ReivindicadoEmUtc`
carrega o lease e `ProximaTentativaEmUtc`, o agendamento do backoff.

**`MensagensOutbox`** — o anúncio gravado junto do evento, com o `traceparent`.
Traz também a reserva (`ReservaToken` + `ReservadaAteUtc`), que é como dois
publicadores concorrentes nunca pegam a mesma mensagem; a reserva vence sozinha se
quem reservou morrer. Mensagens publicadas são expurgadas depois de um dia — um
outbox sem expurgo vira a maior tabela do banco e degrada justamente a consulta que
roda a cada ciclo.

**`DeadLetters`** — uma carta por rodada de falha esgotada. O índice por evento
**não** é único de propósito: um evento reprocessado à mão que volta a falhar
merece uma carta nova, e não a sobrescrita da anterior.

**`RegistrosAuditoria`** — quem consultou o quê, quando e de onde.

**`StatusContratos`** — o estado consolidado.

| Status recebido | Efeito |
| --- | --- |
| `CONFIRMADO` | Soma ao total pago e conta o pagamento |
| `ESTORNADO` | Abate do saldo; recusado se exceder o saldo líquido |
| `PENDENTE` / `FALHA` | Atualiza só o último status, sem mover valor |

`SaldoLiquido` é derivado (`pago − estornado`) e nunca persistido: guardar o
total criaria uma segunda fonte de verdade que um dia diverge da soma real.

Índices: único em `id_transacao` (a garantia de idempotência) e em `IdContrato` do
status; de apoio em `Status`, `IdContrato` e `RecebidoEmUtc` dos eventos, que são
exatamente os filtros do painel; composto em `(Status, ProximaTentativaEmUtc)` para
a varredura do supervisor e em `(PublicadaEmUtc, ProximaTentativaEmUtc)` para a do
publicador — sem eles, cada ciclo seria um scan de tabela.

---

## Painel administrativo

React 18 + TypeScript + Vite, sem biblioteca de UI.

- **Tela de login** e cabeçalho com usuário e papel. Um `401` em qualquer das
  requisições em polling devolve para o login em um ponto só; sem esse ponto único
  de aviso, cada requisição mostraria seu próprio "não autorizado".
- **Cartões de resumo** — recebidos, processados, com erro, em retentativa, na
  dead-letter, pendentes e quantos ainda estão na fila.
- **Atualização automática** a cada 3 s, com indicador "ao vivo" e botão de pausa.
  A requisição anterior é abortada antes da próxima, para que uma resposta lenta
  não sobrescreva uma recente; o *loading* só aparece na primeira carga, senão a
  tabela piscaria a cada ciclo.
- **Filtros** por resultado (Sucesso / Erro / Pendentes) e por ID de contrato ou
  de transação. Trocar de filtro volta para a página 1 — manter a página 7 quase
  sempre resulta em lista vazia sem explicação.
- **Erros em destaque**: banner fixo no topo com a contagem e atalho "ver apenas
  os erros", linha da tabela em vermelho, badge com ícone, e o motivo da falha
  **na própria linha** — descobrir por que falhou não deveria exigir abrir um
  modal. A DLQ tem banner próprio, porque é a única lista que exige uma pessoa.
- **Aba da dead-letter queue** com motivo, tentativas e botão de reprocessar,
  desabilitado para quem não é administrador.
- **Aba de auditoria**, visível apenas para administrador.
- **Detalhe do evento** com tentativas, próxima tentativa agendada e o payload
  bruto reindentado (e cru, se não for JSON).

O painel não tem um botão "enviar webhook de teste": isso exigiria o segredo do
HMAC no bundle do navegador. Para simular, use os scripts em `tools/`.

---

## API

| Método | Rota | Acesso | Descrição |
| --- | --- | --- | --- |
| `POST` | `/webhooks/pagamento` | ApiKey + HMAC | Recebimento da notificação |
| `POST` | `/api/auth/login` | anônimo | Emite o token do painel |
| `GET` | `/api/auth/eu` | autenticado | Identidade do token em uso |
| `GET` | `/api/eventos` | autenticado | Lista paginada; filtros `resultado`, `idContrato`, `idTransacao` |
| `GET` | `/api/eventos/{id}` | autenticado | Detalhe com o payload bruto |
| `GET` | `/api/contratos` | autenticado | Status consolidado por contrato |
| `GET` | `/api/metricas` | autenticado | Totais por resultado, fila, outbox e DLQ |
| `GET` | `/api/dead-letters` | autenticado | Cartas da DLQ (`apenasPendentes`) |
| `POST` | `/api/dead-letters/{eventoId}/reprocessar` | administrador | Devolve o evento para a fila |
| `GET` | `/api/auditoria` | administrador | Trilha de acessos |
| `GET` | `/health` | anônimo | Health check, incluindo conectividade com o banco |

Paginação com `pagina` e `tamanhoPagina` (padrão 25, teto 200), resolvida no banco
com `Skip`/`Take` + `Count` — nunca em memória.

Erros seguem **ProblemDetails (RFC 7807)**: `ValidationException` → 400,
`NotFoundException` → 404, `DomainException` → 409, rate limit → 429 com
`Retry-After`, resto → 500 com `traceId`.

O webhook responde em snake_case (contrato do banco); a API de consulta responde
em camelCase (contrato nosso). São dois públicos diferentes e não faz sentido
forçar um a usar a convenção do outro.

---

## Testes

**277 testes, todos verdes.**

```
Sabemi.Pagamentos.UnitTests ......... 84
Sabemi.Pagamentos.IntegrationTests .. 47
Painel (Vitest) ..................... 146
```

### Unitários

Regras do domínio (transições de estado do evento, lease, retentativa, carta da
DLQ, acúmulo e estorno no contrato), o serviço de recebimento com repositórios
dublados, o processador, a política de backoff, o cálculo de assinatura e a
validação do payload.

Nos testes de serviço os repositórios são mockados, mas **os validadores usados
são os reais** — mockar validação só esconderia erro de validação. Foi assim que
apareceu um `NullReferenceException` na regra de `data_pagamento` quando o campo
vinha ausente: a validação quebrava exatamente com o payload que deveria reprovar.

O dublê da reivindicação aplica a mesma regra do repositório — só devolve o evento
quando ele está realmente reivindicável. Um dublê que sempre devolvesse o evento
esconderia justamente o caso que o método existe para tratar.

O relógio entra por injeção (`TimeProvider`), porque backoff e janela de rotação
são regras *sobre o tempo*: testá-las com o relógio real significaria ou esperar de
verdade, ou afrouxar a asserção até ela não provar mais nada.

### Integração

Sobem a API completa em memória — pipeline, filtros, autenticação, DI, EF Core,
o outbox e **os workers de background reais** — trocando apenas o provider por
SQLite. Cobrem:

- 401 sem ApiKey, com ApiKey errada e com assinatura que não bate com o corpo;
- 202 no payload válido, com o contrato creditado **depois** do processamento
  assíncrono (o teste espera pelo estado final, não assume que já aconteceu);
- reenvio da mesma transação: 200, um único evento, contrato creditado uma vez;
- **seis entregas simultâneas**: exatamente um 202 e cinco 200;
- payload inválido e JSON quebrado: 400, evento gravado e visível no painel com o
  corpo original preservado;
- **a jornada completa de uma falha**: `AguardandoRetentativa` com horário
  agendado, reentrega pelo supervisor, esgotamento do orçamento, carta na DLQ,
  reprocessamento manual com a causa corrigida e o contrato fechando em zero;
- 401 sem token em todos os endpoints do painel, login válido e inválido, 403 para
  operador nas rotas de administrador;
- a trilha registrando usuário, papel, rota e filtros — e **não** registrando o
  polling de métricas;
- rate limit por parceiro devolvendo 429 com `Retry-After`;
- rotação de segredo: chave nova aceita, chave anterior aceita dentro da janela,
  combinação mista aceita, chave anterior fora da janela recusada e chave de outro
  parceiro recusada;
- filtros, métricas, paginação e 404.

### Painel

Vitest com Testing Library e jsdom, consultando a tela como o operador a usa —
por papel acessível e texto visível, não por classe CSS. A API é dublada na
fronteira do `client`, então o que se verifica é o comportamento, não o
`fetch`.

Os testes de jornada em `App.test.tsx` são o único nível que prova que **o filtro
digitado na tela vira consulta enviada**: os testes de componente sabem que o
callback foi chamado, não que alguém o ligou na requisição. Cobrem login, sessão
reaproveitada, filtro por status, filtro por contrato, alerta de erro com
atalhos, troca de abas, paginação, reprocessamento da DLQ, saída e o operador
que não enxerga a auditoria — nem dispara a consulta dela.

Os demais cobrem o cliente HTTP (token, 401 com e sem sessão, `ProblemDetails`,
montagem da query), o `useAutoRefresh` (intervalo, pausa, aborto da requisição
anterior, erro sem apagar a tela), a sessão em `sessionStorage`, os formatadores
e cada tabela.

Um deles nasceu de um defeito encontrado ao escrevê-los: o `useAutoRefresh`
guardava a função de busca em um `ref` e o efeito não dependia dela, então um
filtro novo só valia no tique seguinte — e com a atualização automática pausada,
**nunca**. A tabela seguia mostrando o resultado do filtro anterior sem nada na
tela indicando isso. O teste que prova o conserto está em
`useAutoRefresh.test.ts`.

---

## Decisões técnicas

**O log bruto é imutável.** Guardar o payload original inteiro custa espaço e
paga em auditoria: qualquer dúvida futura sobre "o que o banco mandou mesmo" tem
resposta exata.

**Validação e regra de negócio são erros diferentes.** `Invalido` é payload ruim
— reprocessar não adianta. `Falha` é execução que quebrou depois de já ter sido
retentada. Colapsar os dois em "erro" perderia a informação que decide o que fazer
a seguir.

**Idempotência é sempre o banco decidindo, nunca a aplicação.** No recebimento é o
índice único; no processamento é o UPDATE condicional. Nos dois casos a versão
"consultar e depois gravar" existe como otimização, não como garantia.

**Identidade: raízes de agregação geram o próprio Id, e só elas.** A aplicação
precisa referenciar o evento antes de salvar. O EF Core decide entre `INSERT` e
`UPDATE` olhando se a chave já está preenchida — por isso entidades filhas
nasceriam transientes, e enquanto transientes a igualdade cai para referência.

**Uma exceção, um status HTTP.** Em vez de espalhar `if` de status code pelos
controllers, o tipo da exceção define a resposta em um único middleware.

**Totais derivados, nunca persistidos.** O custo de calcular é irrelevante frente
ao custo de divergir.

**A infraestrutura é substituível por configuração, não por refatoração.** Fila,
IdP e exportador de telemetria são escolhas de composição na borda. Nenhum caso de
uso sabe se a mensagem foi por `Channel` ou por RabbitMQ, nem se o token veio de
Keycloak ou da própria API.

---

## Estrutura de pastas

```
sabemi-webhooks/
├── src/
│   ├── Sabemi.Pagamentos.Domain/          # Entidades, regras, portas (sem dependências)
│   │   ├── Common/                        # Entity, IUnitOfWork, exceções, PagedResult
│   │   ├── Eventos/                       # EventoWebhook, status, DLQ, repositórios
│   │   ├── Outbox/                        # MensagemOutbox e sua porta
│   │   ├── Auditoria/                     # RegistroAuditoria e sua porta
│   │   └── Contratos/                     # StatusContrato, repositório
│   ├── Sabemi.Pagamentos.Application/
│   │   ├── Webhooks/                      # Recebimento, validação, assinatura HMAC
│   │   ├── Processamento/                 # Porta da fila, processador, política de backoff
│   │   ├── DeadLetters/                   # Reprocessamento manual
│   │   ├── Observabilidade/               # ActivitySource e Meter da aplicação
│   │   └── Consultas/                     # Serviços de leitura do painel
│   ├── Sabemi.Pagamentos.Infrastructure/
│   │   ├── Persistence/                   # DbContext, configurations, repositórios, migrations
│   │   └── Processamento/                 # Channel, worker, publicador do outbox, supervisor
│   │       └── RabbitMq/                  # Conexão, publicador e consumidor
│   └── Sabemi.Pagamentos.Api/
│       ├── Webhooks/                       # Filtro de autenticação e rotação de chaves
│       ├── Seguranca/                      # JWT, papéis e rate limiting
│       ├── Auditoria/                      # Middleware da trilha
│       ├── Observabilidade/                # Composição do OpenTelemetry
│       ├── Controllers/                    # Webhook, painel, DLQ, auditoria, login
│       └── Middleware/                     # ProblemDetails
├── web/                                    # Painel React + TypeScript + Vite + Vitest
├── tests/
├── tools/                                  # Envio assinado, segredos, limpeza da base e verificação
├── .github/workflows/ci.yml
├── .env.example                            # Modelo das senhas e segredos (versionado)
├── .env                                    # Valores reais — fora do git
├── docker-compose.yml
└── Dockerfile
```

---

## O que ficou de fora

Deixados de propósito, porque cada um merece uma decisão de arquitetura própria e
não um `TODO`:

- **Cofre de segredos.** ApiKey, segredo HMAC, senhas e chave de assinatura do JWT
  já não moram em nenhum arquivo versionado: vêm do `.env` no compose ou do
  user-secrets no desenvolvimento local, e a subida falha se algum faltar. Isso
  ainda não é um cofre — falta rotação automatizada, versionamento e trilha de
  quem leu o segredo, que é o que Key Vault ou Secrets Manager entregam. A janela
  de duas chaves já está pronta para essa rotação.
- **Circuit breaker por dependência.** O backoff protege o evento; não protege a
  dependência de continuar recebendo carga enquanto está caindo.
- **Particionamento e retenção do log de eventos.** A tabela cresce para sempre.
  O expurgo existe só para o outbox, onde o dado não tem valor de auditoria.
- **Publisher confirms e quorum queues no RabbitMQ.** Hoje a durabilidade vem do
  outbox; com múltiplos nós de broker, a fila precisaria ser replicada.
- **Alertas.** As métricas existem e são exportadas, mas ninguém é acordado por
  elas: falta a regra de alerta e o destino.
