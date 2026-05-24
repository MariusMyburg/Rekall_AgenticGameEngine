namespace Rekall.Age.Rendering;

public sealed record RekallAgeRenderPlanExecutionResult(
    string OutputPath,
    bool NonBlank,
    int Width,
    int Height);
