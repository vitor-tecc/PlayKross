# Self-hosted screen sharing: architecture and first milestone

Date: 2026-10-13 (America/Sao_Paulo)

Status: Proposed written specification. The user approved the conversational architecture; this document awaits review before implementation planning.

## 1. Intended outcome

Build working software for friends to share gameplay and react together. Broadcasters use a native Windows application; viewers use modern browsers without installation or accounts. A broadcaster can watch other streams while publishing. The room host can initially be the user's PC and is independent of the broadcasters joining it.

The primary performance objective is consistent 1920x1080, 60 FPS delivery to approximately eight viewers of one stream, with interactive latency. Parsec is a reference for perceived smoothness, not a promised equivalence. Previously poor website streaming does not establish that browser playback, upload bandwidth, or any particular component caused the problem.

The non-negotiable audio rule is to exclude Discord and incoming stream audio at capture. A viewer muting Discord is not a solution. Unsupported isolation modes must remain silent.

## 2. Evidence and assumptions

Read-only inspection found an empty workspace without a Git repository, Windows version 10.0.26200.0, an RTX 5070 with driver 617.14, OBS 31.0.4, Node 24.21.0, and a dotnet executable. No .NET SDK was listed. The user reports a Ryzen 7 7700X, 32 GB DDR5, and approximately 250 Mbps upload. These facts do not establish encoder availability under load, sustainable internet throughput, or playback performance.

Assumptions: Windows 11 is the initial broadcasting target; SDR video is the initial color mode; rooms are small friend groups; hosting has no compulsory cloud dependency; recording is off. HDR, mobile background playback, unrestricted anonymous public broadcasting, and anti-cheat compatibility guarantees are outside the initial scope.

Public-address availability, CGNAT, router administration, external firewall behavior, and external viewer devices remain unmeasured deployment conditions. The software must explain and diagnose these conditions rather than assume them away.

No capture, audio-isolation, encoding, playback, or external-network acceptance test has been run.

## 3. Technology choices

| Responsibility | Choice | Boundary |
| --- | --- | --- |
| Desktop application | C#, .NET 10 LTS, WPF | Native source picker, media orchestration, controls, lifecycle, diagnostics |
| Desktop viewing | Microsoft WebView2 | Loads the same room viewer; no native capture commands exposed to arbitrary web content |
| Capture and main encoding | Owned, isolated OBS Studio helper; obs-websocket 5 protocol | Native Windows capture, application audio, NVIDIA NVENC, WHIP output |
| Media distribution | MediaMTX | One published path per stream/quality, fan-out to independent WHEP readers |
| Room control | ASP.NET Core and SignalR | Anonymous identity, room membership, ownership, state, authorization, diagnostics intake |
| Browser UI | React, TypeScript, Vite | WHEP lifecycle, video panels, playback controls, browser measurements |
| Shared preview worker, later | Supervised FFmpeg | One smaller rendition per stream, never one transcode per viewer |
| Public HTTPS | Caddy | Reverse proxy and certificate handling |
| Optional relay | coturn | TURN fallback when a reachable relay is necessary |

Use exact pinned releases, hashes for downloaded binaries, lockfiles, and a capability report. The installed OBS version is a discovery candidate, not an accepted production baseline. Implementation planning selects a maintained release compatible with the verified WHIP, capture, encoder, and control capabilities. Changes to those capabilities require rerunning relevant acceptance checks.

OBS is a native helper process, not an embedded browser encoder. Early development builds can use a separately downloaded distribution in an application-owned portable directory. Later packaging includes permitted binaries, notices, and corresponding source/license obligations. The application never edits the user's personal OBS profile, scenes, plugin configuration, or installation.

## 4. Data flow and responsibility

```text
Broadcaster: selected source -> OBS native capture -> NVENC H.264 + Opus
                                                 -> WHIP upload
Room host:    MediaMTX main stream -> independent WHEP readers
                                    -> optional shared preview worker
Preview:      one low rendition -> MediaMTX preview path -> WHEP readers
Control:      desktop/browser <-> room API and SignalR
```

