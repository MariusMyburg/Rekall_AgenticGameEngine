# OpenAI Provider Acceptance — 2026-08-25

## Outcome

The provider-neutral Studio path and the ordinary OpenAI Responses project-agent path are accepted deterministically. Studio consumes `RekallAgeLanguageModelProviderCatalog` leases and `IRekallAgeProjectAgentRunner`; it does not construct provider clients. The selectable provider IDs are exactly `ollama` and `openai`, the OpenAI default is exactly `gpt-5.6-sol`, and the Ollama default remains `qwen3.5:35b`.

Authentication state for this run was `absent` (redacted state only). The real-API smoke was therefore not run and is not claimed as passed. Its exact external gate is:

```text
REKALL_OPENAI_API_KEY_MISSING
```

The deterministic acceptance used fake HTTP only. No credential value was printed, serialized, placed in an artifact, or written to this report.

## Tested source identity

- Branch: `codex/high-fidelity-forward-plus`
- Base commit: `c5bb5d1779ac344eb7fb9b79f9d98c26ce3c17cc`
- Base tree: `33834ff780bb03ec87044f4ba616e38606ad8d85`
- Provider/model under acceptance: `openai` / `gpt-5.6-sol`
- Ollama compatibility model exercised by Studio: `qwen3.5:35b`

## Studio behavior accepted

- Provider, model, reasoning, refresh, run, and cancel controls are provider-neutral.
- The OpenAI credential control is a masked `PasswordBox`; its value is copied only into session memory, the control is immediately cleared, and the value has no workbench/project/settings/automation binding.
- Switching providers clears stale models, cancels and awaits active model refresh and agent work, disposes the owned lease, acquires the selected provider through the catalog, then loads models.
- The exact provider default is selected only when returned by that provider. Qwen remains selectable.
- Missing OpenAI auth remains the stable actionable Studio and automation status `REKALL_OPENAI_API_KEY_MISSING: OpenAI requires OPENAI_API_KEY for this session.`
- Studio agent output includes provider, model, response ID, token usage, tool count, elapsed time, summary, and bounded final content. Provider exceptions preserve bounded requested/resolved diagnostics.
- Completed agent operations are removed from the active lifecycle slot, so command-reported failures are not rethrown by later disposal.

## TDD evidence

Production edits followed observed RED before GREEN for each new behavior. Representative focused commands were:

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~ProviderSelectionExposesStableMissingOpenAiCredentialGateWithoutRetainingOllamaModels|FullyQualifiedName~ProviderSwitchCancelsAndAwaitsTheCurrentRunBeforeDisposingItsLeaseAndLoadingTheExactDefault" --verbosity minimal
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentConsumesBoundedStreamingProgressAndPreservesToolCallIdentity" --verbosity minimal
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~StudioWorkspaceWiresCanonicalGameCreationCommandsAndRenderedViewport" --verbosity minimal
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~ProviderSwitchCancelsAndAwaitsModelRefreshBeforeDisposingItsLease" --verbosity minimal
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~HeadlessOpenAiAutomationStopsAtTheStableCredentialGateAndWritesEvidence" --verbosity minimal
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAiResponsesRunsTheOrdinaryProjectAgentPathThroughStrictPostMutationGameplayProof" --verbosity minimal
```

Observed RED evidence included missing provider-neutral ViewModel members, missing `ResponseId`, missing XAML bindings, incorrect model-refresh disposal order (`lease-disposed` before `models-cancelled`), missing catalog-backed automation entry point, and the acceptance ZIP lookup using the wrong directory. The gameplay assertion itself was not weakened. GREEN included the exact automation credential-gate test in 4.5006 seconds, the strict deterministic acceptance in 14.9530 seconds, and the post-review agent-task lifecycle regression test in 4.7965 seconds.

## Deterministic ordinary project-agent acceptance

`OpenAiProjectAgentAcceptanceTests.OpenAiResponsesRunsTheOrdinaryProjectAgentPathThroughStrictPostMutationGameplayProof` acquires the `openai` catalog lease, uses its `IRekallAgeProjectAgentRunner`, and therefore executes through `RekallAgeProjectAgentSession`. The scripted Responses stream reverses these hashed aliases back to canonical commands:

1. `rekall_context_engine_status_8179b61222fc` → `rekall.context.engine_status`
2. `rekall_tools_search_b2d44b5dab44` → `rekall.tools.search`
3. `rekall_workflow_agent_authoring_gauntlet_68326d2159c2` → `rekall.workflow.agent_authoring_gauntlet`
4. `rekall_tools_search_b2d44b5dab44` → `rekall.tools.search`
5. `rekall_runtime_inspect_scene_265e291310a4` → `rekall.runtime.inspect_scene`

All five real AGE command executions succeeded. The runtime proof occurred after the latest scene/module mutation and used one frame with `deltaSeconds = 1` and semantic action `agent.gauntlet.advance` (`value = 1`, `isDown = true`, `wasPressed = true`). It required all three strict assertions:

- `Game.Modules.AgentGauntlet.GauntletState` exists on `Agent Authored Marker`.
- `delta.component.property` for `progress` equals `1`.
- `delta.position2d.x` equals `1`.

An independent direct runtime inspection repeated the same proof after the agent completed. Every assertion passed, the observed X transform delta was exactly `1`, and `AgentGauntletRuntimeSystem` ran. The persisted scene retained the attached `Game.*` state component and `Rekall.InputActionMap` semantic mapping.

## Acceptance artifacts and hashes

The focused evidence run retained artifacts at the following exact paths long enough to hash and inspect them. They were deleted after recording, as confirmed in the residue section.

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `C:\Users\Marius\AppData\Local\Temp\rekall-age-openai-provider-acceptance-evidence-20260825-task4\Builds\OpenAiAcceptance.zip` | 2,070,417 | `892D4693C81EE20522F99481D0D4696735290FD81E2211B8CBC888867F5247D7` |
| `C:\Users\Marius\AppData\Local\Temp\rekall-age-openai-provider-acceptance-evidence-20260825-task4\Builds\OpenAiAcceptance\rekall.package.json` | 7,654 | `23AE8929FA1FEA17C74E104ED2A1840559C69D302544BA809BD65F2C58797457` |
| `C:\Users\Marius\AppData\Local\Temp\rekall-age-openai-provider-acceptance-evidence-20260825-task4\Builds\AgentAuthoringGauntletAudit\package_play_frame_001.png` | 596 | `24035D3B833B4A9A1844B04B7C9BDF34D56D61B72B003CBF6213ECA355E86002` |
| `C:\Users\Marius\AppData\Local\Temp\rekall-age-openai-provider-acceptance-evidence-20260825-task4\Scenes\Main.age.scene.json` | 1,856 | `13E6CE257FF881772C3D24825FD96F68B38206B13EE6CC26141318BFD86E5153` |
| `C:\Users\Marius\AppData\Local\Temp\rekall-age-openai-provider-acceptance-evidence-20260825-task4\Modules\AgentGauntlet\AgentGauntletModule.cs` | 3,478 | `DEBE7BE3F6CFB475CE7CC1144CA256400C795FCE8B4857C66E797C5250E7FEF4` |
| `C:\Users\Marius\AppData\Local\Temp\rekall-age-openai-provider-acceptance-evidence-20260825-task4\Transactions\transactions.age.json` | 10,099 | `E77B443457389F0A19B1AA018955E15108A8E8E2D33057456E3CA11ECAAFDD3D` |

The gauntlet packaged, audited, and captured the authored game before returning success. The package ZIP was discoverable beneath the project root, its manifest existed, and the proof PNG was non-empty.

## Final sequential verification

Commands were executed in this order.

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenAiLanguageModelClientTests|FullyQualifiedName~OpenAiProjectAgentAcceptanceTests|FullyQualifiedName~LanguageModelProviderCatalogTests|FullyQualifiedName~ProjectAgentRunnerTests|FullyQualifiedName~ProjectAgentSessionTests|FullyQualifiedName~LanguageModelAgentTests" --verbosity minimal
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --filter "FullyQualifiedName~ProviderSelectionExposesStableMissingOpenAiCredentialGate|FullyQualifiedName~ProviderSwitchCancelsAndAwaits|FullyQualifiedName~OpenAiSessionKeyUnlocks|FullyQualifiedName~HeadlessOpenAiAutomationStopsAtTheStableCredentialGate|FullyQualifiedName~HeadlessAutomationCreatesProjectAndCompletesAgentGauntlet" --verbosity minimal
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --no-restore --verbosity minimal
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --verbosity minimal
dotnet build Rekall.AGE.sln --no-restore --verbosity minimal
```

