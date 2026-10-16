#requires -Version 7.4
param([switch]$DuringPreparation)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$directory = Join-Path $root '.artifacts/protocol'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
foreach ($name in @('publisher-ready.local.json','stop-publisher','revoke-publication')) { Remove-Item -LiteralPath (Join-Path $directory $name) -ErrorAction SilentlyContinue }
$services = $null; $fixture = $null; $publisher = $null; $helper = $null
try {
    $services = & (Join-Path $PSScriptRoot 'Start-Local.ps1') -NoBuild -PassThru
    $fixture = Start-Process -FilePath (Join-Path $root '.artifacts/native/Debug/CaptureFixture.exe') -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 500
    $probe = Join-Path $root 'tools/acceptance/OwnedEngineProbe/bin/Debug/net10.0-windows10.0.26100.0/OwnedEngineProbe.dll'
    $environment = @{ JKILLER_PROBE_JOIN_LINK=$services.JoinLink; JKILLER_FIXTURE_PID=$fixture.Id.ToString(); DOTNET_CLI_HOME=(Join-Path $root '.tools/dotnet-home') }
    $publisher = Start-Process -FilePath (Join-Path $root '.tools/dotnet/dotnet.exe') -ArgumentList @(('"' + $probe + '"'), 'publish') -Environment $environment -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $directory 'controller-exit.log') -RedirectStandardError (Join-Path $directory 'controller-exit-errors.log') -PassThru
    $expected = Join-Path $root '.runtime/obs/bin/64bit/obs64.exe'
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not $DuringPreparation -and -not (Test-Path -LiteralPath (Join-Path $directory 'publisher-ready.local.json'))) {
        if ($publisher.HasExited -or [DateTime]::UtcNow -ge $deadline) { throw 'Synthetic publication did not prepare for the controller-exit check.' }
        Start-Sleep -Milliseconds 150
    }
    do {
        $children = @(Get-CimInstance Win32_Process -Filter ("ParentProcessId=" + $publisher.Id) | Where-Object { $_.ExecutablePath -eq $expected })
        if ($children.Count -eq 0) { Start-Sleep -Milliseconds 50 }
    } until ($children.Count -gt 0 -or [DateTime]::UtcNow -ge $deadline)
    if ($children.Count -ne 1) { throw 'Owned OBS helper could not be identified uniquely.' }
    $helper = [Diagnostics.Process]::GetProcessById($children[0].ProcessId)
    if (-not $DuringPreparation) { Start-Sleep -Milliseconds 750 }
    $publisher.Kill(); $publisher.WaitForExit(5000) | Out-Null
    if (-not $helper.WaitForExit(7000)) { throw 'Controller exit left its owned capture/encoding helper running.' }
    Write-Output 'PASS: original controller exit automatically stops its owned capture/encoding helper.'
}
finally {
    foreach ($process in @($publisher,$helper,$fixture)) { if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null } }
    if ($services) { & (Join-Path $PSScriptRoot 'Stop-Local.ps1') }
}
