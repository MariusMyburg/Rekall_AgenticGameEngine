# Native Vulkan SSAO Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute quality-scaled depth-derived contact occlusion in AGE's native Vulkan renderer and prove its visible use in Aetherfall.

**Architecture:** Add a truthful `ssao-resolve` pass after opaque HDR and before fog, compile a dedicated fullscreen fragment shader, and execute it with sampled scene depth plus multiplicative HDR blending. Reuse the existing ambient-occlusion planner for High/Epic budgets and expose allocation, draw, and timing evidence through the native frame report.

**Tech Stack:** C#/.NET 10, Silk.NET Vulkan, GLSL compiled through shaderc, xUnit, Rekall AGE native viewport capture.

**Spec:** `docs/superpowers/specs/2026-08-26-native-vulkan-ssao-design.md`

## Global Constraints

- Keep gameplay and Aetherfall-specific behavior out of engine core.
- High uses 8 taps; Epic/Ultra use 12 taps through `RekallAgeInteractiveAmbientOcclusionPlanner`.
- Execute AO after opaque HDR and before fog/transparent effects.
- Reject clear/background depth and clamp the multiplier to a conservative non-black floor.
- Disabled SSAO creates no pipeline, descriptor, draw, or executed-pass evidence.
- Use the existing active-camera near/far/projection facts; do not infer a genre camera.
- Preserve strict gameplay proofs and zero-issue scene/project validation.

---

### Task 1: Make the render graph truthful

**Files:**
- Modify: `src/Rekall.Age.Rendering/RekallAgeHighFidelityRenderGraphBuilder.cs`
- Modify: `tests/Rekall.Age.Tests/Rendering/HighFidelityRenderGraphTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/InteractiveAmbientOcclusionPlannerTests.cs`

**Interfaces:**
- Consumes: `RekallAgeResolvedRenderFeaturePlan.Post.Ssao` and `RekallAgeInteractiveAmbientOcclusionPlanner.Plan(...)`.
- Produces: graph pass named `ssao-resolve` with reads `depth-buffer` and writes `scene-hdr`, ordered immediately after `opaque-hdr`.

- [ ] **Step 1: Write failing graph-order tests**

Add assertions equivalent to:

```csharp
var high = Build("High");
Assert.DoesNotContain(high.Resources, item => item.Name == "ssao-occlusion");
var ssao = Assert.Single(high.Passes, item => item.Name == "ssao-resolve");
Assert.Equal("graphics", ssao.Kind);
Assert.Equal(["depth-buffer"], ssao.Reads);
Assert.Equal(["scene-hdr"], ssao.Writes);
Assert.True(high.Passes.Single(p => p.Name == "opaque-hdr").Order < ssao.Order);
Assert.True(ssao.Order < high.Passes.Single(p => p.Name == "fog-integrate").Order);
Assert.DoesNotContain(Build("Performance").Passes, p => p.Name == "ssao-resolve");
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~HighFidelityRenderGraphTests|FullyQualifiedName~InteractiveAmbientOcclusionPlannerTests"
```

Expected: graph-order assertions fail because SSAO is a fictitious cluster output and no `ssao-resolve` pass exists.

- [ ] **Step 3: Implement the graph contract**

Remove the conditional `ssao-occlusion` resource, remove it from `ClusterWrites`/`OpaqueReads`, insert:

```csharp
if (plan.Post.Ssao)
{
    passes.Add(Pass("ssao-resolve", "graphics", ["depth-buffer"], ["scene-hdr"], nextOrder++));
}
```

immediately after `opaque-hdr` and before `fog-integrate`. Keep every later order monotonic.

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the Step 2 command. Expected: all selected tests pass and graph validation remains valid.

- [ ] **Step 5: Commit the truthful graph boundary**

```powershell
git add src/Rekall.Age.Rendering/RekallAgeHighFidelityRenderGraphBuilder.cs tests/Rekall.Age.Tests/Rendering/HighFidelityRenderGraphTests.cs
git commit -m "feat: declare native ssao resolve pass"
```

### Task 2: Compile a bounded depth-occlusion shader

**Files:**
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_ssao.frag`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanShaderCompiler.cs`
- Modify: `tests/Rekall.Age.Tests/Rendering/VulkanShaderCompilerTests.cs`

**Interfaces:**
- Consumes: fullscreen UV, combined scene-depth sampler at set 0 binding 0, and `SsaoPushConstants`.
- Produces: `RekallAgeVulkanHighFidelityShaderCompilationResult.Ssao` as a fragment SPIR-V module.

- [ ] **Step 1: Write the failing compiler contract**

Extend the post-pipeline test to require:

