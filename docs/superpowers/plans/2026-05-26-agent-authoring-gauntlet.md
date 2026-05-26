# Agent Authoring Gauntlet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a first-class workflow that proves an agent can create, verify, package, audit, and report on a playable Rekall AGE game through generic engine contracts.

**Architecture:** Implement the gauntlet as a command-bus workflow in `Rekall.Age.GameTemplates.Commands` that composes existing template creation, verification, packaging, audit, and context tools. Expose the workflow through CLI and MCP metadata without adding genre-specific runtime behavior. Return structured checks and next actions so agents can use the result as a repair loop entry point.

**Tech Stack:** C# 13, .NET 10, xUnit, Rekall AGE command bus, existing game template/playable package commands.

---

### Task 1: Command Behavior Tests

**Files:**
- Modify: `tests/Rekall.Age.Tests/GameTemplates/GameTemplateWorkflowTests.cs`
- Create: `src/Rekall.Age.GameTemplates/Commands/RunAgentAuthoringGauntletCommand.cs`

- [ ] **Step 1: Write the failing success-path test**

Add a test that executes `rekall.workflow.agent_authoring_gauntlet` for the `pong` template and asserts:
- result is ready
- checks include `create-playable`, `verify-playable`, `package-playable`, and `audit-package`
- package archive and proof frame exist
- recommended next actions include package inspection or package running

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~GameTemplateWorkflowTests.AgentAuthoringGauntlet"
```

Expected: compile failure because `RunAgentAuthoringGauntletCommand` does not exist.

- [ ] **Step 3: Implement the minimal command**

Create `RunAgentAuthoringGauntletCommand` with request/result/check records. Compose:
- `CreatePlayableGameFromTemplateCommand`
- `VerifyPlayableGameCommand`
- `PackagePlayableGameCommand`
- `AuditPlayablePackageCommand`

The command should stop after failed create, verify, or package steps, return failed checks, and include fix-oriented next actions such as `rekall.workflow.verify_playable_game`, `rekall.workflow.package_playable_game`, or `rekall.workflow.audit_playable_package`.

- [ ] **Step 4: Run the focused test and verify it passes**

Run the focused `GameTemplateWorkflowTests.AgentAuthoringGauntlet` filter again.

### Task 2: Discovery And Tooling Surface

**Files:**
- Modify: `tests/Rekall.Age.Tests/Mcp/McpCatalogTests.cs`
- Modify: `tests/Rekall.Age.Tests/Mcp/McpJsonRpcServerTests.cs`
- Modify: `tests/Rekall.Age.Tests/Cli/CliSmokeTests.cs`
- Modify: `src/Rekall.Age.Mcp/RekallAgeMcpCatalog.cs`
- Modify: `src/Rekall.Age.Agent/Commands/GetEngineStatusCommand.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`

- [ ] **Step 1: Write failing discovery and CLI tests**

Assert the MCP catalog exposes `rekall.workflow.agent_authoring_gauntlet` as a recommended workflow with priority before the existing one-shot package workflow. Assert `context engine` prints it. Assert `game gauntlet <root> <name> pong <packageOutput> <auditOutput>` exits successfully and prints readiness, checks, archive, proof frame, and next actions.

- [ ] **Step 2: Run focused tests and verify they fail**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~McpCatalogTests|FullyQualifiedName~McpJsonRpcServerTests.ToolsListExposesAgentToolMetadata|FullyQualifiedName~CliSmokeTests.Cli"
```

Expected: fail because the command is not registered or printed yet.

- [ ] **Step 3: Register and print the workflow**

Register `RunAgentAuthoringGauntletCommand` in CLI registry and relevant tests. Add CLI route `game gauntlet`. Update MCP classifier recommendation/priority. Add the workflow to engine status.

- [ ] **Step 4: Run focused tests and verify they pass**

Run the same focused filter.

### Task 3: Documentation And Verification

**Files:**
- Modify: `README.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Document the gauntlet**

Add a short README section explaining that the agent authoring gauntlet is the preferred closed-loop proof command and list the CLI/MCP tool names.

- [ ] **Step 2: Run full verification**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj
```

Expected: all tests pass.

