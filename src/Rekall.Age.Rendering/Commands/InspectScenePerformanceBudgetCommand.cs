using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;

namespace Rekall.Age.Rendering.Commands;

public sealed record InspectScenePerformanceBudgetRequest(
    string ProjectRoot,
    string SceneName,
    int Frames = 0,
    int Width = 1920,
    int Height = 1080,
    string Profile = "desktop60",
    bool DebugOverlay = false);

public sealed record InspectScenePerformanceBudgetResult(
    string SceneName,
    int FrameIndex,
    string Profile,
    int TargetFramesPerSecond,
    int EntityCount,
    int RenderableCount,
    int MeshCount,
    int DrawCalls,
    int EstimatedDrawInvocations,
    int Triangles,
    int Vertices,
    int TextureCount,
    int RuntimeTextureCount,
    int AssetIssueCount,
    bool StereoEnabled,
    bool UsesSinglePassMultiview,
    int EyeCount,
    long EstimatedRenderTargetPixels,
    long EstimatedGeometryBytes,
    IReadOnlyList<RekallAgeScenePerformanceLayerBreakdown> LayerBreakdown,
    IReadOnlyList<RekallAgeScenePerformanceCameraMask> CameraMasks,
    IReadOnlyList<RekallAgeScenePerformanceCulledRenderable> CulledRenderables,
    RekallAgeScenePerformanceBudgetLimits Limits,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Recommendations);

public sealed record RekallAgeScenePerformanceLayerBreakdown(
    string Layer,
    int RenderableCount,
    int MeshCount,
    int DrawCalls,
    int Triangles,
    int Vertices);

public sealed record RekallAgeScenePerformanceCameraMask(
    string EntityId,
    string EntityName,
    bool Active,
    string CullingMask,
    double RenderOrder = 0,
    double ViewportX = 0,
    double ViewportY = 0,
    double ViewportWidth = 1,
    double ViewportHeight = 1);

public sealed record RekallAgeScenePerformanceCulledRenderable(
    string EntityId,
    string EntityName,
    string Kind,
    string Layer,
    string Reason,
    string? CameraEntityName,
    string CullingMask);

public sealed record RekallAgeScenePerformanceBudgetLimits(
    int MaxDrawInvocations,
    int MaxTriangles,
    int MaxVertices,
    int MaxTextures,
    long MaxRenderTargetPixels);

