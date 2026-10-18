# M3: AMD AMF and browser broadcasting

Date: 2026-10-18. Status: design direction approved in chat; written specification awaiting owner review. Implementation and live acceptance remain pending.

## Intent and baseline

Enable the current friend group to broadcast on AMD hardware and directly from the website while minimizing broadcaster CPU/GPU work and interference with games. Native AMD AMF H.264 is the first implementation slice; browser publishing follows it. Preserve NVIDIA NVENC, opt-in native CPU fallback, existing capture/audio safety, concurrent broadcasting, independent viewing and fullscreen behavior.

The governing scope is [AGENTS.md](../../../AGENTS.md), [the roadmap](../../ROADMAP.md) and [the M3 handoff](../../M3-HANDOFF.md). M2 is accepted on the owner's functional testing; historical results retain their recorded revision/package limits. Existing M3 theme, captured-cursor and compact-volume changes are retained.

Initial inspection found a clean checkout at `9c60e9069991e6974b2b03fa3b8df4e59fa2c11a` on `codex/m3-viewing-refinements`. [PR #10](https://github.com/vitor-tecc/PlayKross/pull/10) was open and unmerged when checked on 2026-10-18. Recheck before implementation or integration; reuse its work without merging it implicitly.

## Architecture and boundaries

Native publication remains selected WGC video plus optional selected audio -> owned OBS helper -> one H.264 video encoder and existing Opus encoder -> authenticated WHIP -> existing shared media distribution. Browser publication becomes user-selected display capture -> one browser WebRTC sender -> the same authenticated WHIP/distribution path. Neither route creates an encode per viewer.

Extend the pinned OBS implementation rather than integrating a second AMF SDK wrapper or changing the media server. These alternatives would expand maintenance and packaging without an established benefit. Retain the existing native engine boundary and add focused encoder configuration/selection logic. Keep the browser sender separate from the existing WHEP receiver; extract only small shared signaling helpers where necessary, rather than refactoring playback.

Out of scope: shared previews, local preview encodes, transcoding, simulcast renditions, Intel Quick Sync work, AV1/HEVC migration, microphone capture, viewer quotas, bandwidth admission, eight-viewer performance gates, permanent hosting and unrelated interface changes. Do not remove bundled components solely because their use is outside M3 scope.

## Slice 1: native AMD compatibility

### Selection and resource ownership

`ObsMediaEngine` discovers candidates from the owned helper and attempts `obs_nvenc_h264_tex`, then `h264_texture_amf`. Preserve this order to retain existing NVIDIA behavior. Missing capability means unavailable; a listed encoder still requires successful initialization. Mixed-adapter optimization and manual adapter/encoder selectors are not part of this initial slice.

Each attempt uses the current typed `PreparePipeline` request and must release its output, video/audio encoders and diagnostic callbacks before another attempt. Keep the same original capture selection and safety generation; never create a second concurrent encoding pipeline to probe availability. Do not change dimensions/rate between hardware candidates.

Only after both hardware choices are unavailable or fail may the existing explicitly enabled x264 fallback run. Preserve its maximum 720p30/4 Mbps behavior and lower requested settings. Update the consent text and failure messages to refer to hardware encoding rather than NVENC alone. With fallback disabled, stop and explain which hardware candidates were unavailable or failed, suggesting driver/hardware checks and the explicit CPU option. Never silently enable CPU encoding.

Cancellation, failed preparation, readiness timeout and Stop retain the existing owned-helper cleanup, playback-audio lease and capture-safety boundaries. A new start requires the normal fresh publication grant. Preserve disarm/heartbeat behavior, application process identity and pinned system endpoint handling.

### Pinned OBS findings and configuration

Inspect against OBS **32.2.2**, not current upstream defaults. The hash-pinned source under `.tools/obs-source/obs-studio-32.2.2-sources` contains the AMF implementation in `plugins/obs-ffmpeg/texture-amf.cpp` and `texture-amf-opts.hpp`. AMF is supplied by the existing `obs-ffmpeg` module; the AMD driver supplies the required AMF runtime. No new downloaded encoder SDK or personal OBS installation is required.

Use encoder-specific configuration. Retain current NVENC/x264 settings and add the following AMF policy:

| Setting | AMF request |
|---|---|
| Codec / profile | H.264 / High, matching the existing SDR video path |
| Rate control | CBR at the requested video bitrate |
| Keyframe interval | One second |
| B-frames | Zero |
| Repeated headers | Enabled, for readers joining after startup |
| Preset | `speed` |
| Pre-analysis | `pre_analysis=false` |
| Additional options | `usage=lowlatency preanalysis=false vbaq=false` |

The similarly named AMF controls have different meanings in this pin: `pre_analysis` selects AMF pre-analysis, while the parsed `preanalysis` option controls `PREENCODE_ENABLE`. OBS initially enables pre-encode and VBAQ, so disabling only a generic `lookahead` setting would not implement the intended policy. The option parser supports the above overrides before AMF initialization. Do not apply NVIDIA preset names or claim a generic OBS `lookahead` field measures AMD's analysis state.

Validate typed request ranges, codec, returned bitrate, zero B-frames, repeated-header configuration and the AMF-specific policy. Treat OBS settings readback as configuration evidence, not a readback of every driver property. Driver-level acceptance and resulting packets require live evidence. Preserve existing H.264 late-join checks and do not bypass a failed compatibility check to continue publishing.

### Diagnostics and failures

Successful `obs_output_initialize_encoders` establishes that the selected video/audio encoders initialized. Report AMD hardware initialization only after that success, not from capability enumeration or encoder-object creation. Distinguish encoder preparation from active publication and actual sent frames/bytes. Retain the existing active-video/audio-encoder counters.

OBS can internally reroute `h264_texture_amf` to `h264_fallback_amf` when its texture path cannot initialize. This is still AMF hardware encoding, with a different frame-transfer path; it is not x264 CPU encoding and does not consume the CPU-encoding consent. It may incur CPU copies and extra GPU traffic.

In this OBS version, `obs_encoder_get_id` and `obs_encoder_get_caps` return original registration information after rerouting. Therefore report the initialized AMF family and original OBS registration ID, with the transfer route explicitly **unverified** unless separately observed. Never infer texture-only operation, zero copies or low game impact from that ID/capability. Do not patch OBS internals merely to make this initial diagnostic stronger. Include the rerouting limitation in compatibility and live-test notes.

Extend the diagnostic export allowlist for the supported AMD registration/family metadata without admitting arbitrary driver messages, filesystem paths, tokens or invitations. User-facing failures distinguish missing module/capability, initialization failure and incompatible readback. Include bounded component errors where safe; raw helper logs remain local and ignored. Do not label every initialization failure as a proven driver defect.

### Packaging

Retain the pinned complete OBS runtime, allowed modules and existing license/source notices. Verify that the portable path includes the module and required bundled dependencies; encoder enumeration alone is insufficient for package acceptance. Document supported driver/runtime requirements using the actual test system rather than inventing a universal minimum driver version.

`Start-Broadcaster.ps1` rebuilds managed code only. Native implementation changes require a matching plugin rebuild/install after stopping the owned broadcaster, or a fresh portable package. Code-only verification must not install the plugin, stop an active session or change personal OBS files. A generated package is not committed to Git and is distributed only through the release/package workflow when requested.

## Slice 2: broadcasting from the website

### Controls and capture

Add Start sharing, Stop sharing, an initially unchecked Share captured audio option and publication status to the joined room. Explain that video/audio choices depend on the browser and source, and that native per-application audio isolation is unavailable here. Browser encoding and hardware acceleration are browser-managed; this route does not expose AMF/NVENC controls or promise a hardware encoder.

Feature-detect secure context and `navigator.mediaDevices.getDisplayMedia`. An unsupported context/browser gets an actionable explanation. Call `getDisplayMedia` directly from the Start gesture, before awaiting network work; never invoke it on page load, session restoration or automatic reconnect. Let the browser present its source chooser. Every new start requires a new gesture and chooser. Do not call `getUserMedia` or request microphone permissions.

Use one video track and, only when requested and returned by display capture, one audio track. Stop unexpected extra tracks. If audio was requested but absent, disclose video-only operation without a microphone or native-audio substitute. If an audio track ends, update status and continue video-only; ended video stops the publication. Permission denial/cancellation must leave no active tracks or reservation and allow another user-initiated start.

The initial browser video target is at most 1280x720 at 30 fps and 4 Mbps, with captured aspect ratio retained. Apply supported post-selection capture constraints and sender bitrate limits; do not promise exact dimensions/frame rate or forbid the user's source choice. Use a single sender encoding without canvas copies, local rendered previews or additional renditions. Browser-selected scaling/encoding still requires real performance validation.

### Publication controller and ownership

Keep room UI, capture/publication lifecycle and WHIP transport as focused units. A publication controller owns the capture tracks, start generation, reservation, lease renewal, sender and cleanup. Its states are idle, choosing, starting, live and stopping; errors return to an actionable idle/error presentation after cleanup. Disable duplicate Start attempts. Stop invalidates pending asynchronous operations immediately. A late capture result is stopped, and a late reservation/WHIP result is cleaned up rather than becoming live.

Reuse the authenticated `POST /api/streams`, lease and DELETE endpoints and the existing media proxy. The current reservation method is idempotent for a session and returns an existing publication. A new browser start must not adopt that existing publication and later delete it on failure. Add an optional create-only mode, `POST /api/streams?requireNew=true`, that atomically returns HTTP 409 when the session already owns a publication. The default endpoint/store behavior remains unchanged for native clients. Conflict cleanup stops only this attempt's capture; it does not revoke the existing stream.

This create-only check also protects simultaneous tabs sharing the same session cookie, including starts that race room snapshots. Only the controller holding a successful new reservation may renew or stop that stream. Other tabs display the existing publication without a Stop control that impersonates its controller. Page departure in a viewing tab must not implicitly leave the shared session or revoke another tab's broadcast. Do not introduce a room-wide publisher/viewer limit.

Renew the publication every 15 seconds against the existing 60-second lease. Stop on explicit lease rejection, session expiry, revocation or publication removal. Transient renewal failures may retry within the known lease lifetime, but must stop capture before it expires if renewal cannot be confirmed. Do not create replacement reservations or silently restart capture in response to connection failure.

### WHIP sender

Create a separate `WhipSession` patterned on the bounded signaling/cleanup behavior of `WhepSession`. Reuse validated same-origin endpoint/resource checks, authenticated OPTIONS/POST and the existing ICE-server Link parsing where useful. Attach captured tracks as send-only transceivers; negotiate H.264 and optional Opus for existing browser/WebView compatibility. Reject unsupported codec negotiation clearly rather than silently publishing a format current receivers cannot use.

Use a single bounded ICE-gathering and connection attempt with cancellation. Post SDP to the granted WHIP endpoint, require a successful created-resource response and validate its same-origin `/media-resources/{id}` Location before using it. Do not forward credentials to arbitrary URLs. Keep the resource as soon as the response is known so invalid SDP, late cancellation and remote-description failures can DELETE it. No automatic reconnection with stale grants.

Display live only once the peer connects; connection alone is not proof that a remote viewer received media. Failures close the peer, stop all owned capture tracks, cancel timers/handlers, delete the known WHIP resource and stop the owned reservation. Cleanup must be idempotent and attempt each release even if another release fails. Bound cleanup network requests independently of an already aborted start.

Stop sharing, browser video-track ending, leave, component disposal, page departure and connection failure use the same cleanup. On page departure use best-effort authenticated keepalive cleanup for known owned resources. Abrupt process/network loss or a reservation response lost before its ID is known cannot guarantee immediate client deletion; existing server lease/media expiry remains the bounded fallback. Never guess an ID from a later room snapshot and revoke a publication to compensate for an unknown result.

### Watching and audio feedback

While requested captured audio is being selected/started, block playback audio from all room players in the publishing page before opening capture. Keep that block while captured audio is active; show why playback is muted and retain each card's chosen volume for restoration. Release only this browser controller's block on cleanup or confirmed video-only operation. Compose it with the native host playback-audio block so ending browser capture cannot clear a native mute lease.

Retain watching other streams and fullscreen behavior. Represent this controller's own publication as a sharing-status tile instead of opening its WHEP receiver, avoiding unnecessary self-decode and self-audio. Do not add a local preview pipeline. Other tabs/windows/apps can still produce audio included in system capture; disclose this limitation. Browser audio suppression hints, where supported, do not establish strict exclusion.

## Verification and acceptance

Automated implementation validation uses `./scripts/Verify.ps1 -Mode CodeOnly`. It compiles the native plugin without installing it or starting capture/media. Keep all raw outputs under ignored `.artifacts/`; commit only concise revision-scoped results. No new runtime result is claimed by this specification.

| Area | Required noninteractive evidence |
|---|---|
| Native selection | NVENC preservation; AMD-only success; NVENC failure followed by AMD; missing/failed hardware; CPU disabled/enabled; failed-attempt cleanup; cancellation/restart |
| Native policy | AMF-specific configuration/readback; incompatible configuration rejection; hardware-initialized truthfulness; preserved repeated headers/audio/safety; one pipeline |
| Diagnostics/package | Supported AMD metadata survives redaction; unsupported/private data stays excluded; module/dependency/package boundaries remain correct |
| Browser capture | Gesture-driven API use; opt-in audio; missing/ended tracks; permission denial; no microphone API; unsupported/insecure context; capture resolving after Stop |
| Browser publication | Strict reservation conflict, simultaneous same-session starts, ownership checks, renewal/revocation, codec/HTTP/ICE errors, canceled/late responses, resource and reservation cleanup, restart |
| Browser interface | Start/Stop/status/accessibility; responsive room/fullscreen regressions; feedback mute/restoration with native block; own tile avoids a WHEP receiver; concurrent publishers remain independent |

Mocks and source/configuration tests establish orchestration, not working hardware encoding, audio delivery or game performance. Live acceptance is separate:

1. Ask which AMD GPU(s), driver and Windows version are available before hardware testing. Record exact revision, package/plugin hashes, adapter arrangement and settings. Use a matching fresh package or safely installed plugin; preserve unrelated active sessions.
2. On AMD, validate actual H.264 video/available selected audio in browser and WebView, late joins, start/stop/restart, hardware failure with CPU consent both off/on, and concurrent publication. Preserve NVENC with a separate real NVIDIA regression check when hardware is available. Record untested adapter combinations honestly.
3. Compare the same AMD game/workload without broadcasting and with one publication, recording CPU/GPU usage, encoder/render lag, FPS/frame-time observations and whether this PC also hosts the room. Distinguish startup stutter from sustained behavior. Do not invent a universal percentage or zero-impact guarantee.
4. Validate browser publishing first on available desktop Chrome/Edge systems, recording browser/OS versions and actual screen/window/tab/audio support. Check denied/canceled chooser, browser Stop sharing, restart, page departure, duplicate tabs, captured-audio feedback, and browser/native coexistence with a small two-publication scenario. Mark other browsers unsupported or unvalidated according to actual evidence.
5. Record browser CPU/GPU/game-impact observations separately from native AMF results. No viewer-count campaign or bandwidth-admission gate is required. AMD hardware acceptance and browser acceptance remain pending until their respective evidence exists.

## Delivery and review stages

This specification is the artifact for the next owner review. After written-spec approval, produce the implementation plan with AMD tasks first and browser tasks second; the owner then reviews that plan and chooses execution method. If agents are later selected, every agent/reviewer must explicitly use `gpt-6.1-sol` with a chosen reasoning effort, including nested agents. Never substitute Astra.

For each implemented slice, verify, update handoff/compatibility/validation notes, commit and push on a `codex/` branch, and open/update the appropriate PR. Recheck PR #10 and the remote base first. Do not merge or rewrite published history without instruction. Keep generated binaries, credentials, invitations, caches, runtime state and raw artifacts out of Git. Implementation completion and live hardware/browser acceptance must be reported separately.

## Technical references

- Pinned local OBS source: `plugins/obs-ffmpeg/texture-amf.cpp`, `texture-amf-opts.hpp`, `plugins/obs-webrtc/whip-service.cpp` and `libobs/obs-encoder.c` under the hash-verified OBS 32.2.2 source tree. In particular, check `amf_avc_create_texencode`, `amf_avc_init`, `amf_apply_opt`, `obs_encoder_create_rerouted`, `obs_encoder_get_id` and `obs_encoder_get_caps`.
- Existing native boundaries: [ObsMediaEngine](../../../src/JKiller.Windows/Obs/ObsMediaEngine.cs), [ObsPipeline](../../../native/jk-obs/src/ObsPipeline.cpp) and [DiagnosticsExport](../../../src/JKiller.Core/Diagnostics/DiagnosticsExport.cs).
- Existing web/host boundaries: [RoomPage](../../../web/src/room/RoomPage.tsx), [WhepSession](../../../web/src/media/WhepSession.ts), [RoomEndpoints](../../../src/JKiller.Host/Rooms/RoomEndpoints.cs), [RoomStore](../../../src/JKiller.Host/Rooms/RoomStore.cs) and [MediaProxy](../../../src/JKiller.Host/Media/MediaProxy.cs).
- [W3C Screen Capture working draft](https://www.w3.org/TR/2026/WD-screen-capture-20260827/) for user activation, source choice, constraints and optional capture audio; browser implementation/support still needs testing.
- [RFC 9725: WHIP](https://www.rfc-editor.org/rfc/rfc9725.html) for HTTP session establishment and resource cleanup; implementation must also respect the pinned media server and existing proxy behavior.
