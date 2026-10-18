# M3 Native AMD Broadcasting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add AMD AMF H.264 to the owned native broadcaster while preserving NVENC and explicit CPU-fallback consent.

**Architecture:** Extend the pinned OBS pipeline and managed selection flow. Initialize one candidate at a time in NVENC -> AMF -> consented x264 order. Treat configured policy, successful encoder initialization and measured delivery/performance as separate evidence.

**Tech Stack:** C++20, OBS 32.2.2, .NET 10/C#, WPF, xUnit, CTest, PowerShell.

**Spec:** [Approved M3 design](../specs/2026-10-18-milestone-3-design.md). This is the first of two linked plans; execute [browser broadcasting](2026-10-18-m3-browser-broadcasting.md) after this implementation passes its code-only gate. Missing AMD hardware delays live acceptance, not browser implementation.

## Global Constraints

- Preserve one shared H.264/Opus pipeline per broadcaster; no per-viewer encoding, previews, transcoding or simulcast.
- Pin OBS **32.2.2**. Candidate IDs: `obs_nvenc_h264_tex`, then `h264_texture_amf`; `obs_x264` only with explicit consent.
- CPU fallback retains a maximum of 720p30/4 Mbps and lower requested settings.
- AMF: CBR, High profile, one-second keyframes, zero B-frames, repeated headers, `speed`, `pre_analysis=false`, `usage=lowlatency preanalysis=false vbaq=false`.
- Preserve audio/capture identity, stop/restart, safety generation, mute leases, concurrency, viewing and fullscreen behavior. No microphone, Intel work, viewer quotas, bandwidth admission or eight-viewer gate.
- AMF internal frame-upload rerouting is hardware encoding; its transfer route stays unverified. Do not infer zero-copy behavior from `obs_encoder_get_id` or `obs_encoder_get_caps`.
- Use `./scripts/Verify.ps1 -Mode CodeOnly`. Never install over an active helper or alter personal OBS. Managed-only startup does not install native changes.
- Git delivery: verified commits on `codex/`, push and PR; no merge/history rewrite. Exclude private/generated/runtime/raw files.
- Any selected agents/reviewers, including nested agents: explicitly `gpt-6.1-sol` and chosen reasoning effort; no Astra.

## Review Focus

1. Initialization fails after allocating output/callbacks: release the entire failed attempt before another candidate (Task A1).
2. An old/mismatched plugin returns a success with missing AMF policy fields: reject preparation, rather than publishing an unverified configuration (A2).
3. Stop occurs between failed NVENC and AMF preparation: never start the next candidate after invalidation (A2).
4. A probe forces CPU fallback on an AMD machine: hide both hardware candidates, not only NVENC (A3).
5. An AMF driver internally reroutes texture encoding: report initialized AMF and an unverified transfer route without asserting observed zero-copy or game performance (A1/A2 and separate live evidence in A3).

## File responsibilities and execution setup

Existing `native/jk-obs/src/ObsPipeline.cpp` owns native configuration/allocation/readback; keep that boundary. Add a device-free integration harness compiling this real file, following `AudioContextTests.cpp`, instead of introducing a second production encoder abstraction.

`src/JKiller.Windows/Obs/ObsMediaEngine.cs` owns candidate order, consent and response validation. `BroadcasterModel` and diagnostic contracts/export own display/metadata. Existing package/probe scripts own their verification boundaries.

Before A1, re-read AGENTS/roadmap/handoff/spec and inspect `git status`, current branch, attached worktrees, remote refs and PR #10. Use the worktree skill at execution time if isolation is needed; preserve the approved spec and existing interface work. Do not implement while this plan is awaiting owner review. For test commands, run from the repository root with the repository's `.tools/dotnet`, `.tools/nuget` and `.tools/dotnet-home` configuration; do not change audit/dependency policy to work around network restrictions.

## Task A1: native AMF initialization, configuration and cleanup

**Files:** Modify `native/jk-obs/src/ObsPipeline.cpp` and `native/jk-obs/CMakeLists.txt`. Create `native/jk-obs/tests/PipelineTests.cpp`, `native/jk-obs/tests/pipeline-harness/ModuleState.hpp`, `native/jk-obs/tests/pipeline-harness/NativeDiagnostics.hpp` and `native/jk-obs/tests/pipeline-harness/util/platform.h`.

