# PlayKross

<img src="assets/branding/playkross-logo.png" alt="PlayKross" width="180" />

Self-hosted screen sharing: a native Windows broadcaster and browser viewers. Blue #6a94c5 and yellow #FFDE5A form the visual identity.

The working implementation is on [feat/milestone-1](https://github.com/vitor-tecc/PlayKross/tree/feat/milestone-1), under [draft PR #3](https://github.com/vitor-tecc/PlayKross/pull/3). Main retains the approved planning baseline until the implementation is ready to merge.

**Development prototype; acceptance pending.** Review blockers are tracked in [#1](https://github.com/vitor-tecc/PlayKross/issues/1), including the strict audio-exclusion boundary. Real Discord exclusion, calibrated A/V timing and external phone acceptance are tracked in [#2](https://github.com/vitor-tecc/PlayKross/issues/2). Single-viewer loopback measurements do not establish eight-viewer internet capacity.

To inspect/build the prototype:

```powershell
git clone --branch feat/milestone-1 https://github.com/vitor-tecc/PlayKross.git
cd PlayKross
```

Follow the [implementation README and Windows setup](https://github.com/vitor-tecc/PlayKross/blob/feat/milestone-1/README.md). Source-only CI and dependency-update configuration live in the draft implementation. Local SDKs, runtime binaries, package caches, room credentials and generated evidence are ignored rather than uploaded.

Future smoother UX and dependency simplification remain deferred.
