# Native Rig Joint Attachments Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let any ordinary parented runtime entity follow a stable named joint from its parent entity's native AGE rig.

**Architecture:** Native rig evaluation publishes both pose-global and skin matrices. The shared render-frame world-transform resolver interprets a built-in `Rekall.RigAttachment` component and composes `local * jointPoseGlobal * parentWorld`, caching pose resolution per rig entity and emitting typed fallback observations.

**Tech Stack:** C#/.NET 10, `System.Numerics.Matrix4x4`, AGE native rig/runtime/rendering/component contracts, xUnit, Aetherfall executable acceptance.

**Spec:** `docs/superpowers/specs/2026-08-26-native-rig-joint-attachments-design.md`

## Global Constraints

- Core adds only a generic named-joint transform attachment; equipment and character behavior remain agent-authored.
- Attachment identity uses stable case-insensitive joint IDs, never persisted joint indices.
- Existing ordinary hierarchy composition and skin matrices remain byte-for-byte compatible when no enabled attachment exists.
- Invalid attachment authoring emits a typed warning and falls back to ordinary parent composition; it never hides the child.
- Real Aetherfall render-frame evidence must prove equipment motion from joint pose and exact inheritance of root movement.

---

### Task 1: Publish pose-global rig matrices

**Files:**
- Modify: `src/Rekall.Age.Modeling.Contracts/RekallAgeRigContracts.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeRigEvaluator.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/RigAuthoringTests.cs`

**Interfaces:**
- Consumes: `RekallAgeRigAsset` plus named local delta matrices.
- Produces: `RekallAgeEvaluatedRig.PoseGlobalMatrices` in the same order as `JointIds` and `JointMatrices`.

- [x] **Step 1: Write the failing evaluator test**

Add `RigEvaluatorPublishesPoseGlobalsSeparatelyFromSkinMatrices`. Evaluate the existing two-joint `HumanoidRig` with a chest rotation; assert `PoseGlobalMatrices.Count == 2`, that the chest pose global includes its bind translation, and that it differs from the chest skin matrix.

- [x] **Step 2: Run the focused test and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RigEvaluatorPublishesPoseGlobalsSeparatelyFromSkinMatrices`

Expected: compile failure because `RekallAgeEvaluatedRig` has no `PoseGlobalMatrices` member.

- [x] **Step 3: Implement pose-global publication**

Add an init-only `IReadOnlyList<IReadOnlyList<double>> PoseGlobalMatrices` property to `RekallAgeEvaluatedRig`, defaulting to an empty array for source compatibility. In `RekallAgeRigEvaluator.Evaluate`, serialize the already-computed `poseGlobals` array with the same finite row-major `Values` helper and set the property on the result.

- [x] **Step 4: Run the complete rig-authoring class green**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RigAuthoringTests`

Expected: all tests pass and existing skin-matrix assertions remain unchanged.

### Task 2: Add the built-in component and shared attachment resolver

**Files:**
- Modify: `src/Rekall.Age.Modules/BuiltIns/RekallAgeInteractiveSubsystemComponents.cs`
- Modify: `src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeRigPoseResolver.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeRuntimeWorldTransformResolver.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/ViewportContractTests.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/RigAuthoringTests.cs`

**Interfaces:**
- Consumes: parent `Rekall.RigPose`, child `Rekall.RigAttachment { jointId, enabled }`, existing `parentId`, and Task 1 pose globals.
- Produces: generic render-frame world transform `local * jointPoseGlobal * parentWorld` plus `runtime.transform.rig_attachment_*` observations.

- [ ] **Step 1: Write a failing render-frame attachment test**

