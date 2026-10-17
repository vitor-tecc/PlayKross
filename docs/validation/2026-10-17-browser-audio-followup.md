# Browser audio follow-up and M2 closure

The user reports that the playback/interface features work as intended in practical testing, with one newly reported issue: selecting a browser for Application audio was refused. This is functional feedback on the tested features, not evidence that every item in the earlier acceptance matrix was exercised.

## Browser selection correction

The managed `AudioPolicy` and native `AllowsApplicationAudioImage` independently denied browser/shared-host executables under the older strict-isolation rule. Both now permit explicit selection of browsers and shared hosts. Application capture remains bound to the selected process identity and its children; it does not isolate individual tabs. The selector explains that other tabs, web Discord and room playback may be included. Direct selection of the Discord desktop app, the broadcaster and its own playback stays excluded; native controller-tree/liveness checks remain active. Audio stays opt-in, and there is no automatic system-output fallback.

System output remains mixed device capture. This correction does not add per-process subtraction of Discord.exe from a system mix, or guarantee exclusion of future child processes. Strict isolation remains deferred under the current M2 requirements.

Regression evidence: before the production correction, 14 managed audio-policy cases and the native input-policy check failed with the intended browser/shared-host eligibility assertions. The first integrated run also identified two UI tests enforcing the obsolete denial; their expectations now cover browser selection reaching the media request and retained own-playback exclusion. Read-only review found no important issue in the integration or audio correction. Logs are ignored under `.artifacts/m2-browser-*.log`.

The baseline CodeOnly run passed managed/browser/native tests but stopped in `ConcurrentProbeChecks.ps1`: its privilege fallback matched an English error message on a Portuguese Windows installation. A structured error probe confirmed `NewItemSymbolicLinkElevationRequired`; the fallback now matches that ID and still propagates unrelated errors. It does not grant symlink privileges or change Windows settings.

Final `Verify.ps1 -Mode CodeOnly`: PASS, 270 managed tests, 53 browser tests, 37 native noninteractive checks, application/browser/probe builds, setup/portable boundaries and all three headless UI checks. File-symlink cases used the existing scoped metadata fallback; real directory junctions were exercised. No media, tone fixture or browser was launched, and the rebuilt plugin was not installed into the development runtime. Actual browser-audio audibility remains a retest on a fresh matching package.

## Cursor visibility answer

`native/jk-obs/src/ObsVideoSource.cpp` passes `FALSE` as the cursor argument to both `winrt_capture_init_window` and `winrt_capture_init_monitor`. The pinned OBS header identifies that first argument as `BOOL cursor`; its WGC implementation forwards the value into `GraphicsCaptureSession.IsCursorCaptureEnabled`. See [Microsoft's capture-session API](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession).

The minimal change is to pass `TRUE` for both capture modes, rebuild the owned native plugin and package matching binaries, then visually verify the received pointer in browser and in-app viewing for window/display capture, scaling, edges and restart. No server, codec or viewer-overlay protocol change is required because the pointer is composited into the video. A future user toggle would additionally need settings/UI plumbing. No cursor behavior was changed in this follow-up; the user asked what changes are needed. Viewer fullscreen's idle cursor hiding is a separate local behavior and should remain intact.

## Remaining M2 acceptance

- Retest browser Application audio audibly using the corrected matching package. Preserve the user's practical sign-off on the existing features.
- Close the still-unrecorded functional cases from the [acceptance checklist](milestone-2-checklist.md): audible Application/System/Off in browser and WebView, full System-output publication and native playback mute/restoration, source/endpoint loss and pinned-device behavior through a default-device change, and manually operated browser Escape. Existing two-publication/restart and native-viewing evidence remains valid for its recorded revision. Do not repeat already covered cases merely to recreate history.
- Record which of those cases the user's practical test covered; broad feedback alone does not establish device-loss or default-switch testing. Record final revision/package and mark M2 accepted when required cases pass or the user explicitly accepts a remaining limitation.

Cursor visibility is a small requested follow-up question, not an existing M2 gate. Strict Discord isolation, startup stutter, external numerical characterization, eight-viewer testing and large-group performance are not M2 closure requirements. No fixed viewer quota or bandwidth-admission system is added.
