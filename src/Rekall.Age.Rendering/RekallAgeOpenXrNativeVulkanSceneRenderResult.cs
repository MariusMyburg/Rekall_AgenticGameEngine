namespace Rekall.Age.Rendering;

public sealed record RekallAgeOpenXrNativeVulkanSceneRenderResult(
    bool Rendered,
    IReadOnlyList<string> Errors);
