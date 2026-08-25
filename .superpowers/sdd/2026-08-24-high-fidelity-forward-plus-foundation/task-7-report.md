# Task 7 Report: GPU Timing, Budget Inspection, and Quality Overrides

Date: 2026-08-25
Base commit: `ee161b116340c8ff6ba585fe4246bc30fbc9d82f`
Branch: `codex/high-fidelity-forward-plus`
Implementation commit: `dd2c69c18cd5438c4a5078fcd412599b9483875c`

## Outcome

Task 7 is implemented. The native Vulkan high-fidelity path owns persistent timestamp query pools, records paired timestamps around every pass that it actually executes, reads only a fence-completed prior frame, converts device ticks with the physical device's `timestampPeriod` and graphics queue family's `timestampValidBits`, and returns ordered backend-neutral GPU pass/frame reports. Unsupported or unavailable queries return `REKALL_GPU_TIMESTAMPS_UNAVAILABLE`; CPU elapsed time is never substituted.

Capture and performance-budget inspection accept caller-scoped quality intent, bounded overrides, and an `includeGpuTimings` switch without mutating authored scene/gameplay state. Results expose requested/resolved preset, internal resolution, resource bytes, draw/dispatch work, degradations, GPU timing provenance, and suggested commands. The exact generic shared operation `rekall.render.compare_quality_presets` produces deterministic aligned captures through both the command/MCP registry and CLI.

The engine contracts contain no Aetherfall, game, genre, or controller-specific concepts. Vulkan is the reference implementation while the public quality, degradation, resource, workload, and timing records remain backend-neutral.

## Strict TDD evidence

The writing-good-tests reference was read before test edits. Tests assert observable contracts and native behavior rather than implementation text.

### RED

1. Initial focused command, with exact NTFS isolation root `D:\RekallAgeTask7Red`:

   ```powershell
   $env:REKALL_AGE_TEST_TEMP_ROOT='D:\RekallAgeTask7Red'
   $env:MSBUILDDISABLENODEREUSE='1'
   dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanGpuProfilerTests|FullyQualifiedName~RuntimeInspectCliTests|FullyQualifiedName~McpCatalogTests"
   ```

   Expected compilation RED after 20.8 seconds: `CompareQualityPresetsCommand`, `CompareQualityPresetsRequest`, `RekallAgeRenderQualityOverrides`, `RekallAgeVulkanGpuProfiler`, `RekallAgeVulkanGpuPassTimestampSample`, and `RekallAgeVulkanGpuQueryPoolLifecycle` did not exist; capture requests did not expose quality/timing inputs. Two test-fixture compilation errors (command-context construction and an ambiguous `Assert.Throws` lambda) were corrected before accepting this feature RED.

2. CLI/MCP inspection RED at `D:\RekallAgeTask7InspectionRed`: 0/2 passed in 581 ms. MCP reflection returned null for `InspectScenePerformanceBudgetRequest.QualityPreset`; the extended positional CLI invocation exited 2 because no route existed.

3. Native per-preset timing RED at `D:\RekallAgeTask7CompareRed`: 0/1 passed. Both Performance and Low returned `REKALL_GPU_TIMESTAMPS_UNAVAILABLE` because a one-capture-per-preset comparison had no completed same-quality prior frame. The operation was repaired to perform one timing warm-up and one measured capture per Vulkan preset, without weakening the assertion.

4. Bounded-override RED at `D:\RekallAgeTask7BoundsRed`: 0/1 passed in 170 ms. A requested 0.01 scale resolved to a 1-pixel render width (0.03125 at width 32), and unbounded shadow/particle values leaked into resource estimates. The caller override layer now clamps and reports exact requested/resolved facts.

5. Abandoned-recording lifecycle RED at `D:\RekallAgeTask7CancelRed`: compilation failed because `CancelRecording` did not exist. The lifecycle now releases only an unsubmitted `Recording` lease; it still refuses to reset or reuse `InFlight` and unread `Completed` slots.

### GREEN milestones

- Pure timestamp conversion, wrap, ordering, unavailable state, lifecycle, and software compare: 8/8 passed.
- CLI/MCP capture and comparison surface: 3/3 passed.
- Native prior-frame profiler: 1/1 passed.
- Native per-preset warm-up comparison: 1/1 passed in 2.8993 seconds.
- Inspection CLI/MCP extension: 2/2 passed in 1.5769 seconds.
- Bounded override regression: 1/1 passed in 1.0181 seconds.
- Native measured performance-budget inspection: 1/1 passed in 2.1913 seconds.
- Cancellation plus native prior-frame regression: 2/2 passed in 2.0095 seconds.

