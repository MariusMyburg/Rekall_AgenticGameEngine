# Task 1 report — provider-neutral transcript, streaming, and runner contracts

## Outcome

Task 1 is complete on `codex/high-fidelity-forward-plus` from base commit
`5bd6644fbf9a0d2802c1ab388ee6f78ecb82edb8`.

- Implementation commit: `27b24cab04b0b3d34e8de0f7a67f32226af2432c`
- Commit subject: `feat: generalize language model provider sessions`
- Evidence/ledger changes are committed separately after this report so the report can name the implementation commit without self-reference.

## RED evidence

Strict RED→GREEN TDD was used after reading `writing-good-tests.md`. Each test names an observable production break and exercises the real contract/agent/session behavior; language-model clients are doubled only at the external provider boundary.

| Cycle | RED command/result | Wall time |
|---|---|---:|
| Provider-neutral contracts | `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelContractTests"` failed to compile with the intended missing `Id`, `ToolCallId`, `ResponseId`, optional usage, stream-event, and provider-exception symbols. | 4.0s |
| Agent streaming | Targeted stream filter failed 4/4: three cases proved the agent incorrectly called `ChatAsync`; the cancellation case timed out because enumeration never started. | 10.3s |
| Cancellation regression | After streaming began, the disposal assertion stayed RED and isolated a cancellation race: outer `WaitAsync` could complete before the linked per-turn token reached the iterator. | 5.0s |
| Common runner | `ProjectAgentRunnerTests` failed to compile because `IRekallAgeProjectAgentRunner` and `RekallAgeLanguageModelProjectAgentRunner` did not exist. | 3.8s |
| Structured-field redaction | The focused provider-exception test failed for all three loggable fields because request/requested/resolved facts contained the supplied secret verbatim. | 5.2s |

The runner's first GREEN attempt printed identical expected/actual results but failed record equality because transcript arrays compare by reference. The assertion was corrected to recursive structural equivalence; no production change was made for that test defect.

## Exact contract choices

- Existing positional parameter lists remain unchanged.
- `RekallAgeLanguageModelToolCall.Id`, `RekallAgeLanguageModelMessage.ToolCallId`, and `RekallAgeLanguageModelResponse.ResponseId` are nullable init-only `string` properties.
- `RekallAgeLanguageModelUsage.CachedInputTokens` and `.ReasoningTokens` are nullable init-only `int` properties; agent results sum provided values and remain `null` when no provider supplied them.
- Streaming uses the exact approved `RekallAgeLanguageModelStreamEventKind` values (`TextDelta`, `ThinkingDelta`, `ToolCallDelta`, `Usage`, `Completed`), stream-event record, and optional `IRekallAgeStreamingLanguageModelClient` interface.
- `RekallAgeLanguageModelProviderException` exposes `Code`, `ProviderId`, nullable numeric `HttpStatus`, `RequestId`, `Retryable`, `RequestedValue`, and `ResolvedValue`. Provider-controlled message text is capped at 4,096 characters. Caller-supplied sensitive values are replaced with `[REDACTED]` in `Message`, `ToString()`, request ID, requested value, and resolved value.
- `IRekallAgeProjectAgentRunner` reuses the existing `RekallAgeProjectAgentSessionRequest`, `RekallAgeProjectAgentSessionResult`, and `IProgress<RekallAgeLanguageModelAgentProgress>` contracts. `RekallAgeLanguageModelProjectAgentRunner` owns one existing session and delegates provider identity, discovery, and execution directly.

No OpenAI-, Responses-, or Codex-specific fields were added to provider-neutral contracts.

## Backward-compatibility proof

- Tests construct every existing language-model positional record without changing its constructor call.
- Ollama does not implement the optional streaming interface, so the agent continues through the existing `ChatAsync` path with the same request options, context construction, timeout recovery, transcript processing, and checkpoint enforcement.
- Existing agent, project-session, and Ollama tests are part of the 89-test focused gate and all pass.
- No project references changed.
- Runtime authoring/gameplay checkpoint code was not relaxed or bypassed.

