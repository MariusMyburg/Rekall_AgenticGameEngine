# Runtime Soak and Performance Inspection Design

## Purpose

Rekall AGE needs repeatable evidence that an authored scene can run for a
meaningful fixed-step duration without losing temporal continuity, allowing
frame-local facts to grow without bound, or exceeding explicit resource and
throughput budgets. The evidence must be available to developers, AI agents,
CI, installed-product acceptance, and eventually Studio through one generic
engine command.

This first contract is an offline diagnostic, not an in-player profiler. It
does not encode a genre, controller, rendering backend, or game loop.

## Selected approach

Add `rekall.runtime.inspect_soak`, backed by the existing scene store, runtime
world builder, and fixed-step execution loop. It loads a scene once, executes
it in bounded chunks, records compact checkpoints, and evaluates explicit
budgets. CLI and MCP invoke the same command.

Alternatives rejected for this phase:

- Test-only stopwatch assertions are not inspectable by agents and become
  hardware-dependent hidden policy.
- A resident telemetry/profiler service introduces sampling, persistence, UI,
  and lifecycle concerns before a stable diagnostic contract exists.

## Request contract

The request contains:

- project root and scene name;
- total frames, defaulting to 3,600 fixed frames;
- checkpoint interval, defaulting to 600 frames;
- minimum simulated frames per wall-clock second, where zero disables the
  machine-dependent throughput blocker;
- maximum retained managed-memory growth in bytes, where a negative value
  disables the blocker;
- maximum entity-count growth from the initial authored world;
- maximum observations and events allowed at any checkpoint; and
- whether the ordered runtime-system set must remain stable.

Frame and checkpoint counts are bounded to prevent the diagnostic itself from
becoming an unbounded workload. Invalid requests return structured errors
before loading the scene.

## Execution and measurements

The command builds the initial runtime world once and creates one default
execution loop for the project. It captures baseline managed memory after a
full collection, then runs consecutive chunks by passing each resulting world
into the next execution call. A final partial chunk is permitted.

Each checkpoint records:

- frame index and deterministic elapsed seconds;
- cumulative wall-clock milliseconds and effective simulated frames/second;
- entity, component, observation, event, and runtime-system counts;
- retained and sampled managed memory; and
- active physics, audio, animation, UI, rendering, and XR counts needed to
  explain the workload without serializing the entire world.

The result also records baseline/final retained memory, peak sampled memory,
total wall time, throughput, completed frames, final frame/elapsed values, and
the ordered systems run.

## Checks and failure behavior

Every result contains named checks with a passed flag, measured value, limit,
and explanation. The following are always evaluated:

- frame continuity: final frame equals initial frame plus requested frames;
- elapsed continuity: final fixed-step time matches the expected duration;
- complete execution: every requested frame completed; and
- stable system order when requested.

Configured checks cover throughput, retained managed-memory growth, entity
growth, checkpoint observations, and checkpoint events. A failed check returns
`REKALL_RUNTIME_SOAK_BUDGET_EXCEEDED` with the complete result preserved for
diagnosis. Cancellation propagates normally and never becomes a passing result.

Working-set values are advisory only because they include runtime and native
allocator behavior outside the managed scene simulation. The initial gate uses
retained managed memory and a deliberately conservative throughput floor in an
isolated installed CLI process.

## Integration

- Register the command in the CLI/MCP command registry and progressive agent
  catalog.
- Add a CLI surface for direct installed-product use.
- Add concise engine-status guidance so agents discover the command for soak,
  stability, and performance evidence.
- Extend installed distribution acceptance with a generic scene that runs the
  command under conservative budgets and emits its measured result.
- Do not couple the command to Studio. Studio will later consume the same
  result contract.

## Verification

Test-first coverage will prove:

1. a multi-chunk run reaches the exact frame and elapsed time with stable
   systems and bounded checkpoint data;
2. an intentionally impossible throughput or memory/entity budget returns the
   structured failure while preserving measurements;
3. invalid frame/checkpoint inputs fail before scene loading;
4. CLI/MCP catalogs expose the command; and
5. the assembled installed distribution completes the soak check.

The full Debug suite, two independent Release passes, zero-warning build,
distribution assembly, and installed acceptance remain the product gate.
