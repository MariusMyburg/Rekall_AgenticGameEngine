using System.Runtime.InteropServices;
using System.Text;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeNativeVulkanLogicalDeviceBootstrap : IRekallAgeVulkanLogicalDeviceBootstrap
{
    private const int VkSuccess = 0;
    private const int VkStructureTypeApplicationInfo = 0;
    private const int VkStructureTypeInstanceCreateInfo = 1;
    private const int VkStructureTypeDeviceQueueCreateInfo = 2;
    private const int VkStructureTypeDeviceCreateInfo = 3;
    private const uint VkApiVersion10 = 4194304;
    private const uint VkQueueGraphicsBit = 0x00000001;
    private const uint VkQueueComputeBit = 0x00000002;
    private const uint VkQueueTransferBit = 0x00000004;
    private const uint VkQueueSparseBindingBit = 0x00000008;
    private const int PhysicalDevicePropertiesBufferSize = 2048;
    private const int PhysicalDeviceNameOffset = 20;
    private const int PhysicalDeviceNameSize = 256;

    public ValueTask<RekallAgeVulkanLogicalDeviceBootstrapResult> BootstrapAsync(
        string? preferredDeviceType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (!TryLoadVulkan(errors, out var library, out var loaderName))
        {
            return ValueTask.FromResult(Unavailable(null, errors));
        }

        try
        {
            var context = CreateContext(library, errors);
            if (context is null)
            {
                return ValueTask.FromResult(Unavailable(loaderName, errors));
            }

            return ValueTask.FromResult(BootstrapDevice(context.Value, loaderName!, preferredDeviceType, errors));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static RekallAgeVulkanLogicalDeviceBootstrapResult BootstrapDevice(
        VulkanBootstrapContext context,
        string loaderName,
        string? preferredDeviceType,
        List<string> errors)
    {
        using (context)
        {
            var candidates = EnumerateCandidateDevices(context, errors);
            var selection = RekallAgeVulkanDeviceSelector.Select(candidates, preferredDeviceType);
            if (selection is null)
            {
                errors.Add("No Vulkan physical device with a graphics queue was found.");
                return Unavailable(loaderName, errors);
            }

            var device = CreateLogicalDevice(context, selection, errors);
            if (device == IntPtr.Zero)
            {
                return Unavailable(loaderName, errors);
            }

            try
            {
                context.GetDeviceQueue(device, selection.QueueFamily.Index, 0, out var queue);
                if (queue == IntPtr.Zero)
                {
                    errors.Add("vkGetDeviceQueue returned a null graphics queue.");
                    return Unavailable(loaderName, errors);
                }

                return new RekallAgeVulkanLogicalDeviceBootstrapResult(
                    Available: true,
                    LoaderName: loaderName,
                    SelectedDevice: new RekallAgeVulkanSelectedDevice(
                        selection.Device.Name,
                        selection.Device.DeviceType,
                        selection.Device.ApiVersion,
                        selection.QueueFamily),
                    Errors: errors);
            }
            finally
            {
                context.DestroyDevice(device, IntPtr.Zero);
            }
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

    private static VulkanBootstrapContext? CreateContext(IntPtr library, List<string> errors)
    {
        if (!TryGetVulkanExport(library, "vkCreateInstance", errors, out VkCreateInstance createInstance)
            || !TryGetVulkanExport(library, "vkDestroyInstance", errors, out VkDestroyInstance destroyInstance)
            || !TryGetVulkanExport(library, "vkEnumeratePhysicalDevices", errors, out VkEnumeratePhysicalDevices enumerateDevices)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceProperties", errors, out VkGetPhysicalDeviceProperties getProperties)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceQueueFamilyProperties", errors, out VkGetPhysicalDeviceQueueFamilyProperties getQueueFamilies)
            || !TryGetVulkanExport(library, "vkCreateDevice", errors, out VkCreateDevice createDevice)
            || !TryGetVulkanExport(library, "vkDestroyDevice", errors, out VkDestroyDevice destroyDevice)
            || !TryGetVulkanExport(library, "vkGetDeviceQueue", errors, out VkGetDeviceQueue getDeviceQueue))
        {
            return null;
        }

        var appName = Marshal.StringToHGlobalAnsi("Rekall AGE");
        var engineName = Marshal.StringToHGlobalAnsi("Rekall AGE");
        var appInfoPointer = IntPtr.Zero;
        var createInfoPointer = IntPtr.Zero;
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

            var result = createInstance(createInfoPointer, IntPtr.Zero, out var instance);
            if (result != VkSuccess)
            {
                errors.Add($"vkCreateInstance failed with VkResult {result}.");
                return null;
            }

            return new VulkanBootstrapContext(
                instance,
                destroyInstance,
                enumerateDevices,
                getProperties,
                getQueueFamilies,
                createDevice,
                destroyDevice,
                getDeviceQueue);
        }
        finally
        {
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

    private static IReadOnlyList<RekallAgeVulkanCandidateDevice> EnumerateCandidateDevices(
        VulkanBootstrapContext context,
        List<string> errors)
    {
        var deviceCount = 0u;
        var result = context.EnumeratePhysicalDevices(context.Instance, ref deviceCount, IntPtr.Zero);
        if (result != VkSuccess)
        {
            errors.Add($"vkEnumeratePhysicalDevices count query failed with VkResult {result}.");
            return [];
        }

        if (deviceCount == 0)
        {
            return [];
        }

        var devicesBuffer = Marshal.AllocHGlobal(checked((int)deviceCount * IntPtr.Size));
        try
        {
            result = context.EnumeratePhysicalDevices(context.Instance, ref deviceCount, devicesBuffer);
            if (result != VkSuccess)
            {
                errors.Add($"vkEnumeratePhysicalDevices enumeration failed with VkResult {result}.");
                return [];
            }

            var devices = new List<RekallAgeVulkanCandidateDevice>((int)deviceCount);
            for (var index = 0; index < deviceCount; index++)
            {
                var physicalDevice = Marshal.ReadIntPtr(devicesBuffer, index * IntPtr.Size);
                devices.Add(ReadCandidateDevice(context, physicalDevice));
            }

            return devices;
        }
        finally
        {
            Marshal.FreeHGlobal(devicesBuffer);
        }
    }

    private static RekallAgeVulkanCandidateDevice ReadCandidateDevice(
        VulkanBootstrapContext context,
        IntPtr physicalDevice)
    {
        var properties = Marshal.AllocHGlobal(PhysicalDevicePropertiesBufferSize);
        try
        {
            Marshal.Copy(new byte[PhysicalDevicePropertiesBufferSize], 0, properties, PhysicalDevicePropertiesBufferSize);
            context.GetPhysicalDeviceProperties(physicalDevice, properties);
            var apiVersion = unchecked((uint)Marshal.ReadInt32(properties, 0));
            var deviceType = unchecked((uint)Marshal.ReadInt32(properties, 16));
            var nameBytes = new byte[PhysicalDeviceNameSize];
            Marshal.Copy(IntPtr.Add(properties, PhysicalDeviceNameOffset), nameBytes, 0, nameBytes.Length);
            var zeroIndex = Array.IndexOf(nameBytes, (byte)0);
            var nameLength = zeroIndex >= 0 ? zeroIndex : nameBytes.Length;
            var name = Encoding.UTF8.GetString(nameBytes, 0, nameLength);
            return new RekallAgeVulkanCandidateDevice(
                string.IsNullOrWhiteSpace(name) ? "<unnamed Vulkan device>" : name,
                RekallAgeVulkanDeviceTypeNames.FromVulkanDeviceType(deviceType),
                FormatVulkanVersion(apiVersion),
                ReadQueueFamilies(context, physicalDevice));
        }
        finally
        {
            Marshal.FreeHGlobal(properties);
        }
    }

    private static IReadOnlyList<RekallAgeVulkanQueueFamilyInfo> ReadQueueFamilies(
        VulkanBootstrapContext context,
        IntPtr physicalDevice)
    {
        var queueFamilyCount = 0u;
        context.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref queueFamilyCount, IntPtr.Zero);
        if (queueFamilyCount == 0)
        {
            return [];
        }

        var size = Marshal.SizeOf<VkQueueFamilyProperties>();
        var buffer = Marshal.AllocHGlobal(checked((int)queueFamilyCount * size));
        try
        {
            context.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref queueFamilyCount, buffer);
            var families = new List<RekallAgeVulkanQueueFamilyInfo>((int)queueFamilyCount);
            for (var index = 0; index < queueFamilyCount; index++)
            {
                var pointer = IntPtr.Add(buffer, index * size);
                var properties = Marshal.PtrToStructure<VkQueueFamilyProperties>(pointer);
                families.Add(new RekallAgeVulkanQueueFamilyInfo(
                    (uint)index,
                    DecodeQueueCapabilities(properties.QueueFlags),
                    properties.QueueCount));
            }

            return families;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr CreateLogicalDevice(
        VulkanBootstrapContext context,
        RekallAgeVulkanDeviceSelection selection,
        List<string> errors)
    {
        var priorityPointer = Marshal.AllocHGlobal(sizeof(float));
        var queueCreateInfoPointer = IntPtr.Zero;
        var deviceCreateInfoPointer = IntPtr.Zero;
        try
        {
            Marshal.Copy(BitConverter.GetBytes(1.0f), 0, priorityPointer, sizeof(float));
            var queueCreateInfo = new VkDeviceQueueCreateInfo(
                VkStructureTypeDeviceQueueCreateInfo,
                IntPtr.Zero,
                0,
                selection.QueueFamily.Index,
                1,
                priorityPointer);
            queueCreateInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkDeviceQueueCreateInfo>());
            Marshal.StructureToPtr(queueCreateInfo, queueCreateInfoPointer, false);

            var deviceCreateInfo = new VkDeviceCreateInfo(
                VkStructureTypeDeviceCreateInfo,
                IntPtr.Zero,
                0,
                1,
                queueCreateInfoPointer,
                0,
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                IntPtr.Zero);
            deviceCreateInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkDeviceCreateInfo>());
            Marshal.StructureToPtr(deviceCreateInfo, deviceCreateInfoPointer, false);

            var physicalDevice = FindPhysicalDeviceHandle(context, selection.Device.Name);
            if (physicalDevice == IntPtr.Zero)
            {
                errors.Add($"Selected Vulkan physical device '{selection.Device.Name}' could not be resolved.");
                return IntPtr.Zero;
            }

            var result = context.CreateDevice(physicalDevice, deviceCreateInfoPointer, IntPtr.Zero, out var device);
            if (result != VkSuccess)
            {
                errors.Add($"vkCreateDevice failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return device;
        }
        finally
        {
            if (deviceCreateInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(deviceCreateInfoPointer);
            }

            if (queueCreateInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(queueCreateInfoPointer);
            }

            Marshal.FreeHGlobal(priorityPointer);
        }
    }

    private static IntPtr FindPhysicalDeviceHandle(VulkanBootstrapContext context, string selectedName)
    {
        var count = 0u;
        if (context.EnumeratePhysicalDevices(context.Instance, ref count, IntPtr.Zero) != VkSuccess || count == 0)
        {
            return IntPtr.Zero;
        }

        var devicesBuffer = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
        try
        {
            if (context.EnumeratePhysicalDevices(context.Instance, ref count, devicesBuffer) != VkSuccess)
            {
                return IntPtr.Zero;
            }

            for (var index = 0; index < count; index++)
            {
                var physicalDevice = Marshal.ReadIntPtr(devicesBuffer, index * IntPtr.Size);
                if (ReadCandidateDevice(context, physicalDevice).Name.Equals(selectedName, StringComparison.Ordinal))
                {
                    return physicalDevice;
                }
            }

            return IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(devicesBuffer);
        }
    }

    private static IReadOnlyList<string> DecodeQueueCapabilities(uint queueFlags)
    {
        var capabilities = new List<string>();
        if ((queueFlags & VkQueueGraphicsBit) != 0)
        {
            capabilities.Add("graphics");
        }

        if ((queueFlags & VkQueueComputeBit) != 0)
        {
            capabilities.Add("compute");
        }

        if ((queueFlags & VkQueueTransferBit) != 0)
        {
            capabilities.Add("transfer");
        }

        if ((queueFlags & VkQueueSparseBindingBit) != 0)
        {
            capabilities.Add("sparse-binding");
        }

        return capabilities;
    }

    private static RekallAgeVulkanLogicalDeviceBootstrapResult Unavailable(
        string? loaderName,
        IReadOnlyList<string> errors)
    {
        return new RekallAgeVulkanLogicalDeviceBootstrapResult(
            Available: false,
            LoaderName: loaderName,
            SelectedDevice: null,
            Errors: errors.ToArray());
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
    private delegate int VkCreateInstance(IntPtr createInfo, IntPtr allocator, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyInstance(IntPtr instance, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkEnumeratePhysicalDevices(
        IntPtr instance,
        ref uint physicalDeviceCount,
        IntPtr physicalDevices);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetPhysicalDeviceProperties(IntPtr physicalDevice, IntPtr properties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetPhysicalDeviceQueueFamilyProperties(
        IntPtr physicalDevice,
        ref uint queueFamilyPropertyCount,
        IntPtr queueFamilyProperties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateDevice(
        IntPtr physicalDevice,
        IntPtr createInfo,
        IntPtr allocator,
        out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyDevice(IntPtr device, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetDeviceQueue(
        IntPtr device,
        uint queueFamilyIndex,
        uint queueIndex,
        out IntPtr queue);

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

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkQueueFamilyProperties(
        uint QueueFlags,
        uint QueueCount,
        uint TimestampValidBits,
        uint MinImageTransferGranularityWidth,
        uint MinImageTransferGranularityHeight,
        uint MinImageTransferGranularityDepth);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkDeviceQueueCreateInfo(
        int SType,
        IntPtr PNext,
        uint Flags,
        uint QueueFamilyIndex,
        uint QueueCount,
        IntPtr QueuePriorities);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkDeviceCreateInfo(
        int SType,
        IntPtr PNext,
        uint Flags,
        uint QueueCreateInfoCount,
        IntPtr QueueCreateInfos,
        uint EnabledLayerCount,
        IntPtr EnabledLayerNames,
        uint EnabledExtensionCount,
        IntPtr EnabledExtensionNames,
        IntPtr EnabledFeatures);

    private readonly record struct VulkanBootstrapContext(
        IntPtr Instance,
        VkDestroyInstance DestroyInstance,
        VkEnumeratePhysicalDevices EnumeratePhysicalDevices,
        VkGetPhysicalDeviceProperties GetPhysicalDeviceProperties,
        VkGetPhysicalDeviceQueueFamilyProperties GetPhysicalDeviceQueueFamilyProperties,
        VkCreateDevice CreateDevice,
        VkDestroyDevice DestroyDevice,
        VkGetDeviceQueue GetDeviceQueue) : IDisposable
    {
        public void Dispose()
        {
            if (Instance != IntPtr.Zero)
            {
                DestroyInstance(Instance, IntPtr.Zero);
            }
        }
    }
}