public sealed class InspectScenePerformanceBudgetCommand
    : IRekallAgeCommand<InspectScenePerformanceBudgetRequest, InspectScenePerformanceBudgetResult>
{
    public string Name => "rekall.render.performance.inspect_scene_budget";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects scene geometry, texture, stereo, and render-target pressure against a named generic performance profile.",
        typeof(InspectScenePerformanceBudgetRequest).FullName!,
        typeof(InspectScenePerformanceBudgetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectScenePerformanceBudgetResult>> ExecuteAsync(
        InspectScenePerformanceBudgetRequest request,
        RekallAgeCommandContext context)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return RekallAgeCommandResult<InspectScenePerformanceBudgetResult>.Failure(
                Empty(request),
                "Scene performance budget inspection requires non-negative frames and positive dimensions.",
                validationErrors);
        }

        var profile = ResolveProfile(request.Profile);
        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            request.Frames,
            context.CancellationToken).ConfigureAwait(false);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            world,
            request.Width,
            request.Height,
            request.DebugOverlay);
        var renderFrame = frame.ForHeadsetOutput();
        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            request.ProjectRoot,
            renderFrame,
            context.CancellationToken).ConfigureAwait(false);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(renderFrame, assets);
        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(renderFrame, meshes);
        var layerBreakdown = BuildLayerBreakdown(renderFrame, meshes);
        var cameraMasks = BuildCameraMasks(frame);
        var stereoEnabled = renderFrame.Stereo is { Enabled: true };
        var usesSinglePassMultiview = renderFrame.Stereo is { Enabled: true, PreferSinglePassMultiview: true };
        var eyeCount = stereoEnabled
            ? Math.Max(1, renderFrame.Stereo?.EyeCount ?? 1)
            : profile.DefaultEyeCount;
        var drawInvocations = batch.Draws.Count * (usesSinglePassMultiview ? 1 : Math.Max(1, eyeCount));
        var triangles = checked(batch.Indices.Count / 3);
        var textureIds = batch.Draws
            .SelectMany(draw => new[]
            {
                draw.TextureId,
                draw.NormalTextureId,
                draw.MetallicRoughnessTextureId,
                draw.OcclusionTextureId,
                draw.EmissiveTextureId
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var renderTargetPixels = checked((long)Math.Max(1, request.Width) * Math.Max(1, request.Height) * Math.Max(1, eyeCount));
        var geometryBytes = EstimateGeometryBytes(batch.Vertices.Count, batch.Indices.Count);
        var blockers = BuildBlockers(profile, drawInvocations, triangles, batch.Vertices.Count, textureIds, renderTargetPixels);
        var warnings = BuildWarnings(profile, drawInvocations, triangles, batch.Vertices.Count, textureIds, renderTargetPixels, assets.Issues.Count);
        var result = new InspectScenePerformanceBudgetResult(
            world.SceneName,
            world.FrameIndex,
            profile.Id,
            profile.TargetFramesPerSecond,
            world.Entities.Count,
            renderFrame.Renderables.Count,
            meshes.Count,
            batch.Draws.Count,
            drawInvocations,
            triangles,
            batch.Vertices.Count,
            textureIds,
            assets.Textures.Count,
            assets.Issues.Count,
            stereoEnabled,
            usesSinglePassMultiview,
            eyeCount,
            renderTargetPixels,
            geometryBytes,
            layerBreakdown,
            cameraMasks,
            BuildCulledRenderables(renderFrame),
            profile.Limits,
            blockers,
            warnings,
            BuildRecommendations(profile, blockers, warnings, usesSinglePassMultiview, eyeCount));

        if (blockers.Count == 0)
        {
            return RekallAgeCommandResult<InspectScenePerformanceBudgetResult>.Success(
                result,
                $"Scene '{request.SceneName}' fits the {profile.Id} performance budget.");
        }

        return RekallAgeCommandResult<InspectScenePerformanceBudgetResult>.Failure(
            result,
            $"Scene '{request.SceneName}' exceeds the {profile.Id} performance budget.",
            [
                new RekallAgeCommandError(
                    "REKALL_SCENE_PERFORMANCE_BUDGET_EXCEEDED",
                    string.Join(" ", blockers),
                    request.SceneName)
            ]);
    }

    private static IReadOnlyList<RekallAgeCommandError> Validate(InspectScenePerformanceBudgetRequest request)
    {
        var errors = new List<RekallAgeCommandError>();
        if (request.Frames < 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_SCENE_PERFORMANCE_BUDGET_INVALID_REQUEST",
                "Frame count cannot be negative.",
                request.SceneName));
        }

        if (request.Width <= 0 || request.Height <= 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_SCENE_PERFORMANCE_BUDGET_INVALID_REQUEST",
                "Performance budget dimensions must be positive.",
                $"{request.Width}x{request.Height}"));
        }

        return errors;
    }

    private static IReadOnlyList<string> BuildBlockers(
        BudgetProfile profile,
        int drawInvocations,
        int triangles,
        int vertices,
        int textures,
        long renderTargetPixels)
    {
        var blockers = new List<string>();
        AddExceeded(blockers, "draw invocations", drawInvocations, profile.Limits.MaxDrawInvocations);
        AddExceeded(blockers, "triangles", triangles, profile.Limits.MaxTriangles);
        AddExceeded(blockers, "vertices", vertices, profile.Limits.MaxVertices);
        AddExceeded(blockers, "textures", textures, profile.Limits.MaxTextures);
        AddExceeded(blockers, "render-target pixels", renderTargetPixels, profile.Limits.MaxRenderTargetPixels);
        return blockers;
    }

    private static IReadOnlyList<string> BuildWarnings(
        BudgetProfile profile,
        int drawInvocations,
        int triangles,
        int vertices,
        int textures,
        long renderTargetPixels,
        int assetIssueCount)
    {
        var warnings = new List<string>();
        AddNearLimit(warnings, "draw invocations", drawInvocations, profile.Limits.MaxDrawInvocations);
        AddNearLimit(warnings, "triangles", triangles, profile.Limits.MaxTriangles);
        AddNearLimit(warnings, "vertices", vertices, profile.Limits.MaxVertices);
        AddNearLimit(warnings, "textures", textures, profile.Limits.MaxTextures);
        AddNearLimit(warnings, "render-target pixels", renderTargetPixels, profile.Limits.MaxRenderTargetPixels);
        if (assetIssueCount > 0)
        {
            warnings.Add($"{assetIssueCount} viewport asset issue(s) were reported while resolving textures or models.");
        }

        return warnings;
    }

    private static IReadOnlyList<string> BuildRecommendations(
        BudgetProfile profile,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        bool usesSinglePassMultiview,
        int eyeCount)
    {
        var recommendations = new List<string>();
        if (blockers.Any(item => item.Contains("draw", StringComparison.OrdinalIgnoreCase))
            || warnings.Any(item => item.Contains("draw", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("Batch repeated materials and merge static geometry authored by agents where interaction does not require separate entities.");
        }

        if (blockers.Any(item => item.Contains("triangles", StringComparison.OrdinalIgnoreCase))
            || blockers.Any(item => item.Contains("vertices", StringComparison.OrdinalIgnoreCase))
            || warnings.Any(item => item.Contains("triangles", StringComparison.OrdinalIgnoreCase))
            || warnings.Any(item => item.Contains("vertices", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("Generate lower-detail mesh variants and add Rekall.LodGroup levels so runtime rendering selects simpler geometry by camera distance.");
        }

        if (blockers.Any(item => item.Contains("textures", StringComparison.OrdinalIgnoreCase))
            || warnings.Any(item => item.Contains("textures", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("Pack small material textures into atlases and prefer shared samplers for repeated props.");
        }

        if (profile.Id.StartsWith("vr", StringComparison.OrdinalIgnoreCase)
            && eyeCount > 1
            && !usesSinglePassMultiview)
        {
            recommendations.Add("Use single-pass multiview stereo rendering for XR scenes to avoid duplicating draw submission per eye.");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("Scene is within budget; keep using this command after major geometry, texture, or XR camera changes.");
        }

        return recommendations;
    }

    private static IReadOnlyList<RekallAgeScenePerformanceLayerBreakdown> BuildLayerBreakdown(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        var meshesByEntityId = meshes
            .GroupBy(mesh => mesh.EntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var layers = new SortedDictionary<string, MutableLayerBreakdown>(StringComparer.Ordinal);
        foreach (var renderable in frame.Renderables)
        {
            var layer = RekallAgeRenderLayerMask.NormalizeLayer(renderable.Layer);
            if (!layers.TryGetValue(layer, out var breakdown))
            {
                breakdown = new MutableLayerBreakdown(layer);
                layers.Add(layer, breakdown);
            }

            breakdown.RenderableCount++;
            if (!meshesByEntityId.TryGetValue(renderable.EntityId, out var renderableMeshes))
            {
                continue;
            }

            breakdown.MeshCount += renderableMeshes.Length;
            breakdown.DrawCalls += renderableMeshes.Length;
            foreach (var mesh in renderableMeshes)
            {
                breakdown.Triangles += mesh.Indices.Count / 3;
                breakdown.Vertices += mesh.Vertices.Count;
            }
        }

        return layers.Values
            .Select(layer => new RekallAgeScenePerformanceLayerBreakdown(
                layer.Layer,
                layer.RenderableCount,
                layer.MeshCount,
                layer.DrawCalls,
                layer.Triangles,
                layer.Vertices))
            .ToArray();
    }

    private static IReadOnlyList<RekallAgeScenePerformanceCameraMask> BuildCameraMasks(
        RekallAgeRuntimeViewportFrame frame)
    {
        return frame.Cameras
            .Select(camera => new RekallAgeScenePerformanceCameraMask(
                camera.EntityId,
                camera.EntityName,
                camera.Active,
                RekallAgeRenderLayerMask.NormalizeCullingMask(camera.CullingMask),
                camera.RenderOrder,
                camera.ViewportX,
                camera.ViewportY,
                camera.ViewportWidth,
                camera.ViewportHeight))
            .OrderByDescending(camera => camera.Active)
            .ThenBy(camera => camera.RenderOrder)
            .ThenBy(camera => camera.EntityName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<RekallAgeScenePerformanceCulledRenderable> BuildCulledRenderables(
        RekallAgeRuntimeViewportFrame frame)
    {
        return frame.Culling.CulledRenderables
            .Select(renderable => new RekallAgeScenePerformanceCulledRenderable(
                renderable.EntityId,
                renderable.EntityName,
                renderable.Kind,
                renderable.Layer,
                renderable.Reason,
                renderable.CameraEntityName,
                renderable.CullingMask))
            .ToArray();
    }

    private static void AddExceeded(List<string> messages, string label, long value, long limit)
    {
        if (value > limit)
        {
            messages.Add($"{label} {value} exceeds budget {limit}.");
        }
    }

    private static void AddNearLimit(List<string> messages, string label, long value, long limit)
    {
        if (value <= limit && value >= Math.Ceiling(limit * 0.75))
        {
            messages.Add($"{label} {value} is near budget {limit}.");
        }
    }

    private static long EstimateGeometryBytes(int vertices, int indices)
    {
        const int approximateVertexBytes = 48;
        const int indexBytes = 4;
        return checked((long)vertices * approximateVertexBytes + (long)indices * indexBytes);
    }

    private static BudgetProfile ResolveProfile(string? profile)
    {
        return (profile ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "mobile" or "mobile60" => new BudgetProfile(
                "mobile60",
                60,
                1,
                new RekallAgeScenePerformanceBudgetLimits(250, 150_000, 100_000, 64, 2_500_000)),
            "vr" or "vr90" or "openxr" => new BudgetProfile(
                "vr90",
                90,
                2,
                new RekallAgeScenePerformanceBudgetLimits(1_500, 1_000_000, 650_000, 128, 8_500_000)),
            _ => new BudgetProfile(
                "desktop60",
                60,
                1,
                new RekallAgeScenePerformanceBudgetLimits(3_000, 2_000_000, 1_250_000, 256, 8_500_000))
        };
    }

    private static InspectScenePerformanceBudgetResult Empty(InspectScenePerformanceBudgetRequest request)
    {
        var profile = ResolveProfile(request.Profile);
        return new InspectScenePerformanceBudgetResult(
            request.SceneName,
            Math.Max(0, request.Frames),
            profile.Id,
            profile.TargetFramesPerSecond,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            false,
            profile.DefaultEyeCount,
            0,
            0,
            [],
            [],
            [],
            profile.Limits,
            [],
            [],
            []);
    }

    private sealed class MutableLayerBreakdown(string layer)
    {
        public string Layer { get; } = layer;

        public int RenderableCount { get; set; }

        public int MeshCount { get; set; }

        public int DrawCalls { get; set; }

        public int Triangles { get; set; }

        public int Vertices { get; set; }
    }

    private sealed record BudgetProfile(
        string Id,
        int TargetFramesPerSecond,
        int DefaultEyeCount,
        RekallAgeScenePerformanceBudgetLimits Limits);
}
