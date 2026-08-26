# Procedural Destruction Design

**Status:** Approved for implementation (user pre-approved; async execution).

## Context and goal

The engine has no destruction capability at all (confirmed: zero hits for
"fracture"/"destructible"/"voronoi" anywhere in `src`). The user's explicit
framing: build procedural destruction as a real generic engine capability,
then prove it with a visually impressive original game (grenades spawn
periodically, explode, break geometry into pieces, and crater the terrain).
**The demo is the test, not the deliverable** — if it doesn't work or doesn't
look good, that means the destruction system needs fixing or improving, not
that the demo gets special-cased to hide the gap. The demo must be authored
through the CLI/MCP authoring surface as an external client would use it (the
same discipline Aetherfall was built under), not by hand-editing engine
internals to fake the result.

## Architecture

Three generic engine capabilities, each independently useful outside any
demo, plus one game built on top of all three:

**1. Mesh fracture** (`Rekall.Age.Modeling`). Reuses the existing real CSG
kernel (`CSG.Sharp`, already wired into `RekallAgeModelingBoolean.cs` for the
`rekall.modeling.boolean` node) rather than building a new geometry kernel.
A Voronoi-style fracture picks *N* random seed points inside the source
mesh's bounds; each seed's chunk is the source mesh intersected, in sequence,
against one oriented half-space "slab" per other seed (the perpendicular
bisector plane between the two seeds, represented as a very large thin box
mesh so `Csg.CSG.Intersect` — the exact same operation the boolean node
already calls — does all the real clipping work). No new geometry kernel, no
new polytope math: fracture is "generate slab meshes, call the existing
intersect operation N-1 times per chunk."

The low-level CSG⇄`RekallAgeMeshAsset` conversion currently lives as private
methods on `RekallAgeModelingGraphEvaluator` (`ToCsg`/`FromCsg`, tightly
coupled to the boolean node's two-named-operand attribute-blend plan). Task 1
extracts the shared, operand-attribute-plan-free parts (`ToCsg`,
`InterpolateCorner`, `Barycentric`, the vector helpers) into a small internal
reusable class so both the existing boolean node and the new fracture
operation call the same tested conversion code — improving an existing
primitive rather than duplicating it, and proven behavior-preserving because
every existing boolean-node test must keep passing unmodified.

Exposed as both a direct `Rekall.Age.Modeling` API (`RekallAgeMeshFracture`)
and a new modeling graph node (`rekall.modeling.fracture`), matching how
every other geometry operation in this engine is reachable both
programmatically and through the graph.

**2. Terrain deformation (crater stamp).** Reuses the existing mesh-deform
primitive family (`RekallAgeMeshDeformOperations`, which already implements
taper/bend/noise as per-point displacement functions) rather than adding a
new heightfield subsystem. A new `crater_stamp` deform displaces points
within an authored radius of a center downward along a smooth falloff curve
(steep-walled near center, blending to zero at the radius edge) — applicable
to *any* mesh, including a terrain mesh, exactly like the existing deforms.
No new terrain/heightfield contract; a "terrain" is just a mesh, as it
already is everywhere else in this engine.

**3. `Rekall.Destructible` runtime component + system** (`Rekall.Age.Runtime`,
following the exact pattern of every other built-in runtime component/system
pair). An entity carrying `Rekall.Destructible` (referencing a
pre-authored fracture-chunk model set, an explosion impulse magnitude, and
optional terrain-crater parameters) responds to a semantic `destroy` event
(or a health-reaches-zero condition, matching how Aetherfall's own combat
already emits semantic events) by: deactivating itself, spawning its chunk
entities as dynamic rigid bodies (BEPU already supports dynamic convex-mesh
shapes) with an outward impulse from the impact point, and — if configured
with a terrain entity reference — applying the crater stamp to that terrain
mesh at the impact point. This is generic destruction plumbing; it knows
nothing about grenades.

**4. The demo game** (`Examples/<name>`, a new original project — grenade
timer, spawn cadence, and the "throw/arm/explode" gameplay loop are ordinary
agent-authored game module code, exactly like Aetherfall's own combat/
encounter modules). Authored through the CLI (`rekall-age` commands) or MCP
tools as an external client would, not through direct engine-source edits —
this is explicitly the point: it is the acceptance test for capabilities 1–3
working together through the real authoring surface. Any authoring
friction found while building it is itself a generic-engine deficiency to
fix, following this repo's own reproduce → failing-test → repair protocol
(never special-cased for "the destruction demo").

## Testing

- Fracture: focused tests on the extracted CSG-kernel class prove
  boolean-node behavior is unchanged (regression). Fracture-specific tests
  prove N chunks are produced, each is a valid closed manifold mesh (reusing
  `RekallAgeMeshValidator`), chunks are pairwise non-overlapping to within
  tolerance, and the sum of chunk volumes approximates the source volume.
- Crater stamp: a focused deform test proves a flat plane mesh's points
  within the stamp radius drop by the authored depth (smoothly falling off
  at the edge) and points outside the radius are untouched, mirroring the
  existing bend/taper deform test style exactly.
- `Rekall.Destructible`: a runtime acceptance test (in the style of the
  existing built-in-component tests) proves a destroy event spawns the
  expected number of dynamic chunk entities with nonzero outward velocity,
  and — when configured with a terrain reference — the terrain mesh's
  points near the impact point measurably drop.
- The demo game gets Aetherfall-style deterministic gameplay-checkpoint
  proof (`runtime inspect` with exact input/assertion payloads) plus native
  Vulkan capture reviewed for visual quality, exactly like every other
  example game in this repo — because the visual/gameplay result *is* the
  acceptance criterion for capabilities 1–3, per the user's explicit framing.

## Non-goals for the first slice

- Physically exact fracture patterns (real-world fracture mechanics,
  material-dependent break patterns) — Voronoi cells are a good-enough
  visual approximation, matching how most real-time games do this.
  Sub-shattering (chunks that themselves re-fracture on a second impact) is
  a tracked follow-up, not attempted here.
- A dedicated heightfield/terrain contract — the crater stamp works on any
  mesh, which is sufficient for the demo's terrain.
