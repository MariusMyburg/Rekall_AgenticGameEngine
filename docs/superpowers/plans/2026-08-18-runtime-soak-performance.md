# Runtime Soak and Performance Inspection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a bounded, generic runtime soak command that proves deterministic continuity and evaluates explicit stability, growth, and throughput budgets through the same C# contract, CLI, MCP, and installed distribution.

**Architecture:** `InspectRuntimeSoakCommand` will load one authored scene, build one default execution loop, and resume the immutable runtime world through bounded chunks. It will preserve compact checkpoint evidence and named checks in its result even when a configured budget fails. CLI and MCP remain adapters over the command registry; Studio is deliberately not coupled to this first contract.

**Tech Stack:** C# 13, .NET 10, xUnit, immutable runtime world records, existing Rekall command bus, CLI pattern matching, MCP catalog projection, PowerShell installed-distribution acceptance.

---

## Task 1: Prove the core soak contract with failing tests

**Files:**

- Create: `tests/Rekall.Age.Tests/Runtime/RuntimeSoakCommandTests.cs`
- Reference: `tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs`
- Reference: `src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs`

- [x] **Step 1: Add a multi-chunk continuity test**

Create a temporary project and scene through `RekallAgeProjectStore` and `RekallAgeSceneStore`, then execute a request for 125 frames with a 50-frame checkpoint interval and disabled machine-dependent budgets. Assert:

```csharp
Assert.True(result.Ok, result.Summary);
Assert.Equal(125, result.Value.CompletedFrames);
Assert.Equal([50, 100, 125], result.Value.Checkpoints.Select(item => item.CompletedFrames));
Assert.Equal(125, result.Value.FinalFrameIndex);
Assert.Equal(125.0 / 60.0, result.Value.FinalElapsedSeconds, precision: 10);
Assert.All(result.Value.Checks, check => Assert.True(check.Passed, check.Message));
```

- [x] **Step 2: Add a structured budget-failure test**

Run a valid scene with `MinimumFramesPerSecond = double.MaxValue`. Assert `Ok` is false, the measured result is retained, the throughput check fails, and errors contain exactly `REKALL_RUNTIME_SOAK_BUDGET_EXCEEDED`.

- [x] **Step 3: Add invalid-request tests**

Use a nonexistent project root with zero frames, zero checkpoint interval, excessive frames, negative entity limits, and non-finite throughput. Each case must fail with `REKALL_RUNTIME_SOAK_INVALID_REQUEST`, proving validation occurs before scene I/O.

- [x] **Step 4: Run the focused test and observe RED**

Run:

```powershell
$env:TEMP='F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\TestTemp'
$env:TMP=$env:TEMP
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter FullyQualifiedName~RuntimeSoakCommandTests -p:UseSharedCompilation=false
```

Expected: compilation fails because the soak request, result, and command do not yet exist.

## Task 2: Implement the bounded runtime soak command

**Files:**

- Create: `src/Rekall.Age.Runtime/Commands/InspectRuntimeSoakCommand.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs`
- Modify: `tests/Rekall.Age.Tests/Runtime/RuntimeSoakCommandTests.cs`
- Reference: `src/Rekall.Age.Runtime/RekallAgeRuntimeWorldBuilder.cs`
- Reference: `src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs`

- [x] **Step 1: Define typed request and evidence records**

Define `InspectRuntimeSoakRequest` with project/scene, bounded frame and checkpoint values, optional throughput and retained-memory budgets, entity/observation/event limits, and stable-system requirement. Define compact `RuntimeSoakCheckpoint`, `RuntimeSoakCheck`, and `InspectRuntimeSoakResult` records with initial/final frame and elapsed values, wall time, throughput, memory measurements, system order, checkpoints, and checks.

- [x] **Step 2: Validate before loading**

Use constants `MaximumFrames = 1_000_000` and `MaximumCheckpoints = 10_000`. Reject blank roots/scenes; frame counts outside `1..MaximumFrames`; checkpoint intervals outside `1..Frames`; more than `MaximumCheckpoints`; negative entity/observation/event limits; non-finite or negative throughput; and retained-memory limits below `-1`. Return a minimal result plus `REKALL_RUNTIME_SOAK_INVALID_REQUEST`.

- [x] **Step 3: Execute one world through resumable chunks**

Load once using `RekallAgeSceneStore`, project with `RekallAgeRuntimeWorldBuilder`, create one `RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot)`, then repeat:

```csharp
var chunkFrames = Math.Min(request.CheckpointInterval, request.Frames - completedFrames);
var run = await loop.RunAsync(world, chunkFrames, context.CancellationToken);
world = run.World;
completedFrames += run.FramesSimulated;
checkpoints.Add(ToCheckpoint(initialWorld, world, completedFrames, stopwatch.Elapsed, sampledMemory));
```

Capture retained managed memory with `GC.GetTotalMemory(forceFullCollection: true)` before and after execution and sampled memory with `GC.GetTotalMemory(false)` at checkpoints. Use `Stopwatch.GetElapsedTime` or `Stopwatch` only for advisory wall-clock measurements; deterministic continuity comes from frame and elapsed values.

- [x] **Step 4: Evaluate named checks and preserve failures**

Always check completion, frame continuity, elapsed continuity, and stable ordered systems. Conditionally check configured throughput and retained-memory growth. Check entity growth, maximum checkpoint observations, and maximum checkpoint events. If any check fails, return the full result with `REKALL_RUNTIME_SOAK_BUDGET_EXCEEDED`; otherwise return success with measured throughput and retained growth in the summary.

- [x] **Step 5: Run the focused tests and observe GREEN**

