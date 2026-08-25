# Codex App Server Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a full Codex project-agent runner using the authenticated Codex App Server and AGE's MCP stdio server, with `gpt-5.6-sol` as the default model.

**Architecture:** A lifecycle-safe C# JSONL/JSON-RPC client owns the Codex process. A project runner starts isolated Codex threads in the project root, exposes AGE through packaged MCP stdio, streams events into existing Studio progress, and returns the shared project-agent result without nesting Codex inside AGE's language-model loop.

**Tech Stack:** C# 13, .NET 10, `System.Diagnostics.Process`, JSONL/JSON-RPC 2.0 semantics, Codex App Server v2 protocol, AGE MCP stdio, xUnit, WPF Studio.

**Spec:** `docs/superpowers/specs/2026-08-25-openai-codex-agent-backends-design.md`

## Global Constraints

- Provider ID is exactly `codex`; default model is exactly `gpt-5.6-sol`.
- Codex owns its credentials; AGE never reads, copies, logs, or persists tokens.
- Production tool integration uses AGE's existing MCP stdio server, not experimental App Server dynamic tools.
- The Codex process is launched with structured `ProcessStartInfo.ArgumentList`, never a shell command string.
- The project root is the only writable root; default network access is disabled.
- App Server request IDs, notifications, cancellation, EOF, stderr, process exit, and pending tasks are bounded and deterministic.
- Provider choice never broadens filesystem authority or weakens AGE command validation.
- Real authenticated Codex smoke and real game-authoring gauntlet are mandatory on this machine.

---

### Task 1: Codex App Server Protocol and Process Lifecycle

**Files:**
- Create: `src/Rekall.Age.Agent/Codex/RekallAgeCodexAppServerContracts.cs`
- Create: `src/Rekall.Age.Agent/Codex/IRekallAgeCodexProcess.cs`
- Create: `src/Rekall.Age.Agent/Codex/RekallAgeCodexProcess.cs`
- Create: `src/Rekall.Age.Agent/Codex/RekallAgeCodexAppServerClient.cs`
- Test: `tests/Rekall.Age.Tests/Agent/CodexAppServerClientTests.cs`
- Test: `tests/Rekall.Age.Tests/Agent/CodexProcessLifecycleTests.cs`

**Interfaces:**
- Consumes: installed `codex app-server --listen stdio://` and the stable v2 methods documented in the spec.
- Produces: initialize/account/model/thread/turn operations plus a bounded notification stream.

- [ ] **Step 1: Write failing protocol sequencing tests**

Use a fake duplex process. Assert exact sequence and payloads for `initialize`, `initialized`, `account/read`, paginated `model/list`, `thread/start`, `turn/start`, `turn/interrupt`, and `turn/completed`. Feed out-of-order numeric response IDs and prove each pending task completes once.

- [ ] **Step 2: Implement typed contracts and the single-writer client**

Use one writer lock/channel, monotonically increasing IDs, and a pending-request dictionary. A dedicated stdout reader distinguishes responses, server requests, and notifications. Bound one line, stderr history, pending requests, and notification backlog. Unknown IDs become diagnostics without completing unrelated work.

- [ ] **Step 3: Write failing lifecycle/error tests**

Cover missing executable, incompatible initialize response, malformed JSON, oversized line, premature EOF, nonzero exit, cancellation before/after turn start, unresponsive shutdown, stderr flooding, and concurrent disposal. Assert the exact owned process tree is terminated only after graceful close/interrupt timeouts.

- [ ] **Step 4: Implement process ownership and stable errors**

Add exact codes from the spec. Start stderr/stdout drains immediately. On disposal: interrupt an active turn, close stdin, wait boundedly, then terminate only the owned process tree. Complete all pending tasks once with a structured provider exception.

