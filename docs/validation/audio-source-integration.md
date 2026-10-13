# Native application-audio integration evidence

Date: 2026-10-13. Environment: Windows 10.0.26200.0, OBS 32.2.2 in a project-owned portable directory, Visual Studio C++ Build Tools 18, Windows SDK 10.0.26100.0.

## Measured

- Native process binding rejects reused identities, observed exit, access denial, and reactivation after sealing; an actual spawned child-process exit was tested.
- The bounded PCM queue drops oldest audio above 200 ms. Native route tests reject input-device/system audio, unapproved names/generations, and monitoring routes.
- A real Windows process-loopback test captured a quiet synthetic 997 Hz tone from the selected helper: 191,040 interleaved samples, RMS 0.0148187. This was actual OS capture, not mocked PCM.
- The built extension loaded into the owned OBS helper and registered `jk_process_audio`. Private named-pipe control reached the maintained OBS request API with the network control server disabled.
- In the actual OBS integration, the selected sibling application's audio source captured and emitted 44,640 stereo frames during the approximately 0.93-second observation. `armed`, `audioRunning`, and `graphSafe` were true; `audioError` was empty.
- The selected process's natural exit muted capture and disarmed publication. Explicit disarm also left capture stopped.
- A controller-child tone fixture exposed an own-tree acceptance bug. After native ancestry validation, the same real-helper regression returned `armed=false`; room/controller process-tree audio was rejected before capture activation.
- Real named-pipe tests cover authorized roundtrips, wrong-owner rejection, and oversized-frame rejection. Remote clients are prohibited by the Windows pipe creation flag; this is not a public WebSocket service.

## Problems diagnosed during integration

- SDK extraction was blocked by an owned .NET build-server file lock; shutdown and retry resolved it.
- The initial OBS source tar extraction rejected Unix formatting-helper links. Excluding those helper links retained all required headers/source files.
- An import-definition header joined `LIBRARY` and `EXPORTS` on one line; the actual Microsoft linker rejection identified the formatting error.
- Native control initially became available before OBS's request API finished initialization. Starting it after the frontend finished-loading event resolved the real startup regression.
- OBS's `obs_data_t` cannot preserve primitive JSON arrays. The real input-kind list regression caught that loss; the adapter now uses hash-pinned nlohmann/json 3.12.0 and the maintained raw-JSON request API.

## Not established by these results

No gameplay capture, video encoding, browser playback, streaming latency, eight-viewer throughput, or external-network performance has been measured. Actual NVENC activation is unverified. These audio tests do not prove real Discord desktop/browser exclusion under the full concurrent-source test; that remains a milestone acceptance check. The retained raw logs are in the ignored Superpowers workspace and contain no recorded desktop or private call audio.

The private helper always starts with system/input-device audio disabled. Desktop lifecycle code must also start from a clean owned scene, so previously serialized application sources cannot begin capture at application launch. Audio eligibility stays conservative: unverified shared hosts remain silent.
