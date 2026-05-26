using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.OpenXR;
using Silk.NET.Vulkan;
using XrResult = Silk.NET.OpenXR.Result;
using VkResult = Silk.NET.Vulkan.Result;
using XrInstance = Silk.NET.OpenXR.Instance;
using VkInstance = Silk.NET.Vulkan.Instance;
using XrStructureType = Silk.NET.OpenXR.StructureType;

namespace Rekall.Age.Rendering;

public sealed unsafe class RekallAgeSilkOpenXrHeadsetClearSubmitter
{
    private const string VulkanEnable2Extension = "XR_KHR_vulkan_enable2";
    private const long VkFormatR8G8B8A8Srgb = 43;
    private const long VkFormatB8G8R8A8Srgb = 50;
    private const long XrInfiniteDuration = long.MaxValue;

    public ValueTask<RekallAgeOpenXrHeadsetClearSubmitResult> SubmitAsync(
        RekallAgeOpenXrHeadsetClearSubmitRequest request,
        CancellationToken cancellationToken)
    {
        var plan = RekallAgeOpenXrHeadsetSubmitPlanner.Plan(request);
        var errors = new List<string>();
        var instanceCreated = false;
        var vulkanInstanceCreated = false;
        var vulkanDeviceCreated = false;
        var sessionCreated = false;
        var referenceSpaceCreated = false;
        var swapchainCreated = false;
        var submittedFrames = 0;
        var recommendedWidth = 0;
        var recommendedHeight = 0;

        try
        {
            using var xr = XR.GetApi();
            using var vk = Vk.GetApi();
            var extensionName = SilkMarshal.StringToPtr(VulkanEnable2Extension);
            var appName = SilkMarshal.StringToPtr("Rekall AGE");
            var engineName = SilkMarshal.StringToPtr("Rekall AGE");
            try
            {
                var extensionNames = stackalloc byte*[1];
                extensionNames[0] = (byte*)extensionName;
                var createInfo = new Silk.NET.OpenXR.InstanceCreateInfo
                {
                    Type = XrStructureType.InstanceCreateInfo,
                    EnabledExtensionCount = 1,
                    EnabledExtensionNames = extensionNames,
                    ApplicationInfo = new Silk.NET.OpenXR.ApplicationInfo
                    {
                        ApplicationVersion = 1,
                        EngineVersion = 1,
                        ApiVersion = MakeOpenXrVersion(1, 0, 0)
                    }
                };
                CopyAscii((byte*)createInfo.ApplicationInfo.ApplicationName, 128, "Rekall AGE");
                CopyAscii((byte*)createInfo.ApplicationInfo.EngineName, 128, "Rekall AGE");

                XrInstance xrInstance;
                if (xr.CreateInstance(&createInfo, &xrInstance) != XrResult.Success)
                {
                    return ValueTask.FromResult(Fail(errors, "xrCreateInstance failed."));
                }

                instanceCreated = true;
                try
                {
                    var systemInfo = new SystemGetInfo
                    {
                        Type = XrStructureType.SystemGetInfo,
                        FormFactor = FormFactor.HeadMountedDisplay
                    };
                    ulong systemId;
                    if (xr.GetSystem(xrInstance, &systemInfo, &systemId) != XrResult.Success)
                    {
                        return ValueTask.FromResult(Fail(errors, "xrGetSystem did not return a head-mounted display."));
                    }

                    if (!TryLoadXrFunction(xr, xrInstance, "xrCreateVulkanInstanceKHR", out XrCreateVulkanInstanceKhr createVulkanInstance, errors)
                        || !TryLoadXrFunction(xr, xrInstance, "xrGetVulkanGraphicsDevice2KHR", out XrGetVulkanGraphicsDevice2Khr getVulkanGraphicsDevice, errors)
                        || !TryLoadXrFunction(xr, xrInstance, "xrCreateVulkanDeviceKHR", out XrCreateVulkanDeviceKhr createVulkanDevice, errors)
                        || !TryLoadXrFunction(xr, xrInstance, "xrGetVulkanGraphicsRequirements2KHR", out XrGetVulkanGraphicsRequirements2Khr getVulkanGraphicsRequirements, errors)
                        || !TryLoadXrFunction(xr, xrInstance, "xrEnumerateViewConfigurationViews", out XrEnumerateViewConfigurationViewsDelegate enumerateViewConfigurationViews, errors)
                        || !TryLoadXrFunction(xr, xrInstance, "xrLocateViews", out XrLocateViewsDelegate locateViews, errors))
                    {
                        return ValueTask.FromResult(Fail(errors));
                    }

                    var requirements = new GraphicsRequirementsVulkan2KHR
                    {
                        Type = XrStructureType.GraphicsRequirementsVulkanKhr
                    };
                    var requirementsResult = getVulkanGraphicsRequirements(xrInstance, systemId, &requirements);
                    if (requirementsResult != XrResult.Success)
                    {
                        return ValueTask.FromResult(Fail(errors, $"xrGetVulkanGraphicsRequirements2KHR failed with {requirementsResult}."));
                    }

                    var vkGetInstanceProcAddr = vk.GetInstanceProcAddr(default, "vkGetInstanceProcAddr");
                    var vkAppInfo = new Silk.NET.Vulkan.ApplicationInfo
                    {
                        SType = Silk.NET.Vulkan.StructureType.ApplicationInfo,
                        PApplicationName = (byte*)appName,
                        ApplicationVersion = 1,
                        PEngineName = (byte*)engineName,
                        EngineVersion = 1,
                        ApiVersion = Vk.Version11
                    };
                    var vkInstanceCreateInfo = new Silk.NET.Vulkan.InstanceCreateInfo
                    {
                        SType = Silk.NET.Vulkan.StructureType.InstanceCreateInfo,
                        PApplicationInfo = &vkAppInfo
                    };
                    var xrVkInstanceCreateInfo = new VulkanInstanceCreateInfoKHR
                    {
                        Type = XrStructureType.VulkanInstanceCreateInfoKhr,
                        SystemId = systemId,
                        PfnGetInstanceProcAddr = vkGetInstanceProcAddr,
                        VulkanCreateInfo = &vkInstanceCreateInfo
                    };
                    var xrCreateVkInstanceResult = createVulkanInstance(xrInstance, &xrVkInstanceCreateInfo, out var vkInstance, out var vkCreateInstanceResult);
                    if (xrCreateVkInstanceResult != XrResult.Success || vkCreateInstanceResult != VkResult.Success)
                    {
                        return ValueTask.FromResult(Fail(errors, $"xrCreateVulkanInstanceKHR failed xr={xrCreateVkInstanceResult} vk={vkCreateInstanceResult}."));
                    }

                    vulkanInstanceCreated = true;
                    try
                    {
                        var graphicsDeviceInfo = new VulkanGraphicsDeviceGetInfoKHR
                        {
                            Type = XrStructureType.VulkanGraphicsDeviceGetInfoKhr,
                            SystemId = systemId,
                            VulkanInstance = new VkHandle(vkInstance.Handle)
                        };
                        if (getVulkanGraphicsDevice(xrInstance, &graphicsDeviceInfo, out var physicalDevice) != XrResult.Success)
                        {
                            return ValueTask.FromResult(Fail(errors, "xrGetVulkanGraphicsDevice2KHR failed."));
                        }

                        var queueFamilyIndex = SelectGraphicsQueueFamily(vk, physicalDevice, errors);
                        if (queueFamilyIndex is null)
                        {
                            return ValueTask.FromResult(Fail(errors));
                        }

                        var priority = 1f;
                        var queueInfo = new DeviceQueueCreateInfo
                        {
                            SType = Silk.NET.Vulkan.StructureType.DeviceQueueCreateInfo,
                            QueueFamilyIndex = queueFamilyIndex.Value,
                            QueueCount = 1,
                            PQueuePriorities = &priority
                        };
                        var vkDeviceCreateInfo = new DeviceCreateInfo
                        {
                            SType = Silk.NET.Vulkan.StructureType.DeviceCreateInfo,
                            QueueCreateInfoCount = 1,
                            PQueueCreateInfos = &queueInfo
                        };
                        var xrVkDeviceCreateInfo = new VulkanDeviceCreateInfoKHR
                        {
                            Type = XrStructureType.VulkanDeviceCreateInfoKhr,
                            SystemId = systemId,
                            PfnGetInstanceProcAddr = vkGetInstanceProcAddr,
                            VulkanPhysicalDevice = new VkHandle(physicalDevice.Handle),
                            VulkanCreateInfo = &vkDeviceCreateInfo
                        };
                        var xrCreateVkDeviceResult = createVulkanDevice(xrInstance, &xrVkDeviceCreateInfo, out var vkDevice, out var vkCreateDeviceResult);
                        if (xrCreateVkDeviceResult != XrResult.Success || vkCreateDeviceResult != VkResult.Success)
                        {
                            return ValueTask.FromResult(Fail(errors, $"xrCreateVulkanDeviceKHR failed xr={xrCreateVkDeviceResult} vk={vkCreateDeviceResult}."));
                        }

                        vulkanDeviceCreated = true;
                        try
                        {
                            vk.GetDeviceQueue(vkDevice, queueFamilyIndex.Value, 0, out var queue);
                            var binding = new GraphicsBindingVulkan2KHR
                            {
                                Type = XrStructureType.GraphicsBindingVulkanKhr,
                                Instance = new VkHandle(vkInstance.Handle),
                                PhysicalDevice = new VkHandle(physicalDevice.Handle),
                                Device = new VkHandle(vkDevice.Handle),
                                QueueFamilyIndex = queueFamilyIndex.Value,
                                QueueIndex = 0
                            };
                            var sessionCreateInfo = new Silk.NET.OpenXR.SessionCreateInfo
                            {
                                Type = XrStructureType.SessionCreateInfo,
                                Next = &binding,
                                SystemId = systemId
                            };
                            Silk.NET.OpenXR.Session session;
                            var createSessionResult = xr.CreateSession(xrInstance, &sessionCreateInfo, &session);
                            if (createSessionResult != XrResult.Success)
                            {
                                return ValueTask.FromResult(Fail(errors, $"xrCreateSession failed for XR-created Vulkan device with {createSessionResult}."));
                            }

                            sessionCreated = true;
                            try
                            {
                                var spaceCreateInfo = new ReferenceSpaceCreateInfo
                                {
                                    Type = XrStructureType.ReferenceSpaceCreateInfo,
                                    ReferenceSpaceType = ReferenceSpaceType.Local,
                                    PoseInReferenceSpace = IdentityPose()
                                };
                                Space space;
                                if (xr.CreateReferenceSpace(session, &spaceCreateInfo, &space) != XrResult.Success)
                                {
                                    return ValueTask.FromResult(Fail(errors, "xrCreateReferenceSpace failed."));
                                }

                                referenceSpaceCreated = true;
                                try
                                {
                                    var viewConfigViews = stackalloc ViewConfigurationView[2];
                                    for (var i = 0; i < 2; i++)
                                    {
                                        viewConfigViews[i] = new ViewConfigurationView { Type = XrStructureType.ViewConfigurationView };
                                    }

                                    enumerateViewConfigurationViews(
                                        xrInstance,
                                        systemId,
                                        ViewConfigurationType.PrimaryStereo,
                                        2,
                                        out var viewCount,
                                        viewConfigViews);
                                    recommendedWidth = checked((int)Math.Max(1, viewConfigViews[0].RecommendedImageRectWidth));
                                    recommendedHeight = checked((int)Math.Max(1, viewConfigViews[0].RecommendedImageRectHeight));

                                    var format = SelectSwapchainFormat(xr, session, errors);
                                    if (format is null)
                                    {
                                        return ValueTask.FromResult(Fail(errors));
                                    }

                                    var swapchainCreateInfo = new SwapchainCreateInfo
                                    {
                                        Type = XrStructureType.SwapchainCreateInfo,
                                        UsageFlags = SwapchainUsageFlags.ColorAttachmentBit | SwapchainUsageFlags.TransferDstBit,
                                        Format = format.Value,
                                        SampleCount = 1,
                                        Width = (uint)recommendedWidth,
                                        Height = (uint)recommendedHeight,
                                        FaceCount = 1,
                                        ArraySize = 2,
                                        MipCount = 1
                                    };
                                    Swapchain swapchain;
                                    if (xr.CreateSwapchain(session, &swapchainCreateInfo, &swapchain) != XrResult.Success)
                                    {
                                        return ValueTask.FromResult(Fail(errors, "xrCreateSwapchain failed."));
                                    }

                                    swapchainCreated = true;
                                    try
                                    {
                                        var images = EnumerateSwapchainImages(xr, swapchain, errors);
                                        if (images.Length == 0)
                                        {
                                            return ValueTask.FromResult(Fail(errors));
                                        }

                                        using var vkFrameResources = VulkanClearFrameResources.Create(vk, vkDevice, queueFamilyIndex.Value, errors);
                                        if (vkFrameResources is null)
                                        {
                                            return ValueTask.FromResult(Fail(errors));
                                        }

                                        if (!BeginSessionWhenReady(xr, xrInstance, session, errors))
                                        {
                                            return ValueTask.FromResult(Fail(errors));
                                        }

                                        submittedFrames = SubmitFrames(
                                            xr,
                                            vk,
                                            session,
                                            space,
                                            swapchain,
                                            images,
                                            locateViews,
                                            queue,
                                            vkFrameResources,
                                            plan,
                                            recommendedWidth,
                                            recommendedHeight,
                                            cancellationToken,
                                            errors);
                                        var submitted = submittedFrames > 0 && errors.Count == 0;
                                        return ValueTask.FromResult(new RekallAgeOpenXrHeadsetClearSubmitResult(
                                            submitted,
                                            instanceCreated,
                                            vulkanInstanceCreated,
                                            vulkanDeviceCreated,
                                            sessionCreated,
                                            referenceSpaceCreated,
                                            swapchainCreated,
                                            submittedFrames,
                                            recommendedWidth,
                                            recommendedHeight,
                                            errors));
                                    }
                                    finally
                                    {
                                        xr.DestroySwapchain(swapchain);
                                    }
                                }
                                finally
                                {
                                    xr.DestroySpace(space);
                                }
                            }
                            finally
                            {
                                xr.DestroySession(session);
                            }
                        }
                        finally
                        {
                            vk.DestroyDevice(vkDevice, null);
                        }
                    }
                    finally
                    {
                        vk.DestroyInstance(vkInstance, null);
                    }
                }
                finally
                {
                    xr.DestroyInstance(xrInstance);
                }
            }
            finally
            {
                SilkMarshal.Free(extensionName);
                SilkMarshal.Free(appName);
                SilkMarshal.Free(engineName);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or SEHException or AccessViolationException or InvalidOperationException)
        {
            errors.Add($"OpenXR headset clear submit failed: {ex.Message}");
        }

        return ValueTask.FromResult(new RekallAgeOpenXrHeadsetClearSubmitResult(
            false,
            instanceCreated,
            vulkanInstanceCreated,
            vulkanDeviceCreated,
            sessionCreated,
            referenceSpaceCreated,
            swapchainCreated,
            submittedFrames,
            recommendedWidth,
            recommendedHeight,
            errors));
    }

    private static int SubmitFrames(
        XR xr,
        Vk vk,
        Silk.NET.OpenXR.Session session,
        Space space,
        Swapchain swapchain,
        Image[] images,
        XrLocateViewsDelegate locateViews,
        Queue queue,
        VulkanClearFrameResources vkFrameResources,
        RekallAgeOpenXrHeadsetClearSubmitPlan plan,
        int width,
        int height,
        CancellationToken cancellationToken,
        List<string> errors)
    {
        var submitted = 0;
        var views = stackalloc View[2];
        var projectionViews = stackalloc CompositionLayerProjectionView[2];
        var layerPointers = stackalloc CompositionLayerBaseHeader*[1];
        for (var frame = 0; frame < plan.FrameCount; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            views[0] = new View { Type = XrStructureType.View };
            views[1] = new View { Type = XrStructureType.View };
            var waitInfo = new FrameWaitInfo { Type = XrStructureType.FrameWaitInfo };
            var frameState = new FrameState { Type = XrStructureType.FrameState };
            if (xr.WaitFrame(session, &waitInfo, ref frameState) != XrResult.Success)
            {
                errors.Add("xrWaitFrame failed.");
                break;
            }

            var beginInfo = new FrameBeginInfo { Type = XrStructureType.FrameBeginInfo };
            if (xr.BeginFrame(session, &beginInfo) != XrResult.Success)
            {
                errors.Add("xrBeginFrame failed.");
                break;
            }

            var locateInfo = new ViewLocateInfo
            {
                Type = XrStructureType.ViewLocateInfo,
                ViewConfigurationType = ViewConfigurationType.PrimaryStereo,
                DisplayTime = frameState.PredictedDisplayTime,
                Space = space
            };
            var viewState = new ViewState { Type = XrStructureType.ViewState };
            var locateResult = locateViews(session, &locateInfo, &viewState, 2, out var viewCount, views);
            if (locateResult != XrResult.Success || viewCount < 2)
            {
                errors.Add($"xrLocateViews failed or returned fewer than 2 views ({locateResult}, {viewCount}).");
                EndEmptyFrame(xr, session, frameState.PredictedDisplayTime);
                break;
            }

            var acquireInfo = new SwapchainImageAcquireInfo { Type = XrStructureType.SwapchainImageAcquireInfo };
            uint imageIndex;
            if (xr.AcquireSwapchainImage(swapchain, &acquireInfo, &imageIndex) != XrResult.Success)
            {
                errors.Add("xrAcquireSwapchainImage failed.");
                EndEmptyFrame(xr, session, frameState.PredictedDisplayTime);
                break;
            }

            var imageWaitInfo = new SwapchainImageWaitInfo
            {
                Type = XrStructureType.SwapchainImageWaitInfo,
                Timeout = XrInfiniteDuration
            };
            if (xr.WaitSwapchainImage(swapchain, &imageWaitInfo) != XrResult.Success)
            {
                errors.Add("xrWaitSwapchainImage failed.");
            }
            else
            {
                var selectedImage = images[Math.Min((int)imageIndex, images.Length - 1)];
                ClearSwapchainImage(vk, queue, vkFrameResources, selectedImage, plan);
            }

            var releaseInfo = new SwapchainImageReleaseInfo { Type = XrStructureType.SwapchainImageReleaseInfo };
            if (xr.ReleaseSwapchainImage(swapchain, &releaseInfo) != XrResult.Success)
            {
                errors.Add("xrReleaseSwapchainImage failed.");
            }

            for (var eye = 0; eye < 2; eye++)
            {
                projectionViews[eye] = new CompositionLayerProjectionView
                {
                    Type = XrStructureType.CompositionLayerProjectionView,
                    Pose = views[eye].Pose,
                    Fov = views[eye].Fov,
                    SubImage = new SwapchainSubImage
                    {
                        Swapchain = swapchain,
                        ImageRect = new Rect2Di(
                            new Offset2Di(0, 0),
                            new Extent2Di(width, height)),
                        ImageArrayIndex = (uint)eye
                    }
                };
            }

            var projection = new CompositionLayerProjection
            {
                Type = XrStructureType.CompositionLayerProjection,
                Space = space,
                ViewCount = 2,
                Views = projectionViews
            };
            layerPointers[0] = (CompositionLayerBaseHeader*)&projection;
            var endInfo = new FrameEndInfo
            {
                Type = XrStructureType.FrameEndInfo,
                DisplayTime = frameState.PredictedDisplayTime,
                EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
                LayerCount = 1,
                Layers = layerPointers
            };
            if (xr.EndFrame(session, &endInfo) != XrResult.Success)
            {
                errors.Add("xrEndFrame failed.");
                break;
            }

            submitted++;
        }

        return submitted;
    }

    private static void EndEmptyFrame(XR xr, Silk.NET.OpenXR.Session session, long displayTime)
    {
        var endInfo = new FrameEndInfo
        {
            Type = XrStructureType.FrameEndInfo,
            DisplayTime = displayTime,
            EnvironmentBlendMode = EnvironmentBlendMode.Opaque
        };
        xr.EndFrame(session, &endInfo);
    }

    private static bool BeginSessionWhenReady(XR xr, XrInstance instance, Silk.NET.OpenXR.Session session, List<string> errors)
    {
        var lastState = SessionState.Unknown;
        for (var attempt = 0; attempt < 180; attempt++)
        {
            var buffer = new EventDataBuffer { Type = XrStructureType.EventDataBuffer };
            var result = xr.PollEvent(instance, &buffer);
            if (result == XrResult.EventUnavailable)
            {
                Thread.Sleep(16);
                continue;
            }

            if (result != XrResult.Success)
            {
                errors.Add($"xrPollEvent failed with {result}.");
                return false;
            }

            if (buffer.Type != XrStructureType.EventDataSessionStateChanged)
            {
                continue;
            }

            var stateChanged = *(EventDataSessionStateChanged*)&buffer;
            if (stateChanged.Session.Handle != session.Handle)
            {
                continue;
            }

            lastState = stateChanged.State;
            if (stateChanged.State == SessionState.Ready)
            {
                var beginInfo = new SessionBeginInfo
                {
                    Type = XrStructureType.SessionBeginInfo,
                    PrimaryViewConfigurationType = ViewConfigurationType.PrimaryStereo
                };
                var beginResult = xr.BeginSession(session, &beginInfo);
                if (beginResult != XrResult.Success)
                {
                    errors.Add($"xrBeginSession failed with {beginResult}.");
                    return false;
                }

                return true;
            }
        }

        errors.Add($"OpenXR session did not reach READY; last state was {lastState}.");
        return false;
    }

    private static Image[] EnumerateSwapchainImages(XR xr, Swapchain swapchain, List<string> errors)
    {
        uint count;
        var countResult = xr.EnumerateSwapchainImages(swapchain, 0, &count, null);
        if (countResult != XrResult.Success || count == 0)
        {
            errors.Add($"xrEnumerateSwapchainImages count failed with {countResult}.");
            return [];
        }

        var imageHeaders = new SwapchainImageVulkan2KHR[count];
        for (var i = 0; i < imageHeaders.Length; i++)
        {
            imageHeaders[i] = new SwapchainImageVulkan2KHR { Type = XrStructureType.SwapchainImageVulkanKhr };
        }

        fixed (SwapchainImageVulkan2KHR* imagePointer = imageHeaders)
        {
            uint written;
            var result = xr.EnumerateSwapchainImages(
                swapchain,
                count,
                &written,
                (SwapchainImageBaseHeader*)imagePointer);
            if (result != XrResult.Success || written == 0)
            {
                errors.Add($"xrEnumerateSwapchainImages failed with {result}.");
                return [];
            }

            return imageHeaders
                .Take((int)Math.Min(count, written))
                .Select(image => new Image(image.Image))
                .ToArray();
        }
    }

    private static long? SelectSwapchainFormat(XR xr, Silk.NET.OpenXR.Session session, List<string> errors)
    {
        uint count;
        var countResult = xr.EnumerateSwapchainFormats(session, 0, &count, null);
        if (countResult != XrResult.Success || count == 0)
        {
            errors.Add($"xrEnumerateSwapchainFormats count failed with {countResult}.");
            return null;
        }

        var formats = new long[count];
        fixed (long* formatPointer = formats)
        {
            uint written;
            var result = xr.EnumerateSwapchainFormats(session, count, &written, formatPointer);
            if (result != XrResult.Success || written == 0)
            {
                errors.Add($"xrEnumerateSwapchainFormats failed with {result}.");
                return null;
            }
        }

        if (formats.Contains(VkFormatR8G8B8A8Srgb))
        {
            return VkFormatR8G8B8A8Srgb;
        }

        if (formats.Contains(VkFormatB8G8R8A8Srgb))
        {
            return VkFormatB8G8R8A8Srgb;
        }

        return formats[0];
    }

    private static uint? SelectGraphicsQueueFamily(Vk vk, PhysicalDevice physicalDevice, List<string> errors)
    {
        uint count;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, null);
        if (count == 0)
        {
            errors.Add("Vulkan physical device exposed no queue families.");
            return null;
        }

        var properties = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* propertyPointer = properties)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, propertyPointer);
        }

        for (uint i = 0; i < properties.Length; i++)
        {
            if ((properties[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                return i;
            }
        }

        errors.Add("Vulkan physical device exposed no graphics queue family.");
        return null;
    }

    private static void ClearSwapchainImage(
        Vk vk,
        Queue queue,
        VulkanClearFrameResources resources,
        Image image,
        RekallAgeOpenXrHeadsetClearSubmitPlan plan)
    {
        vk.ResetCommandBuffer(resources.CommandBuffer, 0);
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = Silk.NET.Vulkan.StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        vk.BeginCommandBuffer(resources.CommandBuffer, &beginInfo);
        var range = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 2
        };
        var toTransfer = new ImageMemoryBarrier
        {
            SType = Silk.NET.Vulkan.StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range,
            DstAccessMask = AccessFlags.TransferWriteBit
        };
        vk.CmdPipelineBarrier(
            resources.CommandBuffer,
            PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.TransferBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &toTransfer);
        var color = new ClearColorValue(plan.Red, plan.Green, plan.Blue, plan.Alpha);
        vk.CmdClearColorImage(resources.CommandBuffer, image, ImageLayout.TransferDstOptimal, &color, 1, &range);
        var toColorAttachment = new ImageMemoryBarrier
        {
            SType = Silk.NET.Vulkan.StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.TransferDstOptimal,
            NewLayout = ImageLayout.ColorAttachmentOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ColorAttachmentReadBit
        };
        vk.CmdPipelineBarrier(
            resources.CommandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.ColorAttachmentOutputBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &toColorAttachment);
        vk.EndCommandBuffer(resources.CommandBuffer);
        var commandBuffer = resources.CommandBuffer;
        var submitInfo = new SubmitInfo
        {
            SType = Silk.NET.Vulkan.StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };
        vk.QueueSubmit(queue, 1, &submitInfo, default);
        vk.QueueWaitIdle(queue);
    }

    private static bool TryLoadXrFunction<TDelegate>(
        XR xr,
        XrInstance instance,
        string name,
        out TDelegate function,
        List<string> errors)
        where TDelegate : Delegate
    {
        var namePointer = (byte*)SilkMarshal.StringToPtr(name);
        PfnVoidFunction pointer;
        var result = xr.GetInstanceProcAddr(instance, namePointer, &pointer);
        SilkMarshal.Free((nint)namePointer);
        if (result != XrResult.Success || pointer.Handle == null)
        {
            function = null!;
            errors.Add($"{name} was not available from xrGetInstanceProcAddr ({result}).");
            return false;
        }

        function = Marshal.GetDelegateForFunctionPointer<TDelegate>((nint)pointer.Handle);
        return true;
    }

    private static RekallAgeOpenXrHeadsetClearSubmitResult Fail(List<string> errors, string? error = null)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            errors.Add(error);
        }

        return new RekallAgeOpenXrHeadsetClearSubmitResult(false, false, false, false, false, false, false, 0, 0, 0, errors);
    }

    private static Posef IdentityPose()
    {
        return new Posef(
            new Quaternionf(0, 0, 0, 1),
            new Vector3f(0, 0, 0));
    }

    private static ulong MakeOpenXrVersion(uint major, uint minor, uint patch)
    {
        return ((ulong)major << 48) | ((ulong)minor << 32) | patch;
    }

    private static void CopyAscii(byte* destination, int destinationLength, string value)
    {
        var length = Math.Min(destinationLength - 1, value.Length);
        for (var i = 0; i < length; i++)
        {
            destination[i] = (byte)value[i];
        }

        destination[length] = 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate XrResult XrCreateVulkanInstanceKhr(
        XrInstance instance,
        VulkanInstanceCreateInfoKHR* createInfo,
        out VkInstance vulkanInstance,
        out VkResult vulkanResult);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate XrResult XrGetVulkanGraphicsDevice2Khr(
        XrInstance instance,
        VulkanGraphicsDeviceGetInfoKHR* getInfo,
        out PhysicalDevice vulkanPhysicalDevice);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate XrResult XrCreateVulkanDeviceKhr(
        XrInstance instance,
        VulkanDeviceCreateInfoKHR* createInfo,
        out Device vulkanDevice,
        out VkResult vulkanResult);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate XrResult XrGetVulkanGraphicsRequirements2Khr(
        XrInstance instance,
        ulong systemId,
        GraphicsRequirementsVulkan2KHR* graphicsRequirements);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate XrResult XrEnumerateViewConfigurationViewsDelegate(
        XrInstance instance,
        ulong systemId,
        ViewConfigurationType viewConfigurationType,
        uint viewCapacityInput,
        out uint viewCountOutput,
        ViewConfigurationView* views);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate XrResult XrLocateViewsDelegate(
        Silk.NET.OpenXR.Session session,
        ViewLocateInfo* viewLocateInfo,
        ViewState* viewState,
        uint viewCapacityInput,
        out uint viewCountOutput,
        View* views);

    private sealed class VulkanClearFrameResources : IDisposable
    {
        private readonly Vk _vk;
        private readonly Device _device;
        private readonly CommandPool _commandPool;

        private VulkanClearFrameResources(Vk vk, Device device, CommandPool commandPool, CommandBuffer commandBuffer)
        {
            _vk = vk;
            _device = device;
            _commandPool = commandPool;
            CommandBuffer = commandBuffer;
        }

        public CommandBuffer CommandBuffer { get; }

        public static VulkanClearFrameResources? Create(Vk vk, Device device, uint queueFamilyIndex, List<string> errors)
        {
            var commandPoolInfo = new CommandPoolCreateInfo
            {
                SType = Silk.NET.Vulkan.StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
                QueueFamilyIndex = queueFamilyIndex
            };
            if (vk.CreateCommandPool(device, &commandPoolInfo, null, out var commandPool) != VkResult.Success)
            {
                errors.Add("vkCreateCommandPool failed.");
                return null;
            }

            var allocateInfo = new CommandBufferAllocateInfo
            {
                SType = Silk.NET.Vulkan.StructureType.CommandBufferAllocateInfo,
                CommandPool = commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1
            };
            if (vk.AllocateCommandBuffers(device, &allocateInfo, out var commandBuffer) != VkResult.Success)
            {
                vk.DestroyCommandPool(device, commandPool, null);
                errors.Add("vkAllocateCommandBuffers failed.");
                return null;
            }

            return new VulkanClearFrameResources(vk, device, commandPool, commandBuffer);
        }

        public void Dispose()
        {
            _vk.DestroyCommandPool(_device, _commandPool, null);
        }
    }
}


