# Milestone 3 handoff

Started 2026-10-18 from `main` at `9c279a9`. The owner confirmed all remaining M2 functional acceptance cases passed; see [M2 acceptance](validation/milestone-2-results.md). Preserve earlier automated failures, package hashes and measurement limits.

## First approved slice

The owner approved this bounded design in chat:

- Apply navy `#0E182A` to the native application window, with neutral input backgrounds. Explicitly apply the window style to `MainWindow`; an implicit base `Window` style does not apply automatically to this derived window class. Keep readable foreground, caret, focus and disabled states.
- Include the broadcaster's cursor by default in both native window and monitor capture. The pointer is part of the video. Preserve the viewer's separate fullscreen cursor/bar hiding behavior. No cursor toggle is required in this slice.
- Bound the website volume track to 80 CSS pixels, allowing it to shrink in narrow cards. Keep the percentage where space permits, independent per-stream volume, keyboard control and fullscreen behavior.
- Make verification, commit, push and a GitHub pull request the default delivery workflow in `AGENTS.md`, with private/generated runtime content excluded.

Grid/focus, independent volume and overlay controls already shipped during M2. Do not redo them. Shared previews and further subscription/layout refinements remain future M3 scope to select with the owner; this first slice does not complete the entire milestone.

The owner also asked about browser publishing, clone-based self-hosting and non-NVIDIA encoding. [The feasibility notes](setup/publishing-and-compatibility.md) explain the existing boundaries and potential work. Those questions do not authorize adding a browser publisher, transcoding subsystem or new hardware encoder in this slice. No viewer quota or bandwidth-admission system is introduced.

## Verification and delivery

Use `./scripts/Verify.ps1 -Mode CodeOnly` for integrated correctness. It compiles the matching native plugin without installing it or starting media/capture. Inspect actual WPF resource application and responsive browser layout separately. Keep raw artifacts under ignored `.artifacts/`.

The first slice passed integrated correctness and actual WPF/browser layout checks; see [the verification record](validation/2026-10-18-m3-interface.md). This is not a claim that the entire M3 milestone is complete.

After changing native code, `Start-Broadcaster.ps1` alone cannot install the updated cursor behavior. Stop the owned broadcaster and rebuild/install the matching plugin, or build a fresh portable ZIP. Do not overwrite an active helper or present old packages as M3 builds.
