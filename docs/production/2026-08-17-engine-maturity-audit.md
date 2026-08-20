# Rekall AGE Production Maturity Audit

**Audit date:** 2026-08-20

**Product:** Rekall AGE `0.1.0-preview.1`
**North star:** [Rekall AGE Product Vision](../PRODUCT-VISION.md)

## Method

This audit distinguishes implemented behavior from projections, diagnostics,
scaffolding, and product claims. A subsystem is not production-ready merely
because it has component schemas, read models, or tests. Production-ready means
an agent can discover it, author it through MCP or the portable C# SDK, run it in
the installed engine, observe failures, and prove the shipped game works after
relocation.

Evidence reviewed:

- 24 engine and test projects, approximately 60,000 C# lines
- 897 automated tests across authoring, Studio, runtime, rendering, packaging, MCP, and workflows
- the installed `win-x64` distribution and generic authoring gauntlet
- runtime execution-system registration and subsystem projections
- MCP schemas, transaction behavior, and agent context summaries
- Studio XAML, view models, and command wiring
- NuGet vulnerability and deprecation reports

Maturity labels:

- **Proven:** implemented and exercised from an installed distribution
- **Implemented:** real behavior with meaningful automated coverage
- **Partial:** useful behavior exists, but essential production capability is absent
- **Projection:** inspectable data contract exists without operational behavior
- **Facade:** UI or API surface suggests behavior that is not wired
- **Missing:** no credible implementation exists

## Latest installed evidence

The 2026-08-20 AI game-creation checkpoint completed a zero-warning locked
Release build and two independent passes of 894 engine plus 3 Studio tests,
published the CLI,
Studio, headless player, and Windows player as self-contained applications, and
passed the installed distribution acceptance. The shipped engine completed the
generic authoring gauntlet, rendered a nonblank proof, launched play, packaged,
relocated, audited, and reran the game. The assembled directory contains 1,178
files; its 200,855,482-byte archive has SHA-256
`EBC4864EB3D84355BA9560FA18314005FE743B290189CC57B1D6A960DDAF6F81`.
The same installed run passed restricted module-trust/tamper, negative archive,
runtime UI/audio, animation, compatibility, atomic persistence, optimistic
revision, and damaged-document recovery checks.
The installed Studio separately starts from no project, calls an
Ollama-compatible model through its production adapter, executes the generic
gauntlet through progressive MCP, and returns nonblank viewport and packaged
game evidence. Real local `qwen3.5:35b` completed the same Studio entry point in
four turns/four tools with zero validation issues.

## Capability matrix

