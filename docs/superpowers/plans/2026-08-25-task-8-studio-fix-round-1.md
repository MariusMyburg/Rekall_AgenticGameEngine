# Task 8 Studio Fix Round 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Subagents are prohibited by the task owner.

**Goal:** Make Studio rendering evidence session-owned and correctly scoped, make rendering operations lifecycle-cancellable and disposal-safe, and preserve real comparison nonblank diagnostics.

**Architecture:** `RekallAgeWorkbenchSession` becomes the single owner of typed rendering evidence for its active `(projectRoot, sceneName)` scope and merges that evidence into every canonical model rebuild. Studio invokes all capture/comparison work through a lifecycle-linked tracked operation wrapper, while immutable Editor presentation records preserve the shared command’s real facts.

**Tech Stack:** C# 14 / .NET 10, immutable records, WPF MVVM, xUnit, Rekall AGE command registry.

**Spec:** `.superpowers/sdd/2026-08-24-high-fidelity-forward-plus-foundation/task-8-brief.md` plus review fix-round payload dated 2026-08-25.

## Global Constraints

- Rendering evidence is keyed to the exact normalized project root and scene name.
- Selection and read-only refreshes retain evidence; authored resource mutations, undo/redo, and scene changes invalidate it.
- Usable typed capture/comparison values survive accompanying command errors.
- Studio capture/comparison commands receive a lifecycle-linked per-operation token.
- Disposal cancels and awaits active rendering work before disposing preview/dependencies.
- GPU cancellation is expected lifecycle behavior, not `REKALL_STUDIO_UNEXPECTED_FAILURE`.
- Comparison `NonBlank=false` remains false in both comparison and debug-view presentation records.
- Deferred minors are not broadened into this round.

---

### Task 1: Session-owned scoped rendering evidence

**Files:**
- Modify: `src/Rekall.Age.Editor/RekallAgeWorkbenchSession.cs`
- Test: `tests/Rekall.Age.Tests/Editor/WorkbenchRenderingEvidenceSessionTests.cs`

**Interfaces:**
- Consumes: `CaptureRuntimeViewportResult`, `CompareQualityPresetsResult`, and existing `RekallAgeWorkbenchModelBuilder.With*Result` mappings.
- Produces: canonical `RekallAgeWorkbenchSession.Model` with retained or invalidated `Rendering` evidence.

- [x] Write real-session tests that execute deterministic typed commands, select another entity, perform a current-scene component mutation, switch scenes, and return a partial failed comparison.
- [x] Run only `WorkbenchRenderingEvidenceSessionTests`; expect evidence to disappear on selection, leak across scene scope, and be absent after partial failure.
- [x] Add an internal scoped evidence snapshot in `RekallAgeWorkbenchSession`, merge it after harmless rebuilds, invalidate it for active-scene/authored-resource mutations and history restoration, and apply usable typed results before the failure return.
- [x] Rerun `WorkbenchRenderingEvidenceSessionTests`; expect all cases green.

### Task 2: Studio rendering lifecycle cancellation

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Test: `tests/Rekall.Age.Studio.Tests/StudioViewModelTests.cs`

**Interfaces:**
- Consumes: the existing `_lifecycleCancellation` token and `RekallAgeWorkbenchSession.ExecuteAsync(..., CancellationToken)`.
- Produces: tracked render operation tasks whose linked tokens are canceled and awaited by `DisposeAsync`.

- [x] Add a deterministic command that waits only on the supplied command-context token and a preview-session probe that records disposal ordering.
- [x] Start a Studio quality capture, wait for the command to enter, dispose the ViewModel, and expect RED because the token is not canceled and disposal cannot finish safely.
- [x] Route capture/quality-capture/compare commands through `RunRenderingAsync(Func<CancellationToken,...>)`, track their completion under a lock, handle lifecycle cancellation without unexpected-failure diagnostics, and await tracked work before dependency disposal.
- [x] Rerun the deterministic cancellation/disposal test and existing quality behavior tests.

### Task 3: Exact comparison nonblank facts

**Files:**
- Modify: `src/Rekall.Age.Editor.Contracts/RekallAgeWorkbenchModel.cs`
- Modify: `src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Editor/WorkbenchReadModelTests.cs`

**Interfaces:**
- Consumes: `RekallAgeQualityPresetCapture.NonBlank`.
- Produces: `RekallAgeWorkbenchRenderQualityComparisonModel.NonBlank` and matching debug-view `NonBlank`.

- [x] Add a literal `NonBlank=false` capture regression and assert both presentation layers remain false.
- [x] Run the focused test; expect the comparison record to lack the property or the debug row to report true.
- [x] Add the immutable field and map the exact command value without inference.
- [x] Rerun the focused read-model and session partial-result tests.

### Task 4: Verification, evidence, and commit

**Files:**
- Modify: `.superpowers/sdd/2026-08-24-high-fidelity-forward-plus-foundation/task-8-report.md`

- [x] Run the focused fix-round tests sequentially under an exact `D:` NTFS root.
- [x] Run the complete Editor-focused Task 8/session filters and full Studio suite sequentially.
- [x] Run `dotnet build Rekall.AGE.sln --no-restore -m:1 --verbosity:minimal`.
- [ ] Append root causes, exact RED/GREEN commands/output, regression totals, commit/process/temp status, and concerns to the report.
- [ ] Run `git diff --check`, commit all changes, record hashes, and verify a clean worktree and no Task 8 processes.