The host runs the room server and media router. Each broadcaster uploads one main feed to the host. The router forwards compressed video without re-encoding the main feed. Increasing viewer count must not increase the broadcaster's encoder-session count. Encryption, packetization, retransmission, and separate network sends still incur per-connection costs.

Hosting on the broadcaster's PC means viewer fan-out uses that PC's internet upload. Hosting elsewhere moves fan-out to the other host; each broadcaster still needs enough upload to send its feed there. No broadcaster must also become a room host.

The room server allocates opaque stream paths. A path is owned by a session, not by its nickname. MediaMTX administration, metrics, internal RTSP, and OBS control stay private. Public readers and publishers receive room/path/action-scoped capabilities; public configuration must not allow arbitrary publish or read access.

## 5. Identity and room behavior

Joining a room link creates an anonymous server-issued session ID and a secret credential. The credential authorizes that session; possession of a displayed ID alone does not. Room links carry an unguessable invitation capability, not a universal publishing secret. Credentials must not appear in diagnostic exports or application logs.

Nicknames are editable display values. Renaming preserves membership, stream IDs, ownership, and subscriptions. Duplicate nicknames are permitted and visually distinguishable. Native and browser clients maintain their own stable sessions across reconnects using protected local credential storage or appropriate secure cookies.

Streams have explicit states: idle, preparing, publishing, reconnecting, stopping, stopped, and failed. Reconnection cannot transfer ownership to a different session. A server restart initially invalidates ephemeral sessions and asks clients to rejoin; persistent accounts are not introduced.

## 6. Capture and audio safety

### Video

Support monitor capture and selected-window capture through OBS's native Windows sources. Prefer Windows Graphics Capture where appropriate; use the engine's supported monitor capture fallback. Resolve and display the selected window and executable. Capability checks detect missing sources and unsupported capture before publishing. Minimized windows, exclusive fullscreen games, protected content, elevated applications, and anti-cheat restrictions receive specific failure reporting.

Do not automatically replace a lost window with another application. Source loss pauses or blanks video and mutes its associated audio until the user explicitly confirms a replacement. Preserve aspect ratio with letterboxing where necessary; advertised output dimensions refer to the encoded frame, not the original window's dimensions.

### Application audio

All global desktop/system audio sources are disabled. The only permitted digital audio inputs are explicitly approved application sources. Window capture and audio selection are distinct; selecting a monitor never implies system-audio capture.

Windows application loopback isolates a process or process tree, not arbitrary browser tabs. OBS exposes application audio sources and capture-audio options. Inspect the pinned source implementation and configuration semantics before relying on window matching. Enforce exact selection, avoid executable/title fallback to a replacement, and fail closed if the supported API cannot maintain the intended binding.

Discord, the broadcaster/viewing application's process tree, general browsers, and shared browser-host applications are ineligible audio sources in the first release. Unknown shared-process hosts are also ineligible until their isolation boundary has been verified. Capturing a browser window's video remains possible with audio disabled. Running Discord in a separate browser does not make unsafe browser audio capture eligible in the initial release.

For monitor sharing, offer explicit eligible application-audio selection or video without audio. Do not offer a system-output mode with a promise to subtract Discord later. If application audio is incompatible, unavailable, denied, lost, or ambiguously matched, keep it muted and explain why. Validate process identity during selection and source lifecycle changes; PID reuse must not attach to a different process.

The owned scene graph must be audited before Start and monitored while publishing. Unexpected audio sources, monitoring routes, or configuration changes stop/mute capture. A monitoring failure must fail closed. Nothing routes incoming room playback into the outgoing mix. Headless or hidden operation must not conceal which source is live.

### Microphone

A microphone is a separate opt-in input with its own mute control and level meter. It is off by default. Process isolation cannot remove Discord sound physically played through speakers and picked up by a microphone; communicate this limitation when enabling the microphone and recommend headphones. Digital audio-exclusion acceptance uses microphone-off tests, with separate microphone behavior checks.

## 7. Codec and streaming controls

