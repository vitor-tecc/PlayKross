# M3 Browser Broadcasting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let joined website users start and stop a shared browser-captured H.264 broadcast with optional captured audio and authenticated ownership/cleanup.

**Architecture:** Add create-only reservations to the existing host endpoint, a WHIP sender, an owned publication controller and room controls. Keep capture lifecycle separate from transport and existing WHEP playback. Implement after [native AMD](2026-10-18-m3-amd-broadcasting.md) passes code-only verification; outstanding live AMD acceptance remains recorded separately.

**Tech Stack:** React 19/TypeScript, getDisplayMedia, WebRTC/WHIP, Vitest/jsdom, ASP.NET Core/.NET 10, xUnit, PowerShell.

**Spec:** [Approved M3 design](../specs/2026-10-18-milestone-3-design.md).

## Global Constraints

- User gesture and secure context are required; call `getDisplayMedia` before awaiting network work. No automatic capture/restart or `getUserMedia`/microphone capture.
- One video track, at most one opt-in returned capture-audio track; disclose video-only operation when audio is unavailable.
- Initial target: at most 1280x720, 30 fps and 4 Mbps; retain aspect ratio. Single sender encoding; no canvas copy, preview encode, simulcast, transcoding or per-viewer encoding.
- Browser hardware/encoder choice is browser-managed; native AMF/NVENC settings do not control it.
- Preserve one publication per authenticated session, concurrent independent broadcasters and existing native/audio/view/fullscreen behavior. No quotas, bandwidth admission, Intel work or eight-viewer gate.
- Renewal every 15 seconds; existing lease is 60 seconds. Stop before confirmed ownership expires; never adopt an existing publication.
- Preserve active sessions and personal OBS. Use `./scripts/Verify.ps1 -Mode CodeOnly`; real browser/audio/game-impact tests stay separate.
- Use `codex/`, commit/push/PR, never merge without instruction; exclude credentials, invitations, runtime state, generated binaries and raw artifacts.
- All selected implementer/review agents, including nested agents: explicit `gpt-6.1-sol` and chosen reasoning effort; no Astra.

## Review Focus

1. Two tabs with the same cookie start simultaneously: exactly one owns a new reservation; losing cleanup cannot stop the winner (B1/B3).
2. Stop/restart races a late chooser, reservation, WHIP response or old failure callback: dispose the old attempt without touching the new one (B2/B3).
3. Native playback mute already exists: browser stop or missing captured audio cannot clear it or erase per-card sound/volume preferences (B4).
4. Renewal hangs or browser timers are throttled: ownership expiry still stops capture when execution resumes; no stale callback extends another start's deadline (B3).
5. A room snapshot arrives before reservation response or out of order: do not stop a new publication based on stale absence; do stop after authoritative removal/lease rejection (B3/B4).

## File responsibilities and commands

Keep `RoomStore`/`RoomEndpoints` as the authenticated reservation boundary and `MediaProxy` unchanged unless a regression test proves a necessary fix. New `WhipSession.ts` owns one peer and its HTTP resource; `BrowserPublisher.ts` owns capture, reservation, timers and cancellation. New `BroadcastControls.tsx` supplies controls/status; `RoomPage` retains room membership and stream layout. Use existing dependencies and test conventions; no new package is planned.

Run all commands from repository root. Managed tests use repository `.tools/dotnet`, `.tools/nuget` and `.tools/dotnet-home` configuration. Browser commands use the existing npm installation. Native execution/worktree setup is covered in the AMD plan; recheck Git/PR state when moving to this slice. No implementation until the plans are reviewed and an execution method selected.

## Task B1: create-only authenticated reservation and browser API

**Files:** Modify `src/JKiller.Host/Rooms/RoomStore.cs`, `src/JKiller.Host/Rooms/RoomEndpoints.cs`, `tests/JKiller.Host.Tests/RoomStoreTests.cs`, `tests/JKiller.Host.Tests/RoomApiTests.cs`, `web/src/api/contracts.ts` and `web/src/api/roomApi.ts`. Create `web/tests/roomApi.test.ts`.

**Interfaces:** Extend `PublishReservation ReservePublish(Guid sessionId, bool requireNew = false)`. Bind optional `bool? requireNew` on `POST /api/streams` and pass `requireNew == true`; missing/false keeps native idempotency. Existing `InvalidOperationException` -> HTTP 409 mapping is retained. TypeScript adds `PublishReservation { streamId:string; path:string; whipUri:string; token:string; expiresAt:string }` and these `roomApi` members:

