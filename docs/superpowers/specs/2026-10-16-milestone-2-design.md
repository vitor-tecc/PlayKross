# Milestone 2: multiple viewers and working audio

Date: 2026-10-16 (America/Sao_Paulo)

Status: Proposed design for user review. The revised requirements are approved; this design and the subsequent implementation plan are not yet approved. No M2 product code or live-media validation accompanies this document.

Baseline: `1ec3c3d` on `main`, which contains M1 integration PR #3. Continue on a new `codex/` branch in the existing project folder. Use the repository-local contributor identity and the GPT-6.1 Sol agent policy in [AGENTS.md](../../../AGENTS.md).

## 1. Outcome and scope

Friends can hear and watch one Windows broadcaster from one, four, and eight viewers without adding encoder instances for each viewer. A slow or abandoned viewer cannot accumulate unbounded application queues or consume a reader slot indefinitely. Capacity decisions explain when the configured host budget cannot support another viewer.

Working audio is required. Application audio should omit unrelated Discord; explicit entire-screen/system-output audio can include it. Incidental Discord capture is acceptable initially, so strict exclusion does not gate M2. Audio stays opt-in and microphone capture remains absent. These requirements supersede conflicting historical restrictions, as recorded in the [M2 handoff](../../M2-HANDOFF.md).

Retain H.264/Opus, one WHIP publication, WHEP readers, NVENC, the explicit capped CPU fallback, repeated H.264 headers, anonymous membership, publication ownership, disconnect grace, and native controller/source lifetime checks. Multiple simultaneous broadcasters per room, previews, alternate quality renditions, grid/focus, and automatic shared-bitrate adaptation remain M3 or later work.

Reported upload capacity is an input to configuration, not measured proof. Local browser processes establish local load behavior; external acceptance needs separate internet connections. M1 practical acceptance remains in force and its historical records are not rewritten.

## 2. Current implementation findings

| Area | Observed baseline | M2 consequence |
| --- | --- | --- |
| `RoomStore` | Eight participants including the publisher; one publisher per room | Default to nine participants and retain one publisher; count media readers separately |
| Playback authorization | Sixty-second read grants; valid grants can authorize multiple handshakes | Reserve at WHEP POST and bind each read grant to one media-session attempt |
| `MediaProxy` / media auth | Scoped authorization, resource ownership, post-handshake revocation recheck; auth callback can report the MediaMTX session ID | Preserve these checks and associate the callback ID with the pending reservation |
| Resource/session stores | Entries are retained until DELETE/revocation; no periodic live-inventory reconciliation | Add reconciliation and explicit pending/active/closing accounting |
| Host traffic | Reads private MediaMTX inventory and byte deltas; aggregate NIC totals are not stream attribution | Share bounded inventory sampling; retain per-reader observations and unavailable values |
| Browser player | Independent peer, muted start, three timed retries and bounded rolling diagnostics | Add admission errors and viewer-specific slow-playback recovery |
| Audio contracts and engine | `AudioSelection` contains an optional process only; Geometry Dash/fixture policy in managed and native code | Introduce explicit modes and update both sides of the boundary |
| Native guard | Permits one named, generation-bound `jk_process_audio` route; controller and video loss stop publication | Generalize the approved route to its selected mode without permitting arbitrary inputs |
| Audio buffering | Application PCM queue drops oldest blocks beyond 200 ms | Reuse this bound and expose queue/drop diagnostics for both audio modes |
| Acceptance validator | Historical schema requires strict Discord exclusion and unrelated original M1 gates | Add a versioned M2 report; keep interpretation of historical M1 reports unchanged |

These are source-review findings, not performance results or proof of an audio failure's cause.

## 3. Approaches considered

