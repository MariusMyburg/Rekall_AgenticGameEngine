# Curve Revolve Authoring Design

Date: 2026-08-26

Status: approved for implementation under the standing controller preapproval

## Purpose

AGE can sweep fixed circle/rectangle profiles along editable curves, but agents
cannot revolve an arbitrary editable profile into rotationally symmetric mesh
forms. That omission forces armor shells, columns, urns, bowls, wheels, and
architectural capitals to be assembled from coarse fixed primitives. The
result is visible in Aetherfall's blocky Warden and repetitive ruin dressing.

Add one generic typed Curve-to-Mesh node. It must improve the engine contract,
not add a Warden or ruin generator.

## Reference architecture

Blender's BMesh Spin operator and Screw modifier establish the useful boundary:
the editable source profile remains separate from evaluated rotational mesh
topology. AGE follows that concept without copying GPL implementation. Godot's
mesh/render storage reinforces the downstream boundary: positions, indices,
UVs, normals, tangents, and material surfaces are explicit renderer inputs,
not the editable source of truth.

## Public contract

Register `rekall.modeling.curve.revolve@1` with:

- one required `curve: Curve` input;
- one `geometry: Geometry` output;
- `axis`: `x`, `y`, or `z`, default `y`;
- `origin`: finite Vector3, default `[0,0,0]`;
- `angleDegrees`: greater than zero and at most `360`, default `360`;
- `segments`: integer `3..4096`, default `32`;
- `weldDistance`: finite world-unit distance `0..1`, default `0.000001`;
- `materialAssetId`: nonempty asset ID, default `material.default`;
- `slotName`: nonempty display name, default `Revolved Surface`.

The input accepts exactly one evaluated spline with at least two distinct
samples. Both open and cyclic profiles are valid. A full 360-degree revolution
wraps the angular seam; a partial revolution retains two open angular
boundaries. This first node does not invent caps for partial sweeps or open
profile ends. Agents close the source profile or compose caps explicitly when
they need a solid.

## Evaluation and topology

Rotate every evaluated profile sample about the selected axis through evenly
spaced angular rings. For full revolutions create `segments` rings and wrap;
for partial revolutions create `segments + 1` rings. Samples whose radial
distance from the axis is within `weldDistance` share one stable evaluated
point across all rings. When axis welding collapses a quad, emit the remaining
nondegenerate triangle; never retain repeated-index or zero-area faces.

Connect adjacent profile samples and angular rings with consistently wound
quads/triangles. A cyclic profile connects its final sample back to its first.
Reject non-finite input, coincident profile spans, overflow, and a projected
result above 2,000,000 points or faces before allocating the output.

Stable evaluated IDs are deterministic for identical graph revision, node,
profile samples, parameters, and evaluation context. The node preserves the
usual graph source/evaluated/cooked separation and emits no runtime behavior.

## Attributes and materials

Emit:

- corner-domain `uv.generated` with semantic `texcoord-0`; U follows angular
  travel and uses distinct corner values at the wrapped seam, while V follows
  normalized cumulative profile arc length;
- point-domain `curve.source.span` provenance for the originating evaluated
  curve sample;
- point-domain `revolve.angle` finite scalar metadata;
- face-domain `material.index = 0` and exactly one authored material slot;
- face-domain `normal.smooth = true`, allowing the existing auto-smooth and
  split-weighted-normal policy to preserve caps or explicitly sharp composed
  boundaries later.

The compiler remains responsible for consuming explicit normals/tangents; the
revolve node does not bypass the normal toolchain.

## Agent and Studio access

The canonical node catalog is the single discovery surface for CLI, MCP,
embedded agents, and Studio's descriptor-driven parameter editor. No bespoke
Studio panel or game-specific command is introduced. Catalog/schema tests must
prove the ports, parameter types, ranges, units, and axis choices.

## Aetherfall acceptance

Use ordinary revision-checked AGE graph/bake/rebuild commands to add at least
two visible consumers:

1. a layered revolved Warden cuirass or helmet form that materially improves
   the hero silhouette over stacked boxes;
2. a revolved architectural prop or capital used in the citadel location.

Both consumers must pass through material assignment, auto smoothing, split
weighted normals, model publication, and the real Vulkan player. Acceptance
requires focused node/topology/compiler tests, strict Aetherfall asset
assertions, an inspected High capture with zero missing assets/fallbacks/
observations or black-dot noise, unchanged 2/4/4/5 gameplay proofs, zero
project/scene validation issues, and the `desktop60` budget.

This tranche is successful only if the capture gains recognizable form detail;
polygon growth without a clearer silhouette does not count.

## Deferred extensions

The following remain explicit later work: mesh-selection Spin, screw/helix
translation, per-ring twist/scale fields, automatic partial-sweep caps,
multi-spline output, profile thickness, interactive Studio axis gizmos, and a
general surface-of-revolution modifier. Their absence must remain visible in
the modeling capability matrix.
