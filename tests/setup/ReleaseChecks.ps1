$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../scripts/ReleaseTools.ps1')

function Assert-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid release input was accepted.' }
}

$commit = 'abcdef0123456789abcdef0123456789abcdef01'
$version = Get-ReleaseVersion -RunId '12345' -Commit $commit
if ($version -cne 'ci-12345-abcdef012345') { throw 'Release identity mismatch.' }
foreach ($bad in @('', '0', '../123', '12;whoami', '1`n2')) {
    Assert-Rejected { Get-ReleaseVersion -RunId $bad -Commit $commit }
}
Assert-Rejected { Get-ReleaseVersion -RunId '123' -Commit '../main' }

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('playkross-release-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $name = "PlayKross-win-x64-$version"
    $zip = Join-Path $fixture "$name.zip"
    [IO.File]::WriteAllText($zip, 'package fixture')
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    $manifest = @{ archive="$name.zip"; bytes=(Get-Item $zip).Length; sha256=$hash }
    $manifest | ConvertTo-Json | Set-Content (Join-Path $fixture "$name.manifest.json")
    "$hash  $name.zip" | Set-Content (Join-Path $fixture "$name.sha256")
    $assets = @(Get-ReleaseAssets -Directory $fixture -Version $version)
    if ($assets.Count -ne 3 -or @($assets | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count) { throw 'Expected exactly three release files.' }
    Assert-Rejected { Get-ReleaseAssets -Directory $fixture -Version '../escape' }
    [IO.File]::AppendAllText($zip, 'tampered')
    Assert-Rejected { Get-ReleaseAssets -Directory $fixture -Version $version }
    [IO.File]::WriteAllText($zip, 'package fixture')
    $manifest.archive = '../outside.zip'
    $manifest | ConvertTo-Json | Set-Content (Join-Path $fixture "$name.manifest.json")
    Assert-Rejected { Get-ReleaseAssets -Directory $fixture -Version $version }
    $manifest.archive = "$name.zip"
    $manifest | ConvertTo-Json | Set-Content (Join-Path $fixture "$name.manifest.json")
    "$('0' * 64)  $name.zip" | Set-Content (Join-Path $fixture "$name.sha256")
    Assert-Rejected { Get-ReleaseAssets -Directory $fixture -Version $version }
    Remove-Item -LiteralPath (Join-Path $fixture "$name.sha256")
    Assert-Rejected { Get-ReleaseAssets -Directory $fixture -Version $version }
    Write-Output 'ReleaseChecks: identity, exact asset set, corruption, checksum, manifest and missing-file checks passed.'
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe release fixture cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