## Streaming lifecycle and errors

- The agent selects streaming only through `IRekallAgeStreamingLanguageModelClient`; otherwise it calls `ChatAsync` exactly as before.
- Text, thinking, tool-call, usage, and completion facts use the existing progress callback with phases `model.text_delta`, `model.thinking_delta`, `model.tool_call_delta`, `model.usage`, and `model.completed`.
- Provider-controlled progress messages are capped at 4,096 characters.
- A stream must end after exactly one non-null `Completed` response. Missing completion, null completion response, duplicate completion, unknown event kinds, or any event after completion throws `RekallAgeLanguageModelProviderException` with stable code `REKALL_LANGUAGE_MODEL_STREAM_INVALID` and `Retryable = false`.
- Per-turn cancellation/timeouts bound stream reads. Caller cancellation explicitly cancels the owned per-turn token before abandoning provider work, which stops enumeration and prevents tool execution after cancellation.
- Assistant transcript messages retain the provider tool-call objects and IDs. Each tool-result message records the exact originating `ToolCallId`; the next provider request retains that association.
- Each completed provider response is added to the transcript once; deltas are progress only.

## Verification evidence

Baseline before edits:

- Focused existing agent/session/Ollama filter: 79 passed, 0 failed, 0 skipped; test duration 623ms; command wall time 25.1s including the initial build.

GREEN cycles:

- Initial contract filter: 4/4 passed in 6.9s (15ms test duration).
- Final contract filter including structured-field redaction: 5/5 passed in 6.7s (15ms test duration).
- Targeted streaming lifecycle filter: 4/4 passed in 5.3s (38ms test duration).
- Runner filter: 1/1 passed in 5.1s (28ms test duration).

Required focused regression command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelContractTests|FullyQualifiedName~LanguageModelAgentTests|FullyQualifiedName~ProjectAgentRunnerTests|FullyQualifiedName~ProjectAgentSessionTests|FullyQualifiedName~OllamaLanguageModelClientTests"
```

Result: 89 passed, 0 failed, 0 skipped; test duration 623ms; command wall time 4.3s; no compiler or test warnings.

Solution build command:

```powershell
dotnet build Rekall.AGE.sln --no-restore
```

Result: build succeeded, 0 warnings, 0 errors; MSBuild elapsed 6.26s; command wall time 6.7s.

`git diff --cached --check` reported no whitespace errors. Git emitted only the repository's normal Windows line-ending conversion notices while staging.

## Cleanliness and residual concerns

At implementation commit `27b24cab04b0b3d34e8de0f7a67f32226af2432c`:

- Worktree status: clean.
- Matching long-lived `dotnet`, `testhost`, or Rekall processes rooted in this worktree: 0.
- Untracked repository temp/backup artifacts: 0.
- No project-reference changes or installed dependencies were introduced.

No blocking concerns. Sensitive-value redaction is intentionally driven by the caller-supplied sensitive-value collection; provider implementations must supply their credentials when constructing provider exceptions. A provider stream that never terminates remains bounded by the existing per-turn deadline or caller cancellation.

## Review fix round 1 — 2026-08-25

Implementation commit: `5ba97ffc354338d341be327a21eab79939e319d9` (`fix: harden language model provider boundaries`).

### Root causes and exact contract repairs

1. **Structured exception identifiers could expose credentials.** The initial exception implementation applied the sensitive-value policy to the human-readable message and request/value fields, but assigned `Code` and `ProviderId` directly after only nonblank validation. A caller-supplied credential embedded in either identifier therefore remained available to serializers and structured logs. `Code` and `ProviderId` now pass through a bounded, deterministic identifier policy before assignment: valid canonical values remain useful, invalid values are normalized where possible, any value containing a supplied secret uses the stable fallback `REKALL_LANGUAGE_MODEL_PROVIDER_ERROR` or `unknown`, and blank/oversized/unusable values also fall back. Representative structured serialization proves the supplied secret is absent from `Message`, `ToString()`, `Code`, `ProviderId`, `RequestId`, `RequestedValue`, and `ResolvedValue`.
2. **The stream boundary trusted C# nullability annotations at runtime.** The initial consumer checked only the outer completed response. Scripted providers or deserializers can still construct null stream events and records whose non-nullable members are null, allowing raw `NullReferenceException` or malformed state to escape. The consumer now rejects null events, null event text, null/blank required response identities, null content/thinking/tool-call collection/finish reason/usage, null tool-call entries, and null/blank required tool-call members before transcript or progress use. Every malformed shape becomes `RekallAgeLanguageModelProviderException` with code `REKALL_LANGUAGE_MODEL_STREAM_INVALID`; cancellation remains `OperationCanceledException` and stops enumeration before tool work.
3. **Association coverage used only one tool call.** Production already associated results with the current call's ID and atomically discarded an incomplete leading tool-only tail, but the tests could not detect cross-association between same-name calls or orphaning under constrained trimming. New tests execute two `inspect` calls with distinct IDs, arguments, and results through action recovery, then inspect the next provider request. The retained context preserves each exact call/result pair; the tighter context prunes the whole association block while the execution ledger retains both distinct results. Mutation checks proved the new tests fail if the second result reuses the first ID or if trimming retains a tool-only tail, so no production association/trimming change was required.

Existing positional constructors, optional streaming selection, the Ollama non-streaming route, and engine-general authoring checkpoint enforcement are unchanged.

### RED evidence

Provider identifier safety:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ProviderExceptionUsesStableSecretFreeIdentifierFallbacksInStructuredLogs|FullyQualifiedName~ProviderExceptionNormalizesInvalidIdentifiersToStableSafeValues"
```

