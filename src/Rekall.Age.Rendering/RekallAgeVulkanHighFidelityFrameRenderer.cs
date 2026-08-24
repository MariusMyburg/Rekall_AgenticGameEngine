using System.Globalization;
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
        var diagnostics = new List<RekallAgeHighFidelityRenderGraphDiagnostic>(graph.Diagnostics);
        var bloom = frame.PostProcessStack.Passes.FirstOrDefault(pass =>
            pass.Type.Equals("bloom", StringComparison.OrdinalIgnoreCase)
            || pass.Type.Equals("brightExtract", StringComparison.OrdinalIgnoreCase));
        var composite = frame.PostProcessStack.Passes.FirstOrDefault(pass =>
            pass.Type.Equals("composite", StringComparison.OrdinalIgnoreCase));
        for (var index = 0; index < frame.PostProcessStack.Passes.Count; index++)
        {
            AddPostDiagnostics(frame.PostProcessStack.Passes[index], index, resolved, diagnostics);
        }

        graph = graph with { Diagnostics = diagnostics };
        return new RekallAgeVulkanHighFidelityFramePlan(
            graph,
            new RekallAgeHighFidelityPostSettings(
                Exposure: 1,
                WhitePoint: 16,
                Saturation: 1,
                Contrast: 1.05,
                GradeStrength: 1,
                BloomThreshold: ResolveNonNegative(bloom?.Threshold ?? 1),
                BloomIntensity: resolved.Post.Bloom
                    ? ResolveNonNegative(composite?.Intensity ?? bloom?.Intensity ?? 0.65)
                    : 0,
                BloomRadius: ResolveRadius(bloom?.Radius ?? 1)));
    }

    private static void AddPostDiagnostics(
        RekallAgeRuntimeViewportPostProcessPass pass,
        int index,
        RekallAgeResolvedRenderFeaturePlan resolved,
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics)
    {
        var type = pass.Type.Trim().ToLowerInvariant();
        var prefix = $"post.pass[{index}]";
        if (type is "bloom" or "brightextract")
        {
            if (!resolved.Post.Bloom)
            {
                AddDiagnostic(
                    diagnostics,
                    "REKALL_RENDER_POST_PASS_DISABLED_BY_RESOLVED_QUALITY",
                    $"{prefix}.type",
                    pass.Type,
                    "ignored");
            }

            if (!NearlyEqual(pass.Scale, 0.25))
            {
                AddDiagnostic(diagnostics, "REKALL_RENDER_POST_SETTING_UNSUPPORTED", $"{prefix}.scale", pass.Scale, 0.25);
            }

            if (pass.Iterations != 1)
            {
                AddDiagnostic(diagnostics, "REKALL_RENDER_POST_SETTING_UNSUPPORTED", $"{prefix}.iterations", pass.Iterations, 1);
            }

            if (!double.IsFinite(pass.Threshold) || pass.Threshold < 0)
            {
                AddDiagnostic(diagnostics, "REKALL_RENDER_POST_SETTING_CLAMPED", $"{prefix}.threshold", pass.Threshold, ResolveNonNegative(pass.Threshold));
            }

            if (!double.IsFinite(pass.Intensity) || pass.Intensity < 0)
            {
                AddDiagnostic(diagnostics, "REKALL_RENDER_POST_SETTING_CLAMPED", $"{prefix}.intensity", pass.Intensity, ResolveNonNegative(pass.Intensity));
            }

            if (!double.IsFinite(pass.Radius) || pass.Radius < 0.05 || pass.Radius > 32)
            {
                AddDiagnostic(diagnostics, "REKALL_RENDER_POST_SETTING_CLAMPED", $"{prefix}.radius", pass.Radius, ResolveRadius(pass.Radius));
            }

            if (!pass.BlendMode.Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                AddDiagnostic(diagnostics, "REKALL_RENDER_POST_SETTING_UNSUPPORTED", $"{prefix}.blendMode", pass.BlendMode, "add");
            }

            return;
        }

        if (type == "composite")
        {
            if (!double.IsFinite(pass.Intensity) || pass.Intensity < 0)
            {
                AddDiagnostic(diagnostics, "REKALL_RENDER_POST_SETTING_CLAMPED", $"{prefix}.intensity", pass.Intensity, ResolveNonNegative(pass.Intensity));
            }

            if (!pass.BlendMode.Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                AddDiagnostic(diagnostics, "REKALL_RENDER_POST_SETTING_UNSUPPORTED", $"{prefix}.blendMode", pass.BlendMode, "add");
            }

            return;
        }

        if (type is "tone-map" or "tonemap")
        {
            return;
        }

        AddDiagnostic(
            diagnostics,
            "REKALL_RENDER_POST_PASS_UNSUPPORTED",
            $"{prefix}.type",
            pass.Type,
            "ignored");
    }

    private static void AddDiagnostic(
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics,
        string code,
        string target,
        object? requested,
        object? resolved)
    {
        var requestedValue = ToInvariantString(requested);
        var resolvedValue = ToInvariantString(resolved);
        diagnostics.Add(new RekallAgeHighFidelityRenderGraphDiagnostic(
            code,
            target,
            $"Authored post setting degraded: requested='{requestedValue}', resolved='{resolvedValue}'."));
    }

    private static double ResolveNonNegative(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static double ResolveRadius(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.05, 32) : 1;

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 0.000001;

    private static string ToInvariantString(object? value) => value switch
    {
        null => string.Empty,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
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
    double BloomIntensity,
    double BloomRadius);

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
