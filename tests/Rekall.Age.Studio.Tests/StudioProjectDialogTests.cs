using System.IO;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioProjectDialogTests
{
    [Fact]
    public async Task EmptyStudioViewportInvitesProjectSelectionInsteadOfReportingVulkanFailure()
    {
        await using var viewModel = new RekallAgeStudioViewModel();

        Assert.False(viewModel.HasProject);
        Assert.Contains("Open or create", viewModel.ViewportSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unavailable", viewModel.ViewportBackendLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unavailable", viewModel.ViewportUnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectContextExplainsTheEmptyStateAndShowsTheSelectedFolder()
    {
        await using var viewModel = new RekallAgeStudioViewModel();

        Assert.Equal("No project open", viewModel.ProjectContextText);

        viewModel.ProjectPathInput = Path.Combine("C:\\", "Games", "Neon Orchard");

        Assert.Equal("Neon Orchard", viewModel.ProjectContextText);
    }

    [Fact]
    public void CreateProjectRequestResolvesAProjectFolderAndRejectsMissingRequiredFields()
    {
        var parent = Path.Combine(Path.GetTempPath(), "rekall-studio-projects");

        var valid = RekallAgeCreateProjectRequest.TryCreate(
            parent,
            "Neon Orchard",
            "Main",
            out var request,
            out var error);

        Assert.True(valid, error);
        Assert.NotNull(request);
        Assert.Equal(Path.Combine(parent, "Neon Orchard"), request.ProjectRoot);
        Assert.Equal("Neon Orchard", request.ProjectName);
        Assert.Equal("Main", request.SceneName);

        Assert.False(RekallAgeCreateProjectRequest.TryCreate(parent, " ", "Main", out _, out var nameError));
        Assert.Contains("name", nameError, StringComparison.OrdinalIgnoreCase);
        Assert.False(RekallAgeCreateProjectRequest.TryCreate(" ", "Game", "Main", out _, out var folderError));
        Assert.Contains("folder", folderError, StringComparison.OrdinalIgnoreCase);
        Assert.False(RekallAgeCreateProjectRequest.TryCreate(parent, "Game", " ", out _, out var sceneError));
        Assert.Contains("scene", sceneError, StringComparison.OrdinalIgnoreCase);
        Assert.False(RekallAgeCreateProjectRequest.TryCreate(parent, ".", "Main", out _, out _));
        Assert.False(RekallAgeCreateProjectRequest.TryCreate(parent, "..", "Main", out _, out _));
        Assert.False(RekallAgeCreateProjectRequest.TryCreate(parent, "Game", "Act/One", out _, out _));
    }
}
