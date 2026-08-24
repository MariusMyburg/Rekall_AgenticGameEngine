using System.IO;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

/// <summary>
/// Proves Studio exposes Publish Web / Audit Web as ordinary commands wired to the same generic
/// <c>rekall.game.publish_web</c>/<c>rekall.game.audit_web</c> commands the CLI and MCP use (see
/// <see cref="Rekall.Age.Tests.Workflows.WebGamePublishingTests"/> for the underlying command-level, real,
/// end-to-end proof -- exercising the full multi-minute publish subprocess a second time here, through the WPF
/// view model, would only prove command availability at several times the cost).
/// </summary>
public sealed class StudioWebPublishingTests
{
    [Fact]
    public async Task PublishWebAndAuditWebAreUnavailableUntilAProjectIsOpen()
    {
        await using var viewModel = new RekallAgeStudioViewModel();

        Assert.False(viewModel.PublishWebCommand.CanExecute(null));
        Assert.False(viewModel.AuditWebCommand.CanExecute(null));
    }

    [Fact]
    public async Task PublishWebAndAuditWebBecomeAvailableOnceAProjectIsOpen()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-webpublish-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel
            {
                ProjectPathInput = root,
                ProjectNameInput = "Web Publishing Test",
                SceneNameInput = "Main"
            };

            await ((RekallAgeAsyncCommand)viewModel.CreateCommand).ExecuteAsync(null);

            Assert.True(viewModel.PublishWebCommand.CanExecute(null));
            // rekall.game.audit_web republishes the project itself before verifying it (the same self-contained
            // shape as AuditPlayablePackageCommand), so it does not require a prior successful Publish Web click.
            Assert.True(viewModel.AuditWebCommand.CanExecute(null));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
