# Programmable Rendering and GPU Resources Design

Date: 2026-08-21

Status: approved architecture; implementation will be staged

## Objective

Make Rekall AGE a genuinely programmable production renderer that AI agents
and C# game modules can use to author arbitrary visual techniques. Custom
shaders, post-processing, geometry, textures, and buffers must be ordinary,
inspectable engine resources rather than hard-coded effect switches.

The first user-facing proof is an agent-authored animated raindrop effect over
an otherwise ordinary rendered scene. Rain is not an engine feature: it is a
project fragment shader and post-process pass consuming generic inputs.

## Current state

AGE already has useful foundations:

- `rekall.shader.list`, `read`, `write`, `validate`, and `assign_pipeline`
  expose project GLSL vertex and fragment sources and compile them with
  Shaderc.
- `Rekall.MeshRenderer` and runtime viewport records retain vertex/fragment
  shader names.
- `Rekall.PostProcessStack` projects authored pass records into runtime frames.
- `Rekall.GeometryMesh` persists explicit vertices and 16-bit indices and is
  consumed by rendering and physics.
- The Windows player renders through Veldrid and the native capture path uses
  Vulkan directly.

These contracts currently stop short of production behavior. Assigned entity
shaders are projected but not selected by the renderers. The Windows player
maps all post-process records onto one fixed bloom/FXAA presenter, ignoring an
authored shader source. Geometry lacks an efficient typed runtime update API,
and GPU buffers are renderer-private.

## Architectural decision

AGE will use two access tiers.

The normal tier is backend-neutral and engine-owned. Agent-authored modules
declare shader interfaces, render passes, meshes, and resources through typed
contracts. They receive stable handles and a validated command encoder. AGE
owns allocation, synchronization, lifetime, hot reload, diagnostics, and
packaging.

The full-trust tier is an explicit native render extension. A trusted module
may request a backend-specific device context after declaring its backend and
capability requirements. Full-trust code is never silently loaded into the
restricted module host and is reported as non-portable by trust, package, and
compatibility inspection.

Normal modules will not receive an unmanaged Vulkan or Veldrid device. Giving
every module raw device ownership would make resource lifetime, hot reload,
backend portability, command ordering, and agent diagnostics unreliable.

## Shader assets and interface ABI

Project shaders live below `Shaders/` and are described by a versioned shader
asset manifest. A manifest identifies:

- logical shader name and source files;
- stage: vertex, fragment, or compute;
- entry point;
- declared vertex layout;
- resource bindings and access mode;
- push-constant or small-uniform fields;
- supported feature/backend requirements;
- compile-time variant keys;
- source and compiled-content hashes.

GLSL 450 is the first canonical source language because AGE already compiles
it with Shaderc and Vulkan/OpenXR is the strongest native path. Compilation
produces SPIR-V, then reflection verifies that the compiled bindings match the
manifest. Veldrid consumes the same validated SPIR-V path for the Windows
player. Additional source languages may be added behind the same manifest and
reflection ABI; no game contract will depend on Shaderc-specific objects.

The initial material ABI supplies world/view/projection transforms, camera and
frame timing, entity identity, lights, standard material textures/samplers,
and declared custom parameters. The initial full-screen ABI supplies scene
color, optional depth and motion inputs, output size, time/delta time, frame
index, camera data, and declared project parameters. Bindings are semantic and
versioned; agents inspect names and types rather than guessing descriptor slots.

Compilation is bounded by source size, include depth, variant count, compiler
time, SPIR-V size, descriptor counts, and workgroup dimensions. Includes stay
inside engine or project shader roots. Compilation and SPIR-V validation return
line-oriented diagnostics and executable repair actions. Invalid pipelines
fail closed to a diagnostic fallback material in interactive development and
remain blocking for validation/package gates.

## Pipelines, materials, and render graph

`Rekall.ShaderPipeline` becomes a first-class versioned asset reference rather
than two loose strings. It combines validated stages with raster, depth,
blending, topology, vertex-layout, and render-target compatibility state.
`Rekall.MaterialInstance` references a pipeline and supplies typed parameter,
texture, sampler, and buffer bindings. Existing `MeshRenderer.vertexShader`
and `fragmentShader` properties remain a compatibility shape and migrate into
the same runtime pipeline resolver.

