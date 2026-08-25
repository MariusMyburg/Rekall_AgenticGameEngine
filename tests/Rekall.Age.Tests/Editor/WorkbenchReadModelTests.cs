using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor;
using Rekall.Age.Project;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Editor;

public sealed class WorkbenchReadModelTests
{
    [Fact]
    public async Task WorkbenchKeepsAuthoredQualitySeparateFromUnavailableRuntimeResolution()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Quality Workbench", ["world", "rendering3d"]),
            CancellationToken.None);
        var qualityEntity = RekallAgeEntityDocument.Create("Render Settings", ["rendering"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.RenderQualityProfile",
                new JsonObject
                {
                    ["preset"] = "Epic",
                    ["resolutionScale"] = 0.85,
                    ["shadowCascadeCount"] = 4,
                    ["enableGpuTimestamps"] = true
                }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(qualityEntity),
            CancellationToken.None);

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        Assert.Equal(qualityEntity.Id, model.Rendering.Authoring?.EntityId);
        Assert.Equal("Epic", model.Rendering.Authoring?.Preset);
        Assert.Equal(0.85, model.Rendering.Authoring?.ResolutionScale);
        Assert.Equal("Epic", model.Rendering.Runtime.RequestedPreset);
        Assert.Null(model.Rendering.Runtime.ResolvedPreset);
        Assert.Null(model.Rendering.Runtime.TotalGpuMilliseconds);
        Assert.Equal("Unavailable", model.Rendering.Runtime.TotalGpuMillisecondsText);
        Assert.Equal("REKALL_GPU_TIMESTAMPS_UNAVAILABLE", model.Rendering.Runtime.GpuTimingCode);
        Assert.Contains(model.Actions.Actions, action =>
            action.Tool == "rekall.render.compare_quality_presets" && action.Recommended);
        Assert.Contains(model.Actions.Actions, action =>
            action.Tool == "rekall.render.performance.inspect_scene_budget" && action.Recommended);
    }

    [Fact]
    public void WorkbenchRenderingRuntimePreservesRequestedResolvedFactsAndOrderedGpuTiming()
    {
        var degradation = new RekallAgeRenderFeatureDegradation(
            "REKALL_RENDER_FEATURE_DEVICE_CLAMPED",
            "shadowResolution",
            "16384",
            "8192",
            "The requested shadow resolution was clamped by device limits.");
        var plan = new RekallAgeResolvedRenderFeaturePlan(
            "Cinematic",
            "High",
            1920,
            1080,
            1440,
            810,
            0.75,
            new RekallAgeResolvedShadowQuality(3, 8192, 12),
            new RekallAgeResolvedFogQuality("froxel", 160, 90, 48),
            new RekallAgeResolvedPostQuality(true, true, true),
            new RekallAgeResolvedParticleQuality(64000),
            12_345_678,
            87_654_321,
            [degradation]);
        var timings = new RekallAgeGpuFrameTimingReport(
            true,
            null,
            12,
            [
                new RekallAgeGpuPassTiming("shadow", 1_250_000, 1.25),
                new RekallAgeGpuPassTiming("forward", 2_500_000, 2.5)
            ],
            4_000_000,
            4,
            "vulkan-timestamp-query");

        var runtime = RekallAgeWorkbenchModelBuilder.BuildRenderingRuntime(
            plan,
            timings,
            123_456_789,
            17,
            3,
            ["command execute rekall.render.capture_runtime_viewport"]);

        Assert.Equal("Cinematic", runtime.RequestedPreset);
        Assert.Equal("High", runtime.ResolvedPreset);
        Assert.Equal(1440, runtime.RenderWidth);
        Assert.Equal(810, runtime.RenderHeight);
        Assert.Equal(4, runtime.TotalGpuMilliseconds);
        Assert.Equal("4.000 ms", runtime.TotalGpuMillisecondsText);
        Assert.Equal(["shadow", "forward"], runtime.PassTimings.Select(pass => pass.Name));
        Assert.Equal([1.25, 2.5], runtime.PassTimings.Select(pass => pass.Milliseconds));
        Assert.Contains(runtime.Resources, resource => resource.Name == "Frame resources" && resource.Bytes == 123_456_789);
        Assert.Contains(runtime.Resources, resource => resource.Name == "Planned transient" && resource.Bytes == 12_345_678);
        Assert.Contains(runtime.Resources, resource => resource.Name == "Planned persistent" && resource.Bytes == 87_654_321);
        var presentedDegradation = Assert.Single(runtime.Degradations);
        Assert.Equal(degradation.Code, presentedDegradation.Code);
        Assert.Equal("16384", presentedDegradation.RequestedValue);
        Assert.Equal("8192", presentedDegradation.ResolvedValue);
        Assert.Equal(17, runtime.DrawCount);
        Assert.Equal(3, runtime.DispatchCount);
    }

    [Fact]
    public void WorkbenchRenderingRuntimeNeverFormatsUnavailableGpuTimingAsZero()
    {
        var plan = new RekallAgeResolvedRenderFeaturePlan(
            "Low",
            "Low",
            320,
            180,
            214,
            121,
            0.67,
            new RekallAgeResolvedShadowQuality(1, 1024, 4),
            new RekallAgeResolvedFogQuality("analytic"),
            new RekallAgeResolvedPostQuality(true, false, false),
            new RekallAgeResolvedParticleQuality(8000),
            100,
            200,
            []);

        var runtime = RekallAgeWorkbenchModelBuilder.BuildRenderingRuntime(
            plan,
            RekallAgeGpuFrameTimingReport.Unavailable(7),
            300,
            2,
            0,
            []);

        Assert.False(runtime.GpuTimingAvailable);
        Assert.Null(runtime.TotalGpuMilliseconds);
        Assert.Equal("Unavailable", runtime.TotalGpuMillisecondsText);
        Assert.DoesNotContain("0", runtime.TotalGpuMillisecondsText, StringComparison.Ordinal);
        Assert.Empty(runtime.PassTimings);
    }

    [Fact]
    public async Task WorkbenchQualityComparisonUsesExactCommandResultPathsAndDegradationFacts()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Quality Comparison", ["world"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]),
            CancellationToken.None);
        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);
        var degradation = new RekallAgeRenderFeatureDegradation(
            "REKALL_RENDER_QUALITY_OVERRIDE_CLAMPED",
            "resolutionScale",
            "0.01",
            "0.25",
            "The caller override was clamped.");
        var unavailable = RekallAgeGpuFrameTimingReport.Unavailable(3);
        var comparison = new CompareQualityPresetsResult(
            "Main",
            3,
            [
                new RekallAgeQualityPresetCapture(
                    "Performance",
                    "Performance",
                    3,
                    "D:/captures/performance.png",
                    true,
                    320,
                    180,
                    80,
                    45,
                    1234,
                    7,
                    1,
                    [degradation],
                    unavailable,
                    RekallAgeViewportFrameAnalysis.NotAnalyzed),
                new RekallAgeQualityPresetCapture(
                    "Epic",
                    "High",
                    3,
                    "D:/captures/epic.png",
                    true,
                    320,
                    180,
                    320,
                    180,
                    5678,
                    11,
                    2,
                    [],
                    new RekallAgeGpuFrameTimingReport(
                        true,
                        null,
                        3,
                        [new RekallAgeGpuPassTiming("forward", 2_000_000, 2)],
                        2_500_000,
                        2.5,
                        "vulkan-timestamp-query"),
                    RekallAgeViewportFrameAnalysis.NotAnalyzed)
            ],
            ["command execute rekall.render.capture_runtime_viewport"]);

        var mapped = RekallAgeWorkbenchModelBuilder.WithQualityComparisonResult(model, comparison);

        Assert.Equal(
            ["D:/captures/performance.png", "D:/captures/epic.png"],
            mapped.Rendering.Comparisons.Select(item => item.ScreenshotPath));
        Assert.Equal("Performance", mapped.Rendering.Comparisons[0].RequestedPreset);
        Assert.Equal("Performance", mapped.Rendering.Comparisons[0].ResolvedPreset);
        Assert.Equal("Unavailable", mapped.Rendering.Comparisons[0].TotalGpuMillisecondsText);
        Assert.Equal("Epic", mapped.Rendering.Comparisons[1].RequestedPreset);
        Assert.Equal("High", mapped.Rendering.Comparisons[1].ResolvedPreset);
        Assert.Equal("2.500 ms", mapped.Rendering.Comparisons[1].TotalGpuMillisecondsText);
        var mappedDegradation = Assert.Single(mapped.Rendering.Comparisons[0].Degradations);
        Assert.Equal(degradation.Code, mappedDegradation.Code);
        Assert.Equal("0.01", mappedDegradation.RequestedValue);
        Assert.Equal("0.25", mappedDegradation.ResolvedValue);
        Assert.Equal(comparison.NextCommands, mapped.Rendering.Runtime.SuggestedActions);
    }

    [Fact]
    public async Task WorkbenchRemainsInspectableWhenAnAuthoredModuleNeedsRepair()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Repairable", ["world", "modules"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]),
            CancellationToken.None);
        var moduleRoot = Path.Combine(root, "Modules", "BrokenModule");
        Directory.CreateDirectory(moduleRoot);
        await File.WriteAllTextAsync(
            Path.Combine(moduleRoot, "BrokenModule.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        Assert.Contains(model.Inspector.AvailableComponents, component => component.Type == "Rekall.Transform3D");
        Assert.Contains(model.Diagnostics.Issues, issue => issue.Code == "REKALL_MODULE_RECEIPT_MISSING" && issue.Severity == "blocking");
    }

    [Fact]
    public async Task WorkbenchModelUsesStableIdsAndInspectorProperties()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Crystal Mines", ["world", "rendering2d"]),
            CancellationToken.None);

        var sceneStore = new RekallAgeSceneStore();
        var player = RekallAgeEntityDocument.Create("Player", ["player"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform2D",
                new JsonObject
                {
                    ["x"] = 4,
                    ["y"] = 8
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SpriteRenderer",
                new JsonObject { ["sprite"] = "asset_player_12345678" }));
        await sceneStore.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"]).AddEntity(player),
            CancellationToken.None);

        var assetStore = new RekallAgeAssetCatalogStore();
        await assetStore.SaveAsync(
            root,
            RekallAgeAssetCatalogDocument.Empty.AddOrReplace(new RekallAgeAssetDocument(
                "asset_player_12345678",
                "player",
                "Player",
                "sprite",
                "source.png",
                "Assets/sprite/asset_player_12345678.png",
                "1234567890abcdef")),
            CancellationToken.None);

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        Assert.Equal("Crystal Mines", model.Project.Name);
        Assert.Equal("Main", model.Scene.Name);
        var node = Assert.Single(model.Scene.RootEntities);
        Assert.Equal(player.Id, node.EntityId);
        Assert.Equal("Player", node.Name);
        var transform = model.Inspector.Components.Single(component => component.Type == "Rekall.Transform2D");
        Assert.Contains(transform.Properties, property => property.Name == "x" && property.Value == "4");
        Assert.Equal("Transform 2D", transform.DisplayName);
        Assert.True(transform.SchemaKnown);
        var x = Assert.Single(transform.Properties, property => property.Name == "x");
        Assert.Equal("number", x.EditorKind);
        Assert.True(x.IsDefined);
        var rotation = Assert.Single(transform.Properties, property => property.Name == "rotation");
        Assert.Equal("number", rotation.EditorKind);
        Assert.False(rotation.IsDefined);
        Assert.Contains(
            model.Inspector.AvailableComponents,
            component => component.Type == "Rekall.AudioEmitter"
                && component.Properties.Any(property => property.Name == "clip" && property.EditorKind == "assetRef" && property.AssetKind == "audio"));
        Assert.Equal("asset_player_12345678", Assert.Single(model.Assets.Assets).AssetId);
        Assert.Contains(model.Diagnostics.Issues, issue => issue.Code == "REKALL_CAMERA_MISSING");
        Assert.Equal("Main", model.Runtime.SceneName);
        Assert.Equal(0, model.Runtime.FrameIndex);
        Assert.Equal(1, model.Runtime.EntityCount);
        Assert.Equal(1, model.Runtime.RenderableCount);
        Assert.Null(model.Runtime.ActiveCameraName);
        Assert.Equal("rekall.render.capture_runtime_viewport", model.Runtime.ViewportCaptureTool);
        Assert.DoesNotContain(model.Runtime.Observations, observation => observation.Severity == "blocking");
        Assert.Equal(1, model.SceneSummary.EntityCount);
        Assert.Equal(2, model.SceneSummary.ComponentCount);
        Assert.Contains(model.SceneSummary.ComponentTypes, component => component.Type == "Rekall.SpriteRenderer" && component.Count == 1);
        Assert.Contains(model.Actions.Actions, action => action.Tool == "rekall.validation.scene" && action.Recommended);
        Assert.Contains(model.Actions.Actions, action => action.Tool == "rekall.render.capture_runtime_viewport" && action.Recommended);
        Assert.Contains(model.Actions.Actions, action => action.Tool == "rekall.asset.tripo.generate_model" && action.Recommended);
    }

    [Fact]
    public async Task WorkbenchRuntimePanelReportsActiveCameraAndViewportCaptureTool()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Viewport Project", ["world", "rendering2d"]),
            CancellationToken.None);

        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
                .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 2, ["y"] = 3 }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" }))),
            CancellationToken.None);

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        Assert.Equal("MainCamera", model.Runtime.ActiveCameraName);
        Assert.Equal("rekall.render.capture_runtime_viewport", model.Runtime.ViewportCaptureTool);
        Assert.Equal(2, model.Runtime.EntityCount);
        Assert.Equal(2, model.Runtime.RenderableCount);
    }

    [Fact]
    public async Task WorkbenchModelBuildsGenericSceneSummaryAndActionPalette()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Workbench 2", ["world", "rendering3d", "modules"]),
            CancellationToken.None);

        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Generated Mesh", ["model", "generated"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer", new JsonObject { ["mesh"] = "mesh-1" }))),
            CancellationToken.None);

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        Assert.Equal(2, model.SceneSummary.EntityCount);
        Assert.Equal(2, model.SceneSummary.RootEntityCount);
        Assert.Equal(3, model.SceneSummary.ComponentCount);
        Assert.Contains(model.SceneSummary.Tags, tag => tag == "generated");
        Assert.Equal("Rekall.Camera3D", model.SceneSummary.ComponentTypes[0].Type);
        Assert.All(model.Actions.Actions, action => Assert.StartsWith("rekall.", action.Tool, StringComparison.Ordinal));
        Assert.Contains(model.Actions.Actions, action => action.Id == "inspect-runtime" && action.Tool == "rekall.runtime.inspect_scene");
        Assert.Contains(model.Actions.Actions, action => action.Id == "build-modules" && action.Tool == "rekall.build.modules");
        Assert.Contains(model.Actions.Actions, action => action.Id == "agent-authoring-gauntlet" && action.Tool == "rekall.workflow.agent_authoring_gauntlet");
    }

    [Fact]
    public async Task WorkbenchModelLoadsPersistedTransactionHistory()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Transaction Project", ["world"]),
            CancellationToken.None);

        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("create scene through command"),
            CancellationToken.None);
        var createScene = await new CreateSceneCommand().ExecuteAsync(
            new CreateSceneRequest(root, "Main", ["world"]),
            context);
        Assert.True(createScene.Ok, createScene.Summary);

        await new RekallAgeTransactionLogStore().AppendAsync(
            root,
            context.Transaction,
            context.Actor,
            CancellationToken.None);

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        var transaction = Assert.Single(model.Transactions.Transactions);
        Assert.Equal(context.Transaction.Id, transaction.Id);
        Assert.Equal("create scene through command", transaction.Name);
        Assert.Contains(context.Transaction.ChangedResources.Single(), transaction.ChangedResources);
    }
}
