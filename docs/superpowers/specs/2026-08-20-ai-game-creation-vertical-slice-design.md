# AI Game-Creation Vertical Slice Design

**Date:** 2026-08-20

## Outcome

Rekall AGE must expose one coherent Windows desktop workflow in which a user
can create or open a project, ask an AI agent to author or revise a game through
generic engine commands and C# modules, inspect and directly edit the result,
validate it, see a real rendered frame, and launch play mode.

This is a product integration tranche, not a new game template system. The
engine exposes generic, inspectable primitives; the user and agent own game
content and behavior.

## Architectural boundary

The reusable center is a UI-independent workbench session in
`Rekall.Age.Editor`. It owns project/scene identity, selection, command
execution, transaction publication, model refresh, validation, viewport
capture, and player lifecycle. Studio is a WPF adapter over that session. MCP,
CLI, and the embedded Ollama agent continue to use the same registered
`rekall.*` command contracts.

Studio must not mutate scene JSON directly or maintain a second hidden world.
Every mutation goes through the command registry and returns structured result,
diagnostic, changed-resource, and next-action data.

## User workflow

1. Launch Studio into a useful welcome state.
2. Create a project and initial scene, or open an existing project directory.
3. Select entities and inspect structured components/properties.
4. Execute generic authoring actions and property edits with validation and
   optimistic document revision checks.
5. Ask the configured Ollama agent to author or revise the current project;
   show bounded progress, tool calls, completion state, and failures, and allow
   cancellation.
6. Refresh the same workbench model after every human or agent transaction.
7. Capture and display an actual engine-rendered viewport frame.
8. Launch, stop, and observe the real Windows player for the current scene.
9. Validate and package through the same engine workflows.

## First production slice

The first slice deliberately prioritizes reliable orchestration over a broad
property-editor widget set:

- session create/open/reload and scene selection;
- entity selection with structured inspector state;
- command-backed validate, capture, play, stop, undo, and redo;
- nonblocking operation state and bounded user-visible failures;
- rendered PNG viewport rather than descriptive placeholder text;
- Studio command wiring and an executable end-to-end test seam.

The embedded agent panel follows on that stable session surface. It uses the
existing provider-neutral agent, Ollama client, MCP tool executor, and embedded
agent contract; no Studio-only tool semantics are introduced.

## Failures and lifecycle

- Long operations are asynchronous and cancellable; the WPF dispatcher is not
  blocked.
- A failed command leaves the last valid workbench model visible and reports a
  bounded structured failure.
- Player processes are explicitly owned and stopped on project change or
  Studio shutdown.
- Captures publish atomically and are loaded without retaining file locks.
- Agent completion never implies success: Studio refreshes, validates, and
  displays verification state from engine commands.
- Project and scene paths remain confined by the existing stores and commands.

## Acceptance

The tranche is complete only when automated tests and a packaged-binary run
prove that a fresh Studio session can create/open a project, execute mutations,
undo/redo, validate, display a nonblank rendered frame, launch/stop the player,
and run an agent-authored revision whose resulting project validates and plays.
The existing CLI/MCP game-authoring loop must remain green.
