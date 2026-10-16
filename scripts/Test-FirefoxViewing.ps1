#requires -Version 7.4
param([string]$MediaInterface='Ethernet',[string]$FirefoxPath,[switch]$VideoOnly)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
New-Item -ItemType Directory -Force (Join-Path $root '.runtime') | Out-Null
try{$probeLock=[IO.FileStream]::new((Join-Path $root '.runtime/live-flow-probe.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Another live-flow probe owns the shared controls.'}
try{
. (Join-Path $PSScriptRoot 'DependencyTools.ps1')
$env:DOTNET_CLI_HOME=Join-Path $root '.tools/dotnet-home';$env:NUGET_PACKAGES=Join-Path $root '.tools/nuget'
if(-not $FirefoxPath){
    $FirefoxPath=@((Join-Path $env:LOCALAPPDATA 'Mozilla Firefox/firefox.exe'),'C:/Program Files/Mozilla Firefox/firefox.exe','C:/Program Files (x86)/Mozilla Firefox/firefox.exe') | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
}
if(-not $FirefoxPath -or -not(Test-Path -LiteralPath $FirefoxPath)){throw 'Install Firefox or supply -FirefoxPath. Its existing user profile is never used.'}
$driverRoot=Join-Path $root '.tools/geckodriver';$archive=Join-Path $driverRoot 'geckodriver-v0.37.1-win64.zip'
New-Item -ItemType Directory -Force $driverRoot | Out-Null
if(-not(Test-Path -LiteralPath $archive)){Invoke-WebRequest 'https://github.com/mozilla/geckodriver/releases/download/v0.37.1/geckodriver-v0.37.1-win64.zip' -OutFile $archive}
Assert-DependencyHash -Path $archive -Algorithm SHA256 -ExpectedHash 'dfed9315abe8d2fbc1b6161a2ee8002452e79cf05ee92fdc653a4e26bc35edd8'
Expand-DependencyArchive -Path $archive -Destination $driverRoot
$directory=Join-Path $root '.artifacts/firefox-viewing';$profiles=Join-Path $directory 'profiles';New-Item -ItemType Directory -Force $profiles | Out-Null
$flow=Join-Path $root '.artifacts/live-flow';New-Item -ItemType Directory -Force $flow | Out-Null
$services=$null;$fixture=$null;$publisher=$null;$driver=$null;$session=$null
function DriverRequest([string]$Method,[string]$Path,$Body=$null){
    $parameters=@{Uri=('http://127.0.0.1:4844'+$Path);Method=$Method;TimeoutSec=45}
    if($null -ne $Body){$parameters.ContentType='application/json';$parameters.Body=$Body|ConvertTo-Json -Depth 12 -Compress}
    try{(Invoke-RestMethod @parameters).value}catch{throw 'Firefox WebDriver request failed; inspect the local driver log. Request bodies/credentials are omitted.'}
}
function Execute([string]$Script){DriverRequest POST ('/session/'+$session+'/execute/sync') @{script=$Script;args=@()}}
function Until([scriptblock]$Check,[int]$Seconds=30){$timer=[Diagnostics.Stopwatch]::StartNew();do{if(& $Check){return};Start-Sleep -Milliseconds 200}while($timer.Elapsed.TotalSeconds -lt $Seconds);throw 'Firefox playback deadline exceeded. Check the selected media interface and stream status.'}
function ClickButton([string]$Name){
    $element=DriverRequest POST ('/session/'+$session+'/execute/sync') @{script='return [...document.querySelectorAll("button")].find(b=>b.textContent===arguments[0]);';args=@($Name)}
    if(-not $element){throw "Missing viewer button: $Name"}
    DriverRequest POST ('/session/'+$session+'/element/'+$element.'element-6066-11e4-a52e-4f735466cecf'+'/click') @{}|Out-Null
}
Push-Location $root
try{
    & (Join-Path $root '.tools/dotnet/dotnet.exe') build tools/acceptance/LiveFlowProbe --nologo | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Native publisher probe build failed.'}
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,4844);try{$listener.Start()}finally{$listener.Stop()}
    $services=& (Join-Path $PSScriptRoot 'Start-Local.ps1') -MediaInterface $MediaInterface -PassThru
    foreach($name in @('ready.json','next-preset','stop')){Remove-Item -LiteralPath (Join-Path $flow $name) -ErrorAction SilentlyContinue}
    $fixture=Start-Process -FilePath (Join-Path $root '.artifacts/native/Debug/CaptureFixture.exe') -WindowStyle Hidden -PassThru;Start-Sleep -Milliseconds 500
    $environment=@{JKILLER_PROBE_JOIN_LINK=$services.JoinLink;JKILLER_TARGET_PID=$fixture.Id.ToString();DOTNET_CLI_HOME=$env:DOTNET_CLI_HOME;NUGET_PACKAGES=$env:NUGET_PACKAGES}
    $environment['JKILLER_PROBE_CAPTURE_MODE']=if($VideoOnly){'video-only'}else{'application'}
    $publisher=Start-Process -FilePath (Join-Path $root '.tools/dotnet/dotnet.exe') -ArgumentList ('"'+(Join-Path $root 'tools/acceptance/LiveFlowProbe/bin/Debug/net10.0-windows10.0.26100.0/LiveFlowProbe.dll')+'"') -WorkingDirectory $root -WindowStyle Hidden -Environment $environment -RedirectStandardOutput (Join-Path $directory 'publisher.log') -RedirectStandardError (Join-Path $directory 'publisher-errors.log') -PassThru
    Until {if($publisher.HasExited){throw 'Synthetic publisher failed; inspect publisher-errors.log.'};Test-Path (Join-Path $flow 'ready.json')} 40
    $driver=Start-Process -FilePath (Join-Path $driverRoot 'geckodriver.exe') -ArgumentList ('--host 127.0.0.1 --port 4844 --log fatal --profile-root "'+$profiles+'"') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $directory 'driver.log') -RedirectStandardError (Join-Path $directory 'driver-errors.log') -PassThru
    Until {try{(DriverRequest GET '/status').ready}catch{$false}} 10
    $created=DriverRequest POST '/session' @{capabilities=@{alwaysMatch=@{browserName='firefox';'moz:firefoxOptions'=@{binary=$FirefoxPath;args=@('-headless')}}}}
    $session=$created.sessionId;DriverRequest POST ('/session/'+$session+'/timeouts') @{script=20000;pageLoad=30000;implicit=0}|Out-Null
    DriverRequest POST ('/session/'+$session+'/url') @{url=$services.JoinLink}|Out-Null
    Until {Execute 'return !!document.querySelector("#nickname");'}
    Execute 'history.replaceState(null,"","/");window.jkProbePeers=[];const Original=window.RTCPeerConnection;window.RTCPeerConnection=new Proxy(Original,{construct(Target,args){const peer=Reflect.construct(Target,args,Target);jkProbePeers.push(peer);return peer;}});const input=document.querySelector("#nickname");Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,"value").set.call(input,"Firefox probe viewer");input.dispatchEvent(new Event("input",{bubbles:true}));return true;'|Out-Null
    $phase='join';ClickButton 'Join room';Until {Execute 'return [...document.querySelectorAll("button")].some(b=>b.textContent==="Watch stream");'}
    $reports=@()
    foreach($height in @(720,1080)){
        if($height -eq 1080){
            Set-Content -LiteralPath (Join-Path $flow 'next-preset') -Value continue
            Until {if($publisher.HasExited){throw 'Publisher exited during reconfiguration.'};(Get-Content (Join-Path $flow 'ready.json') -Raw|ConvertFrom-Json).height -eq 1080} 35
            Until {Execute 'return !!document.querySelector("video") && document.querySelector("video").videoHeight===1080;'} 30
        }else{$phase='720p first frames';ClickButton 'Watch stream';Until {Execute 'return document.querySelector("video")?.videoHeight===720;'} 30;ClickButton 'Enable sound'}
        Execute 'window.jkProbePresented=0;const v=document.querySelector("video");v.scrollIntoView({block:"center"});const frame=()=>{jkProbePresented++;v.requestVideoFrameCallback(frame);};v.requestVideoFrameCallback(frame);return true;'|Out-Null
        $phase='numeric frame evidence';Start-Sleep -Seconds 3
        $metricsScript=@'
const done=arguments[arguments.length-1];const peer=jkProbePeers.find(p=>p.connectionState==='connected');if(!peer){done({error:'No connected receiver'});return;}peer.getStats().then(stats=>{const all=[...stats.values()];const video=all.find(s=>s.type==='inbound-rtp'&&s.kind==='video');const audio=all.find(s=>s.type==='inbound-rtp'&&s.kind==='audio');const element=document.querySelector('video');const canvas=document.createElement('canvas');canvas.width=32;canvas.height=32;const ctx=canvas.getContext('2d');ctx.drawImage(element,0,0,32,32);const pixels=ctx.getImageData(0,0,32,32).data;let sum=0,squares=0;for(let i=0;i<pixels.length;i+=4){const value=(pixels[i]+pixels[i+1]+pixels[i+2])/3;sum+=value;squares+=value*value;}done({width:element.videoWidth,height:element.videoHeight,decodedFrames:video?.framesDecoded,presentedFrames:window.jkProbePresented||null,presentationMethod:window.jkProbePresented>0?"requestVideoFrameCallback count":"unavailable in this headless Firefox run",videoCodec:stats.get(video?.codecId)?.mimeType,audioCodec:stats.get(audio?.codecId)?.mimeType,audioPackets:audio?.packetsReceived,audioEnergy:audio?.totalAudioEnergy,pixelLumaVariance:squares/1024-(sum/1024)**2,connection:peer.connectionState,ice:peer.iceConnectionState,check:'short-real-synthetic-smoke',topology:'same-PC Ethernet media; localhost signaling'});}).catch(()=>done({error:'Stats unavailable'}));
'@
        $metrics=DriverRequest POST ('/session/'+$session+'/execute/async') @{script=$metricsScript;args=@()}
        $initial=$metrics;Start-Sleep -Seconds 1
        $metrics=DriverRequest POST ('/session/'+$session+'/execute/async') @{script=$metricsScript;args=@()}
        $metrics|Add-Member -NotePropertyName decodedFrameProgress -NotePropertyValue ($metrics.decodedFrames-$initial.decodedFrames)
        if($metrics.height -ne $height -or $metrics.width -ne $(if($height -eq 720){1280}else{1920}) -or $metrics.decodedFrames -le 0 -or $metrics.decodedFrameProgress -lt 10 -or $metrics.videoCodec -ne 'video/H264' -or $metrics.audioCodec -ne 'audio/opus' -or $metrics.audioPackets -le 0 -or $metrics.pixelLumaVariance -le 1){throw 'Firefox real video/audio validation failed.'}
        $reports+=@{browserVersion=$created.capabilities.browserVersion;metrics=$metrics;captureMode=$environment['JKILLER_PROBE_CAPTURE_MODE'];audioValidation=if($VideoOnly){'pending; application audio disabled'}else{'short delivery only; Discord and sync pending'}}
        $reports|ConvertTo-Json -Depth 8|Set-Content (Join-Path $directory 'numeric-results.json')
        $metrics|ConvertTo-Json -Depth 4
    }
    ClickButton 'Fullscreen';Until {Execute 'return !!document.fullscreenElement;'} 5
    ClickButton 'Return to room';Until {Execute 'return !document.fullscreenElement;'} 5
    Write-Output 'PASS: installed Firefox with default preferences received actual 720p/1080p H.264/Opus, varied pixels, progressing decoded frames, sound selection and fullscreen return. No private profile or capture recording used.'
}catch{
    if($session){try{$snapshot=Execute 'return {notice:document.querySelector(".notice")?.textContent,status:document.querySelector(".stream-status")?.textContent,nickname:document.querySelector("#nickname")?.value,buttons:[...document.querySelectorAll("button")].map(b=>b.textContent),width:document.querySelector("video")?.videoWidth,height:document.querySelector("video")?.videoHeight,qualityFrames:document.querySelector("video")?.getVideoPlaybackQuality().totalVideoFrames,frameCallbacks:window.jkProbePresented,visibility:document.visibilityState,videoTop:document.querySelector("video")?.getBoundingClientRect().top,videoBottom:document.querySelector("video")?.getBoundingClientRect().bottom};';@{stage=$phase;state=$snapshot}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $directory 'failure.json');@{stage=$phase;state=$snapshot}|ConvertTo-Json -Depth 4}catch{}}
    throw
}finally{
    if($session){try{DriverRequest DELETE ('/session/'+$session)|Out-Null}catch{}}
    if($driver -and -not $driver.HasExited){$driver.Kill($true);$driver.WaitForExit(3000)|Out-Null}
    if($publisher){Set-Content -LiteralPath (Join-Path $flow 'stop') -Value stop}
    if($publisher -and -not $publisher.HasExited){$publisher.WaitForExit(15000)|Out-Null;if(-not $publisher.HasExited){$publisher.Kill($true);$publisher.WaitForExit(3000)|Out-Null}}
    if($fixture -and -not $fixture.HasExited){$fixture.Kill();$fixture.WaitForExit(2000)|Out-Null}
    if($services){& (Join-Path $PSScriptRoot 'Stop-Local.ps1')};Pop-Location
}
}finally{$probeLock.Dispose()}
