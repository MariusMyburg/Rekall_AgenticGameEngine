using System.Runtime.InteropServices;
using System.Text;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeNativeVulkanRenderPassSubmission : IRekallAgeVulkanRenderPassSubmission, IRekallAgeVulkanRenderPassReadback, IRekallAgeVulkanRenderPassCapture
{
    private const int VkSuccess = 0;
    private const int VkStructureTypeApplicationInfo = 0;
    private const int VkStructureTypeInstanceCreateInfo = 1;
    private const int VkStructureTypeDeviceQueueCreateInfo = 2;
    private const int VkStructureTypeDeviceCreateInfo = 3;
    private const int VkStructureTypeSubmitInfo = 4;
    private const int VkStructureTypeMemoryAllocateInfo = 5;
    private const int VkStructureTypeFenceCreateInfo = 8;
    private const int VkStructureTypeBufferCreateInfo = 12;
    private const int VkStructureTypeFramebufferCreateInfo = 37;
    private const int VkStructureTypeRenderPassCreateInfo = 38;
    private const int VkStructureTypeCommandPoolCreateInfo = 39;
    private const int VkStructureTypeCommandBufferAllocateInfo = 40;
    private const int VkStructureTypeCommandBufferBeginInfo = 42;
    private const int VkStructureTypeRenderPassBeginInfo = 43;
    private const int VkStructureTypeImageCreateInfo = 14;
    private const int VkStructureTypeImageViewCreateInfo = 15;
    private const uint VkApiVersion10 = 4194304;
    private const uint VkImageType2D = 1;
    private const uint VkImageViewType2D = 1;
    private const uint VkImageTilingOptimal = 0;
    private const uint VkSharingModeExclusive = 0;
    private const uint VkImageLayoutUndefined = 0;
    private const uint VkImageLayoutColorAttachmentOptimal = 2;
    private const uint VkImageLayoutTransferSrcOptimal = 6;
    private const uint VkSampleCount1Bit = 1;
    private const uint VkFormatR8G8B8A8Unorm = 37;
    private const uint VkFormatB8G8R8A8Unorm = 44;
    private const uint VkImageUsageColorAttachmentBit = 0x00000010;
    private const uint VkImageUsageTransferSrcBit = 0x00000001;
    private const uint VkBufferUsageTransferDstBit = 0x00000002;
    private const uint VkImageAspectColorBit = 0x00000001;
    private const uint VkAttachmentLoadOpClear = 1;
    private const uint VkAttachmentStoreOpStore = 0;
    private const uint VkAttachmentLoadOpDontCare = 2;
    private const uint VkAttachmentStoreOpDontCare = 1;
    private const uint VkPipelineBindPointGraphics = 0;
    private const uint VkPipelineStageColorAttachmentOutputBit = 0x00000400;
    private const uint VkPipelineStageTransferBit = 0x00001000;
    private const uint VkAccessColorAttachmentWriteBit = 0x00000100;
    private const uint VkAccessTransferReadBit = 0x00000800;
    private const uint VkSubpassExternal = 0xFFFFFFFF;
    private const uint VkCommandBufferLevelPrimary = 0;
    private const uint VkSubpassContentsInline = 0;
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
    private const ulong FenceTimeoutNanoseconds = 5_000_000_000;

    public ValueTask<RekallAgeVulkanRenderPassSubmissionResult> SubmitClearRenderPassAsync(
        uint width,
        uint height,
        string format,
        string? preferredDeviceType,
        RekallAgeVulkanClearColor clearColor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (width == 0 || height == 0)
        {
            errors.Add("Vulkan render target width and height must be greater than zero.");
            return ValueTask.FromResult(Unavailable(null, null, width, height, format, clearColor, false, false, false, false, errors));
        }

        if (!TryLoadVulkan(errors, out var library, out var loaderName))
        {
            return ValueTask.FromResult(Unavailable(null, null, width, height, format, clearColor, false, false, false, false, errors));
        }

        try
        {
            var context = CreateContext(library, errors);
            if (context is null)
            {
                return ValueTask.FromResult(Unavailable(loaderName, null, width, height, format, clearColor, false, false, false, false, errors));
            }

            return ValueTask.FromResult(SubmitClearRenderPass(context.Value, loaderName!, width, height, format, preferredDeviceType, clearColor, errors));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    public ValueTask<RekallAgeVulkanRenderPassReadbackResult> ReadClearRenderPassAsync(
        uint width,
        uint height,
        string format,
        string? preferredDeviceType,
        RekallAgeVulkanClearColor clearColor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (width == 0 || height == 0)
        {
            errors.Add("Vulkan readback width and height must be greater than zero.");
            return ValueTask.FromResult(UnavailableReadback(null, null, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors));
        }

        var byteCount = checked((ulong)width * height * 4);
        if (byteCount > int.MaxValue)
        {
            errors.Add("Vulkan readback size exceeds the maximum host buffer size for this smoke tool.");
            return ValueTask.FromResult(UnavailableReadback(null, null, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors));
        }

        if (!TryLoadVulkan(errors, out var library, out var loaderName))
        {
            return ValueTask.FromResult(UnavailableReadback(null, null, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors));
        }

        try
        {
            var context = CreateContext(library, errors);
            if (context is null)
            {
                return ValueTask.FromResult(UnavailableReadback(loaderName, null, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors));
            }

            return ValueTask.FromResult(ReadClearRenderPassOperation(context.Value, loaderName!, width, height, format, preferredDeviceType, clearColor, errors).Result);
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    public ValueTask<RekallAgeVulkanRenderPassCaptureResult> CaptureClearRenderPassAsync(
        uint width,
        uint height,
        string format,
        string? preferredDeviceType,
        string outputDirectory,
        RekallAgeVulkanClearColor clearColor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            errors.Add("Vulkan capture output directory is required.");
            return ValueTask.FromResult(UnavailableCapture(string.Empty, null, null, width, height, format, clearColor, 0, 0, default, 0, errors));
        }

        if (width == 0 || height == 0)
        {
            errors.Add("Vulkan capture width and height must be greater than zero.");
            return ValueTask.FromResult(UnavailableCapture(string.Empty, null, null, width, height, format, clearColor, 0, 0, default, 0, errors));
        }

        var byteCount = checked((ulong)width * height * 4);
        if (byteCount > int.MaxValue)
        {
            errors.Add("Vulkan capture size exceeds the maximum host buffer size for this smoke tool.");
            return ValueTask.FromResult(UnavailableCapture(string.Empty, null, null, width, height, format, clearColor, 0, 0, default, 0, errors));
        }

        if (!TryLoadVulkan(errors, out var library, out var loaderName))
        {
            return ValueTask.FromResult(UnavailableCapture(string.Empty, null, null, width, height, format, clearColor, 0, 0, default, 0, errors));
        }

        try
        {
            var context = CreateContext(library, errors);
            if (context is null)
            {
                return ValueTask.FromResult(UnavailableCapture(string.Empty, loaderName, null, width, height, format, clearColor, 0, 0, default, 0, errors));
            }

            var operation = ReadClearRenderPassOperation(context.Value, loaderName!, width, height, format, preferredDeviceType, clearColor, errors);
            if (!operation.Result.Readback)
            {
                return ValueTask.FromResult(UnavailableCapture(
                    string.Empty,
                    operation.Result.LoaderName,
                    operation.Result.SelectedDevice,
                    width,
                    height,
                    format,
                    clearColor,
                    operation.Result.BytesRead,
                    operation.Result.NonZeroBytes,
                    operation.Result.FirstPixel,
                    operation.Result.ByteChecksum,
                    operation.Result.Errors));
            }

            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(
                outputDirectory,
                $"vulkan-clear-{width}x{height}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png");
            return WriteCaptureAsync(outputPath, width, height, format, operation.Result, operation.RgbaBytes, cancellationToken);
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static RekallAgeVulkanRenderPassSubmissionResult SubmitClearRenderPass(
        VulkanRenderPassSubmissionContext context,
        string loaderName,
        uint width,
        uint height,
        string format,
        string? preferredDeviceType,
        RekallAgeVulkanClearColor clearColor,
        List<string> errors)
    {
        using (context)
        {
            var nativeDevices = EnumerateCandidateDevices(context, errors);
            var selection = RekallAgeVulkanDeviceSelector.Select(nativeDevices.Select(device => device.Device), preferredDeviceType);
            if (selection is null)
            {
                errors.Add("No Vulkan physical device with a graphics queue was found.");
                return Unavailable(loaderName, null, width, height, format, clearColor, false, false, false, false, errors);
            }

            var nativeDevice = nativeDevices.First(device => device.Device.Name.Equals(selection.Device.Name, StringComparison.Ordinal));
            var device = CreateLogicalDevice(context, nativeDevice.Handle, selection, errors);
            if (device == IntPtr.Zero)
            {
                return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, false, false, false, false, errors);
            }

            try
            {
                var image = CreateImage(context, device, width, height, format, includeTransferSource: false, errors);
                if (image == IntPtr.Zero)
                {
                    return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, false, false, false, false, errors);
                }

                try
                {
                    var memory = BindImageMemory(context, nativeDevice.Handle, device, image, errors, out _);
                    if (memory == IntPtr.Zero)
                    {
                        return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, true, false, false, false, errors);
                    }

                    try
                    {
                        var imageView = CreateImageView(context, device, image, format, errors);
                        if (imageView == IntPtr.Zero)
                        {
                            return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, true, false, false, false, errors);
                        }

                        try
                        {
                            var renderPass = CreateRenderPass(context, device, format, finalLayoutTransferSource: false, errors);
                            if (renderPass == IntPtr.Zero)
                            {
                                return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, true, true, false, false, errors);
                            }

                            try
                            {
                                var framebuffer = CreateFramebuffer(context, device, renderPass, imageView, width, height, errors);
                                if (framebuffer == IntPtr.Zero)
                                {
                                    return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, true, true, true, false, errors);
                                }

                                try
                                {
                                    context.GetDeviceQueue(device, selection.QueueFamily.Index, 0, out var queue);
                                    if (queue == IntPtr.Zero)
                                    {
                                        errors.Add("vkGetDeviceQueue returned a null graphics queue.");
                                        return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, true, true, true, true, errors);
                                    }

                                    var commandPool = CreateCommandPool(context, device, selection.QueueFamily.Index, errors);
                                    if (commandPool == IntPtr.Zero)
                                    {
                                        return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, true, true, true, true, errors);
                                    }

                                    try
                                    {
                                        var commandBuffer = AllocateCommandBuffer(context, device, commandPool, errors);
                                        if (commandBuffer == IntPtr.Zero)
                                        {
                                            return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, true, true, true, true, errors, commandPoolCreated: true);
                                        }

                                        if (!RecordClearRenderPass(context, commandBuffer, renderPass, framebuffer, width, height, clearColor, errors, out var renderPassBegan, out var renderPassEnded))
                                        {
                                            return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, true, true, true, true, errors, true, true, renderPassBegan, renderPassEnded);
                                        }

                                        var fence = CreateFence(context, device, errors);
                                        if (fence == IntPtr.Zero)
                                        {
                                            return Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, true, true, true, true, errors, true, true, true, true);
                                        }

                                        try
                                        {
                                            var fenceSignaled = SubmitAndWait(context, device, queue, commandBuffer, fence, errors);
                                            return fenceSignaled
                                                ? new RekallAgeVulkanRenderPassSubmissionResult(
                                                    Submitted: true,
                                                    LoaderName: loaderName,
                                                    SelectedDevice: ToSelectedDevice(selection),
                                                    Width: width,
                                                    Height: height,
                                                    Format: NormalizeFormat(format),
                                                    ImageCreated: true,
                                                    ImageViewCreated: true,
                                                    RenderPassCreated: true,
                                                    FramebufferCreated: true,
                                                    CommandPoolCreated: true,
                                                    CommandBufferAllocated: true,
                                                    RenderPassBegan: true,
                                                    RenderPassEnded: true,
                                                    FenceSignaled: true,
                                                    ClearColor: clearColor,
                                                    Errors: errors)
                                                : Unavailable(loaderName, ToSelectedDevice(selection), width, height, format, clearColor, true, true, true, true, errors, true, true, true, true);
                                        }
                                        finally
                                        {
                                            context.DestroyFence(device, fence, IntPtr.Zero);
                                        }
                                    }
                                    finally
                                    {
                                        context.DestroyCommandPool(device, commandPool, IntPtr.Zero);
                                    }
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

    private static VulkanReadbackOperation ReadClearRenderPassOperation(
        VulkanRenderPassSubmissionContext context,
        string loaderName,
        uint width,
        uint height,
        string format,
        string? preferredDeviceType,
        RekallAgeVulkanClearColor clearColor,
        List<string> errors)
    {
        using (context)
        {
            var nativeDevices = EnumerateCandidateDevices(context, errors);
            var selection = RekallAgeVulkanDeviceSelector.Select(nativeDevices.Select(device => device.Device), preferredDeviceType);
            if (selection is null)
            {
                errors.Add("No Vulkan physical device with a graphics queue was found.");
                return UnavailableReadbackOperation(loaderName, null, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors);
            }

            var selectedDevice = ToSelectedDevice(selection);
            var nativeDevice = nativeDevices.First(device => device.Device.Name.Equals(selection.Device.Name, StringComparison.Ordinal));
            var device = CreateLogicalDevice(context, nativeDevice.Handle, selection, errors);
            if (device == IntPtr.Zero)
            {
                return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors);
            }

            try
            {
                context.GetDeviceQueue(device, selection.QueueFamily.Index, 0, out var queue);
                if (queue == IntPtr.Zero)
                {
                    errors.Add("vkGetDeviceQueue returned a null graphics queue.");
                    return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors);
                }

                var image = CreateImage(context, device, width, height, format, includeTransferSource: true, errors);
                if (image == IntPtr.Zero)
                {
                    return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors);
                }

                try
                {
                    var imageMemory = BindImageMemory(context, nativeDevice.Handle, device, image, errors, out _);
                    if (imageMemory == IntPtr.Zero)
                    {
                        return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors);
                    }

                    try
                    {
                        var imageView = CreateImageView(context, device, image, format, errors);
                        if (imageView == IntPtr.Zero)
                        {
                            return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors);
                        }

                        try
                        {
                            var renderPass = CreateRenderPass(context, device, format, finalLayoutTransferSource: true, errors);
                            if (renderPass == IntPtr.Zero)
                            {
                                return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors);
                            }

                            try
                            {
                                var framebuffer = CreateFramebuffer(context, device, renderPass, imageView, width, height, errors);
                                if (framebuffer == IntPtr.Zero)
                                {
                                    return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, false, false, false, 0, 0, default, 0, errors);
                                }

                                try
                                {
                                    var byteCount = checked((ulong)width * height * 4);
                                    var readbackBuffer = CreateBuffer(context, device, byteCount, errors);
                                    if (readbackBuffer == IntPtr.Zero)
                                    {
                                        return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, true, false, false, 0, 0, default, 0, errors);
                                    }

                                    try
                                    {
                                        var readbackMemory = BindBufferMemory(context, nativeDevice.Handle, device, readbackBuffer, errors);
                                        if (readbackMemory == IntPtr.Zero)
                                        {
                                            return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, true, false, false, 0, 0, default, 0, errors);
                                        }

                                        try
                                        {
                                            var commandPool = CreateCommandPool(context, device, selection.QueueFamily.Index, errors);
                                            if (commandPool == IntPtr.Zero)
                                            {
                                                return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, true, true, false, 0, 0, default, 0, errors);
                                            }

                                            try
                                            {
                                                var commandBuffer = AllocateCommandBuffer(context, device, commandPool, errors);
                                                if (commandBuffer == IntPtr.Zero)
                                                {
                                                    return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, true, true, false, 0, 0, default, 0, errors);
                                                }

                                                if (!RecordClearRenderPassAndCopy(context, commandBuffer, renderPass, framebuffer, image, readbackBuffer, width, height, clearColor, errors))
                                                {
                                                    return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, true, true, false, 0, 0, default, 0, errors);
                                                }

                                                var fence = CreateFence(context, device, errors);
                                                if (fence == IntPtr.Zero)
                                                {
                                                    return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, true, true, false, 0, 0, default, 0, errors);
                                                }

                                                try
                                                {
                                                    var submitted = SubmitAndWait(context, device, queue, commandBuffer, fence, errors);
                                                    if (!submitted)
                                                    {
                                                        return UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, false, true, true, false, 0, 0, default, 0, errors);
                                                    }

                                                    var readback = MapReadbackBuffer(context, device, readbackMemory, byteCount, NormalizeFormat(format), errors);
                                                    return readback.BufferMapped
                                                        ? new VulkanReadbackOperation(
                                                            new RekallAgeVulkanRenderPassReadbackResult(
                                                                Readback: true,
                                                                LoaderName: loaderName,
                                                                SelectedDevice: selectedDevice,
                                                                Width: width,
                                                                Height: height,
                                                                Format: NormalizeFormat(format),
                                                                ClearColor: clearColor,
                                                                Submitted: true,
                                                                BufferCreated: true,
                                                                BufferBound: true,
                                                                BufferMapped: true,
                                                                BytesRead: byteCount,
                                                                NonZeroBytes: readback.NonZeroBytes,
                                                                FirstPixel: readback.FirstPixel,
                                                                ByteChecksum: readback.ByteChecksum,
                                                                Errors: errors),
                                                            readback.RgbaBytes)
                                                        : UnavailableReadbackOperation(loaderName, selectedDevice, width, height, format, clearColor, true, true, true, false, 0, 0, default, 0, errors);
                                                }
                                                finally
                                                {
                                                    context.DestroyFence(device, fence, IntPtr.Zero);
                                                }
                                            }
                                            finally
                                            {
                                                context.DestroyCommandPool(device, commandPool, IntPtr.Zero);
                                            }
                                        }
                                        finally
                                        {
                                            context.FreeMemory(device, readbackMemory, IntPtr.Zero);
                                        }
                                    }
                                    finally
                                    {
                                        context.DestroyBuffer(device, readbackBuffer, IntPtr.Zero);
                                    }
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
                        context.FreeMemory(device, imageMemory, IntPtr.Zero);
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

    private static VulkanRenderPassSubmissionContext? CreateContext(IntPtr library, List<string> errors)
    {
        if (!TryGetVulkanExport(library, "vkCreateInstance", errors, out VkCreateInstance createInstance)
            || !TryGetVulkanExport(library, "vkDestroyInstance", errors, out VkDestroyInstance destroyInstance)
            || !TryGetVulkanExport(library, "vkEnumeratePhysicalDevices", errors, out VkEnumeratePhysicalDevices enumerateDevices)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceProperties", errors, out VkGetPhysicalDeviceProperties getProperties)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceQueueFamilyProperties", errors, out VkGetPhysicalDeviceQueueFamilyProperties getQueueFamilies)
            || !TryGetVulkanExport(library, "vkGetPhysicalDeviceMemoryProperties", errors, out VkGetPhysicalDeviceMemoryProperties getMemoryProperties)
            || !TryGetVulkanExport(library, "vkCreateDevice", errors, out VkCreateDevice createDevice)
            || !TryGetVulkanExport(library, "vkDestroyDevice", errors, out VkDestroyDevice destroyDevice)
            || !TryGetVulkanExport(library, "vkGetDeviceQueue", errors, out VkGetDeviceQueue getDeviceQueue)
            || !TryGetVulkanExport(library, "vkCreateImage", errors, out VkCreateImage createImage)
            || !TryGetVulkanExport(library, "vkDestroyImage", errors, out VkDestroyImage destroyImage)
            || !TryGetVulkanExport(library, "vkGetImageMemoryRequirements", errors, out VkGetImageMemoryRequirements getImageMemoryRequirements)
            || !TryGetVulkanExport(library, "vkCreateBuffer", errors, out VkCreateBuffer createBuffer)
            || !TryGetVulkanExport(library, "vkDestroyBuffer", errors, out VkDestroyBuffer destroyBuffer)
            || !TryGetVulkanExport(library, "vkGetBufferMemoryRequirements", errors, out VkGetBufferMemoryRequirements getBufferMemoryRequirements)
            || !TryGetVulkanExport(library, "vkAllocateMemory", errors, out VkAllocateMemory allocateMemory)
            || !TryGetVulkanExport(library, "vkFreeMemory", errors, out VkFreeMemory freeMemory)
            || !TryGetVulkanExport(library, "vkBindImageMemory", errors, out VkBindImageMemory bindImageMemory)
            || !TryGetVulkanExport(library, "vkBindBufferMemory", errors, out VkBindBufferMemory bindBufferMemory)
            || !TryGetVulkanExport(library, "vkMapMemory", errors, out VkMapMemory mapMemory)
            || !TryGetVulkanExport(library, "vkUnmapMemory", errors, out VkUnmapMemory unmapMemory)
            || !TryGetVulkanExport(library, "vkCreateImageView", errors, out VkCreateImageView createImageView)
            || !TryGetVulkanExport(library, "vkDestroyImageView", errors, out VkDestroyImageView destroyImageView)
            || !TryGetVulkanExport(library, "vkCreateRenderPass", errors, out VkCreateRenderPass createRenderPass)
            || !TryGetVulkanExport(library, "vkDestroyRenderPass", errors, out VkDestroyRenderPass destroyRenderPass)
            || !TryGetVulkanExport(library, "vkCreateFramebuffer", errors, out VkCreateFramebuffer createFramebuffer)
            || !TryGetVulkanExport(library, "vkDestroyFramebuffer", errors, out VkDestroyFramebuffer destroyFramebuffer)
            || !TryGetVulkanExport(library, "vkCreateCommandPool", errors, out VkCreateCommandPool createCommandPool)
            || !TryGetVulkanExport(library, "vkDestroyCommandPool", errors, out VkDestroyCommandPool destroyCommandPool)
            || !TryGetVulkanExport(library, "vkAllocateCommandBuffers", errors, out VkAllocateCommandBuffers allocateCommandBuffers)
            || !TryGetVulkanExport(library, "vkBeginCommandBuffer", errors, out VkBeginCommandBuffer beginCommandBuffer)
            || !TryGetVulkanExport(library, "vkCmdBeginRenderPass", errors, out VkCmdBeginRenderPass cmdBeginRenderPass)
            || !TryGetVulkanExport(library, "vkCmdEndRenderPass", errors, out VkCmdEndRenderPass cmdEndRenderPass)
            || !TryGetVulkanExport(library, "vkCmdCopyImageToBuffer", errors, out VkCmdCopyImageToBuffer cmdCopyImageToBuffer)
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

            return new VulkanRenderPassSubmissionContext(
                instance,
                destroyInstance,
                enumerateDevices,
                getProperties,
                getQueueFamilies,
                getMemoryProperties,
                createDevice,
                destroyDevice,
                getDeviceQueue,
                createImage,
                destroyImage,
                getImageMemoryRequirements,
                createBuffer,
                destroyBuffer,
                getBufferMemoryRequirements,
                allocateMemory,
                freeMemory,
                bindImageMemory,
                bindBufferMemory,
                mapMemory,
                unmapMemory,
                createImageView,
                destroyImageView,
                createRenderPass,
                destroyRenderPass,
                createFramebuffer,
                destroyFramebuffer,
                createCommandPool,
                destroyCommandPool,
                allocateCommandBuffers,
                beginCommandBuffer,
                cmdBeginRenderPass,
                cmdEndRenderPass,
                cmdCopyImageToBuffer,
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

    private static IReadOnlyList<NativeVulkanCandidateDevice> EnumerateCandidateDevices(VulkanRenderPassSubmissionContext context, List<string> errors)
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

    private static RekallAgeVulkanCandidateDevice ReadCandidateDevice(VulkanRenderPassSubmissionContext context, IntPtr physicalDevice)
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

    private static IReadOnlyList<RekallAgeVulkanQueueFamilyInfo> ReadQueueFamilies(VulkanRenderPassSubmissionContext context, IntPtr physicalDevice)
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

    private static IntPtr CreateLogicalDevice(VulkanRenderPassSubmissionContext context, IntPtr physicalDevice, RekallAgeVulkanDeviceSelection selection, List<string> errors)
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

    private static IntPtr CreateImage(
        VulkanRenderPassSubmissionContext context,
        IntPtr device,
        uint width,
        uint height,
        string format,
        bool includeTransferSource,
        List<string> errors)
    {
        var usage = VkImageUsageColorAttachmentBit;
        if (includeTransferSource)
        {
            usage |= VkImageUsageTransferSrcBit;
        }

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
            usage,
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

    private static IntPtr BindImageMemory(VulkanRenderPassSubmissionContext context, IntPtr physicalDevice, IntPtr device, IntPtr image, List<string> errors, out uint? memoryTypeIndex)
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

    private static VkMemoryRequirements GetMemoryRequirements(VulkanRenderPassSubmissionContext context, IntPtr device, IntPtr image)
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

    private static IReadOnlyList<RekallAgeVulkanMemoryTypeInfo> ReadMemoryTypes(VulkanRenderPassSubmissionContext context, IntPtr physicalDevice)
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

    private static IntPtr AllocateMemory(VulkanRenderPassSubmissionContext context, IntPtr device, ulong allocationSize, uint memoryTypeIndex, List<string> errors)
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

    private static IntPtr CreateImageView(VulkanRenderPassSubmissionContext context, IntPtr device, IntPtr image, string format, List<string> errors)
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

    private static IntPtr CreateRenderPass(
        VulkanRenderPassSubmissionContext context,
        IntPtr device,
        string format,
        bool finalLayoutTransferSource,
        List<string> errors)
    {
        var finalLayout = finalLayoutTransferSource
            ? VkImageLayoutTransferSrcOptimal
            : VkImageLayoutColorAttachmentOptimal;
        var attachment = new VkAttachmentDescription(
            0,
            ParseFormat(format),
            VkSampleCount1Bit,
            VkAttachmentLoadOpClear,
            VkAttachmentStoreOpStore,
            VkAttachmentLoadOpDontCare,
            VkAttachmentStoreOpDontCare,
            VkImageLayoutUndefined,
            finalLayout);
        var attachmentPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkAttachmentDescription>());
        var colorRef = new VkAttachmentReference(0, VkImageLayoutColorAttachmentOptimal);
        var colorRefPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkAttachmentReference>());
        var subpassPointer = IntPtr.Zero;
        var dependencyPointer = IntPtr.Zero;
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
            if (finalLayoutTransferSource)
            {
                var dependency = new VkSubpassDependency(
                    0,
                    VkSubpassExternal,
                    VkPipelineStageColorAttachmentOutputBit,
                    VkPipelineStageTransferBit,
                    VkAccessColorAttachmentWriteBit,
                    VkAccessTransferReadBit,
                    0);
                dependencyPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkSubpassDependency>());
                Marshal.StructureToPtr(dependency, dependencyPointer, false);
            }

            var createInfo = new VkRenderPassCreateInfo(
                VkStructureTypeRenderPassCreateInfo,
                IntPtr.Zero,
                0,
                1,
                attachmentPointer,
                1,
                subpassPointer,
                finalLayoutTransferSource ? 1u : 0u,
                dependencyPointer);
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

            if (dependencyPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(dependencyPointer);
            }

            Marshal.FreeHGlobal(colorRefPointer);
            Marshal.FreeHGlobal(attachmentPointer);
        }
    }

    private static IntPtr CreateFramebuffer(VulkanRenderPassSubmissionContext context, IntPtr device, IntPtr renderPass, IntPtr imageView, uint width, uint height, List<string> errors)
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

    private static IntPtr CreateBuffer(
        VulkanRenderPassSubmissionContext context,
        IntPtr device,
        ulong sizeBytes,
        List<string> errors)
    {
        var createInfo = new VkBufferCreateInfo(
            VkStructureTypeBufferCreateInfo,
            IntPtr.Zero,
            0,
            sizeBytes,
            VkBufferUsageTransferDstBit,
            VkSharingModeExclusive,
            0,
            IntPtr.Zero);
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkBufferCreateInfo>());
        try
        {
            Marshal.StructureToPtr(createInfo, pointer, false);
            var result = context.CreateBuffer(device, pointer, IntPtr.Zero, out var buffer);
            if (result != VkSuccess)
            {
                errors.Add($"vkCreateBuffer failed with VkResult {result}.");
                return IntPtr.Zero;
            }

            return buffer;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static IntPtr BindBufferMemory(
        VulkanRenderPassSubmissionContext context,
        IntPtr physicalDevice,
        IntPtr device,
        IntPtr buffer,
        List<string> errors)
    {
        var requirements = GetBufferMemoryRequirements(context, device, buffer);
        var memoryTypes = ReadMemoryTypes(context, physicalDevice);
        var memoryTypeIndex = RekallAgeVulkanMemoryTypeSelector.Select(
            memoryTypes,
            requirements.MemoryTypeBits,
            ["host-visible", "host-coherent"]);
        if (memoryTypeIndex is null)
        {
            errors.Add("No host-visible, host-coherent Vulkan memory type was compatible with the readback buffer.");
            return IntPtr.Zero;
        }

        var memory = AllocateMemory(context, device, requirements.Size, memoryTypeIndex.Value, errors);
        if (memory == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var bindResult = context.BindBufferMemory(device, buffer, memory, 0);
        if (bindResult != VkSuccess)
        {
            context.FreeMemory(device, memory, IntPtr.Zero);
            errors.Add($"vkBindBufferMemory failed with VkResult {bindResult}.");
            return IntPtr.Zero;
        }

        return memory;
    }

    private static VkMemoryRequirements GetBufferMemoryRequirements(VulkanRenderPassSubmissionContext context, IntPtr device, IntPtr buffer)
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

    private static IntPtr CreateCommandPool(
        VulkanRenderPassSubmissionContext context,
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
        VulkanRenderPassSubmissionContext context,
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

    private static bool RecordClearRenderPass(
        VulkanRenderPassSubmissionContext context,
        IntPtr commandBuffer,
        IntPtr renderPass,
        IntPtr framebuffer,
        uint width,
        uint height,
        RekallAgeVulkanClearColor clearColor,
        List<string> errors,
        out bool renderPassBegan,
        out bool renderPassEnded)
    {
        renderPassBegan = false;
        renderPassEnded = false;
        var beginInfoPointer = IntPtr.Zero;
        var clearValuePointer = IntPtr.Zero;
        var renderPassBeginInfoPointer = IntPtr.Zero;
        try
        {
            var beginInfo = new VkCommandBufferBeginInfo(VkStructureTypeCommandBufferBeginInfo, IntPtr.Zero, 0, IntPtr.Zero);
            beginInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkCommandBufferBeginInfo>());
            Marshal.StructureToPtr(beginInfo, beginInfoPointer, false);
            var result = context.BeginCommandBuffer(commandBuffer, beginInfoPointer);
            if (result != VkSuccess)
            {
                errors.Add($"vkBeginCommandBuffer failed with VkResult {result}.");
                return false;
            }

            var clearValue = new VkClearColorValue(clearColor.R, clearColor.G, clearColor.B, clearColor.A);
            clearValuePointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkClearColorValue>());
            Marshal.StructureToPtr(clearValue, clearValuePointer, false);
            var renderPassBeginInfo = new VkRenderPassBeginInfo(
                VkStructureTypeRenderPassBeginInfo,
                IntPtr.Zero,
                renderPass,
                framebuffer,
                new VkRect2D(new VkOffset2D(0, 0), new VkExtent2D(width, height)),
                1,
                clearValuePointer);
            renderPassBeginInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkRenderPassBeginInfo>());
            Marshal.StructureToPtr(renderPassBeginInfo, renderPassBeginInfoPointer, false);
            context.CmdBeginRenderPass(commandBuffer, renderPassBeginInfoPointer, VkSubpassContentsInline);
            renderPassBegan = true;
            context.CmdEndRenderPass(commandBuffer);
            renderPassEnded = true;

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
            if (renderPassBeginInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(renderPassBeginInfoPointer);
            }

            if (clearValuePointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(clearValuePointer);
            }

            if (beginInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(beginInfoPointer);
            }
        }
    }

    private static bool RecordClearRenderPassAndCopy(
        VulkanRenderPassSubmissionContext context,
        IntPtr commandBuffer,
        IntPtr renderPass,
        IntPtr framebuffer,
        IntPtr image,
        IntPtr readbackBuffer,
        uint width,
        uint height,
        RekallAgeVulkanClearColor clearColor,
        List<string> errors)
    {
        var beginInfoPointer = IntPtr.Zero;
        var clearValuePointer = IntPtr.Zero;
        var renderPassBeginInfoPointer = IntPtr.Zero;
        var copyRegionPointer = IntPtr.Zero;
        try
        {
            var beginInfo = new VkCommandBufferBeginInfo(VkStructureTypeCommandBufferBeginInfo, IntPtr.Zero, 0, IntPtr.Zero);
            beginInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkCommandBufferBeginInfo>());
            Marshal.StructureToPtr(beginInfo, beginInfoPointer, false);
            var result = context.BeginCommandBuffer(commandBuffer, beginInfoPointer);
            if (result != VkSuccess)
            {
                errors.Add($"vkBeginCommandBuffer failed with VkResult {result}.");
                return false;
            }

            var clearValue = new VkClearColorValue(clearColor.R, clearColor.G, clearColor.B, clearColor.A);
            clearValuePointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkClearColorValue>());
            Marshal.StructureToPtr(clearValue, clearValuePointer, false);
            var renderPassBeginInfo = new VkRenderPassBeginInfo(
                VkStructureTypeRenderPassBeginInfo,
                IntPtr.Zero,
                renderPass,
                framebuffer,
                new VkRect2D(new VkOffset2D(0, 0), new VkExtent2D(width, height)),
                1,
                clearValuePointer);
            renderPassBeginInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkRenderPassBeginInfo>());
            Marshal.StructureToPtr(renderPassBeginInfo, renderPassBeginInfoPointer, false);
            context.CmdBeginRenderPass(commandBuffer, renderPassBeginInfoPointer, VkSubpassContentsInline);
            context.CmdEndRenderPass(commandBuffer);

            var copyRegion = new VkBufferImageCopy(
                0,
                0,
                0,
                new VkImageSubresourceLayers(VkImageAspectColorBit, 0, 0, 1),
                new VkOffset3D(0, 0, 0),
                new VkExtent3D(width, height, 1));
            copyRegionPointer = Marshal.AllocHGlobal(Marshal.SizeOf<VkBufferImageCopy>());
            Marshal.StructureToPtr(copyRegion, copyRegionPointer, false);
            context.CmdCopyImageToBuffer(
                commandBuffer,
                image,
                VkImageLayoutTransferSrcOptimal,
                readbackBuffer,
                1,
                copyRegionPointer);

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
            if (copyRegionPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(copyRegionPointer);
            }

            if (renderPassBeginInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(renderPassBeginInfoPointer);
            }

            if (clearValuePointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(clearValuePointer);
            }

            if (beginInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(beginInfoPointer);
            }
        }
    }

    private static VulkanReadbackBytes MapReadbackBuffer(
        VulkanRenderPassSubmissionContext context,
        IntPtr device,
        IntPtr memory,
        ulong sizeBytes,
        string format,
        List<string> errors)
    {
        var result = context.MapMemory(device, memory, 0, sizeBytes, 0, out var mapped);
        if (result != VkSuccess)
        {
            errors.Add($"vkMapMemory failed with VkResult {result}.");
            return new VulkanReadbackBytes(false, [], 0, new RekallAgeVulkanReadbackPixel(0, 0, 0, 0), 0);
        }

        try
        {
            var bytes = new byte[(int)sizeBytes];
            Marshal.Copy(mapped, bytes, 0, bytes.Length);
            var nonZero = 0ul;
            var checksum = 0ul;
            foreach (var value in bytes)
            {
                checksum += value;
                if (value != 0)
                {
                    nonZero++;
                }
            }

            var firstPixel = bytes.Length >= 4
                ? ToReadbackPixel(bytes[0], bytes[1], bytes[2], bytes[3], format)
                : new RekallAgeVulkanReadbackPixel(0, 0, 0, 0);
            return new VulkanReadbackBytes(true, bytes, nonZero, firstPixel, checksum);
        }
        finally
        {
            context.UnmapMemory(device, memory);
        }
    }

    private static RekallAgeVulkanReadbackPixel ToReadbackPixel(byte first, byte second, byte third, byte fourth, string format)
    {
        return format.Equals("B8G8R8A8_UNorm", StringComparison.Ordinal)
            ? new RekallAgeVulkanReadbackPixel(third, second, first, fourth)
            : new RekallAgeVulkanReadbackPixel(first, second, third, fourth);
    }

    private static IntPtr CreateFence(VulkanRenderPassSubmissionContext context, IntPtr device, List<string> errors)
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
        VulkanRenderPassSubmissionContext context,
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

    private static RekallAgeVulkanRenderPassSubmissionResult Unavailable(
        string? loaderName,
        RekallAgeVulkanSelectedDevice? selectedDevice,
        uint width,
        uint height,
        string format,
        RekallAgeVulkanClearColor clearColor,
        bool imageCreated,
        bool imageViewCreated,
        bool renderPassCreated,
        bool framebufferCreated,
        IReadOnlyList<string> errors,
        bool commandPoolCreated = false,
        bool commandBufferAllocated = false,
        bool renderPassBegan = false,
        bool renderPassEnded = false,
        bool fenceSignaled = false)
    {
        return new RekallAgeVulkanRenderPassSubmissionResult(
            Submitted: false,
            LoaderName: loaderName,
            SelectedDevice: selectedDevice,
            Width: width,
            Height: height,
            Format: NormalizeFormat(format),
            ClearColor: clearColor,
            ImageCreated: imageCreated,
            ImageViewCreated: imageViewCreated,
            RenderPassCreated: renderPassCreated,
            FramebufferCreated: framebufferCreated,
            CommandPoolCreated: commandPoolCreated,
            CommandBufferAllocated: commandBufferAllocated,
            RenderPassBegan: renderPassBegan,
            RenderPassEnded: renderPassEnded,
            FenceSignaled: fenceSignaled,
            Errors: errors.ToArray());
    }

    private static RekallAgeVulkanRenderPassReadbackResult UnavailableReadback(
        string? loaderName,
        RekallAgeVulkanSelectedDevice? selectedDevice,
        uint width,
        uint height,
        string format,
        RekallAgeVulkanClearColor clearColor,
        bool submitted,
        bool bufferCreated,
        bool bufferBound,
        bool bufferMapped,
        ulong bytesRead,
        ulong nonZeroBytes,
        RekallAgeVulkanReadbackPixel firstPixel,
        ulong byteChecksum,
        IReadOnlyList<string> errors)
    {
        return new RekallAgeVulkanRenderPassReadbackResult(
            Readback: false,
            LoaderName: loaderName,
            SelectedDevice: selectedDevice,
            Width: width,
            Height: height,
            Format: NormalizeFormat(format),
            ClearColor: clearColor,
            Submitted: submitted,
            BufferCreated: bufferCreated,
            BufferBound: bufferBound,
            BufferMapped: bufferMapped,
            BytesRead: bytesRead,
            NonZeroBytes: nonZeroBytes,
            FirstPixel: firstPixel,
            ByteChecksum: byteChecksum,
            Errors: errors.ToArray());
    }

    private static VulkanReadbackOperation UnavailableReadbackOperation(
        string? loaderName,
        RekallAgeVulkanSelectedDevice? selectedDevice,
        uint width,
        uint height,
        string format,
        RekallAgeVulkanClearColor clearColor,
        bool submitted,
        bool bufferCreated,
        bool bufferBound,
        bool bufferMapped,
        ulong bytesRead,
        ulong nonZeroBytes,
        RekallAgeVulkanReadbackPixel firstPixel,
        ulong byteChecksum,
        IReadOnlyList<string> errors)
    {
        return new VulkanReadbackOperation(
            UnavailableReadback(
                loaderName,
                selectedDevice,
                width,
                height,
                format,
                clearColor,
                submitted,
                bufferCreated,
                bufferBound,
                bufferMapped,
                bytesRead,
                nonZeroBytes,
                firstPixel,
                byteChecksum,
                errors),
            []);
    }

    private static RekallAgeVulkanRenderPassCaptureResult UnavailableCapture(
        string outputPath,
        string? loaderName,
        RekallAgeVulkanSelectedDevice? selectedDevice,
        uint width,
        uint height,
        string format,
        RekallAgeVulkanClearColor clearColor,
        ulong bytesRead,
        ulong nonZeroBytes,
        RekallAgeVulkanReadbackPixel firstPixel,
        ulong byteChecksum,
        IReadOnlyList<string> errors)
    {
        return new RekallAgeVulkanRenderPassCaptureResult(
            Captured: false,
            OutputPath: outputPath,
            LoaderName: loaderName,
            SelectedDevice: selectedDevice,
            Width: width,
            Height: height,
            Format: NormalizeFormat(format),
            ClearColor: clearColor,
            BytesRead: bytesRead,
            NonZeroBytes: nonZeroBytes,
            FirstPixel: firstPixel,
            ByteChecksum: byteChecksum,
            Errors: errors.ToArray());
    }

    private static async ValueTask<RekallAgeVulkanRenderPassCaptureResult> WriteCaptureAsync(
        string outputPath,
        uint width,
        uint height,
        string format,
        RekallAgeVulkanRenderPassReadbackResult readback,
        byte[] rgbaBytes,
        CancellationToken cancellationToken)
    {
        await RekallAgePngWriter.WriteRgbaAsync(outputPath, checked((int)width), checked((int)height), rgbaBytes, cancellationToken);
        return new RekallAgeVulkanRenderPassCaptureResult(
            Captured: true,
            OutputPath: outputPath,
            LoaderName: readback.LoaderName,
            SelectedDevice: readback.SelectedDevice,
            Width: width,
            Height: height,
            Format: NormalizeFormat(format),
            ClearColor: readback.ClearColor,
            BytesRead: readback.BytesRead,
            NonZeroBytes: readback.NonZeroBytes,
            FirstPixel: readback.FirstPixel,
            ByteChecksum: readback.ByteChecksum,
            Errors: readback.Errors);
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

    private readonly record struct VulkanReadbackBytes(
        bool BufferMapped,
        byte[] RgbaBytes,
        ulong NonZeroBytes,
        RekallAgeVulkanReadbackPixel FirstPixel,
        ulong ByteChecksum);

    private readonly record struct VulkanReadbackOperation(
        RekallAgeVulkanRenderPassReadbackResult Result,
        byte[] RgbaBytes);

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
    private delegate void VkGetDeviceQueue(IntPtr device, uint queueFamilyIndex, uint queueIndex, out IntPtr queue);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateImage(IntPtr device, IntPtr createInfo, IntPtr allocator, out IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyImage(IntPtr device, IntPtr image, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetImageMemoryRequirements(IntPtr device, IntPtr image, IntPtr memoryRequirements);

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
    private delegate int VkBindImageMemory(IntPtr device, IntPtr image, IntPtr memory, ulong memoryOffset);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkBindBufferMemory(IntPtr device, IntPtr buffer, IntPtr memory, ulong memoryOffset);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkMapMemory(IntPtr device, IntPtr memory, ulong offset, ulong size, uint flags, out IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkUnmapMemory(IntPtr device, IntPtr memory);

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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateCommandPool(IntPtr device, IntPtr createInfo, IntPtr allocator, out IntPtr commandPool);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyCommandPool(IntPtr device, IntPtr commandPool, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkAllocateCommandBuffers(IntPtr device, IntPtr allocateInfo, IntPtr commandBuffers);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkBeginCommandBuffer(IntPtr commandBuffer, IntPtr beginInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkCmdBeginRenderPass(IntPtr commandBuffer, IntPtr renderPassBeginInfo, uint contents);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkCmdEndRenderPass(IntPtr commandBuffer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkCmdCopyImageToBuffer(IntPtr commandBuffer, IntPtr srcImage, uint srcImageLayout, IntPtr dstBuffer, uint regionCount, IntPtr regions);

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
    private readonly record struct VkExtent3D(uint Width, uint Height, uint Depth);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkImageCreateInfo(int SType, IntPtr PNext, uint Flags, uint ImageType, uint Format, VkExtent3D Extent, uint MipLevels, uint ArrayLayers, uint Samples, uint Tiling, uint Usage, uint SharingMode, uint QueueFamilyIndexCount, IntPtr QueueFamilyIndices, uint InitialLayout);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkBufferCreateInfo(int SType, IntPtr PNext, uint Flags, ulong Size, uint Usage, uint SharingMode, uint QueueFamilyIndexCount, IntPtr QueueFamilyIndices);

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
    private readonly record struct VkSubpassDependency(uint SrcSubpass, uint DstSubpass, uint SrcStageMask, uint DstStageMask, uint SrcAccessMask, uint DstAccessMask, uint DependencyFlags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkRenderPassCreateInfo(int SType, IntPtr PNext, uint Flags, uint AttachmentCount, IntPtr Attachments, uint SubpassCount, IntPtr Subpasses, uint DependencyCount, IntPtr Dependencies);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkFramebufferCreateInfo(int SType, IntPtr PNext, uint Flags, IntPtr RenderPass, uint AttachmentCount, IntPtr Attachments, uint Width, uint Height, uint Layers);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkCommandPoolCreateInfo(int SType, IntPtr PNext, uint Flags, uint QueueFamilyIndex);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkCommandBufferAllocateInfo(int SType, IntPtr PNext, IntPtr CommandPool, uint Level, uint CommandBufferCount);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkCommandBufferBeginInfo(int SType, IntPtr PNext, uint Flags, IntPtr InheritanceInfo);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkOffset2D(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkOffset3D(int X, int Y, int Z);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkExtent2D(uint Width, uint Height);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkRect2D(VkOffset2D Offset, VkExtent2D Extent);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkClearColorValue(float R, float G, float B, float A);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkRenderPassBeginInfo(int SType, IntPtr PNext, IntPtr RenderPass, IntPtr Framebuffer, VkRect2D RenderArea, uint ClearValueCount, IntPtr ClearValues);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkImageSubresourceLayers(uint AspectMask, uint MipLevel, uint BaseArrayLayer, uint LayerCount);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkBufferImageCopy(ulong BufferOffset, uint BufferRowLength, uint BufferImageHeight, VkImageSubresourceLayers ImageSubresource, VkOffset3D ImageOffset, VkExtent3D ImageExtent);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkFenceCreateInfo(int SType, IntPtr PNext, uint Flags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VkSubmitInfo(int SType, IntPtr PNext, uint WaitSemaphoreCount, IntPtr WaitSemaphores, IntPtr WaitDstStageMask, uint CommandBufferCount, IntPtr CommandBuffers, uint SignalSemaphoreCount, IntPtr SignalSemaphores);

    private readonly record struct VulkanRenderPassSubmissionContext(
        IntPtr Instance,
        VkDestroyInstance DestroyInstance,
        VkEnumeratePhysicalDevices EnumeratePhysicalDevices,
        VkGetPhysicalDeviceProperties GetPhysicalDeviceProperties,
        VkGetPhysicalDeviceQueueFamilyProperties GetPhysicalDeviceQueueFamilyProperties,
        VkGetPhysicalDeviceMemoryProperties GetPhysicalDeviceMemoryProperties,
        VkCreateDevice CreateDevice,
        VkDestroyDevice DestroyDevice,
        VkGetDeviceQueue GetDeviceQueue,
        VkCreateImage CreateImage,
        VkDestroyImage DestroyImage,
        VkGetImageMemoryRequirements GetImageMemoryRequirements,
        VkCreateBuffer CreateBuffer,
        VkDestroyBuffer DestroyBuffer,
        VkGetBufferMemoryRequirements GetBufferMemoryRequirements,
        VkAllocateMemory AllocateMemory,
        VkFreeMemory FreeMemory,
        VkBindImageMemory BindImageMemory,
        VkBindBufferMemory BindBufferMemory,
        VkMapMemory MapMemory,
        VkUnmapMemory UnmapMemory,
        VkCreateImageView CreateImageView,
        VkDestroyImageView DestroyImageView,
        VkCreateRenderPass CreateRenderPass,
        VkDestroyRenderPass DestroyRenderPass,
        VkCreateFramebuffer CreateFramebuffer,
        VkDestroyFramebuffer DestroyFramebuffer,
        VkCreateCommandPool CreateCommandPool,
        VkDestroyCommandPool DestroyCommandPool,
        VkAllocateCommandBuffers AllocateCommandBuffers,
        VkBeginCommandBuffer BeginCommandBuffer,
        VkCmdBeginRenderPass CmdBeginRenderPass,
        VkCmdEndRenderPass CmdEndRenderPass,
        VkCmdCopyImageToBuffer CmdCopyImageToBuffer,
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
