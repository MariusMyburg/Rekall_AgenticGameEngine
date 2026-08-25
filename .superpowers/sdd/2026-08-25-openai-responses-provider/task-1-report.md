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
