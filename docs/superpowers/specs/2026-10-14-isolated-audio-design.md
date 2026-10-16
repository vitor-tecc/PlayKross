# M1 audio isolation amendment — draft for user approval

Status: **user declined this launch-based approach, then deferred all audio work on2026-10-14. Retained only as a design record, not an implementation plan.** No audio re-enablement or launcher implementation is authorized by this document. Current M1 is explicitly video-only. Native capture/NVENC, MediaMTX, browser viewing, invitations, settings, hosting and no-microphone requirements remain as approved.

## User-visible decision

Add an explicit **Launch app with isolated audio** action for a small set of supported standalone applications. Initially validate the synthetic fixture and Geometry Dash. The user chooses the executable and starts a fresh instance through PlayKross. The UI explains that isolation requires a fresh launch; it never assigns restrictions to an already-running game. Existing windows/screens remain shareable with audio disabled, or with an explicitly selected separately launched supported audio source.

If the target ever creates another process, its audio boundary becomes permanently unsafe for that launch and capture fails closed. This deliberately sacrifices audio compatibility for apps with workers/launchers. The game is allowed to create children normally, but explicit breakaway attempts can fail because the boundary disallows escape. No silent restrictions, automatic game termination, system-output recording, microphone or mixed-audio subtraction are added. Browser/Discord/controller/viewer/unknown roots never qualify. Discord within a shared browser cannot be separated safely and that source stays video-only.

## Why this approach

Windows process-loopback captures the target process **and its descendants**. The current positive filename profile plus a polled process tree cannot catch a prohibited child that starts, produces audio and exits between polls. Therefore current audio remains disabled.

Recommended: use a fresh unnamed Windows job with breakaway disabled, atomically associate the target at creation before its code executes, and retain its cumulative process accounting. Query that accounting after PCM has been copied and before every emitted block. `TotalProcesses` must remain exactly one and `ActiveProcesses` exactly one. Accounting includes terminated children, so a short-lived child cannot disappear from history. A child starting after the check cannot retroactively alter the already-copied PCM block; later blocks must be rejected. The implementation must validate this ordering with real adversarial process/tone fixtures.

Alternatives: retaining snapshot polling cannot satisfy the strict exclusion requirement; an active-process limit of one would prevent child creation and interfere more severely with games. A virtual audio device/application-routing solution is deferred unless the proposed boundary fails validation; it adds driver/deployment work and does not itself prove Discord exclusion.

This is a proposed enforcement argument, not a measured guarantee. [Microsoft job semantics](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects), [cumulative accounting](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_accounting_information), and [process-loopback scope](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-process_loopback_mode) were checked on2026-10-14.

## Ownership and native enforcement

The desktop controller owns a launch registry containing exact executable/profile, PID, creation identity, job handle and a fresh opaque launch ID. Use the existing Windows native process-creation patterns for atomic job assignment; refuse incompatible inherited jobs or access failures. Do not adopt an existing process or an already-populated job. The application job must be distinct from the OBS helper's kill-on-controller-exit job and omit KILL_ON_JOB_CLOSE. Stopping broadcasting/closing PlayKross must not intentionally kill the user's game. Job association remains until the game exits and can affect breakaway behavior; explain this before launch.

The native helper accepts a boundary only over the existing controller-identity-bound pipe. Duplicate a query-only job handle from the authenticated controller and validate root PID/creation/profile, membership, cumulative/active counts and no-breakaway flags independently. A bare job name, caller-supplied boolean, filename match or arbitrary audio input setting is insufficient. The private launch registration is part of the trusted controller/native boundary; it is not a defense against malicious software already able to modify the controller under the same Windows user.

Both native audio creation paths require a registered live boundary. Check after buffer copy, when queuing and immediately before dispatch; on failed query, extra process, root exit, changed identity, controller loss or selection change: seal the source, clear bounded PCM queues and stop publication through the existing safety path. No stale queued block can be replayed after sealing. The UI reports the reason and allows a new video-only broadcast. Settings restarts reuse only the same still-valid registered launch, with fresh helper-handle registration; PID reuse cannot rebind it.

## Validation milestones

1. Kernel-boundary primitive: real source launched from birth; root accounting1; long-lived and short-lived child accounting permanently>1; denied breakaway; access/identity failures; closing capture leaves target alive. No audio re-enabled yet.
2. Native delivery boundary: audible intended997Hz; child tone created before/while/after PCM copy; rejected child blocks after its exit; cumulative history persists; direct input bypass denied; queue contents discarded on seal; controller/viewer intersection denied. Cover ordering with tests before implementation.
3. Desktop workflow: explicit supported-app launch, registration, safe source selection, requested/applied settings restart, actionable boundary-loss status; browser/unknown/existing-process audio still denied. Native positive profiles enforced independently.
4. Actual media: selected tone audible; separate native1801Hz and browser/incoming2753Hz excluded; user-controlled real Discord desktop/browser activity excluded; calibrated delivered A/V offset within±100ms. No automatic calls/messages or private capture recordings. Repeat local600s and external phone evidence after audio restoration.

Passing mocked correctness cannot certify safe real audio, smooth delivery, hardware encoding or audiovisual timing. M1 remains pending until the fresh required real gates pass. No eight-viewer/multiple-broadcaster/future-UX work is included.
