using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering;
using Rekall.Age.Editor;
using Rekall.Age.Modeling.Commands;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Modeling;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Assets;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioContentDragPayload(
    string ContentId,
    string ContentKind,
    IReadOnlyList<string> Operations)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RekallAgeStudioContentDragPayload FromItem(RekallAgeContentBrowserItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(item.Id, item.Kind, item.Capabilities
            .Where(operation => operation is RekallAgeContentCapability.Assign or RekallAgeContentCapability.Place)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static RekallAgeStudioContentDragPayload FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var payload = JsonSerializer.Deserialize<RekallAgeStudioContentDragPayload>(json, JsonOptions)
            ?? throw new JsonException("The Studio content drag payload is empty.");
        if (string.IsNullOrWhiteSpace(payload.ContentId) || string.IsNullOrWhiteSpace(payload.ContentKind))
        {
            throw new JsonException("The Studio content drag payload has no stable content identity.");
        }
        return payload with { Operations = payload.Operations?.ToArray() ?? [] };
    }

    public static bool TryParse(string? json, out RekallAgeStudioContentDragPayload payload)
    {
        payload = null!;
        try
        {
            payload = FromJson(json!);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public bool Supports(string operation) => Operations.Contains(operation, StringComparer.OrdinalIgnoreCase);
}

internal sealed record RekallAgeStudioContentDropResult(
    bool Applied, string Code, string Summary, string? TransactionId = null);

internal sealed record RekallAgeStudioContentPropertyTarget(
    string EntityId,
    string ComponentType,
    string PropertyName,
    string AssetKind,
    bool EntityLocked,
    bool PropertyLocked);

internal sealed record RekallAgeStudioContentViewportTarget(
    Vector3? WorldHit,
    Vector3 CameraPosition,
    Vector3 CameraForward,
    float CameraFrontDistance);

internal sealed record RekallAgeStudioViewportPlacementContext(
    Vector3 CameraPosition,
    Vector3 CameraForward,
    Vector3 CameraRight,
    Vector3 CameraUp,
    float VerticalFieldOfViewDegrees)
{
    public static RekallAgeStudioViewportPlacementContext From(RekallAgeRuntimeViewportCamera? camera)
    {
        if (camera is null) return new(Vector3.Zero, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 65);
        var rotation = Quaternion.CreateFromYawPitchRoll(
            Degrees(camera.RotationY), Degrees(camera.RotationX), Degrees(camera.RotationZ));
        return new(
            new((float)camera.X, (float)camera.Y, (float)camera.Z),
            Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, rotation)),
            Vector3.Normalize(Vector3.Transform(Vector3.UnitX, rotation)),
            Vector3.Normalize(Vector3.Transform(Vector3.UnitY, rotation)),
            (float)Math.Clamp(camera.FieldOfViewDegrees, 1, 179));
    }

    public RekallAgeStudioContentViewportTarget TargetAt(double normalizedX, double normalizedY, double aspectRatio)
    {
        var tan = MathF.Tan(VerticalFieldOfViewDegrees * MathF.PI / 360f);
        var ray = Vector3.Normalize(CameraForward
            + CameraRight * (float)((normalizedX * 2 - 1) * Math.Max(0.1, aspectRatio) * tan)
            + CameraUp * (float)((1 - normalizedY * 2) * tan));
        Vector3? hit = null;
        if (Math.Abs(ray.Y) > 0.00001f)
        {
            var distance = -CameraPosition.Y / ray.Y;
            if (distance > 0 && float.IsFinite(distance)) hit = CameraPosition + ray * distance;
        }
        return new(hit, CameraPosition, CameraForward, 5);
    }

    private static float Degrees(double degrees) => (float)(degrees * Math.PI / 180d);
}

internal sealed record RekallAgeStudioContentPropertyMutation(
    string Tool,
    string EntityId,
    string ComponentType,
    string PropertyName,
    string PropertyValue);

internal sealed record RekallAgeStudioContentPlacement(
    string Tool,
    string ModelAssetId,
    Vector3 Position);

internal sealed record RekallAgeStudioContentCommandEvidence(
    bool Applied, string Code, string Summary, string? TransactionId);

internal interface IRekallAgeStudioContentPropertyMutationCommand
{
    ValueTask<RekallAgeStudioContentCommandEvidence> ExecuteAsync(
        RekallAgeStudioContentPropertyMutation request, CancellationToken cancellationToken);
}

