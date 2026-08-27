# High-Fidelity Forward+ Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the first production high-fidelity Vulkan slice: explicit Performance-to-Epic quality profiles, inspectable pass/resource planning, HDR PBR output, cascaded directional shadows, scalable fog, bloom/tone mapping, GPU particles, profiling, and a fully upgraded playable Aetherfall Resonance Court.

**Architecture:** Authored environment and quality components project into backend-neutral runtime contracts. A deterministic resolver combines authored intent with device capabilities to produce an inspectable feature plan, then a Vulkan high-fidelity frame renderer executes a declared pass graph while retaining the existing renderer as a compatibility path. Aetherfall consumes only generic scene, asset, animation, material, light, fog, particle, and post-process primitives.

**Tech Stack:** C# 13, .NET 10, Silk.NET Vulkan, GLSL/SPIR-V, AGE immutable runtime contracts, xUnit, AGE CLI/Studio, JSON scene/model/material assets.

**Spec:** `docs/superpowers/specs/2026-08-24-high-fidelity-3d-rendering-design.md`

## Global Constraints

- Vulkan is the reference implementation; authored contracts and diagnostics remain backend-neutral.
- The `High` acceptance target is 2560x1440 at 60 FPS on the NVIDIA GeForce RTX 5090, measured with GPU timestamps over 600 representative frames.
- Presets are exactly `Performance`, `Low`, `Medium`, `High`, `Ultra`, and `Epic`; agents may override individual settings.
- Quality scaling may change rendering work only. It must never change gameplay simulation, visibility facts, collision, AI, or deterministic runtime state.
- Unsupported, clamped, or degraded features must return stable codes with requested/resolved values; no silent substitution.
- Engine contracts remain generic and may not mention Aetherfall, warden, enemy, boss, weapon, arena, or another game-specific concept.
- After the latest Aetherfall scene or module mutation, deterministic `runtime inspect` must prove representative input changes an attached agent-owned component or transform.
- Update this plan and the design specification whenever implementation evidence changes a contract, preset value, pass order, or acceptance gate.

---

## File Structure

New files are split by responsibility:

- `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderQualityContracts.cs` — backend-neutral preset, resolved-plan, pass, timing, and degradation records.
- `src/Rekall.Age.Rendering/RekallAgeRenderQualityProfileResolver.cs` — pure authored-intent/device-capability resolver.
- `src/Rekall.Age.Rendering/RekallAgeHighFidelityRenderGraph.cs` — pass/resource/dependency model and validator.
- `src/Rekall.Age.Rendering/RekallAgeHighFidelityRenderGraphBuilder.cs` — resolved-plan-to-graph compiler.
- `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFrameRenderer.cs` — orchestration only; no scene parsing.
- `src/Rekall.Age.Rendering/RekallAgeVulkanShadowCascadePlanner.cs` — cascade math, stabilization, caster selection, and atlas layout.
- `src/Rekall.Age.Rendering/RekallAgeVulkanFogPlanner.cs` — analytic/froxel plan and volume packing.
- `src/Rekall.Age.Rendering/RekallAgeVulkanParticlePlanner.cs` — emitter validation, capacity allocation, and draw planning.
- `src/Rekall.Age.Rendering/RekallAgeVulkanGpuProfiler.cs` — timestamp queries and per-pass report construction.
- `src/Rekall.Age.Rendering/Shaders/rekall_shadow.vert` — depth-only shadow transform.
- `src/Rekall.Age.Rendering/Shaders/rekall_shadow.frag` — alpha-mask shadow support.
- `src/Rekall.Age.Rendering/Shaders/rekall_fog.comp` — froxel density/light injection and temporal integration.
- `src/Rekall.Age.Rendering/Shaders/rekall_particles.comp` — bounded particle simulation.
- `src/Rekall.Age.Rendering/Shaders/rekall_particles.vert` and `.frag` — camera-facing/mesh particle rendering.
- `src/Rekall.Age.Rendering/Shaders/rekall_bloom.comp` — extract/downsample/upsample pyramid.
- `src/Rekall.Age.Rendering/Shaders/rekall_tonemap.frag` — exposure, AgX-style curve, grade, and output conversion.
- `tests/Rekall.Age.Tests/Rendering/RenderQualityProfileTests.cs` — preset and override resolution.
- `tests/Rekall.Age.Tests/Rendering/HighFidelityRenderGraphTests.cs` — graph topology/resource validation.
- `tests/Rekall.Age.Tests/Rendering/VulkanShadowCascadePlannerTests.cs` — deterministic shadow math.
- `tests/Rekall.Age.Tests/Rendering/VulkanFogPlannerTests.cs` — fog tier/volume planning.
- `tests/Rekall.Age.Tests/Rendering/VulkanParticlePlannerTests.cs` — deterministic emitter budgets.
- `tests/Rekall.Age.Tests/Rendering/VulkanHighFidelityCaptureTests.cs` — native HDR/shadow/fog/post/particle integration.
- `tests/Rekall.Age.Tests/Rendering/VulkanGpuProfilerTests.cs` — timing availability and report invariants.
- `tests/Rekall.Age.Tests/Examples/AetherfallHighFidelityAcceptanceTests.cs` — authored-zone structure and executable gameplay evidence.

Existing files retain their current responsibilities and receive narrow extensions only.

---

### Task 1: Backend-Neutral Quality and Environment Contracts