## Query lifecycle and provenance design

### Device facts and conversion

- On persistent device creation, the profiler reads `VkPhysicalDeviceProperties.limits.timestampPeriod` and the selected graphics queue family's `timestampValidBits`.
- One 64-bit start/end pair is assigned to each ordered executed pass. Start writes use top-of-pipe; end writes use bottom-of-pipe, so each reported duration envelopes that pass's submitted GPU work.
- Tick deltas are computed modulo the reported valid-bit mask; 64 bits use natural unsigned subtraction. Nanoseconds are `ticks * timestampPeriod`, milliseconds are nanoseconds divided by 1,000,000.
- Total frame GPU duration is the first executed pass's start through the last executed pass's end, not the sum of potentially gapped pass durations.
- Output provenance is exactly `vulkan-timestamp-query` when available. Invalid period, zero or greater-than-64 valid bits, empty results, failed result retrieval, lack of timestamp support, and initial/wrong-quality readback all return the exact unavailable code with nullable totals and no pass values.

### Pool/fence state machine

Each persistent profiler has two slots with states `Free -> Recording -> InFlight -> Completed -> Read`. A slot is acquired/reset only from `Free` or `Read`. `vkCmdResetQueryPool` is recorded in the producing command buffer only after that lifecycle acquisition.

After successful `vkQueueSubmit`, the query lease receives a unique fence token and becomes `InFlight`. Only successful `vkWaitForFences` changes it to `Completed`. The next capture checks completed slots before recording its own queries, calls `vkGetQueryPoolResults` without a wait flag, marks the slot `Read`, and then permits reuse. A completed result whose quality signature does not match is intentionally consumed/discarded, preventing cross-preset attribution.

If query-pool creation, query-object construction, command recording, cancellation, or submission fails before a successful queue submit, only the still-`Recording` lease can be cancelled and reused. An `InFlight` lease is never cancelled/reset. Device/session disposal waits idle before destroying pools.

### Frame report integration

The high-fidelity report preserves executed dependency order and attaches timings by stable pass name. It also carries the resolved quality plan, graph resource-byte estimate, per-resource byte estimates, and executed draw/dispatch totals. The capture and performance-budget results forward those facts. A later CPU UI composite remains labeled `cpu-composite` and is not assigned a fabricated GPU duration.

## Quality inputs, CLI/MCP schema, and compatibility

### Shared request/result contracts

- `CaptureRuntimeViewportRequest` retains its primary constructor and all legacy positional behavior; init properties add `QualityPreset`, `QualityOverrides`, and `IncludeGpuTimings`.
- `InspectScenePerformanceBudgetRequest` likewise retains its primary constructor and adds the same three init properties.
- Their result records add resolved quality, GPU timing report, resource bytes, and render workload counts without breaking existing constructors.
- `CompareQualityPresetsRequest` accepts project/scene, 2-6 distinct exact presets, aligned frames/inputs, output location, dimensions, backend, bounded overrides, and timing choice. Its result returns requested/resolved presets, capture paths and image metrics, degradations, resource/workload facts, timing reports, and next commands.

Caller override safety bounds are resolution scale 0.25-2.0, shadow cascades 1-4, shadow resolution 128-8,192, and maximum active particles 0-1,048,576. Clamps use `REKALL_RENDER_QUALITY_OVERRIDE_CLAMPED` with invariant requested/resolved values. Invalid preset/fog/non-finite values continue through the deterministic quality resolver and its stable invalid degradation. The only supported presets are exactly Performance, Low, Medium, High, Ultra, and Epic. Compare also validates backend and requires software or Vulkan.

### CLI routes

All older routes remain unchanged, including the legacy viewport forms and the legacy performance-budget forms. New longest positional forms are:

```text
render viewport capture <root> <scene> <frames> <output> <width> <height> <backend> <inputsJson> <qualityPreset> <overridesJson> <includeGpuTimings>
render performance budget <root> <scene> <profile> <frames> <width> <height> <qualityPreset> <overridesJson> <includeGpuTimings>
render quality compare <root> <scene> <frames> <output> <width> <height> <backend> <commaPresets> <overridesJson> <includeGpuTimings>
```

