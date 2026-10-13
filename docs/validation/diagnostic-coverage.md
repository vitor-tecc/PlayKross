# Native diagnostic coverage — milestone 1, task 10

The active capture path is exact-handle Windows Graphics Capture. `scripts/Build-InstrumentedObs.ps1` rebuilds only the application-owned `libobs-winrt` component from the hash-verified OBS 32.2.2 source archive. Its small patch counts accepted texture updates and timestamps them after the existing GPU copy command is submitted. No CPU image readback is added. The owned engine/plugin use the same QPC-based `os_gettime_ns()` clock.

| Observation | Evidence and meaning |
|---|---|
| Capture frames/rate | Actual accepted WGC texture sequence; independent of OBS's output/render clock. An acquired frame can contain unchanged pixels. |
| Compositor frames/repeats | Main-composition callbacks associate the selected source sequence with OBS's frame timestamp; preview draws are excluded. Reusing a source sequence is counted separately. |
| Encoded frames | Maintained `obs_encoder_get_encoded_frames` counter of the actual initialized video encoder. |
| Output frames/media bytes | OBS output counters; bytes include media/audio, and do not establish total NIC/WAN upload or successful viewer delivery. |
| Encode API-call time | Matched FERC minus FER from OBS's existing encoder packet timing. Host API duration, **not GPU kernel time**. |
| Input-to-output packet residence | Output callback timestamp minus matched FER. Includes encode buffering and output interleaving. |
| Fresh capture-to-packet time | Source acquisition to the matched output callback, only for newly used source sequences. Intentionally repeated old frames are excluded, preventing a static scene from being reported as seconds of pipeline latency. |
| Missing correlation | Optional measurements remain unavailable; encoder and fresh-capture sample counts are separate. |
| Requested/applied FPS/resolution | Native encoder/video readback targets, separate from measured capture, encoded/output and browser decoded/presented rates. |
| GPU kernel duration | Explicitly unavailable. |

Outstanding frame correlation is bounded to 512 keys; summary samples cover at most 60 seconds and are also capped at 4096. Source reset clears stale associations. The public OBS callback observes the existing shared encoder; it starts no additional encoding pipeline.

Unused DXGI desktop-duplication and stock window-title source acquisition hooks are pending and are not selected by this milestone. They must be instrumented/validated before enabling those paths. Existing OBS timing covers its raw/GPU encoding paths without a separate encoder patch.

## Actual measurements, 2026-10-13

Synthetic 1080p60 / 15 Mbps publisher on this Ryzen/RTX machine, local WHIP/MediaMTX, with **no browser viewer during calibration**. Twenty seconds were sampled after five seconds of warmup in each mode. Baseline restores the verified upstream capture DLL and disables trace callbacks; the owned instrumented DLL is restored afterwards.

- Initial paired runs: baseline 59.993 encoded FPS, 1.020% normalized publisher CPU and 0.3184 ms mean of OBS's rolling render averages; instrumented 60.018 encoded FPS, 1.016% CPU and 0.3297 ms. Difference was about 0.0113 ms. This short sequential comparison is subject to machine activity and build differences; it is not a per-frame p95 overhead measurement or sustained streaming acceptance.
- After adding fresh-frame latency filtering: 60.029 encoded FPS, 60.031 compositor FPS, 37.395 accepted capture FPS and approximately 40 fixture paints per second; 0.948% normalized publisher CPU and 0.3085 ms mean of rolling render averages. The fixture's requested 16 ms Windows timer is not a measured 60 FPS producer.
- Final timing summary: 0.357 ms mean encode API call; 75.481 ms mean input-to-output packet residence; 109.029 ms mean fresh capture-to-packet time; 1527 encoder timing samples, 954 fresh-capture samples, zero unmatched encoder packets. These are native pipeline measurements, **not end-to-end viewer latency**.
- Actual NVENC H.264 encoder initialized and produced approximately 60 frames/s; CPU fallback was not selected. `nvidia-smi` identified RTX 5070 / driver 617.14 and aggregate encoder utilization of 24–30% during runs. It also showed 21% after our publisher stopped, so other device activity exists; aggregate utilization alone is not per-process attribution.
- A five-second static fixture run stopped its paint timer: no new paints/acquisitions in that sample while encoded output remained near 60 FPS. Intentional repeats are reported separately. OS redraws can still produce updates on otherwise static windows.
- Real Edge playback still passed at 720p30 and 1080p60 with the hook enabled. Geometry Dash additionally passed a numeric nonblank-pixel check at both presets; no image or gameplay recording was saved. Its received audio energy remained zero, so audible game audio/Discord exclusion remain pending.

One Geometry Dash capture startup produced no texture within the ten-second readiness deadline and stopped before publication. The window was visible, unminimized and uncloaked. A controlled original-DLL run and a repeated instrumented-DLL run subsequently passed. The transient cause is **unresolved**; no architecture change or claimed fix follows from those retries. One early static observation also failed before detailed deltas were collected; the isolated repeat passed. Keep both as issues to investigate if they recur.

Native frame/rate tests and actual API/disabled-baseline checks were observed RED before implementation and then GREEN. Automated correctness and these short calibration/smoke measurements are separate from the outstanding ten-minute gameplay/cadence, audiovisual synchronization, real Discord/incoming-audio exclusion, other-OS/browser and external-network acceptance checks.
