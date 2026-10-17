#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstanceRootA,[Parameter(Mandatory)][string]$InstanceRootB,[switch]$VideoOnly,[string]$SystemEndpointId,[string]$MonitorSourceId,[switch]$Headed)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'ConcurrentProbeTools.ps1')
$mode=Resolve-ConcurrentMode -VideoOnly:$VideoOnly -SystemEndpointId $SystemEndpointId -MonitorSourceId $MonitorSourceId
Assert-ConcurrentRoots $InstanceRootA $InstanceRootB
$run=Join-Path $root ('.artifacts/concurrent/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N'))
$locks=[Collections.Generic.List[IDisposable]]::new(); $owned=[Collections.Generic.List[object]]::new(); $publishers=[Collections.Generic.List[object]]::new(); $services=$null
New-Item -ItemType Directory -Path $run | Out-Null
$result=[ordered]@{startedUtc=[DateTime]::UtcNow.ToString('o');sourceCommit=(& git -c safe.directory=D:/Documentos/JKiller -C $root rev-parse HEAD).Trim();captureMode=$mode;browserHeadless=(-not $Headed);automated='failed';humanListening='pending';syntheticToneDetection='pending';evidence='Decoded frames/Opus packet counters are transport evidence, not audibility, isolation or timing acceptance.';error=$null}
if($mode -eq 'system-output'){$result.browserPhysicalAudio='disabled: explicit Chromium --mute-audio';$result.monitorSourceId=$MonitorSourceId;$result.renderEndpointId=$SystemEndpointId}
function Track($Process) { $Process.Refresh(); if($Process.HasExited){throw 'Owned probe exited during startup.'}; $identity=@{Id=$Process.Id;Creation=$Process.StartTime.ToFileTimeUtc().ToString();Path=$Process.MainModule.FileName}; $owned.Add($identity); return $Process }
try {
    foreach($instance in @($InstanceRootA,$InstanceRootB)) {
        $lockPath=Join-Path $instance '.runtime/concurrent-probe.lock'
        $locks.Add([IO.FileStream]::new($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None))
    }
    foreach($project in @('LiveFlowProbe','WebViewProbe')) {
        & (Join-Path $root '.tools/dotnet/dotnet.exe') build (Join-Path $root ('tools/acceptance/'+$project)) --nologo
        if($LASTEXITCODE -ne 0){throw "$project build failed."}
    }
    if($mode -eq 'system-output'){
        $assemblyRoot=Join-Path $root 'tools/acceptance/LiveFlowProbe/bin/Debug/net10.0-windows10.0.26100.0'
        foreach($assembly in @('JKiller.Contracts.dll','JKiller.Windows.dll')){[Reflection.Assembly]::LoadFrom((Join-Path $assemblyRoot $assembly))|Out-Null}
        $monitor=[JKiller.Windows.Capture.WindowsSourceCatalog]::new().ListSourcesAsync([Threading.CancellationToken]::None).GetAwaiter().GetResult() | Where-Object { $_.Kind -eq 'Monitor' -and $_.Id -eq $MonitorSourceId }
        if(-not $monitor){throw 'Explicit monitor is unavailable; no fallback.'}
        function Assert-FixtureEndpoint {
            $default=[JKiller.Windows.Capture.WindowsAudioCatalog]::new().ResolveDefaultAsync([Threading.CancellationToken]::None).GetAwaiter().GetResult()
            if(-not $default -or $default.Id -ne $SystemEndpointId){throw 'Fixture WAVE_MAPPER default differs from explicit fixed capture endpoint; no device settings changed.'}
        }
        Assert-FixtureEndpoint
    }
    $services=& (Join-Path $PSScriptRoot 'Start-Local.ps1') -PassThru
    foreach($entry in $services.Processes){$owned.Add(@{Id=$entry.Pid;Creation=$entry.CreationFileTime;Path=$entry.ExecutablePath})}
    $probe=Join-Path $root 'tools/acceptance/LiveFlowProbe/bin/Debug/net10.0-windows10.0.26100.0/LiveFlowProbe.dll'
    $common=@{JKILLER_PROBE_JOIN_LINK=$services.JoinLink;JKILLER_PROBE_CAPTURE_MODE=$mode;DOTNET_CLI_HOME=(Join-Path $root '.tools/dotnet-home');NUGET_PACKAGES=(Join-Path $root '.tools/nuget')}
    $slot=0; $fixtureIds=@()
    foreach($instance in @($InstanceRootA,$InstanceRootB)) {
        $letter=@('A','B')[$slot++]; $directory=Join-Path $run $letter; New-Item -ItemType Directory $directory | Out-Null
        if($mode -eq 'system-output'){Assert-FixtureEndpoint}
        $fixture=Track (Start-Process -FilePath (Join-Path $root '.artifacts/native/Debug/CaptureFixture.exe') -WindowStyle Hidden -PassThru)
        $fixtureIds += $fixture.Id; Start-Sleep -Milliseconds 500
        if($mode -eq 'system-output'){
            $surface=Set-ConcurrentMonitorFixture ($owned.ToArray() | Where-Object Id -eq $fixture.Id) $monitor.NativeHandle
            Assert-FixtureEndpoint
            $surface | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'synthetic-monitor-surface.json')
        }
        $child=$common.Clone(); $child.JKILLER_TARGET_PID=$fixture.Id.ToString()
        $arguments=@($probe,'--root',$instance,'--artifact-dir',$directory,'--video-source',$(if($mode -eq 'system-output'){'monitor:'+$MonitorSourceId}else{'fixture-window'}),'--nickname',('Concurrent '+$letter),'--concurrent')
        if($mode -eq 'system-output'){$arguments += @('--render-endpoint',$SystemEndpointId)}
        # Start-Process joins an ArgumentList into one Windows command line; quote each supplied path/value.
        if(($arguments | Where-Object {$_ -match '["\r\n]'}).Count){throw 'Quotes/newlines are not valid probe option values.'}
        $quoted=@($arguments | ForEach-Object {'"'+$_.TrimEnd('\')+'"'})
        $publisher=Track (Start-Process -FilePath (Join-Path $root '.tools/dotnet/dotnet.exe') -ArgumentList $quoted -Environment $child -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $directory 'publisher.log') -RedirectStandardError (Join-Path $directory 'publisher-errors.log') -PassThru)
        $publishers.Add([pscustomobject]@{Process=$publisher;Directory=$directory})
    }
    foreach($publisher in $publishers){$deadline=[DateTime]::UtcNow.AddSeconds(45); while(-not (Test-Path -LiteralPath (Join-Path $publisher.Directory 'ready.json'))){if($publisher.Process.HasExited){throw "Publisher exited; inspect $($publisher.Directory)/publisher-errors.log"}; if([DateTime]::UtcNow -gt $deadline){throw 'Concurrent publisher readiness timed out.'};Start-Sleep -Milliseconds 150}}
    Invoke-ConcurrentEnvironment @{JKILLER_PROBE_JOIN_LINK=$services.JoinLink;JKILLER_CONCURRENT_RUN=$run;JKILLER_PROBE_CAPTURE_MODE=$mode} {
        Push-Location (Join-Path $root 'web')
        try{ $browserArguments=@('exec','--','playwright','test','--config','playwright.protocol.config.ts','--output',(Join-Path $run 'browser'),'concurrent.spec.ts'); if($Headed){$browserArguments+='--headed'}; npm @browserArguments; if($LASTEXITCODE -ne 0){throw 'Two-publication browser functional check failed.'}}finally{Pop-Location}
    }
    # Free the portable helper before using the same isolated root for native viewing.
    foreach($publisher in $publishers){Set-Content -LiteralPath (Join-Path $publisher.Directory 'stop') -Value stop; if(-not $publisher.Process.WaitForExit(15000)){throw 'Publisher did not stop gracefully.'};if($publisher.Process.ExitCode -ne 0){throw 'Publisher cleanup reported failure.'}}
    Invoke-ConcurrentEnvironment @{JKILLER_FIXTURE_PID=$fixtureIds[0].ToString();JKILLER_PROBE_JOIN_LINK=$services.JoinLink;JKILLER_PROBE_CAPTURE_MODE=$mode;JKILLER_PROBE_MONITOR_ID=$MonitorSourceId;JKILLER_PROBE_RENDER_ENDPOINT_ID=$SystemEndpointId;JKILLER_PROBE_ROOT=$InstanceRootA} {
        & (Join-Path $root '.tools/dotnet/dotnet.exe') (Join-Path $root 'tools/acceptance/WebViewProbe/bin/Debug/net10.0-windows10.0.26100.0/WebViewProbe.dll')
        if($LASTEXITCODE -ne 0){throw 'Native WebView functional check failed.'}
    }
    $result.automated='passed'
} catch {$result.error=$_.Exception.Message; throw} finally {
    foreach($publisher in $publishers){Set-Content -LiteralPath (Join-Path $publisher.Directory 'stop') -Value stop; if(-not $publisher.Process.HasExited){$publisher.Process.WaitForExit(10000)|Out-Null}}
    try {
        $cleanupErrors=@(); $reverse=$owned.ToArray(); [array]::Reverse($reverse)
        foreach($identity in $reverse){try{Stop-ConcurrentOwnedProcess $identity}catch{$cleanupErrors += $_.Exception.Message}}
        if($cleanupErrors.Count){$result.automated='failed'; $result.error='Cleanup: '+($cleanupErrors -join '; ')}
    } finally {
        foreach($held in $locks){$held.Dispose()}
        $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8NoBOM
        Write-Output "Concurrent run artifacts: $run; human listening PENDING. Supplied roots preserved."
    }
    if($cleanupErrors.Count){throw $result.error}
}