```ts
reservePublication(signal: AbortSignal): Promise<PublishReservation>; // POST ?requireNew=true
renewPublication(id: string, signal: AbortSignal): Promise<RoomSnapshot>;
stopPublication(id: string, signal: AbortSignal, keepalive?: boolean): Promise<RoomSnapshot>;
```

- [ ] **Step 1: Add failing store/API tests** `CreateOnlyCannotAdoptExistingPublication`, `ConcurrentCreateOnlyHasExactlyOneWinner` and `DefaultReservationRemainsIdempotent`. Inside the current room-store lock, conflict must happen before lease renewal/grant issue/revision mutation. Test same-session HTTP races plus different owners; status assertions:

```csharp
Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
Assert.Equal(original.StreamId, Assert.Single(store.Snapshot(owner.SessionId).Streams).StreamId);
```

Use the existing fake clock to prove a rejected create-only request does not renew the original lease. Keep 401 for missing identity, 403 for other-owner stop/renew and no new participant/publisher limit. Browser API tests assert same-origin credentials, URL-encoded IDs, exact methods/query and keepalive DELETE; preserve existing generic 409 wording outside the publishing controller.
- [ ] **Step 2: Run** `./.tools/dotnet/dotnet.exe test tests/JKiller.Host.Tests --filter "FullyQualifiedName~RoomStoreTests|FullyQualifiedName~RoomApiTests" --nologo` and `npm --prefix web test -- --run tests/roomApi.test.ts`. Expected RED on create-only conflict or missing browser methods.
- [ ] **Step 3: Implement** the exact optional mode and API members above. Return the existing reservation shape on success; never return an existing grant from create-only. Keep the existing default behavior and authentication/origin middleware. No changes to membership/capacity policy.
- [ ] **Step 4: Repeat Step 2.** Expected GREEN, including legacy idempotency/concurrency and authentication cases.
- [ ] **Step 5: Commit** listed files: `feat(m3): reserve browser publications without adopting existing streams`.

## Task B2: owned WHIP sender

**Files:** Create `web/src/media/WhipSession.ts` and `web/tests/WhipSession.test.ts`. Import the existing pure `parseIceServers` export from `web/src/media/WhepSession.ts`; do not restructure the receiver.

**Interfaces:** Export `PublicationTransport { start(grant:PublishReservation, stream:MediaStream, signal:AbortSignal):Promise<void>; close():Promise<void> }`. `WhipSession` implements it and takes `(deps?: WhipDependencies, onFailure?: (error:Error)=>void)`. `WhipDependencies` contains `origin:string`, `fetch:typeof fetch`, `createPeer:(servers:RTCIceServer[])=>RTCPeerConnection`, `supportsCodecs:(audio:boolean)=>boolean`, `timeoutMs:number` (default 15000). The controller owns capture tracks; closing transport closes its peer/resource, not unrelated tracks or reservations.

- [ ] **Step 1: Add failing fake-peer/HTTP tests** modeled on `WhepSession.test.ts`. `UsesOneSendOnlyEncodingAndCompatibleCodecs` asserts one video and optional audio send-only transceiver, H.264/Opus preferences, no recvonly/simulcast encodings, and video sender `maxBitrate=4_000_000`. `RejectsForeignOrMalformedEndpointsAndResources` asserts zero credential-bearing requests to foreign URLs. `LateCreatedResourceAfterStopIsDeleted` and `InvalidAnswerReleasesKnownResource` assert:

```ts
expect(peer.close).toHaveBeenCalledTimes(1);
expect(requests.filter(r => r.method === "DELETE").map(r => r.url))
  .toEqual(["https://room.test/media-resources/12345678-1234-1234-1234-123456789abc"]);
expect(unrelatedTrack.stop).not.toHaveBeenCalled();
```

