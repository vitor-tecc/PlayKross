#requires -Version 7.4
param([ValidateSet('Automated','CodeOnly','LocalMedia','External')][string]$Mode='Automated', [string]$RoomLink)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_CLI_HOME=Join-Path $root '.tools/dotnet-home'; $env:NUGET_PACKAGES=Join-Path $root '.tools/nuget'; $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
Push-Location $root
try {
    switch ($Mode) {
        { $_ -in 'Automated','CodeOnly' } {
            & (Join-Path $root '.tools/dotnet/dotnet.exe') test JKiller.sln --nologo; if ($LASTEXITCODE -ne 0) { throw '.NET correctness tests failed.' }
            & (Join-Path $root '.tools/dotnet/dotnet.exe') build src/JKiller.App/JKiller.App.csproj --nologo; if ($LASTEXITCODE -ne 0) { throw 'Native UI build failed.' }
            npm --prefix web test -- --run; if ($LASTEXITCODE -ne 0) { throw 'Browser correctness tests failed.' }
            npm --prefix web run build; if ($LASTEXITCODE -ne 0) { throw 'Browser production build failed.' }
            if ($Mode -eq 'CodeOnly') { & (Join-Path $PSScriptRoot 'Build-Native.ps1') -Plugin -Test -NonInteractive -NoInstall }
            else { & (Join-Path $PSScriptRoot 'Build-Native.ps1') -Plugin -Test }
            & (Join-Path $root 'tests/setup/SetupChecks.ps1')
            & (Join-Path $root 'tests/setup/CleanupChecks.ps1')
            & (Join-Path $root 'tests/setup/MediaInterfaceChecks.ps1')
            if ($Mode -eq 'CodeOnly') { Write-Output 'Deferred: audio tone, native child-process tests, app/browser/capture/media startup and all real streaming measurements. Compiled plugin was not installed.' }
            Write-Output 'Automated correctness: passed. Real streaming smoothness, audio exclusion and internet performance are separate checks.'
        }
        LocalMedia {
            & (Join-Path $root 'tests/setup/LocalHostingChecks.ps1')
            & (Join-Path $PSScriptRoot 'Test-OwnedEngine.ps1')
            & (Join-Path $PSScriptRoot 'Test-LocalProtocol.ps1')
            & (Join-Path $PSScriptRoot 'Test-LiveFlow.ps1')
            & (Join-Path $PSScriptRoot 'Test-InAppViewing.ps1')
            Write-Output 'Local protocol/integration checks: passed. Sustained and external acceptance remain separate.'
        }
        External {
            if (-not $RoomLink) { Write-Output 'PENDING: provide the public HTTPS invitation as -RoomLink after configuring docs/setup/internet.md.'; return }
            $uri=[Uri]$RoomLink
            if ($uri.Scheme -ne 'https' -or $uri.IsLoopback -or -not $uri.Fragment.StartsWith('#invite=')) { throw 'Use a public HTTPS invitation.' }
            Write-Output 'PENDING: turn off phone Wi-Fi, open this link on mobile data, join, watch and enable sound:'
            Write-Output $RoomLink
            Write-Output 'Export Playback diagnostics after ten minutes and record device/browser, mobile network, ICE path, loss, RTT, presented FPS and measured video/audio timing. Keep invitations out of reports. Run tools/acceptance/Analyze-Timing.ps1 with measured evidence. A local TCP/HTTP probe cannot pass this gate.'
        }
    }
} finally { Pop-Location }
