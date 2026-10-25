# Automatic Windows releases implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Publish a verified portable Windows prerelease for each update reaching main.

**Architecture:** A read-only Windows job prepares dependencies, verifies code and packages the app. A separate Windows publishing job validates transferred assets and uses its scoped GitHub token to upload a draft and publish it.

**Tech Stack:** GitHub Actions, PowerShell 7, existing .NET/C++/OBS packaging, GitHub CLI.

**Spec:** `docs/superpowers/specs/2026-10-25-automatic-releases-design.md`

## Global constraints

- Publish only main; PRs build without release credentials.
- Reuse the existing allowlisted packager and pinned dependencies; include no used runtime data.
- Keep every main run; use immutable run/commit identity and never replace a published release.
- Server operators clone the repository. Hosted checks do not close M3 hardware acceptance.

## Review focus

Missing/tampered assets must fail before uploading; failed uploads must remain drafts; reruns must not replace published assets; manual feature-branch runs must not publish; fresh hosted machines must build the modified capture library.

### Task 1: Release validation and workflow

**Files:** create `scripts/ReleaseTools.ps1`, `tests/setup/ReleaseChecks.ps1`, `.github/workflows/release.yml`; modify `scripts/Verify.ps1`.

- [ ] Add failing behavioral checks for run/commit version validation and exact asset/checksum/manifest matching, including corrupt/missing files and traversal names.
- [ ] Implement the helper and run the checks green.
- [ ] Add the main/PR/manual workflow with read-only build, pinned actions, complete dependency/native setup, CodeOnly verification, packaging and exact asset transfer.
- [ ] Add draft-first publication with the exact source commit, prerelease notes, main-only guard and published-rerun refusal to overwrite.
- [ ] Validate workflow syntax and execute the actual package build locally or in hosted CI; inspect outcomes and correct fresh-runner issues.

### Task 2: Documentation and delivery

**Files:** modify `README.md`, `docs/setup/github.md`, `docs/setup/portable.md`, `AGENTS.md`; create a bounded validation record.

- [ ] Explain automatic main releases, manual reruns, download location, cloning for hosts, review/merge activation boundary and retained hardware limits.
- [ ] Obtain a read-only independent GPT-6.1 Sol review and fix important findings.
- [ ] Verify changed checks, commit and push the feature branch, open a PR, attach it to this chat, inspect CI and report what is active versus awaiting merge.
