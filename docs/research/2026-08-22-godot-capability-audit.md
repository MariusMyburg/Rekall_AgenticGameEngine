# Godot Capability Reference Audit

Date: 2026-08-22

Reference checkout: `F:\Dev\godot-reference`, Godot commit
`893cf5cbfe789ae67c9389708e1428141bb39b18`, shallow blob-filtered sparse
checkout. Godot is MIT licensed; AGE remains its own 100% C# architecture.
Concepts may be learned from and attributed, while copied implementation code
must retain required license notices.

## Architectural findings

Godot separates renderer policy from graphics-driver mechanics. Forward+ uses
clustered lighting; Mobile uses a lower-cost forward path; Compatibility uses
OpenGL/WebGL. A RenderingDevice layer owns explicit buffers, textures,
samplers, uniform sets, pipelines, command recording, synchronization, and
shader compilation. Shader source is a first-class resource with include
libraries, preprocessing, variants, reflection, editor diagnostics, and
pipeline caching. The renderer then layers culling, canvas/2D, scene/3D,
lighting, shadows, environment, post-processing, particles, and compositor
effects over those contracts.

Godot's managed layer generates a broad C# API from native class metadata and
supports exported properties, lifecycle calls, signals, RPC metadata, state
preservation during assembly reload, diagnostics, and external IDE workflows.
AGE's native language is already C#, which avoids this interop/glue boundary;
AGE should preserve that advantage while broadening its generic SDK surface and
hot-reload/state contracts.

Godot Web builds compile the native engine through Emscripten to WebAssembly
and use the Compatibility renderer through WebGL 2. Godot 4's current stable
documentation still states that C# projects cannot export to Web. AGE should
not inherit that limitation: its web target should compile game modules and
engine code as .NET WebAssembly and drive a browser graphics backend through a
small generated host, preferably WebGPU with WebGL 2 fallback. This is a
separate backend, packaging, and acceptance track—not a claim that current AGE
desktop assemblies already run in a browser.

## AGE gap priorities

1. Shader libraries, bounded preprocessing, target defines, reflection,
   variants, cache manifests, and precise agent diagnostics.
2. A public C# RenderingDevice-style resource/command API used by both engine
   systems and agent-authored modules, rather than diagnostic-only Vulkan
   commands or fixed Player layouts.
3. A render graph with explicit pass inputs/outputs, lifetime analysis,
   barriers, transient resources, capture, and inspection.
4. Compute shaders, storage buffers/images, indirect drawing, instancing,
   particles, GPU-driven culling, and programmable compositor effects.
5. Renderer profiles: scalable desktop, mobile/XR, and web compatibility, with
   inspectable feature negotiation and authored fallback paths.
6. Production lighting/shadows, environment/post effects, decals, probes,
   occlusion, LOD, batching, texture streaming, and pipeline prewarming.
7. A .NET WebAssembly Player, browser input/audio/filesystem/network bridge,
   WebGPU/WebGL backend, web package/audit workflow, and playable acceptance.

## First translated implementation

Add project shader include libraries and a bounded C# preprocessor integrated
with validation, pipeline compilation, hashing, hot reload, CLI, and MCP. This
is generic graphics programmability, not a game-specific effect.

