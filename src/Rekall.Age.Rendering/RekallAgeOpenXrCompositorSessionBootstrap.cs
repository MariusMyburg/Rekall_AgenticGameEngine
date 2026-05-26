using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Rekall.Age.Rendering;

public interface IRekallAgeOpenXrCompositorSessionBootstrap
{
    ValueTask<RekallAgeOpenXrCompositorSessionBootstrapResult> BootstrapAsync(
        RekallAgeOpenXrVulkanDeviceInteropInfo vulkan,
        CancellationToken cancellationToken);
}

public sealed record RekallAgeOpenXrCompositorSessionBootstrapResult(
    bool InstanceCreated,
    bool HmdSystemAvailable,
    ulong? SystemId,
    bool VulkanGraphicsRequirementsReady,
    bool SessionCreated,
    bool SwapchainFormatsEnumerated,
    IReadOnlyList<long> SwapchainFormats,
    long? PreferredColorFormat,
    long? PreferredDepthFormat,
    bool ColorSwapchainCreated,
    bool DepthSwapchainCreated,
    int ColorSwapchainImageCount,
    int DepthSwapchainImageCount,
    IReadOnlyList<ulong> ColorSwapchainImages,
    IReadOnlyList<ulong> DepthSwapchainImages,
    bool ReadyForFrameSubmission,
    bool FrameLoopReady,
    bool SessionReadyEventObserved,
    int? LastSessionState,
    bool SessionBegan,
    bool FrameWaited,
    bool FrameBegan,
    bool ColorSwapchainImageAcquired,
    bool ColorSwapchainImageWaited,
    bool ColorSwapchainImageReleased,
    bool FrameEnded,
    long? PredictedDisplayTime,
    IReadOnlyList<string> NextRenderSteps,
    IReadOnlyList<string> Errors);

