# Player Recovery, Crash Diagnostics, and Release Operability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bounded structured failure reports, classify and retry only recoverable graphics lifecycle failures, expose diagnostic evidence to agents, and prove installed Windows-player recovery without weakening ordinary failure handling.

**Architecture:** Put bounded report storage and generic session supervision in testable engine libraries. Keep Veldrid/SDL adaptation in the Windows player. A cold player-session restart is the first honest recovery mode; arbitrary runtime/module failures remain fatal. CLI/MCP read reports through one command and installed acceptance uses an engine-owned one-shot device-loss injection.

**Tech Stack:** C# 13, .NET 10, `System.Text.Json`, xUnit, Veldrid/SDL2, existing command registry, CLI/MCP catalog, PowerShell installed acceptance.

---

## Task 1: Add bounded atomic diagnostic reports

**Files:**

- Create: `src/Rekall.Age.Core/Diagnostics/RekallAgeFailureReport.cs`
- Create: `src/Rekall.Age.Core/Diagnostics/RekallAgeFailureReportStore.cs`
- Create: `tests/Rekall.Age.Tests/Core/FailureReportStoreTests.cs`

- [x] **Step 1: Add failing storage tests**

Cover schema/product/component/outcome/category/frame/attempt fields, explicit-root and Local App Data resolution, atomic JSON creation, newest-first bounded reads, report-size/entry/retention limits, malformed JSON, duplicate ids, traversal-safe filenames, reparse roots, and concurrent writes. Assert reports never serialize environment variables, arbitrary exception data, or project content.

- [x] **Step 2: Run focused tests RED**

```powershell
$env:TEMP='F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\TestTemp'
$env:TMP=$env:TEMP
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj -c Debug --filter FullyQualifiedName~FailureReportStoreTests -p:UseSharedCompilation=false
```

Expected: report contracts/store do not exist.

- [x] **Step 3: Implement bounded contracts and atomic store**

Use schema 1, product metadata, stable code/category/outcome/recovery-mode, bounded exception facts, project/scene identifiers, limitations, and next actions. Stream/limit reads, reject reparse roots, use unique temp plus atomic move, cap defaults at 50 reports and 1 MiB/report, and delete only validated report files beyond retention.

- [x] **Step 4: Run focused tests GREEN and commit**

```powershell
git add src/Rekall.Age.Core/Diagnostics tests/Rekall.Age.Tests/Core/FailureReportStoreTests.cs
git commit -m "feat: add bounded failure reports"
```

Verified 2026-08-18: the focused Debug selection passed 5/5. Evidence covers
atomic creation, newest-first reads, bounded retention and payloads, malformed
report isolation, concurrent unique writes, root reparse rejection, and an
explicit contract that does not capture ambient environment or exception data.

## Task 2: Implement graphics failure classification and bounded supervision

**Files:**

- Create: `src/Rekall.Age.Rendering/Recovery/RekallAgeGraphicsFailureClassifier.cs`
- Create: `src/Rekall.Age.Rendering/Recovery/RekallAgeGraphicsDeviceLostException.cs`
- Create: `src/Rekall.Age.Rendering/Recovery/RekallAgePlayerSessionSupervisor.cs`
- Create: `tests/Rekall.Age.Tests/Rendering/PlayerSessionSupervisorTests.cs`

- [x] **Step 1: Add failing classifier/supervisor tests**

Prove typed device loss and narrow Vulkan/Veldrid signatures are recoverable; swapchain/surface invalidation is separately categorized; arbitrary `InvalidOperationException`, module trust exceptions, initialization failures, and misleading nested messages are fatal. Prove bounded retry count, disposal before recreation, finite-frame remainder accounting, continuous mode, cancellation, success-after-retry, exhaustion, and no retry for fatal failures.

- [x] **Step 2: Run focused tests RED**

Expected: classifier and supervisor types are missing.

- [x] **Step 3: Implement pure lifecycle contracts**

Define an injected player-session interface/factory and supervisor result/events. Walk a bounded exception chain. Default to two retries with a bounded delay. Never depend on SDL or Veldrid concrete types in the supervisor.

