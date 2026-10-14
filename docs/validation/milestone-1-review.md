# Milestone one review status

Read-only independent review of `d6fc6f4..516593b` completed before the PlayKross branding/GitHub/size task. No tests or benchmarks were run by the reviewer. **Not ready for release or milestone acceptance.** The 2026-10-14 author fix pass reproduced findings and verified corrections with non-interactive tests. No new live-streaming acceptance is claimed.

| ID | Severity | Finding | Status |
|---|---|---|---|
| R1 | Critical | Selected process-tree audio can include a newly spawned prohibited child or intersect the controller/viewer ancestry. Native source creation does not independently enforce positive profiles. | Safety mitigation verified: all current app-audio inputs disabled in UI, engine and native creation. Restoring useful isolated audio remains open |
| R2 | Important | Crashed broadcaster leaves the room's single publisher slot reserved until session expiry. | Corrected: monotonic 60-second lease, 10-second owner renewal, revocation/reclaim; fake-clock and in-memory HTTP/controller tests passed. Real crash test deferred |
| R3 | Important | Established SignalR subscriptions outlive session expiration/revocation. | Corrected: expiring auth ticket, active-session delivery, explicit abort on revoke and expiry sweep. Established in-memory WebSocket tests passed for both cases |
| R4 | Important | Native Join again reuses expired credentials and fails before sending the join request. | Corrected: expired join drops credentials; active join retains identity. Deterministic client tests passed |
| R5 | Important | Sustained harness drops valid timestamps aged ≥2s and accepts too few remaining latency observations. | Corrected: retain all valid ages; require ≥95% coverage over expected one-per-second samples and attempts. Numeric regressions passed; live run deferred |
| R6 | Important | Explicit software encoder fallback keeps 1080p60 instead of the approved 720p30 limit. | Corrected: cap at 720p30/4Mbps (preserve lower requests), apply/read back video settings and fitting before x264. Native replies simulated; real fallback test deferred |
| R7 | Important | Host bitrate cache/rates use adjustable wall time and inconsistent request endpoints. | Corrected: monotonic cache age and per-page response-completion sampling. Clock-jump/slow-inventory regressions passed |
| R8 | Minor | Identical source window titles lack executable/PID distinction. | Deferred |
| R9 | Minor | Duplicate nicknames remain visually indistinguishable. | Deferred |
| R10 | Minor | Audio queue discard counter is not exported. | Deferred |

The earlier NOW branding/GitHub/size work is complete. This fix pass does not certify real streaming behavior. Application audio is intentionally unavailable until its process boundary can enforce exclusion; a filename allowlist or periodic process-tree snapshot cannot guarantee safety against a short-lived/new prohibited child. No multi-viewer or multi-broadcaster milestone starts before the audio functionality and manual acceptance gates are closed. See [remaining work and deferred tests](milestone-1-next-steps.md).

Reviewer declined to judge: eight-viewer throughput/slow-reader isolation/upload capacity; multiple broadcasters/previews/admission; grid/focus/annotations/resize/cursor/zoom; additional application audio profiles; real Discord/audible game isolation; calibrated A/V offset; the then-ongoing final ten-minute run; external/other-OS/relay/certificate/NAT/firewall results; intermittent no-texture startup; exact GPU-kernel/physical-display/per-process GPU timing; disabled DXGI/title-matching capture; HDR/AV1/anti-cheat/installer packaging; and exhaustive third-party internals/binary auditing. These remain deferred, unavailable, or unmeasured as documented in the approved plan and measurement boundaries. The finished synthetic run is now preserved separately, without changing those other gates.
