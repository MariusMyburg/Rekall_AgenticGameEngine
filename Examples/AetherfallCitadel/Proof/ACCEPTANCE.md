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
- Aetherfall now uses the generic light `priority` contract (separate from
  shadow priority) to select the arrival hearth, Warden softbox, amber
  practical, and moon pool for its four-light budget. Their authored local
  ranges were widened to 14/14/14/22 world units. The aligned High frame rises
  from mean luminance 0.036 to 0.069 and from 16,029 to 24,001 distinct colors,
  with localized warm/cool pools and retained deep blacks. Architecture and
  character detail remain below final acceptance.
- The repeated weathered-ruin graph now authors layered string courses,
  pilaster caps, five facade ribs, uneven crown blocks, and a displaced fallen
  crown before its shared bevel/weathering/weighted-normal stages. The rebuilt
  live-linked model contains 7,392 compiled vertices and 3,024 triangles, up
  from the broad low-detail slab that dominated the earlier frame.
- The Warden graph now authors a recessed faceplate, helmet crest, mirrored
  pauldron spikes, layered tassets, mirrored greaves and boots, blade grip and
  pommel, and an inset belt buckle. Reducing its global bevel from three to two
  segments kept those meaningful silhouette additions while bringing the
  compiled artifact below AGE's 64 MiB asset limit. The published runtime mesh
  contains 64,284 vertices and 26,228 triangles.
- The aligned real RTX 5090 High capture after both live-linked rebuilds remains
  at 141 draws/4 dispatches with zero missing or fallback assets. It records
  24,500 distinct colors, 65.2% dominant-color share, and mean luminance 0.070.
  The player now reads as layered armor rather than a box figure, and repeated
  ruins catch light across authored profiles; material separation, large-scale
  composition, and broader architectural readability remain below final visual
  acceptance.
- Aetherfall exposed that compiled mesh surfaces preserved material asset IDs,
  but the standard Vulkan path never resolved those material graphs. AGE now
  resolves the portable static PBR closure for each compiled surface, loads
  graph-referenced textures, applies base-color/metallic/roughness/normal/
  emissive bindings, and retains entity material values as instance overrides.
- The Warden now publishes exactly two semantic surfaces from its ordinary
  modeling graph: aged steel for armor/weapon forms and charcoal cloth for the
  body, coat, hood, arms, and cloak. The mesh compiler coalesces alternating
  faces by material slot, reducing its cooked output from 21 surface runs to two
  draw surfaces without changing its 64,284 vertices or 26,228 triangles.
- The representative visual proof now uses frame 120 rather than the misleading
  first smoothing frame, allowing the authored follow camera to settle at
  `(0,13,-25.2)` and show the complete player silhouette. The real RTX 5090 High
  capture records 24,760 distinct colors, mean luminance 0.079, 142 render-work
  draws across all passes, and zero observations, missing assets, unsupported
  assets, or fallbacks. The desktop60 budget inspection reports 43 scene draw
  calls and 162,632 triangles, within every configured limit.
- The prior four `REKALL_UI_ELEMENT_NO_CANVAS` observations were traced to the
  real HUD canvas being authored invisible while its child labels remained
  visible. The canvas is now active, and the exact deterministic movement proof
  still changes Warden X by `0.506840` with both assertions passing.
- The shared weathered-ruin model now publishes two semantic graph-authored PBR
  surfaces: `aetherfall.ruin-mass.material` for the sooted structural masonry
  and `aetherfall.ruin-trim.material` for coping, string courses, pilaster caps,
  facade ribs, and crown blocks. Their graph color factors remain near-neutral
  so they preserve each placed instance's authored stone tint instead of
  multiplying the already-dark masonry toward black. The rebuilt artifact
  retains 7,392 vertices and 3,024 triangles while all 30 placed instances gain
  material-readable profiling. The settled real RTX 5090 High frame has 24,628
  distinct colors, mean luminance 0.078, 201 render-work draws, and zero
  observations or asset
  fallbacks. The matching `desktop60` budget passes with 63 scene draw calls,
  162,632 triangles, 594,744 vertices, and no blockers or warnings. The capture
  also shows that shadowed masonry is still crushed too close to black and the
  directly lit trim contrast is too hard; those remain active lighting/material-
  range work rather than accepted final quality.
