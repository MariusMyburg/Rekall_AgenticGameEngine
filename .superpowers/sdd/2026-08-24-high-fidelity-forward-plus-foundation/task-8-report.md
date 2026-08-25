# Task 8 Report: Studio High-Fidelity Authoring Surface

Date: 2026-08-25
Base commit: `50a136da9130da68a81a01b46174c630addf8961`
Branch: `codex/high-fidelity-forward-plus`
Implementation commit: `114a2cccc5b0fe856f92e66af1d6d3569ffa841a`

## Outcome

Task 8 is implemented. Rekall AGE Studio now exposes the existing backend-neutral high-fidelity quality, timing, resource, degradation, debug-capture, and comparison contracts. The Output workspace has a compact Rendering tab with three explicit regions: authored quality intent, resolved/runtime facts, and diagnostics/captures.

The selector contains exactly `Performance`, `Low`, `Medium`, `High`, `Ultra`, and `Epic`. Authored controls never synthesize a second quality plan. Studio passes typed `RekallAgeRenderQualityOverrides` into the same capture/comparison operations used by agents and projects typed `RekallAgeResolvedRenderFeaturePlan`, `RekallAgeGpuFrameTimingReport`, capture, and comparison results into immutable presentation records. Requested and resolved presets remain separate.

No gameplay or simulation state is mutated by a quality change. Persistent authoring changes target only `Rekall.RenderQualityProfile` through the generic component command registry. Caller-scoped capture/compare overrides change rendering work only. A behavior regression serializes and reloads an unrelated agent-owned `Game.RuntimeState` component and proves its score and active values are unchanged.

## Read-model architecture

- `RekallAgeWorkbenchModel.Rendering` owns immutable authored, runtime, comparison, debug-view, timing, resource, and degradation presentation records.
- Opening a scene projects authored intent from the runtime world's existing `QualityProfiles`. It deliberately leaves resolved/runtime facts unavailable until a shared rendering command returns evidence; Studio does not run a hidden resolver or guess device support.
- `RekallAgeWorkbenchModelBuilder.BuildRenderingRuntime` maps the exact resolved feature plan, ordered GPU passes, total GPU time, resource bytes, draw/dispatch counts, degradation facts, and bounded suggested actions.
- `WithCaptureResult` and `WithQualityComparisonResult` consume the concrete typed Task 7 results. They do not parse CLI text. Capture debug paths come directly from the returned final, shadow-cascade, fog-slice, and particle debug-capture records. Comparison ordering and paths remain the operation's deterministic caller order.
- Available timing formats nullable GPU totals to three invariant-culture milliseconds. Unavailable timing stays nullable and displays exactly `Unavailable`; it is never converted to `0`, CPU time, or an empty string.
- Recommended workbench actions now include the existing shared commands `rekall.render.capture_runtime_viewport`, `rekall.render.compare_quality_presets`, and `rekall.render.performance.inspect_scene_budget`.

## Studio bindings and generic mutation path

The Rendering tab binds the preset selector and override expander to authored fields, and binds the runtime panel to requested/resolved preset, output/internal resolution, total GPU time, workload, ordered pass timings, resources, timing code/provenance, degradations, comparisons, debug views, and suggested actions. Apply, capture, and compare buttons are bound to executable view-model commands; the WPF acceptance test resolves the real controls and verifies the command instances and preset binding.

Mutation flow is:

```text
Studio control
  -> RekallAgeWorkbenchSession.ExecuteAsync
  -> rekall.component.add / set_property / remove_property
  -> Rekall.RenderQualityProfile in the scene transaction log
  -> ordinary runtime projection and shared render commands
```

Attach uses `rekall.component.add`. Apply uses only `rekall.component.set_property` and `rekall.component.remove_property` for preset and optional overrides. Capture uses `rekall.render.capture_runtime_viewport`; compare uses `rekall.render.compare_quality_presets`. No Studio-only renderer state, alternate preset table, hidden quality resolver, or scene JSON rewrite was added.

The visual acceptance run exposed a pre-existing shared contract gap: runtime projection already consumed `Rekall.RenderQualityProfile`, but the reserved built-in component catalog/schema did not declare it, so generic add rejected the exact reserved type. The generic repair registers `RekallAgeRenderQualityProfileComponent` with the exact preset values and runtime-consumed property names, and adds `Rekall.RenderQualityProfile` to the reserved catalog. This makes Studio and agents equivalent through the same schema/admission path; it is not a Studio exception.

