# Milestone 2: concurrent broadcasting, working audio, and fullscreen viewing

Date: 2026-10-16 (America/Sao_Paulo)

Status: Revised proposed design for user review. The user replaced the earlier viewer-capacity milestone with concurrent broadcasting, removed the room-size cap and eight-viewer testing requirement, and requested fullscreen controls that hide automatically. This document replaces the previous proposal in this file; product implementation has not begun.

Baseline: M1 integration `1ec3c3d`; documentation work continues on `codex/m2-design` in the existing project folder. Use the repository-local contributor identity and the GPT-6.1 Sol agent policy in [AGENTS.md](../../../AGENTS.md).

## 1. Goal and scope

The main goal is for multiple people to broadcast in the same room at the same time. Each person publishes one independent stream from their Windows app. Room members can choose which streams to watch, including while broadcasting themselves. Working audio and fullscreen controls that get out of the way are also required.

Remove the fixed per-room participant and publisher limits. Do not introduce a replacement reader cap or bandwidth-admission policy. The user prefers to address actual capacity problems when they appear in personal use. There is no required one/four/eight-viewer capacity benchmark, large-group test, or external eight-viewer sign-off. This is a scope change, not evidence that any particular room size performs well.

Keep one shared H.264/Opus encoding pipeline per broadcaster, independent of that stream's viewer count. More broadcasters naturally mean more independent publications; do not create a new encoder per viewer. Retain NVENC, the explicit capped CPU fallback, repeated H.264 headers, scoped grants, stable identity, ownership, publication leases, and source/controller lifetime checks.

Carry forward the approved audio requirements: audio is opt-in, application capture should leave unrelated Discord out, explicit system-output capture can include Discord, incidental Discord capture is acceptable initially, and microphone capture is absent. Strict exclusion does not block this milestone.

Shared low-quality previews, new renditions, an advanced grid/focus interface, automatic bitrate adaptation, proactive bandwidth admission, and scale benchmarking move to future work driven by actual need. Reuse the existing stream cards and responsive room layout.

## 2. Current implementation and approach

| Area | Existing behavior | Proposed change |
| --- | --- | --- |
| `RoomStore` | Eight participants and one publisher per room; publications already keyed by stream and owner | Remove per-room count restrictions; preserve one publication per owning session |
| Browser room | Maps all snapshot streams to independent players; count displays `/ 8` | Keep independent players and remove the displayed maximum |
| Ownership/revocation | Grants and resources scoped by session and generated path | Verify stopping/leaving one publisher cannot affect another publisher |
| Windows client | Explains publication conflicts as one-broadcaster-only | Remove that assumption and update native help text |
| Host diagnostics | Multiplies total reader count by the sum of stream bitrates | Attribute readers to paths before estimating per-stream/room traffic |
| Fullscreen | Entire stream card, including heading, status, controls and diagnostics | Make bars overlays that hide without changing video geometry |
| In-app viewer | Same room page; fullscreen events already make its WPF window borderless/maximized | Reuse that integration and verify restoration |
| Audio | Optional process selection; Geometry Dash/fixture-only managed/native policy | Add explicit modes and update both entry points |

Recommended approach: extend the existing room and media model. It already separates stream paths, player components and ownership; the room-wide one-publisher check is the main functional restriction. A new SFU, per-viewer encoding, or replacement room model would add unnecessary work. Merely increasing numeric limits would retain the constraint the user asked to remove.

## 3. Concurrent room behavior

Remove participant-count checks from Join and Rejoin and the publisher-count check from publication reservation. Remove their configuration/validation fields rather than replacing eight with a larger number. Keep unrelated room-creation limits, credential expiry, request-size validation, rate limiting and existing infrastructure resource safeguards. Those safeguards are not a configurable per-room people quota and must not be described as one. Do not add a new limit on simultaneous streams or WHEP readers.

Each session continues to own at most one publication. Repeated/concurrent Start requests by the same session return the same stream identity; different sessions receive different stream IDs and generated paths. Two sessions with the same nickname remain independent. Renaming changes the displayed owner name without replacing players or disrupting playback.

Publishers A and B can publish concurrently. A viewer can watch A, B, both, or neither through the existing per-card Watch/Stop controls. Watching is explicit; joining the room must not open a decoder for every stream automatically. Each card retains independent sound, fullscreen, reconnect and cleanup state. Do not add shared preview streams or a custom focus-layout system.

Stopping, losing the source, timing out, or leaving as A revokes A's publication and readers of A's path. B's publication, lease and viewers remain active. Leaving as an ordinary viewer closes only that viewer's media resources. Preserve reconnect grace, per-tab tracking, cross-room isolation and authorization. Do not kick all sessions in a room when only one generated path is revoked.

