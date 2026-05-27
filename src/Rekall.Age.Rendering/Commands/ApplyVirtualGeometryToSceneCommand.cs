using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Rendering.Commands;

public sealed record ApplyVirtualGeometryToSceneRequest(
    string ProjectRoot,
    string SceneName,
    int MinSourceTriangles = 10000,
    int Width = 1920,
    int Height = 1080,
    bool DebugOverlay = false,
    bool OverwriteExisting = false,
    double TargetPixelError = 1.5,
    int ClusterTriangleCount = 128,
    int MaxSelectedTriangles = 12000,
    int MaxLodLevel = 8,
    string DebugMode = "off");

public sealed record ApplyVirtualGeometryToSceneResult(
    string SceneName,
    int CandidateEntityCount,
    int AppliedEntityCount,
    int SkippedExistingEntityCount,
    IReadOnlyList<ApplyVirtualGeometrySceneEntity> AppliedEntities,
    IReadOnlyList<ApplyVirtualGeometrySceneEntity> SkippedExistingEntities);

public sealed record ApplyVirtualGeometrySceneEntity(
    string EntityId,
    string EntityName,
    int SourceTriangles);

public sealed class ApplyVirtualGeometryToSceneCommand
    : IRekallAgeCommand<ApplyVirtualGeometryToSceneRequest, ApplyVirtualGeometryToSceneResult>
{
    private readonly RekallAgeSceneStore _sceneStore = new();

    public string Name => "rekall.render.virtual_geometry.apply_scene";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Applies Rekall.VirtualGeometry to existing dense scene renderable entities using a generic source-triangle threshold.",
        typeof(ApplyVirtualGeometryToSceneRequest).FullName!,
        typeof(ApplyVirtualGeometryToSceneResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ApplyVirtualGeometryToSceneResult>> ExecuteAsync(
        ApplyVirtualGeometryToSceneRequest request,
        RekallAgeCommandContext context)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return RekallAgeCommandResult<ApplyVirtualGeometryToSceneResult>.Failure(
                Empty(request),
                "Virtual geometry scene application requires positive dimensions and non-negative thresholds.",
                errors);
        }

        var scene = await _sceneStore.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken)
            .ConfigureAwait(false);
        var sourceTrianglesByEntityId = await InspectSourceTrianglesByEntityIdAsync(request, scene, context.CancellationToken)
            .ConfigureAwait(false);
        var candidates = scene.Entities
            .Select(entity => new
            {
                Entity = entity,
                SourceTriangles = sourceTrianglesByEntityId.TryGetValue(entity.Id, out var triangles) ? triangles : 0
            })
            .Where(item => item.SourceTriangles >= request.MinSourceTriangles)
            .OrderBy(item => item.Entity.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Entity.Id, StringComparer.Ordinal)
            .ToArray();

        var applied = new List<ApplyVirtualGeometrySceneEntity>();
        var skippedExisting = new List<ApplyVirtualGeometrySceneEntity>();
        var updated = scene;
        foreach (var candidate in candidates)
        {
            var summary = new ApplyVirtualGeometrySceneEntity(
                candidate.Entity.Id,
                candidate.Entity.Name,
                candidate.SourceTriangles);
            if (candidate.Entity.Components.Any(component => component.Type.Equals("Rekall.VirtualGeometry", StringComparison.Ordinal))
                && !request.OverwriteExisting)
            {
                skippedExisting.Add(summary);
                continue;
            }

            updated = updated.ReplaceEntity(candidate.Entity.AddComponent(CreateVirtualGeometryComponent(request)));
            applied.Add(summary);
        }

        if (applied.Count > 0)
        {
            await _sceneStore.SaveAsync(request.ProjectRoot, updated, context.CancellationToken).ConfigureAwait(false);
            context.Transaction.RecordChangedResource(_sceneStore.GetScenePath(request.ProjectRoot, request.SceneName));
        }

        var result = new ApplyVirtualGeometryToSceneResult(
            scene.Name,
            candidates.Length,
            applied.Count,
            skippedExisting.Count,
            applied,
            skippedExisting);
        return RekallAgeCommandResult<ApplyVirtualGeometryToSceneResult>.Success(
            result,
            $"Applied virtual geometry to {applied.Count} of {candidates.Length} dense scene entity/entities.");
    }

    private static async ValueTask<IReadOnlyDictionary<string, int>> InspectSourceTrianglesByEntityIdAsync(
        ApplyVirtualGeometryToSceneRequest request,
        RekallAgeSceneDocument scene,
        CancellationToken cancellationToken)
    {
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            world,
            request.Width,
            request.Height,
            request.DebugOverlay).ForHeadsetOutput();
        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            request.ProjectRoot,
            frame,
            cancellationToken).ConfigureAwait(false);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets);
        return meshes
            .GroupBy(mesh => BaseEntityId(mesh.EntityId), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(mesh => mesh.VirtualGeometrySourceTriangleCount ?? mesh.Indices.Count / 3),
                StringComparer.Ordinal);
    }

    private static string BaseEntityId(string entityId)
    {
        var suffix = entityId.IndexOf(':', StringComparison.Ordinal);
        return suffix < 0 ? entityId : entityId[..suffix];
    }

    private static RekallAgeComponentDocument CreateVirtualGeometryComponent(ApplyVirtualGeometryToSceneRequest request)
    {
        return RekallAgeComponentDocument.Create(
            "Rekall.VirtualGeometry",
            new JsonObject
            {
                ["enabled"] = true,
                ["targetPixelError"] = request.TargetPixelError,
                ["clusterTriangleCount"] = request.ClusterTriangleCount,
                ["maxSelectedTriangles"] = request.MaxSelectedTriangles,
                ["maxLodLevel"] = request.MaxLodLevel,
                ["debugMode"] = string.IsNullOrWhiteSpace(request.DebugMode) ? "off" : request.DebugMode.Trim()
            });
    }

    private static IReadOnlyList<RekallAgeCommandError> Validate(ApplyVirtualGeometryToSceneRequest request)
    {
        var errors = new List<RekallAgeCommandError>();
        if (request.Width <= 0 || request.Height <= 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_VIRTUAL_GEOMETRY_APPLY_INVALID_REQUEST",
                "Virtual geometry apply dimensions must be positive.",
                $"{request.Width}x{request.Height}"));
        }

        if (request.MinSourceTriangles < 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_VIRTUAL_GEOMETRY_APPLY_INVALID_REQUEST",
                "Minimum source triangle threshold cannot be negative.",
                request.SceneName));
        }

        if (request.ClusterTriangleCount <= 0 || request.MaxSelectedTriangles <= 0 || request.MaxLodLevel < 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_VIRTUAL_GEOMETRY_APPLY_INVALID_REQUEST",
                "Virtual geometry cluster, selected triangle, and LOD settings must be positive.",
                request.SceneName));
        }

        if (request.TargetPixelError <= 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_VIRTUAL_GEOMETRY_APPLY_INVALID_REQUEST",
                "Virtual geometry target pixel error must be positive.",
                request.SceneName));
        }

        return errors;
    }

    private static ApplyVirtualGeometryToSceneResult Empty(ApplyVirtualGeometryToSceneRequest request)
    {
        return new ApplyVirtualGeometryToSceneResult(request.SceneName, 0, 0, 0, [], []);
    }
}
