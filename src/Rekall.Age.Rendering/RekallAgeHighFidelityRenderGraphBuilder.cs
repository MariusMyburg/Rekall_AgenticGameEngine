using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Compiles immutable resolved-quality and viewport facts into the first inspectable Forward+ frame graph.
/// </summary>
public sealed class RekallAgeHighFidelityRenderGraphBuilder
{
    public RekallAgeHighFidelityRenderGraph Build(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeResolvedRenderFeaturePlan plan,
        RekallAgeVulkanParticlePlan? particlePlan = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(plan);
        particlePlan ??= new RekallAgeVulkanParticlePlanner().Plan(frame, plan.Particles, frame.DeltaSeconds);

        var resources = new List<RekallAgeHighFidelityRenderResource>
        {
            Resource("depth-buffer", "D32_SFloat", plan.RenderWidth, plan.RenderHeight, 1, "transient", ["depth-attachment", "sampled"]),
            Resource("normal-buffer", "R16G16_SFloat", plan.RenderWidth, plan.RenderHeight, 1, "transient", ["color-attachment", "sampled"]),
            Resource("shadow-directional", "D32_SFloat", plan.Shadows.Resolution, plan.Shadows.Resolution, plan.Shadows.CascadeCount, "persistent", ["depth-attachment", "sampled", $"filter-taps:{plan.Shadows.FilterTapCount}"]),
            Resource("cluster-indices", "R32_UInt", DivideRoundUp(plan.RenderWidth, 16), DivideRoundUp(plan.RenderHeight, 16), 24, "transient", ["storage", "sampled"])
        };

        if (plan.Post.Ssao)
        {
            resources.Add(Resource("ssao-occlusion", "R8_UNorm", DivideRoundUp(plan.RenderWidth, 2), DivideRoundUp(plan.RenderHeight, 2), 1, "transient", ["storage", "sampled"]));
        }

        resources.Add(Resource(
            "scene-hdr",
            "R16G16B16A16_SFloat",
            plan.RenderWidth,
            plan.RenderHeight,
            1,
            "transient",
            plan.Fog.Mode.Equals("analytic", StringComparison.OrdinalIgnoreCase)
                ? ["color-attachment", "sampled"]
                : ["color-attachment", "storage", "sampled"]));

        if (!plan.Fog.Mode.Equals("analytic", StringComparison.OrdinalIgnoreCase))
        {
            resources.Add(Resource("fog-froxel", "R16G16B16A16_SFloat", plan.Fog.FroxelWidth, plan.Fog.FroxelHeight, plan.Fog.FroxelDepth, "transient", ["storage", "sampled", "transfer-source"]));
            resources.Add(Resource("fog-debug-readback", "R16G16B16A16_SFloat", plan.Fog.FroxelWidth, plan.Fog.FroxelHeight, plan.Fog.FroxelDepth, "transient", ["transfer-destination", "host-readback"]));
            resources.Add(Resource("fog-history", "R16G16B16A16_SFloat", plan.Fog.FroxelWidth, plan.Fog.FroxelHeight, plan.Fog.FroxelDepth, "persistent", ["sampled", "transfer-destination"]));
        }

        if (particlePlan.AllocatedCapacity > 0)
        {
            resources.Add(Resource("particle-state-a", "R32_UInt", particlePlan.AllocatedCapacity, 1, 16, "persistent", ["storage", "history-input"]));
            resources.Add(Resource("particle-state-b", "R32_UInt", particlePlan.AllocatedCapacity, 1, 16, "persistent", ["storage", "history-input"]));
            resources.Add(Resource("particle-emitter-data", "R32_UInt", Math.Max(1, particlePlan.Emitters.Count), 1, 40, "transient", ["transfer-destination", "storage"]));
            resources.Add(Resource("particle-active-indices", "R32_UInt", particlePlan.AllocatedCapacity, 1, 1, "transient", ["storage"]));
            resources.Add(Resource("particle-indirect", "R32_UInt", 4, 1, 1, "transient", ["storage", "indirect", "transfer-destination"]));
        }

        if (plan.Post.Bloom)
        {
            resources.Add(Resource("bloom-pyramid", "R16G16B16A16_SFloat", DivideRoundUp(plan.RenderWidth, 4), DivideRoundUp(plan.RenderHeight, 4), 1, "transient", ["storage", "sampled"]));
        }

        resources.Add(Resource("ldr-color", "R8G8B8A8_UNorm", plan.OutputWidth, plan.OutputHeight, 1, "transient", ["color-attachment", "sampled"]));
        resources.Add(Resource("present-output", "R8G8B8A8_UNorm", plan.OutputWidth, plan.OutputHeight, 1, "external", ["color-attachment", "present"]));

        var passes = new List<RekallAgeHighFidelityRenderPass>
        {
            Pass("depth-normal", "graphics", [], ["depth-buffer", "normal-buffer"], 0),
            Pass("shadow-directional", "graphics", [], ["shadow-directional"], 1),
            Pass("cluster-build", "compute", ["depth-buffer", "normal-buffer"], ClusterWrites(plan), 2),
            Pass("opaque-hdr", "graphics", OpaqueReads(plan), ["scene-hdr"], 3)
        };

        var nextOrder = 4;
        passes.Add(plan.Fog.Mode.Equals("analytic", StringComparison.OrdinalIgnoreCase)
            ? Pass("fog-integrate", "graphics", ["scene-hdr", "depth-buffer"], ["scene-hdr"], nextOrder++)
            : Pass("fog-integrate", "compute", ["scene-hdr", "depth-buffer", "fog-history"], ["fog-froxel", "fog-history", "scene-hdr"], nextOrder++));

        if (!plan.Fog.Mode.Equals("analytic", StringComparison.OrdinalIgnoreCase))
        {
            passes.Add(Pass("fog-debug-readback", "transfer", ["fog-froxel"], ["fog-debug-readback"], nextOrder++));
        }


        if (particlePlan.AllocatedCapacity > 0)
        {
            passes.Add(Pass("particle-upload", "transfer", [], ["particle-emitter-data"], nextOrder++));
            passes.Add(Pass(
                "particle-simulate",
                "compute",
                [particlePlan.SimulationSource, "particle-emitter-data"],
                [particlePlan.SimulationDestination, "particle-active-indices", "particle-indirect"],
                nextOrder++));
        }

        var transparentReads = new List<string> { "depth-buffer", "scene-hdr" };
        if (particlePlan.AllocatedCapacity > 0)
        {
            transparentReads.Add(particlePlan.SimulationDestination);
            transparentReads.Add("particle-active-indices");
            transparentReads.Add("particle-indirect");
        }
        passes.Add(Pass("transparent-particles", "graphics", transparentReads, ["scene-hdr"], nextOrder++));
        if (plan.Post.Bloom)
        {
            passes.Add(Pass("bloom", "compute", ["scene-hdr"], ["bloom-pyramid"], nextOrder++));
        }

        var toneMapReads = new List<string> { "scene-hdr" };
        if (plan.Post.Bloom)
        {
            toneMapReads.Add("bloom-pyramid");
        }

        passes.Add(Pass("tone-map", "graphics", toneMapReads, ["ldr-color"], nextOrder++));
        passes.Add(Pass("ui", "graphics", ["ldr-color"], ["ldr-color"], nextOrder++));
        passes.Add(Pass("present", "present", ["ldr-color"], ["present-output"], nextOrder));

        var extraParticlePersistentBytes = checked((long)particlePlan.AllocatedCapacity * 64L);
        var particleTransientBytes = particlePlan.AllocatedCapacity == 0
            ? 0L
            : checked((long)particlePlan.AllocatedCapacity * sizeof(uint)
                + (long)Math.Max(1, particlePlan.Emitters.Count) * 160L
                + 4L * sizeof(uint));
        var graph = RekallAgeHighFidelityRenderGraph.Create(
            resources,
            passes,
            transientBudgetBytes: checked(plan.EstimatedTransientBytes + particleTransientBytes),
            persistentBudgetBytes: checked(plan.EstimatedPersistentBytes + extraParticlePersistentBytes));
        if (frame.Width == plan.OutputWidth && frame.Height == plan.OutputHeight)
        {
            return graph;
        }

        return graph with
        {
            Diagnostics = graph.Diagnostics
                .Append(new RekallAgeHighFidelityRenderGraphDiagnostic(
                    "REKALL_RENDER_GRAPH_VIEWPORT_DIMENSIONS_MISMATCH",
                    "viewport",
                    $"Viewport dimensions {frame.Width}x{frame.Height} do not match resolved output dimensions {plan.OutputWidth}x{plan.OutputHeight}."))
                .ToArray()
        };
    }

    private static IReadOnlyList<string> ClusterWrites(RekallAgeResolvedRenderFeaturePlan plan) =>
        plan.Post.Ssao ? ["cluster-indices", "ssao-occlusion"] : ["cluster-indices"];

    private static IReadOnlyList<string> OpaqueReads(RekallAgeResolvedRenderFeaturePlan plan)
    {
        var reads = new List<string> { "depth-buffer", "normal-buffer", "shadow-directional", "cluster-indices" };
        if (plan.Post.Ssao)
        {
            reads.Add("ssao-occlusion");
        }

        return reads;
    }

    private static RekallAgeHighFidelityRenderResource Resource(
        string name,
        string format,
        int width,
        int height,
        int layers,
        string lifetime,
        IReadOnlyList<string> usage) => new(name, format, width, height, layers, lifetime, usage);

    private static RekallAgeHighFidelityRenderPass Pass(
        string name,
        string kind,
        IReadOnlyList<string> reads,
        IReadOnlyList<string> writes,
        int order) => new(name, kind, reads, writes, order, Enabled: true);

    private static int DivideRoundUp(int value, int divisor) => Math.Max(1, (value + divisor - 1) / divisor);
}
