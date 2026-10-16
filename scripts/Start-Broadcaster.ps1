#requires -Version 7.4
param([switch]$NoBuild, [switch]$PassThru)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'Run scripts/Setup-Dev.ps1 first.' }
if (-not (Test-Path -LiteralPath (Join-Path $root '.runtime/obs/obs-plugins/64bit/jk-obs.dll'))) { throw 'Run scripts/Build-Native.ps1 -Plugin first.' }
if (-not $NoBuild) {
    $env:DOTNET_CLI_HOME=Join-Path $root '.tools/dotnet-home'; $env:NUGET_PACKAGES=Join-Path $root '.tools/nuget'
    & $dotnet build (Join-Path $root 'src/JKiller.App') --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Broadcaster build failed.' }
}
$dll = Join-Path $root 'src/JKiller.App/bin/Debug/net10.0-windows10.0.26100.0/JKiller.App.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw 'Build the native app before launching it.' }
$process = Start-Process -FilePath $dotnet -ArgumentList @(('"' + $dll + '"')) -WorkingDirectory $root -WindowStyle Hidden -PassThru
if ($PassThru) { return $process }
Write-Output 'Opened PlayKross. Paste the room link, select video, then Start. Audio is off by default; selecting a supported app opts into experimental app-and-child-process audio.'
