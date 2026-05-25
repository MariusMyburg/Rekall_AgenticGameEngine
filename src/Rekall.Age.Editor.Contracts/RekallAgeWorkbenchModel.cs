namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeWorkbenchModel(
    RekallAgeProjectTreeModel Project,
    RekallAgeSceneGraphModel Scene,
    RekallAgeInspectorModel Inspector,
    RekallAgeAssetBrowserModel Assets,
    RekallAgeValidationPanelModel Diagnostics,
    RekallAgeTransactionPanelModel Transactions,
    RekallAgeImportQueueModel ImportQueue,
    RekallAgeRuntimePanelModel Runtime);

public sealed record RekallAgeRuntimePanelModel(
    string SceneName,
    int FrameIndex,
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
