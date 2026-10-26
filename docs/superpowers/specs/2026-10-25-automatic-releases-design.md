# Automatic Windows releases

The owner requested automatic compilation, packaging and GitHub publication for every update. Server operators continue cloning the repository; downloadable releases contain the Windows desktop app only.

Use GitHub Actions on `windows-2025`, reusing the pinned dependency setup, native capture build, CodeOnly verification and portable packager. Build pull requests too, but publish only pushes to `main` or manual runs on `main`. Completed repository changes continue through commit/push and a review PR; local edits cannot trigger GitHub until pushed. The owner subsequently authorized merging this work and removed the per-merge confirmation requirement: follow the current `AGENTS.md` delivery instructions after verification and required checks pass.

Each workflow run gets a unique `ci-<run-id>-<12-character-commit>` version. Reruns retain that identity. Upload exactly the ZIP, checksum and manifest, validate their consistency after artifact transfer, create a draft GitHub release, then publish it as a prerelease after all uploads succeed. Published releases are never overwritten. Main updates are not cancelled by newer updates. No broad personal token is needed: only the publishing job receives `contents: write` through `GITHUB_TOKEN`.

Approaches considered: version-tag releases require a manual tag for each update; publishing all feature branches mixes incomplete work into downloads. Main-triggered prereleases meet the requested automation while keeping review branches separate. This trigger boundary and prerelease labeling are implementation choices consistent with the existing review workflow and pending M3 hardware acceptance.

Hosted builds establish compilation and package correctness, not live AMD/NVIDIA/audio/capture/game-impact acceptance. Keep those limitations in release notes. No server deployment, self-update installer, code signing, private runtime upload or automatic merge is added.
