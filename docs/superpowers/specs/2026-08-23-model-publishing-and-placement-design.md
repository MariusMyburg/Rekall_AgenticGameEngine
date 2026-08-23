# Rekall AGE Model Publishing and Placement Design

## Purpose

Rekall AGE must let a human or AI agent create and edit a model in the
Modeling workspace, publish it as a stable game asset, place it in scenes,
attach agent-authored C# behavior and other components, and continue editing
the source model without breaking existing world or prefab references.

The engine must preserve a clean separation between geometry authoring,
runtime resources, scene instances, and reusable gameplay composition.

## Architectural decision

Use a live-linked published Model Asset between modeling source documents and
world entities. Publishing is a canonical, revision-checked transaction; it
does not move or destroy the editable source. The published asset records its
source dependency and deterministic build result. Later source changes mark it
stale and rebuild it without changing the stable Model Asset ID.

Dragging a Model Asset into a world creates ordinary entities referencing that
stable ID. Gameplay behavior remains attached through generic entity
components supplied by built-in systems or agent-authored C# modules. A
configured entity hierarchy can then be saved as a Prefab. Model Assets are
visual/physical resources; Prefabs are reusable gameplay compositions.

Users may explicitly freeze a Model Asset revision or detach an instance when
an immutable snapshot is required. Live linking is the default.

## Four-layer lifecycle

### 1. Editable modeling source

Modeling source remains under the revisioned modeling stores and may be one of:

- an editable mesh asset;
- a procedural modeling graph output;
- a mesh with a modifier stack;
- a compound model containing multiple named parts;
- related material graphs, material instances, skeletons, animations, LOD
  sources, and collision-authoring data.

These documents are authoring truth. Stable topology and node identifiers must
remain inspectable by both Studio and MCP tools.

### 2. Published Model Asset

Add a versioned `RekallAgeModelAssetDocument` stored under
`Assets/Models/<asset-id>.age.model.json` and indexed by the project asset
catalog. It contains:

- stable asset ID, display name, logical revision, and schema version;
- source reference: source kind, source asset ID, optional graph output, and
  optional modifier stack;
- source dependency revisions and content fingerprints;
- deterministic build state: current, stale, building, or failed;
- compiled resource references and content hashes;
- named material slots and their default material-asset references;
- local origin/pivot, bounds, unit scale, and coordinate convention;
- optional LOD entries;
- optional collision recipe and generated collision-resource reference;
- optional skeleton, animation, and morph-target resource references;
- thumbnail reference;
- freeze policy and last successful build evidence;
- bounded diagnostics from the latest build.

Derived GPU meshes, collision data, thumbnails, and other compiled outputs are
rebuildable artifacts. The Model Asset document and modeling sources are the
persistent truth.

### 3. World instance

Dragging or programmatically placing a Model Asset executes one canonical,
undoable transaction that creates an entity or entity hierarchy. A simple
model receives at least:

- `Rekall.Transform3D`;
- `Rekall.ModelAssetReference` with the stable Model Asset ID;
- `Rekall.MeshRenderer`;
- material-slot overrides only when the instance differs from the asset;
- collider and rigid-body components only when requested by placement defaults
  or by the author.

Complex published models may instantiate a named node hierarchy. Each generated
entity records its source node ID so rebuilds and diagnostics can match nodes
without relying on names. Scene instances never copy compiled geometry into the
scene document.

Placement accepts an explicit transform for agents. Studio drag/drop derives a
deterministic transform from the viewport ray, selected surface, grid snapping,
and asset pivot, and previews the placement before committing it.

### 4. Gameplay Prefab

An author may add built-in or agent-authored component types to placed entities.
Agent-authored C# modules continue to define generic component schemas and
runtime systems; the model publishing system does not invent or embed a
genre-specific script behavior contract.

Saving the configured entity or hierarchy as a Prefab captures component data,
children, asset references, and permitted overrides. Prefab instances retain
the same stable Model Asset reference, so a model rebuild updates their visual
resource without discarding gameplay configuration.

Prefab persistence must evolve from the current root-entity-only document to a
versioned hierarchy document before compound model placement is considered
complete.

## Canonical commands and MCP surface

The first implementation tranche exposes generic bounded commands:

- `rekall.asset.model.publish` — create a live-linked Model Asset from a mesh,
  graph output, or supported compound modeling source;
- `rekall.asset.model.rebuild` — revision-check dependencies and atomically
  replace derived outputs while retaining the stable asset ID;
- `rekall.asset.model.inspect` — report source, outputs, material slots,
  bounds, build state, dependency health, and bounded diagnostics;
- `rekall.asset.model.list` — list published model assets and health;
- `rekall.asset.model.freeze` — pin or unpin the published source revision;
- `rekall.scene.instantiate_asset` — place a published asset with an explicit
  scene, transform, optional parent, and bounded placement overrides;
- `rekall.level.prefab.create_from_hierarchy` — persist a configured hierarchy;
- `rekall.level.prefab.apply_overrides` and
  `rekall.level.prefab.revert_overrides` — manage reusable gameplay composition.

Commands must publish changed resources through ordinary AGE transactions,
support optimistic revision checks, expose schemas to MCP, and return actionable
next operations. Studio must call these same commands rather than implementing
a separate publishing path.

