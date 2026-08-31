using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioLayoutTests
{
    [Fact]
    public void WorldHostsAResizableContentBrowserWithoutReplacingTheViewportTransformContract()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));

        Assert.Contains("x:Name=\"ContentBrowserPanel\"", window, StringComparison.Ordinal);
        Assert.Contains("<local:ContentBrowser", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContentBrowserSplitter\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Assets\"><ListBox ItemsSource=\"{Binding AssetLines}\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"WorldTransformStrip\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MoveSnapEditor\" MinWidth=\"64\"", window, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentBrowserLayoutIsVisibleByDefaultAndMigratesOlderLayouts()
    {
        var layout = RekallAgeStudioLayout.Default;

        Assert.True(layout.Panel("ContentBrowser").Visible);
        Assert.True(layout.Panel("ContentBrowser").Size >= 190);
        Assert.Equal("Content Browser", layout.ActiveOutputTab);

        var legacy = layout with
        {
            Version = 4,
            Panels = layout.Panels.Where(panel => panel.Id != "ContentBrowser").ToArray()
        };
        var migrated = RekallAgeStudioLayout.Normalize(legacy);

        Assert.NotNull(migrated);
        Assert.True(migrated.Panel("ContentBrowser").Visible);
        Assert.True(migrated.Panel("ContentBrowser").Size >= 190);
    }

    [Fact]
    public void ContentBrowserAndOtherBottomTabsShareOnePersistedHeight()
    {
        var debug = RekallAgeStudioLayout.CreatePreset(RekallAgeStudioLayoutPreset.Debug);
        Assert.Equal("Runtime", debug.ActiveOutputTab);
        Assert.Equal(420, debug.Panel("Output").Size);
        Assert.Equal(debug.Panel("Output").Size, debug.Panel("ContentBrowser").Size);

        var contentActive = RekallAgeStudioLayout.Normalize(debug with { ActiveOutputTab = "Content Browser" })!;
        Assert.Equal(420, contentActive.Panel("Output").Size);
        Assert.Equal(420, contentActive.Panel("ContentBrowser").Size);

        var code = File.ReadAllText(Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
            "src", "Rekall.Age.Studio", "MainWindow.xaml.cs"));
        Assert.Contains("OutputRow.Height = new GridLength(layout.Panel(\"Output\").Visible ? layout.Panel(\"Output\").Size : 0)", code, StringComparison.Ordinal);
        Assert.Contains("Size = sharedBottomHeight", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewMenuCanRestoreAndFocusTheContentBrowser()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml.cs"));

        Assert.Contains("Header=\"_View\"", window, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Content Browser\"", window, StringComparison.Ordinal);
        Assert.Contains("OnShowContentBrowserClick", window, StringComparison.Ordinal);
        Assert.Contains("ContentBrowserHost.Focus()", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldViewportHostsRekallAgeVulkanViewportHostInsteadOfSceneViewportImage()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml.cs"));

        Assert.Contains("local:RekallAgeVulkanViewportHost", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SceneVulkanViewportHost\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("SceneViewportImage", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Source=\"{Binding ViewportImage}\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("SceneGizmoPopup", window, StringComparison.Ordinal);
        Assert.DoesNotContain("SceneGizmoOverlayCanvas", window, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldViewportShowsAProjectWelcomeStateSeparatelyFromVulkanFailures()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));

        Assert.Contains("x:Name=\"NoProjectViewportPlaceholder\"", window, StringComparison.Ordinal);
        Assert.Contains("Create a project", window, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Open a project", window, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorldViewportKeepsVulkanUnavailablePlaceholderAndExternalTransformControls()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));

        Assert.Contains("x:Name=\"VulkanUnavailablePlaceholder\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"VulkanUnavailablePlaceholder\" Visibility=\"Collapsed\"",
            window,
            StringComparison.Ordinal);
        Assert.Contains("ViewportUnavailableReason", window, StringComparison.Ordinal);
        Assert.Contains("ViewportBackendLabel", window, StringComparison.Ordinal);
        Assert.Contains("Vulkan is unavailable", window, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Grid.Row=\"2\"", window, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TransformTools}\"", window, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TransformSpaces}\"", window, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldTransformSnapEditorsRemainReadableAtScaledDpi()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));

        Assert.Contains("x:Name=\"MoveSnapEditor\" MinWidth=\"64\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RotationSnapEditor\" MinWidth=\"64\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScaleSnapEditor\" MinWidth=\"64\"", window, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Move snap distance\"", window, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Rotation snap angle\"", window, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Scale snap increment\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBox Width=\"40\" Text=\"{Binding RotationSnap", window, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBox Width=\"45\" Text=\"{Binding MoveSnap", window, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioCarriesTheSharedVulkanRuntimePackagesIntoItsExecutableOutput()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var project = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "Rekall.Age.Studio.csproj"));

        Assert.Contains("<PackageReference Include=\"Veldrid\" Version=\"4.9.0\"", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Veldrid.SPIRV\" Version=\"1.0.15\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultAndPresetsExposeEveryNamedPanelWithUsefulBounds()
    {
        var layout = RekallAgeStudioLayout.Default;

        Assert.Equal(RekallAgeStudioLayout.CurrentVersion, layout.Version);
        Assert.Equal(5, layout.Version);
        Assert.Equal("Author", layout.ActiveWorkspace);
        Assert.Equal(["ContentBrowser", "Hierarchy", "Inspector", "Output"], layout.Panels.Select(panel => panel.Id).Order().ToArray());
        Assert.All(layout.Panels, panel => Assert.True(panel.Visible));
        Assert.InRange(layout.WindowWidth, 1120, 3840);
        Assert.InRange(layout.WindowHeight, 700, 2160);
        Assert.Equal(340, layout.Panel("Hierarchy").Size);
        Assert.Equal(460, layout.Panel("Inspector").Size);

        var authoring = RekallAgeStudioLayout.CreatePreset(RekallAgeStudioLayoutPreset.Authoring);
        Assert.Equal(RekallAgeStudioDockRegion.Left, authoring.Panel("Hierarchy").Region);
        Assert.Equal(RekallAgeStudioDockRegion.Right, authoring.Panel("Inspector").Region);
        Assert.Equal(RekallAgeStudioDockRegion.Bottom, authoring.Panel("Output").Region);
        Assert.Equal("Author", authoring.ActiveWorkspace);
        Assert.Equal("Validation", authoring.ActiveOutputTab);

        var debug = RekallAgeStudioLayout.CreatePreset(RekallAgeStudioLayoutPreset.Debug);
        Assert.True(debug.Panel("Output").Size > authoring.Panel("Output").Size);
        Assert.Equal("Runtime", debug.ActiveOutputTab);
        Assert.Equal("World", debug.ActiveWorkspace);
        Assert.Equal(330, debug.Panel("Hierarchy").Size);
        Assert.Equal(460, debug.Panel("Inspector").Size);
    }

    [Fact]
    public void CurrentLayoutWidensKnownLegacyAndUndersizedPanelsButPreservesWiderCustomPanels()
    {
        var legacyDefault = RekallAgeStudioLayout.Default with
        {
            Version = 2,
            Panels =
            [
                new("Hierarchy", RekallAgeStudioDockRegion.Left, true, 290, 0),
                new("Inspector", RekallAgeStudioDockRegion.Right, true, 370, 0),
                new("Output", RekallAgeStudioDockRegion.Bottom, true, 260, 0)
            ]
        };
        var migrated = RekallAgeStudioLayout.Normalize(legacyDefault)!;

        Assert.Equal(5, migrated.Version);
        Assert.Equal(340, migrated.Panel("Hierarchy").Size);
        Assert.Equal(460, migrated.Panel("Inspector").Size);

        var undersized = legacyDefault with
        {
            Panels =
            [
                new("Hierarchy", RekallAgeStudioDockRegion.Left, true, 180, 0),
                new("Inspector", RekallAgeStudioDockRegion.Right, true, 180, 0),
                new("Output", RekallAgeStudioDockRegion.Bottom, true, 260, 0)
            ]
        };
        var migratedUndersized = RekallAgeStudioLayout.Normalize(undersized)!;

        Assert.Equal(340, migratedUndersized.Panel("Hierarchy").Size);
        Assert.Equal(460, migratedUndersized.Panel("Inspector").Size);

        var custom = legacyDefault with
        {
            Panels =
            [
                new("Hierarchy", RekallAgeStudioDockRegion.Left, true, 380, 0),
                new("Inspector", RekallAgeStudioDockRegion.Right, true, 520, 0),
                new("Output", RekallAgeStudioDockRegion.Bottom, true, 260, 0)
            ]
        };
        var normalizedCustom = RekallAgeStudioLayout.Normalize(custom)!;

        Assert.Equal(380, normalizedCustom.Panel("Hierarchy").Size);
        Assert.Equal(520, normalizedCustom.Panel("Inspector").Size);
    }

    [Fact]
    public void CurrentLayoutRepairsUndersizedVersionThreeWorldPanelsThatClipInspectorActions()
    {
        var undersizedVersionThree = RekallAgeStudioLayout.Default with
        {
            Version = 3,
            Panels =
            [
                new("Hierarchy", RekallAgeStudioDockRegion.Left, true, 180, 0),
                new("Inspector", RekallAgeStudioDockRegion.Right, true, 180, 0),
                new("Output", RekallAgeStudioDockRegion.Bottom, true, 260, 0)
            ]
        };

        var migrated = RekallAgeStudioLayout.Normalize(undersizedVersionThree)!;

        Assert.Equal(5, migrated.Version);
        Assert.Equal(340, migrated.Panel("Hierarchy").Size);
        Assert.Equal(460, migrated.Panel("Inspector").Size);
    }

    [Fact]
    public async Task LayoutStoreRoundTripsAValidatedLayoutAndReplacesThePreviousFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-layout-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "layout.json");
        try
        {
            var store = new RekallAgeStudioLayoutStore(path);
            var first = RekallAgeStudioLayout.Default with
            {
                WindowWidth = 1400,
                ActiveOutputTab = "Content Browser",
                ActiveWorkspace = "Modeling"
            };
            await store.SaveAsync(first, CancellationToken.None);
            var second = first with
            {
                WindowWidth = 1660,
                Panels = first.Panels.Select(panel => panel.Id == "Inspector"
                    ? panel with { Region = RekallAgeStudioDockRegion.Left, Visible = false, Size = 420 }
                    : panel).ToArray()
            };

            await store.SaveAsync(second, CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal(1660, loaded.WindowWidth);
            Assert.Equal("Content Browser", loaded.ActiveOutputTab);
            Assert.Equal("Modeling", loaded.ActiveWorkspace);
            Assert.False(loaded.Panel("Inspector").Visible);
            Assert.Equal(RekallAgeStudioDockRegion.Left, loaded.Panel("Inspector").Region);
            Assert.Equal(420, loaded.Panel("Inspector").Size);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StudioCodeWorkspaceExposesComponentAuthoringEditorBuildAndIdeActions()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "CodeWorkspace.xaml"));

        Assert.Contains("Header=\"Code\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CodeWorkspaceHost\"", window, StringComparison.Ordinal);
        Assert.Contains("CreateAttachCodeComponentCommand", code, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding CodeSources}\"", code, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CodeSourceText", code, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn=\"True\"", code, StringComparison.Ordinal);
        Assert.Contains("AcceptsTab=\"True\"", code, StringComparison.Ordinal);
        Assert.Contains("FontFamily=\"Cascadia Code\"", code, StringComparison.Ordinal);
        Assert.Contains("SaveCodeCommand", code, StringComparison.Ordinal);
        Assert.Contains("BuildCodeCommand", code, StringComparison.Ordinal);
        Assert.Contains("OpenCodeFileCommand", code, StringComparison.Ordinal);
        Assert.Contains("OpenCodeProjectCommand", code, StringComparison.Ordinal);
        Assert.Contains("OpenCodeSolutionCommand", code, StringComparison.Ordinal);
        Assert.Contains("OpenCodeInVsCodeCommand", code, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding CodeOutputLines}\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioShellPromotesModelingToAnUnclutteredTopLevelWorkspace()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));
        var modeling = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "ModelingWorkspace.xaml"));

        Assert.Contains("x:Name=\"WorkspaceSelector\"", window, StringComparison.Ordinal);
        Assert.Contains("Header=\"Author\"", window, StringComparison.Ordinal);
        Assert.Contains("Header=\"World\"", window, StringComparison.Ordinal);
        Assert.Contains("Header=\"Modeling\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AuthorWorkspaceHost\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ModelingWorkspaceHost\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProjectBar\"", window, StringComparison.Ordinal);
        Assert.Contains("MeshViewportImage", modeling, StringComparison.Ordinal);
        Assert.Contains("ModelingGraphViewportImage", modeling, StringComparison.Ordinal);
        Assert.Contains("CreateMeshPrimitiveCommand", modeling, StringComparison.Ordinal);
        Assert.Contains("MeshOperationIds", modeling, StringComparison.Ordinal);
        Assert.Contains("PreviewMeshOperationCommand", modeling, StringComparison.Ordinal);
        Assert.Contains("ApplyMeshOperationCommand", modeling, StringComparison.Ordinal);
        Assert.Contains("NODE CONTRACTS", modeling, StringComparison.Ordinal);
        Assert.Contains("MouseWheel=\"OnModelingGraphCanvasMouseWheel\"", modeling, StringComparison.Ordinal);
        Assert.Contains("Pan: middle/right drag", modeling, StringComparison.Ordinal);
        Assert.Contains("Zoom: wheel", modeling, StringComparison.Ordinal);
        Assert.Contains("OnResetModelingGraphCanvasView", modeling, StringComparison.Ordinal);
        Assert.Contains("Orbit: middle drag", modeling, StringComparison.Ordinal);
        Assert.Contains("Pan: Shift+middle drag", modeling, StringComparison.Ordinal);
        Assert.Contains("Zoom: wheel", modeling, StringComparison.Ordinal);
        Assert.DoesNotContain("<Line X1=\"0\" Y1=\"0\" X2=\"1\" Y2=\"1\"", modeling, StringComparison.Ordinal);
        Assert.Contains("MeshViewportRenderStyle", modeling, StringComparison.Ordinal);
        Assert.Contains("ModelingGraphViewportRenderStyle", modeling, StringComparison.Ordinal);
        Assert.Contains("ViewportRenderStyles", modeling, StringComparison.Ordinal);
        Assert.Contains("WorldViewportRenderStyle", window, StringComparison.Ordinal);
        Assert.Contains("<WrapPanel Orientation=\"Horizontal\">", window, StringComparison.Ordinal);
        Assert.Contains("<WrapPanel Orientation=\"Horizontal\">", modeling, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Preview\" Width=\"62\"", modeling, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Apply\" Width=\"52\"", modeling, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Cancel\" Width=\"55\"", modeling, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorWorkspaceKeepsProviderSetupContextualAndPrimaryActionsVisible()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));
        var author = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "AuthorWorkspace.xaml"));
        var app = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "App.xaml"));

        Assert.DoesNotContain("<TabItem Header=\"AI Agent\"", window, StringComparison.Ordinal);
        Assert.Contains("IsOpenAiSelected", author, StringComparison.Ordinal);
        Assert.Contains("IsCodexSelected", author, StringComparison.Ordinal);
        Assert.Contains("IsEditable=\"False\"", author, StringComparison.Ordinal);
        Assert.Contains("Text=\"Run Agent\"", author, StringComparison.Ordinal);
        Assert.Contains("Content=\"Cancel\"", author, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding AgentLines}\"", author, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CreateProjectButton\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenProjectButton\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"Create Project…\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"Open Project…\"", window, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding ProjectPathInput}\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ProjectContextText}\"", author, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ProjectNameInput}\"", author, StringComparison.Ordinal);
        Assert.Contains("HasInspectorSelection", window, StringComparison.Ordinal);
        Assert.Contains("InspectorComponentBrowserEmptyText", window, StringComparison.Ordinal);
        Assert.Contains("Content=\"{TemplateBinding SelectionBoxItem}\"", app, StringComparison.Ordinal);
        Assert.Contains("ContentTemplate=\"{TemplateBinding SelectionBoxItemTemplate}\"", app, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalAlignment=\"Right\" Background=\"Transparent\" BorderThickness=\"0\" Focusable=\"False\" Cursor=\"Hand\" IsChecked=\"{Binding IsDropDownOpen", app, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldWorkspaceKeepsAgentActivityAndPromptSubmissionVisible()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));

        Assert.Contains("x:Name=\"WorldAuthoringStrip\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AgentActivityText}\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AgentTaskInput, UpdateSourceTrigger=PropertyChanged}\"", window, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RunAgentCommand}\"", window, StringComparison.Ordinal);
        Assert.Contains("Key=\"Enter\"", window, StringComparison.Ordinal);
        Assert.Contains("IsIndeterminate=\"True\"", window, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioShellOffersSettingsAndConfigureOnlyRecoveryBannersInAuthorAndWorld()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));

        Assert.Contains("Header=\"_Settings\"", window, StringComparison.Ordinal);
        Assert.Contains("Header=\"Language Model Setup…\"", window, StringComparison.Ordinal);
        Assert.Equal(2, Count(window, "AI setup incomplete"));
        Assert.Equal(2, Count(window, "Content=\"Configure\""));
        Assert.Contains("IsLanguageModelSetupIncomplete", window, StringComparison.Ordinal);
        Assert.Contains("OnLanguageModelSetupClick", window, StringComparison.Ordinal);
        Assert.Equal(
            3,
            Count(window, "IsEnabled=\"{Binding CanOpenLanguageModelSetup, ElementName=StudioWindow}\""));
        Assert.Contains("MinWidth=\"", window, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", window, StringComparison.Ordinal);
    }

    private static int Count(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    [Fact]
    public void WorldWorkspaceExposesTheAdvancedInspectorSurface()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));

        Assert.Contains("x:Name=\"InspectorSearchBox\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InspectorComponentList\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InspectorSelectionName}\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InspectorSelectionId}\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InspectorComponentCountText}\"", window, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding InspectorComponentEditors}\"", window, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding SelectedInspectorComponent, Mode=TwoWay}\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SelectedInspectorComponentDescription}\"", window, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding PropertyEditors}\"", window, StringComparison.Ordinal);
        Assert.Contains("ContentTemplateSelector=\"{StaticResource InspectorEditorTemplateSelector}\"", window, StringComparison.Ordinal);
        Assert.Contains("InspectorBooleanEditorTemplate", window, StringComparison.Ordinal);
        Assert.Contains("InspectorColorEditorTemplate", window, StringComparison.Ordinal);
        Assert.Contains("InspectorVector3EditorTemplate", window, StringComparison.Ordinal);
        Assert.Contains("ResetInspectorPropertyCommand", window, StringComparison.Ordinal);
        Assert.Contains("Content=\"Add / Replace\"", window, StringComparison.Ordinal);
        Assert.Contains("Content=\"Show/Hide\" Padding=\"5,4\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Set Value\"", window, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"version\":999,\"panels\":[]}")]
    [InlineData("{\"version\":1,\"windowWidth\":0,\"windowHeight\":0,\"panels\":[]}")]
    [InlineData("{\"version\":2,\"windowWidth\":1500,\"windowHeight\":940,\"activeOutputTab\":\"Validation\",\"panels\":[null]}")]
    public async Task LayoutStoreFallsBackForCorruptFutureOrIncompleteDocuments(string content)
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-layout-invalid-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "layout.json");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(path, content);

            var loaded = await new RekallAgeStudioLayoutStore(path).LoadAsync(CancellationToken.None);

            Assert.Equal(RekallAgeStudioLayout.Default, loaded);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LayoutStoreClampsFiniteSizesAndRejectsUnknownPanelData()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-layout-bounds-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "layout.json");
        try
        {
            var invalid = RekallAgeStudioLayout.Default with
            {
                Version = 4,
                WindowWidth = 99_000,
                WindowHeight = double.NaN,
                Panels =
                [
                    new("Hierarchy", RekallAgeStudioDockRegion.Left, true, 9_999, 0),
                    new("Inspector", RekallAgeStudioDockRegion.Right, true, -4, 0),
                    new("Output", RekallAgeStudioDockRegion.Bottom, true, 9_999, 0)
                ]
            };
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(invalid, new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            }));

            var loaded = await new RekallAgeStudioLayoutStore(path).LoadAsync(CancellationToken.None);

            Assert.Equal(3840, loaded.WindowWidth);
            Assert.Equal(RekallAgeStudioLayout.Default.WindowHeight, loaded.WindowHeight);
            Assert.Equal(720, loaded.Panel("Hierarchy").Size);
            Assert.Equal(180, loaded.Panel("Inspector").Size);
            Assert.Equal(640, loaded.Panel("Output").Size);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
