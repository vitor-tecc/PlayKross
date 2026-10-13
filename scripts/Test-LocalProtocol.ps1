#requires -Version 7.4
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$directory = Join-Path $root '.artifacts/protocol'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
foreach ($name in @('publisher-ready.local.json','stop-publisher','revoke-publication')) { Remove-Item -LiteralPath (Join-Path $directory $name) -ErrorAction SilentlyContinue }
$services = $null; $fixture = $null; $publisher = $null
try {
    $services = & (Join-Path $PSScriptRoot 'Start-Local.ps1') -PassThru
    $fixture = Start-Process -FilePath (Join-Path $root '.artifacts/native/Debug/CaptureFixture.exe') -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 500
    $probe = Join-Path $root 'tools/acceptance/OwnedEngineProbe/bin/Debug/net10.0-windows10.0.26100.0/OwnedEngineProbe.dll'
    $environment = @{ JKILLER_PROBE_JOIN_LINK=$services.JoinLink; JKILLER_FIXTURE_PID=$fixture.Id.ToString(); DOTNET_CLI_HOME=(Join-Path $root '.tools/dotnet-home'); NUGET_PACKAGES=(Join-Path $root '.tools/nuget') }
    $publisher = Start-Process -FilePath (Join-Path $root '.tools/dotnet/dotnet.exe') -ArgumentList @(('"' + $probe + '"'), 'publish') -Environment $environment -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $directory 'publisher.log') -RedirectStandardError (Join-Path $directory 'publisher-errors.log') -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path -LiteralPath (Join-Path $directory 'publisher-ready.local.json'))) {
        if ($publisher.HasExited) { throw 'Native publication probe failed; inspect .artifacts/protocol/publisher-errors.log.' }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Native publication did not prepare within 30 seconds.' }
        Start-Sleep -Milliseconds 150
    }
    $previous = $env:JKILLER_PROBE_JOIN_LINK
    $env:JKILLER_PROBE_JOIN_LINK = $services.JoinLink
    try {
        Push-Location $root
        try { npm --prefix web run test:protocol; if ($LASTEXITCODE -ne 0) { throw 'Actual browser protocol integration failed.' } }
        finally { Pop-Location }
    }
    finally { $env:JKILLER_PROBE_JOIN_LINK = $previous }
}
finally {
    Set-Content -LiteralPath (Join-Path $directory 'stop-publisher') -Value 'stop'
    if ($publisher -and -not $publisher.HasExited) { $publisher.WaitForExit(15000) | Out-Null; if (-not $publisher.HasExited) { $publisher.Kill($true); $publisher.WaitForExit(5000) | Out-Null } }
    if ($fixture -and -not $fixture.HasExited) { $fixture.CloseMainWindow() | Out-Null; $fixture.WaitForExit(2000) | Out-Null; if (-not $fixture.HasExited) { $fixture.Kill(); $fixture.WaitForExit(2000) | Out-Null } }
    if ($services) { & (Join-Path $PSScriptRoot 'Stop-Local.ps1') }
}
if ($publisher -and $publisher.HasExited -and $publisher.ExitCode -ne 0) { throw 'Native publication or real revocation validation failed; inspect publisher-errors.log.' }
