<#
.SYNOPSIS
    Envia uma notificacao de pagamento assinada, como o banco parceiro faria.

.EXAMPLE
    ./tools/enviar-webhook.ps1 -IdTransacao TX-001 -IdContrato CT-1000 -Valor 250.00 -Status CONFIRMADO

.NOTES
    A assinatura HMAC e calculada sobre os bytes exatos do corpo enviado; por isso
    o JSON e montado uma unica vez e reaproveitado no hash e na requisicao.
#>
param(
    [string]$IdTransacao = "TX-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())",
    [string]$IdContrato = "CT-1000",
    [decimal]$Valor = 250.00,
    [ValidateSet("CONFIRMADO", "PENDENTE", "FALHA", "ESTORNADO")]
    [string]$Status = "CONFIRMADO",
    [string]$Api = "http://localhost:5090",
    [string]$ApiKey = "sabemi-dev-api-key",
    [string]$Segredo = "segredo-hmac-de-desenvolvimento"
)

$ErrorActionPreference = "Stop"

$dataPagamento = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
$valorTexto = $Valor.ToString([System.Globalization.CultureInfo]::InvariantCulture)

$corpo = '{"id_transacao":"' + $IdTransacao + '","id_contrato":"' + $IdContrato +
         '","valor":' + $valorTexto + ',"data_pagamento":"' + $dataPagamento +
         '","status":"' + $Status + '"}'

$hmac = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Segredo))
try {
    $hash = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($corpo))
}
finally {
    $hmac.Dispose()
}

$assinatura = "sha256=" + ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()

Write-Host "-> POST $Api/webhooks/pagamento"
Write-Host "   corpo: $corpo"

try {
    $resposta = Invoke-WebRequest -Uri "$Api/webhooks/pagamento" -Method Post `
        -ContentType "application/json" `
        -Headers @{ "X-Api-Key" = $ApiKey; "X-Signature" = $assinatura } `
        -Body $corpo
    Write-Host "   HTTP $($resposta.StatusCode)"
    Write-Host $resposta.Content
}
catch [System.Net.WebException], [Microsoft.PowerShell.Commands.HttpResponseException] {
    # 400 e 401 sao respostas esperadas em cenarios de teste: mostra o corpo.
    $resposta = $_.Exception.Response
    Write-Host "   HTTP $([int]$resposta.StatusCode)"
    $leitor = [IO.StreamReader]::new($resposta.GetResponseStream())
    try { Write-Host $leitor.ReadToEnd() } finally { $leitor.Dispose() }
}
