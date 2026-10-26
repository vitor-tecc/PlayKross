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
    "$hash  $name.zip" | Set-Content (Join-Path $fixture "$name.sha256")

    # Exercise the real publisher with only the remote GitHub CLI boundary substituted.
    $environmentNames = @('GITHUB_RUN_ID','GITHUB_SHA','GITHUB_REPOSITORY','GITHUB_REF','GITHUB_EVENT_NAME','GITHUB_SERVER_URL','GITHUB_STEP_SUMMARY')
    $savedEnvironment = @{}
    foreach ($key in $environmentNames) { $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key) }
    try {
        $env:GITHUB_RUN_ID = '12345'; $env:GITHUB_SHA = $commit
        $env:GITHUB_REPOSITORY = 'fixture/playkross'; $env:GITHUB_REF = 'refs/heads/main'
        $env:GITHUB_EVENT_NAME = 'push'; $env:GITHUB_SERVER_URL = 'https://github.com'
        $env:GITHUB_STEP_SUMMARY = Join-Path $fixture 'summary.md'
        $releaseFixture = @{ Calls = @(); FailAt = ''; RemoteReleases = '[[]]' }
        function gh {
            $releaseFixture.Calls += ,@($args)
            $global:LASTEXITCODE = if ($args[0] -eq $releaseFixture.FailAt -or $args[1] -eq $releaseFixture.FailAt) { 1 } else { 0 }
            if ($args[0] -eq 'api') { $releaseFixture.RemoteReleases }
        }
        $publisher = Join-Path $PSScriptRoot '../../scripts/Publish-Release.ps1'
        $releaseFixture.Calls = @(); $releaseFixture.FailAt = ''; $releaseFixture.RemoteReleases = '[[]]'
        & $publisher -Directory $fixture | Out-Null
        if ((($releaseFixture.Calls | ForEach-Object { $_[0..1] -join ' ' }) -join ',') -cne 'api --paginate,release create,release upload,release edit') { throw 'New release must create a draft, upload, then publish.' }
        $create = $releaseFixture.Calls[1]
        if ('--draft' -notin $create -or '--prerelease' -notin $create -or $commit -notin $create) { throw 'Release must begin as a prerelease draft for the exact commit.' }

        $releaseFixture.Calls = @(); $releaseFixture.FailAt = 'upload'
        Assert-Rejected { & $publisher -Directory $fixture }
        if (@($releaseFixture.Calls | Where-Object { $_[1] -eq 'edit' }).Count) { throw 'Failed upload must not publish.' }

        $releaseFixture.Calls = @(); $releaseFixture.FailAt = 'create'
        Assert-Rejected { & $publisher -Directory $fixture }
        if ($releaseFixture.Calls.Count -ne 2) { throw 'Failed creation must not upload or publish.' }

        $releaseFixture.Calls = @(); $releaseFixture.FailAt = 'edit'
        Assert-Rejected { & $publisher -Directory $fixture }
        if ($releaseFixture.Calls.Count -ne 4) { throw 'Publication failure must surface after the upload.' }

        $releaseFixture.Calls = @(); $releaseFixture.FailAt = 'api'
        Assert-Rejected { & $publisher -Directory $fixture }
        if ($releaseFixture.Calls.Count -ne 1) { throw 'Failed inspection must not create a release.' }

        $releaseFixture.Calls = @(); $releaseFixture.FailAt = ''
        $releaseFixture.RemoteReleases = "[[{`"tag_name`":`"$version`",`"draft`":false}]]"
        & $publisher -Directory $fixture | Out-Null
        if ($releaseFixture.Calls.Count -ne 1) { throw 'Published releases must not be modified.' }

        $releaseFixture.Calls = @()
        $releaseFixture.RemoteReleases = "[[],[{`"tag_name`":`"$version`",`"draft`":true}]]"
        & $publisher -Directory $fixture | Out-Null
        if ((($releaseFixture.Calls | ForEach-Object { $_[0..1] -join ' ' }) -join ',') -cne 'api --paginate,release upload,release edit') { throw 'Draft retry must upload and publish without creating another release.' }

        $releaseFixture.Calls = @(); $env:GITHUB_EVENT_NAME = 'workflow_dispatch'
        & $publisher -Directory $fixture | Out-Null
        if ($releaseFixture.Calls.Count -ne 3) { throw 'Manual main runs must support draft recovery.' }

        $releaseFixture.Calls = @(); [IO.File]::AppendAllText($zip, 'tampered')
        Assert-Rejected { & $publisher -Directory $fixture }
        if ($releaseFixture.Calls.Count) { throw 'Corrupt assets must not contact the publishing API.' }
        [IO.File]::WriteAllText($zip, 'package fixture')

        $releaseFixture.Calls = @(); $env:GITHUB_REF = 'refs/heads/feature'
        Assert-Rejected { & $publisher -Directory $fixture }
        if ($releaseFixture.Calls.Count) { throw 'Feature branches must not contact publishing API.' }
        $env:GITHUB_REF = 'refs/heads/main'; $env:GITHUB_EVENT_NAME = 'pull_request'
        Assert-Rejected { & $publisher -Directory $fixture }
        if ($releaseFixture.Calls.Count) { throw 'Pull requests must not contact publishing API.' }
    } finally {
        Remove-Item Function:gh -ErrorAction SilentlyContinue
        foreach ($key in $environmentNames) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key]) }
    }
    Write-Output 'ReleaseChecks: identity, exact asset set, corruption, checksum, manifest and missing-file checks passed.'
    Write-Output 'ReleaseChecks: draft-first publishing, API/create/upload/publish failures, published/draft reruns, manual main, corrupt assets and main-only guard passed (remote CLI substituted).'
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe release fixture cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
