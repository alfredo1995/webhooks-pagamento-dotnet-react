<#
.SYNOPSIS
    Esvazia as tabelas de dados, mantendo o schema e as migrations aplicadas.

.EXAMPLE
    ./tools/limpar-base.ps1              # limpa tudo
    ./tools/limpar-base.ps1 -SomenteErros # limpa so os eventos com erro e a DLQ

.NOTES
    Serve para devolver o painel a um estado apresentavel depois de uma bateria de
    testes manuais. Nao ha FK entre as tabelas — o vinculo evento/carta e por Guid,
    nao por constraint —, mas a ordem abaixo segue a dependencia logica mesmo
    assim: apagar o evento antes da carta deixaria a carta apontando para o vazio
    caso a execucao morresse no meio.
#>
param(
    [switch]$SomenteErros,
    [string]$Container = "sabemi-sqlserver",
    [string]$Banco = "SabemiPagamentos"
)

$ErrorActionPreference = "Stop"

$raiz = Split-Path $PSScriptRoot -Parent
$caminhoEnv = Join-Path $raiz ".env"

if (Test-Path $caminhoEnv) {
    Get-Content $caminhoEnv | ForEach-Object {
        if ($_ -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
            Set-Item -Path "env:$($Matches[1])" -Value $Matches[2].Trim('"').Trim()
        }
    }
}

$senha = $env:MSSQL_SA_PASSWORD
if (-not $senha) { throw "Defina MSSQL_SA_PASSWORD no .env." }

if ($SomenteErros) {
    # O evento de erro sai junto com a carta e a mensagem de outbox dele. O status
    # do contrato nao e tocado: falha de validacao nunca chegou a mover saldo, e
    # falha de processamento foi barrada antes de aplicar.
    $sql = @"
DELETE FROM DeadLetters;
DELETE o FROM MensagensOutbox o JOIN EventosWebhook e ON e.Id = o.EventoId WHERE e.Status IN (4, 5, 6);
DELETE FROM EventosWebhook WHERE Status IN (4, 5, 6);
"@
    $rotulo = "eventos com erro, em retentativa e a dead-letter queue"
}
else {
    $sql = @"
DELETE FROM DeadLetters;
DELETE FROM MensagensOutbox;
DELETE FROM EventosWebhook;
DELETE FROM StatusContratos;
DELETE FROM RegistrosAuditoria;
"@
    $rotulo = "todos os dados (schema e migrations preservados)"
}

Write-Host "-> limpando: $rotulo"

docker exec $Container /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $senha -C -d $Banco -b -Q "SET NOCOUNT ON; $sql" | Out-Null

Write-Host "-> restante por tabela:"

$contagem = @"
SET NOCOUNT ON;
SELECT 'EventosWebhook     = '+CAST(COUNT(*) AS VARCHAR) FROM EventosWebhook
UNION ALL SELECT 'DeadLetters        = '+CAST(COUNT(*) AS VARCHAR) FROM DeadLetters
UNION ALL SELECT 'StatusContratos    = '+CAST(COUNT(*) AS VARCHAR) FROM StatusContratos
UNION ALL SELECT 'MensagensOutbox    = '+CAST(COUNT(*) AS VARCHAR) FROM MensagensOutbox
UNION ALL SELECT 'RegistrosAuditoria = '+CAST(COUNT(*) AS VARCHAR) FROM RegistrosAuditoria;
"@

docker exec $Container /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $senha -C -d $Banco -h -1 -W -Q $contagem

Write-Host ""
Write-Host "Para repovoar: ./tools/enviar-webhook.ps1 (um evento) ou"
Write-Host "               bash tools/simular-fluxo.sh (cenario completo)"
