# Generic Controller Input Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete generic keyboard, mouse, gamepad, joystick, and XR input across runtime, Windows Player, CLI, MCP, SDK, diagnostics, and packages.

**Architecture:** Extend the existing immutable runtime input and semantic-action system with structured controllers and device facts. Capture SDL controllers through an injectable native adapter, expose generic inspect/rebind commands, and preserve scene-authored portable defaults plus optional user overrides.

**Tech Stack:** C# 13, .NET 10, SDL2 shipped by Veldrid, System.Text.Json, xUnit, Rekall AGE command/MCP contracts.

**Spec:** `docs/superpowers/specs/2026-08-22-generic-controller-input-design.md`

## Global Constraints

- Preserve all existing keyboard, mouse, deterministic semantic input, and OpenXR behavior.
- Gameplay consumes semantic scalar actions; no genre/controller behavior enters the engine.
- Device state and authored bindings are bounded, finite, normalized, deterministic, and inspectable.
- No new native/package dependency; use the SDL2 already distributed with the Player.

---

### Task 1: Structured controller runtime contracts and projection

**Files:**
- Modify: `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeInputActionSystem.cs`
- Modify: `src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs`
- Modify tests: `tests/Rekall.Age.Tests/Runtime/InputActionSystemTests.cs`
- Modify tests: `tests/Rekall.Age.Tests/Runtime/RuntimeInputInspectionTests.cs`

- [x] Write failing hand-derived gamepad/joystick axis, button, hat, filtering, deadzone, inversion, edge, alias, and malformed-binding tests.
- [x] Verify failure because controller state/bindings do not exist.
- [x] Add bounded immutable contracts and deterministic frame conversion.
- [x] Implement stable controller binding projection and structured observations.
- [ ] Run input/runtime tests green and commit.

### Task 2: SDL controller and joystick capture

**Files:**
- Create: `src/Rekall.Age.Player.Windows/RekallAgeSdlControllerInputSource.cs`
- Modify: `src/Rekall.Age.Player.Windows/Program.cs`
- Create: `tests/Rekall.Age.Tests/PlayerWindows/SdlControllerInputSourceTests.cs`

- [ ] Write failing fake-native tests for mapping, normalization, button edges, hot-plug, disconnect release, raw-joystick fallback, bounds, and handle cleanup.
- [ ] Verify the source is absent.
- [x] Implement SDL2 native adapter and bounded polling source.
- [x] Merge snapshots into every Player runtime frame and held-input bridge.
- [ ] Run focused tests green and commit.

### Task 3: Inspect, rebind, overrides, CLI, and MCP

**Files:**
- Create: `src/Rekall.Age.Runtime/Commands/InspectInputCommand.cs`
- Create: `src/Rekall.Age.World/Commands/RebindInputActionCommand.cs`
- Create: `src/Rekall.Age.Runtime/RekallAgeInputOverrideStore.cs`
- Modify: `src/Rekall.Age.Workflows/RekallAgeDefaultCommandRegistry.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Add focused command, CLI, MCP, and store tests under `tests/Rekall.Age.Tests`.

- [x] Write failing tests for bounded inspect output, transactional scene rebind/remove, CLI routes, and MCP schemas.
- [x] Verify missing command/route failures.
- [x] Implement typed inspect and transactional scene rebind/remove commands.
- [x] Register commands and CLI routes; prove MCP discovery.
- [ ] Run focused tests green and commit.

### Task 4: Validation, SDK/agent documentation, and installed proof

**Files:**
- Modify input validation, SDK inspection, README, and `docs/production/PROGRESS.md`.
- Add validation/module/schema/package acceptance tests.

- [x] Write failing validation/schema/SDK tests for canonical fields and diagnostics.
- [x] Implement exact component descriptions and agent guidance.
- [ ] Run complete input, engine, Studio, Debug, and Release gates.
- [ ] Run optional live device inspection and a relocated packaged semantic-input proof.
- [ ] Record evidence, review, commit, push, and merge.
