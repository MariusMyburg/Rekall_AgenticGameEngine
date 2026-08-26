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
- The shared hollow-sentinel graph now uses the same production-oriented
  structure: 38 reachable nodes, higher-resolution torso/head/pauldrons/collar,
  capsule arms, mirrored capsule legs, boots, and horns, with smooth anatomy
  bypassing the three-segment hard-surface bevel. It evaluates in 0.47 seconds
  and publishes 16,392 vertices and 7,356 triangles. Because this model drives
  the opening sentinel, dormant court enemies, guardian effigy, and guardian,
  the improvement exercises reusable generic modeling rather than a one-off
  scene prop.
- The weathered-ruin investigation found a concrete remaining AGE modeling
  limitation: boolean difference evaluated successfully, but both curved and
  rectangular cutters could produce n-gons that the explicit triangulate node
  or model compiler rejected as zero-area/non-triangulable. After three bounded
  attempts, the scene kit switched to constructive modular modeling instead of
  concealing that interoperability defect. Four weathered piers plus a separate
  header now form real open bays, preserving the existing buttresses, relief
  ribs, string courses, caps, crown damage, two material surfaces, bevel,
  weathering, weighted normals, and box UVs. The stable 43-node graph evaluates
  in 0.53 seconds and publishes 10,752 vertices and 4,368 triangles. The real
  RTX 5090 High diagnostic frame is
  `Proof/Captures/ConstructiveRuinPass/vulkan-scene-1280x720-20260826015916177.png`:
  11,370 distinct colors, 14.5% dominant-color share, mean luminance 0.104,
  213 draws, four dispatches, and zero observations or asset fallbacks.

The frame is diagnostic progress, not final visual acceptance: several ruin
silhouettes remain too black, the Warden/sentinel/rubble/ruin geometry remains
visibly coarse, and the composition is not yet at the requested Diablo/Alan
Wake quality bar. True authored sky/cubemap sampling is also still outstanding;
the new background is an explicit fallback, not a claim that sky assets render.

## Runtime hierarchy and Warden articulation checkpoint

- AGE now composes ordinary `parentId` 3D transforms for cameras, meshes,
  lights, fog, and particles. Resolution is cached per frame and invalid
  missing-parent or cyclic hierarchies fall back to local transforms with
  bounded structured observations rather than corrupting the frame.
- Aetherfall exercises that generic contract with two ordinary model-backed
  child entities parented to `AetherWarden`: a 25-node beveled runeblade graph
  and a 24-node articulated pauldron/arm graph. Both use existing animation
  clips and players; no weapon or character behavior was added to engine core.
- The real RTX 5090 High combat capture is
  `Proof/Captures/WardenArticulation/vulkan-scene-1280x720-20260826023235054.png`.
  At frame 10 it records the authored combat state (`AETHER 92`, one shard,
  score 25), 12,532 distinct colors, 18.1% dominant-color share, mean
  luminance 0.109, 227 render-work draws, four dispatches, and zero runtime
  observations, missing assets, unsupported assets, or fallbacks.
- The complete Aetherfall high-fidelity acceptance class passes 16/16. After
  the final scene mutation, movement, combat, progression, and reset still pass
  all 2/4/4/5 strict assertions; Warden X movement remains `0.506840`.
  Project and scene validation report zero issues. The High `desktop60` budget
  passes at 70 scene draw calls, 193,068 triangles, 748,408 vertices, and seven
  textures.
- This checkpoint proves visible rigid articulation and root-follow behavior.
  It does not claim Blender-class deformable procedural skin authoring or final
  Diablo/Alan Wake fidelity; those remain active high-priority modeling and
  content goals.

## Native weighted procedural mantle checkpoint

- AGE procedural meshes now carry canonical point-domain `joint-indices-0`
  (`Int4`) and `joint-weights-0` (`Float4`) attributes through validation,
  semantic mesh editing, compilation, Model Asset projection, and the existing
  morph-before-skin Vulkan deformation path. Bindings are pair-validated,
  finite, nonnegative, normalized, and expanded to emitted corner vertices.
