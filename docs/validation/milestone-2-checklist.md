# M2 functional acceptance

Record UTC date, source revision, package SHA-256, OS/browser/encoder, requested/applied settings, capture mode and evidence artifact directory for every run. Mark each item PASS, FAIL or PENDING with a concrete reason. M1 results and its strict-exclusion/benchmark validator are historical.

Prepare a fresh matching ZIP and extract it twice into distinct writable roots. Do not copy the repository/cache, reuse personal OBS or remove supplied roots. Stop the owned broadcaster before installing a rebuilt native plugin. The runner locks each root, uses a shared local host, separate helper profiles/identities/control files, scoped environment variables and only cleans its own process identities.

```powershell
./scripts/Build-Native.ps1 -Plugin
./scripts/Package-Portable.ps1 -Version milestone-2-candidate-UNIQUE
./scripts/Test-ConcurrentBroadcasting.ps1 -InstanceRootA 'D:\absolute\fresh-A' -InstanceRootB 'D:\absolute\fresh-B'
# Separate Off run:
./scripts/Test-ConcurrentBroadcasting.ps1 -InstanceRootA 'D:\absolute\fresh-A' -InstanceRootB 'D:\absolute\fresh-B' -VideoOnly
# Explicit system-output run (capture only safe synthetic desktop content):
./scripts/Test-ConcurrentBroadcasting.ps1 -InstanceRootA 'D:\absolute\fresh-A' -InstanceRootB 'D:\absolute\fresh-B' -SystemEndpointId '<concrete endpoint ID>' -MonitorSourceId '<exact monitor source ID>'
```

The default is Application audio from the owned synthetic fixture. Never infer monitor/endpoint IDs, silently change capture mode or bypass an ownership lock. The runner builds probes and starts local services/fixtures; it is excluded from CodeOnly. It uses the existing Edge Playwright protocol configuration with screenshots, traces and video recording disabled. Numeric transport evidence is saved under `.artifacts/concurrent/<unique-run>`; invitation credentials are not report content.

| Functional check | Evidence required |
|---|---|
| Two concurrent publishers | Distinct selected cards/stream IDs, decoded frames and Opus packets for each |
| Independent start/stop/restart | Stop/restart A, keep B's element/media/peer and advancing frames/audio packets |
| Off | No selected capture route; decoded silent Opus does not prove audio exclusion |
| Application | Intended synthetic process audio delivery; process loss stops route without fallback |
| System output | Explicit monitor/endpoint; selected device delivery, endpoint loss and default-switch do not retarget |
| Viewer sound | Enable/mute toggles plus independent human listening result |
| Synthetic tone detection | Separate detector output, if run; counters/energy alone are insufficient |
| Broadcasting while watching | Native session/publication remains live while WebView receives frames |
| System feedback | Native CoreWebView2 mute and disabled sound control; restoration after final lease |
| Fullscreen | Browser geometry, immediate hide/cursor, movement reveal, two-second idle hide, keyboard access, Escape; same video/media/peer and advancing frames |
| Native fullscreen | Native window style/state restored; video/media continues |
| Native script failure | Guidance/reload has usable space, preserves native mute and media until explicit reload |
| Recovery | Source/endpoint loss, restart and renderer recovery; record each separately |
| Human audible confirmation | Listener confirms intended sound in browser and WebView for Application/System/Off; PENDING while unavailable |

No fixed room quota, replacement reader cap, admission system, per-viewer encoder, microphone, eight-viewer gate or long benchmark is part of M2. No maximum capacity is established by these small checks. Keep acceptance pending while required functional checks are missing.
