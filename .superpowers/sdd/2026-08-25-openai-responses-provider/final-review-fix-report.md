# OpenAI provider final-review fix report

Base reviewed: `bf547a7d3137e3969af34842057d9f25888e240c`

## Finding 1 — canonical diagnostics and rejected option facts

### RED

Command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAiLanguageModelClientTests|FullyQualifiedName~OpenAiResponseStreamReaderTests"
```

Result: expected failure, exit 1. The focused test assembly could not compile because `RekallAgeLanguageModelProviderException.ProviderDetail` did not exist (CS1061 at the new HTTP and SSE provider-detail assertions). This proves the separately retained bounded provider-detail contract was absent before production changes. The same RED test edits also changed boundary expectations from provider-derived/non-spec codes to the binding spec's canonical OpenAI taxonomy and added missing requested/resolved option facts.

### GREEN

The provider exception now carries a separately bounded/redacted `ProviderDetail`. HTTP status and recognized provider categories map to the exact spec taxonomy; transport and transient service failures map to `REKALL_OPENAI_UNAVAILABLE`. Local model, reasoning-effort, and temperature/reasoning-combination rejections now carry canonical codes plus explicit requested/resolved facts.

Command: same focused command as RED.

Result: exit 0, 71 passed, 0 failed, 0 skipped.

## Finding 2 — success-path credential echo protection

### RED

Command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~JsonSuccessRedactsSessionCredential|FullyQualifiedName~JsonSuccessRejectsCredential|FullyQualifiedName~CredentialBearingOpaqueContinuation|FullyQualifiedName~SseSuccessRedactsSessionCredential|FullyQualifiedName~SseSuccessRejectsCredential"
```

Result: expected failure, exit 1; 0 passed, 5 failed. JSON and SSE non-executable output still contained the dynamically generated in-memory session credential; JSON tool payload and inbound opaque continuation were accepted; the SSE tool-argument fixture reached premature EOF instead of being rejected at the payload boundary. Assertions intentionally reported booleans/codes only, so the generated credential never entered console evidence.

### GREEN

JSON response mapping now redacts the session credential from surfaced text, reasoning, response IDs, completion reasons, and retained non-executable message/reasoning fields. Tool-call payloads and inbound/retained executable opaque state containing the credential are rejected with bounded stable diagnostics. SSE text/reasoning redaction holds possible credential prefixes across delta boundaries; credential-aware tool deltas are withheld until safe and rejected before surfacing/execution when tainted.

Command: same five-test focused command as RED.

Result: exit 0, 5 passed, 0 failed, 0 skipped. No generated credential appeared in the command output.

Supplemental success-field RED/GREEN: `JsonSuccessRedactsSessionCredentialFromProviderStatusAndOpaquePropertyNames` first failed with a boolean-only absence assertion (exit 1, 0/1), proving provider status and an opaque JSON property name could still surface the credential. After redacting the fallback finish status and recursively redacting opaque property names as well as values, the same command exited 0 (1/1). The diagnostic probe replaced the generated credential before displaying structure; no credential entered console evidence.

## Finding 3 — provider descriptor/catalog contract

### RED

Command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelProviderCatalogTests|FullyQualifiedName~ProviderCommandsExposeBothProviders"
```

Result: expected failure, exit 1. The focused test assembly reported CS1061 for the absent descriptor properties `AuthenticationState`, `IsAvailable`, `Availability`, and `Diagnostics`. The RED also requires canonical `Local Ollama` / `OpenAI API` display names and CLI consumption of catalog-owned state without changing the existing positional constructor.

Consumer RED command:

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAiSessionKeyUnlocksTheSelectedProvider"
```

Result: expected failure, exit 1; 0 passed, 1 failed. After applying a session key, Studio still exposed the catalog's stale `required` authentication state instead of the session-specific `configured` descriptor.

### GREEN

The four-position descriptor constructor remains unchanged and now has init-only authentication state, availability, a derived availability label, and stable diagnostic facts. Catalog descriptions are session-aware; acquisition consumes those same facts. Exact display names are `Local Ollama` and `OpenAI API`. CLI renders descriptor state/diagnostic codes, while Studio refreshes its selected descriptor when in-memory authentication changes.

Commands/results:

- Engine catalog/CLI focus: exit 0, 9 passed, 0 failed, 0 skipped.
- Studio descriptor consumers (missing auth, provider switch, session-key update): exit 0, 3 passed, 0 failed, 0 skipped.

## Finding 4 — asynchronous provider-lease ownership

### RED

Command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~RepeatedAsyncLeaseDisposal|FullyQualifiedName~SynchronousLeaseDisposalStillPrefersAsyncRunner|FullyQualifiedName~ConcurrentLeaseDisposal"
```

Result: expected failure, exit 1. The focused test assembly reported CS1061 because `RekallAgeLanguageModelProviderLease.DisposeAsync` did not exist. The new tests require repeated callers to await one in-progress shutdown, prefer `IAsyncDisposable` when a runner implements both disposal contracts, retain synchronous compatibility, and dispose the owned HTTP resource exactly once.

### GREEN

The lease now implements both `IDisposable` and `IAsyncDisposable`; all callers share one in-progress disposal task, async runner shutdown is preferred, sync runner fallback remains, and owned HTTP disposal follows runner shutdown. Studio awaits provider release during switching and shutdown, including unused acquired leases. All three CLI provider lifetimes now use `await using`.

Commands/results:

- Lease/catalog plus ordinary CLI lifecycles: exit 0, 16 passed, 0 failed, 0 skipped.
- Studio provider-switch/shutdown/repeated-dispose lifecycles: exit 0, 9 passed, 0 failed, 0 skipped.

## Combined amended regression gate

Engine/provider command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAi|FullyQualifiedName~LanguageModelProviderCatalogTests|FullyQualifiedName~AgentCliTests|FullyQualifiedName~LanguageModelContractTests|FullyQualifiedName~ProjectAgentRunnerTests|FullyQualifiedName~OllamaLanguageModelClientTests"
```

