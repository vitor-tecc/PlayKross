# Owned engine checkpoint

Run `powershell -File scripts/Build-Native.ps1 -Plugin -Test`, then `powershell -File scripts/Test-OwnedEngine.ps1 -SyntheticCapture` from the repository root. The second command displays a disposable synthetic motion window because Windows capture requires a visible source. It records no desktop content. It closes only its own helper and fixture processes.

Measured on this Windows machine with OBS 32.2.2 and RTX 5070:

- Private pipe handshake checks the server's PID and original process creation identity. The native server checks the controller's identity and prohibits remote clients.
- OBS's actual capability list contains `obs_nvenc_h264_tex`, `ffmpeg_opus`, `whip_custom`, and `jk_bound_capture`.
- Actual encoder initialization succeeds at 1920×1080/60, 15,000 kbps, H.264, zero B-frames and disabled lookahead. OBS logs independently report NVENC initialization and 128 kbps stereo Opus. This is initialization evidence, not sustained GPU encoder activity or playback performance.
- The synthetic native window prepares through Windows Graphics Capture and D3D11 textures at requested output 1280×720/30, 6 Mbps. The wrapper creates one original capture item and never searches by title. Renaming that window preserves capture; destroying it and creating an identical-title window in the same process invalidates the original capture and publication.
- Seventeen existing personal OBS configuration files retain identical SHA256 hashes before and after the probe. The supervisor launches only the project-owned portable runtime, seeds an empty scene with all device audio disabled, removes inherited extra-plugin paths, and checks its pinned plugin inventory.
- Native tests: 15 passing. .NET tests at this checkpoint: 29 Core and 20 Windows passing. They cover bounded IPC, cancellation, mismatched identities, source lifetime, settings readback, explicit CPU fallback, and late Start versus Stop.

The initial synthetic capture attempt initialized WGC on the pipe thread: no frames arrived and dimensions stayed zero, with no initialization error. Pinned OBS source showed its WGC frame pool uses a dispatcher and its graphics thread pumps messages. Moving initialization to the graphics-thread source tick made the same integration test pass. No architecture change was needed.

Media credentials are passed to a private OBS service held in memory; this service is not installed as the frontend's persistent streaming service. Portable configuration inherits an ACL restricted to the current user and SYSTEM. No broadcast credential needs storage at this stage. Anonymous session credentials will use DPAPI in the desktop session vault.

Still pending: end-to-end WHIP/WHEP playback, sustained encoding activity, capture/encoding timing instrumentation, Discord isolation during concurrent playback, ten-minute motion/gameplay acceptance, external networking and other operating systems. This checkpoint cannot satisfy those gates.
