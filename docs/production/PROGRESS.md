# Rekall AGE Production Progress

This is the durable execution ledger for Rekall AGE. Update it only from
verified repository or acceptance evidence. Conversational recency does not
change the priority order.

Last verified: 2026-08-21 12:40 Africa/Johannesburg

Branch: `codex/production-foundation`

Latest milestone: command dispatch now fails closed on unknown top-level
arguments and the complete installed product gate passes under download load

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

## Acceptance benchmark queue

1. Current: complete the unchanged Lumen Vault gameplay-authoring gauntlet
   through real installed binaries and real Ollama, including compiled
   game-authored behavior, semantic input, runtime state transitions, proof
   capture, packaging, and package audit.
2. Next visual-effects class: acquire a rights-compatible nature image from the
   internet with source/provenance recorded, import it through generic asset
   contracts, present it across the full player window, and author moving
   raindrops-on-glass through generic material, shader, buffer, sampler, UV,
   transparency/blending, and engine-time primitives. Acceptance requires at
   least two temporally distinct captured frames proving real animation, plus
   validation, packaging, and package audit. The engine must not contain a
   nature-scene or raindrop-specific built-in; the Ollama agent authors the
   effect from inspectable general-purpose capabilities.
3. After the arbitrary described-game path is reliable, prove a complete
   playable Pong game through the same generic contracts.
4. Platform track after the desktop authoring loop is reliable: publish games
   as static browser deployments through .NET WebAssembly and a WebGPU renderer
   backend, with ahead-of-time compiled game-authored modules and browser-native
   input, audio, storage, and networking adapters. WebGL2 is a later bounded
   compatibility tier, not the primary rendering contract. Preserve the same
   generic world/runtime and authoring ABI; do not fork game semantics by
   platform.

## Verified status

- Generic command dispatch now rejects unknown top-level arguments before a
  command can execute or mutate state with stable code
  `REKALL_COMMAND_ARGUMENT_UNKNOWN`, the exact unknown and allowed fields, the
  bounded command contract, and native structured-value repair guidance.
  Supported aliases are replaced by their canonical fields during
  normalization, so strict binding does not break documented compatibility.
  Malformed runtime-inspection calls reach typed binding before checkpoint
  policy can hide the defect. Missing-required-field errors now also project C#
  constructor parameters through the command JSON naming policy, returning
  exact copyable names such as `projectName` rather than `ProjectName`. The
  new casing regression failed first and all 12 dispatcher tests then passed.
  Focused dispatcher/agent coverage passed 51/51.
  The production gate also exposed a real scheduler-contention boundary: the
  prior one-second restricted-module request deadline rejected a valid
  400-millisecond module during the full suite while Ollama downloaded a large
  model. A 1.2-second valid request reliably reproduced the boundary before the
  deadline was raised to two seconds; three consecutive focused runs now allow
  that request while still terminating a five-second hung module. The locked
  build completed with zero warnings/errors; both independent passes completed
  1,028/1,028 engine and 7/7 Studio tests, and the complete installed matrix
  passed under the continuing download load. The 1,186-payload archive is
  201,618,693 bytes with SHA-256
  `5122140FE8B74065EE349EC23F9A94284DD048DB6FFD5B7C9BECB08606AB0FB8`.
  Qwen 3.8 benchmark 47 is next after the model pull completes.
- Browser game publishing is architecturally viable but not implemented. The
  managed world/runtime and generic authoring contracts are the reusable base;
  the current native Vulkan/SPIR-V renderer, Windows AppContainer module host,
  and desktop player cannot run in a browser. The production direction is a
  WebGPU renderer and browser host over .NET WebAssembly, ahead-of-time module
  compilation rather than in-browser dynamic compilation/loading, browser
  capability adapters, and automated multi-browser gameplay proof. This track
  remains behind closing the installed arbitrary-game authoring benchmark so it
  reuses a proven runtime ABI instead of destabilizing the core prematurely.
- Clean installed real-Qwen benchmark 46 authored a nonblank 11-renderable
  scene with coherent player and seal entities, semantic input, camera, light,
  floor, colliders, and agent-owned state. It nevertheless exhausted 64 turns
  with the runtime module still at its scaffold and no package. The decisive
  contract defect was silent argument dropping: repeated runtime inspections
  supplied plausible `inputFrames` and `frameCount` fields instead of `inputs`
  and `frames`, plus JSON-encoded assertion strings. AGE ignored the unknown
  names and reported only missing checkpoint coverage, so Qwen spent dozens of
  turns permuting an ineffective shape instead of receiving an exact binding
  error. Evidence SHA-256 is
  `EC7DFD8B23F49C4E4081D835CA678FD820F0D0743BD9D0D318AA85BF1D01CED5`.
  The next implementation item is fail-closed unknown argument validation with
  exact allowed names and native structured-value repair guidance across the
  generic command dispatcher.
- Module builds now reject stale immutable-world lineage with
  `REKALL_MODULE_IMMUTABLE_WORLD_STALE_BASE` and the exact continuation variable,
  and reject mutation of an outer world inside an entity-update callback with
  `REKALL_MODULE_IMMUTABLE_WORLD_NESTED_MUTATION` and a sequential-repair rule.
  The bounded preflight masks comments and strings, preserves valid chained
  mutation and read-only callback queries, reports source lines, and issues no
  trusted build receipt on rejection. The embedded agent contract and compiled
  SDK inspection expose the same rule and copyable pattern. The exact installed
  Benchmark 45 source now fails before compilation with the stale-lineage
  diagnostic. Focused build/agent/SDK coverage passed 18/18. The locked Release
  build completed with zero warnings/errors; both independent passes completed
  1,026/1,026 engine and 7/7 Studio tests, and the complete installed matrix
  passed. The 1,186-payload archive is 201,614,635 bytes with SHA-256
  `A390D3B8ACBA938C98A43B75A1DC1FBEE7CD147FB17FEB49C839E8FE7A15F36E`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  Clean installed benchmark 46 is next.
- Clean installed real-Qwen benchmark 45 confirms that the discarded-mutation
  preflight and destructive checkpoint guard both change behavior, then exposes
  two subtler immutable-world hazards. Qwen authored and compiled semantic
  delta-time movement, seal contact/progress/completion, and reset behavior. It
  assigned mutation results, but later assigned `updatedWorld` from stale
  `world`, silently discarding earlier movement. It also mutated the outer
  immutable world from inside an entity-update callback and then overwrote that
  nested result when the callback operation returned. Duplicate `PlayerOrb`
  names obscured checkpoint identity; late repair deleted the coherent player
  and retained its sparse shell. AGE correctly blocked delivery after 64 tools:
  the final scene had two renderables, no camera, no package, and evidence
  SHA-256
  `711BF15F71B83590409BAF1323AED0190A6D4018727811BB707D793F9E8A08B4`.
  The next implementation item is fail-closed, exact-repair module source
  diagnostics for stale immutable-world lineage and nested mutation before a
  trusted build receipt is issued.
- Clean installed real-Qwen benchmark 44 confirms the logical-entity contract
  works, then exposes two deeper generic hazards. Qwen initially authored eight
  coherent entities: each seal held its geometry, transform, and trigger; the
  floor held collider, geometry, and transform; and the player held state,
  rigid body, collider, and transform. It authored and compiled a substantial
  movement, collection, progress/completion, and reset module. The final source,
  however, discarded the immutable results of bare
  `world.UpdateEntitiesWithComponent(...)` calls, making movement and collection
  no-ops. Before a qualifying checkpoint, it then applied `clearExisting=true`
  with one player and deleted the valid arena. AGE correctly blocked delivery;
  the run ended after 76 tools with no package, two renderables, a missing-camera
  blocker, and evidence SHA-256
  `00AD67FA477FE7F807B8A1379F328859886BFBB08502B4CC72490FDBC4FD9FCD`.
- Module builds now reject a bare discarded immutable-world mutation such as
  `world.UpdateEntitiesWithComponent(...)` with
  `REKALL_MODULE_IMMUTABLE_MUTATION_DISCARDED`, source line evidence, and the
  exact `world = world.Update...` repair before issuing trusted build receipts.
  The embedded contract states the same immutable-world rule. While the first
  executable checkpoint is pending, agent policy now blocks destructive
  `clearExisting=true` scene replacement with
  `REKALL_RUNTIME_CHECKPOINT_DESTRUCTIVE_REPLACEMENT_DEFERRED`, while retaining
  safe `clearExisting=false` upserts and targeted entity/component prerequisite
  repairs. Both defects have red/green regressions. The gate also exposed a
  pre-existing cancellation-test race: its token could expire during the new
  source preflight before the fake compiler existed. Cancellation now begins
  after the injected compiler process starts, preserving deterministic proof
  that external cancellation terminates the process; it passed three focused
  runs. The final uninterrupted zero-warning/error gate passed 1,023/1,023
  engine and 7/7 Studio tests twice plus the complete installed matrix. Its
  1,186-file archive is 201,602,891 bytes with SHA-256
  `CAFA899BFBD3FFE265A489031F3C63BAA70F3812EE715D48A34A1E0C73DB6EC3`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  Clean installed benchmark 45 is next.
- Clean installed real-Qwen benchmark 43 confirms malformed-blueprint recovery
  now changes behavior, then exposes logical-entity composition and runtime
  evidence repair as the next generic blockers. After early invalid broad calls,
  Qwen switched to valid small blueprints and targeted entity/component tools,
  authored and compiled a substantial delta-time movement, seal collection,
  progress/win, and reset module, passed movement checkpoints, and produced a
  nonblank frame with seven renderables. It nevertheless split transforms,
  geometry, and state across sibling `FooTransform`/`FooMesh` entities; its
  module then treated exact `EntitiesNamed("EnergySeal")` as a prefix query for
  `EnergySeal1/2/3`. No seal transition could occur. The protected repair loop
  spent its remaining turns permuting assertion fields and temporarily attaching
  an unrelated seal component to the player, so AGE correctly blocked packaging
  after 76 turns/75 tools. Two validation warnings remained. Evidence SHA-256 is
  `88AAC1948297D6629B8B86C5853618881CECBF20F42882F7F500D105A03866BC`.
- Blueprint and embedded-agent contracts now state that transform, render,
  collider/body, input, and agent state for one logical runtime object belong on
  the same entity, never separate `FooTransform`/`FooMesh` siblings.
  `EntitiesNamed` SDK inspection and compiler recovery now explicitly state its
  case-insensitive exact-name semantics and direct numbered/grouped queries to
  `EntitiesWithComponent`, `EntitiesWithTag`, or their intersection. Three
  recent failed runtime inspections trigger a bounded circuit-breaker that
  forbids unrelated proof components and assertion weakening, supplies the
  exact component-property assertion shape, and redirects repair to the authored
  rule and scene prerequisites. The locked gate also exposed two load-sensitive
  Windows AppContainer reliability issues: a 250 ms valid-request deadline was
  too narrow under full-suite scheduling pressure, and the isolation harness
  reused one cancellation budget for sequential process-exit and stderr-drain
  phases. Restricted requests now allow one bounded second while the existing
  five-second hung-module test remains fail-closed; exit and bounded diagnostic
  collection retain independent ten-second budgets. The new jitter regression,
  hung-module termination, and 256 KiB stderr drain/bound test passed three
  consecutive focused runs. The final uninterrupted zero-warning/error gate
  passed 1,021/1,021 engine and 7/7 Studio tests twice plus the complete
  installed matrix. Its 1,186-file archive is 201,598,814 bytes with SHA-256
  `5AA2EAD44C58C6DE78811B99EAFBC232899D3F9E7E9585468BE17B1452980430`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  Clean installed benchmark 44 is next.
- Clean installed real-Qwen benchmark 42 failed before meaningful state proof
  because 18 of 23 blueprint calls used invalid or unsupported structure. The
  recurring shapes nested complete entity objects inside `components`, split
  `type` and `properties` across adjacent component objects, or passed a deeply
  malformed JSON-encoded entity tree. Qwen still compiled a substantial
  progress/reset module and passed a thin movement checkpoint, but ended with
  eight entities, three renderables, four validation issues, no camera/package,
  and no state transition after 64 turns/61 tools. Evidence SHA-256 is
  `CC0DC0A09B83B909DBBCB36F8FB7D7892540FBB608A36FD418A53801932C8477`.
- Dynamic JSON argument failures now append a bounded copy of the declared
  command contract. Blueprint validation states the exact flat topology:
  entities are siblings in the top-level `entities` array; every component is
  one object containing `type` and optional `properties`; entity fields never
  belong inside components. After three recent blueprint failures—even with
  different arguments—the agent loop injects a circuit-breaker that stops broad
  retries and directs one small flat repair or targeted `rekall.component.add`.
  Red/green dispatcher, blueprint, and agent-policy regressions pass. The locked
  zero-warning/error gate passed 1,019/1,019 engine and 7/7 Studio tests twice
  plus the complete installed matrix. Its fresh archive is 201,596,332 bytes
  with SHA-256
  `AC7114DD78552662336965086F03B6BE85BBD43B3A5986FDAE590261F4296EF1`;
  zero run-scoped build temp directories remained. Clean installed benchmark 43
  is next.
