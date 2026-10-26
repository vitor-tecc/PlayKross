#requires -Version 7.4
param([string]$Directory = (Join-Path $PSScriptRoot '../.artifacts/releases'))
if ($env:GITHUB_REF -ne 'refs/heads/main' -or $env:GITHUB_EVENT_NAME -notin @('push','workflow_dispatch')) {
    throw 'Only main push/manual runs may publish a release.'
}
$notesPath = Join-Path $Directory 'release-notes.md'
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseTools.ps1')
$version = Get-ReleaseVersion -RunId $env:GITHUB_RUN_ID -Commit $env:GITHUB_SHA
$assets = @(Get-ReleaseAssets -Directory $Directory -Version $version)
$repository = $env:GITHUB_REPOSITORY
# A successful API listing distinguishes missing releases from auth/network failures.
$releases = gh api --paginate --slurp "repos/$repository/releases?per_page=100"
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect existing releases.' }
$pages = ($releases -join "`n") | ConvertFrom-Json
$existing = @(foreach ($page in $pages) {
  foreach ($release in $page) {
    if ($release.tag_name -eq $version) { $release }
  }
})
if ($existing.Count -gt 1) { throw 'Ambiguous release identity.' }
if ($existing.Count -eq 1 -and -not $existing[0].draft) {
  Write-Output "Already published $version; existing assets are unchanged."
  return
}
$notes = @"
Automatic Windows desktop build from commit $env:GITHUB_SHA.

Download the ZIP, extract the entire folder and open PlayKross.exe. The SHA-256 checksum and file manifest accompany it. Windows 11 x64 is required; .NET is bundled. WebView2 and Visual C++ x64 runtime may be needed; see the portable instructions in the repository.

To host a room, clone the repository and follow docs/setup/windows.md and docs/setup/internet.md. The desktop ZIP does not run the server.

This is an automated prerelease. Code and package checks passed; live AMD/NVIDIA, captured audio/video, external-network and game-impact acceptance remain separate checks. See docs/M3-HANDOFF.md at this commit for recorded limits.

Build details: $env:GITHUB_SERVER_URL/$repository/actions/runs/$env:GITHUB_RUN_ID
"@
$notes | Set-Content -LiteralPath $notesPath -Encoding utf8NoBOM
if ($existing.Count -eq 0) {
  gh release create $version --repo $repository --target $env:GITHUB_SHA --title "PlayKross $version" --notes-file $notesPath --draft --prerelease --latest=false
  if ($LASTEXITCODE -ne 0) { throw 'Draft release creation failed. If main has since changed workflow files, GitHub may deny this older target; dispatch a new main run or have a maintainer release the exact old commit with workflow-authorized credentials. Repository tag policy can also deny creation.' }
}
# Only an unpublished draft can be repaired on a rerun.
gh release upload $version @assets --repo $repository --clobber
if ($LASTEXITCODE -ne 0) { throw 'Asset upload failed; release remains a draft.' }
gh release edit $version --repo $repository --draft=false --prerelease --latest=false
if ($LASTEXITCODE -ne 0) { throw 'Release publication failed; the release remains a draft. A main workflow change can prevent publishing an older target with GITHUB_TOKEN; dispatch a new main run or have a maintainer recover the draft.' }
"Release: $env:GITHUB_SERVER_URL/$repository/releases/tag/$version" >> $env:GITHUB_STEP_SUMMARY
