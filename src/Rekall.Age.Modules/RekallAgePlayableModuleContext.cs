namespace Rekall.Age.Modules;

public sealed record RekallAgePlayableModuleContext(
    string SceneName,
    IReadOnlyList<string> EntityNames);