- `Rekall.SkeletonPose` is now a discoverable built-in component rather than a
  runtime-only reserved name that ordinary agents and Studio cannot author.
  The game remains responsible for pose behavior: `AetherfallRulesSystem`
  emits two generic joint matrices from elapsed time without adding cape or
  character logic to engine core.
- Aetherfall consumes the path with `Warden Deformable Mantle`, a 40-point,
  28-quad weighted mesh authored and reshaped through AGE's public mesh/model
  commands. Its tapered sides, longitudinal folds, and pointed hem remain
  editable source geometry, and the runtime test proves a weighted vertex
  changes position between frames 1 and 30.
- The real RTX 5090 High Vulkan proof is
  `Proof/Captures/ProceduralSkinningShaped/vulkan-scene-1280x720-20260826030318049.png`.
  At frame 30 it records 11,471 distinct colors, 14.5% dominant-color share,
  mean luminance 0.104, 225 render-work draws, four dispatches, and zero
  observations, missing assets, unsupported assets, or fallbacks.
- The complete Aetherfall high-fidelity acceptance class passes 17/17. After
  the final source-mesh and model rebuild, movement, combat, progression, and
  reset still pass all 2/4/4/5 strict assertions; Warden X movement remains
  `0.506840`. Project and scene validation report zero issues. High
  `desktop60` at 1280x720 passes at 71 scene draws, 193,124 triangles, 748,520
  vertices, seven textures, and a measured 4.665 ms GPU frame.
- This is the first native weighted procedural consumer, not final character
  production. Native armature assets, bind-pose/hierarchy editing, automatic
  and painted weight tools, constraints, Studio rig visualization, and a much
  larger character/environment fidelity pass remain outstanding.

## Native Vulkan SSAO checkpoint

- The High/Epic Vulkan graph now contains a truthful `ssao-resolve` graphics
  pass after `opaque-hdr` and before volumetric fog. The old placeholder
  `ssao-occlusion` resource and its false producer/consumer edges are gone.
- The native pass samples the actual scene depth, rejects background pixels,
  reconstructs view-space depth, and applies a deterministic rotated disk of
  eight taps on High or twelve on Epic. It multiplicatively darkens loaded HDR
  with a conservative `0.55` floor, then restores depth for later consumers.
- The real RTX 5090 High Vulkan proof is
  `Proof/Captures/NativeSsao/vulkan-scene-1280x720-20260826032647150.png`.
  At frame 30 it records 11,551 distinct colors, 17.0% dominant-color share,
  mean luminance 0.100, 226 render-work draws, four dispatches, and zero
  observations, missing assets, unsupported assets, or fallbacks.
- Visual inspection confirms a stable, clean result with restrained contact
  darkening around the Warden, rubble, and architecture, without the earlier
  black-dot noise, halos, banding, or newly crushed silhouettes.
- The consolidated render-graph, planner, shader, native-executor, and
  Aetherfall selection passes 72/72. Movement, combat, progression, and reset
  still pass all 2/4/4/5 strict assertions with Warden X movement at
  `0.506840`. Project and scene validation report zero issues.
- High `desktop60` at 1280x720 passes at 71 scene draws, 193,124 triangles,
  748,520 vertices, and a measured 4.810080 ms GPU frame. Native SSAO accounts
  for one render-work draw and 0.011712 ms; the complete workload is 226 draws
  and four dispatches.
- This is a functional, bounded depth-only SSAO milestone. It is not yet the
  deferred normal-aware, half-resolution, bilateral/temporal-denoised solution,
  and it does not by itself close the remaining character, prop, environment,
  material, composition, and animation fidelity gap.

## Split-normal authoring checkpoint

- AGE now exposes generic face smoothing, explicit sharp-edge marking, angle-
  based auto smoothing, and split weighted corner-normal generation through
  semantic mesh edits, procedural graphs, and modifier stacks. The source
  policy remains inspectable as face `normal.smooth` and edge `normal.sharp`
  attributes; the baked semantic corner output is `normal.authored`.
- Aetherfall's Warden graph uses a 55-degree policy and its weathered ruin uses
  35 degrees. Both were patched, baked, and rebuilt through ordinary revision-
  checked AGE commands rather than by editing cooked mesh bytes.
