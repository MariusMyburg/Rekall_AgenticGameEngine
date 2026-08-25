# Task 1 Report: Codex App Server Protocol and Owned Process Lifecycle

## Outcome

Implemented the typed Codex App Server JSONL boundary and exact owned-process lifecycle in `Rekall.Age.Agent`. The client starts `codex app-server --listen stdio://` with `ProcessStartInfo.ArgumentList`, performs the installed stable handshake, exposes typed account/model/thread/turn operations, and keeps notification, server-request, diagnostic, pending-request, JSONL-line, and stderr state bounded.

Task 2 supplies MCP processes through typed `RekallAgeCodexMcpServer` values. `thread/start` always emits `sandbox="workspace-write"`, replaces writable roots with the one normalized project root, and applies `sandbox_workspace_write.network_access=false` unless explicitly enabled. There is no public raw sandbox or config JSON escape hatch.

## Task-owned files

- `src/Rekall.Age.Agent/Codex/RekallAgeCodexAppServerContracts.cs`
- `src/Rekall.Age.Agent/Codex/IRekallAgeCodexProcess.cs`
- `src/Rekall.Age.Agent/Codex/RekallAgeCodexProcess.cs`
- `src/Rekall.Age.Agent/Codex/RekallAgeCodexAppServerClient.cs`
- `tests/Rekall.Age.Tests/Agent/CodexAppServerClientTests.cs`
- `tests/Rekall.Age.Tests/Agent/CodexProcessLifecycleTests.cs`
- this report

Neither SDD `progress.md` file was edited or staged.

## Protocol implementation

- Starts stdout and stderr drains before the first protocol write.
- Sends headerless JSONL messages with monotonic numeric IDs through one async writer gate.
- Implements `initialize`, `initialized`, `account/read`, paginated `model/list`, `thread/start`, `turn/start`, and `turn/interrupt` using the installed v0.130.0 stable schema shapes.
- Uses a bounded pending dictionary and matches responses strictly by numeric ID. Out-of-order responses complete only their own pending operation. Duplicate or unknown IDs become bounded stable diagnostics.
- Distinguishes responses from method-bearing notifications and method-plus-ID server requests, including when a server request reuses a numeric value that is currently pending as a client request ID.
- Surfaces bounded notification, server-request, and diagnostic reads. `turn/completed` is processed internally before notification backlog admission, so terminal state is not lost if the notification queue is full.
- Retains an immediately arriving `turn/completed` across the response/continuation race and completes the registered turn exactly once.
- If caller cancellation happens after `turn/start` is written but before its response, retains that one pending response long enough to learn the late exact turn ID and issue `turn/interrupt`; it does not orphan a server-side turn.
- Does not retain the account email returned by Codex. The typed account result contains only authentication type and authentication requirement/state booleans.

## Lifecycle and security implementation

- Resolves `REKALL_AGE_CODEX_PATH` or `codex` as a literal executable and passes `app-server`, `--listen`, and `stdio://` only through `ArgumentList`; no shell or command string is used.
- Maps missing runtime, unsupported initialization, protocol corruption, nonzero exit, failed turn, model mismatch, and cancellation to the exact `REKALL_CODEX_*` codes from the binding design.
- Rejects malformed JSON, oversized inbound or outbound JSONL lines, invalid response shapes, and premature EOF without copying provider payloads into exception text.
- Drains stderr continuously. Retained stderr history is bounded, logical-line redacted across arbitrary read chunk boundaries, and replaces oversized lines with a fixed marker. Stable exceptions never include stderr.
- Disposal is concurrency-safe and shares one completion task. It interrupts the exact active thread/turn, closes stdin, waits for the configured bounded shutdown interval, and only then calls `Kill(entireProcessTree: true)` on the exact process instance owned by the client.
- Pending requests and turn completions use `TrySet*` and dictionary removal so shutdown, cancellation, response, and failure races remain exact-once.

## TDD evidence

The fake is a real in-memory duplex process implementation: feedable stdout/stderr readers, a line-recording stdin writer, a controllable exit task, and observable close/wait/kill/dispose sequencing. Tests assert protocol output and lifecycle effects, not source text or mock existence.

Observed RED evidence before the corresponding production changes:

1. The first protocol test did not compile because `Rekall.Age.Agent.Codex` and the wished-for process/client contracts did not exist.
2. After the minimal client compiled, the exact transcript failed because the first `model/list` request emitted `"cursor":null` instead of omitting the optional cursor. Production serialization was corrected, then the transcript passed.
3. The first lifecycle suite ran 12 tests with 2 failures:
   - cancellation while `turn/start` was pending timed out waiting for the required late-ID interrupt;
   - chunk-local stderr redaction reconstructed a split synthetic account identifier.
   The client was changed to retain/cancel late turn starts safely and to redact bounded logical stderr lines; both focused tests then passed.
