$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot '../../scripts/PortablePackageTools.ps1'
if (-not (Test-Path -LiteralPath $helper)) { throw 'Portable packaging safety implementation is missing.' }
. $helper
Add-Type -AssemblyName System.IO.Compression.FileSystem

# A missing path boundary could extract attacker-controlled archive entries outside staging.
foreach ($path in @('../escape.dll', 'bin/../../escape.dll', '/absolute.dll', 'C:/private.dll', 'bin/file.dll:secret', 'bin\..\escape.dll', 'bin/NUL.dll')) {
    $rejected = $false
    try { Assert-PortableRelativePath -Path $path } catch { $rejected = $true }
    if (-not $rejected) { throw "Unsafe archive path accepted: $path" }
}
Assert-PortableRelativePath -Path 'bin/64bit/obs64.exe'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('playkross-portable-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $archive = Join-Path $fixture 'upstream.zip'
    $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @('bin/64bit/obs64.exe','data/obs-studio/license.txt','obs-plugins/64bit/test.dll','config/obs-studio/basic/profiles/old/service.json','logs/private.log','.git/config')) {
            $entry = $zip.CreateEntry($name)
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write('fixture') } finally { $writer.Dispose() }
        }
    } finally { $zip.Dispose() }
    $destination = Join-Path $fixture 'clean'
    Expand-PortableObsArchive -Archive $archive -Destination $destination
    foreach ($name in @('bin/64bit/obs64.exe','data/obs-studio/license.txt','obs-plugins/64bit/test.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $destination $name))) { throw "Required clean OBS payload omitted: $name" }
    }
    foreach ($name in @('config','logs','.git')) {
        if (Test-Path -LiteralPath (Join-Path $destination $name)) { throw "Private state included: $name" }
    }

    # Entry-name validation must happen before extraction, including ignored paths.
    foreach ($case in @(@{ Name='../escape.dll'; Attributes=0 }, @{ Name='bin/64bit/link.dll'; Attributes=([int]0xA000 -shl 16) })) {
        $badArchive = Join-Path $fixture ('bad-' + [guid]::NewGuid().ToString('N') + '.zip')
        $zip = [IO.Compression.ZipFile]::Open($badArchive, [IO.Compression.ZipArchiveMode]::Create)
        try { $entry = $zip.CreateEntry($case.Name); $entry.ExternalAttributes = $case.Attributes } finally { $zip.Dispose() }
        $rejected = $false
        try { Expand-PortableObsArchive -Archive $badArchive -Destination (Join-Path $fixture 'unsafe') } catch { $rejected = $true }
        if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $fixture 'escape.dll'))) { throw 'Unsafe ZIP entry was extracted.' }
    }

    # A source junction can leak files even when its leaf filename looks valid.
    $foreign = Join-Path $fixture 'foreign'
    New-Item -ItemType Directory -Path $foreign | Out-Null
    [IO.File]::WriteAllText((Join-Path $foreign 'secret.dll'), 'private')
    $link = Join-Path $fixture 'junction'
    New-Item -ItemType Junction -Path $link -Target $foreign | Out-Null
    $rejected = $false
    try { Assert-PortableFile -Path (Join-Path $link 'secret.dll') -Root $fixture } catch { $rejected = $true }
    if (-not $rejected) { throw 'Packaging followed a source junction.' }
    [IO.Directory]::Delete($link)

    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$allowed.Add('PlayKross.exe')
    $rejected = $false
    try { Assert-PortableEntry -Path 'UserSession.json' -AllowedPaths $allowed } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unallowlisted session state entered the release.' }
    Assert-PortableEntry -Path 'PlayKross.exe' -AllowedPaths $allowed
    Write-Output 'PortablePackageChecks: traversal, symbolic links, source junctions, clean OBS extraction and payload allowlist passed.'
} finally {
    $link = Join-Path $fixture 'junction'
    if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
    $resolved = [IO.Path]::GetFullPath($fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
