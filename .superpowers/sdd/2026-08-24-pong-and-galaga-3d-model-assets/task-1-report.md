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

Concern: Vulkan viewport captures retain elapsed timing but currently return an
empty projected-action list; the software deterministic path used by package
proof exposes the final projected actions and is covered by the focused test.

## Commit

Implementation commit: `bffe85b7778e997e89a761c4f5be1adfb2546f3e`
