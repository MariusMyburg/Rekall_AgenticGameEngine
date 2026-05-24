namespace Rekall.Age.Modules;

public sealed record RekallAgePlayableModuleFrame(
    string Text,
    IReadOnlyList<RekallAgePlayableDrawCommand>? DrawCommands = null);

public sealed record RekallAgePlayableDrawCommand(
    string Kind,
    string Id,
    double X,
    double Y,
    double Width,
    double Height,
    string Fill,
    string Text = "");
