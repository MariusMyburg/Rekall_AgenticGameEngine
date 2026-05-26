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

public static class RekallAgeOpenXrHeadsetSubmitPlanner
{
    public static RekallAgeOpenXrHeadsetClearSubmitPlan Plan(RekallAgeOpenXrHeadsetClearSubmitRequest request)
    {
        return new RekallAgeOpenXrHeadsetClearSubmitPlan(
            Math.Clamp(request.FrameCount, 1, 600),
            Math.Clamp(request.Red, 0, 1),
            Math.Clamp(request.Green, 0, 1),
            Math.Clamp(request.Blue, 0, 1),
            Math.Clamp(request.Alpha, 0, 1));
    }
}