AGE will compile each frame's authored rendering into a directed render graph.
Each pass declares reads, writes, scale, format, sample count, load/store
behavior, and ordering dependencies. The graph validates missing producers,
cycles, incompatible formats, read/write hazards, and resource limits before
recording commands. AGE derives transitions, barriers, temporary render-target
aliasing, and ping-pong allocation.

Initial pass kinds are:

- scene raster pass;
- full-screen raster pass;
- compute pass;
- copy/resolve pass;
- presentation pass.

`Rekall.PostProcessStack` is a concise authoring projection onto sequential
full-screen graph passes. A pass may reference an authored fragment shader and
typed parameters. It may read scene color and optionally depth or another
named graph resource, then write a named output. Built-in bloom and FXAA are
ordinary engine shader assets on this graph, not special renderer branches.

The raindrop proof consists of a project full-screen vertex/fragment pipeline
whose fragment stage distorts `sceneColor` from procedural droplets using time,
resolution, and authored strength/speed parameters. Removing that project pass
removes the effect without changing engine code.

## Geometry API

Geometry is separated into immutable mesh assets and dynamic mesh instances.

Immutable mesh assets support positions, normals, tangents, colors, multiple
UV sets, skinning data, morph data, declared custom vertex attributes, 16- or
32-bit indices, submeshes, topology, bounds, and optional CPU retention. Agents
can inspect summaries and bounded vertex/index slices and can create or patch
assets transactionally.

Dynamic C# geometry uses typed handles rather than rewriting JSON arrays each
frame. A module can create a dynamic mesh with declared capacities/layout,
update a bounded vertex or index range, replace submesh metadata, recalculate
or explicitly supply bounds/normals/tangents, and publish it to an entity. AGE
validates finite values, index ranges, capacity, topology, and byte budgets
before accepting an update. Updates become frame commands and are applied at a
safe renderer synchronization point.

`Rekall.GeometryMesh` remains the human- and agent-authorable persisted form.
The module SDK gains typed readers and mutation/build helpers that project to
that form for durable edits or to a runtime dynamic-mesh handle for high-rate
procedural/deformable content. Physics never reads mutable GPU memory directly;
collision geometry updates are explicit and independently bounded.

## GPU resource API

Normal modules can declare engine-owned resources:

- uniform, structured storage, raw storage, vertex, index, and indirect
  buffers;
- sampled, storage, color-target, depth-target, and transfer textures;
- samplers;
- shader parameter blocks and resource sets.

Every resource declaration has a stable logical name, element or pixel format,
capacity, usage flags, CPU access policy, initialization source, persistence
scope, and bounded lifetime. The SDK exposes create, inspect, update-range,
resize-with-policy, clear, copy, and release operations through opaque handles.
Readback is explicit, asynchronous, size-bounded, and unavailable for protected
or transient resources.

AGE owns backend objects and defers destruction until submitted frames no
longer reference them. It tracks generations so stale handles fail safely.
The render graph derives resource states and synchronization; modules cannot
inject untracked barriers in the normal tier. Per-project and per-frame quotas
bound resource count, total bytes, update bytes, dispatch size, draw count, and
readback volume.

Compute and indirect execution are enabled only through declared passes and
validated resources. Shader reflection verifies access modes and strides.
Indirect commands use engine-defined layouts so malformed project data cannot
make the backend read arbitrary memory.

## Full-trust native render extensions

An `IRekallAgeNativeRenderExtension` contract is loaded only from a module
whose manifest explicitly requests full-trust rendering. It declares supported
backends, insertion points, required device features, and owned resources.
The extension receives a backend adapter and frame context, not ownership of
the swapchain or global renderer lifetime.

Native extensions may allocate backend resources and record backend commands
inside their assigned graph pass. AGE still controls pass ordering, target
ownership, frame fences, and teardown. Device loss, resize, reload, and
shutdown callbacks are mandatory. Package inspection reports native extension
presence and backend restrictions, and restricted-host execution rejects it
with an exact trust diagnostic.

## Agent and MCP authoring surface

Agents receive purpose-built commands rather than unbounded file or memory
dumps:

- create/read/write/validate/compile shader asset;
- inspect reflected shader interface and variants;
- create/assign/inspect pipeline and material instance;
- create/inspect/patch render graph and post-process stack;
- inspect graph validation, resource lifetimes, and GPU timings;
- capture a named intermediate render target;
- inspect mesh schema, bounds, submeshes, attributes, and bounded slices;
- create/patch immutable mesh or declare a dynamic mesh;
- inspect resource metadata and bounded readback summaries.

The C# SDK exposes the same semantic contracts with typed values and opaque
handles. Runtime observations report compilation failure, missing binding,
invalid geometry, exhausted resource budget, stale handle, graph hazard,
unsupported backend feature, and device loss. Each observation includes the
entity/pass/resource identity and a concrete next action where one exists.

## Runtime and packaging lifecycle

Shader compilation occurs during authoring/build and may hot-reload in the
editor/player. A newly compiled pipeline becomes active only after full
validation; a failed reload leaves the previous valid pipeline running and
surfaces the failure. Pipeline and resource caches use content hashes and
bounded eviction.

Packages contain manifests, source when configured, reflected interfaces, and
validated compiled artifacts for declared target backends. The package
inventory hashes every artifact. Package audit recompiles or verifies compiled
content according to the build profile, checks required GPU features, and
proves at least one real frame through the same runtime graph.

## Diagnostics and evidence

Inspection must report:

- selected pipeline and variant per draw;
- source/compiled hashes and backend compatibility;
- reflected bindings and currently bound resource identities;
- render-graph pass order, inputs, outputs, formats, and hazards;
- resource count, size, usage, generation, lifetime, and last update;
- mesh attribute/index layouts, bounds, and selected bounded slices;
- per-pass CPU/GPU time when timestamp queries are available;
- fallback use and the exact reason;
- named intermediate target captures.

No diagnostic returns an unbounded shader, geometry, buffer, or texture dump.
Source reads, mesh slices, readbacks, and captures have explicit limits and
continuation ranges.

## Error handling and safety

- Authoring commands are project-root confined and transactional.
- Shader compilation and validation use bounded worker execution.
- Non-finite values, out-of-range indices, integer overflow, incompatible
  layouts, graph cycles, and unsupported features fail before GPU submission.
- Resource allocation is quota checked before backend allocation.
- A failed optional pass may use an explicit authored fallback policy; package
  validation never silently accepts a missing required pass.
- Device loss invalidates handle generations, emits structured observations,
  recreates engine-owned resources where source data permits, and disables
  unrecoverable native extensions cleanly.

## Delivery tranches and acceptance

### Tranche 1: executable material shaders

Make existing project vertex/fragment assignments execute in native Vulkan
capture and the Windows player through one reflected ABI and pipeline cache.
Prove different authored shader output, compile errors, hot reload fallback,
renderer parity, inspection, and packaged execution.

### Tranche 2: authored post-processing and render graph

Replace the fixed presenter switch with graph-backed full-screen passes while
retaining bloom/FXAA compatibility. Prove a project-authored animated raindrop
effect in the live player and hardware capture, with graph/resource inspection
and an intermediate capture.

### Tranche 3: typed dynamic geometry

Add immutable mesh inspection/patching and runtime dynamic-mesh handles with
range updates and 32-bit indices. Prove an agent-authored C# module deforms or
generates geometry over time without rebuilding the scene document each frame.

### Tranche 4: general GPU resources and trusted extensions

Add typed storage/compute/indirect resources and pass encoders, then the
explicit full-trust native extension boundary. Prove compute-generated data
feeding a draw, safe stale-handle/resource-budget failures, device recreation,
package trust reporting, and an expert backend-specific extension.

Each tranche requires focused TDD, full warning-as-error Release build, all
engine and Studio tests, hardware Vulkan proof, Windows-player proof, bounded
diagnostic evidence, and a safety commit/push. No tranche adds game-specific
render behavior to the engine.

## Non-goals

- A built-in rain, toon, outline, dissolve, or other game-specific effect.
- Allowing ordinary modules to own the swapchain or global graphics device.
- Exposing raw pointers or unbounded buffer dumps through MCP.
- Replacing imported-model or persisted-geometry workflows with runtime-only
  handles.
- Promising every backend feature on hardware that does not support it; feature
  requirements remain explicit and inspectable.