Default video: H.264, 8-bit 4:2:0, browser-compatible negotiated profile, no B-frames, no encoder lookahead, low-latency settings, and a starting keyframe interval of approximately one second. Verify the negotiated profile against the pinned output and supported browsers instead of assuming a requested profile was honored. Default audio: Opus, 48 kHz, stereo where the source supports it, approximately 128 kbps.

Prefer NVIDIA NVENC on the RTX 5070. Evidence consists of actual initialized encoder identity and device in engine logs, negotiated video codec, and encoder activity under streaming load. A configured encoder name alone is insufficient. If initialization fails, remain stopped and offer a clearly labeled software H.264 fallback at reduced settings, initially 720p30. No silent CPU fallback is allowed.

AV1 is an extension point, not an initial compatibility dependency. Unsupported H.264 viewers receive an actionable compatibility error; no automatic per-viewer transcoder is spawned.

| Requested output | 30 FPS starting bitrate | 60 FPS starting bitrate |
| --- | --- | --- |
| 854x480 | 2 Mbps | 3 Mbps |
| 1280x720 | 4 Mbps | 6 Mbps |
| 1920x1080 | 8 Mbps | 15 Mbps |

Slider range: 1-25 Mbps in 0.5 Mbps increments. Mbps means megabits per second; internal conversion to engine kbps is explicit. These are starting presets, not measured quality promises. Audio, packet overhead, retransmissions, and previews add bandwidth.

Requested resolution/FPS/bitrate remain distinct from configured engine state and observed delivery. Settings are acknowledged only after engine readback. Initially resolution and FPS changes stop, reconfigure, and restart the feed while preserving stream ownership. Live bitrate changes are enabled only if supported and verified on the pinned encoder/output; otherwise they use the same explicit brief restart. The viewer reconnects to the live edge instead of retaining an old backlog.

## 8. Quality levels, bandwidth, and playback limits

The first milestone has one main rendition. It can diagnose a slow viewer but cannot make a fixed 15 Mbps feed fit a 3 Mbps connection. That limitation must be visible.

After main-feed validation, add one shared preview, initially 854x480 at 30 FPS and approximately 2 Mbps, per active broadcast. A host-side FFmpeg worker decodes the main feed once and encodes the preview once. This is an extra decode/encode workload per stream, not free scaling or transcoding built into MediaMTX. Use verified hardware acceleration on a suitable host; if unavailable, measure the reduced software workload or leave previews unavailable with an explicit capacity limit.

Focused playback uses the main feed when sustainable. Unfocused panels use previews. A viewer may select preview manually, and measured sustained loss/stalls can trigger a downgrade after a grace period. Upgrade decisions use hysteresis. Switching renditions may briefly establish a new WHEP connection; do not promise seamless switching. Preserve audio continuity where practical and prevent duplicate audio during overlapping connections.

If preview is still unsustainable, pause video or offer audio-only playback. Do not reduce every healthy viewer's main feed to accommodate one slow connection. Pause offscreen video subscriptions and close inactive connections; visually small main-feed elements still consume full decoding resources.

Budget host egress as the sum of subscribed rendition bitrates across streams and viewers, plus audio and measured overhead. Eight main subscriptions at 15 Mbps require about 120 Mbps; at 20 Mbps, about 160 Mbps, before overhead. Two such 15 Mbps streams with eight viewers each require about 240 Mbps before overhead. Remote broadcaster ingress is a separate quantity.

Initial configured upload budget: 175 Mbps, a conservative 70% of the user-reported 250 Mbps. This is a provisional admission setting, not a measured available capacity. Reserve 20% above estimated media bitrate inside that budget, then refine using observed overhead and congestion. Decline or downgrade subscriptions that exceed budget instead of admitting unlimited load.

Use bounded outgoing queues and independent reader lifecycles. Never block the shared publisher awaiting a particular viewer. Overflow, excessive live-edge delay, or sustained failures result in reader recovery, downgrade, or disconnection. Inspect and test the pinned media server's queue behavior rather than assuming defaults prove isolation. Under a stalled-viewer test, memory and delay must stabilize and healthy viewers must continue.