1. **Recommended: extend the existing native pipeline and MediaMTX fan-out.** Add a host admission service, reconcile actual media sessions, and implement explicit guarded audio modes. This retains the working video path and limits changes to the missing controls.
2. **Use standard OBS output capture directly.** This saves endpoint-capture code, but still needs native route auditing, readiness diagnostics, fixed-device validation, and controller-lifetime integration. Merely allowing `wasapi_output_capture` through today's policy would leave those checks incomplete. Prefer a small application-owned endpoint source sharing the existing guard and PCM conventions.
3. **Replace transport or encode separately for constrained viewers.** This introduces a second media architecture or rendition system before the existing path has been measured at eight viewers. Defer it; M2 disconnects or reconnects a struggling reader rather than changing every viewer's quality.

## 4. Media architecture and limits

The flow remains: selected video plus explicitly selected audio -> one native H.264/Opus pipeline -> one WHIP publication -> MediaMTX -> up to eight independent WHEP resources. Room APIs and SignalR carry control and status; ASP.NET does not proxy RTP media or retain media-frame queues.

Add validated host options under `MediaAdmission` and pass the same limits through development/public startup and packaging documentation:

| Setting | Proposed initial value | Meaning |
| --- | --- | --- |
| Room participants / publishers | 9 / 1 | One broadcaster plus eight distinct viewer identities |
| Readers per stream | 8 | Includes pending, active, and closing WHEP reservations |
| Host egress budget | 200 Mbps | Operator-configured media allowance across all rooms; adjustable for the actual host |
| Per-reader overhead multiplier | 1.25 | Planning allowance for protocol traffic/retransmission; not a measured guarantee |
| Audio bitrate allowance | 0.128 Mbps | Reserve even in Off mode, matching the existing Opus pipeline |
| Pending handshake deadline | 20 seconds | Bounds application reservation/handshake work; uncertain upstream cleanup stays charged |
| Inventory poll / stale threshold | 2 seconds / 6 seconds | New admissions pause if complete live inventory cannot be trusted |
| MediaMTX write queue / max readers | 64 / 8 | Finite per-reader queue and independent server-side count limit |

The 200 Mbps budget is a proposed default suitable for a reported approximately 250 Mbps upload with spare capacity. It does not detect the connection's speed. Document adjustment before external hosting; show configured budget, reserved amount, and observed media traffic separately.

Reader cost is `ceil((videoKbps + 128) * 1.25)` in integer kbps. At 15 Mbps video, eight readers reserve 151.280 Mbps; at 25 Mbps, they require 251.280 Mbps and do not fit the default. Admission rejects excess demand with an actionable capacity message rather than oversubscribing or lowering every viewer's quality. Include local viewers in reservations conservatively, even when their traffic does not leave the PC. Publisher-to-host upload is a separate path to validate.

All rooms share the host budget. The existing maximum of sixteen rooms remains bounded. Every WHEP resource consumes a slot, including additional tabs and a broadcaster watching their own stream. Multi-tab room presence stays supported; it does not grant unlimited playback resources. Eight external viewers means the broadcaster must not also open a ninth self-view resource.

## 5. Admission, publication settings, and cleanup

Introduce one host-owned admission service with a single synchronization boundary for budget and reader counts. It owns publication bitrate revisions and reader reservations; do not scatter independent counters across endpoints. Never await network calls while holding its lock. Define a fixed lock order for grant authorization, admission, and resource registration; admission must not call back into `RoomStore` or acquire the grant-store lock while holding its own lock.

### Publication settings

Extend the typed publication-reservation request to include validated broadcast settings and a settings revision. The host needs the requested bitrate ceiling before it grants playback; missing metadata must not be treated as zero. Update the native client, core interface/controller, and protocol fixtures together. An old client without this metadata receives an upgrade-required validation error.

Before a running stream increases bitrate, atomically reprice its pending, active, and closing readers against the global budget. If the increase does not fit, reject it before stopping the current engine and retain the previous stream/settings. Keep new reservations on the accepted revision. Hold the previous, higher allowance through a bitrate decrease until the replacement publication is confirmed; a failure or uncertain old-session cleanup cannot release the difference prematurely.