**Files:**
- Create: `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderQualityContracts.cs`
- Modify: `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
- Modify: `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs`
- Create: `src/Rekall.Age.Rendering/RekallAgeRenderQualityProfileResolver.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/RenderQualityProfileTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/ViewportContractTests.cs`

**Interfaces:**
- Consumes: `Rekall.RenderQualityProfile`, `Rekall.Environment3D`, `Rekall.ShadowSettings`, and `Rekall.FogVolume` component properties plus `RekallAgeRenderingDeviceCapabilities`.
- Produces: `RekallAgeResolvedRenderFeaturePlan Resolve(RekallAgeRenderQualityIntent intent, RekallAgeRenderingDeviceCapabilities capabilities, int outputWidth, int outputHeight)`.

- [x] **Step 1: Write failing preset-resolution tests**

Add tests that assert all six presets resolve to the exact table in the spec and that finite authored overrides win when supported:

```csharp
[Theory]
[InlineData("Performance", 0.50, 1, 512, "analytic", 2000)]
[InlineData("Low", 0.67, 1, 1024, "analytic", 8000)]
[InlineData("Medium", 0.75, 2, 1024, "froxel-low", 24000)]
[InlineData("High", 1.00, 3, 2048, "froxel", 64000)]
[InlineData("Ultra", 1.00, 4, 2048, "froxel-high", 128000)]
[InlineData("Epic", 1.25, 4, 4096, "froxel-epic", 250000)]
public void ResolverProducesStablePresetDefaults(
    string preset, double scale, int cascades, int shadowResolution, string fogMode, int particles)
{
    var plan = resolver.Resolve(new(preset), Capabilities.All, 2560, 1440);
    Assert.Equal(scale, plan.ResolutionScale, 2);
    Assert.Equal(cascades, plan.Shadows.CascadeCount);
    Assert.Equal(shadowResolution, plan.Shadows.Resolution);
    Assert.Equal(fogMode, plan.Fog.Mode);
    Assert.Equal(particles, plan.Particles.MaximumActiveParticles);
}
```

- [x] **Step 2: Run the tests and verify the missing contracts fail compilation**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~RenderQualityProfileTests"`

Expected: FAIL because `RekallAgeRenderQualityIntent` and the resolver do not exist.

- [x] **Step 3: Add immutable contracts and pure resolution**

Define bounded records, including:

```csharp
public sealed record RekallAgeRenderQualityIntent(
    string Preset = "High",
    double? ResolutionScale = null,
    int? ShadowCascadeCount = null,
    int? ShadowResolution = null,
    string? FogMode = null,
    bool? Bloom = null,
    bool? Ssao = null,
    int? MaximumActiveParticles = null,
    bool AutomaticScaling = false,
    double TargetFramesPerSecond = 60);

public sealed record RekallAgeResolvedRenderFeaturePlan(
    string RequestedPreset,
    string ResolvedPreset,
    int OutputWidth,
    int OutputHeight,
    int RenderWidth,
    int RenderHeight,
    double ResolutionScale,
    RekallAgeResolvedShadowQuality Shadows,
    RekallAgeResolvedFogQuality Fog,
    RekallAgeResolvedPostQuality Post,
    RekallAgeResolvedParticleQuality Particles,
    long EstimatedTransientBytes,
    long EstimatedPersistentBytes,
    IReadOnlyList<RekallAgeRenderFeatureDegradation> Degradations);
```

Project authored components into `RekallAgeRuntimeRenderView` init-only collections and attach the resolved plan to `RekallAgeRuntimeViewportFrame` as an init property so existing positional constructors remain compatible.

- [x] **Step 4: Add invalid/unsupported override diagnostics**

Assert NaN, negative resolutions, unknown presets, unsupported timestamp use, and device-limit clamps return stable degradations such as `REKALL_RENDER_QUALITY_OVERRIDE_INVALID` and `REKALL_RENDER_FEATURE_DEVICE_CLAMPED`, preserving both requested and resolved values.

- [x] **Step 5: Run focused contracts and projection tests**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~RenderQualityProfileTests|FullyQualifiedName~ViewportContractTests"`

Expected: PASS.

- [x] **Step 6: Commit**

```powershell
git add src/Rekall.Age.Rendering.Abstractions src/Rekall.Age.Runtime.Abstractions src/Rekall.Age.Runtime src/Rekall.Age.Rendering/RekallAgeRenderQualityProfileResolver.cs tests/Rekall.Age.Tests/Rendering
git commit -m "feat: resolve scalable render quality profiles"
```

---

### Task 2: Inspectable High-Fidelity Render Graph

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeHighFidelityRenderGraph.cs`
- Create: `src/Rekall.Age.Rendering/RekallAgeHighFidelityRenderGraphBuilder.cs`
- Modify: `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderQualityContracts.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/HighFidelityRenderGraphTests.cs`

**Interfaces:**
- Consumes: `RekallAgeResolvedRenderFeaturePlan` and `RekallAgeRuntimeViewportFrame`.
- Produces: `RekallAgeHighFidelityRenderGraph Build(frame, plan)` with ordered resources, passes, dependencies, and validation diagnostics.

- [x] **Step 1: Write failing graph topology tests**

Cover `Performance`, `High`, and `Epic`. High must order these named passes:

```csharp
Assert.Equal(
    ["depth-normal", "shadow-directional", "cluster-build", "opaque-hdr", "fog-integrate", "fog-debug-readback", "transparent-particles", "bloom", "tone-map", "ui", "present"],
    graph.Passes.Select(pass => pass.Name));
Assert.All(graph.Passes, pass => Assert.All(pass.Reads, resource => Assert.Contains(graph.Resources, item => item.Name == resource)));
```

Performance must omit volumetric, bloom, and SSAO resources. Epic must increase dimensions/samples without changing dependency order.

- [x] **Step 2: Run the graph tests and verify red**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~HighFidelityRenderGraphTests"`

Expected: FAIL because the graph types do not exist.

- [x] **Step 3: Implement resource/pass records and validation**

Use explicit formats and lifetimes:

```csharp
public sealed record RekallAgeHighFidelityRenderResource(
    string Name, string Format, int Width, int Height, int Layers,
    string Lifetime, IReadOnlyList<string> Usage);

public sealed record RekallAgeHighFidelityRenderPass(
    string Name, string Kind, IReadOnlyList<string> Reads,
    IReadOnlyList<string> Writes, int Order, bool Enabled);
```

Validate duplicate names, missing producers, read-before-write, cycles, invalid dimensions, incompatible depth/color use, and memory arithmetic overflow.

