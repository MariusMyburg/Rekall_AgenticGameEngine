# Asset-Backed Viewport Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render imported PNG sprite assets in deterministic runtime viewport captures instead of drawing sprite markers only.

**Architecture:** Keep runtime viewport frames as the renderer input. Add a renderer-local RGBA image type and PNG reader, pass decoded sprite assets into `RekallAgeRuntimeSoftwareRenderer`, and let `CaptureRuntimeViewportCommand` resolve project asset IDs through `RekallAgeAssetCatalogStore`. Preserve legacy screenshot command behavior while exposing richer asset-backed metadata through the runtime viewport capture command and CLI.

**Tech Stack:** C# 13, .NET 10, xUnit, existing Rekall command bus, existing asset catalog, existing runtime viewport frame builder, existing PNG writer.

---

## File Structure

- Create `src/Rekall.Age.Rendering/RekallAgeRgbaImage.cs`
  - Immutable RGBA image record with pixel access helpers.
- Create `src/Rekall.Age.Rendering/RekallAgePngReader.cs`
  - Decode non-interlaced 8-bit RGB/RGBA PNGs, including PNG filters 0-4.
- Create `src/Rekall.Age.Rendering/RekallAgeRuntimeViewportAssetSet.cs`
  - Store decoded assets and issue records keyed by asset ID.
- Modify `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
  - Add asset-backed and fallback counts to `RekallAgeRuntimeViewportCapture`.
- Modify `src/Rekall.Age.Rendering/RekallAgeRuntimeSoftwareRenderer.cs`
  - Add an overload that accepts an asset set and draws sprite pixels with nearest-neighbor sampling.
- Modify `src/Rekall.Age.Rendering/Commands/CaptureRuntimeViewportCommand.cs`
  - Load asset catalog, decode sprite PNG assets, pass assets to the renderer, and return asset-backed metadata.
- Modify `src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj`
  - Reference `Rekall.Age.Assets`.
- Modify `src/Rekall.Age.Cli/Program.cs`
  - Print asset-backed counts and issue codes for runtime viewport captures.
- Modify `src/Rekall.Age.Rendering/RekallAgeSoftwarePreview.cs`
  - Use the same asset resolver for legacy screenshots while keeping the legacy result shape.
- Tests:
  - `tests/Rekall.Age.Tests/Rendering/PngReaderTests.cs`
  - `tests/Rekall.Age.Tests/Rendering/RuntimeViewportAssetRenderingTests.cs`
  - Existing capture, CLI, and screenshot tests.
- Modify `README.md`
  - Document that runtime viewport capture resolves imported PNG sprite assets.

## Task 1: PNG Reader And RGBA Image

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeRgbaImage.cs`
- Create: `src/Rekall.Age.Rendering/RekallAgePngReader.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/PngReaderTests.cs`

- [ ] **Step 1: Write the failing PNG reader test**

Create `tests/Rekall.Age.Tests/Rendering/PngReaderTests.cs`:

```csharp
using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class PngReaderTests
{
    [Fact]
    public async Task PngReaderDecodesRgbaWrittenByPngWriter()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "sprite.png");
        byte[] rgba =
        [
            255, 0, 0, 255,
            0, 255, 0, 255,
            0, 0, 255, 255,
            255, 255, 0, 128
        ];
        await RekallAgePngWriter.WriteRgbaAsync(path, 2, 2, rgba, CancellationToken.None);

        var image = await RekallAgePngReader.ReadRgbaAsync(path, CancellationToken.None);

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(rgba, image.Rgba);
        Assert.Equal((byte)255, image.GetPixel(0, 0).R);
        Assert.Equal((byte)128, image.GetPixel(1, 1).A);
    }
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter PngReaderDecodesRgbaWrittenByPngWriter -p:UseSharedCompilation=false
```

Expected: compile failure because `RekallAgePngReader` and `RekallAgeRgbaImage` do not exist.

- [ ] **Step 3: Add RGBA image and PNG reader**

