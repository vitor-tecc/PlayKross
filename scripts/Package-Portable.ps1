#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$')][string]$Version = ('milestone-1-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [switch]$SkipNativeBuild,
    [switch]$KeepStaging
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'DependencyTools.ps1')
. (Join-Path $PSScriptRoot 'PortablePackageTools.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$releaseRoot = Join-Path $root '.artifacts/releases'
$stage = Join-Path $root ('.artifacts/portable-staging-' + [guid]::NewGuid().ToString('N'))
$packageName = 'PlayKross-win-x64-' + $Version
$payload = Join-Path $stage $packageName
$publish = Join-Path $stage 'publish'
$archivePath = Join-Path $releaseRoot ($packageName + '.zip')
$temporaryArchive = Join-Path $stage 'release.partial.zip'
$manifestPath = Join-Path $releaseRoot ($packageName + '.manifest.json')
$hashPath = Join-Path $releaseRoot ($packageName + '.sha256')
$allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function Copy-PayloadFile([string]$Source, [string]$RelativePath) {
    Assert-PortableRelativePath -Path $RelativePath
    Assert-PortableFile -Path $Source -Root $root
    if (-not $allowed.Add($RelativePath)) { throw "Duplicate portable payload: $RelativePath" }
    $target = Join-Path $payload $RelativePath
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $target
}

foreach ($directory in @($releaseRoot, $stage)) { Assert-PortableDirectory -Path $directory -Root $root }
foreach ($path in @($archivePath, $manifestPath, $hashPath)) {
    if (Test-Path -LiteralPath $path) { throw "Release already exists; choose a new -Version: $path" }
}
$pins = Get-Content -LiteralPath (Join-Path $root 'config/dependencies.json') -Raw | ConvertFrom-Json
$obsArchive = Join-Path $root ('.tools/downloads/obs-' + $pins.obs.version + '.zip')
$sourceArchive = Join-Path $root '.tools/downloads/OBS-Studio-32.2.2-Sources.tar.gz'
$jsonHeader = Join-Path $root '.tools/json/include/nlohmann/json.hpp'
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
$plugin = Join-Path $root '.artifacts/native/Release/jk-obs.dll'
$capture = Join-Path $root '.artifacts/instrumented-obs/Release/libobs-winrt.dll'
$provenancePath = Join-Path $root '.artifacts/instrumented-obs/provenance.json'
$patch = Join-Path $root 'patches/obs-32.2.2-capture-trace.patch'
foreach ($path in @($obsArchive, $sourceArchive, $jsonHeader, $dotnet, $capture, $provenancePath, $patch)) { Assert-PortableFile -Path $path -Root $root }
Assert-DependencyHash -Path $obsArchive -Algorithm $pins.obs.algorithm -ExpectedHash $pins.obs.hash
Assert-DependencyHash -Path $sourceArchive -Algorithm SHA256 -ExpectedHash 'ec81fb66b03e75ddb3076b576f62679c39262e0e9960cef3e17a40dc5d68e6b4'
Assert-DependencyHash -Path $jsonHeader -Algorithm SHA256 -ExpectedHash 'aaf127c04cb31c406e5b04a63f1ae89369fccde6d8fa7cdda1ed4f32dfc5de63'
$provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
Assert-DependencyHash -Path $capture -Algorithm SHA256 -ExpectedHash $provenance.binarySha256
Assert-DependencyHash -Path $patch -Algorithm SHA256 -ExpectedHash $provenance.patchSha256
Assert-DependencyHash -Path $sourceArchive -Algorithm SHA256 -ExpectedHash $provenance.sourceArchiveSha256

New-Item -ItemType Directory -Path $releaseRoot, $stage, $payload, $publish -Force | Out-Null
$oldCliHome = $env:DOTNET_CLI_HOME
$oldNuget = $env:NUGET_PACKAGES
$oldTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$success = $false
try {
    if (-not $SkipNativeBuild) {
        & (Join-Path $PSScriptRoot 'Build-Native.ps1') -Plugin -NoInstall -Configuration Release *>&1 | Tee-Object -FilePath (Join-Path $stage 'native-build.log') | Write-Host
    }
    Assert-PortableFile -Path $plugin -Root $root
    $env:DOTNET_CLI_HOME = Join-Path $root '.tools/dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $root '.tools/nuget'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    & $dotnet publish (Join-Path $root 'src/JKiller.App/JKiller.App.csproj') -c Release -r win-x64 --self-contained true -p:PortableBuild=true -p:RestoreLockedMode=true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $publish --verbosity minimal *>&1 | Tee-Object -FilePath (Join-Path $stage 'publish.log') | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'Portable application publish failed; no release ZIP was produced.' }
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'PlayKross.exe'))) { throw 'Publish omitted PlayKross.exe.' }
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'coreclr.dll'))) { throw 'Publish omitted the self-contained .NET runtime.' }
    foreach ($file in Get-ChildItem -LiteralPath $publish -Recurse -File -Force) {
        $relative = [IO.Path]::GetRelativePath($publish, $file.FullName).Replace('\','/')
        # The pinned SDK copies IntelliSense documentation; it is unnecessary at runtime.
        if ($relative -match '^Microsoft\.Web\.WebView2\.(?:Core|WinForms|Wpf)\.xml$') { continue }
        # Only compiler/runtime output; project files cannot silently add private content.
        if ($relative -notmatch '^(?:[^/]+\.(?:dll|exe)|PlayKross\.(?:deps|runtimeconfig)\.json|(?:LICENSE|ThirdPartyNotices)\.txt|[^/]+/[^/]+\.resources\.dll|runtimes/win-x64/native/[^/]+\.dll)$') {
            throw "Unexpected publish payload requires review: $relative"
        }
        Copy-PayloadFile $file.FullName $relative
    }

    $obsDestination = Join-Path $payload '.runtime/obs'
    Expand-PortableObsArchive -Archive $obsArchive -Destination $obsDestination
    foreach ($file in Get-ChildItem -LiteralPath $obsDestination -Recurse -File -Force) {
        [void]$allowed.Add([IO.Path]::GetRelativePath($payload, $file.FullName).Replace('\','/'))
    }
    $overrides = @{
        '.runtime/obs/obs-plugins/64bit/jk-obs.dll' = $plugin
        '.runtime/obs/bin/64bit/libobs-winrt.dll' = $capture
    }
    foreach ($relative in $overrides.Keys) {
        Assert-PortableFile -Path $overrides[$relative] -Root $root
        Copy-Item -LiteralPath $overrides[$relative] -Destination (Join-Path $payload $relative) -Force
        [void]$allowed.Add($relative)
    }
    Copy-PayloadFile $provenancePath '.runtime/obs/capture-provenance.json'
    $plugins = @(Get-Content -LiteralPath (Join-Path $root 'config/obs/allowed-plugins.txt') | Where-Object { $_ })
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $obsDestination 'obs-plugins/64bit') -Filter '*.dll' -File) {
        if ($file.Name -notin $plugins) { throw "OBS plugin outside the owned allowlist: $($file.Name)" }
    }
    foreach ($relative in @('dependencies.json','obs/allowed-plugins.txt','obs/global.ini','obs/user.ini','obs/basic/profiles/JKiller/basic.ini','obs/basic/scenes/JKiller.json','obs/plugin_config/obs-websocket/config.json')) {
        Copy-PayloadFile (Join-Path $root ('config/' + $relative)) ('config/' + $relative)
    }
    Copy-PayloadFile (Join-Path $root 'docs/setup/portable.md') 'README.md'
    foreach ($pair in @(
        @('native/jk-obs/LICENSE', 'Licenses/jk-obs-GPL-2.0.txt'),
        @('native/instrumented-obs/LICENSE', 'Licenses/OBS-GPL-2.0.txt'),
        @('native/instrumented-obs/NOTICE.md', 'Licenses/capture-NOTICE.md'),
        @('.tools/dotnet/LICENSE.txt', 'Licenses/dotnet-LICENSE.txt'),
        @('.tools/dotnet/ThirdPartyNotices.txt', 'Licenses/dotnet-ThirdPartyNotices.txt'),
        @('.tools/nuget/microsoft.web.webview2/1.0.4258.31/LICENSE.txt', 'Licenses/WebView2-LICENSE.txt'),
        @('.tools/nuget/microsoft.web.webview2/1.0.4258.31/NOTICE.txt', 'Licenses/WebView2-NOTICE.txt')
    )) { Copy-PayloadFile (Join-Path $root $pair[0]) $pair[1] }

    # Corresponding modified native source accompanies these uncommitted binaries.
    foreach ($component in @('jk-obs','instrumented-obs')) {
        $sourceRoot = Join-Path $root ('native/' + $component)
        foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Force) {
            $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName).Replace('\','/')
            if ($relative -match '^(?:(?:src|include|tests)/[^/]+\.(?:cpp|hpp|h)|CMakeLists\.txt|LICENSE|README\.md|NOTICE\.md)$') {
                Copy-PayloadFile $file.FullName ('Source/native/' + $component + '/' + $relative)
            }
        }
    }
    Copy-PayloadFile $patch 'Source/patches/obs-32.2.2-capture-trace.patch'
    Copy-PayloadFile $sourceArchive 'Source/upstream/OBS-Studio-32.2.2-Sources.tar.gz'
    Copy-PayloadFile $jsonHeader 'Source/upstream/json/include/nlohmann/json.hpp'
    Copy-PayloadFile (Join-Path $root 'config/dependencies.json') 'Source/config/dependencies.json'
    foreach ($script in @('Build-Native.ps1','Build-InstrumentedObs.ps1','DependencyTools.ps1','Setup-Dev.ps1')) {
        Copy-PayloadFile (Join-Path $PSScriptRoot $script) ('Source/scripts/' + $script)
    }
    $commit = (& git -c ('safe.directory=' + $root) -C $root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record source revision.' }
    [ordered]@{
        product = 'PlayKross'; version = $Version; target = 'win-x64'; selfContained = $true
        sourceRepository = 'https://github.com/vitor-tecc/PlayKross'; sourceCommit = $commit
        sourceNote = 'Native corresponding source included; the checkout may contain changes beyond this commit.'
        obsVersion = $pins.obs.version; obsArchiveSha256 = $pins.obs.hash
        pluginSha256 = (Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash
        capture = $provenance
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $payload 'BUILD-INFO.json') -Encoding utf8NoBOM
    [void]$allowed.Add('BUILD-INFO.json')

    $inventory = @(foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File -Force | Sort-Object FullName) {
        Assert-PortableFile -Path $file.FullName -Root $payload
        $relative = [IO.Path]::GetRelativePath($payload, $file.FullName).Replace('\','/')
        Assert-PortableEntry -Path $relative -AllowedPaths $allowed
        [ordered]@{ path = $packageName + '/' + $relative; bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    })
    if ($inventory.Count -ne $allowed.Count) { throw 'Portable payload differs from the complete allowlisted inventory.' }
    $zip = [IO.Compression.ZipFile]::Open($temporaryArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $inventory) {
            $relative = $item.path.Substring($packageName.Length + 1)
            $entry = $zip.CreateEntry($item.path, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $inputFile = [IO.File]::OpenRead((Join-Path $payload $relative))
            $outputEntry = $entry.Open()
            try { $inputFile.CopyTo($outputEntry) } finally { $outputEntry.Dispose(); $inputFile.Dispose() }
        }
    } finally { $zip.Dispose() }
    # Verify the actual compressed artifact, not just the staging tree.
    $zip = [IO.Compression.ZipFile]::OpenRead($temporaryArchive)
    try {
        if ($zip.Entries.Count -ne $inventory.Count) { throw 'Release ZIP file count mismatch.' }
        for ($index = 0; $index -lt $inventory.Count; $index++) {
            $entry = $zip.Entries[$index]
            $expected = $inventory[$index]
            Assert-PortableEntry -Path $entry.FullName -ExternalAttributes $entry.ExternalAttributes
            if ($entry.FullName -cne $expected.path -or $entry.Length -ne $expected.bytes) { throw 'Release ZIP inventory mismatch.' }
            $stream = $entry.Open()
            try { $actualHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            if ($actualHash -ne $expected.sha256) { throw "Release ZIP hash mismatch: $($entry.FullName)" }
        }
    } finally { $zip.Dispose() }
    $archiveHash = (Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA256).Hash
    $archiveBytes = (Get-Item -LiteralPath $temporaryArchive).Length
    [ordered]@{
        archive = $packageName + '.zip'; bytes = $archiveBytes; sha256 = $archiveHash
        files = $inventory.Count; unpackedBytes = ($inventory | ForEach-Object { $_.bytes } | Measure-Object -Sum).Sum
        inventory = $inventory
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    "$archiveHash  $packageName.zip" | Set-Content -LiteralPath $hashPath -Encoding ascii
    Move-Item -LiteralPath $temporaryArchive -Destination $archivePath
    $success = $true
    [pscustomobject]@{ Archive = $archivePath; Bytes = $archiveBytes; SHA256 = $archiveHash; Manifest = $manifestPath; Files = $inventory.Count }
} finally {
    $env:DOTNET_CLI_HOME = $oldCliHome
    $env:NUGET_PACKAGES = $oldNuget
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $oldTelemetry
    if ($success -and -not $KeepStaging) {
        # Delete only this invocation's unique, checked workspace staging directory.
        Assert-PortableDirectory -Path $stage -Root $root
        foreach ($item in Get-ChildItem -LiteralPath $stage -Recurse -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing staging cleanup containing a reparse point.' }
        }
        Remove-Item -LiteralPath $stage -Recurse -Force
    } elseif (-not $success) { Write-Warning "Packaging failed. Logs and staging preserved at $stage; no verified release ZIP was produced." }
}
