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

### Capacity-hardened rerun

Relocation capacity preflight passed the complete product gate before rerun 8,
whose project and temporary paths were placed on F:.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun8`
- Engine-reported result: completed in 22 turns
- Tool calls: 21
- Prompt tokens: 285,836
- Completion tokens: 4,476
- Independent acceptance: failed

The model returned an empty no-tool response immediately after its second
project-validation call. The embedded agent treated that empty response as
successful completion even though the trace contained no runtime inspection,
module authoring, capture, package, audit, or relocation. Independent validation
still found one blocking zero-mass issue. Runtime inspection did prove motion for
the combined 3D body/collider (Y -1.767) and planar body/collider (Y -1.267), but
both scenes emitted `REKALL_PHYSICS_BODY_NO_TRANSFORM` because their physics
entities lacked explicit transform components.

This is not an accepted run. It isolates a provider-neutral loop defect: an empty
final model response must not produce `Completed=true`. Empty finals now receive
a bounded corrective continuation asking for every requested outcome and a
concrete evidence-backed final response. Missing physics-transform validation
remains a separate generic authoring diagnostic gap.

### Atomic-blueprint rerun

Fresh installed binaries with empty-final rejection and static transform
diagnostics were used for rerun 9 on F:.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun9`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 462,217
- Completion tokens: 10,135

The agent reached successful project repair, module build, graphics package
creation, original-package audit, package run, and relocation. It exhausted the
bound before runtime-state inspection and relocated inspect/audit/capture, so
the run is not accepted.

The trace also disproved the workflow's prior atomicity claim: a malformed
entity in a later requested scene threw after the project manifest and earlier
scene had already been written. Standalone blueprint application surfaced the
same malformed content as a generic exception. Finally, the exact physics
transform repair remained fragile because `rekall.component.add` dynamically
required an otherwise empty `properties` object. These findings drive complete
blueprint preflight with structured errors and an optional empty component
property bag before the unchanged benchmark runs again.

### Dimension-safety rerun

Rerun 10 used the atomic-blueprint distribution with the same 36-turn task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun10`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 405,610
- Completion tokens: 9,633

The agent correctly returned `Completed=false`. It reached a validation-clean
two-scene project and compiled a playable module, but did not create or prove a
package. It spent repeated calls on component-schema discovery, then selected
`rekall.play.scene` for frame-based inspection. That operation correctly
requires a compiled playable module, while the generic
`rekall.runtime.inspect_scene` command supplies the requested deterministic
subsystem state without one.

Independent installed-CLI validation reported zero issues, but runtime evidence
showed a deeper contract gap. The 3D body fell to Y -2.132 after 30 frames. The
nominal 2D body used `Rekall.BoxCollider3D` on a `Rekall.Transform2D`/
`Rekall.Rigidbody2D` entity and remained at approximately Y zero; its static
floor had the same dimensional mismatch. Static validation therefore accepted
content whose runtime physics did not match the authored dimension.

This run drives blocking dimension-mismatch diagnostics with executable generic
component removal/addition repairs, plus prominent module-free runtime-inspection
guidance in engine status and the embedded-agent contract.

### Canonical-component rerun

Rerun 11 used the dimension-safe distribution with the unchanged 36-turn task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun11`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 490,824
- Completion tokens: 8,646

The agent used module-free deterministic inspection for both scenes, compiled a
playable module, created a graphics package, inspected and ran it, and relocated
it. It correctly returned `Completed=false` because neither the original nor
relocated package had complete audit/capture evidence.

Independent installed-CLI verification rejected the physics evidence. The
agent authored `Rigidbody3D` and `Rigidbody2D` without the canonical `Rekall.`
prefix. Runtime correctly treated those as custom component types, so both
scenes reported zero active physics bodies and neither dynamic entity moved.
Validation had accepted the plausible aliases because custom components are
allowed to use arbitrary names. It also retained two camera-layer warnings, so
the task's zero-issue condition was not met.

The generic correction is intentionally narrow: exact unqualified aliases of a
registered built-in are now blocking diagnostics with executable migration to
the canonical type while preserving properties; namespaced agent-authored
components remain valid. The run also showed that package proof should use the
single audit workflow, which already consolidates inspection, run, and nonblank
capture, instead of consuming turns on its sub-operations.

### Runtime-schema casing rerun

