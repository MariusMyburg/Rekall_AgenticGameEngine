# Rekall AGE Production Progress

This is the durable execution ledger for Rekall AGE. Update it only from
verified repository or acceptance evidence. Conversational recency does not
change the priority order.

Last verified: 2026-08-24 13:54 Africa/Johannesburg

Branch: `codex/model-asset-games` (based exactly on `1c269fe`, which merged
`codex/web-scene-bootstrap` into `master`)

Current execution order is governed by
[`STRATEGIC-PRIORITIES.md`](STRATEGIC-PRIORITIES.md). The immediate acceptance
target is a substantial original platformer authored through the ordinary local
LLM/Studio/MCP path, accepted on Windows, then published as the unchanged AGE
project through a genuine browser-WASM runtime and direct WebGPU scene renderer.
The current moving-dot and triangle pages remain RenderingDevice proofs only;
CPU-frame upload, JavaScript gameplay rewrites, remote desktop streaming, and
other visually convincing substitutes are explicitly not web game publication.

The genuine web-publishing work now has a bounded static module build-input
slice in addition to its first staging exporter and shared-runtime seams.
Agent-authored modules and their configured runtime systems can be registered
through explicit constructor factories for browser trimming/AOT without
weakening the ordinary desktop module loader. A desktop build-time generator
reuses the canonical module build policy and verified build receipts, rejects
non-public, open-generic, unstable, or multi-module export layouts, emits
deterministic escaped direct C# type/factory references, and emits MSBuild
inputs that reference the original authored module projects. Those inputs bind
the generated registry to per-file source hashes before compilation and after
publication, while the staging request can consume that same immutable discovery
plan for its canonical manifest identity. The WebAssembly project conditionally
imports those inputs, binds each generated registration to its canonical module
ID/name/assembly/source fingerprint, and exposes the loaded module identities in
structured bootstrap evidence.
Canonical web manifests now include the verified module ID, assembly identity,
and source fingerprint.
A real trimmed `browser-wasm` publish test retains the authored fixture module
and now includes the unchanged staged `game.manifest.json`, project, entry scene,
asset catalog, and referenced asset bytes in the static web package;
the test suppresses the repository's pre-existing Core trim-analysis warnings
because those warnings are still treated as errors outside this slice. This
proves static compile/link inclusion and package composition, not rendering,
input, continuous simulation, or game playability.

Shipped game content can be read through one bounded logical-path contract from
either the filesystem or browser-style memory, and one byte-based scene codec
supplies identical schema/shape validation to the desktop scene store and future
browser bootstrap. An ordinary project and entry scene can be staged into a
path-confined deterministic inventory containing the project document, scene,
referenced-only sanitized asset catalog, and referenced asset bytes. The
exporter preserves the 64 MiB per-read and depth-128 document limits, adds a
4,096-entry/512 MiB aggregate closure bound, hashes actual staged bytes, and
emits the canonical `game.manifest.json`. Equivalent projects with relative or
root-dependent absolute imported asset paths produce the same logical manifest
and build identity. The browser-WASM host now performs bounded browser HTTP
reads, validates canonical manifest identity plus declared size/SHA-256 for the
project and entry scene, decodes both through trimming-safe shared codecs,
requires the generated static module IDs to match the manifest, constructs the
canonical `RekallAgeRuntimeWorld`, and runs one fixed runtime frame through the
generated authored-module/runtime-system factories. Structured evidence exposes
build/project/scene/module identities, frame/system facts, bounded entity and
component facts, and stable failure diagnostics. An executable staged-project
test proves an authored system changes `Game.BootstrapState.ticks` from 0 to 1
in that C# runtime world; tampered bytes, project-identity mismatch, oversized
HTTP content, and excessive registrations fail closed. Browser input, the
animation/simulation loop, direct WebGPU scene rendering, audio, and playability
remain unimplemented. The current Web page still renders only the WebGPU
contract proof and must not be described as a playable game export.
The consolidated manifest/export/bootstrap/codec/static-loader selection passes
55/55, including the real trimmed `browser-wasm` publish; Web Player Release
builds with zero warnings/errors, and the browser-platform JavaScript contract
selection passes 23/23. Independent review found no blockers. A legacy runtime
viewport color-threshold test remains red on the unchanged base commit because
the shared camera-correct software renderer produces a darker valid cube than
that stale threshold expects; it is not caused by this web-bootstrap slice.

Windows graphics packages now launch the authored runtime scene directly and
no longer append the misleading legacy `--playable` flag. For compatibility,
that obsolete flag also selects the canonical runtime; the CPU-raster proof
adapter is available only through the explicit `--legacy-playable-adapter`
diagnostic option. Held keyboard, mouse, controller, and semantic-action state
now persists across fixed-step catch-up ticks while transient press/release
edges and deltas are cleared, so gameplay input is not render-rate dependent
after a hitch. The focused player/package/input selection passes 19/19, the
Windows player Release build has zero warnings/errors, and a real three-frame
Clockwork Canopy launch through the obsolete flag loaded 25 runtime renderables,
loaded no legacy adapter, and exited normally.

The runtime UI projection now respects entity visibility for Label, Button,
Panel, Image, UiElement, and UiCanvas visuals. A single entity may intentionally
carry both UiCanvas and one visual without expanding that visual to the full
canvas; its authored bounds are retained. Fixed anchors (equal minimum and
maximum anchors) now retain authored size and apply position/pivot at the anchor,
while differing anchors retain stretch semantics and apply the newly
agent-visible left/top/right/bottom offsets. The combined Runtime, Rendering,
and Modules verification for these UI contracts passes 849/849. A fresh
Clockwork Canopy capture reports zero UI observations; its remaining invisible
HUD is an authored positive bottom-edge offset, not a missing-canvas or
zero-sized-layout engine defect. Software viewport capture now
routes 3D meshes through the same mesh, model-matrix, authored-camera, and depth
pipeline used to prepare Vulkan scenes, then composites remaining 2D/UI content.
This removes the legacy fixed-oblique cube projection that drew rear faces and
made ordinary cubes appear hollow or inside-out. It also rasterizes the visible
portion of large triangles whose vertices lie outside the viewport and preserves
near-surface depth independent of entity order. The complete Rendering namespace
selection passes 501/501, and a fresh software capture of Clockwork Canopy now
matches the Vulkan camera geometry instead of inventing side/bottom cube faces.

The first stable Model Asset metadata and publishing foundation is now available through the default
CLI/Studio command registry and its derived MCP catalog. The canonical surface
is `rekall.asset.model.publish`, `rekall.asset.model.rebuild`,
`rekall.asset.model.inspect`, `rekall.asset.model.list`,
`rekall.asset.model.freeze`, `rekall.asset.model.unfreeze`, and
`rekall.scene.instantiate_asset`. One shared concrete publishing service now
supplies dependency health to placement through the dependency-neutral Assets
contract; LevelDesign does not depend on AssetPipeline. A registry-only
end-to-end proof creates an editable mesh, publishes and lists a stable Model
Asset, creates a scene, places the asset, inspects the entity, attaches and
retains `Game.HeroState`, edits the source into Stale health, rebuilds to
Current, and proves the same entity ID, `Rekall.ModelAssetReference` asset ID,
and agent-owned component values survive in persisted scene metadata. This is
not yet proof that the placed Model Asset renders in a player or hot-refreshes
visibly after rebuild. Published geometry is now content-addressed and immutable;
the revision-checked Model Asset manifest is the durable commit pointer, exact
validated output races are safely reused, and deterministic interruption tests
prove a valid last-successful manifest/output pair at every publication boundary.
Immutable blobs are never rollback-owned, never recorded as deletable transaction
preimages, and can remain unreachable after a failed pointer publication. Cleanup
is explicit architecture debt: any future collector must be bounded,
reachability-aware across Model Asset manifests, and use a grace period rather
than deleting blobs during rollback or undo. Legacy transaction logs that claim
delete or overwrite ownership of compiled blobs are now rejected before mutation
by the default registry and Workbench undo, including link/junction aliases; the
stable errors are `REKALL_RESOURCE_RESTORE_PROTECTED` and
`REKALL_RESOURCE_RESTORE_PATH_INVALID`. Current and Frozen manifests, compiled
bytes, and unrelated scene/history resources remain unchanged. Catalog writers
replay pure, side-effect-free semantic transforms under optimistic revision conflicts, expose
stable `REKALL_ASSET_CATALOG_BUSY` exhaustion after 16 attempts; correlated
frozen inspection validates compiled structure and provenance without requiring
the editable source, all publication preimages are read once under the 64 MiB
bound, and Model Asset/recovery paths reject filesystem-link traversal. MCP
discoverability is derived from the registry rather than a parallel tool
implementation. The focused Model Asset, published-output, placement,
catalog-revision, and transaction coverage passes 99/99; the complete core
suite passes 1559/1559; the focused legacy-restore, transaction, Model Asset,
Workbench, placement, and MCP coverage passes 75/75; the complete Studio suite
passes 51/51;
the Windows player prerequisite and `Rekall.AGE.sln` Release builds both succeed
with zero warnings and zero errors. The next Studio slice is the Modeling workspace's
Publish/Update action, a health-aware Asset Browser with viewport drag/drop
preview, and Inspector component attachment, all as clients of these same
commands. Visible runtime Model Asset resolution and hot-refresh acceptance are
the next engine/runtime proof after that Studio surface.

Studio scene interaction now uses a render-derived interaction snapshot rather
than guessed editor geometry. Uniform-stretch letterboxing is mapped exactly;
UI hit regions take priority; world hits resolve by depth and stable identity;
hidden entities and synthetic render surfaces cannot be selected. The viewport
now selects canonical scene entities directly. A tested three-axis scene gizmo
projects from that same snapshot and exposes Select, Move, Rotate, Scale,
World/Local, and separate movement/degree/scale snapping controls. Dragging a
handle publishes exactly one canonical `rekall.component.set_property`
transaction, and the persisted `Rekall.Transform3D` change reverses through the
ordinary Studio undo stack. Locked entities do not expose an editable gizmo.
Simulate mode also has deterministic Pause/Resume and exact single-frame Step;
paused timer ticks are suppressed and stopping clears paused state.

The Studio shell now treats World and Modeling as top-level per-window
workspaces, following the useful Blender distinction between a workspace and a
nested tool panel. Modeling hides the game/project bars and fills the client
area with resizable Mesh Editing, Procedural Geometry, Materials, and
UV/Attributes surfaces. The Mesh Editing surface is now viewport-first: a
compact functional tool shelf and edit header surround a production grid,
auto-framed editable geometry, orientation indicator, mesh outliner, and active
tool/properties region. Its Add action is backed by the generic modeling
primitive factory and canonical asset transaction; box, grid, sphere, cylinder,
cone, and torus all create real editable assets which Studio immediately opens.
Mesh and procedural controls are bound to the canonical sessions rather than
display-only substitutes. World exposes live hierarchy,
inspector, and output splitters; panel visibility; Default, Authoring, and Debug
presets; and versioned, normalized persistence of window bounds, maximization,
panel sizes/visibility, output tab, and active workspace. Rich scene actions now
rename, duplicate, delete, show/hide, lock/unlock, parent, and unparent via
canonical command transactions. A real STA WPF render loads and evaluates the
populated procedural probe, switches into the dedicated workspace, requires the
project chrome to disappear and the modeling host to occupy the client area,
creates and opens a real cube through the same Add command as the UI, and writes
an inspected 1480×820 PNG proof. The complete Studio suite passes 51/51, the
complete modeling namespace passes 145/145, and the Release Studio build
succeeds with zero warnings and zero errors.

The procedural sphere contract is now a true closed shared-topology surface:
one vertex per pole, shared periodic seam vertices, outward cap triangles, and
shared middle-ring quads. The former schema-valid but open 45-point sphere for
8 segments/4 rings is replaced by a finite manifold 26-point/56-edge/32-face
surface with zero boundary and non-manifold edges. The real populated Studio
fixture now requires its box/sphere Boolean evaluation to succeed before WPF
layout can pass, so Studio presentation cannot hide a failed modeling output.

Production modeling matrices now close the catalog-wide verification gap.
Box, sphere, cylinder, cone, and torus each prove finite closed topology and the
correct Euler class. Every unordered pairing of those five closed shapes,
including same-family overlaps, executes union, intersection, and difference:
15 pairings and 45 Boolean evaluations, all required to return nonempty, valid,
closed topology. All 10 advertised semantic mesh operations and all 6 ordered
modifiers also execute twice from immutable input, produce byte-equivalent
deterministic meshes, advance revisions correctly, remain inside their declared
change masks, and pass strict validation; an exact inventory assertion fails if
a future catalog entry is advertised without joining the production matrix.
The focused production matrix passes 37/37, the complete modeling namespace
passes 139/139, and the complete Studio suite passes 37/37.

Studio procedural graphs now expose descriptor-driven typed parameter editors
for scalar, integer, Boolean, string, enum, material, and vector values. Editors
enforce finite numbers, declared bounds, enum choices, and exact vector arity;
the apply command is enabled only for a valid changed set. One action publishes
one exact-revision graph patch through the canonical service, reloads the
canonical graph, retains honest invalidation state, reevaluates the selected
output, refreshes its deterministic mesh preview, and reports patch/evaluation
diagnostics. The reusable `ProceduralModelingProbe` project supplies a populated
six-node box/sphere/transform/Boolean/UV/output graph for real Studio acceptance.
The complete Studio suite passes 37/37 and a zero-warning Studio build succeeds.

Real UI acceptance caught populated-graph crashes that source-level and session
tests could not: WPF `Run.Text` attempted to write display bindings back into
read-only node, parameter, and evaluation properties. All display-only inline
bindings are now explicitly one-way. A new STA layout test instantiates the real
Studio window, loads and evaluates the six-node probe, materializes node metrics,
typed parameters, and evaluation evidence, then exercises layout; it fails with
the former `XamlParseException` if a read-only inline binding regresses.

That probe exposed and drove a generic persistence repair: every modeling asset
store now canonicalizes relative project roots before publishing mesh, modeling
graph, material graph, material instance, or modifier-stack resource paths.
This prevents transaction summarization from resolving a relative root twice.
A red-to-green five-store regression and a real relative-root CLI graph patch
both prove an absolute changed resource, correct project-relative display path,
and `exists: true`. The complete modeling namespace now passes 101/101, and a
solution-wide locked restore succeeds.

Studio now opens persisted procedural modelling graphs as exact revisioned
assets and presents the same canonical node descriptors used by CLI/MCP:
stable node/type IDs, display names, descriptions, typed parameters with
resolved defaults, link counts, and named outputs. Evaluation uses a retained
engine evaluator so repeated output requests expose real per-node timing,
cache-hit, invalidation, budget, last-good-output, and structured diagnostic
evidence. A successful output is rendered through the existing deterministic
mesh viewport rather than an editor-only geometry path. Three focused session
proofs cover persistence/descriptor discovery, two-node cache reuse, output
mesh publication, and the closed pre-open failure path. The visible workspace
adds separate graph-control, node-contract, evaluated-output, and diagnostics
editors under Modeling. Its first shared design-system slice introduces
reusable editor panel/header/section tokens and a dense viewport-first dark
layout inspired by Blender's functional clarity while retaining AGE's cyan
identity and AI-first evidence surfaces. A real Windows render inspection
confirmed correct WPF composition and accessibility structure. The Studio
project builds with zero warnings/errors; the focused graph tests pass 3/3.
Graph mutation/canvas layout, node linking, attribute/material inspectors,
persistent docking, orbit/pan/zoom, and broader visual-regression automation
remain open. The complete Studio suite passes 34/34.

The Studio graph session now also mutates through the canonical
`RekallAgeModelingGraphPatchService` against its exact opened file revision.
Successful batches reload canonical node/output views, clear stale evaluated
output, retain the evaluator cache for honest dependency invalidation evidence,
and append recoverable transaction history. A parameter-edit proof changes a
box from two to four world units, advances logical/file revisions, records one
transaction, and then observes both reachable nodes invalidated on reevaluation.
The focused graph-session selection passes 6/6. Typed graph-patch controls are
now present; the visual node canvas and structural add/link/remove controls
remain the next Studio authoring slice.

The modeling/Studio tranche is now fast-forwarded onto local `master` through
`8b34093`. Integration validation found and repaired two stale transitive lock
graphs: the Windows and Web players now include the modelling project's
`SamuelRe.CSG.Sharp` dependency and a solution-wide locked restore succeeds.
The merged checkout's complete Studio suite passes 34/34. A broader modelling
test build initially exhausted the F: volume while copying reproducible CLI
runtime outputs, not while executing tests; those build outputs were cleaned
safely. With space restored, the active feature worktree now passes the full
modeling selection at 101/101; the next master merge will rerun that same proof
on the integrated checkout.

The first optimization primitive is verified across every canonical modeling
composition path. `merge_by_distance` welds selected points through a bounded,
deterministic spatial hash; averages compatible point attributes; remaps face
corners and point/edge selections; removes collapsed loose edges; deduplicates
coincident edges; and reports deleted/modified stable IDs plus point/edge
provenance. It fails closed when a requested weld would collapse a face
boundary or when coordinate/distance ratios exceed safe cell bounds. The same
implementation is exposed as `rekall.modeling.merge_by_distance` in procedural
graphs and `rekall.modifier.merge_by_distance` in immutable cached modifier
stacks, with descriptor discovery available through the existing graph,
modifier, and mesh operation commands. A disconnected two-quad seam proof
reduces 8 points/8 edges to a valid manifold 6 points/7 edges/2 faces while
retaining both faces. The full modeling namespace passes 82/82 tests and the
modeling project builds with zero warnings/errors. General remeshing, smoothing
subdivision, and boolean operations remain open; the next kernel work continues
with those generic capabilities rather than declaring the modeling tranche
complete.

True smooth surface subdivision is also verified. `subdivide_smooth` applies
one Catmull-Clark-style level to a complete manifold or boundary surface,
deriving shared face and edge points, interior and boundary vertex positions,
quad topology, stable point/edge/face provenance, expanded edge/face
selections, and interpolated point/edge/face/corner attributes. It retains
source IDs where possible, rejects partial-face evaluation until crack-safe
selection support exists, and reports non-manifold source edges with a stable
repair code rather than guessing. A boundary quad proof produces 9 points, 12
edges, 4 quads, 16 corners, and center UV interpolation; a closed box proof
produces a valid 26-point/48-edge/24-quad surface through both
`rekall.modeling.subdivide_smooth` and
`rekall.modifier.subdivide_smooth`. The modeling namespace now passes 85/85
tests and the modeling project remains zero-warning/zero-error.

Studio now has its first native modeling workflow, implemented as a client of
the same mesh contracts rather than a parallel editor-only mutation system.
The Modeling tab discovers persisted mesh assets, opens exact file revisions,
switches point/edge/face/corner edit domains, exposes stable element IDs,
maintains ordered/active extend-or-toggle selection state, lists compatible
semantic operations, accepts their structured parameters, previews without
disk mutation, cancels previews, and applies through optimistic revision checks
plus reversible transaction history. The session proof previews a quad
extrusion to five faces while the stored source stays at one, then applies the
same operation, reloads the five-face mesh, clears stale selection/preview
state, and verifies one transaction-log entry. The complete Studio suite passes
26/26. This is a usable schema-driven edit slice, not completion of Task 9:
viewport element picking/overlays, direct manipulation, parameter-schema
editors, attributes/materials, graph editors, and sculpt/paint remain open.

The first mesh viewport and element-picking layer is now integrated into that
tab. It auto-frames finite 3D mesh positions through a deterministic isometric
projection, draws shaded face regions, stable edges, point/corner markers,
active-domain selection highlights, and an explicit preview watermark, and
retains bounded screen-space hit records for point, edge, face, and corner IDs.
Mouse clicks select the returned stable ID through the same ordered modeling
session; Shift extends and Ctrl toggles. A 640x360 proof independently picks the
expected point `1`, edge `11`, face `21`, and corner `31` from their projected
locations and rejects an out-of-frame point. The complete Studio suite passes
27/27. This is a deterministic edit viewport, while orbit/pan/zoom, occlusion-
aware GPU picking, transform handles, and compiled material display remain
open.

Studio mesh operations now use typed, descriptor-driven parameter controls
instead of requiring authors to write JSON. Each compatible operation exposes
its canonical parameter name, data type, required/optional state, default, and
help text; malformed numeric values disable preview/apply, and only validated
values are converted into the operation request. Extrusion advertises a useful
one-unit Z default while generic point transforms retain zero offsets. A focused
proof verifies descriptor defaults and invalid-number rejection. After fresh
recompilation, the complete Studio suite passes 28/28 and the modeling namespace
passes 85/85. Direct manipulation, richer attribute/material editing, and graph
authoring remain open, so this advances but does not complete the Studio
modeling tranche.

Point editing now has its first direct viewport manipulation path. A selected
point set receives colored X/Y/Z handles at its projected centroid; bounded
axis hit testing converts a screen drag through the viewport projection scale
into a deterministic single-axis mesh-space delta. Releasing the drag applies
the existing semantic `transform` operation as actor `studio-gizmo`, so the
edit uses the same validation, optimistic persistence, and reversible
transaction history as C#/CLI/MCP operations instead of an editor-only
mutation. Surviving stable-ID selections and their active order are retained
after operations, enabling repeated manipulation; deleted IDs are filtered.
Focused proofs cover axis selection/projection math, off-gizmo rejection,
persisted two-point translation, and selection retention. The complete Studio
suite passes 30/30 after recompilation. This is translation-only on the current
fixed isometric view; rotation/scale handles and orbit/pan/zoom remain open.

The procedural primitive substrate now includes
`rekall.modeling.primitive.frustum`, a bounded descriptor-driven node covering
cylinders, cones, and tapered solids with independent non-negative top/bottom
radii, positive depth, 3-4,096 radial segments, and independent cap controls.
A zero-radius end is represented by one true apex with triangular side faces,
not a ring of coincident degenerate points. Its topology is assembled through
shared stable edges, deterministic face winding and IDs, then passed through
the strict mesh validator before publication. Cylinder and true-apex cone
proofs respectively produce valid 16-point/10-face and 9-point/9-face meshes;
two zero-radius ends fail with a stable parameter diagnostic. Descriptor
inventory discovery and the full modeling namespace pass 88/88 after
recompilation. Robust arbitrary-mesh booleans, remeshing, and a broader
primitive inventory remain open.

`rekall.modeling.primitive.torus` adds a second periodic-surface primitive with
positive major/minor radii and independently bounded 3-4,096 segment counts in
both directions. It creates one shared point at each periodic coordinate and
deduplicated shared edges across both seams, rather than overlapping seam
rings. An 8x4 proof produces a closed manifold with exactly 32 points, 64
edges, 32 quads, 128 corners, zero boundary edges, and zero non-manifold edges.
Self-intersecting radius pairs fail with a stable parameter diagnostic. The
descriptor inventory is updated and the complete modeling namespace passes
90/90 after recompilation.

The first strict solid-Boolean graph node is now verified.
`rekall.modeling.boolean` accepts two required geometry inputs and exposes
`union`, `intersect`, and ordered `difference` through the same portable graph
descriptor/patch/evaluate/bake surface. Inputs must be non-empty closed
manifolds. AGE triangulates them deterministically, invokes the 100% C#
MIT-licensed `SamuelRe.CSG.Sharp` 1.0.0 BSP kernel, welds the returned vertices
with a scale-aware tolerance, splits every polygon boundary at coincident
kernel vertices to eliminate T-junctions under a hard 50-million-check work
ceiling, rebuilds shared stable topology, and refuses any output with validation
errors, boundary edges, or non-manifold edges. Overlapping-box union,
intersection, and difference plus a rotated non-coplanar union all return valid
closed AGE meshes with expected bounds. Open surfaces fail with
`REKALL_MODELING_BOOLEAN_INPUT_NOT_CLOSED_MANIFOLD`; attributed/material inputs
currently fail with `REKALL_MODELING_BOOLEAN_ATTRIBUTES_UNSUPPORTED` rather
than losing authored data. The dependency is in the distribution lock graph,
its full MIT notice is shipped in `THIRD-PARTY-NOTICES.txt`, NuGet reports no
known direct or transitive vulnerabilities, the modeling namespace passes
96/96, and Studio passes 30/30 with the CSG assembly copied beside the desktop
binary. Attribute interpolation/provenance through split faces and broader
adversarial Boolean fixtures remain open before this is called a complete
Boolean system.

Boolean results now carry two typed face-domain provenance attributes:
`boolean.sourceOperand` (`a` or `b`) and `boolean.sourceFaceId` (the stable
source-face ID encoded losslessly as a string). Their value counts are required
to match result faces and the kernel wrapper fails if source metadata is lost.
These engine-owned attributes are the only attributes accepted back into a
subsequent Boolean; user-authored attributes and materials still fail closed.
Boolean input conversion now reuses AGE's canonical deterministic ear-clipping
compiler, which safely triangulates concave n-gons and prior Boolean faces with
collinear T-junction split points. A two-stage union-then-frustum-difference
graph proves that Boolean outputs remain composable, closed, valid, and expose
fresh current-node provenance. The complete modeling namespace passes 97/97.

Boolean evaluation now preserves compatible face-domain authored data. Operand
attribute schemas are matched by name/type/semantic/interpolation; each result
face copies the value belonging to its source face, while missing non-material
face attributes use their typed default. Material slots from both operands are
deduplicated by slot name and asset ID and source `material-index` values are
remapped into the merged slot table, so split faces retain the correct material.
A two-material overlapping-solid proof returns both `mat.stone` and `mat.metal`
and result faces referencing both remapped indices. One-sided material schemas
fail with `REKALL_MODELING_BOOLEAN_MATERIAL_SCHEMA_MISMATCH`; point/edge/corner
attributes fail with `REKALL_MODELING_BOOLEAN_ATTRIBUTES_UNSUPPORTED` until
true split-vertex interpolation is implemented. The modeling namespace passes
99/99 after recompilation.

Corner-domain Boolean interpolation and portable UV projection are now
verified. `rekall.modeling.project_uv` exposes the existing semantic UV
operation as a descriptor-driven graph node with named output attribute,
XY/XZ/YZ axis choice, and finite scale/offset controls. The Boolean adapter
attaches each kernel polygon to the canonical compiler triangle positions and
stable source-corner IDs. Every output corner, including T-junction insertions
and newly cut vertices, receives barycentric values from that source triangle;
nearest, linear, and normalized-linear policies are honored across scalar,
vector, color, quaternion, matrix, Boolean/integer, and string types as
appropriate. A rotated overlapping-box proof projects UVs before the Boolean
and returns a finite, varied `uv.generated` value for every output corner while
remaining strictly valid and closed. Point and edge domains still fail closed
because their ownership at cross-operand seams needs an explicit contract. The
node inventory and complete modeling namespace pass 100/100 after
recompilation.

The Blender-informed agentic modelling tranche is now the active implementation
priority after the completed WebGPU and remaining Godot audits. A shallow,
blobless, sparse Blender reference checkout is pinned at
`4641b05b1687912ec97d021f12c1076aba3b90ae`; no GPL source is copied. The audit
establishes that AGE's current packed triangle commands are render ingestion,
not a modelling system. The accepted architecture introduces persistent mesh
assets with stable point/edge/face/corner identities, typed domain attributes,
revision-safe semantic operations, strict validation, provenance/diffs,
immutable evaluated snapshots, and a deterministic compiler into the existing
render/physics/GLB/Studio paths. Procedural geometry and semantic material
graphs will share that substrate and expose one canonical descriptor surface to
C#, CLI, MCP, Studio, prompts, and documentation. Community Blender MCP research
reinforces a closed inspect/mutate/viewport-evidence loop, while AGE deliberately
uses bounded typed operations instead of arbitrary remote scripting as the
normal path. The design, source audit, first acceptance gate, and staged
test-driven implementation plan are recorded in the 2026-08-23 modelling docs;
Task 1 (contracts and strict topology validation) is complete. New independent
`Rekall.Age.Modeling.Contracts` and `Rekall.Age.Modeling` projects define compact
point/edge/face/corner topology, stable element IDs, typed domain attributes,
material slots, named/active/history selection records, and finite bounds. The
strict validator reports coded element-linked errors for document/ID/array
shape, invalid references, self/duplicate edges, malformed/repeated/duplicate/
zero-area faces, corner-edge endpoint mismatches, attribute type/length,
material indices, and selection-domain errors; structurally valid boundary,
loose, and non-manifold topology is summarized, with non-manifold edges emitted
as warnings rather than silently rejected. The seven focused tests pass and the
complete Release solution builds with zero warnings and zero errors. Task 2 is
also complete: `RekallAgeMeshAssetStore` persists canonical bounded mesh JSON
under stable logical IDs in `Modeling/Meshes`, fails closed on unsafe IDs,
future schemas, invalid meshes, stale file revisions, and invalid logical
revision increments, and reuses AGE's atomic publication, SHA-256 revisions,
document-size/depth boundary, previous-version recovery, quarantine, and schema
inspection. Focused persistence evidence covers canonical round trips, listing,
no temporary siblings, conflicts without overwrite, recovery/restore, safe
names, invalid topology rejection without publication, and future-schema
rejection. All 15 modelling kernel/store test cases pass and the Release
solution remains zero-warning/zero-error. Task 3, editable adjacency, selectors,
and the semantic operation framework, is active next.

Task 3 has its first verified slice. `RekallAgeMeshAdjacency` derives stable-ID
point-to-edge, point-to-face, edge-to-point, edge-to-face, and face-neighbor
facts only from strictly valid assets, including loose-edge and shared-edge
cases. Public operation contracts now carry revision lineage, fine-grained
created/deleted/modified element sets, changed attributes, affected bounds,
stable-ID provenance, and output validation. The pure operation executor
implements atomic point transforms and face-winding reversal, preserves the
input snapshot, advances the logical output revision, rejects missing IDs,
wrong domains, duplicate selections, unknown operations, and non-finite
parameters with stable codes, reorders corner-domain attributes with their
stable corner identities, and refuses to return invalid output. All 20 modelling
namespace tests pass. Generic selectors, topology-creating operations, batch/
preview persistence, and element deltas remain active Task 3 work.

The first topology-creating operation is now implemented and verified.
`triangulate_faces` derives triangle faces from selected ngons while keeping the
source asset immutable, retaining the original face ID where possible, creating
only the required diagonal edges/faces/corners, returning source-face to output-
face provenance, copying face and corner attributes through explicit source
maps, initializing newly created edge attributes from declared/type defaults,
and passing the strict validator before returning. The focused quad test proves
one diagonal edge, two triangle faces, six corners, stable source provenance,
and six propagated UV values. Extrude remains the next topology operation.

`extrude_faces` is now implemented as a true region operation rather than an
isolated generator. It duplicates each selected point once, duplicates selected
edges for the translated top region, creates vertical edges only for unique
boundary points, preserves selected face and corner identities on the top,
creates side quads only on region boundary edges, propagates point/edge/face/
corner attributes through explicit source maps and defaults, expands named
point/face selections through provenance, and reports created elements, changed
attributes, affected bounds, and original-to-top/side mappings. The focused
quad proof produces 8 points, 12 edges, 5 faces, and 20 corners from an immutable
4-point source and passes strict validation.

Generic stable-ID element queries are now implemented for point, edge, face,
and corner domains. Selectors can intersect explicit IDs, named selection sets,
one-ring connectivity, finite spatial bounds, and typed attribute equality;
results are deterministically ordered, capped at 4,096, and report matched,
domain, and truncation counts. Connectivity uses the validated adjacency view,
spatial predicates use point positions, edge midpoints, face centroids, or
corner points, and wrong-domain/missing IDs, selections, attributes, bounds,
and limits fail with stable repairable codes. Three focused query tests pass.

Task 3 and the first agent-facing Task 4 surface are now verified.
The semantic executor publishes canonical operation descriptors and implements
face deletion alongside transform, reverse-winding, ngon triangulation, and
region extrusion. Face deletion removes dependent corners and domain data while
deliberately preserving now-loose points and edges as editable topology. The
revision-safe edit service supports read-only preview, one-operation apply, and
1-128 step atomic batches: candidates execute fully in memory, validation or
operation failure publishes nothing, and a successful batch advances the mesh
only one logical revision while capturing one transaction preimage. Eight typed
commands now expose create, bounded inspect, strict validate, semantic query,
preview, apply, batch, and deterministic assertions through the default engine
registry and MCP/JSON-RPC under the dedicated `modeling` category. Evidence is
bounded to stable-ID samples, counts, affected bounds, revisions, diagnostics,
change sets, and provenance rather than dumping mesh buffers. Checkpoint policy
keeps these bounded mesh construction/repair commands available while gameplay
evidence is being established. The CLI now has a generic registered-command
gateway (`command execute <name> <arguments-json>`), so the same canonical
schemas are scriptable without parallel parsers; JSON arguments are redacted
from logs and transaction names. A physical Release CLI create/inspect run
created a strict triangle asset and returned a two-ID truncated sample. The
Release modelling/registry/checkpoint selection passes 35/35 and the Debug
CLI/MCP/process selection passes 18/18, all with zero build warnings/errors.
Every mesh result now also carries bounded canonical next-tool guidance for
inspection, preview/apply, validation, and assertions. Task 4 is complete; the
editable-mesh render compiler and scene asset reference are active next.

Task 5 now has a verified runtime compiler and scene-reference slice. Strictly
validated editable assets compile deterministically into immutable UInt32-indexed
triangle snapshots with concave-ngon ear clipping, point/corner attribute splits,
authored or generated normals, finite tangents, material surfaces, and stable
face/corner/point provenance for every output triangle. The generic
`Rekall.MeshAssetReference` component resolves a persistent asset and optional
exact file revision through project-aware runtime worlds; software, Vulkan,
WebGPU, OpenXR, headless inspection, Player, preview, GLB-consuming render
frames, and Studio now receive that common geometry model. The compiler/file-
revision cache now lives in the modelling layer so rendering and BEPU consume
one resolver and one immutable snapshot. Static triangle-mesh and dynamic convex-
hull cooking use compiled positions/indices, retain packed `GeometryMesh`
compatibility, recook when the source revision changes, and surface coded physics
observations on failure. A command-level GLB proof exports the compiled editable
asset and independently reads one mesh back from its metadata. Resolution failures
and stale revisions become coded viewport observations instead of silent blank
geometry or render-loop crashes. Runtime viewport indices are UInt32 end to end,
and triangle provenance remains attached for picking and repair loops. The
focused compiler/render/legacy/Vulkan selection passes 38/38; the subsequent
compiler/render/physics/GLB selection passes 12/12. The engine test
project and Studio build with zero warnings/errors, and Player's changed source
compiles cleanly. Task 5 is now code-complete: compiled material-slot surfaces
remain distinct draw meshes through Vulkan/WebGPU preparation and GLB export,
carrying material asset IDs, exact index ranges, and source-face membership.
The deterministic legacy adapter promotes packed indexed triangles into strict
editable topology, deduplicates shared edges, assigns stable point/edge/face/
corner IDs, and preserves complete authored normal/UV/color streams. The final
compiler/render/physics/export/legacy selection passes 46/46 with a zero-warning,
zero-error build. The transitive NuGet project graph is relocked for the new
modelling dependencies; all 1,347 ordinarily runnable Release engine tests pass,
and the three Windows Player tests whose generated executable had been cleaned
pass after rebuilding that explicit prerequisite (1,350/1,350 combined). Task 6's command-level closed loop, compact reversible deltas,
and independently inspected visible evidence are active next.

Task 6 has begun with a verified grouped undo/redo invariant. A two-operation
mesh batch (point transform plus face-winding reversal) publishes once, restores
the exact valid pre-edit topology through the transaction preimage command, and
then restores the exact edited positions, corner identities, and corner-point
mapping from the undo transaction's captured preimage. The focused edit-service
selection passes 4/4. Compact element-delta persistence remains the active next
step; the test deliberately proves existing lossless behavior before replacing
full mesh snapshots with bounded reversible deltas.

Compact reversible transaction deltas are now implemented and verified. For
changed JSON resources, the transaction log recursively records only changed
object values and bounded array splices, retains before/after SHA-256 identities,
and admits a delta only when its encoded operations are smaller than the full
preimage. Mesh undo prefers the exact-after-state delta, reconstructs canonical
before bytes, and verifies the before SHA; a missing snapshot therefore no
longer prevents exact undo. If the resource advanced after the transaction, the
delta is not force-applied and the integrity-checked full preimage remains the
safe compatibility fallback. Compactness, delta-only mesh restoration, grouped
undo/redo, stale-after fallback, transaction persistence, and history behavior
pass a combined 12/12 focused selection with zero build warnings/errors. The
deterministic closed-loop modelling fixture is active next.

Task 6 is now complete with a real MCP JSON-RPC closed loop and committed
evidence. The default registry exposes bounded compiled inspection and local-ray
picking commands; both return immutable material-surface facts and exact source
face/corner/point provenance without dumping raw buffers. The deterministic
fixture authors a strict ngon plus a shared-point triangle with distinct corner
UVs, two material slots, and a named face selection; extruding through MCP yields
11 points, 17 edges, 7 faces, 28 corners, 14 triangles, and three contiguous draw
surfaces spanning both material slots. The same agent path validates, compiles,
picks source face 21, creates the scene and camera, captures a software runtime
frame, discovers the edit in transaction history, undoes exactly to logical
revision 1, and redoes exactly to revision 2, with no direct mesh or scene JSON
editing. Independent PNG inspection exposed and fixed a generic compiled-mesh
material bug: the compiler/runtime contract now preserves whether vertex colors
were actually authored, so absent colors defer to `Rekall.Material.baseColor`
through both software and Vulkan paths instead of default white masking the
material. The final 640x360 proof contains 65,841 cyan foreground pixels (28.6%
of the frame), and its bounded JSON report records topology, compiler, surface,
pick, pixel, SHA-256, undo, and redo facts under
`docs/production/evidence/agentic-modelling-closed-loop`. The focused modelling,
MCP-catalog, rendering, and transaction selections remain green. Task 7's
versioned procedural modelling graph is active next.

Task 7 now has its source-document, descriptor, validation, persistence, and
atomic-patch foundation. Twelve canonical version-1 node descriptors cover box,
grid, and sphere primitives plus transform, join, extrude, triangulate, captured
and named attributes, scalar field math, material assignment, and mesh output.
Descriptors expose typed directional ports, cardinality, required inputs,
parameters, defaults, ranges, units, and enum choices from one C#/future-command
surface. Versioned graph assets carry stable node/link IDs, named outputs, and
exposed parameters. Strict validation rejects unsupported schemas, unsafe or
duplicate identities, unknown type versions and directional ports, unknown
parameters, incompatible value/domain links, missing or multiply connected
inputs, invalid outputs, and cycles; valid graphs compile to deterministic
topological plans containing only nodes reachable from requested outputs and
report unused nodes separately. Canonical graph files persist under
`Modeling/Graphs` with bounded schema probing and exact SHA-256 file revisions.
Typed 1-256 operation patch batches add/remove nodes and links, set parameters
and outputs, and manage exposed parameters entirely in memory; the candidate
advances one logical revision, passes full validation, captures a transaction
preimage, and publishes atomically only when the expected file revision still
matches. Stale writes and cycle-producing batches leave the source bytes
unchanged. The focused graph contract/persistence/patch suite passes 7/7 with a
zero-warning, zero-error affected-graph build. Deterministic demand evaluation,
node hashing, cache invalidation, budgets, and bounded reports are active next.

Task 7 demand evaluation is now implemented and verified. Evaluations select
named outputs and execute only their reachable deterministic plan. Each node's
SHA-256 cache identity includes its type/version, recursively canonical sorted
parameters, ordered input-link hashes, deterministic seed/time, engine version,
evaluation schema, graph schema, and target profile; graph logical revision is
reported but deliberately does not invalidate unrelated nodes. The evaluator
tracks per-asset/node prior identities to distinguish cache hits from actual
dependency invalidations, bounds its node report, checks cancellation and wall
time, and enforces node, total unique-live point/face, approximate-memory, and
report budgets. Successful outputs become the last-good immutable snapshots;
evaluation failure returns coded node-linked diagnostics and those snapshots
without pretending the failed candidate succeeded. The first executable box and
mesh-output nodes prove demand pruning (an unused sphere never executes), a
repeat run hits both cached nodes, and changing `sizeX` invalidates exactly the
box/output chain while moving evaluated X bounds from +/-2 to +/-4. A separate
budget proof rejects the eight-point box under a four-point limit and returns
the exact prior output. All 9 graph contract, persistence, patch, and evaluation
tests pass with a zero-warning, zero-error affected build. Initial node evaluator
coverage and bake-through to persistent editable meshes are active next.

The procedural graph now crosses the production mesh boundary. The transform
evaluator applies nonzero per-axis scale followed by deterministic X/Y/Z Euler
rotation and translation to a replacement topology while retaining upstream
immutability and strict mesh validation. The bake service demands one named
graph output, refuses failed evaluation, checks the target mesh's exact file
revision, derives precisely one next logical mesh revision, captures a
transaction preimage, and atomically publishes through the existing
`RekallAgeMeshAssetStore`; new targets require the explicit missing-document
revision. A transform proof scales a two-unit box on X, rotates 90 degrees about
Z, and translates it to produce X bounds 2..4 and Y bounds -2..2. A two-bake
proof changes the source width from 2 to 6, advances the persistent target from
logical revision 1 to 2, and compiles that same baked asset through the ordinary
runtime compiler to 12 triangles with X bounds -3..3. The focused transform and
bake tests pass with zero build warnings/errors. Remaining Task 7 work is the
broader executable node inventory and its agent command/MCP surface.

The first topology-changing procedural chain is also executable. The grid node
creates deterministic subdivided quad topology from bounded size and segment
parameters with stable per-domain IDs. Extrude and triangulate nodes demand
geometry inputs and invoke the existing semantic `extrude_faces` and
`triangulate_faces` operations over the evaluated face set, then re-identify the
immutable result as the node output at the graph revision. A 4x2 single-cell
grid extrudes by +2 Z into a strict 8-point, 5-quad solid and triangulates to 10
triangle faces with bounds Z=0..2. This proves graph topology nodes reuse AGE's
ordinary stable-ID provenance, attribute propagation, region-boundary rules,
and strict validation rather than maintaining a parallel modelling kernel. The
focused chain test and affected build pass with zero warnings/errors. Join,
sphere, field/attribute, and material-assignment evaluators remain active.

The full initial procedural evaluator inventory is now implemented. The sphere
primitive generates bounded UV/normal-bearing latitude geometry and deterministic
triangle topology. Join consumes ordered multi-links, remaps point/edge/face/
corner IDs and references, merges compatible attributes with typed defaults,
remaps face material indices into deduplicated slots, and preserves selections
under deterministic input-qualified names. Scalar field math supports broadcast
inputs or parameter constants for add/subtract/multiply/divide/min/max with coded
length, zero-division, and nonfinite failures. Named scalar attributes become
fields; capture writes or replaces finite float attributes at exact point/edge/
face/corner cardinality; material assignment creates/reuses a slot and publishes
face-domain `material.index` data. One proof joins a 48-face UV sphere and
translated six-face box into a valid 54-face mesh with unique stable domains.
Another evaluates 0.25*2, captures `weight=0.5`, reads it back, adds 0.5, captures
`weight.final=1.0`, assigns `mat.stone`, and passes strict mesh validation. The
affected build is zero-warning/zero-error and the new inventory tests pass. The
remaining Task 7 gate is a bounded default-registry CLI/MCP command surface for
node discovery and graph create/inspect/patch/validate/evaluate/bake/report
inspection; it is now an explicit plan item rather than an implicit omission.

Task 7 is now complete. Nine default-registry commands expose bounded node-type
search/inspection and graph create/inspect/atomic-patch/validate/demand-evaluate/
bake/latest-evaluation inspection through the same JSON command surface used by
the CLI and MCP. Evaluation and bake commands share one evaluator per registry,
so agents receive truthful cache-hit and dependency-invalidation evidence across
calls. Graph inspection caps node/link/output samples; evaluation and bake return
topology counts, finite bounds, bounded diagnostics and node timings without
dumping mesh buffers. Mutations retain exact file revisions, transaction
preimages, changed-resource facts, strict candidate validation, and atomic
publication. A default-registry JSON proof discovers and inspects the box node,
creates and validates a graph without direct document edits, evaluates a width-2
box, proves two cache hits on repeat, applies a revision-checked width-6 patch,
proves exactly two invalidated nodes and X bounds changing from +/-1 to +/-3,
bakes through the normal mesh store, compiles 12 triangles, and inspects the
latest bounded evaluation. MCP catalog classification publishes all nine under
`modeling`. The broadened modeling, closed-loop mesh, registry, and MCP selection
passes 25/25 with zero build warnings/errors. Semantic material graphs and
generic modifier contracts are the active Task 8 gate.

Task 8 has its first verified foundation. A distinct material-graph document
keeps semantic authoring independent from backend shader text and carries stable
node/link IDs, one typed surface output, logical/file revisions, and typed
instance-facing exposed parameters. Eleven version-1 descriptors cover scalar
and color constants, UV coordinates, mapping, texture sampling, float math,
color mixing, normal mapping, PBR and emissive closures, and material output.
Their shared catalog declares typed directional ports, required/cardinality
rules, defaults, finite ranges, enum choices, and texture-asset references.
Strict validation rejects unsafe/duplicate IDs, unknown type versions,
parameters and directional ports, incompatible links, missing or multiply
connected inputs, invalid outputs, and cycles, then produces a deterministic
reachable execution plan and separately reports unused nodes. Canonical files
under `Materials/Graphs` use AGE's bounded schema probe, SHA-256 file revisions,
atomic publication, recovery preimages, and exact logical-revision progression.
The material contract/validation/persistence proofs and all procedural graph
tests pass 19/19. Backend-neutral compilation IR, mapped GLSL/WGSL generation,
material-instance persistence, and generic modifiers remain active.

The semantic material graph now compiles through one deterministic intermediate
evaluation into Vulkan GLSL and WebGPU WGSL rather than maintaining backend-
specific authoring formats. All eleven initial nodes emit typed expressions;
texture samples receive stable scene-ABI set-2 texture/sampler pairs with an
explicit seven-texture bound, and both sources carry node/port-to-generated-line
maps for diagnostic attribution. The generated portable surface contains base
color, metallic, roughness, normal, and emissive channels and feeds a shared
lighting closure. Identical graphs produce identical source and SHA-256 content
identity. The GLSL proof physically compiles the generated fragment source to
nonempty SPIR-V; the WGSL source uses matching bindings and WebGPU entry syntax.
Material instances now persist independently under `Materials/Instances`, bind
to an exact material-graph SHA-256 revision, expose only graph-declared typed and
ranged overrides, publish atomically with logical revisions and recovery, and
resolve onto cloned node parameters without mutating the base graph. Unknown
overrides and stale graph revisions publish nothing. The focused material suite
passes 7/7. Ordered generic modifier descriptors/evaluation are active next;
physical WebGPU material rendering and richer PBR integration remain required
before the material path is considered visually complete.

The ordered modifier foundation is now implemented over the same editable mesh
kernel. Versioned descriptors initially expose transform, triangulate, and
region-extrude with generic parameters, possible-change masks, deterministic
flags, and explicit unknown-attribute preservation/loss policy. Persistent
stacks bind to an exact source-mesh SHA-256 revision and carry stable ordered
modifier identities, enable state, and typed parameters. Evaluation is
immutable, budgeted, cancellation-aware, and content-addressed per step; an
unchanged repeat hits both cached steps, while editing only a downstream
modifier retains the upstream hit and reports one exact invalidation. Named
selection sets or complete generic point/face domains drive operations without
genre assumptions. Atomic 1-256 operation patches add, remove, reorder,
configure, enable/disable, or retarget a source in memory, validate the complete
candidate, capture a transaction preimage, and publish only against the exact
stack file revision. Preview evaluation writes no target. Bake verifies the
source dependency revision, evaluates the stack, then publishes through the
ordinary strict mesh store with target revision protection and transaction
evidence. The five modifier evaluation/persistence/patch/preview/bake proofs
pass. A bounded command/MCP surface and the broader topology/UV/boolean/
subdivision/remesh/optimization inventory remain active.

Material graphs and modifier stacks are now portable agent surfaces rather than
library-only capabilities. Fifteen default-registry commands cover semantic
material-node and modifier-type discovery; material graph create, bounded
inspect, atomic patch, validate, and dual-backend compile; exact-revision
material instance create/inspect; and modifier stack create, bounded inspect,
atomic patch, preview evaluate, and bake. Material patches apply 1-256 typed
add/remove/configure/link/output/exposure operations in memory, reject stale or
invalid candidates without changing bytes, and publish one logical revision
with transaction preimage. Compilation omits shader sources by default and can
return at most 65,536 characters when explicitly requested, while always
returning bounded resource/source-map/diagnostic evidence. Modifier evaluation
returns topology and bounds rather than raw buffers. All fifteen tools are MCP-
classified under `modeling`, with inspection/discovery/validation/compilation/
preview tools recommended. A JSON-only registry proof creates, revision-patches,
compiles, and instances a material without direct document editing; the focused
command and atomic material-patch proofs pass 4/4. Broader modifier inventory
and visual backend acceptance remain active.

The broader mesh inventory has its first strict shading-data slice. The generic
operation executor now generates finite Newell face normals into named corner-
domain Float3 attributes and projects selected face corners onto XY/XZ/YZ into
named corner-domain Float2 UV attributes with explicit scale/offset. Both
operations preserve topology and unrelated attributes, replace only compatible
destination attributes, report exact modified stable face/corner IDs and
affected bounds, and fail on incompatible attributes, invalid axes, nonfinite
coordinates, or degenerate normals. Corner-domain storage preserves seams for
subsequent tangent generation and compilation. Two new recommended commands,
`rekall.mesh.operation_types.search` and `.inspect`, expose the executor's
canonical domains, change masks, typed defaults, and descriptions so agents do
not guess operation IDs or parameters. The affected operation/command suite
passes 17/17. Subdivision, remesh, optimization, and boolean operations remain
active and will not be advertised until each has strict topology/provenance
proofs.

Centroid-fan face subdivision is now a shared strict mesh operation, procedural
node, and ordered modifier. Each selected polygon derives one centroid point,
one radial edge per source corner, and one triangle per boundary edge while
retaining the source face ID for the first output and stable original corner IDs
once each. The result reports created point/edge/face/corner IDs and source-face
to output-face provenance. Point and corner numeric attributes interpolate at
the centroid according to interpolation policy; boundary corner values and face
attributes copy from exact sources; new edge attributes use declared/type
defaults; face selections expand through provenance. A UV-bearing quad proof
produces 5 points, 8 edges, 4 faces, and 12 corners, including four 0.5/0.5
centroid UV samples, and passes strict topology validation. The procedural node
executes the same operation and produces the same 5-point/4-face shape; modifier
discovery exposes the same capability. The affected graph/modifier/subdivision
selection passes 15/15. This is linear centroid subdivision, not yet a smooth
Catmull-Clark claim; smooth subdivision, remesh, optimization, and booleans
remain active.

Earlier merged baseline: the engine-owned graphics/agent tranche is merged to
`master` at `7f71694`, green at 1,111/1,111 engine tests, 11/11 Studio tests,
and a zero-warning, zero-error Release solution build. The Studio ergonomics
tranche now adds explicit Edit, Simulate, and external production-Player modes,
a persistent non-destructive in-viewport runtime session, a default-on live
preview toggle, mode-aware authoring command guards, and a shared Segoe UI dark
control theme. Automatic Edit rendering now remains entirely in memory, obeys
the Live toggle, and leaves persistent proof PNGs to the explicit Capture action.
Mode transitions are serialized and cancellation-safe; preview reset is
failure-atomic, runtime/render work executes off the WPF dispatcher, preview
ownership is released before Play, and natural Player exit is reconciled back
to Edit by the Studio cadence. Candidate preview state is rendered successfully
before it atomically replaces the previous session, and repeated close/dispose
requests share the same in-progress shutdown task. The complete Studio suite passes 19/19, the
engine suite passes 1,111/1,111, and both Debug and Release solution builds
complete with zero warnings/errors. Real Windows UI inspection proved Edit capture,
continuous simulation from frame 0 through frame 120, Stop/reset to frame 0,
and unchanged authored scene state. Independent review reports no remaining
Critical or Important findings. The tranche is fast-forwarded onto `master` at
`1dccb1c`; the merged checkout passes 19/19 Studio tests. The final progress
checkpoint and `master` push are the remaining integration steps.
Pong remains the active game acceptance; this bounded
Studio tranche precedes Galaga so that Galaga can exercise a credible editor.

The first installed-Studio Pong run reached mechanical package, relocation,
and audit success, but independent frame review rejected it: the court rendered
as broad bands, the ball/paddles were not recognizably composed, and the
requested score/control UI was absent. Source inspection found a generic
authoring-contract defect: `rekall.geometry.plane` is an XZ plane with a +Y
normal, but its agent-facing schema did not disclose that orientation. The
engine now documents the convention and emits
`REKALL_VIEWPORT_PLANE_EDGE_ON_TO_CAMERA` when a world plane is nearly edge-on
to the active camera. Strict task evidence now also derives requested UI,
scoring, reset/serve, collision, and two-player semantic-input obligations from
the ordinary user request and refuses completion without focused passing
captures/runtime transitions. The focused plane/schema and task-evidence tests
are green, and the complete Release solution builds with zero warnings/errors.
Pong remains active until its repaired Studio output is independently playable
and recognizable; the failed V1 package is diagnostic evidence only.

The user correctly required engine-owned graphics verification before another
LLM acceptance attempt. The in-progress Pong repair was stopped after preserving
its module, scene, transactions, and diagnostic captures. A direct regression
fixture reproduced the missing-world failure without an LLM: the software
renderer multiplied primitive scale twice, used the largest axis as every
screen axis, and had name/order-dependent opaque composition. Wide thin walls
therefore became screen-covering bands and erased foreground geometry. The
software path now derives pixels-per-world-unit from the active perspective or
orthographic camera, applies primitive scale per axis exactly once, and draws
same-sort opaque content far-to-near before UI. A fresh Pong capture immediately
made the backdrop, boundaries, center line, ball, and both paddles visible.

The same audit found two UI/backend defects. Runtime capture now emits
`REKALL_VIEWPORT_UI_LARGE_COVERAGE` when UI layout bounds cover at least 35% of
a viewport that also contains world renderables, with reference-space guidance;
the built-in UI Canvas schema now explicitly explains viewport scaling and why
Width=100 on a 100-wide reference canvas fills the full window. Native Vulkan
scene capture previously reported UI renderables but omitted them from the PNG;
it now composites the exact runtime software UI overlay into hardware capture
and recomputes output metrics. Direct RTX 5090 captures prove Vulkan world+HUD
output and expose the same 44% excessive-HUD warning as software. All 315
rendering-namespace tests pass, including the new perspective backdrop,
anisotropic primitive, UI coverage, and Vulkan UI composition regressions; the
complete Release solution builds with zero warnings/errors.

The preserved Pong repair exposed two more generic contracts before acceptance.
First, a pruned long-running agent conversation inserted its durable tool ledger
as a new system message after the original user request and could retain orphaned
tool results; Qwen 3.8 then failed with `no user query found in messages`. The
ledger is now a user-role continuation and pruning starts only at a complete
message boundary; blank-system sessions preserve only the initial user task and
cannot retain an unresolved assistant tool call. Second, UI text used only a
five-pixel bitmap alphabet. The Windows renderer now defaults to antialiased
Segoe UI and exposes generic `FontFamily`, `FontWeight`, `FontStyle`, and imported
`FontAssetId` contracts. It supports distinct installed families, validates
imported TTF/OTF files, and uses the bitmap alphabet only as a deterministic
fallback for missing, corrupt, unsupported, or uninstalled fonts. Rasterization
has hard text, pixel, dimension, entry, and 16 MiB total-cache limits covering
both pixels and bounded canonical keys, with overflow-safe truncation and tight
measured glyph surfaces. Rendering and
clipping/overlap diagnostics share the same effective viewport, inherited, and
element-own clip rectangle plus those exact metrics. Module indexing skips unrelated
runtime assemblies but remains strict for assemblies that reference the Rekall
module contract, so project module load failures are not silently hidden. A
fresh reviewed 960x540 Pong capture at
`Artifacts/PongStudioV1/Artifacts/ModernFontCaptureBounded/Main_runtime_001.png`
is informative with 73 distinct colors, no culling, asset issues, fallbacks, or
runtime observations, and preserves the full court with compact modern score,
serve, and control text. The font validation/cache/measurement/asset-count,
message-pruning, module-filtering, locked player publish, package publish, and
bounded MCP schema regressions are included in the 1,111-test green run. The
next action is to checkpoint/merge this tranche, then resume Pong through Studio
and require fresh runtime behavior evidence, package relocation, audit, and
independent play/visual inspection.

## Product objective

Build Rekall AGE into a proprietary, production-quality, AI-first C# game
engine. It must let professional developers and AI agents author arbitrary
games through generic, inspectable runtime primitives, SDK helpers, structured
diagnostics, and portable MCP/tool contracts. The engine supplies capability;
agents author the game.

## Stable priority order

1. Generic runtime and rendering primitives.
2. Agent SDK, MCP/tool contracts, diagnostics, and bounded repair loops.
3. Closed-loop authoring proof using packaged, installed binaries.
4. Reliability, performance, security, packaging, and release hardening.
5. Rekall.Age.Studio as a professional consumer of the proven contracts.

Studio is important, but it does not define or reorder the engine foundation.
The current ergonomics tranche is deliberately bounded to generic viewport and
mode contracts needed to author and verify Galaga; it adds no genre-specific
behavior.

## Acceptance benchmark queue

1. Prove a complete playable Pong game through the generic portable authoring
   contracts. Require agent-owned delta-time gameplay, semantic controls,
   scoring, reset/serve transitions, executable assertions for both players and
   scoring, independent visual inspection, clean validation, a portable package,
   relocation, and consolidated audit.
2. Prove a Galaga-class game entirely through Studio. Require player movement
   and firing, multiple enemies, projectiles, collisions, score/lives or an
   equivalent complete loop, executable transition assertions, informative
   visual evidence, packaging, relocation, and audit without genre-specific
   engine built-ins.
3. Resume the Studio end-to-end visual-effects class: acquire a rights-compatible
   nature image from the internet with source/provenance recorded, import it
   through generic asset contracts, present it across the full player window,
   and author moving
   raindrops-on-glass through generic material, shader, buffer, sampler, UV,
   transparency/blending, and engine-time primitives. Acceptance requires at
   least two temporally distinct captured frames proving real animation, plus
   validation, packaging, and package audit. The engine must not contain a
   nature-scene or raindrop-specific built-in; the Ollama agent authors the
   effect from inspectable general-purpose capabilities. Author this game
   entirely through Rekall.Age.Studio so prompt entry, tool execution, project
   mutation, play, capture, packaging, and audit prove the UI as a real consumer
   of the same portable authoring contracts.
   After this base remote-image path passes, generalize it into a provider-neutral
   asset catalog with Poly Haven first (official API; CC0 HDRIs, PBR textures,
   and 3D models), then ambientCG, and approved manifest-based Kenney/Quaternius
   packs. Preserve variant/dependency manifests, hashes, source/license/author
   provenance, and generated package credits; do not scrape providers without a
   supported API or permission. Mixed-license catalogs remain opt-in and
   policy-filtered.
4. Broaden the 3D acceptance suite through both Studio and portable MCP
   contracts.
5. Platform track after the desktop authoring loop is reliable: publish games
   as static browser deployments through .NET WebAssembly and a WebGPU renderer
   backend, with ahead-of-time compiled game-authored modules and browser-native
   input, audio, storage, and networking adapters. WebGL2 is a later bounded
   compatibility tier, not the primary rendering contract. Preserve the same
   generic world/runtime and authoring ABI; do not fork game semantics by
   platform.
6. Advanced world-modelling track after the active WebGPU tranche and remaining
   Godot capability audit: clone and study Blender's relevant modelling,
   geometry, modifier, material, UV, validation, undo, and interchange systems,
   together with useful Blender MCP authoring patterns. Implement an AGE-native,
   100% C# modelling system rather than embedding Blender or exposing native
   implementation objects. Users and agents must be able to author, inspect,
   revise, validate, and reuse complex game-ready geometry and materials through
   the same generic C#, Studio, CLI, and MCP contracts. Acceptance requires
   increasingly complex editable worlds used in real playable runtime proofs;
   primitive generation or opaque imported meshes alone is insufficient.

## Verified status

- The rebuilt `0.1.0-preview.1` Windows distribution passes the locked Release
  gate with zero compiler warnings/errors, 1,091/1,091 engine tests and 11/11
  Studio tests in each of two independent passes, followed by the complete
  installed-product matrix. The refined viewport analyzer preserves the 98.5%
  generic blocking threshold while emitting
  `REKALL_VIEWPORT_LOW_VISUAL_COVERAGE` at 95%; Studio treats that advisory as
  failed task-specific visual evidence without invalidating legitimate sparse
  package proofs. The focused rendering/package/gauntlet regressions pass
  13/13 and Studio view-model tests pass 11/11.

- A second clean installed-Studio Rain Glass diagnostic used real
  `qwen3.8:27b` with no configured reasoning, turn, output, or deadline limit.
  It completed 34 turns and 35 tool calls, compiled agent-owned runtime and
  playable modules, passed three runtime assertions, validated, packaged, and
  audited. Independent evidence still rejected the deliverable: the model
  never searched for or imported the explicitly requested internet image and
  rendered only three tiny droplets against a dark clear color. Studio reported
  `Succeeded=false`, four renderables, and `visual repair required`. AGE now
  derives an explicit delivery checklist from the authoritative user request
  and, in strict sessions, refuses completion without direct evidence for
  requested remote-image search/import and license provenance, authored/
  validated/assigned custom shaders, asset-backed full-window captures, and
  distinct-time frames. The prompt explicitly states that moving geometry is
  not a substitute for a requested custom shader. The affected generic agent,
  SDK, world-mutation, viewport, and Studio selections pass 80/80 and 11/11.
  This remains diagnostic evidence; Pong and Galaga now take priority before a
  third Rain Glass run.

- The corrected installed-Studio Rain Glass run used only the ordinary request
  recorded above with real `qwen3.8:27b`, no configured turn/output/deadline
  limit, native reasoning, and task-specific completion required. Across 33
  turns and 51 tool calls it searched Openverse, selected and imported a real
  1024x683 CC BY 2.0 Flickr landscape with exact source, creator, license URL,
  SHA-256, and byte-count provenance, authored a ten-entity scene, scaffolded
  and built a game-owned RainGlass runtime module, then attempted executable
  semantic-input proof. The run failed honestly. Its first fully shaped failed
  runtime assertion triggered `checked(int.MaxValue + repairTurns)` because the
  unlimited turn sentinel still flowed through finite reserve arithmetic. Its
  2D drops remained still because the SDK exposed only `WithPosition3D`, while
  partial blueprint repair retained both lower-camel and Pascal-case component
  properties. Finally, independent 960x540 capture proved the asset loaded but
  occupied only a tiny area: 98.0% of pixels were the camera clear color even
  though the prior 98.5% warning threshold called the frame informative.
  Test-first repairs now skip finite reserve arithmetic for unbounded runs;
  expose `WithPosition2D`, `WithRotation2D`, and `WithScale2D` through the
  compiled SDK and agent contract; make typed component reads/writes and scene
  merges case-insensitive without duplicate keys; and emit
  `REKALL_VIEWPORT_LOW_VISUAL_COVERAGE` for frames dominated 95% or more by one
  color. Studio rejects that advisory for task-specific visual proof, while the
  generic package blocker remains at 98.5% to preserve valid sparse games. The
  affected language-agent, SDK, world-mutation, runtime-helper, and viewport
  selections pass. This failed run remains diagnostic evidence, not acceptance.

- User-facing Studio and project-agent sessions now leave maximum turns,
  generated output, per-turn duration, and Ollama reasoning mode unspecified by
  default. Explicit bounds remain available for deterministic tests and
  automation, and Studio retains its user-operated Cancel command. Regressions
  first failed under the former 24/36-turn, 1,024-token, two-minute, and `low`
  reasoning defaults; the changed surface now passes 111/111 engine and 9/9
  Studio tests. Before the final reasoning-default removal, the locked Release
  workflow built with zero warnings/errors, passed 1,087/1,087 engine and 9/9
  Studio tests twice, and passed the complete installed-distribution matrix.
  A diagnostic installed-Studio run pursued only `Create a nature scene viewed
  through moving raindrops on glass.` with real `qwen3.8:27b`, no turn, output,
  or turn-duration limit, and task-specific completion required. That wording
  incorrectly permitted a procedural scene: Qwen authored 30 coherent entities
  but no remote asset, then aimed its +Z-facing camera away from the negative-Z
  landscape. Independent capture reported zero asset-backed renderables and
  `REKALL_VIEWPORT_DOMINATED_BY_ONE_COLOR`. AGE now exposes its right-handed
  +Z-forward camera convention through Transform3D/Camera3D schemas and the
  embedded contract, and task-specific Studio automation requires a visually
  informative frame. The corrected clean-project acceptance request is:
  `Create a game that uses a suitable openly licensed nature image from the
  internet as a full-window background, with moving raindrops on glass over
  it.` The final package rebuild and repeated acceptance will also include the
  newly unspecified reasoning mode.

- The first `Rain Glass Reverie` run was performed through the installed Studio
  with Qwen 3.8 27B and stopped honestly at `turn_limit`: Qwen found only the
  local-file asset importer, passed it the requested HTTPS URL, received `Asset
  source file was not found`, and created no game files. AGE now has a generic
  `rekall.asset.import_remote` command with HTTPS-only public-address policy,
  per-hop redirect/DNS revalidation and connection pinning, 32 MiB/30-second
  limits, SHA-256 verification, project-confined staging/cleanup, catalogued
  creator/license/source provenance, stable diagnostics, optional operator
  contact, and bounded `Retry-After` handling. Focused asset/MCP coverage passed
  56/56 before the protocol/task-contract expansion; the expanded selection is
  currently green at 31/31 engine and 1/1 Studio tests.
- The first rebuilt installed-Studio retry proved the new command was naturally
  discoverable and called it on turn 2, but Wikimedia returned a real HTTP 429.
  Qwen did not fabricate a replacement and made no game mutation; the attempt
  was deliberately cancelled and preserved. AGE now distinguishes host rate
  limits, respects bounded retry instructions, and tells agents to wait or
  select another licensed source rather than bypass a provider.
- A user review correctly rejected the long internal acceptance specification
  in Studio's visible task field. The field now starts empty with an ordinary-
  language watermark. `RekallAgeAgentTaskComposer` preserves the user's short
  request once as authoritative intent, while the embedded engine contract owns
  tool discovery, implementation, rights/provenance, validation, runtime
  evidence, revision, packaging, and audit requirements without inventing
  unrelated gameplay. New `rekall.asset.search_remote_images` provides bounded
  agent-selectable Openverse results with URL, landing page, creator,
  attribution, and license metadata; AGE exposes evidence while the agent—not
  the engine—chooses content. A live anonymous Openverse probe returned 200 and
  a CC BY forest-lake result; its direct Flickr image host returned 200 without
  a proxy. The next Studio rerun must contain only the corrected ordinary image-
  background request recorded above.

- Real `qwen3.8:27b` repaired and delivered the preserved benchmark-48 Lumen
  Vault instead of replacing it. Qwen separated the three HUD rows, balanced
  the arena across X and Z, reduced the existing player/seal scales, and
  corrected the existing camera to `(0,24,18)` with pitch `53`, yaw `180` from
  direct runtime evidence. The player-facing 1280x720 frame shows the player
  and all three seals distinctly with zero clipping/overlap/layout warnings;
  its 13,791-byte PNG has SHA-256
  `110C402A258D4EA97B28636DB5E3B54113843971340CA34612E81A0C8702F8CC`.
  Representative semantic horizontal and vertical input then ran 153 frames
  and passed six strict assertions: exact agent component existence, X and Z
  movement, collected-state change, `Collected == 3`, and `Complete == true`.
  Project validation reported zero issues. The graphics package ran, captured,
  passed all eight audit checks including layout integrity, and relocated with
  121 verified files. Its 45,419,287-byte archive has SHA-256
  `E3FF107748583A4EAA73F587CF459BD090F39D2E34AF88E320F44FC75831BEC4`.
- The evidence-driven repair exposed and closed generic authoring/runtime
  defects rather than hard-coding Lumen behavior. Progressive discovery keeps
  the bounded direct tool set while allowing every command to be rediscovered
  and executed through the permanent gateway; checkpoint policy evaluates the
  gateway's real target and its bounded object-form or encoded arguments.
  Encoded destructive replacement cannot evade checkpoint policy, oversized
  encoded calls fail closed in both MCP and Studio, and gateway project/scene
  defaults remain inside the open-project security boundary. Read-only source
  inspection no longer counts as runtime authoring; existing direct runtime-
  system implementations satisfy revision sessions from turn zero, while
  comments, strings, generic arguments, constraints, and reparse-point module
  roots do not. Stale SDK build errors return the exact install action. Provider
  turns now have a hard wall-time bound even when a client ignores cancellation,
  while provider self-cancellation propagates as a real failure. Project/Studio
  Ollama sessions use measured low-reasoning 1,024-token two-minute defaults
  while the provider-neutral agent retains broader defaults. Viewport diagnostics
  detect severe text clipping and sibling text overlap, carry their evidence
  through package capture, and make package layout audit fail closed. Finally,
  the software proof renderer now projects world meshes through the authored
  non-default `Camera3D` position, rotation, projection, FOV, viewport, and
  near/far depth range in perspective and orthographic modes
  instead of placing meshes from world X/Y alone; the regression failed before
  the fix and the surrounding rendering selection passed 72/72.
- The final changed-surface selection passed 123/123. Independent review found
  no remaining Critical or Important issues and assessed the milestone ready
  to merge. The locked Release build completed with zero warnings/errors. Two
  independent passes each completed 1,056/1,056 engine and 7/7 Studio tests.
  The assembled installed
  distribution passed its complete acceptance matrix, including installed
  Studio agent authoring, trust tamper rejection, package audit/relocation,
  recovery, animation, morphing, runtime soak, atomic persistence, and document
  revision/recovery. Its 1,186-payload archive is 201,644,796 bytes with SHA-256
  `8D685EAD8229B2C6DD851D20A5B496901702643B94D5FCEC714704784A3A5116`.
  The next acceptance begins with a new project and performs the entire
  raindrops-on-glass authoring loop through Rekall.Age.Studio.

- Clean installed benchmark 48 completed the unchanged Lumen Vault request with
  real `qwen3.8:27b`: 88 turns, 102 tool executions, and 17 failed calls that
  were repaired in-session. Qwen authored a coherent 12-entity 3D scene and a
  trusted 6,632-byte game-owned runtime system. A 150-frame semantic-input run
  proved delta-time player motion, collection of all three seals, progress state,
  and completion with six passing runtime assertions. The resulting package
  ran, relocated, and audited successfully; its archive is 38,034,938 bytes with
  SHA-256 `8B6658AE3A06396ED4E1D783EA08D5548A1F4517D1150A87176883F7587FBF24`.
  Independent inspection then correctly exposed that mechanical acceptance was
  too weak: the side-on composition overlapped its primary renderables, the UI
  title was clipped, and the spatial layout was overwhelmingly X-dominant.
  AGE now measures visible UI text area, emits stable severe-clipping diagnostics
  with entity-specific repair hints, carries complete layout diagnostics through
  package capture, and blocks package audit on severe element/text clipping while
  preserving advisory composition warnings and hints. The agent also now detects
  provider output-limit finishes, requests one immediate tool action at reduced
  reasoning, and restores the requested reasoning level after recovery. Focused
  regressions passed 15/15. The locked Release build had zero warnings/errors;
  two independent passes completed 1,032/1,032 engine and 7/7 Studio tests. The
  rebuilt installed distribution passed its complete acceptance matrix, including
  the installed Studio agent proof and the new `layout-integrity` package check.
  Its 1,186-payload archive is 201,624,030 bytes with SHA-256
  `417E706850E9D76AA250E61BC182A690334AD0683B6D8D7986C540E5E696C62B`.
- Clean real-Qwen benchmark 47 proved that context capacity and per-turn output
  budget are separate production controls. Qwen 3.8 authored the complete
  11-entity Lumen Vault scene and a freshly trusted 7,877-byte runtime-system
  module with semantic movement/reset actions, delta-time motion, seal contact,
  progress/completion state, and HUD updates. After the build, one model turn
  generated 42,945 tokens for 4m22s, filled the entire 65,535-token allocation,
  was reported by Ollama as `truncated = 1`, and produced no useful tool
  mutation; the following turn began repeating the same failure. The disposable
  benchmark process was stopped while its authored project was preserved.
  AGE now exposes provider-neutral `MaxOutputTokens`, embedded agent turns
  default to 8,192, and the Ollama adapter emits `options.num_predict`. This
  bounds one reasoning/action step without reducing the 65,536-token retained
  context, turn count, repair reserves, or total game scope. Caller values are
  clamped to 512..65,536 tokens. The regressions failed first, then the focused
  language-model/Ollama selection passed 45/45. Ollama documents
  `num_predict` as the maximum generated-token count and its default as `-1`
  (unbounded), matching the measured failure.
  The locked Release build completed in 9.48 seconds with zero warnings/errors;
  two independent passes completed 1,030/1,030 engine and 7/7 Studio tests.
  The complete installed distribution matrix passed, including generic
  authoring/package proof, trust tamper rejection, relocation/audit, runtime
  subsystems, diagnostics recovery, morph animation, atomic persistence,
  document revision/recovery, and Studio automation. Its 600-frame soak
  simulated exactly 10 seconds at 4,383.5 FPS with 711,288 retained bytes and
  all nine checks. The 1,186-payload archive is 201,620,673 bytes with SHA-256
  `07D8213411D538FB5121A60D4C7E6C6787C0856F8B6DAD2173171C43811D8050`.
  Clean installed benchmark 48 is next with the unchanged Lumen Vault request,
  real Qwen 3.8, the 65,536-token context, and the new 8,192-token turn bound.
- Generic command dispatch now rejects unknown top-level arguments before a
  command can execute or mutate state with stable code
  `REKALL_COMMAND_ARGUMENT_UNKNOWN`, the exact unknown and allowed fields, the
  bounded command contract, and native structured-value repair guidance.
  Supported aliases are replaced by their canonical fields during
  normalization, so strict binding does not break documented compatibility.
  Malformed runtime-inspection calls reach typed binding before checkpoint
  policy can hide the defect. Missing-required-field errors now also project C#
  constructor parameters through the command JSON naming policy, returning
  exact copyable names such as `projectName` rather than `ProjectName`. The
  new casing regression failed first and all 12 dispatcher tests then passed.
  Focused dispatcher/agent coverage passed 51/51.
  AGE's provider-neutral language-model request now carries a bounded context
  window and embedded agent sessions default to 65,536 tokens; the Ollama
  adapter emits this as `options.num_ctx`. This avoids relying on Ollama's
  32K automatic default for a 32-GiB GPU during long-horizon game authoring and
  follows Ollama's recommendation of at least 64K for agent/coding workloads.
  The request-propagation regressions failed first, then the combined Ollama
  client/language-agent selection passed 43/43.
  The production gate also exposed a real scheduler-contention boundary: the
  prior one-second restricted-module request deadline rejected a valid
  400-millisecond module during the full suite while Ollama downloaded a large
  model. A 1.2-second valid request reliably reproduced the boundary before the
  deadline was raised to two seconds; three consecutive focused runs now allow
  that request while still terminating a five-second hung module. The locked
  build completed with zero warnings/errors; both independent passes completed
  1,028/1,028 engine and 7/7 Studio tests, and the complete installed matrix
  passed under the continuing download load. The 1,186-payload archive is
  201,619,976 bytes with SHA-256
  `B960C61BF2F83019E8350CEFC201290F047A72D3D6C4664E4525EE8D799C6B42`.
  Qwen 3.8 benchmark 47 is next after the model pull completes.
- Browser game publishing is architecturally viable but not implemented. The
  managed world/runtime and generic authoring contracts are the reusable base;
  the current native Vulkan/SPIR-V renderer, Windows AppContainer module host,
  and desktop player cannot run in a browser. The production direction is a
  WebGPU renderer and browser host over .NET WebAssembly, ahead-of-time module
  compilation rather than in-browser dynamic compilation/loading, browser
  capability adapters, and automated multi-browser gameplay proof. This track
  remains behind closing the installed arbitrary-game authoring benchmark so it
  reuses a proven runtime ABI instead of destabilizing the core prematurely.
- Clean installed real-Qwen benchmark 46 authored a nonblank 11-renderable
  scene with coherent player and seal entities, semantic input, camera, light,
  floor, colliders, and agent-owned state. It nevertheless exhausted 64 turns
  with the runtime module still at its scaffold and no package. The decisive
  contract defect was silent argument dropping: repeated runtime inspections
  supplied plausible `inputFrames` and `frameCount` fields instead of `inputs`
  and `frames`, plus JSON-encoded assertion strings. AGE ignored the unknown
  names and reported only missing checkpoint coverage, so Qwen spent dozens of
  turns permuting an ineffective shape instead of receiving an exact binding
  error. Evidence SHA-256 is
  `EC7DFD8B23F49C4E4081D835CA678FD820F0D0743BD9D0D318AA85BF1D01CED5`.
  The next implementation item is fail-closed unknown argument validation with
  exact allowed names and native structured-value repair guidance across the
  generic command dispatcher.
- Module builds now reject stale immutable-world lineage with
  `REKALL_MODULE_IMMUTABLE_WORLD_STALE_BASE` and the exact continuation variable,
  and reject mutation of an outer world inside an entity-update callback with
  `REKALL_MODULE_IMMUTABLE_WORLD_NESTED_MUTATION` and a sequential-repair rule.
  The bounded preflight masks comments and strings, preserves valid chained
  mutation and read-only callback queries, reports source lines, and issues no
  trusted build receipt on rejection. The embedded agent contract and compiled
  SDK inspection expose the same rule and copyable pattern. The exact installed
  Benchmark 45 source now fails before compilation with the stale-lineage
  diagnostic. Focused build/agent/SDK coverage passed 18/18. The locked Release
  build completed with zero warnings/errors; both independent passes completed
  1,026/1,026 engine and 7/7 Studio tests, and the complete installed matrix
  passed. The 1,186-payload archive is 201,614,635 bytes with SHA-256
  `A390D3B8ACBA938C98A43B75A1DC1FBEE7CD147FB17FEB49C839E8FE7A15F36E`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  Clean installed benchmark 46 is next.
- Clean installed real-Qwen benchmark 45 confirms that the discarded-mutation
  preflight and destructive checkpoint guard both change behavior, then exposes
  two subtler immutable-world hazards. Qwen authored and compiled semantic
  delta-time movement, seal contact/progress/completion, and reset behavior. It
  assigned mutation results, but later assigned `updatedWorld` from stale
  `world`, silently discarding earlier movement. It also mutated the outer
  immutable world from inside an entity-update callback and then overwrote that
  nested result when the callback operation returned. Duplicate `PlayerOrb`
  names obscured checkpoint identity; late repair deleted the coherent player
  and retained its sparse shell. AGE correctly blocked delivery after 64 tools:
  the final scene had two renderables, no camera, no package, and evidence
  SHA-256
  `711BF15F71B83590409BAF1323AED0190A6D4018727811BB707D793F9E8A08B4`.
  The next implementation item is fail-closed, exact-repair module source
  diagnostics for stale immutable-world lineage and nested mutation before a
  trusted build receipt is issued.
- Clean installed real-Qwen benchmark 44 confirms the logical-entity contract
  works, then exposes two deeper generic hazards. Qwen initially authored eight
  coherent entities: each seal held its geometry, transform, and trigger; the
  floor held collider, geometry, and transform; and the player held state,
  rigid body, collider, and transform. It authored and compiled a substantial
  movement, collection, progress/completion, and reset module. The final source,
  however, discarded the immutable results of bare
  `world.UpdateEntitiesWithComponent(...)` calls, making movement and collection
  no-ops. Before a qualifying checkpoint, it then applied `clearExisting=true`
  with one player and deleted the valid arena. AGE correctly blocked delivery;
  the run ended after 76 tools with no package, two renderables, a missing-camera
  blocker, and evidence SHA-256
  `00AD67FA477FE7F807B8A1379F328859886BFBB08502B4CC72490FDBC4FD9FCD`.
- Module builds now reject a bare discarded immutable-world mutation such as
  `world.UpdateEntitiesWithComponent(...)` with
  `REKALL_MODULE_IMMUTABLE_MUTATION_DISCARDED`, source line evidence, and the
  exact `world = world.Update...` repair before issuing trusted build receipts.
  The embedded contract states the same immutable-world rule. While the first
  executable checkpoint is pending, agent policy now blocks destructive
  `clearExisting=true` scene replacement with
  `REKALL_RUNTIME_CHECKPOINT_DESTRUCTIVE_REPLACEMENT_DEFERRED`, while retaining
  safe `clearExisting=false` upserts and targeted entity/component prerequisite
  repairs. Both defects have red/green regressions. The gate also exposed a
  pre-existing cancellation-test race: its token could expire during the new
  source preflight before the fake compiler existed. Cancellation now begins
  after the injected compiler process starts, preserving deterministic proof
  that external cancellation terminates the process; it passed three focused
  runs. The final uninterrupted zero-warning/error gate passed 1,023/1,023
  engine and 7/7 Studio tests twice plus the complete installed matrix. Its
  1,186-file archive is 201,602,891 bytes with SHA-256
  `CAFA899BFBD3FFE265A489031F3C63BAA70F3812EE715D48A34A1E0C73DB6EC3`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  Clean installed benchmark 45 is next.
- Clean installed real-Qwen benchmark 43 confirms malformed-blueprint recovery
  now changes behavior, then exposes logical-entity composition and runtime
  evidence repair as the next generic blockers. After early invalid broad calls,
  Qwen switched to valid small blueprints and targeted entity/component tools,
  authored and compiled a substantial delta-time movement, seal collection,
  progress/win, and reset module, passed movement checkpoints, and produced a
  nonblank frame with seven renderables. It nevertheless split transforms,
  geometry, and state across sibling `FooTransform`/`FooMesh` entities; its
  module then treated exact `EntitiesNamed("EnergySeal")` as a prefix query for
  `EnergySeal1/2/3`. No seal transition could occur. The protected repair loop
  spent its remaining turns permuting assertion fields and temporarily attaching
  an unrelated seal component to the player, so AGE correctly blocked packaging
  after 76 turns/75 tools. Two validation warnings remained. Evidence SHA-256 is
  `88AAC1948297D6629B8B86C5853618881CECBF20F42882F7F500D105A03866BC`.
- Blueprint and embedded-agent contracts now state that transform, render,
  collider/body, input, and agent state for one logical runtime object belong on
  the same entity, never separate `FooTransform`/`FooMesh` siblings.
  `EntitiesNamed` SDK inspection and compiler recovery now explicitly state its
  case-insensitive exact-name semantics and direct numbered/grouped queries to
  `EntitiesWithComponent`, `EntitiesWithTag`, or their intersection. Three
  recent failed runtime inspections trigger a bounded circuit-breaker that
  forbids unrelated proof components and assertion weakening, supplies the
  exact component-property assertion shape, and redirects repair to the authored
  rule and scene prerequisites. The locked gate also exposed two load-sensitive
  Windows AppContainer reliability issues: a 250 ms valid-request deadline was
  too narrow under full-suite scheduling pressure, and the isolation harness
  reused one cancellation budget for sequential process-exit and stderr-drain
  phases. Restricted requests now allow one bounded second while the existing
  five-second hung-module test remains fail-closed; exit and bounded diagnostic
  collection retain independent ten-second budgets. The new jitter regression,
  hung-module termination, and 256 KiB stderr drain/bound test passed three
  consecutive focused runs. The final uninterrupted zero-warning/error gate
  passed 1,021/1,021 engine and 7/7 Studio tests twice plus the complete
  installed matrix. Its 1,186-file archive is 201,598,814 bytes with SHA-256
  `5AA2EAD44C58C6DE78811B99EAFBC232899D3F9E7E9585468BE17B1452980430`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  Clean installed benchmark 44 is next.
- Clean installed real-Qwen benchmark 42 failed before meaningful state proof
  because 18 of 23 blueprint calls used invalid or unsupported structure. The
  recurring shapes nested complete entity objects inside `components`, split
  `type` and `properties` across adjacent component objects, or passed a deeply
  malformed JSON-encoded entity tree. Qwen still compiled a substantial
  progress/reset module and passed a thin movement checkpoint, but ended with
  eight entities, three renderables, four validation issues, no camera/package,
  and no state transition after 64 turns/61 tools. Evidence SHA-256 is
  `CC0DC0A09B83B909DBBCB36F8FB7D7892540FBB608A36FD418A53801932C8477`.
- Dynamic JSON argument failures now append a bounded copy of the declared
  command contract. Blueprint validation states the exact flat topology:
  entities are siblings in the top-level `entities` array; every component is
  one object containing `type` and optional `properties`; entity fields never
  belong inside components. After three recent blueprint failures—even with
  different arguments—the agent loop injects a circuit-breaker that stops broad
  retries and directs one small flat repair or targeted `rekall.component.add`.
  Red/green dispatcher, blueprint, and agent-policy regressions pass. The locked
  zero-warning/error gate passed 1,019/1,019 engine and 7/7 Studio tests twice
  plus the complete installed matrix. Its fresh archive is 201,596,332 bytes
  with SHA-256
  `AC7114DD78552662336965086F03B6BE85BBD43B3A5986FDAE590261F4296EF1`;
  zero run-scoped build temp directories remained. Clean installed benchmark 43
  is next.
- Clean installed real-Qwen benchmark 41 confirms runtime evidence now fails
  structurally without exceptions and isolates destructive partial upserts as
  the next generic blocker. Qwen authored and compiled a coherent rules module
  with `CurrentProgress`, `GameComplete`, reset, semantic movement, contact,
  seal deactivation, and progress recomputation; built a nine-entity scene with
  five renderables; and passed several movement/component checkpoints. AGE
  correctly blocked package delivery because progress never changed. The final
  targeted component-identity repairs used partial scene blueprints that
  replaced whole entity component sets, stripping seal transforms/renderers;
  numeric delta evidence also lacked an explicitly authored initial value and
  the single input sample drove only one of twenty frames. The run ended red
  after 76 turns/71 tools with zero validation issues, no package, and evidence
  SHA-256 `5C720F38EE2D90A63AE4CCF963E6DC2D4A23705CDBDCFEF2DBA39191FD441719`.
- Non-clearing scene blueprints now perform safe partial upserts for uniquely
  matched id/name entities: component properties merge by exact component type,
  and unspecified stable id, tags, parent, visibility, lock state, transforms,
  renderers, and other components are preserved. `clearExisting=true` retains
  exact scene replacement semantics, while targeted removal commands provide
  deletion. Runtime tool and stateful-gate descriptions now state that each
  input sample drives only its corresponding frame and numeric delta assertions
  require an explicitly authored initial property. The partial-repair red/green
  regression plus all 35 language-agent tests and both blueprint behavior tests
  pass. The locked zero-warning/error gate passed 1,018/1,018 engine and 7/7
  Studio tests twice plus the complete installed matrix. Its 1,186-file archive
  is 201,594,233 bytes with SHA-256
  `29FB0FC0F44D4CCE61C29F85DAE4BE5CD504B24B6CF69E22E89A42FE5830A956`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  A clean installed rerun is next.
- Clean installed real-Qwen benchmark 40 proves the stateful gate changes
  authoring behavior but exposed runtime-inspection robustness defects. Qwen
  authored and compiled genuine `PlayerState`, `SealState`, and `HUDScore`
  contracts with delta-time semantic movement, distance-based seal contact, and
  state mutation instead of scaffold-only motion. Delivery remained blocked
  because the state assertion targeted a nonexistent `PlayerState.Active`
  property; later scene churn removed the attached state, and a final assertion
  omitted `entityName`. `rekall.runtime.inspect_scene` then threw a raw
  `NullReferenceException`; the run correctly ended red after 76 turns/76 tools,
  with zero validation issues, four renderables, no package, and evidence
  SHA-256 `5D025EC5564A6462EF9B289247CA2402A899FC493B36D6AA320AE949E4C96650`.
- Runtime assertion validation now runs before simulation and rejects blank
  `entityName`, `subject`, or `operator` fields with bounded
  `REKALL_RUNTIME_ASSERTION_FIELD_REQUIRED` errors and exact argument targets;
  failed-summary bounding is null-safe as a second line of defense. Semantic
  input validation also runs before simulation. `changed.component.property`
  now reports an absent property directly rather than the misleading value
  `false` when it is missing from both initial and final state. Both defects have
  red/green regressions and all nine runtime-inspection focused tests pass. The
  locked zero-warning/error gate passed 1,017/1,017 engine and 7/7 Studio tests
  twice plus the complete installed matrix. Its 1,186-file archive is
  201,589,933 bytes with SHA-256
  `E8B621715EFC354F97D3D9C4F9D26EB83B722FDAE32886B5E0B6A74564A50487`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  A clean installed rerun is next.
- Clean installed real-Qwen benchmark 39 demonstrates both the SDK-recovery
  improvement and the next false-positive boundary. `qwen3.5:35b` compiled its
  runtime module without a failed build, reached zero validation issues, a
  nonblank 960x540 frame with 15 renderables, a 38 MB package, and a successful
  structural package audit after 85 turns/71 tools. Evidence SHA-256 is
  `045D63DC82AF2345F141AC88E4F6D70344FD4E9C6D9E2D7BE2AD06BF399EEA2F`.
  Independent source and frame review rejects it as a gameplay pass: the rules
  module only applies scaffold `ValuePerSecond` movement and contains no seal
  contact, progress, completion/HUD, or reset logic; its runtime assertions
  prove movement and a static property only. The package audit is structurally
  correct but not sufficient task evidence.
- Stateful task evidence now derives from generic behavioral terms such as
  collection/contact, score/progress, reset, health/damage, timers, spawning,
  and destruction. Such tasks cannot unlock delivery or narrative completion
  with movement or a static property assertion: a fresh runtime inspection must
  also prove `delta.component.property` against zero or
  `changed.component.property == true` for agent-owned state. Missing proof
  receives a bounded repair reserve. The red false-pass regression and all
  35 language-agent policy tests pass. The locked zero-warning/error gate passed
  1,015/1,015 engine and 7/7 Studio tests twice plus the complete installed
  matrix. Its 1,186-file archive is 201,586,790 bytes with SHA-256
  `51E6DB118BD1682D2C5834F330A7572EE45E092E1A993F0153B95B535DEBCD04`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  A clean installed rerun is next.
- Task-specific Studio completion now rejects narrative self-audits until a
  configured completion-audit tool has succeeded, with no intervening tool call
  before the evidence-backed final response. The strict contract is explicit at
  the language-agent and project-session boundaries and enabled only for
  task-specific Studio automation, preserving generic gauntlet behavior. Red
  regressions were followed by 40/40 language/session and 7/7 Studio focused
  passes. The locked zero-warning/error gate then passed 1,013/1,013 engine and
  7/7 Studio tests twice plus the full installed-product matrix. Its 1,186-file
  archive is 201,584,539 bytes with SHA-256
  `2D5CFBFB86B53C5E7A6D92DE8114D9768D2B1546B7C3F9B3415495609F0AA985`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 38 proves the strict contract fails
  closed. Local `qwen3.5:35b` created seven renderables and a game-authored
  `LumenVaultRuntime` module but produced no package or audit; Studio returned
  `turn_limit`, a blank outer viewport, four blocking validation issues, and
  `Succeeded=False` after 59 tool calls rather than accepting an unsupported
  completion narrative. The final generic bottleneck was typed SDK repair:
  Qwen compared a `ComponentBoolean` result with an integer, passed numeric
  values to `WithComponentBoolean`, and supplied a boolean fallback to the
  double-valued `InputActionValue`. Evidence SHA-256 is
  `56ABB8FD0000CE02DFABF7889BFFE9E783EC56C4FFB2EB2A090961FF5BFDD604`.
  A new red/green build-command regression now requires bounded compiler
  recovery to show exact bool read/write and semantic reset-action forms; its
  two focused recovery tests pass. The locked zero-warning/error gate passed
  1,014/1,014 engine and 7/7 Studio tests twice plus the complete installed
  matrix. Its 1,186-file archive is 201,585,180 bytes with SHA-256
  `7104C0B6752840FD01DF37884CB6139E0CA3AF71E033F45C156A9CBF5B5E5769`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  A clean installed rerun is next.
- Failed package audits now inject bounded, task-anchored recovery: agents must
  repair the original requested entities, visuals, HUD, and behavior; generic
  `Cube/Test/Demo/Fault` filler is explicitly rejected; and scene/module changes
  require fresh validation, requested runtime assertions, package creation, and
  package audit. The audit reason remains direct tool evidence and AGE does not
  author content for the agent. All 33 language-agent tests passed. The locked
  zero-warning/error gate passed 1,012/1,012 engine and 7/7 Studio tests twice
  and the complete installed matrix. Its 1,186-file archive is 201,583,675
  bytes with SHA-256
  `6DD3F185B7A3156354955F876ACC9E47CBFE1AFE2344E40F3BE3ADCC05D96BBB`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 37 stopped before package audit recovery
  could apply. It compiled `GameplayModule` and passed two semantic movement
  assertions, but had zero renderables, no package, and no package audit. Qwen
  then emitted a completion narrative; the ordinary completion-audit prompt was
  followed only by `rekall.context.engine_status` and another narrative, which
  the agent loop incorrectly accepted as completed after 26 calls. Studio's
  outer acceptance correctly remained red and reported a blank viewport. The
  exact fail-open defect is that `completionAuditPending` conflates a requested
  narrative self-audit with successful configured audit-tool evidence.
  Task-specific Studio automation must require a successful
  `rekall.workflow.audit_playable_package` before narrative termination.
  Evidence SHA-256 is
  `31670F495385DD5318859BB1951FBB6AE378EFC6DBF6AC5443D73A2ECDBE4C66`.
- The post-runtime delivery reserve is now 16 bounded turns. It activates only
  once after a qualifying successful gameplay checkpoint, does not increase the
  general authoring budget, and retains the global 256-turn hard ceiling. A
  red/green scripted delivery regression and all 32 language-agent policy tests
  passed. The locked zero-warning/error gate passed 1,011/1,011 engine and 7/7
  Studio tests twice and the complete installed matrix. Its 1,186-file archive
  is 201,582,146 bytes with SHA-256
  `5F8DBD4B3843FD8A06949F448AE687FC01CA49CA2539B447140A47905ED5EDCC`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 36 reached package creation at call 26,
  produced an 85 MB archive, ran package audit, and continued for 49 more calls
  rather than expiring immediately. It compiled both `PlayerMovementSystem` and
  `GamePlayable`, passed semantic movement assertions, and ended with a nonblank
  960x540 Studio viewport. The run remained red at 55/75 successful calls with
  two renderables, a stale package, blocking validation, and no passing final
  audit. The audit's uninformative-frame failure did not provide an anchored
  recovery directive; Qwen added unrelated `Cube`/`CubeFaulted` validation-demo
  content and an unresolved `default` shader instead of completing the requested
  arena, orb, seals, HUD, completion, and reset behavior. The next generic agent
  correction is a bounded failed-audit recovery message that requires repairs
  against the original task and prohibits diagnostic filler content. Evidence
  SHA-256 is
  `AE7280A767B2210735484740CE5C39AF77E4A6EBB0359B9864DB35D384EE45D4`.
- Passing gameplay checkpoints now give just-in-time package ordering: when the
  task requires a package, agents are told to scaffold the generic
  `rekall.module.scaffold_playable` adapter before the final build, keep all
  world gameplay in the runtime-system module, and refresh runtime proof once
  after that build. The complete language-agent policy suite passed 31/31. The
  locked zero-warning/error gate passed 1,010/1,010 engine and 7/7 Studio tests
  twice and the complete installed matrix. Its 1,186-file archive is
  201,581,919 bytes with SHA-256
  `94A1D028DBBBC26BEE15A484DB4923ED083949BD9A2A81472D2A22895CA02B82`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 35 proved the new ordering. After a
  passing three-assertion gameplay checkpoint, Qwen immediately discovered and
  scaffolded `LumenVaultPlayableShell`, built both modules, and refreshed a
  passing runtime checkpoint. The run compiled real world gameplay with three
  semantic actions and finished with a nonblank 960x540 Studio viewport, but
  remained red at 45/75 successful calls with one renderable and no package or
  audit. The exact remaining policy limit is measured: the late checkpoint at
  turn 69 granted only eight bounded delivery turns, all consumed by adapter
  discovery/scaffolding/build, required visual-schema discovery, one correctly
  deferred validation, and the refreshed runtime proof at turn 77. Complex
  packaged tasks need a larger but still bounded post-runtime delivery reserve
  for visual repair, validation, package creation, and audit. Evidence SHA-256
  is `00AE88F02C0C5155EE28AA59A2B5D12E990E66BC95168EB5D6CCBBF14052DFEE`.
- Semantic input-map evaluation is now independent of an entity's visual
  `Visible` flag. Hidden configuration entities can project actions, while the
  map's explicit `Active` property remains the authoritative enable/disable
  switch for both visible and hidden entities. Focused input/runtime/UI coverage
  passed 17/17. The locked zero-warning/error gate passed 1,010/1,010 engine and
  7/7 Studio tests twice and the complete installed matrix. Its 1,186-file
  archive is 201,582,200 bytes with SHA-256
  `742DB0C5DAB7F1CD598616F59ACCACD12CC8D845D90C87E11A49CD3CD4203F2D`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 34 reached a passing executable gameplay
  checkpoint: its authored module compiled, semantic `move.horizontal` input
  projected two declared actions, four runtime assertions passed, and the orb's
  strict X-position delta changed under engine delta time. The viewport was
  nonblank at 960x540 with five renderables. The run remained red after 77/82
  successful calls, with no package or audit: Qwen delayed the separately
  required generic `IRekallAgePlayableModule` package-proof adapter until the
  first package attempt exposed its absence. Scaffolding/building that adapter
  correctly invalidated the earlier runtime proof, and the following package
  call was therefore deferred at the protected turn limit. The next generic
  agent-contract correction is to front-load the adapter immediately after a
  gameplay checkpoint, before the final build/inspection/package sequence.
  Evidence SHA-256 is
  `A62D352CE41BBE821B46F1E84DC9108DE940A3850769EC766F9EB6387CB65944`.
- Front-loaded runtime assertion evidence: failed inspection summaries now lead
  with bounded entity, subject, component/property, operator, expected value,
  actual value, and comparison explanation before large subsystem/entity data.
  At most eight details are included, every field and the 4,000-character total
  are bounded, overflow is counted, and all structured results remain intact.
  Runtime/CLI/agent regression coverage passed 84/84. The locked zero-warning/
  error gate passed 1,007/1,007 engine and 7/7 Studio tests twice and the
  complete installed matrix. Its 1,186-file archive is 201,581,877 bytes with
  SHA-256
  `A985056BB845D5E9ED4267058DA79447162DC7AAD5C17B3B1881B0BAF72585D7`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 33 proved the diagnostic contract: its
  failed runtime summaries exposed the exact missing `PlayerState`, missing
  component-type arguments, missing numeric state, and `delta.position3d.x`
  actual value `0`. Qwen used those facts to reduce four failed assertions to
  one and used the populated compiler recovery to return to successful builds.
  The run remained red at 55/75 successful calls, with one renderable, no
  nonblank proof, and no package; its final source repair was not rebuilt or
  retested before the protected limit. The next generic runtime defect is exact:
  its valid `Rekall.InputActionMap` lived on an intentionally non-rendered
  `visible:false` configuration entity, but `runtime.input.actions` discarded
  the whole entity and reported `inputActionCount: 0`. Input maps already have
  an explicit `Active` property, so visual visibility must not silently disable
  semantic controls. Evidence SHA-256 is
  `D1B03B427B90A2E7DF61D97AEA2943C98A40EA587518FC1CEE4EB7BDD7C966AB`.
- Runtime SDK compiler recovery: failed runtime-module builds now put exact
  immutable entity/transform/component/update patterns before verbose compiler
  diagnostics and return populated SDK-inspection plus source-list suggestions.
  AGE does not rewrite or author the game source; ordinary compiler errors and
  timeout/cancellation semantics remain authoritative. Focused build/scaffold/
  SDK coverage passed 14/14. The locked zero-warning/error gate passed
  1,006/1,006 engine and 7/7 Studio tests twice and the complete installed
  matrix. Its 1,186-file archive is 201,577,852 bytes with SHA-256
  `D3E00E027AD3FBDE71C553011B21FCD643DACB9E477B9EF70AC2FA55ADDEAE6B`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 32 proved compiler recovery: the authored
  `LumenVaultRules` module compiled on its first build at tool call 19, later
  builds recovered after edits, and 13 real runtime inspections executed. One
  checkpoint passed, the final viewport was nonblank at 960x540 with six
  renderables, and the protected run expanded to 73 tool calls. It remained red
  with no package after later scene/module mutations invalidated the proof and
  repeated runtime assertions failed. The generic diagnostic defect is now
  measured: serialized runtime results put large subsystem/entity state before
  `AssertionResults`, so the bounded LLM tool output can omit the failed
  subject's exact actual value even though the command promises bounded repair
  evidence. The next tranche puts compact failed assertion summaries and actual
  values at the beginning of the command result while retaining full structured
  inspection data. Evidence SHA-256 is
  `D6F3A52B9460823A3E05AFFAD34900D05D7448BDC68E26E89D3787B20AACD413`.
- Runtime checkpoint component identity now matches generic module authoring:
  exact non-`Rekall.*` runtime identities are eligible agent-owned state whether
  scaffold-qualified (`Game.*`) or exact authored CLR names, while canonical
  engine-owned components cannot substitute for game state. Actual component
  attachment and assertion truth remain enforced by runtime inspection. Focused
  red/green coverage passed 2/2 and the complete language-agent selection passed
  31/31. The locked zero-warning/error gate passed 1,005/1,005 engine and 7/7
  Studio tests twice and the complete installed matrix. Its 1,186-file archive
  is 201,575,773 bytes with SHA-256
  `5C9262D403586F19E21D8D13B6EFE04F3A656DCB02B6BD51E5042A864ED0099B`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 31 preserved the early gameplay priority:
  it scaffolded `GameRules` immediately after its first successful scene slice
  and produced a camera plan with nine renderables. It remained red at 43/57
  successful tool calls with no successful module build, runtime inspection,
  proof frame, or package. Qwen replaced correct scaffold/SDK patterns across
  eight source writes with invented calls including `Transform3D`,
  `ReadTransform3D`, `GetTransform3D`, `GetComponentNumber`, and an invalid
  two-argument `WithPosition3D`; six compiler attempts failed. The initial SDK
  inspection had returned the correct entity-transform/component-state recipe,
  but later repair attempts repeatedly omitted the required query and searched
  component schemas instead. The next generic tranche provides bounded,
  compiler-directed rejection/recovery for known nonexistent runtime SDK call
  shapes at source-write time, preserving valid authored C# rather than adding
  game-specific code. Evidence SHA-256 is
  `B1728ADCC42B5A62FAE4E64D638F48A3EFDDAF360F9A1B2C93A658DD3AFB0C00`.
- Early runtime authoring checkpoint: tasks that require runtime behavior now
  allow at most four successful world-authoring mutations before deferring
  further non-module work until runtime-system scaffold/source authoring begins.
  The bound is request-configurable from 0 (disabled) through 32, emits
  structured recovery, and leaves non-runtime tasks unchanged. Focused policy
  tests passed 2/2 and the full language-agent suite passed 29/29. The locked
  zero-warning/error gate passed 1,003/1,003 engine and 7/7 Studio tests twice
  and the complete installed matrix. Its archive is 201,575,814 bytes with
  SHA-256
  `FD0421AA9DDF871D4854588E8B74F0A54F917F79A9C34D32455B117020839289`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 30 made 37/61 successful tool calls,
  successfully compiled the authored `LumenVaultGameplay` runtime module, and
  produced a nonblank 960x540 viewport with ten renderables. The module slice
  began after two successful scene mutations instead of benchmark 29's 47-turn
  delay, proving the early policy changes shipped agent behavior. The run still
  reached the 64-turn limit without packaging: seven final runtime inspections
  were rejected because checkpoint coverage recognizes only `Game.*` component
  names, while the engine's own runtime-system scaffold and component registry
  use CLR component names such as `LumenVaultGameManager`. The exact asserted
  component was attached, but `candidateAgentComponentAssertion` remained null.
  The next generic tranche aligns agent-owned checkpoint identity with actual
  module component contracts and rejects built-in-only substitutions without
  imposing a namespace convention the scaffold does not produce. Evidence
  SHA-256 is
  `BE1586FDEAF0928A784D59082E5646934D46AF4852501A8DF6B17A3C61E2D2C5`.
- Schema-aware component admission: the production registry injects indexed
  built-in property policy into component add, property set, and scene blueprint
  commands without coupling the generic world layer to module schemas. Unknown
  names, case duplicates, encoded structured values, and numeric range violations
  fail before transaction capture or persistence with exact indexed targets and
  schema-search recovery. Valid built-ins and arbitrary `Game.*` state remain
  accepted. Focused admission/dispatch/validation coverage passed 48/48. The
  locked zero-warning/error gate passed 1,001/1,001 engine and 7/7 Studio tests
  twice and the complete installed matrix. Its 1,186-payload-file archive is
  201,572,291 bytes with SHA-256
  `FFBB2C30C6977659E43E3AACEE47FBCE4F6697C81A4BB5EB5519E6FE5F3581DA`;
  zero reusable nodes and zero run-scoped temp directories remained.
- Clean installed real-Qwen benchmark 29 made 41/61 successful tool calls,
  compiled the authored `LumenVault` module, reached zero validation issues,
  and produced a nonblank 960x540 viewport with nine renderables. Property
  admission rejected invalid authored fields at their exact blueprint indices
  and Qwen repaired them without a late remove-property loop. It remained red
  with no package because it spent 47 turns iterating scene content before
  scaffolding the required gameplay module; after the final successful build,
  its only remaining runtime call omitted inputs/assertions. The next generic
  tranche bounds pre-gameplay scene iteration and requires the thin executable
  runtime slice earlier whenever runtime behavior assertions are required.
  Evidence SHA-256 is
  `FEF947B5170416BD190435C1580FBF942B39859B23101C17765596894B016D3B`.
- Fail-closed component identity authoring: one exact catalog covers all 70
  built-in `Rekall.*` component identities and is mechanically checked against
  the module index. Direct component adds and bulk scene blueprints reject
  unknown reserved types before persistence, including case/whitespace variants,
  with conservative spelling and schema-search recovery; arbitrary `Game.*`
  components remain valid. Final validation consumes the same catalog. Focused
  world/dispatch/validation coverage passed 62/62. The locked zero-warning/error
  gate passed 995/995 engine and 7/7 Studio tests twice and the complete
  installed matrix. Its 1,186-payload-file archive is 201,558,474 bytes with
  SHA-256
  `611E63631B6F2D8AC04554287560AA5EA3A45A4E99FF5BFA5FD415BD84B06D27`;
  zero reusable nodes and zero run-scoped temp directories remained.
- Clean installed real-Qwen benchmark 28 made 57/64 successful tool calls,
  compiled the authored `GameRules` system, reached zero validation issues, and
  produced a nonblank 960x540 viewport with eight renderables. It remained red
  without a package: built-in component property defects were accepted during
  initial authoring, and Qwen spent roughly 24 late calls repeatedly validating
  and removing them one at a time before reopening an already-existing module.
  The next generic tranche validates exact built-in property names/shapes/ranges
  at component-add and blueprint mutation boundaries so invalid content never
  consumes the runtime/delivery budget. Evidence SHA-256 is
  `F8AAAEFC1569A8FE5B2F859A10F05923BA2EDD9D6F3BA2D070A9B4FB9C7A563C`.
- Post-runtime delivery reserve: a qualifying successful runtime inspection can
  extend an agent run by at most eight delivery turns, only on the turn that
  produces the fresh evidence and only once. Early checkpoints cannot arm the
  reserve later merely because budget elapsed; later mutations still invalidate
  runtime proof, and all agent/repair limits share the absolute 256-turn ceiling.
  Red-first policy coverage passes 27/27. The locked zero-warning/error gate
  passed 989/989 engine and 7/7 Studio tests twice and the complete installed
  matrix. The 1,186-payload-file archive is 201,545,117 bytes with SHA-256
  `3DC30C9445105B09ED0E5C252EB470F1DCD52F1A2BB8701EFDB1CACD5AD91567`;
  it left zero reusable compiler nodes and zero run-scoped temp directories.
- Clean installed real-Qwen benchmark 26 compiled authored gameplay, passed
  four runtime inspections, and produced a nonblank 960x540 viewport with 14
  renderables. It remained red at 48/78 successful tool calls with two invented
  HUD types and no package. The first reserve implementation armed from an old
  checkpoint as its remaining budget elapsed and added only one turn; the fresh
  post-repair checkpoint therefore could not arm it. That timing defect is now
  fixed by the verified current-turn requirement. Evidence SHA-256 is
  `FDE2804B1BB1D15D1924CA6B447679C75A306820EB612510BEE466335D820A0A`.
- Clean installed real-Qwen benchmark 27 remained red at 46/76 successful tool
  calls. It compiled `LumenVaultRules` and produced a nonblank 960x540 viewport
  with three renderables, but no runtime inspection passed and no package was
  produced. The measured generic defect is earlier in the loop:
  `rekall.component.add` accepted invented reserved `Rekall.Collider3D`, so the
  runtime reported zero compatible colliders and only final validation exposed
  the invalid type. Direct component mutation must reject unknown reserved
  types immediately and return exact schema recovery guidance. Evidence SHA-256
  is `9F3978D410ABF2F2ABB2B70D723C7A910F79DFD27E274955BAAE6D2D85BE131C`.
- Scene blueprint component normalization now accepts the canonical
  `{type, properties}` shape plus deterministic flat, `typeName`, and strict
  name/value-list representations while rejecting conflicts with precise JSON
  paths. Runtime property names remain case-sensitive, including `Type` beside
  the reserved lowercase discriminator. Focused dispatch coverage passed 26/26.
  The locked zero-warning/error gate passed 986/986 engine and 7/7 Studio tests
  twice and the complete installed matrix. The 1,186-payload-file archive is
  201,543,401 bytes with SHA-256
  `7DB2F19581316C9FACAF7263463BEF516E492865AFE3D56918C7717BE5E66ECB`.
- The locked gate now disables reusable MSBuild nodes for every outer operation
  and gives each engine-test run a unique, automatically cleaned temp root.
  This eliminated 15 orphan compiler nodes (previously consuming about 2.55 GB)
  and prevents accumulation under the shared test-temp directory. A one-time
  cleanup removed 290,083 stale Rekall test files totaling 47,936,807,924 bytes.
  The verified full gate ended with zero reusable nodes and zero run-scoped temp
  directories.
- Clean installed real-Qwen benchmark 25 made 49/73 successful tool calls and
  completed five real runtime inspections. Its final call passed three behavior
  assertions with semantic input and the authored `VaultGameplaySystem`; final
  validation had only `REKALL_UI_ELEMENT_NO_CANVAS`, and the viewport was a
  nonblank 960x540 frame with two renderables. It remains red: the successful
  final runtime checkpoint consumed the last protected turn, leaving no bounded
  opportunity to repair, package, and audit. No package was produced. The next
  generic tranche is a one-shot post-checkpoint delivery reserve, not additional
  scene-format tolerance. Evidence SHA-256 is
  `1797A54E8DA7353343BF3A94A6508938F51C283132ECE4FFECBB6A8EFCC170B3`.
- Reserved component fail-closed validation: every unknown `Rekall.*` component
  is now blocking. Repair suggestions are emitted only for a unique exact final
  segment match or a full-name edit distance of at most three; otherwise the
  validator refuses to guess. Focused validation and repair coverage passed,
  including property-preserving repair and no-suggestion behavior for an
  invented type. The locked zero-warning/error gate passed 982/982 engine and
  7/7 Studio tests twice and the complete installed matrix. The 1,186-payload-
  file archive is 201,530,567 bytes with SHA-256
  `B6B5B36BC2105BF3880750E0B2835970DFE436284F2FC4C8E7B1BDDECE28A826`.
- Clean installed real-Qwen benchmark 23 compiled an authored `GameRules`
  runtime system, projected semantic input, and produced a nonblank 960x540
  frame with two renderables. It remained red at the protected 76-turn bound:
  46/74 tool calls succeeded, final validation correctly exposed all 13
  blocking component type/property defects, and no package was produced. Nine
  `rekall.scene.apply_blueprint` calls failed while Qwen alternated among
  predictable component object representations and occasionally malformed
  encoded arrays. The next generic tranche is bounded, unambiguous scene
  blueprint component normalization with precise indexed rejection of
  ambiguous shapes. Evidence SHA-256 is
  `BE91F223DF81D16BF76DB1AA3CEA24D5BF3269A20B43C5A1419C5EEB097C85F8`.
- Bounded module compiler lifecycle: every module build has a two-minute
  engine-owned deadline. Timeout and external cancellation terminate the whole
  process tree with five-second cleanup bounds; timeout returns
  `REKALL_MODULE_BUILD_TIMEOUT`, exit `-1`, and no trust receipt. Six focused
  tests include real wedged processes. The locked zero-warning/error gate passed
  980/980 engine and 7/7 Studio tests twice and the full installed matrix. The
  1,186-payload-file archive is 201,528,876 bytes with SHA-256
  `5DE2A82788093487520C6B9E33DA42AF0DAB19038512D9B340225D57D285A4A4`.
- Clean real-Qwen benchmark 22 had no compiler hang; successful module builds
  completed in under one second with `timedOut:false`. It still failed at 64
  turns (45/76 successful calls, zero final renderables, no package). A new
  generic validator defect was measured: distant unknown reserved types such
  as `Rekall.Components.Transform3D` are silently skipped because reserved-type
  reporting currently requires edit distance <=3. Seven such hallucinations
  were hidden while only `Rekall.UICanvas` was reported. The next tranche makes
  every unknown `Rekall.*` type blocking and adds safe exact-suffix repair.
  Benchmark evidence SHA-256 is
  `2D43EAC81EAC088DC2EF0CF0DBDE175CDBE9B4E7A5C205DA3A99B8453F269CA9`.

- Runtime checkpoint argument normalization: the protected agent policy now
  evaluates bounded JSON-encoded arrays consistently with generic typed command
  dispatch, including nested input arrays, without mutating calls. Malformed,
  scalar/object-shaped, and over-1,000,000-character values fail closed. The
  focused policy selection passes 7/7. The zero-warning/error locked Release
  pipeline passed 978/978 engine and 7/7 Studio tests twice and completed the
  installed matrix. Its 1,186-payload-file archive is 201,524,293 bytes with
  SHA-256
  `6EC4582475E075B27E8E2E99383B37AD1D4E3076B535361AFDA39111E03020DF`.
- Fresh installed real-Qwen Lumen Vault benchmark 21 proved encoded arrays
  reached actual runtime inspection; semantic input projected and call 67
  passed three authored gameplay assertions. It is retained as diagnostic, not
  acceptance: a child `dotnet build` wedged without a timeout and required
  manual termination, then the recovered 64-turn run ended with one blocking
  UI-canvas issue, six renderables, and no package. The evidence SHA-256 is
  `BC644243E78375936006D3E907890B18FE93541642F48650083EF10312F2BE4C`.
  The immediate next tranche is bounded compiler timeout and process-tree
  cleanup, followed by another unchanged empty-project run.

- Agent entity query contract: `FindEntity` preserves exact opaque-id
  precedence, then resolves one unique case-insensitive exact authored name;
  duplicate names fail closed and `EntitiesNamed` remains the explicit
  multi-match primitive. A compiled project module proves name-based mutation.
  Focused TDD passed 7/7, full verification passed 976/976 engine and 7/7
  Studio tests, and the locked Release/distribution matrix passed twice with
  zero warnings/errors. The 1,186-file archive is 201,523,273 bytes with
  SHA-256
  `F8AF2C5D45182FF3FB5BB7A663BA05F74734ABDFD545435883784784AF5740A7`.
- Fresh installed real-`qwen3.5:35b` Lumen Vault benchmark 20 remained red at
  the 64-turn bound: 32/64 tool calls succeeded, the viewport was nonblank at
  960x540 but contained only two renderables, and no package was produced.
  Qwen compiled a real runtime system and authored semantic actions, but
  repeatedly JSON-encoded the typed `inputs` and `assertions` arrays. The
  mandatory gameplay checkpoint therefore never ran. The next work item is a
  generic, bounded typed-argument normalization/diagnostic contract, followed
  by the unchanged benchmark—not Studio polish or broader CI work.

- Windows distribution: fresh 200,967,116-byte win-x64 archive assembled from
  `3a84dbc` with SHA-256
  `A989D5790695578B672320B4DC89347F599D02391B1AD03D25A437EC11EFEB32`.
  The assembled directory contains 1,178 files.
- Canonical verification: 894/894 engine tests and 3/3 Windows Studio tests
  passed twice independently with four distinct retained TRX files; Release
  build completed with zero warnings and zero errors.
- Current Release verification: 950/950 engine tests and 7/7 Windows Studio
  tests pass. The full solution builds with warnings treated as errors and
  reports zero warnings and zero errors.
- Programmable material shader foundation: project vertex/fragment pairs now
  compile to real SPIR-V, reflect bounded vertex and descriptor metadata through
  Khronos SPIRV-Reflect, receive deterministic SHA-256 pipeline identities, and
  are validated against scene-material ABI version 1. Assignment uses that same
  resolver and rejects incompatible vertex formats or GPU resource sets before
  scene mutation. The focused locked Release selection passes 11/11 with
  warnings treated as errors; no vulnerable legacy reflection dependency was
  accepted.
- Shader draw propagation: authored shader-pipeline references now survive the
  generic viewport-to-mesh material binding path (including primitives,
  authored geometry, imported models, morphs, skinning, and virtual geometry)
  and are copied to the immutable draw range consumed by GPU backends. The
  focused locked Release mesh/batch selection passes 44/44 with warnings
  treated as errors.
- Native programmable-shader execution: Vulkan viewport capture resolves every
  distinct referenced project pipeline before GPU allocation, caches immutable
  opaque/transparent pipelines by SHA-256 content identity, selects default or
  authored pipelines per draw, and destroys custom pipelines/layouts/modules in
  reverse creation order. Invalid pairs fail before GPU work with bounded
  entity-specific diagnostics and no fallback. Real RTX 5090 tests proved a
  constant-magenta authored fragment shader changes captured pixels and a
  mixed two-draw frame retains both magenta custom output and green default
  output. The focused locked Release selection passes 45/45 with warnings
  treated as errors.
- Windowed programmable-shader execution: the Veldrid player now resolves and
  caches project pipelines by content hash, selects authored or default opaque
  and transparent pipelines per draw, and keeps resource binding ordered after
  pipeline selection. A recursive debounced `Shaders/` watcher invalidates live
  entries; invalid edits retain the last valid pipeline with a structured log,
  while a 64-pair bound prevents unbounded GPU residency. A real three-frame
  Windows Vulkan process created and drew an assigned shader successfully, and
  a real 300-frame process survived an intentionally corrupted live fragment
  shader after startup. The Windows Release build has zero warnings/errors and
  the focused locked Release selection passes 3/3.
- Agent shader inspection: `rekall.shader.inspect_pipeline` and
  `shader inspect-pipeline <root> <vertex> <fragment>` compile and reflect a
  project pair, then return ABI version, stable SHA-256 identity, SPIR-V byte
  counts, bounded vertex/resource metadata, validity, and bounded diagnostics
  without returning authored source. The command is registered for MCP and the
  focused locked Release command/catalog selection passes 12/12; the CLI build
  has zero warnings/errors.
- Shader validation and package integrity: the Validation layer now owns a
  dependency-inverted pipeline-validation contract, while the canonical
  Workflows composition supplies the real Rendering resolver. Playable
  verification blocks incompatible assigned pipelines and preserves their
  entity-specific shader diagnostic instead of collapsing it into a generic
  readiness failure. Packaging ships only vertex/fragment sources referenced
  by packaged scenes, includes them in the immutable SHA-256 inventory, and
  excludes unreferenced shader experiments. Consolidated audit recompiles the
  packaged scene after integrity inspection, including relocated directories
  and archives, so a shader cannot be made acceptable merely by rewriting its
  inventory hash. The focused locked Release selection passes 43/43.
- Portable material-resource ABI: native Vulkan no longer uses its legacy
  combined frame/material descriptor set and draw push constants. It now binds
  the same ABI v1 sets as the Windows player: frame uniform at set 0, an
  alignment-correct dynamic draw-uniform buffer at set 1, and seven separate
  sampled-image/sampler pairs at set 2. The default engine shaders were migrated
  to the same contract; persistent OpenXR renderers refresh both frame and draw
  uniforms on every compatible frame. The backend description now reports set
  indices and zero push-constant bytes honestly. The Vulkan scene tranche passes 80/80,
  including a real resourceful project shader on the local RTX 5090.
- Retained custom-material acceptance: `Examples/CustomMaterialShader` was
  created through public project, scene, entity, component, geometry, shader
  write, and shader assignment commands. `agent/tint` reflects all four vertex
  attributes plus frame/draw/material resources and resolves to pipeline SHA-256
  `B364F777B8DCD9D368DE9853C5A833F6D515CCCC8DB46A9B0F9CC03F787C04BF`.
  The 960x540 native Vulkan capture on `NVIDIA GeForce RTX 5090` is informative,
  uses no fallback/missing/unsupported assets or runtime observations, visibly
  separates the purple authored cube from the green default cube, and has
  SHA-256 `01AC5884D0B6E5535D2E4EEE8A109B82FCD424DA769D5DFD438EAAB3C27A12EB`.
  The resourceful Windows player completed 30/30 frames. Its graphics package
  contains 119 inventoried files, including both referenced shader sources;
  relocation and consolidated audit passed every check. The source archive has
  SHA-256 `82814A36867E3B2A55C601B460AAEBF67DBF02538BF825127215F6651ADA369D`.
- Full product gate after material acceptance: 967/967 engine tests and 7/7
  Windows Studio tests pass; the Release solution build reports zero warnings
  and zero errors. The gate initially exposed a headless Studio progress race:
  a later model failure could arrive before queued `Progress<T>` evidence. The
  headless path now reports immediately while WPF retains UI-context marshalling;
  the exact regression passed five consecutive focused runs before the full gate.
- Persistent 3D physics: the runtime now retains a BEPU simulation across
  frames, incrementally synchronizes bodies and statics, preserves angular
  motion/orientation and sleep state, and lets BEPU own contact response.
  Authored material response is projected into native contact springs instead
  of applying a second axis-aligned bounce pass after the solver. Generic world
  settings expose bounded velocity-iteration and substep counts.
- Inspectable physics evidence: `runtime inspect` reports bounded per-body
  backend, awake state, linear/angular velocity, orientation quaternion, and
  peak speeds with frame indices. The agent-authored `TumblingCubes` example
  builds from a freshly installed project-local SDK, simulates five randomly
  oriented falling cubes for 600 frames, and reports all settled bodies at
  zero angular speed. Its 960x540 RTX 5090 Vulkan capture is informative with
  zero missing assets, unsupported assets, fallbacks, or observations.
- Unified rotation contract: BEPU pose conversion now exactly matches the
  renderer's X/Y/Z matrix composition. A multi-axis regression proves the
  published Euler representation recreates the physics quaternion rather than
  producing visible flips near coupled rotations. The corrected Windows
  Vulkan player was watched live and its tumbling and settling behavior was
  confirmed visually.
- Real-time playback: runtime-observed playable games use a bounded fixed-step
  accumulator, so physics advances at 60 Hz from actual player delta time
  instead of once per rendered frame. Playable games and runtime loops now
  dispose their retained simulations deterministically.
- Direct SDK repair: agents can explicitly run `rekall.module.install_sdk` or
  `module install-sdk <root>` to install or repair the versioned project-local
  module SDK before building, without silent mutation during ordinary builds.
- Physics event parity: BEPU-backed 2D bodies now participate in the same
  generic `collision.begin`/`collision.stay`/`collision.end` and
  `trigger.enter`/`trigger.stay`/`trigger.exit` authoring contracts as 3D
  bodies. Runtime-realistic tests prove `Position2D` coordinates, explicit
  world-unit collider dimensions independent of visual transform scale, and
  exact `Rekall.CircleCollider2D` payload facts rather than accidentally using
  the unused 3D origin. The complete Release suite passes 936/936. These events
  intentionally remain deterministic bounding-radius overlap facts rather
  than exact BEPU contact manifolds; contact points, normals, impulses, and
  exact shape overlap are a
  separate production physics tranche.
- Physics SDK parity: agent-authored C# modules now have a typed `Raycast2D`
  alongside `Raycast3D`. It returns stable distance-ordered visible box/circle
  hits with optional tag/component filters, exact circle intersection, and
  exact transformed oriented-box intersection. Runtime SDK inspection exposes
  its compiled signature, usage, immutable `RekallAgeRuntimeVector2`, and
  construction guidance. `Rekall.EventBindings` schemas now document the exact
  `{ event, handler, active }` shape, generic lifecycle/pointer/2D-or-3D
  collision/trigger facts, and custom event emission.
- Physics pose parity: BEPU primitive and mesh bodies now receive authored
  Transform2D rotation or Transform3D orientation for both static and dynamic
  poses. Motion tests prove a rotated thin box blocks planar and 3D bodies at
  positions where the previous axis-aligned shape missed. Collider and trigger
  schemas explicitly define their dimensions as world-unit values independent
  of visual transform scale, matching established projects such as the
  Bouncing Ball example; BEPU, overlap facts, and ray queries now agree on that
  contract.
- Installed acceptance: canonical gate exited 0 against the freshly assembled
  product. Shipped project/module workflows, the generic game-authoring
  gauntlet, packaging and clean relocation, package audit, nonblank capture,
  Windows play, negative archive preflight, runtime UI/audio, animation,
  compatibility, atomic persistence, optimistic revisions, and damaged-file
  recovery all have installed-binary proof. Module trust reports ready with
  the `windows-appcontainer-restricted` posture. The shipped Studio also
  created a project from no prior files, traversed its Ollama adapter and agent
  tool loop, completed the gauntlet, captured a nonblank viewport, and produced
  a packaged game under deterministic model responses.
- Local agent: direct installed-AGE evidence rejected both
  `devstral-small-2:24b` and `qwen3-coder:30b` as replacements for the proven
  `qwen3.5:35b`. Devstral completed only two status calls in its fair full run;
  Qwen Coder made materially more progress at its normal temperature but did
  not complete, and a 0.15-temperature profile regressed into a 55-failure
  loop. Both Qwen Coder tags were removed. `qwen3.5:35b` is being restored as
  the default because it remains the only evaluated local model with repeated
  task-specific AGE game/package passes on this 32 GB RTX 5090.
- Studio authoring: project create/open, entity hierarchy/selection, generic
  entity/component/property mutation, scene validation, software-rendered
  viewport capture, and Windows player launch/stop now execute through the
  shared canonical command catalog. The focused checkpoint passes 7/7 tests;
  the Studio build has zero warnings and zero errors.
- Embedded AI: Studio now discovers local Ollama models, defaults to the
  installed `qwen3.5:35b`, runs/cancels bounded project-scoped authoring,
  streams turn/tool progress, and reloads, validates, and captures after the
  run. Canonical MCP execution rejects direct or JSON-string gateway attempts
  to use another project root. The focused agent/Ollama/MCP/Studio selection
  passes 41/41 across agent, Ollama, MCP, workbench, schema, catalog, and CLI
  coverage; the Studio build remains warning-free. A hidden executable smoke
  opened the authored project, stayed alive for five seconds, and was then
  stopped without an orphan process.
- Real local embedded-service proof: `qwen3.5:35b` authored and repaired a
  three-entity 3D scene with generic camera/light transforms and a colored
  cube. A final four-turn/four-tool evidence pass reported zero issues and
  captured an inspected nonblank 960x540 software frame at
  `Artifacts/StudioAgentProof/vout/Main_runtime_001.png` (3,419 bytes,
  SHA-256 `04CE4CFDC27FD73D50844FD3B3A81297A64CFF06C4870E37534395239463ED1C`).
- Real playable-game proof: the project-scoped service now accepts configured
  successful compound workflows as terminal evidence, so a model cannot waste
  later turns or mutate after the gauntlet has already passed. On a fresh root,
  `qwen3.5:35b` completed in two turns/two tools: engine status followed by
  `rekall.workflow.agent_authoring_gauntlet`. The resulting manifest reports
  passing scene-validation, module-build, restricted module-trust, and
  playtest checks. The 1,279,705-byte archive SHA-256 is
  `DECCAFEE7619D346DF48844374B80ECD31A32C999656277C3E896D3D194FC548`;
  the audited proof frame SHA-256 is
  `1D913CA3E7DB6204B7F48D04F4115B681722A129B20610BC7841FE258952C6C2`.
- Portable-game proof: the AI-created archive relocated to a clean 31-file
  destination, inspected ready, ran its packaged player for two frames with
  agent-authored module output, passed a full audit, and produced a second
  nonblank informative runtime capture with four distinct colors. Studio now
  exposes editable scene selection/Switch plus canonical Package and Audit
  Package actions, retaining the returned archive path for the audit. The
  warning-as-error Studio build and 7/7 focused workbench checks pass.
- Safe iteration: Studio Undo/Redo now restores persisted transaction
  preimages through the canonical restore command and refreshes the viewport.
  Undo itself captures an inverse preimage for real redo; multi-resource
  failure rolls back already restored resources in memory before returning a
  structured failure. Generic entity creation and component addition now
  capture preimages at the engine command layer. The combined workbench/world/
  dynamic-dispatch selection passes 21/21 and Studio builds warning-free.
- Schema-guided Studio authoring: the workbench now projects every registered
  built-in and verified project-module component schema into the inspector,
  including valid-but-undefined properties, types, editor kinds, numeric
  bounds, allowed values, descriptions, and asset kinds. Studio provides
  editable schema selectors, boolean/enum/asset choices, contextual constraint
  help, and still commits through the canonical generic component commands.
  The combined editor/module/world selection passes 36/36, the Studio build has
  zero warnings/errors, and a hidden authored-project smoke remained healthy.
- Deterministic Studio automation: a Windows-targeted test project now drives
  the real Studio view model and async commands without UI timing races. It
  creates a project and entity, chooses a registered component/property schema,
  mutates the persisted scene through canonical commands, and proves undo/redo.
  Locked restore and the zero-warning Release solution build pass; the Release
  checkpoint now passes 894 engine tests plus 3 Studio tests (897 total).
- Studio agent automation and installed proof: the shipped Studio has an
  explicit headless automation entry point that still drives its real view
  model, Ollama adapter, project-scoped agent, progressive MCP executor,
  validation, viewport, and packaging paths. A deterministic installed fixture
  completes engine discovery plus the generic gauntlet in two tool calls and
  produces a nonblank frame and audited archive. A separate real local
  `qwen3.5:35b` run completed in four turns/four tools with zero validation
  issues; its archive SHA-256 is
  `7A2DB7E70FA763932F8347C41DFD7ED3CEAE13393D68B7EC4E9D70F3F670E881`
  and viewport SHA-256 is
  `23DCEE33EF1F4D8B2D322833ED6DB9CD0C5316E3F0138BE7ED8D101EA80E0FDF`.
  The gauntlet now safely reuses a compatible open project/scene instead of
  failing with a false missing-revision conflict.
- Arbitrary-game authoring hardening: task-specific Studio sessions no longer
  accept the fixed gauntlet as terminal completion. The blueprint workflow
  safely reuses compatible open project/scene documents and rejects missing
  capabilities without rewriting them. Studio and agent schema discovery keep
  built-ins inspectable while a project module needs repair, while low-level
  module loading remains fail-closed. Tool search exposes matched native tools
  on the next turn, the runtime-system scaffold documents exact semantic-input
  and immutable-world SDK patterns, completion audits may accept repaired
  non-security failures, and Studio automation discovers the actual produced
  archive with a configurable bounded turn budget. Empty zero-renderable debug
  frames no longer satisfy its nonblank viewport gate.
- Agent authoring: both source and installed multi-subsystem benchmarks created
  and repaired UI, animation, and audio content using tool calls.
- Runtime animation: generic clip playback, bounded Hermite interpolation,
  glTF `CUBICSPLINE`, bounded parameter-driven state graphs, weighted layers,
  crossfades,
  deterministic resume, GLB skeletal channels, runtime joint poses, imported
  JOINTS_0/WEIGHTS_0, and CPU skinning before Vulkan submission are covered.
- Installed skeletal rendering: the shipped CLI sampled `Lift` at frame 30,
  exposed skin `Rig` and one joint, then produced informative hardware Vulkan
  frames at frames 1 and 30 with different SHA-256 hashes and visible movement.
- Diagnostics: runtime inspection exposes UI/audio/animation state; viewport
  analysis reports severe clipping and invisible text without irrelevant
  camera advice for UI-only scenes.
- Broad benchmark baseline: installed `qwen3.5:35b` reached the 36-turn limit
  after 36 tool calls, 410,197 prompt tokens, and 11,325 completion tokens. It
  exposed project-validation discovery, playable repair propagation, and
  in-process module rebuild defects rather than producing a false pass.
- Benchmark-driven fixes: project-wide validation now aggregates all scenes;
  `Rigidbody2D` is registered and executes deterministic Bepu XY-plane physics;
  loaded project modules no longer lock authoring outputs; playable verification
  preserves executable scaffold suggestions. The full Debug suite is 562/562.
- Broad benchmark rerun: fresh installed binaries again reached the 36-turn
  bound after 36 tool calls, 389,332 prompt tokens, and 9,309 completion
  tokens. Unlike the baseline, it discovered project validation and authored
  both 3D and 2D physics scenes. Independent inspection isolated two remaining
  generic repair defects: no ordinary command could remove a rejected property,
  and schema numeric bounds such as positive mass were not validated.
- Validation repair contract (targeted verification):
  `rekall.component.remove_property` now removes a single property
  transactionally; unknown-property issues carry exact executable repair
  arguments; and out-of-range numeric properties produce blocking diagnostics
  with an exact boundary-setting action. Five focused regressions and the full
  564/564 Debug suite pass.
- Validation repair distribution gate: the fresh self-contained binaries passed
  installed doctor, project/module authoring, the generic game gauntlet,
  package audit/relocation/run/capture, runtime UI, and audio acceptance.
- Broad benchmark rerun 3: the fixed 36-turn agent run used 36 tools,
  477,614 prompt tokens, and 6,961 completion tokens. It still stopped at the
  bound, but independent installed-CLI verification proves two scenes, zero
  project validation issues, one active 3D body at Y -2.137 after 30 frames,
  and one active 2D body with `Rekall.PhysicsState2D`. The remaining failures
  are redundant discovery, absent no-module scaffold guidance, package-root
  ambiguity, and no ordinary package-relocation command.
- Benchmark-driven deliverable contracts: component search now explicitly
  batches concepts; invented tool aliases return nearest exact registered
  names; a missing module returns an executable playable-scaffold action;
  package creation, executable, archive, and package-root roles are explicit;
  invalid executable package paths return structured diagnostics; and
  `rekall.workflow.relocate_playable_package` copies and integrity-verifies a
  package at a fresh destination. A real relocated package runs successfully,
  and the full Debug suite passes at 567/567.
- Deliverable-contract distribution gate: fresh installed binaries passed the
  complete black-box acceptance, including relocated package audit/run/capture,
  after both independent Release passes completed at 567/567.
- Broad benchmark rerun 4: the fixed 36-turn run again stopped at the bound
  after 36 tool calls, 460,681 prompt tokens, and 7,194 completion tokens. It
  exposed two generic authoring defects before packaging: encoded JSON object
  fields such as `component.add.properties` bypass normalization, and the atomic
  blueprint workflow supports only one scene, forcing inefficient incremental
  authoring for multi-scene projects.
- Multi-scene authoring contract: `rekall.workflow.create_blueprint_project`
  now accepts an arbitrary `Scenes` list and creates every complete scene in
  one command while retaining the existing single-scene request shape.
- Dynamic argument recovery: bounded encoded `JsonObject` and `JsonArray`
  fields now normalize to their declared types while genuine string fields stay
  unchanged. Focused regressions and the full 568/568 Debug suite pass.
- Multi-scene/normalization distribution gate: fresh installed binaries passed
  the complete black-box acceptance after both independent 568/568 Release
  passes.
- Broad benchmark rerun 5: the unchanged 36-turn run reached multi-scene
  authoring, validation repair, module build, package creation, package run,
  relocation, both audits, and capture after 36 tools (706,921 prompt tokens;
  6,571 completion tokens). Independent verification proves zero validation
  issues, active 3D/2D physics, package integrity, successful primary package
  execution, and successful relocation. It remained a failure because a
  graphics package contains only the Windows player, which exits successfully
  but does not emit the structured frames consumed by deterministic package
  capture/audit.
- Graphics deliverable proof: graphics packages retain the Windows player as
  primary launch and include an integrity-inventoried headless proof companion.
  Package capture selects the proof player; primary run semantics remain
  unchanged. A real graphics package now captures nonblank evidence and passes
  audit both before and after relocation. The full Debug suite passes 569/569.
- Graphics-proof distribution gate: both independent Release passes and the
  complete installed black-box acceptance passed on fresh binaries.
- Broad benchmark rerun 6: the agent reported completion after 35 turns and 34
  tool calls (591,837 prompt tokens; 8,491 completion tokens), but independent
  acceptance rejected that claim. Package audit captured the scaffold module's
  blank structured frame instead of the packaged authored runtime scene. The
  agent also supplied the package root as its proof output directory; capture
  wrote an undeclared PNG into the immutable package, so subsequent integrity
  checks correctly failed. This is a near-pass, not an accepted benchmark pass.
- Package-proof contract repair: capture now first proves the packaged launch,
  then renders the manifest scene from the packaged `Game` root through the
  deterministic runtime viewport. Directory and manifest packages reject any
  proof output at or beneath the immutable package root before execution or
  writes, with an exact safe retry command; package audit preserves the audit
  intent in that retry. Original, relocated-directory, and ZIP scenarios pass,
  rejected output leaves integrity intact, and the full Debug suite passes
  569/569.
- First distribution attempt: the clean build and both independent Release
  passes completed at 569/569, but installed acceptance caught a compatibility
  regression before relocation: authored-scene capture changed the established
  proof filename from `package_play_frame_001.png` to `Main_runtime_001.png`.
  The command now keeps the deterministic package-proof filename while retaining
  the authored-scene pixels. A filename regression and the full 569/569 Debug
  suite pass; the distribution gate must be rerun from scratch.
- Final package-proof distribution gate: a fresh clean build completed with 0
  warnings and 0 errors; both independent Release passes completed at 569/569;
  and installed acceptance passed project-local SDK/module authoring, the
  generic gauntlet, deterministic package proof, relocated ZIP audit/run/capture,
  runtime UI, software viewport analysis, audio simulation, and Windows player
  audio-device startup. The canonical archive is 194,669,640 bytes.
- Broad benchmark rerun 7: the original graphics package now passed audit and
  independent inspection found a ready 468-file package plus zero validation
  issues across two scenes. The run still failed at 36 tools (675,995 prompt;
  7,189 completion): C: had only 67,919,872 bytes free, relocation returned a
  generic copy exception seven times, and no relocated proof was produced.
  Independent runtime inspection also rejected physics completion because each
  rigid body and collider were on separate entities and transforms stayed at
  zero. This is not an accepted pass.
- Relocation capacity contract: the workflow now measures the verified package
  inventory against free space on the destination volume before it creates a
  staging directory. Insufficient capacity returns
  `REKALL_PACKAGE_RELOCATION_SPACE_INSUFFICIENT`, reports required/available
  bytes, explicitly prevents same-destination retries, and leaves no destination
  or staging residue. The regression and full Debug suite pass at 569/569 with
  test temporaries routed to F:.
- Relocation-capacity distribution gate: the clean build completed with zero
  warnings/errors, both independent Release passes completed at 569/569, and
  fresh installed acceptance passed SDK/module authoring, gauntlet, original and
  relocated package proof, runtime UI, viewport, simulated audio, and Windows
  player audio. Acceptance temporaries ran on F:; the new canonical archive is
  194,669,627 bytes.
- Broad benchmark rerun 8: the engine reported completion after 22 turns and 21
  tools (285,836 prompt; 4,476 completion), but the final model response was
  empty and the trace ended after validation. Independent acceptance found one
  blocking mass issue, no deliverable workflow, valid 3D/2D motion, and runtime
  warnings for missing explicit transforms. Empty no-tool responses can no
  longer complete the embedded agent; they now trigger a bounded corrective
  continuation. This run remains failed.
- Agent-loop completion contract: an empty or whitespace no-tool response now
  receives a bounded corrective user turn and cannot set `Completed=true`.
- Physics authoring contract: any `Rekall.Rigidbody3D`/`Rigidbody2D` without its
  dimension-matching transform is now a blocking static validation issue using
  the same `REKALL_PHYSICS_BODY_NO_TRANSFORM` code as runtime observation, with
  an exact executable `rekall.component.add` repair. Both regressions and the
  full Debug suite pass at 571/571.
- Agent/physics distribution gate: the clean build completed with zero warnings
  and errors, both independent Release passes completed at 571/571, and fresh
  installed acceptance passed the complete SDK, authoring, package relocation,
  visual proof, UI, and audio checks. The canonical archive is 194,672,330 bytes.
- Broad benchmark rerun 9: the installed agent reached valid original package
  audit and relocation, but exhausted its 36-turn bound before relocated audit,
  capture, and final evidence. It used 36 tools, 462,217 prompt tokens, and
  10,135 completion tokens. The run exposed that a malformed entity in a later
  multi-scene blueprint could leave earlier project files behind, and that an
  empty component property object was still a dynamically required argument.
  This is not an accepted benchmark pass.
- Blueprint/component repair: project blueprint workflows now preflight every
  requested scene, entity, and component before creating the project; invalid
  standalone blueprints return structured errors without changing the scene.
  `rekall.component.add` now defaults omitted properties to an empty object.
  Three regressions and the full Debug suite pass at 574/574.
- Blueprint/component distribution gate: the clean Release build completed
  with zero warnings and errors, both independent Release passes completed at
  574/574, and installed acceptance passed project-local SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport analysis, simulated audio, and Windows player audio. The
  canonical 1,149-file archive is 194,675,770 bytes.
- Broad benchmark rerun 10: the agent correctly stopped at its 36-turn bound
  (36 tools, 405,610 prompt tokens, 9,633 completion tokens) before package
  creation. It produced two validation-clean scenes, but used playable-module
  execution instead of module-free runtime inspection and authored 3D colliders
  into its 2D scene. Independent inspection proved the 3D body at Y -2.132 but
  the nominal 2D body at Y approximately zero. This is not an accepted pass.
- Physics/inspection repair: validation now blocks colliders that conflict with
  an entity's 2D/3D transform or body contract and returns exact component
  removal/addition repairs. The new generic `rekall.component.remove` command
  preserves other components and transaction preimages. Engine status and the
  embedded-agent contract now direct deterministic subsystem verification to
  `rekall.runtime.inspect_scene`, which requires no playable module. The full
  Debug suite passes at 576/576.
- Physics/inspection distribution gate: the clean Release build completed with
  zero warnings and errors, both independent Release passes completed at
  576/576, and installed acceptance passed SDK/module authoring, generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical archive is
  194,688,256 bytes.
- Broad benchmark rerun 11: the agent reached module-free inspection of both
  scenes, module build, package creation/inspection/run, and relocation before
  the 36-turn bound (36 tools, 490,824 prompt tokens, 8,646 completion tokens).
  Independent inspection found zero active physics bodies: plausible
  `Rigidbody3D`/`Rigidbody2D` names lacked the canonical `Rekall.` prefix and
  were treated as custom components. Two validation warnings also remained,
  and original/relocated audits were absent. This is not an accepted pass.
- Alias/audit repair: validation now blocks exact unqualified aliases of
  registered built-ins and returns executable remove/add migration commands
  that preserve authored properties. Agent status, audit schema, and embedded
  guidance now identify `rekall.workflow.audit_playable_package` as the
  consolidated inspect/run/nonblank-capture proof. The full Debug suite passes
  at 577/577.
- Alias/audit distribution gate: the clean Release build completed with zero
  warnings and errors, both independent Release passes completed at 577/577,
  and installed acceptance passed the full SDK, authoring, package portability,
  visual proof, UI, and audio matrix. The canonical archive is 194,690,547
  bytes.
- Broad benchmark rerun 12: the agent stopped at 36 tools (440,173 prompt;
  6,657 completion) before module/package work. Four direct native calls carried
  an equivalent gateway argument envelope and were rejected as missing fields.
  Independent inspection also exposed that runtime transform extraction ignored
  canonical schema-cased `X`/`Y` properties, so a valid 2D body initialized near
  the origin rather than its authored Y=5 position. The project retained two
  blocking issues and had no 3D body. This is not an accepted pass.
- Runtime/protocol repair: runtime transform extraction is now case-insensitive
  and proven with exact schema-cased 2D physics properties. Discovered native
  tools safely unwrap gateway-style `name`/`arguments` envelopes, including
  JSON-string arguments, before typed dispatch. The full Debug suite passes at
  578/578.
- Runtime/protocol distribution gate: the clean Release build completed with
  zero warnings and errors, both independent Release passes completed at
  578/578, and installed acceptance passed the complete SDK, authoring, package
  portability, visual proof, UI, and audio matrix. The canonical archive is
  194,691,566 bytes.
- Broad benchmark rerun 13: the agent stopped at 36 tools (615,017 prompt;
  6,808 completion) after deterministic runtime inspection, module scaffolding,
  package creation, and original-package audit, but before relocation. It
  recovered from `frameCount` instead of `Frames`, required a separate scaffold
  retry after package creation found no module, and used `archivePath` instead
  of the audit command's canonical `PackagePath`. Repeated component searches
  also exposed that core `Rekall.MeshRenderer` and `Rekall.SpriteRenderer`
  runtime contracts were absent from the registered schema catalog. This is not
  an accepted pass.
- Agent contract discovery repair: dynamic requests narrowly map `frameCount`
  to `Frames` and `archivePath` to `PackagePath` only when the target command
  declares the canonical property. Broad physics schema discovery ranks the
  matching 2D/3D transform, rigid-body, collider, renderer, camera, and light
  families together. `Rekall.MeshRenderer` and `Rekall.SpriteRenderer` now have
  strict registered schemas, and the engine-owned gauntlet no longer authors an
  ignored sprite color property. Focused regressions and the full Debug suite
  pass at 580/580.
- Agent contract discovery distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 580/580; and fresh installed acceptance passed project-local SDK/module
  authoring, the generic gauntlet, original and relocated package audit/run/
  nonblank capture, runtime UI, software viewport analysis, simulated audio,
  and Windows player audio. The canonical 1,149-file archive is 194,700,296
  bytes.
- Broad benchmark rerun 14: the installed agent stopped correctly at 36 tools
  (512,928 prompt; 6,331 completion) after clean validation, two runtime
  inspections, module build, and a final package-creation call, but before any
  package audit or relocation. Independent installed inspection rejected the
  physics proof: both scenes reported two nominal bodies but zero colliders,
  empty dynamic transforms, and no movement after 30 frames. Validation had
  incorrectly reported zero issues because a rigid body shape was not required.
  This is not an accepted pass.
- Rigid-body shape repair: validation now blocks a 2D or 3D rigid body without
  a dimension-compatible collider and returns an exact executable default
  collider addition. Rigidbody and collider schema descriptions now explain
  dynamic composition and that a static surface omits the rigid body. Applied
  to the untouched rerun-14 project, the repaired validator reports all four
  false bodies as blocking issues. The regression and full Debug suite pass at
  581/581.
- Rigid-body shape distribution gate: the clean Release build completed with
  zero warnings and zero errors; both independent Release passes completed at
  581/581; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package audit/run/nonblank capture, runtime
  UI, software viewport analysis, simulated audio, and Windows player audio.
  The canonical 1,149-file archive is 194,703,222 bytes.
- Broad benchmark rerun 15: the agent reached original package audit,
  relocation, and relocated audit by tool 26, then stopped at 36 tools
  (620,253 prompt; 7,983 completion) while revisiting authoring evidence. Both
  physics scenes genuinely simulated: two 3D bodies moved from Y=10 to 8.733
  and one 2D body moved from Y=5 to 3.733. The run nevertheless ended with one
  deliberately introduced invalid renderer property still blocking validation
  and two render-layer warnings; package proofs also predated those late edits.
  This is not an accepted pass.
- Runtime motion evidence: deterministic runtime inspection now reports each
  entity's initial transform and exact 2D/3D position delta alongside the final
  transform. One call therefore proves simulation or animation displacement
  without an agent inferring the starting pose or spending calls on repeated
  inspection. Applied to rerun 15, it reports Y deltas of -1.267 for both 3D
  bodies and the 2D body. The regression and full Debug suite pass at 581/581.
- Runtime motion-evidence distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 581/581; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. Installed inspection printed the
  new delta fields. The canonical 1,149-file archive is 194,706,489 bytes.
- Broad benchmark rerun 16: the installed agent stopped at the 36-turn bound
  after 35 tools (475,141 prompt; 8,249 completion), one operation short of a
  relocated-package audit. Independent verification proved zero validation
  issues, exact Y delta -1.267 for one 3D and one 2D dynamic body, static
  colliders, and original/relocated manifests. Recoverable waste included a
  blueprint without `Entities`, inspection without `Frames`, a package attempt
  before scaffolding, and an unsafe proof directory correctly rejected by the
  immutable-package guard. This is not an accepted pass.
- Embedded delivery sequencing contract: the Ollama agent system contract is
  now a named, regression-tested engine API. It requires requested fault
  injection and zero-issue repair before runtime evidence; treats nonzero
  `PositionDelta2D`/`PositionDelta3D` as direct motion proof; and orders original
  audit, relocation, and relocated audit after authoring is stable. It also
  prohibits reopening authoring after package proof unless evidence failed and
  explicitly keeps proof output outside immutable packages. The full Debug
  suite passes at 582/582.
- Embedded delivery-contract distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 582/582; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,708,031 bytes.
- Broad benchmark rerun 17: model variance consumed the full 36 tools (640,102
  prompt; 8,977 completion) in authoring and validation repair without runtime
  or package work. The first atomic blueprint encoded `Scenes` as a string and
  omitted project identity; later authoring required many component/property
  removals. Independent evidence still found real 3D/2D motion, but Physics2D
  retained two visibility warnings and no deliverable existed. This is not an
  accepted pass.
- Compact blueprint tool contracts: atomic project and scene blueprint command
  descriptions now include minimal exact nested JSON exemplars covering project
  identity, scene/entity/component arrays, and the canonical component
  `type`/`properties` shape. They explicitly prohibit JSON-string encoding for
  the nested arrays. The regression and full Debug suite pass at 583/583.
- Compact blueprint-contract distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 583/583; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,708,844 bytes.
- Broad benchmark rerun 18: the agent reported completion after 29 turns and 28
  tools (475,523 prompt; 6,866 completion), with clean validation, genuine 3D/
  2D motion, original audit, relocation, and relocated audit. Independent
  installed verification confirmed both package audits and the motion deltas,
  but rejected completion: the 3D dynamic body had no renderer and Physics2D
  reported zero visible renderables. Nonblank capture proved package execution,
  not the specifically requested visible physics content. This is a near-pass,
  not an accepted pass.
- Evidence-gated agent completion: the language-model agent API now supports an
  opt-in two-phase completion audit, enabled for embedded Ollama runs. A first
  final response is only a proposal; a dedicated audit turn must compare every
  explicit task requirement against direct tool evidence, treating zero counts,
  warnings/issues, missing components/artifacts, stale proofs, and mere
  existence evidence as failures. If audit resumes tool use, the next proposed
  completion is audited again. The regression and full Debug suite pass at
  584/584.
- Evidence-gated completion distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 584/584; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,709,786 bytes.
- Broad benchmark rerun 19: the evidence gate correctly prevented a premature
  success and stopped at 36 turns/35 tools (614,364 prompt; 8,248 completion)
  after revalidating and reinspecting both scenes. Independent installed proof
  showed the artifacts were actually complete: zero validation issues, visible
  3D/2D dynamic bodies with Y delta -1.267, and fresh passing 467-file audits of
  original and relocated packages. The bounded agent lacked a final audited
  response, so the benchmark remains failed despite complete artifacts.
- Bounded request aliases: dynamic requests now additionally normalize `frame`
  to `Frames` and `packageDirectory` to `PackagePath` only when the selected
  request type declares the canonical field. Existing `frameCount` and
  `archivePath` behavior remains covered. The regression and full Debug suite
  pass at 584/584.
- Bounded request-alias distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 584/584; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,709,366 bytes.
- Broad benchmark rerun 20: the installed agent stopped at 36 turns/32 tools
  (688,007 prompt; 16,335 completion) without an audited final response. Field
  aliases removed the prior malformed inspection/package requests, but one
  invented schema-search namespace and packaging before module scaffolding
  remained. Independent evidence found zero validation issues, visible 3D
  motion, genuine but non-rendered 2D body motion, and fresh passing 218-file
  original/relocated package audits. This remains a failed benchmark.
- Visible-delivery agent contract: embedded runs now name the exact component-
  schema search command, require a renderer on every requested visible dynamic
  body, and order required module scaffolding before the first package call.
  The regression and full Debug suite pass at 585/585.
- Visible-delivery contract distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 585/585; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,710,036 bytes.
- Broad benchmark rerun 21: the agent produced complete artifacts and a final-
  looking response at turn 36 after 34 tools (663,401 prompt; 10,676 completion),
  but the evidence gate correctly returned `Completed=False` because no audit
  turn remained. Independent proof found zero validation issues, visible 3D/2D
  motion, and fresh passing 467-file original/relocated audits. Repeated schema
  discovery and a redundant second relocation cycle consumed the final budget.
- Audit-efficiency contract: initial component discovery is now explicitly
  consolidated, and completion audits reuse current passing direct evidence.
  They must not recreate or relocate proven packages unless evidence is missing,
  contradicted, or stale after mutation. The full Debug suite passes at 585/585.
- Audit-efficiency distribution gate: the clean Release build completed with
  zero warnings and zero errors; both independent Release passes completed at
  585/585; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,710,683 bytes.
- Broad benchmark rerun 22: model variance spent eight runtime calls repeating
  missing `Frames` failures with `fabricFrameCount`/`fabricFrames`, then stopped
  at 36 turns/36 tools (545,575 prompt; 8,024 completion) before packaging.
  Independent proof found zero validation issues and visible 3D/2D bodies with
  Y delta -1.267, but no package. This is a clear benchmark failure.
- Wrapper-prefixed frame recovery: dynamic requests now normalize the two
  observed wrapper-prefixed fields to `Frames` only for request types declaring
  that canonical property. The regression and full Debug suite pass at 585/585.
- Wrapper-prefixed frame distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 585/585; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,710,286 bytes.
- Broad benchmark rerun 23: the agent stopped at 36 turns/36 tools (572,246
  prompt; 15,997 completion) after repeated empty-scene project rejections and
  malformed incremental blueprint repairs. It never reached runtime or package
  proof; only project/scene documents existed. This is a clear failure.
- Empty-scene blueprint scaffolding: atomic project creation now permits named
  scenes with empty entity arrays, and ordinary scene blueprints accept empty
  arrays for no-op/clear semantics. This provides a transactional generic
  recovery path while retaining entity/component validation. The full Debug
  suite passes at 586/586.
- Empty-scene scaffold distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 586/586; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,710,362 bytes.
- Broad benchmark rerun 24: the installed agent stopped at 36 turns/36 tools
  (573,004 prompt; 15,500 completion) after four similar oversized blueprint
  failures, later validation/runtime work, and package preflight/scaffolding.
  Independent proof found one warning, non-rendered requested dynamic bodies,
  zero visible 2D renderables, and no package. This is a clear failure.
- Atomic-blueprint fallback contract: the embedded agent now attempts a complete
  atomic project once, then on structural failure creates the same named empty
  scenes and uses smaller per-scene blueprints. It must not repeat substantially
  identical failed blueprint arguments. The full Debug suite passes at 586/586.
- Atomic-blueprint fallback distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 586/586; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,710,384 bytes.
- Broad benchmark rerun 25: the agent reached original/relocated package audits,
  then its completion audit wholesale replaced Main and stopped at 36 turns/38
  tools (715,864 prompt; 11,563 completion). Independent proof found five
  blocking noncanonical rigid-body types, zero 3D motion, and stale package
  evidence. This is a failed, regressed final state.
- Targeted audit repair: completion audits now prohibit scene redesign or
  wholesale replacement. A genuine failed requirement must be repaired with
  the smallest canonical targeted mutation, followed only by evidence made
  stale by that change. The full Debug suite passes at 586/586.
- Targeted audit-repair distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 586/586; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,710,647 bytes.
- Broad benchmark rerun 26: all 35 tools succeeded, but 27 individual suggested
  property-removal calls consumed the 36-turn bound (616,406 prompt; 11,328
  completion) before runtime or package proof. This is a clear throughput
  failure despite correct individual diagnostics.
- Bounded batch validation repair: `rekall.validation.repair_project` executes
  engine-generated mutation suggestions in bounded passes, skips read-only
  discovery actions, stops safely on failed mutation, and returns fresh project
  validation. The embedded contract uses it for multiple repairs while retaining
  deliberate-fault requirements. The full Debug suite passes at 587/587.
- Batch validation-repair distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 587/587; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,726,034 bytes.
- Broad benchmark rerun 27: the installed agent stopped at 36 turns/36 tools
  (568,211 prompt; 8,199 completion) after batch repair aborted on an incomplete
  advisory blueprint suggestion. Independent proof found two blocking invented
  component types, zero active physics bodies in both scenes, and no package.
- Canonical validation repair: batch execution now permits only exact safe
  component mutation commands. Close unknown reserved component types receive
  executable canonical add/remove repairs with authored properties preserved,
  rather than incomplete blueprint hints. The full Debug suite passes at
  588/588.
- Canonical validation-repair distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 588/588; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,726,872 bytes.
- Broad benchmark rerun 28: the installed agent reached clean validation,
  moving visible 3D/2D scenes, module build, and package creation, then deferred
  deliberate-fault exercise until after packaging and stopped at 36 turns/36
  tools (515,162 prompt; 8,862 completion). Independent proof found both bodies
  moving by -1.267 and the original 467-file package passing inspect, run,
  audit, and nonblank capture; relocation alone remained missing.
- Validation sequencing and registered guidance: deliberate faults must use
  existing relevant components immediately after scene authoring, never new
  audit-only entities. Validator and context suggestions now reference only
  registered validation/schema operations instead of the nonexistent generic
  repair workflow or incomplete blueprint calls. The full Debug suite passes
  at 589/589.
- Validation-sequencing distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 589/589; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,726,496 bytes.
- Broad benchmark rerun 29: the agent reached both moving-scene inspections,
  original package audit, relocation, and relocated audit by tool 26, then
  completion audit regressed the source project and stopped at 36 turns/36
  tools (677,078 prompt; 12,861 completion). Both independent 467-file package
  audits passed, but the package retained negative mass as string `"-2.5"`
  because validation had falsely reported numeric strings as clean.
- Numeric-string range validation: invariant numeric strings now participate in
  built-in schema minimum/maximum enforcement and receive canonical numeric
  repair suggestions. The full Debug suite passes at 590/590.
- Numeric-string validation distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 590/590; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,727,467 bytes.
- Broad benchmark rerun 30: the agent repaired faults before runtime, proved
  both moving scenes, and audited an original package, then completion audit
  reopened authoring and stopped at 36 turns/36 tools (638,419 prompt; 13,470
  completion). Independent final proof found 17 issues after the rewrite, no 3D
  body, and a still-moving 2D body. Package audit exposed only nonblank proof,
  not the task's required informative-frame fact, and batch repair was repeated
  four times after reaching non-automatic remaining issues.
- Informative package proof and repair termination: packaged capture now keeps
  nonblank and informative facts distinct, returns full bounded frame analysis,
  and package audit requires an explicit `informative-frame` check. Batch
  validation repair now returns a termination reason and remaining automatic
  repair count; advisory-only leftovers terminate as `no-progress` with an
  explicit instruction not to retry unchanged. The full Debug suite passes at
  591/591.
- Informative-proof distribution gate: the clean Release build completed with
  zero warnings and zero errors; both independent Release passes completed at
  591/591; installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. Relocated package audit explicitly
  reported `informative-frame: True` with four distinct colors. The canonical
  1,149-file archive is 194,731,087 bytes.
- Broad benchmark rerun 31: the unchanged installed-agent task reached moving
  3D and 2D scenes, original package audit, relocation, and relocated audit by
  tool 26, then stopped at the 36-turn limit after 35 tool calls (605,077
  prompt; 10,904 completion). Independent installed verification found zero
  blocking validation issues, both dynamic bodies moving, canonical numeric
  masses of `0.0001`, and both 218-file packages ready, runnable, nonblank, and
  explicitly informative with three distinct colors. Four camera-culling
  warnings remain because each camera mask excludes the authored render layer;
  both balls are therefore reported culled. This is the strongest artifact
  result so far, but not a clean bounded benchmark pass.
- Durable completion evidence and camera-mask guidance: pruned agent context
  now retains up to 12 distinct successful validation/runtime/build/delivery
  milestones in addition to the 12 most recent executions. Camera 2D/3D
  schemas explicitly define `CullingMask` as a named-layer expression and
  reject numeric-bitmask folklore through guidance; render-layer validation
  warnings now state the exact wildcard or named-layer correction. The full
  Debug suite passes at 592/592. The first full run failed only after `F:`
  reached zero free bytes from 36.4 GB of accumulated generated test/gate temp
  artifacts; after stopping the runner and clearing only those verified
  ephemeral directories, the unchanged suite passed with about 69 GB free.
- Durable-evidence distribution gate: the clean Release build completed with
  zero warnings and zero errors; both independent Release passes completed at
  592/592; installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software
  viewport, simulated audio, and Windows player audio. Relocated audit
  explicitly reported `informative-frame: True` with four distinct colors.
  The canonical 1,149-manifest-file archive is 194,732,792 bytes.
- Broad benchmark rerun 32: the unchanged installed-agent task completed in 23
  turns and 20 tool calls (483,918 prompt; 6,135 completion), well inside its
  36-turn bound. Independent installed verification found two scenes with zero
  issues and zero warnings, 3D and 2D bodies each moving by `-1.267` after 30
  frames, canonical positive numeric masses, and no culled renderables. Both
  original and relocated 218-file packages were ready, ran with exit code 0,
  captured nonblank frames, and passed `informative-frame` with five distinct
  colors. This is the first genuine bounded broad-authoring acceptance pass.
- Package relocation trust-boundary hardening: ZIP relocation now reuses the
  same normalized-path, collision, entry-count, per-entry-size, and total-size
  bounded extractor as package run/audit/capture. An adversarial regression
  test mutates a previously inspected archive during the relocation capacity
  check and proves structured
  `REKALL_PACKAGE_RELOCATION_SOURCE_CHANGED` failure, no traversal write, no
  destination, and no abandoned staging directory. All four package-integrity
  scenarios and the full Debug suite pass at 592/592.
- Relocation-security distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 592/592; the rebuilt installed product passed portable SDK/module
  authoring, the generic gauntlet, relocated ZIP run/audit/capture through the
  hardened extractor, informative proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-manifest-file
  archive is 194,732,663 bytes.
- Directory-package trust boundary: directory and manifest-path inspection now
  applies the same default 100,000-entry, 8 GB per-file, and 32 GB total
  uncompressed bounds as archive inspection before recursive enumeration or
  hashing. Package roots and descendants marked as symbolic links, junctions,
  or other reparse points fail with a structured
  `REKALL_PACKAGE_PATH_REPARSE_POINT` diagnostic. Injectable bounded limits and
  file attributes provide deterministic low-cost regression coverage; all four
  package-integrity scenarios and the full Debug suite pass at 592/592.
- Directory-security distribution gate: the clean Release build completed with
  zero warnings and zero errors; both independent Release passes completed at
  592/592; the installed product passed bounded directory-package gauntlet,
  hardened relocated ZIP run/audit/capture, informative proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-manifest-file archive is 194,734,675 bytes with SHA-256
  `9b754c5f6d2b81b13e28a2516b74855178a020c47ae7a8f043ea36bb6ea935f9`.
- Runtime soak/performance contract: `rekall.runtime.inspect_soak` now loads an
  authored scene once, resumes its immutable world through bounded fixed-step
  chunks, records compact subsystem/memory/throughput checkpoints, and returns
  named deterministic and caller-budget checks through the same CLI/MCP
  contract. Test-first implementation exposed and fixed a core resumed-time
  drift: continuous and chunked execution now derive elapsed time from an
  absolute frame timebase. Invalid requests fail before scene I/O, and budget
  failures preserve all evidence with
  `REKALL_RUNTIME_SOAK_BUDGET_EXCEEDED`.
- Runtime-soak distribution gate: the installed CLI completed 600 frames over
  exactly 10 simulated seconds in five checkpoints at 4,629.6 frames/second,
  with 686,216 bytes retained managed-memory growth, stable 20-system order,
  zero entity growth, and zero checkpoint observations/events. All nine checks
  passed against a 30 FPS, 64 MiB, zero-entity-growth, 32-observation, and
  128-event budget. A separate installed negative proof completed 12 frames
  and retained three checkpoints while returning exit code 1, a failed
  throughput check, and the structured budget error. The clean Release build
  had zero warnings/errors, both Release passes completed at 601/601, and the
  installed SDK/module, gauntlet, relocated-package, UI/viewport, audio, and
  soak matrix passed. The canonical 1,149-manifest-file archive is 194,778,548
  bytes with SHA-256
  `675a442cf35947263841ae915550632c7a63f4d5fa0bbbc572c378f8f607cd2f`.
- Module SDK trust anchor: project-local SDK manifests now carry an atomic,
  bounded SHA-256 inventory, and every module build verifies the exact resource
  set, compatibility/product contract, canonical props bytes, and running-host
  assembly bytes before starting the compiler. Reparse paths, forged local
  resources even with matching forged inventory, malformed/duplicate entries,
  unexpected files/directories, and injected low bounds fail closed with
  `REKALL_MODULE_SDK_INTEGRITY_FAILED`. The complete Modules plus engine-doctor
  Debug selection passes at 62/62.
- Module provenance receipts: successful canonical builds now atomically emit
  `rekall.module.build.json` with the explicit `in-process-full-trust` posture,
  product/SDK identity, deterministic pre-build source fingerprint, output
  size/SHA-256 inventory, and main assembly identity. The read-only inspector
  rejects stale source, missing/extra/tampered output, malformed/traversing or
  duplicate receipt entries, identity mismatches, reparse points, and injected
  bounds without loading module code; packaged output remains verifiable after
  authoring source is removed. Source edits during compilation fail with
  `REKALL_MODULE_SOURCE_CHANGED_DURING_BUILD` and no receipt.
- Canonical intermediate hardening: module projects exclude every `bin/**` and
  `obj/**` tree from source discovery, while policy verifies and build resets
  the dedicated `obj/rekall` tree. This fixed a full-suite discovery where a
  migrated example's legacy generated sources entered portable compilation.
  Bouncing Ball now consumes the public project-local SDK instead of repository
  project references. The complete Debug suite passes at 631/631.
- Verified-only module loading: schema discovery, runtime systems, playback,
  CLI, and dynamic/MCP execution now share one admission path. It requires a
  ready trust inspection, constrains dependency resolution to receipt-inventoried
  files under the verified output root, and rehashes each stream under a
  read/delete-safe lock immediately before `AssemblyLoadContext` consumes it.
  Missing receipts, stale source, changed artifacts, and unverified dependencies
  fail with their exact trust code; the generic coded-boundary contract preserves
  that code through dynamic and CLI adapters. Packaged modules still load after
  source and the project-local SDK are removed. PDBs remain deliberately
  non-shipping ancillary output and are excluded from receipts, while every
  other output remains exact. The focused loader/adapter matrix passes at 23/23
  and the complete Debug suite at 637/637.
- Public trust workflow: `rekall.module.inspect_trust` is a read-only,
  recommended CLI/MCP command that reports the explicit
  `in-process-full-trust` posture, bounded module evidence, exact issues, and a
  rebuild action without loading code. Engine status and README guidance make
  clear that unsigned receipts are integrity/provenance consistency—not a
  sandbox, code signature, or publisher authentication. Playable verification
  exposes a named `module-trust` check, and packaging repeats trust inspection
  immediately before copying the `Game` payload. An injected reparse regression
  proves exact rejection and no payload copy. Packaged receipts intentionally
  exclude non-shipping PDBs while remaining exact for all shipping artifacts.
  The complete Debug suite passes at 639/639.
- Installed module-trust distribution gate: the shipped CLI scaffolded and
  built a portable runtime module, emitted its receipt, and reported
  `Ready: True` with `in-process-full-trust`. A copied project then had one DLL
  byte changed in place; both read-only trust inspection and schema-loading
  admission returned nonzero with exact
  `REKALL_MODULE_OUTPUT_HASH_MISMATCH`, while the untouched project and
  relocated package continued to run, audit, and capture. The clean Release
  build had zero warnings/errors, both independent Release passes completed at
  639/639, the installed gauntlet/package/UI/audio/Windows-player/soak matrix
  passed, and the 600-frame soak ran at 4,627.9 FPS with 687,712 bytes retained
  growth. The canonical 1,149-manifest-file archive is 194,923,288 bytes with
  SHA-256
  `365fcc80428348006174384f32221f47d352b8238807caf75e83ca35deb743b5`.
- Bounded failure-report foundation: the shared Core contract records only
  explicit schema/product/component/outcome/category/recovery/frame/exception
  facts and operator actions. Its store uses per-root concurrency control,
  unique temporary files plus atomic moves, bounded payload/read/retention
  limits, newest-first inspection, malformed-file isolation, and fail-closed
  reparse-root handling. The focused Debug selection passed 5/5, including 12
  concurrent complete writes and contract checks excluding ambient environment
  variables, arbitrary exception data, and project content.
- Bounded player-session supervision: rendering now classifies only typed
  device loss and narrow Veldrid Vulkan device/surface signatures as
  recoverable. The generic supervisor disposes failed sessions before cold
  recreation, preserves finite-frame remainder and continuous-run accounting,
  defaults to two retries, and keeps initialization or arbitrary runtime
  failures fatal. Its production writer persists recovered/exhausted/fatal
  evidence through the bounded atomic store and returns report paths; a writer
  failure is reported but cannot hide the original outcome. The supervisor
  selection passes 8/8 and the combined diagnostics/recovery selection 13/13.
- Agent-readable failure evidence: `rekall.diagnostics.inspect_failures` is a
  recommended read-only CLI/MCP command with a 50-report ceiling and exact
  component/outcome/code filtering. It returns report paths, bounded exception
  facts, limitations, next actions, and isolated malformed-file issues without
  executing project code. Engine status advertises the workflow. CLI output is
  intentionally compact and excludes stack excerpts. The direct command,
  catalog, status, and real CLI-process selection passes 5/5.
- Windows-player cold recovery: the Veldrid/SDL player now runs through the
  generic bounded supervisor and recreates the complete session only for
  classified graphics lifecycle failures. A real Vulkan process injected one
  device loss, disposed the failed session, completed exactly 5/5 total frames
  in two attempts, emitted `REKALL_PLAYER_GRAPHICS_RECOVERED`, and exited 0.
  Arbitrary fatal injection emitted `REKALL_PLAYER_RUNTIME_FATAL` and exited 10
  after one attempt. Repeated loss stopped after the default two retries,
  emitted `REKALL_PLAYER_GRAPHICS_RECOVERY_EXHAUSTED`, exited 11 after three
  attempts, and preserved three completed frames. Each case wrote one bounded
  report. Strict player build produced zero warnings/errors; the combined
  player/supervisor/inspection selection passes 11/11. Cleanup proceeds across
  all GPU resources and closes the SDL window even when idle-wait/disposal
  steps fail. Recovery remains an honest cold restart and does not preserve
  arbitrary in-memory module state.
- Desktop diagnostics and shutdown operability: Studio startup, dispatcher,
  AppDomain, and unobserved-task failures now use the same bounded atomic
  evidence contract with exception-instance duplicate suppression. Dispatcher
  failures remain fatal; unobserved tasks are recorded and explicitly
  observed. The strict Studio build and focused desktop/Studio/CLI selection
  pass with zero warnings/errors and 4/4 tests. Full-suite diagnosis also found
  that Veldrid's non-threaded SDL window could block close for about 21 seconds
  after async initialization. The player now owns SDL windows on a dedicated
  thread and requires confirmed closure within one second; the unchanged
  three-process fault proof dropped to 5 seconds. Locked dependency graphs were
  regenerated and the exact locked graphics-player publish regression passed.
  The complete Debug suite passes 658/658 in 2m15s.
- Installed recovery product gate: the canonical build completed a locked
  restore, a zero-warning/zero-error Release build, and two independent
  658/658 Release passes. Installed one-shot graphics loss recovered by cold
  session restart in two attempts, completed 5/5 frames, emitted
  `REKALL_PLAYER_GRAPHICS_RECOVERED`, and exited 0. Installed arbitrary fatal
  failure emitted `REKALL_PLAYER_RUNTIME_FATAL` after one attempt and exited
  10. Installed repeated graphics loss exhausted the two-retry budget after
  three attempts, preserved 3/5 completed frames, emitted
  `REKALL_PLAYER_GRAPHICS_RECOVERY_EXHAUSTED`, and exited 11. Exactly three
  bounded reports were written and the shipped CLI inspected all three codes.
  The unchanged installed authoring gauntlet, relocated package audit,
  informative hardware frame, runtime UI, audible player, and 600-frame soak
  also passed. The soak simulated exactly 10 seconds at 4,467.9 FPS with
  691,608 bytes retained growth and all nine checks passing. Recovery is a
  bounded cold restart and intentionally does not preserve arbitrary in-memory
  module state.
- Installed compatibility product gate: shipped binaries inspected exactly two
  implicit schema-0 documents as migratable, proved dry-run byte immutability,
  applied both schema-1 migrations, preserved unknown extension data and one
  exact backup set, then reinspected exactly two current documents. A forced
  project schema 2 was rejected with `REKALL_DOCUMENT_SCHEMA_FUTURE` and its
  SHA-256 remained unchanged. The same canonical run passed Debug at 683/683,
  two independent Release passes at 683/683, installed module trust/tamper,
  the generic authoring gauntlet, relocation/package audit, an informative
  hardware frame, runtime UI, audible player, 600-frame soak, and all desktop
  recovery outcomes. Soak simulated exactly 10 seconds at 4,504.8 FPS with
  703,912 bytes retained growth and all nine checks passing.

## Current gaps

- Physics is functional but not yet production-complete. 2D is a planar BEPU
  projection with box/circle shapes and persisted linear velocity; 3D adds
  box/sphere/capsule and static or convex mesh shapes. Persistent 3D bodies now
  retain angular velocity, orientation, and sleep state, and native BEPU
  contacts own friction/restitution response. Remaining breadth includes
  generic joints/constraints, a dedicated 2D world/material contract, authored
  angular control, collision layers/masks, exact contact manifold/impulse
  facts, deformables, and measured large-world broadphase performance.
- 3D rendering is substantial and hardware-backed: perspective/orthographic
  cameras, viewports/layers/stereo/OpenXR, primitives and authored/imported GLB
  meshes, PBR texture inputs, directional/point lighting, generic animation,
  skeletal skinning, morph targets, LOD/virtual geometry, Vulkan capture and a
  windowed player are verified. Breadth still lacks a generic spot-light
  contract and mature shadow/contact/render-feature coverage expected of a
  finished general-purpose 3D engine.
- Expand adversarial security tests around authored JSON, migration races,
  diagnostic stores, and full-trust module inputs.
- Production consumers still execute C# modules in-process until the active
  restricted-host consumer cutover is complete. The AppContainer worker and
  broker now exist with adversarial local proof, but are not yet a shipped
  product claim. Build receipts also remain unsigned; publisher signatures are
  a separate future security capability.
- Complete advanced animation breadth such as native glTF weight-channel
  animation, TANGENT/sparse/quantized morph accessors, broader complex
  transform fixtures, richer graph curves, and interruptible or hierarchical
  graph policies.
- Expand Studio asset/module workflows and run broader installed game-creation
  benchmarks beyond the fixed gauntlet. Deterministic WPF automation,
  schema-guided editing, transactional undo/redo, embedded Ollama authoring,
  real play/package/audit controls, and installed Studio-to-agent game creation
  are now present. The workbench is functional but not yet a finished
  professional editor.
- Repeat the now-passing uninterrupted empty-project task-specific benchmark
  against freshly assembled installed binaries. Repository-built Studio passed
  the complete `Echo Foundry` authoring and audit session in 49 turns. The next
  bar is distribution parity, followed by reducing the six recovered malformed
  or initially incomplete non-scope tool calls without hiding real errors.
- Fresh arbitrary-game Benchmark 16 used the restored real local Ollama
  `qwen3.5:35b` through repository-built Studio. It authored a runtime module,
  repaired it to a successful build, created semantic input and scene content,
  and reached direct runtime assertion evidence, but exhausted the 64-turn
  budget plus 12 protected repair turns after 71 tool executions. It is an
  honest failure: the final scene had zero renderables and three invalid
  `Transform3D` properties, so no package was produced. The measured loop was
  dominated by AGE rejecting the model's intuitive
  `delta.transform.position3d.x` assertion subject even though the diagnostic
  only said to supply a "transform delta." AGE now normalizes that generic
  alias (and its 2D/axis variants) to the canonical `delta.position*` subjects
  in both checkpoint preflight and runtime evaluation, and returns a copyable
  exact transition assertion. The focused agent/runtime selection passes
  52/52. Failure evidence SHA-256 is
  `F29CA3D71DFD7F6DE598FF12DA7867710BF4E340F4F82554FE153E4BC6423341`.
- Fresh arbitrary-game benchmark 2 (`Lumen Vault`) reached eight visible
  renderables and a valid active camera, and compiled an initial generic
  delta-time gameplay system, but exhausted 64 turns after destructive
  re-scaffolding replaced that working source. The final blockers were numeric
  SDK misuse, unremoved schema-invalid properties, a missing final module
  receipt, and no package. This is retained as failure evidence rather than a
  product pass; evidence SHA-256 is
  `7B5A618D19D8C7D07FFAF51183732BD19D3976CAC469072BA3D6164B89092FB3`.
- Fresh arbitrary-game benchmark 3 exposed a more serious false-positive
  boundary. Studio reported task success with clean validation, six visible
  renderables, two compiled modules, a 44,665,001-byte audited/relocated
  package, and evidence SHA-256
  `7A68FF6A88A4CF240D0DE94DCB165AD4ADA2A10F948E3B25F934979A0DD9F4A5`.
  Independent source and runtime inspection disproved the requested gameplay:
  `Player Orb` lacked the registered `VaultPlayerComponent`; seals were never
  deactivated or reset; no completion/HUD state was written; and one nested
  mutation restarted from stale `world`. The package and audit are valid but
  are not accepted as game-completion proof. The new generic runtime assertion
  path rejects this exact project with `REKALL_RUNTIME_ASSERTION_FAILED` and
  reports the missing player component and bounded actual state.
- Fresh arbitrary-game benchmark 4 proved that the executable evidence path is
  real but exposed late-test orchestration. Ollama compiled its authored
  runtime module, then delayed `rekall.runtime.inspect_scene` until turn 62 of
  64. The command correctly failed two gameplay assertions and reported zero
  projected semantic input actions, but only two turns remained and no repair
  or retest occurred. Independent inspection confirmed no
  `Rekall.InputActionMap` in the scene and two declared agent-owned state
  components missing from module registration. This is retained as failure
  evidence with SHA-256
  `D6E8C09C180DB20ED4BB63EC6F1526A7303058152F3A111C6E4766787E5ACD32`.
- Fresh arbitrary-game benchmark 5 used the real local Ollama
  `qwen3.5:35b` through the freshly assembled installed Studio, not the
  deterministic transport fixture. It compiled an agent-owned runtime module
  and captured six visible renderables, but ignored the prompt-only gameplay
  checkpoint, never called `rekall.runtime.inspect_scene`, exhausted 64 turns,
  retained eight blocking unknown collider/rigidbody component types, and
  produced no package. This is a product failure with evidence SHA-256
  `8002393ED2AD7E566D649060FB5F2594F63A45445BBB8528C37C06E7244475B5`.
  The staged 200,950,053-byte archive used for that real-model run has SHA-256
  `00DDD9AF5E6BA4AF6E2D210D1717ABF627A5237FF9A240EEC6C363D33D0F1B36`.
  Both canonical 917-test/6-Studio-test Release passes and all installed checks
  before the final deterministic Studio/Ollama transport fixture passed; that
  fixture failed before its first agent tool call and is not counted as AI or
  gameplay evidence.
- Fresh arbitrary-game benchmark 6 used the real local Ollama `qwen3.5:35b`
  through a newly assembled 1,177-file product. The checkpoint enforcement
  worked: unrelated calls and an empty-assertion inspection were blocked, a
  qualifying failed inspection unlocked the 12-turn repair reserve, and the
  model later produced a relocated audited package with ten renderables. It is
  retained as failure evidence, not a playable-game pass. Independent source
  and scene inspection found zero projected input actions because the action
  map was encoded as a JSON string, no seal state component was attached, and
  the module queried the exact name `Energy Seal` while authored entities were
  named `Energy Seal 1` through `3`; the module therefore returned before its
  gameplay logic. The model weakened a later assertion and exhausted turn 76
  after audit. Evidence SHA-256 is
  `05D7AF9CEF33B0385EB9D94BEF3CDE3A430A6EA3D10D1343E7F7F17F12AA500E`;
  the benchmark product archive SHA-256 is
  `61935BAB1D47E86B11DDEC303BCB4C1EE649C39D59BCFF2818C4BF633439CD43`.
- Fresh arbitrary-game benchmark 7 used real local Ollama `qwen3.5:35b`
  against the self-contained product built from commit `5137685`; its
  200,956,173-byte archive SHA-256 is
  `E05F83A8A6852C6BE4A0970B8BF20A0EA6C2EF903E961FBF2D9D5B9166C1E0E1`.
  It exposed an orchestration deadlock rather than a gameplay pass. The model
  compiled `GameRules` before populating the scene, so the immediate checkpoint
  correctly required attached state and a transition but incorrectly deferred
  `create_blueprint_project`, `scene.apply_blueprint`, and component discovery.
  The scene remained empty, all 64 turns were exhausted, and no package was
  produced. This is retained as failure evidence with SHA-256
  `65A805689F53C13E81DA2ED32184606B7B73F7E4E3A7B476889210E80CA260DE`.
- Fresh arbitrary-game benchmark 8 used real local Ollama `qwen3.5:35b`
  against the self-contained product built from `f57854c`; its 200,956,794-byte
  archive SHA-256 is
  `0D24AA4CDD9FF0B685B7947F983FAE236C35D22D7BDA2B387E0F6877DF77A6B9`.
  The deadlock was resolved: after compiling its runtime module first, the
  model successfully used prerequisite authoring to populate a scene with an
  active camera, input map, player state, and three seal components. It still
  failed the product gate: nine runtime inspections omitted assertions and six
  supplied insufficient coverage, it exhausted 64 turns, Studio saw only one
  renderable, and no package was produced. Evidence SHA-256 is
  `45C783F1C4355D72755068382305E06B93DB0B251EE7139EE3A6B10222E596D4`.
- Fresh arbitrary-game benchmark 9 used real local Ollama `qwen3.5:35b`
  against the self-contained product built from `ae73d20`; its 200,959,555-byte
  archive SHA-256 is
  `6660FC1370811713440E1C48D03012CE9515A6723F2385FC466FAEAF4E9FA874`.
  Structured evidence exposed a pure tool-name failure: after one correct
  engine-status call, the model emitted `rekal.*` instead of `rekall.*` for 25
  consecutive calls despite exact suggested names. The project remained empty
  and no package was produced; Studio correctly reported failure. Evidence
  SHA-256 is
  `8FB9F93685B3E4F70B5431D8F136227CB1359A4732A6E17D2A281254319E8F61`.
- Fresh arbitrary-game benchmark 10 used real local Ollama `qwen3.5:35b`
  against the 1,177-file self-contained product built from `2e083ca`; its
  200,963,166-byte archive SHA-256 is
  `C728242296448F33518AF086DEC1C97DDE0BEC36A166E874DC271FF2294A12F7`.
  The one-edit recovery removed Benchmark 9's typo loop and the model reached
  source authoring and its first module build in 15 tool attempts. Turn 16
  then failed outside AGE command execution when Ollama returned HTTP 500 for
  malformed generated function-call XML. Studio correctly failed the product
  gate, but its structured execution list was empty because the session did
  not return normally. No scene or package was produced. Evidence SHA-256 is
  `B5A8C70A041A1EDE99A325FF51AFB0DED301E0FBC6CA257D400AB5F761FB834D`.
- Fresh arbitrary-game benchmark 11 used real local Ollama `qwen3.5:35b`
  against the self-contained product built from `0e8d6d9`; its 200,964,267-byte
  archive SHA-256 is
  `81E86F266C7D888F213BB48C345CF9825F9BEA57344140CBE5F2BAC0427F0F30`.
  The provider retry path avoided Benchmark 10's interruption and Studio
  retained all 64 tool executions. The model authored a nine-entity scene,
  compiled `LumenRules.dll`, and produced a nonblank seven-renderable viewport.
  It still failed honestly: 28 tool attempts failed, no package was produced,
  and repeated runtime checkpoints omitted `componentType` while sometimes
  putting the exact `Game.Modules.LumenRules.PlayerState` type in `entityName`.
  The checkpoint gate therefore never executed the malformed proof and the
  model exhausted its turn bound. Final validation also reported invalid
  guessed built-in component names/properties introduced by late wholesale
  blueprint replacements. Evidence SHA-256 is
  `CBFA0484D85CA1447191CF0FD23DF96C6AD843DADCC80D3DBFC0B643BBF76A59`.
- Fresh arbitrary-game benchmark 12 attempted the unchanged task with
  `devstral-small-2:24b` against the 1,177-file product built from `ef6cf3d`;
  its 200,965,841-byte archive SHA-256 is
  `3A2B8744CFB95958B247EE35C10F97E48A069749DAB2A934AF4EC79B985FB40F`.
  The run stopped before its first tool because AGE sent Ollama's optional
  `think: medium` field and Devstral explicitly returned HTTP 400, "does not
  support thinking." This is retained as provider-compatibility failure
  evidence, not a comparison of game-authoring quality. Evidence SHA-256 is
  `2AAB90FF76E7B1B2C60276979958166AD2AF12F2E4310F8B411B8594454E147A`.
  Independent native-API smokes then proved Devstral selects a registered tool
  and emits the exact four-field `Game.*` component assertion plus a strict
  transform delta that Benchmark 11 repeatedly malformed.
- Fresh arbitrary-game benchmark 13 was Devstral's fair full comparison after
  the model-capability fallback shipped in product `8e57cb9`. The unchanged
  1,177-file product archive is 200,966,057 bytes with SHA-256
  `FA15362BB75D7FF972AB6DA287D85BF711ECC59B1C04A3EB7F697E286528E678`.
  Devstral stopped after six turns and two successful engine-status calls,
  authored no project content, and ended by promising to begin. Evidence
  SHA-256 is
  `FE7446128D32E048CAA254BA3C0C02E485505CF5A844277C25121DEED26524DC`.
- Fresh arbitrary-game benchmark 14 evaluated real local
  `qwen3-coder:30b` against the same installed product. It made 87 executions
  including the protected repair reserve, authored eight renderables, compiled
  two modules, fixed validation, and passed a strict runtime inspection at
  execution 75. Later scene/playable repairs invalidated that evidence and it
  exhausted the reserve before packaging: 30 calls failed and no archive was
  produced. Evidence SHA-256 is
  `50779E136539B1C32260C5A3BE970FC5FE6EB81E8EA5E8A16E51722881DA5C61`.
  A bounded continuation made the existing project worse by replacing it with
  five entities and invalid `Rekall.Collider3D` content; continuation evidence
  SHA-256 is
  `09537B03240050F762047C45776D26AD983E3268A8B520D7F9D37FED359FB872`.
- Fresh arbitrary-game benchmark 15 evaluated the same Qwen Coder weights with
  temperature 0.15, top-p 0.8, and top-k 20 from an empty project. Native API
  smokes selected the exact status tool and emitted a correctly structured
  component-existence plus strict movement assertion, but full authoring
  regressed decisively: 64 executions contained 55 failures, the final scene
  had only two renderables and 15 blocking validation issues, the module never
  produced a receipt, and the last turns repeated trust inspection instead of
  the returned build action. No runtime checkpoint or package was produced.
  Evidence SHA-256 is
  `0CD2E7AA3AB10D941004E455A69E6EEAF532E47425B5DA52417D89F73A50EE9B`.

## Recently completed

The freshly assembled `3a84dbc` product now passes the complete installed
distribution acceptance in one clean run. The first attempt isolated a bug in
the deterministic Studio transport fixture: its raw HTTP reader handled
`Content-Length` but treated a chunked JSON POST as header-only, closed the
socket while Studio was still sending, and correctly caused Studio to fail
without tools or a package. The fixture now consumes bounded chunk framing;
the exact focused check went red then passed with two tool calls, a nonblank
viewport, and a package, after which the complete installed matrix exited 0.
This fixture proves shipped Studio/Ollama protocol wiring only. It is not a
language-model or autonomous-authoring proof; Benchmark 16 still requires real
local Qwen 3.5.

The repeated-failure recovery is now present in a fresh self-contained Windows
product assembled from `3a84dbc`. Its manifest declares 1,177 payload files;
the installed directory contains 1,178 files including the manifest. The
200,967,116-byte archive SHA-256 is
`A989D5790695578B672320B4DC89347F599D02391B1AD03D25A437EC11EFEB32`.
This product is the next real-model benchmark subject; assembly alone is not a
game-creation pass.

Benchmark 15's 54-call trust-inspection loop now has a generic bounded
intervention. After three consecutive failures of the same canonical tool with
identical arguments, the language-model agent injects the failed call, exact
arguments, consecutive count, and any engine-returned `nextActions` into a
direct recovery message. It explicitly does not execute the suggested action
for the model, and a later different or successful call clears the intervention
for that turn. The regression reproduces three identical missing-receipt trust
failures and proves the next model request receives the exact
`rekall.build.modules` recovery action. The focused agent loop passes 20/20;
the complete Release engine suite passes 926/926, Studio passes 7/7, and the
warning-as-error solution build reports zero warnings and zero errors.

The replacement-model experiment is complete. Devstral was decisively worse
than Qwen Coder, while normal-temperature Qwen Coder was much closer to the
closed loop than its low-temperature profile. Neither matched the strongest
real `qwen3.5:35b` AGE evidence: Qwen 3.5 previously completed `Prism Relay`,
`Signal Garden`, and uninterrupted `Echo Foundry` task-specific sessions with
compiled gameplay, nonblank captures, packages, relocation, and audits. The
experiment therefore removed the Qwen Coder tags and selected Qwen 3.5 for
restoration. This is a measured model-quality decision, not an engine success;
the current Lumen Vault loop remains red.

Benchmark 12's model-capability mismatch now has a bounded adapter fallback.
When—and only when—Ollama returns HTTP 400 stating that the selected model does
not support thinking, the adapter removes the optional `think` field and
retries once. Other 4xx failures still surface unchanged; existing bounded
5xx/rate-limit/timeout recovery remains intact. The regression proves the
first request contains `think`, the compatible retry omits it, and the response
is retained. All four Ollama adapter tests pass. The complete Release engine
suite passes 925/925, Studio passes 7/7, and the warning-as-error solution build
reports zero warnings and zero errors. Fresh Benchmark 13 is Devstral's first
valid full comparison gate.

Benchmark 11's repeated assertion-field inversion now receives a copyable,
structured repair. Checkpoint failures expose the required four-field
`component`/`exists` shape and, when malformed arguments unambiguously contain
an ordinary entity name plus a `Game.*` type placed in `entityName`, derive a
candidate assertion with those values in their correct fields. The engine does
not execute, author, or accept that suggestion as evidence; the agent must
still run the canonical inspection and pass the strict transition assertions.
The human instruction also names `componentType` explicitly and prohibits
putting types in `entityName`. The focused regression reproduces the exact
inversion. The complete Release engine suite passes 924/924, Studio passes 7/7,
and the warning-as-error solution build reports zero warnings and zero errors.
Fresh Benchmark 12 is the next real-Ollama gate.

Benchmark 10's provider interruption now has bounded generic recovery and
durable partial evidence. The Ollama adapter retries request timeout, rate
limit, and server failures twice with cancellation-aware bounded backoff; a
persistent failure still surfaces with its exact status and response body.
Studio records every completed tool execution as progress arrives, so a later
provider or session exception cannot erase the execution ledger used to
diagnose autonomous authoring. Focused regressions cover the observed HTTP 500
recovery and a later-turn model failure. The complete Release engine suite
passes 923/923, Studio passes 7/7, and the warning-as-error solution build
reports zero warnings and zero errors. Fresh Benchmark 11 is the next real-
Ollama gate.

Benchmark 9's systematic namespace typo now has bounded deterministic recovery.
The progressive MCP executor canonicalizes only a unique registered tool name
exactly one insertion, deletion, or substitution away; ambiguous names and
names two or more edits away still fail closed. Successful results record the
attempted name, canonical name, and edit distance. The language-model agent
uses the canonical name for gameplay checkpoint, repair-reserve, completion-
audit, terminal-tool, progress, and retained-execution policy, including the
observed `rekal.runtime.inspect_scene` case. The focused MCP/agent selection
passes 10/10; the complete Release engine suite passes 922/922, Studio passes
6/6, and the warning-as-error solution build reports zero warnings and zero
errors. Fresh Benchmark 10 is the next real-Ollama gate.

Benchmark 8's repeated malformed checkpoint attempts now receive structured,
fact-specific repair evidence. Synthetic checkpoint failures report booleans
for representative inputs, an attached `Game.*` component assertion, and a
strict transition assertion, plus the exact missing list. Studio automation
now persists the bounded structured tool-execution ledger—including arguments,
success state, and result preview—rather than only human progress lines, so
future installed real-model failures are independently diagnosable. Focused
engine and Studio tests pass; the complete Release engine suite remains
920/920, Studio passes 6/6, and the warning-as-error solution build reports
zero warnings and zero errors. Fresh Benchmark 9 is the next real-Ollama gate.

Benchmark 7's deadlock is now covered by a generic checkpoint-preparation
contract. After a successful runtime build, the agent may still use bounded
tool discovery, project/scene summaries, blueprint/scene/entity/component
authoring, exact component/SDK discovery, module source inspection/repair, and
module build operations needed to construct executable evidence. Validation,
capture, package, audit, and other delivery work remain deferred until a
qualifying runtime inspection executes. Focused TDD proves prerequisite scene
authoring executes while premature packaging does not. The complete Release
engine suite passes 920/920, Studio passes 6/6, and the warning-as-error
solution build reports zero warnings and zero errors. Fresh Benchmark 8 is the
next real-Ollama gate.

Benchmark 6's false-positive assertion path is now a generic executable
coverage contract. The first gameplay checkpoint requires a non-empty input
sequence, an existence assertion for an attached agent-owned `Game.*`
component, and a strict proof of either a nonzero transform delta or changed
agent-owned component state. Existence-only checks and non-strict zero
thresholds return `REKALL_RUNTIME_CHECKPOINT_COVERAGE_REQUIRED` without
executing. Runtime inspection adds generic `delta.component.property` and
`changed.component.property` subjects over initial/final bounded state. The
embedded contract explicitly forbids weakening a failed assertion. Focused TDD
passes 6/6; the complete Release engine suite passes 919/919, Studio passes
6/6, and the warning-as-error solution build reports zero warnings and zero
errors. A fresh real-Ollama Benchmark 7 is the next gate.

The measured `Lumen Vault` failures now have generic TDD coverage. Runtime
entities expose typed number/boolean/string component readers and immutable
writers without `JsonObject`; SDK inspection returns an exact transform/state
recipe plus a scalar two-axis/double-math recipe; the runtime-system scaffold
uses typed entity/world helpers rather than rebuilding `world.Entities`; and
both runtime and playable scaffolds now fail with an executable source-edit
diagnostic instead of overwriting existing agent work. The embedded contract
also fixes scalar semantic-action and numeric-type guidance. The combined
authoring-contract selection passes 29/29. The full engine suite passes
911/911, Studio passes 6/6, and the warning-as-error solution build reports
zero warnings and zero errors. The next gate is another fresh empty-project
game, not subsystem expansion.

The benchmark-3 false positive is now converted into an executable evidence
contract. `rekall.runtime.inspect_scene` accepts representative input frames
and up to 64 generic assertions over entity existence/visibility, attached
components, component properties, final transforms, and position deltas. It
returns bounded actual values and fails with
`REKALL_RUNTIME_ASSERTION_FAILED` when authored behavior is absent. Task-
specific agent sessions that author runtime systems cannot complete without a
fresh successful assertion-bearing inspection after the latest scene/module
mutation; CLI and MCP share the same contract. Focused runtime/agent/CLI tests
pass, and the malformed benchmark package is independently rejected by the
new CLI assertion path. The full engine suite passes 915/915, Studio passes
6/6, and the warning-as-error solution build reports zero warnings and zero
errors. Benchmark 4 then remained the next installed-agent gate.

Benchmark 4's late-test failure is now converted into a generic repair-loop
contract. The embedded agent receives an immediate first runnable gameplay
checkpoint after the first successful runtime-module build, before polish,
cleanup, packaging, or capture. A failed assertion-bearing inspection injects
the bounded actual values as repair evidence and unlocks a protected 12-turn
repair/retest reserve instead of ending at the ordinary turn limit. Runtime SDK
inspection, scaffold comments, and the embedded prompt now state that input
helpers do not create bindings—agents must author `Rekall.InputActionMap`—and
that every attached/read/written agent component must be registered. A
simulated end-of-budget failure now repairs source, reruns assertions, and
completes inside the reserve. The focused contract selection passes 20/20; the
full engine suite passes 917/917, Studio passes 6/6, and the warning-as-error
solution build reports zero warnings and zero errors. Benchmark 5 against a
fresh distribution is next.

Benchmark 5 proves that prompting alone is not a sufficient gameplay-testing
contract for the current local model. The agent loop now enforces the first
executable checkpoint after a successful agent-authored runtime-module build:
unrelated validation, discovery, polish, capture, and packaging calls are not
executed and return `REKALL_RUNTIME_CHECKPOINT_REQUIRED` until the model calls
`rekall.runtime.inspect_scene` with representative input and a non-empty
assertions array. Empty-assertion inspections return
`REKALL_RUNTIME_ASSERTIONS_REQUIRED`. A failed assertion remains direct repair
evidence and activates the protected repair/retest reserve. Focused agent tests
pass 16/16; after one concurrent 250-ms isolation-test timeout, the exact test
passed alone and the complete engine suite passed serially at 918/918. Studio
passes 6/6 and the warning-as-error solution build reports zero warnings and
zero errors. A fresh real-Ollama Benchmark 6 is the next gate.

`Echo Foundry` is the first uninterrupted empty-project task-specific game
creation pass. Local Ollama `qwen3.5:35b` authored the 3D industrial arena,
semantic controls, four resonators, HUD, delta-time movement/contact/score/reset
runtime system, and separate generic playable proof adapter; repaired compiler
and visual-composition evidence; reached zero validation issues; built and
trusted both modules; packaged the Windows game; repaired an initially
one-color-dominated proof frame through ordinary scene authoring; and passed the
consolidated package audit in one 49-turn session. Studio captured a 960x540
viewport with seven renderables. The session's engine work and audit were
successful, but automation initially reported false because its evidence
collector searched only `Builds` while the agent correctly used
`Output/Packages`. Package discovery is now bounded to 1,024 project-local
directories and 256 archives, skips reparse points, and verified the unchanged
archive in a four-turn audit-only run. Package SHA-256:
`03F8BFB1E0D7CC09E9D1CD2EB11FEACB48DA1C2CA7CA8B6972D205CD502E9976`;
Studio viewport:
`A5B27F145862D62E88AF25A3665E8D7E767BF663398CE5423EF8BB4A2CB9D66A`;
package-audit frame:
`D86CE63E2BFB4780E28C8517724D9264DB13B9DB333C64D67441FDFA1421F3FB`;
collector evidence:
`A88A17CCF4D5F4F5A231DF69C35CAF0B4AC78ED20B0E4042597C7892EAED3EA8`.
Current verification remains 908/908 engine tests, advances to 6/6 Studio
tests, and the full solution builds with zero warnings and zero errors.

The task-specific `Signal Garden` checkpoint is accepted from direct repository
evidence. Starting from an empty project, local Ollama `qwen3.5:35b` authored a
3D night garden, semantic input, an agent-owned delta-time world gameplay
system, bloom activation/score/reset logic, and HUD content. Its first 64-turn
run produced the authored scene and compiling gameplay but stopped with stale
module evidence before packaging. A bounded continuation preserved that game,
added the generic playable package-proof adapter, built both modules, reached
zero validation issues, captured a 960x540 Studio viewport with 16 renderables,
packaged the player, and passed consolidated audit in 18 turns. The package
SHA-256 is
`65A2ABFD5B1B79D5CB28C9E5D8C45C83A1AB70C7CEF36D3B57E2AC18C4C0CBD0`;
the Studio viewport SHA-256 is
`C268B21F353DA442FEF869108861C6B8E007505C7D36242430540A2B5BDE0CB5`;
the package-audit frame SHA-256 is
`C0D8367533FBDA1628DEBD7A30A7759A31FBE10B2438EADFC224BA5C61F0D49F`;
and the continuation evidence SHA-256 is
`DED324F74DD1375C7CF359977B2D2A5533F9BA8E5E253FF3288DB532708FCA4D`.

Project agent sessions now supply their already-owned project root and active
scene to native tools when those scope fields are omitted, while still
rejecting explicit out-of-scope paths. Runtime scene inspection has a safe
one-frame default. The embedded contract now establishes the generic playable
module as an early deterministic package-proof adapter while keeping actual
world gameplay in the agent-authored runtime system; final builds therefore no
longer become stale from late adapter scaffolding. Current verification is
908/908 engine tests, 5/5 Studio tests, and a zero-warning full solution build.

The task-specific `Prism Relay` checkpoint is accepted from direct repository
evidence. Local Ollama `qwen3.5:35b` authored a non-gauntlet 3D game with five
visible renderables, semantic input, an agent-authored delta-time C# gameplay
module, a playable adapter, and an inspectable HUD. The final project has zero
validation issues, both module projects build, both receipts verify as
`windows-appcontainer-restricted`, and the consolidated playable-package audit
passed. Studio reports a 960x540 nonblank runtime viewport. The final
continuation completed in three turns and two tool calls after a canonical tool
search. The package SHA-256 is
`E17A9F9830276FF940CD7082F35DABBFDE8AF6D26271CE69CCACDED59B01583E`;
the Studio viewport SHA-256 is
`13CC2332D50127370DABEE528FC824AFCBC398C3E187BDEB3CBED7C5A2CAC2B0`;
the package-audit frame SHA-256 is
`06192B3845A53BE229110C60B1050BE97166F3098A2ABC660A1B0EF099F58AFB`;
and the final evidence SHA-256 is
`4427B355EA5242BECE210F0AF311145BD89649DCB58E9D8A7BBFA7363F207AF8`.

The generic authoring contract now exposes exact queryable runtime SDK method
signatures and source-topology/build rules through
`rekall.module.inspect_runtime_sdk`. Progressive discovery exposes matched
native tools directly instead of steering the model through the compatibility
gateway. The immutable module SDK gained generic `RemoveEntity`; successful
playable-package audits prime the next evidence-backed completion while any
later tool call invalidates that proof; and Studio automation can safely resume
an existing project. These behaviors are covered by the current 907/907 engine
and 5/5 Studio test suites, and the full Debug solution builds with zero
warnings and zero errors.

Implement the persisted compatibility design: central project/scene schema
enforcement, deterministic read-only inspection, and explicit atomic legacy
migration. Package, module SDK, receipt, animation, and diagnostic versions
remain intentionally separate contracts. The module trust boundary and desktop
recovery paths remain installed-product verified.

Compatibility Task 1 is verified at 14/14 focused tests: project and scene
stores now share a bounded raw schema probe, persist explicit schema 1,
normalize implicit legacy schema 0 only in memory, keep loads read-only, and
fail closed with typed stable codes for malformed, invalid, or future schema
facts.

Compatibility Task 2 is verified at 14/14 focused tests: the recommended
`rekall.compatibility.inspect_project` command now provides bounded, read-only,
manifest-first inspection through direct command, CLI, and MCP surfaces. It
reports current/legacy/future/malformed/missing states, exact versions and
codes, migration eligibility, blockers, limitations, and next actions without
executing project code or changing source bytes. Oversized/excessive inputs and
reparse traversal fail closed.

Compatibility Task 3 is verified in a 37/37 combined focused selection:
`rekall.compatibility.migrate_project` is
available through direct command, CLI, and MCP with dry-run as the default and
explicit `--apply`. It stages all outputs before replacement, rechecks source
bytes, durably preserves exact originals and hashes, keeps unknown extension
data, records transaction preimages, rolls back partial replacement in reverse
order, rejects reparse-backed engine state, and retains five backup sets without
following reparse paths. Future or malformed inputs remain untouched.

Compatibility Task 4 is installed-product verified: policy and CLI/MCP
workflows are documented, Debug and both Release passes complete at 683/683,
and shipped-binary positive and negative migration proofs passed alongside the
unchanged installed product matrix.

Archive security Tasks 1-4 are complete. Inspection and extraction share one
bounded metadata-first immutable ZIP plan; extraction is exact-length,
reparse-aware, transactional, and cannot publish partial destinations. The
trust boundary and exact limits are documented, and the installed gate includes
a safe negative duplicate-manifest fixture.

Animation state graph Task 4 is installed-product verified. Fresh shipped
binaries authored a genre-neutral two-clip graph, captured an informative idle
frame, changed only its generic `phase` parameter, inspected `active` with
`previous=idle` at exactly 0.500 transition progress, and captured a distinct
informative active frame. The frame SHA-256 values are
`E17ABB6DAE0EDD3963D775617A0FBCADD38E8AE5FCD5E13AE9A52475B3BDC7E4` and
`DC7D7EEB7133226AEA816A7DF24DEEE30C10ABBB15C1881119CBB709F3B405E4`.
Debug passed 738/738 in 2m20s; the zero-warning, zero-error locked Release build
passed 738/738 twice in 2m20s and 2m17s. The unchanged installed product matrix
passed, including all recovery outcomes. Its 600-frame soak simulated exactly
10 seconds at 4,264.1 FPS with 681,624 retained bytes and all nine checks. The
1,149-payload-file archive is 195,141,113 bytes with SHA-256
`7297CE4FCF52960F3217BE6A80CF7046E8052F9A3E12998602C807C0DA9A426D`.

## In progress

The current item remains the actual AI game-creation loop. The proven
`qwen3.5:35b` is restored locally as a 23 GB Ollama model. Run a fresh unchanged
benchmark through that real local model
and independently inspect its scene, source, input projection, and runtime
transitions. Require clean validation, informative
capture, compiled agent-authored behavior, a playable relocated package, and a
passing consolidated audit. Only after this generic loop is honestly green,
run a fresh Pong brief as the compact fully playable proof before using Galaga
as the broader multi-entity gameplay benchmark. Use concrete failures to
improve only generic authoring primitives, schemas, diagnostics, and repair
efficiency. Studio, embedded AI, MCP, CLI, and packaged players must continue
to consume the same contracts.

Fresh installed Lumen Vault benchmark 17 is retained as an honest failure.
Real local `qwen3.5:35b` used 76 bounded turns (including the protected repair
reserve), with 54 successful and 22 failed tool executions. It compiled an
agent-authored runtime system, declared semantic input, produced an eight-entity
runtime scene and a nonblank 960x540 Studio viewport with six visible
renderables, but never passed the strict movement checkpoint and therefore did
not package. The measured cause is a generic contract mismatch: runtime modules
consume semantic actions, while deterministic input frames expose only raw
device facts. The model supplied intuitive `move_horizontal` and
`move_vertical` fields; deserialization discarded them and all projected action
values stayed zero. The current plan adds bounded typed semantic-action
injection and rejects ineffective checkpoint inputs with a copyable repair
shape before rerunning the unchanged game brief.

The semantic runtime-input tranche is now implemented and installed-product
verified. Runtime input frames expose bounded typed semantic action samples;
samples override raw projection only for exact actions declared by an active
`Rekall.InputActionMap`, undeclared samples remain isolated, raw device input
continues to work, and invalid duplicates/bounds fail with structured errors.
The MCP schema, runtime command description, embedded agent contract, and
checkpoint preflight all expose the same copyable shape. Unknown flat action
fields no longer count as evidence and are rejected before tool execution.
The focused runtime/agent/MCP selection passed 27 tests. The zero-warning,
zero-error Release solution passed 971 engine and 7 Studio tests, and both
locked distribution passes repeated 971/971 and 7/7. All installed-product
acceptance checks passed against the 1,186-file Windows distribution; its
201,512,801-byte archive has SHA-256
`5884DEEE2A9010904C113FFE3CD32FA4143459D5E269D5611385D3E0944BBFF4`.

Fresh installed Lumen Vault benchmark 18 is retained as the next honest
failure. The unchanged brief and real `qwen3.5:35b` used 74 tool executions
(48 successful, 26 failed), compiled the agent-authored runtime system, and
produced a nonblank 960x540 viewport with 11 renderables. Crucially, all six
runtime checkpoints now used the exact typed `semanticActions` payload, proving
the benchmark-17 blocker is removed. The run still ended at the turn limit
without packaging because AGE accepted `Rekall.InputActionMap.Actions` as a
JSON-encoded string rather than an actual array. Runtime consequently reported
zero input actions and the model repeatedly revised otherwise executable C#.
The next generic target is fail-closed structured component-property authoring
and a direct runtime diagnostic with the exact valid action-map shape; no
Lumen-Vault-specific behavior belongs in the engine.

The structured component-authoring tranche is now implemented and
installed-product verified. Component schema and mutation guidance require
native JSON arrays/objects and give an exact semantic binding example. Runtime
emits bounded error observations for malformed action maps and injected action
names absent from active maps. Project validation blocks structured CLR array
properties stored with the wrong JSON shape, losslessly parses an encoded
array/object when possible, and supplies its ordinary
`rekall.component.set_property` repair so the bounded project repair workflow
can reach zero issues without hand-editing files. The full Release solution
passed 975 engine and 7 Studio tests; the locked distribution repeated 975/975
and 7/7 twice with zero warnings and zero errors. All installed-product checks
passed against the 1,186-file distribution. Its 201,521,262-byte archive has
SHA-256
`8540332C94F139382D5AAE0BD5BB1AD31696839E4E48E84AADD12B317B902DB8`.

Fresh installed Lumen Vault benchmark 19 is retained as the next honest
failure. The unchanged brief and real `qwen3.5:35b` used 65 tool executions
(39 successful, 26 failed), compiled an agent-authored runtime system, and
produced a nonblank 960x540 viewport with 17 renderables. Native structured
binding authoring worked: runtime exposed 13 action projections rather than
the zero actions in benchmark 18. Independent one-frame replay proved injected
`move.horizontal=1` reached all matching declarations. Movement still remained
zero because the authored module called `FindEntity(world, "OrbPlayer")`;
AGE's generically named helper only accepted an opaque entity id, returned
null for the exact unique name, and the authored system exited silently. The
next generic target is an unambiguous, observable entity query contract that
preserves id lookup while making unique-name lookup safe and agent-efficient.

The 2026-08-20 capability audit verified that built-in component schemas and
`rekall.module.search_component_schemas` expose exact 2D/3D transform, camera,
renderer, light, rigidbody, collider, world, and material contracts, while
`rekall.module.inspect_runtime_sdk` exposes immutable entity/component helpers,
semantic input, generic events/observations, camera vectors, and typed
`Raycast2D`/`Raycast3D` queries.
Runtime inspection, viewport capture diagnostics, validator repair actions,
and MCP command schemas provide executable evidence. Persistent simulation,
angular state, BEPU-native material response, and bounded physics telemetry are
now verified. Further physics breadth should be driven by the real Qwen
benchmark, with likely candidates being exact contact evidence, collision
filtering, constraints, or authored angular control rather than genre behavior.

The programmable-rendering architecture's executable-material plan is complete.
Tasks 1-6 are verified:
existing agent-visible shader authoring and assignment metadata now resolves to
reflected, content-addressed, ABI-validated shader assets, and incompatible
pairs cannot alter a scene, authored shader identity reaches each GPU draw,
and native Vulkan capture executes the selected project pipeline with measured
pixel proof. The windowed Windows player executes the same authored sources and
retains its last valid pipeline across broken live edits. Agent inspection,
dependency-inverted validation, referenced-source packaging, relocation, and
semantic package audit are verified. The declared frame/draw/material resource
ABI is now common to both GPU backends, and the retained example proves native
hardware, Windows player, package relocation, and audit. Custom post-processing, dynamic
geometry, and typed GPU resources are
separate subsequent tranches; the first post-process proof will be an
agent-authored raindrop shader rather than an engine rain feature.

AI game-creation Tasks 1-3 have a verified functional checkpoint. A new
UI-independent workbench session creates projects
and scenes through canonical commands, opens and switches scenes, executes
dynamic registered commands, appends their transactions, reloads external
agent changes, refreshes the canonical read model, preserves the last valid
model on failure, and carries explicit entity selection into the structured
inspector. Studio consumes that session and the centralized default command
registry; it can create/open, select, mutate arbitrary generic components and
JSON properties, validate, capture a 960x540 software viewport after edits,
and own a real Windows player process. Unexpected async command failures are
reported in-product instead of escaping the UI dispatcher. Transactional
undo/redo, scene switching, package/audit actions, and embedded Ollama
authoring, schema-guided property editing, deterministic WPF view-model
automation, and installed Studio-to-agent automation are verified.

The fresh installed-product checkpoint completed a zero-warning locked Release
build, two independent 894/894 Release suites, four self-contained publishes,
and distribution assembly. The shipped acceptance then passed the generic
agent-authoring gauntlet, clean package relocation/audit, nonblank capture,
Windows play and the broader production matrix. The 1,178-file distribution
archive is 200,832,939 bytes with SHA-256
`4553BF616B31461BCEF11679DA66177B46929B15829FB5F39CF00FED5FFC9D6D`.

The reusable project agent session uses the provider-neutral language-model
agent, local Ollama adapter, progressive MCP executor, and shared default
command catalog. It exposes model listing and bounded live progress, treats
project-root scope violations as failed executions even when a model claims
completion, and scopes both direct arguments and JSON-string gateway
envelopes. Studio exposes model selection, task input, Run/Cancel, and a
bounded transcript, then reloads, validates, and recaptures authored state.
The first real model run exposed inefficient malformed blueprint and camera
composition attempts; schema descriptions now explicitly separate camera and
light configuration from Transform3D pose. The repaired proof is clean and
visually informative, but it is a scene-authoring proof rather than the final
playable/package installed-game acceptance.

The first complete-game attempt exposed a separate completion-control defect:
after a passing gauntlet the model performed an unnecessary second audit and
continued editing until its turn limit. The provider-neutral agent request now
supports explicitly configured terminal-success tools, including gateway-
wrapped targets. The Studio project session configures only the generic
agent-authoring gauntlet as terminal; ordinary tools still require the normal
completion audit. A fresh real rerun stopped immediately after the passing
gauntlet and returned success.

The restricted module-host tranche is paused at commit `4e43119`, a stable
native containment and typed-broker checkpoint. Project-write denial and
64-KiB stderr-drain proof are also locally verified and will be preserved
before the authoring tranche begins. Memory-limit classification, ten-pass
timing, installed hostile fixtures, production consumer cutover, and shipped
worker packaging remain explicitly unfinished and must be resumed after the
game-creation loop is usable.

The next audit-driven tranche is a restricted host for agent-authored C#
modules. The selected Windows-first architecture keeps the existing generic C#
SDK, verified receipt admission, and runtime priority semantics, but moves all
project-assembly execution and reflection into a no-network AppContainer worker
with kill-on-close, one-process and memory job limits, bounded framed IPC,
timeouts, and no silent in-process fallback. The reviewed design is
`docs/superpowers/specs/2026-08-20-restricted-module-host-design.md`, with the
TDD sequence in
`docs/superpowers/plans/2026-08-20-restricted-module-host.md`.

Restricted module host Task 1 is verified in 42 focused module/build/CLI tests
after the complete 855/855 Debug suite. New schema-2 module receipts require the
`windows-appcontainer-restricted` execution posture; legacy, empty, and unknown
postures fail with `REKALL_MODULE_RECEIPT_HOST_POSTURE_MISMATCH` plus an
executable rebuild action. The generic protocol layer now supplies typed
initialize/runtime/playable contracts and versioned little-endian JSON frames
with exact 64 MiB message and depth-128 bounds, strict monotonic sequences,
duplicate-field rejection, cancellation preservation, stable coded failures,
and adversarial coverage for malformed, truncated, oversized, invalid UTF-8,
unknown-version/operation, inconsistent response, and typed-payload cases. The
next witnessed-red slice is the deterministic worker server; no production
consumer has been cut over yet.

Restricted module host Task 2 is verified by 23 focused protocol/worker tests,
a zero-warning Debug solution build, and the complete 866/866 Debug suite. The
new `Rekall.Age.ModuleHost.exe` runs a persistent single-request session over
protocol-only standard output, independently rechecks its confined load plan
and every artifact hash, discovers ordered system IDs/priorities and component
schemas, retains playable state, and executes typed runtime/playable calls with
source-generated JSON metadata. A real child-process test completed finite
initialize/shutdown framing with clean stderr. Adversarial proof covers calls
before initialization, duplicate initialization, unknown systems, traversal,
post-plan DLL mutation, sequence violations, a 5,000-character module throw,
and non-JSON `NaN` render output; failures are bounded, coded, stack-free, and
terminate the session. This is still an ordinary diagnostic worker until Task
3 adds immutable staging, AppContainer launch, explicit handle inheritance,
job limits, timeouts, and broker lifecycle ownership.

Restricted module host Task 3 staging is verified in an 18/18 combined
worker/staging selection. The broker now admits a product/protocol-matched host
manifest, copies only manifest-verified worker files and receipt-verified
module artifacts into a unique session tree, rechecks source and destination
size/SHA-256 around every copy, writes the confined load plan, marks all staged
files read-only, and removes the exact session tree after success or failure.
Tests prove source, project files, PDBs, build receipts, and unmanifested host
files do not cross the boundary; altered host/project artifacts leave no
session tree. Windows alias forms including alternate data streams, device
names, duplicate separators, and trailing-dot paths are rejected before copy.
The next active slice is AppContainer SID/ACL creation and job-bounded native
process launch; staging alone is not treated as sandbox activation.

Restricted module host Task 3 now has a verified native-containment checkpoint.
The launcher creates or derives the stable no-capability AppContainer profile,
grants read/execute only to the immutable staged tree, inherits exactly three
protocol pipe handles, starts suspended, assigns a kill-on-close job before
resume, limits the job to one process and 512 MiB, and supplies a deliberately
  scrubbed, alphabetically sorted Unicode environment instead of inheriting
  broker secrets. A 37/37 module-host selection and zero-warning Debug solution
  build pass; six of those tests are native Windows integration cases. They prove typed broker
initialize/playable calls, a 250 ms fail-closed hang deadline, exact abrupt
crash reporting, absent injected environment secrets, and denial of an
unstaged sentinel read, child-process creation, and loopback networking. The
broker owns staging/profile/process disposal and distinguishes crash from
timeout without exposing module diagnostics. Remaining Task 3 work is the
project-write, memory-limit, excessive-stderr, repeated-timing, orphan-process,
and orphan-staging matrix plus final stable-code consolidation; no runtime,
playable, schema, Studio, CLI, or MCP consumer has been cut over yet.

The completed persisted-document recovery tranche began because atomic
publication and optimistic revisions now prevent torn and stale engine writes,
but storage damage or external/manual corruption still blocks a project. The
reviewed design retains one exact previous validated project/scene version,
adds bounded read-only recovery inspection, and requires an explicit
revision-guarded restore that quarantines the damaged bytes. Normal loads never
silently roll back. Design and TDD sequence:
`docs/superpowers/specs/2026-08-18-persisted-document-recovery-design.md` and
`docs/superpowers/plans/2026-08-18-persisted-document-recovery.md`.

Persisted document recovery Task 1 is verified at 13/13 focused persistence
tests. Conditional publication can atomically retain the exact prior bytes at a
distinct same-volume recovery path while replacing the live document. Repeated
success replaces the recovery snapshot with exactly the immediately preceding
version; stale writes preserve both live and recovery bytes; creation does not
fabricate history; existing cancellation, busy, size, and cleanup guarantees
remain green.

Persisted document recovery Task 2 is verified in a 46/46 focused
atomic/project/scene selection and the complete 830/830 Debug suite. Successful
conditional manifest and scene replacements retain exactly the immediate prior
bytes under a confined `.rekall/recovery` path. Read-only inspection reports
primary/previous availability, exact revisions, schema/shape status, stable
codes, recoverability, and a next action. Explicit restore validates the prior
snapshot, requires the caller's current revision, atomically restores exact
bytes, quarantines the displaced document with its revision, and retains at
most four deterministic corrupt artifacts per document. Malformed prior data
and escaping scene names fail closed; normal loads never silently fall back.
That verified store is the foundation consumed by the portable agent commands,
CLI routes, and MCP tools described in the next milestone.

Persisted document recovery Task 3 is verified in the complete 92/92
agent/MCP/CLI selection and the full 840/840 Debug suite. Generic
`rekall.recovery.inspect_document` and `rekall.recovery.restore_document`
commands target either the manifest or one named scene, expose portable MCP
schemas, preserve read-only inspection, require an exact inspected revision for
restore, return executable ordinary validation actions, and report stable
failure codes plus a fresh inspection action after conflicts. CLI project and
scene routes use the same registry commands. Direct, CLI, and JSON-RPC MCP tests
damage real documents, observe structured recovery facts, explicitly restore,
and successfully perform an ordinary scene mutation afterward. A wider test
found the engine-status payload crossing its 12,000-character agent-efficiency
boundary; the top-level map was curated to retain high-priority recovery
discovery while leaving low-level render-plan execution available through tool
search. Those portable contracts are now included in the installed product
gate recorded in the next milestone.

Persisted document recovery Task 4 passed the complete product gate. The fresh
locked Release build completed in 8.78 seconds with zero warnings and zero
errors; two independent Release passes completed 840/840 in 1m26s and 1m24s.
The shipped CLI authored a scene, retained a prior version, then had its live
scene deliberately malformed. An ordinary scene-summary command failed with
exact code `REKALL_DOCUMENT_JSON_MALFORMED`; recovery inspection reported a
valid previous version and exact damaged revision; a stale restore failed with
`REKALL_DOCUMENT_REVISION_CONFLICT` without changing damage; and the exact
restore quarantined one byte-identical damaged file, passed ordinary validation
with zero issues, and accepted a normal post-restore entity mutation. It left
zero temp/lock controls. The unchanged installed product matrix passed. Atomic
JSON acceptance parsed 5,767 snapshots with two bounded transient opens, zero
malformed snapshots, and zero temp files. Soak completed 600 frames and exactly
10 seconds at 4,320.2 FPS with 713,600 retained bytes and all nine checks. The
1,149-payload-file archive is 195,355,222 bytes with SHA-256
`8837F18945FDCEB4622DE5072D4A5FE0C518B2AE61B7F8A29E3E8527DFDD64CE`.
One-version rollback, not autosave/history/merge or external backup, remains the
explicit supported boundary.

The next risk-driven tranche is optimistic document revisions. Atomic files
eliminate torn reads but do not prevent two valid agent/editor processes from
silently overwriting one another. The reviewed design adds exact snapshot
revision tokens, bounded cross-process compare-and-publish, stable conflict
diagnostics, conditional project/scene mutations, and serialized transaction
append. It does not claim automatic content merge or collaborative-editing UX.
Design and TDD sequence:
`docs/superpowers/specs/2026-08-18-optimistic-document-revisions-design.md` and
`docs/superpowers/plans/2026-08-18-optimistic-document-revisions.md`.

Optimistic document revisions Task 1 is verified at 10/10 focused persistence
tests. Every immutable snapshot exposes a deterministic lowercase SHA-256 token.
All cooperating atomic writers now take a cancellable bounded sibling lock;
conditional publication compares under that lock and either publishes the
complete file or returns exact `REKALL_DOCUMENT_REVISION_CONFLICT` /
`REKALL_DOCUMENT_BUSY` codes without changing the destination. Two writers
using one revision produced exactly one winner and one stale rejection, with no
temporary or engine-owned lock debris.

Optimistic document revisions Task 2 is verified in a 57/57 combined
project/world/compatibility/transaction selection plus a 12/12 project rerun.
Project and scene stores expose versioned loads and conditional saves without
changing persisted JSON. Every ordinary capability/entity/component/blueprint
mutation saves against the loaded or explicitly supplied `expectedRevision`;
creation now requires the explicit missing revision and cannot overwrite an
existing project or scene. A dynamic stale entity mutation returned exact code
`REKALL_DOCUMENT_REVISION_CONFLICT` with expected/current recovery facts while
preserving the intervening entity.

Optimistic document revisions Task 3 is verified in a 69/69 combined
agent-context/MCP/transaction/level-design/geometry selection. Compact project
and scene summaries expose their exact 64-character revisions, while MCP
schemas expose optional `expectedRevision` without making it mandatory for
ordinary semantic operations. A wider source audit converted generic
level-design, KSA import, geometry, prefab, parenting, grid, and virtual-geometry
scene mutations to conditional publication. Thirty-two simultaneous distinct
transaction appends retained all 32 entries through bounded conflict/reload
retries and left no engine-owned control files.

Optimistic document revisions Task 4 passed the complete product gate. Debug
passed 818/818 in 1m24s; the clean locked Release build had zero warnings and
zero errors, and two independent Release passes completed 818/818 in 1m25s and
1m27s. The first Release attempt exposed a transient Windows replace/open
window in the existing reader stress test; snapshot acquisition was hardened to
a bounded 64-attempt ceiling and the exact Release stress passed 10 consecutive
reruns before the complete gate restarted from zero. Shipped binaries exposed
an exact scene revision, rejected a stale mutation with
`REKALL_DOCUMENT_REVISION_CONFLICT`, exposed a changed revision, accepted the
refreshed retry, retained both valid entity edits and both audit entries, and
left zero lock/temp controls. Atomic acceptance concurrently parsed 5,784
complete documents with three tolerated transient opens and zero malformed
documents. The installed matrix passed; its 600-frame soak simulated exactly
10 seconds at 4,259.3 FPS with 712,576 retained bytes and all nine checks. The
1,149-payload-file archive is 195,300,134 bytes with SHA-256
`A222BB5ACD590E796F1CEEB920FAF96FBB5C509604273E5D360450DAE60B3005`.
Automatic content merge, CRDTs, and collaborative-editor conflict UX remain
explicitly outside this tranche.

Atomic persisted JSON was selected as the next risk-driven tranche. Code inspection found
that project and scene loads schema-probe one file handle and then reopen the
path for typed deserialization, while their saves write directly to the live
file. The same direct-write pattern exists in the asset catalog/pipeline,
prefab, render-plan, and transaction-log stores. The reviewed design now fixes
one bounded immutable read snapshot, consistent parse depth, durable
same-directory atomic publication, failure cleanup, and installed concurrent
reader proof. It explicitly does not claim multi-writer merge semantics or a
restricted host for full-trust C# modules. Design and TDD sequence:
`docs/superpowers/specs/2026-08-18-atomic-persisted-json-design.md` and
`docs/superpowers/plans/2026-08-18-atomic-persisted-json.md`.

Atomic persisted JSON Task 1 is verified at 5/5 focused core tests. A shared
bounded snapshot reads one exact byte sequence from one handle, rejects size
overflow before the document allocation, and detects short/changed reads. A
shared UTF-8-without-BOM publisher stages beside the destination with
`CreateNew`, write-through flushes the complete payload, replaces only after
successful staging, preserves existing bytes on cancellation/failure, and
cleans recognizable temporary siblings.

Atomic persisted JSON Task 2 is verified in a 50/50 combined
project/scene/core/compatibility/transaction selection. Schema validation and
typed deserialization now consume the same immutable bytes at one shared depth
limit of 128; depth 80 loads consistently and depth 129 fails with typed code
`REKALL_DOCUMENT_JSON_MALFORMED`. Project and scene saves use durable atomic
publication. A four-reader/50-write scene stress test observed only complete
128-entity documents and passed five additional repetitions. Windows existing
files use `File.Replace`; snapshot opens allow delete sharing and retry only a
small bounded transient replacement window.

Atomic persisted JSON Task 3 is verified in a 92/92 combined
asset/level-design/render-plan/transaction selection. Asset catalog, asset
pipeline, prefab, render-plan, and transaction-log stores now share an explicit
64 MiB and depth-128 policy, deserialize one bounded snapshot, and publish only
through the durable atomic writer. Cross-store round trips preserve existing
shapes; a sparse 64 MiB+1 catalog fails before JSON allocation; depth-80 render
metadata loads consistently; and successful writes leave no temporary siblings.
At this milestone transaction append was still last-writer-wins; optimistic
document revisions Task 3 later closed that audit-history loss window through
bounded compare/reload retries without claiming content-merge semantics.

Atomic persisted JSON Task 4 passed the complete product gate. Debug passed
806/806 in 1m25s; the locked zero-warning, zero-error Release build passed
806/806 twice in 1m25s and 1m24s. A fresh installed CLI performed 20 capability
mutations and 40 entity mutations while an independent process repeatedly
opened and parsed the live project, scene, and transaction documents. It parsed
5,783 complete snapshots, tolerated one bounded transient replacement-window
open miss, observed zero malformed documents, and found zero leaked temporary
siblings. The unchanged installed matrix passed; its 600-frame soak simulated
exactly 10 seconds at 4,365.4 FPS with 703,760 retained bytes and all nine
checks. The 1,149-payload-file archive is 195,258,607 bytes with SHA-256
`2E05246183D9A65F3CF250DECFD5BAF713946255E0BA8FEFE3D7CDD19402F456`.
This proves atomic publication and immutable-reader safety, not multi-writer
merge: independent simultaneous writers remain explicitly last-writer-wins.

The bounded cubic interpolation tranche is installed-product verified. The
bounded morph-target tranche is also installed-product verified. Morph target
Task 1 passed a 51/51 focused runtime/schema/CLI selection.
`Rekall.MorphWeights` exposes one bounded, non-clamped generic array and reuses
ordinary linear/cubic clips and state-graph catalog clips. A post-animation
runtime system rejects empty, excessive, non-numeric, nested, non-finite, and
out-of-range input; removes stale state; preserves exact negative and
extrapolated values; and publishes sorted bounded `Rekall.MorphState`
projection. Split execution matches continuous execution. Runtime CLI
inspection reports counts and invariant-culture weights without vertex data.
Authored modules remain responsible for game behavior.

Morph target Task 2 is verified in a 24/24 combined asset/loader/skeletal
selection. Asset reports expose ordered names, separate mesh defaults and node
overrides, supported POSITION/NORMAL semantics, and explicit limitations. The
loader carries exact aligned deltas and resolved defaults through node
translation/rotation/scale and index remapping, excluding translation from
deltas. It rejects more than 64 targets, more than 4,194,304 declared vectors,
bad counts/strides/defaults/names, non-finite or excessive values, TANGENT,
sparse/quantized accessors, missing base normals, and incompatible compound
layouts before returning partial meshes. Existing plain and skeletal GLB paths
remain green.

Morph target Task 3 is verified across a 117/117 viewport/Vulkan/asset/CLI
selection, followed by a 4/4 inspector smoke rerun after count hardening. Render
projection consumes runtime-only validated state. CPU preparation applies exact
signed weights and normalized normals before skeletal matrices, atomically
falls back to imported defaults on count mismatch, and prevents non-finite or
out-of-float-range values reaching GPU buffers. The generic bounded
`rekall.render.inspect_scene_mesh_geometry` command and `render mesh inspect`
CLI use the same prepared meshes as Vulkan and report post-morph/post-skin
counts, weight source, and finite bounds without vertex dumps. The real fixture
produced exact bounds `(8.5,21,30)` through `(10.5,23,30)`.

Morph target Task 4 passed the complete product gate. Debug passed 792/792 in
1m27s; the locked zero-warning, zero-error Release build passed 792/792 twice
in 1m26s and 1m24s. Shipped binaries imported the real two-target GLB as
`wide,raised` with mesh defaults `[0.25,-0.5]`, sampled generic cubic authored
weights to `[0.75,0]` at frame 30, and reported final post-morph bounds
`(8,21.5,30)` through `(10,23.5,30)`. Native Vulkan captures were informative,
hardware accelerated, free of fallbacks/issues/observations, and changed from
SHA-256 `D97998D4615E2B707B22C0D7137FB84C7C7C26086789B05205A2616D8C07A503`
to `57D4F1735ED1B04F3C8B4AD4A5E481C880C3F59183563D7EFA4F07880D7B32D3`.
The installed matrix passed; its 600-frame soak simulated exactly 10 seconds
at 4,312.7 FPS with 709,392 retained bytes and all nine checks. The
1,149-payload-file archive is 195,236,150 bytes with SHA-256
`CB0DA6560A1422BE5DE7F99182A4651170C6CE397B912762875E1E7BCDF1FE0A`.
Native glTF weight animation and TANGENT/sparse/quantized or incompatible
compound morph layouts remain explicit unsupported boundaries.

That decision is now fixed in
`docs/superpowers/specs/2026-08-18-morph-target-runtime-design.md`: a bounded
`Rekall.MorphWeights` component reuses ordinary clips/mixers/graphs, glTF
POSITION/NORMAL deltas remain aligned through chunking, CPU deformation occurs
before skinning, asset/runtime counts fail closed, and native glTF `weights`
channels remain an explicit follow-up rather than partial hidden support.
The TDD implementation sequence is tracked in
`docs/superpowers/plans/2026-08-18-morph-target-runtime.md`.

The cubic interpolation design is fixed in
`docs/superpowers/specs/2026-08-18-cubic-animation-interpolation-design.md`:
authored clips and glTF `CUBICSPLINE` share duration-scaled Hermite semantics,
fail closed on unknown modes or malformed/non-finite tangent data, preserve
exact endpoints, and keep morph targets outside this focused tranche.
The executable TDD sequence is tracked in
`docs/superpowers/plans/2026-08-18-cubic-animation-interpolation.md`.

Cubic interpolation Task 1 is verified in a 43/43 animation selection. A
focused parser/sampler accepts finite scalar, flat-vector, and RGB/RGBA Hermite
keys, scales tangents by segment duration, preserves exact endpoints, and
clamps color output. Ten adversarial shape/time/value cases fail closed; the
runtime emits bounded target-specific observations without mutation. Unknown
interpolation names no longer silently execute as linear.

Cubic interpolation Task 2 is verified in a 51/51 combined animation/asset
selection. glTF `CUBICSPLINE` output accessors are decoded as standard
input-tangent/value/output-tangent triplets and bounded before runtime use.
Imported translation and scale produce the expected nonlinear midpoint;
rotation output is normalized. Unsupported modes, non-tripled counts,
non-finite records, duplicate cubic times, and near-zero cubic quaternions fail
closed with no invalid pose publication.

Cubic interpolation Task 3 is installed-product verified. Agent schemas expose
the exact four-field cubic key shape, derivatives in units per second, supported
value shapes, and bounds. Debug passed 760/760 in 2m23s; the zero-warning,
zero-error Release build completed in 8.18s and both independent Release passes
completed 760/760 in 2m18s. Shipped binaries reported X 110.0 at frame 30 where
linear would be 80.0 while the graph transition was exactly 0.500. Clean,
informative frames had SHA-256
`38DAB210A0FE5E822F773251EFE18B1B05EF713709F2940813B2F8A99AC3C143` and
`0C9C041274F4063D671D2B9F5ABEBFB0BBC5F6A9E9F8D1AA91D5F86140AAD017`.
The installed matrix passed; its final 600-frame soak reached 4,382.7 FPS with
673,112 retained bytes and all nine checks. The 1,149-payload archive is
195,163,655 bytes with SHA-256
`85CB44D5718825F9F865F7F2FE156ECDE4C325BA5E7DA0573BCADC2DD440204E`.

The next tranche design is fixed in
`docs/superpowers/specs/2026-08-18-animation-state-graph-design.md`: a bounded,
versioned, parameter-driven graph projects into the existing generic mixer,
uses engine delta time, preserves deterministic resume, emits generic state and
transition facts, and keeps all game-specific parameter decisions in
agent-authored content.

Animation state graph Task 1 is verified at 22/22 focused tests. A pure
immutable parser/evaluator now fails closed on malformed, excessive,
non-finite, ambiguously typed, duplicate, or dangling authored graph facts and
selects exact/any, conditional/unconditional, and self-reset transitions in
deterministic order without world, asset, or gameplay dependencies.

Animation state graph Task 2 is verified at 9/9 graph-runtime tests and 50/50
combined animation tests. A pre-animation runtime system projects bounded graph
state into the existing generic mixer, advances only by engine delta time,
supports deterministic reset/resume and noninterruptible cross-fades, emits
bound generic state/transition facts, suppresses conflicting drivers, and
fails closed. Split 17+43-frame execution exactly matches continuous 60-frame
state and output; paused graphs and 64-state clock bounds are explicit.

Animation state graph Task 3 is verified in a consolidated 64/64 selection.
`Rekall.AnimationStateGraph` is discoverable through built-in schemas and MCP
with exact bounded authoring shapes and explicit agent-owned parameter meaning.
Runtime projection and CLI inspection report graph kind, active/previous state,
active clip, transition progress, and bounded layers without unbounded
parameter dumps; existing animation inspection remains compatible.

Archive preflight Task 1 is verified at 15/15 focused tests: a central
metadata-only contract now returns a deterministic manifest-first immutable
entry plan and rejects exceeded bounds, missing/duplicate/oversized manifests,
traversal and Windows-ambiguous names, case/ancestor collisions, and
link/special-file modes with stable codes before opening entry content.

Archive preflight Task 2 is verified with 18/18 focused adversarial tests and
5/5 broad package-integrity tests. ZIP inspection now applies preflight before
manifest deserialization or file-list allocation, reads the bounded unique
manifest and inventory from the immutable plan, hashes only planned files, and
returns exact archive security codes. Valid inspect/run/capture/audit/relocate
paths remain unchanged.

Archive preflight Task 3 is verified with 23/23 focused archive security tests
and 5/5 broad package-integrity tests. Extraction now consumes only the shared
immutable preflight plan, checks destination boundaries for reparse points,
copies every entry to its exact declared length, stages beside the destination,
and publishes by atomic directory move. Invalid preflight cannot create a
destination, existing destinations remain untouched, failures clean staging,
and changed-after-inspection relocation retains its stable diagnostic.

Archive preflight Task 4 is installed-product verified. The full Debug suite
passed 706/706 in 2m18s; the locked Release build had zero warnings/errors and
two independent 706/706 passes completed in 2m18s and 2m17s. Shipped inspect
and audit rejected a duplicate-root-manifest ZIP with exact code
`REKALL_PACKAGE_ARCHIVE_MANIFEST_DUPLICATE`, and rejected audit produced no
output directory. The unchanged installed product matrix passed. Soak completed
600 frames and exactly 10 seconds at 4,449.2 FPS with 693,680 retained bytes and
all nine checks. The 1,149-payload-file archive is 195,083,188 bytes with
SHA-256 `5744CCEEE831BC9C80ABE7F8A2668AA1BE4C570E70106097EE26052368E88B60`.

Genuine web publishing Task 5 (the direct RenderingDevice scene renderer) has
started with its first verified slice: `RekallAgeRenderingDeviceSceneRenderer`
and `RekallAgeRenderingDeviceSceneResources` execute an ordinary
`RekallAgeRuntimeViewportFrame` entirely through the generic
`IRekallAgeRenderingDevice` contract instead of native Vulkan calls. Rather
than duplicating the existing native scene pipeline, it reuses the same
backend-neutral frame/draw projection already used by the Vulkan path --
`RekallAgeVulkanSceneBatchBuilder`, `RekallAgeVulkanSceneDrawPlanBuilder`, and
`RekallAgeVulkanSceneGeometryUploadBuilder` are all pure data transforms with
no native Vulkan dependency -- so both backends share one camera/model/light
projection instead of diverging. The new renderer creates a persistent,
content-hash-cached vertex/index buffer pair, a shared WGSL scene pipeline
(`RekallAgeSceneWgslShaderSource`), a frame uniform buffer, and one
reusable uniform buffer plus binding set per draw slot, then records a real
begin-pass/set-pipeline/set-buffers/draw-indexed/end-pass/submit sequence.
Four focused tests against `RekallAgeInMemoryRenderingDevice` cover a single
draw, two draws with distinct model matrices and separate binding sets,
resource reuse across two identical frames (no growth in
`InspectResources()` between frames), and a rejected empty frame. This slice
covers camera projection, geometry primitives (explicit vertex/index meshes),
transforms, per-draw material/emissive factors, and directional lighting
through vertex color; it does not yet cover texture sampling, depth testing,
skinning/morphing, atmosphere/cloud/water shading, or UI canvas draws, which
remain the next parts of this same task. The focused rendering selection
passed 510/510, the zero-warning/zero-error Release solution built cleanly,
and the complete engine suite passed 1,624/1,629 with the same five
pre-existing failures present on an unmodified tree (a wedged-compiler-timeout
pair in `BuildModulesCommandTests`, one `McpAgentToolExecutorTests` assertion,
one `ProjectRuntimeSystemTests` average-of-empty-sequence failure, and one
`WindowsPlayerRecoveryTests` failure caused by a missing Debug player
executable from a Release-only build); the change set is strictly additive
(three new files, nothing existing modified), so none of the five are
attributable to this slice. Studio passed 53/53. Task 6 (browser input
bridge) is next.

Task 6 (the browser input bridge) is now complete. `RekallAgeWebInputBridge`
converts successive raw `RekallAgeWebInputSnapshot` polls into the same
`RekallAgeRuntimeInputState` the Windows SDL2 player produces: it owns
held/pressed/released edge detection for keys, pointer buttons, and
per-gamepad buttons, releases every held key/button on a focus-loss fact
(matching the Windows player's mouse-capture release), and normalizes browser
`KeyboardEvent.code` values against the same canonical key names the
`Silk.NET.Input.Key` enum produces on Windows (`KeyW` -> `W`, `ArrowUp` ->
`Up`, `Digit1` -> `Number1`, `ShiftLeft` -> `ShiftLeft`, and so on), so one
authored `Rekall.InputActionMap` binds identically in both environments.
`RekallAgeWebInputSnapshotJson` parses the bounded JSON payload the browser
side produces without throwing on missing optional fields. On the JavaScript
side, `web-input.js` (`createWebInputBridge`) captures only raw, unmapped
device facts -- held key codes, pointer position scaled from CSS to canvas
pixels, accumulated pointer/wheel deltas reset after each poll, held pointer
buttons with capture, touch points, and polled Gamepad API state -- plus
stable resize/visibility/fullscreen/device-loss lifecycle event constructors;
it makes no gameplay decisions. `main.js` wires the bridge in, queues
lifecycle facts from `resize`/`visibilitychange`/`fullscreenchange`, and
exposes `input.snapshot`/`input.pullLifecycleEvents` through the existing
`setModuleImports` seam. `Program.cs` calls the real round trip once during
bootstrap (JS snapshot -> `RekallAgeWebInputSnapshotJson.Parse` ->
`RekallAgeWebInputBridge.Capture`) and folds the confirmed viewport size into
the existing runtime status text, so this is exercised on every load rather
than being dead code awaiting Task 7's continuous loop. Verification: the
focused selection passed 20/20 C# tests (11 bridge/edge-detection tests plus
9 JSON-parsing/round-trip tests) and 10/10 new Node tests (33/33 including the
existing WebGPU suite); the zero-warning/zero-error Release solution built
cleanly for both the desktop and `browser-wasm` targets; and the complete
engine suite passed 1,644/1,649 with the same five pre-existing,
environment-caused failures (the two `BuildModulesCommandTests` cases now
fail because `pwsh` is not on this shell's `PATH` rather than the earlier
timeout symptom, confirming they are environment-dependent, not regressions).
Studio passed 53/53. Continuous simulation/presentation (Task 7) is next.

Task 7 (the browser simulation/presentation loop) is now wired end to end at
the build/test evidence tier; it has not yet been exercised in a real
browser. `RekallAgeWebPlayer` owns one bootstrapped session's play loop: each
tick captures the browser input snapshot through the Task 6 bridge (so a
held key's edge is never lost or double-fired across a pause boundary),
advances `RekallAgeRuntimeSimulationClock` only while unpaused and only when
elapsed time is positive, then always builds the current viewport frame
(`RekallAgeRuntimeRenderFrameBuilder`), projects it into backend-neutral
scene meshes (`RekallAgeVulkanSceneMeshBuilder`), and presents through the
Task 5 `RekallAgeRenderingDeviceSceneRenderer` -- once per visual tick,
regardless of whether that tick simulated zero, one, or several fixed steps,
so a paused or sub-frame-rate tick still redraws the current world instead of
a stale or blank canvas. On the JavaScript side, `web-player-loop.js`
(`createFrameLoop`) is a thin `requestAnimationFrame` driver owning only
timing, pause/resume, and one clamp on an oversized frame gap (a backgrounded
tab); it delegates all fixed-step/catch-up/clamping semantics to the existing
C# `RekallAgeRuntimeSimulationClock` rather than re-implementing them. `main.js`
bridges that push-style loop to a pull-style `frame.awaitNext` JS import so
`Program.cs` drives its own `while (true) { var elapsed = await
BrowserHost.AwaitNextFrameAsync(); ... }` loop the same way it already awaits
every other browser I/O call, without introducing any JS-to-.NET export --
this codebase had none before and still has none. `Program.cs` now runs the
real bootstrapped project continuously once one exists
(`gameBootstrap.Session is { } session`); the bounded WebGPU triangle proof
remains the fallback compatibility demonstration only when no published
project manifest is present. Verification: the focused C# selection passed
6/6 new `RekallAgeWebPlayer` tests (ordinary tick, zero-elapsed
zero-step-still-renders, pause/resume never advancing simulation while still
presenting and preserving frame identity, held-key survival across a pause
boundary, monotonic tick sequence regardless of pause, and resize between
ticks) against a real `Rekall.Camera3D`/`Rekall.GeometryPrimitive` scene
executed through the unmodified `RekallAgeRuntimeExecutionLoop`; 10/10 new
Node tests for `web-player-loop.js` (43/43 including the existing WebGPU and
input suites); the zero-warning/zero-error Release solution built cleanly;
and a real trimmed `browser-wasm` publish (`-p:PublishTrimmed=true
-p:ILLinkTreatWarningsAsErrors=false -p:SuppressTrimAnalysisWarnings=true`,
the same flags the existing `WebGameExporterTests` harness uses to suppress
the repository's pre-existing Core/Runtime/BepuPhysics trim-analysis findings
that are unrelated to this slice) succeeded and correctly fingerprinted both
new JavaScript modules into the published `wwwroot`. The complete engine
suite passed 1,650/1,655 with the same five pre-existing, environment-caused
failures as the prior two checkpoints. Studio passed 53/53. **This is
source/build/test evidence only (evidence-hierarchy tiers 1-2): no real
browser or Chromium session exercised this loop this session, so real
player launch, visual review, and gameplay-input-changes-state proof (tiers
4-5) remain outstanding and must not be read as claimed.** CLI/MCP/Studio
publish-web and audit-web commands (Task 8) are next, followed by the
Clockwork Canopy browser acceptance (Task 9).

A pre-commit review of the Task 5/7 work surfaced three real correctness
gaps that build/test evidence alone had not caught, and all three are fixed
in this checkpoint before Task 8 begins. (1) The browser tick loop captured
`output.Handle` once, before the frame loop started, and kept rendering into
it on every tick even after a `REKALL_WEB_VIEWPORT_RESIZED` lifecycle fact
was queued by `main.js`'s `fitCanvas()` -- `input.pullLifecycleEvents()` was
already wired end-to-end but nothing ever called it, so a real browser
resize would have silently rendered the new viewport into an old-sized
target. `RekallAgeWebPlayerLifecycleEventsJson.TryGetLatestResize` now
parses the queued lifecycle facts each tick in `Program.cs`; on a resize the
loop calls `ImportCanvasOutput` again at the new size and destroys the old
target, keeping the previous target only if the re-import itself fails. (2)
`RekallAgeRenderingDeviceSceneRenderer`'s pipeline had no depth attachment
(`DepthStencil: null`), so any 3D scene with overlapping geometry -- the
`WebPlayerTests` sphere included -- had undefined occlusion once actually
rasterized; correctness of this was invisible to build/test evidence because
the in-memory conformance device does not rasterize. Fixed generically,
without touching the WebGPU canvas-import protocol or JavaScript: the new
`RekallAgeRenderingDeviceSceneResources.ResolveRenderTarget` inspects the
caller's color target through the already-generic
`IRekallAgeRenderingDevice.InspectResources()` contract, and composes a
`Depth32Float` depth texture plus a new render target combining both,
recreating them only when the caller's color target handle or size changes
(so it also naturally follows the resize fix above, since a resize produces
a new color target handle). (3) The `while (true)` browser tick loop had no
exception handling; any tick exception or a rejected `AwaitNextFrameAsync()`
promise would have escaped `Main` entirely and frozen the tab on its last
`#state` text with no diagnostic. Both awaits are now wrapped: a failure
sets an explicit `REKALL_WEB_FRAME_LOOP_FAILED` / `REKALL_WEB_PLAYER_TICK_EXCEPTION`
`#state` code, calls `SetReady(false)`, stops the frame loop, and exits the
loop instead of hanging silently. Verification: 6 new/expanded C# tests (2
composed-depth-attachment tests in `RenderingDeviceSceneRendererTests`, 4
resize-lifecycle-JSON tests in `WebInputBridgeContractTests`) plus all
existing focused suites (`RenderingDeviceSceneRendererTests` 6/6,
`WebPlayerTests` 6/6, `WebInputBridgeContractTests` 24/24 -- 36/36 total);
the zero-warning/zero-error Release solution build; a real trimmed
`browser-wasm` publish with the same suppression flags as prior checkpoints,
succeeding unchanged. The complete engine suite was run twice under this
checkpoint; each run showed a small, non-overlapping set of failures (15 in
one run, 7 in the other, only `WindowsPlayerRecoveryTests` common to both)
in tests unrelated to any file this session touched (`BuildModulesCommandTests`
wedged-compiler-timeout tests, `PlayablePackageIntegrityTests` player-publish
tests, `McpAgentToolExecutorTests` token-budget test, module-schema/scaffold
tests, `WindowsPlayerRecoveryTests` needing a built Windows player exe) --
consistent with pre-existing environment/resource-contention flakiness under
this session's heavy concurrent background test/build load, not a
regression from this checkpoint's changes. Studio passed 53/53 (a first
attempt at this same command stalled for roughly an hour under that same
concurrent load without producing output, since `dotnet test` buffers stdout
until completion; a clean rerun with `--blame-hang-timeout` for diagnostics
completed in 13 seconds once contention cleared). **This checkpoint remains
tiers 1-2 evidence only: no real browser or Chromium session has exercised
the resize/depth/exception-safety paths added here.**

Task 8 of the genuine-web-game-publishing plan ("expose publish/audit
through CLI, MCP, Studio") is done. Recon confirmed neither
`PublishWebGameCommand` nor `AuditWebGameCommand` existed yet -- only the
Task 3 content-closure exporter (`RekallAgeWebGameExporter`) and module
registry generator (`RekallAgeWebModuleRegistryGenerator`) did -- so both
were built as thin orchestration commands over the existing building
blocks, following `AuditPlayablePackageCommand`'s composite-audit shape and
`BuildPlayerCommand`'s subprocess-invocation shape.
`PublishWebGameCommand` (`rekall.game.publish_web`) runs the exact sequence
already proven by
`WebGameExporterTests.TrimmedWebAssemblyPublishIncludesTheStaticallyRegisteredAuthoredModule`:
discover the authored static module(s), generate the registry + MSBuild
inputs, stage the declarative content closure, restore the freshly
generated module project(s), then one real trimmed `dotnet publish` of the
generic `Rekall.Age.Player.Web` project (never a project-specific web
player, per AGENTS.md) using `--artifacts-path` to isolate the shared
engine project's own obj/bin per request -- this is required, not
cosmetic: without it, a concurrent publish (a second agent session, or this
command racing an unrelated build of the same project, as the conformance
test suite does) collides writing the same
`obj/Release/net10.0/Rekall.Age.Player.Web.dll` and fails with a
file-locked CSC error instead of a graceful command failure; this was
caught empirically mid-task by a real collision in the full-suite run, not
by code review, and fixed by switching from an initial (broken)
`-p:BaseIntermediateOutputPath`/`-p:BaseOutputPath` attempt -- which
silently produced a `project.assets.json` missing the `browser-wasm`
target -- to `--artifacts-path`, the same isolation `BuildPlayerCommand`
already uses successfully for the same class of shared-project problem.
`AuditWebGameCommand` (`rekall.game.audit_web`) republishes the project
itself (the same self-contained shape as `AuditPlayablePackageCommand`,
not a wrapper requiring a prior publish) and then verifies: manifest
decode/hash/compatibility integrity (via the existing
`RekallAgeWebGameManifestCodec.DecodeAndValidate`, which already checks
engine/project-schema/module-SDK identity), module-registry coverage
against a fresh `Discover()` of the authored project, byte-identical
content relocation from the manifest's declared hashes, WebAssembly
runtime-identity artifacts (`dotnet.js`, `*.wasm`, `index.html`), and a
real static-server-boot check (a loopback `HttpListener` actually serving
`index.html` and `game.manifest.json`, not a filesystem existence check).
Per the plan, the audit list also calls for "a browser smoke frame"; that
is tier 3+ evidence this checkpoint does not claim --
`AuditWebGameResult.BrowserSmokeFrameVerified` is always `false` with an
explicit "not yet implemented, requires a real browser session" message,
kept outside the `Ready` gate so the command stays usable rather than
silently omitting or faking that check. Both commands are registered in
`RekallAgeDefaultCommandRegistry`, wired as CLI `game publish-web` /
`game audit-web` in `Rekall.Age.Cli/Program.cs`, exposed as Publish Web /
Audit Web buttons in Studio (`RekallAgeStudioViewModel.cs`,
`MainWindow.xaml` -- the plan referenced a `RekallAgeStudioWindow.xaml`
that does not exist; the actual Studio window file is `MainWindow.xaml`),
and classified in `RekallAgeMcpCatalog` under the existing `workflow`
category (a new `rekall.game.` prefix branch was added to the classifier)
so agent tool discovery surfaces them without inventing a new category or
any platformer-specific tool. Verification: a real, end-to-end
`WebGamePublishingTests.PublishesAndAuditsARealWebGameEndToEnd` test
(scaffolds a runtime module, builds it, publishes it through the real
trimmed WebAssembly pipeline, then audits the result -- all five audit
checks pass, `BrowserSmokeFrameVerified` is confirmed `false`) plus an
overlap-rejection test; CLI failure-path tests proving the command names
are reachable and fail closed; an expanded `McpCatalogTests` asserting
both tools are categorized `workflow`, recommended, and -- per AGENTS.md --
that no platformer- or genre-specific wording ever appears in the exposed
tool surface; two Studio `ICommand.CanExecute` tests. The full engine
suite was run four times total across this checkpoint (two before the
`--artifacts-path` fix, two after); every run showed a nonzero,
non-identical failure count (17, 15) confined to tests this session did
not touch, and root-caused to one specific, reproducible mechanism:
`BuildPlayerCommand`-based tests (`PlayablePackageIntegrityTests`,
`BuildPlayerCommandTests`, `AgentAuthoringGauntletTests`, and others)
invoke real `dotnet publish` against the shared `Rekall.Age.Player`/
`Rekall.Age.Player.Windows` projects and collide with each other under
xUnit's default test parallelism -- the same class of contention this
checkpoint's own fix addresses for `Rekall.Age.Player.Web`, just not yet
applied to those other commands (out of scope for this task). A
deliberately concurrent 3-way stress test (the new end-to-end test, the
pre-existing `WebGameExporterTests` trimmed-publish test, and the overlap
test, run together) was used to directly confirm the `--artifacts-path`
fix eliminates the collision for the files this task touched; that
combination now passes reliably. Studio passed 55/55 (53 pre-existing +
2 new). The zero-warning/zero-error Release solution build succeeded. A
second, distinct correctness bug was also caught and fixed mid-task,
directly by observing its effect rather than by inspection: running
`PublishWebGameCommand` without `--locked-mode` (necessary, since a
freshly generated module project cannot be in any committed lock file)
let `dotnet publish`'s implicit restore silently rewrite the engine's own
checked-in `src/Rekall.Age.Player.Web/packages.lock.json` with the test
run's transient temp-module project reference -- caught by `git status`
showing a real, checked-in file dirtied by a test run, not by code review.
Fixed with `-p:NuGetLockFilePath` redirecting the lock file into the same
per-request working directory `--artifacts-path` already isolates;
verified clean by rerunning the full end-to-end test and confirming
`git status` reports no diff on either engine `packages.lock.json`
afterward. **This checkpoint is tiers 1-3 evidence (source/build/test plus
a real static-server-boot proof): no real browser or Chromium session has
loaded a published web game this session, so `BrowserSmokeFrameVerified`
stays `false` and Task 5 step 7's original browser-execution gap remains
open.** Task 9 (accept Clockwork Canopy unchanged in the browser) is next.

That browser-execution gap is now closed with real tier-4/5 evidence,
gathered directly rather than deferred further. The `claude-in-chrome`
extension was unavailable in this unattended session, so a real, separately
installed Playwright-driven Chromium (not a stub, not headless -- headless
Chromium's ANGLE/D3D11 WebGPU backend failed device creation outright, a
real browser-environment constraint, not an engine defect) loaded a real
`rekall.game.publish_web` output of the existing `Examples/TumblingCubes`
project (camera, lit cube geometry, a floor, a spawner module, `physics3d`
capability) served over a genuine local static HTTP server. This surfaced
four real, previously-undiscovered defects in the actual browser execution
path -- none visible to any build, unit test, or code review, because the
in-memory test rendering device and the static-server-boot audit check
never execute WebGPU or the browser tick loop at all. Each was root-caused
from the real failure and reverified by republishing and reloading: (1)
`PublishTrimmed=true` removed BepuPhysics's constraint type processors
(e.g. `BallSocketTypeProcessor`), which BepuPhysics registers by scanning
its own assembly rather than through calls the trimmer's static analysis
can see, throwing `Arg_NoDefCTor` the instant a physics3d scene ticked;
fixed by rooting the `BepuPhysics`/`BepuUtilities` assemblies via
`TrimmerRootAssembly` in `Rekall.Age.Player.Web.csproj`. (2) The same
trimming pass disabled System.Text.Json's reflection contract resolver by
default, and the engine represents authored component data as untyped
`JsonObject`/`JsonNode` trees rather than through source-generated
`JsonSerializerContext` types, so the first tick threw
`NoMetadataForType` on `System.String`; fixed with
`<JsonSerializerIsReflectionEnabledByDefault>true</...>`. (3) The real game
tick loop never called `RekallAgeWebGpuRenderingDevice.FlushAsync` (only
the old one-shot compatibility proof workload did), so the WebGPU JS
bridge's bounded `pendingScopes`/`pendingCompilations` queues
(`webgpu-device.js`, `MAX_PENDING = 64`) never drained and every
subsequent WebGPU packet failed closed with
`REKALL_WEBGPU_PENDING_OVERFLOW` within the first frame or two of a
resource-heavy scene; fixed by flushing once per tick in
`Program.cs`'s frame loop, folding flush diagnostics into the same
fail-closed `#state` reporting the tick loop already used. (4) The first
resulting real WebGPU validation error was concrete and specific:
`webgpu-device.js` unconditionally set `stencilLoadOp`/`stencilStoreOp` on
any depth-stencil attachment, but the engine's only depth format
(`Depth32Float`, added this session for occlusion) carries no stencil
aspect, which WebGPU rejects outright, faulting the device permanently
(`RekallAgeWebGpuRenderingDevice.Fault` is sticky by design) on the very
first real frame; fixed by gating the stencil ops on the attachment
texture's actual format. (5) Once frame 1 rendered, frame 2 immediately
failed with `REKALL_WEBGPU_READBACK_PENDING`: the JS bridge stages a CPU
readback copy for every submit that draws into the live canvas output
(originally built only for the one-shot pixel-proof compatibility page,
which calls `readPixels()` once), but the ordinary game loop never calls
`readPixels()`, so the first frame's unconsumed readback buffer blocked
every later frame forever; fixed by dropping and replacing an unconsumed
prior readback instead of throwing, since nothing had mapped or read it.
After all five fixes, the same real browser loaded the same real published
build, bootstrapped a five-entity physics scene, and sustained real WebGPU
frame submission and presentation across 238+ real ticks without a single
diagnostic, and a canvas screenshot shows two correctly lit, correctly
occluding cubes on a floor -- genuine visual proof, not merely a
`Rendered: true`/`DrawCount` counter, which (per the same investigation)
cannot see a validation failure or a hollow/culled-away frame on their own.
The engine's own focused rendering/web-player suite (36/36) and the real
end-to-end publish/exporter suite (15/15) both still pass after these
production-code changes, and the Release solution build is 0
warnings/errors. These fixes live entirely in `Rekall.Age.Player.Web`
(`Program.cs`, `Rekall.Age.Player.Web.csproj`, `wwwroot/webgpu-device.js`)
and have no unit-test harness of their own -- `webgpu-device.js` is only
ever exercised by a real WebGPU device, so this real-browser verification
*is* the regression evidence for this checkpoint, not a substitute for one
that could exist. Task 9 (accept Clockwork Canopy unchanged in the
browser) is next, now with a browser-execution path already proven to
carry a real physics3d scene through a full publish/serve/render/tick
cycle.

The per-tick flush/readback cost recorded above is now fixed, not just
deferred. Added `RekallAgeWebGpuRenderingDevice.DrainAsync` (and a matching
`IRekallAgeWebGpuBridge.DrainAsync`/`webgpu-device.js` `drain()`/
`webgpu.drain` JS export): it still awaits the queued error-scope and
shader-compilation promises (so it still detects and reports validation
errors and still bounds `pendingScopes`/`pendingCompilations` the same way
`flush()` does), but does not also await
`device.queue.onSubmittedWorkDone()`, so it no longer serializes CPU and
GPU every tick; the real tick loop in `Program.cs` now calls `DrainAsync`
instead of `FlushAsync`. Separately, `RekallAgeWebGpuSubmitPacket` gained a
`CaptureReadback` flag (default `false`), and `webgpu-device.js` now only
stages the full-canvas CPU readback copy when a submit explicitly sets it;
`RekallAgeWebGpuRenderingDevice` exposes this as a distinct
`SubmitWithPixelReadback` method (deliberately not on the generic
`IRekallAgeRenderingDevice` contract -- pixel readback stays a WebGPU-
bridge concern), used only by `WebGpuProofExecution`'s one-shot
compatibility workload; ordinary scene submission (`Submit`) never
requests it, so the real tick loop no longer pays for an unread full-canvas
copy every frame. Reverified against the same real browser/published
build used above: the same TumblingCubes scene sustained real ticks with
identical per-frame draw counts and zero diagnostics, and a fresh canvas
screenshot shows the same two cubes in a visibly different (still
physically plausible) tumbled pose from the earlier screenshot, confirming
live simulation continued correctly through the change, not just that it
still boots. Six existing bridge test fakes across
`WebGpuProofEvidenceTests`/`WebGpuProofWorkloadTests`/
`WebGpuRenderingDeviceTests` were updated for the new interface member.
Focused WebGPU/rendering/player suite: 84/84. Real end-to-end
publish/exporter suite: 15/15. Release solution build: 0 warnings/errors.
`git status` clean.

Three follow-up checks de-risked before starting Task 9, since the drain/
readback changes above touch paths whose success can look identical to a
silent failure: (1) the standalone WebGPU compatibility proof page (the
`gameBootstrap.Session is null` branch, the one path that actually depends
on the now-gated readback existing) was republished with no game content
and reloaded in the same real browser -- `#state` still reaches `GPU
WORKLOAD EXECUTED` with a passing pixel proof, confirming the
`CaptureReadback`/`SubmitWithPixelReadback` gating did not silently break
the one caller that needs it. (2) The depth-stencil fix was temporarily
disabled again (`git diff` confirmed a clean revert afterward, no code
changed under version control), republished, and reloaded: `#state`
correctly showed the same real `REKALL_WEBGPU_VALIDATION_ERROR` as before,
confirming `DrainAsync` still surfaces real backend diagnostics rather than
silently swallowing them the way a no-op drain would. (3) A real browser
`ArrowUp` keydown/keyup was sent to the same published TumblingCubes build
via Playwright while intercepting `JSON.stringify` calls carrying
`heldKeyCodes` (the exact object `main.js`'s `input.snapshot()` serializes
for the C# side to parse): the snapshot showed `heldKeyCodes: ["ArrowUp"]`
while held and `[]` immediately after release, proving real keyboard input
reaches the runtime input bridge end-to-end in a published build, not just
that the page boots. This directly de-risks Task 9's planned semantic
input checks (movement/jump/grounding/etc.), which all depend on this same
path. No further code changes resulted; these were verification-only.

Also fixed while starting Task 9, caught the same way -- by actually
running a real, larger published scene rather than trusting the smaller
one to represent it: `MAX_PENDING` in `webgpu-device.js` (bounding
`pendingScopes`/`pendingCompilations`, a JS safety limit, not a WebGPU or
hardware constraint) was 64, and a real 28-entity scene's first tick
creates enough packets to trip `REKALL_WEBGPU_PENDING_OVERFLOW` before the
per-tick drain gets a chance to run once, dropping that frame. Raised to
512 for real headroom; reverified clean against the same scene, and the
compatibility proof page (republished fresh) still passes its pixel proof
afterward, confirming the raised compilation-pending bound didn't regress
its shader-compilation path.

**Task 9 needs one authoring decision from the user before it can
proceed; not a web-publishing defect.** Attempting to "accept Clockwork
Canopy unchanged in the browser" first looked like a broad rendering
failure, but closer investigation (below) narrowed it to a single small,
well-understood finding. Evidence trail, gathered directly and then
corrected once follow-up checks contradicted the first read of it:

1. Copied `Artifacts/AgentGames/OriginalPlatformer` (gitignored, no git
   ref -- see below) into scratch, content-hashed the authored source
   (`rekall.project.json`, `Scenes/`, `Modules/*.cs`/`*.csproj`,
   `Assets/`, `Shaders/`) as its frozen identity:
   `5289b667730b6c0f8835f17128723e75d176214a1730279dd187fe975ab7f4bf`.
   Installed the module SDK and built both modules
   (`ClockworkPlayable`, `ClockworkRules`) fresh against the copy, then
   published it through `rekall.game.publish_web` -- all 5
   `rekall.game.audit_web` checks passed (manifest-integrity,
   module-registry-coverage, content-relocation, runtime-identity,
   static-server-boot).
2. Loaded the real published build in the same real browser used above:
   it boots, bootstraps a real 28-entity physics/UI scene, and sustains
   real ticks with zero diagnostics -- the web publishing pipeline itself
   is working correctly. But the canvas shows almost nothing beyond the
   three backdrop planes, one small cube, and the HUD.
3. To rule out a web-specific defect, captured the *same* frozen copy
   through `render viewport capture ... vulkan` (real native Vulkan GPU
   rendering) and `... software` (the software rasterizer): both show the
   same near-empty result -- not a browser/WebGPU bug, since it reproduces
   identically across all three independent rendering backends.
4. Hiding the three backdrop planes (`visible: false`) and recapturing
   ruled out depth-ordering/occlusion as the cause: the same absence
   persisted with just the clear color behind it.
5. **The actual cause: `OrthographicSize: 3.4` on `CameraRig` frames only
   about 9 of this level's ~43 authored units of width.** Re-running the
   identical frozen scene with `OrthographicSize` temporarily widened to
   25 renders the *entire* level correctly in one screenshot -- player,
   multiple platforms, three collectibles, a hazard, all in their authored
   positions, sharp and correctly composited. Every "missing" entity
   outside the narrow 3.4 window (`FloatP1-3`, `Platform1`, all three
   `Hazard`s, `Glow2-6`, `GoalPad`, `Spire`) was simply, correctly outside
   the camera's frustum -- not a rendering defect. This is confirmed
   correct camera behavior, not a bug.
6. One narrower, genuinely unexplained item remains: `Glow1` (a collectible
   at X=0.5, comfortably inside the narrow 3.4 frustum's X range of
   [-7.03, 2.03]) does not render at `OrthographicSize: 3.4` even though it
   does render at `OrthographicSize: 25`. This is a real, minor, single-
   entity anomaly worth a follow-up look, but it is not the broad
   rendering failure first suspected, and does not block reasoning about
   the scene overall.
7. **The prior "accepted" evidence is stale, not from a different camera.**
   `Artifacts/AgentGames/OriginalPlatformer/_visual_evidence/
   Main_runtime_002.png` shows converging-edge, multi-face geometry that
   an orthographic camera cannot produce -- initially read as evidence the
   camera itself had changed since acceptance. Checked instead: the PNG's
   file mtime is `2026-08-23 19:21:52`; commit `219c676` ("render: share
   camera-correct software scene path", which explicitly replaces "the
   legacy fixed-oblique cube projection that drew rear faces and made
   ordinary cubes appear hollow or inside-out") landed at `19:25:50`, four
   minutes *later*. The accepted screenshot predates that fix -- it was
   captured through the old software-capture path that drew a fixed
   oblique view regardless of the authored camera, not through a
   perspective camera that later became orthographic. The scene's camera
   has most likely been orthographic all along; the capture path only
   recently started rendering it honestly.

**Net finding, corrected from the first pass above:** there is no broad
rendering defect and no baseline mismatch from a camera change. The real,
much smaller question is authorial: `OrthographicSize: 3.4` frames a small
fraction of this level, and nobody has looked at what the now-honest
render actually shows since the capture path was fixed. That's the user's
call, not an engineering judgment to make alone -- is 3.4 intentional (a
tightly-framed camera that follows the player, by design), or should it be
widened to show more of the level? The `Glow1` anomaly (point 6) is a
separate, smaller, worth-investigating item either way. Diagnostic
screenshots from this investigation (native Vulkan capture, software
capture, backdrops-hidden capture, the widened-ortho full-level capture,
and the prior accepted evidence for comparison) are preserved. Task 9
resumes once the user answers the framing question; the frozen copy and
its content hash remain available to continue from.

**Task 9 is complete.** The user chose to widen `OrthographicSize`. Set it
to `8` on the real, authoritative `CameraRig` entity in
`Artifacts/AgentGames/OriginalPlatformer/Scenes/Main.age.scene.json`
(gitignored, no git ref -- this is the actual accepted-game edit, not a
scratch-only change) -- a reasonable middle ground between the narrow 3.4
and the whole-level 25, giving a real few-platforms-ahead camera window.
Verified via both native Vulkan and software viewport captures of the real
source project before proceeding further: the full nearby level (player,
platforms, a collectible, a hazard-adjacent platform) renders correctly
and consistently across both backends at this setting, and the `Glow1`
anomaly from the investigation above no longer reproduces at this size (it
renders correctly in both captures) -- resolved as a side effect of the
widened framing, not separately root-caused.

Re-froze the project after the edit (new content hash:
`5aba3ea98dc86eef5656a8f1f89ab4768a4510403c6aa9ba178e27683da48a61`),
reinstalled the module SDK, rebuilt both modules clean, republished
through `rekall.game.publish_web`, and re-ran `rekall.game.audit_web` --
all 5 checks passed again. Loading the republished build in the real
browser surfaced a second real, generic-engine defect, caught the same
way as everything else in this session: by actually looking at the
rendered canvas, not just trusting `Rendered: true`. The scene rendered,
but visibly darker and less saturated than the same scene's native Vulkan
and software captures -- pixel-sampled to confirm: backdrop colors came
out at roughly 15-20% of their authored brightness in the browser versus
the expected floor. Root cause: `RekallAgeSceneWgslShaderSource`'s
fragment shader (the shared lit-scene shader `RekallAgeRenderingDeviceSceneRenderer`
uses, added this session for Task 5) computed `lit = ambient + ndotl *
(1.0 - ambient)` with `ambient = 0.15`, while
`RekallAgePerspectiveSoftwareSceneRenderer` (the pre-existing software
renderer) computes `shade = clamp(0.35 + ndotl * 0.75, 0.22, 1.15)` for
the identical `batch.Frame.LightDirection` -- more than double the
brightness floor. For a scene whose authored light angle gives low-to-zero
diffuse contribution on most surfaces (as this one does), that difference
is the entire visible result: correctly lit on software/native, mostly
ambient-floor-dark on the newer WGSL path. Fixed by matching the WGSL
shader's formula to the software renderer's exactly (same 0.35/0.75/
[0.22, 1.15] constants), so the same scene now looks the same across all
three backends -- reverified by re-sampling the same pixels after the fix
(brightness moved from ~15-20% to ~40-47% of authored values, matching
expectations). Focused suite (`RenderingDeviceSceneRendererTests`,
`WebPlayerTests`): 12/12.

With the corrected, re-lit, re-widened build, ran a real Playwright
gameplay sequence against the actual published output (not a synthetic
fixture): movement (`D`/`A`, confirmed by walking into and colliding with
the `Glow1` collectible), jump/grounding (the jump command correctly did
nothing while airborne, only ever applying while `grounded`), gravity and
falling off a ledge, collectible pickup (canvas draw count dropped 24 to
23 on contact, matching `Collected=true` + `WithVisible(false)`),
death/respawn (position returns to the authored spawn point after falling
past the death plane), reset (an isolated, clean before/after test:
draws 24 -> 23 (collect) -> 24 (reset restores the collectible) --
confirms `reset` correctly clears `Collected` and re-shows the entity, not
just repositions the player), and camera-follow (the world visibly scrolls
and previously off-screen platforms enter frame as the player moves,
exactly matching `ApplyPresentation`'s `cameraX = clamp(playerX +
CameraOffsetX, ...)`). Hazard collision and the goal/win condition were
**not** reached through scripted play -- both require precise multi-jump
platforming across level gaps that a blind, scripted input sequence cannot
reliably execute -- but both use the identical overlap-test and
lives/death/respawn (or `phase = "won"`) code path already proven correct
by the death/respawn test above, read directly in
`ClockworkRulesSystem.cs`. **The authored HUD text (`SCORE ... LIVES ...
PLAYING`) does not visually render in the web/WebGPU path.** This is a
pre-existing, already self-documented gap -- `RekallAgeRenderingDeviceSceneRenderer`'s
own class doc comment states "UI canvas draws are not yet covered by this
renderer; they are deferred" -- not a regression from this task, and not
attempted here: implementing UI-canvas/glyph rendering in the generic-
device renderer is a substantial new rendering feature, not a defect
repair, and is explicitly out of scope for "accept unchanged." The
underlying `HudState`/`Label` text data is still computed correctly every
tick by the backend-agnostic module system; only its on-screen text
presentation is missing in this specific renderer.

Verification for this task: focused suites (`RenderingDeviceSceneRendererTests`
12/12, `WebPlayerTests` included), Release solution build 0 warnings/
errors, full engine test suite (background run) -- see below for the
specific pass/fail counts and confirmation that any failures match the
same pre-existing parallel-execution flakiness cluster documented earlier
this session. `git status` clean after each publish cycle (raw
`dotnet publish`/`restore` calls outside `PublishWebGameCommand`'s
lock-file isolation twice wrote to checked-in `packages.lock.json` files
during this task's diagnostics; both reverted with `git checkout --` before
committing anything). Known, deliberately out-of-scope items carried
forward: HUD/UI canvas text rendering (above), and hazard/goal reachability
via scripted browser input (would need a real jump-timing-aware test
harness, not attempted this session). The old moving-dot server this task's
plan says to replace was not located in this session's scope -- see the
open item below rather than a completed replacement.

The first Godot-reference graphics milestone is verified. A shallow,
blob-filtered sparse reference checkout at `F:\Dev\godot-reference` pins Godot
commit `893cf5cbfe789ae67c9389708e1428141bb39b18`; no Godot implementation code
was copied. AGE now has a bounded 100% C# project shader preprocessor with
nested `.glslinc` libraries, `#pragma once`, deterministic dependency output,
root/link confinement, cycle/missing/malformed/depth/file/size diagnostics,
and exact expanded-source inspection. Validation, Vulkan compilation,
reflection, runtime pipeline resolution, dependency-sensitive hashing, and
existing shader hot reload all consume expanded source. CLI/MCP expose
`rekall.shader.write_include` and `rekall.shader.preprocess`, while existing
list/read commands understand include resources. Verification passed 16/16
focused shader/MCP tests, the complete 1,134/1,134 engine suite, all 25/25
Studio tests, and a zero-warning, zero-error Release solution build. The same
gate found and repaired two controller-input regressions: schema verbosity no
longer truncates bounded agent discovery, and SDL controller subsystems remain
alive until the Player window owner closes, preserving supervised graphics
recovery.

The next Godot-gap implementation is the public 100% C# RenderingDevice-style
resource/command contract and its inspectable agent surface. Web remains an
explicit proof track: .NET WebAssembly Player, browser host/input/audio/storage,
WebGPU primary backend, WebGL 2 fallback, packaging/audit, and a real playable
browser acceptance. Current desktop assemblies are not yet a web export.

RenderingDevice Task 1 is committed at `91c3f0a`: public backend
capabilities, bounded buffer/texture descriptors, stable diagnostics, opaque
device/kind/slot/generation handles, immutable copy command buffers, stale and
foreign handle rejection, resource inspection, and an in-memory conformance
device pass 5/5 focused tests and a zero-warning Release rendering build.

RenderingDevice Tasks 2 and 3 are implemented and focused-verified, pending the
full-suite checkpoint. The public contract now records flat immutable transfer,
render, and compute streams: begin/end pass, pipeline and binding-set selection,
vertex/index buffers, draw/indexed draw, and bounded dispatch. The in-memory
backend validates pass state and resource usage while recording, then replays
validation at submission so destroyed/stale resources cannot silently execute.
The focused RenderingDevice suite passes 12/12. The new
`rekall.render.device.inspect_workload` command is registered in the default
CLI/MCP registry, validates portable resource/compute requirements, and reports
limits, memory estimates, command capabilities, and stable diagnostics. Its
real CLI JSON path and the combined rendering/MCP selection pass 15/15.

RenderingDevice Task 4 is verified. A generic fullscreen present-pass
planner now emits and submits a validated six-command AGE stream and recreates
only size-dependent target resources; its focused tests pass 4/4. The Windows
Player's Veldrid present and post-process draw has been replaced with a thin
executor over that AGE command stream in both runtime and playable paths. The
live Vulkan Player completed `Examples/VulkanCubeProbe Main` for 5/5 requested
frames in one attempt with no recovery. The complete Release engine suite passes
1,151/1,151, Studio passes 25/25, and the Release solution builds with zero
warnings/errors. The verified implementation is committed at `c3801df` and is
included with this ledger update on `origin/codex/studio-interaction`.

The first browser runtime proof is also verified. Official .NET 10 WASM
workloads are installed; `Rekall.Age.Player.Web` publishes real AGE Core, World,
and Rendering.Abstractions assemblies through Emscripten. A live Chromium run
reported `.NET 10.0.11 / browser-wasm`, detected WebGPU, rendered the canvas
shell, and produced zero browser warnings/errors. This proves the runtime and
interop route only, not playable export; the UI states that boundary explicitly.

The 2026-08-22 generic controller-input milestone is implemented pending its
physical-device acceptance gate: immutable controller axes/buttons/hats now flow through runtime
frames; semantic maps support canonical gamepad controls, arbitrary raw
joysticks, deadzones, saturation, inversion, response curves, and device/player
filters; the Windows Player polls SDL2 with hot-plug and raw-device fallback;
CLI/MCP expose deterministic controller frames plus dedicated bounded binding
inspection and transactional scene rebinding; and agent-authored modules can
inspect physical devices while retaining semantic actions as the normal path.
The next research track is a shallow Godot .NET source audit focused on
rendering, shader programmability, scripting, and web export, with useful
concepts translated into AGE's 100% C# and agent-first architecture.

That remaining Godot audit is now complete and recorded in
`docs/production/2026-08-23-godot-remaining-capability-audit.md`. The WebGPU
tranche closes the explicit low-level resource/command and physical browser
execution gap. The genuine remaining concepts are a declarative render graph
and named scene attachments, typed reusable material assets/instances, editable
multi-surface mesh assets, scene-level instancing, shader variants/cache,
generic GPU particles, production lighting/environment services, deterministic
asset cooking, and complete browser game export. The next source-reference item
is the queued Blender audit and AGE-native modelling design; Blender will inform
authoring topology/operations while Godot informs efficient runtime projection.

Complete the RenderingDevice-to-Player migration checkpoint currently in
flight: this is now complete and pushed at `601b4bb`. The active graphics item
is the bounded declarative GPU-workload boundary for agent-authored C# modules,
specified in `docs/superpowers/specs/2026-08-22-module-gpu-workload-design.md`.
Its first contract checkpoint passes 3/3 focused tests: immutable named buffer,
texture, sampler, shader, layout/set, pipeline, render-target, and command
records survive runtime-world JSON round-trip; module SDK helpers provide
bounded deterministic add/replace/list/remove behavior without native handles.
Runtime SDK inspection now exposes the exact compiled helpers, workload shape,
opaque-handle safety boundary, and a complete compute-pass authoring recipe;
the combined runtime/SDK selection passes 8/8.
The transactional compiler's compute slice is also focused-verified. Valid
named buffer/shader/compute-pipeline graphs resolve to opaque handles and an
immutable command buffer; duplicate IDs, missing references, command overflow,
and declarations reserved for later transfer/render stages fail closed before
retaining any resources. The combined GPU workload contract/SDK/compiler
selection passed 12/12 at that compute-only checkpoint; the broader
transfer/render milestone below supersedes it.
The transfer/render compiler and agent inspection milestone is now implemented
and checkpoint-verified. Named textures, samplers, layouts/sets, render
pipelines, color/depth targets, clears, vertex/index bindings, draw/indexed
draw, copies, and compute compile through the same transactional path. Null
nested collections, missing operands/references, unsupported formats, shader
source totals, and aggregate allocation budgets return stable diagnostics
before allocation. `rekall.render.device.inspect_runtime_workload` is registered
for CLI/MCP and returns named opaque resources plus immutable command kinds
without submission. Its real CLI path compiled a WebGPU-shaped workload to two
resources and four commands with zero diagnostics. The latest focused
compiler/inspection/catalog selection passes 11/11, the full engine suite
passes 1,164/1,164, Studio passes 25/25, and the Release solution builds with
zero warnings or errors. Initial asset-data upload and real Player workload
execution are the active gaps.
The first real module-authored GPU workload now executes in the Windows Vulkan
Player. The compiler accepts explicit validated external resources without
taking ownership; the new Veldrid RenderingDevice adapter maps AGE resources,
GLSL shaders, layouts/sets, render/compute pipelines, targets, and immutable
commands onto the Player's active command list. The Player imports
`engine.scene-color` and `engine.output`, caches workload compilations, rebuilds
them when frame resources change, and reports stable diagnostics without
crashing the render loop. A live probe exposed and fixed a runtime projection
bug that discarded module-authored workloads after every simulation step; its
regression is now covered. The programmable compositor probe reports one
enabled and one executed workload on Vulkan and its captured blue/pink diagonal
shader output is stored at
`Examples/ProgrammableCompositorProbe/Captures/vulkan-programmable-compositor.png`.
The current native boundary is intentionally explicit: the Veldrid adapter
accepts GLSL, supports the render/compositor path and compute without storage,
and advertises storage buffers/textures as unavailable until raw/structured
layout metadata and upload/readback are implemented. Focused workload,
projection, compiler, and SDK-inspection coverage passes 19/19; the full engine
suite passes 1,167/1,167, Studio passes 25/25, and the Release solution builds
with zero warnings or errors. A final bounded live Vulkan run completed 5/5
frames in one attempt with no recovery and logged one enabled/one executed
module workload.
Portable vertex-buffer layout authoring was implemented as the first half of
this milestone. Runtime pipelines declare typed attributes, byte offsets, strides,
and per-vertex/per-instance stepping; the compiler maps them into public
RenderingDevice descriptors and the Veldrid adapter maps those descriptors to
native vertex layouts. Validation bounds buffer/attribute counts and strides,
rejects null/empty layouts, duplicate or non-dense shader locations, and format
ranges that exceed the stride. Runtime SDK inspection supplies the exact C#
shape and a position/normal/UV recipe. The combined RenderingDevice, compiler,
and SDK-inspection selection passes 28/28; the Release solution is again clean
with zero warnings/errors and the Vulkan compositor remains 5/5. Bounded
Project-asset upload was the remaining half of this data-backed geometry
milestone and is completed below.
The data-backed geometry milestone is now complete. `IRekallAgeRenderingDevice`
exposes bounded buffer and tightly packed texture writes with stable
usage/range/subresource diagnostics and uploaded-byte inspection. The runtime
workload compiler resolves `InitialDataAsset` only through an explicit resolver,
adds the necessary copy-destination usage, rejects missing, empty, oversized,
or wrongly sized payloads before retaining allocations, and rolls back every
created resource if a native write fails. The production project resolver uses
stable catalog IDs, relocates stale checkout paths by catalog kind and filename,
permits reads only below the current project `Assets` root, rejects reparse
points and hard links using the opened file handle, and performs bounded reads
from that same handle so a path swap or file growth cannot escape the 64 MiB
limit. Catalog and payload SHA-256 values are verified before bytes are
returned. The Player uses a content-derived catalog revision for workload cache
identity and does not cache resolution-dependent failures, so same-size edits
and repaired assets retry deterministically. Portable initial texture uploads
reject depth/stencil formats, multisampling, and multiple array layers until an
explicit cross-backend payload layout exists; mip chains are bounded by the
resource dimensions. A shared overflow-safe layout calculator accounts for
format size, every mip, array layers, and samples in compiler budgets and
backend inspection, and rejects impossible mip counts before iteration. The
combined device/compiler/resolver selection passes 43/43, the full engine suite
passes 1,186/1,186, Studio passes 25/25, and the
Release solution builds with zero warnings/errors. A bounded native Vulkan run completed 5/5 frames and
logged one enabled/one executed workload. Independent foreground
Windows capture then proved the full native chain with a cyan/blue/magenta
triangle whose positions are decoded from the 24-byte catalog-backed vertex
asset through an explicit `Uint32x2` layout. The committed evidence is
`Examples/ProgrammableGeometryProbe/Captures/vulkan-asset-backed-geometry.jpg`.
Storage-resource and indirect-command metadata/execution is the next GPU tranche.
The storage-buffer and indirect-command portion of that tranche is now
implemented and broadly verified. Storage buffers carry an explicit structure
stride that must divide the allocation; the conformance and Vulkan adapters
validate it, and the Vulkan adapter advertises the selected device's real
structured-buffer and indirect-command capabilities. Portable command buffers
now include range- and alignment-checked indirect draw, indexed-draw, and
dispatch records without exposing native handles. Compiler and runtime SDK
contracts map the same metadata and give agents exact 16-byte draw, 20-byte
indexed-draw, and 12-byte dispatch argument layouts. The geometry probe now
also uploads, binds, and mutates a catalog-backed structured buffer in a native
compute pass before drawing its colorful triangle; its latest Vulkan run logged
one enabled and one executed workload and completed 5/5 frames. The full engine
suite passes 1,196/1,196, Studio passes 25/25, and the Release solution builds
with zero warnings/errors. Independent review found no merge blockers. Storage
textures and deterministic native indirect execution are now complete. Texture
binding sets reject sampled/storage usage mismatches and byte-range fields on
textures before submission; the Vulkan adapter exposes storage images and maps
read-only versus writable shader bindings explicitly. Small exact argument
buffers can use bounded, little-endian `InitialDataUInt32` values, while large
payloads retain the catalog-backed asset path. The native probe now binds both
read-only and writable storage-image views, mutates its structured buffer, and
renders its asset-backed colorful triangle through `DrawIndirect`; Vulkan again
completed 5/5 frames with one enabled/one executed workload. The focused GPU
selection passes 59/59, the full engine suite passes 1,201/1,201, Studio passes
25/25, and the Release solution builds with zero warnings/errors. WebGPU backend
execution parity is the next rendering tranche.
WebGPU execution parity is now physically proven for the portable GPU workload
boundary. The conformance-backed C# adapter, strict versioned protocol, trimmed
browser-WASM bridge, and browser WebGPU executor cover resource creation,
bounded uploads, binding/pipeline resolution, compute/render/transfer command
streams, indirect commands, submission, and readback without exposing native
handles. A runtime-compiled WGSL workload uploaded a 24-byte `Uint32x2` vertex
buffer and indirect arguments `[3, 1, 0, 0]`, drew through `engine.output`, and
returned the same frame's padded canvas readback to C# for independent raw-pixel
validation. Physical Chromium acceptance recorded one submitted frame, no GPU
diagnostics, no browser warning/error logs, and the expected background plus
cyan/blue/magenta samples. The committed PNG and structured evidence are under
`docs/production/evidence/webgpu-physical-proof-2026-08-23.*`; the PNG SHA-256
is `69411E4435E077180018BAF82465491FC138D5A33B5213594536D9AD725652DB`.
The final schema-v2 rerun used Google Chrome 151.0.7922.170 and bound the
decoded 1280x720 capture, zero-entry browser log, and evidence to one prepared
session plus an immutable 92-file/14,210,180-byte publish whose manifest
SHA-256 is `8EC7A673243B33CD0056E78BFA5EA8AF78A02567D1C0E3F6742837D42239B2A6`.
Adversarial harness tests reject truncated/header-only PNGs, stale or
cross-session artifacts, tampered publish identity, out-of-run paths, and
unrelated server processes.
This proves programmable WebGPU execution, not complete game export. Browser
scene/module loading, semantic input, audio, storage, networking, packaging,
audit, and deterministic gameplay acceptance remain the platform track. The
safety checkpoint passed a zero-warning, zero-error Release solution build,
1,303/1,303 engine tests, 25/25 Studio tests, and the browser acceptance harness
self-test.
After its Windows programmable-compositor proof, resume Pong through the generic
portable authoring path, followed by a Galaga-class game in Studio. The queued
Rain Glass shader acceptance follows those playable proofs
and still requires the licensed remote image, full-window asset-backed
composition, two temporally distinct frames, package relocation, and audit.

Task 9's plan item 7 ("replace the old moving-dot server with the accepted
relocated game") was previously logged as not located in this session's
scope. It is now done: it referred to this README's own "Web Player Proof"
section, which documented serving `Rekall.Age.Player.Web`'s bare
RenderingDevice proof build (a moving shape on an otherwise empty canvas,
via `python -m http.server` directly against that project's own publish
output) as if it were the web-publishing story. Promoted the accepted,
camera-fixed, correctly-lit Clockwork Canopy project into
`Examples/ClockworkCanopy/` (`rekall.project.json`, `Scenes/Main.age.scene.json`,
both modules' source and `.csproj` files -- no `bin/`, `obj/`, `.rekall/`, or
generated lock/transaction files, matching the existing `Examples/TumblingCubes`
pattern exactly) and re-verified the promoted copy through the ordinary
commands from a clean checkout: `module install-sdk`, `build modules`,
`game publish-web`, `game audit-web` -- all 5 audit checks passed
(manifest-integrity, module-registry-coverage, content-relocation,
runtime-identity, static-server-boot). `git status` was clean after each
step (no stray `packages.lock.json` writes this time). Rewrote the README's
"Web Player Proof" section to document publishing/auditing/serving
`Examples/ClockworkCanopy` as the primary path, and demoted the bare
`Rekall.Age.Player.Web` proof to what it actually is: a renderer/bridge
diagnostic fallback, explicitly not a playable game export. Added
`.rekall-web-publish/` and `*.web-publish/` to `.gitignore` so the CLI's
per-project web-publish staging/output directories (e.g.
`Examples/ClockworkCanopy.web-publish/`) don't show up as untracked cruft.
This closes the last open item from Task 9; the full plan
(`docs/superpowers/plans/2026-08-23-genuine-web-game-publishing.md`) is now
complete end to end.

The previously-documented "pre-existing parallel-execution flakiness
cluster" (`BuildModulesCommandTests` wedged-compiler tests,
`McpAgentToolExecutorTests`, `ProjectRuntimeSystemTests`,
`ScaffoldRuntimeSystemModuleCommandTests`, `WindowsPlayerRecoveryTests`) is
fixed. All 6 failures turned out to be real, individually diagnosable
causes, not vague nondeterminism -- confirmed by running each in isolation
first (5 of 6 failed deterministically alone, only
`ScaffoldRuntimeSystemModuleCommandTests` needed the full parallel suite to
reproduce):
- `BuildModulesCommandTests` (2 tests): spawned `pwsh` (PowerShell 7) as a
  portable stand-in for a hung external compiler process; `pwsh` is an
  optional separate install and was not present on this machine. The first
  pass here just switched to `powershell` (Windows PowerShell 5.1) --
  caught in review before landing: `tests/Rekall.Age.Tests` targets
  `net10.0`, not `net10.0-windows`, and `powershell.exe` doesn't exist off
  Windows, so that traded "fails on this dev machine" for "fails on any
  non-Windows runner." Resolves `pwsh` first (cross-platform, and matches
  what a non-Windows CI runner for this project would actually have),
  falling back to `powershell` only when `pwsh` isn't on `PATH`, preserving
  the original intent while fixing this environment.
- `WindowsPlayerRecoveryTests`: hardcoded the `bin/Debug/net10.0-windows`
  output path for `Rekall.Age.Player.Windows.exe`, so a Release-only
  build/test pass (this session's convention) made it fail even though the
  player was built and fine. Now tries Release first, then Debug, mirroring
  `WebGameCliTests.FindCliAssemblyPath`'s existing pattern for the CLI
  assembly.
- `McpAgentToolExecutorTests.BroadComponentSearchFitsTheAgentToolBudget...`:
  the built-in component catalog has grown enough that a `limit=12` response
  for its broad multi-topic query now serializes to 13,955 characters, over
  the 12,000-character agent tool budget
  (`RekallAgeMcpAgentToolExecutor.ExecuteRegisteredToolAsync`). The first
  pass here just lowered the test's `limit` to 9 to dodge the budget -- caught
  in review before landing: the production default really is 12, the
  `rekall.module.search_component_schemas` schema's own description tells
  agents to "raise Limit if needed" for broad queries, and the executor's
  prior behavior on overflow discarded the entire structured `value` in
  favor of an opaque 8,000-character string preview. An agent following the
  tool's own advice at its own default was silently losing the structured
  component list it asked for -- on the exact primary-authoring-discovery
  surface this session was about to lean on for Pong/Galaga modeling work.
  Fixed for real in `RekallAgeMcpAgentToolExecutor`: when a tool result
  exceeds budget, find the largest JSON array reachable under its `value`
  and drop trailing (lowest-ranked, since search results are already
  ordered by match score) elements until the whole document fits, recording
  `"<property>Truncated": {returned, total}` next to it, instead of
  replacing `value` outright. Falls back to the old opaque-preview behavior
  only if there's no array to shrink or shrinking it to one element still
  doesn't fit. Test restored to the real `limit=12` default; passes because
  the array now degrades gracefully instead of the whole response being
  discarded.
- `ProjectRuntimeSystemTests.RuntimeViewportCaptureUsesProjectRuntimeSystem
  Output`: the test's orbit-scene camera had no explicit `Transform3D`, so
  it defaulted to the world origin facing +Z. The orbiting cube ends at
  world X=2, Z=0 -- off to the side of the camera's frustum, not in front of
  it, once the software renderer honors the real authored camera instead of
  the legacy oblique-projection fallback (the same class of gap Task 9
  found in Clockwork Canopy). Gave the camera an explicit position/pitch so
  the cube stays in frame. The rendered cube then landed left-of-center
  instead of the right-of-center the original assertion expected; rather
  than trust that and flip the assertion on one backend's word, captured
  the identical scene through both `render viewport capture ... software`
  and `... vulkan` (NVIDIA GeForce RTX 5090) independently -- both backends
  agree the cube lands left-of-center, so this is a real, consistent AGE
  camera convention (unrotated camera facing +Z, no yaw: +world-X projects
  screen-left), not a backend divergence like the WGSL lighting bug was.
  Corrected the assertion to match, with the cross-backend check recorded
  in the test's own comment.
- `ScaffoldRuntimeSystemModuleCommandTests`: a real engine bug, not test
  brittleness. `RekallAgeModuleIndexer.IndexAssembly` filtered candidate
  module types by `!type.IsAbstract` only, which does not exclude open
  generic types (`Type.ContainsGenericParameters`). Whenever an unbound
  generic `RekallAgeModule` subclass from another test's dynamically
  compiled module assembly (`Game.Modules.WebRules.WebRulesModule<T>`) was
  already loaded into the shared parallel-test-run AppDomain, the indexer's
  `Activator.CreateInstance` call on it threw
  `ArgumentException: Cannot create an instance of ... because
  Type.ContainsGenericParameters is true`, which is exactly why this one
  only reproduced under full-suite parallel load and never in isolation.
  Added the missing `!type.ContainsGenericParameters` filter.
- A sixth, previously-unseen failure surfaced once the above were fixed:
  `WebGamePublishingTests.PublishesAndAuditsARealWebGameEndToEnd` hit
  `NuGet.targets error: The process cannot access the file '...
  packages.lock.json' because it is being used by another process.`
  `rekall.game.audit_web` republishes through the same
  `PublishWebGameCommand` path a preceding `rekall.game.publish_web` call
  in the same test already used, against the same isolated
  `workingRoot`/lock-file path. On Windows the just-exited `dotnet.exe`
  process's handle on that file is not always released the instant
  `WaitForExitAsync` returns -- hit this same error interactively earlier
  this session too, only fixed then by `dotnet build-server shutdown`.
  Added a bounded retry (4 attempts, linear backoff) in
  `PublishWebGameCommand.RunDotNetAsync`, matching only that specific
  transient NuGet lock-file error string so any real compile/publish
  failure still surfaces on the first attempt.

Verification: 3 full sequential runs of the complete engine suite as each
fix landed (1659/1665 -> 1664/1665 after the first 5 fixes -> 1665/1665
after the `ModuleIndexer` and `PublishWebGameCommand` fixes), 55/55 Studio
tests, a zero-warning zero-error Release solution build, `git status` clean
(only the 6 intended files touched, no stray `packages.lock.json` writes).

`codex/web-scene-bootstrap` merged into `master` (two merges: the original
Task 9 completion, then a follow-up with the review-corrected flakiness
fixes above), both isolated-suite-verified at 1665/1665 before pushing.
Started `codex/model-asset-games` off `master` for the next standing
directive: accept two substantial, genuinely 3D, visually strong games
(Pong and an original Galaga-like game), authored strictly through AGE's
ordinary CLI/MCP command surface with progressive discovery -- not by
recalling command/component names from memory or hand-editing scene
JSON -- and each placing at least one authored, published Model Asset
whose render mesh *and* physics collider are both resolved through the
canonical Model Asset path, directly exercising STRATEGIC-PRIORITIES.md
priority #3. Plan:
`docs/superpowers/plans/2026-08-24-pong-and-galaga-3d-model-assets.md`.

Built a small Node.js MCP stdio client
(scratchpad, not committed -- reusable across both games) that speaks the
real `initialize`/`tools/list`/`tools/call` JSON-RPC protocol against
`dotnet Rekall.Age.Cli.dll mcp stdio`, the same way an actual MCP client
would, rather than assuming command names/schemas from source. Finding:
this entrypoint (`RekallAgeMcpJsonRpcServer`, wired directly in
`RunMcpStdioAsync`) exposes all 170 registered commands directly via
`tools/list`/`tools/call` -- it does not go through
`RekallAgeMcpAgentToolExecutor`'s progressive-discovery gateway
(`rekall.tools.search`/`rekall.tools.execute`) at all, so that gateway's
budget-limited exposure model is exercised by its own unit tests but not
by this actual CLI entrypoint. All of `rekall.modeling.graph.*`,
`rekall.asset.model.*`, and `rekall.scene.instantiate_asset` were
genuinely discoverable this way (contrary to an earlier, wrong shortcut of
grepping `Program.cs` for CLI verbs and concluding they had no discovery
path -- corrected before it caused any real damage). Two schema/UX
findings from driving the actual protocol, both since resolved: (1)
`rekall.asset.model.publish`'s own tool description says
`source has exact shape { kind: Mesh, assetId, outputName? }`, literally
implying the JSON string `"Mesh"` for `kind`, but the actual required
shape was the enum's underlying integer (`0`) -- the string value threw
`REKALL_COMMAND_ARGUMENTS_INVALID`. Fixed by making the behavior match
the description (accepting the string, the friendlier direction) rather
than the reverse: added
`[JsonConverter(typeof(JsonStringEnumConverter<RekallAgeModelSourceKind>))]`
to `RekallAgeModelSourceKind`, matching the existing convention already
used for every enum in `Rekall.Age.Modeling.Contracts`. Verified through
the exact raw-JSON path a real client uses
(`RekallAgeCommandRegistry.ExecuteJsonAsync`, not a typed C# request
construction) with a new test,
`PublishAcceptsTheSourceKindEnumNameAsRawJsonMatchingItsOwnToolDescription`.
(2) Re-checked `rekall.scene.instantiate_asset` for a missing scene
directly over the real MCP protocol before touching anything -- it
already returns a structured `REKALL_MODEL_PLACEMENT_FAILED` error (the
underlying `DirectoryNotFoundException` is caught by the command's
existing `IOException`-family catch clause), not a raw unstructured
exception. The earlier note above was wrong/stale; corrected here rather
than left standing. The error message itself is a bare `Could not find
file '<full path>'`, which is arguably terser than ideal, but it is the
same generic catch-all shape every scene-loading command in this codebase
uses (no sibling command has a bespoke `REKALL_SCENE_NOT_FOUND`), so left
as-is rather than inventing a one-off convention.

While driving that same discovery-then-publish-then-place sequence to
build the first real Model Asset test fixture, found and fixed the actual
substance behind STRATEGIC-PRIORITIES.md priority #3's still-open status:
runtime rendering and physics never resolved `Rekall.ModelAssetReference`
at all -- only the older `Rekall.MeshAssetReference`. Confirmed empirically
(not from source inspection alone, per this session's established
discipline): published a real box Model Asset through the actual MCP
command surface, placed it with `rekall.scene.instantiate_asset`, and
captured the resulting frame -- `Asset-backed: 0, Fallback: 1`, an
unresolved fallback shape, not the authored box. Fixed with one new
resolver (`RekallAgeCompiledModelAssetResolver` in `Rekall.Age.Runtime`,
mirroring the existing `RekallAgeCompiledMeshAssetResolver`'s synchronous
cached shape) and one new branch each in
`RekallAgeBepuPhysicsSystem.CreatePhysicsEntity` and
`RekallAgeRuntimeRenderFrameBuilder`/`RekallAgeCompiledMeshResolver`.
Re-verified the identical repro after the fix through the same real MCP
sequence: the placed box now renders its actual modeled geometry (captured
PNG shows the box), and adding `Rekall.MeshCollider` (`Convex=true`, since
the schema's own description says dynamic meshes require it) +
`Rekall.Rigidbody3D` to the same entity produces a real dynamic physics
body -- `Physics bodies: 1, Physics colliders: 1` -- that falls under
gravity exactly like the pre-existing `Rekall.MeshAssetReference` path
already did (`runtime inspect` over 30 frames: `position3D=(0, -1.267, 0)`,
`linear=(0, -4.905, 0)`, matching free fall). Added
`ModelAssetRenderingTests`/`ModelAssetPhysicsTests` mirroring the existing
`MeshAssetRenderingTests`/`MeshAssetPhysicsTests` coverage. Verification:
full engine suite 1668/1668 (Release and Debug), zero-warning zero-error
builds both configurations; committed and pushed on
`codex/model-asset-games` separately from any game content, per the user's
explicit instruction to keep engine fixes and game authoring in distinct
commits.

Secondary finding, not yet fixed (does not block the above): the 41 CLI
test failures seen in an interim run before Debug was ever built in this
worktree were all `Rekall.Age.Tests.Cli.*` tests whose
`FindCliAssemblyPath`-equivalent helpers hardcode `bin/Debug` with no
Release fallback -- the same class of brittleness
`WindowsPlayerRecoveryTests` had earlier this session (fixed there with a
Release-then-Debug search), but not yet applied to this larger CLI test
cluster. Worth a follow-up pass, not blocking.

### Pong 3D: promotion, HUD, lighting, and two more real CLI/MCP gaps

Promoted the sandbox Pong 3D project into `Examples/Pong3D/` (matching the
`Examples/ClockworkCanopy` and `Examples/GlbStationTest` precedent),
carrying every real asset artifact the Model Asset path needs to resolve
after relocation: both modeling graphs, both meshes, both `.age.model.json`
documents, both compiled-mesh outputs, and `Assets/assets.age.catalog.json`
-- not just the scene/module source. Excluded `bin/`, `obj/`, `.rekall/`,
`packages.lock.json`, `Transactions/`, matching the established exclusion
set.

Relocating the raw catalog file by copying it verbatim (rather than through
a CLI command) left its `sourcePath`/`importedPath` fields pointing at the
old `.age-sandbox/Pong3D` location -- own mistake, not an engine defect
(`GlbStationTest`'s catalog uses the same absolute-path convention and was
never relocated after authoring, so it never surfaced this). Fixed the
correct way, through the CLI: `rekall.asset.model.rebuild` on both assets
regenerated the catalog entries with paths correct for the new location.
The rendering/physics resolver path was unaffected throughout (it resolves
relative to `projectRoot`, not the catalog's absolute fields) -- confirmed
by `render viewport capture` from the promoted path showing real geometry
(`Asset-backed`/`Fallback` both read `0`, meaning the metric itself doesn't
track `Rekall.ModelAssetReference` yet, a minor observability gap noted but
not chased further) both before and after the catalog rebuild.

Discovered via a real `rekall.build.player` + native launch attempt (not
source-reading) that a project built only through
`rekall.module.scaffold_runtime_system` cannot launch natively:
`Rekall.Age.Playback.RekallAgePlayableModuleMissingException`. Found the
fix by searching the actual MCP tool list rather than guessing --
`rekall.module.scaffold_playable` exists precisely for this, and
`Examples/ClockworkCanopy` already carries both a `*Playable` and a
`*Rules` module for exactly this reason, which a first read of either
scaffold tool's own description would not have surfaced (neither
cross-referenced the other). Scaffolded `PongPlayable`, confirmed the
native player now gets past that exception (a leftover
`set_CursorVisible`/`IOException` in the same run is a non-interactive
console-redirection artifact of this shell environment, not a game or
engine defect), then fixed the documentation gap at the source -- see the
"engine fix" commit below. Per the session's established convention (real
3D visual proof lives in the web publish/audit/browser path, not the
native player's text loop, exactly as documented in the `web-scene-bootstrap`
README rewrite), did not chase native-player visuals further.

Following that same web publish/audit path surfaced a second, more
consequential real gap: `rekall.game.publish_web` staged a Model Asset's
*compiled mesh* (`asset.ImportedPath`) but never its own
`.age.model.json` *definition document*
(`ModelAssetMetadata.ModelDocumentPath`) -- confirmed by serving the
published bundle and finding `Assets/Models/pong-ball-model.age.model.json`
404s while `Assets/Models/Compiled/.../<hash>.age.compiled-mesh.json`
serves fine. Any web-published game using `Rekall.ModelAssetReference`
would silently fall back to the player's boot placeholder rather than
rendering. Fixed in `RekallAgeWebGameExporter.StageAsync`
(`src/Rekall.Age.Workflows/Web/RekallAgeWebGameExporter.cs`), verified the
fix by republishing and confirming both files now serve `200`, and added
`StagesTheModelAssetDefinitionDocumentAlongsideItsCompiledMeshOutput`
(`WebGameExporterTests`, 14/14 passing). Committed separately from Pong's
game content, together with the scaffold-description fix above, as
`f30f36c` on `codex/model-asset-games`; full engine suite re-verified green
at 1669/1669 (`Rekall.Age.Tests`) + 55/55 (`Rekall.Age.Studio.Tests`),
Release, after both fixes.

Attempted the remaining tier-4/5 evidence step (real browser session
against the served, fixed web build) via the same Playwright setup used
earlier this session for `ClockworkCanopy`. Both games show the identical
static "WEB PLAYER / CONTRACT PROOF 001 ... NOT YET A PLAYABLE EXPORT"
boot placeholder in a headless Chromium capture, with no console output
beyond the browser's own benign `favicon.ico` 404 -- a pre-existing
limitation of this specific Playwright/WebGPU-headless configuration in
this sandbox (already present for `ClockworkCanopy` before any Pong work
started), not a Pong-specific regression and not evidence the fix above is
incomplete. Recorded honestly rather than claimed: tier 1-3 automated
`rekall.game.audit_web` checks all pass (manifest-integrity,
module-registry-coverage, content-relocation, runtime-identity,
static-server-boot); tier 4-5 real-browser visual proof was not obtained in
this environment for either game.

Game-quality/content fixes made directly through
`rekall.component.set_property`/`rekall.component.add`/`rekall.entity.create`
(no hand-edited scene JSON): raised `KeyLight` intensity and added a second,
cooler-toned `FillLight` after finding the unlit back wall rendered fully
black regardless of its authored color (no ambient term -- a real, if minor,
lighting-model characteristic worth remembering for future scenes, not
treated as a bug to fix in the renderer); brightened the arena panel
colors; added a native `Rekall.UiCanvas` + two `Rekall.Label` score
readouts (confirmed the native text-rendering path actually paints through
both the Vulkan and software `render viewport capture` backends before
wiring gameplay to it) and extended `PongRulesSystem` to write the live
score into both labels every frame -- verified end-to-end via `runtime
inspect` (HUD read "0"/"5" after 3000 idle frames exactly matching the
simulated score) and via a captured frame mid-rally; fixed a game-design
bug where every serve launched with `velocityY = 0`, making the AI's
perfect Y-tracking degenerate every rally into an unbreakable straight
line -- serves now alternate a small deterministic vertical component with
`serveDirection`.

Verified `scoreRight` (left/player-side miss) increments correctly via a
fully passive 3000-frame idle run (score ended 0-5, matching an immobile
player paddle). Did not obtain equivalent direct evidence for `scoreLeft`
(right/AI-side miss, which requires the AI's speed cap to be outrun during
an extended rally that a static or simply-scripted player can't sustain);
the code path is the same `StartServe` call mirrored across
`newX < -ArenaHalfWidth` / `newX > ArenaHalfWidth`, not a separately
implemented, untested branch, so this is a coverage gap in the evidence
gathered, not a known-unverified code path. Also confirmed, at a real
paddle-contact frame (ball at `x=6.3`, about to bounce), that
`Events: 0` -- `PongRulesSystem`'s own AABB math drives the bounce, and the
Model-Asset-derived `Rekall.MeshCollider` on the ball, while genuinely
resolved (per the priority #3 fix above), does not participate in the
physics-engine collision-event stream during actual gameplay. Recorded
plainly rather than implied otherwise: the collider is present and
resolves through the real Model Asset path, but gameplay does not consume
physics collision events for it.

Clarification on the `render viewport capture` "Asset-backed"/"Fallback"
counters used as the priority #3 before/after evidence earlier: checked
`RekallAgeRuntimeSoftwareRenderer.DrawRenderables` directly rather than
assume. `AssetBackedRenderableCount` only ever increments for `"sprite"`-
kind renderables (2D image-backed UI/sprites); any `"mesh"`-kind
renderable that resolves -- whether via `Rekall.MeshAssetReference` or
`Rekall.ModelAssetReference` -- takes the separate
`TryDrawEngineRenderable` branch and increments neither counter. This is
why Pong's captures consistently read `Asset-backed: 0`: it isn't blind to
3D Model Assets, it was never counting them in the first place, by design.
`FallbackRenderableCount` is the metric that actually tracks resolution
health for meshes (it's the one that went `1 -> 0` across the priority #3
fix's before/after repro), and it continues to work as a self-verifying
regression signal for both `Examples/Pong3D`'s Model Assets specifically
(confirmed `Fallback: 0` throughout this session's captures) and for any
future mesh/Model Asset that fails to resolve. No engine change made here
-- documenting the naming precisely so a future reader doesn't misread
`Asset-backed: 0` as "nothing resolved" when 3D meshes are working
correctly and simply aren't what that specific counter measures.

Not yet done for Pong: package/relocate/audit via
`rekall.workflow.package_playable_game` /
`rekall.workflow.relocate_playable_package` /
`rekall.workflow.audit_playable_package` (only the web publish/audit path
was exercised); a dedicated evidence archive under `Artifacts/`; direct
evidence for the `scoreLeft` path noted above.

### A real, repeat CLI/MCP gap fixed: silent input-frame underrun

While re-verifying Pong's `reset` behavior earlier in this branch, and
again while testing Galaga's fire/enemy-dive logic, hit the same failure
twice: supplying one `PressedKeys`-held input entry to
`rekall.runtime.inspect_scene` and requesting many more frames than that,
expecting the key to stay held. The user asked directly whether this
needed better advertising to LLM clients; checked the command's own
schema description before assuming it did, per the discipline that paid
off on the `instantiate_asset` finding earlier -- and it already says,
verbatim, `inputs[i] applies only to simulation frame i; omitted later
frames receive neutral input, so repeat a held semantic sample for every
frame it must remain down`. The documentation was correct both times; the
mistake was mine, not re-reading it before constructing a second harness.

What *was* a real gap: hitting this produces a plausible-looking, silently
wrong result with nothing in the response indicating anything went wrong
-- documented behavior is not the same as detectable behavior. Fixed by
adding a structured `REKALL_RUNTIME_INPUT_FRAMES_EXHAUSTED` warning
observation in `RekallAgeRuntimeSnapshotService.InspectSceneTimelineAsync`
whenever `0 < inputs.Count < frames`, naming exactly which frames received
no input and repeating the fix. Verified empirically (the warning now
appears on the exact repro that previously produced silent wrong Pong
`reset` behavior earlier this session) and covered with two new tests
(the warning firing, and its absence when supplied frames fully cover the
run). Committed separately from any game content, full suite re-verified
green (1672/1672 + 55/55) after.

### Galaga 3D: an original 3D space-shooter, carrying forward Pong's lessons

Authored `Examples/Galaga3D/` directly from the start (skipping the
sandbox-then-promote round trip that caused Pong's stale-catalog detour),
scaffolded both `rekall.module.scaffold_runtime_system` and
`rekall.module.scaffold_playable` up front (the lesson from discovering
Pong needed both only after a native-player crash), and wired the HUD
before writing gameplay logic rather than after.

Two distinct Model Assets -- a player ship (frustum body + box wings + two
angled frustum fins) and an enemy ship (sphere body + torus ring + two
frustum legs), each combined via `rekall.modeling.join` rather than
`rekall.modeling.boolean` (avoids CSG failure risk on disjoint, barely-
touching primitives; not needed for a stylized low-poly silhouette
anyway) -- published and placed exactly like Pong's ball/paddle, again
exercising the priority #3 render+physics Model Asset resolution path.
The result reads as recognizable ship silhouettes on the first bake, no
iteration needed (unlike Pong's lighting, which needed several passes).

`GalagaRulesSystem` is a materially different exercise of the runtime SDK
than `PongRulesSystem`: Pong's entities were all fixed and pre-placed at
scene-authoring time, but Galaga needs projectiles that are created and
destroyed during actual gameplay. Confirmed
`RekallAgeRuntimeModuleSdk.CreateEntity`/`.AddEntity`/`.RemoveEntity`/
`.UpsertComponent` support this genuinely (discovered by reading the
scaffolded module's own generated comments, which explicitly document the
runtime-spawning pattern) -- built a full bullet entity from scratch each
frame a fire input is live and cooldown has elapsed, including its own
`Rekall.GeometryPrimitive`/`Rekall.MeshRenderer`/`Rekall.Transform3D`.
Bullet entity IDs are derived from `world.FrameIndex` (deterministic,
replay-safe) rather than a mutable instance counter on the system, since
it is not established whether `IRekallAgeRuntimeModuleSystem` instances
are reused or recreated across frames and a wrong assumption there would
silently produce colliding IDs (`AddEntity` no-ops on a duplicate id with
no error).

Verified via real deterministic `runtime inspect` execution, not by
hand-tracing the logic: firing/scoring confirmed over a 300-frame run with
`fire`+`player.move` held on every frame (via a properly repeated
`Inputs` array, not the single-entry mistake described above) --
`SCORE 200`, two enemies destroyed, HUD text matching. The enemy-hits-
player branch was the one genuine open question (the advisor flagged that
the dive path's sway term has no explicit homing toward the player and
might be geometrically unreachable from a stationary player, the same
class of bug as Pong's dead-center serve) -- checked with a real 2000-
frame idle run (zero input at all) and a declarative assertion rather than
assuming either way: `lives` assertion `less-than 3` passed with
`actual: 1`, confirming the branch fires without needing any homing fix.
Visually verified with a cross-backend capture (`render viewport capture`
has no input parameter, so this used a frame reached by an enemy's own
dive timer rather than player-fired bullets) at frame 220, where one enemy
has visibly broken formation mid-dive; vulkan and software compositions
agree.

Real, distinct CLI/MCP finding, not yet fixed: `rekall.play.capture_frame`
and `rekall.workflow.capture_playable_package_frame` both hardcode an
`{deltaSeconds, primaryAction, verticalAxis}` input shape tied to the
`ClockworkCanopy` reference platformer's controls, not a generic shape
driven by a project's own declared `Rekall.InputActionMap` actions (the
way `rekall.runtime.inspect_scene`'s `PressedKeys`/`SemanticActions` shape
is). A client authoring any game whose inputs aren't literally "vertical
axis + one action button" -- Galaga's `fire`/`player.move`, Pong's
`paddle.move`/`reset` -- cannot drive real gameplay through either
capture/run/audit workflow tool at all. This blocked getting a
projectile-in-flight visual capture for Galaga specifically. Not fixed in
this session: redesigning those two tools' input contract to accept the
same generic shape `runtime inspect` already uses is a larger, standalone
change, not a small patch, and deserves its own dedicated pass rather than
being folded in here.

Not yet done for Galaga: package/relocate/audit workflow; a direct
projectile-vs-enemy visual capture (blocked by the finding above); native
Windows player launch attempt (expected to hit the same console-
redirection artifact already documented for Pong, given the same
environment); a dedicated evidence archive under `Artifacts/`.

## 2026-08-24 Pong3D Windows package accepted

Pong3D now completes the package, archive, relocation, audit, semantic-input
capture, and native-launch portion of its acceptance path. The graphical
package at `artifacts/acceptance/Pong3D-Windows` exposes a self-contained
`Play.exe` plus quoted `Play.bat`; its 48.2 MiB archive inspected ready with
311 integrity-inventoried files and `Play.exe` as the manifest launch path.
Relocation from the archive to
`artifacts/acceptance/Relocated/Pong3D-Windows` passed integrity verification.

The relocated audit passed manifest readiness, key artifacts, packaged scene
validation, deterministic run, capture, non-blank frame, layout integrity, and
informative-frame checks (93 distinct colors). A canonical `inputFrames`
capture drove three repeated `paddle.move` semantic-action frames and produced
`artifacts/acceptance/Evidence/Pong3D/package_play_frame_003.png` with 13 draw
commands. Starting the relocated `Play.exe` with no arguments from an unrelated
working directory reached and remained in graphical play startup until the
bounded acceptance process ended it.

## 2026-08-24 Galaga3D Windows package accepted

Galaga3D now completes the same delivery path independently. The graphical
package at `artifacts/acceptance/Galaga3D-Windows` exposes the same generic,
manifest-driven `Play.exe`/`Play.bat` contract; its 48.1 MiB archive inspected
ready with 311 integrity-inventoried files and relocated successfully to
`artifacts/acceptance/Relocated/Galaga3D-Windows`.

The relocated audit passed every readiness, validation, run, capture, layout,
and informative-frame check (44 distinct colors). A canonical `inputFrames`
capture drove repeated `player.move` plus pressed/held `fire` semantic actions
and produced
`artifacts/acceptance/Evidence/Galaga3D/package_play_frame_003.png` with 18 draw
commands. Its relocated no-argument `Play.exe` also reached and remained in
graphical play startup from an unrelated working directory.

The generic engine work required to close both packages is verified, not just
source-inspected: package capture now accepts semantic actions, physical key
facts, edges, and per-frame deltas while preserving the legacy playback input
field; Vulkan capture reports the same action/timing diagnostics; fixed-step
elapsed time remains exact; dynamic command JSON and recovery suggestions retain
canonical frames. The Windows player resolves its adjacent bounded manifest,
rejects traversal and reparse-point game roots, and publishes self-contained.
Final Release verification passed 1,688/1,688 engine tests and 55/55 Studio
tests, and independent reviews report no remaining Critical or Important
findings.

## Evidence index

- `docs/production/2026-08-17-engine-maturity-audit.md`
- `docs/production/2026-08-17-ollama-authoring-benchmark.md`
- `docs/production/2026-08-22-rendering-device-migration.md`
- `docs/superpowers/specs/2026-08-22-module-gpu-workload-design.md`
- `docs/superpowers/plans/2026-08-22-module-gpu-workloads.md`
- `docs/superpowers/plans/2026-08-17-runtime-subsystems.md`
- `docs/superpowers/specs/2026-08-18-runtime-soak-performance-design.md`
- `docs/superpowers/plans/2026-08-18-runtime-soak-performance.md`
- `docs/superpowers/specs/2026-08-18-persisted-compatibility-migrations-design.md`
- `docs/superpowers/plans/2026-08-18-persisted-compatibility-migrations.md`
- `docs/superpowers/specs/2026-08-18-package-archive-preflight-security-design.md`
- `docs/superpowers/plans/2026-08-18-package-archive-preflight-security.md`
- `docs/production/package-trust-and-archive-security.md`
- `docs/superpowers/specs/2026-08-18-animation-state-graph-design.md`
- `docs/superpowers/specs/2026-08-18-cubic-animation-interpolation-design.md`
- `docs/superpowers/plans/2026-08-18-cubic-animation-interpolation.md`
- `docs/superpowers/specs/2026-08-18-morph-target-runtime-design.md`
- `docs/superpowers/plans/2026-08-18-morph-target-runtime.md`
- `docs/superpowers/specs/2026-08-18-atomic-persisted-json-design.md`
- `docs/superpowers/plans/2026-08-18-atomic-persisted-json.md`
- `eng/accept-installed-atomic-json.ps1`
- `docs/superpowers/specs/2026-08-18-optimistic-document-revisions-design.md`
- `docs/superpowers/plans/2026-08-18-optimistic-document-revisions.md`
- `eng/accept-installed-document-revisions.ps1`
- `eng/accept-installed-document-recovery.ps1`
- `Artifacts/TestResults/release-pass-1.trx`
- `Artifacts/TestResults/release-pass-2.trx`
- `Artifacts/Distribution/Rekall-AGE-0.1.0-preview.1-win-x64.zip`
- `eng/accept-installed-skeletal-animation.ps1`
- `Artifacts/InstalledSkeletalProof/<run-id>/evidence.json`
- `eng/accept-installed-morph-animation.ps1`
- `Artifacts/InstalledMorphProof/isolated-pass/evidence.json`

## Update rule

At every verified milestone, update the timestamp, verified status, current
gaps, in-progress item, and next item in this file in the same commit as the
milestone documentation or immediately after the verification completes.
