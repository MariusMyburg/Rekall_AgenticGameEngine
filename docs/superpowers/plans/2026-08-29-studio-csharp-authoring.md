# Studio C# Authoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Studio users create, attach, edit, build, and externally debug project-owned C# gameplay through one Code workspace.

**Architecture:** Add an engine-level deterministic IDE-workspace generator, then a focused Studio code session for safe project-local source editing and process launch intent. The main Studio ViewModel orchestrates existing canonical scaffold/build/component commands and exposes a dedicated Code workspace.

**Tech Stack:** .NET 10/C# 13, WPF, xUnit, JSON via `System.Text.Json`, Rekall AGE command/workbench contracts, Visual Studio `.slnx` and `launchSettings.json`, VS Code `launch.json`/`tasks.json`.

**Spec:** `docs/superpowers/specs/2026-08-29-studio-csharp-authoring-design.md`

## Global Constraints

- Game behavior remains in project-owned modules under `Modules/`.
- Studio must use canonical scaffold, build, and component mutation commands.
- F5 configurations must launch the production Windows Player with the active project root and scene.
- Generated IDE files must never overwrite authored C# or module project files.
- Embedded editing is restricted to source paths returned by `rekall.module.list_sources`.
- Do not add Roslyn, hot reload, or arbitrary out-of-project file editing in this milestone.

---

### Task 1: Deterministic IDE development workspace

**Files:**
- Create: `src/Rekall.Age.Editor/Development/RekallAgeProjectDevelopmentWorkspace.cs`
- Test: `tests/Rekall.Age.Tests/Editor/ProjectDevelopmentWorkspaceTests.cs`

**Interfaces:**
- Consumes: absolute game project root, scene name, Player executable path, optional CLI executable path.
- Produces: `GenerateAsync(RekallAgeProjectDevelopmentWorkspaceRequest, CancellationToken)` returning generated solution, debug project, Visual Studio launch settings, and VS Code configuration paths.

- [ ] **Step 1: Write failing generation and regeneration tests**

Create a fixture with `rekall.project.json`, two `Modules/*/*.csproj` files, and authored `.cs` content. Assert the wished-for generator emits a deterministic `.slnx`, generated debug project, valid JSON configurations, exact Player arguments, and leaves authored bytes unchanged after regeneration.

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ProjectDevelopmentWorkspaceTests"`

Expected: compilation fails because `RekallAgeProjectDevelopmentWorkspace` and its request/result contracts do not exist.

- [ ] **Step 3: Implement validated atomic generation**

Implement focused helpers that discover `Modules/*/*.csproj`, XML-escape relative paths, serialize JSON with indentation, and write generated files through `RekallAgeAtomicFile.WriteAllTextAsync`. Use this launch profile shape:

```json
{
  "profiles": {
    "Rekall AGE Game": {
      "commandName": "Executable",
      "executablePath": "<absolute player>",
      "commandLineArgs": "\"<project root>\" \"<scene>\" --graphics --backend vulkan",
      "workingDirectory": "<project root>"
    }
  }
}
```

Emit `.vscode/launch.json` with `type: coreclr`, `request: launch`, `program` equal to the Player path, and the same argument vector.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Task 1 filter and expect all `ProjectDevelopmentWorkspaceTests` to pass with no warnings.

- [ ] **Step 5: Commit**

Commit: `feat: generate game IDE debug workspaces`

### Task 2: Safe Studio C# source session

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioCodeSession.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioCodeSessionTests.cs`

**Interfaces:**
- Consumes: active project root and canonical `ListModuleSourcesCommand`/`WriteModuleSourceCommand` results.
- Produces: `RefreshAsync`, `OpenAsync`, `SaveAsync`, `GenerateDevelopmentWorkspaceAsync`, `OpenFile`, `OpenProject`, `OpenSolution`, and `OpenInVsCode`; exposes selected source, source text, dirty state, and status.

- [ ] **Step 1: Write failing session tests**

Assert source enumeration excludes `bin/obj`, opening loads UTF-8 text, assigning different text marks the session dirty, saving writes only the selected returned source path, and launch methods send exact file/project/solution/folder intents to an injected `IRekallAgeStudioExternalLauncher`.

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~StudioCodeSessionTests"`

Expected: compilation fails because the session and launcher contracts do not exist.

- [ ] **Step 3: Implement the minimal session and shell launcher**

Use canonical module source commands for enumeration/write, `File.ReadAllTextAsync` only after matching a returned source path, and a default launcher based on `ProcessStartInfo` argument lists. `OpenInVsCode` launches `code` with the project root; other open operations use shell association.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Task 2 filter and expect all `StudioCodeSessionTests` to pass.

- [ ] **Step 5: Commit**

Commit: `feat: add Studio C# source session`

