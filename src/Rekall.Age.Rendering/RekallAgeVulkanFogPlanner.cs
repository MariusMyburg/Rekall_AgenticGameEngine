using System.Globalization;
using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Resolves generic viewport fog facts into a bounded, deterministic native execution plan.
/// </summary>
public sealed class RekallAgeVulkanFogPlanner
{
    public const int DefaultMaximumLocalVolumes = 64;
    public const int DefaultMaximumGlobalVolumes = 8;
    public const long MaximumFroxelCellCount = 8_640_000;

    public RekallAgeVulkanFogPlan Plan(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeResolvedFogQuality quality,
        RekallAgeVulkanFogHistory? previousHistory = null,
        int maximumLocalVolumes = DefaultMaximumLocalVolumes)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(quality);
        maximumLocalVolumes = Math.Clamp(maximumLocalVolumes, 0, DefaultMaximumLocalVolumes);
        var diagnostics = new List<RekallAgeVulkanFogDiagnostic>();
        var mode = NormalizeMode(quality.Mode);
        var grid = ResolveGrid(mode, quality, diagnostics);
        var ordered = frame.FogVolumes
            .Select(Sanitize)
            .Where(item => item is not null)
            .Cast<RekallAgeVulkanFogVolume>()
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.EntityId, StringComparer.Ordinal)
            .ToArray();
        var selectedLocals = 0;
        var selectedGlobals = 0;
        var selected = new List<RekallAgeVulkanFogVolume>(ordered.Length);
        var dropped = new List<string>();
        foreach (var volume in ordered)
        {
            if (volume.Shape.Equals("global", StringComparison.Ordinal))
            {
                if (selectedGlobals >= DefaultMaximumGlobalVolumes)
                {
                    dropped.Add(volume.EntityId);
                    continue;
                }

                selectedGlobals++;
            }
            else
            {
                if (selectedLocals >= maximumLocalVolumes)
                {
                    dropped.Add(volume.EntityId);
                    continue;
                }

                selectedLocals++;
            }

            selected.Add(volume);
        }

        if (dropped.Count > 0)
        {
            diagnostics.Add(new RekallAgeVulkanFogDiagnostic(
                "REKALL_FOG_VOLUME_LIMIT_CLAMPED",
                $"Fog volume packing retained at most {maximumLocalVolumes.ToString(CultureInfo.InvariantCulture)} local and {DefaultMaximumGlobalVolumes.ToString(CultureInfo.InvariantCulture)} global volumes, and dropped {dropped.Count.ToString(CultureInfo.InvariantCulture)}.",
                dropped));
        }

        var history = BuildHistory(frame, mode, grid);
        var historyReset = ShouldResetHistory(previousHistory, history, diagnostics);
        var enabled = selected.Any(volume => volume.Density > 0.000001f || volume.Emission.LengthSquared() > 0.000001f);
        return new RekallAgeVulkanFogPlan(
            mode,
            enabled,
            grid,
            selected,
            dropped,
            historyReset,
            UsesFroxel(mode) && !historyReset,
            UsesFroxel(mode)
                ? new RekallAgeVulkanFogDispatch(
                    DivideRoundUp(grid.Width, 4),
                    DivideRoundUp(grid.Height, 4),
                    DivideRoundUp(grid.Depth, 4))
                : new RekallAgeVulkanFogDispatch(0, 0, 0),
            history,
            diagnostics);
    }

    private static RekallAgeVulkanFogGrid ResolveGrid(
        string mode,
        RekallAgeResolvedFogQuality quality,
        ICollection<RekallAgeVulkanFogDiagnostic> diagnostics)
    {
        if (!UsesFroxel(mode))
        {
            return new RekallAgeVulkanFogGrid(0, 0, 0);
        }

        var width = Math.Clamp(quality.FroxelWidth, 1, 512);
        var height = Math.Clamp(quality.FroxelHeight, 1, 512);
        var depth = Math.Clamp(quality.FroxelDepth, 1, 128);
        var requested = new RekallAgeVulkanFogGrid(quality.FroxelWidth, quality.FroxelHeight, quality.FroxelDepth);
        while ((long)width * height * depth > MaximumFroxelCellCount && depth > 1)
        {
            depth--;
        }

        while ((long)width * height * depth > MaximumFroxelCellCount && height > 1)
        {
            height--;
        }

        while ((long)width * height * depth > MaximumFroxelCellCount && width > 1)
        {
            width--;
        }

        var resolved = new RekallAgeVulkanFogGrid(width, height, depth);
        if (requested != resolved)
        {
            diagnostics.Add(new RekallAgeVulkanFogDiagnostic(
                "REKALL_FOG_GRID_LIMIT_CLAMPED",
                $"Requested fog grid {requested.Width}x{requested.Height}x{requested.Depth} resolved to {width}x{height}x{depth}.",
                []));
        }

        return resolved;
    }

    private static RekallAgeVulkanFogVolume? Sanitize(RekallAgeRuntimeViewportFogVolume source)
    {
        var shape = source.Shape.Trim().ToLowerInvariant();
        if (shape is not ("global" or "box" or "sphere"))
        {
            return null;
        }

        var transform = source.Transform;
        var position = new Vector3(
            FiniteFloat(transform.X),
            FiniteFloat(transform.Y),
            FiniteFloat(transform.Z));
        var halfExtents = new Vector3(
            PositiveExtent(transform.ScaleX),
            PositiveExtent(transform.ScaleY),
            PositiveExtent(transform.ScaleZ));
        var density = Math.Clamp(FiniteFloat(source.Density), 0, 64);
        var albedo = ParseColor(source.Albedo, Vector3.One);
        var emission = ParseColor(source.Emission, Vector3.Zero);
        var anisotropy = Math.Clamp(FiniteFloat(source.Anisotropy), -0.95f, 0.95f);
        var heightFalloff = Math.Clamp(FiniteFloat(source.HeightFalloff), 0, 64);
        var blendDistance = Math.Clamp(FiniteFloat(source.BlendDistance), 0, halfExtents.MaxComponent());
        return new RekallAgeVulkanFogVolume(
            source.EntityId,
            source.EntityName,
            shape,
            density,
            albedo,
            emission,
            anisotropy,
            heightFalloff,
            blendDistance,
            source.Priority,
            position,
            halfExtents,
            albedo * density);
    }

    private static RekallAgeVulkanFogHistory BuildHistory(
        RekallAgeRuntimeViewportFrame frame,
        string mode,
        RekallAgeVulkanFogGrid grid)
    {
        var camera = frame.ActiveCamera;
        return new RekallAgeVulkanFogHistory(
            frame.FrameIndex,
            camera?.EntityId,
            new Vector3(
                FiniteFloat(camera?.X ?? 0),
                FiniteFloat(camera?.Y ?? 0),
                FiniteFloat(camera?.Z ?? 0)),
            new Vector3(
                FiniteFloat(camera?.RotationX ?? 0),
                FiniteFloat(camera?.RotationY ?? 0),
                FiniteFloat(camera?.RotationZ ?? 0)),
            mode,
            grid);
    }

    private static bool ShouldResetHistory(
        RekallAgeVulkanFogHistory? previous,
        RekallAgeVulkanFogHistory current,
        ICollection<RekallAgeVulkanFogDiagnostic> diagnostics)
    {
        if (previous is null)
        {
            diagnostics.Add(new RekallAgeVulkanFogDiagnostic(
                "REKALL_FOG_HISTORY_INITIALIZED",
                "Fog temporal history starts from the current frame.",
                []));
            return true;
        }

        if (!previous.Mode.Equals(current.Mode, StringComparison.Ordinal) || previous.Grid != current.Grid)
        {
            diagnostics.Add(new RekallAgeVulkanFogDiagnostic(
                "REKALL_FOG_HISTORY_GRID_CHANGED",
                "Fog temporal history reset because the resolved mode or grid changed.",
                []));
            return true;
        }

        if (previous.CameraEntityId != current.CameraEntityId
            || current.FrameIndex != previous.FrameIndex + 1
            || Vector3.Distance(previous.CameraPosition, current.CameraPosition) > 1f
            || RotationDistance(previous.CameraRotationDegrees, current.CameraRotationDegrees) > 15f)
        {
            diagnostics.Add(new RekallAgeVulkanFogDiagnostic(
                "REKALL_FOG_HISTORY_CAMERA_CUT",
                "Fog temporal history reset because camera continuity was interrupted.",
                current.CameraEntityId is null ? [] : [current.CameraEntityId]));
            return true;
        }

        return false;
    }

    private static float RotationDistance(Vector3 left, Vector3 right) =>
        Math.Max(WrappedDegrees(left.X, right.X), Math.Max(WrappedDegrees(left.Y, right.Y), WrappedDegrees(left.Z, right.Z)));

    private static float WrappedDegrees(float left, float right)
    {
        var delta = Math.Abs(left - right) % 360f;
        return Math.Min(delta, 360f - delta);
    }

    private static Vector3 ParseColor(string value, Vector3 fallback)
    {
        var normalized = value.Trim();
        if (normalized.Length != 7 || normalized[0] != '#')
        {
            return fallback;
        }

        return byte.TryParse(normalized.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            && byte.TryParse(normalized.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            && byte.TryParse(normalized.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue)
                ? new Vector3(red / 255f, green / 255f, blue / 255f)
                : fallback;
    }

    private static string NormalizeMode(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "froxel-low" => "froxel-low",
        "froxel" => "froxel",
        "froxel-high" => "froxel-high",
        "froxel-epic" => "froxel-epic",
        _ => "analytic"
    };

    private static bool UsesFroxel(string mode) => !mode.Equals("analytic", StringComparison.Ordinal);

    private static float FiniteFloat(double value) =>
        double.IsFinite(value) && value >= -float.MaxValue && value <= float.MaxValue ? (float)value : 0;

    private static float PositiveExtent(double value) => Math.Max(0.001f, Math.Abs(FiniteFloat(value)));

    private static int DivideRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;
}

public readonly record struct RekallAgeVulkanFogGrid(int Width, int Height, int Depth)
{
    public long CellCount => (long)Width * Height * Depth;
}

public readonly record struct RekallAgeVulkanFogDispatch(int GroupCountX, int GroupCountY, int GroupCountZ);

public sealed record RekallAgeVulkanFogVolume(
    string EntityId,
    string EntityName,
    string Shape,
    float Density,
    Vector3 Albedo,
    Vector3 Emission,
    float Anisotropy,
    float HeightFalloff,
    float BlendDistance,
    int Priority,
    Vector3 Position,
    Vector3 HalfExtents,
    Vector3 Scattering);

public sealed record RekallAgeVulkanFogHistory(
    int FrameIndex,
    string? CameraEntityId,
    Vector3 CameraPosition,
    Vector3 CameraRotationDegrees,
    string Mode,
    RekallAgeVulkanFogGrid Grid);

public sealed record RekallAgeVulkanFogPlan(
    string Mode,
    bool Enabled,
    RekallAgeVulkanFogGrid Grid,
    IReadOnlyList<RekallAgeVulkanFogVolume> Volumes,
    IReadOnlyList<string> DroppedEntityIds,
    bool HistoryReset,
    bool TemporalReprojection,
    RekallAgeVulkanFogDispatch Dispatch,
    RekallAgeVulkanFogHistory NextHistory,
    IReadOnlyList<RekallAgeVulkanFogDiagnostic> Diagnostics)
{
    public bool UsesFroxelGrid => !Mode.Equals("analytic", StringComparison.Ordinal);

    public bool DirectLightAvailable { get; init; }

    public bool ShadowAvailable { get; init; }
}

public sealed record RekallAgeVulkanFogDiagnostic(
    string Code,
    string Message,
    IReadOnlyList<string> EntityIds);

internal static class RekallAgeVector3Extensions
{
    public static float MaxComponent(this Vector3 value) => Math.Max(value.X, Math.Max(value.Y, value.Z));
}
