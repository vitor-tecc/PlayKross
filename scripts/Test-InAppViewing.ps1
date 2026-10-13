#requires -Version 7.4
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_CLI_HOME=Join-Path $root '.tools/dotnet-home'; $env:NUGET_PACKAGES=Join-Path $root '.tools/nuget'
& (Join-Path $root '.tools/dotnet/dotnet.exe') build (Join-Path $root 'tools/acceptance/WebViewProbe') --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Embedded viewer probe build failed.' }
$services=$null; $fixture=$null; $previous=@{}
foreach ($name in @('JKILLER_FIXTURE_PID','JKILLER_PROBE_JOIN_LINK')) { $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
Push-Location $root
try {
    $services = & (Join-Path $PSScriptRoot 'Start-Local.ps1') -PassThru
    $fixture = Start-Process -FilePath (Join-Path $root '.artifacts/native/Debug/CaptureFixture.exe') -WindowStyle Hidden -PassThru; Start-Sleep -Milliseconds 500
    $env:JKILLER_FIXTURE_PID=$fixture.Id.ToString(); $env:JKILLER_PROBE_JOIN_LINK=$services.JoinLink
    & (Join-Path $root '.tools/dotnet/dotnet.exe') (Join-Path $root 'tools/acceptance/WebViewProbe/bin/Debug/net10.0-windows10.0.26100.0/WebViewProbe.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Embedded viewer integration failed.' }
} finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
    if ($fixture -and -not $fixture.HasExited) { $fixture.Kill(); $fixture.WaitForExit(2000) | Out-Null }
    if ($services) { & (Join-Path $PSScriptRoot 'Stop-Local.ps1') }; Pop-Location
}
