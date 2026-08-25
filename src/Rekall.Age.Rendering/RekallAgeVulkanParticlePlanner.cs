using Rekall.Age.Core.Rendering;
using Rekall.Age.Rendering.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace Rekall.Age.Rendering;

/// <summary>Resolves generic authored emitters into stable bounded GPU ranges.</summary>
public sealed class RekallAgeVulkanParticlePlanner
{
    public const int MaximumEmitterCapacity = 262_144;
    public const int MaximumGlobalCapacity = 1_048_576;
    public const double MaximumLifetimeSeconds = 3_600;
    public const int MaximumCurveKeys = 4;

    public RekallAgeVulkanParticlePlan Plan(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeResolvedParticleQuality quality,
        double deltaSeconds,
        RekallAgeVulkanParticleHistory? history = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(quality);
        var diagnostics = new List<RekallAgeVulkanParticleDiagnostic>();
        var rejected = new List<string>();
        var candidates = new List<RekallAgeRuntimeViewportParticleEmitter>();
        var unsupportedModes = new List<string>();
        var cameraCulled = new List<string>();
        foreach (var source in frame.ParticleEmitters
                     .OrderByDescending(item => item.Priority)
                     .ThenBy(item => item.EntityId, StringComparer.Ordinal))
        {
            if (!source.Enabled)
            {
                continue;
            }

            if (!double.IsFinite(source.SpawnRate) || source.SpawnRate < 0
                || source.Bursts.Any(item => !double.IsFinite(item.TimeSeconds) || item.TimeSeconds < 0 || item.Count < 0))
            {
                Reject("REKALL_PARTICLE_EMISSION_INVALID", source.EntityId, "Particle spawn rate and bursts must be finite and non-negative.");
                continue;
            }

            if (source.SpawnRate == 0 && source.Bursts.All(item => item.Count == 0))
            {
                continue;
            }

            if (!source.DrawMode.Equals("quad", StringComparison.OrdinalIgnoreCase))
            {
                unsupportedModes.Add(source.EntityId);
                continue;
            }

            if (!VisibleToCamera(source, frame.ActiveCamera))
            {
                cameraCulled.Add(source.EntityId);
                continue;
            }

            if (!PositiveFinite(source.LifetimeSeconds) || source.LifetimeSeconds > MaximumLifetimeSeconds)
            {
                Reject("REKALL_PARTICLE_LIFETIME_INVALID", source.EntityId, "Particle lifetime must be finite, positive, and bounded.");
                continue;
            }

            if (source.Capacity <= 0 || source.Capacity > MaximumEmitterCapacity)
            {
                Reject("REKALL_PARTICLE_EMITTER_CAPACITY_UNSAFE", source.EntityId, $"Particle emitter capacity must be between 1 and {MaximumEmitterCapacity}.");
                continue;
            }

            if (!source.SimulationSpace.Equals("world", StringComparison.OrdinalIgnoreCase)
                && !source.SimulationSpace.Equals("local", StringComparison.OrdinalIgnoreCase))
            {
                Reject("REKALL_PARTICLE_SIMULATION_SPACE_UNSUPPORTED", source.EntityId, "Supported particle simulation spaces are world and local.");
                continue;
            }

            if (!MotionValid(source))
            {
                Reject("REKALL_PARTICLE_MOTION_INVALID", source.EntityId, "Particle direction, cone, speed, gravity, and drag must be finite and within supported ranges.");
                continue;
            }

            if (!SizeCurveValid(source))
            {
                Reject("REKALL_PARTICLE_SIZE_CURVE_INVALID", source.EntityId, $"Particle size curves require 1-{MaximumCurveKeys} ordered finite keys in normalized time with non-negative values.");
                continue;
            }

            if (!ColorCurveValid(source))
            {
                Reject("REKALL_PARTICLE_COLOR_INVALID", source.EntityId, $"Particle color curves require 1-{MaximumCurveKeys} ordered normalized-time keys and #RRGGBB or #RRGGBBAA colors.");
                continue;
            }

            if (!double.IsFinite(source.EmissiveIntensity) || source.EmissiveIntensity < 0
                || !double.IsFinite(source.SoftParticleFade) || source.SoftParticleFade < 0)
            {
                Reject("REKALL_PARTICLE_APPEARANCE_INVALID", source.EntityId, "Particle emissive intensity and soft-particle fade must be finite and non-negative.");
                continue;
            }

            if (source.FlipbookColumns <= 0 || source.FlipbookColumns > 4096
                || source.FlipbookRows <= 0 || source.FlipbookRows > 4096
                || !double.IsFinite(source.FlipbookFramesPerSecond)
                || source.FlipbookFramesPerSecond < 0)
            {
                Reject("REKALL_PARTICLE_FLIPBOOK_INVALID", source.EntityId, "Particle flipbook dimensions and frame rate must be finite, positive, and bounded.");
                continue;
            }

            if (!source.BlendMode.Equals("alpha", StringComparison.OrdinalIgnoreCase)
                && !source.BlendMode.Equals("additive", StringComparison.OrdinalIgnoreCase))
            {
                Reject("REKALL_PARTICLE_BLEND_MODE_UNSUPPORTED", source.EntityId, "Supported particle blend modes are alpha and additive.");
                continue;
            }

            candidates.Add(source);
        }

        AddGroupedDiagnostic(
            "REKALL_PARTICLE_DRAW_MODE_UNSUPPORTED",
            "Particle draw mode is not available on this backend. Supported mode: quad.",
            unsupportedModes);
        AddGroupedDiagnostic(
            "REKALL_PARTICLE_CAMERA_CULLED",
            "Particle emitter was culled by the active camera layer mask or visibility distance.",
            cameraCulled);

        var globalCapacity = Math.Clamp(quality.MaximumActiveParticles, 0, MaximumGlobalCapacity);
        var ranges = new List<RekallAgeVulkanParticleEmitterRange>();
        var overflow = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        foreach (var source in candidates)
        {
            var remaining = globalCapacity - offset;
            if (remaining <= 0)
            {
                overflow.Add(source.EntityId);
                continue;
            }

            var resolvedCapacity = Math.Min(source.Capacity, remaining);
            if (resolvedCapacity < source.Capacity)
            {
                overflow.Add(source.EntityId);
            }

            var spawnCount = ResolveSpawnCount(source, frame.ElapsedSeconds, deltaSeconds, resolvedCapacity);
            var spawnStart = spawnCount == 0 ? 0 : (int)(StableHash(source.DeterministicSeed, frame.FrameIndex) % (uint)resolvedCapacity);
            ranges.Add(new RekallAgeVulkanParticleEmitterRange(
                source.EntityId,
                source.EntityName,
                offset,
                resolvedCapacity,
                spawnStart,
                spawnCount,
                source.DeterministicSeed,
                source.SimulationSpace,
                source.DrawMode,
                source.BlendMode,
                source.Lit,
                source.EmissiveIntensity,
                source.SoftParticleFade,
                source.TextureAssetId,
                source.Priority,
                source.VisibilityDistance,
                source.Layer,
                source));
            offset += resolvedCapacity;
        }

        var overflowIds = overflow.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (overflowIds.Length > 0)
        {
            diagnostics.Add(new RekallAgeVulkanParticleDiagnostic(
                "REKALL_PARTICLE_CAPACITY_OVERFLOW",
                $"Resolved particle capacity {globalCapacity} truncated or dropped {overflowIds.Length} emitter ranges.",
                overflowIds));
        }

        var plannedSpawnCount = ranges.Sum(item => item.SpawnCount);
        var topologyFingerprint = ComputeTopologyFingerprint(ranges);
        var stateReused = history is not null
            && history.Capacity == offset
            && history.TopologyFingerprint.Equals(topologyFingerprint, StringComparison.Ordinal)
            && history.LastFrameIndex is int previousFrame
            && frame.FrameIndex == previousFrame + 1;
        var destinationIsA = stateReused && !history!.LastDestinationIsA;
        return new RekallAgeVulkanParticlePlan(
            ranges,
            offset,
            plannedSpawnCount,
            offset == 0 ? new RekallAgeVulkanParticleDispatch(0, 0, 0) : new RekallAgeVulkanParticleDispatch(DivideRoundUp(offset, 256), 1, 1),
            overflowIds,
            rejected.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            diagnostics)
        {
            PreviousStateReused = stateReused,
            TopologyFingerprint = topologyFingerprint,
            SimulationSource = destinationIsA ? "particle-state-b" : "particle-state-a",
            SimulationDestination = destinationIsA ? "particle-state-a" : "particle-state-b"
        };

        void Reject(string code, string entityId, string message)
        {
            rejected.Add(entityId);
            diagnostics.Add(new RekallAgeVulkanParticleDiagnostic(code, message, [entityId]));
        }

        void AddGroupedDiagnostic(string code, string message, IReadOnlyList<string> entityIds)
        {
            if (entityIds.Count > 0)
            {
                diagnostics.Add(new RekallAgeVulkanParticleDiagnostic(
                    code,
                    message,
                    entityIds.OrderBy(item => item, StringComparer.Ordinal).ToArray()));
            }
        }
    }

