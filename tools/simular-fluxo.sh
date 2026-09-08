#!/usr/bin/env bash
# Popula o painel com um cenario completo: pagamentos normais, reenvio duplicado,
# payload invalido e um estorno que estoura o saldo. Serve para ver, em uma
# passada, todos os estados que o dashboard precisa distinguir.
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENVIAR="$RAIZ/enviar-webhook.sh"

API=${API:-http://localhost:5090}
export API

echo "=== 1. Tres pagamentos confirmados em contratos diferentes ==="
"$ENVIAR" "TX-$(date +%s)-A" "CT-2026-0001" 1250.90 CONFIRMADO
"$ENVIAR" "TX-$(date +%s)-B" "CT-2026-0001" 480.00 CONFIRMADO
"$ENVIAR" "TX-$(date +%s)-C" "CT-2026-0002" 3200.00 CONFIRMADO

echo
echo "=== 2. Reenvio da mesma transacao (idempotencia) ==="
DUP="TX-DUPLICADA-$(date +%s)"
"$ENVIAR" "$DUP" "CT-2026-0003" 700.00 CONFIRMADO
echo "   reenviando a mesma id_transacao..."
"$ENVIAR" "$DUP" "CT-2026-0003" 700.00 CONFIRMADO

echo
echo "=== 3. Payload invalido (fica visivel no painel como erro) ==="
CORPO='{"id_transacao":"TX-INVALIDA-'"$(date +%s)"'","id_contrato":"","valor":-5,"status":"NAO-EXISTE"}'
ASSINATURA="sha256=$(printf '%s' "$CORPO" | openssl dgst -sha256 -hmac "${SEGREDO:-segredo-hmac-de-desenvolvimento}" -hex | awk '{print $NF}')"
curl -sS -w "   HTTP %{http_code}\n" -X POST "$API/webhooks/pagamento" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ${API_KEY:-sabemi-dev-api-key}" \
  -H "X-Signature: $ASSINATURA" \
  -d "$CORPO"
echo

echo
echo "=== 4. Estorno acima do saldo (falha no processamento, nao na validacao) ==="
"$ENVIAR" "TX-ESTORNO-$(date +%s)" "CT-2026-0002" 99999.00 ESTORNADO

echo
echo "=== 5. Requisicao sem ApiKey (recusada, nao entra no log) ==="
curl -sS -o /dev/null -w "   HTTP %{http_code}\n" -X POST "$API/webhooks/pagamento" \
  -H "Content-Type: application/json" \
  -d '{"id_transacao":"TX-SEM-CHAVE","id_contrato":"CT-X","valor":1,"status":"CONFIRMADO"}'

echo
echo "Pronto. Abra o painel em http://localhost:3000"
