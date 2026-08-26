# Split-Normal Authoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans
> to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** Add inspectable face-smoothing and edge-sharpness policies, bake
split weighted corner normals from them, and prove the result in Aetherfall.

**Architecture:** Store `normal.smooth` on faces and `normal.sharp` on edges as
editable source facts. Route mesh edits, modeling graphs, and modifier stacks
through one semantic executor that builds deterministic smooth corner fans and
emits renderer-ready corner normals.

**Tech Stack:** .NET 8/C#, AGE mesh contracts and modeling evaluator, xUnit,
JSON modeling graphs, native Vulkan capture.

**Spec:** `docs/superpowers/specs/2026-08-26-split-normal-authoring-design.md`

## Global Constraints

- Copy no Blender GPL implementation; use only its topology-domain and smooth-
  fan architecture as behavioral reference.
- Keep game-specific asset composition in Aetherfall and normal authoring in
  generic AGE contracts.
- Preserve stable topology IDs; these operations change attributes only.
- Every generated normal must be finite and unit length within `1e-6`.
- Use RED/GREEN TDD for each production behavior.
- Do not claim visual acceptance without a real High Vulkan capture and strict
  gameplay evidence after the final asset mutation.

---

### Task 1: Face and Edge Shading Policy Operations

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshNormalOperations.cs`
- Modify: `tests/Rekall.Age.Tests/Modeling/MeshNormalAuthoringTests.cs`

**Interfaces:**
- Produces: `shade_faces(smooth: bool, attribute: string = "normal.smooth")`
  on face selections.
- Produces: `mark_sharp(sharp: bool, attribute: string = "normal.sharp")`
  on edge selections.

- [x] **Step 1: Write failing policy edit tests**

  Add tests that construct a box, call `shade_faces` on one face and
  `mark_sharp` on one edge, and assert face/edge `Bool` attributes have the
  selected value while every unselected element retains the default. Assert a
  second inverse edit preserves unrelated values and increments revision once.

- [x] **Step 2: Run RED**

  Run:
  `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~MeshNormalAuthoringTests.Policy"`

  Expected: FAIL because the operation IDs are unknown.

- [x] **Step 3: Implement the typed operations**

  Add face/edge descriptors and executor switch cases. Implement a shared
  helper that validates an existing attribute's exact domain/type, fills a new
  `Bool` attribute with the operation default, updates resolved selected stable
  IDs, preserves topology and unrelated attributes, returns an attribute-only
  change set, and emits `REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT` for an
  incompatible existing attribute.

- [x] **Step 4: Run GREEN and commit**

  Run the filtered tests plus `MeshEditServiceTests` and
  `MeshOperationTests`. Commit as `feat: author mesh smoothing policy`.

---

### Task 2: Auto-Smooth Classification and Split Weighted Normals

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshNormalOperations.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.cs`
- Modify: `tests/Rekall.Age.Tests/Modeling/MeshNormalAuthoringTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modeling/MeshCompilerTests.cs`

**Interfaces:**
- Produces: `auto_smooth(angleDegrees, sharpAttribute = "normal.sharp")`.
- Extends: `weighted_normals(attribute = "normal.authored",
  faceAreaWeight = 1, cornerAngleWeight = 1,
  smoothAttribute = "normal.smooth", sharpAttribute = "normal.sharp")`.

- [x] **Step 1: Write failing classification tests**

  Add a bent two-quad fixture with one coplanar pair, one right-angle pair, and
  boundary edges. Assert 180 degrees keeps manifold edges smooth, 89 degrees
  marks the right-angle edge sharp, coplanar stays smooth at zero degrees, and
  boundaries are marked sharp. Add an explicit non-manifold fixture and assert
  its shared edge is sharp. Assert values are deterministic across two runs.

- [x] **Step 2: Write failing smooth-fan tests**

  On a curved three-face fan, assert unmarked smooth corners at one point share
  the same unit normal. Mark the middle boundary sharp and assert corners on
  opposite fans differ. Mark one face flat and assert its corners equal the
  exact face normal. Set nonzero area and corner-angle exponents and assert
  every output is finite and unit length. Compile the mesh and assert compiled
  normals equal the authored corner values and each tangent is orthogonal.

- [x] **Step 3: Run RED**

  Run the two focused test classes. Expected: failures for unknown
  `auto_smooth`, missing policy-aware parameters, and normals blended across
  the requested split.

- [x] **Step 4: Implement deterministic classification**

  Build edge-to-incident-face adjacency from face corner loops. Calculate
  finite Newell face normals. Write the sharp edge attribute using
  `dot(faceA, faceB) < cos(angleDegrees)` for exactly two incident faces and
  `true` otherwise. Require complete face selection and the closed 0–180 range.

- [x] **Step 5: Implement smooth corner fans**

  Precompute face normal, doubled area, and each corner's clamped interior
  angle. At each point, build a local corner adjacency graph through smooth
  two-face manifold edges. Traverse in stable corner-ID order. For every
  connected smooth component, sum normals using
  `pow(area, faceAreaWeight) * pow(angle, cornerAngleWeight)`, normalize once,
  and assign it to component corners. Assign exact face normals to flat-face
  corners. Preserve the existing all-smooth result when policy attributes are
  absent.

- [x] **Step 6: Run GREEN and commit**

  Run `MeshNormalAuthoringTests`, `MeshCompilerTests`, topology validation,
  bevel, solidify, and modifier-stack tests. Commit as
  `feat: bake split weighted mesh normals`.

---

### Task 3: Graph and Modifier Surfaces

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingNodeCatalog.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingGraphEvaluator.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModifierCatalog.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModifierStackEvaluator.cs`
- Modify: `tests/Rekall.Age.Tests/Modeling/MeshNormalAuthoringTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modeling/HardSurfaceModifierStackTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modeling/ComprehensiveModelingCatalogTests.cs`