    private static bool VisibleToCamera(
        RekallAgeRuntimeViewportParticleEmitter emitter,
        RekallAgeRuntimeViewportCamera? camera)
    {
        if (camera is null)
        {
            return true;
        }

        if (!RekallAgeRenderLayerMask.IncludesLayer(emitter.Layer, camera.CullingMask))
        {
            return false;
        }

        if (!double.IsFinite(emitter.VisibilityDistance) || emitter.VisibilityDistance < 0)
        {
            return false;
        }

        var dx = emitter.Transform.X - camera.X;
        var dy = emitter.Transform.Y - camera.Y;
        var dz = emitter.Transform.Z - camera.Z;
        var distanceSquared = dx * dx + dy * dy + dz * dz;
        return distanceSquared <= emitter.VisibilityDistance * emitter.VisibilityDistance;
    }

    private static int ResolveSpawnCount(
        RekallAgeRuntimeViewportParticleEmitter emitter,
        double elapsedSeconds,
        double deltaSeconds,
        int capacity)
    {
        var current = FiniteNonNegative(elapsedSeconds);
        var delta = FiniteNonNegative(deltaSeconds);
        var previous = Math.Max(0, current - delta);
        var continuous = PositiveFinite(emitter.SpawnRate)
            ? Math.Max(0, (long)Math.Floor(current * emitter.SpawnRate) - (long)Math.Floor(previous * emitter.SpawnRate))
            : 0;
        var bursts = emitter.Bursts
            .Where(item => double.IsFinite(item.TimeSeconds)
                && item.TimeSeconds > previous
                && item.TimeSeconds <= current
                && item.Count > 0)
            .Sum(item => (long)item.Count);
        return (int)Math.Min(capacity, Math.Min(int.MaxValue, continuous + bursts));
    }

