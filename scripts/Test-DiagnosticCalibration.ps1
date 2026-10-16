#requires -Version 7.4
param([switch]$Baseline, [switch]$Static)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$services=$null; $fixture=$null
$oldTrace=$env:JKILLER_DISABLE_TRACE; $oldInvite=$env:JKILLER_PROBE_JOIN_LINK; $oldFixture=$env:JKILLER_FIXTURE_PID
$oldStatic=$env:JKILLER_FIXTURE_STATIC
try {
    $services=& (Join-Path $PSScriptRoot 'Start-Local.ps1') -PassThru
    $fixture=Start-Process -FilePath (Join-Path $root '.artifacts/native/Debug/CaptureFixture.exe') -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 500
    $env:JKILLER_DISABLE_TRACE=if ($Baseline) { '1' } else { $null }
    $env:JKILLER_FIXTURE_STATIC=if ($Static) { '1' } else { $null }
    $env:JKILLER_PROBE_JOIN_LINK=$services.JoinLink; $env:JKILLER_FIXTURE_PID=$fixture.Id.ToString()
    & (Join-Path $root '.tools/dotnet/dotnet.exe') (Join-Path $root 'tools/acceptance/DiagnosticsProbe/bin/Debug/net10.0-windows10.0.26100.0/DiagnosticsProbe.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Actual diagnostic calibration failed.' }
} finally {
    $env:JKILLER_DISABLE_TRACE=$oldTrace; $env:JKILLER_PROBE_JOIN_LINK=$oldInvite; $env:JKILLER_FIXTURE_PID=$oldFixture
    $env:JKILLER_FIXTURE_STATIC=$oldStatic
    if ($fixture -and -not $fixture.HasExited) { $fixture.Kill(); $fixture.WaitForExit(2000) | Out-Null }
    if ($services) { & (Join-Path $PSScriptRoot 'Stop-Local.ps1') }
}