Also test canceled start, expired grant, missing video, unsupported H.264/Opus, video-only success without Opus capability, failed OPTIONS/POST, missing/malformed Location, non-SDP/bad answer, no ICE candidate, connection timeout, post-live failure/disconnect and repeated close. Preserve a valid late response's resource even if its fetch ignored abort. Match sender negotiated codecs to the offered H.264/optional Opus media sections; reject a rejected/unsupported answer rather than trusting capability enumeration.
- [ ] **Step 2: Run** `npm --prefix web test -- --run tests/WhipSession.test.ts tests/WhepSession.test.ts`. Expected RED for missing sender/behavior; existing receiver cases stay GREEN.
- [ ] **Step 3: Implement** `start`/`close` with same-origin generated endpoint checks, bearer OPTIONS/POST, ICE Link handling, one peer, compatible send-only transceivers, bounded waits and stored resource Location before reading/setting the answer. Require HTTP 201 and SDP. Use independent 5000-ms keepalive DELETE cleanup, no stale-grant reconnect and at most 2000-ms transient disconnection grace, as in current receiver behavior. Close failed peers/resources even if another cleanup fails; use bounded generic errors, never raw SDP/tokens/server bodies.
- [ ] **Step 4: Repeat Step 2 and run** `npm --prefix web run build`. Expected GREEN; typecheck/production build succeeds with existing dependencies.
- [ ] **Step 5: Commit** the sender/tests: `feat(m3): publish captured tracks through authenticated WHIP`.

## Task B3: capture/publication controller with cancellation and lease safety

**Files:** Create `web/src/media/BrowserPublisher.ts` and `web/tests/BrowserPublisher.test.ts`.

**Interfaces:** Export `PublisherPhase = "idle"|"choosing"|"starting"|"live"|"stopping"` and `PublisherSnapshot { phase:PublisherPhase; streamId:string|null; hasAudio:boolean; error:string|null }`. Export `PublicationApi = Pick<typeof roomApi, "reservePublication"|"renewPublication"|"stopPublication">`. `BrowserPublisher` takes `(deps:BrowserPublisherDependencies, changed:(value:PublisherSnapshot)=>void)`, exposes `start(shareAudio:boolean):Promise<void>`, `stop():Promise<void>` and `reconcile(room:RoomSnapshot):void`. Dependencies: `secureContext:boolean`, `capture:((options:DisplayMediaStreamOptions)=>Promise<MediaStream>)|undefined`, `api:PublicationApi`, `createTransport:(onFailure:(error:Error)=>void)=>PublicationTransport`, `setCaptureAudioBlocked:(blocked:boolean)=>void`, `now:()=>number` (monotonic milliseconds) and `sessionExpiresAt:string`. Use normal timers with Vitest fake timers; no additional timer library.

`start` reports handled capture/network failures through the snapshot after cleanup and resolves; intentional cancellation resolves without a failure message. Duplicate starts while busy are ignored. `stop` stops known local tracks immediately, then resolves after bounded best-effort owned server cleanup, even if the network is unavailable. Transport `start` may reject; the controller catches it. These semantics allow event handlers and page departure to invoke cleanup without unhandled rejections.

- [ ] **Step 1: Add failing capture/ownership tests.** `CaptureStartsSynchronouslyFromStart` checks ordering before awaiting the returned promise; no reservation precedes chooser success. `AudioIsOptInAndUnavailableAudioIsDisclosed` checks `audio:false` by default, no microphone call, excess tracks stopped and one video/at most one audio sent. `StopClosesLateCaptureAndReservation` asserts late tracks stop and only the known owned reservation is deleted. `ConflictNeverStopsExistingStream` checks no guessed stop ID after HTTP 409.

```ts
const starting = publisher.start(true);
expect(events.slice(0, 2)).toEqual(["mute-room", "display-chooser"]);
expect(api.reservePublication).not.toHaveBeenCalled();
await publisher.stop();
resolveCapture(oldStream);
await starting;
expect(oldVideo.stop).toHaveBeenCalledTimes(1);
expect(api.stopPublication).not.toHaveBeenCalled();
```