- The strict consumer test first failed 0/2 because both auto-smooth nodes were
  absent, then passed 2/2 after authoring. The consolidated normal, graph,
  modifier, compiler, command, and high-fidelity selection passes 80/80.
- The retained RTX 5090 High Vulkan frame is
  `Proof/Captures/SplitNormals/vulkan-scene-1280x720-20260826035849526.png`.
  Frame 30 is informative with 11,509 distinct colors, 17.0% dominant-color
  share, mean luminance 0.100, 64 renderables, 226 render-work draws, four
  dispatches, and zero observations, missing assets, unsupported assets, or
  fallbacks. Visual inspection shows stable curved armor/stone shading and
  preserved hard architectural boundaries without black-dot noise.
- Movement, combat, progression, and reset retain all 2/4/4/5 strict passes;
  project and scene validation report zero issues. High `desktop60` remains
  within every configured budget at 71 scene draws, 193,124 triangles, 748,520
  vertices, seven textures, and 5.307360 ms measured GPU time; SSAO accounts
  for 0.012000 ms and the complete workload remains 226 draws/four dispatches.
- This closes the first split-normal policy slice, not Aetherfall's visual-
  fidelity target. Custom-normal editing/transfer, richer high-resolution
  geometry, production materials, denser world dressing, animation, and final
  composition remain substantial visible work.

## Typed curve-revolve authoring checkpoint

- AGE now publishes `rekall.modeling.curve.revolve@1` as a typed Curve-to-
  Geometry authoring node. It supports X/Y/Z axes, arbitrary origins, partial
  and full angles, bounded segment counts, axis-pole welding, material slots,
  seam-correct corner `uv.generated`, point `curve.source.span` and
  `revolve.angle` provenance, and face `normal.smooth`. Deterministic tests also
  compile the result through auto-smooth and weighted split normals and verify
  finite unit normal/tangent frames.
- Aetherfall consumes the primitive through ordinary graph patch, evaluate,
  bake, and Model Asset rebuild commands. The Warden has a ten-control-point,
  40-segment layered cuirass; the weathered ruin has paired closed 14-control-
  point, 32-segment crown capitals. Both remain editable source graphs and
  retain UV, provenance, material, and authored-normal evidence in their baked
  meshes.
- The first ruin composition placed the capital before the existing whole-mesh
  bevel. Because the model is instanced 30 times, that expanded the scene to
  2,799,400 vertices and correctly failed `desktop60`. Joining the already-
  smooth capital after the legacy bevel reduced the evaluated ruin from 12,612
  to 3,012 points while preserving the visible profile; the final budget is
  91 scene draws, 224,564 triangles, 1,158,440 vertices, seven textures, and
  6.374784 ms measured GPU time with no blockers or warnings.
- The retained RTX 5090 High Vulkan frame is
  `Proof/Captures/CurveRevolve/vulkan-scene-1280x720-20260826043723828.png`.
  Frame 30 is informative with 11,725 distinct colors, 17.0% dominant-color
  share, mean luminance 0.100, 64 renderables, 282 render-work draws, four
  dispatches, and zero observations, missing assets, unsupported assets, or
  fallbacks. Visual comparison with the split-normal frame shows the Warden's
  block torso replaced by a rounded layered armor volume while the dark,
  restrained composition and clean SSAO remain stable.
- The final consolidated curve/modeling/compiler/command/Aetherfall selection
  passes 85/85. Movement, combat, progression, and reset pass all 2/4/4/5
  strict executable assertions; the progression route respects the authored
  0.1-second delta clamp and proves two shards plus conduit activation. Project
  and scene validators report zero issues.
- This is an honest generic authoring milestone, not final Diablo/Alan Wake
  fidelity. Screw/helix pitch, explicit cap policies, fields, multi-spline
  output, mesh-selection Spin, a modifier form, Studio profile/axis editing,
  higher-resolution production assets, richer materials, animation, world
  dressing, and composition remain substantial work.

## Curve screw/helix authoring checkpoint

