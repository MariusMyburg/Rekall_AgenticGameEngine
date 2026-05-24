using System.Runtime.InteropServices;
using System.Text;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeNativeVulkanBufferSmoke : IRekallAgeVulkanBufferSmoke
{
    private const int VkSuccess = 0;
    private const int VkStructureTypeApplicationInfo = 0;
    private const int VkStructureTypeInstanceCreateInfo = 1;
    private const int VkStructureTypeDeviceQueueCreateInfo = 2;
    private const int VkStructureTypeDeviceCreateInfo = 3;
    private const int VkStructureTypeMemoryAllocateInfo = 5;
    private const int VkStructureTypeBufferCreateInfo = 12;
    private const uint VkApiVersion10 = 4194304;
    private const uint VkSharingModeExclusive = 0;
    private const uint VkQueueGraphicsBit = 0x00000001;
    private const uint VkQueueComputeBit = 0x00000002;
    private const uint VkQueueTransferBit = 0x00000004;
    private const uint VkQueueSparseBindingBit = 0x00000008;
    private const uint VkBufferUsageTransferSrcBit = 0x00000001;
    private const uint VkBufferUsageTransferDstBit = 0x00000002;
    private const uint VkBufferUsageUniformBufferBit = 0x00000010;
    private const uint VkBufferUsageStorageBufferBit = 0x00000020;
    private const uint VkBufferUsageIndexBufferBit = 0x00000040;
    private const uint VkBufferUsageVertexBufferBit = 0x00000080;
    private const uint VkMemoryPropertyHostVisibleBit = 0x00000001;
    private const uint VkMemoryPropertyHostCoherentBit = 0x00000002;
    private const uint VkMemoryPropertyHostCachedBit = 0x00000008;
    private const uint VkMemoryPropertyDeviceLocalBit = 0x00000004;
    private const int PhysicalDevicePropertiesBufferSize = 2048;
    private const int PhysicalDeviceMemoryPropertiesBufferSize = 4096;
    private const int PhysicalDeviceNameOffset = 20;
    private const int PhysicalDeviceNameSize = 256;
    private const int MemoryTypesOffset = 4;
    private const int MemoryTypeSize = 8;

    public ValueTask<RekallAgeVulkanBufferSmokeResult> CreateMappedBufferAsync(
        ulong sizeBytes,
        string usage,
        string? preferredDeviceType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (sizeBytes == 0)
        {
            errors.Add("Vulkan buffer size must be greater than zero.");
            return ValueTask.FromResult(Unavailable(null, null, sizeBytes, usage, null, [], false, false, 0, errors));
        }

        if (!TryLoadVulkan(errors, out var library, out var loaderName))
        {
            return ValueTask.FromResult(Unavailable(null, null, sizeBytes, usage, null, [], false, false, 0, errors));
        }

        try
        {
            var context = CreateContext(library, errors);
            if (context is null)
            {
                return ValueTask.FromResult(Unavailable(loaderName, null, sizeBytes, usage, null, [], false, false, 0, errors));
            }

            return ValueTask.FromResult(CreateMappedBuffer(context.Value, loaderName!, sizeBytes, usage, preferredDeviceType, errors));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static RekallAgeVulkanBufferSmokeResult CreateMappedBuffer(
        VulkanBufferContext context,
        string loaderName,
        ulong sizeBytes,
        string usage,
        string? preferredDeviceType,
        List<string> errors)
    {
        using (context)
        {
            var nativeDevices = EnumerateCandidateDevices(context, errors);
            var selection = RekallAgeVulkanDeviceSelector.Select(nativeDevices.Select(device => device.Device), preferredDeviceType);
            if (selection is null)
            {
                errors.Add("No Vulkan physical device with a graphics queue was found.");
                return Unavailable(loaderName, null, sizeBytes, usage, null, [], false, false, 0, errors);
            }

            var nativeDevice = nativeDevices.First(device => device.Device.Name.Equals(selection.Device.Name, StringComparison.Ordinal));
            var logicalDevice = CreateLogicalDevice(context, nativeDevice.Handle, selection, errors);
            if (logicalDevice == IntPtr.Zero)
            {
                return Unavailable(loaderName, ToSelectedDevice(selection), sizeBytes, usage, null, [], false, false, 0, errors);
            }

            try
            {
                var buffer = CreateBuffer(context, logicalDevice, sizeBytes, usage, errors);
                if (buffer == IntPtr.Zero)
                {
                    return Unavailable(loaderName, ToSelectedDevice(selection), sizeBytes, usage, null, [], false, false, 0, errors);
                }

                try
                {
                    var requirements = GetMemoryRequirements(context, logicalDevice, buffer);
                    var memoryTypes = ReadMemoryTypes(context, nativeDevice.Handle);
                    var memoryTypeIndex = RekallAgeVulkanMemoryTypeSelector.Select(
                        memoryTypes,
                        requirements.MemoryTypeBits,
                        ["host-visible", "host-coherent"]);
                    if (memoryTypeIndex is null)
                    {
                        errors.Add("No compatible Vulkan memory type was found.");
                        return Unavailable(loaderName, ToSelectedDevice(selection), sizeBytes, usage, null, [], false, false, 0, errors);
                    }

                    var memoryProperties = memoryTypes.First(item => item.Index == memoryTypeIndex.Value).Properties;
                    var memory = AllocateMemory(context, logicalDevice, requirements.Size, memoryTypeIndex.Value, errors);
                    if (memory == IntPtr.Zero)
                    {
                        return Unavailable(loaderName, ToSelectedDevice(selection), sizeBytes, usage, memoryTypeIndex, memoryProperties, false, false, 0, errors);
                    }

                    try
                    {
                        var bindResult = context.BindBufferMemory(logicalDevice, buffer, memory, 0);
                        if (bindResult != VkSuccess)
                        {
                            errors.Add($"vkBindBufferMemory failed with VkResult {bindResult}.");
                            return Unavailable(loaderName, ToSelectedDevice(selection), sizeBytes, usage, memoryTypeIndex, memoryProperties, false, false, 0, errors);
                        }

                        var bytesWritten = MapAndWrite(context, logicalDevice, memory, Math.Min(sizeBytes, 16), errors);
                        if (bytesWritten == 0)
                        {
                            return Unavailable(loaderName, ToSelectedDevice(selection), sizeBytes, usage, memoryTypeIndex, memoryProperties, true, false, 0, errors);
                        }

                        return new RekallAgeVulkanBufferSmokeResult(
                            Created: true,
                            LoaderName: loaderName,
                            SelectedDevice: ToSelectedDevice(selection),
                            SizeBytes: sizeBytes,
                            Usage: NormalizeUsage(usage),
                            MemoryTypeIndex: memoryTypeIndex,
                            MemoryProperties: memoryProperties,
                            Bound: true,
                            Mapped: true,
                            BytesWritten: bytesWritten,
                            Errors: errors);
                    }
                    finally
                    {
                        context.FreeMemory(logicalDevice, memory, IntPtr.Zero);
                    }
                }
                finally
                {
                    context.DestroyBuffer(logicalDevice, buffer, IntPtr.Zero);
                }
            }
            finally
            {
                context.DestroyDevice(logicalDevice, IntPtr.Zero);
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

    private static VulkanBufferContext? CreateContext(IntPtr library, List<string> errors)
    {
        if (!TryGetVulkanExport(library, "vkCreateInstance", errors, out VkCreateInstance createInstance)
            || !TryGetVulkanExport(library, "vkDestroyInstance", errors, out VkDestroyInstance destroyInstance)
            || !TryGetVulkanExport(library, "vkEnumeratePhysicalDevices", errors, out VkEnumeratePhysicalDevices enumerateDevices)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceProperties", errors, out VkGetPhysicalDeviceProperties getProperties)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceQueueFamilyProperties", errors, out VkGetPhysicalDeviceQueueFamilyProperties getQueueFamilies)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceMemoryProperties", errors, out VkGetPhysicalDeviceMemoryProperties getMemoryProperties)
            || !TryGetVulkanExport(library, "vkCreateDevice", errors, out VkCreateDevice createDevice)
            || !TryGetVulkanExport(library, "vkDestroyDevice", errors, out VkDestroyDevice destroyDevice)
            || !TryGetVulkanExport(library, "vkCreateBuffer", errors, out VkCreateBuffer createBuffer)
            || !TryGetVulkanExport(library, "vkDestroyBuffer", errors, out VkDestroyBuffer destroyBuffer)
            || !TryGetVulkanExport(library, "vkGetBufferMemoryRequirements", errors, out VkGetBufferMemoryRequirements getBufferMemoryRequirements)
            || !TryGetVulkanExport(library, "vkAllocateMemory", errors, out VkAllocateMemory allocateMemory)
            || !TryGetVulkanExport(library, "vkFreeMemory", errors, out VkFreeMemory freeMemory)
            || !TryGetVulkanExport(library, "vkBindBufferMemory", errors, out VkBindBufferMemory bindBufferMemory)
            || !TryGetVulkanExport(library, "vkMapMemory", errors, out VkMapMemory mapMemory)
            || !TryGetVulkanExport(library, "vkUnmapMemory", errors, out VkUnmapMemory unmapMemory))
        {
            return null;
        }

        var appName = Marshal.StringToHGlobalAnsi("Rekall AGE");
        var engineName = Marshal.StringToHGlobalAnsi("Rekall AGE");
        var appInfoPointer = IntPtr.Zero;
        var createInfoPointer = IntPtr.Zero;
        try
        {
            var appInfo = new VkApplicationInfo(VkStructureTypeApplicationInfo, IntPtr.Zero, appName, 1, engineName, 1, VkApiVersion10);
            appInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkApplicationInfo>());
            Marshal.StructureToPtr(appInfo, appInfoPointer, false);
            var createInfo = new VkInstanceCreateInfo(VkStructureTypeInstanceCreateInfo, IntPtr.Zero, 0, appInfoPointer, 0, IntPtr.Zero, 0, IntPtr.Zero);
            createInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkInstanceCreateInfo>());
            Marshal.StructureToPtr(createInfo, createInfoPointer, false);
            var result = createInstance(createInfoPointer, IntPtr.Zero, out var instance);
            if (result != VkSuccess)
            {
                errors.Add($"vkCreateInstance failed with VkResult {result}.");
                return null;
            }

            return new VulkanBufferContext(
                instance,
                destroyInstance,
                enumerateDevices,
                getProperties,
                getQueueFamilies,
                getMemoryProperties,
                createDevice,
                destroyDevice,
                createBuffer,
                destroyBuffer,
                getBufferMemoryRequirements,
                allocateMemory,
                freeMemory,
                bindBufferMemory,
                mapMemory,
                unmapMemory);
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

    private static IReadOnlyList<NativeVulkanCandidateDevice> EnumerateCandidateDevices(VulkanBufferContext context, List<string> errors)
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

            var devices = new List<NativeVulkanCandidateDevice>((int)deviceCount);
            for (var index = 0; index < deviceCount; index++)
            {
                var handle = Marshal.ReadIntPtr(devicesBuffer, index * IntPtr.Size);
                devices.Add(new NativeVulkanCandidateDevice(handle, ReadCandidateDevice(context, handle)));
            }

            return devices;
        }
        finally
        {
            Marshal.FreeHGlobal(devicesBuffer);
        }
    }

    private static RekallAgeVulkanCandidateDevice ReadCandidateDevice(VulkanBufferContext context, IntPtr physicalDevice)
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

    private static IReadOnlyList<RekallAgeVulkanQueueFamilyInfo> ReadQueueFamilies(VulkanBufferContext context, IntPtr physicalDevice)
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
                var properties = Marshal.PtrToStructure<VkQueueFamilyProperties>(IntPtr.Add(buffer, index * size));
                families.Add(new RekallAgeVulkanQueueFamilyInfo((uint)index, DecodeQueueCapabilities(properties.QueueFlags), properties.QueueCount));
            }

            return families;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr CreateLogicalDevice(
        VulkanBufferContext context,
        IntPtr physicalDevice,
        RekallAgeVulkanDeviceSelection selection,
        List<string> errors)
    {
        var priorityPointer = Marshal.AllocHGlobal(sizeof(float));
        var queueCreateInfoPointer = IntPtr.Zero;
        var deviceCreateInfoPointer = IntPtr.Zero;
        try
        {
            Marshal.Copy(BitConverter.GetBytes(1.0f), 0, priorityPointer, sizeof(float));
            var queueCreateInfo = new VkDeviceQueueCreateInfo(VkStructureTypeDeviceQueueCreateInfo, IntPtr.Zero, 0, selection.QueueFamily.Index, 1, priorityPointer);
            queueCreateInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkDeviceQueueCreateInfo>());
            Marshal.StructureToPtr(queueCreateInfo, queueCreateInfoPointer, false);
            var deviceCreateInfo = new VkDeviceCreateInfo(VkStructureTypeDeviceCreateInfo, IntPtr.Zero, 0, 1, queueCreateInfoPointer, 0, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
            deviceCreateInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkDeviceCreateInfo>());
            Marshal.StructureToPtr(deviceCreateInfo, deviceCreateInfoPointer, false);
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

    private static IntPtr CreateBuffer(
        VulkanBufferContext context,
        IntPtr device,
        ulong sizeBytes,
        string usage,
        List<string> errors)
    {
        var createInfo = new VkBufferCreateInfo(
            VkStructureTypeBufferCreateInfo,
            IntPtr.Zero,
            0,
            sizeBytes,
            ParseUsage(usage),
            VkSharingModeExclusive,
            0,
            IntPtr.Zero);
        var createInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkBufferCreateInfo>());
        try
        {
            Marshal.StructureToPtr(createInfo, createInfoPointer, false);
            var result = context.CreateBuffer(device, createInfoPointer, IntPtr.Zero, out var buffer);
            if (result != VkSuccess)
            {
                errors.Add($"vkCreateBuffer failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return buffer;
        }
        finally
        {
            Marshal.FreeHGlobal(createInfoPointer);
        }
    }

    private static VkMemoryRequirements GetMemoryRequirements(VulkanBufferContext context, IntPtr device, IntPtr buffer)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkMemoryRequirements>());
        try
        {
            context.GetBufferMemoryRequirements(device, buffer, pointer);
            return Marshal.PtrToStructure<VkMemoryRequirements>(pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static IReadOnlyList<RekallAgeVulkanMemoryTypeInfo> ReadMemoryTypes(VulkanBufferContext context, IntPtr physicalDevice)
    {
        var buffer = Marshal.AllocHGlobal(PhysicalDeviceMemoryPropertiesBufferSize);
        try
        {
            Marshal.Copy(new byte[PhysicalDeviceMemoryPropertiesBufferSize], 0, buffer, PhysicalDeviceMemoryPropertiesBufferSize);
            context.GetPhysicalDeviceMemoryProperties(physicalDevice, buffer);
            var memoryTypeCount = unchecked((uint)Marshal.ReadInt32(buffer, 0));
            var memoryTypes = new List<RekallAgeVulkanMemoryTypeInfo>((int)memoryTypeCount);
            for (var index = 0; index < memoryTypeCount; index++)
            {
                var offset = MemoryTypesOffset + ((int)index * MemoryTypeSize);
                var flags = unchecked((uint)Marshal.ReadInt32(buffer, offset));
                memoryTypes.Add(new RekallAgeVulkanMemoryTypeInfo((uint)index, DecodeMemoryProperties(flags)));
            }

            return memoryTypes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr AllocateMemory(
        VulkanBufferContext context,
        IntPtr device,
        ulong allocationSize,
        uint memoryTypeIndex,
        List<string> errors)
    {
        var allocateInfo = new VkMemoryAllocateInfo(
            VkStructureTypeMemoryAllocateInfo,
            IntPtr.Zero,
            allocationSize,
            memoryTypeIndex);
        var allocateInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkMemoryAllocateInfo>());
        try
        {
            Marshal.StructureToPtr(allocateInfo, allocateInfoPointer, false);
            var result = context.AllocateMemory(device, allocateInfoPointer, IntPtr.Zero, out var memory);
            if (result != VkSuccess)
            {
                errors.Add($"vkAllocateMemory failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return memory;
        }
        finally
        {
            Marshal.FreeHGlobal(allocateInfoPointer);
        }
    }

    private static int MapAndWrite(
        VulkanBufferContext context,
        IntPtr device,
        IntPtr memory,
        ulong writeSize,
        List<string> errors)
    {
        var result = context.MapMemory(device, memory, 0, writeSize, 0, out var data);
        if (result != VkSuccess)
        {
            errors.Add($"vkMapMemory failed with VkResult {result}.");
            return 0;
        }

        try
        {
            var bytes = Enumerable.Range(0, checked((int)writeSize)).Select(item => (byte)(item + 1)).ToArray();
            Marshal.Copy(bytes, 0, data, bytes.Length);
            return bytes.Length;
        }
        finally
        {
            context.UnmapMemory(device, memory);
        }
    }

    private static RekallAgeVulkanSelectedDevice ToSelectedDevice(RekallAgeVulkanDeviceSelection selection)
    {
        return new RekallAgeVulkanSelectedDevice(selection.Device.Name, selection.Device.DeviceType, selection.Device.ApiVersion, selection.QueueFamily);
    }

    private static RekallAgeVulkanBufferSmokeResult Unavailable(
        string? loaderName,
        RekallAgeVulkanSelectedDevice? selectedDevice,
        ulong sizeBytes,
        string usage,
        uint? memoryTypeIndex,
        IReadOnlyList<string> memoryProperties,
        bool bound,
        bool mapped,
        int bytesWritten,
        IReadOnlyList<string> errors)
    {
        return new RekallAgeVulkanBufferSmokeResult(
            Created: false,
            LoaderName: loaderName,
            SelectedDevice: selectedDevice,
            SizeBytes: sizeBytes,
            Usage: NormalizeUsage(usage),
            MemoryTypeIndex: memoryTypeIndex,
            MemoryProperties: memoryProperties,
            Bound: bound,
            Mapped: mapped,
            BytesWritten: bytesWritten,
            Errors: errors.ToArray());
    }

    private static uint ParseUsage(string usage)
    {
        return NormalizeUsage(usage) switch
        {
            "transfer-src" => VkBufferUsageTransferSrcBit,
            "transfer-dst" => VkBufferUsageTransferDstBit,
            "uniform-buffer" => VkBufferUsageUniformBufferBit,
            "storage-buffer" => VkBufferUsageStorageBufferBit,
            "index-buffer" => VkBufferUsageIndexBufferBit,
            _ => VkBufferUsageVertexBufferBit
        };
    }

    private static string NormalizeUsage(string usage)
    {
        return string.IsNullOrWhiteSpace(usage) ? "vertex-buffer" : usage.Trim().ToLowerInvariant();
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

    private static IReadOnlyList<string> DecodeMemoryProperties(uint flags)
    {
        var properties = new List<string>();
        if ((flags & VkMemoryPropertyDeviceLocalBit) != 0)
        {
            properties.Add("device-local");
        }

        if ((flags & VkMemoryPropertyHostVisibleBit) != 0)
        {
            properties.Add("host-visible");
        }

        if ((flags & VkMemoryPropertyHostCoherentBit) != 0)
        {
            properties.Add("host-coherent");
        }

        if ((flags & VkMemoryPropertyHostCachedBit) != 0)
        {
            properties.Add("host-cached");
        }

        return properties;
    }

    private static bool TryGetVulkanExport<TDelegate>(IntPtr library, string name, List<string> errors, out TDelegate function)
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

    private sealed record NativeVulkanCandidateDevice(IntPtr Handle, RekallAgeVulkanCandidateDevice Device);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateInstance(IntPtr createInfo, IntPtr allocator, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyInstance(IntPtr instance, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkEnumeratePhysicalDevices(IntPtr instance, ref uint physicalDeviceCount, IntPtr physicalDevices);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetPhysicalDeviceProperties(IntPtr physicalDevice, IntPtr properties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetPhysicalDeviceQueueFamilyProperties(IntPtr physicalDevice, ref uint queueFamilyPropertyCount, IntPtr queueFamilyProperties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetPhysicalDeviceMemoryProperties(IntPtr physicalDevice, IntPtr memoryProperties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateDevice(IntPtr physicalDevice, IntPtr createInfo, IntPtr allocator, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyDevice(IntPtr device, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateBuffer(IntPtr device, IntPtr createInfo, IntPtr allocator, out IntPtr buffer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyBuffer(IntPtr device, IntPtr buffer, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetBufferMemoryRequirements(IntPtr device, IntPtr buffer, IntPtr memoryRequirements);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkAllocateMemory(IntPtr device, IntPtr allocateInfo, IntPtr allocator, out IntPtr memory);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkFreeMemory(IntPtr device, IntPtr memory, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkBindBufferMemory(IntPtr device, IntPtr buffer, IntPtr memory, ulong memoryOffset);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkMapMemory(IntPtr device, IntPtr memory, ulong offset, ulong size, uint flags, out IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkUnmapMemory(IntPtr device, IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkApplicationInfo(int SType, IntPtr PNext, IntPtr ApplicationName, uint ApplicationVersion, IntPtr EngineName, uint EngineVersion, uint ApiVersion);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkInstanceCreateInfo(int SType, IntPtr PNext, uint Flags, IntPtr ApplicationInfo, uint EnabledLayerCount, IntPtr EnabledLayerNames, uint EnabledExtensionCount, IntPtr EnabledExtensionNames);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkQueueFamilyProperties(uint QueueFlags, uint QueueCount, uint TimestampValidBits, uint MinImageTransferGranularityWidth, uint MinImageTransferGranularityHeight, uint MinImageTransferGranularityDepth);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkDeviceQueueCreateInfo(int SType, IntPtr PNext, uint Flags, uint QueueFamilyIndex, uint QueueCount, IntPtr QueuePriorities);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkDeviceCreateInfo(int SType, IntPtr PNext, uint Flags, uint QueueCreateInfoCount, IntPtr QueueCreateInfos, uint EnabledLayerCount, IntPtr EnabledLayerNames, uint EnabledExtensionCount, IntPtr EnabledExtensionNames, IntPtr EnabledFeatures);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkBufferCreateInfo(int SType, IntPtr PNext, uint Flags, ulong Size, uint Usage, uint SharingMode, uint QueueFamilyIndexCount, IntPtr QueueFamilyIndices);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkMemoryRequirements(ulong Size, ulong Alignment, uint MemoryTypeBits);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkMemoryAllocateInfo(int SType, IntPtr PNext, ulong AllocationSize, uint MemoryTypeIndex);

    private readonly record struct VulkanBufferContext(
        IntPtr Instance,
        VkDestroyInstance DestroyInstance,
        VkEnumeratePhysicalDevices EnumeratePhysicalDevices,
        VkGetPhysicalDeviceProperties GetPhysicalDeviceProperties,
        VkGetPhysicalDeviceQueueFamilyProperties GetPhysicalDeviceQueueFamilyProperties,
        VkGetPhysicalDeviceMemoryProperties GetPhysicalDeviceMemoryProperties,
        VkCreateDevice CreateDevice,
        VkDestroyDevice DestroyDevice,
        VkCreateBuffer CreateBuffer,
        VkDestroyBuffer DestroyBuffer,
        VkGetBufferMemoryRequirements GetBufferMemoryRequirements,
        VkAllocateMemory AllocateMemory,
        VkFreeMemory FreeMemory,
        VkBindBufferMemory BindBufferMemory,
        VkMapMemory MapMemory,
        VkUnmapMemory UnmapMemory) : IDisposable
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