Result: 2 failed, 0 passed; command wall time 22.7s. The exception exposed the raw secret-bearing code/provider identifiers and did not normalize the invalid but useful values.

Malformed stream shapes (after correcting a test-only jagged-array inference compile error):

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentRejectsRuntimeNullStreamEventsAndText|FullyQualifiedName~AgentRejectsDeserializedCompletedResponsesWithNullRequiredMembers"
```

Result: 12 failed, 0 passed; command wall time 5.2s. Five cases leaked raw `NullReferenceException` (null event, null tool-call collection, null usage, null nested call, and null nested name/arguments paths); seven malformed completed responses were accepted or failed later instead of at the boundary.

The three same-name association/trimming tests passed against the original implementation, establishing that this item was a test-coverage gap rather than a production defect. Two deliberate mutation checks then proved the tests discriminate the required behavior:

- Mutating result association to always use `response.ToolCalls[0].Id` made 2/3 tests fail in 4.0s, with `call_beta` cross-associated to `call_alpha`.
- Removing the incomplete leading-tail discard made 1/2 constrained-trimming cases fail in 4.0s by retaining orphan tool-result messages without their assistant call block.

Both mutations were reverted before GREEN verification.

### GREEN evidence

Identifier safety, including preservation of valid structured facts (corrected and rerun during review fix round 2):

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ProviderExceptionUsesStableSecretFreeIdentifierFallbacksInStructuredLogs|FullyQualifiedName~ProviderExceptionNormalizesInvalidIdentifiersToStableSafeValues|FullyQualifiedName~ProviderExceptionPreservesStructuredFactsAndRedactsSuppliedSecrets|FullyQualifiedName~ProviderExceptionRedactsSuppliedSecretsFromStructuredLoggableFields"
```

