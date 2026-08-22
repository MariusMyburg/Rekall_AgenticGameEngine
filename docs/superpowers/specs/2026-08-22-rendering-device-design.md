# Public C# Rendering Device Design

## Goal

Give AGE engine systems, trusted extensions, tools, and agents one backend-neutral,
100% C# graphics contract instead of leaking Vulkan/Veldrid objects or adding
one-off GPU commands.

## Architecture

- `Rekall.Age.Rendering.Abstractions` owns immutable descriptors, opaque typed
  handles, capabilities, resource/queue interfaces, and command records.
- Handles carry resource kind, device identity, slot, and generation so stale,
  foreign-device, or wrong-kind use fails deterministically.
- Descriptors cover buffers, textures/views, samplers, shader modules, binding
  layouts/sets, render/compute pipelines, and render targets.
- Command encoders cover transfer, render, and compute operations. A command
  buffer is immutable after finish and is submitted explicitly.
- A shared validator applies finite numeric checks, format/usage compatibility,
  alignment, bounded dimensions/counts/source sizes, binding uniqueness, and
  required-feature checks before a backend allocates anything.
- Resource labels, descriptors, command summaries, byte budgets, and stable
  diagnostics are inspectable; native handles are never exposed.
- The first backend adapter wraps the Player's existing graphics device. Engine
  rendering migrates onto the same contract incrementally, with conformance
  tests preventing backend-specific semantic drift.

## Agent and module boundary

Trusted C# renderer extensions may use the device interface directly. Ordinary
agent-authored gameplay modules remain deterministic and backend-independent:
they author bounded declarative render/compute workloads that the engine
validates and executes. This keeps restricted modules from owning native GPU
lifetimes while still exposing programmable shaders, geometry, buffers, and
compositor passes through generic contracts.

## Web direction

The contract models capabilities rather than Vulkan concepts. A WebGPU adapter
can preserve buffer/texture/binding/pipeline/encoder semantics; a WebGL 2
compatibility adapter can reject or lower unsupported compute/storage features
with explicit diagnostics. Web export is complete only after a .NET WASM Player,
browser services, packaging/audit, and real playable browser proof exist.

## Non-goals for the first tranche

The first tranche does not claim the Player has migrated completely, allow
unbounded GPU allocation from restricted modules, or claim WebGPU/WebGL export.
