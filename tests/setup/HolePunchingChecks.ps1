$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../scripts/MediaNetwork.ps1')
$template=Get-Content -Raw (Join-Path $PSScriptRoot '../../config/mediamtx.public.yml')
# A tunnel hostname is a website endpoint, never a media candidate.
$result=New-HolePunchingMediaConfig -Template $template -InterfaceAlias 'Ethernet'
function Value($text,$key){
    $matches=[regex]::Matches($text,'(?m)^'+[regex]::Escape($key)+':\s*(.*)$')
    if($matches.Count -ne 1){throw "Expected one $key setting."}
    return $matches[0].Groups[1].Value.Trim()
}
if((Value $result 'webrtcLocalUDPAddress') -ne '""'){throw 'Static UDP prevents the intended random-port STUN profile.'}
if((Value $result 'webrtcLocalTCPAddress') -ne '""'){throw 'Unexpected public TCP media listener.'}
if((Value $result 'webrtcIPsFromInterfaces') -ne 'true'){throw 'Selected interface candidates are missing.'}
$interfaces=Value $result 'webrtcIPsFromInterfacesList' | ConvertFrom-Json -NoEnumerate
if($interfaces.Count -ne 1 -or $interfaces[0] -cne 'Ethernet'){throw 'Unselected VPN/interface candidates can leak into media setup.'}
if((Value $result 'webrtcAdditionalHosts') -ne '[]'){throw 'Tunnel website must not be advertised as a UDP media host.'}
$servers=Value $result 'webrtcICEServers2' | ConvertFrom-Json -NoEnumerate
if($servers.Count -ne 1 -or $servers[0].url -ne 'stun:stun.l.google.com:19302'){throw 'STUN candidate discovery is missing.'}
foreach($key in @('authMethod','authHTTPAddress','authHTTPExclude','apiAddress','metricsAddress','webrtcAddress','writeQueueSize')){
    if((Value $result $key) -ne (Value $template $key)){throw "Connectivity profile changed the private/authenticated $key boundary."}
}
foreach($bad in @('',"Ethernet`nwebrtcAddress: :8889")){
    $rejected=$false;try{New-HolePunchingMediaConfig -Template $template -InterfaceAlias $bad|Out-Null}catch{$rejected=$true}
    if(-not $rejected){throw 'Invalid interface alias accepted.'}
}
foreach($bad in @($template.Replace('webrtcICEServers2: []',''),($template+"`nwebrtcICEServers2: []"))){
    $rejected=$false;try{New-HolePunchingMediaConfig -Template $bad -InterfaceAlias 'Ethernet'|Out-Null}catch{$rejected=$true}
    if(-not $rejected){throw 'Ambiguous/incomplete template silently accepted.'}
}
$quoted=New-HolePunchingMediaConfig -Template $template -InterfaceAlias 'Ethernet "special"'
$alias=Value $quoted 'webrtcIPsFromInterfacesList' | ConvertFrom-Json -NoEnumerate
if($alias[0] -cne 'Ethernet "special"'){throw 'Interface alias was not safely serialized.'}
Write-Output 'HolePunchingChecks: random UDP, exact interface, STUN, private auth/listeners and invalid-template rejection passed.'