## Unavailable, clamped, and degraded behavior

- Initial or unsupported GPU timing carries `REKALL_GPU_TIMESTAMPS_UNAVAILABLE`, nullable totals, no fabricated passes, provenance `unavailable`, and visible text `Unavailable`.
- Requested and resolved preset fields remain distinct. An unavailable plan shows the authored requested preset and resolved `Unavailable`, not a duplicated requested value.
- Device clamps, unsupported features, and invalid authored intent retain the renderer's stable code, feature, requested value, resolved value, and message. Studio maps these facts without recomputation.
- Comparison rows retain requested/resolved presets, exact screenshot paths, output/internal dimensions, resource bytes, workload counts, nullable timing, and degradation facts.
- Read-model regressions explicitly fail if unavailable timing becomes zero, requested/resolved presets are conflated, ordered pass timing changes, or degradation requested/resolved facts disappear.

## Strict RED -> GREEN evidence

The required `writing-good-tests.md` guidance and TDD instructions were read before test edits. Tests target observable read-model, command, mutation, binding, and rendered-control behavior rather than private implementation structure.

### RED

1. Main focused RED at exact NTFS root `D:\RekallAgeTask8RedMain`:

   ```powershell
   $env:TEMP='D:\RekallAgeTask8RedMain'
   $env:TMP=$env:TEMP
   $env:REKALL_AGE_TEST_TEMP_ROOT=$env:TEMP
   dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkbenchReadModelTests|FullyQualifiedName~StudioWorkbenchSourceTests|FullyQualifiedName~StudioCliTests"
   ```

   Expected compilation failure: `RekallAgeWorkbenchModel.Rendering` and `RekallAgeWorkbenchModelBuilder.BuildRenderingRuntime` did not exist, and the source/CLI expectations for the Rendering surface and actions were unmet.

2. Studio behavior RED at `D:\RekallAgeTask8RedStudio`: expected compilation failure because the preset/readout properties and Apply command did not exist. A missing fixture namespace import was corrected before accepting this feature RED.

3. Shared schema RED at `D:\RekallAgeTask8SchemaRed`: 0/1 passed. The real attach flow returned `Unknown reserved component 'Rekall.RenderQualityProfile'`; the assertion failed at the point where Apply should become executable. After the generic built-in schema/catalog repair, the same test passed 1/1.

4. Full-suite visual-harness RED at `D:\RekallAgeTask8StudioPreflight`: 67/68 passed. The new visual fact created a second WPF `Application` in the same AppDomain when run with the existing modeling visual fact. Both checks were placed inside the existing single STA application lifecycle; the combined visual test then passed and the final suite exited normally. No product assertion was weakened.

### GREEN milestones

- Read-model mapping and unavailable behavior: 9/9 passed.
- Focused main Task 8 tests during development: 12/12 passed.
- Generic attach schema behavior: 1/1 passed.
- Generic mutation/gameplay isolation behavior: 1/1 passed.
- Combined rendered Rendering/modeling workspace acceptance: 1/1 passed.
- Final exact Task 8 filter: 12/12 passed.
- Final full Studio suite: 67/67 passed.

## Visual QA evidence and limitation

`StudioModelingGraphRenderingTests` launches the real WPF application on one STA thread, creates a project/entity, attaches the shared quality component, opens the Rendering tab, resolves the real preset/apply/capture/compare controls, validates their bindings and command instances, checks explicit unavailable timing, renders at 1500x940, and writes:

`F:\Dev\Rekall_AGE\.worktrees\high-fidelity-forward-plus\artifacts\studio-acceptance\rendering-workbench.png`

The final PNG exceeded the 20,000-byte nontrivial-render threshold and was inspected at original resolution. It shows the authored/resolved/diagnostic columns, keyboard-readable labels, High requested versus Unavailable resolved, explicit unavailable total GPU time and stable timing code, an enabled Apply control, and successful `Rekall.RenderQualityProfile` attachment in the inspector/status line. The first inspected render caught the missing built-in schema through its visible error status; the final render contains no such error.

Truthful limitation: this visual run validates the attached-authoring and unavailable-diagnostics state, not a hardware-populated Vulkan timing list. Populated plans, ordered passes, resources, comparisons, paths, and degradations are covered by typed read-model tests. The ignored artifact is regenerated by the acceptance test and is not committed.

## Godot and Blender reference notes

No source was copied. The local reference checkouts are sparse; exact omitted files were read from their local Git objects with `git show HEAD:<path>`.