## 9. Diagnostics contract

Every metric carries its source, units, sample interval, stream/session association, and availability. Missing measurements are null with a reason, never zero or a copied requested setting. Reports must distinguish direct measurement, estimate, configured target, and unavailable data. Exports redact credentials and sensitive paths; recordings and packet dumps are opt-in.

| Stage | Required observations | Measurement boundary |
| --- | --- | --- |
| Capture | Source identity, capture API, source dimensions, acquired-frame rate and gaps | Engine/source instrumentation; OBS rendering FPS is not capture FPS |
| Rendering/encoding | Actual encoder/device, render time, render skips, encoded-frame rate, encoding delay/skips | OBS counters/logs plus native instrumentation where needed |
| Publishing | Codec/profile, encoded size, output-frame rate, sent bitrate, reconnects | Negotiated output, frame/byte deltas, media session statistics |
| Host | Per-path readers/traffic, aggregate egress, resource use, queue failures | MediaMTX metrics, room subscriptions, host network counters |
| Viewer | Received size/bitrate, decoded and presented FPS, drops, decode time, stalls | RTCPeerConnection.getStats and requestVideoFrameCallback where supported |
| Network | Packet loss, RTT, jitter, setup duration, candidate transport, relay status | Selected ICE pair and available WebRTC/server statistics |
| End to end | Glass-to-glass latency and audiovisual sync | Timestamp/flash-tone test with documented clock uncertainty |

Stock obs-websocket reports rendering FPS/time and render/output counters; it does not supply exact source acquisition FPS or per-frame NVENC encoding duration. Do not label averageFrameRenderTime as encoding time or infer successful hardware use from low CPU utilization.

Milestone 1 includes a diagnostic coverage report and native instrumentation/profiling for source acquisition and source-to-encoded-output timing. Distinguish pipeline residence time from GPU kernel execution time; asynchronous NVENC submission duration is not the latter. Instrumentation must not itself materially worsen playback. Unsupported exact GPU timing may remain explicitly unavailable, with measured pipeline delay and encoding skips provided instead.

Native diagnostic extensions use a local, versioned interface to the media adapter. OBS vendor requests/events are an available extension mechanism. If necessary data cannot be obtained without engine changes, make the bounded instrumentation change and review its maintenance cost before claiming diagnostic coverage. Until instrumented, affected acceptance checks remain pending.

Browser decoder hardware acceleration is not universally exposed; report it as unknown unless independently verified. Browser frame presentation callbacks measure presentation events, not guaranteed physical display scanout. Network RTT is not end-to-end video latency. Synchronizing viewers exactly is not an initial feature; each viewer plays near the live edge.

## 10. External connectivity and security

Initial localhost deployment requires no public ports. LAN and internet deployments are explicit profiles. Prefer native Windows executables initially, avoiding a mandatory Docker/WSL dependency. A later Linux-host deployment uses the same media and room protocols.

Public access uses HTTPS for the room, SignalR, WHIP/WHEP signaling, and capability exchange. Media uses WebRTC's separate encrypted connection. MediaMTX must advertise reachable public/LAN addresses. Forward the configured media port (initially UDP 8189) and configure Windows firewall rules; HTTPS alone does not forward media. Optional TCP media fallback is diagnosed distinctly and may behave worse under packet loss.

Under CGNAT, ordinary home-router forwarding may be insufficient. Options are an ISP-provided public address or moving the media host/relay to a reachable machine. A TURN server on the same unreachable home network does not create public reachability. A public TURN relay also has bandwidth costs and capacity limits. Test TURN separately, including restricted-network behavior.

Self-host all application services. No required paid streaming backend, account provider, analytics, or third-party media API. Optional external dependencies include public certificate issuance, DNS/dynamic DNS, ISP address service, public STUN, and rented reachable hosting. Document each configured dependency and cost category. STUN discovers addresses; it does not carry the video. TURN carries media and consumes relay bandwidth.

