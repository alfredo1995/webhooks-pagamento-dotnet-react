# Webhooks de Pagamento — .NET 8 + React

Serviço que recebe notificações de pagamento de um banco parceiro, garante que a
mesma transação nunca seja processada duas vezes, executa a regra de negócio fora
do ciclo da requisição e expõe tudo em um painel administrativo.

Desafio técnico. O enunciado pedia: endpoint de webhook, autenticação por
header, idempotência, duas tabelas (log de eventos brutos e status do contrato),
processamento pesado em background com resposta rápida, e um dashboard com
filtros por status e por contrato, destacando visualmente os eventos com erro.

---

## Sumário

- [Como rodar](#como-rodar)
- [Visão geral](#visão-geral)
- [O endpoint de webhook](#o-endpoint-de-webhook)
- [Segurança](#segurança)
- [Idempotência](#idempotência)
- [Processamento em background](#processamento-em-background)
- [Modelo de dados](#modelo-de-dados)
- [Painel administrativo](#painel-administrativo)
- [API de consulta](#api-de-consulta)
- [Testes](#testes)
- [Decisões técnicas](#decisões-técnicas)
- [Estrutura de pastas](#estrutura-de-pastas)
- [O que ficou de fora](#o-que-ficou-de-fora)

---

## Como rodar

### Docker Compose (não exige .NET nem Node instalados)

```bash
docker compose up --build
```

Sobe SQL Server, API e painel. As migrations são aplicadas no startup.

| O quê | Onde |
| --- | --- |
| **Painel** | <http://localhost:3000> |
| Swagger | <http://localhost:5090/swagger> |
| Health check | <http://localhost:5090/health> |
| SQL Server | `localhost,1434` (sa / `Sabemi_dev_123`) |

### Enviar webhooks

O corpo precisa ir assinado, então há scripts prontos que calculam o HMAC:

```bash
# um evento
./tools/enviar-webhook.sh TX-001 CT-1000 250.00 CONFIRMADO

# cenário completo: pagamentos, reenvio duplicado, payload inválido,
# estorno acima do saldo e requisição sem ApiKey
./tools/simular-fluxo.sh
```

No Windows:

```powershell
./tools/enviar-webhook.ps1 -IdTransacao TX-001 -IdContrato CT-1000 -Valor 250.00
```

### Desenvolvimento local

```bash
docker compose up -d sqlserver          # só o banco
dotnet run --project src/Sabemi.Pagamentos.Api

cd web && npm install && npm run dev    # painel em http://localhost:5173
```

O Vite faz proxy de `/api` para a API, o mesmo papel que o nginx cumpre em
produção — o front usa caminhos relativos nos dois ambientes.

### Testes

```bash
dotnet test
```

Sem SDK instalado:

```bash
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test SabemiPagamentos.sln
```

---

## Visão geral

```
   banco parceiro
        │  POST /webhooks/pagamento  (X-Api-Key + X-Signature)
        ▼
┌───────────────────┐
│ autenticação      │  ApiKey + HMAC do corpo bruto
└─────────┬─────────┘
          ▼
┌───────────────────┐
│ log de eventos    │  grava o payload original, válido ou não
│ (idempotência)    │  índice único em id_transacao
└─────────┬─────────┘
          ▼
     ┌────────┐        responde 202 em milissegundos
     │  fila  │  ────────────────────────────────────▶  banco parceiro
     └───┬────┘
         ▼
┌───────────────────┐
│ worker background │  regra "pesada" (2s) + atualização do contrato
└─────────┬─────────┘
          ▼
┌───────────────────┐        ┌──────────────────┐
│ status do contrato│ ◀───── │ painel React     │  filtros, alertas, polling
└───────────────────┘        └──────────────────┘
```

Quatro projetos, com dependências apontando sempre para dentro:

| Camada | Responsabilidade |
| --- | --- |
| `Domain` | Entidades, regras e as interfaces de repositório/fila. Zero dependência de framework |
| `Application` | Casos de uso: recebimento, processamento e consultas |
| `Infrastructure` | EF Core + SQL Server, fila em memória, worker |
| `Api` | HTTP: autenticação do webhook, controllers, ProblemDetails |

---

## O endpoint de webhook

```http
POST /webhooks/pagamento
X-Api-Key: sabemi-dev-api-key
X-Signature: sha256=<hmac do corpo>
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
| Payload válido | `202 Accepted` | Gravado e enfileirado; processa em background |
| `id_transacao` repetido | `200 OK` | Nada é reprocessado; responde `resultado: "Duplicado"` |
| Payload inválido | `400 Bad Request` | **Gravado no log** e exibido no painel como erro |
| ApiKey ou assinatura inválida | `401 Unauthorized` | Recusado sem gravar |

O campo `status` aceita as variações que aparecem na prática — `CONFIRMADO`,
`PAGO`, `liquidado`, `PENDENTE`, `FALHA`, `ESTORNADO`, `chargeback` — e `valor`
pode vir como número ou string (`"125.90"`), porque contrato de terceiro raramente
é tão uniforme quanto a documentação promete.

**Payload inválido é gravado, não descartado.** O requisito de mostrar erros de
validação no painel só se sustenta se o evento existir em algum lugar. Um `400`
que não deixa rastro tornaria a tela de erros permanentemente vazia.

---

## Segurança

Duas verificações independentes, ambas no filtro que roda antes de qualquer
processamento:

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

## Processamento em background

O endpoint responde assim que grava e enfileira. O trabalho pesado — simulado com
`Task.Delay(2s)`, como pede o enunciado — roda em um `BackgroundService`
consumindo um `Channel<Guid>`.

Três decisões que valem explicação:

**A fila carrega só o identificador, nunca o payload.** O dado já está no banco
antes de enfileirar. Isso mantém a fila leve e, principalmente, faz do banco a
fonte da verdade.

**Fila limitada com contrapressão.** `BoundedChannel` com `FullMode.Wait`: sob
rajada, o recebimento espera em vez de estourar a memória. É contrapressão
honesta, não um buffer infinito que adia o problema.

**Recuperação no startup.** Se o processo cair com itens na fila, nada se perde:
os eventos continuam como `Recebido`/`Processando` no banco e são reenfileirados
na próxima subida. É o que fecha o ciclo de durabilidade de uma fila em memória.

Cada evento roda em seu próprio escopo de DI, porque o `DbContext` é *scoped* e
não pode ser compartilhado entre processamentos concorrentes. O processador é
idempotente por construção: evento já concluído é ignorado, o que importa porque
a reentrega acontece de verdade no cenário de restart.

A camada de aplicação só conhece a interface `IFilaProcessamento`. Trocar por
RabbitMQ, Service Bus ou Kafka é escrever um novo adapter — nenhum caso de uso
muda.

---

## Modelo de dados

**`EventosWebhook`** — o log de eventos brutos.

Guarda o payload original inteiro e nunca o reescreve. Os campos extraídos
(`IdContrato`, `Valor`, `DataPagamento`, `StatusPagamento`) são apenas uma
projeção para consulta; se amanhã a interpretação mudar, ou se um evento precisar
ser auditado, a verdade continua no `PayloadBruto`.

Estados: `Recebido` → `Processando` → `Processado`, com `Invalido` (reprovado na
validação, nunca entrou na fila) e `Falha` (quebrou durante o processamento) como
desfechos de erro. Os dois estados de erro existem separados de propósito: o
primeiro é culpa do payload e não adianta reprocessar; o segundo é falha de
execução e é candidato a retentativa.

**`StatusContratos`** — o estado consolidado.

| Status recebido | Efeito |
| --- | --- |
| `CONFIRMADO` | Soma ao total pago e conta o pagamento |
| `ESTORNADO` | Abate do saldo; recusado se exceder o saldo líquido |
| `PENDENTE` / `FALHA` | Atualiza só o último status, sem mover valor |

`SaldoLiquido` é derivado (`pago − estornado`) e nunca persistido: guardar o
total criaria uma segunda fonte de verdade que um dia diverge da soma real.

Índices: único em `id_transacao` (a garantia de idempotência) e em
`IdContrato` do status; de apoio em `Status`, `IdContrato` e `RecebidoEmUtc` dos
eventos, que são exatamente os filtros do painel.

---

## Painel administrativo

React 18 + TypeScript + Vite, sem biblioteca de UI.

- **Cartões de resumo** — recebidos, processados, com erro, pendentes e quantos
  ainda estão na fila.
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
  modal.
- **Detalhe do evento** com o payload bruto reindentado (e cru, se não for JSON).
- **Status por contrato** em aba separada.

O painel não tem um botão "enviar webhook de teste": isso exigiria o segredo do
HMAC no bundle do navegador. Para simular, use os scripts em `tools/`.

---

## API de consulta

| Método | Rota | Descrição |
| --- | --- | --- |
| `GET` | `/api/eventos` | Lista paginada; filtros `resultado`, `idContrato`, `idTransacao` |
| `GET` | `/api/eventos/{id}` | Detalhe com o payload bruto |
| `GET` | `/api/contratos` | Status consolidado por contrato |
| `GET` | `/api/metricas` | Totais por resultado e tamanho da fila |
| `GET` | `/health` | Health check, incluindo conectividade com o banco |

Paginação com `pagina` e `tamanhoPagina` (padrão 25, teto 200), resolvida no banco
com `Skip`/`Take` + `Count` — nunca em memória.

Erros seguem **ProblemDetails (RFC 7807)**: `ValidationException` → 400,
`NotFoundException` → 404, `DomainException` → 409, resto → 500 com `traceId`.

O webhook responde em snake_case (contrato do banco); a API de consulta responde
em camelCase (contrato nosso). São dois públicos diferentes e não faz sentido
forçar um a usar a convenção do outro.

---

## Testes

**88 testes, todos verdes.**

```
Sabemi.Pagamentos.UnitTests ......... 67
Sabemi.Pagamentos.IntegrationTests .. 21
```

### Unitários

Regras do domínio (transições de estado do evento, acúmulo e estorno no
contrato), o serviço de recebimento com repositórios dublados, o processador, o
cálculo de assinatura e a validação do payload.

Nos testes de serviço os repositórios são mockados, mas **os validadores usados
são os reais** — mockar validação só esconderia erro de validação. Foi assim que
apareceu um `NullReferenceException` na regra de `data_pagamento` quando o campo
vinha ausente: a validação quebrava exatamente com o payload que deveria reprovar.

### Integração

Sobem a API completa em memória — pipeline, filtros, DI, EF Core e **o worker de
background real** — trocando apenas o provider por SQLite. Cobrem:

- 401 sem ApiKey, com ApiKey errada e com assinatura que não bate com o corpo;
- 202 no payload válido, com o contrato creditado **depois** do processamento
  assíncrono (o teste espera pelo estado final, não assume que já aconteceu);
- reenvio da mesma transação: 200, um único evento, contrato creditado uma vez;
- **seis entregas simultâneas**: exatamente um 202 e cinco 200;
- payload inválido e JSON quebrado: 400, evento gravado e visível no painel com o
  corpo original preservado;
- estorno acima do saldo virando `Falha` (processamento) e não `Invalido`
  (validação);
- filtros, métricas, paginação e 404.

---

## Decisões técnicas

**O log bruto é imutável.** Guardar o payload original inteiro custa espaço e
paga em auditoria: qualquer dúvida futura sobre "o que o banco mandou mesmo" tem
resposta exata.

**Validação e regra de negócio são erros diferentes.** `Invalido` é payload ruim
— reprocessar não adianta. `Falha` é execução que quebrou — é candidato a
retentativa. Colapsar os dois em "erro" perderia a informação que decide o que
fazer a seguir.

**Identidade: raízes de agregação geram o próprio Id, e só elas.** A aplicação
precisa referenciar o evento antes de salvar. O EF Core decide entre `INSERT` e
`UPDATE` olhando se a chave já está preenchida — por isso entidades filhas
nasceriam transientes, e enquanto transientes a igualdade cai para referência.

**Uma exceção, um status HTTP.** Em vez de espalhar `if` de status code pelos
controllers, o tipo da exceção define a resposta em um único middleware.

**Totais derivados, nunca persistidos.** O custo de calcular é irrelevante frente
ao custo de divergir.

**A API de consulta não exige autenticação.** É um painel interno; em produção
ficaria atrás do IdP corporativo (JWT/SSO). Está fora do escopo do desafio, mas é
uma lacuna consciente, não um esquecimento.

---

## Estrutura de pastas

```
sabemi-webhooks/
├── src/
│   ├── Sabemi.Pagamentos.Domain/          # Entidades, regras, portas (sem dependências)
│   │   ├── Common/                        # Entity, IUnitOfWork, exceções, PagedResult
│   │   ├── Eventos/                       # EventoWebhook, status, repositório
│   │   └── Contratos/                     # StatusContrato, repositório
│   ├── Sabemi.Pagamentos.Application/
│   │   ├── Webhooks/                      # Recebimento, validação, assinatura HMAC
│   │   ├── Processamento/                 # Porta da fila e processador
│   │   └── Consultas/                     # Serviços de leitura do painel
│   ├── Sabemi.Pagamentos.Infrastructure/
│   │   ├── Persistence/                   # DbContext, configurations, repositórios, migrations
│   │   └── Processamento/                 # Fila (Channel) e worker
│   └── Sabemi.Pagamentos.Api/
│       ├── Webhooks/                       # Filtro de autenticação
│       ├── Controllers/                    # Webhook + consultas do painel
│       └── Middleware/                     # ProblemDetails
├── web/                                    # Painel React + TypeScript + Vite
├── tests/
├── tools/                                  # Scripts de envio assinado
├── .github/workflows/ci.yml
├── docker-compose.yml
└── Dockerfile
```

---

## O que ficou de fora

Deixados de propósito, porque cada um merece uma decisão de arquitetura própria e
não um `TODO`:

- **Retentativa automática com backoff** para eventos em `Falha` — hoje eles
  ficam registrados e reprocessáveis, mas o disparo seria manual.
- **Dead-letter queue** depois de N tentativas.
- **Fila distribuída** (RabbitMQ / Service Bus) com padrão *outbox*, para quando
  houver mais de uma instância da API.
- **Autenticação no painel** (JWT/SSO) e trilha de auditoria de quem consultou o quê.
- **Rate limiting** por parceiro no endpoint de webhook.
- **Rotação de segredo** com aceitação de duas chaves durante a janela de troca.
- **Observabilidade** com OpenTelemetry, propagando o trace da requisição até o
  processamento em background.