Create a temporary two-joint rig, a parent with `Rekall.RigPose`, and a mesh child with `Rekall.RigAttachment { jointId: "chest" }`. Build a frame before and after changing only the chest delta. Assert the child renderable moves/rotates while its runtime local transform remains identical and the parent world offset is still included.

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RuntimeFrameComposesNamedRigAttachment`

Expected: the child retains ordinary parent composition and does not include the chest bind/pose matrix.

- [ ] **Step 3: Register the inspectable built-in component**

Add `RekallAgeRigAttachmentComponent` with `JointId` and default-true `Enabled`; register it next to `RekallAgeRigPoseComponent`. Assert the built-in catalog recognizes `Rekall.RigAttachment` in the focused test.

- [ ] **Step 4: Expose resolved pose matrices by joint ID**

Extend `RekallAgeRigPoseResolution` with an init-only case-insensitive joint-pose dictionary. Populate it from `evaluated.JointIds.Zip(evaluated.PoseGlobalMatrices)` while preserving existing skin and issue behavior.

- [ ] **Step 5: Compose attachments in the shared transform resolver**

Pass the frame builder's existing rig resolver into `RekallAgeRuntimeWorldTransformResolver`. Cache each parent rig resolution per frame. For enabled attachment children, validate the joint ID, parent pose, pose resolution, and named joint; on success decompose `childLocal * jointPoseGlobal * parentWorld`. On failure call the existing bounded `Report` path with the spec's exact code and compose `childLocal * parentWorld`.

- [ ] **Step 6: Add diagnostic fallback tests**

Add one theory covering blank joint, missing parent pose, invalid rig asset, and unknown joint. Each case must keep the child renderable at its ordinary parented position and emit exactly the expected typed transform warning.

- [ ] **Step 7: Run viewport, rig, and project-validator selections green**

Run the three relevant test classes and confirm existing hierarchy, component-catalog, and rig-skin behavior remains green.

### Task 3: Convert Aetherfall equipment into real rig attachments

**Files:**
- Modify: `Examples/AetherfallCitadel/Scenes/Main.age.scene.json`
- Modify: `tests/Rekall.Age.Tests/Examples/AetherfallHighFidelityAcceptanceTests.cs`

**Interfaces:**
- Consumes: Task 2 `Rekall.RigAttachment` and the Warden's `upper_arm_l` / `forearm_r` named joints.
- Produces: joint-local pauldron and runeblade entities whose rendered world transforms follow the actual Warden pose.

- [ ] **Step 1: Write failing Aetherfall acceptance**

Require the pauldron attachment joint to equal `upper_arm_l` and the runeblade joint to equal `forearm_r`. Build two frames from worlds that differ only in those named rig deltas; assert both rendered equipment transforms change while the equipment runtime local transforms remain unchanged.

- [ ] **Step 2: Run the focused acceptance and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~WardenUsesModelBackedParentedArticulationThatFollowsGameplayRoot`

Expected: missing `Rekall.RigAttachment` components.

- [ ] **Step 3: Rebase the authored equipment transforms**

Add `Rekall.RigAttachment` to the two entities. Rebase the pauldron from Warden-root position `(-0.84, 2.52, 0.02)` to upper-arm-local `(-0.12, 0.37, 0.02)`. Rebase the blade from `(1.24, 1.92, 0.18)` to forearm-local `(0.17, 0.37, 0.18)`. Preserve their authored rotation, scale, material, model reference, tags, and root parent ID.

- [ ] **Step 4: Run Aetherfall acceptance and inspect a native frame**

Run combined Aetherfall gameplay/high-fidelity acceptance. Capture a High Vulkan frame; reject and correct double transforms, detached equipment, exploded placement, or attachment observations rather than weakening assertions.

### Task 4: Verification, evidence, and delivery

**Files:**
- Modify: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md`
- Modify: `docs/production/PROGRESS.md`
- Modify: `docs/superpowers/plans/2026-08-25-comprehensive-agentic-modeling-expansion.md`

**Interfaces:**
- Consumes: verified generic and Aetherfall outputs.
- Produces: reproducible evidence and a pushed content-addressed commit.

- [ ] **Step 1: Run proportional final gates**

Run Release solution build, relevant rig/viewport/validation tests, combined Aetherfall acceptance, project and scene validation, module trust, and 2560x1440 High `desktop60` budget.

- [ ] **Step 2: Record exact evidence and residual gaps**

Document test counts, performance, capture path, Godot/Blender reference influence, diagnostic behavior, and that rigid joint attachment does not yet provide IK, sockets UI, or production animation clips.

- [ ] **Step 3: Audit, commit, and push**

Run `git diff --check`, inspect status for generated/transaction pollution, commit the exact intended files, push `codex/high-fidelity-forward-plus`, and verify local/remote commit identity plus a clean worktree.
