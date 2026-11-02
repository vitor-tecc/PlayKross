# Browser broadcasting quality controls — 2026-11-02

Scope: follow-up to `24a6199` on `codex/browser-broadcast-quality`. The owner requested browser quality presets after reporting terrible Chrome image quality at an actual 0.5 Mbps and explicitly authorized implementation without a design document. This record covers the quality-control change, not a reproduction or resolution of that friend's live issue. Earlier M2/M3 results retain their recorded revision and package limits.

## Delivered behavior

- Browser Start sharing accepts the desktop's six presets: 480p30/2 Mbps, 480p60/3 Mbps, 720p30/4 Mbps, 720p60/6 Mbps, 1080p30/8 Mbps and 1080p60/15 Mbps.
- Resolution/FPS selection resets the preset bitrate; manual 1–25 Mbps adjustment supports 0.5 Mbps steps. Defaults remain 720p30/4 Mbps. Invalid quality cannot request capture or reserve a publication. Active controls lock; stop/start requests a new chooser gesture.
- The selected dimensions/FPS reach the captured video track as ideals and maxima. The selected bitrate/FPS reach the single WHIP sender before and after negotiation. No extra encoding, simulcast, transcoding, microphone capture or automatic restart is added.
- Expand Upload diagnostics while live for outgoing video bitrate, encoded resolution/FPS and browser-reported quality limitation. A two-second sampling loop uses outbound video byte deltas; audio and protocol overhead are excluded. Missing stats are unavailable; initial bitrate needs a second sample. Late measurements cannot update a stopped attempt, and stop clears pending polling.
- New labels/help are available in English and Brazilian Portuguese.

## Verification

- Clean baseline: 101 browser tests passed before changes.
- Test-first checks failed for missing controls, ignored selected quality, absent validation and absent upload measurements, then passed after implementation.
- `./scripts/Verify.ps1 -Mode CodeOnly` passed: 122 browser tests across 13 files; 101 Core, 76 Host and 116 Windows .NET tests; 43 native noninteractive tests; native/plugin and production browser builds; existing setup, package, release, endpoint picker, fullscreen and cleanup checks. No plugin was installed and no real capture/media service was started. The first sandboxed attempt failed because MSBuild's Windows file tracker was denied access; the authorized run outside the sandbox passed.
- Headless installed Edge rendered the production website using synthetic room data at 1366px (English) and 390px (Portuguese). Both selected 1080p60 and showed the expected 15 Mbps preset, with no horizontal overflow; screenshots of the actual controls were visually inspected. This layout harness stubs room membership and blocks the realtime subscription; it does not establish media transport or capture quality. Raw screenshots/logs stay ignored under `.artifacts/`.
- Independent read-only code review found no actionable correctness/lifecycle issues. `git diff --check` passed.

## Limits and live follow-up

The friend's Chrome session, network and capture content were not available for reproduction. Increasing the ceiling does not force a minimum or constant bitrate and does not prove the 0.5 Mbps symptom fixed. After updating/rebuilding the room server's website and refreshing Chrome, select the desired preset and inspect Upload diagnostics while sharing moving content. Record actual bitrate/resolution/FPS and any reported limitation alongside viewer observations. Actual Chrome display capture/audio, sustained high-resolution/high-FPS delivery, hardware encoder selection, game impact and external-network behavior still need live acceptance; do not infer them from mocked unit tests or layout checks.
