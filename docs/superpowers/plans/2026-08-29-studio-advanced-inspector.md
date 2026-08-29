# Studio Advanced Inspector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Widen Studio's default hierarchy/Inspector panels and replace the flat Inspector presentation with a searchable, schema-driven component browser and editor.

**Architecture:** Keep `RekallAgeInspectorModel` authoritative and add a small pure projection helper for filtering attached components. The Studio view model publishes observable projected components and selection summaries; the WPF World workspace renders them as generic cards while all mutations continue through the existing component/property commands.

**Tech Stack:** C# 14, .NET 10, WPF XAML, xUnit

**Spec:** `docs/superpowers/specs/2026-08-29-studio-advanced-inspector-design.md`

## Global Constraints

- Do not add component-specific UI or gameplay behavior.
- Preserve manually resized saved panel widths.
- Keep existing generic component and property mutation commands authoritative.
- Run only focused Studio tests during implementation.

---

### Task 1: Wider Defaults and Legacy Layout Migration

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioLayout.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Test: `tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs`

**Interfaces:**
- Consumes: `RekallAgeStudioLayout.Normalize(RekallAgeStudioLayout?)`
- Produces: layout version 3 with 340/460 default widths and migration of the exact legacy preset pairs

- [ ] **Step 1: Write failing layout tests**

```csharp
Assert.Equal(340, RekallAgeStudioLayout.Default.Panel("Hierarchy").Size);
Assert.Equal(460, RekallAgeStudioLayout.Default.Panel("Inspector").Size);
Assert.Equal(460, RekallAgeStudioLayout.Normalize(legacyDefault)!.Panel("Inspector").Size);
Assert.Equal(447, RekallAgeStudioLayout.Normalize(custom)!.Panel("Inspector").Size);
```

- [ ] **Step 2: Run the focused tests and verify the old widths fail**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --no-restore -m:1 --filter "FullyQualifiedName~StudioLayoutTests"`

- [ ] **Step 3: Implement version 3 defaults and migration**

Set default/preset widths to `340/460`, `360/500`, and `330/460`. In `Normalize`, recognize versions 1, 2, and 3; map only exact version-2 legacy pairs `290/370`, `300/390`, and `270/340` to their corresponding new pairs before ordinary clamping.

- [ ] **Step 4: Update the XAML column fallbacks and run the layout tests**

Set `HierarchyColumn` to 340 and `InspectorColumn` to 460. Re-run the Task 1 command and expect all `StudioLayoutTests` to pass.

### Task 2: Generic Inspector Component Projection

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioInspectorBrowser.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioInspectorBrowserTests.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<RekallAgeInspectorComponentModel>`
- Produces: `RekallAgeStudioInspectorBrowser.Project(components, query, selectedType)` returning `RekallAgeStudioInspectorBrowserResult`

- [ ] **Step 1: Write failing projection tests**

```csharp
var result = RekallAgeStudioInspectorBrowser.Project(components, "speed", "Game.Vehicle");
Assert.Equal(["Game.Vehicle"], result.Components.Select(item => item.Type));
Assert.Equal("Game.Vehicle", result.SelectedComponent?.Type);
```

Also assert case-insensitive matches across display name, type, description, property name/value; no-match returns an empty collection and null selection; an unavailable selection falls back to the first visible component.

- [ ] **Step 2: Run and verify the missing browser type fails compilation**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --no-restore -m:1 --filter "FullyQualifiedName~StudioInspectorBrowserTests"`

- [ ] **Step 3: Implement the pure projection helper**

```csharp
internal sealed record RekallAgeStudioInspectorBrowserResult(
    IReadOnlyList<RekallAgeInspectorComponentModel> Components,
    RekallAgeInspectorComponentModel? SelectedComponent);
```

Implement ordinal-ignore-case matching with `Contains(term, StringComparison.OrdinalIgnoreCase)` and no mutation of the source list.

- [ ] **Step 4: Run the projection tests and expect them all to pass**

Run the Task 2 command.

### Task 3: Inspector View-Model State and Selection Synchronization

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`

