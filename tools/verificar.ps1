<#
.SYNOPSIS
    Roda a mesma bateria do CI, na sua maquina.

.EXAMPLE
    ./tools/verificar.ps1              # tudo
    ./tools/verificar.ps1 -Rapido      # pula o build das imagens Docker

.NOTES
    Existe porque o CI e o unico lugar onde essas checagens rodavam juntas, e
    depender so dele significa descobrir a quebra depois do push. Aqui o
    resultado e o mesmo e sai antes do commit.

    O SDK do .NET nao precisa estar instalado: sem ele, os testes rodam no
    contêiner oficial, como o README ja documenta para quem so tem Docker.
#>
param([switch]$Rapido)

$raiz = Split-Path $PSScriptRoot -Parent
Set-Location $raiz

$resumo = [System.Collections.Generic.List[object]]::new()
$falhas = 0

function Invoke-Etapa {
    param([string]$Nome, [scriptblock]$Acao)

    Write-Host ""
    Write-Host "-- $Nome" -ForegroundColor DarkGray

    $saida = & $Acao 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   [ok] $Nome" -ForegroundColor Green
        $script:resumo.Add(@{ ok = $true; nome = $Nome })
    }
    else {
        Write-Host "   [falhou] $Nome" -ForegroundColor Red
        # So o rabo da saida: o log inteiro de um restore afogaria o erro de verdade.
        $saida | Select-Object -Last 25 | ForEach-Object { Write-Host "     $_" }
        $script:resumo.Add(@{ ok = $false; nome = $Nome })
        $script:falhas++
    }
}

$temDotnet = $null -ne (Get-Command dotnet -ErrorAction SilentlyContinue)

Invoke-Etapa "backend - 131 testes" {
    if ($temDotnet) {
        dotnet test SabemiPagamentos.sln --configuration Release --nologo -v q
    }
    else {
        docker run --rm -v "${raiz}:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 `
            dotnet test SabemiPagamentos.sln --configuration Release --nologo -v q
    }
}

Invoke-Etapa "painel - checagem de tipos" { Push-Location web; npm run lint; $c = $LASTEXITCODE; Pop-Location; $global:LASTEXITCODE = $c }
Invoke-Etapa "painel - 146 testes"        { Push-Location web; npm test;     $c = $LASTEXITCODE; Pop-Location; $global:LASTEXITCODE = $c }
Invoke-Etapa "painel - build de producao" { Push-Location web; npm run build; $c = $LASTEXITCODE; Pop-Location; $global:LASTEXITCODE = $c }

if (-not $Rapido) {
    Invoke-Etapa "imagem Docker da API"    { docker build -q -t sabemi-api:verificacao . }
    Invoke-Etapa "imagem Docker do painel" { docker build -q -t sabemi-painel:verificacao ./web }
}
else {
    Write-Host ""
    Write-Host "-- imagens Docker puladas (-Rapido)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "-------------------------" -ForegroundColor DarkGray
foreach ($item in $resumo) {
    if ($item.ok) { Write-Host " [ok] $($item.nome)" -ForegroundColor Green }
    else { Write-Host " [falhou] $($item.nome)" -ForegroundColor Red }
}

Write-Host ""
if ($falhas -eq 0) { Write-Host "todas as verificacoes passaram" -ForegroundColor Green }
else { Write-Host "$falhas verificacao(oes) falharam" -ForegroundColor Red }

exit $falhas
