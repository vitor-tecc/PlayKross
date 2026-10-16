# Windows in-app viewer

Join in the native broadcaster, then click **Watch in app**. A separate room window lets you keep capture controls accessible while watching. It reuses your room session through a same-origin HttpOnly cookie in an application-owned, per-origin WebView2 profile protected with user/SYSTEM directory ACLs. There is no account and no extra nickname/session slot. Microsoft Evergreen WebView2 must be installed; the SDK is pinned to 1.0.4258.31, and the installed runtime updates independently.

The page has no native command bridge: host objects and web messages are disabled. Main-frame and subframe navigation are restricted to the joined origin; popups/downloads are blocked, certificate errors cancel, and media permissions including microphone/camera are denied. Use the native app's diagnostic export button when embedded downloads are blocked. Page capture commands, process IDs, paths and native handles are not accepted from the room page.

A failed viewer renderer/browser shows **Reload viewer** and recreates the WebView. Native capture and its isolated helper continue independently. Incoming playback is not a native audio source. Windows application audio uses the explicitly selected game's process tree; the app, WebView2, Discord, browsers and unknown/shared hosts remain audio-ineligible. Sharing their pixels is video-only. Real audible feedback/Discord exclusion still requires acceptance testing.

Actual test `./scripts/Test-InAppViewing.ps1` on 2026-10-13 passed synthetic native 1080p publication plus real WebView2 decoding, live nickname change, fullscreen/return with continuing playback, cookie/native session preservation, bridge-disabled settings, canceled off-origin navigation and a deliberate renderer crash followed by recovery while publication continued. Runtime 155.0.4283.45. No real desktop/audio recording was saved. This is integration validation, not multi-broadcaster performance; viewing another broadcaster while publishing awaits that milestone.

Navigation/popup policy tests were observed RED then GREEN. Existing browser fullscreen/rename lifecycle tests remain in `web/tests/StreamPlayer.test.tsx` and actual media tests in `web/tests/protocol`; duplicating them under a second Playwright directory would not improve coverage. Runtime behaviors are checked by the application-owned WPF/WebView2 integration probe.

Security choices follow [Microsoft's WebView2 guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security).
