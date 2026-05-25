namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeWorkbenchModel(
    RekallAgeProjectTreeModel Project,
    RekallAgeSceneGraphModel Scene,
    RekallAgeInspectorModel Inspector,
    RekallAgeAssetBrowserModel Assets,
    RekallAgeValidationPanelModel Diagnostics,
    RekallAgeTransactionPanelModel Transactions,
    RekallAgeImportQueueModel ImportQueue);
