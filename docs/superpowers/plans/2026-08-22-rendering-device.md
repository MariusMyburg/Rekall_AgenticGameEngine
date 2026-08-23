# Public C# Rendering Device Implementation Plan

**Goal:** Establish one validated, inspectable graphics resource and command
contract, then migrate AGE rendering and add web-capable adapters incrementally.

- [x] Add failing contract/descriptor/handle validation tests.
- [x] Implement immutable public descriptors, typed opaque handles, capability
  model, stable diagnostics, and bounded shared validation for buffers,
  textures, samplers, shaders, layouts/sets, pipelines, and render targets.
- [x] Add command encoders and immutable command-buffer validation tests.
- [x] Add an in-memory conformance backend for deterministic tests and agent
  inspection without a GPU.
- [x] Add CLI/MCP capability and workload inspection commands.
- [x] Implement a Veldrid/Vulkan Player adapter and migrate one real render path.
  The present pass completed a live 5/5-frame Vulkan acceptance.
- [x] Expose bounded declarative GPU workloads to agent-authored C# modules.
- [x] Add compute/storage/indirect operations and programmable compositor proof.
- [x] Implement the primary WebGPU adapter behind the same API and physically
  prove a compiler-authored WGSL indirect draw with same-frame pixel readback.
- [ ] Implement the later WebGL 2 compatibility adapter behind the same API.
- [ ] Verify native and browser playable acceptance, package relocation, audit,
  full suites, and zero-warning Release builds.
