# Studio Agent Authoring Workspace Design

**Status:** Approved for implementation (user explicitly approved the direction and pre-approved in-scope repairs discovered during the continued benchmark).

## Purpose

Make ordinary-language game creation the clearest first-class workflow in
Rekall AGE Studio. A user who creates or opens a project must be able to choose
an available language-model provider, describe a game, run the agent, follow
its progress, and reach playable evidence without understanding Studio's
diagnostic panel layout or provider internals.

This work repairs defects observed during a real empty-project benchmark on
2026-08-29: the AI Agent surface was buried in the bottom Output panel, its
prompt and Run button were below the visible window, the Authoring preset did
not make it usable, unrelated OpenAI and Codex controls appeared for Ollama,
the provider selector exposed a descriptor record, the model selector accepted
arbitrary text, and a missing configured default left the workflow disabled
despite valid discovered models.

## Product boundaries

- Studio remains a client of the existing provider-neutral project-agent and
  engine command contracts. It does not author game content itself.
- Provider-specific authentication remains in memory and uses the existing
  provider catalog and runner lifecycle.
- The agent must still complete the generic gameplay checkpoint and delivery
  workflow; this change does not weaken acceptance gates or fabricate success.
- Diagnostics remain inspectable, but machine-oriented error detail must not
  dominate the primary authoring interaction.
- Game behavior remains agent-authored and engine-generic.

## Considered approaches

### 1. Keep AI Agent as an Output tab

Increase the Authoring preset's Output height and add conditional visibility.
This is the smallest patch, but it preserves the mistaken hierarchy that game
authoring is diagnostic output. It also continues to compete with viewport,
hierarchy, and inspector space and is fragile at smaller window sizes.

### 2. Use a modal or step-by-step wizard

A wizard could simplify initial provider setup and prompt entry. It becomes a
poor fit once an agent is running: transcript, viewport changes, controls, and
recovery need to remain simultaneously inspectable. A modal also makes the
main workflow feel secondary and interrupts iteration.

### 3. Add a top-level Author workspace (selected)

Promote ordinary-language authoring beside World and Modeling. Give it a
responsive configuration/task column and a progress/transcript column, with a
compact project context header and an explicit path back to World. This makes
the product hierarchy match its purpose and provides stable room for long
tasks, errors, and follow-up runs.

## Interaction design

### Workspace hierarchy

The top-level workspace selector becomes `Author`, `World`, and `Modeling`,
with Author first. Creating a project switches to Author automatically. Opening
an existing project preserves the user's current workspace unless no workspace
preference exists. The Authoring layout preset selects Author and uses sensible
panel visibility without depending on the bottom Output panel.

The existing AI Agent Output tab is removed. Agent transcript and progress live
only in Author, while validation/runtime/delivery diagnostics remain in Output.
This avoids two competing instances of the same controls and state.

### Responsive authoring surface

`AuthorWorkspace.xaml` owns a two-column layout:

- Left: project/scene context, provider card, model and reasoning selectors,
  large ordinary-language task editor, and always-visible Run/Cancel actions.
- Right: current run status, bounded progress summary, live transcript, and
  shortcuts to World, Validation, and Delivery evidence.

At narrow widths the two columns stack in a vertical `ScrollViewer`. The task
editor has a useful minimum height, while the action row remains visible at
ordinary 1120x700 minimum window dimensions. No required control may depend on
dragging a splitter or choosing Debug layout.

### Progressive provider disclosure

Provider selection uses the descriptor's `DisplayName`; internal record
representations never appear. Provider-specific setup is mutually exclusive:

- Ollama: endpoint/readiness summary and Refresh Models.
- OpenAI API: session-key input/apply and API readiness.
- Codex: authentication state and Sign In/Cancel Sign-In.

Codex buttons are collapsed unless Codex is selected. The OpenAI key is
collapsed unless OpenAI API is selected. Reasoning effort is shown only when
the selected provider supports it; otherwise it is omitted or clearly marked
as provider-managed. All visibility decisions are exposed as inspectable
view-model properties and tested independently of WPF rendering.

