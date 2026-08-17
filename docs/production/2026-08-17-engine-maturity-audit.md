# Rekall AGE Production Maturity Audit

**Audit date:** 2026-08-17  
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
- 505 automated tests across authoring, runtime, rendering, packaging, MCP, and workflows
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

## Capability matrix

| Area | Current maturity | Evidence | Production gap |
| --- | --- | --- | --- |
| Project/world authoring | Implemented | deterministic project/scene stores; entity/component commands; blueprints; transactions | schema migration, corruption recovery, autosave, and large-project stress proof |
| Portable C# modules | Proven | installed SDK scaffolds and builds without `src/`; isolated intermediates; runtime loading | explicit trust/sandbox policy, dependency policy, compatibility fixtures, module reload |
| MCP command surface | Implemented | JSON-RPC tools with generated JSON schemas, structured content, priorities, transactions | richer field descriptions/constraints, pagination/filtering, capability benchmark, protocol conformance suite |
| Agent context | Partial | compact project/scene summaries, diagnostics, next actions | indexed queries for large projects, token budgets, change-focused context, benchmarked efficiency |
| Desktop runtime | Implemented | fixed-step runtime, generic events/input, module systems, windowed player | save/load state, crash recovery, lifecycle soak tests, frame pacing/telemetry |
| Input and events | Implemented | semantic action maps, pointer, timer, collision, trigger, XR pose, custom module events | rebinding persistence, device hot-plug, gamepad breadth, accessibility proof |
| 3D physics | Implemented | BEPU simulation, bodies, shapes, materials, contacts, ray facts | character-independent query/controller primitives, joints/constraints authoring, stress/performance budgets |
| 2D physics | Partial | component projection and generic interaction contracts | a clearly supported operational 2D simulation path and acceptance game |
| Vulkan rendering | Implemented | native Vulkan, materials, shaders, GLB, textures, lighting, captures, visibility, budgets | broader device matrix, GPU CI/smoke lab, resize/device-loss recovery, renderer decomposition |
| Software rendering | Implemented | deterministic viewport and proof captures | fidelity limits must remain explicit; not a shipping renderer |
| OpenXR | Partial/experimental | real runtime probing, swapchains, stereo planning, windowed headset submission | repeatable headset acceptance, performance, controller breadth, lifecycle hardening |
| Audio | Projection | listener/emitter contracts and WAV MIME recognition | no decoder, mixer, buses, playback device, spatialization, streaming, or runtime audio system |
| Animation | Partial | transform animation plus animation-player projections | no general clip evaluator, sprite animation execution, skeletal skinning, blending, state graphs, import pipeline |
| Runtime UI | Projection | canvas/element contracts, pointer facts, UI-layer placeholder renderable | no layout, typography, element rendering, focus/navigation, binding, or button behavior system |
| Assets | Partial | images, DDS/KTX2, GLB metadata/meshes, WAV recognition, reports, Tripo bridge | audio cooking, animation import, dependency graph, reimport/watch pipeline, deterministic cache cleanup |
| Multiplayer | Partial/experimental | authoritative session, ownership, snapshots/deltas, named-pipe and WebSocket transport | authentication, encryption policy, internet deployment, discovery/matchmaking, load/adversarial tests |
| Live editing | Partial | scene/assets/blueprint/diff local IPC operations | module hot reload, conflict/revision UX, reconnect/recovery, Studio integration |
| Playable packaging | Partial | installed players, verify/package/run/capture/audit workflows | manifest leaks absolute build paths, project-local SDK/cache can ship, no per-file game-package integrity manifest, relocation is not a required gate |
| Engine distribution | Proven | locked restore, two suites, self-contained applications, hashes, clean installed gauntlet | binary signing, installer/updater, release provenance/SBOM, clean-machine VM matrix |
| Studio | Facade | real read models and a WPF shell | controls are unwired, viewport is text, no interactive open/edit/play workflow |
| Security | Partial | no currently known vulnerable NuGet dependency; distribution forbidden-file checks | arbitrary-module trust boundary, fuzzing, path/archive hardening, secret scanning, signed releases, threat model |
| Test platform | Implemented | 505 green tests and installed acceptance | deprecated xUnit v2 package, limited GPU/audio/headset automation, soak/fuzz/performance regression suites |
| LLM providers/Ollama | Missing | no provider integration or configured model | provider-neutral contract, Ollama adapter, model discovery/health, opt-in credentials, engine-specific evals |

## Material findings

### 1. The product can now ship an engine, but not yet a production game package

The Windows engine archive is reproducible, self-contained, hashed, and proven
outside the repository. Playable game packages are weaker: their manifest stores
absolute build-machine paths, package copying can include `.rekall` SDK files,
and relocation is handled indirectly by filename rebasing rather than expressed
as a clean relative contract. This is the first P0 because every game produced
by every other subsystem depends on a trustworthy deliverable.

### 2. Three expected engine pillars are substantially incomplete

Audio is a no-op runtime system. General UI is a no-op runtime system with a
placeholder layer renderable. General animation has inspectable projections but
only transform animation executes. These are core engine deficiencies and must
be implemented before a “fully capable game engine” claim is credible.

### 3. The agent-native architecture is real, but not yet measured

MCP is not a fake wrapper: it exposes real command execution, generated input
schemas, transactions, structured results, and the same workflows used by CLI.
The portable module SDK is also real. What is missing is a repeatable agent
effectiveness benchmark measuring discovery, tool-call count, tokens/context,
repair loops, time, and playable outcome across representative game tasks.

### 4. Studio must be described honestly

Studio is a read-only diagnostic shell. Eight visible toolbar buttons have no
commands or handlers; the viewport is text; hierarchy and inspector are string
lists. The underlying models are reusable, but Studio must remain experimental
until it can complete a real authoring loop.

### 5. Production quality needs non-functional gates

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
