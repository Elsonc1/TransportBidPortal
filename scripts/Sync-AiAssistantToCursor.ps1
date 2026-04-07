#Requires -Version 5.1
<#
.SYNOPSIS
  Copia regras de docs/ai-assistant/rules para .cursor/rules (requerido pelo Cursor IDE).

.DESCRIPTION
  O Cursor so le automaticamente .cursor/rules/*.mdc.
  Edite a fonte em docs/ai-assistant/rules e execute este script antes do commit.

.NOTES
  Execute na raiz do repositorio TransportBidPortal:
    .\scripts\Sync-AiAssistantToCursor.ps1
#>
$ErrorActionPreference = "Stop"
# scripts/ -> raiz do projeto TransportBidPortal
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $root "TransportBidPortal.csproj"))) {
  $root = (Get-Location).Path
}
if (-not (Test-Path (Join-Path $root "TransportBidPortal.csproj"))) {
  throw "Execute na pasta TransportBidPortal (onde esta TransportBidPortal.csproj)."
}
$src = Join-Path $root "docs\ai-assistant\rules"
$dst = Join-Path $root ".cursor\rules"
if (-not (Test-Path $src)) { throw "Pasta nao encontrada: $src" }
if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }
Copy-Item -Path (Join-Path $src "*.mdc") -Destination $dst -Force
Write-Host "OK: copiado de $src para $dst"
