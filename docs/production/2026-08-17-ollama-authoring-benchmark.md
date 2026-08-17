# Ollama agent authoring benchmark — 2026-08-17

## Objective

Prove that a local provider-neutral Ollama agent can author and verify generic Rekall AGE runtime content through engine tools within a bounded loop. The task required a new project and scene, a styled UI button, an inline transform animation, scene validation, and deterministic inspection after 30 frames. The gauntlet was explicitly excluded so the benchmark exercised ordinary authoring primitives.

## Model and bound

- Provider: Ollama native chat/tool API
- Model: `qwen3.5:35b`
- Maximum model turns: 16
- Authoring surface: progressive Rekall MCP command discovery and execution

## Iteration evidence

| Run | Result | Prompt tokens | Finding |
| --- | --- | ---: | --- |
| V1 | turn limit | 358,674 | Content was mostly authored, but incremental calls exhausted the budget. |
| V2 | turn limit | 316,707 | Raw-history pruning caused loss of authoring progress. |
| V3 | turn limit | 166,057 | The agent remained in broad tool discovery. |
| V4 | turn limit | 144,565 | The model called a discovered native tool directly; the progressive executor rejected it. |
| V5 | turn limit | 171,111 | Component names and animation properties were plausible but not exact runtime contracts. |
| V6 | turn limit | 224,382 | Atomic blueprint creation worked, but the full component catalog was too large and nested animation/UI types were invented. |
| V7 | turn limit | 186,751 | Focused schema search improved outer component types; nested track type and camera-neutral validation still failed the acceptance gate. |
| V8 | completed in 15 turns | 122,878 | Authored exact runtime contracts and completed bounded runtime inspection. |

## Generic engine changes driven by the benchmark

- Progressive tool discovery exposes only a compact gateway plus commands the agent has discovered.
- The agent retains a bounded persistent execution ledger when raw messages are pruned.
- Exact registered native tool calls are accepted after discovery even when a model bypasses the wrapper.
- `rekall.workflow.create_blueprint_project` atomically applies a complete agent-supplied project/scene blueprint.
- `rekall.module.search_component_schemas` returns focused exact runtime component contracts.
- Animation-track schema guidance requires fully qualified runtime types such as `Rekall.Transform3D` and exact properties such as `X`.
- Validation requires a camera only for active camera-rendered world content; UI-only and nonvisual scenes remain valid.
- `rekall.runtime.inspect_scene` returns a bounded post-simulation entity-state summary so an agent can verify effects, not only subsystem counts.

## Independent V8 verification

- Scene validation: `ok`, 0 blocking issues, 0 warnings.
- Runtime frame: 30 at the deterministic 60 Hz step.
- Entities: 3.
- UI elements: 1; styled button text is `AGENT READY` in the authored scene.
- Animation players: 1.
- Animated entity post-simulation X: `3.000`, the expected midpoint of 0→6 at 0.5 seconds.
- Runtime observations: none.

The benchmark passed the functional acceptance gate. The remaining efficiency signal is that V8 still used repeated discovery and blueprint calls; future benchmark work should reduce redundant calls without weakening the generic authoring surface.

## Broad installed-engine baseline

A subsequent installed-only benchmark required ordinary tools to author 3D and
2D physics scenes, deliberately introduce and repair invalid content, validate
the whole project, capture an informative frame, and package/audit a relocatable
game. It intentionally excluded the built-in gauntlet.

- Project: `rekall-age-installed-broad-benchmark-4344e0eac0594a5eaa8a1fd112209432`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 410,197
- Completion tokens: 11,325

The run created both scenes and recovered from several malformed arguments, but
did not reach valid packaging. The failure exposed generic engine contracts:

- project-wide validation did not exist, so a project-validation search selected
  unrelated render-plan validation;
- `Rigidbody2D` was inspectable by name but had neither a registered authoring
  schema nor deterministic physics execution;
- playable verification discarded the nested executable
  `rekall.module.scaffold_playable` repair suggestion;
- loading a compiled project module locked its authoring output, so the same
  long-lived agent process could not rebuild after verification.

Those findings drove project validation, planar Bepu physics, repair-action
propagation, rebuild-safe module loading, and regression coverage. This baseline
remains a failure until the identical task passes against a newly assembled
installed distribution.