Create `src/Rekall.Age.Rendering/RekallAgeRgbaImage.cs`:

```csharp
namespace Rekall.Age.Rendering;

public sealed record RekallAgeRgbaImage(int Width, int Height, byte[] Rgba)
{
    public RekallAgeRgbaPixel GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException($"Pixel coordinate {x},{y} is outside {Width}x{Height}.");
        }

        var index = (y * Width + x) * 4;
        return new RekallAgeRgbaPixel(Rgba[index], Rgba[index + 1], Rgba[index + 2], Rgba[index + 3]);
    }
}

public readonly record struct RekallAgeRgbaPixel(byte R, byte G, byte B, byte A);
```

Create `src/Rekall.Age.Rendering/RekallAgePngReader.cs` with:

- PNG signature validation
- IHDR parsing for width, height, bit depth, color type, compression, filter, and interlace
- `IDAT` chunk concatenation
- `ZLibStream` decompression
- scanline reconstruction for filters 0, 1, 2, 3, and 4
- output conversion from RGB or RGBA to `RekallAgeRgbaImage`
- `InvalidDataException` for unsupported bit depth, color type, interlace, malformed chunks, or invalid scanline length

Use these method signatures:

```csharp
public static class RekallAgePngReader
{
    public static ValueTask<RekallAgeRgbaImage> ReadRgbaAsync(
        string path,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Run the focused test and verify it passes**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter PngReaderDecodesRgbaWrittenByPngWriter -p:UseSharedCompilation=false
```

Expected: 1 passed.

- [ ] **Step 5: Commit**

```powershell
git add src\Rekall.Age.Rendering\RekallAgeRgbaImage.cs src\Rekall.Age.Rendering\RekallAgePngReader.cs tests\Rekall.Age.Tests\Rendering\PngReaderTests.cs
git commit -m "feat: add png reader for viewport assets"
```

## Task 2: Software Renderer Asset Drawing

**Files:**
- Create: `src/Rekall.Age.Rendering/RekallAgeRuntimeViewportAssetSet.cs`
- Modify: `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeRuntimeSoftwareRenderer.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/RuntimeViewportAssetRenderingTests.cs`

- [ ] **Step 1: Write the failing renderer test**

Create `tests/Rekall.Age.Tests/Rendering/RuntimeViewportAssetRenderingTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class RuntimeViewportAssetRenderingTests
{
    [Fact]
    public async Task SoftwareRendererDrawsDecodedSpriteAssetPixels()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 4, ["y"] = 8 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 160, 90, debugOverlay: false);
        var asset = new RekallAgeRgbaImage(
            2,
            2,
            [
                250, 10, 20, 255,
                250, 10, 20, 255,
                250, 10, 20, 255,
                250, 10, 20, 255
            ]);

        var capture = await new RekallAgeRuntimeSoftwareRenderer().CaptureAsync(
            frame,
            Path.Combine(root, "captures"),
            "Main_runtime_000.png",
            new RekallAgeRuntimeViewportAssetSet(
                new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal)
                {
                    ["asset_player"] = asset
                },
                Array.Empty<RekallAgeRuntimeViewportAssetIssue>()),
            CancellationToken.None);
        var output = await RekallAgePngReader.ReadRgbaAsync(capture.ScreenshotPath, CancellationToken.None);

        Assert.True(capture.NonBlank);
        Assert.Equal(1, capture.AssetBackedRenderableCount);
        Assert.Equal(0, capture.FallbackRenderableCount);
        Assert.Contains(Enumerable.Range(0, output.Rgba.Length / 4), pixel =>
        {
            var index = pixel * 4;
            return output.Rgba[index] == 250 && output.Rgba[index + 1] == 10 && output.Rgba[index + 2] == 20;
        });
    }
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter SoftwareRendererDrawsDecodedSpriteAssetPixels -p:UseSharedCompilation=false
```

Expected: compile failure because the asset-set types and capture count fields do not exist.

- [ ] **Step 3: Add asset-set contracts and capture count fields**

