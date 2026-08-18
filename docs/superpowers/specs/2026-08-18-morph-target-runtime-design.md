# Bounded Morph-Target Runtime Design

**Date:** 2026-08-18

**Status:** Approved by the user's standing authorization for autonomous engine
architecture decisions

## Purpose

Add a complete, genre-neutral morph-target path from glTF asset import through
agent discovery, runtime animation, inspection, CPU deformation, skinning, and
installed Vulkan proof. The engine exposes weights and diagnostics; it does not
generate facial expressions, lip sync, creatures, or any other game-specific
behavior.

## Chosen Architecture

Introduce `Rekall.MorphWeights` as a small generic component on the same entity
as `Rekall.MeshRenderer`. Its finite numeric `Weights` array is an ordinary
component property, so the existing animation clip, mixer, cubic sampler, and
state graph can animate morphs without a parallel animation framework.

The glTF mesh loader preserves bounded POSITION and NORMAL deltas plus imported
default weights and target names in each loaded mesh. The runtime validates the
authored weight array and publishes inspectable `Rekall.MorphState`. Render
projection carries that state to the renderer. CPU mesh preparation applies
morph deltas first and skeletal skinning second, matching glTF deformation
order, before the existing Vulkan upload path.

This tranche intentionally does not add a new `ModelAnimator`, attach morphs to
`SkeletalAnimator`, or treat runtime projection as implementation. Those paths
would either destabilize proven contracts or make unskinned morph models
unnatural.

## Public Authoring Contract

The built-in component is:

```json
{
  "type": "Rekall.MorphWeights",
  "properties": {
    "Weights": [0.0, 0.75, -0.1]
  }
}
```

`Weights` contains 1 to 64 finite numbers with absolute value at most 1,000,000.
Values are not clamped to 0..1: glTF permits extrapolation and agents may
intentionally author it. The broad magnitude ceiling keeps CPU/GPU float
conversion bounded rather than imposing an artistic range.
Strings, nulls, objects, nested arrays, non-finite numbers, and more than 64
entries fail closed. Omitting the component uses imported node/mesh defaults.
Adding the component means its array is an explicit complete override and must
exactly match the asset's morph-target count.

An engine-general runtime system runs after ordinary animation sampling. It
validates `Rekall.MorphWeights` without loading renderer code, persists only:

```json
{
  "version": 1,
  "weights": [0.0, 0.75, -0.1]
}
```

as runtime-only `Rekall.MorphState`, removes stale state after invalid input or
component removal, and emits a bounded `runtime.animation.morph_weights_invalid`
observation without mutating authored content. Exact target-count compatibility
is checked when the model asset and renderable meet; a mismatch produces a
bounded render-asset issue and the renderer uses imported defaults rather than
partially applying an override.

## glTF Asset Contract

Version 1 supports morph target `POSITION` and optional `NORMAL` float VEC3
accessors. `TANGENT`, sparse accessors, quantized accessors, and non-triangle
primitives remain unsupported and are rejected when present on a morph-bearing
primitive rather than silently discarded.

Limits are:

- 64 targets per primitive;
- 4,194,304 total target delta vectors per primitive across POSITION and
  NORMAL data;
- exact base-vertex count for every POSITION/NORMAL target accessor;
- finite delta components with absolute value at most 1,000,000;
- 128 characters per target name; and
- one compatible target layout per rendered model asset: all morph-bearing
  primitives must expose the same target count and ordered names.

The compatible-layout rule lets one generic weight array drive a compound model
without inventing mesh-specific component paths. Assets with genuinely
different morph sets must be split into separately rendered model assets in
version 1. Non-morph primitives may coexist with the compatible morph set.

Names come from bounded `mesh.extras.targetNames` when present; otherwise the
engine exposes deterministic `target-0`, `target-1`, and so on. Mesh-level
`weights` are the imported defaults, overridden by node-level `weights` for a
loaded node instance. Counts must exactly match the target layout; absent
defaults are zero. The asset import/inspection report exposes morph target
count, ordered names, defaults, supported semantics, and limitations so an
agent never has to reverse-engineer binary accessors.

