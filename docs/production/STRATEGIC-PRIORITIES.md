# Rekall AGE Strategic Priorities

Last aligned: 2026-08-25

This document converts the product vision into the current execution order. It
is deliberately capability-first: substantial playable evidence has priority
over exhaustive hardening of pathological cases while ordinary authoring,
runtime, Studio, and deployment paths remain incomplete.

## Product truth

Rekall AGE has a real proprietary, AI-first C# engine foundation. Its ordinary
authoring path includes project-scoped LLM sessions, progressive MCP discovery,
typed commands, portable C# runtime modules, semantic input, deterministic
runtime inspection, Vulkan rendering, a WebGPU RenderingDevice, physics,
audio, animation, UI, modeling, packaging, and Studio workflows.

Rekall AGE is not yet production-ready, Godot-equivalent, Blender-grade, or
capable of publishing a normal AGE game to the browser. Contract tests and
package audits are valuable evidence, but they do not substitute for an
independently reviewed, playable game after the latest mutation.

## Immediate 2026-08-25 controller escalation: comprehensive modeling

The Aetherfall controller review rejected the current world as overly
bright/gray and visibly low-poly. AGE's first editable-mesh foundation was
necessary but not remotely comprehensive enough. The immediate engine and
flagship priority is the independent Blender/Godot-informed modeling expansion
specified in
`docs/superpowers/specs/2026-08-25-comprehensive-agentic-modeling-design.md` and
planned in
`docs/superpowers/plans/2026-08-25-comprehensive-agentic-modeling-expansion.md`.

Wave 1 adds production bevel/inset, solidify, mirror, array/instances,
weighted/split normals, curves/profile sweep, broader primitives, and the
selection/agent/Studio surfaces required to use them. Aetherfall must visibly
consume at least five of these capabilities before renderer-tier capture and
package acceptance resume. Catalog existence or tessellation-only polygon
growth does not count as visual acceptance.

The phrase "100% C#" means that engine and game logic remain C#. Platform
interop may use the minimum required JavaScript, XAML describes Windows UI,
and shaders use their native shader languages. Those seams must never become a
second game engine or a place where authored gameplay is rewritten.

## Immediate execution order

1. **Accept a substantial original platformer on installed Windows.** A normal
   user request must drive the local model through the ordinary Studio/LLM/MCP
   path. Require movement, jumping, collision/grounding, hazards or enemies,
   collectibles, score/lives, goal, death/respawn, reset/replay, camera, HUD,
   strict deterministic gameplay assertions, independent visual review,
   manual play, relocation, package audit, and archived evidence.
2. **Publish that exact AGE project to the browser.** Browser export must bundle
   the same project manifest, scene, assets, and agent-authored C# modules. The
   .NET WASM player must execute the shared deterministic C# runtime, use
   build-time/static module registration suitable for WASM/AOT, bridge browser
   input into the same semantic input stream, and render the scene directly
   through AGE's WebGPU backend. Add web package integrity/audit and browser
   gameplay assertions.
3. **Close the Modeling-to-game common path.** Runtime rendering and physics
   must resolve published Model Assets. Prove visible and physical placement,
   rebuild, and hot refresh. Studio must expose Publish/Update, dependency
   health and thumbnails, viewport drag/drop, and component/C# script
   attachment through the canonical commands.
4. **Complete the professional Studio consumer.** Prioritize the workflows
   required by real games: asset previews, component and module editing,
   build/runtime diagnostics, proper docking, modeling navigation, node
   editing, capture, recovery, and installed-product parity. Studio must not
   hide manual JSON or CLI steps required for an ordinary authoring loop.
5. **Drive breadth with accepted games.** Complete and independently accept
   Pong, an original Galaga-like game, shader-driven rain-on-glass, the
   platformer, and a 3D model/physics game. Use their common failures to drive
   generic tile/terrain, navigation, particles, shadows/environment, material
   integration, render-graph attachments, physics breadth, asset cooking,
   audio, profiling, and deployment work.

## Post-Aetherfall: multimodal design references in Studio

Do not interrupt the active Aetherfall milestone for this work. After the
flagship game is accepted, add the capability in evidence-driven stages:

1. Let users paste, attach, or drag images into the ordinary Studio agent
   conversation, preview/remove them before sending, and deliver them with the
   text request to multimodal providers. Store project attachments within the
   selected project boundary with provenance, size/type limits, privacy-safe
   diagnostics, and explicit provider capability reporting.
2. Add a lightweight persistent project References library. Users and agents
   can label images as mood, environment, character, material, UI, gameplay,
   composition, or another free-form role; cite a reference from later
   messages; and distinguish inspiration from edit targets and shippable
   assets. References never silently enter a package or become copied content.
3. Let real usage determine whether a broader Game Design workspace is useful.
   If added, it should connect vision, design pillars, references, mechanics,
   locations, characters, art direction, tasks, and acceptance evidence while
   retaining ordinary chat as the fastest path. Avoid a mandatory form-heavy
   design bureaucracy or a second hidden source of project truth.

Acceptance must prove a user can send a text-and-image request from Studio,
the selected provider receives the intended bounded inputs, an agent can use a
persisted reference in later authoring, and project/package audits report the
reference provenance without bundling it unless explicitly requested.

## Genuine web publishing contract

A web export is valid only when all of the following are true:

- it consumes an ordinary AGE project rather than a hard-coded proof workload;
- the same C# gameplay module behavior runs in browser WASM;
- scenes, assets, and module identities are packaged with integrity metadata;
- browser keyboard, pointer, touch, and gamepad facts enter AGE's generic input
  stream;
- AGE converts its runtime scene projection into direct WebGPU resources,
  pipelines, buffers, textures, and draw commands;
- resize, focus, pause/resume, failure diagnostics, and package relocation are
  explicit;
- deterministic browser inputs change inspectable AGE game state;
- the resulting visual output and gameplay are independently exercised in a
  real browser.

The JavaScript layer is limited to browser/platform interop: WebGPU, WebAudio,
input capture, storage, fullscreen/focus/resize, and networking. It does not
own world state, collision, game rules, rendering composition, or authored
gameplay.

## Forbidden shortcuts

The following do not prove AGE web publication and must not be presented as if
they do:

- uploading CPU-rasterized finished frames as the shipping renderer;
- recreating or hand-writing the game in JavaScript or Canvas2D;
- creating a platformer-specific engine path instead of generic scene/runtime
  contracts;
- streaming a Windows player into a browser;
- playing prerecorded screenshots or video;
- pairing headless AGE simulation with unrelated browser visuals;
- hosting a desktop game archive without executing it in browser WASM;
- calling the moving-dot or triangle RenderingDevice proof a playable export;
- declaring success from page load, package audit, or pixels without semantic
  input changing AGE runtime state and without real gameplay review.

## Evidence hierarchy

For each game milestone, evidence is cumulative:

1. source and schema inspection;
2. clean build and targeted tests;
3. strict deterministic runtime input/state assertions after the latest edit;
4. real player launch and manual gameplay;
5. independent visual review of current frames;
6. package, relocation, and audit;
7. installed-product execution;
8. for portable games, browser execution of the unchanged AGE project.

No lower level alone proves the higher levels.

