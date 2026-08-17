# Windows Distribution and CI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a versioned, self-contained Windows Rekall AGE ZIP that can author, build, package, run, and audit a game without the engine source repository, and enforce that contract in CI.

**Architecture:** A Core locator identifies installed layouts, Build assembles and hashes already-published tool payloads, and `BuildPlayerCommand` copies shipped players when installed while retaining its repository fallback. A canonical PowerShell pipeline performs locked restore, build, tests, publish, assembly, and installed acceptance; Windows CI invokes only that pipeline.

**Tech Stack:** C# 13, .NET 10 self-contained `win-x64`, PowerShell 7, SHA-256, ZIP, xUnit, GitHub Actions

**Spec:** `docs/superpowers/specs/2026-08-17-production-foundation-design.md`

## Global Constraints

- Product version is `0.1.0-preview.1`; target RID is `win-x64`.
- Manifest paths are relative with `/` separators; SHA-256 is lowercase hexadecimal.
- Installed workflows never search for or reference `src/`.
- The proprietary notice and third-party notices ship; secrets, logs, tests, and development projects do not.
- Existing command-bus, MCP, gauntlet, and generic authoring contracts remain authoritative.

---

### Task 1: Distribution layout discovery

**Files:**
- Create: `src/Rekall.Age.Core/Product/RekallAgeDistributionLayout.cs`
- Create: `tests/Rekall.Age.Tests/Core/DistributionLayoutTests.cs`

**Interfaces:**
- Produces: `bool RekallAgeDistributionLayout.TryFind(string startPath, out RekallAgeDistributionPaths paths)`
- Produces: `RekallAgeDistributionPaths` with root, manifest, CLI, Studio, players, SDK, and docs paths

- [ ] **Step 1: Write the failing test**

```csharp
var root = TestPaths.CreateTempDirectory();
var cli = Path.Combine(root, "tools", "cli");
Directory.CreateDirectory(cli);
File.WriteAllText(Path.Combine(root, "rekall.distribution.json"), "{}");
Assert.True(RekallAgeDistributionLayout.TryFind(cli, out var paths));
Assert.Equal(root, paths.Root);
Assert.Equal(Path.Combine(root, "players", "windows"), paths.WindowsPlayerPayload);
Assert.Equal(Path.Combine(root, "sdk", "1"), paths.ModuleSdk);
```

- [ ] **Step 2: Verify RED**

Run `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~DistributionLayoutTests --no-restore`; expect compilation failure because the locator is absent.

- [ ] **Step 3: Implement ancestor-only manifest discovery**

```csharp
public sealed record RekallAgeDistributionPaths(
    string Root, string Manifest, string Cli, string Studio,
    string HeadlessPlayerPayload, string WindowsPlayerPayload,
    string ModuleSdk, string Documentation);
```

Walk normalized parents until `rekall.distribution.json` exists. Never fall back to repository discovery.

- [ ] **Step 4: Verify GREEN and commit**

Run the filtered test, then commit as `feat: locate installed engine distribution`.

### Task 2: Verified distribution assembler

**Files:**
- Create: `src/Rekall.Age.Build/Distribution/RekallAgeDistributionAssembler.cs`
- Create: `src/Rekall.Age.Build/Commands/AssembleDistributionCommand.cs`
- Create: `tests/Rekall.Age.Tests/Build/DistributionAssemblerTests.cs`

**Interfaces:**
- Produces command: `rekall.distribution.assemble`
- Produces: `RekallAgeDistributionManifest`, `RekallAgeDistributionFile`, and `AssembleDistributionResult`

- [ ] **Step 1: Write failing assembly tests**

Create fake published directories and assert the assembler creates this layout:

```text
tools/cli/  tools/studio/  players/headless/  players/windows/
sdk/1/  docs/README.md  PROPRIETARY-NOTICE.md
THIRD-PARTY-NOTICES.txt  rekall.distribution.json
```

Assert product version `0.1.0-preview.1`, RID `win-x64`, relative `/` paths, 64-character lowercase hashes, the expected executables, and `<output>.zip`. Seed `secret.env`, `cli.log`, and `test.trx` in separate cases and require `REKALL_DISTRIBUTION_FORBIDDEN_FILE`.

- [ ] **Step 2: Verify RED**

Run the assembler tests; expect compilation failure because the assembler is absent.

- [ ] **Step 3: Implement safe copy, manifest, and ZIP**

Copy inputs in ordinal relative-path order. Reject output equal to or containing an input. Reject `*.log`, `*.trx`, `.env`, `*.pfx`, `*.snk`, `appsettings.*.json`, and `TestResults` segments. Hash copied files with SHA-256, excluding the manifest itself, write camel-case JSON, and create ZIP entries in ordinal order.

Use this command request:

```csharp
public sealed record AssembleDistributionRequest(
    string OutputRoot, string CliPublishRoot, string StudioPublishRoot,
    string HeadlessPlayerPublishRoot, string WindowsPlayerPublishRoot,
    string SdkSourceRoot, string ReadmePath, string ProprietaryNoticePath,
    string ThirdPartyNoticesPath, string RuntimeIdentifier = "win-x64");
```

- [ ] **Step 4: Verify GREEN and commit**

