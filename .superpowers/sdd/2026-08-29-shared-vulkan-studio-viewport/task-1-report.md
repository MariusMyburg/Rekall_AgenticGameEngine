# Task 1 Report: Shared presentation contracts and Win32 surface

## Outcome

Implemented the new `Rekall.Age.Rendering.Windows` contract assembly and added focused tests for the Task 1 contract requirements.

## Files changed

- Added `src/Rekall.Age.Rendering.Windows/Rekall.Age.Rendering.Windows.csproj`
- Added `src/Rekall.Age.Rendering.Windows/RekallAgeVulkanPresentationModels.cs`
- Added `src/Rekall.Age.Rendering.Windows/RekallAgeWin32RenderSurface.cs`
- Added `tests/Rekall.Age.Tests/Rendering/VulkanPresentationContractTests.cs`
- Updated `tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj`
- Updated `tests/Rekall.Age.Tests/packages.lock.json`
- Updated `Rekall.AGE.sln`

## TDD notes

1. Added `VulkanPresentationContractTests` first, referencing the new shared Windows rendering namespace before it existed.
2. Ran the required focused test command and verified RED from missing `Rekall.Age.Rendering.Windows`, `IRekallAgeVulkanPresentationSession`, `RekallAgeWin32RenderSurface`, and `RekallAgeVulkanPresentationFrame`.
3. Implemented the minimal contract assembly to satisfy the tests.
4. Re-ran the same focused command to GREEN.

## What was implemented

### Shared session contract

- Added `IRekallAgeVulkanPresentationSession` with:
  - `PresentAsync(RekallAgeWin32RenderSurface surface, RekallAgeRuntimeViewportFrame frame, RekallAgeRuntimeViewportAssetSet assets, CancellationToken cancellationToken)`
  - forward-compatible default no-op invalidation hooks for assets and shaders

### Presentation telemetry/result model

- Added `RekallAgeVulkanPresentationFrame`
- Validates:
  - non-empty scene/backend/acceleration status
  - positive width/height
  - non-negative renderable/observation counts
- Added `FromViewportFrame(...)` helper to project the existing runtime viewport frame into Vulkan presentation telemetry
- Default success telemetry reports:
  - `BackendId = "vulkan"`
  - `HardwareAccelerated = true`
  - `AccelerationStatus = "hardware"`
- Reserved optional extension points for later tasks:
  - `PresentedImage`
  - `VulkanInterop`

### Win32 surface ownership model

- Added `RekallAgeWin32RenderSurface`
- Validates non-zero HWND and positive dimensions
- Supports owned and external surface creation:
  - `CreateExternal(...)`
  - `CreateOwned(...)`
- Disposal is idempotent
- External surfaces never call the destroy callback
- Owned surfaces use `DestroyWindow` by default unless a custom destroy delegate is supplied
- Added `WithSize(...)` and `Clone()` for future resize/session handoff needs

## Verification

Focused RED:

`dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~VulkanPresentationContractTests`

- Failed with missing shared Windows rendering contract types, as expected.

Focused GREEN:

`dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~VulkanPresentationContractTests`

- Passed: 6
- Failed: 0
- Skipped: 0

Additional check:

- `git diff --check` returned only LF/CRLF warnings on touched text files, with no whitespace errors.

## Notes / concerns

- `PresentedImage`, `VulkanInterop`, and the invalidation hooks are contract placeholders for Task 2+ and are not populated by a real renderer yet.
- The focused task did not add Player or Studio integration; those remain for later tasks by design.
- Left the unrelated untracked `smooth-sim.nettrace` and `smooth-sim.nettrace.etlx` artifacts untouched.