CPU fallback can apply a lower bitrate than requested; retaining the requested ceiling is conservative. An independently reported lower applied rate may improve diagnostics, but cannot silently increase the admitted ceiling. Settings restart preserves stream ownership and identity and obtains a fresh WHIP grant. The implementation plan must specify prepare/confirm/rollback handling so cancellation does not publish at an unreserved rate or strand a settings transaction.

### Read-session lifecycle

1. `GET .../playback` still issues a scoped short-lived grant; OPTIONS validates it without reserving bandwidth.
2. At WHEP POST, validate ownership, path, content type, and bounded SDP. Atomically reserve count and budget before forwarding upstream. Recheck the grant when committing the reservation.
3. Bind the read grant to one pending attempt. The private MediaMTX auth callback may validate that same attempt repeatedly for the same media ID, but cannot bind the grant to a different ID. A grant cannot create simultaneous peers or be reused after completion/expiry; a retry obtains a fresh grant.
4. Associate the callback's media ID with the reservation before accepting the created resource. Require a valid resource Location and matching nonempty media ID. Commit HTTP resource ownership after the SDP response, retaining the existing post-await revocation check, but keep the reservation pending until private inventory confirms the peer connection is established. Returning HTTP 201 alone must not cancel the pending deadline.
5. On cancellation, timeout, malformed upstream response, lost response delivery, or revocation, mark closing and revoke further use of the attempt. DELETE/kick the known upstream session using an independent bounded cleanup token. Definitive rejection with no created session can release immediately; uncertain creation stays charged until reconciled.
6. DELETE, room Leave, session expiry, publisher stop, source/controller loss, host shutdown, and restart all converge on idempotent close/release. A repeated callback or cleanup completion cannot release another reservation or decrement capacity twice.

Poll the complete private MediaMTX inventory every two seconds using a bounded client and pagination. Share this sample between reconciliation and diagnostics. Match ID, generated path, and read/publish role; never infer ownership from nickname or remote address. Expire abandoned pending attempts, remove confirmed-dead resources, and release closing reservations only after a successful kick/DELETE or confirmed absence from a complete inventory. For an in-flight handshake, settle/cancel upstream work and retain a cleanup tombstone for late callbacks/responses before absence can release the reservation; a premature 404 must not free a slot while its session can still be created. A peer that never connects must not survive its pending deadline merely because it has an HTTP resource.

If the admin API is unavailable or inventory is partial/stale, preserve known reservations and pause new WHEP admissions. Existing healthy streams continue. Retry cleanup without an unbounded queue; coalesce by reservation/session ID. On host startup, reconcile/kick leftover sessions on application-generated paths before opening new admission. The existing in-memory room/session credentials are not restored after restart.

Use typed problem responses: `409 reader_capacity` or `409 bandwidth_capacity`, `409 reader_cooldown` with Retry-After, `503 media_inventory_unavailable` with Retry-After, and existing authentication/validation statuses for those failures. The browser displays the actual reason. Capacity rejection ends automatic retries; cooldown waits consume the same bounded recovery budget as other retries. Transient network/inventory failures retain a bounded retry path. Do not expose tokens, upstream URLs, or admin credentials in errors.

## 6. Bounded queues and slow viewers

Keep independent MediaMTX readers. Set `writeQueueSize: 64` and `pathDefaults.maxReaders: 8` in both source configurations and generated profiles, retaining pinned versions and no-recording behavior. Review the pinned reader implementation: its queue contains callbacks for media units, and overflow increments discarded-frame counters. A queue setting is not a millisecond or byte bound and does not prove low end-to-end latency. The proposed size must pass the real-media tests; increasing it to hide a stalled viewer is not an acceptable fix.

Use per-session `outboundFramesDiscarded` deltas from the private API. After a ten-second initial connection warm-up, growth across three consecutive fresh two-second inventory samples closes only that reader and applies a ten-second reconnect cooldown for that owner/path. Counter resets start a new observation window. New viewers must not change shared encoder settings or restart the publisher. If this policy proves too sensitive under legitimate bursts, adjust it from retained measurements and review the change.

