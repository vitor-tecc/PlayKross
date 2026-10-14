# Milestone one remaining work

Status on 2026-10-14: code corrections for R2–R7 are implemented and verified with non-interactive correctness tests. R1 has a fail-closed safety mitigation, **not restored application-audio functionality**. M1 remains unaccepted; capacity and multi-broadcaster work remain gated.

## Work completed without opening user applications

- Native, core and desktop-engine entry points reject current unconfined process-audio capture; the audio selector explains that video remains available.
- Publication slots have a monotonic 60-second lease renewed every ten seconds. Renewal loss stops the broadcaster; expiry revokes media grants and releases the slot. Settings restart retains the stream identity within the grace period.
- Established room subscriptions close on session revocation/expiry and updates target only active session identities. Native rejoin drops expired credentials and preserves active credentials.
- CPU fallback applies at most 720p30 and 4Mbps, preserving lower requested settings. Requested, applied and observed measurements remain separate.
- Latency evidence retains multi-second decoded ages and needs at least 95% coverage. Upload-rate sampling uses a monotonic clock and consistent per-page completion timestamps.

Use `./scripts/Verify.ps1 -Mode CodeOnly` on an occupied PC. It runs .NET tests (host integration uses in-memory HTTP/WebSockets), DOM/numeric frontend tests, builds the UI/frontend/native plugin and runs the twenty native tests without audio devices or child-process fixtures. Setup checks use temporary files. It does not start the broadcaster, browser, capture fixture, media server or timing source, and does not install its compiled plugin. Three native live/process tests are explicitly excluded. This verifies correctness, **not streaming performance**.

## Next implementation prerequisite: restore safe application audio

Windows' documented `INCLUDE_TARGET_PROCESS_TREE` includes child processes. A root filename allowlist, ancestry check or polling snapshot cannot guarantee exclusion of a child that starts, plays and exits between checks. This is why current modes are video-only rather than claiming that Discord exclusion is solved. [Microsoft API semantics](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-process_loopback_mode).

Implement an enforceable source boundary before re-enabling audio. The acceptance contract is: selected app remains audible, ordinary/new/short-lived prohibited children and controller/viewer tree intersections cannot reach emitted PCM, access-denied/PID reuse/source changes fail closed, and the native entry point enforces the same policy independently. Do not solve this by altering Firefox/Discord, capturing all system output, subtracting mixed audio, or silently applying process restrictions to an already running game. Any boundary change needs its own small design and test-first implementation; the existing process-tree loopback can be reused only once those conditions can be enforced. No microphone is added.

The existing LiveFlow/LocalMedia/Firefox/sustained audio probes explicitly request app audio and are currently blocked by the mitigation. Before running them later, either restore the approved safe boundary or add a clearly labeled video-only probe path that leaves every audio/synchronization gate pending. Do not change their audio assertions to pass silence or reuse historical results as new evidence.

## Tests deferred until the user is ready

1. Install the compiled native plugin with the owned broadcaster stopped. Run the three deferred native cases: `AudioLoopback.IntendedApplicationTone`, `ProcessBinding.RealProcessExitCannotRebind`, and `LocalControl.wrong-owner`. The low-level tone case alone does not validate production eligibility or Discord exclusion.
2. Run actual native-to-Edge, interface-mode Firefox and WebView2 viewing with an authorized synthetic source; settings restart, nickname identity, fullscreen return and renderer recovery. Test lower CPU fallback on the actual encoder, and controller death/reclaim within approximately 60 seconds plus the one-second host sweep. Keep services alive while killing only the owned controller; verify stale grants fail and another owner can publish.
3. After audio is safely restored, run synthetic selected/separate/browser tones and actual desktop/browser Discord plus incoming-stream exclusion under user control. No automatic calls/messages or private recordings. Calibrate delivered audio/video offset to ±100ms.
4. Run the full-resolution 600-second high-motion test with the repaired latency gate. Capture, decoded and presented FPS must each be measured; ≥57 average, ≥55 presented in ≥95% of five-second intervals, freezes ≤500ms, native skips <1%, latency p95 ≤500ms with ≥95% marker coverage. Preserve failed runs and measurement uncertainty.
5. Configure public HTTPS and reachable media only when router/public-address conditions allow. Phone Wi-Fi off, ten-minute external report, real direct/relay evidence, losses/RTT/FPS and separate A/V timing. No domain is required by the existing optional IP-certificate profile, but external reachability/certification is still untested. Local tabs cannot satisfy this check.
6. Reassess the [acceptance checklist](milestone-1-checklist.md) with fresh evidence for this revision. Only then accept M1 and write the detailed M2 plan for one/four/eight viewers. Multiple broadcasters/grid follow later.

No live tests above were run during the 2026-10-14 code-only fix pass. Historical synthetic/Firefox reports remain preserved in [results](milestone-1-results.md) with their original revision/topology limitations. Deferred minors R8–R10 remain in the review backlog.