- [ ] **Step 5: Run focused tests and commit**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~CodexAppServerClientTests|FullyQualifiedName~CodexProcessLifecycleTests"
git add src/Rekall.Age.Agent/Codex tests/Rekall.Age.Tests/Agent
git commit -m "feat: control Codex App Server sessions"
```

---

### Task 2: Codex Project Runner and AGE MCP Bridge

**Files:**
- Create: `src/Rekall.Age.Workflows/RekallAgeCodexProjectAgentRunner.cs`
- Create: `src/Rekall.Age.Workflows/RekallAgeCodexMcpConfiguration.cs`
- Modify: `src/Rekall.Age.Workflows/RekallAgeLanguageModelProviderCatalog.cs`
- Test: `tests/Rekall.Age.Tests/Workflows/CodexProjectAgentRunnerTests.cs`
- Test: `tests/Rekall.Age.Tests/Mcp/McpJsonRpcServerTests.cs`

**Interfaces:**
- Consumes: Task 1 client, `IRekallAgeProjectAgentRunner`, and packaged `Rekall.Age.Cli mcp stdio`.
- Produces: `RekallAgeCodexProjectAgentRunner` with model discovery, auth state, project turn execution, progress, cancellation, and result mapping.

- [ ] **Step 1: Write failing thread/configuration tests**

Assert `thread/start` receives absolute project `cwd`, exact model, `workspace-write`, no extra writable roots, network disabled, generic AGE developer instructions, and structured config for MCP server `rekall-age` with exact CLI executable plus `mcp`, `stdio` arguments. Assert no shell-escaped command string exists.

- [ ] **Step 2: Implement MCP configuration and project runner**

Resolve the packaged CLI path explicitly. Validate it before starting Codex. Map `account/read` and `model/list` to provider status/models. Start one ephemeral thread per project run, stream bounded agent/tool progress, finish on `turn/completed`, and map failure/cancellation to stable result diagnostics.

- [ ] **Step 3: Write failing policy/evidence tests**

With a scripted App Server, prove the developer instructions require semantic input, delta time, agent-owned runtime state, strict post-mutation gameplay assertions, and the gauntlet delivery path. Prove MCP errors remain visible. Prove approval requests follow the configured policy and are never silently accepted.

- [ ] **Step 4: Implement approval and cancellation routing**

Expose an approval callback on the runner. Studio can answer; noninteractive CLI defaults to denial unless `--approval-policy never` was explicitly selected. Cancellation sends `turn/interrupt`, waits for `turn/completed`, then uses lifecycle cleanup.

- [ ] **Step 5: Run workflow/MCP tests and commit**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~CodexProjectAgentRunnerTests|FullyQualifiedName~McpJsonRpcServerTests|FullyQualifiedName~ProjectAgentRunnerTests"
git add src/Rekall.Age.Workflows tests/Rekall.Age.Tests/Workflows tests/Rekall.Age.Tests/Mcp
git commit -m "feat: connect Codex agents to AGE MCP tools"
```

---

### Task 3: CLI, Studio, Real Codex Gauntlet, and Delivery Evidence

**Files:**
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioAutomation.cs`
- Test: `tests/Rekall.Age.Tests/Cli/AgentCliTests.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`
- Create: `tests/Rekall.Age.Tests/Workflows/CodexAgentAcceptanceTests.cs`
- Create: `docs/production/2026-08-25-codex-agent-acceptance.md`

**Interfaces:**
- Consumes: Codex runner and the provider-neutral UI/CLI from the OpenAI plan.
- Produces: end-user Codex selection/auth/model/run/cancel flows and real acceptance evidence.

- [ ] **Step 1: Write failing CLI and Studio integration tests**

Cover `agent models codex`, `agent run-project codex`, and `agent auth codex status`. Assert Studio provider switching disposes the prior runner, reports ChatGPT/API-key/unauthenticated state without secrets, lists `gpt-5.6-sol`, streams progress, routes approvals, and cancels/awaits an active turn on disposal.

- [ ] **Step 2: Implement the end-user Codex surfaces**

Reuse the existing provider selector and runner contract. Add Codex login/status actions backed by App Server account methods. Do not read auth files directly. Keep Ollama/OpenAI behavior and automation arguments compatible.

- [ ] **Step 3: Run a real authenticated protocol smoke**

Use the installed Codex runtime, require `account/read` to report authenticated, require `model/list` to contain `gpt-5.6-sol`, start an ephemeral read-only smoke thread, complete one bounded turn, cancel no processes, and record runtime/model/account-mode facts without email or tokens.

- [ ] **Step 4: Run the real Codex-authored AGE gauntlet**

Create a fresh temporary project and ask `gpt-5.6-sol` to author a small non-Aetherfall 3D game solely through AGE MCP tools. Require the closed-loop gauntlet plus independent assertions after the latest mutation: representative semantic input, attached `Game.*` component, nonzero transform delta or changed agent-owned property, successful module build, package, audit, and nonblank capture. Repair any generic engine defect exposed and rerun the strict assertion; never weaken it.

- [ ] **Step 5: Run full zero-failure verification**

Run Codex-focused tests, full Studio, full engine, and solution build sequentially. Audit no Codex/AGE child process remains. Record exact bounded temp residue; never claim blocked cleanup succeeded.

- [ ] **Step 6: Update Markdown and commit**

Record protocol version/runtime version, auth mode without identity, model, exact commands/durations, streamed event/tool counts, gameplay assertions, package/audit/capture hashes, process cleanup, and all test totals.

```powershell
git add src/Rekall.Age.Cli src/Rekall.Age.Studio tests/Rekall.Age.Tests tests/Rekall.Age.Studio.Tests docs/production/2026-08-25-codex-agent-acceptance.md
git commit -m "feat: author games with Codex in AGE"
```