Discovered tests: `ProviderExceptionPreservesStructuredFactsAndRedactsSuppliedSecrets`, `ProviderExceptionRedactsSuppliedSecretsFromStructuredLoggableFields`, `ProviderExceptionUsesStableSecretFreeIdentifierFallbacksInStructuredLogs`, and `ProviderExceptionNormalizesInvalidIdentifiersToStableSafeValues`. Fresh result: 4 passed, 0 failed, 0 skipped; test duration 28ms. The discovery command plus this test run had a combined command wall time of 7.2s. The superseded filter included two nonexistent names and actually selected only 2 tests; its exact audit is recorded below.

Malformed-stream rejection plus distinct cancellation behavior:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentRejectsRuntimeNullStreamEventsAndText|FullyQualifiedName~AgentRejectsDeserializedCompletedResponsesWithNullRequiredMembers|FullyQualifiedName~AgentCancellationStopsStreamEnumerationBeforeToolWork"
```

Result: 13 passed, 0 failed, 0 skipped; test duration 63ms; command wall time 6.9s.

Same-name association, recovery, and atomic trimming:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentAssociatesSameNameToolResultsWithExactProviderCallIds|FullyQualifiedName~AgentRetainsOrPrunesSameNameCallResultBlockAtomicallyThroughRecoveryAndTrimming"
```

Result after restoring both mutations: 3 passed, 0 failed, 0 skipped; test duration 27ms; command wall time 4.0s.

Required focused regression command:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelContractTests|FullyQualifiedName~LanguageModelAgentTests|FullyQualifiedName~ProjectAgentRunnerTests|FullyQualifiedName~ProjectAgentSessionTests|FullyQualifiedName~OllamaLanguageModelClientTests"
```

Result: 106 passed, 0 failed, 0 skipped; test duration 621ms; command wall time 4.3s; no compiler or test warnings.

Solution build:

```powershell
dotnet build Rekall.AGE.sln --no-restore
```

Result: build succeeded, 0 warnings, 0 errors; MSBuild elapsed 6.84s; command wall time 7.2s.

`git diff --check` and `git diff --cached --check` reported no whitespace errors; Git emitted only the repository's normal Windows line-ending conversion notices. No project references or installed dependencies changed.

At implementation commit `5ba97ffc354338d341be327a21eab79939e319d9`, before this report-only update, the worktree was clean. The final audit found 0 matching long-lived `dotnet`, `testhost`, or Rekall processes rooted in this worktree and 0 untracked temp/backup artifacts. No blocking or residual concerns were identified for this review round.

## Review fix round 2 — 2026-08-25

Implementation commit: `df400695e182ec0f232f9479d4f550d83b1af69f` (`fix: make provider secret checks normalization-safe`).

### Root cause and repair

The round-1 identifier policy checked supplied sensitive values only in the raw `Code`/`ProviderId` input with `StringComparison.Ordinal`, then uppercased codes and lowercased provider IDs. A case-variant credential could therefore evade the raw check and become visible in the normalized public field. Identifier sanitization could also introduce a sensitive substring through its `_`/`-` separator replacement, because the final candidate was never rechecked. Finally, message/request/value redaction retained the same case-sensitive comparison, so differently cased provider text could remain in serialized public fields.

The repair applies one culture-independent policy throughout: raw identifiers and their final normalized candidates are both checked using `StringComparison.OrdinalIgnoreCase`; any unsafe candidate uses the existing stable fallback; and all message/request/requested/resolved redaction uses ordinal-ignore-case replacement. The tests cover upper-, lower-, mixed-case, and Unicode `SÉCRET`/`sécret` pairs, serialize every public loggable text field plus `ToString()`, and separately prove that sanitization-created `a_b`/`a-b` substrings are rejected. Valid secret-free identifiers remain unchanged.

### RED evidence

Case-variant and Unicode public-field serialization:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ProviderExceptionRejectsCaseVariantSecretsAcrossAllSerializedPublicFields"
```

Result: 4 failed, 0 passed, 0 skipped; test duration 27ms; command wall time 16.3s. All four rows exposed a non-fallback code after case normalization; the serialized fields also retained differently cased sensitive text.