## Studio interaction

### Modeling workspace

The Modeling header presents a primary `Publish to Assets` action. Before first
publication it opens a compact panel for asset ID, display name, source output,
pivot, materials, collision, and rebuild policy. After publication it becomes
`Update Asset` and shows one of Current, Stale, Building, Frozen, or Failed.

The right-side asset properties show source and compiled revisions, dependency
health, material slots, bounds, triangle/vertex counts, collision summary, and
build diagnostics. `Locate in Assets` switches to the published asset. Double
clicking a published Model Asset returns to its source modeling document.

Successful source saves mark dependent Model Assets stale. Studio schedules a
debounced background rebuild for assets configured for automatic rebuilding.
Explicit Update remains available and is the deterministic recovery path.
Failed builds retain the last successful runtime output, display the failure
prominently, and never partially replace asset data.

### Asset browser

The World workspace gains a filterable asset browser with thumbnails, kind,
build-health badge, tags, and source relationship. Model Assets and Prefabs are
distinct kinds. Stale assets remain placeable using the last successful output
but display a warning; assets without any successful build are not placeable.

Dragging a Model Asset over the viewport displays a ghosted placement preview.
Dropping executes `rekall.scene.instantiate_asset`. Dragging a Prefab executes
the prefab-instantiation command. Both operations are fully undoable.

### Inspector and component attachment

The Inspector exposes `Add Component`, populated from built-in schemas and
compiled agent-authored module schemas. Adding a C# gameplay component modifies
the selected entity, not the Model Asset. Authors can then create a Prefab or
apply the instance changes to an existing Prefab.

Model material slots, collision policy, and placement defaults can be edited at
the Model Asset level. Per-instance overrides are explicit, inspectable, and
revertible. Studio visually distinguishes asset defaults, prefab values, and
scene overrides.

## Dependency and rebuild model

Each successful build records exact source document revisions and content
fingerprints. Dependency status is derived by comparing current sources with
that manifest; timestamps are not authoritative. Build inputs are normalized
and outputs are deterministic for identical inputs and engine/compiler version.

Publishing proceeds as an atomic transaction:

1. load and revision-check the Model Asset and all declared sources;
2. validate and evaluate modeling data within production budgets;
3. compile mesh, materials, collision, LOD, animation, and thumbnail outputs in
   a staging location;
4. validate the complete staged result;
5. atomically publish outputs and the new Model Asset revision;
6. update the catalog and record every changed resource;
7. notify Studio/runtime asset watchers after commit.

If any stage fails, no new revision is published. Existing scenes continue to
use the prior successful output. Diagnostics identify the source document,
stable source element or node where possible, failing stage, and repair action.

## Runtime resolution

Runtime systems resolve `Rekall.ModelAssetReference` through a shared Model
Asset resolver rather than teaching each renderer or physics system about
modeling files. The resolver supplies compiled render meshes, material bindings,
collision data, skeletons, animations, bounds, and a content revision.

Running Studio previews may hot-reload a successfully rebuilt revision at a
safe frame boundary. Packaged builds consume audited compiled outputs and do not
require editable modeling sources unless a development package explicitly asks
for them.

## Agent-first requirements

An agent must be able to perform and verify the entire lifecycle without UI
automation:

- inspect source and determine whether it is publishable;
- publish and inspect the Model Asset;
- place it at an explicit transform;
- attach an agent-owned component through generic scene mutation tools;
- optionally create a Prefab from the hierarchy;
- edit the source and rebuild;
- prove that existing scene instances retain their stable reference and render
  the new compiled revision;
- run deterministic runtime gameplay assertions after attaching behavior.

Tool responses must report stable IDs, revisions, dependency status, changed
resources, warnings, and exact next actions. No command may ask the engine to
author gameplay content for the agent.

## Validation and acceptance

The implementation is not complete until automated tests prove:

- first publish and revision-checked rebuild;
- deterministic byte-equivalent outputs from identical inputs;
- stale detection from dependency fingerprints;
- atomic failure retaining the last successful output;
- frozen revision behavior;
- catalog discovery and Studio presentation;
- drag/drop and command placement producing equivalent scene entities;
- undo/redo of placement;
- simple and hierarchical model placement;
- material-slot and per-instance override behavior;
- collision recipe resolution;
- Prefab creation preserving the stable Model Asset reference and attached
  agent-owned components;
- source rebuild updating all instances without deleting gameplay data;
- packaging includes compiled outputs and audits missing/stale dependencies;
- deterministic runtime inspection proves attached agent-authored behavior.

A rendered Studio acceptance frame must show a source model, its published asset
state, the asset browser entry, and an instantiated world entity. Source-only
tests or a successful build do not prove the user workflow.

## Delivery order

1. Model Asset contract, store, dependency manifest, validation, and catalog
   integration.
2. Deterministic publishing/rebuilding commands and shared runtime resolver.
3. Generic scene asset-instantiation command for simple models.
4. Modeling publication controls, Asset Browser, drag/drop preview, and undo.
5. Inspector component attachment and Model Asset/prefab/instance override UX.
6. Hierarchical Prefab v2 and compound model placement.
7. Hot reload, packaging audit, performance budgets, and complete acceptance
   gauntlet.

