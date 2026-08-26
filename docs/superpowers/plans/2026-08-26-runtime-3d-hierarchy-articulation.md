# Runtime 3D Hierarchy Articulation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make ordinary AGE entity hierarchies produce visible, animated 3D attachments and use the capability in Aetherfall.

**Architecture:** A frame-local resolver composes `Rekall.Transform3D` matrices through `parentId`, caches results, and publishes bounded viewport diagnostics. The render-frame builder uses those world transforms for every spatial rendering projection; Aetherfall remains an ordinary authored consumer using child entities and existing animation clips.

**Tech Stack:** .NET 10, C#, `System.Numerics`, xUnit, AGE scene/modeling JSON, Vulkan capture CLI

**Spec:** `docs/superpowers/specs/2026-08-26-runtime-3d-hierarchy-articulation-design.md`

## Global Constraints

- Keep engine behavior generic; do not add attack, weapon, character, or genre logic to engine core.
- Preserve current unparented transform behavior.
- Use AGE's existing scale → X/Y/Z rotation → translation matrix convention.
- Require strict runtime gameplay evidence after the Aetherfall scene mutation.
- Prioritize playable visible acceptance over exhaustive pathological hardening.

---

### Task 1: Frame-local 3D world-transform resolution

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeRuntimeWorldTransformResolver.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/ViewportContractTests.cs`

**Interfaces:**
- Consumes: `RekallAgeRuntimeWorld`, entity `ParentId`, and `RekallAgeRuntimeTransform`
- Produces: `Resolve(string entityId)`, plus bounded `RekallAgeRuntimeViewportObservation` diagnostics

- [x] **Step 1: Write failing frame-builder tests**

  Add literal parent/child scenes proving combined translation/rotation/scale,
  and separate missing-parent/cycle cases proving local fallback plus stable
  diagnostic codes.

- [x] **Step 2: Run the focused tests and confirm RED**

  Run `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~ViewportContractTests.RuntimeFrameComposesParented3DTransforms|FullyQualifiedName~ViewportContractTests.RuntimeFrameReportsInvalid3DTransformHierarchy"` and confirm the child remains local and diagnostics are absent.

- [x] **Step 3: Implement the resolver and integrate all spatial projections**

  Compose matrices in renderer order, cache per frame, convert the resulting
  quaternion back to finite XYZ Euler degrees, and use resolved transforms for
  cameras, renderables, lights, particles, and fog.

- [x] **Step 4: Run the focused tests and relevant rendering suite**

  Run the focused filter again, then `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~ViewportContractTests|FullyQualifiedName~ModelAssetRenderingTests|FullyQualifiedName~VulkanSceneMeshBuilderTests"`.

- [x] **Step 5: Commit the generic engine slice**

  Commit the resolver, frame integration, tests, spec, and plan as `feat: add runtime 3d transform hierarchies`.

### Task 2: Aetherfall articulated attachments

**Files:**
- Create: `Examples/AetherfallCitadel/Modeling/Graphs/aetherfall.warden-blade.graph.age.modeling-graph.json`
- Create: `Examples/AetherfallCitadel/Modeling/Graphs/aetherfall.warden-arm.graph.age.modeling-graph.json`
- Modify generated mesh/model/catalog artifacts through AGE authoring commands
- Modify: `Examples/AetherfallCitadel/Scenes/Main.age.scene.json`
- Test: `tests/Rekall.Age.Tests/Examples/AetherfallHighFidelityAcceptanceTests.cs`

**Interfaces:**
- Consumes: parent-local render transforms and existing `Rekall.AnimationClip` / `Rekall.AnimationPlayer`
- Produces: detailed model-backed child attachments parented to `warden`

- [ ] **Step 1: Write the failing Aetherfall acceptance test**

  Assert that the Warden has visible model-backed child attachments, that their
  transforms are local, and that a runtime frame advances at least one attachment
  rotation while its resolved world position follows Warden movement.

- [ ] **Step 2: Run the Aetherfall test and confirm RED**

  Run only the new acceptance test; confirm it fails because the articulated
  attachments do not exist.

- [ ] **Step 3: Author and publish detailed attachment meshes**

  Build blade and arm/pauldron graphs from generic primitives, transforms,
  joins, bevels, smooth normals, material assignment, and UV projection. Evaluate,
  bake, publish, and inspect each through AGE commands.

- [ ] **Step 4: Author child entities and motion**

  Parent the attachments to `warden`, set local pivots, attach ordinary animation
  components, and ensure their material/shadow settings match the dark Warden.

- [ ] **Step 5: Run the acceptance test and confirm GREEN**

  Run the new test and the existing Aetherfall high-fidelity acceptance class.

- [ ] **Step 6: Commit the authored consumer**

  Commit graphs, generated assets, catalog/model records, scene, and tests as
  `feat: articulate the aetherfall warden`.

### Task 3: Playable visual and gameplay acceptance

**Files:**
- Modify: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Consumes: the latest engine and Aetherfall bytes
- Produces: current real-player capture and deterministic gameplay evidence

- [ ] **Step 1: Build module and validate the latest scene**

  Build `Examples/AetherfallCitadel/Modules/AetherfallRules` and run strict scene
  validation.

- [ ] **Step 2: Capture combat motion in real Vulkan**

  Run the High-quality Vulkan capture with representative movement/combat input.
  Inspect the image and diagnostics; revise attachment pivots, lighting, camera,
  or authored mesh if the motion is unreadable or visually regresses.

- [ ] **Step 3: Run strict gameplay and render gates**

  Re-run movement, combat, progression, and reset assertion files after the final
  scene mutation, plus the Desktop60 High render-budget audit.

- [ ] **Step 4: Update evidence and verify the diff**

  Record exact capture/performance/gameplay facts, run focused tests and
  `git diff --check`, and inspect the final diff for game-specific engine logic.

- [ ] **Step 5: Commit and push**

  Commit as `feat: prove articulated aetherfall combat` and push the active
  branch only after fresh verification succeeds.

## Self-review

- The spec's hierarchy, diagnostics, authored-consumer, gameplay, and capture
  requirements each map to a task.
- No placeholder implementation steps remain.
- The same `RekallAgeRuntimeTransform` and viewport observation contracts are
  used consistently across tasks.
- Native deformable procedural skinning is explicitly preserved as later scope,
  not silently claimed by this milestone.
