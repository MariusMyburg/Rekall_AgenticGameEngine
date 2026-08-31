namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeWorkbenchModel(
    RekallAgeProjectTreeModel Project,
    RekallAgeSceneGraphModel Scene,
    RekallAgeInspectorModel Inspector,
    RekallAgeAssetBrowserModel Assets,
    RekallAgeValidationPanelModel Diagnostics,
    RekallAgeTransactionPanelModel Transactions,
    RekallAgeImportQueueModel ImportQueue,
    RekallAgeRuntimePanelModel Runtime,
    RekallAgeWorkbenchSceneSummaryModel SceneSummary,
    RekallAgeWorkbenchActionPaletteModel Actions)
{
    public RekallAgeContentBrowserModel Content { get; init; } = RekallAgeContentBrowserModel.Empty;

    public RekallAgeWorkbenchRenderQualityModel Rendering { get; init; } =
        RekallAgeWorkbenchRenderQualityModel.Empty("High");
}

public sealed record RekallAgeWorkbenchRenderQualityModel(
    RekallAgeWorkbenchRenderQualityAuthoringModel? Authoring,
    RekallAgeWorkbenchRenderQualityRuntimeModel Runtime,
    IReadOnlyList<RekallAgeWorkbenchRenderQualityComparisonModel> Comparisons,
    IReadOnlyList<RekallAgeWorkbenchRenderDebugViewModel> DebugViews)
{
    public static RekallAgeWorkbenchRenderQualityModel Empty(string requestedPreset) => new(
        null,
        RekallAgeWorkbenchRenderQualityRuntimeModel.Unavailable(requestedPreset),
        [],
        []);
}

public sealed record RekallAgeWorkbenchRenderQualityAuthoringModel(
    string EntityId,
    string EntityName,
    string Preset,
    double? ResolutionScale,
    int? ShadowCascadeCount,
    int? ShadowResolution,
    string? FogMode,
    bool? Bloom,
    bool? Ssao,
    int? MaximumActiveParticles,
    bool AutomaticScaling,
    double TargetFramesPerSecond,
    bool EnableGpuTimestamps);

public sealed record RekallAgeWorkbenchRenderQualityRuntimeModel(
    string RequestedPreset,
    string? ResolvedPreset,
    int? OutputWidth,
    int? OutputHeight,
    int? RenderWidth,
    int? RenderHeight,
    double? ResolutionScale,
    bool GpuTimingAvailable,
    string? GpuTimingCode,
    string GpuTimingProvenance,
    double? TotalGpuMilliseconds,
    string TotalGpuMillisecondsText,
    int DrawCount,
    int DispatchCount,
    IReadOnlyList<RekallAgeWorkbenchRenderPassTimingModel> PassTimings,
    IReadOnlyList<RekallAgeWorkbenchRenderResourceModel> Resources,
    IReadOnlyList<RekallAgeWorkbenchRenderDegradationModel> Degradations,
    IReadOnlyList<string> SuggestedActions)
{
    public static RekallAgeWorkbenchRenderQualityRuntimeModel Unavailable(string requestedPreset) => new(
        requestedPreset,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        "REKALL_GPU_TIMESTAMPS_UNAVAILABLE",
        "unavailable",
        null,
        "Unavailable",
        0,
        0,
        [],
        [],
        [],
        []);
}

public sealed record RekallAgeWorkbenchRenderPassTimingModel(
    string Name,
    double Nanoseconds,
    double? Milliseconds,
    string MillisecondsText);

public sealed record RekallAgeWorkbenchRenderResourceModel(
    string Name,
    long Bytes);

public sealed record RekallAgeWorkbenchRenderDegradationModel(
    string Code,
    string Feature,
    string RequestedValue,
    string ResolvedValue,
    string Message);

public sealed record RekallAgeWorkbenchRenderQualityComparisonModel(
    string RequestedPreset,
    string ResolvedPreset,
    string ScreenshotPath,
    bool NonBlank,
    int OutputWidth,
    int OutputHeight,
    int RenderWidth,
    int RenderHeight,
    long ResourceBytes,
    int DrawCount,
    int DispatchCount,
    double? TotalGpuMilliseconds,
    string TotalGpuMillisecondsText,
    IReadOnlyList<RekallAgeWorkbenchRenderDegradationModel> Degradations);

public sealed record RekallAgeWorkbenchRenderDebugViewModel(
    string Label,
    string Kind,
    string OutputPath,
    bool NonBlank);

public sealed record RekallAgeWorkbenchSceneSummaryModel(
    int EntityCount,
    int RootEntityCount,
    int ComponentCount,
    IReadOnlyList<string> Tags,
    IReadOnlyList<RekallAgeWorkbenchComponentTypeSummary> ComponentTypes);

public sealed record RekallAgeWorkbenchComponentTypeSummary(
    string Type,
    int Count);

public sealed record RekallAgeWorkbenchActionPaletteModel(
    IReadOnlyList<RekallAgeWorkbenchActionItem> Actions);

public sealed record RekallAgeWorkbenchActionItem(
    string Id,
    string Label,
    string Category,
    string Tool,
    string Summary,
    bool Recommended);

public sealed record RekallAgeRuntimePanelModel(
    string SceneName,
    int FrameIndex,
    string? ActiveCameraName,
    string ViewportCaptureTool,
    int EntityCount,
    int RenderableCount,
    int PhysicsBodyCount,
    int AudioEmitterCount,
    int AnimationPlayerCount,
    int UiElementCount,
    IReadOnlyList<RekallAgeRuntimePanelObservation> Observations);

public sealed record RekallAgeRuntimePanelObservation(
    string Code,
    string Severity,
    string Subsystem,
    string Target,
    string Message);
