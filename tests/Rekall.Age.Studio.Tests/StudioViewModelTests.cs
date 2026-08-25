using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor;
using Rekall.Age.Modeling;
using Rekall.Age.Project;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Studio;
using Rekall.Age.Workflows;
using Rekall.Age.Workflows.Commands;
using Rekall.Age.World;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioViewModelTests
{
    [Fact]
    public async Task ProviderSelectionExposesStableMissingOpenAiCredentialGateWithoutRetainingOllamaModels()
    {
        var ollama = new ProviderLifecycleHandler(blockOllamaChat: false);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = " " },
            () => new HttpClient(ollama, disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());

        Assert.Equal(["ollama", "openai"], viewModel.LanguageModelProviders.Select(provider => provider.Id));
        Assert.Equal(["none", "low", "medium", "high", "xhigh", "max"], viewModel.ReasoningEfforts);
        Assert.Equal("medium", viewModel.SelectedReasoningEffort);
        Assert.Equal("ollama", viewModel.SelectedLanguageModelProvider.Id);
        await ExecuteAsync(viewModel.RefreshLanguageModelsCommand);
        Assert.Equal(["gemma3:latest", "qwen3.5:35b"], viewModel.LanguageModels);
        Assert.Equal("qwen3.5:35b", viewModel.SelectedLanguageModel);

        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "openai");
        await viewModel.WaitForLanguageModelProviderTransitionAsync();

        Assert.Empty(viewModel.LanguageModels);
        Assert.Empty(viewModel.SelectedLanguageModel);
        Assert.Equal(
            "REKALL_OPENAI_API_KEY_MISSING: OpenAI requires OPENAI_API_KEY for this session.",
            viewModel.ProviderStatus);
        Assert.False(viewModel.RefreshLanguageModelsCommand.CanExecute(null));
        Assert.DoesNotContain("qwen", viewModel.ProviderStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderSwitchCancelsAndAwaitsTheCurrentRunBeforeDisposingItsLeaseAndLoadingTheExactDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-provider-switch-" + Guid.NewGuid().ToString("N"));
        var ollama = new ProviderLifecycleHandler(blockOllamaChat: true);
        var openAi = new ProviderLifecycleHandler(blockOllamaChat: false);
        var handlers = new Queue<ProviderLifecycleHandler>([ollama, openAi]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = "session-test-key" },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                catalog,
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Provider Switch",
                SceneNameInput = "Main",
                AgentTaskInput = "Inspect the open project."
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.RefreshLanguageModelsCommand);

            var run = ExecuteAsync(viewModel.RunAgentCommand);
            await ollama.WaitForChatAsync();
            viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "openai");
            await viewModel.WaitForLanguageModelProviderTransitionAsync();
            await run;

            Assert.Equal(["chat-cancelled", "lease-disposed"], ollama.Events);
            Assert.Equal("openai", viewModel.SelectedLanguageModelProvider.Id);
            Assert.Equal(["gpt-5.6-sol", "gpt-5.6-sol-preview"], viewModel.LanguageModels);
            Assert.Equal("gpt-5.6-sol", viewModel.SelectedLanguageModel);
            Assert.Equal("OpenAI ready with 2 models.", viewModel.ProviderStatus);
            Assert.False(viewModel.IsAgentRunning);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAiSessionKeyUnlocksTheSelectedProviderWithoutAppearingInInspectableStudioState()
    {
        const string secret = "studio-session-secret-must-stay-private";
        var handlers = new Queue<ProviderLifecycleHandler>(
            [new(blockOllamaChat: false), new(blockOllamaChat: false)]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = " " },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());
        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "openai");
        await viewModel.WaitForLanguageModelProviderTransitionAsync();

        await viewModel.ApplyOpenAiApiKeyAsync(secret);

        Assert.True(viewModel.HasSessionOpenAiCredential);
        Assert.Equal("gpt-5.6-sol", viewModel.SelectedLanguageModel);
        var inspectable = string.Join('\n',
            [viewModel.ProviderStatus, viewModel.StatusText, .. viewModel.ValidationLines, .. viewModel.AgentLines]);
        Assert.DoesNotContain(secret, inspectable, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderSwitchCancelsAndAwaitsModelRefreshBeforeDisposingItsLease()
    {
        var ollama = new ProviderLifecycleHandler(blockOllamaChat: false, blockOllamaModels: true);
        var openAi = new ProviderLifecycleHandler(blockOllamaChat: false);
        var handlers = new Queue<ProviderLifecycleHandler>([ollama, openAi]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = "session-test-key" },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());

        var refresh = ExecuteAsync(viewModel.RefreshLanguageModelsCommand);
        await ollama.WaitForModelsAsync();
        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "openai");
        await viewModel.WaitForLanguageModelProviderTransitionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await refresh;

        Assert.Equal(["models-cancelled", "lease-disposed"], ollama.Events);
        Assert.Equal("gpt-5.6-sol", viewModel.SelectedLanguageModel);
    }

    [Fact]
    public async Task PartialComparisonFailureKeepsUsableTypedEvidenceVisibleWithItsError()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-partial-comparison-" + Guid.NewGuid().ToString("N"));
        try
        {
            await CreateQualityProjectAsync(root);
            var registry = new RekallAgeCommandRegistry();
            registry.Register(new PartialQualityComparisonCommand());
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(registry),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.OpenCommand);

            await ExecuteAsync(viewModel.CompareQualityCommand);

            var comparison = Assert.Single(viewModel.RenderQualityComparisons);
            Assert.Equal("D:/captures/partial-high.png", comparison.ScreenshotPath);
            Assert.Equal("High", viewModel.ResolvedQualityPreset);
            Assert.Single(viewModel.RenderDebugViews);
            Assert.Contains(viewModel.ValidationLines, line =>
                line.Contains("REKALL_TEST_PARTIAL_COMPARISON", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeCancelsAndAwaitsActiveQualityCaptureBeforePreviewDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-cancel-quality-" + Guid.NewGuid().ToString("N"));
        RekallAgeStudioViewModel? viewModel = null;
        Task? captureTask = null;
        Task? disposeTask = null;
        var command = new CancellationBlockingCaptureCommand();
        var preview = new RecordingPreviewSession();
        preview.BlockDispose();
        try
        {
            await CreateQualityProjectAsync(root);
            var registry = new RekallAgeCommandRegistry();
            registry.Register(command);
            viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(registry),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.OpenCommand);

            captureTask = ExecuteAsync(viewModel.CaptureQualityCommand);
            await command.WaitForStartAsync();
            var cancellationObserved = command.WaitForCancellationAsync();
            var previewDisposeEntered = preview.WaitForDisposeAsync();
            disposeTask = viewModel.DisposeAsync().AsTask();

            var firstLifecycleSignal = await Task.WhenAny(cancellationObserved, previewDisposeEntered);

            Assert.Same(cancellationObserved, firstLifecycleSignal);
            Assert.False(previewDisposeEntered.IsCompleted);
        }
        finally
        {
            command.Release();
            preview.ReleaseDispose();
            if (captureTask is not null) await captureTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (disposeTask is not null) await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            else if (viewModel is not null) await viewModel.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        Assert.DoesNotContain(viewModel!.ValidationLines, line =>
            line.Contains("REKALL_STUDIO_UNEXPECTED_FAILURE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AttachQualityProfileUsesTheSharedBuiltInComponentContract()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-attach-quality-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel
            {
                ProjectPathInput = root,
                ProjectNameInput = "Attach Quality",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);

            Assert.True(viewModel.AttachQualityProfileCommand.CanExecute(null));
            await ExecuteAsync(viewModel.AttachQualityProfileCommand);

            Assert.True(viewModel.ApplyQualityCommand.CanExecute(null), viewModel.StatusText);
            Assert.Contains(viewModel.ComponentSchemas, schema => schema.Type == "Rekall.RenderQualityProfile");
            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var profile = Assert.Single(
                Assert.Single(scene.Entities).Components,
                component => component.Type == "Rekall.RenderQualityProfile");
            Assert.Equal("High", profile.Properties["preset"]!.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task QualityControlsPersistGenericComponentMutationsWithoutChangingGameplayState()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-quality-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new RekallAgeProjectStore().SaveAsync(
                root,
                RekallAgeProjectManifest.Create("Quality Controls", ["world", "rendering3d"]),
                CancellationToken.None);
            var quality = RekallAgeEntityDocument.Create("Render Settings", ["rendering"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.RenderQualityProfile",
                    new JsonObject { ["preset"] = "High" }));
            var gameplay = RekallAgeEntityDocument.Create("Runtime State", ["gameplay"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Game.RuntimeState",
                    new JsonObject { ["score"] = 41, ["active"] = true }));
            await new RekallAgeSceneStore().SaveAsync(
                root,
                RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                    .AddEntity(quality)
                    .AddEntity(gameplay),
                CancellationToken.None);

            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.OpenCommand);
            Assert.Equal(["Performance", "Low", "Medium", "High", "Ultra", "Epic"], viewModel.QualityPresets);
            Assert.Equal("High", viewModel.RequestedQualityPreset);
            Assert.Equal("Unavailable", viewModel.ResolvedQualityPreset);
            Assert.Equal("Unavailable", viewModel.TotalGpuMillisecondsText);
            viewModel.SelectedQualityPreset = "Epic";
            viewModel.QualityResolutionScaleInput = "0.8";
            viewModel.QualityShadowCascadeCountInput = "4";
            viewModel.QualityShadowResolutionInput = "4096";
            viewModel.QualityFogModeInput = "froxel-high";
            viewModel.QualityBloomOverride = false;
            viewModel.QualitySsaoOverride = true;
            viewModel.QualityMaximumActiveParticlesInput = "128000";

            await ExecuteAsync(viewModel.ApplyQualityCommand);

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var savedQuality = Assert.Single(
                scene.GetRequiredEntity(quality.Id).Components,
                component => component.Type == "Rekall.RenderQualityProfile");
            Assert.Equal("Epic", savedQuality.Properties["preset"]!.GetValue<string>());
            Assert.Equal(0.8, savedQuality.Properties["resolutionScale"]!.GetValue<double>());
            Assert.Equal(4, savedQuality.Properties["shadowCascadeCount"]!.GetValue<int>());
            Assert.Equal(4096, savedQuality.Properties["shadowResolution"]!.GetValue<int>());
            Assert.Equal("froxel-high", savedQuality.Properties["fogMode"]!.GetValue<string>());
            Assert.False(savedQuality.Properties["bloom"]!.GetValue<bool>());
            Assert.True(savedQuality.Properties["ssao"]!.GetValue<bool>());
            Assert.Equal(128000, savedQuality.Properties["maximumActiveParticles"]!.GetValue<int>());
            var savedGameplay = Assert.Single(
                scene.GetRequiredEntity(gameplay.Id).Components,
                component => component.Type == "Game.RuntimeState");
            Assert.Equal(41, savedGameplay.Properties["score"]!.GetValue<int>());
            Assert.True(savedGameplay.Properties["active"]!.GetValue<bool>());

            var transactions = await new RekallAgeTransactionLogStore().LoadAsync(root, CancellationToken.None);
            Assert.Contains(transactions.Transactions, transaction =>
                transaction.Actor == "studio" && transaction.Name.StartsWith("Set render quality", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PublishedAndPlacedStudioModelSurvivesWindowsPackagingAndPlayableAudit()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-model-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            var gauntlet = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
                new RunAgentAuthoringGauntletRequest(
                    root,
                    "Packaged Model Test",
                    "Main",
                    Path.Combine(root, "Builds", "InitialPackage")),
                new Rekall.Age.Core.Commands.RekallAgeCommandContext(
                    "studio-model-package-test",
                    RekallAgeTransaction.Begin("create playable Studio model fixture"),
                    CancellationToken.None));
            Assert.True(gauntlet.Ok, gauntlet.Summary);

            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Packaged Model Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.OpenCommand);
            viewModel.MeshPrimitiveAssetIdInput = "package-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.ModelAssetIdInput = "package-model";
            viewModel.ModelAssetDisplayNameInput = "Package Model";
            viewModel.ModelEntityNameInput = "Packaged Instance";
            viewModel.ModelPositionZ = 5;
            await ExecuteAsync(viewModel.PublishAndPlaceModelCommand);

            var model = await new RekallAgeModelAssetStore().LoadAsync(root, "package-model", CancellationToken.None);
            await ExecuteAsync(viewModel.PackageCommand);

            Assert.Equal(RekallAgePlayablePackageTargets.Windows, viewModel.SelectedPackageTarget);
            Assert.True(viewModel.LastPackageOutputDirectory is not null,
                viewModel.StatusText + Environment.NewLine + string.Join(Environment.NewLine, viewModel.ValidationLines));
            Assert.NotNull(viewModel.LastPackagePath);
            Assert.True(File.Exists(Path.Combine(viewModel.LastPackageOutputDirectory!, "Play.exe")));
            Assert.True(File.Exists(Path.Combine(viewModel.LastPackageOutputDirectory!, "Play.bat")));
            Assert.True(File.Exists(viewModel.LastPackagePath));

            using var archive = ZipFile.OpenRead(viewModel.LastPackagePath!);
            var entryPaths = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("Play.exe", entryPaths);
            Assert.Contains("Play.bat", entryPaths);
            Assert.Contains("Game/Scenes/Main.age.scene.json", entryPaths);
            Assert.Contains("Game/Assets/Models/package-model.age.model.json", entryPaths);
            Assert.Contains("Game/" + model.LastSuccessfulBuild!.CompiledMeshPath, entryPaths);
            using (var sceneReader = new StreamReader(archive.GetEntry("Game/Scenes/Main.age.scene.json")!.Open()))
            {
                var sceneJson = await sceneReader.ReadToEndAsync();
                Assert.Contains("package-model", sceneJson, StringComparison.Ordinal);
                Assert.Contains("Rekall.ModelAssetReference", sceneJson, StringComparison.Ordinal);
            }

            Assert.True(viewModel.AuditPackageCommand.CanExecute(null));
            await ExecuteAsync(viewModel.AuditPackageCommand);
            Assert.DoesNotContain(viewModel.ValidationLines, line => line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PublishAndPlaceTurnsTheSelectedEditableMeshIntoASelectedSceneEntity()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-place-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Place Model Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.SelectedMeshPrimitive = "box";
            viewModel.MeshPrimitiveAssetIdInput = "hero-box";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.ModelAssetIdInput = "hero-model";
            viewModel.ModelAssetDisplayNameInput = "Hero Model";
            viewModel.ModelEntityIdInput = "hero-instance";
            viewModel.ModelEntityNameInput = "Hero Instance";
            viewModel.ModelPositionX = 1.25;
            viewModel.ModelPositionY = -2.5;
            viewModel.ModelPositionZ = 3.75;
            viewModel.ModelRotationY = 45;
            viewModel.ModelScaleX = 0.5;
            viewModel.ModelScaleY = 2;
            viewModel.ModelScaleZ = 3;

            Assert.True(viewModel.PublishAndPlaceModelCommand.CanExecute(null));
            var previewResetsBeforePlacement = preview.ResetCount;
            await ExecuteAsync(viewModel.PublishAndPlaceModelCommand);

            var published = await new RekallAgeModelAssetStore().LoadAsync(root, "hero-model", CancellationToken.None);
            Assert.Equal("Hero Model", published.DisplayName);
            Assert.Equal(RekallAgeModelSourceKind.Mesh, published.Source.Kind);
            Assert.Equal("hero-box", published.Source.AssetId);
            Assert.NotNull(published.LastSuccessfulBuild);
            Assert.True(File.Exists(Path.Combine(root, published.LastSuccessfulBuild!.CompiledMeshPath)));

            var entity = Assert.Single((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
            Assert.Equal("hero-instance", entity.Id);
            Assert.Equal("Hero Instance", entity.Name);
            var reference = Assert.Single(entity.Components, component => component.Type == "Rekall.ModelAssetReference");
            Assert.Equal("hero-model", reference.Properties["assetId"]!.GetValue<string>());
            var transform = Assert.Single(entity.Components, component => component.Type == "Rekall.Transform3D");
            Assert.Equal(1.25, transform.Properties["x"]!.GetValue<double>());
            Assert.Equal(-2.5, transform.Properties["y"]!.GetValue<double>());
            Assert.Equal(3.75, transform.Properties["z"]!.GetValue<double>());
            Assert.Equal(45, transform.Properties["yaw"]!.GetValue<double>());
            Assert.Equal(0.5, transform.Properties["scaleX"]!.GetValue<double>());
            Assert.Equal(2, transform.Properties["scaleY"]!.GetValue<double>());
            Assert.Equal(3, transform.Properties["scaleZ"]!.GetValue<double>());
            Assert.Equal(entity.Id, viewModel.SelectedEntityId);
            Assert.Equal("hero-model", viewModel.LastPublishedModelAssetId);
            Assert.Equal(entity.Id, viewModel.LastPlacedModelEntityId);
            Assert.True(preview.ResetCount > previewResetsBeforePlacement);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SeparatePublishRebuildAndPlaceActionsPreserveTheLiveLinkedModelWorkflow()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-model-actions-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Model Actions Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.MeshPrimitiveAssetIdInput = "display-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);

            Assert.True(viewModel.PublishModelCommand.CanExecute(null));
            Assert.False(viewModel.PlaceModelCommand.CanExecute(null));
            await ExecuteAsync(viewModel.PublishModelCommand);

            var store = new RekallAgeModelAssetStore();
            var first = await store.LoadAsync(root, "display-mesh", CancellationToken.None);
            Assert.Equal(1, first.Revision);
            Assert.Empty((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
            Assert.True(viewModel.PlaceModelCommand.CanExecute(null));

            viewModel.ModelAssetDisplayNameInput = "Renamed Display Model";
            await ExecuteAsync(viewModel.PublishModelCommand);
            var rebuilt = await store.LoadAsync(root, "display-mesh", CancellationToken.None);
            Assert.Equal(2, rebuilt.Revision);
            Assert.Equal("Renamed Display Model", rebuilt.DisplayName);
            Assert.Equal("display-mesh", rebuilt.Source.AssetId);

            viewModel.ModelEntityNameInput = "Display Instance";
            await ExecuteAsync(viewModel.PlaceModelCommand);
            var entity = Assert.Single((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
            Assert.Equal("Display Instance", entity.Name);
            Assert.Equal(entity.Id, viewModel.SelectedEntityId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StaleModelPlacementSurfacesSuccessfulWarningsInStudioDiagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-stale-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Stale Model Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.MeshPrimitiveAssetIdInput = "stale-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            await ExecuteAsync(viewModel.PublishModelCommand);

            var meshStore = new RekallAgeMeshAssetStore();
            var loaded = await meshStore.LoadVersionedAsync(root, "stale-mesh", CancellationToken.None);
            var replacement = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
                "sphere",
                "stale-mesh",
                "Stale Mesh",
                CancellationToken.None);
            await meshStore.SaveIfRevisionAsync(
                root,
                replacement with { Revision = loaded.Value.Revision + 1 },
                loaded.Revision,
                CancellationToken.None);

            await ExecuteAsync(viewModel.PlaceModelCommand);

            Assert.Contains(
                viewModel.ValidationLines,
                line => line.Contains("warning: REKALL_MODEL_SOURCE_STALE", StringComparison.OrdinalIgnoreCase));
            Assert.Single((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingModelAssetRejectsASelectedMeshSourceMismatchWithoutPlacingTheWrongGeometry()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-model-source-mismatch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Model Source Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.MeshPrimitiveAssetIdInput = "first-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            await ExecuteAsync(viewModel.PublishModelCommand);

            viewModel.MeshPrimitiveAssetIdInput = "second-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.ModelAssetIdInput = "first-mesh";
            viewModel.ModelAssetDisplayNameInput = "First Mesh";
            await ExecuteAsync(viewModel.PublishAndPlaceModelCommand);

            Assert.Empty((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
            Assert.Null(viewModel.LastPlacedModelEntityId);
            Assert.Contains(
                viewModel.ValidationLines,
                line => line.Contains("REKALL_STUDIO_MODEL_SOURCE_MISMATCH", StringComparison.Ordinal));
            var published = await new RekallAgeModelAssetStore().LoadAsync(root, "first-mesh", CancellationToken.None);
            Assert.Equal("first-mesh", published.Source.AssetId);
            Assert.Equal(1, published.Revision);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidPlacementScaleSurfacesTheCanonicalDiagnosticWithoutMutatingTheScene()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-invalid-model-placement-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Invalid Placement Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.MeshPrimitiveAssetIdInput = "invalid-scale-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.ModelScaleX = 0;

            await ExecuteAsync(viewModel.PublishAndPlaceModelCommand);

            Assert.Empty((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
            Assert.Contains(
                viewModel.ValidationLines,
                line => line.Contains("REKALL_MODEL_PLACEMENT_TRANSFORM_INVALID", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidModelAssetIdDisablesPlacementWithoutThrowingFromCommandEvaluation()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-invalid-model-id-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Invalid Model ID Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.ModelAssetIdInput = "bad/id";

            var exception = Record.Exception(() => viewModel.PlaceModelCommand.CanExecute(null));

            Assert.Null(exception);
            Assert.False(viewModel.PlaceModelCommand.CanExecute(null));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ViewportPickSelectsTheMappedSceneEntityAndRejectsLetterboxSpace()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-pick-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Viewport Pick Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            var entity = Assert.Single(viewModel.EntityNodes);
            preview.Regions.Add(new(entity.EntityId, RekallAgeStudioViewportRegionKind.World, 40, 40, 20, 20, 2, 0));

            Assert.True(await viewModel.SelectViewportEntityAsync(200, 100, 100, 50));
            Assert.Equal(entity.EntityId, viewModel.SelectedEntityId);
            Assert.False(await viewModel.SelectViewportEntityAsync(200, 100, 20, 50));
            Assert.Equal(entity.EntityId, viewModel.SelectedEntityId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SceneGizmoDragPersistsAsOneUndoableTransformTransaction()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-gizmo-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Scene Gizmo Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            var entity = Assert.Single(viewModel.EntityNodes);
            viewModel.ComponentTypeInput = "Rekall.Transform3D";
            await ExecuteAsync(viewModel.AddComponentCommand);
            preview.Regions.Add(new(entity.EntityId, RekallAgeStudioViewportRegionKind.World, 40, 40, 20, 20, 2, 0));
            await viewModel.SelectEntityAsync(entity);
            var transactionsBefore = viewModel.TransactionLines.Count;

            Assert.True(viewModel.BeginSceneTransform(100, 100, 65, 50));
            Assert.True(viewModel.UpdateSceneTransform(100, 100, 91, 50));
            Assert.True(await viewModel.CompleteSceneTransformAsync());

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var transform = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.Transform3D");
            Assert.Equal(0.5, transform.Properties["x"]!.GetValue<double>(), 6);
            Assert.Equal(transactionsBefore + 1, viewModel.TransactionLines.Count);

            await ExecuteAsync(viewModel.UndoCommand);
            scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            transform = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.Transform3D");
            Assert.False(transform.Properties.ContainsKey("x"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RichSceneCommandsRenameDuplicateHideLockAndReparentThroughCanonicalTransactions()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-scene-tools-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Scene Tools Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            var parent = Assert.Single(viewModel.EntityNodes);
            await ExecuteAsync(viewModel.AddEntityCommand);
            var child = viewModel.EntityNodes.Single(entity => entity.EntityId != parent.EntityId);
            await viewModel.SelectEntityAsync(child);

            viewModel.EntityNameInput = "Playable Hero";
            await ExecuteAsync(viewModel.RenameEntityCommand);
            viewModel.ParentEntityIdInput = parent.EntityId;
            await ExecuteAsync(viewModel.ReparentEntityCommand);
            await ExecuteAsync(viewModel.ToggleEntityVisibleCommand);
            await ExecuteAsync(viewModel.ToggleEntityLockedCommand);
            await ExecuteAsync(viewModel.DuplicateEntityCommand);

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var updated = scene.GetRequiredEntity(child.EntityId);
            Assert.Equal("Playable Hero", updated.Name);
            Assert.Equal(parent.EntityId, updated.ParentId);
            Assert.False(updated.Visible);
            Assert.True(updated.Locked);
            Assert.Contains(scene.Entities, entity => entity.Id != child.EntityId && entity.Name == "Playable Hero Copy");

            await ExecuteAsync(viewModel.UndoCommand);
            scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            Assert.DoesNotContain(scene.Entities, entity => entity.Id != child.EntityId && entity.Name == "Playable Hero Copy");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ModelingAddMenuCreatesAndOpensACanonicalEditablePrimitiveAsset()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-add-mesh-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Add Mesh Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.SelectedMeshPrimitive = "box";
            viewModel.MeshPrimitiveAssetIdInput = "hero-box";

            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);

            Assert.Equal("hero-box", viewModel.SelectedMeshAssetId);
            Assert.Contains("hero-box", viewModel.MeshAssetIds);
            Assert.NotNull(viewModel.MeshViewportImage);
            Assert.Equal("hero-box", viewModel.ModelAssetIdInput);
            Assert.Equal("Hero Box", viewModel.ModelAssetDisplayNameInput);
            Assert.Equal("Hero Box", viewModel.ModelEntityNameInput);
            Assert.True(viewModel.PublishAndPlaceModelCommand.CanExecute(null));
            var mesh = await new RekallAgeMeshAssetStore().LoadAsync(root, "hero-box", CancellationToken.None);
            Assert.Equal("hero-box", mesh.AssetId);
            Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ViewModelExposesDistinctEditAndPersistentSimulateModes()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-mode-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview);
            viewModel.ProjectPathInput = root;
            viewModel.ProjectNameInput = "Mode Test";
            viewModel.SceneNameInput = "Main";
            await ExecuteAsync(viewModel.CreateCommand);

            Assert.Equal(RekallAgeStudioMode.Edit, viewModel.Mode);
            Assert.True(viewModel.SimulateCommand.CanExecute(null));

            await ExecuteAsync(viewModel.SimulateCommand);
            await viewModel.AdvanceLivePreviewAsync();

            Assert.Equal(RekallAgeStudioMode.Simulate, viewModel.Mode);
            Assert.True(viewModel.IsSimulating);
            Assert.False(viewModel.PlayCommand.CanExecute(null));
            Assert.Equal(6, viewModel.PreviewFrameIndex);
            Assert.Equal(2, preview.ResetCount);
            Assert.Equal(1, preview.StepCount);

            await ExecuteAsync(viewModel.StopCommand);

            Assert.Equal(RekallAgeStudioMode.Edit, viewModel.Mode);
            Assert.False(viewModel.IsSimulating);
            Assert.Equal(3, preview.ResetCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PausedSimulationSuppressesAutomaticTicksAndSingleStepAdvancesExactlyOneFrame()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-step-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Pause Step Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.SimulateCommand);

            await ExecuteAsync(viewModel.PauseSimulationCommand);
            Assert.True(viewModel.IsSimulationPaused);
            await viewModel.AdvanceLivePreviewAsync();
            Assert.Equal(0, viewModel.PreviewFrameIndex);
            Assert.Equal(0, preview.StepCount);

            await ExecuteAsync(viewModel.StepSimulationCommand);
            Assert.Equal(1, viewModel.PreviewFrameIndex);
            Assert.Equal(1, preview.StepCount);

            await ExecuteAsync(viewModel.PauseSimulationCommand);
            Assert.False(viewModel.IsSimulationPaused);
            await viewModel.AdvanceLivePreviewAsync();
            Assert.Equal(7, viewModel.PreviewFrameIndex);
            Assert.Equal(2, preview.StepCount);

            await ExecuteAsync(viewModel.StopCommand);
            Assert.False(viewModel.IsSimulationPaused);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LiveOffSuppressesAutomaticEditPreviewAndPersistentCaptureArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-live-off-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Live Off Test",
                SceneNameInput = "Main",
                IsLiveViewportEnabled = false
            };

            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);

            Assert.Equal(0, preview.ResetCount);
            Assert.False(Directory.Exists(Path.Combine(root, "Artifacts", "Studio", "Viewport")));

            viewModel.IsLiveViewportEnabled = true;
            await ExecuteAsync(viewModel.AddEntityCommand);

            Assert.Equal(1, preview.ResetCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ModeTransitionDisablesConflictingCommandsBeforeAwaitingPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-transition-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Transition Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            preview.BlockNextReset();

            var simulate = ExecuteAsync(viewModel.SimulateCommand);
            await preview.WaitForBlockedResetAsync();

            Assert.True(viewModel.IsBusy);
            Assert.False(viewModel.PlayCommand.CanExecute(null));
            Assert.False(viewModel.SimulateCommand.CanExecute(null));
            Assert.False(viewModel.StopCommand.CanExecute(null));

            preview.ReleaseBlockedReset();
            await simulate;
            Assert.Equal(RekallAgeStudioMode.Simulate, viewModel.Mode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RepeatedDisposeAwaitsTheSameInProgressShutdown()
    {
        var preview = new RecordingPreviewSession();
        preview.BlockDispose();
        var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            new EmptyModel(),
            preview);

        var first = viewModel.DisposeAsync().AsTask();
        await preview.WaitForDisposeAsync();
        var second = viewModel.DisposeAsync().AsTask();

        Assert.Same(first, second);
        Assert.False(second.IsCompleted);

        preview.ReleaseDispose();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public void StudioShellRequiresSharedDarkControlsAndVisibleModeAffordances()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var app = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "App.xaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));

        Assert.Contains("Property=\"FontFamily\" Value=\"Segoe UI\"", app, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type Button}\"", app, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type TextBox}\"", app, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ComboBox}\"", app, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ListBox}\"", app, StringComparison.Ordinal);
        Assert.Contains("SimulateCommand", window, StringComparison.Ordinal);
        Assert.Contains("IsLiveViewportEnabled", window, StringComparison.Ordinal);
        Assert.Contains("ModeLabel", window, StringComparison.Ordinal);
    }
    [Fact]
    public void StudioRejectsLowCoverageAdvisoryAsTaskSpecificVisualProof()
    {
        var analysis = new RekallAgeViewportFrameAnalysis(
            true,
            true,
            100,
            100,
            5,
            0.96,
            1,
            0.2,
            0.1,
            ["REKALL_VIEWPORT_LOW_VISUAL_COVERAGE"]);

        Assert.False(RekallAgeStudioViewModel.IsStudioVisualProofAcceptable(analysis));
        Assert.True(RekallAgeStudioViewModel.IsStudioVisualProofAcceptable(
            analysis with { DominantColorRatio = 0.7, WarningCodes = [] }));
    }

    [Theory]
    [InlineData("REKALL_VIEWPORT_CAMERA_FACES_AWAY_FROM_CONTENT")]
    [InlineData("REKALL_VIEWPORT_UI_LARGE_COVERAGE")]
    public void StudioRejectsBlockingLayoutWarningsAsTaskSpecificVisualProof(string warningCode)
    {
        var informative = new RekallAgeViewportFrameAnalysis(
            true,
            true,
            100,
            100,
            12,
            0.7,
            0.3,
            0.5,
            0.2,
            []);

        Assert.False(RekallAgeStudioViewModel.IsStudioVisualProofAcceptable(
            informative,
            [warningCode]));
    }

    [Fact]
    public void AutomationRejectsANonInformativeViewportEvenWhenItIsNonblankAndPackaged()
    {
        var archive = Path.GetTempFileName();
        try
        {
            Assert.False(RekallAgeStudioAutomation.IsSuccessful(
                "AI authoring completed with evidence.",
                nonblankViewport: true,
                visuallyInformativeViewport: false,
                requireVisuallyInformativeViewport: true,
                archive));
            Assert.True(RekallAgeStudioAutomation.IsSuccessful(
                "AI authoring completed with evidence.",
                nonblankViewport: true,
                visuallyInformativeViewport: true,
                requireVisuallyInformativeViewport: true,
                archive));
        }
        finally
        {
            File.Delete(archive);
        }
    }

    [Fact]
    public void StudioAutomationLeavesTurnLimitDisabledWhenItIsNotRequested()
    {
        Assert.True(RekallAgeStudioAutomation.TryParse(
            [
                "--studio-agent-automation",
                "--project", "C:\\Game",
                "--project-name", "Game",
                "--model", "model",
                "--task", "Create a game",
                "--evidence", "C:\\Evidence\\result.json"
            ],
            out var options,
            out var error), error);

        Assert.Equal(default(int?), (int?)options!.MaxTurns);
    }

    [Fact]
    public async Task HeadlessOpenAiAutomationStopsAtTheStableCredentialGateAndWritesEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-openai-gate-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(root, "evidence", "result.json");
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = " " },
            () => new HttpClient(new ProviderLifecycleHandler(blockOllamaChat: false), disposeHandler: true));
        try
        {
            var result = await RekallAgeStudioAutomation.RunWithCatalogAsync(
                new RekallAgeStudioAutomationOptions(
                    root,
                    "Credential Gate",
                    "Main",
                    "gpt-5.6-sol",
                    "Author a game.",
                    evidence)
                {
                    Provider = "openai"
                },
                catalog,
                CancellationToken.None);

            const string expected = "REKALL_OPENAI_API_KEY_MISSING: OpenAI requires OPENAI_API_KEY for this session.";
            Assert.False(result.Succeeded);
            Assert.Equal(expected, result.Status);
            Assert.False(File.Exists(Path.Combine(root, "rekall.project.json")));
            Assert.True(File.Exists(evidence));
            Assert.Contains(expected, await File.ReadAllTextAsync(evidence), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StudioStartsWithAnEmptyOrdinaryLanguageAuthoringRequest()
    {
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            new EmptyModel());

        Assert.Empty(viewModel.AgentTaskInput);
        Assert.False(viewModel.RunAgentCommand.CanExecute(null));
    }

    [Fact]
    public void AutomationArgumentsRequireExplicitBoundedInputs()
    {
        var parsed = RekallAgeStudioAutomation.TryParse(
            [
                RekallAgeStudioAutomation.AutomationSwitch,
                "--project", "game",
                "--project-name", "Game",
                "--scene", "Main",
                "--provider", "openai",
                "--model", "model",
                "--task", "Author a game",
                "--evidence", "evidence.json",
                "--max-turns", "40",
                "--require-task-specific-completion"
            ],
            out var options,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal("game", options!.ProjectRoot);
        Assert.Equal("openai", options.Provider);
        Assert.False(options.TreatGauntletAsTerminalSuccess);
        Assert.Equal(40, options.MaxTurns);
        Assert.False(RekallAgeStudioAutomation.TryParse(
            [RekallAgeStudioAutomation.AutomationSwitch, "--project", "game"],
            out _, out var missing));
        Assert.Contains("--model", missing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HeadlessAutomationCreatesProjectAndCompletesAgentGauntlet()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-agent-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(root + "-evidence", "studio-agent.json");
        try
        {
            var result = await RekallAgeStudioAutomation.RunAsync(
                new RekallAgeStudioAutomationOptions(root, "Automated Agent Game", "Main", "deterministic", "Author and prove a playable game.", evidence),
                new GauntletModel(root),
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Status + Environment.NewLine + result.ViewportSummary + Environment.NewLine + string.Join(Environment.NewLine, result.AgentTranscript));
            Assert.True(result.NonblankViewport);
            Assert.True(result.ViewportRenderableCount > 0);
            Assert.NotEmpty(result.AgentToolExecutions);
            Assert.True(File.Exists(result.PackageArchivePath));
            Assert.True(File.Exists(evidence));
            Assert.Contains("provider: deterministic", result.AgentTranscript);
            Assert.Contains("model: deterministic", result.AgentTranscript);
            Assert.Contains(result.AgentTranscript, line => line.StartsWith("response: deterministic-response-", StringComparison.Ordinal));
            Assert.Contains("usage: input=200 output=20 cached=unavailable reasoning=unavailable", result.AgentTranscript);
            Assert.Contains("tools: 2", result.AgentTranscript);
            Assert.Contains(result.AgentTranscript, line => line.StartsWith("elapsed: ", StringComparison.Ordinal) && line.EndsWith(" ms", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            var evidenceRoot = Path.GetDirectoryName(evidence)!;
            if (Directory.Exists(evidenceRoot)) Directory.Delete(evidenceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task HeadlessAutomationDoesNotCallAnEmptyDebugFrameNonblank()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-empty-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(root + "-evidence", "studio-agent.json");
        try
        {
            var result = await RekallAgeStudioAutomation.RunAsync(
                new RekallAgeStudioAutomationOptions(root, "Empty", "Main", "deterministic", "Inspect only.", evidence)
                {
                    TreatGauntletAsTerminalSuccess = false,
                    MaxTurns = 2
                },
                new EmptyModel(),
                CancellationToken.None);

            Assert.Equal(0, result.ViewportRenderableCount);
            Assert.False(result.NonblankViewport);
            Assert.False(result.VisuallyInformativeViewport);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            var evidenceRoot = Path.GetDirectoryName(evidence)!;
            if (Directory.Exists(evidenceRoot)) Directory.Delete(evidenceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AgentPreservesCompletedToolEvidenceWhenALaterModelTurnFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-partial-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var registry = RekallAgeDefaultCommandRegistry.Create();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(registry),
                new FailsAfterToolModel());
            viewModel.ProjectPathInput = root;
            viewModel.ProjectNameInput = "Partial Evidence";
            viewModel.SceneNameInput = "Main";
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.AgentTaskInput = "Inspect the engine, then continue.";

            await ExecuteAsync(viewModel.RunAgentCommand);

            var execution = Assert.Single(viewModel.LastAgentToolExecutions);
            Assert.Equal("rekall.context.engine_status", execution.Name);
            Assert.True(execution.Succeeded);
            Assert.Contains("REKALL_STUDIO_UNEXPECTED_FAILURE", viewModel.ValidationLines.Single(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HeadlessAutomationContinuesAnExistingStudioProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-existing-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(root + "-evidence", "studio-agent.json");
        try
        {
            await using (var setup = new RekallAgeStudioViewModel())
            {
                setup.ProjectPathInput = root;
                setup.ProjectNameInput = "Existing Game";
                setup.SceneNameInput = "Main";
                await ExecuteAsync(setup.CreateCommand);
            }

            var result = await RekallAgeStudioAutomation.RunAsync(
                new RekallAgeStudioAutomationOptions(root, "Must Not Replace Existing Game", "Main", "deterministic", "Inspect the existing game.", evidence)
                {
                    TreatGauntletAsTerminalSuccess = true,
                    MaxTurns = 2
                },
                new EmptyModel(),
                CancellationToken.None);

            Assert.StartsWith("AI authoring completed", result.Status, StringComparison.Ordinal);
            Assert.True(File.Exists(evidence));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            var evidenceRoot = Path.GetDirectoryName(evidence)!;
            if (Directory.Exists(evidenceRoot)) Directory.Delete(evidenceRoot, recursive: true);
        }
    }

    [Fact]
    public void AutomationFindsNestedAgentAuthoredPackageOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            var nested = Path.Combine(root, "Output", "Packages");
            Directory.CreateDirectory(nested);
            var archive = Path.Combine(nested, "EchoFoundry.zip");
            File.WriteAllText(archive, "package");

            Assert.Equal(archive, RekallAgeStudioAutomation.ResolvePackageArchivePath(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ViewModelCreatesAndEditsProjectThroughSchemaGuidedCanonicalCommands()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-vm-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel();
            viewModel.ProjectPathInput = root;
            viewModel.ProjectNameInput = "Automated Studio Game";
            viewModel.SceneNameInput = "Main";

            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);

            Assert.Contains(viewModel.ComponentSchemas, schema => schema.Type == "Rekall.Transform2D");
            viewModel.ComponentTypeInput = "Rekall.Transform2D";
            await ExecuteAsync(viewModel.AddComponentCommand);
            viewModel.PropertyNameInput = "x";
            viewModel.PropertyValueInput = "12.5";
            Assert.Equal("number", viewModel.SelectedPropertySchema?.EditorKind);
            await ExecuteAsync(viewModel.SetPropertyCommand);

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var entity = Assert.Single(scene.Entities);
            var transform = Assert.Single(entity.Components, component => component.Type == "Rekall.Transform2D");
            Assert.Equal(12.5, transform.Properties["x"]!.GetValue<double>());

            await ExecuteAsync(viewModel.UndoCommand);
            scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            transform = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.Transform2D");
            Assert.False(transform.Properties.ContainsKey("x"));

            await ExecuteAsync(viewModel.RedoCommand);
            scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            transform = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.Transform2D");
            Assert.Equal(12.5, transform.Properties["x"]!.GetValue<double>());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static Task ExecuteAsync(System.Windows.Input.ICommand command) =>
        ((RekallAgeAsyncCommand)command).ExecuteAsync(null);

    private static async Task CreateQualityProjectAsync(string root)
    {
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Studio Quality", ["world", "rendering3d"]),
            default);
        var quality = RekallAgeEntityDocument.Create("Quality", ["rendering"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.RenderQualityProfile",
                new JsonObject { ["preset"] = "High" }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(quality),
            default);
    }

    private static RekallAgeQualityPresetCapture PartialQualityCapture(bool nonBlank = false) => new(
        "High",
        "High",
        1,
        "D:/captures/partial-high.png",
        nonBlank,
        320,
        180,
        320,
        180,
        4096,
        1,
        0,
        [],
        new RekallAgeGpuFrameTimingReport(
            true,
            null,
            1,
            [new RekallAgeGpuPassTiming("forward", 2_000_000, 2)],
            2_500_000,
            2.5,
            "vulkan-timestamp-query"),
        RekallAgeViewportFrameAnalysis.NotAnalyzed);

    private sealed class PartialQualityComparisonCommand
        : IRekallAgeCommand<CompareQualityPresetsRequest, CompareQualityPresetsResult>
    {
        public string Name => "rekall.render.compare_quality_presets";
        public RekallAgeCommandSchema Schema => new(
            Name,
            "Returns one usable comparison capture followed by a deterministic failure.",
            typeof(CompareQualityPresetsRequest).FullName!,
            typeof(CompareQualityPresetsResult).FullName!);

        public ValueTask<RekallAgeCommandResult<CompareQualityPresetsResult>> ExecuteAsync(
            CompareQualityPresetsRequest request,
            RekallAgeCommandContext context)
        {
            var value = new CompareQualityPresetsResult(
                request.SceneName,
                1,
                [PartialQualityCapture()],
                ["command execute rekall.render.performance.inspect_scene_budget"]);
            var error = new RekallAgeCommandError(
                "REKALL_TEST_PARTIAL_COMPARISON",
                "The second requested preset failed after the first capture completed.",
                "Performance");
            return ValueTask.FromResult(RekallAgeCommandResult<CompareQualityPresetsResult>.Failure(
                value,
                error.Message,
                [error]));
        }
    }

    private sealed class CancellationBlockingCaptureCommand
        : IRekallAgeCommand<CaptureRuntimeViewportRequest, CaptureRuntimeViewportResult>
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "rekall.render.capture_runtime_viewport";
        public RekallAgeCommandSchema Schema => new(
            Name,
            "Waits deterministically for lifecycle cancellation.",
            typeof(CaptureRuntimeViewportRequest).FullName!,
            typeof(CaptureRuntimeViewportResult).FullName!);

        public async ValueTask<RekallAgeCommandResult<CaptureRuntimeViewportResult>> ExecuteAsync(
            CaptureRuntimeViewportRequest request,
            RekallAgeCommandContext context)
        {
            _started.TrySetResult();
            using var registration = context.CancellationToken.Register(() => _cancellationObserved.TrySetResult());
            var signal = await Task.WhenAny(_cancellationObserved.Task, _release.Task);
            if (ReferenceEquals(signal, _cancellationObserved.Task))
            {
                await _release.Task;
                context.CancellationToken.ThrowIfCancellationRequested();
            }

            var error = new RekallAgeCommandError("REKALL_TEST_CAPTURE_RELEASED", "The deterministic capture was released by its test.");
            return RekallAgeCommandResult<CaptureRuntimeViewportResult>.Failure(
                default!,
                error.Message,
                [error]);
        }

        public Task WaitForStartAsync() => _started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        public Task WaitForCancellationAsync() => _cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        public void Release() => _release.TrySetResult();
    }

    private sealed class ProviderLifecycleHandler(
        bool blockOllamaChat,
        bool blockOllamaModels = false) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _chatStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _modelsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<string> _events = new();

        public IReadOnlyList<string> Events => _events.ToArray();

        public Task WaitForChatAsync() => _chatStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        public Task WaitForModelsAsync() => _modelsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/tags", StringComparison.Ordinal))
            {
                _modelsStarted.TrySetResult();
                if (blockOllamaModels)
                {
                    var cancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    var completed = await Task.WhenAny(cancellation, _disposed.Task);
                    if (ReferenceEquals(completed, cancellation))
                    {
                        _events.Enqueue("models-cancelled");
                        await cancellation;
                    }

                    throw new OperationCanceledException("The provider lease was disposed before model refresh cancellation was observed.");
                }
                return JsonResponse("""
                    {"models":[{"name":"qwen3.5:35b","size":24000000000},{"name":"gemma3:latest","size":3000}]}
                    """);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/api/chat", StringComparison.Ordinal))
            {
                _chatStarted.TrySetResult();
                if (blockOllamaChat)
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _events.Enqueue("chat-cancelled");
                        throw;
                    }
                }

                return JsonResponse("""
                    {"model":"qwen3.5:35b","message":{"role":"assistant","content":"complete"},"done":true,"done_reason":"stop","total_duration":1,"prompt_eval_count":1,"eval_count":1}
                    """);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/models", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {"data":[{"id":"gpt-5.6-sol-preview"},{"id":"gpt-5.6-sol"}]}
                    """);
            }

            throw new InvalidOperationException($"Unexpected provider request: {request.Method} {request.RequestUri.AbsolutePath}");
        }

        protected override void Dispose(bool disposing)
        {
            _events.Enqueue("lease-disposed");
            _disposed.TrySetResult();
            base.Dispose(disposing);
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class GauntletModel(string projectRoot) : IRekallAgeLanguageModelClient
    {
        private int _calls;
        public string ProviderId => "deterministic";

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls) == 1
                ? new RekallAgeLanguageModelToolCall("rekall.context.engine_status", new JsonObject())
                : new RekallAgeLanguageModelToolCall(
                    "rekall.workflow.agent_authoring_gauntlet",
                    new JsonObject
                    {
                        ["projectRoot"] = projectRoot,
                        ["projectName"] = "Automated Agent Game",
                        ["sceneName"] = "Main"
                    });
            return ValueTask.FromResult(new RekallAgeLanguageModelResponse(
                ProviderId,
                request.Model,
                string.Empty,
                "Run the complete generic proof.",
                [call],
                "tool_calls",
                new RekallAgeLanguageModelUsage(100, 10, 1))
            {
                ResponseId = $"deterministic-response-{_calls}"
            });
        }
    }

    private sealed class RecordingPreviewSession : IRekallAgeStudioPreviewSession
    {
        private int _frame;
        private TaskCompletionSource? _blockedReset;
        private TaskCompletionSource? _resetEntered;
        private TaskCompletionSource? _disposeBlocked;
        private TaskCompletionSource? _disposeEntered;
        public int ResetCount { get; private set; }
        public int StepCount { get; private set; }
        public List<RekallAgeStudioViewportPickRegion> Regions { get; } = [];

        public ValueTask<RekallAgeStudioPreviewFrame> ResetAsync(
            string projectRoot,
            string sceneName,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            ResetCount++;
            _frame = 0;
            _resetEntered?.TrySetResult();
            return _blockedReset is null
                ? ValueTask.FromResult(CreateFrame(_frame))
                : AwaitBlockedResetAsync(_blockedReset, cancellationToken);
        }

        public ValueTask<RekallAgeStudioPreviewFrame> StepAsync(int frameCount, CancellationToken cancellationToken)
        {
            StepCount++;
            _frame += frameCount;
            return ValueTask.FromResult(CreateFrame(_frame));
        }

        public ValueTask DisposeAsync()
        {
            _disposeEntered?.TrySetResult();
            return _disposeBlocked is null ? ValueTask.CompletedTask : new ValueTask(_disposeBlocked.Task);
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken)
        {
            _frame = 0;
            return ValueTask.CompletedTask;
        }

        public void BlockNextReset()
        {
            _blockedReset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _resetEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForBlockedResetAsync() => _resetEntered?.Task.WaitAsync(TimeSpan.FromSeconds(5))
            ?? Task.CompletedTask;

        public void ReleaseBlockedReset() => _blockedReset?.TrySetResult();

        public void BlockDispose()
        {
            _disposeBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForDisposeAsync() => _disposeEntered?.Task.WaitAsync(TimeSpan.FromSeconds(5))
            ?? Task.CompletedTask;

        public void ReleaseDispose() => _disposeBlocked?.TrySetResult();

        private async ValueTask<RekallAgeStudioPreviewFrame> AwaitBlockedResetAsync(
            TaskCompletionSource blockedReset,
            CancellationToken cancellationToken)
        {
            await blockedReset.Task.WaitAsync(cancellationToken);
            _blockedReset = null;
            return CreateFrame(_frame);
        }

        private RekallAgeStudioPreviewFrame CreateFrame(int frame)
        {
            var image = BitmapSource.Create(
                100, 100, 96, 96, PixelFormats.Bgra32, null, new byte[40_000], 400);
            image.Freeze();
            return new RekallAgeStudioPreviewFrame(
                image, frame, 0, 0, "software-live",
                new RekallAgeStudioViewportInteractionSnapshot(100, 100, Regions));
        }
    }

    private sealed class EmptyModel : IRekallAgeLanguageModelClient
    {
        public string ProviderId => "deterministic";

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new RekallAgeLanguageModelResponse(
                ProviderId,
                request.Model,
                "No content authored.",
                string.Empty,
                [],
                "stop",
                new RekallAgeLanguageModelUsage(1, 1, 1)));
    }

    private sealed class FailsAfterToolModel : IRekallAgeLanguageModelClient
    {
        private int _calls;
        public string ProviderId => "deterministic";

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) > 1)
            {
                throw new InvalidDataException("simulated later model failure");
            }

            return ValueTask.FromResult(new RekallAgeLanguageModelResponse(
                ProviderId,
                request.Model,
                string.Empty,
                string.Empty,
                [new RekallAgeLanguageModelToolCall("rekall.context.engine_status", new JsonObject())],
                "tool_calls",
                new RekallAgeLanguageModelUsage(1, 1, 1)));
        }
    }
}
