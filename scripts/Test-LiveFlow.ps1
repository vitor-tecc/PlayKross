#requires -Version 7.4
param([int]$GeometryDashPid = 0)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$directory = Join-Path $root '.artifacts/live-flow'; New-Item -ItemType Directory -Force -Path $directory | Out-Null
foreach ($name in @('ready.json','next-preset','stop')) { Remove-Item -LiteralPath (Join-Path $directory $name) -ErrorAction SilentlyContinue }
$services = $null; $fixture = $null; $publisher = $null
try {
    $services = & (Join-Path $PSScriptRoot 'Start-Local.ps1') -PassThru
    if ($GeometryDashPid) {
        $target = Get-Process -Id $GeometryDashPid
        if ($target.ProcessName -ne 'GeometryDash') { throw 'The selected live target must be Geometry Dash.' }
        $targetId = $target.Id
    } else {
        $fixture = Start-Process -FilePath (Join-Path $root '.artifacts/native/Debug/CaptureFixture.exe') -WindowStyle Hidden -PassThru
        $targetId = $fixture.Id; Start-Sleep -Milliseconds 500
    }
    $probe = Join-Path $root 'tools/acceptance/LiveFlowProbe/bin/Debug/net10.0-windows10.0.26100.0/LiveFlowProbe.dll'
    $environment = @{ JKILLER_PROBE_JOIN_LINK=$services.JoinLink; JKILLER_TARGET_PID=$targetId.ToString(); DOTNET_CLI_HOME=(Join-Path $root '.tools/dotnet-home'); NUGET_PACKAGES=(Join-Path $root '.tools/nuget') }
    $publisher = Start-Process -FilePath (Join-Path $root '.tools/dotnet/dotnet.exe') -ArgumentList @(('"' + $probe + '"')) -Environment $environment -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $directory 'publisher.log') -RedirectStandardError (Join-Path $directory 'publisher-errors.log') -PassThru
    foreach ($height in @(720,1080)) {
        $deadline = [DateTime]::UtcNow.AddSeconds(40)
        while (-not (Test-Path -LiteralPath (Join-Path $directory 'ready.json')) -or ((Get-Content -Raw -LiteralPath (Join-Path $directory 'ready.json') | ConvertFrom-Json).height -ne $height)) {
            if ($publisher.HasExited) { throw 'Native flow failed; inspect .artifacts/live-flow/publisher-errors.log.' }
            if ([DateTime]::UtcNow -ge $deadline) { throw "Native $height preset did not become ready." }
            Start-Sleep -Milliseconds 150
        }
        $previous = @{}; foreach ($name in @('JKILLER_PROBE_JOIN_LINK','JKILLER_SMOKE_HEIGHT','JKILLER_LIVE_FLOW')) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
        try {
            $env:JKILLER_PROBE_JOIN_LINK=$services.JoinLink; $env:JKILLER_SMOKE_HEIGHT=$height.ToString(); $env:JKILLER_LIVE_FLOW='1'
            Push-Location (Join-Path $root 'web')
            try { npm exec -- playwright test --config playwright.protocol.config.ts live-flow.spec.ts; if ($LASTEXITCODE -ne 0) { throw "Actual $height browser smoke failed." } }
            finally { Pop-Location }
        } finally { foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') } }
        if ($height -eq 720) { Set-Content -LiteralPath (Join-Path $directory 'next-preset') -Value 'continue' }
    }
} finally {
    Set-Content -LiteralPath (Join-Path $directory 'stop') -Value 'stop'
    if ($publisher -and -not $publisher.HasExited) { $publisher.WaitForExit(15000) | Out-Null; if (-not $publisher.HasExited) { $publisher.Kill($true); $publisher.WaitForExit(5000) | Out-Null } }
    if ($fixture -and -not $fixture.HasExited) { $fixture.Kill(); $fixture.WaitForExit(2000) | Out-Null }
    if ($services) { & (Join-Path $PSScriptRoot 'Stop-Local.ps1') }
}
if ($publisher -and $publisher.HasExited -and $publisher.ExitCode -ne 0) { throw 'Native controller validation failed; inspect publisher-errors.log.' }
