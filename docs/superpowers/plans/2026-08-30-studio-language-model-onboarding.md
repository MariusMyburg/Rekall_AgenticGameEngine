# Studio Language-Model Onboarding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a readiness-first Studio wizard that guides first-launch users to one verified language-model provider and compatible authoring model, with opt-in Windows-protected API-key persistence and actionable recovery.

**Architecture:** A versioned non-secret setup store and a provider-scoped protected credential store feed a typed readiness probe. A dedicated wizard view model/window consumes those services, while a small startup coordinator restores configuration before normal model refresh and exposes the same setup flow from Settings, Author, and World.

**Tech Stack:** .NET 10, C# 13, WPF, System.Text.Json, Windows DPAPI (`System.Security.Cryptography.ProtectedData`), xUnit, existing Rekall AGE provider catalog and async-command conventions.

**Spec:** `docs/superpowers/specs/2026-08-30-studio-language-model-onboarding-design.md`

## Global Constraints

- Persist provider, model, reasoning effort, endpoints, schema version, completion state, and last successful check; never persist an API key in ordinary JSON.
- Remembered OpenAI and Kimi keys are opt-in and protected for the current Windows user.
- Never expose credentials in projects, status text, validation output, logs, exception messages, `ToString`, or test failure output.
- The wizard may be dismissed without blocking manual Studio work, but dismissed setup remains incomplete and reappears on the next ordinary launch.
- Automation mode never opens interactive onboarding.
- A setup is complete only when the selected provider is reachable/authenticated and a completion- and tool-capable model is selected.
- Preserve World as the default workspace after command-line, Create, Open, or Examples project loading.
- Codex controls appear only when Codex is selected.
- Use only narrowly targeted tests during development; do not run the full solution suite.

---

## File Structure

Create these focused units:

- `src/Rekall.Age.Studio/RekallAgeStudioLanguageModelSetup.cs` — versioned non-secret state, normalization, store interface, and atomic JSON store.
- `src/Rekall.Age.Studio/RekallAgeStudioCredentialStore.cs` — provider-scoped credential interface and current-user DPAPI implementation.
- `src/Rekall.Age.Studio/RekallAgeLanguageModelReadiness.cs` — readiness states/checks/remediation records and production probe.
- `src/Rekall.Age.Studio/RekallAgeStudioLanguageModelSetupViewModel.cs` — wizard navigation, provider-specific setup state, cancellation, and completion predicate.
- `src/Rekall.Age.Studio/LanguageModelSetupWindow.xaml` — owned five-step wizard UI.
- `src/Rekall.Age.Studio/LanguageModelSetupWindow.xaml.cs` — PasswordBox extraction, native GGUF picker, URI launch, and close semantics only.
- `src/Rekall.Age.Studio/RekallAgeStudioLanguageModelSetupCoordinator.cs` — startup restoration, sanity check, wizard invocation, and shared Settings entry point.

Modify these integration points:

- `src/Rekall.Age.Studio/Rekall.Age.Studio.csproj` — add the DPAPI package reference.
- `src/Rekall.Age.Studio/MainWindow.xaml` and `.xaml.cs` — Settings menu, incomplete-setup banner, startup sequencing, and wizard ownership.
- `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs` — stop optimistic readiness, restore provider/model/reasoning, accept resolved session credentials, share readiness status, and clear both session keys at shutdown.
- `src/Rekall.Age.Studio/AuthorWorkspace.xaml` and `.xaml.cs` — route provider configuration through the shared coordinator and expose `Fix setup` without duplicating wizard logic.
- `src/Rekall.Age.Studio/Documentation/Rekall-AGE-Documentation.html` — first-launch, credential, provider, and recovery documentation.

Create focused test files matching each production unit under `tests/Rekall.Age.Studio.Tests`.

---