- AGE extends `rekall.modeling.curve.revolve@1` with bounded signed
  `pitchPerTurn` and angles up to 36,000 degrees. A pitched revolution remains
  open across whole turns, advances along the selected X/Y/Z axis, and emits
  signed point-domain `revolve.axial_offset` alongside the existing source-span,
  angle, UV, material, and smoothing data. Zero-pitch revolutions above one turn
  are rejected because they would only generate overlapping geometry.
- Aetherfall's conduit graph now consumes the generic primitive twice: two
  1,080-degree, 96-segment counter-wound coils surround a beveled obsidian body,
  and a transformed spherical aether core rises above it. The result was
  evaluated, baked, published, rebuilt at model revision 3, and assigned to both
  live conduit entities through ordinary AGE commands; the old ruin proxies are
  gone.
- The retained RTX 5090 High Vulkan frame is
  `Proof/Captures/CurveScrew/vulkan-scene-1280x720-20260826051445564.png`.
  Frame 30 is informative with 11,735 distinct colors, 16.9% dominant-color
  share, mean luminance 0.101, 65 renderables, 297 render-work draws, four
  dispatches, and zero observations, missing assets, unsupported assets, or
  fallbacks. Visual inspection confirms that the left conduit reads as a tall
  authored device with an elevated core and helical cage.
- All 22 Aetherfall high-fidelity acceptance tests pass. Movement, combat,
  progression, and reset retain all 2/4/4/5 strict executable assertions, and
  project plus scene validation report zero issues. High `desktop60` passes at
  96 scene draw calls, 230,332 triangles, 1,214,504 vertices, nine textures,
  and 6.656480 ms measured GPU time.
- The source-detail increase is intentionally retained, but the scene is now
  near its 1,250,000-vertex desktop60 limit. Automatic generated LOD variants
  and distance selection are therefore the next scalability requirement.
  Character anatomy, clothing, animation, architectural density, materials,
  and composition also remain visibly below the requested final fidelity.

## Selection- and weight-aware bevel checkpoint

- `bevel_edges` no longer requires the complete mesh edge set. It reconstructs
  selected two-face manifold edges with bounded profile rings, transition faces
  along affected neighboring edges, and vertex caps; zero-area transition
  wedges are removed before strict mesh validation rather than weakening the
  validator. Typed domain attributes and stable source provenance survive.
- Optional edge-domain Float weights scale selected widths and filter zero
  weights; `materialIndex` assigns generated bevel faces without flattening
  source materials; and `hardenNormals` authors bevel faces as smooth while
  retaining hard source planes. Operations, graph nodes, and modifiers publish
  the same parameters through their canonical descriptors.
- Generic `select_edges_by_angle` and
  `rekall.modeling.selection.edge_angle` author a reusable named edge selection
  from adjacent-face angle. Aetherfall's Warden runeblade now uses this node to
  select `runeblade-hard-edges` before its three-segment bevel. The ordinary
  graph bake produces 2,048 points, 4,446 edges, 2,422 faces, and 8,892 corners;
  the live-linked Model Asset compiles to 8,892 vertices and 4,048 triangles in
  one runed-steel surface.
- The installed RTX 5090 High Vulkan frame is
  `Proof/Captures/SelectiveBevel/vulkan-scene-1280x720-20260826084700066.png`.
  It is informative with 11,681 distinct colors, 16.9% dominant-color share,
  mean luminance 0.100, 65 renderables, 301 render-work draws, four dispatches,
  and zero observations, missing assets, or fallbacks. Original-size review
  finds no cracks or exploded topology. The frame remains prototype-grade:
  selective hard-edge rounding improves authoring control, but the Warden still
  needs split/skinned anatomy, deformable clothing, richer materials, and a
  stronger silhouette before it approaches the requested reference quality.
- Review-driven topology regressions now prove representative selected box-edge
  subsets remain closed, manifold, and oppositely wound across every shared
  edge. Bevel cap cycles follow their actual topological boundary; smoothing
  defaults survive joins into weighted normals; and new smoothing/material
  attributes publish invalidation metadata. Incompatible same-name smoothing
  attributes fail closed, while indexed edge/cap lookups avoid quadratic scans
  on production meshes. Focused modeling passes 78/78,
  complete Aetherfall acceptance passes 42/42, and both project and scene
  validators are clean. High `desktop60` passes at 97 draws, 193,570 triangles,
  833,144 vertices, nine textures, and 5.548224 ms measured GPU time.

