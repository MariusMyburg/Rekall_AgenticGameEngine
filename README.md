# Rekall AGE

Rekall AGE is the Rekall Agentic Game Engine: a C#/.NET game engine designed so AI agents and humans can author complete games through inspectable, composable engine contracts.

The central idea is deliberately different from a traditional genre-first engine. Rekall AGE does not try to become a built-in first-person controller, platformer controller, tower defense loop, or RPG rules engine. Instead, it exposes generic project files, scene entities, components, runtime facts, diagnostics, SDK helpers, command bus operations, MCP tools, rendering paths, and packaging workflows. Agents use those primitives to author the actual game.

This README is intended to be the broad public entry point and the practical technical reference for the repository.

## Table of Contents

- [What Makes Rekall AGE Agentic](#what-makes-rekall-age-agentic)
- [Status](#status)
- [Quick Start](#quick-start)
- [Repository Layout](#repository-layout)
- [Core Architecture](#core-architecture)
- [Project and Scene Model](#project-and-scene-model)
- [Command Bus and Transactions](#command-bus-and-transactions)
- [MCP Integration](#mcp-integration)
- [CLI Reference](#cli-reference)
- [Agent Authoring Guide](#agent-authoring-guide)
- [Runtime Modules and SDK Helpers](#runtime-modules-and-sdk-helpers)
- [Input, Events, Timers, Pointers, Collisions, and Triggers](#input-events-timers-pointers-collisions-and-triggers)
- [Rendering Overview](#rendering-overview)
- [Vulkan Renderer](#vulkan-renderer)
- [Materials, Shaders, Procedural Materials, and GLB](#materials-shaders-procedural-materials-and-glb)
- [Performance Budgets, Render Layers, and Visibility](#performance-budgets-render-layers-and-visibility)
- [Virtual Geometry](#virtual-geometry)
- [VR and OpenXR](#vr-and-openxr)
- [Planet and Solar-System Rendering](#planet-and-solar-system-rendering)
- [Physics and Interaction](#physics-and-interaction)
- [Multiplayer](#multiplayer)
- [Live Player Editing](#live-player-editing)
- [Starter Templates and Packaging Workflows](#starter-templates-and-packaging-workflows)
- [Studio and Workbench Foundation](#studio-and-workbench-foundation)
- [Testing and Verification](#testing-and-verification)
- [Design Principles for Contributors](#design-principles-for-contributors)

## What Makes Rekall AGE Agentic

Rekall AGE is built around a simple rule:

> Engine core should provide generic authoring primitives. Game-specific behavior belongs in agent-authored modules, templates, examples, or user projects.

That has several practical consequences:

- The engine exposes scene data as deterministic entities and components.
- AI agents can inspect engine status, command schemas, component schemas, project summaries, runtime snapshots, viewport diagnostics, transaction history, and package audits.
- Runtime gameplay is implemented in project C# modules that use engine SDK helpers.
- Commands are routed through a typed command bus and can be reached through CLI or MCP.
- Mutating commands record transactions and resource changes so agents can reason about what changed.
- Visual output is verified through viewport captures, screenshot analysis, visibility inspection, and performance budgets.
- Generic facts such as `entity.tick`, `pointer.click`, `timer.elapsed`, `collision.begin`, and `trigger.enter` are emitted by the runtime, but the engine does not decide what they mean for a specific game.

In other words: Rekall AGE gives agents the hands, eyes, memory, and diagnostic vocabulary they need to author games. It does not ask the engine to author the game for them.

## Status

Rekall AGE is an active engine prototype with a substantial vertical slice:

- deterministic project, scene, entity, and component files
- typed command bus
- CLI adapter
- MCP stdio JSON-RPC adapter
- transaction history and preimage restore
- C# module scaffolding, source editing, compilation, and runtime loading
- project-authored runtime systems
- headless runtime snapshots
- software viewport capture
- Vulkan-first rendering path
- Vulkan device, command buffer, buffer, image, render target, render pass, shader, and scene-capture diagnostics
- runtime viewport capture and frame-analysis diagnostics
- GLB import inspection and GLB export
- generated primitives, custom meshes, procedural extrusions, line segments, and render materials
- render layers, camera culling masks, stereo planning, OpenXR planning, and performance budgets
- CPU-side virtual geometry LOD for dense meshes
- KSA planet and solar-system import workflows
- procedural planet, cloud, atmosphere, orbit, ring, starfield, marker, halo, and label renderables
- starter game templates and closed-loop playable package workflows
- live player editing over local IPC
- generic runtime events, timers, pointer rays, collision facts, trigger facts, observations, semantic input, camera helpers, and multiplayer helpers
- Windows player and WPF Studio foundation

The renderer is Vulkan-first, but the engine architecture keeps backend-neutral render plans and a Direct3D 12 extension point. The current virtual geometry implementation is a near-term CPU-side clustered LOD system; it is not a full GPU Nanite clone yet.

## Quick Start

Prerequisites:

- A .NET SDK capable of building `net10.0` projects.
- Windows for the current desktop player and WPF Studio shell.
- A Vulkan-capable GPU and driver for native Vulkan rendering.
- Optional: SteamVR/OpenXR runtime for headset testing.

Build and test:

```powershell
dotnet build Rekall.AGE.sln
dotnet test Rekall.AGE.sln
```

Inspect the engine:

```powershell
dotnet run --project src/Rekall.Age.Cli -- context engine
dotnet run --project src/Rekall.Age.Cli -- templates list
dotnet run --project src/Rekall.Age.Cli -- module schemas
```

Create and prove a starter playable game:

```powershell
dotnet run --project src/Rekall.Age.Cli -- game gauntlet .age-sandbox "Gauntlet Pong" pong .age-sandbox/Builds/GauntletPong .age-sandbox/Artifacts/GauntletAudit
```

Run the MCP server:

```powershell
dotnet run --project src/Rekall.Age.Cli -- mcp stdio
```

Open Studio:

```powershell
dotnet run --project src/Rekall.Age.Studio -- --project .age-sandbox --scene Main
```

Capture a runtime viewport:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport 640 360 vulkan
```

Launch the player:

```powershell
dotnet run --project src/Rekall.Age.Player -- .age-sandbox Main
dotnet run --project src/Rekall.Age.Player -- .age-sandbox Main --frames 2 --inputs '[{"verticalAxis":1,"primaryAction":true},{"verticalAxis":-1}]'
```

## Repository Layout

| Path | Purpose |
| --- | --- |
| `src/Rekall.Age.Core` | command bus, command schemas, command results, transactions |
| `src/Rekall.Age.Project` | project manifests and capabilities |
| `src/Rekall.Age.World` | scene, entity, component, prefab, and blueprint storage |
| `src/Rekall.Age.Modules` | built-in component schemas, module attributes, runtime module SDK helpers |
| `src/Rekall.Age.Runtime.Abstractions` | runtime world, entity, subsystem, input, event, multiplayer, and render contracts |
| `src/Rekall.Age.Runtime` | runtime snapshot builder, built-in runtime systems, multiplayer commands |
| `src/Rekall.Age.Rendering.Abstractions` | viewport frame, renderable, camera, material, stereo, and virtual geometry contracts |
| `src/Rekall.Age.Rendering` | software viewport, Vulkan renderer, GLB export, OpenXR planning, virtual geometry, render commands |
| `src/Rekall.Age.AssetPipeline` | asset import reports and metadata inspection |
| `src/Rekall.Age.Assets` | deterministic asset catalog and asset import commands |
| `src/Rekall.Age.LevelDesign` | level editing, geometry creation, KSA planet/solar import, prefabs |
| `src/Rekall.Age.GameTemplates` | starter templates and playable workflow commands |
| `src/Rekall.Age.Playback` | terminal/player-facing playback and playtest workflows |
| `src/Rekall.Age.Validation` | generic scene validation |
| `src/Rekall.Age.Agent` | engine status and agent context commands |
| `src/Rekall.Age.Mcp` | MCP tool catalog and stdio JSON-RPC adapter |
| `src/Rekall.Age.Cli` | CLI adapter over the command registry |
| `src/Rekall.Age.Player` | player runtime |
| `src/Rekall.Age.Player.Windows` | Windows graphics player and live-edit server |
| `src/Rekall.Age.Studio` | WPF workbench shell |
| `tests/Rekall.Age.Tests` | engine, CLI, runtime, rendering, workflow, and regression tests |
| `Examples` | example Rekall projects and captures |
| `docs/superpowers` | implementation specs and plans used during development |

## Core Architecture

Rekall AGE is organized around a few stable contracts.

### Typed Commands

Engine operations implement `IRekallAgeCommand<TRequest, TResult>`. Each command exposes:

- a stable command name such as `rekall.render.capture_runtime_viewport`
- a human-readable description
- request/result CLR type names
- structured success or failure output
- zero or more structured command errors

The same command registry is used by CLI and MCP, which keeps agent tooling and human terminal usage aligned.

### Deterministic Authoring Files

Projects contain:

- `rekall.project.json`
- `Scenes/<scene>.age.scene.json`
- `Assets/assets.age.catalog.json`
- `Modules/<ModuleName>/...`
- `Transactions/transactions.age.json`
- optional transaction snapshots under `Transactions/Snapshots/<transaction-id>/`

Scenes are made of entities. Entities have ids, names, tags, visibility, transforms, hierarchy, and arbitrary typed components.

### Runtime Snapshot

The runtime builds immutable frame snapshots from scene data and compiled project modules. Runtime systems consume an input frame, advance generic subsystems, emit observations/events, mutate runtime entities, and project renderable records into the viewport contract.

### Rendering Frame

Rendering consumes `RekallAgeRuntimeViewportFrame`: active camera, cameras, renderables, observations, culling diagnostics, stereo settings, post-process stack, and material/mesh/texture metadata. Software capture, Vulkan capture, OpenXR planning, performance budgets, GLB export, and virtual geometry all read from this same projected frame.

## Project and Scene Model

Core authoring concepts:

- A project declares capabilities such as `world`, `rendering3d`, `planet`, or template-specific needs.
- A scene declares scene capabilities and owns entity documents.
- Entities are generic data records, not engine-owned game classes.
- Components are JSON-backed records with typed schemas discovered from built-in modules and compiled project modules.
- Tags are agent-facing labels for querying and grouping.
- Scene mutations can be applied one entity at a time or in high-throughput blueprints.

Important world commands:

```powershell
dotnet run --project src/Rekall.Age.Cli -- project create .age-sandbox "My Game" world,rendering3d
dotnet run --project src/Rekall.Age.Cli -- scene create .age-sandbox Main world,rendering3d
dotnet run --project src/Rekall.Age.Cli -- entity create .age-sandbox Main "Player" player,controllable
dotnet run --project src/Rekall.Age.Cli -- entity inspect .age-sandbox Main <entity-id>
dotnet run --project src/Rekall.Age.Cli -- component set .age-sandbox Main <entity-id> Rekall.Transform x 42
dotnet run --project src/Rekall.Age.Cli -- validation scene .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- context summary .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- context scene .age-sandbox Main
```

High-throughput agent scene editing is available through MCP command `rekall.scene.apply_blueprint`, and CLI-adjacent level tools include duplication, parenting, prefab creation, prefab instantiation, and grid snapping.

## Built-In Components

Built-in component schemas are provided by `rekall.builtins`. Current built-ins include:

- `Rekall.Transform`
- `Rekall.InputActionMap`
- `Rekall.EventBindings`
- `Rekall.PointerRay`
- `Rekall.Timer`
- `Rekall.Camera2D`
- `Rekall.Camera3D`
- `Rekall.CameraZoomInput`
- `Rekall.CameraTarget3D`
- `Rekall.CameraTargetCycleInput`
- `Rekall.RenderLayer`
- `Rekall.XrRig`
- `Rekall.XrPoseSource`
- `Rekall.XrController`
- `Rekall.DirectionalLight`
- `Rekall.PointLight`
- `Rekall.MultiplayerSession`
- `Rekall.NetworkIdentity`
- `Rekall.NetworkTransform`
- `Rekall.GeometryPrimitive`
- `Rekall.GeometryMesh`
- `Rekall.LineSegments`
- `Rekall.GeometryExtrusion`
- `Rekall.Material`
- `Rekall.ProceduralMaterial`
- `Rekall.LodGroup`
- `Rekall.VirtualGeometry`
- `Rekall.PhysicsWorld3D`
- `Rekall.PhysicsMaterial3D`
- `Rekall.Rigidbody3D`
- `Rekall.Trigger`
- `Rekall.BoxCollider2D`
- `Rekall.CircleCollider2D`
- `Rekall.BoxCollider3D`
- `Rekall.SphereCollider3D`
- `Rekall.CapsuleCollider3D`
- `Rekall.MeshCollider`
- `Rekall.PlanetRenderer`
- `Rekall.CloudLayerRenderer`
- `Rekall.AtmosphereRenderer`
- `Rekall.CelestialBody`
- `Rekall.KeplerOrbit`
- `Rekall.CelestialRotation`
- `Rekall.OrbitPathRenderer`
- `Rekall.RingRenderer`
- `Rekall.StarfieldRenderer`
- `Rekall.MarkerRenderer`
- `Rekall.HaloRenderer`
- `Rekall.PostProcessStack`
- `Rekall.TextLabelRenderer`

Inspect schemas:

```powershell
dotnet run --project src/Rekall.Age.Cli -- module schemas
dotnet run --project src/Rekall.Age.Cli -- module schemas project .age-sandbox
```

## Command Bus and Transactions

Every CLI or MCP mutation runs inside a transaction. Transactions are persisted project-locally in `Transactions/transactions.age.json` and include:

- transaction id
- command label
- source (`cli`, `mcp`, or other host)
- resource-change summaries
- relative paths
- resource kind
- existence state
- file size metadata when available

Commands that capture preimages write snapshots under `Transactions/Snapshots/<transaction-id>/`. Use preimage restore to roll back a resource:

```powershell
dotnet run --project src/Rekall.Age.Cli -- transaction history .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- transaction restore-preimage .age-sandbox <transaction-id> Scenes/Main.age.scene.json
```

MCP command results include transaction metadata in structured content so agents can immediately inspect command effects without rereading the whole project.

## MCP Integration

Rekall AGE includes a stdio MCP JSON-RPC adapter:

```powershell
dotnet run --project src/Rekall.Age.Cli -- mcp stdio
```

The MCP server supports:

- `initialize`
- `tools/list`
- `tools/call`

The MCP tool catalog is generated from the command registry. Tools are categorized by name prefix:

- `rekall.context.*`
- `rekall.templates.*`
- `rekall.workflow.*`
- `rekall.transaction.*`
- `rekall.render.*`
- `rekall.shader.*`
- `rekall.module.*`
- `rekall.live.*`
- `rekall.multiplayer.*`
- `rekall.play*`
- `rekall.asset.*`
- `rekall.project.*`
- `rekall.scene.*`
- `rekall.entity.*`
- `rekall.geometry.*`
- `rekall.planet.*`
- `rekall.solar.*`
- `rekall.component.*`

Recommended agent entry points include:

- `rekall.context.engine_status`
- `rekall.templates.inspect`
- `rekall.workflow.agent_authoring_gauntlet`
- `rekall.workflow.create_playable_package_from_template`
- `rekall.workflow.audit_playable_package`
- `rekall.scene.apply_blueprint`
- `rekall.validation.scene`
- `rekall.render.capture_runtime_viewport`
- `rekall.render.performance.inspect_scene_budget`
- `rekall.render.visibility.inspect_scene`
- `rekall.render.virtual_geometry.inspect_scene`
- `rekall.render.virtual_geometry.apply_scene`
- `rekall.render.openxr.bootstrap_session`
- `rekall.render.openxr.inspect_headset_frame_plan`
- `rekall.solar.import_ksa_system`
- `rekall.live.status`
- `rekall.live.apply_scene_blueprint`
- `rekall.live.apply_scene_diff`
- `rekall.module.scaffold_runtime_system`
- `rekall.module.write_source`
- `rekall.build.modules`
- `rekall.multiplayer.snapshot`
- `rekall.multiplayer.delta`

MCP logging is intentionally kept away from JSON-RPC stdout. By default logs are written under `%LOCALAPPDATA%\Rekall AGE\Mcp\Logs`. Use `REKALL_AGE_MCP_LOG_DIR` or shared `REKALL_AGE_LOG_DIR` to redirect logs for automation.

## CLI Reference

The CLI command shape is:

```powershell
dotnet run --project src/Rekall.Age.Cli -- <area> <command> ...
```

Common command groups:

```powershell
dotnet run --project src/Rekall.Age.Cli -- templates list
dotnet run --project src/Rekall.Age.Cli -- templates inspect pong
dotnet run --project src/Rekall.Age.Cli -- templates verify-mvp

dotnet run --project src/Rekall.Age.Cli -- game create .age-sandbox "Crystal Mines" pong
dotnet run --project src/Rekall.Age.Cli -- game create-playable .age-sandbox "Playable Pong" pong
dotnet run --project src/Rekall.Age.Cli -- game verify-playable .age-sandbox Main 2
dotnet run --project src/Rekall.Age.Cli -- game package-playable .age-sandbox Main .age-sandbox/Builds/RekallAgePlayer
dotnet run --project src/Rekall.Age.Cli -- game audit-package .age-sandbox/Builds/RekallAgePlayer.zip .age-sandbox/Artifacts/PackageAudit

dotnet run --project src/Rekall.Age.Cli -- asset import .age-sandbox .\player.png sprite "Player"
dotnet run --project src/Rekall.Age.Cli -- asset import-report .age-sandbox .\robot.glb model "Robot"
dotnet run --project src/Rekall.Age.Cli -- asset list .age-sandbox

dotnet run --project src/Rekall.Age.Cli -- module scaffold-runtime-system .age-sandbox game.motion "Game Motion" GameMotion OrbitMotion OrbitMotionSystem
dotnet run --project src/Rekall.Age.Cli -- module sources .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- module read-source .age-sandbox GameMotion GameMotionModule.cs
dotnet run --project src/Rekall.Age.Cli -- module write-source .age-sandbox GameMotion GameMotionModule.cs .\GameMotionModule.cs
dotnet run --project src/Rekall.Age.Cli -- build modules .age-sandbox

dotnet run --project src/Rekall.Age.Cli -- runtime inspect .age-sandbox Main 3
dotnet run --project src/Rekall.Age.Cli -- play scene .age-sandbox Main 4
dotnet run --project src/Rekall.Age.Cli -- playtest scene .age-sandbox Main 2 '[{"verticalAxis":1}]' '[{"frameIndex":0,"contains":"Score"}]'

dotnet run --project src/Rekall.Age.Cli -- geometry primitive create .age-sandbox Main "Cube" cube 0 0 0 "#8ab4f8"
dotnet run --project src/Rekall.Age.Cli -- geometry mesh create .age-sandbox Main "Triangle" '[{"x":0,"y":0,"z":0},{"x":1,"y":0,"z":0},{"x":0,"y":1,"z":0}]' '[0,1,2]'
dotnet run --project src/Rekall.Age.Cli -- geometry extrusion create .age-sandbox Main "Block" '[{"x":-0.5,"y":-0.5},{"x":0.5,"y":-0.5},{"x":0.5,"y":0.5},{"x":-0.5,"y":0.5}]' 1
```

CLI logging defaults to `%LOCALAPPDATA%\Rekall AGE\Cli\Logs`. Use `REKALL_AGE_CLI_LOG_DIR` or `REKALL_AGE_LOG_DIR` to redirect logs.

## Agent Authoring Guide

This section is the practical loop for using AI agents with Rekall AGE.

### 1. Inspect Before Editing

Start by asking the engine what it knows:

```powershell
dotnet run --project src/Rekall.Age.Cli -- context engine
dotnet run --project src/Rekall.Age.Cli -- context summary <projectRoot>
dotnet run --project src/Rekall.Age.Cli -- context scene <projectRoot> <sceneName>
dotnet run --project src/Rekall.Age.Cli -- module schemas
dotnet run --project src/Rekall.Age.Cli -- validation scene <projectRoot> <sceneName>
```

Agents should read existing scene/module content before replacing it. Prefer narrow mutations, blueprints, or module source edits over wholesale scene rewrites.

### 2. Author With Generic Data

Good agent-authored game data uses:

- entity names for readability
- tags for stable group queries
- generic components for engine-facing behavior
- project components for game-specific state
- `Rekall.InputActionMap` for semantic controls
- `Rekall.EventBindings` for generic event facts
- `Rekall.Timer`, `Rekall.PointerRay`, colliders, and triggers for reusable runtime facts
- `Rekall.RenderLayer` and camera culling masks for visibility
- `Rekall.Material`, `Rekall.ProceduralMaterial`, `Rekall.GeometryMesh`, and `Rekall.GeometryPrimitive` for visual output

Avoid hard-coding one genre into engine core. A door opening, weapon firing, inventory pickup, dialogue trigger, or vehicle controller should be project module behavior.

### 3. Put Game Rules in Runtime Modules

Scaffold a runtime system:

```powershell
dotnet run --project src/Rekall.Age.Cli -- module scaffold-runtime-system <projectRoot> game.rules "Game Rules" GameRules GameState GameRulesSystem
```

Then edit module source and build:

```powershell
dotnet run --project src/Rekall.Age.Cli -- module sources <projectRoot>
dotnet run --project src/Rekall.Age.Cli -- module read-source <projectRoot> GameRules GameRulesModule.cs
dotnet run --project src/Rekall.Age.Cli -- module write-source <projectRoot> GameRules GameRulesModule.cs .\GameRulesModule.cs
dotnet run --project src/Rekall.Age.Cli -- build modules <projectRoot>
```

Runtime modules implement `IRekallAgeRuntimeModuleSystem`, run during snapshots, and receive frame time/input. Use the engine-provided time step for movement, animation, timers, cooldowns, and simulation.

### 4. Verify With Closed Loops

Use increasingly strong proof loops:

```powershell
dotnet run --project src/Rekall.Age.Cli -- validation scene <projectRoot> <sceneName>
dotnet run --project src/Rekall.Age.Cli -- runtime inspect <projectRoot> <sceneName> 3
dotnet run --project src/Rekall.Age.Cli -- render viewport capture <projectRoot> <sceneName> 3 <artifactDir> 640 360 software
dotnet run --project src/Rekall.Age.Cli -- render viewport capture <projectRoot> <sceneName> 3 <artifactDir> 640 360 vulkan
dotnet run --project src/Rekall.Age.Cli -- render performance budget <projectRoot> <sceneName> desktop60 0 1920 1080
dotnet run --project src/Rekall.Age.Cli -- game verify-playable <projectRoot> <sceneName> 2
```

For user-facing playable delivery, prefer:

```powershell
dotnet run --project src/Rekall.Age.Cli -- game gauntlet <projectRoot> "Game Name" <template> <buildDir> <auditDir>
```

### 5. Emit Observations Instead of Failing Silently

Project modules should emit structured observations when content is missing or inconsistent:

- blocking observation: scene cannot run correctly
- warning observation: scene runs but likely needs author attention
- info observation: useful state for agents

Observations appear in runtime inspection, viewport diagnostics, Studio, and command output.

### 6. Use Runtime Diagnostics to Improve Authored Content

If a scene is technically valid but visually weak, inspect:

- active camera
- renderable counts
- culling masks
- render layers
- frame-analysis warnings
- layout bounds
- material/texture resolution
- performance budget pressure

Then revise ordinary scene data. Do not add a built-in "make this look good" engine feature when better diagnostics and authored transforms/materials/cameras solve the problem.

## Runtime Modules and SDK Helpers

The module SDK is designed to keep agent-authored C# short and generic.

Entity query helpers:

- `FindEntity`
- `EntitiesNamed`
- `EntitiesWithTag`
- `EntitiesWithComponent`
- `EntitiesWithTagAndComponent`

Mutation helpers:

- `ReplaceEntity`
- `UpdateEntity`
- `UpdateEntitiesWithTag`
- `UpdateEntitiesWithComponent`
- `UpdateEntitiesWithTagAndComponent`
- `WithTag`
- `WithoutTag`
- `WithVisible`
- `WithPosition3D`
- `WithRotation3D`
- `WithScale3D`
- `UpsertComponent`
- `UpdateComponent`

Input helpers:

- `InputActions`
- `InputActionValue`
- `IsInputActionDown`
- `WasInputActionPressed`
- `WasInputActionReleased`

Camera/vector helpers:

- `Forward3D`
- `Right3D`
- `Up3D`
- `Offset3D`

Event helpers:

- `EventsOfType`
- `EventsFor`
- `WasEventRaised`
- `EmitEvent`
- `EmitBoundEvents`

Observation helpers:

- `EmitObservation`
- `EmitSceneObservation`
- `ObservationsWithCode`
- `ObservationsWithSeverity`
- `ObservationsFor`
- `ObservationsForScene`
- `HasBlockingObservations`

Multiplayer helpers:

- `NetworkSessions`
- `PrimaryNetworkSession`
- `NetworkEntities`
- `NetworkEntityForEntity`
- `NetworkEntityByNetworkId`
- `NetworkEntitiesOwnedBy`
- `RuntimeEntitiesOwnedBy`
- `ReplicatedRuntimeEntities`
- `IsNetworkOwner`
- `IsReplicated`

Physics/query helpers:

- `Raycast3D`
- JSON property helpers such as `ReadNumber`, `ReadBoolean`, and `ReadString`

## Input, Events, Timers, Pointers, Collisions, and Triggers

### Semantic Input

`Rekall.InputActionMap` projects raw input into named actions:

```json
{
  "type": "Rekall.InputActionMap",
  "properties": {
    "active": true,
    "actions": [
      { "name": "move.x", "positiveKey": "D", "negativeKey": "A" },
      { "name": "move.y", "positiveKey": "W", "negativeKey": "S" },
      { "name": "fire", "button": "Left" },
      { "name": "zoom", "mouseWheelScale": 0.5 },
      { "name": "look.x", "mouseAxis": "x", "mouseScale": 0.12 }
    ]
  }
}
```

Runtime and player input snapshots include keyboard state, mouse buttons, mouse position, mouse delta, mouse wheel, and projected action values. Gameplay modules should consume semantic actions instead of hard-coding raw key folklore.

### Runtime Events

`Rekall.EventBindings` stores generic event subscriptions:

```json
{
  "type": "Rekall.EventBindings",
  "properties": {
    "events": [
      { "event": "entity.begin", "handler": "spawn" },
      { "event": "entity.tick", "handler": "update" },
      { "event": "pointer.click", "handler": "activate" },
      { "event": "timer.elapsed", "handler": "cooldownReady" }
    ]
  }
}
```

The runtime emits facts. Project modules decide what those facts mean.

### Timers

`Rekall.Timer` exposes `timerId`, `durationSeconds`, and `repeat`. The runtime advances timers with frame time, emits `timer.elapsed`, and stores inspectable timer state.

### Pointer Rays

`Rekall.PointerRay` defines a generic ray with origin, direction, range, button, and optional target filters. It emits `pointer.enter`, `pointer.leave`, `pointer.hit`, `pointer.down`, `pointer.up`, and `pointer.click` facts against 3D colliders.

### Collisions and Triggers

3D colliders emit `collision.begin`, `collision.stay`, and `collision.end`. `Rekall.Trigger` emits `trigger.enter`, `trigger.stay`, and `trigger.exit` for non-physical volumes. Pickups, damage, sensors, quests, doors, zones, checkpoints, and prompts remain module-authored behavior.

## Rendering Overview

Rekall AGE rendering starts from runtime viewport frames. A frame contains:

- scene name and frame index
- active camera
- all camera records
- renderables
- render layer/culling information
- material and texture references
- mesh/procedural geometry data
- stereo settings
- post-process stack
- observations

Supported renderable families include:

- sprites
- cube, sphere, cylinder, cone, and plane primitives
- custom `Rekall.GeometryMesh` triangle lists
- `Rekall.GeometryExtrusion`
- line segments
- imported GLB meshes
- planet renderers
- cloud layers
- atmosphere shells
- orbit paths
- rings
- starfields
- markers
- halos
- text labels

Rendering tools:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render backends
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport 640 360 vulkan
dotnet run --project src/Rekall.Age.Cli -- render glb export .age-sandbox Main .age-sandbox/Artifacts/Main.glb 0
```

Viewport captures include frame-analysis diagnostics:

- nonblank status
- color diversity
- dominant color ratio
- luminance statistics
- warnings such as flat color, one-color domination, low luminance variance, very dark, and near transparency

Viewport layout diagnostics report active camera rect, camera pose, spatial renderable bounds, dominant-axis warnings, flat-axis warnings, and authoring hints.

## Vulkan Renderer

The Vulkan path is the primary native renderer. It includes:

- native Vulkan loader probing
- physical-device and graphics-queue selection
- logical-device bootstrap
- command-pool allocation
- command-buffer recording
- queue submission and fence waits
- host-visible mapped buffers
- device-local images
- offscreen render targets
- image views, render passes, framebuffers, and readback
- bundled GLSL shader deployment and Shaderc compilation
- frame uniforms
- per-draw model push constants
- color and depth targets
- indexed draw submission
- texture and material binding
- Vulkan scene capture for generated primitives, authored meshes, imported GLB meshes, and planet renderables

Low-level Vulkan diagnostics:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render vulkan probe
dotnet run --project src/Rekall.Age.Cli -- render vulkan device bootstrap discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan command-buffer submit-empty discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan buffer create-mapped 256 vertex-buffer discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan image create-bound 64 64 R8G8B8A8_UNorm color-attachment discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan render-target create 128 72 R8G8B8A8_UNorm discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan render-pass read-clear 32 32 R8G8B8A8_UNorm discrete-gpu 0.25 0.5 0.75 1
dotnet run --project src/Rekall.Age.Cli -- render vulkan render-pass capture-clear 32 32 R8G8B8A8_UNorm discrete-gpu .age-sandbox/Artifacts/Vulkan 0.25 0.5 0.75 1
```

Backend-neutral render plans:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render plan create .age-sandbox vulkan NativePreview
dotnet run --project src/Rekall.Age.Cli -- render resource add .age-sandbox frame-color image R8G8B8A8_UNorm color-attachment,transfer-src
dotnet run --project src/Rekall.Age.Cli -- render command-buffer record .age-sandbox main graphics '[{"op":"begin-render-pass","label":"frame","arguments":{"target":"frame-color","width":"32","height":"16","preferredDeviceType":"discrete-gpu"}},{"op":"clear","label":"sky","arguments":{"r":"0.25","g":"0.5","b":"0.75","a":"1"}},{"op":"end-render-pass","label":"frame","arguments":{}}]'
dotnet run --project src/Rekall.Age.Cli -- render plan validate .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- render plan execute .age-sandbox .age-sandbox/Artifacts/Render
```

## Materials, Shaders, Procedural Materials, and GLB

### Materials

`Rekall.Material` supports:

- `baseColor`
- `baseColorTexture`
- `metallicFactor`
- `roughnessFactor`
- `metallicRoughnessTexture`
- `normalTexture`
- `normalScale`
- `occlusionTexture`
- `occlusionStrength`
- `emissiveColor`
- `emissiveTexture`
- `emissiveStrength`

### Procedural Materials

`Rekall.ProceduralMaterial` generates deterministic PBR texture channels from scene data. Generators include:

- `checker`
- `stripes`
- `rings`
- `noise`

Common properties include `resolution`, `scale`, `seed`, `baseColorA`, `baseColorB`, `metallicFactor`, `roughnessA`, `roughnessB`, `normalStrength`, and `emissiveStrength`.

### Project Shaders

Agents can author GLSL shaders through the command bus and MCP tools:

- `rekall.shader.list`
- `rekall.shader.read`
- `rekall.shader.write`
- `rekall.shader.validate`
- `rekall.shader.assign_pipeline`

Runtime render meshes can reference `RekallAgeRuntimeRenderShaderPipeline` without requiring new engine-specific component types.

### Geometry Authoring

Agents can create primitives, raw meshes, recipes, and extrusions:

```powershell
dotnet run --project src/Rekall.Age.Cli -- geometry primitive create .age-sandbox Main "Crystal Orb" sphere -2 1 0 "#33ff66"
dotnet run --project src/Rekall.Age.Cli -- geometry mesh create .age-sandbox Main "Agent Triangle" '[{"x":0,"y":0,"z":0},{"x":1,"y":0,"z":0},{"x":0,"y":1,"z":0,"r":0,"g":1,"b":0}]' '[0,1,2]' 0 0 0 "#ff6633"
dotnet run --project src/Rekall.Age.Cli -- geometry extrusion create .age-sandbox Main "Agent Block" '[{"x":-0.5,"y":-0.5},{"x":0.5,"y":-0.5},{"x":0.5,"y":0.5},{"x":-0.5,"y":0.5}]' 1 0 0 0 "#44ccff"
```

`Rekall.GeometryMesh` vertices support position, optional normals, optional vertex color, and optional UVs. If normals are omitted, the engine infers averaged normals from triangle winding.

### GLB Import and Export

Asset import can inspect GLB scenes, nodes, meshes, materials, images, and animations:

```powershell
dotnet run --project src/Rekall.Age.Cli -- asset import-report .age-sandbox .\robot.glb model "Robot"
```

Runtime-renderable scenes can be exported as binary glTF:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render glb export .age-sandbox Main .age-sandbox/Artifacts/Main.glb 0
```

The exporter resolves the same runtime snapshot used by viewport capture, writes GLB 2.0 buffers/accessors/materials, embeds texture data when available, and can merge imported GLB nodes, meshes, materials, images, textures, samplers, cameras, skins, animations, and extension declarations into the output.

## Performance Budgets, Render Layers, and Visibility

Performance budget inspection reports:

- entities
- renderables
- meshes
- draw calls
- estimated draw invocations
- triangles
- vertices
- textures
- runtime textures
- asset issues
- stereo mode
- multiview status
- eye count
- render-target pixels
- estimated geometry bytes
- per-layer breakdown
- camera masks
- culled renderables
- virtual geometry reductions
- blockers, warnings, and recommendations

Profiles:

| Profile | Target | Eyes | Draw invocations | Triangles | Vertices | Textures | Pixels |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `desktop60` | 60 FPS | 1 | 3,000 | 2,000,000 | 1,250,000 | 256 | 8,500,000 |
| `mobile60` | 60 FPS | 1 | 250 | 150,000 | 100,000 | 64 | 2,500,000 |
| `vr90` | 90 FPS | 2 | 1,500 | 1,000,000 | 650,000 | 128 | 8,500,000 |

Commands:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render performance budget .age-sandbox Main desktop60 0 1920 1080
dotnet run --project src/Rekall.Age.Cli -- render visibility inspect .age-sandbox Main 0
```

`Rekall.RenderLayer` lets renderables opt into named layers. `Rekall.Camera2D` and `Rekall.Camera3D` have `cullingMask` fields so cameras can include or exclude layers. Visibility inspection helps agents understand why an authored entity is hidden from a given camera.

`Rekall.LodGroup` provides explicit distance LODs that can swap to simpler primitives, alternate mesh assets, alternate texture assets, color overrides, and scale multipliers before batching.

## Virtual Geometry

Rekall AGE includes a near-term CPU-side virtual geometry system inspired by Nanite's broad goal: dense source meshes should not require every source triangle to be submitted every frame.

The current implementation is intentionally conservative:

- It is CPU-side clustered LOD selection in the current Vulkan path.
- It works on the engine mesh records before batching/upload.
- It reduces triangle/index/vertex payloads for enabled renderables.
- It uses source triangle budget and camera distance to choose a LOD level.
- It preserves the generic authoring contract through `Rekall.VirtualGeometry`.
- It is not yet GPU mesh-shader clustering, hierarchical visibility, occlusion-driven streaming, or disk-page virtualized geometry.

`Rekall.VirtualGeometry` properties:

```json
{
  "type": "Rekall.VirtualGeometry",
  "properties": {
    "enabled": true,
    "targetPixelError": 1.5,
    "clusterTriangleCount": 128,
    "maxSelectedTriangles": 12000,
    "maxLodLevel": 8,
    "debugMode": "off"
  }
}
```

Selection logic:

- `maxSelectedTriangles` computes a budget-driven LOD level.
- `targetPixelError` and camera distance compute a distance-driven LOD level.
- The higher of those levels is clamped by `maxLodLevel`.
- Triangles are sampled by stride within clusters.
- Vertex/index buffers are remapped to the selected triangles.
- Mesh records retain source triangle count and selected LOD for diagnostics.

Inspect virtual geometry:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render virtual-geometry inspect .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- render virtual-geometry inspect .age-sandbox Main 0 1920 1080
```

Apply virtual geometry to dense scene entities:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render virtual-geometry apply .age-sandbox Main 10000 --dry-run
dotnet run --project src/Rekall.Age.Cli -- render virtual-geometry apply .age-sandbox Main 10000
```

Apply it to one named entity:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render virtual-geometry apply-entity .age-sandbox Main Earth 30000 --dry-run
dotnet run --project src/Rekall.Age.Cli -- render virtual-geometry apply-entity .age-sandbox Main Earth 30000
```

KSA planet and solar-system importers automatically add `Rekall.VirtualGeometry` to dense planet renderables, including generated surface/atmosphere/cloud renderables where appropriate.

Example result from a detailed Earth scene:

- Overall budget triangles reduced from `634,610` to `567,023`.
- Virtualized renderables selected `5,577` of `73,164` source triangles.
- Virtualized renderables reduced `67,587` triangles.
- Estimated geometry bytes dropped from `23,846,616` to `21,738,564`.

Longer-term direction:

- GPU-first cluster hierarchy
- mesh shader or task/mesh shader submission where available
- persistent cluster pages
- streaming and residency management
- hierarchical culling
- debug visualization for selected clusters/pages

That future architecture should still keep the same agent-facing principle: dense geometry is controlled by inspectable scene components and diagnostics, not hidden genre-specific renderer behavior.

## VR and OpenXR

Rekall AGE has OpenXR diagnostics and a Vulkan headset-output planning path.

Important camera fields:

- `Camera3D.stereoMode`: `mono`, `stereo`, `vr`, or `xr`
- `Camera3D.stereoRenderMode`: `single-pass-multiview`, side-by-side preview, or compatibility modes
- `Camera3D.interpupillaryDistance`
- `Camera3D.stereoConvergenceDistance`
- `Camera3D.xrViewConfiguration`: usually `primary-stereo`
- `Camera3D.foveatedRendering`

Stereo planning:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render stereo inspect .age-sandbox Main 0 1920 1080
```

OpenXR runtime checks:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render openxr probe
dotnet run --project src/Rekall.Age.Cli -- render openxr bootstrap-session
dotnet run --project src/Rekall.Age.Cli -- render openxr frame-plan .age-sandbox Main 0 1920 1080
```

The headset frame plan checks:

- OpenXR session readiness
- HMD availability
- active stereo camera
- eye count
- multiview preference
- shared geometry buffers
- color/depth swapchain count
- swapchain array size
- recommended eye dimensions
- native Vulkan render target ownership
- required OpenXR frame-loop calls
- blockers and warnings

The native OpenXR/Vulkan plan uses:

- OpenXR-created Vulkan instance/device/queue for headset rendering
- OpenXR color swapchain images
- engine-owned depth images
- per-swapchain-image, per-eye views
- shared scene pipeline
- compositor-compatible final color layout

Operational note for local SteamVR/OpenXR sessions:

```powershell
$env:PATH = 'C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64;' + $env:PATH
dotnet run --project src\Rekall.Age.Cli -- render openxr bootstrap-session
```

A good bootstrap reports headset session ready, HMD system available, `XR_KHR_vulkan_enable2`, and two primary-stereo views.

Playable VR should use the windowed player so desktop keyboard/mouse input and OpenXR poses/actions flow into the same generic runtime input stream:

```powershell
Rekall.Age.Player.Windows.exe <projectRoot> <sceneName> --graphics --backend vulkan --vr
```

On high-resolution headsets, tune eye size explicitly when needed:

```powershell
Rekall.Age.Player.Windows.exe <projectRoot> <sceneName> --graphics --backend vulkan --vr --vr-eye-width 1600 --vr-eye-height 1600
```

The direct OpenXR submitter is a diagnostic/headset-output tool, not the normal playable path:

```powershell
dotnet run --project src\Rekall.Age.Cli -- render openxr submit-scene <projectRoot> <sceneName> 0 0 0
```

The first `0` means continuous submission. Width/height `0 0` means use the OpenXR runtime's recommended eye size.

## Planet and Solar-System Rendering

Rekall AGE includes generic components for large astronomical scenes:

- `Rekall.CelestialBody`
- `Rekall.KeplerOrbit`
- `Rekall.CelestialRotation`
- `Rekall.PlanetRenderer`
- `Rekall.CloudLayerRenderer`
- `Rekall.AtmosphereRenderer`
- `Rekall.OrbitPathRenderer`
- `Rekall.RingRenderer`
- `Rekall.StarfieldRenderer`
- `Rekall.MarkerRenderer`
- `Rekall.HaloRenderer`
- `Rekall.TextLabelRenderer`

KSA import commands:

```powershell
dotnet run --project src/Rekall.Age.Cli -- planet import-ksa .age-sandbox Planets <ksaRoot> Earth Gaia
dotnet run --project src/Rekall.Age.Cli -- solar import-ksa-system .age-sandbox SolarSystem <ksaRoot>
dotnet run --project src/Rekall.Age.Cli -- solar import-ksa-system .age-sandbox SolarSystem <ksaRoot> SolSystemDense.xml 0.000001 0.00002
```

The KSA solar importer reads `Content/Core/Astronomicals.xml` plus a system XML such as `SolSystem.xml` or `SolSystemDense.xml`, resolves `LoadFromLibrary` references, copies available diffuse KTX2 assets, and writes generic Rekall components.

Planet rendering features:

- generated sphere meshes with configurable slices/stacks
- surface texture support
- PBR material integration
- emissive stellar bodies
- automatic point-light behavior for stellar celestial bodies
- atmosphere shells with Rayleigh/Mie scattering parameters
- cloud-layer renderers
- cloud shadows
- water/specular material support
- orbit paths for planets and moons
- moon orbits relative to updated parent planets
- rings, starfields, markers, halos, and labels
- virtual geometry on dense generated planet meshes

`Rekall.KeplerOrbit` supports time scale, parent-body nesting, and readable local satellite scaling. Runtime snapshots execute the built-in `runtime.celestial.kepler` system before project-authored gameplay systems.

## Physics and Interaction

Rekall AGE provides generic physics authoring components:

- `Rekall.PhysicsWorld3D`
- `Rekall.PhysicsMaterial3D`
- `Rekall.Rigidbody3D`
- `Rekall.BoxCollider2D`
- `Rekall.CircleCollider2D`
- `Rekall.BoxCollider3D`
- `Rekall.SphereCollider3D`
- `Rekall.CapsuleCollider3D`
- `Rekall.MeshCollider`
- `Rekall.Trigger`

The engine uses physics to produce generic facts and query results. Project modules consume those facts for gameplay. This keeps damage, interaction, inventory, puzzle rules, sensors, and mission logic out of engine core.

## Multiplayer

Multiplayer is component-driven and inspectable.

Core components:

- `Rekall.MultiplayerSession`
- `Rekall.NetworkIdentity`
- `Rekall.NetworkTransform`

Runtime helpers let project modules query sessions, network entities, ownership, replication flags, prediction mode, and priority. Authoritative snapshots preserve:

- entity id
- network id
- owner client id
- authority
- position/rotation/scale values
- replicate-position/rotation/scale flags
- prediction mode
- priority

Snapshot utilities:

- `RekallAgeMultiplayerSnapshotInterpolator`
- `RekallAgeMultiplayerClientReconciler`
- `RekallAgeMultiplayerSnapshotApplier`
- `RekallAgeMultiplayerSnapshotDeltaBuilder`

CLI commands:

```powershell
dotnet run --project src/Rekall.Age.Cli -- multiplayer host .age-sandbox Main 30
dotnet run --project src/Rekall.Age.Cli -- multiplayer status .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- multiplayer connect .age-sandbox Main client-a "Client A"
dotnet run --project src/Rekall.Age.Cli -- multiplayer input .age-sandbox Main client-a 1 ship-1 '{"moveX":1}'
dotnet run --project src/Rekall.Age.Cli -- multiplayer tick .age-sandbox Main 1
dotnet run --project src/Rekall.Age.Cli -- multiplayer snapshot .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- multiplayer delta .age-sandbox Main 0
```

The current runtime provides a tested server-authoritative substrate with local pipe and WebSocket command transports. Production-grade UDP, matchmaking, NAT traversal, interest management, and continuous client snapshot streaming are future layers over the same generic contracts.

## Live Player Editing

The Windows player starts a local named-pipe live-edit server for the loaded project and scene. Agents can target a running player without restarting it.

Live MCP commands:

- `rekall.live.status`
- `rekall.live.apply_scene_blueprint`
- `rekall.live.apply_scene_diff`
- `rekall.live.reload_scene`
- `rekall.live.reload_assets`

Live editing can:

- inspect session id, pipe name, frame index, entity count, renderable count, scene revision, and asset revision
- apply generic entity/component blueprints
- apply upsert/delete/clear scene diffs
- persist changes to scene storage
- reload runtime assets
- refresh player-side GPU texture/material bindings

Mutations are queued onto the player render thread so runtime-world swaps and GPU resource replacement are serialized with rendering.

## Starter Templates and Packaging Workflows

Starter template ids:

- `pong`
- `breakout`
- `asteroids`
- `top-down-shooter`
- `platformer-2d`
- `tower-defense`
- `visual-novel`
- `first-person-exploration`
- `collectathon-3d`
- `puzzle`

Each template creates a project manifest, `Main` scene, active camera, starter render primitives, and template-owned example entities/components under `Game.Templates.*`. Game behavior belongs in project-authored modules.

Workflow commands:

```powershell
dotnet run --project src/Rekall.Age.Cli -- game create .age-sandbox "Crystal Mines" pong
dotnet run --project src/Rekall.Age.Cli -- game create-playable .age-sandbox "Playable Pong" pong
dotnet run --project src/Rekall.Age.Cli -- game create-package-playable .age-sandbox "Packaged Pong" pong .age-sandbox/Builds/RekallAgePlayer .age-sandbox/Artifacts/PackageFrames
dotnet run --project src/Rekall.Age.Cli -- game verify-playable .age-sandbox Main 2
dotnet run --project src/Rekall.Age.Cli -- game package-playable .age-sandbox Main .age-sandbox/Builds/RekallAgePlayer
dotnet run --project src/Rekall.Age.Cli -- game inspect-package .age-sandbox/Builds/RekallAgePlayer.zip
dotnet run --project src/Rekall.Age.Cli -- game run-package .age-sandbox/Builds/RekallAgePlayer.zip 2
dotnet run --project src/Rekall.Age.Cli -- game capture-package-frame .age-sandbox/Builds/RekallAgePlayer.zip .age-sandbox/Artifacts/PackageFrames 1
dotnet run --project src/Rekall.Age.Cli -- game audit-package .age-sandbox/Builds/RekallAgePlayer.zip .age-sandbox/Artifacts/PackageAudit
```

The preferred closed loop is `rekall.workflow.agent_authoring_gauntlet` / `game gauntlet`: create, verify, package, audit, capture a proof frame, and report next actions.

## Studio and Workbench Foundation

Studio is the desktop workbench shell:

```powershell
dotnet run --project src/Rekall.Age.Studio -- --project .age-sandbox --scene Main
```

The current workbench foundation includes editor-facing read models, level-design workflows, asset pipeline reports, validation status, and startup/error logging. Studio logs are written under `%LOCALAPPDATA%\Rekall AGE\Studio\Logs`.

Workbench and level design commands:

```powershell
dotnet run --project src/Rekall.Age.Cli -- studio open .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- level entity duplicate .age-sandbox Main <entity-id> "Player Copy"
dotnet run --project src/Rekall.Age.Cli -- level entity parent .age-sandbox Main <entity-id> <parent-id>
dotnet run --project src/Rekall.Age.Cli -- level prefab create .age-sandbox Main <entity-id> PlayerPrefab
dotnet run --project src/Rekall.Age.Cli -- level prefab instantiate .age-sandbox Main <prefab-id> "Prefab Player"
dotnet run --project src/Rekall.Age.Cli -- level entity snap .age-sandbox Main <entity-id> 1
```

## Testing and Verification

Run all tests:

```powershell
dotnet test Rekall.AGE.sln
```

Useful focused runs:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter VirtualGeometry
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~Rendering|FullyQualifiedName~Cli"
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~Runtime"
```

Recommended manual verification for rendering changes:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render vulkan probe
dotnet run --project src/Rekall.Age.Cli -- render viewport capture Examples\VulkanCubeProbe Main 0 .age-artifacts 640 360 vulkan
dotnet run --project src/Rekall.Age.Cli -- render performance budget Examples\VulkanCubeProbe Main desktop60 0 640 360
```

Recommended manual verification for agent-authored games:

```powershell
dotnet run --project src/Rekall.Age.Cli -- game gauntlet .age-sandbox "Gauntlet Pong" pong .age-sandbox/Builds/GauntletPong .age-sandbox/Artifacts/GauntletAudit
```

## Design Principles for Contributors

These rules are architectural, not stylistic:

- Prefer generic authoring primitives over genre-specific built-ins.
- Do not add a built-in runtime behavior if an agent can author it cleanly from existing primitives.
- Put game behavior in project modules, templates, or examples unless it is truly engine-general.
- Expose inspectable data, diagnostics, and SDK helpers before adding hidden behavior.
- Keep input generic: capture raw input, normalize it, project semantic actions, and let modules consume action helpers.
- Keep events generic: emit facts, do not execute genre behavior.
- Let modules emit custom facts through `EmitEvent` and `EmitBoundEvents`.
- Use generic query helpers rather than hard-coded entity classes or scene scans.
- Use generic mutation helpers rather than rebuilding world entity arrays manually.
- Emit structured observations when authored content is missing, inconsistent, or worth surfacing to an agent.
- Use engine frame time for realtime gameplay, animation, timers, cooldowns, and simulation.
- Use camera/vector SDK helpers instead of guessing Euler signs or rebuilding basis math.
- Preserve generic replication metadata in multiplayer snapshots.
- Prefer the closed-loop gauntlet when proving a playable user-facing path.
- When visual output is weak, improve diagnostics and authored scene data before adding a showcase-specific engine path.

Rekall AGE should feel like an engine that AI agents can understand, inspect, repair, extend, and use to make arbitrary games.
