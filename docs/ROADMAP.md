# PlayKross milestones

Updated 2026-10-16. This is the current milestone-status overview. The latest user scope revision moves concurrent broadcasting into M2 and removes its fixed room-size and eight-viewer testing requirements. Original specifications, plans and measurement records remain available as historical context; they do not override the revised scope or M1 acceptance below.

| Milestone | Status | Scope |
|---|---|---|
| **1 — Core streaming** | **Accepted with known limitations** | One native Windows broadcaster, browser and in-app viewing, anonymous rooms, capture/settings controls, diagnostics, self-hosted connectivity and a portable broadcaster ZIP |
| **2 — Concurrent broadcasting** | **Design approved; implementation plan awaiting review** | Multiple simultaneous broadcasters, no fixed per-room participant/publisher cap, working audio, independent stream watching, and fullscreen bars that hide automatically |
| **3 — Viewing refinements** | Planned as needed | Shared previews, richer subscription/layout and grid/focus controls; capacity/performance work driven by observed use |
| **4 — Complete experience** | Planned | UI polish, simpler setup/deployment, packaging refinements, recovery and broader compatibility validation |

## M1 acceptance

M1 is accepted for its current functional scope following practical sign-off. Brief stutters during broadcast startup are a known deferred issue; smooth playback after startup was reported in repeated real use. This is product acceptance with a revised gate, not a declaration that every original benchmark passed.

The previously required recorded ten-minute external benchmark is deferred as a prerequisite for M1 closure. Exact external FPS, packet loss, stalls, latency and long-run stability remain unmeasured where the evidence is absent. Historical automated and real-media results keep their recorded revision, workload and topology limits. No new streaming test was run to record this acceptance, and eight-viewer capacity remains unvalidated.

Experimental application audio remains off by default, initially limited to the supported Steam Geometry Dash profile. It includes child processes; exclusion of future children is not guaranteed. The previously accepted experimental limitation remains in place, and no microphone capture is added. Strict Discord exclusion and calibrated audiovisual timing are not marked passed.

## Follow-up for M2 planning

- The [revised M2 design](superpowers/specs/2026-10-16-milestone-2-design.md) is approved. Review the [M2 implementation plan](superpowers/plans/2026-10-16-milestone-2.md) and select an execution method before product code begins.
- Make concurrent broadcasting the main goal. Remove fixed room participant/publisher quotas and the UI's `/ 8` count; keep ownership, leases, and each stream's playback independent.
- Deliver working audio to browser and in-app viewers. Prefer application audio with Discord included only for explicitly selected entire-screen/system-output audio, but accept incidental Discord capture initially. Strict isolation is deferred; audio remains opt-in and microphone capture stays out of scope. See the [revised M2 audio requirements](M2-HANDOFF.md#revised-m2-audio-requirements-2026-10-16).
- Track brief startup stutter separately from sustained playback. Establish a baseline before selecting a fix; distinguish capture/encoding startup, viewer decode startup and network recovery.
- Carry forward external numerical/stability characterization and retained unexpected-stop investigations. Do not interpret functional M1 acceptance as measured capacity or latency evidence.
- On entering a stream's fullscreen view, hide the top and bottom bars immediately; reveal on cursor movement and hide again after two seconds of inactivity, with keyboard/touch access preserved.
- Remove the one/four/eight-viewer capacity campaign and large-group external acceptance gate. Use a small two-publication functional check; investigate scaling when real use exposes a problem. Do not introduce the earlier proposed reader cap or bandwidth-admission system.
- Review and approve the implementation plan before coding; the design review is complete. Current product code still has the M1 restrictions until that implementation lands.

## Technical records

- [Original architecture and milestone specification](superpowers/specs/2026-10-13-screen-sharing-design.md)
- [Detailed M1 implementation plan](superpowers/plans/2026-10-13-milestone-1.md)
- [M1 measured results and historical evidence](validation/milestone-1-results.md)
- [M1 review and deferred findings](validation/milestone-1-review.md)
- [Benchmark checklist](validation/milestone-1-checklist.md)
