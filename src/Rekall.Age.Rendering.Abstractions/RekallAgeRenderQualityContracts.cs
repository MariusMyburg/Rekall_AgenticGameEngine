namespace Rekall.Age.Rendering.Abstractions;

/// <summary>
/// Backend-neutral authored quality intent. Resolution is performed against explicit device facts.
/// </summary>
public sealed record RekallAgeRenderQualityIntent(
    string Preset = "High",
    double? ResolutionScale = null,
    int? ShadowCascadeCount = null,
    int? ShadowResolution = null,
    string? FogMode = null,
    bool? Bloom = null,
    bool? Ssao = null,
    int? MaximumActiveParticles = null,
    bool AutomaticScaling = false,
    double TargetFramesPerSecond = 60)
{
    public bool EnableGpuTimestamps { get; init; }
}

/// <summary>
/// Bounded, caller-scoped render-work overrides. Applying these values never mutates authored scene or gameplay state.
/// </summary>
public sealed record RekallAgeRenderQualityOverrides(
    double? ResolutionScale = null,
    int? ShadowCascadeCount = null,
    int? ShadowResolution = null,
    string? FogMode = null,
    bool? Bloom = null,
    bool? Ssao = null,
    int? MaximumActiveParticles = null);

/// <summary>
/// Backend-neutral duration for one executed render pass, sourced from a GPU timestamp query.
/// </summary>
public sealed record RekallAgeGpuPassTiming(
    string Name,
    double Nanoseconds,
    double Milliseconds);

/// <summary>
/// Inspectable GPU timing result. Unavailable timing is represented by a stable code and nullable totals, never CPU time.
/// </summary>
public sealed record RekallAgeGpuFrameTimingReport(
    bool Available,
    string? Code,
    int FrameIndex,
    IReadOnlyList<RekallAgeGpuPassTiming> Passes,
    double? TotalNanoseconds,
    double? TotalMilliseconds,
    string Provenance)
{
    public static RekallAgeGpuFrameTimingReport Unavailable(int frameIndex) => new(
        false,
        "REKALL_GPU_TIMESTAMPS_UNAVAILABLE",
        frameIndex,
        Array.Empty<RekallAgeGpuPassTiming>(),
        null,
        null,
        "unavailable");
}

public sealed record RekallAgeResolvedRenderFeaturePlan(
    string RequestedPreset,
    string ResolvedPreset,
    int OutputWidth,
    int OutputHeight,
    int RenderWidth,
    int RenderHeight,
    double ResolutionScale,
    RekallAgeResolvedShadowQuality Shadows,
    RekallAgeResolvedFogQuality Fog,
    RekallAgeResolvedPostQuality Post,
    RekallAgeResolvedParticleQuality Particles,
    long EstimatedTransientBytes,
    long EstimatedPersistentBytes,
    IReadOnlyList<RekallAgeRenderFeatureDegradation> Degradations)
{
    public RekallAgeResolvedLightingQuality Lighting { get; init; } = new(4);
}

public sealed record RekallAgeResolvedLightingQuality(int MaximumPointLights);

public sealed record RekallAgeResolvedShadowQuality(
    int CascadeCount,
    int Resolution,
    int FilterTapCount);

public sealed record RekallAgeResolvedFogQuality(
    string Mode,
    int FroxelWidth = 0,
    int FroxelHeight = 0,
    int FroxelDepth = 0);

public sealed record RekallAgeResolvedPostQuality(
    bool Bloom,
    bool Ssao,
    bool GpuTimestamps);

public sealed record RekallAgeResolvedParticleQuality(int MaximumActiveParticles);

/// <summary>
/// A stable, inspectable explanation for any resolver fallback or device-enforced reduction.
/// </summary>
public sealed record RekallAgeRenderFeatureDegradation(
    string Code,
    string Feature,
    string RequestedValue,
    string ResolvedValue,
    string Message);

/// <summary>
/// A backend-neutral render resource planned from resolved quality and viewport facts.
/// </summary>
public sealed record RekallAgeHighFidelityRenderResource(
    string Name,
    string Format,
    int Width,
    int Height,
    int Layers,
    string Lifetime,
    IReadOnlyList<string> Usage);

/// <summary>
/// A backend-neutral render pass whose declared resource accesses can be inspected before execution.
/// </summary>
public sealed record RekallAgeHighFidelityRenderPass(
    string Name,
    string Kind,
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Writes,
    int Order,
    bool Enabled);

public sealed record RekallAgeHighFidelityRenderDependency(
    string ProducerPass,
    string ConsumerPass,
    string Resource);

public sealed record RekallAgeHighFidelityRenderGraphDiagnostic(
    string Code,
    string Target,
    string Message);
