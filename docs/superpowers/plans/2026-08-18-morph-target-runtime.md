# Bounded Morph-Target Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry bounded glTF morph targets through generic agent-authored weights, runtime inspection, CPU deformation-before-skinning, and installed Vulkan proof.

**Architecture:** `Rekall.MorphWeights` is validated after generic animation sampling into runtime-only `Rekall.MorphState`. glTF import/mesh loading preserves one compatible ordered POSITION/NORMAL target layout and defaults. Render projection carries validated weights; mesh preparation applies weighted deltas before the existing skinning path and fails closed on count mismatch.

**Tech Stack:** C# 14, .NET 10, `System.Text.Json`, `System.Text.Json.Nodes`, `System.Numerics`, xUnit, existing Rekall AGE asset/runtime/rendering/CLI contracts.

**Spec:** `docs/superpowers/specs/2026-08-18-morph-target-runtime-design.md`

## Global Constraints

- Core behavior remains genre-neutral and never authors morph content.
- `Weights` contains 1..64 finite numbers with absolute value at most 1,000,000; values are not clamped to 0..1.
- glTF version 1 supports float VEC3 POSITION and optional NORMAL deltas only.
- Each target accessor count exactly matches the base vertex count.
- Each primitive has at most 64 targets and 4,194,304 total POSITION/NORMAL delta vectors; delta components have absolute value at most 1,000,000.
- All morph-bearing primitives in one rendered asset use one compatible ordered target layout.
- Explicit authored weights must exactly match target count; mismatch uses imported defaults and emits `REKALL_RENDER_MORPH_WEIGHT_COUNT_MISMATCH`.
- CPU morph deformation runs before skeletal skinning.
- Native glTF `weights` animation channels remain explicitly unsupported in this tranche.

---

### Task 1: Generic weight validation, state, and inspection

**Files:**
- Create: `src/Rekall.Age.Runtime/RekallAgeMorphWeightSystem.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs`
- Modify: `src/Rekall.Age.Modules/BuiltIns/RekallAgeInteractiveSubsystemComponents.cs`
- Modify: `src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs`
- Modify: `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Create: `tests/Rekall.Age.Tests/Runtime/RuntimeMorphTargetTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modules/ModuleMetadataTests.cs`
- Modify: `tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs`
- Modify: `tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Registers public component `Rekall.MorphWeights` with one `double[] Weights` property.
- Produces runtime-only `Rekall.MorphState` with `{version:1,weights:[...]}`.
- Adds `IReadOnlyList<RekallAgeRuntimeMorphState> MorphStates` to `RekallAgeRuntimeAnimationView` with a compatibility default.
- Adds runtime system id `runtime.animation.morph`, priority 5, after clip/graph/skeletal sampling and before downstream render projection.

- [x] **Step 1: Write failing schema and runtime tests**

Assert schema name, same-entity mesh guidance, 64-entry and ±1,000,000 bounds,
non-clamping, and generic clip/mixer/graph reuse. Run a world with valid weights
and assert exact persisted state. Animate `Rekall.MorphWeights.Weights` through
a real linear clip and a real cubic clip and assert post-animation runtime
state. The production mutation these tests catch is a system running before
animation or coercing/clamping weight values.

- [x] **Step 2: Run the focused tests and verify RED**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~RuntimeMorphTargetTests|FullyQualifiedName~ModuleMetadataTests" --verbosity minimal
```

Expected: schema/state/system assertions fail because no morph contract exists.

- [x] **Step 3: Implement component registration and runtime validation**

For each entity, remove stale `Rekall.MorphState` first. Accept only a flat
numeric array of 1..64 finite values in the inclusive magnitude range. Deep
clone valid values into runtime state; on failure emit one bounded
`runtime.animation.morph_weights_invalid` observation naming the entity and
actual bounded count/reason. Do not mutate `Rekall.MorphWeights`.

- [x] **Step 4: Add failing invalid-input, stale-state, and split-run tests**

Cover empty/excessive arrays, null/string/object/nested entries, NaN/infinity,
±1,000,001, component removal, valid negative/extrapolated weights, continuous
60 versus split 17+43 frames, and graph-driven catalog clips. Assert invalid
input publishes no state and one bounded observation.

- [x] **Step 5: Implement bounded projection and CLI output**

Define:

```csharp
public sealed record RekallAgeRuntimeMorphState(
    string EntityId,
    string EntityName,
    IReadOnlyList<double> Weights);
