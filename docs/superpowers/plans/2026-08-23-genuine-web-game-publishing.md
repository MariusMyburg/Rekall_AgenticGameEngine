# Genuine AGE Web Game Publishing Implementation Plan

> **Execution rule:** Implement this plan test-first in small commits. Do not
> replace failed scene rendering with completed CPU-frame upload, JavaScript
> gameplay, prerecorded media, remote streaming, or platformer-specific code.

**Goal:** Publish an ordinary AGE project so its existing scene, assets, and
agent-authored C# runtime modules execute in browser WASM and render directly
through AGE's WebGPU RenderingDevice.

**Design:**
[`2026-08-23-genuine-web-game-publishing-design.md`](../specs/2026-08-23-genuine-web-game-publishing-design.md)

**Controlling acceptance consumer:** the independently accepted Clockwork Canopy
project, with its exact authored module and scene behavior unchanged.

---

## Task 1: Add static/AOT project-module registration

**Files:**

- Modify: `src/Rekall.Age.Runtime/RekallAgeProjectRuntimeSystemLoader.cs`
- Create: `src/Rekall.Age.Runtime/RekallAgeRuntimeModuleRegistration.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/ProjectRuntimeSystemTests.cs`

1. Add failing tests proving registered module `Type` values configure and
   instantiate the same runtime systems, in the same priority/ID order, as the
   existing filesystem assembly loader.
2. Add failures for null, non-module, abstract, duplicate, or unconstructable
   registrations with stable diagnostic codes/messages.
3. Extract the current module-to-system adaptation into one shared path.
4. Add `CreateDefault(IEnumerable<Type> moduleTypes)` without weakening the
   existing desktop `CreateDefault(projectRoot)` path.
5. Run the targeted tests and commit.

## Task 2: Decouple shipped content reads from operating-system paths

**Files:**

- Create: `src/Rekall.Age.Project/IRekallAgeGameContent.cs`
- Create: `src/Rekall.Age.Project/RekallAgeFileGameContent.cs`
- Create: `src/Rekall.Age.Project/RekallAgeMemoryGameContent.cs`
- Create: `src/Rekall.Age.World/RekallAgeSceneCodec.cs`
- Modify: `src/Rekall.Age.World/RekallAgeSceneStore.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeWorldBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Project/GameContentTests.cs`
- Test: `tests/Rekall.Age.Tests/World/SceneCodecTests.cs`

1. Write failing tests for normalized logical paths, bounded reads, missing
   content, traversal rejection, exact bytes, and cancellation.
2. Write scene-codec parity tests proving filesystem and memory content produce
   equal validated `RekallAgeSceneDocument` values and reject the same malformed,
   oversized, deep, incompatible, or wrong-name documents.
3. Implement a minimal read-only logical-content contract. Keep mutable
   authoring stores filesystem-based.
4. Extract serialization and required-shape validation from the scene store into
   the codec and make the store delegate to it.
5. Add runtime/frame-builder asset-resolution overloads that consume logical
   content; preserve current project-root overloads as adapters.
6. Run Project, World, Runtime, and rendering viewport tests; commit.

## Task 3: Define the hashed web-game artifact and exporter

**Files:**

- Create: `src/Rekall.Age.Workflows/Web/RekallAgeWebGameManifest.cs`
- Create: `src/Rekall.Age.Workflows/Web/RekallAgeWebGameManifestCodec.cs`
- Create: `src/Rekall.Age.Workflows/Web/RekallAgeWebModuleRegistryGenerator.cs`
- Create: `src/Rekall.Age.Workflows/Commands/PublishWebGameCommand.cs`
- Create: `tests/Rekall.Age.Tests/Workflows/WebGamePublishingTests.cs`

1. Write failing manifest tests covering deterministic ordering, engine/schema/
   SDK identity, entry scene, module IDs, logical paths, media types, sizes,
   SHA-256 hashes, required rendering capabilities, and build identity.
2. Write failing export preflight tests for missing scenes, module build failure,
   incompatible SDK, unsafe output overlap, traversal/reparse paths, unsupported
   required capabilities, and undeclared asset dependencies.
3. Implement deterministic dependency closure from the entry scene and project
   metadata. Do not copy editor recovery, transactions, `bin`, `obj`, or unrelated
   source files into the site.
