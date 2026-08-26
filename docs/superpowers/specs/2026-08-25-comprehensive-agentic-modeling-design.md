# Comprehensive Agentic Modeling Design

Status: approved by the user's standing autonomous implementation approval and
the explicit 2026-08-25 request for comprehensive Blender/Godot-informed
modeling

Date: 2026-08-25

## Purpose

Make AGE capable of authoring detailed production 3D content without requiring
an external DCC for ordinary work. Users and agents must be able to construct,
edit, inspect, parameterize, reuse, import, optimize, rig, and publish meshes,
curves, instances, materials, and derived game assets through the same typed
contracts.

This extends, rather than replaces, the accepted foundation in
`2026-08-23-agentic-modelling-system-design.md`. That foundation established
stable topology, semantic operations, non-destructive graphs/modifiers,
material graphs, publishing, and an agent/Studio surface. This design closes the
large capability gap left after that first foundation.

## Source-reference boundaries

- Blender source is available locally at `F:\Dev\blender-reference`, pinned in
  the existing audit. It is architectural and behavioral reference only. AGE
  copies no GPL implementation code.
- Godot source is available locally at `F:\Dev\godot-reference`. Its mesh
  resources, import pipeline, rendering storage, and editor/runtime separation
  inform AGE's source/evaluated/cooked boundaries.
- AGE remains an independent C# implementation with serialization-safe,
  inspectable, versioned contracts.

## Rejected approaches

### Embed or link Blender

Rejected. It would make Blender/GPL a runtime and distribution boundary,
complicate deterministic headless authoring, and prevent AGE from owning a
small typed agent API.

### Treat Blender as a required subprocess

Rejected as the primary path. Optional interchange with Blender is valuable,
but an external process cannot be AGE's built-in modeling system and does not
meet offline/package portability goals.

### Add isolated generator nodes as each game needs them

Rejected. A pile of primitives and bespoke nodes does not form a modeling
system. AGE needs a coherent topology kernel, modifier stack, curve system, UV
pipeline, attribute propagation, import/cook boundary, and closed-loop authoring
surface.

## Architectural model

AGE uses five explicit layers:

1. **Source geometry** — versioned editable mesh, curve, point, instance, and
   rig documents with stable identities and typed attributes.
2. **Semantic edit kernel** — deterministic topology/attribute operations with
   selection, provenance, reversible deltas, diagnostics, and affected bounds.
3. **Non-destructive evaluation** — ordered modifiers and typed modeling graphs
   producing immutable snapshots through dependency-aware caches and budgets.
4. **Cooked products** — render surfaces, collision, navigation, LODs, meshlets,
   lightmap UVs, and interchange artifacts derived from evaluated snapshots.
5. **Authoring clients** — SDK, CLI, MCP, Studio, and agent-authored modules all
   consume the same descriptors, commands, evidence, and transaction model.

This mirrors Blender's separation of topology operations, modifiers, Geometry
Nodes, and evaluated geometry while following Godot's separation between
imported/source mesh resources, runtime surfaces, and renderer-owned buffers.

## Geometry components

### Mesh

The existing point/edge/face/corner topology remains canonical. Required
extensions are:

- named selection sets and active/ordered selection history;
- edge seam, sharp, crease, bevel-weight, and freestyle-like generic flags;
- face smooth, material index, face-set, and custom attributes;
- corner UV, color, custom normal, and tangent layers;
- explicit loose points/edges, boundaries, manifold classification, islands,
  and connected-component queries;
- stable provenance across every topology-changing operation.

### Curves

Curves are first-class source documents, not disguised triangle meshes. A curve
document owns spline IDs, control-point IDs, poly/Bezier/NURBS-like spline type,
handles, tilt, radius, cyclic state, resolution, and typed point/spline
attributes. Initial evaluation covers poly and cubic Bezier; rational NURBS is a
later compatible spline type, not a fake Bezier substitution.

Curve operations include create, transform points/handles, subdivide, resample,
reverse, trim, join, fillet, offset, set cyclic, and convert to mesh through a
profile sweep. This supports arches, rails, roots, cables, weapons, trim,
ornament, roads, and pipes.

### Instances and point sets

Instance geometry stores a stable source reference plus transforms and typed
per-instance attributes. Scatter, array, mirror, and instance-on-points preserve
instances until an explicit realize operation. This prevents environmental
dressing from duplicating full mesh topology.

### Rigs and skinning

