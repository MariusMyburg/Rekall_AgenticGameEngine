# Executable Custom Material Shaders Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make an agent-authored GLSL vertex/fragment pipeline assigned to a mesh execute identically in the Windows player and native Vulkan capture, with reflected ABI validation, diagnostics, hot reload safety, and package proof.

**Architecture:** A project shader resolver compiles GLSL to SPIR-V, reflects it through the maintained Vortice wrapper for Khronos SPIRV-Reflect, validates a versioned scene-material ABI, and returns a content-addressed immutable pipeline asset. Render batching carries the pipeline key per draw. Native Vulkan capture and the Windows player cache backend pipelines by that key and fall back only with structured diagnostics.

**Tech Stack:** C# / .NET 10, Shaderc, Vortice.SPIRV.Reflect, Veldrid Vulkan player, Silk.NET Vulkan capture, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-21-programmable-rendering-and-gpu-resources-design.md`

## Global Constraints

- GLSL 450 is the first canonical project shader language.
- Project shader paths stay confined below `Shaders/`.
- The initial material ABI is version 1 and uses the existing scene vertex layout and frame/draw/material resource sets.
- Invalid project shaders never replace a previously valid live pipeline.
- Native Vulkan capture and the Windows player consume the same resolved shader asset and pipeline key.
- Ordinary modules receive no raw graphics device.
- Diagnostics are bounded and never return unbounded shader or GPU data.
- No game-specific effect is added to the engine.

---

### Task 1: Reflected scene-material shader assets

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeProjectShaderPipelineResolver.cs`
- Create: `src/Rekall.Age.Rendering/RekallAgeSceneMaterialShaderAbi.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanShaderCompiler.cs`
- Modify: `src/Rekall.Age.Rendering/Commands/ShaderAuthoringCommands.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/ShaderAuthoringCommandTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanShaderCompilerTests.cs`

**Interfaces:**
- Consumes: `RekallAgeRuntimeViewportShaderPipeline`, project-root-confined `ShaderSourcePaths`, and native SPIR-V reflection.
- Produces: `RekallAgeResolvedShaderPipeline`, `RekallAgeShaderPipelineKey`, and `RekallAgeProjectShaderPipelineResolver.ResolveAsync(string, RekallAgeRuntimeViewportShaderPipeline, CancellationToken)`.

- [x] **Step 1: Write failing resolver and ABI tests**

Create project vertex/fragment files that implement the current locations and
sets, then assert successful resolution, stable SHA-256 keying, reflected
vertex elements, and three resource layouts. Add failures for a path escape,
missing stage, compile error, wrong vertex location, and wrong resource set.

```csharp
var result = await new RekallAgeProjectShaderPipelineResolver().ResolveAsync(
    root,
    new RekallAgeRuntimeViewportShaderPipeline("agent/tint", "agent/tint"),
    CancellationToken.None);

Assert.True(result.Valid, string.Join(Environment.NewLine, result.Errors));
Assert.Equal(64, result.Key.ContentHash.Length);
Assert.Equal(RekallAgeSceneMaterialShaderAbi.Version, result.AbiVersion);
Assert.Contains(result.VertexElements, element => element.Location == 0 && element.Format == "Float3");
```

- [x] **Step 2: Run the focused tests and verify red**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore -warnaserror --filter "FullyQualifiedName~ShaderAuthoringCommandTests|FullyQualifiedName~VulkanShaderCompilerTests"
```

Expected: failures because the resolver and ABI types do not exist.

- [x] **Step 3: Implement compilation, reflection, and exact ABI validation**

Define immutable public records:

```csharp
public readonly record struct RekallAgeShaderPipelineKey(string ContentHash);

public sealed record RekallAgeResolvedShaderPipeline(
    RekallAgeShaderPipelineKey Key,
    int AbiVersion,
    string VertexName,
    string FragmentName,
    string VertexSource,
    string FragmentSource,
    byte[] VertexSpirv,
    byte[] FragmentSpirv,
    IReadOnlyList<RekallAgeShaderVertexElement> VertexElements,
    IReadOnlyList<RekallAgeShaderResourceElement> Resources,
    bool Valid,
    IReadOnlyList<string> Errors);
```

Compile both stages with the existing compiler, reflect the resulting SPIR-V
through `Vortice.SPIRV.Reflect`, and compare reflected locations,
formats, resource names, kinds, stages, and sets against ABI version 1. Hash
ABI version, normalized names, source bytes, and SPIR-V bytes in deterministic
order. Return bounded errors with codes embedded in messages:
`REKALL_SHADER_STAGE_MISSING`, `REKALL_SHADER_COMPILE_FAILED`,
`REKALL_SHADER_VERTEX_ABI_MISMATCH`, and
`REKALL_SHADER_RESOURCE_ABI_MISMATCH`.

