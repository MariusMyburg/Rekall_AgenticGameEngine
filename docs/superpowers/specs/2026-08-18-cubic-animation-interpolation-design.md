# Bounded Cubic Animation Interpolation Design

**Date:** 2026-08-18

**Status:** Approved by the user's standing authorization for autonomous engine
architecture decisions

## Purpose

Add one deterministic cubic interpolation contract shared by agent-authored
animation clips and imported glTF skeletal animation. The feature remains a
generic sampler primitive: Rekall AGE does not infer motion, generate curves,
or attach game-specific meaning to animated properties.

## Decision

Use cubic Hermite interpolation with explicit derivatives measured in value
units per second. An authored track selects `interpolation: "cubic"`; every key
contains `time`, `value`, `inTangent`, and `outTangent`. A segment consumes the
left key's outgoing tangent and the right key's incoming tangent and scales
both by the segment duration before evaluating the standard Hermite basis.

The same evaluator semantics apply to glTF `CUBICSPLINE` channels. The glTF
reader decodes each output accessor as the standard input-tangent/value/output-
tangent triplet per input time and exposes those three bounded arrays instead
of flattening or silently treating the channel as linear.

## Alternatives Rejected

1. Add more named easing functions. This is easy to author but cannot preserve
   imported DCC curves and does not let agents express arbitrary key slopes.
2. Support Hermite only in Rekall's JSON clip format. This leaves the imported
   skeletal path with inconsistent interpolation behavior.
3. Store Bezier control points. They are less direct for glTF compatibility and
   make time/value parameterization ambiguous without a more complex solver.

## Authored Clip Contract

Existing `step`, `linear`, `smooth`, and `smoothstep` tracks remain compatible.
The new exact shape is:

```json
{
  "component": "Rekall.Transform3D",
  "property": "X",
  "interpolation": "cubic",
  "keys": [
    { "time": 0, "value": 0, "inTangent": 0, "outTangent": 12 },
    { "time": 1, "value": 6, "inTangent": 0, "outTangent": 0 }
  ]
}
```

Supported cubic values are finite scalars, flat finite numeric arrays of 1 to
16 elements, and hexadecimal RGB/RGBA colors. Scalar tangents are finite
numbers. Array tangents must be flat finite numeric arrays of identical length.
Color tangents are flat finite numeric arrays of three channels for RGB or four
for RGBA, in channel units per second. Cubic strings, booleans, nested arrays,
objects, mismatched shapes, and non-finite values fail closed.

Keys on a cubic track must have finite, strictly increasing times after authored
order is considered. All four fields are required on every key so the persisted
contract is uniform and inspectable even where an endpoint tangent is unused.
The existing 4,096-key and 1,024-track limits remain unchanged. Sampling before
the first key and after the last key returns the exact endpoint value; sampling
at a key time returns that exact authored value.

Unknown interpolation modes must no longer fall through to linear behavior.
They produce a bounded `runtime.animation.interpolation_invalid` observation
and do not mutate the target property. Invalid cubic data produces
`runtime.animation.cubic_key_invalid`, names the component/property target, and
does not partially apply the track.

## Determinism and Numeric Policy

For normalized segment time `t`, segment duration `d`, values `p0`/`p1`, and
derivatives `m0`/`m1`, evaluate:

```text
(2t^3 - 3t^2 + 1)p0
+ (t^3 - 2t^2 + t)(d m0)
+ (-2t^3 + 3t^2)p1
+ (t^3 - t^2)(d m1)
```

Normalized time retains the sampler's existing five-decimal midpoint rounding.
Every produced numeric component must remain finite; otherwise the track fails
closed. Colors round channel values away from zero and clamp them to 0..255.
Weighted mixer behavior remains unchanged because it blends the already
sampled property values.

## glTF Skeletal Contract

`RekallAgeGlbNodeAnimationChannel` gains nullable, count-matched
`InTangents` and `OutTangents` collections. `LINEAR` and `STEP` channels keep
one value per time and null tangent collections. `CUBICSPLINE` requires exactly
three output vectors per time, finite times, finite values/tangents, and strictly
increasing times. Unknown sampler interpolation is rejected during import.

The skeletal runtime uses the same duration-scaled Hermite equation for
translation and scale. Cubic rotation evaluates four quaternion components and
normalizes the result; a non-finite or near-zero quaternion fails the asset
sample instead of inventing an orientation. Existing linear quaternion slerp
and step behavior remain unchanged.

The initial scope does not add morph-weight channels. Morph targets require a
separate asset/runtime contract and must not be smuggled into this change.

## Agent Discoverability and Inspection

The built-in `Rekall.AnimationClip` track description documents the cubic key
shape, tangent units, supported value shapes, and exact limits. No new command
is required: clips continue to be authored through generic component and asset
primitives, and existing runtime animation inspection reports the resulting
player/layer state.

## Verification

Automated tests must prove:

- scalar and vector Hermite results that differ from linear interpolation;
- duration scaling, exact endpoints, and split-run determinism;
- color interpolation and clamping;
- rejection of missing, non-finite, nested, mismatched, duplicate-time, and
  unsupported-value tangent data without target mutation;
- unknown interpolation rejection while all existing modes remain compatible;
- mixer and state-graph clips consume cubic tracks without special cases;
- glTF cubic triplet decoding and malformed accessor-count rejection;
- skeletal translation/scale Hermite sampling and normalized cubic rotation;
- schema discoverability; and
- an installed-distribution fixture with an exact nonlinear runtime position
  plus a nonblank proof frame.

The complete Debug suite and canonical locked two-pass Release/install gate
remain the acceptance threshold.

## Explicit Limitations

This tranche does not add curve editors, automatic tangents, Bezier controls,
per-segment interpolation modes, extrapolation, quaternion squad, morph-target
weights, graph blend curves, or transition interruption. Those are separate
generic capabilities and must earn their own contracts and evidence.
