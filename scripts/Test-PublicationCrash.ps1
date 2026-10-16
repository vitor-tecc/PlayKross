#requires -Version 7.4
param([switch]$SourceLoss)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$directory=Join-Path $root '.artifacts/live-flow'
New-Item -ItemType Directory -Force $directory | Out-Null
New-Item -ItemType Directory -Force (Join-Path $root '.runtime') | Out-Null
try{$probeLock=[IO.FileStream]::new((Join-Path $root '.runtime/live-flow-probe.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Another live-flow probe owns the shared controls.'}
$services=$null;$fixture=$null;$publisher=$null
Push-Location $root
try{
    $services=& (Join-Path $PSScriptRoot 'Start-Local.ps1') -NoBuild -PassThru
    foreach($name in @('ready.json','next-preset','stop')){Remove-Item -LiteralPath (Join-Path $directory $name) -ErrorAction SilentlyContinue}
    $fixture=Start-Process -FilePath (Join-Path $root '.artifacts/native/Debug/CaptureFixture.exe') -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 500
    $environment=@{JKILLER_PROBE_JOIN_LINK=$services.JoinLink;JKILLER_TARGET_PID=$fixture.Id.ToString();JKILLER_PROBE_CAPTURE_MODE='video-only';DOTNET_CLI_HOME=(Join-Path $root '.tools/dotnet-home');NUGET_PACKAGES=(Join-Path $root '.tools/nuget')}
    $probe=Join-Path $root 'tools/acceptance/LiveFlowProbe/bin/Debug/net10.0-windows10.0.26100.0/LiveFlowProbe.dll'
    $publisher=Start-Process -FilePath (Join-Path $root '.tools/dotnet/dotnet.exe') -ArgumentList ('"'+$probe+'"') -WorkingDirectory $root -Environment $environment -WindowStyle Hidden -RedirectStandardOutput (Join-Path $directory 'crash-publisher.log') -RedirectStandardError (Join-Path $directory 'crash-errors.log') -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(40)
    while(-not(Test-Path (Join-Path $directory 'ready.json'))){if($publisher.HasExited){throw 'Crash fixture publisher exited before readiness.'};if([DateTime]::UtcNow -ge $deadline){throw 'Crash fixture readiness timed out.'};Start-Sleep -Milliseconds 150}
    $ready=Get-Content -Raw (Join-Path $directory 'ready.json') | ConvertFrom-Json
    $helper=Get-Process -Id $ready.helperPid
    if($helper.StartTime.ToFileTimeUtc().ToString() -ne $ready.helperCreation -or $helper.Path -ne (Join-Path $root '.runtime/obs/bin/64bit/obs64.exe')){throw 'Owned helper identity changed.'}
    if($SourceLoss){
        $clock=[Diagnostics.Stopwatch]::StartNew()
        $fixture.Kill();$fixture.WaitForExit(2000)|Out-Null
        if(-not $publisher.WaitForExit(10000)){throw 'Selected-source exit did not stop the publisher.'}
        if(-not $helper.WaitForExit(5000)){throw 'Selected-source exit left the native helper running.'}
        $errorText=Get-Content -Raw (Join-Path $directory 'crash-errors.log')
        if($errorText -notmatch 'The selected screen or window became unavailable\.'){throw 'Real source loss did not preserve its actionable native reason.'}
        $report=@{status='passed';topology='same-PC real WGC/native processes';captureMode='video-only';sourceLossStopSeconds=$clock.Elapsed.TotalSeconds;helperExited=$true;reason='video_source_lost'}
        $report | ConvertTo-Json | Set-Content (Join-Path $root '.artifacts/source-loss.json')
        $report | ConvertTo-Json -Compress | Write-Output
        return
    }
    $link=[Uri]$services.JoinLink
    $invitation=[Uri]::UnescapeDataString($link.Fragment.Substring(8))
    $observer=Invoke-RestMethod 'http://localhost:5000/api/join' -Method Post -ContentType 'application/json' -Body (@{invite=$invitation;nickname='Crash acceptance observer';desktop=$true}|ConvertTo-Json)
    $headers=@{Authorization='Bearer '+$observer.sessionToken}
    $snapshot=Invoke-RestMethod 'http://localhost:5000/api/room' -Headers $headers
    if(@($snapshot.streams).Count -ne 1){throw 'Expected one active publication before controller crash.'}
    $oldId=$snapshot.streams[0].streamId
    $clock=[Diagnostics.Stopwatch]::StartNew()
    # Kill the controller alone: the kernel job must close the native helper.
    $publisher.Kill();$publisher.WaitForExit(5000)|Out-Null
    if(-not $helper.WaitForExit(5000)){throw 'Controller death did not terminate its owned helper within five seconds.'}
    $helperExitSeconds=$clock.Elapsed.TotalSeconds
    foreach($entry in $services.Processes){if(-not(Get-Process -Id $entry.Pid -ErrorAction SilentlyContinue)){throw 'Controller crash stopped a room/media service.'}}
    if($fixture.HasExited){throw 'Controller crash killed the independent target.'}
    while($clock.Elapsed.TotalSeconds -lt 35){Start-Sleep -Milliseconds 150}
    $grant=Invoke-RestMethod ('http://localhost:5000/api/streams/'+$oldId+'/playback') -Headers $headers
    do{
        $snapshot=Invoke-RestMethod 'http://localhost:5000/api/room' -Headers $headers
        $present=@($snapshot.streams | Where-Object streamId -eq $oldId).Count -gt 0
        if($present){Start-Sleep -Milliseconds 150}
    }until(-not $present -or $clock.Elapsed.TotalSeconds -ge 65)
    if($present){throw 'Abandoned publication was not reclaimed within 65 seconds.'}
    $reclaimedSeconds=$clock.Elapsed.TotalSeconds
    if([DateTimeOffset]$grant.expiresAt -le [DateTimeOffset]::UtcNow){throw 'Stale-grant probe would only test token expiry.'}
    $stale=Invoke-WebRequest ('http://localhost:5000'+$grant.whepUri) -Method Options -Headers @{Authorization='Bearer '+$grant.token} -SkipHttpErrorCheck
    if([int]$stale.StatusCode -ne 403){throw 'Unexpired stale media grant remained usable after publication expiry.'}
    $replacement=Invoke-RestMethod 'http://localhost:5000/api/streams' -Method Post -Headers $headers
    if($replacement.streamId -eq $oldId){throw 'Replacement publication reused the crashed stream identity.'}
    Invoke-RestMethod ('http://localhost:5000/api/streams/'+$replacement.streamId) -Method Delete -Headers $headers | Out-Null
    $report=@{status='passed';topology='same-PC real processes and HTTP';captureMode='video-only';helperExitSeconds=$helperExitSeconds;publicationReclaimedSeconds=$reclaimedSeconds;staleGrantStillUnexpired=$true;staleGrantStatus=[int]$stale.StatusCode;anotherOwnerReserved=$true;hostAndMediaSurvived=$true;targetSurvived=$true}
    $report | ConvertTo-Json | Set-Content (Join-Path $root '.artifacts/publication-crash.json')
    $report | ConvertTo-Json -Compress | Write-Output
}finally{
    if($publisher){'stop' | Set-Content (Join-Path $directory 'stop')}
    if($publisher -and -not $publisher.HasExited){$publisher.Kill($true);$publisher.WaitForExit(5000)|Out-Null}
    if($fixture -and -not $fixture.HasExited){$fixture.Kill();$fixture.WaitForExit(2000)|Out-Null}
    try{if($services){& (Join-Path $PSScriptRoot 'Stop-Local.ps1')}}finally{Pop-Location;$probeLock.Dispose()}
}
