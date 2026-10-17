# M2 candidate results

Updated 2026-10-17. **M2 acceptance remains pending.** Application/Off transport checks reached their assertions, but complete browser protocol runs failed at automated Escape. Human listening and full System-output publication remain pending. The detailed local engineering report is `.superpowers/sdd/2026-10-16-milestone-2/task-9-report.md`.

| Check | Status | Evidence / limit |
|---|---|---|
| Pure concurrent setup boundaries | PASS | Temporary files/fakes: roots, hashes, modes, environment restoration, process ownership, source allowlist and CodeOnly boundary |
| Integrated CodeOnly | PASS | 2026-10-17: .NET 263/263, browser 42/42, native noninteractive 37/37; setup/portable checks, acceptance builds, pure options and headless WPF layout passed; plugin not installed by this command |
| Two-publication Application | PARTIAL | Headless and headed Edge 155.0.4283.45 reached distinct selected streams, frames/Opus packets, A stop/restart with B element/media/peer continuity and advancing counters, and sound toggles; whole tests FAIL at Escape |
| Two-publication Off | PARTIAL | Fresh second extraction pair; distinct streams and independent restart reached, both sampled audioEnergy 0; whole test FAIL at same Escape boundary. Silent Opus packets are transport, not exclusion proof |
| System-output publication | PENDING | Two monitors/five active render endpoints available. Explicit monitor/concrete current-default endpoint selected, but owned synthetic surface guard refused capture: physical bounds/identity/visibility/borderless matched; topmost=false. No System publisher/capture started |
| Source/endpoint loss and default switch | PENDING | Separate live route checks required |
| Browser fullscreen | PARTIAL / FAIL | Geometry, immediate bars/cursor hide, movement reveal, two-second idle hide and Tab access reached. Escape FAILED under both headed/headless automation; manual Escape PENDING. Media-free repro also fails outside app, even with CDP focus emulation disabled; no app workaround added |
| Actual WebView Application | PASS (functional scope only) | Runtime 155.0.4283.60: native broadcast + 1080p receive, nickname, production window factory fullscreen state/style/resize restoration and same media, continuing frames, stable native session, bridge/navigation protection, forced script failure guidance/reload/native mute, renderer crash recovery. No audible confirmation |
| Actual WebView System mute | PENDING | Monitor-surface guard stopped the safe System run before capture; Application run separately exercises an additional mute lease and forced-script failure |
| Native Application synthetic capture | PASS | Owned tone capture stopped: 191040 samples, RMS 0.0147799. RMS/capture-only evidence; no frequency analysis |
| Native System endpoint synthetic capture | PASS | Existing owned fixture resolves concrete default once; 192000 samples, RMS 0.0149347, stopped. It does not print chosen endpoint ID and does not prove full System broadcast or viewer audibility |
| Frequency-specific/received tone detection | PENDING | Existing native tests compute nonzero PCM RMS during generated tones; no spectral detector or received-browser tone analysis ran |
| Human listening | PENDING | User cannot listen during this session; no audibility claim |
| Maximum capacity | UNPROVEN | No numerical or eight-viewer gate required |

See the [functional checklist](milestone-2-checklist.md). No screenshots, private recordings, friends' calls or external-network tests are needed for the local synthetic check. Existing M1 results retain their original revision and do not validate the new native audio routes.

Candidate: `.artifacts/releases/PlayKross-win-x64-milestone-2-candidate-20261017-0314.zip`, 267862435 bytes, 2544 verified files, source commit `60e2f1bffcc46ca464c63a575b5c8d10ee7c306b`. SHA-256: `82133825C0869AF27E0E116DCFF040D5C486EDFF3EC52DC5C229D9CDC5D7DD3C`. Required nested audio-harness corresponding source is present. Subsequent runner/probe/results-only changes do not change packaged app/native binaries. Any later app/native review fixes require a fresh matching candidate.

Raw numeric/log evidence (ignored): `.artifacts/task9-codeonly.log`, `task9-application-tone.log`, `task9-system-endpoint-tone.log`, `task9-webview-application.log`, `task9-fullscreen-repro.json`, `task9-device-inventory.json`, `task9-system-surface-repro.log`, and unique `.artifacts/concurrent` runs:

- `20261017-001157-9ea0817eb4ae46d69a6ac41e42abf471`: Application headless FAIL at Escape.
- `20261017-001358-75eb4a9ad39e49b5a25b7ed8e1efd713`: Application headed FAIL at Escape; saved A 43 frames/78 packets and B 365 frames/618 packets after restart.
- `20261017-001735-ce1f361464494198a86d078ddd39e6df`: Off headless FAIL at Escape; A 43 frames/75 packets and B 315 frames/565 packets, both energy 0.
- `20261017-002121-39921942f5b4427fb4e820d785d7c597`: System pre-capture guard refusal; explicit external browser `--mute-audio` configured to prevent feedback, but browser never launched for this run.

The standalone fullscreen repro used the same Edge channel/default context in headed and headless modes, with and without `Emulation.setFocusEmulationEnabled(false)`: all four received trusted, unprevented Escape events yet retained page fullscreen. This demonstrates a current automation boundary outside the app. The [Playwright maintainer discussion](https://github.com/microsoft/playwright/issues/19720) describes emulated page fullscreen without browser-window fullscreen; [issue 15116](https://github.com/microsoft/playwright/issues/15116) records broader headless limitations. Neither is treated as proof that manually operated Escape passes here.
