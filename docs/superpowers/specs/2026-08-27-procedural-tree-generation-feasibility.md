# Procedural Tree Generation: Feasibility Research

**Status:** Research complete. Not implemented. No engine code changed by this
document.

**Question asked:** Can AGE support trees "grown" from authored parameters,
spanning a quality range from low-poly stylized to highly detailed/realistic,
built on the modeling capability already in the engine rather than a bespoke
one-off system?

**Verdict: yes, and the geometric core already works today with zero engine
changes.** The one real gap found is small and specific (see "The one actual
gap" below) - it is not required for a first working version, only for
matching the tapering behavior described here to organic bark shapes more
precisely later.

## What this is based on

Direct reading of the actual modeling source (not assumption): `RekallAgeMeshPrimitiveFactory.cs`,
`RekallAgeModelingProductionGeometry.cs`'s `CreateProfileSweep`, `RekallAgeCurveContracts.cs`,
`RekallAgeModelingNodeCatalog.cs`, `RekallAgeModelingGraphEvaluator.cs`. The
modeling system is a node-graph evaluator (`RekallAgeModelingGraphEvaluator`) -
architecturally the same shape as Blender's Geometry Nodes: a graph of typed
nodes with typed ports, evaluated deterministically into mesh assets.

## The core mechanic already exists: tapered profile sweep

A tree branch is fundamentally a tube that tapers from a wide base to a
narrow tip, following a (possibly curved) centerline. AGE already has exactly
this as `rekall.modeling.curve.profile_sweep`, and - this is the key finding -
**it already supports per-point tapering, not just a flat constant radius.**

Reading `CreateProfileSweep`'s actual sweep math:

```csharp
var value = point + tiltedNormal * sample.X * (float)pathPoint.Radius
                   + tiltedBinormal * sample.Y * (float)pathPoint.Radius;
```

`pathPoint.Radius` comes from `RekallAgeEvaluatedCurvePoint.Radius`, which in
turn traces back to a per-control-point `Radius` field authored directly on
the curve asset (`RekallAgeCurveContracts.cs`, default `1`). So: **author a
curve whose control points have decreasing Radius from base to tip, sweep it,
and the resulting mesh already tapers organically along its length.** This
was not assumed - it was confirmed by reading the exact multiplication in the
sweep's vertex-generation loop. No engine change is needed for basic
trunk/branch tapering.

Parallel-transport frames (`previousNormal`/`tiltedNormal`/`tiltedBinormal` in
the same method) mean the tube's cross-section stays consistently oriented
along a curving path without twisting artifacts - exactly what a naturally
bending branch needs, already handled.

## The rest of the toolkit a tree generator would compose

All of the following already exist as modeling-graph nodes (verified against
`RekallAgeModelingNodeCatalog.cs`'s actual registered node list, not
inferred):

- `rekall.modeling.curve.line` / `.circle` / `.fillet` / `.join` / `.resample`
  / `.trim` / `.reverse` - build and manipulate the branch skeleton as curves.
- `rekall.modeling.curve.profile_sweep` - turn each branch curve into a real
  tapered tube mesh (see above).
- `rekall.modeling.deform.bend` / `.taper` / `.noise` - additional organic
  variation (a dedicated taper deform exists independently of profile_sweep's
  own per-point radius, plus bend/noise for natural irregularity rather than
  perfectly straight geometric branches).
- `rekall.modeling.join` - combine the trunk sweep, every branch sweep, and
  leaf geometry into one final mesh.
- `rekall.modeling.merge_by_distance` - weld branch-junction seams where
  sweeps meet, using deterministic spatial hashing (already built for exactly
  this kind of seam-welding problem).
- `rekall.modeling.array` / `rekall.modeling.scatter.area` - place repeated
  leaf/foliage geometry (billboards or small leaf clusters) at branch tips or
  along branch length. `scatter.area` is currently described as "a bounded
  horizontal area" for environmental dressing; using it (or a variant of it)
  for per-branch leaf placement instead of a flat area is the one piece that
  would need checking/extending when this is actually built, not a blocker to
  feasibility.
- `rekall.modeling.boolean` - available if smoothed/blended trunk-to-branch
  junctions are wanted instead of a simple weld.
- `Rekall.Material` / texture system - bark and leaf materials are ordinary
  materials; nothing tree-specific needed there at all.
- `Rekall.LodGroup` (already used elsewhere for distance-based mesh swapping)
  - the natural home for the "low-poly vs. highly detailed" quality range:
    swap between tree meshes generated with shallower recursion depth and
    lower `profileSegments` (a parameter `profile_sweep` already exposes) at
    greater distance, and deeper/higher-segment versions up close. This is a
    generation-time parameter choice, not an engine gap.

## What is genuinely new work (not an engine gap - an algorithm to write)

The one thing that does not exist anywhere in the engine today is the actual
**branching-skeleton generator**: the recursive/L-system logic that decides,
from authored parameters (recursion depth, branch angle range, length
falloff per generation, radius falloff per generation, random seed for
organic variation), where each branch curve's control points and per-point
radii should be. This is pure algorithm work, not a modeling-primitive gap -
it is the part that actually "grows" the tree, and it would be written once,
as project/plugin code, using entirely the existing primitives listed above.

Recommended shape for this work when it is picked up:

1. A recursive branching algorithm (simpler and more controllable than a full
   L-system for a first version; a real L-system or space-colonization
   algorithm is a legitimate later upgrade, not a blocker to a first working
   version) that produces a tree of branch curves with decreasing length/
   radius per generation and randomized angle/twist within authored bounds.
2. For each branch curve: build a `RekallAgeCurveAsset` with per-control-point
   `Radius`, then a `profile_sweep` node to realize it as a mesh.
3. Join all branch meshes plus procedurally placed leaf geometry into one
   final mesh via `rekall.modeling.join` + `rekall.modeling.merge_by_distance`.
4. Expose recursion depth and `profileSegments` as the "low-poly vs. highly
   detailed" quality dial.

This should be built the same way the destruction/fracture system's
extensibility was: as a project-registered plugin following the
`IRekallAgeMeshOperationPlugin`/`IRekallAgeFractureAlgorithmPlugin` precedent
(see the "Mesh operation and fracture plugin system" checkpoint), proven out
in a real example project first, before any consideration of promoting parts
of it into a built-in engine feature.

## The one actual gap found

None that blocks a first version. The one thing worth flagging for a later,
more polished pass: `rekall.modeling.scatter.area`'s current shape is
described for "a bounded horizontal area," which fits ground-cover dressing
better than per-branch leaf placement; whoever implements this should check
whether it already generalizes to scattering along/around a branch's own
sweep (likely a straightforward parameter addition, not a redesign) or
whether leaf placement is simpler done directly via `rekall.modeling.array`
per branch tip instead. This is a real open question for the implementation
phase, not a blocker to feasibility.

## Conclusion

Procedural tree generation is not just feasible but unusually well-supported
by AGE's existing modeling primitives - specifically because the tapered
profile-sweep-along-a-curve mechanic (the actual hard geometric problem)
already exists and already works, verified by reading its exact math rather
than assumed. The remaining work is a real, scoped algorithm (the branching
generator itself), not new engine capability, and it fits cleanly into the
already-established project-plugin extensibility pattern.
