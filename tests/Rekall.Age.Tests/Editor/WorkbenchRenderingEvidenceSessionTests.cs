using System.Text.Json;
using System.Text.Json.Nodes;
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

public sealed class WorkbenchRenderingEvidenceSessionTests
{
    [Fact]
    public async Task CaptureEvidenceSurvivesSelectionUntilCurrentSceneMutation()
    {
        var fixture = await CreateFixtureAsync(includeArena: false);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new DeterministicCaptureCommand());
        registry.Register(new SetComponentPropertyCommand());
        var session = new RekallAgeWorkbenchSession(registry);
        Assert.True((await session.OpenAsync(fixture.Root, "Main", default)).Ok);

        var captured = await session.ExecuteAsync(
            "rekall.render.capture_runtime_viewport",
            JsonSerializer.Serialize(new CaptureRuntimeViewportRequest(
                fixture.Root, "Main", 1, Path.Combine(fixture.Root, "capture"))),
            "Capture quality evidence",
            "studio",
            default);

        Assert.True(captured.Ok, captured.Summary);
        Assert.Equal("3.250 ms", session.Model!.Rendering.Runtime.TotalGpuMillisecondsText);

        Assert.True((await session.SelectEntityAsync(fixture.StateEntityId, default)).Ok);
        Assert.Equal("3.250 ms", session.Model!.Rendering.Runtime.TotalGpuMillisecondsText);
        Assert.Single(session.Model.Rendering.DebugViews);

        var mutated = await session.ExecuteAsync(
            "rekall.component.set_property",
            JsonSerializer.Serialize(new
            {
                projectRoot = fixture.Root,
                sceneName = "Main",
                entityId = fixture.StateEntityId,
                componentType = "Game.State",
                propertyName = "score",
                value = 2
            }),
            "Mutate current scene",
            "studio",
            default);

