# Studio Windows Packaging Design

## Outcome

Rekall AGE Studio packages an open project as a runnable Windows game by default. The delivery UI makes the selected target and generated artifacts explicit, so a non-developer can package, locate, and share a game without knowing command-line flags.

## Architectural References

- Godot's export preset separates the export platform, runnable state, and export path. Rekall AGE adopts the same separation through an explicit package target and visible output paths, without copying Godot code.
- Blender exposes output configuration in the editor and runs delivery work through operators that report actionable diagnostics. Rekall AGE keeps packaging command-driven and surfaces its result in a dedicated Delivery tab.

## Package Target Contract

`PackagePlayableGameRequest` gains an optional `Target` string. Supported values are:

- `windows`: publish the graphical Windows player, create `Play.exe` and `Play.bat`, and retain the headless proof player used by package audit.
- `headless`: publish the current automation/server-oriented package without a graphical launcher.

The existing `Graphics` property remains for JSON and source compatibility. A missing target resolves from `Graphics`; `Graphics: true` maps to `windows`, otherwise to `headless`. An unknown target or a conflicting `Target: headless` plus `Graphics: true` returns a structured command failure. Results report the resolved target.

The CLI gains `--target windows` and `--target headless`; `--graphics` remains a compatibility alias for Windows packaging.

## Studio Delivery Experience

Studio defaults `SelectedPackageTarget` to `windows` and offers Windows Player and Headless Automation choices. Packaging sends the explicit target rather than hard-coding `graphics = false`.

A Delivery tab displays:

- selected package target;
- package output directory;
- runnable executable or launch artifact;
- shareable ZIP archive;
- the latest package status;
- Package, Audit Package, and Open Package Folder actions.

After a successful package, Studio populates all artifact paths. Open Package Folder launches the OS file browser at the exact output directory and is disabled until that directory exists.

## Verification

Tests cover target resolution, invalid/conflicting requests, CLI routing, Studio's Windows default and request payload, artifact state, open-folder behavior, and XAML bindings. A real workflow package test verifies that the Windows target creates both launchers and that the archive contains them.