- Clean installed real-Qwen benchmark 41 confirms runtime evidence now fails
  structurally without exceptions and isolates destructive partial upserts as
  the next generic blocker. Qwen authored and compiled a coherent rules module
  with `CurrentProgress`, `GameComplete`, reset, semantic movement, contact,
  seal deactivation, and progress recomputation; built a nine-entity scene with
  five renderables; and passed several movement/component checkpoints. AGE
  correctly blocked package delivery because progress never changed. The final
  targeted component-identity repairs used partial scene blueprints that
  replaced whole entity component sets, stripping seal transforms/renderers;
  numeric delta evidence also lacked an explicitly authored initial value and
  the single input sample drove only one of twenty frames. The run ended red
  after 76 turns/71 tools with zero validation issues, no package, and evidence
  SHA-256 `5C720F38EE2D90A63AE4CCF963E6DC2D4A23705CDBDCFEF2DBA39191FD441719`.
- Non-clearing scene blueprints now perform safe partial upserts for uniquely
  matched id/name entities: component properties merge by exact component type,
  and unspecified stable id, tags, parent, visibility, lock state, transforms,
  renderers, and other components are preserved. `clearExisting=true` retains
  exact scene replacement semantics, while targeted removal commands provide
  deletion. Runtime tool and stateful-gate descriptions now state that each
  input sample drives only its corresponding frame and numeric delta assertions
  require an explicitly authored initial property. The partial-repair red/green
  regression plus all 35 language-agent tests and both blueprint behavior tests
  pass. The locked zero-warning/error gate passed 1,018/1,018 engine and 7/7
  Studio tests twice plus the complete installed matrix. Its 1,186-file archive
  is 201,594,233 bytes with SHA-256
  `29FB0FC0F44D4CCE61C29F85DAE4BE5CD504B24B6CF69E22E89A42FE5830A956`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  A clean installed rerun is next.
- Clean installed real-Qwen benchmark 40 proves the stateful gate changes
  authoring behavior but exposed runtime-inspection robustness defects. Qwen
  authored and compiled genuine `PlayerState`, `SealState`, and `HUDScore`
  contracts with delta-time semantic movement, distance-based seal contact, and
  state mutation instead of scaffold-only motion. Delivery remained blocked
  because the state assertion targeted a nonexistent `PlayerState.Active`
  property; later scene churn removed the attached state, and a final assertion
  omitted `entityName`. `rekall.runtime.inspect_scene` then threw a raw
  `NullReferenceException`; the run correctly ended red after 76 turns/76 tools,
  with zero validation issues, four renderables, no package, and evidence
  SHA-256 `5D025EC5564A6462EF9B289247CA2402A899FC493B36D6AA320AE949E4C96650`.
- Runtime assertion validation now runs before simulation and rejects blank
  `entityName`, `subject`, or `operator` fields with bounded
  `REKALL_RUNTIME_ASSERTION_FIELD_REQUIRED` errors and exact argument targets;
  failed-summary bounding is null-safe as a second line of defense. Semantic
  input validation also runs before simulation. `changed.component.property`
  now reports an absent property directly rather than the misleading value
  `false` when it is missing from both initial and final state. Both defects have
  red/green regressions and all nine runtime-inspection focused tests pass. The
  locked zero-warning/error gate passed 1,017/1,017 engine and 7/7 Studio tests
  twice plus the complete installed matrix. Its 1,186-file archive is
  201,589,933 bytes with SHA-256
  `E8B621715EFC354F97D3D9C4F9D26EB83B722FDAE32886B5E0B6A74564A50487`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  A clean installed rerun is next.
- Clean installed real-Qwen benchmark 39 demonstrates both the SDK-recovery
  improvement and the next false-positive boundary. `qwen3.5:35b` compiled its
  runtime module without a failed build, reached zero validation issues, a
  nonblank 960x540 frame with 15 renderables, a 38 MB package, and a successful
  structural package audit after 85 turns/71 tools. Evidence SHA-256 is
  `045D63DC82AF2345F141AC88E4F6D70344FD4E9C6D9E2D7BE2AD06BF399EEA2F`.
  Independent source and frame review rejects it as a gameplay pass: the rules
  module only applies scaffold `ValuePerSecond` movement and contains no seal
  contact, progress, completion/HUD, or reset logic; its runtime assertions
  prove movement and a static property only. The package audit is structurally
  correct but not sufficient task evidence.
- Stateful task evidence now derives from generic behavioral terms such as
  collection/contact, score/progress, reset, health/damage, timers, spawning,
  and destruction. Such tasks cannot unlock delivery or narrative completion
  with movement or a static property assertion: a fresh runtime inspection must
  also prove `delta.component.property` against zero or
  `changed.component.property == true` for agent-owned state. Missing proof
  receives a bounded repair reserve. The red false-pass regression and all
  35 language-agent policy tests pass. The locked zero-warning/error gate passed
  1,015/1,015 engine and 7/7 Studio tests twice plus the complete installed
  matrix. Its 1,186-file archive is 201,586,790 bytes with SHA-256
  `51E6DB118BD1682D2C5834F330A7572EE45E092E1A993F0153B95B535DEBCD04`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  A clean installed rerun is next.
- Task-specific Studio completion now rejects narrative self-audits until a
  configured completion-audit tool has succeeded, with no intervening tool call
  before the evidence-backed final response. The strict contract is explicit at
  the language-agent and project-session boundaries and enabled only for
  task-specific Studio automation, preserving generic gauntlet behavior. Red
  regressions were followed by 40/40 language/session and 7/7 Studio focused
  passes. The locked zero-warning/error gate then passed 1,013/1,013 engine and
  7/7 Studio tests twice plus the full installed-product matrix. Its 1,186-file
  archive is 201,584,539 bytes with SHA-256
  `2D5CFBFB86B53C5E7A6D92DE8114D9768D2B1546B7C3F9B3415495609F0AA985`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 38 proves the strict contract fails
  closed. Local `qwen3.5:35b` created seven renderables and a game-authored
  `LumenVaultRuntime` module but produced no package or audit; Studio returned
  `turn_limit`, a blank outer viewport, four blocking validation issues, and
  `Succeeded=False` after 59 tool calls rather than accepting an unsupported
  completion narrative. The final generic bottleneck was typed SDK repair:
  Qwen compared a `ComponentBoolean` result with an integer, passed numeric
  values to `WithComponentBoolean`, and supplied a boolean fallback to the
  double-valued `InputActionValue`. Evidence SHA-256 is
  `56ABB8FD0000CE02DFABF7889BFFE9E783EC56C4FFB2EB2A090961FF5BFDD604`.
  A new red/green build-command regression now requires bounded compiler
  recovery to show exact bool read/write and semantic reset-action forms; its
  two focused recovery tests pass. The locked zero-warning/error gate passed
  1,014/1,014 engine and 7/7 Studio tests twice plus the complete installed
  matrix. Its 1,186-file archive is 201,585,180 bytes with SHA-256
  `7104C0B6752840FD01DF37884CB6139E0CA3AF71E033F45C156A9CBF5B5E5769`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
  A clean installed rerun is next.
- Failed package audits now inject bounded, task-anchored recovery: agents must
  repair the original requested entities, visuals, HUD, and behavior; generic
  `Cube/Test/Demo/Fault` filler is explicitly rejected; and scene/module changes
  require fresh validation, requested runtime assertions, package creation, and
  package audit. The audit reason remains direct tool evidence and AGE does not
  author content for the agent. All 33 language-agent tests passed. The locked
  zero-warning/error gate passed 1,012/1,012 engine and 7/7 Studio tests twice
  and the complete installed matrix. Its 1,186-file archive is 201,583,675
  bytes with SHA-256
  `6DD3F185B7A3156354955F876ACC9E47CBFE1AFE2344E40F3BE3ADCC05D96BBB`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 37 stopped before package audit recovery
  could apply. It compiled `GameplayModule` and passed two semantic movement
  assertions, but had zero renderables, no package, and no package audit. Qwen
  then emitted a completion narrative; the ordinary completion-audit prompt was
  followed only by `rekall.context.engine_status` and another narrative, which
  the agent loop incorrectly accepted as completed after 26 calls. Studio's
  outer acceptance correctly remained red and reported a blank viewport. The
  exact fail-open defect is that `completionAuditPending` conflates a requested
  narrative self-audit with successful configured audit-tool evidence.
  Task-specific Studio automation must require a successful
  `rekall.workflow.audit_playable_package` before narrative termination.
  Evidence SHA-256 is
  `31670F495385DD5318859BB1951FBB6AE378EFC6DBF6AC5443D73A2ECDBE4C66`.
- The post-runtime delivery reserve is now 16 bounded turns. It activates only
  once after a qualifying successful gameplay checkpoint, does not increase the
  general authoring budget, and retains the global 256-turn hard ceiling. A
  red/green scripted delivery regression and all 32 language-agent policy tests
  passed. The locked zero-warning/error gate passed 1,011/1,011 engine and 7/7
  Studio tests twice and the complete installed matrix. Its 1,186-file archive
  is 201,582,146 bytes with SHA-256
  `5F8DBD4B3843FD8A06949F448AE687FC01CA49CA2539B447140A47905ED5EDCC`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 36 reached package creation at call 26,
  produced an 85 MB archive, ran package audit, and continued for 49 more calls
  rather than expiring immediately. It compiled both `PlayerMovementSystem` and
  `GamePlayable`, passed semantic movement assertions, and ended with a nonblank
  960x540 Studio viewport. The run remained red at 55/75 successful calls with
  two renderables, a stale package, blocking validation, and no passing final
  audit. The audit's uninformative-frame failure did not provide an anchored
  recovery directive; Qwen added unrelated `Cube`/`CubeFaulted` validation-demo
  content and an unresolved `default` shader instead of completing the requested
  arena, orb, seals, HUD, completion, and reset behavior. The next generic agent
  correction is a bounded failed-audit recovery message that requires repairs
  against the original task and prohibits diagnostic filler content. Evidence
  SHA-256 is
  `AE7280A767B2210735484740CE5C39AF77E4A6EBB0359B9864DB35D384EE45D4`.
- Passing gameplay checkpoints now give just-in-time package ordering: when the
  task requires a package, agents are told to scaffold the generic
  `rekall.module.scaffold_playable` adapter before the final build, keep all
  world gameplay in the runtime-system module, and refresh runtime proof once
  after that build. The complete language-agent policy suite passed 31/31. The
  locked zero-warning/error gate passed 1,010/1,010 engine and 7/7 Studio tests
  twice and the complete installed matrix. Its 1,186-file archive is
  201,581,919 bytes with SHA-256
  `94A1D028DBBBC26BEE15A484DB4923ED083949BD9A2A81472D2A22895CA02B82`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 35 proved the new ordering. After a
  passing three-assertion gameplay checkpoint, Qwen immediately discovered and
  scaffolded `LumenVaultPlayableShell`, built both modules, and refreshed a
  passing runtime checkpoint. The run compiled real world gameplay with three
  semantic actions and finished with a nonblank 960x540 Studio viewport, but
  remained red at 45/75 successful calls with one renderable and no package or
  audit. The exact remaining policy limit is measured: the late checkpoint at
  turn 69 granted only eight bounded delivery turns, all consumed by adapter
  discovery/scaffolding/build, required visual-schema discovery, one correctly
  deferred validation, and the refreshed runtime proof at turn 77. Complex
  packaged tasks need a larger but still bounded post-runtime delivery reserve
  for visual repair, validation, package creation, and audit. Evidence SHA-256
  is `00AE88F02C0C5155EE28AA59A2B5D12E990E66BC95168EB5D6CCBBF14052DFEE`.
- Semantic input-map evaluation is now independent of an entity's visual
  `Visible` flag. Hidden configuration entities can project actions, while the
  map's explicit `Active` property remains the authoritative enable/disable
  switch for both visible and hidden entities. Focused input/runtime/UI coverage
  passed 17/17. The locked zero-warning/error gate passed 1,010/1,010 engine and
  7/7 Studio tests twice and the complete installed matrix. Its 1,186-file
  archive is 201,582,200 bytes with SHA-256
  `742DB0C5DAB7F1CD598616F59ACCACD12CC8D845D90C87E11A49CD3CD4203F2D`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 34 reached a passing executable gameplay
  checkpoint: its authored module compiled, semantic `move.horizontal` input
  projected two declared actions, four runtime assertions passed, and the orb's
  strict X-position delta changed under engine delta time. The viewport was
  nonblank at 960x540 with five renderables. The run remained red after 77/82
  successful calls, with no package or audit: Qwen delayed the separately
  required generic `IRekallAgePlayableModule` package-proof adapter until the
  first package attempt exposed its absence. Scaffolding/building that adapter
  correctly invalidated the earlier runtime proof, and the following package
  call was therefore deferred at the protected turn limit. The next generic
  agent-contract correction is to front-load the adapter immediately after a
  gameplay checkpoint, before the final build/inspection/package sequence.
  Evidence SHA-256 is
  `A62D352CE41BBE821B46F1E84DC9108DE940A3850769EC766F9EB6387CB65944`.
