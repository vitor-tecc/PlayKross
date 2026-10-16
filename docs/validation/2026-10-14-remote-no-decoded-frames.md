# Remote broadcaster: bytes received, no decoded video

The user supplied two broadcaster logs and a redacted room export after a friend used the first portable package. The viewer stayed on a black surface with “Waiting for frames.” A separate screenshot showed HTTP 409 when attempting another broadcast in the room.

## Evidence

The export contains 259 reports: 24 publisher, 234 viewer and one host summary. Publisher interval reports average 59.981 encoded FPS, 59.985 sent FPS and 14.981 Mbps, with NVENC H.264 initialized at 1920×1080/60. All three viewer observers receive approximately 15 Mbps on selected direct connections, with zero decoded FPS in every available interval, no reported first frame, and unavailable/zero dimensions. This localizes the observed failure after packet delivery; it does not identify a particular decoder error or establish bitstream validity. Reported zero packet loss does not prove every encoded picture is usable.

Both native logs show successful H.264 encoder initialization, WHIP connection and thousands of output frames. Missing optional AJA/DeckLink/VLC/effects components are not evidence that the selected H.264 pipeline failed. The remote broadcaster used a different Windows/GPU configuration from the previously measured host; previous host-only results do not establish remote broadcaster compatibility.

No private raw logs, observer/session IDs, invitations, addresses, executable paths or captures accompany this source note.

## Confirmed configuration defect and focused correction

In pinned OBS 32.2.2, [WHIPService::ApplyEncoderSettings](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-webrtc/whip-service.cpp) enables `repeat_headers` and disables B-frames. [NVENC](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-nvenc/nvenc-properties.c) and [x264](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-x264/obs-x264.c) default header repetition to false. The native PlayKross pipeline created and initialized encoders without enabling this setting, then attached its private WHIP service later. `obs_output_initialize_encoders` does not apply the service's encoder-settings callback automatically.

The correction sets `repeat_headers=true` before encoder creation and initialization for both encoding paths. This makes later keyframes carry the H.264 setup information needed by readers joining later. Native readback exposes `repeatHeaders`, and a safe initialization log records its setting without connection secrets. The managed engine rejects false/missing header readback rather than publishing with a mismatched native component. Existing bitrate, profile, codec, frame rate and network configuration are retained to keep the correction focused.

This is a confirmed configuration defect and a plausible explanation for this report. The supplied export does not contain H.264 packets/SPS/PPS or a browser decoder-error reason. Resolution of the friend's black video remains **unconfirmed until retest**; correctness tests are not decoder or internet-streaming evidence.

## Separate HTTP 409

Current M1 allows one broadcaster per room. Reserving another stream while that slot is occupied produces HTTP 409; the native client now explains the one-broadcaster limit instead of showing only a generic status code. This does not enable multiple broadcasters. Watching a friend's existing stream remains supported. A screenshot without its request endpoint cannot independently establish which operation returned 409, but a second publication reservation reproduces that response.

## Validation and retest

Two header-readback regressions (NVENC and explicit CPU fallback) and the publication-conflict message regression failed before their fixes, then passed. Full Windows unit suite: 58 passed. These use simulated native replies/HTTP, not actual media. Independent scoped review found no actionable defects and confirmed the upstream initialization contract.

The replacement package is built with the Release native component and matching self-contained managed app. It must be extracted into a fresh folder; updating only the executable or only the native DLL is insufficient. The package script verifies every compressed file/hash and excludes runtime private state. Native compilation and packaging do not prove a particular GPU's encoded output decodes correctly.

Release build, self-contained WPF publish and 22 selected noninteractive native tests passed. Replacement archive: `.artifacts/releases/PlayKross-win-x64-milestone-1-headers-20261014.zip`, 267,775,575 compressed bytes, 2,535 files / 601,728,819 unpacked bytes. SHA-256: `10CC0FB211D990F7ABBB3289AB4107F66C707AC3BE19B5723F26A8814CFA9BD5`. The original experimental archive remains separate; distribute the headers revision for this retest.

Retest with one broadcaster, the same source/settings and audio off, then open one browser viewer after broadcasting starts. Allow several keyframes, and export diagnostics while the attempt is active. A successful repeat needs nonzero decoded FPS, delivered dimensions and moving video; receiving Mbps alone is insufficient. If it still fails, preserve the new native log (including `repeat_headers=true`) and diagnostics, then investigate received/keyframe/decoder counters or a controlled synthetic bitstream. Do not change architecture or several encoder/network settings at once.

No live broadcaster, browser, capture, audio, network test or host restart was performed for this correction. Existing M1 external stability/numerical gates and later capacity/multiple-broadcaster work remain pending.