| Area | Current maturity | Evidence | Production gap |
| --- | --- | --- | --- |
| Project/world authoring | Implemented | deterministic project/scene stores; entity/component commands; blueprints; transactions; bounded immutable reads, durable atomic publication, exact optimistic revisions, stale-writer rejection, one-version explicit corruption recovery with bounded quarantine, and serialized audit append with installed proof | automatic content merge/collaboration UX, autosave/history, external backup, and large-project stress proof |
| Portable C# modules | Proven | installed SDK scaffolds and builds without `src/`; isolated intermediates; AppContainer-restricted execution posture; runtime loading | dependency policy, compatibility fixtures, module reload breadth, and signed receipts |
| MCP command surface | Implemented | JSON-RPC tools with generated JSON schemas, structured content, priorities, transactions, and generic project/scene recovery inspection and explicit restore | richer field descriptions/constraints, pagination/filtering, capability benchmark, protocol conformance suite |
| Agent context | Implemented | compact schema contracts, type-directed model-argument normalization, bounded failure previews and persistent tool ledger, source and installed Ollama authoring benchmarks | indexed queries for very large projects, more benchmark tasks, lower redundant-call rate |
| Desktop runtime | Implemented | fixed-step runtime, generic events/input, module systems, windowed player | save/load state, crash recovery, lifecycle soak tests, frame pacing/telemetry |
| Input and events | Implemented | semantic action maps, pointer, timer, collision, trigger, XR pose, custom module events | rebinding persistence, device hot-plug, gamepad breadth, accessibility proof |
| 3D physics | Implemented | BEPU simulation, bodies, shapes, materials, contacts, ray facts | character-independent query/controller primitives, joints/constraints authoring, stress/performance budgets |
| 2D physics | Partial | component projection and generic interaction contracts | a clearly supported operational 2D simulation path and acceptance game |
| Vulkan rendering | Implemented | native Vulkan, materials, shaders, GLB, textures, lighting, captures, visibility, budgets | broader device matrix, GPU CI/smoke lab, resize/device-loss recovery, renderer decomposition |
| Software rendering | Implemented | deterministic viewport and proof captures | fidelity limits must remain explicit; not a shipping renderer |
| OpenXR | Partial/experimental | real runtime probing, swapchains, stereo planning, windowed headset submission | repeatable headset acceptance, performance, controller breadth, lifecycle hardening |
| Audio | Proven | validated PCM WAV decoding, deterministic voices/mix frames, buses, gain/pitch/looping, spatial attenuation/pan, SDL Windows device queue, relocated package and installed-player proof | streaming/compressed codecs, device hot-plug/recovery, effects/DSP, broader hardware matrix |
| Animation | Implemented | versioned inline/catalog clips, scalar/vector/color/string/sprite-frame/morph-weight tracks, bounded Hermite interpolation, glTF CUBICSPLINE import/execution, loop/clamp/ping-pong, bounded weighted layers and deterministic cross-fades, bounded parameter-driven state graphs with deterministic resume and installed distinct-frame proof, per-layer/state/morph inspection, bounded GLB skin/hierarchy/channel and POSITION/NORMAL morph import, deterministic skeletal joint-pose sampling, CPU morph deformation before vertex/normal skinning, generic final-mesh inspection, and installed hardware Vulkan captures with exact moved bounds and distinct frame hashes; bounded asset/track/key/marker work with structured diagnostics, deterministic malformed corpus, 7,200-frame resume proof, events, generic property mutation | native glTF weight animation, TANGENT/sparse/quantized morph accessors, incompatible compound morph layouts, broader complex transform fixtures, richer transition curves and interruptible/hierarchical graph policies |
| Runtime UI | Proven | canvases, anchors, deterministic container stacking/padding/gap/alignment/clipping, panels/labels/images/buttons, semantic focus/navigation, pointer interaction facts, pixel-level software proof, Vulkan overlays, and installed-distribution visual capture | accessibility semantics, richer text shaping, responsive-layout breadth, broader hardware visual matrix |
| Assets | Partial | images, DDS/KTX2, bounded GLB metadata/meshes/skins/animation channels/POSITION-NORMAL morph targets, WAV recognition, reports, Tripo bridge | audio cooking, native glTF weight-channel and broader animation dependency import, dependency graph, reimport/watch pipeline, deterministic cache cleanup |
| Multiplayer | Partial/experimental | authoritative session, ownership, snapshots/deltas, named-pipe and WebSocket transport | authentication, encryption policy, internet deployment, discovery/matchmaking, load/adversarial tests |
| Live editing | Partial | scene/assets/blueprint/diff local IPC operations | module hot reload, conflict/revision UX, reconnect/recovery, Studio integration |
| Playable packaging | Proven | relative hashed manifest, minimal payload, forbidden-file checks, archive safety, relocation run/audit/capture, packaged runtime UI/animation/audio state | signing, delta patching/updater integration, broader clean-machine matrix |
| Engine distribution | Proven | locked restore, two suites, self-contained applications, hashes, clean installed gauntlet | binary signing, installer/updater, release provenance/SBOM, clean-machine VM matrix |
| Studio | Implemented | command-backed project create/open and scene switching; generic schema-guided hierarchy/inspector mutation; transactional undo/redo; engine viewport; real player ownership; package/audit actions; embedded project-scoped Ollama authoring; deterministic Windows view-model automation; installed headless Studio-to-agent game-creation proof | asset/module workflow depth, interactive UI automation, and broader installed game-description benchmarks |
| Security | Partial | no currently known vulnerable NuGet dependency; AppContainer-restricted module execution posture; distribution forbidden-file checks; bounded metadata-first ZIP preflight and transactional exact-length extraction | fuzzing breadth, secret scanning, signed releases/packages and receipts, threat model |
| Test platform | Implemented | 897 green tests across engine and Windows Studio projects; latest canonical two-pass Release acceptance retains separate TRX evidence and covers Studio agent creation, Vulkan, relocation, SDL audio, runtime UI visual proof, animation limits, state graphs, cubic and morph sampling, exact final-mesh bounds, malformed corpus, long-run determinism, desktop recovery, persisted-document corruption recovery, compatibility, adversarial ZIP preflight, concurrent persisted-JSON readers, and stale-writer recovery | deprecated xUnit v2 package, broader GPU/headset automation, soak/fuzz/performance regression suites |
| LLM providers/Ollama | Implemented | provider-neutral contracts, native Ollama chat/tools/model discovery, bounded project-scoped loop, `qwen3.5:35b` source benchmarks, deterministic installed Studio adapter proof, and compound-workflow termination | additional models/providers, broader installed game-description benchmarks, lower token/correction cost, quality/cost routing policy |

## Material findings

### 1. Engine and game-package relocation are now proven

The Windows engine archive and playable game package are self-contained, hashed,
minimal, and exercised after relocation. Package inspection rejects undeclared,
tampered, ambiguous, oversized, linked, or colliding archive content before
deserialization or execution. Extraction is exact-length and transactional.
The precise boundary is documented in
`docs/production/package-trust-and-archive-security.md`. Signing, provenance,
and a clean-machine OS/GPU matrix remain release-engineering gaps.

### 2. Core audio, UI, and animation now execute; advanced breadth remains