```csharp
Assert.Equal(RekallAgeVulkanShaderStage.Fragment, result.Ssao.Stage);
Assert.EndsWith("rekall_ssao.frag", result.Ssao.SourcePath, StringComparison.Ordinal);
Assert.NotEmpty(result.Ssao.Spirv);
Assert.Equal(0, result.Ssao.Spirv.Length % 4);
```

- [ ] **Step 2: Run the compiler test and verify RED**

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~VulkanShaderCompilerTests.CompileHighFidelityPostShadersProducesComputeAndGraphicsSpirvModules
```

Expected: compilation fails because `Ssao` is absent.

- [ ] **Step 3: Add the shader result and compiler input**

Compile `Shaders/rekall_ssao.frag` as a fragment stage and add it between
`AnalyticFog` and `Bloom` in `RekallAgeVulkanHighFidelityShaderCompilationResult`.
Include `ssao.Spirv.Length > 0` in `Compiled`.

- [ ] **Step 4: Implement bounded depth-derived AO**

The shader must expose:

```glsl
layout(set = 0, binding = 0) uniform sampler2D SceneDepth;
layout(push_constant) uniform SsaoParameters {
    vec4 TexelRadiusStrength; // inverse width, inverse height, radius px, strength
    vec4 DepthProjection;     // near, far, bias, orthographic flag
    vec4 Execution;           // sample count, frame rotation, floor, padding
} Params;
```

Use a fixed 12-direction unit disk, loop only to the clamped requested count,
rotate offsets from `Execution.y`, ignore center/sample depth at `>= 0.999999`,
compare reconstructed linear depth with `DepthProjection.z` bias, range-weight
each hit, and output `vec4(mix(1.0, floor, occlusion * strength))`. Keep every
normalize/divide guarded against zero.

- [ ] **Step 5: Run shader/compiler tests and verify GREEN**

Run the Step 2 command plus `FullyQualifiedName~InteractiveAmbientOcclusionPlannerTests`. Expected: all pass with non-empty aligned SPIR-V.

- [ ] **Step 6: Commit the shader contract**

```powershell
git add src/Rekall.Age.Rendering/Shaders/rekall_ssao.frag src/Rekall.Age.Rendering/RekallAgeVulkanShaderCompiler.cs tests/Rekall.Age.Tests/Rendering/VulkanShaderCompilerTests.cs
git commit -m "feat: compile native ssao shader"
```

### Task 3: Execute and report native SSAO

**Files:**
- Modify: `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- Modify: `tests/Rekall.Age.Tests/Rendering/VulkanHighFidelityCaptureTests.cs`
- Modify: `tests/Rekall.Age.Tests/Rendering/VulkanSceneCommandPlanTests.cs`

**Interfaces:**
- Consumes: compiled `Ssao`, sampled `SceneDepthImageView`, shared sampler, active-camera projection facts, `RekallAgeInteractiveAmbientOcclusionPlan`.
- Produces: optional Vulkan SSAO shader/layout/descriptor/pipeline, one fullscreen draw, `ssao-resolve` frame report, and optional GPU timing.

- [ ] **Step 1: Add failing plan/report integration tests**

Require High command plans/reports to contain an enabled `ssao-resolve` pass
with one draw and zero dispatches; require Performance to omit it. Extend the
native capture test to require an executed pass and an allocated
`scene-depth`/`depth-buffer` sampled resource without changing fallback rules.

- [ ] **Step 2: Run focused native tests and verify RED**

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanHighFidelityCaptureTests|FullyQualifiedName~VulkanSceneCommandPlanTests"
```

Expected: SSAO pass execution/report assertions fail.

- [ ] **Step 3: Add conditional Vulkan objects and cleanup**

When `plan.QualityPlan.Post.Ssao` is true, create:

```csharp
ShaderModule SsaoShader;
DescriptorSetLayout SsaoDescriptorSetLayout;
DescriptorSet SsaoDescriptorSet;
PipelineLayout SsaoPipelineLayout;
Pipeline SsaoPipeline;
```

Bind scene depth as `CombinedImageSampler`; create the pipeline against the HDR
render pass with load semantics, depth testing disabled, and multiplicative
RGB blending (`src=Zero`, `dst=SrcColor`). Destroy every nonzero handle in
`VulkanState.Dispose`.

- [ ] **Step 4: Record the pass with explicit transitions**

After opaque rendering, transition scene depth from
`DepthStencilAttachmentOptimal` to `ShaderReadOnlyOptimal`, begin the HDR load
render pass, bind the SSAO pipeline/descriptor, push:

```csharp
new SsaoPushConstants(
    new(1f / width, 1f / height, ao.RadiusPixels, ao.Strength),
    new(camera.NearClip, camera.FarClip, ao.Bias, camera.Orthographic ? 1f : 0f),
    new(ao.SampleCount, frame.FrameIndex % 64, 0.55f, 0f));
