# Milestone one acceptance gate

Do not advance viewer capacity or multi-publisher work until the required checks pass. Each result must carry `passed`, `failed`, `pending`, or `inconclusive`, workload, duration, topology, versions, measurement method, uncertainty, and evidence files. Automated correctness, protocol smoke tests, and actual performance are separate records.

- Automated: .NET, native, browser tests and production builds; setup corruption/traversal, source ownership/lifetime, authorization/revocation and cleanup tests.
- Local real media: native capture → NVENC H.264/Opus → MediaMTX → real browser; nonblank picture, explicit app audio, settings restart, nickname identity, fullscreen return and in-app watching/recovery.
- Sustained motion: at least 600 seconds after warmup, actual source/capture/decode/presentation rates, average fresh capture/decoded/presented FPS ≥57, ≥55 presented FPS in 95% of five-second intervals, no freeze >500 ms, separate native rendering/encoding skips <1%, visual-clock latency p95 ≤500 ms. A static/repeated frame encoded 60 times is insufficient evidence.
- Hardware: initialized native NVENC encoder plus produced video packets; distinguish device-wide utilization from per-process attribution. GPU kernel timing is unavailable in these host timestamps.
- Audio timing: calibrated start/end audiovisual offset within ±100 ms, with the analyzer/device scheduling uncertainty stated. The fixture's waveform position/flash intent alone does not prove delivered synchronization.
- Synthetic isolation: selected 997 Hz source, separate 1801 Hz native source and 2753 Hz browser playback; measure received frequency levels and detection floor. These fixtures validate process boundaries, not actual Discord behavior.
- Actual Discord: intended game audio audible while desktop Discord, browser Discord, and incoming room audio are concurrently active and excluded. Shared-browser capture stays video-only. Do not join calls or send messages automatically. Ask before saving any actual gameplay/Discord/desktop audio or pictures; numeric synthetic harness results require no private-content recording.
- External: phone Wi-Fi off, public HTTPS invitation, real direct/relayed ICE evidence, ten-minute viewer report, losses/RTT/FPS and separately measured video/audio timing. Local tabs do not qualify. Record phone OS/browser/network; relay and other-OS statuses stay separate.
- Safety changes: retitle/colliding titles, original source closed/restarted, PID reuse, monitor/window selection changes, controller killed during startup/live output, and microphone/system-device absence. Changing an unsafe/invalid source must stop audio/video without adopting a different process.
- Independent review: whole branch, audio boundaries, helper/session ownership, authentication, private listeners, bounded queues, measurement provenance, and pending evidence. Resolve material findings with reproduced failures and fresh tests.

Commands:

```powershell
./scripts/Verify.ps1 -Mode Automated
./scripts/Verify.ps1 -Mode LocalMedia
./scripts/Build-TimingSource.ps1
./scripts/Test-Sustained.ps1 -GpuSource -Seconds 600
./scripts/Verify.ps1 -Mode External -RoomLink 'https://PUBLIC_ADDRESS/#invite=...'
./tools/acceptance/Analyze-Timing.ps1 -Evidence .artifacts/acceptance/evidence.json
```

The evidence validator cannot independently certify that measurements/human declarations are truthful. It prevents missing or mocked observations from becoming a passed milestone. Preserve numerical raw reports privately under `.artifacts/acceptance`, publish summaries without tokens, nicknames, addresses or executable paths, and retain failed/inconclusive runs alongside repeats.