**Interfaces:**
- Produces nodes: `rekall.modeling.shade_faces`,
  `rekall.modeling.mark_sharp`, `rekall.modeling.auto_smooth`.
- Extends node: `rekall.modeling.weighted_normals` with all split-normal
  parameters.
- Produces modifier: `rekall.modifier.auto_smooth` and extends
  `rekall.modifier.weighted_normals` identically.

- [x] **Step 1: Write failing descriptor and evaluation tests**

  Assert exact stable IDs, parameter names/types/defaults/ranges, graph
  evaluation order `box -> auto_smooth -> weighted_normals -> output`, and
  modifier order `auto_smooth -> weighted_normals`. Assert both paths emit the
  same sharp-edge and corner-normal values.

- [x] **Step 2: Run RED**

  Run the three focused classes. Expected: missing descriptor and evaluator
  routing failures.

- [x] **Step 3: Route descriptors through the semantic executor**

  Add typed catalog entries. Use complete face or edge domain IDs through
  `ApplySemanticOperation`; pass all parameters without reimplementing normal
  calculations. Add modifier routing with the same defaults.

- [x] **Step 4: Run GREEN and commit**

  Run focused normal, graph, modifier, catalog, compiler, and command schema
  tests. Commit as `feat: expose split normal authoring`.

---

### Task 4: Aetherfall Consumer and Playable Acceptance

**Files:**
- Modify: `Examples/AetherfallCitadel/Modeling/Graphs/aetherfall.warden.graph.age.modeling-graph.json`
- Modify: `Examples/AetherfallCitadel/Modeling/Graphs/aetherfall.weathered-ruin.graph.age.modeling-graph.json`
- Modify: corresponding published model documents and cooked mesh products
  through ordinary AGE commands
- Modify: `tests/Rekall.Age.Tests/Examples/AetherfallHighFidelityAcceptanceTests.cs`
- Modify: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md`
- Modify: `docs/production/2026-08-25-comprehensive-modeling-capability-matrix.md`
- Modify: `docs/production/PROGRESS.md`
- Modify: `docs/superpowers/plans/2026-08-26-split-normal-authoring.md`

**Interfaces:**
- Consumes: graph auto smoothing and split weighted normals.
- Produces: rebuilt Warden and weathered-ruin Model Assets plus retained visual,
  gameplay, validation, and performance evidence.

- [ ] **Step 1: Add failing Aetherfall acceptance assertions**

  Require both graphs to contain `rekall.modeling.auto_smooth` immediately
  before their weighted-normal node, require nonzero corner-angle weighting,
  and require published compiled normals to contain more than one distinct
  shading direction while remaining finite/unit length.

- [ ] **Step 2: Run RED**

  Run `AetherfallHighFidelityAcceptanceTests`. Expected: graph nodes absent.

- [ ] **Step 3: Mutate and rebuild through ordinary AGE commands**

  Insert auto smooth at 55 degrees for the Warden and 35 degrees for the ruin,
  rewire each final graph edge through it, set weighted normal output to
  `normal.authored` with area and corner-angle weights of 1, bake both graphs,
  and rebuild their existing Model Assets in place. Do not hand-edit cooked
  mesh bytes or add Aetherfall behavior to engine core.

- [ ] **Step 4: Run focused GREEN and capture Vulkan evidence**

  Run the consolidated normal/model/Aetherfall selection, then capture frame 30
  at 1280x720 High with native Vulkan into
  `Examples/AetherfallCitadel/Proof/Captures/SplitNormals`. Inspect the PNG for
  smoother curved Warden forms and preserved crisp masonry/bevel boundaries,
  with zero black-dot noise, missing assets, fallbacks, or observations.

- [ ] **Step 5: Re-run playable and performance gates**

  Run the checked-in movement/combat/progression/reset proof pairs and require
  2/4/4/5 strict passes. Run project validation, scene validation, and the High
  desktop60 budget at 1280x720; require zero issues and every budget within its
  configured limit.

- [ ] **Step 6: Record, verify, commit, and push**

  Record exact capture statistics, visual inspection, test counts, gameplay
  counts, validation, and timing. Update the modeling matrix from planned to
  the exact proven partial status. Run `git diff --check`, commit documentation
  as `docs: record split normal acceptance`, push
  `codex/high-fidelity-forward-plus`, and verify local/remote commit identity.
