# Curve Revolve Authoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a typed editable-curve revolution node and prove materially more
detailed Warden and citadel forms in the real Aetherfall Vulkan player.

**Architecture:** Extend the canonical modeling node catalog and evaluator with
one Curve-to-Geometry operation. Keep the curve as source truth, generate
deterministic evaluated topology plus UV/material/provenance/smoothing
attributes, then use ordinary graph bake and Model Asset rebuild commands for
the game consumers.

**Tech Stack:** .NET/C#, AGE modeling contracts/evaluator, JSON curve and graph
assets, xUnit, native Vulkan capture.

**Spec:** `docs/superpowers/specs/2026-08-26-curve-revolve-authoring-design.md`

## Global Constraints

- Copy no Blender GPL implementation; use only Spin/Screw source/evaluated
  separation and topology concepts as behavioral reference.
- Register exactly `rekall.modeling.curve.revolve@1`; do not add an
  Aetherfall-specific engine primitive.
- Accept one evaluated spline and preserve source/evaluated/cooked separation.
- Reject output above 2,000,000 points or faces before allocation.
- Emit finite deterministic topology and `uv.generated`, `curve.source.span`,
  `revolve.angle`, `material.index`, and `normal.smooth` attributes.
- Aetherfall mutations use revision-checked AGE commands; cooked mesh JSON is
  never hand-edited.
- Retain strict gameplay 2/4/4/5, zero validation issues, and passing
  `desktop60` after the final asset mutation.

---

### Task 1: Public Node Contract

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingNodeCatalog.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingGraphEvaluator.cs`
- Create: `tests/Rekall.Age.Tests/Modeling/CurveRevolveTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modeling/ComprehensiveModelingCatalogTests.cs`

**Interfaces:**
- Consumes: `RekallAgeModelingValueType.Curve` and `Geometry` graph ports.
- Produces: discoverable `rekall.modeling.curve.revolve@1` and evaluator
  dispatch to `CreateCurveRevolve(...)`.

- [x] **Step 1: Write the failing catalog/schema test**

  Add `CatalogPublishesTypedCurveRevolveContract` that finds the exact node and
  asserts a required Curve input named `curve`, a Geometry output named
  `geometry`, axis choices `x/y/z`, `angleDegrees` range `(0,360]`, `segments`
  range `3..4096`, world-unit `weldDistance`, Vector3 `origin`, and material/
  slot strings.

- [x] **Step 2: Write the failing evaluation dispatch test**

  Build a graph containing a two-point poly curve source, a revolve node with
  `axis=y`, `segments=8`, and an output node. Validate and evaluate it. The
  initial failure must identify the missing node descriptor/dispatch rather
  than malformed fixture data.

- [x] **Step 3: Run RED**

  Run:
  `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~CurveRevolveTests`

  Expected: failure because `rekall.modeling.curve.revolve` is not registered.

- [x] **Step 4: Register the exact descriptor and dispatch**

  Add a catalog node equivalent to:

  ```csharp
  Node("rekall.modeling.curve.revolve", "Curve Revolve",
      "Revolves one evaluated curve profile around an authored axis into deterministic UV/material/provenance-aware mesh topology.",
      [Input("curve", RekallAgeModelingValueType.Curve, true),
       Output("geometry", RekallAgeModelingValueType.Geometry)],
      [Text("axis", "Axis", "y", ["x", "y", "z"]),
       Vector3("origin", "Origin", 0),
       Number("angleDegrees", "Angle", 360, double.Epsilon, 360, "degree"),
       Integer("segments", "Segments", 32, 3, 4096),
       Number("weldDistance", "Weld Distance", 0.000001, 0, 1, "world-unit"),
       Text("materialAssetId", "Material Asset ID", "material.default"),
       Text("slotName", "Slot Name", "Revolved Surface")])
  ```

  Route evaluator dispatch to `CreateCurveRevolve(graph, node, incoming,
  values)` without a game-specific branch.

- [x] **Step 5: Run the schema tests and commit**

  Run the new schema test plus `ComprehensiveModelingCatalogTests`; require all
  selected tests green. Commit as `feat: expose curve revolve authoring`.

---

### Task 2: Deterministic Revolved Topology and Attributes

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingProductionGeometry.cs`
- Modify: `tests/Rekall.Age.Tests/Modeling/CurveRevolveTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modeling/ModelingGraphEvaluationTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modeling/MeshCompilerTests.cs`

**Interfaces:**
- Consumes: one `RekallAgeEvaluatedCurveSpline`, axis/origin/angle/segment/weld
  parameters, and the existing `BuildMesh`/validator boundary.
- Produces: `CreateCurveRevolve(...) -> RekallAgeMeshAsset` with deterministic
  valid faces and the five required attribute families.

- [x] **Step 1: Add failing topology cases**

  Cover all of these fixtures explicitly:

  - open two-sample profile, full 8-segment revolution: 16 points, 8 quads;
  - one endpoint on the axis: 9 points and 8 nondegenerate triangles;
  - cyclic profile: last profile span connects to the first;
  - 180-degree partial revolution: 9 angular rings and open seam boundaries;
  - x/y/z axes and nonzero origin produce finite expected bounds;
  - wrapped UV seam contains both U=0 and U=1 corner values;
  - profile V follows cumulative arc length, not sample index;
  - material slot/index, `normal.smooth`, provenance, and angle metadata have
    exact domain/value counts;
  - identical evaluation serializes identically;
  - invalid axis, angle, segments, non-finite origin/weld, coincident spans,
    and over-limit point/face products fail with stable diagnostics.

