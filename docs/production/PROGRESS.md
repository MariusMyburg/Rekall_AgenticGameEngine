# Rekall AGE Production Progress

This is the durable execution ledger for Rekall AGE. Update it only from
verified repository or acceptance evidence. Conversational recency does not
change the priority order.

Last verified: 2026-08-20 13:34 Africa/Johannesburg

Branch: `codex/production-foundation`

Latest milestone: document recovery is exposed through agent, CLI, and MCP contracts

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

- Windows distribution: fresh 195,300,134-byte win-x64 archive assembled with
  SHA-256 `A222BB5ACD590E796F1CEEB920FAF96FBB5C509604273E5D360450DAE60B3005`.
  Its manifest lists 1,149 payload files; the assembled directory has 1,150
  files including the distribution manifest itself.
- Canonical verification: 818/818 Release tests passed twice independently;
  Release build completed with zero warnings and zero errors.
- Current Debug verification: 840/840 tests pass after bounded project/scene
  recovery inspection, explicit restore, quarantine, path confinement, and
  agent/CLI/MCP exposure.
- Installed acceptance: canonical gate exited 0; project/module workflows,
  packaging and relocation, negative archive preflight, nonblank capture,
  runtime UI, and audible audio paths have installed-binary proof.
- Local agent: Ollama currently uses `qwen3.5:35b` through its native API.
- Agent authoring: both source and installed multi-subsystem benchmarks created
  and repaired UI, animation, and audio content using tool calls.
- Runtime animation: generic clip playback, bounded Hermite interpolation,
  glTF `CUBICSPLINE`, bounded parameter-driven state graphs, weighted layers,
  crossfades,
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
- Evidence-gated completion distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 584/584; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,709,786 bytes.
- Broad benchmark rerun 19: the evidence gate correctly prevented a premature
  success and stopped at 36 turns/35 tools (614,364 prompt; 8,248 completion)
  after revalidating and reinspecting both scenes. Independent installed proof
  showed the artifacts were actually complete: zero validation issues, visible
  3D/2D dynamic bodies with Y delta -1.267, and fresh passing 467-file audits of
  original and relocated packages. The bounded agent lacked a final audited
  response, so the benchmark remains failed despite complete artifacts.
- Bounded request aliases: dynamic requests now additionally normalize `frame`
  to `Frames` and `packageDirectory` to `PackagePath` only when the selected
  request type declares the canonical field. Existing `frameCount` and
  `archivePath` behavior remains covered. The regression and full Debug suite
  pass at 584/584.
- Bounded request-alias distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 584/584; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,709,366 bytes.
- Broad benchmark rerun 20: the installed agent stopped at 36 turns/32 tools
  (688,007 prompt; 16,335 completion) without an audited final response. Field
  aliases removed the prior malformed inspection/package requests, but one
  invented schema-search namespace and packaging before module scaffolding
  remained. Independent evidence found zero validation issues, visible 3D
  motion, genuine but non-rendered 2D body motion, and fresh passing 218-file
  original/relocated package audits. This remains a failed benchmark.
- Visible-delivery agent contract: embedded runs now name the exact component-
  schema search command, require a renderer on every requested visible dynamic
  body, and order required module scaffolding before the first package call.
  The regression and full Debug suite pass at 585/585.
- Visible-delivery contract distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 585/585; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,710,036 bytes.
- Broad benchmark rerun 21: the agent produced complete artifacts and a final-
  looking response at turn 36 after 34 tools (663,401 prompt; 10,676 completion),
  but the evidence gate correctly returned `Completed=False` because no audit
  turn remained. Independent proof found zero validation issues, visible 3D/2D
  motion, and fresh passing 467-file original/relocated audits. Repeated schema
  discovery and a redundant second relocation cycle consumed the final budget.
- Audit-efficiency contract: initial component discovery is now explicitly
  consolidated, and completion audits reuse current passing direct evidence.
  They must not recreate or relocate proven packages unless evidence is missing,
  contradicted, or stale after mutation. The full Debug suite passes at 585/585.
- Audit-efficiency distribution gate: the clean Release build completed with
  zero warnings and zero errors; both independent Release passes completed at
  585/585; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,710,683 bytes.
- Broad benchmark rerun 22: model variance spent eight runtime calls repeating
  missing `Frames` failures with `fabricFrameCount`/`fabricFrames`, then stopped
  at 36 turns/36 tools (545,575 prompt; 8,024 completion) before packaging.
  Independent proof found zero validation issues and visible 3D/2D bodies with
  Y delta -1.267, but no package. This is a clear benchmark failure.
- Wrapper-prefixed frame recovery: dynamic requests now normalize the two
  observed wrapper-prefixed fields to `Frames` only for request types declaring
  that canonical property. The regression and full Debug suite pass at 585/585.
- Wrapper-prefixed frame distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 585/585; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,710,286 bytes.
- Broad benchmark rerun 23: the agent stopped at 36 turns/36 tools (572,246
  prompt; 15,997 completion) after repeated empty-scene project rejections and
  malformed incremental blueprint repairs. It never reached runtime or package
  proof; only project/scene documents existed. This is a clear failure.
- Empty-scene blueprint scaffolding: atomic project creation now permits named
  scenes with empty entity arrays, and ordinary scene blueprints accept empty
  arrays for no-op/clear semantics. This provides a transactional generic
  recovery path while retaining entity/component validation. The full Debug
  suite passes at 586/586.
- Empty-scene scaffold distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 586/586; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,710,362 bytes.
- Broad benchmark rerun 24: the installed agent stopped at 36 turns/36 tools
  (573,004 prompt; 15,500 completion) after four similar oversized blueprint
  failures, later validation/runtime work, and package preflight/scaffolding.
  Independent proof found one warning, non-rendered requested dynamic bodies,
  zero visible 2D renderables, and no package. This is a clear failure.
- Atomic-blueprint fallback contract: the embedded agent now attempts a complete
  atomic project once, then on structural failure creates the same named empty
  scenes and uses smaller per-scene blueprints. It must not repeat substantially
  identical failed blueprint arguments. The full Debug suite passes at 586/586.
- Atomic-blueprint fallback distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 586/586; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,710,384 bytes.
- Broad benchmark rerun 25: the agent reached original/relocated package audits,
  then its completion audit wholesale replaced Main and stopped at 36 turns/38
  tools (715,864 prompt; 11,563 completion). Independent proof found five
  blocking noncanonical rigid-body types, zero 3D motion, and stale package
  evidence. This is a failed, regressed final state.
