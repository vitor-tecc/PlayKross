# Friends outside your home

The host runs the room website, invitation/session service and MediaMTX. Each broadcaster sends one encoded stream to that host; viewers receive copies from the host. The host can initially be your PC. Hosting the website alone does not make WebRTC media reachable.

You do **not** need to buy a domain. Let's Encrypt now supports short-lived IP-address certificates; the pinned Caddy 2.11.7 example requests the `shortlived` ACME profile. This still needs a reachable public IP, incoming HTTPS validation and automatic renewal. An optional free dynamic-DNS provider can give a stable hostname; it adds a DNS provider/account dependency, not a streaming backend. Certificate issuance is free, depends on an external certificate authority and may reveal the address in public certificate records. Dynamic residential addresses can change and invalidate old links.

When you can access the router, compare its WAN address with your public address. Private WAN ranges (10/8, 172.16/12, 192.168/16) and 100.64/10 commonly indicate another NAT/CGNAT layer. Ordinary port forwarding won't bypass your ISP's CGNAT. Ask the ISP for public IPv4, use reachable IPv6 if both ends support it, or host the room/media server on another publicly reachable machine. A self-hosted TURN relay needs a reachable address too; a relay behind the same unreachable NAT doesn't solve it. ISP/static-IP/remote-host costs depend on your provider.

Reserve this PC's LAN address. Forward TCP **80 and 443** for HTTPS certificate validation/web traffic and UDP **8189** for media to that PC. Add narrowly scoped inbound Windows firewall rules for the relevant executables/ports on the appropriate home profile yourself. No script changes firewall/router rules. Never forward host ports 5000/5001, MediaMTX 8889/9997/9998 or Caddy administration 2019; those remain loopback/private. Disable UPnP auto-opening; it is not used by this project. UDP 443 is unnecessary for this profile.

Stop local services and prepare the optional reverse proxy:

```powershell
./scripts/Stop-Local.ps1
./scripts/Setup-Dev.ps1 -Dependencies caddy
$env:JKILLER_PUBLIC_HOST = 'YOUR_PUBLIC_IP_OR_HOSTNAME'
./.tools/caddy/caddy.exe adapt --config config/Caddyfile.example --adapter caddyfile
./scripts/Start-Local.ps1 -PublicOrigin "https://$env:JKILLER_PUBLIC_HOST"
./.tools/caddy/caddy.exe run --config config/Caddyfile.example --adapter caddyfile
```

`adapt` only parses; it doesn't start hosting or issue a certificate. `run` starts the public HTTPS service and certificate authority requests. Keep that terminal running; Ctrl+C stops Caddy. Start-Local with PublicOrigin opens the media UDP listener and advertises your configured public address plus loopback, while retaining all administrative/signaling TCP listeners on loopback. It does not start Caddy or configure network exposure. The host/private secrets are generated in process memory and never passed in argv. Stop tracked host/media with Stop-Local.

Use the printed public invitation for friends and remote broadcasters. The host PC's native app can use the separately printed localhost invitation to avoid router hairpin/NAT-loopback problems; that is the same room/invite. Its **Open browser viewer / Watch in app** localhost page is a local development route and should only be used under the localhost origin profile; with a public profile, browser-origin enforcement requires the public HTTPS page (use your router's hairpin support or LAN DNS mapping to the host). Native bearer operations deliberately omit browser Origin headers and have no cookie jar. Do not weaken certificate/origin checks to work around a routing failure.

First load `/health` from the phone with Wi-Fi off; then join, Watch, Enable sound and inspect Playback diagnostics. A working page plus failed media usually points to UDP/firewall/advertised-address/ICE, not an encoder decision. Export after a ten-minute test. Record device/browser, network, direct/relay evidence, loss, RTT, presented FPS and separately measured video/audio timing. `Verify -Mode External -RoomLink 'https://.../#invite=...'` prints this checklist and stays pending; the PC cannot certify that your phone used mobile data.

For networks blocking UDP, optional MediaMTX ICE TCP or a **self-hosted coturn** relay can help. TCP head-of-line blocking may increase latency. coturn must be publicly reachable, use authentication (never an anonymous/open relay), TLS where supported, a protected long-term credential or expiring TURN REST credentials, bounded allocation/bandwidth limits and an explicit relay port range/firewall setup. Set MediaMTX `webrtcICEServers2` according to its pinned documentation, protect that generated config, and advertise TURN through the authenticated WHIP/WHEP OPTIONS exchange. The present script profile has no relay configured or tested. Local TURN on this host won't bypass host CGNAT; a remote relay/remote host needs sufficient bandwidth and may cost money. Do not use a general website tunnel as evidence that UDP streaming will work.

One shared quality level avoids repeated encoding but offers no per-viewer lower rendition. Slow viewers have bounded server queues and may lose/stall frames. Server upload remains copies × streams: eight viewers at 15 Mbps ≈120 Mbps before overhead, two such streams ≈240 Mbps. The initial 175 Mbps budgeting ceiling is provisional; your reported 250 Mbps uplink is not an actual streaming measurement. Later shared preview/full renditions and focused viewing must fit both encoder capacity and viewer decoding budgets.

Current references: [Caddy TLS issuer/profile syntax](https://caddyserver.com/docs/caddyfile/directives/tls), [Caddy 2.11.7](https://github.com/caddyserver/caddy/releases/tag/v2.11.7), [Let's Encrypt IP certificate availability](https://letsencrypt.org/2026/01/15/6day-and-ip-general-availability), [MediaMTX WebRTC connectivity](https://mediamtx.org/docs/usage/webrtc-specific-features), [coturn deployment](https://github.com/coturn/coturn/wiki/turnserver).
