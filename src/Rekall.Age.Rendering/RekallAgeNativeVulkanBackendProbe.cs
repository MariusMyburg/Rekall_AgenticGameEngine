using System.Runtime.InteropServices;
using System.Text;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeNativeVulkanBackendProbe : IRekallAgeVulkanBackendProbe
{
    private const int VkSuccess = 0;
    private const int ExtensionPropertySize = 260;
    private const int ExtensionNameSize = 256;
    private const int VkStructureTypeApplicationInfo = 0;
    private const int VkStructureTypeInstanceCreateInfo = 1;
    private const uint VkApiVersion10 = 4194304;
    private const int PhysicalDevicePropertiesBufferSize = 2048;
    private const int PhysicalDeviceNameOffset = 20;
    private const int PhysicalDeviceNameSize = 256;

    public ValueTask<RekallAgeVulkanProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (!TryLoadVulkan(errors, out var library, out var loaderName))
        {
            return ValueTask.FromResult(new RekallAgeVulkanProbeResult(
                Available: false,
                LoaderName: null,
                ApiVersion: null,
                InstanceExtensions: [],
                PhysicalDevices: [],
                Errors: errors));
        }

        try
        {
            var apiVersion = ProbeInstanceVersion(library, errors);
            var extensions = ProbeInstanceExtensions(library, errors);
            var physicalDevices = ProbePhysicalDevices(library, apiVersion, errors);
            return ValueTask.FromResult(new RekallAgeVulkanProbeResult(
                Available: errors.Count == 0 || extensions.Count > 0 || apiVersion is not null || physicalDevices.Count > 0,
                LoaderName: loaderName,
                ApiVersion: apiVersion,
                InstanceExtensions: extensions,
                PhysicalDevices: physicalDevices,
                Errors: errors));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static bool TryLoadVulkan(List<string> errors, out IntPtr library, out string? loaderName)
    {
        foreach (var candidate in RekallAgeVulkanLoaderCandidateNames.ForCurrentPlatform())
        {
            if (NativeLibrary.TryLoad(candidate, out library))
            {
                loaderName = candidate;
                return true;
            }
        }

        library = IntPtr.Zero;
        loaderName = null;
        errors.Add("Vulkan loader was not found.");
        return false;
    }

    private static string? ProbeInstanceVersion(IntPtr library, List<string> errors)
    {
        if (!NativeLibrary.TryGetExport(library, "vkEnumerateInstanceVersion", out var address))
        {
            return "1.0.0";
        }

        var enumerate = Marshal.GetDelegateForFunctionPointer<VkEnumerateInstanceVersion>(address);
        var result = enumerate(out var version);
        if (result != VkSuccess)
        {
            errors.Add($"vkEnumerateInstanceVersion failed with VkResult {result}.");
            return null;
        }

        return FormatVulkanVersion(version);
    }

    private static IReadOnlyList<string> ProbeInstanceExtensions(IntPtr library, List<string> errors)
    {
        if (!NativeLibrary.TryGetExport(library, "vkEnumerateInstanceExtensionProperties", out var address))
        {
            errors.Add("vkEnumerateInstanceExtensionProperties was not exported by the Vulkan loader.");
            return [];
        }

        var enumerate = Marshal.GetDelegateForFunctionPointer<VkEnumerateInstanceExtensionProperties>(address);
        var count = 0u;
        var result = enumerate(IntPtr.Zero, ref count, IntPtr.Zero);
        if (result != VkSuccess)
        {
            errors.Add($"vkEnumerateInstanceExtensionProperties count query failed with VkResult {result}.");
            return [];
        }

        if (count == 0)
        {
            return [];
        }

        var bytes = checked((int)count * ExtensionPropertySize);
        var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            result = enumerate(IntPtr.Zero, ref count, buffer);
            if (result != VkSuccess)
            {
                errors.Add($"vkEnumerateInstanceExtensionProperties enumeration failed with VkResult {result}.");
                return [];
            }

            var extensions = new List<string>((int)count);
            for (var index = 0; index < count; index++)
            {
                var item = IntPtr.Add(buffer, index * ExtensionPropertySize);
                var nameBytes = new byte[ExtensionNameSize];
                Marshal.Copy(item, nameBytes, 0, nameBytes.Length);
                var zero = Array.IndexOf(nameBytes, (byte)0);
                var length = zero >= 0 ? zero : nameBytes.Length;
                var name = Encoding.ASCII.GetString(nameBytes, 0, length);
                if (name.Length > 0)
                {
                    extensions.Add(name);
                }
            }

            return extensions
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<RekallAgeVulkanPhysicalDeviceInfo> ProbePhysicalDevices(
        IntPtr library,
        string? apiVersion,
        List<string> errors)
    {
        if (!TryGetVulkanExport(library, "vkCreateInstance", errors, out VkCreateInstance createInstance)
            || !TryGetVulkanExport(library, "vkDestroyInstance", errors, out VkDestroyInstance destroyInstance)
            || !TryGetVulkanExport(library, "vkEnumeratePhysicalDevices", errors, out VkEnumeratePhysicalDevices enumerateDevices)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceProperties", errors, out VkGetPhysicalDeviceProperties getProperties))
        {
            return [];
        }

        var appName = Marshal.StringToHGlobalAnsi("Rekall AGE");
        var engineName = Marshal.StringToHGlobalAnsi("Rekall AGE");
        var appInfoPointer = IntPtr.Zero;
        var createInfoPointer = IntPtr.Zero;
        var instance = IntPtr.Zero;
        try
        {
            var appInfo = new VkApplicationInfo(
                VkStructureTypeApplicationInfo,
                IntPtr.Zero,
                appName,
                1,
                engineName,
                1,
                VkApiVersion10);
            appInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkApplicationInfo>());
            Marshal.StructureToPtr(appInfo, appInfoPointer, false);

            var createInfo = new VkInstanceCreateInfo(
                VkStructureTypeInstanceCreateInfo,
                IntPtr.Zero,
                0,
                appInfoPointer,
                0,
                IntPtr.Zero,
                0,
                IntPtr.Zero);
            createInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkInstanceCreateInfo>());
            Marshal.StructureToPtr(createInfo, createInfoPointer, false);

            var createResult = createInstance(createInfoPointer, IntPtr.Zero, out instance);
            if (createResult != VkSuccess)
            {
                errors.Add($"vkCreateInstance failed with VkResult {createResult}.");
                return [];
            }

            var deviceCount = 0u;
            var enumerateResult = enumerateDevices(instance, ref deviceCount, IntPtr.Zero);
            if (enumerateResult != VkSuccess)
            {
                errors.Add($"vkEnumeratePhysicalDevices count query failed with VkResult {enumerateResult}.");
                return [];
            }

            if (deviceCount == 0)
            {
                return [];
            }

            var devicesBuffer = Marshal.AllocHGlobal(checked((int)deviceCount * IntPtr.Size));
            try
            {
                enumerateResult = enumerateDevices(instance, ref deviceCount, devicesBuffer);
                if (enumerateResult != VkSuccess)
                {
                    errors.Add($"vkEnumeratePhysicalDevices enumeration failed with VkResult {enumerateResult}.");
                    return [];
                }

                var devices = new List<RekallAgeVulkanPhysicalDeviceInfo>((int)deviceCount);
                for (var index = 0; index < deviceCount; index++)
                {
                    var device = Marshal.ReadIntPtr(devicesBuffer, index * IntPtr.Size);
                    devices.Add(ReadPhysicalDeviceInfo(getProperties, device, apiVersion));
                }

                return devices
                    .OrderBy(device => device.Name, StringComparer.Ordinal)
                    .ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(devicesBuffer);
            }
        }
        finally
        {
            if (instance != IntPtr.Zero)
            {
                destroyInstance(instance, IntPtr.Zero);
            }

            if (createInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(createInfoPointer);
            }

            if (appInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(appInfoPointer);
            }

            Marshal.FreeHGlobal(appName);
            Marshal.FreeHGlobal(engineName);
        }
    }

    private static RekallAgeVulkanPhysicalDeviceInfo ReadPhysicalDeviceInfo(
        VkGetPhysicalDeviceProperties getProperties,
        IntPtr device,
        string? defaultApiVersion)
    {
        var properties = Marshal.AllocHGlobal(PhysicalDevicePropertiesBufferSize);
        try
        {
            Span<byte> zero = stackalloc byte[PhysicalDevicePropertiesBufferSize];
            Marshal.Copy(zero.ToArray(), 0, properties, zero.Length);
            getProperties(device, properties);

            var apiVersion = unchecked((uint)Marshal.ReadInt32(properties, 0));
            var deviceType = unchecked((uint)Marshal.ReadInt32(properties, 16));
            var nameBytes = new byte[PhysicalDeviceNameSize];
            Marshal.Copy(IntPtr.Add(properties, PhysicalDeviceNameOffset), nameBytes, 0, nameBytes.Length);
            var zeroIndex = Array.IndexOf(nameBytes, (byte)0);
            var nameLength = zeroIndex >= 0 ? zeroIndex : nameBytes.Length;
            var name = Encoding.UTF8.GetString(nameBytes, 0, nameLength);
            return new RekallAgeVulkanPhysicalDeviceInfo(
                string.IsNullOrWhiteSpace(name) ? "<unnamed Vulkan device>" : name,
                RekallAgeVulkanDeviceTypeNames.FromVulkanDeviceType(deviceType),
                apiVersion == 0 ? defaultApiVersion ?? "<unknown>" : FormatVulkanVersion(apiVersion));
        }
        finally
        {
            Marshal.FreeHGlobal(properties);
        }
    }

    private static bool TryGetVulkanExport<TDelegate>(
        IntPtr library,
        string name,
        List<string> errors,
        out TDelegate function)
        where TDelegate : Delegate
    {
        if (!NativeLibrary.TryGetExport(library, name, out var address))
        {
            errors.Add($"{name} was not exported by the Vulkan loader.");
            function = null!;
            return false;
        }

        function = Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
        return true;
    }

    private static string FormatVulkanVersion(uint version)
    {
        var major = version >> 22;
        var minor = (version >> 12) & 0x3ff;
        var patch = version & 0xfff;
        return $"{major}.{minor}.{patch}";
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkEnumerateInstanceVersion(out uint apiVersion);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkEnumerateInstanceExtensionProperties(
        IntPtr layerName,
        ref uint propertyCount,
        IntPtr properties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateInstance(
        IntPtr createInfo,
        IntPtr allocator,
        out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyInstance(
        IntPtr instance,
        IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkEnumeratePhysicalDevices(
        IntPtr instance,
        ref uint physicalDeviceCount,
        IntPtr physicalDevices);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetPhysicalDeviceProperties(
        IntPtr physicalDevice,
        IntPtr properties);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkApplicationInfo(
        int SType,
        IntPtr PNext,
        IntPtr ApplicationName,
        uint ApplicationVersion,
        IntPtr EngineName,
        uint EngineVersion,
        uint ApiVersion);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkInstanceCreateInfo(
        int SType,
        IntPtr PNext,
        uint Flags,
        IntPtr ApplicationInfo,
        uint EnabledLayerCount,
        IntPtr EnabledLayerNames,
        uint EnabledExtensionCount,
        IntPtr EnabledExtensionNames);
}