- Front-loaded runtime assertion evidence: failed inspection summaries now lead
  with bounded entity, subject, component/property, operator, expected value,
  actual value, and comparison explanation before large subsystem/entity data.
  At most eight details are included, every field and the 4,000-character total
  are bounded, overflow is counted, and all structured results remain intact.
  Runtime/CLI/agent regression coverage passed 84/84. The locked zero-warning/
  error gate passed 1,007/1,007 engine and 7/7 Studio tests twice and the
  complete installed matrix. Its 1,186-file archive is 201,581,877 bytes with
  SHA-256
  `A985056BB845D5E9ED4267058DA79447162DC7AAD5C17B3B1881B0BAF72585D7`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 33 proved the diagnostic contract: its
  failed runtime summaries exposed the exact missing `PlayerState`, missing
  component-type arguments, missing numeric state, and `delta.position3d.x`
  actual value `0`. Qwen used those facts to reduce four failed assertions to
  one and used the populated compiler recovery to return to successful builds.
  The run remained red at 55/75 successful calls, with one renderable, no
  nonblank proof, and no package; its final source repair was not rebuilt or
  retested before the protected limit. The next generic runtime defect is exact:
  its valid `Rekall.InputActionMap` lived on an intentionally non-rendered
  `visible:false` configuration entity, but `runtime.input.actions` discarded
  the whole entity and reported `inputActionCount: 0`. Input maps already have
  an explicit `Active` property, so visual visibility must not silently disable
  semantic controls. Evidence SHA-256 is
  `D1B03B427B90A2E7DF61D97AEA2943C98A40EA587518FC1CEE4EB7BDD7C966AB`.
- Runtime SDK compiler recovery: failed runtime-module builds now put exact
  immutable entity/transform/component/update patterns before verbose compiler
  diagnostics and return populated SDK-inspection plus source-list suggestions.
  AGE does not rewrite or author the game source; ordinary compiler errors and
  timeout/cancellation semantics remain authoritative. Focused build/scaffold/
  SDK coverage passed 14/14. The locked zero-warning/error gate passed
  1,006/1,006 engine and 7/7 Studio tests twice and the complete installed
  matrix. Its 1,186-file archive is 201,577,852 bytes with SHA-256
  `D3E00E027AD3FBDE71C553011B21FCD643DACB9E477B9EF70AC2FA55ADDEAE6B`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 32 proved compiler recovery: the authored
  `LumenVaultRules` module compiled on its first build at tool call 19, later
  builds recovered after edits, and 13 real runtime inspections executed. One
  checkpoint passed, the final viewport was nonblank at 960x540 with six
  renderables, and the protected run expanded to 73 tool calls. It remained red
  with no package after later scene/module mutations invalidated the proof and
  repeated runtime assertions failed. The generic diagnostic defect is now
  measured: serialized runtime results put large subsystem/entity state before
  `AssertionResults`, so the bounded LLM tool output can omit the failed
  subject's exact actual value even though the command promises bounded repair
  evidence. The next tranche puts compact failed assertion summaries and actual
  values at the beginning of the command result while retaining full structured
  inspection data. Evidence SHA-256 is
  `D6F3A52B9460823A3E05AFFAD34900D05D7448BDC68E26E89D3787B20AACD413`.
- Runtime checkpoint component identity now matches generic module authoring:
  exact non-`Rekall.*` runtime identities are eligible agent-owned state whether
  scaffold-qualified (`Game.*`) or exact authored CLR names, while canonical
  engine-owned components cannot substitute for game state. Actual component
  attachment and assertion truth remain enforced by runtime inspection. Focused
  red/green coverage passed 2/2 and the complete language-agent selection passed
  31/31. The locked zero-warning/error gate passed 1,005/1,005 engine and 7/7
  Studio tests twice and the complete installed matrix. Its 1,186-file archive
  is 201,575,773 bytes with SHA-256
  `5C9262D403586F19E21D8D13B6EFE04F3A656DCB02B6BD51E5042A864ED0099B`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 31 preserved the early gameplay priority:
  it scaffolded `GameRules` immediately after its first successful scene slice
  and produced a camera plan with nine renderables. It remained red at 43/57
  successful tool calls with no successful module build, runtime inspection,
  proof frame, or package. Qwen replaced correct scaffold/SDK patterns across
  eight source writes with invented calls including `Transform3D`,
  `ReadTransform3D`, `GetTransform3D`, `GetComponentNumber`, and an invalid
  two-argument `WithPosition3D`; six compiler attempts failed. The initial SDK
  inspection had returned the correct entity-transform/component-state recipe,
  but later repair attempts repeatedly omitted the required query and searched
  component schemas instead. The next generic tranche provides bounded,
  compiler-directed rejection/recovery for known nonexistent runtime SDK call
  shapes at source-write time, preserving valid authored C# rather than adding
  game-specific code. Evidence SHA-256 is
  `B1728ADCC42B5A62FAE4E64D638F48A3EFDDAF360F9A1B2C93A658DD3AFB0C00`.
- Early runtime authoring checkpoint: tasks that require runtime behavior now
  allow at most four successful world-authoring mutations before deferring
  further non-module work until runtime-system scaffold/source authoring begins.
  The bound is request-configurable from 0 (disabled) through 32, emits
  structured recovery, and leaves non-runtime tasks unchanged. Focused policy
  tests passed 2/2 and the full language-agent suite passed 29/29. The locked
  zero-warning/error gate passed 1,003/1,003 engine and 7/7 Studio tests twice
  and the complete installed matrix. Its archive is 201,575,814 bytes with
  SHA-256
  `FD0421AA9DDF871D4854588E8B74F0A54F917F79A9C34D32455B117020839289`;
  zero reusable build nodes and zero run-scoped build temp directories remained.
- Clean installed real-Qwen benchmark 30 made 37/61 successful tool calls,
  successfully compiled the authored `LumenVaultGameplay` runtime module, and
  produced a nonblank 960x540 viewport with ten renderables. The module slice
  began after two successful scene mutations instead of benchmark 29's 47-turn
  delay, proving the early policy changes shipped agent behavior. The run still
  reached the 64-turn limit without packaging: seven final runtime inspections
  were rejected because checkpoint coverage recognizes only `Game.*` component
  names, while the engine's own runtime-system scaffold and component registry
  use CLR component names such as `LumenVaultGameManager`. The exact asserted
  component was attached, but `candidateAgentComponentAssertion` remained null.
  The next generic tranche aligns agent-owned checkpoint identity with actual
  module component contracts and rejects built-in-only substitutions without
  imposing a namespace convention the scaffold does not produce. Evidence
  SHA-256 is
  `BE1586FDEAF0928A784D59082E5646934D46AF4852501A8DF6B17A3C61E2D2C5`.
- Schema-aware component admission: the production registry injects indexed
  built-in property policy into component add, property set, and scene blueprint
  commands without coupling the generic world layer to module schemas. Unknown
  names, case duplicates, encoded structured values, and numeric range violations
  fail before transaction capture or persistence with exact indexed targets and
  schema-search recovery. Valid built-ins and arbitrary `Game.*` state remain
  accepted. Focused admission/dispatch/validation coverage passed 48/48. The
  locked zero-warning/error gate passed 1,001/1,001 engine and 7/7 Studio tests
  twice and the complete installed matrix. Its 1,186-payload-file archive is
  201,572,291 bytes with SHA-256
  `FFBB2C30C6977659E43E3AACEE47FBCE4F6697C81A4BB5EB5519E6FE5F3581DA`;
  zero reusable nodes and zero run-scoped temp directories remained.
- Clean installed real-Qwen benchmark 29 made 41/61 successful tool calls,
  compiled the authored `LumenVault` module, reached zero validation issues,
  and produced a nonblank 960x540 viewport with nine renderables. Property
  admission rejected invalid authored fields at their exact blueprint indices
  and Qwen repaired them without a late remove-property loop. It remained red
  with no package because it spent 47 turns iterating scene content before
  scaffolding the required gameplay module; after the final successful build,
  its only remaining runtime call omitted inputs/assertions. The next generic
  tranche bounds pre-gameplay scene iteration and requires the thin executable
  runtime slice earlier whenever runtime behavior assertions are required.
  Evidence SHA-256 is
  `FEF947B5170416BD190435C1580FBF942B39859B23101C17765596894B016D3B`.
- Fail-closed component identity authoring: one exact catalog covers all 70
  built-in `Rekall.*` component identities and is mechanically checked against
  the module index. Direct component adds and bulk scene blueprints reject
  unknown reserved types before persistence, including case/whitespace variants,
  with conservative spelling and schema-search recovery; arbitrary `Game.*`
  components remain valid. Final validation consumes the same catalog. Focused
  world/dispatch/validation coverage passed 62/62. The locked zero-warning/error
  gate passed 995/995 engine and 7/7 Studio tests twice and the complete
  installed matrix. Its 1,186-payload-file archive is 201,558,474 bytes with
  SHA-256
  `611E63631B6F2D8AC04554287560AA5EA3A45A4E99FF5BFA5FD415BD84B06D27`;
  zero reusable nodes and zero run-scoped temp directories remained.
- Clean installed real-Qwen benchmark 28 made 57/64 successful tool calls,
  compiled the authored `GameRules` system, reached zero validation issues, and
  produced a nonblank 960x540 viewport with eight renderables. It remained red
  without a package: built-in component property defects were accepted during
  initial authoring, and Qwen spent roughly 24 late calls repeatedly validating
  and removing them one at a time before reopening an already-existing module.
  The next generic tranche validates exact built-in property names/shapes/ranges
  at component-add and blueprint mutation boundaries so invalid content never
  consumes the runtime/delivery budget. Evidence SHA-256 is
  `F8AAAEFC1569A8FE5B2F859A10F05923BA2EDD9D6F3BA2D070A9B4FB9C7A563C`.
- Post-runtime delivery reserve: a qualifying successful runtime inspection can
  extend an agent run by at most eight delivery turns, only on the turn that
  produces the fresh evidence and only once. Early checkpoints cannot arm the
  reserve later merely because budget elapsed; later mutations still invalidate
  runtime proof, and all agent/repair limits share the absolute 256-turn ceiling.
  Red-first policy coverage passes 27/27. The locked zero-warning/error gate
  passed 989/989 engine and 7/7 Studio tests twice and the complete installed
  matrix. The 1,186-payload-file archive is 201,545,117 bytes with SHA-256
  `3DC30C9445105B09ED0E5C252EB470F1DCD52F1A2BB8701EFDB1CACD5AD91567`;
  it left zero reusable compiler nodes and zero run-scoped temp directories.
- Clean installed real-Qwen benchmark 26 compiled authored gameplay, passed
  four runtime inspections, and produced a nonblank 960x540 viewport with 14
  renderables. It remained red at 48/78 successful tool calls with two invented
  HUD types and no package. The first reserve implementation armed from an old
  checkpoint as its remaining budget elapsed and added only one turn; the fresh
  post-repair checkpoint therefore could not arm it. That timing defect is now
  fixed by the verified current-turn requirement. Evidence SHA-256 is
  `FDE2804B1BB1D15D1924CA6B447679C75A306820EB612510BEE466335D820A0A`.
- Clean installed real-Qwen benchmark 27 remained red at 46/76 successful tool
  calls. It compiled `LumenVaultRules` and produced a nonblank 960x540 viewport
  with three renderables, but no runtime inspection passed and no package was
  produced. The measured generic defect is earlier in the loop:
  `rekall.component.add` accepted invented reserved `Rekall.Collider3D`, so the
  runtime reported zero compatible colliders and only final validation exposed
  the invalid type. Direct component mutation must reject unknown reserved
  types immediately and return exact schema recovery guidance. Evidence SHA-256
  is `9F3978D410ABF2F2ABB2B70D723C7A910F79DFD27E274955BAAE6D2D85BE131C`.
- Scene blueprint component normalization now accepts the canonical
  `{type, properties}` shape plus deterministic flat, `typeName`, and strict
  name/value-list representations while rejecting conflicts with precise JSON
  paths. Runtime property names remain case-sensitive, including `Type` beside
  the reserved lowercase discriminator. Focused dispatch coverage passed 26/26.
  The locked zero-warning/error gate passed 986/986 engine and 7/7 Studio tests
  twice and the complete installed matrix. The 1,186-payload-file archive is
  201,543,401 bytes with SHA-256
  `7DB2F19581316C9FACAF7263463BEF516E492865AFE3D56918C7717BE5E66ECB`.
- The locked gate now disables reusable MSBuild nodes for every outer operation
  and gives each engine-test run a unique, automatically cleaned temp root.
  This eliminated 15 orphan compiler nodes (previously consuming about 2.55 GB)
  and prevents accumulation under the shared test-temp directory. A one-time
  cleanup removed 290,083 stale Rekall test files totaling 47,936,807,924 bytes.
  The verified full gate ended with zero reusable nodes and zero run-scoped temp
  directories.
- Clean installed real-Qwen benchmark 25 made 49/73 successful tool calls and
  completed five real runtime inspections. Its final call passed three behavior
  assertions with semantic input and the authored `VaultGameplaySystem`; final
  validation had only `REKALL_UI_ELEMENT_NO_CANVAS`, and the viewport was a
  nonblank 960x540 frame with two renderables. It remains red: the successful
  final runtime checkpoint consumed the last protected turn, leaving no bounded
  opportunity to repair, package, and audit. No package was produced. The next
  generic tranche is a one-shot post-checkpoint delivery reserve, not additional
  scene-format tolerance. Evidence SHA-256 is
  `1797A54E8DA7353343BF3A94A6508938F51C283132ECE4FFECBB6A8EFCC170B3`.