Create `src/Rekall.Age.Rendering/RekallAgeRuntimeViewportAssetSet.cs`:

```csharp
namespace Rekall.Age.Rendering;

public sealed record RekallAgeRuntimeViewportAssetSet(
    IReadOnlyDictionary<string, RekallAgeRgbaImage> Images,
    IReadOnlyList<RekallAgeRuntimeViewportAssetIssue> Issues)
{
    public static RekallAgeRuntimeViewportAssetSet Empty { get; } = new(
        new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal),
        Array.Empty<RekallAgeRuntimeViewportAssetIssue>());
}

public sealed record RekallAgeRuntimeViewportAssetIssue(
    string AssetId,
    string Code,
    string Message);
```

Modify `RekallAgeRuntimeViewportCapture` to append:

```csharp
int AssetBackedRenderableCount,
int FallbackRenderableCount,
int MissingAssetCount,
int UnsupportedAssetCount,
IReadOnlyList<string> AssetIssueCodes
```

Update existing capture construction sites with zeros and empty arrays before implementing asset drawing.

- [ ] **Step 4: Draw sprite images in the software renderer**

Modify `RekallAgeRuntimeSoftwareRenderer`:

- keep the existing `CaptureAsync(frame, outputDirectory, fileName, cancellationToken)` overload and delegate to the new overload with `RekallAgeRuntimeViewportAssetSet.Empty`
- add a new overload accepting `RekallAgeRuntimeViewportAssetSet assets`
- for sprite renderables with `AssetId` present in `assets.Images`, draw the decoded image instead of marker
- alpha-blend each source pixel over the destination
- scale sprites to at least 16 pixels on their longest axis and at most 64 pixels on their longest axis
- count asset-backed and fallback renderables
- include asset issue codes from `assets.Issues`

Use deterministic anchor logic based on the existing marker placement.

- [ ] **Step 5: Run the focused test and existing renderer tests**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "SoftwareRendererDrawsDecodedSpriteAssetPixels|RuntimeSoftwareRendererWritesNonBlankFrame|CaptureRuntimeViewportCommandWritesFrameFromRuntimeSnapshot" -p:UseSharedCompilation=false
```

Expected: all selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src\Rekall.Age.Rendering.Abstractions\RekallAgeRenderWorldContracts.cs src\Rekall.Age.Rendering\RekallAgeRuntimeViewportAssetSet.cs src\Rekall.Age.Rendering\RekallAgeRuntimeSoftwareRenderer.cs tests\Rekall.Age.Tests\Rendering\RuntimeViewportAssetRenderingTests.cs
git commit -m "feat: draw viewport sprite assets"
```

## Task 3: Command Asset Resolution

**Files:**
- Modify: `src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj`
- Modify: `src/Rekall.Age.Rendering/Commands/CaptureRuntimeViewportCommand.cs`
- Modify: `src/Rekall.Age.Rendering/RekallAgeSoftwarePreview.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/CaptureRuntimeViewportCommandTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/CaptureScreenshotCommandTests.cs`

- [ ] **Step 1: Write the failing command test**

Add to `CaptureRuntimeViewportCommandTests`:

```csharp
[Fact]
public async Task CaptureRuntimeViewportCommandResolvesImportedSpritePng()
{
    var root = TestPaths.CreateTempDirectory();
    var source = Path.Combine(root, "player.png");
    await RekallAgePngWriter.WriteRgbaAsync(
        source,
        2,
        2,
        [
            40, 220, 90, 255,
            40, 220, 90, 255,
            40, 220, 90, 255,
            40, 220, 90, 255
        ],
        CancellationToken.None);
    var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("asset viewport"), CancellationToken.None);
    var import = await new ImportAssetCommand().ExecuteAsync(
        new ImportAssetRequest(root, source, "sprite", "Player"),
        context);
    var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
        .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
        .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 1, ["y"] = 2 }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = import.Value.Asset.Id })));
    await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

    var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
        new CaptureRuntimeViewportRequest(root, "Main", 1, Path.Combine(root, "Viewport"), 160, 90, false),
        context);
    var output = await RekallAgePngReader.ReadRgbaAsync(result.Value.ScreenshotPath, CancellationToken.None);

    Assert.True(result.Ok, result.Summary);
    Assert.Equal(1, result.Value.AssetBackedRenderableCount);
    Assert.Equal(0, result.Value.FallbackRenderableCount);
    Assert.Contains(Enumerable.Range(0, output.Rgba.Length / 4), pixel =>
    {
        var index = pixel * 4;
        return output.Rgba[index] == 40 && output.Rgba[index + 1] == 220 && output.Rgba[index + 2] == 90;
    });
}
```

