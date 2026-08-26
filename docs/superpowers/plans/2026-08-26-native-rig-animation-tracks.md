# Native Rig Animation Track Plan

**Goal:** Let agents author reusable animation clips for AGE-native named-joint rigs, blend those clips with the existing generic mixer/state graph, and replace Aetherfall's code-only limb oscillation with inspectable authored movement.

**Architecture:** Extend the existing versioned `.age.animation.json` track contract rather than creating a second animation ecosystem. A track targeting `Rekall.RigPose` may provide a stable `jointId` and animate that joint's local `rotation`, `translation`, or `scale`. The existing player, mixer, state graph, clip events, asset catalog, loop modes, clocks, and diagnostics remain authoritative. Runtime sampling writes canonical named `jointDeltas` matrices that the existing rig evaluator, renderer, skinning, and joint-attachment contracts already consume.

**Reference principles:** Godot's `AnimationMixer` resolves Skeleton3D transform tracks by stable bone name, retains separate position/rotation/scale channels, initializes them from rest state, and blends quaternion rotation as rotation rather than four unrelated scalars. Godot's `Skeleton3D` keeps named rest hierarchy and local pose channels separate from evaluated global pose. Blender likewise evaluates authored animation before ordered pose constraints and performs influence blending in a defined transform space. AGE adopts those architectural lessons through its own JSON, runtime, and row-vector contracts; no source is copied.

## Tasks

- [x] Add failing runtime acceptance for named native-rig rotation tracks, quaternion interpolation, and mixer blending.
- [x] Add failing ordering acceptance proving a pre-animation agent module can update a mixer and native rig animation consumes it in the same frame.
- [x] Extend generic animation track identity with optional stable `jointId` and keep different joints in distinct blend groups.
- [x] Sample finite normalized quaternions with shortest-path spherical interpolation and blend mixer rotations with hemisphere correction.
- [x] Merge sampled translation/rotation/scale into the target joint's canonical delta matrix while preserving unrelated joints and channels.
- [x] Emit bounded, typed observations for missing joint IDs, unsupported rig properties, malformed channel values, and non-decomposable existing matrices.
- [x] Author Aetherfall idle/walk clips as ordinary animation assets and let its module drive generic mixer weights from semantic movement state.
- [x] Remove redundant game-code limb oscillation once the authored clips prove equivalent gameplay movement.
- [x] Run Aetherfall deterministic movement, native Vulkan comparison, validation, trust, and performance evidence.
- [x] Document residual visual gaps, commit, push, and continue.

## Acceptance

- An inline and an asset-backed generic animation clip can target a named joint in any `Rekall.RigPose` without assuming humanoid anatomy.
- Half-time sampling between identity and a 90-degree authored key produces a normalized approximately 45-degree rotation, not component-wise quaternion shrinkage.
- Mixer layers blend matching joint channels and never merge tracks for different stable joint IDs.
- Agent-authored modules that select the pre-animation phase can change mixer weights before sampling in the same runtime frame without moving animation after event/collision consumers globally.
- Aetherfall movement produces materially different renderer-built Warden vertices from authored idle/walk clips, retains semantic root motion and equipment attachments, and emits zero rig/animation observations.
- Native frame review shows more convincing guarded idle and locomotion timing than the prior sinusoid-only procedural pose. If it does not, revise the authored clips rather than weakening acceptance.

## Deferred follow-ons

- Two-bone IK, pole targets, foot planting, constraints, retargeting, root-motion extraction, Studio curve/dope-sheet editing, and imported/native clip conversion remain separate generic milestones.
- A versioned binary compiled-mesh format remains required to eliminate JSON artifact inflation.
- Provider-neutral Tripo/Meshy generation and normalized GLB import remain queued after the native Aetherfall milestone.

## Verified outcome

- The existing generic animation clip, player, mixer, state, marker, and asset-catalog pipeline now accepts stable named-joint `Rekall.RigPose` tracks for translation, rotation, and scale. No Warden or humanoid behavior was added to engine core.
- Rotation keys validate and normalize finite quaternions, use shortest-path spherical interpolation, and mixer-blend with hemisphere correction. Blend identity includes `jointId`, preventing two bones with the same channel name from collapsing into one group.
- Sampled channels decompose and recompose the canonical local delta matrix while retaining unrelated joints and unanimated channels. Invalid rig track shapes and cubic quaternion requests emit typed observations.
- Runtime ordering remains globally compatible: core animation stays at priority 0, while Aetherfall deliberately selects pre-animation priority -5 to write mixer weights after semantic input and before animation sampling. Animation marker/event timing for other games is not moved behind gameplay globally.
- Aetherfall replaces code-only arm/leg/knee/foot sine matrices with three inspectable assets: presentation, guarded idle, and a keyed 0.8-second armored walk. Its module owns only game state, mixer weights, facing, root/pelvis motion, and upper-body ability accents.
- Runtime animation acceptance passes 31/31 and combined Aetherfall acceptance passes 45/45. Project and scene validation report zero issues, and both modules remain ready under `windows-appcontainer-restricted` trust.
- Native proof is `Examples/AetherfallCitadel/Proof/Captures/WardenAuthoredWalkFinal/vulkan-scene-1280x720-20260826134808283.png`: RTX 5090 High Vulkan, 320 render-work draws, four dispatches, 65 renderables, 13,067 distinct colors, luminance 0.110, and zero observations, missing assets, or fallbacks.
- High 2560x1440 `desktop60` passes at 6.626656 ms measured GPU time, 99 draws, 213,882 triangles, 1,095,508 vertices, and nine textures.
- The authored gait is reusable, inspectable, and visibly articulated, but original-size review still finds the Warden too bright, too procedurally modeled, and too small in the gameplay composition to meet the target. Fitted armor/cloth construction, high-frequency surface detail, lighting/material restraint, stronger close composition, ability clips, IK/foot planting, and Studio curve editing remain active.