## Hierarchy-targeted animation checkpoint

- Generic `Rekall.AnimationClip` tracks may now omit a target to animate their
  owner, use `targetEntityId` for an exact arbitrary entity, or use
  `targetPath` for 1-32 slash-separated direct-child ID or unique-name segments
  relative to the owner. The path model is inspired by Godot's node-path
  animation concept while retaining AGE entity IDs and component/property
  contracts. Invalid, missing, ambiguous, and conflicting targets fail closed
  with structured runtime observations.
- Targeted tracks execute in simple clips, weighted animation mixers, and
  state-graph-produced mixers. Local animation resolves first and targeted
  mutations then apply in deterministic owner/path/property order; weighted
  layers blend on the resolved target rather than independently overwriting it.
- Aetherfall's Warden now uses one 1.4-second root timeline for root yaw,
  pauldron roll, runeblade roll, and two-axis mantle motion. The redundant
  pauldron and runeblade `AnimationPlayer`/`AnimationClip` child clocks were
  removed, and acceptance asserts that the authored target paths resolve and
  produce distinct runtime transforms on all three attachments.
- The fresh installed native Vulkan capture is
  `Proof/Captures/HierarchyAnimation/vulkan-scene-1280x720-20260826080803287.png`.
  At High it is informative with 11,686 distinct colors, 16.9% dominant-color
  share, mean luminance 0.100, 65 renderables, 301 render-work draws, four
  dispatches, and zero observations or fallbacks. The coordinated accessory
  motion works, but a still frame is necessarily subtle and the main body
  silhouette remains rigid because its large masses are fused into one baked
  model. Split/skinned body authoring is the next character-quality requirement.
- Runtime animation passes 28/28, state-graph runtime passes 10/10, component
  metadata passes 11/11, and the complete Aetherfall selection passes 42/42.
  The latter also proves the test
  isolation repair: both shared-project suites now occupy one non-parallel
  collection, preventing module build receipts from being deleted while another
  test is loading them. High `desktop60` passes at 97 scene draws, 193,762
  triangles, 833,624 vertices, nine textures, and 5.111776 ms measured GPU time.
  The existing deterministic gameplay proofs remain 2/4/4/5 and both project
  and scene validation remain clean after the scene mutation.

## Warden limb-detail and facial-focal checkpoint

- The ordinary AGE modeling graph now authors the Warden with 101 typed nodes:
  paired thigh guards, tapered bracers, anatomically separated gauntlet forms,
  and a
  separate face-slit surface join the existing curve-revolved cuirass,
  pauldrons, mantle, cloth, blade, bevel, weighted-normal, and UV chain. The
  graph remains valid with zero unreachable nodes and evaluates its `mesh`
  output to 10,978 points, 26,750 edges, 13,416 faces, and 48,532 corners.
- A restrained emissive material graph is assigned only to the facial slit.
  The rebuilt live-linked Model Asset contains 48,532 compiled vertices and
  21,700 triangles across a stable steel, cloth, then aether surface order.
  The stronger acceptance contract requires all new authored nodes and all
  three ordered surfaces; it caught and drove repair of the initial variadic
  join-link ordering instead of accepting an unstable material-slot change.
- The fresh native High Vulkan frame is
  `Proof/Captures/WardenDetailPass/vulkan-scene-1280x720-20260826074140056.png`.
  It is informative with 11,828 distinct colors, 16.9% dominant-color share,
  mean luminance 0.101, 65 renderables, 301 render-work draws, four dispatches,
  and zero observations or asset fallbacks. Original-image review confirms the
  new limb armor and facial focal surface resolve in the gameplay camera.
- This is a verified intermediate character-authoring improvement, not final
  visual acceptance. The rigid stance, distant composition, coarse environment
  props, and limited material breakup still read as prototype-grade and remain
  the highest-priority visible gaps. High `desktop60` passes at 97 scene draws,
  193,762 triangles, 833,624 vertices, nine textures, and 4.767552 ms measured
  GPU time. Gameplay remains 2/4/4/5 and both validators report zero issues.