    private static bool MotionValid(RekallAgeRuntimeViewportParticleEmitter emitter)
    {
        var directionLengthSquared = emitter.VelocityDirectionX * emitter.VelocityDirectionX
            + emitter.VelocityDirectionY * emitter.VelocityDirectionY
            + emitter.VelocityDirectionZ * emitter.VelocityDirectionZ;
        return double.IsFinite(directionLengthSquared) && directionLengthSquared > 0
            && double.IsFinite(emitter.VelocityConeDegrees) && emitter.VelocityConeDegrees >= 0 && emitter.VelocityConeDegrees < 90
            && double.IsFinite(emitter.MinimumSpeed) && emitter.MinimumSpeed >= 0
            && double.IsFinite(emitter.MaximumSpeed) && emitter.MaximumSpeed >= emitter.MinimumSpeed
            && double.IsFinite(emitter.GravityX) && double.IsFinite(emitter.GravityY) && double.IsFinite(emitter.GravityZ)
            && double.IsFinite(emitter.Drag) && emitter.Drag >= 0;
    }

    private static bool SizeCurveValid(RekallAgeRuntimeViewportParticleEmitter emitter) =>
        CurveTimesValid(emitter.SizeCurve.Select(item => item.NormalizedAge).ToArray(), emitter.SizeCurve.Count)
        && emitter.SizeCurve.All(item => double.IsFinite(item.Value) && item.Value >= 0);

