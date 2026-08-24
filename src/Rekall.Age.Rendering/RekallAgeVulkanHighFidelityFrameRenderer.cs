using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Keeps high-fidelity frame orchestration separate from scene extraction and Vulkan resource ownership.
/// </summary>
public sealed class RekallAgeVulkanHighFidelityFrameRenderer
{
    public RekallAgeVulkanHighFidelityFramePlan? Plan(RekallAgeRuntimeViewportFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.ResolvedQualityPlan is not { } resolved
            || frame.PostProcessStack is not { Enabled: true })
        {
            return null;
        }

        var graph = new RekallAgeHighFidelityRenderGraphBuilder().Build(frame, resolved);
        var bloom = frame.PostProcessStack.Passes.FirstOrDefault(pass =>
            pass.Type.Equals("bloom", StringComparison.OrdinalIgnoreCase)
            || pass.Type.Equals("brightExtract", StringComparison.OrdinalIgnoreCase));
        return new RekallAgeVulkanHighFidelityFramePlan(
            graph,
            new RekallAgeHighFidelityPostSettings(
                Exposure: 1,
                WhitePoint: 16,
                Saturation: 1,
                Contrast: 1.05,
                GradeStrength: 1,
                BloomThreshold: Math.Max(0, bloom?.Threshold ?? 1),
                BloomIntensity: resolved.Post.Bloom ? Math.Max(0, bloom?.Intensity ?? 0.65) : 0));
    }
}

public sealed record RekallAgeVulkanHighFidelityFramePlan(
    RekallAgeHighFidelityRenderGraph Graph,
    RekallAgeHighFidelityPostSettings PostSettings)
{
    public bool Ready => Graph.IsValid;
}

public sealed record RekallAgeHighFidelityPostSettings(
    double Exposure,
    double WhitePoint,
    double Saturation,
    double Contrast,
    double GradeStrength,
    double BloomThreshold,
    double BloomIntensity);

public sealed record RekallAgeHighFidelityFrameReport(
    bool Executed,
    string SceneColorFormat,
    string OutputColorFormat,
    IReadOnlyList<RekallAgeHighFidelityFrameResourceReport> Resources,
    IReadOnlyList<RekallAgeHighFidelityFramePassReport> Passes,
    IReadOnlyList<string> Diagnostics);

public sealed record RekallAgeHighFidelityFrameResourceReport(
    string Name,
    string Format,
    int Width,
    int Height,
    bool Allocated);

public sealed record RekallAgeHighFidelityFramePassReport(
    string Name,
    string Kind,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    bool Executed,
    int DispatchCount,
    int DrawCount);
