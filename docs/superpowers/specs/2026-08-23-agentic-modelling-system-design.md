# Agentic Modelling System Design

Status: accepted under the user's standing autonomous implementation approval

Date: 2026-08-23

## Purpose

Give users and AI agents a built-in, production-oriented way to author advanced
geometry, materials, and reusable procedural world assets entirely through AGE.
The system must make compact semantic changes, inspect their consequences, and
prove evaluated gameplay assets without requiring Blender, raw vertex dumps, or
engine-authored game content.

## Core decisions

### One typed geometry substrate

Editable mesh operations, procedural modelling graphs, modifiers, sculpting,
physics cooking, rendering, and interchange use the same geometry domains and
attribute vocabulary. Mesh starts with point, edge, face, and corner domains;
instance, curve, point-cloud, and volume components are additive later.

### Stable identity beside compact storage

Assets persist compact arrays and stable 64-bit generational element IDs.
Array locations may change; stable IDs survive when semantic elements survive.
Every topology operation returns provenance for preserved, replaced, split,
merged, created, and deleted elements.

### Source, evaluated, and cooked products are distinct

The versioned source document is editable. Operations and graphs publish
immutable evaluated snapshots. Render, physics, navigation, and export cooking
produce content-addressed target artifacts. Failure never overwrites the last
good evaluated or cooked revision.

### Descriptors are the API

Mesh operations, node types, material nodes, and modifier types each expose one
canonical descriptor containing stable type/version, description, ports,
parameters, ranges, units, enum choices, supported domains/targets,
determinism, side effects, costs, and invalidation behavior. C# SDK, CLI, MCP,
Studio, prompts, validation, and docs consume the same descriptors.

### Closed-loop evidence is mandatory

Mutation results contain bounded semantic diffs and validation. Evaluation
reports contain revision lineage, dependencies, cache facts, node timings,
geometry/material summaries, diagnostics, and preview artifact references.
Viewport picking maps render triangles back to authored elements.

## Principal contracts

`Rekall.Age.Modeling.Contracts` owns serialization-safe records and enums:

- `RekallAgeGeometryDocument`
- `RekallAgeMeshTopology`
- `RekallAgeMeshElementId`
- `RekallAgeGeometryDomain`
- `RekallAgeGeometryValueType`
- `RekallAgeGeometryAttribute`
- `RekallAgeMaterialSlot`
- `RekallAgeMeshSelection`
- `RekallAgeMeshOperationDescriptor`
- `RekallAgeMeshOperationRequest/Result`
- `RekallAgeMeshChangeSet`
- `RekallAgeElementProvenance`
- `RekallAgeMeshValidationReport`
- `RekallAgeModelingGraphDocument`
- `RekallAgeModelingNodeTypeDescriptor`
- `RekallAgeGraphPatch`
- `RekallAgeGraphEvaluationReport`

Mesh topology stores point IDs/positions, edge IDs/endpoints, face IDs/offsets,
and corner IDs/point/edge references. Attributes declare domain, type,
interpolation, storage, semantic, default, and values. Built-ins include point
position; edge seam/sharpness/crease; face material index/sharpness; and named
corner UV/color/custom-normal layers.

## Persistence and concurrency

Mesh, modelling graph, and material graph assets live outside scene documents
under stable logical asset IDs. Paths and display names are aliases, not
identity. Documents carry schema/type revisions and content hashes. Mutations
require the current file revision and are written atomically with recovery
preimages using AGE's existing persistence primitives.

Mesh edits and graph patches are atomic ordered batches. Validation runs before
commit. Preview evaluates without persistence. Undo/redo records compact
element or graph deltas and integrates with the existing transaction log.

## Validation

Strict validation covers finite data, reference ownership, duplicate/self
edges, invalid face cycles, repeated face elements, corner-edge endpoint
mismatches, duplicate faces, degeneracy, winding, attribute lengths/types,
material-slot ranges, boundary/non-manifold statistics, and optional
self-intersection analysis. Diagnostics carry stable codes, element IDs,
locations, severity, repairability, and next actions.

Only deterministic, provenance-preserving repairs qualify as safe repair.
Lossy topology or attribute work requires explicit operation policy and must be
reported; it is never silent.

## Semantic operations

Operations accept selectors by explicit IDs, named selection, attribute
predicate, connectivity expansion, spatial predicate, or prior batch result.
All operations are deterministic, cancellable, previewable, revision-safe, and
return change masks and provenance.

The initial operator inventory is create/delete, transform, reverse faces,
triangulate, and extrude region. Subsequent generic operators add inset, bevel,
subdivide, connect, bridge, fill, dissolve, collapse, weld, bisect, mirror,
solidify, array, boolean, remesh, decimate, attribute transfer, material
assignment, and UV project/unwrap/pack.

## Evaluation and compilation

Modelling graphs have stable graph/node/port IDs, versioned node types, typed
links, exposed parameters, and named outputs. A validator compiles the source
graph to an immutable execution plan. Only requested outputs and reachable
nodes execute. Node cache keys include type/version, parameters, input hashes,
dependency revisions, deterministic seed/time, engine/schema version, and
target profile.

Graph-local anonymous attributes exist only while reachable. Evaluation has
cancellation plus time, memory, and element budgets. The report preserves the
last good output when evaluation fails.

The render compiler triangulates polygons with face/corner provenance, resolves
normal domains and split normals, computes tangents per chosen UV map,
deduplicates render vertices by the complete shading tuple, and emits UInt32
indices and current AGE render buffers. Physics, navigation, GLB, and Studio
consume the same evaluated snapshot through their own deterministic compilers.

## Materials

Material graphs use semantic typed nodes for constants, coordinates, mapping,
textures, scalar/vector math, mixing, normal maps, PBR surface, emissive, and
output. Backend compilers target AGE's Vulkan and WebGPU shader paths and map
generated diagnostics back to node and port IDs. Material instances expose
typed defaults and overrides. Raw shader authoring remains supported but is not
the graph storage format.

## Agent surface

The first command family is:

- `rekall.mesh.create_asset`
- `rekall.mesh.inspect`
- `rekall.mesh.query_elements`
- `rekall.mesh.validate`
- `rekall.mesh.diff`
- `rekall.mesh.operation.preview`
- `rekall.mesh.operation.apply`
- `rekall.mesh.operation.batch`
- `rekall.mesh.assert`
- `rekall.mesh.compile_render`
- `rekall.modeling.node_types.search/inspect`
- `rekall.modeling.graph.create/inspect/apply_patch/validate/evaluate/bake`
- `rekall.modeling.inspect_evaluation`

Responses default to bounded summaries and samples. Full arrays remain in
versioned assets and can be paged explicitly. Every mutating command returns
the new revision and suggested evidence-producing next action.

## Studio

Studio will become another client of these contracts: mesh element selection,
active/ordered selection history, operation parameter previews, attribute and
material inspectors, node graphs, evaluation timings, viewer outputs, gizmos,
and undo groups. No Studio-only modelling mutation logic is permitted.

## Security and licensing

AGE independently implements the design in C#. Blender GPL source is not
copied. Imported/remote assets record source URL, author, license, hashes,
settings, dependencies, and cooked artifacts. Arbitrary code execution is not
the default modelling interface.
