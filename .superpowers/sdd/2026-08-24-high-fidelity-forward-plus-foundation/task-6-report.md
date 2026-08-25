# Task 6 Report: Generic GPU Particle Emitters

Date: 2026-08-25
Base commit: `f7b5b38`
Branch: `codex/high-fidelity-forward-plus`

## Outcome

Task 6 is complete. `Rekall.ParticleEmitter3D` now projects into a generic backend-neutral runtime/viewport contract, resolves through a deterministic bounded planner, simulates in native Vulkan compute using the engine `DeltaSeconds`, persists double-buffered state across consecutive renderer-session frames, writes the active count into GPU indirect arguments without CPU readback, and draws camera-facing particles after fog into scene HDR before bloom/tone mapping.

The implementation remains engine-generic. It contains no game/genre effect semantics; agents author emitter parameters and attach/update emitter entities. Quality resolution changes only particle render capacity. Unsupported mesh/ribbon/beam modes are rejected with `REKALL_PARTICLE_DRAW_MODE_UNSUPPORTED`, never silently converted to quads.

## Executable contract

- Authored data: enablement, local/world space, capacity, continuous rate, bounded bursts, lifetime, seed, velocity cone/range, gravity, drag, size/color curves, quad/mesh mode, lit/unlit, HDR emission, soft fade, texture, flipbook, alpha/additive, priority, visibility distance, layer, and transform.
- Stable planning: priority descending then entity ID; per-emitter ceiling 262,144; global ceiling 1,048,576; deterministic spawn start/count from seed/frame/elapsed time/delta; partial final allocation plus bounded overflow IDs.
- Validation/degradation: lifetime/capacity/curve/flipbook/blend validation; layer and visibility-distance culling; compute/storage-buffer capability degradation to zero; Vulkan compute-queue and storage-range validation before particle allocation; explicit missing/multi-texture-batch diagnostics.
- State lifetime: persistent A/B buffers are zero-initialized on first use/discontinuity/capacity change and reused only for the next consecutive frame at the same capacity. Graph and native execution agree on A→B or B→A.
- Pass order: fog integration/debug → particle upload → particle compute → transparent particle graphics into `scene-hdr` → bloom/tone map.
- Rendering: depth test on, depth write off, scene-depth soft intersection, camera-facing quads, local/world position handling, authored flipbook UVs, mixed alpha/additive in one indirect batch, lit/unlit modulation, HDR emissive output.
- Empty parity: no particle resources, upload, compute, or indirect draw when the planner has no allocation; the existing transparent pass/topology remains otherwise unchanged.

## TDD evidence

### RED

1. Planner/projection tests initially failed compilation because the runtime/viewport emitter contracts and `RekallAgeVulkanParticlePlanner` did not exist.
2. The unsupported-mode/layer/distance test initially planned beam/masked/distant emitters instead of only the visible quad.
3. Native capture tests initially failed compilation because particle reports, debug captures, shader compilation, compute dispatch, and indirect draw evidence did not exist.
4. Persistent-state test failed at `Assert.True(secondParticles.PreviousStateReused)` because buffers were per-capture.
5. The first resident-state implementation reproduced a native `0xC0000005` at `vkCmdUpdateBuffer`; root-cause tracing showed that the resident path skipped creation of the per-frame indirect buffer. State and frame resource allocation were separated, then the same test passed.
6. Capability test resolved 64,000 particles instead of zero when compute or storage buffers were unavailable.
7. Invalid flipbook/blend test planned all three emitters rather than rejecting malformed inputs.

### GREEN

- Planner/projection initial focused gate: 5/5.
- Final particle planner/shader/native gate after local rotation packing: 15/15, zero skipped.
- Focused projection/planner/render-graph/high-fidelity/native gate: 64/64, zero skipped.
- Rendering namespace: 618/618, zero skipped.
- Consecutive native captures: same state generation; first A→B without reuse, second B→A with reuse.

## Native visual/debug artifacts

All artifacts were produced by the actual native compute/indirect-draw path and visually inspected.

| Artifact | SHA-256 | Observation |
|---|---|---|
| `task-6-evidence/native-particle-final.png` | `C733CDAB0F3F84E90B518D73A13A29CBC87D058DC153447BA9B5DC2828757ED9` | Nonblank HDR-emissive particle over the native scene. |
| `task-6-evidence/native-particle-bounds.png` | `FF2A25AE94D65F340E2D42B4C3DE5E70EF28651D8EA650052124E7A17B561F15` | Nonblank projected emitter bounds view. |
| `task-6-evidence/native-particle-overdraw.png` | `EB7632D62CCEF0AADF97FAA18E09EB8C5B15493B673B1B2F28C952D5793BC50D` | Nonblank particle/final-frame overdraw heat view. |

Native report assertions also prove: one compute dispatch, one indirect draw, correct fog/simulate/draw ordering, both persistent resources allocated, depth sampled/tested without writes, HDR output, and `DeltaSeconds == 1/60`.

