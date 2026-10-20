# Milestone 3 handoff

Started 2026-10-18 from `main` at `9c279a9`. The owner confirmed all remaining M2 functional acceptance cases passed; see [M2 acceptance](validation/milestone-2-results.md). Preserve earlier automated failures, package hashes and measurement limits.

The owner approved the AMD-first/browser-second design direction and the [written M3 specification](superpowers/specs/2026-10-18-milestone-3-design.md) in chat on 2026-10-18. The linked [native AMD plan](superpowers/plans/2026-10-18-m3-amd-broadcasting.md) and [browser broadcasting plan](superpowers/plans/2026-10-18-m3-browser-broadcasting.md) were approved for native execution on 2026-10-18. Native AMD and browser implementations now pass code-only checks; final review and real AMD/browser acceptance are tracked separately.

## Revised main goal — AMD AMF H.264

The owner scrapped shared lower-quality previews and made **AMD AMF H.264 hardware broadcasting compatibility** M3's main goal. Intel Quick Sync is deferred: none of the current friend group uses Intel GPUs. This supersedes the earlier viewing-refinement/preview direction and historical M3 preview requirements. No preview implementation exists to remove; the change removes planned work.

The priority is minimal CPU/GPU workload on a broadcaster who may also be gaming and hosting the room. Preserve one shared H.264/Opus encoding pipeline per broadcaster, existing NVIDIA NVENC support and explicitly enabled CPU fallback. Do not introduce an additional preview encode, server transcoding worker, per-viewer encoding or automatic CPU fallback. Hardware encoding does not guarantee zero game impact; report measured effects where evidence exists.

The AMD work is to implement hardware initialization/configuration, accurately report the active encoder, provide clear unsupported-driver/hardware failures, and validate the packaged path on real AMD hardware. Preserve browser/WebView playback, audio modes, start/stop/restart and concurrent broadcasting. Check game/frame-time impact on the AMD test system without inventing a universal numerical performance guarantee. Code-only tests or an AMD encoder listed in capabilities do not establish working AMD broadcasting. AMD initialization/configuration and managed selection are implemented and code-only verified; live hardware acceptance remains **pending**. See [AMD verification](validation/2026-10-18-m3-amd.md).

Further grid/subscription polish, permanent hosting and Intel support are not M3 acceptance requirements under this revision. [Publishing and compatibility notes](setup/publishing-and-compatibility.md) retain implementation context for AMD and the browser feature below.

## Additional M3 feature — start broadcasting in the browser

The owner added the ability to start a broadcast directly from the website using the browser's own capture method (`getDisplayMedia()`), alongside native AMD compatibility. A browser broadcaster must not need the PlayKross desktop app or its OBS helper. AMD AMF remains the main M3 goal; this is an additional deliverable. Browser implementation is code-only verified; real-browser/audio acceptance remains **pending**. See [browser verification](validation/2026-10-18-m3-browser.md).

- Provide explicit Start/Stop sharing controls and the browser's permission/source chooser for screen, window or tab capture where supported. Require a secure context (HTTPS or suitable localhost development), handle permission denial/cancellation, and never start capture automatically.
- Publish the browser-captured tracks through the existing authenticated room/WHIP media flow. Preserve ownership, one publication per session, publication renewal, stop/restart and concurrent native/browser broadcasters. Reuse the single-feed distribution approach; do not add previews, simulcast renditions or per-viewer encoding.
- Offer opt-in captured audio only where the browser/OS/source supports it, explain when audio is unavailable, and preserve the no-microphone scope. Browser capture does not promise the native app's per-process audio selection/isolation. Account for room-playback feedback when broadcasting while watching.
- Handle the browser's own Stop sharing action, ended tracks, failures, page departure and publication cleanup. Failed or cancelled starts must release owned capture/media resources and any publication reservation.
- Validate actual video and available audio in supported browsers, native/browser coexistence, broadcasting while watching, restart and cleanup. Record browser/OS limitations and observed CPU/GPU/game impact. Browser-selected encoding does not guarantee AMF/NVENC use or zero game interference; native AMD encoder settings do not control the browser's encoder.

See [browser publishing implementation notes](setup/publishing-and-compatibility.md#broadcasting-entirely-from-the-website). The website now contains the browser capture/WHIP flow; code-only and mocked UI checks do not establish live browser publication acceptance.

Follow-up: the [video-only playback regression](validation/2026-10-18-m3-browser-playback.md) fixes WHEP POST 400 for browser publications without audio by accepting MediaMTX's PCMU placeholder in the viewer. Three real synthetic Edge protocol cases and a Firefox 158 receive check passed; actual display capture/audio and the remaining M3 live acceptance checks are still pending.

## Initial interface/cursor slice

The owner approved this bounded design in chat:

- Apply navy `#0E182A` to the native application window, with neutral input backgrounds. Explicitly apply the window style to `MainWindow`; an implicit base `Window` style does not apply automatically to this derived window class. Keep readable foreground, caret, focus and disabled states.
- Include the broadcaster's cursor by default in both native window and monitor capture. The pointer is part of the video. Preserve the viewer's separate fullscreen cursor/bar hiding behavior. No cursor toggle is required in this slice.
- Bound the website volume track to 80 CSS pixels, allowing it to shrink in narrow cards. Keep the percentage where space permits, independent per-stream volume, keyboard control and fullscreen behavior.
- Make verification, commit, push and a GitHub pull request the default delivery workflow in `AGENTS.md`, with private/generated runtime content excluded.

Grid/focus, independent volume and overlay controls already shipped during M2. Do not redo them. The initial interface/cursor slice remains delivered work; it does not complete the revised AMD/browser milestone.

The earlier questions about browser publishing, clone-based self-hosting and non-NVIDIA encoding were initially feasibility-only. AMD AMF H.264 is now M3's main goal and browser publishing is an explicitly requested additional M3 feature. Intel remains deferred. No viewer quota or bandwidth-admission system is introduced.

## Verification and delivery

Audio selection update, 2026-10-20: at the owner's request, native application audio no longer excludes any executable name or the controller/helper/playback process tree. Browser hosts, Discord and PlayKross can be explicitly selected. Audio stays opt-in, with exact process binding, selection generations and publication cleanup retained. This supersedes earlier application eligibility exclusions; historical validation retains its original revision limits. Rebuild both the managed app and native plugin (or use a fresh portable package); an old plugin still enforces its compiled restrictions.

Use `./scripts/Verify.ps1 -Mode CodeOnly` for integrated correctness. It compiles the matching native plugin without installing it or starting media/capture. Inspect actual WPF resource application and responsive browser layout separately. Keep raw artifacts under ignored `.artifacts/`.

The first slice passed integrated correctness and actual WPF/browser layout checks; see [the verification record](validation/2026-10-18-m3-interface.md). This is not a claim that the entire M3 milestone is complete.

After changing native code, `Start-Broadcaster.ps1` alone cannot install the updated cursor behavior. Stop the owned broadcaster and rebuild/install the matching plugin, or build a fresh portable ZIP. Do not overwrite an active helper or present old packages as M3 builds.
