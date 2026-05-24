using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeAndCaptureTests
{
    [Fact]
    public async Task HeadlessRuntimeRunsAndSoftwarePreviewWritesPng()
    {
        var root = TestPaths.CreateTempDirectory();
        var sceneStore = new RekallAgeSceneStore();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })));
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var runtime = new RekallAgeHeadlessRuntime(sceneStore);
        var result = await runtime.RunAsync(root, "Main", TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.True(result.FramesSimulated > 0);

        var preview = new RekallAgeSoftwarePreview(sceneStore);
        var capture = await preview.CaptureAsync(root, "Main", Path.Combine(root, "Artifacts", "Screenshots"), CancellationToken.None);

        Assert.True(capture.NonBlank);
        Assert.True(File.Exists(capture.ScreenshotPath));
        Assert.True(new FileInfo(capture.ScreenshotPath).Length > 64);
    }
}
