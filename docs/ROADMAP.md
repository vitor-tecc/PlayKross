# PlayKross milestones

Updated 2026-10-18. This is the current milestone-status overview. M2 has the owner's functional acceptance and M3 development has begun. The revised scope moves concurrent broadcasting into M2 and removes its fixed room-size and eight-viewer testing requirements. Original specifications, plans and measurement records remain available as historical context; they do not override the revised scope or milestone acceptance below.

| Milestone | Status | Scope |
|---|---|---|
| **1 — Core streaming** | **Accepted with known limitations** | One native Windows broadcaster, browser and in-app viewing, anonymous rooms, capture/settings controls, diagnostics, self-hosted connectivity and a portable broadcaster ZIP |
| **2 — Concurrent broadcasting** | **Accepted after owner functional testing** | Multiple simultaneous broadcasters, no fixed per-room participant/publisher cap, explicit application/system audio, independent stream watching, and fullscreen bars that hide automatically; see [acceptance](validation/milestone-2-results.md) |
| **3 — AMD and browser broadcasting** | **In progress; native AMF code verified, browser implementation/live acceptance pending** | Main goal: AMD AMF H.264 compatibility with minimal broadcaster CPU/GPU workload and game impact. Additional feature: start broadcasts from the website using browser capture, without the desktop app. Preserve one shared feed per broadcaster. Shared previews are scrapped; Intel is deferred. The initial native theme/cursor and compact website volume changes remain delivered work. See [M3 handoff](M3-HANDOFF.md) |
| **4 — Complete experience** | Planned | UI polish, simpler setup/deployment, packaging refinements, recovery and broader compatibility validation |

## M1 acceptance

M1 is accepted for its current functional scope following practical sign-off. Brief stutters during broadcast startup are a known deferred issue; smooth playback after startup was reported in repeated real use. This is product acceptance with a revised gate, not a declaration that every original benchmark passed.

The previously required recorded ten-minute external benchmark is deferred as a prerequisite for M1 closure. Exact external FPS, packet loss, stalls, latency and long-run stability remain unmeasured where the evidence is absent. Historical automated and real-media results keep their recorded revision, workload and topology limits. No new streaming test was run to record this acceptance, and eight-viewer capacity remains unvalidated.

Experimental application audio remains off by default, initially limited to the supported Steam Geometry Dash profile. It includes child processes; exclusion of future children is not guaranteed. The previously accepted experimental limitation remains in place, and no microphone capture is added. Strict Discord exclusion and calibrated audiovisual timing are not marked passed.

## M2 acceptance and M3 entry

On 2026-10-18 the project owner confirmed that **every item in the remaining M2 closure list was tested and works as intended**: audible Application/System/Off delivery in browser and WebView (including the corrected browser Application selection), full System publication and native playback mute/restoration, application/endpoint loss and pinned-device behavior through default-device changes, and manual browser fullscreen Escape. M2 is accepted on that human functional evidence. The tested package hash, exact device/browser matrix and numerical results were not supplied; do not invent them or relabel earlier failed automated runs. See [M2 results](validation/milestone-2-results.md).

M3 began with the owner's three bounded interface/cursor changes. The owner then replaced shared previews with **AMD AMF H.264 broadcasting compatibility** as M3's main goal and added **website broadcasting using the browser's own capture method** as another M3 feature. Minimizing broadcaster CPU/GPU workload and interference with games is the guiding priority. Keep one shared feed per broadcaster; do not add preview encodes on the broadcaster or room host. Intel Quick Sync is deferred because none of the current friend group uses Intel GPUs. Browser capture/encoding is browser-managed and does not promise native AMD encoder control. This revised scope supersedes historical M3 preview requirements. See [M3 handoff](M3-HANDOFF.md).

## Implemented M2 scope and retained evidence

- The [revised M2 design](superpowers/specs/2026-10-16-milestone-2-design.md) and [implementation plan](superpowers/plans/2026-10-16-milestone-2.md) are approved for subagent-driven execution. Implementation and code reviews are complete; the recorded functional checks still determine milestone acceptance.
- Make concurrent broadcasting the main goal. Remove fixed room participant/publisher quotas and the UI's `/ 8` count; keep ownership, leases, and each stream's playback independent.
- Deliver working audio to browser and in-app viewers. Prefer application audio with Discord included only for explicitly selected entire-screen/system-output audio, but accept incidental Discord capture initially. Strict isolation is deferred; audio remains opt-in and microphone capture stays out of scope. See the [revised M2 audio requirements](M2-HANDOFF.md#revised-m2-audio-requirements-2026-10-16).
- Track brief startup stutter separately from sustained playback. Establish a baseline before selecting a fix; distinguish capture/encoding startup, viewer decode startup and network recovery.
- Carry forward external numerical/stability characterization and retained unexpected-stop investigations. Do not interpret functional M1 acceptance as measured capacity or latency evidence.
- On entering a stream's fullscreen view, hide the top and bottom bars immediately; reveal on cursor movement and hide again after two seconds of inactivity, with keyboard/touch access preserved.
- Remove the one/four/eight-viewer capacity campaign and large-group external acceptance gate. Use a small two-publication functional check; investigate scaling when real use exposes a problem. Do not introduce the earlier proposed reader cap or bandwidth-admission system.
- The owner has now confirmed the remaining functional checks. Preserve the implementation and historical measurements; further work belongs to M3 unless a regression appears.

## Technical records

- [Original architecture and milestone specification](superpowers/specs/2026-10-13-screen-sharing-design.md)
- [Detailed M1 implementation plan](superpowers/plans/2026-10-13-milestone-1.md)
- [M1 measured results and historical evidence](validation/milestone-1-results.md)
- [M1 review and deferred findings](validation/milestone-1-review.md)
- [Benchmark checklist](validation/milestone-1-checklist.md)
