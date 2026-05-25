# Runtime Viewport Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a runtime-backed viewport frame and deterministic software capture path so screenshots, CLI, MCP, and Studio consume `RekallAgeRuntimeWorld` render projections.

**Architecture:** Add renderer-facing runtime viewport frame records to `Rekall.Age.Rendering.Abstractions`, then implement frame building and software PNG capture in `Rekall.Age.Rendering`. Keep `rekall.capture.screenshot` externally compatible while adding `rekall.render.capture_runtime_viewport` for richer runtime frame metadata.

**Tech Stack:** C# 13, .NET 10, xUnit, existing Rekall command bus, existing runtime snapshot service, existing PNG writer, existing CLI switch routing.

---

## File Structure

- Modify `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
  - Add runtime viewport frame, renderable, camera, overlay, and capture records.
- Modify `src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj`
  - Reference `Rekall.Age.Runtime` and `Rekall.Age.Runtime.Abstractions`.
- Create `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`
  - Convert `RekallAgeRuntimeWorld` render projections into `RekallAgeRuntimeViewportFrame`.
- Create `src/Rekall.Age.Rendering/RekallAgeRuntimeSoftwareRenderer.cs`
  - Render deterministic runtime viewport PNGs through `RekallAgePngWriter`.
- Modify `src/Rekall.Age.Rendering/RekallAgeSoftwarePreview.cs`
  - Delegate legacy screenshot capture to the runtime software renderer while preserving `Main_preview.png` naming.
- Create `src/Rekall.Age.Rendering/Commands/CaptureRuntimeViewportCommand.cs`
  - Add command `rekall.render.capture_runtime_viewport`.
- Modify `src/Rekall.Age.Cli/Program.cs`
  - Register the command and route `render viewport capture <root> <scene> <frames> <outputDirectory>`.
- Modify `src/Rekall.Age.Editor.Contracts/RekallAgeWorkbenchModel.cs`
  - Add active camera and viewport capture command hint to runtime panel model.
- Modify `src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs`
  - Build runtime viewport metadata from the runtime world.
- Modify `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
  - Show runtime viewport metadata in the viewport summary.
- Modify `tests/Rekall.Age.Tests/Rendering/ViewportContractTests.cs`
  - Cover runtime frame building.
- Modify `tests/Rekall.Age.Tests/Rendering/CaptureScreenshotCommandTests.cs`
  - Cover runtime viewport capture and legacy screenshot compatibility.
- Add `tests/Rekall.Age.Tests/Cli/RuntimeViewportCliTests.cs`
  - Cover CLI route output.
- Modify `tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs`
  - Cover MCP visibility.
- Modify `tests/Rekall.Age.Tests/Editor/WorkbenchReadModelTests.cs`
  - Cover Studio read-model viewport metadata.
- Modify `README.md`
  - Document runtime viewport capture.

## Task 1: Runtime Viewport Frame Contracts

**Files:**
- Modify: `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
- Modify: `src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj`
- Create: `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/ViewportContractTests.cs`

- [ ] **Step 1: Write the failing frame-builder test**

Add a test named `RuntimeFrameBuilderUsesRuntimeRenderProjection`:

```csharp
var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
    .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
        .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
    .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
        .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 4, ["y"] = 8 }))
        .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" })))
    .AddEntity(RekallAgeEntityDocument.Create("Light", ["lighting"])
        .AddComponent(RekallAgeComponentDocument.Create("Rekall.PointLight", new JsonObject { ["intensity"] = 1 })));
var world = new RekallAgeRuntimeWorldBuilder().Build(scene) with { FrameIndex = 2, ElapsedTime = TimeSpan.FromSeconds(2.0 / 60.0) };

var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: true);