Godot editor references:

- `F:\Dev\godot-reference\editor\inspector\editor_inspector.cpp`: the inspector derives editable property controls from declared property metadata, groups authored values, filters unavailable/high-end fields, and distinguishes read-only presentation. The reusable idea was to keep authored profile controls schema-driven and separate from runtime diagnostic facts.
- `F:\Dev\godot-reference\editor\debugger\editor_visual_profiler.cpp`: frame metrics are retained in ordered history, invalid metric slots are skipped, and CPU/GPU series and display units remain explicitly labeled. The reusable ideas were an ordered pass list, an explicit no-data state, and no substitution of CPU duration for absent GPU evidence.
- The design specification's renderer references, especially `F:\Dev\godot-reference\servers\rendering\renderer_rd\renderer_scene_render_rd.*` and `F:\Dev\godot-reference\servers\rendering\storage\environment_storage.*`, reinforced the authored-state versus resolved/device-resource boundary. Studio therefore displays the renderer's resolved plan instead of resolving quality itself.

Blender/EEVEE references:

- `F:\Dev\blender-reference\source\blender\draw\engines\eevee\eevee_engine.cc`: viewport/final-frame execution is routed through the EEVEE `Instance` lifecycle. The reusable idea was one renderer contract for capture/comparison rather than a Studio-specific render path.
- `F:\Dev\blender-reference\source\blender\draw\engines\eevee\eevee_instance.hh`: film, render buffers, pipelines, shadows, volumes, probes, and debug scopes are explicit modules owned by one instance. The reusable idea was to present distinct pass/resource/debug facts without turning those facts into authored settings.
- `F:\Dev\blender-reference\source\blender\draw\engines\eevee\eevee_film.hh`: display, film, and internally scaled render extents are separate, and enabled passes are explicit. The reusable ideas were separate output/internal resolution labels and explicit debug/pass selection.

## Final verification

All final commands ran sequentially with `MSBUILDDISABLENODEREUSE=1`; tests/build used exact dedicated NTFS roots under `D:`. No publish or test processes contended for outputs.

| Command | Exact result |
|---|---|
| Exact brief filter at `D:\RekallAgeTask8FocusedFinal` | 12/12 passed, 0 failed, 0 skipped; test duration 372 ms; command wall time 4.9 s. |
| Full `Rekall.Age.Studio.Tests` suite at `D:\RekallAgeTask8StudioFinal` | 67/67 passed, 0 failed, 0 skipped; test duration 58 s; command wall time 55.9 s. |
| `dotnet build Rekall.AGE.sln --no-restore -m:1 --verbosity:minimal` at `D:\RekallAgeTask8BuildFinal` | Succeeded; 0 warnings, 0 errors; MSBuild elapsed 7.43 s; command wall time 8.8 s. |
| Adjacent reserved catalog/schema regression in preflight | 13/13 passed including the 12 Task 8 tests; 0 failed, 0 skipped. |
| `git diff --check` before the final evidence commit | No whitespace errors; only Git's existing LF-to-CRLF conversion notices. |

Final verified tests total 79 passed, 0 failed, 0 skipped across the required focused and full Studio gates.

## Commits, process, cleanup, and concerns

- `114a2cccc5b0fe856f92e66af1d6d3569ffa841a` - `feat: author scalable rendering in Studio`.
- No Task 8 `dotnet`, `testhost`, or `vstest` process remains after verification (`TASK8_PROCESS_COUNT=0`).
- Seventeen exact `D:\RekallAgeTask8*` roots were enumerated and every resolved path was validated against the dedicated top-level pattern before cleanup. The PowerShell-native recursive removal was rejected before process creation by execution policy, so all 17 test roots remain and no partial deletion occurred. This is external temp residue, not worktree residue.
- The final worktree cleanliness and evidence-report commit are recorded in the handoff after commits are created.
- No functional concerns remain. Hardware-populated timing-panel visual QA is deferred; typed tests cover the populated contract and unavailable hardware remains explicit rather than fabricated.

## Fix round 1: retained evidence, lifecycle cancellation, and exact debug facts

Date: 2026-08-25
Implementation commit: `871c92b937e7b419ab5e6a8ca0788267e7916c17`

### Root causes and architecture repair