Add required usings:

```csharp
using Rekall.Age.Assets.Commands;
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter CaptureRuntimeViewportCommandResolvesImportedSpritePng -p:UseSharedCompilation=false
```

Expected: compile failure because command result asset count fields are not present.

- [ ] **Step 3: Add command result fields and asset catalog resolution**

Modify `CaptureRuntimeViewportResult` to append:

```csharp
int AssetBackedRenderableCount,
int FallbackRenderableCount,
int MissingAssetCount,
int UnsupportedAssetCount,
IReadOnlyList<string> AssetIssueCodes
```

Add a project reference from Rendering to Assets:

```xml
<ProjectReference Include="..\Rekall.Age.Assets\Rekall.Age.Assets.csproj" />
```

Inside `CaptureRuntimeViewportCommand`, load `RekallAgeAssetCatalogStore`, select catalog assets whose `Id` is referenced by sprite renderables, decode PNGs with `RekallAgePngReader`, and create `RekallAgeRuntimeViewportAssetSet`.

For missing IDs, create:

```csharp
new RekallAgeRuntimeViewportAssetIssue(assetId, "REKALL_RENDER_ASSET_MISSING", "Sprite asset was not found in the project catalog.")
```

For decode failures, create:

```csharp
new RekallAgeRuntimeViewportAssetIssue(asset.Id, "REKALL_RENDER_ASSET_UNSUPPORTED", ex.Message)
```

Pass the asset set to `RekallAgeRuntimeSoftwareRenderer.CaptureAsync`.

- [ ] **Step 4: Update legacy screenshot delegation**

Modify `RekallAgeSoftwarePreview.CaptureAsync` to load the asset set from `projectRoot` and pass it to the renderer. Keep `RekallAgeScreenshotResult` unchanged.

- [ ] **Step 5: Run command and screenshot tests**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "CaptureRuntimeViewportCommandResolvesImportedSpritePng|CaptureRuntimeViewportCommandWritesFrameFromRuntimeSnapshot|CaptureScreenshotCommandWritesPngAndReturnsStructuredResult" -p:UseSharedCompilation=false
```

Expected: all selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src\Rekall.Age.Rendering\Rekall.Age.Rendering.csproj src\Rekall.Age.Rendering\Commands\CaptureRuntimeViewportCommand.cs src\Rekall.Age.Rendering\RekallAgeSoftwarePreview.cs tests\Rekall.Age.Tests\Rendering\CaptureRuntimeViewportCommandTests.cs tests\Rekall.Age.Tests\Rendering\CaptureScreenshotCommandTests.cs
git commit -m "feat: resolve viewport sprite assets"
```

## Task 4: CLI Output And Fallback Reporting

**Files:**
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Test: `tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/CaptureRuntimeViewportCommandTests.cs`

- [ ] **Step 1: Write fallback command test**

Add to `CaptureRuntimeViewportCommandTests`:

```csharp
[Fact]
public async Task CaptureRuntimeViewportCommandReportsMissingSpriteAssetsAsFallbacks()
{
    var root = TestPaths.CreateTempDirectory();
    var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
        .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
        .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_missing" })));
    await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

    var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
        new CaptureRuntimeViewportRequest(root, "Main", 0, Path.Combine(root, "Viewport"), 160, 90, false),
        new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("missing asset viewport"), CancellationToken.None));

    Assert.True(result.Ok, result.Summary);
    Assert.True(result.Value.NonBlank);
    Assert.Equal(0, result.Value.AssetBackedRenderableCount);
    Assert.Equal(1, result.Value.FallbackRenderableCount);
    Assert.Equal(1, result.Value.MissingAssetCount);
    Assert.Contains("REKALL_RENDER_ASSET_MISSING", result.Value.AssetIssueCodes);
}
```