4. Generate an isolated WASM build project with `ProjectReference`s to the exact
   authored module projects and a generated registry that statically references
   every exportable module type. Validate that the compiled module identity and
   SDK ABI match the authored desktop build.
5. Invoke `dotnet publish` non-interactively into staging, copy validated hashed
   game content, atomically publish the final directory, and return structured
   evidence and next actions.
6. Prove identical inputs produce identical logical manifests/build identities
   apart from explicitly recorded toolchain artifacts; commit.

## Task 4: Add web manifest content loading

**Files:**

- Create: `src/Rekall.Age.Player.Web/RekallAgeWebGameContent.cs`
- Create: `src/Rekall.Age.Player.Web/RekallAgeWebGameBootstrap.cs`
- Modify: `src/Rekall.Age.Player.Web/Rekall.Age.Player.Web.csproj`
- Test: `tests/Rekall.Age.Tests/Workflows/WebGamePublishingTests.cs`
- Create: `src/Rekall.Age.Player.Web/tests/web-game-content.test.mjs`

1. Write C# and browser-protocol tests for manifest fetch, content fetch, bounded
   sizes, hash mismatch, missing entry, cancellation, caching, and diagnostics.
2. Add required project references to World, Project, Runtime, Modules, and the
   package manifest contract.
3. Implement `HttpClient`-backed logical content that accepts only manifest
   entries and verifies SHA-256 before returning bytes.
4. Bootstrap the entry scene through `RekallAgeSceneCodec`, build the runtime
   world, and create the default execution loop using generated module types.
5. Publish loaded project/scene/module/build identities in machine-readable host
   status; commit.

## Task 5: Build a direct RenderingDevice scene renderer

**Files:**

- Create: `src/Rekall.Age.Rendering/RekallAgeRenderingDeviceSceneRenderer.cs`
- Create: `src/Rekall.Age.Rendering/RekallAgeRenderingDeviceSceneResources.cs`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_scene.wgsl`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_ui.wgsl`
- Test: `tests/Rekall.Age.Tests/Rendering/RenderingDeviceSceneRendererTests.cs`
- Modify: `src/Rekall.Age.Player.Web/Program.cs`

1. Start with failing in-memory-device tests that build real runtime viewport
   frames and require camera uniforms, color/depth targets, vertex/index/instance
   buffers, texture/sampler bindings, render passes, draw commands, and present.
2. Cover every capability used by the accepted game: its camera projection,
   geometry primitives/compiled meshes, transforms, material color/texture,
   depth and alpha, directional light, visibility/order, UI canvas, and labels.
3. Reuse mesh/frame/material projection contracts from the Vulkan scene path;
   extract shared backend-neutral batching where this removes duplication.
4. Implement persistent pipeline/resource caches and per-frame dynamic uploads.
   Key assets by declared content hash and recreate only size-dependent targets
   on resize.
5. Rasterize glyphs only into a reusable glyph atlas texture; never rasterize the
   completed scene/frame for WebGPU upload.
6. Emit stable diagnostics for required unsupported facts, invalid assets, device
   loss, and resource limits. No silent software presentation path.
7. Execute the same renderer against `RekallAgeWebGpuRenderingDevice`; preserve
   the low-level triangle proof as a device conformance test, not the player UI.
8. Run RenderingDevice, WebGPU protocol, renderer, and browser JavaScript tests;
   commit.

## Task 6: Bridge browser devices into generic AGE input

**Files:**

- Create: `src/Rekall.Age.Player.Web/RekallAgeWebInputBridge.cs`
- Create: `src/Rekall.Age.Player.Web/wwwroot/web-input.js`
- Modify: `src/Rekall.Age.Player.Web/wwwroot/main.js`
- Modify: `src/Rekall.Age.Player.Web/Program.cs`
- Create: `src/Rekall.Age.Player.Web/tests/web-input.test.mjs`
- Test: `tests/Rekall.Age.Tests/Runtime/WebInputBridgeContractTests.cs`

1. Write tests for held/pressed/released keyboard facts, pointer coordinates and
   scaling, deltas, wheel, buttons, pointer capture, focus-loss release, touch,
   gamepad identity/axes/buttons, canvas size, and one-shot edge consumption.