CLI capture/inspection print requested/resolved preset, internal resolution, transient/persistent and total resource bytes, ordered GPU pass nanoseconds/milliseconds plus total, workload counts, degradation code with requested/resolved values, and next commands. Compare prints the same per-capture facts, including pass timings when available.

MCP discovers these exact command request schemas from the same command registry. `rekall.render.compare_quality_presets` is classified as a recommended generic rendering tool with agent priority 27. CLI and MCP therefore execute the same command implementation; there is no duplicate comparison path.

## Preset comparison evidence

- Software comparison captured Performance and High at identical frame 3, identical 160x90 output and caller-forced 80x45 internal resolution, with two distinct paths and nonblank analysis. Serializing the authored scene before/after returned byte-equivalent JSON.
- Native comparison captured Performance and Low at identical frame 2. Each preset performed a same-signature warm-up before its measured capture; both returned available ordered GPU passes whose report frame matched the captured frame.
- Bounded comparison clamped 0.01 to 0.25, `Int32.MaxValue` shadow resolution to 8,192, and `Int32.MaxValue` particles to 1,048,576. Every clamp included stable code plus exact requested/resolved values.
- The operation enumerates presets in caller order, reuses identical deterministic frame/input facts, writes each preset into its own canonical subdirectory, and never writes the scene document.

## Godot architectural reference notes

Local reference only; no source was copied.

- `F:\Dev\godot-reference\drivers\vulkan\rendering_device_driver_vulkan.cpp`, lines 6807-6869: query-pool create/free/read, timestamp-period conversion concern, command-buffer pool reset, and timestamp writes. The reusable ideas were explicit pool ownership, command-buffer reset, 64-bit results, and device-period conversion. Rekall independently uses paired top/bottom pass markers and valid-bit modulo handling required by this task.
- `F:\Dev\godot-reference\servers\rendering\rendering_device.cpp`, lines 8153-8161: per-frame-slot prior-result retrieval, reset, and result/name swapping. The reusable idea was delayed readback from a prior frame slot rather than stalling the current producer.
- Same file, lines 8536-8544 and 8596-8598: bounded per-frame pool allocation and initialization reset. Rekall uses a small explicit slot lifecycle tied to producing fence completion.
- Same file, lines 8762-8774: bounded named timestamp capture. The reusable idea was stable names and a fixed maximum; Rekall derives an exact bounded pair count from executed graph pass names.

## Final verification

All commands ran sequentially with `MSBUILDDISABLENODEREUSE=1`; test commands used dedicated exact NTFS roots under `D:`.

| Command | Result |
|---|---|
| Exact Task 7 filter from the brief, final run at `D:\RekallAgeTask7FocusedFinal2` | 35/35 passed, 0 failed, 0 skipped; test duration 6 s; command wall time 10.0 s. |
| Adjacent `ScenePerformanceBudgetCommandTests|CaptureRuntimeViewportCommandTests|VulkanHighFidelityCaptureTests|VulkanParticleCaptureTests|RenderQualityProfileTests|HighFidelityRenderGraphTests` at `D:\RekallAgeTask7RegressionFinal` | 89/89 passed, 0 failed, 0 skipped; test duration 37 s; command wall time 38.1 s. |
| `dotnet build Rekall.AGE.sln --no-restore -m:1 --verbosity:minimal` after the final source change | Succeeded; 0 warnings, 0 errors; 5.69 s. |
| `git diff --check` before implementation commit | Clean; only existing line-ending conversion warnings. |

The verified Task 7 test gates total zero failures and zero skips. The final 2,560x1,440 High preset acceptance measurement over 600 representative timestamped frames is intentionally the Task 10 gate and was not claimed here.

## Commits

- `dd2c69c18cd5438c4a5078fcd412599b9483875c` - `feat: inspect high-fidelity GPU quality budgets`
- Evidence-report commit: pending at report authoring time; recorded in the handoff after commit.

## Process, cleanup, and residual concerns

