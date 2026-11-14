# Refactor and optimization pass — 2026-11-13

Branch `codex/refactor-optimizations`, based on `main` at `7697eed`. Code-only verification; no live capture, media server, AMD hardware or external-network test was run. This does not change any pending M3 live-acceptance status.

## Changes

**Website (no visual changes; existing styles and copy are reused)**

- `web/src/media/signaling.ts` now holds the WHIP/WHEP code the two sessions duplicated: endpoint/resource validation, bounded signaling requests, peer-state waits, connection supervision (immediate fail on `failed`, 2 s grace on `disconnected`), best-effort resource deletion, ICE `Link` parsing and codec capability checks. `WhepSession` and `WhipSession` keep their public APIs, error messages and cleanup order. Both now accept only canonical UUID resource paths (the viewer previously accepted any 36 hex/dash characters).
- WHEP failure classification is a small `classifyFailure()` method instead of a nested ternary.
- Viewer diagnostics posting moved from `StreamPlayer` into `media/diagnosticsReporter.ts` (still one request in flight per stream, same payload). Stream icons moved to `room/Icon.tsx` as a static table instead of being rebuilt on every render. The sound toggle is a separate, explicit `toggleSound()`.
- Diagnostic export failures show an inline alert inside the panel instead of a blocking `alert()` dialog. Both diagnostics downloads use one `downloadBlob()` helper.
- `RoomPage` shares one join/remember-invitation path between restoring and joining, and ignores room snapshots whose revision is not newer than the current one (previously an equal-revision duplicate re-rendered the whole room).

**New behavior**

- **Room updates recover by themselves.** SignalR's automatic reconnect stops after three attempts (1/2/4 s); afterwards the room list silently stopped updating until reload. The page now shows "Room updates were interrupted. Reconnecting…" while SignalR reconnects and reopens a fresh subscription after it gives up, backing off 2/5/10/15 s. The host already sends a full snapshot on every connection, so no change is lost. Leaving the room cancels pending retries.
- **The browser broadcaster remembers the last started quality** (resolution, FPS, bitrate) in this browser's local storage. Invalid or unreadable values fall back to 720p30/4 Mbps. Captured audio is deliberately *not* remembered and stays opt-in for every broadcast.

**Room host**

- Each room-changing endpoint sends its snapshot immediately, and `RoomUpdatesService` then sent the same revision again ~100 ms later, so every participant received each change twice. `RoomConnections` now delivers each room revision once and skips stale revisions; a failed send can still be retried.
- The diagnostics rate check (one sample per observer/stream every 900 ms) uses a dictionary lookup instead of scanning up to 4,096 stored samples on every POST.

**Windows app**

- Publisher diagnostics rates, UI text and the report envelope moved from `BroadcasterModel.Observe` into `Presentation/PublisherObservation.cs`, now unit-tested. Same metrics, text and metadata allow-list.

## Verification

| Check | Result |
|---|---|
| `dotnet test JKiller.sln` | Core 101, Host 78 (+2), Windows 120 (+4): all passed |
| `dotnet build src/JKiller.App` | 0 warnings, 0 errors |
| `npm test -- --run` (web) | 15 files, 132 tests (+10): all passed |
| `npm run build` (typecheck + Vite) | passed |

New tests cover signaling helpers, room resubscription/backoff/cancel-on-leave, stale-snapshot rejection, quality preference persistence (and that audio is never remembered), host revision de-duplication and publisher observation math. Native (`jk-obs`) code was not changed.

## Visual refresh — 2026-11-14

The website keeps its brand palette (navy `#0E182A`, yellow `#FFDE5A`, blue `#6A94C5`), logo, copy and controls, with a more polished layout:

- Colors are shared CSS tokens. The page has a soft brand-tinted glow instead of a flat fill.
- The header holds the room count (with a presence dot) and language selector together instead of a separate floating row.
- **Start sharing** is the primary yellow action; **Stop sharing** is red-tinted. The sharing panel shows a status dot (grey idle, yellow while connecting, red when live) and groups its quality fields and help notes.
- Participants appear as chips with initials; your own chip is highlighted.
- Stream cards have a framed watch button, a red **Live** badge with a dot, and refined borders and shadows. Your own broadcast card and the empty room have dedicated styling.
- Mobile layout puts the header actions on their own row and makes the sharing button full width.

Checked with Edge screenshots of mocked join, room, focused, empty and 390 px mobile states. Web tests (132) and the production build pass. The WPF window and the WebView layout probes were not rerun.
