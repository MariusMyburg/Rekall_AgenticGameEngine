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
        => Plan(frame, null, null, null);

    public RekallAgeVulkanHighFidelityFramePlan? Plan(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh>? meshes) =>
        Plan(frame, meshes, null, null);

    public RekallAgeVulkanHighFidelityFramePlan? Plan(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh>? meshes,
        RekallAgeVulkanFogHistory? previousFogHistory,
        RekallAgeVulkanParticleHistory? previousParticleHistory = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.ResolvedQualityPlan is not { } resolved
            || frame.PostProcessStack is not { Enabled: true })
        {
            return null;
        }

        var particlePlan = new RekallAgeVulkanParticlePlanner().Plan(
            frame,
            resolved.Particles,
            frame.DeltaSeconds,
            previousParticleHistory);
        var graph = new RekallAgeHighFidelityRenderGraphBuilder().Build(frame, resolved, particlePlan);
        var diagnostics = new List<RekallAgeHighFidelityRenderGraphDiagnostic>(graph.Diagnostics);
        diagnostics.AddRange(particlePlan.Diagnostics.Select(diagnostic =>
            new RekallAgeHighFidelityRenderGraphDiagnostic(
                diagnostic.Code,
                "particle-simulate",
                diagnostic.Message)));
        var effectiveCamera = new RekallAgeVulkanSceneBatchBuilder().ResolveEffectiveCamera(frame, meshes ?? []);
        var selectedDirectionalRenderable = SelectDirectionalLight(frame);
        var directionalLight = selectedDirectionalRenderable is null
            ? RekallAgeVulkanDirectionalLightInjection.Disabled
            : ToDirectionalLightInjection(selectedDirectionalRenderable);
        var shadowPlan = BuildShadowPlan(frame, meshes, resolved.Shadows, effectiveCamera, selectedDirectionalRenderable);
        diagnostics.AddRange(shadowPlan.Diagnostics.Select(diagnostic =>
            new RekallAgeHighFidelityRenderGraphDiagnostic(
                diagnostic.Code,
                "shadow-directional",
                diagnostic.Message)));
        var fogPlan = new RekallAgeVulkanFogPlanner().Plan(
            frame,
            resolved.Fog,
            previousFogHistory,
            effectiveCamera: effectiveCamera) with
        {
            DirectLightAvailable = directionalLight.Available,
            ShadowAvailable = shadowPlan.Enabled,
            DirectLightEntityId = directionalLight.EntityId
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
        var environmentExposure = frame.Environment is { Exposure: var exposure } && double.IsFinite(exposure)
            ? Math.Clamp(exposure, -8, 8)
            : 0;
        var environmentWhitePoint = frame.Environment is { WhitePoint: var whitePoint } && double.IsFinite(whitePoint)
            ? Math.Clamp(whitePoint, 0.1, 64)
            : 11.2;
        return new RekallAgeVulkanHighFidelityFramePlan(
            graph,
            new RekallAgeHighFidelityPostSettings(
                Exposure: environmentExposure,
                WhitePoint: environmentWhitePoint,
                Saturation: 1,
                Contrast: 1.05,
                GradeStrength: 1,
                BloomThreshold: ResolveNonNegative(bloom?.Threshold ?? 1),
                BloomIntensity: resolved.Post.Bloom
                    ? ResolveNonNegative(composite?.Intensity ?? bloom?.Intensity ?? 0.65)
                    : 0,
                BloomRadius: ResolveRadius(bloom?.Radius ?? 1)),
            shadowPlan,
            fogPlan,
            particlePlan)
        {
            DirectionalLight = directionalLight,
            EffectiveCamera = effectiveCamera,
            QualityPlan = resolved
        };
    }

    private static RekallAgeVulkanShadowPlan BuildShadowPlan(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh>? meshes,
        RekallAgeResolvedShadowQuality quality,
        RekallAgeVulkanEffectiveCamera camera,
        RekallAgeRuntimeViewportRenderable? light)
    {
        if (light is null)
        {
            return DisabledShadowPlan(quality, "REKALL_SHADOW_LIGHT_MISSING", "No visible directional light is available for the shadow pass.");
        }

        var shadowCamera = new RekallAgeVulkanShadowCamera(
            camera.Position,
            camera.Forward,
            camera.Up,
            camera.Orthographic ? MathF.PI / 3 : 2 * MathF.Atan(camera.TangentOrHalfHeight),
            camera.Aspect,
            camera.NearClip,
            camera.FarClip,
            ReceiverMask: uint.MaxValue,
            ProjectionMode: camera.Orthographic ? "orthographic" : "perspective",
            OrthographicSize: camera.TangentOrHalfHeight * 2);
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
            LightEntityId = light.EntityId
        };
    }

    private static RekallAgeRuntimeViewportRenderable? SelectDirectionalLight(
        RekallAgeRuntimeViewportFrame frame) => frame.Renderables
        .Where(item => item.Kind.Equals("light", StringComparison.Ordinal)
            && IsDirectionalLightVariant(item.Variant)
            && item.Intensity > 0.0001)
        .OrderByDescending(item => item.ShadowPriority)
        .ThenBy(item => item.EntityId, StringComparer.Ordinal)
        .FirstOrDefault();

    private static bool IsDirectionalLightVariant(string? variant)
    {
        var normalized = variant?.Trim();
        return normalized?.Equals("DirectionalLight", StringComparison.OrdinalIgnoreCase) == true
            || normalized?.Equals("Rekall.DirectionalLight", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static RekallAgeVulkanDirectionalLightInjection ToDirectionalLightInjection(
        RekallAgeRuntimeViewportRenderable light)
    {
        var color = ParseLightColor(light.MaterialColor);
        var intensity = (float)Math.Clamp(light.Intensity, 0.05, 4.0);
        return new RekallAgeVulkanDirectionalLightInjection(
            true,
            light.EntityId,
            DirectionFromEuler(light.RotationX, light.RotationY, light.RotationZ),
            new Vector4(color * intensity, 1));
    }

    private static Vector3 ParseLightColor(string? color)
    {
        if (color is { Length: 7 or 9 } && color[0] == '#'
            && byte.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            && byte.TryParse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            && byte.TryParse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return new Vector3(red / 255f, green / 255f, blue / 255f);
        }

        return Vector3.One;
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
    RekallAgeVulkanFogPlan FogPlan,
    RekallAgeVulkanParticlePlan ParticlePlan)
{
    public bool Ready => Graph.IsValid;

    public RekallAgeVulkanDirectionalLightInjection DirectionalLight { get; init; } =
        RekallAgeVulkanDirectionalLightInjection.Disabled;

    public RekallAgeVulkanEffectiveCamera EffectiveCamera { get; init; } =
        RekallAgeVulkanEffectiveCamera.Default;

    public RekallAgeResolvedRenderFeaturePlan? QualityPlan { get; init; }
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

    public RekallAgeHighFidelityParticleReport? Particles { get; init; }

    public IReadOnlyList<RekallAgeHighFidelityParticleDebugCapture> ParticleDebugCaptures { get; init; } =
        Array.Empty<RekallAgeHighFidelityParticleDebugCapture>();

    public RekallAgeResolvedRenderFeaturePlan? QualityPlan { get; init; }

    public RekallAgeGpuFrameTimingReport GpuTimings { get; init; } =
        RekallAgeGpuFrameTimingReport.Unavailable(0);

    public long ResourceBytes { get; init; }

    public int DrawCount { get; init; }

    public int DispatchCount { get; init; }
}

public sealed record RekallAgeHighFidelityParticleReport(
    bool Enabled,
    int AllocatedCapacity,
    int PlannedSpawnCount,
    RekallAgeVulkanParticleDispatch SimulationDispatch,
    int SimulationDispatchCount,
    int DrawCount,
    bool IndirectDraw,
    bool DepthTested,
    bool DepthWrite,
    bool SceneDepthSampled,
    bool HdrOutput,
    double DeltaSeconds,
    IReadOnlyList<string> OverflowEntityIds,
    IReadOnlyList<string> RejectedEntityIds,
    IReadOnlyList<string> Diagnostics)
{
    public int StateResourceGeneration { get; init; }

    public bool PreviousStateReused { get; init; }

    public string SimulationSource { get; init; } = string.Empty;

    public string SimulationDestination { get; init; } = string.Empty;

    public int GpuActiveCount { get; init; }
}

public sealed record RekallAgeHighFidelityParticleDebugCapture(
    string Kind,
    string OutputPath,
    bool NonBlank,
    ulong ByteChecksum)
{
    public string Source { get; init; } = string.Empty;

    public string EvidenceResource { get; init; } = string.Empty;

    public ulong GpuSampleCount { get; init; }

    public int EvidenceWidth { get; init; }

    public int EvidenceHeight { get; init; }

    public int OutputWidth { get; init; }

    public int OutputHeight { get; init; }

    public ulong GpuEvidenceChecksum { get; init; }
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
    bool Allocated)
{
    public long EstimatedBytes { get; init; }
}

public sealed record RekallAgeHighFidelityFramePassReport(
    string Name,
    string Kind,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    bool Executed,
    int DispatchCount,
    int DrawCount)
{
    public double? GpuNanoseconds { get; init; }

    public double? GpuMilliseconds { get; init; }
}
