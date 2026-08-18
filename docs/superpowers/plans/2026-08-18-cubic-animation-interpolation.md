# Bounded Cubic Animation Interpolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one bounded, deterministic Hermite interpolation contract for agent-authored clips and glTF `CUBICSPLINE` skeletal channels, then prove it from installed binaries.

**Architecture:** A focused runtime helper validates and samples scalar, flat-vector, and color cubic keys before any target mutation. The glTF reader preserves standard tangent/value triplets in channel records, and the skeletal sampler applies the same duration-scaled Hermite equation with quaternion normalization. Existing players, mixers, graphs, and inspection consume sampled output without special cases.

**Tech Stack:** C# 14, .NET 10, `System.Text.Json.Nodes`, `System.Numerics`, xUnit, existing Rekall AGE runtime/module/asset/CLI contracts.

**Spec:** `docs/superpowers/specs/2026-08-18-cubic-animation-interpolation-design.md`

## Global Constraints

- Cubic tangents are derivatives in value units per second and are multiplied by segment duration during Hermite evaluation.
- Authored cubic values are finite scalars, flat finite numeric arrays with 1..16 elements, or RGB/RGBA hexadecimal colors; strings, booleans, objects, and nested arrays are invalid.
- Every cubic key has `time`, `value`, `inTangent`, and `outTangent`; times are finite and strictly increasing in authored order.
- Existing limits remain 1,024 tracks per clip and 4,096 keys per track.
- Unknown interpolation modes fail closed instead of falling through to linear.
- glTF `CUBICSPLINE` output count is exactly three times its input time count; `LINEAR` and `STEP` remain count-matched one-to-one.
- Cubic quaternion output is normalized and near-zero/non-finite output fails closed.
- Morph-weight channels and curve-authoring UI are outside this tranche.

---

### Task 1: Authored clip Hermite validation and sampling

**Files:**
- Create: `src/Rekall.Age.Runtime/RekallAgeCubicAnimationSampler.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeTransformAnimationSystem.cs`
- Modify: `src/Rekall.Age.Runtime/Properties/AssemblyInfo.cs`
- Modify: `tests/Rekall.Age.Tests/Runtime/RuntimeAnimationTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Produces internal `RekallAgeCubicAnimationSampler.TryCreateKeys(JsonArray, out RekallAgeCubicAnimationKey[], out string issue)`.
- Produces internal `RekallAgeCubicAnimationSampler.Sample(IReadOnlyList<RekallAgeCubicAnimationKey>, double time)` returning a cloned `JsonNode`.
- `RekallAgeTransformAnimationSystem.ApplyTrack` routes only exact `cubic` tracks through this helper and emits stable observations before mutation.

- [ ] **Step 1: Write failing scalar, vector, color, and endpoint tests**

Add real runtime tests with two-key clips. Prove scalar `0 -> 6` over one
second with outgoing tangent 12 and incoming tangent 0 samples 4.5 at 0.5
seconds rather than the linear value 3. Prove a two-element vector samples each
component independently, RGB/RGBA output rounds and clamps, and exact key times
return the authored value.

- [ ] **Step 2: Run the focused tests and verify RED**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~RuntimeAnimationTests --verbosity minimal
```

Expected: the new cubic tracks sample through the old linear fallback and the
nonlinear assertions fail.

- [ ] **Step 3: Implement the bounded cubic helper and runtime routing**

Define immutable parsed keys containing time, value kind/components, and
in/out tangent component arrays. Evaluate each component with:

```csharp
var t2 = t * t;
var t3 = t2 * t;
var result = (2 * t3 - 3 * t2 + 1) * p0
    + (t3 - 2 * t2 + t) * duration * m0
    + (-2 * t3 + 3 * t2) * p1
    + (t3 - t2) * duration * m1;
```

Preserve exact scalar/vector/color output shape. Reuse the existing five-place
normalized-time rounding and clone endpoint values.

- [ ] **Step 4: Write failing malformed-input and compatibility tests**

Cover missing tangent, non-finite number, nested array, vector length mismatch,
wrong color tangent arity, duplicate/decreasing/non-finite time, cubic string,
and unknown interpolation. For every case assert the exact stable observation
and unchanged target property. Reassert step, linear, smooth, and smoothstep.

- [ ] **Step 5: Implement fail-closed validation and run all animation regressions**

