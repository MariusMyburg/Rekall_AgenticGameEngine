# Rekall AGE

Rekall AGE is a greenfield, agent-native C# game engine.

The current MVP includes:

- typed command bus through `IRekallAgeCommand`
- transaction tracking
- project-local transaction history for Studio and agent read models
- transaction history resource-change summaries with path, kind, existence, and size metadata
- immediate MCP/dynamic command transaction metadata with structured resource-change summaries
- transaction resource preimage snapshots for pre-mutation restore groundwork
- transaction preimage restore command for reverting a changed resource from a persisted snapshot
- project capability manifests
- deterministic scene/entity/component files
- C# module attributes and reflection-based component schema discovery
- C# module scaffolding for agent-authored or human-authored gameplay modules
- C# module source writing through the command bus for MCP-first agents
- C# module build command for compiling scaffolded gameplay modules
- generic C# runtime-system module scaffolding for agent-authored systems
- project-authored C# runtime systems that participate in runtime snapshots and viewport captures
- playable game readiness verification workflow for validation, build, and playtest checks
- playable game packaging workflow that verifies, bundles game content, writes a package manifest, creates a zip archive, and publishes launch artifacts
- playable package inspection with file inventory, key-artifact detection, manifest checks, and source-template metadata
- playable package audit workflow that inspects, runs, captures a PNG proof frame, and reports one deliverable-readiness verdict
- project module assembly loading for agent-readable schemas after build
- module-authored playable runtime execution; no engine-owned fallback games
- Vulkan-first internal rendering backend catalog with Direct3D 12 extension point
- native Vulkan loader probing for agent-readable backend diagnostics
- native Vulkan logical-device bootstrap validation with physical-device and graphics-queue selection
- native Vulkan command-pool allocation, empty command-buffer recording, queue submission, and fence wait validation
- native Vulkan mapped buffer creation with host-visible memory allocation and byte writes
- native Vulkan 2D image creation with device-local memory allocation and binding
- native Vulkan offscreen render target creation with image view, render pass, and framebuffer
- native Vulkan clear render-pass submission, readback, PNG capture, and agent-controlled clear color
- low-level render plan authoring, validation, command-buffer recording, and deterministic execution artifacts
- Vulkan render-plan execution for clear render passes
- deterministic asset import and catalog listing commands
- GLB asset import metadata inspection for scenes, nodes, meshes, materials, images, and animations
- runtime scene GLB export for engine-authored primitives, custom meshes, and extrusions with transforms, normals, UVs, vertex colors, and PBR base-color materials
- entity inspection and single-property component mutation commands
- MVP terminal player for module-authored projects, including deterministic playtest frames
- structured scene playtest assertions for MCP/headless agent loops
- structured validation
- compact agent project summaries
- MCP tool catalog skeleton
- stdio MCP JSON-RPC adapter for `initialize`, `tools/list`, and `tools/call`
- CLI adapter over the same command bus
- headless runtime smoke execution with active gameplay-system observations
- canonical runtime scene snapshots with render, physics, audio, animation, and UI projections
- runtime input snapshots with keyboard, mouse button, mouse position, mouse delta, mouse wheel, and semantic action projections
- deterministic transform animation for runtime pitch/yaw/roll rates
- runtime scene inspection through command bus, CLI, MCP catalog, and Studio read models
- runtime-backed viewport frame capture through command bus, CLI, MCP catalog, and Studio metadata
- runtime viewport backend reporting with explicit software/Vulkan acceleration status
- live player scene and asset editing over local named-pipe IPC through MCP tools
- Serilog-backed CLI, MCP stdio, and Studio startup/exception logging with daily rolling files
- deterministic software screenshot capture through command bus
- software viewport rasterization for cube, sphere, cylinder, cone, and plane geometry primitives plus authored triangle meshes with material colors and directional-light shading
- Vulkan runtime viewport clear-pass capture for empty frames, plus native offscreen Vulkan scene rendering for generated primitive meshes and authored triangle meshes
- Vulkan scene-rendering contracts, camera/view-projection propagation, primitive/custom mesh preparation, bundled GLSL shader deployment and compilation, uniform descriptors, model push constants, depth targets, indexed draw submission, readback, and runtime viewport scene-capture routing
- agent-authored 3D geometry primitive creation through command bus, CLI, and MCP catalog
- agent-authored custom 3D triangle mesh payloads through `Rekall.GeometryMesh`
- agent-authored procedural extrusions from 2D profiles through `rekall.geometry.create_extrusion`
- agent-accessible GLB export through `rekall.render.export_scene_glb` / `render glb export`
- built-in starter game workflows
- one-shot playable game workflow that creates, scaffolds, and builds genre-aware C# module code

