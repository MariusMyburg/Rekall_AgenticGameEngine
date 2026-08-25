# Comprehensive Modeling Capability Matrix

Date: 2026-08-25

Status: active implementation contract

This matrix prevents AGE's first modeling foundation from being mistaken for a
comprehensive authoring system. A capability is `implemented` only when its
descriptor, operation/evaluator, strict tests, agent/Studio access, and visible
consumer are all proven. `Partial` lists the exact missing behavior.

Blender reference root: `F:\Dev\blender-reference`
Godot reference root: `F:\Dev\godot-reference`

No GPL source is copied. References identify concepts and subsystem boundaries;
AGE remains an independent C# implementation.

## Source/evaluation/resource architecture

| Capability | Status | AGE evidence | Reference inspiration | Remaining acceptance |
|---|---|---|---|---|
| Stable point/edge/face/corner topology | Implemented | `Rekall.Age.Modeling.Contracts/RekallAgeMeshContracts.cs`; topology validation tests | Blender BMesh and `DNA_mesh_types.h` | Continue provenance coverage for every new operator |
| Typed domain attributes | Implemented | mesh contracts/compiler tests | Blender attribute/domain APIs | Add complete preservation tests for new Wave 1 operations |
| Source/evaluated/cooked separation | Partial | mesh/graph/modifier assets and compiled model assets | Blender depsgraph/modifiers; Godot importer mesh and render storage | Add general cook service, LOD/collision/nav stages, round-trip reports |
| Descriptor-driven API | Partial | mesh operation, modeling node, modifier, material catalogs | Blender operator/RNA/node declarations | Wave 1 catalog RED until all required descriptors and client surfaces exist |
| Atomic preview/apply/undo | Implemented for current mesh operations | mesh edit/store/undo command tests | Blender operators/undo | Extend to curves, UV, rig, sculpt, import settings |

## Mesh edit kernel

| Capability | Status | AGE ID/evidence | Blender reference | Exact gap |
|---|---|---|---|---|
| Transform selection | Implemented | `transform` | `bmo_utils.cc` | Add rotate/scale/shear/shrink-fatten modes |
| Extrude faces | Implemented | `extrude_faces` | `bmo_extrude.cc` | Add vertex/edge/individual/region mode inventory |
| Triangulate | Implemented | `triangulate_faces` | `bmo_triangulate.cc` | Add constrained/beautify policies |
| Merge by distance | Implemented | `merge_by_distance` | `bmo_removedoubles.cc` | Add explicit collapse-at-center/cursor/active policies |
| Smooth subdivision | Implemented | `subdivide_smooth` | subdivision surface code | Add levels, creases, boundary and UV policies |
| Boolean | Partial | `rekall.modeling.boolean` | Blender geometry boolean | Current supported closed-manifold path needs attribute/material policy expansion |
| Bevel/chamfer | Partial | `bevel_edges`, graph and functional modifier-stack nodes; focused tests prove 3-segment profile, overlap clamp, deterministic output, UV/material preservation, and weighted-normal composition | `bmo_bevel.cc`, `GEO_mesh_bevel.*`, `node_geo_mesh_bevel.cc` | Complete-manifold selection is functional; add arbitrary subset bevel, weights, and material assignment per beveled region |
| Inset | Partial | `inset_faces`, `rekall.modeling.inset`; focused graph proof produces recessed border geometry | `bmo_inset.cc` | Individual/single-face mode is functional; add connected multi-face region mode and broader boundary policies |
| Solidify | Partial | `solidify`, graph and functional modifier-stack nodes; ordered-stack proof covers shell/rim composition | `MOD_solidify*` | Broader non-manifold, material-offset, and even-thickness corner policies |
| Mirror/symmetrize | Partial | graph and functional modifier-stack nodes; winding-correct transformed copy with optional seam weld and explicit bisect diagnostic | `bmo_mirror.cc`, `MOD_mirror.cc` | Add bisect mode, arbitrary plane, and preserved instance output |
| Array/instances | Partial | graph and functional modifier-stack nodes; deterministic realized absolute/relative copies with explicit linked-instance diagnostic | `MOD_array.cc`, Geometry Nodes instances | Add linked instance geometry, explicit realize, object/curve offsets, and per-instance attributes |
| Bridge/fill/connect | Partial | `fill_holes`, `bridge_edge_loops`, `poke_faces` executor descriptors plus graph nodes; focused tests prove simple boundary-loop fill, two equal-cardinality loop bridging, centroid fans, material/default attributes, invalid selections, provenance, and deterministic replay | `bmo_bridge.cc`, `bmo_fill_*`, `bmo_connect*.cc` | Add unequal-loop correspondence, grid/span policies, multi-loop material controls, connect paths/vertices, and wider selection-set editing evidence |
| Split/dissolve/collapse | Partial | `dissolve_edges` executor descriptor and graph node; focused proof merges one two-face manifold edge and rejects boundary/material-conflict cases explicitly | `bmo_split_edges.cc`, `bmo_dissolve.cc` | Add multi-edge/vertex dissolve, split edges/faces, collapse policies, broader corner-attribute interpolation, and undo/redo command coverage |
| Bisect/loop cut/wireframe | Partial | `bisect_plane` executor descriptor and graph node; deterministic complete-mesh one-sided clip interpolates point attributes and diagnoses partial selection/cap modes explicitly | `bmo_bisect_plane.cc`, `bmo_subdivide*`, `bmo_wireframe.cc` | Add retained-both-sides mode, direct cut filling, arbitrary partial islands, loop cut/subdivide, and wireframe |
| Decimate/remesh | Planned Wave 2 | comprehensive design Wave 2 | `MOD_decimate.cc`, `MOD_remesh.cc` | Explicit lossy/attribute-transfer reports |

