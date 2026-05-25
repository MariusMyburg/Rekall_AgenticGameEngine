# Rekall AGE Scene Runtime Foundation Design

Date: 2026-05-25

## Purpose

Rekall AGE now has a command-backed workbench shell and editor read models. The next production slice should give that editor, the CLI, MCP, playtests, validation, screenshots, and future renderer/physics/audio/animation systems one canonical runtime scene model.

This design defines **Scene Runtime Foundation 1**. It creates the runtime spine that turns deterministic scene documents into runtime worlds, executes fixed-step systems, records observations, and exposes subsystem-ready projections for rendering, physics, audio, animation, and UI.

## Why This Slice

Workbench Foundation 1 made the editor visible, but most panels still project raw scene and asset files. A Unity/Unreal-class engine needs runtime state as a first-class product concept:

- editor play mode must run a scene without mutating authoring files
- rendering needs a frame model, not ad hoc component scans
- physics needs fixed-step state and collision observations
- audio needs listener/emitter state and validation
- animation needs per-frame playback state
- UI needs a runtime document tree and event targets
- agents need compact runtime summaries after playtests

Scene Runtime Foundation 1 is the right next layer because it gives all later subsystems a shared lifecycle and diagnostic contract.

## Master Design Alignment

This design extends:

- `docs/superpowers/specs/2026-05-24-rekall-age-agentic-engine-design.md`
- `docs/superpowers/specs/2026-05-25-rekall-age-production-workbench-design.md`

The same rules remain in force:

- authoring files remain deterministic and text-friendly
- all meaningful mutations still go through typed commands
- runtime play mode clones or projects scene state instead of editing authoring files
- editor, MCP, CLI, and tests share the same runtime/read-model services
- diagnostics are structured and agent-readable

## Scope

Scene Runtime Foundation 1 should include:

- runtime world records for scenes, entities, components, transforms, tags, and source IDs
- runtime snapshot builder from `RekallAgeSceneDocument`
- fixed-step frame clock and execution loop
- runtime system registry with ordered systems
- runtime observations with severity, target, subsystem, and suggested commands
- transform extraction for `Rekall.Transform2D` and `Rekall.Transform3D`
- render projection for cameras, sprites, meshes, lights, and UI layers
- physics projection for rigid bodies and colliders
- audio projection for listeners and emitters
- animation projection for animation players and clips
- UI projection for canvases and basic UI elements
- CLI command to inspect a scene runtime snapshot
- MCP catalog exposure through ordinary command registration
- tests proving deterministic fixed-step execution and subsystem projections

It should defer:

- real physics solving
- real audio mixing
- animation curve interpolation
- GPU scene rendering
- editor play-mode controls beyond runtime command/read-model exposure
- asset decoding for meshes/audio/animation

## Alternatives Considered

### Editor Interaction First

This would wire more buttons and panels in WPF. It would improve feel, but many controls would still have shallow behavior because runtime state is not rich enough.

### Renderer First

This would build a higher-level software or Vulkan scene renderer. Rendering needs a canonical frame source; otherwise it would duplicate scene scanning and become hard to align with physics, animation, and UI.

### Runtime Scene First

This is the recommended path. A runtime world and fixed-step execution layer make all later subsystems share state, diagnostics, and lifecycle.

## Architecture

Add runtime-domain types to `Rekall.Age.Runtime.Abstractions` and concrete builders/commands to `Rekall.Age.Runtime`.

```text
Rekall.Age.Runtime.Abstractions
  RekallAgeRuntimeWorld
  RekallAgeRuntimeEntity
  RekallAgeRuntimeComponent
  RekallAgeRuntimeTransform
  RekallAgeRuntimeFrameClock
  RekallAgeRuntimeObservation
  RekallAgeRuntimeSubsystemViews
  IRekallAgeRuntimeSystem

Rekall.Age.Runtime
  RekallAgeRuntimeWorldBuilder
  RekallAgeRuntimeExecutionLoop
  RekallAgeRuntimeSystemRegistry
  RekallAgeRuntimeSnapshotService
  Commands/InspectSceneRuntimeCommand
```

`Rekall.Age.Editor` should later consume this runtime snapshot service for viewport/play-mode panels. Scene Runtime Foundation 1 only needs to expose the service and command.

## Runtime World Model

The runtime world should be immutable at snapshot boundaries.

Required records:

- `RekallAgeRuntimeWorld`
  - scene id and name
  - frame index
  - elapsed time
  - entities
  - subsystem views
  - observations
- `RekallAgeRuntimeEntity`
  - entity id, name, tags, parent id, prefab source id
  - transform
  - components
