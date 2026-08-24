# High-Fidelity 3D Rendering and Authoring Design

**Status:** Approved in principle by the user's explicit pre-approval on 2026-08-24

## Purpose

Make Rekall AGE capable of authoring and shipping detailed, high-resolution 3D worlds with scalable physically based materials, animated models, lighting, shadows, fog, particles, post-processing, reflections, and dense geometry. The system must serve cinematic stylized games, photorealistic games, and character-led games without adding genre-specific engine behavior.

The native Vulkan renderer is the reference implementation. Rendering contracts, scene components, assets, diagnostics, and quality profiles remain backend-neutral so WebGPU can reach parity without changing authored content.

The flagship performance gate is 2560x1440 at 60 frames per second on the local NVIDIA GeForce RTX 5090 using the `High` preset. `Performance` through `Epic` presets expose deliberate trade-offs rather than silently omitting features.

## Scope Decomposition

This is a renderer program, not one indivisible feature. It is delivered as independently playable milestones:

1. **High-fidelity foundation:** quality profiles, feature resolution, HDR frame resources, depth/normal prepass, directional cascaded shadows, atmospheric fog, bloom, tone mapping, GPU timings, and one fully upgraded Aetherfall zone.
2. **Material and environment fidelity:** image-based lighting, reflection probes, mipmapped/filtered texture resources, extended glTF PBR material features, decals, and authored sky/environment assets.
3. **Effects:** GPU particles, trails, beams, flipbooks, distortion, soft particles, local fog volumes, and effect budgets.
4. **Character fidelity:** GPU skinning, robust animation blending, motion vectors, attachments, morph targets, character LOD, and animation diagnostics.
5. **Indirect light and reflections:** screen-space reflections, scalable ambient occlusion/indirect light, baked lighting, probes, and an optional hardware-ray-tracing extension where supported.
6. **Dense worlds:** instancing, hierarchical visibility, occlusion, LOD/HLOD, streaming cells, texture residency, and virtualized geometry improvements.
7. **Authoring parity:** Studio inspectors/previews, agent-facing create/update/inspect commands, package support, WebGPU parity, and automated visual regression coverage for every production feature.

Each milestone must improve the real Aetherfall game and produce playable acceptance evidence before the next milestone becomes the priority.

## Approaches Considered

### Extend the current single-pass forward renderer

This minimizes initial code but cannot scale cleanly to dense local lights, shadow resources, screen-space effects, volumetric integration, or feature-dependent intermediate buffers. It is rejected as the production architecture.

### Deferred rendering

A traditional geometry buffer simplifies many-light shading and screen-space effects. It raises complexity for transparent materials, MSAA, bandwidth-constrained presets, and VR. AGE may use deferred-like intermediate buffers for individual effects, but a full deferred renderer is not the reference path.

### Hybrid Forward+ — selected

The selected renderer uses clustered light assignment and forward material shading, with optional depth/normal/motion prepasses and screen-space resources. It supports transparent effects, MSAA, VR, broad material models, and scalable pass removal while still enabling shadows, SSAO, SSR, fog, bloom, and temporal techniques.

## Godot Reference Principles

AGE will study and adapt architectural principles from the local Godot source without copying game-specific APIs or implementation text:

- `servers/rendering/renderer_rd/forward_clustered/render_forward_clustered.*`: separate render-list construction, pass selection, clustered resources, and draw execution.
- `servers/rendering/renderer_rd/renderer_scene_render_rd.*`: environment resolution, shadow-quality configuration, post-processing order, render-buffer ownership, and backend feature gates.
- `servers/rendering/storage/environment_storage.*`: keep authored environment state separate from renderer-owned GPU resources.
- `servers/rendering/renderer_rd/storage_rd/light_storage.*`: give lights and shadow atlases stable storage contracts rather than rebuilding implicit state per draw.
- `servers/rendering/renderer_rd/environment/fog.*` and volumetric fog shaders: treat volumetric fog as a bounded froxel resource with temporal reprojection and explicit quality controls.
- `servers/rendering/renderer_rd/storage_rd/particles_storage.*`: separate emitter state, simulation buffers, and render instances.
- `servers/rendering/renderer_rd/effects/*`: implement luminance, tone mapping, glow, SSAO, SSIL, and related effects as distinct passes with declared resources.

