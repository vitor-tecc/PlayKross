# Native audio source restriction removal

Requested 2026-10-20 after an installed native broadcaster rejected browser audio with the older message that Discord, browser hosts and room playback cannot be audio sources. Baseline: `7e43d5f`, branch `codex/remove-audio-source-restrictions`.

Removed executable-name exclusions and native controller/helper/process-tree exclusions from application audio preparation and routing. Explicitly selected browsers, Discord, PlayKross, OBS and shared hosts are eligible. The native route no longer scans the controller process tree. Audio remains opt-in; valid modes, concrete source identities, process lifetime, selection generation, publication ownership and cleanup still apply. Microphone capture is not added.

Verification:

- Before the fix, the revised managed eligibility regression failed in 10 cases. Native tests failed for unrestricted executable names and routing audio from the controller itself.
- `./scripts/Verify.ps1 -Mode CodeOnly` passed: 295 .NET tests, 98 browser tests, 43 native tests, managed/browser/native builds and all setup checks. Three existing native device/process cases are excluded by CodeOnly.
- Native route coverage accepts the controller process and an unrelated/unavailable controller PID for application audio. Existing tests still verify stale/replaced identity rejection, source loss, audio Off, system endpoint ownership, cleanup and generation handling.
- A separate focused code review found no actionable issues.

Use a rebuilt managed app with the matching native plugin or the fresh `m3-audio-unrestricted-20261020` portable package. Earlier plugins retain their compiled restrictions. No live browser-audio broadcast, device capture or playback measurement was performed for this change; automated checks do not establish audible delivery or streaming quality. Historical milestone evidence remains limited to its recorded revisions/packages.