Add `StaleCallbacksCannotStopRestartedPublication`, `AudioEndingContinuesVideoOnly`, `VideoEndingStopsPublication`, `LeaseFailureStopsBeforeExpiry`, `SnapshotBeforeReservationDoesNotCancelStart` and `OutOfOrderSnapshotsCannotRevokeNewPublication`. At 15 seconds assert renewal; at a 50000-ms conservative deadline from the last successful renewal request's send time, stop on unconfirmed renewal (10-second margin before server's 60 seconds). A timer jump past the deadline stops before issuing more work; explicit 401/403/removal/session expiry stops promptly. Test cleanup failure still stops tracks, a stopped controller may start fresh, and page departure cleanup never calls `roomApi.leave`.
- [ ] **Step 2: Run** `npm --prefix web test -- --run tests/BrowserPublisher.test.ts`. Expected RED for missing controller. Use deferred promises and fake monotonic time; no real chooser/media/server.
- [ ] **Step 3: Implement** the exact controller interfaces. Gate insecure/missing capture support; synchronously request the mute block for audio and call display capture before the first await. After selection, constrain supported video width/height/frameRate maxima to 1280/720/30 without stretching; send one video and optional audio track. Surface video-only audio absence, stop extras and reject missing/ended video. Capture failure never reserves a publication.
- [ ] **Step 4: Implement** attempt generation and owned cleanup. Serialize successful owned resources, immediately invalidate pending work on Stop, and bind handlers/transport callbacks/timers to that generation. On stale results stop only old resources; every restart uses a fresh chooser/reservation/transport. Renew every 15000 ms with independent 5000-ms request bounds, 50000-ms ownership cutoff and wall-clock session expiry. Schedule the ownership cutoff separately from the next renewal so a hung renewal cannot prevent cleanup. Avoid treating a legacy pre-reservation room snapshot as revocation: track room revision and whether that owned ID has been observed; newer absence after observation stops it, otherwise renewal rejection/expiry remains the fallback. Repeated Stop is harmless and never guesses a reservation ID or touches another publisher. Complete browser suspension can prevent JavaScript track-stop timers from running: server revocation/expiry remains the publication boundary, and local tracks stop as soon as execution resumes; disclose this limit in validation notes rather than promising a hard real-time browser deadline.
- [ ] **Step 5: Repeat Step 2 and B2 sender tests.** Expected GREEN for all start/stop/error/timer permutations; no pending timers/listeners after completion. Return actionable fixed errors without capture-source names or grants in diagnostics.
- [ ] **Step 6: Commit** the controller/tests: `feat(m3): own browser capture and publication lifecycle`.

## Task B4: room controls, feedback mute and self-publication tile

**Files:** Create `web/src/room/BroadcastControls.tsx` and `web/tests/BroadcastControls.test.tsx`. Modify `web/src/room/RoomPage.tsx`, `web/src/room/StreamPlayer.tsx`, `web/src/style.css`, `web/tests/RoomPage.test.tsx` and `web/tests/StreamPlayer.test.tsx`. Keep `web/src/media/hostPlaybackAudio.ts` and its existing native event contract unless tests demonstrate a necessary compatibility fix.

**Interfaces:** `BroadcastControlsProps { state:PublisherSnapshot; unavailableReason:string|null; onStart:(shareAudio:boolean)=>void; onStop:()=>void }`. It owns only the checkbox choice; the publication controller owns lifecycle. `RoomPage` owns one controller per restored authenticated session, applies `reconcile` on newer room snapshots and disposes it on session change/unmount/pagehide. Add optional `audioBlockedReason?:string` to `StreamPlayerProps`; the existing boolean and sound/volume state semantics remain compatible. Define room capture mute separately from native host mute and combine them with OR.

- [ ] **Step 1: Add failing UI tests** `NoCaptureBeforeStartGesture`, `CapturedAudioDefaultsOff`, `StopWorksDuringChooser`, `NativeMuteSurvivesBrowserCleanup`, `MutedPlayersPreserveTheirPreferences`, `OwnPublicationDoesNotRequestPlayback`, `ViewingTabDepartureDoesNotStopForeignController` and `LeavingStopsOwnedCaptureBeforeMembershipRelease`. Simulate chooser cancellation and unavailable audio; assert status text, accessible Start/Stop states and no microphone request. Keep mounted cards/players stable when publications change. Assertions include:

```ts
fireEvent.click(screen.getByRole("button", { name: "Start sharing" }));
expect(capture).toHaveBeenCalledTimes(1);
expect(video.muted).toBe(true); // also assert true inside capture's mock
fireEvent.click(screen.getByRole("button", { name: "Stop sharing" }));
await waitFor(() => expect(ownedVideo.stop).toHaveBeenCalled());
expect(video.muted).toBe(true); // pre-existing native host block remains
expect(video.volume).toBe(.35);
expect(api.playback).not.toHaveBeenCalledWith(ownedStreamId, expect.anything());
```

