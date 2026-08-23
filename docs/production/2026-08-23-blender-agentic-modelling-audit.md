# Blender-Informed Agentic Modelling Audit

Status: source audit complete; implementation architecture accepted

Date: 2026-08-23

## Reference boundaries

- Blender was inspected from the local sparse checkout at
  `F:\Dev\blender-reference`, pinned to commit
  `4641b05b1687912ec97d021f12c1076aba3b90ae`.
- The community Blender MCP reference was inspected from
  `ahujasid/blender-mcp` on 2026-08-23.
- No Blender or Blender MCP source is copied into AGE. Blender is GPL and is
  used only as architectural evidence. AGE remains an independent C# design.

## Finding

AGE currently has a useful packed triangle ingestion path, elementary
generators, shader authoring, PBR rendering, physics, GLB interchange, runtime
inspection, and viewport capture. It does not yet have a modelling kernel.
`rekall.geometry.create_mesh` stores render vertices directly in a scene,
accepts only one UV set and one surface, and uses 16-bit authoring indices.
There are no durable point/edge/face/corner identities, arbitrary typed
attributes, semantic topology transactions, evaluated geometry, or material
graphs.

The architectural correction is to separate three layers:

1. persistent editable geometry and semantic material documents;
2. immutable evaluated snapshots produced by operations, modifiers, and graphs;
3. derived render/physics/export products consumed by existing AGE systems.

Packed triangles are a compiled product, not the authoring source of truth.

## Blender evidence worth transferring

| Capability | Blender evidence | AGE decision |
|---|---|---|
| Editable topology | `source/blender/bmesh/bmesh_class.hh`: `BMVert`, `BMEdge`, `BMLoop`, `BMFace` | Point, edge, face, and face-corner are first-class domains. Corners are required for UV seams, split normals, ngons, and face-varying data. |
| Persistent mesh layout | `source/blender/makesdna/DNA_mesh_types.h`: positions, edges, face offsets, corner vertices/edges | Persist compact structure-of-arrays data plus stable IDs; build adjacency as an editable facade. Do not persist object-pointer cycles. |
| Derived triangles | `bmesh_mesh_tessellate.cc`; `Mesh::corner_tris` and `corner_tri_faces` | Triangulate during evaluation and retain triangle-to-face/corner provenance for picking and diagnostics. |
| Attribute domains | `BKE_attribute_enums.hh`, `BKE_attribute.hh`, `BKE_attribute_storage.hh` | Every attribute declares domain, value type, interpolation, storage, default, and semantic. Mutations return precise invalidation masks. |
| Typed operations | `bmesh_operator_api.hh`, `bmesh_opdefines.cc` | One descriptor-driven operator system supplies SDK, CLI, MCP, Studio, validation, and docs. Results include created/deleted/modified IDs and provenance. |
| Validation and partial updates | `bmesh_mesh_validate.cc`, `bmesh_mesh_partial_update.hh` | Structured production validation is mandatory. Affected-element sets drive incremental normals, tessellation, BVH, bounds, and GPU updates. |
| UVs, normals, tangents | `uvedit_unwrap_ops.cc`, `BKE_mesh_tangent.hh`, mesh normal-domain APIs | UVs are corner attributes; seams/sharpness are edge attributes; normals can be face, point, or corner derived data; tangents are per UV map. |
| Original/evaluated separation | `BKE_modifier.hh`, depsgraph evaluation/query sources | Modifiers never mutate source assets during evaluation. Immutable snapshots are cached by source, stack, dependency, time, and target revisions. |
| Fields and anonymous attributes | `BKE_geometry_fields.hh`, `node_geo_attribute_capture.cc`, reference-lifetime analysis | Procedural nodes evaluate typed fields over explicit domains. Graph-local attributes are demand-driven compiler temporaries, not serialized user names. |
| Lazy node execution | `NOD_geometry_nodes_lazy_function.hh`, `NOD_geometry_nodes_execute.hh` | Compile versioned graphs to validated immutable plans and evaluate only demanded outputs with node-hash cache reuse. |
| Self-describing API | Blender RNA access and node/material RNA sources | One canonical descriptor model powers C#, JSON schemas, MCP, CLI, Studio, prompts, validation, and documentation. |
| Evaluation evidence | `NOD_eval_log.hh`, `NOD_warning.hh`, Viewer node/editor | Reports expose revisions, dependencies, cache facts, node timings, bounded value/geometry summaries, warnings, and preview artifacts. |
| Atomic operators and undo | `WM_types.hh`, `wm_event_system.cc`, `BKE_undo_system.hh` | Graph and mesh edits are revision-checked atomic patches. Failed validation commits nothing. Successful grouped gestures store reversible deltas. |
| Sculpt locality | paint BVH, sculpt stroke cache, sculpt undo sources | Sculpt is a deterministic stream of bounded brush stamps with BVH-local dirtiness and undo; dynamic topology comes only after explicit attribute propagation. |

