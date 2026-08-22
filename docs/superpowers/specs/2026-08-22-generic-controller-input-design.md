# Generic Keyboard, Mouse, Gamepad, and Joystick Input Design

## Goal

Complete Rekall AGE's generic input stack so authored games and AI agents can
use keyboard, mouse, SDL game controllers, arbitrary joysticks, and OpenXR
through one inspectable semantic-action contract across runtime, Player, CLI,
MCP, SDK, packaging, and deterministic tests.

## Existing foundation

The runtime already carries keyboard key sets, mouse buttons/position/delta/
wheel, XR poses/actions, and injected semantic actions. `Rekall.InputActionMap`
projects keyboard and mouse controls into scalar semantic actions; modules use
`InputActionValue`, `IsInputActionDown`, `WasInputActionPressed`, and
`WasInputActionReleased`. The Windows Player captures keyboard/mouse through
Veldrid SDL. CLI and MCP expose deterministic input frames through
`rekall.runtime.inspect_scene` and `rekall.run.scene`.

The missing production layer is physical gamepad/joystick capture, device
identity/hot-plug, controller-axis/button projection, controller diagnostics,
and rebinding persistence.

## Runtime contracts

Add immutable generic records:

- `RekallAgeRuntimeInputDevice`: stable id, kind (`keyboard`, `mouse`,
  `gamepad`, `joystick`, `openxr`), display name, connection state, optional
  player index, vendor/product ids, mapping, axis/button/hat counts.
- `RekallAgeRuntimeControllerState`: device id, kind, player index, named finite
  axes, held/pressed/released buttons, and named hats.
- `RekallAgeRuntimeControllerAxis`: canonical name and normalized value.
- `RekallAgeRuntimeControllerHat`: canonical name plus X/Y in `-1,0,1`.

`RekallAgeRuntimeInputState` and deterministic `InputFrame` gain bounded device
and controller arrays. Existing positional constructors remain source-compatible
by appending optional properties. Runtime input views and inspection results
expose bounded current devices and controller states. Semantic actions gain
optional physical source device id/kind while retaining the authored source
entity fields.

## Semantic bindings

`Rekall.InputActionMap.Actions` retains existing keyboard/mouse fields and adds:

- `controllerButton`, `positiveControllerButton`, `negativeControllerButton`
- `controllerAxis`, `controllerAxisScale`, `deadzone`, `saturation`, `invert`,
  `responseExponent`
- `controllerHat` plus `controllerHatDirection`
- optional `deviceId`, `deviceKind`, and `playerIndex` filters

`gamepadButton`, `gamepadAxis`, and `joystick*` spellings are accepted aliases
for model robustness, but schemas advertise canonical controller names. Multiple
matching devices contribute in stable device-id order and the final scalar is
clamped to `[-1,1]`. Deadzone uses rescaled radial-independent scalar behavior:
values inside the threshold are zero and the remaining range is remapped to
preserve full travel. Invalid non-finite or out-of-range binding parameters emit
structured input observations instead of silently producing unstable values.

Canonical gamepad controls use SDL names: `LeftX`, `LeftY`, `RightX`, `RightY`,
`LeftTrigger`, `RightTrigger`, `A`, `B`, `X`, `Y`, `Back`, `Guide`, `Start`,
`LeftStick`, `RightStick`, `LeftShoulder`, `RightShoulder`, and D-pad directions.
Arbitrary joysticks use `Axis0..N`, `Button0..N`, and `Hat0..N` directions.

## Windows SDL capture

Add an isolated `RekallAgeSdlControllerInputSource` behind a narrow injectable
native API. It polls SDL after the window event pump, opens recognized game
controllers with SDL's mapping database, falls back to raw joystick controls,
tracks instance ids and handles, and closes removed devices. Each poll emits one
immutable snapshot with normalized axes and exact held/pressed/released edges.

Hot-plug is reconciled every poll without restarting the Player. Stable ids use
SDL GUID plus instance identity; player indices remain deterministic for the
session. Disconnect releases held buttons once and emits a disconnected device
fact. Focus loss releases keyboard, mouse, and controller held state. The source
has hard limits for devices, axes, buttons, and hats and rejects malformed native
counts.

The implementation P/Invokes the SDL2 already shipped by Veldrid; it adds no
new package. A native adapter seam lets tests prove normalization, edges,
hot-plug, mapping/fallback, cleanup, and limits without physical hardware.

## Rebinding and persistence

Add portable generic commands:

- `rekall.input.inspect`: bounded devices, live controller state, authored maps,
  resolved bindings, and structured issues.
- `rekall.input.rebind`: transactionally replace one named semantic binding on
  a selected `Rekall.InputActionMap` entity using the existing component
  admission and scene revision contracts.
- `rekall.input.reset_binding`: restore the scene-authored binding captured as
  the command's baseline or remove a user override.

Project-authored defaults remain in scene JSON. Optional per-user overrides are
stored outside the project and keyed by project id, action-map entity id, and
semantic action name; packages never include local overrides. The Player merges
validated overrides at runtime. This design keeps authored games portable while
allowing user rebinding.

## CLI, MCP, SDK, and agent exposure

MCP receives schemas automatically from registered typed commands. CLI adds
`input inspect`, `input rebind`, and `input reset-binding`; runtime/run commands
accept controller arrays in the same JSON/file argument used today. Component
schema descriptions include canonical controller controls and a complete sample.
The runtime SDK inspection contract explains filters, deadzones, and scalar
two-axis usage. Agent prompts prefer semantic actions for gameplay proof while
documenting raw controller frames for device-specific engine tests.

## Validation and diagnostics

Scene validation detects duplicate semantic names in one map, unknown binding
fields/controls, invalid device filters, deadzone/saturation errors, and actions
with no effective binding. Runtime emits bounded observations for injected
device ids absent from declared frames, disconnected required player indices,
and malformed maps. Diagnostics never require a physical controller to validate
portable content.

## Acceptance

- Existing keyboard/mouse/XR tests remain green.
- Deterministic runtime tests prove gamepad axis deadzone/inversion, button
  edges, two controllers with player filters, joystick axis/button/hat aliases,
  hot-unplug release, and semantic SDK reads.
- Windows source tests prove native normalization and lifecycle through the
  adapter seam; an optional live smoke reports actual attached devices.
- CLI and MCP tests prove discovery, exact schemas, raw-controller frame input,
  inspect, rebind, reset, and bounded output.
- A packaged relocated game accepts both keyboard and gamepad semantic bindings
  without changing its gameplay module.

