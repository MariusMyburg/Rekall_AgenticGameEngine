# OpenAI Responses Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `gpt-5.6-sol` a first-class AGE language-model provider through the OpenAI Responses API while preserving the existing Ollama path and AGE-owned agent loop.

**Architecture:** Extend AGE's provider-neutral transcript contracts with provider call identity and streaming, implement a raw HTTPS/SSE Responses adapter, centralize provider creation, then expose provider/model selection through CLI and Studio. AGE continues to own tool policy, gameplay checkpoints, MCP execution, and delivery recovery.

**Tech Stack:** C# 13, .NET 10, `HttpClient`, `System.Text.Json`, SSE parsing, xUnit, WPF Studio, OpenAI Responses API.

**Spec:** `docs/superpowers/specs/2026-08-25-openai-codex-agent-backends-design.md`

## Global Constraints

- Provider IDs are exactly `ollama`, `openai`, and later `codex`.
- The OpenAI default model is exactly `gpt-5.6-sol`; unavailable models never silently fall back.
- Existing Ollama CLI invocations and installed Qwen workflows remain compatible.
- AGE's current generic agent loop, MCP tools, checkpoint enforcement, and gameplay-proof rules remain authoritative.
- API keys never enter project files, packages, settings, command results, logs, exceptions, or test snapshots.
- The default OpenAI endpoint is `https://api.openai.com/v1/`; custom non-HTTPS endpoints are accepted only for loopback.
- Tool aliases are deterministic, reversible, collision-safe, and translated back to canonical AGE tool names before policy evaluation.
- Cancellation terminates HTTP/SSE work; retry is bounded to transient 408, 429, and 5xx responses.
- Every unsupported or degraded option returns stable requested/resolved diagnostics; no silent omission.
- Real API acceptance is required when `OPENAI_API_KEY` is available and otherwise reports an explicit credential gate.

---

### Task 1: Provider-Neutral Transcript, Streaming, and Runner Contracts

**Files:**
- Modify: `src/Rekall.Age.Agent/LanguageModels/RekallAgeLanguageModelContracts.cs`
- Create: `src/Rekall.Age.Agent/LanguageModels/RekallAgeLanguageModelProviderException.cs`
- Modify: `src/Rekall.Age.Agent/LanguageModels/RekallAgeLanguageModelAgent.cs`
- Create: `src/Rekall.Age.Workflows/IRekallAgeProjectAgentRunner.cs`
- Create: `src/Rekall.Age.Workflows/RekallAgeLanguageModelProjectAgentRunner.cs`
- Test: `tests/Rekall.Age.Tests/Agent/LanguageModelContractTests.cs`
- Test: `tests/Rekall.Age.Tests/Agent/LanguageModelAgentTests.cs`
- Test: `tests/Rekall.Age.Tests/Workflows/ProjectAgentRunnerTests.cs`

**Interfaces:**
- Consumes: existing positional language-model records and `RekallAgeProjectAgentSession`.
- Produces: optional tool/response IDs, stream events, structured provider errors, and `IRekallAgeProjectAgentRunner` used by both Studio and Codex work.

- [ ] **Step 1: Write failing backward-compatibility and call-identity tests**

Construct every existing positional record unchanged, then assert these init-only additions round-trip:

```csharp
var call = new RekallAgeLanguageModelToolCall("rekall.engine.status", new JsonObject())
{
    Id = "call_123"
};
var toolResult = new RekallAgeLanguageModelMessage("tool", "{}", "rekall.engine.status")
{
    ToolCallId = "call_123"
};
Assert.Equal("call_123", call.Id);
Assert.Equal(call.Id, toolResult.ToolCallId);
```

Assert provider exceptions preserve code/provider/status/request ID/retryability/requested/resolved values and redact a supplied secret.

