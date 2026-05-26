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
    bool HeadsetSessionReady,
    IReadOnlyList<string> RequiredExtensions,
    IReadOnlyList<string> EnabledExtensions,
    IReadOnlyList<string> MissingExtensions,
    IReadOnlyList<string> NextRenderSteps,
    IReadOnlyList<string> Errors);

public sealed class RekallAgeNativeOpenXrSessionBootstrap : IRekallAgeOpenXrSessionBootstrap
{
    private const int XrSuccess = 0;
    private const int XrTypeInstanceCreateInfo = 3;
    private const int XrTypeSystemGetInfo = 4;
    private const int XrFormFactorHeadMountedDisplay = 1;
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
            if (!TryGetRequiredExports(loaderHandle, out var createInstance, out var getSystem, out var destroyInstance, out var exportError))
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
                            RequiredExtensions,
                            enabledExtensions,
                            missingExtensions,
                            ["Connect or wake a headset and make it available to the active OpenXR runtime."],
                            [$"xrGetSystem for XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY failed with XrResult {systemResult}."]);
                    }

                    return new RekallAgeOpenXrSessionBootstrapResult(
                        true,
                        true,
                        true,
                        true,
                        systemId,
                        runtime.VulkanEnable2Available,
                        runtime.PrimaryStereoReady,
                        true,
                        RequiredExtensions,
                        enabledExtensions,
                        missingExtensions,
                        [
                            "Create Vulkan instance/device through XR_KHR_vulkan_enable2 or validate compatible Vulkan requirements.",
                            "Create an OpenXR session with XrGraphicsBindingVulkan2KHR.",
                            "Create primary-stereo swapchains and submit projection layers with xrEndFrame.",
                            "Drive view poses from xrLocateViews and controller actions from OpenXR input."
                        ],
                        []);
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
        out XrDestroyInstanceDelegate destroyInstance,
        out string error)
    {
        createInstance = null!;
        getSystem = null!;
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

        if (!NativeLibrary.TryGetExport(loaderHandle, "xrDestroyInstance", out var destroyInstanceSymbol))
        {
            error = "OpenXR loader does not export xrDestroyInstance.";
            return false;
        }

        createInstance = Marshal.GetDelegateForFunctionPointer<XrCreateInstanceDelegate>(createInstanceSymbol);
        getSystem = Marshal.GetDelegateForFunctionPointer<XrGetSystemDelegate>(getSystemSymbol);
        destroyInstance = Marshal.GetDelegateForFunctionPointer<XrDestroyInstanceDelegate>(destroyInstanceSymbol);
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
    private delegate int XrDestroyInstanceDelegate(IntPtr instance);

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