```

draw three fullscreen vertices, end the pass, and leave depth shader-readable
for fog. Wrap it in `gpuFrameQuery.BeginPass/EndPass("ssao-resolve")`.

- [ ] **Step 5: Make reports and capability validation truthful**

Add `ssao-resolve` to executed pass mapping with `Executed=true`, `DrawCount=1`,
`DispatchCount=0`; include sampled-depth format validation and a stable
`REKALL_SSAO_DEPTH_SAMPLING_UNSUPPORTED` diagnostic on failure. Report no SSAO
resource or pass when disabled.

- [ ] **Step 6: Run focused renderer tests and verify GREEN**

Run the Step 2 command plus:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~HighFidelityRenderGraphTests|FullyQualifiedName~VulkanShaderCompilerTests|FullyQualifiedName~InteractiveAmbientOcclusionPlannerTests"
```

Expected: zero failures and native integration evidence shows one SSAO draw.

- [ ] **Step 7: Commit native execution**

```powershell
git add src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs tests/Rekall.Age.Tests/Rendering/VulkanHighFidelityCaptureTests.cs tests/Rekall.Age.Tests/Rendering/VulkanSceneCommandPlanTests.cs
git commit -m "feat: execute native vulkan ssao"
```

### Task 4: Aetherfall visual and gameplay acceptance

**Files:**
- Modify: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md`
- Modify: `docs/production/PROGRESS.md`
- Modify: `docs/superpowers/plans/2026-08-26-native-vulkan-ssao.md`
- Test: `tests/Rekall.Age.Tests/Examples/AetherfallHighFidelityAcceptanceTests.cs`

**Interfaces:**
- Consumes: native `ssao-resolve` pass and unchanged Aetherfall High settings.
- Produces: retained capture path/statistics, gameplay/validation/budget evidence, and explicit residual visual gaps.

- [ ] **Step 1: Capture a real High Vulkan Aetherfall frame**

```powershell
dotnet run --project src\Rekall.Age.Cli --no-build -- render viewport capture Examples\AetherfallCitadel Main 30 Examples\AetherfallCitadel\Proof\Captures\NativeSsao 1280 720 vulkan '[]' High '{}' true
```

Expected: NVIDIA Vulkan, `ssao-resolve` reports one draw/timing, zero
observations/missing/unsupported/fallback assets, informative PNG.

- [ ] **Step 2: Inspect the PNG visually**

Use the local image viewer. Require stronger grounding at Warden feet, rubble,
gate, and wall intersections without black dots, halos, banding, or large
silhouette crushing. If the frame fails, repair generic radius/bias/strength
and repeat the same capture; do not hide failure by disabling SSAO.

- [ ] **Step 3: Run consolidated automated verification**

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~HighFidelityRenderGraphTests|FullyQualifiedName~InteractiveAmbientOcclusionPlannerTests|FullyQualifiedName~VulkanShaderCompilerTests|FullyQualifiedName~VulkanHighFidelityCaptureTests|FullyQualifiedName~VulkanSceneCommandPlanTests|FullyQualifiedName~AetherfallHighFidelityAcceptanceTests"
```

Expected: zero failures.

- [ ] **Step 4: Re-run gameplay, validation, and budget gates**

Run all four checked-in proof payload pairs through `runtime inspect`, then:

```powershell
dotnet run --project src\Rekall.Age.Cli --no-build -- validation project Examples\AetherfallCitadel
dotnet run --project src\Rekall.Age.Cli --no-build -- validation scene Examples\AetherfallCitadel Main
dotnet run --project src\Rekall.Age.Cli --no-build -- render performance budget Examples\AetherfallCitadel Main desktop60 30 1280 720 High '{}' true
```

Expected: gameplay assertions 2/4/4/5, zero validation issues, and within every desktop60 budget.

- [ ] **Step 5: Record exact evidence and residual gaps**

Append the capture path, distinct-color/luminance/draw/dispatch/timing facts,
visual inspection outcome, test count, gameplay counts, validation status, and
budget counts to Aetherfall acceptance and production progress. State plainly
that native depth-only SSAO is not the deferred normal-aware denoised solution.

- [ ] **Step 6: Verify, commit, and push**

```powershell
git diff --check
git status --short
git add Examples/AetherfallCitadel/Proof/ACCEPTANCE.md docs/production/PROGRESS.md docs/superpowers/plans/2026-08-26-native-vulkan-ssao.md
git commit -m "docs: record native ssao acceptance"
git push origin codex/high-fidelity-forward-plus
```

Expected: clean worktree and remote branch at the exact accepted commit.
