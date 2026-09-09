#!/usr/bin/env bash
# Carrega os valores do .env no user-secrets do .NET, para o `dotnet run` local.
#
#   ./tools/configurar-segredos-locais.sh
#
# O appsettings.json declara as chaves sem valor de proposito — nenhum segredo e
# versionado. No compose quem preenche e o ambiente; fora dele, e o user-secrets,
# que grava fora da arvore do projeto e por isso nao tem como vazar em commit.
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJETO="$RAIZ/src/Sabemi.Pagamentos.Api"

if [[ ! -f "$RAIZ/.env" ]]; then
  echo "Falta o .env. Rode primeiro: cp .env.example .env" >&2
  exit 1
fi

set -a
# shellcheck disable=SC1091
. "$RAIZ/.env"
set +a

# A porta 1434 e a que o compose publica do SQL Server para a maquina; e o unico
# ponto onde o acesso local difere do de dentro da rede do compose.
CONEXAO="Server=localhost,1434;Database=SabemiPagamentos;User Id=sa;Password=${MSSQL_SA_PASSWORD:?defina no .env};TrustServerCertificate=True;Encrypt=False"

definir() {
  dotnet user-secrets --project "$PROJETO" set "$1" "$2" >/dev/null
  echo "   $1"
}

echo "-> gravando segredos no user-secrets de $PROJETO"

definir "ConnectionStrings:SqlServer"          "$CONEXAO"
definir "Autenticacao:ChaveAssinatura"         "${JWT_CHAVE_ASSINATURA:?defina no .env}"
definir "Autenticacao:Usuarios:0:Login"        "${PAINEL_OPERADOR_LOGIN:?defina no .env}"
definir "Autenticacao:Usuarios:0:Senha"        "${PAINEL_OPERADOR_SENHA:?defina no .env}"
definir "Autenticacao:Usuarios:1:Login"        "${PAINEL_ADMIN_LOGIN:?defina no .env}"
definir "Autenticacao:Usuarios:1:Senha"        "${PAINEL_ADMIN_SENHA:?defina no .env}"
definir "Webhook:Parceiros:0:ApiKey"           "${WEBHOOK_API_KEY:?defina no .env}"
definir "Webhook:Parceiros:0:Segredo"          "${WEBHOOK_SEGREDO:?defina no .env}"
definir "Processamento:RabbitMq:Usuario"       "${RABBITMQ_USER:-sabemi}"
definir "Processamento:RabbitMq:Senha"         "${RABBITMQ_PASS:-}"

echo
echo "Pronto. Agora:"
echo "   docker compose up -d sqlserver"
echo "   dotnet run --project src/Sabemi.Pagamentos.Api"