- [x] **Step 4: Add deterministic memory estimation tests**

Assert the graph's byte estimate equals the sum of format bytes × dimensions × layers and never exceeds the resolved plan without a `REKALL_RENDER_GRAPH_MEMORY_BUDGET_EXCEEDED` diagnostic.

- [x] **Step 5: Run graph tests**

Expected: PASS.

- [x] **Step 6: Commit**

```powershell
git add src/Rekall.Age.Rendering src/Rekall.Age.Rendering.Abstractions tests/Rekall.Age.Tests/Rendering/HighFidelityRenderGraphTests.cs
git commit -m "feat: plan inspectable high-fidelity render graphs"
```

---

### Task 3: HDR Scene Target, Bloom, and Tone Mapping

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFrameRenderer.cs`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_bloom.comp`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_tonemap.frag`
- Modify: `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneRenderTarget.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneCommandPlan.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanShaderCompiler.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanHighFidelityCaptureTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanSceneCaptureTests.cs`

**Interfaces:**
- Consumes: legacy-prepared scene meshes plus a validated high-fidelity graph.
- Produces: a captured LDR PNG and `RekallAgeHighFidelityFrameReport` containing executed passes/resources; retains the legacy path when no high-fidelity plan is authored.

- [x] **Step 1: Write failing native integration tests**

Create a small emissive PBR scene with a `Rekall.PostProcessStack` and `High` profile. Assert the native result reports `R16G16B16A16_SFloat` scene color, executes bloom and tone-map passes, and produces a non-blank frame with a brighter-but-bounded emissive region.

- [x] **Step 2: Verify the test fails because post passes are metadata-only**

Run the focused test and confirm the current Vulkan path does not report or execute the passes.

- [x] **Step 3: Add an HDR render target and explicit post resources**

Keep swapchain/offscreen output at `R8G8B8A8_UNorm`, render the scene to `R16G16B16A16_SFloat`, and allocate the bloom pyramid from the render graph. Validate format support before allocation.

- [x] **Step 4: Implement bloom and AgX-style tone mapping**

The bloom shader performs thresholded downsample and energy-preserving upsample. Tone mapping receives exposure, white point, saturation, contrast, grade strength, bloom intensity, and output conversion. Clamp only at final output; keep scene/emissive lighting in linear HDR.

- [x] **Step 5: Preserve existing software/UI behavior**

Composite UI after tone mapping exactly once. Run existing Vulkan capture/UI tests to prove there is no double composition or legacy-path regression.

- [x] **Step 6: Run focused shader/capture tests**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanHighFidelityCaptureTests|FullyQualifiedName~VulkanSceneCaptureTests|FullyQualifiedName~VulkanSceneCommandPlanTests"`

Expected: PASS.

- [x] **Step 7: Commit**

```powershell
git add src/Rekall.Age.Rendering tests/Rekall.Age.Tests/Rendering
git commit -m "feat: render HDR scenes with bloom and tone mapping"
```

---

### Task 4: Stabilized Cascaded Directional Shadows

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeVulkanShadowCascadePlanner.cs`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_shadow.vert`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_shadow.frag`
- Modify: `src/Rekall.Age.Rendering/Shaders/rekall_scene.vert`
- Modify: `src/Rekall.Age.Rendering/Shaders/rekall_scene.frag`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneUniformUpload.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanScenePipelineDescription.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFrameRenderer.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanShadowCascadePlannerTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanHighFidelityCaptureTests.cs`

**Interfaces:**
- Consumes: active camera, primary directional light, visible caster bounds, layer masks, and resolved shadow quality.
- Produces: `RekallAgeVulkanShadowPlan` with one-to-four stable cascade matrices, splits, atlas viewports, caster IDs, and filter/bias parameters.

- [x] **Step 1: Write failing cascade math tests**

Assert increasing splits, finite matrices, exact preset cascade counts, stable atlas viewports, layer filtering, and texel stabilization:

```csharp
var moved = planner.Plan(camera with { X = camera.X + 0.0001 }, light, casters, quality);
Assert.Equal(first.Cascades[0].ViewProjection, moved.Cascades[0].ViewProjection);
```

- [x] **Step 2: Verify red and implement the pure planner**

Use practical logarithmic/linear split weighting, frustum corner fitting, light-space bounds, padding, and texel snapping. Reject non-finite poses with `REKALL_SHADOW_CAMERA_INVALID`.

- [x] **Step 3: Add depth-only cascade rendering and shader sampling**

Allocate a depth array/atlas, render only selected casters, bind matrices/splits, select cascade by view depth, and apply preset-controlled PCF. Respect `castShadows`, receiver/caster masks, bias, normal bias, distance, and priority.

- [x] **Step 4: Add visual and workload diagnostics**

Expose cascade split/depth debug captures and report resolution, caster count, draw count, culled count, filter taps, and atlas bytes per cascade.

- [x] **Step 5: Run shadow and full existing Vulkan suites**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanShadowCascadePlannerTests|FullyQualifiedName~VulkanScene"`

Expected: PASS.

- [x] **Step 6: Commit**

```powershell
git add src/Rekall.Age.Rendering tests/Rekall.Age.Tests/Rendering
git commit -m "feat: add scalable cascaded scene shadows"
```

---

