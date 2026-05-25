# Rekall AGE Production Workbench Design

Date: 2026-05-25

## Purpose

Rekall AGE should grow from the current agent-native vertical slice into a commercial-feeling game creation platform without losing its founding idea: agents, CLI, MCP, tests, and any human editor all operate through the same typed command and runtime contract.

This design defines the next production milestone: a real editor workbench backed by mature runtime and asset contracts. It does not attempt to build all Unity or Unreal-class systems in one pass. It creates the product surface and backend boundaries that let rendering, physics, audio, animation, UI tooling, asset import, and level-design systems mature in focused follow-up plans.

## Master Design Alignment

This design extends `docs/superpowers/specs/2026-05-24-rekall-age-agentic-engine-design.md`.

The production workbench must preserve these rules:

- The command bus remains the canonical mutation interface.
- The editor is a client of the same command bus as MCP and CLI.
- No editor-only hidden state is allowed for projects, scenes, assets, prefabs, or runtime settings.
- Mutations are transaction-based, diffable, validatable, undoable, and reportable.
- Capabilities decide which components, systems, tools, validators, importers, and context summaries exist.
- Agent context remains compact and purpose-built; the editor can consume richer views but must not replace the agent context layer.

## Scope

The next production milestone is **Rekall Studio Workbench 1**.

It should include:

- desktop editor shell
- project open/create workflow
- scene hierarchy
- entity/component inspector
- asset browser and import queue
- viewport preview panel
- play, playtest, and capture controls
- validation and diagnostics panel
- transaction history with undo/redo-ready records
- prefab and level-authoring contracts
- runtime service contracts for rendering, physics, audio, animation, UI, and input
- asset database contracts for import, dependency tracking, cooking, cache invalidation, and artifact reporting
- MCP and CLI parity for the new editor-backed workflows

It should defer:

- full physically based renderer
- production terrain
- visual scripting
- animation graph editor implementation
- multiplayer
- console, mobile, and web platform targets
- marketplace/package ecosystem
- large-scale plugin host
- complete profiler and frame debugger

## Alternatives Considered

### Runtime-First

A runtime-first phase would deepen renderer, physics, audio, animation, and scheduling internals before a full editor exists. This is attractive for engine purity, but it risks creating internal systems that are not authorable, inspectable, or agent-operable.

### Pipeline-First

A pipeline-first phase would build the asset database, importers, dependency graph, cook cache, and build pipeline before a full editor exists. This gives strong production foundations, but users and agents would still lack a rich place to compose scenes and debug content.

### Editor-First With Production Contracts

This is the recommended approach. Build the editor workbench first, but keep it thin over command, asset, runtime, and validation services. The editor forces the authoring workflow to become real while the contracts prevent a throwaway UI from bypassing the agent-native architecture.

## Product Architecture

Rekall Studio should be a new desktop application project:

```text
Rekall.Age.Studio
  Desktop editor shell and workbench composition

Rekall.Age.Editor
  Editor-domain services, view models, panels, undo/redo projections, command adapters

Rekall.Age.Editor.Contracts
  Stable DTOs for project trees, scene graphs, inspector schemas, asset views, diagnostics

Rekall.Age.Runtime.Abstractions
  Runtime world, frame, scheduler, subsystem, and simulation contracts

Rekall.Age.Rendering.Abstractions
  Cameras, render worlds, materials, meshes, sprites, targets, frame captures

Rekall.Age.Physics
  Physics scene contracts and initial deterministic lightweight 2D/3D simulation adapters

Rekall.Age.Audio
  Audio asset, clip, bus, emitter, listener, and preview contracts

Rekall.Age.Animation
  Clip, track, keyframe, state, and playback contracts

Rekall.Age.Ui
  Runtime UI document, layout, style, event, and preview contracts

Rekall.Age.AssetPipeline
  Import graph, source watchers, cooked artifacts, dependency tracking, cache invalidation

Rekall.Age.LevelDesign
  Prefabs, overrides, placement tools, tilemaps, grid snapping, selection, gizmo operations
```

The current `Rekall.Age.Rendering` project can continue hosting existing software and Vulkan proof work while new renderer-neutral abstractions are introduced. The split should happen only where it reduces coupling.