Rerun 12 used the canonical-alias distribution with the unchanged 36-turn task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun12`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 440,173
- Completion tokens: 6,657

The agent stopped before module and package work. Four calls to discovered native
tools used the progressive gateway's `name`/`arguments` envelope, sometimes with
the inner arguments encoded as JSON. The executor passed that wrapper directly
to the typed command and returned missing-field errors even though the intended
request was recoverable.

Independent inspection found two blocking unknown Transform3D properties in
Main and no 3D body. Physics2D used canonical component names and reported one
body, but the dynamic entity remained near Y zero rather than falling from its
authored Y=5. The component schema advertises Pascal-case `X`, `Y`, `Mass`,
`Width`, and `Height`; physics property reads supported that casing, while the
runtime world builder extracted transforms only through lowercase JSON keys.
Thus an agent following the exact schema could author valid but incorrectly
initialized runtime state.

The provider-neutral repair makes transform extraction case-insensitive and
allows discovered native tools to unwrap the gateway envelope when it contains
only the matching tool name and object/JSON-string arguments.

### Agent-contract discovery rerun

Rerun 13 used the runtime-schema distribution with the unchanged 36-turn task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun13`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 615,017
- Completion tokens: 6,808

The agent progressed through deterministic runtime inspection, module
scaffolding, package creation, and original-package audit, but had no remaining
turn for relocation and relocated audit. It recovered from a malformed first
blueprint, used `frameCount` instead of the runtime command's canonical
`Frames`, and later used `archivePath` instead of the audit command's
`PackagePath`. Package creation correctly returned the exact playable-scaffold
action when no module existed, and succeeded after the agent executed it.

The trace also spent many calls searching component schemas separately. Review
of the returned catalog found a product defect rather than only a prompting
problem: core `Rekall.MeshRenderer` and `Rekall.SpriteRenderer` runtime
components had no registered authoring schemas. The generic correction adds
their strict contracts, ranks complete composable physics/rendering families
for broad searches, and narrowly normalizes the two recoverable request aliases
according to the selected command's actual request type.

### Rigid-body shape rerun

Rerun 14 used the agent-contract discovery distribution with the unchanged
36-turn task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun14`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 512,928
- Completion tokens: 6,331

The agent reached validation, deterministic inspection of both scenes, module
build, and package creation, but had no remaining calls for original-package
audit, relocation, or relocated audit. It also spent a call on
`rekall.module.scaffold_runtime_system` without the required runtime-system
identity fields even though only a playable scaffold was needed.

Independent installed-CLI validation reported zero issues, but runtime evidence
rejected both physics proofs. Main and Physics2D each reported two bodies, zero
colliders, and no dynamic movement after 30 frames. The authored rigid bodies
had no collision shapes, so the Bepu runtime could not create simulated dynamic
bodies. Empty transform property bags also placed all authored bodies at the
origin; that is valid on its own, but it made the absent simulation obvious.

The generic correction makes a dimension-compatible collider a blocking
contract for every rigid body, supplies an executable default collider addition,
and documents through component schemas that dynamic bodies combine transform,
rigid body, and collider while static surfaces omit the rigid body. The repaired
validator identifies all four false bodies in the untouched benchmark project.

### Motion-evidence rerun

Rerun 15 used the rigid-body shape distribution with the unchanged 36-turn
task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun15`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 620,253
- Completion tokens: 7,983

This run reached package creation, original audit, relocation, and relocated
audit by tool 26. It then revisited physics proof and authoring requirements,
ending on validation after using its remaining calls for inspection and
property mutations. Two early calls were also recoverable: one gateway payload
was doubly encoded, and one scene blueprint was applied before the scene was
created.

Independent installed inspection proved actual deterministic motion. Main had
two dynamic 3D bodies with matching colliders, both moving from Y=10 to Y=8.733
after 30 frames. Physics2D had one dynamic body and matching collider, moving
from Y=5 to Y=3.733. Original and relocated package manifests and nonblank proof
frames existed. The final project was not acceptable: it retained one blocking
deliberately invalid MeshRenderer property plus two camera-layer warnings, and
the package proofs predated the late mutations.

The run exposed a generic evidence problem: runtime inspection returned only
final transforms, forcing the agent to remember or rediscover authored starting
positions before it could prove displacement. Inspection now returns the
initial transform and explicit 2D/3D position deltas for every bounded entity
state. Against the untouched run, it directly reports Y delta -1.267 for the
two 3D bodies and the 2D body.

### Delivery-sequencing rerun

