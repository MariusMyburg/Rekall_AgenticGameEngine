# Studio Live Preview and Ergonomics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver explicit Edit/Simulate/Play Studio modes, persistent in-editor simulation preview, and a coherent modern dark WPF shell.

**Status (2026-08-21 23:25):** Tasks 1-3 implementation, verification, independent review, branch checkpoint/push, fast-forward integration at `1dccb1c`, and merged-master Studio verification are complete: 19/19 Studio tests, 1,111/1,111 engine tests, zero-warning Debug and Release builds, real Windows Edit/Simulate/Stop inspection, and no remaining Critical/Important review findings. The final progress checkpoint and master push remain.

**Architecture:** A focused preview-session service owns generic runtime state and rendering; the view model owns editor-mode transitions; the window owns the dispatcher cadence. Application resources style the complete control family consistently.

**Tech Stack:** C# 13, .NET 10, WPF, Rekall runtime/rendering contracts, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-21-studio-live-preview-ergonomics-design.md`

## Global Constraints

- Preserve generic agent-authoring primitives and avoid game-specific behavior.
- Simulate never writes runtime state back to authored scene documents.
- Play uses the production windowed Player process.
- Live preview work is cancellation-safe, single-flight, and bounded to 10 Hz.
- All new WPF resources use the existing product without a new UI dependency.

---

### Task 1: Persistent Studio preview session

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioPreviewSession.cs`
- Modify: `src/Rekall.Age.Studio/Rekall.Age.Studio.csproj`
- Test: `tests/Rekall.Age.Studio.Tests/StudioPreviewSessionTests.cs`

**Interfaces:**
- Produces: `ResetAsync(projectRoot, sceneName, width, height, token)`, `StepAsync(frameCount, token)`, and `RekallAgeStudioPreviewFrame`.
- Consumes: `RekallAgeRuntimeSnapshotService`, `RekallAgeRuntimeExecutionLoop`, `RekallAgeRuntimeRenderFrameBuilder`, asset resolver, and software renderer.

- [ ] Write a failing test that resets a small animated/runtime scene, steps one then six frames, and asserts frame indices persist and increase rather than restarting.
- [ ] Run `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter FullyQualifiedName~StudioPreviewSessionTests` and confirm the missing type failure.
- [ ] Implement a disposable single-owner preview session that freezes a `BitmapSource`, caps dimensions at 1920×1080 and step batches at 60, and exposes renderable/observation facts.
- [ ] Run the focused preview tests and confirm they pass.
- [ ] Commit the preview-session slice.

### Task 2: Edit, Simulate, Play state machine

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`

**Interfaces:**
- Produces: `RekallAgeStudioMode`, `Mode`, `ModeLabel`, `IsSimulating`, `IsLiveViewportEnabled`, `SimulateCommand`, and `AdvanceLivePreviewAsync()`.
- Consumes: Task 1 preview session.

- [ ] Write failing tests proving Edit→Simulate→Edit, Edit→Play→Edit command state, simulation frame advancement, and preview reset after authored mutation.
- [ ] Run the focused mode tests and confirm they fail against the current Play/Stop-only model.
- [ ] Implement mutually exclusive mode transitions, reusable stop behavior, preview cancellation, and a 100 ms dispatcher timer that advances six fixed runtime frames only in Simulate.
- [ ] Keep explicit Capture as proof capture and use the preview service for automatic edit/simulate frames.
- [ ] Run all Studio tests and confirm they pass.
- [ ] Commit the mode slice.

### Task 3: Modern coherent Studio shell

**Files:**
- Modify: `src/Rekall.Age.Studio/App.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Consumes: Task 2 mode properties and commands.
- Produces: shared styles for buttons, editors, selectors, lists, trees, tabs, toolbars, mode badges, and viewport empty state.

- [ ] Add a failing source-level style contract test requiring dark base controls, Segoe UI, accent/focus states, Simulate command binding, Live toggle, and mode badge.
- [ ] Run the focused style test and confirm failure against the current raw WPF control surface.
- [ ] Implement the resource palette/templates and reorganize the toolbar into project, edit, run-mode, proof, and delivery groups with concise labels/tooltips.
- [ ] Build and launch Studio, capture the real window, and visually inspect hierarchy, contrast, disabled states, mode affordance, and absence of white control panels.
- [ ] Run all Studio tests, full engine tests, and a zero-warning Release solution build; update `PROGRESS.md` with exact evidence.
- [ ] Request independent review, commit, push the branch, merge to master, verify merged master, and push master.
