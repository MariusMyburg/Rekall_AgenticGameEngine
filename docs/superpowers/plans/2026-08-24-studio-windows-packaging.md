# Studio Windows Packaging Implementation Plan
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:test-driven-development for each implementation task and superpowers:verification-before-completion before declaring completion.

**Goal:** Make Studio produce and expose a runnable Windows package by default while retaining a generic, explicit headless target.

**Architecture:** Add target resolution at the workflow boundary, route CLI and Studio through it, and expose immutable delivery artifacts through the Studio view model and a dedicated Delivery tab. Preserve `Graphics` and `--graphics` compatibility.

**Tech Stack:** .NET 10, C#, WPF/XAML, xUnit, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-08-24-studio-windows-packaging-design.md`

## Global Constraints

- Preserve generic package semantics; do not introduce game- or genre-specific behavior.
- Return structured command errors for invalid target input.
- Keep existing request JSON and CLI invocations working.
- Use test-first red/green/refactor cycles.
- Do not weaken existing playable verification or package audit behavior.

## Task 1: Add the explicit package target contract

- [ ] Add failing workflow tests for explicit Windows/headless targets, legacy graphics mapping, unknown targets, and conflicting options in `tests/Rekall.Age.Tests/Workflows/PlayablePackageIntegrityTests.cs` or a focused new test file.
- [ ] Add `RekallAgePlayablePackageTargets` and `Target` to the request/result contracts in `src/Rekall.Age.Workflows/Commands/PackagePlayableGameCommand.cs`.
- [ ] Resolve the target before verification/build work and return structured errors for invalid input.
- [ ] Route build, proof-player, launcher, and arguments through the resolved target.
- [ ] Run focused workflow tests.

## Task 2: Route the CLI through targets

- [ ] Add failing CLI source/routing tests for `--target windows`, `--target headless`, and the retained `--graphics` alias.
- [ ] Update dispatch and `PackagePlayableGameAsync` in `src/Rekall.Age.Cli/Program.cs`.
- [ ] Print the resolved target with other package artifacts.
- [ ] Run focused CLI tests.

## Task 3: Add Studio delivery state and actions

- [ ] Add failing Studio tests for the Windows default, package target payload, artifact population, and open-folder enablement/callback.
- [ ] Add target choices and selected target to `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`.
- [ ] Send `target` in the package request and populate output, launch, and archive properties from success.
- [ ] Add an injectable folder-opening action and `OpenPackageFolderCommand`; default it to Windows Explorer.
- [ ] Ensure command refresh and failures leave state inspectable.
- [ ] Run focused Studio tests.

## Task 4: Build the Delivery UI

- [ ] Add failing XAML source assertions for target selector, artifact bindings, and Open Package Folder.
- [ ] Add a compact target selector near Package and a dedicated Delivery tab to `src/Rekall.Age.Studio/MainWindow.xaml`.
- [ ] Use clear labels suitable for non-developers and preserve existing Package/Audit controls.
- [ ] Run focused Studio source tests.

## Task 5: Verify the end-to-end milestone

- [ ] Run workflow, CLI, and Studio focused suites.
- [ ] Run the full engine and Studio test suites.
- [ ] Package a small verified project with target `windows`, inspect the archive for `Play.exe` and `Play.bat`, and confirm the manifest launch path.
- [ ] Review the diff for compatibility, genericity, and user-visible clarity.
- [ ] Commit, fast-forward integration if clean, verify commit identity/worktree state, and push `master`.