### Task 1: Versioned Non-Secret Setup Store

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioLanguageModelSetup.cs`
- Create: `tests/Rekall.Age.Studio.Tests/LanguageModelSetupStoreTests.cs`

**Interfaces:**
- Produces: `RekallAgeStudioLanguageModelSetup`, `IRekallAgeStudioLanguageModelSetupStore`, and `RekallAgeStudioLanguageModelSetupStore`.
- Consumers: Tasks 4, 6, and 7.

- [ ] **Step 1: Write normalization and persistence tests**

Create tests covering missing files, corrupt JSON, unsupported future versions, round-trip persistence, incomplete dismissal, completed state, and secret exclusion. Use an isolated temporary directory and this representative completed value:

```csharp
var setup = new RekallAgeStudioLanguageModelSetup(
    Version: RekallAgeStudioLanguageModelSetup.CurrentVersion,
    IsComplete: true,
    ProviderId: "ollama",
    ModelId: "qwen3.8:27b",
    ReasoningEffort: "high",
    OllamaUrl: "http://127.0.0.1:11434",
    OpenAiUrl: null,
    KimiUrl: null,
    LastSuccessfulCheckUtc: new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero),
    ReadinessVersion: RekallAgeStudioLanguageModelSetup.CurrentReadinessVersion);
```

Assert that the serialized file contains no case-insensitive occurrence of `key`, `token`, `password`, `authorization`, or a supplied sentinel secret.

- [ ] **Step 2: Run the focused store tests and verify they fail**

Run:

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~LanguageModelSetupStoreTests"
```

Expected: compilation fails because the setup/store types do not exist.

- [ ] **Step 3: Implement the state and atomic store**

Define the record with these constants and defaults:

```csharp
internal sealed record RekallAgeStudioLanguageModelSetup(
    int Version,
    bool IsComplete,
    string ProviderId,
    string ModelId,
    string ReasoningEffort,
    string? OllamaUrl,
    string? OpenAiUrl,
    string? KimiUrl,
    DateTimeOffset? LastSuccessfulCheckUtc,
    int ReadinessVersion)
{
    public const int CurrentVersion = 1;
    public const int CurrentReadinessVersion = 1;

    public static RekallAgeStudioLanguageModelSetup Incomplete { get; } = new(
        CurrentVersion, false, "ollama", "qwen3.8:27b", "high",
        null, null, null, null, CurrentReadinessVersion);
}
```

Add:

```csharp
internal interface IRekallAgeStudioLanguageModelSetupStore
{
    ValueTask<RekallAgeStudioLanguageModelSetup> LoadAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(RekallAgeStudioLanguageModelSetup setup, CancellationToken cancellationToken);
}
```

Normalize only known provider IDs (`ollama`, `gguf`, `kimi`, `openai`, `codex`), non-empty model IDs, supported reasoning values, and absolute HTTP(S) endpoints. Unknown/corrupt/future state returns `Incomplete`.

The production path is `%LOCALAPPDATA%\Rekall\AGE\Studio\language-model-setup-v1.json`. Save to a unique sibling temporary file, flush it, then replace/move it over the destination. Always clean up only that exact temporary file in `finally`.

For isolated automation and live QA, resolve the root from `REKALL_AGE_STUDIO_SETUP_ROOT` when it contains a valid absolute path; otherwise use the production LocalApplicationData root. This override changes only onboarding preferences/credentials, never projects or engine content. Add tests proving a relative or malformed override is ignored and an absolute override places the setup file beneath that exact directory.

- [ ] **Step 4: Run the focused store tests**

Run the command from Step 2.

Expected: all `LanguageModelSetupStoreTests` pass.

- [ ] **Step 5: Commit the setup store**

```powershell
git add src/Rekall.Age.Studio/RekallAgeStudioLanguageModelSetup.cs tests/Rekall.Age.Studio.Tests/LanguageModelSetupStoreTests.cs
git commit -m "feat(studio): persist language model setup"
```

---

### Task 2: Windows-Protected Credential Store

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioCredentialStore.cs`
- Create: `tests/Rekall.Age.Studio.Tests/StudioCredentialStoreTests.cs`
- Modify: `src/Rekall.Age.Studio/Rekall.Age.Studio.csproj`

**Interfaces:**
- Consumes: provider IDs normalized by `RekallAgeStudioLanguageModelSetup`.
- Produces: `IRekallAgeStudioCredentialStore` and `RekallAgeStudioDpapiCredentialStore`.
- Consumers: Tasks 4 and 6.

- [ ] **Step 1: Write credential isolation and redaction tests**

Use a temporary credential directory and test these operations:

```csharp
await store.WriteAsync("openai", "openai-sentinel-secret", cancellationToken);
await store.WriteAsync("kimi", "kimi-sentinel-secret", cancellationToken);
Assert.Equal("openai-sentinel-secret", await store.ReadAsync("openai", cancellationToken));
Assert.Equal("kimi-sentinel-secret", await store.ReadAsync("kimi", cancellationToken));
await store.RemoveAsync("openai", cancellationToken);
Assert.Null(await store.ReadAsync("openai", cancellationToken));
Assert.Equal("kimi-sentinel-secret", await store.ReadAsync("kimi", cancellationToken));
```

Assert that protected files contain neither sentinel value as UTF-8 or UTF-16 text, `ToString()` does not expose values, unsupported provider IDs are rejected, whitespace credentials are rejected, and a corrupt protected payload returns a stable `REKALL_CREDENTIAL_STORE_CORRUPT` failure without embedding file bytes.

- [ ] **Step 2: Run the focused credential tests and verify they fail**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~StudioCredentialStoreTests"
```