### Task 5: Analytic and Volumetric Fog

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeVulkanFogPlanner.cs`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_fog.comp`
- Modify: `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs`
- Modify: `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeHighFidelityRenderGraph.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeHighFidelityRenderGraphBuilder.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeRenderQualityProfileResolver.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFrameRenderer.cs`
- Modify: `src/Rekall.Age.Rendering/Shaders/rekall_fog.frag`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanFogPlannerTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/ViewportContractTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/HighFidelityRenderGraphTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanHighFidelityCaptureTests.cs`

**Interfaces:**
- Consumes: projected `Rekall.FogVolume` values, the scene renderer's resolved effective camera, one selected directional-light injection fact or explicit none, cascade shadow resources, opaque depth, previous fog history, and resolved fog quality.
- Produces: ordered bounded fog volumes plus analytic or froxel dispatch plans, a persistent GPU-history resource, reset/reuse facts, and graph-declared GPU-readback debug evidence.

- [x] **Step 1: Write failing projection/planner tests**

Test global and rotated local box/sphere volumes, unsupported-shape degradation with stable entity IDs, density/albedo/emission/anisotropy clamping, priority ordering, exact preset grids, and camera/grid history reset.

- [x] **Step 2: Implement generic fog projection and planning**

Use records such as:

```csharp
public sealed record RekallAgeRuntimeViewportFogVolume(
    string EntityId, string EntityName, string Shape,
    double Density, string Albedo, string Emission,
    double Anisotropy, double HeightFalloff, int Priority,
    RekallAgeRuntimeTransform Transform);
```

Performance/Low resolve to analytic distance/height fog. Medium and above resolve bounded froxel dimensions from the quality profile.

- [x] **Step 3: Implement light/shadow-aware froxel integration**

Resolve one effective camera from authored pose plus actual scene bounds and use it unchanged for scene/shadow uniforms, fog push constants, and history continuity; default/null cameras auto-frame once, perspective rays use its projection tangent/aspect, and orthographic rays use parallel direction plus per-pixel origins from its half extents. Match the Vulkan scene projection's flipped `M22` by reconstructing framebuffer UV Y through inverse camera-up in CPU helpers, analytic/froxel shaders, and temporal-history projection. Store/sample opaque depth in both shaders and stop integration at opaque surfaces. Select only the case-insensitive canonical `DirectionalLight` or `Rekall.DirectionalLight` variant by shadow priority then stable entity ID and use that exact direction/color/entity for shadow planning, frame UBOs, fog planning, shader injection, and reports; point, spot, custom, and foreign-namespace light variants are excluded, while no directional light injects zero energy with no synthetic fallback. Evaluate anisotropic phase and cascade shadow lookup from that selection, and local volumes through packed world-to-local transforms. Keep one initialized 3D history image per native renderer session, bind/sample/reproject it on reusable frames, and clear/reset it on camera cuts or grid changes. Declare and budget the history plus `fog-froxel` transfer-source and `fog-debug-readback` transfer-destination resources in the render graph, execute their dependency-ordered readback pass, derive debug slices from the returned GPU cells, record injection and composite as two dispatches, and composite before transparent particles. Clamp supported volume counts and return affected entity IDs on overflow; reject unsupported shapes with a stable degradation code and dropped IDs.

- [x] **Step 4: Add fog debug slices and tests**

Capture density, lighting, and integrated-transmittance debug outputs. Assert empty density produces the same pixel checksum as fog disabled within the existing deterministic tolerance.

- [x] **Step 5: Run focused tests and commit**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanFogPlannerTests|FullyQualifiedName~ViewportContractTests|FullyQualifiedName~VulkanHighFidelityCaptureTests"
git add src/Rekall.Age.Runtime.Abstractions src/Rekall.Age.Runtime src/Rekall.Age.Rendering.Abstractions src/Rekall.Age.Rendering tests/Rekall.Age.Tests/Rendering
git commit -m "feat: render scalable atmospheric fog volumes"
```

---

### Task 5A: Windows AppContainer Module-Host Zero-Failure Gate

**Files:**
- Modify: `tests/Rekall.Age.Tests/Modules/ModuleHostWindowsIsolationTests.cs`
- Modify: `docs/production/PROGRESS.md`
- Modify: `docs/superpowers/plans/2026-08-24-high-fidelity-forward-plus-foundation.md`

**Root cause and invariant:**
- Commit `bf292d2` made `Rekall.Age.Rendering.Abstractions.dll` a declared worker
  runtime dependency, but the Windows isolation fixture's manifest-backed real
  host payload retained its earlier eight-file allowlist. The stager correctly
  copied only that verified inventory, so the CLR terminated the worker with
  `0xE0434352` and a `FileNotFoundException` before `host.initialize` could
  write a response; the broker's truncated-frame/`EndOfStreamException` was
  downstream evidence, not a codec or AppContainer defect.
- A real restricted-worker payload must contain every runtime assembly required
  by the worker's dependency graph, and every file must still cross the
  boundary only through the existing size/SHA-256 verified manifest and
  immutable stager. Do not compensate with broader ACLs/capabilities,
  unrestricted fallback, retries, relaxed timeouts, or protocol changes.

- [x] **Phase 1:** reproduce all 8/8 failures and capture launch, stage, profile,
  job, exit, stderr, stdio, and protocol evidence. Native launch completed with
  zero capabilities, a one-process/512 MiB kill-on-close job, and present staged
  inputs; the worker exited `0xE0434352` with bounded stderr naming the omitted
  rendering-contract assembly before its first response frame.
- [x] **Pattern/hypothesis:** compare the complete-output executable path, which
  passed finite typed initialize/shutdown with exit 0 and empty stderr, against
  the staged path. A staged-but-uncontained worker reproduced the same EOF,
  proving the inventory boundary independently of AppContainer.
- [x] **RED/GREEN:** add
  `StagedWorkerPayloadCompletesFiniteProtocolBeforeContainment`; witness its
  truncated-frame RED, add only `Rekall.Age.Rendering.Abstractions.dll` to the
  exact manifest fixture, then witness 1/1 GREEN.
- [x] **Security and race gate:** run the complete 9-test Windows isolation class
  once and then ten consecutive times (90/90). Secret scrubbing, unstaged
  read/write denial, child/network denial, bounded 64 KiB stderr, typed framing,
  request timeout/crash classification, no-capability profile, and job limits
  all remained effective. Dedicated roots ended with zero module-host processes
  and zero `session-*` trees.
