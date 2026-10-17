# Milestone 2 handoff

M1 has practical acceptance with known limitations as of 2026-10-16. Start with [the current roadmap](ROADMAP.md). The approved M2 plan has been implemented through its candidate validation tooling; acceptance remains pending in [M2 results](validation/milestone-2-results.md). Use the [functional checklist](validation/milestone-2-checklist.md) for this revision; M1 measurements remain historical.

The [2026-10-17 playback/interface iteration](validation/2026-10-17-m2-iteration.md) adds active source staging/restart, independent playback volume, overlay/grid/focus controls, desktop colors and a short user-local WebView2 profile path. It records fresh real Firefox and nested-portable WebView2 evidence. The reported three-404 Firefox sequence remains unreproduced and now has connection diagnostics; previous candidate ZIPs and their historical measurements do not include these changes.

The user revised the scope again on 2026-10-16: concurrent broadcasting is now M2's main goal, fixed per-room people limits and eight-viewer testing are removed, and fullscreen bars must hide automatically. The user approved the [revised M2 design](superpowers/specs/2026-10-16-milestone-2-design.md) and [implementation plan](superpowers/plans/2026-10-16-milestone-2.md), selecting subagent-driven execution. All nine implementation tasks and independent code reviews are complete. Continue the pending functional checks in the results/checklist; do not repeat implementation or treat code review as audible acceptance.

## Git baseline and attribution

