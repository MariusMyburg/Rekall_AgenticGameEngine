# Authoring a game through the CLI/MCP surface — an evaluation

Date: 2026-08-28
Subject: `Examples/StellarDominion`, built end-to-end through the engine's own
command surface as an assessment of how well an LLM can author with it.

## What was built

A space scene: a textured, banded gas giant with a structured ring system and a
physically-based scattering atmosphere; a textured moon; an 8,000-star field; a
sun with bloom; three capital ships with procedurally generated hulls and
separately-modelled emissive drive blocks; twenty fighters flying inclined
patrol orbits around their leaders; and a camera lens-dirt effect.

Everything went through `rekall.*` commands — no scene JSON was hand-edited.

## Verdict

**The command surface is genuinely good for agent authoring.** Diagnostics are
the standout: errors name the exact property path, list allowed values, and
suggest the discovery command to run next. Several times an error message alone
was enough to fix the problem without reading engine source.

The blockers that cost the most time were not missing features — they were
places where the engine silently did something other than what was asked.

## What worked well

- **`rekall.scene.apply_blueprint`** is the right primitive. A whole scene in one
  call, idempotent, with `clearExisting`. Iterating a 34-entity scene was one
  command per revision.
- **Validation diagnostics.** `Component 'Rekall.Transform3D' has no property
  'rotationX'. Allowed properties: Pitch, Roll, ScaleX, ScaleY, ScaleZ, X, Y,
  Yaw, Z.` — precise, actionable, and it lists the alternatives.
- **Built-in space primitives** are far richer than expected: `PlanetRenderer`,
  `AtmosphereRenderer` with Rayleigh/Mie/ozone terms, `RingRenderer`,
  `StarfieldRenderer`, `CelestialRotation`. Most of the astronomy was solved.
- **`rekall.level.camera.aim_at`** removed all Euler guesswork for framing.
- **Module scaffolding** (`install_sdk` → `scaffold` → `build modules`) worked
  first time, and the build-receipt requirement caught a stale module correctly.
- **`rekall.runtime.inspect_scene`** gave exact per-entity transforms, which is
  what proved the fleet motion was correct — and caught a real bug in it.

## What cost time, in order

### 1. The CLI cannot carry a large blueprint — MCP must be used

`command execute <name> <json>` passes arguments on the command line. The scene
with procedural hulls is ~93 KB of JSON:

```
/usr/bin/bash: line 1: /c/Program Files/dotnet/dotnet: Argument list too long
```

There is no `--arguments-file` option. Any scene with authored geometry will hit
this. The MCP stdio server has no such ceiling and handled the same payload
immediately, so **MCP is the only viable path for real authoring** — the CLI's
`command execute` is effectively limited to small payloads.

*Suggested fix:* accept `@path/to/file.json` for the arguments parameter.

### 2. Silent fallbacks are worse than errors

`Environment3D.backgroundColor` accepts `#RRGGBB` only. Passing `#02030aff` — an
8-digit form the engine accepts elsewhere, e.g. `RingRenderer.color` and
`StarfieldRenderer.color` — is **silently discarded** and the background falls
back to a default. Rendered through AgX, that default became a light grey sky in
what was meant to be deep space, and it read as a scattering bug rather than a
rejected colour. This cost several iterations.

Two separate colour parsers disagree:
`RekallAgeEnvironmentBackgroundResolver.Parse` accepts length 7 only;
`RekallAgeVulkanSceneBatchBuilder.ParseColor` accepts 7 **or** 9.

*Suggested fix:* one shared colour parser, and a validation error rather than a
silent fallback when a colour fails to parse.

### 3. Two renderers, very different output

`render viewport capture` defaults to a **software** rasterizer with flat
shading, no atmosphere, no bloom, and no tonemapping. The first captures looked
broken until the `vulkan` backend argument was passed. The default is a
reasonable choice for headless CI, but an agent evaluating "does my scene look
right" will draw the wrong conclusion from it.

*Suggested fix:* note the backend in the capture summary, or warn when a scene
declares post-processing the software path cannot execute.

### 4. Undocumented physical conventions

Each of these needed a source read to resolve:

- Directional light direction comes from the light's **Euler rotation**, not its
  position — the position is ignored for direction.
- Directional light intensity is **clamped to 4.0**; higher values are silently
  capped.
