# RenderingDevice Migration Evidence

Status: verified, committed at `c3801df`, and pushed on `origin/codex/studio-interaction`

Updated: 2026-08-22 21:32 Africa/Johannesburg

## Implemented and focused-verified

- Public 100% C# immutable commands now cover buffer copies, render and compute
  pass boundaries, pipeline and binding-set selection, vertex/index buffers,
  draw, indexed draw, and bounded compute dispatch.
- The in-memory conformance backend validates during recording and revalidates
  the complete command sequence at submit time, including stale resources.
- The focused RenderingDevice contract suite passes 12/12.
- `rekall.render.device.inspect_workload` is available through the default
  command registry and therefore MCP. The CLI accepts inline JSON or a JSON file
  and returns backend limits, memory estimates, the command surface, and stable
  diagnostics. Its real CLI path was exercised successfully for a WebGPU-shaped
  workload; the combined rendering/MCP selection passes 15/15.
- `RekallAgePresentPassCommandPlanner` produces a validated fullscreen present
  stream and retains stable resources across frames while recreating only the
  swapchain-sized texture/target on resize. Its focused suite passes 4/4.
- A thin Veldrid adapter now consumes that AGE command stream for the Windows
  Player's real fullscreen present/post-process pass in both runtime and
  playable modes. Player.Windows builds Release with zero warnings and errors.

## Final verification

- Live Player: `Examples/VulkanCubeProbe Main --frames 5 --no-vsync` completed
  5/5 frames in one attempt, with no recovery.
- Complete Release engine suite: 1,151/1,151 passed in 2m28s.
- Complete Release Studio suite: 25/25 passed.
- Complete Release solution build: zero warnings and zero errors.

The verified implementation is committed at `c3801df`; this ledger update is
included in the same remote branch safety checkpoint.

This checkpoint does not claim the entire scene renderer has migrated, a native
Vulkan RenderingDevice backend is complete, or browser play is complete. It is
the first real Player pass executed from the shared backend-neutral contract.
