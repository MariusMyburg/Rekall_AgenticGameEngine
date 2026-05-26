using System.Runtime.InteropServices;
using System.Text;

namespace Rekall.Age.Rendering;

public interface IRekallAgeOpenXrSessionBootstrap
{
    ValueTask<RekallAgeOpenXrSessionBootstrapResult> BootstrapAsync(CancellationToken cancellationToken);
}

public sealed record RekallAgeOpenXrSessionBootstrapResult(
    bool LoaderAvailable,
    bool RuntimeAvailable,
    bool InstanceCreated,
    bool HmdSystemAvailable,
    ulong? SystemId,
    bool VulkanEnable2Available,
    bool PrimaryStereoReady,
    bool VulkanGraphicsRequirementsReady,
    RekallAgeOpenXrVulkanGraphicsRequirements? VulkanGraphicsRequirements,
    bool PrimaryStereoViewConfigurationReady,
    IReadOnlyList<RekallAgeOpenXrViewConfigurationView> PrimaryStereoViews,
    bool HeadsetSessionReady,
    IReadOnlyList<string> RequiredExtensions,
    IReadOnlyList<string> EnabledExtensions,
    IReadOnlyList<string> MissingExtensions,
    IReadOnlyList<string> NextRenderSteps,
    IReadOnlyList<string> Errors);

public sealed record RekallAgeOpenXrVulkanGraphicsRequirements(
    string MinimumApiVersion,
    string MaximumApiVersion);

public sealed record RekallAgeOpenXrViewConfigurationView(
    int Index,
    uint RecommendedImageRectWidth,
    uint MaxImageRectWidth,
    uint RecommendedImageRectHeight,
    uint MaxImageRectHeight,
    uint RecommendedSwapchainSampleCount,
    uint MaxSwapchainSampleCount);

public sealed class RekallAgeNativeOpenXrSessionBootstrap : IRekallAgeOpenXrSessionBootstrap
{
    private const int XrSuccess = 0;
    private const int XrTypeInstanceCreateInfo = 3;
    private const int XrTypeSystemGetInfo = 4;
    private const int XrTypeViewConfigurationView = 41;
    private const int XrTypeGraphicsRequirementsVulkanKhr = 1000025002;
    private const int XrFormFactorHeadMountedDisplay = 1;
    private const int XrViewConfigurationTypePrimaryStereo = 2;
    private const int XrMaxApplicationNameSize = 128;
    private const int XrMaxEngineNameSize = 128;
    private static readonly string[] RequiredExtensions = ["XR_KHR_vulkan_enable2"];

    private readonly IRekallAgeOpenXrRuntimeProbe _runtimeProbe;

    public RekallAgeNativeOpenXrSessionBootstrap()
        : this(new RekallAgeNativeOpenXrRuntimeProbe())
    {
    }

    public RekallAgeNativeOpenXrSessionBootstrap(IRekallAgeOpenXrRuntimeProbe runtimeProbe)
    {
        _runtimeProbe = runtimeProbe;
    }

