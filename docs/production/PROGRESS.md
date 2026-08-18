# Rekall AGE Production Progress

This is the durable execution ledger for Rekall AGE. Update it only from
verified repository or acceptance evidence. Conversational recency does not
change the priority order.

Last verified: 2026-08-18 02:28 Africa/Johannesburg

Branch: `codex/production-foundation`

Latest milestone: evidence-gated agent completion passed 584/584 Debug tests

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
- Agent/physics distribution gate: the clean build completed with zero warnings
  and errors, both independent Release passes completed at 571/571, and fresh
  installed acceptance passed the complete SDK, authoring, package relocation,
  visual proof, UI, and audio checks. The canonical archive is 194,672,330 bytes.
- Broad benchmark rerun 9: the installed agent reached valid original package
  audit and relocation, but exhausted its 36-turn bound before relocated audit,
  capture, and final evidence. It used 36 tools, 462,217 prompt tokens, and
  10,135 completion tokens. The run exposed that a malformed entity in a later
  multi-scene blueprint could leave earlier project files behind, and that an
  empty component property object was still a dynamically required argument.
  This is not an accepted benchmark pass.
- Blueprint/component repair: project blueprint workflows now preflight every
  requested scene, entity, and component before creating the project; invalid
  standalone blueprints return structured errors without changing the scene.
  `rekall.component.add` now defaults omitted properties to an empty object.
  Three regressions and the full Debug suite pass at 574/574.
- Blueprint/component distribution gate: the clean Release build completed
  with zero warnings and errors, both independent Release passes completed at
  574/574, and installed acceptance passed project-local SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport analysis, simulated audio, and Windows player audio. The
  canonical 1,149-file archive is 194,675,770 bytes.
- Broad benchmark rerun 10: the agent correctly stopped at its 36-turn bound
  (36 tools, 405,610 prompt tokens, 9,633 completion tokens) before package
  creation. It produced two validation-clean scenes, but used playable-module
  execution instead of module-free runtime inspection and authored 3D colliders
  into its 2D scene. Independent inspection proved the 3D body at Y -2.132 but
  the nominal 2D body at Y approximately zero. This is not an accepted pass.
- Physics/inspection repair: validation now blocks colliders that conflict with
  an entity's 2D/3D transform or body contract and returns exact component
  removal/addition repairs. The new generic `rekall.component.remove` command
  preserves other components and transaction preimages. Engine status and the
  embedded-agent contract now direct deterministic subsystem verification to
  `rekall.runtime.inspect_scene`, which requires no playable module. The full
  Debug suite passes at 576/576.
- Physics/inspection distribution gate: the clean Release build completed with
  zero warnings and errors, both independent Release passes completed at
  576/576, and installed acceptance passed SDK/module authoring, generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical archive is
  194,688,256 bytes.
- Broad benchmark rerun 11: the agent reached module-free inspection of both
  scenes, module build, package creation/inspection/run, and relocation before
  the 36-turn bound (36 tools, 490,824 prompt tokens, 8,646 completion tokens).
  Independent inspection found zero active physics bodies: plausible
  `Rigidbody3D`/`Rigidbody2D` names lacked the canonical `Rekall.` prefix and
  were treated as custom components. Two validation warnings also remained,
  and original/relocated audits were absent. This is not an accepted pass.
- Alias/audit repair: validation now blocks exact unqualified aliases of
  registered built-ins and returns executable remove/add migration commands
  that preserve authored properties. Agent status, audit schema, and embedded
  guidance now identify `rekall.workflow.audit_playable_package` as the
  consolidated inspect/run/nonblank-capture proof. The full Debug suite passes
  at 577/577.
- Alias/audit distribution gate: the clean Release build completed with zero
  warnings and errors, both independent Release passes completed at 577/577,
  and installed acceptance passed the full SDK, authoring, package portability,
  visual proof, UI, and audio matrix. The canonical archive is 194,690,547
  bytes.
