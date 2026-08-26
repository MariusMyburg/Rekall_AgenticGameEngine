# Comprehensive Agentic Modeling Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:subagent-driven-development or superpowers:executing-plans to
> implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** Expand AGE from its first editable-mesh foundation into a substantial,
Blender/Godot-informed built-in modeling system and use it to deliver visibly
detailed Aetherfall assets.

**Architecture:** Keep compact stable-ID source documents, deterministic
semantic operations, immutable modifier/graph evaluation, and derived cooked
products separate. Extend one descriptor-driven API consumed by agents, CLI,
MCP, Studio, bake, and runtime rather than adding game-specific generators.

**Tech Stack:** .NET 8/C#, AGE modeling contracts and atomic asset stores,
xUnit, JSON modeling graphs, WPF Studio, Vulkan/WebGPU render compilers.

**Spec:** `docs/superpowers/specs/2026-08-25-comprehensive-agentic-modeling-design.md`

## Global constraints

- Use local Blender/Godot source only as behavioral and architectural evidence;
  copy no GPL implementation.
- Preserve stable point/edge/face/corner identities where semantics survive and
  report explicit provenance where they do not.
- Every operation is deterministic, previewable, revision-safe, bounded,
  undoable, descriptor-driven, and strictly validated before publication.
- Preserve or explicitly report loss of attributes, material indices, UVs,
  normals, and selections.
- Keep game-specific composition in Aetherfall; add only generic authoring
  capabilities to engine projects.
- Do not accept catalog/test-only completion. Each tranche needs a visible
  Windows-player consumer and strict gameplay proof after final mutation.

---

### Task 1: Reference Matrix and Catalog Acceptance

**Files:**
- Create: `docs/production/2026-08-25-comprehensive-modeling-capability-matrix.md`
- Create: `tests/Rekall.Age.Tests/Modeling/ComprehensiveModelingCatalogTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Consumes: Blender BMesh/Geometry Nodes/modifier inventories and Godot
  importer/mesh-resource/render-storage boundaries.
- Produces: one versioned capability matrix and an exact Wave 1 descriptor gate.

- [x] **Step 1: Record the capability matrix**

  Map each spec capability to `implemented`, `partial`, or `planned`, its AGE
  operation/node/modifier/command ID, source file, focused test, and visible
  consumer. Record Blender/Godot reference paths and AGE's independent design
  decision for every row.

- [x] **Step 2: Write the failing Wave 1 inventory test**

  Assert the default catalogs contain the exact stable IDs:
  `bevel_edges`, `inset_faces`, `solidify`, `weighted_normals`,
  `rekall.modeling.mirror`, `rekall.modeling.array`,
  `rekall.modeling.curve.profile_sweep`, and additional primitive nodes for
  plane, disc, cylinder, cone, ico sphere, and capsule.

- [x] **Step 3: Run RED**

  Run:
  `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~ComprehensiveModelingCatalogTests`

  Expected: failure listing every missing Wave 1 descriptor.

- [ ] **Step 4: Keep the matrix honest during later tasks**

  Update rows only after their focused tests and visible consumer pass. `partial`
  must state the unsupported modes explicitly.

---

### Task 2: Production Bevel and Inset Kernel

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.cs`
- Create/Modify: `src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.Bevel.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.Inset.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingNodeCatalog.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingGraphEvaluator.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/BevelModelingGraphTests.cs`
- Create: `tests/Rekall.Age.Tests/Modeling/InsetModelingGraphTests.cs`

**Interfaces:**
- Produces:
  `bevel_edges(width, segments, profile, clampOverlap, hardenNormals)` and
  `inset_faces(thickness, depth, individual, boundary)` operations plus
  `rekall.modeling.bevel` and `rekall.modeling.inset` nodes.

- [ ] **Step 1: Extend bevel RED tests**

  Prove box and multi-face manifold cases for one and three segments, profile
  `0.5`, clamped width, material/UV propagation, stable provenance, deterministic
  replay, undo/redo, and invalid partial/non-manifold diagnostics.

- [ ] **Step 2: Implement segmented bevel independently**

  Generate inset face corners, ordered edge strips, profile rings, and vertex
  caps from AGE adjacency. Preserve source face material and corner attributes;
  create explicit new edge/face/corner IDs and provenance.

- [ ] **Step 3: Add inset RED tests and implementation**

  Test region and individual modes on quads/ngons, positive/negative depth,
  boundary policy, UV/material propagation, collapse rejection, and stable
  source-to-inset provenance.

- [ ] **Step 4: Expose graph nodes and run GREEN**

  Run the bevel, inset, topology-validation, compiler, graph, command, and
  undo/redo focused suites. Require zero failures.

---

