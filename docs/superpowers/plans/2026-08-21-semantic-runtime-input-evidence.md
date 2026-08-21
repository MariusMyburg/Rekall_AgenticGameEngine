# Semantic Runtime Input Evidence Implementation Plan

Date: 2026-08-21

Status: complete; unchanged benchmark exposed the next independent blocker

## Objective

Make deterministic runtime gameplay testing use the same semantic action
contract that agent-authored modules consume. Agents must be able to inject a
declared action such as `move.horizontal` without reverse-engineering a raw
keyboard binding, and ineffective input payloads must fail before simulation
with an exact copyable shape.

This is a generic runtime and authoring capability. It does not add a
controller, genre, or game-specific behavior to the engine.

## Measured failure

Fresh installed Lumen Vault benchmark 17 used real local `qwen3.5:35b`. It
compiled an agent-authored runtime system, declared three semantic actions,
and captured six visible renderables. Across repeated strict checkpoints it
sent fields such as `move_horizontal` and `move_vertical` in runtime input
frames. `RekallAgeRuntimeInputFrame` exposed only raw device state, so the JSON
serializer ignored those unknown fields, every projected action remained
zero, and the 76-turn bounded run ended without a package.

## Contract

- Add a bounded typed semantic-action sample to runtime input frames and input
  state: name, finite value, down/pressed/released facts.
- The input action system applies a semantic sample only to a matching action
  declared by an active `Rekall.InputActionMap`; direct injection does not
  create undeclared bindings.
- A semantic sample overrides the raw-device projection for that declared
  action in the frame. This makes deterministic evidence exact while retaining
  raw keyboard, mouse, gamepad, and XR testing.
- Runtime inspection and agent checkpoint guidance expose a copyable shape:
  `{"semanticActions":[{"name":"move.horizontal","value":1,"isDown":true}]}`.
- Checkpoint preflight treats an input array containing no effective raw or
  semantic input as missing input and rejects it before command execution.
- Bounds reject blank names, non-finite values, excessive sample counts, and
  duplicate semantic names in one frame with structured diagnostics.

## Tasks

- [x] Add failing runtime tests for declared semantic injection, undeclared
  isolation, raw-input compatibility, and invalid semantic samples.
- [x] Add failing agent-loop tests proving ineffective input frames are
  rejected with exact semantic-action repair guidance.
- [x] Implement the version-compatible runtime input records and projection.
- [x] Implement bounded inspection validation and update command/agent
  descriptions.
- [x] Pass the focused runtime, MCP, CLI, and agent selections.
- [x] Rerun Lumen Vault from an empty project through real installed Qwen 3.5
  and independently verify gameplay, validation, capture, package, relocation,
  and audit.
- [x] Update `docs/production/PROGRESS.md`, run the full Release gate, commit,
  and push the safety checkpoint.

Benchmark 18 proved that the model consumed the new contract: every executable
checkpoint used typed `semanticActions`, so unknown flat fields no longer
entered the runtime. The run then exposed a separate structured-property
authoring failure: `Rekall.InputActionMap.Actions` was stored as an encoded JSON
string instead of a JSON array, leaving zero declared runtime actions. That
measured failure is the next generic repair target; this plan does not claim
the full Lumen Vault acceptance is green.
