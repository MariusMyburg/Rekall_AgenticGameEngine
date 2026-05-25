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

    [Fact]
    public async Task SoftwareRendererRasterizesPrimitiveCubeWithDirectionalLighting()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["yaw"] = 32, ["pitch"] = -8, ["scaleX"] = 2, ["scaleY"] = 2, ["scaleZ"] = 2 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshRenderer",
                    new JsonObject { ["mesh"] = "rekall.primitive.cube" })))
            .AddEntity(RekallAgeEntityDocument.Create("KeyLight", ["light"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["pitch"] = -35, ["yaw"] = -35 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.DirectionalLight",
                    new JsonObject { ["intensity"] = 1.0 })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 180, 120, debugOverlay: false);

        var capture = await new RekallAgeRuntimeSoftwareRenderer().CaptureAsync(
            frame,
            Path.Combine(root, "captures"),
            "Main_runtime_000.png",
            RekallAgeRuntimeViewportAssetSet.Empty,
            CancellationToken.None);
        var output = await RekallAgePngReader.ReadRgbaAsync(capture.ScreenshotPath, CancellationToken.None);
        var shadedCubePixels = Enumerable.Range(0, output.Rgba.Length / 4)
            .Select(pixel => pixel * 4)
            .Where(index =>
                output.Rgba[index] >= 45
                && output.Rgba[index] <= 190
                && output.Rgba[index + 1] >= 70
                && output.Rgba[index + 1] <= 210
                && output.Rgba[index + 2] >= 100)
            .Select(index => (R: output.Rgba[index], G: output.Rgba[index + 1], B: output.Rgba[index + 2]))
            .Distinct()
            .ToArray();

        Assert.True(capture.NonBlank);
        Assert.Equal(0, capture.FallbackRenderableCount);
        Assert.Contains("mesh", frame.Renderables.Select(renderable => renderable.Kind));
        Assert.Contains("light", frame.Renderables.Select(renderable => renderable.Kind));
        Assert.True(shadedCubePixels.Length >= 3);
    }
}