- Reserved component fail-closed validation: every unknown `Rekall.*` component
  is now blocking. Repair suggestions are emitted only for a unique exact final
  segment match or a full-name edit distance of at most three; otherwise the
  validator refuses to guess. Focused validation and repair coverage passed,
  including property-preserving repair and no-suggestion behavior for an
  invented type. The locked zero-warning/error gate passed 982/982 engine and
  7/7 Studio tests twice and the complete installed matrix. The 1,186-payload-
  file archive is 201,530,567 bytes with SHA-256
  `B6B5B36BC2105BF3880750E0B2835970DFE436284F2FC4C8E7B1BDDECE28A826`.
- Clean installed real-Qwen benchmark 23 compiled an authored `GameRules`
  runtime system, projected semantic input, and produced a nonblank 960x540
  frame with two renderables. It remained red at the protected 76-turn bound:
  46/74 tool calls succeeded, final validation correctly exposed all 13
  blocking component type/property defects, and no package was produced. Nine
  `rekall.scene.apply_blueprint` calls failed while Qwen alternated among
  predictable component object representations and occasionally malformed
  encoded arrays. The next generic tranche is bounded, unambiguous scene
  blueprint component normalization with precise indexed rejection of
  ambiguous shapes. Evidence SHA-256 is
  `BE91F223DF81D16BF76DB1AA3CEA24D5BF3269A20B43C5A1419C5EEB097C85F8`.
- Bounded module compiler lifecycle: every module build has a two-minute
  engine-owned deadline. Timeout and external cancellation terminate the whole
  process tree with five-second cleanup bounds; timeout returns
  `REKALL_MODULE_BUILD_TIMEOUT`, exit `-1`, and no trust receipt. Six focused
  tests include real wedged processes. The locked zero-warning/error gate passed
  980/980 engine and 7/7 Studio tests twice and the full installed matrix. The
  1,186-payload-file archive is 201,528,876 bytes with SHA-256
  `5DE2A82788093487520C6B9E33DA42AF0DAB19038512D9B340225D57D285A4A4`.
- Clean real-Qwen benchmark 22 had no compiler hang; successful module builds
  completed in under one second with `timedOut:false`. It still failed at 64
  turns (45/76 successful calls, zero final renderables, no package). A new
  generic validator defect was measured: distant unknown reserved types such
  as `Rekall.Components.Transform3D` are silently skipped because reserved-type
  reporting currently requires edit distance <=3. Seven such hallucinations
  were hidden while only `Rekall.UICanvas` was reported. The next tranche makes
  every unknown `Rekall.*` type blocking and adds safe exact-suffix repair.
  Benchmark evidence SHA-256 is
  `2D43EAC81EAC088DC2EF0CF0DBDE175CDBE9B4E7A5C205DA3A99B8453F269CA9`.

- Runtime checkpoint argument normalization: the protected agent policy now
  evaluates bounded JSON-encoded arrays consistently with generic typed command
  dispatch, including nested input arrays, without mutating calls. Malformed,
  scalar/object-shaped, and over-1,000,000-character values fail closed. The
  focused policy selection passes 7/7. The zero-warning/error locked Release
  pipeline passed 978/978 engine and 7/7 Studio tests twice and completed the
  installed matrix. Its 1,186-payload-file archive is 201,524,293 bytes with
  SHA-256
  `6EC4582475E075B27E8E2E99383B37AD1D4E3076B535361AFDA39111E03020DF`.
- Fresh installed real-Qwen Lumen Vault benchmark 21 proved encoded arrays
  reached actual runtime inspection; semantic input projected and call 67
  passed three authored gameplay assertions. It is retained as diagnostic, not
  acceptance: a child `dotnet build` wedged without a timeout and required
  manual termination, then the recovered 64-turn run ended with one blocking
  UI-canvas issue, six renderables, and no package. The evidence SHA-256 is
  `BC644243E78375936006D3E907890B18FE93541642F48650083EF10312F2BE4C`.
  The immediate next tranche is bounded compiler timeout and process-tree
  cleanup, followed by another unchanged empty-project run.

- Agent entity query contract: `FindEntity` preserves exact opaque-id
  precedence, then resolves one unique case-insensitive exact authored name;
  duplicate names fail closed and `EntitiesNamed` remains the explicit
  multi-match primitive. A compiled project module proves name-based mutation.
  Focused TDD passed 7/7, full verification passed 976/976 engine and 7/7
  Studio tests, and the locked Release/distribution matrix passed twice with
  zero warnings/errors. The 1,186-file archive is 201,523,273 bytes with
  SHA-256
  `F8AF2C5D45182FF3FB5BB7A663BA05F74734ABDFD545435883784784AF5740A7`.
- Fresh installed real-`qwen3.5:35b` Lumen Vault benchmark 20 remained red at
  the 64-turn bound: 32/64 tool calls succeeded, the viewport was nonblank at
  960x540 but contained only two renderables, and no package was produced.
  Qwen compiled a real runtime system and authored semantic actions, but
  repeatedly JSON-encoded the typed `inputs` and `assertions` arrays. The
  mandatory gameplay checkpoint therefore never ran. The next work item is a
  generic, bounded typed-argument normalization/diagnostic contract, followed
  by the unchanged benchmark—not Studio polish or broader CI work.

- Windows distribution: fresh 200,967,116-byte win-x64 archive assembled from
  `3a84dbc` with SHA-256
  `A989D5790695578B672320B4DC89347F599D02391B1AD03D25A437EC11EFEB32`.
  The assembled directory contains 1,178 files.
- Canonical verification: 894/894 engine tests and 3/3 Windows Studio tests
  passed twice independently with four distinct retained TRX files; Release
  build completed with zero warnings and zero errors.
- Current Release verification: 950/950 engine tests and 7/7 Windows Studio
  tests pass. The full solution builds with warnings treated as errors and
  reports zero warnings and zero errors.
- Programmable material shader foundation: project vertex/fragment pairs now
  compile to real SPIR-V, reflect bounded vertex and descriptor metadata through
  Khronos SPIRV-Reflect, receive deterministic SHA-256 pipeline identities, and
  are validated against scene-material ABI version 1. Assignment uses that same
  resolver and rejects incompatible vertex formats or GPU resource sets before
  scene mutation. The focused locked Release selection passes 11/11 with
  warnings treated as errors; no vulnerable legacy reflection dependency was
  accepted.
- Shader draw propagation: authored shader-pipeline references now survive the
  generic viewport-to-mesh material binding path (including primitives,
  authored geometry, imported models, morphs, skinning, and virtual geometry)
  and are copied to the immutable draw range consumed by GPU backends. The
  focused locked Release mesh/batch selection passes 44/44 with warnings
  treated as errors.
- Native programmable-shader execution: Vulkan viewport capture resolves every
  distinct referenced project pipeline before GPU allocation, caches immutable
  opaque/transparent pipelines by SHA-256 content identity, selects default or
  authored pipelines per draw, and destroys custom pipelines/layouts/modules in
  reverse creation order. Invalid pairs fail before GPU work with bounded
  entity-specific diagnostics and no fallback. Real RTX 5090 tests proved a
  constant-magenta authored fragment shader changes captured pixels and a
  mixed two-draw frame retains both magenta custom output and green default
  output. The focused locked Release selection passes 45/45 with warnings
  treated as errors.
- Windowed programmable-shader execution: the Veldrid player now resolves and
  caches project pipelines by content hash, selects authored or default opaque
  and transparent pipelines per draw, and keeps resource binding ordered after
  pipeline selection. A recursive debounced `Shaders/` watcher invalidates live
  entries; invalid edits retain the last valid pipeline with a structured log,
  while a 64-pair bound prevents unbounded GPU residency. A real three-frame
  Windows Vulkan process created and drew an assigned shader successfully, and
  a real 300-frame process survived an intentionally corrupted live fragment
  shader after startup. The Windows Release build has zero warnings/errors and
  the focused locked Release selection passes 3/3.
- Agent shader inspection: `rekall.shader.inspect_pipeline` and
  `shader inspect-pipeline <root> <vertex> <fragment>` compile and reflect a
  project pair, then return ABI version, stable SHA-256 identity, SPIR-V byte
  counts, bounded vertex/resource metadata, validity, and bounded diagnostics
  without returning authored source. The command is registered for MCP and the
  focused locked Release command/catalog selection passes 12/12; the CLI build
  has zero warnings/errors.
- Shader validation and package integrity: the Validation layer now owns a
  dependency-inverted pipeline-validation contract, while the canonical
  Workflows composition supplies the real Rendering resolver. Playable
  verification blocks incompatible assigned pipelines and preserves their
  entity-specific shader diagnostic instead of collapsing it into a generic
  readiness failure. Packaging ships only vertex/fragment sources referenced
  by packaged scenes, includes them in the immutable SHA-256 inventory, and
  excludes unreferenced shader experiments. Consolidated audit recompiles the
  packaged scene after integrity inspection, including relocated directories
  and archives, so a shader cannot be made acceptable merely by rewriting its
  inventory hash. The focused locked Release selection passes 43/43.
- Portable material-resource ABI: native Vulkan no longer uses its legacy
  combined frame/material descriptor set and draw push constants. It now binds
  the same ABI v1 sets as the Windows player: frame uniform at set 0, an
  alignment-correct dynamic draw-uniform buffer at set 1, and seven separate
  sampled-image/sampler pairs at set 2. The default engine shaders were migrated
  to the same contract; persistent OpenXR renderers refresh both frame and draw
  uniforms on every compatible frame. The backend description now reports set
  indices and zero push-constant bytes honestly. The Vulkan scene tranche passes 80/80,
  including a real resourceful project shader on the local RTX 5090.
- Retained custom-material acceptance: `Examples/CustomMaterialShader` was
  created through public project, scene, entity, component, geometry, shader
  write, and shader assignment commands. `agent/tint` reflects all four vertex
  attributes plus frame/draw/material resources and resolves to pipeline SHA-256
  `B364F777B8DCD9D368DE9853C5A833F6D515CCCC8DB46A9B0F9CC03F787C04BF`.
  The 960x540 native Vulkan capture on `NVIDIA GeForce RTX 5090` is informative,
  uses no fallback/missing/unsupported assets or runtime observations, visibly
  separates the purple authored cube from the green default cube, and has
  SHA-256 `01AC5884D0B6E5535D2E4EEE8A109B82FCD424DA769D5DFD438EAAB3C27A12EB`.
  The resourceful Windows player completed 30/30 frames. Its graphics package
  contains 119 inventoried files, including both referenced shader sources;
  relocation and consolidated audit passed every check. The source archive has
  SHA-256 `82814A36867E3B2A55C601B460AAEBF67DBF02538BF825127215F6651ADA369D`.
- Full product gate after material acceptance: 967/967 engine tests and 7/7
  Windows Studio tests pass; the Release solution build reports zero warnings
  and zero errors. The gate initially exposed a headless Studio progress race:
  a later model failure could arrive before queued `Progress<T>` evidence. The
  headless path now reports immediately while WPF retains UI-context marshalling;
  the exact regression passed five consecutive focused runs before the full gate.
- Persistent 3D physics: the runtime now retains a BEPU simulation across
  frames, incrementally synchronizes bodies and statics, preserves angular
  motion/orientation and sleep state, and lets BEPU own contact response.
  Authored material response is projected into native contact springs instead
  of applying a second axis-aligned bounce pass after the solver. Generic world
  settings expose bounded velocity-iteration and substep counts.
- Inspectable physics evidence: `runtime inspect` reports bounded per-body
  backend, awake state, linear/angular velocity, orientation quaternion, and
  peak speeds with frame indices. The agent-authored `TumblingCubes` example
  builds from a freshly installed project-local SDK, simulates five randomly
  oriented falling cubes for 600 frames, and reports all settled bodies at
  zero angular speed. Its 960x540 RTX 5090 Vulkan capture is informative with
  zero missing assets, unsupported assets, fallbacks, or observations.
- Unified rotation contract: BEPU pose conversion now exactly matches the
  renderer's X/Y/Z matrix composition. A multi-axis regression proves the
  published Euler representation recreates the physics quaternion rather than
  producing visible flips near coupled rotations. The corrected Windows
  Vulkan player was watched live and its tumbling and settling behavior was
  confirmed visually.
- Real-time playback: runtime-observed playable games use a bounded fixed-step
  accumulator, so physics advances at 60 Hz from actual player delta time
  instead of once per rendered frame. Playable games and runtime loops now
  dispose their retained simulations deterministically.
- Direct SDK repair: agents can explicitly run `rekall.module.install_sdk` or
  `module install-sdk <root>` to install or repair the versioned project-local
  module SDK before building, without silent mutation during ordinary builds.
- Physics event parity: BEPU-backed 2D bodies now participate in the same
  generic `collision.begin`/`collision.stay`/`collision.end` and
  `trigger.enter`/`trigger.stay`/`trigger.exit` authoring contracts as 3D
  bodies. Runtime-realistic tests prove `Position2D` coordinates, explicit
  world-unit collider dimensions independent of visual transform scale, and
  exact `Rekall.CircleCollider2D` payload facts rather than accidentally using
  the unused 3D origin. The complete Release suite passes 936/936. These events
  intentionally remain deterministic bounding-radius overlap facts rather
  than exact BEPU contact manifolds; contact points, normals, impulses, and
  exact shape overlap are a
  separate production physics tranche.
