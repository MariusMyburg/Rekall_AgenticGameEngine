# Task 8 Report: Studio High-Fidelity Authoring Surface

Date: 2026-08-25  
Base commit: `50a136da9130da68a81a01b46174c630addf8961`  
Branch: `codex/high-fidelity-forward-plus`  
Implementation commit: pending at initial report authoring; recorded in the final evidence commit and handoff.

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
| `git diff --check` before commit | No whitespace errors; only Git's existing LF-to-CRLF conversion notices. |

Final verified tests total 79 passed, 0 failed, 0 skipped across the required focused and full Studio gates.

## Commits, process, cleanup, and concerns

- Implementation commit: pending at initial report authoring; final hash is recorded in the follow-up evidence commit and handoff.
- No Task 8 `dotnet`, `testhost`, or `vstest` process remains after verification (`TASK8_PROCESS_COUNT=0`).
- Seventeen exact `D:\RekallAgeTask8*` roots were enumerated and every resolved path was validated against the dedicated top-level pattern before cleanup. The PowerShell-native recursive removal was rejected before process creation by execution policy, so all 17 test roots remain and no partial deletion occurred. This is external temp residue, not worktree residue.
- The final worktree cleanliness and evidence-report commit are recorded in the handoff after commits are created.
- No functional concerns remain. Hardware-populated timing-panel visual QA is deferred; typed tests cover the populated contract and unavailable hardware remains explicit rather than fabricated.
