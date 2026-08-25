# Task 3: Shared Provider Factory and CLI Integration

## Status

DONE

## Implementation summary

- Added the reusable `RekallAgeLanguageModelProviderCatalog`, inspectable `ollama`/`openai` descriptors, session-only settings, and ownership-safe leases.
- The catalog creates provider clients only after validating session auth. Missing OpenAI auth fails as `REKALL_OPENAI_API_KEY_MISSING` before `HttpClient` construction or network access.
- Leases own their `HttpClient` and project-agent runner; disposal is idempotent and stale runner/lease access is rejected.
- Routed all CLI language-model commands through the catalog while retaining positional Ollama forms. Added `agent providers`, OpenAI model listing/run/project-run routes, stable provider diagnostics, and provider-bearing actor IDs.
- OpenAI session key state is excluded from JSON serialization and has a redacted diagnostic representation.

## TDD evidence

### RED

1. `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelProviderCatalogTests"`
   - Observed expected compilation failure because `RekallAgeLanguageModelProviderCatalog`, descriptor, settings, and lease types did not exist.
2. `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentCliTests"`
   - Observed expected behavioral failure: `agent providers` exited `2` rather than the asserted `0` because the route did not exist.
3. `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelProviderCatalogTests"`
   - Observed expected serialization failure: the injected OpenAI session value was present in serialized settings.

### GREEN

`dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelProviderCatalogTests|FullyQualifiedName~AgentCliTests|FullyQualifiedName~ProjectAgentSessionTests"`

Observed: 21 passed, 0 failed, 0 skipped.

## Final verification

- Focused regression command above: passed (21/21).
- `dotnet build Rekall.AGE.sln --no-restore`: passed with 0 warnings and 0 errors.
- Self-review: reviewed the complete `ff6a8974ff48ec80af241e13a7bc010a454b691e..HEAD` task diff plus uncommitted task files; `git diff --check` reported no whitespace errors.

## Secret-redaction and ownership evidence

- Tests prove missing OpenAI auth does not invoke the `HttpClient` factory.
- Tests prove settings serialization excludes the OpenAI session key and CLI output does not contain the injected session credential.
- Tests prove a lease disposes its exact handler once, invalidates its runner, and provider re-acquisition returns a fresh runner.
- CLI creates actors as `rekall-{provider}-agent`; no account or credential identity is used.

## Commit

- `c58dcab` — `feat: select language model providers from the CLI`

## Concerns

None.

## Fix round 1 (review follow-up)

### Findings addressed

- Unsupported provider/model errors now retain requested and resolved values. The CLI renders bounded `Requested:` and `Resolved:` facts after the stable provider error code/message.
- Cancellation now has a dedicated CLI boundary result: `REKALL_LANGUAGE_MODEL_CANCELLED`, exit code 1, and no fatal/unhandled diagnostic.
- Added deterministic, loopback OpenAI Responses CLI coverage for a successful `agent run openai gpt-5.6-sol`, a provider-backed `agent run-project openai gpt-5.6-sol`, provider usage, and tool-execution output. The spawned CLI uses its ordinary catalog, lease, adapter, SSE reader, and agent loop; no live network or external credential is used.
- Provider lease disposal now uses an atomic exact-once state transition. A concurrent-disposal test runs 32 simultaneous callers against counting owned resources and asserts each is disposed once.

### Review-fix RED evidence

1. `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelProviderCatalogTests"`
   - Failed as expected: unsupported provider diagnostics had no requested value.
2. `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentCliTests"`
   - Failed as expected: unavailable-model CLI output omitted requested/resolved facts and a cancelled OpenAI CLI command rendered `Unexpected error` instead of the stable cancellation code.

### Review-fix GREEN evidence

`dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelProviderCatalogTests|FullyQualifiedName~AgentCliTests|FullyQualifiedName~ProjectAgentSessionTests"`

Observed: 25 passed, 0 failed, 0 skipped.

- `dotnet build Rekall.AGE.sln --no-restore`: passed with 0 warnings and 0 errors.
- `129b36c` — `fix: harden language model provider CLI`