internal interface IRekallAgeStudioContentPlacementCommand
{
    ValueTask<RekallAgeStudioContentCommandEvidence> ExecuteAsync(
        RekallAgeStudioContentPlacement request, CancellationToken cancellationToken);
}

internal interface IRekallAgeStudioContentDragResolver
{
    RekallAgeStudioContentDragPayload? Resolve(string contentId);
}

internal sealed class RekallAgeStudioContentDragResolver(
    Func<string, RekallAgeStudioContentDragPayload?> resolve) : IRekallAgeStudioContentDragResolver
{
    public RekallAgeStudioContentDragPayload? Resolve(string contentId) => resolve(contentId);
}

internal sealed class RekallAgeStudioContentPropertyMutationCommand(
    Func<RekallAgeStudioContentPropertyMutation, CancellationToken, ValueTask<RekallAgeStudioContentCommandEvidence>> execute)
    : IRekallAgeStudioContentPropertyMutationCommand
{
    public ValueTask<RekallAgeStudioContentCommandEvidence> ExecuteAsync(
        RekallAgeStudioContentPropertyMutation request, CancellationToken cancellationToken) =>
        execute(request, cancellationToken);
}

internal sealed class RekallAgeStudioContentPlacementCommand(
    Func<RekallAgeStudioContentPlacement, CancellationToken, ValueTask<RekallAgeStudioContentCommandEvidence>> execute)
    : IRekallAgeStudioContentPlacementCommand
{
    public ValueTask<RekallAgeStudioContentCommandEvidence> ExecuteAsync(
        RekallAgeStudioContentPlacement request, CancellationToken cancellationToken) =>
        execute(request, cancellationToken);
}

internal static class RekallAgeStudioImportedModelPublisher
{
    private const string ProvenanceAttributeName = "rekall.content.source.identity";
    private const int MaximumCollisionAttempts = 16;

