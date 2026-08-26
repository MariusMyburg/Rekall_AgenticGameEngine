# Runtime 3D Hierarchy Articulation Design

## Purpose

Aetherfall proves that a detailed static character is not enough: authored
weapons, armor plates, limbs, cameras, lights, fog, and effects need to move as
parts of a coherent object. AGE scene entities already persist `parentId`, and
generic animation clips already change `Rekall.Transform3D`, but the runtime
render-frame builder currently projects every 3D transform as world-space. A
child therefore does not follow or rotate around its parent.

This slice makes entity hierarchy useful for visible rigid articulation now.
It complements AGE's existing GLB skeletal-animation and skin-deformation path;
it does not pretend to replace native armatures, weight painting, or deformable
procedural meshes, which remain a later modeling tranche.

## Decision

Resolve render-world 3D transforms from ordinary entity-local transforms and
`parentId`. Compose each local matrix as scale, X/Y/Z Euler rotation in degrees,
then translation, matching AGE's Vulkan model-matrix convention. A child's world
matrix is `local * parentWorld` under `System.Numerics` row-vector semantics.

The resolver is a rendering service with one responsibility: map a runtime
entity ID to a stable world transform for the current immutable runtime world.
It caches successful resolutions for the frame. Cameras, 3D meshes, lights,
particles, and fog use the same result. Existing unparented scenes remain
byte-for-byte compatible at the viewport contract.

## Failure behavior

- A missing parent leaves the child at its authored local transform and emits
  one viewport observation with code `runtime.transform.parent_missing`.
- A parent cycle leaves each affected entity at its authored local transform and
  emits `runtime.transform.parent_cycle` rather than recursing indefinitely.
- A matrix that cannot be decomposed into finite translation, rotation, and scale
  falls back to the local transform and emits `runtime.transform.compose_failed`.
- These are render diagnostics; they do not mutate the authored scene or runtime
  gameplay world.

## Authoring and gameplay use

Rigid articulated characters are ordinary scene hierarchies. A root entity owns
gameplay state and locomotion. Child entities own local-pivot transforms, model
references, and generic animation clips. Agent-authored modules may change those
components for state-driven attacks, but the engine does not contain attack,
weapon, limb, or genre behavior.

Aetherfall will use this contract first with a detailed separate Warden weapon
attachment and at least one armor/limb attachment. Their local clips must create
a readable idle/combat silhouette while following Warden movement. Existing
strict movement and combat assertions remain authoritative gameplay evidence;
a real Vulkan capture is required for visual acceptance.

## Deferred native deformation

The next native modeling tranche will preserve generic point/corner joint-index
and joint-weight attributes through compiled meshes, add armature/skin assets and
agent/Studio weight-authoring operations, and feed the existing runtime skinning
path. That work should reuse this hierarchy for bones and attachments rather than
inventing a competing transform graph.

## Acceptance

- A test proves that parent translation, rotation, and scale affect a child 3D
  renderable at its local pivot.
- Tests prove missing-parent and cyclic hierarchies terminate with stable
  diagnostics and local fallback.
- Aetherfall contains visible child attachments driven by ordinary animation
  components and parented to `AetherWarden`.
- The attachment follows deterministic Warden movement in runtime inspection.
- A real High-quality Vulkan combat capture visibly shows articulated motion with
  no blocking render or asset observations.
- Focused rendering/runtime tests, scene validation, render budget, and strict
  movement/combat/progression/reset gameplay checks pass after the scene change.