Sensitive substrings introduced by identifier sanitization:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ProviderExceptionRejectsSecretsCreatedByIdentifierSanitization"
```

Result: 2 failed, 0 passed, 0 skipped; test duration 32ms; command wall time 5.4s. `rekall_a b_failed` became the unsafe code `REKALL_A_B_FAILED`, and `provider a b` became the unsafe provider ID `provider-a-b`.

### GREEN evidence

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ProviderExceptionRejectsCaseVariantSecretsAcrossAllSerializedPublicFields|FullyQualifiedName~ProviderExceptionRejectsSecretsCreatedByIdentifierSanitization"
```

Result: 6 passed, 0 failed, 0 skipped; test duration 27ms; command wall time 4.2s.

### Report filter audit and correction

The exact previously recorded command was rerun verbatim:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ProviderExceptionUsesStableSecretFreeIdentifierFallbacksInStructuredLogs|FullyQualifiedName~ProviderExceptionNormalizesInvalidIdentifiersToStableSafeValues|FullyQualifiedName~ProviderExceptionExposesStructuredFactsAndRedactsSensitiveValues|FullyQualifiedName~ProviderExceptionPreservesValidStableIdentifiers"
```

Actual result: 2 passed, 0 failed, 0 skipped; test duration 30ms; command wall time 3.8s. The first two names existed; `ProviderExceptionExposesStructuredFactsAndRedactsSensitiveValues` and `ProviderExceptionPreservesValidStableIdentifiers` did not exist and selected no tests. The earlier 4-test claim was inaccurate.

The corrected filter was first run in discovery mode:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --list-tests --filter "FullyQualifiedName~ProviderExceptionUsesStableSecretFreeIdentifierFallbacksInStructuredLogs|FullyQualifiedName~ProviderExceptionNormalizesInvalidIdentifiersToStableSafeValues|FullyQualifiedName~ProviderExceptionPreservesStructuredFactsAndRedactsSuppliedSecrets|FullyQualifiedName~ProviderExceptionRedactsSuppliedSecretsFromStructuredLoggableFields"
```

It discovered exactly these four tests:

- `Rekall.Age.Tests.Agent.LanguageModelContractTests.ProviderExceptionPreservesStructuredFactsAndRedactsSuppliedSecrets`
- `Rekall.Age.Tests.Agent.LanguageModelContractTests.ProviderExceptionRedactsSuppliedSecretsFromStructuredLoggableFields`
- `Rekall.Age.Tests.Agent.LanguageModelContractTests.ProviderExceptionUsesStableSecretFreeIdentifierFallbacksInStructuredLogs`
- `Rekall.Age.Tests.Agent.LanguageModelContractTests.ProviderExceptionNormalizesInvalidIdentifiersToStableSafeValues`

The same corrected filter without `--list-tests` passed 4/4 with 0 failures and 0 skips in 28ms; discovery plus execution command wall time was 7.2s.

### Regression verification

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~LanguageModelContractTests|FullyQualifiedName~LanguageModelAgentTests|FullyQualifiedName~ProjectAgentRunnerTests|FullyQualifiedName~ProjectAgentSessionTests|FullyQualifiedName~OllamaLanguageModelClientTests"
dotnet build Rekall.AGE.sln --no-restore
```

Focused result: 112 passed, 0 failed, 0 skipped; test duration 620ms. Build result: succeeded with 0 warnings and 0 errors; MSBuild elapsed 5.15s. The combined command wall time was 9.8s. No project references or dependencies changed.

Final task audit: 0 worktree-rooted long-lived `dotnet`, `testhost`, or Rekall processes and 0 untracked temp/backup artifacts. After the report commit, all round-2 task files are committed. The only remaining worktree modification is the controller-owned SDD ledger line in `progress.md`, which the controller explicitly directed this task not to modify, stage, or commit. No blocking implementation concerns remain.
