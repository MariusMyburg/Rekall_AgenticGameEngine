using System.Runtime.InteropServices;
using System.Text;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeNativeVulkanRenderTargetSmoke : IRekallAgeVulkanRenderTargetSmoke
{
    private const int VkSuccess = 0;
    private const int VkStructureTypeApplicationInfo = 0;
    private const int VkStructureTypeInstanceCreateInfo = 1;
    private const int VkStructureTypeDeviceQueueCreateInfo = 2;
    private const int VkStructureTypeDeviceCreateInfo = 3;
    private const int VkStructureTypeMemoryAllocateInfo = 5;
    private const int VkStructureTypeFramebufferCreateInfo = 37;
    private const int VkStructureTypeRenderPassCreateInfo = 38;
    private const int VkStructureTypeImageCreateInfo = 14;
    private const int VkStructureTypeImageViewCreateInfo = 15;
    private const uint VkApiVersion10 = 4194304;
    private const uint VkImageType2D = 1;
    private const uint VkImageViewType2D = 1;
    private const uint VkImageTilingOptimal = 0;
    private const uint VkSharingModeExclusive = 0;
    private const uint VkImageLayoutUndefined = 0;
    private const uint VkImageLayoutColorAttachmentOptimal = 2;
    private const uint VkSampleCount1Bit = 1;
    private const uint VkFormatR8G8B8A8Unorm = 37;
    private const uint VkFormatB8G8R8A8Unorm = 44;
    private const uint VkImageUsageColorAttachmentBit = 0x00000010;
    private const uint VkImageAspectColorBit = 0x00000001;
    private const uint VkAttachmentLoadOpClear = 1;
    private const uint VkAttachmentStoreOpStore = 0;
    private const uint VkAttachmentLoadOpDontCare = 2;
    private const uint VkAttachmentStoreOpDontCare = 1;
    private const uint VkPipelineBindPointGraphics = 0;
    private const uint VkQueueGraphicsBit = 0x00000001;
    private const uint VkQueueComputeBit = 0x00000002;
    private const uint VkQueueTransferBit = 0x00000004;
    private const uint VkQueueSparseBindingBit = 0x00000008;
    private const int PhysicalDevicePropertiesBufferSize = 2048;
    private const int PhysicalDeviceMemoryPropertiesBufferSize = 4096;
    private const int PhysicalDeviceNameOffset = 20;
    private const int PhysicalDeviceNameSize = 256;
    private const int MemoryTypesOffset = 4;
    private const int MemoryTypeSize = 8;

    public ValueTask<RekallAgeVulkanRenderTargetSmokeResult> CreateRenderTargetAsync(
        uint width,
        uint height,
        string format,
        string? preferredDeviceType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (width == 0 || height == 0)
        {
            errors.Add("Vulkan render target width and height must be greater than zero.");
            return ValueTask.FromResult(Unavailable(null, null, width, height, format, false, false, false, false, errors));
        }

        if (!TryLoadVulkan(errors, out var library, out var loaderName))
        {
            return ValueTask.FromResult(Unavailable(null, null, width, height, format, false, false, false, false, errors));
        }

        try
        {
            var context = CreateContext(library, errors);
            if (context is null)
            {
                return ValueTask.FromResult(Unavailable(loaderName, null, width, height, format, false, false, false, false, errors));
            }

            return ValueTask.FromResult(CreateRenderTarget(context.Value, loaderName!, width, height, format, preferredDeviceType, errors));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static RekallAgeVulkanRenderTargetSmokeResult CreateRenderTarget(
        VulkanRenderTargetContext context,
        string loaderName,
        uint width,
        uint height,
        string format,
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
                return Unavailable(loaderName, null, width, height, format, false, false, false, false, errors);
            }

            var nativeDevice = nativeDevices.First(device => device.Device.Name.Equals(selection.Device.Name, StringComparison.Ordinal));
            var device = CreateLogicalDevice(context, nativeDevice.Handle, selection, errors);
            if (device == IntPtr.Zero)
            {
                return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, false, false, false, false, errors);
            }

            try
            {
                var image = CreateImage(context, device, width, height, format, errors);
                if (image == IntPtr.Zero)
                {
                    return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, false, false, false, false, errors);
                }

                try
                {
                    var memory = BindImageMemory(context, nativeDevice.Handle, device, image, errors, out _);
                    if (memory == IntPtr.Zero)
                    {
                        return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, true, false, false, false, errors);
                    }

                    try
                    {
                        var imageView = CreateImageView(context, device, image, format, errors);
                        if (imageView == IntPtr.Zero)
                        {
                            return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, true, false, false, false, errors);
                        }

                        try
                        {
                            var renderPass = CreateRenderPass(context, device, format, errors);
                            if (renderPass == IntPtr.Zero)
                            {
                                return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, true, true, false, false, errors);
                            }

                            try
                            {
                                var framebuffer = CreateFramebuffer(context, device, renderPass, imageView, width, height, errors);
                                if (framebuffer == IntPtr.Zero)
                                {
                                    return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, true, true, true, false, errors);
                                }

                                try
                                {
                                    return new RekallAgeVulkanRenderTargetSmokeResult(
                                        Created: true,
                                        LoaderName: loaderName,
                                        SelectedDevice: ToSelectedDevice(selection),
                                        Width: width,
                                        Height: height,
                                        Format: NormalizeFormat(format),
                                        ImageCreated: true,
                                        ImageViewCreated: true,
                                        RenderPassCreated: true,
                                        FramebufferCreated: true,
                                        Errors: errors);
                                }
                                finally
                                {
                                    context.DestroyFramebuffer(device, framebuffer, IntPtr.Zero);
                                }
                            }
                            finally
                            {
                                context.DestroyRenderPass(device, renderPass, IntPtr.Zero);
                            }
                        }
                        finally
                        {
                            context.DestroyImageView(device, imageView, IntPtr.Zero);
                        }
                    }
                    finally
                    {
                        context.FreeMemory(device, memory, IntPtr.Zero);
                    }
                }
                finally
                {
                    context.DestroyImage(device, image, IntPtr.Zero);
                }
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

    private static VulkanRenderTargetContext? CreateContext(IntPtr library, List<string> errors)
    {
        if (!TryGetVulkanExport(library, "vkCreateInstance", errors, out VkCreateInstance createInstance)
            || !TryGetVulkanExport(library, "vkDestroyInstance", errors, out VkDestroyInstance destroyInstance)
            || !TryGetVulkanExport(library, "vkEnumeratePhysicalDevices", errors, out VkEnumeratePhysicalDevices enumerateDevices)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceProperties", errors, out VkGetPhysicalDeviceProperties getProperties)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceQueueFamilyProperties", errors, out VkGetPhysicalDeviceQueueFamilyProperties getQueueFamilies)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceMemoryProperties", errors, out VkGetPhysicalDeviceMemoryProperties getMemoryProperties)
            || !TryGetVulkanExport(library, "vkCreateDevice", errors, out VkCreateDevice createDevice)
            || !TryGetVulkanExport(library, "vkDestroyDevice", errors, out VkDestroyDevice destroyDevice)
            || !TryGetVulkanExport(library, "vkCreateImage", errors, out VkCreateImage createImage)
            || !TryGetVulkanExport(library, "vkDestroyImage", errors, out VkDestroyImage destroyImage)
            || !TryGetVulkanExport(library, "vkGetImageMemoryRequirements", errors, out VkGetImageMemoryRequirements getImageMemoryRequirements)
            || !TryGetVulkanExport(library, "vkAllocateMemory", errors, out VkAllocateMemory allocateMemory)
            || !TryGetVulkanExport(library, "vkFreeMemory", errors, out VkFreeMemory freeMemory)
            || !TryGetVulkanExport(library, "vkBindImageMemory", errors, out VkBindImageMemory bindImageMemory)
            || !TryGetVulkanExport(library, "vkCreateImageView", errors, out VkCreateImageView createImageView)
            || !TryGetVulkanExport(library, "vkDestroyImageView", errors, out VkDestroyImageView destroyImageView)
            || !TryGetVulkanExport(library, "vkCreateRenderPass", errors, out VkCreateRenderPass createRenderPass)
            || !TryGetVulkanExport(library, "vkDestroyRenderPass", errors, out VkDestroyRenderPass destroyRenderPass)
            || !TryGetVulkanExport(library, "vkCreateFramebuffer", errors, out VkCreateFramebuffer createFramebuffer)
            || !TryGetVulkanExport(library, "vkDestroyFramebuffer", errors, out VkDestroyFramebuffer destroyFramebuffer))
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

            return new VulkanRenderTargetContext(
                instance,
                destroyInstance,
                enumerateDevices,
                getProperties,
                getQueueFamilies,
                getMemoryProperties,
                createDevice,
                destroyDevice,
                createImage,
                destroyImage,
                getImageMemoryRequirements,
                allocateMemory,
                freeMemory,
                bindImageMemory,
                createImageView,
                destroyImageView,
                createRenderPass,
                destroyRenderPass,
                createFramebuffer,
                destroyFramebuffer);
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

    private static IReadOnlyList<NativeVulkanCandidateDevice> EnumerateCandidateDevices(VulkanRenderTargetContext context, List<string> errors)
    {
        var count = 0u;
        var result = context.EnumeratePhysicalDevices(context.Instance, ref count, IntPtr.Zero);
        if (result != VkSuccess)
        {
            errors.Add($"vkEnumeratePhysicalDevices count query failed with VkResult {result}.");
            return [];
        }

        if (count == 0)
        {
            return [];
        }

        var devicesBuffer = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
        try
        {
            result = context.EnumeratePhysicalDevices(context.Instance, ref count, devicesBuffer);
            if (result != VkSuccess)
            {
                errors.Add($"vkEnumeratePhysicalDevices enumeration failed with VkResult {result}.");
                return [];
            }

            var devices = new List<NativeVulkanCandidateDevice>((int)count);
            for (var index = 0; index < count; index++)
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

    private static RekallAgeVulkanCandidateDevice ReadCandidateDevice(VulkanRenderTargetContext context, IntPtr physicalDevice)
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

    private static IReadOnlyList<RekallAgeVulkanQueueFamilyInfo> ReadQueueFamilies(VulkanRenderTargetContext context, IntPtr physicalDevice)
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

    private static IntPtr CreateLogicalDevice(VulkanRenderTargetContext context, IntPtr physicalDevice, RekallAgeVulkanDeviceSelection selection, List<string> errors)
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

    private static IntPtr CreateImage(VulkanRenderTargetContext context, IntPtr device, uint width, uint height, string format, List<string> errors)
    {
        var createInfo = new VkImageCreateInfo(
            VkStructureTypeImageCreateInfo,
            IntPtr.Zero,
            0,
            VkImageType2D,
            ParseFormat(format),
            new VkExtent3D(width, height, 1),
            1,
            1,
            VkSampleCount1Bit,
            VkImageTilingOptimal,
            VkImageUsageColorAttachmentBit,
            VkSharingModeExclusive,
            0,
            IntPtr.Zero,
            VkImageLayoutUndefined);
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkImageCreateInfo>());
        try
        {
            Marshal.StructureToPtr(createInfo, pointer, false);
            var result = context.CreateImage(device, pointer, IntPtr.Zero, out var image);
            if (result != VkSuccess)
            {
                errors.Add($"vkCreateImage failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return image;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static IntPtr BindImageMemory(VulkanRenderTargetContext context, IntPtr physicalDevice, IntPtr device, IntPtr image, List<string> errors, out uint? memoryTypeIndex)
    {
        memoryTypeIndex = null;
        var requirements = GetMemoryRequirements(context, device, image);
        var memoryTypes = ReadMemoryTypes(context, physicalDevice);
        var selectedMemoryType = RekallAgeVulkanMemoryTypeSelector.Select(memoryTypes, requirements.MemoryTypeBits, ["device-local"]);
        if (selectedMemoryType is null)
        {
            errors.Add("No compatible Vulkan image memory type was found.");
            return IntPtr.Zero;
        }

        memoryTypeIndex = selectedMemoryType.Value;
        var memory = AllocateMemory(context, device, requirements.Size, selectedMemoryType.Value, errors);
        if (memory == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var bindResult = context.BindImageMemory(device, image, memory, 0);
        if (bindResult != VkSuccess)
        {
            context.FreeMemory(device, memory, IntPtr.Zero);
            errors.Add($"vkBindImageMemory failed with VkResult {bindResult}.");
            return IntPtr.Zero;
        }

        return memory;
    }

    private static VkMemoryRequirements GetMemoryRequirements(VulkanRenderTargetContext context, IntPtr device, IntPtr image)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkMemoryRequirements>());
        try
        {
            context.GetImageMemoryRequirements(device, image, pointer);
            return Marshal.PtrToStructure<VkMemoryRequirements>(pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static IReadOnlyList<RekallAgeVulkanMemoryTypeInfo> ReadMemoryTypes(VulkanRenderTargetContext context, IntPtr physicalDevice)
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
                memoryTypes.Add(new RekallAgeVulkanMemoryTypeInfo((uint)index, RekallAgeVulkanMemoryPropertyNames.FromVulkanFlags(flags)));
            }

            return memoryTypes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr AllocateMemory(VulkanRenderTargetContext context, IntPtr device, ulong allocationSize, uint memoryTypeIndex, List<string> errors)
    {
        var allocateInfo = new VkMemoryAllocateInfo(VkStructureTypeMemoryAllocateInfo, IntPtr.Zero, allocationSize, memoryTypeIndex);
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkMemoryAllocateInfo>());
        try
        {
            Marshal.StructureToPtr(allocateInfo, pointer, false);
            var result = context.AllocateMemory(device, pointer, IntPtr.Zero, out var memory);
            if (result != VkSuccess)
            {
                errors.Add($"vkAllocateMemory failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return memory;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static IntPtr CreateImageView(VulkanRenderTargetContext context, IntPtr device, IntPtr image, string format, List<string> errors)
    {
        var createInfo = new VkImageViewCreateInfo(
            VkStructureTypeImageViewCreateInfo,
            IntPtr.Zero,
            0,
            image,
            VkImageViewType2D,
            ParseFormat(format),
            new VkComponentMapping(0, 0, 0, 0),
            new VkImageSubresourceRange(VkImageAspectColorBit, 0, 1, 0, 1));
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkImageViewCreateInfo>());
        try
        {
            Marshal.StructureToPtr(createInfo, pointer, false);
            var result = context.CreateImageView(device, pointer, IntPtr.Zero, out var view);
            if (result != VkSuccess)
            {
                errors.Add($"vkCreateImageView failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return view;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static IntPtr CreateRenderPass(VulkanRenderTargetContext context, IntPtr device, string format, List<string> errors)
    {
        var attachment = new VkAttachmentDescription(
            0,
            ParseFormat(format),
            VkSampleCount1Bit,
            VkAttachmentLoadOpClear,
            VkAttachmentStoreOpStore,
            VkAttachmentLoadOpDontCare,
            VkAttachmentStoreOpDontCare,
            VkImageLayoutUndefined,
            VkImageLayoutColorAttachmentOptimal);
        var attachmentPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkAttachmentDescription>());
        var colorRef = new VkAttachmentReference(0, VkImageLayoutColorAttachmentOptimal);
        var colorRefPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkAttachmentReference>());
        var subpassPointer = IntPtr.Zero;
        var createInfoPointer = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(attachment, attachmentPointer, false);
            Marshal.StructureToPtr(colorRef, colorRefPointer, false);
            var subpass = new VkSubpassDescription(
                0,
                VkPipelineBindPointGraphics,
                0,
                IntPtr.Zero,
                1,
                colorRefPointer,
                IntPtr.Zero,
                IntPtr.Zero,
                0,
                IntPtr.Zero);
            subpassPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkSubpassDescription>());
            Marshal.StructureToPtr(subpass, subpassPointer, false);
            var createInfo = new VkRenderPassCreateInfo(
                VkStructureTypeRenderPassCreateInfo,
                IntPtr.Zero,
                0,
                1,
                attachmentPointer,
                1,
                subpassPointer,
                0,
                IntPtr.Zero);
            createInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkRenderPassCreateInfo>());
            Marshal.StructureToPtr(createInfo, createInfoPointer, false);
            var result = context.CreateRenderPass(device, createInfoPointer, IntPtr.Zero, out var renderPass);
            if (result != VkSuccess)
            {
                errors.Add($"vkCreateRenderPass failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return renderPass;
        }
        finally
        {
            if (createInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(createInfoPointer);
            }

            if (subpassPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(subpassPointer);
            }

            Marshal.FreeHGlobal(colorRefPointer);
            Marshal.FreeHGlobal(attachmentPointer);
        }
    }

    private static IntPtr CreateFramebuffer(VulkanRenderTargetContext context, IntPtr device, IntPtr renderPass, IntPtr imageView, uint width, uint height, List<string> errors)
    {
        var attachmentsPointer = Marshal.AllocHGlobal(IntPtr.Size);
        var createInfoPointer = IntPtr.Zero;
        try
        {
            Marshal.WriteIntPtr(attachmentsPointer, imageView);
            var createInfo = new VkFramebufferCreateInfo(
                VkStructureTypeFramebufferCreateInfo,
                IntPtr.Zero,
                0,
                renderPass,
                1,
                attachmentsPointer,
                width,
                height,
                1);
            createInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkFramebufferCreateInfo>());
            Marshal.StructureToPtr(createInfo, createInfoPointer, false);
            var result = context.CreateFramebuffer(device, createInfoPointer, IntPtr.Zero, out var framebuffer);
            if (result != VkSuccess)
            {
                errors.Add($"vkCreateFramebuffer failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return framebuffer;
        }
        finally
        {
            if (createInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(createInfoPointer);
            }

            Marshal.FreeHGlobal(attachmentsPointer);
        }
    }

    private static RekallAgeVulkanRenderTargetSmokeResult Unavailable(
        string? loaderName,
        RekallAgeVulkanSelectedDevice? selectedDevice,
        uint width,
        uint height,
        string format,
        bool imageCreated,
        bool imageViewCreated,
        bool renderPassCreated,
        bool framebufferCreated,
        IReadOnlyList<string> errors)
    {
        return new RekallAgeVulkanRenderTargetSmokeResult(
            Created: false,
            LoaderName: loaderName,
            SelectedDevice: selectedDevice,
            Width: width,
            Height: height,
            Format: NormalizeFormat(format),
            ImageCreated: imageCreated,
            ImageViewCreated: imageViewCreated,
            RenderPassCreated: renderPassCreated,
            FramebufferCreated: framebufferCreated,
            Errors: errors.ToArray());
    }

    private static RekallAgeVulkanSelectedDevice ToSelectedDevice(RekallAgeVulkanDeviceSelection selection)
    {
        return new RekallAgeVulkanSelectedDevice(selection.Device.Name, selection.Device.DeviceType, selection.Device.ApiVersion, selection.QueueFamily);
    }

    private static uint ParseFormat(string format)
    {
        return NormalizeFormat(format) switch
        {
            "B8G8R8A8_UNorm" => VkFormatB8G8R8A8Unorm,
            _ => VkFormatR8G8B8A8Unorm
        };
    }

    private static string NormalizeFormat(string format)
    {
        return string.IsNullOrWhiteSpace(format) ? "R8G8B8A8_UNorm" : format.Trim();
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
    private delegate int VkCreateImage(IntPtr device, IntPtr createInfo, IntPtr allocator, out IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyImage(IntPtr device, IntPtr image, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetImageMemoryRequirements(IntPtr device, IntPtr image, IntPtr memoryRequirements);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkAllocateMemory(IntPtr device, IntPtr allocateInfo, IntPtr allocator, out IntPtr memory);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkFreeMemory(IntPtr device, IntPtr memory, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkBindImageMemory(IntPtr device, IntPtr image, IntPtr memory, ulong memoryOffset);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateImageView(IntPtr device, IntPtr createInfo, IntPtr allocator, out IntPtr imageView);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyImageView(IntPtr device, IntPtr imageView, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateRenderPass(IntPtr device, IntPtr createInfo, IntPtr allocator, out IntPtr renderPass);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyRenderPass(IntPtr device, IntPtr renderPass, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateFramebuffer(IntPtr device, IntPtr createInfo, IntPtr allocator, out IntPtr framebuffer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyFramebuffer(IntPtr device, IntPtr framebuffer, IntPtr allocator);

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
    private readonly record struct VkExtent3D(uint Width, uint Height, uint Depth);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkImageCreateInfo(int SType, IntPtr PNext, uint Flags, uint ImageType, uint Format, VkExtent3D Extent, uint MipLevels, uint ArrayLayers, uint Samples, uint Tiling, uint Usage, uint SharingMode, uint QueueFamilyIndexCount, IntPtr QueueFamilyIndices, uint InitialLayout);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkMemoryRequirements(ulong Size, ulong Alignment, uint MemoryTypeBits);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkMemoryAllocateInfo(int SType, IntPtr PNext, ulong AllocationSize, uint MemoryTypeIndex);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkComponentMapping(uint R, uint G, uint B, uint A);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkImageSubresourceRange(uint AspectMask, uint BaseMipLevel, uint LevelCount, uint BaseArrayLayer, uint LayerCount);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkImageViewCreateInfo(int SType, IntPtr PNext, uint Flags, IntPtr Image, uint ViewType, uint Format, VkComponentMapping Components, VkImageSubresourceRange SubresourceRange);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkAttachmentDescription(uint Flags, uint Format, uint Samples, uint LoadOp, uint StoreOp, uint StencilLoadOp, uint StencilStoreOp, uint InitialLayout, uint FinalLayout);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkAttachmentReference(uint Attachment, uint Layout);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkSubpassDescription(uint Flags, uint PipelineBindPoint, uint InputAttachmentCount, IntPtr InputAttachments, uint ColorAttachmentCount, IntPtr ColorAttachments, IntPtr ResolveAttachments, IntPtr DepthStencilAttachment, uint PreserveAttachmentCount, IntPtr PreserveAttachments);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkRenderPassCreateInfo(int SType, IntPtr PNext, uint Flags, uint AttachmentCount, IntPtr Attachments, uint SubpassCount, IntPtr Subpasses, uint DependencyCount, IntPtr Dependencies);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkFramebufferCreateInfo(int SType, IntPtr PNext, uint Flags, IntPtr RenderPass, uint AttachmentCount, IntPtr Attachments, uint Width, uint Height, uint Layers);

    private readonly record struct VulkanRenderTargetContext(
        IntPtr Instance,
        VkDestroyInstance DestroyInstance,
        VkEnumeratePhysicalDevices EnumeratePhysicalDevices,
        VkGetPhysicalDeviceProperties GetPhysicalDeviceProperties,
        VkGetPhysicalDeviceQueueFamilyProperties GetPhysicalDeviceQueueFamilyProperties,
        VkGetPhysicalDeviceMemoryProperties GetPhysicalDeviceMemoryProperties,
        VkCreateDevice CreateDevice,
        VkDestroyDevice DestroyDevice,
        VkCreateImage CreateImage,
        VkDestroyImage DestroyImage,
        VkGetImageMemoryRequirements GetImageMemoryRequirements,
        VkAllocateMemory AllocateMemory,
        VkFreeMemory FreeMemory,
        VkBindImageMemory BindImageMemory,
        VkCreateImageView CreateImageView,
        VkDestroyImageView DestroyImageView,
        VkCreateRenderPass CreateRenderPass,
        VkDestroyRenderPass DestroyRenderPass,
        VkCreateFramebuffer CreateFramebuffer,
        VkDestroyFramebuffer DestroyFramebuffer) : IDisposable
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
