# Windows Package Launchers Design

## Goal

Windows graphical packages must give players an obvious `Play.exe` that can be double-clicked without command-line arguments or a separately installed .NET runtime. They must also include `Play.bat` as a transparent fallback.

## Design

The existing Windows player remains the only runtime executable. Packaging publishes it self-contained for `win-x64`, copies its app host to `Play.exe`, and records `Play.exe` as the manifest launch path. No game-specific launcher project or behavior is introduced.

When the Windows player receives no arguments, a generic package-launch resolver reads `rekall.package.json` beside the running executable. It validates that the manifest and game root remain inside the package, converts the manifest's relative game-root argument to an absolute path, and returns the remaining generic player arguments unchanged. Explicit command-line arguments retain their current behavior.

`Play.bat` contains no authored scene data or shell interpolation. It invokes the adjacent `Play.exe` with no arguments, relying on the same validated manifest path. Both launchers are included in the package integrity inventory and archive.

## Acceptance

- A graphical Windows package returns and records `Play.exe` as `LaunchPath`.
- `Play.exe` is a self-contained `win-x64` publish and resolves launch arguments from an adjacent manifest when double-clicked.
- `Play.bat` invokes `%~dp0Play.exe` and propagates its exit code.
- Explicit player arguments continue to work unchanged.
- Manifest traversal or an absolute packaged game root is rejected.
- The launchers remain valid after relocating a package to a path containing spaces.
- Existing package inspection, audit, capture, and archive behavior remains green.

