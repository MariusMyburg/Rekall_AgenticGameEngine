using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Produces deterministic, backend-neutral directional shadow cascade data from visible scene facts.
/// </summary>
public sealed class RekallAgeVulkanShadowCascadePlanner
{
    private const float PracticalSplitWeight = 0.65f;
    private const float MinimumExtent = 0.01f;

    public RekallAgeVulkanShadowPlan Plan(
        RekallAgeVulkanShadowCamera camera,
        RekallAgeVulkanDirectionalShadowLight light,
        IReadOnlyList<RekallAgeVulkanShadowCaster> casters,
        RekallAgeResolvedShadowQuality quality)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(casters);
        ArgumentNullException.ThrowIfNull(quality);

        var orthographic = camera.ProjectionMode.Equals("orthographic", StringComparison.OrdinalIgnoreCase);
        var perspective = camera.ProjectionMode.Equals("perspective", StringComparison.OrdinalIgnoreCase);
        if (!IsFinite(camera.Position)
            || !IsFinite(camera.Forward)
            || !IsFinite(camera.Up)
            || !float.IsFinite(camera.AspectRatio)
            || !float.IsFinite(camera.NearClip)
            || !float.IsFinite(camera.FarClip)
            || camera.Forward.LengthSquared() < 0.000001f
            || camera.Up.LengthSquared() < 0.000001f
            || Vector3.Cross(camera.Up, camera.Forward).LengthSquared() < 0.000001f
            || (!perspective && !orthographic)
            || (perspective && (!float.IsFinite(camera.VerticalFieldOfViewRadians)
                || camera.VerticalFieldOfViewRadians <= 0
                || camera.VerticalFieldOfViewRadians >= MathF.PI))
            || (orthographic && (!float.IsFinite(camera.OrthographicSize) || camera.OrthographicSize <= 0))
            || camera.AspectRatio <= 0
            || camera.NearClip <= 0
            || camera.FarClip <= camera.NearClip)
        {
            return Disabled(quality, "REKALL_SHADOW_CAMERA_INVALID", "The active camera pose or projection is non-finite or degenerate.");
        }

        if (!light.CastShadows)
        {
            return Disabled(quality, "REKALL_SHADOW_LIGHT_DISABLED", "The primary directional light does not cast shadows.");
        }

        if (!IsFinite(light.Direction)
            || light.Direction.LengthSquared() < 0.000001f
            || !float.IsFinite(light.MaximumDistance)
            || light.MaximumDistance <= camera.NearClip)
        {
            return Disabled(quality, "REKALL_SHADOW_LIGHT_INVALID", "The primary directional light shadow settings are non-finite or degenerate.");
        }

        var cascadeCount = Math.Clamp(quality.CascadeCount, 1, 4);
        var resolution = Math.Max(1, quality.Resolution);
        var maximumDistance = MathF.Min(camera.FarClip, light.MaximumDistance);
        var splits = BuildSplits(camera.NearClip, maximumDistance, cascadeCount);
        var selectedCasters = casters
            .Where(caster => caster.CastShadows
                && (caster.LayerMask & light.CasterMask) != 0
                && (camera.ReceiverMask & light.ReceiverMask) != 0
                && IsFinite(caster.BoundsMinimum)
                && IsFinite(caster.BoundsMaximum))
            .OrderBy(caster => caster.EntityId, StringComparer.Ordinal)
            .ToArray();
        var cascades = new List<RekallAgeVulkanShadowCascade>(cascadeCount);
        var splitNear = camera.NearClip;
        for (var index = 0; index < cascadeCount; index++)
        {
            var splitFar = splits[index];
            var corners = BuildFrustumCorners(camera, splitNear, splitFar);
            var matrix = BuildStableLightMatrix(corners, light.Direction, light.MaximumDistance, resolution);
            var casterIds = selectedCasters
                .Where(caster => IntersectsCascadeBounds(caster, matrix))
                .Select(caster => caster.EntityId)
                .ToArray();
            cascades.Add(new RekallAgeVulkanShadowCascade(
                index,
                splitNear,
                splitFar,
                matrix,
                new RekallAgeVulkanShadowAtlasViewport(0, 0, resolution, resolution, index),
                casterIds,
                Math.Max(0, selectedCasters.Length - casterIds.Length),
                checked((long)resolution * resolution * 4L)));
            splitNear = splitFar;
        }