- Physics SDK parity: agent-authored C# modules now have a typed `Raycast2D`
  alongside `Raycast3D`. It returns stable distance-ordered visible box/circle
  hits with optional tag/component filters, exact circle intersection, and
  exact transformed oriented-box intersection. Runtime SDK inspection exposes
  its compiled signature, usage, immutable `RekallAgeRuntimeVector2`, and
  construction guidance. `Rekall.EventBindings` schemas now document the exact
  `{ event, handler, active }` shape, generic lifecycle/pointer/2D-or-3D
  collision/trigger facts, and custom event emission.
- Physics pose parity: BEPU primitive and mesh bodies now receive authored
  Transform2D rotation or Transform3D orientation for both static and dynamic
  poses. Motion tests prove a rotated thin box blocks planar and 3D bodies at
  positions where the previous axis-aligned shape missed. Collider and trigger
  schemas explicitly define their dimensions as world-unit values independent
  of visual transform scale, matching established projects such as the
  Bouncing Ball example; BEPU, overlap facts, and ray queries now agree on that
  contract.
- Installed acceptance: canonical gate exited 0 against the freshly assembled
  product. Shipped project/module workflows, the generic game-authoring
  gauntlet, packaging and clean relocation, package audit, nonblank capture,
  Windows play, negative archive preflight, runtime UI/audio, animation,
  compatibility, atomic persistence, optimistic revisions, and damaged-file
  recovery all have installed-binary proof. Module trust reports ready with
  the `windows-appcontainer-restricted` posture. The shipped Studio also
  created a project from no prior files, traversed its Ollama adapter and agent
  tool loop, completed the gauntlet, captured a nonblank viewport, and produced
  a packaged game under deterministic model responses.
- Local agent: direct installed-AGE evidence rejected both
  `devstral-small-2:24b` and `qwen3-coder:30b` as replacements for the proven
  `qwen3.5:35b`. Devstral completed only two status calls in its fair full run;
  Qwen Coder made materially more progress at its normal temperature but did
  not complete, and a 0.15-temperature profile regressed into a 55-failure
  loop. Both Qwen Coder tags were removed. `qwen3.5:35b` is being restored as
  the default because it remains the only evaluated local model with repeated
  task-specific AGE game/package passes on this 32 GB RTX 5090.
- Studio authoring: project create/open, entity hierarchy/selection, generic
  entity/component/property mutation, scene validation, software-rendered
  viewport capture, and Windows player launch/stop now execute through the
  shared canonical command catalog. The focused checkpoint passes 7/7 tests;
  the Studio build has zero warnings and zero errors.
- Embedded AI: Studio now discovers local Ollama models, defaults to the
  installed `qwen3.5:35b`, runs/cancels bounded project-scoped authoring,
  streams turn/tool progress, and reloads, validates, and captures after the
  run. Canonical MCP execution rejects direct or JSON-string gateway attempts
  to use another project root. The focused agent/Ollama/MCP/Studio selection
  passes 41/41 across agent, Ollama, MCP, workbench, schema, catalog, and CLI
  coverage; the Studio build remains warning-free. A hidden executable smoke
  opened the authored project, stayed alive for five seconds, and was then
  stopped without an orphan process.
- Real local embedded-service proof: `qwen3.5:35b` authored and repaired a
  three-entity 3D scene with generic camera/light transforms and a colored
  cube. A final four-turn/four-tool evidence pass reported zero issues and
  captured an inspected nonblank 960x540 software frame at
  `Artifacts/StudioAgentProof/vout/Main_runtime_001.png` (3,419 bytes,
  SHA-256 `04CE4CFDC27FD73D50844FD3B3A81297A64CFF06C4870E37534395239463ED1C`).
- Real playable-game proof: the project-scoped service now accepts configured
  successful compound workflows as terminal evidence, so a model cannot waste
  later turns or mutate after the gauntlet has already passed. On a fresh root,
  `qwen3.5:35b` completed in two turns/two tools: engine status followed by
  `rekall.workflow.agent_authoring_gauntlet`. The resulting manifest reports
  passing scene-validation, module-build, restricted module-trust, and
  playtest checks. The 1,279,705-byte archive SHA-256 is
  `DECCAFEE7619D346DF48844374B80ECD31A32C999656277C3E896D3D194FC548`;
  the audited proof frame SHA-256 is
  `1D913CA3E7DB6204B7F48D04F4115B681722A129B20610BC7841FE258952C6C2`.
- Portable-game proof: the AI-created archive relocated to a clean 31-file
  destination, inspected ready, ran its packaged player for two frames with
  agent-authored module output, passed a full audit, and produced a second
  nonblank informative runtime capture with four distinct colors. Studio now
  exposes editable scene selection/Switch plus canonical Package and Audit
  Package actions, retaining the returned archive path for the audit. The
  warning-as-error Studio build and 7/7 focused workbench checks pass.
- Safe iteration: Studio Undo/Redo now restores persisted transaction
  preimages through the canonical restore command and refreshes the viewport.
  Undo itself captures an inverse preimage for real redo; multi-resource
  failure rolls back already restored resources in memory before returning a
  structured failure. Generic entity creation and component addition now
  capture preimages at the engine command layer. The combined workbench/world/
  dynamic-dispatch selection passes 21/21 and Studio builds warning-free.
- Schema-guided Studio authoring: the workbench now projects every registered
  built-in and verified project-module component schema into the inspector,
  including valid-but-undefined properties, types, editor kinds, numeric
  bounds, allowed values, descriptions, and asset kinds. Studio provides
  editable schema selectors, boolean/enum/asset choices, contextual constraint
  help, and still commits through the canonical generic component commands.
  The combined editor/module/world selection passes 36/36, the Studio build has
  zero warnings/errors, and a hidden authored-project smoke remained healthy.
- Deterministic Studio automation: a Windows-targeted test project now drives
  the real Studio view model and async commands without UI timing races. It
  creates a project and entity, chooses a registered component/property schema,
  mutates the persisted scene through canonical commands, and proves undo/redo.
  Locked restore and the zero-warning Release solution build pass; the Release
  checkpoint now passes 894 engine tests plus 3 Studio tests (897 total).
- Studio agent automation and installed proof: the shipped Studio has an
  explicit headless automation entry point that still drives its real view
  model, Ollama adapter, project-scoped agent, progressive MCP executor,
  validation, viewport, and packaging paths. A deterministic installed fixture
  completes engine discovery plus the generic gauntlet in two tool calls and
  produces a nonblank frame and audited archive. A separate real local
  `qwen3.5:35b` run completed in four turns/four tools with zero validation
  issues; its archive SHA-256 is
  `7A2DB7E70FA763932F8347C41DFD7ED3CEAE13393D68B7EC4E9D70F3F670E881`
  and viewport SHA-256 is
  `23DCEE33EF1F4D8B2D322833ED6DB9CD0C5316E3F0138BE7ED8D101EA80E0FDF`.
  The gauntlet now safely reuses a compatible open project/scene instead of
  failing with a false missing-revision conflict.
- Arbitrary-game authoring hardening: task-specific Studio sessions no longer
  accept the fixed gauntlet as terminal completion. The blueprint workflow
  safely reuses compatible open project/scene documents and rejects missing
  capabilities without rewriting them. Studio and agent schema discovery keep
  built-ins inspectable while a project module needs repair, while low-level
  module loading remains fail-closed. Tool search exposes matched native tools
  on the next turn, the runtime-system scaffold documents exact semantic-input
  and immutable-world SDK patterns, completion audits may accept repaired
  non-security failures, and Studio automation discovers the actual produced
  archive with a configurable bounded turn budget. Empty zero-renderable debug
  frames no longer satisfy its nonblank viewport gate.
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

- Physics is functional but not yet production-complete. 2D is a planar BEPU
  projection with box/circle shapes and persisted linear velocity; 3D adds
  box/sphere/capsule and static or convex mesh shapes. Persistent 3D bodies now
  retain angular velocity, orientation, and sleep state, and native BEPU
  contacts own friction/restitution response. Remaining breadth includes
  generic joints/constraints, a dedicated 2D world/material contract, authored
  angular control, collision layers/masks, exact contact manifold/impulse
  facts, deformables, and measured large-world broadphase performance.
- 3D rendering is substantial and hardware-backed: perspective/orthographic
  cameras, viewports/layers/stereo/OpenXR, primitives and authored/imported GLB
  meshes, PBR texture inputs, directional/point lighting, generic animation,
  skeletal skinning, morph targets, LOD/virtual geometry, Vulkan capture and a
  windowed player are verified. Breadth still lacks a generic spot-light
  contract and mature shadow/contact/render-feature coverage expected of a
  finished general-purpose 3D engine.
- Expand adversarial security tests around authored JSON, migration races,
  diagnostic stores, and full-trust module inputs.
- Production consumers still execute C# modules in-process until the active
  restricted-host consumer cutover is complete. The AppContainer worker and
  broker now exist with adversarial local proof, but are not yet a shipped
  product claim. Build receipts also remain unsigned; publisher signatures are
  a separate future security capability.
- Complete advanced animation breadth such as native glTF weight-channel
  animation, TANGENT/sparse/quantized morph accessors, broader complex
  transform fixtures, richer graph curves, and interruptible or hierarchical
  graph policies.
- Expand Studio asset/module workflows and run broader installed game-creation
  benchmarks beyond the fixed gauntlet. Deterministic WPF automation,
  schema-guided editing, transactional undo/redo, embedded Ollama authoring,
  real play/package/audit controls, and installed Studio-to-agent game creation
  are now present. The workbench is functional but not yet a finished
  professional editor.
- Repeat the now-passing uninterrupted empty-project task-specific benchmark
  against freshly assembled installed binaries. Repository-built Studio passed
  the complete `Echo Foundry` authoring and audit session in 49 turns. The next
  bar is distribution parity, followed by reducing the six recovered malformed
  or initially incomplete non-scope tool calls without hiding real errors.
- Fresh arbitrary-game Benchmark 16 used the restored real local Ollama
  `qwen3.5:35b` through repository-built Studio. It authored a runtime module,
  repaired it to a successful build, created semantic input and scene content,
  and reached direct runtime assertion evidence, but exhausted the 64-turn
  budget plus 12 protected repair turns after 71 tool executions. It is an
  honest failure: the final scene had zero renderables and three invalid
  `Transform3D` properties, so no package was produced. The measured loop was
  dominated by AGE rejecting the model's intuitive
  `delta.transform.position3d.x` assertion subject even though the diagnostic
  only said to supply a "transform delta." AGE now normalizes that generic
  alias (and its 2D/axis variants) to the canonical `delta.position*` subjects
  in both checkpoint preflight and runtime evaluation, and returns a copyable
  exact transition assertion. The focused agent/runtime selection passes
  52/52. Failure evidence SHA-256 is
  `F29CA3D71DFD7F6DE598FF12DA7867710BF4E340F4F82554FE153E4BC6423341`.
- Fresh arbitrary-game benchmark 2 (`Lumen Vault`) reached eight visible
  renderables and a valid active camera, and compiled an initial generic
  delta-time gameplay system, but exhausted 64 turns after destructive
  re-scaffolding replaced that working source. The final blockers were numeric
  SDK misuse, unremoved schema-invalid properties, a missing final module
  receipt, and no package. This is retained as failure evidence rather than a
  product pass; evidence SHA-256 is
  `7B5A618D19D8C7D07FFAF51183732BD19D3976CAC469072BA3D6164B89092FB3`.
- Fresh arbitrary-game benchmark 3 exposed a more serious false-positive
  boundary. Studio reported task success with clean validation, six visible
  renderables, two compiled modules, a 44,665,001-byte audited/relocated
  package, and evidence SHA-256
  `7A68FF6A88A4CF240D0DE94DCB165AD4ADA2A10F948E3B25F934979A0DD9F4A5`.
  Independent source and runtime inspection disproved the requested gameplay:
  `Player Orb` lacked the registered `VaultPlayerComponent`; seals were never
  deactivated or reset; no completion/HUD state was written; and one nested
  mutation restarted from stale `world`. The package and audit are valid but
  are not accepted as game-completion proof. The new generic runtime assertion
  path rejects this exact project with `REKALL_RUNTIME_ASSERTION_FAILED` and
  reports the missing player component and bounded actual state.
- Fresh arbitrary-game benchmark 4 proved that the executable evidence path is
  real but exposed late-test orchestration. Ollama compiled its authored
  runtime module, then delayed `rekall.runtime.inspect_scene` until turn 62 of
  64. The command correctly failed two gameplay assertions and reported zero
  projected semantic input actions, but only two turns remained and no repair
  or retest occurred. Independent inspection confirmed no
  `Rekall.InputActionMap` in the scene and two declared agent-owned state
  components missing from module registration. This is retained as failure
  evidence with SHA-256
  `D6E8C09C180DB20ED4BB63EC6F1526A7303058152F3A111C6E4766787E5ACD32`.
