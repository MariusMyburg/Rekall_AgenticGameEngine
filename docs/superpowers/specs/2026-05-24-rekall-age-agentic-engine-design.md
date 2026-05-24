# Rekall AGE Agentic Engine Design

Date: 2026-05-24

## Purpose

Rekall AGE is a greenfield, agent-native C# game engine designed so AI agents can create, inspect, mutate, validate, test, and package complete games through MCP and headless tooling.

The engine is not a conventional editor-first game engine with AI added later. Its canonical interface is a typed command/runtime contract. MCP, CLI, tests, future GUI editors, and human IDE workflows all sit on top of that same contract.

The v1 goal is rapid creation of complete small and medium games by agents. High-end engine capability remains a long-term architectural goal, but v1 should prove the agentic game-creation loop first.

## Founding Principles

1. Rekall AGE is agent-native. MCP/headless workflows are the primary path.
2. Human IDE coding is supported as module authoring, but must not compromise the agent-first design.
3. No editor-only hidden state is allowed. Any future editor is a client of the same command bus as MCP and CLI.
4. Every meaningful mutation must be structured, transaction-based, diffable, undoable, validatable, and reportable.
5. Runtime features are capability-driven so projects expose only the systems, tools, schemas, and validators they actually use.
6. Agents should receive compact, purpose-built context snapshots instead of needing to read the entire project.
7. Validation, testing, screenshots, and build feedback are first-class parts of the creation loop.

## Architecture Spine

The engine should be split into modular C# packages:

```text
Rekall.Age.Core
  Runtime loop, services, diagnostics, command bus, transactions

Rekall.Age.Project
  Project manifest, packages, capabilities, build profiles

Rekall.Age.World
  Scenes, entities, components, prefabs, serialization, stable IDs

Rekall.Age.Modules
  C# game modules, source-generated metadata, schemas, validators

Rekall.Age.Commands
  Shared command abstractions, schemas, results, undo data

Rekall.Age.Assets
  Importers, GUIDs, dependency graph, asset database

Rekall.Age.Agent
  Agent context snapshots, planning hints, reports, validation summaries

Rekall.Age.Mcp
  MCP tools over the command/runtime contract

Rekall.Age.Rendering.Abstractions
  Shared rendering interfaces, cameras, materials, render targets

Rekall.Age.Rendering2D
  Sprites, tilemaps, 2D cameras, 2D draw pipeline

Rekall.Age.Rendering3D
  Meshes, basic materials, lights, 3D cameras, 3D draw pipeline

Rekall.Age.Physics2D
  Optional 2D physics capability

Rekall.Age.Physics3D
  Optional 3D physics capability

Rekall.Age.Audio
  Optional audio capability

Rekall.Age.Input
  Optional input abstraction

Rekall.Age.Ui
  Optional game UI capability

Rekall.Age.Build
  Headless build and packaging for platform targets
```

`Rekall.Age.Core` must not know about 2D, 3D, physics, audio, UI, or editor concepts. Those are installed as capabilities.

## Capability Model

A project declares capabilities in its manifest:

```json
{
  "capabilities": [
    "world",
    "rendering2d",
    "physics2d",
    "audio",
    "mcp"
  ]
}
```

Capabilities determine:

- available components
- available systems
- available MCP tools
- available command schemas
- available validators
- available asset importers
- generated context summaries
- build dependencies

This keeps a 2D project from exposing irrelevant 3D APIs, and lets headless simulations strip rendering entirely.

## Command Bus And Transactions

The command bus is the core engine interface.

Every meaningful project mutation should be represented as a typed command:

```text
CreateProject
AddCapability
CreateScene
CreateEntity
AddComponent
SetComponentProperty
CreatePrefab
InstantiatePrefab
ImportAsset
CreateMaterial
AttachModule
RunScene
CaptureScreenshot
ValidateProject
BuildGame
```

Public APIs should use Rekall-branded names:

```csharp
public interface IRekallAgeCommand<TRequest, TResult>
{
    string Name { get; }
    RekallAgeCommandSchema Schema { get; }
    ValueTask<TResult> ExecuteAsync(TRequest request, RekallAgeCommandContext context);
}
```

Commands run inside `RekallAgeTransaction` objects. A transaction groups related mutations and records:

- changed files
- changed scenes, entities, components, prefabs, and assets
- validation results
- compile results
- runtime test results
- screenshots and logs
- undo or rollback data
- compact agent summary

Example transaction:

```text
BeginTransaction("Create player controller")
  CreateEntity("Player")
  AddComponent("Transform2D")
  AddComponent("SpriteRenderer")
  AddComponent("PlayerController")
  SetComponentProperty(...)
  ValidateScene()
  RunHeadlessSmokeTest()
CommitTransaction()
```

Agents should normally mutate projects through commands rather than direct file edits.

