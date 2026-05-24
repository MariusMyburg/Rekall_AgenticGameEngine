using System.Runtime.InteropServices;
using System.Text;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeNativeVulkanCommandSubmission : IRekallAgeVulkanCommandSubmission
{
    private const int VkSuccess = 0;
    private const int VkStructureTypeApplicationInfo = 0;
    private const int VkStructureTypeInstanceCreateInfo = 1;
    private const int VkStructureTypeDeviceQueueCreateInfo = 2;
    private const int VkStructureTypeDeviceCreateInfo = 3;
    private const int VkStructureTypeSubmitInfo = 4;
    private const int VkStructureTypeFenceCreateInfo = 8;
    private const int VkStructureTypeCommandPoolCreateInfo = 39;
    private const int VkStructureTypeCommandBufferAllocateInfo = 40;
    private const int VkStructureTypeCommandBufferBeginInfo = 42;
    private const uint VkApiVersion10 = 4194304;
    private const uint VkQueueGraphicsBit = 0x00000001;
    private const uint VkQueueComputeBit = 0x00000002;
    private const uint VkQueueTransferBit = 0x00000004;
    private const uint VkQueueSparseBindingBit = 0x00000008;
    private const uint VkCommandBufferLevelPrimary = 0;
    private const ulong FenceTimeoutNanoseconds = 5_000_000_000;
    private const int PhysicalDevicePropertiesBufferSize = 2048;
    private const int PhysicalDeviceNameOffset = 20;
    private const int PhysicalDeviceNameSize = 256;

    public ValueTask<RekallAgeVulkanCommandSubmissionResult> SubmitEmptyCommandBufferAsync(
        string? preferredDeviceType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (!TryLoadVulkan(errors, out var library, out var loaderName))
        {
            return ValueTask.FromResult(Unavailable(null, null, false, false, false, errors));
        }

        try
        {
            var context = CreateContext(library, errors);
            if (context is null)
            {
                return ValueTask.FromResult(Unavailable(loaderName, null, false, false, false, errors));
            }

            return ValueTask.FromResult(Submit(context.Value, loaderName!, preferredDeviceType, errors));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static RekallAgeVulkanCommandSubmissionResult Submit(
        VulkanSubmissionContext context,
        string loaderName,
        string? preferredDeviceType,
        List<string> errors)
    {
        using (context)
        {
            var nativeDevices = EnumerateCandidateDevices(context, errors);
            var selection = RekallAgeVulkanDeviceSelector.Select(
                nativeDevices.Select(device => device.Device),
                preferredDeviceType);
            if (selection is null)
            {
                errors.Add("No Vulkan physical device with a graphics queue was found.");
                return Unavailable(loaderName, null, false, false, false, errors);
            }

            var nativeDevice = nativeDevices.First(device => device.Device.Name.Equals(selection.Device.Name, StringComparison.Ordinal));
            var logicalDevice = CreateLogicalDevice(context, nativeDevice.Handle, selection, errors);
            if (logicalDevice == IntPtr.Zero)
            {
                return Unavailable(loaderName, ToSelectedDevice(selection), false, false, false, errors);
            }

            var commandPoolCreated = false;
            var commandBufferAllocated = false;
            var fenceSignaled = false;
            try
            {
                context.GetDeviceQueue(logicalDevice, selection.QueueFamily.Index, 0, out var queue);
                if (queue == IntPtr.Zero)
                {
                    errors.Add("vkGetDeviceQueue returned a null graphics queue.");
                    return Unavailable(loaderName, ToSelectedDevice(selection), false, false, false, errors);
                }

                var commandPool = CreateCommandPool(context, logicalDevice, selection.QueueFamily.Index, errors);
                commandPoolCreated = commandPool != IntPtr.Zero;
                if (commandPool == IntPtr.Zero)
                {
                    return Unavailable(loaderName, ToSelectedDevice(selection), commandPoolCreated, false, false, errors);
                }

                try
                {
                    var commandBuffer = AllocateCommandBuffer(context, logicalDevice, commandPool, errors);
                    commandBufferAllocated = commandBuffer != IntPtr.Zero;
                    if (commandBuffer == IntPtr.Zero)
                    {
                        return Unavailable(loaderName, ToSelectedDevice(selection), commandPoolCreated, false, false, errors);
                    }

                    if (!RecordEmptyCommandBuffer(context, commandBuffer, errors))
                    {
                        return Unavailable(loaderName, ToSelectedDevice(selection), commandPoolCreated, commandBufferAllocated, false, errors);
                    }

                    var fence = CreateFence(context, logicalDevice, errors);
                    if (fence == IntPtr.Zero)
                    {
                        return Unavailable(loaderName, ToSelectedDevice(selection), commandPoolCreated, commandBufferAllocated, false, errors);
                    }

                    try
                    {
                        fenceSignaled = SubmitAndWait(context, logicalDevice, queue, commandBuffer, fence, errors);
                        return fenceSignaled
                            ? new RekallAgeVulkanCommandSubmissionResult(
                                Submitted: true,
                                LoaderName: loaderName,
                                SelectedDevice: ToSelectedDevice(selection),
                                CommandPoolCreated: commandPoolCreated,
                                CommandBufferAllocated: commandBufferAllocated,
                                FenceSignaled: true,
                                Errors: errors)
                            : Unavailable(loaderName, ToSelectedDevice(selection), commandPoolCreated, commandBufferAllocated, false, errors);
                    }
                    finally
                    {
                        context.DestroyFence(logicalDevice, fence, IntPtr.Zero);
                    }
                }
                finally
                {
                    context.DestroyCommandPool(logicalDevice, commandPool, IntPtr.Zero);
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

    private static VulkanSubmissionContext? CreateContext(IntPtr library, List<string> errors)
    {
        if (!TryGetVulkanExport(library, "vkCreateInstance", errors, out VkCreateInstance createInstance)
            || !TryGetVulkanExport(library, "vkDestroyInstance", errors, out VkDestroyInstance destroyInstance)
            || !TryGetVulkanExport(library, "vkEnumeratePhysicalDevices", errors, out VkEnumeratePhysicalDevices enumerateDevices)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceProperties", errors, out VkGetPhysicalDeviceProperties getProperties)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceQueueFamilyProperties", errors, out VkGetPhysicalDeviceQueueFamilyProperties getQueueFamilies)
            || !TryGetVulkanExport(library, "vkCreateDevice", errors, out VkCreateDevice createDevice)
            || !TryGetVulkanExport(library, "vkDestroyDevice", errors, out VkDestroyDevice destroyDevice)
            || !TryGetVulkanExport(library, "vkGetDeviceQueue", errors, out VkGetDeviceQueue getDeviceQueue)
            || !TryGetVulkanExport(library, "vkCreateCommandPool", errors, out VkCreateCommandPool createCommandPool)
            || !TryGetVulkanExport(library, "vkDestroyCommandPool", errors, out VkDestroyCommandPool destroyCommandPool)
            || !TryGetVulkanExport(library, "vkAllocateCommandBuffers", errors, out VkAllocateCommandBuffers allocateCommandBuffers)
            || !TryGetVulkanExport(library, "vkBeginCommandBuffer", errors, out VkBeginCommandBuffer beginCommandBuffer)
            || !TryGetVulkanExport(library, "vkEndCommandBuffer", errors, out VkEndCommandBuffer endCommandBuffer)
            || !TryGetVulkanExport(library, "vkCreateFence", errors, out VkCreateFence createFence)
            || !TryGetVulkanExport(library, "vkDestroyFence", errors, out VkDestroyFence destroyFence)
            || !TryGetVulkanExport(library, "vkQueueSubmit", errors, out VkQueueSubmit queueSubmit)
            || !TryGetVulkanExport(library, "vkWaitForFences", errors, out VkWaitForFences waitForFences))
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

            return new VulkanSubmissionContext(
                instance,
                destroyInstance,
                enumerateDevices,
                getProperties,
                getQueueFamilies,
                createDevice,
                destroyDevice,
                getDeviceQueue,
                createCommandPool,
                destroyCommandPool,
                allocateCommandBuffers,
                beginCommandBuffer,
                endCommandBuffer,
                createFence,
                destroyFence,
                queueSubmit,
                waitForFences);
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

    private static IReadOnlyList<NativeVulkanCandidateDevice> EnumerateCandidateDevices(
        VulkanSubmissionContext context,
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

    private static RekallAgeVulkanCandidateDevice ReadCandidateDevice(
        VulkanSubmissionContext context,
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
        VulkanSubmissionContext context,
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
                var properties = Marshal.PtrToStructure<VkQueueFamilyProperties>(IntPtr.Add(buffer, index * size));
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
        VulkanSubmissionContext context,
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

    private static IntPtr CreateCommandPool(
        VulkanSubmissionContext context,
        IntPtr device,
        uint queueFamilyIndex,
        List<string> errors)
    {
        var createInfo = new VkCommandPoolCreateInfo(VkStructureTypeCommandPoolCreateInfo, IntPtr.Zero, 0, queueFamilyIndex);
        var createInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkCommandPoolCreateInfo>());
        try
        {
            Marshal.StructureToPtr(createInfo, createInfoPointer, false);
            var result = context.CreateCommandPool(device, createInfoPointer, IntPtr.Zero, out var commandPool);
            if (result != VkSuccess)
            {
                errors.Add($"vkCreateCommandPool failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return commandPool;
        }
        finally
        {
            Marshal.FreeHGlobal(createInfoPointer);
        }
    }

    private static IntPtr AllocateCommandBuffer(
        VulkanSubmissionContext context,
        IntPtr device,
        IntPtr commandPool,
        List<string> errors)
    {
        var allocateInfo = new VkCommandBufferAllocateInfo(
            VkStructureTypeCommandBufferAllocateInfo,
            IntPtr.Zero,
            commandPool,
            VkCommandBufferLevelPrimary,
            1);
        var allocateInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkCommandBufferAllocateInfo>());
        var commandBufferPointer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.StructureToPtr(allocateInfo, allocateInfoPointer, false);
            var result = context.AllocateCommandBuffers(device, allocateInfoPointer, commandBufferPointer);
            if (result != VkSuccess)
            {
                errors.Add($"vkAllocateCommandBuffers failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return Marshal.ReadIntPtr(commandBufferPointer);
        }
        finally
        {
            Marshal.FreeHGlobal(commandBufferPointer);
            Marshal.FreeHGlobal(allocateInfoPointer);
        }
    }

    private static bool RecordEmptyCommandBuffer(
        VulkanSubmissionContext context,
        IntPtr commandBuffer,
        List<string> errors)
    {
        var beginInfo = new VkCommandBufferBeginInfo(VkStructureTypeCommandBufferBeginInfo, IntPtr.Zero, 0, IntPtr.Zero);
        var beginInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkCommandBufferBeginInfo>());
        try
        {
            Marshal.StructureToPtr(beginInfo, beginInfoPointer, false);
            var result = context.BeginCommandBuffer(commandBuffer, beginInfoPointer);
            if (result != VkSuccess)
            {
                errors.Add($"vkBeginCommandBuffer failed with VkResult {result}.");
                return false;
            }

            result = context.EndCommandBuffer(commandBuffer);
            if (result != VkSuccess)
            {
                errors.Add($"vkEndCommandBuffer failed with VkResult {result}.");
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(beginInfoPointer);
        }
    }

    private static IntPtr CreateFence(VulkanSubmissionContext context, IntPtr device, List<string> errors)
    {
        var createInfo = new VkFenceCreateInfo(VkStructureTypeFenceCreateInfo, IntPtr.Zero, 0);
        var createInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkFenceCreateInfo>());
        try
        {
            Marshal.StructureToPtr(createInfo, createInfoPointer, false);
            var result = context.CreateFence(device, createInfoPointer, IntPtr.Zero, out var fence);
            if (result != VkSuccess)
            {
                errors.Add($"vkCreateFence failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return fence;
        }
        finally
        {
            Marshal.FreeHGlobal(createInfoPointer);
        }
    }

    private static bool SubmitAndWait(
        VulkanSubmissionContext context,
        IntPtr device,
        IntPtr queue,
        IntPtr commandBuffer,
        IntPtr fence,
        List<string> errors)
    {
        var commandBufferPointer = Marshal.AllocHGlobal(IntPtr.Size);
        var submitInfoPointer = IntPtr.Zero;
        var fencePointer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(commandBufferPointer, commandBuffer);
            Marshal.WriteIntPtr(fencePointer, fence);
            var submitInfo = new VkSubmitInfo(
                VkStructureTypeSubmitInfo,
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                1,
                commandBufferPointer,
                0,
                IntPtr.Zero);
            submitInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkSubmitInfo>());
            Marshal.StructureToPtr(submitInfo, submitInfoPointer, false);

            var result = context.QueueSubmit(queue, 1, submitInfoPointer, fence);
            if (result != VkSuccess)
            {
                errors.Add($"vkQueueSubmit failed with VkResult {result}.");
                return false;
            }

            result = context.WaitForFences(device, 1, fencePointer, 1, FenceTimeoutNanoseconds);
            if (result != VkSuccess)
            {
                errors.Add($"vkWaitForFences failed with VkResult {result}.");
                return false;
            }

            return true;
        }
        finally
        {
            if (submitInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(submitInfoPointer);
            }

            Marshal.FreeHGlobal(fencePointer);
            Marshal.FreeHGlobal(commandBufferPointer);
        }
    }

    private static RekallAgeVulkanSelectedDevice ToSelectedDevice(RekallAgeVulkanDeviceSelection selection)
    {
        return new RekallAgeVulkanSelectedDevice(
            selection.Device.Name,
            selection.Device.DeviceType,
            selection.Device.ApiVersion,
            selection.QueueFamily);
    }

    private static RekallAgeVulkanCommandSubmissionResult Unavailable(
        string? loaderName,
        RekallAgeVulkanSelectedDevice? selectedDevice,
        bool commandPoolCreated,
        bool commandBufferAllocated,
        bool fenceSignaled,
        IReadOnlyList<string> errors)
    {
        return new RekallAgeVulkanCommandSubmissionResult(
            Submitted: false,
            LoaderName: loaderName,
            SelectedDevice: selectedDevice,
            CommandPoolCreated: commandPoolCreated,
            CommandBufferAllocated: commandBufferAllocated,
            FenceSignaled: fenceSignaled,
            Errors: errors.ToArray());
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

    private sealed record NativeVulkanCandidateDevice(
        IntPtr Handle,
        RekallAgeVulkanCandidateDevice Device);

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
    private delegate int VkCreateDevice(IntPtr physicalDevice, IntPtr createInfo, IntPtr allocator, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyDevice(IntPtr device, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetDeviceQueue(IntPtr device, uint queueFamilyIndex, uint queueIndex, out IntPtr queue);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateCommandPool(IntPtr device, IntPtr createInfo, IntPtr allocator, out IntPtr commandPool);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyCommandPool(IntPtr device, IntPtr commandPool, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkAllocateCommandBuffers(IntPtr device, IntPtr allocateInfo, IntPtr commandBuffers);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkBeginCommandBuffer(IntPtr commandBuffer, IntPtr beginInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkEndCommandBuffer(IntPtr commandBuffer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateFence(IntPtr device, IntPtr createInfo, IntPtr allocator, out IntPtr fence);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyFence(IntPtr device, IntPtr fence, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkQueueSubmit(IntPtr queue, uint submitCount, IntPtr submits, IntPtr fence);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkWaitForFences(IntPtr device, uint fenceCount, IntPtr fences, uint waitAll, ulong timeout);

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
    private readonly record struct VkCommandPoolCreateInfo(int SType, IntPtr PNext, uint Flags, uint QueueFamilyIndex);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkCommandBufferAllocateInfo(int SType, IntPtr PNext, IntPtr CommandPool, uint Level, uint CommandBufferCount);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkCommandBufferBeginInfo(int SType, IntPtr PNext, uint Flags, IntPtr InheritanceInfo);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkFenceCreateInfo(int SType, IntPtr PNext, uint Flags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkSubmitInfo(int SType, IntPtr PNext, uint WaitSemaphoreCount, IntPtr WaitSemaphores, IntPtr WaitDstStageMask, uint CommandBufferCount, IntPtr CommandBuffers, uint SignalSemaphoreCount, IntPtr SignalSemaphores);

    private readonly record struct VulkanSubmissionContext(
        IntPtr Instance,
        VkDestroyInstance DestroyInstance,
        VkEnumeratePhysicalDevices EnumeratePhysicalDevices,
        VkGetPhysicalDeviceProperties GetPhysicalDeviceProperties,
        VkGetPhysicalDeviceQueueFamilyProperties GetPhysicalDeviceQueueFamilyProperties,
        VkCreateDevice CreateDevice,
        VkDestroyDevice DestroyDevice,
        VkGetDeviceQueue GetDeviceQueue,
        VkCreateCommandPool CreateCommandPool,
        VkDestroyCommandPool DestroyCommandPool,
        VkAllocateCommandBuffers AllocateCommandBuffers,
        VkBeginCommandBuffer BeginCommandBuffer,
        VkEndCommandBuffer EndCommandBuffer,
        VkCreateFence CreateFence,
        VkDestroyFence DestroyFence,
        VkQueueSubmit QueueSubmit,
        VkWaitForFences WaitForFences) : IDisposable
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