- Targeted audit repair: completion audits now prohibit scene redesign or
  wholesale replacement. A genuine failed requirement must be repaired with
  the smallest canonical targeted mutation, followed only by evidence made
  stale by that change. The full Debug suite passes at 586/586.
- Targeted audit-repair distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 586/586; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,710,647 bytes.
- Broad benchmark rerun 26: all 35 tools succeeded, but 27 individual suggested
  property-removal calls consumed the 36-turn bound (616,406 prompt; 11,328
  completion) before runtime or package proof. This is a clear throughput
  failure despite correct individual diagnostics.
- Bounded batch validation repair: `rekall.validation.repair_project` executes
  engine-generated mutation suggestions in bounded passes, skips read-only
  discovery actions, stops safely on failed mutation, and returns fresh project
  validation. The embedded contract uses it for multiple repairs while retaining
  deliberate-fault requirements. The full Debug suite passes at 587/587.
- Batch validation-repair distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 587/587; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,726,034 bytes.
- Broad benchmark rerun 27: the installed agent stopped at 36 turns/36 tools
  (568,211 prompt; 8,199 completion) after batch repair aborted on an incomplete
  advisory blueprint suggestion. Independent proof found two blocking invented
  component types, zero active physics bodies in both scenes, and no package.
- Canonical validation repair: batch execution now permits only exact safe
  component mutation commands. Close unknown reserved component types receive
  executable canonical add/remove repairs with authored properties preserved,
  rather than incomplete blueprint hints. The full Debug suite passes at
  588/588.
- Canonical validation-repair distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 588/588; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,726,872 bytes.
- Broad benchmark rerun 28: the installed agent reached clean validation,
  moving visible 3D/2D scenes, module build, and package creation, then deferred
  deliberate-fault exercise until after packaging and stopped at 36 turns/36
  tools (515,162 prompt; 8,862 completion). Independent proof found both bodies
  moving by -1.267 and the original 467-file package passing inspect, run,
  audit, and nonblank capture; relocation alone remained missing.
- Validation sequencing and registered guidance: deliberate faults must use
  existing relevant components immediately after scene authoring, never new
  audit-only entities. Validator and context suggestions now reference only
  registered validation/schema operations instead of the nonexistent generic
  repair workflow or incomplete blueprint calls. The full Debug suite passes
  at 589/589.
- Validation-sequencing distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 589/589; and installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-file archive
  is 194,726,496 bytes.
- Broad benchmark rerun 29: the agent reached both moving-scene inspections,
  original package audit, relocation, and relocated audit by tool 26, then
  completion audit regressed the source project and stopped at 36 turns/36
  tools (677,078 prompt; 12,861 completion). Both independent 467-file package
  audits passed, but the package retained negative mass as string `"-2.5"`
  because validation had falsely reported numeric strings as clean.
- Numeric-string range validation: invariant numeric strings now participate in
  built-in schema minimum/maximum enforcement and receive canonical numeric
  repair suggestions. The full Debug suite passes at 590/590.
- Numeric-string validation distribution gate: the clean Release build
  completed with zero warnings and zero errors; both independent Release passes
  completed at 590/590; and installed acceptance passed SDK/module authoring,
  the generic gauntlet, original and relocated package proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-file archive is 194,727,467 bytes.
- Broad benchmark rerun 30: the agent repaired faults before runtime, proved
  both moving scenes, and audited an original package, then completion audit
  reopened authoring and stopped at 36 turns/36 tools (638,419 prompt; 13,470
  completion). Independent final proof found 17 issues after the rewrite, no 3D
  body, and a still-moving 2D body. Package audit exposed only nonblank proof,
  not the task's required informative-frame fact, and batch repair was repeated
  four times after reaching non-automatic remaining issues.
- Informative package proof and repair termination: packaged capture now keeps
  nonblank and informative facts distinct, returns full bounded frame analysis,
  and package audit requires an explicit `informative-frame` check. Batch
  validation repair now returns a termination reason and remaining automatic
  repair count; advisory-only leftovers terminate as `no-progress` with an
  explicit instruction not to retry unchanged. The full Debug suite passes at
  591/591.
- Informative-proof distribution gate: the clean Release build completed with
  zero warnings and zero errors; both independent Release passes completed at
  591/591; installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. Relocated package audit explicitly
  reported `informative-frame: True` with four distinct colors. The canonical
  1,149-file archive is 194,731,087 bytes.
- Broad benchmark rerun 31: the unchanged installed-agent task reached moving
  3D and 2D scenes, original package audit, relocation, and relocated audit by
  tool 26, then stopped at the 36-turn limit after 35 tool calls (605,077
  prompt; 10,904 completion). Independent installed verification found zero
  blocking validation issues, both dynamic bodies moving, canonical numeric
  masses of `0.0001`, and both 218-file packages ready, runnable, nonblank, and
  explicitly informative with three distinct colors. Four camera-culling
  warnings remain because each camera mask excludes the authored render layer;
  both balls are therefore reported culled. This is the strongest artifact
  result so far, but not a clean bounded benchmark pass.
- Durable completion evidence and camera-mask guidance: pruned agent context
  now retains up to 12 distinct successful validation/runtime/build/delivery
  milestones in addition to the 12 most recent executions. Camera 2D/3D
  schemas explicitly define `CullingMask` as a named-layer expression and
  reject numeric-bitmask folklore through guidance; render-layer validation
  warnings now state the exact wildcard or named-layer correction. The full
  Debug suite passes at 592/592. The first full run failed only after `F:`
  reached zero free bytes from 36.4 GB of accumulated generated test/gate temp
  artifacts; after stopping the runner and clearing only those verified
  ephemeral directories, the unchanged suite passed with about 69 GB free.
- Durable-evidence distribution gate: the clean Release build completed with
  zero warnings and zero errors; both independent Release passes completed at
  592/592; installed acceptance passed SDK/module authoring, the generic
  gauntlet, original and relocated package proof, runtime UI, software
  viewport, simulated audio, and Windows player audio. Relocated audit
  explicitly reported `informative-frame: True` with four distinct colors.
  The canonical 1,149-manifest-file archive is 194,732,792 bytes.
