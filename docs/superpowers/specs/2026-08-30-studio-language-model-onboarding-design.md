# Studio Language-Model Onboarding Design

## Purpose

Rekall AGE Studio must make its primary interaction model—describing a game in ordinary language—usable on first launch without assuming that a language-model runtime, account, credential, or compatible model is already configured.

Studio currently selects Local Ollama and `qwen3.8:27b` optimistically, refreshes models after the main window loads, and keeps provider/model choices only for the current process. A missing Ollama runtime or stopped service becomes a generic provider failure. OpenAI and Kimi credentials can be entered in Author, but are session-only, and there is no persistent setup state or guided recovery.

This design introduces a readiness-first onboarding wizard, reusable provider diagnostics, versioned non-secret preferences, and opt-in Windows-protected credential storage.

## Goals

- Guide a new user from an unconfigured machine to one verified, usable authoring provider and model.
- Detect and distinguish installation, service, endpoint, authentication, model, and tool-capability failures.
- Persist the selected provider, model, reasoning effort, and onboarding completion state.
- Offer explicit opt-in secure persistence for OpenAI and Kimi credentials.
- Never expose credentials in project files, ordinary settings JSON, status text, logs, diagnostics, exception details, or test output.
- Allow ordinary non-AI editing when setup is dismissed, while keeping incomplete setup visible and recoverable.
- Reuse the same readiness and remediation contracts from first launch, Settings, Author, and later provider failures.
- Preserve the existing behavior that an opened project lands on the World workspace.

## Non-goals

- The engine will not choose, install, or author content through a provider without a user-selected action.
- The wizard will not replace the Author workspace or the compact World prompt.
- The first version will not build a general-purpose Studio preferences framework.
- The first version will not store cloud credentials in plaintext, ordinary JSON, environment variables, or project data.
- The wizard will not make unsupported models appear compatible or weaken the existing tool-capability requirement.

## User Experience

The wizard is an owned, centered Studio window using the existing dark dialog language. It is keyboard accessible, scrollable at high DPI, and sized so buttons and status text do not clip at 125–200% scaling.

### Step 1: Welcome

Explain that Studio uses an AI provider to turn game descriptions into inspectable engine changes. State that local providers keep prompts on the machine, while cloud providers send prompts and relevant authoring context to their service and may require a paid account.

Actions:

- `Get started`
- `Set up later`

`Set up later` closes the wizard without marking setup complete. Studio remains usable for manual editing and shows a non-modal `AI setup incomplete · Configure` affordance in Author and World. The wizard opens again on the next ordinary launch.

### Step 2: Choose Provider

Present provider cards in three groups:

- Local: Local Ollama; Local GGUF via Ollama.
- API: Kimi API; OpenAI API.
- Account: Codex via ChatGPT.

Each card states whether prompts leave the PC, what account or runtime it requires, and whether separate provider charges may apply. Only the selected provider's setup controls are shown. Codex sign-in controls never appear for Ollama, GGUF, Kimi, or OpenAI.

### Step 3: Configure and Test

#### Ollama

Run bounded checks in this order:

1. Resolve whether the `ollama` executable is installed.
2. Validate the configured endpoint, defaulting to `http://127.0.0.1:11434`.
3. Confirm the endpoint is an Ollama service and obtain its version.
4. Discover installed models.
5. Inspect model capabilities and find at least one completion- and tool-capable model.
6. Prefer `qwen3.8:27b` when installed; otherwise show valid alternatives without inventing model IDs.

Remediation is specific to the failed check:

- Runtime missing: explain the requirement, provide `Open Ollama download page`, and offer `Retry` after installation.
- Installed but service stopped: offer `Start Ollama` and `Retry`.
- Custom or remote endpoint unreachable: allow editing the endpoint and retrying.
- Non-Ollama response: report an endpoint mismatch rather than an installation failure.
- No models: offer a guided `Download qwen3.8:27b` action with progress and cancellation.
- No tool-capable models: explain the authoring requirement and recommend downloading a compatible model.

Downloads must show expected model identity, available size information when known, third-party ownership, and that disk/network use may be substantial. Closing Studio cancels active probes or downloads cleanly.

#### Local GGUF

Run the same Ollama prerequisite checks first. Once Ollama is ready, let the user select a `.gguf` file, reuse the existing validated importer, refresh models, and verify the imported model's authoring capability. Failed import, missing file, invalid header, or incompatible model each remains a distinct actionable state.

#### OpenAI and Kimi

Detect credentials in this priority order:

1. A remembered Windows-protected credential.
2. A supported environment variable.
3. A key entered in the wizard for the current session.

The wizard never displays an existing credential value. It shows its source as `Remembered securely`, `Configured from environment`, or `Session only`.

New key entry uses a masked control and provides an explicit `Remember securely on this PC` checkbox. The checkbox is opt-in. When enabled, the credential is stored through the Windows credential service under a Rekall AGE-specific target. When disabled, the key remains in memory only. Applying a key clears the UI control immediately and validates the credential through bounded model discovery.

