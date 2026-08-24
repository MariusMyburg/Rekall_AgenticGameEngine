# Aetherfall: Citadel of Echoes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Author, prove, package, and visually accept a substantial original 3D action-adventure that stress-tests Rekall AGE and drives reusable engine repairs.

**Architecture:** `Examples/AetherfallCitadel` is a portable AGE project whose stable world/configuration lives in one inspectable scene and whose behavior lives in focused files in an agent-authored runtime-system module. Dynamic actors and effects are ordinary immutable runtime entities; deterministic CLI inspections are the executable gameplay specification. Engine changes are admitted only through a reproduce-test-repair-retest loop and cannot contain game vocabulary.

**Tech Stack:** .NET 10, C#, Rekall AGE runtime module SDK, AGE JSON scene/modeling/model-asset contracts, xUnit, native Vulkan player, Windows packaging workflow

**Spec:** `docs/superpowers/specs/2026-08-24-aetherfall-ultimate-3d-testbed-design.md`

## Global Constraints

- Core systems expose generic, inspectable, composable contracts; all Aetherfall behavior stays agent-authored.
- Every real-time calculation uses `context.DeltaTime` with a maximum simulation step of `0.1` seconds.
- All controls use exact semantic actions from the scene's `Rekall.InputActionMap`.
- Every immutable world mutation is assigned and no entity-update callback mutates an outer world.
- Every visible dynamic actor keeps transform, renderer/model reference, and agent-owned state on the same logical entity.
- The first gameplay checkpoint runs immediately after the first successful rules build and proves `Game.Modules.AetherfallRules.WardenState` plus a nonzero 3D transform delta.
- Failed gameplay assertions are repaired without weakening the intended assertion.
- A generic engine deficiency is fixed with a focused failing engine test before the game works around it.
- Final gameplay, capture, and package evidence must be regenerated after the last scene or module mutation.
- Windows delivery includes both `Play.exe` and `Play.bat` and passes relocated launch and audit.

## File Structure

### Game project