Assert.Equal("Main", frame.SceneName);
Assert.Equal(2, frame.FrameIndex);
Assert.Equal(320, frame.Width);
Assert.Equal(180, frame.Height);
Assert.Equal("Camera", frame.ActiveCamera?.EntityName);
Assert.Contains(frame.Renderables, item => item.Kind == "sprite" && item.AssetId == "asset_player");
Assert.Contains(frame.Renderables, item => item.Kind == "light" && item.EntityName == "Light");
Assert.True(frame.DebugOverlay.Enabled);
```

- [ ] **Step 2: Run the focused test and confirm it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter RuntimeFrameBuilderUsesRuntimeRenderProjection -p:UseSharedCompilation=false`

Expected: compile failure for `RekallAgeRuntimeRenderFrameBuilder` and frame record types.

- [ ] **Step 3: Implement contracts and frame builder**

Add records:

```csharp
public sealed record RekallAgeRuntimeViewportFrame(...);
public sealed record RekallAgeRuntimeViewportCamera(...);
public sealed record RekallAgeRuntimeViewportRenderable(...);
public sealed record RekallAgeRuntimeViewportOverlay(bool Enabled, int ObservationCount);
public sealed record RekallAgeRuntimeViewportCapture(...);
```

The builder should map runtime cameras, sprites, meshes, lights, and UI layers into stable renderables ordered by `SortKey`, select the first active camera or first camera, copy observations, and reject non-positive dimensions with `ArgumentOutOfRangeException`.

- [ ] **Step 4: Run the focused test and confirm it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter RuntimeFrameBuilderUsesRuntimeRenderProjection -p:UseSharedCompilation=false`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs tests/Rekall.Age.Tests/Rendering/ViewportContractTests.cs
git commit -m "feat: add runtime viewport frame builder"
```

## Task 2: Runtime Software Viewport Capture

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeRuntimeSoftwareRenderer.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeSoftwarePreview.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/CaptureScreenshotCommandTests.cs`

- [ ] **Step 1: Write failing renderer and legacy compatibility tests**

Add `RuntimeSoftwareRendererWritesNonBlankFrame`:

```csharp
var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: true);
var capture = await new RekallAgeRuntimeSoftwareRenderer()
    .CaptureAsync(frame, Path.Combine(root, "Viewport"), "Main_runtime_002.png", CancellationToken.None);
Assert.True(capture.NonBlank);
Assert.Equal(320, capture.Width);
Assert.Equal(180, capture.Height);
Assert.Equal(2, capture.FrameIndex);
Assert.Equal("Camera", capture.ActiveCamera);
Assert.True(File.Exists(capture.ScreenshotPath));
```

Extend the existing legacy screenshot test to assert:

```csharp
Assert.Contains("Main_preview.png", result.Value.ScreenshotPath);
Assert.True(result.Value.VisibleRenderers >= 1);
Assert.NotNull(result.Value.ActiveCamera);
```

- [ ] **Step 2: Run focused tests and confirm they fail**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "RuntimeSoftwareRendererWritesNonBlankFrame|CaptureScreenshotCommandWritesPngAndReturnsStructuredResult" -p:UseSharedCompilation=false`

Expected: missing renderer type, then legacy capture still passes until delegation is implemented.

- [ ] **Step 3: Implement software renderer and legacy delegation**

`RekallAgeRuntimeSoftwareRenderer.CaptureAsync` should create the output directory, fill a deterministic RGBA background from scene name and frame index, draw one marker per renderable kind, draw a top debug band when enabled, write through `RekallAgePngWriter`, and return `RekallAgeRuntimeViewportCapture`.

Update `RekallAgeSoftwarePreview.CaptureAsync` to load a runtime world through `RekallAgeRuntimeSnapshotService`, build a 160x90 frame, capture `Scene_preview.png`, and translate to `RekallAgeScreenshotResult`.

