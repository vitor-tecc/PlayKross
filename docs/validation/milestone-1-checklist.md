# Milestone one acceptance gate

**2026-10-16 acceptance amendment:** M1 is accepted with known limitations following practical sign-off. Brief startup stutter is deferred. The recorded ten-minute external benchmark is deferred as an M1 closure prerequisite, while its measurements and the targets below remain useful for later characterization. M2 planning may proceed; eight-viewer capacity, strict audio exclusion and unmeasured timing remain unvalidated. See the [current roadmap](../ROADMAP.md). This checklist preserves the original benchmark contract and historical scope changes; it must not be read as a list of newly passed checks.

For future capacity and multi-publisher acceptance, each result must carry `passed`, `failed`, `pending`, or `inconclusive`, workload, duration, topology, versions, measurement method, uncertainty, and evidence files. Automated correctness, protocol smoke tests, product acceptance and actual performance are separate records. M1 practical acceptance does not satisfy M2/M3 measurement gates.

2026-10-14 update: the user authorized live tests after the earlier CodeOnly pass. Fresh video-only local and600-second measurements passed; mobile-data Safari/Firefox/friend playback was reported working through the optional CGNAT profile. External numeric duration/path/loss/FPS evidence remains pending. Unconfined app audio stays disabled; its enforced boundary is an implementation prerequisite that silence cannot pass. See [ordered remaining work](milestone-1-next-steps.md).

Later explicit user amendment: audio is deferred. M1 now accepts **video only**; audio timing, synthetic audio isolation and actual Discord audio checks below are future gates, not current completion requirements and not marked passed. Native/host/browser correctness, video performance, external numerical evidence, source lifetime and review remain required. Audio capture stays disabled.

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
./scripts/Verify.ps1 -Mode CodeOnly
./scripts/Verify.ps1 -Mode LocalMedia
./scripts/Build-TimingSource.ps1
./scripts/Test-LiveFlow.ps1 -VideoOnly
./scripts/Test-InAppViewing.ps1 -VideoOnly
./scripts/Test-Sustained.ps1 -GpuSource -Seconds 600 -VideoOnly
./scripts/Test-PublicationCrash.ps1
./scripts/Test-PublicationCrash.ps1 -SourceLoss
./scripts/Test-TemporaryVideo.ps1 -Seconds 1800
./scripts/Verify.ps1 -Mode External -RoomLink 'https://PUBLIC_ADDRESS/#invite=...'
./tools/acceptance/Analyze-Timing.ps1 -Evidence .artifacts/acceptance/evidence.json
```

The evidence validator cannot independently certify that measurements/human declarations are truthful. It prevents missing or mocked observations from becoming a passed milestone. Preserve numerical raw reports privately under `.artifacts/acceptance`, publish summaries without tokens, nicknames, addresses or executable paths, and retain failed/inconclusive runs alongside repeats.

The legacy combined `Verify -Mode LocalMedia` still requests application audio and is deferred; use the explicit video-only probes above. The older full audio evidence validator has its original requirements and must not be represented as passing the amended video-only scope merely because audio is silent.
