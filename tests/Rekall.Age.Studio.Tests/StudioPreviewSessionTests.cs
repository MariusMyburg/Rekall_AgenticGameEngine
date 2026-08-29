using System.IO;
using System.Text.Json.Nodes;
using Rekall.Age.Studio;
using Rekall.Age.World;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioPreviewSessionTests
{
    [Fact]
    public async Task InitialEditPreviewIncludesAuthoredUiWithoutAdvancingGameplay()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-preview-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var canvas = RekallAgeEntityDocument.Create("HUD", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.UiCanvas",
                    new JsonObject { ["ReferenceWidth"] = 320, ["ReferenceHeight"] = 180 }));
            var label = RekallAgeEntityDocument.Create("Status", ["ui"]) with { ParentId = canvas.Id };
            label = label.AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Label",
                new JsonObject { ["Width"] = 120, ["Height"] = 24, ["Text"] = "READY" }));
            await new RekallAgeSceneStore().SaveAsync(
                root,
                RekallAgeSceneDocument.Create("Main", ["ui"]).AddEntity(canvas).AddEntity(label),
                CancellationToken.None);
            await using var preview = new RekallAgeStudioPreviewSession();

            var initial = await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);

            Assert.Equal(0, initial.FrameIndex);
            Assert.Equal(1, initial.RenderableCount);
            Assert.Equal(label.Id, initial.Interaction.Pick(10, 10));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewSessionPersistsRuntimeFramesUntilReset()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new RekallAgeSceneStore().SaveAsync(
                root,
                RekallAgeSceneDocument.Create("Main", ["world"]),
                CancellationToken.None);
            await using var preview = new RekallAgeStudioPreviewSession();

            var initial = await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);
            var first = await preview.StepAsync(1, CancellationToken.None);
            var seventh = await preview.StepAsync(6, CancellationToken.None);
            var reset = await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);

            Assert.Equal(0, initial.FrameIndex);
            Assert.Equal(1, first.FrameIndex);
            Assert.Equal(7, seventh.FrameIndex);
            Assert.Equal(0, reset.FrameIndex);
            Assert.Equal(320, seventh.Image.PixelWidth);
            Assert.Equal(180, seventh.Image.PixelHeight);
            Assert.True(seventh.Image.IsFrozen);
            Assert.Equal(320, seventh.Interaction.FrameWidth);
            Assert.Equal(180, seventh.Interaction.FrameHeight);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FailedResetPreservesThePreviousCoherentPreviewSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-preview-reset-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new RekallAgeSceneStore().SaveAsync(
                root,
                RekallAgeSceneDocument.Create("Main", ["world"]),
                CancellationToken.None);
            await using var preview = new RekallAgeStudioPreviewSession();
            await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                preview.ResetAsync(root, "Missing", 640, 360, CancellationToken.None).AsTask());
            var advanced = await preview.StepAsync(1, CancellationToken.None);

            Assert.Equal(1, advanced.FrameIndex);
            Assert.Equal(320, advanced.Image.PixelWidth);
            Assert.Equal(180, advanced.Image.PixelHeight);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RenderFailureAfterInspectionDoesNotReplaceThePreviousSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-preview-render-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new RekallAgeSceneStore().SaveAsync(
                root,
                RekallAgeSceneDocument.Create("Main", ["world"]),
                CancellationToken.None);
            await using var preview = new RekallAgeStudioPreviewSession(
                (world, _, width, height, _) => width == 640
                    ? ValueTask.FromException<RekallAgeStudioPreviewFrame>(new InvalidOperationException("simulated render failure"))
                    : ValueTask.FromResult(CreateFrame(world.FrameIndex, width, height)));
            await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                preview.ResetAsync(root, "Main", 640, 360, CancellationToken.None).AsTask());
            var advanced = await preview.StepAsync(1, CancellationToken.None);

            Assert.Equal(1, advanced.FrameIndex);
            Assert.Equal(320, advanced.Image.PixelWidth);
            Assert.Equal(180, advanced.Image.PixelHeight);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static RekallAgeStudioPreviewFrame CreateFrame(int frameIndex, int width, int height)
    {
        var image = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[checked(width * height * 4)],
            checked(width * 4));
        image.Freeze();
        return new RekallAgeStudioPreviewFrame(
            image, frameIndex, 0, 0, "test",
            new RekallAgeStudioViewportInteractionSnapshot(width, height, []));
    }
}
