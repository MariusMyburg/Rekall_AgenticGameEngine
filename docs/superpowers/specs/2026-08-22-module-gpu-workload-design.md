# Agent-Authored GPU Workload Design

## Goal

Let agent-authored C# runtime modules describe arbitrary bounded GPU resource
graphs and transfer/render/compute command streams without exposing Vulkan,
Veldrid, WebGPU, pointers, or native resource lifetimes.

## Boundary

- Immutable workload records live in `Rekall.Age.Runtime.Abstractions`, which is
  already part of the installed module SDK and restricted module-host protocol.
- A workload uses stable authored string identifiers for resources. Backends
  resolve them to opaque RenderingDevice handles only after complete validation.
- Runtime-module SDK helpers add, replace, list, and remove workloads through
  the immutable runtime render view. Modules never mutate a backend device.
- Shader source may be authored inline or supplied by the existing project
  shader pipeline. Source and every resource/command collection remain bounded.
- The rendering layer compiles a workload transactionally: validate names,
  limits, references, usages, formats, pass state, byte budgets, and capabilities;
  allocate only after validation; publish no partial graph on failure.
- Stable diagnostics and an inspectable compiled plan are available to runtime,
  CLI/MCP, Studio, tests, and future WebGPU/WebGL adapters.

## Workload model

A workload contains typed buffer, texture, sampler, shader, binding-layout,
binding-set, pipeline, and render-target declarations plus one flat command
stream. Commands cover copies, render/compute pass boundaries, bindings,
vertex/index selection, draw/indexed draw, and dispatch. Optional future
commands such as indirect draw or texture copies extend the same discriminated
command record without changing native ownership rules.

## Safety and determinism

- Per-frame limits cover workload count, identifiers, descriptors, commands,
  shader bytes, individual resources, aggregate allocation, attachments,
  bindings, and dispatch dimensions.
- IDs are ordinal, trimmed, unique, and bounded. Every reference is resolved
  before device allocation.
- Runtime modules form one immutable world lineage; replacing the same workload
  ID is deterministic and listing is ordinal.
- Restricted modules can describe workloads but cannot submit native commands,
  retain handles, read arbitrary GPU memory, or bypass engine capability checks.
- A backend unsupported feature fails with a stable diagnostic; WebGL 2 may
  explicitly reject compute/storage while WebGPU and desktop adapters preserve it.

## Acceptance

1. Module SDK helpers and contracts survive JSON round-trip through the runtime
   world shape.
2. A compiler rejects malformed and over-budget graphs before allocation and
   produces an immutable RenderingDevice command buffer for a valid graph.
3. CLI/MCP and runtime inspection report authored IDs, resource/command counts,
   byte estimates, capability requirements, and exact diagnostics.
4. A real programmable compositor/compute proof executes through the Windows
   Player, followed by equivalent WebGPU browser acceptance.