Expected: compilation fails because the credential-store types do not exist.

- [ ] **Step 3: Implement provider-scoped DPAPI protection**

Define:

```csharp
internal interface IRekallAgeStudioCredentialStore
{
    ValueTask<string?> ReadAsync(string providerId, CancellationToken cancellationToken);
    ValueTask WriteAsync(string providerId, string credential, CancellationToken cancellationToken);
    ValueTask RemoveAsync(string providerId, CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioCredentialStoreException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
```

Store only `openai` and `kimi`. Encode the credential as UTF-8, protect it with `ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser)`, zero the plaintext byte buffer in `finally`, and atomically write provider-specific files under `%LOCALAPPDATA%\Rekall\AGE\Studio\Credentials`. Use entropy derived from the exact target `Rekall AGE Studio/<providerId>`. Protect/unprotect failures become redacted stable exceptions.

Add:

```xml
<PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.0" />
```

- [ ] **Step 4: Run the focused credential tests**

Run the command from Step 2.

Expected: all `StudioCredentialStoreTests` pass on Windows.

- [ ] **Step 5: Commit protected credentials**

```powershell
git add src/Rekall.Age.Studio/RekallAgeStudioCredentialStore.cs src/Rekall.Age.Studio/Rekall.Age.Studio.csproj tests/Rekall.Age.Studio.Tests/StudioCredentialStoreTests.cs
git commit -m "feat(studio): remember provider keys securely"
```

---

### Task 3: Typed Provider Readiness Probe

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeLanguageModelReadiness.cs`
- Create: `tests/Rekall.Age.Studio.Tests/LanguageModelReadinessProbeTests.cs`
- Modify: `src/Rekall.Age.Agent/LanguageModels/RekallAgeOllamaLanguageModelClient.cs`
- Modify: `tests/Rekall.Age.Tests/Agent/OllamaLanguageModelClientTests.cs`

**Interfaces:**
- Consumes: `RekallAgeLanguageModelProviderCatalog`, `RekallAgeLanguageModelProviderSettings`, and `IRekallAgeLanguageModelClient.ListModelsAsync`.
- Produces: `IRekallAgeLanguageModelReadinessProbe.ProbeAsync(RekallAgeLanguageModelReadinessRequest, CancellationToken)`.
- Consumers: Tasks 4, 6, and 7.

- [ ] **Step 1: Write failing Ollama protocol/identity tests**

Extend the Ollama client tests so a successful version response is parsed and a non-Ollama/malformed version response is rejected with a stable provider exception. Add this API:

```csharp
public async ValueTask<string> GetVersionAsync(CancellationToken cancellationToken)
```

Expected successful response: `{ "version": "0.33.2" }`.

- [ ] **Step 2: Run the exact Ollama client tests and verify failure**

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~OllamaLanguageModelClientTests"
```

Expected: compilation fails because `GetVersionAsync` does not exist.

- [ ] **Step 3: Implement bounded Ollama version detection**

Call `GET /api/version`, require a non-empty `version`, and map malformed responses to `REKALL_OLLAMA_ENDPOINT_INVALID`. Preserve cancellation; do not include response bodies in the exception message.

- [ ] **Step 4: Write readiness result/probe tests**

Define fakes for executable detection, process start, HTTP/provider leases, and environment credentials. Cover exact results for:

- executable missing;
- executable present but local endpoint refused connection;
- custom endpoint unreachable;
- endpoint responds but is not Ollama;
- no models;
- models present but no tool-capable completion model;
- default missing with compatible fallback warning;
- `qwen3.8:27b` ready;
- Kimi/OpenAI missing key, rejected key, rate limit, outage, network failure, and ready;
- Codex authentication required, required model missing, and ready;
- GGUF inheriting Ollama prerequisite failures;
- cancellation producing cancellation rather than a failure result;
- every result omitting a supplied sentinel key.