Rig documents own stable bone IDs, hierarchy, rest/local transforms, constraints,
and named sockets. Meshes carry bounded joint indices and normalized weights.
Authoring includes parent/reparent, mirror naming, automatic envelope weights,
manual weight paint operations, normalization, pruning, and deterministic skin
cooking. Animation continues through AGE's existing animation assets and state
graphs.

## Semantic topology operations

The production operator inventory is grouped by user intent. Every operator is
descriptor-driven, previewable, cancellable, revision-checked, deterministic,
and reports changed IDs, provenance, invalidation flags, bounds, warnings, and
next actions.

### Create and transform

- box, plane/grid, circle/disc, cylinder, cone/frustum, UV sphere, ico sphere,
  capsule, torus, line/polyline, and curve primitives;
- transform, translate, rotate, scale, shear, shrink/fatten, push/pull, and
  randomize with explicit seed;
- duplicate, duplicate linked instance, and separate/join geometry.

### Construct and reshape

- extrude vertices, edges, individual faces, and connected regions;
- inset individual/region faces;
- bevel/chamfer edges and vertices with width, segments, profile, clamp,
  material, and hardened-normal policy;
- bridge edge loops, grid fill, fill holes, poke faces, connect vertices,
  loop cut/subdivide, knife/bisect plane, and spin/screw;
- solidify, wireframe, skin/profile sweep, and curve-to-mesh.

### Simplify and repair

- delete by domain, dissolve vertices/edges/faces, collapse, weld/merge by
  distance, split edges, detach regions, and remove loose geometry;
- triangulate, constrained triangulation, limited dissolve, planarize, beautify,
  recalculate/reverse normals, orient manifold components, and validate/repair;
- decimate by collapse/unsubdivide/planar policy and remesh by explicit voxel or
  surface policy with loss reporting.

### Combine and deform

- boolean union/intersection/difference with exact failure diagnostics and
  material/attribute propagation policy;
- mirror/symmetrize, array, lattice/simple deform, bend, twist, taper, displace,
  smooth/laplacian smooth, shrinkwrap, and surface deform;
- Catmull-Clark and simple subdivision with crease/boundary policy;
- weighted normals, normal edit, auto smooth, split normals, and tangent
  generation.

## Non-destructive modifier stack

Any pure semantic operation may be exposed as a modifier when its parameters
do not depend on transient editor state. The first production modifier set is:

- mirror, array, bevel, solidify, subdivision, weighted normals, triangulate;
- boolean, weld, decimate, remesh, displace, simple deform, shrinkwrap;
- curve deform, lattice, armature, data/attribute transfer, and geometry graph.

Modifiers declare topology/position/normal/attribute invalidation, supported
geometry kinds, deterministic dependencies, target references, estimated cost,
and attribute-loss behavior. Failed evaluation retains the last good snapshot
and produces structured diagnostics; it never silently publishes partial data.

## UV, normals, and material authoring

UV authoring must support:

- planar, box, cylindrical, spherical, and camera projections;
- seam marking and island discovery;
- angle/conformal unwrap policies;
- island transform, average scale, minimize stretch, and bounded deterministic
  packing with margin and rotation policy;
- multiple named UV maps, active render UV, and generated lightmap UV;
- tangent generation per material/UV choice.

Normal authoring must support flat/smooth per face, auto smooth by angle,
sharp-edge splits, weighted normals, custom corner normals, normal transfer,
and deterministic recomputation after topology changes.

Material slots remain face-addressable. Operations preserve or explicitly map
material indices. Geometry graphs can select by material and replace or assign
materials without recompiling unrelated topology.

## Import, export, and cooking

Following Godot's importer/resource/runtime separation:

- importers produce immutable source/import records plus normalized AGE source
  documents; imported files are never the mutable runtime source of truth;
- glTF/GLB is the primary full-fidelity interchange path; OBJ/MTL, PLY, and STL
  cover common static-mesh workflows;
- imports record source URI, hash, author/license metadata, unit/up-axis policy,
  dependencies, warnings, and reimport settings;
- cooking derives render surfaces, meshlets/virtual geometry, collision,
  navigation, LOD chains, occlusion data, and target material/shader products;
- round-trip reports identify unsupported or lossy features rather than
  silently discarding them.

## Sculpting, painting, and retopology

Sculpt and paint are built on deterministic bounded brush-stamp streams over a
spatial acceleration structure. Initial brushes are draw, smooth, inflate,
flatten, crease, grab, mask, and attribute/weight paint. Each stroke reports
affected elements/bounds and stores compact undo. Dynamic topology,
multiresolution, voxel remesh, and retopology snapping require explicit lossy
policies and attribute-transfer reports.

