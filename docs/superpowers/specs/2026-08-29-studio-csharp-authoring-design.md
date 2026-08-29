# Studio C# Authoring and IDE Debugging Design

## Purpose

Make authored C# gameplay a first-class Studio workflow. A user should be able to select an entity, create a component and runtime system, attach the component, edit the generated source, build it, and continue editing the scene without learning the command API. The same game project must open cleanly in Visual Studio or VS Code and launch the production Windows Player with F5.

## Product principles

- C# gameplay remains project-owned content under `Modules/`; Studio does not move game rules into the engine.
- Studio orchestrates existing generic commands rather than creating a second module compiler or scene mutation path.
- The embedded editor is intentionally focused: source selection, text editing, save, build, diagnostics, and navigation. Visual Studio and VS Code remain the advanced refactoring and debugging environments.
- F5 launches the production Player with the authored project root and scene. There is no debug-only gameplay runtime.
- Generated IDE files contain project-relative paths wherever possible and may be regenerated safely without overwriting authored C# files.

## User workflow

Studio gains a top-level **Code** workspace beside Author, World, and Modeling.

When an entity is selected, the workspace offers a **New Entity Component** panel with module name, component name, and runtime-system name. **Create, build & attach** performs one understandable operation:

1. Scaffold a runtime-system module through `rekall.module.scaffold_runtime_system`.
2. Build project modules through `rekall.build.modules`.
3. Attach the generated fully-qualified component type to the selected entity through `rekall.component.add`.
4. Reload the Workbench model and select the generated source in the editor.
5. Display each stage and any compiler diagnostics in the Code output area.

Existing source files appear in a module/file list. Selecting one loads its contents. **Save** uses `rekall.module.write_source`; **Build** uses `rekall.build.modules`; **Refresh** re-enumerates source and IDE artifacts. Unsaved text is never silently replaced: changing files or refreshing while dirty requires a Save/Discard/Cancel decision in the view.

The workspace also provides:

- **Open file**: shell-open the selected `.cs` file in its registered editor.
- **Open project**: shell-open the selected module `.csproj`.
- **Open solution**: generate/update the project development workspace, then shell-open its `.slnx` file.
- **Open in VS Code**: generate/update the workspace, then launch VS Code at the game project root.

Studio always displays the exact paths it will open. IDE absence or shell launch failure becomes a visible diagnostic and never corrupts project content.

## Architecture

### Development workspace generator

`RekallAgeProjectDevelopmentWorkspace` is an engine service, not WPF code. Given a project root, scene name, and Player executable, it discovers direct module projects and writes only generated integration files:

- `<project-name>.slnx`, containing every `Modules/*/*.csproj` and a generated debug-launcher project.
- `.rekall/ide/Rekall.Game.Debug/Rekall.Game.Debug.csproj`, a minimal executable project used as Visual Studio's startup project.
- `.rekall/ide/Rekall.Game.Debug/Program.cs`, a harmless explanation/fallback when run without an IDE profile.
- `.rekall/ide/Rekall.Game.Debug/Properties/launchSettings.json`, with `commandName: Executable`, the production Player path, project root, scene, Vulkan graphics arguments, and project-root working directory.
- `.vscode/launch.json`, with a CoreCLR launch configuration pointing at the same Player and arguments.
- `.vscode/tasks.json`, with a module-build task that invokes the existing CLI `build modules` command when a CLI path is available; otherwise it uses `dotnet build` on the generated solution.

Generation validates that the project root, scene, and Player executable are concrete absolute paths. It uses atomic replacement for generated text files. It never edits source files or module project files.

### Studio code session

`RekallAgeStudioCodeSession` isolates file enumeration, safe project-local source reads, dirty state, save, workspace generation, and external-process launch requests from the main ViewModel. It depends on an injectable launcher interface so tests can assert launch intent without opening applications.

The main Studio ViewModel remains responsible for canonical Workbench operations. It coordinates scaffold/build/attach and projects observable state into the Code workspace. This preserves transaction history and ensures component/schema refresh uses the same model as the Inspector.

### UI composition

`CodeWorkspace.xaml` owns the Code-specific layout and binds to the existing main ViewModel, matching the Author and Modeling workspace pattern. The left column contains source navigation and the new-component form. The center is a monospaced multiline editor. The right/bottom area shows module build and IDE-generation results. Primary actions remain visible without scrolling at a 1280×720 window.

## Naming and validation

Module, component, and system names are converted with the same identifier rules as the canonical scaffold command. Blank names are rejected in Studio before dispatch. The generated component type is taken from the scaffold command result rather than reconstructed from unchecked input.

Only source paths returned by `rekall.module.list_sources` may be loaded or saved in the embedded editor. External open operations may target the corresponding `.cs`, direct module `.csproj`, generated `.slnx`, or project root. No arbitrary filesystem editor is introduced in this milestone.

## Failure handling

- Existing module: preserve it, select its source, and explain that Studio will not overwrite it.
- Compilation failure: keep the editor text and show module compiler diagnostics; do not attach an unbuilt component.
- Attachment failure: keep the successfully built module and report the scene error with no compensating source deletion.
- Missing Player: IDE files are not generated; show the same actionable Player-not-found message as Play.
- IDE unavailable: generation succeeds, launch fails visibly, and paths remain available for manual opening.
- Dirty editor: file changes, source refresh, and workspace close route through an explicit Save/Discard/Cancel decision.

## Verification

Automated tests prove:

- Development workspace generation discovers modules and emits parseable `.slnx`, Visual Studio launch settings, and VS Code launch/task JSON with exact Player/project/scene arguments.
- Regeneration is deterministic and preserves authored module source.
- Code-session source enumeration, load, dirty detection, save, and path containment.
- Create/build/attach produces a registered component on the selected entity and exposes it through the Inspector.
- Studio XAML exposes the Code workspace and required actions.
- Focused Studio and module tests pass, followed by clean Studio and Player builds.

Manual acceptance uses Neon Orchard: open the project, select an entity, open Code, inspect an existing module, generate the workspace, and verify the resulting Visual Studio and VS Code debug configurations point to the production Player. A disposable fixture project proves Create/build/attach so Neon Orchard's authored game is not polluted by the test.

## Deferred scope

- Roslyn language services, IntelliSense, refactoring, breakpoints, and an embedded debugger.
- Editing arbitrary C# files outside the active game project.
- Hot reload into a running Player process.
- Automatically selecting a Visual Studio startup project through user-specific `.suo` state.
- macOS/Linux IDE launch profiles and non-Windows Players.
