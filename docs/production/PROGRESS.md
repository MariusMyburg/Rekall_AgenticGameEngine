# Rekall AGE Production Progress

This is the durable execution ledger for Rekall AGE. Update it only from
verified repository or acceptance evidence. Conversational recency does not
change the priority order.

Last verified: 2026-08-18 00:03 Africa/Johannesburg

Branch: `codex/production-foundation`

Latest milestone: agent completion and physics diagnostics pass the Debug gate

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

- Windows distribution: fresh 194,666,626-byte win-x64 archive assembled.
- Canonical verification: 569/569 Release tests passed twice independently;
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
- Deliverable-contract distribution gate: fresh installed binaries passed the
  complete black-box acceptance, including relocated package audit/run/capture,
  after both independent Release passes completed at 567/567.
- Broad benchmark rerun 4: the fixed 36-turn run again stopped at the bound
  after 36 tool calls, 460,681 prompt tokens, and 7,194 completion tokens. It
  exposed two generic authoring defects before packaging: encoded JSON object
  fields such as `component.add.properties` bypass normalization, and the atomic
  blueprint workflow supports only one scene, forcing inefficient incremental
  authoring for multi-scene projects.
- Multi-scene authoring contract: `rekall.workflow.create_blueprint_project`
  now accepts an arbitrary `Scenes` list and creates every complete scene in
  one command while retaining the existing single-scene request shape.
- Dynamic argument recovery: bounded encoded `JsonObject` and `JsonArray`
  fields now normalize to their declared types while genuine string fields stay
  unchanged. Focused regressions and the full 568/568 Debug suite pass.
- Multi-scene/normalization distribution gate: fresh installed binaries passed
  the complete black-box acceptance after both independent 568/568 Release
  passes.
- Broad benchmark rerun 5: the unchanged 36-turn run reached multi-scene
  authoring, validation repair, module build, package creation, package run,
  relocation, both audits, and capture after 36 tools (706,921 prompt tokens;
  6,571 completion tokens). Independent verification proves zero validation
  issues, active 3D/2D physics, package integrity, successful primary package
  execution, and successful relocation. It remained a failure because a
  graphics package contains only the Windows player, which exits successfully
  but does not emit the structured frames consumed by deterministic package
  capture/audit.
- Graphics deliverable proof: graphics packages retain the Windows player as
  primary launch and include an integrity-inventoried headless proof companion.
  Package capture selects the proof player; primary run semantics remain
  unchanged. A real graphics package now captures nonblank evidence and passes
  audit both before and after relocation. The full Debug suite passes 569/569.
- Graphics-proof distribution gate: both independent Release passes and the
  complete installed black-box acceptance passed on fresh binaries.
- Broad benchmark rerun 6: the agent reported completion after 35 turns and 34
  tool calls (591,837 prompt tokens; 8,491 completion tokens), but independent
  acceptance rejected that claim. Package audit captured the scaffold module's
  blank structured frame instead of the packaged authored runtime scene. The
  agent also supplied the package root as its proof output directory; capture
  wrote an undeclared PNG into the immutable package, so subsequent integrity
  checks correctly failed. This is a near-pass, not an accepted benchmark pass.
- Package-proof contract repair: capture now first proves the packaged launch,
  then renders the manifest scene from the packaged `Game` root through the
  deterministic runtime viewport. Directory and manifest packages reject any
  proof output at or beneath the immutable package root before execution or
  writes, with an exact safe retry command; package audit preserves the audit
  intent in that retry. Original, relocated-directory, and ZIP scenarios pass,
  rejected output leaves integrity intact, and the full Debug suite passes
  569/569.
- First distribution attempt: the clean build and both independent Release
  passes completed at 569/569, but installed acceptance caught a compatibility
  regression before relocation: authored-scene capture changed the established
  proof filename from `package_play_frame_001.png` to `Main_runtime_001.png`.
  The command now keeps the deterministic package-proof filename while retaining
  the authored-scene pixels. A filename regression and the full 569/569 Debug
  suite pass; the distribution gate must be rerun from scratch.
- Final package-proof distribution gate: a fresh clean build completed with 0
  warnings and 0 errors; both independent Release passes completed at 569/569;
  and installed acceptance passed project-local SDK/module authoring, the
  generic gauntlet, deterministic package proof, relocated ZIP audit/run/capture,
  runtime UI, software viewport analysis, audio simulation, and Windows player
  audio-device startup. The canonical archive is 194,669,640 bytes.
- Broad benchmark rerun 7: the original graphics package now passed audit and
  independent inspection found a ready 468-file package plus zero validation
  issues across two scenes. The run still failed at 36 tools (675,995 prompt;
  7,189 completion): C: had only 67,919,872 bytes free, relocation returned a
  generic copy exception seven times, and no relocated proof was produced.
  Independent runtime inspection also rejected physics completion because each
  rigid body and collider were on separate entities and transforms stayed at
  zero. This is not an accepted pass.
- Relocation capacity contract: the workflow now measures the verified package
  inventory against free space on the destination volume before it creates a
  staging directory. Insufficient capacity returns
  `REKALL_PACKAGE_RELOCATION_SPACE_INSUFFICIENT`, reports required/available
  bytes, explicitly prevents same-destination retries, and leaves no destination
  or staging residue. The regression and full Debug suite pass at 569/569 with
  test temporaries routed to F:.
- Relocation-capacity distribution gate: the clean build completed with zero
  warnings/errors, both independent Release passes completed at 569/569, and
  fresh installed acceptance passed SDK/module authoring, gauntlet, original and
  relocated package proof, runtime UI, viewport, simulated audio, and Windows
  player audio. Acceptance temporaries ran on F:; the new canonical archive is
  194,669,627 bytes.
- Broad benchmark rerun 8: the engine reported completion after 22 turns and 21
  tools (285,836 prompt; 4,476 completion), but the final model response was
  empty and the trace ended after validation. Independent acceptance found one
  blocking mass issue, no deliverable workflow, valid 3D/2D motion, and runtime
  warnings for missing explicit transforms. Empty no-tool responses can no
  longer complete the embedded agent; they now trigger a bounded corrective
  continuation. This run remains failed.
- Agent-loop completion contract: an empty or whitespace no-tool response now
  receives a bounded corrective user turn and cannot set `Completed=true`.
- Physics authoring contract: any `Rekall.Rigidbody3D`/`Rigidbody2D` without its
  dimension-matching transform is now a blocking static validation issue using
  the same `REKALL_PHYSICS_BODY_NO_TRANSFORM` code as runtime observation, with
  an exact executable `rekall.component.add` repair. Both regressions and the
  full Debug suite pass at 571/571.

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

Run the clean Release/distribution gate for the agent-loop and physics-diagnostic
milestone, then rerun the installed benchmark.

## Next after the current item

Rerun the identical installed Ollama benchmark on fresh binaries. Classify any
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
