# PlayKross

![PlayKross](assets/branding/playkross-logo.png)

Self-hosted Windows broadcasting with browser and in-app viewers. PlayKross supports concurrent broadcasters, independent stream playback, Windows Graphics Capture, shared H.264/Opus encoding and fullscreen controls that hide immediately and after two seconds of inactivity. Audio starts Off; explicitly choose Application or System output for monitor sharing. Microphone capture is absent.

**M1 accepted with known limitations (2026-10-16); M2 accepted after owner testing (2026-10-18); M3 in progress:** fixed room participant/publisher quotas have been removed, while each session still owns at most one publication. Functional evidence and the owner's sign-off are recorded in [M2 results](docs/validation/milestone-2-results.md). No maximum capacity is proven. Startup stutters, strict audio isolation and external numerical characterization remain deferred. See the [current roadmap](docs/ROADMAP.md) and [M3 handoff](docs/M3-HANDOFF.md).

Project owner and contributor: [@vitor-tecc](https://github.com/vitor-tecc). See [contributors](CONTRIBUTORS.md) and the [M3 handoff](docs/M3-HANDOFF.md).

The [approved M2 design](docs/superpowers/specs/2026-10-16-milestone-2-design.md) uses small functional checks; no eight-viewer or large-group acceptance campaign is required. Scaling work follows observed problems.

For friends who broadcast, use the [portable ZIP instructions](docs/setup/portable.md). Extract the whole package and open **PlayKross.exe**; .NET is bundled. Browser-only viewers need only the public room invitation. The room host runs separately.

On this already-prepared PC, run PowerShell 7 from the project folder:

```powershell
./scripts/Start-Local.ps1
./scripts/Start-Broadcaster.ps1
```

Paste the printed `http://localhost:5000/#invite=...` link into the native app, choose a nickname, Join, select video and Start. Application audio captures the selected process and its children. System output requires a selected monitor and concrete render device, captures mixed device playback including Discord, and mutes in-app room playback to prevent recapture. External browsers can still be captured; use a separate output or application audio. Display selection never enables audio automatically. The room page opens in a browser or with **Watch in app**. Localhost links work only on this PC.

Video/audio selections can be staged while live and applied with **Apply sources and settings (restart)**. Viewer controls and independent volume are overlaid on each stream; click a stream to focus it, or return to the grid. The in-app browser stores its profile under Windows LocalAppData to support deeply nested portable extraction folders. See the [iteration report](docs/validation/2026-10-17-m2-iteration.md) for playback diagnostics, current verification limits and OBS/quality architecture answers.

Browsers and shared application hosts can be explicitly selected for Application audio. Capture includes their child processes and can include other tabs, Discord running in a browser, and room playback. The Discord desktop app remains excluded from direct Application selection; individual browser tabs are not isolated. System output still captures the selected device's mixed audio, including Discord.

In a browser, **Join room**, then click **Watch stream**; use **Enable sound** separately. To broadcast from this browser, use **Start sharing** and its source chooser over HTTPS or localhost; **Share captured audio** is optional and source/browser-dependent. Use **Stop sharing** to end it. No microphone or desktop helper is requested. See [browser publishing](docs/setup/publishing-and-compatibility.md#broadcasting-entirely-from-the-website) for limits and pending live validation. For Firefox on this PC, start the server with `./scripts/Start-Local.ps1 -MediaInterface Ethernet` instead of default startup. This explicitly uses the Ethernet address for encrypted UDP media while the website/admin stay on localhost. Default loopback-only media was observed to fail in Firefox here, while Edge worked. See [Firefox local viewing](docs/setup/windows.md#firefox-on-the-host-pc) for scope and adapter selection.

```powershell
./scripts/Stop-Local.ps1
```

Stop broadcasting first. Closing the native app kills its owned helper; Stop-Local stops only tracked host/media processes. Your separately installed OBS and profiles are not used.

Use **Leave room** for immediate departure. A closed browser or lost connection gets a 30-second reconnect grace after the server detects the last connection ending. Active tabs and the native app maintain presence independently of the 12-hour session credential.

[Fresh Windows setup](docs/setup/windows.md) · [Friends outside your home / no domain](docs/setup/internet.md) · [Measurements and pending acceptance](docs/validation/milestone-1-results.md) · [Diagnostic field meanings](docs/validation/room-diagnostics.md) · [Approved architecture](docs/superpowers/specs/2026-10-13-screen-sharing-design.md) · [Implementation plan](docs/superpowers/plans/2026-10-13-milestone-1.md)

Each broadcaster has one shared encoder pipeline; the media server forwards copies to viewers. Upload and viewer decoding needs grow with active streams. M3 now prioritizes AMD AMF H.264 broadcasting with minimal game impact and also includes starting broadcasts from the website using browser capture. AMD and browser publishing are implemented and code-only verified; real AMD/browser audio/video/game-impact acceptance remains pending. Intel support is deferred. Shared lower-quality previews have been scrapped to avoid additional encoding workload. Separate viewer quality levels are not implemented; slower viewers may stall or discard frames.

```powershell
./scripts/Verify.ps1 -Mode Automated
./scripts/Verify.ps1 -Mode CodeOnly
./scripts/Verify.ps1 -Mode LocalMedia
./scripts/Verify.ps1 -Mode External -RoomLink 'https://YOUR_PUBLIC_ADDRESS/#invite=...'
```

Use **CodeOnly** while using the PC: it compiles and runs non-interactive correctness tests without launching the app, browser, media server or capture/tone fixtures, and does not install the compiled native plugin. Automated/LocalMedia include additional program/device activity; experimental audio needs fresh real tests before any quality or exclusion claim. Correctness checks do not prove real streaming performance. External mode deliberately reports pending until a separate-network viewer supplies real evidence.

The native plugin and rebuilt OBS capture component retain their GPL source/licenses under `native/`; pinned upstream downloads and package locks record dependencies. This source tree is a development build, not a signed installer. Review [third-party components](docs/setup/windows.md#components-and-licenses) before distributing binaries.

[GitHub development](docs/setup/github.md) · [Workspace size and safe cleanup](docs/setup/workspace-size.md)
