# Aetherfall: Citadel of Echoes — Ultimate 3D Testbed Design

**Status:** Accepted by the user's explicit pre-approval on 2026-08-24

## Purpose

Build a substantially larger, more demanding, interactive, and visually impressive original 3D game with Rekall AGE while acting as its LLM authoring backend. The game is both a player-facing deliverable and a closed-loop acceptance test: when ordinary authoring exposes a generic AGE deficiency, improve the engine contract first, prove the repair, and then continue authoring the game.

The finished project must run from Studio and as a relocatable Windows package. It must be understandable to another agent from its scene, component, module, model-asset, diagnostic, and package contracts without relying on hidden state.

## Chosen Game

**Title:** Aetherfall: Citadel of Echoes

**Format:** Single-player, elevated-camera 3D action-adventure through a grounded ruined citadel landscape.

The player pilots an agile aether warden through three visually distinct connected combat spaces. They activate energy conduits, collect echo shards, evade moving hazards, fight several reusable enemy archetypes, and defeat a multi-stage Citadel Guardian. Movement, aiming, dash, pulse fire, interaction, pause, and reset use semantic input actions. The game is immediately readable from an elevated perspective and does not depend on genre-specific engine behavior.

## Authoritative Visual Direction

Aetherfall must look as realistic and dark as AGE can achieve. Its target is mature cinematic survival-horror naturalism with the environmental readability of an elevated action RPG—not a colorful abstract arena, children's game, or renderer test chamber.

The world is a coherent explorable location built on continuous terrain or ground, even where the playable surface is mostly flat. Paths, encounter clearings, terraces, cliffs, ruins, foundations, rubble, vegetation or restrained alien growth, and near/mid/far landmarks establish believable geography. Architecture must be embedded in the ground and shaped by the location. Terrain, solid world boundaries, foreground occluders, or fog hide the edges of the authored world; disconnected platforms may not float in an empty colored void.

Materials use desaturated wet stone, fractured slate, compacted earth, ash, rough weathered metal, aged timber, mud, and other physically credible surfaces. Lighting uses a cold overcast or moonlit key, sparse warm practical sources, volumetric mist and restrained shafts, readable silhouettes, and deep contact shadows. Emissive color is scarce and motivated. Effects are brief and physical—dust, sparks, debris, mist, impact flashes, and subtle activation energy—rather than persistent neon hoops or rails.

Giant glowing rings, candy magenta/cyan palettes, floating colorful spheres/cubes/cones, obvious raw primitives, geometric panel fields, and nonsensical decorative shapes are explicitly rejected. Actors and enemies must read as authored, material-bearing silhouettes from the gameplay camera. The HUD must support the world without dominating it.

The visual bar is judged from actual native 2560×1440 captures, not component counts or feature flags. A technically valid frame that still looks crude, toy-like, empty, or incoherent fails acceptance and must be revised.

### Approved visual-reference traits

The user's Diablo screenshot is a mood, composition, readability, and lighting reference only; no source asset or protected game content is copied into Aetherfall. The resulting scene should use a tighter elevated three-quarter gameplay camera; dense masonry, ground detail, stairs, debris, walls, and foreground occlusion that fill and frame the view; near-black surroundings with preserved cool gray/green midtones; readable player and enemy silhouettes grouped into an active encounter; restrained atmospheric falloff toward the edges; and sparse warm orange action or practical effects that become the focal point and visibly influence nearby surfaces. The reference rejects a distant barren overview just as strongly as it rejects colorful abstraction.

## Game-Driven Engine Development Rule

Aetherfall is a realistic acceptance driver for both rendering and gameplay. It must exercise responsive movement, aiming, attacks, dash, enemy behavior, collision and damage, health, pickups, progression, encounter state, action-synchronized effects, camera behavior, HUD feedback, pause/reset, difficulty, and readability—not merely prove that a scene can render.

When ordinary terrain, material, modeling, model rendering, animation, camera/composition, lighting, fog, effects, input, events, queries, mutation, physics, navigation, runtime inspection, packaging, or gameplay authoring is awkward, impossible, uninspectable, or visibly weak, improve the smallest generic AGE contract first and then make Aetherfall consume it. Do not hide common engine deficiencies behind one-off game hacks. Game-specific combat, AI, progression, and encounter decisions remain in Aetherfall's authored modules and assets; the engine must not gain genre-specific behavior.

AGE's built-in modeling system is itself under production acceptance. Final flagship geometry must prove that inspectable modeling graphs can create, revise, bake, publish, catalog, and render reusable terrain modules, broken walls and arches, cliffs and rock clusters, rubble and props, gates and foundations, and readable player/enemy characters. Scene primitives may block out composition but may not remain the visible substitute for required production assets. If the graph lacks necessary generic operators, topology and surface control, UV/material-slot authoring, composition/hierarchy, deformation or animation preparation, diagnostics, or dependable bake/publish behavior, extend those generic modeling contracts and prove them before completing the corresponding game asset.

