# Shared by the package producer and the isolated GitHub publishing job.
function Get-ReleaseVersion {
    param([string]$RunId, [string]$Commit)
    if ($RunId -cnotmatch '^[1-9][0-9]{0,19}$' -or $Commit -cnotmatch '^[0-9a-f]{40}$') {
        throw 'A positive GitHub run ID and full lowercase source commit are required.'
    }
    "ci-$RunId-$($Commit.Substring(0, 12))"
}

function Get-ReleaseAssets {
    param([string]$Directory, [string]$Version)
    if ($Version -cnotmatch '^ci-[1-9][0-9]{0,19}-[0-9a-f]{12}$') { throw 'Invalid automatic release version.' }
    $name = "PlayKross-win-x64-$Version"
    $archive = Join-Path $Directory "$name.zip"
    $checksum = Join-Path $Directory "$name.sha256"
    $manifest = Join-Path $Directory "$name.manifest.json"
    foreach ($path in @($archive, $checksum, $manifest)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release asset is missing: $path" }
        if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Release assets must be ordinary files.' }
    }
    $record = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    if ($record.archive -cne "$name.zip" -or $record.bytes -ne (Get-Item -LiteralPath $archive).Length -or $record.sha256 -ne $hash) {
        throw 'Release archive differs from its manifest.'
    }
    if ((Get-Content -LiteralPath $checksum -Raw).Trim() -cne "$hash  $name.zip") { throw 'Release checksum does not match the archive.' }
    $archive
    $checksum
    $manifest
}
