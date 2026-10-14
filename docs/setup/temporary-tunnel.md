# Temporary internet test through CGNAT

This optional test route was approved on 2026-10-14. Cloudflare Quick Tunnel provides a temporary HTTPS address for the room website and signaling. MediaMTX uses Google STUN to discover possible public UDP addresses and attempts a direct encrypted video connection. The video is not transported by the website tunnel. CGNAT/firewalls can still prevent it; a reachable authenticated TURN relay or another public room/media host would then be necessary. No relay is configured by these scripts.

No domain, account, viewer installation or paid service is required for this experiment. It adds external Cloudflare website/signaling and Google STUN dependencies. Cloudflare terminates website HTTPS and can see signaling traffic; media uses separate WebRTC encryption. Quick Tunnel is a development service without an uptime guarantee, changes hostname on restart, limits concurrent requests and does not support SSE. This app uses SignalR WebSockets. This is an optional profile, not a mandatory proprietary streaming backend. See [Cloudflare documentation](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/) and [MediaMTX connectivity](https://mediamtx.org/docs/features/webrtc-specific-features).

Stop existing owned services, then start this profile from the project directory:

```powershell
./scripts/Stop-Local.ps1
./scripts/Start-TemporaryTunnel.ps1 -MediaInterface Ethernet
./scripts/Start-Broadcaster.ps1
```

Use the exact adapter name from `Get-NetIPAddress -AddressFamily IPv4`; the script requires one preferred unicast IPv4 on that adapter. It excludes other adapters such as a VPN. The optional portable cloudflared 2026.10.0 download is checked against its official SHA256 before every launch; it is not installed as a system service and autoupdates are disabled. The explicit empty configuration avoids personal cloudflared configuration. Logs, generated configuration, process ownership state and downloaded executable remain in ignored project folders. Administration, media signaling and the host backend stay on loopback. No router/firewall settings are changed. The random UDP profile does not require the fixed 8189 port mapping used by the public-IP profile.

Paste the printed **localhost native app link** into the broadcaster on this PC. Browser viewers, including on this PC, must use the printed **public HTTPS link** because browser origin checks remain enabled. Keep its invitation private. Share it directly with friends; do not post it in source control or reports.

For the synthetic video-only acceptance fixture, build the existing host/native acceptance probe and timing source first, then:

```powershell
./scripts/Build-TimingSource.ps1
./.tools/dotnet/dotnet.exe build src/JKiller.Host/JKiller.Host.csproj
./.tools/dotnet/dotnet.exe build tools/acceptance/LiveFlowProbe/LiveFlowProbe.csproj
./scripts/Test-TemporaryVideo.ps1 -Seconds 1800 -MediaInterface Ethernet
```

The test starts its own tunnel/services, captures only the synthetic fixture, publishes requested 1080p60/15 Mbps, and stops all its owned processes at the deadline or on exit. It needs the installed project native plugin from `Build-Native.ps1 -Plugin -Test`. Leave its terminal running. Audio is explicitly disabled; silence cannot pass any audio or synchronization gate. The fixture's native diagnostics are collected privately. A room currently permits eight participants total, including the broadcaster, not eight viewers. Repeated new test sessions can occupy this limit; do not raise it or restart an active friend test merely to make a probe pass.

On the phone, turn Wi-Fi off, open the public invitation, join and select **Watch stream**. Run ten minutes, export Playback diagnostics, and record phone/OS/browser/network, observed smoothness, actual resolution, decoded/presented FPS, packet loss, RTT and direct/relay evidence. RTT is connection round-trip delay, not end-to-end video delay. Cross-device video latency needs clock calibration; audio timing remains pending. A local browser through the public URL does not prove an external network.

To stop an ordinary tunnel launch:

```powershell
./scripts/Stop-TemporaryTunnel.ps1
```

The stop script checks executable, PID creation time and matching service ownership before stopping processes. It refuses to kill a reused PID or another service run. If HTTPS works but video does not, inspect browser gathering/connection state, candidate types, native packet activity and server logs before changing architecture. A selected direct candidate pair proves a direct path for that connection; the absence of configured TURN alone is not a measured path classification.