- [ ] **Step 5: Run the focused readiness tests and verify failure**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~LanguageModelReadinessProbeTests"
```

Expected: compilation fails because readiness types do not exist.

- [ ] **Step 6: Implement readiness contracts and production probe**

Define:

```csharp
internal enum RekallAgeLanguageModelReadinessState { Ready, Warning, Blocked }

internal sealed record RekallAgeLanguageModelReadinessCheck(
    string Id,
    RekallAgeLanguageModelReadinessState State,
    string Summary,
    string? ActionId = null);

internal sealed record RekallAgeLanguageModelReadinessResult(
    string ProviderId,
    RekallAgeLanguageModelReadinessState State,
    string Code,
    string Summary,
    IReadOnlyList<RekallAgeLanguageModelReadinessCheck> Checks,
    IReadOnlyList<string> CompatibleModels,
    string? RecommendedActionId,
    bool CanRetry);

internal sealed record RekallAgeLanguageModelReadinessRequest(
    string ProviderId,
    string? PreferredModel,
    RekallAgeLanguageModelProviderSettings Settings);

internal interface IRekallAgeLanguageModelReadinessProbe
{
    ValueTask<RekallAgeLanguageModelReadinessResult> ProbeAsync(
        RekallAgeLanguageModelReadinessRequest request,
        CancellationToken cancellationToken);
}
```

Add injectable interfaces for executable lookup and Ollama process start so tests never launch software. For local default endpoints, distinguish missing executable from refused service. For remote/custom endpoints, do not require a local executable. Acquire provider leases through the existing catalog, list models, and require `SupportsCompletion is not false && SupportsTools is true`.

Normalize failures to the codes specified in the design. Use exception type/status code, not raw response content, to classify authentication, rate limit, outage, and network errors. Bound user-facing summaries and never include credentials.

- [ ] **Step 7: Run the two focused suites**

Run the commands from Steps 2 and 5.

Expected: both exact test classes pass.

- [ ] **Step 8: Commit readiness probing**

```powershell
git add src/Rekall.Age.Agent/LanguageModels/RekallAgeOllamaLanguageModelClient.cs src/Rekall.Age.Studio/RekallAgeLanguageModelReadiness.cs tests/Rekall.Age.Tests/Agent/OllamaLanguageModelClientTests.cs tests/Rekall.Age.Studio.Tests/LanguageModelReadinessProbeTests.cs
git commit -m "feat(studio): diagnose provider readiness"
```

---

### Task 4: Testable Wizard State Machine

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioLanguageModelSetupViewModel.cs`
- Create: `tests/Rekall.Age.Studio.Tests/LanguageModelSetupViewModelTests.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`
- Modify: `tests/Rekall.Age.Studio.Tests/LanguageModelProviderViewModelTests.cs`

**Interfaces:**
- Consumes: setup store from Task 1, credential store from Task 2, and readiness probe from Task 3.
- Produces: wizard steps, provider/model choices, readiness rows, commands, `CanFinish`, and `CompletedSetup`.
- Consumers: Tasks 5 and 6.

- [ ] **Step 1: Write wizard transition and completion tests**

Test:

- initial step is Welcome and Back is disabled;
- provider selection exposes only that provider's setup state;
- probe generation rejects a late result from a previous provider;
- missing Ollama exposes `open-ollama-download`, stopped service exposes `start-ollama`, and no models exposes `pull-qwen3.8:27b`;
- cloud key apply clears the caller-owned input, optionally writes the protected store, and never exposes it through properties;
- environment and remembered sources display only their source labels;
- removing a remembered key recomputes readiness;
- Set Up Later never writes complete state;
- Finish is false until a compatible selected model exists and all required checks pass;
- Finish persists completion/version/timestamp;
- shutdown cancellation stops active probes and pull operations.

Use fakes for all external services. Assert no public property string or readiness row contains sentinel secrets.

- [ ] **Step 2: Run focused wizard/view-model tests and verify failure**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~LanguageModelSetupViewModelTests"
```

Expected: compilation fails because the setup view model does not exist.

- [ ] **Step 3: Implement the wizard view model**

Define:

```csharp
internal enum RekallAgeStudioLanguageModelSetupStep
{
    Welcome,
    Provider,
    Configuration,
    Model,
    Summary
}