- [ ] **Step 4: Run focused tests and confirm they pass**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "RuntimeSoftwareRendererWritesNonBlankFrame|CaptureScreenshotCommandWritesPngAndReturnsStructuredResult" -p:UseSharedCompilation=false`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Rendering/RekallAgeRuntimeSoftwareRenderer.cs src/Rekall.Age.Rendering/RekallAgeSoftwarePreview.cs tests/Rekall.Age.Tests/Rendering/CaptureScreenshotCommandTests.cs
git commit -m "feat: add runtime software viewport capture"
```

## Task 3: Runtime Viewport Capture Command

**Files:**
- Create: `src/Rekall.Age.Rendering/Commands/CaptureRuntimeViewportCommand.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/CaptureScreenshotCommandTests.cs`

- [ ] **Step 1: Write failing command test**

Add `CaptureRuntimeViewportCommandWritesFrameMetadata`:

```csharp
var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
    new CaptureRuntimeViewportRequest(root, "Main", 3, Path.Combine(root, "Viewport"), 320, 180, true),
    context);

Assert.True(result.Ok);
Assert.True(result.Value.Captured);
Assert.True(result.Value.NonBlank);
Assert.Equal(3, result.Value.FrameIndex);
Assert.Equal(320, result.Value.Width);
Assert.Equal(180, result.Value.Height);
Assert.Equal("MainCamera", result.Value.ActiveCamera);
Assert.True(result.Value.RenderableCount >= 1);
Assert.Contains(result.Value.ScreenshotPath, context.Transaction.ChangedResources);
```

- [ ] **Step 2: Run command test and confirm it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter CaptureRuntimeViewportCommandWritesFrameMetadata -p:UseSharedCompilation=false`

Expected: missing command types.

- [ ] **Step 3: Implement command**

`CaptureRuntimeViewportRequest` should contain `ProjectRoot`, `SceneName`, `Frames`, `OutputDirectory`, `Width`, `Height`, and `DebugOverlay`. Validate frames are non-negative and dimensions are positive. Execute runtime snapshot service, frame builder, and software renderer. Name files as `{SceneName}_runtime_{Frames:000}.png`. Record the screenshot path in the transaction.

- [ ] **Step 4: Run command test and confirm it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter CaptureRuntimeViewportCommandWritesFrameMetadata -p:UseSharedCompilation=false`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Rendering/Commands/CaptureRuntimeViewportCommand.cs tests/Rekall.Age.Tests/Rendering/CaptureScreenshotCommandTests.cs
git commit -m "feat: add runtime viewport capture command"
```

## Task 4: CLI And MCP Exposure

**Files:**
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Add: `tests/Rekall.Age.Tests/Cli/RuntimeViewportCliTests.cs`
- Modify: `tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs`

- [ ] **Step 1: Write failing CLI and MCP tests**

CLI test should run:

```csharp
var result = await RunAsync(FindCliAssemblyPath(), "render", "viewport", "capture", root, "Main", "2", Path.Combine(root, "Viewport"));
Assert.Equal(0, result.ExitCode);
Assert.Contains("Runtime viewport Main frame 2", result.Output);
Assert.Contains("Renderable:", result.Output);
Assert.Contains("Main_runtime_002.png", result.Output);
```

MCP test addition:

```csharp
registry.Register(new CaptureRuntimeViewportCommand());
Assert.Contains("rekall.render.capture_runtime_viewport", names);
```

- [ ] **Step 2: Run focused tests and confirm they fail**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "RuntimeViewportCapturePrintsMetadata|CatalogExposesWorkbenchWorkflowCommands" -p:UseSharedCompilation=false`

Expected: CLI route missing.

- [ ] **Step 3: Add CLI route and registration**

Register `new CaptureRuntimeViewportCommand()`, add switch route:

```csharp
["render", "viewport", "capture", var root, var scene, var frames, var outputDirectory] =>
    await CaptureRuntimeViewportAsync(registry, context, root, scene, frames, outputDirectory)
```

Print summary, output path, dimensions, frame index, active camera, renderable count, and observation count.

