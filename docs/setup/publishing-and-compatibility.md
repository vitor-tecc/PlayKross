# Browser publishing, independent hosting and encoder compatibility

Investigated 2026-10-18 against source `9c279a9` plus the first M3 interface/cursor slice. These are implementation notes and feasibility answers, not claims that browser publishing or AMD/Intel native encoders have shipped.

**M3 implementation update, 2026-10-18:** Native AMD AMF H.264 initialization/configuration is implemented and code-only verified; live AMD acceptance remains pending. Website broadcasting is also implemented and code-only verified; live browser/audio acceptance remains pending. Shared previews/transcoding are scrapped and Intel Quick Sync is deferred. See [the M3 handoff](../M3-HANDOFF.md) and [AMD verification](../validation/2026-10-18-m3-amd.md). The original investigation retains its historical baseline.

## Broadcasting entirely from the website

The joined room now offers **Start sharing**, **Stop sharing** and an initially unchecked **Share captured audio** option. Use HTTPS (or suitable localhost development) and a browser with display capture and H.264 sending support. The browser opens its own screen/window/tab chooser on each start; PlayKross does not silently choose a source or request a microphone. No desktop app or OBS helper is needed for this route; the room/media server must still be reachable.

The initial target is up to 1280x720 at 30 fps and 4 Mbps with captured aspect ratio retained. Capture constraints and the single sender's bitrate limit are browser-managed, not an exact output guarantee. Browser publishing does not expose native AMF/NVENC controls or guarantee hardware acceleration or low game impact. No preview, simulcast rendition or per-viewer encode is created.

Captured audio is opt-in and browser/OS/source-dependent. A requested track may be absent or later end; the page discloses video-only operation without substituting a microphone. This does not provide native per-process audio isolation. Room sound in the publishing tab is muted before requesting captured audio, and restored after it ends/stops without clearing a native mute block or card volume choices. Other tabs/apps may still be captured. The broadcaster's own card becomes a status tile, avoiding self-playback decoding/audio; other streams remain watchable.

The sender uses the existing authenticated reservation/lease/WHIP flow. Browser starts atomically request a new publication and cannot adopt an existing same-session stream. Conflicting tabs receive an explanation and cannot stop the other tab's publication through their local controller. The default native reservation behavior remains unchanged. Stop, ended video, failed connections, leave and page departure release owned tracks and request resource/publication cleanup. Every restart needs another gesture/chooser and fresh grant; capture never restarts automatically.

Publication renewal runs every 15 seconds with a conservative local cutoff before the server's 60-second lease. Failed cleanup after abrupt network/page loss falls back to server expiry/revocation. Fully suspended JavaScript cannot execute track-stop timers: publication expiry remains server-enforced, and local tracks stop when execution resumes. A viewing tab's departure does not implicitly leave the shared authenticated session or stop another controller's broadcast.

[Browser code verification](../validation/2026-10-18-m3-browser.md) records automated and mocked-layout evidence. A subsequent [video-only playback regression check](../validation/2026-10-18-m3-browser-playback.md) verified short real synthetic browser transport/decode in Edge and Firefox 158. The viewer accepts MediaMTX's PCMU placeholder for publications with no captured audio; real published audio remains Opus. Run `./scripts/Test-BrowserPublishing.ps1` for the three local browser protocol regressions. Actual display capture/audio, native/browser coexistence and CPU/GPU/game impact remain pending; no broad browser support matrix is claimed yet.

Sources: [W3C Screen Capture](https://www.w3.org/TR/2026/WD-screen-capture-20260827/), [WHIP RFC 9725](https://www.rfc-editor.org/rfc/rfc9725.html), and [MediaMTX browser publishing](https://mediamtx.org/docs/publish/web-browsers).

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

Keep H.264/Opus for this compatibility work; changing to AV1/HEVC introduces a separate receiving-browser compatibility question. Prioritize the AMD hardware path, preserve NVIDIA and the existing explicit CPU fallback, and avoid extra encodes or expensive analysis that unnecessarily competes with games. Browser publishing provides another implemented route, with browser-controlled encoding and the unvalidated live capture/audio limitations above.

Source: [OBS hardware encoding](https://obsproject.com/kb/hardware-encoding). Current code: `src/JKiller.Windows/Obs/ObsMediaEngine.cs` and `native/jk-obs/src/ObsPipeline.cpp`.