- Broad benchmark rerun 12: the agent stopped at 36 tools (440,173 prompt;
  6,657 completion) before module/package work. Four direct native calls carried
  an equivalent gateway argument envelope and were rejected as missing fields.
  Independent inspection also exposed that runtime transform extraction ignored
  canonical schema-cased `X`/`Y` properties, so a valid 2D body initialized near
  the origin rather than its authored Y=5 position. The project retained two
  blocking issues and had no 3D body. This is not an accepted pass.
- Runtime/protocol repair: runtime transform extraction is now case-insensitive
  and proven with exact schema-cased 2D physics properties. Discovered native
  tools safely unwrap gateway-style `name`/`arguments` envelopes, including
  JSON-string arguments, before typed dispatch. The full Debug suite passes at
  578/578.
- Runtime/protocol distribution gate: the clean Release build completed with
  zero warnings and errors, both independent Release passes completed at
  578/578, and installed acceptance passed the complete SDK, authoring, package
  portability, visual proof, UI, and audio matrix. The canonical archive is
  194,691,566 bytes.
- Broad benchmark rerun 13: the agent stopped at 36 tools (615,017 prompt;
  6,808 completion) after deterministic runtime inspection, module scaffolding,
  package creation, and original-package audit, but before relocation. It
  recovered from `frameCount` instead of `Frames`, required a separate scaffold
  retry after package creation found no module, and used `archivePath` instead
  of the audit command's canonical `PackagePath`. Repeated component searches
  also exposed that core `Rekall.MeshRenderer` and `Rekall.SpriteRenderer`
  runtime contracts were absent from the registered schema catalog. This is not
  an accepted pass.
- Agent contract discovery repair: dynamic requests narrowly map `frameCount`
  to `Frames` and `archivePath` to `PackagePath` only when the target command
  declares the canonical property. Broad physics schema discovery ranks the
  matching 2D/3D transform, rigid-body, collider, renderer, camera, and light
  families together. `Rekall.MeshRenderer` and `Rekall.SpriteRenderer` now have
  strict registered schemas, and the engine-owned gauntlet no longer authors an
  ignored sprite color property. Focused regressions and the full Debug suite
  pass at 580/580.
- Agent contract discovery distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 580/580; and fresh installed acceptance passed project-local SDK/module
  authoring, the generic gauntlet, original and relocated package audit/run/
  nonblank capture, runtime UI, software viewport analysis, simulated audio,
  and Windows player audio. The canonical 1,149-file archive is 194,700,296
  bytes.
- Broad benchmark rerun 14: the installed agent stopped correctly at 36 tools
  (512,928 prompt; 6,331 completion) after clean validation, two runtime
  inspections, module build, and a final package-creation call, but before any
  package audit or relocation. Independent installed inspection rejected the
  physics proof: both scenes reported two nominal bodies but zero colliders,
  empty dynamic transforms, and no movement after 30 frames. Validation had
  incorrectly reported zero issues because a rigid body shape was not required.
  This is not an accepted pass.
- Rigid-body shape repair: validation now blocks a 2D or 3D rigid body without
  a dimension-compatible collider and returns an exact executable default
  collider addition. Rigidbody and collider schema descriptions now explain
  dynamic composition and that a static surface omits the rigid body. Applied
  to the untouched rerun-14 project, the repaired validator reports all four
  false bodies as blocking issues. The regression and full Debug suite pass at
  581/581.
- Rigid-body shape distribution gate: the clean Release build completed with
  zero warnings and zero errors; both independent Release passes completed at
  581/581; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package audit/run/nonblank capture, runtime
  UI, software viewport analysis, simulated audio, and Windows player audio.
  The canonical 1,149-file archive is 194,703,222 bytes.
- Broad benchmark rerun 15: the agent reached original package audit,
  relocation, and relocated audit by tool 26, then stopped at 36 tools
  (620,253 prompt; 7,983 completion) while revisiting authoring evidence. Both
  physics scenes genuinely simulated: two 3D bodies moved from Y=10 to 8.733
  and one 2D body moved from Y=5 to 3.733. The run nevertheless ended with one
  deliberately introduced invalid renderer property still blocking validation
  and two render-layer warnings; package proofs also predated those late edits.
  This is not an accepted pass.