- [ ] **Step 4: Run focused tests and confirm they pass**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "RuntimeViewportCapturePrintsMetadata|CatalogExposesWorkbenchWorkflowCommands" -p:UseSharedCompilation=false`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Cli/Program.cs tests/Rekall.Age.Tests/Cli/RuntimeViewportCliTests.cs tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs
git commit -m "feat: expose runtime viewport capture through cli and mcp"
```

## Task 5: Studio Runtime Viewport Metadata

**Files:**
- Modify: `src/Rekall.Age.Editor.Contracts/RekallAgeWorkbenchModel.cs`
- Modify: `src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `tests/Rekall.Age.Tests/Editor/WorkbenchReadModelTests.cs`

- [ ] **Step 1: Write failing editor assertions**

Add assertions:

```csharp
Assert.Equal("Camera", model.Runtime.ActiveCameraName);
Assert.Equal("rekall.render.capture_runtime_viewport", model.Runtime.ViewportCaptureTool);
Assert.Contains("frame 0", new RekallAgeStudioViewModel(model).ViewportSummary);
Assert.Contains("Camera", new RekallAgeStudioViewModel(model).ViewportSummary);
```

- [ ] **Step 2: Run editor test and confirm it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter WorkbenchModelUsesStableIdsAndInspectorProperties -p:UseSharedCompilation=false`

Expected: runtime panel lacks active camera and capture tool.

- [ ] **Step 3: Add runtime viewport metadata**

Extend `RekallAgeRuntimePanelModel` with `ActiveCameraName` and `ViewportCaptureTool`. Populate `ActiveCameraName` from runtime rendering cameras, and set `ViewportCaptureTool` to `rekall.render.capture_runtime_viewport`. Update Studio viewport summary to include scene, frame, camera, renderable count, and runtime observation count.

- [ ] **Step 4: Run editor test and confirm it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter WorkbenchModelUsesStableIdsAndInspectorProperties -p:UseSharedCompilation=false`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Editor.Contracts/RekallAgeWorkbenchModel.cs src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs tests/Rekall.Age.Tests/Editor/WorkbenchReadModelTests.cs
git commit -m "feat: surface runtime viewport metadata in studio"
```

## Task 6: Documentation And Full Verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update README**

Add this CLI example:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport
```

Add a short section explaining that runtime viewport captures are derived from runtime snapshots and preserve authoring files.

- [ ] **Step 2: Run marker scan**

Run: `$env:REKALL_SCAN_PATTERN='TO' + 'DO|TB' + 'D|place' + 'holder|lor' + 'em|FIX' + 'ME'; rg -n $env:REKALL_SCAN_PATTERN docs/superpowers/plans/2026-05-25-rekall-age-runtime-viewport-rendering.md docs/superpowers/specs/2026-05-25-rekall-age-runtime-viewport-rendering-design.md README.md`

Expected: no matches.

- [ ] **Step 3: Run build**

Run: `dotnet build Rekall.AGE.sln -p:UseSharedCompilation=false`

Expected: build succeeds with 0 errors.

- [ ] **Step 4: Run full tests**

Run: `dotnet test Rekall.AGE.sln -p:UseSharedCompilation=false`

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "docs: document runtime viewport capture"
```

## Self-Review

- Spec coverage: tasks cover frame contracts, frame building, runtime software rendering, legacy screenshot compatibility, command bus, CLI, MCP, Studio metadata, docs, build, and tests.
- Marker scan target: the plan avoids open-ended markers except inside the split scan command.
- Type consistency: `RekallAgeRuntimeViewportFrame`, `RekallAgeRuntimeViewportCapture`, `RekallAgeRuntimeRenderFrameBuilder`, `RekallAgeRuntimeSoftwareRenderer`, `CaptureRuntimeViewportRequest`, and `CaptureRuntimeViewportResult` are used consistently.
- Scope check: no real texture sampling, mesh rasterization, GPU swapchain, WPF image stream, camera matrix system, or asset decoding is included.