## Approaches Considered

### 1. Large open-world exploration game

This would maximize spatial scale, but current AGE content density, navigation, streaming, and asset-authoring workflows would dominate the work before meaningful gameplay breadth was proven. It is deferred until smaller production scene services are accepted.

### 2. Scripted cinematic corridor

This would make visual framing easier, but would exercise too little dynamic world mutation, input, game state, replayability, and emergent interaction. It would be a weak engine test.

### 3. Connected arena-action adventure — selected

Three linked arenas provide a bounded production surface while stressing dynamic entities, state transitions, semantic controls, collision queries, camera presentation, HUD, model assets, reset, performance, and delivery. Each arena is a reusable authored configuration rather than a hard-coded engine mode.

## Player Experience

The opening vista shows the player on a rain-darkened ruined approach, with a grounded path through fractured terrain toward the central citadel, distant structures, sparse practical lights, mist, and the Guardian's sealed observatory. A concise HUD communicates integrity, aether, shards, current objective, score/combo, and boss state without competing with the world.

The first space teaches motion, dash, pulse fire, pickups, and conduit activation. The second combines moving sentries, orbiting hazards, cover pillars, and a timed energy lock. The third escalates into a guardian battle whose shield, vulnerable phase, radial attack, and defeat state are visible in both the world and HUD. Victory opens the observatory core and presents a replay prompt. Defeat and manual reset restore a deterministic clean run.

## Controls

The scene owns a `Rekall.InputActionMap` with these exact semantic actions:

- `move.horizontal`: A/D and Left/Right
- `move.vertical`: W/S and Up/Down
- `aim.horizontal`: mouse/controller projection when available; keyboard fallback J/L
- `aim.vertical`: mouse/controller projection when available; keyboard fallback I/K
- `ability.pulse`: primary mouse button and Space
- `ability.dash`: secondary mouse button and Left Shift
- `interact`: E
- `pause`: Escape
- `reset`: R

The rules module consumes semantic values only. Movement, cooldowns, effects, hazard motion, enemy behavior, and transitions use `context.DeltaTime`, clamped to a safe simulation maximum.

## Authored Architecture

### Project layout

The game lives under `Examples/AetherfallCitadel` and follows existing portable project conventions:

- `rekall.project.json` declares rendering3d, input, modules, physics, audio, animation, UI, and world capabilities.
- `Scenes/Main.age.scene.json` contains stable scene entities and configuration.
- `Modules/AetherfallRules` contains all game behavior and agent-owned components.
- `Modules/AetherfallPlayable` supplies player-facing launch text only; it does not substitute for world gameplay.
- `Modeling`, `Assets/Models`, and the asset catalog contain persistent source geometry, compiled model assets, and provenance.
- `Proof` contains reproducible inspection inputs and human-readable acceptance notes, never runtime secrets.

### Agent-owned components

Components are inspectable data contracts, registered by the rules module:

- `WardenState`: velocity, integrity, aether, score, combo, cooldowns, invulnerability, shard count, objective phase, and facing.
- `EnemyState`: archetype, health, speed, attack cadence, preferred range, spawn point, phase, and active flag.
- `ProjectileState`: owner faction, damage, velocity, remaining lifetime, radius, and visual role.
- `PickupState`: pickup kind, value, collected flag, and respawn policy.
- `ConduitState`: required shards, activation progress, active flag, and linked gate.
- `HazardState`: motion kind, origin, axis/radius, amplitude, speed, damage, and phase offset.
- `GuardianState`: health, shield, stage, attack clock, vulnerability, and defeated flag.
- `EncounterState`: active zone, wave, remaining enemies, gate state, elapsed time, and completion flag.
- `EffectState`: effect kind, age, lifetime, start/end scale, and color role.

Game behavior uses generic SDK queries and immutable mutations. Dynamically spawned projectiles and effects are ordinary runtime entities with stable deterministic IDs derived from the frame and an authored sequence. Missing configuration emits structured observations. State changes emit custom event facts for other authored modules or future tooling.

### Rules decomposition

The module is split into focused files rather than one monolith:

- module/component registration
- input and warden simulation
- encounter and objective progression
- enemy and guardian simulation
- projectile, pickup, hazard, and effect simulation
- collision/math helpers
- presentation synchronization for world markers, lights, camera, and HUD
- deterministic reset and entity factory helpers

