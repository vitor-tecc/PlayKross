#requires -Version 7.4
param([ValidateRange(60,1800)][int]$Seconds=1800,[string]$MediaInterface='Ethernet')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$directory=Join-Path $root '.artifacts/live-flow'
New-Item -ItemType Directory -Force $directory | Out-Null
New-Item -ItemType Directory -Force (Join-Path $root '.runtime') | Out-Null
try{$probeLock=[IO.FileStream]::new((Join-Path $root '.runtime/live-flow-probe.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Another live-flow probe owns the shared controls.'}
$services=$null;$fixture=$null;$publisher=$null
Push-Location $root
try{
    $services=& (Join-Path $PSScriptRoot 'Start-TemporaryTunnel.ps1') -NoBuild -PassThru -MediaInterface $MediaInterface
    foreach($name in @('ready.json','next-preset','stop')){Remove-Item -LiteralPath (Join-Path $directory $name) -ErrorAction SilentlyContinue}
    $fixture=Start-Process -FilePath (Join-Path $root '.artifacts/timing-source/Debug/CaptureFixture.exe') -ArgumentList '--timing' -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 600
    $environment=@{JKILLER_PROBE_JOIN_LINK=$services.LocalJoinLink;JKILLER_TARGET_PID=$fixture.Id.ToString();JKILLER_SUSTAINED_SECONDS=$Seconds.ToString();JKILLER_PROBE_CAPTURE_MODE='video-only';DOTNET_CLI_HOME=(Join-Path $root '.tools/dotnet-home');NUGET_PACKAGES=(Join-Path $root '.tools/nuget')}
    $probe=Join-Path $root 'tools/acceptance/LiveFlowProbe/bin/Debug/net10.0-windows10.0.26100.0/LiveFlowProbe.dll'
    $publisher=Start-Process -FilePath (Join-Path $root '.tools/dotnet/dotnet.exe') -ArgumentList ('"'+$probe+'"') -WorkingDirectory $root -Environment $environment -WindowStyle Hidden -RedirectStandardOutput (Join-Path $directory 'external-publisher.log') -RedirectStandardError (Join-Path $directory 'external-errors.log') -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(40)
    while(-not(Test-Path (Join-Path $directory 'ready.json'))){
        if($publisher.HasExited){throw 'External test publisher exited; inspect external-errors.log.'}
        if([DateTime]::UtcNow -ge $deadline){throw 'External native publication did not become ready.'}
        Start-Sleep -Milliseconds 150
    }
    $stopAt=[DateTime]::UtcNow.AddSeconds($Seconds)
    Write-Output ('PHONE TEST LINK: '+$services.JoinLink)
    Write-Output ('Synthetic video-only 1080p60 test is ready until '+$stopAt.ToLocalTime().ToString('HH:mm:ss')+'. Phone Wi-Fi off; join and watch. External acceptance is pending, not automatically passed.')
    # Private invitation is written only to ignored local runtime state for local probes.
    $services | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root '.runtime/external-test.local.json')
    while(-not(Test-Path (Join-Path $directory 'stop')) -and [DateTime]::UtcNow -lt $stopAt){
        if($publisher.HasExited){throw 'External publisher stopped unexpectedly.'}
        Start-Sleep -Seconds 1
    }
}finally{
    if($publisher){'stop' | Set-Content (Join-Path $directory 'stop')}
    if($publisher -and -not $publisher.HasExited){$publisher.WaitForExit(15000)|Out-Null;if(-not $publisher.HasExited){$publisher.Kill($true);$publisher.WaitForExit(5000)|Out-Null}}
    if($fixture -and -not $fixture.HasExited){$fixture.Kill();$fixture.WaitForExit(2000)|Out-Null}
    try{
        if($services){& (Join-Path $PSScriptRoot 'Stop-TemporaryTunnel.ps1') -ExpectedTunnel $services.Tunnel;Remove-Item -LiteralPath (Join-Path $root '.runtime/external-test.local.json') -ErrorAction SilentlyContinue}
    }finally{Pop-Location;$probeLock.Dispose()}
}