## Starter Game Workflows

Agents can create these game foundations through the command bus or CLI:

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

Each template creates a project manifest, `Main` scene, active camera, core render primitives, and starter game-owned entities/components under `Game.Templates.*`; game behavior belongs in project-authored modules, not engine built-ins.

## Build

```powershell
dotnet build Rekall.AGE.sln
```

## Test

```powershell
dotnet test Rekall.AGE.sln
```

## CLI Example

```powershell
dotnet run --project src/Rekall.Age.Cli -- templates list
dotnet run --project src/Rekall.Age.Cli -- mcp stdio
dotnet run --project src/Rekall.Age.Cli -- render backends
dotnet run --project src/Rekall.Age.Cli -- render vulkan probe
dotnet run --project src/Rekall.Age.Cli -- render vulkan device bootstrap discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan command-buffer submit-empty discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan buffer create-mapped 256 vertex-buffer discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan image create-bound 64 64 R8G8B8A8_UNorm color-attachment discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan render-target create 128 72 R8G8B8A8_UNorm discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan render-pass read-clear 32 32 R8G8B8A8_UNorm discrete-gpu 0.25 0.5 0.75 1
dotnet run --project src/Rekall.Age.Cli -- render vulkan render-pass capture-clear 32 32 R8G8B8A8_UNorm discrete-gpu .age-sandbox/Artifacts/Vulkan 0.25 0.5 0.75 1
dotnet run --project src/Rekall.Age.Cli -- render plan create .age-sandbox software Preview
dotnet run --project src/Rekall.Age.Cli -- render resource add .age-sandbox preview-color image R8G8B8A8_UNorm color-attachment
dotnet run --project src/Rekall.Age.Cli -- render command-buffer record .age-sandbox main graphics '[{"op":"begin-render-pass","label":"preview","arguments":{"target":"preview-color"}},{"op":"draw-rect","label":"agent-rect","arguments":{"x":"8","y":"8","width":"24","height":"16","color":"#ffcc33"}},{"op":"end-render-pass","label":"preview","arguments":{}}]'
dotnet run --project src/Rekall.Age.Cli -- render plan validate .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- render plan execute .age-sandbox .age-sandbox/Artifacts/Render
dotnet run --project src/Rekall.Age.Cli -- render plan create .age-sandbox vulkan NativePreview
dotnet run --project src/Rekall.Age.Cli -- render resource add .age-sandbox frame-color image R8G8B8A8_UNorm color-attachment,transfer-src
dotnet run --project src/Rekall.Age.Cli -- render command-buffer record .age-sandbox main graphics '[{"op":"begin-render-pass","label":"frame","arguments":{"target":"frame-color","width":"32","height":"16","preferredDeviceType":"discrete-gpu"}},{"op":"clear","label":"sky","arguments":{"r":"0.25","g":"0.5","b":"0.75","a":"1"}},{"op":"end-render-pass","label":"frame","arguments":{}}]'
dotnet run --project src/Rekall.Age.Cli -- render plan execute .age-sandbox .age-sandbox/Artifacts/Render
dotnet run --project src/Rekall.Age.Cli -- module schemas
dotnet run --project src/Rekall.Age.Cli -- templates inspect pong
dotnet run --project src/Rekall.Age.Cli -- templates verify-mvp
dotnet run --project src/Rekall.Age.Cli -- game create .age-sandbox "Crystal Mines" pong
dotnet run --project src/Rekall.Age.Cli -- game create-playable .age-sandbox "Playable Pong" pong
dotnet run --project src/Rekall.Age.Cli -- game create-package-playable .age-sandbox "Packaged Pong" pong .age-sandbox/Builds/RekallAgePlayer .age-sandbox/Artifacts/PackageFrames
dotnet run --project src/Rekall.Age.Cli -- game verify-playable .age-sandbox Main 2 '[{"frameIndex":0,"contains":"PONG"}]'
dotnet run --project src/Rekall.Age.Cli -- game package-playable .age-sandbox Main .age-sandbox/Builds/RekallAgePlayer
dotnet run --project src/Rekall.Age.Cli -- game inspect-package .age-sandbox/Builds/RekallAgePlayer.zip
dotnet run --project src/Rekall.Age.Cli -- game run-package .age-sandbox/Builds/RekallAgePlayer.zip 2
dotnet run --project src/Rekall.Age.Cli -- game capture-package-frame .age-sandbox/Builds/RekallAgePlayer.zip .age-sandbox/Artifacts/PackageFrames 1
dotnet run --project src/Rekall.Age.Cli -- game audit-package .age-sandbox/Builds/RekallAgePlayer.zip .age-sandbox/Artifacts/PackageAudit
dotnet run --project src/Rekall.Age.Cli -- asset import .age-sandbox .\player.png sprite "Player Ship"
dotnet run --project src/Rekall.Age.Cli -- asset import-report .age-sandbox .\robot.glb model "Robot"
dotnet run --project src/Rekall.Age.Cli -- asset list .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- module scaffold-playable .age-sandbox crystal.playable "Crystal Playable" CrystalPlayable crystal
dotnet run --project src/Rekall.Age.Cli -- module scaffold-runtime-system .age-sandbox game.motion "Game Motion" GameMotion OrbitMotion OrbitMotionSystem
dotnet run --project src/Rekall.Age.Cli -- module sources .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- module read-source .age-sandbox CrystalPlayable CrystalPlayableModule.cs
dotnet run --project src/Rekall.Age.Cli -- module write-source .age-sandbox CrystalPlayable CrystalPlayableModule.cs .\CrystalPlayableModule.cs
dotnet run --project src/Rekall.Age.Cli -- build modules .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- module schemas project .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- context engine
dotnet run --project src/Rekall.Age.Cli -- context summary .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- context scene .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- transaction history .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- transaction restore-preimage .age-sandbox <transaction-id> Scenes/Main.age.scene.json
dotnet run --project src/Rekall.Age.Cli -- entity inspect .age-sandbox Main <entity-id>
dotnet run --project src/Rekall.Age.Cli -- component set .age-sandbox Main <entity-id> Rekall.Transform x 42
dotnet run --project src/Rekall.Age.Cli -- play scene .age-sandbox Main 4
dotnet run --project src/Rekall.Age.Cli -- play scene .age-sandbox Main 2 '[{"verticalAxis":1,"primaryAction":true},{"verticalAxis":-1}]'
dotnet run --project src/Rekall.Age.Cli -- playtest scene .age-sandbox Main 2 '[{"verticalAxis":1,"primaryAction":true},{"verticalAxis":-1}]' '[{"frameIndex":0,"contains":"Score 10"},{"frameIndex":1,"contains":"Left paddle lane 0"}]'
dotnet run --project src/Rekall.Age.Cli -- build player .age-sandbox Main
dotnet run --project src/Rekall.Age.Player -- .age-sandbox Main
dotnet run --project src/Rekall.Age.Player -- .age-sandbox Main --frames 2 --inputs '[{"verticalAxis":1,"primaryAction":true},{"verticalAxis":-1}]'
dotnet run --project src/Rekall.Age.Cli -- run scene .age-sandbox Main 0.1
dotnet run --project src/Rekall.Age.Cli -- run scene .age-sandbox Main 0.016 '[{"pressedKeys":["W"],"pressedKeysThisFrame":["W"]}]'
dotnet run --project src/Rekall.Age.Cli -- runtime inspect .age-sandbox Main 3
dotnet run --project src/Rekall.Age.Cli -- runtime inspect .age-sandbox Main 1 '[{"pressedKeys":["W"],"pressedKeysThisFrame":["W"],"mouseWheelDelta":1}]'
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport
dotnet run --project src/Rekall.Age.Cli -- capture screenshot .age-sandbox Main
```

