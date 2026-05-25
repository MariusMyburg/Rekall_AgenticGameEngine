# Rekall AGE Vulkan Scene Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a real Vulkan offscreen scene renderer for runtime viewport capture.

**Architecture:** Add a dedicated Vulkan scene capture interface and native implementation that render `RekallAgeRuntimeViewportFrame` content through Vulkan graphics pipeline resources. Keep the existing clear-pass path intact while routing `BackendId=vulkan` viewport capture to scene capture for supported scenes.

**Tech Stack:** C#/.NET 10, native Vulkan loader interop already present in `Rekall.Age.Rendering`, Serilog diagnostics, xUnit tests, PNG readback through `RekallAgePngWriter`.

---

### Task 1: Scene Capture Contract

**Files:**
- Create: `src/Rekall.Age.Rendering/IRekallAgeVulkanSceneCapture.cs`
- Create: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneCaptureResult.cs`
- Modify: `src/Rekall.Age.Rendering/Commands/CaptureRuntimeViewportCommand.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/CaptureRuntimeViewportCommandTests.cs`

- [ ] **Step 1: Write the failing command routing test**

Add a test proving Vulkan scene renderables no longer fail at the command boundary when a scene capture backend is supplied.

- [ ] **Step 2: Add the scene capture contracts**

Define a capture interface over `RekallAgeRuntimeViewportFrame` and `RekallAgeRuntimeViewportAssetSet`.

- [ ] **Step 3: Route runtime viewport Vulkan requests to scene capture**

Replace the clear-pass-only renderable rejection with scene capture. Keep clear-pass available for empty frames.

- [ ] **Step 4: Verify the routing test passes**

Run the targeted capture runtime viewport tests.

### Task 2: Mesh Preparation

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneMeshBuilder.cs`
- Create: `src/Rekall.Age.Rendering/RekallAgeVulkanSceneMeshModels.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanSceneMeshBuilderTests.cs`

- [ ] **Step 1: Write primitive mesh tests**

Assert generated mesh vertex/index counts and material colors for cube, sphere, cylinder, cone, and plane.

- [ ] **Step 2: Implement primitive mesh generation**

Convert viewport mesh renderables into interleaved position, normal, color, and UV vertices plus indices.

- [ ] **Step 3: Verify primitive mesh tests pass**

Run the new mesh builder tests.

### Task 3: Native Scene Capture Skeleton

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- Modify: `src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanSceneCaptureTests.cs`

- [ ] **Step 1: Write unavailable-path test**

Assert invalid dimensions and unsupported renderables return structured errors rather than throwing.

- [ ] **Step 2: Implement validation, unsupported-kind detection, and result shaping**

Return stable `RekallAgeVulkanSceneCaptureResult` metadata before native Vulkan calls are added.

- [ ] **Step 3: Verify unavailable-path tests pass**

Run the new scene capture tests.

### Task 4: Graphics Pipeline Resources

**Files:**
- Modify: `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_scene.vert`
- Create: `src/Rekall.Age.Rendering/Shaders/rekall_scene.frag`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanSceneCaptureTests.cs`

- [ ] **Step 1: Add shader/resource creation tests with fake result seams**

Assert shader module, descriptor, pipeline, and depth metadata is represented in results.

- [ ] **Step 2: Implement Vulkan resource lifecycle**

Create color/depth images, image views, render pass, framebuffer, vertex/index/uniform buffers, descriptor set layout, pipeline layout, shader modules, and graphics pipeline.

- [ ] **Step 3: Verify resource metadata tests pass**

Run the Vulkan scene capture tests.

### Task 5: Command Recording And Readback

**Files:**
- Modify: `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanSceneCaptureTests.cs`

- [ ] **Step 1: Add native smoke test**

When Vulkan is available, capture a cube scene and assert the PNG is nonblank. When Vulkan is unavailable, assert the result is a structured unavailable result.

- [ ] **Step 2: Record scene draw commands**

Begin render pass, bind graphics pipeline, viewport/scissor, descriptor sets, vertex/index buffers, draw indexed meshes, transition/copy color image to readback buffer, submit, wait, map, and write PNG.

- [ ] **Step 3: Verify native smoke test passes**

Run the native Vulkan scene capture test.

### Task 6: CLI/MCP/Docs Verification

**Files:**
- Modify: `README.md`
- Test: `tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs`
- Test: `tests/Rekall.Age.Tests/Mcp/McpJsonRpcServerTests.cs`

- [ ] **Step 1: Update assertions and docs**

Document `vulkan-scene-rendered`, unsupported paths, and log locations.

- [ ] **Step 2: Run build and full tests**

Run `dotnet build Rekall.AGE.sln` and `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-build`.
