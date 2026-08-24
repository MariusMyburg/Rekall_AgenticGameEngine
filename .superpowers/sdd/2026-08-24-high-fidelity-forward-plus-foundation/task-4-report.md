# Task 4 Report: Stabilized Cascaded Directional Shadows

## Status

`DONE_WITH_CONCERNS`

Task 4 is implemented on `codex/high-fidelity-forward-plus`. The high-fidelity Vulkan path now plans one-to-four deterministic stabilized directional cascades, allocates and renders a sampled `D32_SFLOAT` depth array, samples the selected cascade during the graph-authoritative HDR opaque pass, and reports the real allocation and workload. The compatibility shader/pipeline remains unchanged when the shadow plan is disabled.

The required shadow/Vulkan filter, native visual acceptance, the complete rendering test namespace, build, formatting, and diff gates pass. The repository-wide test command retains the same eight pre-existing Windows AppContainer module-host isolation failures documented by Task 3; no rendering test fails.

## TDD evidence

### Baseline

Before Task 4 tests or production changes:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanShadowCascadePlannerTests|FullyQualifiedName~VulkanScene"
```

Output: `Passed: 89, Failed: 0, Skipped: 0`.

### Planner RED

The planner test was written first with deterministic practical splits, exact preset counts/resolutions/taps, finite matrices, depth-array viewports, masks/caster intent, exact sub-texel matrix equality, and the required invalid-camera diagnostic.

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanShadowCascadePlannerTests"
```

Initial output: build failed with `CS0246` for the deliberately absent `RekallAgeVulkanShadowCamera`, `RekallAgeVulkanDirectionalShadowLight`, and `RekallAgeVulkanShadowCaster` contracts. After the minimum API compiled, the same command produced three genuine behavioral failures: the asserted third practical split exposed an incorrect expected literal, the mask fixture incorrectly assumed a near caster occupies every depth cascade, and the exact `Matrix4x4` stabilization assertion exposed changing translation terms after a `0.0001` camera move.

The test literal was independently recalculated to `30.7863`, the mask assertion was corrected to distinguish eligibility from per-cascade depth membership, and production stabilization was fixed by snapping the cascade center in a fixed light-orientation basis. The image assertion was not involved or weakened.

GREEN output for the original planner group was `Passed: 8, Failed: 0`. Subsequent strict RED/GREEN additions proved:

- fitted light-frustum caster culling: RED returned `inside, outside`; GREEN returned only `inside` with one per-cascade culled caster;
- degenerate camera basis: RED `Expected False, Actual True`; GREEN `Passed: 1, Failed: 0` with `REKALL_SHADOW_CAMERA_INVALID`;
- orthographic projection: RED `CS0117` for absent `ProjectionMode`/`OrthographicSize`; GREEN `Passed: 1, Failed: 0` with finite cascades even when perspective FOV is not finite.

### Native visual RED and GREEN

`DirectionalShadowsAllocateDrawSampleAndDarkenNativePixels` was added before the Vulkan implementation. Its first run failed to compile because `RekallAgeHighFidelityFrameReport.ShadowCascades` and runtime shadow intent did not exist. Once the path compiled, it reached real Vulkan execution and failed twice on workload evidence: first the pass reported only two draws against the three-draw fixture requirement, then cascade zero reported no caster. The fixture gained representative near/main/far casters; no output assertion was weakened.

