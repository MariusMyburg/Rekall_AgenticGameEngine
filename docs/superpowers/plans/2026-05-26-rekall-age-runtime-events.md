# Runtime Events Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add generic runtime lifecycle events that AI-authored modules can consume to implement arbitrary game behavior.

**Architecture:** Keep events as engine-level facts, not hard-coded gameplay. The runtime emits `entity.begin` and `entity.tick` for opted-in entities through generic components and exposes them through `world.Subsystems.Events`; modules consume events through SDK helpers and decide what behavior to run.

**Tech Stack:** C# 13, .NET 10, xUnit, `System.Text.Json.Nodes`, existing Rekall AGE runtime/module contracts.

---

### Task 1: Add Runtime Event Contracts

**Files:**
- Modify: `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
- Modify: `src/Rekall.Age.Modules/RekallAgeRuntimeModuleSdk.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/RuntimeEventSdkTests.cs`

- [ ] **Step 1: Write failing SDK test**

Create `RuntimeEventSdkTests` with a `RekallAgeRuntimeWorld` containing `world.Subsystems.Events.Events`, then assert `world.EventsOfType("entity.tick")`, `world.EventsFor("ent_player")`, and `world.WasEventRaised("ent_player", "entity.begin")`.

- [ ] **Step 2: Run failing test**

Run: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~RuntimeEventSdkTests"`

Expected: compile failure because event contracts and helpers do not exist.

- [ ] **Step 3: Implement contracts and helpers**

Add `RekallAgeRuntimeEventView`, `RekallAgeRuntimeEvent`, and `Events` to `RekallAgeRuntimeSubsystemViews`. Add SDK helpers `EventsOfType`, `EventsFor`, and `WasEventRaised`.

- [ ] **Step 4: Run SDK test**

Run: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~RuntimeEventSdkTests"`

Expected: pass.

### Task 2: Generate Lifecycle Events

**Files:**
- Modify: `src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs`
- Create: `src/Rekall.Age.Runtime/RekallAgeLifecycleEventSystem.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/RuntimeLifecycleEventSystemTests.cs`

- [ ] **Step 1: Write failing lifecycle tests**

Create tests proving `Rekall.EventBindings` opts an entity into `entity.begin` and/or `entity.tick`, `handler` names are copied into events, `entity.begin` fires only when `world.FrameIndex == 0`, and invisible entities do not emit lifecycle events.

- [ ] **Step 2: Run failing lifecycle tests**

Run: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~RuntimeLifecycleEventSystemTests"`

Expected: fail because lifecycle generation does not exist.

- [ ] **Step 3: Implement lifecycle event generation**

Add built-in `Rekall.EventBindings` schema and `RekallAgeLifecycleEventSystem` with priority after input action projection and before project-authored gameplay systems where possible. Generate generic event payloads with source `runtime.lifecycle`.

- [ ] **Step 4: Run lifecycle tests**

Run: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~RuntimeLifecycleEventSystemTests"`

Expected: pass.

### Task 3: Document Agent-Facing Event Usage

**Files:**
- Modify: `README.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Document the contract**

Document `Rekall.EventBindings`, `entity.begin`, `entity.tick`, and SDK helpers, emphasizing that events are signals for agent-authored modules rather than engine-owned gameplay methods.

- [ ] **Step 2: Verify full suite**

Run: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj`

Expected: all tests pass.
