#requires -Version 7.4
param([switch]$NoBuild, [switch]$PassThru, [Uri]$PublicOrigin, [string]$MediaInterface)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'MediaNetwork.ps1')
if($PublicOrigin -and $MediaInterface){throw 'Use MediaInterface for local browser testing, or PublicOrigin for public hosting; choose one mode.'}
$addresses=if($MediaInterface){@(Get-NetIPAddress -AddressFamily IPv4)}else{@()}
$mediaAddress=Resolve-MediaAddress -InterfaceAlias $MediaInterface -Addresses $addresses
$origin = if ($PublicOrigin) { $PublicOrigin } else { [Uri]'http://localhost:5000' }
if ($PublicOrigin -and (-not $origin.IsAbsoluteUri -or $origin.Scheme -ne 'https' -or $origin.IsLoopback -or $origin.AbsolutePath -ne '/' -or $origin.UserInfo -or $origin.Query -or $origin.Fragment -or $origin.Port -ne 443)) { throw 'PublicOrigin must be a complete public HTTPS origin on port 443, with no path, credentials, query or fragment.' }
$state = Join-Path $root '.runtime/local-services.local.json'
$logs = Join-Path $root '.artifacts/local'
New-Item -ItemType Directory -Force -Path (Join-Path $root '.runtime') | Out-Null
try { $launchLock = [IO.FileStream]::new((Join-Path $root '.runtime/local-launch.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch { throw 'Another local launch is in progress. Wait for it to finish before trying again.' }
try {
foreach ($port in @(5000,5001,8889,9997,9998)) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
    try { $listener.Start() }
    catch { throw "Port $port is occupied. Stop the application using it or run scripts/Stop-Local.ps1 for an existing JKiller host." }
    finally { $listener.Stop() }
}
$udp = [Net.Sockets.UdpClient]::new()
try { $udp.Client.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Parse($mediaAddress), 8189)) }
catch { throw 'Port 8189 UDP is occupied. Close its current owner before starting the local host.' }
finally { $udp.Dispose() }
if (Test-Path -LiteralPath $state) {
    $previous = Get-Content -Raw -LiteralPath $state | ConvertFrom-Json
    foreach ($entry in $previous) {
        $active = Get-Process -Id $entry.Pid -ErrorAction SilentlyContinue
        if ($active -and $active.StartTime.ToFileTimeUtc().ToString() -eq $entry.CreationFileTime) { throw 'An owned JKiller service is already running. Run scripts/Stop-Local.ps1 first.' }
    }
}
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
$media = Join-Path $root '.runtime/mediamtx/mediamtx.exe'
if (-not (Test-Path -LiteralPath $dotnet) -or -not (Test-Path -LiteralPath $media)) { throw 'Run scripts/Setup-Dev.ps1 first.' }
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$devEnvironment = @{ DOTNET_CLI_HOME=(Join-Path $root '.tools/dotnet-home'); NUGET_PACKAGES=(Join-Path $root '.tools/nuget'); DOTNET_CLI_TELEMETRY_OPTOUT='1' }
if (-not $NoBuild) {
    $build = Start-Process -FilePath $dotnet -ArgumentList @('build', 'src/JKiller.Host/JKiller.Host.csproj') -WorkingDirectory $root -WindowStyle Hidden -Environment $devEnvironment -RedirectStandardOutput (Join-Path $logs 'build.log') -RedirectStandardError (Join-Path $logs 'build-errors.log') -PassThru
    # Start-Process -Wait waits for descendants, including persistent SDK workers.
    # The host can start once its actual build process has exited successfully.
    if (-not $build.WaitForExit(120000)) { if (-not $build.HasExited) { $build.Kill(); $build.WaitForExit() }; throw 'Host build did not exit within two minutes; inspect .artifacts/local/build.log.' }
    if ($build.ExitCode -ne 0) { throw 'Host build failed; inspect .artifacts/local/build.log.' }
}
$hostDll = Join-Path $root 'src/JKiller.Host/bin/Debug/net10.0/JKiller.Host.dll'
if (-not (Test-Path -LiteralPath $hostDll)) { throw 'Host output is missing. Run Start-Local.ps1 without -NoBuild.' }
function New-PrivateSecret { return [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_') }
$bootstrap = New-PrivateSecret
$admin = New-PrivateSecret
$owned = [Collections.Generic.List[object]]::new()
function Track-Process($process, $role, $path) {
    $process.Refresh()
    if ($process.HasExited) { throw "$role exited during startup; inspect .artifacts/local/$role-errors.log." }
    $owned.Add([pscustomobject]@{ Role=$role; Pid=$process.Id; CreationFileTime=$process.StartTime.ToFileTimeUtc().ToString(); ExecutablePath=$path })
    ConvertTo-Json -InputObject @($owned.ToArray()) | Set-Content -LiteralPath $state -Encoding utf8
}
try {
    $hostEnvironment = $devEnvironment.Clone()
    $hostEnvironment['JKILLER_BOOTSTRAP_SECRET'] = $bootstrap
    $hostEnvironment['JKILLER_ADMIN_SECRET'] = $admin
    $hostEnvironment['PublicOrigin'] = $origin.GetLeftPart([UriPartial]::Authority)
    $hostProcess = Start-Process -FilePath $dotnet -ArgumentList ('"' + $hostDll + '"') -WorkingDirectory $root -WindowStyle Hidden -Environment $hostEnvironment -RedirectStandardOutput (Join-Path $logs 'host.log') -RedirectStandardError (Join-Path $logs 'host-errors.log') -PassThru
    Track-Process $hostProcess 'host' $dotnet
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if ($hostProcess.HasExited) { throw 'Host exited during startup; inspect .artifacts/local/host-errors.log.' }
        try { $ready = (Invoke-RestMethod 'http://127.0.0.1:5000/health' -TimeoutSec 1).status -eq 'ready' } catch { $ready = $false }
        if (-not $ready) { Start-Sleep -Milliseconds 150 }
    } until ($ready -or [DateTime]::UtcNow -ge $deadline)
    if (-not $ready) { throw 'Host did not become ready within 15 seconds.' }
    $configuration = Join-Path $root 'config/mediamtx.local.yml'
    if ($PublicOrigin) {
        $configuration = Join-Path $root '.runtime/mediamtx-public.generated.yml'
        $template = Get-Content -Raw -LiteralPath (Join-Path $root 'config/mediamtx.public.yml')
        $advertised = ConvertTo-Json -Compress -InputObject @($origin.DnsSafeHost, '127.0.0.1')
        $template.Replace('webrtcAdditionalHosts: [127.0.0.1]', ('webrtcAdditionalHosts: ' + $advertised)) | Set-Content -LiteralPath $configuration -Encoding utf8
    }
    elseif($MediaInterface){
        $configuration=Join-Path $root '.runtime/mediamtx-interface.generated.yml'
        $template=Get-Content -Raw -LiteralPath (Join-Path $root 'config/mediamtx.local.yml')
        $template.Replace('webrtcLocalUDPAddress: 127.0.0.1:8189',('webrtcLocalUDPAddress: '+$mediaAddress+':8189')).Replace('webrtcAdditionalHosts: [127.0.0.1]',('webrtcAdditionalHosts: ["'+$mediaAddress+'"]')) | Set-Content -LiteralPath $configuration -Encoding utf8
    }
    $mediaProcess = Start-Process -FilePath $media -ArgumentList ('"' + $configuration + '"') -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logs 'media.log') -RedirectStandardError (Join-Path $logs 'media-errors.log') -PassThru
    Track-Process $mediaProcess 'media' $media
    $adminHeaders = @{ Authorization='Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('jk-admin:' + $admin)) }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if ($mediaProcess.HasExited) { throw 'MediaMTX rejected startup; inspect .artifacts/local/media-errors.log and media.log.' }
        try { $inventory = Invoke-RestMethod 'http://127.0.0.1:9997/v3/webrtc/sessions/list' -Headers $adminHeaders -TimeoutSec 1; $ready = $true } catch { $ready = $false }
        if (-not $ready) { Start-Sleep -Milliseconds 150 }
    } until ($ready -or [DateTime]::UtcNow -ge $deadline)
    if (-not $ready) { throw 'Private MediaMTX authentication did not become ready; inspect local service logs.' }
    $invitation = Invoke-RestMethod 'http://127.0.0.1:5001/internal/bootstrap' -Headers @{ Authorization='Bearer ' + $bootstrap } -TimeoutSec 3
    $localLink = $invitation.joinLink.Replace($origin.GetLeftPart([UriPartial]::Authority), 'http://localhost:5000')
    if ($PassThru) { return [pscustomobject]@{ JoinLink=$invitation.joinLink; LocalJoinLink=$localLink; Processes=$owned.ToArray() } }
    Write-Output ('Room: ' + $invitation.joinLink)
    if ($PublicOrigin) { Write-Output ('Host PC native broadcaster link: ' + $localLink); Write-Output 'Public media UDP listener enabled. HTTPS proxy, router/firewall and external test must be configured separately; see docs/setup/internet.md.' }
    elseif($MediaInterface){Write-Output ('Website/admin remain on localhost. Authenticated encrypted media uses '+$MediaInterface+' ('+$mediaAddress+'):8189 UDP. No router/firewall changes were made. This is not internet hosting.')}
    else { Write-Output 'Services are local-only. If Firefox cannot load the stream on this PC, use -MediaInterface with your Ethernet/Wi-Fi interface; see docs/setup/windows.md.' }
    Write-Output 'Stop tracked services with pwsh -File scripts/Stop-Local.ps1.'
}
catch {
    if ($owned.Count -gt 0) { & (Join-Path $PSScriptRoot 'Stop-Local.ps1') }
    throw
}
}
finally { $launchLock.Dispose() }