```

Projection reads runtime state only, caps output at 64 values, and sorts by
entity id. CLI prints one line per state with count and invariant-culture
bounded weights. It never dumps vertex deltas.

- [x] **Step 6: Verify runtime/schema/CLI regressions and commit**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~RuntimeMorphTargetTests|FullyQualifiedName~ModuleMetadataTests|FullyQualifiedName~RuntimeInspectCliTests|FullyQualifiedName~SceneRuntimeFoundationTests" --verbosity minimal
git diff --check
git add src tests docs/production/PROGRESS.md
git commit -m "feat: validate inspectable morph weights"
```

---

### Task 2: Bounded glTF morph metadata and mesh loading

**Files:**
- Modify: `src/Rekall.Age.Assets/RekallAgeAssetDocument.cs`
- Modify: `src/Rekall.Age.Assets/RekallAgeGlbMetadataReader.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneMeshModels.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeGlbMeshLoader.cs`
- Modify: `tests/Rekall.Age.Tests/Rendering/GlbTestMeshFactory.cs`
- Modify: `tests/Rekall.Age.Tests/Assets/AssetPipelineImportTests.cs`
- Create: `tests/Rekall.Age.Tests/Rendering/GlbMorphTargetLoaderTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Extends `RekallAgeGlbMeshMetadata` through init properties `MorphTargetCount`, `MorphTargetNames`, and `DefaultMorphWeights`.
- Extends `RekallAgeGlbNodeMetadata` through init property `MorphWeights` so node-instance overrides remain distinguishable from mesh defaults.
- Adds `RekallAgeVulkanSceneMorphTarget(string Name, IReadOnlyList<Vector3> PositionDeltas, IReadOnlyList<Vector3> NormalDeltas)`.
- Adds `MorphTargets` and `DefaultMorphWeights` init properties to `RekallAgeVulkanSceneMesh`.

- [x] **Step 1: Add a failing minimal morph GLB fixture and metadata/loader tests**

Build a triangle with base POSITION/NORMAL and two target objects, ordered names
`wide` and `raised`, mesh defaults `[0.25,-0.5]`, and a node override
`[0.5,0.75]`. Assert bounded import metadata and loaded per-emitted-vertex
deltas/defaults. Include a node transform and hand-derive that translation does
not affect deltas while scale/rotation do.

- [x] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~GlbMorphTargetLoaderTests|FullyQualifiedName~AssetPipelineImportTests" --verbosity minimal
```

- [x] **Step 3: Implement metadata discovery and bounded loader records**

Metadata assigns deterministic `target-N` fallbacks, limits names to 128
characters, records mesh defaults and node overrides separately without reading
unbounded binary values, and validates both weight-array counts against the
target layout. The mesh loader resolves node override, then mesh default, then
zero weights; validates target array count before accessor allocation; validates
exact accessor shape/count and finite magnitude; then remaps target deltas
beside each emitted chunk vertex.

- [x] **Step 4: Add failing adversarial loader tests**

Use real GLBs for 65 targets, target-vector total above 4,194,304, mismatched
accessor count, NaN and magnitude overflow, TANGENT/sparse/quantized accessors,
bad default counts, target-name overflow, and incompatible multi-primitive
layouts. Assert deterministic `InvalidDataException` messages and no partial
mesh result.

- [x] **Step 5: Complete validation, run non-morph/skinning loader regressions, and commit**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~GlbMorphTargetLoaderTests|FullyQualifiedName~GlbMeshLoader|FullyQualifiedName~GlbSkeletal|FullyQualifiedName~AssetPipelineImportTests" --verbosity minimal
git diff --check
git add src tests docs/production/PROGRESS.md
git commit -m "feat: import bounded gltf morph targets"
```

---

### Task 3: Morph deformation before skinning and fail-closed binding

**Files:**
- Modify: `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeRuntimeViewportAssetResolver.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneMeshBuilder.cs`
- Create: `src/Rekall.Age.Rendering/Commands/InspectSceneMeshGeometryCommand.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeRenderingModule.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `tests/Rekall.Age.Tests/Rendering/RuntimeRenderFrameBuilderTests.cs`
- Create: `tests/Rekall.Age.Tests/Rendering/VulkanSceneMorphTargetTests.cs`
- Create: `tests/Rekall.Age.Tests/Rendering/InspectSceneMeshGeometryCommandTests.cs`
- Modify: `tests/Rekall.Age.Tests/Cli/RenderingCliTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Adds nullable `RekallAgeRuntimeViewportMorph? Morph` to the end of `RekallAgeRuntimeViewportRenderable`.
- Defines `RekallAgeRuntimeViewportMorph(IReadOnlyList<double> Weights, bool AuthoredOverride)`.
- `RekallAgeVulkanSceneMeshBuilder` computes `ApplyMorph(mesh, morph)` before `ApplySkin(mesh, skin)`.
- Asset resolution emits `REKALL_RENDER_MORPH_WEIGHT_COUNT_MISMATCH` for explicit incompatible overrides.
- Adds generic read-only command `rekall.render.inspect_scene_mesh_geometry` and CLI `render mesh inspect <root> <scene> [frames]`, returning bounded final vertex/index counts and per-mesh post-morph/post-skin bounds without dumping vertex arrays.

- [x] **Step 1: Write failing exact deformation tests**

Construct a real loaded mesh with two targets and assert hand-derived weighted
positions, normalized normals, imported-default selection without a component,
and authored override selection with runtime state. Assert zero and negative
weights and exact base output at all-zero weights.

- [x] **Step 2: Run focused renderer tests and verify RED**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~MorphTarget --verbosity minimal
```

