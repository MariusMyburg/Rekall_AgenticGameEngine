# SDD ledger — plan: docs/superpowers/plans/2026-08-25-openai-responses-provider.md

## Baseline

- Branch: `codex/high-fidelity-forward-plus`
- Start commit: `5bd6644fbf9a0d2802c1ab388ee6f78ecb82edb8`
- Parent rendering plan is paused after reviewed Task 8 and resumes at Aetherfall Task 9 after this plan and the Codex App Server plan complete.
- Latest verified gates: full core 1874/1874 after Task 7; Studio 69/69 and solution build 0 warnings/errors after Task 8.
- Spec: `docs/superpowers/specs/2026-08-25-openai-codex-agent-backends-design.md`.

## Pre-flight consistency scan

| Tasks | Producer / consumer or shared surface | Finding |
|---|---|---|
| 1 | Tool/response IDs, streaming, structured provider errors, common runner, and their tests agree | Clean |
| 2 | OpenAI alias map, HTTPS/Responses/SSE adapter, error mapping, and fake-HTTP tests agree | Clean |
| 3 | Shared catalog/owned lease, preserved Ollama routes, OpenAI CLI routes, and behavior tests agree | Clean |
| 4 | Studio provider lifecycle, deterministic fake acceptance, optional real API gate, evidence, and full verification agree | Clean |
| 1 → 2 | Task 1 produces transcript identity and stream contracts consumed by the OpenAI adapter | Clean |
| 1 → 3 | Task 1 produces `IRekallAgeProjectAgentRunner` consumed by the shared provider factory/CLI | Clean |
| 2 → 3 | Task 2 produces `RekallAgeOpenAiLanguageModelClient` constructed by Task 3 | Clean |
| 1 + 3 → 4 | Task 4 consumes the common runner, provider catalog, and owned lease without recreating provider logic | Clean |
| 2 → 4 | Task 4 acceptance exercises canonical tool alias reversal and OpenAI response mapping through the ordinary project-agent loop | Clean |

Pre-flight result: no conflicts. The spec is binding. The later Codex plan deliberately consumes this plan's runner/catalog boundary rather than duplicating it.

Task 1: fix round 1/5 (2 addressed, 2 open — case-normalization secret leak; inaccurate report filter names/count; commits bf6ad27..e28e3a2)
Task 1: fix round 2/5 (1 addressed, 1 open — historical report still reproduces nonexistent test filters; commits e28e3a2..9705642)
Task 1: fix round 3/5 (1 addressed, 0 open — stale nonexistent filters removed from durable report; commits 9705642..efea43f)
Task 1: complete (commits 5bd6644..efea43f, review clean; focused 112/112; build 0 warnings/errors)
Task 2: minor (deferred): transient retry classification uses status >=500 rather than the exact 500..599 range; final branch review must triage synthetic 600 behavior.
Task 2: fix round 1/5 IN PROGRESS at restart request (review head `56b0cffa89daa9b1c437c413743827097d7f9241`; original implementer `/root/openai_task2_adapter`). Open Critical/Important findings: preserve and replay bounded provider-tagged opaque Responses reasoning/output items in exact order across stateless function-call turns with `include: ["reasoning.encrypted_content"]`; reject query/fragment/user-info custom endpoints and normalize only paths; allow separately bounded large terminal SSE envelopes; preserve refusal content/deltas; add or correct durable evidence for Retry-After HTTP-date, invalid UTF-8, post-completion events, failed/incomplete events, parallel calls, done shapes, and transport exceptions. Resume from `task-2-report.md`; if the original agent is unavailable after restart, dispatch a fresh fix implementer with this ledger line, the Task 2 brief, report, and review package `review-efea43f..56b0cff.diff`.
Task 2: fix round 1/5 (5 addressed, 0 open; commits 985be1f..ff6a897; focused 166/166; build 0 warnings/errors)
Task 2: complete (commits efea43f..ff6a897, review clean)
Task 3: review scope resolution — real API/no-credential acceptance evidence and actual Studio consumption are explicitly owned by Task 4, so the reviewer’s cross-task unverifiable items remain downstream gates rather than Task 3 gaps.
Task 3: fix round 1/5 (4 addressed, 1 open — unconditional cancellation classification introduced new Important breakage; commits 9fa8d93..4504620)
Task 3: fix round 2/5 (1 addressed, 0 open — agent cancellation now requires requested caller cancellation and agent-command scope; commits 4504620..c5bb5d1)
Task 3: complete (commits ff6a897..c5bb5d1, review clean; focused 27/27; build 0 warnings/errors)
Task 4: fix round 1/5 (2 addressed, 2 open — cancellation before cleanup protection; switch-after-fault retained stale runner; commits 05f6082..b12c475)
Task 4: fix round 2/5 (2 addressed, 0 open — unconditional redacted cleanup and deterministic switch-after-fault; commits b12c475..bf547a7)
Task 4: evidence resolution — controller confirmed `OPENAI_AUTH_STATE=absent`, `REKALL_OPENAI_API_KEY_MISSING`, zero task process/temp residue, one matching NVIDIA access-violation event, and final commit/tree identity. Full-suite counts and deleted artifact hashes remain durable in the committed acceptance evidence and task report; no redundant rerun/regeneration was warranted after Studio-only fix bytes.
Task 4: complete (commits c5bb5d1..bf547a7, review clean; provider 150/150; Studio 81/81 after fixes; engine 2001/2001; build 0 warnings/errors; live API explicitly credential-gated)
Final review: Important fixes required — canonical AGE error taxonomy/requested-resolved diagnostics; success-payload credential echo protection; inspectable provider availability/authentication state and exact display names; async runner/lease ownership before Codex; final acceptance provenance.
Final review: minor (deferred) — clamp provider-supplied `Retry-After` delay to a documented maximum; bounded retries and cancellation already prevent an unrecoverable hang, so this does not displace provider/Codex delivery.
Final review fix wave: complete (commits bf547a7..d877f45; 5 Important findings addressed; scoped re-review clean; deferred retry minors unchanged and non-blocking)
Final controller verification: Studio 81/81; engine 2020/2020; solution build 0 warnings/errors; exact head `d877f4549321c6787194ab78594fcf47c828d59f`.
Plan complete: OpenAI Responses provider, provider-neutral CLI/Studio, deterministic strict gameplay acceptance, package/audit/capture evidence, and final shared-boundary review are complete. Live API remains explicitly gated by absent `OPENAI_API_KEY` as `REKALL_OPENAI_API_KEY_MISSING`.

## Task 1 — provider-neutral transcript, streaming, and runner contracts

- Status: complete.
- Implementation commit: `27b24cab04b0b3d34e8de0f7a67f32226af2432c` (`feat: generalize language model provider sessions`).
- Focused verification: 89/89 passed; zero failures/skips and no compiler/test warnings.
- Solution build: succeeded with 0 warnings and 0 errors.
- Evidence: `task-1-report.md`.
