# SDD ledger — plan: docs/superpowers/plans/2026-08-25-codex-app-server-provider.md

## Baseline

- Branch: `codex/high-fidelity-forward-plus`
- Start commit: `89884cd596580a3927609af3d19e813b8e90dec3`
- Spec: `docs/superpowers/specs/2026-08-25-openai-codex-agent-backends-design.md`.
- OpenAI Responses plan is complete, independently reviewed, and freshly verified at Studio 81/81, engine 2020/2020, solution build 0 warnings/errors.
- Parent rendering plan remains paused after reviewed Task 8 and resumes at Aetherfall Task 9 after this plan.
- Local runtime preflight: `codex-cli 0.130.0`; authenticated via ChatGPT; App Server help confirms default `stdio://` transport and structured `--listen` option.

## Pre-flight consistency scan

| Tasks | Producer / consumer or shared surface | Finding |
|---|---|---|
| 1 | Typed protocol/process contracts, fake duplex tests, lifecycle bounds, and exact stable errors agree | Clean |
| 2 | Codex runner/MCP configuration, approval routing, cancellation, and workflow tests agree | Clean |
| 3 | CLI/Studio provider surfaces, authenticated smoke, real game gauntlet, evidence, and full verification agree | Clean |
| 1 → 2 | Task 1 produces initialized account/model/thread/turn operations and bounded notifications consumed by the project runner | Clean |
| 1 → 3 | Task 3 auth/model/status surfaces consume Task 1 account/model operations through the Task 2 runner/catalog boundary | Clean |
| 2 → 3 | Task 2 produces the provider-catalog runner, AGE MCP bridge, approval, progress, and cancellation contracts consumed by CLI/Studio and acceptance | Clean |
| 2 + OpenAI plan | Task 2 extends the already async, ownership-safe provider lease/catalog boundary without recreating provider status or lifecycle logic | Clean |
| 3 + parent rendering plan | The real Codex game is explicitly small and non-Aetherfall, so it validates generic authoring without displacing the later flagship visual milestone | Clean |

Pre-flight result: no contradictions. The spec is binding. Official OpenAI documentation confirms JSONL over default stdio, initialize/initialized sequencing, thread/start/turn/start/turn/completed, and Codex-owned account modes.

Task 1: fix round 1/5 in progress — close sandbox-authority, approval-response, terminal-admission, late-turn, stalled-writer, completed-turn-bound, auth-payload-redaction, and returned-cwd findings from review head `fd856a8`.
Task 1: Ruling: user priority override narrows fix round 1 to sandbox/project-root authority, approval-response functionality, basic terminal admission safety, and auth/account redaction. Defer late-turn-no-response deadlines, stalled-stdin simulation, unconsumed completion-cache growth, and returned-cwd mismatch until after end-to-end Codex functionality — this accelerates playable delivery; cost if wrong: rare malformed/unresponsive App Server behavior may still require forceful cleanup or later hardening.
Task 1: fix round 1/5 (4 retained findings addressed, 1 open — overbroad credential-field redaction damaged usage telemetry; commits fd856a8..2982963; returned-cwd validation also completed before scope correction)
Task 1: fix round 2/5 (1 addressed, 0 open — exact/contextual redaction preserves usage and safe auth mode/type; commits 2982963..d877076)
Task 1: complete (commits 89884cd..d877076, review clean; controller focused 25/25; build 0 warnings/errors; deferred lifecycle hardening ledgered)
Task 2: minor (deferred) — aggregate result/tool collection bounds and dead-client retry reset after process/protocol failure; neither blocks the first functional real gauntlet.
Task 2: fix round 1/5 in progress — serialize explicit `ephemeral: true` for every Codex project thread and prove exact transcript.
Task 2: fix round 1/5 (1 addressed, 0 open — every project thread now explicitly ephemeral; commits 6355fe0..7ab99d5)
Task 2: complete (commits d877076..7ab99d5, review clean; controller focused 21/21; build 0 warnings/errors; two non-blocking hardening minors deferred)
Task 3: complete (implementation ab73565; authenticated ChatGPT/Sol smoke 1/1; real Prism Relay AGE MCP gauntlet and strict independent gameplay proof passed; Studio 83/83; engine 2064/2064; build 0 warnings/errors; no residual blocker)
