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
    RekallAgeWorkbenchActionPaletteModel Actions);

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