- The agent-authored follow camera now uses a 40-degree field of view and
  settles at `(0,17.5,-30)` with a 42-degree downward pitch at the opening
  position. This reduces the oversized over-the-shoulder player presentation
  and exposes substantially more continuous ground, approach rubble, gate, and
  surrounding silhouette in a Diablo-like elevated three-quarter composition.
  The real High capture records 21,305 distinct colors, mean luminance 0.050,
  71.2% dominant-color share, 194 render-work draws, and zero observations or
  asset fallbacks. The stricter composition also makes the remaining deficiency
  explicit: too much of the surrounding authored terrain and architecture still
  collapses into near-black negative space, so density and indirect readability
  remain active work rather than being hidden by a close camera.
- After the camera scene/module mutation, the exact deterministic movement proof
  still passes both assertions and moves Warden X by `0.506840`; the focused
  presentation test proves the runtime follow height and 40-degree FOV. The
  matching High `desktop60` budget remains clean at 63 scene draw calls, 162,632
  triangles, and 594,744 vertices.
- The broken processional arch no longer renders its detailed curve work as one
  uniformly shaded surface or a single round ring. Its editable graph now uses
  96-sample inner and outer rectangular Bézier profile sweeps to form a layered
  masonry archivolt, then assigns the boolean-cut wall/buttresses to structural
  stone and the archivolts, keystone, coping, engaged columns, and finials to
  carved trim. The baked mesh retains 8,784 points and 12,590 faces; the cooked
  artifact contains exactly two semantic material surfaces, 42,680 vertices,
  and 17,500 triangles. The settled real High capture has 21,217 distinct
  colors, 198 render-work draws, and zero observations or asset fallbacks. The
  final High `desktop60` budget remains clean at 64 scene draw calls, 162,692
  triangles, and 637,600 vertices. This improves the gate's architectural
  hierarchy, but the surrounding negative space and broader prop density still
  fail final visual acceptance.
- The arrival near field now places four additional instances of the ordinary
  graph-authored `aetherfall-ruin-dressing-scatter-model` at asymmetric edge
  transforms, outside the central gameplay lane. Two low-key authored bounce
  lights reveal those shapes without raising global exposure. The accepted real
  RTX 5090 High frame is
  `Proof/Captures/NearFieldWideBounce/vulkan-scene-1280x720-20260826001720940.png`:
  18,118 distinct colors, 68.1% dominant-color share, mean luminance 0.044,
  213 render-work draws, and zero observations, missing assets, unsupported
  assets, or fallbacks. The matching High `desktop60` inspection passes with 68
  scene draw calls, 171,316 triangles, 656,080 vertices, and no warnings or
  blockers. This is improved grounded density, not final visual acceptance.
- AGE now resolves active point-light budgets by quality tier: Performance 2,
  Low 4, Medium 8, and High/Ultra/Epic 16. The real Vulkan uniform/shader path
  consumes all sixteen slots rather than retaining extra lights only in CPU
  metadata, and its fragment loop terminates at the selected count so lower
  tiers reduce actual light work. Stable selection remains `priority`,
  intensity, then entity ID.
  Capture and performance reports expose the resolved budget plus exact selected
  and dropped entity IDs. Aetherfall High selects all nine authored point lights
  with no drops; Performance selects the arrival practical and Warden fill while
  reporting the remaining seven IDs as dropped.
- The accepted High proof is
  `Proof/Captures/HighActiveLightCount/vulkan-scene-1280x720-20260826003608775.png`.
  It records 22,592 distinct colors, 58.1% dominant-color share, mean luminance
  0.071, 213 render-work draws, and zero observations or asset fallbacks. This
  restores central warm-path readability while retaining the new cool/warm side
  pools. True screen-cluster assignment and per-cluster overflow remain; the
  current quality-scaled sixteen-light array is the first production Forward+
  bridge, not the final dense-world lighting architecture.
- AGE environment authoring now carries separate `ambientSkyColor` and
  `ambientGroundColor` values alongside ambient energy. Both the native Vulkan
  path and the standalone Windows player evaluate a normal-oriented
  hemispherical ambient term; equal white defaults preserve older scenes. This
  follows Godot's useful separation of environment ambient color and energy
  without copying its renderer implementation or turning the feature into an
  Aetherfall-specific light.
- Aetherfall authors a restrained cool sky (`#9fb3c2`) and warm stone bounce
  (`#795743`) at ambient energy `2.0`. More importantly, the resulting visual
  inspection exposed that the six principal three-dimensional modeling graphs
  still used one-axis planar UV projection. AGE's existing generic face-aware
  box projection was applied through revision-checked graph patch, bake, and
  live-linked model rebuild commands to the Warden, hollow sentinel, weathered
  ruin, broken arch, rubble boulder, and ruin dressing. The near ruins now wrap
  the masonry texture around their depth instead of collapsing side faces into
  uniform dark texture strips.