1. Rendering evidence was transient ViewModel state. `RunAsync` rebuilt `_currentModel` from the canonical `RekallAgeWorkbenchSession.Model`, whose ordinary builder path contains authored intent but no command evidence. Selection, reload, or a later command therefore erased timing, comparisons, debug views, and recommendations. In addition, `RunAsync` only applied typed result values when `result.Ok` was true, so a usable partial comparison was dropped with its accompanying error.

   The session now owns the typed rendering presentation snapshot for the exact normalized `(projectRoot, sceneName)` scope. Capture/comparison values are applied to `session.Model` before the failure return, provided they contain usable typed evidence. Selection and reload merge that snapshot back into the canonical model while taking fresh authored intent from the rebuild. A scene change clears the scoped evidence rather than caching it for a later return. Successful mutations invalidate it when they touch the active scene or another potentially rendering-relevant project resource; mutations for a different scene and generated `Artifacts`/`Builds` output do not leak or spuriously replace the active evidence. Undo and redo restoration also invalidate it.

   Studio now always applies the canonical session model when one is available, including on a failed operation with partial typed evidence. It presents the stable command error beside the retained comparison rather than maintaining or recomputing a second ViewModel-only result.

2. Vulkan quality capture/comparison passed `CancellationToken.None` to the shared session. `DisposeAsync` canceled the lifecycle token but immediately stopped/disposed preview dependencies without knowing about active rendering work.

   Capture, quality capture, and quality comparison now enter one `RunRenderingAsync` path. Each operation receives a CTS linked to Studio lifecycle cancellation and passes its token unchanged through `RekallAgeWorkbenchSession.ExecuteAsync` into the generic command context. Active rendering tasks are registered under a lock before the caller can race disposal, unregistered on completion, and snapshotted after lifecycle cancellation. Disposal awaits the snapshot before stopping or disposing preview/dependencies. Lifecycle `OperationCanceledException` is handled as expected shutdown and does not produce `REKALL_STUDIO_UNEXPECTED_FAILURE`.

3. Comparison presentation discarded `RekallAgeQualityPresetCapture.NonBlank`: the comparison record had no field and generated debug rows hard-coded `true`.

   `RekallAgeWorkbenchRenderQualityComparisonModel` now carries `NonBlank`. The builder maps the capture's literal value into both the comparison record and its final-output debug view. A false capture remains false end to end.

The deferred reviewer minors remain deliberately out of scope: stale/missing debug bitmap load behavior, deep immutable collection snapshots, and broader Studio end-to-end coverage beyond these Important regressions were not broadened into this fix.

### Fix-round RED -> GREEN evidence

The systematic-debugging and test-driven-development guidance plus `writing-good-tests.md` were reread before test edits. The new tests use deterministic typed commands and synchronization signals; none depends on a genuinely slow Vulkan device.

#### RED

1. Session evidence RED at exact NTFS root `D:\RekallAgeTask8Fix1EvidenceRed`:

   ```powershell
   $env:TEMP='D:\RekallAgeTask8Fix1EvidenceRed'
   $env:TMP=$env:TEMP
   dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkbenchRenderingEvidenceSessionTests" --logger "console;verbosity=minimal"
   ```

   Result: 0/3 passed. Selection returned `Unavailable` instead of the captured `3.250 ms`; scene/session presentation had no retained `High` evidence; and the partial-failure comparison collection was empty.

2. Studio partial-result and lifecycle RED at `D:\RekallAgeTask8Fix1StudioRed`:

   ```powershell
   $env:TEMP='D:\RekallAgeTask8Fix1StudioRed'
   $env:TMP=$env:TEMP
   dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~PartialComparisonFailureKeepsUsableTypedEvidenceVisibleWithItsError|FullyQualifiedName~DisposeCancelsAndAwaitsActiveQualityCaptureBeforePreviewDependencies" --logger "console;verbosity=minimal"
   ```

   Result: 0/2 passed, duration 929 ms. The partial comparison collection was empty. In the deterministic lifecycle test, preview disposal was observed before the command token was canceled, proving both the missing token link and missing disposal coordination.

3. Comparison diagnostic RED at `D:\RekallAgeTask8Fix1NonBlankRed`:

   ```powershell
   $env:TEMP='D:\RekallAgeTask8Fix1NonBlankRed'
   $env:TMP=$env:TEMP
   dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkbenchQualityComparisonUsesExactCommandResultPathsAndDegradationFacts" --logger "console;verbosity=minimal"
   ```

   Result: expected compile failure, 0 tests run. `CS1061` at lines 226 and 230 reported that `RekallAgeWorkbenchRenderQualityComparisonModel` had no `NonBlank` definition. This precisely exposed the missing presentation fact before implementation.

