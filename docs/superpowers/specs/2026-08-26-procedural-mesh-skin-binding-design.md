# Procedural Mesh Skin Binding Design

## Outcome

AGE-authored editable meshes can carry four joint indices and four normalized
weights per point through compilation and render projection, then deform from
the same generic `Rekall.SkeletonPose` contract already used by imported GLB
assets. This closes the first native procedural-character gap without adding
character or game logic to engine core.

## Reference boundaries

Blender keeps deform groups/weights as mesh-owned per-vertex data and applies
armature deformation as a later evaluated stage. Godot keeps bone indices and
weights on mesh surfaces while the skeleton supplies the current pose. AGE will
follow the same separation:

- editable mesh: stable point-domain weight attributes;
- compiled mesh: render-ready four-influence bindings copied to every emitted
  corner vertex;
- runtime entity: generic pose matrices independent of the mesh source;
- renderer: existing bounded CPU skin deformation before world transforms.

No Blender or Godot code is copied.

## Authoring contract

Two point-domain attributes are canonical:

- semantic `joint-indices-0`, value type `Int4`;
- semantic `joint-weights-0`, value type `Float4`.

Both must be present together. Joint indices must be non-negative. Weights must
be finite and non-negative; at least one influence must be positive. Compilation
normalizes the four weights. A malformed pair fails compilation with a precise
diagnostic rather than silently producing unstable deformation.

The four-influence limit matches the existing AGE/GLB render binding and is a
practical first production contract. Rich vertex groups and automatic envelope
weight authoring will reduce into this cooked representation later.

## Data flow

`RekallAgeCompiledMeshVertex` gains optional joint indices and weights. Runtime
viewport geometry gains a parallel binding array so the ordinary compiled-model
resolver preserves the data. `RekallAgeVulkanSceneMeshBuilder` applies the
existing skin routine to authored geometry exactly as it does to imported GLB
meshes.

Old mesh JSON remains compatible because absent bindings retain rigid behavior.
Morphing remains before skinning, matching the current GLB path.

## Acceptance

- compiler tests prove point weights survive corner expansion and normalize;
- invalid paired attributes fail with stable codes;
- compiled asset resolution exposes the bindings;
- a two-joint authored mesh visibly bends under `Rekall.SkeletonPose` matrices;
- unweighted procedural meshes render byte-for-byte through the prior path;
- focused modeling/rendering suites pass.

## Deferred follow-on

Native rig documents, bone hierarchy editing, automatic envelope weights,
weight-paint strokes, mirror/normalize tools, constraints, and Studio overlays
remain Wave 3 work built on this contract. The next Aetherfall consumer should
use a simple torso/limb bend before broadening the tool surface.