**Interfaces:** Keep the existing `PreparePipeline({encoder, bitrateKbps, generation})` RPC. Its response retains `success`, `encoder`, `codec`, `hardwareInitialized`, `bitrateKbps`, `bFrames`, `repeatHeaders` and legacy `lookahead`. Add `preset:string`, `preAnalysis:bool`, `amfOptions:string`, `encoderFamily:"nvenc"|"amf"|"x264"` and `encoderTransferRoute:"unverified"`. The AMF validation path must use `preAnalysis`/`amfOptions`, not legacy `lookahead`. Diagnostics return the same family/route strings. Keep `StartPipeline`, `Disarm` and state RPC signatures unchanged.

The test harness supplies only the OBS APIs referenced by the actual pipeline and records settings, allocation counts, callback counts, initialization outcome and requested encoder. `PipelineFixture::Prepare(const char*, int, int)` returns a fixture-owned response; `Settings()` returns fixture-owned applied settings; `InitializeSucceeds` controls the fake initialization result. No OBS runtime, capture, device or child process is loaded.

- [ ] **Step 1: Add failing device-free integration cases** in `PipelineTests.cpp`, registered as `Pipeline.*` CTests. Assert policy against settings actually passed by `ObsPipeline.cpp`, not a duplicated policy function:

```cpp
PipelineFixture f;
auto* reply = f.Prepare("h264_texture_amf", 4000, 1);
Require(obs_data_get_bool(reply, "success"), "AMF preparation rejected");
Require(std::string(obs_data_get_string(f.Settings(), "preset")) == "speed", "AMF preset");
Require(!obs_data_get_bool(f.Settings(), "pre_analysis"), "AMF analysis");
Require(std::string(obs_data_get_string(f.Settings(), "ffmpeg_opts")) ==
        "usage=lowlatency preanalysis=false vbaq=false", "AMF options");
Require(obs_data_get_int(reply, "bFrames") == 0, "B-frames");
Require(obs_data_get_bool(reply, "repeatHeaders"), "late-join headers");
```

Add named cases `AmfPolicyAndInitialization`, `FailedAttemptReleasesAllResources`, `ActiveOutputCannotBeReconfigured`, `InvalidTypedRequestCreatesNothing`, `NvencAndX264PolicyPreserved`, and `RerouteDoesNotProveTextureTransfer`. Check 1000/25000-kbps boundaries, rejection of 500/25500/1250, generation <= 0 and unsupported IDs; allocation/callback counts return to zero on failure and before a second preparation. Simulate AMF rerouting with an unchanged original ID; initialization success still yields family `amf`, route `unverified`. Failed initialization must report `hardwareInitialized=false`.

- [ ] **Step 2: Run** `./scripts/Build-Native.ps1 -Plugin -Test -NonInteractive -NoInstall`. Expected RED: the AMF success/policy assertions fail against the existing NVENC/x264-only code. Fix harness compilation independently before interpreting a failure as behavioral evidence.
- [ ] **Step 3: Extend** `Prepare`, `Describe` and `Release` in `ObsPipeline.cpp` using the exact RPC fields above. Separate the AMF branch from NVENC preset/tune/multipass settings. Preserve typed ranges, audio allocation, callback ownership, active-output refusal, generation and initialization-before-hardware reporting. Add fixed family/route values to native diagnostics; do not patch upstream OBS or parse logs to invent the route. Preserve required configuration fields even when no output is active; bounded failure codes/messages must not expose URLs/tokens.
- [ ] **Step 4: Repeat the Step 2 command.** Expected GREEN for all noninteractive tests and native plugin compilation; no installation. Confirm the harness compiles production `ObsPipeline.cpp` and no excluded live test was registered under a new name.
- [ ] **Step 5: Commit** only the listed native source/test/CMake files: `feat(m3): add pinned OBS AMF H264 pipeline support`.

## Task A2: managed candidate selection, consent and accurate diagnostics

