# Owned capture instrumentation

`libobs-winrt` is rebuilt from OBS Studio 32.2.2's official source archive, under GPL-2.0-or-later. The archive SHA-256 and resulting patch/binary hashes are recorded by `scripts/Build-InstrumentedObs.ps1`. The upstream license text is preserved here.

The patch adds accepted GPU-texture sequence/monotonic timestamp queries and requests a 4 ms WGC minimum update interval when that OS property is available, with original/applied readback. A bounded local environment override supports reproduction of the 16 ms default capture-throttle experiment. This hint is not a promised capture rate. It retains the upstream frame pool, GPU texture copies and rendering implementation. It does not add image readback or change the encoder. Other OBS components remain the verified upstream binaries. Source and patch/build instructions must accompany any distributed modified binary as required by the upstream license.

The planned encoder patch is unnecessary: OBS 32.2.2 already supplies matched CTS/FER/FERC timing to its maintained `obs_output_add_packet_callback`. The application uses that API, rather than maintaining duplicate hooks in raw/GPU encoder internals. Its output callback residence includes interleaving and is labelled accordingly. GPU-kernel timing remains unavailable.