internal sealed record RekallAgeStudioLanguageModelReadinessRow(
    string Id,
    string StatusGlyph,
    string Label,
    string Detail,
    RekallAgeLanguageModelReadinessState State);
```

Expose `NextCommand`, `BackCommand`, `RetryCommand`, provider-specific remediation commands, `SetUpLaterCommand`, and `FinishCommand`. Keep secrets only as method arguments and local variables:

```csharp
internal Task ApplyApiKeyAsync(string providerId, string key, bool rememberSecurely)
```

Copy key data into provider settings only for the active session; clear references after the provider transition. Use a cancellation generation for every provider switch/probe. `CanFinish` requires `Readiness.State == Ready`, a selected compatible model, and successful settings-store availability.

- [ ] **Step 4: Add normal Studio restoration APIs and remove optimistic readiness**

In `RekallAgeStudioViewModel`, add:

```csharp
internal async Task RestoreLanguageModelSetupAsync(
    RekallAgeStudioLanguageModelSetup setup,
    string? openAiSessionKey,
    string? kimiSessionKey,
    CancellationToken cancellationToken)
```

Restore provider, wait for the generation-safe transition, select only a discovered compatible saved model, and restore reasoning effort. Replace the constructor status `ready. Refresh models.` with `Local Ollama selected; setup not checked.`. Preserve existing fixed-client test constructors. Clear both `_sessionOpenAiApiKey` and `_sessionKimiApiKey` during shutdown.

- [ ] **Step 5: Run the focused provider and wizard tests**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~LanguageModelSetupViewModelTests|FullyQualifiedName~StudioViewModelTests|FullyQualifiedName~LanguageModelProviderViewModelTests"
```

Expected: all selected tests pass, including existing provider lifecycle and redaction cases.

- [ ] **Step 6: Commit wizard state**

```powershell
git add src/Rekall.Age.Studio/RekallAgeStudioLanguageModelSetupViewModel.cs src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs tests/Rekall.Age.Studio.Tests/LanguageModelSetupViewModelTests.cs tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs tests/Rekall.Age.Studio.Tests/LanguageModelProviderViewModelTests.cs
git commit -m "feat(studio): add onboarding state machine"
```

---

### Task 5: Accessible Five-Step Wizard Window

**Files:**
- Create: `src/Rekall.Age.Studio/LanguageModelSetupWindow.xaml`
- Create: `src/Rekall.Age.Studio/LanguageModelSetupWindow.xaml.cs`
- Create: `tests/Rekall.Age.Studio.Tests/LanguageModelSetupWindowTests.cs`
- Modify: `tests/Rekall.Age.Studio.Tests/WpfApplicationTestCollection.cs`

**Interfaces:**
- Consumes: `RekallAgeStudioLanguageModelSetupViewModel` from Task 4.
- Produces: an owned modal setup window returning completed, deferred, or closed-incomplete state.
- Consumers: Task 6.

- [ ] **Step 1: Write focused WPF/source tests**

Assert the XAML/code-behind contains:

- five named step panels bound to the step enum;
- provider cards grouped Local/API/Account;
- provider-specific panels controlled only by the selected provider;
- masked PasswordBoxes for OpenAI and Kimi;
- unchecked `Remember securely on this PC` controls by default;
- Back, Next, Retry, Set up later, and Finish buttons with non-clipping padding/minimum widths;
- scrollable content and no fixed height that clips at 200% DPI;
- `IsDefault`/`IsCancel` behavior appropriate to each step;
- readiness rows with glyph and text, not color alone;
- code-behind that immediately clears PasswordBox values and does not store them in fields;
- GGUF file filter exactly `GGUF models (*.gguf)|*.gguf`.

Add an STA smoke test that constructs the window with fakes, confirms it has an owner, and exercises provider-step visibility.

