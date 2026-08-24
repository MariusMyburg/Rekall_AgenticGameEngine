using System.Globalization;
using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Keeps high-fidelity frame orchestration separate from scene extraction and Vulkan resource ownership.
/// </summary>
public sealed class RekallAgeVulkanHighFidelityFrameRenderer
{
    public RekallAgeVulkanHighFidelityFramePlan? Plan(RekallAgeRuntimeViewportFrame frame)
        => Plan(frame, null, null);

    public RekallAgeVulkanHighFidelityFramePlan? Plan(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh>? meshes) =>
        Plan(frame, meshes, null);

    public RekallAgeVulkanHighFidelityFramePlan? Plan(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh>? meshes,
        RekallAgeVulkanFogHistory? previousFogHistory)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.ResolvedQualityPlan is not { } resolved
            || frame.PostProcessStack is not { Enabled: true })
        {
            return null;
        }

        var graph = new RekallAgeHighFidelityRenderGraphBuilder().Build(frame, resolved);
        var diagnostics = new List<RekallAgeHighFidelityRenderGraphDiagnostic>(graph.Diagnostics);
        var shadowPlan = BuildShadowPlan(frame, meshes, resolved.Shadows);
        diagnostics.AddRange(shadowPlan.Diagnostics.Select(diagnostic =>
            new RekallAgeHighFidelityRenderGraphDiagnostic(
                diagnostic.Code,
                "shadow-directional",
                diagnostic.Message)));
        var fogPlan = new RekallAgeVulkanFogPlanner().Plan(frame, resolved.Fog, previousFogHistory) with
        {
            DirectLightAvailable = frame.Renderables.Any(item =>
                item.Kind.Equals("light", StringComparison.Ordinal) && item.Intensity > 0.0001),
            ShadowAvailable = shadowPlan.Enabled,
            DirectLightEntityId = shadowPlan.LightEntityId ?? frame.Renderables.FirstOrDefault(item =>
                item.Kind.Equals("light", StringComparison.Ordinal) && item.Intensity > 0.0001)?.EntityId
        };
        diagnostics.AddRange(fogPlan.Diagnostics.Select(diagnostic =>
            new RekallAgeHighFidelityRenderGraphDiagnostic(
                diagnostic.Code,
                "fog-integrate",
                diagnostic.Message)));
        var bloom = frame.PostProcessStack.Passes.FirstOrDefault(pass =>
            pass.Type.Equals("bloom", StringComparison.OrdinalIgnoreCase)
            || pass.Type.Equals("brightExtract", StringComparison.OrdinalIgnoreCase));
        var composite = frame.PostProcessStack.Passes.FirstOrDefault(pass =>
            pass.Type.Equals("composite", StringComparison.OrdinalIgnoreCase));
        var retainedPasses = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < frame.PostProcessStack.Passes.Count; index++)
        {
            var pass = frame.PostProcessStack.Passes[index];
            AddPostDiagnostics(pass, index, resolved, diagnostics);
            AddCollapsedPassDiagnostic(pass, index, retainedPasses, diagnostics);
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
                BloomRadius: ResolveRadius(bloom?.Radius ?? 1)),
            shadowPlan,
            fogPlan);
    }

    private static RekallAgeVulkanShadowPlan BuildShadowPlan(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh>? meshes,
        RekallAgeResolvedShadowQuality quality)
    {
        var camera = frame.ActiveCamera;
        if (camera is null)
        {
            return DisabledShadowPlan(quality, "REKALL_SHADOW_CAMERA_INVALID", "Directional shadows require a finite active camera.");
        }

        var light = frame.Renderables
            .Where(item => item.Kind.Equals("light", StringComparison.Ordinal)
                && item.Variant?.Contains("point", StringComparison.OrdinalIgnoreCase) != true
                && item.Intensity > 0.0001)
            .OrderByDescending(item => item.ShadowPriority)
            .ThenBy(item => item.EntityId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (light is null)
        {
            return DisabledShadowPlan(quality, "REKALL_SHADOW_LIGHT_MISSING", "No visible directional light is available for the shadow pass.");
        }

        var shadowCamera = new RekallAgeVulkanShadowCamera(
            new Vector3((float)camera.X, (float)camera.Y, (float)camera.Z),
            DirectionFromEuler(camera.RotationX, camera.RotationY, camera.RotationZ),
            RotateDirection(Vector3.UnitY, camera.RotationX, camera.RotationY, camera.RotationZ),
            MathF.PI / 180f * (float)camera.FieldOfViewDegrees,
            frame.Height <= 0 ? 1 : frame.Width / (float)frame.Height,
            (float)camera.NearClip,
            (float)camera.FarClip,
            ReceiverMask: uint.MaxValue,
            ProjectionMode: camera.ProjectionMode,
            OrthographicSize: (float)camera.OrthographicSize);
        var shadowLight = new RekallAgeVulkanDirectionalShadowLight(
            DirectionFromEuler(light.RotationX, light.RotationY, light.RotationZ),
            light.CastShadows,
            light.ShadowCasterMask,
            light.ShadowReceiverMask,
            (float)light.ShadowMaximumDistance,
            (float)light.ShadowBias,
            (float)light.ShadowNormalBias,
            light.ShadowPriority);
        var plan = new RekallAgeVulkanShadowCascadePlanner().Plan(
            shadowCamera,
            shadowLight,
            BuildCasters(frame, meshes),
            quality);
        return plan with
        {
            LightEntityId = plan.Enabled ? light.EntityId : null
        };
    }

    private static IReadOnlyList<RekallAgeVulkanShadowCaster> BuildCasters(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh>? meshes)
    {
        var renderables = frame.Renderables.ToDictionary(item => item.EntityId, StringComparer.Ordinal);
        if (meshes is null)
        {
            return frame.Renderables
                .Where(item => item.Kind.Equals("mesh", StringComparison.Ordinal)
                    && !item.AlphaMode.Equals("blend", StringComparison.OrdinalIgnoreCase))
                .Select(item => new RekallAgeVulkanShadowCaster(
                    item.EntityId,
                    new Vector3(
                        (float)(item.X - Math.Abs(item.ScaleX)),
                        (float)(item.Y - Math.Abs(item.ScaleY)),
                        (float)(item.Z - Math.Abs(item.ScaleZ))),
                    new Vector3(
                        (float)(item.X + Math.Abs(item.ScaleX)),
                        (float)(item.Y + Math.Abs(item.ScaleY)),
                        (float)(item.Z + Math.Abs(item.ScaleZ))),
                    item.ShadowLayerMask,
                    item.CastShadows))
                .ToArray();
        }

        var casters = new List<RekallAgeVulkanShadowCaster>(meshes.Count);
        foreach (var mesh in meshes)
        {
            if (!renderables.TryGetValue(mesh.EntityId, out var renderable)
                || mesh.Vertices.Count == 0
                || mesh.AlphaMode.Equals("blend", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var model = CreateModelMatrix(renderable);
            var minimum = new Vector3(float.MaxValue);
            var maximum = new Vector3(float.MinValue);
            foreach (var vertex in mesh.Vertices)
            {
                var world = Vector3.Transform(new Vector3(vertex.X, vertex.Y, vertex.Z), model);
                minimum = Vector3.Min(minimum, world);
                maximum = Vector3.Max(maximum, world);
            }
            casters.Add(new RekallAgeVulkanShadowCaster(
                mesh.EntityId,
                minimum,
                maximum,
                mesh.ShadowLayerMask,
                mesh.CastShadows));
        }
        return casters;
    }

    private static Matrix4x4 CreateModelMatrix(RekallAgeRuntimeViewportRenderable renderable) =>
        Matrix4x4.CreateScale(
            (float)Math.Max(0.001, Math.Abs(renderable.ScaleX)),
            (float)Math.Max(0.001, Math.Abs(renderable.ScaleY)),
            (float)Math.Max(0.001, Math.Abs(renderable.ScaleZ)))
        * Matrix4x4.CreateRotationX(MathF.PI / 180f * (float)renderable.RotationX)
        * Matrix4x4.CreateRotationY(MathF.PI / 180f * (float)renderable.RotationY)
        * Matrix4x4.CreateRotationZ(MathF.PI / 180f * (float)renderable.RotationZ)
        * Matrix4x4.CreateTranslation((float)renderable.X, (float)renderable.Y, (float)renderable.Z);

    private static Vector3 DirectionFromEuler(double x, double y, double z) =>
        Vector3.Normalize(RotateDirection(Vector3.UnitZ, x, y, z));

    private static Vector3 RotateDirection(Vector3 value, double x, double y, double z)
    {
        var rotation = Matrix4x4.CreateRotationX(MathF.PI / 180f * (float)x)
            * Matrix4x4.CreateRotationY(MathF.PI / 180f * (float)y)
            * Matrix4x4.CreateRotationZ(MathF.PI / 180f * (float)z);
        return Vector3.TransformNormal(value, rotation);
    }

    private static RekallAgeVulkanShadowPlan DisabledShadowPlan(
        RekallAgeResolvedShadowQuality quality,
        string code,
        string message) => new(
            false,
            quality.Resolution,
            quality.FilterTapCount,
            0,
            0,
            0,
            0,
            Vector3.UnitZ,
            uint.MaxValue,
            [],
            0,
            0,
            [new RekallAgeVulkanShadowDiagnostic(code, message)]);

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
            AddRoutingDiagnostics(
                pass,
                prefix,
                resolved.Post.Bloom ? "scene-hdr" : "ignored",
                "ignored",
                resolved.Post.Bloom ? "bloom-pyramid" : "ignored",
                diagnostics);

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
            AddRoutingDiagnostics(
                pass,
                prefix,
                "scene-hdr",
                resolved.Post.Bloom ? "bloom-pyramid" : "ignored",
                "ldr-color",
                diagnostics);

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
            AddRoutingDiagnostics(pass, prefix, "scene-hdr", "ignored", "ldr-color", diagnostics);
            return;
        }

        AddDiagnostic(
            diagnostics,
            "REKALL_RENDER_POST_PASS_UNSUPPORTED",
            $"{prefix}.type",
            pass.Type,
            "ignored");
    }

    private static void AddCollapsedPassDiagnostic(
        RekallAgeRuntimeViewportPostProcessPass pass,
        int index,
        IDictionary<string, int> retainedPasses,
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics)
    {
        var semantic = pass.Type.Trim().ToLowerInvariant() switch
        {
            "bloom" or "brightextract" => "bloom",
            "composite" => "composite",
            "tone-map" or "tonemap" => "tone-map",
            _ => null
        };
        if (semantic is null)
        {
            return;
        }

        if (retainedPasses.TryAdd(semantic, index))
        {
            return;
        }

        var retainedIndex = retainedPasses[semantic];
        AddDiagnostic(
            diagnostics,
            "REKALL_RENDER_POST_PASS_COLLAPSED",
            $"post.pass[{index}].type",
            pass.Type,
            $"ignored; post.pass[{retainedIndex}] retained");
    }

    private static void AddRoutingDiagnostics(
        RekallAgeRuntimeViewportPostProcessPass pass,
        string prefix,
        string resolvedInput,
        string resolvedSource,
        string resolvedOutput,
        ICollection<RekallAgeHighFidelityRenderGraphDiagnostic> diagnostics)
    {
        if (!pass.Input.Equals("sceneColor", StringComparison.OrdinalIgnoreCase))
        {
            AddDiagnostic(
                diagnostics,
                "REKALL_RENDER_POST_ROUTING_SUBSTITUTED",
                $"{prefix}.input",
                pass.Input,
                resolvedInput);
        }

        if (pass.Source is not null)
        {
            AddDiagnostic(
                diagnostics,
                "REKALL_RENDER_POST_ROUTING_SUBSTITUTED",
                $"{prefix}.source",
                pass.Source,
                resolvedSource);
        }

        if (!pass.Output.Equals("sceneColor", StringComparison.OrdinalIgnoreCase))
        {
            AddDiagnostic(
                diagnostics,
                "REKALL_RENDER_POST_ROUTING_SUBSTITUTED",
                $"{prefix}.output",
                pass.Output,
                resolvedOutput);
        }
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
    RekallAgeHighFidelityPostSettings PostSettings,
    RekallAgeVulkanShadowPlan ShadowPlan,
    RekallAgeVulkanFogPlan FogPlan)
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
    IReadOnlyList<string> Diagnostics)
{
    public IReadOnlyList<RekallAgeHighFidelityShadowCascadeReport> ShadowCascades { get; init; } =
        Array.Empty<RekallAgeHighFidelityShadowCascadeReport>();

    public IReadOnlyList<RekallAgeHighFidelityShadowDebugCapture> ShadowDebugCaptures { get; init; } =
        Array.Empty<RekallAgeHighFidelityShadowDebugCapture>();

    public RekallAgeHighFidelityFogReport? Fog { get; init; }

    public IReadOnlyList<RekallAgeHighFidelityFogDebugCapture> FogDebugCaptures { get; init; } =
        Array.Empty<RekallAgeHighFidelityFogDebugCapture>();
}

