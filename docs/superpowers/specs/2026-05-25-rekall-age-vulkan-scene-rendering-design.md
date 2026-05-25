# Rekall AGE Vulkan Scene Rendering Design

## Goal

Implement a real Vulkan scene-rendering foundation for runtime viewport capture. The renderer must move beyond clear-pass proof frames and support actual graphics pipelines, shader modules, vertex/index buffers, uniform buffers, descriptor sets, depth testing, material colors, texture-ready resource boundaries, offscreen color targets, and PNG readback.

## Scope

The first Option A implementation targets offscreen runtime viewport capture, not a live swapchain. `render viewport capture ... vulkan` should render scene content through Vulkan and report hardware acceleration when the host has a Vulkan-capable graphics device. Studio/live viewport swapchains can reuse the renderer later, but they are not part of this first slice.

The first drawable scene content is the engine's current runtime viewport frame contract:

- built-in mesh primitives: cube, sphere, cylinder, cone, plane
- material color from `Rekall.GeometryPrimitive` / `Rekall.MeshRenderer`
- directional-light-ready shader inputs, with a deterministic default light
- sprite/texture resource abstractions present in the Vulkan renderer API, with sprite texture draw support allowed to land after the mesh pipeline if it needs a separate task

The renderer must keep software rendering as a fallback and comparison oracle. It must never silently pretend Vulkan rendered a scene if it used software.

## Architecture

Add a Vulkan scene capture boundary separate from the existing clear-pass interface:

- `IRekallAgeVulkanSceneCapture` captures a `RekallAgeRuntimeViewportFrame`.
- `RekallAgeVulkanSceneCaptureResult` reports capture path, backend metadata, draw counts, asset counts, readback stats, and errors.
- `RekallAgeNativeVulkanSceneCapture` owns the native Vulkan graphics pipeline path.
- `CaptureRuntimeViewportCommand` routes `BackendId=vulkan` to the scene capture interface instead of rejecting renderables.

The native renderer should be structured as focused helper units instead of further inflating `RekallAgeNativeVulkanRenderPassSubmission`:

- shader compiler/loader: GLSL source to SPIR-V or embedded SPIR-V assets
- mesh builder: converts runtime primitives to vertex/index buffers
- frame graph/resources: color target, depth target, framebuffer, readback buffer
- pipeline state: render pass, descriptor set layout, pipeline layout, graphics pipeline
- command recording: begin pass, bind pipeline/descriptors/buffers, issue draw calls, copy color to readback buffer
- readback/write: map host buffer and write PNG

The existing clear-pass implementation can be reused for device selection, memory type selection, loader probing, and readback patterns, but scene rendering should have its own file and result contracts.

## Shader And Asset Strategy

Use project-owned shader source with deterministic compilation. Developer machines should not need global Vulkan SDK tools. A NuGet shader compiler package is acceptable for build/runtime shader compilation, and checked-in shader source should live under `src/Rekall.Age.Rendering/Shaders`.

The initial mesh shader contract:

- vertex input: position, normal, color, UV
- uniform: model-view-projection matrix plus model matrix or normal transform
- fragment: material/vertex color with simple directional light

The texture shader contract can share the same descriptor layout once image samplers are added.

## Runtime Behavior

For `BackendId=software`, behavior remains unchanged.

For `BackendId=vulkan`:

- if Vulkan scene capture succeeds, result uses `BackendId=vulkan`, `HardwareAccelerated=true`, `AccelerationStatus=vulkan-scene-rendered`
- if Vulkan is unavailable, result fails with `REKALL_RUNTIME_VIEWPORT_VULKAN_UNAVAILABLE`
- if a renderable type is not yet supported by Vulkan, result fails with `REKALL_RUNTIME_VIEWPORT_VULKAN_RENDERABLE_UNSUPPORTED`, naming the unsupported kinds
- if shader/pipeline/resource creation fails, result fails with `REKALL_RUNTIME_VIEWPORT_VULKAN_SCENE_FAILED` and logs detailed diagnostics through Serilog

The command should not fall back to software when the caller explicitly requests Vulkan.

## Testing

Tests must be layered:

- unit tests for primitive mesh generation and vertex/index counts
- command tests with fake `IRekallAgeVulkanSceneCapture` verifying runtime routing and result metadata
- native smoke tests that run only when Vulkan is available and assert a nonblank PNG for a simple primitive scene
- regression test replacing the old Vulkan renderable rejection with a successful fake scene capture

All tests must be compatible with machines that do not have Vulkan by making native smoke failures report unavailable rather than throwing.

## Documentation

README should clearly distinguish:

- software viewport rendering
- Vulkan clear-pass tools
- Vulkan scene viewport rendering
- current unsupported paths, if any remain after the first implementation slice

The docs must not claim live swapchain rendering until Studio uses the Vulkan renderer directly.
