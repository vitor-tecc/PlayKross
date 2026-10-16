$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../scripts/DependencyTools.ps1')
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('jkiller-setup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $corrupt = Join-Path $testRoot 'corrupt.zip'
    [IO.File]::WriteAllText($corrupt, 'not the approved archive')
    $failed = $false
    try { Assert-DependencyHash -Path $corrupt -Algorithm SHA256 -ExpectedHash ('0' * 64) } catch { $failed = $true }
    if (-not $failed) { throw 'Corrupt archive accepted' }
    Add-Type -AssemblyName System.IO.Compression
    $zipPath = Join-Path $testRoot 'traversal.zip'
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    $entry = $zip.CreateEntry('../escaped.txt')
    $writer = [IO.StreamWriter]::new($entry.Open())
    $writer.Write('must not escape')
    $writer.Dispose()
    $zip.Dispose()
    $failed = $false
    try { Expand-DependencyArchive -Path $zipPath -Destination (Join-Path $testRoot 'output') } catch { $failed = $true }
    if (-not $failed -or (Test-Path -LiteralPath (Join-Path $testRoot 'escaped.txt'))) { throw 'Traversal archive was not rejected safely' }
    Write-Output 'SetupChecks: corrupt and traversal archives rejected; no escaped file.'
} finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup target' }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
