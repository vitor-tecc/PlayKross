function Assert-DependencyHash {
    param([string]$Path,[ValidateSet('SHA256','SHA512')][string]$Algorithm,[string]$ExpectedHash)
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash
    if (-not [string]::Equals($actual, $ExpectedHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Dependency archive failed $Algorithm verification."
    }
}
function Expand-DependencyArchive {
    param([string]$Path,[string]$Destination)
    $root = [IO.Path]::GetFullPath($Destination).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entry in $zip.Entries) {
            $relative = $entry.FullName.Replace('/', [IO.Path]::DirectorySeparatorChar)
            $target = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $relative))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or $relative.Contains(':') -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
                throw 'Dependency archive contains an unsafe path or symbolic link.'
            }
        }
    } finally { $zip.Dispose() }
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($Path, $root, $true)
}
