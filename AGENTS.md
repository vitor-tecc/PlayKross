# Project instructions

## Agent models

- Use GPT-6.1 Sol (`gpt-6.1-sol`) for every agent and subagent created for this project, including nested delegation and review agents.
- Do not use GPT-6 Astra (`gpt-6-astra`). This project preference takes precedence over model-routing suggestions in skills.
- Set the model and reasoning effort explicitly when creating agents. Choose higher reasoning when the task benefits from it.
- Pass this preference to delegated agents. If GPT-6.1 Sol is unavailable, report that limitation instead of silently substituting another model.

## Development context

- M2 was accepted by the project owner on 2026-10-18 after confirming that every remaining functional acceptance case was tested and works as intended. Begin M3 from [the current roadmap](docs/ROADMAP.md); retain historical test results and their revision/package limits.
- M3's main goal is AMD AMF H.264 hardware broadcasting compatibility. Prioritize minimal broadcaster CPU/GPU workload and game impact; preserve a single shared encoding pipeline per broadcaster and explicit consent for CPU fallback. Shared lower-quality previews/transcoding are scrapped. Intel Quick Sync is deferred because the current friend group does not need Intel GPU support. This scope supersedes historical M3 preview requirements; see [the M3 handoff](docs/M3-HANDOFF.md).
- M3 also includes starting broadcasts directly from the website using the browser's own capture (`getDisplayMedia()`), without requiring the desktop app/OBS helper. Keep capture user-initiated, audio opt-in and browser-dependent, microphone capture excluded, and publication ownership/cleanup intact. Browser encoder selection is separate from native AMD AMF support.
- Start M2 work with [the current roadmap](docs/ROADMAP.md) and [the M2 handoff](docs/M2-HANDOFF.md). Their revised M2 scope and audio requirements supersede conflicting historical restrictions for new M2 work; historical test results remain evidence only for their recorded revision.
- M2 prioritizes concurrent broadcasters, working audio, and fullscreen bars that hide automatically. Remove per-room participant/publisher quotas; do not add a replacement viewer cap or bandwidth-admission system. Eight-viewer capacity testing and large-group performance gates are outside M2 scope; address observed scale problems when needed.

## GitHub delivery

- By default, complete requested repository changes through verification, commit, and push to the configured GitHub repository. Include code, documentation, and project-instruction changes; do not leave completed work only in the local checkout.
- Use a `codex/` feature branch and open or update a pull request for review. Do not merge or rewrite published history unless explicitly requested. Report the branch/PR and verification results, and explain any authentication or network blocker.
- Keep generated binaries, dependency caches, private invitations, credentials, runtime state, and raw test artifacts out of Git. Distribute release binaries through the project's release/package workflow when requested.
- The Windows release workflow builds/packages PRs and automatically publishes a desktop prerelease for each update reaching `main`. Preserve this automation, its verification gate and clean package boundary. Room-server users clone the repository; no server binary release is required. Do not merge PRs without explicit authorization; publishing begins after the reviewed update reaches `main`.
