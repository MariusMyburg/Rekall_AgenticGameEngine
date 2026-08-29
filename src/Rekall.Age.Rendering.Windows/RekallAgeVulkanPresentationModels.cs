using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering.Windows;

public interface IRekallAgeVulkanPresentationSession : IAsyncDisposable
{
    ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
        RekallAgeWin32RenderSurface surface,
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        CancellationToken cancellationToken);

    ValueTask InvalidateAssetsAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    ValueTask InvalidateShadersAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed record RekallAgeVulkanPresentationFrame
{
    public RekallAgeVulkanPresentationFrame(
        string sceneName,
        int frameIndex,
        double elapsedSeconds,
        int width,
        int height,
        int renderableCount,
        int observationCount,
        string backendId = "vulkan",
        bool hardwareAccelerated = true,
        string accelerationStatus = "hardware",
        string? selectedDeviceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (renderableCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(renderableCount));
        }

        if (observationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(observationCount));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(backendId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accelerationStatus);

        SceneName = sceneName;
        FrameIndex = frameIndex;
        ElapsedSeconds = elapsedSeconds;
        Width = width;
        Height = height;
        RenderableCount = renderableCount;
        ObservationCount = observationCount;
        BackendId = backendId.Trim().ToLowerInvariant();
        HardwareAccelerated = hardwareAccelerated;
        AccelerationStatus = accelerationStatus.Trim().ToLowerInvariant();
        SelectedDeviceName = string.IsNullOrWhiteSpace(selectedDeviceName) ? null : selectedDeviceName.Trim();
    }

    public string SceneName { get; }

    public int FrameIndex { get; }

    public double ElapsedSeconds { get; }

    public int Width { get; }

    public int Height { get; }

    public int RenderableCount { get; }

    public int ObservationCount { get; }

    public string BackendId { get; }

    public bool HardwareAccelerated { get; }

    public string AccelerationStatus { get; }

    public string? SelectedDeviceName { get; }

    public RekallAgeRgbaImage? PresentedImage { get; init; }

    public RekallAgeVulkanPresentationInteropMetadata? VulkanInterop { get; init; }

    public static RekallAgeVulkanPresentationFrame FromViewportFrame(
        RekallAgeRuntimeViewportFrame frame,
        string backendId = "vulkan",
        bool hardwareAccelerated = true,
        string accelerationStatus = "hardware",
        string? selectedDeviceName = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Viewport frame width must be positive.");
        }

        if (frame.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Viewport frame height must be positive.");
        }

        return new RekallAgeVulkanPresentationFrame(
            frame.SceneName,
            frame.FrameIndex,
            frame.ElapsedSeconds,
            frame.Width,
            frame.Height,
            frame.Renderables.Count,
            frame.Observations.Count,
            backendId,
            hardwareAccelerated,
            accelerationStatus,
            selectedDeviceName);
    }
}

public sealed record RekallAgeVulkanPresentationInteropMetadata(string GraphicsApi = "vulkan")
{
    public nuint VkInstance { get; init; }

    public nuint VkPhysicalDevice { get; init; }

    public nuint VkDevice { get; init; }

    public nuint GraphicsQueue { get; init; }

    public uint GraphicsQueueFamilyIndex { get; init; }
}
