# Studio Agent Authoring Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make ordinary-language game creation a first-class, responsive Studio workspace with provider-specific configuration, validated model selection, and automatic usable-model fallback.

**Architecture:** Add a focused `AuthorWorkspace` WPF control bound to semantic provider/readiness properties on the existing Studio view model. Promote Author to the top-level workspace selector, migrate layout persistence away from the old AI Agent output tab, and keep the existing provider catalog, runner lifecycle, gameplay gates, and engine commands authoritative.

**Tech Stack:** C# 13, .NET 10, WPF/XAML, xUnit, existing Rekall AGE editor/workflow/provider contracts.

**Spec:** `docs/superpowers/specs/2026-08-29-studio-agent-authoring-workspace-design.md`

## Global Constraints

- Studio remains a client of provider-neutral agent and engine contracts; it never authors game content itself.
- Provider-specific credentials remain session-only and must never appear in inspectable state or logs.
- Model selection accepts only discovered model IDs; no editable free-text model input.
- Provider switches retain existing cancellation, awaiting, lease disposal, and stale-result suppression behavior.
- The gameplay checkpoint, deterministic runtime assertion, packaging, audit, and visual evidence gates remain strict.
- Run only narrow feature tests during TDD; do not run the full solution suite unless explicitly required as a final acceptance gate.

---

### Task 1: Provider-specific presentation and resilient model selection

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`

**Interfaces:**
- Consumes: `RekallAgeLanguageModelProviderDescriptor`, `RekallAgeLanguageModelInfo`, existing `LanguageModelProviders`, `LanguageModels`, provider transition generation and lease lifecycle.
- Produces: `bool IsOllamaSelected`, `bool IsOpenAiSelected`, `bool IsCodexSelected`, `bool HasUsableLanguageModel`, `string ProviderDisplayStatus`, and validated `SelectedLanguageModel` behavior for XAML.

- [ ] **Step 1: Add failing provider-presentation tests**

Add tests that select each descriptor and assert exactly one semantic provider flag is true, then assert switching provider raises property changes for all provider flags and `HasUsableLanguageModel`.

```csharp
Assert.True(viewModel.IsOllamaSelected);
Assert.False(viewModel.IsOpenAiSelected);
Assert.False(viewModel.IsCodexSelected);

viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(p => p.Id == "codex");
await viewModel.WaitForLanguageModelProviderTransitionAsync();

Assert.False(viewModel.IsOllamaSelected);
Assert.False(viewModel.IsOpenAiSelected);
Assert.True(viewModel.IsCodexSelected);
```

- [ ] **Step 2: Add failing model-fallback and validation tests**

Replace the old expectation that a missing configured default leaves selection empty. Assert that discovered models remain available, the first returned model is selected, Run can become eligible after task/project setup, and assigning an undiscovered model is ignored.

```csharp
Assert.Equal(["gpt-5.6-sol-preview"], viewModel.LanguageModels);
Assert.Equal("gpt-5.6-sol-preview", viewModel.SelectedLanguageModel);
Assert.True(viewModel.HasUsableLanguageModel);
Assert.Contains("configured default", viewModel.ProviderDisplayStatus, StringComparison.OrdinalIgnoreCase);

