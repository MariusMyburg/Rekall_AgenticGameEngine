# Native Vulkan Screen-Space Ambient Occlusion Design

## Purpose

Make AGE's advertised High/Epic ambient-occlusion quality setting execute in
the native Vulkan renderer so nearby geometry gains readable contact depth in
real captures and packaged play. The feature remains renderer-generic and is
validated first against Aetherfall rather than adding game-specific shading.

## Current deficiency

The quality resolver and high-fidelity render graph expose SSAO, and the
Windows interactive renderer evaluates a bounded depth-based approximation.
The native Vulkan path does not allocate or execute an SSAO pass. Its graph
currently describes `ssao-occlusion` as a side effect of `cluster-build`, even
though native capture executes neither that write nor an occlusion composite.
This makes inspection overstate real rendering capability and leaves contact
detail flatter in the authoritative Vulkan proof path.

## Reference boundary

Godot's RenderingDevice renderer keeps screen-space effects as explicit
resources and passes rather than hiding them inside material shaders. AGE will
follow that ownership boundary while retaining its own small Vulkan executor.
Blender EEVEE similarly treats ambient occlusion as a screen-space lighting
stage; AGE uses that separation as architectural inspiration, not copied code.

## Considered approaches

1. **Depth-derived fullscreen resolve (selected).** Execute one explicit
   post-opaque graphics pass that samples the resolved scene depth, estimates
   local occlusion with a bounded rotated kernel, and multiplicatively blends
   the result into HDR scene color before fog and transparent effects. This is
   immediately useful, modest in memory, and fits the existing native pipeline.
2. **Fold AO into tone mapping.** This is smaller but would darken fog,
   particles, and emissive content and would keep the render graph dishonest
   about where occlusion occurs.
3. **Full half-resolution normal-aware SSAO plus bilateral blur.** This is the
   long-term quality path, but it first requires a real native depth/normal
   prepass and two additional filtered resources. It is too large for the next
   visible checkpoint and should build on evidence from the selected slice.

## Render contract

- Add an explicit `ssao-resolve` graphics pass after `opaque-hdr` and before
  fog integration.
- The pass reads `depth-buffer` and writes `scene-hdr` through multiplicative
  blending; the HDR attachment is loaded rather than cleared.
- Remove the fictitious `ssao-occlusion` write from `cluster-build` and the
  corresponding opaque-pass read. The graph must report only executed work.
- Performance/Low/Medium presets omit the pass according to the existing
  resolved `Post.Ssao` flag. High uses 8 taps; Epic/Ultra use 12 taps, with
  radius, bias, and strength supplied by the shared ambient-occlusion planner.
- The shader reconstructs comparable view distance from sampled depth using
  the active camera near/far and projection facts. It rejects clear/background
  depth and clamps all results to a conservative floor so AO cannot turn the
  scene into black silhouettes.
- A small frame-index rotation breaks fixed directional banding without
  introducing stochastic noise or temporal history.

## Native execution

The Vulkan executor adds one descriptor set for the sampled depth texture, one
fullscreen pipeline using the existing fullscreen vertex shader, and compact
push constants for texel size, near/far, projection mode, sample count, radius,
strength, bias, and frame rotation. The pipeline targets the existing HDR
render pass with load semantics and multiplicative color blending. Resource
barriers transition depth from attachment use to shader read, then back when
required by later work; `scene-hdr` remains the loaded color attachment.

Unsupported sampled-depth capability fails closed during high-fidelity format
validation with a structured diagnostic. Disabled SSAO allocates no additional
pipeline or descriptor objects and records no executed pass.

## Evidence and tests

- Render-graph tests require truthful `opaque-hdr -> ssao-resolve -> fog`
  ordering and no fake cluster output.
- Planner/shader contract tests require exact High and Epic budgets, bounded
  parameters, depth/background rejection, and deterministic shader compilation.
- Native integration tests require the SSAO pipeline, descriptor, draw, report,
  and GPU-timing pass to exist only when resolved quality enables it.
- A real High Aetherfall Vulkan capture must execute `ssao-resolve`, remain
  informative, contain zero observations/missing/fallback assets, and be
  visually inspected for stronger contact grounding without black speckle or
  crushed silhouettes.
- The strict movement/combat/progression/reset proofs, project/scene validation,
  and `desktop60` budget must remain green after the final game evidence update.

## Deferred work

A native normal prepass, half-resolution AO image, bilateral denoise/upsample,
temporal accumulation, GTAO-style horizon search, and bent-normal/indirect-light
integration remain later quality improvements. This slice establishes truthful
execution and visible contact depth without blocking useful delivery on the
complete long-term technique.