Godot is a reference for subsystem boundaries and proven rendering trade-offs. AGE retains its own immutable runtime world, command system, rendering-device abstractions, diagnostics, and agent-first authoring model.

Blender remains the reference for editable mesh/material/animation authoring vocabulary and stable data-block-style asset ownership. Agents author content through AGE's modeling graphs, mesh tools, material graphs, animation assets, and scene components; the engine does not author the content for them.

## Architectural Boundaries

### Authored scene contracts

Authored state stays generic and inspectable:

- `Rekall.RenderQualityProfile`: selected preset, optional feature overrides, resolution scale, target frame rate, and platform policy.
- `Rekall.Environment3D`: sky/environment asset, ambient energy, exposure, tone mapper, white point, color grade, and background policy.
- `Rekall.ShadowSettings`: cascade count, atlas resolution, maximum distance, split policy, bias, normal bias, filter, and stabilization.
- Existing generic light components gain shadow intent (`castShadows`, priority, range, softness) without gaining gameplay behavior.
- `Rekall.FogVolume`: global or bounded density, albedo, emission, anisotropy, falloff, blend distance, and priority.
- `Rekall.ParticleEmitter3D`: simulation space, capacity, spawn rate/bursts, lifetime, velocity/acceleration, size/color curves, collision policy, material, mesh/quad mode, sorting, and deterministic seed.
- `Rekall.ReflectionProbe`: bounds, update policy, resolution, priority, and blend distance.
- Existing mesh/material/model/skin/morph/animation contracts remain the content path; new fidelity must not require game-specific components.

Project defaults may select a quality preset, but the player CLI and Studio can override it. An authored component may override individual features only within backend and safety limits.

### Resolved feature plan

The engine compiles authored intent plus device facts into a `RekallAgeResolvedRenderFeaturePlan`. It contains:

- requested and resolved preset
- render and output resolutions
- enabled passes and feature-specific quality settings
- estimated transient/persistent GPU memory
- light, shadow, particle, geometry, animation, and texture budgets
- device-limit clamps and explicit degradation reasons
- backend support status

This plan is inspectable before execution and embedded in captures, performance reports, Studio state, and package diagnostics. Unsupported or clamped features produce stable observation codes and authoring hints; AGE never silently substitutes a materially different look.

### Render graph and resources

The first production frame graph is:

1. scene extraction, visibility, LOD, and quality resolution
2. persistent resource residency and transient resource planning
3. optional depth/normal/motion prepass
4. directional shadow cascades and later punctual-light atlas updates
5. clustered light/decal/probe assignment
6. opaque Forward+ HDR scene pass
7. sky and environment contribution
8. atmospheric and volumetric integration
9. transparent meshes, particles, trails, and distortion inputs
10. SSAO/SSR/other enabled screen-space effects
11. exposure, bloom, tone mapping, color grading, sharpening, and upscale
12. screen-space UI composition and presentation

The graph declares reads, writes, formats, extents, lifetimes, queue intent, and dependencies. Vulkan execution owns synchronization and resource transitions; authored content never manipulates barriers or native handles.

### Renderer storage

Following the useful Godot separation, CPU scene documents and runtime facts do not own Vulkan resources. Focused stores own cached GPU representations for:

- meshes and geometry LODs
- textures, samplers, mip chains, and residency
- materials and pipeline variants
- lights, shadow maps, and probes
- particle simulations and instance buffers
- animation skin/morph buffers
- environment and post-process resources

Stores use stable asset/content identities and bounded invalidation. Hot reload replaces only affected resources and retains the last valid representation on compilation/import failure.

## Physically Based Shading

The reference material model uses linear HDR lighting and a metallic/roughness workflow:

- base color, metallic, roughness, tangent-space normal, occlusion, and emissive inputs
- physically plausible diffuse/specular energy split and Fresnel response
- image-based diffuse and specular environment light
- filtered mip-chain sampling with anisotropic filtering where supported
- alpha opaque/mask/blend modes and double-sided policy
- optional clearcoat, sheen, transmission, and detail layers in later material-fidelity work

Imported glTF/GLB data and AGE material graphs compile to the same runtime material description. Tangent generation, color-space interpretation, texture-coordinate choice, and missing-resource fallbacks are explicit and diagnosable.