Rerun 16 used the motion-evidence distribution with the unchanged 36-turn task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun16`
- Result: failed at the 36-turn bound
- Tool calls: 35
- Prompt tokens: 475,141
- Completion tokens: 8,249

The run created, validated, inspected, packaged, audited, and relocated the
game, then stopped one operation before relocated-package audit. Independent
installed verification proved both scenes clean with zero warnings: Main had
one dynamic 3D body, one static collider, and Y delta -1.267; Physics2D had the
matching planar contract and the same Y delta. Both original and relocated
package manifests existed.

Four recoverable calls prevented completion: an empty scene blueprint omitted
`Entities`, an inspection omitted `Frames`, packaging preceded the exact
scaffold repair, and the first package audit placed proof output inside the
immutable package. The last case was correctly rejected without corrupting the
package and returned a safe retry.

The generic response is an explicit embedded delivery protocol rather than a
benchmark-specific workflow. Requested deliberate faults and all validation
repairs must finish before runtime evidence; explicit position deltas are direct
motion proof; and original audit, relocation, and relocated audit happen only
after authoring is stable. Package proof outputs must stay outside immutable
packages. The contract is now centralized and regression-tested instead of
being an opaque CLI string.

### Compact-blueprint rerun

Rerun 17 used the delivery-contract distribution with the unchanged 36-turn
task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun17`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 640,102
- Completion tokens: 8,977

Model variance regressed this run into authoring and repair. The first atomic
project blueprint encoded `Scenes` as a JSON string while omitting
`ProjectRoot`, `ProjectName`, and `ProjectCapabilities`. Later calls created the
project but authored enough malformed component/property structure to consume
the remaining budget on suggested removals. No runtime inspection or package
workflow occurred in the agent trace.

Independent installed inspection nevertheless found two functioning physics
scenes: the principal 3D and 2D bodies each had Y delta -1.267. Static-floor
collider composition was incomplete in Main, and Physics2D retained two
camera/render-layer warnings. With no deliverable, the run is a clear failure.

The generic correction puts a compact exact JSON exemplar directly in the
atomic project and scene blueprint tool descriptions. It shows required project
identity, true scene/entity/component arrays, and the canonical component
`type`/`properties` nesting, explicitly warning that nested arrays are never
JSON strings. This complements the formal generated schema at the point where
an LLM chooses argument structure.

### Evidence-gate rerun

Rerun 18 used the compact-blueprint distribution with the unchanged 36-turn
task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun18`
- Model result: reported completion after 29 turns
- Tool calls: 28
- Prompt tokens: 475,523
- Completion tokens: 6,866

The agent reached clean validation, runtime inspection of both scenes, package
creation, original audit, relocation, and relocated audit. Independent
installed verification confirmed zero validation issues, 3D delta
`(0.005,-1.178,-0.998)`, 2D Y delta `-1.267`, and fresh passing audits with
nonblank frames for both original and relocated packages.

Completion was still false. The 3D dynamic body had no renderer component;
Physics2D reported zero visible renderables and its dynamic body likewise had no
renderer. Main's only visible runtime rendering came from unrelated atmosphere
content. A nonblank package frame therefore proved executable rendering but not
the explicit requirement that the dynamic physics bodies be visible.

The generic correction adds an optional evidence-gated completion phase to the
language-model agent and enables it for embedded Ollama runs. The first no-tool
final answer becomes a proposal. A dedicated audit turn must re-read the
original task and tool evidence, treating zero counts, warnings/issues, missing
components or artifacts, stale package proof, and existence-only evidence as
failures. If the audit calls tools, any later completion proposal is audited
again before the agent can return `Completed=true`.

### Audited-completion rerun

Rerun 19 used the evidence-gated distribution with the unchanged 36-turn task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun19`
- Result: failed at the 36-turn bound
- Tool calls: 35
- Prompt tokens: 614,364
- Completion tokens: 8,248

The agent reached clean validation, original package audit, relocation, and
relocated audit. Its completion audit then re-opened engine status, validation,
and both runtime inspections instead of accepting an unsupported narrative. It
ran out of turns before an audited final response. The only failed tool call
used `frame` instead of the canonical runtime-inspection field `Frames`.

Independent installed verification established that this run's artifacts were
in fact complete: both scenes had zero issues, the visible principal 3D and 2D
bodies each reported Y delta -1.267, and fresh independent audits of the
467-file original and relocated packages passed run, integrity, capture, and
nonblank checks. The fixed benchmark measures autonomous completion inside the
bound, so complete artifacts without the final audited response remain a fail.

The generic efficiency repair expands narrow type-directed normalization with
`frame` to `Frames` and `packageDirectory` to `PackagePath`. These aliases apply
only when the chosen command request declares the canonical field and therefore
do not weaken unrelated schemas. Existing `frameCount` and `archivePath`
normalization remains covered.

### Visible-delivery rerun