viewModel.SelectedLanguageModel = "invented-model";
Assert.Equal("gpt-5.6-sol-preview", viewModel.SelectedLanguageModel);
```

- [ ] **Step 3: Run the exact failing view-model selection**

Run:

```powershell
dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~ProviderPresentation|FullyQualifiedName~ProviderDefaultAbsence" -p:UseSharedCompilation=false
```

Expected: FAIL because semantic properties do not exist and missing defaults currently clear the selected model.

- [ ] **Step 4: Implement semantic provider state and validated model selection**

Add computed properties based on the selected descriptor ID and notify them from the provider setter. Restrict model assignment to empty during lifecycle reset or a value present in `LanguageModels`; expose `HasUsableLanguageModel` and notify it whenever the collection/selection changes.

```csharp
public bool IsOllamaSelected => SelectedLanguageModelProvider.Id == "ollama";
public bool IsOpenAiSelected => SelectedLanguageModelProvider.Id == "openai";
public bool IsCodexSelected => SelectedLanguageModelProvider.Id == "codex";
public bool HasUsableLanguageModel => LanguageModels.Contains(SelectedLanguageModel, StringComparer.Ordinal);
```

Use a private lifecycle-only assignment helper when transitions must clear selection so public binding input cannot invent IDs.

- [ ] **Step 5: Implement default fallback with concise primary status**

In `ApplyLanguageModels`, remember the prior model for the same provider, prefer it when still present, otherwise prefer `provider.DefaultModel`, otherwise select the first discovered model. For fallback, set `ProviderDisplayStatus` to a concise warning such as `Configured default qwen3.5:35b unavailable; using qwen3.8:27b.` while retaining `REKALL_LANGUAGE_MODEL_DEFAULT_UNAVAILABLE` with requested/resolved facts in `ValidationLines` and the Studio log.

- [ ] **Step 6: Run the focused view-model tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~ProviderSelection|FullyQualifiedName~ProviderSwitch|FullyQualifiedName~ProviderDefaultAbsence|FullyQualifiedName~OpenAiSessionKey|FullyQualifiedName~Codex" -p:UseSharedCompilation=false
```

Expected: PASS with no credential leakage and unchanged cancellation/disposal guarantees.

- [ ] **Step 7: Commit provider behavior**

```powershell
git add src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs
git commit -m "fix: structure Studio provider selection"
```

### Task 2: First-class responsive Author workspace

**Files:**
- Create: `src/Rekall.Age.Studio/AuthorWorkspace.xaml`
- Create: `src/Rekall.Age.Studio/AuthorWorkspace.xaml.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioLayout.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs`

**Interfaces:**
- Consumes: Task 1 semantic provider properties, existing provider/model collections, commands, `AgentTaskInput`, `AgentLines`, and `StatusText`.
- Produces: named `AuthorWorkspaceHost`, `ShowOpenAiConfiguration`, `ShowCodexConfiguration` via data triggers, top-level workspace mapping helpers, and layout normalization that accepts `Author`, `World`, or `Modeling`.

- [ ] **Step 1: Add failing layout and XAML source tests**

Assert the workspace selector contains Author first, `AuthorWorkspaceHost` exists, the old `TabItem Header="AI Agent"` is absent, the model ComboBox is not editable, OpenAI/Codex sections bind to their matching semantic flags, and Authoring preset resolves to `ActiveWorkspace == "Author"`.

```csharp
Assert.Contains("<TabItem Header=\"Author\"", window, StringComparison.Ordinal);
Assert.Contains("x:Name=\"AuthorWorkspaceHost\"", window, StringComparison.Ordinal);
Assert.DoesNotContain("<TabItem Header=\"AI Agent\"", window, StringComparison.Ordinal);
Assert.Equal("Author", RekallAgeStudioLayout.CreatePreset(RekallAgeStudioLayoutPreset.Authoring).ActiveWorkspace);
```

- [ ] **Step 2: Run the exact failing layout selection**

Run:

```powershell
dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~StudioLayoutTests" -p:UseSharedCompilation=false
```

Expected: FAIL because Author does not exist and Authoring still points to the AI Agent output tab.

- [ ] **Step 3: Create `AuthorWorkspace` with provider-specific cards**

Use a responsive root `Grid` inside a vertical `ScrollViewer`. The left column contains provider, provider-specific setup, non-editable model, reasoning, task editor, and Run/Cancel. The right column contains status and transcript. Collapse provider cards with data triggers:

```xml
<Border x:Name="OpenAiConfiguration">
  <Border.Style>
    <Style TargetType="Border">
      <Setter Property="Visibility" Value="Collapsed" />
      <Style.Triggers>
        <DataTrigger Binding="{Binding IsOpenAiSelected}" Value="True">
          <Setter Property="Visibility" Value="Visible" />
        </DataTrigger>
      </Style.Triggers>
    </Style>
  </Border.Style>
</Border>
```

