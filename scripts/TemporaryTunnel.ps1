function New-TemporaryTunnelPortReservation {
    param([int]$Port=5000)
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port)
    try{$listener.Start();return $listener}
    catch{$listener.Stop();throw 'Tunnel target port is occupied; refusing to expose its current owner.'}
}
function Assert-ExpectedTunnelRun {
    param($Current,$Expected)
    if($Expected -and ($Current.Pid -ne $Expected.Pid -or $Current.CreationFileTime -ne $Expected.CreationFileTime -or $Current.ExecutablePath -ne $Expected.ExecutablePath)){throw 'Tunnel ownership changed; refusing to stop a replacement run.'}
}
function Get-TemporaryTunnelOrigin {
    param([string]$LogText)
    $origins=@([regex]::Matches($LogText,'https://[a-z0-9]+(?:-[a-z0-9]+)*\.trycloudflare\.com(?=[\s|]|$)') | ForEach-Object Value | Sort-Object -Unique)
    if($origins.Count -gt 1){throw 'Tunnel log contains multiple origins; refusing an ambiguous address.'}
    if($origins.Count -eq 1){return $origins[0]}
    return $null
}

function Test-OwnedTunnelProcess {
    param($Entry,[string]$ExpectedExecutable)
    if([IO.Path]::GetFullPath($Entry.ExecutablePath) -ne [IO.Path]::GetFullPath($ExpectedExecutable)){return $false}
    $process=Get-Process -Id $Entry.Pid -ErrorAction SilentlyContinue
    return $process -and $process.StartTime.ToFileTimeUtc().ToString() -eq $Entry.CreationFileTime -and $process.Path -eq $ExpectedExecutable
}