### Fresh-distribution rerun

After the four baseline defects were fixed and the canonical Release gate
passed twice at 562/562 tests, the identical installed-only task was rerun from
a fresh distribution.

- Project: `rekall-age-installed-broad-benchmark-rerun-1e80188b97a841aca9ace259e4007d89`
- Result: failed at the same 36-turn bound
- Tool calls: 36
- Prompt tokens: 389,332
- Completion tokens: 9,309

The rerun discovered and used project-wide validation and authored both the 3D
and planar 2D scenes, confirming the previous fixes materially advanced the
agent. Independent inspection found that it could add corrected properties but
could not remove `InvalidPropertyXyz` or the obsolete `BoxCollider2D.Size`
property through an ordinary tool. It also authored `Rigidbody3D.Mass = -5`,
which passed validation despite violating the registered positive-mass schema.

This drove a generic transactional `rekall.component.remove_property` command,
exact executable remove actions on unknown-property diagnostics, and numeric
schema-bound validation with executable boundary-setting actions. The rerun is
still recorded as a failure; these changes must pass the complete release gate
and the identical installed benchmark before the acceptance claim changes.

### Executable-repair rerun

Fresh binaries containing executable property repair and numeric-bound
validation passed the canonical gate, then ran the same 36-turn installed task.

- Project: `rekall-age-installed-broad-benchmark-rerun2-26073df8bc1f42b487cad5e957240e09`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 477,614
- Completion tokens: 6,961

Independent installed-CLI verification established meaningful partial success:
project validation passed across two scenes with zero issues; the 3D scene had
one simulated body and two colliders, with the body at Y -2.137 after 30 frames;
the 2D scene had one simulated body and two colliders and emitted
`Rekall.PhysicsState2D`. Both deliberate invalid properties were removed.

The model did not finish packaging, capture, audit, and relocation. Its trace
showed six separate component-schema searches, a missing-module error without
an executable scaffold action, attempts to inspect/audit a player executable
instead of a package root, and no ordinary engine command for package
relocation. These are generic discovery and deliverable-contract gaps, so this
run remains a failure despite its verified physics and repair evidence.

### Deliverable-contract rerun

After package relocation, package-path guidance, scaffold suggestions, and
nearest-tool recovery passed the installed product gate, the unchanged task ran
again.

- Project: `rekall-age-installed-broad-benchmark-rerun4-0ef73379c9b34271a3cad92a2b8cfdb4`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 460,681
- Completion tokens: 7,194

This run did not reach packaging. The model created the first scene atomically,
then had to author the second scene incrementally because
`rekall.workflow.create_blueprint_project` accepted only one scene. Several
`rekall.component.add` calls supplied the `Properties` object as an encoded JSON
string; the generic normalizer intentionally bypassed all `JsonNode` types and
therefore rejected those otherwise recoverable arguments. These failures drive
encoded `JsonObject`/`JsonArray` normalization and a multi-scene atomic project
blueprint contract. The unchanged benchmark remains the acceptance gate.

### Multi-scene/normalization rerun

The atomic multi-scene blueprint and encoded JSON-object recovery passed the
installed product gate before the unchanged task ran again.

- Project: `rekall-age-installed-broad-benchmark-rerun5-8298ef1cdf6e4edc951ccb27ef1cde39`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 706,921
- Completion tokens: 6,571

This run reached the full requested chain: project creation, validation repair,
module scaffold/build, package creation, original package audit/run, relocation,
relocated audit, and capture. Independent installed-CLI verification found zero
issues across both scenes, an active 3D body at Y -2.137, an active 2D body with
`Rekall.PhysicsState2D`, a ready 255-file graphics package, exit code 0 from its
Windows player, and a valid relocated package.

Both audits and the final capture still failed because the graphics package's
Windows player does not emit the structured render-frame JSON consumed by the
deterministic package proof commands. The next generic fix is a dual-mode
graphics package: retain the Windows graphics player as the primary launch
artifact and include a headless proof companion that capture/audit can select.

### Graphics-proof rerun

The dual-mode graphics package passed the full product gate before the unchanged
benchmark ran again.