- Repository: [vitor-tecc/PlayKross](https://github.com/vitor-tecc/PlayKross).
- M1 integration: [PR #3](https://github.com/vitor-tecc/PlayKross/pull/3), targeting `main`. Begin new development from the updated `main` after that PR is merged. Preserve the M1 branch as historical context rather than continuing it after a squash merge.
- [@vitor-tecc](https://github.com/vitor-tecc) is the project owner and contributor. Use the configured repository-local author identity for future commits; do not override it with the former unassociated `Codex <codex@local.invalid>` identity.
- Use a new `codex/` feature branch for M2. Do not rewrite existing published history to change attribution.

## M2 scope

The main goal is multiple people broadcasting concurrently in the same room, with independent stream ownership and playback. Remove the fixed per-room participant and publisher limits and the UI's maximum-people display. Each participant can publish one stream and choose which other streams to watch; do not add a replacement fixed reader cap or proactive bandwidth-admission system. Keep one shared encoding pipeline per broadcaster rather than encoding separately for each viewer.

Fullscreen viewing must hide the top and bottom playback bars immediately on entry. Cursor movement reveals them; two seconds of inactivity hides them again. Preserve keyboard/touch control access, playback state, and fullscreen exit in the browser and in-app viewer.

The earlier required one/four/eight-viewer capacity matrix, external eight-viewer attendance, and large-group performance gate are removed. Use a small concurrent-publication functional check. Scaling work is deferred until personal use reveals an actual problem. Shared previews and advanced grid/focus remain future viewing refinements.

Working broadcast audio is also required for M2 acceptance. An experimental selector or silent audio track does not satisfy this requirement. See the revised audio requirements below.

The approved small-step implementation plan was completed using Superpowers subagent-driven development, test-first changes, review and verification. Continue from the recorded functional evidence and pending checks. Do not restart design, plan approval or M1 work without a relevant requirement change, regression or unresolved question.

## Revised M2 audio requirements (2026-10-16)

- Prioritize working audio from the broadcaster through to browser and in-app viewers. Strict Discord exclusion must not block initial M2 delivery.
- Prefer selected-application audio that leaves unrelated Discord audio out. Prefer capturing Discord only when the user explicitly selects entire-screen/system-output audio. These are preferences, not an initial isolation acceptance gate: incidental Discord capture is acceptable initially and must be described accurately.
- Permit an explicit entire-screen/system-output audio mode in the M2 design. It captures the selected output's mixed sound, which can include Discord and other applications. Keep audio opt-in and distinguish application audio from system audio in the controls; selecting a display alone must not silently enable system audio.
- Keep microphone capture out of scope. Account for room-playback feedback when designing system audio; do not claim that mixed output excludes room playback or Discord.
- Validate actual audible delivery with video in a small concurrent-broadcasting check, without requiring eight viewers. Exercise the implemented application/system modes, audio off, start/stop/restart, watching while publishing, and browser sound enablement; record observed timing issues, interruptions and any Discord leakage. Automated correctness tests alone do not establish working audio.
- These requirements supersede the earlier prohibition on system-output audio and the strict Discord-isolation gate for M2. Preserve historical M1 evidence and report remaining isolation limitations without marking them fixed.

## Existing implementation

- Windows C#/WPF broadcaster with WebView2 in-app viewing; .NET 10. Native capture/encoding uses an application-owned OBS 32.2.2 helper and C++ extension.
- Shared H.264/Opus publication over WHIP to self-hosted MediaMTX 1.21.2; browser viewers use WHEP. ASP.NET Core/SignalR handles anonymous rooms and stable identity.
- At the M1 baseline, the room cap is eight participants including the broadcaster, and only one publisher is allowed. Removing these per-room limits and enabling concurrent publishers is M2 work; the scope revision does not itself change current application behavior or prove a maximum supported load.
- Native hardware encoding currently supports NVIDIA NVENC. Explicit CPU fallback is capped at 720p30/4 Mbps; AMD/Intel hardware encoders are not implemented.
- Repeated H.264 setup headers are configured before encoder initialization, checked through native readback and logged. Retain this browser compatibility correction.
- Presence has explicit Leave, disconnect grace, multi-tab tracking and native heartbeats. Preserve ownership, media revocation and publisher lease behavior.
- At the M1 baseline, experimental audio is off by default and limited to the supported Steam Geometry Dash profile (plus the development fixture). App capture includes child processes; future-child exclusion is not guaranteed. Direct Discord/browser/room-playback/system sources are currently blocked. This describes existing behavior; the revised M2 requirements above govern audio changes. No microphone is added.

## Known follow-up and evidence limits

- [Startup stutter #4](https://github.com/vitor-tecc/PlayKross/issues/4): brief stuttering at broadcast startup is accepted for later investigation. Measure startup separately from steady playback before choosing a fix.
- [External characterization #2](https://github.com/vitor-tecc/PlayKross/issues/2): exact external FPS, loss, stalls, latency and long-run stability remain unmeasured where the reports lack evidence. Missing measurements are not passed; practical M1 acceptance does not validate M2 capacity.
- [Deferred review/audio backlog #1](https://github.com/vitor-tecc/PlayKross/issues/1): strict audio isolation remains deferred under the revised M2 requirements; it and remaining minor findings are not silently resolved.
- Check two independent publications, selected-stream playback, stop/restart isolation, and broadcasting while watching. More than two publishers must not be blocked by a newly chosen quota. Large-group performance and external load characterization are optional future work and do not gate M2.
- Correctness tests with simulated media do not prove playback smoothness, active hardware encoding, Discord exclusion or internet performance. Preserve failed runs and measurement uncertainty.

## Operation and verification

See [Windows development setup](setup/windows.md), [temporary hosting](setup/temporary-tunnel.md) and [portable distribution](setup/portable.md). Use the printed public HTTPS room invitation in the native app and browser for external hosting; do not commit private invitations or addresses.

`Start-Broadcaster.ps1` rebuilds managed code only. After changing native code, stop the owned broadcaster and rebuild/install its matching plugin before using the development launcher. A fresh portable ZIP bundles matching components.

`Verify.ps1 -Mode CodeOnly` is the noninteractive correctness path. It does not launch broadcaster/browser/media/capture/tone fixtures or install the compiled plugin. Keep generated binaries, dependencies, session state, raw logs and test artifacts out of Git. Reuse the existing project folder; no application or service needs to be launched merely to plan M2.
