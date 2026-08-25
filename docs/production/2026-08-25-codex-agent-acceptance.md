# Codex App Server Agent Acceptance — 2026-08-25

## Outcome

The Codex App Server provider is accepted through both deterministic tests and a real authenticated end-to-end AGE authoring run. The AGE CLI and Studio expose Codex as a provider, report only safe authentication/model facts, and route approval requests through explicit policies. The real run used ChatGPT authentication and `gpt-5.6-sol`; no account identity, credential, or token value was read or emitted.

On Windows, the installed npm shim is resolved without a shell to the structured launch `node.exe <codex.js> app-server --listen stdio://`. The accepted protocol is the JSONL App Server protocol shipped with Codex CLI `0.149.1`; its `initialize` response does not advertise a separate numeric protocol version. The acceptance machine required `0.149.1` because the previously installed `0.130.0` model catalog did not expose `gpt-5.6-sol`.

## Authenticated protocol smoke

The opt-in acceptance test was run with:

```powershell
$env:REKALL_RUN_CODEX_ACCEPTANCE = '1'
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~CodexAgentAcceptanceTests
```

It passed 1/1 in approximately 6 seconds. The test verified safe ChatGPT authentication state, confirmed `gpt-5.6-sol` in `model/list`, created an ephemeral read-only thread, completed a bounded real turn, disposed the App Server client before deleting the test root, and emitted no identity or credential material.

## Real Sol-authored AGE game

The real project-agent run created **Prism Relay**, a deliberately compact non-Aetherfall game, solely through AGE MCP tools. The run completed in 286.17 seconds with 26 tool calls, 2,406,226 input tokens, and 9,707 output tokens.

The authored result contains 22 entities and 23 renderables, a semantic `move.horizontal` action in `Rekall.InputActionMap`, a delta-time-driven `PrismRelaySystem`, and attached agent-owned state `Game.Modules.PrismRelayRuntime.PrismRelayState`. The agent used the ordinary closed-loop gauntlet, repaired a missing generic UI canvas and missing `world`/`rendering3d` project capabilities, then repeated the strict gameplay proof after the latest mutation.

Independent controller verification built both module projects and inspected six 0.1-second semantic input frames. All three strict assertions passed:

- `delta.position3d.x > 0`: observed `4.2`.
- `delta.component.property DistanceTravelled > 0`: observed `4.2`.
- `changed.component.property ElapsedSeconds == true`: observed `true`.

The package audit reported ready, 43 files, zero missing artifacts, player exit code 0, a captured nonblank frame, valid layout, and 100 informative colors. The final capture contained 28,537 non-background pixels.

## Retained acceptance artifacts

The bounded evidence root is retained for inspection at `C:\Users\Marius\AppData\Local\Temp\RekallAgeCodexAcceptance-20260825`. At final review it contained 108 files totaling 9,059,837 bytes.

| Artifact | SHA-256 |
|---|---|
| `Build\PrismRelay.zip` | `AD4E082CB044FDB67AC89DF2D6EACD6A0412CEAB9DDDFDA5EBB5C997F2F81C54` |
| `Builds\AgentAuthoringGauntletAudit\package_play_frame_001.png` | `2C6B0E7A0BE6F6D8D20D4F9AC5F73B105566917D9E2DB4F2ED9B019455947C32` |
| `Builds\IndependentAudit\package_play_frame_001.png` | `2C6B0E7A0BE6F6D8D20D4F9AC5F73B105566917D9E2DB4F2ED9B019455947C32` |

## Ordinary-workflow fixes exposed by the real run

- Resolved Windows npm shims to a structured Node launch after the shim itself failed as a direct process executable.
- Replaced JSON-array redaction enumeration with indexed traversal after a normal command array triggered `InvalidOperationException` while replacing a value.
- Preserved safe exception type in protocol failure diagnostics.
- Kept the default unattended behavior deny-by-default, while making the explicitly selected CLI `--approval-policy never` mode satisfy App Server approval callbacks without prompting.
- Serialized MCP elicitation responses as `{ "action": "accept", "content": {} }`; command/file approvals retain their distinct `{ "decision": "accept" }` contract.
- Added read-only ephemeral thread support for the smallest authenticated smoke.

No genre-specific engine behavior or deferred pathological Task 1/2 hardening was added.

## Final verification

| Gate | Result |
|---|---|
| Focused Codex/CLI/catalog tests | 60 passed, 0 failed |
| Authenticated Codex smoke | 1 passed, 0 failed |
| Studio regression that exposed an ambiguous tab selector | 1 passed, 0 failed |
| Full Studio suite | 83 passed, 0 failed; 47 s |
| Full engine suite | 2,064 passed, 0 failed; 4 m 8 s |
| Solution build | succeeded; 0 warnings, 0 errors; 3.72 s |

The first full Studio run exposed a test-only ambiguous selector: two tab controls legitimately contained two items. The selector now identifies the actual `Mesh Edit` tab. Its isolated rerun and the complete Studio suite both passed. The Studio process already open for the user was not closed.

Final process review found no task-owned Codex CLI, AGE CLI, player, `dotnet`, or `testhost` process. The remaining `codex.exe` and two CUA Node processes belong to the running Codex desktop application.

## Review fix round 1

Studio approval prompts now render a maximum 1,200-character allowlisted summary of the sanitized request. Supported requests show command, working directory, changed paths, reason, and MCP server/tool/message facts when supplied. Credential-like and unknown fields are never rendered; unknown methods or supported methods without an informative allowlisted fact are declined without prompting.

Fresh Codex users can now select **Sign in to Codex** in Studio or run `rekall agent auth codex login`. AGE uses the documented App Server `account/login/start` browser flow, waits for `account/login/completed`, refreshes safe account/model state, and sends `account/login/cancel` when the user cancels after launch. Studio exposes a separate cancellation action. No Codex auth file is read, the transient authentication URL is handed directly to the OS browser rather than retained in status/evidence, and failures expose stable `REKALL_CODEX_LOGIN_FAILED` or `REKALL_CODEX_CANCELLED` facts without server identity/token detail.

Review-focused tests passed 15/15 engine and 4/4 Studio. Fresh full verification passed Studio 86/86 in 48 seconds, engine 2,068/2,068 in 4 minutes 33 seconds, and the solution build with zero warnings/errors in 4.74 seconds. An engine run also exposed that a fixed 25 ms scheduler delay could lose queued tool evidence under load; the runner now waits for the FIFO terminal notification, proving all preceding tool/message/usage notifications were projected before cancellation. The exact regressions passed 2/2. One unrelated module-host scheduler-jitter test timed out during an intermediate loaded run, passed its immediate isolated rerun in 3 seconds, and passed again in the final complete 2,068-test run.

Fix round 2 closes the fresh-account Studio path: authentication-required model discovery retains the exact Codex lease/runner so **Sign in to Codex** is enabled, successful login refreshes Sol models and enables authoring, and provider switching cancels/awaits login before disposal. Approval prompts admit only exact ordinal matches for the three supported App Server methods; prefix/suffix confusables fail closed. Focused verification passed Studio 14/14, engine 24/24, and a zero-warning solution build.