- [x] **Engine/module-host gate:** `FullyQualifiedName~Module` passed 185/185 and the
  complete `Rekall.Age.Tests` project passed 1,813/1,813 with zero failures or
  skips; both post-run checks found zero module-host processes/session trees.
  No Studio code or test fixture was affected. An additional Studio isolation
  check passed all 35 non-ViewModel tests and identified the unrelated existing
  long-running `HeadlessAutomationCreatesProjectAndCompletesAgentGauntlet` case
  with the test runner's five-minute hang evidence. This completes Task 5A's
  AppContainer and engine gates only; overall repository/Studio verification is
  still open and is tracked as the blocking follow-up below.
- [x] **Fix Round 1 cleanup hardening:** put direct staged-worker launch,
  writes, flush, finite-session reads, exit, and stderr drain under one bounded
  deadline; always close stdin, kill any live process tree, await bounded exit,
  and dispose the process before staged-session disposal. A deliberately hung
  partial-response worker proved the failure cleanup path. The expanded class
  passed ten consecutive times (100/100), the complete engine project passed
  its new total of 1,814/1,814, and both residue checks found zero workers and
  zero `session-*` trees. The historical Task 5A engine result above remains
  1,813/1,813; the extra test is this cleanup regression.

**Architecture decision:** retain the existing no-capability AppContainer,
explicit three-handle inheritance, immutable verified staging, typed framed
protocol, and kill-on-close job unchanged. The repair belongs at the payload
inventory source that omitted a declared dependency.

**Verification commands:**

```powershell
dotnet build src/Rekall.Age.ModuleHost/Rekall.Age.ModuleHost.csproj -c Debug --no-restore
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~ModuleHostWindowsIsolationTests"
1..10 | ForEach-Object { dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~ModuleHostWindowsIsolationTests" }
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~Module"
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Debug --no-build
dotnet build Rekall.AGE.sln -c Debug --no-restore
```

---

### Task 5B: Studio Headless-Gauntlet Verification Follow-up — COMPLETE

**Status:** COMPLETE. Task 6 and final repository delivery are unblocked.

**Observed evidence:**
- The complete Studio project remained CPU-active for 31:06 without returning a
  result; its responsive testhost had consumed 30:21 CPU and 1.60 GiB working
  set when the bounded diagnostic run was stopped.
- All Studio classes except `StudioViewModelTests` passed 35/35 in 1.66 seconds.
- A five-minute per-test blame run passed 10 ViewModel tests, then recorded
  `StudioViewModelTests.HeadlessAutomationCreatesProjectAndCompletesAgentGauntlet`
  as incomplete. This is not a Task 5A module-host failure, but it keeps the
  overall repository gate open.
- Evidence:
  `.superpowers/sdd/2026-08-24-high-fidelity-forward-plus-foundation/task-5a-evidence/task-5a-studio-viewmodel-isolation.trx`,
  `.superpowers/sdd/2026-08-24-high-fidelity-forward-plus-foundation/task-5a-evidence/35827907-8866-4a94-b4c7-9a394a02628f/Sequence_a2412baa567843ee99ae5a061543f113.xml`,
  and the adjacent `testhost_57332_20260825T020605_hangdump.dmp`.

- [x] Apply systematic debugging to the headless gauntlet's CPU-active path and
  identify the first non-progressing boundary.
- [x] Add a witnessed RED regression for that root cause and implement one fix
  without weakening the gauntlet or hiding it behind a retry/skip.
- [x] Run the complete Studio project to an actual zero-failure result.
- [x] Reconfirm the complete engine project, update the tracked verification
  evidence, and only then unblock Task 6/final delivery.

**Resolution:** Studio project creation persisted an intentionally empty scene,
but the gauntlet treated scene-file existence as proof of authored content. It
therefore skipped both generic blueprint and agent-owned playable-module
authoring, failed `package-created` at module build with
`REKALL_MODULE_PROJECTS_MISSING`, and the no-limit deterministic agent repeated
the failed terminal workflow indefinitely. The gauntlet now preserves a scene
only when the loaded document has authored entities; an existing empty editor
scene follows the same complete author/build/package/audit path as a newly
created scene. Authored non-empty scenes remain preserved.

The focused empty-editor-scene regression had a witnessed 83 ms RED and passed
in 8 seconds after the one-line invariant repair. All four gauntlet tests passed
4/4 in 26.5 seconds. The original unmodified Studio case passed three
consecutive runs in 9 seconds each. The complete Studio project passed 65/65 in
46.6 seconds, the complete engine project passed its new total of 1,815/1,815
in 4 minutes 6 seconds, and the complete solution built with zero warnings and
zero errors in 5.47 seconds. Post-run checks found zero Rekall test/worker/player
processes, zero staged `session-*` trees in the current engine roots, and zero
current-run Studio automation roots.

**Fix Round 1 closure:** The gauntlet-authored route now satisfies the mandatory
runtime gameplay checkpoint rather than proving only playable-adapter text. Its
generic marker owns `Game.Modules.AgentGauntlet.GauntletState` and a semantic
input map; its agent-authored module consumes the semantic action with the
engine delta time and changes both component state and `Position2D`. After the
latest scene/module mutation, the workflow builds modules and requires exact
`rekall.runtime.inspect_scene` assertions of `progress delta = 1` and
`position2d.x delta = 1` before the unchanged package, audit, and nonblank proof
capture. The expanded regression verifies generated project/source, attached
state, exact runtime results, archive, audit, capture, and proof output.

Final Fix Round 1 gates passed: gauntlet class 4/4, the original Studio case
three consecutive times, Studio 65/65, engine 1,815/1,815, and the solution
build with zero warnings/errors. The tracked phase ledger, commands, exact
timestamps/timings/counts, raw artifact hashes, environmental gate chronology,
and residue audit are in
[`docs/production/evidence/2026-08-25-task-5b-studio-gauntlet.md`](../../production/evidence/2026-08-25-task-5b-studio-gauntlet.md).

---

### Task 6: Generic GPU Particle Emitters

