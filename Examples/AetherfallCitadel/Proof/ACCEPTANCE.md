# Aetherfall: Citadel of Echoes — Acceptance Evidence

Accepted on 2026-08-24 as a large agent-authored 3D game and AGE engine testbed.

## Authored game

- 79 initial runtime entities across the Arrival Terrace, Resonance Court, and Guardian Observatory.
- Semantic movement, aim, dash, pulse, interact, pause, and reset actions.
- Pickups, hazards, conduit/gate progression, three hostile archetypes, a staged guardian encounter, score/combo, victory/defeat, camera follow, and dynamic HUD.
- Two agent-owned C# modules and ten graph-authored, baked, published, and packaged 3D model assets.
- One active perspective camera and a Vulkan-rendered starfield/citadel composition.

## Deterministic gameplay proofs

All proofs execute `Main` after the final scene and module mutation and assert the attached agent-owned `Game.AetherfallWardenState` component.

| Proof | Frames | Strict result |
| --- | ---: | --- |
| `movement` | 4 | Warden X changes by `0.506840`; 2/2 assertions pass. |
| `combat` | 10 | Aether is consumed, one shard is collected, and score changes to 25; 4/4 assertions pass. |
| `progression` | 21 | Both conduits activate, the Resonance Court encounter becomes active, and integrity changes to 88; 4/4 assertions pass. |
| `reset` | 2 | Phase, position, and aether return to exact authored defaults; 5/5 assertions pass. |

The checked-in input and assertion payloads beside this file reproduce these proofs with `rekall.runtime.inspect_scene` through the CLI `runtime inspect` command.

## Rendering and performance

- Native Vulkan proof: NVIDIA GeForce RTX 5090, hardware accelerated, zero missing assets, zero unsupported assets, and zero runtime observations.
- Opening frame: 63 renderables and 2,404 distinct colors.
- Scripted combat frame: 62 renderables and 2,574 distinct colors.
- Scripted Resonance Court frame: 82 renderables and 2,899 distinct colors.
- Visibility inspection: 59/59 initially visible renderables visible to `CitadelCamera`; zero culled.
- Desktop 60 budget: 56 draw calls, 3,626 triangles, 4,257 vertices at 1280x720; within every configured budget.
- Runtime soak: 3,600/3,600 frames, 60 simulated seconds, about 1,110 FPS, zero entity growth, zero observations, and zero events at the final checkpoint.

## Closed-loop delivery

- The agent-authoring gauntlet preserves the existing authored scene and passes project, scene, module trust/build, playtest, package, audit, and proof-frame checks.
- The Windows package contains `Play.exe`, `Play.bat`, both game modules, `Main`, ten model documents, ten compiled model meshes, and all manifest-declared dependencies.
- The ZIP relocates to an independent directory with 351/351 files verified.
- The relocated package runs 5/5 frames with exit code 0.
- The package audit reports zero missing key artifacts, a valid packaged scene, successful run, non-blank informative capture, and intact layout.
- A scripted relocated-package combat capture succeeds at frame 10 with 62 draw commands.

## Engine improvements driven by the game

- Added canonical Vulkan topology for the generic torus and octahedron scene primitives, following the stable mesh-surface/topology boundaries used by Godot and Blender.
- Exposed deterministic runtime input sequences on the CLI native viewport-capture path.
- Made the generic agent-authoring gauntlet accept both 2D and 3D render-capable projects.
- Made the gauntlet preserve an existing authored scene instead of clearing it, removing a credible data-loss hazard.

Generated packages and screenshots live under the ignored `Builds/` directory; this file and its JSON payloads are the durable, reproducible acceptance record.

## Active high-fidelity expansion checkpoint — 2026-08-25

This newer modeling/rendering tranche is still in progress and does not replace
the accepted gameplay evidence above.

- A persisted cubic Bézier curve resource now drives the broken entrance arch;
  its graph and published mesh are checked for source/evaluated drift. The
  graph now uses the generic uniform arc-length resample node at 48 path points
  before its 16-sided profile sweep, producing an 8,752-point/12,532-face
  beveled and weathered architectural mesh instead of a visibly segmented arc.
- A real 1280x720 High-quality Vulkan capture on the NVIDIA GeForce RTX 5090
  exposed a generic renderer defect: native scene lighting discarded authored
  environment ambient values, while native post-processing ignored authored
  exposure/white point and divided HDR lighting by a fixed white point of 16.
- AGE now transports environment parameters through the native frame-uniform
  ABI, uses authored exposure and white point, and treats the conventional 11.2
  scene-white value as a neutral highlight reference rather than a blanket
  midtone divisor. The Windows player and native scene renderer share the same
  calibrated ambient coefficient.
- The aligned repaired frame is informative with 12,732 distinct colors,
  dominant-color share 30.9%, mean luminance 0.188, 141 draws, 4 dispatches,
  zero missing assets, and zero fallback assets. Aetherfall exposure is now
  -1.15 for dark but readable presentation.
- The capture also proved that unit-strength micro-normal maps were producing
  distracting high-frequency noise across the whole scene. Authored material
  instances now use a 0.35 normal scale. This materially reduces the visible
  noise while preserving stone and ground relief.
- The fractured-slate boulder is the first Aetherfall consumer of AGE's
  crease-aware subdivision tranche. Its ordinary procedural graph now adds a
  smooth-subdivision detail stage after authored deformation, increasing the
  reusable asset from 222 points/240 faces to 922 points/920 faces. The graph
  then authors area-weighted smooth corner normals before UV compilation. It
  was rebaked and its live-linked model rebuilt at logical revision 4 with a
  matching source revision and compiled hash; this is real player geometry,
  not an existence-only descriptor check.
- A subsequent real RTX 5090 High capture isolated another generic source of
  the visible black-dot noise: derivative-built normal-map tangent frames
  normalized zero/near-zero vectors when UV derivatives were degenerate. Both
  the native Vulkan shader and Windows player shader now reject degenerate UV
  determinants/tangents and fall back to the authored geometric normal. The
  aligned 1280x720 frame remains at 141 draws/4 dispatches with zero missing or
  fallback assets, while distinct colors fall from 12,738 to 12,129 and broad
  ground/ruin speckling is visibly reduced. Large unlit black faces remain a
  separate material/lighting/composition deficiency and are not accepted.
- Native Vulkan now consumes the same four deterministically selected
  practical point lights already available to the Windows player instead of
  silently discarding lights two through four. The frame ABI carries each
  light's color, position, range, and priority, and the shader applies bounded
  range-window plus inverse-square-style attenuation. This removed the old
  non-physical whole-court flood from the first point light and exposed the
  intended localized warm pools. The aligned frame is now too dark overall
  (mean luminance 0.036), so camera-relevant practical selection/authored cool
  fill and material readability remain required; global exposure is not used
  to conceal that deficiency.

The frame is diagnostic progress, not final visual acceptance: several ruin
silhouettes remain too black, prop geometry remains visibly coarse, and the
composition is not yet at the requested Diablo/Alan Wake quality bar.