**Interfaces:**
- Consumes: `RekallAgeStudioInspectorBrowser.Project`
- Produces: `InspectorComponents`, `InspectorSearchInput`, `SelectedInspectorComponent`, `InspectorSelectionName`, `InspectorSelectionId`, `InspectorComponentCountText`, `InspectorComponentBrowserEmptyText`, and `SelectedInspectorComponentDescription`

- [ ] **Step 1: Write failing view-model tests**

Open a scene entity with multiple components, then assert the projected component count and selection summary. Set `InspectorSearchInput`, assert only matching cards remain, assign `SelectedInspectorComponent`, and assert `ComponentTypeInput` and property schemas synchronize.

- [ ] **Step 2: Run the exact new view-model test and verify missing properties fail**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --no-restore -m:1 --filter "FullyQualifiedName~AdvancedInspector"`

- [ ] **Step 3: Implement observable projection state**

Refresh the projected collection from `_currentModel.Inspector.Components` during `ApplyModel` and whenever the query changes. Preserve the selected component type where possible, fall back to the first visible component, and route selection through `ComponentTypeInput`.

- [ ] **Step 4: Run the new test and nearby empty-Inspector test**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --no-restore -m:1 --filter "FullyQualifiedName~AdvancedInspector|FullyQualifiedName~EmptyProjectInspector"`

### Task 4: Advanced Inspector WPF Surface

**Files:**
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Test: `tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs`

**Interfaces:**
- Consumes: Task 3 view-model properties and existing add/remove/set/reset commands
- Produces: `InspectorSearchBox`, `InspectorComponentList`, structured component cards, selection metadata, and schema-driven editing panel

- [ ] **Step 1: Write a failing XAML structure test**

Assert `MainWindow.xaml` contains `InspectorSearchBox`, `InspectorComponentList`, bindings for `InspectorSelectionName`, `InspectorComponentCountText`, `SelectedInspectorComponent`, and `SelectedInspectorComponentDescription`, plus the existing mutation commands.

- [ ] **Step 2: Run `StudioLayoutTests` and verify the new structure assertions fail**

Run the Task 1 test command.

- [ ] **Step 3: Replace the flat Inspector visual tree**

Build a grid with selection header, search field, star-sized component-card list, and a bordered editor footer. Use a `ListBox` bound two-way to `SelectedInspectorComponent`; card templates show component metadata and defined property rows. Keep add/replace/remove and property set/remove controls in the footer.

- [ ] **Step 4: Run the layout and view-model tests**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --no-restore -m:1 --filter "FullyQualifiedName~StudioLayoutTests|FullyQualifiedName~AdvancedInspector|FullyQualifiedName~EmptyProjectInspector"`

### Task 5: Build and Live Acceptance

**Files:**
- Verify: `src/Rekall.Age.Studio/Rekall.Age.Studio.csproj`

**Interfaces:**
- Consumes: Tasks 1-4
- Produces: a running Studio with Summit Run loaded in World view

- [ ] **Step 1: Build Studio**

Run: `dotnet build src/Rekall.Age.Studio/Rekall.Age.Studio.csproj --no-restore -m:1`

- [ ] **Step 2: Restart Studio and load `F:\Dev\Rekall_AGE\Examples\SummitRun`**

Open World view, select Rover and Camera, search attached components, select cards, and confirm property metadata/value controls update without authoring changes.

- [ ] **Step 3: Check the current Studio log**

Inspect `%LOCALAPPDATA%\Rekall AGE\Studio\Logs\studio-*.log` for new error/fatal entries after startup and interaction.

- [ ] **Step 4: Commit the implementation**

```powershell
git add -- src/Rekall.Age.Studio tests/Rekall.Age.Studio.Tests
git commit -m "feat: advance Studio scene inspector"
```