- Broad benchmark rerun 32: the unchanged installed-agent task completed in 23
  turns and 20 tool calls (483,918 prompt; 6,135 completion), well inside its
  36-turn bound. Independent installed verification found two scenes with zero
  issues and zero warnings, 3D and 2D bodies each moving by `-1.267` after 30
  frames, canonical positive numeric masses, and no culled renderables. Both
  original and relocated 218-file packages were ready, ran with exit code 0,
  captured nonblank frames, and passed `informative-frame` with five distinct
  colors. This is the first genuine bounded broad-authoring acceptance pass.
- Package relocation trust-boundary hardening: ZIP relocation now reuses the
  same normalized-path, collision, entry-count, per-entry-size, and total-size
  bounded extractor as package run/audit/capture. An adversarial regression
  test mutates a previously inspected archive during the relocation capacity
  check and proves structured
  `REKALL_PACKAGE_RELOCATION_SOURCE_CHANGED` failure, no traversal write, no
  destination, and no abandoned staging directory. All four package-integrity
  scenarios and the full Debug suite pass at 592/592.
- Relocation-security distribution gate: the clean Release build completed
  with zero warnings and zero errors; both independent Release passes completed
  at 592/592; the rebuilt installed product passed portable SDK/module
  authoring, the generic gauntlet, relocated ZIP run/audit/capture through the
  hardened extractor, informative proof, runtime UI, software viewport,
  simulated audio, and Windows player audio. The canonical 1,149-manifest-file
  archive is 194,732,663 bytes.
- Directory-package trust boundary: directory and manifest-path inspection now
  applies the same default 100,000-entry, 8 GB per-file, and 32 GB total
  uncompressed bounds as archive inspection before recursive enumeration or
  hashing. Package roots and descendants marked as symbolic links, junctions,
  or other reparse points fail with a structured
  `REKALL_PACKAGE_PATH_REPARSE_POINT` diagnostic. Injectable bounded limits and
  file attributes provide deterministic low-cost regression coverage; all four
  package-integrity scenarios and the full Debug suite pass at 592/592.
- Directory-security distribution gate: the clean Release build completed with
  zero warnings and zero errors; both independent Release passes completed at
  592/592; the installed product passed bounded directory-package gauntlet,
  hardened relocated ZIP run/audit/capture, informative proof, runtime UI,
  software viewport, simulated audio, and Windows player audio. The canonical
  1,149-manifest-file archive is 194,734,675 bytes with SHA-256
  `9b754c5f6d2b81b13e28a2516b74855178a020c47ae7a8f043ea36bb6ea935f9`.
- Runtime soak/performance contract: `rekall.runtime.inspect_soak` now loads an
  authored scene once, resumes its immutable world through bounded fixed-step
  chunks, records compact subsystem/memory/throughput checkpoints, and returns
  named deterministic and caller-budget checks through the same CLI/MCP
  contract. Test-first implementation exposed and fixed a core resumed-time
  drift: continuous and chunked execution now derive elapsed time from an
  absolute frame timebase. Invalid requests fail before scene I/O, and budget
  failures preserve all evidence with
  `REKALL_RUNTIME_SOAK_BUDGET_EXCEEDED`.
- Runtime-soak distribution gate: the installed CLI completed 600 frames over
  exactly 10 simulated seconds in five checkpoints at 4,629.6 frames/second,
  with 686,216 bytes retained managed-memory growth, stable 20-system order,
  zero entity growth, and zero checkpoint observations/events. All nine checks
  passed against a 30 FPS, 64 MiB, zero-entity-growth, 32-observation, and
  128-event budget. A separate installed negative proof completed 12 frames
  and retained three checkpoints while returning exit code 1, a failed
  throughput check, and the structured budget error. The clean Release build
  had zero warnings/errors, both Release passes completed at 601/601, and the
  installed SDK/module, gauntlet, relocated-package, UI/viewport, audio, and
  soak matrix passed. The canonical 1,149-manifest-file archive is 194,778,548
  bytes with SHA-256
  `675a442cf35947263841ae915550632c7a63f4d5fa0bbbc572c378f8f607cd2f`.
- Module SDK trust anchor: project-local SDK manifests now carry an atomic,
  bounded SHA-256 inventory, and every module build verifies the exact resource
  set, compatibility/product contract, canonical props bytes, and running-host
  assembly bytes before starting the compiler. Reparse paths, forged local
  resources even with matching forged inventory, malformed/duplicate entries,
  unexpected files/directories, and injected low bounds fail closed with
  `REKALL_MODULE_SDK_INTEGRITY_FAILED`. The complete Modules plus engine-doctor
  Debug selection passes at 62/62.
- Module provenance receipts: successful canonical builds now atomically emit
  `rekall.module.build.json` with the explicit `in-process-full-trust` posture,
  product/SDK identity, deterministic pre-build source fingerprint, output
  size/SHA-256 inventory, and main assembly identity. The read-only inspector
  rejects stale source, missing/extra/tampered output, malformed/traversing or
  duplicate receipt entries, identity mismatches, reparse points, and injected
  bounds without loading module code; packaged output remains verifiable after
  authoring source is removed. Source edits during compilation fail with
  `REKALL_MODULE_SOURCE_CHANGED_DURING_BUILD` and no receipt.
- Canonical intermediate hardening: module projects exclude every `bin/**` and
  `obj/**` tree from source discovery, while policy verifies and build resets
  the dedicated `obj/rekall` tree. This fixed a full-suite discovery where a
  migrated example's legacy generated sources entered portable compilation.
  Bouncing Ball now consumes the public project-local SDK instead of repository
  project references. The complete Debug suite passes at 631/631.
- Verified-only module loading: schema discovery, runtime systems, playback,
  CLI, and dynamic/MCP execution now share one admission path. It requires a
  ready trust inspection, constrains dependency resolution to receipt-inventoried
  files under the verified output root, and rehashes each stream under a
  read/delete-safe lock immediately before `AssemblyLoadContext` consumes it.
  Missing receipts, stale source, changed artifacts, and unverified dependencies
  fail with their exact trust code; the generic coded-boundary contract preserves
  that code through dynamic and CLI adapters. Packaged modules still load after
  source and the project-local SDK are removed. PDBs remain deliberately
  non-shipping ancillary output and are excluded from receipts, while every
  other output remains exact. The focused loader/adapter matrix passes at 23/23
  and the complete Debug suite at 637/637.
