#!/usr/bin/env bash
# Esvazia as tabelas de dados, mantendo o schema e as migrations aplicadas.
#
#   ./tools/limpar-base.sh              # limpa tudo
#   ./tools/limpar-base.sh --erros      # limpa so os eventos com erro e a DLQ
#
# Serve para devolver o painel a um estado apresentavel depois de uma bateria de
# testes manuais. Nao ha FK entre as tabelas — o vinculo evento/carta e por Guid,
# nao por constraint —, mas a ordem abaixo segue a dependencia logica mesmo
# assim: apagar o evento antes da carta deixaria a carta apontando para o vazio
# caso a execucao morresse no meio.
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ -f "$RAIZ/.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  . "$RAIZ/.env"
  set +a
fi

CONTAINER=${CONTAINER:-sabemi-sqlserver}
SENHA=${MSSQL_SA_PASSWORD:?defina MSSQL_SA_PASSWORD no .env}
BANCO=${BANCO:-SabemiPagamentos}

if [[ "${1:-}" == "--erros" ]]; then
  # O evento de erro sai junto com a carta e a mensagem de outbox dele. O status
  # do contrato nao e tocado: falha de validacao nunca chegou a mover saldo, e
  # falha de processamento foi barrada antes de aplicar.
  SQL="
    DELETE FROM DeadLetters;
    DELETE o FROM MensagensOutbox o
      JOIN EventosWebhook e ON e.Id = o.EventoId
      WHERE e.Status IN (4, 5, 6);
    DELETE FROM EventosWebhook WHERE Status IN (4, 5, 6);
  "
  ROTULO="eventos com erro, em retentativa e a dead-letter queue"
else
  SQL="
    DELETE FROM DeadLetters;
    DELETE FROM MensagensOutbox;
    DELETE FROM EventosWebhook;
    DELETE FROM StatusContratos;
    DELETE FROM RegistrosAuditoria;
  "
  ROTULO="todos os dados (schema e migrations preservados)"
fi

echo "-> limpando: $ROTULO"

docker exec "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SENHA" -C -d "$BANCO" -b -Q "SET NOCOUNT ON; $SQL" >/dev/null

echo "-> restante por tabela:"
docker exec "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SENHA" -C -d "$BANCO" -h -1 -W -Q "
    SET NOCOUNT ON;
    SELECT 'EventosWebhook     = '+CAST(COUNT(*) AS VARCHAR) FROM EventosWebhook
    UNION ALL SELECT 'DeadLetters        = '+CAST(COUNT(*) AS VARCHAR) FROM DeadLetters
    UNION ALL SELECT 'StatusContratos    = '+CAST(COUNT(*) AS VARCHAR) FROM StatusContratos
    UNION ALL SELECT 'MensagensOutbox    = '+CAST(COUNT(*) AS VARCHAR) FROM MensagensOutbox
    UNION ALL SELECT 'RegistrosAuditoria = '+CAST(COUNT(*) AS VARCHAR) FROM RegistrosAuditoria;"

echo
echo "Para repovoar com um cenario completo: ./tools/simular-fluxo.sh"
