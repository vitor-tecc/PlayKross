param([switch]$CheckOnly,[ValidateSet('dotnet','obs','mediamtx','caddy')][string[]]$Dependencies = @('dotnet','obs','mediamtx'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DependencyTools.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'config/dependencies.json') | ConvertFrom-Json -AsHashtable
foreach ($id in $Dependencies) {
    $dependency = $manifest[$id]
    $destination = [IO.Path]::GetFullPath((Join-Path $projectRoot $dependency.destination))
    if (-not $destination.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Dependency destination leaves the project.' }
    $program = Join-Path $destination $dependency.executable
    if ($CheckOnly) {
        [pscustomobject]@{ Dependency=$id; PinnedVersion=$dependency.version; Present=(Test-Path -LiteralPath $program); Path=$program }
        continue
    }
    $archive = Join-Path $projectRoot ('.tools/downloads/' + $id + '-' + $dependency.version + '.zip')
    New-Item -ItemType Directory -Path (Split-Path $archive) -Force | Out-Null
    if (-not (Test-Path -LiteralPath $archive)) {
        Write-Output "Downloading pinned $id $($dependency.version)..."
        Invoke-WebRequest -Uri $dependency.url -OutFile $archive
    }
    Assert-DependencyHash -Path $archive -Algorithm $dependency.algorithm -ExpectedHash $dependency.hash
    # An existing extraction is accepted only if our verified-archive marker matches.
    $marker = Join-Path $destination '.verified-archive'
    $alreadyVerified = (Test-Path -LiteralPath $marker) -and ((Get-Content -Raw -LiteralPath $marker).Trim() -eq $dependency.hash) -and (Test-Path -LiteralPath $program)
    if (-not $alreadyVerified) {
        Expand-DependencyArchive -Path $archive -Destination $destination
        $dependency.hash | Set-Content -LiteralPath $marker
    }
    if (-not (Test-Path -LiteralPath $program)) { throw "Expected $id executable is missing after extraction." }
    Write-Output "$id $($dependency.version): verified and available."
}
if ($CheckOnly) {
    [pscustomobject]@{ Prerequisite='PowerShell >=7.4'; Present=($PSVersionTable.PSVersion -ge [Version]'7.4') }
    foreach ($name in @('node','npm','git')) { [pscustomobject]@{ Prerequisite=$name; Present=($null -ne (Get-Command $name -ErrorAction SilentlyContinue)) } }
    [pscustomobject]@{ Prerequisite='Visual Studio C++ Build Tools / Windows SDK / CMake'; Present=(Test-Path -LiteralPath 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'); Note='Confirm C++ Desktop workload and Windows 11 SDK 10.0.26100 or newer in Visual Studio Installer.' }
}