- Fresh arbitrary-game benchmark 5 used the real local Ollama
  `qwen3.5:35b` through the freshly assembled installed Studio, not the
  deterministic transport fixture. It compiled an agent-owned runtime module
  and captured six visible renderables, but ignored the prompt-only gameplay
  checkpoint, never called `rekall.runtime.inspect_scene`, exhausted 64 turns,
  retained eight blocking unknown collider/rigidbody component types, and
  produced no package. This is a product failure with evidence SHA-256
  `8002393ED2AD7E566D649060FB5F2594F63A45445BBB8528C37C06E7244475B5`.
  The staged 200,950,053-byte archive used for that real-model run has SHA-256
  `00DDD9AF5E6BA4AF6E2D210D1717ABF627A5237FF9A240EEC6C363D33D0F1B36`.
  Both canonical 917-test/6-Studio-test Release passes and all installed checks
  before the final deterministic Studio/Ollama transport fixture passed; that
  fixture failed before its first agent tool call and is not counted as AI or
  gameplay evidence.
- Fresh arbitrary-game benchmark 6 used the real local Ollama `qwen3.5:35b`
  through a newly assembled 1,177-file product. The checkpoint enforcement
  worked: unrelated calls and an empty-assertion inspection were blocked, a
  qualifying failed inspection unlocked the 12-turn repair reserve, and the
  model later produced a relocated audited package with ten renderables. It is
  retained as failure evidence, not a playable-game pass. Independent source
  and scene inspection found zero projected input actions because the action
  map was encoded as a JSON string, no seal state component was attached, and
  the module queried the exact name `Energy Seal` while authored entities were
  named `Energy Seal 1` through `3`; the module therefore returned before its
  gameplay logic. The model weakened a later assertion and exhausted turn 76
  after audit. Evidence SHA-256 is
  `05D7AF9CEF33B0385EB9D94BEF3CDE3A430A6EA3D10D1343E7F7F17F12AA500E`;
  the benchmark product archive SHA-256 is
  `61935BAB1D47E86B11DDEC303BCB4C1EE649C39D59BCFF2818C4BF633439CD43`.
- Fresh arbitrary-game benchmark 7 used real local Ollama `qwen3.5:35b`
  against the self-contained product built from commit `5137685`; its
  200,956,173-byte archive SHA-256 is
  `E05F83A8A6852C6BE4A0970B8BF20A0EA6C2EF903E961FBF2D9D5B9166C1E0E1`.
  It exposed an orchestration deadlock rather than a gameplay pass. The model
  compiled `GameRules` before populating the scene, so the immediate checkpoint
  correctly required attached state and a transition but incorrectly deferred
  `create_blueprint_project`, `scene.apply_blueprint`, and component discovery.
  The scene remained empty, all 64 turns were exhausted, and no package was
  produced. This is retained as failure evidence with SHA-256
  `65A805689F53C13E81DA2ED32184606B7B73F7E4E3A7B476889210E80CA260DE`.
- Fresh arbitrary-game benchmark 8 used real local Ollama `qwen3.5:35b`
  against the self-contained product built from `f57854c`; its 200,956,794-byte
  archive SHA-256 is
  `0D24AA4CDD9FF0B685B7947F983FAE236C35D22D7BDA2B387E0F6877DF77A6B9`.
  The deadlock was resolved: after compiling its runtime module first, the
  model successfully used prerequisite authoring to populate a scene with an
  active camera, input map, player state, and three seal components. It still
  failed the product gate: nine runtime inspections omitted assertions and six
  supplied insufficient coverage, it exhausted 64 turns, Studio saw only one
  renderable, and no package was produced. Evidence SHA-256 is
  `45C783F1C4355D72755068382305E06B93DB0B251EE7139EE3A6B10222E596D4`.
- Fresh arbitrary-game benchmark 9 used real local Ollama `qwen3.5:35b`
  against the self-contained product built from `ae73d20`; its 200,959,555-byte
  archive SHA-256 is
  `6660FC1370811713440E1C48D03012CE9515A6723F2385FC466FAEAF4E9FA874`.
  Structured evidence exposed a pure tool-name failure: after one correct
  engine-status call, the model emitted `rekal.*` instead of `rekall.*` for 25
  consecutive calls despite exact suggested names. The project remained empty
  and no package was produced; Studio correctly reported failure. Evidence
  SHA-256 is
  `8FB9F93685B3E4F70B5431D8F136227CB1359A4732A6E17D2A281254319E8F61`.
- Fresh arbitrary-game benchmark 10 used real local Ollama `qwen3.5:35b`
  against the 1,177-file self-contained product built from `2e083ca`; its
  200,963,166-byte archive SHA-256 is
  `C728242296448F33518AF086DEC1C97DDE0BEC36A166E874DC271FF2294A12F7`.
  The one-edit recovery removed Benchmark 9's typo loop and the model reached
  source authoring and its first module build in 15 tool attempts. Turn 16
  then failed outside AGE command execution when Ollama returned HTTP 500 for
  malformed generated function-call XML. Studio correctly failed the product
  gate, but its structured execution list was empty because the session did
  not return normally. No scene or package was produced. Evidence SHA-256 is
  `B5A8C70A041A1EDE99A325FF51AFB0DED301E0FBC6CA257D400AB5F761FB834D`.
- Fresh arbitrary-game benchmark 11 used real local Ollama `qwen3.5:35b`
  against the self-contained product built from `0e8d6d9`; its 200,964,267-byte
  archive SHA-256 is
  `81E86F266C7D888F213BB48C345CF9825F9BEA57344140CBE5F2BAC0427F0F30`.
  The provider retry path avoided Benchmark 10's interruption and Studio
  retained all 64 tool executions. The model authored a nine-entity scene,
  compiled `LumenRules.dll`, and produced a nonblank seven-renderable viewport.
  It still failed honestly: 28 tool attempts failed, no package was produced,
  and repeated runtime checkpoints omitted `componentType` while sometimes
  putting the exact `Game.Modules.LumenRules.PlayerState` type in `entityName`.
  The checkpoint gate therefore never executed the malformed proof and the
  model exhausted its turn bound. Final validation also reported invalid
  guessed built-in component names/properties introduced by late wholesale
  blueprint replacements. Evidence SHA-256 is
  `CBFA0484D85CA1447191CF0FD23DF96C6AD843DADCC80D3DBFC0B643BBF76A59`.
- Fresh arbitrary-game benchmark 12 attempted the unchanged task with
  `devstral-small-2:24b` against the 1,177-file product built from `ef6cf3d`;
  its 200,965,841-byte archive SHA-256 is
  `3A2B8744CFB95958B247EE35C10F97E48A069749DAB2A934AF4EC79B985FB40F`.
  The run stopped before its first tool because AGE sent Ollama's optional
  `think: medium` field and Devstral explicitly returned HTTP 400, "does not
  support thinking." This is retained as provider-compatibility failure
  evidence, not a comparison of game-authoring quality. Evidence SHA-256 is
  `2AAB90FF76E7B1B2C60276979958166AD2AF12F2E4310F8B411B8594454E147A`.
  Independent native-API smokes then proved Devstral selects a registered tool
  and emits the exact four-field `Game.*` component assertion plus a strict
  transform delta that Benchmark 11 repeatedly malformed.
- Fresh arbitrary-game benchmark 13 was Devstral's fair full comparison after
  the model-capability fallback shipped in product `8e57cb9`. The unchanged
  1,177-file product archive is 200,966,057 bytes with SHA-256
  `FA15362BB75D7FF972AB6DA287D85BF711ECC59B1C04A3EB7F697E286528E678`.
  Devstral stopped after six turns and two successful engine-status calls,
  authored no project content, and ended by promising to begin. Evidence
  SHA-256 is
  `FE7446128D32E048CAA254BA3C0C02E485505CF5A844277C25121DEED26524DC`.
- Fresh arbitrary-game benchmark 14 evaluated real local
  `qwen3-coder:30b` against the same installed product. It made 87 executions
  including the protected repair reserve, authored eight renderables, compiled
  two modules, fixed validation, and passed a strict runtime inspection at
  execution 75. Later scene/playable repairs invalidated that evidence and it
  exhausted the reserve before packaging: 30 calls failed and no archive was
  produced. Evidence SHA-256 is
  `50779E136539B1C32260C5A3BE970FC5FE6EB81E8EA5E8A16E51722881DA5C61`.
  A bounded continuation made the existing project worse by replacing it with
  five entities and invalid `Rekall.Collider3D` content; continuation evidence
  SHA-256 is
  `09537B03240050F762047C45776D26AD983E3268A8B520D7F9D37FED359FB872`.
- Fresh arbitrary-game benchmark 15 evaluated the same Qwen Coder weights with
  temperature 0.15, top-p 0.8, and top-k 20 from an empty project. Native API
  smokes selected the exact status tool and emitted a correctly structured
  component-existence plus strict movement assertion, but full authoring
  regressed decisively: 64 executions contained 55 failures, the final scene
  had only two renderables and 15 blocking validation issues, the module never
  produced a receipt, and the last turns repeated trust inspection instead of
  the returned build action. No runtime checkpoint or package was produced.
  Evidence SHA-256 is
  `0CD2E7AA3AB10D941004E455A69E6EEAF532E47425B5DA52417D89F73A50EE9B`.

## Recently completed

The freshly assembled `3a84dbc` product now passes the complete installed
distribution acceptance in one clean run. The first attempt isolated a bug in
the deterministic Studio transport fixture: its raw HTTP reader handled
`Content-Length` but treated a chunked JSON POST as header-only, closed the
socket while Studio was still sending, and correctly caused Studio to fail
without tools or a package. The fixture now consumes bounded chunk framing;
the exact focused check went red then passed with two tool calls, a nonblank
viewport, and a package, after which the complete installed matrix exited 0.
This fixture proves shipped Studio/Ollama protocol wiring only. It is not a
language-model or autonomous-authoring proof; Benchmark 16 still requires real
local Qwen 3.5.

The repeated-failure recovery is now present in a fresh self-contained Windows
product assembled from `3a84dbc`. Its manifest declares 1,177 payload files;
the installed directory contains 1,178 files including the manifest. The
200,967,116-byte archive SHA-256 is
`A989D5790695578B672320B4DC89347F599D02391B1AD03D25A437EC11EFEB32`.
This product is the next real-model benchmark subject; assembly alone is not a
game-creation pass.

Benchmark 15's 54-call trust-inspection loop now has a generic bounded
intervention. After three consecutive failures of the same canonical tool with
identical arguments, the language-model agent injects the failed call, exact
arguments, consecutive count, and any engine-returned `nextActions` into a
direct recovery message. It explicitly does not execute the suggested action
for the model, and a later different or successful call clears the intervention
for that turn. The regression reproduces three identical missing-receipt trust
failures and proves the next model request receives the exact
`rekall.build.modules` recovery action. The focused agent loop passes 20/20;
the complete Release engine suite passes 926/926, Studio passes 7/7, and the
warning-as-error solution build reports zero warnings and zero errors.

The replacement-model experiment is complete. Devstral was decisively worse
than Qwen Coder, while normal-temperature Qwen Coder was much closer to the
closed loop than its low-temperature profile. Neither matched the strongest
real `qwen3.5:35b` AGE evidence: Qwen 3.5 previously completed `Prism Relay`,
`Signal Garden`, and uninterrupted `Echo Foundry` task-specific sessions with
compiled gameplay, nonblank captures, packages, relocation, and audits. The
experiment therefore removed the Qwen Coder tags and selected Qwen 3.5 for
restoration. This is a measured model-quality decision, not an engine success;
the current Lumen Vault loop remains red.

Benchmark 12's model-capability mismatch now has a bounded adapter fallback.
When—and only when—Ollama returns HTTP 400 stating that the selected model does
not support thinking, the adapter removes the optional `think` field and
retries once. Other 4xx failures still surface unchanged; existing bounded
5xx/rate-limit/timeout recovery remains intact. The regression proves the
first request contains `think`, the compatible retry omits it, and the response
is retained. All four Ollama adapter tests pass. The complete Release engine
suite passes 925/925, Studio passes 7/7, and the warning-as-error solution build
reports zero warnings and zero errors. Fresh Benchmark 13 is Devstral's first
valid full comparison gate.

Benchmark 11's repeated assertion-field inversion now receives a copyable,
structured repair. Checkpoint failures expose the required four-field
`component`/`exists` shape and, when malformed arguments unambiguously contain
an ordinary entity name plus a `Game.*` type placed in `entityName`, derive a
candidate assertion with those values in their correct fields. The engine does
not execute, author, or accept that suggestion as evidence; the agent must
still run the canonical inspection and pass the strict transition assertions.
The human instruction also names `componentType` explicitly and prohibits
putting types in `entityName`. The focused regression reproduces the exact
inversion. The complete Release engine suite passes 924/924, Studio passes 7/7,
and the warning-as-error solution build reports zero warnings and zero errors.
Fresh Benchmark 12 is the next real-Ollama gate.

Benchmark 10's provider interruption now has bounded generic recovery and
durable partial evidence. The Ollama adapter retries request timeout, rate
limit, and server failures twice with cancellation-aware bounded backoff; a
persistent failure still surfaces with its exact status and response body.
Studio records every completed tool execution as progress arrives, so a later
provider or session exception cannot erase the execution ledger used to
diagnose autonomous authoring. Focused regressions cover the observed HTTP 500
recovery and a later-turn model failure. The complete Release engine suite
passes 923/923, Studio passes 7/7, and the warning-as-error solution build
reports zero warnings and zero errors. Fresh Benchmark 11 is the next real-
Ollama gate.

