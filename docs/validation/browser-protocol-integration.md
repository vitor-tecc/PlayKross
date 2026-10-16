# One-viewer protocol checkpoint

Build the native plugin/fixture (`pwsh -File scripts/Build-Native.ps1 -Plugin`), the owned-engine probe (`.tools/dotnet/dotnet.exe build tools/acceptance/OwnedEngineProbe`), and the viewer (`npm --prefix web ci`, then `npm --prefix web run build`). Run `pwsh -File scripts/Test-LocalProtocol.ps1` for the real media check. It needs installed Microsoft Edge and Node 24+, starts only local services, displays a synthetic native motion window with a quiet 997 Hz tone, and cleans up its own processes. It captures no desktop/gameplay/Discord content.

Actual test machine results:

- Native Windows Graphics Capture -> one initialized NVENC H.264 encoder and Opus encoder -> authenticated WHIP -> MediaMTX -> WHEP -> real Edge 155.0.4283.45.
- Requested/applied output: 1280×720, 30 FPS, 6 Mbps. Browser-observed resolution: 1280×720, codec `video/H264`; received audio codec: `audio/opus`.
- Latest short sample: 37 decoded frames, 37 total video-playback frames, 1,399 received video packets, 105 received audio packets, nonzero audio energy 0.00015377698258920946. This sample proves actual negotiation/decoding and non-silent synthetic audio. These counts do not establish a steady-state FPS, latency, bitrate, or smoothness result.
- Actual fullscreen entry/return preserves playback. Ownership revocation removes the browser player, closes its receiving peer, and stops the native output before local engine Stop.
- `Test-ControllerExit.ps1` and `Test-ControllerExit.ps1 -DuringPreparation` verify that loss of the original controller terminates its owned helper. A Windows job is assigned atomically during helper process creation, with kill-on-close and no inherited job handle, so startup and blocked shutdown cannot leave helper descendants behind.
- Automated correctness: 6 frontend tests, 29 Core tests, 21 Windows tests, 31 Host tests and 15 native tests at this checkpoint. Mocked media tests cover cancellation, late resources, retries, fullscreen, rename and source cleanup; they do not establish streaming performance.

Real testing found two integration defects: writing even an empty body for MediaMTX's 204 response caused a Kestrel error, and OBS's default automatic reconnect kept retrying a revoked bearer token. The proxy now skips bodies for no-content responses; the native output disables internal reconnect, leaving fresh-grant recovery to the typed controller. The same real regression check passed after both fixes.

Window content uses aspect-preserving bounds so changing output resolution does not crop or stretch the selected source. Browser sessions restore their authenticated identity without exposing the cookie credential. Reopening the same invitation preserves ownership; an explicit different invitation selects its intended room. Video sessions use receive-only tracks and request no camera or microphone permission.

The job assignment uses Microsoft's documented [process creation job-list attribute](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute) and [job-object lifecycle](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

Still pending: usable Windows control UI, 1080p60 sustained measurement, accurate capture/encode timing, actual hardware activity counters, prohibited-audio correlation/Discord checks, other browsers/operating systems, external networks, and four/eight viewers. This short local synthetic test cannot accept milestone 1 performance or justify advancing the scaling milestones.
