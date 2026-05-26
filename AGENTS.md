# Rekall AGE Agent Guidance

Rekall AGE means Agentic Game Engine.

The engine must be designed for AI agents and users to author arbitrary games, not for the engine core to privilege one genre, controller pattern, or gameplay loop. Core systems should expose generic, inspectable, composable contracts that agents can use to build game-specific behavior.

Permanent architectural rule:

- Prefer generic authoring primitives over genre-specific built-ins.
- Rekall AGE must never allow agents to ask the engine to author content for them. The engine should enable agents to author games themselves through inspectable primitives, diagnostics, templates, SDK helpers, and generic runtime contracts.
- Put game behavior in agent-authored modules, templates, or examples unless it is truly engine-general.
- Engine input should provide capture, normalization, semantic action projection, inspection, and SDK helpers. It should not hard-code first-person, top-down, shooter, platformer, or other genre behavior as the default path.
- Engine events should expose generic lifecycle and interaction facts, such as `entity.begin`, `entity.tick`, `timer.elapsed`, `pointer.enter`, `pointer.leave`, `pointer.hit`, `pointer.down`, `pointer.up`, `pointer.click`, `collision.begin`, `collision.stay`, `collision.end`, `trigger.enter`, `trigger.stay`, and `trigger.exit`. They should not execute genre-specific behavior; AI-authored modules should consume event facts and decide what happens.
- AI-authored modules should be able to emit their own custom event facts through SDK helpers such as `EmitEvent` and `EmitBoundEvents`; do not require every useful event type to become an engine built-in.
- AI-authored modules should use generic entity query helpers such as `FindEntity`, `EntitiesNamed`, `EntitiesWithTag`, `EntitiesWithComponent`, and `EntitiesWithTagAndComponent` instead of hard-coding genre-specific entity classes or scene scans.
- AI-authored modules should use generic mutation helpers such as `ReplaceEntity`, `UpdateEntity`, `UpdateEntitiesWithTag`, `UpdateEntitiesWithComponent`, `UpdateEntitiesWithTagAndComponent`, `WithTag`, `WithoutTag`, and `WithVisible` instead of manually rebuilding `world.Entities` when ordinary entity updates are enough.
- AI-authored modules should emit structured runtime observations through SDK helpers such as `EmitObservation` and `EmitSceneObservation` when authored content is missing, inconsistent, or worth surfacing to agents; prefer inspectable diagnostics over silent failure.
- AI-authored realtime gameplay should use the engine-provided time step, such as playable `input.DeltaSeconds` or runtime `context.DeltaTime`, for motion, timers, animation, cooldowns, and simulation. Do not tie gameplay speed to render frame count.
- AI-authored runtime camera logic should use generic camera/vector SDK helpers such as `Forward3D`, `Right3D`, `Up3D`, and `Offset3D` instead of guessing Euler signs or rebuilding camera basis math by hand.
- AI-authored controls should expose semantic action maps through generic components such as `Rekall.InputActionMap`; modules should consume SDK helpers such as `InputActionValue`, `IsInputActionDown`, and `WasInputActionPressed` instead of guessing raw key meanings such as `A`/`D` or hard-coding keyboard folklore.
- AI-authored multiplayer logic should use generic multiplayer SDK helpers such as `NetworkSessions`, `PrimaryNetworkSession`, `NetworkEntities`, `NetworkEntityForEntity`, `NetworkEntityByNetworkId`, `NetworkEntitiesOwnedBy`, `RuntimeEntitiesOwnedBy`, `ReplicatedRuntimeEntities`, `IsNetworkOwner`, and `IsReplicated`; do not hard-code networking behavior around one genre or controller model.
- Authoritative multiplayer snapshots should preserve generic replication metadata such as authority, replicate flags, prediction mode, and priority so clients and agents can interpret state without genre-specific assumptions.
- Client-side multiplayer code should use generic snapshot utilities such as `RekallAgeMultiplayerSnapshotInterpolator`, `RekallAgeMultiplayerClientReconciler`, `RekallAgeMultiplayerSnapshotApplier`, and `RekallAgeMultiplayerSnapshotDeltaBuilder` before inventing game-specific replication loops.
- Running authoritative multiplayer sessions should expose generic snapshot and delta operations, including `rekall.multiplayer.snapshot` and `rekall.multiplayer.delta`, before adding transport-specific streaming behavior.
- When proving a user-facing playable game path, prefer the closed-loop `rekall.workflow.agent_authoring_gauntlet` / `game gauntlet` workflow before adding narrower bespoke checks; it should create, verify, package, audit, capture a proof frame, and return next actions through generic commands.
- When agent-authored visual output is technically valid but weak, improve generic viewport/camera/layout diagnostics first. Do not add built-in "make a showcase" or game-specific composition workflows when agents can revise ordinary scene blueprints, transforms, cameras, and renderables from better inspection facts.
- When a user-facing example fails, fix the generic engine contract first, then update the example as a consumer of that contract.
- Before adding a new built-in runtime behavior, ask whether an AI agent could author it cleanly from existing primitives. If yes, improve the primitives instead.