| Gate | Result | Wall duration |
|---|---|---:|
| Focused engine/provider | 150 passed, 0 failed | 14.7046 s |
| Focused Studio/provider | 6 passed, 0 failed | 13.4826 s |
| Full Studio | 74 passed, 0 failed | 52.5244 s |
| Full engine | 2,001 passed, 0 failed | 263.0224 s |
| Solution build | succeeded; 0 warnings, 0 errors | 5.4022 s |

One earlier full-engine attempt was aborted after 1,133 passes and zero failures because the native test host crashed. Windows Application Error event 1000 and WER event 1001 identified NVIDIA `nvoglv64.dll` version `32.0.16.1088`, exception `0xc0000005`, fault offset `0x0000000000f62e02`. No engine code was changed for this external driver fault. The immediate identical command completed all 2,001 tests successfully.

The environment gate was checked without emitting any value:

```powershell
if([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)){ 'OPENAI_AUTH_STATE=absent'; 'OPENAI_REAL_SMOKE_GATE=REKALL_OPENAI_API_KEY_MISSING' } else { 'OPENAI_AUTH_STATE=present-redacted'; 'OPENAI_REAL_SMOKE_GATE=authorized' }
```

Observed output was `OPENAI_AUTH_STATE=absent` and `OPENAI_REAL_SMOKE_GATE=REKALL_OPENAI_API_KEY_MISSING`.

## Residue and credential review

- Task-owned running `dotnet`, `testhost`, and player processes: `0`.
- Task acceptance temp roots matching `rekall-age-openai-project-acceptance-*`: `0`.
- Retained evidence roots matching `rekall-age-openai-provider-acceptance-*`: `0`.
- Studio provider-switch, OpenAI-gate, and partial-evidence temp roots: `0`.
- Seven unrelated `rekall-age-studio-agent-*` directories predated this task; the newest was created `2026-08-25T02:01:05.1363311+02:00` and was left untouched.
- The native crash left one task acceptance directory because process termination bypassed test cleanup; it and the deliberately retained evidence root were removed after their exact resolved paths were verified under the Windows temp directory. These were ephemeral and are not recoverable.
- Review covered `c5bb5d1..HEAD`, the uncommitted task diff before commit, generated evidence, UI bindings, automation JSON, diagnostics, and task files. No credential-shaped value or key assignment was found. Test credentials are synthetic fixtures, never emitted into production evidence.
