# Private router checkpoint

Run `pwsh -File scripts/Start-Local.ps1` from the repository root. It builds the host, starts only the project-owned .NET host and pinned MediaMTX 1.21.2, and displays an invitation link. Stop them with `pwsh -File scripts/Stop-Local.ps1`. The browser interface is added in the next task; `/health` is available now. PowerShell 7.4 or newer is required by these service scripts.

The local profile binds room HTTP on 127.0.0.1:5000, private bootstrap/auth on 127.0.0.1:5001, media signaling on 127.0.0.1:8889, administration on 127.0.0.1:9997, metrics on 127.0.0.1:9998, and WebRTC UDP on 127.0.0.1:8189. It opens no public listener and changes no firewall or router configuration. Remote deployment is a later setup task.

Measured checks on this machine:

- The actual binary accepts the local configuration. Its administration request authenticates through the private host callback; anonymous administration returns 401.
- Two complete local startup/stop rounds pass. An occupied port leaves the existing listener alone. A held startup lock prevents a competing launch from overwriting ownership metadata. Stop checks original creation time and executable path before terminating any tracked process.
- Host correctness tests: 28 passing; Core: 29; Windows: 20. HTTP tests cover session/ownership distinctions, secure public cookies, rejected cross-origin room and SignalR requests, bounded joins, nickname data, SDP/header forwarding, rewritten resource URLs, PATCH/DELETE ownership and grant expiry. Mocked SDP is protocol/control correctness evidence only.
- Revocation tests exercise native media-session disconnection commands, resource invalidation, host shutdown, and atomic authorization commits. Real established WebRTC disconnection still needs a real publication, added during the browser integration task.

Invitations grant room entry. Session credentials grant room control. Separate media grants permit one generated path and action for at most 60 seconds, capped by session expiry. Existing resource cleanup uses the original authenticated session or original resource credential, so an expired handshake grant can still close its own established resource. Revocation immediately removes resource access and explicitly disconnects known/starting media sessions through private administration. Room expiry updates are sent as revisioned snapshots.

Bootstrap and administrator secrets are generated per local run and passed through the host process environment. They do not appear in process arguments or tracked process JSON. Invitation links are intentionally displayed to the host, with credentials omitted from persistent application/request logs and validation output.

The proxy fixes its upstream origin, accepts only generated media paths, validates returned session URLs, and exposes its own resource IDs. It forwards signaling, SDP and ICE headers; it does not relay the actual media packets through ASP.NET.

Version-dependent behavior was checked against the [pinned MediaMTX configuration](https://github.com/bluenviron/mediamtx/blob/v1.21.2/mediamtx.yml), [HTTP authentication documentation](https://mediamtx.org/docs/features/authentication), [pinned session API](https://github.com/bluenviron/mediamtx/blob/v1.21.2/api/openapi.yaml), and [pinned WHIP/WHEP server](https://github.com/bluenviron/mediamtx/blob/v1.21.2/internal/servers/webrtc/http_server.go). The current API uses `/v3/webrtc/sessions/...`; its wildcard ETag is preserved verbatim.

Pending: real browser decoding/rendering, media-session revocation during a live stream, upload/latency measurements, Discord exclusion, sustained NVENC activity, and external-network acceptance.
