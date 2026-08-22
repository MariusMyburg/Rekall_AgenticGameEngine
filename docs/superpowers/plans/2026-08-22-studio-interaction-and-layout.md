# Studio Interaction and Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add direct viewport scene editing, pause/single-step simulation, richer generic scene operations, and persistent dockable Studio layouts.

**Architecture:** The engine gains one portable revision-safe entity metadata command. Studio builds pick and gizmo data from its existing runtime preview frame, routes edits through command transactions, controls the existing fixed-step preview loop with an explicit pause state, and persists validated layout preferences outside projects.

**Tech Stack:** C# 13, .NET 9, WPF, System.Text.Json, xUnit, existing Rekall AGE command/runtime/rendering contracts.

**Spec:** `docs/superpowers/specs/2026-08-22-studio-interaction-and-layout-design.md`

## Global Constraints

- No genre-specific behavior in engine or Studio.
- All authored scene mutations use registered transactional commands and remain undoable.
- Simulate remains non-destructive and fixed-step.
- Layout data remains per-user and outside game projects/packages.
- No new network-restored UI dependency.

---

### Task 1: Generic entity metadata mutation

**Files:**
- Create: `src/Rekall.Age.World/Commands/UpdateEntityMetadataCommand.cs`
- Modify: `src/Rekall.Age.Workflows/RekallAgeDefaultCommandRegistry.cs`
- Test: `tests/Rekall.Age.Tests/World/EntityMetadataCommandTests.cs`

**Interfaces:**
- Produces `UpdateEntityMetadataRequest(ProjectRoot, SceneName, EntityId, Name, Visible, Locked, ParentId, ClearParent)` and command `rekall.scene.entity.update_metadata`.

- [ ] Write real-store tests for partial rename/visibility/lock updates, clear/set parent, missing parent, and cycle rejection.
- [ ] Run the focused tests and verify they fail because the command is absent.
- [ ] Implement validation, immutable entity replacement, revision-safe save, changed-resource recording, and registry inclusion.
- [ ] Run the focused tests and all World/LevelDesign tests green.
- [ ] Commit the task.

### Task 2: Versioned workspace layout

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioLayout.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs`

**Interfaces:**
- Produces immutable `RekallAgeStudioLayout`, `RekallAgeStudioDockPanelLayout`, presets, validation, and async `IRekallAgeStudioLayoutStore`.

- [ ] Write tests with literal layouts for default/preset behavior, valid round-trip, invalid/corrupt/future fallback, bounded sizes, and atomic replacement.
- [ ] Run tests and verify expected missing-type failures.
- [ ] Implement models and a path-injected JSON store using temp-write plus atomic replace/move.
- [ ] Run focused tests green and refactor validation without changing behavior.
- [ ] Commit the task.

### Task 3: Preview interaction snapshot and picking

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioViewportInteraction.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioPreviewSession.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewportInteractionTests.cs`
- Modify tests: `tests/Rekall.Age.Studio.Tests/StudioPreviewSessionTests.cs`

**Interfaces:**
- Preview frame adds an immutable interaction snapshot.
- Produces `MapDisplayPoint`, `Pick`, and selected-entity gizmo projection APIs.

- [ ] Write hand-derived mapping and overlapping-region tests, including letterbox rejection, UI/world priority, depth, hidden content, and empty-space selection.
- [ ] Run focused tests and verify missing interaction API failures.
- [ ] Implement bounded pick regions from UI rectangles and camera-projected 2D/3D bounds using render-frame entity ids.
- [ ] Return the snapshot atomically with each frozen preview bitmap.
- [ ] Run focused tests green and commit.

### Task 4: Transactional scene editing and transform gizmos

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.SceneEditing.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Modify tests: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioGizmoTests.cs`

**Interfaces:**
- Adds Select/Move/Rotate/Scale modes, Local/World orientation, snap settings, selected entity state, pointer begin/update/end/cancel, and create/duplicate/rename/delete/reparent/visibility/lock/reset commands.

- [ ] Write view-model tests proving command guards and each generic scene operation through persisted scene state and undo.
- [ ] Write gizmo tests with literal 2D/3D origin/delta/final values, snapping, locked entities, one-commit drag, and cancel.
- [ ] Run focused tests and verify failures for missing commands/state.
- [ ] Implement scene operations through registered commands and preserve selection across model refreshes.
- [ ] Implement overlay handle models and transient drag state; commit one component-property transaction on pointer-up.
- [ ] Route WPF pointer/keyboard events and render selection/gizmo overlay.
- [ ] Run focused tests green and commit.

### Task 5: Pause and exact single-step

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.Simulation.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify tests: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`

**Interfaces:**
- Adds `IsSimulationPaused`, `PauseResumeCommand`, and `StepSimulationCommand`.

- [ ] Write tests proving Pause blocks cadence, Resume continues, Step advances exactly one frame only while paused, and Stop clears pause/frame state.
- [ ] Run focused tests and verify missing-state failures.
- [ ] Implement serialized pause/step transitions and toolbar controls.
- [ ] Run focused tests green and commit.

### Task 6: Dock host and persistence lifecycle

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Modify tests: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioShellTests.cs`

**Interfaces:**
- Exposes named panel visibility/dock/size properties, presets, reset, and debounced save/close flush.

- [ ] Write view-model and shell tests proving panel docking, hide/restore, splitter size updates, preset/reset, load, and shutdown flush.
- [ ] Run focused tests and verify failures for missing layout lifecycle.
- [ ] Replace fixed shell regions with named dock regions, splitters, View menu, panel header actions, and persisted bottom tab.
- [ ] Restore safe window bounds and flush settings on close.
- [ ] Run focused tests green and commit.

### Task 7: Integration verification and evidence

**Files:**
- Modify: `docs/production/PROGRESS.md`
- Update plan checkboxes in this file.

- [ ] Run all Studio tests and relevant engine command/render/runtime tests.
- [ ] Run complete engine tests and Debug/Release solution builds with zero warnings/errors.
- [ ] Launch the real Windows Studio and verify viewport pick, 2D and 3D gizmo edits with undo, duplicate/rename/reparent/visibility/lock/delete, Pause/Step/Stop, dock relocation/resize/hide, and restart persistence.
- [ ] Capture bounded evidence and update the durable progress ledger.
- [ ] Review the diff, commit, push the feature branch, fast-forward master, rerun merged smoke tests, and push master.