- [ ] **Step 2: Run the exact window tests and verify failure**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~LanguageModelSetupWindowTests"
```

Expected: compilation/source assertions fail because the window files do not exist.

- [ ] **Step 3: Implement the WPF wizard**

Use an owned `Window` with `SizeToContent="Height"`, `MaxHeight` bound conservatively to the work area, minimum width sufficient for provider cards, and a `ScrollViewer`. Follow `CreateProjectDialog.xaml` resources and spacing rather than introducing a separate visual language.

The password handlers must follow this shape:

```csharp
private async void OnApplyOpenAiKeyClick(object sender, RoutedEventArgs e)
{
    var key = OpenAiApiKeyInput.Password;
    OpenAiApiKeyInput.Clear();
    await ViewModel.ApplyApiKeyAsync("openai", key, RememberOpenAiKey.IsChecked == true);
}
```

Do not bind secret text. Native browse and official provider-page launches remain user-triggered actions. Closing/Escape maps to incomplete deferment and cancels active wizard work.

- [ ] **Step 4: Run the exact window tests**

Run the command from Step 2.

Expected: all `LanguageModelSetupWindowTests` pass.

- [ ] **Step 5: Commit the wizard UI**

```powershell
git add src/Rekall.Age.Studio/LanguageModelSetupWindow.xaml src/Rekall.Age.Studio/LanguageModelSetupWindow.xaml.cs tests/Rekall.Age.Studio.Tests/LanguageModelSetupWindowTests.cs tests/Rekall.Age.Studio.Tests/WpfApplicationTestCollection.cs
git commit -m "feat(studio): add first-launch AI setup wizard"
```

---

### Task 6: Startup Coordinator, Settings Entry, and Recovery Banner

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioLanguageModelSetupCoordinator.cs`
- Create: `tests/Rekall.Age.Studio.Tests/StudioLanguageModelSetupCoordinatorTests.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Modify: `tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs`
- Modify: `tests/Rekall.Age.Studio.Tests/StudioProjectDialogTests.cs`

**Interfaces:**
- Consumes: setup store, credential store, readiness probe, wizard view model/window, and `RekallAgeStudioViewModel.RestoreLanguageModelSetupAsync`.
- Produces: `InitializeAsync`, `ShowSetupAsync`, `IsSetupIncomplete`, and setup-status text for MainWindow.
- Consumers: Task 7 and normal Studio startup.

- [ ] **Step 1: Write startup sequencing tests**

Cover:

- missing settings opens the wizard after the main window has loaded;
- incomplete settings reopen every ordinary launch;
- completed and healthy settings skip the wizard;
- completed but blocked sanity opens the correct recovery state;
- corrupt/future settings open incomplete setup;
- automation bypasses the wizard;
- secure remembered key wins over environment, which wins over no key; session entry applies only after explicit wizard input;
- Setup Later leaves `IsSetupIncomplete` true;
- Settings always reopens setup;
- a command-line/create/open/example project still selects World after setup handling;
- setup initialization occurs before the unconditional model refresh.

- [ ] **Step 2: Run coordinator and startup tests and verify failure**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~StudioLanguageModelSetupCoordinatorTests|FullyQualifiedName~StudioProjectDialogTests"
```

Expected: compilation fails because the coordinator does not exist.

- [ ] **Step 3: Implement the coordinator**

Define:

```csharp
internal sealed class RekallAgeStudioLanguageModelSetupCoordinator
{
    public bool IsSetupIncomplete { get; private set; }
    public string SetupStatusText { get; private set; } = "AI setup not checked.";

    public Task InitializeAsync(Window owner, RekallAgeStudioViewModel studio, CancellationToken cancellationToken);
    public Task ShowSetupAsync(Window owner, RekallAgeStudioViewModel studio, CancellationToken cancellationToken);
}
```

Load settings and credentials, perform a short readiness probe, restore the provider/model, then decide whether to show the owned wizard. Keep window creation behind an injected factory in tests. A completed-but-offline setup is blocked for authoring but does not prevent project initialization or manual editing.

- [ ] **Step 4: Integrate startup without breaking World selection**

In `MainWindow.OnLoaded`, use this order:

```csharp
_layout = await _layoutStore.LoadAsync(CancellationToken.None);
ApplyLayout(_layout);
await _languageModelSetupCoordinator.InitializeAsync(this, _viewModel, CancellationToken.None);
await _viewModel.InitializeAsync(projectRoot, sceneName);
if (_viewModel.HasProject) SelectWorkspace("World");
```

Remove the unconditional post-load refresh when the coordinator has already performed discovery. Keep refresh for a completed ready provider only.

- [ ] **Step 5: Add Settings and non-modal recovery affordances**

Add a top-level `Settings` menu with `Language Model Setup…`. Add compact Author and World banners bound to coordinator state: `AI setup incomplete · Configure`. The button opens the wizard; it does not switch provider by itself. Ensure menu and banner buttons use minimum widths/padding and wrap where necessary.