Use `runtime.animation.cubic_key_invalid` for invalid cubic data and
`runtime.animation.interpolation_invalid` for unknown modes. Include the exact
`component.property` target in the bounded message. Do not accept stringified
numbers or partially valid keys for cubic input.

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~RuntimeAnimationTests|FullyQualifiedName~RuntimeAnimationStateGraphTests" --verbosity minimal
git diff --check
```

- [ ] **Step 6: Record evidence and commit**

```powershell
git add src/Rekall.Age.Runtime tests/Rekall.Age.Tests/Runtime docs/production/PROGRESS.md
git commit -m "feat: sample bounded cubic animation tracks"
```

---

### Task 2: glTF CUBICSPLINE import and skeletal execution

**Files:**
- Modify: `src/Rekall.Age.Assets/RekallAgeGlbSkeletalAnimationReader.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeSkeletalAnimationSystem.cs`
- Modify: `tests/Rekall.Age.Tests/Rendering/GlbTestMeshFactory.cs`
- Modify: `tests/Rekall.Age.Tests/Assets/GlbSkeletalAnimationReaderTests.cs`
- Modify: `tests/Rekall.Age.Tests/Runtime/RuntimeAnimationTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Extends `RekallAgeGlbNodeAnimationChannel` with nullable `IReadOnlyList<Vector4>? InTangents` and `OutTangents` after `Values`.
- `RekallAgeGlbSkeletalAnimationReader` returns triplet-decoded, count-matched channel data.
- `RekallAgeSkeletalAnimationSystem.Sample` handles exact `cubicspline` using the Task 1 Hermite numeric policy.

- [ ] **Step 1: Add a failing cubic glTF fixture and reader tests**

Add `GlbTestMeshFactory.CreateSingleJointCubicAnimatedGlb()` containing two
translation times and six VEC3 output records in input/value/output order.
Assert interpolation `cubicspline`, two values, two input tangents, and two
output tangents. Add malformed output-count and unsupported-interpolation
fixtures that must throw `InvalidDataException`.

- [ ] **Step 2: Run reader tests and verify RED**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~GlbSkeletalAnimationReaderTests --verbosity minimal
```

- [ ] **Step 3: Decode and validate standard glTF triplets**

Validate interpolation against `linear`, `step`, and `cubicspline` before
constructing a channel. For cubic, require `values.Length == times.Length * 3`
and split each triplet; otherwise require one value per time. Reject any
non-finite time/value/tangent and require strictly increasing cubic times.

- [ ] **Step 4: Add failing skeletal sampling tests**

Run the cubic fixture through the real runtime for 30 frames and assert the
joint translation is the nonlinear Hermite result. Add scale coverage and a
cubic quaternion fixture whose unnormalized component interpolation must emerge
as a finite unit quaternion. Add a near-zero quaternion rejection test.

- [ ] **Step 5: Implement skeletal Hermite sampling and verify regressions**

Use duration-scaled component Hermite for translation/scale. For rotation,
normalize the four-component result and reject length below `1e-8` or any
non-finite component. Keep step and linear/slerp paths byte-compatible.

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~GlbSkeletalAnimationReaderTests|FullyQualifiedName~RuntimeAnimationTests" --verbosity minimal
git diff --check
```

- [ ] **Step 6: Record evidence and commit**

```powershell
git add src/Rekall.Age.Assets src/Rekall.Age.Runtime tests/Rekall.Age.Tests docs/production/PROGRESS.md
git commit -m "feat: execute gltf cubic spline animation"
```

---

### Task 3: Agent schema and installed nonlinear proof

**Files:**
- Modify: `src/Rekall.Age.Modules/BuiltIns/RekallAgeInteractiveSubsystemComponents.cs`
- Modify: `tests/Rekall.Age.Tests/Modules/ModuleMetadataTests.cs`
- Modify: `eng/accept-distribution.ps1`
- Modify: `docs/production/2026-08-17-engine-maturity-audit.md`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Extends only the existing `animationTracks` description; no new component or command.
- Extends installed acceptance with a genre-neutral cubic property track and exact runtime/UI assertion.

- [ ] **Step 1: Write a failing schema discovery test**

Assert the `Tracks` description names `cubic`, the four-field key shape,
units-per-second tangents, scalar/flat-vector/color shapes, 16 components, and
the unchanged 1,024/4,096 bounds.

- [ ] **Step 2: Update schema metadata and run metadata/MCP regressions**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ModuleMetadataTests|FullyQualifiedName~Mcp" --verbosity minimal
```

- [ ] **Step 3: Add an installed cubic fixture and exact assertion**

Extend the neutral installed animation project with a panel track from X 20 to
140 over one second, outgoing tangent 240 and incoming tangent 0. Inspect at
frame 30 and require UI layout X 110.0, which distinguishes Hermite from the
linear value 80.0. Capture the resulting frame, require it informative and
nonblank, and keep all existing graph/package/runtime acceptance assertions.

- [ ] **Step 4: Run complete Debug verification**

```powershell
$env:TEMP = 'F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\TestTemp'
$env:TMP = $env:TEMP
dotnet test Rekall.AGE.sln --no-restore --verbosity minimal
```

- [ ] **Step 5: Run the canonical locked two-pass Release gate**

```powershell
$env:TEMP = 'F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\GateTemp'
$env:TMP = $env:TEMP
& .\eng\build.ps1
```

- [ ] **Step 6: Record exact evidence, review, and commit**

Record test counts/timings, exact installed nonlinear value, proof-frame hash,
soak measurements, archive size/hash, and explicit limitations. Update the
durable ledger and maturity audit from observed output only.

```powershell
git diff --check
git add src tests eng docs
git commit -m "test: gate installed cubic animation"
```
