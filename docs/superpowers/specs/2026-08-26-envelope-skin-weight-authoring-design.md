# Envelope Skin-Weight Authoring Design

## Purpose

AGE can currently author only an axis-aligned blend between two joints. That is
enough to prove deformation, but it cannot author ordinary multi-joint
characters. Aetherfall exposes this directly: its Warden has a native rig but
cannot deform its limbs convincingly for locomotion or combat.

This slice adds a generic authoring primitive. It does not add character,
humanoid, locomotion, or combat behavior to engine core.

## Reference principles

Blender's `armature_deform.cc` computes envelope influence from distance to a
bone segment, interpolates head/tail radii along that segment, gives full
influence inside the radius, and applies quadratic falloff outside it. Godot's
mesh/render contracts bound ordinary vertex skinning to four joint indices and
four normalized weights. AGE combines those principles in a deterministic,
inspectable authoring operation.

## Public contract

The mesh operation `assign_envelope_skin_weights`, graph node
`rekall.modeling.skin.envelope_weights`, and modifier
`rekall.modifier.skin.envelope_weights` accept:

- `envelopes`: one through 256 objects with `jointIndex`, `head`, `tail`,
  `headRadius`, `tailRadius`, `falloff`, and optional `weight` (default `1`).
- `maximumInfluences`: one through four, default four.
- `fallbackToNearest`: default true.
- the normal point selection or named point-selection contract.

Coordinates and scalar values must be finite. Joint indices are non-negative;
radii and envelope weights are positive; falloff is non-negative. Degenerate
head/tail segments are valid and behave as point envelopes using the head
radius.

## Weight calculation

For every selected point and envelope:

1. Project the point onto the finite head-to-tail segment.
2. Interpolate radius between `headRadius` and `tailRadius` at the clamped
   projection.
3. Return full influence inside the radius.
4. Return zero beyond `radius + falloff`.
5. Between those distances, return
   `1 - ((distance - radius)^2 / falloff^2)`.
6. Multiply by the envelope's authored `weight`.

Multiple envelopes targeting the same joint resolve to their maximum influence.
AGE orders candidates by descending influence and then ascending joint index,
keeps `maximumInfluences`, normalizes them, and pads the `Int4`/`Float4` payload
with zeros. This deterministic tie-break makes cache and source-control output
stable.

If every influence is zero and `fallbackToNearest` is true, the point binds
fully to the joint whose envelope surface is nearest, with joint-index tie-break.
If false, the mutation fails without replacing the mesh and identifies the
unbound stable point.

## Attribute and mutation semantics

The operation creates or replaces the canonical point-domain bindings:

- `skin.joints`, `Int4`, semantic `joint-indices-0`, nearest interpolation.
- `skin.weights`, `Float4`, semantic `joint-weights-0`, normalized-linear
  interpolation.

It follows the existing linear-weight rules for incomplete semantic pairs,
case-insensitive duplicates, unrelated canonical-name collisions, selection
preservation, revision increments, change sets, compilation, and unknown
attribute preservation.

## Aetherfall acceptance

After the engine contract is green, the Warden rig may expand to stable named
spine and limb joints. Its graph consumes envelope weights and the
agent-authored module emits named joint deltas from the engine-provided runtime
clock.
Acceptance must prove both the four-influence compiled payload and actual
rendered vertex movement under representative input; component presence alone
is insufficient.