- [x] **Step 4: Make validate/assign use the same resolver**

When both project stages are known, `rekall.shader.assign_pipeline` must call
the resolver and reject ABI-incompatible pairs before scene mutation. Preserve
single-stage `rekall.shader.validate` for authoring feedback, but add the
pair-level ABI diagnostics to assignment.

- [x] **Step 5: Run focused tests and commit**

Run the command from Step 2 and expect all selected tests to pass, then commit:

```powershell
git add src/Rekall.Age.Rendering tests/Rekall.Age.Tests/Rendering
git commit -m "feat: validate reflected project shader pipelines"
```

### Task 2: Preserve shader identity through mesh batching

**Files:**
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneMeshModels.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneMeshBuilder.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneBatch.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneBatchBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanSceneMeshBuilderTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanSceneBatchBuilderTests.cs`

**Interfaces:**
- Consumes: `RekallAgeRuntimeViewportRenderable.ShaderPipeline`.
- Produces: nullable `RekallAgeRuntimeViewportShaderPipeline ShaderPipeline` on `RekallAgeVulkanSceneMesh` and `RekallAgeVulkanSceneDraw`.

- [x] **Step 1: Write failing propagation tests**

```csharp
var pipeline = new RekallAgeRuntimeViewportShaderPipeline("agent/tint", "agent/tint");
var frame = Fixture.FrameWithCube(shaderPipeline: pipeline);
var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, Fixture.EmptyAssets));
Assert.Equal(pipeline, mesh.ShaderPipeline);
var draw = Assert.Single(new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]).Draws);
Assert.Equal(pipeline, draw.ShaderPipeline);
```

- [x] **Step 2: Run the focused batch tests and verify red**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore -warnaserror --filter "FullyQualifiedName~VulkanSceneMeshBuilderTests|FullyQualifiedName~VulkanSceneBatchBuilderTests"
```

- [x] **Step 3: Add immutable pipeline references to mesh and draw records**

Copy the viewport pipeline reference without resolving files in the pure mesh
or batch builders. Preserve `null` as the engine default pipeline. Ensure mesh
chunking, imported GLB primitives, virtual geometry, skinning, and morph paths
copy the same reference to every derived mesh/draw.

- [x] **Step 4: Run focused tests and commit**

Run Step 2 and expect pass, then:

```powershell
git add src/Rekall.Age.Rendering tests/Rekall.Age.Tests/Rendering
git commit -m "feat: carry authored shader pipelines into draw batches"
```

### Task 3: Execute project pipelines in native Vulkan capture

**Files:**
- Modify: `src/Rekall.Age.Rendering/IRekallAgeVulkanSceneCapture.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- Create: `src/Rekall.Age.Rendering/RekallAgeVulkanScenePipelineCache.cs`
- Modify: `src/Rekall.Age.Rendering/Commands/CaptureRuntimeViewportCommand.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanSceneCaptureTests.cs`
- Test: `tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs`

**Interfaces:**
- Consumes: resolver from Task 1 and draw pipeline references from Task 2.
- Produces: capture overload accepting `projectRoot`; bounded `RekallAgeVulkanShaderPipelineUse` records in the capture result.

- [ ] **Step 1: Write failing native-capture tests**

Use an authored fragment shader that outputs a constant magenta color while
retaining ABI declarations. Capture a scene with one assigned cube and assert
the selected pipeline key is reported and the captured dominant color differs
from the engine-default control. Add a mixed default/custom scene and an
invalid-pipeline failure that names the entity and shader.

```csharp
Assert.Contains(result.ShaderPipelines, item =>
    item.EntityId == "cube" && item.Scope == "project" && item.Valid);
Assert.Empty(result.Errors);
```

- [ ] **Step 2: Run native-capture tests and verify red**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore -warnaserror --filter "FullyQualifiedName~VulkanSceneCaptureTests|FullyQualifiedName~RuntimeInspectCliTests"
```

- [ ] **Step 3: Build a per-capture Vulkan pipeline cache**

Resolve every distinct non-null pipeline before allocating GPU resources.
Create a `VkPipeline` for each valid key using the existing descriptor-set
layouts, vertex input, render pass, depth state, and transparent/opaque state.
Cache by `(RekallAgeShaderPipelineKey, bool transparent)`. During draw command
recording, bind the selected pipeline before its draw. Dispose all custom
pipelines and shader modules in reverse creation order.

