# Rekall AGE Production Progress

This is the durable execution ledger for Rekall AGE. Update it only from
verified repository or acceptance evidence. Conversational recency does not
change the priority order.

Last verified: 2026-08-17 22:40 Africa/Johannesburg

Branch: `codex/production-foundation`

Latest milestone: installed benchmark completion gaps converted into deliverable contracts

## Product objective

Build Rekall AGE into a proprietary, production-quality, AI-first C# game
engine. It must let professional developers and AI agents author arbitrary
games through generic, inspectable runtime primitives, SDK helpers, structured
diagnostics, and portable MCP/tool contracts. The engine supplies capability;
agents author the game.

## Stable priority order

1. Generic runtime and rendering primitives.
2. Agent SDK, MCP/tool contracts, diagnostics, and bounded repair loops.
3. Closed-loop authoring proof using packaged, installed binaries.
4. Reliability, performance, security, packaging, and release hardening.
5. Rekall.Age.Studio as a professional consumer of the proven contracts.

Studio is important, but it does not define or reorder the engine foundation.

## Verified status

- Windows distribution: fresh 194,657,246-byte win-x64 archive assembled.
- Canonical verification: 564/564 Release tests passed twice independently;
  Release build completed with zero warnings and zero errors.
- Installed acceptance: direct rerun exited 0; project/module workflows,
  packaging and relocation, nonblank capture, runtime UI, and audible audio
  paths have installed-binary proof.
- Local agent: Ollama currently uses `qwen3.5:35b` through its native API.
- Agent authoring: both source and installed multi-subsystem benchmarks created
  and repaired UI, animation, and audio content using tool calls.
- Runtime animation: generic clip playback, weighted layers, crossfades,
  deterministic resume, GLB skeletal channels, runtime joint poses, imported
  JOINTS_0/WEIGHTS_0, and CPU skinning before Vulkan submission are covered.
- Installed skeletal rendering: the shipped CLI sampled `Lift` at frame 30,
  exposed skin `Rig` and one joint, then produced informative hardware Vulkan
  frames at frames 1 and 30 with different SHA-256 hashes and visible movement.
- Diagnostics: runtime inspection exposes UI/audio/animation state; viewport
  analysis reports severe clipping and invisible text without irrelevant
  camera advice for UI-only scenes.
- Broad benchmark baseline: installed `qwen3.5:35b` reached the 36-turn limit
  after 36 tool calls, 410,197 prompt tokens, and 11,325 completion tokens. It
  exposed project-validation discovery, playable repair propagation, and
  in-process module rebuild defects rather than producing a false pass.
- Benchmark-driven fixes: project-wide validation now aggregates all scenes;
  `Rigidbody2D` is registered and executes deterministic Bepu XY-plane physics;
  loaded project modules no longer lock authoring outputs; playable verification
  preserves executable scaffold suggestions. The full Debug suite is 562/562.
- Broad benchmark rerun: fresh installed binaries again reached the 36-turn
  bound after 36 tool calls, 389,332 prompt tokens, and 9,309 completion
  tokens. Unlike the baseline, it discovered project validation and authored
  both 3D and 2D physics scenes. Independent inspection isolated two remaining
  generic repair defects: no ordinary command could remove a rejected property,
  and schema numeric bounds such as positive mass were not validated.
- Validation repair contract (targeted verification):
  `rekall.component.remove_property` now removes a single property
  transactionally; unknown-property issues carry exact executable repair
  arguments; and out-of-range numeric properties produce blocking diagnostics
  with an exact boundary-setting action. Five focused regressions and the full
  564/564 Debug suite pass.
- Validation repair distribution gate: the fresh self-contained binaries passed
  installed doctor, project/module authoring, the generic game gauntlet,
  package audit/relocation/run/capture, runtime UI, and audio acceptance.
- Broad benchmark rerun 3: the fixed 36-turn agent run used 36 tools,
  477,614 prompt tokens, and 6,961 completion tokens. It still stopped at the
  bound, but independent installed-CLI verification proves two scenes, zero
  project validation issues, one active 3D body at Y -2.137 after 30 frames,
  and one active 2D body with `Rekall.PhysicsState2D`. The remaining failures
  are redundant discovery, absent no-module scaffold guidance, package-root
  ambiguity, and no ordinary package-relocation command.
- Benchmark-driven deliverable contracts: component search now explicitly
  batches concepts; invented tool aliases return nearest exact registered
  names; a missing module returns an executable playable-scaffold action;
  package creation, executable, archive, and package-root roles are explicit;
  invalid executable package paths return structured diagnostics; and
  `rekall.workflow.relocate_playable_package` copies and integrity-verifies a
  package at a fresh destination. A real relocated package runs successfully,
  and the full Debug suite passes at 567/567.

## Current gaps

- Make the installed Ollama benchmark complete generic 2D/3D physics,
  deliberate-fault repair, visual proof, package audit, and relocation within
  its fixed turn budget.
- Add broader performance budgets, soak/device-loss recovery, security threat
  tests, compatibility fixtures, and release-operability evidence.
- Complete advanced animation coverage such as cubic interpolation, morph
  targets, complex transform fixtures, and generic state-graph primitives.
- Replace the current Studio facade with a professional workbench only after
  its runtime/authoring contracts are stable and independently proven.

## In progress

Commit the deliverable-contract milestone, then run the canonical two-pass
Release/distribution gate.

## Next after the current item

Rebuild, then rerun the identical installed Ollama benchmark on fresh binaries. Classify any
remaining failure by generic engine contract and fix it with regression tests.
A genuine broad benchmark pass precedes quantified production hardening;
Studio follows as a consumer of those results.

## Evidence index

- `docs/production/2026-08-17-engine-maturity-audit.md`
- `docs/production/2026-08-17-ollama-authoring-benchmark.md`
- `docs/superpowers/plans/2026-08-17-runtime-subsystems.md`
- `Artifacts/TestResults/release-pass-1.trx`
- `Artifacts/TestResults/release-pass-2.trx`
- `Artifacts/Distribution/Rekall-AGE-0.1.0-preview.1-win-x64.zip`
- `eng/accept-installed-skeletal-animation.ps1`
- `Artifacts/InstalledSkeletalProof/<run-id>/evidence.json`

## Update rule

At every verified milestone, update the timestamp, verified status, current
gaps, in-progress item, and next item in this file in the same commit as the
milestone documentation or immediately after the verification completes.