Keep the OpenAI `PasswordBox` and Apply click handler inside the control so the secret never enters a binding. Expose an internal event or call the view model from code-behind after clearing the box.

- [ ] **Step 4: Promote Author and remove duplicated AI output UI**

Add top-level tabs in exact order Author, World, Modeling. Host `AuthorWorkspace` beside `WorldWorkspace` and `ModelingWorkspaceHost`. Replace index arithmetic with `WorkspaceName()` and `SelectWorkspace(string)` helpers so layout capture/application and selection changes remain stable. Author keeps `ProjectBar` visible, hides the gameplay toolbar, and shows only `AuthorWorkspaceHost`.

- [ ] **Step 5: Update layout persistence and migration**

Increment `CurrentVersion` to 2. Accept `Author`, `World`, and `Modeling`; remove `AI Agent` from valid output tabs. In the store, deserialize legacy version 1 and normalize `ActiveOutputTab == "AI Agent"` to `ActiveWorkspace = "Author"`, `ActiveOutputTab = "Validation"`, then save as version 2 on the next normal layout write. Authoring preset selects Author; Default selects Author for a new installation; Debug selects World/Runtime.

- [ ] **Step 6: Make project creation navigate to Author**

In `MainWindow.OnViewModelPropertyChanged`, observe the project transition that makes `HasProject` true (or add a view-model `ProjectGeneration` notification) and select Author after Create completes. Do not steal workspace focus after ordinary edits or provider progress. Add a focused WPF/view-model test for exactly one create transition.

- [ ] **Step 7: Run layout and Studio shell tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~StudioLayoutTests|FullyQualifiedName~StudioViewModelTests" -p:UseSharedCompilation=false
```

Expected: PASS.

- [ ] **Step 8: Commit the Author workspace**

```powershell
git add src/Rekall.Age.Studio/AuthorWorkspace.xaml src/Rekall.Age.Studio/AuthorWorkspace.xaml.cs src/Rekall.Age.Studio/MainWindow.xaml src/Rekall.Age.Studio/MainWindow.xaml.cs src/Rekall.Age.Studio/RekallAgeStudioLayout.cs tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs
git commit -m "feat: promote natural-language authoring in Studio"
```

### Task 3: Empty-project clarity and project-root discoverability

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs`

**Interfaces:**
- Consumes: existing selected-entity state and project path input.
- Produces: `bool HasSelectedEntity`, `string InspectorEmptyStateText`, project-path tooltip, and Browse button using `Microsoft.Win32.OpenFolderDialog`.

- [ ] **Step 1: Add failing empty-inspector test**

Create an empty project, apply its workbench model, and assert component/property inputs remain empty with `HasSelectedEntity == false` and a neutral prompt.

```csharp
Assert.False(viewModel.HasSelectedEntity);
Assert.Empty(viewModel.ComponentTypeInput);
Assert.Empty(viewModel.PropertyNameInput);
Assert.Equal("Select an entity to inspect components.", viewModel.InspectorEmptyStateText);
```

- [ ] **Step 2: Run the exact failing test**

Run:

```powershell
dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~EmptyProjectInspector" -p:UseSharedCompilation=false
```

Expected: FAIL because `ApplyModel` selects the first available component schema even without an entity.

- [ ] **Step 3: Implement neutral inspector state**

In `ApplyModel`, when `SelectedEntityId` is null, clear component/property/value input, property collections, and help text. Notify `HasSelectedEntity`; use XAML triggers to show the neutral prompt and collapse editing controls until an entity is selected. Preserve schema-guided behavior after a real selection.

- [ ] **Step 4: Add project-path tooltip and Browse action**

Bind the project path TextBox tooltip to `ProjectPathInput`. Add a Browse button whose click handler opens `Microsoft.Win32.OpenFolderDialog` with `InitialDirectory` set only when the current path exists, then assigns the selected folder to `ProjectPathInput`. This is user-local navigation and does not create/delete content.

- [ ] **Step 5: Add and run source/layout assertions**

