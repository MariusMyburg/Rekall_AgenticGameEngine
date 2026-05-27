# Rekall AGE Virtual Geometry CPU Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in CPU-side virtual geometry path that reduces selected triangles for dense mesh renderables before the current Vulkan batch path.

**Architecture:** Add a generic `Rekall.VirtualGeometry` component and propagate its settings into viewport renderables. Apply deterministic CPU-side triangle level selection in `RekallAgeVulkanSceneMeshBuilder`, then expose source/selected triangle counts through scene performance budget inspection.

**Tech Stack:** C#/.NET 10, xUnit, existing Rekall AGE runtime/rendering abstractions, existing Vulkan scene mesh/batch builders.

---

## File Map

- `src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs`: register and describe `Rekall.VirtualGeometry`.
- `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`: add runtime viewport virtual geometry settings.
- `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`: read the component from entities into renderables.
- `src/Rekall.Age.Rendering/RekallAgeVirtualGeometryReducer.cs`: create the CPU triangle reduction helper.
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneMeshBuilder.cs`: apply reduction after ordinary mesh construction.
- `src/Rekall.Age.Rendering/Commands/InspectScenePerformanceBudgetCommand.cs`: add virtual geometry diagnostics.
- `tests/Rekall.Age.Tests/Rendering/VirtualGeometryTests.cs`: focused red/green tests for the new behavior.

## Tasks

### Task 1: Component And Runtime Contract

- [x] Write tests asserting the built-in module exposes `Rekall.VirtualGeometry` and runtime frames carry settings.
- [x] Run `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter VirtualGeometry` and verify the tests fail because the component/settings do not exist.
- [x] Add `RekallAgeVirtualGeometryComponent`, `RekallAgeRuntimeViewportVirtualGeometry`, and frame-builder parsing.
- [x] Re-run the focused tests and verify they pass.

### Task 2: CPU Triangle Reduction

- [x] Write a test where a resolved imported mesh has many triangles and a renderable has `MaxSelectedTriangles`.
- [x] Run the focused test and verify it fails because the mesh builder returns all source triangles.
- [x] Add `RekallAgeVirtualGeometryReducer` and call it from `RekallAgeVulkanSceneMeshBuilder`.
- [x] Re-run the focused tests and verify selected triangles are capped while material/entity metadata is preserved.

### Task 3: Performance Diagnostics

- [x] Write a performance budget test that checks virtual geometry source triangles, selected triangles, and reduced triangles.
- [x] Run the focused test and verify it fails because the budget result has no virtual geometry counters.
- [x] Extend `InspectScenePerformanceBudgetResult` and compute counters from meshes/renderables.
- [x] Re-run focused tests and existing rendering budget tests.

### Task 4: Verification

- [x] Run `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter VirtualGeometry`.
- [x] Run `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~Rendering"`.
- [x] Run `dotnet test Rekall.AGE.sln` and report any baseline host-abort behavior separately from focused failures.
