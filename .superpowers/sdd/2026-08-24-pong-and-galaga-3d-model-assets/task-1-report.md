# Task 1 report — generic playable capture input

## Status

DONE

## Root cause and data-path comparison

The capture contracts were split from the working runtime input contract.

Working path (`rekall.runtime.inspect_scene`): `InspectSceneRuntimeRequest.Inputs`
uses `RekallAgeRuntimeInputFrame`, which normalizes via `ToState()`, runs through
`RekallAgeInputActionSystem`, and exposes declared `Rekall.InputActionMap`
actions to authored runtime systems. The frame supports held keys, press/release
edges, and named semantic actions.

Broken direct capture path: `rekall.play.capture_frame` accepted
`RekallAgePlaybackInput` (`verticalAxis`, `primaryAction`, `deltaSeconds`) and
passed it directly to a playable module. It had no runtime input-frame projection
or named action query.

Broken package capture path: `rekall.workflow.capture_playable_package_frame`
accepted the same legacy playback list only to pass it to `RunPlayablePackage`;
the subsequent `CaptureRuntimeViewportCommand` received no inputs, so the
authored runtime scene was always captured with neutral input.

Root-cause hypothesis (confirmed): the two capture commands retained a separate
Clockwork-Canopy-era playback input type instead of accepting and forwarding the
canonical runtime input frame used by runtime inspection; package viewport capture
then dropped the request entirely.

## RED and GREEN evidence

RED 1:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~CapturePlayableFrameProjectsSemanticActionsAndInputEdgesIntoThePlayableModule|FullyQualifiedName~CapturePlayableFrameSchemaDocumentsGenericInputFrames|FullyQualifiedName~PackageCaptureForwardsGenericInputFramesIntoTheRuntimeViewport"
```

Observed expected compile failures: `CS0029` converting
`RekallAgeRuntimeInputFrame` to the legacy `RekallAgePlaybackInput` at both
capture request boundaries.

RED 2:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~PackageCaptureForwardsGenericInputFramesIntoTheRuntimeViewport" --logger "console;verbosity=minimal"
```

Observed expected compile failure: `CS1061`, package capture result lacked
`ElapsedSeconds`, so the requested per-frame `deltaSeconds` could not be proven.