Each system consumes and returns `RekallAgeRuntimeWorld`; no callback mutates an outer world. The game may implement authored kinematic collision where appropriate, but it must use generic engine physics/event contracts when they provide the necessary deterministic facts.

## World and Visual Direction

The citadel uses a strong silhouette and color script instead of relying on texture quantity:

- arrival terraces: deep indigo stone, cyan aether paths, warm gold guidance lights
- resonance court: magenta energy machinery, moving rings, high-contrast cover
- guardian observatory: dark radial architecture, red shield phase, white-gold vulnerable core

Model assets define the warden, three enemy silhouettes, guardian, conduit, gate, bridge segment, platform trim, shard, and citadel spires. Geometry remains editable and published through AGE's mesh/model asset contracts. Repeated environment pieces use model-asset references; runtime actors use visible render components and transforms on the same logical entity as their state.

Presentation includes a composed perspective camera, layered floor/platform geometry, vertical landmarks, animated emissive-like colors where supported, multiple lights, readable shadow-free contrast, billboarding/world labels only when valuable, transient hit/dash/pickup effects, and a polished HUD. The renderer must never silently frame an unintended default camera.

## Engine-Improvement Rule

When authoring or acceptance reveals a deficiency:

1. Reproduce it with the smallest realistic failing test or deterministic scene.
2. Decide whether an existing primitive is being used incorrectly or a generic contract is missing.
3. If generic, implement the smallest reusable engine improvement with tests and diagnostics.
4. Re-run the focused engine test and the game checkpoint that exposed it.
5. Record residual pathological risk as technical debt unless it threatens common workflows, security, or data.

No engine API may mention Aetherfall, warden, enemy, boss, arena, weapon, or another game-specific concept.

## Milestones and Playable Gates

### Gate 1 — executable movement slice

The project, input map, warden state, camera, floor, and rules module exist. `rekall.runtime.inspect_scene` supplies representative `move.horizontal` and `move.vertical` frames and proves the exact attached `Game.Modules.AetherfallRules.WardenState` plus a nonzero 3D transform delta. This gate runs immediately after the first successful rules build.

### Gate 2 — combat and interaction

Pulse fire spawns visible projectiles; dash changes warden state and movement; one enemy can take damage and deactivate; a shard can be collected; a conduit can activate. Focused deterministic inspections prove the latest mutations with strict component-property or transform assertions.

### Gate 3 — complete progression

All three zones, hazards, enemy archetypes, gate transitions, guardian stages, victory, defeat, pause, and reset work. Inspections prove representative combat, collection, objective completion, guardian transition, and reset.

### Gate 4 — visual and performance acceptance

Fresh native captures show the opening vista, mid-combat court, and guardian phase. Each is inspected visually, not just checked for file existence. Visibility and performance diagnostics show the intended camera and a bounded render workload. At least one independent review judges composition, readability, feedback, and obvious rendering defects.

### Gate 5 — delivery acceptance

Studio can open the project, build modules, run the game, package for Windows, and expose the output. The package includes `Play.exe` and `Play.bat`, launches after relocation to a clean path, captures a current playable frame, and passes package audit. Final evidence comes after the latest scene or module mutation.

## Quantitative Acceptance Floor

The final authored game must meet all of these minimums without padding invisible content:

- three connected, visually distinct spaces and one multi-stage final encounter
- one player actor, at least three enemy archetypes, and at least twelve concurrent hostile actors in the stress encounter
- movement, dash, pulse fire, interaction, pickup, hazard damage, score/combo, victory, defeat, pause, and reset
- at least four deterministic runtime inspections covering motion, combat/collection, progression, and reset after the corresponding latest mutations
- at least ten reusable published model assets or equivalently complex authored geometry assets
- at least sixty visible authored scene entities, with dynamic entities added during play
- objective, integrity, aether, shard, score/combo, and guardian HUD feedback
- three accepted native screenshots at distinct gameplay states
- a clean focused test suite, full relevant engine suite, package audit, and relocated Windows launch

## Non-Goals

- No open-world streaming, online multiplayer, procedural narrative generation, external account service, or photorealistic asset mandate.
- No game-specific engine helpers.
- No claim that a clean build, screenshot, package, or source inspection proves gameplay.
- No weakening of a failed assertion to manufacture a pass.
- No reliance on a headless diagnostic path as the normal player experience.

## Completion Definition

Aetherfall is complete only when a person can launch the relocated Windows package, understand the controls and objective, play through a multi-system run to victory or defeat, reset cleanly, and see a composed 3D presentation—and when fresh deterministic evidence proves that agent-authored gameplay is attached and effective after the final mutation. Any generic AGE fixes discovered during the work are tested, documented where appropriate, integrated into the same accepted commit history, reviewed, merged, and pushed.
