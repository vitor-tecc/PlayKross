$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../scripts/TemporaryTunnel.ps1')
$origin=Get-TemporaryTunnelOrigin "INFO documentation https://developers.cloudflare.com`nYour quick Tunnel has been created! https://calm-example-test.trycloudflare.com`n"
if($origin -cne 'https://calm-example-test.trycloudflare.com'){throw 'Did not extract the exact temporary HTTPS origin.'}
foreach($inputText in @('http://calm-test.trycloudflare.com','https://trycloudflare.com.evil.test','https://calm-test.trycloudflare.com.evil.test')){
    if(Get-TemporaryTunnelOrigin $inputText){throw 'Accepted a non-tunnel or misleading origin.'}
}
$rejected=$false;try{Get-TemporaryTunnelOrigin 'https://one.trycloudflare.com https://two.trycloudflare.com'|Out-Null}catch{$rejected=$true}
if(-not $rejected){throw 'Ambiguous origins accepted.'}
$current=Get-Process -Id $PID
$entry=[pscustomobject]@{Pid=$PID;CreationFileTime=$current.StartTime.ToFileTimeUtc().ToString();ExecutablePath=$current.Path}
if(-not (Test-OwnedTunnelProcess $entry $current.Path)){throw 'Matching process identity rejected.'}
$entry.CreationFileTime='0'
if(Test-OwnedTunnelProcess $entry $current.Path){throw 'Reused PID accepted.'}
$entry.CreationFileTime=$current.StartTime.ToFileTimeUtc().ToString()
if(Test-OwnedTunnelProcess $entry (Join-Path $PSScriptRoot 'different.exe')){throw 'Foreign executable accepted.'}
Write-Output 'TemporaryTunnelChecks: origin extraction, lookalike rejection, ambiguity and process identity passed.'
