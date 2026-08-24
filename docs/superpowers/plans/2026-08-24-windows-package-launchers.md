# Windows Package Launchers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a directly runnable, self-contained `Play.exe` and fallback `Play.bat` in graphical Windows packages.

**Architecture:** Add a generic, low-level packaged-launch argument resolver to Core so the Windows player can bootstrap from the adjacent package manifest without depending on Workflows. Extend player publishing and package assembly to produce the two human-facing launchers while retaining the existing player payload and manifest integrity model.

**Tech Stack:** C# 13, .NET 10, xUnit, System.Text.Json, Windows `win-x64` app host.

**Spec:** `docs/superpowers/specs/2026-08-24-windows-package-launchers-design.md`

## Global Constraints

- Preserve explicit player command-line behavior.
- Keep launcher behavior generic and manifest-driven; do not add authored-game behavior to the engine.
- Reject package paths that escape the directory containing `rekall.package.json`.
- Generate a self-contained `win-x64` graphical player.
- Include both launchers in the package integrity inventory and archive.

---

### Task 1: Manifest-driven player bootstrap

**Files:**
- Create: `src/Rekall.Age.Core/Product/RekallAgePackagedLaunchResolver.cs`
- Modify: `src/Rekall.Age.Player.Windows/Program.cs`
- Test: `tests/Rekall.Age.Tests/Core/PackagedLaunchResolverTests.cs`

**Interfaces:**
- Consumes: `rekall.package.json` with `kind`, `gameRoot`, `sceneName`, and `arguments`.
- Produces: `RekallAgePackagedLaunchResolver.Resolve(string executablePath, IReadOnlyList<string> suppliedArguments) : string[]`.

- [x] **Step 1: Write failing resolver tests**

Cover explicit arguments, a relocated package path containing spaces, missing manifest, invalid kind, rooted game paths, and traversal outside the package.

- [x] **Step 2: Run tests and confirm RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PackagedLaunchResolverTests`

Expected: compilation fails because `RekallAgePackagedLaunchResolver` does not exist.

- [x] **Step 3: Implement the minimal resolver and player hookup**

If arguments were supplied, return them unchanged. Otherwise read the adjacent manifest, validate its kind and bounded relative game root, create `[absoluteGameRoot, sceneName, ...remainingOptions]`, and call the resolver at the start of the Windows player's `RunAsync` method.

- [x] **Step 4: Run focused tests and confirm GREEN**

Run the command from Step 2. Expected: all resolver tests pass.

- [x] **Step 5: Commit**

Commit message: `feat(player): bootstrap packaged game from manifest`

### Task 2: Human-facing Windows package launchers

**Files:**
- Modify: `src/Rekall.Age.Build/Commands/BuildPlayerCommand.cs`
- Modify: `src/Rekall.Age.Workflows/Commands/PackagePlayableGameCommand.cs`
- Modify: `tests/Rekall.Age.Tests/Workflows/PlayablePackageIntegrityTests.cs`
- Modify: `tests/Rekall.Age.Tests/Build/BuildPlayerCommandTests.cs`

**Interfaces:**
- Consumes: the graphical Windows player app host from `BuildPlayerCommand`.
- Produces: package-root `Play.exe`, package-root `Play.bat`, and manifest `launchPath: "Play.exe"`.

- [x] **Step 1: Write failing packaging and publish tests**

Assert graphical publishing requests `win-x64` self-contained output, graphical packaging returns and records `Play.exe`, `Play.bat` invokes only the adjacent executable, and both files appear in the integrity inventory after relocation.

- [x] **Step 2: Run tests and confirm RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BuildPlayerCommandTests|FullyQualifiedName~GraphicsPackageIncludesDeterministicProofPlayerForCaptureAndAudit"`

Expected: launcher assertions fail because packages still expose `Rekall.Age.Player.Windows.exe` and have no batch fallback.

- [x] **Step 3: Implement self-contained publishing and launcher creation**

For graphical Windows publish add `-r win-x64 --self-contained true`. During graphical package assembly copy the app host to `Play.exe`, write a fixed `Play.bat` that calls `"%~dp0Play.exe"` and returns `%ERRORLEVEL%`, then create the manifest inventory and archive. Return `Play.exe` from `PackagePlayableGameResult.LaunchPath`.

- [x] **Step 4: Run focused tests and confirm GREEN**

Run the command from Step 2. Expected: all selected tests pass.

- [x] **Step 5: Commit**

Commit message: `feat(packaging): add runnable Windows launchers`

### Task 3: End-to-end verification

**Files:**
- Modify: `docs/superpowers/plans/2026-08-24-windows-package-launchers.md`

**Interfaces:**
- Consumes: completed manifest bootstrap and launcher package behavior.
- Produces: recorded verification evidence and a reviewed commit series.

- [x] **Step 1: Run package workflow tests**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PlayablePackageIntegrityTests`

Expected: all package integrity, relocation, capture, and audit tests pass.

- [x] **Step 2: Run Release solution verification**

Run: `dotnet test Rekall.AGE.sln -c Release --no-restore`

Expected: build succeeds with zero warnings and all tests pass.

- [x] **Step 3: Review the complete diff**

Confirm the executable is package-relative, batch content is constant and safely quoted, manifest traversal is rejected, self-contained publishing applies only to the Windows graphical player, and unrelated package behavior is unchanged.

- [x] **Step 4: Record completion**

Mark every completed checkbox in this plan and commit the evidence update with message `docs: record Windows launcher verification`.

## Verification evidence

- Resolver RED/GREEN: 8/8 focused tests pass, including relocation with spaces, dot-segment traversal, rooted paths, and a junction escaping the package.
- Packaging RED/GREEN: graphical publish includes `hostfxr.dll`; `Play.exe` and `Play.bat` are integrity-inventoried and survive relocation.
- Package workflow suite: 9/9 `PlayablePackageIntegrityTests` pass.
- Real launcher smoke: relocated `Play.exe` and `Play.bat`, started without arguments from a different working directory, each rendered two bounded graphical frames and exited `0`.
- Full solution: 1,688/1,688 engine tests and 55/55 Studio tests pass in Release with no build warnings or errors.
- Independent review: launcher and generic capture-input changes are approved with no remaining Critical or Important findings.
- Authored-game acceptance: Pong3D and Galaga3D graphical Windows packages each inspect and relocate successfully with 311 integrity-inventoried files; both audits pass every readiness, run, capture, layout, and informative-frame check.
