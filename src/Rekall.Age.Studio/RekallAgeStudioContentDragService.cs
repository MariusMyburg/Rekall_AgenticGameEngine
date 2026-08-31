using System.Numerics;
using System.Text.Json;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Rendering.Abstractions;

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

internal sealed class RekallAgeStudioContentDragService(
    IRekallAgeStudioContentPropertyMutationCommand propertyMutation,
    IRekallAgeStudioContentPlacementCommand placement)
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

        if (!payload.Supports(RekallAgeContentCapability.Assign))
        {
            return Rejected("REKALL_CONTENT_DROP_NOT_ASSIGNABLE", "This content does not support property assignment.");
        }
        if (target.EntityLocked || target.PropertyLocked)
        {
            return Rejected("REKALL_CONTENT_DROP_LOCKED", "Unlock the entity and property before assigning content.");
        }
        if (!Compatible(payload.ContentKind, target.AssetKind))
        {
            return Rejected("REKALL_CONTENT_DROP_INCOMPATIBLE", "The content kind is incompatible with this property.");
        }

        var evidence = await propertyMutation.ExecuteAsync(
            new("rekall.component.set_property", target.EntityId, target.ComponentType,
                target.PropertyName, payload.ContentId), cancellationToken);
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

        if (!payload.Supports(RekallAgeContentCapability.Place) || NormalizeKind(payload.ContentKind) != "model")
        {
            return Rejected("REKALL_CONTENT_DROP_NOT_PLACEABLE", "Only place-capable model content can be dropped into the viewport.");
        }

        var position = target.WorldHit ?? CameraFrontPosition(target);
        var evidence = await placement.ExecuteAsync(
            new("rekall.model_asset.instantiate", payload.ContentId, position), cancellationToken);
        return FromEvidence(evidence);
    }

    public bool CanAssign(RekallAgeStudioContentDragPayload payload, RekallAgeStudioContentPropertyTarget target) =>
        payload.Supports(RekallAgeContentCapability.Assign)
        && !target.EntityLocked && !target.PropertyLocked
        && Compatible(payload.ContentKind, target.AssetKind);

    public bool CanPlace(RekallAgeStudioContentDragPayload payload) =>
        payload.Supports(RekallAgeContentCapability.Place) && NormalizeKind(payload.ContentKind) == "model";

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
