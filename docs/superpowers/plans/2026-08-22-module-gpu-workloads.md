# Agent-Authored GPU Workload Implementation Plan

**Goal:** Expose bounded, backend-neutral GPU programmability to agent-authored
C# modules and execute it through the shared RenderingDevice contract.

- [x] Add failing runtime-contract, JSON round-trip, and module-SDK helper tests.
- [x] Add immutable named GPU workload/resource/command records to
  `Rekall.Age.Runtime.Abstractions` and stable SDK add/replace/list/remove helpers.
- [x] Advertise exact workload types and helper signatures through runtime SDK
  inspection and module prompting.
- [x] Add a transactional compiler from named workloads to RenderingDevice
  descriptors, opaque handles, and immutable command buffers.
  Buffer/texture asset-data upload remains a separate execution-stage item.
- [x] Add stable graph/reference/budget/capability diagnostics and inspection.
- [x] Execute a programmable post-process or compute workload in the Windows
  Player with deterministic capture evidence.
  The portable compiler accepts validated non-owning imports; the Veldrid
  `IRekallAgeRenderingDevice` adapter executes the same immutable command
  buffer. The Player imports `engine.scene-color` and `engine.output` without
  exposing native handles, caches compiled workloads, and invalidates them on
  frame-resource changes. The committed probe capture is
  `Examples/ProgrammableCompositorProbe/Captures/vulkan-programmable-compositor.png`.
- [x] Expose workload validation/inspection through CLI/MCP.
- [x] Add explicit portable vertex-buffer layouts and bounded project-asset
  uploads so authored workloads can render arbitrary data-backed geometry.
  Uploads resolve stable catalog IDs within the current project, verify SHA-256
  and a 64 MiB per-asset bound, reject filesystem links/escapes, and execute via
  both the conformance device and the native Veldrid adapter. The committed
  Vulkan proof is
  `Examples/ProgrammableGeometryProbe/Captures/vulkan-asset-backed-geometry.jpg`.
- [ ] Add storage-buffer/texture and indirect-command metadata plus native
  execution where the selected backend advertises those capabilities.
- [x] Run full suites, Release build, live Player proof, update production
  progress, commit, and push.
