using System.Text.Json.Nodes;
using System.Text.Json;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Project;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanGpuProfilerTests
{
    [Fact]
    public void TimestampPeriodConvertsRawTicksToNanosecondsAndMilliseconds()
    {
        var report = RekallAgeVulkanGpuProfiler.ResolveCompletedFrame(
            frameIndex: 42,
            timestampPeriodNanoseconds: 0.5,
            timestampValidBits: 64,
            [new RekallAgeVulkanGpuPassTimestampSample("opaque-hdr", 100, 340)]);

        Assert.True(report.Available);
        Assert.Null(report.Code);
        Assert.Equal(42, report.FrameIndex);
        var pass = Assert.Single(report.Passes);
        Assert.Equal("opaque-hdr", pass.Name);
        Assert.Equal(120, pass.Nanoseconds);
        Assert.Equal(0.00012, pass.Milliseconds, 8);
        Assert.Equal(120, report.TotalNanoseconds);
        Assert.Equal(0.00012, report.TotalMilliseconds!.Value, 8);
        Assert.Equal("vulkan-timestamp-query", report.Provenance);
    }

    [Fact]
    public void TimestampDeltaHonorsValidBitWrap()
    {
        var report = RekallAgeVulkanGpuProfiler.ResolveCompletedFrame(
            frameIndex: 7,
            timestampPeriodNanoseconds: 2,
            timestampValidBits: 8,
            [new RekallAgeVulkanGpuPassTimestampSample("tone-map", 250, 5)]);

        var pass = Assert.Single(report.Passes);
        Assert.Equal(22, pass.Nanoseconds);
        Assert.Equal(22, report.TotalNanoseconds);
    }

    [Fact]
    public void CompletedFramePreservesExecutedPassOrderAndUsesFrameEnvelopeForTotal()
    {
        var report = RekallAgeVulkanGpuProfiler.ResolveCompletedFrame(
            frameIndex: 9,
            timestampPeriodNanoseconds: 1,
            timestampValidBits: 64,
            [
                new RekallAgeVulkanGpuPassTimestampSample("opaque-hdr", 100, 250),
                new RekallAgeVulkanGpuPassTimestampSample("bloom", 270, 330),
                new RekallAgeVulkanGpuPassTimestampSample("tone-map", 350, 410)
            ]);

        Assert.Equal(["opaque-hdr", "bloom", "tone-map"], report.Passes.Select(pass => pass.Name));
        Assert.Equal([150d, 60d, 60d], report.Passes.Select(pass => pass.Nanoseconds));
        Assert.Equal(310, report.TotalNanoseconds);

        var attached = RekallAgeVulkanGpuProfiler.AttachTimings(
            [
                new RekallAgeHighFidelityFramePassReport("opaque-hdr", "graphics", [], ["scene-hdr"], true, 0, 3),
                new RekallAgeHighFidelityFramePassReport("bloom", "compute", ["scene-hdr"], ["bloom-pyramid"], true, 1, 0),
                new RekallAgeHighFidelityFramePassReport("tone-map", "graphics", ["scene-hdr"], ["ldr-color"], true, 0, 1)
            ],
            report);

        Assert.Equal([150d, 60d, 60d], attached.Select(pass => pass.GpuNanoseconds));
        Assert.Equal([0.00015, 0.00006, 0.00006], attached.Select(pass => pass.GpuMilliseconds));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(64, 0)]
    [InlineData(65, 1)]
    public void UnsupportedOrInvalidTimestampQueriesAreUnavailableWithoutCpuSubstitution(
        uint timestampValidBits,
        double timestampPeriodNanoseconds)
    {
        var report = RekallAgeVulkanGpuProfiler.ResolveCompletedFrame(
            frameIndex: 11,
            timestampPeriodNanoseconds,
            timestampValidBits,
            [new RekallAgeVulkanGpuPassTimestampSample("opaque-hdr", 10, 20)]);

        Assert.False(report.Available);
        Assert.Equal("REKALL_GPU_TIMESTAMPS_UNAVAILABLE", report.Code);
        Assert.Empty(report.Passes);
        Assert.Null(report.TotalNanoseconds);
        Assert.Null(report.TotalMilliseconds);
        Assert.Equal("unavailable", report.Provenance);
    }

    [Fact]
    public void QueryPoolSlotCannotResetOrReuseUntilItsProducingFenceCompletesAndReadbackFinishes()
    {
        var lifecycle = new RekallAgeVulkanGpuQueryPoolLifecycle(slotCount: 2);
        var first = lifecycle.Acquire(frameIndex: 1, queryCount: 4);
        lifecycle.MarkSubmitted(first, fenceToken: 101);
        var second = lifecycle.Acquire(frameIndex: 2, queryCount: 4);
        lifecycle.MarkSubmitted(second, fenceToken: 102);

        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = lifecycle.Acquire(frameIndex: 3, queryCount: 4);
        });
        Assert.False(lifecycle.MarkFenceCompleted(fenceToken: 999));
        Assert.False(lifecycle.CanRead(first));
        Assert.False(lifecycle.CanReset(first.SlotIndex));

        Assert.True(lifecycle.MarkFenceCompleted(fenceToken: 101));
        Assert.True(lifecycle.CanRead(first));
        Assert.False(lifecycle.CanReset(first.SlotIndex));
        lifecycle.MarkRead(first);

        Assert.True(lifecycle.CanReset(first.SlotIndex));
        var reused = lifecycle.Acquire(frameIndex: 3, queryCount: 6);
        Assert.Equal(first.SlotIndex, reused.SlotIndex);
        Assert.Equal(first.Generation + 1, reused.Generation);
    }

    [Fact]
    public void UnsubmittedQueryRecordingCanBeCancelledAndSafelyReused()
    {
        var lifecycle = new RekallAgeVulkanGpuQueryPoolLifecycle(slotCount: 1);
        var abandoned = lifecycle.Acquire(frameIndex: 5, queryCount: 4);

        Assert.True(lifecycle.CancelRecording(abandoned));
        Assert.True(lifecycle.CanReset(abandoned.SlotIndex));
        var reused = lifecycle.Acquire(frameIndex: 6, queryCount: 2);

        Assert.Equal(abandoned.SlotIndex, reused.SlotIndex);
        Assert.Equal(abandoned.Generation + 1, reused.Generation);
    }

    [Fact]
    public async Task CompareQualityPresetsCapturesAlignedFramesWithoutMutatingAuthoredScene()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Quality Comparison", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["z"] = 3 }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))),
            CancellationToken.None);
        var sceneStore = new RekallAgeSceneStore();
        var before = JsonSerializer.Serialize(await sceneStore.LoadAsync(root, "Main", CancellationToken.None));

        var result = await new CompareQualityPresetsCommand().ExecuteAsync(
            new CompareQualityPresetsRequest(
                root,
                "Main",
                ["Performance", "High"],
                Frames: 3,
                OutputDirectory: Path.Combine(root, "QualityCaptures"),
                Width: 160,
                Height: 90,
                BackendId: "software",
                Overrides: new RekallAgeRenderQualityOverrides(ResolutionScale: 0.5)),
            new RekallAgeCommandContext(
                "agent",
                RekallAgeTransaction.Begin("compare quality presets"),
                CancellationToken.None));

        Assert.True(result.Ok);
        Assert.Equal(3, result.Value.FrameIndex);
        Assert.Equal(["Performance", "High"], result.Value.Captures.Select(capture => capture.RequestedPreset));
        Assert.All(result.Value.Captures, capture =>
        {
            Assert.Equal(80, capture.RenderWidth);
            Assert.Equal(45, capture.RenderHeight);
            Assert.Equal(3, capture.FrameIndex);
            Assert.True(File.Exists(capture.ScreenshotPath));
            Assert.True(capture.NonBlank);
        });
        Assert.Equal(2, result.Value.Captures.Select(capture => capture.ScreenshotPath).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(result.Value.NextCommands, command => command.Contains("rekall.render.capture_runtime_viewport", StringComparison.Ordinal));
        var after = JsonSerializer.Serialize(await sceneStore.LoadAsync(root, "Main", CancellationToken.None));
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task CompareQualityPresetsClampsBoundedOverridesWithRequestedAndResolvedFacts()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Bounded Quality Comparison", ["world", "rendering2d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Sprite", ["prop"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 8, ["y"] = 8 }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "missing" }))),
            CancellationToken.None);

        var result = await new CompareQualityPresetsCommand().ExecuteAsync(
            new CompareQualityPresetsRequest(
                root,
                "Main",
                ["Performance", "High"],
                OutputDirectory: Path.Combine(root, "BoundedCompare"),
                Width: 32,
                Height: 24,
                BackendId: "software",
                Overrides: new RekallAgeRenderQualityOverrides(
                    ResolutionScale: 0.01,
                    ShadowResolution: int.MaxValue,
                    MaximumActiveParticles: int.MaxValue)),
            new RekallAgeCommandContext(
                "agent",
                RekallAgeTransaction.Begin("bounded compare quality presets"),
                CancellationToken.None));

        Assert.True(result.Ok);
        Assert.All(result.Value.Captures, capture =>
        {
            Assert.Equal(0.25, capture.RenderWidth / 32d);
            Assert.Contains(capture.Degradations, degradation =>
                degradation.Code == "REKALL_RENDER_QUALITY_OVERRIDE_CLAMPED"
                && degradation.Feature == "resolutionScale"
                && degradation.RequestedValue == "0.01"
                && degradation.ResolvedValue == "0.25");
            Assert.Contains(capture.Degradations, degradation =>
                degradation.Feature == "shadowResolution"
                && degradation.RequestedValue == int.MaxValue.ToString()
                && degradation.ResolvedValue == "8192");
            Assert.Contains(capture.Degradations, degradation =>
                degradation.Feature == "maximumActiveParticles"
                && degradation.RequestedValue == int.MaxValue.ToString()
                && degradation.ResolvedValue == "1048576");
        });
    }

    [Fact]
    public async Task NativeProfilerReadsOnlyTheFenceCompletedPriorFrameAndReportsExecutedPassesInOrder()
    {
        var output = TestPaths.CreateTempDirectory();
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera", "Camera", "Camera3D", true, 0, 0, -4, FieldOfViewDegrees: 70);
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("Performance", Bloom: false, FogMode: "analytic")
            {
                EnableGpuTimestamps = true
            },
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("vulkan"),
            96,
            64);
        var firstFrame = new RekallAgeRuntimeViewportFrame(
            "GPU Timing",
            10,
            10d / 60d,
            96,
            64,
            camera,
            [camera],
            [
                new RekallAgeRuntimeViewportRenderable(
                    "cube", "Cube", "mesh", "rekall.primitive.cube", 0, 0, 2, 0,
                    Variant: "rekall.geometry.cube", MaterialColor: "#486a9a")
            ],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            [],
            PostProcessStack: new RekallAgeRuntimeViewportPostProcessStack(
                "post", "Tone Map", true,
                [new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")]))
        {
            ResolvedQualityPlan = quality
        };

        using var capture = new RekallAgeNativeVulkanSceneCapture();
        var first = await capture.CaptureSceneAsync(
            firstFrame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);
        var second = await capture.CaptureSceneAsync(
            firstFrame with { FrameIndex = 11, ElapsedSeconds = 11d / 60d },
            RekallAgeRuntimeViewportAssetSet.Empty,
            output,
            "discrete-gpu",
            CancellationToken.None);

        Assert.True(first.Captured, string.Join(Environment.NewLine, first.Errors));
        Assert.False(Assert.IsType<RekallAgeHighFidelityFrameReport>(first.HighFidelityFrame).GpuTimings.Available);
        Assert.True(second.Captured, string.Join(Environment.NewLine, second.Errors));
        var report = Assert.IsType<RekallAgeHighFidelityFrameReport>(second.HighFidelityFrame);
        Assert.True(report.GpuTimings.Available, report.GpuTimings.Code);
        Assert.Equal(10, report.GpuTimings.FrameIndex);
        Assert.Equal(
            report.Passes.Where(pass => pass.Executed).Select(pass => pass.Name),
            report.GpuTimings.Passes.Select(pass => pass.Name));
        Assert.All(report.GpuTimings.Passes, pass => Assert.True(pass.Nanoseconds >= 0));
        Assert.True(report.GpuTimings.TotalNanoseconds > 0);
        Assert.All(report.Passes.Where(pass => pass.Executed), pass => Assert.NotNull(pass.GpuNanoseconds));
    }

    [Fact]
    public async Task NativePresetComparisonWarmsEachPresetSoReturnedMetricsMatchThatPreset()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Native Quality Comparison", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["z"] = 3 }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })))
                .AddEntity(RekallAgeEntityDocument.Create("Post", ["post-process"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.PostProcessStack", new JsonObject
                    {
                        ["enabled"] = true,
                        ["passes"] = new JsonArray
                        {
                            new JsonObject { ["name"] = "tone", ["type"] = "tone-map" }
                        }
                    }))),
            CancellationToken.None);

        var result = await new CompareQualityPresetsCommand().ExecuteAsync(
            new CompareQualityPresetsRequest(
                root,
                "Main",
                ["Performance", "Low"],
                Frames: 2,
                OutputDirectory: Path.Combine(root, "NativeCompare"),
                Width: 96,
                Height: 64,
                BackendId: "vulkan",
                IncludeGpuTimings: true),
            new RekallAgeCommandContext(
                "agent",
                RekallAgeTransaction.Begin("native compare quality presets"),
                CancellationToken.None));

        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        Assert.Equal(2, result.Value.Captures.Count);
        Assert.All(result.Value.Captures, capture =>
        {
            Assert.True(capture.GpuTimings.Available, $"{capture.RequestedPreset}: {capture.GpuTimings.Code}");
            Assert.Equal(capture.FrameIndex, capture.GpuTimings.FrameIndex);
            Assert.NotEmpty(capture.GpuTimings.Passes);
        });
    }

    [Fact]
    public async Task PerformanceBudgetInspectionReturnsMeasuredGpuPassesAndSuggestedCommands()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Measured GPU Budget", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["z"] = 3 }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })))
                .AddEntity(RekallAgeEntityDocument.Create("Post", ["post-process"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.PostProcessStack", new JsonObject
                    {
                        ["enabled"] = true,
                        ["passes"] = new JsonArray
                        {
                            new JsonObject { ["name"] = "tone", ["type"] = "tone-map" }
                        }
                    }))),
            CancellationToken.None);

        var result = await new InspectScenePerformanceBudgetCommand().ExecuteAsync(
            new InspectScenePerformanceBudgetRequest(root, "Main", Frames: 4, Width: 96, Height: 64)
            {
                QualityPreset = "Performance",
                IncludeGpuTimings = true
            },
            new RekallAgeCommandContext(
                "agent",
                RekallAgeTransaction.Begin("inspect measured GPU budget"),
                CancellationToken.None));

        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        Assert.Equal("Performance", result.Value.QualityPlan?.ResolvedPreset);
        Assert.True(result.Value.GpuTimings.Available, result.Value.GpuTimings.Code);
        Assert.Equal(4, result.Value.GpuTimings.FrameIndex);
        Assert.NotEmpty(result.Value.GpuTimings.Passes);
        Assert.True(result.Value.ResourceBytes > 0);
        Assert.True(result.Value.RenderWorkloadDrawCount > 0);
        Assert.Contains(result.Value.SuggestedCommands, command =>
            command.Contains("rekall.render.compare_quality_presets", StringComparison.Ordinal));
    }
}
