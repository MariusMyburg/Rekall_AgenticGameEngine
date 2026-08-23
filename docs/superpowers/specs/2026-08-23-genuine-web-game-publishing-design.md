# Genuine AGE Web Game Publishing Design

**Date:** 2026-08-23  
**Status:** Accepted by standing product direction  
**Scope:** Export and run an ordinary Rekall AGE game in a modern browser

## Goal

Publish the same AGE project that runs on Windows as a static browser game. The
browser build executes the project's agent-authored C# gameplay in .NET WASM,
loads the project's scene and assets, translates browser devices into AGE's
generic input contracts, and renders the runtime scene directly through AGE's
WebGPU RenderingDevice.

This is a normal platform target, comparable in product meaning to a Godot web
export. It is not a fallback renderer, a streamed desktop player, a JavaScript
rewrite, a proof animation, or a CPU-rendered image uploaded to the GPU.

## Product invariants

1. **One game, multiple platform hosts.** World state, scene interpretation,
   gameplay systems, semantic actions, timing, and rendering projection remain
   shared C# engine contracts. Windows and web differ only at platform seams.
2. **Authored C# remains authored C#.** Export builds the project's existing
   module projects into the WASM application. It does not translate their
   behavior into JavaScript or require a second browser gameplay implementation.
3. **Direct GPU rendering.** AGE turns the projected runtime world into vertex,
   index, uniform, texture, sampler, shader, pipeline, binding, render-pass, and
   draw commands submitted through `IRekallAgeRenderingDevice`. The browser
   bridge maps those commands to WebGPU.
4. **Thin JavaScript platform seam.** JavaScript may expose WebGPU, WebAudio,
   keyboard, pointer, touch, gamepad, resize/focus/fullscreen, storage, and
   networking APIs to .NET. It must not own game rules, world state, collision,
   camera composition, or entity rendering decisions.
5. **Generic export.** No platformer-specific engine or exporter behavior is
   permitted. The first platformer is an acceptance consumer of ordinary
   components and modules.
6. **Evidence is behavioral.** Loading a page or drawing pixels is insufficient.
   Browser input must change inspectable AGE runtime state, and the game must be
   played and visually reviewed in a real browser.

## Existing foundation to reuse

- `RekallAgeRuntimeWorldBuilder`, `RekallAgeRuntimeExecutionLoop`, and
  `RekallAgeRuntimeSimulationClock` already provide shared deterministic C# world
  construction and fixed-step execution.
- `RekallAgeRuntimeRenderFrameBuilder` already projects a runtime world into
  backend-independent camera, sprite, mesh, light, UI, text, material, and
  post-process facts.
- `IRekallAgeRenderingDevice` already represents buffers, textures, samplers,
  shaders, binding layouts/sets, pipelines, render targets, command encoders,
  submission, and presentation.
- `RekallAgeWebGpuRenderingDevice` and its browser bridge already execute that
  contract against real WebGPU and have device-level conformance evidence.
- `RekallAgeRuntimeGpuWorkloadCompiler` already compiles agent-authored generic
  GPU workloads through the same RenderingDevice.
- `RekallAgeRuntimeInputState` and `RekallAgeInputActionSystem` already normalize
  raw device facts into semantic actions used by gameplay modules.

The missing product path is orchestration and scene rendering, not a new engine.

## Export artifact

`rekall game publish-web <projectRoot> <sceneName> <outputDirectory>` produces a
relocatable static site containing:

- the .NET WASM runtime and AGE web player assemblies;
- the project's existing module projects compiled into the WASM application;
- a generated C# module registry that statically roots module and runtime-system
  types for trimming/AOT safety;
- `game.manifest.json`, including engine/schema/SDK compatibility, entry scene,
  viewport policy, content entries, sizes, media types, and SHA-256 hashes;
- canonical scene JSON and required content-addressed assets;
- WebGPU shaders and pipeline metadata selected by the scene renderer;
- the thin browser interop JavaScript and player shell;
- package audit evidence and a deterministic build identity.

The exporter discovers ordinary module projects from the project manifest/module
layout and generates a temporary build project. That project adds the modules as
`ProjectReference`s and emits a registry such as `Type[] ModuleTypes`. The shared
runtime loader gains an overload that consumes registered module types, invokes
their normal `Configure` methods, instantiates their registered runtime systems,
and applies the same ordering used on desktop. Filesystem assembly discovery
remains the desktop authoring path; static registration is the browser/AOT path.

The generated build project is an implementation detail in an isolated staging
directory. It does not copy or rewrite gameplay source into AGE's repository, and
the final static site contains only runtime output and declared game content.

## Content loading

Runtime-readable content must not be coupled to `System.IO` paths. Introduce a
small read-only `IRekallAgeGameContent` contract with normalized logical paths,
bounded byte/text reads, existence checks, and declared metadata. Implement it
for:

- a validated filesystem project/package on desktop;
- the hashed web manifest using `HttpClient`/static asset fetches in WASM;
- in-memory fixtures for deterministic tests.

Scene serialization/deserialization moves behind a reusable codec used by both
`RekallAgeSceneStore` and web content loading. The same schema, size/depth bounds,
required-shape validation, and compatibility checks apply on every platform.
Asset resolvers consume logical content rather than assuming an operating-system
path. Mutable authoring stores remain filesystem-based; the shipped player needs
only the read-only content interface.