- [x] **Step 3: Implement projection and CPU morph application**

Read only `Rekall.MorphState`, convert values to finite floats, select exact
authored weights or node/mesh defaults, accumulate deltas, normalize/fallback
normals, and verify every output component. Preserve target/default records on
the returned mesh for inspection but never mutate cached asset meshes.

- [x] **Step 4: Write failing morph-before-skin and mismatch tests**

Create one-vertex hand-derived morph+joint data where reversing order produces
a different point; assert the glTF order result. Supply too few/too many
authored weights and assert one bounded asset issue, imported-default output,
and no partial override. Reassert procedural, authored geometry, virtual
geometry, non-morph GLB, and skeletal-only behavior.

- [x] **Step 5: Add the generic bounded final-mesh inspection path**

Write command and CLI tests first. Build the ordinary runtime frame, resolve
assets, and invoke the same `RekallAgeVulkanSceneMeshBuilder` used by Vulkan.
Return at most 256 mesh summaries sorted by entity/mesh id, each with final
vertex/index/triangle counts, finite min/max XYZ bounds, morph target count,
and applied weight source (`none`, `default`, or `authored`). Add one bounded
truncation warning when necessary. Do not expose raw vertex or delta arrays.

- [x] **Step 6: Implement ordering/mismatch checks and verify rendering regressions**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~MorphTarget|FullyQualifiedName~InspectSceneMeshGeometry|FullyQualifiedName~VulkanScene|FullyQualifiedName~RuntimeRenderFrameBuilder|FullyQualifiedName~RenderingCliTests" --verbosity minimal
git diff --check
```

- [x] **Step 7: Record evidence and commit**

```powershell
git add src tests docs/production/PROGRESS.md
git commit -m "feat: render morph targets before skinning"
```

---

### Task 4: Installed Vulkan proof and product gate

**Files:**
- Create: `eng/accept-installed-morph-animation.ps1`
- Modify: `eng/accept-distribution.ps1`
- Modify: `docs/production/2026-08-17-engine-maturity-audit.md`
- Modify: `docs/production/PROGRESS.md`
- Modify: this plan

**Interfaces:**
- Uses only shipped CLI/player binaries and a generated bounded glTF fixture.
- Proves asset metadata, generic authored animation, runtime state, CPU vertex movement, and native Vulkan frame output.

- [x] **Step 1: Add installed morph fixture and assertions**

Generate a visible triangle with one `raised` POSITION/NORMAL target, author a
generic cubic clip over `Rekall.MorphWeights.Weights`, and inspect frames 1 and
30. Require target name/default metadata, exact nonlinear weight, exact moved
vertex bounds from the shipped generic `render mesh inspect` command, zero
runtime/render issues, informative native Vulkan captures, and distinct
SHA-256 hashes. Independently derive the expected bounds in the acceptance
script and compare with invariant-culture numeric output. Keep the
desktop/windowed and package proofs unchanged.

- [x] **Step 2: Run complete Debug verification**

```powershell
$env:TEMP = 'F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\TestTemp'
$env:TMP = $env:TEMP
dotnet test Rekall.AGE.sln --no-restore --verbosity minimal
```

- [x] **Step 3: Run the canonical locked two-pass Release gate**

```powershell
$env:TEMP = 'F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\GateTemp'
$env:TMP = $env:TEMP
& .\eng\build.ps1
```

- [x] **Step 4: Record exact evidence and limitations**

Record test counts/timings, metadata, runtime weights, moved vertex bounds,
Vulkan backend/device, frame hashes, soak data, archive size/hash, and the
explicit native-glTF-weight-animation/TANGENT/sparse/compound-layout limits.

- [x] **Step 5: Review and commit**

```powershell
git diff --check
git add eng docs
git commit -m "test: gate installed morph target animation"
```