        Assert.True(mutated.Ok, mutated.Summary);
        Assert.Equal("Unavailable", session.Model!.Rendering.Runtime.TotalGpuMillisecondsText);
        Assert.Empty(session.Model.Rendering.DebugViews);
        Assert.Empty(session.Model.Rendering.Comparisons);
    }

    [Fact]
    public async Task RenderingEvidenceNeverCrossesSceneScopeOrReturnsAfterSceneChange()
    {
        var fixture = await CreateFixtureAsync(includeArena: true);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new DeterministicCaptureCommand());
        var session = new RekallAgeWorkbenchSession(registry);
        Assert.True((await session.OpenAsync(fixture.Root, "Main", default)).Ok);
        Assert.True((await session.ExecuteAsync(
            "rekall.render.capture_runtime_viewport",
            JsonSerializer.Serialize(new CaptureRuntimeViewportRequest(
                fixture.Root, "Main", 1, Path.Combine(fixture.Root, "capture"))),
            "Capture Main",
            "studio",
            default)).Ok);
        Assert.Equal("High", session.Model!.Rendering.Runtime.ResolvedPreset);

        Assert.True((await session.OpenSceneAsync("Arena", default)).Ok);
        Assert.Equal("Arena", session.SceneName);
        Assert.Equal("Unavailable", session.Model!.Rendering.Runtime.TotalGpuMillisecondsText);
        Assert.Empty(session.Model.Rendering.DebugViews);

        Assert.True((await session.OpenSceneAsync("Main", default)).Ok);
        Assert.Equal("Unavailable", session.Model!.Rendering.Runtime.TotalGpuMillisecondsText);
        Assert.Empty(session.Model.Rendering.DebugViews);
    }

    [Fact]
    public async Task PartialFailedComparisonPublishesItsUsableTypedCaptures()
    {
        var fixture = await CreateFixtureAsync(includeArena: false);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new PartialComparisonCommand());
        var session = new RekallAgeWorkbenchSession(registry);
        Assert.True((await session.OpenAsync(fixture.Root, "Main", default)).Ok);

        var result = await session.ExecuteAsync(
            "rekall.render.compare_quality_presets",
            JsonSerializer.Serialize(new CompareQualityPresetsRequest(
                fixture.Root,
                "Main",
                ["High", "Epic"],
                1,
                Path.Combine(fixture.Root, "compare"))),
            "Compare quality",
            "studio",
            default);

        Assert.False(result.Ok);
        Assert.Equal("REKALL_TEST_PARTIAL_COMPARISON", Assert.Single(result.Errors).Code);
        var comparison = Assert.IsType<CompareQualityPresetsResult>(result.Value);
        Assert.Single(comparison.Captures);
        var presented = Assert.Single(session.Model!.Rendering.Comparisons);
        Assert.Equal("D:/captures/high.png", presented.ScreenshotPath);
        Assert.Equal("High", session.Model.Rendering.Runtime.ResolvedPreset);
        Assert.Single(session.Model.Rendering.DebugViews);
    }

    [Fact]
    public async Task FailedCaptureRetainsTypedRuntimeEvidenceWithoutInventingDebugImagery()
    {
        var fixture = await CreateFixtureAsync(includeArena: false);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new PartialFailedCaptureCommand());
        var session = new RekallAgeWorkbenchSession(registry);
        Assert.True((await session.OpenAsync(fixture.Root, "Main", default)).Ok);

        var result = await session.ExecuteAsync(
            "rekall.render.capture_runtime_viewport",
            JsonSerializer.Serialize(new CaptureRuntimeViewportRequest(
                fixture.Root,
                "Main",
                1,
                Path.Combine(fixture.Root, "failed-capture"))),
            "Capture partial quality evidence",
            "studio",
            default);

        Assert.False(result.Ok);
        Assert.Equal("REKALL_TEST_NATIVE_CAPTURE_FAILED", Assert.Single(result.Errors).Code);
        var capture = Assert.IsType<CaptureRuntimeViewportResult>(result.Value);
        Assert.False(capture.Captured);
        var runtime = session.Model!.Rendering.Runtime;
        Assert.Equal("Epic", runtime.RequestedPreset);
        Assert.Equal("High", runtime.ResolvedPreset);
        Assert.Equal("3.250 ms", runtime.TotalGpuMillisecondsText);
        Assert.Equal(4, runtime.DrawCount);
        Assert.Equal(2, runtime.DispatchCount);
        Assert.Contains(runtime.Resources, item => item.Name == "Frame resources" && item.Bytes == 8192);
        var degradation = Assert.Single(runtime.Degradations);
        Assert.Equal("REKALL_TEST_FEATURE_DEGRADED", degradation.Code);
        Assert.Equal("enabled", degradation.RequestedValue);
        Assert.Equal("disabled", degradation.ResolvedValue);
        Assert.Single(runtime.SuggestedActions);
        Assert.Empty(session.Model.Rendering.DebugViews);
        Assert.Empty(session.Model.Rendering.Comparisons);

        Assert.True((await session.SelectEntityAsync(fixture.StateEntityId, default)).Ok);
        Assert.Equal("Epic", session.Model!.Rendering.Runtime.RequestedPreset);
        Assert.Equal("3.250 ms", session.Model.Rendering.Runtime.TotalGpuMillisecondsText);
        Assert.Empty(session.Model.Rendering.DebugViews);
    }

    [Fact]
    public async Task SuccessfulComparisonRetainsEvidenceWhenDefaultQualityCapturesAreTransactionOutputs()
    {
        var fixture = await CreateFixtureAsync(includeArena: false);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new OutputRecordingComparisonCommand());
        var session = new RekallAgeWorkbenchSession(registry);
        Assert.True((await session.OpenAsync(fixture.Root, "Main", default)).Ok);

        var result = await session.ExecuteAsync(
            "rekall.render.compare_quality_presets",
            JsonSerializer.Serialize(new CompareQualityPresetsRequest(
                fixture.Root,
                "Main",
                ["Performance", "Epic"])),
            "Compare default quality output",
            "studio",
            default);

        Assert.True(result.Ok, result.Summary);
        var comparison = Assert.IsType<CompareQualityPresetsResult>(result.Value);
        Assert.All(comparison.Captures, capture => Assert.StartsWith("QualityCaptures", capture.ScreenshotPath));
        Assert.Equal(2, session.Model!.Rendering.Comparisons.Count);
        Assert.Equal("Performance", session.Model.Rendering.Runtime.RequestedPreset);
        Assert.Equal("3.250 ms", session.Model.Rendering.Runtime.TotalGpuMillisecondsText);
    }

    [Fact]
    public async Task SuccessfulCaptureRetainsEvidenceWhenCustomOutputDirectoryIsOutsideProject()
    {
        var fixture = await CreateFixtureAsync(includeArena: false);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new OutputRecordingCaptureCommand());
        var session = new RekallAgeWorkbenchSession(registry);
        Assert.True((await session.OpenAsync(fixture.Root, "Main", default)).Ok);
        var outputDirectory = Path.Combine(
            Path.GetDirectoryName(fixture.Root)!,
            "external-quality-output-" + Guid.NewGuid().ToString("N"));

        var result = await session.ExecuteAsync(
            "rekall.render.capture_runtime_viewport",
            JsonSerializer.Serialize(new CaptureRuntimeViewportRequest(
                fixture.Root,
                "Main",
                1,
                outputDirectory)),
            "Capture external quality output",
            "studio",
            default);

        Assert.True(result.Ok, result.Summary);
        var capture = Assert.IsType<CaptureRuntimeViewportResult>(result.Value);
        Assert.StartsWith(outputDirectory, capture.ScreenshotPath);
        Assert.Equal("High", session.Model!.Rendering.Runtime.ResolvedPreset);
        Assert.Equal("3.250 ms", session.Model.Rendering.Runtime.TotalGpuMillisecondsText);
        Assert.Single(session.Model.Rendering.DebugViews);
    }

    [Fact]
    public async Task AuthoredRenderPlanMutationStillInvalidatesCaptureEvidence()
    {
        var fixture = await CreateFixtureAsync(includeArena: false);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new DeterministicCaptureCommand());
        registry.Register(new CreateRenderPlanCommand());
        var session = new RekallAgeWorkbenchSession(registry);
        Assert.True((await session.OpenAsync(fixture.Root, "Main", default)).Ok);
        Assert.True((await session.ExecuteAsync(
            "rekall.render.capture_runtime_viewport",
            JsonSerializer.Serialize(new CaptureRuntimeViewportRequest(
                fixture.Root,
                "Main",
                1,
                Path.Combine(fixture.Root, "capture"))),
            "Capture before render mutation",
            "studio",
            default)).Ok);
        Assert.Equal("High", session.Model!.Rendering.Runtime.ResolvedPreset);

        var mutated = await session.ExecuteAsync(
            "rekall.render.plan.create",
            JsonSerializer.Serialize(new CreateRenderPlanRequest(
                fixture.Root,
                "vulkan",
                "Mutated plan")),
            "Mutate authored render plan",
            "studio",
            default);

        Assert.True(mutated.Ok, mutated.Summary);
        Assert.Null(session.Model!.Rendering.Runtime.ResolvedPreset);
        Assert.Equal("Unavailable", session.Model.Rendering.Runtime.TotalGpuMillisecondsText);
        Assert.Empty(session.Model.Rendering.DebugViews);
    }

    private static async Task<Fixture> CreateFixtureAsync(bool includeArena)
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Rendering Evidence", ["world", "rendering3d"]),
            default);
        var quality = RekallAgeEntityDocument.Create("Quality", ["rendering"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.RenderQualityProfile",
                new JsonObject { ["preset"] = "High" }));
        var state = RekallAgeEntityDocument.Create("State", ["state"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Game.State",
                new JsonObject { ["score"] = 1 }));
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(quality)
                .AddEntity(state),
            default);
        if (includeArena)
        {
            await store.SaveAsync(
                root,
                RekallAgeSceneDocument.Create("Arena", ["world", "rendering3d"]),
                default);
        }
        return new Fixture(root, state.Id);
    }

    private static CaptureRuntimeViewportResult CaptureResult(bool nonBlank = true) => new(
        true,
        "D:/captures/high.png",
        nonBlank,
        320,
        180,
        1,
        "Camera",
        1,
        ["mesh"],
        0,
        [],
        0,
        [],
        0,
        1,
        0,
        0,
        [],
        "vulkan",
        true,
        "hardware-accelerated",
        "Test GPU",
        RekallAgeViewportFrameAnalysis.NotAnalyzed,
        new CaptureRuntimeViewportLayoutDiagnostics(
            false,
            null,
            new CaptureRuntimeViewportWorldBounds(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            [],
            []))
    {
        QualityPlan = QualityPlan(),
        GpuTimings = new RekallAgeGpuFrameTimingReport(
            true,
            null,
            1,
            [new RekallAgeGpuPassTiming("forward", 3_000_000, 3)],
            3_250_000,
            3.25,
            "vulkan-timestamp-query"),
        ResourceBytes = 4096,
        DrawCount = 1,
        DispatchCount = 0,
        SuggestedCommands = ["command execute rekall.render.performance.inspect_scene_budget"]
    };

    private static RekallAgeResolvedRenderFeaturePlan QualityPlan() => new(
        "High",
        "High",
        320,
        180,
        320,
        180,
        1,
        new RekallAgeResolvedShadowQuality(3, 2048, 12),
        new RekallAgeResolvedFogQuality("froxel", 160, 90, 48),
        new RekallAgeResolvedPostQuality(true, true, true),
        new RekallAgeResolvedParticleQuality(64_000),
        2048,
        4096,
        []);

    private sealed record Fixture(string Root, string StateEntityId);

    private sealed class DeterministicCaptureCommand
        : IRekallAgeCommand<CaptureRuntimeViewportRequest, CaptureRuntimeViewportResult>
    {
        public string Name => "rekall.render.capture_runtime_viewport";
        public RekallAgeCommandSchema Schema => new(Name, "Deterministic test capture.", typeof(CaptureRuntimeViewportRequest).FullName!, typeof(CaptureRuntimeViewportResult).FullName!);

        public ValueTask<RekallAgeCommandResult<CaptureRuntimeViewportResult>> ExecuteAsync(
            CaptureRuntimeViewportRequest request,
            RekallAgeCommandContext context) =>
            ValueTask.FromResult(RekallAgeCommandResult<CaptureRuntimeViewportResult>.Success(CaptureResult()));
    }

    private sealed class PartialComparisonCommand
        : IRekallAgeCommand<CompareQualityPresetsRequest, CompareQualityPresetsResult>
    {
        public string Name => "rekall.render.compare_quality_presets";
        public RekallAgeCommandSchema Schema => new(Name, "Deterministic partial comparison.", typeof(CompareQualityPresetsRequest).FullName!, typeof(CompareQualityPresetsResult).FullName!);

        public ValueTask<RekallAgeCommandResult<CompareQualityPresetsResult>> ExecuteAsync(
            CompareQualityPresetsRequest request,
            RekallAgeCommandContext context)
        {
            var capture = CaptureResult();
            var value = new CompareQualityPresetsResult(
                request.SceneName,
                1,
                [new RekallAgeQualityPresetCapture(
                    "High",
                    "High",
                    1,
                    capture.ScreenshotPath,
                    capture.NonBlank,
                    320,
                    180,
                    320,
                    180,
                    capture.ResourceBytes,
                    capture.DrawCount,
                    capture.DispatchCount,
                    [],
                    capture.GpuTimings,
                    capture.FrameAnalysis)],
                capture.SuggestedCommands);
            var error = new RekallAgeCommandError(
                "REKALL_TEST_PARTIAL_COMPARISON",
                "The second preset failed after one usable capture.",
                "Epic");
            return ValueTask.FromResult(RekallAgeCommandResult<CompareQualityPresetsResult>.Failure(
                value,
                error.Message,
                [error]));
        }
    }

    private sealed class PartialFailedCaptureCommand
        : IRekallAgeCommand<CaptureRuntimeViewportRequest, CaptureRuntimeViewportResult>
    {
        public string Name => "rekall.render.capture_runtime_viewport";
        public RekallAgeCommandSchema Schema => new(Name, "Deterministic failed native capture with typed evidence.", typeof(CaptureRuntimeViewportRequest).FullName!, typeof(CaptureRuntimeViewportResult).FullName!);

        public ValueTask<RekallAgeCommandResult<CaptureRuntimeViewportResult>> ExecuteAsync(
            CaptureRuntimeViewportRequest request,
            RekallAgeCommandContext context)
        {
            var degradation = new RekallAgeRenderFeatureDegradation(
                "REKALL_TEST_FEATURE_DEGRADED",
                "testFeature",
                "enabled",
                "disabled",
                "The test device could not enable the requested feature.");
            var value = CaptureResult(nonBlank: false) with
            {
                Captured = false,
                ScreenshotPath = Path.Combine(request.OutputDirectory, "failed-native.png"),
                HardwareAccelerated = false,
                AccelerationStatus = "vulkan-scene-failed",
                QualityPlan = QualityPlan() with
                {
                    RequestedPreset = "Epic",
                    ResolvedPreset = "High",
                    Degradations = [degradation]
                },
                ResourceBytes = 8192,
                DrawCount = 4,
                DispatchCount = 2
            };
            var error = new RekallAgeCommandError(
                "REKALL_TEST_NATIVE_CAPTURE_FAILED",
                "Native capture failed after resolving and profiling the frame.",
                request.SceneName);
            return ValueTask.FromResult(RekallAgeCommandResult<CaptureRuntimeViewportResult>.Failure(
                value,
                error.Message,
                [error]));
        }
    }

    private sealed class OutputRecordingCaptureCommand
        : IRekallAgeCommand<CaptureRuntimeViewportRequest, CaptureRuntimeViewportResult>
    {
        public string Name => "rekall.render.capture_runtime_viewport";
        public RekallAgeCommandSchema Schema => new(Name, "Records deterministic generated capture output.", typeof(CaptureRuntimeViewportRequest).FullName!, typeof(CaptureRuntimeViewportResult).FullName!);

        public ValueTask<RekallAgeCommandResult<CaptureRuntimeViewportResult>> ExecuteAsync(
            CaptureRuntimeViewportRequest request,
            RekallAgeCommandContext context)
        {
            var value = CaptureResult() with
            {
                ScreenshotPath = Path.Combine(request.OutputDirectory, "captured.png")
            };
            context.Transaction.RecordChangedResource(value.ScreenshotPath);
            return ValueTask.FromResult(RekallAgeCommandResult<CaptureRuntimeViewportResult>.Success(value));
        }
    }

    private sealed class OutputRecordingComparisonCommand
        : IRekallAgeCommand<CompareQualityPresetsRequest, CompareQualityPresetsResult>
    {
        public string Name => "rekall.render.compare_quality_presets";
        public RekallAgeCommandSchema Schema => new(Name, "Records deterministic generated comparison outputs.", typeof(CompareQualityPresetsRequest).FullName!, typeof(CompareQualityPresetsResult).FullName!);

        public ValueTask<RekallAgeCommandResult<CompareQualityPresetsResult>> ExecuteAsync(
            CompareQualityPresetsRequest request,
            RekallAgeCommandContext context)
        {
            var source = CaptureResult();
            var captures = request.Presets.Select(preset =>
            {
                var screenshotPath = Path.Combine(
                    request.OutputDirectory,
                    preset.ToLowerInvariant(),
                    "captured.png");
                context.Transaction.RecordChangedResource(screenshotPath);
                return new RekallAgeQualityPresetCapture(
                    preset,
                    preset,
                    1,
                    screenshotPath,
                    true,
                    320,
                    180,
                    320,
                    180,
                    source.ResourceBytes,
                    source.DrawCount,
                    source.DispatchCount,
                    [],
                    source.GpuTimings,
                    source.FrameAnalysis);
            }).ToArray();
            return ValueTask.FromResult(RekallAgeCommandResult<CompareQualityPresetsResult>.Success(
                new CompareQualityPresetsResult(
                    request.SceneName,
                    1,
                    captures,
                    source.SuggestedCommands)));
        }
    }
}
