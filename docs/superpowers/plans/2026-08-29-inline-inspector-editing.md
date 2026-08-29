# Inline Inspector Editing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every displayed Inspector property directly editable with a schema-appropriate control while preserving canonical transactions and validation.

**Architecture:** Add Studio-only mutable row models over immutable inspector projections. Parameterized row commands submit native JSON through existing component mutation commands. WPF typed templates become the primary workflow; the lower form becomes an advanced fallback.

**Tech Stack:** .NET 10, C# 13, WPF, `System.Text.Json.Nodes`, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-29-inline-inspector-editing-design.md`

## Global Constraints

- Preserve generic component authoring and native JSON values.
- Reuse canonical set/remove property commands and transaction history.
- Commit on semantic boundaries, never per keystroke.
- Preserve dirty invalid drafts after command rejection.
- Use schema metadata; never guess entity references from property names.
- Run narrowly targeted tests.

---

### Task 1: Mutable typed property rows

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioInspectorPropertyEditorModel.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioInspectorPropertyEditorModelTests.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`

- [ ] Add failing conversion tests for scalar, integer, boolean, enum, asset/entity reference, color, vector, JSON, range, NaN/infinity, and undefined properties.
- [ ] Implement metadata-first template selection and native `JsonNode` conversion with invariant parsing.
- [ ] Add parameterized Commit/Reset commands that call canonical component commands and retain rejected drafts.
- [ ] Build row groups from each visible component while preserving dirty drafts by entity/component/property key.
- [ ] Run focused model/ViewModel tests and commit.

### Task 2: Inline WPF editor templates

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioInspectorEditorTemplateSelector.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Test: `tests/Rekall.Age.Tests/Editor/StudioWorkbenchSourceTests.cs`

- [ ] Add failing source tests requiring typed templates and per-row command parameters.
- [ ] Render direct boolean, numeric, enum, reference, color, vector, string, and JSON controls inside each component card.
- [ ] Wire semantic commit/reset boundaries and inline validation without per-keystroke transactions.
- [ ] Rename/demote the lower panel to component management and advanced custom JSON.
- [ ] Run focused source/Studio tests and commit.

### Task 3: Transactional and live acceptance

**Files:**
- Modify only implementation/tests required by acceptance defects.

- [ ] Prove inline commits persist correct native JSON and create one transaction.
- [ ] Prove Undo/Redo restores/reapplies the exact value and viewport refresh path.
- [ ] Prove local/server-invalid drafts remain visible and scene state is unchanged.
- [ ] Live-test number, boolean, enum/reference, color/vector/JSON, component management, and advanced custom property workflows.
- [ ] Build Studio, run focused tests, review, commit, and include in final Summit Run acceptance.