### Model readiness and fallback

The model selector is non-editable and can only select an ID returned by the
active provider. Provider refresh follows this order:

1. Keep the previous selection when it still exists for the same provider.
2. Select the provider's configured default when available.
3. Otherwise select the first usable discovered chat model and show a concise
   warning that the configured default was unavailable and which fallback was
   selected.
4. Disable Run only when discovery returns no usable model or provider setup is
   incomplete.

The fallback is a usability recovery, not a hidden success: the structured
diagnostic code and requested/resolved facts remain in Validation and logs.
Embedding-only models must not be selected as chat fallbacks when capability
metadata identifies them as such. Where provider metadata cannot distinguish
capability, preserve discovery order and provider policy rather than applying
name folklore in Studio.

### Empty-project state

An empty scene must show a neutral inspector prompt such as "Select an entity
to inspect components." It must not display the first registered component or
property as though it were selected. Project path fields show the full value
through tooltip/selection and provide an explicit Browse action; truncation
must not be the only way to understand the chosen root.

## State and code structure

- `AuthorWorkspace.xaml`: the dedicated responsive authoring UI.
- `MainWindow.xaml/.cs`: top-level Author tab, project-create navigation, and
  routing shortcuts; remove the old AI Agent Output tab.
- `RekallAgeStudioViewModel.cs`: provider-specific visibility/readiness
  properties, validated model selection, fallback policy, concise status, and
  neutral inspector state.
- `RekallAgeStudioLayout.cs`: versioned workspace/preset changes and migration
  from persisted `AI Agent` output-tab layouts to Author.
- Existing provider catalog/runner types remain authoritative and unchanged
  unless capability metadata is genuinely missing from the generic contract.

The UI binds to explicit semantic properties such as `IsOllamaSelected`,
`IsOpenAiSelected`, `IsCodexSelected`, `HasUsableLanguageModel`, and
`ProviderStatusKind`. It does not infer provider behavior from control state or
duplicate provider IDs throughout XAML converters.

## Failure handling

- Provider discovery failure leaves the task text intact and presents a short
  actionable message beside Retry/Refresh.
- Full stable diagnostic codes and bounded facts remain in Validation, Studio
  logs, and the agent transcript when relevant.
- Provider changes cancel and await active discovery/run work exactly as the
  current lifecycle contract requires.
- Switching providers clears models from the previous provider and never
  displays stale provider-specific controls.
- A failed fallback never invents a model ID; it leaves Run disabled with the
  exact recovery action.

## Verification and acceptance

Use narrow TDD selections during implementation:

- View-model tests prove mutually exclusive provider surfaces, stable display
  names, validated model selection, default fallback selection, preservation of
  a still-valid prior model, no-model failure, and neutral empty inspector.
- Layout tests prove Author is first-class, Authoring selects it, old persisted
  layouts migrate safely, and the Output tab set no longer contains AI Agent.
- XAML/source tests prove provider controls have conditional visibility, the
  model selector is non-editable, and Run/Cancel live in `AuthorWorkspace`.
- A real Windows pass at 1120x700 and the normal maximized size proves prompt,
  Run, Cancel, provider, model, status, and transcript are reachable without a
  splitter adjustment or Debug layout.

After the focused tests and warning-free Studio build pass, resume the same
`Neon Orchard` empty-project benchmark through the visible Author workspace.
Acceptance requires the agent to create a nonblank game, attach representative
input and an agent-owned `Game.*` component, pass a strict deterministic
`rekall.runtime.inspect_scene` input-frame assertion showing a nonzero transform
delta or changed agent-owned component property, package and audit the game,
and launch the produced player for visual/playable inspection. Any failure is
retained as evidence and repaired at the generic engine/Studio contract that
caused it before rerunning the intended assertion.

## Explicit non-goals

- Replacing the provider catalog or project-agent orchestration.
- Adding genre-specific game templates or built-in gameplay behavior.
- Redesigning World or Modeling beyond the empty-inspector and navigation
  changes required by this authoring flow.
- Weakening gameplay, package, audit, or visual acceptance gates.
