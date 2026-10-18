# Browser publishing, independent hosting and encoder compatibility

Investigated 2026-10-18 against source `9c279a9` plus the first M3 interface/cursor slice. These are implementation notes and feasibility answers, not claims that browser publishing or AMD/Intel native encoders have shipped.

**M3 implementation update, 2026-10-18:** Native AMD AMF H.264 initialization/configuration is implemented and code-only verified; live AMD acceptance remains pending. Website broadcasting is the next implementation slice. Shared previews/transcoding are scrapped and Intel Quick Sync is deferred. See [the M3 handoff](../M3-HANDOFF.md) and [AMD verification](../validation/2026-10-18-m3-amd.md). The original investigation retains its historical baseline.

## Broadcasting entirely from the website

Watching already works in the website. Broadcasting there needs a new browser publisher. The current React app contains WHEP receiving, but no screen-capture or WHIP publishing flow. The host already exposes authenticated publication reservations (`POST /api/streams`), renewal/stop and an authenticated WHIP proxy; those can be reused while preserving one publication per session and ownership/revocation.

The planned M3 browser publisher needs:

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

The native app attempts NVENC, then AMD AMF H.264, initializing and releasing candidates sequentially. Capability enumeration alone does not prove support. The **Allow CPU encoding if hardware initialization fails** checkbox remains off unless explicitly selected before starting. Only after both hardware candidates are unavailable/fail may it use x264 at a maximum of 720p30 and 4 Mbps, retaining lower requests. CPU encoding may compete with a game. Windows Graphics Capture and other Windows prerequisites still apply.

The managed/native AMF path uses pinned OBS 32.2.2's obs-ffmpeg module and the installed AMD driver's AMF runtime. Its initial policy is CBR/High, one-second keyframes, no B-frames, repeated headers, speed preset, disabled pre-analysis/pre-encode/VBAQ and low-latency usage. NVIDIA settings are retained. Incompatible readback fails closed; CPU fallback cannot bypass it. Manual adapter/encoder selectors are outside this slice.

Applied encoder identity and successful hardware initialization are distinct from actual media activity. OBS can internally reroute `h264_texture_amf` to AMF hardware encoding through frame uploads while its public ID/caps retain the original registration. The transfer route is therefore **unverified**; this is not x264 fallback and does not establish zero copies or low CPU/GPU/game impact. Settings readback is configuration evidence, not a readback of every driver property.

Use a matching rebuilt plugin after stopping the owned broadcaster or a fresh portable package. `Start-Broadcaster.ps1` rebuilds managed code only. CodeOnly does not install the plugin. The existing complete runtime includes obs-ffmpeg, obs-amf-test and FFmpeg dependencies; the driver supplies AMF. Existing licenses/source notices remain applicable.

Real AMD validation remains pending for actual output/audio, browser/WebView decoding, late joins, failure, restart, concurrency and game/frame-time impact. Integrated/discrete and mixed-adapter configurations retain individual test limits; no universal minimum driver or performance guarantee is claimed. Intel Quick Sync stays deferred.

Keep H.264/Opus for this compatibility work; changing to AV1/HEVC introduces a separate receiving-browser compatibility question. Prioritize the AMD hardware path, preserve NVIDIA and the existing explicit CPU fallback, and avoid extra encodes or expensive analysis that unnecessarily competes with games. Browser publishing would be another route for non-NVIDIA users, with browser-controlled encoding and the capture/audio limitations above.

Source: [OBS hardware encoding](https://obsproject.com/kb/hardware-encoding). Current code: `src/JKiller.Windows/Obs/ObsMediaEngine.cs` and `native/jk-obs/src/ObsPipeline.cpp`.