- Branch and base were verified before work; implementation commit was made on `codex/high-fidelity-forward-plus` directly from `ee161b116340c8ff6ba585fe4246bc30fbc9d82f`.
- No project-wide test suites were run concurrently, so publish outputs were not contended.
- Performance-budget timing uses a dedicated GUID capture directory under the OS temp root, an isolated nested transaction, and a `finally` cleanup. The final inspection run left only the empty parent `C:\Users\Marius\AppData\Local\Temp\RekallAgeGpuBudget`.
- Seventeen exact `D:\RekallAgeTask7*` test roots remain. Their absolute paths were enumerated and validated, but both a validated bulk `Remove-Item -LiteralPath ... -Recurse -Force` and a validated single-root removal were blocked before process creation by execution policy. No deletion was performed or partially performed. This is external test residue, not worktree residue.
- No Task 7 `testhost`/`vstest` command remains active after the completed gates. Worktree cleanliness is verified after the evidence commit in the handoff.
- Residual acceptance work: Task 10 must run the required High 2,560x1,440/60, 600-representative-frame GPU timestamp gate. Unsupported hardware remains truthful through `REKALL_GPU_TIMESTAMPS_UNAVAILABLE`; it cannot satisfy that later performance acceptance gate.
- The implementation records only passes actually executed by the current native path. Graph-declared but currently unimplemented passes are not assigned fabricated durations.

---

## Review fix round 1/5 (2026-08-25)

Implementation commit: `5e445c2` (`fix: isolate GPU quality capture lifecycle`)

This section supersedes the earlier statement that native comparison warms the same scene frame and that the returned timing frame equals the captured frame. Task 7 deliberately reads a completed prior frame. Fix round 1 now uses frame `N-1` as the isolated temporal/timestamp warmup and frame `N` as the aligned measured image; the timing report remains truthfully labeled `N-1`.

### Root causes and repairs

1. **Submitted-fence failure crossed the resource-ownership boundary unsafely.** `vkQueueSubmit` could succeed, then `vkWaitForFences` could return timeout/error and throw before either the timestamp lease or frame-resource lifetime was transitioned. The common `finally` cancelled only `Recording` queries and unconditionally disposed `VulkanState`. Persistent frames do not own their device, so that disposal skipped device idle and destroyed the submitted fence, command pool, buffers, images, descriptors, and pipelines while the queue might still reference them.

   The repair adds an independently testable submission state machine: `NotSubmitted -> Submitted -> FenceCompleted`, `RecoveredAfterDeviceIdle`, or terminal `DeviceLost`. `VulkanState.Dispose` refuses unresolved submitted work. A failed fence wait first attempts `vkDeviceWaitIdle`; successful idle makes resource destruction safe and invalidates the abandoned `InFlight` query lease, while `ErrorDeviceLost` takes a separate rebuild path. If neither completion nor device loss can be established, the persistent context retains the complete frame state, refuses new submissions, and retries idle recovery on the next capture. Persistent disposal also refuses to destroy native children when idle cannot be established. This favors bounded poisoned-session retention over invalid Vulkan destruction.

2. **Comparison ownership was scoped to the command instead of one preset.** The public compare command held one default `CaptureRuntimeViewportCommand`, whose native scene capture held one persistent Vulkan context. Fog history, particles, and query pools could therefore cross preset and invocation boundaries.

   The default compare operation now creates one fresh `RekallAgeNativeVulkanSceneCapture` session per requested preset, uses that same session only for its `N-1` warmup and aligned `N` measurement, then disposes it deterministically before moving to the next preset. Each later preset and later command invocation begins with fresh temporal generations. The authored runtime input and scene frame remain identical across presets; only caller-scoped rendering work changes.

3. **Capture facts stopped at the shared command boundary.** `CaptureRuntimeViewportResult` already carried draw/dispatch counts, but it had no next commands and the CLI printed neither workload nor recovery/follow-up actions.

   The shared capture result now adds an init-only `SuggestedCommands` property, preserving the existing positional record constructor and request forms. It always returns exactly two generic strings, sanitizes and bounds authored path/name fragments, and caps every command at 512 characters. The commands point to `rekall.render.compare_quality_presets` and `rekall.render.performance.inspect_scene_budget`. The CLI prints `Workload: draws=<n>; dispatches=<n>` plus `Next:` lines. MCP uses the unchanged registry/executor serialization path and exposes `drawCount`, `dispatchCount`, and `suggestedCommands` from that same result.

### Strict RED evidence