- Public trust workflow: `rekall.module.inspect_trust` is a read-only,
  recommended CLI/MCP command that reports the explicit
  `in-process-full-trust` posture, bounded module evidence, exact issues, and a
  rebuild action without loading code. Engine status and README guidance make
  clear that unsigned receipts are integrity/provenance consistency—not a
  sandbox, code signature, or publisher authentication. Playable verification
  exposes a named `module-trust` check, and packaging repeats trust inspection
  immediately before copying the `Game` payload. An injected reparse regression
  proves exact rejection and no payload copy. Packaged receipts intentionally
  exclude non-shipping PDBs while remaining exact for all shipping artifacts.
  The complete Debug suite passes at 639/639.
- Installed module-trust distribution gate: the shipped CLI scaffolded and
  built a portable runtime module, emitted its receipt, and reported
  `Ready: True` with `in-process-full-trust`. A copied project then had one DLL
  byte changed in place; both read-only trust inspection and schema-loading
  admission returned nonzero with exact
  `REKALL_MODULE_OUTPUT_HASH_MISMATCH`, while the untouched project and
  relocated package continued to run, audit, and capture. The clean Release
  build had zero warnings/errors, both independent Release passes completed at
  639/639, the installed gauntlet/package/UI/audio/Windows-player/soak matrix
  passed, and the 600-frame soak ran at 4,627.9 FPS with 687,712 bytes retained
  growth. The canonical 1,149-manifest-file archive is 194,923,288 bytes with
  SHA-256
  `365fcc80428348006174384f32221f47d352b8238807caf75e83ca35deb743b5`.
- Bounded failure-report foundation: the shared Core contract records only
  explicit schema/product/component/outcome/category/recovery/frame/exception
  facts and operator actions. Its store uses per-root concurrency control,
  unique temporary files plus atomic moves, bounded payload/read/retention
  limits, newest-first inspection, malformed-file isolation, and fail-closed
  reparse-root handling. The focused Debug selection passed 5/5, including 12
  concurrent complete writes and contract checks excluding ambient environment
  variables, arbitrary exception data, and project content.
- Bounded player-session supervision: rendering now classifies only typed
  device loss and narrow Veldrid Vulkan device/surface signatures as
  recoverable. The generic supervisor disposes failed sessions before cold
  recreation, preserves finite-frame remainder and continuous-run accounting,
  defaults to two retries, and keeps initialization or arbitrary runtime
  failures fatal. Its production writer persists recovered/exhausted/fatal
  evidence through the bounded atomic store and returns report paths; a writer
  failure is reported but cannot hide the original outcome. The supervisor
  selection passes 8/8 and the combined diagnostics/recovery selection 13/13.
- Agent-readable failure evidence: `rekall.diagnostics.inspect_failures` is a
  recommended read-only CLI/MCP command with a 50-report ceiling and exact
  component/outcome/code filtering. It returns report paths, bounded exception
  facts, limitations, next actions, and isolated malformed-file issues without
  executing project code. Engine status advertises the workflow. CLI output is
  intentionally compact and excludes stack excerpts. The direct command,
  catalog, status, and real CLI-process selection passes 5/5.
- Windows-player cold recovery: the Veldrid/SDL player now runs through the
  generic bounded supervisor and recreates the complete session only for
  classified graphics lifecycle failures. A real Vulkan process injected one
  device loss, disposed the failed session, completed exactly 5/5 total frames
  in two attempts, emitted `REKALL_PLAYER_GRAPHICS_RECOVERED`, and exited 0.
  Arbitrary fatal injection emitted `REKALL_PLAYER_RUNTIME_FATAL` and exited 10
  after one attempt. Repeated loss stopped after the default two retries,
  emitted `REKALL_PLAYER_GRAPHICS_RECOVERY_EXHAUSTED`, exited 11 after three
  attempts, and preserved three completed frames. Each case wrote one bounded
  report. Strict player build produced zero warnings/errors; the combined
  player/supervisor/inspection selection passes 11/11. Cleanup proceeds across
  all GPU resources and closes the SDL window even when idle-wait/disposal
  steps fail. Recovery remains an honest cold restart and does not preserve
  arbitrary in-memory module state.
- Desktop diagnostics and shutdown operability: Studio startup, dispatcher,
  AppDomain, and unobserved-task failures now use the same bounded atomic
  evidence contract with exception-instance duplicate suppression. Dispatcher
  failures remain fatal; unobserved tasks are recorded and explicitly
  observed. The strict Studio build and focused desktop/Studio/CLI selection
  pass with zero warnings/errors and 4/4 tests. Full-suite diagnosis also found
  that Veldrid's non-threaded SDL window could block close for about 21 seconds
  after async initialization. The player now owns SDL windows on a dedicated
  thread and requires confirmed closure within one second; the unchanged
  three-process fault proof dropped to 5 seconds. Locked dependency graphs were
  regenerated and the exact locked graphics-player publish regression passed.
  The complete Debug suite passes 658/658 in 2m15s.
- Installed recovery product gate: the canonical build completed a locked
  restore, a zero-warning/zero-error Release build, and two independent
  658/658 Release passes. Installed one-shot graphics loss recovered by cold
  session restart in two attempts, completed 5/5 frames, emitted
  `REKALL_PLAYER_GRAPHICS_RECOVERED`, and exited 0. Installed arbitrary fatal
  failure emitted `REKALL_PLAYER_RUNTIME_FATAL` after one attempt and exited
  10. Installed repeated graphics loss exhausted the two-retry budget after
  three attempts, preserved 3/5 completed frames, emitted
  `REKALL_PLAYER_GRAPHICS_RECOVERY_EXHAUSTED`, and exited 11. Exactly three
  bounded reports were written and the shipped CLI inspected all three codes.
  The unchanged installed authoring gauntlet, relocated package audit,
  informative hardware frame, runtime UI, audible player, and 600-frame soak
  also passed. The soak simulated exactly 10 seconds at 4,467.9 FPS with
  691,608 bytes retained growth and all nine checks passing. Recovery is a
  bounded cold restart and intentionally does not preserve arbitrary in-memory
  module state.
- Installed compatibility product gate: shipped binaries inspected exactly two
  implicit schema-0 documents as migratable, proved dry-run byte immutability,
  applied both schema-1 migrations, preserved unknown extension data and one
  exact backup set, then reinspected exactly two current documents. A forced
  project schema 2 was rejected with `REKALL_DOCUMENT_SCHEMA_FUTURE` and its
  SHA-256 remained unchanged. The same canonical run passed Debug at 683/683,
  two independent Release passes at 683/683, installed module trust/tamper,
  the generic authoring gauntlet, relocation/package audit, an informative
  hardware frame, runtime UI, audible player, 600-frame soak, and all desktop
  recovery outcomes. Soak simulated exactly 10 seconds at 4,504.8 FPS with
  703,912 bytes retained growth and all nine checks passing.