- Runtime motion evidence: deterministic runtime inspection now reports each
  entity's initial transform and exact 2D/3D position delta alongside the final
  transform. One call therefore proves simulation or animation displacement
  without an agent inferring the starting pose or spending calls on repeated
  inspection. Applied to rerun 15, it reports Y deltas of -1.267 for both 3D
  bodies and the 2D body. The regression and full Debug suite pass at 581/581.
- Runtime motion-evidence distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 581/581; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. Installed inspection printed the
  new delta fields. The canonical 1,149-file archive is 194,706,489 bytes.
- Broad benchmark rerun 16: the installed agent stopped at the 36-turn bound
  after 35 tools (475,141 prompt; 8,249 completion), one operation short of a
  relocated-package audit. Independent verification proved zero validation
  issues, exact Y delta -1.267 for one 3D and one 2D dynamic body, static
  colliders, and original/relocated manifests. Recoverable waste included a
  blueprint without `Entities`, inspection without `Frames`, a package attempt
  before scaffolding, and an unsafe proof directory correctly rejected by the
  immutable-package guard. This is not an accepted pass.
- Embedded delivery sequencing contract: the Ollama agent system contract is
  now a named, regression-tested engine API. It requires requested fault
  injection and zero-issue repair before runtime evidence; treats nonzero
  `PositionDelta2D`/`PositionDelta3D` as direct motion proof; and orders original
  audit, relocation, and relocated audit after authoring is stable. It also
  prohibits reopening authoring after package proof unless evidence failed and
  explicitly keeps proof output outside immutable packages. The full Debug
  suite passes at 582/582.
- Embedded delivery-contract distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 582/582; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,708,031 bytes.
- Broad benchmark rerun 17: model variance consumed the full 36 tools (640,102
  prompt; 8,977 completion) in authoring and validation repair without runtime
  or package work. The first atomic blueprint encoded `Scenes` as a string and
  omitted project identity; later authoring required many component/property
  removals. Independent evidence still found real 3D/2D motion, but Physics2D
  retained two visibility warnings and no deliverable existed. This is not an
  accepted pass.
- Compact blueprint tool contracts: atomic project and scene blueprint command
  descriptions now include minimal exact nested JSON exemplars covering project
  identity, scene/entity/component arrays, and the canonical component
  `type`/`properties` shape. They explicitly prohibit JSON-string encoding for
  the nested arrays. The regression and full Debug suite pass at 583/583.
- Compact blueprint-contract distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 583/583; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,708,844 bytes.
- Broad benchmark rerun 18: the agent reported completion after 29 turns and 28
  tools (475,523 prompt; 6,866 completion), with clean validation, genuine 3D/
  2D motion, original audit, relocation, and relocated audit. Independent
  installed verification confirmed both package audits and the motion deltas,
  but rejected completion: the 3D dynamic body had no renderer and Physics2D
  reported zero visible renderables. Nonblank capture proved package execution,
  not the specifically requested visible physics content. This is a near-pass,
  not an accepted pass.
- Evidence-gated agent completion: the language-model agent API now supports an
  opt-in two-phase completion audit, enabled for embedded Ollama runs. A first
  final response is only a proposal; a dedicated audit turn must compare every
  explicit task requirement against direct tool evidence, treating zero counts,
  warnings/issues, missing components/artifacts, stale proofs, and mere
  existence evidence as failures. If audit resumes tool use, the next proposed
  completion is audited again. The regression and full Debug suite pass at
  584/584.

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

Run the complete clean product gate for evidence-gated agent completion.

## Next after the current item

Rerun the identical installed Ollama benchmark on the rebuilt distribution.
Classify any remaining failure by generic engine contract and fix it with regression tests.
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
