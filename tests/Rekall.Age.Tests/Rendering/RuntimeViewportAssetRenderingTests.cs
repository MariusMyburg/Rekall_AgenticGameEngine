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
