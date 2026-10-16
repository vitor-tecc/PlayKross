# Native application integration — milestone 1, task 9

Measured on this Windows machine on 2026-10-13. These are short local smoke checks, not sustained streaming acceptance.

- Actual WPF application initializes, accepts an invitation and joins its room using Windows UI Automation. Its controller is also exercised through the same presentation model in the real-media harness.
- Geometry Dash's existing original window/process was selected; no gameplay input, screenshot, or recording was made. Capture reported Windows Graphics Capture / D3D11 texture, source 2559×1440, fitted without stretching to the requested output.
- At 720p30 / 6 Mbps target: NVENC H.264 initialized; Edge 155.0.4283.45 received H.264 1280×720, 122 decoded frames and instantaneous reported decode FPS 30.
- After a stop/restart with the same stream identity at 1080p60 / 15 Mbps target: NVENC H.264 initialized; Edge received H.264 1920×1080, 240 decoded frames and instantaneous reported decode FPS 61. These counters are from a short sample; they are not an average FPS measurement or proof of smooth gameplay.
- Geometry Dash process-only audio reported running, emitted frames, no startup error and Opus packets, but browser audio energy was **zero** in both samples. Audible game sound and Discord exclusion remain unverified.
- The independent synthetic motion/tone fixture produced nonzero Opus audio energy at both presets (0.0015579354 at 720p; 0.0017991903 at 1080p). This proves selected fixture audio reached the receiver; it does not prove Discord or incoming playback exclusion.
- Fullscreen and return succeeded at both presets. The owned publisher/helper and local server processes were cleaned up; Geometry Dash remains running.

Correctness checks: nine controller lifecycle/settings tests, nine typed-engine tests, six room-client/DPAPI cases and three native presentation-model cases pass; full .NET suite is 101 tests. A real WPF close reentrancy crash was reproduced by the UI probe and fixed by posting the final close to the next dispatcher turn.

Audio selection is explicit, with only the standalone Steam Geometry Dash and synthetic-fixture profiles enabled. Discord, browsers/shared processes and unknown apps cannot select audio. Other apps can share video with audio off. A display selection does not select system audio. No microphone feature exists. Process identities include the original creation time and image path, and native capture fails closed on original-source loss.

Outstanding: ten-minute high-motion cadence/latency measurement; real audible Geometry Dash audio; controlled Discord desktop/browser and incoming-playback exclusion; active GPU encoder utilization; external-network/relay and other-OS browser checks; multiple viewers/broadcasters. None are inferred from the passing tests or short loopback samples.

Local launch: run `scripts/Start-Local.ps1` to obtain a room link, then `scripts/Start-Broadcaster.ps1`. Stop the app's broadcast independently of the room host. The browser viewer is at the room link. `scripts/Test-LiveFlow.ps1` uses the synthetic fixture; `-GeometryDashPid <PID>` explicitly selects the already-running game and saves only diagnostic text, without recording its content.