## Current gaps

- Expand adversarial security tests around authored JSON, migration races,
  diagnostic stores, and full-trust module inputs.
- In-process C# modules intentionally remain full trust and receipts remain
  unsigned; a future restricted/out-of-process host and publisher signatures
  are separate security capabilities, not claims of the current boundary.
- Complete advanced animation breadth such as native glTF weight-channel
  animation, TANGENT/sparse/quantized morph accessors, broader complex
  transform fixtures, richer graph curves, and interruptible or hierarchical
  graph policies.
- Replace the current Studio facade with a professional workbench only after
  its runtime/authoring contracts are stable and independently proven.

## Recently completed

Implement the persisted compatibility design: central project/scene schema
enforcement, deterministic read-only inspection, and explicit atomic legacy
migration. Package, module SDK, receipt, animation, and diagnostic versions
remain intentionally separate contracts. The module trust boundary and desktop
recovery paths remain installed-product verified.

Compatibility Task 1 is verified at 14/14 focused tests: project and scene
stores now share a bounded raw schema probe, persist explicit schema 1,
normalize implicit legacy schema 0 only in memory, keep loads read-only, and
fail closed with typed stable codes for malformed, invalid, or future schema
facts.

Compatibility Task 2 is verified at 14/14 focused tests: the recommended
`rekall.compatibility.inspect_project` command now provides bounded, read-only,
manifest-first inspection through direct command, CLI, and MCP surfaces. It
reports current/legacy/future/malformed/missing states, exact versions and
codes, migration eligibility, blockers, limitations, and next actions without
executing project code or changing source bytes. Oversized/excessive inputs and
reparse traversal fail closed.

Compatibility Task 3 is verified in a 37/37 combined focused selection:
`rekall.compatibility.migrate_project` is
available through direct command, CLI, and MCP with dry-run as the default and
explicit `--apply`. It stages all outputs before replacement, rechecks source
bytes, durably preserves exact originals and hashes, keeps unknown extension
data, records transaction preimages, rolls back partial replacement in reverse
order, rejects reparse-backed engine state, and retains five backup sets without
following reparse paths. Future or malformed inputs remain untouched.

Compatibility Task 4 is installed-product verified: policy and CLI/MCP
workflows are documented, Debug and both Release passes complete at 683/683,
and shipped-binary positive and negative migration proofs passed alongside the
unchanged installed product matrix.

Archive security Tasks 1-4 are complete. Inspection and extraction share one
bounded metadata-first immutable ZIP plan; extraction is exact-length,
reparse-aware, transactional, and cannot publish partial destinations. The
trust boundary and exact limits are documented, and the installed gate includes
a safe negative duplicate-manifest fixture.

Animation state graph Task 4 is installed-product verified. Fresh shipped
binaries authored a genre-neutral two-clip graph, captured an informative idle
frame, changed only its generic `phase` parameter, inspected `active` with
`previous=idle` at exactly 0.500 transition progress, and captured a distinct
informative active frame. The frame SHA-256 values are
`E17ABB6DAE0EDD3963D775617A0FBCADD38E8AE5FCD5E13AE9A52475B3BDC7E4` and
`DC7D7EEB7133226AEA816A7DF24DEEE30C10ABBB15C1881119CBB709F3B405E4`.
Debug passed 738/738 in 2m20s; the zero-warning, zero-error locked Release build
passed 738/738 twice in 2m20s and 2m17s. The unchanged installed product matrix
passed, including all recovery outcomes. Its 600-frame soak simulated exactly
10 seconds at 4,264.1 FPS with 681,624 retained bytes and all nine checks. The
1,149-payload-file archive is 195,141,113 bytes with SHA-256
`7297CE4FCF52960F3217BE6A80CF7046E8052F9A3E12998602C807C0DA9A426D`.

## In progress

The next risk-driven tranche is explicit persisted-document recovery. Atomic
publication and optimistic revisions now prevent torn and stale engine writes,
but storage damage or external/manual corruption still blocks a project. The
reviewed design retains one exact previous validated project/scene version,
adds bounded read-only recovery inspection, and requires an explicit
revision-guarded restore that quarantines the damaged bytes. Normal loads never
silently roll back. Design and TDD sequence:
`docs/superpowers/specs/2026-08-18-persisted-document-recovery-design.md` and
`docs/superpowers/plans/2026-08-18-persisted-document-recovery.md`.

Persisted document recovery Task 1 is verified at 13/13 focused persistence
tests. Conditional publication can atomically retain the exact prior bytes at a
distinct same-volume recovery path while replacing the live document. Repeated
success replaces the recovery snapshot with exactly the immediately preceding
version; stale writes preserve both live and recovery bytes; creation does not
fabricate history; existing cancellation, busy, size, and cleanup guarantees
remain green.

Persisted document recovery Task 2 is verified in a 46/46 focused
atomic/project/scene selection and the complete 830/830 Debug suite. Successful
conditional manifest and scene replacements retain exactly the immediate prior
bytes under a confined `.rekall/recovery` path. Read-only inspection reports
primary/previous availability, exact revisions, schema/shape status, stable
codes, recoverability, and a next action. Explicit restore validates the prior
snapshot, requires the caller's current revision, atomically restores exact
bytes, quarantines the displaced document with its revision, and retains at
most four deterministic corrupt artifacts per document. Malformed prior data
and escaping scene names fail closed; normal loads never silently fall back.
That verified store is the foundation consumed by the portable agent commands,
CLI routes, and MCP tools described in the next milestone.

Persisted document recovery Task 3 is verified in the complete 92/92
agent/MCP/CLI selection and the full 840/840 Debug suite. Generic
`rekall.recovery.inspect_document` and `rekall.recovery.restore_document`
commands target either the manifest or one named scene, expose portable MCP
schemas, preserve read-only inspection, require an exact inspected revision for
restore, return executable ordinary validation actions, and report stable
failure codes plus a fresh inspection action after conflicts. CLI project and
scene routes use the same registry commands. Direct, CLI, and JSON-RPC MCP tests
damage real documents, observe structured recovery facts, explicitly restore,
and successfully perform an ordinary scene mutation afterward. A wider test
found the engine-status payload crossing its 12,000-character agent-efficiency
boundary; the top-level map was curated to retain high-priority recovery
discovery while leaving low-level render-plan execution available through tool
search. The next step is installed-distribution damage/recovery evidence and
the locked complete product gate.