Final native/capability command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~DirectionalShadowsAllocateDrawSampleAndDarkenNativePixels|FullyQualifiedName~ShadowDepthAtlasRequiresSamplingAndFitsDeviceArrayLimits"
```

Output: `Passed: 2, Failed: 0, Skipped: 0` in 1 second.

The native test performs two 256x192 captures differing only in directional `castShadows`. It requires a real allocated `shadow-directional` `D32_SFloat` resource at 2048x2048, an executed depth pass with at least three draws, the HDR opaque pass reading the shadow resource, three cascade reports at 12 filter taps, distinct checksums, and more than 48 visibly darkened receiver pixels.

Capability/limit hardening also followed RED/GREEN. RED was `CS0117` for absent `ValidateShadowDepthFormat` and `ValidateShadowAtlasLimits`; GREEN was `Passed: 1, Failed: 0`. Native allocation now preflights depth attachment, sampled image, linear filtering, maximum 2D resolution, and maximum array layers. It returns `REKALL_RENDER_FORMAT_UNSUPPORTED` or `REKALL_SHADOW_ATLAS_LIMIT_EXCEEDED` before allocation with requested and supported values.

### Integration regression RED and GREEN

The first complete rendering namespace run after extending the frame UBO failed five existing `RenderingDeviceSceneRendererTests` with `Buffer write data must be nonempty and contained by the resource.` The generic rendering-device adapter still hard-coded a 128-byte frame uniform while the shadow matrices made the actual interop struct larger.

Production was fixed to derive both frame and draw uniform byte sizes from `Marshal.SizeOf`. The focused adapter rerun passed `6/6`; the first complete rendering rerun passed `557/557`.

## Final verification

Required brief command after all planner corrections:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanShadowCascadePlannerTests|FullyQualifiedName~VulkanScene"
```

Output: `Passed: 100, Failed: 0, Skipped: 0` in 2 seconds.

Complete rendering namespace after the final orthographic correction:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~Rekall.Age.Tests.Rendering"
```

Output: `Passed: 561, Failed: 0, Skipped: 0` in 3 seconds.

Build and formatting:

```powershell
dotnet build src\Rekall.Age.Rendering\Rekall.Age.Rendering.csproj --no-restore
dotnet format Rekall.AGE.sln --no-restore --verify-no-changes --include <all changed C# files>
git diff --check
```

Outputs: build succeeded with `0 Warning(s), 0 Error(s)`; formatting and diff checks exited 0. Git printed only the repository's LF-to-CRLF conversion notices.

Repository-wide evidence:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore
```

Output: `Failed: 8, Passed: 1759, Skipped: 0, Total: 1767` in 3 minutes 50 seconds. All eight failures are the existing `ModuleHostWindowsIsolationTests` AppContainer worker startup/protocol failures (`host.initialize`, truncated frame, or transport `EndOfStreamException`). This run preceded only the isolated orthographic planner correction; the complete affected Rendering namespace was rerun afterward and is green at 561/561.

## Implementation

- Added a pure backend-neutral cascade planner with practical 0.65 logarithmic/linear split weighting, perspective and orthographic frustum construction, finite/degenerate validation, deterministic light selection inputs, stable light-basis texel snapping, finite view-projection matrices, depth-array layer viewports, and one-to-four resolved cascades.
- Caster selection respects cast intent, camera/light receiver masks, light caster mask, maximum shadow distance, camera depth range, transformed world bounds, and fitted light-frustum bounds. Entity ordering and directional-light priority tie-breaking are deterministic.
- Extended generic runtime renderable, mesh, batch, draw, command, and uniform contracts with cast/receive intent, layer masks, bias/normal bias, maximum distance, priority, alpha mode, and alpha cutoff without adding gameplay or genre behavior.
- Added a depth-only Vulkan pipeline and shaders. Shadow alpha masks sample the existing base-color descriptor and discard below the authored cutoff; blended/transparent draws remain outside the opaque depth path.
- Added real Vulkan depth-array image/memory/view/layer views, render pass/framebuffers, per-cascade UBOs/descriptors, compare sampler, pipeline layout/pipeline, command recording, layout dependencies, scene sampling descriptor, and dependency-safe destruction.
- Extended scene frame uniforms with four cascade matrices/splits, camera-forward depth selection, resolution, bias/normal bias, filter taps, maximum distance, and enabled state. Scene fragment shading performs preset-controlled one-to-24-tap PCF and applies visibility only to opted-in receivers.
- Kept render-graph authority: the validated graph still determines HDR/post resources and pass order; the actual shadow pass precedes `opaque-hdr`, the latter reports `shadow-directional` as an input, and the existing no-shadow compatibility shader does not require set 3.
- Reports now expose allocated atlas format/extent, executed pass draws, and per-cascade split range, resolution, caster count, actual selected draw count, per-cascade culled count, filter taps, atlas bytes, depth bias, and normal bias.
- Added truthful pre-allocation validation for D32 sampled linear depth support and device image/array limits. Invalid camera/light plans degrade to an inspectable disabled shadow plan with stable codes instead of producing non-finite matrices or making false allocation claims.