- `RekallAgeRuntimeComponent`
  - component type
  - normalized property bag
- `RekallAgeRuntimeTransform`
  - 2D position/rotation/scale
  - 3D position/rotation/scale

The runtime world may contain derived data, but it must keep source IDs so diagnostics and editor selections can map back to authoring entities.

## Fixed-Step Execution

Add a deterministic execution loop:

```text
load scene -> build runtime world -> repeat fixed steps -> collect observations -> return final world
```

The first loop should:

- default to 60 Hz
- accept duration or explicit frame count
- run systems in stable order by priority and id
- keep frame index and elapsed time deterministic
- avoid mutating scene files
- return a final snapshot and observations

## Runtime Systems

Systems should be small and ordered.

Initial systems:

- transform normalization system
- render projection system
- physics projection system
- audio projection system
- animation projection system
- UI projection system

These systems do not need full simulation yet. Their job is to project valid runtime views and emit diagnostics for missing or inconsistent data.

## Subsystem Projections

### Rendering

Projection should produce:

- active camera candidates
- sprite renderers
- mesh renderers or mesh sets
- light entities
- UI layer roots

This should reuse or align with `Rekall.Age.Rendering.Abstractions` rather than create a second render model.

### Physics

Projection should produce:

- rigid bodies
- collider shapes
- triggers
- fixed-step settings
- debug draw descriptors

If a rigid body lacks a transform, emit a warning.

### Audio

Projection should produce:

- listeners
- emitters
- clip asset references
- buses or bus names

If emitters exist without a listener, emit a warning.

### Animation

Projection should produce:

- animation player components
- clip references
- playback state
- target entity IDs

If an animation player references a missing clip, emit a warning.

### UI

Projection should produce:

- UI canvas roots
- UI element entities
- interactive element count

If UI elements exist without a canvas, emit a warning.

## Commands

Add `rekall.runtime.inspect_scene`.

Request:

```json
{
  "projectRoot": ".age-sandbox",
  "sceneName": "Main",
  "frames": 3
}
```

Result:

```json
{
  "sceneName": "Main",
  "frameIndex": 3,
  "entityCount": 12,
  "renderableCount": 8,
  "physicsBodyCount": 2,
  "audioEmitterCount": 1,
  "animationPlayerCount": 1,
  "uiElementCount": 4,
  "observations": []
}
```

CLI route:

```powershell
dotnet run --project src/Rekall.Age.Cli -- runtime inspect .age-sandbox Main 3
```

## Error Handling

Runtime inspection should fail normally for missing scenes or invalid arguments, but content issues should become observations.

Observation fields:

- code
- severity
- subsystem
- target id
- message
- suggested command tools

Use severity values:

- `info`
- `warning`
- `blocking`

## Testing Strategy

Tests should prove:

- runtime world builder preserves stable scene/entity IDs
- transform extraction reads `Rekall.Transform2D` and `Rekall.Transform3D`
- fixed-step execution advances frame index deterministically
- render projection counts cameras, sprites, meshes, lights, and UI roots
- physics/audio/animation/UI projections produce counts and warnings
- runtime inspection command returns compact agent-readable summary
- CLI route prints runtime counts
- existing `dotnet test Rekall.AGE.sln` remains clean

## First Implementation Boundary

The implementation plan should build **Scene Runtime Foundation 1** only.

It should not add real solvers, mixers, animation interpolation, GPU rendering, or WPF play controls. Those become follow-up specs once the canonical runtime world is stable.

## Acceptance Criteria

Scene Runtime Foundation 1 is ready when:

- a scene can be loaded into a runtime world with stable IDs and normalized transforms
- a fixed-step runtime run produces deterministic frame index and elapsed time
- rendering, physics, audio, animation, and UI projections are available from the runtime world
- subsystem content issues are emitted as structured observations
- `rekall.runtime.inspect_scene` is available through command bus, CLI, and MCP catalog registration
- tests cover world building, execution, projections, observations, command output, and CLI output
- full build and test suite pass

## Open Risks

- Component names are still convention-based strings. This is acceptable for this slice, but source-generated schemas should eventually provide stronger component contracts.
- The initial projections may be metadata-only. They should be honest about that and avoid claiming full renderer/physics/audio/animation behavior.
- Runtime snapshots must not mutate scene documents; tests should guard that boundary.

## Spec Self-Review

- Marker scan: clean.
- Consistency check: runtime world is read/projection-oriented and preserves command-bus mutation rules.
- Scope check: the slice is focused on canonical runtime state and projections, not full subsystem implementations.
- Ambiguity check: command names, acceptance criteria, and deferred work are explicit.
