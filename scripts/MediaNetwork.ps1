function New-HolePunchingMediaConfig {
    param([string]$Template,[string]$InterfaceAlias)
    if([string]::IsNullOrWhiteSpace($InterfaceAlias) -or $InterfaceAlias -match '[\x00-\x1f\x7f]'){throw 'An exact, printable media interface alias is required.'}
    $replacements=[ordered]@{
        webrtcLocalUDPAddress='""'
        webrtcLocalTCPAddress='""'
        webrtcIPsFromInterfaces='true'
        webrtcAdditionalHosts='[]'
        webrtcICEServers2=(ConvertTo-Json -Compress -InputObject @(@{url='stun:stun.l.google.com:19302'}))
    }
    foreach($key in $replacements.Keys){
        $pattern='(?m)^'+[regex]::Escape($key)+':[^\r\n]*'
        if([regex]::Matches($Template,$pattern).Count -ne 1){throw "Media template must contain exactly one $key setting."}
        $line=$key+': '+$replacements[$key]
        $Template=[regex]::Replace($Template,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($match) $line})
    }
    if($Template -match '(?m)^webrtcIPsFromInterfacesList:'){throw 'Media template already defines an interface list.'}
    $interfaces=ConvertTo-Json -Compress -InputObject @($InterfaceAlias)
    return $Template+"`nwebrtcIPsFromInterfacesList: $interfaces`n"
}

function Resolve-MediaAddress {
    param([string]$InterfaceAlias,[object[]]$Addresses)
    if([string]::IsNullOrWhiteSpace($InterfaceAlias)){return '127.0.0.1'}
    $usable=@($Addresses | Where-Object { $_.InterfaceAlias -eq $InterfaceAlias -and $_.AddressState.ToString() -eq 'Preferred' } | ForEach-Object {
        $address=$null
        if([Net.IPAddress]::TryParse($_.IPAddress,[ref]$address) -and $address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork){
            $bytes=$address.GetAddressBytes()
            if($bytes[0] -ne 0 -and $bytes[0] -ne 127 -and $bytes[0] -lt 224 -and -not($bytes[0] -eq 169 -and $bytes[1] -eq 254)){ $address.ToString() }
        }
    } | Sort-Object -Unique)
    if($usable.Count -ne 1){throw 'MediaInterface must exactly name an interface with one preferred unicast IPv4 address. Check Get-NetIPAddress -AddressFamily IPv4.'}
    return $usable[0]
}