    public async ValueTask<RekallAgeOpenXrSessionBootstrapResult> BootstrapAsync(CancellationToken cancellationToken)
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
            return CreateNotReadyResult(
                runtime.LoaderAvailable,
                runtime.RuntimeAvailable,
                runtime.VulkanEnable2Available,
                runtime.PrimaryStereoReady,
                [],
                missingExtensions,
                runtime.Errors.Count > 0 ? runtime.Errors : BuildPreInstanceErrors(runtime, missingExtensions));
        }

        if (!TryLoadOpenXrLoader(out var loaderHandle))
        {
            return CreateNotReadyResult(
                false,
                runtime.RuntimeAvailable,
                runtime.VulkanEnable2Available,
                runtime.PrimaryStereoReady,
                [],
                missingExtensions,
                ["OpenXR loader was available during probing but could not be loaded for session bootstrap."]);
        }

        try
        {
            if (!TryGetRequiredExports(
                    loaderHandle,
                    out var createInstance,
                    out var getSystem,
                    out var getInstanceProcAddr,
                    out var destroyInstance,
                    out var exportError))
            {
                return CreateNotReadyResult(
                    true,
                    runtime.RuntimeAvailable,
                    runtime.VulkanEnable2Available,
                    runtime.PrimaryStereoReady,
                    [],
                    missingExtensions,
                    [exportError]);
            }

            var enabledExtensions = RequiredExtensions.ToArray();
            var createInfo = CreateInstanceCreateInfo(enabledExtensions, out var nativeExtensionNames);
            try
            {
                var createResult = createInstance(ref createInfo, out var instance);
                if (createResult != XrSuccess || instance == IntPtr.Zero)
                {
                    return CreateNotReadyResult(
                        true,
                        runtime.RuntimeAvailable,
                        runtime.VulkanEnable2Available,
                        runtime.PrimaryStereoReady,
                        enabledExtensions,
                        missingExtensions,
                        [$"xrCreateInstance failed with XrResult {createResult}."]);
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
                        return new RekallAgeOpenXrSessionBootstrapResult(
                            true,
                            true,
                            true,
                            false,
                            null,
                        runtime.VulkanEnable2Available,
                        runtime.PrimaryStereoReady,
                        false,
                        null,
                        false,
                        [],
                        false,
                        RequiredExtensions,
                        enabledExtensions,
                            missingExtensions,
                            ["Connect or wake a headset and make it available to the active OpenXR runtime."],
                            [$"xrGetSystem for XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY failed with XrResult {systemResult}."]);
                    }

                    var nativeErrors = new List<string>();
                    var vulkanRequirements = QueryVulkanGraphicsRequirements(
                        instance,
                        systemId,
                        getInstanceProcAddr,
                        nativeErrors,
                        out var vulkanGraphicsRequirementsReady);
                    var primaryStereoViews = QueryPrimaryStereoViews(
                        instance,
                        systemId,
                        getInstanceProcAddr,
                        nativeErrors,
                        out var primaryStereoViewConfigurationReady);
                    var headsetSessionReady = vulkanGraphicsRequirementsReady
                        && primaryStereoViewConfigurationReady
                        && primaryStereoViews.Count >= 2;

                    return new RekallAgeOpenXrSessionBootstrapResult(
                        true,
                        true,
                        true,
                        true,
                        systemId,
                        runtime.VulkanEnable2Available,
                        runtime.PrimaryStereoReady,
                        vulkanGraphicsRequirementsReady,
                        vulkanRequirements,
                        primaryStereoViewConfigurationReady,
                        primaryStereoViews,
                        headsetSessionReady,
                        RequiredExtensions,
                        enabledExtensions,
                        missingExtensions,
                        [
                            "Create Vulkan instance/device through XR_KHR_vulkan_enable2 or validate compatible Vulkan requirements.",
                            "Create an OpenXR session with XrGraphicsBindingVulkan2KHR.",
                            "Create primary-stereo swapchains and submit projection layers with xrEndFrame.",
                            "Drive view poses from xrLocateViews and controller actions from OpenXR input."
                        ],
                        nativeErrors);
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
            return CreateNotReadyResult(
                true,
                runtime.RuntimeAvailable,
                runtime.VulkanEnable2Available,
                runtime.PrimaryStereoReady,
                RequiredExtensions,
                missingExtensions,
                [$"OpenXR session bootstrap failed: {ex.Message}"]);
        }
        finally
        {
            NativeLibrary.Free(loaderHandle);
        }
    }

    private static RekallAgeOpenXrSessionBootstrapResult CreateNotReadyResult(
        bool loaderAvailable,
        bool runtimeAvailable,
        bool vulkanEnable2Available,
        bool primaryStereoReady,
        IReadOnlyList<string> enabledExtensions,
        IReadOnlyList<string> missingExtensions,
        IReadOnlyList<string> errors)
    {
        return new RekallAgeOpenXrSessionBootstrapResult(
            loaderAvailable,
            runtimeAvailable,
            false,
            false,
            null,
            vulkanEnable2Available,
            primaryStereoReady,
            false,
            null,
            false,
            [],
            false,
            RequiredExtensions,
            enabledExtensions,
            missingExtensions,
            BuildNextSteps(loaderAvailable, runtimeAvailable, missingExtensions),
            errors);
    }

    private static IReadOnlyList<string> BuildPreInstanceErrors(
        RekallAgeOpenXrProbeResult runtime,
        IReadOnlyList<string> missingExtensions)
    {
        var errors = new List<string>();
        if (!runtime.LoaderAvailable)
        {
            errors.Add("OpenXR loader was not found.");
        }
        else if (!runtime.RuntimeAvailable)
        {
            errors.Add("OpenXR runtime was not available for instance creation.");
        }

        if (missingExtensions.Count > 0)
        {
            errors.Add($"Missing required OpenXR extension(s): {string.Join(", ", missingExtensions)}.");
        }

        return errors;
    }

    private static IReadOnlyList<string> BuildNextSteps(
        bool loaderAvailable,
        bool runtimeAvailable,
        IReadOnlyList<string> missingExtensions)
    {
        var steps = new List<string>();
        if (!loaderAvailable)
        {
            steps.Add("Install and activate an OpenXR runtime.");
        }
        else if (!runtimeAvailable)
        {
            steps.Add("Start or select the active OpenXR runtime.");
        }

        if (missingExtensions.Count > 0)
        {
            steps.Add("Use an OpenXR runtime that exposes XR_KHR_vulkan_enable2.");
        }

        if (steps.Count == 0)
        {
            steps.Add("Create an OpenXR instance and query the head-mounted-display system.");
        }

        return steps;
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
        out XrDestroyInstanceDelegate destroyInstance,
        out string error)
    {
        createInstance = null!;
        getSystem = null!;
        getInstanceProcAddr = null!;
        destroyInstance = null!;
        if (!NativeLibrary.TryGetExport(loaderHandle, "xrCreateInstance", out var createInstanceSymbol))
        {
            error = "OpenXR loader does not export xrCreateInstance.";
            return false;
        }

        if (!NativeLibrary.TryGetExport(loaderHandle, "xrGetSystem", out var getSystemSymbol))
        {
            error = "OpenXR loader does not export xrGetSystem.";
            return false;
        }

        if (!NativeLibrary.TryGetExport(loaderHandle, "xrGetInstanceProcAddr", out var getInstanceProcAddrSymbol))
        {
            error = "OpenXR loader does not export xrGetInstanceProcAddr.";
            return false;
        }

        if (!NativeLibrary.TryGetExport(loaderHandle, "xrDestroyInstance", out var destroyInstanceSymbol))
        {
            error = "OpenXR loader does not export xrDestroyInstance.";
            return false;
        }

        createInstance = Marshal.GetDelegateForFunctionPointer<XrCreateInstanceDelegate>(createInstanceSymbol);
        getSystem = Marshal.GetDelegateForFunctionPointer<XrGetSystemDelegate>(getSystemSymbol);
        getInstanceProcAddr = Marshal.GetDelegateForFunctionPointer<XrGetInstanceProcAddrDelegate>(getInstanceProcAddrSymbol);
        destroyInstance = Marshal.GetDelegateForFunctionPointer<XrDestroyInstanceDelegate>(destroyInstanceSymbol);
        error = string.Empty;
        return true;
    }

    private static RekallAgeOpenXrVulkanGraphicsRequirements? QueryVulkanGraphicsRequirements(
        IntPtr instance,
        ulong systemId,
        XrGetInstanceProcAddrDelegate getInstanceProcAddr,
        List<string> errors,
        out bool ready)
    {
        ready = false;
        if (!TryGetInstanceFunction(
                instance,
                getInstanceProcAddr,
                "xrGetVulkanGraphicsRequirements2KHR",
                out XrGetVulkanGraphicsRequirements2KhrDelegate getRequirements,
                out var error))
        {
            errors.Add(error);
            return null;
        }

        var requirements = new XrGraphicsRequirementsVulkanKhr
        {
            Type = XrTypeGraphicsRequirementsVulkanKhr,
            Next = IntPtr.Zero,
            MinApiVersionSupported = 0,
            MaxApiVersionSupported = 0
        };
        var result = getRequirements(instance, systemId, ref requirements);
        if (result != XrSuccess)
        {
            errors.Add($"xrGetVulkanGraphicsRequirements2KHR failed with XrResult {result}.");
            return null;
        }

        ready = true;
        return new RekallAgeOpenXrVulkanGraphicsRequirements(
            FormatOpenXrVersion(requirements.MinApiVersionSupported),
            FormatOpenXrVersion(requirements.MaxApiVersionSupported));
    }

    private static IReadOnlyList<RekallAgeOpenXrViewConfigurationView> QueryPrimaryStereoViews(
        IntPtr instance,
        ulong systemId,
        XrGetInstanceProcAddrDelegate getInstanceProcAddr,
        List<string> errors,
        out bool ready)
    {
        ready = false;
        if (!TryGetInstanceFunction(
                instance,
                getInstanceProcAddr,
                "xrEnumerateViewConfigurationViews",
                out XrEnumerateViewConfigurationViewsDelegate enumerateViews,
                out var error))
        {
            errors.Add(error);
            return [];
        }

        var countResult = enumerateViews(
            instance,
            systemId,
            XrViewConfigurationTypePrimaryStereo,
            0,
            out var viewCount,
            null);
        if (countResult != XrSuccess)
        {
            errors.Add($"xrEnumerateViewConfigurationViews count query failed with XrResult {countResult}.");
            return [];
        }

        if (viewCount == 0)
        {
            errors.Add("xrEnumerateViewConfigurationViews returned no primary-stereo views.");
            return [];
        }

        var nativeViews = new XrViewConfigurationView[viewCount];
        for (var i = 0; i < nativeViews.Length; i++)
        {
            nativeViews[i] = new XrViewConfigurationView
            {
                Type = XrTypeViewConfigurationView,
                Next = IntPtr.Zero
            };
        }

        var enumerateResult = enumerateViews(
            instance,
            systemId,
            XrViewConfigurationTypePrimaryStereo,
            viewCount,
            out var written,
            nativeViews);
        if (enumerateResult != XrSuccess)
        {
            errors.Add($"xrEnumerateViewConfigurationViews failed with XrResult {enumerateResult}.");
            return [];
        }

        ready = written >= 2;
        return nativeViews
            .Take(checked((int)Math.Min(viewCount, written)))
            .Select((view, index) => new RekallAgeOpenXrViewConfigurationView(
                index,
                view.RecommendedImageRectWidth,
                view.MaxImageRectWidth,
                view.RecommendedImageRectHeight,
                view.MaxImageRectHeight,
                view.RecommendedSwapchainSampleCount,
                view.MaxSwapchainSampleCount))
            .ToArray();
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

    private static string FormatOpenXrVersion(ulong version)
    {
        var major = version >> 48;
        var minor = (version >> 32) & 0xffff;
        var patch = version & 0xffffffff;
        return $"{major}.{minor}.{patch}";
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
    private delegate int XrDestroyInstanceDelegate(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrGetVulkanGraphicsRequirements2KhrDelegate(
        IntPtr instance,
        ulong systemId,
        ref XrGraphicsRequirementsVulkanKhr graphicsRequirements);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int XrEnumerateViewConfigurationViewsDelegate(
        IntPtr instance,
        ulong systemId,
        int viewConfigurationType,
        uint viewCapacityInput,
        out uint viewCountOutput,
        [In, Out] XrViewConfigurationView[]? views);

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
    private struct XrViewConfigurationView
    {
        public int Type;
        public IntPtr Next;
        public uint RecommendedImageRectWidth;
        public uint MaxImageRectWidth;
        public uint RecommendedImageRectHeight;
        public uint MaxImageRectHeight;
        public uint RecommendedSwapchainSampleCount;
        public uint MaxSwapchainSampleCount;
    }

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