## Primitives, curves, and instances

| Capability | Status | AGE ID/evidence | Blender reference | Exact gap |
|---|---|---|---|---|
| Box/grid/UV sphere/frustum/torus | Implemented | current primitive catalog and topology matrix | Blender primitive builders | Preserve as shared-builder consumers |
| Plane/disc/cylinder/cone/ico sphere/capsule | Partial | functional evaluator/catalog nodes; `ProductionPrimitiveTests` prove deterministic bounded topology, expected counts, finite bounds, caps, and closed-manifold classification | geometry primitive nodes and BMesh primitives | Add first-class generated UV/normal attributes to every primitive, richer cap policies, and visible Aetherfall consumers |
| Poly and cubic Bezier curve documents | Planned/RED | Wave 1 Task 5 | Blender Curves/Geometry curve nodes | Stable spline/control-point/handle contracts and asset store |
| Curve resample/reverse/trim/fillet/join | Planned/RED | Wave 1 Task 5 | curve geometry intern and nodes | Deterministic evaluation and provenance |
| Profile sweep/curve to mesh | Partial | functional `rekall.modeling.curve.profile_sweep`; `CurveProfileSweepTests` prove deterministic parallel-transport path frames, circle/rectangle profiles, open-path caps, corner UVs, and material slots | `node_geo_curve_to_mesh.cc`, curve bevel concepts | Replace inline path arrays with versioned curve assets, add cyclic seams/tilt/radius interpolation and source-span provenance, then consume in Aetherfall arch/trim assets |
| Scatter/instance on points/realize | Partial | `rekall.modeling.scatter.area` currently realizes copies | Geometry Nodes instance operators | Preserve linked instances and add explicit realize |

## Normals, UVs, and materials