### Task 3: Solidify, Mirror, Array, and Instances

**Files:**
- Create: `src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.Solidify.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeMeshMirror.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeGeometryArray.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingNodeCatalog.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingGraphEvaluator.cs`
- Modify: `src/Rekall.Age.Modeling.Contracts/RekallAgeModelingGraphContracts.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/HardSurfaceModifierTests.cs`

**Interfaces:**
- Produces:
  `solidify(thickness, offset, rim, evenThickness)`,
  `rekall.modeling.mirror(axis, origin, mergeDistance, bisect)`, and
  `rekall.modeling.array(count, offset, relativeOffset, instanceMode)`.

- [ ] **Step 1: Write RED tests**

  Prove an open plane solidifies into a closed shell; a half mesh mirrors with
  seam weld; a three-copy array has deterministic transforms; instance mode
  avoids duplicating source topology until realize; materials/attributes and
  source provenance survive.

- [ ] **Step 2: Implement solidify**

  Offset points from deterministic averaged normals, duplicate reversed inner
  faces, build boundary rims, reject non-finite/collapsing thickness, and emit
  explicit source/inner/rim provenance.

- [ ] **Step 3: Implement mirror and array**

  Mirror positions/winding/normal attributes around an authored plane; merge
  seam points through the existing weld kernel. Represent linked array copies
  as stable instances and add explicit realize evaluation.

- [ ] **Step 4: Run GREEN and update inventory**

  Run hard-surface, modifier-stack, graph-evaluation, mesh-compiler, and catalog
  tests. Mark only proven modes implemented.

---

### Task 4: Normal, Smoothing, and Tangent Toolchain

**Files:**
- Create: `src/Rekall.Age.Modeling/RekallAgeMeshNormalOperations.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshCompiler.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingNodeCatalog.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/MeshNormalAuthoringTests.cs`

**Interfaces:**
- Produces: `shade_faces`, `mark_sharp`, `auto_smooth`, `weighted_normals`,
  `set_custom_corner_normals`, `clear_custom_normals`, and tangent inspection.

- [ ] **Step 1: Write RED shading tests**

  Use a beveled box and curved surface to prove flat versus smooth faces,
  sharp-edge splits, angle-based auto smooth, face-area/corner-angle weighted
  normals, custom corner normals, and tangents generated per chosen UV map.

- [ ] **Step 2: Implement normal operations**

  Store authoring choices as face/edge/corner attributes. Recompute only
  invalidated islands and preserve explicit custom normals until an operation
  reports it cannot.

- [ ] **Step 3: Integrate compiler and inspection**

  Deduplicate render vertices by the complete position/UV/normal/tangent/
  material tuple and return split counts plus source-corner maps.

- [ ] **Step 4: Run GREEN**

  Run normal, compiler, Vulkan/WebGPU model rendering, and catalog tests.

---

### Task 5: Curve Documents and Profile Sweep

**Files:**
- Create: `src/Rekall.Age.Modeling.Contracts/RekallAgeCurveContracts.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeCurveValidator.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeCurveEvaluator.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeCurveProfileSweep.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingNodeCatalog.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingGraphEvaluator.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/CurveAuthoringTests.cs`

**Interfaces:**
- Produces: versioned poly/cubic-Bezier spline documents, stable spline/control
  point IDs, and nodes for line, circle, Bezier path, resample, reverse, trim,
  fillet, join, and `curve.profile_sweep`.

- [x] **Step 1: Write RED curve tests**

  Prove serialization, stable IDs, validation, Bezier sampling, cyclic seams,
  parallel-transport frames without flips, radius/tilt interpolation, curve
  resampling, and closed profile sweep with UV/material output.

  Current tranche proves document round-trip, stable-ID validation, cubic
  Bezier sampling, cyclic seam closure, radius/tilt interpolation, persisted
  resources, graph transport, sweep UV/material output, and source-span facts.
  The Aetherfall broken processional arch now consumes this contract through a
  persisted Bezier resource and published model. Deterministic line/circle
  builders plus typed reverse and uniform arc-length resample graph operations
  are now implemented with radius, tilt, and source-span preservation. Typed
  normalized arc-length trim, ordered endpoint join with automatic reversal,
  and bounded multi-segment corner fillet nodes complete the planned practical
  curve-operation inventory.

- [x] **Step 2: Implement curve source/evaluation**

  Keep source control data distinct from sampled evaluated points. Cache by
  curve revision, spline parameters, requested resolution, and dependencies.

- [x] **Step 3: Implement profile sweep**

  Sweep authored or built-in circle/rectangle profiles along stable frames,
  join cyclic seams, cap open ends by policy, and emit mesh provenance back to
  spline/control-point spans.