## MCP Surface

`Rekall.Age.Mcp` is a thin adapter over the command bus.

Tool groups should include:

```text
rekall.project.*
rekall.capability.*
rekall.context.*
rekall.scene.*
rekall.entity.*
rekall.component.*
rekall.asset.*
rekall.module.*
rekall.validate.*
rekall.run.*
rekall.capture.*
rekall.build.*
rekall.workflow.*
```

MCP should expose both low-level commands and higher-level workflows.

Low-level examples:

```text
rekall.entity.create
rekall.component.set_property
rekall.asset.import
```

Workflow examples:

```text
rekall.workflow.create_2d_player
rekall.workflow.create_camera2d
rekall.workflow.create_main_menu
rekall.workflow.create_basic_level
rekall.workflow.fix_validation_errors
rekall.workflow.package_playable_build
```

Workflow tools must decompose into ordinary commands internally so they remain diffable, undoable, and validatable.

Command results should be compact and structured:

```json
{
  "ok": false,
  "summary": "Scene cannot run: no active camera.",
  "changed": ["Scenes/Main.age"],
  "errors": [
    {
      "code": "REKALL_SCENE_NO_CAMERA",
      "target": "Scenes/Main.age",
      "suggestedFix": "Create Camera2D or Camera3D and mark it active."
    }
  ]
}
```

## World And Scene Model

The world model should be deterministic, text-friendly, and easy for agents to inspect.

Scenes, prefabs, entities, components, and assets need stable IDs so tools can make precise edits without fragile name matching or file offsets.

Example scene shape:

```json
{
  "id": "scene_main",
  "name": "Main",
  "capabilities": ["world", "rendering2d"],
  "entities": [
    {
      "id": "ent_player",
      "name": "Player",
      "tags": ["player"],
      "components": [
        {
          "type": "Rekall.Transform2D",
          "position": [0, 0],
          "rotation": 0,
          "scale": [1, 1]
        },
        {
          "type": "Game.PlayerController",
          "speed": 7.5
        }
      ]
    }
  ]
}
```

The world system must support:

- deterministic serialization order
- text-friendly project, scene, and prefab files
- stable object IDs
- explicit asset references
- component schemas
- prefab graphs
- scene summaries
- transaction diffs
- validation rules per component and capability

Direct file editing remains possible for humans, but the indexer and validator must detect, normalize, and report the resulting changes.

## C# Module Model

Game code is organized as Rekall AGE modules rather than loose scripts.

Modules declare components, systems, validators, workflows, tests, and required capabilities:

```csharp
[RekallAgeModule("Platformer")]
public sealed class PlatformerModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RequireCapability("world");
        builder.RequireCapability("rendering2d");
        builder.RequireCapability("physics2d");

        builder.RegisterComponent<PlayerController>();
        builder.RegisterSystem<PlayerMovementSystem>();
        builder.RegisterValidator<PlayerSceneValidator>();
        builder.RegisterWorkflow<CreatePlatformerPlayerWorkflow>();
    }
}
```

Components should be explicit and schema-friendly:

```csharp
[RekallAgeComponent("Player Controller")]
public sealed partial class PlayerController : RekallAgeComponent
{
    [RekallAgeProperty(Min = 0, Max = 30)]
    public float MoveSpeed { get; set; } = 8f;

    [RekallAgeProperty]
    public RekallAgeAssetRef JumpSound { get; set; }
}
```

Source generators and analyzers should produce metadata:

```json
{
  "module": "Platformer",
  "components": [
    {
      "type": "Game.PlayerController",
      "displayName": "Player Controller",
      "properties": {
        "MoveSpeed": {
          "type": "float",
          "default": 8,
          "min": 0,
          "max": 30
        },
        "JumpSound": {
          "type": "assetRef",
          "assetKind": "audio"
        }
      }
    }
  ]
}
```

Generated metadata powers:

- MCP component discovery
- command validation
- future inspector UI
- schema documentation
- project validation
- code completion helpers
- migration tooling
- agent context snapshots

If code cannot be indexed into metadata, agents should treat it as opaque. It may still run, but it should not become part of the agentic surface until wrapped with Rekall contracts.

## Agent Context Layer

`Rekall.Age.Agent` maintains indexed, compact views of the project for LLMs.

Agent context tools should include:

```text
rekall.context.project_summary
rekall.context.scene_summary
rekall.context.entity_detail
rekall.context.available_components
rekall.context.validation_summary
rekall.context.asset_graph
rekall.context.recent_transactions
rekall.context.next_actions
```

The context layer should index:

- project manifest
- installed capabilities
- scenes
- prefabs
- assets
- modules
- generated component schemas
- validators
- tests
- recent command history
- known errors
- screenshots and build artifacts

Context should be available at several sizes:

```text
tiny    -> 1-2 KB: current health and next action
small   -> 5-10 KB: useful for most MCP loops
medium  -> 20-50 KB: scene or module detail
full    -> explicit request only
```

Example project summary:

```json
{
  "project": "Crystal Mines",
  "capabilities": ["world", "rendering2d", "physics2d", "audio"],
  "playableScenes": ["MainMenu", "Mine01"],
  "modules": ["CoreGameplay", "Mining", "Inventory"],
  "health": {
    "status": "blocked",
    "blockingIssues": [
      "Mine01 has no active camera",
      "Player prefab missing Rigidbody2D"
    ]
  },
  "recommendedNextActions": [
    "Run rekall.workflow.fix_validation_errors",
    "Capture screenshot for Mine01"
  ]
}
```

## Validation, Testing, And Feedback

Validation is a first-class subsystem.

Validation layers:

```text
Schema validation
  Does data match known schemas?

Capability validation
  Does the project use only installed capabilities?

Reference validation
  Do asset, prefab, scene, and entity references resolve?

Gameplay validation
  Does a playable scene have camera, input, spawn point, and game loop requirements?

Runtime validation
  Can the scene run headlessly for N seconds without errors?

Visual validation
  Can the engine capture a screenshot, and is it nonblank?
```

Validators should be normal C# module types:

```csharp
[RekallAgeValidator("platformer.scene")]
public sealed class PlatformerSceneValidator : IRekallAgeValidator
{
    public ValueTask ValidateAsync(RekallAgeValidationContext context)
    {
        context.RequireEntityWithTag("player");
        context.RequireActiveCamera();
        context.RequireComponent<PlayerController>("player");
        return ValueTask.CompletedTask;
    }
}
```

Validation output must be structured and fix-oriented:

```json
{
  "status": "failed",
  "blocking": [
    {
      "code": "REKALL_CAMERA_MISSING",
      "message": "Scene 'Mine01' has no active camera.",
      "suggestedCommands": [
        {
          "tool": "rekall.workflow.create_camera2d",
          "arguments": { "scene": "Mine01" }
        }
      ]
    }
  ]
}
```

Testing tools should include:

```text
rekall.test.compile
rekall.test.modules
rekall.test.scene_headless
rekall.test.scene_visual
rekall.test.package_smoke
```

Visual feedback should support deterministic desktop and headless screenshots:

```json
{
  "screenshot": "Artifacts/Screenshots/Mine01_0003.png",
  "nonBlank": true,
  "visibleRenderers": 28,
  "activeCamera": "MainCamera",
  "frameTimeMs": 6.4
}
```

The core loop should be:

```text
mutate -> compile -> validate -> run -> capture -> inspect -> fix
```

## V1 Capability Set

V1 should include:

- project creation
- capability add and remove
- scene, entity, component, and prefab commands
- deterministic text-friendly serialization
- C# module discovery
- source-generated metadata
- MCP server
- CLI wrapper over the same command bus
- headless runtime
- basic 2D renderer with sprites, tilemaps, and cameras
- basic 3D renderer with meshes, cameras, simple lights, and basic materials
- basic asset import for textures, sprites, meshes, and audio
- basic input abstraction
- basic UI with menus, labels, buttons, and panels
- basic 2D and 3D physics integrations
- audio playback
- validation and structured diagnostics
- headless scene smoke tests
- screenshot capture
- packaging for Windows and Linux desktop
- example game templates

V1 should defer:

- full editor UI
- realtime global illumination
- advanced PBR
- terrain
- animation graph editor
- visual scripting
- multiplayer and networking
- console, mobile, and web targets
- advanced particles
- advanced cinematic tooling
- marketplace or large package ecosystem

Example v1 target games:

- Pong
- Breakout
- Asteroids
- top-down shooter
- 2D platformer
- simple tower defense
- visual novel or adventure game
- simple first-person exploration game
- simple 3D collectathon
- basic puzzle game

## Platform Targets

Initial first-class targets:

- Windows desktop
- Windows headless
- Linux desktop
- Linux headless

Headless execution is required for agent workflows and CI.

## Reference Engines

The workspace includes Prowl and Stride as examples. Rekall AGE should remain greenfield.

Prowl is useful as a compact C#/.NET reference for scenes, assets, editor scripting, components, and serialization.

Stride is useful as a reference for larger C# engine modularity and long-term capability.

Neither should be forked or treated as Rekall AGE's base architecture.

## Open Implementation Notes

The first implementation plan should start with the agent-native vertical slice:

1. solution and package structure
2. project manifest and capability model
3. command bus and transaction result model
4. world model with deterministic scene serialization
5. module metadata skeleton
6. validation skeleton
7. MCP server skeleton
8. CLI adapter
9. headless runtime loop
10. minimal 2D/3D render proof with screenshot capture

This proves the core thesis before expanding engine breadth.
