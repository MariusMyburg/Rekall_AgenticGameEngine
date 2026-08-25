# OpenAI and Codex Agent Backends Design

**Status:** Approved on 2026-08-25. The user's standing pre-approval covers the implementation plan and automatic execution.

## Objective

Rekall AGE will support OpenAI's `gpt-5.6-sol` as a first-class game-authoring model while retaining local Ollama/Qwen support. It will expose two complementary integrations:

1. `openai` uses the OpenAI Responses API inside AGE's existing provider-neutral agent loop.
2. `codex` uses the Codex App Server as a full external agent runtime connected to AGE through its existing MCP stdio server.

Both integrations ship before the next Aetherfall authoring milestone. They create new authenticated sessions; they do not attach to or take over the current Codex conversation.

## Architectural decisions

### AGE remains provider-neutral

`IRekallAgeLanguageModelClient` remains the boundary for model providers that return messages and tool calls to `RekallAgeLanguageModelAgent`. Ollama and OpenAI Responses implement this boundary. Engine checkpoints, tool discovery, mutation policy, runtime gameplay proof, and delivery recovery therefore remain identical across Qwen and OpenAI models.

Codex App Server is not forced through this interface. Codex already owns a complete agent loop, thread state, tool execution, and streamed events. Nesting it inside `RekallAgeLanguageModelAgent` would duplicate orchestration and weaken diagnostics. A new `IRekallAgeProjectAgentRunner` boundary lets Studio and CLI select either:

- `RekallAgeLanguageModelProjectAgentRunner`, which wraps the current AGE loop for `ollama` and `openai`;
- `RekallAgeCodexProjectAgentRunner`, which owns one Codex App Server process/session and supplies AGE's MCP server to that runtime.

Qwen remains an offline/local option. No provider becomes an engine-core gameplay dependency.

### Stable provider identities

Provider IDs and initial defaults are exact:

| Provider ID | Display name | Default model | Authentication |
|---|---|---|---|
| `ollama` | Local Ollama | `qwen3.5:35b` when installed | Local service |
| `openai` | OpenAI API | `gpt-5.6-sol` | `OPENAI_API_KEY` or a session-only injected key |
| `codex` | Codex | `gpt-5.6-sol` | Codex-managed ChatGPT login or API key |

Provider selection and model selection are separate. Missing defaults never silently select another provider or model.

## Shared contracts

Add immutable provider records carrying ID, display name, default model, authentication state, availability, and stable diagnostics. Add a provider factory/registry that owns clients and runners, rather than constructing Ollama directly in Studio or CLI.

Extend tool-call transcript fidelity without breaking existing positional constructors:

- `RekallAgeLanguageModelToolCall.Id` stores the provider call ID.
- `RekallAgeLanguageModelMessage.ToolCallId` associates a tool result with the originating call.
- `RekallAgeLanguageModelResponse.ResponseId` preserves the provider response ID.
- usage adds optional cached-input and reasoning-output token facts while retaining existing totals.

Add a generic `RekallAgeLanguageModelProviderException` with `Code`, `ProviderId`, `HttpStatus`, `RequestId`, `Retryable`, `RequestedValue`, and `ResolvedValue`. Exception messages and logs may contain bounded provider error text but never credentials, authorization headers, or full sensitive request bodies.

Streaming is an optional capability, not a new requirement for every provider. `IRekallAgeStreamingLanguageModelClient` emits bounded text/reasoning/tool-progress/usage events and a final `RekallAgeLanguageModelResponse`. The agent consumes it when implemented and otherwise uses `ChatAsync`. Cancellation always terminates HTTP reads or child-process work.

## OpenAI Responses provider

### Transport and authentication

`RekallAgeOpenAiLanguageModelClient` uses HTTPS and JSON/SSE against `https://api.openai.com/v1/` by default. `REKALL_AGE_OPENAI_URL` may override the endpoint for tests or compatible gateways; non-HTTPS non-loopback endpoints are rejected. `OPENAI_API_KEY` is read at process/session construction. Keys are never persisted into projects, scenes, packages, logs, diagnostics, command results, or Studio settings.

The client uses:

