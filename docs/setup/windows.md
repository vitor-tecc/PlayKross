# Windows setup

Use Windows 11 x64 (tested build 26200). Process-loopback APIs need Windows build 20348 or newer; native capture currently targets SDK 10.0.26100, and broadcasting is Windows-only. Browser viewers can use other operating systems, subject to actual browser/codec tests.

Install PowerShell 7.4+, Git, Node.js 24 LTS with npm, and Visual Studio C++ Desktop Build Tools including CMake, x64 MSVC and Windows 11 SDK 10.0.26100 or newer. The scripts recognize Visual Studio 2022/2026 generators. Microsoft Evergreen WebView2 is needed for **Watch in app**; [Microsoft runtime distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution) explains installation. NVIDIA's working driver/NVENC support is required for the default encoder. CPU fallback is an explicit opt-in and needs its own performance check.

Open PowerShell in the repository. Do not run the app as administrator. Run:

```powershell
./scripts/Setup-Dev.ps1 -CheckOnly
./scripts/Setup-Dev.ps1
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.tools/dotnet-home'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
npm --prefix web ci --cache .tools/npm-cache
npm --prefix web run build
./scripts/Build-Native.ps1 -Plugin -Test
./scripts/Build-InstrumentedObs.ps1
./.tools/dotnet/dotnet.exe build JKiller.sln
./.tools/dotnet/dotnet.exe build tools/acceptance/LiveFlowProbe
./.tools/dotnet/dotnet.exe build tools/acceptance/OwnedEngineProbe
./scripts/Verify.ps1 -Mode Automated
./scripts/Start-Local.ps1
./scripts/Start-Broadcaster.ps1
```

Setup downloads pinned SDK, portable OBS and MediaMTX to this project and verifies hashes before extraction. Native builds download pinned OBS source/JSON headers and verify them. Instrumentation rebuilds only the owned WGC component; missing instrumentation appears as unavailable capture FPS. Stop the owned broadcaster before rebuilding native libraries. This does not change personal OBS settings. Build/download errors must be fixed before launching; scripts do not install system prerequisites or relax execution policy.

Copy the printed room invitation into the app and browser. Invite fragments are secrets; don't put them in screenshots, logs or public issues. Pick video, then optional **explicit application audio**. Display capture never automatically selects system audio. Requested settings can be changed with **Apply settings (restart)**, which briefly interrupts capture while retaining stream identity. Applied encoder readback and observed output rates are separate displays.

Native app sessions are protected with Windows DPAPI and user/SYSTEM ACLs. Native OBS uses a private PID/creation-identity-bound pipe, not an open websocket control port. The development services bind TCP 5000/5001/8889/9997/9998 and UDP 8189 to loopback. Public hosting is opt-in and documented separately. Keep the PC awake while hosting.

Run `Verify -Mode LocalMedia` with no project service already running. It starts/stops its own services and synthetic fixture and tests actual H.264/Opus browser playback and WebView2. It uses installed Edge, no screenshots or real desktop recording. It is a protocol smoke test. The later sustained harness records numerical synthetic measurements separately. A renderer may report 60 FPS while a static/slow source repeats frames; inspect capture and encoded rates together.

## Components and licenses

| Component | Pin / purpose | Distribution considerations |
|---|---|---|
| .NET SDK/runtime | 10.0.401 / 10.0.12 | Microsoft .NET license/notices |
| OBS / native plugin | 32.2.2 / shared NVENC H.264, Opus, WHIP | GPL-2.0-or-later; retain corresponding modified source and notices |
| MediaMTX | 1.21.2 / media forwarding | MIT, upstream notices |
| WebView2 SDK | 1.0.4258.31 | Microsoft terms; runtime is separately installed Evergreen |
| React / SignalR / Vite | exact web/package-lock.json | retain upstream licenses; development tools aren't media servers |
| nlohmann JSON | 3.12.0 | MIT |
| Optional Caddy | 2.11.7 | Apache-2.0; checksum-pinned Windows binary |

The broadcaster encoder was actually initialized as `obs_nvenc_h264_tex` on RTX 5070 / driver 617.14, and produced video packets. Device-wide encoder utilization also included other GPU activity, so that alone cannot prove application attribution. H.264/Opus is the current interoperability choice; explicit CPU H.264 fallback keeps codec compatibility. AV1/multiple renditions are deferred until compatibility and performance are measured.