Benchmark 9's systematic namespace typo now has bounded deterministic recovery.
The progressive MCP executor canonicalizes only a unique registered tool name
exactly one insertion, deletion, or substitution away; ambiguous names and
names two or more edits away still fail closed. Successful results record the
attempted name, canonical name, and edit distance. The language-model agent
uses the canonical name for gameplay checkpoint, repair-reserve, completion-
audit, terminal-tool, progress, and retained-execution policy, including the
observed `rekal.runtime.inspect_scene` case. The focused MCP/agent selection
passes 10/10; the complete Release engine suite passes 922/922, Studio passes
6/6, and the warning-as-error solution build reports zero warnings and zero
errors. Fresh Benchmark 10 is the next real-Ollama gate.

Benchmark 8's repeated malformed checkpoint attempts now receive structured,
fact-specific repair evidence. Synthetic checkpoint failures report booleans
for representative inputs, an attached `Game.*` component assertion, and a
strict transition assertion, plus the exact missing list. Studio automation
now persists the bounded structured tool-execution ledger—including arguments,
success state, and result preview—rather than only human progress lines, so
future installed real-model failures are independently diagnosable. Focused
engine and Studio tests pass; the complete Release engine suite remains
920/920, Studio passes 6/6, and the warning-as-error solution build reports
zero warnings and zero errors. Fresh Benchmark 9 is the next real-Ollama gate.

Benchmark 7's deadlock is now covered by a generic checkpoint-preparation
contract. After a successful runtime build, the agent may still use bounded
tool discovery, project/scene summaries, blueprint/scene/entity/component
authoring, exact component/SDK discovery, module source inspection/repair, and
module build operations needed to construct executable evidence. Validation,
capture, package, audit, and other delivery work remain deferred until a
qualifying runtime inspection executes. Focused TDD proves prerequisite scene
authoring executes while premature packaging does not. The complete Release
engine suite passes 920/920, Studio passes 6/6, and the warning-as-error
solution build reports zero warnings and zero errors. Fresh Benchmark 8 is the
next real-Ollama gate.

Benchmark 6's false-positive assertion path is now a generic executable
coverage contract. The first gameplay checkpoint requires a non-empty input
sequence, an existence assertion for an attached agent-owned `Game.*`
component, and a strict proof of either a nonzero transform delta or changed
agent-owned component state. Existence-only checks and non-strict zero
thresholds return `REKALL_RUNTIME_CHECKPOINT_COVERAGE_REQUIRED` without
executing. Runtime inspection adds generic `delta.component.property` and
`changed.component.property` subjects over initial/final bounded state. The
embedded contract explicitly forbids weakening a failed assertion. Focused TDD
passes 6/6; the complete Release engine suite passes 919/919, Studio passes
6/6, and the warning-as-error solution build reports zero warnings and zero
errors. A fresh real-Ollama Benchmark 7 is the next gate.

The measured `Lumen Vault` failures now have generic TDD coverage. Runtime
entities expose typed number/boolean/string component readers and immutable
writers without `JsonObject`; SDK inspection returns an exact transform/state
recipe plus a scalar two-axis/double-math recipe; the runtime-system scaffold
uses typed entity/world helpers rather than rebuilding `world.Entities`; and
both runtime and playable scaffolds now fail with an executable source-edit
diagnostic instead of overwriting existing agent work. The embedded contract
also fixes scalar semantic-action and numeric-type guidance. The combined
authoring-contract selection passes 29/29. The full engine suite passes
911/911, Studio passes 6/6, and the warning-as-error solution build reports
zero warnings and zero errors. The next gate is another fresh empty-project
game, not subsystem expansion.

The benchmark-3 false positive is now converted into an executable evidence
contract. `rekall.runtime.inspect_scene` accepts representative input frames
and up to 64 generic assertions over entity existence/visibility, attached
components, component properties, final transforms, and position deltas. It
returns bounded actual values and fails with
`REKALL_RUNTIME_ASSERTION_FAILED` when authored behavior is absent. Task-
specific agent sessions that author runtime systems cannot complete without a
fresh successful assertion-bearing inspection after the latest scene/module
mutation; CLI and MCP share the same contract. Focused runtime/agent/CLI tests
pass, and the malformed benchmark package is independently rejected by the
new CLI assertion path. The full engine suite passes 915/915, Studio passes
6/6, and the warning-as-error solution build reports zero warnings and zero
errors. Benchmark 4 then remained the next installed-agent gate.

Benchmark 4's late-test failure is now converted into a generic repair-loop
contract. The embedded agent receives an immediate first runnable gameplay
checkpoint after the first successful runtime-module build, before polish,
cleanup, packaging, or capture. A failed assertion-bearing inspection injects
the bounded actual values as repair evidence and unlocks a protected 12-turn
repair/retest reserve instead of ending at the ordinary turn limit. Runtime SDK
inspection, scaffold comments, and the embedded prompt now state that input
helpers do not create bindings—agents must author `Rekall.InputActionMap`—and
that every attached/read/written agent component must be registered. A
simulated end-of-budget failure now repairs source, reruns assertions, and
completes inside the reserve. The focused contract selection passes 20/20; the
full engine suite passes 917/917, Studio passes 6/6, and the warning-as-error
solution build reports zero warnings and zero errors. Benchmark 5 against a
fresh distribution is next.

Benchmark 5 proves that prompting alone is not a sufficient gameplay-testing
contract for the current local model. The agent loop now enforces the first
executable checkpoint after a successful agent-authored runtime-module build:
unrelated validation, discovery, polish, capture, and packaging calls are not
executed and return `REKALL_RUNTIME_CHECKPOINT_REQUIRED` until the model calls
`rekall.runtime.inspect_scene` with representative input and a non-empty
assertions array. Empty-assertion inspections return
`REKALL_RUNTIME_ASSERTIONS_REQUIRED`. A failed assertion remains direct repair
evidence and activates the protected repair/retest reserve. Focused agent tests
pass 16/16; after one concurrent 250-ms isolation-test timeout, the exact test
passed alone and the complete engine suite passed serially at 918/918. Studio
passes 6/6 and the warning-as-error solution build reports zero warnings and
zero errors. A fresh real-Ollama Benchmark 6 is the next gate.

`Echo Foundry` is the first uninterrupted empty-project task-specific game
creation pass. Local Ollama `qwen3.5:35b` authored the 3D industrial arena,
semantic controls, four resonators, HUD, delta-time movement/contact/score/reset
runtime system, and separate generic playable proof adapter; repaired compiler
and visual-composition evidence; reached zero validation issues; built and
trusted both modules; packaged the Windows game; repaired an initially
one-color-dominated proof frame through ordinary scene authoring; and passed the
consolidated package audit in one 49-turn session. Studio captured a 960x540
viewport with seven renderables. The session's engine work and audit were
successful, but automation initially reported false because its evidence
collector searched only `Builds` while the agent correctly used
`Output/Packages`. Package discovery is now bounded to 1,024 project-local
directories and 256 archives, skips reparse points, and verified the unchanged
archive in a four-turn audit-only run. Package SHA-256:
`03F8BFB1E0D7CC09E9D1CD2EB11FEACB48DA1C2CA7CA8B6972D205CD502E9976`;
Studio viewport:
`A5B27F145862D62E88AF25A3665E8D7E767BF663398CE5423EF8BB4A2CB9D66A`;
package-audit frame:
`D86CE63E2BFB4780E28C8517724D9264DB13B9DB333C64D67441FDFA1421F3FB`;
collector evidence:
`A88A17CCF4D5F4F5A231DF69C35CAF0B4AC78ED20B0E4042597C7892EAED3EA8`.
Current verification remains 908/908 engine tests, advances to 6/6 Studio
tests, and the full solution builds with zero warnings and zero errors.

The task-specific `Signal Garden` checkpoint is accepted from direct repository
evidence. Starting from an empty project, local Ollama `qwen3.5:35b` authored a
3D night garden, semantic input, an agent-owned delta-time world gameplay
system, bloom activation/score/reset logic, and HUD content. Its first 64-turn
run produced the authored scene and compiling gameplay but stopped with stale
module evidence before packaging. A bounded continuation preserved that game,
added the generic playable package-proof adapter, built both modules, reached
zero validation issues, captured a 960x540 Studio viewport with 16 renderables,
packaged the player, and passed consolidated audit in 18 turns. The package
SHA-256 is
`65A2ABFD5B1B79D5CB28C9E5D8C45C83A1AB70C7CEF36D3B57E2AC18C4C0CBD0`;
the Studio viewport SHA-256 is
`C268B21F353DA442FEF869108861C6B8E007505C7D36242430540A2B5BDE0CB5`;
the package-audit frame SHA-256 is
`C0D8367533FBDA1628DEBD7A30A7759A31FBE10B2438EADFC224BA5C61F0D49F`;
and the continuation evidence SHA-256 is
`DED324F74DD1375C7CF359977B2D2A5533F9BA8E5E253FF3288DB532708FCA4D`.

Project agent sessions now supply their already-owned project root and active
scene to native tools when those scope fields are omitted, while still
rejecting explicit out-of-scope paths. Runtime scene inspection has a safe
one-frame default. The embedded contract now establishes the generic playable
module as an early deterministic package-proof adapter while keeping actual
world gameplay in the agent-authored runtime system; final builds therefore no
longer become stale from late adapter scaffolding. Current verification is
908/908 engine tests, 5/5 Studio tests, and a zero-warning full solution build.

The task-specific `Prism Relay` checkpoint is accepted from direct repository
evidence. Local Ollama `qwen3.5:35b` authored a non-gauntlet 3D game with five
visible renderables, semantic input, an agent-authored delta-time C# gameplay
module, a playable adapter, and an inspectable HUD. The final project has zero
validation issues, both module projects build, both receipts verify as
`windows-appcontainer-restricted`, and the consolidated playable-package audit
passed. Studio reports a 960x540 nonblank runtime viewport. The final
continuation completed in three turns and two tool calls after a canonical tool
search. The package SHA-256 is
`E17A9F9830276FF940CD7082F35DABBFDE8AF6D26271CE69CCACDED59B01583E`;
the Studio viewport SHA-256 is
`13CC2332D50127370DABEE528FC824AFCBC398C3E187BDEB3CBED7C5A2CAC2B0`;
the package-audit frame SHA-256 is
`06192B3845A53BE229110C60B1050BE97166F3098A2ABC660A1B0EF099F58AFB`;
and the final evidence SHA-256 is
`4427B355EA5242BECE210F0AF311145BD89649DCB58E9D8A7BBFA7363F207AF8`.

The generic authoring contract now exposes exact queryable runtime SDK method
signatures and source-topology/build rules through
`rekall.module.inspect_runtime_sdk`. Progressive discovery exposes matched
native tools directly instead of steering the model through the compatibility
gateway. The immutable module SDK gained generic `RemoveEntity`; successful
playable-package audits prime the next evidence-backed completion while any
later tool call invalidates that proof; and Studio automation can safely resume
an existing project. These behaviors are covered by the current 907/907 engine
and 5/5 Studio test suites, and the full Debug solution builds with zero
warnings and zero errors.

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

The current item remains the actual AI game-creation loop. The proven
`qwen3.5:35b` is restored locally as a 23 GB Ollama model. Run a fresh unchanged
benchmark through that real local model
and independently inspect its scene, source, input projection, and runtime
transitions. Require clean validation, informative
capture, compiled agent-authored behavior, a playable relocated package, and a
passing consolidated audit. Only after this generic loop is honestly green,
run a fresh Pong brief as the compact fully playable proof before using Galaga
as the broader multi-entity gameplay benchmark. Use concrete failures to
improve only generic authoring primitives, schemas, diagnostics, and repair
efficiency. Studio, embedded AI, MCP, CLI, and packaged players must continue
to consume the same contracts.

Fresh installed Lumen Vault benchmark 17 is retained as an honest failure.
Real local `qwen3.5:35b` used 76 bounded turns (including the protected repair
reserve), with 54 successful and 22 failed tool executions. It compiled an
agent-authored runtime system, declared semantic input, produced an eight-entity
runtime scene and a nonblank 960x540 Studio viewport with six visible
renderables, but never passed the strict movement checkpoint and therefore did
not package. The measured cause is a generic contract mismatch: runtime modules
consume semantic actions, while deterministic input frames expose only raw
device facts. The model supplied intuitive `move_horizontal` and
`move_vertical` fields; deserialization discarded them and all projected action
values stayed zero. The current plan adds bounded typed semantic-action
injection and rejects ineffective checkpoint inputs with a copyable repair
shape before rerunning the unchanged game brief.