- [ ] **Step 4: Fail closed and report bounded pipeline use**

If a referenced project pipeline is missing, invalid, or incompatible, return
a failed hardware capture with the resolver diagnostics; do not silently use
the default pipeline. Include at most 128 pipeline-use records containing
entity id/name, logical stages, hash, validity, and fallback status.

- [ ] **Step 5: Run focused tests and commit**

Run Step 2 and expect pass, then:

```powershell
git add src/Rekall.Age.Rendering tests/Rekall.Age.Tests/Rendering tests/Rekall.Age.Tests/Cli
git commit -m "feat: execute project shaders in Vulkan capture"
```

### Task 4: Execute and hot-reload project pipelines in the Windows player

**Files:**
- Create: `src/Rekall.Age.Player.Windows/RekallAgeVeldridShaderPipelineCache.cs`
- Create: `src/Rekall.Age.Player.Windows/RekallAgeProjectShaderHotReload.cs`
- Modify: `src/Rekall.Age.Player.Windows/Program.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VeldridShaderPipelineContractTests.cs`
- Create: `tests/Rekall.Age.Tests/Playback/WindowsPlayerSourceTests.cs`

**Interfaces:**
- Consumes: Task 1 resolved source/SPIR-V/reflection and Task 2 draw references.
- Produces: `RekallAgeVeldridShaderPipelineCache.Resolve(...)`, `InvalidateChangedFiles(...)`, and retained-last-valid behavior.

- [ ] **Step 1: Write failing cache and source-contract tests**

Extract player pipeline creation behind a testable cache contract. Assert one
creation per content key/transparency pair, correct pipeline selection per
draw, invalidation after a shader file change, and retention of the previous
valid pipeline when recompilation fails.

```csharp
var first = cache.Resolve(validAsset, transparent: false);
var second = cache.Resolve(validAsset, transparent: false);
Assert.Same(first.Pipeline, second.Pipeline);
cache.InvalidateChangedFiles([fragmentPath]);
Assert.True(cache.Resolve(invalidReplacement, false).RetainedPreviousValid);
```

- [ ] **Step 2: Run focused player contract tests and verify red**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore -warnaserror --filter "FullyQualifiedName~VeldridShaderPipelineContractTests|FullyQualifiedName~WindowsPlayerSourceTests"
```

- [ ] **Step 3: Implement Veldrid pipeline caching and draw selection**

Create project shaders with `ResourceFactory.CreateFromSpirv` from the resolved
GLSL sources and the ABI version 1 vertex/resource layouts. Cache opaque and
transparent pipelines separately by content key. In `DrawScenePacketPass`,
select the draw's cached pipeline or the engine default for `null`; keep frame,
draw, and material resource-set binding unchanged.

- [ ] **Step 4: Add bounded shader hot reload**

Extend the existing project watcher to include `Shaders/**/*.vert` and
`Shaders/**/*.frag`. Debounce changes, resolve on a worker, and enqueue only a
validated replacement for render-thread pipeline creation. On failure, retain
the previous pipeline and write one bounded player diagnostic with shader name
and compiler/ABI errors. Dispose superseded pipelines only after the current
submitted frame is complete; the first implementation may use
`GraphicsDevice.WaitForIdle()` at the debounced replacement boundary.

- [ ] **Step 5: Run focused tests and commit**

Run Step 2 and expect pass, then:

```powershell
git add src/Rekall.Age.Player.Windows tests/Rekall.Age.Tests
git commit -m "feat: run and hot reload project shaders in player"
```

### Task 5: Agent inspection, validation, and package integrity

**Files:**
- Create: `src/Rekall.Age.Rendering/Commands/InspectShaderPipelineCommand.cs`
- Modify: `src/Rekall.Age.Workflows/RekallAgeDefaultCommandRegistry.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `src/Rekall.Age.Validation/RekallAgeProjectValidator.cs`
- Modify: `src/Rekall.Age.Workflows/Commands/PackagePlayableGameCommand.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/ShaderAuthoringCommandTests.cs`
- Test: `tests/Rekall.Age.Tests/Validation/ProjectValidatorTests.cs`
- Test: `tests/Rekall.Age.Tests/Workflows/PlayablePackageIntegrityTests.cs`
- Test: `tests/Rekall.Age.Tests/Mcp/McpCatalogTests.cs`

**Interfaces:**
- Consumes: Task 1 resolver and capture pipeline-use evidence.
- Produces: `rekall.shader.inspect_pipeline` / `shader inspect-pipeline <root> <vertex> <fragment>`.