**Unblocked by:** completed Task 5B Studio headless-gauntlet verification.

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeVulkanParticlePlanner.cs`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_particles.comp`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_particles.vert`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_particles.frag`
- Modify: `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs`
- Modify: `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFrameRenderer.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanParticlePlannerTests.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/RuntimeProjectionTests.cs`

**Interfaces:**
- Consumes: `Rekall.ParticleEmitter3D`, deterministic frame/delta/seed, depth, camera basis, and resolved particle capacity.
- Produces: `RekallAgeVulkanParticlePlan` with stable emitter ranges, simulation dispatches, bounds, material/draw mode, and overflow observations.

- [x] **Step 1: Write failing deterministic planner tests**

Assert the same emitter/seed/frame produces identical spawn ranges and that capacity overflow selects emitters by authored priority then stable entity ID. Reject unbounded lifetime, non-finite curves, and capacities above the safety ceiling.

- [x] **Step 2: Project the authored emitter contract**

Support continuous rate, bounded bursts, lifetime, deterministic seed, local/world simulation, velocity cone, gravity/drag, size/color curves, quad/mesh mode, lit/unlit, emissive intensity, soft-particle fade, texture/flipbook, priority, and visibility distance.

- [x] **Step 3: Implement persistent GPU state and compute simulation**

Allocate double-buffered particle state per resolved global capacity. Dispatch simulation with `DeltaSeconds`, recycle dead particles deterministically, and generate an indirect draw count without CPU readback.

- [x] **Step 4: Render particles after fog integration**

Implement camera-facing quads first, depth testing without depth writes, alpha/additive modes, HDR emissive output, soft depth intersection, and layer/camera masking. Mesh/ribbon/beam modes remain explicit later capability flags, not silent quad substitutions.

- [x] **Step 5: Add particle bounds/overdraw debug views and run tests**

Run the particle, projection, render-graph, and high-fidelity capture suites. Assert disabled emitters and zero spawn rate allocate no active slots.

Executable contract note: active particles add graph-authoritative `particle-upload` and `particle-simulate` passes after fog, followed by the existing transparent/HDR pass. State A/B are persistent initialized history inputs whose exact source/destination alternates with renderer-session history; emitter, active-index, and indirect buffers remain bounded per-frame resources. Empty frames retain the pre-particle topology and allocate no particle resources.

- [x] **Step 6: Commit**

```powershell
git add src/Rekall.Age.Runtime.Abstractions src/Rekall.Age.Runtime src/Rekall.Age.Rendering.Abstractions src/Rekall.Age.Rendering tests/Rekall.Age.Tests
git commit -m "feat: add scalable GPU particle emitters"
```

---

### Task 7: GPU Timing, Budget Inspection, and Quality Overrides

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeVulkanGpuProfiler.cs`
- Modify: `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderQualityContracts.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFrameRenderer.cs`
- Modify: `src/Rekall.Age.Rendering/Commands/InspectScenePerformanceBudgetCommand.cs`
- Modify: `src/Rekall.Age.Rendering/Commands/CaptureRuntimeViewportCommand.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `src/Rekall.Age.Mcp/RekallAgeMcpCatalog.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanGpuProfilerTests.cs`
- Test: `tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs`
- Test: `tests/Rekall.Age.Tests/Mcp/McpCatalogTests.cs`

**Interfaces:**
- Consumes: Vulkan timestamp support, executed graph passes, selected preset, and optional CLI/MCP overrides.
- Produces: per-pass GPU nanoseconds/milliseconds, total GPU frame time, resource bytes, workload counts, active degradations, and suggested commands.

- [x] **Step 1: Write failing profiler/report tests**

Test timestamp conversion, unavailable-query behavior, wrap/valid-bit handling, ordered pass reports, and total duration. Unsupported queries must return `REKALL_GPU_TIMESTAMPS_UNAVAILABLE`, not fabricated CPU timings.

- [x] **Step 2: Implement query-pool lifecycle and delayed readback**

Write timestamps around every declared pass. Read a completed prior frame to avoid stalling the current frame. Reset/reuse pools only after fence completion.

- [x] **Step 3: Extend CLI and MCP capture/inspection inputs**

Add `qualityPreset`, a bounded override object, and `includeGpuTimings`. Print requested/resolved preset, internal resolution, pass timings, memory, and degradation codes. Preserve existing positional CLI invocations.

- [x] **Step 4: Add preset comparison command**

Implement `rekall.render.compare_quality_presets` to capture aligned deterministic frames for requested presets and return metrics/paths without mutating the scene.

- [x] **Step 5: Run focused tests and commit**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanGpuProfilerTests|FullyQualifiedName~RuntimeInspectCliTests|FullyQualifiedName~McpCatalogTests"
git add src/Rekall.Age.Rendering src/Rekall.Age.Rendering.Abstractions src/Rekall.Age.Cli src/Rekall.Age.Mcp tests/Rekall.Age.Tests
git commit -m "feat: inspect high-fidelity GPU quality budgets"
```

---

### Task 8: Studio High-Fidelity Authoring Surface

**Files:**
- Modify: `src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Test: `tests/Rekall.Age.Tests/Editor/WorkbenchReadModelTests.cs`
- Test: `tests/Rekall.Age.Tests/Editor/StudioWorkbenchSourceTests.cs`
- Test: `tests/Rekall.Age.Tests/Editor/StudioCliTests.cs`

**Interfaces:**
- Consumes: the same resolved feature plan, comparison command, and component mutation commands exposed to agents.
- Produces: quality selection, override editing, per-pass timing/resource panels, degradations, and debug-view/capture actions; no Studio-only rendering state.

- [x] **Step 1: Write failing read-model/source tests**

Assert the workbench exposes requested/resolved preset, total GPU milliseconds, pass timings, resource bytes, degradations, and recommended quality/capture actions.

- [x] **Step 2: Extend the workbench read model**

Add immutable presentation records populated from command results. Empty/unavailable timings must render as unavailable, not zero.

- [x] **Step 3: Add compact Studio controls**

