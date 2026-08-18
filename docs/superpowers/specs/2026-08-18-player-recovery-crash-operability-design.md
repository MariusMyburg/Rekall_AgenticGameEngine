# Player Recovery, Crash Diagnostics, and Release Operability Design

## Objective

Make Windows player failures bounded, inspectable, and operationally useful.
Rekall AGE must distinguish recoverable graphics lifecycle failures from fatal
engine or game failures, retry only the former, and persist compact structured
evidence that users and agents can inspect without scraping logs.

This milestone is deliberately engine-general. It does not add game behavior,
genre assumptions, or content authoring.

## Current evidence

- `Rekall.Age.Player.Windows` owns the SDL window, Veldrid device, swapchain,
  pipelines, and resources in one `RekallAgeVeldridPlayer` instance.
- Its render loop submits and swaps directly. Exceptions escape `Main`; there is
  no classified retry or structured crash artifact.
- `PlayerLog` writes a timestamped text log under Local AppData but does not
  expose its path, retention, machine-readable state, or a CLI/MCP inspection
  contract.
- Studio registers unhandled-exception hooks and Serilog output, but that is a
  separate log-only path.
- Native Vulkan diagnostic commands return structured API errors, while the
  windowed Veldrid player does not expose raw Vulkan results directly.

## Honest recovery posture

The first production boundary is a **bounded cold player-session recovery**:
dispose the failed graphics/player session and create a fresh window, device,
swapchain, pipelines, assets, runtime projection, live-edit endpoint, and audio
output from the authored project. This is materially better than an unexplained
process crash and is testable through installed binaries, but it is not seamless
GPU-resource resurrection and does not promise preservation of arbitrary
in-memory module state.

The public evidence must name this recovery mode `cold-session-restart`.
Future work may extract a re-creatable graphics session that preserves the
runtime world and window, but the current milestone must not claim that.

## Failure classification

Introduce an engine-owned classification service with stable categories:

- `graphics.device-lost`: recoverable; includes an engine typed device-loss
  exception and narrowly recognized Veldrid/Vulkan device-loss evidence.
- `graphics.swapchain-invalid`: recoverable; covers out-of-date/suboptimal or
  surface-loss evidence where a fresh session is safe.
- `graphics.initialization-failed`: fatal for this launch; no looped retry.
- `module.trust-rejected`: fatal and already coded; never retry.
- `runtime.unhandled`: fatal; arbitrary engine, module, content, I/O, or
  programmer exceptions are not mislabeled as graphics recovery.

Classification walks a bounded exception chain, never relies on an unbounded
stack/message scan, and records whether the result came from a typed signal or
a narrow compatibility signature.

## Bounded supervisor

Add a player-session supervisor independent from SDL/Veldrid so it can be unit
tested with injected sessions.

- Default maximum: two recovery attempts after the initial session.
- Retry only classified recoverable failures raised after initialization.
- Dispose every failed session before creating the next one.
- Track requested frames, completed frames, attempt count, failure category,
  and final outcome.
- For finite `--frames`, continue until the requested total is completed rather
  than replaying the full count after each recovery.
- For continuous play, retry and resume continuous operation.
- Apply a small bounded delay between retries; cancellation stops immediately.
- Exhaustion returns a stable nonzero exit code and a crash report.
- Successful recovery writes a recovery report, logs the evidence path, and
  returns normal exit code when the requested play session subsequently ends.

The cold restart reloads authored state from disk. This must be stated in the
report so agents do not infer preservation of ephemeral game state.

## Structured diagnostic artifacts

Create a shared `Rekall.Age.Core.Diagnostics` contract and atomic JSON store.
Artifacts live under:

- `REKALL_AGE_DIAGNOSTICS_DIR` when explicitly configured; otherwise
- `%LOCALAPPDATA%\Rekall AGE\Diagnostics`.

Each schema-1 artifact contains:

- report id and UTC timestamp;
- product version/channel and process component;
- outcome (`recovered` or `fatal`), stable category/code, and recovery mode;
- attempt and completed/requested frame counts;
- exception type/message plus a bounded stack excerpt for fatal evidence;
- OS/process architecture and backend;
- project/scene identifiers supplied by the player;
- explicit limitations and next actions.

Writes use a unique temporary file and atomic replacement. The store enforces
bounded report size, bounded enumeration, and retention (latest 50 by default).
It rejects reparse-point diagnostic roots and never writes dumps, environment
variables, module source, scene content, tokens, or arbitrary object graphs.

## Agent and operator surface

Expose `rekall.diagnostics.inspect_failures` through CLI and MCP. It is read-only
and returns the latest bounded reports with filters for component/outcome/code.
Engine status recommends it as a release-operability tool. Player stderr prints
the stable code and report path on fatal exit; normal recovered sessions print
the recovery evidence path.

The existing text log remains useful but becomes secondary evidence.

## Deterministic and installed proof

Add a Windows-player-only diagnostic flag
`--simulate-device-loss-frame <positive frame>`. It raises the same typed signal
at the render boundary once per process, not once per restarted session. The
flag is an operability test hook, does not alter authored content, and is not
available to modules.

Acceptance must prove:

1. a finite installed Windows-player run injects device loss, cold-restarts,
   completes the requested total frames, exits 0, and writes a `recovered`
   report with one recovery;
2. an injected fatal non-graphics failure exits nonzero and writes a bounded
   `fatal` report without retry (unit/process harness as appropriate);
3. retry exhaustion is deterministic and produces a stable code;
4. the existing installed audio-required and ordinary player paths still pass;
5. CLI inspection reads the installed evidence.

## Non-goals

- seamless Vulkan device recreation in the same window;
- preservation/serialization of arbitrary in-memory module state;
- OS minidumps or third-party crash-upload services;
- automatic telemetry or network transmission;
- retrying arbitrary exceptions;
- masking module trust, validation, or authored-content failures;
- Studio workbench redesign.

## Completion standard

Complete only after deterministic supervisor/classifier/store tests, Windows
player integration tests, exact CLI/MCP evidence, two full Release test passes,
installed positive recovery proof, fatal/exhaustion negative proof, and the
unchanged gauntlet/package/UI/audio/soak product matrix all pass.
