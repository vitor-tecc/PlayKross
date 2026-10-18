# M3 first interface/cursor slice

2026-10-18, branch `codex/m3-viewing-refinements`, based on `9c279a9`. Bounded design approved by the owner in chat. M2 acceptance is separately recorded from the owner's confirmation that all remaining functional cases were tested and work as intended.

## Changes

- The derived WPF `MainWindow` explicitly uses the named window style: navy `#0E182A` background and readable blue text. Text/dropdown inputs use a separate neutral `#252B35` surface and light text. The generic `Window` style remains available to ordinary viewer windows.
- Both pinned native WGC initialization calls now request cursor inclusion. This affects the pointer transmitted inside window/display video; it does not change the viewer's local fullscreen cursor hiding.
- Website volume tracks are capped at 80 CSS pixels, can shrink, and retain independent playback volume, keyboard operation and percentage display where space permits.
- `AGENTS.md` now defaults repository work to verified commits, pushes and pull requests. Roadmap/handoffs record M2 acceptance and M3 entry. Feasibility answers are in [publishing and compatibility](../setup/publishing-and-compatibility.md).

## Verification

- `./scripts/Verify.ps1 -Mode CodeOnly`: **PASS**, 270 managed tests (100 core / 72 host / 98 Windows), 53 browser tests, 37 native noninteractive checks. Application/probe/native and browser production builds, setup/portable boundaries, probe option validation and all three existing headless UI checks passed. The initial sandbox run failed NuGet vulnerability-feed access (`NU1900`); the network-enabled rerun passed without changing dependency/audit policy. Native plugin compiled but was not installed. Raw log: ignored `.artifacts/m3-codeonly.log`.
- A temporary headless WPF probe loaded the compiled application's actual resources and `MainWindow`. Resolved background was `#FF0E182A`, and the room-link input was `#FF252B35`. Clearing the explicit style reproduced the old implicit-style white background (`#FFFFFFFF`), confirming the cause. The window content was rendered offscreen and inspected. No window was shown; no capture/media started. Ignored output: `.artifacts/m3-ui-probe/theme.log` and `native-theme.png`.
- Reused the existing temporary Edge/Playwright layout probe with a mocked three-stream room: desktop grid/focus, 900/700/390/320px responsive widths, no horizontal overflow or clipped controls, independent volume reaching the video element, keyboard focus control, error/Retry diagnostics, and 640x240 fullscreen diagnostics passed. Desktop/mobile rendered output was visually inspected. Mocked data/layout evidence does not establish real-media delivery. Ignored artifacts: `.artifacts/m3-ui-probe/`.
- Independent read-only GPT-6.1 Sol review found no critical, important or minor findings. It checked the pinned WGC header's first `BOOL cursor` parameter, WPF style application, responsive volume controls and documentation scope. `git diff --check` passed.

No fresh transmitted-cursor visual check was run. Source and plugin compilation establish the default setting, not pointer appearance at every scale/edge or after live restart. A matching native rebuild/install or fresh portable package is required before testing it; old ZIPs are unchanged. The native live audio/process fixtures were excluded from CodeOnly as intended. No services, display/device settings or active broadcaster processes were changed.
