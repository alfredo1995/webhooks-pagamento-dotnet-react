<#
.SYNOPSIS
    Carrega os valores do .env no user-secrets do .NET, para o `dotnet run` local.

.EXAMPLE
    ./tools/configurar-segredos-locais.ps1

.NOTES
    O appsettings.json declara as chaves sem valor de proposito — nenhum segredo e
    versionado. No compose quem preenche e o ambiente; fora dele, e o user-secrets,
    que grava fora da arvore do projeto e por isso nao tem como vazar em commit.
#>
$ErrorActionPreference = "Stop"

$raiz = Split-Path $PSScriptRoot -Parent
$projeto = Join-Path $raiz "src/Sabemi.Pagamentos.Api"
$caminhoEnv = Join-Path $raiz ".env"

if (-not (Test-Path $caminhoEnv)) {
    throw "Falta o .env. Rode primeiro: Copy-Item .env.example .env"
}

Get-Content $caminhoEnv | ForEach-Object {
    if ($_ -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        Set-Item -Path "env:$($Matches[1])" -Value $Matches[2].Trim('"').Trim()
    }
}

function Assert-Definida([string]$nome) {
    $valor = (Get-Item "env:$nome" -ErrorAction SilentlyContinue).Value
    if (-not $valor) { throw "Defina $nome no .env." }
    return $valor
}

# A porta 1434 e a que o compose publica do SQL Server para a maquina; e o unico
# ponto onde o acesso local difere do de dentro da rede do compose.
$senhaBanco = Assert-Definida "MSSQL_SA_PASSWORD"
$conexao = "Server=localhost,1434;Database=SabemiPagamentos;User Id=sa;Password=$senhaBanco;TrustServerCertificate=True;Encrypt=False"

function Set-Segredo([string]$chave, [string]$valor) {
    dotnet user-secrets --project $projeto set $chave $valor | Out-Null
    Write-Host "   $chave"
}

Write-Host "-> gravando segredos no user-secrets de $projeto"

Set-Segredo "ConnectionStrings:SqlServer"    $conexao
Set-Segredo "Autenticacao:ChaveAssinatura"   (Assert-Definida "JWT_CHAVE_ASSINATURA")
Set-Segredo "Autenticacao:Usuarios:0:Login"  (Assert-Definida "PAINEL_OPERADOR_LOGIN")
Set-Segredo "Autenticacao:Usuarios:0:Senha"  (Assert-Definida "PAINEL_OPERADOR_SENHA")
Set-Segredo "Autenticacao:Usuarios:1:Login"  (Assert-Definida "PAINEL_ADMIN_LOGIN")
Set-Segredo "Autenticacao:Usuarios:1:Senha"  (Assert-Definida "PAINEL_ADMIN_SENHA")
Set-Segredo "Webhook:Parceiros:0:ApiKey"     (Assert-Definida "WEBHOOK_API_KEY")
Set-Segredo "Webhook:Parceiros:0:Segredo"    (Assert-Definida "WEBHOOK_SEGREDO")
Set-Segredo "Processamento:RabbitMq:Usuario" $env:RABBITMQ_USER
Set-Segredo "Processamento:RabbitMq:Senha"   $env:RABBITMQ_PASS

Write-Host ""
Write-Host "Pronto. Agora:"
Write-Host "   docker compose up -d sqlserver"
Write-Host "   dotnet run --project src/Sabemi.Pagamentos.Api"
