# JKiller OBS extension

The M2 candidate enables explicit `Off`, `Application(process identity)` and `SystemOutput(render endpoint ID)` modes. Application capture includes children; strict isolation is deferred. System output is explicit monitor-sharing audio and includes all playback on the fixed selected render endpoint, potentially Discord or external browser playback. It never selects a microphone or silently follows another endpoint. No mode fallback is performed on source loss. In-app viewing uses a native playback mute lease during system sharing.

This module uses OBS 32.2.2's public source and vendor APIs. Application loopback is bound to the original process handle; endpoint loopback is bound to a concrete render endpoint. Both preserve the 200 ms drop-oldest PCM queue. There is no audio-input-device implementation. Each broadcaster shares one H.264/Opus pipeline across its viewers.

M3 supports typed NVENC, AMD AMF H.264 and explicitly consented x264 preparation. AMF uses the bundled obs-ffmpeg module and the driver's runtime, with speed/CBR/one-second-keyframe/no-B-frame/repeated-header settings and disabled pre-analysis/pre-encode/VBAQ. Initialized AMF does not prove texture-only transfer: OBS may internally reroute to frame-upload AMF while retaining its public ID/caps. Diagnostics leave the transfer route unverified. Real hardware/media/game-impact validation is separate.

Noninteractive verification: `pwsh -File scripts/Build-Native.ps1 -Plugin -Test -NonInteractive -NoInstall`. Device-free Pipeline tests compile the actual pipeline against test OBS adapters. This compiles without installing; never replace an active helper's plugin. Managed-only startup cannot install native changes.

Build with `pwsh -File scripts/Build-Native.ps1 -Plugin -Test` from the repository root after setup. Only the application-owned OBS runtime is modified. The SDK headers come from the official hash-verified source release; Windows-incompatible Unix formatting-helper links are excluded during extraction. Import libraries are generated from the pinned official DLL exports.

The extension is distributed under GPL-2.0-or-later, consistent with its OBS API dependency. Preserve the upstream OBS/obs-websocket notices and provide corresponding source and build scripts with distributed binaries. The official helper runtime remains separately attributable to the OBS Project.

Unit safety tests do not prove Discord exclusion or streaming performance. The synthetic-tone integration test proves only that selected-process audio can be received and stopped on the test machine.
