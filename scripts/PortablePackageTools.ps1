# Shared archive/file boundaries; dot-sourcing this file never builds or launches anything.
function Test-PortableNativeSourcePath([string]$Path) {
    if($Path -match '(^|/)\.\.(/|$)'){return $false}
    return $Path -match '^(?:(?:src|include|tests)/[^/]+\.(?:cpp|hpp|h)|tests/audio-harness/(?:ModuleState\.hpp|obs-frontend-api\.h|util/platform\.h)|tests/pipeline-harness/(?:ModuleState\.hpp|NativeDiagnostics\.hpp|util/platform\.h)|CMakeLists\.txt|LICENSE|README\.md|NOTICE\.md)$'
}
function Assert-PortableRelativePath {
    param([Parameter(Mandatory)][string]$Path)
    $normalized = $Path.Replace('\', '/').TrimEnd('/')
    if (-not $normalized -or $normalized.StartsWith('/') -or $normalized.Contains(':') -or $normalized.Contains([char]0)) {
        throw "Unsafe portable path: $Path"
    }
    foreach ($part in $normalized.Split('/')) {
        if (-not $part -or $part -in @('.', '..') -or $part.TrimEnd(' ', '.') -ne $part -or $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { throw "Unsafe portable path: $Path" }
    }
}

function Assert-PortableEntry {
    param([Parameter(Mandatory)][string]$Path, [int]$ExternalAttributes = 0, [Collections.Generic.HashSet[string]]$AllowedPaths)
    Assert-PortableRelativePath -Path $Path
    if ((($ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000 -or ($ExternalAttributes -band [int][IO.FileAttributes]::ReparsePoint)) {
        throw "Portable archives cannot contain symbolic links: $Path"
    }
    if ($null -ne $AllowedPaths -and -not $AllowedPaths.Contains($Path.Replace('\', '/').TrimEnd('/'))) {
        throw "Unallowlisted portable file: $Path"
    }
}

function Assert-PortableDirectory {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Root)
    $boundary = [IO.Path]::GetFullPath($Root).TrimEnd('\','/')
    $resolved = [IO.Path]::GetFullPath($Path)
    if ($resolved -ne $boundary -and -not $resolved.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Portable source leaves its boundary: $Path"
    }
    for ($current = $resolved; $current; $current = [IO.Path]::GetDirectoryName($current)) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Portable source contains a junction or symbolic link: $Path" }
        if ([string]::Equals($current.TrimEnd('\','/'), $boundary, [StringComparison]::OrdinalIgnoreCase)) { break }
    }
}

function Assert-PortableFile {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Root)
    Assert-PortableDirectory -Path $Path -Root $Root
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer) { throw "Expected a portable file: $Path" }
}

function Expand-PortableObsArchive {
    param([Parameter(Mandatory)][string]$Archive, [Parameter(Mandatory)][string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        # Validate the entire archive before creating any output, even ignored entries.
        foreach ($entry in $zip.Entries) {
            Assert-PortableEntry -Path $entry.FullName -ExternalAttributes $entry.ExternalAttributes
            if (-not $seen.Add($entry.FullName.Replace('\', '/').TrimEnd('/'))) { throw 'Duplicate OBS archive path.' }
        }
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name.Split('/')[0] -notin @('bin', 'data', 'obs-plugins') -or $name.EndsWith('/') -or $name.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)) { continue }
            $target = Join-Path $Destination $name
            New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        }
    } finally { $zip.Dispose() }
}