The user can remove a remembered credential. Removing it deletes only that Rekall AGE credential target and immediately recomputes readiness using any remaining environment or session source.

Diagnostics distinguish missing, rejected/expired, insufficient permission, rate-limited, provider outage, and local network failure without including response bodies or credential fragments that could contain secrets.

#### Codex

Reuse the current Codex-managed authentication flow. Show sign-in progress, cancel, retry, authenticated state, and model availability. A successful account session is not complete until the required authoring model is available. The wizard does not copy or persist Codex credentials.

### Step 4: Choose Model

List only models that can perform completion and satisfy the engine's tool-use contract. Preserve a separate warning when capability is unknown rather than claiming compatibility. Select the provider default when available; otherwise select a discovered compatible fallback and explain the substitution.

Persist the chosen model and reasoning effort. If the model is later removed or becomes unavailable, normal startup remains non-blocking but shows the setup recovery affordance and opens the wizard when the stored setup can no longer pass its bounded sanity check.

### Step 5: Readiness Summary

Show a status row for:

- Provider selected.
- Runtime or authentication ready.
- Endpoint reachable when applicable.
- Compatible model selected.
- Required authoring/tool capability verified.
- Preferences can be saved.

Rows use icon, label, and text; color is not the only status signal. `Finish` is enabled only when every required check passes. Finishing persists non-secret preferences and the current onboarding schema version.

## Startup and Recovery Flow

Ordinary startup uses this sequence:

1. Show `MainWindow` and load the Studio layout.
2. Load versioned language-model setup preferences.
3. Restore the provider, model, reasoning effort, endpoint preferences, and remembered credential handles.
4. Run a short, cancellable sanity check for the selected configuration.
5. Show the wizard if preferences are absent, from an unsupported future/corrupt version, explicitly incomplete, or fail readiness.
6. Initialize an optional command-line project and select World after successful project loading.
7. Start normal preview and provider refresh activity.

Automation mode skips the interactive wizard and continues to rely on explicit automation arguments and diagnostics.

When a previously completed setup fails at runtime, Studio does not silently change providers or reset completion. It shows an actionable banner and opens the wizard on the next launch unless the user repairs the configuration first.

Add `Settings → Language Model Setup…` to reopen the wizard at any time. The existing provider controls in Author remain available but bind to the same setup/readiness services so behavior cannot diverge.

## Architecture

### Non-secret preferences

Add a versioned `RekallAgeStudioLanguageModelSetup` record and `IRekallAgeStudioLanguageModelSetupStore`. Store it atomically under the same canonical LocalApplicationData root as the Studio layout, for example:

`%LOCALAPPDATA%\Rekall\AGE\Studio\language-model-setup-v1.json`

Fields:

- Schema version.
- Completion state.
- Provider ID.
- Model ID.
- Reasoning effort.
- Optional provider endpoint overrides.
- Last successful check timestamp.
- Last successful readiness version.

No property may contain an API key, token, password, authorization header, or provider response body. Unknown, corrupt, or future-version files normalize to incomplete defaults. Writes use a temporary sibling file and atomic replacement so an interrupted write cannot destroy the previous valid state.

### Secure credentials

Add `IRekallAgeStudioCredentialStore` with provider-scoped read, write, and remove operations. The production Windows implementation uses a current-user protected credential mechanism and explicit Rekall AGE target names. The interface returns opaque credential material only to the provider setup path and never to inspectable view-model properties.

Secure-store denial or corruption is an actionable wizard failure. The user can continue with a session-only key instead. Tests use an in-memory fake and assert that serialized preferences and observable UI state never contain secret values.

### Readiness probes

Add `IRekallAgeLanguageModelReadinessProbe` returning a typed result rather than throwing transport-specific exceptions into the UI.

Each result contains:

- Provider ID.
- Overall state: ready, warning, or blocked.
- Stable diagnostic code.
- User-facing summary.
- Ordered check results.
- Available compatible models.
- Recommended remediation actions.
- Whether retry is meaningful.

The probe composes existing catalog/client operations but adds Ollama executable, service identity, version, and model prerequisite checks. It normalizes raw HTTP, JSON, authentication, and cancellation outcomes into stable diagnostics while preserving detailed exceptions only in redacted internal logs.

Representative codes include:

- `REKALL_ONBOARDING_OLLAMA_RUNTIME_MISSING`
- `REKALL_ONBOARDING_OLLAMA_SERVICE_STOPPED`
- `REKALL_ONBOARDING_OLLAMA_ENDPOINT_UNREACHABLE`
- `REKALL_ONBOARDING_OLLAMA_ENDPOINT_INVALID`
- `REKALL_ONBOARDING_NO_MODELS`
- `REKALL_ONBOARDING_NO_TOOL_MODEL`
- `REKALL_ONBOARDING_API_KEY_REQUIRED`
- `REKALL_ONBOARDING_AUTH_REJECTED`
- `REKALL_ONBOARDING_PROVIDER_RATE_LIMITED`
- `REKALL_ONBOARDING_PROVIDER_UNAVAILABLE`