    private static bool ColorCurveValid(RekallAgeRuntimeViewportParticleEmitter emitter) =>
        CurveTimesValid(emitter.ColorCurve.Select(item => item.NormalizedAge).ToArray(), emitter.ColorCurve.Count)
        && emitter.ColorCurve.All(item => IsParticleColor(item.Color));

    private static bool CurveTimesValid(IReadOnlyList<double> times, int count)
    {
        if (count is < 1 or > MaximumCurveKeys) return false;
        var ordered = times.Order().ToArray();
        return ordered.All(item => double.IsFinite(item) && item >= 0 && item <= 1)
            && ordered.Zip(ordered.Skip(1), (left, right) => right > left).All(item => item);
    }

    private static bool IsParticleColor(string value) =>
        value is { Length: 7 or 9 }
        && value[0] == '#'
        && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    private static string ComputeTopologyFingerprint(IReadOnlyList<RekallAgeVulkanParticleEmitterRange> ranges)
    {
        var topology = string.Join("\n", ranges.Select(range =>
            $"{range.EntityId.Length}:{range.EntityId}|{range.AllocationOffset}|{range.AllocationCapacity}|{range.SimulationSpace.ToLowerInvariant()}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(topology)));
    }

    private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static double FiniteNonNegative(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static uint StableHash(uint seed, int frame)
    {
        var value = seed ^ unchecked((uint)frame * 0x9E3779B9u);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        return value ^ (value >> 16);
    }

    private static int DivideRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;
}

public readonly record struct RekallAgeVulkanParticleDispatch(int GroupCountX, int GroupCountY, int GroupCountZ);

public sealed record RekallAgeVulkanParticleEmitterRange(
    string EntityId,
    string EntityName,
    int AllocationOffset,
    int AllocationCapacity,
    int SpawnStart,
    int SpawnCount,
    uint DeterministicSeed,
    string SimulationSpace,
    string DrawMode,
    string BlendMode,
    bool Lit,
    double EmissiveIntensity,
    double SoftParticleFade,
    string? TextureAssetId,
    int Priority,
    double VisibilityDistance,
    string Layer,
    RekallAgeRuntimeViewportParticleEmitter Source);

public sealed record RekallAgeVulkanParticlePlan(
    IReadOnlyList<RekallAgeVulkanParticleEmitterRange> Emitters,
    int AllocatedCapacity,
    int PlannedSpawnCount,
    RekallAgeVulkanParticleDispatch SimulationDispatch,
    IReadOnlyList<string> OverflowEntityIds,
    IReadOnlyList<string> RejectedEntityIds,
    IReadOnlyList<RekallAgeVulkanParticleDiagnostic> Diagnostics)
{
    public bool PreviousStateReused { get; init; }

    public string SimulationSource { get; init; } = "particle-state-a";

    public string SimulationDestination { get; init; } = "particle-state-b";

    public string TopologyFingerprint { get; init; } = string.Empty;
}

public sealed record RekallAgeVulkanParticleHistory(
    int Capacity,
    int? LastFrameIndex,
    bool LastDestinationIsA,
    string TopologyFingerprint);

public sealed record RekallAgeVulkanParticleDiagnostic(
    string Code,
    string Message,
    IReadOnlyList<string> EntityIds);
