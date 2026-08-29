# Studio Bundled Examples Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bundle all authored examples with Studio and expose them through a safe, discoverable Examples main menu.

**Architecture:** A manifest-driven catalog discovers examples; an atomic library copier creates writable user copies; MainWindow renders a dynamic menu and reuses the existing project-open path. An MSBuild publish-only item group stages examples beside Studio while filtering transient development state.

**Tech Stack:** C# 13, .NET 10 WPF, xUnit, MSBuild

**Spec:** `docs/superpowers/specs/2026-08-30-studio-bundled-examples-design.md`

## Global Constraints

- Never edit the installed or repository example in place.
- Discover projects from `rekall.project.json`; do not hardcode game names or behavior.
- Include authored content and exclude only transient development state.
- Run only focused tests during implementation.

---

### Task 1: Manifest-driven catalog and writable library

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioExampleCatalog.cs`
- Create: `src/Rekall.Age.Studio/RekallAgeStudioExampleLibrary.cs`
- Create: `tests/Rekall.Age.Studio.Tests/StudioExampleLibraryTests.cs`

**Interfaces:**
- Produces: `RekallAgeStudioExampleCatalog.Discover()` returning ordered `RekallAgeStudioExample` records.
- Produces: `RekallAgeStudioExampleLibrary.CopyAsync(example, destination, cancellationToken)` and `FindFreshDestination(...)`.

- [x] Write catalog tests using real temporary project manifests and verify the expected failure before implementation.
- [x] Implement ordered discovery with root precedence and manifest validation.
- [x] Write copy/collision/transient tests and verify the expected failure before implementation.
- [x] Implement atomic, non-overwriting writable copies and rerun the focused test class.

### Task 2: Main menu and publish payload

**Files:**
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Modify: `src/Rekall.Age.Studio/Rekall.Age.Studio.csproj`

**Interfaces:**
- Consumes: `RekallAgeStudioExampleCatalog` and `RekallAgeStudioExampleLibrary` from Task 1.
- Produces: a conventional `File` menu plus a dynamic `Examples` menu whose clicks open writable project copies.

- [x] Add focused source integration assertions for the dynamic menu and World-on-open behavior and verify the expected failure before the UI is wired.
- [x] Add the top menu and route File items through existing handlers.
- [x] Populate example items, handle existing-copy choices, copy asynchronously, and open via `OpenProjectAsync`.
- [x] Add publish-only example content with transient-directory exclusions.
- [x] Run focused tests, build Studio, inspect evaluated publish items, and commit the feature.