- [ ] **Step 1: Write failing command, validation, MCP, and package tests**

Assert inspection reports logical names, ABI version, SHA-256 key, SPIR-V byte
counts, four vertex elements, three resource layouts, and bounded diagnostics.
Assert project validation blocks an assigned ABI mismatch. Assert packaging
copies referenced shader sources, inventories their hashes, and package audit
executes a custom-shader proof frame.

- [ ] **Step 2: Run focused contract tests and verify red**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore -warnaserror --filter "FullyQualifiedName~ShaderAuthoringCommandTests|FullyQualifiedName~ProjectValidatorTests|FullyQualifiedName~PlayablePackageIntegrityTests|FullyQualifiedName~McpCatalogTests"
```

- [ ] **Step 3: Implement bounded inspection and validator integration**

Return no source text from inspection. Return at most 32 vertex elements, 16
resource layouts, 64 binding elements, and 32 diagnostics. Validate only
pipelines actually referenced by scene renderers and report entity/scene,
logical stage names, error code, and the exact inspect/write/assign action.

- [ ] **Step 4: Inventory project shader sources in packages**

Copy only project-root-confined shader files referenced by packaged scenes.
Include them in the existing immutable package manifest and hashes. Package
audit must fail if a referenced source is absent/tampered or no longer compiles
to the declared ABI. Preserve packages with no custom shaders unchanged.

- [ ] **Step 5: Run focused tests and commit**

Run Step 2 and expect pass, then:

```powershell
git add src/Rekall.Age.Rendering src/Rekall.Age.Workflows src/Rekall.Age.Cli src/Rekall.Age.Validation tests/Rekall.Age.Tests
git commit -m "feat: inspect and package custom shader pipelines"
```

### Task 6: Real hardware and installed acceptance

**Files:**
- Create: `Examples/CustomMaterialShader/rekall.project.json`
- Create: `Examples/CustomMaterialShader/Scenes/Main.age.scene.json`
- Create: `Examples/CustomMaterialShader/Shaders/agent/tint.vert`
- Create: `Examples/CustomMaterialShader/Shaders/agent/tint.frag`
- Modify: `docs/production/PROGRESS.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: all prior tasks.
- Produces: a generic custom-material example and retained native/player/package evidence.

- [ ] **Step 1: Create the example through public authoring commands**

Create a camera, light, floor, and two meshes. Assign the project shader only
to one mesh. The shader must use the ABI's frame/draw/material resources and
produce an unmistakable time-independent tint so captures are deterministic.

- [ ] **Step 2: Verify headless inspection and native Vulkan output**

```powershell
dotnet run --project src/Rekall.Age.Cli -c Release -- shader inspect-pipeline Examples/CustomMaterialShader agent/tint agent/tint
dotnet run --project src/Rekall.Age.Cli -c Release -- render viewport capture Examples/CustomMaterialShader Main 1 Examples/CustomMaterialShader/Captures/Hardware 960 540 vulkan
```

Expected: ABI version 1, valid reflected layout, hardware acceleration true,
RTX 5090 selected on this machine, informative frame, and one reported project
pipeline with no fallback.

- [ ] **Step 3: Launch and visually verify the Windows player**

```powershell
dotnet build src/Rekall.Age.Player.Windows/Rekall.Age.Player.Windows.csproj -c Release --no-restore -warnaserror
src/Rekall.Age.Player.Windows/bin/Release/net10.0-windows/Rekall.Age.Player.Windows.exe Examples/CustomMaterialShader Main --graphics --backend vulkan
```

Keep the window open long enough to confirm the default and authored materials
differ. Edit the fragment shader once to prove valid hot reload, then introduce
and repair one compile error to prove last-valid retention.

- [ ] **Step 4: Run the complete product gate**

```powershell
dotnet test Rekall.AGE.sln -c Release --no-restore -warnaserror
dotnet build Rekall.AGE.sln -c Release --no-restore -warnaserror
```

Expected: all engine and Studio tests pass; build has zero warnings/errors.

- [ ] **Step 5: Package, relocate, audit, and record evidence**

Use the existing game package, relocation, and consolidated audit workflows.
Verify the relocated package contains inventoried shader sources, runs its
Windows player, captures the custom result, and passes audit. Record test
counts, capture path/hash, package path/hash, device, pipeline key, hot-reload
result, and remaining render-resource tranches in `PROGRESS.md`.

- [ ] **Step 6: Commit and push the tranche**

```powershell
git add Examples/CustomMaterialShader docs src tests
git commit -m "feat: execute agent-authored material shaders"
git push origin codex/production-foundation
```