- [ ] **Step 2: Run the contract tests and verify RED**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelContractTests"
```

Expected: FAIL because the new properties, stream contracts, runner contract, and structured exception do not exist.

- [ ] **Step 3: Add the exact provider-neutral contracts**

Add init-only `Id`, `ToolCallId`, and `ResponseId` properties. Add:

```csharp
public enum RekallAgeLanguageModelStreamEventKind
{
    TextDelta,
    ThinkingDelta,
    ToolCallDelta,
    Usage,
    Completed
}

public sealed record RekallAgeLanguageModelStreamEvent(
    RekallAgeLanguageModelStreamEventKind Kind,
    string Text,
    RekallAgeLanguageModelResponse? Response = null);

public interface IRekallAgeStreamingLanguageModelClient
{
    IAsyncEnumerable<RekallAgeLanguageModelStreamEvent> StreamChatAsync(
        RekallAgeLanguageModelRequest request,
        CancellationToken cancellationToken);
}
```

Add optional cached-input/reasoning token usage as init properties. Add `RekallAgeLanguageModelProviderException` with stable structured fields and bounded redacted message construction.

- [ ] **Step 4: Write failing stream-consumption and runner tests**

Use a scripted streaming client that emits text, thinking, and one completed response. Assert the agent reports bounded progress, records the final response once, preserves provider call IDs on assistant/tool messages, and cancellation stops enumeration. Assert `RekallAgeLanguageModelProjectAgentRunner` delegates model discovery and project execution without changing the existing result.

- [ ] **Step 5: Implement stream consumption and the common runner**

The agent uses streaming only when the client implements the optional interface. Require exactly one `Completed` response; missing or duplicate completion is `REKALL_LANGUAGE_MODEL_STREAM_INVALID`. Preserve the existing non-streaming path byte-for-byte in behavior. The runner owns a `RekallAgeProjectAgentSession` and exposes `ProviderId`, `ListModelsAsync`, and `RunAsync` through `IRekallAgeProjectAgentRunner`.

- [ ] **Step 6: Run focused and existing agent regressions**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelContractTests|FullyQualifiedName~LanguageModelAgentTests|FullyQualifiedName~ProjectAgentRunnerTests|FullyQualifiedName~ProjectAgentSessionTests|FullyQualifiedName~OllamaLanguageModelClientTests"
```

Expected: zero failures and no warnings.

- [ ] **Step 7: Commit**

```powershell
git add src/Rekall.Age.Agent src/Rekall.Age.Workflows tests/Rekall.Age.Tests/Agent tests/Rekall.Age.Tests/Workflows
git commit -m "feat: generalize language model provider sessions"
```

---

### Task 2: OpenAI Responses HTTP and SSE Adapter

**Files:**
- Create: `src/Rekall.Age.Agent/LanguageModels/RekallAgeOpenAiLanguageModelClient.cs`
- Create: `src/Rekall.Age.Agent/LanguageModels/RekallAgeOpenAiToolNameMap.cs`
- Create: `src/Rekall.Age.Agent/LanguageModels/RekallAgeOpenAiResponseStreamReader.cs`
- Test: `tests/Rekall.Age.Tests/Agent/OpenAiLanguageModelClientTests.cs`
- Test: `tests/Rekall.Age.Tests/Agent/OpenAiToolNameMapTests.cs`
- Test: `tests/Rekall.Age.Tests/Agent/OpenAiResponseStreamReaderTests.cs`

**Interfaces:**
- Consumes: Task 1 transcript IDs, streaming events, and provider exception.
- Produces: `RekallAgeOpenAiLanguageModelClient : IRekallAgeLanguageModelClient, IRekallAgeStreamingLanguageModelClient`.

- [ ] **Step 1: Write failing tool-alias and endpoint-validation tests**

Cover canonical dotted names, pre-existing underscores, case differences, long names, collision pairs, stable input ordering, reverse lookup, duplicate canonical names, `https://api.openai.com/v1/`, loopback HTTP, and rejected remote HTTP. Assert aliases satisfy `^[A-Za-z0-9_-]{1,64}$` and include a deterministic hash suffix.