    internal static RekallAgeStudioGeneratedModelIds GeneratedIds(RekallAgeContentBrowserItem item, int attempt)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        var identityInput = string.Join("\n", item.Id, item.Kind, item.Revision);
        var sourceIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityInput))).ToLowerInvariant();
        var readable = SanitizeIdPrefix(item.DisplayName);
        return new(
            BoundedHashedId(readable, "mesh", sourceIdentity, attempt),
            BoundedHashedId(readable, "model", sourceIdentity, attempt),
            sourceIdentity);
    }

    public static async ValueTask<string> EnsurePublishedAsync(
        RekallAgeWorkbenchSession session,
        RekallAgeContentBrowserItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(item);
        if (session.ProjectRoot is null) throw new InvalidOperationException("Open a project before placing content.");
        var modelStore = new RekallAgeModelAssetStore();
        var sourcePath = item.SourcePath ?? item.Path
            ?? throw new InvalidOperationException("The imported model source is unavailable.");
        var meshes = await new RekallAgeGlbMeshLoader().LoadAsync(item.Id, sourcePath, cancellationToken);
        if (meshes.Count == 0) throw new InvalidDataException("The imported model contains no triangle geometry.");
        var topology = ToTopology(meshes);
        var meshStore = new RekallAgeMeshAssetStore();
        for (var attempt = 0; attempt < MaximumCollisionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ids = GeneratedIds(item, attempt);
            var marker = "rekall-content:" + ids.SourceIdentity;
            var modelPath = modelStore.GetModelPath(session.ProjectRoot, ids.ModelAssetId);
            var meshPath = meshStore.GetMeshPath(session.ProjectRoot, ids.MeshAssetId);
            if (File.Exists(modelPath))
            {
                var model = await modelStore.LoadAsync(session.ProjectRoot, ids.ModelAssetId, cancellationToken);
                if (model.Source.AssetId == ids.MeshAssetId && model.Source.OutputName == marker
                    && File.Exists(meshPath)
                    && MeshBelongsTo(await meshStore.LoadAsync(session.ProjectRoot, ids.MeshAssetId, cancellationToken), ids.SourceIdentity))
                    return ids.ModelAssetId;
                continue;
            }

            if (File.Exists(meshPath)
                && !MeshBelongsTo(await meshStore.LoadAsync(session.ProjectRoot, ids.MeshAssetId, cancellationToken), ids.SourceIdentity))
                continue;

            if (!File.Exists(meshPath))
            {
                var provenance = new RekallAgeGeometryAttribute(
                    ProvenanceAttributeName, RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.String,
                    topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(ids.SourceIdentity)).ToArray(),
                    Semantic: "content-source-identity");
                var created = await session.ExecuteAsync(
                    "rekall.mesh.create_asset",
                    JsonSerializer.Serialize(new CreateMeshAssetRequest(
                        session.ProjectRoot, ids.MeshAssetId, item.DisplayName, topology, [provenance])),
                    $"Create editable mesh for {item.DisplayName}", "studio", cancellationToken);
                if (!created.Ok) throw new InvalidOperationException(created.Summary);
            }

            var published = await session.ExecuteAsync(
                "rekall.asset.model.publish",
                JsonSerializer.Serialize(new PublishModelAssetRequest(
                    session.ProjectRoot, ids.ModelAssetId, item.DisplayName,
                    new(RekallAgeModelSourceKind.Mesh, ids.MeshAssetId, marker), RekallAgeDocumentRevision.Missing)),
                $"Publish Model Asset for {item.DisplayName}", "studio", cancellationToken);
            if (!published.Ok) throw new InvalidOperationException(published.Summary);
            return ids.ModelAssetId;
        }

        throw new InvalidDataException("REKALL_CONTENT_MODEL_ID_COLLISION: Could not allocate an imported Model Asset identity.");
    }

    private static RekallAgeMeshTopology ToTopology(IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        var positions = new List<RekallAgeGeometryVector3>();
        var faces = new List<int[]>();
        foreach (var mesh in meshes)
        {
            var offset = positions.Count;
            positions.AddRange(mesh.Vertices.Select(vertex =>
                new RekallAgeGeometryVector3(vertex.X, vertex.Y, vertex.Z)));
            for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
                faces.Add([offset + checked((int)mesh.Indices[index]),
                    offset + checked((int)mesh.Indices[index + 1]),
                    offset + checked((int)mesh.Indices[index + 2])]);
        }

        var edgeMap = new Dictionary<(int, int), int>();
        var edges = new List<RekallAgeMeshEdgePointIndices>();
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var offsets = new List<int> { 0 };
        foreach (var face in faces)
        {
            for (var index = 0; index < face.Length; index++)
            {
                var a = face[index]; var b = face[(index + 1) % face.Length];
                var key = a < b ? (a, b) : (b, a);
                if (!edgeMap.TryGetValue(key, out var edge))
                {
                    edge = edges.Count; edgeMap[key] = edge; edges.Add(new(a, b));
                }
                cornerPoints.Add(a); cornerEdges.Add(edge);
            }
            offsets.Add(cornerPoints.Count);
        }
        return new(
            Enumerable.Range(1, positions.Count).Select(index => (ulong)index).ToArray(), positions,
            Enumerable.Range(1, edges.Count).Select(index => (ulong)(10_000 + index)).ToArray(), edges,
            Enumerable.Range(1, faces.Count).Select(index => (ulong)(20_000 + index)).ToArray(), offsets,
            Enumerable.Range(1, cornerPoints.Count).Select(index => (ulong)(30_000 + index)).ToArray(),
            cornerPoints, cornerEdges);
    }

    private static bool MeshBelongsTo(RekallAgeMeshAsset mesh, string sourceIdentity) =>
        mesh.Attributes.Any(attribute => attribute.Name == ProvenanceAttributeName
            && attribute.Domain == RekallAgeGeometryDomain.Point
            && attribute.ValueType == RekallAgeGeometryValueType.String
            && attribute.Values.Count == mesh.Topology.PointIds.Count
            && attribute.Values.All(value => value.ValueKind == JsonValueKind.String && value.GetString() == sourceIdentity));

    private static string SanitizeIdPrefix(string value)
    {
        var chars = value.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-')
            .ToArray();
        var sanitized = new string(chars).Trim('-', '.', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "imported" : sanitized;
    }

    private static string BoundedHashedId(string prefix, string role, string sourceIdentity, int attempt)
    {
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceIdentity}\n{role}\n{attempt}")))
            .ToLowerInvariant()[..24];
        var tail = $"-{role}-{suffix}";
        return prefix[..Math.Min(prefix.Length, 128 - tail.Length)] + tail;
    }
}

internal sealed record RekallAgeStudioGeneratedModelIds(
    string MeshAssetId,
    string ModelAssetId,
    string SourceIdentity);

