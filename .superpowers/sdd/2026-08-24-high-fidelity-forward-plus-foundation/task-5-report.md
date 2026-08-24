# Task 5 Report: Analytic and Volumetric Fog

## Status

`DONE_WITH_CONCERNS`

Task 5 adds generic projected fog-volume transforms, deterministic bounded fog planning, lower-tier analytic fog, and higher-tier native Vulkan froxel fog. The graph-authoritative native path now executes fog after opaque HDR and before transparent rendering, uses resolved quality grids and device limits, records truthful workload/allocation facts, and emits inspectable density, lighting, and integrated-transmittance slices.

The required focused gate passes 53/53 and the complete Rendering namespace passes 577/577. Build, formatting, and diff gates pass. The repository-wide suite retains the same eight pre-existing Windows AppContainer module-host failures reported by Tasks 3 and 4; 1,776 other tests pass and no rendering test fails.

## TDD evidence

### Baseline

Before Task 5 changes:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~RenderQualityProfileTests|FullyQualifiedName~HighFidelityRenderGraphTests|FullyQualifiedName~ViewportContractTests|FullyQualifiedName~VulkanHighFidelityCaptureTests|FullyQualifiedName~VulkanShadowPlannerTests"
```

Output: `Passed: 60, Failed: 0, Skipped: 0`.

### Projection and planner RED/GREEN

Projection and planner tests were written first for runtime transform projection, global/box/sphere shapes, finite optical clamping, priority/entity ordering, bounded packing with affected IDs, exact preset grids, dispatch dimensions, and deterministic camera/grid history invalidation.

Initial RED compilation failed with `CS0246` for the deliberately absent `RekallAgeRuntimeViewportFogVolume` and planner contracts. The first minimum GREEN command was:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanFogPlannerTests|FullyQualifiedName~ViewportContractTests.RuntimeFrameProjectsFogVolumeTransformAndMaterialFacts"
```

Output: `Passed: 9, Failed: 0` for the original group.

Exact fixed High/Ultra/Epic grids and device-limit degradation were then added RED-first. The device-limit test initially retained the unbounded Epic `320x180x96` request instead of resolving `48x27x14`; GREEN passed 7/7 after the quality resolver proportionally clamped against both 3D-image and compute-workgroup limits and emitted stable `fogGrid` degradation facts.

Self-review added a final global-volume bound test. RED failed with `CS0117` because `DefaultMaximumGlobalVolumes` did not exist. The planner now retains at most 64 local and 8 global volumes, with priority/entity ordering and `REKALL_FOG_VOLUME_LIMIT_CLAMPED` IDs. The affected GREEN command passed 9/9:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanFogPlannerTests"
```

### Native analytic/froxel RED/GREEN

The native tests were added before implementation. Initial RED compilation failed for absent `RekallAgeHighFidelityFogReport`, `RekallAgeHighFidelityFrameReport.Fog`, and `FogDebugCaptures` contracts.

The minimum native GREEN command was:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanHighFidelityCaptureTests.AnalyticFog|FullyQualifiedName~VulkanHighFidelityCaptureTests.FroxelFog"
```

Output: `Passed: 2, Failed: 0` after real Vulkan execution. It remained 2/2 after command recording was refactored to the approved opaque -> fog -> transparent -> bloom/tone-map order.

The tests require:

- an actual analytic fullscreen graphics draw on Performance/Low;
- an allocated `R16G16B16A16_SFloat` 3D grid and compute injection/composite dispatch on High;
- resolved High grid `160x90x48` and dispatch `40x23x12`;
- direct-light injection and initial history-reset facts;
- fog before transparent in both graph and recorded pass reports;
- fogged pixels differing from empty pixels;
- byte-identical empty froxel and disabled/empty analytic outputs;
- three existing, nonblank, dimension-correct debug slice PNGs.

The blended-renderable regression also passed 1/1 after the new fog/transparent render passes were introduced, proving the transparent path remains executable and does not inflate shadow work.