Public deployments use expiring room/path/action credentials, origin validation, sensible request/room limits, and TLS. Administrators choose room capacity. Invite-link possession grants entry to that room; no nickname authentication is implied. Keep internal admin services and OBS control off the public surface. Restrict any WebView2 native bridge to application-owned origins and a small typed command set. Remote web content cannot supply arbitrary executable paths, OBS requests, or process-launch arguments.

## 11. First implementation milestone: one real stream

The implementation plan following this specification covers only this milestone. Scaling, preview generation, multiple simultaneous broadcasters, and complete UI each receive their own subsequent focused implementation plan after the required previous validation.

Deliver a Windows app that chooses one monitor/window, selects an eligible application's audio or silence, publishes H.264/Opus through the owned OBS helper to a local/LAN or configured public MediaMTX host, and supplies a room link for one browser viewer. Include a minimal anonymous room/session model, editable nickname, ownership protection, Start/Stop, the stated quality controls, individual fullscreen, reconnect behavior, and readable/exportable diagnostics. Include a minimal in-app viewer to prove publishing and watching can coexist without feedback; a multi-stream grid comes later.

Provide PowerShell setup/start/stop scripts, dependency/capability checks, a configuration example with no live secrets, explicit error messages, and exact build/run instructions. Missing .NET SDK, media binaries, capture sources, NVENC, certificates, or external connectivity must produce actionable failures. Download and validate dependencies only after the implementation workflow is authorized; no installers have run during design.

The helper is a supervised process with an application-owned portable profile, private authenticated control, and a known scene graph. Detect a conflicting existing instance and do not attach to or terminate the user's personal OBS. Stopping publication releases network sessions and capture; application exit does not leave a stream live. Source selection is explicit and no sensitive capture starts merely on application launch.

### Automated correctness acceptance

Use xUnit for .NET logic and integration checks, a TypeScript test runner for browser logic, and browser automation for UI/network lifecycle where available. Tests must exercise meaningful safety and failure behavior, including:

- Audio defaults and prohibited-source rejection; ambiguous or lost source fails closed.
- Stable session ownership across renaming/reconnect; unauthorized sessions cannot replace a stream.
- Settings validation, Mbps conversion, applied/readback state, and restart semantics.
- Counter deltas, reset handling, metric availability, and credential redaction.
- WHIP/WHEP errors, timeouts, reconnection limits, cleanup, and helper lifecycle.
- Fullscreen return and prevention of duplicated incoming audio.

Mocks and synthetic media may verify these behaviors. They do not prove gameplay capture, audio exclusion, NVENC activation, or smooth playback. A real local integration stream verifies actual protocol negotiation and playback independently of unit tests.

### Real-media acceptance

Record source, game/scene, preset, versions, hardware, wired/Wi-Fi use, viewer device/browser, duration, and network topology. Use high-motion gameplay or a repeatable motion scene; a static desktop is insufficient. Run each baseline for at least ten minutes after startup and report time-series summaries, not only an average.

Initial targets for 1080p60 at 15 Mbps on a suitable uncongested path:

- Average presented FPS at least 57; at least 95% of five-second intervals at least 55 FPS.
- No playback freeze longer than 500 ms during the steady-state baseline.
- Render and encoding skipped-frame ratios each below 1%, reported separately.
- Measured end-to-end latency p95 at most 500 ms, with method and clock uncertainty disclosed.
- Audiovisual offset within 100 ms in the flash/tone test, checked at the start and end.
- NVENC initialization and activity proven under the test workload.

These are engineering acceptance targets, not guarantees for arbitrary internet paths. A missed target opens an investigation; it cannot be waived by passing mocked tests. Report conditions that make a result inconclusive.

Audio acceptance plays identifiable intended-application audio, Discord desktop audio, Discord browser audio, and incoming stream audio concurrently. Verify intended audio is present and prohibited sources are absent from the received signal using listening plus a calibrated tone/correlation check. Verify default microphone-off behavior. Repeat after source loss, window-title change, process restart, and display/window selection changes. Prohibited browser audio selections must be rejected or silent. Establish the test's detection floor and record it; an audio spectrum alone does not prove every possible exclusion scenario.

