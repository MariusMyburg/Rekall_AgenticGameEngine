# Task 2 Report: Codex Project Runner and AGE MCP Bridge

## Outcome

Implemented the functional Codex App Server project-agent runner and connected each Codex project thread to AGE through the packaged `Rekall.Age.Cli mcp stdio` server.

The runner uses one ephemeral Codex thread per project run, an absolute project working directory, exact model `gpt-5.6-sol`, `workspace-write`, network disabled, and the project root as the only writable root. The MCP server is named `rekall-age` and is supplied as an exact executable path plus the structured argument array `mcp`, `stdio`, `--project-root`, and the normalized selected root; no shell command string is constructed.

## Implemented contracts

- `RekallAgeCodexMcpConfiguration`
  - resolves the packaged native CLI host from an installed distribution, application directory, or current development build;
  - validates the executable before the Codex client factory can start a process;
  - produces exactly one typed `RekallAgeCodexMcpServer` named `rekall-age`.
- `RekallAgeCodexProjectAgentRunner`
  - lazily owns and asynchronously disposes the Task 1 App Server client;
  - reads Codex-owned account state and visible model state without reading credentials or retaining account identity;
  - starts the restricted project thread and scoped task, then finishes only from terminal turn completion;
  - projects bounded agent messages, command/MCP tool progress, structured MCP errors, token usage, thread ID, tool count, and elapsed time into existing AGE project-agent result contracts;
  - exposes a typed approval callback, defaults noninteractive requests to `decline`, and projects explicit approval policies including `never`;
  - on cancellation, sends `turn/interrupt`, waits for terminal `turn/completed`, returns `REKALL_CODEX_CANCELLED`, and deterministically disposes the owned client process session;
  - preserves queued progress immediately preceding terminal completion before closing its bounded consumers.
- Shared provider catalog
  - adds the stable `codex` descriptor and default `gpt-5.6-sol`;
  - maps unknown, unauthenticated, ChatGPT, API-key, and model-unavailable facts into the existing safe descriptor/diagnostic records;
  - acquires the Codex runner through the existing provider lease, whose Task 1 async-disposal preference owns cleanup.
- Generic AGE developer instructions
  - require semantic `Rekall.InputActionMap` consumption through SDK actions;
  - require `DeltaSeconds`/`DeltaTime` simulation;
  - require attached agent-owned `Game.*` runtime state;
  - require `EmitObservation`/`EmitSceneObservation` diagnostics;
  - require strict `rekall.runtime.inspect_scene` transform/component delta evidence after the latest mutation;
  - require the closed-loop `rekall.workflow.agent_authoring_gauntlet` path;
  - explicitly tell Codex to author content itself rather than asking AGE to author it.

## TDD evidence

Observed RED before implementation for the missing runner/configuration types, packaged CLI resolver, shared provider description, provider acquisition, approval API, progress/error evidence, nested App Server usage shape, and terminal-drain ordering. Each boundary was then taken GREEN with the focused fake-process transcript tests.

The scripted tests cover:

- exact thread/MCP/sandbox payload and absence of a combined command string;
- packaged CLI discovery and pre-process validation;
- safe account/model descriptor projection and lazy shared lease acquisition;
- developer instruction requirements;
- agent text, MCP failure, structured AGE MCP errors, nested total token usage, tool count, thread ID, and elapsed time;
- default denial, callback-controlled exact response IDs, and explicit `never` policy;
- cancellation ordering: interrupt acknowledgement alone does not complete the run; terminal completion is required.

## Verification

- Focused Task 2 gate:
  - `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~CodexProjectAgentRunnerTests|FullyQualifiedName~McpJsonRpcServerTests|FullyQualifiedName~ProjectAgentRunnerTests"`
  - Passed with zero failures.
- Shared catalog coverage:
  - `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelProviderCatalogTests"`
  - Passed with zero failures.
- Warning-free solution build:
  - `dotnet build Rekall.AGE.sln --no-restore --warnaserror`
  - Succeeded with `0 Warning(s)` and `0 Error(s)`.

Final fresh verification is rerun after this report before commit.

## Scope and residuals

- No authenticated Codex process was started; Task 3 owns real local App Server and authored-game acceptance.
- No Task 1 late-turn, stalled-writer, or completion-cache hardening was changed.
- No credentials, tokens, account identity, or secret-bearing endpoint configuration were added or logged.
- No genre-specific engine behavior was added.

## Fix round 1: explicit ephemeral threads

- Added `Ephemeral` to the typed `RekallAgeCodexThreadStartRequest` contract.
- Every `RekallAgeCodexProjectAgentRunner` project run now opts in explicitly and sends `"ephemeral": true` in the `thread/start` request.
- Extended the exact restricted-thread fake App Server transcript test to require the flag.
- RED: the focused transcript test failed because `thread/start.params.ephemeral` was absent.
- GREEN: the same focused transcript test passed after the contract, client serialization, and project-run setting were connected.
- Deferred aggregate-result bounds and dead-client retry reset were intentionally left unchanged.

## Final integration fix round 1: MCP project-root authority

- Extracted `RekallAgeProjectCommandScope` from the embedded project-agent executor so Ollama/OpenAI and the external Codex MCP bridge share one normalization, defaulting, gateway-decoding, and validation rule.
- Codex now launches the packaged host with the structured arguments `mcp`, `stdio`, `--project-root`, `<normalized selected root>`.
- Scoped MCP servers default omitted direct and gateway target roots to the selected project and reject any different absolute root with `REKALL_AGENT_PROJECT_SCOPE_VIOLATION` before registry execution.
- Plain `mcp stdio` remains unscoped for explicit non-agent use. A real `rekall.project.create` MCP test preserves the ordinary Prism Relay-style path.
- RED evidence: the Codex transcript exposed missing root arguments; scoped MCP tests exposed the absent boundary; and the CLI rejected the scoped stdio form with exit code 2.
- GREEN evidence: focused MCP/project-session/Codex-runner/CLI tests passed 51/51; the one broader engine suite passed 2,075/2,075; the warning-as-error solution build completed with zero warnings and errors.
- Deferred lifecycle and aggregate-result hardening remained unchanged.