GREEN:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~CapturePlayableFrameProjectsSemanticActionsAndInputEdgesIntoThePlayableModule|FullyQualifiedName~CapturePlayableFrameSchemaDocumentsGenericInputFrames|FullyQualifiedName~PackageCaptureForwardsGenericInputFramesIntoTheRuntimeViewport" --logger "console;verbosity=minimal"
```

Result: Passed 3/3. Coverage proves semantic projection, repeated held state,
transient press/release, generic package forwarding, schema documentation, and
0.1s + 0.2s package capture elapsed time.

## Implementation

- `RekallAgeRuntimeInputFrame` is now the canonical `Inputs` item for both
  capture requests and carries `deltaSeconds`; documented `primaryAction` and
  `verticalAxis` remain bounded legacy compatibility fields.
- Direct playable capture projects the canonical frame through the existing
  `RekallAgeInputActionSystem`; playable modules receive generic action helpers
  (`InputActionValue`, `IsInputActionDown`, `WasInputActionPressed`, and
  `WasInputActionReleased`). Existing legacy `RekallAgePlaybackInput` has an
  implicit compatibility conversion.
- Runtime viewport and packaged capture forward generic frames intact, report
  projected input actions and elapsed seconds, and honor a valid per-frame
  `deltaSeconds` during snapshot execution.
- CLI package-frame capture now accepts an optional generic frames JSON argument.
- Both public capture schemas show semantic-action/physical-input examples and
  identify legacy fields as non-canonical.

## Focused verification

```powershell
dotnet build Rekall.AGE.sln -c Release --no-restore -v:minimal
```

Result: succeeded with 0 warnings and 0 errors.

## Self-review and concerns

Reviewed `git diff --check` and the changed runtime, capture, workflow, CLI, and
test paths. No genre-specific engine behavior or authored-game files were added.

No remaining concerns.

## Commit

Implementation commit: `bffe85b7778e997e89a761c4f5be1adfb2546f3e`

## Repair loop — compatibility, Vulkan diagnostics, and deterministic timing

### Root cause

The generic request-type replacement removed collection-level source
compatibility: an `IReadOnlyList<RekallAgePlaybackInput>` cannot implicitly
convert to an `IReadOnlyList<RekallAgeRuntimeInputFrame>`, even though its
elements can convert. Vulkan scene capture then discarded the already-projected
input actions, while its clear-only path built a result without carrying either
actions or elapsed time. Finally, the snapshot service passes the fixed tick as
`TimeSpan.FromSeconds(1.0 / 60.0)` per frame; repeatedly adding that rounded
value changed the engine's established fixed-step timeline semantics and caused
deterministic elapsed-time drift.

### RED and GREEN evidence

RED compatibility command:

```powershell
dotnet test Rekall.AGE.sln -c Release --no-restore --filter "FullyQualifiedName~CapturePlayableFrameCommandRasterizesModuleDrawCommands|FullyQualifiedName~CapturePlayablePackageFrameRequestTests|FullyQualifiedName~CaptureRuntimeViewportCommandCanUseVulkanForClearOnlyRuntimeFrames|FullyQualifiedName~CaptureRuntimeViewportCommandRoutesVulkanSceneRenderablesToSceneCapture"
```

Before the adapter, compilation failed with `CS1503`: a variable typed
`IReadOnlyList<RekallAgePlaybackInput>` could not bind to either capture
request's runtime-frame collection parameter.

RED timing command:

```powershell
dotnet test Rekall.AGE.sln -c Release --no-restore --filter "FullyQualifiedName~RuntimeSoakPrintsCheckpointsAndPassedChecks|FullyQualifiedName~DefaultRuntimeAppliesDeterministicCelestialRotation|FullyQualifiedName~ExecutionLoopAdvancesFramesDeterministically|FullyQualifiedName~SoakResumesAcrossChunksWithExactDeterministicContinuity"
```

Observed four failures, including `194.99928` versus `195` celestial rotation
and `0.0499998` versus `0.05` elapsed time.

GREEN focused repair command:

```powershell
dotnet build src\Rekall.Age.Cli\Rekall.Age.Cli.csproj -c Debug --no-restore
dotnet test Rekall.AGE.sln -c Release --no-restore --filter "FullyQualifiedName~CapturePlayableFrameCommandRasterizesModuleDrawCommands|FullyQualifiedName~CapturePlayablePackageFrameRequestTests|FullyQualifiedName~CaptureRuntimeViewportCommandCanUseVulkanForClearOnlyRuntimeFrames|FullyQualifiedName~CaptureRuntimeViewportCommandRoutesVulkanSceneRenderablesToSceneCapture|FullyQualifiedName~RuntimeSoakPrintsCheckpointsAndPassedChecks|FullyQualifiedName~DefaultRuntimeAppliesDeterministicCelestialRotation|FullyQualifiedName~ExecutionLoopAdvancesFramesDeterministically|FullyQualifiedName~SoakResumesAcrossChunksWithExactDeterministicContinuity"
```

Result: 8/8 passed. The Debug CLI build is necessary because the CLI test
intentionally launches `src/Rekall.Age.Cli/bin/Debug/net10.0` even when the
test project is invoked with `-c Release`.

### Repair summary

- Both capture request records now provide a legacy-list constructor adapter;
  direct capture executes an old-list variable successfully, while package
  request conversion is checked at runtime.
- Vulkan scene and clear-pass results both forward the projected `InputActions`
  and the inspected `ElapsedSeconds`.
- The runtime execution loop uses the original frame-indexed fixed-step timeline
  for the fixed tick, while non-fixed supplied deltas retain explicit per-frame
  accumulation.
- New/extended tests prove source compatibility, Vulkan clear diagnostics,
  Vulkan scene diagnostics, and the repaired timing failures.

### Full Release suite attempt

`dotnet test Rekall.AGE.sln -c Release --no-build --no-restore` was started
after the focused pass. `Rekall.Age.Studio.Tests` completed 55/55 passing and
its TRX recorded zero failures. `Rekall.Age.Tests` then stalled for several
minutes with no CPU progress or result file, so the verified stalled test host
was stopped. This was subsequently rerun cleanly by the controller on commit
`cbd7853`: engine 1684/1684 and Studio 55/55 passed.

## Second repair loop — JSON gateway and null compatibility

### Root cause

The first compatibility repair added two request constructors with only the
last collection type differing. `System.Text.Json`, which powers the dynamic
registry/MCP command boundary, could not select between those constructors.
The same overload set made an explicit positional final `null` ambiguous to C#
callers.

### RED and GREEN evidence

RED:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CapturePlayablePackageFrameRequestTests"
```

Observed expected compile failures: `CS0121` for both positional final-null
constructions and `CS1061` because the canonical `InputFrames` field did not
yet exist.

GREEN:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CapturePlayablePackageFrameRequestTests|FullyQualifiedName~CapturePlayableFrameCommandRasterizesModuleDrawCommands|FullyQualifiedName~CapturePlayableFrameProjectsSemanticActionsAndInputEdgesIntoThePlayableModule|FullyQualifiedName~PackageCaptureForwardsGenericInputFramesIntoTheRuntimeViewport|FullyQualifiedName~CaptureRuntimeViewportCommandCanUseVulkanForClearOnlyRuntimeFrames|FullyQualifiedName~CaptureRuntimeViewportCommandRoutesVulkanSceneRenderablesToSceneCapture|FullyQualifiedName~RuntimeSoakPrintsCheckpointsAndPassedChecks|FullyQualifiedName~DefaultRuntimeAppliesDeterministicCelestialRotation|FullyQualifiedName~ExecutionLoopAdvancesFramesDeterministically|FullyQualifiedName~SoakResumesAcrossChunksWithExactDeterministicContinuity"
dotnet build Rekall.AGE.sln -c Release --no-restore -v:minimal
```

Result: 13/13 focused tests passed; Release build succeeded with 0 warnings and
0 errors.

### Repair summary

- Each capture request once again has one unambiguous primary constructor:
  legacy `Inputs` remains `IReadOnlyList<RekallAgePlaybackInput>?`.
- `InputFrames` is the distinct canonical generic JSON/CLI field. Direct capture
  prefers it and falls back to legacy projection; package capture does the same
  before invoking the runtime viewport.
- CLI, schemas, and generic test calls use `inputFrames` / `InputFrames`.
- Dynamic registry JSON probes deserialize semantic actions, physical key edges,
  and per-frame delta values for both capture request models. A positional-final
  null test prevents overload ambiguity from returning.