#### GREEN milestones

- Session ownership/scoping behavior at `D:\RekallAgeTask8Fix1EvidenceGreen`: 3/3 passed, 0 failed, duration 245 ms.
- Partial result plus lifecycle ordering at `D:\RekallAgeTask8Fix1StudioGreen`: 2/2 passed, 0 failed, duration 308 ms.
- Exact false-`NonBlank` mapping at `D:\RekallAgeTask8Fix1NonBlankGreen`: 1/1 passed, 0 failed, duration 102 ms.
- Focused core/CLI/source fix gate at `D:\RekallAgeTask8Fix1FocusedCore`: 7/7 passed, 0 failed, duration 1 s. This included the three session facts, exact comparison mapping, `StudioWorkbenchSourceTests`, and `StudioCliTests`.
- Focused Studio quality/lifecycle gate at `D:\RekallAgeTask8Fix1FocusedStudio`: 5/5 passed, 0 failed, duration 830 ms. This included partial comparison, deterministic cancellation/disposal ordering, generic quality attachment/mutation, gameplay-state isolation, and repeat-dispose coordination.

### Final regression totals and audits

All gates ran sequentially; no publish or test process contended for outputs.

| Command | Exact result |
|---|---|
| Focused core/CLI/source fix gate | 7/7 passed, 0 failed, 0 skipped; duration 1 s. |
| Focused Studio quality/lifecycle gate | 5/5 passed, 0 failed, 0 skipped; duration 830 ms. |
| Full `Rekall.Age.Studio.Tests` at `D:\RekallAgeTask8Fix1FullStudio` | 69/69 passed, 0 failed, 0 skipped; duration 59 s. |
| `dotnet build Rekall.AGE.sln --no-restore --verbosity minimal -m:1` at `D:\RekallAgeTask8Fix1SolutionBuild` | Succeeded; 0 warnings, 0 errors; elapsed 6.67 s. |
| `git diff --check` before implementation commit | No whitespace errors; only the repository's LF-to-CRLF conversion notices. |

Final required regression total: 81 passed, 0 failed, 0 skipped across the focused core, focused Studio, and full Studio gates. The solution build has 0 warnings and 0 errors.

No new visual claim is made for this fix round. The committed changes affect evidence ownership, cancellation/disposal ordering, and an exact boolean mapping; those paths were verified deterministically. The previously recorded WPF rendering acceptance remains the Task 8 visual evidence, with its already stated hardware-timing limitation.

### Commit, process, temp, and concerns

- `871c92b937e7b419ab5e6a8ca0788267e7916c17` - `fix: retain Studio rendering evidence safely`.
- After verification and commit, `TASK8_FIX_PROCESS_COUNT=0`: no `dotnet`, `testhost`, or `vstest.console` process remained.
- Ten exact `D:\RekallAgeTask8Fix1*` roots were enumerated and validated against the dedicated prefix. A PowerShell-native recursive cleanup command was rejected before process creation by execution policy, so all ten remain intact. Together with the 17 previously reported Task 8 roots, this is external temp residue only; no worktree file is affected.
- Functional concerns: none in the Important scope. The three explicitly deferred minors and the hardware-populated timing-panel visual limitation remain recorded technical follow-up.

## Fix round 2: failed-capture evidence and generated-output isolation

Date: 2026-08-25
Implementation commit: `dd80d5b14bca9fbce54fee047e99d050da2c440a`

### Root cause and repair

The remaining Important finding had two coupled causes in `RekallAgeWorkbenchSession`:

1. `ApplyUsableRenderingResult` only admitted `CaptureRuntimeViewportResult` when both `Captured=true` and `QualityPlan` was present. Native Vulkan failure results truthfully set `Captured=false`, but can still carry a resolved feature plan, GPU timing report, resource/workload facts, degradations, suggested actions, and stable command errors. The predicate discarded all those usable typed facts. The session now admits any capture with a non-null resolved plan, preserves the operation's `Ok=false` result and error code, and retains that evidence through harmless selection/rebuilds. `BuildDebugViews` separately requires `Captured=true`, so a failed result with a non-empty prospective screenshot path never invents a final image or debug capture.