The next risk-driven tranche is optimistic document revisions. Atomic files
eliminate torn reads but do not prevent two valid agent/editor processes from
silently overwriting one another. The reviewed design adds exact snapshot
revision tokens, bounded cross-process compare-and-publish, stable conflict
diagnostics, conditional project/scene mutations, and serialized transaction
append. It does not claim automatic content merge or collaborative-editing UX.
Design and TDD sequence:
`docs/superpowers/specs/2026-08-18-optimistic-document-revisions-design.md` and
`docs/superpowers/plans/2026-08-18-optimistic-document-revisions.md`.

Optimistic document revisions Task 1 is verified at 10/10 focused persistence
tests. Every immutable snapshot exposes a deterministic lowercase SHA-256 token.
All cooperating atomic writers now take a cancellable bounded sibling lock;
conditional publication compares under that lock and either publishes the
complete file or returns exact `REKALL_DOCUMENT_REVISION_CONFLICT` /
`REKALL_DOCUMENT_BUSY` codes without changing the destination. Two writers
using one revision produced exactly one winner and one stale rejection, with no
temporary or engine-owned lock debris.

Optimistic document revisions Task 2 is verified in a 57/57 combined
project/world/compatibility/transaction selection plus a 12/12 project rerun.
Project and scene stores expose versioned loads and conditional saves without
changing persisted JSON. Every ordinary capability/entity/component/blueprint
mutation saves against the loaded or explicitly supplied `expectedRevision`;
creation now requires the explicit missing revision and cannot overwrite an
existing project or scene. A dynamic stale entity mutation returned exact code
`REKALL_DOCUMENT_REVISION_CONFLICT` with expected/current recovery facts while
preserving the intervening entity.

Optimistic document revisions Task 3 is verified in a 69/69 combined
agent-context/MCP/transaction/level-design/geometry selection. Compact project
and scene summaries expose their exact 64-character revisions, while MCP
schemas expose optional `expectedRevision` without making it mandatory for
ordinary semantic operations. A wider source audit converted generic
level-design, KSA import, geometry, prefab, parenting, grid, and virtual-geometry
scene mutations to conditional publication. Thirty-two simultaneous distinct
transaction appends retained all 32 entries through bounded conflict/reload
retries and left no engine-owned control files.

Optimistic document revisions Task 4 passed the complete product gate. Debug
passed 818/818 in 1m24s; the clean locked Release build had zero warnings and
zero errors, and two independent Release passes completed 818/818 in 1m25s and
1m27s. The first Release attempt exposed a transient Windows replace/open
window in the existing reader stress test; snapshot acquisition was hardened to
a bounded 64-attempt ceiling and the exact Release stress passed 10 consecutive
reruns before the complete gate restarted from zero. Shipped binaries exposed
an exact scene revision, rejected a stale mutation with
`REKALL_DOCUMENT_REVISION_CONFLICT`, exposed a changed revision, accepted the
refreshed retry, retained both valid entity edits and both audit entries, and
left zero lock/temp controls. Atomic acceptance concurrently parsed 5,784
complete documents with three tolerated transient opens and zero malformed
documents. The installed matrix passed; its 600-frame soak simulated exactly
10 seconds at 4,259.3 FPS with 712,576 retained bytes and all nine checks. The
1,149-payload-file archive is 195,300,134 bytes with SHA-256
`A222BB5ACD590E796F1CEEB920FAF96FBB5C509604273E5D360450DAE60B3005`.
Automatic content merge, CRDTs, and collaborative-editor conflict UX remain
explicitly outside this tranche.

Atomic persisted JSON was selected as the next risk-driven tranche. Code inspection found
that project and scene loads schema-probe one file handle and then reopen the
path for typed deserialization, while their saves write directly to the live
file. The same direct-write pattern exists in the asset catalog/pipeline,
prefab, render-plan, and transaction-log stores. The reviewed design now fixes
one bounded immutable read snapshot, consistent parse depth, durable
same-directory atomic publication, failure cleanup, and installed concurrent
reader proof. It explicitly does not claim multi-writer merge semantics or a
restricted host for full-trust C# modules. Design and TDD sequence:
`docs/superpowers/specs/2026-08-18-atomic-persisted-json-design.md` and
`docs/superpowers/plans/2026-08-18-atomic-persisted-json.md`.

Atomic persisted JSON Task 1 is verified at 5/5 focused core tests. A shared
bounded snapshot reads one exact byte sequence from one handle, rejects size
overflow before the document allocation, and detects short/changed reads. A
shared UTF-8-without-BOM publisher stages beside the destination with
`CreateNew`, write-through flushes the complete payload, replaces only after
successful staging, preserves existing bytes on cancellation/failure, and
cleans recognizable temporary siblings.

Atomic persisted JSON Task 2 is verified in a 50/50 combined
project/scene/core/compatibility/transaction selection. Schema validation and
typed deserialization now consume the same immutable bytes at one shared depth
limit of 128; depth 80 loads consistently and depth 129 fails with typed code
`REKALL_DOCUMENT_JSON_MALFORMED`. Project and scene saves use durable atomic
publication. A four-reader/50-write scene stress test observed only complete
128-entity documents and passed five additional repetitions. Windows existing
files use `File.Replace`; snapshot opens allow delete sharing and retry only a
small bounded transient replacement window.

Atomic persisted JSON Task 3 is verified in a 92/92 combined
asset/level-design/render-plan/transaction selection. Asset catalog, asset
pipeline, prefab, render-plan, and transaction-log stores now share an explicit
64 MiB and depth-128 policy, deserialize one bounded snapshot, and publish only
through the durable atomic writer. Cross-store round trips preserve existing
shapes; a sparse 64 MiB+1 catalog fails before JSON allocation; depth-80 render
metadata loads consistently; and successful writes leave no temporary siblings.
At this milestone transaction append was still last-writer-wins; optimistic
document revisions Task 3 later closed that audit-history loss window through
bounded compare/reload retries without claiming content-merge semantics.