The systematic-debugging trace identified the queue/fence, persistent-session, and presentation boundaries before test or production edits. The writing-good-tests reference and strict TDD skill were applied before editing tests.

Exact initial RED command (dedicated NTFS root, sequential execution):

```powershell
$taskTemp = 'D:\RekallAgeTask7FixRed'
New-Item -ItemType Directory -Force -Path $taskTemp | Out-Null
$env:TEMP = $taskTemp
$env:TMP = $taskTemp
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~VulkanGpuProfilerTests|FullyQualifiedName~CaptureRuntimeViewportCommandTests|FullyQualifiedName~RuntimeInspectCliTests|FullyQualifiedName~McpAgentToolExecutorTests" --no-restore --verbosity minimal
```

Expected RED after 21.1 seconds: build failed with 12 contract errors. `CaptureRuntimeViewportResult.SuggestedCommands`, `RekallAgeVulkanSubmissionLifecycle`, `RekallAgeVulkanSubmissionState`, `RekallAgeVulkanGpuQueryPoolLifecycle.InvalidateSubmitted`, and the test-visible quality-capture high-fidelity evidence did not exist. Representative exact diagnostics were `CS1061: 'CaptureRuntimeViewportResult' does not contain a definition for 'SuggestedCommands'`, `CS0246: The type or namespace name 'RekallAgeVulkanSubmissionLifecycle' could not be found`, and `CS1061: 'RekallAgeVulkanGpuQueryPoolLifecycle' does not contain a definition for 'InvalidateSubmitted'`.

The first compiling GREEN attempt exposed one legitimate prior-frame expectation mismatch: 70/71 passed and `NativePresetComparisonWarmsEachPresetSoReturnedMetricsMatchThatPreset` reported expected frame 2 / actual frame 1 for both presets. The implementation was not relabeled or substituted. The regression was corrected to assert the task-specified completed prior-frame provenance (`capture.FrameIndex - 1`) while keeping the aligned image at frame 2.

### Lifecycle and provenance regressions

- `SubmittedFrameTimeoutRetainsResourcesUntilDeviceIdleAndInvalidatesItsQueryLease` starts after a modeled successful submit, records `Result.Timeout`, proves resources/query slot remain unreleasable, then records successful device idle, invalidates the exact fence-token lease, and proves generation-safe reuse.
- `SubmittedFrameWaitErrorFollowedByDeviceLossRequiresRebuildBeforeRelease` starts after a modeled successful submit, records `Result.ErrorUnknown`, proves retention, then records `Result.ErrorDeviceLost` from idle recovery and proves the separate device-rebuild terminal state.
- Production code marks the frame submitted immediately after successful `vkQueueSubmit`, before profiler bookkeeping that could itself throw. Thus every later exception observes the correct native ownership state.
- A fence success completes the query normally for delayed readback. Idle recovery/device loss discards the failed capture's query lease; it is never reported as a later successful capture and never exhausts the two-slot pool.
- A still-unresolved persistent state is held as a complete `VulkanState`, not partially dismantled. No subsequent frame may use that persistent device until idle recovery succeeds or device loss causes deterministic teardown/recreation.
- GPU timing facts remain exclusively `vulkan-timestamp-query` results. This fix adds no CPU clocks or timing substitution.

### Isolated preset comparison evidence

`NativeTemporalFogComparisonIsIsolatedAcrossPresetOrderAndRepeatedInvocations` authors a real Camera3D, cube, global fog volume, and tone-map stack, then runs the same `CompareQualityPresetsCommand` instance three times:

1. Performance, Low;
2. Low, Performance;
3. Performance, Low again.

All comparisons use Vulkan, frame 4, 64x48 output, a shared bounded render-only override (`resolutionScale=0.5`, one 128px shadow cascade, `froxel-low`, bloom/SSAO disabled, zero particles), and GPU timings. Every measured preset reports temporal reprojection, `HistorySampled=true`, and `HistoryResourceGeneration=1`, proving the history came from its own fresh session's frame-3 warmup. For each preset, PNG bytes are identical across forward, reversed, and repeated invocations. JSON serialization of the authored scene before and after is identical.

### CLI, command, and MCP compatibility evidence