- Project: `rekall-age-installed-broad-benchmark-rerun6-4dbccbd96368474c93c7a3706fdc1d8b`
- Agent-reported result: completed in 35 turns
- Tool calls: 34
- Prompt tokens: 591,837
- Completion tokens: 8,491
- Independent acceptance: failed

The agent reached authoring, validation repair, packaging, execution, inspection,
capture, audit, and relocation within the bound. Its completion claim was not
accepted. The package proof companion rendered the default scaffold module's
blank frame instead of the packaged authored runtime scene. In addition, the
agent passed the package root as `outputDirectory`; capture wrote
`package_play_frame_001.png` into the immutable package, after which integrity
inspection correctly reported an unexpected file and blocked further proof.

This run isolates two generic package-proof contracts: evidence must be derived
from the packaged authored scene, and proof output must be rejected before any
write when it resolves inside a mutable package directory. The next rerun remains
the same installed-only benchmark and acceptance is still based on independent
evidence rather than the model's final message.

### Authored-scene package-proof rerun

Fresh binaries with authored-scene package capture and output isolation passed
the complete product gate before rerun 7.

- Project: `rekall-age-installed-broad-benchmark-rerun7-20260817`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 675,995
- Completion tokens: 7,189

The repaired original graphics-package audit succeeded and independent installed
inspection found a ready 468-file package plus project-wide validation with zero
issues across both scenes. The run still failed acceptance. The C: volume had
only 67,919,872 bytes free after accumulated benchmark evidence, so every package
relocation attempt failed while copying the package. Staging directories were
cleaned, but the command exposed a generic I/O exception and no actionable
capacity diagnostic; the agent repeated relocation seven times and exhausted its
turn budget.

Independent runtime inspection also found one 3D and one 2D body, but both body
transforms remained at zero because each rigid body and its collider were authored
on separate entities. Counts alone therefore did not prove the requested motion.
This remains a failed benchmark. It drives destination-capacity preflight and
structured relocation recovery before another clean installed run; physics motion
continues to require state-based independent acceptance.

## Expanded installed-engine benchmark

The benchmark was then expanded to require UI, animation, imported audio, static
validation, deterministic runtime inspection, and a software viewport proof from
the assembled self-contained Windows distribution. The agent was forbidden from
using `play.scene`, authored modules, or the closed-loop gauntlet so the run
continued to measure ordinary generic authoring primitives.

- Installed product: `Rekall-AGE-0.1.0-preview.1-win-x64`
- Model: `qwen3.5:35b` through the native Ollama tool API
- Bound: 24 model turns
- Result: completed in 23 turns with 22 tool calls
- Prompt tokens: 311,000
- Completion tokens: 8,232

Independent verification with the installed CLI, rather than the model's final
message, established:

- scene validation `ok`, with 0 blocking issues and 0 warnings;
- one active 200x100 UI canvas and two resolved elements;
- interactive button text exactly `SYSTEMS READY`;
- inline transform animation at X `3.000` after 30 fixed frames;
- one active looping voice for imported asset
  `asset_benchmark-tone_46c758f0`, with 1,600 mixed samples;
- zero structured runtime observations; and
- a 200x100 software capture reported informative with no missing, unsupported,
  or fallback assets.

The installed run is a functional contract/runtime pass. Its capture remained
visually weak and the agent used several corrective blueprint applications.
Those are measured gaps: generic viewport/UI composition diagnostics and lower
redundant discovery/correction cost remain priority work.

## Additional generic changes driven by the expanded run

- Component-schema search now returns compact contracts and rejects missing
  queries with a structured error instead of failing internally.
- Dynamic command arguments normalize bounded JSON-string encodings according
  to the target request type while preserving genuine string fields.
- Required command fields fail before dispatch with structured diagnostics.
- Validation rejects misspelled reserved UI component types, UI elements without
  a canvas, and unknown properties on registered built-in components instead of
  allowing runtime-ignored authoring mistakes.
- Runtime inspection exposes bounded audio voices, animation players, UI canvas
  dimensions, resolved element layouts, interactivity, and text.
- Viewport layout diagnostics distinguish world content from camera-independent
  UI and report severely clipped elements or text with no visible pixels.
- Failed agent tool calls include bounded argument and result previews so repair
  remains inspectable without unbounded context growth.
