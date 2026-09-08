#!/usr/bin/env bash
# Envia uma notificacao de pagamento assinada, como o banco parceiro faria.
#
#   ./tools/enviar-webhook.sh TX-001 CT-1000 250.00 CONFIRMADO
#
# A assinatura precisa ser calculada sobre os bytes exatos do corpo, por isso o
# JSON e montado uma vez e reaproveitado no hash e no envio.
set -euo pipefail

API=${API:-http://localhost:5090}
API_KEY=${API_KEY:-sabemi-dev-api-key}
SEGREDO=${SEGREDO:-segredo-hmac-de-desenvolvimento}

ID_TRANSACAO=${1:-TX-$(date +%s)}
ID_CONTRATO=${2:-CT-1000}
VALOR=${3:-250.00}
STATUS=${4:-CONFIRMADO}
DATA_PAGAMENTO=${5:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}

CORPO=$(printf '{"id_transacao":"%s","id_contrato":"%s","valor":%s,"data_pagamento":"%s","status":"%s"}' \
  "$ID_TRANSACAO" "$ID_CONTRATO" "$VALOR" "$DATA_PAGAMENTO" "$STATUS")

ASSINATURA="sha256=$(printf '%s' "$CORPO" | openssl dgst -sha256 -hmac "$SEGREDO" -hex | awk '{print $NF}')"

echo "-> POST $API/webhooks/pagamento"
echo "   corpo: $CORPO"

curl -sS -o /tmp/resposta-webhook.json -w "   HTTP %{http_code}\n" \
  -X POST "$API/webhooks/pagamento" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $API_KEY" \
  -H "X-Signature: $ASSINATURA" \
  -d "$CORPO"

cat /tmp/resposta-webhook.json
echo