## Lighting and Shadows

The foundation milestone implements one shadowed directional light with stabilized cascaded shadow maps. It includes:

- one to four cascades depending on quality
- practical split weighting, stable texel snapping, configurable distance
- depth bias and normal bias
- PCF filtering with preset-controlled sample radius
- receiver/caster layer masks and per-renderable shadow intent
- shadow-caster culling and per-cascade workload diagnostics

Later milestones add spot/point shadow atlases, cached static shadows, contact shadows, and ray-traced shadows on capable devices. Light clustering must bound overflow deterministically and report dropped/merged light influence by entity and cluster.

## Atmosphere, Fog, and Post-Processing

The foundation supports inexpensive analytic height/distance fog on lower tiers and froxel volumetric fog on higher tiers. Volumetric fog includes bounded grid dimensions, light injection, density volumes, anisotropic scattering, temporal reprojection, camera-cut reset, and optional filtering.

The HDR post stack executes authored standard passes rather than treating their names as metadata only:

- histogram or luminance exposure
- bloom/glow pyramid
- AgX-style default tone mapping plus linear/Reinhard/filmic alternatives
- color grade, saturation, contrast, and white balance
- vignette, chromatic aberration, depth of field, motion blur, film grain, and sharpening as optional later passes
- spatial upscale initially, with a temporal upscaler interface reserved for later implementation

Every pass reports input/output resource, resolution, dispatch/draw count, and measured GPU duration.

## Particles and Visual Effects

Particle behavior is authored data. The renderer/simulation provides generic execution:

- deterministic emitter scheduling and bounded burst queues
- GPU simulation for position, velocity, age, rotation, size, color, and custom channels
- quad, mesh, ribbon/trail, and beam rendering modes delivered incrementally
- flipbook animation, soft-particle depth fading, lit/unlit materials, emissive HDR output, and optional collision/depth interaction
- effect LOD, distance culling, capacity limits, and overflow observations

Game modules emit generic custom facts or add/update emitter entities. The engine never contains an Aetherfall-specific hit, dash, weapon, or boss effect.

## Animated Model Fidelity

AGE builds on its existing GLB skin, morph, transform animation, mixer, and state-graph contracts:

- joint and morph evaluation remains deterministic and inspectable
- Vulkan consumes GPU skin/morph buffers without rebuilding authored meshes per frame
- animation blending provides crossfade, additive layers, masks, root-motion facts, markers, and attachment transforms
- motion vectors support temporal effects and motion blur
- character LOD selects mesh, skeleton update rate, morph budget, and animation evaluation distance
- malformed clips, missing joints, incompatible morph layouts, and budget clamps emit structured observations

Animation state machines and character behavior remain agent-authored modules; the engine supplies generic sampling, blending, events, and rendering.

## Scalability Profiles

Profiles are data, versioned and inspectable. The initial defaults are:

| Feature | Performance | Low | Medium | High | Ultra | Epic |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Internal resolution scale | 0.50 | 0.67 | 0.75 | 1.00 | 1.00 | 1.25 ceiling |
| Directional shadow cascades | 0-1 | 1 | 2 | 3 | 4 | 4 |
| Shadow resolution | 512 | 1024 | 1024 | 2048 | 2048 | 4096 |
| Shadow filtering | hard/2 tap | 4 tap | 8 tap | 12 tap | 16 tap | 24 tap |
| Fog | analytic | analytic | low froxel | 160x90x48 | 240x135x64 | 320x180x96 |
| Bloom | off | quarter res | quarter res | pyramid | larger pyramid | cinematic pyramid |
| SSAO | off | off | half res | half res | full res | full res high samples |
| Reflections | probes | probes | probes | probes + SSR | high SSR | high SSR + RT when available |
| Active particles | 2,000 | 8,000 | 24,000 | 64,000 | 128,000 | 250,000 |
| Texture bias | +2 | +1 | 0 | 0 | 0 | negative bias if safe |
| Animation update | aggressively tiered | tiered | tiered | full nearby | expanded full range | maximum authored range |

`High` is the 1440p/60 acceptance preset. `Epic` prioritizes maximum quality and is not required to sustain 60 FPS in every scene. Agents may override any row, but the resolved plan reports when the override exceeds device, memory, or project budgets.