Run one real external viewer from another internet connection before declaring milestone 1 streaming validation complete. Distinguish local, LAN, external direct, and relayed evidence. An unavailable external participant/network leaves that acceptance pending; do not substitute multiple local tabs or contact friends without the user's authorization.

## 12. Later milestones and review gates

| Milestone | Addition | Required validation before moving on |
| --- | --- | --- |
| 2: Fan-out | One broadcaster, one/four/eight viewers; admission budget; slow-reader handling | Real decoding clients, external viewers, unchanged broadcaster encoder count, bounded memory/delay, healthy-reader isolation |
| 3: Concurrent streams | Multiple broadcasters; shared previews; subscription selection; grid/focus | At least two real publishers; broadcast-and-watch without recapture; host ingress/egress and encoder/decoder load; preview switching under constrained links |
| 4: Complete experience | Full controls, packaging, deployment guide, UI polish | Clean setup, restart/recovery, nickname continuity, fullscreen, supported browser matrix, repeat core audio/performance checks |

Use a fast-to-slow viewer test and a deliberately stalled connection in milestone 2. Compare the same source, preset, and workload with one, four, and eight viewers. Keep local load testing separate from external acceptance. Identify whether actual degradation occurs before changing the media architecture.

Each milestone receives a requirement-compliance review and code-quality review. Maintain separate findings for product behavior, safety, diagnostics, and performance. Resolve material findings and rerun affected checks before advancing. Small commits/task boundaries must make each change reviewable. Do not report the project complete while required hardware or external-network acceptance remains pending.

## 13. Debugging and extension boundaries

When a failure occurs, capture evidence first: source timing and source state; render and encoder timing/skips; outgoing bitrate; host queues/traffic; packet loss/RTT; viewer decode/presentation/stalls. Hold the workload constant, test one hypothesis at a time, and change architecture only when evidence warrants it. Record whether the bottleneck is capture, rendering, encoding, networking, or playback, including uncertainty.

Future features remain deferred: synchronized colored annotations, arbitrary panel resizing, cursor visibility control, and wheel zoom/pan/reset. Reserve stable stream IDs, versioned control messages, and normalized [0,1] video-content coordinates. Transform pointer coordinates through inverse pan/zoom and letterbox mapping; reject input outside content. Panel layout and fullscreen do not change stored annotation coordinates. Resolution/aspect-ratio changes create a content-geometry version so stale annotations can be handled deliberately.

Capture adapters, media-session adapters, diagnostics sources, rendition selection, and room state are separate interfaces. This permits targeted instrumentation, future codecs, or replacing a media component without rewriting identity and UI behavior. It does not authorize implementing deferred features during the first milestone.

## 14. Documentation basis

Official documentation inspected during the architecture discussion:

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [OBS application audio capture](https://obsproject.com/kb/application-audio-capture-guide)
- [OBS WHIP guide](https://obsproject.com/kb/whip-streaming-guide)
- [OBS websocket protocol and diagnostics](https://raw.githubusercontent.com/obsproject/obs-websocket/master/docs/generated/protocol.md)
- [Microsoft process-loopback audio sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)
- [MediaMTX OBS publishing](https://mediamtx.org/docs/publish/obs-studio)
- [MediaMTX architecture](https://mediamtx.org/docs/features/architecture)
- [MediaMTX codecs and connectivity](https://mediamtx.org/docs/features/webrtc-specific-features)
- [MediaMTX outgoing queue configuration](https://mediamtx.org/docs/references/configuration-file)
- [MediaMTX re-encoding with external tools](https://mediamtx.org/docs/features/remuxing-reencoding-compression)
- [WebView2 runtime distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)

Microsoft's process-loopback sample and OBS's user guide describe different minimum Windows-version boundaries. Target Windows 11 initially and verify the exact shipped implementation instead of inferring Windows 10 support from either page alone. Documentation demonstrates available mechanisms, not successful execution or performance on this hardware.