## Native and visual evidence

Final visually inspected pair:

- shadowed: `C:\Users\Marius\AppData\Local\Temp\rekall-age-tests\a38ddd0f91744def96e878809db5bc21\vulkan-scene-256x192-20260824184909482.png`
- same authored frame with the directional light's shadow casting disabled: `C:\Users\Marius\AppData\Local\Temp\rekall-age-tests\a38ddd0f91744def96e878809db5bc21\vulkan-scene-256x192-20260824184909862.png`

Both were opened at original resolution. The shadowed capture contains a clear dark cast-shadow footprint behind the orange caster on the tan ground; that footprint is absent in the control image. An independent `System.Drawing` pixel comparison measured 119 pixels darkened by at least three summed RGB levels, 157 brightness-different pixels, and a maximum summed-RGB reduction of 331. This independently exceeds the test's strict `>48` receiver-pixel threshold.

## Files

Production contracts/orchestration:

- `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanShadowCascadePlanner.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFrameRenderer.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFormatValidator.cs`
- `src/Rekall.Age.Rendering/RekallAgeRenderingDeviceSceneResources.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneMeshModels.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneMeshBuilder.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneBatch.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneBatchBuilder.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneDrawPlan.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneCommandPlan.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneUniformUpload.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanScenePipelineDescription.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanShaderCompiler.cs`
- `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`

Shaders:

- `src/Rekall.Age.Rendering/Shaders/rekall_shadow.vert`
- `src/Rekall.Age.Rendering/Shaders/rekall_shadow.frag`
- `src/Rekall.Age.Rendering/Shaders/rekall_scene.vert`
- `src/Rekall.Age.Rendering/Shaders/rekall_scene.frag`

Tests:

- `tests/Rekall.Age.Tests/Rendering/VulkanShadowCascadePlannerTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanHighFidelityCaptureTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanShaderCompilerTests.cs`

The approved design and implementation plan were not changed because the executable evidence did not change the approved contract, pass order, or acceptance gate.

## Self-review

- Genericity: the planner consumes camera, directional-light, quality, layer-mask, and world-bound facts only. It has no game, genre, controller, or content-specific behavior.
- Determinism: split math, light selection, caster order, quality clamps, viewports/layers, and diagnostics are stable. Sub-texel camera motion is asserted with exact matrix equality.
- Projection coverage: both perspective and orthographic cameras are explicitly handled; non-finite or parallel bases return `REKALL_SHADOW_CAMERA_INVALID`.
- Graph authority: shadows augment the graph-authoritative high-fidelity path and never replace its resolved extents, HDR target, post pass sequence, or compatibility fallback.
- Native truthfulness: report allocation/pass/cascade claims are derived from enabled plans and recorded work. Failed capability checks return an unexecuted high-fidelity report and do not claim shadow allocation.
- Shader/resource compatibility: the shadow sample descriptor exists only in the shadow-enabled permutation. Existing compatibility and high-fidelity-without-light captures continue using the prior descriptor layout and shader permutation.
- Alpha masking: the depth fragment shader reuses material base-color texture/sampler descriptors and authored alpha cutoff; ordinary opaque primitives avoid unnecessary discard behavior.
- Ownership: every new Vulkan object is state-owned, handle-guarded, and destroyed in dependency-safe order.
- Regression fix: uniform-buffer sizes now derive from actual interop layouts, preventing shadow UBO growth from corrupting the portable rendering-device adapter.

## Concerns and follow-up

- The repository-wide test command remains red only in the existing Windows AppContainer isolation fixture. It is unrelated to rendering but prevents a fully green repository-level status.
- The foundation implements the approved single directional shadow light and conventional depth-array cascades. Punctual-light atlases, cached/static shadow reuse, virtual shadow allocation, contact shadows, and GPU timing remain later milestones.
- Current per-renderable shadow intent is present in the backend-neutral runtime renderable contract and honored through mesh/batch/native execution. Projecting a richer authoring-schema surface for custom numeric layer masks and per-light softness can be expanded with the planned material/light authoring work without changing this execution contract.

