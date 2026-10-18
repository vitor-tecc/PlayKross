# Browser publishing, independent hosting and encoder compatibility

Investigated 2026-10-18 against source `9c279a9` plus the first M3 interface/cursor slice. These are implementation notes and feasibility answers, not claims that browser publishing or AMD/Intel native encoders have shipped.

## Broadcasting entirely from the website

Watching already works in the website. Broadcasting there needs a new browser publisher. The current React app contains WHEP receiving, but no screen-capture or WHIP publishing flow. The host already exposes authenticated publication reservations (`POST /api/streams`), renewal/stop and an authenticated WHIP proxy; those can be reused while preserving one publication per session and ownership/revocation.

A browser publisher would need:

1. A user-initiated Share screen/window/tab control using `getDisplayMedia()` over HTTPS (localhost is suitable for development). Every new capture requires the browser's source/permission chooser; the website cannot silently choose a source.
2. A WHIP sender around `RTCPeerConnection`: attach screen video and optional supported capture-audio tracks, negotiate compatible media (prefer the existing H.264/Opus path), exchange SDP with the granted endpoint, handle ICE and clean up the WHIP resource.
3. Reservation/lease lifecycle, stop/cancel/permission failure, the browser's own Stop sharing action, track-ended events, reconnect, navigation and duplicate-tab ownership. Failed starts must release the reservation and capture tracks.
4. Publishing/settings/status UI and tests for publishing while watching, independent concurrent publishers, cancellation, restart and feedback handling. Keep microphone capture out unless separately requested.
5. A measured browser/OS support matrix for both video and audio. Captured audio is optional and browser/OS/source-dependent; a requested audio track may be absent. Desktop Chromium is a reasonable first target to validate, with video-only fallback disclosed. Browser process isolation and native per-application audio behavior cannot be promised by the website.

The browser performs capture and encoding, so recipients need no PlayKross/OBS installation for that route. Encoding hardware choice and available controls are browser-managed; website publishing is not a guarantee of hardware acceleration or native 1080p60 performance. A reachable room/media server is still required. This fits the existing media architecture but is a separate feature, larger than adding one button.

Sources: [MDN getDisplayMedia](https://developer.mozilla.org/en-US/docs/Web/API/MediaDevices/getDisplayMedia), [MediaMTX browser publishing](https://mediamtx.org/docs/publish/web-browsers), and [WebRTC connectivity/codecs](https://mediamtx.org/docs/features/webrtc-specific-features).

## Can someone clone and host their own Cloudflare tunnel?

Yes, provided their Windows machine meets the [fresh development prerequisites](windows.md) and they complete the pinned dependency, web, native and host builds. The scripts derive paths from their own checkout. They create their own runtime state and room invitations; they do not depend on the owner's Cloudflare account or a committed personal tunnel credential.

After the documented fresh setup, they can use:

```powershell
Get-NetIPAddress -AddressFamily IPv4 | Select-Object InterfaceAlias,IPAddress,AddressState
./scripts/Start-TemporaryTunnel.ps1 -MediaInterface 'Ethernet'
# On a prepared broadcaster PC, to publish natively:
./scripts/Start-Broadcaster.ps1
# When finished:
./scripts/Stop-TemporaryTunnel.ps1
```

Replace `Ethernet` with their exact adapter name. The tunnel script downloads/verifies the pinned cloudflared binary, starts the local host/MediaMTX and prints a new private HTTPS room invitation. Cloudflare Quick Tunnels need no account/domain for this route and provide a temporary hostname; they are development tunnels with service limits and no uptime guarantee. A portable broadcaster ZIP alone does not contain/start this server setup.

The HTTPS tunnel carries the website and signaling. WebRTC media travels separately; the script uses STUN/hole punching, which cannot guarantee connectivity through every CGNAT/firewall. More reliable deployment needs reachable media ports or an authenticated TURN relay/public media host. The current temporary-tunnel script does not configure TURN. Keep the host awake and use the public invitation rather than sending `localhost` to friends. Multiple checkouts on the same machine still contend for fixed local service ports.

Sources: [project tunnel instructions](temporary-tunnel.md), [Cloudflare Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/), and [MediaMTX connectivity](https://mediamtx.org/docs/features/webrtc-specific-features).

## Broadcasting without NVIDIA NVENC

The current native app already has an explicit **Allow CPU encoding if NVENC initialization fails** checkbox. Select it before starting. If NVENC is absent or fails initialization, the app can use bundled OBS x264 with an applied maximum of 720p30 and 4 Mbps, retaining lower requested settings. That is CPU encoding and may compete with a game for CPU time. An AMD/Intel PC must still satisfy Windows Graphics Capture and the application's other prerequisites; CPU fallback does not make the Windows app cross-platform. Real performance on each target machine needs testing.

To improve hardware coverage, add AMD AMF H.264 and Intel Quick Sync H.264. OBS supports these encoder families, but PlayKross currently chooses only NVENC/x264 and the native typed pipeline rejects other encoder IDs. This requires both managed and native changes:

- Discover supported encoders and attempt actual initialization; an enumerated encoder is not proof of working hardware/driver support. Offer Auto/hardware selection with clear applied-encoder diagnostics and explicit CPU fallback consent.
- Add encoder-specific configuration/readback adapters. Preserve low latency, no B-frames/lookahead where supported, repeated H.264 parameter sets for late joiners, bitrate/keyframe settings and the shared one-encode-per-broadcaster pipeline. Do not reuse NVIDIA preset names for other encoders.
- Generalize native validation and hardware-initialized diagnostics, then verify the packaged OBS modules and licensing/notices for each path.
- Test real AMD/Intel systems, including integrated/discrete and multi-adapter configurations, hardware failure, restart, late joins, audio and browser/WebView decoding. Mocked capability tests cannot establish working AMF/QSV encoding.

Keep H.264/Opus for this compatibility work; changing to AV1/HEVC introduces a separate receiving-browser compatibility question. Start by making the existing CPU option discoverable, then implement and validate vendor hardware paths. Browser publishing would be another route for non-NVIDIA users, with browser-controlled encoding and the capture/audio limitations above.

Source: [OBS hardware encoding](https://obsproject.com/kb/hardware-encoding). Current code: `src/JKiller.Windows/Obs/ObsMediaEngine.cs` and `native/jk-obs/src/ObsPipeline.cpp`.