- `GET /v1/models` for accessible model discovery;
- `POST /v1/responses` for agent turns;
- `stream: true` for incremental events, internally assembled into the provider-neutral final response.

`gpt-5.6-sol` is selected exactly when requested. An inaccessible or unknown model returns a stable provider error and does not fall back.

### Request mapping

AGE messages map to Responses input items. System policy becomes developer instructions. User and assistant text preserve order. Tool results use `function_call_output` with the original provider call ID.

AGE MCP tools map to Responses function tools. Provider function names must be safe and deterministic. The adapter creates a reversible alias from the canonical AGE tool name, retains a collision-proof suffix, and maps returned calls back to the exact canonical name before policy evaluation. The canonical name remains visible in the tool description. Invalid or duplicate mappings fail before the network request.

Request options map as follows:

- `MaxOutputTokens` -> `max_output_tokens`;
- supported `Think` values -> `reasoning.effort`;
- context-window limits remain AGE-side transcript bounds and are not falsely sent as a provider option;
- unsupported combinations such as a rejected temperature/reasoning setting return or record stable requested/resolved facts instead of disappearing silently.

### Response mapping and reliability

The adapter assembles text, reasoning summaries, function calls, finish status, response ID, usage, cached input, and reasoning token counts. Function-call arguments must parse as one JSON object; malformed arguments return `REKALL_OPENAI_TOOL_ARGUMENTS_INVALID` with the affected call ID.

Retry behavior is bounded and cancellation-aware. HTTP 408, 429, and transient 5xx responses may retry using `Retry-After` plus bounded backoff. Authentication, permission, invalid-request, and model errors do not retry. Stable codes include:

- `REKALL_OPENAI_API_KEY_MISSING`
- `REKALL_OPENAI_AUTHENTICATION_FAILED`
- `REKALL_OPENAI_MODEL_UNAVAILABLE`
- `REKALL_OPENAI_RATE_LIMITED`
- `REKALL_OPENAI_REQUEST_INVALID`
- `REKALL_OPENAI_TOOL_ARGUMENTS_INVALID`
- `REKALL_OPENAI_RESPONSE_INVALID`
- `REKALL_OPENAI_UNAVAILABLE`

## Codex App Server provider

### Runtime boundary

`RekallAgeCodexAppServerClient` launches the installed Codex executable with `ProcessStartInfo.ArgumentList`, never through a shell. The default command is `codex app-server --listen stdio://`; `REKALL_AGE_CODEX_PATH` may select an explicit executable. Missing or incompatible runtimes return `REKALL_CODEX_RUNTIME_MISSING` or `REKALL_CODEX_PROTOCOL_UNSUPPORTED` with an install/update action.

The client implements the stable JSONL/JSON-RPC lifecycle from the installed generated schema:

1. start process and concurrently drain bounded, redacted stderr;
2. send `initialize` and `initialized`;
3. call `account/read` and `model/list`;
4. call `thread/start` with absolute project `cwd`, exact model, `workspace-write`, network disabled by default, and AGE developer instructions;
5. call `turn/start` with the user's task;
6. stream item/agent-message/tool-progress notifications;
7. finish only on `turn/completed` or explicit cancellation through `turn/interrupt`;
8. close stdin, wait a bounded interval, then terminate only the exact owned process tree if it does not exit.

One writer serializes requests. A monotonically increasing request ID maps responses to pending tasks. Unknown IDs, malformed JSON, premature EOF, and process exit produce stable protocol diagnostics and complete every pending request exactly once.

### AGE tool connection

Codex receives AGE tools through the existing packaged CLI command `Rekall.Age.Cli mcp stdio`. The App Server thread configuration points an MCP server named `rekall-age` at the exact packaged CLI executable and arguments using structured config/argument APIs. AGE does not use experimental dynamic tools for the production path.

The Codex thread uses the project root as its only writable root. It receives the same generic authoring requirements: semantic input, delta-time simulation, runtime gameplay assertions after the latest mutation, inspectable diagnostics, and the closed-loop `rekall.workflow.agent_authoring_gauntlet` delivery path. App Server approvals and MCP errors are surfaced in Studio; they are never silently auto-approved beyond the user's configured AGE policy.

