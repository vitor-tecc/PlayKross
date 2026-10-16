# PlayKross milestones

Updated 2026-10-16. This is the current milestone-status overview. Original specifications, plans and measurement records remain available for technical detail; the acceptance decision below supersedes their earlier M1 advancement gate.

| Milestone | Status | Scope |
|---|---|---|
| **1 — Core streaming** | **Accepted with known limitations** | One native Windows broadcaster, browser and in-app viewing, anonymous rooms, capture/settings controls, diagnostics, self-hosted connectivity and a portable broadcaster ZIP |
| **2 — Multiple viewers** | **Next; detailed plan not written** | One broadcaster with one/four/eight viewers, bandwidth admission, bounded queues/delay and isolation of slow viewers |
| **3 — Multiple streams** | Planned | Concurrent broadcasters, previews, subscription selection, grid/focus and simultaneous broadcast/watch load |
| **4 — Complete experience** | Planned | UI polish, simpler setup/deployment, packaging refinements, recovery and broader compatibility validation |

## M1 acceptance

M1 is accepted for its current functional scope following practical sign-off. Brief stutters during broadcast startup are a known deferred issue; smooth playback after startup was reported in repeated real use. This is product acceptance with a revised gate, not a declaration that every original benchmark passed.

The previously required recorded ten-minute external benchmark is deferred as a prerequisite for M1 closure. Exact external FPS, packet loss, stalls, latency and long-run stability remain unmeasured where the evidence is absent. Historical automated and real-media results keep their recorded revision, workload and topology limits. No new streaming test was run to record this acceptance, and eight-viewer capacity remains unvalidated.

Experimental application audio remains off by default, initially limited to the supported Steam Geometry Dash profile. It includes child processes; exclusion of future children is not guaranteed. The previously accepted experimental limitation remains in place, and no microphone capture is added. Strict Discord exclusion and calibrated audiovisual timing are not marked passed.

## Follow-up for M2 planning

- Track brief startup stutter separately from sustained playback. Establish a baseline before selecting a fix; distinguish capture/encoding startup, viewer decode startup and network recovery.
- Carry forward external numerical/stability characterization and retained unexpected-stop investigations. Do not interpret functional M1 acceptance as measured capacity or latency evidence.
- Compare the same source/settings at one, four and eight real decoding viewers, including external and deliberately slow/stalled viewers. Verify encoder count, host upload, memory/delay bounds and healthy-viewer isolation.
- Write and review the detailed M2 design and implementation plan before coding. M1 acceptance permits that planning; it does not itself start M2 implementation or enable multiple broadcasters.

## Technical records

- [Original architecture and milestone specification](superpowers/specs/2026-10-13-screen-sharing-design.md)
- [Detailed M1 implementation plan](superpowers/plans/2026-10-13-milestone-1.md)
- [M1 measured results and historical evidence](validation/milestone-1-results.md)
- [M1 review and deferred findings](validation/milestone-1-review.md)
- [Benchmark checklist](validation/milestone-1-checklist.md)