Atomic persisted JSON Task 4 passed the complete product gate. Debug passed
806/806 in 1m25s; the locked zero-warning, zero-error Release build passed
806/806 twice in 1m25s and 1m24s. A fresh installed CLI performed 20 capability
mutations and 40 entity mutations while an independent process repeatedly
opened and parsed the live project, scene, and transaction documents. It parsed
5,783 complete snapshots, tolerated one bounded transient replacement-window
open miss, observed zero malformed documents, and found zero leaked temporary
siblings. The unchanged installed matrix passed; its 600-frame soak simulated
exactly 10 seconds at 4,365.4 FPS with 703,760 retained bytes and all nine
checks. The 1,149-payload-file archive is 195,258,607 bytes with SHA-256
`2E05246183D9A65F3CF250DECFD5BAF713946255E0BA8FEFE3D7CDD19402F456`.
This proves atomic publication and immutable-reader safety, not multi-writer
merge: independent simultaneous writers remain explicitly last-writer-wins.

The bounded cubic interpolation tranche is installed-product verified. The
bounded morph-target tranche is also installed-product verified. Morph target
Task 1 passed a 51/51 focused runtime/schema/CLI selection.
`Rekall.MorphWeights` exposes one bounded, non-clamped generic array and reuses
ordinary linear/cubic clips and state-graph catalog clips. A post-animation
runtime system rejects empty, excessive, non-numeric, nested, non-finite, and
out-of-range input; removes stale state; preserves exact negative and
extrapolated values; and publishes sorted bounded `Rekall.MorphState`
projection. Split execution matches continuous execution. Runtime CLI
inspection reports counts and invariant-culture weights without vertex data.
Authored modules remain responsible for game behavior.

Morph target Task 2 is verified in a 24/24 combined asset/loader/skeletal
selection. Asset reports expose ordered names, separate mesh defaults and node
overrides, supported POSITION/NORMAL semantics, and explicit limitations. The
loader carries exact aligned deltas and resolved defaults through node
translation/rotation/scale and index remapping, excluding translation from
deltas. It rejects more than 64 targets, more than 4,194,304 declared vectors,
bad counts/strides/defaults/names, non-finite or excessive values, TANGENT,
sparse/quantized accessors, missing base normals, and incompatible compound
layouts before returning partial meshes. Existing plain and skeletal GLB paths
remain green.

Morph target Task 3 is verified across a 117/117 viewport/Vulkan/asset/CLI
selection, followed by a 4/4 inspector smoke rerun after count hardening. Render
projection consumes runtime-only validated state. CPU preparation applies exact
signed weights and normalized normals before skeletal matrices, atomically
falls back to imported defaults on count mismatch, and prevents non-finite or
out-of-float-range values reaching GPU buffers. The generic bounded
`rekall.render.inspect_scene_mesh_geometry` command and `render mesh inspect`
CLI use the same prepared meshes as Vulkan and report post-morph/post-skin
counts, weight source, and finite bounds without vertex dumps. The real fixture
produced exact bounds `(8.5,21,30)` through `(10.5,23,30)`.

Morph target Task 4 passed the complete product gate. Debug passed 792/792 in
1m27s; the locked zero-warning, zero-error Release build passed 792/792 twice
in 1m26s and 1m24s. Shipped binaries imported the real two-target GLB as
`wide,raised` with mesh defaults `[0.25,-0.5]`, sampled generic cubic authored
weights to `[0.75,0]` at frame 30, and reported final post-morph bounds
`(8,21.5,30)` through `(10,23.5,30)`. Native Vulkan captures were informative,
hardware accelerated, free of fallbacks/issues/observations, and changed from
SHA-256 `D97998D4615E2B707B22C0D7137FB84C7C7C26086789B05205A2616D8C07A503`
to `57D4F1735ED1B04F3C8B4AD4A5E481C880C3F59183563D7EFA4F07880D7B32D3`.
The installed matrix passed; its 600-frame soak simulated exactly 10 seconds
at 4,312.7 FPS with 709,392 retained bytes and all nine checks. The
1,149-payload-file archive is 195,236,150 bytes with SHA-256
`CB0DA6560A1422BE5DE7F99182A4651170C6CE397B912762875E1E7BCDF1FE0A`.
Native glTF weight animation and TANGENT/sparse/quantized or incompatible
compound morph layouts remain explicit unsupported boundaries.

That decision is now fixed in
`docs/superpowers/specs/2026-08-18-morph-target-runtime-design.md`: a bounded
`Rekall.MorphWeights` component reuses ordinary clips/mixers/graphs, glTF
POSITION/NORMAL deltas remain aligned through chunking, CPU deformation occurs
before skinning, asset/runtime counts fail closed, and native glTF `weights`
channels remain an explicit follow-up rather than partial hidden support.
The TDD implementation sequence is tracked in
`docs/superpowers/plans/2026-08-18-morph-target-runtime.md`.

The cubic interpolation design is fixed in
`docs/superpowers/specs/2026-08-18-cubic-animation-interpolation-design.md`:
authored clips and glTF `CUBICSPLINE` share duration-scaled Hermite semantics,
fail closed on unknown modes or malformed/non-finite tangent data, preserve
exact endpoints, and keep morph targets outside this focused tranche.
The executable TDD sequence is tracked in
`docs/superpowers/plans/2026-08-18-cubic-animation-interpolation.md`.

Cubic interpolation Task 1 is verified in a 43/43 animation selection. A
focused parser/sampler accepts finite scalar, flat-vector, and RGB/RGBA Hermite
keys, scales tangents by segment duration, preserves exact endpoints, and
clamps color output. Ten adversarial shape/time/value cases fail closed; the
runtime emits bounded target-specific observations without mutation. Unknown
interpolation names no longer silently execute as linear.

Cubic interpolation Task 2 is verified in a 51/51 combined animation/asset
selection. glTF `CUBICSPLINE` output accessors are decoded as standard
input-tangent/value/output-tangent triplets and bounded before runtime use.
Imported translation and scale produce the expected nonlinear midpoint;
rotation output is normalized. Unsupported modes, non-tripled counts,
non-finite records, duplicate cubic times, and near-zero cubic quaternions fail
closed with no invalid pose publication.

Cubic interpolation Task 3 is installed-product verified. Agent schemas expose
the exact four-field cubic key shape, derivatives in units per second, supported
value shapes, and bounds. Debug passed 760/760 in 2m23s; the zero-warning,
zero-error Release build completed in 8.18s and both independent Release passes
completed 760/760 in 2m18s. Shipped binaries reported X 110.0 at frame 30 where
linear would be 80.0 while the graph transition was exactly 0.500. Clean,
informative frames had SHA-256
`38DAB210A0FE5E822F773251EFE18B1B05EF713709F2940813B2F8A99AC3C143` and
`0C9C041274F4063D671D2B9F5ABEBFB0BBC5F6A9E9F8D1AA91D5F86140AAD017`.
The installed matrix passed; its final 600-frame soak reached 4,382.7 FPS with
673,112 retained bytes and all nine checks. The 1,149-payload archive is
195,163,655 bytes with SHA-256
`85CB44D5718825F9F865F7F2FE156ECDE4C325BA5E7DA0573BCADC2DD440204E`.