Result: exit 0, 118 passed, 0 failed, 0 skipped.

Studio/provider command:

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~Provider|FullyQualifiedName~OpenAi|FullyQualifiedName~ShutdownCleansUp|FullyQualifiedName~RepeatedDisposeAwaits"
```

First result: exit 1, 11 passed, 2 failed. Both failures were exact expected-text changes required by the canonical `OpenAI API` descriptor and catalog-owned missing-auth diagnostic. Test expectations were updated without changing production behavior.

Rerun result: exit 0, 13 passed, 0 failed, 0 skipped.

## Finding 4 supplemental failure-path ownership RED/GREEN

Self-review found that an owned HTTP resource throwing from `Dispose` could leave the shared lease-completion task unresolved. This is part of the required exact-once asynchronous ownership boundary, not an unrelated lifecycle permutation.

RED command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~RepeatedAsyncLeaseDisposalCompletesWhenHttpDisposalFails" --verbosity minimal
```

Expected RED observed: exit 1, 0 passed, 1 failed. The assertion expected the synthetic disposal failure but received `TimeoutException` after five seconds, proving repeated callers could wait forever even though runner and HTTP disposal had been attempted. No secret fixture participated in this test.

Minimal GREEN: runner and HTTP disposal failures are collected before the single shared completion is resolved. HTTP release is still attempted after runner failure; repeated async and synchronous callers observe the same completed result, and each owned resource is attempted exactly once.

GREEN command: same focused command. Result: exit 0, 1 passed, 0 failed, 0 skipped.

## Finding 5 — acceptance provenance

### RED

The committed acceptance document was tested with a bounded PowerShell content assertion for the final implementation SHA, 81-test Studio evidence, fresh 2,020-test engine evidence, and explicit historical association of the prior 2,001 result. The assertion exited 1 with `PROVENANCE_ASSERTION=missing` against the pre-documentation `HEAD` version.

### GREEN

The production acceptance now identifies implementation commit `5c6f7a2251fca35cdfb95bc2c771c3a233473033` and tree `63551061bc9d81a4849505979b5b0d97f9f091e2`, records the fresh final-byte results, and identifies the previous 2,001-test result as historical evidence for earlier shared Agent/Workflow bytes. The same content assertion against the working document exited 0 with `PROVENANCE_ASSERTION=present`.

The previously reviewed deterministic gameplay artifact hashes were preserved rather than regenerated. The deterministic gameplay acceptance itself was freshly re-executed inside the final 2,020-test engine suite.

## Final-byte focused verification

After the supplemental ownership fix, both amended gates were rerun from the final implementation bytes:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAi|FullyQualifiedName~LanguageModelProviderCatalogTests|FullyQualifiedName~AgentCliTests|FullyQualifiedName~LanguageModelContractTests|FullyQualifiedName~ProjectAgentRunnerTests|FullyQualifiedName~OllamaLanguageModelClientTests" --verbosity minimal
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~Provider|FullyQualifiedName~OpenAi|FullyQualifiedName~ShutdownCleansUp|FullyQualifiedName~RepeatedDisposeAwaits" --verbosity minimal
```

Results:

- Engine/provider: exit 0, 119 passed, 0 failed, 0 skipped; 9 seconds test duration.
- Studio/provider: exit 0, 13 passed, 0 failed, 0 skipped; 528 milliseconds test duration.

## Required sequential full verification

Executed sequentially after all source and test changes:

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --verbosity minimal
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --verbosity minimal
dotnet build Rekall.AGE.sln --no-restore --verbosity minimal
```

Results:

- Full Studio: exit 0, 81 passed, 0 failed, 0 skipped; 48 seconds test duration.
- Full engine: exit 0, 2,020 passed, 0 failed, 0 skipped; 4 minutes 23 seconds test duration.
- Solution build: exit 0, succeeded with 0 warnings and 0 errors; 5.15 seconds.

The full engine result includes the deterministic ordinary OpenAI project-agent gameplay acceptance and existing Ollama/Qwen workflows. No live OpenAI call was made: the value-free environment gate reported `OPENAI_AUTH_STATE=absent` and `OPENAI_REAL_SMOKE_GATE=REKALL_OPENAI_API_KEY_MISSING`.

## Scans, residue, and self-review

- Credential-shaped added-line scan across the task diff excluding controller-owned `progress.md`: `0` matches.
- Credential-shaped report scan: `0` matches.
- Task-owned worktree-associated `dotnet`, `testhost`, and player processes: `0`.
- OpenAI acceptance and Studio provider/OpenAI/switch/partial temp-root patterns: `0` each.
- Repository `*.tmp` / `*.temp` files: `0`.
- `git diff --check` / staged `git diff --cached --check`: clean.
- Self-review covered every task-owned source, test, and documentation change from `bf547a7d3137e3969af34842057d9f25888e240c` through the implementation commit and evidence diff. The two explicitly deferred retry edge cases were not changed. No Codex implementation or unrelated refactor was added.
- Controller-owned `.superpowers/sdd/2026-08-25-openai-responses-provider/progress.md` remained modified but unstaged and is not part of either task commit.

## Commit provenance

- Tested implementation: `5c6f7a2251fca35cdfb95bc2c771c3a233473033` (`fix: close OpenAI provider review boundaries`), tree `63551061bc9d81a4849505979b5b0d97f9f091e2`.
- Production acceptance and this ignored SDD report are committed separately after the tested implementation; the evidence commit SHA is reported in the task handoff.
