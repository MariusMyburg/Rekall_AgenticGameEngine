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
- VR play mode must still create and keep a desktop player window. Keyboard and mouse input come from that window and must be bridged into the same generic runtime input stream as OpenXR poses/actions; do not treat a headless OpenXR submitter as a playable VR session.
- AI-authored multiplayer logic should use generic multiplayer SDK helpers such as `NetworkSessions`, `PrimaryNetworkSession`, `NetworkEntities`, `NetworkEntityForEntity`, `NetworkEntityByNetworkId`, `NetworkEntitiesOwnedBy`, `RuntimeEntitiesOwnedBy`, `ReplicatedRuntimeEntities`, `IsNetworkOwner`, and `IsReplicated`; do not hard-code networking behavior around one genre or controller model.
- Authoritative multiplayer snapshots should preserve generic replication metadata such as authority, replicate flags, prediction mode, and priority so clients and agents can interpret state without genre-specific assumptions.
- Client-side multiplayer code should use generic snapshot utilities such as `RekallAgeMultiplayerSnapshotInterpolator`, `RekallAgeMultiplayerClientReconciler`, `RekallAgeMultiplayerSnapshotApplier`, and `RekallAgeMultiplayerSnapshotDeltaBuilder` before inventing game-specific replication loops.
- Running authoritative multiplayer sessions should expose generic snapshot and delta operations, including `rekall.multiplayer.snapshot` and `rekall.multiplayer.delta`, before adding transport-specific streaming behavior.
- When proving a user-facing playable game path, prefer the closed-loop `rekall.workflow.agent_authoring_gauntlet` / `game gauntlet` workflow before adding narrower bespoke checks; it should create, verify, package, audit, capture a proof frame, and return next actions through generic commands.
- When agent-authored visual output is technically valid but weak, improve generic viewport/camera/layout diagnostics first. Do not add built-in "make a showcase" or game-specific composition workflows when agents can revise ordinary scene blueprints, transforms, cameras, and renderables from better inspection facts.
- When a user-facing example fails, fix the generic engine contract first, then update the example as a consumer of that contract.
- Before adding a new built-in runtime behavior, ask whether an AI agent could author it cleanly from existing primitives. If yes, improve the primitives instead.

OpenXR operational note from the local FPS test:

- On this machine, the active OpenXR runtime is registered, but `openxr_loader.dll` may not be on the default DLL search path for Codex-launched PowerShell sessions. Prepend the SteamVR loader directory before running OpenXR commands:
  `$env:PATH = 'C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64;' + $env:PATH`
- Verify headset readiness before launching content:
  `dotnet run --project src\Rekall.Age.Cli -- render openxr bootstrap-session`
  A good run reports `Headset session ready: True`, `HMD system available: True`, `XR_KHR_vulkan_enable2: True`, and two primary-stereo views.
- Playable VR should be launched through the windowed player, for example:
  `Rekall.Age.Player.Windows.exe <projectRoot> <sceneName> --graphics --backend vulkan --vr`
  This must show the normal desktop player window, capture SDL keyboard/mouse input there, and drive headset submission from that same windowed play session.
- Windowed playable VR uses an interactive default eye render size instead of blindly using the OpenXR runtime's recommended eye size. On high-resolution headsets the runtime recommendation can be far too expensive for the current software/headset bridge. Agents can tune it explicitly with `--vr-eye-width <pixels>` and `--vr-eye-height <pixels>`.
- The standalone OpenXR submitter remains a diagnostic/headset-output tool, not the normal playable path:
  `dotnet run --project src\Rekall.Age.Cli -- render openxr submit-scene <projectRoot> <sceneName> 0 0 0`
  The first `0` means continuous submission; the width/height `0 0` means use the runtime-recommended eye size.
- For the black-box FPS scene tested locally, the command was:
  `dotnet run --project src\Rekall.Age.Cli -- render openxr submit-scene .age-blackbox-mcp\RuntimeFPS Main 0 0 0`
- The OpenXR scene submitter is headless when run directly from the CLI. It does not have SDL mouse/keyboard input by itself. Use it to diagnose compositor/headset presentation; use the windowed player for actual play.