The next tranche design is fixed in
`docs/superpowers/specs/2026-08-18-animation-state-graph-design.md`: a bounded,
versioned, parameter-driven graph projects into the existing generic mixer,
uses engine delta time, preserves deterministic resume, emits generic state and
transition facts, and keeps all game-specific parameter decisions in
agent-authored content.

Animation state graph Task 1 is verified at 22/22 focused tests. A pure
immutable parser/evaluator now fails closed on malformed, excessive,
non-finite, ambiguously typed, duplicate, or dangling authored graph facts and
selects exact/any, conditional/unconditional, and self-reset transitions in
deterministic order without world, asset, or gameplay dependencies.

Animation state graph Task 2 is verified at 9/9 graph-runtime tests and 50/50
combined animation tests. A pre-animation runtime system projects bounded graph
state into the existing generic mixer, advances only by engine delta time,
supports deterministic reset/resume and noninterruptible cross-fades, emits
bound generic state/transition facts, suppresses conflicting drivers, and
fails closed. Split 17+43-frame execution exactly matches continuous 60-frame
state and output; paused graphs and 64-state clock bounds are explicit.

Animation state graph Task 3 is verified in a consolidated 64/64 selection.
`Rekall.AnimationStateGraph` is discoverable through built-in schemas and MCP
with exact bounded authoring shapes and explicit agent-owned parameter meaning.
Runtime projection and CLI inspection report graph kind, active/previous state,
active clip, transition progress, and bounded layers without unbounded
parameter dumps; existing animation inspection remains compatible.

Archive preflight Task 1 is verified at 15/15 focused tests: a central
metadata-only contract now returns a deterministic manifest-first immutable
entry plan and rejects exceeded bounds, missing/duplicate/oversized manifests,
traversal and Windows-ambiguous names, case/ancestor collisions, and
link/special-file modes with stable codes before opening entry content.

Archive preflight Task 2 is verified with 18/18 focused adversarial tests and
5/5 broad package-integrity tests. ZIP inspection now applies preflight before
manifest deserialization or file-list allocation, reads the bounded unique
manifest and inventory from the immutable plan, hashes only planned files, and
returns exact archive security codes. Valid inspect/run/capture/audit/relocate
paths remain unchanged.

Archive preflight Task 3 is verified with 23/23 focused archive security tests
and 5/5 broad package-integrity tests. Extraction now consumes only the shared
immutable preflight plan, checks destination boundaries for reparse points,
copies every entry to its exact declared length, stages beside the destination,
and publishes by atomic directory move. Invalid preflight cannot create a
destination, existing destinations remain untouched, failures clean staging,
and changed-after-inspection relocation retains its stable diagnostic.

Archive preflight Task 4 is installed-product verified. The full Debug suite
passed 706/706 in 2m18s; the locked Release build had zero warnings/errors and
two independent 706/706 passes completed in 2m18s and 2m17s. Shipped inspect
and audit rejected a duplicate-root-manifest ZIP with exact code
`REKALL_PACKAGE_ARCHIVE_MANIFEST_DUPLICATE`, and rejected audit produced no
output directory. The unchanged installed product matrix passed. Soak completed
600 frames and exactly 10 seconds at 4,449.2 FPS with 693,680 retained bytes and
all nine checks. The 1,149-payload-file archive is 195,083,188 bytes with
SHA-256 `5744CCEEE831BC9C80ABE7F8A2668AA1BE4C570E70106097EE26052368E88B60`.

## Next after the current item

Compare the now-verified animation/renderer and revisioned-persistence
foundations against the remaining corruption recovery, diagnostic-store, and
full-trust module-isolation risks, then select the highest-leverage generic
production-hardening tranche. Studio continues to follow proven engine
contracts instead of reordering the foundation roadmap.

## Evidence index

- `docs/production/2026-08-17-engine-maturity-audit.md`
- `docs/production/2026-08-17-ollama-authoring-benchmark.md`
- `docs/superpowers/plans/2026-08-17-runtime-subsystems.md`
- `docs/superpowers/specs/2026-08-18-runtime-soak-performance-design.md`
- `docs/superpowers/plans/2026-08-18-runtime-soak-performance.md`
- `docs/superpowers/specs/2026-08-18-persisted-compatibility-migrations-design.md`
- `docs/superpowers/plans/2026-08-18-persisted-compatibility-migrations.md`
- `docs/superpowers/specs/2026-08-18-package-archive-preflight-security-design.md`
- `docs/superpowers/plans/2026-08-18-package-archive-preflight-security.md`
- `docs/production/package-trust-and-archive-security.md`
- `docs/superpowers/specs/2026-08-18-animation-state-graph-design.md`
- `docs/superpowers/specs/2026-08-18-cubic-animation-interpolation-design.md`
- `docs/superpowers/plans/2026-08-18-cubic-animation-interpolation.md`
- `docs/superpowers/specs/2026-08-18-morph-target-runtime-design.md`
- `docs/superpowers/plans/2026-08-18-morph-target-runtime.md`
- `docs/superpowers/specs/2026-08-18-atomic-persisted-json-design.md`
- `docs/superpowers/plans/2026-08-18-atomic-persisted-json.md`
- `eng/accept-installed-atomic-json.ps1`
- `docs/superpowers/specs/2026-08-18-optimistic-document-revisions-design.md`
- `docs/superpowers/plans/2026-08-18-optimistic-document-revisions.md`
- `eng/accept-installed-document-revisions.ps1`
- `Artifacts/TestResults/release-pass-1.trx`
- `Artifacts/TestResults/release-pass-2.trx`
- `Artifacts/Distribution/Rekall-AGE-0.1.0-preview.1-win-x64.zip`
- `eng/accept-installed-skeletal-animation.ps1`
- `Artifacts/InstalledSkeletalProof/<run-id>/evidence.json`
- `eng/accept-installed-morph-animation.ps1`
- `Artifacts/InstalledMorphProof/isolated-pass/evidence.json`

## Update rule

At every verified milestone, update the timestamp, verified status, current
gaps, in-progress item, and next item in this file in the same commit as the
milestone documentation or immediately after the verification completes.