4. A dedicated back-to-back response/notification test failed with `REKALL_CODEX_CANCELLED` because `turn/completed` arrived before the async response continuation registered the turn. A bounded early-completion handoff fixed the race, and the test passed.
5. An outbound-line test failed with `REKALL_CODEX_CANCELLED` after writing an oversized task, and an initialize-corruption test was incorrectly classified as `REKALL_CODEX_PROTOCOL_UNSUPPORTED`. The single writer now rejects oversized serialized lines before stdin changes, and only incompatible initialize response shapes/JSON-RPC rejection map to `PROTOCOL_UNSUPPORTED`; malformed transport data remains `PROTOCOL_INVALID`.

The notification-backlog proof initially had a test-observation race: consuming the first entry could free capacity before the second was processed. The test was corrected with a later unknown-ID response as an ordered reader barrier; no production change was made for that test issue.

## Verification evidence

Focused gate:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~CodexAppServerClientTests|FullyQualifiedName~CodexProcessLifecycleTests"
```

Initial implementation result: 19 passed, 0 failed, 0 skipped.

Solution compatibility gate:

```powershell
dotnet build Rekall.AGE.sln --no-restore
```

Result before final commit: build succeeded with 0 warnings and 0 errors.

The installed v0.130.0 JSON schemas were generated only under this task's ignored SDD directory for contract inspection and removed afterward. No generated schema is committed. No real App Server process was launched by these focused fake-process tests; the authenticated runtime smoke and runner/MCP gauntlet remain integration acceptance work outside Task 1.

## Self-review and residue

- Reviewed the task-owned diff against base `89884cd596580a3927609af3d19e813b8e90dec3` for request routing, cancellation/disposal races, payload bounds, and exact stable codes.
- Scanned task-owned source, tests, and this report for credential/account leakage. Only synthetic redaction fixtures and protocol field names occur; no token, real account identity, or raw provider stderr is present.
- Confirmed the generated schema directory was removed and the tests created no operating-system child process or temporary directory.

## Residual scope

Task 1 intentionally does not implement the Task 2 project runner, MCP child configuration construction, Studio provider UI, or the real authenticated authoring gauntlet.

## Fix round 1: load-bearing protocol boundary

Review identified four issues that had to be resolved before the runner could safely consume this client. The fix round made these changes:

- Replaced caller-controlled `Sandbox` and raw `Config` with typed MCP server entries. The outgoing config is reconstructed from scratch, hard-codes workspace-write sandboxing, retains only the normalized project root as writable, and keeps network disabled by default. The completed returned-working-directory check also normalizes and compares `cwd`, rejecting a mismatch with `REKALL_CODEX_PROTOCOL_INVALID` without echoing the returned path.
- Added result and error response APIs for queued server requests. Both string and numeric JSON-RPC IDs preserve their original JSON type and pass through the existing single writer. If the bounded server-request queue overflows, the client sends a fixed `-32000` error for the unqueued request and begins terminal cleanup of only its owned process instead of silently dropping an actionable approval.
- Added one lifecycle gate around terminal transition, request admission/removal, and turn registration/completion. Terminal transition snapshots and removes admitted work while holding that same gate, so no request or turn can be added behind the terminal snapshot and wait forever.
- Sanitizes notification and server-request parameters before bounded queue admission. Recursive field-name redaction covers account identity and common credential containers, while string-value sanitization removes bearer tokens, API-key shapes, email addresses, and user-profile paths. Tests prove synthetic email/token/authorization/api-key/secret material is absent from retained payloads.

Focused RED observations for this fix round:

1. The authority transcript did not compile because the safe typed MCP contract did not exist. The typed authority construction was added together with a behavioral returned-`cwd` mismatch test and normalized comparison; the focused gate then passed both boundaries.
2. The server-response transcript did not compile because no result/error APIs existed. Overflow and retained-payload tests then drove the fixed denial/shutdown and redaction behavior.
3. A response immediately followed by terminal protocol corruption returned a turn that could be registered outside the terminal snapshot. The shared lifecycle gate now guarantees either terminal start failure or a registered completion that is terminally completed, never an orphan.

Final fix-round verification is recorded below after running from the final task-owned working tree.

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~CodexAppServerClientTests|FullyQualifiedName~CodexProcessLifecycleTests"
```

Result: 24 passed, 0 failed, 0 skipped.

```powershell
dotnet build Rekall.AGE.sln --no-restore
```

Result: build succeeded with 0 warnings and 0 errors.

Per the end-to-end priority override, late turn-start response deadlines, stalled-stdin write deadlines, and eviction of completed turns that callers never consume are deferred until after the first functional Codex integration. They were not started in this fix round.
