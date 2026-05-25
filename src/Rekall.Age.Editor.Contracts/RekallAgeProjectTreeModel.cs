namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeProjectTreeModel(
    string Name,
    string RootPath,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<RekallAgeProjectSceneItem> Scenes);

public sealed record RekallAgeProjectSceneItem(
    string Name,
    string Path,
    bool Active);