internal sealed class RekallAgeStudioContentDragService(
    IRekallAgeStudioContentPropertyMutationCommand propertyMutation,
    IRekallAgeStudioContentPlacementCommand placement,
    IRekallAgeStudioContentDragResolver resolver)
{
    internal const string DataFormat = "Rekall.AGE.Studio.Content.v1";
    public async ValueTask<RekallAgeStudioContentDropResult> AssignAsync(
        RekallAgeStudioContentDragPayload payload,
        RekallAgeStudioContentPropertyTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(payload, out var current, out var rejection)) return rejection;

        if (!current.Supports(RekallAgeContentCapability.Assign))
        {
            return Rejected("REKALL_CONTENT_DROP_NOT_ASSIGNABLE", "This content does not support property assignment.");
        }
        if (target.EntityLocked || target.PropertyLocked)
        {
            return Rejected("REKALL_CONTENT_DROP_LOCKED", "Unlock the entity and property before assigning content.");
        }
        if (!Compatible(current.ContentKind, target.AssetKind))
        {
            return Rejected("REKALL_CONTENT_DROP_INCOMPATIBLE", "The content kind is incompatible with this property.");
        }

        var evidence = await propertyMutation.ExecuteAsync(
            new("rekall.component.set_property", target.EntityId, target.ComponentType,
                target.PropertyName, current.ContentId), cancellationToken);
        return FromEvidence(evidence);
    }

    public async ValueTask<RekallAgeStudioContentDropResult> PlaceAsync(
        RekallAgeStudioContentDragPayload payload,
        RekallAgeStudioContentViewportTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(payload, out var current, out var rejection)) return rejection;

        if (!current.Supports(RekallAgeContentCapability.Place) || NormalizeKind(current.ContentKind) != "model")
        {
            return Rejected("REKALL_CONTENT_DROP_NOT_PLACEABLE", "Only place-capable model content can be dropped into the viewport.");
        }

        var position = target.WorldHit ?? CameraFrontPosition(target);
        var evidence = await placement.ExecuteAsync(
            new("rekall.model_asset.instantiate", current.ContentId, position), cancellationToken);
        return FromEvidence(evidence);
    }

    public bool CanAssign(RekallAgeStudioContentDragPayload payload, RekallAgeStudioContentPropertyTarget target) =>
        TryResolve(payload, out var current, out _)
        && current.Supports(RekallAgeContentCapability.Assign)
        && !target.EntityLocked && !target.PropertyLocked
        && Compatible(current.ContentKind, target.AssetKind);

    public bool CanPlace(RekallAgeStudioContentDragPayload payload) =>
        TryResolve(payload, out var current, out _)
        && current.Supports(RekallAgeContentCapability.Place) && NormalizeKind(current.ContentKind) == "model";

    private bool TryResolve(
        RekallAgeStudioContentDragPayload claimed,
        out RekallAgeStudioContentDragPayload current,
        out RekallAgeStudioContentDropResult rejection)
    {
        current = resolver.Resolve(claimed.ContentId)!;
        if (current is null)
        {
            rejection = Rejected("REKALL_CONTENT_DROP_STALE", "The dragged content is no longer present in the current project index.");
            return false;
        }
        if (!claimed.ContentKind.Equals(current.ContentKind, StringComparison.OrdinalIgnoreCase)
            || !claimed.Operations.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(current.Operations))
        {
            rejection = Rejected("REKALL_CONTENT_DROP_PAYLOAD_MISMATCH", "The drag data no longer matches the current indexed content.");
            return false;
        }
        rejection = null!;
        return true;
    }

    private static bool Compatible(string contentKind, string assetKind) =>
        NormalizeKind(contentKind).Equals(NormalizeKind(assetKind), StringComparison.Ordinal);

    private static string NormalizeKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "image" or "texture2d" or "texture-2d" => "texture",
        "model-asset" or "model_asset" or "glb" or "gltf" => "model",
        "sound" or "audio-clip" or "audioclip" => "audio",
        _ => kind.Trim().ToLowerInvariant()
    };

    private static Vector3 CameraFrontPosition(RekallAgeStudioContentViewportTarget target)
    {
        var forward = target.CameraForward.LengthSquared() > 0.000001f
            ? Vector3.Normalize(target.CameraForward)
            : Vector3.UnitZ;
        var distance = float.IsFinite(target.CameraFrontDistance) && target.CameraFrontDistance > 0
            ? target.CameraFrontDistance
            : 5f;
        return target.CameraPosition + forward * distance;
    }

    private static RekallAgeStudioContentDropResult FromEvidence(RekallAgeStudioContentCommandEvidence evidence) =>
        new(evidence.Applied, evidence.Code, evidence.Summary, evidence.TransactionId);

    private static RekallAgeStudioContentDropResult Rejected(string code, string summary) =>
        new(false, code, summary);
}
