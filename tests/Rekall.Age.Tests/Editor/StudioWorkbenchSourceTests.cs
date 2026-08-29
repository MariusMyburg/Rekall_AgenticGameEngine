namespace Rekall.Age.Tests.Editor;

public sealed class StudioWorkbenchSourceTests
{
    [Fact]
    public async Task StudioInspectorUsesTypedInlineEditorsAsPrimaryPropertyWorkflow()
    {
        var root = FindRepositoryRoot();
        var studioDirectory = Path.Combine(root, "src", "Rekall.Age.Studio");
        var xaml = await File.ReadAllTextAsync(Path.Combine(studioDirectory, "MainWindow.xaml"));
        var windowCode = await File.ReadAllTextAsync(Path.Combine(studioDirectory, "MainWindow.xaml.cs"));
        var selectorCode = await File.ReadAllTextAsync(Path.Combine(
            studioDirectory,
            "RekallAgeStudioInspectorEditorTemplateSelector.cs"));
        var viewModelCode = await File.ReadAllTextAsync(Path.Combine(studioDirectory, "RekallAgeStudioViewModel.cs"));

        Assert.Contains("ItemsSource=\"{Binding InspectorComponentEditors}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding PropertyEditors}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ContentTemplateSelector=\"{StaticResource InspectorEditorTemplateSelector}\"", xaml, StringComparison.Ordinal);

        foreach (var templateKind in new[]
                 {
                     "Boolean", "Number", "Integer", "Enum", "AssetRef", "EntityRef", "Color",
                     "Vector2", "Vector3", "Vector4", "Json", "String"
                 })
        {
            Assert.Contains($"{templateKind}Template=\"{{StaticResource Inspector{templateKind}EditorTemplate}}\"", xaml, StringComparison.Ordinal);
            Assert.Contains($"public DataTemplate? {templateKind}Template", selectorCode, StringComparison.Ordinal);
        }

        Assert.Contains("Command=\"{Binding DataContext.CommitInspectorPropertyCommand, RelativeSource={RelativeSource AncestorType=Window}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DataContext.ResetInspectorPropertyCommand, RelativeSource={RelativeSource AncestorType=Window}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"{Binding}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ValidationMessage", xaml, StringComparison.Ordinal);
        Assert.Contains("IsDefined", xaml, StringComparison.Ordinal);
        Assert.Contains("Description", xaml, StringComparison.Ordinal);
        Assert.Contains("OnInspectorTextEditorKeyDown", xaml, StringComparison.Ordinal);
        Assert.Contains("OnInspectorTextEditorLostKeyboardFocus", xaml, StringComparison.Ordinal);
        Assert.Contains("OnInspectorBooleanChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("OnInspectorChoiceChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding ReferenceValue, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ReferenceSearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OnInspectorReferenceChoiceChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("OnInspectorJsonEditorKeyDown", xaml, StringComparison.Ordinal);
        Assert.Contains("RestoreOriginalDraft", windowCode, StringComparison.Ordinal);
        Assert.Contains("CommitInspectorPropertyCommand", windowCode, StringComparison.Ordinal);

        Assert.Contains("COMPONENTS &amp; ADVANCED JSON", xaml, StringComparison.Ordinal);
        Assert.Contains("Add custom JSON property", xaml, StringComparison.Ordinal);
        Assert.Contains("Attached properties are edited above", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SetPropertyCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RemovePropertyCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("rekall.component.set_property", viewModelCode, StringComparison.Ordinal);
        Assert.Contains("rekall.component.remove_property", viewModelCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StudioWorkspaceWiresCanonicalGameCreationCommandsAndRenderedViewport()
    {
        var root = FindRepositoryRoot();
        var mainWindowXaml = await File.ReadAllTextAsync(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));
        var authorWorkspaceXaml = await File.ReadAllTextAsync(Path.Combine(root, "src", "Rekall.Age.Studio", "AuthorWorkspace.xaml"));
        var xaml = mainWindowXaml + Environment.NewLine + authorWorkspaceXaml;
        var mainWindowCode = await File.ReadAllTextAsync(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml.cs"));
        var code = await File.ReadAllTextAsync(Path.Combine(root, "src", "Rekall.Age.Studio", "RekallAgeStudioViewModel.cs"));

        Assert.Contains("Content=\"Open Project…\" Click=\"OnOpenProjectClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Create Project…\" Click=\"OnCreateProjectClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("_viewModel.OpenProjectAsync(dialog.FolderName)", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("_viewModel.CreateCommand", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddEntityCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddComponentCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RemoveComponentCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SetPropertyCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RemovePropertyCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ValidateCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CaptureCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding QualityPresets}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedQualityPreset", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AttachQualityProfileCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ApplyQualityCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CaptureQualityCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CompareQualityCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding RenderPassTimings}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding RenderResources}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding RenderDegradations}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding RenderQualityComparisons}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding RenderDebugViews}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedRenderDebugView", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding TotalGpuMillisecondsText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding PlayCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding StopCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SwitchSceneCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding PackageCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AuditPackageCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding UndoCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RedoCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SceneNames}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ComponentSchemas}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding InspectorComponentEditors}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding PropertyEditors}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RefreshLanguageModelsCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RunAgentCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CancelAgentCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding LanguageModelProviders}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedLanguageModelProvider", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding LanguageModels}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedLanguageModel", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ReasoningEfforts}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedReasoningEffort", xaml, StringComparison.Ordinal);
        Assert.Contains("PasswordChar=\"●\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnApplyOpenAiApiKeyClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OllamaModels", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AgentTaskInput", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding AgentLines}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItemChanged=\"OnSelectedEntityChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<local:RekallAgeVulkanViewportHost x:Name=\"SceneVulkanViewportHost\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ViewportSummary}\" FontSize=\"14\" VerticalAlignment=\"Center\"", xaml, StringComparison.Ordinal);

