using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class HighFidelityRenderGraphTests
{
    [Fact]
    public void BuilderCompilesHighIntoTheDeclaredForwardPlusPassOrder()
    {
        var graph = Build("High");

        Assert.Equal(
            ["depth-normal", "shadow-directional", "cluster-build", "opaque-hdr", "fog-integrate", "fog-debug-readback", "transparent-particles", "bloom", "tone-map", "ui", "present"],
            graph.Passes.Select(pass => pass.Name));
        Assert.All(graph.Passes, pass => Assert.All(
            pass.Reads,
            resource => Assert.Contains(graph.Resources, item => item.Name == resource)));
        Assert.True(graph.IsValid);
    }

    [Fact]
    public void FroxelGraphDeclaresDepthAndPersistentHistoryAuthority()
    {
        var graph = Build("High");

        Assert.Contains(graph.Resources, resource => resource is
        {
            Name: "fog-history",
            Format: "R16G16B16A16_SFloat",
            Width: 160,
            Height: 90,
            Layers: 48,
            Lifetime: "persistent"
        });
        var fog = Assert.Single(graph.Passes, pass => pass.Name == "fog-integrate");
        Assert.Contains("depth-buffer", fog.Reads);
        Assert.Contains("fog-history", fog.Reads);
        Assert.Contains("fog-history", fog.Writes);
    }

    [Fact]
    public void FroxelGraphDeclaresNativeDebugReadbackTransferSourceUsage()
    {
        var graph = Build("High");

        var froxel = Assert.Single(graph.Resources, resource => resource.Name == "fog-froxel");
        Assert.Contains("storage", froxel.Usage);
        Assert.Contains("sampled", froxel.Usage);
        Assert.Contains("transfer-source", froxel.Usage);
        Assert.Contains(graph.Resources, resource => resource.Name == "fog-debug-readback"
            && resource.Usage.Contains("transfer-destination", StringComparer.Ordinal)
            && resource.Usage.Contains("host-readback", StringComparer.Ordinal));
        var readback = Assert.Single(graph.Passes, pass => pass.Name == "fog-debug-readback");
        Assert.Equal("transfer", readback.Kind);
        Assert.Contains("fog-froxel", readback.Reads);
        Assert.Contains("fog-debug-readback", readback.Writes);
        Assert.Contains(graph.Dependencies, dependency => dependency is
        {
            ProducerPass: "fog-integrate",
            ConsumerPass: "fog-debug-readback",
            Resource: "fog-froxel"
        });
    }

    [Fact]
    public void BuilderOmitsPerformanceOnlyVolumetricBloomAndSsaoResources()
    {
        var graph = Build("Performance");
        var names = graph.Resources.Select(resource => resource.Name).ToArray();

        Assert.DoesNotContain("fog-froxel", names);
        Assert.DoesNotContain("bloom-pyramid", names);
        Assert.DoesNotContain("ssao-occlusion", names);
    }

    [Fact]
    public void BuilderIncreasesEpicResourceQualityWithoutChangingPassDependencies()
    {
        var high = Build("High");
        var epic = Build("Epic");

        Assert.Equal(high.Passes.Select(pass => pass.Name), epic.Passes.Select(pass => pass.Name));
        Assert.Equal(high.Dependencies.Select(dependency => dependency.Resource), epic.Dependencies.Select(dependency => dependency.Resource));
        Assert.Equal(2560, high.Resources.Single(resource => resource.Name == "scene-hdr").Width);
        Assert.Equal(3200, epic.Resources.Single(resource => resource.Name == "scene-hdr").Width);
        Assert.Equal(3, high.Resources.Single(resource => resource.Name == "shadow-directional").Layers);
        Assert.Equal(4, epic.Resources.Single(resource => resource.Name == "shadow-directional").Layers);
        Assert.Contains("filter-taps:24", epic.Resources.Single(resource => resource.Name == "shadow-directional").Usage);
    }

    [Fact]
    public void GraphReportsEachStructuralValidationFailureAndMemoryOverflow()
    {
        var graph = RekallAgeHighFidelityRenderGraph.Create(
            [
                new RekallAgeHighFidelityRenderResource("duplicate", "R8_UNorm", 1, 1, 1, "transient", ["sampled"]),
                new RekallAgeHighFidelityRenderResource("duplicate", "R8_UNorm", 1, 1, 1, "transient", ["sampled"]),
                new RekallAgeHighFidelityRenderResource("bad-dimensions", "R8_UNorm", 0, 1, 1, "transient", ["sampled"]),
                new RekallAgeHighFidelityRenderResource("depth-as-color", "D32_SFloat", 1, 1, 1, "transient", ["color-attachment"]),
                new RekallAgeHighFidelityRenderResource("late", "R8_UNorm", 1, 1, 1, "transient", ["sampled"]),
                new RekallAgeHighFidelityRenderResource("overflow", "R16G16B16A16_SFloat", int.MaxValue, int.MaxValue, int.MaxValue, "transient", ["sampled"])
            ],
            [
                new RekallAgeHighFidelityRenderPass("reader", "graphics", ["late", "missing"], [], 0, true),
                new RekallAgeHighFidelityRenderPass("writer", "graphics", [], ["late"], 1, true)
            ],
            [
                new RekallAgeHighFidelityRenderDependency("reader", "writer", "late"),
                new RekallAgeHighFidelityRenderDependency("writer", "reader", "late")],
            transientBudgetBytes: 1,
            persistentBudgetBytes: 0);

        Assert.False(graph.IsValid);
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_DUPLICATE_RESOURCE");
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_INVALID_DIMENSIONS");
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_DEPTH_COLOR_INCOMPATIBLE");
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_MISSING_RESOURCE");
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_MISSING_PRODUCER");
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_READ_BEFORE_WRITE");
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_CYCLE");
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_MEMORY_OVERFLOW");
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_MEMORY_BUDGET_EXCEEDED");
    }

    [Fact]
    public void PersistentFeedbackAllowsPriorFrameReadButStillRequiresAWriter()
    {
        var feedback = RekallAgeHighFidelityRenderGraph.Create(
            [new RekallAgeHighFidelityRenderResource("history", "R8_UNorm", 1, 1, 1, "persistent", ["sampled", "storage"])],
            [new RekallAgeHighFidelityRenderPass("temporal", "compute", ["history"], ["history"], 0, true)]);
        var orphan = RekallAgeHighFidelityRenderGraph.Create(
            [new RekallAgeHighFidelityRenderResource("history", "R8_UNorm", 1, 1, 1, "persistent", ["sampled"])],
            [new RekallAgeHighFidelityRenderPass("temporal", "compute", ["history"], [], 0, true)]);

        Assert.True(feedback.IsValid);
        Assert.Contains(orphan.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_MISSING_PRODUCER");
    }

    [Fact]
    public void GraphEstimatesHighResourceBytesAndStaysWithinTheResolvedBudget()
    {
        var graph = Build("High");

        Assert.Equal(144_795_648, graph.EstimatedBytes);
        Assert.Equal(
            graph.Resources
                .Where(resource => resource.Lifetime != "external")
                .Sum(resource => resource.Format switch
                {
                    "R8_UNorm" => (long)resource.Width * resource.Height * resource.Layers,
                    "R16G16_SFloat" or "R32_UInt" or "D32_SFloat" or "R8G8B8A8_UNorm" => (long)resource.Width * resource.Height * resource.Layers * 4,
                    "R16G16B16A16_SFloat" => (long)resource.Width * resource.Height * resource.Layers * 8,
                    _ => 0
                }),
            graph.EstimatedBytes);
        Assert.DoesNotContain(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_MEMORY_BUDGET_EXCEEDED");
    }

    [Fact]
    public void BuilderReportsViewportDimensionsThatDoNotMatchTheResolvedPlan()
    {
        var plan = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High"),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
            2560,
            1440);
        var frame = Frame(1280, 720, plan);

        var graph = new RekallAgeHighFidelityRenderGraphBuilder().Build(frame, plan);

        Assert.False(graph.IsValid);
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_VIEWPORT_DIMENSIONS_MISMATCH"
            && item.Target == "viewport");
    }

    [Fact]
    public void GraphRejectsUnsupportedResourceFormatsBeforeEstimatingMemory()
    {
        var graph = RekallAgeHighFidelityRenderGraph.Create(
            [new RekallAgeHighFidelityRenderResource("custom", "R9G9B9E5_UFloat", 2, 2, 1, "transient", ["sampled"])],
            []);

        Assert.False(graph.IsValid);
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_UNSUPPORTED_FORMAT"
            && item.Target == "custom");
    }

    [Fact]
    public void GraphRejectsCustomDependenciesThatOmitADeclaredResourceRead()
    {
        var graph = RekallAgeHighFidelityRenderGraph.Create(
            [new RekallAgeHighFidelityRenderResource("output", "R8_UNorm", 1, 1, 1, "transient", ["sampled"])],
            [
                new RekallAgeHighFidelityRenderPass("writer", "graphics", [], ["output"], 0, true),
                new RekallAgeHighFidelityRenderPass("reader", "graphics", ["output"], [], 1, true)
            ],
            dependencies: []);

        Assert.False(graph.IsValid);
        Assert.Contains(graph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_DEPENDENCY_MISSING"
            && item.Target == "output");
    }

    [Fact]
    public void GraphRejectsInvalidDependencyEndpointsResourcesAndDuplicatePassNames()
    {
        var malformedDependencyGraph = RekallAgeHighFidelityRenderGraph.Create(
            [new RekallAgeHighFidelityRenderResource("output", "R8_UNorm", 1, 1, 1, "transient", ["sampled"])],
            [
                new RekallAgeHighFidelityRenderPass("writer", "graphics", [], ["output"], 0, true),
                new RekallAgeHighFidelityRenderPass("reader", "graphics", ["output"], [], 1, true)
            ],
            [
                new RekallAgeHighFidelityRenderDependency("missing", "reader", "output"),
                new RekallAgeHighFidelityRenderDependency("writer", "reader", "other")]);
        var duplicatePassGraph = RekallAgeHighFidelityRenderGraph.Create(
            [new RekallAgeHighFidelityRenderResource("output", "R8_UNorm", 1, 1, 1, "transient", ["sampled"])],
            [
                new RekallAgeHighFidelityRenderPass("duplicate", "graphics", [], ["output"], 0, true),
                new RekallAgeHighFidelityRenderPass("duplicate", "graphics", [], [], 1, true)
            ]);

        Assert.False(malformedDependencyGraph.IsValid);
        Assert.Contains(malformedDependencyGraph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_INVALID_DEPENDENCY_ENDPOINT");
        Assert.Contains(malformedDependencyGraph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_INVALID_DEPENDENCY_RESOURCE");
        Assert.False(duplicatePassGraph.IsValid);
        Assert.Contains(duplicatePassGraph.Diagnostics, item => item.Code == "REKALL_RENDER_GRAPH_DUPLICATE_PASS");
    }

    private static RekallAgeHighFidelityRenderGraph Build(string preset)
    {
        var plan = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent(preset),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
            2560,
            1440);
        var frame = Frame(2560, 1440, plan);

        return new RekallAgeHighFidelityRenderGraphBuilder().Build(frame, plan);
    }

    private static RekallAgeRuntimeViewportFrame Frame(
        int width,
        int height,
        RekallAgeResolvedRenderFeaturePlan plan) => new(
            "Main",
            0,
            0,
            width,
            height,
            null,
            [],
            [],
            1,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            [])
        {
            ResolvedQualityPlan = plan
        };
}