On visible, playing browser video after the same warm-up, trigger a viewer-only reconnect when presentation makes no progress for two seconds or rolling average jitter-buffer residence exceeds 250 ms for five seconds. Use deltas of `jitterBufferDelay / jitterBufferEmittedCount`, scoped to the same SSRC/connection; missing or reset fields remain unavailable. Paused/hidden tabs do not trigger the visible-playback heuristic. This metric is receiver buffer residence, not capture-to-display latency.

Close the old peer/resource before opening a fresh one. Retain bounded retry attempts, respect server cooldown, and stop after three unsuccessful automatic recoveries until the user presses Retry. Reset the recovery budget only after thirty seconds of healthy visible playback. An unsupported metric does not establish a passed delay bound; use presentation progress and real timing evidence. A stalled unmanaged receiver remains contained by server queues and the reader cap even if it sends no browser telemetry.

Expose host/broadcaster process private memory and viewer observations during testing. Require bounded reservation/store sizes and no continuing memory/delay growth under the sustained slow-reader workload. Configuring a finite queue alone cannot establish the byte footprint or WebRTC's internal buffering. If real tests reveal continued delay growth or healthy-reader degradation, investigate that layer before accepting M2; do not create a per-viewer encoder as an automatic workaround.

## 7. Explicit audio modes

Replace the nullable-process-only selection with a validated mode plus its identity: `Off`, `Application(process identity)`, or `SystemOutput(render endpoint ID)`. Reject missing, contradictory, or unknown fields in both managed and native entry points. Distinguish selected, initialized, capturing, emitted, and viewer-audible observations in status/diagnostics.

### Application

Reuse process-loopback capture and exact PID/creation-time/path binding. Replace the Geometry Dash-only product allowlist with explicit selection of an identifiable standalone application. Keep direct Discord, general/shared browser hosts, the controller/helper, and room-viewing processes ineligible initially; report unsupported sources clearly. This preserves the preference for unrelated Discord to stay out without promising perfect child-process exclusion. No application launch, job confinement, or future-child exclusion mechanism is required for M2.

Continue capturing the selected process tree, including children. Process exit, PID reuse, access failure, or a changed selection cannot attach to another application. Surface the error and stop the selected audio/publication through the existing guard path. The user can explicitly restart with Audio Off or select another source. Never fall back from application capture to system output.

### System output

Offer `System output (all sound on selected output)` for monitor sharing. This is a product choice matching the requested whole-screen mode: Windows captures a rendering endpoint's mix, not audio associated with a particular monitor. Application audio remains available independently for monitor or window video.

Enumerate active render endpoints in a separate Windows audio catalog. Display friendly device names. A default-output shortcut resolves to a concrete endpoint ID at selection/start; keep that ID fixed for the capture generation and across settings restarts. Do not follow later default-device changes. Missing/disabled/replaced devices produce an actionable stop and require explicit reselection. A window-video selection cannot retain System output silently.

Implement a small application-owned native WASAPI endpoint-loopback source, `jk_system_audio`, using the selected render endpoint, shared-mode conversion to the established 48 kHz stereo float PCM format, the existing bounded PCM queue, and timestamp/error conventions. Keep process and endpoint acquisition separate while sharing PCM delivery and diagnostics. Verify the device is an active rendering endpoint; reject capture endpoints and generic `default` identifiers in native requests. Keep generic OBS input-device/global audio sources disabled.

Extend native source readiness, scene routing, guard arming, IPC validation, and diagnostics together. The armed selection identifies one route by type, source name, generation, and selected identity. Each PCM delivery checks the live controller binding and selected generation. Only that route can reach the existing Opus mix; monitoring remains disabled, and controller/video-source loss stops capture. Mode/source changes destroy the old source and require an explicit restart. Endpoint capture must not rely on an unrelated application's process lifetime.

System output includes Discord, other programs, and room playback routed to that endpoint. The UI says so before Start and while live. Keep the broadcaster's own in-app stream muted with sound enablement disabled during System output publishing; restore user control when it ends. External browsers are outside that local control, so explain that playing the same room through the captured output will be recaptured. Use another output or mute local playback. Do not claim system audio excludes feedback or add microphone capture/echo cancellation as a substitute.

