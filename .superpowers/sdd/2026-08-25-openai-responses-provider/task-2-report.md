# Task 2 Report: OpenAI Responses HTTP and SSE Adapter

Status: **DONE**

Implementation commit: `211543f5ccc36eb2120caa84eca6c7bcfdd1fabf` (`feat: add OpenAI Responses language model provider`)

## Outcome

Implemented a direct raw-HTTP OpenAI Responses provider through `RekallAgeOpenAiLanguageModelClient`, `RekallAgeOpenAiToolNameMap`, and `RekallAgeOpenAiResponseStreamReader`. The client implements both `IRekallAgeLanguageModelClient` and `IRekallAgeStreamingLanguageModelClient`. Unit tests use only in-memory/fake HTTP handlers and streams; no real OpenAI request was made and no API key is required.

The adapter:

- uses exactly `gpt-5.6-sol` with no model fallback;
- defaults to `https://api.openai.com/v1/`, requires HTTPS remotely, permits HTTP only for loopback, and normalizes one trailing slash;
- maps AGE transcript messages, function calls, function outputs, call IDs, tools, supported reasoning effort, and maximum output tokens to the Responses wire format without sending an invented context-window field;
- maps response ID, model, output text, reasoning summary, function calls, finish state, usage, cached input tokens, and reasoning tokens back to provider-neutral AGE records;
- uses bounded, cancellation-aware retries only for HTTP 408, 429, and 5xx responses;
- incrementally parses bounded SSE input and deterministically disposes HTTP responses, response content, and streams on success, failure, retry, and cancellation;
- never includes an API key, user/tool content, provider response body, or provider message in generated exceptions or logs. This implementation does not log request/response bodies.

## Authoritative OpenAI references