## Fix Round 1: Important review findings

All four Important review findings were repaired with isolated RED/GREEN tests. The approved design and implementation plan remain unchanged: these fixes make the already-approved eligibility, selected-light, conservative-culling, and debug-capture contracts executable and truthful without changing graph pass order or acceptance gates.

### 1. Transparent eligibility and recorded workload

RED command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~BlendedRenderableDoesNotCastOrInflateRecordedShadowDrawReports"
```

RED output: `Failed: 1, Passed: 0`; the baseline shadow pass reported 5 draws while the otherwise-identical frame containing one `AlphaMode="blend"` mesh reported 6.

The batch now classifies authored blend materials as transparent, shadow planning excludes them, and command recording plus pass/cascade workload reports share one `IsShadowDrawEligible` predicate. Reports count actual recordable command draws rather than planned caster IDs.

GREEN output for the same command: `Passed: 1, Failed: 0`.

### 2. Shadow-selected light and direct shading

RED command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ShadowSelectedDirectionalLightIsTheDirectLightAttenuatedBySceneShading"
```

Initial RED output: compilation failed with `CS1061` because `RekallAgeVulkanShadowPlan` did not identify its selected light. A strengthened preservation assertion produced a second genuine RED with `CS1061` for absent `AdditionalLightPosition` and `AdditionalLightColor` frame facts.

The deterministic priority-selected directional light entity ID now travels from high-fidelity planning through prepared-frame construction into primary direct-light resolution. A fixture with a point light first, two directional lights, distinct colors, and priorities 10/20 proves that the priority-20 red directional light is both the planned shadow light and the directional direct-light slot (`LightPosition.W == 0`, color `(2,0,0,1)`). The previously supported green point light remains in a separate additional direct-light UBO/shader slot (`position (0,2,3,1)`, color `(0,4,0,1)`), whose BRDF contribution is not multiplied by the directional cascade factor. If the selected directional light was already the legacy primary, the additional slot is disabled to avoid double contribution.

GREEN output for the same command: `Passed: 1, Failed: 0`.

### 3. Conservative low-angle/boundary caster selection

RED command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LowAngleCasterBeforeCascadeBoundaryIsConservativelyIncludedForDownstreamReceivers"
```

Initial RED output: `Failed: 1, Passed: 0`; cascade 1 omitted `boundary-upstream` because camera-forward same-slice depth gating rejected a caster that can shadow downstream receivers along a low-angle directional light. The strengthened 80-unit extrusion fixture then produced a second genuine RED: cascade 0 contained only `boundary-upstream` and omitted `distant-upstream` at z=-70.

Camera-forward depth gating was removed. Each cascade now culls against its fitted light-space receiver bounds extruded upstream by the authored directional shadow maximum distance, with bounded XY fitting and deterministic texel snapping retained.

GREEN command/output:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanShadowCascadePlannerTests"
```

Output: `Passed: 12, Failed: 0, Skipped: 0` in 41 ms, including conservative boundary coverage, outside-frustum culling, and exact stabilization.

### 4. Executable cascade split/depth debug captures

RED command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~DirectionalShadowsAllocateDrawSampleAndDarkenNativePixels"
```

RED output: compilation failed with `CS1061` because the high-fidelity report had no `ShadowDebugCaptures` evidence.

The native path now creates a host-visible readback buffer, copies every actual `D32_SFLOAT` array layer after HDR scene sampling, normalizes occupied depth to a grayscale PNG, and reports each file with its planned cascade index/split range, nonblank fact, and checksum. `D32_SFLOAT` capability validation now truthfully includes `TransferSrcBit`; unsupported devices degrade before allocation under the existing stable format diagnostic.

GREEN output for the same command: `Passed: 1, Failed: 0`. The test requires one output per planned cascade, exact split correspondence, planned resolution, an occupied pixel, and more than one distinct SHA-256 image hash.

### Fix-round native and visual evidence

Combined regression command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~DirectionalShadowsAllocateDrawSampleAndDarkenNativePixels|FullyQualifiedName~BlendedRenderableDoesNotCastOrInflateRecordedShadowDrawReports|FullyQualifiedName~ShadowSelectedDirectionalLightIsTheDirectLightAttenuatedBySceneShading|FullyQualifiedName~LowAngleCasterBeforeCascadeBoundaryIsConservativelyIncludedForDownstreamReceivers" --logger "console;verbosity=normal"
```