        var cascadeCasterIds = cascades.SelectMany(cascade => cascade.CasterIds).ToHashSet(StringComparer.Ordinal);
        return new RekallAgeVulkanShadowPlan(
            true,
            resolution,
            quality.FilterTapCount,
            light.DepthBias,
            light.NormalBias,
            light.MaximumDistance,
            light.Priority,
            Vector3.Normalize(camera.Forward),
            light.ReceiverMask,
            cascades,
            cascadeCasterIds.Count,
            Math.Max(0, casters.Count - cascadeCasterIds.Count),
            Array.Empty<RekallAgeVulkanShadowDiagnostic>());
    }

    private static float[] BuildSplits(float nearClip, float farClip, int cascadeCount)
    {
        var splits = new float[cascadeCount];
        var ratio = farClip / nearClip;
        for (var index = 1; index <= cascadeCount; index++)
        {
            var fraction = (float)index / cascadeCount;
            var logarithmic = nearClip * MathF.Pow(ratio, fraction);
            var linear = nearClip + (farClip - nearClip) * fraction;
            splits[index - 1] = index == cascadeCount
                ? farClip
                : logarithmic * PracticalSplitWeight + linear * (1 - PracticalSplitWeight);
        }

        return splits;
    }

    private static Vector3[] BuildFrustumCorners(
        RekallAgeVulkanShadowCamera camera,
        float splitNear,
        float splitFar)
    {
        var forward = Vector3.Normalize(camera.Forward);
        var right = Vector3.Normalize(Vector3.Cross(camera.Up, forward));
        var up = Vector3.Normalize(Vector3.Cross(forward, right));
        var orthographic = camera.ProjectionMode.Equals("orthographic", StringComparison.OrdinalIgnoreCase);
        var tangent = orthographic ? 0 : MathF.Tan(camera.VerticalFieldOfViewRadians * 0.5f);
        var nearHalfHeight = orthographic ? camera.OrthographicSize * 0.5f : tangent * splitNear;
        var nearHalfWidth = nearHalfHeight * camera.AspectRatio;
        var farHalfHeight = orthographic ? nearHalfHeight : tangent * splitFar;
        var farHalfWidth = farHalfHeight * camera.AspectRatio;
        var nearCenter = camera.Position + forward * splitNear;
        var farCenter = camera.Position + forward * splitFar;
        return
        [
            nearCenter - right * nearHalfWidth - up * nearHalfHeight,
            nearCenter + right * nearHalfWidth - up * nearHalfHeight,
            nearCenter - right * nearHalfWidth + up * nearHalfHeight,
            nearCenter + right * nearHalfWidth + up * nearHalfHeight,
            farCenter - right * farHalfWidth - up * farHalfHeight,
            farCenter + right * farHalfWidth - up * farHalfHeight,
            farCenter - right * farHalfWidth + up * farHalfHeight,
            farCenter + right * farHalfWidth + up * farHalfHeight
        ];
    }

    private static Matrix4x4 BuildStableLightMatrix(
        IReadOnlyList<Vector3> corners,
        Vector3 authoredLightDirection,
        float casterExtrusionDistance,
        int resolution)
    {
        var center = Vector3.Zero;
        foreach (var corner in corners)
        {
            center += corner;
        }
        center /= corners.Count;

        var lightDirection = Vector3.Normalize(authoredLightDirection);
        var up = MathF.Abs(Vector3.Dot(lightDirection, Vector3.UnitY)) > 0.98f
            ? Vector3.UnitZ
            : Vector3.UnitY;
        var radius = corners.Max(corner => Vector3.Distance(center, corner));
        radius = MathF.Ceiling(MathF.Max(radius, MinimumExtent) * 16f) / 16f;
        var orientation = Matrix4x4.CreateLookAt(Vector3.Zero, lightDirection, up);
        var centerLight = Vector3.Transform(center, orientation);
        var worldUnitsPerTexel = radius * 2f / resolution;
        centerLight.X = MathF.Floor(centerLight.X / worldUnitsPerTexel) * worldUnitsPerTexel;
        centerLight.Y = MathF.Floor(centerLight.Y / worldUnitsPerTexel) * worldUnitsPerTexel;
        centerLight.Z = MathF.Floor(centerLight.Z / worldUnitsPerTexel) * worldUnitsPerTexel;
        Matrix4x4.Invert(orientation, out var inverseOrientation);
        var snappedCenter = Vector3.Transform(centerLight, inverseOrientation);
        var depthPadding = radius + 10f;
        var eye = snappedCenter - lightDirection * (casterExtrusionDistance + depthPadding);
        var view = Matrix4x4.CreateLookAt(eye, snappedCenter, up);

        var projection = Matrix4x4.CreateOrthographicOffCenter(
            -radius,
            radius,
            -radius,
            radius,
            0.01f,
            casterExtrusionDistance + radius * 2f + 20f);
        return view * projection;
    }

    private static bool IntersectsCascadeBounds(
        RekallAgeVulkanShadowCaster caster,
        Matrix4x4 viewProjection)
    {
        var minimum = caster.BoundsMinimum;
        var maximum = caster.BoundsMaximum;
        var corners = new[]
        {
            new Vector3(minimum.X, minimum.Y, minimum.Z),
            new Vector3(maximum.X, minimum.Y, minimum.Z),
            new Vector3(minimum.X, maximum.Y, minimum.Z),
            new Vector3(maximum.X, maximum.Y, minimum.Z),
            new Vector3(minimum.X, minimum.Y, maximum.Z),
            new Vector3(maximum.X, minimum.Y, maximum.Z),
            new Vector3(minimum.X, maximum.Y, maximum.Z),
            new Vector3(maximum.X, maximum.Y, maximum.Z)
        };
        var clipMinimum = new Vector3(float.MaxValue);
        var clipMaximum = new Vector3(float.MinValue);
        foreach (var corner in corners)
        {
            var clip = Vector4.Transform(new Vector4(corner, 1), viewProjection);
            if (!float.IsFinite(clip.X) || !float.IsFinite(clip.Y) || !float.IsFinite(clip.Z) || MathF.Abs(clip.W) < 0.000001f)
            {
                return false;
            }

            var normalized = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
            clipMinimum = Vector3.Min(clipMinimum, normalized);
            clipMaximum = Vector3.Max(clipMaximum, normalized);
        }

        const float padding = 0.05f;
        return clipMaximum.X >= -1 - padding && clipMinimum.X <= 1 + padding
            && clipMaximum.Y >= -1 - padding && clipMinimum.Y <= 1 + padding
            && clipMaximum.Z >= -padding && clipMinimum.Z <= 1 + padding;
    }

    private static RekallAgeVulkanShadowPlan Disabled(
        RekallAgeResolvedShadowQuality quality,
        string code,
        string message) => new(
            false,
            Math.Max(1, quality.Resolution),
            Math.Max(0, quality.FilterTapCount),
            0,
            0,
            0,
            0,
            Vector3.UnitZ,
            uint.MaxValue,
            Array.Empty<RekallAgeVulkanShadowCascade>(),
            0,
            0,
            [new RekallAgeVulkanShadowDiagnostic(code, message)]);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public sealed record RekallAgeVulkanShadowCamera(
    Vector3 Position,
    Vector3 Forward,
    Vector3 Up,
    float VerticalFieldOfViewRadians,
    float AspectRatio,
    float NearClip,
    float FarClip,
    uint ReceiverMask = uint.MaxValue,
    string ProjectionMode = "perspective",
    float OrthographicSize = 10);