- [ ] **Step 6: Run the focused coordinator/layout/startup tests**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~StudioLanguageModelSetupCoordinatorTests|FullyQualifiedName~StudioProjectDialogTests|FullyQualifiedName~StudioLayoutTests"
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit startup integration**

```powershell
git add src/Rekall.Age.Studio/RekallAgeStudioLanguageModelSetupCoordinator.cs src/Rekall.Age.Studio/MainWindow.xaml src/Rekall.Age.Studio/MainWindow.xaml.cs tests/Rekall.Age.Studio.Tests/StudioLanguageModelSetupCoordinatorTests.cs tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs tests/Rekall.Age.Studio.Tests/StudioProjectDialogTests.cs
git commit -m "feat(studio): run provider setup on first launch"
```

---

### Task 7: Shared Author Workflow and Guided Remediation Actions

**Files:**
- Modify: `src/Rekall.Age.Studio/AuthorWorkspace.xaml`
- Modify: `src/Rekall.Age.Studio/AuthorWorkspace.xaml.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioLanguageModelSetupViewModel.cs`
- Modify: `tests/Rekall.Age.Studio.Tests/LanguageModelWorkspaceSourceTests.cs`
- Modify: `tests/Rekall.Age.Studio.Tests/LanguageModelSetupViewModelTests.cs`

**Interfaces:**
- Consumes: coordinator and readiness/remediation actions from Tasks 3–6.
- Produces: consistent recovery behavior in Author and the wizard.

- [ ] **Step 1: Write remediation and shared-flow tests**

Add exact tests for:

- `Open Ollama download page` uses the official HTTPS URL and only on user command;
- `Start Ollama` invokes the injected process launcher without a shell command string;
- `Download qwen3.8:27b` invokes `ollama pull` with separate argument-list entries, streams bounded progress, supports cancellation, and refreshes readiness on success;
- GGUF selection cannot proceed until Ollama prerequisites pass;
- Author's `Fix setup` routes to the coordinator rather than duplicating provider logic;
- existing provider-specific Author controls remain mutually exclusive;
- Codex buttons remain absent when another provider is selected;
- OpenAI/Kimi remembered-source labels never display key values;
- both session keys clear on shutdown.

- [ ] **Step 2: Run focused remediation tests and verify failure**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~LanguageModelWorkspaceSourceTests|FullyQualifiedName~LanguageModelSetupViewModelTests"
```

Expected: new assertions fail because remediation commands and shared routing are incomplete.

- [ ] **Step 3: Implement explicit remediation services**

Add injectable URI and Ollama-process launchers. Use `ProcessStartInfo.ArgumentList`:

```csharp
var startInfo = new ProcessStartInfo
{
    FileName = ollamaExecutable,
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};
startInfo.ArgumentList.Add("pull");
startInfo.ArgumentList.Add("qwen3.8:27b");
```

Do not run an installer automatically. The installer action opens the official Ollama page; the user installs it and presses Retry. Starting an already installed local service and pulling a specifically displayed model are explicit wizard actions.

- [ ] **Step 4: Route Author through shared setup status**

Keep its compact provider controls, but replace raw generic Ollama failures with readiness summaries and a `Fix setup` action. Key application continues to clear PasswordBoxes immediately. Successful explicit changes update persisted non-secret selection only through the coordinator.

- [ ] **Step 5: Run focused provider/remediation tests**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~LanguageModelWorkspaceSourceTests|FullyQualifiedName~LanguageModelSetupViewModelTests|FullyQualifiedName~StudioViewModelTests|FullyQualifiedName~LanguageModelProviderViewModelTests"
```

Expected: all selected tests pass.

- [ ] **Step 6: Commit shared remediation**

```powershell
git add src/Rekall.Age.Studio/AuthorWorkspace.xaml src/Rekall.Age.Studio/AuthorWorkspace.xaml.cs src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs src/Rekall.Age.Studio/RekallAgeStudioLanguageModelSetupViewModel.cs tests/Rekall.Age.Studio.Tests/LanguageModelWorkspaceSourceTests.cs tests/Rekall.Age.Studio.Tests/LanguageModelSetupViewModelTests.cs
git commit -m "feat(studio): guide provider setup recovery"
```

---

### Task 8: Documentation, Live First-Launch QA, and Focused Acceptance Gate