- [x] **Step 2: Implement bounded ring construction**

  Resolve the single curve input, axis unit vector and origin. Compute radial
  vectors and Rodrigues rotations without allocating output until checked
  point/face upper bounds pass. Reuse one point for every profile sample within
  `weldDistance` of the axis. Build wrapped or open angular spans and reduce
  collapsed quads to consistently wound triangles.

- [x] **Step 3: Emit seam-correct attributes**

  Generate UV values per emitted corner, using logical ring `segments` as U=1
  on wrapped seam corners even though their point IDs reuse ring zero. Normalize
  V by cumulative profile distance. Add deterministic source-span and angular
  point values, one material slot with face index zero, and face smoothing true.

- [x] **Step 4: Prove compiler compatibility**

  Compile the revolved output after auto smoothing and weighted normals. Assert
  finite unit normals, finite orthogonal tangents, expected material surface,
  and no zero-area or non-triangulable diagnostics.

- [x] **Step 5: Run GREEN and commit**

  Run `CurveRevolveTests`, `ModelingGraphEvaluationTests`, `MeshCompilerTests`,
  graph validation tests, and the modeling catalog selection. Commit as
  `feat: generate revolved curve meshes`.

---

### Task 3: Aetherfall Revolved Consumers

**Files:**
- Modify: `Examples/AetherfallCitadel/Modeling/Graphs/aetherfall.warden.graph.age.modeling-graph.json`
- Modify: `Examples/AetherfallCitadel/Modeling/Graphs/aetherfall.weathered-ruin.graph.age.modeling-graph.json`
- Modify: corresponding baked meshes, compiled model products, Model Asset
  documents, and catalog entries through AGE commands
- Modify: `tests/Rekall.Age.Tests/Examples/AetherfallHighFidelityAcceptanceTests.cs`

**Interfaces:**
- Consumes: `rekall.modeling.curve.source`,
  `rekall.modeling.curve.revolve`, material assignment, transform, join,
  auto-smooth, weighted normals, graph bake, and Model Asset rebuild.
- Produces: a shaped Warden armor form and a reusable citadel architectural
  dressing form in the existing published models.

- [ ] **Step 1: Write failing consumer assertions**

  Require both real graphs to contain at least one revolve node fed by a curve
  source. Require the Warden profile to contain at least six authored radial/
  height changes and at least 32 angular segments; require the ruin profile to
  contain at least eight changes and at least 24 segments. Assert the baked and
  compiled outputs contain the expected material, UV, normal, and varied-normal
  evidence and that live-linked source revisions are current.

- [ ] **Step 2: Run RED**

  Run the two consumer cases. Expected: no revolve nodes in either graph.

- [ ] **Step 3: Patch the Warden through AGE commands**

  Add an editable vertical armor profile describing waist pinch, rib flare,
  breastplate projection, gorget narrowing, and layered lower rim. Revolve it
  around Y with at least 32 segments, assign aged steel, transform it into the
  existing body, join it before the final weather/normal/UV chain, bake the
  Warden mesh, and rebuild `aetherfall-warden-dark-model`.

- [ ] **Step 4: Patch the ruin through AGE commands**

  Add a stepped capital/brazier profile with plinth, neck, bowl lip, and crown
  changes. Revolve it around Y with at least 24 segments, assign ruin trim,
  transform/copy it into the reusable module, join before the final weather/
  normal/UV chain, bake the mesh, and rebuild
  `aetherfall-weathered-ruin-model` so all existing instances consume it.

- [ ] **Step 5: Run consumer GREEN and commit**

  Require both cases green and retain the current compiled products in source
  control rather than leaving Model Assets pointed at ignored local files.
  Commit as `feat: add revolved aetherfall forms`.

---

### Task 4: Playable Visual Acceptance, Documentation, and Push

**Files:**
- Modify: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md`
- Modify: `docs/production/2026-08-25-comprehensive-modeling-capability-matrix.md`
- Modify: `docs/production/PROGRESS.md`
- Modify: `docs/superpowers/plans/2026-08-26-curve-revolve-authoring.md`

**Interfaces:**
- Consumes: final engine and Aetherfall bytes from Tasks 1-3.
- Produces: retained visual/playable/performance evidence and pushed commit
  identity.

- [ ] **Step 1: Run consolidated tests**

  Run revolve, curve, graph, compiler, catalog, command-schema, and complete
  Aetherfall high-fidelity selections. Record exact pass/fail counts; repair
  every failure in scope.

- [ ] **Step 2: Capture and inspect High Vulkan output**

  Capture frame 30 at 1280x720 High into
  `Examples/AetherfallCitadel/Proof/Captures/CurveRevolve`. Inspect the original
  PNG and compare with the split-normal capture. Require recognizable new armor
  curvature and architectural profile detail, preserved dark composition,
  and zero observations, missing/unsupported assets, fallbacks, or black-dot
  noise. If form detail is not legible, revise the profiles and recapture.

- [ ] **Step 3: Re-run playable and performance gates**

  Run the checked-in movement/combat/progression/reset inputs and assertions,
  requiring 2/4/4/5. Run project and scene validation, requiring zero issues.
  Run High `desktop60` at 1280x720 with GPU timings and require no blockers or
  warnings and every configured limit to pass.

- [ ] **Step 4: Record exact evidence**

  Update the acceptance ledger, capability matrix, progress ledger, and this
  checklist with actual node/mesh/capture/test/gameplay/validation/timing facts.
  Mark revolve partial and list screw/helix, caps, fields, multi-spline, mesh
  Spin, modifier, and Studio gizmo gaps explicitly.

- [ ] **Step 5: Verify, commit, and push**

  Run `git diff --check`, the final focused test selection, and clean-status
  checks. Commit documentation as `docs: record curve revolve acceptance`,
  push `codex/high-fidelity-forward-plus`, and require local HEAD to equal the
  remote branch HEAD with a clean worktree.
