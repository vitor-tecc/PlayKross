# JKiller OBS extension

This module uses OBS 32.2.2's public source and vendor APIs. Its application audio source uses Windows process-loopback inclusion bound to the original process handle. It has no endpoint-loopback or audio-input-device implementation.

Build with `pwsh -File scripts/Build-Native.ps1 -Plugin -Test` from the repository root after setup. Only the application-owned OBS runtime is modified. The SDK headers come from the official hash-verified source release; Windows-incompatible Unix formatting-helper links are excluded during extraction. Import libraries are generated from the pinned official DLL exports.

The extension is distributed under GPL-2.0-or-later, consistent with its OBS API dependency. Preserve the upstream OBS/obs-websocket notices and provide corresponding source and build scripts with distributed binaries. The official helper runtime remains separately attributable to the OBS Project.

Unit safety tests do not prove Discord exclusion or streaming performance. The synthetic-tone integration test proves only that selected-process audio can be received and stopped on the test machine.
