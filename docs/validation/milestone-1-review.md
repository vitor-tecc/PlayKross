# Milestone one review status

Read-only independent review of `d6fc6f4..516593b` completed before the PlayKross branding/GitHub/size task. No tests or benchmarks were run by the reviewer. **Not ready for release or milestone acceptance.** The next implementation pass must verify each finding against current code and use RED→GREEN regressions for fixes.

| ID | Severity | Finding | Status |
|---|---|---|---|
| R1 | Critical | Selected process-tree audio can include a newly spawned prohibited child or intersect the controller/viewer ancestry. Native source creation does not independently enforce positive profiles. | Open; fail-closed tree eligibility required |
| R2 | Important | Crashed broadcaster leaves the room's single publisher slot reserved until session expiry. | Open; bounded publication lease/reconciliation required |
| R3 | Important | Established SignalR subscriptions outlive session expiration/revocation. | Open; disconnect/remove subscription on both events |
| R4 | Important | Native Join again reuses expired credentials and fails before sending the join request. | Open; expired join must use the invitation anonymously |
| R5 | Important | Sustained harness drops valid timestamps aged ≥2s and accepts too few remaining latency observations. | Open; retain delayed observations and require coverage |
| R6 | Important | Explicit software encoder fallback keeps 1080p60 instead of the approved 720p30 limit. | Open; reduce and accurately report applied settings |
| R7 | Important | Host bitrate cache/rates use adjustable wall time and inconsistent request endpoints. | Open; monotonic consistently placed sampling timestamps |
| R8 | Minor | Identical source window titles lack executable/PID distinction. | Deferred |
| R9 | Minor | Duplicate nicknames remain visually indistinguishable. | Deferred |
| R10 | Minor | Audio queue discard counter is not exported. | Deferred |

The new NOW scope prioritizes GitHub integration, branding and safe workspace-size reduction. Those changes do not resolve R1–R7; tests passing after branding do not constitute a renewed streaming approval. In particular, real Discord exclusion is not guaranteed by the current implementation. No multi-viewer or multi-broadcaster milestone starts before the review and manual acceptance gates are closed.

Reviewer declined to judge: eight-viewer throughput/slow-reader isolation/upload capacity; multiple broadcasters/previews/admission; grid/focus/annotations/resize/cursor/zoom; additional application audio profiles; real Discord/audible game isolation; calibrated A/V offset; the then-ongoing final ten-minute run; external/other-OS/relay/certificate/NAT/firewall results; intermittent no-texture startup; exact GPU-kernel/physical-display/per-process GPU timing; disabled DXGI/title-matching capture; HDR/AV1/anti-cheat/installer packaging; and exhaustive third-party internals/binary auditing. These remain deferred, unavailable, or unmeasured as documented in the approved plan and measurement boundaries. The finished synthetic run is now preserved separately, without changing those other gates.