- Command behavior asserts the software capture reports `DrawCount=1`, `DispatchCount=0`, exactly two useful generic suggestions, and a maximum 512-character length.
- CLI behavior exercises the existing longest positional viewport invocation unchanged, then asserts quality output plus `Workload: draws=1; dispatches=0` and both `Next:` commands.
- MCP behavior registers the real capture command, executes `rekall.render.capture_runtime_viewport` through `RekallAgeMcpAgentToolExecutor` with JSON arguments, and asserts serialized `value.drawCount`, `value.dispatchCount`, and the bounded `value.suggestedCommands`. This is execution/serialization coverage, not a catalog-string check.
- No request constructor, legacy CLI route, positional invocation, command name, or existing response field was removed or reordered. Suggested commands are an additive init property. Compare still returns requested/resolved preset, paths/image analysis, degradations, resource bytes, draw/dispatch counts, and truthful timing reports in caller order.

### Godot reference note for the fix

No source was copied. The existing Task 7 references remain the relevant architectural provenance: `F:\Dev\godot-reference\servers\rendering\rendering_device.cpp` (per-frame-slot prior-result retrieval/reset and bounded timestamp capture) reinforced that an unread/in-flight slot is an ownership object, not merely a metric buffer; `F:\Dev\godot-reference\drivers\vulkan\rendering_device_driver_vulkan.cpp` (query-pool ownership/read/reset and timestamp-period conversion) reinforced keeping pool destruction behind a completed device lifecycle. Rekall's explicit fence/idle/device-loss seam and retained full-frame state are independent implementations tailored to this renderer.

### Exact GREEN verification

All test commands were sequential. No project-wide suites contended for publish outputs.

| Command | Exact result |
|---|---|
| Same four-class filter as RED at `D:\RekallAgeTask7FixGreen1` | 71/71 passed, 0 failed, 0 skipped; test duration 10 s; command wall time 16.0 s. |
| `VulkanHighFidelityCaptureTests|VulkanParticleCaptureTests|VulkanGpuProfilerTests|CaptureRuntimeViewportCommandTests|RuntimeInspectCliTests|McpAgentToolExecutorTests|McpCatalogTests|ScenePerformanceBudgetCommandTests|RenderQualityProfileTests|HighFidelityRenderGraphTests` at `D:\RekallAgeTask7FixRegression` | 140/140 passed, 0 failed, 0 skipped; test duration 36 s; command wall time 40.3 s. |
| `dotnet build Rekall.AGE.sln --no-restore -m:1 --verbosity:minimal` at `D:\RekallAgeTask7FixBuild` | Build succeeded; 0 warnings, 0 errors; MSBuild elapsed 7.93 s; command wall time 8.4 s. |
| `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --no-build --verbosity minimal` at `D:\RekallAgeTask7FixFull` | 1,874/1,874 passed, 0 failed, 0 skipped; test duration 4 m 27 s. |

Every focused, adjacent, and full-core verification gate completed with zero failures and zero skips. `git diff --check` reported no whitespace errors; only Git's existing LF-to-CRLF working-copy notices were emitted.

### Process, temp, and residual status

- Implementation commit: `5e445c2`. Evidence-report commit is recorded in the handoff because a commit cannot contain its own final hash.
- No `dotnet`, `testhost`, or `vstest.console` process whose command line referenced this worktree/test project remained after verification.
- The exact five fix-round roots `D:\RekallAgeTask7FixRed`, `D:\RekallAgeTask7FixGreen1`, `D:\RekallAgeTask7FixRegression`, `D:\RekallAgeTask7FixBuild`, and `D:\RekallAgeTask7FixFull` remain. Their resolved absolute paths matched the intended allow-list, but the validated PowerShell `Remove-Item -LiteralPath ... -Recurse -Force` process was rejected by execution policy before launch; no partial deletion occurred. The earlier seventeen Task 7 test roots also remain as already reported.
- If both a fence wait and repeated device-idle recovery return non-device-loss failures, the poisoned native context intentionally retains/leaks its device and children rather than risk destroying in-use Vulkan objects. This is the safe terminal behavior for an unprovable driver state; a later capture retries recovery when the session remains live.
- The review's three Minor items remain intentionally deferred: general ownership-aware capture-command/registry shutdown, explicit `resolved=unavailable` for unsupported compare preset diagnostics, and broader MCP quality-override execution coverage beyond this capture behavior.
- Task 10 still owns the High 2,560x1,440/60 over 600 representative GPU-timestamped frames acceptance gate.
