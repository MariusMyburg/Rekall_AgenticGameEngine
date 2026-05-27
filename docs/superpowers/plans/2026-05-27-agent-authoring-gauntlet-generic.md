# Rekall AGE Generic Agent Authoring Gauntlet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a closed-loop workflow that proves an agent can author, build, verify, package, audit, and capture a playable Rekall AGE project without using game templates.

**Architecture:** Implement the gauntlet as a command-bus workflow under `Rekall.Age.Workflows.Commands`. The workflow composes existing generic project, scene, blueprint, module, build, package, audit, and context commands, and returns structured checks plus next actions.

**Tech Stack:** C#/.NET 10, xUnit, existing Rekall AGE command bus, workflow commands, CLI, MCP catalog, README.

---

## File Map

- `src/Rekall.Age.Workflows/Commands/RunAgentAuthoringGauntletCommand.cs`: new workflow command and result records.
- `src/Rekall.Age.Cli/Program.cs`: command registration and `game gauntlet` route/output.
- `src/Rekall.Age.Agent/Commands/GetEngineStatusCommand.cs`: recommended workflow entry.
- `src/Rekall.Age.Mcp/RekallAgeMcpCatalog.cs`: recommendation and priority for the gauntlet.
- `src/Rekall.Age.Mcp/RekallAgeMcpJsonRpcServer.cs`: initialization guidance mentions the preferred proof loop.
- `tests/Rekall.Age.Tests/Workflows/AgentAuthoringGauntletTests.cs`: workflow success and failure tests.
- `tests/Rekall.Age.Tests/Cli/CliSmokeTests.cs`: CLI route assertion.
- `tests/Rekall.Age.Tests/Mcp/McpCatalogTests.cs`: MCP discovery assertion.
- `tests/Rekall.Age.Tests/Agent/AgentContextCommandTests.cs`: engine status assertion.
- `README.md`: document the generic gauntlet workflow and CLI/MCP names.

## Tasks

### Task 1: Workflow Contract

- [ ] Write failing tests that call `RunAgentAuthoringGauntletCommand` and assert no template wording, generic authoring checks, package archive, proof frame, and next actions.
- [ ] Run the focused workflow tests and verify failure because the command does not exist.
- [ ] Implement `RunAgentAuthoringGauntletCommand` by composing generic commands.
- [ ] Re-run focused workflow tests and verify pass.

### Task 2: Discovery And CLI

- [ ] Write failing tests for MCP catalog, engine status, and `game gauntlet`.
- [ ] Run focused tests and verify failure because the workflow is not registered or routed.
- [ ] Register the command, add CLI route/output, and mark it as recommended.
- [ ] Re-run focused tests and verify pass.

### Task 3: Documentation And Verification

- [ ] Update README with the generic agent authoring gauntlet.
- [ ] Run focused workflow, CLI, MCP, and agent context tests.
- [ ] Run the full test project or report any existing host-abort behavior separately.
