# Milestone 2 handoff

M1 has practical acceptance with known limitations as of 2026-10-16. Start with [the current roadmap](ROADMAP.md). This handoff describes the next scope; it is not an approved detailed M2 implementation plan.

## Git baseline and attribution

- Repository: [vitor-tecc/PlayKross](https://github.com/vitor-tecc/PlayKross).
- M1 integration: [PR #3](https://github.com/vitor-tecc/PlayKross/pull/3), targeting `main`. Begin new development from the updated `main` after that PR is merged. Preserve the M1 branch as historical context rather than continuing it after a squash merge.
- [@vitor-tecc](https://github.com/vitor-tecc) is the project owner and contributor. Use the configured repository-local author identity for future commits; do not override it with the former unassociated `Codex <codex@local.invalid>` identity.
- Use a new `codex/` feature branch for M2. Do not rewrite existing published history to change attribution.

## M2 scope

One broadcaster serving one, four and eight viewers. Design bandwidth admission, bounded queues/delay and slow-viewer isolation. Verify that the broadcaster keeps one shared encoding pipeline rather than encoding separately for each viewer. Multiple simultaneous broadcasters, shared previews and grid/focus are M3 scope.

Use Superpowers to review the current implementation, write a focused M2 design and a small-step implementation plan with validation criteria, obtain plan approval, then implement test-first with review and verification. Do not restart M1 or repeat completed work without a relevant regression or unresolved question.

## Existing implementation

- Windows C#/WPF broadcaster with WebView2 in-app viewing; .NET 10. Native capture/encoding uses an application-owned OBS 32.2.2 helper and C++ extension.
- Shared H.264/Opus publication over WHIP to self-hosted MediaMTX 1.21.2; browser viewers use WHEP. ASP.NET Core/SignalR handles anonymous rooms and stable identity.
- Current room cap is eight participants including the broadcaster. Eight viewers require an explicit M2 capacity change and measured validation; they are not already proven by that cap.
- Native hardware encoding currently supports NVIDIA NVENC. Explicit CPU fallback is capped at 720p30/4 Mbps; AMD/Intel hardware encoders are not implemented.
- Repeated H.264 setup headers are configured before encoder initialization, checked through native readback and logged. Retain this browser compatibility correction.
- Presence has explicit Leave, disconnect grace, multi-tab tracking and native heartbeats. Preserve ownership, media revocation and publisher lease behavior.
- Experimental audio is off by default and limited to the supported Steam Geometry Dash profile (plus the development fixture). App capture includes child processes; future-child exclusion is not guaranteed. Preserve the accepted limitation, block direct Discord/browser/room-playback/system sources, and add no microphone.

## Known follow-up and evidence limits

- [Startup stutter #4](https://github.com/vitor-tecc/PlayKross/issues/4): brief stuttering at broadcast startup is accepted for later investigation. Measure startup separately from steady playback before choosing a fix.
- [External characterization #2](https://github.com/vitor-tecc/PlayKross/issues/2): exact external FPS, loss, stalls, latency and long-run stability remain unmeasured where the reports lack evidence. Missing measurements are not passed; practical M1 acceptance does not validate M2 capacity.
- [Deferred review/audio backlog #1](https://github.com/vitor-tecc/PlayKross/issues/1): strict audio isolation and remaining minor findings are not silently resolved.
- Compare the same source/settings at one, four and eight real decoding viewers, including separate internet connections and deliberately slow/stalled viewers. Observe capture, encoding, send, decode and presentation separately, along with encoder count, upload, memory and delay.
- Correctness tests with simulated media do not prove playback smoothness, active hardware encoding, Discord exclusion or internet performance. Preserve failed runs and measurement uncertainty.

## Operation and verification

See [Windows development setup](setup/windows.md), [temporary hosting](setup/temporary-tunnel.md) and [portable distribution](setup/portable.md). Use the printed public HTTPS room invitation in the native app and browser for external hosting; do not commit private invitations or addresses.

`Start-Broadcaster.ps1` rebuilds managed code only. After changing native code, stop the owned broadcaster and rebuild/install its matching plugin before using the development launcher. A fresh portable ZIP bundles matching components.

`Verify.ps1 -Mode CodeOnly` is the noninteractive correctness path. It does not launch broadcaster/browser/media/capture/tone fixtures or install the compiled plugin. Keep generated binaries, dependencies, session state, raw logs and test artifacts out of Git. Reuse the existing project folder; no application or service needs to be launched merely to plan M2.