2. Successful typed evidence was applied before transaction invalidation. The path heuristic treated any changed resource outside the active scene, another scene, `Artifacts`, or `Builds` as authored rendering state. Capture/compare commands record their generated screenshot paths in the transaction, so default relative `QualityCaptures` output and valid custom external directories immediately cleared the evidence that produced them. Invalidation now distinguishes the two output-only evidence commands (`rekall.render.capture_runtime_viewport` and `rekall.render.compare_quality_presets`) from authored mutations. Their recorded generated outputs never invalidate their own evidence regardless of directory. Other commands still use the existing resource-scope logic: a real `rekall.render.plan.create` mutation clears evidence, the current-scene component mutation regression remains green, and switching scenes still clears the scope permanently.

No Studio-only state or alternate quality resolution was introduced. The fix remains in the shared session/read-model path consumed by agents and Studio. The previously deferred Minors were not broadened.

### Strict RED -> GREEN evidence

The systematic-debugging, test-driven-development, verification-before-completion, and `writing-good-tests.md` instructions were read before edits. The tests use real `RekallAgeWorkbenchSession` behavior with deterministic typed commands and a real render-plan mutation command.

#### RED

1. Partial failed capture at `D:\RekallAgeTask8Fix2PartialCaptureRed`:

   ```powershell
   $env:TEMP='D:\RekallAgeTask8Fix2PartialCaptureRed'
   $env:TMP=$env:TEMP
   dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~FailedCaptureRetainsTypedRuntimeEvidenceWithoutInventingDebugImagery" --logger "console;verbosity=minimal"
   ```

   Result: 0/1 passed, duration 146 ms. The typed failed result requested `Epic` and resolved `High`, but the session still presented authored `High` as requested. This was the expected evidence-discard failure; the result itself remained `Ok=false` with `REKALL_TEST_NATIVE_CAPTURE_FAILED`.

2. Generated output paths at `D:\RekallAgeTask8Fix2GeneratedOutputRed`:

   ```powershell
   $env:TEMP='D:\RekallAgeTask8Fix2GeneratedOutputRed'
   $env:TMP=$env:TEMP
   dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~SuccessfulComparisonRetainsEvidenceWhenDefaultQualityCapturesAreTransactionOutputs|FullyQualifiedName~SuccessfulCaptureRetainsEvidenceWhenCustomOutputDirectoryIsOutsideProject" --logger "console;verbosity=minimal"
   ```

   Result: 0/2 passed, duration 207 ms. The external capture's resolved preset was erased to `null`; the default `QualityCaptures` comparison list was erased from two rows to zero.

#### GREEN

- Partial failed capture at `D:\RekallAgeTask8Fix2PartialCaptureGreen`: 1/1 passed, 0 failed, duration 143 ms. It verifies requested/resolved quality, `3.250 ms` timing, draw/dispatch counts, frame bytes, stable degradation facts, suggested action, retained selection refresh, original failure/error status, and zero debug/comparison imagery.
- Generated output plus authored-render guard at `D:\RekallAgeTask8Fix2GeneratedOutputGreen`: 3/3 passed, 0 failed, duration 249 ms. Both output locations retain evidence, while the real render-plan mutation invalidates it.
- Complete session evidence class at `D:\RekallAgeTask8Fix2SessionFocused`: 7/7 passed, 0 failed, duration 371 ms, including current-scene and scene-change invalidation.

### Final regression totals and concerns

All final gates ran sequentially with no competing publish/test process.

| Command | Exact result |
|---|---|
| Focused Task 8 core/CLI/source gate at `D:\RekallAgeTask8Fix2FocusedCore` | 11/11 passed, 0 failed, 0 skipped; duration 1 s. |
| Full `Rekall.Age.Studio.Tests` at `D:\RekallAgeTask8Fix2FullStudio` | 69/69 passed, 0 failed, 0 skipped; duration 57 s. |
| `dotnet build Rekall.AGE.sln --no-restore --verbosity minimal -m:1` at `D:\RekallAgeTask8Fix2SolutionBuild` | Succeeded; 0 warnings, 0 errors; elapsed 6.66 s. |
| `git diff --check` before implementation commit | No whitespace errors; only repository LF-to-CRLF conversion notices. |

Final required regression total: 80 passed, 0 failed, 0 skipped across the focused Task 8 and full Studio gates. `TASK8_FIX2_PROCESS_COUNT=0` after verification.

Concerns: none in the remaining Important scope. Eight dedicated `D:\RekallAgeTask8Fix2*` roots remain because the same execution policy that blocked prior Task 8 cleanup still rejects recursive removal before process creation; this brings documented external Task 8 temp residue to 35 roots. Deferred Minors and the previously stated hardware-populated visual limitation remain unchanged.
