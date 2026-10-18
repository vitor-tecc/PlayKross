# Milestone 3 handoff

Started 2026-10-18 from `main` at `9c279a9`. The owner confirmed all remaining M2 functional acceptance cases passed; see [M2 acceptance](validation/milestone-2-results.md). Preserve earlier automated failures, package hashes and measurement limits.

## Revised main goal — AMD AMF H.264

The owner scrapped shared lower-quality previews and made **AMD AMF H.264 hardware broadcasting compatibility** M3's main goal. Intel Quick Sync is deferred: none of the current friend group uses Intel GPUs. This supersedes the earlier viewing-refinement/preview direction and historical M3 preview requirements. No preview implementation exists to remove; the change removes planned work.

The priority is minimal CPU/GPU workload on a broadcaster who may also be gaming and hosting the room. Preserve one shared H.264/Opus encoding pipeline per broadcaster, existing NVIDIA NVENC support and explicitly enabled CPU fallback. Do not introduce an additional preview encode, server transcoding worker, per-viewer encoding or automatic CPU fallback. Hardware encoding does not guarantee zero game impact; report measured effects where evidence exists.

M3's remaining work is to implement AMD hardware initialization/configuration, accurately report the active encoder, provide clear unsupported-driver/hardware failures, and validate the packaged path on real AMD hardware. Preserve browser/WebView playback, audio modes, start/stop/restart and concurrent broadcasting. Check game/frame-time impact on the AMD test system without inventing a universal numerical performance guarantee. Code-only tests or an AMD encoder listed in capabilities do not establish working AMD broadcasting. AMD implementation and live hardware acceptance are **pending**; this scope update does not claim they are complete.

Further grid/subscription polish, browser publishing, permanent hosting and Intel support are not M3 acceptance requirements under this revision. [Publishing and compatibility notes](setup/publishing-and-compatibility.md) retain the earlier feasibility context and identify the new AMD priority.

## Initial interface/cursor slice

The owner approved this bounded design in chat:

- Apply navy `#0E182A` to the native application window, with neutral input backgrounds. Explicitly apply the window style to `MainWindow`; an implicit base `Window` style does not apply automatically to this derived window class. Keep readable foreground, caret, focus and disabled states.
- Include the broadcaster's cursor by default in both native window and monitor capture. The pointer is part of the video. Preserve the viewer's separate fullscreen cursor/bar hiding behavior. No cursor toggle is required in this slice.
- Bound the website volume track to 80 CSS pixels, allowing it to shrink in narrow cards. Keep the percentage where space permits, independent per-stream volume, keyboard control and fullscreen behavior.
- Make verification, commit, push and a GitHub pull request the default delivery workflow in `AGENTS.md`, with private/generated runtime content excluded.

Grid/focus, independent volume and overlay controls already shipped during M2. Do not redo them. The initial interface/cursor slice remains delivered work; it does not complete the revised AMD milestone.

The earlier questions about browser publishing, clone-based self-hosting and non-NVIDIA encoding were initially feasibility-only. AMD AMF H.264 is now the explicitly requested M3 goal; browser publishing and Intel remain separate. No viewer quota or bandwidth-admission system is introduced.

## Verification and delivery

Use `./scripts/Verify.ps1 -Mode CodeOnly` for integrated correctness. It compiles the matching native plugin without installing it or starting media/capture. Inspect actual WPF resource application and responsive browser layout separately. Keep raw artifacts under ignored `.artifacts/`.

The first slice passed integrated correctness and actual WPF/browser layout checks; see [the verification record](validation/2026-10-18-m3-interface.md). This is not a claim that the entire M3 milestone is complete.

After changing native code, `Start-Broadcaster.ps1` alone cannot install the updated cursor behavior. Stop the owned broadcaster and rebuild/install the matching plugin, or build a fresh portable ZIP. Do not overwrite an active helper or present old packages as M3 builds.
