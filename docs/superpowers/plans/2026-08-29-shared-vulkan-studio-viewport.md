# Shared Vulkan Studio Viewport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Studio World and the Windows Player present runtime scenes through one persistent Vulkan renderer and run Studio simulation at a smooth, real-time cadence.

**Architecture:** Extract the desktop scene renderer from the Windows Player into a Windows Vulkan presentation library. The Player supplies its SDL Win32 surface and Studio supplies a WPF `HwndHost` child surface; both submit the same runtime viewport frame and asset contracts to the same renderer session. Studio retains editor interaction metadata but removes normal software bitmap rendering.

**Tech Stack:** .NET 10, C# 13, WPF `HwndHost`, Win32 child windows, Veldrid Vulkan, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-29-shared-vulkan-studio-viewport-design.md`

## Global Constraints

- Use generic runtime viewport frame and asset contracts; do not add game-specific behavior.
- Vulkan is the normal World viewport backend; failure is explicit and never silently downgraded to software.
- Preserve deterministic fixed-step runtime behavior and viewport entity interaction metadata.
- Run narrowly targeted tests during development.

---

### Task 1: Shared presentation contracts and Win32 surface

**Files:**
- Create: `src/Rekall.Age.Rendering.Windows/Rekall.Age.Rendering.Windows.csproj`
- Create: `src/Rekall.Age.Rendering.Windows/RekallAgeVulkanPresentationModels.cs`
- Create: `src/Rekall.Age.Rendering.Windows/RekallAgeWin32RenderSurface.cs`
- Create: `tests/Rekall.Age.Tests/Rendering/VulkanPresentationContractTests.cs`
- Modify: `Rekall.AGE.slnx`

**Interfaces:**
- Consumes: `RekallAgeRuntimeViewportFrame`, `RekallAgeRuntimeViewportAssetSet`.
- Produces: `IRekallAgeVulkanPresentationSession`, `RekallAgeVulkanPresentationFrame`, and an owned/non-owned Win32 surface descriptor.

- [ ] Write contract tests proving dimensions are validated, an external HWND is never destroyed by the session, and telemetry identifies `vulkan` plus hardware acceleration.
- [ ] Run `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~VulkanPresentationContractTests` and verify RED.
- [ ] Add the Windows rendering project, immutable models, disposal contract, and Win32 surface ownership rules.
- [ ] Add the project to the solution and run the focused tests to GREEN.
- [ ] Commit the contract milestone.

### Task 2: Extract the persistent Player renderer

**Files:**
- Create: `src/Rekall.Age.Rendering.Windows/RekallAgeVeldridVulkanPresentationSession.cs`
- Create: `src/Rekall.Age.Rendering.Windows/RekallAgeVeldridRenderResources.cs`
- Create: `src/Rekall.Age.Rendering.Windows/RekallAgeVeldridRenderPacketCache.cs`
- Modify: `src/Rekall.Age.Player.Windows/Program.cs`
- Modify: `src/Rekall.Age.Player.Windows/Rekall.Age.Player.Windows.csproj`
- Test: `tests/Rekall.Age.Tests/Playback/WindowsPlayerSourceTests.cs`

**Interfaces:**
- Consumes: Task 1 session contract and a Win32 swapchain source.
- Produces: one long-lived renderer used by `RekallAgeVeldridPlayer.RenderFrame` for scene, UI, shadows, particles, post-processing, and presentation.

- [ ] Add failing source/contract tests requiring the Player to reference and construct `RekallAgeVeldridVulkanPresentationSession` and forbidding a second desktop scene draw loop in `Program.cs`.
- [ ] Run only `WindowsPlayerSourceTests` and verify RED.
- [ ] Move render-owned Veldrid resources, pipeline creation, packet caching, authored UI upload, shadow/particle/post-processing recording, submit, and swapchain presentation behind `PresentAsync` without changing shader sources or defaults.
- [ ] Replace the Player render body with frame construction plus one shared-session presentation call; keep runtime, SDL input/audio, screenshots, OpenXR, recovery, and logging in the Player.
- [ ] Run `WindowsPlayerSourceTests` plus the existing focused Vulkan pipeline/cache tests to GREEN.
- [ ] Build `Rekall.Age.Player.Windows` and run Summit Run for 180 frames with Vulkan.
- [ ] Commit the Player migration milestone.

### Task 3: Host Vulkan inside the Studio World viewport

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeVulkanViewportHost.cs`
- Create: `src/Rekall.Age.Studio/RekallAgeStudioVulkanPreviewSession.cs`
- Modify: `src/Rekall.Age.Studio/Rekall.Age.Studio.csproj`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioPreviewSessionTests.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs`

**Interfaces:**
- Consumes: shared presentation session and the existing Studio preview runtime world.
- Produces: a WPF child HWND renderer, Vulkan backend/status telemetry, and unchanged viewport interaction metadata.

- [ ] Add failing tests requiring Studio preview frames to report Vulkan hardware acceleration, requiring `MainWindow` to host `RekallAgeVulkanViewportHost`, and forbidding normal `RenderRgba` calls in Studio.
- [ ] Run the two focused Studio test classes and verify RED.
- [ ] Implement the `HwndHost` child surface with resize, DPI, focus, pointer-coordinate forwarding, and deterministic disposal.
- [ ] Split Studio preview into runtime stepping/frame building and shared Vulkan presentation; retain `RekallAgeStudioViewportInteractionBuilder` for picking.
- [ ] Replace the WPF bitmap image with the Vulkan host while retaining the viewport header, transforms, selection, status, and unavailable placeholder.
- [ ] Surface Vulkan initialization/device errors in validation and status without software fallback.
- [ ] Run the focused Studio tests and build Studio to GREEN.
- [ ] Commit the Studio Vulkan milestone.

### Task 4: Real-time simulation cadence

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioPreviewCadence.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `tests/Rekall.Age.Studio.Tests/StudioPreviewCadenceTests.cs`
- Modify: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`

**Interfaces:**
- Consumes: persistent Vulkan `PresentAsync` and runtime fixed-step APIs.
- Produces: 60 Hz target presentation, bounded elapsed-time catch-up, and newest-state-only rendering.

- [ ] Add failing tests for one-frame on-time ticks, bounded catch-up after a missed deadline, pause accumulator reset, single-step exactly one frame, and stop/reset behavior.
- [ ] Run only the cadence and named ViewModel tests and verify RED.
- [ ] Implement a monotonic elapsed-time accumulator capped at six fixed steps, present once per callback, and reset timing on simulate/pause/resume/stop transitions.
- [ ] Run the focused cadence and ViewModel tests to GREEN.
- [ ] Commit the cadence milestone.

### Task 5: Playable acceptance and integration

**Files:**
- Modify only files required by defects found in the acceptance run.

**Interfaces:**
- Consumes: shared renderer in Player and Studio.
- Produces: measured end-to-end evidence for Summit Run.

- [ ] Build Studio and Windows Player with zero warnings and errors.
- [ ] Launch Studio with `Examples/SummitRun`, open World, verify backend `vulkan`, select Rover from the viewport, and confirm Inspector updates.
- [ ] Simulate for at least ten seconds, record frame/time cadence and responsiveness, then verify pause, step, resume, and stop.
- [ ] Launch the Windows Player for 180 frames and verify it reports Vulkan success through the same renderer.
- [ ] Run `git diff --check` and the narrowly relevant Studio/Player tests.
- [ ] Commit acceptance repairs, fast-forward the tested commit to the main checkout, rebuild installed artifacts, and leave Summit Run open in Studio.