**Files:** Modify `src/JKiller.Windows/Obs/ObsMediaEngine.cs`, `src/JKiller.App/MainWindow.xaml`, `src/JKiller.Windows/Presentation/BroadcasterModel.cs`, `src/JKiller.Contracts/DiagnosticsContracts.cs`, `src/JKiller.Core/Diagnostics/DiagnosticsExport.cs`, `tests/JKiller.Windows.Tests/ObsMediaEngineTests.cs`, `tests/JKiller.Windows.Tests/BroadcasterModelTests.cs` and `tests/JKiller.Core.Tests/DiagnosticsExportTests.cs`. Create `tests/JKiller.Windows.Tests/ObsDiagnosticsReaderTests.cs`.

**Interfaces:** Keep `IMediaEngine` and `AppliedMediaSettings` unchanged. Append optional `string? EncoderFamily = null, string? EncoderTransferRoute = null` to `MediaDiagnostics`. Native JSON names are `encoderFamily`/`encoderTransferRoute`. Export metadata allowlists use those same keys and the values from A1; add `h264_texture_amf` to `encoder`. Legacy diagnostics remain deserializable with null optional fields.

- [ ] **Step 1: Add failing managed tests.** Extend the existing fake RPC to configure advertised IDs, per-candidate success, malformed readback, barriers/cancellation and prepared bitrate/dimensions. Preserve existing fake defaults for unchanged NVENC tests. Name cases `AmdOnlyInitializesHardware`, `FailedNvencTriesAmfBeforeConsentedCpu`, `MissingHardwareNeverEnablesCpu`, `AmfPolicyReadbackIsRequired`, and `StopBetweenCandidatesPreventsNextAttempt`. Assertions include:

```csharp
Assert.Equal(new[] { "obs_nvenc_h264_tex", "h264_texture_amf" }, rpc.PreparedEncoders);
Assert.Equal("h264_texture_amf", applied.Encoder);
Assert.True(applied.HardwareVerified);
Assert.DoesNotContain("obs_x264", rpc.PreparedEncoders);
```

Also assert no third candidate after cancellation, one helper cleanup, unchanged hardware dimensions, CPU at <=720p/30 fps/4000 kbps only with consent, and retention of lower requests. Reject success with wrong codec/encoder/bitrate, hardware flag false for AMF, B-frames, missing headers, wrong/missing preset/analysis/options. Keep valid NVENC/x264 legacy readback working. Redaction tests retain only exact family/route/encoder values and exclude arbitrary errors/paths/tokens; reader tests deserialize both new and legacy diagnostic payloads. Presentation tests show AMF registration plus the explicit unverified-route limitation.
- [ ] **Step 2: Run** `./.tools/dotnet/dotnet.exe test tests/JKiller.Windows.Tests --filter "FullyQualifiedName~ObsMediaEngineTests|FullyQualifiedName~ObsDiagnosticsReaderTests|FullyQualifiedName~BroadcasterModelTests" --nologo` and `./.tools/dotnet/dotnet.exe test tests/JKiller.Core.Tests --filter FullyQualifiedName~DiagnosticsExportTests --nologo`. Expected RED on AMD selection, new diagnostic values or presentation behavior.
- [ ] **Step 3: Change** `PrepareAsync(BroadcastRequest, CancellationToken)` to attempt discovered hardware IDs sequentially and check epoch/cancellation before and after each asynchronous attempt. Only initialization failure/unavailability advances the hardware candidates; incompatible successful readback fails closed. Do not swallow RPC disconnection, cancellation or safety faults as encoder failure. Retain the existing explicit CPU reduction/configuration branch after both hardware candidates fail. Validate AMF using A1 fields, and report bounded per-candidate unavailable/failed status without echoing native free text.
- [ ] **Step 4: Apply** the diagnostic contract/export/presentation changes. Update the checkbox to `Allow CPU encoding if hardware initialization fails (may cost game performance)`. Show AMF transfer route as unverified in the existing applied/observed presentation, without introducing a manual encoder selector or performance promise. Do not clear native playback mute before the owned helper stops.
- [ ] **Step 5: Repeat Step 2 and build** `./.tools/dotnet/dotnet.exe build src/JKiller.App/JKiller.App.csproj --nologo`. Expected GREEN including existing audio, start/stop, heartbeat and mute-lease tests; no capture/helper launch.
- [ ] **Step 6: Commit** the explicit files from this task: `feat(m3): select AMD hardware before opt-in CPU fallback`.

## Task A3: verification/package path and honest hardware handoff

