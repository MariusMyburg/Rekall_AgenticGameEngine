using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class CaptureScreenshotCommandTests
{
    [Fact]
    public async Task CaptureScreenshotCommandWritesPngAndReturnsStructuredResult()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("capture"), CancellationToken.None);
        await new CreateGameFromTemplateCommand()
            .ExecuteAsync(new CreateGameFromTemplateRequest(root, "Puzzle Capture", "puzzle"), context);
        var command = new CaptureScreenshotCommand();

        var result = await command.ExecuteAsync(
            new CaptureScreenshotRequest(root, "Main", Path.Combine(root, "Shots")),
            context);

        Assert.True(result.Ok);
        Assert.True(result.Value.NonBlank);
        Assert.True(File.Exists(result.Value.ScreenshotPath));
        Assert.Contains("Main_preview.png", result.Value.ScreenshotPath);
    }
}