## Workbench Foundation

The production workbench slice adds editor-facing read models, level-design workflows, asset pipeline reports, and a first Windows desktop Studio shell.

```powershell
dotnet run --project src/Rekall.Age.Cli -- studio open .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- asset import-report .age-sandbox .\player.png sprite "Player"
dotnet run --project src/Rekall.Age.Cli -- level entity duplicate .age-sandbox Main <entity-id> "Player Copy"
dotnet run --project src/Rekall.Age.Cli -- level prefab create .age-sandbox Main <entity-id> PlayerPrefab
dotnet run --project src/Rekall.Age.Cli -- level prefab instantiate .age-sandbox Main <prefab-id> "Prefab Player"
dotnet run --project src/Rekall.Age.Cli -- level entity snap .age-sandbox Main <entity-id> 1
dotnet run --project src/Rekall.Age.Cli -- geometry primitive create .age-sandbox Main "Crystal Orb" sphere -2 1 0 "#33ff66"
dotnet run --project src/Rekall.Age.Cli -- geometry mesh create .age-sandbox Main "Agent Triangle" '[{"x":0,"y":0,"z":0},{"x":1,"y":0,"z":0},{"x":0,"y":1,"z":0,"r":0,"g":1,"b":0}]' '[0,1,2]' 0 0 0 "#ff6633"
dotnet run --project src/Rekall.Age.Cli -- geometry extrusion create .age-sandbox Main "Agent Block" '[{"x":-0.5,"y":-0.5},{"x":0.5,"y":-0.5},{"x":0.5,"y":0.5},{"x":-0.5,"y":0.5}]' 1 0 0 0 "#44ccff"
dotnet run --project src/Rekall.Age.Studio -- --project .age-sandbox --scene Main
```