## Final verification

Required focused command after all self-review changes:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanFogPlannerTests|FullyQualifiedName~ViewportContractTests|FullyQualifiedName~VulkanHighFidelityCaptureTests" --logger "console;verbosity=minimal"
```

Output: `Passed: 53, Failed: 0, Skipped: 0` in 12 seconds.

Complete Rendering namespace:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~Rekall.Age.Tests.Rendering" --logger "console;verbosity=minimal"
```

Output: `Passed: 577, Failed: 0, Skipped: 0` in 12 seconds.

Build/format/diff:

```powershell
dotnet build src\Rekall.Age.Rendering\Rekall.Age.Rendering.csproj --no-restore
dotnet format Rekall.AGE.sln --no-restore --verify-no-changes --include <all changed C# files>
git diff --check
```

Outputs: build succeeded with `0 Warning(s), 0 Error(s)`; format and diff checks exited 0. The first format check exposed six indentation diagnostics in the changed runtime projection file; the indentation-only correction was applied and the exact gate reran green. Git emitted only the repository's LF-to-CRLF notices.

Repository-wide evidence:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --logger "console;verbosity=minimal"
```

Output: `Failed: 8, Passed: 1776, Skipped: 0, Total: 1784` in 3 minutes 51 seconds. All eight failures are the pre-existing `ModuleHostWindowsIsolationTests` AppContainer worker startup/protocol failures (`host.initialize`, truncated frame, or transport `EndOfStreamException`) documented in prior task reports. No changed rendering/runtime projection test failed.

## Native and visual evidence

The retained native High run produced these original-resolution 160x90 slice images:

- density: `C:\Users\Marius\AppData\Local\Temp\rekall-age-tests\62b5377c617c488eb755240c1961ea2b\vulkan-fog-density-slice-24-20260824200933712.png`, 359 bytes, SHA-256 `93EE7506AE018F04A1AA0FA235379ADA72A1F017B495D5084B0812853D1069A0`;
- lighting: `C:\Users\Marius\AppData\Local\Temp\rekall-age-tests\62b5377c617c488eb755240c1961ea2b\vulkan-fog-lighting-slice-24-20260824200933712.png`, 282 bytes, SHA-256 `AB9000D888E768CBC5B37F71E5050613F7252388BF88693053951F4C7E79B563`;
- integrated transmittance: `C:\Users\Marius\AppData\Local\Temp\rekall-age-tests\62b5377c617c488eb755240c1961ea2b\vulkan-fog-integrated-transmittance-slice-24-20260824200933712.png`, 407 bytes, SHA-256 `A1028A6C9DCB9F642D8B46D43D2532E6F3C8E0B711214A3E8EC1A4A3C347D585`.

The fogged final frame hash is `89DF75A6C85180ACFC335C77F6C18248C06813921A61ED15FFEF7BCA6BAC36E1`. Empty froxel and forced-empty analytic frames are byte-identical at `9DD74A6BE57C8E44A1F6616FE1D4D9D93C7248BDBB86AA91C3FAFF0639E158ED`, while the nonzero-density frame differs. The final frame and density slice were opened at original resolution; the density slice contains a visible depth/height gradient and the deterministic hashes independently prove nonzero fog changes the native result without disturbing empty-fog parity.

## Implementation

- Runtime projection now carries each generic `Rekall.FogVolume` entity transform into a backend-neutral viewport fog contract.
- The pure fog planner sanitizes non-finite density/scattering inputs, clamps density/albedo/emission/anisotropy/height falloff/blend distance, orders by priority then entity ID, bounds local/global packing, reports overflow IDs, resolves analytic/froxel modes, bounds froxel cells, and emits deterministic history reset/reuse facts.
- Quality resolution preserves analytic Performance/Low, scaled low-froxel Medium, and exact fixed High/Ultra/Epic grids. Device dimension/workgroup limits degrade grid resolution before allocation with stable facts.
- The validated render graph owns fog resources and pass order. Analytic fog is an explicit graphics pass; froxel fog adds storage usage to scene HDR and an explicit 3D resource/compute pass; both precede transparent rendering.
- Native Vulkan preflights compute queue, `RGBA16F` storage support, and maximum 3D dimensions before allocating. The enabled froxel path owns a 3D image/view/memory, bounded SSBO, descriptors, compute pipeline, barriers, density/light injection, depth integration, and HDR composite. Analytic fog owns a fullscreen blending pipeline.
- Native reports expose mode, enabled state, grid/dispatch, recorded dispatch/draw counts, packed/dropped volumes, direct-light injection, shadow attenuation, and temporal reset/reuse planning. Capability failure cannot claim allocation or execution.
- Debug density, lighting, and integrated-transmittance slices are deterministic CPU reconstructions from the exact sanitized plan, written only after successful native Vulkan execution. They are inspectable diagnostics rather than GPU-image readbacks.

## Files

Production:

- `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
- `src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs`
- `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
- `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`
- `src/Rekall.Age.Rendering/RekallAgeRenderQualityProfileResolver.cs`
- `src/Rekall.Age.Rendering/RekallAgeHighFidelityRenderGraphBuilder.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanFogPlanner.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFrameRenderer.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneCommandPlan.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanShaderCompiler.cs`
- `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- `src/Rekall.Age.Rendering/Shaders/rekall_fog.comp`
- `src/Rekall.Age.Rendering/Shaders/rekall_fog.frag`

