# Rekall AGE

Rekall AGE is the Rekall Agentic Game Engine: a C#/.NET engine for humans and AI agents to author arbitrary games through inspectable, generic, composable engine contracts.

The core idea is simple and strict: Rekall AGE is not a genre engine. It does not provide a privileged controller, combat loop, camera loop, level loop, or rules model. The engine exposes authoring primitives, diagnostics, runtime contracts, rendering infrastructure, networking primitives, package workflows, and MCP/CLI tools. Agents and users author the actual game behavior in project data and project modules.

This README is the broad public entry point and the technical reference for the repository.

## Contents

- [Engine Philosophy](#engine-philosophy)
- [Current Status](#current-status)
- [Quick Start](#quick-start)
- [Repository Map](#repository-map)
- [Mental Model](#mental-model)
- [Project And Scene Files](#project-and-scene-files)
- [Command Bus](#command-bus)
- [Transactions](#transactions)
- [MCP Server](#mcp-server)
- [CLI Command Surface](#cli-command-surface)
- [Agent Authoring Guide](#agent-authoring-guide)
- [Runtime Modules](#runtime-modules)
- [Runtime SDK Helpers](#runtime-sdk-helpers)
- [Built-In Components](#built-in-components)
- [Input System](#input-system)
- [Runtime Events](#runtime-events)
- [Timers](#timers)
- [Pointer Rays And Picking](#pointer-rays-and-picking)
- [Physics And Interaction](#physics-and-interaction)
- [Rendering Architecture](#rendering-architecture)
- [Software Viewport](#software-viewport)
- [Vulkan Renderer](#vulkan-renderer)
- [Render Plans](#render-plans)
- [Materials And Textures](#materials-and-textures)
- [Shaders](#shaders)
- [Geometry Authoring](#geometry-authoring)
- [GLB Import And Export](#glb-import-and-export)
- [Render Layers And Visibility](#render-layers-and-visibility)
- [Performance Budgets](#performance-budgets)
- [Virtual Geometry](#virtual-geometry)
- [Planet And Solar-System Rendering](#planet-and-solar-system-rendering)
- [VR And OpenXR](#vr-and-openxr)
- [Multiplayer](#multiplayer)
- [Live Player Editing](#live-player-editing)
- [Playable Verification And Packaging](#playable-verification-and-packaging)
- [Studio Workbench](#studio-workbench)
- [Testing](#testing)
- [Contributor Rules](#contributor-rules)

## Engine Philosophy

Rekall AGE is agent-first because every meaningful engine surface is designed to be read, queried, mutated, validated, and verified by an AI agent.

The engine should provide:

- deterministic project and scene files
- typed command contracts
- machine-readable command results
- transaction history
- component schema discovery
- runtime frame inspection
- viewport diagnostics
- validation reports
- structured observations
- build and package proof workflows
- generic runtime facts
- generic networking metadata
- generic rendering contracts

The engine should not provide:

- engine-authored game foundations
- genre-specific built-in gameplay loops
- default hard-coded player behavior
- engine-owned combat, inventory, puzzle, dialogue, mission, vehicle, or controller logic
- hidden renderer behavior that agents cannot inspect

Game behavior belongs in project scenes, project modules, examples, and user-authored content.

## Current Status

Rekall AGE is an active engine prototype with a substantial technical vertical slice.

Implemented foundations:

- .NET `net10.0` solution
- typed command bus
- CLI adapter
- MCP stdio JSON-RPC adapter
- deterministic project manifests
- deterministic scene, entity, and component documents
- transaction logging and preimage restore
- component schema discovery
- C# project module scaffolding, source editing, build, and runtime loading
- runtime snapshots
- generic input projection
- generic runtime event facts
- timer facts
- pointer ray facts
- 3D collider and trigger facts
- runtime observations
- software viewport capture
- Vulkan renderer path
- Vulkan low-level diagnostics
- shader authoring and validation
- GLB import reports and scene export
- generated primitives, meshes, recipes, and extrusions
- render layers and camera culling masks
- runtime viewport capture
- performance budget inspection
- CPU-side virtual geometry LOD
- KSA planet and solar-system import
- procedural planet, atmosphere, clouds, orbit, marker, label, and starfield renderables
- OpenXR probing, stereo planning, and headset frame planning
- generic multiplayer session, snapshot, delta, and reconciliation contracts
- live player editing over local IPC
- package verification, package inspection, package run, proof-frame capture, and package audit
- Windows graphics player
- WPF Studio workbench foundation

Important scope note:

- The current virtual geometry system is CPU-side clustered LOD for dense meshes in the Vulkan path. It is inspired by Nanite's goal of making dense scenes practical, but it is not yet GPU mesh-shader virtualized geometry with disk-page streaming and hierarchical occlusion.
- The renderer is Vulkan-first today. Backend-neutral render plans and abstraction boundaries keep room for other backends.
- Multiplayer is a generic authoritative-session foundation, not a finished matchmaking or internet transport product.
- VR has OpenXR diagnostics and headset output planning. Playable VR should run through the windowed player so desktop input and OpenXR input share the same runtime input stream.

## Quick Start

Prerequisites:

- .NET SDK capable of building `net10.0`
- Windows for the current desktop player and WPF Studio shell
- Vulkan-capable GPU and driver for native Vulkan rendering
- optional SteamVR/OpenXR runtime for headset testing

Build:

```powershell
dotnet build Rekall.AGE.sln
```

Test:

```powershell
dotnet test Rekall.AGE.sln
```

Inspect the engine:

```powershell
dotnet run --project src/Rekall.Age.Cli -- context engine
dotnet run --project src/Rekall.Age.Cli -- module schemas
```

Create a project and scene:

```powershell
dotnet run --project src/Rekall.Age.Cli -- project create .age-sandbox "Agentic Game" world,rendering3d
dotnet run --project src/Rekall.Age.Cli -- scene create .age-sandbox Main world,rendering3d
dotnet run --project src/Rekall.Age.Cli -- geometry primitive create .age-sandbox Main "First Cube" cube 0 0 0 "#8ab4f8"
dotnet run --project src/Rekall.Age.Cli -- validation scene .age-sandbox Main
```

Capture a viewport:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport 640 360 vulkan
```

Run the player:

```powershell
dotnet run --project src/Rekall.Age.Player -- .age-sandbox Main
dotnet run --project src/Rekall.Age.Player -- .age-sandbox Main --frames 2
```

Run the MCP server:

```powershell
dotnet run --project src/Rekall.Age.Cli -- mcp stdio
```

Open Studio:

```powershell
dotnet run --project src/Rekall.Age.Studio -- --project .age-sandbox --scene Main
```

## Repository Map

| Path | Responsibility |
| --- | --- |
| `src/Rekall.Age.Core` | command interfaces, command registry, command schemas, command results, transactions |
| `src/Rekall.Age.Project` | project manifest, project store, capabilities |
| `src/Rekall.Age.World` | scene documents, entities, components, blueprints, prefabs, hierarchy |
| `src/Rekall.Age.Validation` | scene validation and agent-readable next actions |
| `src/Rekall.Age.Agent` | engine status, project summaries, scene summaries |
| `src/Rekall.Age.Mcp` | MCP tool catalog and stdio JSON-RPC server |
| `src/Rekall.Age.Cli` | command-line adapter over the shared command registry |
| `src/Rekall.Age.Modules` | module attributes, built-in component schemas, playable module contracts, runtime SDK helpers |
| `src/Rekall.Age.Runtime.Abstractions` | runtime world, input, event, physics, render, and multiplayer contracts |
| `src/Rekall.Age.Runtime` | runtime snapshot builder, built-in runtime systems, multiplayer commands |
| `src/Rekall.Age.Playback` | deterministic scene play, playtest, ASCII frame output |
| `src/Rekall.Age.Rendering.Abstractions` | viewport frames, renderables, cameras, materials, virtual geometry, stereo data |
| `src/Rekall.Age.Rendering` | software renderer, Vulkan renderer, OpenXR planning, GLB export, virtual geometry |
| `src/Rekall.Age.Assets` | asset catalog and import commands |
| `src/Rekall.Age.AssetPipeline` | asset inspection and import reports |
| `src/Rekall.Age.LevelDesign` | geometry creation, prefabs, KSA planet and solar import, level utilities |
| `src/Rekall.Age.Build` | module and player build commands |
| `src/Rekall.Age.Workflows` | generic playable verification and package workflows |
| `src/Rekall.Age.Player` | player runtime |
| `src/Rekall.Age.Player.Windows` | SDL/Vulkan Windows player and live-edit host |
| `src/Rekall.Age.Editor.Contracts` | editor-facing read models |
| `src/Rekall.Age.Editor` | workbench model construction |
| `src/Rekall.Age.Studio` | WPF Studio shell |
| `tests/Rekall.Age.Tests` | regression, command, runtime, rendering, MCP, multiplayer, and workflow tests |
| `Examples` | sample projects and capture artifacts |

## Mental Model

Rekall AGE has four layers:

1. Authoring data: project files, scene files, entities, components, assets, modules, shaders.
2. Engine commands: typed mutations and inspections exposed through CLI and MCP.
3. Runtime projection: scene data plus compiled modules become runtime snapshots, event facts, observations, and viewport frames.
4. Verification: validation, runtime inspection, viewport capture, performance budgets, package inspection, package run, and package audit prove the authored result.

The same command names and result objects are used by humans and agents. The CLI is a human adapter. MCP is an agent adapter. The command registry is the contract.

## Project And Scene Files

A Rekall AGE project is intentionally ordinary on disk.

Typical project layout:

```text
rekall.project.json
Scenes/
  Main.age.scene.json
Assets/
  assets.age.catalog.json
Modules/
  GameRules/
    GameRules.csproj
    GameRulesModule.cs
Shaders/
Transactions/
Builds/
Artifacts/
```

Project manifest:

- stores project name
- stores schema version
- stores declared capabilities
- does not store engine-owned game origin metadata

Scene document:

- stores scene name
- stores scene capabilities
- stores entities
- stores components as JSON-backed typed records
- stores entity hierarchy
- stores tags and visibility

Entity document:

- id
- name
- tags
- transform
- parent/child relationships
- components
- visibility

Component document:

- type name such as `Rekall.Camera3D`
- JSON properties
- schema discovered through built-in or project module metadata

Core project commands:

```text
rekall.project.create
rekall.capability.add
rekall.context.project_summary
rekall.context.scene_summary
```

Core scene/entity commands:

```text
rekall.scene.create
rekall.scene.apply_blueprint
rekall.entity.create
rekall.entity.inspect
rekall.entity.delete
rekall.component.add
rekall.component.set_property
rekall.validation.scene
```

CLI examples:

```powershell
dotnet run --project src/Rekall.Age.Cli -- project create .age-sandbox "My Game" world,rendering3d
dotnet run --project src/Rekall.Age.Cli -- scene create .age-sandbox Main world,rendering3d
dotnet run --project src/Rekall.Age.Cli -- geometry primitive create .age-sandbox Main "First Cube" cube 0 0 0 "#8ab4f8"
dotnet run --project src/Rekall.Age.Cli -- validation scene .age-sandbox Main
```

## Command Bus

Commands implement `IRekallAgeCommand<TRequest, TResult>`.

Each command provides:

- stable command name
- description
- request type
- result type
- structured success result
- structured errors
- transaction resource changes when it mutates files

Command results are designed for agents:

- `Ok`
- `Summary`
- `Value`
- `Errors`
- transaction metadata through the active command context

Command schemas are discoverable through the registry and MCP tool list. This means agents can inspect available tools instead of relying on hidden documentation.

## Transactions

Mutating commands run inside `RekallAgeTransaction`.

Transactions record:

- transaction id
- transaction name
- actor
- timestamp
- changed resources
- preimage snapshots for restore

Transaction files live under:

```text
Transactions/transactions.age.json
Transactions/Snapshots/<transaction-id>/
```

Commands:

```text
rekall.transaction.history
rekall.transaction.restore_preimage
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- transaction history .age-sandbox 20
dotnet run --project src/Rekall.Age.Cli -- transaction restore-preimage .age-sandbox <transactionId> Scenes/Main.age.scene.json
```

## MCP Server

Rekall AGE ships a stdio MCP JSON-RPC adapter in `src/Rekall.Age.Mcp`, launched by the CLI.

Run:

```powershell
dotnet run --project src/Rekall.Age.Cli -- mcp stdio
```

MCP provides:

- `initialize`
- `tools/list`
- `tools/call`
- JSON schema generation for command request records
- tool metadata such as category, recommendation, and agent priority
- structured command results
- clean JSON-RPC stdout
- diagnostic logging outside stdout

MCP log locations:

- default: `%LOCALAPPDATA%\Rekall AGE\Mcp\Logs`
- override: `REKALL_AGE_MCP_LOG_DIR`
- shared override: `REKALL_AGE_LOG_DIR`

Tool categories:

| Prefix | Category |
| --- | --- |
| `rekall.context.*` | context |
| `rekall.workflow.*` | workflow |
| `rekall.transaction.*` | transactions |
| `rekall.render.*` | rendering |
| `rekall.shader.*` | shaders |
| `rekall.module.*` | modules |
| `rekall.live.*` | live |
| `rekall.multiplayer.*` | multiplayer |
| `rekall.play*` | playtesting |
| `rekall.asset.*` | assets |
| `rekall.project.*`, `rekall.scene.*`, `rekall.entity.*`, `rekall.component.*`, `rekall.geometry.*`, `rekall.planet.*`, `rekall.solar.*` | world |

Recommended agent entry points:

```text
rekall.context.engine_status
rekall.context.project_summary
rekall.context.scene_summary
rekall.module.component_schemas
rekall.module.scaffold_runtime_system
rekall.module.read_source
rekall.module.write_source
rekall.build.modules
rekall.validation.scene
rekall.runtime.inspect_scene
rekall.render.capture_runtime_viewport
rekall.render.performance.inspect_scene_budget
rekall.render.visibility.inspect_scene
rekall.render.virtual_geometry.inspect_scene
rekall.workflow.verify_playable_game
rekall.workflow.package_playable_game
rekall.workflow.audit_playable_package
```

## CLI Command Surface

The CLI is a thin adapter over the shared command registry. It is useful for manual testing, CI, and examples.

Major command groups:

```text
project
capability
scene
entity
component
asset
geometry
level
studio
play
playtest
run
runtime
multiplayer
context
transaction
capture
render
module
build
validation
game
mcp
```

The `game` group is for generic playable verification and packaging of the user's authored project. It does not create games for the user.

Useful CLI examples:

```powershell
dotnet run --project src/Rekall.Age.Cli -- context engine
dotnet run --project src/Rekall.Age.Cli -- module schemas
dotnet run --project src/Rekall.Age.Cli -- module schemas project .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- validation scene .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- runtime inspect .age-sandbox Main 3
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport 640 360 vulkan
dotnet run --project src/Rekall.Age.Cli -- game verify-playable .age-sandbox Main 2
dotnet run --project src/Rekall.Age.Cli -- game package-playable .age-sandbox Main .age-sandbox/Builds/RekallAgePlayer
```

## Agent Authoring Guide

This is the recommended loop for AI agents using Rekall AGE.

### 1. Inspect

Start by reading engine status, project state, scene state, component schemas, and module sources.

```powershell
dotnet run --project src/Rekall.Age.Cli -- context engine
dotnet run --project src/Rekall.Age.Cli -- context summary .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- context scene .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- module schemas
dotnet run --project src/Rekall.Age.Cli -- module sources .age-sandbox
```

### 2. Author Generic Data

Use entities, components, tags, render layers, semantic input maps, colliders, triggers, cameras, renderers, network identities, and project modules. Do not rely on engine-owned game behavior.

### 3. Put Rules In Project Modules

Create a runtime-system module:

```powershell
dotnet run --project src/Rekall.Age.Cli -- module scaffold-runtime-system .age-sandbox game.rules "Game Rules" GameRules GameState GameRulesSystem
```

Read and edit source:

```powershell
dotnet run --project src/Rekall.Age.Cli -- module read-source .age-sandbox GameRules GameRulesModule.cs
dotnet run --project src/Rekall.Age.Cli -- module write-source .age-sandbox GameRules GameRulesModule.cs .\GameRulesModule.cs
dotnet run --project src/Rekall.Age.Cli -- build modules .age-sandbox
```

### 4. Emit Observations

Project modules should emit observations when authored content is missing, inconsistent, or worth surfacing.

Use observations instead of silent failure when:

- an expected entity is missing
- a component has invalid data
- a camera sees nothing
- a tag query returns no targets
- a network authority assumption is not true
- a required asset is missing

### 5. Verify

Use closed-loop checks:

```powershell
dotnet run --project src/Rekall.Age.Cli -- validation scene .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- runtime inspect .age-sandbox Main 3
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport 640 360 vulkan
dotnet run --project src/Rekall.Age.Cli -- render performance budget .age-sandbox Main desktop60 0 1920 1080
dotnet run --project src/Rekall.Age.Cli -- game package-playable .age-sandbox Main .age-sandbox/Builds/RekallAgePlayer
dotnet run --project src/Rekall.Age.Cli -- game audit-package .age-sandbox/Builds/RekallAgePlayer.zip .age-sandbox/Artifacts/PackageAudit
```

### 6. Iterate Narrowly

Prefer:

- component updates over whole-scene replacement
- blueprints over repeated manual edits
- module source edits over engine changes
- diagnostics before assumptions
- validation and captures after mutation

## Runtime Modules

Project modules are C# assemblies under `Modules/<ModuleName>`.

Module attributes:

```csharp
[RekallAgeModule("game.rules", "Game Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class GameRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
    }
}
```

Runtime systems implement `IRekallAgeRuntimeModuleSystem`.

Playable modules implement `IRekallAgePlayableModule`.

Scaffold commands:

```text
rekall.module.scaffold
rekall.module.scaffold_playable
rekall.module.scaffold_runtime_system
```

Source commands:

```text
rekall.module.list_sources
rekall.module.read_source
rekall.module.write_source
rekall.build.modules
```

The playable scaffold is intentionally neutral. It creates an editable shell, not a game design.

## Runtime SDK Helpers

Runtime modules should use SDK helpers rather than manually scanning or rebuilding world state.

Query helpers:

```text
FindEntity
EntitiesNamed
EntitiesWithTag
EntitiesWithComponent
EntitiesWithTagAndComponent
```

Mutation helpers:

```text
ReplaceEntity
UpdateEntity
UpdateEntitiesWithTag
UpdateEntitiesWithComponent
UpdateEntitiesWithTagAndComponent
WithTag
WithoutTag
WithVisible
```

Event and observation helpers:

```text
EmitEvent
EmitBoundEvents
EmitObservation
EmitSceneObservation
```

Camera/vector helpers:

```text
Forward3D
Right3D
Up3D
Offset3D
```

Input helpers:

```text
InputActionValue
IsInputActionDown
WasInputActionPressed
```

Multiplayer helpers:

```text
NetworkSessions
PrimaryNetworkSession
NetworkEntities
NetworkEntityForEntity
NetworkEntityByNetworkId
NetworkEntitiesOwnedBy
RuntimeEntitiesOwnedBy
ReplicatedRuntimeEntities
IsNetworkOwner
IsReplicated
```

## Built-In Components

Built-in component schemas come from `rekall.builtins`.

Common world components:

- `Rekall.Transform2D`
- `Rekall.Transform3D`
- `Rekall.Camera2D`
- `Rekall.Camera3D`
- `Rekall.RenderLayer`
- `Rekall.Visible`

Rendering components:

- `Rekall.SpriteRenderer`
- `Rekall.MeshRenderer`
- `Rekall.LineRenderer`
- `Rekall.GeometryMesh`
- `Rekall.GeometryPrimitive`
- `Rekall.ProceduralMaterial`
- `Rekall.LodGroup`
- `Rekall.VirtualGeometry`
- `Rekall.RenderShaderPipeline`

Input and interaction components:

- `Rekall.InputActionMap`
- `Rekall.PointerRay`
- `Rekall.Collider3D`
- `Rekall.Trigger`
- `Rekall.Timer`

Physics components:

- `Rekall.PhysicsBody3D`
- `Rekall.PhysicsState3D`
- `Rekall.Collider3D`
- `Rekall.Trigger`

Planet and astronomy components:

- `Rekall.PlanetRenderer`
- `Rekall.PlanetAtmosphere`
- `Rekall.PlanetCloudLayer`
- `Rekall.PlanetRing`
- `Rekall.CelestialBody`
- `Rekall.KeplerOrbit`
- `Rekall.OrbitPath`

Multiplayer components:

- `Rekall.MultiplayerSession`
- `Rekall.NetworkIdentity`
- `Rekall.NetworkTransform`
- `Rekall.NetworkInput`

Inspect schemas:

```powershell
dotnet run --project src/Rekall.Age.Cli -- module schemas
dotnet run --project src/Rekall.Age.Cli -- module schemas project .age-sandbox
```

## Input System

The input system is generic.

It captures:

- keyboard
- mouse buttons
- mouse position
- mouse delta
- mouse wheel
- OpenXR poses/actions when VR is active

`Rekall.InputActionMap` projects raw input into semantic actions. Project modules consume semantic actions, not hard-coded key folklore.

Example concepts:

- action id
- input binding
- action value
- button state
- pressed this frame
- released this frame
- analog axis

Runtime code should use:

```text
InputActionValue
IsInputActionDown
WasInputActionPressed
```

VR rule:

Playable VR still keeps a desktop player window. Keyboard and mouse input come from that window and are bridged into the same runtime input stream as OpenXR data.

## Runtime Events

The runtime emits generic facts. Project modules decide what they mean.

Core lifecycle facts:

```text
entity.begin
entity.tick
```

Timer facts:

```text
timer.elapsed
```

Pointer facts:

```text
pointer.enter
pointer.leave
pointer.hit
pointer.down
pointer.up
pointer.click
```

Physics facts:

```text
collision.begin
collision.stay
collision.end
trigger.enter
trigger.stay
trigger.exit
```

Project modules can emit custom event facts through SDK helpers. The engine does not need every useful game event to become a built-in.

## Timers

Timers are generic runtime facts.

Typical use:

- cooldowns
- delayed triggers
- animation beats
- spawn intervals
- temporary states
- UI prompts

Timer behavior should use runtime delta time. Do not tie authored gameplay speed to render frame count.

## Pointer Rays And Picking

Pointer rays provide generic interaction information.

They can express:

- ray origin
- ray direction
- hit entity
- hit point
- enter/leave state
- down/up/click facts

Project modules decide what happens when a pointer hits something. The engine only supplies the fact.

## Physics And Interaction

Physics is a generic interaction substrate, not game logic.

Core components:

- `Rekall.PhysicsBody3D`
- `Rekall.PhysicsState3D`
- `Rekall.Collider3D`
- `Rekall.Trigger`

The runtime can produce:

- collision facts
- trigger facts
- physics state updates
- ray query results
- overlap-style interaction facts

Project modules should use these facts for authored behavior:

- pickups
- doors
- damage
- sensors
- zones
- prompts
- objectives
- scripted interactions

The engine should not know what a collision means for a particular game.

## Rendering Architecture

Rendering starts from `RekallAgeRuntimeViewportFrame`.

A viewport frame contains:

- frame index
- elapsed time
- active camera
- all cameras
- renderables
- render layers
- culling diagnostics
- bounds diagnostics
- observations
- stereo data
- material data
- virtual geometry metadata

Renderables are backend-neutral records. The software renderer, Vulkan renderer, GLB exporter, performance budget inspector, visibility inspector, OpenXR planner, and virtual geometry diagnostics all consume this frame contract.

Renderable families:

- sprites
- meshes
- generated primitives
- custom geometry meshes
- line segments
- runtime render meshes
- GLB meshes
- planet renderables
- atmosphere renderables
- cloud renderables
- orbit lines
- markers
- labels
- starfields

Viewport capture:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport 640 360 software
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport 640 360 vulkan
```

## Software Viewport

The software viewport is useful for deterministic diagnostics, CI, and fallback captures.

It supports:

- simple renderable rasterization
- non-blank checks
- frame metadata
- active camera reporting
- viewport image writing
- proof captures for tests

It is not the target high-performance renderer. It exists so agents can still see and prove authored output without requiring native graphics.

## Vulkan Renderer

The Vulkan path is the primary native renderer.

Code areas:

- `src/Rekall.Age.Rendering/RekallAgeVulkan*`
- `src/Rekall.Age.Rendering/Shaders`
- `src/Rekall.Age.Rendering/RekallAgeVulkanScene*`

Vulkan diagnostics include:

```text
rekall.render.vulkan.probe
rekall.render.vulkan.device.bootstrap
rekall.render.vulkan.command_buffer.submit_empty
rekall.render.vulkan.buffer.create_mapped
rekall.render.vulkan.image.create_bound
rekall.render.vulkan.render_target.create
rekall.render.vulkan.render_pass.submit_clear
rekall.render.vulkan.render_pass.read_clear
rekall.render.vulkan.render_pass.capture_clear
```

CLI examples:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render vulkan probe
dotnet run --project src/Rekall.Age.Cli -- render vulkan device bootstrap discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan command-buffer submit-empty discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan render-pass capture-clear 64 64 R8G8B8A8_UNorm discrete-gpu .age-sandbox/Artifacts/Vulkan 0.2 0.4 0.8 1
```

Vulkan scene rendering handles:

- camera matrices
- frame uniform data
- draw push constants
- vertex buffers
- index buffers
- texture descriptors
- PBR material channels
- generated mesh batches
- GLB mesh batches
- planet shader uniforms
- atmosphere/cloud/ring material data
- offscreen capture targets
- OpenXR stereo swapchain targets

Vulkan prepared frame stages:

1. build runtime viewport frame
2. build Vulkan scene meshes
3. batch renderables
4. build draw plan
5. build geometry upload
6. build uniform upload
7. choose render target
8. record command buffer
9. submit
10. optionally read back/capture

## Render Plans

Render plans are backend-neutral command-buffer descriptions.

Commands:

```text
rekall.render.plan.create
rekall.render.resource.add
rekall.render.command_buffer.record
rekall.render.plan.inspect
rekall.render.plan.validate
rekall.render.plan.execute
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render plan create .age-sandbox vulkan NativePreview
dotnet run --project src/Rekall.Age.Cli -- render resource add .age-sandbox frame-color image R8G8B8A8_UNorm color-attachment,transfer-src
dotnet run --project src/Rekall.Age.Cli -- render command-buffer record .age-sandbox main graphics '[{"op":"begin-render-pass","label":"frame","arguments":{"target":"frame-color","width":"64","height":"64","preferredDeviceType":"discrete-gpu"}},{"op":"clear","label":"clear","arguments":{"r":"0.1","g":"0.2","b":"0.3","a":"1"}},{"op":"end-render-pass","label":"frame","arguments":{}}]'
dotnet run --project src/Rekall.Age.Cli -- render plan validate .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- render plan execute .age-sandbox .age-sandbox/Artifacts/Render
```

Render plans are useful when agents need to reason about GPU resources and command submission without hand-writing backend code.

## Materials And Textures

Renderable materials can include:

- base color
- texture asset id
- metallic value
- roughness value
- normal texture
- occlusion texture
- emissive texture/color
- alpha mode
- double-sided mode
- sampler mode
- shader pipeline id

Texture support includes imported images and GPU-compressed KTX2 texture metadata where available. Asset import reports expose dimensions, format, mip levels, and Vulkan format information.

Procedural materials:

- deterministic seed
- base color generation
- normal map generation
- metallic/roughness generation
- emissive generation
- repeatable texture output for agents

## Shaders

Project shaders live under project shader storage and are exposed through commands:

```text
rekall.shader.list
rekall.shader.read
rekall.shader.write
rekall.shader.validate
rekall.shader.assign_pipeline
```

Runtime render meshes can reference `RekallAgeRuntimeRenderShaderPipeline`.

Shader validation uses the Vulkan shader compiler path so agents can catch shader errors before runtime.

These shader commands are currently part of the command bus and MCP catalog. They are intended for agent use through MCP `tools/call`; dedicated CLI route aliases can be added later without changing the underlying command contracts.

## Geometry Authoring

Geometry commands:

```text
rekall.geometry.create_primitive
rekall.geometry.create_mesh
rekall.geometry.create_recipe
rekall.geometry.create_extrusion
```

Level-design commands:

```text
rekall.level.entity.duplicate
rekall.level.entity.parent
rekall.level.entity.snap_to_grid
rekall.level.prefab.create_from_entity
rekall.level.prefab.instantiate
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- geometry primitive create .age-sandbox Main "Cube" cube 0 0 0 "#8ab4f8"
dotnet run --project src/Rekall.Age.Cli -- geometry mesh create .age-sandbox Main "Triangle" '[[0,0,0],[1,0,0],[0,1,0]]' '[0,1,2]' 0 0 0 "#8ab4f8"
dotnet run --project src/Rekall.Age.Cli -- geometry extrusion create .age-sandbox Main "Wall" '[[0,0],[2,0],[2,1],[0,1]]' 2.5 0 0 0 "#cccccc"
dotnet run --project src/Rekall.Age.Cli -- level entity duplicate .age-sandbox Main <entityId> CubeCopy
```

Geometry is regular scene data. Agents can inspect it, mutate it, apply render layers, assign materials, and enable virtual geometry.

## GLB Import And Export

Asset import:

```text
rekall.asset.import
rekall.asset.import_report
rekall.asset.list
```

GLB import reports expose:

- mesh count
- node count
- material count
- texture metadata
- primitive topology
- triangle counts
- asset warnings

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- asset import-report .age-sandbox .\robot.glb model "Robot"
dotnet run --project src/Rekall.Age.Cli -- asset import .age-sandbox .\robot.glb model "Robot"
dotnet run --project src/Rekall.Age.Cli -- asset list .age-sandbox
```

Scene export:

```text
rekall.render.export_scene_glb
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render glb export .age-sandbox Main .age-sandbox/Artifacts/Main.glb 0
```

## Render Layers And Visibility

`Rekall.RenderLayer` lets renderables opt into named layers.

Camera components include culling masks:

- `Rekall.Camera2D`
- `Rekall.Camera3D`

Visibility inspection reports:

- active camera
- camera culling masks
- layer membership
- hidden renderables
- culling reason
- renderable counts
- mesh counts
- draw counts
- triangle counts

Command:

```text
rekall.render.visibility.inspect_scene
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render visibility inspect .age-sandbox Main 0
```

Use this when an authored entity exists but does not appear in a capture.

## Performance Budgets

Performance inspection compares a projected scene against named profiles.

Command:

```text
rekall.render.performance.inspect_scene_budget
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render performance budget .age-sandbox Main desktop60 0 1920 1080
dotnet run --project src/Rekall.Age.Cli -- render performance budget .age-sandbox Main vr90 0 1920 1080
```

Budget reports include:

- renderables
- meshes
- draw calls
- triangles
- vertices
- texture pressure
- render-target pixels
- stereo multiplier
- layer breakdown
- camera breakdown
- virtual geometry reductions
- warnings
- next actions

Agents should run this before assuming rendering is performant.

## Virtual Geometry

Virtual geometry is Rekall AGE's dense-mesh performance path.

Current implementation:

- CPU-side clustered LOD
- integrated into the Vulkan scene mesh path
- component controlled through `Rekall.VirtualGeometry`
- inspectable through budget and virtual-geometry diagnostics
- applied by command to dense renderables
- useful for imported/generated high-triangle scenes, including detailed planet scenes

It does:

- estimate source triangle pressure
- group source geometry
- select an appropriate reduced payload
- preserve inspectable source/reduced counts
- reduce submitted vertices/indices
- report selected LOD level

It does not yet do:

- GPU mesh shaders
- GPU task shaders
- disk-page streaming
- full hierarchical occlusion
- Nanite-compatible compressed cluster pages

Commands:

```text
rekall.render.virtual_geometry.inspect_scene
rekall.render.virtual_geometry.apply_scene
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render virtual-geometry inspect .age-sandbox Main 0 1920 1080
dotnet run --project src/Rekall.Age.Cli -- render virtual-geometry apply .age-sandbox Main 10000 --dry-run
dotnet run --project src/Rekall.Age.Cli -- render virtual-geometry apply .age-sandbox Main 10000
dotnet run --project src/Rekall.Age.Cli -- render virtual-geometry apply-entity .age-sandbox Main Earth 30000 --dry-run
dotnet run --project src/Rekall.Age.Cli -- render virtual-geometry apply-entity .age-sandbox Main Earth 30000
```

Detailed planet scenes benefit because generated or imported dense planet renderables can receive `Rekall.VirtualGeometry` and reduce the triangle payload submitted by the current Vulkan path.

Future architecture direction:

- GPU-first cluster building
- persistent cluster cache
- meshlet/cluster bounds
- screen-space error selection
- occlusion-aware hierarchy
- streaming pages
- Vulkan mesh-shader path where supported
- fallback compute/indirect path where mesh shaders are unavailable

The agent-facing contract should remain stable: dense geometry is controlled by scene components and exposed through diagnostics.

## Planet And Solar-System Rendering

Planet support lives mainly in `src/Rekall.Age.LevelDesign`, `src/Rekall.Age.Runtime`, and `src/Rekall.Age.Rendering`.

Commands:

```text
rekall.planet.import_ksa
rekall.solar.import_ksa_system
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- planet import-ksa .age-sandbox Planets <ksaRoot> Earth Earth
dotnet run --project src/Rekall.Age.Cli -- solar import-ksa-system .age-sandbox SolarSystem <ksaRoot>
dotnet run --project src/Rekall.Age.Cli -- solar import-ksa-system .age-sandbox SolarSystem <ksaRoot> SolSystemDense.xml 0.000001 0.00002
```

The importer handles:

- KSA astronomical XML
- `LoadFromLibrary` resolution
- celestial body data
- Kepler orbit data
- planet radius/mass/rotation data
- diffuse texture references where available
- generated renderable planet entities
- orbit path entities
- labels and markers
- scaled solar-system distances
- moon orbits relative to parent bodies
- automatic dense-renderable virtual geometry where appropriate

Planet renderable families:

- surface sphere
- atmosphere shell
- cloud shell
- rings
- orbit paths
- starfield
- markers
- labels
- halos

Shader data includes:

- planet radius
- atmosphere radius
- surface color
- cloud color
- light direction
- texture bindings
- atmospheric scattering parameters
- shadowing hints

Recommended planet workflow:

1. Import a body or system.
2. Validate scene.
3. Inspect performance budget.
4. Inspect virtual geometry.
5. Capture Vulkan viewport.
6. Apply entity-scoped virtual geometry if needed.
7. Re-capture and compare triangle counts.

## VR And OpenXR

OpenXR support is diagnostic and planning-heavy, with a Vulkan headset-output path under development.

Commands:

```text
rekall.render.openxr.probe
rekall.render.openxr.bootstrap_session
rekall.render.openxr.inspect_headset_frame_plan
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render openxr probe
dotnet run --project src/Rekall.Age.Cli -- render openxr bootstrap-session
dotnet run --project src/Rekall.Age.Cli -- render openxr frame-plan .age-sandbox Main 0 1920 1080
```

Stereo planning:

```text
rekall.render.stereo.inspect_plan
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render stereo inspect .age-sandbox Main 0 1920 1080
```

OpenXR diagnostics report:

- active runtime availability
- HMD system availability
- view configuration
- primary stereo views
- Vulkan graphics requirements
- `XR_KHR_vulkan_enable2`
- swapchain expectations
- frame-loop requirements
- blocker messages
- next actions

Playable VR rule:

Use the windowed player for playable sessions. It keeps SDL keyboard/mouse input and OpenXR poses/actions in one generic runtime input stream.

Local SteamVR note:

```powershell
$env:PATH = 'C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64;' + $env:PATH
dotnet run --project src\Rekall.Age.Cli -- render openxr bootstrap-session
```

Windowed player example:

```powershell
Rekall.Age.Player.Windows.exe <projectRoot> <sceneName> --graphics --backend vulkan --vr
```

Eye size can be tuned:

```powershell
Rekall.Age.Player.Windows.exe <projectRoot> <sceneName> --graphics --backend vulkan --vr --vr-eye-width 1280 --vr-eye-height 1280
```

The direct OpenXR submitter remains a diagnostic tool:

```powershell
dotnet run --project src\Rekall.Age.Cli -- render openxr submit-scene <projectRoot> <sceneName> 0 0 0
```

## Multiplayer

Multiplayer is generic and metadata-driven.

Core concepts:

- session
- client
- owner
- network entity
- network id
- authority
- replicate flag
- prediction mode
- replication priority
- snapshot
- delta
- reconciliation
- interpolation

Components:

- `Rekall.MultiplayerSession`
- `Rekall.NetworkIdentity`
- `Rekall.NetworkTransform`
- `Rekall.NetworkInput`

Runtime helpers:

```text
NetworkSessions
PrimaryNetworkSession
NetworkEntities
NetworkEntityForEntity
NetworkEntityByNetworkId
NetworkEntitiesOwnedBy
RuntimeEntitiesOwnedBy
ReplicatedRuntimeEntities
IsNetworkOwner
IsReplicated
```

Snapshot utilities:

```text
RekallAgeMultiplayerSnapshotInterpolator
RekallAgeMultiplayerClientReconciler
RekallAgeMultiplayerSnapshotApplier
RekallAgeMultiplayerSnapshotDeltaBuilder
```

Commands:

```text
rekall.multiplayer.host
rekall.multiplayer.status
rekall.multiplayer.connect
rekall.multiplayer.disconnect
rekall.multiplayer.submit_input
rekall.multiplayer.tick
rekall.multiplayer.snapshot
rekall.multiplayer.delta
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- multiplayer host .age-sandbox Main 30
dotnet run --project src/Rekall.Age.Cli -- multiplayer status .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- multiplayer connect .age-sandbox Main client-a "Client A"
dotnet run --project src/Rekall.Age.Cli -- multiplayer input .age-sandbox Main client-a 1 network-entity-1 '{"moveX":1}'
dotnet run --project src/Rekall.Age.Cli -- multiplayer tick .age-sandbox Main 1
dotnet run --project src/Rekall.Age.Cli -- multiplayer snapshot .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- multiplayer delta .age-sandbox Main 0
```

Authoritative snapshots preserve:

- server tick
- session id
- client id
- entity id
- network id
- owner id
- authority
- replicate flag
- prediction mode
- priority
- transform state
- component payloads

Agents should not hard-code networking around one controller model. Project modules use ownership and replication metadata to decide behavior.

## Live Player Editing

The Windows player exposes a local live-edit surface.

Commands:

```text
rekall.live.status
rekall.live.reload_scene
rekall.live.reload_assets
rekall.live.apply_scene_blueprint
rekall.live.apply_scene_diff
```

Live status reports:

- session id
- pipe name
- frame index
- entity count
- renderable count
- scene revision
- asset revision
- graphics backend

Live edits support:

- scene reload
- asset reload
- generic blueprint application
- generic scene diff application

Mutations are queued onto the player render thread so runtime swaps and graphics resource replacement stay serialized with rendering.

## Playable Verification And Packaging

Generic playable workflows live in `src/Rekall.Age.Workflows`.

They verify and package an authored project. They do not create a game for the user.

Commands:

```text
rekall.workflow.verify_playable_game
rekall.workflow.package_playable_game
rekall.workflow.inspect_playable_package
rekall.workflow.run_playable_package
rekall.workflow.capture_playable_package_frame
rekall.workflow.audit_playable_package
```

CLI:

```powershell
dotnet run --project src/Rekall.Age.Cli -- game verify-playable .age-sandbox Main 2
dotnet run --project src/Rekall.Age.Cli -- game package-playable .age-sandbox Main .age-sandbox/Builds/RekallAgePlayer
dotnet run --project src/Rekall.Age.Cli -- game inspect-package .age-sandbox/Builds/RekallAgePlayer.zip
dotnet run --project src/Rekall.Age.Cli -- game run-package .age-sandbox/Builds/RekallAgePlayer.zip 2
dotnet run --project src/Rekall.Age.Cli -- game capture-package-frame .age-sandbox/Builds/RekallAgePlayer.zip .age-sandbox/Artifacts/PackageFrames 1
dotnet run --project src/Rekall.Age.Cli -- game audit-package .age-sandbox/Builds/RekallAgePlayer.zip .age-sandbox/Artifacts/PackageAudit
```

Package manifest contains:

- package kind
- scene name
- bundled game root
- launch path
- launch arguments
- verification checks
- draw assertion results

Audit checks:

- manifest readable
- package ready
- run succeeds
- proof frame captured
- key artifacts present
- verification checks passed

## Studio Workbench

Studio is the WPF workbench shell.

Current foundation:

- startup project/scene loading
- workbench model construction
- scene summary
- validation status
- renderable counts
- camera counts
- physics body counts
- asset reports
- logging

Run:

```powershell
dotnet run --project src/Rekall.Age.Studio -- --project .age-sandbox --scene Main
```

Logs:

```text
%LOCALAPPDATA%\Rekall AGE\Studio\Logs
```

## Testing

Main test command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj
```

Full solution:

```powershell
dotnet test Rekall.AGE.sln
```

Important test areas:

- command bus
- transactions
- project/scene persistence
- validation
- module source editing
- runtime module loading
- input action projection
- event facts
- physics/collider/trigger facts
- multiplayer snapshots and deltas
- MCP initialize/tools/list/tools/call
- Vulkan diagnostics
- render plans
- runtime viewport captures
- GLB import/export
- OpenXR probe and frame plans
- planet import and rendering
- virtual geometry inspection/application
- packaging workflows

Manual rendering verification:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render vulkan probe
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport 640 360 vulkan
dotnet run --project src/Rekall.Age.Cli -- render performance budget .age-sandbox Main desktop60 0 640 360
```

Manual OpenXR verification:

```powershell
dotnet run --project src\Rekall.Age.Cli -- render openxr bootstrap-session
dotnet run --project src\Rekall.Age.Cli -- render openxr frame-plan .age-sandbox Main 0 1280 1280
```

## Contributor Rules

Permanent architectural rules:

- Prefer generic authoring primitives over built-in game behavior.
- Do not add engine-owned game creation workflows.
- Put game behavior in project modules, examples, or user projects unless it is truly engine-general.
- Keep input generic: capture, normalize, project semantic actions, and let modules consume actions.
- Keep runtime events generic: emit facts, do not decide gameplay meaning.
- Let project modules emit custom facts.
- Use SDK helpers for entity queries and mutations.
- Use engine delta time for motion, timers, cooldowns, and simulation.
- Use camera/vector SDK helpers instead of guessing basis math.
- Preserve generic multiplayer metadata in authoritative snapshots.
- Improve diagnostics before adding special-case authoring behavior.
- When an example fails, fix the generic engine contract first.

Rekall AGE should feel like an engine that agents can inspect, understand, repair, extend, and use to author any game without the engine pretending to be the designer.