Studio writes Serilog diagnostics and unhandled exception details to daily rolling files under `%LOCALAPPDATA%\Rekall AGE\Studio\Logs`. Startup project-load failures are logged there and shown in the workbench as a validation status instead of a raw WPF exception dialog.

The CLI writes handled command failures and unexpected exceptions under `%LOCALAPPDATA%\Rekall AGE\Cli\Logs`; `mcp stdio` writes protocol and tool-call diagnostics under `%LOCALAPPDATA%\Rekall AGE\Mcp\Logs` without polluting JSON-RPC stdout. Set `REKALL_AGE_CLI_LOG_DIR`, `REKALL_AGE_MCP_LOG_DIR`, or the shared `REKALL_AGE_LOG_DIR` to redirect those logs during tests or automation.

Successful CLI and MCP mutations persist project-local transaction history in `Transactions/transactions.age.json`. Studio, workbench read models, and the `rekall.transaction.history` command load that log so agents and humans can inspect recent command effects after the original command context has ended. Each persisted transaction includes structured resource-change summaries with relative paths, resource kinds, existence state, and file sizes when available. Dynamic command and MCP tool-call results include the same transaction and resource-change metadata immediately in `structuredContent.transaction`. Mutations that capture preimages write before-change snapshots under `Transactions/Snapshots/<transaction-id>/`; `rekall.transaction.restore_preimage` restores a selected resource from one of those snapshots while capturing the current file as the new transaction preimage.

## Scene Runtime Foundation

Inspect a deterministic runtime snapshot without mutating authoring files:

```powershell
dotnet run --project src/Rekall.Age.Cli -- runtime inspect .age-sandbox Main 3
dotnet run --project src/Rekall.Age.Cli -- runtime inspect .age-sandbox Main 1 '[{"pressedKeys":["W"],"pressedKeysThisFrame":["W"]}]'
```

The command reports entity counts, renderable counts, physics/audio/animation/UI readiness, projected input actions, and structured runtime observations for agents and Studio. The optional final JSON argument is a per-frame array of runtime input frames, with fields such as `pressedKeys`, `pressedKeysThisFrame`, `releasedKeysThisFrame`, `pressedButtons`, `mouseX`, `mouseY`, `mouseDeltaX`, `mouseDeltaY`, and `mouseWheelDelta`; the argument can also be a path to a JSON file.

## Runtime Viewport Capture

