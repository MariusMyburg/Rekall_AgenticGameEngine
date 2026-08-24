# 3D Pong and 3D Galaga: Modeling-to-Game Acceptance Plan

**Goal:** Accept two substantial, visually strong, genuinely 3D games (Pong
and an original Galaga-like game) authored strictly through AGE's ordinary
CLI/MCP command surface, each proving STRATEGIC-PRIORITIES.md priority #3
(the Modeling-to-game common path: runtime rendering *and* physics resolving
published Model Assets, not just primitives) in addition to priority #5
(breadth via Pong/Galaga acceptance).

**Standing directive for this work (explicit user instruction, 2026-08-24):**
Author both games "as a client LLM" -- through `rekall.tools.search`-style
progressive discovery and the registered command surface, the same way an
external LLM client connected over MCP would have to. No hand-editing scene
JSON or module source with omniscient knowledge of internal schemas as a
shortcut; no recalling component/command names from memory instead of
discovering them. Where the command surface genuinely cannot do something an
author would reasonably need, record that as an engine finding -- do not
route around it with a manual edit and call it done. Both games must be 3D
(not 2D), visually strong (real lighting, materials, modeled geometry -- not
bare primitive-cube placeholders), and any real engine defect found along the
way gets fixed in a commit separate from game content.

## Why Model Assets, not primitives

`Examples/ProceduralModelingProbe` (procedural modeling graph: box + sphere,
boolean union, UV projection -> mesh output, via `rekall.modeling.*` node
types) and `Examples/GlbStationTest` (imported GLB Model Asset) already prove
AGE has a working, non-trivial modeling path. `docs/superpowers/plans/
2026-08-23-model-asset-foundation.md` already landed `rekall.asset.model.
publish/.rebuild/.inspect/.list` and `rekall.scene.instantiate_asset` as
canonical commands. What has *not* been directly evidenced anywhere in
`PROGRESS.md` is physics resolving a published Model Asset's geometry (e.g. a
`Rekall.MeshCollider` sized/shaped from the same compiled mesh a
`Rekall.ModelAssetReference` renders) -- STRATEGIC-PRIORITIES.md priority #3's
own wording flags exactly this as open. Both games below require at least one
entity whose visible mesh *and* collision shape both come from the same
authored, published Model Asset, so this session either closes that gap or
produces a concrete, reproducible finding about why it can't yet.

## Order and shared discipline

1. Pong first (simpler acceptance surface: 2 paddles, 1 ball, walls, score),
   then Galaga (more actors: player ship, multiple enemies, projectiles,
   formation/attack behavior).
2. For each game, follow the same acceptance bar already used for Clockwork
   Canopy (`docs/production/clockwork-canopy-web-acceptance.md` and Task 9 of
   `docs/superpowers/plans/2026-08-23-genuine-web-game-publishing.md`):
   source/schema inspection, clean build + targeted tests, strict
   deterministic runtime input/state assertions, real player launch and
   manual play, independent visual review of actual rendered frames (native
   Vulkan *and* software, cross-checked the way the WGSL lighting bug and the
   camera-convention question earlier this session were resolved -- by
   comparing real captures, not assuming), package relocation and audit,
   archived evidence.
3. Discovery step before any authoring call for a given subsystem: use
   `rekall.tools.search` (or the registry-search-equivalent CLI/MCP path) to
   find the relevant commands/components rather than assuming names. Record
   genuinely missing or confusing capability as a finding.
4. Engine fixes land in their own commits, separate from game content
   commits, each with the same evidence discipline as this session's earlier
   flakiness fixes (isolated repro, real fix, full-suite reverify).
5. `docs/production/PROGRESS.md` gets a durable entry per completed game
   (not per sub-step) with exact evidence, mirroring the existing entries'
   density. `STRATEGIC-PRIORITIES.md` item 3 gets marked closed (with
   evidence pointer) only once physics-resolving-a-Model-Asset is actually
   proven for real, not assumed.

## Game 1: Pong (3D)

- **Visual target:** a real 3D arena (side walls, floor/back plane with
  material, not a flat void), modeled paddles and ball (not
  `rekall.primitive.*` box/sphere placeholders) via the modeling graph
  system, real lighting (at least one directional/point light with a
  deliberate look, not the bare default), a scored HUD.
- **Model Asset requirement:** the ball (or a paddle) is a published Model
  Asset instantiated via `rekall.scene.instantiate_asset`, carrying a
  `Rekall.MeshCollider` (or the closest physics-resolvable collider the
  engine actually supports for a Model Asset) that the physics system
  resolves for real collision, not a hand-placed `Rekall.SphereCollider3D`
  sized independently of the model.
- **Gameplay contract:** two paddles (player + AI or two-player input, agent's
  choice, documented), ball with velocity/bounce physics, wall bounce,
  scoring on miss, reset serve, win condition at a score threshold,
  deterministic gameplay assertions (velocity reflects correctly off a
  paddle at a given contact point, wall bounce sign-flips the right axis,
  score increments exactly once per miss, reset restores serve state).
- **3D-ness check:** camera must not be a flat top-down 2D-equivalent view by
  accident -- verify via a real perspective or angled orthographic camera and
  an actual captured frame showing genuine depth (e.g. paddles/ball with
  visible shading from the 3D geometry, not silhouettes indistinguishable
  from a 2D sprite).

## Game 2: Galaga-like (3D)

- **Visual target:** modeled player ship and at least one modeled enemy type
  (not primitives), a starfield or environment backdrop with real depth,
  projectile visuals, formation movement, real lighting/material treatment
  distinct from Pong's (don't reuse the exact same look).
- **Model Asset requirement:** same bar as Pong -- at least the player ship or
  the enemy is a published Model Asset with both a resolved render mesh and a
  resolved physics/trigger collider (for projectile-hit and collision
  detection), instantiated through the canonical command, not hand-placed.
- **Gameplay contract:** player movement (strafe, bounded to the play field),
  player-fired projectiles, enemy formation entry and attack-dive behavior
  (or a documented simpler substitute if full formation AI is out of scope --
  record the substitution explicitly, don't silently ship less and call it
  Galaga-equivalent), enemy hit/destroy on projectile contact, player
  hit/death and lives, score, wave progression or a clear win/loss condition,
  reset/replay.
- **Modeling stretch, time permitting:** `Rekall.LodGroup` and
  `Rekall.ProceduralMaterial`/`Rekall.Material` on the ship/enemy models,
  since the advisor flagged these as natural extensions of the same
  Model-Asset path and the user asked for real visual quality.

## Explicit non-goals for this pass

- Web publishing for these two games is not required by this plan (Task 9's
  web-publishing plan is already closed for Clockwork Canopy); revisit only
  if time remains after both games are fully accepted on Windows.
- Full Blender-parity modeling features are out of scope; use whatever
  `rekall.modeling.*` node types and Model Asset commands already exist, and
  record gaps rather than building new modeling primitives as a prerequisite.

## Evidence and completion

Each game is "accepted" only when all of these hold, matching the plan's own
evidence hierarchy (`STRATEGIC-PRIORITIES.md`'s "Evidence hierarchy"
section):

1. Source/schema inspection notes (what commands/components were discovered
   and used, and any gaps found).
2. Clean Release build, targeted + full engine test suite green.
3. Deterministic runtime input/state assertions proven after the final edit.
4. Real player launch and manual play (Windows installed player).
5. Independent visual review of actual current frames (not stale evidence --
   this session already found and fixed one case of exactly that mistake).
6. Package, relocation, and audit.
7. `docs/production/PROGRESS.md` entry with the above, committed and pushed.