- The aligned real RTX 5090 High proof is
  `Proof/Captures/HemisphereBoxFinal/vulkan-scene-1280x720-20260826010143841.png`:
  23,611 distinct colors, 55.8% dominant-color share, mean luminance 0.075,
  213 render-work draws, and zero observations, missing assets, unsupported
  assets, or fallbacks. After the scene and six-model mutation, movement,
  combat, progression, and reset still pass all 2/4/4/5 strict assertions;
  Warden movement remains exactly `0.506840` on X.
- Visual inspection of that frame exposed another generic environment gap:
  `backgroundPolicy` and the camera clear color reached runtime data, but the
  native HDR target and standalone Windows player still cleared to unrelated
  hard-coded colors. `Rekall.Environment3D.backgroundColor` is now a shared,
  backward-compatible fallback for color backgrounds and for sky policies whose
  sky asset is absent or not yet rendered; `camera`/`clear` policies retain the
  camera's authored clear color. Native Vulkan and the Windows player use the
  same resolver rather than Aetherfall-specific behavior.
- Aetherfall now authors a blue-charcoal `#111a1d` background fallback and a
  denser, slower-falling global mist (`0.006` density, `0.035` height falloff).
  The real RTX 5090 High proof at
  `Proof/Captures/EnvironmentBackgroundFog/vulkan-scene-1280x720-20260826011700840.png`
  has 11,196 distinct colors, 14.5% dominant-color share, mean luminance 0.104,
  213 draws, four dispatches, and zero observations, missing assets, unsupported
  assets, or fallbacks. It makes the court read as continuous terrain rather
  than geometry floating in a black void. After this scene mutation the strict
  movement/combat/progression/reset proofs still pass 2/4/4/5 assertions, with
  Warden X movement unchanged at `0.506840`.
- The same checkpoint exposed schema/runtime drift in the generic authoring
  surface: project validation rejected 288 properties and reserved component
  uses that the high-fidelity runtime already consumed. AGE's built-in schema
  module and reserved-type catalog now describe `Rekall.Environment3D`,
  `Rekall.ShadowSettings`, `Rekall.FogVolume`, `Rekall.ParticleEmitter3D`, mesh
  shadow flags, and point-light range/priority/shadow intent. `validation scene`
  now reports status `ok` with zero issues, and agents/Studio can discover the
  same production properties through component schemas instead of authoring
  data that validation falsely calls invalid.
- The Warden modeling checkpoint replaced the visibly faceted arm frustums with
  24-sided, six-ring capsules; raised the silhouette resolution of its torso,
  hood, head, pauldrons, collar, greaves, weapon grip, crest, and spikes; added
  mirrored elbow and knee guards, a rear armor ridge, and a cloak clasp; and
  reduced whole-model displacement that was making clean armor edges noisy.
  The first revision exposed a real procedural-authoring performance trap:
  applying a segmented bevel after joining already-smooth spheres, capsules,
  and toruses caused evaluation to exceed 90 seconds. The graph now follows a
  Blender-style nondestructive structure in generic AGE primitives: separate
  hard-surface and smooth branches, bevel only the hard-surface branches, then
  recombine them before material assignment, weathering, weighted normals, and
  box UV projection. Evaluation dropped to 1.54 seconds while retaining 85
  reachable nodes, 9,766 editable points, 12,282 faces, two semantic material
  surfaces, 43,948 compiled vertices, and 19,384 triangles.
- That rendered checkpoint also exposed an authored coordinate-contract error:
  Aetherfall had rotated AGE's Y-axis frustums and capsules as if their axial
  direction were Z. Revision-checked graph patches restored upright torso,
  hood, arms, greaves, crest, and weapon grip transforms. The accepted real
  RTX 5090 High diagnostic frame is
  `Proof/Captures/HeroAxisCorrection/vulkan-scene-1280x720-20260826014700390.png`:
  11,345 distinct colors, 14.4% dominant-color share, mean luminance 0.104,
  213 render-work draws, four dispatches, and zero observations or asset
  fallbacks. The player now reads as an upright layered armored figure rather
  than sideways discs, although rigid posing, stylized proportions, nearby
  ruin coarseness, and broader realism remain active failures against the final
  Diablo/Alan Wake visual bar.

The frame is diagnostic progress, not final visual acceptance: several ruin
silhouettes remain too black, the Warden/sentinel/rubble/ruin geometry remains
visibly coarse, and the composition is not yet at the requested Diablo/Alan
Wake quality bar. True authored sky/cubemap sampling is also still outstanding;
the new background is an explicit fallback, not a claim that sky assets render.
