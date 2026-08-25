# Task 3 Report: End-User Codex Surfaces and Real AGE Acceptance

## Outcome

Implemented and committed the end-user Codex App Server workflow in `ab735659919e38847efca5606fa534c4bcc5fe47` (`feat: author games with Codex in AGE`). The CLI now exposes safe Codex authentication status and explicit unattended project runs; Studio exposes Codex selection, safe auth/model status, and interactive approval routing that declines by default outside its UI handler.

The real Windows npm installation is launched without a shell through structured Node arguments. App Server MCP elicitation approvals use the live protocol's `action`/`content` response shape, while command/file approvals retain their distinct `decision` response shape. JSON-array redaction, read-only ephemeral smoke threads, and bounded protocol failure facts were repaired from real ordinary-workflow failures.

## Real authenticated evidence

- Runtime/protocol: Codex CLI `0.149.1` and its shipped JSONL App Server protocol; `initialize` exposes no independent numeric protocol version. The runtime was upgraded from `0.130.0` because that catalog did not expose Sol.
- Safe auth state: ChatGPT; no identity, credential, or token value emitted.
- Provider/model: `codex` / `gpt-5.6-sol`.
- Authenticated protocol smoke: 1/1 passed in approximately 6 seconds.
- Real Sol project-agent run: 286.17 seconds, 26 AGE MCP tool calls.
- Authored game: Prism Relay, not Aetherfall; 22 entities, 23 renderables, semantic input, delta-time module, attached `Game.*` state.
- Strict independent gameplay proof: transform X delta `4.2`, `DistanceTravelled` delta `4.2`, and changed `ElapsedSeconds`; all three assertions passed after the latest mutation.
- Modules: 2 projects built.
- Delivery: package ready, 43 files, zero missing artifacts, player exit 0, nonblank captured frame, 100 informative colors.
- Package ZIP SHA-256: `AD4E082CB044FDB67AC89DF2D6EACD6A0412CEAB9DDDFDA5EBB5C997F2F81C54`.
- Capture SHA-256: `2C6B0E7A0BE6F6D8D20D4F9AC5F73B105566917D9E2DB4F2ED9B019455947C32`.
- Retained bounded evidence root: `C:\Users\Marius\AppData\Local\Temp\RekallAgeCodexAcceptance-20260825` (108 files, 9,059,837 bytes).

Full evidence and exact artifact paths are recorded in `docs/production/2026-08-25-codex-agent-acceptance.md`.

## Verification

- Focused Codex/CLI/catalog: 60 passed, 0 failed.
- Authenticated Codex smoke: 1 passed, 0 failed.
- Studio selector regression: 1 passed, 0 failed.
- Full Studio: 83 passed, 0 failed in 47 seconds.
- Full engine: 2,064 passed, 0 failed in 4 minutes 8 seconds.
- Solution build: succeeded with 0 warnings and 0 errors; final post-cleanup build took 4.06 seconds.
- `git diff --check`: clean.
- No task-owned Codex CLI, AGE CLI, player, `dotnet`, or `testhost` process remained. The user's open Studio was not closed.

## Residual blockers

None for Task 3. The retained Prism Relay root is intentional acceptance evidence, not an unbounded process or secret-bearing residue. Deferred pathological Task 1/2 hardening and flagship Aetherfall visual/gameplay work were not expanded into this functionality-first task.