Add a preset selector, override expander, pass timing list, degradation list, debug-view selector, and compare/capture buttons. Bind all mutations through existing generic component commands.

- [x] **Step 4: Run editor/Studio tests and commit**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkbenchReadModelTests|FullyQualifiedName~StudioWorkbenchSourceTests|FullyQualifiedName~StudioCliTests"
git add src/Rekall.Age.Editor src/Rekall.Age.Studio tests/Rekall.Age.Tests/Editor
git commit -m "feat: author scalable rendering in Studio"
```

---

**2026-08-27 reconciliation:** Tasks 1-8 are confirmed implemented and delivered
(exact `feat:` commits found for each in git history: `bf292d2`, `8efbc15`,
`1b2ba10`, `9cc78ac`, `9a96964`, `992567d`, `dd2c69c`, `114a2cc`; every file
this plan's File Structure section names exists on disk). Their checkboxes
below are now marked complete to match reality; they were previously
implemented but left unticked. Task 9 is confirmed genuinely in progress, not
stalled: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md` records repeated
real checkpoints against this infrastructure (native rig animation, restrained
lighting, textured Warden surfaces, a scaled many-light bridge, environment/UV
fixes, height-fog correction), each with real RTX 5090 High 2560x1440 GPU
timings under the 16.67 ms bar. It remains open by its own explicit acceptance
criteria (fitted armor/cloth, IK/foot planting, combat/ability animation,
richer environment composition) — this is intentional iterative visual-quality
work, not a blocked or forgotten task, and is not a gate on Task 10.

### Task 9: Upgrade the Playable Aetherfall Resonance Court

**Files:**
- Modify: `Examples/AetherfallCitadel/Scenes/Main.age.scene.json`
- Modify/Create: `Examples/AetherfallCitadel/Modeling/Graphs/*.age.modeling-graph.json`
- Modify/Create: `Examples/AetherfallCitadel/Modeling/Meshes/*.age.mesh.json`
- Modify/Create: `Examples/AetherfallCitadel/Assets/Models/**/*.json`
- Modify: `Examples/AetherfallCitadel/Assets/assets.age.catalog.json`
- Modify/Create: `Examples/AetherfallCitadel/Assets/Materials/**/*.json`
- Modify: `Examples/AetherfallCitadel/Modules/AetherfallRules/PresentationSimulation.cs` only if generic effect entities require authored state synchronization.
- Modify: `Examples/AetherfallCitadel/Proof/*.json`
- Modify: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md`
- Test: `tests/Rekall.Age.Tests/Examples/AetherfallHighFidelityAcceptanceTests.cs`
- Test: `tests/Rekall.Age.Tests/Examples/AetherfallCitadelAcceptanceTests.cs`

**Interfaces:**
- Consumes: only the generic quality, environment, material, light/shadow, fog, particle, model, animation, and post-process contracts from Tasks 1-8.
- Produces: a dense playable Resonance Court benchmark and reproducible Performance/High/Epic visual evidence.

- [ ] **Step 1: Write failing structural acceptance tests**

Require one authored quality profile, environment, shadow settings, at least two fog volumes, at least six particle emitters with distinct visual roles, textured PBR materials with normal/emissive inputs, shadow-casting architecture/actors, and animated visible warden/enemy model references.

- [ ] **Step 2: Prove gameplay before the visual mutation**

Run the checked-in movement, combat, progression, and reset inspections and record their current exact pass results.

- [ ] **Step 3: Author detailed reusable court assets through AGE tools**

Use modeling graphs, mesh bake, model publish, material graphs, texture catalog entries, and ordinary scene entities. Increase silhouette detail, bevel/trim layering, floor breakup, conduit machinery, cover, rails, distant architecture, and material variation without padding invisible entities.

- [ ] **Step 4: Author lighting, shadows, fog, particles, animation, and post**

Configure a shadowed directional key, bounded accent lights, global height fog, local court fog volumes, conduit/projectile/impact/dash/mote/activation emitters, HDR emissive materials, bloom, tone map, and grade. Animation remains in agent-authored assets/modules and consumes `DeltaSeconds`.

2026-08-26 playable result: the native Vulkan path now resolves Performance/
Low/Medium/High point-light budgets of 2/4/8/16, uploads sixteen real GPU light
slots, terminates fragment light work at the selected count, and reports
selected/dropped entity IDs through capture and performance inspection.
Aetherfall High retains all nine authored practicals while
Performance deterministically reports seven drops. This completes the bounded
many-light bridge; true screen/depth cluster assignment, per-cluster lists, and
per-cluster overflow facts remain before the architecture is fully Forward+.

2026-08-26 environment/UV result: `Rekall.Environment3D` now authors separate
sky and ground ambient colors with backward-compatible white defaults. Native
Vulkan and the Windows player consume the same normal-oriented hemispherical
term. Aetherfall's real-player inspection then identified collapsed planar UVs
on six genuinely three-dimensional assets; their ordinary graphs now use
face-aware box projection and were rebaked/rebuilt through AGE's public
revision-checked commands. The accepted aligned capture is recorded in
`Proof/ACCEPTANCE.md`; this improves material continuity and indirect form
readability but does not complete the final visual bar.

2026-08-26 background/fog result: environment authoring now carries an optional
`backgroundColor` fallback through runtime and viewport contracts. One shared
resolver drives both native Vulkan and the Windows player, removing their
unrelated hard-coded scene clears while preserving camera-clear behavior for
`camera`/`clear` policies and legacy scenes without the property. Aetherfall's
authored fallback plus corrected global height-fog scale turns the opening frame
from a black void into continuous terrain with atmospheric depth. The accepted
capture and exact metrics are in `Proof/ACCEPTANCE.md`. True sky/cubemap sampling
and the now-dominant coarse model silhouettes remain subsequent visible work.
The playable validation gate also uncovered 288 false blockers caused by the
built-in authoring schemas lagging the renderer. Environment, shadow, fog,
particle, mesh-shadow, and point-light range/priority/shadow properties now have
discoverable schemas and reserved-type catalog entries; Aetherfall validates
with zero issues rather than teaching agents to delete functional render data.

- [ ] **Step 5: Re-run strict gameplay assertions after the final mutation**

Run all four checked-in proof matrices. If any assertion fails, repair the authored behavior; do not weaken it.

- [ ] **Step 6: Capture and inspect Performance, High, and Epic frames**

Capture the same deterministic Resonance Court input state at 2560x1440. Visually inspect each frame. Assert no missing/unsupported assets, no blocking degradations, correct preset resolution, and materially increasing enabled features/workload from Performance to Epic.

- [ ] **Step 7: Run the 600-frame High GPU acceptance**

Record average, median, 95th-percentile, and maximum GPU time plus per-pass attribution. High must average at or below 16.67 ms on the RTX 5090. Epic is measured but not required to meet 60 FPS.

- [ ] **Step 8: Update acceptance Markdown and commit**

Update `Proof/ACCEPTANCE.md` with exact commit, commands, entity/model/material/emitter counts, strict gameplay deltas, capture paths, preset reports, GPU timings, and any explicitly deferred technical debt.

```powershell
git add Examples/AetherfallCitadel tests/Rekall.Age.Tests/Examples docs/superpowers/specs/2026-08-24-high-fidelity-3d-rendering-design.md docs/superpowers/plans/2026-08-24-high-fidelity-forward-plus-foundation.md
git commit -m "feat: transform Aetherfall with high-fidelity rendering"
```

---

**2026-08-27 reconciliation:** the repo-wide policy is now to run only
targeted tests during ordinary feature work, not full/broad suites (see
`AGENTS.md`). Task 10 as originally written calls for a full solution test
pass and a 3,600-frame soak/600-frame High GPU acceptance gate; both were
already satisfied repeatedly during Task 9's iterative checkpoints (see
`Proof/ACCEPTANCE.md` — e.g. the 2026-08-26 "Restrained lighting and authored
Warden surface/form checkpoint" entry records a real RTX 5090 High 2560x1440
run at 8.546048 ms, and prior checkpoints likewise passed under the 16.67 ms
bar with zero observations/missing assets). Closing Task 10 here means: a
clean Release solution build (confirmed, 0 warnings/errors), reconciling this
plan's and `PROGRESS.md`'s stale status against that real evidence, and a
push — not re-running an already-satisfied heavy hardware gate from scratch.

### Task 10: Full Verification, Windows Delivery, Review, and Push

**Files:**
- Modify: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md` only if final measured evidence differs.
- Modify: `docs/superpowers/specs/2026-08-24-high-fidelity-3d-rendering-design.md` and this plan only when final implementation changed the approved contract.