## Verification

| Command | Result |
|---|---|
| `dotnet build Rekall.AGE.sln -c Debug --no-restore` | Succeeded; 0 warnings, 0 errors; 5.79 s. |
| Focused Task 6/high-fidelity filter | 64/64 passed; 0 skipped; 39 s. |
| `dotnet test ... --filter FullyQualifiedName~Rekall.Age.Tests.Rendering --no-build` | 618/618 passed; 0 skipped; 41 s. |
| Final planner/shader/native filter | 15/15 passed; 0 skipped; 2 s test duration. |
| `dotnet test Rekall.AGE.sln -c Debug --no-build` | Studio 65/65 passed. Concurrent engine run: 1 transient web-publish contention failure, 1,828 passed. |
| Isolated `WebGamePublishingTests.PublishesAndAuditsARealWebGameEndToEnd` | 1/1 passed; 64 s. |
| Full standalone engine assembly in a fresh NTFS temp root | 1,829/1,829 passed; 0 skipped; 4m15s. |
| `git diff --check` | Clean (line-ending warnings only). |

The first full-solution attempt was invalidated by a pre-existing shared test-temp root filling `C:` to zero free bytes; unrelated asset/build/agent/Vulkan tests all reported `There is not enough space on the disk`. Generated shared test residue was pruned after exact-path verification, freeing about 56 GB. An intermediate isolated `F:` run was stopped because its filesystem cannot exercise NTFS junction/hard-link security tests. Final engine and Studio results above use clean NTFS task roots.

## Residue and resource audit

- No task `dotnet`, `testhost`, or `vstest` process remains.
- Four exact task-specific validation roots were deleted. Two roots contained intentional restricted-ACL security-test directories; their ACLs were reset only within the verified task roots before deletion.
- The two final focused native-capture directories were deleted after evidence copying.
- The ignored evidence directory and this ignored report are intentionally force-added to source control.
- Vulkan state cleanup owns all per-frame emitter/active/indirect buffers, descriptor pools/layouts, pipelines/modules, and image resources. The renderer session exclusively owns persistent particle A/B buffers and destroys them on session disposal.

## Files

### Runtime and contracts

- `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
- `src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs`
- `src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs`
- `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
- `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`

### Planning, graph, native execution, shaders

- `src/Rekall.Age.Rendering/RekallAgeVulkanParticlePlanner.cs`
- `src/Rekall.Age.Rendering/RekallAgeRenderQualityProfileResolver.cs`
- `src/Rekall.Age.Rendering/RekallAgeHighFidelityRenderGraph.cs`
- `src/Rekall.Age.Rendering/RekallAgeHighFidelityRenderGraphBuilder.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFrameRenderer.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneCommandPlan.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanShaderCompiler.cs`
- `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- `src/Rekall.Age.Rendering/Shaders/rekall_particles.comp`
- `src/Rekall.Age.Rendering/Shaders/rekall_particles.vert`
- `src/Rekall.Age.Rendering/Shaders/rekall_particles.frag`

### Tests and tracked design

- `tests/Rekall.Age.Tests/Runtime/RuntimeProjectionTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanParticlePlannerTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanParticleCaptureTests.cs`
- `tests/Rekall.Age.Tests/Rendering/HighFidelityRenderGraphTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanShaderCompilerTests.cs`
- `tests/Rekall.Age.Tests/Rendering/RenderQualityProfileTests.cs`
- `docs/superpowers/specs/2026-08-24-high-fidelity-3d-rendering-design.md`
- `docs/superpowers/plans/2026-08-24-high-fidelity-forward-plus-foundation.md`

## Self-review and residual concerns

- Generic authoring rule: satisfied; no named game/effect semantics in engine code.
- Determinism: scheduling, allocation, seed hashing, time use, and history reuse are frame/delta driven and stable.
- Graph authority: pass order, exact persistent source/destination, memory, lifetime, and empty parity are declared and tested.
- Safety: CPU validation and Vulkan device-limit checks precede allocation; all counts are bounded; overflow/rejection/culling diagnostics include stable entity IDs.
- Synchronization: transfer→compute and compute→vertex/indirect barriers cover the buffers actually consumed by draw.
- Resource ownership: the crash-driven review confirmed resident and per-frame ownership are separate; zero-failure native and full suites follow.
- Residual limitation: one indirect particle batch accepts one authored texture asset. Multiple distinct texture IDs fail explicitly with `REKALL_PARTICLE_TEXTURE_BATCH_UNSUPPORTED`; mesh/ribbon/beam modes remain later explicit capabilities. The first lit mode is a bounded material modulation rather than full clustered-light evaluation. Local-space particles follow emitter translation, while changing emitter rotation does not yet rotate particles already alive. These are visible scope limits, not silent draw-mode substitutions.