The loader transforms POSITION deltas with the node transform's linear portion
and NORMAL deltas as normals; translation is never applied to a delta. Chunking
and index remapping keep every target delta aligned one-to-one with the emitted
vertex array.

## Runtime and Rendering Data Flow

```text
Rekall.MorphWeights
  -> runtime.animation.morph validation
  -> Rekall.MorphState
  -> runtime viewport MorphWeights
  -> asset/target-count compatibility check
  -> base vertex + weighted morph deltas
  -> skeletal skinning (when present)
  -> virtual-geometry reduction/material binding
  -> Vulkan upload and draw
```

`RekallAgeVulkanSceneMesh` gains immutable target records containing an ordered
name plus position/normal delta arrays, and imported default weights. Runtime
renderables gain a nullable bounded morph-weight record. Mesh preparation uses
the explicit runtime override only when its count exactly matches; otherwise it
uses defaults. For each vertex:

```text
position = basePosition + sum(target.positionDelta * weight)
normal   = normalize(baseNormal + sum(target.normalDelta * weight))
```

Missing NORMAL deltas contribute zero. A near-zero resulting normal falls back
to the base normal. The explicit target, delta, and weight magnitude limits keep
the weighted sum finite; mesh preparation still verifies every result and
fails closed to the base/default mesh if that invariant is ever violated. No
non-finite value may reach GPU buffers.

Morphing occurs before `ApplySkin`. Procedural primitives, authored inline
geometry, sprites, UI, and assets without morph targets remain byte-compatible.

## Agent Inspection

Built-in schema descriptions name the exact weight shape, non-clamping policy,
64-target limit, same-entity mesh relationship, and existing generic animation
paths. Asset inspection exposes target names/defaults. Runtime projection and
CLI inspection expose entity id/name, active target count, bounded weights,
whether weights are imported defaults or an authored override, and any
count-compatibility issue without dumping vertex deltas.

## Native glTF Weight Animation Boundary

glTF animation channels whose target path is `weights` remain unsupported by
the current skeletal channel reader and continue to fail explicitly. This first
tranche proves morph assets and animation through Rekall AGE's generic clip,
mixer, cubic, and graph contracts. A follow-up may map native glTF weight
channels into the same `Rekall.MorphState`, but it must handle arbitrary target
counts and CUBICSPLINE output cardinality without weakening this runtime or
inventing a second renderer path.

## Error Handling

Malformed component data produces `runtime.animation.morph_weights_invalid`.
Malformed or excessive glTF target data makes model loading fail with a bounded
asset issue. A runtime/asset count mismatch produces
`REKALL_RENDER_MORPH_WEIGHT_COUNT_MISMATCH`; no partial override is applied.
All errors identify the entity/asset and expected versus actual bounded counts.

## Verification

Automated coverage must prove:

- POSITION-only and POSITION+NORMAL target import, names, node/mesh defaults,
  transforms, chunk alignment, and exact limits;
- rejection of bad counts, non-finite deltas, unsupported morph semantics,
  incompatible compound layouts, and excessive totals;
- component validation, stale-state removal, ordinary/cubic clip animation,
  mixer/state-graph reuse, and split-run determinism;
- bounded asset metadata and runtime/CLI inspection;
- exact CPU morph output, normal normalization, override/default selection,
  mismatch fail-closed behavior, and morph-before-skin ordering;
- compatibility of non-morph, procedural, and existing skeletal meshes; and
- installed binaries authoring a generic morph weight, inspecting its state,
  and producing two informative hardware Vulkan frames with distinct hashes and
  measured vertex movement.

The complete Debug suite and canonical locked two-pass Release/install gate
remain mandatory.

## Explicit Limitations

Version 1 does not implement native glTF `weights` animation channels, TANGENT
deltas, sparse/quantized morph accessors, GPU compute deformation, automatic
normal generation, mesh-specific weight sets inside one asset, Studio curve or
shape editing, facial rigs, lip sync, or automatic content authoring.