        Assert.Contains("RekallAgeDefaultCommandRegistry.Create()", code, StringComparison.Ordinal);
        Assert.Contains("new RekallAgeWorkbenchSession", code, StringComparison.Ordinal);
        Assert.Contains("rekall.render.capture_runtime_viewport", code, StringComparison.Ordinal);
        Assert.Contains("rekall.render.compare_quality_presets", code, StringComparison.Ordinal);
        Assert.Contains("ApplyRenderQualityAsync", code, StringComparison.Ordinal);
        Assert.Contains("rekall.component.set_property", code, StringComparison.Ordinal);
        Assert.Contains("rekall.component.remove_property", code, StringComparison.Ordinal);
        Assert.Contains("rekall.validation.scene", code, StringComparison.Ordinal);
        Assert.Contains("rekall.entity.create", code, StringComparison.Ordinal);
        Assert.Contains("rekall.component.add", code, StringComparison.Ordinal);
        Assert.Contains("rekall.component.remove", code, StringComparison.Ordinal);
        Assert.Contains("rekall.component.set_property", code, StringComparison.Ordinal);
        Assert.Contains("rekall.component.remove_property", code, StringComparison.Ordinal);
        Assert.Contains("JsonNode.Parse", code, StringComparison.Ordinal);
        Assert.Contains("RekallAgeLanguageModelProviderCatalog", code, StringComparison.Ordinal);
        Assert.Contains("IRekallAgeProjectAgentRunner", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new RekallAgeOllamaLanguageModelClient", code, StringComparison.Ordinal);
        Assert.Contains("_session.ReloadAsync", code, StringComparison.Ordinal);
        Assert.Contains("_session.OpenSceneAsync", code, StringComparison.Ordinal);
        Assert.Contains("rekall.workflow.package_playable_game", code, StringComparison.Ordinal);
        Assert.Contains("rekall.workflow.audit_playable_package", code, StringComparison.Ordinal);
        Assert.Contains("_session.UndoSinceOpenAsync", code, StringComparison.Ordinal);
        Assert.Contains("_session.RedoAsync", code, StringComparison.Ordinal);
        Assert.Contains("model.Inspector.AvailableComponents", code, StringComparison.Ordinal);
        Assert.Contains("SelectedPropertySchema", code, StringComparison.Ordinal);
        Assert.True(
            code.IndexOf("Replace(SceneNames, model.Project.Scenes", StringComparison.Ordinal)
            < code.IndexOf("SceneNameInput = model.Scene.Name", StringComparison.Ordinal),
            "Scene choices must be populated before the selected scene is restored so WPF cannot clear it during refresh.");

        var studioDirectory = Path.Combine(root, "src", "Rekall.Age.Studio");
        var studioSources = string.Join(
            Environment.NewLine,
            await Task.WhenAll(Directory.EnumerateFiles(studioDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(path => File.ReadAllTextAsync(path))));
        var mainWorkspaceSources = mainWindowCode + Environment.NewLine + code;
        Assert.DoesNotContain("RekallAgeProjectStore", mainWorkspaceSources, StringComparison.Ordinal);
        Assert.DoesNotContain("RekallAgeSceneStore", mainWorkspaceSources, StringComparison.Ordinal);
        Assert.DoesNotContain("RekallAgeAssetCatalogStore", mainWorkspaceSources, StringComparison.Ordinal);
        Assert.Contains("RekallAgeDefaultCommandRegistry.Create()", studioSources, StringComparison.Ordinal);
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