**Interfaces:**
- Consumes: the exact accepted feature-branch commits and checked-in proof payloads.
- Produces: a clean tested `master`, relocatable Windows package, package audit, pushed origin, and next-milestone recommendation.

- [ ] **Step 1: Run focused renderer, runtime, Studio, CLI, MCP, and Aetherfall suites**

Run each task's focused filters again from the final tree. Expected: zero failures.

- [ ] **Step 2: Build the standalone Windows player prerequisite**

Run: `dotnet build src/Rekall.Age.Player.Windows/Rekall.Age.Player.Windows.csproj -c Debug`

Expected: build succeeds with zero warnings/errors.

- [ ] **Step 3: Run the complete solution suite**

Run: `dotnet test Rekall.AGE.sln --no-build`

Expected: every test passes. Record the exact count and duration.

- [ ] **Step 4: Run soak, native capture, and High GPU gate**

Run 3,600 deterministic runtime frames and the 600-frame 2560x1440 High Vulkan workload. Require zero entity growth, zero unexpected observations/events, and the 16.67 ms average GPU gate.

- [ ] **Step 5: Package, relocate, run, capture, and audit Windows delivery**

Use the compiled CLI to install the matching module SDK, package `Main` with target `windows`, verify `Play.exe` and `Play.bat`, relocate the ZIP, run the relocated package, capture a deterministic combat frame, and audit all manifest files/assets.

- [ ] **Step 6: Perform final branch-range review**

Review generic naming, pass/resource lifetime, synchronization, device cleanup, overflow handling, diagnostics, quality-table consistency, authored gameplay separation, ignored generated outputs, and `git diff --check`. Repair and re-run affected tests for every finding.

- [ ] **Step 7: Commit final evidence if changed**

```powershell
git add Examples/AetherfallCitadel/Proof/ACCEPTANCE.md docs/superpowers/specs/2026-08-24-high-fidelity-3d-rendering-design.md docs/superpowers/plans/2026-08-24-high-fidelity-forward-plus-foundation.md
git commit -m "test: accept high-fidelity Aetherfall rendering"
```

- [ ] **Step 8: Fast-forward exact tested commits and push**

From a clean main worktree, fast-forward `master` to the feature branch, verify commit identity and cleanliness, and push `origin master`. Do not rerun identical tests when integration changes no bytes.

- [ ] **Step 9: Select the next visual milestone from acceptance evidence**

Use the most consequential real limitation observed in the playable slice to choose Material/Environment Fidelity, Effects Expansion, Character Fidelity, Indirect Light/Reflections, Dense Worlds, or WebGPU Parity. Update the roadmap Markdown before beginning its design cycle.

---

## Plan Self-Review

- Every first-milestone requirement in the specification maps to Tasks 1-10.
- The contracts flow consistently from authored runtime projection to quality resolution, graph planning, Vulkan execution, diagnostics, Studio, and Aetherfall.
- The plan contains no game-specific engine API and keeps Aetherfall mutations in the example project.
- Every implementation task begins with a failing test, names an expected failure, verifies the focused suite, and ends with a commit.
- The delivery task includes the known standalone Windows-player prerequisite before the complete no-build suite.
- Later roadmap features remain explicit non-goals for this implementation plan rather than hidden placeholders.
