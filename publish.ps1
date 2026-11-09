[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'

if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) {
    throw 'Özel OutputDirectory kullanımı güvenli aşamalı yayın akışında desteklenmiyor. Varsayılan artifacts klasörünü kullanın.'
}

$builder = Join-Path $PSScriptRoot 'build-exe.ps1'

if (-not (Test-Path -LiteralPath $builder -PathType Leaf)) {
    throw "Güvenli derleme betiği bulunamadı: $builder"
}

& $builder -RuntimeIdentifier $RuntimeIdentifier
