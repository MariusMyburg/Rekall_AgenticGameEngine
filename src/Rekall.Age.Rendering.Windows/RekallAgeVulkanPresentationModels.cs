using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Rendering.Windows;

public interface IRekallAgeVulkanPresentationSession : IAsyncDisposable
{
    RekallAgeVulkanNativeDeviceInfo DeviceInfo =>
        throw new NotSupportedException("This presentation session does not expose native Vulkan device metadata.");

    ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
        RekallAgeVulkanSceneSubmission submission,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This presentation session does not support surface-bound scene submissions.");

    ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
        RekallAgeWin32RenderSurfaceDescriptor surface,
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This presentation session is bound to its construction surface.");

    ValueTask InvalidateAssetsAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    ValueTask InvalidateShadersAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    ValueTask<RekallAgeVulkanPresentedPixels> CapturePresentedRgbaAsync(
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This presentation session does not support GPU readback.");

    void Resize(int pixelWidth, int pixelHeight) =>
        throw new NotSupportedException("This presentation session does not support resizing.");
}

public sealed record RekallAgeVulkanPresentationOptions(
    string ProjectRoot,
    bool SyncToVerticalBlank = true,
    int SceneSupersampleFactor = 2,
    bool DebugHudEnabled = false,
    Action<string>? Log = null);

public sealed record RekallAgeVulkanSceneSubmission
{
    public RekallAgeVulkanSceneSubmission(
        RekallAgeRuntimeViewportFrame Frame,
        RekallAgeRuntimeViewportAssetSet Assets,
        IReadOnlyList<RekallAgeRuntimeGpuWorkload> RuntimeGpuWorkloads,
        int RuntimeEntityCount,
        int SceneRevision,
        int AssetRevision,
        string? DebugBackendText = null)
    {
        ArgumentNullException.ThrowIfNull(Frame);
        ArgumentNullException.ThrowIfNull(Assets);
        ArgumentNullException.ThrowIfNull(RuntimeGpuWorkloads);
        if (RuntimeEntityCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RuntimeEntityCount));
        }

        if (SceneRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SceneRevision));
        }

        if (AssetRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AssetRevision));
        }

        this.Frame = Frame;
        this.Assets = Assets;
        this.RuntimeGpuWorkloads = RuntimeGpuWorkloads.Count == 0
            ? Array.Empty<RekallAgeRuntimeGpuWorkload>()
            : RuntimeGpuWorkloads.ToArray();
        this.RuntimeEntityCount = RuntimeEntityCount;
        this.SceneRevision = SceneRevision;
        this.AssetRevision = AssetRevision;
        this.DebugBackendText = string.IsNullOrWhiteSpace(DebugBackendText) ? null : DebugBackendText.Trim();
    }

    public RekallAgeRuntimeViewportFrame Frame { get; }

    public RekallAgeRuntimeViewportAssetSet Assets { get; }

    public IReadOnlyList<RekallAgeRuntimeGpuWorkload> RuntimeGpuWorkloads { get; }

    public int RuntimeEntityCount { get; }

    public int SceneRevision { get; }

    public int AssetRevision { get; }

    public string? DebugBackendText { get; }
}

public sealed record RekallAgeVulkanPresentedPixels
{
    public RekallAgeVulkanPresentedPixels(int width, int height, ReadOnlyMemory<byte> rgba)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (rgba.Length != checked(width * height * 4))
        {
            throw new ArgumentException("RGBA pixels must contain exactly width * height * 4 bytes.", nameof(rgba));
        }

        Width = width;
        Height = height;
        Rgba = rgba.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Rgba { get; }
}

public sealed record RekallAgeVulkanPixelSubmission
{
    public RekallAgeVulkanPixelSubmission(
        int width,
        int height,
        ReadOnlyMemory<byte> rgba,
        RekallAgeVulkanSceneSubmission scene)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (rgba.Length != checked(width * height * 4))
        {
            throw new ArgumentException("RGBA pixels must contain exactly width * height * 4 bytes.", nameof(rgba));
        }

        ArgumentNullException.ThrowIfNull(scene);
        Width = width;
        Height = height;
        Rgba = rgba.ToArray();
        Scene = scene;
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Rgba { get; }

