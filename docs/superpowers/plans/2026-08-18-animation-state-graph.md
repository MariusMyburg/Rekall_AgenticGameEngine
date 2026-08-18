# Generic Animation State Graph Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a bounded, deterministic, inspectable, parameter-driven animation state graph that reuses Rekall AGE's generic clip mixer.

**Architecture:** A focused parser/evaluator validates authored graph JSON into immutable models. A runtime system advances graph clocks and projects stable/cross-fading states into a runtime-only mixer; the existing animation system remains the sole clip sampler. Built-in schemas and runtime projection expose the authored and live contracts to agents.

**Tech Stack:** C# 14, .NET 10, `System.Text.Json.Nodes`, xUnit, existing Rekall AGE runtime/module/MCP/CLI contracts.

**Spec:** `docs/superpowers/specs/2026-08-18-animation-state-graph-design.md`

## Global Constraints

- Core behavior stays genre-neutral; modules own parameter meaning and mutation.
- Version 1 limits: 64 states, 256 transitions, 128 parameters, 16 conditions per transition, 128-character identifiers, and 1,024-character string values.
- Only finite numbers, booleans, and bounded strings are valid parameter/condition values.
- Exact-state transitions precede any-state transitions; authored order is stable within each group; at most one starts per frame.
- Active transitions cannot be interrupted; all clocks use `context.DeltaTime`.
- The graph reuses existing clip/mixer sampling and never overwrites an authored `Rekall.AnimationMixer`.
- Invalid graphs fail closed with bounded structured observations.

---

### Task 1: Bounded graph parser and transition evaluator

**Files:**
- Create: `src/Rekall.Age.Runtime/RekallAgeAnimationStateGraphDefinition.cs`
- Create: `tests/Rekall.Age.Tests/Runtime/AnimationStateGraphDefinitionTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Produces `RekallAgeAnimationStateGraphDefinition.TryParse(JsonObject, out definition, out issue)`.
- Produces immutable state/transition/condition records and `SelectTransition(currentState, parameters)`.
- Has no world, asset, I/O, or gameplay dependency.

- [x] **Step 1: Write failing parser tests**

Cover a valid two-state graph, exact-before-any ordering, typed equality and
numeric comparisons, unconditional and self-transition rules, duplicate/missing
states, invalid initial state/version/operator/value, non-finite numbers, every
count/string bound, and deterministic error codes.

- [x] **Step 2: Run the focused tests and confirm they fail because the parser types do not exist**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~AnimationStateGraphDefinitionTests --verbosity minimal
```

- [x] **Step 3: Implement immutable parsing and evaluation**

Use explicit records:

```csharp
internal sealed record RekallAgeAnimationGraphStateDefinition(
    string Name, string Clip, double Speed, string LoopMode, double StartTimeSeconds);

internal sealed record RekallAgeAnimationGraphTransitionDefinition(
    string From, string To, double DurationSeconds, bool ResetTime,
    IReadOnlyList<RekallAgeAnimationGraphConditionDefinition> Conditions);

internal sealed record RekallAgeAnimationGraphIssue(string Code, string Message, string Target);
```

Clone primitive parameter values into an ordinal dictionary. Reject arrays,
objects, nulls, non-finite numbers, and unknown operators during parse. Build
exact-state and any-state candidate sequences without hash-order dependence.

- [x] **Step 4: Run focused tests and `git diff --check`**

- [x] **Step 5: Record evidence and commit**

```powershell
git add src/Rekall.Age.Runtime/RekallAgeAnimationStateGraphDefinition.cs tests/Rekall.Age.Tests/Runtime/AnimationStateGraphDefinitionTests.cs docs/production/PROGRESS.md
git commit -m "feat: define bounded animation state graphs"
```

Verified 2026-08-18: 22/22 focused parser/evaluator tests pass. The
immutable definition rejects all documented count bounds, duplicate or missing
states, invalid versions/operators/references, non-finite and structured
parameters, and mismatched primitive comparisons with stable graph codes. Exact
state ordering, any-state fallback, typed operators, unconditional transitions,
and reset-only self-transitions are deterministic.

---

### Task 2: Runtime graph projection, clocks, blending, and facts