Rerun 20 used the request-alias distribution with the unchanged 36-turn task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun20`
- Result: failed at the 36-turn bound
- Tool calls: 32
- Prompt tokens: 688,007
- Completion tokens: 16,335

The aliases eliminated the prior malformed runtime and package-path fields, but
the agent still spent one call on an invented
`rekall.tools.search_component_schemas` name and attempted packaging before
scaffolding the required playable module. It reached clean validation, runtime
inspection, original audit, relocation, relocated audit, and further completion
audits, but never returned an audited final answer.

Independent installed verification found zero validation issues; a visible 3D
dynamic body with Y delta -1.267; and fresh passing run/capture/nonblank audits
of the 218-file original and relocated packages. The moving 2D body also had Y
delta -1.267, but lacked a renderer, so the scene's one visible renderable was
only the static floor. The visible dynamic-body requirement therefore remained
unmet even outside the turn-bound failure.

The generic correction makes the exact component-schema search namespace
explicit, requires every requested visible simulated body to have a renderer in
addition to its physics composition, and directs agents to scaffold a playable
module before the first package call when the project has none. A regression
test locks all three bounded-delivery requirements into the embedded contract.

### Audit-efficiency rerun

Rerun 21 used the visible-delivery distribution with the unchanged 36-turn
task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun21`
- Result: failed at the 36-turn bound
- Tool calls: 34
- Prompt tokens: 663,401
- Completion tokens: 10,676

The agent authored complete artifacts and produced a final-looking response on
turn 36, but the evidence gate correctly returned `Completed=False` because no
turn remained to audit that proposal. Independent installed verification found
zero validation issues, visible rendered 3D and 2D dynamic bodies with Y delta
-1.267, and fresh passing run/capture/nonblank audits of the 467-file original
and final relocated packages.

The remaining waste was audit-side rather than artifact-side. The trace
repeated component-schema discovery despite a sufficient initial query, then
revalidated, reinspected, reaudited, and attempted another relocation after the
existing relocated package had already passed. One repeated relocation failed
because its destination correctly already existed; the agent then created and
audited a second relocated copy.

The generic correction now tells initial schema discovery to be consolidated
unless returned evidence explicitly omits a required concept. The completion-
audit instruction reuses current direct passing evidence and prohibits repeated
package creation or relocation unless evidence is missing, contradicted, or
stale after a later mutation. The audit remains strict; it avoids redundant
work rather than accepting weaker evidence.

### Wrapper-prefixed frame rerun

Rerun 22 used the audit-efficiency distribution with the unchanged 36-turn
task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun22`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 545,575
- Completion tokens: 8,024

Model variance produced `fabricFrameCount` and `fabricFrames` instead of the
canonical `Frames`. The agent repeated the same missing-field failure across
eight runtime-inspection calls and exhausted the bound before packaging.
Independent installed verification found a clean two-scene project with visible
3D and 2D dynamic bodies and Y delta -1.267, but no playable package existed.

The type-directed request normalizer now accepts the two observed wrapper-
prefixed fields only when the selected request type declares canonical
`Frames`. Their scalar values still pass through the existing typed conversion,
and unrelated request contracts remain unaffected. A regression extends the
same alias test that covers `frame` and `frameCount`.

### Empty-scene scaffold rerun

Rerun 23 used the wrapper-recovery distribution with the unchanged 36-turn
task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun23`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 572,246
- Completion tokens: 15,997

The agent repeatedly attempted to establish the requested project with two
named but empty scenes before applying content incrementally. The atomic
project command rejected every empty scene, after which malformed component
nesting and repeated repair calls consumed the run. It never reached runtime or
packaging; only the project and scene documents existed afterward.

Empty scenes are a valid engine-general authoring state, so the blueprint
contract now accepts them. The project workflow can transactionally establish
all named scenes with empty entity arrays, and the ordinary scene blueprint
supports an empty array as a no-op or an explicit clear when `clearExisting` is
true. Schema descriptions expose the recovery path, and a two-scene regression
proves persisted empty scaffolds without weakening entity/component validation.

### Atomic-fallback rerun

Rerun 24 used the empty-scene distribution with the unchanged 36-turn task.

- Project: `Artifacts/BenchmarkRuns/installed-broad-rerun24`
- Result: failed at the 36-turn bound
- Tool calls: 36
- Prompt tokens: 573,004
- Completion tokens: 15,500

The empty-scene capability eventually established the project, but only after
four substantially similar oversized atomic-blueprint deserialization failures.
The agent reached validation and runtime inspection, then stopped after package
preflight and module scaffolding without producing a package.

Independent installed verification found one remaining camera-layer warning,
genuine 3D/2D motion, but no renderer on either requested dynamic body. The 2D
scene reported zero visible renderables. The artifacts and bounded completion
both failed the acceptance bar.

The embedded contract now makes the recovery deterministic: try the complete
atomic project blueprint once; on argument or structure failure, retry once with
the same named scenes and empty entity arrays, then apply smaller per-scene
blueprints. It explicitly prohibits repeating substantially the same failed
blueprint arguments. A regression locks the fallback wording into the installed
agent contract.

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
