namespace Rekall.Age.Studio;

internal static class RekallAgeStudioPreviewCadence
{
    internal const int TargetFramesPerSecond = 60;
    internal const int FramesPerPresentation = 1;
    internal const int MaximumSimulationFramesPerPresentation = 6;
    internal static readonly TimeSpan PresentationInterval =
        TimeSpan.FromSeconds(1d / TargetFramesPerSecond);
}