Update the browser count to show the actual number of people without a denominator. Remove one-publisher and measured-capacity promises from client messages, native help text and current setup/README guidance when implementation lands. No application-managed admission queue or bitrate registration transaction is required.

For diagnostics, count readers by exact generated stream path and calculate each estimate from that stream's latest known applied bitrate and reader count. Sum estimates for a room only when the required observations are available. Missing publisher data stays unavailable. Actual MediaMTX traffic and aggregate NIC counters retain their provenance; do not use a cross-product of all publishers and all readers. Global resource safeguards and inventory sampling limits are not evidence of proven scale.

## 4. Fullscreen bars

The requested top/bottom bars are the stream card's heading and playback UI inside the browser or in-app fullscreen viewer. Group the top heading and bottom status/controls/diagnostics into overlays over the video. In fullscreen, video occupies the available area with its existing aspect-ratio behavior. Showing/hiding bars must not resize, crop, remount or reconnect the stream. Outside fullscreen, keep normal visible room controls.

On successful fullscreen entry, hide both overlays and the idle cursor immediately. Entry alone must not reveal the bars because the activation button retained pointer focus. Pointer movement inside that fullscreen player reveals both bars and the cursor and starts a two-second idle timer. Further movement restarts the timer. At expiry, hide them again unless the user is interacting with controls. Pointer exit hides them when no control interaction is active.

Keep bars visible while the pointer is over interactive controls, a control is being pressed, or keyboard focus is actively navigating the overlay. Release that hold on pointer/focus exit and resume the idle timer. Moving over the video alone reveals temporarily and does not hold bars open. Prevent click-through to obscured controls when hidden.

Keyboard interaction, particularly Tab navigation, reveals controls and provides visible focus; Escape still leaves fullscreen. Touch/pointer-down on the video reveals controls so touch users can exit, mute or resume playback. These are accessibility alternatives to cursor movement. Preserve browser-managed fullscreen exit behavior.

On exit, cancel idle timers, restore normal cursor/control visibility and retain the same video element, peer connection, sound selection and playback state. Clean up listeners/timers on unmount and when the fullscreen element changes. Only the active fullscreen card responds; other cards keep normal controls.

Routine status remains in the bottom overlay. An actionable failure or blocked-playback prompt can reveal controls to make Retry/Play available. In-app fullscreen continues using WebView2's existing event and restores the previous WPF window style/state on exit. Browser-owned temporary notices are outside the application's bar visibility control.

## 5. Working audio

Represent selection explicitly as `Off`, `Application(process identity)`, or `SystemOutput(render endpoint ID)`. Managed/native entry points reject inconsistent or unknown modes/identities. Selecting a display never silently enables system capture. Do not add microphone selection or capture.

### Application mode

Reuse exact PID/creation-time/path binding and process-tree loopback. Broaden the Geometry Dash-only product allowlist to explicitly selected identifiable standalone applications. Keep direct Discord, shared/general browser hosts, the controller/helper and room-viewing processes ineligible initially. Explain unsupported applications without promising child-process isolation; incidental Discord capture remains accepted.

Process exit, PID reuse, unavailable identity or source change cannot attach to a replacement application. Surface the error and use the existing guard stop path. The user can explicitly restart with Audio Off or another source. Never fall back automatically to system output.

### System output mode

Offer explicit `System output (all sound on selected output)` for monitor sharing. Audio belongs to a Windows render endpoint's mix, not a monitor. Enumerate active render endpoints separately and display friendly names. Resolve a default-output shortcut to a concrete ID at selection/start, retain that ID through capture/settings restarts and require reselection if it disappears. Do not silently follow a changed default device. Switching to window video cannot retain system audio silently.

Use an application-owned native WASAPI endpoint-loopback source, `jk_system_audio`, sharing the existing PCM conventions and 200 ms drop-oldest queue. Validate an active render endpoint and reject capture devices. Extend scene policy, route readiness, private IPC, arm/disarm, diagnostics and per-block controller/generation checks together. Permit only the selected route into the existing Opus mix, with monitoring disabled; generic global/input-device sources remain disabled.

Application/system capture share delivery/lifecycle conventions but retain distinct acquisition identities. Controller/video-source loss stops capture. Endpoint capture cannot depend on an unrelated process being alive. Mode/source changes destroy the old source and require an explicit restart.

### Broadcasting while watching

Application mode supports publishing and watching other room streams without intentionally capturing the room-viewing process. System mode captures all sounds on its chosen output, including Discord and incoming streams routed there. State that limitation in the UI; strict exclusion is not an acceptance gate.