    public RekallAgeVulkanSceneSubmission Scene { get; }
}

public sealed record RekallAgeVulkanNativeDeviceInfo(
    string DeviceName,
    string Backend,
    ulong Instance,
    ulong PhysicalDevice,
    ulong Device,
    ulong GraphicsQueue,
    uint GraphicsQueueFamilyIndex,
    string? DriverName,
    string? DriverInfo);

public sealed record RekallAgeVulkanPresentationFrame
{
    private const string VulkanBackendId = "vulkan";
    private const string HardwareAccelerationStatus = "hardware";
    private const string UnavailableAccelerationStatus = "unavailable";

    private RekallAgeVulkanPresentationFrame(
        string sceneName,
        int frameIndex,
        double elapsedSeconds,
        int width,
        int height,
        int renderableCount,
        int observationCount,
        bool presentedFrame,
        bool hardwareAccelerated,
        string accelerationStatus,
        string? selectedDeviceName,
        string? failureReason,
        IReadOnlyList<string> errors)
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

        ArgumentException.ThrowIfNullOrWhiteSpace(accelerationStatus);
        ArgumentNullException.ThrowIfNull(errors);

        if (presentedFrame)
        {
            if (!hardwareAccelerated)
            {
                throw new ArgumentException("Successful Vulkan presentation must be hardware accelerated.", nameof(hardwareAccelerated));
            }

            if (!accelerationStatus.Equals(HardwareAccelerationStatus, StringComparison.Ordinal))
            {
                throw new ArgumentException("Successful Vulkan presentation must report hardware acceleration.", nameof(accelerationStatus));
            }

            if (failureReason is not null)
            {
                throw new ArgumentException("Successful Vulkan presentation cannot include a failure reason.", nameof(failureReason));
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException("Successful Vulkan presentation cannot include errors.", nameof(errors));
            }
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
            if (!accelerationStatus.Equals(UnavailableAccelerationStatus, StringComparison.Ordinal))
            {
                throw new ArgumentException("Unavailable Vulkan presentation must report unavailable acceleration.", nameof(accelerationStatus));
            }
        }

        SceneName = sceneName;
        FrameIndex = frameIndex;
        ElapsedSeconds = elapsedSeconds;
        Width = width;
        Height = height;
        RenderableCount = renderableCount;
        ObservationCount = observationCount;
        PresentedFrame = presentedFrame;
        BackendId = VulkanBackendId;
        HardwareAccelerated = hardwareAccelerated;
        AccelerationStatus = accelerationStatus;
        SelectedDeviceName = string.IsNullOrWhiteSpace(selectedDeviceName) ? null : selectedDeviceName.Trim();
        FailureReason = failureReason;
        Errors = errors.Count == 0 ? Array.Empty<string>() : errors.ToArray();
    }

    public string SceneName { get; }

    public int FrameIndex { get; }

    public double ElapsedSeconds { get; }

    public int Width { get; }

    public int Height { get; }

    public int RenderableCount { get; }

    public int ObservationCount { get; }

    public bool PresentedFrame { get; }

    public string BackendId { get; }

    public bool HardwareAccelerated { get; }

    public string AccelerationStatus { get; }

    public string? SelectedDeviceName { get; }

    public string? FailureReason { get; }

    public IReadOnlyList<string> Errors { get; }

    public RekallAgeRgbaImage? PresentedImage { get; init; }

    public RekallAgeVulkanPresentationInteropMetadata? VulkanInterop { get; init; }

    public static RekallAgeVulkanPresentationFrame Presented(
        RekallAgeRuntimeViewportFrame frame,
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
            presentedFrame: true,
            hardwareAccelerated: true,
            accelerationStatus: HardwareAccelerationStatus,
            selectedDeviceName,
            failureReason: null,
            errors: Array.Empty<string>());
    }

    public static RekallAgeVulkanPresentationFrame Unavailable(
        RekallAgeRuntimeViewportFrame frame,
        string failureReason,
        IReadOnlyList<string>? errors = null,
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
            presentedFrame: false,
            hardwareAccelerated: false,
            accelerationStatus: UnavailableAccelerationStatus,
            selectedDeviceName,
            failureReason,
            errors ?? Array.Empty<string>());
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