**Files:**
- Create: `src/Rekall.Age.Runtime/RekallAgeAnimationStateGraphSystem.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeTransformAnimationSystem.cs`
- Modify: `tests/Rekall.Age.Tests/Runtime/RuntimeAnimationTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Consumes the Task 1 definition and evaluator.
- Produces runtime-only `Rekall.AnimationGraphMixer` and persisted `Rekall.AnimationGraphState`.
- Existing transform animation system treats graph mixer as highest-priority animation driver and routes it through `ApplyMixer`.

- [x] **Step 1: Write failing end-to-end runtime tests**

Prove initial sampling, parameter transition, exact-before-any selection,
halfway and completed linear weights, target reset/resume, pause, one transition
per frame, noninterruption, begin/end fact payloads, split-run determinism,
64-state bounded clocks, invalid-graph no-mutation observations, and graph versus
player/mixer conflict diagnostics.

- [x] **Step 2: Run the focused runtime tests and confirm behavioral failures**

- [x] **Step 3: Implement the graph system**

Give the system `Priority = -10` and `Id = "runtime.animation.graph"`. For each
graph entity, validate before mutation, restore only declared state clocks,
advance finite nonnegative clocks by delta time and per-state speed, and create
one or two mixer layers with `fadeSeconds = 0`. Persist:

```json
{
  "version": 1,
  "activeState": "target",
  "previousState": "source",
  "transitionElapsedSeconds": 0.25,
  "transitionDurationSeconds": 0.5,
  "transitionProgress": 0.5,
  "playing": true,
  "stateClocks": { "source": 1.25, "target": 0.25 }
}
```

Emit facts only through authored handlers returned by the same generic handler
lookup pattern used for `animation.event`. The graph system must append to the
event view rather than replace prior facts.

- [x] **Step 4: Integrate the runtime-only mixer with the existing sampler**

Add graph mixer precedence before authored mixer/player. Suppress the other
drivers on that entity and emit one bounded conflict observation. Continue to
write existing `Rekall.AnimationState` layer state so clip timing, markers, and
runtime sampling remain unchanged.

- [x] **Step 5: Run focused animation tests, then all runtime animation tests**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~AnimationStateGraph --verbosity minimal
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~RuntimeAnimationTests --verbosity minimal
```

- [x] **Step 6: Record evidence and commit**

```powershell
git add src/Rekall.Age.Runtime tests/Rekall.Age.Tests/Runtime docs/production/PROGRESS.md
git commit -m "feat: execute animation state graphs"
```

Verified 2026-08-18: 9/9 graph runtime tests and 50/50 combined graph,
player, mixer, marker, malformed-input, resume, and animation regression tests
pass. Real catalog clips cross-fade generic transform properties; bound
enter/exit/begin/end facts are exact; pause, noninterruption, reset/resume,
64-state clocks, driver conflict, invalid fail-closed behavior, and split-run
byte-equivalent state are covered. Existing sampling owns authoritative graph
times without duplicating track or blend math.

---

### Task 3: Agent schema and runtime inspection

**Files:**
- Modify: `src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs`
- Modify: `src/Rekall.Age.Modules/BuiltIns/RekallAgeInteractiveSubsystemComponents.cs`
- Modify: `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `tests/Rekall.Age.Tests/Modules/ModuleMetadataTests.cs`
- Modify: `tests/Rekall.Age.Tests/Runtime/RuntimeAnimationTests.cs`
- Modify: `tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Registers public component type `Rekall.AnimationStateGraph`.
- Extends `RekallAgeRuntimeAnimationPlayer` with nullable `StateName`, `PreviousStateName`, and numeric `TransitionProgress` without breaking existing constructors.

- [x] **Step 1: Write failing discovery, projection, and CLI tests**

Assert the schema names exact shapes/limits and explicitly says modules own
parameter semantics. Assert runtime projection reports kind, active/previous
state, active clip, transition progress, and projected layers. Assert CLI output
prints those facts without dumping unbounded parameter values.

- [x] **Step 2: Implement schema registration and inspection fields**

Use component properties `Version`, `Playing`, `InitialState`, `Parameters`,
`States`, and `Transitions` with descriptive `Kind` metadata. Read live facts
from `Rekall.AnimationGraphState` and layers from `Rekall.AnimationState`.

- [x] **Step 3: Run metadata, animation projection, CLI, and MCP catalog regressions**

- [x] **Step 4: Record evidence and commit**

```powershell
git add src tests docs/production/PROGRESS.md
git commit -m "feat: inspect animation state graphs"
```

Verified 2026-08-18: a consolidated 64/64 graph definition/runtime,
legacy animation, built-in metadata, CLI inspection, and MCP agent-tool
selection passes. Agents discover exact state/transition/parameter shapes and
limits. Runtime projection and CLI expose active/previous state, transition
progress, active clip, and bounded layers without dumping parameters.

---

### Task 4: Installed generic proof and product gate

**Files:**
- Modify: `eng/accept-distribution.ps1`
- Modify: `docs/production/2026-08-17-engine-maturity-audit.md`
- Modify: `docs/production/PROGRESS.md`
- Modify: this plan

**Interfaces:**
- Extends installed acceptance with a genre-neutral two-clip graph proof.
- Does not weaken or replace any existing installed proof.

- [ ] **Step 1: Add installed graph fixture and assertions**

Author two generic color/transform clips plus a graph. Update a neutral `phase`
parameter through authored content, inspect the transition, capture pre/post
frames, require distinct SHA-256 hashes, and require no error observations.

- [ ] **Step 2: Run complete Debug verification**

```powershell
dotnet test Rekall.AGE.sln --no-restore --verbosity minimal
```

- [ ] **Step 3: Run the canonical locked two-pass Release gate**

```powershell
$env:TEMP = 'F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\GateTemp'
$env:TMP = $env:TEMP
& .\eng\build.ps1
```

- [ ] **Step 4: Record exact test counts, timings, installed facts, frame hashes, soak data, archive size/hash, limitations, and next priority**

- [ ] **Step 5: Review, run `git diff --check`, and commit**

```powershell
git add eng/accept-distribution.ps1 docs
git commit -m "test: gate installed animation state graphs"
```