Run the assembler tests and commit as `feat: assemble verified windows distribution`.

### Task 3: Installed player payload packaging

**Files:**
- Modify: `src/Rekall.Age.Build/Commands/BuildPlayerCommand.cs`
- Modify: `tests/Rekall.Age.Tests/Build/BuildPlayerCommandTests.cs`

**Interfaces:**
- Consumes `REKALL_AGE_DISTRIBUTION_ROOT` first, then the distribution containing `AppContext.BaseDirectory`, then repository fallback

- [ ] **Step 1: Write the failing installed-payload test**

Build a fake distribution with manifest and player payload, set `REKALL_AGE_DISTRIBUTION_ROOT` in `try/finally`, execute `BuildPlayerCommand`, and assert copied bytes, launch path, arguments, and absence of `dotnet publish` output. This test catches installed CLI attempts to find source projects.

- [ ] **Step 2: Verify RED**

Run `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~BuildPlayerCommandTests --no-restore`; expect the new test to fail at source-project discovery.

- [ ] **Step 3: Implement distribution-first copying**

Select `players/windows` for graphics and `players/headless` otherwise. Validate payload and executable, safely prepare the exact output, recursively copy, and return launch details. Invoke the existing source publish only when no distribution exists.

- [ ] **Step 4: Verify GREEN and commit**

Run BuildPlayer and gauntlet tests; commit as `feat: package games from installed players`.

### Task 4: CLI route and canonical production scripts

**Files:**
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `tests/Rekall.Age.Tests/Cli/CliSmokeTests.cs`
- Create: `eng/build.ps1`
- Create: `eng/accept-distribution.ps1`
- Create: `THIRD-PARTY-NOTICES.txt`

**Interfaces:**
- CLI: `distribution assemble <output> <cli> <studio> <headless> <windows> <sdk> <readme> <notice> <thirdParty>`
- Build: `pwsh ./eng/build.ps1 -Configuration Release -RuntimeIdentifier win-x64`

- [ ] **Step 1: Add a failing CLI assembly test**

Invoke the route with fake inputs and assert exit `0`, archive/manifest paths, version, and hashes. Invoke with a forbidden file and assert exit `1` plus `REKALL_DISTRIBUTION_FORBIDDEN_FILE`.

- [ ] **Step 2: Verify RED**

Run the new CLI test; expect unknown-route exit `2`.

- [ ] **Step 3: Register and print the assembly command**

Print output root, manifest, archive, file count, and version. Return `1` on command failure.

- [ ] **Step 4: Implement installed acceptance**

The script resolves the distributed CLI, creates unique temporary roots, runs doctor, project/scene creation, runtime-system scaffolding, module build, project doctor, and a separate complete gauntlet. Reject generated `ProjectReference`, `src/Rekall.Age.Modules`, absolute drive paths, missing SDK, or a blank/missing proof frame. Clean successful roots and preserve a failing root.

- [ ] **Step 5: Implement the canonical build**

The script uses `$PSScriptRoot` paths and checked external processes to perform locked restore, Release build, two Release test runs, four self-contained publishes, SDK staging, distribution assembly, and installed acceptance. Default output is `Artifacts/Distribution`.

- [ ] **Step 6: Add third-party notices and commit**

List each direct external package with pinned version and upstream URL. Run CLI tests and commit as `build: add canonical windows release pipeline`.

### Task 5: Dependency locking and Windows CI

**Files:**
- Modify: `Directory.Build.props`
- Create: generated `packages.lock.json` files
- Create: `.github/workflows/windows-release.yml`
- Modify: `README.md`
- Modify: this plan

**Interfaces:**
- CI invokes only `pwsh ./eng/build.ps1 -Configuration Release -RuntimeIdentifier win-x64`
- Artifact: `Artifacts/Distribution/Rekall-AGE-0.1.0-preview.1-win-x64.zip`

- [ ] **Step 1: Enable dependency locking**

Add `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`, run `dotnet restore Rekall.AGE.sln --force-evaluate`, then locked restore and verify it changes no lock file.

- [ ] **Step 2: Add authoritative Windows CI**

Use `windows-latest`, checkout, `actions/setup-dotnet` with `10.0.x`, least `contents: read` permission, canonical build invocation, failure TRX upload, and ZIP upload. Add no deployment credentials.

- [ ] **Step 3: Document the production build**

Document the command, archive, manifest hashes, installed doctor, and supported/experimental posture without open-source claims.

- [ ] **Step 4: Run the complete pipeline**

Run `pwsh ./eng/build.ps1 -Configuration Release -RuntimeIdentifier win-x64`; require locked restore, warning-free build, two green suites, four publishes, verified ZIP, and installed gauntlet success.

- [ ] **Step 5: Inspect and verify output**

Recompute every manifest hash, reject absolute JSON paths and forbidden files, confirm tools/SDK/notices/docs/player payloads, and run distributed `context doctor`.

- [ ] **Step 6: Mark complete and commit**

Commit locks, CI, docs, tests, source, and this checked plan as `ci: enforce windows developer preview release`.

- [ ] **Step 7: Record evidence**

Report ZIP path/size, file count, both test totals, installed gauntlet result, commit, and non-blocking experimental checks.