Audio now decodes, mixes, spatializes, relocates, and reaches the installed
Windows player's SDL queue. General UI renders in software and Vulkan/windowed
paths. Versioned animation clips mutate generic component properties and expose
post-simulation state. Weighted cross-fades and skeletal GLB execution reach
the installed Vulkan renderer with visibly distinct captured frames. Bounded
parameter-driven state graphs also have shipped-binary inspection and
distinct-frame proof. Authored and glTF cubic Hermite curves share bounded
duration-scaled semantics with installed nonlinear proof. Generic authored
morph weights now drive bounded glTF POSITION/NORMAL targets before skinning,
with exact final-mesh inspection and installed hardware-Vulkan proof. Richer
text/accessibility, native glTF weight channels, additional morph accessor
forms, richer graph policies, and compressed/streaming audio remain material
gaps.

### 3. The agent-native architecture is measured but not yet broad enough

MCP exposes real commands, generated schemas, transactions, and the same
workflows used by CLI. A local `qwen3.5:35b` authored and verified UI plus
animation through engine tools within 15 bounded turns. It also completed an
expanded installed-distribution UI, imported-audio, and animation task in 23
turns. Independent installed-CLI checks confirmed runtime state and capture
evidence. The benchmark must still expand to representative 2D, 3D, physics,
packaging, and repair tasks while reducing its 311,000-prompt-token correction
cost and improving generic visual-composition feedback.

### 4. Studio must be described honestly

Studio is a read-only diagnostic shell. Eight visible toolbar buttons have no
commands or handlers; the viewport is text; hierarchy and inspector are string
lists. The underlying models are reusable, but Studio must remain experimental
until it can complete a real authoring loop.

### 5. Persisted corruption recovery is explicit and agent-operable

Conditional manifest and scene mutation retains exactly one prior validated
version. Agents can inspect primary and previous schema/shape/revision facts
through the same direct, CLI, and MCP command contracts, then explicitly
restore only at the inspected current revision. A restore atomically publishes
the retained bytes, quarantines the displaced bytes under a bounded policy,
and returns ordinary validation as its next action; normal reads never silently
roll back. Installed proof covers malformed failure, stale restore rejection,
exact recovery, validation, and post-restore mutation. This is not autosave,
arbitrary history, merge, or external backup.

### 6. Production quality needs non-functional gates

The codebase has strong unit/contract coverage, especially rendering and runtime,
but no production claim should omit device loss, soak, performance regression,
malformed-input fuzzing, clean-machine install, package relocation, and signed
artifact provenance.

## Priority model

Each tranche is ordered by four factors: dependency breadth, player/user impact,
agent effectiveness, and production failure risk. Conversational recency does
not change this order without new evidence.

### P0 — deliverable integrity and truthful contracts

1. Make game packages relative, minimal, hashed, relocatable, and verified after extraction.
2. Replace broad product claims with a full machine-readable capability matrix including partial/unavailable states and evidence commands.
3. Add package/path/archive adversarial tests and an explicit authored-module trust policy.

### P0 — complete-game runtime pillars

4. Implement generic runtime audio: WAV decoding/cooking, voices, buses, spatial parameters, deterministic headless diagnostics, Windows playback, and agent contracts.
5. Implement generic runtime UI: layout, visual/text primitives, focus/navigation, pointer/action events, bindings, rendering, and capture proof.
6. Implement generic animation: clip data, deterministic sampling, sprite tracks, transform/property tracks, skeletal import/skinning, blending, and runtime observations.

### P0 — agent efficacy

7. Build an installed-engine agent benchmark suite spanning 2D, 3D, UI, audio, physics, packaging, and repair tasks.
8. Improve MCP schemas, filtered discovery, context projections, and diagnostics from measured failure data.
9. Add a provider-neutral LLM contract and Ollama adapter; evaluate installed local models before recommending a default.

### P1 — professional workflows

10. Turn Studio into a real command-backed editor in dependency order: project open/create, selection, editing, undo/redo, validation, viewport, play, capture, assets, modules, and diagnostics.
11. Harden asset dependency/reimport/cooking workflows and live module iteration.
12. Add profiling, performance budgets, crash reports, recovery, device-loss tests, and long-running soak gates.

### P1 — release operations

13. Add SBOM/provenance, signing hooks, installer/update design, clean Windows VM acceptance, and release retention policy.
14. Migrate the deprecated xUnit v2 package and add fuzz, GPU, audio, multiplayer, and headset test lanes.

### P2 — experimental promotion

15. Promote OpenXR, multiplayer, virtual geometry, and external asset providers only when subsystem-specific acceptance matrices pass.

## Definition of production quality

Rekall AGE reaches production quality only when:

- a clean supported machine can install the engine and verify its provenance;
- an agent can discover and author every supported subsystem through MCP and the portable SDK;
- shipped games relocate and run without source, SDK caches, or build-machine paths;
- core 2D/3D rendering, physics, audio, animation, UI, input, assets, and packaging have substantial playable proofs;
- Studio completes the same authoring loop without hidden state;
- malformed content fails safely with structured diagnostics;
- performance, soak, security, and recovery gates are enforced;
- supported versus experimental claims are generated from verified evidence rather than aspiration.