## Blender MCP lessons

The community Blender MCP offers scene/object inspection, viewport screenshots,
arbitrary Python execution, materials, and asset acquisition. Its strongest
pattern is the observe/mutate/observe loop: an agent can inspect structured
state and visual evidence after each meaningful change.

AGE will preserve that closed loop and improve its safety and precision:

- normal modelling work uses typed, bounded, revision-checked operations;
- every response includes stable element IDs, diffs, validation, affected
  bounds, diagnostics, and next actions;
- large buffers are paged or asset-backed rather than dumped into context;
- viewport captures and triangle-to-source provenance prove visual results;
- agent-authored C# remains the intentional general escape hatch instead of
  unrestricted remote scripting being the primary modelling API;
- remote assets retain provenance, license, hashes, import settings, and target
  cook facts.

## Do not copy

- Do not copy GPL implementation code.
- Do not recreate BMesh as millions of managed objects joined by circular
  pointers; use compact pools and stable generational handles.
- Do not expose transient array indices as agent identities.
- Do not make raw render vertices editable topology.
- Do not store UVs, colors, or normals only per point.
- Do not silently discard attributes during boolean, remesh, or sculpt work.
- Do not copy Blender's global mutable UI context or stringly operator slots.
- Do not create separate Studio and agent contracts.
- Do not publish partial or invalid evaluated/cooked products.

## Prioritized delivery

### P0: modelling kernel

- versioned mesh assets with stable point/edge/face/corner IDs;
- compact polygon topology, material slots, and typed domain attributes;
- adjacency/query facade and strict structured validation;
- revision-safe semantic operations with preview, provenance, diff, batch, and
  undo/redo;
- derived triangulation, split normals, tangents, UInt32 indices, bounds, and
  authored-element picking maps;
- CLI/MCP inspection, queries, assertions, and bounded evidence;
- migration adapter for existing `Rekall.GeometryMesh` consumers.

### P1: procedural geometry and materials

- versioned typed modelling graphs and canonical node descriptors;
- demand-driven deterministic evaluation, dependency invalidation, caching,
  budgets, and reports;
- transform, join, extrude, inset, bevel, subdivide, bridge, fill, weld,
  triangulate, bisect, mirror, solidify, array, attribute, selection, and UV
  operations;
- non-destructive modifier stacks over immutable snapshots;
- semantic material graphs compiled to Vulkan GLSL/SPIR-V and WebGPU WGSL,
  with raw shaders retained as an expert route.

### P2: advanced world authoring

- boolean, remesh, decimate, subdivision surface, geometry-node expansion;
- curve, volume, point-cloud, and instance geometry components;
- brush/attribute painting, sculpt BVH, multiresolution, and explicit dynamic
  topology policies;
- Studio mesh/material/node modes built on the identical command schemas;
- staged deterministic cooking with collision, LOD, optimization, target
  material compilation, glTF capability reports, and round-trip tests.

## First acceptance gate

An agent must, without editing JSON directly:

1. create an ngon-based persistent mesh asset;
2. author two corner UV values that share one point;
3. assign multiple material slots and extrude selected faces;
4. inspect returned stable IDs and provenance;
5. undo and redo the exact edit;
6. compile the asset to a visible scene entity with UInt32-capable indices;
7. map a rendered triangle back to its authored face;
8. pass strict topology/attribute validation; and
9. produce structured runtime inspection plus viewport capture evidence.

This is the minimum proof that AGE has begun to become a real built-in modelling
system rather than a larger collection of mesh generator commands.