During system-output publishing, start the in-app viewer muted and prevent enabling its stream sound on the captured endpoint. Explain the option to use application audio or a separate playback output. Keep watching video and concurrent broadcasting available. Independent browsers are outside this local control, so describe that incoming sound played through the captured output will be rebroadcast. Do not claim echo cancellation or headphones remove digital loopback recapture.

Initialization failure prevents Start in the selected mode with an actionable error; a legitimately quiet source is not initialization failure. Show selected mode/device, readiness/error, captured/emitted frames, queue drops and active encoder counts. Replace the hard-coded `JK Application Audio` diagnostics lookup with the selected source. Counters/Opus packets help diagnosis but do not establish audibility.

## 6. Implementation boundaries

| Area | Work to include in the later implementation plan |
| --- | --- |
| Host room store/tests | Remove room participant/publisher quotas; distinct concurrent owners; same-owner idempotency |
| Media grants/proxy/revocation tests | Cross-stream ownership, stop, expiry and viewer cleanup isolation |
| Native room client/app | Remove one-publisher messaging; independent publisher/viewer lifetimes |
| Browser room/player/CSS | Count text, independent player state, fullscreen overlays/timers |
| Diagnostics | Per-path reader/traffic attribution with truthful unavailable observations |
| Audio contracts/core/Windows/native | Explicit guarded modes, picker, setup/error handling |
| Docs/portable distribution | Current multi-publisher behavior, audio limitations, matching components |

Keep current bounded media queues and bounded reconnect behavior. Do not implement the previous proposal's reader reservations, configured 200 Mbps budget, reader cooldown/eviction policy, 64-unit queue retuning, or settings-budget revisions. Diagnose actual scaling failures when observed, as requested.

## 7. Validation sized for this milestone

Remove the required one/four/eight-viewer matrix, eight external viewers, network-shaped slow-reader campaign and numeric capacity/memory thresholds from M2 acceptance. Historical performance reports stay unchanged. Do not create new required large-group tests or treat missing eight-viewer evidence as a blocker. Automated correctness still matters; working audio/concurrent media need small practical checks.

Automated coverage should verify:

- Join/Rejoin beyond the former eight-person boundary with in-memory sessions, without a new fixed maximum. This is a logic regression check, not an eight-viewer media test.
- Two distinct sessions reserve/renew separate publications concurrently; repeated same-owner requests remain idempotent. Another publisher is accepted without a two-publisher ceiling.
- Unauthorized stop/replace, cross-room playback, rename, lease expiry and Leave preserve other publishers and unrelated reader resources.
- Multiple cards retain independent Watch/Stop/sound/reconnect state; fullscreen does not recreate the video/WHEP session.
- Fullscreen entry hides immediately; movement reveals; idle hides; movement resets; control interaction, keyboard and touch work; exit/unmount clears timers. Use deterministic timers and separately confirm actual browser fullscreen.
- Audio mode/identity validation, no microphone, source/controller loss, PID reuse, endpoint loss/default changes and native route/generation checks.
- Per-stream traffic estimates with different reader counts/bitrates do not overcount; missing data remains unavailable.

Use `Verify.ps1 -Mode CodeOnly` for noninteractive correctness during implementation. It does not launch capture/audio fixtures or prove real playback. Rebuild/install matching native components with the owned broadcaster stopped before live checks; package matching components together.

The practical check uses two concurrent publications and an actual decoding viewer that can select both. Verify intended video/audio, stop/restart one publication while the other keeps playing, and broadcast while watching another stream. Use two PCs or independently isolated development/portable roots; do not bypass the current single-root owned OBS lock to launch conflicting helpers. Fixtures establish the local protocol/lifecycle path; document remaining human listening or real-application checks honestly.

Exercise Application, System output and Off; browser sound enable/mute; fullscreen hide/reveal/exit in a browser and WebView2; and feedback limitations while broadcasting/viewing. Record tested devices/settings and observed audible interruptions, timing issues or Discord inclusion. No ten-minute benchmark, fixed FPS/latency threshold, eight-viewer attendance or separate-network load test is required to close this revised milestone. An external friend-group trial is useful when available and is not a prerequisite.

Report automated results, observed real-media behavior and pending functional checks separately. Two tested publishers demonstrate concurrency, not unlimited capacity. Missing eight-viewer measurements are outside this milestone; missing working-audio or concurrent-publishing evidence still needs an honest status.

## 8. Handoff to planning

After review of this revised design, write a focused implementation plan for concurrent publications, audio and fullscreen behavior, with concrete files, failing checks and small reviewable steps. No product code or services need to run merely to revise this design. Preserve M1 acceptance/history and investigate startup stutter only if a relevant regression or observed functional problem justifies it.
