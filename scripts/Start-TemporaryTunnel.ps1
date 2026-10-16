#requires -Version 7.4
param([string]$MediaInterface='Ethernet',[switch]$NoBuild,[switch]$PassThru)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'TemporaryTunnel.ps1')
. (Join-Path $PSScriptRoot 'DependencyTools.ps1')
. (Join-Path $PSScriptRoot 'MediaNetwork.ps1')
Resolve-MediaAddress $MediaInterface @(Get-NetIPAddress -AddressFamily IPv4) | Out-Null
$state=Join-Path $root '.runtime/temporary-tunnel.local.json'
$directory=Join-Path $root '.artifacts/temporary-tunnel'
$executable=Join-Path $root '.tools/cloudflared/cloudflared.exe'
New-Item -ItemType Directory -Force (Split-Path $state),$directory,(Split-Path $executable) | Out-Null
try{$launchLock=[IO.FileStream]::new((Join-Path $root '.runtime/tunnel-launch.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}
catch{throw 'Another temporary tunnel launch is in progress.'}
$tunnel=$null;$services=$null;$localLock=$null;$reservation=$null
try{
    if(Test-Path $state){throw 'A temporary tunnel is already tracked. Run Stop-TemporaryTunnel.ps1 before starting another.'}
    if(Test-Path (Join-Path $root '.runtime/local-services.local.json')){throw 'Stop existing local services before changing the hosting profile.'}
    $localLock=[IO.FileStream]::new((Join-Path $root '.runtime/local-launch.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    # Reserve the target before forwarding starts; an unrelated listener is never
    # knowingly published. Keep local launch coordination through host startup.
    $reservation=New-TemporaryTunnelPortReservation
    $hash='86aee4017b26625cee8484c113558f48effa4cd47f7aa05fcf425604e5d2b23c'
    if(-not(Test-Path $executable)){
        $download=$executable+'.download'
        try{
            Invoke-WebRequest 'https://github.com/cloudflare/cloudflared/releases/download/2026.10.0/cloudflared-windows-amd64.exe' -OutFile $download
            Assert-DependencyHash $download SHA256 $hash
            Move-Item -LiteralPath $download -Destination $executable
        }finally{if(Test-Path $download){Remove-Item -LiteralPath $download}}
    }
    Assert-DependencyHash $executable SHA256 $hash
    # Explicit empty configuration avoids using any personal cloudflared configuration.
    $configuration=Join-Path $root '.runtime/cloudflared-empty.generated.yml'
    '{}' | Set-Content -LiteralPath $configuration
    $arguments='tunnel --config "'+$configuration+'" --no-autoupdate --metrics 127.0.0.1:0 --protocol http2 --url http://127.0.0.1:5000'
    $tunnel=Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $directory 'tunnel.log') -RedirectStandardError (Join-Path $directory 'tunnel-errors.log') -PassThru
    $entry=[pscustomobject]@{Pid=$tunnel.Id;CreationFileTime=$tunnel.StartTime.ToFileTimeUtc().ToString();ExecutablePath=$executable}
    @{Tunnel=$entry;Processes=@()} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $state
    $deadline=[DateTime]::UtcNow.AddSeconds(45);$origin=$null
    do{
        if($tunnel.HasExited){throw 'Temporary tunnel exited; inspect .artifacts/temporary-tunnel/tunnel-errors.log.'}
        $origin=Get-TemporaryTunnelOrigin (Get-Content -Raw (Join-Path $directory 'tunnel-errors.log'))
        if(-not $origin){Start-Sleep -Milliseconds 200}
    }until($origin -or [DateTime]::UtcNow -ge $deadline)
    if(-not $origin){throw 'No temporary HTTPS origin appeared within 45 seconds.'}
    $reservation.Stop()
    $services=& (Join-Path $PSScriptRoot 'Start-Local.ps1') -NoBuild:$NoBuild -PassThru -PublicOrigin $origin -MediaInterface $MediaInterface -HolePunching -LifecycleLock $localLock
    @{Tunnel=$entry;Processes=$services.Processes} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $state
    $deadline=[DateTime]::UtcNow.AddSeconds(45);$ready=$false
    do{
        try{$ready=(Invoke-RestMethod ($origin+'/health') -TimeoutSec 3).status -eq 'ready'}catch{$ready=$false}
        if(-not $ready){Start-Sleep -Milliseconds 300}
    }until($ready -or [DateTime]::UtcNow -ge $deadline)
    if(-not $ready){throw 'Tunnel website did not become reachable; media has not been tested.'}
    foreach($check in @(@{Path='/internal/bootstrap';Method='GET';Status=404},@{Path='/api/room';Method='GET';Status=401},@{Path='/media/rooms/11111111111111111111111111111111/streams/22222222222222222222222222222222/main/whep';Method='OPTIONS';Status=401})){
        $response=Invoke-WebRequest ($origin+$check.Path) -Method $check.Method -SkipHttpErrorCheck -TimeoutSec 5
        if([int]$response.StatusCode -ne $check.Status){throw "Public $($check.Method) $($check.Path) returned $([int]$response.StatusCode); expected $($check.Status)."}
    }
    Write-Host 'Temporary HTTPS website and anonymous-access boundary checks passed. External video remains untested; audio is disabled.'
    if($PassThru){return [pscustomobject]@{JoinLink=$services.JoinLink;LocalJoinLink=$services.LocalJoinLink;Processes=$services.Processes;Tunnel=$entry}}
    Write-Output ('Phone/friend link: '+$services.JoinLink)
    Write-Output ('Host PC native app link (also supports Watch in app): '+$services.JoinLink)
    Write-Output ('Optional localhost native control link: '+$services.LocalJoinLink)
    Write-Output 'Stop with scripts/Stop-TemporaryTunnel.ps1. This optional testing service adds Cloudflare website/signaling and Google STUN dependencies; no account, domain, relay or paid service is configured. CGNAT can still prevent video.'
}catch{
    if($services){& (Join-Path $PSScriptRoot 'Stop-Local.ps1')}
    if($tunnel -and -not $tunnel.HasExited){$tunnel.Kill();$tunnel.WaitForExit(5000)|Out-Null}
    if($tunnel -and (Test-Path $state)){Remove-Item -LiteralPath $state}
    throw
}finally{if($reservation){$reservation.Stop()};if($localLock){$localLock.Dispose()};$launchLock.Dispose()}