Final output after the strengthened light-preservation change: `Passed: 4, Failed: 0, Skipped: 0, Total: 4` in 4 seconds. Both native Vulkan tests executed on the local device.

A final retained native evidence run used `REKALL_AGE_TEST_TEMP_ROOT=C:\Users\Marius\AppData\Local\Temp\rekall-age-task4-fix-evidence` and passed `1/1` in 2 seconds. The three actual 2048x2048 layer visualizations are:

- cascade 0: `C:\Users\Marius\AppData\Local\Temp\rekall-age-task4-fix-evidence\16783f72d3024ccc8c410e71911798fa\vulkan-shadow-cascade-0-20260824193300255.png`, 32,396 bytes, SHA-256 `D20031CC4F6A14B4FA4C4419A22C52DBB52CD68D050693B245747808B8D0B887`;
- cascade 1: `C:\Users\Marius\AppData\Local\Temp\rekall-age-task4-fix-evidence\16783f72d3024ccc8c410e71911798fa\vulkan-shadow-cascade-1-20260824193300255.png`, 25,916 bytes, SHA-256 `CA67378C176AE4963B42F51818C96A85482C7F8DD3CAFA564E3067DD11D3B06A`;
- cascade 2: `C:\Users\Marius\AppData\Local\Temp\rekall-age-task4-fix-evidence\16783f72d3024ccc8c410e71911798fa\vulkan-shadow-cascade-2-20260824193300255.png`, 22,500 bytes, SHA-256 `759590EBED1BD64B772E29E6E074DA088A8E8BCF13BCF78A38476DDD0E85131F`.

All three were opened and inspected. They are nonblank and visibly distinguishable: the same ground and caster silhouettes occupy successively smaller near, middle, and far cascade footprints, with readable intra-surface depth gradients rather than synthetic placeholders.

### Fix-round final verification

Required brief gate:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanShadowCascadePlannerTests|FullyQualifiedName~VulkanScene" --logger "console;verbosity=normal"
```

Output: `Total tests: 102, Passed: 102, Failed: 0` in 2.6292 seconds.

Broader Rendering gate:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~Rekall.Age.Tests.Rendering" --logger "console;verbosity=minimal"
```

Output: `Passed: 564, Failed: 0, Skipped: 0, Total: 564` in 6 seconds.

Build/format/diff gates:

```powershell
dotnet build src\Rekall.Age.Rendering\Rekall.Age.Rendering.csproj --no-restore
dotnet format Rekall.AGE.sln --no-restore --verify-no-changes --include <all fix-round changed C# files>
git diff --check
```

Outputs: build succeeded with `0 Warning(s), 0 Error(s)`; format and diff checks exited 0. Git emitted only the repository's LF-to-CRLF working-copy notices.

### Fix-round files and self-review

Production:

- `src/Rekall.Age.Rendering/RekallAgeVulkanShadowCascadePlanner.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFrameRenderer.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneBatch.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneBatchBuilder.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanScenePreparedFrame.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneUniformUpload.cs`
- `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFormatValidator.cs`
- `src/Rekall.Age.Rendering/Shaders/rekall_scene.vert`
- `src/Rekall.Age.Rendering/Shaders/rekall_scene.frag`

Tests:

- `tests/Rekall.Age.Tests/Rendering/VulkanHighFidelityCaptureTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanSceneBatchBuilderTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanShadowCascadePlannerTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanShaderCompilerTests.cs`

Self-review found no remaining Important-round blocker: graph authority and compatibility fallback are unchanged; blend/mask/opaque distinctions remain generic; light selection uses only priority and stable entity-ID tie-breaking; conservative extrusion remains bounded by authored shadow distance and fitted XY receiver coverage; workload evidence derives from the same predicate as recording; readback resources are state-owned and destroyed; and debug evidence is copied from the sampled native depth allocation. The review-led minor repeated PCF-offset concern remains deliberately out of this round as instructed.
