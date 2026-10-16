#requires -Version 7.4
param([string]$Origin = 'http://localhost:5000', [string]$Output = '.artifacts/diagnostics/room-report.json')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_CLI_HOME = Join-Path $root '.tools/dotnet-home'; $env:NUGET_PACKAGES = Join-Path $root '.tools/nuget'
$project = Join-Path $root 'tools/acceptance/ExportDiagnostics/ExportDiagnostics.csproj'
& (Join-Path $root '.tools/dotnet/dotnet.exe') build $project --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic exporter build failed.' }
$previous = $env:JKILLER_DIAGNOSTIC_ORIGIN
Push-Location $root
try {
    $env:JKILLER_DIAGNOSTIC_ORIGIN = $Origin
    $report = & (Join-Path $root '.tools/dotnet/dotnet.exe') (Join-Path $root 'tools/acceptance/ExportDiagnostics/bin/Debug/net10.0-windows10.0.26100.0/ExportDiagnostics.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Export failed. No report was written.' }
    $report | ConvertFrom-Json | Out-Null
    $target = [IO.Path]::GetFullPath($Output); New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
    [IO.File]::WriteAllText($target, ($report -join [Environment]::NewLine))
    Write-Output $target
} finally { $env:JKILLER_DIAGNOSTIC_ORIGIN = $previous; Pop-Location }