### Task 3: Canonical create, build, and attach orchestration

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`

**Interfaces:**
- Consumes: selected entity, module/component/system form fields, canonical `rekall.module.scaffold_runtime_system`, `rekall.build.modules`, and `rekall.component.add` commands.
- Produces: observable code-source collections/editor fields and commands `RefreshCodeCommand`, `SaveCodeCommand`, `BuildCodeCommand`, `CreateAttachCodeComponentCommand`, `OpenCodeFileCommand`, `OpenCodeProjectCommand`, `OpenCodeSolutionCommand`, `OpenCodeInVsCodeCommand`.

- [ ] **Step 1: Write a failing end-to-end ViewModel test**

Create a disposable project and entity through the existing ViewModel. Set `CodeModuleNameInput = "Mover"`, `CodeComponentNameInput = "MoverState"`, and `CodeSystemNameInput = "MoverSystem"`; execute the wished-for create/attach command. Assert the module source exists, build output succeeds, the selected entity Inspector includes `Game.Modules.Mover.MoverState`, and the source editor selects `MoverModule.cs`.

- [ ] **Step 2: Run the exact test and verify RED**

Run only the new test by fully qualified name. Expected: compilation fails on the missing Code properties/command.

- [ ] **Step 3: Implement minimal orchestration**

Dispatch scaffold with all required fields, stop on failure, dispatch build, stop on failure, then attach using:

```json
{
  "componentType": "Game.Modules.Mover.MoverState",
  "properties": { "enabled": true, "valuePerSecond": 1 }
}
```

Use the actual namespace/component values returned by the scaffold result. Reload the Workbench model and refresh code sources after success. Surface compiler errors in `CodeOutputLines` without deleting source.

- [ ] **Step 4: Run the exact test, adjacent ViewModel tests, and verify GREEN**

Run the new test, then the existing source-selection/component tests touched by command refresh behavior.

- [ ] **Step 5: Commit**

Commit: `feat: create and attach C# entity components`

### Task 4: Dedicated Code workspace UI

**Files:**
- Create: `src/Rekall.Age.Studio/CodeWorkspace.xaml`
- Create: `src/Rekall.Age.Studio/CodeWorkspace.xaml.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioLayout.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs`

**Interfaces:**
- Consumes: Task 3 observable properties and commands.
- Produces: top-level Code tab with new-component form, source tree/list, editor, build/output panel, and IDE actions.

- [ ] **Step 1: Write failing layout/source assertions**

Assert MainWindow has `Header="Code"` and `CodeWorkspaceHost`; CodeWorkspace binds every Task 3 command, uses `AcceptsReturn="True"`, `AcceptsTab="True"`, a monospace font, and keeps Create/build/attach plus IDE actions visible.

- [ ] **Step 2: Run the layout test and verify RED**

Run the `StudioLayoutTests` filter. Expected: missing Code workspace assertions fail.

- [ ] **Step 3: Implement the WPF workspace and dirty-change guard**

Follow the existing Author/Modeling UserControl pattern. Handle source selection in code-behind only to present Save/Discard/Cancel before changing a dirty editor; all mutations remain ViewModel commands.

- [ ] **Step 4: Run layout and Studio code tests and verify GREEN**

Run `StudioLayoutTests|StudioCodeSessionTests` and the Task 3 ViewModel test.

- [ ] **Step 5: Commit**

Commit: `feat: add Studio Code workspace`

### Task 5: Acceptance, diagnostics, and live IDE artifact proof

**Files:**
- Modify if needed: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify if needed: focused tests from Tasks 1–4.

**Interfaces:**
- Consumes: completed Code workflow.
- Produces: verified disposable create/build/attach evidence and Neon Orchard IDE workspace artifacts without changing Neon Orchard gameplay source.

- [ ] **Step 1: Run focused automated acceptance**

Run the Task 1–4 test classes together. Expect all pass with no warnings.

- [ ] **Step 2: Build Studio and Windows Player**

Run `dotnet build` for both projects with `--no-restore`. Expect zero warnings/errors.

- [ ] **Step 3: Live-test Studio**

Open Neon Orchard, select Fruit1, enter Code, open an existing module source, generate the development workspace, and confirm the displayed source/project/solution paths exist. Do not save changes to Neon Orchard source.

- [ ] **Step 4: Validate generated IDE artifacts**

Parse both JSON files, run `dotnet sln <generated.slnx> list`, and assert the launch configurations contain the exact production Player, Neon Orchard root, and Main scene. Shell-open the solution only if a registered IDE exists; leave Studio open in Code.

- [ ] **Step 5: Review and final commit**

Run `git diff --check`, request focused code review, address critical/important findings, rerun affected tests, and commit as `feat: complete Studio C# authoring workflow`.
