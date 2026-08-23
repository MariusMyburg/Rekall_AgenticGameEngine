# Rekall AGE Strategic Priorities

Last aligned: 2026-08-23

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