- [ ] **Step 2: Implement the pure alias map and endpoint normalizer**

Build aliases from a readable sanitized prefix plus the first 12 lowercase hexadecimal SHA-256 characters of the canonical name. Reject duplicates before any HTTP call. Normalize exactly one trailing slash.

- [ ] **Step 3: Write failing Responses payload/response tests**

With a recording `HttpMessageHandler`, assert:

- bearer authorization exists in the request but never in captured diagnostics;
- model is `gpt-5.6-sol` exactly;
- system policy becomes developer input;
- function tools use aliases and preserve canonical names in descriptions;
- assistant calls and `function_call_output` retain the same call ID;
- `max_output_tokens` and supported `reasoning.effort` map exactly;
- context-window tokens are not emitted as a false API field;
- output text, reasoning summary, calls, response ID, finish reason, and usage map back to AGE records;
- malformed/non-object arguments return `REKALL_OPENAI_TOOL_ARGUMENTS_INVALID`.

- [ ] **Step 4: Implement non-streaming model discovery and Responses mapping**

Use `GET models` and `POST responses`. Model discovery sorts accessible IDs ordinally and reports size zero rather than inventing weights. Parse structured OpenAI error JSON and the `x-request-id` header. Do not log bodies that may contain user content.

- [ ] **Step 5: Write failing SSE assembly, retry, and cancellation tests**

Feed fragmented lines, CRLF/LF, comments, multiple data lines, text deltas, reasoning deltas, function-call argument deltas, completion, provider error, malformed JSON, premature EOF, and cancellation. Assert 408/429/5xx bounded retries honor `Retry-After`; assert 400/401/403/model errors never retry.

- [ ] **Step 6: Implement the streaming reader and streaming client path**

Parse SSE incrementally without buffering an unbounded response. Bound one event and accumulated text/arguments. Emit provider-neutral deltas and exactly one completed response. Dispose every response/stream on success, error, retry, and cancellation.

- [ ] **Step 7: Run focused tests and the existing agent/Ollama suite**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAi|FullyQualifiedName~LanguageModelAgentTests|FullyQualifiedName~OllamaLanguageModelClientTests"
```

Expected: zero failures.

- [ ] **Step 8: Commit**

```powershell
git add src/Rekall.Age.Agent/LanguageModels tests/Rekall.Age.Tests/Agent
git commit -m "feat: add OpenAI Responses language model provider"
```

---

### Task 3: Shared Provider Factory and CLI Integration

**Files:**
- Create: `src/Rekall.Age.Workflows/RekallAgeLanguageModelProviderCatalog.cs`
- Create: `src/Rekall.Age.Workflows/RekallAgeLanguageModelProviderLease.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Test: `tests/Rekall.Age.Tests/Workflows/LanguageModelProviderCatalogTests.cs`
- Test: `tests/Rekall.Age.Tests/Cli/AgentCliTests.cs`

**Interfaces:**
- Consumes: Ollama and OpenAI model clients plus Task 1 runner.
- Produces: one ownership-safe provider factory used by CLI and Studio; new provider-aware CLI routes.

- [ ] **Step 1: Write failing catalog/factory tests**

Assert the catalog contains exact `ollama` and `openai` descriptors, defaults, display names, and auth kinds. Assert OpenAI without a key returns `REKALL_OPENAI_API_KEY_MISSING` before network access. Assert an owned lease disposes its exact `HttpClient`/runner once and provider switches cannot reuse a disposed session.

- [ ] **Step 2: Implement provider catalog and owned leases**

Create Ollama from `REKALL_AGE_OLLAMA_URL`; create OpenAI from `OPENAI_API_KEY` and optional `REKALL_AGE_OPENAI_URL`. Accept session-only injected settings for tests/Studio. Never expose the key through descriptor records.

