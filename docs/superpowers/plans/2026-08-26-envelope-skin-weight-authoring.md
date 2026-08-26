# Envelope Skin-Weight Authoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add deterministic Blender-inspired, four-influence envelope skin-weight authoring and prove it through AGE graph/modifier contracts and Aetherfall deformation.

**Architecture:** A focused mesh operation owns envelope validation, segment-distance influence, strongest-four selection, normalization, and canonical attribute replacement. The modeling graph and modifier stack are thin adapters over that operation; Aetherfall consumes the same public node and native rig contracts without engine-side character behavior.

**Tech Stack:** C#/.NET 10, `System.Text.Json.Nodes`, AGE editable mesh/modeling graph/modifier/runtime contracts, xUnit, native Vulkan acceptance.

**Spec:** `docs/superpowers/specs/2026-08-26-envelope-skin-weight-authoring-design.md`

## Global Constraints

- Core systems remain genre-neutral; no humanoid, controller, locomotion, or combat behavior enters engine core.
- One through 256 finite envelopes produce one through four deterministic normalized influences.
- Preserve canonical `joint-indices-0`/`joint-weights-0` semantics and last-good source/model publication behavior.
- Aetherfall gameplay proof must compare actual rendered vertices after representative runtime frames.

---

### Task 1: Mesh envelope-weight operation

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshSkinOperations.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/SkinWeightAuthoringTests.cs`

**Interfaces:**
- Consumes: `RekallAgeMeshOperationRequest` with point IDs and structured `envelopes`.
- Produces: `assign_envelope_skin_weights` and canonical point `Int4`/`Float4` bindings.

- [x] Write a failing test with five overlapping envelopes that proves strongest-four selection, deterministic joint-index tie-break, normalization, and successful compilation.
- [x] Run the focused test and witness `REKALL_MESH_OPERATION_UNKNOWN`.
- [x] Implement strict envelope parsing, Blender-style segment/radius/falloff influence, per-joint maximum aggregation, top-four selection, normalization, nearest fallback, and canonical attribute replacement.
- [x] Add failing tests for malformed envelopes and `fallbackToNearest=false`, then implement exact mutation errors without changing the source mesh.
- [x] Run the complete `SkinWeightAuthoringTests` class green (12/12).

### Task 2: Graph node and modifier adapters

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingNodeCatalog.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingGraphEvaluator.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModifierCatalog.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModifierStackEvaluator.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/SkinWeightAuthoringTests.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/ModelingProductionContractMatrixTests.cs`

**Interfaces:**
- Consumes: `assign_envelope_skin_weights` from Task 1.
- Produces: `rekall.modeling.skin.envelope_weights` and `rekall.modifier.skin.envelope_weights` with structured envelopes, `maximumInfluences`, selection, and nearest-fallback parameters.

- [x] Write failing graph and modifier tests that compile the resulting four-influence mesh and inspect both descriptors.
- [x] Run focused tests and witness missing catalog/evaluator types.
- [x] Register both descriptors and route their parameters unchanged into the mesh operation.
- [x] Run graph, modifier, and production contract selections green (59/59 before the two final failure-path cases).

### Task 3: Aetherfall multi-joint consumer

**Files:**
- Modify: `Examples/AetherfallCitadel/Modeling/Rigs/aetherfall.warden.rig.age.rig.json`
- Modify: `Examples/AetherfallCitadel/Modeling/Graphs/aetherfall.warden.graph.age.modeling-graph.json`
- Modify: `Examples/AetherfallCitadel/Modeling/Meshes/aetherfall-warden-dark-mesh.age.mesh.json`
- Modify: `Examples/AetherfallCitadel/Assets/Models/aetherfall-warden-dark-model.age.model.json`
- Modify: `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallRulesSystem.cs`
- Test: `tests/Rekall.Age.Tests/Examples/AetherfallHighFidelityAcceptanceTests.cs`

**Interfaces:**
- Consumes: the graph node and stable native rig joint IDs from Tasks 1-2.
- Produces: multi-joint compiled Warden bindings and runtime-clock-authored pose deltas.

- [x] Write failing acceptance for named limb/spine joints, envelope node use, at least six used joint indices, and rendered vertex change across representative frames/input.
- [x] Expand the native rig, replace the two-joint linear node with explicit bone envelopes, rebake, rebuild, and update the agent-authored module pose.
- [x] Run focused acceptance, capture a native High Vulkan proof, and reject or repair visibly broken deformation rather than weakening assertions.

### Task 4: Verification, evidence, and delivery

**Files:**
- Modify: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md`
- Modify: `docs/production/PROGRESS.md`
- Modify: `docs/superpowers/plans/2026-08-25-comprehensive-agentic-modeling-expansion.md`
- Modify: `src/Rekall.Age.Core/Transactions/RekallAgeTransactionLogStore.cs`
- Test: `tests/Rekall.Age.Tests/Core/TransactionHistoryCommandTests.cs`

**Interfaces:**
- Consumes: verified engine and Aetherfall outputs.
- Produces: reproducible evidence, tracked compiled outputs, pushed commit.

- [x] Repair the bounded transaction journal exposed by the 23 MB graph bake, preserving the newest reversible history and snapshot-backed exact preimages; prove the same bake exits successfully.
- [x] Run the relevant modeling (61/61) and Aetherfall (44/44) selections, transaction tests (8/8), zero-warning solution build, project/scene validation, module trust, and 2560x1440 High `desktop60` budget (8.066848 ms GPU time).
- [x] Record exact counts, timings, capture path, residual visual gaps, and Blender/Godot reference influence.
- [x] Replace stale tracked compiled hashes with exact model references, run `git diff --check`, commit, and push.