Automatic scalability is optional and bounded. When enabled, it adjusts resolution scale and designated elastic settings using hysteresis around the target GPU frame time. It never changes gameplay simulation, authored visibility, collision, AI, or deterministic runtime state.

## Agent and Studio Authoring Experience

Agents receive generic commands to:

- inspect supported render features and device limits
- create/update environment, quality, fog, probe, particle, and shadow components
- inspect the resolved feature plan and per-pass budget
- capture a frame with selected preset and deterministic inputs
- compare two presets using aligned captures and metrics
- inspect shadow cascades, light clusters, fog slices, overdraw, motion vectors, normals, LOD, and particle bounds
- obtain bounded next actions for missing textures, excessive lights, shadow acne risk, invalid fog volumes, particle overflow, or frame-budget failures

Studio uses the same commands/read models. It adds quality selection, per-feature overrides, live pass timings, debug views, environment controls, and asset preview. No Studio-only hidden renderer configuration is permitted.

## First Playable Vertical Slice

The first implementation plan upgrades Aetherfall's Resonance Court rather than building a disconnected showcase. The accepted frame must contain:

- detailed reusable court architecture with substantially higher geometric and material fidelity
- textured metallic/roughness/normal/emissive PBR surfaces
- one animated warden and visible animated enemy archetypes
- stabilized directional shadows on characters, architecture, and props
- atmospheric height fog on all presets and volumetric fog on `Medium` and above
- HDR emissive energy, bloom, AgX-style tone mapping, and color grading
- authored particle emitters for conduit energy, projectiles, impacts, dash, ambient motes, and encounter activation
- quality-profile switching without scene mutation
- debug captures for depth, normals, shadow cascades, clusters, fog, and final HDR/LDR output

Gameplay remains the existing semantic-input, agent-authored rules. After the latest scene/module mutation, deterministic runtime inspection must still prove representative movement and combat through `Game.AetherfallWardenState` plus strict component/transform changes.

## Diagnostics and Failure Policy

Common workflow failures are blocking and structured:

- missing required Vulkan feature or format
- render-graph dependency/resource conflict
- shadow atlas exhaustion
- cluster overflow above the resolved deterministic policy
- invalid/non-finite environment or particle data
- GPU memory budget exceeded before allocation
- shader or material pipeline compilation failure with no retained valid version

Quality reduction is allowed only when requested by a preset or automatic-scaling policy. Device-enforced degradation returns stable codes, exact requested/resolved values, affected features/entities, and remediation commands.

## Verification

Each milestone uses test-driven implementation and layered evidence:

- pure contract/profile/render-graph tests
- shader compilation, resource-layout, synchronization, and deterministic plan tests
- renderer integration tests with small realistic scenes
- native Vulkan captures on the local RTX 5090 with visual inspection
- GPU timestamp and memory-budget evidence at 2560x1440
- preset comparison captures from Performance through Epic
- Aetherfall deterministic gameplay assertions after visual scene/module changes
- soak, resize, camera-cut, hot-reload, package, relocation, and audit checks
- complete solution tests before integration and push

The High acceptance frame must sustain an average GPU frame time at or below 16.67 ms over a representative 600-frame Resonance Court run at 2560x1440 on the RTX 5090. Capture-only CPU throughput is not a substitute for GPU timing. The report must identify the renderer pass responsible when the budget fails.

## Non-Goals for the First Milestone

- Full global illumination, hardware ray tracing, open-world streaming, texture virtual memory, complete WebGPU parity, cloth, hair simulation, or cinematic camera tooling.
- Photorealistic asset quantity across all Aetherfall zones.
- A game-specific visual-polish command or automatic content author.
- Silent feature substitution to obtain a passing screenshot.

These remain later milestones behind the same generic contracts.

## Completion Definition

The renderer program succeeds when agents can author detailed worlds and animated characters from inspectable AGE primitives, select predictable quality tiers, diagnose the actual GPU workload, and ship the same authored content across supported backends. The first milestone succeeds when the upgraded Resonance Court is visibly and measurably superior, preserves executable gameplay, meets the High 1440p/60 RTX 5090 gate, scales from Performance through Epic, packages for Windows, relocates, and passes a current package audit.
