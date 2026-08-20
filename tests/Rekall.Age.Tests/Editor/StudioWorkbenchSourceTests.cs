namespace Rekall.Age.Tests.Editor;

public sealed class StudioWorkbenchSourceTests
{
    [Fact]
    public async Task StudioWorkspaceWiresCanonicalGameCreationCommandsAndRenderedViewport()
    {
        var root = FindRepositoryRoot();
        var xaml = await File.ReadAllTextAsync(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));
        var code = await File.ReadAllTextAsync(Path.Combine(root, "src", "Rekall.Age.Studio", "RekallAgeStudioViewModel.cs"));

        Assert.Contains("Command=\"{Binding OpenCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CreateCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddEntityCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddComponentCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RemoveComponentCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SetPropertyCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RemovePropertyCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ValidateCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CaptureCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding PlayCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding StopCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DiscoverModelsCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RunAgentCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CancelAgentCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding OllamaModels}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AgentTaskInput", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding AgentLines}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItemChanged=\"OnSelectedEntityChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"{Binding ViewportImage}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ViewportSummary}\" FontSize=\"14\" VerticalAlignment=\"Center\"", xaml, StringComparison.Ordinal);

        Assert.Contains("RekallAgeDefaultCommandRegistry.Create()", code, StringComparison.Ordinal);
        Assert.Contains("new RekallAgeWorkbenchSession", code, StringComparison.Ordinal);
        Assert.Contains("rekall.render.capture_runtime_viewport", code, StringComparison.Ordinal);
        Assert.Contains("rekall.validation.scene", code, StringComparison.Ordinal);
        Assert.Contains("rekall.entity.create", code, StringComparison.Ordinal);
        Assert.Contains("rekall.component.add", code, StringComparison.Ordinal);
        Assert.Contains("rekall.component.remove", code, StringComparison.Ordinal);
        Assert.Contains("rekall.component.set_property", code, StringComparison.Ordinal);
        Assert.Contains("rekall.component.remove_property", code, StringComparison.Ordinal);
        Assert.Contains("JsonNode.Parse", code, StringComparison.Ordinal);
        Assert.Contains("RekallAgeProjectAgentSession", code, StringComparison.Ordinal);
        Assert.Contains("RekallAgeOllamaLanguageModelClient", code, StringComparison.Ordinal);
        Assert.Contains("qwen3.5:35b", code, StringComparison.Ordinal);
        Assert.Contains("_session.ReloadAsync", code, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Rekall AGE repository root.");
    }
}