At startup the web host fetches and validates the manifest, checks the entry
scene hash before parsing it, validates compatibility, loads the scene, builds
the runtime world, creates the statically registered project systems, and starts
the fixed-step simulation clock. Asset bytes are hash-checked on first use and
cached for the session.

## Browser runtime and input

The browser host owns one animation loop driven by `requestAnimationFrame`.
Elapsed browser time advances `RekallAgeRuntimeSimulationClock`; the clock runs
zero or more fixed steps and clamps pathological catch-up according to the same
explicit runtime policy used by other hosts. Rendering happens once per presented
frame from the latest projected world.

The interop layer tracks:

- held, pressed-this-frame, and released-this-frame keyboard codes;
- pointer position, delta, buttons, wheel, capture, and canvas-relative scale;
- touch contacts represented through generic pointer facts;
- connected gamepads with stable IDs, axes, buttons, hats where available, and
  player indices;
- canvas pixel dimensions, device-pixel ratio, focus, visibility, and resize.

.NET snapshots these values into `RekallAgeRuntimeInputState`. Existing semantic
action maps then produce the same named actions used on Windows. Edge facts are
consumed once; held facts persist. Losing focus releases held input to prevent
stuck controls. No JavaScript key-to-game-action mapping is allowed.

## Direct WebGPU scene renderer

Add a renderer that consumes `RekallAgeRuntimeViewportFrame` and records direct
RenderingDevice work. It is shared in architecture with the Vulkan scene path,
but backend-neutral at the command boundary.

The first complete slice supports every scene capability used by the accepted
platformer, whether the author chose a 2D or 3D presentation. At minimum the
generic renderer therefore covers:

- active `Camera2D` and perspective camera projection, clear/background,
  viewport, depth, and resize;
- colored and textured sprites, transforms, depth/order, alpha blending, and
  visibility;
- batched quad geometry with per-instance transform/color/UV data;
- primitive and compiled mesh geometry, model/view/projection transforms,
  materials, depth testing, and the ordinary directional-light path;
- texture creation and cache keyed by content hash;
- UI panels/images and labels, with a GPU glyph atlas generated or cooked as an
  asset rather than a completed CPU framebuffer;
- deterministic resource lifetime, resize recreation, diagnostics, and device
  loss reporting.

The renderer owns long-lived pipelines and caches, updates dynamic instance and
uniform buffers each frame, imports the current browser canvas output, records a
render pass, submits, and presents. CPU work may decode source images, cook mesh
or font data, and populate GPU resources; it may never rasterize the completed
frame for upload as the shipping render path.

Subsequent generic increments add shadows, additional light types, particles,
render-graph attachments, advanced custom material shaders, and post-processing
through the same contract. Unsupported scene facts emit stable diagnostics and
fail package capability audit when required by the selected entry scene; they do
not silently switch to a software presentation path.

Agent-authored `Rekall.GpuWorkload` content executes through
`RekallAgeRuntimeGpuWorkloadCompiler` on the same WebGPU device. Scene composition
and custom GPU workloads therefore share resources and scheduling rules rather
than becoming separate browser engines.

## Audio and storage seams

WebAudio consumes the same runtime audio intent/mix output used by the engine.
Browser autoplay policy is surfaced as an actionable `audio-awaiting-user-gesture`
state, then resumes audio on a generic user gesture. Web storage exposes bounded,
versioned save slots to C#; game modules use engine storage contracts and do not
call JavaScript directly.

These are required for a production platform target, but they follow the first
playable graphics/input slice unless the selected game requires audio for its
acceptance criteria.

## Diagnostics and package audit

The player publishes a machine-readable status object containing build identity,
manifest identity, entry scene, runtime frame index, simulation time, loaded
module IDs, input sequence, viewport size, rendered entity/draw counts, device
state, and bounded diagnostics. Debug builds additionally expose a read-only
runtime snapshot endpoint for deterministic browser assertions.

`rekall game audit-web` verifies:

- every declared file exists and matches its hash and size;
- no undeclared mutable project paths escape the package;
- engine, schema, SDK, module, shader, and asset compatibility;
- the entry scene and required capabilities;
- relocation under an arbitrary directory and ordinary static HTTP hosting;
- a smoke boot in browser WASM.

Audit is necessary but does not replace gameplay acceptance.

## Acceptance for the first vertical slice

The original platformer accepted on Windows is published without rewriting its
scene or C# gameplay logic. In the in-app browser:

1. the package identifies the same project, scene, module IDs, and content hashes;
2. the player renders the actual level through WebGPU;
3. semantic left/right input moves the player by a strict nonzero transform delta;
4. jump input changes vertical state and produces visible motion;
5. collision/grounding, collectible, hazard/death/respawn, goal, score/lives,
   camera follow, HUD, and reset/replay are exercised;
6. runtime snapshots and current visuals agree;
7. the game remains functional after package relocation and a fresh reload.

Only then may the moving-dot/triangle proof page be replaced by a claim that AGE
publishes playable games to the web.

## Explicit non-goals

- WebGL as an unplanned substitute when WebGPU is unavailable. A future WebGL
  backend may be designed as a genuine RenderingDevice backend, but it is not a
  euphemism for a reduced or unrelated player.
- Runtime compilation or arbitrary assembly download in the browser. Export-time
  compilation/static registration is deterministic and compatible with WASM/AOT.
- A browser editor or Studio port in this milestone.
- Platformer templates or built-in platformer behavior in the engine core.
- Claiming full 3D browser parity from the first accepted-game vertical slice.