The semantic runtime-input tranche is now implemented and installed-product
verified. Runtime input frames expose bounded typed semantic action samples;
samples override raw projection only for exact actions declared by an active
`Rekall.InputActionMap`, undeclared samples remain isolated, raw device input
continues to work, and invalid duplicates/bounds fail with structured errors.
The MCP schema, runtime command description, embedded agent contract, and
checkpoint preflight all expose the same copyable shape. Unknown flat action
fields no longer count as evidence and are rejected before tool execution.
The focused runtime/agent/MCP selection passed 27 tests. The zero-warning,
zero-error Release solution passed 971 engine and 7 Studio tests, and both
locked distribution passes repeated 971/971 and 7/7. All installed-product
acceptance checks passed against the 1,186-file Windows distribution; its
201,512,801-byte archive has SHA-256
`5884DEEE2A9010904C113FFE3CD32FA4143459D5E269D5611385D3E0944BBFF4`.

Fresh installed Lumen Vault benchmark 18 is retained as the next honest
failure. The unchanged brief and real `qwen3.5:35b` used 74 tool executions
(48 successful, 26 failed), compiled the agent-authored runtime system, and
produced a nonblank 960x540 viewport with 11 renderables. Crucially, all six
runtime checkpoints now used the exact typed `semanticActions` payload, proving
the benchmark-17 blocker is removed. The run still ended at the turn limit
without packaging because AGE accepted `Rekall.InputActionMap.Actions` as a
JSON-encoded string rather than an actual array. Runtime consequently reported
zero input actions and the model repeatedly revised otherwise executable C#.
The next generic target is fail-closed structured component-property authoring
and a direct runtime diagnostic with the exact valid action-map shape; no
Lumen-Vault-specific behavior belongs in the engine.

The structured component-authoring tranche is now implemented and
installed-product verified. Component schema and mutation guidance require
native JSON arrays/objects and give an exact semantic binding example. Runtime
emits bounded error observations for malformed action maps and injected action
names absent from active maps. Project validation blocks structured CLR array
properties stored with the wrong JSON shape, losslessly parses an encoded
array/object when possible, and supplies its ordinary
`rekall.component.set_property` repair so the bounded project repair workflow
can reach zero issues without hand-editing files. The full Release solution
passed 975 engine and 7 Studio tests; the locked distribution repeated 975/975
and 7/7 twice with zero warnings and zero errors. All installed-product checks
passed against the 1,186-file distribution. Its 201,521,262-byte archive has
SHA-256
`8540332C94F139382D5AAE0BD5BB1AD31696839E4E48E84AADD12B317B902DB8`.

Fresh installed Lumen Vault benchmark 19 is retained as the next honest
failure. The unchanged brief and real `qwen3.5:35b` used 65 tool executions
(39 successful, 26 failed), compiled an agent-authored runtime system, and
produced a nonblank 960x540 viewport with 17 renderables. Native structured
binding authoring worked: runtime exposed 13 action projections rather than
the zero actions in benchmark 18. Independent one-frame replay proved injected
`move.horizontal=1` reached all matching declarations. Movement still remained
zero because the authored module called `FindEntity(world, "OrbPlayer")`;
AGE's generically named helper only accepted an opaque entity id, returned
null for the exact unique name, and the authored system exited silently. The
next generic target is an unambiguous, observable entity query contract that
preserves id lookup while making unique-name lookup safe and agent-efficient.

The 2026-08-20 capability audit verified that built-in component schemas and
`rekall.module.search_component_schemas` expose exact 2D/3D transform, camera,
renderer, light, rigidbody, collider, world, and material contracts, while
`rekall.module.inspect_runtime_sdk` exposes immutable entity/component helpers,
semantic input, generic events/observations, camera vectors, and typed
`Raycast2D`/`Raycast3D` queries.
Runtime inspection, viewport capture diagnostics, validator repair actions,
and MCP command schemas provide executable evidence. Persistent simulation,
angular state, BEPU-native material response, and bounded physics telemetry are
now verified. Further physics breadth should be driven by the real Qwen
benchmark, with likely candidates being exact contact evidence, collision
filtering, constraints, or authored angular control rather than genre behavior.

The programmable-rendering architecture's executable-material plan is complete.
Tasks 1-6 are verified:
existing agent-visible shader authoring and assignment metadata now resolves to
reflected, content-addressed, ABI-validated shader assets, and incompatible
pairs cannot alter a scene, authored shader identity reaches each GPU draw,
and native Vulkan capture executes the selected project pipeline with measured
pixel proof. The windowed Windows player executes the same authored sources and
retains its last valid pipeline across broken live edits. Agent inspection,
dependency-inverted validation, referenced-source packaging, relocation, and
semantic package audit are verified. The declared frame/draw/material resource
ABI is now common to both GPU backends, and the retained example proves native
hardware, Windows player, package relocation, and audit. Custom post-processing, dynamic
geometry, and typed GPU resources are
separate subsequent tranches; the first post-process proof will be an
agent-authored raindrop shader rather than an engine rain feature.

AI game-creation Tasks 1-3 have a verified functional checkpoint. A new
UI-independent workbench session creates projects
and scenes through canonical commands, opens and switches scenes, executes
dynamic registered commands, appends their transactions, reloads external
agent changes, refreshes the canonical read model, preserves the last valid
model on failure, and carries explicit entity selection into the structured
inspector. Studio consumes that session and the centralized default command
registry; it can create/open, select, mutate arbitrary generic components and
JSON properties, validate, capture a 960x540 software viewport after edits,
and own a real Windows player process. Unexpected async command failures are
reported in-product instead of escaping the UI dispatcher. Transactional
undo/redo, scene switching, package/audit actions, and embedded Ollama
authoring, schema-guided property editing, deterministic WPF view-model
automation, and installed Studio-to-agent automation are verified.

The fresh installed-product checkpoint completed a zero-warning locked Release
build, two independent 894/894 Release suites, four self-contained publishes,
and distribution assembly. The shipped acceptance then passed the generic
agent-authoring gauntlet, clean package relocation/audit, nonblank capture,
Windows play and the broader production matrix. The 1,178-file distribution
archive is 200,832,939 bytes with SHA-256
`4553BF616B31461BCEF11679DA66177B46929B15829FB5F39CF00FED5FFC9D6D`.

The reusable project agent session uses the provider-neutral language-model
agent, local Ollama adapter, progressive MCP executor, and shared default
command catalog. It exposes model listing and bounded live progress, treats
project-root scope violations as failed executions even when a model claims
completion, and scopes both direct arguments and JSON-string gateway
envelopes. Studio exposes model selection, task input, Run/Cancel, and a
bounded transcript, then reloads, validates, and recaptures authored state.
The first real model run exposed inefficient malformed blueprint and camera
composition attempts; schema descriptions now explicitly separate camera and
light configuration from Transform3D pose. The repaired proof is clean and
visually informative, but it is a scene-authoring proof rather than the final
playable/package installed-game acceptance.

The first complete-game attempt exposed a separate completion-control defect:
after a passing gauntlet the model performed an unnecessary second audit and
continued editing until its turn limit. The provider-neutral agent request now
supports explicitly configured terminal-success tools, including gateway-
wrapped targets. The Studio project session configures only the generic
agent-authoring gauntlet as terminal; ordinary tools still require the normal
completion audit. A fresh real rerun stopped immediately after the passing
gauntlet and returned success.

The restricted module-host tranche is paused at commit `4e43119`, a stable
native containment and typed-broker checkpoint. Project-write denial and
64-KiB stderr-drain proof are also locally verified and will be preserved
before the authoring tranche begins. Memory-limit classification, ten-pass
timing, installed hostile fixtures, production consumer cutover, and shipped
worker packaging remain explicitly unfinished and must be resumed after the
game-creation loop is usable.

The next audit-driven tranche is a restricted host for agent-authored C#
modules. The selected Windows-first architecture keeps the existing generic C#
SDK, verified receipt admission, and runtime priority semantics, but moves all
project-assembly execution and reflection into a no-network AppContainer worker
with kill-on-close, one-process and memory job limits, bounded framed IPC,
timeouts, and no silent in-process fallback. The reviewed design is
`docs/superpowers/specs/2026-08-20-restricted-module-host-design.md`, with the
TDD sequence in
`docs/superpowers/plans/2026-08-20-restricted-module-host.md`.

Restricted module host Task 1 is verified in 42 focused module/build/CLI tests
after the complete 855/855 Debug suite. New schema-2 module receipts require the
`windows-appcontainer-restricted` execution posture; legacy, empty, and unknown
postures fail with `REKALL_MODULE_RECEIPT_HOST_POSTURE_MISMATCH` plus an
executable rebuild action. The generic protocol layer now supplies typed
initialize/runtime/playable contracts and versioned little-endian JSON frames
with exact 64 MiB message and depth-128 bounds, strict monotonic sequences,
duplicate-field rejection, cancellation preservation, stable coded failures,
and adversarial coverage for malformed, truncated, oversized, invalid UTF-8,
unknown-version/operation, inconsistent response, and typed-payload cases. The
next witnessed-red slice is the deterministic worker server; no production
consumer has been cut over yet.

Restricted module host Task 2 is verified by 23 focused protocol/worker tests,
a zero-warning Debug solution build, and the complete 866/866 Debug suite. The
new `Rekall.Age.ModuleHost.exe` runs a persistent single-request session over
protocol-only standard output, independently rechecks its confined load plan
and every artifact hash, discovers ordered system IDs/priorities and component
schemas, retains playable state, and executes typed runtime/playable calls with
source-generated JSON metadata. A real child-process test completed finite
initialize/shutdown framing with clean stderr. Adversarial proof covers calls
before initialization, duplicate initialization, unknown systems, traversal,
post-plan DLL mutation, sequence violations, a 5,000-character module throw,
and non-JSON `NaN` render output; failures are bounded, coded, stack-free, and
terminate the session. This is still an ordinary diagnostic worker until Task
3 adds immutable staging, AppContainer launch, explicit handle inheritance,
job limits, timeouts, and broker lifecycle ownership.

Restricted module host Task 3 staging is verified in an 18/18 combined
worker/staging selection. The broker now admits a product/protocol-matched host
manifest, copies only manifest-verified worker files and receipt-verified
module artifacts into a unique session tree, rechecks source and destination
size/SHA-256 around every copy, writes the confined load plan, marks all staged
files read-only, and removes the exact session tree after success or failure.
Tests prove source, project files, PDBs, build receipts, and unmanifested host
files do not cross the boundary; altered host/project artifacts leave no
session tree. Windows alias forms including alternate data streams, device
names, duplicate separators, and trailing-dot paths are rejected before copy.
The next active slice is AppContainer SID/ACL creation and job-bounded native
process launch; staging alone is not treated as sandbox activation.

Restricted module host Task 3 now has a verified native-containment checkpoint.
The launcher creates or derives the stable no-capability AppContainer profile,
grants read/execute only to the immutable staged tree, inherits exactly three
protocol pipe handles, starts suspended, assigns a kill-on-close job before
resume, limits the job to one process and 512 MiB, and supplies a deliberately
  scrubbed, alphabetically sorted Unicode environment instead of inheriting
  broker secrets. A 37/37 module-host selection and zero-warning Debug solution
  build pass; six of those tests are native Windows integration cases. They prove typed broker
initialize/playable calls, a 250 ms fail-closed hang deadline, exact abrupt
crash reporting, absent injected environment secrets, and denial of an
unstaged sentinel read, child-process creation, and loopback networking. The
broker owns staging/profile/process disposal and distinguishes crash from
timeout without exposing module diagnostics. Remaining Task 3 work is the
project-write, memory-limit, excessive-stderr, repeated-timing, orphan-process,
and orphan-staging matrix plus final stable-code consolidation; no runtime,
playable, schema, Studio, CLI, or MCP consumer has been cut over yet.

The completed persisted-document recovery tranche began because atomic
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
search. Those portable contracts are now included in the installed product
gate recorded in the next milestone.

Persisted document recovery Task 4 passed the complete product gate. The fresh
locked Release build completed in 8.78 seconds with zero warnings and zero
errors; two independent Release passes completed 840/840 in 1m26s and 1m24s.
The shipped CLI authored a scene, retained a prior version, then had its live
scene deliberately malformed. An ordinary scene-summary command failed with
exact code `REKALL_DOCUMENT_JSON_MALFORMED`; recovery inspection reported a
valid previous version and exact damaged revision; a stale restore failed with
`REKALL_DOCUMENT_REVISION_CONFLICT` without changing damage; and the exact
restore quarantined one byte-identical damaged file, passed ordinary validation
with zero issues, and accepted a normal post-restore entity mutation. It left
zero temp/lock controls. The unchanged installed product matrix passed. Atomic
JSON acceptance parsed 5,767 snapshots with two bounded transient opens, zero
malformed snapshots, and zero temp files. Soak completed 600 frames and exactly
10 seconds at 4,320.2 FPS with 713,600 retained bytes and all nine checks. The
1,149-payload-file archive is 195,355,222 bytes with SHA-256
`8837F18945FDCEB4622DE5072D4A5FE0C518B2AE61B7F8A29E3E8527DFDD64CE`.
One-version rollback, not autosave/history/merge or external backup, remains the
explicit supported boundary.

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

Run clean installed benchmark 46 with the unchanged task and real Ollama. Use
its next measured blocker to choose the next generic contract repair or, after
successful task-specific runtime/package/audit evidence, advance to the queued
visual-effects acceptance class. Do not return to broad subsystem or CI
expansion until an arbitrary described game completes the full executable loop.

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
- `eng/accept-installed-document-recovery.ps1`
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