Capture a deterministic viewport PNG from the same runtime snapshot used by inspection and Studio metadata:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport 320 180 vulkan
```

The command writes `Main_runtime_003.png` and reports the active camera, frame index, renderable kinds, asset-backed renderable count, fallback renderable count, and runtime observation count.

`Rekall.Camera2D` and `Rekall.Camera3D` are core configurable camera components. They remain plain entity components so project modules can drive them like any other authored data. Runtime rendering carries active state, projection mode, field of view, orthographic size, near/far clip distances, clear color, and transform into the viewport frame; Vulkan scene capture consumes those camera settings for view/projection, while software and Vulkan clear captures honor the active camera clear color. `Rekall.CameraTarget3D` can be added to a camera entity to follow and/or look at any target entity by id, name, or tag with configurable camera and target offsets. `Rekall.CameraZoomInput` can be added to a camera entity to opt into mouse-wheel zoom with configurable speed, orthographic/FOV clamps, and inverted-wheel behavior. The default runtime applies these after orbit, physics, and project-authored motion systems so the camera sees the final frame state.

`Rekall.InputActionMap` is the core semantic input component. Its `actions` property is an array of arbitrary named bindings such as `{ "name": "thrust", "key": "W" }`, `{ "name": "strafe", "positiveKey": "D", "negativeKey": "A" }`, `{ "name": "fire", "button": "Left" }`, `{ "name": "zoom", "mouseWheelScale": 0.5 }`, or `{ "name": "lookX", "mouseAxis": "x", "mouseScale": 0.12 }`. The Windows Vulkan player captures keyboard state, mouse button state, mouse position/delta, and wheel movement from Veldrid. The default runtime projects authored action maps into `world.Subsystems.Input.Actions` with `value`, `isDown`, `wasPressed`, and `wasReleased`. Engine systems and project-authored module systems can read raw per-frame input from `RekallAgeRuntimeWorldFrameContext.Input` and `RekallAgeRuntimeModuleFrameContext.Input`, or use module SDK helpers such as `world.InputActionValue("moveX")`, `world.IsInputActionDown("fire")`, and `world.WasInputActionPressed("interact")`. Genre behavior belongs in agent-authored modules; the engine input layer stays generic.

Runtime entities carry generic tags and components as first-class authoring data. Project-authored modules can query them through SDK helpers such as `world.FindEntity("player")`, `world.EntitiesNamed("Door")`, `world.EntitiesWithTag("enemy")`, `world.EntitiesWithComponent("Game.Health")`, and `world.EntitiesWithTagAndComponent("interactable", "Game.Locked")`. Query results are deterministic, so AI agents can coordinate arbitrary game logic around authored labels and component contracts without requiring engine-owned gameplay classes. Modules can also apply ordinary entity mutations through helpers such as `world.ReplaceEntity(entity)`, `world.UpdateEntity("player", entity => entity.WithTag("selected"))`, `world.UpdateEntitiesWithTag("enemy", entity => entity.WithVisible(false))`, and `world.UpdateEntitiesWithComponent("Game.Health", entity => entity.WithoutTag("active"))`, keeping runtime systems concise while preserving immutable world updates.

Project-authored modules can emit structured runtime diagnostics with SDK helpers such as `world.EmitObservation(entity, "GAME_LOCKED_DOOR_NO_KEY", "warning", "gameplay", "DoorSystem", "Door is locked but no key entity was authored.")` and `world.EmitSceneObservation("GAME_NO_SPAWN_POINTS", "blocking", "gameplay", "SpawnSystem", "No spawn points were authored.")`. Observations flow through runtime inspection, Studio, and viewport diagnostics alongside built-in engine observations; modules can query them with `world.ObservationsWithCode(...)`, `world.ObservationsFor(entity.Id)`, `world.ObservationsWithSeverity(...)`, `world.ObservationsForScene()`, and `world.HasBlockingObservations()`. This gives agents a shared way to explain missing or inconsistent authored content without hard-coding gameplay validation into engine core.

Multiplayer authoring remains component-driven and inspectable. `Rekall.MultiplayerSession`, `Rekall.NetworkIdentity`, and `Rekall.NetworkTransform` project into `world.Subsystems.Multiplayer`, while module SDK helpers such as `world.NetworkSessions()`, `world.PrimaryNetworkSession()`, `world.NetworkEntities()`, `world.NetworkEntityForEntity(entity.Id)`, `world.NetworkEntityByNetworkId("ship-1")`, `world.NetworkEntitiesOwnedBy("client-a")`, `world.RuntimeEntitiesOwnedBy("client-a")`, `world.ReplicatedRuntimeEntities()`, `world.IsNetworkOwner(entity, "client-a")`, and `networkEntity.IsReplicated()` let agents coordinate ownership, prediction, replication, and diagnostics without binding engine core to a specific multiplayer genre. Authoritative snapshots carry each network entity's transform plus authority, replicate-position/rotation/scale flags, prediction mode, and priority; interpolation keeps blended transform values while using the newer authoritative metadata. `RekallAgeMultiplayerSnapshotApplier.Apply(world, snapshot)` can apply a server snapshot to a client runtime world while respecting those replicate flags and advancing the client's frame/time to the authoritative tick. `RekallAgeMultiplayerSnapshotDeltaBuilder.Build(previous, next)` and `.Apply(previous, delta)` provide a generic changed/removed-entity delta primitive; running sessions expose the same primitive through the `rekall.multiplayer.delta` command and `multiplayer delta <root> <scene> <fromServerTick>` CLI path. The current runtime provides a tested server-authoritative substrate with local pipe and WebSocket command transports; production-grade UDP, lobbies, matchmaking, NAT traversal, interest management, and continuous client snapshot streaming are future layers over the same generic contracts.

`Rekall.EventBindings` is the core runtime event component. Its `events` property is an array of generic bindings such as `{ "event": "entity.begin", "handler": "spawnEncounter" }`, `{ "event": "entity.tick", "handler": "patrol" }`, `{ "event": "timer.elapsed", "handler": "spawnWave" }`, or `{ "event": "pointer.click", "handler": "activate" }`. The runtime emits matching facts into `world.Subsystems.Events.Events` with frame, type, entity, source, handler, and payload data; project-authored modules can query them with SDK helpers such as `world.EventsOfType("entity.tick")`, `world.EventsFor(entity.Id)`, and `world.WasEventRaised(entity.Id, "entity.begin")`. Modules can also emit arbitrary custom facts with `world.EmitEvent(entity, "quest.started", "game.quest", "beginIntro")` or emit only matching authored bindings with `world.EmitBoundEvents(entity, "score.changed", "game.score", payload)`. These are agent-facing signals rather than engine-owned gameplay methods, so agents can implement arbitrary behavior behind handler names. Runtime inspection also reports `eventCount` and the current frame's events so agents can verify bindings without writing custom module code.

`Rekall.PointerRay` is the core interaction-ray component. Agents can attach it to any visible entity and author a world-space ray with `pointerId`, `originX/Y/Z`, `directionX/Y/Z`, `range`, `button`, and optional `targetTag` or `targetComponentType` filters. The default runtime tests that ray against 3D colliders and emits bound target events such as `pointer.enter`, `pointer.leave`, `pointer.hit`, `pointer.down`, `pointer.up`, and `pointer.click`; it also stores inspectable `Rekall.PointerState` with the current hovered/pressed entity ids. This gives mouse, gaze, XR hand, editor tool, menu, and game-specific interaction modules a shared primitive without baking any one control scheme into the engine.

`Rekall.Timer` is the core runtime clock component. Agents can author a `timerId`, `durationSeconds`, and `repeat` flag on any visible entity; the runtime advances it during fixed-step simulation, emits `timer.elapsed` when the duration is reached, and stores inspectable `Rekall.TimerState` with elapsed seconds and completion count. This covers cooldowns, spawn pacing, delayed quest beats, animation cues, periodic sensors, and other timing needs without engine-owned behavior.

The default runtime also emits generic 3D collision facts after physics has updated entity transforms. Entities with 3D colliders can bind `collision.begin`, `collision.stay`, and `collision.end` through `Rekall.EventBindings`; the event payload names the other entity and collider types. The runtime stores inspectable `Rekall.CollisionState` overlap ids on collider entities so begin/stay/end can be derived deterministically across frames. Damage, pickups, triggers, landing, area rules, sensors, and puzzle behavior stay in agent-authored modules that consume those facts.

`Rekall.Trigger` is the core non-physical volume component for area facts. Agents can author sphere or box trigger volumes with `shape`, `radius`, `width`, `height`, `depth`, and optional `targetTag` or `targetComponentType` filters. The runtime emits `trigger.enter`, `trigger.stay`, and `trigger.exit` on the trigger entity and stores inspectable `Rekall.TriggerState` occupant ids. This keeps zones, sensors, checkpoints, quest areas, dialogue regions, proximity prompts, and other area semantics as agent-authored behavior over generic facts.

Viewport capture results include frame-analysis diagnostics so agents can distinguish a technically nonblank capture from a useful visual proof. The analysis reports color diversity, dominant-color ratio, luminance statistics, and warning codes such as `REKALL_VIEWPORT_FLAT_COLOR`, `REKALL_VIEWPORT_DOMINATED_BY_ONE_COLOR`, `REKALL_VIEWPORT_LOW_LUMINANCE_VARIANCE`, `REKALL_VIEWPORT_VERY_DARK`, and `REKALL_VIEWPORT_NEARLY_TRANSPARENT`.

The default viewport backend is `software`, which is CPU rasterized. Passing `vulkan` requests native Vulkan capture. Empty runtime frames use the native clear-pass/readback path and report the selected device. Generated primitive mesh scenes and authored `Rekall.GeometryMesh` triangle meshes use native offscreen Vulkan scene rendering: Rekall AGE ships the bundled GLSL shaders beside the rendering assembly, compiles them with Shaderc, creates color and depth targets, uploads local-space vertex/index/uniform buffers, propagates active camera transforms into the view-projection uniform, binds per-mesh model matrices through push constants, submits indexed draw calls, copies the color image to a host buffer, and writes the captured PNG. Vulkan materials support base color, normal, metallic-roughness, occlusion, and emissive texture/factor inputs. Unsupported renderable kinds, such as sprites and imported mesh assets, fail with structured diagnostics instead of falling back silently.

If a sprite renderable references an imported PNG asset, the software viewport draws that PNG into the frame. Missing or unsupported sprite assets fall back to deterministic markers and are reported in the command output.

Primitive mesh renderables, including `rekall.primitive.cube`, and authored `Rekall.GeometryMesh` triangle lists are projected into the software viewport as shaded geometry. `Rekall.DirectionalLight` entities contribute deterministic directional lighting from their `Rekall.Transform3D` rotation and `intensity` value. `Rekall.TransformAnimation` can apply `pitchDegreesPerSecond`, `yawDegreesPerSecond`, and `rollDegreesPerSecond` during runtime frame simulation before capture.

Agents can author renderable 3D geometry through `rekall.geometry.create_primitive` / `geometry primitive create`. Supported primitives are `cube`, `sphere`, `cylinder`, `cone`, and `plane`; the command writes `Rekall.Transform3D`, `Rekall.GeometryPrimitive`, and `Rekall.MeshRenderer` components so the same runtime viewport capture path can render the object immediately.

For high-throughput world creation over MCP, agents can use `rekall.scene.apply_blueprint` to apply many generic entities and components to a scene in one transaction, optionally clearing existing scene entities first. `rekall.entity.delete` removes one authored entity during iteration without requiring a full scene rewrite.

Agents can import Kitten Space Agency astronomical data through `rekall.solar.import_ksa_system` / `solar import-ksa-system <projectRoot> <sceneName> <ksaRoot> [systemFileName] [distanceScale] [radiusScale]`. The importer reads `Content/Core/Astronomicals.xml` plus a KSA system XML such as `SolSystem.xml` or `SolSystemDense.xml`, resolves `LoadFromLibrary` references, copies available diffuse KTX2 texture assets, and writes generic `Rekall.CelestialBody`, `Rekall.KeplerOrbit`, `Rekall.OrbitPathRenderer`, `Rekall.PlanetRenderer`, `Rekall.AtmosphereRenderer`, `Rekall.Material`, `Rekall.Transform3D`, camera, and light entities. Stellar bodies receive emissive material settings and point-light behavior by default; runtime projection also treats visible `Rekall.CelestialBody` entities with a stellar `type` as point lights when no explicit light component exists. Sol is normalized to a warm orange-yellow display/emissive color. Runtime snapshots run the built-in `runtime.celestial.kepler` system before project-authored gameplay systems so solar-system scenes can use real orbital elements while custom launch, travel, navigation, UI, and mission logic remains project module code. `Rekall.KeplerOrbit` supports `timeScale`, parent-body nesting, and readable local satellite scaling, so moons orbit their updated planets in the same frame instead of lagging behind or collapsing into the scaled planet mesh. `Rekall.OrbitPathRenderer` generates emissive orbit-path geometry for any Kepler body, including moon orbits around planets.

`Rekall.Material` is the generic render material component for mesh-like entities. It can override `baseColor`, `baseColorTexture`, `metallicFactor`, `roughnessFactor`, `metallicRoughnessTexture`, `normalTexture`, `normalScale`, `occlusionTexture`, `occlusionStrength`, `emissiveColor`, `emissiveTexture`, and `emissiveStrength`. This keeps special cases like glowing suns, lamps, engine trails, screens, and magic effects in ordinary agent-authored entity data rather than in game-specific engine code.

`Rekall.ProceduralMaterial` is the generic runtime procedural material component. Agents can add it beside any mesh-like renderer to generate deterministic PBR texture channels without importing image assets. Supported generators are `checker`, `stripes`, `rings`, and `noise`, with `resolution`, `scale`, `seed`, `baseColorA`, `baseColorB`, `metallicFactor`, `roughnessA`, `roughnessB`, `normalStrength`, and `emissiveStrength` properties. Explicit texture asset references on `Rekall.Material` still win per channel, so authored content can mix imported base-color textures with procedural metallic-roughness, normal, or emissive channels.

## Live Player Editing

The Windows player starts a local named-pipe live-edit server for the loaded project and scene. MCP agents can target that running session without restarting the player:

- `rekall.live.status` reports the session id, pipe name, frame index, entity/renderable counts, and scene/asset revisions.
- `rekall.live.apply_scene_blueprint` applies generic entity/component blueprints to the running player, optionally persists them to scene storage, and can reload runtime assets in the same request.
- `rekall.live.apply_scene_diff` applies generic upsert/delete/clear scene diffs to the running player, optionally persists them to scene storage, and can reload runtime assets in the same request.
- `rekall.live.reload_scene` reloads the scene document from disk and rebuilds the runtime world.
- `rekall.live.reload_assets` resolves the current runtime frame's assets again and recreates player-side GPU texture/material bindings.

The default pipe name is deterministic for `<projectRoot, sceneName>`, so tools only need the project root and scene name while the player is open. Advanced callers can pass `pipeName` explicitly when they need to target a particular session. Mutations are queued onto the player render thread before being applied, which keeps GPU resource replacement and runtime-world swaps serialized with rendering. The player also watches the project `Assets` tree and debounces filesystem changes into an automatic asset/GPU-binding reload.

Agents can also author arbitrary triangle meshes through `rekall.geometry.create_mesh` / `geometry mesh create`, or directly with a `Rekall.GeometryMesh` component. The runtime frame contract carries the parsed vertex/index payload into the Vulkan scene renderer. Vertices support `x`, `y`, `z`, optional normals through `nx`, `ny`, `nz`, optional vertex color through `r`, `g`, `b`, `a`, and optional `u`, `v` texture coordinates. Indices are 16-bit triangle-list indices. If normals are omitted, Rekall AGE infers averaged per-vertex normals from the triangle winding so simple generated meshes still light correctly.

For higher-level modeling, agents can use `rekall.geometry.create_extrusion` / `geometry extrusion create` with a 2D profile and depth. The command generates a closed hard-edged mesh with front/back caps and side faces, writes editable `Rekall.GeometryExtrusion` source metadata, and emits the renderable `Rekall.GeometryMesh` payload used by both software and Vulkan viewport capture.

Runtime-renderable 3D scenes can be exported as binary glTF through `rekall.render.export_scene_glb` / `render glb export <projectRoot> <sceneName> <output.glb> [frames]`. The exporter resolves the same runtime snapshot used by viewport capture, converts supported engine-authored mesh renderables through the engine mesh builder, and writes a GLB 2.0 file with embedded binary buffers, node transforms, mesh accessors for `POSITION`, `NORMAL`, `TEXCOORD_0`, and `COLOR_0`, triangle indices, PBR base-color materials, and embedded PNG/JPEG base-color textures when a mesh or primitive component references an imported image asset through `textureAssetId` or `texture`. Mesh renderables that reference imported `.glb` model assets are merged into the exported scene with their source nodes, meshes, materials, material-extension texture references, images, textures, samplers, cameras, skins, animations, and extension declarations reindexed into the output, then wrapped by the engine entity transform.

```json
{
  "type": "Rekall.GeometryMesh",
  "properties": {
    "color": "#ff6633",
    "textureAssetId": "asset_paint_0123abcd",
    "vertices": [
      { "x": 0, "y": 0, "z": 0, "nx": 0, "ny": 0, "nz": 1 },
      { "x": 1, "y": 0, "z": 0 },
      { "x": 0, "y": 1, "z": 0, "r": 0, "g": 1, "b": 0, "a": 1 }
    ],
    "indices": [0, 1, 2]
  }
}
```

Compiled project modules can also register `IRekallAgeRuntimeModuleSystem` implementations through `RekallAgeModuleBuilder.RegisterRuntimeSystem<T>()`. Runtime inspection and viewport capture load built project modules, run those systems during fixed-frame simulation, and expose the executed system IDs in runtime inspection output.