These are production roadmap requirements, but hard-surface/curve/UV/modifier
authoring ships first because it immediately enables detailed game worlds and
provides the substrate sculpting needs.

## Agent-first command surface

Agents discover capabilities through descriptors rather than memorized command
names. Required generic command families include:

- `rekall.mesh.operations.search/inspect` and existing preview/apply/batch;
- `rekall.mesh.selection.query/grow/shrink/loop/ring/island/save`;
- `rekall.curve.create/inspect/operation.preview/operation.apply`;
- `rekall.modifier.search/inspect/stack.*`;
- `rekall.uv.project/unwrap/pack/inspect`;
- `rekall.normals.inspect/recalculate/weighted/transfer`;
- `rekall.instance.scatter/realize`;
- `rekall.rig.*`, `rekall.skin.*`, and bounded weight-paint commands;
- `rekall.model.import`, `rekall.model.reimport`, `rekall.model.export`, and
  `rekall.model.inspect_round_trip`;
- `rekall.model.cook` and `rekall.model.inspect_cook`.

Every response is bounded and returns revisions, semantic counts, samples,
diagnostics, provenance, preview/capture paths where relevant, and concrete
next commands. No ordinary authoring workflow requires raw JSON editing.

## Studio authoring surface

Studio is a client of the same commands. Required modes are object, mesh
point/edge/face, curve point/handle, UV, material, rig/weight, sculpt/paint, and
modifier/graph. The viewport needs picking, box/lasso/circle selection, gizmos,
snapping, local axes, overlays, x-ray, wireframe, normals, seams, UV islands,
face orientation, material slots, and before/after preview.

The operator search/palette and properties UI are generated from canonical
descriptors. Modeling actions integrate with transaction history and live
viewport reevaluation. Studio must not contain a private mutation path.

## Performance and correctness

- Evaluation caches are keyed by source revision, operation/modifier/graph
  identity, dependencies, deterministic seed/time, target, and engine schema.
- Invalidation is precise across topology, positions, normals, tangents, UVs,
  materials, bounds, acceleration structures, and cooked products.
- Large meshes use paged bounded inspection, parallel pure evaluation where
  safe, UInt32 indices, and persistent GPU uploads.
- All operations enforce finite values and configurable element/time/memory
  budgets.
- Non-manifold or lossy support is never inferred; diagnostics state exact
  policy and consequences.

## Delivery waves

### Wave 1 — detailed hard-surface world authoring

Bevel, inset, solidify, mirror, array/instances, weighted/split normals,
additional primitives, curve/profile sweep, and the selection/query support
needed to use them. Aetherfall must visibly consume these features in detailed
ruins, gate machinery, Warden armor, weapons, and props.

### Wave 2 — UV, repair, and optimization

Seams, unwrap/pack, projection variants, tangent control, dissolve/split/fill,
bridge/loop operations, decimation, remesh, and LOD/collision cooking.

### Wave 3 — deformation, rigging, and character production

Subdivision/crease refinement, shrinkwrap, curve/lattice/simple deform,
armatures, skin weights, constraints, and character/prop animation authoring.

### Wave 4 — sculpt, paint, retopology, and advanced proceduralism

Brush-stamp sculpt/paint, multiresolution, retopology, volumes, point clouds,
advanced geometry-node fields, and simulation-aware geometry.

## Acceptance gates

Wave 1 is not complete until:

1. descriptor inventory exposes every Wave 1 operation through SDK/CLI/MCP and
   Studio without duplicated schemas;
2. deterministic tests prove topology, provenance, attributes, validation,
   preview/apply, undo/redo, evaluation caching, bake, and render compilation;
3. a single authored asset graph combines at least five Wave 1 capabilities;
4. Aetherfall replaces visibly coarse hero meshes with those capabilities;
5. a real Windows-player capture demonstrates materially rounded hard edges,
   layered silhouettes, trim/profile detail, stable textured materials, and no
   missing assets;
6. strict gameplay proofs remain green after the final asset/scene mutation;
7. the high-quality player remains at or above 60 FPS on the milestone machine.

The broader comprehensive program is complete only when each delivery wave has
equivalent command, Studio, cook, visual, and round-trip evidence. Passing
catalog tests without a visibly stronger authored world is not acceptance.
