#!/usr/bin/env bash
# Roda a mesma bateria do CI, na sua maquina.
#
#   ./tools/verificar.sh              # tudo
#   ./tools/verificar.sh --rapido     # pula o build das imagens Docker
#
# Existe porque o CI e o unico lugar onde essas checagens rodavam juntas, e
# depender so dele significa descobrir a quebra depois do push. Aqui o resultado
# e o mesmo e sai antes do commit.
#
# O SDK do .NET nao precisa estar instalado: sem ele, os testes rodam no
# contêiner oficial, como o README ja documenta para quem so tem Docker.
set -uo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$RAIZ"

RAPIDO=0
[[ "${1:-}" == "--rapido" ]] && RAPIDO=1

VERDE=$'\033[32m'; VERMELHO=$'\033[31m'; CINZA=$'\033[90m'; NORMAL=$'\033[0m'
FALHAS=0
RESUMO=()

etapa() {
  local nome="$1"; shift
  printf '\n%s──%s %s\n' "$CINZA" "$NORMAL" "$nome"

  local saida
  if saida=$("$@" 2>&1); then
    printf '   %s✓%s %s\n' "$VERDE" "$NORMAL" "$nome"
    RESUMO+=("ok|$nome")
  else
    printf '   %s✗%s %s\n' "$VERMELHO" "$NORMAL" "$nome"
    # So o rabo da saida: o log inteiro de um restore afogaria o erro de verdade.
    printf '%s\n' "$saida" | tail -25 | sed 's/^/     /'
    RESUMO+=("falhou|$nome")
    FALHAS=$((FALHAS + 1))
  fi
}

testes_backend() {
  if command -v dotnet >/dev/null 2>&1; then
    dotnet test SabemiPagamentos.sln --configuration Release --nologo -v q
  else
    MSYS_NO_PATHCONV=1 docker run --rm -v "$RAIZ:/src" -w /src \
      mcr.microsoft.com/dotnet/sdk:8.0 \
      dotnet test SabemiPagamentos.sln --configuration Release --nologo -v q
  fi
}

no_painel() { (cd "$RAIZ/web" && "$@"); }

etapa "backend · 131 testes"        testes_backend
etapa "painel · checagem de tipos"  no_painel npm run lint
etapa "painel · 146 testes"         no_painel npm test
etapa "painel · build de producao"  no_painel npm run build

if [[ $RAPIDO -eq 0 ]]; then
  etapa "imagem Docker da API"     docker build -q -t sabemi-api:verificacao .
  etapa "imagem Docker do painel"  docker build -q -t sabemi-painel:verificacao ./web
else
  printf '\n%s──%s imagens Docker puladas (--rapido)\n' "$CINZA" "$NORMAL"
fi

printf '\n%s─────────────────────────%s\n' "$CINZA" "$NORMAL"
for linha in "${RESUMO[@]}"; do
  if [[ "${linha%%|*}" == "ok" ]]; then
    printf ' %s✓%s %s\n' "$VERDE" "$NORMAL" "${linha#*|}"
  else
    printf ' %s✗%s %s\n' "$VERMELHO" "$NORMAL" "${linha#*|}"
  fi
done

if [[ $FALHAS -eq 0 ]]; then
  printf '\n%stodas as verificacoes passaram%s\n' "$VERDE" "$NORMAL"
else
  printf '\n%s%d verificacao(oes) falharam%s\n' "$VERMELHO" "$FALHAS" "$NORMAL"
fi

exit $FALHAS
