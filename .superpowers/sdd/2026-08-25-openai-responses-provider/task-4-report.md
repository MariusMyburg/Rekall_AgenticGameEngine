# Task 4 Report — Provider-Neutral Studio and OpenAI Acceptance

## Result

Task 4 is implemented and deterministically accepted. Studio now selects catalog-backed `ollama` or `openai` providers, owns provider leases, runs through `IRekallAgeProjectAgentRunner`, and exposes provider-neutral model/reasoning controls. The fake-HTTP `gpt-5.6-sol` acceptance executed real AGE authoring, package/audit/capture, and strict post-mutation gameplay proof through the ordinary project-agent session path.

The environment did not contain `OPENAI_API_KEY`, so the real API smoke was not attempted. The honest external gate is `REKALL_OPENAI_API_KEY_MISSING`.

## Implemented behavior

- Replaced Studio's Ollama-only client/session ownership with `RekallAgeLanguageModelProviderCatalog`, owned leases, and the common runner contract.
- Added provider/model/reasoning selection, model refresh, exact defaults, stable diagnostics, and safe run/refresh cancellation and disposal ordering.
- Added a masked, session-memory-only OpenAI key action with no binding or persistence surface.
- Added provider/model/response/usage/tool/elapsed transcript facts and carried the final provider response ID through the agent result.
- Added provider-aware headless automation, including an evidence-writing no-key stop gate.
- Preserved existing injected-client automation tests and selectable Ollama/Qwen behavior.
- Added fake-HTTP Responses acceptance with canonical hashed-alias reversal and real AGE command execution.
- Proved semantic input, attached `Game.Modules.AgentGauntlet.GauntletState`, component progress delta `1`, and transform X delta `1` after the latest scene/module mutation. The strict assertion was repaired by correcting artifact lookup, not weakened.

## TDD and review

Focused RED/GREEN cycles covered provider surface, run and refresh ownership ordering, session credential safety, neutral XAML, automation provider parsing/gating, response ID facts, and deterministic gameplay acceptance. Full-suite review found one lifecycle regression: a completed faulted agent task remained in the active slot and was re-awaited by disposal. The existing failure-evidence test reproduced it; applying Studio's established tracked-operation cleanup pattern made the exact test green.

Self-review covered all task-owned source/tests/docs against `c5bb5d1`, UI and automation persistence surfaces, provider ownership, exception diagnostics, cancellation races, artifact cleanup, and credential-shaped strings. The controller-owned `progress.md` was neither edited nor staged by this task.

## Verification

- Focused engine/provider: 150 passed, 0 failed (14.7046 s).
- Focused Studio/provider: 6 passed, 0 failed (13.4826 s).
- Full Studio: 74 passed, 0 failed (52.5244 s).
- Full engine: 2,001 passed, 0 failed (263.0224 s).
- Solution: build succeeded with 0 warnings and 0 errors (5.4022 s).

An earlier engine attempt was externally aborted by an NVIDIA `nvoglv64.dll` access violation after 1,133 passes and no failures. The unchanged command immediately completed all 2,001 tests. Exact commands, hashes, assertions, artifact paths, authentication gate, and residue are recorded in `docs/production/2026-08-25-openai-provider-acceptance.md`.

## Remaining external gate

Live OpenAI behavior remains unverified on this machine because the session had no credential. No fallback was used and no live-pass claim is made.

## Review fix round 1 — 2026-08-25

Base reviewed: `05f6082f563db0fdc361ab53d64b9978fe0234f4`.

The three load-bearing Studio lifecycle findings were repaired without changing shared engine code:

- Exact-default selection now leaves the model selection empty when `gpt-5.6-sol` is absent and reports `REKALL_LANGUAGE_MODEL_DEFAULT_UNAVAILABLE` with stable `Requested: gpt-5.6-sol.` and the bounded resolved model list. It never selects the first returned OpenAI model as a fallback.
- Provider transitions now carry an incrementing generation plus their captured provider, settings, runner, and owned lease. Only the current generation may publish models, selection, runner, lease, or status. Stale acquired leases are disposed, and every current transition failure clears the model list and selection. A rapid OpenAI-to-Ollama switch with a failing final Ollama provider proves that stale OpenAI models cannot publish while the final transition is pending or after it fails.
- Shutdown now observes tracked provider transitions, refreshes, and agent runs without allowing unexpected faults to skip stop/preview work. Runner/lease disposal, session-key clearing, lifecycle-token disposal, and mode-gate disposal run unconditionally in `finally`. Faulted run/refresh tests prove cleanup and stable `REKALL_STUDIO_LANGUAGE_MODEL_SHUTDOWN_FAILED` evidence while rejecting both the synthetic session credential and opaque upstream exception payload from inspectable Studio state. Generic command failures no longer expose exception messages.

### Strict RED → GREEN evidence

1. Exact default absent:
   - Command: `dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~ProviderDefaultAbsenceLeavesSelectionEmptyWithRequestedAndResolvedDiagnostics" --verbosity minimal`
   - RED: 0 passed, 1 failed; Studio selected `gpt-5.6-sol-preview` instead of leaving selection empty (15.9677 s).
   - GREEN: 1 passed, 0 failed (4.8750 s).
2. Rapid provider switch with failing final provider:
   - Command: `dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~RapidProviderSwitchDoesNotPublishStaleModelsWhenTheFinalProviderFails" --verbosity minimal`
   - RED: 0 passed, 1 failed; stale OpenAI models were visible while the final Ollama transition was pending (3.6179 s).
   - GREEN: 1 passed, 0 failed (4.6921 s).
3. Faulting agent run and model refresh during shutdown:
   - Command: `dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~ShutdownCleansUpProviderAndSessionCredentialAfterFaultingAgentRun|FullyQualifiedName~ShutdownCleansUpProviderAndSessionCredentialAfterFaultingModelRefresh" --verbosity minimal`
   - RED: 0 passed, 2 failed; the completed refresh fault escaped `DisposeAsync` before cleanup, and the faulted agent-run path exposed no stable shutdown diagnostic (3.6416 s).
   - GREEN: 2 passed, 0 failed (4.8036 s).

### Fix verification

- Amended focused set command: `dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~ProviderDefaultAbsenceLeavesSelectionEmptyWithRequestedAndResolvedDiagnostics|FullyQualifiedName~RapidProviderSwitchDoesNotPublishStaleModelsWhenTheFinalProviderFails|FullyQualifiedName~ShutdownCleansUpProviderAndSessionCredentialAfterFaultingAgentRun|FullyQualifiedName~ShutdownCleansUpProviderAndSessionCredentialAfterFaultingModelRefresh" --verbosity minimal`
  - 4 passed, 0 failed (3.6520 s).
- Full Studio command: `dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --verbosity minimal`
  - 78 passed, 0 failed (51.1803 s).
- Warning-free solution command: `dotnet build Rekall.AGE.sln --no-restore --verbosity minimal`
  - Build succeeded, 0 warnings, 0 errors (3.3681 s; MSBuild elapsed 3.18 s).
- Full engine was not rerun in this fix round because the diff changes only `Rekall.Age.Studio`, its test project, and this report; no shared engine behavior or engine test bytes changed. The prior unchanged full-engine result remains 2,001 passed, 0 failed.

The controller-owned `progress.md` remained unstaged and unmodified by this fix round. No live-auth or hardware evidence claims were changed.