**Files:**
- Modify: `src/Rekall.Age.Studio/Documentation/Rekall-AGE-Documentation.html`
- Modify: `tests/Rekall.Age.Studio.Tests/LanguageModelWorkspaceSourceTests.cs`
- Modify: `tests/Rekall.Age.Studio.Tests/LanguageModelSetupWindowTests.cs`

**Interfaces:**
- Consumes: the complete onboarding feature from Tasks 1–7.
- Produces: user documentation and final acceptance evidence.

- [ ] **Step 1: Add documentation assertions**

Require the documentation to explain:

- when and why the wizard opens;
- local versus cloud privacy implications;
- Ollama missing/stopped/no-model recovery;
- `qwen3.8:27b` as the preferred Ollama default;
- Kimi/OpenAI session, environment, and remembered credential sources;
- how to remove remembered keys;
- Codex-only sign-in controls;
- `Settings → Language Model Setup…`;
- Set Up Later and the recovery banner;
- compatible tool-capable model requirement.

- [ ] **Step 2: Run the focused documentation/source tests and verify failure**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~LanguageModelWorkspaceSourceTests|FullyQualifiedName~LanguageModelSetupWindowTests"
```

Expected: documentation assertions fail until the HTML is updated.

- [ ] **Step 3: Update the single-file Studio documentation**

Add a first-launch section near the existing provider documentation and cross-link it from troubleshooting. Use plain language and explicitly state that remembered keys are protected for the current Windows user and are not stored in projects or normal settings files.

- [ ] **Step 4: Run all onboarding-focused tests**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~LanguageModelSetupStoreTests|FullyQualifiedName~StudioCredentialStoreTests|FullyQualifiedName~LanguageModelReadinessProbeTests|FullyQualifiedName~LanguageModelSetupViewModelTests|FullyQualifiedName~LanguageModelSetupWindowTests|FullyQualifiedName~StudioLanguageModelSetupCoordinatorTests|FullyQualifiedName~LanguageModelWorkspaceSourceTests|FullyQualifiedName~StudioViewModelTests|FullyQualifiedName~LanguageModelProviderViewModelTests|FullyQualifiedName~StudioProjectDialogTests|FullyQualifiedName~StudioLayoutTests"
```

Expected: all selected Studio tests pass.

- [ ] **Step 5: Run the focused Ollama client tests and Studio build**

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~OllamaLanguageModelClientTests"
dotnet build src\Rekall.Age.Studio\Rekall.Age.Studio.csproj --no-restore
```

Expected: tests pass; build succeeds with zero warnings and zero errors.

- [ ] **Step 6: Perform live first-launch QA with an isolated profile path**

Launch Studio with `REKALL_AGE_STUDIO_SETUP_ROOT` set to a fresh absolute temporary directory so QA does not delete or overwrite the user's actual setup. Verify visually:

- wizard opens after the main window;
- every provider page fits without clipped text/buttons;
- keyboard navigation reaches every action;
- Ollama on this machine reports executable/API/version/model readiness and selects `qwen3.8:27b`;
- Set Up Later leaves the visible incomplete banner;
- Finish closes the wizard, restart skips it, and Settings reopens it;
- opening Aetherfall Citadel still selects World;
- no unrelated Codex controls appear on Ollama;
- no white flashing/detached Vulkan window occurs while the wizard opens or closes.

Capture a local QA screenshot under the isolated temporary setup directory for visual verification. Do not replace any Steam store screenshot in this task.

- [ ] **Step 7: Inspect logs and persistence for secret leakage**

Use a sentinel test key with fake providers, then inspect the isolated setup JSON, protected credential directory, Studio status/validation collections, and test log. Assert the sentinel appears nowhere except inside the decrypted fake-store assertion path.

- [ ] **Step 8: Commit documentation and final acceptance changes**

```powershell
git add src/Rekall.Age.Studio/Documentation/Rekall-AGE-Documentation.html tests/Rekall.Age.Studio.Tests/LanguageModelWorkspaceSourceTests.cs tests/Rekall.Age.Studio.Tests/LanguageModelSetupWindowTests.cs
git commit -m "docs(studio): document first-launch AI setup"
```

- [ ] **Step 9: Verify clean integration and push**

```powershell
git diff --check
git status --short
git push
```

Expected: no unstaged/untracked implementation files, no whitespace errors, and the tested commits are present on the upstream branch.
