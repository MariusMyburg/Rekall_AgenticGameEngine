# Summit Run 2D Full-Game Acceptance Design

## Purpose

Prove the intended Rekall AGE workflow by asking Studio, in ordinary English, to create a complete playable 2D hill-driving game. The exercise must test the actual Studio chat, scene/world viewport, inspector, runtime, Player, packaging, and diagnostics. Any failure that represents a general engine or Studio deficiency is repaired in the generic contract before the game is completed.

The acceptance game is named **Summit Run**. It is an original side-view vehicle game inspired by the broad physics-driving structure of hill-climb games; it must not copy protected names, art, layouts, audio, or branding.

## Product and Architecture Rules

- Game-specific driving, fuel, scoring, camera-follow, reset, win, and failure behavior lives in an agent-authored C# project module.
- The engine exposes only generic, inspectable authoring primitives. No hill-driving or vehicle gameplay is added to engine core.
- Realtime code uses the engine-provided delta time.
- Controls are exposed through `Rekall.InputActionMap` and consumed as semantic actions.
- The authored controller owns a `Game.*` component whose state can be inspected and asserted.
- Gameplay is not considered complete until deterministic `rekall.runtime.inspect_scene` input frames prove a strict transform or `Game.*` state change after the latest mutation.
- The same visual content must render in Studio, software captures, Vulkan Player output, and packaged output.

## Acceptance Game

### Player experience

The game opens directly into a readable 1280x720 side-view scene. A compact two-wheel rover starts on rolling terrain. The player drives right across several slopes, collects energy cells, manages a fuel meter, and reaches a finish beacon. The presentation uses a restrained high-contrast palette and geometric art so it remains coherent without external asset generation.

Controls:

- `drive.throttle`: D/Right Arrow drives forward; A/Left Arrow reverses.
- `vehicle.lean`: W/Up Arrow leans one way; S/Down Arrow leans the other.
- `game.reset`: R resets after a crash, fuel exhaustion, or at any time.

The HUD shows the title, control hint, distance, collected cells, and fuel/status. The active camera follows the rover smoothly while keeping enough road visible ahead. The game reports a clear success state at the finish and a clear recovery path after failure.

### Scene composition

- One active `Rekall.Camera2D` with a stable orthographic view.
- A chassis and two independently simulated wheels using `Rekall.Rigidbody2D` and 2D colliders.
- Generic hinge joints connect wheels to the chassis; joint motors receive the semantic throttle value.
- Rotated static `Rekall.BoxCollider2D` segments form the terrain.
- Collectibles use visible 2D shapes plus generic triggers.
- A controller entity owns `Game.SummitRunState` and the authored runtime system.
- UI is built with the existing canvas/layout/text primitives.

### Runtime behavior

The module reads semantic input, applies bounded motor targets/impulses, applies bounded chassis lean torque, decreases fuel using delta time only while driving, collects trigger-overlapped energy cells, and updates score/status. It updates the camera from the chassis transform with smoothing based on delta time. Reset restores chassis, wheels, fuel, status, and collectible visibility/state. The module emits structured observations for missing required entities, components, joints, or actions.

## Generic Engine Improvement: `Rekall.ShapeRenderer2D`

### Motivation

The current 2D production renderer requires imported sprite textures. That makes the primary text-only game-authoring workflow unnecessarily dependent on prestaged assets and encourages agents to fake 2D scenes with 3D primitives. A generic geometric 2D renderer is therefore an acceptance-driven engine primitive, not game-specific functionality.

### Authoring contract

Register a built-in `Rekall.ShapeRenderer2D` component with these properties:

- `shape`: enum-like string, `rectangle` or `circle`; default `rectangle`.
- `width`: positive world-unit rectangle width; default `1`.
- `height`: positive world-unit rectangle height; default `1`.
- `radius`: positive world-unit circle radius; default `0.5`.
- `color`: fill color in the engine's accepted hexadecimal color format; default `#ffffff`.
- `active`: whether the shape projects into rendering; default `true`.

`Rekall.Transform2D` supplies world position and rotation. Its scale multiplies the explicit shape dimensions. The explicit dimensions mirror the existing collider convention and keep authoring intent inspectable. `Rekall.RenderLayer` continues to provide layer and camera culling behavior.

Invalid or unknown shape strings normalize to `rectangle`; non-positive dimensions clamp to a small positive value. Inactive or invisible entities do not project a renderable.

### Runtime projection and rendering

The built-in projection represents each active shape as a standard runtime render mesh with built-in projection provenance. The frame builder generates an XY-plane geometry mesh:

- Rectangle: four vertices and two triangles centered on the entity origin.
- Circle: a center vertex and a bounded triangle fan with enough segments for smooth production output.

The renderable remains `kind: "mesh"` so existing software, Vulkan, culling, capture, and Player paths consume the same geometry contract. Shape fill uses the component color as material color. Shape geometry is asset-free and must not be counted as a missing or fallback asset.

The camera is explicitly placed behind the XY plane and looks along the existing positive-Z convention, avoiding default-camera auto-framing and making camera follow deterministic.

### Studio behavior

The component appears in schema search and the Add Component workflow with understandable labels, constraints, defaults, and descriptions. Shape entities appear as renderables in the viewport, remain selectable through the normal entity/hierarchy interaction, and expose editable properties in the existing inspector. No bespoke hill-game Studio UI is added.

## Studio Authoring Trial

Create `Examples/SummitRun` through Studio's Create Project dialog, then use the configured Ollama model in Studio chat with one cohesive game request. The initial prompt must state the intended game, interaction, visuals, controls, and completion criteria, but it must not prescribe scene JSON or C# implementation details.

Watch Studio logs, chat tool activity, project files, validation output, and runtime diagnostics throughout. Repair general failures in Studio or engine code using focused tests, rebuild/relaunch Studio when necessary, and continue the same authoring objective. Do not hide authoring failures by manually replacing the whole project outside Studio; direct file edits are permitted only for narrowly diagnosed repair when the chat workflow itself cannot yet perform the repair, and that limitation must become a tested Studio/engine improvement where generic.

## Required Evidence

Completion requires all of the following after the final scene or module mutation:

1. Focused automated tests for `Rekall.ShapeRenderer2D` schema, projection, frame geometry, and non-fallback rendering.
2. Studio evidence that Create opens a project dialog, the project opens, the scene renders, an entity can be selected, and shape properties are editable in the inspector.
3. A successful project validation and C# module build.
4. Deterministic runtime input frames containing representative throttle and/or lean input.
5. Strict assertions that prove a nonzero rover transform delta and a changed `Game.SummitRunState` property.
6. A closed-loop agent-authoring gauntlet run, followed by any necessary narrow repair loop.
7. A production Vulkan Player launch and visual capture showing the playable composition rather than a blank, fallback, or debug-only view.
8. A packaged game, package audit, and packaged-frame capture.
9. Final focused verification plus independent code review of the resulting engine/Studio changes.

## Out of Scope

- A reusable built-in vehicle controller.
- Genre-specific terrain generation, fuel, scoring, or camera behavior in engine core.
- Copying the visual identity, content, or progression of an existing commercial game.
- Online services, monetization, or multiplayer.
- External raster art when geometric primitives are sufficient for this acceptance game.