- [ ] **Step 3: Write failing CLI behavior tests**

Cover exact commands:

```text
agent providers
agent models openai
agent run openai gpt-5.6-sol <task> [maxTurns]
agent run-project openai gpt-5.6-sol <root> <scene> <task> [maxTurns]
```

Assert existing `agent ... ollama` forms still parse. Assert missing auth, inaccessible model, cancellation, provider usage, and tool executions render bounded stable output without key text.

- [ ] **Step 4: Refactor CLI through the shared provider factory**

Keep existing positional syntax and output facts. Provider-specific creation belongs only in the factory. Use actor IDs containing the provider ID, never the API key or account identity.

- [ ] **Step 5: Run CLI/workflow regressions and commit**

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelProviderCatalogTests|FullyQualifiedName~AgentCliTests|FullyQualifiedName~ProjectAgentSessionTests"
git add src/Rekall.Age.Workflows src/Rekall.Age.Cli tests/Rekall.Age.Tests/Workflows tests/Rekall.Age.Tests/Cli
git commit -m "feat: select language model providers from the CLI"
```

---

### Task 4: Provider-Neutral Studio Authoring and OpenAI Acceptance

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioAutomation.cs`
- Test: `tests/Rekall.Age.Tests/Editor/StudioWorkbenchSourceTests.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`
- Create: `tests/Rekall.Age.Tests/Agent/OpenAiProjectAgentAcceptanceTests.cs`
- Create: `docs/production/2026-08-25-openai-provider-acceptance.md`

**Interfaces:**
- Consumes: shared provider catalog/lease, project runner, and durable Studio session state.
- Produces: Studio provider/model/auth selection and acceptance evidence for `gpt-5.6-sol`.

- [ ] **Step 1: Write failing provider-switch lifecycle tests**

Assert Studio exposes `LanguageModelProviders`, `LanguageModels`, `SelectedLanguageModelProvider`, `SelectedLanguageModel`, `ProviderStatus`, and refresh/run/cancel commands. Switching provider must cancel/await the current run, dispose its lease, clear stale models, load the selected provider, and select the exact default only when present. Missing OpenAI auth is a stable actionable status; Qwen remains selectable.

- [ ] **Step 2: Replace Ollama-only bindings with provider-neutral controls**

Add a compact provider selector, masked session-only API-key input/action, model selector, reasoning effort, refresh, run, and cancel controls. Do not bind or serialize the key into the workbench model. Output provider/model/response ID/usage/tool count/elapsed time and stable diagnostics.

- [ ] **Step 3: Add deterministic OpenAI project-agent acceptance**

Use fake HTTP Responses turns to execute real AGE MCP commands through `RekallAgeProjectAgentSession`. Prove canonical tool alias reversal, scene mutation, an attached `Game.*` component, semantic input, and a strict runtime component or transform delta after the latest mutation.

- [ ] **Step 4: Run optional real API smoke honestly**

When `OPENAI_API_KEY` exists, run `gpt-5.6-sol` through the ordinary CLI project-agent path against a fresh temporary project and require the same gameplay assertion plus package/audit/capture. When absent, record `REKALL_OPENAI_API_KEY_MISSING` as the external credential gate; do not mark the real API smoke passed.

- [ ] **Step 5: Run full verification**

Run focused provider tests, the full Studio suite, the full engine suite, and solution build sequentially. Every executed suite must have zero failures and the build must have zero warnings/errors.

- [ ] **Step 6: Update evidence and commit**

Record exact commands, durations, hashes, provider/model IDs, redacted auth state, gameplay assertions, package/audit/capture paths, process residue, and temp residue in the production Markdown.

```powershell
git add src/Rekall.Age.Studio tests/Rekall.Age.Studio.Tests tests/Rekall.Age.Tests docs/production/2026-08-25-openai-provider-acceptance.md
git commit -m "feat: author games with OpenAI models in Studio"
```

