# Agent-Authored GPU Workload Implementation Plan

**Goal:** Expose bounded, backend-neutral GPU programmability to agent-authored
C# modules and execute it through the shared RenderingDevice contract.

- [x] Add failing runtime-contract, JSON round-trip, and module-SDK helper tests.
- [x] Add immutable named GPU workload/resource/command records to
  `Rekall.Age.Runtime.Abstractions` and stable SDK add/replace/list/remove helpers.
- [x] Advertise exact workload types and helper signatures through runtime SDK
  inspection and module prompting.
- [ ] Add a transactional compiler from named workloads to RenderingDevice
  descriptors, opaque handles, and immutable command buffers.
- [ ] Add stable graph/reference/budget/capability diagnostics and inspection.
- [ ] Execute a programmable post-process or compute workload in the Windows
  Player with deterministic capture evidence.
- [ ] Expose workload validation/inspection through CLI/MCP.
- [ ] Run full suites, Release build, live Player proof, update production
  progress, commit, and push.
