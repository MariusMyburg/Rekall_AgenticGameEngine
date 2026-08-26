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
| Curve resources and profile sweep | Partial | versioned `.age.curve.json` poly/cubic-Bezier documents, strict validator/store, stable spline/control-point IDs, deterministic sampling, graph curve values, cyclic/open profile sweep, radius/tilt interpolation, sweep-aligned corner UVs, material slots, point-domain source-span provenance, and a baked/published Aetherfall Bézier arch consumer | `node_geo_curve_to_mesh.cc`, curve bevel concepts | Add line/circle builders, reverse/resample/trim/fillet/join nodes, multi-spline sweep output, dedicated curve edit commands/Studio mode, and migrate remaining trim/cable/rail assets from legacy inline paths |
| Typed curve revolution and screw | Partial | `rekall.modeling.curve.revolve@1`; deterministic X/Y/Z partial/full rings, signed multi-turn pitch, open whole-turn screw topology, welded zero-pitch axis poles, seam-correct corner UVs, source-span/angle/axial-offset provenance, material/smooth attributes, compiler tangent-frame proof, and live Warden, ruin-capital, and counter-wound conduit consumers | Blender Spin/Screw source/evaluated separation; Godot explicit surface arrays | Add explicit open-end cap policies, field-driven parameters, multi-spline output, mesh-selection Spin, modifier-stack exposure, generated LOD variants, and a Studio profile/axis gizmo |
| Scatter/instance on points/realize | Partial | `rekall.modeling.scatter.area` currently realizes copies | Geometry Nodes instance operators | Preserve linked instances and add explicit realize |

## Normals, UVs, and materials

| Capability | Status | AGE ID/evidence | Blender reference | Exact gap |
|---|---|---|---|---|
| Generated normals | Implemented | `generate_normals`; compiler tests | mesh normals APIs | Make authoring policy first-class |
| Flat/smooth/sharp/auto smooth | Partial | `shade_faces` and `mark_sharp` semantic mesh operations; canonical face `normal.smooth` and edge `normal.sharp` attributes; `rekall.modeling.auto_smooth` graph node and `rekall.modifier.auto_smooth`; deterministic angle, boundary, and nonmanifold classification; Warden and weathered-ruin consumers | `bmo_normals.cc`, set-shade-smooth node | Add Studio face/edge visualization and selection editing, face-strength policy, and imported-policy migration tools |
| Weighted/custom corner normals | Partial | `weighted_normals` now builds split corner smooth fans from flat/sharp policy with bounded face-area and corner-angle exponents; graph and modifier surfaces publish canonical semantic `normal.authored`; compiler tests prove finite unit normals and orthogonal tangents; Aetherfall publishes two live-linked consumers | `MOD_weighted_normal.cc`, normal edit | Add explicit custom-normal editing, normal transfer, face-strength weighting, Studio diagnostics, and broader import/export round trips |
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
| Armature/skin/weights/constraints | Partial | Wave 3: point-domain `Int4` joint indices and `Float4` weights now validate, normalize, survive semantic mesh edits, compile into corner-expanded vertices, project through Model Assets, and deform through the existing skeleton-pose renderer; Aetherfall provides a weighted animated consumer | Blender armature modifier and vertex groups; Godot mesh arrays, Skin, and Skeleton resources | Add native rig/armature documents, hierarchy editing, bind-pose generation, weight assign/paint/normalize/prune tools, constraints, Studio visualization, and complete character acceptance |
| Sculpt/attribute/weight paint | Planned | Wave 4 | Blender sculpt BVH/stroke cache | Deterministic brush stamps, local dirtiness, compact undo |
| Multires/dynamic topology/retopology | Planned | Wave 4 | Blender multires/sculpt/retopo concepts | Explicit lossy policy and attribute-transfer report |
| GLB/OBJ/PLY/STL import and reimport | Partial | Wave 2 | Blender import nodes; Godot resource importers | Normalize into AGE source docs with hashes/settings/license/dependencies |
| Render/collision/nav/LOD/meshlet cooking | Partial: static CPU LOD selection now compacts per-surface inputs, preserves seams and coincident disconnected components, apportions whole-renderable triangle budgets with truthful unsatisfied diagnostics, caches stable reductions across runtime consumers, and has an accepted 17-consumer Aetherfall slice; skinned/morph payloads deliberately remain at source resolution | Waves 2–3 | Godot importer mesh and renderer mesh storage | Add cooked authored LOD artifacts, skinned/morph remapping, collision/nav derivation, GPU meshlets/page streaming, package round-trip, and broader fixed-camera quality evidence |

## Optional external mesh generation

| Capability | Status | Provider/reference | AGE requirement |
|---|---|---|---|
| Text/image/multi-view to editable mesh | Experimental Tripo text-to-GLB proof exists; broader work deferred until native Wave 1/Aetherfall acceptance | Tripo official generation API; Meshy official Text/Image/Multi-Image to 3D APIs | Replace the one-off synchronous bridge with a provider-neutral asynchronous job contract; support text/single-image/labeled multi-view inputs; GLB-first normalized import; never bypass AGE source/edit/cook layers |
| Provider remesh/retexture/rig/animation | Researched; not implemented in the generic provider layer | Tripo post-process/texture/rig/retarget tasks; Meshy remesh/retexture/humanoid rigging/animation APIs | Preserve provider/task/model-version capability snapshot, provenance, licenses, units, axes, topology/UV/PBR/rig metadata, and allow ordinary AGE re-edit/reimport |
| Cost, credentials, consent, and failure handling | Existing Tripo proof reads a server-side environment key and bounded-polls; production job controls deferred | Provider authentication, live pricing, task progress/streaming, cancellation, expiring-output, and changelog documentation | Server-side secrets, explicit remote-data consent, quoted maximum cost plus actual credits, durable resumable jobs without signed URLs, bounded retry/cancel, and inspectable diagnostics |
| Comparative production acceptance | Evaluation designed; paid credentialed run deferred | Same ruin prop, modular environment piece, and humanoid through current Tripo and Meshy models | Record prompt/reference hashes, settings, cost, time, geometry/material/texture/rig metrics, validation warnings, fixed-camera captures, Studio edit/reimport, cook, package, and player evidence before choosing any default |

The provider evaluation was refreshed against official documentation on
2026-08-26 and remains intentionally deferred behind the current native modeling
tranche and Aetherfall visual acceptance. The recommended order is generic job
contract, migration/expansion of the existing Tripo proof, Meshy adapter, then a
capped same-fixture comparison. AGE must remain capable without either service
and must treat generated output as untrusted imported source content subject to
the same validation, editing, cooking, packaging, and provenance rules as any
other asset.

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