public sealed class RekallAgeNativeOpenXrCompositorSessionBootstrap
    : IRekallAgeOpenXrCompositorSessionBootstrap
{
    private const int XrSuccess = 0;
    private const int XrEventUnavailable = 4;
    private const int XrTypeInstanceCreateInfo = 3;
    private const int XrTypeSystemGetInfo = 4;
    private const int XrTypeSessionCreateInfo = 8;
    private const int XrTypeSwapchainCreateInfo = 9;
    private const int XrTypeSessionBeginInfo = 10;
    private const int XrTypeFrameEndInfo = 12;
    private const int XrTypeFrameWaitInfo = 33;
    private const int XrTypeFrameState = 44;
    private const int XrTypeFrameBeginInfo = 46;
    private const int XrTypeSwapchainImageAcquireInfo = 55;
    private const int XrTypeSwapchainImageWaitInfo = 56;
    private const int XrTypeSwapchainImageReleaseInfo = 57;
    private const int XrTypeEventDataBuffer = 16;
    private const int XrTypeEventDataSessionStateChanged = 18;
    private const int XrTypeGraphicsBindingVulkanKhr = 1000025000;
    private const int XrTypeSwapchainImageVulkanKhr = 1000025001;
    private const int XrTypeGraphicsRequirementsVulkanKhr = 1000025002;
    private const int XrFormFactorHeadMountedDisplay = 1;
    private const int XrSessionStateReady = 2;
    private const int XrSessionStateStopping = 6;
    private const int XrSessionStateLossPending = 7;
    private const int XrSessionStateExiting = 8;
    private const int XrViewConfigurationTypePrimaryStereo = 2;
    private const int XrEnvironmentBlendModeOpaque = 1;
    private const int XrMaxApplicationNameSize = 128;
    private const int XrMaxEngineNameSize = 128;
    private const int XrEventDataBufferSize = 4016;
    private const long XrInfiniteDuration = long.MaxValue;
    private const ulong XrSwapchainUsageColorAttachmentBit = 0x00000002;
    private const ulong XrSwapchainUsageDepthStencilAttachmentBit = 0x00000004;
    private static readonly string[] RequiredExtensions = ["XR_KHR_vulkan_enable2"];

    private readonly IRekallAgeOpenXrRuntimeProbe _runtimeProbe;

    public RekallAgeNativeOpenXrCompositorSessionBootstrap()
        : this(new RekallAgeNativeOpenXrRuntimeProbe())
    {
    }

    public RekallAgeNativeOpenXrCompositorSessionBootstrap(IRekallAgeOpenXrRuntimeProbe runtimeProbe)
    {
        _runtimeProbe = runtimeProbe;
    }

    public async ValueTask<RekallAgeOpenXrCompositorSessionBootstrapResult> BootstrapAsync(
        RekallAgeOpenXrVulkanDeviceInteropInfo vulkan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = await _runtimeProbe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        var availableExtensions = runtime.InstanceExtensions
            .Select(extension => extension.Name)
            .ToHashSet(StringComparer.Ordinal);
        var missingExtensions = RequiredExtensions
            .Where(extension => !availableExtensions.Contains(extension))
            .ToArray();
        if (!runtime.LoaderAvailable || !runtime.RuntimeAvailable || missingExtensions.Length > 0)
        {
            return NotReady(
                false,
                false,
                null,
                false,
                false,
                false,
                [],
                null,
                runtime.Errors.Count > 0
                    ? runtime.Errors
                    : [$"Missing required OpenXR extension(s): {string.Join(", ", missingExtensions)}."]);
        }

        if (!TryLoadOpenXrLoader(out var loaderHandle))
        {
            return NotReady(false, false, null, false, false, false, [], null, ["OpenXR loader could not be loaded for compositor session bootstrap."]);
        }

        try
        {
            if (!TryGetRequiredExports(
                    loaderHandle,
                    out var createInstance,
                    out var getSystem,
                    out var getInstanceProcAddr,
                    out var createSession,
                    out var destroySession,
                    out var enumerateSwapchainFormats,
                    out var createSwapchain,
                    out var destroySwapchain,
                    out var enumerateSwapchainImages,
                    out var pollEvent,
                    out var beginSession,
                    out var endSession,
                    out var waitFrame,
                    out var beginFrame,
                    out var endFrame,
                    out var acquireSwapchainImage,
                    out var waitSwapchainImage,
                    out var releaseSwapchainImage,
                    out var destroyInstance,
                    out var exportError))
            {
                return NotReady(false, false, null, false, false, false, [], null, [exportError]);
            }

            var createInfo = CreateInstanceCreateInfo(RequiredExtensions, out var nativeExtensionNames);
            try
            {
                var createResult = createInstance(ref createInfo, out var instance);
                if (createResult != XrSuccess || instance == IntPtr.Zero)
                {
                    return NotReady(false, false, null, false, false, false, [], null, [$"xrCreateInstance failed with XrResult {createResult}."]);
                }

                try
                {
                    var systemInfo = new XrSystemGetInfo
                    {
                        Type = XrTypeSystemGetInfo,
                        Next = IntPtr.Zero,
                        FormFactor = XrFormFactorHeadMountedDisplay
                    };
                    var systemResult = getSystem(instance, ref systemInfo, out var systemId);
                    if (systemResult != XrSuccess)
                    {
                        return NotReady(true, false, null, false, false, false, [], null, [$"xrGetSystem for XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY failed with XrResult {systemResult}."]);
                    }

                    var errors = new List<string>();
                    var requirementsReady = QueryVulkanGraphicsRequirements(
                        instance,
                        systemId,
                        getInstanceProcAddr,
                        errors);
                    if (!requirementsReady)
                    {
                        return NotReady(true, true, systemId, false, false, false, [], null, errors);
                    }

                    var binding = new XrGraphicsBindingVulkanKhr
                    {
                        Type = XrTypeGraphicsBindingVulkanKhr,
                        Next = IntPtr.Zero,
                        Instance = unchecked((IntPtr)vulkan.Instance),
                        PhysicalDevice = unchecked((IntPtr)vulkan.PhysicalDevice),
                        Device = unchecked((IntPtr)vulkan.Device),
                        QueueFamilyIndex = vulkan.GraphicsQueueFamilyIndex,
                        QueueIndex = 0
                    };
                    var bindingPointer = Marshal.AllocHGlobal(Marshal.SizeOf<XrGraphicsBindingVulkanKhr>());
                    try
                    {
                        Marshal.StructureToPtr(binding, bindingPointer, false);
                        var sessionCreateInfo = new XrSessionCreateInfo
                        {
                            Type = XrTypeSessionCreateInfo,
                            Next = bindingPointer,
                            CreateFlags = 0,
                            SystemId = systemId
                        };
                        var sessionResult = createSession(instance, ref sessionCreateInfo, out var session);
                        if (sessionResult != XrSuccess || session == IntPtr.Zero)
                        {
                            return NotReady(
                                true,
                                true,
                                systemId,
                                true,
                                false,
                                false,
                                [],
                                null,
                                [$"xrCreateSession with XrGraphicsBindingVulkan2KHR failed with XrResult {sessionResult}."]);
                        }

                        try
                        {
                            var formats = EnumerateSwapchainFormats(session, enumerateSwapchainFormats, errors, out var formatsReady);
                            var preferred = SelectPreferredColorFormat(formats);
                            var preferredDepth = SelectPreferredDepthFormat(formats);
                            var colorImages = CreateSwapchainAndEnumerateImages(
                                session,
                                createSwapchain,
                                destroySwapchain,
                                enumerateSwapchainImages,
                                preferred,
                                XrSwapchainUsageColorAttachmentBit,
                                checked((uint)Math.Max(1, vulkan.RecommendedEyeWidth)),
                                checked((uint)Math.Max(1, vulkan.RecommendedEyeHeight)),
                                arraySize: 2,
                                "color",
                                errors,
                                out var colorSwapchainCreated);
                            var depthImages = CreateSwapchainAndEnumerateImages(
                                session,
                                createSwapchain,
                                destroySwapchain,
                                enumerateSwapchainImages,
                                preferredDepth,
                                XrSwapchainUsageDepthStencilAttachmentBit,
                                checked((uint)Math.Max(1, vulkan.RecommendedEyeWidth)),
                                checked((uint)Math.Max(1, vulkan.RecommendedEyeHeight)),
                                arraySize: 2,
                                "depth",
                                errors,
                                out var depthSwapchainCreated);
                            var readyForFrameSubmission = formatsReady
                                && colorSwapchainCreated
                                && colorImages.Count > 0
                                && depthSwapchainCreated
                                && depthImages.Count > 0;
                            var frameLoopProbe = readyForFrameSubmission
                                ? ProbeOneFrame(
                                    session,
                                    instance,
                                    createSwapchain,
                                    destroySwapchain,
                                    pollEvent,
                                    beginSession,
                                    endSession,
                                    waitFrame,
                                    beginFrame,
                                    endFrame,
                                    acquireSwapchainImage,
                                    waitSwapchainImage,
                                    releaseSwapchainImage,
                                    preferred,
                                    XrSwapchainUsageColorAttachmentBit,
                                    checked((uint)Math.Max(1, vulkan.RecommendedEyeWidth)),
                                    checked((uint)Math.Max(1, vulkan.RecommendedEyeHeight)),
                                    arraySize: 2,
                                    errors)
                                : default;
                            return new RekallAgeOpenXrCompositorSessionBootstrapResult(
                                true,
                                true,
                                systemId,
                                true,
                                true,
                                formatsReady,
                                formats,
                                preferred,
                                preferredDepth,
                                colorSwapchainCreated,
                                depthSwapchainCreated,
                                colorImages.Count,
                                depthImages.Count,
                                colorImages,
                                depthImages,
                                readyForFrameSubmission,
                                frameLoopProbe.Ready,
                                frameLoopProbe.SessionReadyEventObserved,
                                frameLoopProbe.LastSessionState,
                                frameLoopProbe.SessionBegan,
                                frameLoopProbe.FrameWaited,
                                frameLoopProbe.FrameBegan,
                                frameLoopProbe.ColorSwapchainImageAcquired,
                                frameLoopProbe.ColorSwapchainImageWaited,
                                frameLoopProbe.ColorSwapchainImageReleased,
                                frameLoopProbe.FrameEnded,
                                frameLoopProbe.PredictedDisplayTime,
                                [
                                    "Wrap acquired OpenXR VkImage handles as Veldrid render targets.",
                                    "Drive xrLocateViews, render stereo views into acquired swapchain images, and submit projection layers."
                                ],
                                errors);
                        }
                        finally
                        {
                            _ = destroySession(session);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(bindingPointer);
                    }
                }
                finally
                {
                    _ = destroyInstance(instance);
                }
            }
            finally
            {
                FreeNativeStringArray(nativeExtensionNames);
            }
        }
        catch (Exception ex) when (ex is AccessViolationException or SEHException or MarshalDirectiveException or DllNotFoundException)
        {
            return NotReady(false, false, null, false, false, false, [], null, [$"OpenXR compositor session bootstrap failed: {ex.Message}"]);
        }
        finally
        {
            NativeLibrary.Free(loaderHandle);
        }
    }

    public static long? SelectPreferredColorFormat(IReadOnlyList<long> formats)
    {
        // Vulkan: VK_FORMAT_R8G8B8A8_SRGB=43, VK_FORMAT_B8G8R8A8_SRGB=50.
        foreach (var preferred in new long[] { 43, 50 })
        {
            if (formats.Contains(preferred))
            {
                return preferred;
            }
        }

        return formats.Count == 0 ? null : formats[0];
    }

    public static long? SelectPreferredDepthFormat(IReadOnlyList<long> formats)
    {
        // Vulkan: D32_SFLOAT=126, D24_UNORM_S8_UINT=129, D32_SFLOAT_S8_UINT=130, D16_UNORM=124.
        foreach (var preferred in new long[] { 126, 129, 130, 124 })
        {
            if (formats.Contains(preferred))
            {
                return preferred;
            }
        }

        return null;
    }

    public static bool CanBeginOpenXrSession(int state)
    {
        return state == XrSessionStateReady;
    }

    public static string DescribeOpenXrSessionState(int? state)
    {
        return state switch
        {
            null => "NONE",
            0 => "UNKNOWN",
            1 => "IDLE",
            XrSessionStateReady => "READY",
            3 => "RUNNING",
            4 => "VISIBLE",
            5 => "FOCUSED",
            XrSessionStateStopping => "STOPPING",
            XrSessionStateLossPending => "LOSS_PENDING",
            XrSessionStateExiting => "EXITING",
            _ => state.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static IReadOnlyList<ulong> CreateSwapchainAndEnumerateImages(
        IntPtr session,
        XrCreateSwapchainDelegate createSwapchain,
        XrDestroySwapchainDelegate destroySwapchain,
        XrEnumerateSwapchainImagesDelegate enumerateSwapchainImages,
        long? format,
        ulong usageFlags,
        uint width,
        uint height,
        uint arraySize,
        string label,
        List<string> errors,
        out bool created)
    {
        created = false;
        if (format is null)
        {
            errors.Add($"No OpenXR {label} swapchain format was advertised by the runtime.");
            return [];
        }

        var createInfo = new XrSwapchainCreateInfo
        {
            Type = XrTypeSwapchainCreateInfo,
            Next = IntPtr.Zero,
            CreateFlags = 0,
            UsageFlags = usageFlags,
            Format = format.Value,
            SampleCount = 1,
            Width = width,
            Height = height,
            FaceCount = 1,
            ArraySize = arraySize,
            MipCount = 1
        };
        var result = createSwapchain(session, ref createInfo, out var swapchain);
        if (result != XrSuccess || swapchain == IntPtr.Zero)
        {
            errors.Add($"xrCreateSwapchain for {label} failed with XrResult {result}.");
            return [];
        }

        created = true;
        try
        {
            var countResult = enumerateSwapchainImages(swapchain, 0, out var count, null);
            if (countResult != XrSuccess)
            {
                errors.Add($"xrEnumerateSwapchainImages count query for {label} failed with XrResult {countResult}.");
                return [];
            }

            if (count == 0)
            {
                errors.Add($"xrEnumerateSwapchainImages returned no {label} images.");
                return [];
            }

            var images = new XrSwapchainImageVulkanKhr[count];
            for (var i = 0; i < images.Length; i++)
            {
                images[i] = new XrSwapchainImageVulkanKhr
                {
                    Type = XrTypeSwapchainImageVulkanKhr,
                    Next = IntPtr.Zero
                };
            }

            var enumerateResult = enumerateSwapchainImages(swapchain, count, out var written, images);
            if (enumerateResult != XrSuccess)
            {
                errors.Add($"xrEnumerateSwapchainImages for {label} failed with XrResult {enumerateResult}.");
                return [];
            }

            return images
                .Take(checked((int)Math.Min(count, written)))
                .Select(image => unchecked((ulong)image.Image))
                .ToArray();
        }
        finally
        {
            _ = destroySwapchain(swapchain);
        }
    }

    private static IReadOnlyList<long> EnumerateSwapchainFormats(
        IntPtr session,
        XrEnumerateSwapchainFormatsDelegate enumerateSwapchainFormats,
        List<string> errors,
        out bool ready)
    {
        ready = false;
        var countResult = enumerateSwapchainFormats(session, 0, out var count, null);
        if (countResult != XrSuccess)
        {
            errors.Add($"xrEnumerateSwapchainFormats count query failed with XrResult {countResult}.");
            return [];
        }

        if (count == 0)
        {
            errors.Add("xrEnumerateSwapchainFormats returned no formats.");
            return [];
        }

        var formats = new long[count];
        var enumerateResult = enumerateSwapchainFormats(session, count, out var written, formats);
        if (enumerateResult != XrSuccess)
        {
            errors.Add($"xrEnumerateSwapchainFormats failed with XrResult {enumerateResult}.");
            return [];
        }

        ready = written > 0;
        return formats.Take(checked((int)Math.Min(count, written))).ToArray();
    }

    private static FrameLoopProbeResult ProbeOneFrame(
        IntPtr session,
        IntPtr instance,
        XrCreateSwapchainDelegate createSwapchain,
        XrDestroySwapchainDelegate destroySwapchain,
        XrPollEventDelegate pollEvent,
        XrBeginSessionDelegate beginSession,
        XrEndSessionDelegate endSession,
        XrWaitFrameDelegate waitFrame,
        XrBeginFrameDelegate beginFrame,
        XrEndFrameDelegate endFrame,
        XrAcquireSwapchainImageDelegate acquireSwapchainImage,
        XrWaitSwapchainImageDelegate waitSwapchainImage,
        XrReleaseSwapchainImageDelegate releaseSwapchainImage,
        long? colorFormat,
        ulong usageFlags,
        uint width,
        uint height,
        uint arraySize,
        List<string> errors)
    {
        if (colorFormat is null)
        {
            errors.Add("OpenXR frame-loop probe skipped because no color swapchain format is available.");
            return default;
        }

        var createInfo = new XrSwapchainCreateInfo
        {
            Type = XrTypeSwapchainCreateInfo,
            Next = IntPtr.Zero,
            CreateFlags = 0,
            UsageFlags = usageFlags,
            Format = colorFormat.Value,
            SampleCount = 1,
            Width = width,
            Height = height,
            FaceCount = 1,
            ArraySize = arraySize,
            MipCount = 1
        };
        var createResult = createSwapchain(session, ref createInfo, out var swapchain);
        if (createResult != XrSuccess || swapchain == IntPtr.Zero)
        {
            errors.Add($"xrCreateSwapchain for frame-loop probe failed with XrResult {createResult}.");
            return default;
        }

        try
        {
            return ProbeOneFrameWithSwapchain(
                session,
                instance,
                swapchain,
                pollEvent,
                beginSession,
                endSession,
                waitFrame,
                beginFrame,
                endFrame,
                acquireSwapchainImage,
                waitSwapchainImage,
                releaseSwapchainImage,
                errors);
        }
        finally
        {
            _ = destroySwapchain(swapchain);
        }
    }

    private static FrameLoopProbeResult ProbeOneFrameWithSwapchain(
        IntPtr session,
        IntPtr instance,
        IntPtr colorSwapchain,
        XrPollEventDelegate pollEvent,
        XrBeginSessionDelegate beginSession,
        XrEndSessionDelegate endSession,
        XrWaitFrameDelegate waitFrame,
        XrBeginFrameDelegate beginFrame,
        XrEndFrameDelegate endFrame,
        XrAcquireSwapchainImageDelegate acquireSwapchainImage,
        XrWaitSwapchainImageDelegate waitSwapchainImage,
        XrReleaseSwapchainImageDelegate releaseSwapchainImage,
        List<string> errors)
    {
        var sessionBegan = false;
        var frameWaited = false;
        var frameBegan = false;
        var imageAcquired = false;
        var imageWaited = false;
        var imageReleased = false;
        var frameEnded = false;
        long? predictedDisplayTime = null;
        var sessionReadiness = WaitForSessionReadyEvent(instance, session, pollEvent, errors);
        if (!sessionReadiness.Ready)
        {
            return new FrameLoopProbeResult(
                false,
                sessionReadiness.ReadyEventObserved,
                sessionReadiness.LastState,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                null);
        }

        var sessionBeginInfo = new XrSessionBeginInfo
        {
            Type = XrTypeSessionBeginInfo,
            Next = IntPtr.Zero,
            PrimaryViewConfigurationType = XrViewConfigurationTypePrimaryStereo
        };
        var beginSessionResult = beginSession(session, ref sessionBeginInfo);
        if (beginSessionResult != XrSuccess)
        {
            errors.Add($"xrBeginSession for frame-loop probe failed with XrResult {beginSessionResult}. The OpenXR runtime may not have advanced the session to READY yet.");
            return new FrameLoopProbeResult(false, sessionReadiness.ReadyEventObserved, sessionReadiness.LastState, false, false, false, false, false, false, false, null);
        }

        sessionBegan = true;
        try
        {
            var frameWaitInfo = new XrFrameWaitInfo
            {
                Type = XrTypeFrameWaitInfo,
                Next = IntPtr.Zero
            };
            var frameState = new XrFrameState
            {
                Type = XrTypeFrameState,
                Next = IntPtr.Zero
            };
            var waitFrameResult = waitFrame(session, ref frameWaitInfo, ref frameState);
            if (waitFrameResult != XrSuccess)
            {
                errors.Add($"xrWaitFrame for frame-loop probe failed with XrResult {waitFrameResult}.");
                return new FrameLoopProbeResult(false, sessionReadiness.ReadyEventObserved, sessionReadiness.LastState, sessionBegan, false, false, false, false, false, false, null);
            }

            frameWaited = true;
            predictedDisplayTime = frameState.PredictedDisplayTime;
            var frameBeginInfo = new XrFrameBeginInfo
            {
                Type = XrTypeFrameBeginInfo,
                Next = IntPtr.Zero
            };
            var beginFrameResult = beginFrame(session, ref frameBeginInfo);
            if (beginFrameResult != XrSuccess)
            {
                errors.Add($"xrBeginFrame for frame-loop probe failed with XrResult {beginFrameResult}.");
                return new FrameLoopProbeResult(false, sessionReadiness.ReadyEventObserved, sessionReadiness.LastState, sessionBegan, frameWaited, false, false, false, false, false, predictedDisplayTime);
            }

            frameBegan = true;
            var acquireInfo = new XrSwapchainImageAcquireInfo
            {
                Type = XrTypeSwapchainImageAcquireInfo,
                Next = IntPtr.Zero
            };
            var acquireResult = acquireSwapchainImage(colorSwapchain, ref acquireInfo, out _);
            if (acquireResult != XrSuccess)
            {
                errors.Add($"xrAcquireSwapchainImage for frame-loop probe failed with XrResult {acquireResult}.");
            }
            else
            {
                imageAcquired = true;
                var imageWaitInfo = new XrSwapchainImageWaitInfo
                {
                    Type = XrTypeSwapchainImageWaitInfo,
                    Next = IntPtr.Zero,
                    Timeout = XrInfiniteDuration
                };
                var waitSwapchainResult = waitSwapchainImage(colorSwapchain, ref imageWaitInfo);
                if (waitSwapchainResult != XrSuccess)
                {
                    errors.Add($"xrWaitSwapchainImage for frame-loop probe failed with XrResult {waitSwapchainResult}.");
                }
                else
                {
                    imageWaited = true;
                }

                var releaseInfo = new XrSwapchainImageReleaseInfo
                {
                    Type = XrTypeSwapchainImageReleaseInfo,
                    Next = IntPtr.Zero
                };
                var releaseResult = releaseSwapchainImage(colorSwapchain, ref releaseInfo);
                if (releaseResult != XrSuccess)
                {
                    errors.Add($"xrReleaseSwapchainImage for frame-loop probe failed with XrResult {releaseResult}.");
                }
                else
                {
                    imageReleased = true;
                }
            }

            var frameEndInfo = new XrFrameEndInfo
            {
                Type = XrTypeFrameEndInfo,
                Next = IntPtr.Zero,
                DisplayTime = frameState.PredictedDisplayTime,
                EnvironmentBlendMode = XrEnvironmentBlendModeOpaque,
                LayerCount = 0,
                Layers = IntPtr.Zero
            };
            var endFrameResult = endFrame(session, ref frameEndInfo);
            if (endFrameResult != XrSuccess)
            {
                errors.Add($"xrEndFrame zero-layer probe failed with XrResult {endFrameResult}.");
            }
            else
            {
                frameEnded = true;
            }

            var ready = sessionBegan
                && frameWaited
                && frameBegan
                && imageAcquired
                && imageWaited
                && imageReleased
                && frameEnded;
            return new FrameLoopProbeResult(
                ready,
                sessionReadiness.ReadyEventObserved,
                sessionReadiness.LastState,
                sessionBegan,
                frameWaited,
                frameBegan,
                imageAcquired,
                imageWaited,
                imageReleased,
                frameEnded,
                predictedDisplayTime);
        }
        finally
        {
            _ = endSession(session);
        }
    }

    private static SessionReadinessProbeResult WaitForSessionReadyEvent(
        IntPtr instance,
        IntPtr session,
        XrPollEventDelegate pollEvent,
        List<string> errors)
    {
        var deadline = Stopwatch.GetTimestamp()
            + (long)(Stopwatch.Frequency * 0.25);
        int? lastState = null;
        var readyObserved = false;
        var eventBuffer = Marshal.AllocHGlobal(XrEventDataBufferSize);
        try
        {
            while (Stopwatch.GetTimestamp() < deadline)
            {
                ResetEventBuffer(eventBuffer);
                var pollResult = pollEvent(instance, eventBuffer);
                if (pollResult == XrEventUnavailable)
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (pollResult != XrSuccess)
                {
                    errors.Add($"xrPollEvent for frame-loop probe failed with XrResult {pollResult}.");
                    return new SessionReadinessProbeResult(false, readyObserved, lastState);
                }

                var eventType = Marshal.ReadInt32(eventBuffer);
                if (eventType != XrTypeEventDataSessionStateChanged)
                {
                    continue;
                }

                var stateChanged = Marshal.PtrToStructure<XrEventDataSessionStateChanged>(eventBuffer);
                if (stateChanged.Session != session)
                {
                    continue;
                }

                lastState = stateChanged.State;
                if (CanBeginOpenXrSession(stateChanged.State))
                {
                    return new SessionReadinessProbeResult(true, true, lastState);
                }

                if (stateChanged.State is XrSessionStateStopping or XrSessionStateLossPending or XrSessionStateExiting)
                {
                    errors.Add($"OpenXR session entered {DescribeOpenXrSessionState(stateChanged.State)} before the frame-loop probe could begin.");
                    return new SessionReadinessProbeResult(false, readyObserved, lastState);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(eventBuffer);
        }

        errors.Add($"OpenXR frame-loop probe did not observe XR_SESSION_STATE_READY before timeout; lastState={DescribeOpenXrSessionState(lastState)}.");
        return new SessionReadinessProbeResult(false, readyObserved, lastState);
    }

    private static void ResetEventBuffer(IntPtr eventBuffer)
    {
        Span<byte> zeros = stackalloc byte[32];
        for (var offset = 0; offset < XrEventDataBufferSize; offset += zeros.Length)
        {
            var count = Math.Min(zeros.Length, XrEventDataBufferSize - offset);
            Marshal.Copy(zeros[..count].ToArray(), 0, eventBuffer + offset, count);
        }

        Marshal.WriteInt32(eventBuffer, XrTypeEventDataBuffer);
        Marshal.WriteIntPtr(eventBuffer, IntPtr.Size == 8 ? 8 : 4, IntPtr.Zero);
    }

    private static bool QueryVulkanGraphicsRequirements(
        IntPtr instance,
        ulong systemId,
        XrGetInstanceProcAddrDelegate getInstanceProcAddr,
        List<string> errors)
    {
        if (!TryGetInstanceFunction(
                instance,
                getInstanceProcAddr,
                "xrGetVulkanGraphicsRequirements2KHR",
                out XrGetVulkanGraphicsRequirements2KhrDelegate getRequirements,
                out var error))
        {
            errors.Add(error);
            return false;
        }

        var requirements = new XrGraphicsRequirementsVulkanKhr
        {
            Type = XrTypeGraphicsRequirementsVulkanKhr,
            Next = IntPtr.Zero
        };
        var result = getRequirements(instance, systemId, ref requirements);
        if (result != XrSuccess)
        {
            errors.Add($"xrGetVulkanGraphicsRequirements2KHR failed with XrResult {result}.");
            return false;
        }

        return true;
    }

    private static RekallAgeOpenXrCompositorSessionBootstrapResult NotReady(
        bool instanceCreated,
        bool hmdSystemAvailable,
        ulong? systemId,
        bool requirementsReady,
        bool sessionCreated,
        bool formatsEnumerated,
        IReadOnlyList<long> formats,
        long? preferredFormat,
        IReadOnlyList<string> errors)
    {
        return new RekallAgeOpenXrCompositorSessionBootstrapResult(
            instanceCreated,
            hmdSystemAvailable,
            systemId,
            requirementsReady,
            sessionCreated,
            formatsEnumerated,
            formats,
            preferredFormat,
            null,
            false,
            false,
            0,
            0,
            [],
            [],
            false,
            false,
            false,
            null,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            null,
            [
                "Create an OpenXR-compatible Vulkan instance/device path.",
                "Create an XrSession with XrGraphicsBindingVulkan2KHR.",
                "Enumerate OpenXR swapchain formats before creating color/depth swapchains."
            ],
            errors);
    }

    private static bool TryLoadOpenXrLoader(out IntPtr handle)
    {
        foreach (var candidate in RekallAgeOpenXrLoaderCandidateNames.ForCurrentPlatform())
        {
            if (NativeLibrary.TryLoad(candidate, out handle))
            {
                return true;
            }
        }

        handle = IntPtr.Zero;
        return false;
    }

    private static bool TryGetRequiredExports(
        IntPtr loaderHandle,
        out XrCreateInstanceDelegate createInstance,
        out XrGetSystemDelegate getSystem,
        out XrGetInstanceProcAddrDelegate getInstanceProcAddr,
        out XrCreateSessionDelegate createSession,
        out XrDestroySessionDelegate destroySession,
        out XrEnumerateSwapchainFormatsDelegate enumerateSwapchainFormats,
        out XrCreateSwapchainDelegate createSwapchain,
        out XrDestroySwapchainDelegate destroySwapchain,
        out XrEnumerateSwapchainImagesDelegate enumerateSwapchainImages,
        out XrPollEventDelegate pollEvent,
        out XrBeginSessionDelegate beginSession,
        out XrEndSessionDelegate endSession,
        out XrWaitFrameDelegate waitFrame,
        out XrBeginFrameDelegate beginFrame,
        out XrEndFrameDelegate endFrame,
        out XrAcquireSwapchainImageDelegate acquireSwapchainImage,
        out XrWaitSwapchainImageDelegate waitSwapchainImage,
        out XrReleaseSwapchainImageDelegate releaseSwapchainImage,
        out XrDestroyInstanceDelegate destroyInstance,
        out string error)
    {
        createInstance = null!;
        getSystem = null!;
        getInstanceProcAddr = null!;
        createSession = null!;
        destroySession = null!;
        enumerateSwapchainFormats = null!;
        createSwapchain = null!;
        destroySwapchain = null!;
        enumerateSwapchainImages = null!;
        pollEvent = null!;
        beginSession = null!;
        endSession = null!;
        waitFrame = null!;
        beginFrame = null!;
        endFrame = null!;
        acquireSwapchainImage = null!;
        waitSwapchainImage = null!;
        releaseSwapchainImage = null!;
        destroyInstance = null!;
        if (!TryGetExport(loaderHandle, "xrCreateInstance", out createInstance, out error)
            || !TryGetExport(loaderHandle, "xrGetSystem", out getSystem, out error)
            || !TryGetExport(loaderHandle, "xrGetInstanceProcAddr", out getInstanceProcAddr, out error)
            || !TryGetExport(loaderHandle, "xrCreateSession", out createSession, out error)
            || !TryGetExport(loaderHandle, "xrDestroySession", out destroySession, out error)
            || !TryGetExport(loaderHandle, "xrEnumerateSwapchainFormats", out enumerateSwapchainFormats, out error)
            || !TryGetExport(loaderHandle, "xrCreateSwapchain", out createSwapchain, out error)
            || !TryGetExport(loaderHandle, "xrDestroySwapchain", out destroySwapchain, out error)
            || !TryGetExport(loaderHandle, "xrEnumerateSwapchainImages", out enumerateSwapchainImages, out error)
            || !TryGetExport(loaderHandle, "xrPollEvent", out pollEvent, out error)
            || !TryGetExport(loaderHandle, "xrBeginSession", out beginSession, out error)
            || !TryGetExport(loaderHandle, "xrEndSession", out endSession, out error)
            || !TryGetExport(loaderHandle, "xrWaitFrame", out waitFrame, out error)
            || !TryGetExport(loaderHandle, "xrBeginFrame", out beginFrame, out error)
            || !TryGetExport(loaderHandle, "xrEndFrame", out endFrame, out error)
            || !TryGetExport(loaderHandle, "xrAcquireSwapchainImage", out acquireSwapchainImage, out error)
            || !TryGetExport(loaderHandle, "xrWaitSwapchainImage", out waitSwapchainImage, out error)
            || !TryGetExport(loaderHandle, "xrReleaseSwapchainImage", out releaseSwapchainImage, out error)
            || !TryGetExport(loaderHandle, "xrDestroyInstance", out destroyInstance, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryGetExport<TDelegate>(
        IntPtr loaderHandle,
        string name,
        out TDelegate function,
        out string error)
        where TDelegate : Delegate
    {
        if (!NativeLibrary.TryGetExport(loaderHandle, name, out var symbol))
        {
            function = null!;
            error = $"OpenXR loader does not export {name}.";
            return false;
        }

        function = Marshal.GetDelegateForFunctionPointer<TDelegate>(symbol);
        error = string.Empty;
        return true;
    }

    private static bool TryGetInstanceFunction<TDelegate>(
        IntPtr instance,
        XrGetInstanceProcAddrDelegate getInstanceProcAddr,
        string name,
        out TDelegate function,
        out string error)
        where TDelegate : Delegate
    {
        var result = getInstanceProcAddr(instance, name, out var symbol);
        if (result != XrSuccess || symbol == IntPtr.Zero)
        {
            function = null!;
            error = $"{name} was not available from xrGetInstanceProcAddr (XrResult {result}).";
            return false;
        }

        function = Marshal.GetDelegateForFunctionPointer<TDelegate>(symbol);
        error = string.Empty;
        return true;
    }

    private static XrInstanceCreateInfo CreateInstanceCreateInfo(
        IReadOnlyList<string> enabledExtensions,
        out NativeStringArray nativeExtensionNames)
    {
        nativeExtensionNames = NativeStringArray.Create(enabledExtensions);
        return new XrInstanceCreateInfo
        {
            Type = XrTypeInstanceCreateInfo,
            Next = IntPtr.Zero,
            CreateFlags = 0,
            ApplicationInfo = new XrApplicationInfo
            {
                ApplicationName = FixedAscii("Rekall AGE", XrMaxApplicationNameSize),
                ApplicationVersion = 1,
                EngineName = FixedAscii("Rekall AGE", XrMaxEngineNameSize),
                EngineVersion = 1,
                ApiVersion = MakeOpenXrVersion(1, 0, 0)
            },
            EnabledApiLayerCount = 0,
            EnabledApiLayerNames = IntPtr.Zero,
            EnabledExtensionCount = checked((uint)enabledExtensions.Count),
            EnabledExtensionNames = nativeExtensionNames.PointerArray
        };
    }

    private static void FreeNativeStringArray(NativeStringArray value)
    {
        foreach (var pointer in value.StringPointers)
        {
            Marshal.FreeHGlobal(pointer);
        }

        if (value.PointerArray != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(value.PointerArray);
        }
    }

    private static byte[] FixedAscii(string value, int size)
    {
        var bytes = new byte[size];
        var encoded = Encoding.ASCII.GetBytes(value);
        Array.Copy(encoded, bytes, Math.Min(encoded.Length, size - 1));
        return bytes;
    }

    private static ulong MakeOpenXrVersion(uint major, uint minor, uint patch)
    {
        return ((ulong)major << 48) | ((ulong)minor << 32) | patch;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrCreateInstanceDelegate(ref XrInstanceCreateInfo createInfo, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrGetSystemDelegate(IntPtr instance, ref XrSystemGetInfo getInfo, out ulong systemId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrGetInstanceProcAddrDelegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPStr)] string name,
        out IntPtr function);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrCreateSessionDelegate(IntPtr instance, ref XrSessionCreateInfo createInfo, out IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrDestroySessionDelegate(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrEnumerateSwapchainFormatsDelegate(
        IntPtr session,
        uint formatCapacityInput,
        out uint formatCountOutput,
        [In, Out] long[]? formats);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrCreateSwapchainDelegate(IntPtr session, ref XrSwapchainCreateInfo createInfo, out IntPtr swapchain);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrDestroySwapchainDelegate(IntPtr swapchain);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrEnumerateSwapchainImagesDelegate(
        IntPtr swapchain,
        uint imageCapacityInput,
        out uint imageCountOutput,
        [In, Out] XrSwapchainImageVulkanKhr[]? images);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrPollEventDelegate(IntPtr instance, IntPtr eventData);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrBeginSessionDelegate(IntPtr session, ref XrSessionBeginInfo beginInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrEndSessionDelegate(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrWaitFrameDelegate(IntPtr session, ref XrFrameWaitInfo frameWaitInfo, ref XrFrameState frameState);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrBeginFrameDelegate(IntPtr session, ref XrFrameBeginInfo frameBeginInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrEndFrameDelegate(IntPtr session, ref XrFrameEndInfo frameEndInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrAcquireSwapchainImageDelegate(
        IntPtr swapchain,
        ref XrSwapchainImageAcquireInfo acquireInfo,
        out uint index);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrWaitSwapchainImageDelegate(IntPtr swapchain, ref XrSwapchainImageWaitInfo waitInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrReleaseSwapchainImageDelegate(IntPtr swapchain, ref XrSwapchainImageReleaseInfo releaseInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrDestroyInstanceDelegate(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrGetVulkanGraphicsRequirements2KhrDelegate(
        IntPtr instance,
        ulong systemId,
        ref XrGraphicsRequirementsVulkanKhr graphicsRequirements);

    [StructLayout(LayoutKind.Sequential)]
    private struct XrInstanceCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong CreateFlags;
        public XrApplicationInfo ApplicationInfo;
        public uint EnabledApiLayerCount;
        public IntPtr EnabledApiLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr EnabledExtensionNames;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrApplicationInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = XrMaxApplicationNameSize)]
        public byte[] ApplicationName;
        public uint ApplicationVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = XrMaxEngineNameSize)]
        public byte[] EngineName;
        public uint EngineVersion;
        public ulong ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSystemGetInfo
    {
        public int Type;
        public IntPtr Next;
        public int FormFactor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrGraphicsRequirementsVulkanKhr
    {
        public int Type;
        public IntPtr Next;
        public ulong MinApiVersionSupported;
        public ulong MaxApiVersionSupported;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrGraphicsBindingVulkanKhr
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Instance;
        public IntPtr PhysicalDevice;
        public IntPtr Device;
        public uint QueueFamilyIndex;
        public uint QueueIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong CreateFlags;
        public ulong UsageFlags;
        public long Format;
        public uint SampleCount;
        public uint Width;
        public uint Height;
        public uint FaceCount;
        public uint ArraySize;
        public uint MipCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageVulkanKhr
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Image;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSessionCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong CreateFlags;
        public ulong SystemId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrEventDataSessionStateChanged
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Session;
        public int State;
        public long Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSessionBeginInfo
    {
        public int Type;
        public IntPtr Next;
        public int PrimaryViewConfigurationType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameWaitInfo
    {
        public int Type;
        public IntPtr Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameState
    {
        public int Type;
        public IntPtr Next;
        public long PredictedDisplayTime;
        public long PredictedDisplayPeriod;
        public int ShouldRender;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameBeginInfo
    {
        public int Type;
        public IntPtr Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameEndInfo
    {
        public int Type;
        public IntPtr Next;
        public long DisplayTime;
        public int EnvironmentBlendMode;
        public uint LayerCount;
        public IntPtr Layers;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageAcquireInfo
    {
        public int Type;
        public IntPtr Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageWaitInfo
    {
        public int Type;
        public IntPtr Next;
        public long Timeout;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageReleaseInfo
    {
        public int Type;
        public IntPtr Next;
    }

    private readonly record struct FrameLoopProbeResult(
        bool Ready,
        bool SessionReadyEventObserved,
        int? LastSessionState,
        bool SessionBegan,
        bool FrameWaited,
        bool FrameBegan,
        bool ColorSwapchainImageAcquired,
        bool ColorSwapchainImageWaited,
        bool ColorSwapchainImageReleased,
        bool FrameEnded,
        long? PredictedDisplayTime);

    private readonly record struct SessionReadinessProbeResult(
        bool Ready,
        bool ReadyEventObserved,
        int? LastState);

    private readonly record struct NativeStringArray(IntPtr PointerArray, IReadOnlyList<IntPtr> StringPointers)
    {
        public static NativeStringArray Create(IReadOnlyList<string> values)
        {
            if (values.Count == 0)
            {
                return new NativeStringArray(IntPtr.Zero, []);
            }

            var stringPointers = values.Select(Marshal.StringToHGlobalAnsi).ToArray();
            var pointerArray = Marshal.AllocHGlobal(IntPtr.Size * stringPointers.Length);
            for (var i = 0; i < stringPointers.Length; i++)
            {
                Marshal.WriteIntPtr(pointerArray, i * IntPtr.Size, stringPointers[i]);
            }

            return new NativeStringArray(pointerArray, stringPointers);
        }
    }
}
