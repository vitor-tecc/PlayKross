# Workspace size and safe cleanup

The initial inventory on 2026-10-14 was about 3,721 MiB (3.63 GiB). Git was only 0.38 MiB: local SDKs, package caches, runtime binaries and compilation output explain the large directory, rather than committed source or video recordings.

| Directory | Initial MiB | Purpose |
|---|---:|---|
| `.tools` | 1,879.73 | Local .NET SDK, verified download archives, NuGet/npm packages and source/header dependencies |
| `.artifacts` | 764.52 | Native build output plus small numeric test evidence; one C++ precompiled header was 490.31 MiB |
| `.runtime` | 560.41 | Application-owned OBS/MediaMTX and local WebView profile/configuration |
| `tools` | 134.11 | Acceptance probe builds |
| `web` | 126.96 | Mostly installed npm packages |
| `.superpowers` | 79.85 | Progress/review records and previous probe builds |

Preview safe cleanup from PowerShell 7:

```powershell
./scripts/Optimize-Workspace.ps1
```

Stop broadcasts, host/media services and acceptance helpers, then apply:

```powershell
./scripts/Optimize-Workspace.ps1 -Apply
```

The command removes only npm's download cache, redundant SDK/MediaMTX/Caddy archives when their extracted executable exists, and C++ `.pch`, `.obj`, `.ilk`, `.ipdb`, `.iobj` intermediates under `.artifacts`. It checks resolved workspace paths, rejects links/junctions before mutation and refuses cleanup while workspace runtime/test helpers are running. Its preservation and junction checks run in `tests/setup/CleanupChecks.ps1`.

Source, installed SDK/NuGet/npm packages, runtime executables, plugin/capture DLLs, debug symbols, OBS/source archives needed for the patched capture rebuild, manifests, capture provenance, logs, numeric benchmark evidence, session state and Git history are retained. No personal OBS installation or system directories are cleaned.

Missing intermediates regenerate during `Build-Native.ps1` or `Build-InstrumentedObs.ps1`; the next build can be slower and grow the workspace again. A later `Setup-Dev.ps1` can redownload removed archives from pinned upstream URLs and verify their hashes. Removing a cache reduces local disk use; it does not reduce application dependencies or change streaming behavior.

For a small source checkout, use Git rather than copying the entire development directory. Ignored tooling/runtime/build outputs are not pushed to GitHub. Packaging and dependency simplification remain future work.

Applied cleanup on 2026-10-14 removed **1,172.48 MiB** of the selected regenerable files. The measured workspace afterward was **2,450.04 MiB (2.39 GiB)**. The owned capture DLL hash still matched its recorded provenance, and the supplied logo hash matched the source image.