- `Examples/AetherfallCitadel/rekall.project.json`: portable project manifest and capabilities.
- `Examples/AetherfallCitadel/Scenes/Main.age.scene.json`: stable entities, arena configuration, HUD, camera, lights, action map, and initial agent state.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallRules.csproj`: portable SDK-backed runtime module.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallRulesModule.cs`: module registration and component schemas.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallConstants.cs`: exact action, component, tag, entity, and tuning constants.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallRulesSystem.cs`: ordered frame orchestration only.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/WardenSimulation.cs`: movement, aim, dash, pulse, integrity, and pause handling.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/EncounterSimulation.cs`: zone, wave, conduit, gate, victory, and defeat progression.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/HostileSimulation.cs`: enemy archetypes and guardian state machine.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/WorldInteractionSimulation.cs`: projectile, pickup, hazard, collision, and transient-effect updates.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/PresentationSimulation.cs`: camera, light, world marker, and HUD synchronization.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallEntityFactory.cs`: deterministic projectile/effect factories.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallMath.cs`: bounded 2D-on-XZ vector and overlap helpers.
- `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallReset.cs`: deterministic restoration of authored initial state.
- `Examples/AetherfallCitadel/Modules/AetherfallPlayable/AetherfallPlayable.csproj`: portable playable module.
- `Examples/AetherfallCitadel/Modules/AetherfallPlayable/AetherfallPlayableModule.cs`: concise launch/control text.
- `Examples/AetherfallCitadel/Modeling/**`, `Assets/Models/**`, `Assets/assets.age.catalog.json`: editable geometry, published model assets, compiled meshes, and catalog.
- `Examples/AetherfallCitadel/Proof/*.json`: exact deterministic input/assertion payloads.
- `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md`: commands, expected transitions, captures, and delivery evidence.

### Tests and engine repairs

- `tests/Rekall.Age.Tests/Examples/AetherfallCitadelAcceptanceTests.cs`: structural and command-level acceptance tests for the checked-in game.
- `tests/Rekall.Age.Tests/<subsystem>/<GenericContractTests>.cs`: created only when a reproduced generic engine deficiency identifies its owning subsystem.
- `src/Rekall.Age.<Subsystem>/**`: smallest generic implementation repair selected by that failing test.

---

### Task 1: Portable Project, Scene Skeleton, and Structural Contract

**Files:**
- Create: `Examples/AetherfallCitadel/rekall.project.json`
- Create: `Examples/AetherfallCitadel/Scenes/Main.age.scene.json`
- Create: `Examples/AetherfallCitadel/Proof/movement-inputs.json`
- Create: `Examples/AetherfallCitadel/Proof/movement-assertions.json`
- Create: `tests/Rekall.Age.Tests/Examples/AetherfallCitadelAcceptanceTests.cs`

**Interfaces:**
- Consumes: AGE schema version 1 and built-in component JSON conventions demonstrated by `Examples/Galaga3D`.
- Produces: scene `Main`; entity `AetherWarden`; tags `player`, `enemy`, `hazard`, `pickup`, `conduit`, `gate`, `effect`; exact semantic actions from the spec.

- [ ] **Step 1: Write the failing structural test**

```csharp
[Fact]
public void Aetherfall_scene_exposes_required_authoring_contracts()
{
    var root = TestPaths.RepositoryRoot;
    var scene = JsonNode.Parse(File.ReadAllText(Path.Combine(root,
        "Examples", "AetherfallCitadel", "Scenes", "Main.age.scene.json")))!.AsObject();
    var entities = scene["entities"]!.AsArray();

    Assert.True(entities.Count >= 60);
    Assert.Contains(entities, e => e!["name"]!.GetValue<string>() == "AetherWarden");
    Assert.Contains(entities.SelectMany(e => e!["components"]!.AsArray()),
        c => c!["type"]!.GetValue<string>() == "Rekall.InputActionMap");
    Assert.Contains(entities.SelectMany(e => e!["components"]!.AsArray()),
        c => c!["type"]!.GetValue<string>() == "Game.Modules.AetherfallRules.WardenState");
}
```

- [ ] **Step 2: Run the focused test and verify it fails because the project does not exist**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~AetherfallCitadelAcceptanceTests`

Expected: FAIL with a missing `Examples/AetherfallCitadel/Scenes/Main.age.scene.json` path.

- [ ] **Step 3: Create the manifest and stable scene skeleton**

Author schema version 1 JSON with capabilities `animation,audio,input,modules,physics,rendering3d,ui,world`. Create the warden, active perspective camera, two directional lights, UI canvas/labels, input map, three zone roots, floors, railings, bridges, gates, conduits, pickups, hazard markers, enemy spawn entities, guardian, and distant silhouette entities. Every stable entity has an explicit deterministic ID; every visible 3D entity has `Rekall.Transform3D` and a render component.

- [ ] **Step 4: Add exact movement proof payloads**

`movement-inputs.json` contains four frames, each holding both semantic action facts:

```json
[
  {"semanticActions":[{"name":"move.horizontal","value":1,"isDown":true},{"name":"move.vertical","value":0.5,"isDown":true}]},
  {"semanticActions":[{"name":"move.horizontal","value":1,"isDown":true},{"name":"move.vertical","value":0.5,"isDown":true}]},
  {"semanticActions":[{"name":"move.horizontal","value":1,"isDown":true},{"name":"move.vertical","value":0.5,"isDown":true}]},
  {"semanticActions":[{"name":"move.horizontal","value":1,"isDown":true},{"name":"move.vertical","value":0.5,"isDown":true}]}
]
```

`movement-assertions.json` contains:

```json
[
  {"entityName":"AetherWarden","subject":"component","operator":"exists","componentType":"Game.Modules.AetherfallRules.WardenState"},
  {"entityName":"AetherWarden","subject":"delta.position3d.x","operator":"greater-than","expected":0}
]
```

- [ ] **Step 5: Run the structural test and validation**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~AetherfallCitadelAcceptanceTests`

Run: `dotnet run --project src/Rekall.Age.Cli -- validation scene Examples/AetherfallCitadel Main`

Expected: PASS and no blocking scene diagnostics.

- [ ] **Step 6: Commit the inspectable project skeleton**

```powershell
git add Examples/AetherfallCitadel tests/Rekall.Age.Tests/Examples/AetherfallCitadelAcceptanceTests.cs
git commit -m "feat: add Aetherfall 3D testbed skeleton"
```

### Task 2: First Rules Slice and Mandatory Gameplay Checkpoint

**Files:**
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallRules.csproj`
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallRulesModule.cs`
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallConstants.cs`
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallRulesSystem.cs`
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/WardenSimulation.cs`
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallMath.cs`

**Interfaces:**
- Consumes: `AetherWarden`, `Rekall.InputActionMap`, `move.horizontal`, `move.vertical`, and `WardenState` scene properties.
- Produces: registered `WardenState`, `EnemyState`, `ProjectileState`, `PickupState`, `ConduitState`, `HazardState`, `GuardianState`, `EncounterState`, and `EffectState`; deterministic XZ movement through `WardenSimulation.Update`.

- [ ] **Step 1: Extend the acceptance test to require the module source topology**

```csharp
[Theory]
[InlineData("AetherfallRulesModule.cs")]
[InlineData("AetherfallRulesSystem.cs")]
[InlineData("WardenSimulation.cs")]
[InlineData("AetherfallMath.cs")]
public void Aetherfall_rules_keep_focused_source_files(string file)
{
    Assert.True(File.Exists(Path.Combine(TestPaths.RepositoryRoot, "Examples",
        "AetherfallCitadel", "Modules", "AetherfallRules", file)));
}
```

- [ ] **Step 2: Run the test and verify the topology cases fail**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~AetherfallCitadelAcceptanceTests`

Expected: FAIL for the missing rules files.

- [ ] **Step 3: Implement registration, schemas, constants, math, and movement**

Define the exact components in the spec with `[RekallAgeComponent]` and `[RekallAgeProperty]`, register all nine components, and register `AetherfallRulesSystem`. The initial orchestrator calls `WardenSimulation.Update(world, context)` only. Normalize the two scalar movement axes on XZ, multiply by speed and clamped delta time, clamp to the arrival zone bounds, update facing, transform, velocity state, aether recovery, and elapsed run time through SDK helpers.

- [ ] **Step 4: Build modules**

Run: `dotnet run --project src/Rekall.Age.Cli -- build modules Examples/AetherfallCitadel`

Expected: `AetherfallRules` builds and installs without SDK or mutation diagnostics.

- [ ] **Step 5: Run the mandatory first gameplay checkpoint immediately**

Run: `dotnet run --project src/Rekall.Age.Cli -- runtime inspect Examples/AetherfallCitadel Main 4 Examples/AetherfallCitadel/Proof/movement-inputs.json Examples/AetherfallCitadel/Proof/movement-assertions.json`

Expected: PASS; `AetherWarden` has the exact agent-owned state component and positive `delta3D.x`.

- [ ] **Step 6: If the checkpoint fails, repair the authored input map, component attachment, or movement rule and rerun the exact same assertions**

Do not change the subject or expected value. A failure in a generic engine contract starts the engine repair protocol in Task 6 before continuing.

- [ ] **Step 7: Commit the executable movement slice**

```powershell
git add Examples/AetherfallCitadel tests/Rekall.Age.Tests/Examples/AetherfallCitadelAcceptanceTests.cs
git commit -m "feat: make Aetherfall movement executable"
```

### Task 3: Combat, Dash, Pickups, Hazards, and Effects

**Files:**
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/WorldInteractionSimulation.cs`
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallEntityFactory.cs`
- Modify: `Examples/AetherfallCitadel/Modules/AetherfallRules/WardenSimulation.cs`
- Modify: `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallRulesSystem.cs`
- Create: `Examples/AetherfallCitadel/Proof/combat-inputs.json`
- Create: `Examples/AetherfallCitadel/Proof/combat-assertions.json`
- Modify: `tests/Rekall.Age.Tests/Examples/AetherfallCitadelAcceptanceTests.cs`

**Interfaces:**
- Consumes: `ability.pulse`, `ability.dash`, `interact`, warden facing, tagged pickups/hazards, and enemy state.
- Produces: visible deterministic projectile/effect entities; damage, collection, dash, cooldown, combo, and integrity transitions; custom `combat.hit`, `pickup.collected`, and `warden.dashed` events.

- [ ] **Step 1: Add failing structural assertions for combat prerequisites**

Assert the scene contains at least three `EnemyState`, six `PickupState`, three `HazardState`, and bindings for `ability.pulse`, `ability.dash`, and `interact`.

- [ ] **Step 2: Run the focused test and observe the missing or under-counted prerequisites**

Run the Aetherfall acceptance filter and retain the exact failure output in the work log.

- [ ] **Step 3: Implement dash and pulse fire**

Dash consumes aether, applies a bounded displacement along current movement/facing, sets cooldown and invulnerability, and spawns a short-lived visible ring/afterimage effect. Pulse fire consumes aether, updates cooldown, and creates a visible projectile with deterministic ID, faction, damage, velocity, radius, and lifetime. Empty/zero aim falls back to facing.

- [ ] **Step 4: Implement generic authored interaction simulation**

Advance projectiles by delta time; remove expired entities; test XZ radius overlaps; reduce hostile health; deactivate defeated enemies; collect shards and aether pickups; move configured hazards; apply invulnerability-aware hazard damage; and animate/remove effect entities. Use stable SDK query ordering and perform removals after update passes.

- [ ] **Step 5: Add exact combat proof**

Author deterministic frames that hold `ability.pulse` and movement long enough to hit the nearest configured training sentinel, then dash across a shard. Assertions require `EnemyState.health` to change and `WardenState.shardCount` to increase; include the exact WardenState existence assertion.

- [ ] **Step 6: Build and run the exact combat inspection**

Run module build, then `runtime inspect` using `combat-inputs.json` and `combat-assertions.json`. Expected: all strict assertions pass and at least one dynamic visible projectile/effect appears in runtime state.

- [ ] **Step 7: Commit combat and interaction**

```powershell
git add Examples/AetherfallCitadel tests/Rekall.Age.Tests/Examples/AetherfallCitadelAcceptanceTests.cs
git commit -m "feat: add Aetherfall combat and interactions"
```

### Task 4: Encounters, Enemy Archetypes, Guardian, and Complete Progression

**Files:**
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/EncounterSimulation.cs`
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/HostileSimulation.cs`
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallReset.cs`
- Modify: `Examples/AetherfallCitadel/Modules/AetherfallRules/AetherfallRulesSystem.cs`
- Modify: `Examples/AetherfallCitadel/Scenes/Main.age.scene.json`
- Create: `Examples/AetherfallCitadel/Proof/progression-inputs.json`
- Create: `Examples/AetherfallCitadel/Proof/progression-assertions.json`
- Create: `Examples/AetherfallCitadel/Proof/reset-inputs.json`
- Create: `Examples/AetherfallCitadel/Proof/reset-assertions.json`

**Interfaces:**
- Consumes: `EnemyState.archetype` values `sentinel`, `orbiter`, `lancer`; conduit/gate configuration; warden projectiles and interact action.
- Produces: zone/wave transitions, twelve-hostile stress encounter, guardian shield/vulnerable/defeated stages, victory/defeat/pause/reset, and deterministic restored state.

- [ ] **Step 1: Add failing progression acceptance assertions**

Assert three distinct zone tags, at least twelve active hostile configurations in the resonance-court wave, exactly one `GuardianState`, at least two `ConduitState`, and one `EncounterState`.

- [ ] **Step 2: Run the focused tests and capture the contract failures**

Run the Aetherfall acceptance filter; expected FAIL until the full scene configuration exists.

- [ ] **Step 3: Implement three authored hostile behaviors**

Sentinels maintain preferred range and fire; orbiters circle configured anchors and fire inward; lancers telegraph then dash toward the sampled warden position. All behaviors update through delta time, use component configuration, emit projectiles through the same factory, and can be applied to any entity with `EnemyState`.

- [ ] **Step 4: Implement objectives and gates**

Encounter state activates zone enemies, counts active configured hostiles, advances waves, allows shard-funded conduit interaction, toggles linked gate visibility/state, changes spawn checkpoint, and advances the objective phase. Emit `encounter.wave_completed`, `conduit.activated`, and `gate.opened` facts.

- [ ] **Step 5: Implement the guardian state machine**

Stage one is shielded while orbiting nodes remain; stage two exposes the core and alternates aimed volleys with radial projectiles; low health increases cadence; zero health marks defeat, opens the core, and transitions the encounter to victory. Guardian logic remains in the authored module and uses ordinary configured entities.

- [ ] **Step 6: Implement pause, defeat, victory, and deterministic reset**

Pause freezes authored simulation while still allowing pause/reset input. Defeat occurs at zero integrity. Reset restores stable entities from explicit component spawn/base properties, removes every dynamic projectile/effect by component query, restores collectibles/enemies/conduits/gates/guardian/HUD, and resets frame-derived authored sequences without changing entity IDs.

- [ ] **Step 7: Prove progression and reset with exact inspections**

Build modules. Run `progression-inputs.json` to prove a conduit activation and guardian stage/property transition. Run `reset-inputs.json` with a reset press after a state-changing action and prove `WardenState.objectivePhase == "arrival"`, shard count zero, and the warden transform returned to the authored spawn.

- [ ] **Step 8: Commit complete gameplay progression**

```powershell
git add Examples/AetherfallCitadel tests/Rekall.Age.Tests/Examples/AetherfallCitadelAcceptanceTests.cs
git commit -m "feat: complete Aetherfall progression and guardian"
```

### Task 5: Model Assets, World Composition, HUD, and Playable Shell

**Files:**
- Create: `Examples/AetherfallCitadel/Modules/AetherfallRules/PresentationSimulation.cs`
- Create: `Examples/AetherfallCitadel/Modules/AetherfallPlayable/AetherfallPlayable.csproj`
- Create: `Examples/AetherfallCitadel/Modules/AetherfallPlayable/AetherfallPlayableModule.cs`
- Modify: `Examples/AetherfallCitadel/Scenes/Main.age.scene.json`
- Create/Modify: `Examples/AetherfallCitadel/Modeling/**`
- Create/Modify: `Examples/AetherfallCitadel/Assets/Models/**`
- Create/Modify: `Examples/AetherfallCitadel/Assets/assets.age.catalog.json`
- Modify: `tests/Rekall.Age.Tests/Examples/AetherfallCitadelAcceptanceTests.cs`

**Interfaces:**
- Consumes: all gameplay state and AGE's editable mesh → model asset → scene reference pipeline.
- Produces: at least ten reusable published model assets, composed camera/light/HUD state, readable world feedback, and player-facing launch text.

- [ ] **Step 1: Add failing asset and presentation assertions**

Parse the catalog and assert at least ten model-asset entries; assert all model references resolve to catalog IDs; assert exactly one active perspective camera; assert HUD labels for objective, integrity, aether, shards, score/combo, and guardian; assert at least sixty visible stable entities.

- [ ] **Step 2: Run the focused tests and verify the asset/presentation floor fails**

Run the Aetherfall acceptance filter and retain the counts reported by the failure.

- [ ] **Step 3: Author and publish the reusable model set**

Use persistent editable meshes and published model assets for warden, sentinel, orbiter, lancer, guardian core, conduit, gate, bridge segment, shard, and citadel spire. Preserve stable mesh/model IDs and compiled provenance. Use model references for repetitions and keep primitive geometry for broad architectural masses where it is clearer and cheaper.

- [ ] **Step 4: Compose the three spaces**

Apply the spec's indigo/cyan/gold, magenta, and red/white-gold color script. Arrange floors, bridges, rails, pillars, rings, distant spires, gates, and vertical landmarks so the active camera shows a strong opening depth stack and each combat zone remains readable. No decorative entity may obscure the warden or objective path from the intended camera.

- [ ] **Step 5: Implement reactive presentation synchronization**

Follow the warden with bounded camera smoothing, keep the look target ahead of motion, synchronize label text and color with state, pulse conduit/gate/guardian visual roles, hide inactive encounter actors, and animate configured effect transforms. Emit a structured warning if required HUD/camera/warden entities are missing.

- [ ] **Step 6: Add the playable shell**

Register a playable that prints the title, objective, semantic controls, and current readiness in a concise frame. It does not implement or claim gameplay.

- [ ] **Step 7: Build, validate, and run structural tests**

Run module build, scene validation, asset listing, and the Aetherfall acceptance filter. Expected: at least ten resolved model assets, no blocking diagnostics, and all structural thresholds pass.

- [ ] **Step 8: Commit visual composition and player shell**

```powershell
git add Examples/AetherfallCitadel tests/Rekall.Age.Tests/Examples/AetherfallCitadelAcceptanceTests.cs
git commit -m "feat: compose the Aetherfall citadel experience"
```

### Task 6: Generic AGE Repair Protocol

**Files:**
- Test: `tests/Rekall.Age.Tests/<owning-subsystem>/<GenericContractTests>.cs`
- Modify: `src/Rekall.Age.<OwningSubsystem>/<focused files>`
- Modify when behavior is user-facing: `README.md` or `docs/production/PROGRESS.md`

**Interfaces:**
- Consumes: a concrete failed Aetherfall authoring, runtime, rendering, Studio, or delivery command with bounded inputs.
- Produces: a genre-neutral contract, test, diagnostic, and passing original Aetherfall checkpoint.

- [ ] **Step 1: Save the smallest realistic reproduction**

Reduce the failure to one checked-in xUnit case or existing Aetherfall proof payload. Name the test after the generic behavior, such as dynamic entity inspection, model-reference visibility, camera selection, input projection, or package relocation—not after the game.

- [ ] **Step 2: Run the focused test and verify the intended failure**

The expected failure must identify incorrect observable output, missing diagnostic, or missing generic operation. If the test passes, the deficiency is not reproduced and no engine code changes.

- [ ] **Step 3: Inspect Godot and Blender source architecture when the contract concerns scene/resource ownership or editable/evaluated data**

Use their source as architectural reference only. Record the transferable principle in the commit body or production note; do not copy incompatible implementation or add a dependency.

- [ ] **Step 4: Implement the smallest generic repair**

Keep APIs and diagnostics free of Aetherfall vocabulary. Prefer improving an existing primitive over adding a new one. Preserve compatibility unless the reproduced defect proves the old contract unsafe for common workflows.

- [ ] **Step 5: Run the focused engine test and the unchanged Aetherfall checkpoint**

Both must pass. Then run the owning test project or focused subsystem suite to detect local regressions.

- [ ] **Step 6: Commit the generic repair separately**

Stage only the focused engine test, implementation, and applicable documentation. Use a commit subject that names the generic capability.

This task repeats whenever a new concrete engine deficiency is exposed; it is not permission for speculative refactoring.

### Task 7: Gameplay, Visual, and Performance Acceptance

**Files:**
- Modify: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md`
- Create: ignored/local `Examples/AetherfallCitadel/Builds/Proof/*.png`

**Interfaces:**
- Consumes: final scene/module bytes, four proof payload pairs, native capture, visibility inspection, and performance budget inspection.
- Produces: fresh deterministic gameplay results and three visually reviewed native frames.

- [ ] **Step 1: Rebuild and rerun every gameplay proof after the latest mutation**

Run movement, combat, progression, and reset inspections with their exact checked-in payloads. Record command, commit, assertion summary, entity/renderable counts, and timestamp in `Proof/ACCEPTANCE.md`.

- [ ] **Step 2: Run scene validation and gameplay soak**

Run validation and a long deterministic runtime inspection with representative held movement/fire frames. Expected: no blocking observations, bounded entity growth after projectile/effect expiry, and stable reset behavior.

- [ ] **Step 3: Run visibility and performance diagnostics**

Run `rekall.render.visibility.inspect_scene` and `rekall.render.performance.inspect_scene_budget` through CLI command execution for `Main`. Expected: `CitadelCamera` is active, required player/objective/guardian renderables are not unintentionally culled, and draw/geometry counts remain within reported default budgets.

- [ ] **Step 4: Capture three native gameplay states**

Capture 1280×720 or larger frames for opening vista, resonance-court combat, and guardian vulnerable stage using deterministic input frame files. Ensure each capture follows the final module/scene build.

- [ ] **Step 5: Inspect every PNG visually**

Use the image viewer at original detail. Reject and repair frames with accidental default framing, clipped HUD, unreadable actor silhouettes, excessive empty space, z-fighting, missing geometry, flat color hierarchy, or absent gameplay feedback. Rebuild and recapture after every repair.

- [ ] **Step 6: Run independent visual and code review**

The reviewer checks the spec, final diff, captures, assertion evidence, generic-core boundary, immutable mutation correctness, delta-time use, reset completeness, and visible defects. Resolve every Critical or Important finding and rerun affected evidence.

- [ ] **Step 7: Commit accepted proof documentation**

```powershell
git add Examples/AetherfallCitadel/Proof/ACCEPTANCE.md
git commit -m "test: accept Aetherfall gameplay and visuals"
```

### Task 8: Studio, Windows Package, Relocation, Audit, Merge, and Push

**Files:**
- Modify if a real delivery defect is found: focused Studio/workflow/player files and their tests under `src/` and `tests/`
- Modify: `Examples/AetherfallCitadel/Proof/ACCEPTANCE.md`

**Interfaces:**
- Consumes: accepted final project and current Windows packaging workflow.
- Produces: Studio-openable project, relocatable package with both launchers, passing audit/capture, reviewed branch, and pushed `master`.

- [ ] **Step 1: Exercise the Studio path**

Open `Examples/AetherfallCitadel`/`Main`, build modules, run the player, use the Delivery tab to package Windows, and verify the output action points at the generated package. Any reproducible Studio defect enters Task 6.

- [ ] **Step 2: Run the closed-loop gauntlet**

Run: `dotnet run --project src/Rekall.Age.Cli -- game gauntlet Examples/AetherfallCitadel "Aetherfall: Citadel of Echoes" Main Examples/AetherfallCitadel/Builds/Gauntlet`

Expected: ready, package created, proof frame non-blank, and every gauntlet check passes.

- [ ] **Step 3: Build the explicit Windows graphics package**

Run: `dotnet run --project src/Rekall.Age.Cli -- game package-playable Examples/AetherfallCitadel Main Examples/AetherfallCitadel/Builds/Windows --target windows`

Assert `Play.exe`, `Play.bat`, manifest, scene, rules/playable assemblies, asset catalog, model assets, and compiled meshes exist.

- [ ] **Step 4: Relocate and run the package**

Create a fresh explicit temporary destination, run `game relocate-package`, then `game run-package <relocated-path> 4`. Expected: ready and exit code 0. Also launch `Play.exe` interactively long enough to confirm the desktop window accepts keyboard input.

- [ ] **Step 5: Capture and audit the relocated package**

Run `game capture-package-frame` with a representative input file and `game audit-package` against the relocated package. Inspect the resulting PNG visually. Expected: captured, non-blank, audit ready, no missing key artifacts.

- [ ] **Step 6: Run final verification**

Run `git diff --check`, Aetherfall acceptance tests, Studio tests if Studio bytes changed, the full `Rekall.Age.Tests` suite if engine bytes changed, and final four gameplay inspections if any game bytes changed. Confirm `git status --short` contains only intended files.

- [ ] **Step 7: Request final code review and resolve findings**

Review from the branch base through HEAD. No Critical or Important findings may remain. Re-run only evidence affected by review changes.

- [ ] **Step 8: Merge and push automatically**

Fast-forward `master` to the exact reviewed/tested commit, verify commit identity and clean worktree, and push `origin master`. Do not rerun identical suites merely because the branch name changed.

- [ ] **Step 9: Continue from the strongest accepted testbed gap**

Update the production progress note with the accepted game, evidence, generic engine fixes, and the next highest-value world-class capability exposed by actual play. Start that next milestone automatically under the standing user goal.