- `spaceAmbientFloor()` returns **0** for any body with atmosphere data, so an
  unlit face is a true black silhouette and `ambientEnergy` has no effect on it.
  This is physically defensible but invisible from the authoring surface, and it
  makes a backlit planet unreadable unless you know to change the phase angle.
- A `CloudLayerRenderer` with no texture renders as an opaque white shell that
  hides the planet completely.

*Suggested fix:* surface these in the component schema descriptions, which
agents already read via `rekall.module.search_component_schemas`.

## Engine defects found and fixed

**Ringed planets crashed the Vulkan capture path.** An entity carrying both
`PlanetRenderer` and `RingRenderer` projects *two* meshes, and the cloud and
atmosphere shells derive their renderable ids from the entity id alone, so both
projections emitted `<entity>:clouds` and `<entity>:atmosphere`:

```
An item with the same key has already been added. Key: ent_...:clouds
```

A ringed gas giant is an entirely ordinary thing to author, and it took down the
high-fidelity renderer. Fixed by attaching those shells only to the planet's own
surface projection, with a regression test
(`RingedPlanetProjectionTests`) that fails without the fix.

## Engine capability added: lens dirt

There was no lens-dirt pass. Added as a `lensDirt` post-process pass rather than
an authored overlay:

- Dirt **modulates the bloom term** in the tone-map shader instead of being
  composited over the finished image, because real lens grime only scatters light
  that is already bright. A clean lens (strength 0) leaves output unchanged.
- Two terms: a local one for grime sitting on a highlight, and a **veiling glare**
  term that samples the bloom pyramid over a wide radius, so a bright source
  hazes the whole element the way a dirty lens actually behaves. The first
  implementation only had the local term and was invisible against a mostly-dark
  frame.
- The mask is procedural (rotated-octave fbm, aspect-corrected, edge-weighted),
  so it needs no new descriptor binding or authored texture and is resolution
  independent. The first version used single-octave value noise and produced
  obvious square blocking.

Authored as:

```json
{"name": "dirt", "type": "lensDirt", "intensity": 0.55, "scale": 1.0}
```

**Known gap:** this is implemented in the Vulkan capture path's tone-map shader
only. The interactive Windows player has a separate post-process implementation
and does not yet honour `lensDirt`.

## A bug in my own authored module, caught by the engine

The first `FleetSystem` integrated capital drift as `speed * ElapsedTime` while
adding to the ship's current position each step — compounding quadratically.
`inspect_scene` made it obvious: ships at speed 0.9 had moved 61 units in 1.5
seconds instead of 1.35. Fixed to use `DeltaTime`. Verified after:

| entity | moved over 1.5 s | expected |
|---|---|---|
| Ardent Dominion (speed 0.9) | 1.33 | 1.35 |
| Vigil of Kell (speed 1.4) | 2.08 | 2.10 |
| Ardent Dominion Drive | 1.33 | tracks hull |
| Fighter 1 (r=17, 42°/s) | 18.86 | ~18.7 arc |
| Meridian (static) | 0.00 | 0 |

This is the engine's inspection surface doing exactly its job.

## Honest assessment of the result

The scene reads well, but it is not photoreal. What is missing, and why:

- **Ship hulls have no surface detail.** The procedural loft emits no UVs, so a
  hull texture cannot be applied. They read as smooth plastic. Adding UV
  generation to the loft is the single biggest remaining visual win.
- **Lens dirt reads slightly blue and speckled** rather than as subtle grime,
  because the only bright sources are the blue drive glows.
- **No ring shadow on the planet.** The rings receive the planet's shadow but do
  not appear to cast one back onto it.
- **Metallic hulls need an environment probe.** With `metallicFactor` high they
  went black, since there is no IBL; the value had to be dropped to 0.25 to let
  the directional key shape them at all.

## Reproducing

```bash
python Examples/StellarDominion/Tools/textures.py          # procedural textures
dotnet run --project src/Rekall.Age.Cli -c Release -- asset import ...
python Examples/StellarDominion/Tools/build_scene.py > scene.json
python Examples/StellarDominion/Tools/mcp_client.py rekall.scene.apply_blueprint scene.json
dotnet run --project src/Rekall.Age.Cli -c Release -- build modules Examples/StellarDominion
dotnet run --project src/Rekall.Age.Cli -c Release -- \
  render viewport capture Examples/StellarDominion Main 45 out 1920 1080 vulkan
```

Note the `vulkan` argument — without it you get the software rasterizer.