| Capability | Status | AGE ID/evidence | Blender reference | Exact gap |
|---|---|---|---|---|
| Generated normals | Implemented | `generate_normals`; compiler tests | mesh normals APIs | Make authoring policy first-class |
| Flat/smooth/sharp/auto smooth | Planned/RED | Wave 1 Task 4 | `bmo_normals.cc`, set-shade-smooth node | Face/edge attributes and split compiler behavior |
| Weighted/custom corner normals | Partial | `weighted_normals`, graph and functional modifier-stack nodes; focused finite unit corner-normal and ordered-stack proofs | `MOD_weighted_normal.cc`, normal edit | Add corner-angle/face-strength policy, custom-normal editing, and transfer |
| Tangents per UV map | Implemented foundation | mesh compiler tests | Blender tangent APIs | Add explicit inspection/regeneration command |
| Planar UV projection | Implemented | `project_uv` | UV projection concepts | Current node must remain corner-domain |
| Box/cylindrical/spherical/camera projection | Partial | `project_uv` supports deterministic planar, box, cylindrical, and spherical corner projection with strict finite replay tests | Blender UV project/warp | Add camera projection and richer authored origins/orientations |
| Seams/islands/unwrap/pack | Partial | `mark_uv_seams`, public island inspection, `unwrap_pack_uv`, and graph/lightmap nodes provide deterministic seam-bounded planar charts and bounded packing | UV parametrizer/pack sources | Add angle/conformal solvers, partial-island selection, texel-density policies, tangent regeneration command, and Studio UV mode |
| Multi-material face assignment | Implemented foundation | material slots/assign node | Blender face material indices | Preserve across every new operation/modifier |
| Semantic material graphs | Implemented foundation | material graph catalogs/compilers | Blender shader nodes; Godot materials | Expand physically based node library and layered materials later |

## Deformation, characters, sculpting, and cooking

| Capability | Status | Delivery wave | Reference inspiration | Required evidence |
|---|---|---|---|---|
| Displace/simple deform/lattice/curve deform/shrinkwrap | Planned | Wave 3 | corresponding Blender modifiers | Tests, Studio/agent surface, animated visible consumer |
| Armature/skin/weights/constraints | Planned | Wave 3 | Blender armature/skin modifier; Godot skin/skeleton resources | Stable rig contracts, cook, runtime animation, character acceptance |
| Sculpt/attribute/weight paint | Planned | Wave 4 | Blender sculpt BVH/stroke cache | Deterministic brush stamps, local dirtiness, compact undo |
| Multires/dynamic topology/retopology | Planned | Wave 4 | Blender multires/sculpt/retopo concepts | Explicit lossy policy and attribute-transfer report |
| GLB/OBJ/PLY/STL import and reimport | Partial | Wave 2 | Blender import nodes; Godot resource importers | Normalize into AGE source docs with hashes/settings/license/dependencies |
| Render/collision/nav/LOD/meshlet cooking | Partial | Waves 2–3 | Godot importer mesh and renderer mesh storage | Independent staged cook service and package/round-trip evidence |

## Optional external mesh generation

| Capability | Status | Provider/reference | AGE requirement |
|---|---|---|---|
| Text/image/multi-view to editable mesh | Research queued after native Wave 1 | Tripo official generation API; Meshy official Text/Image/Multi-Image to 3D APIs | Provider-neutral asynchronous job contract; GLB-first normalized import; never bypass AGE source/edit/cook layers |
| Provider remesh/retexture/rig/animation | Research queued | Tripo post-process/texture/animation tasks; Meshy remesh/retexture/rigging/animation APIs | Preserve task/model provenance, licenses, units, axes, topology/UV/PBR/rig metadata, and allow ordinary AGE re-edit/reimport |
| Cost, credentials, consent, and failure handling | Research queued | Provider authentication, pricing, task-status and cancellation documentation | Server-side secrets, preflight cost limits, explicit remote-data consent, bounded polling/retries, cancellation, and inspectable diagnostics |

The provider evaluation is intentionally deferred behind the current native
modeling tranche. AGE must remain capable without either service and must treat
generated output as imported source content subject to the same validation,
editing, cooking, packaging, and provenance rules as any other asset.

## Current executable gate

`ComprehensiveModelingCatalogTests.WaveOnePublishesTheRequiredHardSurfaceCurveNormalAndPrimitiveDescriptors`
is intentionally RED as of its first run on 2026-08-25. The first observed
missing operation descriptors were `inset_faces`, `solidify`, and
`weighted_normals`; inset subsequently became functional while the gate remains
RED on the next unimplemented inventory. Later assertions expose missing node
and modifier IDs as preceding groups become green. The test must not be weakened
to match the old catalog.

## Visible acceptance driver

Aetherfall's 2026-08-25 gray, visibly low-poly player capture is a rejected
baseline. Wave 1 requires the same real-player view to show meaningful bevels,
layered profiles, trim, smaller props, detailed Warden/sentinel silhouettes,
stable PBR materials, deep-black preservation, localized practical lighting,
and readable particles. More triangles without more form detail do not count.