### Failure and verification

Audio initialization failure prevents Start in the requested mode and gives a specific reason. Silence from a quiet application/device is valid and must not be treated as initialization failure. Captured/emitted counters and received Opus packets support diagnosis but do not prove audibility. Add native source mode, capture readiness/error, PCM queue duration/drops, emitted frames, and active video/audio encoder counts. Existing audio diagnostics must use the selected source rather than the hard-coded `JK Application Audio` name.

## 8. Contracts and responsibility boundaries

| Component | Responsibility/change |
| --- | --- |
| `JKiller.Contracts`, core room/media interfaces | Typed audio modes, publication settings/revision, admission problem types, additive diagnostics |
| Host admission service | Atomic counts/budget, per-attempt lifecycle, revision transitions, cooldowns; no media buffering |
| Room/media endpoints and private auth | Membership/scoped authorization, registration and cleanup coordinated with admission |
| Private MediaMTX inventory client/reconciler | Complete bounded samples, ID/role correlation, orphan cleanup and freshness |
| Windows audio catalog and WPF model/view | Explicit mode/device selection, eligibility/status, selected-output explanation |
| `ObsMediaEngine`, native audio sources and guard | Mode-aware setup, exact identity/lifetime, PCM acquisition and emission checks |
| Browser WHEP/player/diagnostics | Typed capacity handling, one peer per player, bounded recovery, audio/presentation observations |
| Acceptance tooling | Separate M2 evidence schema/report and fan-out/slow-reader/audio scenarios |

Keep changes focused on these paths. Do not remove source/controller identity checks, broaden arbitrary OBS RPC access, modify personal OBS configuration, change deployment topology, or retune video encoding without relevant evidence.

## 9. Validation and acceptance

Use test-first implementation for state transitions and native routing. `Verify.ps1 -Mode CodeOnly` remains the noninteractive correctness path; it does not install a new native plugin or launch capture/audio fixtures. Rebuild/install matching native components with the owned broadcaster stopped before live validation; package matching managed/native components together.

Automated checks cover concurrent final-slot/final-budget admission; grant reuse and auth callback binding; OPTIONS without reservations; failed/aborted/late handshakes; repeated cleanup; revocation during negotiation; admin timeout/partial inventory; restart/orphan reconciliation; settings increase rejection before engine stop; revision rollback; multi-tab resource accounting; reader cooldowns; and browser cleanup/retry limits. Exercise mode validation, microphone rejection, application PID reuse/exit, endpoint replacement/default switch, stale generation, controller loss, and unauthorized native audio routes. Use deterministic clocks for lifecycle policies.

Version the M2 acceptance report independently of the original M1 validator. Report each required case as passed, failed, or pending with revision, settings, topology, duration, and evidence provenance. Strict Discord exclusion is informational/deferred; intended audio delivery is required. A known failure cannot become pending because another measurement is missing. Keep automated correctness, measured streaming, and manual listening results separate.

Real-media matrix:

| Case | Required evidence |
| --- | --- |
| One, four, eight viewers | Same 1080p60/15 Mbps high-motion source; each case runs at least ten steady-state minutes after separately measured startup; actual decode/presentation and audible sound at every viewer |
| Shared pipeline | Exactly one active video encoder, one active Opus encoder, and one WHIP publication; counts stay constant as viewers join/leave; applied NVENC initialization/activity documented |
| Slow viewer | One of eight viewers has only its own media path constrained below the stream rate for at least two minutes, then blackholed for at least one minute; healthy viewers stay playing; retain recovery and memory/time-series evidence |
| Cleanup/churn | Repeated join/stop/tab-close, handshake abort, reconnect, Leave, and source/controller loss; all resources/reservations converge to actual live readers without increasing retained-state/memory growth |
| Audio modes | Intended app/fixture and system-output sound received in browser and WebView2; Off, sound enablement/mute, stop/restart, process exit, endpoint loss and default-device switch exercised |
| Discord/feedback characterization | Record actual inclusion/exclusion for each mode using identifiable test sounds; System output includes sounds routed to the selected endpoint; in-app self-playback restriction verified; strict isolation remains deferred |
| External paths | Separate internet connections among the simultaneous real viewers, including a constrained external reader; record each device/browser/connection and direct/relayed ICE path; local tabs cannot substitute for this evidence |