- [x] **Step 4: Persist recovered/exhausted/fatal evidence**

Connect the supervisor to the report store through an injected writer. Successful recovery emits `recovered`/`cold-session-restart`; exhaustion and fatal failures emit stable fatal codes. Report-write failure must not create a retry loop or hide the original result.

- [x] **Step 5: Run focused tests GREEN and commit**

```powershell
git add src/Rekall.Age.Rendering/Recovery tests/Rekall.Age.Tests/Rendering/PlayerSessionSupervisorTests.cs
git commit -m "feat: supervise recoverable player failures"
```

Verified 2026-08-18: the supervisor selection passed 8/8 and the combined
diagnostic/recovery selection passed 13/13. Recovery is limited to typed or
narrow Veldrid Vulkan device/surface signatures, disposes a failed session
before recreation, preserves finite and continuous frame accounting, stops
after two retries by default, never retries initialization/arbitrary runtime
failures, and persists bounded evidence without allowing report failures to
replace the player result.

## Task 3: Expose read-only failure inspection to agents

**Files:**

- Create: `src/Rekall.Age.Agent/Commands/InspectFailureReportsCommand.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `src/Rekall.Age.Mcp/RekallAgeMcpCatalog.cs`
- Modify: `src/Rekall.Age.Agent/Commands/GetEngineStatusCommand.cs`
- Modify: `tests/Rekall.Age.Tests/Agent/AgentContextCommandTests.cs`
- Modify: `tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs`
- Modify: `tests/Rekall.Age.Tests/Cli/CliSmokeTests.cs`

- [x] **Step 1: Add failing command/catalog/CLI tests**

Require `rekall.diagnostics.inspect_failures` to be read-only, recommended, bounded, filterable by component/outcome/code, and to return report paths plus next actions. CLI `diagnostics failures [root]` must print stable codes/outcomes without stack flooding.

- [x] **Step 2: Implement one typed adapter surface**

Register in the CLI composition root so MCP inherits it. Add engine-status guidance and exact empty/malformed/store-unavailable behavior.

- [x] **Step 3: Run focused tests GREEN and commit**

```powershell
git add src/Rekall.Age.Agent/Commands/InspectFailureReportsCommand.cs src/Rekall.Age.Cli/Program.cs src/Rekall.Age.Mcp/RekallAgeMcpCatalog.cs src/Rekall.Age.Agent/Commands/GetEngineStatusCommand.cs tests/Rekall.Age.Tests
git commit -m "feat: expose failure report inspection"
```

Verified 2026-08-18: the focused command/catalog/status/CLI selection passed
5/5. `rekall.diagnostics.inspect_failures` is read-only, recommended, capped
at 50 reports, filters exact component/outcome/code values case-insensitively,
isolates malformed files, and returns report paths plus next actions. The CLI
prints compact exception facts but never stack excerpts.

## Task 4: Integrate bounded recovery into the Windows player

**Files:**

- Modify: `src/Rekall.Age.Player.Windows/Program.cs`
- Create: `src/Rekall.Age.Player.Windows/RekallAgeVeldridPlayerSession.cs`
- Modify: `src/Rekall.Age.Player.Windows/Rekall.Age.Player.Windows.csproj`
- Create: `tests/Rekall.Age.Tests/Cli/WindowsPlayerRecoveryTests.cs`

- [ ] **Step 1: Add failing process-level recovery tests**

Publish/use the Windows player, set an isolated diagnostic root, run a small project with `--frames`, inject one device loss, and assert exit 0, exact requested total frames, one cold restart, and a recovered report. Add fatal injection and always-device-loss exhaustion cases with stable nonzero codes and no unbounded processes/files.

- [ ] **Step 2: Extract a supervisor-compatible session adapter**

Keep the existing `RekallAgeVeldridPlayer` implementation but wrap create/run/dispose behind the generic session contract. `Run` must report frames completed before failure. Dispose the failed session fully before recreation.

- [ ] **Step 3: Add one-shot diagnostic fault injection**

Parse `--simulate-device-loss-frame` and a test-only fatal/exhaustion variant. Store one-shot state at process-supervisor scope so a successful retry does not inject forever. Do not expose injection to project modules or runtime input.

- [ ] **Step 4: Add top-level fatal reporting and stable exits**

Catch startup/session failures, classify, write a bounded report, print code/path, and return stable exit codes. Preserve `--audio-required`, windowed VR, live editing, and ordinary close behavior.

- [ ] **Step 5: Run Windows player/recovery tests GREEN and commit**

```powershell
git add src/Rekall.Age.Player.Windows tests/Rekall.Age.Tests/Cli/WindowsPlayerRecoveryTests.cs
git commit -m "feat: recover Windows player device loss"
```

## Task 5: Unify Studio fatal evidence and document operability

**Files:**

- Modify: `src/Rekall.Age.Studio/App.xaml.cs`
- Modify: `README.md`
- Modify: `tests/Rekall.Age.Tests/Cli/StudioCliTests.cs`
- Modify: `docs/production/PROGRESS.md`

- [ ] **Step 1: Add failing Studio/report integration tests**

Prove dispatcher, AppDomain, startup, and unobserved-task paths map to bounded report requests without serializing arbitrary state. Dispatcher continuation remains explicit and fatal startup terminates.

- [ ] **Step 2: Route Studio hooks through the shared reporter**

Retain Serilog, add structured report ids/paths, and avoid duplicate reports for the same failure event where practical.

- [ ] **Step 3: Document exact posture and operator workflow**

Document CLI/MCP inspection, diagnostic location override, retention/privacy, stable exit codes, cold-restart limitations, and that recovery is not arbitrary exception suppression.

- [ ] **Step 4: Run focused and full Debug suites, then commit**

```powershell
dotnet test Rekall.AGE.sln -c Debug -p:UseSharedCompilation=false --verbosity minimal
git add src/Rekall.Age.Studio/App.xaml.cs README.md tests/Rekall.Age.Tests/Cli/StudioCliTests.cs docs/production/PROGRESS.md
git commit -m "feat: unify desktop failure diagnostics"
```

## Task 6: Installed recovery acceptance and complete product gate

**Files:**

- Modify: `eng/accept-distribution.ps1`
- Modify: `docs/production/PROGRESS.md`
- Modify: `docs/superpowers/plans/2026-08-18-player-recovery-crash-operability.md`

- [ ] **Step 1: Extend installed acceptance**

Use an isolated installed diagnostics directory. Run the shipped Windows player with one-shot device loss and require exit 0, exact recovery code/mode/attempt evidence, and CLI inspection. Run exhaustion/fatal negative proofs and require stable nonzero codes. Re-run ordinary audio-required player and the unchanged gauntlet/package/UI/audio/soak matrix.

- [ ] **Step 2: Run full Debug verification**

```powershell
$env:TEMP='F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\TestTemp'
$env:TMP=$env:TEMP
dotnet test Rekall.AGE.sln -c Debug -p:UseSharedCompilation=false --verbosity minimal
```

- [ ] **Step 3: Run the canonical installed product gate**

```powershell
$env:TEMP='F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\GateTemp'
$env:TMP=$env:TEMP
pwsh -NoProfile -File eng\build.ps1
```

Require zero warnings/errors, two independent full Release passes, self-contained Windows distribution, installed recovery/fatal/exhaustion evidence, and the unchanged installed product matrix.

- [ ] **Step 4: Review and record exact evidence**

Use requesting-code-review subject to delegation rules and verification-before-completion. Record Debug/Release counts, exact recovery/fatal codes, report paths/counts, installed positive/negative outcomes, archive size/hash, cold-restart/state-preservation limitations, and the next compatibility/migration priority.

- [ ] **Step 5: Commit and preserve the production branch**

```powershell
git add eng/accept-distribution.ps1 docs/production/PROGRESS.md docs/superpowers/plans/2026-08-18-player-recovery-crash-operability.md
git commit -m "test: gate installed player recovery"
```