public sealed record RekallAgeVulkanDirectionalShadowLight(
    Vector3 Direction,
    bool CastShadows,
    uint CasterMask,
    uint ReceiverMask,
    float MaximumDistance,
    float DepthBias,
    float NormalBias,
    int Priority);

public sealed record RekallAgeVulkanShadowCaster(
    string EntityId,
    Vector3 BoundsMinimum,
    Vector3 BoundsMaximum,
    uint LayerMask,
    bool CastShadows);

public sealed record RekallAgeVulkanShadowPlan(
    bool Enabled,
    int Resolution,
    int FilterTapCount,
    float DepthBias,
    float NormalBias,
    float MaximumDistance,
    int LightPriority,
    Vector3 CameraForward,
    uint ReceiverMask,
    IReadOnlyList<RekallAgeVulkanShadowCascade> Cascades,
    int SelectedCasterCount,
    int CulledCasterCount,
    IReadOnlyList<RekallAgeVulkanShadowDiagnostic> Diagnostics)
{
    public string? LightEntityId { get; init; }
}

public sealed record RekallAgeVulkanShadowCascade(
    int Index,
    float SplitNear,
    float SplitFar,
    Matrix4x4 ViewProjection,
    RekallAgeVulkanShadowAtlasViewport AtlasViewport,
    IReadOnlyList<string> CasterIds,
    int CulledCasterCount,
    long AtlasBytes);

public readonly record struct RekallAgeVulkanShadowAtlasViewport(
    int X,
    int Y,
    int Width,
    int Height,
    int Layer);

public sealed record RekallAgeVulkanShadowDiagnostic(string Code, string Message);