## Editor UX

The first editor should feel like a real production tool, not a demo wrapper.

Core layout:

- top toolbar with project, save, undo, redo, play, pause, stop, capture, build, and validation actions
- left scene hierarchy with search, tags, active scene selection, prefab markers, and entity visibility/lock states
- central viewport with 2D/3D mode, camera controls, selection outlines, transform gizmos, grid controls, and play preview
- right inspector driven by component schemas and asset metadata
- bottom panel with assets, console, validation, transactions, import jobs, and build output
- command palette for all registered commands and workflows

Important UX rules:

- every editor action calls a typed command or read-model query
- every command result updates the transaction panel and validation panel
- inspector controls are generated from module/component schemas
- scene hierarchy and asset browser use stable IDs, not names, as the backing identity
- viewport previews use runtime snapshot services, not duplicated editor simulation
- failures should be visible, fix-oriented, and linked to suggested commands

## Runtime Systems

The runtime should gain a production spine before individual subsystems become advanced.

Required contracts:

- `RekallAgeRuntimeWorld` for loaded scenes, entities, components, prefabs, and runtime-only state
- `IRekallAgeRuntimeSystem` for deterministic update systems
- `RekallAgeFrameContext` for delta time, frame index, input, diagnostics, and cancellation
- `RekallAgeSubsystemRegistry` for rendering, physics, audio, animation, UI, and input services
- fixed-step simulation support for physics and deterministic playtests
- editor play mode that can clone or snapshot scene state before mutation
- runtime observations that feed agent context and the editor diagnostics panel

The first implementation can keep behavior modest. The important production step is establishing stable subsystem boundaries and lifecycle rules.

## Renderer

Rendering should mature through a renderer-neutral scene contract.

Required model:

- render world extracted from scenes and runtime state
- 2D camera, sprite, tilemap, text, and shape primitives
- 3D camera, mesh, material, light, and transform primitives
- render target descriptors and capture results
- material and shader metadata records
- viewport preview API for editor embedding
- backend capability report for software, Vulkan, and future Direct3D 12

Initial renderer behavior:

- keep deterministic software rendering for tests and headless proof frames
- keep Vulkan clear/pass/readback work as low-level backend validation
- add a higher-level frame graph contract that can represent sprites, meshes, cameras, and UI layers
- report unsupported render commands structurally instead of failing opaquely

## Physics

Physics should start as deterministic contracts and simple adapters.

Required model:

- physics world per scene
- collider components for box, circle, capsule, mesh, and trigger volumes
- rigid body components for 2D and 3D
- fixed-step simulation settings
- raycast and overlap queries
- collision event observations
- editor debug draw descriptors

The first implementation should prioritize stable authoring and testing over solver sophistication.

## Audio

Audio should be asset- and scene-aware from the start.

Required model:

- imported audio clip metadata
- audio emitters and listeners
- buses, volume, mute, loop, spatialization flags
- preview playback command
- runtime audio observations for missing clips, invalid listeners, and play state

The first workbench can expose metadata and validation before full real-time mixing lands.

## Animation

Animation should be represented as data that agents and editor panels can inspect.

Required model:

- animation clips with typed tracks and keyframes
- sprite animation and transform animation support
- playback state component
- simple animation controller states and transitions
- editor timeline read model
- validation for missing targets, invalid curves, and incompatible property paths

The first milestone should implement clip data, metadata, import hooks, and basic playback contracts. A graph editor is deferred.

## UI Tooling

Runtime UI should become a first-class capability.

Required model:

- UI documents with stable element IDs
- layout primitives: canvas, panel, stack, grid, button, label, image
- style records for colors, fonts, spacing, anchors, and interaction states
- input/event binding metadata
- inspector-editable properties
- preview and screenshot support

The editor should expose UI documents as assets and allow basic hierarchy and inspector editing through the same command model.

## Asset Pipeline

The asset pipeline should become robust enough for editor workflows and repeatable builds.

Required model:

- asset GUIDs independent of file names
- source asset records
- imported asset records
- cooked artifact records
- importer registry by kind and extension
- dependency graph
- import settings
- deterministic output paths
- content hashes
- cache invalidation
- import job reports
- artifact diagnostics

Initial importers:

- textures and sprites
- sprite sheets
- meshes through an adapter boundary
- audio clips
- fonts
- UI documents
- animation clips
- materials

The first implementation may use metadata extraction and file copying for complex formats, but the contracts must distinguish source, imported, and cooked artifacts.

## Level Design Workflows

Level design should be commandable and editor-friendly.

Required workflows:

- create scene
- create entity
- duplicate entity
- parent or unparent entity
- add component
- set component property
- create prefab from entity
- instantiate prefab
- apply or revert prefab override
- place entity at viewport/world position
- snap transform to grid
- create 2D tilemap
- paint tile
- create blockout mesh primitive
- add active camera
- add playable loop
- validate playable scene

Every workflow must decompose into ordinary commands internally.

## Data And Read Models

The editor should not read raw project files directly for primary UX state. It should use explicit read models:

- `RekallAgeProjectTree`
- `RekallAgeSceneGraph`
- `RekallAgeInspectorModel`
- `RekallAgeAssetBrowserModel`
- `RekallAgeViewportModel`
- `RekallAgeValidationModel`
- `RekallAgeTransactionLogModel`
- `RekallAgeImportQueueModel`

These read models should also be useful to MCP tools and tests.

## Error Handling

All editor-facing operations should return structured results:

- summary
- changed resources
- validation issues
- diagnostics
- suggested fixes
- affected read models
- optional screenshot or artifact paths

Exceptions should be reserved for programmer errors and unrecoverable I/O failures. User-correctable content issues should be normal validation results.

## Testing Strategy

Coverage should grow in layers:

- command tests for every new mutation
- read-model tests for editor panels
- asset pipeline tests for import, dependency, cache, and diagnostic behavior
- runtime contract tests for deterministic fixed-step execution
- renderer tests for software proof frames and Vulkan backend capabilities
- validation tests for scenes, prefabs, assets, UI, animation, audio, and physics
- workbench smoke tests that open a project, edit an entity, import an asset, validate, play, and capture

Visual editor tests should verify that the workbench can render a nonblank project shell and that critical panels can be populated from read models.

## First Implementation Plan Boundary

The first implementation plan should build **Workbench Foundation 1**, not the whole production engine.

It should deliver:

- new editor/read-model projects
- project tree, scene graph, inspector, asset browser, validation, transaction, and import queue read models
- new commands for duplicate entity, parent entity, prefab create/instantiate, and import-with-report
- asset pipeline records for source/imported/cooked artifacts and dependency graph skeleton
- runtime abstraction records for frame context, subsystem registry, and render world extraction
- renderer-neutral viewport model with software preview integration
- desktop editor shell if a suitable local UI stack is already available, otherwise a command-backed headless workbench model with a minimal desktop host deferred to the next plan
- tests for the complete authoring loop

## Acceptance Criteria

Workbench Foundation 1 is ready when:

- a project can be opened into editor read models without direct file scraping
- a scene hierarchy can be populated from stable IDs
- inspector data is generated from component schemas and scene data
- asset import produces source, imported, and artifact records with diagnostics
- validation issues appear as structured editor diagnostics with suggested commands
- a scene can be modified through command-backed level-design workflows
- transaction history records every editor mutation
- play/capture uses runtime and renderer services, not editor-only code
- CLI/MCP can access the same new workflows
- tests prove the loop: create project, import asset, create scene, place entity, edit component, validate, play, capture, inspect transaction log

## Open Risks

- A full desktop editor stack choice can create churn if chosen before the read models stabilize.
- Mature 3D rendering, physics, and animation are large enough to need separate follow-up specs.
- The existing Vulkan proof work is valuable but is still below the level of a scene renderer.
- Asset import for complex formats should use proven libraries later; the first milestone should avoid pretending metadata-only adapters are complete importers.
- Editor UX can accidentally bypass agent-native contracts unless tests enforce command parity.

## Spec Self-Review

- Placeholder scan: no TBD or TODO markers remain.
- Consistency check: the editor is explicitly a command-bus client and does not own canonical project state.
- Scope check: the commercial target is decomposed into Workbench Foundation 1 plus follow-up subsystem plans.
- Ambiguity check: the first implementation boundary is explicit and acceptance criteria are testable.