Reuse the original suitable-path engineering targets for the 1080p60 baseline: average presented FPS at least 57, at least 95% of five-second windows at 55 FPS or above, no steady-state freeze longer than 500 ms, render/encoding skipped ratios each below 1%, measured video-latency p95 at most 500 ms, and measured audiovisual offset within 100 ms at the start and end. Apply the healthy-viewer targets during slow-reader isolation. Use calibrated timing with disclosed uncertainty; RTT and jitter-buffer residence do not establish video latency.

Record publisher, host, and receiver load separately. For bounded-memory evidence, record per-process private bytes every second, compare the medians of the first and last steady-state minutes, and require growth no greater than the larger of 64 MiB or 10% per application-owned process over a ten-minute case. Browser process measurements are reported with their ownership/sharing limits. Verify live resources return to baseline within ten seconds after a confirmed media close when the admin API is healthy. These are proposed regression thresholds, not theoretical memory guarantees. Retain failed runs rather than adjusting thresholds after seeing their results.

The slow-reader harness must shape/drop the actual selected UDP/TCP media flow for only that viewer; HTTP route delays or browser offline toggles alone do not establish media impairment. Keep local and external results separate. Live device activity and external participation are scheduled explicitly during implementation; no friends are contacted automatically and no private audio recordings are required. Missing equipment or participants leaves the affected acceptance pending and M2 unaccepted.

## 10. Risks and decisions carried into planning

- The proposed 64-unit queue and slow-reader thresholds require workload validation. A stock MediaMTX counter reports discards, not queue age; do not fabricate unavailable queue measurements.
- A configured bitrate/budget is an admission estimate. Observe actual egress and report overload or retransmission excess; the service is not a traffic shaper or an upload speed guarantee.
- Custom endpoint capture adds a small native adapter but retains per-block lifetime checks. Device format conversion, timestamps, and unplug behavior need real audio tests.
- Broader application capture can remain incompatible with some applications. System output provides an explicit alternative for monitor sharing; neither mode may silently substitute for the other.
- M1 startup stutter is measured separately. Change it during M2 only if evidence shows a relevant regression or it prevents M2 acceptance.
- This design is the review artifact. After approval, write the small-step implementation plan with concrete files, failing tests, commands, and review checkpoints before product code begins.

## 11. Technical basis inspected

Source inspection establishes available mechanisms, not successful performance:

- [MediaMTX 1.21.2 configuration](https://github.com/bluenviron/mediamtx/blob/v1.21.2/mediamtx.yml): outgoing queue configuration and path reader limit.
- [MediaMTX 1.21.2 reader](https://github.com/bluenviron/mediamtx/blob/v1.21.2/internal/stream/reader.go) and [stream](https://github.com/bluenviron/mediamtx/blob/v1.21.2/internal/stream/stream.go): individual bounded reader queues and discard accounting.
- [MediaMTX 1.21.2 WebRTC session](https://github.com/bluenviron/mediamtx/blob/v1.21.2/internal/servers/webrtc/session.go) and [API schema](https://github.com/bluenviron/mediamtx/blob/v1.21.2/api/openapi.yaml): reader lifecycle, session identity, readiness, bytes and discarded-frame observations.
- [Microsoft WASAPI loopback recording](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording): rendering-endpoint mixed-output capture.
- [OBS 32.2.2 WASAPI source](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-wasapi/win-wasapi.cpp), also present in the local pinned source tree: endpoint selection and reconnect behavior considered in the alternative analysis.
- [W3C WebRTC statistics](https://www.w3.org/TR/webrtc-stats/): jitter-buffer residence counters and their interpretation.
