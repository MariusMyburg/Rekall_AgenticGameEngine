# AI Game-Creation Vertical Slice Implementation Plan

> Follow test-driven development and verify each checkpoint before updating
> `docs/production/PROGRESS.md`.

**Goal:** Make Rekall AGE usable as an AI-first game engine through one coherent
create, author, inspect, edit, render, play, and verify desktop workflow.

**Design:** `docs/superpowers/specs/2026-08-20-ai-game-creation-vertical-slice-design.md`

### Task 1: Command-backed workbench session

- [x] Add failing session tests for create/open/reload, scene switching,
  selection, command execution, transaction append, structured failures, and
  model refresh.
- [x] Implement a UI-independent async session in `Rekall.Age.Editor` over the
  canonical command registry and workbench model builder.
- [ ] Add validate, capture, undo, redo, play, and stop lifecycle operations;
  prove cancellation and failed-command state preservation.
- [ ] Run editor, vertical-slice, world, runtime, playback, and rendering tests.

### Task 2: Functional Studio workspace

- [x] Replace unwired toolbar controls with async commands and operation state.
- [x] Add project create/open and scene selection without requiring launch
  arguments.
- [x] Bind hierarchy selection to a structured inspector and generic entity /
  component / property authoring actions.
- [x] Project registered built-in and project-module schemas into generic
  component/property selectors, constraints, and value choices.
- [x] Display the engine-produced viewport PNG and refresh it after mutations.
- [ ] Own real player launch/stop and close cleanup; add WPF-view-model tests.

### Task 3: Embedded AI authoring

- [x] Add a project-scoped agent-session service using the existing
  provider-neutral agent, Ollama adapter, MCP tool executor, and embedded
  contract.
- [x] Add model discovery/selection, task entry, cancellation, bounded progress,
  tool-result transcript, failure diagnostics, and post-run refresh/validation.
- [x] Prove with deterministic fake-model tests that agent tool calls mutate
  the open project through canonical commands only.
- [x] Run a real local `qwen3.5:35b` benchmark that creates and revises a small
  complete game from the Studio-facing service.

### Task 4: Game-creation acceptance and distribution

- [ ] Add an installed acceptance that starts from no project, authors a
  multi-system game, validates, captures a nonblank frame, launches play mode,
  packages, relocates, audits, and reruns it.
- [ ] Verify CLI and MCP use the same command results as Studio and the embedded
  agent; reject Studio-only hidden mutation paths by source audit.
- [x] Run locked Release, two independent Release suites, publish,
  distribution assembly, and installed acceptance.
- [x] Record exact evidence and remaining product gaps in the maturity audit and
  progress ledger.
