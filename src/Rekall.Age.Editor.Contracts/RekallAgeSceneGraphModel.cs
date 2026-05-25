namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeSceneGraphModel(
    string SceneId,
    string Name,
    IReadOnlyList<RekallAgeSceneEntityNode> RootEntities);

public sealed record RekallAgeSceneEntityNode(
    string EntityId,
    string Name,
    IReadOnlyList<string> Tags,
    string? ParentId,
    bool Visible,
    bool Locked,
    IReadOnlyList<RekallAgeSceneEntityNode> Children);