- [ ] **Step 2: Extend CLI test assertions**

In `RuntimeViewportCapturePrintsCaptureSummary`, add:

```csharp
Assert.Contains("Asset-backed: 0", result.Output);
Assert.Contains("Fallback: 1", result.Output);
```

The existing CLI fixture uses a sprite ID without importing an asset, so this should report a fallback.

- [ ] **Step 3: Run focused tests and verify the CLI assertion fails before CLI output is updated**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "CaptureRuntimeViewportCommandReportsMissingSpriteAssetsAsFallbacks|RuntimeViewportCapturePrintsCaptureSummary" -p:UseSharedCompilation=false
```

Expected: command fallback test passes after Task 3, CLI test fails until output is updated.

- [ ] **Step 4: Print asset counts in CLI**

In `CaptureRuntimeViewportAsync`, after renderable count output, print:

```csharp
Console.WriteLine($"Asset-backed: {result.Value.AssetBackedRenderableCount}");
Console.WriteLine($"Fallback: {result.Value.FallbackRenderableCount}");
Console.WriteLine($"Missing assets: {result.Value.MissingAssetCount}");
Console.WriteLine($"Unsupported assets: {result.Value.UnsupportedAssetCount}");
foreach (var code in result.Value.AssetIssueCodes)
{
    Console.WriteLine($"Asset issue: {code}");
}
```

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "CaptureRuntimeViewportCommandReportsMissingSpriteAssetsAsFallbacks|RuntimeViewportCapturePrintsCaptureSummary" -p:UseSharedCompilation=false
```

Expected: selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src\Rekall.Age.Cli\Program.cs tests\Rekall.Age.Tests\Cli\RuntimeInspectCliTests.cs tests\Rekall.Age.Tests\Rendering\CaptureRuntimeViewportCommandTests.cs
git commit -m "feat: report viewport asset fallbacks"
```

## Task 5: Documentation And Full Verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update README**

In the Runtime Viewport Capture section, add:

```markdown
If a sprite renderable references an imported PNG asset, the software viewport draws that PNG into the frame. Missing or unsupported sprite assets fall back to deterministic markers and are reported in the command output.
```

- [ ] **Step 2: Scan changed docs for incomplete markers**

Run:

```powershell
rg -n "T[O]DO|T[B]D|place[h]older|s[t]ub|not imple[m]ented" README.md docs\superpowers\specs\2026-05-25-rekall-age-asset-backed-viewport-rendering-design.md docs\superpowers\plans\2026-05-25-rekall-age-asset-backed-viewport-rendering.md
```

Expected: no output.

- [ ] **Step 3: Run full build**

Run:

```powershell
dotnet build-server shutdown; dotnet build Rekall.AGE.sln -p:UseSharedCompilation=false
```

Expected: build succeeds with 0 warnings and 0 errors.

- [ ] **Step 4: Run full tests**

Run:

```powershell
dotnet test Rekall.AGE.sln -p:UseSharedCompilation=false
```

Expected: all tests pass.

- [ ] **Step 5: Commit docs**

```powershell
git add README.md
git commit -m "docs: document asset-backed viewport rendering"
```

## Self-Review

- Spec coverage: tasks cover PNG decoding, software sprite drawing, command asset resolution, fallback reporting, CLI output, legacy compatibility, docs, build, and tests.
- Marker scan: plan avoids incomplete implementation markers.
- Type consistency: capture fields use `AssetBackedRenderableCount`, `FallbackRenderableCount`, `MissingAssetCount`, `UnsupportedAssetCount`, and `AssetIssueCodes` consistently in renderer, command result, CLI, and tests.