- [ ] **Step 2: Run** `npm --prefix web test -- --run tests/BroadcastControls.test.tsx tests/RoomPage.test.tsx tests/StreamPlayer.test.tsx tests/hostPlaybackAudio.test.ts`. Expected RED on missing controls/controller integration; preserve existing independent volume/focus/fullscreen behavior.
- [ ] **Step 3: Implement** controls/status and instantiate the controller using B1/B2. In the Start event's mute callback use React DOM `flushSync` when setting capture mute true, so mounted room videos are muted before `getDisplayMedia` opens; ordinary state updates may release it. Newly mounted cards also receive the composed block. Preserve each card's sound and volume choices. Explain captured-audio feedback and browser-dependent availability; native host mute takes precedence in the displayed reason. No rendered local preview.
- [ ] **Step 4: Integrate** the owned sharing-status tile and lifecycle cleanup. Replace only this controller's known stream card, not all publications sharing the session owner ID. Other streams remain watchable/focusable. Stop precedes explicit Leave; pagehide invokes best-effort owned cleanup without implicitly leaving a shared session. Session changes never carry an old capture into another room. Update the empty-room text to mention website sharing alongside Windows broadcasting and keep toolbar responsive/keyboard accessible.
- [ ] **Step 5: Repeat Step 2 and run** `npm --prefix web run build`. Expected GREEN. Inspect mocked browser layouts at desktop, 390/320-pixel widths and short fullscreen; record layout evidence as mocked, not real-media delivery. Avoid showing invitations in screenshots/raw output.
- [ ] **Step 6: Commit** listed UI/tests: `feat(m3): add browser sharing controls and feedback protection`.

## Task B5: integrated verification and browser compatibility handoff

**Files:** Modify `docs/setup/publishing-and-compatibility.md`, `docs/M3-HANDOFF.md`, `docs/ROADMAP.md` and relevant existing setup instructions only where browser publishing changes their user-facing claims. Add `docs/validation/2026-10-18-m3-browser.md` (actual execution date if later). Product/test repairs, if needed, stay confined to B1-B4 boundaries and carry their focused regression tests.

**Interfaces:** No new protocol/production API. Reuse the existing `CodeOnly` verification entry point and record revision/package/browser limits alongside each result. AMD implementation and live acceptance retain independent status.

- [ ] **Step 1: Run** `./scripts/Verify.ps1 -Mode CodeOnly`. Expected exit 0 for managed/browser/native/setup checks and production builds; no native plugin install or browser/capture/media startup. Retain the ignored log under `.artifacts/`. Run `git diff --check` and validate local documentation links. Review B1-B4 as one publication lifecycle under the selected execution/review method; any agents use explicit `gpt-6.1-sol`/reasoning settings.
- [ ] **Step 2: Record** actual automated counts/results, layout evidence and any failures with revision limits. Explain browser-controlled encoding, no native AMF control, secure context, optional source-dependent audio, no microphone, playback feedback protection and its same-page limitation. Never mark mocked sender tests as browser/audio acceptance or claim hardware acceleration from browser capabilities.
- [ ] **Step 3: Prepare the live checklist** without running unattended capture: available Chrome/Edge versions and Windows/OS; actual chooser screen/window/tab video and audio; denied chooser; browser Stop sharing; restart; page departure; same-cookie tabs; watching with audio; native/browser coexistence and a small two-publication test. Include receiving video/audio in browser/WebView and observed CPU/GPU/game impact. Use consented live sessions and record unknown/unsupported cases honestly; no viewer-capacity campaign. Track any unperformed cases as pending rather than blocking verified code delivery.
- [ ] **Step 4: Commit and push** the final documentation and verified repairs: `docs(m3): record browser publishing verification and limitations`. Recheck remote/PR state, update/attach the appropriate PR, and report native/browser implementation results separately from pending live acceptance. Do not merge, start services or distribute binaries without the relevant request.

## Owner review / execution choice

Review this plan with the linked AMD plan before choosing native or subagent-driven execution. The recommendation in the AMD plan applies to both. Native execution keeps implementation in this session with final independent review; subagent-driven execution reviews each task. Every selected agent/reviewer must explicitly use GPT-6.1 Sol and a chosen reasoning effort. No execution has started merely by writing these plans.