Run the Task 1 command. Expected: all `RuntimeSoakCommandTests` pass.

- [x] **Step 6: Commit the core contract**

```powershell
git add src/Rekall.Age.Runtime/Commands/InspectRuntimeSoakCommand.cs tests/Rekall.Age.Tests/Runtime/RuntimeSoakCommandTests.cs
git commit -m "feat: add runtime soak inspection"
```

## Task 3: Expose the contract through CLI, MCP, and agent guidance

**Files:**

- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `src/Rekall.Age.Agent/Commands/GetEngineStatusCommand.cs`
- Modify: `tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs`
- Modify: `tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs`
- Modify: `tests/Rekall.Age.Tests/Agent/AgentContextCommandTests.cs`
- Modify: `README.md`

- [x] **Step 1: Add failing CLI, MCP, and status tests**

Register `InspectRuntimeSoakCommand` in the workbench catalog test and assert `rekall.runtime.inspect_soak`. Add an engine-status assertion that its workflow entry is recommended and mentions stability/performance evidence. Add a CLI process test for:

```powershell
runtime soak <root> Main 125 50 0 -1 0 128 1024
```

Assert exit zero and output containing `Completed frames: 125`, `Checkpoints: 3`, and passed continuity checks.

- [x] **Step 2: Run the integration tests and observe RED**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~RuntimeInspectCliTests|FullyQualifiedName~WorkbenchMcpCatalogTests|FullyQualifiedName~AgentContextCommandTests" -p:UseSharedCompilation=false
```

Expected: the new catalog, guidance, and CLI assertions fail.

- [x] **Step 3: Register and route the command**

Add `registry.Register(new InspectRuntimeSoakCommand())`. Add a CLI route with the nine explicit values plus a shorter route that uses request defaults. Parse with invariant culture, execute `rekall.runtime.inspect_soak`, and print summary, frame totals, elapsed time, throughput, retained-memory growth, checkpoint count/details, and every named check. Return `result.Ok ? 0 : 1`.

- [x] **Step 4: Add agent discovery guidance**

Add a recommended `RekallAgeAgentWorkflowTool` entry explaining that `rekall.runtime.inspect_soak` produces long-run deterministic stability, bounded-growth, and throughput evidence without authoring content or requiring a playable module.

- [x] **Step 5: Document the public command**

Add the generic soak command to README verification examples and explain that machine-dependent throughput and retained-memory blockers are opt-in explicit budgets; deterministic continuity and bounded scene facts are always checked.

- [x] **Step 6: Run integration and focused runtime tests GREEN**

Run the Task 3 test command plus the Task 1 focused command. Expected: all pass.

- [x] **Step 7: Commit adapters and guidance**

```powershell
git add src/Rekall.Age.Cli/Program.cs src/Rekall.Age.Agent/Commands/GetEngineStatusCommand.cs tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs tests/Rekall.Age.Tests/Agent/AgentContextCommandTests.cs README.md
git commit -m "feat: expose runtime soak evidence"
```

## Task 4: Add installed-product acceptance

**Files:**

- Modify: `eng/accept-distribution.ps1`
- Modify: `docs/production/PROGRESS.md`

- [x] **Step 1: Add installed CLI soak proof**

After the installed audio/UI scene is authored and inspected, invoke the distributed CLI with 600 frames, 120-frame checkpoints, a conservative 30 frames/second minimum, 64 MiB maximum retained managed-memory growth, zero entity growth, 32 observations, and 128 events. Capture output and require passed completion, frame-continuity, elapsed-continuity, stable-systems, throughput, retained-memory, entity-growth, observations, and events checks.

- [x] **Step 2: Run installed acceptance in the assembled distribution gate**

Use the repository product gate rather than accepting a source-tree CLI run as installed evidence:

```powershell
$env:TEMP='F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\GateTemp'
$env:TMP=$env:TEMP
pwsh -NoProfile -File eng\build.ps1
```

Expected: clean Release build with zero warnings/errors, two independent passing Release test runs, assembled self-contained Windows distribution, and installed acceptance including the new soak proof.

- [x] **Step 3: Record exact evidence in the durable ledger**

Update `docs/production/PROGRESS.md` with the timestamp, test total, measured installed throughput and retained-memory growth, archive size/hash, product-gate result, current lifecycle/performance posture, and next production risk. Never claim broad device-loss/crash recovery from this offline soak gate.

- [x] **Step 4: Commit acceptance and evidence**

```powershell
git add eng/accept-distribution.ps1 docs/production/PROGRESS.md
git commit -m "test: gate installed runtime soak"
```

## Task 5: Final review and production handoff

**Files:**

- Review: all files changed by Tasks 1-4
- Modify if needed: `docs/production/PROGRESS.md`

- [x] **Step 1: Inspect the branch diff and working tree**

Run `git status --short` and `git diff HEAD~4 --check`. Confirm no unrelated user work, generated artifacts, secrets, absolute machine paths in product output, or Studio-specific coupling entered the change.

- [x] **Step 2: Verify command semantics from the installed executable**

Confirm the acceptance output contains the exact measured checks and that a deliberately impossible throughput budget returns exit code 1 with `REKALL_RUNTIME_SOAK_BUDGET_EXCEEDED` while retaining checkpoint evidence.

- [x] **Step 3: Update the durable next-action queue**

Mark the runtime soak/performance milestone complete only after the full gate. Set the next production priority to module trust/loading boundaries, followed by device-loss recovery, crash reporting, compatibility/migration, and release operability.

- [x] **Step 4: Commit any review corrections**

Use a focused commit message matching the correction. Leave the worktree clean before reporting the milestone.
