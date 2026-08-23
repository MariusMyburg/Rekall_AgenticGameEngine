# Agentic Modelling System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use test-driven development and
> verification before completion. Implement tasks in order; each task must leave
> focused tests green and update this checklist.

**Goal:** Deliver AGE's first coherent persistent editable-mesh foundation,
semantic agent operations, runtime projection, and visible acceptance proof,
then grow it into procedural geometry/material graphs and Studio modelling.

**Spec:** `docs/superpowers/specs/2026-08-23-agentic-modelling-system-design.md`

## Global constraints

- Keep engine primitives generic and game-agnostic.
- Do not copy Blender GPL implementation code.
- Do not expose transient array indices as durable agent references.
- Use corner-domain data for UVs and other face-varying values.
- Require expected revisions for every mutation and publish atomically.
- Never weaken validation or acceptance assertions merely to obtain a pass.
- Preserve existing packed `Rekall.GeometryMesh` as a compatibility consumer
  while migrating runtime production to compiled mesh assets.
- Update `docs/production/PROGRESS.md` after every verified tranche.

---

### Task 1: Contracts and strict topology validation

**Create:**

- `src/Rekall.Age.Modeling.Contracts`
- `src/Rekall.Age.Modeling`
- focused modelling tests in `tests/Rekall.Age.Tests/Modeling`

- [x] Write failing serialization, stable-ID, ngon, loose-edge, UV-seam,
  non-manifold, invalid-reference, duplicate-edge/face, degeneracy, and attribute
  length/type tests.
- [x] Add compact point/edge/face/corner topology and typed domain attributes.
- [x] Add material slots, selection records, bounds, and structured validation.
- [x] Add projects to the solution and verify zero-warning Release builds.

### Task 2: Versioned mesh asset store

- [x] Write failing persistence, atomic save, recovery, revision-conflict,
  canonical round-trip, safe-name, and document-size tests.
- [x] Persist assets under stable logical IDs outside scene documents.
- [x] Reuse AGE atomic file/recovery/schema primitives and transaction changed
  resources.
- [x] Expose create, load, list, inspect, and validate services.

### Task 3: Editable adjacency, queries, and operation framework

- [x] Write failing adjacency and generic selector tests.
- [x] Add compact adjacency indices without managed pointer cycles.
- [x] Add canonical operation descriptors and request/result schemas.
- [x] Add preview/apply/batch execution, change masks, element provenance, and
  rollback on validation failure.
- [x] Implement create, delete, transform, reverse faces, triangulate, and
  extrude-region operations with property and regression tests.

### Task 4: Command, CLI, and MCP surface

- [x] Write failing registry/schema/JSON-RPC tests for mesh create, inspect,
  query, validate, diff, preview, apply, batch, and assert.
- [x] Return bounded samples, stable IDs, affected bounds, revisions,
  diagnostics, provenance, and next actions.
- [x] Ensure checkpoint gating does not block mesh construction/repair commands.
- [x] Prove stale revisions and invalid operations fail without partial writes.

### Task 5: Render compiler and scene reference

- [x] Write failing ngon triangulation, corner UV split, hard-normal, tangent,
  material-surface, UInt32 index, and triangle-picking provenance tests.
- [x] Compile editable assets to immutable runtime meshes and current render
  buffers, retaining face/corner/point maps.
- [x] Add a generic scene component that references a mesh asset/evaluated
  revision and material slots.
- [x] Adapt software/Vulkan/WebGPU, physics cooking, Studio viewport, inspection,
  and GLB paths through the common snapshot without a second renderer model.
- [x] Add legacy packed-mesh migration/adapter coverage.

### Task 6: Element-delta undo and closed-loop acceptance

- [x] Write failing mesh edit undo/redo and grouped-operation tests.
- [x] Record compact reversible deltas in AGE transactions.
- [x] Create a deterministic acceptance fixture with an ngon, shared-point
  corner UV variants, multiple material slots, and an extrusion.
- [x] Prove inspect -> mutate -> validate -> compile -> render -> pick -> undo ->
  redo using commands/MCP with no direct JSON editing.
- [x] Capture and independently inspect a visible runtime frame; commit bounded
  structured and visual evidence.

### Task 7: Procedural modelling graph

- [x] Add versioned graph/node/port contracts and canonical node descriptors.
- [x] Add atomic revision-checked graph patches and structural/type/domain/cycle
  validation.
- [x] Add deterministic demand evaluation, dependency invalidation, node-hash
  caching, budgets, and bounded reports.
- [x] Supply initial primitives, transform, join, extrude, triangulate,
  named/captured attributes, field math, material assignment, and output nodes.
- [x] Bake through the same mesh asset and runtime compiler and prove a parameter
  edit changes evaluated bounds with cache/invalidation evidence.
- [ ] Expose bounded node discovery and graph create/inspect/patch/validate/
  evaluate/bake/evaluation-inspection commands through CLI/MCP.

### Task 8: Semantic material graphs and modifiers

- [ ] Add typed material graph documents, node descriptors, instances, and
  Vulkan/WebGPU compilers with node/port source mapping.
- [ ] Add initial constant/coordinate/mapping/texture/math/mix/normal/PBR/
  emissive/output nodes.
- [ ] Add ordered modifier descriptors, immutable evaluation, cache identity,
  attribute-propagation policy, preview, reorder, configure, and bake.
- [ ] Expand generic topology, UV, boolean, subdivision, remesh, and optimization
  operations only behind strict tests.

### Task 9: Studio modelling and advanced world authoring

- [ ] Add viewport element picking, mesh edit modes, selection history, operation
  previews, attribute/material inspectors, node editor/viewer, evaluation
  diagnostics, and persistent layouts using the same command schemas.
- [ ] Add brush-stamp sculpt/paint, BVH-local updates, explicit lossy-operation
  policy, and compact stroke undo.
- [ ] Add staged deterministic cooking for collision, navigation, LOD,
  optimization, material targets, glTF capability reports, and round-trip tests.
- [ ] Run complex world, physics, material, packaging, and playable gauntlets.