2. Keep JavaScript values raw and device-semantic. Do not map keys to gameplay
   meanings in JavaScript.
3. Convert snapshots into `RekallAgeRuntimeInputState` in C# and prove an ordinary
   `Rekall.InputActionMap` yields identical semantic action samples to Windows
   input fixtures.
4. Add resize, visibility, pause/resume, fullscreen, and device-loss lifecycle
   facts with stable diagnostics; commit.

## Task 7: Run the browser simulation/presentation loop

**Files:**

- Create: `src/Rekall.Age.Player.Web/RekallAgeWebPlayer.cs`
- Modify: `src/Rekall.Age.Player.Web/Program.cs`
- Modify: `src/Rekall.Age.Player.Web/wwwroot/index.html`
- Modify: `src/Rekall.Age.Player.Web/wwwroot/main.js`
- Create: `src/Rekall.Age.Player.Web/tests/web-player-loop.test.mjs`

1. Write deterministic host tests for request-animation-frame time, fixed-step
   catch-up, clamping, zero-step render frames, input edge consumption, pause,
   resume, and resize.
2. Advance `RekallAgeRuntimeSimulationClock` with browser input, build the latest
   viewport frame, submit direct scene work, and present once per visual frame.
3. Expose bounded read-only debug status: build/manifest/scene/module identity,
   frame/time/input sequence, rendered entity/draw counts, and diagnostics.
4. Replace proof-page wording only after a real project boots; keep startup
   failures explicit and actionable.
5. Run `dotnet publish` for browser-wasm and all JS tests; commit.

## Task 8: Expose publish/audit through CLI, MCP, and Studio

**Files:**

- Create: `src/Rekall.Age.Workflows/Commands/AuditWebGameCommand.cs`
- Modify: `src/Rekall.Age.Workflows/RekallAgeDefaultCommandRegistry.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioWindow.xaml`
- Test: `tests/Rekall.Age.Tests/Cli/WebGameCliTests.cs`
- Test: `tests/Rekall.Age.Tests/Mcp/McpCatalogTests.cs`
- Test: `tests/Rekall.Age.Tests/Workflows/WebGamePublishingTests.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioWebPublishingTests.cs`

1. Register `rekall.game.publish_web` and `rekall.game.audit_web` with typed,
   bounded schemas and actionable next steps.
2. Add CLI `game publish-web` / `game audit-web` and Studio Publish Web / Audit
   Web actions using the same commands.
3. Audit hashes, compatibility, module registry, capability coverage, relocation,
   static-server boot, runtime identity, and a browser smoke frame.
4. Prove MCP discovery exposes the generic tools to agents without adding
   platformer-specific authoring tools; commit.

## Task 9: Accept Clockwork Canopy unchanged in the browser

**Files:**

- Promote after Windows acceptance: `Examples/ClockworkCanopy/**`
- Create: `eng/accept-clockwork-canopy-web.tests.ps1`
- Create: `docs/production/clockwork-canopy-web-acceptance.md`

1. Freeze the accepted Windows project revision and record scene/module/content
   hashes. Do not edit its behavior to make the browser test easier.
2. Publish it through the ordinary command, audit it, relocate it, and serve the
   relocated static directory.
3. In the real in-app browser, verify that reported project/scene/module hashes
   match the Windows acceptance project.
4. Exercise strict semantic input/state checks for movement, jump, grounding,
   collectible/score, hazard/death/respawn, goal/win, camera/HUD, and reset/replay.
5. Inspect current screenshots and manually play the game. Repair generic engine,
   renderer, content, or input defects; rerun the intended assertions without
   weakening them.
6. Run targeted suites, Release solution build, web publish, JavaScript tests,
   web package audit, relocation, and the browser acceptance script.
7. Update `PROGRESS.md` with exact evidence, commit, push `master`, and replace the
   old moving-dot server with the accepted relocated game.

## Deferred follow-on platform work

After this vertical slice, retain the same architecture while adding WebAudio,
versioned browser saves, WebSocket multiplayer, offline/PWA policy, threading
headers/profiles, richer custom materials/post-processing, shadows, particles,
and complete 3D parity. Each addition must be driven by an accepted ordinary AGE
game and must remain visible to agents through inspectable generic contracts.