Tests:

- `tests/Rekall.Age.Tests/Rendering/VulkanFogPlannerTests.cs`
- `tests/Rekall.Age.Tests/Rendering/ViewportContractTests.cs`
- `tests/Rekall.Age.Tests/Rendering/RenderQualityProfileTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanHighFidelityCaptureTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanSceneCommandPlanTests.cs`

The approved design and implementation plan were not changed because executable evidence confirmed the already-approved contract, pass order, and gates.

## Self-review

- Genericity: all inputs are backend-neutral camera, light/shadow, quality, transform, and participating-media facts; no gameplay, controller, or genre behavior was added.
- Determinism: quality grids, clamping, volume packing, overflow IDs, dispatches, history reset codes, debug slices, and capture comparisons are stable.
- Graph authority: native resources and recording follow the validated graph's resolved scaled extent and fog-before-transparent order.
- Native truthfulness: allocations happen only after capability checks; execution/allocation reports derive from enabled plans and recorded work. The report uses `ShadowAttenuationApplied`, not an inaccurate texture-sampling claim.
- Numerical safety: authored optical facts and transform extents are finite and bounded before GPU packing; local and global counts are capped.
- Empty behavior: zero density/emission disables fog work sufficiently to preserve exact final pixel parity across analytic and froxel selections.
- Ownership: new Vulkan resources are handle-guarded and released through the existing state-owned dependency order.

## Concerns and follow-up

- Temporal continuity, grid-change reset, camera-cut reset, and the `TemporalReprojection` decision are executable and tested in the planner. The deterministic capture path creates fresh Vulkan state per capture and therefore does not retain/reproject a prior GPU froxel image across capture calls. Persistent GPU history storage/reprojection remains a production renderer-lifecycle follow-up.
- The initial light-aware implementation injects the selected direct-light color and applies a conservative shadow-availability attenuation scalar; it does not sample cascade depth inside the compute shader. The report names this accurately. Per-froxel cascade lookup is a later fidelity improvement.
- Box and sphere bounds currently use position and absolute scale. Authored fog-volume rotation is preserved in the viewport transform contract but is not yet applied to the local signed-distance evaluation.
- Debug slices reconstruct the exact sanitized plan on CPU after native success rather than reading the 3D image back. They prove inspectable density/light/transmittance inputs and native-pass success, but a future diagnostic mode should expose GPU froxel readback for shader-level comparison.