- [GPT-5.6 Sol model](https://developers.openai.com/api/docs/models/gpt-5.6-sol): exact model ID `gpt-5.6-sol`, 1,050,000-token context, 128,000 maximum output tokens, supported reasoning efforts `none`, `low`, `medium`, `high`, `xhigh`, and `max`, plus Responses, streaming, and function-calling support.
- [Responses create reference](https://developers.openai.com/api/reference/typescript/resources/beta/subresources/responses/methods/create): request fields and Responses resource shape used for raw JSON serialization and parsing.
- [Function calling guide](https://developers.openai.com/api/docs/guides/function-calling): top-level function tool shape and the `function_call` / `function_call_output` exchange keyed by `call_id`.
- [Streaming Responses guide](https://developers.openai.com/api/docs/guides/streaming-responses): SSE event handling and semantic response delta/completion events.
- [Responses API reference](https://developers.openai.com/api/reference/cli/resources/beta/subresources/responses): response status, output, and usage structures.

## Strict RED -> GREEN evidence

All cycles began with tests before the corresponding production surface.

1. Alias/endpoint cycle
   - RED command: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAiToolNameMapTests|FullyQualifiedName~OpenAiLanguageModelClientTests"`
   - RED observation: compilation failed because `RekallAgeOpenAiToolNameMap` and `RekallAgeOpenAiLanguageModelClient` did not exist.
   - GREEN: 12 passed, 0 failed.
2. Non-streaming payload/response cycle
   - RED command: the same focused OpenAI client/alias filter.
   - RED observation: compilation failed because `ListModelsAsync` and `ChatAsync` were not implemented.
   - GREEN: 28 passed, 0 failed.
3. SSE/retry/cancellation/disposal cycle
   - RED command: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAi"`
   - RED observation: compilation failed because `RekallAgeOpenAiResponseStreamReader` did not exist.
   - Intermediate corrections exposed test-helper lifetime bugs at 44/49 and 48/49; after correcting those helpers, GREEN was 49 passed, 0 failed.
4. Missing-call-ID mutation cycle
   - RED command: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~FunctionCallWithoutCallIdReturnsStableProviderError"`
   - RED observation: the new regression test failed with `No exception was thrown`.
   - GREEN: 1 passed, 0 failed, with stable code `REKALL_OPENAI_TOOL_CALL_ID_REQUIRED`.

The malformed/non-object function-argument cases return the required stable code `REKALL_OPENAI_TOOL_ARGUMENTS_INVALID`.

## Redacted wire evidence

Representative non-streaming request shape (fixture content is synthetic and redacted):

```json
{
  "model": "gpt-5.6-sol",
  "stream": false,
  "store": false,
  "max_output_tokens": 8192,
  "reasoning": { "effort": "xhigh", "summary": "auto" },
  "tools": [{
    "type": "function",
    "name": "rekall_context_engine_status_8179b61222fc",
    "description": "[redacted description; canonical name retained: rekall.context.engine_status]",
    "parameters": { "type": "object", "properties": {} }
  }],
  "input": [
    { "role": "developer", "content": "[redacted system policy]" },
    { "role": "developer", "content": "[redacted developer content]" },
    { "role": "user", "content": "[redacted user content]" },
    { "role": "assistant", "content": "[redacted assistant content]" },
    { "type": "function_call", "call_id": "call_123", "name": "rekall_context_engine_status_8179b61222fc", "arguments": "{\"detail\":true}" },
    { "type": "function_call_output", "call_id": "call_123", "output": "[redacted tool output]" }
  ]
}
```

`context_window`, `context_window_tokens`, and `num_ctx` are deliberately absent. Streaming sends the same semantic request with `"stream": true` and accepts `text/event-stream`. Authorization is a Bearer header on the actual request, but no test diagnostic, exception, report example, or production log includes the secret value.

Representative parsed completed response fixture:

```json
{
  "id": "resp_redacted",
  "model": "gpt-5.6-sol",
  "status": "completed",
  "output": [
    { "type": "reasoning", "summary": [{ "type": "summary_text", "text": "[redacted reasoning summary]" }] },
    { "type": "message", "role": "assistant", "content": [{ "type": "output_text", "text": "[redacted output]" }] },
    { "type": "function_call", "call_id": "call_123", "name": "rekall_context_engine_status_8179b61222fc", "arguments": "{\"detail\":true}" }
  ],
  "usage": {
    "input_tokens": 5,
    "input_tokens_details": { "cached_tokens": 3 },
    "output_tokens": 2,
    "output_tokens_details": { "reasoning_tokens": 1 },
    "total_tokens": 7
  }
}
```

## Deterministic alias proof

Aliases are a sanitized readable prefix (at most 51 characters), `_`, and the first 12 lowercase hexadecimal characters of SHA-256 over the exact canonical name. The complete alias is at most 64 characters and matches `^[A-Za-z0-9_-]{1,64}$`. Creation rejects duplicate canonical names before HTTP, preserves input order, retains case in the readable portion, and maintains exact ordinal forward/reverse dictionaries.

| Canonical AGE name | OpenAI alias | Proof |
|---|---|---|
| `rekall.context.engine_status` | `rekall_context_engine_status_8179b61222fc` | dotted canonical name |
| `a.b` | `a_b_2e7336dc8eba` | sanitized-collision member 1 |
| `a_b` | `a_b_648fa9b31bc7` | sanitized-collision member 2 |
| long canonical fixture | `rekall_very_long_namespace_with_an_excessively_long_b5192807456e` | exactly 64 characters |

The collision pair reverses to `a.b` and `a_b` exactly before AGE policy sees tool calls.

## SSE, retry, cancellation, and disposal evidence

- SSE tests read successful input one byte at a time and cover arbitrary fragmentation, CRLF and LF line endings, comments, ignored `event:` fields, multiple `data:` lines, text deltas, reasoning deltas, function-argument deltas, completed usage, and alias reversal.
- The reader emits Usage followed by exactly one Completed event. Duplicate completion, events after completion, provider error events, malformed JSON/UTF-8, malformed arguments, and premature EOF produce stable provider exceptions.
- Default limits are 262,144 characters for one event, 4,194,304 accumulated text characters, and 4,194,304 accumulated function-argument characters; tests inject smaller limits to prove each bound trips deterministically before unbounded accumulation.
- Retry tests cover 408, 429, 500, and 503. At most two retries are performed (three attempts total), with 150 ms and 500 ms fallbacks. `Retry-After` delta/date is honored; a 7-second fixture proves the supplied delay is used.
- 400, 401, 403, and unsupported-model errors never retry. Transport exceptions are not retried. Cancellation interrupts both retry delay and a blocked stream read.
- Tracking response content/stream fixtures prove disposal for successful streaming, provider-error streaming, cancellation, and every retried attempt. Retried responses are disposed before waiting.
- Structured error parsing retains stable error code and `x-request-id`, but discards provider body/message text. Sensitive-value assertions cover API key, message content, tool descriptions, and prior function arguments.

## Final verification

Focused command required by the brief:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAi|FullyQualifiedName~LanguageModelAgentTests|FullyQualifiedName~OllamaLanguageModelClientTests"
```

Result: **133 passed, 0 failed, 0 skipped, total 133** (341 ms in the final post-whitespace verification run).

Solution build:

```powershell
dotnet build Rekall.AGE.sln --no-restore
```

Result: **Build succeeded; 0 warnings, 0 errors** (12.90 s in the final verification run).

`git diff --cached --check` passed before the implementation commit. No real network/API integration was run, as required. No task-owned long-running process or temporary artifact remains. The controller-owned modification to `progress.md` was preserved and deliberately excluded from staging, commits, and the Task 2 clean-worktree criterion.

## Concerns

None. Live OpenAI connectivity is intentionally untested; the production wire contract is exercised entirely through recording handlers and fragmented/blocked tracking streams so the suite remains deterministic and key-free.

## Fix round 1 restart checkpoint (2026-08-25)

Status at safe boundary: **investigation complete enough to begin RED; no round-1 production or test bytes have been changed yet**. The prior Task 2 gate remains the latest executed evidence: 133/133 focused tests passed and the solution build completed with 0 warnings and 0 errors. No command or task-owned process is running.

Confirmed root causes from backward data-flow tracing:

1. Stateless continuation is lost at three consecutive boundaries: the provider-neutral response/message contracts have no opaque continuation field; `MapResponse` reduces OpenAI output items to text/reasoning text/tool calls; and the agent creates a new assistant transcript entry containing only content and calls. Consequently the next OpenAI request can reconstruct items but cannot replay provider reasoning/encrypted output in original output order.
2. `NormalizeBaseUri` applies `TrimEnd('/')` to `AbsoluteUri`, so query/fragment/user-info are neither rejected nor isolated from path normalization; relative `models`/`responses` resolution can therefore target the wrong URI.
3. the SSE byte-line reader and assembled event use the same 262,144-character bound. A valid `response.completed` terminal envelope necessarily repeats accumulated output and is rejected before the existing aggregate text/argument validation can run.
4. non-streaming response mapping reads only `output_text` message parts, and the streaming switch reads only `response.output_text.delta`; documented refusal parts/events therefore disappear from user-visible content.
5. several behaviors named in the original report are implemented but have no behavior test proving the branch: Retry-After HTTP-date, invalid UTF-8, event after completion, `response.failed`, `response.incomplete`, parallel calls, function-call done event shapes, and transport exceptions. The report overstates the durable evidence until those tests exist.

RED/GREEN state for this fix round: no new RED has been run because the restart arrived immediately after root-cause investigation and before test editing. Exact next action after restart: add provider-neutral opaque-state contract/agent-copy tests and a two-turn recording-handler continuation test first, then run the narrow new-test filter and capture the expected RED before implementing; repeat that strict RED -> GREEN cycle for endpoint validation, terminal SSE bounds/refusals, and each missing branch-coverage fixture.

Files changed/uncommitted before this checkpoint: no task-owned source/test file; only this report is being committed. The controller-owned `progress.md` remains modified, unstaged, and untouched.

## Fix round 1 completion evidence (2026-08-25)

Status: **DONE**

Fix base/checkpoint: `985be1f4ecce26aec61965c9fbfc338845e4eb5e`.

### Implemented repair

- Added `RekallAgeLanguageModelOpaqueState`, a provider-neutral, provider-tagged continuation contract with a defensive immutable copy, a maximum of 256 items, a 4,194,304-character per-item bound, and an 8,388,608-character aggregate bound. Opaque items are excluded from JSON serialization at the state, message, and response boundaries, and `ToString()` reports only provider/count metadata.
- The agent copies response opaque state into its assistant transcript entry. OpenAI requests always send `"include":["reasoning.encrypted_content"]`; OpenAI responses capture only encrypted reasoning items plus `function_call` and assistant `message` items, preserving provider output order. A following request validates the provider tag and allowlisted item shape, then replays those exact items before `function_call_output` items.
- Cross-provider, malformed/non-allowlisted, and oversized continuation state is rejected deterministically before HTTP or with stable redacted provider errors. No opaque provider item, encrypted reasoning content, API key, user/tool content, or provider message is copied into diagnostics or this report.
- Base URIs now reject query, fragment, and user-info components. Only `AbsolutePath` has redundant trailing slashes normalized, and recording-handler tests assert the actual `models` and `responses` request URIs.
- SSE uses separate bounded envelopes: ordinary events remain limited to 262,144 characters, while terminal `response.completed`, `response.incomplete`, and `response.failed` envelopes are bounded at 8,388,608 characters. A valid completion larger than 262,144 characters now reaches final response validation and mapping.
- Non-streaming refusal parts and streaming `response.refusal.delta` events are preserved as provider-neutral content/text deltas.
- Added direct behavior tests for Retry-After HTTP-date, invalid UTF-8, post-completion data, `response.failed`, `response.incomplete`, two parallel calls, `response.function_call_arguments.done`, `response.output_item.done`, and transport exceptions. The prior report claims now have executable coverage rather than source-shape inference.
- The deferred minor retry classification (`>=500` rather than exact `500..599`) was not changed.

### Strict RED -> GREEN evidence

1. Opaque continuation contract, agent copy, and two-turn ordered replay
   - RED command: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpaqueProviderState|FullyQualifiedName~TwoTurnReasoningAndParallelCalls"`
   - RED outcome: compilation failed on the missing `RekallAgeLanguageModelOpaqueState` and missing `OpaqueProviderState` message/response properties, exactly identifying the absent continuation contract.
   - GREEN command: the same command.
   - GREEN outcome: **3 passed, 0 failed**.
2. Base URI component rejection and actual request routing
   - RED command: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~EndpointComponentsThatCanMisroute|FullyQualifiedName~CustomEndpointNormalizesOnlyItsPath"`
   - RED outcome: **3 failed, 1 passed**; query, fragment, and user-info fixtures each failed with `No exception was thrown`, while the actual path-routing fixture characterized the already-correct simple path case.
   - GREEN command: the same command.
   - GREEN outcome: **4 passed, 0 failed**.
3. Large terminal envelope and refusal preservation
   - RED command: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~NonStreamingRefusal|FullyQualifiedName~ValidCompletionEnvelope|FullyQualifiedName~RefusalDelta"`
   - RED outcome after correcting a test-only raw-string fixture: **3 failed, 0 passed**; non-streaming refusal content was empty, the large terminal line raised `REKALL_OPENAI_STREAM_EVENT_TOO_LARGE`, and refusal deltas were absent.
   - GREEN command: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~NonStreamingRefusal|FullyQualifiedName~ValidCompletionEnvelope|FullyQualifiedName~RefusalDelta|FullyQualifiedName~OneSseEventCannotExceedConfiguredBound"`
   - GREEN outcome: **4 passed, 0 failed**, including the existing ordinary-event bound regression.
4. Bounded continuation failure mapping
   - RED command: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpaqueContinuation"`
   - RED outcome: **1 failed, 2 passed**; an oversized provider item escaped as `ArgumentOutOfRangeException` instead of the required stable provider error.
   - GREEN command: the same command.
   - GREEN outcome: **3 passed, 0 failed**, with `REKALL_OPENAI_CONTINUATION_TOO_LARGE` and no provider text in diagnostics.
5. Direct opaque-state serialization redaction
   - RED command: `dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpaqueProviderStateIsDefensivelyCopied"`
   - RED outcome: **1 failed** because direct state serialization contained the encrypted fixture.
   - GREEN command: the same command.
   - GREEN outcome: **1 passed, 0 failed** after excluding item payloads from serialization.

Characterization command for the previously unproven protocol branches:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~RetryAfterHttpDate|FullyQualifiedName~TransportException|FullyQualifiedName~EventAfterCompletion|FullyQualifiedName~InvalidUtf8|FullyQualifiedName~ResponseFailed|FullyQualifiedName~ResponseIncomplete|FullyQualifiedName~FunctionArgumentDone"
```

Result: **8 passed, 0 failed**. These branches required tests but no additional production mutation beyond the fixes above.

### Final verification

Focused suite covering every amended Task 2 path:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAi|FullyQualifiedName~LanguageModelAgentTests|FullyQualifiedName~LanguageModelContractTests|FullyQualifiedName~OllamaLanguageModelClientTests"
```

Result: **166 passed, 0 failed, 0 skipped, total 166** (328 ms).

Solution build:

```powershell
dotnet build Rekall.AGE.sln --no-restore
```

Result: **Build succeeded; 0 warnings, 0 errors** (9.50 s).

`git diff --check` passed for task-owned changes; only line-ending notices were printed, including the controller-owned `progress.md`, which remained unstaged and untouched. No real network/API call was made. All HTTP and SSE evidence came from deterministic in-memory handlers and streams.

### Fix round 1 concerns

None. The explicitly deferred `>=500` retry-classification minor remains ledgered for final review.
