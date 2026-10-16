$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$osVersion = [Environment]::OSVersion.Version
[pscustomobject]@{ Component='Windows'; Value=$osVersion.ToString(); Ready=($osVersion.Build -ge 22000) }
foreach ($dependency in (& (Join-Path $PSScriptRoot 'Setup-Dev.ps1') -CheckOnly)) {
    [pscustomobject]@{ Component=$dependency.Dependency; Value=$dependency.PinnedVersion; Ready=$dependency.Present }
}
$vswhere = 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
if (Test-Path -LiteralPath $vswhere) {
    $cpp = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    [pscustomobject]@{ Component='C++ Build Tools'; Value=$cpp; Ready=(-not [string]::IsNullOrWhiteSpace($cpp)) }
}
$gpuTool = Get-Command nvidia-smi -ErrorAction SilentlyContinue
if ($gpuTool) {
    & $gpuTool.Source --query-gpu=name,driver_version --format=csv,noheader
    Write-Output 'NVIDIA hardware detected. Active NVENC is NOT verified until a real publication initializes the encoder and activity is observed.'
} else { Write-Output 'NVIDIA inspection unavailable. Hardware encoder availability is unverified.' }