**Files:** Modify `tools/acceptance/Common/CpuFallbackProbeRpc.cs`, `tests/JKiller.Windows.Tests/CpuFallbackProbeTests.cs`, `scripts/PortablePackageTools.ps1`, `tests/setup/PortablePackageChecks.ps1`, `docs/setup/publishing-and-compatibility.md`, `native/jk-obs/README.md`, `docs/M3-HANDOFF.md` and `docs/ROADMAP.md`. Add `docs/validation/2026-10-18-m3-amd.md` (use actual execution date if later). The Windows test project already links `CpuFallbackProbeRpc.cs`; reuse that link and its existing tests.

**Interfaces:** `CpuFallbackProbeRpc` keeps its existing `IObsRpcClient` wrapper signature and hides both `obs_nvenc_h264_tex` and `h264_texture_amf` only in test capabilities. Actual encoder preparation/output must remain real when a live probe is explicitly run. Extend `Test-PortableNativeSourcePath` to include the three A1 `tests/pipeline-harness/` headers, retaining its allowlist behavior.

- [ ] **Step 1: Add failing probe/package assertions.** Extend and rename the existing `InjectionSuppressesOnlyNvencDiscoveryLeavingSoftwareAndOpus` test to cover NVENC/AMF/x264/Opus: assert only x264/Opus survive filtering, the original response is unchanged and non-capability RPCs pass through once. Package checks assert `tests/pipeline-harness/ModuleState.hpp`, `NativeDiagnostics.hpp` and `util/platform.h` are accepted source paths, while `tests/pipeline-harness/private.log` and traversal remain rejected. Inspect the pinned archive without extracting into a live runtime: required entries are `obs-plugins/64bit/obs-ffmpeg.dll`, `bin/64bit/obs-amf-test.exe`, `bin/64bit/avcodec-62.dll`, `bin/64bit/avutil-60.dll` and `bin/64bit/swscale-9.dll`. Keep the driver-supplied AMF runtime distinct from bundled dependencies. Do not make clean-checkout fixture tests depend on a predownloaded archive; archive inspection belongs to the prepared development/package verification.
- [ ] **Step 2: Run** `./.tools/dotnet/dotnet.exe test tests/JKiller.Windows.Tests --filter FullyQualifiedName~CpuFallbackProbeTests --nologo` and `./tests/setup/PortablePackageChecks.ps1`. Expected RED on the AMD candidate still being advertised or new harness source paths rejected.
- [ ] **Step 3: Update** the probe filter and package source allowlist. Keep pinned dependencies/modules/license notices; no SDK download, generated binary commit or broad allowlist expansion. Re-run Step 2 to GREEN.
- [ ] **Step 4: Run** `./scripts/Verify.ps1 -Mode CodeOnly`, retaining its ignored log under `.artifacts/`. Expected exit 0 across managed/browser/native/setup/build checks, with explicit live exclusions and no plugin install. Run `git diff --check` and validate relative documentation links. Review the changed native and managed paths together against the spec; use the selected execution method's review workflow with the required model if agents are chosen.
- [ ] **Step 5: Write** the compatibility/handoff/validation updates with exact revision, test results, compiled-but-not-installed status and limitations. Mark native implementation delivered only after its checks pass; keep real AMD and NVIDIA regression, audio delivery, adapter behavior and game impact pending where untested. Ask for AMD GPU/driver/Windows details when arranging live validation; do not stop unrelated sessions to obtain it. Record the matched plugin/package requirement and absence of a universal game-impact guarantee. Historical M2/M3 evidence is retained.
- [ ] **Step 6: Commit and push** the explicit source/test/docs changes: `test(m3): verify AMD package and fallback boundaries`. Recheck the remote/PR state, push the `codex/` branch and update/attach the appropriate PR without merging. Record code verification separately from live acceptance. Then continue to the browser plan if its owner review and execution-method gate are satisfied.

## Owner review / execution choice

This plan and the linked browser plan await owner review. Recommended method: **native execution** for both plans, because the critical changes share encoder/RPC and publisher/controller interfaces that benefit from one implementer's context. Use the required final independent review, explicitly `gpt-6.1-sol` with high reasoning, if this method is selected. Alternatively, subagent-driven execution uses the same model requirement for every implementer and reviewer. Neither method authorizes installation over active sessions or substitutes mocks for live hardware acceptance.
