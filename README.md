# PlayKross

![PlayKross](assets/branding/playkross-logo.png)

Self-hosted Windows broadcasting with browser viewers. The first implemented milestone provides one publisher per room: native Windows Graphics Capture, NVIDIA H.264 encoding, MediaMTX forwarding, anonymous nicknames, settings controls, fullscreen, diagnostics and in-app viewing. Application audio is restored as an explicitly experimental, off-by-default option for Steam Geometry Dash. Microphone capture is absent.

**Milestone acceptance is still pending:** complete external numerical/stability measurements remain pending; external moving video has been confirmed by the user. Experimental audio does not satisfy the original strict Discord-exclusion requirement and has not been live-tested in this revision. A ten-minute full-source 1080p60 synthetic loopback run is preserved as historical evidence; it does not prove this revision, internet or multi-viewer performance. Multiple broadcasters and the eight-viewer capacity milestone follow this gate. Current room limit is eight participants including the publisher, so it is not an eight-viewer capacity claim.

For friends who broadcast, use the [portable ZIP instructions](docs/setup/portable.md). Extract the whole package and open **PlayKross.exe**; .NET is bundled. Browser-only viewers need only the public room invitation. The room host runs separately.

On this already-prepared PC, run PowerShell 7 from the project folder:

```powershell
./scripts/Start-Local.ps1
./scripts/Start-Broadcaster.ps1
```

Paste the printed `http://localhost:5000/#invite=...` link into the native app, choose a nickname, Join, choose a video source, and Start. Audio stays off unless explicitly selected: the supported Steam Geometry Dash profile captures the app and its children. Direct Discord/browser and room-playback audio selection is blocked, but Windows also captures future children, whose exclusion is not guaranteed. Other apps stay video-only. See the [M1 remaining work](docs/validation/milestone-1-next-steps.md). The room page opens in a browser or with **Watch in app**. Localhost links work only on this PC.

In a browser, **Join room**, then click **Watch stream**; use **Enable sound** separately. For Firefox on this PC, start the server with `./scripts/Start-Local.ps1 -MediaInterface Ethernet` instead of default startup. This explicitly uses the Ethernet address for encrypted UDP media while the website/admin stay on localhost. Default loopback-only media was observed to fail in Firefox here, while Edge worked. See [Firefox local viewing](docs/setup/windows.md#firefox-on-the-host-pc) for scope and adapter selection.

```powershell
./scripts/Stop-Local.ps1
```

Stop broadcasting first. Closing the native app kills its owned helper; Stop-Local stops only tracked host/media processes. Your separately installed OBS and profiles are not used.

Use **Leave room** for immediate departure. A closed browser or lost connection gets a 30-second reconnect grace after the server detects the last connection ending. Active tabs and the native app maintain presence independently of the 12-hour session credential.

[Fresh Windows setup](docs/setup/windows.md) · [Friends outside your home / no domain](docs/setup/internet.md) · [Measurements and pending acceptance](docs/validation/milestone-1-results.md) · [Diagnostic field meanings](docs/validation/room-diagnostics.md) · [Approved architecture](docs/superpowers/specs/2026-10-13-screen-sharing-design.md) · [Implementation plan](docs/superpowers/plans/2026-10-13-milestone-1.md)

One encoder feeds the media server; it forwards copies to viewers. This avoids encoding per viewer, while still consuming server upload per copy. At 15–20 Mbps, eight viewers need approximately 120–160 Mbps before overhead. Several streams multiply this load and also require viewer decoding work. Shared lower-quality previews and separately selectable quality levels are future milestones; one quality level currently means slower viewers may stall or discard frames without receiving a lower rendition.

```powershell
./scripts/Verify.ps1 -Mode Automated
./scripts/Verify.ps1 -Mode CodeOnly
./scripts/Verify.ps1 -Mode LocalMedia
./scripts/Verify.ps1 -Mode External -RoomLink 'https://YOUR_PUBLIC_ADDRESS/#invite=...'
```

Use **CodeOnly** while using the PC: it compiles and runs non-interactive correctness tests without launching the app, browser, media server or capture/tone fixtures, and does not install the compiled native plugin. Automated/LocalMedia include additional program/device activity; experimental audio needs fresh real tests before any quality or exclusion claim. Correctness checks do not prove real streaming performance. External mode deliberately reports pending until a separate-network viewer supplies real evidence.

The native plugin and rebuilt OBS capture component retain their GPL source/licenses under `native/`; pinned upstream downloads and package locks record dependencies. This source tree is a development build, not a signed installer. Review [third-party components](docs/setup/windows.md#components-and-licenses) before distributing binaries.

[GitHub development](docs/setup/github.md) · [Workspace size and safe cleanup](docs/setup/workspace-size.md)
