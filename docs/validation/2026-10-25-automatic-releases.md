# Automatic Windows release verification

2026-10-25, branch `codex/automatic-windows-releases`, based on `main` at `7e43d5f`. This work adds desktop packaging/publication automation; server operators still clone the source repository.

## Local behavior and syntax checks

- `tests/setup/ReleaseChecks.ps1` first failed without the release helper, then passed with identity and file-integrity validation. Publisher lifecycle tests first failed without the extracted publisher script, then passed after implementation. They exercise actual scripts with only the remote `gh` boundary substituted: new draft/upload/publish, API/create/upload/publish failures, draft repair, unchanged published releases, manual main runs, corrupt assets and rejected PR/feature-branch publication. No GitHub release was created by these tests.
- Existing `tests/setup/PortablePackageChecks.ps1` passed traversal, symbolic-link/junction, clean-OBS and payload-allowlist cases.
- Checksum-verified actionlint 1.7.7 accepted the release workflow. PowerShell parsing accepted the helper, publisher, release checks and modified verification script; `git diff --check` passed.

## Hosted build evidence

- [Initial full package run](https://github.com/vitor-tecc/PlayKross/actions/runs/40247189279), source `24feb6a`: **passed** on a fresh Windows runner. Integrated CodeOnly passed **292 managed tests, 98 browser tests and 43 native checks**, the app/web/probe builds and setup/headless checks. The modified OBS capture library built, the portable package was created and verified, and the three-file artifact uploaded successfully (268,439,963 bytes including the artifact wrapper). This used artifact-action v4; the final workflow pins current Node 24 artifact actions instead.
- Initial Correctness PR run `40247189166` failed one browser UI test waiting for `Sharing video only`; the same source's push run `40247173740` passed. No browser production/test code was changed for that intermittent result. Both subsequent Correctness runs at `1db8bc9` (`40247470838` and `40247467691`) passed. Preserve the initial failure rather than treating it as an assertion-free run.
- [Second full package run](https://github.com/vitor-tecc/PlayKross/actions/runs/40247470825), source `1db8bc9`: **passed**, including the added publication lifecycle verification and draft-only retry handling.
- [Final workflow package run](https://github.com/vitor-tecc/PlayKross/actions/runs/40247659211), source `e12eeca`: **passed**, with immutable official upload-artifact v7.0.2/download-artifact v8.0.2 revisions replacing the deprecated-runtime actions. All **292 managed, 98 browser and 43 native checks**, setup/headless checks, capture/plugin/app/web builds, portable ZIP verification and artifact upload passed. The three-file artifact is 268,439,959 bytes including its wrapper. Publication was correctly skipped for the PR. Both final Correctness runs (`40247659264`, `40247655999`) also passed. The download action and actual release API publication are only exercised on main; their server-side authorization is not established by the PR run.

## Review and limits

A read-only independent GPT-6.1 Sol review checked fresh-runner prerequisites, permissions, main/PR guards, pagination and release-retry ordering. It identified GitHub's restriction on releasing older commits after `.github/workflows` changes on main. Documentation and publisher errors now explain recovery using the current main build or separately authorized maintainer credentials for the exact older revision; no broader secret was installed. Ordinary overlapping updates still run independently, but that GitHub permission restriction can prevent an older publication.

Hardware/media acceptance remains outside these checks: no live AMD/NVIDIA, display capture, audible audio, external-network stability or game-impact claim is added. The maintainer's running services and native runtime were not rebuilt/restarted by this work; package compilation happened on hosted runners. No runtime binaries, credentials, invitations or raw logs were committed.

Publishing is deliberately skipped on PRs. Actual draft creation, asset upload and release publication with the workflow's `GITHUB_TOKEN` remain unverified until a main push/manual main run after merge. The workflow requires only its scoped token, subject to repository policy; no personal token/new secret is configured. [PR #15](https://github.com/vitor-tecc/PlayKross/pull/15) must be merged to activate automatic main releases. This work does not authorize that merge or change milestone acceptance.