Assert `ProjectPathInput` is used for the tooltip, a named Browse button exists, and the inspector neutral-state binding exists. Run:

```powershell
dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~EmptyProjectInspector|FullyQualifiedName~StudioLayoutTests" -p:UseSharedCompilation=false
```

Expected: PASS.

- [ ] **Step 6: Commit clarity fixes**

```powershell
git add src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs src/Rekall.Age.Studio/MainWindow.xaml src/Rekall.Age.Studio/MainWindow.xaml.cs tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs
git commit -m "fix: clarify empty Studio projects"
```

### Task 4: Focused verification and resumed playable benchmark

**Files:**
- Modify only if failures identify a generic defect: relevant `src/Rekall.Age.*` file and its narrow test.
- Evidence: `Artifacts/StudioNaturalLanguageBenchmark20260829/Project/**`
- Logs: `%LOCALAPPDATA%/Rekall AGE/Studio/Logs/studio-*.log`

**Interfaces:**
- Consumes: completed Tasks 1-3 and the existing Studio/project-agent/runtime gauntlet contracts.
- Produces: warning-free Studio build, visible Windows UX evidence, deterministic gameplay assertion, package/audit evidence, and a playable player launch.

- [ ] **Step 1: Run the complete Studio project tests only**

Run:

```powershell
dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj -p:UseSharedCompilation=false
```

Expected: all Studio tests pass. This is not the full solution suite.

- [ ] **Step 2: Build Studio warning-free**

Run:

```powershell
dotnet build src/Rekall.Age.Studio/Rekall.Age.Studio.csproj -c Debug --no-restore -p:UseSharedCompilation=false
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Inspect the real Author workspace at two sizes**

Launch the rebuilt Studio, verify Author at maximized size, then resize to 1120x700. At both sizes confirm provider, relevant provider setup only, model, task editor, Run, Cancel, status, and transcript are reachable without selecting Debug or resizing the Output splitter.

- [ ] **Step 4: Resume the Neon Orchard prompt through visible UI**

Select the valid Ollama fallback and submit this ordinary-language request:

```text
Create a complete playable 3D arcade game called Neon Orchard. The player pilots a glowing pollinator through a compact floating garden, using semantic keyboard controls to move and collect energy fruit while avoiding moving hazards. Include a clear objective, score, health or failure state, restartable win/lose loop, readable in-world or UI instructions, strong color contrast, lighting, and satisfying movement/collection feedback. Build all gameplay through agent-authored modules and generic AGE components/events. Use delta time, expose an input action map, verify real input-driven gameplay with a strict deterministic runtime assertion, then package and audit the finished Windows game and leave clear evidence of the controls and produced artifact.
```

- [ ] **Step 5: Monitor logs and retain honest failure evidence**

Tail the current Studio log and inspect project/module/build outputs. If a tool call, checkpoint, compile, render, package, or UI flow fails, invoke systematic debugging, add the narrowest failing regression test, repair the generic contract, rerun that exact test, rebuild Studio if affected, and continue the same benchmark. Do not weaken assertions or replace the user prompt with engine instructions.

- [ ] **Step 6: Prove gameplay deterministically**

Use the produced scene and module with `rekall.runtime.inspect_scene` representative input frames. Require an attached `Game.*` component and an executable assertion showing either nonzero position/transform delta or a changed agent-owned component value. On failure, repair authored behavior or the generic engine contract and rerun the same intended assertion.

- [ ] **Step 7: Inspect package and launch playable output**

Confirm package and audit success, verify the archive/runnable paths exist, launch the produced Windows player, exercise the documented controls, and inspect the rendered result plus current logs. Record any residual limitations without claiming success unless the game is visibly nonblank and mechanically responsive.

- [ ] **Step 8: Commit only source/test repairs and report evidence**

Do not commit benchmark packages/captures unless repository policy explicitly tracks them. Commit each generic repair with its test. Report commits, exact test/build results, deterministic assertion evidence, package/audit paths, visual/playable outcome, and remaining risks.
