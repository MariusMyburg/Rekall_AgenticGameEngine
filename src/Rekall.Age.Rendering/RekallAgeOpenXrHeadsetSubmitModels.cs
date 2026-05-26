namespace Rekall.Age.Rendering;

public sealed record RekallAgeOpenXrHeadsetClearSubmitRequest(
    int FrameCount = 120,
    float Red = 0.02f,
    float Green = 0.12f,
    float Blue = 0.32f,
    float Alpha = 1.0f);

public sealed record RekallAgeOpenXrHeadsetClearSubmitResult(
    bool Submitted,
    bool InstanceCreated,
    bool VulkanInstanceCreated,
    bool VulkanDeviceCreated,
    bool SessionCreated,
    bool ReferenceSpaceCreated,
    bool SwapchainCreated,
    int SubmittedFrames,
    int RecommendedWidth,
    int RecommendedHeight,
    IReadOnlyList<string> Errors);

public sealed record RekallAgeOpenXrHeadsetClearSubmitPlan(
    int FrameCount,
    float Red,
    float Green,
    float Blue,
    float Alpha);

public sealed record RekallAgeOpenXrHeadsetSoftwareSceneSubmitRequest(
    string ProjectRoot,
    string SceneName,
    int FrameCount = 120,
    int SimulationStartFrame = 0,
    int RenderWidth = 1920,
    int RenderHeight = 1080,
    bool DebugOverlay = false);

public sealed record RekallAgeOpenXrHeadsetSoftwareSceneSubmitPlan(
    string ProjectRoot,
    string SceneName,
    int FrameCount,
    int SimulationStartFrame,
    int RenderWidth,
    int RenderHeight,
    bool DebugOverlay);

public sealed record RekallAgeOpenXrHeadsetSoftwareSceneSubmitResult(
    bool Submitted,
    bool InstanceCreated,
    bool VulkanInstanceCreated,
    bool VulkanDeviceCreated,
    bool SessionCreated,
    bool ReferenceSpaceCreated,
    bool SwapchainCreated,
    int SubmittedFrames,
    int RecommendedWidth,
    int RecommendedHeight,
    int RenderWidth,
    int RenderHeight,
    int RenderableCount,
    string? ActiveCamera,
    int NativeVulkanFrames,
    int SoftwareFallbackFrames,
    string RenderingBackend,
    IReadOnlyList<string> NativeVulkanFallbackReasons,
    IReadOnlyList<string> Errors);

public static class RekallAgeOpenXrHeadsetSubmitPlanner
{
    public const int ContinuousSceneFrameCount = 0;
    public const int RecommendedRuntimeExtent = 0;
    public const int MaxSceneEyeExtent = 8192;
    public const int MaxSceneFrameCount = 72_000;

    public static RekallAgeOpenXrHeadsetClearSubmitPlan Plan(RekallAgeOpenXrHeadsetClearSubmitRequest request)
    {
        return new RekallAgeOpenXrHeadsetClearSubmitPlan(
            Math.Clamp(request.FrameCount, 1, 600),
            Math.Clamp(request.Red, 0, 1),
            Math.Clamp(request.Green, 0, 1),
            Math.Clamp(request.Blue, 0, 1),
            Math.Clamp(request.Alpha, 0, 1));
    }

    public static RekallAgeOpenXrHeadsetSoftwareSceneSubmitPlan Plan(RekallAgeOpenXrHeadsetSoftwareSceneSubmitRequest request)
    {
        return new RekallAgeOpenXrHeadsetSoftwareSceneSubmitPlan(
            request.ProjectRoot.Trim(),
            request.SceneName.Trim(),
            request.FrameCount <= 0
                ? ContinuousSceneFrameCount
                : Math.Clamp(request.FrameCount, 1, MaxSceneFrameCount),
            Math.Max(0, request.SimulationStartFrame),
            NormalizeSceneEyeExtent(request.RenderWidth),
            NormalizeSceneEyeExtent(request.RenderHeight),
            request.DebugOverlay);
    }

    private static int NormalizeSceneEyeExtent(int extent)
    {
        return extent <= 0
            ? RecommendedRuntimeExtent
            : Math.Clamp(extent, 64, MaxSceneEyeExtent);
    }
}
