#requires -Version 7.4
param([Parameter(Mandatory)][string]$Evidence, [string]$Output='.artifacts/acceptance/validated.json')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$env:DOTNET_CLI_HOME=Join-Path $root '.tools/dotnet-home'; $env:NUGET_PACKAGES=Join-Path $root '.tools/nuget'
& (Join-Path $root '.tools/dotnet/dotnet.exe') build (Join-Path $PSScriptRoot 'AcceptanceAnalysis') --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Acceptance validator build failed.' }
$report=& (Join-Path $root '.tools/dotnet/dotnet.exe') (Join-Path $PSScriptRoot 'AcceptanceAnalysis/bin/Debug/net10.0/AcceptanceAnalysis.dll') ([IO.Path]::GetFullPath($Evidence))
if ($LASTEXITCODE -ne 0) { throw 'Evidence validation failed; no output written.' }
$json=$report | ConvertFrom-Json
$target=[IO.Path]::GetFullPath($Output); New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
$report | Set-Content -LiteralPath $target -Encoding utf8
$json.checks | ConvertTo-Json