- [x] **Step 4: Expose the initial polyline profile-sweep node and run focused GREEN**

  Run curve, graph, bake, compiler, and render tests, including an arch trim,
  cable, and rail fixture.

---

### Task 6: Broader Primitive and Construction Inventory

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshPrimitiveFactory.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeMeshConstructionOperations.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingNodeCatalog.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingGraphEvaluator.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/ProductionPrimitiveTests.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/ConstructionOperationTests.cs`

**Interfaces:**
- Produces: plane/disc/cylinder/cone/ico-sphere/capsule primitives and bridge,
  fill holes, poke, connect, split, dissolve, bisect, loop-subdivide, and
  wireframe operations.

- [ ] **Step 1: Complete production primitive topology/attribute tests**

  Baseline exact-count, finite-position, catalog, and manifold tests are green.
  Still assert winding, caps, seams, UVs, normals, bounds, and minimum/default/
  high segment cases before marking this production-complete.

- [x] **Step 2: Implement the six requested primitives through shared builders**

  Reuse stable ring/grid construction helpers and descriptor validation; avoid
  separate ad-hoc topology logic for each node.

- [ ] **Step 3: Write RED construction-operation tests**

  Cover quads/ngons, boundaries, multiple islands, invalid selections,
  attributes, material indices, provenance, undo/redo, and deterministic replay.

  2026-08-25 tranche: focused RED/GREEN executor tests now cover simple-loop
  `fill_holes`, equal-cardinality `bridge_edge_loops`, face `poke_faces`,
  one-manifold-edge `dissolve_edges`, and complete-mesh one-sided
  `bisect_plane`, including deterministic replay, invalid selections,
  material/default attribute behavior, point interpolation, provenance, and
  explicit diagnostics for unsupported partial bisect and cut filling. This
  step remains incomplete until connect/split/collapse, loop subdivision,
  wireframe, broader island/ngon cases, and edit-service undo/redo coverage are
  executable.

- [ ] **Step 4: Implement and run GREEN**

  Run primitive, operation, validator, graph, command, and compiler suites.

  The focused modeling suite is green (162 tests) for the implemented tranche;
  the task stays unchecked because the remaining Task 6 operation inventory and
  primitive production attribute matrix are not complete.

---

### Task 7: UV Unwrap, Pack, and Attribute Preservation

**Files:**
- Create: `src/Rekall.Age.Modeling/RekallAgeUvIslands.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeUvProjection.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeUvUnwrapper.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeUvPacker.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingNodeCatalog.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/UvAuthoringTests.cs`

**Interfaces:**
- Produces: seam marking, island inspection, planar/box/cylindrical/spherical
  projection, angle/conformal unwrap policies, deterministic packing, and
  generated lightmap UV maps.

- [x] **Step 1: Write RED UV tests**

  Prove corner-domain seams, island discovery, projection orientation, unwrap
  continuity, non-overlapping pack bounds/margins, deterministic replay,
  multiple named maps, and tangent regeneration.

- [x] **Step 2: Implement projection and islands**

  Build islands from seam/boundary edges and map each projection using explicit
  axis/origin/scale policies.

- [ ] **Step 3: Implement bounded unwrap and pack**

  Add deterministic chart parameterization and stable size-descending packing.
  Reject degenerate charts with exact face IDs and repair actions.

  Current tranche provides deterministic seam-bounded dominant-plane charts,
  bounded grid packing, multiple named maps, and a separate lightmap channel.
  Angle/conformal parameterization, size-descending density-aware packing,
  partial-island selection, and face-specific degenerate repair facts remain.

- [x] **Step 4: Run GREEN**

  Run UV, tangent, compiler, material, GLB, and catalog tests.

---

### Task 8: Import, Reimport, Export, and Cook Contracts

**Files:**
- Create: `src/Rekall.Age.Modeling.Contracts/RekallAgeModelInterchangeContracts.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeModelImportService.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeModelCookService.cs`
- Modify: existing GLB/asset pipeline command registration files
- Test: `tests/Rekall.Age.Tests/Modeling/ModelInterchangeTests.cs`

**Interfaces:**
- Produces: `rekall.model.import`, `reimport`, `export`, `inspect_round_trip`,
  `cook`, and `inspect_cook` with source hashes, settings, dependencies,
  provenance/license fields, loss reports, and cooked artifact hashes.

- [ ] **Step 1: Write RED import/cook tests**

  Cover GLB mesh/material/UV/normal/skin input, OBJ/MTL, PLY, STL, unit/up-axis
  conversion, reimport stability, missing dependencies, malformed input,
  unsupported-feature reports, and atomic last-good preservation.

- [ ] **Step 2: Normalize imports into AGE source documents**

  Never edit external files or make renderer buffers the source of truth.
  Persist importer identity, version, source hash, settings, and dependency map.

- [ ] **Step 3: Add deterministic cooking**

  Produce render surfaces, bounds, collision, navigation, LODs, meshlets, and
  target material products through independent stages with hashes/timings.

- [ ] **Step 4: Run round-trip and package tests**

  Require complete loss reports and packaged inclusion of every referenced
  cooked artifact.

---

### Task 9: Descriptor-Driven Agent and Studio Modeling UX

**Files:**
- Modify: `src/Rekall.Age.Modeling/Commands/*`
- Modify: `src/Rekall.Age.Mcp/RekallAgeMcpCatalog.cs`
- Modify: `src/Rekall.Age.Studio/ModelingWorkspace.xaml`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioModelingSession.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/AdvancedAuthoringCommandTests.cs`
- Test: relevant Studio modeling source/layout tests

**Interfaces:**
- Consumes: the canonical operation/node/modifier/curve/UV/import descriptors.
- Produces: search/inspect/query/preview/apply/batch/undo evidence for agents and
  point/edge/face/curve/UV/modifier modes in Studio through the same commands.

- [ ] **Step 1: Write RED schema/discovery tests**

  Assert every catalog descriptor is callable through CLI/MCP and renders a
  typed Studio parameter editor with ranges, units, enums, defaults, and
  validation diagnostics.

  2026-08-26 tranche: persisted curve resources are now callable through the
  default registry and MCP catalog with typed `create`, optimistic `replace`,
  `inspect`, deterministic `list`, and bounded `evaluate` commands. The JSON
  closed-loop proves stable IDs, Bezier enum binding, bounded evaluation
  samples, and logical/file revision advancement. Studio curve-resource
  creation and the complete all-descriptor editor matrix remain.

  2026-08-26 Studio tranche: modeling descriptors now include an explicit
  structured-JSON value type. The curve-source document editor parses and
  returns JSON objects/arrays instead of converting documents into quoted
  strings, rejects malformed input, and preserves edited curve resources
  through the generic parameter surface. Focused Studio session tests pass.

- [ ] **Step 2: Add generic selection and inspection commands**

  Implement grow/shrink/loop/ring/island/material/sharp/seam queries and named
  selection persistence with bounded samples and stable IDs.

- [ ] **Step 3: Add Studio modes and previews**

  Route picking, selection, gizmos, overlays, operation preview, modifier stack,
  UV inspection, and before/after evidence through canonical session methods.

- [ ] **Step 4: Verify real Studio**

  Run source/binding/layout tests and visually inspect populated, empty, busy,
  and failed modeling states in the real Windows Studio.

---

### Task 10: Aetherfall Detailed-Asset Acceptance

**Files:**
- Modify/Create: `Examples/AetherfallCitadel/Modeling/Graphs/*`
- Modify/Create: `Examples/AetherfallCitadel/Modeling/Meshes/*`
- Modify/Create: `Examples/AetherfallCitadel/Assets/Models/*`
- Modify: `Examples/AetherfallCitadel/Scenes/Main.age.scene.json`
- Modify: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md`
- Modify: `.superpowers/sdd/2026-08-24-high-fidelity-forward-plus-foundation/task-9-report.md`
- Test: `tests/Rekall.Age.Tests/Examples/AetherfallHighFidelityAcceptanceTests.cs`

**Interfaces:**
- Consumes: at least five proven Wave 1 operations/nodes.
- Produces: detailed ruins, processional gate, Warden, sentinel, weapons, trim,
  rail/cable/ornament, rubble, and props plus same-view visual proof.

- [ ] **Step 1: Preserve the rejected baseline**

  Record the overly bright/gray, visibly low-poly frame and the controller
  rejection. It is a regression baseline, not acceptance evidence.

- [ ] **Step 2: Author reusable high-detail kits**

  Use bevel/inset/solidify/mirror/array/weighted normals/curve sweep and material
  assignments in ordinary AGE graphs. Hero meshes must add meaningful silhouette
  and surface-form detail rather than tessellation-only density.

  2026-08-26 checkpoint: the reusable weathered-ruin module now adds masonry
  courses, facade ribs, pilaster caps, and damaged crown silhouettes; the
  Warden adds layered head, shoulder, waist, leg, boot, and weapon forms. Both
  use ordinary generic AGE graph nodes and remain editable source assets.

  Later 2026-08-26 checkpoint: Warden and hollow-sentinel graphs now separate
  smooth primitive branches from hard-surface branches before segmented bevel,
  avoiding the topology explosion caused by beveling every sphere/capsule edge.
  The Warden also corrected axial primitive transforms and the sentinel gained
  complete lower-body anatomy. A weathered-ruin CSG trial exposed that boolean
  outputs can still contain n-gons rejected by explicit triangulation or model
  compilation as zero-area/non-triangulable. The accepted kit therefore uses
  constructive pier/header openings; robust boolean cleanup and triangulation
  interoperability remain explicit engine work rather than being hidden in the
  example.

  Rounded-character checkpoint, 2026-08-26: executable acceptance exposed that
  the external "pauldron" graph duplicated an upper arm, elbow, vambrace, and
  spike. It is now a compact 19-node layered shell with a rolled rim, boss, and
  rivets. The 71-node main Warden replaces rectangular coat panels, box boots,
  and the inset box visor with smooth capsule coat tails, boots, and faceplate,
  routed around the hard-surface bevel chain. This removes visible blockout
  artifacts with ordinary generic graph primitives while retaining editability;
  anatomy, layered armor, surface wear, and richer animation remain open.

  Layered-character checkpoint, 2026-08-26: the main consumer now uses 84
  reachable nodes, with mirrored shoulder shells and knee cops, arrayed
  abdominal and full-width chest lames, and a capsule/torus helmet crest and
  brow. Native review rejected a paired short-capsule chest variant before
  acceptance. The authored and module-driven camera was tightened to a
  14.5-unit height, 15-unit follow offset, 40-degree pitch, and 38-degree FOV so
  these forms remain readable during actual play. This closes the immediate
  blocky-silhouette/framing pass; richer materials, multi-joint deformation,
  locomotion/combat animation, and final character proportions remain open.

- [ ] **Step 3: Bake, publish, catalog, and place**

  Publish immutable models, preserve material slots/UVs/tangents, place coherent
  modular architecture and props, and keep gameplay logic in Aetherfall modules.

  2026-08-26 checkpoint: both graphs were rebaked and their existing live-linked
  models rebuilt in place, so all 30 ruin instances and the player consume the
  improved geometry without game-specific engine behavior. This is a partial
  checkpoint; material-slot separation and additional environment kits remain.

  Later 2026-08-26 checkpoint: compiled surface material IDs now resolve through
  the standard runtime PBR path, including graph-owned texture dependencies and
  scalar/color factors. The Warden is the first Aetherfall two-surface consumer
  (aged steel plus charcoal cloth). The compiler now groups faces by material
  slot, producing two draw surfaces instead of 21 alternating runs. Additional
  architecture kits and richer texture/material authoring remain.

  The repeated weathered-ruin model now also uses the same generic contract:
  its structural mass and carved trim are authored as separate material graph
  branches, compiled to exactly two coalesced surfaces, and rebuilt once for all
  30 scene instances. The settled Vulkan frame proves the surfaces resolve with
  no observations or fallbacks. Both graph base-color factors are intentionally
  near-neutral because the placed entities already author varied masonry tints;
  this avoids double-darkening their colors while preserving reusable PBR
  roughness and metallic response. Remaining scene work must improve shadowed
  masonry readability and soften the harsh lit-trim contrast without flattening
  the requested deep-black presentation.

  The processional arch now applies the same semantic-surface contract to a
  more demanding curve consumer: two 96-sample rectangular Bézier sweeps form
  layered inner/outer archivolts, while the wall mass and carved columns,
  keystone, coping, and finials remain independently material-addressable. The
  published output retains exactly two coalesced surfaces and passes the
  existing 8,000-point detail floor without reverting to the rejected round-ring
  silhouette.

- [ ] **Step 4: Prove visible improvement**

  Capture the same Windows-player view and require rounded hard edges, layered
  profiles, smaller-scale props/trim, improved character silhouette, deep-black
  preservation, localized warm practicals, readable particles, and no missing
  assets or gray wash.

- [ ] **Step 5: Re-run gameplay and performance gates**

  Run the exact movement/combat/progression/reset assertions after final scene
  mutation and a 600-frame High player run at or above 60 FPS.

- [ ] **Step 6: Continue renderer/package acceptance**

  Only after the detailed-model capture passes, resume aligned
  Performance/High/Epic 2560x1440 captures, native High GPU timing, package,
  relocation, audit, full verification, review, merge, and push.

---

### Task 11: Deformation, Rigging, Sculpt, and Retopology Waves

**Files:**
- Create focused contracts/services/tests under `Rekall.Age.Modeling*`
- Extend the same command and Studio modeling surfaces
- Add production acceptance fixtures under `Examples/`

**Interfaces:**
- Produces: subdivision/crease, decimate/remesh, shrinkwrap, curve/lattice/simple
  deform, armature/skin/weights/constraints, deterministic sculpt/paint strokes,
  multiresolution, retopology, point-cloud/volume geometry, and advanced field
  nodes as specified.

- [ ] **Step 1: Execute Wave 2 as an independently accepted tranche**

  Complete UV/repair/optimization operations and prove round-trip/cook/visual
  evidence before starting character deformation.

  2026-08-26 subdivision tranche: AGE now authors bounded edge-domain Float
  crease weights through `set_edge_crease` and
  `rekall.modeling.edge_crease`. Crease weights affect both edge points and
  vertex rules, propagate onto child edges, and work with bounded 1–6 level
  smooth subdivision in procedural graphs and modifier stacks. The focused
  production modeling slice passes 204 tests. Decimate/remesh/shrinkwrap and
  final visual asset acceptance remain, so Wave 2 is not yet complete.

- [ ] **Step 2: Execute Wave 3 as an independently accepted tranche**

  Complete deformation, rigs, weights, constraints, and animated character
  evidence through the same source/evaluated/cooked boundary.

  2026-08-26 first deformation/weight checkpoint: AGE now publishes
  selection-aware linear two-joint skin assignment and axis/range/center taper
  deformation through semantic operations, procedural nodes, and modifier
  stacks. Aetherfall's mantle is no longer a manually authored 40-point proof;
  a stored nine-node graph evaluates and revision-check-bakes 850 editable
  points / 848 faces, then compiles 3,392 skinned vertices / 1,696 triangles.
  Runtime acceptance proves frame-dependent skeletal deformation. Modeling
  passes 246 tests and Aetherfall passes all 42 tests. This begins Wave 3; native
  rig documents, bind-pose generation, constraints, richer weighting tools,
  Studio visualization, and a production-quality full character remain open.

  2026-08-26 envelope-weight checkpoint: AGE now publishes Blender-inspired
  tapered segment envelopes through the mesh operation, modeling graph, and
  modifier stack, with deterministic Godot-compatible strongest-four
  normalized bindings. Aetherfall's Warden expands to ten named joints, its
  baked mesh uses at least six distinct joints, and renderer acceptance proves
  actual vertex movement from nine agent-authored pose deltas. The production
  bake also exposed and drove a bounded transaction-journal retention repair.
  Multi-joint authoring is now functional; constraints/IK, retargeting, weight
  paint, Studio rig visualization, and production character art remain open.

  2026-08-26 native-rig/bend checkpoint: AGE now stores validated named-joint
  hierarchy and bind matrices in a versioned rig asset, evaluates bind-global,
  pose-global, and skin matrices deterministically, exposes revision-safe rig
  commands, and resolves generic `Rekall.RigPose` state in the renderer with
  typed observations. Generic bend deformation is available as a semantic mesh
  operation, modeling node, and modifier. Aetherfall proves delta-time-driven
  named chest motion changes actual runtime vertices and uses bend on its
  mantle. Modeling passes 257 tests and Aetherfall high-fidelity acceptance
  passes 24 tests. This advances Wave 3 but does not close it: multi-joint
  weighting, constraints/IK, retargeting, Studio rig tools, and a genuinely
  production-quality character remain open.

- [ ] **Step 3: Execute Wave 4 as an independently accepted tranche**

  Complete bounded sculpt/paint/retopology/advanced proceduralism with compact
  undo, explicit lossy policies, and complex-world performance evidence.

---

### Task 12: Optional AI Mesh-Generation Providers (Later Evaluation)

**Priority:** deferred until the native Wave 1 modeling and Aetherfall visual
acceptance work is complete. External generation augments AGE; it must not
replace AGE's source mesh, editing, modifier, import, cook, or provenance
contracts.

**Research baseline (reviewed 2026-08-26):**

- AGE already has an experimental Tripo text-to-model bridge:
  `rekall.asset.tripo.generate_model` submits one task, polls synchronously,
  downloads the preferred GLB, and imports it through the ordinary asset
  catalog/pipeline. This is a useful proof, not yet the intended production
  contract. It has no image/multi-view inputs, resumable task record, cancel,
  Studio task UI, explicit cost/consent preflight, durable provenance manifest,
  post-processing, rigging/animation, or provider-neutral abstraction.
- The current Tripo adapter defaults to `Turbo-v1.0-20250506`. Tripo's current
  v3 generation documentation now identifies `v3.1-20260211` as its latest
  high-quality model and documents model-specific parameter compatibility.
  That concrete drift makes migration off the hard-coded model default part of
  the provider-contract work, not optional cleanup. Provider model IDs and
  supported options must be discovered/configured data rather than assumed to
  remain stable in AGE code.

- Tripo's official API exposes text-, image-, and multi-view-to-model tasks,
  optional PBR/UV/quad/parts controls, post-process conversion, and GLTF/FBX/OBJ
  outputs. References: https://platform.tripo3d.ai/docs/generation and
  https://platform.tripo3d.ai/docs/post-process.
- Meshy's official API exposes text-, image-, and multi-image-to-3D, remesh,
  retexture, humanoid rigging, animation, PBR textures up to provider-supported
  resolutions, and GLB/OBJ/FBX outputs. References:
  https://docs.meshy.ai/en/api/text-to-3d,
  https://docs.meshy.ai/en/api/image-to-3d, and
  https://docs.meshy.ai/en/api/rigging.
- Meshy's current API additionally exposes smart-topology/polycount controls,
  2K/4K/8K textures, task streaming/cancellation, real-world auto-sizing and
  origin placement. Its changelog demonstrates regular model and parameter
  churn, reinforcing the need for versioned provider capability snapshots:
  https://docs.meshy.ai/en/api/changelog.
- The 2026-08-26 refresh also found capabilities that make Meshy a useful test
  of a genuinely generic provider boundary rather than a second text-to-mesh
  wrapper: Meshy-7/Ultra selection, adaptive decimation levels, UV unwrap,
  lighting removal for relightable base color, emission maps, transparent
  previews, text-to-motion, and separate convert/resize operations. None of
  these belong in the AGE core as Meshy-specific concepts; adapters should map
  them onto versioned topology, UV, material, preview, animation, conversion,
  and transform capabilities where a portable AGE meaning exists, and retain
  provider extensions as inspectable metadata otherwise.
- Tripo's current P1 generation tier, detailed-geometry option, standard/
  detailed/extreme texture tiers, image and ordered four-view generation, and
  optional parts/quad output likewise reinforce runtime capability discovery.
  The AGE adapter must not infer quality from a provider marketing label or
  silently map AGE quality presets to paid remote operations.
- Tripo's current v3 surface also publishes labeled multiview inputs, GLB/GLTF/
  FBX/OBJ/STL rig inputs up to 150 MB, asynchronous rigging, and animation
  retargeting. Meshy's current surface publishes one-to-four image generation,
  GLB-first results, quad or triangle remesh with a target polycount, and
  dedicated conversion/resizing. These are strong capability matches for AGE,
  but they also confirm that a direct provider-specific Studio workflow would
  age badly.
- Both services meter work in credits, with cost depending on generation model
  and optional texture/topology/post-process stages. Prices are intentionally
  not frozen into this plan; AGE must fetch or accept a provider quote and show
  an upper bound before submission. Current references:
  https://platform.tripo3d.ai/docs/billing and
  https://docs.meshy.ai/en/api/pricing.
- Provider retention and output rights are operational asset metadata, not a
  one-time account-selection concern. Meshy's current API documentation says
  non-Enterprise API outputs are retained for only three days, and its terms
  distinguish free-plan CC BY 4.0 output from paid-plan rights. Tripo's current
  terms likewise distinguish free and paid users and warn that generated output
  may not be exclusive. AGE must therefore copy completed artifacts immediately,
  record provider/plan/terms URL and retrieval timestamp, preserve attribution
  obligations, and never imply copyright, exclusivity, or commercial clearance.

**Evaluation recommendation:** build one generic asynchronous external-asset job
contract first, migrate the existing Tripo proof onto it, then add Meshy as the
second adapter. Do not make either provider mandatory or select a default until
the comparative fixtures pass. Tripo is the lower-risk first migration because
AGE already has a working slice; Meshy is the strongest second-adapter test
because its preview/refine, remesh, PBR, rigging, animation, SSE, and cancellation
surface exercises more of the generic contract.

**Preliminary provider assessment (official APIs checked 2026-08-26):**

- **Tripo should be migrated first, not chosen as the permanent default.** AGE
  already proves its basic submit/poll/download/GLB-import loop, while Tripo's
  current v3 API adds single-image and labeled multi-view generation, detailed
  geometry/PBR options, retopology, format conversion, biped and creature
  auto-rigging, and animation retargeting. Its published API pricing is
  pay-as-you-go, which is well suited to an explicit per-job cost ceiling.
- **Meshy should be the second adapter and breadth benchmark.** Its official API
  currently covers text, single-image and multi-image generation plus remesh,
  UV unwrap, conversion, retexture, rigging, animation, webhooks/SSE, and several
  game-oriented output formats. Its official open-source MCP server is useful
  reference material for agent tool shape, but AGE should integrate through its
  own provider-neutral command/job contract so Studio, CLI, MCP, Codex, and
  other LLM backends all receive the same inspectable behavior.
- **GLB is the preferred first normalized interchange format.** Both providers
  return it and AGE already imports it. FBX/OBJ/USDZ/STL remain optional source
  or export formats; they must not become parallel hidden asset systems.
- **Neither service removes the need for AGE modeling.** Generated meshes must
  remain editable, reimportable source assets that pass topology, scale, UV,
  PBR, skeleton, animation, license/provenance, cook, package, and fixed-camera
  visual checks. Provider output quality and terms can change, so the final
  recommendation requires the paid same-fixture comparison in Step 6.

Current official references:
https://developers.tripo3d.com/en/docs/quick-start,
https://developers.tripo3d.com/en/pricing,
https://www.tripo3d.ai/terms,
https://docs.meshy.ai/en/api/quick-start, and
https://docs.meshy.ai/en/api/ai,
https://docs.meshy.ai/en/api/pricing, and
https://www.meshy.ai/terms-of-use.

- [ ] **Step 1: Define a provider-neutral generation contract**

  Specify prompt/reference inputs, target purpose and polycount, topology/UV/PBR/
  rig requirements, asynchronous task state, cancellation, bounded retries,
  progress/events, expiring artifact URLs, cost estimate/limit and actual credits,
  moderation state, provider/model capability snapshot, and normalized artifact
  manifests. Inputs must support text, one image, and ordered/labeled multi-view
  images without coupling the core contract to either provider's request schema.

- [ ] **Step 2: Design secure provider adapters**

  Keep API keys server-side, make Tripo and Meshy optional adapters, never expose
  credentials to authored game modules, and require explicit user-visible cost
  and remote-data consent before submission. Persist a safe resumable job record,
  not credentials or signed download URLs. Provider capabilities, limits, and
  unavailable/deprecated model versions must produce inspectable diagnostics.

- [ ] **Step 3: Normalize results through AGE import/reimport**

  Prefer GLB as the canonical interchange path; preserve provider/model/task ID,
  prompt/reference hashes, license/provenance, units, axes, material/texture
  dependencies, skeleton/animation metadata, generated parts, source/evaluated
  topology statistics, and deterministic reimport settings. Copy remote artifacts
  into the project before their URLs expire, validate them as untrusted imports,
  and expose the result to AGE's ordinary editable source/modifier/cook layers.
  Persist the applicable provider terms URL, retrieval timestamp, account-plan
  class, attribution requirement, and user-confirmed input-rights declaration;
  treat these as provenance facts rather than AGE offering legal clearance.

- [ ] **Step 4: Evaluate production suitability**

  Compare topology quality, UVs, PBR maps, scale/origin, rigging, animation,
  latency, failure modes, price, rights, and editability on the same prop,
  environment-kit, and humanoid fixtures. Do not select a default provider until
  the results pass AGE validation, cook, Studio editing, packaging, and a
  real-player visual inspection. Record input prompts/reference hashes, requested
  settings, actual credits, wall time, triangle/material/texture/bone counts,
  import warnings, and fixed-camera captures so results remain auditable even as
  provider models change.

- [ ] **Step 5: Expose agent and Studio workflows**

  Add submit/status/cancel/import/retry commands plus a Studio task/provenance
  view, while routing the resulting mesh into ordinary AGE modeling graphs and
  modifier stacks for continued editing. The Studio flow should accept the same
  reference images users can attach to an authoring conversation, but references
  remain ordinary project assets rather than hidden chat-only state.

- [ ] **Step 6: Run a paid, credentialed comparative spike before promotion**

  With explicit user consent and capped credits, run Tripo and Meshy against the
  same realistic ruin prop, modular environment piece, and humanoid character.
  Keep generated artifacts and proof captures out of source control unless their
  terms permit redistribution. Promote capabilities from experimental only after
  a fresh official-API integration test and the editable packaged-game acceptance
  evidence above; otherwise retain local-file import as the reliable fallback.

**Scheduling note (user-requested and reconfirmed 2026-08-26):** retain this
item explicitly on the post-Aetherfall list. Research and contract design may
be refreshed without blocking the current playable milestone, but do not begin
provider implementation or paid generation runs until Aetherfall's immediate
modeling, animation, gameplay, and visual acceptance work is complete. The
first implementation slice is the provider-neutral asynchronous job contract;
then migrate Tripo, add Meshy, and run the capped same-fixture comparison.

## Plan self-review

- Every capability named in the comprehensive design maps to a delivery wave;
  Wave 1 maps to Tasks 1–10 with exact operation/node IDs and acceptance.
- Source, evaluated, cooked, and client boundaries are consistent across tasks.
- No task makes Aetherfall behavior an engine built-in.
- Blender/Godot remain references only; AGE owns independent contracts/code.
- The plan distinguishes topology density from meaningful visible detail.
- Final acceptance requires real-player, gameplay, performance, package, and
  Studio/agent evidence rather than descriptor existence alone.
