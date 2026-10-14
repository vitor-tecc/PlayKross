$ErrorActionPreference='Stop'
$helper=Join-Path $PSScriptRoot '../../scripts/MediaNetwork.ps1'
if(-not(Test-Path $helper)){throw 'Media interface selection is missing; Firefox cannot select a usable local media address.'}
. $helper
$addresses=@(
    [pscustomobject]@{InterfaceAlias='Ethernet';AddressState='Preferred';IPAddress='192.168.15.40'},
    [pscustomobject]@{InterfaceAlias='Radmin VPN';AddressState='Preferred';IPAddress='26.1.2.3'},
    [pscustomobject]@{InterfaceAlias='Ethernet';AddressState='Deprecated';IPAddress='192.168.15.41'},
    [pscustomobject]@{InterfaceAlias='Ethernet';AddressState='Preferred';IPAddress='fe80::1'}
)
if((Resolve-MediaAddress -InterfaceAlias '' -Addresses $addresses) -ne '127.0.0.1'){throw 'Default unexpectedly opens a network interface.'}
if((Resolve-MediaAddress -InterfaceAlias 'Ethernet' -Addresses $addresses) -ne '192.168.15.40'){throw 'Explicit interface selected a VPN, IPv6 or deprecated address.'}
$rejected=$false;try{Resolve-MediaAddress -InterfaceAlias 'Ether*' -Addresses $addresses|Out-Null}catch{$rejected=$true};if(-not $rejected){throw 'Wildcard interface accepted.'}
foreach($bad in @('0.0.0.0','127.0.0.1','169.254.1.1','224.0.0.1','255.255.255.255','not-an-ip')){
    $rejected=$false;try{Resolve-MediaAddress -InterfaceAlias 'Ethernet' -Addresses @([pscustomobject]@{InterfaceAlias='Ethernet';AddressState='Preferred';IPAddress=$bad})|Out-Null}catch{$rejected=$true};if(-not $rejected){throw "Invalid unicast address accepted: $bad"}
}
$ambiguous=$addresses+@([pscustomobject]@{InterfaceAlias='Ethernet';AddressState='Preferred';IPAddress='192.168.15.42'})
$rejected=$false;try{Resolve-MediaAddress -InterfaceAlias 'Ethernet' -Addresses $ambiguous|Out-Null}catch{$rejected=$true};if(-not $rejected){throw 'Ambiguous interface silently selected an arbitrary IP.'}
Write-Output 'MediaInterfaceChecks: private default, exact interface selection, invalid-address rejection and ambiguity rejection passed.'