### Wizard state

Add a testable `RekallAgeStudioLanguageModelSetupViewModel` that owns navigation, selected provider/model, readiness rows, cancellation generations, remediation commands, and the completion predicate. XAML binds to this state. Code-behind is limited to extracting and immediately clearing `PasswordBox` values and opening native file pickers.

Provider switches cancel and await prior probes before replacing provider leases, following the existing generation-safe provider transition behavior. Late results cannot modify the newly selected provider's state.

### Main view-model integration

Stop unconditionally labeling Ollama ready during `RekallAgeStudioViewModel` construction. Restore the selected provider before acquiring the normal runner. A runner becomes authoring-ready only after successful acquisition and compatible model discovery.

The existing Author provider controls call the same setup coordinator used by the wizard. OpenAI and Kimi session credentials are both cleared during shutdown. Persistent preferences update only after an explicit successful wizard finish or an explicit saved change from Settings.

## Error Handling and Safety

- Every asynchronous check and download accepts cancellation.
- Closing the wizard cancels wizard-owned work but does not terminate Studio.
- Closing Studio cancels and awaits active readiness operations through the existing shutdown coordination.
- Provider responses, process output, and exception messages are bounded and redacted before logging or display.
- Credential values never participate in record `ToString`, equality diagnostics, serialization, command lines, URLs, or status text.
- Environment credentials are detected but never copied into the secure store unless the user explicitly enters a new key and enables remembering it.
- Multiple Studio instances use atomic preference writes; the latest explicit successful save wins. Credential operations are provider-scoped and idempotent.
- A port occupied by a non-Ollama service reports endpoint invalid, not service stopped.
- Offline startup allows manual editing and does not mark an unverified configuration complete.

## Testing

Use focused tests only.

### Setup-store tests

- Missing, corrupt, unsupported-future, and older-version settings normalize safely.
- Atomic round-trip preserves provider/model/reasoning/endpoint state.
- Incomplete and completed state obey the schema-version contract.
- Serialized JSON contains no secret-shaped fields or supplied secret values.

### Credential-store tests

- Provider targets are isolated.
- Remember, retrieve, replace, and remove work for the current user.
- Access denial and corrupted payloads return actionable failures without secret leakage.
- Session-only entry never calls the persistent store.

### Readiness tests

- Ollama: runtime missing, service stopped, endpoint unreachable, wrong service, no models, no tool-capable models, default missing with valid fallback, recommended model ready, and cancellation.
- GGUF: every Ollama prerequisite plus invalid file, failed import, incompatible import, and success.
- OpenAI/Kimi: missing, environment, remembered, session-only, rejected, rate-limited, outage, network failure, and success.
- Codex: authentication required, sign-in cancellation/timeout, authenticated with missing model, and ready.
- Diagnostics and logs never include supplied keys.

### Wizard tests

- First launch opens the wizard.
- `Set up later` leaves setup incomplete and causes it to reopen next launch.
- Successful readiness plus compatible model enables Finish and persists completion.
- Completed healthy setup skips the wizard.
- Completed but failed sanity reopens it with the correct recovery step.
- Provider switching rejects stale probe results.
- Model removal invalidates readiness without silently selecting an incompatible model.
- Settings reopens the wizard.
- Automation bypasses the wizard.

### UI and regression tests

- Provider-specific controls are mutually exclusive.
- Tab order, Enter/Escape behavior, status text, button sizing, scrolling, and 125–200% DPI remain usable.
- Secret text never appears in visual tree status controls after application.
- Command-line open, Create, Open, and Examples still select World after project initialization.
- The existing Author provider workflow continues to function through the shared coordinator.

## Acceptance Criteria

The feature is complete when these scenarios are demonstrated:

1. A clean profile with no Ollama receives an accurate installation path and can continue editing without AI.
2. An installed but stopped Ollama receives a start/retry path, not a generic failure.
3. A clean Ollama installation with no models can download or otherwise install `qwen3.8:27b`, verify tool capability, finish setup, restart Studio, and skip the wizard.
4. A Kimi or OpenAI user can enter a key, opt into secure remembering, validate it, restart Studio, and author without re-entering it.
5. Removing a remembered key makes the provider incomplete without leaking or silently retaining the old credential.
6. Codex controls appear only when Codex is selected and complete only after account and model readiness.
7. Dismissing incomplete setup never blocks World, Inspector, Code, Modeling, or manual project editing.
8. Completed setup that later becomes invalid produces an actionable recovery banner and wizard state.
9. Focused automated tests prove persistence, redaction, provider diagnostics, wizard transitions, and startup ordering.
