using System.IO;
using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Windows;
using Rekall.Age.Studio;
using Rekall.Age.World;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioPreviewSessionTests
{
    [Fact]
    public async Task InitialEditPreviewBuildsPickableInteractionAndPresentsVulkanHardwareTelemetry()
    {
        var root = TemporaryRoot("studio-preview-ui");
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
            await SaveSceneAsync(root, RekallAgeSceneDocument.Create("Main", ["ui"]).AddEntity(canvas).AddEntity(label));
            var presenter = new RecordingViewportPresenter();
            await using var preview = new RekallAgeStudioVulkanPreviewSession(presenter);

            var initial = await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);

            Assert.True(initial.Presentation.PresentedFrame);
            Assert.Equal("vulkan", initial.Backend);
            Assert.True(initial.HardwareAccelerated);
            Assert.Equal(0, initial.FrameIndex);
            Assert.Equal(1, initial.RenderableCount);
            Assert.Equal(label.Id, initial.Interaction.Pick(10, 10));
            Assert.Single(presenter.Presentations);
            Assert.Equal(320, presenter.Presentations[0].Frame.Width);
            Assert.Equal(180, presenter.Presentations[0].Frame.Height);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task StepsReuseCachedAssetsAndPersistentPresenter()
    {
        var root = TemporaryRoot("studio-preview-cache");
        try
        {
            await SaveSceneAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]));
            var presenter = new RecordingViewportPresenter();
            var resolveCount = 0;
            await using var preview = new RekallAgeStudioVulkanPreviewSession(
                presenter,
                (_, _, _) =>
                {
                    resolveCount++;
                    return ValueTask.FromResult(RekallAgeRuntimeViewportAssetSet.Empty with { });
                });

            var initial = await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);
            var first = await preview.StepAsync(1, CancellationToken.None);
            var seventh = await preview.StepAsync(6, CancellationToken.None);

            Assert.Equal(0, initial.FrameIndex);
            Assert.Equal(1, first.FrameIndex);
            Assert.Equal(7, seventh.FrameIndex);
            Assert.Equal(1, resolveCount);
            Assert.Equal(0, presenter.AssetInvalidationCount);
            Assert.Equal(0, presenter.ShaderInvalidationCount);
            Assert.Equal(3, presenter.Presentations.Count);
            Assert.Same(presenter.Presentations[0].Assets, presenter.Presentations[1].Assets);
            Assert.Same(presenter.Presentations[0].Assets, presenter.Presentations[2].Assets);
            Assert.Equal(320, seventh.Presentation.Width);
            Assert.Equal(180, seventh.Presentation.Height);
            Assert.Equal(320, seventh.Interaction.FrameWidth);
            Assert.Equal(180, seventh.Interaction.FrameHeight);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ExternalAssetDependencyChangeInvalidatesAndResolvesExactlyOnce()
    {
        var root = TemporaryRoot("studio-preview-external-assets");
        try
        {
            await SaveSceneAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]));
            var presenter = new RecordingViewportPresenter();
            var monitor = new RecordingDependencyMonitor();
            var resolveCount = 0;
            await using var preview = new RekallAgeStudioVulkanPreviewSession(
                presenter,
                (_, _, _) =>
                {
                    resolveCount++;
                    return ValueTask.FromResult(RekallAgeRuntimeViewportAssetSet.Empty with { });
                },
                _ => monitor);
            await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);
            monitor.Enqueue(RekallAgeStudioViewportDependencyChange.Assets);

            await preview.StepAsync(1, CancellationToken.None);
            await preview.StepAsync(1, CancellationToken.None);

            Assert.Equal(1, presenter.AssetInvalidationCount);
            Assert.Equal(2, resolveCount);
            Assert.Equal(2, presenter.Presentations[1].Context.AssetRevision);
            Assert.Equal(2, presenter.Presentations[2].Context.AssetRevision);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task EditDependencyRefreshPresentsOnlyOnceForOneExternalAssetAndShaderChange()
    {
        var root = TemporaryRoot("studio-preview-edit-dependencies");
        try
        {
            await SaveSceneAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]));
            var presenter = new RecordingViewportPresenter();
            var monitor = new RecordingDependencyMonitor();
            var resolveCount = 0;
            await using var preview = new RekallAgeStudioVulkanPreviewSession(
                presenter,
                (_, _, _) =>
                {
                    resolveCount++;
                    return ValueTask.FromResult(RekallAgeRuntimeViewportAssetSet.Empty with { });
                },
                _ => monitor);
            await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);

            var firstStable = await preview.RefreshExternalDependenciesAsync(320, 180, CancellationToken.None);
            var secondStable = await preview.RefreshExternalDependenciesAsync(320, 180, CancellationToken.None);
            monitor.Enqueue(
                RekallAgeStudioViewportDependencyChange.Assets
                | RekallAgeStudioViewportDependencyChange.Shaders);
            var changed = await preview.RefreshExternalDependenciesAsync(320, 180, CancellationToken.None);
            var stableAfterChange = await preview.RefreshExternalDependenciesAsync(320, 180, CancellationToken.None);

            Assert.Null(firstStable);
            Assert.Null(secondStable);
            Assert.NotNull(changed);
            Assert.Null(stableAfterChange);
            Assert.Equal(1, presenter.AssetInvalidationCount);
            Assert.Equal(1, presenter.ShaderInvalidationCount);
            Assert.Equal(2, resolveCount);
            Assert.Equal(2, presenter.Presentations.Count);
            Assert.Equal(2, presenter.Presentations[1].Context.AssetRevision);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ExternalShaderDependencyChangeSignalsTheInjectedPresenterOnce()
    {
        var root = TemporaryRoot("studio-preview-external-shaders");
        try
        {
            await SaveSceneAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]));
            var presenter = new RecordingViewportPresenter();
            var monitor = new RecordingDependencyMonitor();
            await using var preview = new RekallAgeStudioVulkanPreviewSession(
                presenter,
                (_, _, _) => ValueTask.FromResult(RekallAgeRuntimeViewportAssetSet.Empty with { }),
                _ => monitor);
            await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);
            monitor.Enqueue(RekallAgeStudioViewportDependencyChange.Shaders);

            await preview.StepAsync(1, CancellationToken.None);
            await preview.StepAsync(1, CancellationToken.None);

            Assert.Equal(1, presenter.ShaderInvalidationCount);
            Assert.Equal(0, presenter.AssetInvalidationCount);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ProductionDependencyMonitorClassifiesExternalAssetAndShaderChanges()
    {
        var root = TemporaryRoot("studio-preview-dependencies");
        try
        {
            var assets = Path.Combine(root, "Assets", "Textures");
            var shaders = Path.Combine(root, "Shaders", "agent");
            Directory.CreateDirectory(assets);
            Directory.CreateDirectory(shaders);
            var texture = Path.Combine(assets, "albedo.png");
            var shader = Path.Combine(shaders, "surface.frag");
            await File.WriteAllTextAsync(texture, "asset-v1");
            await File.WriteAllTextAsync(shader, "shader-v1");
            using var monitor = new RekallAgeStudioViewportDependencyMonitor(root);

            await File.WriteAllTextAsync(texture, "asset-v2-longer");
            var assetChange = await WaitForChangeAsync(monitor);
            await File.WriteAllTextAsync(shader, "shader-v2-longer");
            var shaderChange = await WaitForChangeAsync(monitor);

            Assert.Equal(RekallAgeStudioViewportDependencyChange.Assets, assetChange);
            Assert.Equal(RekallAgeStudioViewportDependencyChange.Shaders, shaderChange);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData("Assets/Textures/albedo.png")]
    [InlineData("Assets/Models/ship.glb")]
    [InlineData("Materials/ship.age.material-graph.json")]
    [InlineData("Assets/Fonts/hud.ttf")]
    [InlineData("Assets/assets.age.catalog.json")]
    [InlineData("Assets/asset-pipeline.age.json")]
    public async Task DeterministicAssetFingerprintIncludesViewportDependencyKinds(string relativePath)
    {
        var root = TemporaryRoot("studio-preview-fingerprint");
        try
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "dependency-v1");
            var first = RekallAgeStudioViewportDependencyFingerprint.Capture(root);

            await File.WriteAllTextAsync(path, "dependency-v2");
            var second = RekallAgeStudioViewportDependencyFingerprint.Capture(root);

            Assert.NotEqual(first.AssetFingerprint, second.AssetFingerprint);
            Assert.Equal(first.ShaderFingerprint, second.ShaderFingerprint);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task AssetInvalidationResolvesOnceMoreAndSignalsSharedInvalidation()
    {
        var root = TemporaryRoot("studio-preview-invalidation");
        try
        {
            await SaveSceneAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]));
            var presenter = new RecordingViewportPresenter();
            var resolved = new List<RekallAgeRuntimeViewportAssetSet>();
            await using var preview = new RekallAgeStudioVulkanPreviewSession(
                presenter,
                (_, _, _) =>
                {
                    var assets = RekallAgeRuntimeViewportAssetSet.Empty with { };
                    resolved.Add(assets);
                    return ValueTask.FromResult(assets);
                });
            await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);

            await preview.InvalidateAssetsAsync(CancellationToken.None);
            await preview.StepAsync(1, CancellationToken.None);
            await preview.StepAsync(1, CancellationToken.None);

            Assert.Equal(1, presenter.AssetInvalidationCount);
            Assert.Equal(2, resolved.Count);
            Assert.Same(resolved[1], presenter.Presentations[1].Assets);
            Assert.Same(resolved[1], presenter.Presentations[2].Assets);
            Assert.Equal(2, presenter.Presentations[1].Context.AssetRevision);
            Assert.Equal(2, presenter.Presentations[2].Context.AssetRevision);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task FailedInitialVulkanPresentPreservesPreviousCoherentRuntimeSession()
    {
        var root = TemporaryRoot("studio-preview-reset");
        try
        {
            await SaveSceneAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]));
            var presenter = new RecordingViewportPresenter
            {
                Result = frame => frame.Width == 640
                    ? RekallAgeVulkanPresentationFrame.Unavailable(
                        frame,
                        "simulated Vulkan reset failure",
                        ["REKALL_STUDIO_VULKAN_UNAVAILABLE"])
                    : RekallAgeVulkanPresentationFrame.Presented(frame, "test-gpu")
            };
            var resolved = new List<RekallAgeRuntimeViewportAssetSet>();
            await using var preview = new RekallAgeStudioVulkanPreviewSession(
                presenter,
                (_, _, _) =>
                {
                    var assets = RekallAgeRuntimeViewportAssetSet.Empty with { };
                    resolved.Add(assets);
                    return ValueTask.FromResult(assets);
                });
            await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);

            var unavailable = await preview.ResetAsync(root, "Main", 640, 360, CancellationToken.None);
            var advanced = await preview.StepAsync(1, CancellationToken.None);

            Assert.False(unavailable.Presentation.PresentedFrame);
            Assert.Equal(1, advanced.FrameIndex);
            Assert.Equal(320, advanced.Presentation.Width);
            Assert.Equal(180, advanced.Presentation.Height);
            Assert.Same(resolved[0], presenter.Presentations[^1].Assets);
            Assert.Equal(2, resolved.Count);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task UnavailableVulkanTelemetryHasNoBitmapFallback()
    {
        var root = TemporaryRoot("studio-preview-unavailable");
        try
        {
            await SaveSceneAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]));
            var presenter = new RecordingViewportPresenter
            {
                Result = frame => RekallAgeVulkanPresentationFrame.Unavailable(
                    frame,
                    "No Vulkan physical device is available.",
                    ["REKALL_STUDIO_VULKAN_UNAVAILABLE", "VK_ERROR_INITIALIZATION_FAILED"])
            };
            await using var preview = new RekallAgeStudioVulkanPreviewSession(presenter);

            var frame = await preview.ResetAsync(root, "Main", 320, 180, CancellationToken.None);

            Assert.False(frame.Presentation.PresentedFrame);
            Assert.Equal("vulkan", frame.Backend);
            Assert.False(frame.HardwareAccelerated);
            Assert.Equal("No Vulkan physical device is available.", frame.Presentation.FailureReason);
            Assert.Contains("REKALL_STUDIO_VULKAN_UNAVAILABLE", frame.Presentation.Errors);
            Assert.DoesNotContain(
                typeof(System.Windows.Media.Imaging.BitmapSource),
                typeof(RekallAgeStudioPreviewFrame).GetProperties().Select(property => property.PropertyType));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void NormalStudioPreviewSourceDoesNotCallRenderRgba()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Rekall.Age.Studio",
            "RekallAgeStudioVulkanPreviewSession.cs"));

        Assert.DoesNotContain("RenderRgba", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BitmapSource", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PixelFormats", source, StringComparison.Ordinal);
    }

    private static string TemporaryRoot(string name) =>
        Path.Combine(Path.GetTempPath(), $"rekall-age-{name}-{Guid.NewGuid():N}");

    private static Task SaveSceneAsync(string root, RekallAgeSceneDocument scene) =>
        new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None).AsTask();

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static async Task<RekallAgeStudioViewportDependencyChange> WaitForChangeAsync(
        IRekallAgeStudioViewportDependencyMonitor monitor)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var change = await monitor.PollAsync(CancellationToken.None);
            if (change != RekallAgeStudioViewportDependencyChange.None) return change;
            await Task.Delay(20);
        }

        throw new TimeoutException("The production viewport dependency watcher did not observe the change.");
    }

    private sealed class RecordingDependencyMonitor : IRekallAgeStudioViewportDependencyMonitor
    {
        private readonly Queue<RekallAgeStudioViewportDependencyChange> _changes = [];

        public void Enqueue(RekallAgeStudioViewportDependencyChange change) => _changes.Enqueue(change);

        public ValueTask<RekallAgeStudioViewportDependencyChange> PollAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_changes.TryDequeue(out var change)
                ? change
                : RekallAgeStudioViewportDependencyChange.None);
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingViewportPresenter : IRekallAgeStudioViewportPresenter
    {
        public List<Presentation> Presentations { get; } = [];

        public Func<RekallAgeRuntimeViewportFrame, RekallAgeVulkanPresentationFrame> Result { get; init; } =
            frame => RekallAgeVulkanPresentationFrame.Presented(frame, "test-gpu");

        public RekallAgeStudioViewportMetrics Metrics { get; } = new(320, 180, 320, 180, true);

        public int AssetInvalidationCount { get; private set; }

        public int ShaderInvalidationCount { get; private set; }

        public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
            RekallAgeRuntimeViewportFrame frame,
            RekallAgeRuntimeViewportAssetSet assets,
            RekallAgeStudioPresentationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Presentations.Add(new Presentation(frame, assets, context));
            return ValueTask.FromResult(Result(frame));
        }

        public ValueTask InvalidateAssetsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssetInvalidationCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateShadersAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShaderInvalidationCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal sealed record Presentation(
            RekallAgeRuntimeViewportFrame Frame,
            RekallAgeRuntimeViewportAssetSet Assets,
            RekallAgeStudioPresentationContext Context);
    }
}
