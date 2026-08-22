# Public C# Rendering Device Implementation Plan

**Goal:** Establish one validated, inspectable graphics resource and command
contract, then migrate AGE rendering and add web-capable adapters incrementally.

- [x] Add failing contract/descriptor/handle validation tests.
- [ ] Implement immutable public descriptors, typed opaque handles, capability
  model, stable diagnostics, and bounded shared validation. Buffer/texture,
  resource-lifetime, and copy-command foundations are green; sampler, shader,
  binding, pipeline, render-target, render, and compute descriptors remain.
- [ ] Add command encoders and immutable command-buffer validation tests.
- [ ] Add an in-memory conformance backend for deterministic tests and agent
  inspection without a GPU.
- [ ] Add CLI/MCP capability and workload inspection commands.
- [ ] Implement a Veldrid/Vulkan Player adapter and migrate one real render path.
- [ ] Expose bounded declarative GPU workloads to agent-authored C# modules.
- [ ] Add compute/storage/indirect operations and programmable compositor proof.
- [ ] Implement WebGPU and WebGL 2 compatibility adapters behind the same API.
- [ ] Verify native and browser playable acceptance, package relocation, audit,
  full suites, and zero-warning Release builds.