Codex authentication remains owned by Codex. AGE calls `account/read` and can initiate the documented login flow, but it never reads or copies stored tokens. ChatGPT and API-key modes are reported without exposing account secrets.

Stable codes include:

- `REKALL_CODEX_RUNTIME_MISSING`
- `REKALL_CODEX_PROTOCOL_UNSUPPORTED`
- `REKALL_CODEX_AUTHENTICATION_REQUIRED`
- `REKALL_CODEX_MODEL_UNAVAILABLE`
- `REKALL_CODEX_PROCESS_EXITED`
- `REKALL_CODEX_PROTOCOL_INVALID`
- `REKALL_CODEX_TURN_FAILED`
- `REKALL_CODEX_CANCELLED`

## Studio and CLI

Studio replaces Ollama-specific labels with a provider selector, provider status/auth action, model selector, refresh action, reasoning-effort selector, and the existing task/run/cancel surface. Changing provider cancels and disposes the active runner, clears provider-specific models, loads the new provider, and selects its exact default only when available. Agent output includes provider, model, thread/response ID, token usage, tool count, elapsed time, stable diagnostics, and durable execution evidence.

CLI keeps every existing Ollama invocation and adds:

- `agent providers`
- `agent models openai`
- `agent models codex`
- `agent run openai <model> <task> [maxTurns]`
- `agent run-project openai <model> <root> <scene> <task> [maxTurns]`
- `agent run-project codex <model> <root> <scene> <task>`
- `agent auth codex status`

Provider creation is shared between CLI and Studio. No command contains a hard-coded provider-specific agent policy.

## Security and lifecycle

- No API key, bearer token, authorization header, or Codex credential enters source control, project data, packages, evidence archives, logs, crash reports, or UI history.
- Custom endpoints must be absolute HTTPS, except explicit loopback HTTP used for local tests/compatible services.
- Network retries are bounded and honor cancellation.
- Provider clients, HTTP responses, SSE streams, Codex processes, pending JSON-RPC requests, and MCP child processes have deterministic disposal.
- Project roots and executable paths use literal structured arguments. No command string is passed through PowerShell, `cmd.exe`, or a shell.
- Codex sandbox and AGE command validation both remain active; provider choice never grants broader filesystem authority.

## Verification and acceptance

The implementation requires RED-to-GREEN tests at each boundary:

1. contract/transcript tests for call IDs, provider diagnostics, and backward compatibility;
2. fake-HTTP Responses tests for exact payloads, safe tool alias round-trips, SSE assembly, usage, malformed calls, errors, retry, cancellation, endpoint validation, and secret redaction;
3. CLI and Studio tests for provider switching, model discovery/defaults, missing auth, lifecycle cancellation, and preserved Ollama behavior;
4. fake-process App Server tests for initialize/account/model/thread/turn sequencing, out-of-order response IDs, notifications, stderr bounds, EOF, cancellation, and process cleanup;
5. real local App Server smoke using the installed authenticated Codex runtime and `gpt-5.6-sol`;
6. a real Codex-authored project gauntlet proving tool use, an attached agent-owned component, semantic input, a strict runtime transform/component delta after the latest mutation, package, audit, and nonblank capture;
7. full engine and Studio suites with zero failures and a clean solution build.

The real OpenAI API gauntlet runs when `OPENAI_API_KEY` is available. Its absence is an explicit external credential gate, not a fabricated pass. The real authenticated Codex App Server gauntlet remains mandatory on this development machine because Codex login is available.

## Non-goals

- Replacing Ollama or removing Qwen.
- Embedding provider behavior into engine gameplay/runtime contracts.
- Persisting secrets in AGE.
- Using experimental App Server dynamic tools in the production integration.
- Treating a model response, compile, package, or screenshot alone as proof of executable gameplay.

## Official references

- [GPT-5.6 Sol model](https://developers.openai.com/api/docs/models/gpt-5.6-sol)
- [Codex SDK](https://developers.openai.com/codex/sdk/)
- [Codex App Server](https://developers.openai.com/codex/app-server/)
- [Codex CLI reference](https://developers.openai.com/codex/cli/reference/)