## Topology-safe virtual geometry checkpoint

- AGE's CPU virtual-geometry path no longer retains every Nth triangle. It now
  compacts material surfaces to referenced vertices, recognizes geometric
  connectivity across duplicated normal/UV seam vertices, preserves coincident
  disconnected open and closed components, clusters only connected source
  edges, removes collapsed and duplicate faces, and accepts a candidate only
  when boundary and maximum edge-use metrics do not worsen. Cluster size and
  pixel error affect actual output, including maximum-distance LOD. Skinned and
  morph-target meshes remain at source resolution until their vertex-indexed
  deformation payloads can be remapped safely.
- `MaxSelectedTriangles` now applies to the complete renderable as documented.
  Multi-material model budgets are apportioned deterministically across their
  surfaces instead of being independently granted in full to every surface;
  diagnostics explicitly report an unsatisfied cap, including when reduction is
  disabled or deformation payloads prevent safe remapping. Stable source
  geometry is cached before material color materialization and reused by Web,
  OpenXR, repeated asset instances, and the Windows static-scene cache.
  Seventeen static ruin consumers use the generic `Rekall.VirtualGeometry`
  component; animated Warden, enemy, weapon, and pauldron meshes are excluded.
- Fresh frame-30 inspection at 1280x720 selects 62,202 of 100,368 source
  triangles, reducing 38,166. Every consumer selects 3,416–3,904 triangles
  against its authored 5,228 cap and reports `BudgetSatisfied=True`. The native
  RTX 5090 High Vulkan frame captured after the final reducer mutation is
  `Proof/Captures/VirtualGeometryFinal/vulkan-scene-1280x720-20260826072813552.png`.
  It is informative with 11,670 distinct colors, 16.9% dominant-color share,
  mean luminance 0.101, 65 renderables, 297 render-work draws, four dispatches,
  and zero observations or asset fallbacks. Original-image review finds no new
  holes, seam tearing, silhouette loss, or reduction-attributable black-dot
  noise; it is pixel-identical to the immediately preceding 62,202-selection
  capture despite the later topology/cache hardening.
- High `desktop60` passes at 96 scene draws, 192,166 triangles, 778,804
  vertices, nine textures, and 5.009344 ms measured GPU frame time. Relative to
  the pre-LOD curve-screw checkpoint, this removes 38,166 submitted triangles
  and 435,700 vertices while retaining the visible composition.
- The focused virtual-geometry/static-architecture selection passes 30/30, a
  renderer/Web/OpenXR/cache integration selection passes 110/110, and the
  combined Aetherfall acceptance selection passes 42/42 after rebuilding the
  canonical gameplay-module receipt. After the final scene mutation, the public
  deterministic movement/combat/progression/reset proofs pass all 2/4/4/5
  strict assertions, including Warden X movement of `0.506840`; project and
  scene validation report zero issues.
- This closes topology-safe static generated LOD selection and multi-surface
  budget semantics, not GPU meshlet streaming or final art fidelity. Disk-page
  streaming, hierarchical occlusion, skinned/morph LOD remapping, authored LOD
  variants, and visibly richer characters, clothing, ruins, props, materials,
  animation, and composition remain in progress.

## Procedural skin-weight and taper-deform checkpoint

- AGE now authors complete two-joint point skin bindings through
  `assign_linear_skin_weights`, `rekall.modeling.skin.linear_weights`, and
  `rekall.modifier.skin.linear_weights`. The contract accepts X/Y/Z geometry
  ranges, emits normalized canonical `joint-indices-0`/`joint-weights-0`
  attributes, preserves complete existing bindings, and rejects partial,
  duplicate, incompatible, or canonical-name-conflicting data.
- Generic `taper_points`, `rekall.modeling.deform.taper`, and
  `rekall.modifier.deform.taper` scale planes perpendicular to an authored axis
  between explicit endpoint scales around an authored center. This is reusable
  for cloth, foliage, props, characters, and architecture; no mantle behavior
  was added to engine core.