public sealed record RekallAgeHighFidelityFogReport(
    string Mode,
    bool Enabled,
    RekallAgeVulkanFogGrid Grid,
    RekallAgeVulkanFogDispatch Dispatch,
    int DispatchCount,
    int PackedVolumeCount,
    IReadOnlyList<string> DroppedEntityIds,
    bool DirectLightInjected,
    bool ShadowAttenuationApplied,
    bool HistoryReset,
    bool TemporalReprojection)
{
    public bool SceneDepthSampled { get; init; }

    public bool HistoryDescriptorBound { get; init; }

    public bool HistorySampled { get; init; }

    public int HistoryResourceGeneration { get; init; }

    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    public string? DirectionalLightEntityId { get; init; }

    public bool CascadeShadowSampled { get; init; }
}

public sealed record RekallAgeHighFidelityFogDebugCapture(
    string Kind,
    int SliceIndex,
    string OutputPath,
    bool NonBlank,
    ulong ByteChecksum)
{
    public string Source { get; init; } = string.Empty;
}

public sealed record RekallAgeHighFidelityShadowDebugCapture(
    int CascadeIndex,
    float SplitNear,
    float SplitFar,
    string OutputPath,
    bool NonBlank,
    ulong ByteChecksum);

public sealed record RekallAgeHighFidelityShadowCascadeReport(
    int Index,
    float SplitNear,
    float SplitFar,
    int Resolution,
    int CasterCount,
    int DrawCount,
    int CulledCount,
    int FilterTapCount,
    long AtlasBytes,
    float DepthBias,
    float NormalBias);

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