- `aetherfall.warden-mantle.graph` is a stored nine-node production consumer:
  16x24 grid, hanging transform, tapered silhouette, deterministic cloth folds,
  solidified thickness/rim, linear weights, box UVs, and weighted normals. Its
  revision-checked bake replaces the former hand-shaped 40-point source with
  850 editable points and 848 faces. The rebuilt Model Asset contains 3,392
  skinned vertices and 1,696 triangles, all with compiled joint bindings.
- Runtime acceptance proves the live weighted mantle changes vertex position
  between frames 1 and 30. Modeling passes 246/246 and the complete Aetherfall
  selection passes 42/42; project and scene validation report zero issues.
  High `desktop60` at 2560x1440 passes with 97 scene draws, 195,210 triangles,
  836,424 vertices, and nine textures.
- The native RTX 5090 frame-30 diagnostic is
  `Proof/Captures/DeformableMantle/vulkan-scene-1280x720-20260826093900972.png`:
  11,773 distinct colors, 16.9% dominant-color share, mean luminance 0.100,
  301 render-work draws, four dispatches, and zero observations or fallbacks.
  It proves clean integration but not final art. The Warden body, garment
  construction, materials, and pose still read as blockout quality and remain
  the next visible character work.

## Native named-rig and grounded-character checkpoint

- `aetherfall.warden.rig` is a native AGE rig asset with stable `root` and
  `chest` joints. `Rekall.RigPose` drives the skinned Warden Model Asset through
  module-authored, delta-time-based named joint matrices; acceptance proves the
  runtime JSON pose and the resulting rendered vertices both change between
  representative frames.
- The mantle now uses the generic bend-deform node in addition to taper, folds,
  thickness, UVs, normals, and skin weights. Duplicate legacy cape/wing/crown/
  blade scene entities and the main graph's toy spike, duplicate blade, and
  duplicate cloak branches were removed.
- The grounded 0.58-scale Warden source remains a compact 73-node editable
  graph, but higher-quality bevel construction raises the rebuilt model to
  44,696 compiled vertices and preserves three deliberate material surfaces.
  The player fill is reduced to intensity 4.2 and warmed to `#806f5f`.
- `Proof/Captures/WardenDetailed/vulkan-scene-1280x720-20260826103940219.png`
  is the post-change native High Vulkan frame: RTX 5090 hardware path, 301
  render-work draws, four dispatches, zero observations, zero missing assets,
  and zero fallbacks. It is correctly grounded and more coherent, but remains
  an intermediate blockout rather than the requested final high-detail hero.
- Verification passes modeling 257/257, Aetherfall high-fidelity 24/24 (281
  combined), project
  validation, scene validation, module trust, and the 2560x1440 High
  `desktop60` budget at 6.496608 ms measured GPU time.

## Rounded articulated-character checkpoint

- The external Warden pauldron no longer duplicates an entire upper arm. Its
  editable 19-node graph now builds a compact layered shell from smooth sphere
  forms, a rolled torus rim, a front boss, and an authored rivet array. The
  graph explicitly excludes the obsolete upper-arm, elbow, vambrace, and spike
  branches that caused the doubled toy silhouette.
- The main Warden graph replaces rectangular coat panels and box boots with
  mirrored smooth capsule construction, and replaces the flat inset box visor
  with a rounded capsule faceplate. Smooth cloth/armor branches bypass the
  hard-surface segmented-bevel path, retaining useful density without the
  earlier topology explosion.
- `Proof/Captures/RoundedVisor/vulkan-scene-1280x720-20260826110408142.png`
  is the native RTX 5090 High Vulkan proof: 301 render-work draws, four
  dispatches, 9,780 distinct colors, zero observations, zero missing assets,
  and zero fallbacks. The doubled arm is gone and the feet, coat tails, shoulder
  shell, and faceplate read as rounded armor forms. This remains an intermediate
  hero asset; closer composition, anatomy, surface wear, layered materials, and
  richer locomotion/combat animation are still required for final visual
  acceptance.
- The full Aetherfall high-fidelity class passes 24/24. Project and scene
  validation report zero issues, both gameplay modules are ready under the
  `windows-appcontainer-restricted` posture, and the 2560x1440 High
  `desktop60` budget passes at 6.335008 ms measured GPU time, 97 scene draws,
  197,410 triangles, 835,052 vertices, and nine textures.
