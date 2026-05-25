using System.Numerics;
using System.Runtime.InteropServices;
using Rekall.Age.Rendering.Abstractions;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeNativeVulkanSceneCapture : IRekallAgeVulkanSceneCapture
{
    private readonly IRekallAgeVulkanRenderPassCapture _clearCapture;

    public RekallAgeNativeVulkanSceneCapture()
        : this(new RekallAgeNativeVulkanRenderPassSubmission())
    {
    }

    public RekallAgeNativeVulkanSceneCapture(IRekallAgeVulkanRenderPassCapture clearCapture)
    {
        _clearCapture = clearCapture;
    }

    public async ValueTask<RekallAgeVulkanSceneCaptureResult> CaptureSceneAsync(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        string outputDirectory,
        string? preferredDeviceType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = Validate(frame, outputDirectory);
        if (errors.Count > 0)
        {
            return Unavailable(frame, string.Empty, null, null, assets, 0, 0, 0, [], errors);
        }

        var unsupportedRenderables = frame.Renderables
            .Where(renderable => !IsSupportedRenderable(renderable, assets))
            .ToArray();
        var unsupportedKinds = unsupportedRenderables
            .Select(renderable => renderable.Kind)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(kind => kind, StringComparer.Ordinal)
            .ToArray();
        if (unsupportedKinds.Length > 0)
        {
            errors.Add($"Vulkan scene capture does not yet support renderable kinds: {string.Join(", ", unsupportedKinds)}.");
            return Unavailable(frame, string.Empty, null, null, assets, 0, 0, unsupportedRenderables.Length, unsupportedKinds, errors);
        }

        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets);
        if (meshes.Count == 0)
        {
            var clear = await _clearCapture.CaptureClearRenderPassAsync(
                checked((uint)frame.Width),
                checked((uint)frame.Height),
                "R8G8B8A8_UNorm",
                preferredDeviceType,
                outputDirectory,
                RekallAgeVulkanClearColor.Default,
                cancellationToken);
            return FromClearCapture(frame, assets, clear);
        }

        return VulkanSceneRenderer.TryCapture(frame, assets, meshes, outputDirectory, preferredDeviceType, cancellationToken);
    }

    private static bool IsSupportedRenderable(
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRuntimeViewportAssetSet assets)
    {
        if (renderable.Kind.Equals("light", StringComparison.Ordinal)
            || renderable.Kind.Equals("ui", StringComparison.Ordinal))
        {
            return true;
        }

        return RekallAgeVulkanSceneMeshBuilder.IsSupportedMeshRenderable(renderable)
            || (renderable.Kind.Equals("mesh", StringComparison.Ordinal)
                && renderable.AssetId is not null
                && assets.Models.ContainsKey(renderable.AssetId));
    }

    private static List<string> Validate(RekallAgeRuntimeViewportFrame frame, string outputDirectory)
    {
        var errors = new List<string>();
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            errors.Add("Vulkan scene capture width and height must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            errors.Add("Vulkan scene capture output directory is required.");
        }

        return errors;
    }

    private static RekallAgeVulkanSceneCaptureResult FromClearCapture(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        RekallAgeVulkanRenderPassCaptureResult clear)
    {
        return new RekallAgeVulkanSceneCaptureResult(
            clear.Captured,
            clear.OutputPath,
            clear.LoaderName,
            clear.SelectedDevice,
            clear.Width,
            clear.Height,
            clear.Format,
            clear.BytesRead,
            clear.NonZeroBytes,
            clear.FirstPixel,
            clear.ByteChecksum,
            DrawCallCount: 0,
            MeshCount: 0,
            SpriteCount: frame.Renderables.Count(renderable => renderable.Kind.Equals("sprite", StringComparison.Ordinal)),
            UnsupportedRenderableCount: 0,
            UnsupportedRenderableKinds: [],
            ColorTargetCreated: clear.Captured,
            DepthTargetCreated: false,
            RenderPassCreated: clear.Captured,
            FramebufferCreated: clear.Captured,
            VertexBufferCreated: false,
            IndexBufferCreated: false,
            UniformBufferCreated: false,
            DescriptorSetLayoutCreated: false,
            PipelineLayoutCreated: false,
            GraphicsPipelineCreated: false,
            TextureResourcesCreated: assets.Images.Count > 0,
            Errors: clear.Errors);
    }

    private static RekallAgeVulkanSceneCaptureResult Unavailable(
        RekallAgeRuntimeViewportFrame frame,
        string outputPath,
        string? loaderName,
        RekallAgeVulkanSelectedDevice? selectedDevice,
        RekallAgeRuntimeViewportAssetSet assets,
        int meshCount,
        int drawCallCount,
        int unsupportedCount,
        IReadOnlyList<string> unsupportedKinds,
        IReadOnlyList<string> errors)
    {
        return new RekallAgeVulkanSceneCaptureResult(
            false,
            outputPath,
            loaderName,
            selectedDevice,
            checked((uint)Math.Max(0, frame.Width)),
            checked((uint)Math.Max(0, frame.Height)),
            "R8G8B8A8_UNorm",
            0,
            0,
            default,
            0,
            drawCallCount,
            meshCount,
            frame.Renderables.Count(renderable => renderable.Kind.Equals("sprite", StringComparison.Ordinal)),
            unsupportedCount,
            unsupportedKinds,
            ColorTargetCreated: false,
            DepthTargetCreated: false,
            RenderPassCreated: false,
            FramebufferCreated: false,
            VertexBufferCreated: false,
            IndexBufferCreated: false,
            UniformBufferCreated: false,
            DescriptorSetLayoutCreated: false,
            PipelineLayoutCreated: false,
            GraphicsPipelineCreated: false,
            TextureResourcesCreated: assets.Images.Count > 0,
            Errors: errors);
    }

    private static unsafe class VulkanSceneRenderer
    {
        private const ulong FenceTimeoutNanoseconds = 5_000_000_000;

        public static RekallAgeVulkanSceneCaptureResult TryCapture(
            RekallAgeRuntimeViewportFrame frame,
            RekallAgeRuntimeViewportAssetSet assets,
            IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
            string outputDirectory,
            string? preferredDeviceType,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();
            var state = new VulkanState(Vk.GetApi());
            var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);
            var gpuVertices = BuildGpuVertices(batch.Vertices);
            var indices = batch.Indices.ToArray();
            var drawRanges = batch.Draws
                .Select(draw => new DrawRange(
                    draw.FirstIndex,
                    draw.IndexCount,
                    draw.VertexOffset,
                    ToGpuDrawPushConstants(draw.Model, draw.MaterialFactors),
                    draw.TextureId,
                    draw.MetallicRoughnessTextureId,
                    draw.NormalTextureId,
                    draw.OcclusionTextureId))
                .ToArray();

            if (gpuVertices.Length == 0 || indices.Length == 0)
            {
                errors.Add("Vulkan scene capture could not build drawable mesh buffers.");
                return Unavailable(frame, string.Empty, null, null, assets, meshes.Count, 0, 0, [], errors);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                CreateInstance(state);
                SelectPhysicalDevice(state, preferredDeviceType, errors);
                if (state.PhysicalDevice.Handle == 0)
                {
                    return Unavailable(frame, string.Empty, "Silk.NET Vulkan", null, assets, meshes.Count, 0, 0, [], errors);
                }

                CreateDevice(state);
                CreateImage(state, checked((uint)frame.Width), checked((uint)frame.Height), Format.R8G8B8A8Unorm, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit, ImageAspectFlags.ColorBit, out state.ColorImage, out state.ColorMemory, out state.ColorView);
                CreateImage(state, checked((uint)frame.Width), checked((uint)frame.Height), Format.D32Sfloat, ImageUsageFlags.DepthStencilAttachmentBit, ImageAspectFlags.DepthBit, out state.DepthImage, out state.DepthMemory, out state.DepthView);
                CreateRenderPass(state);
                CreateFramebuffer(state, checked((uint)frame.Width), checked((uint)frame.Height));
                CreateBuffers(state, batch.Frame, gpuVertices, indices, checked((ulong)frame.Width * (ulong)frame.Height * 4));
                CreateTextures(state, meshes);
                CreateDescriptors(state, drawRanges);
                if (!TryCompileSceneShaders(errors, out var shaders))
                {
                    return Unavailable(frame, string.Empty, "Silk.NET Vulkan", state.SelectedDevice, assets, meshes.Count, 0, 0, [], errors) with
                    {
                        ColorTargetCreated = state.ColorImage.Handle != 0,
                        DepthTargetCreated = state.DepthImage.Handle != 0,
                        RenderPassCreated = state.RenderPass.Handle != 0,
                        FramebufferCreated = state.Framebuffer.Handle != 0,
                        VertexBufferCreated = state.VertexBuffer.Handle != 0,
                        IndexBufferCreated = state.IndexBuffer.Handle != 0,
                        UniformBufferCreated = state.UniformBuffer.Handle != 0,
                        DescriptorSetLayoutCreated = state.DescriptorSetLayout.Handle != 0
                    };
                }

                CreatePipeline(state, frame, shaders);
                CreateCommandPoolAndBuffer(state);
                RecordCommands(state, checked((uint)frame.Width), checked((uint)frame.Height), drawRanges);
                SubmitAndWait(state);

                var rgba = ReadBack(state, checked((ulong)frame.Width * (ulong)frame.Height * 4));
                Directory.CreateDirectory(outputDirectory);
                var outputPath = Path.Combine(outputDirectory, $"vulkan-scene-{frame.Width}x{frame.Height}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png");
                RekallAgePngWriter.WriteRgbaAsync(outputPath, frame.Width, frame.Height, rgba, cancellationToken).AsTask().GetAwaiter().GetResult();

                var (nonZero, firstPixel, checksum) = Analyze(rgba);
                return new RekallAgeVulkanSceneCaptureResult(
                    true,
                    outputPath,
                    "Silk.NET Vulkan",
                    state.SelectedDevice,
                    checked((uint)frame.Width),
                    checked((uint)frame.Height),
                    "R8G8B8A8_UNorm",
                    checked((ulong)rgba.Length),
                    nonZero,
                    firstPixel,
                    checksum,
                    drawRanges.Length,
                    meshes.Count,
                    frame.Renderables.Count(renderable => renderable.Kind.Equals("sprite", StringComparison.Ordinal)),
                    0,
                    [],
                    ColorTargetCreated: true,
                    DepthTargetCreated: true,
                    RenderPassCreated: true,
                    FramebufferCreated: true,
                    VertexBufferCreated: true,
                    IndexBufferCreated: true,
                    UniformBufferCreated: true,
                    DescriptorSetLayoutCreated: true,
                    PipelineLayoutCreated: true,
                    GraphicsPipelineCreated: true,
                    TextureResourcesCreated: state.TextureById.Count > 0,
                    Errors: []);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(ex.Message);
                return Unavailable(frame, string.Empty, "Silk.NET Vulkan", state.SelectedDevice, assets, meshes.Count, 0, 0, [], errors) with
                {
                    ColorTargetCreated = state.ColorImage.Handle != 0,
                    DepthTargetCreated = state.DepthImage.Handle != 0,
                    RenderPassCreated = state.RenderPass.Handle != 0,
                    FramebufferCreated = state.Framebuffer.Handle != 0,
                    VertexBufferCreated = state.VertexBuffer.Handle != 0,
                    IndexBufferCreated = state.IndexBuffer.Handle != 0,
                    UniformBufferCreated = state.UniformBuffer.Handle != 0,
                    DescriptorSetLayoutCreated = state.DescriptorSetLayout.Handle != 0,
                    PipelineLayoutCreated = state.PipelineLayout.Handle != 0,
                    GraphicsPipelineCreated = state.Pipeline.Handle != 0
                };
            }
            finally
            {
                state.Dispose();
            }
        }

        private static void CreateInstance(VulkanState state)
        {
            var appNameBytes = "Rekall AGE\0"u8.ToArray();
            fixed (byte* appName = appNameBytes)
            {
                var applicationInfo = new ApplicationInfo
                {
                    SType = StructureType.ApplicationInfo,
                    PApplicationName = appName,
                    ApplicationVersion = 1,
                    PEngineName = appName,
                    EngineVersion = 1,
                    ApiVersion = Vk.Version10
                };
                var createInfo = new InstanceCreateInfo
                {
                    SType = StructureType.InstanceCreateInfo,
                    PApplicationInfo = &applicationInfo
                };
                ThrowIfFailed(state.Vk.CreateInstance(&createInfo, null, out state.Instance), "vkCreateInstance");
            }
        }

        private static void SelectPhysicalDevice(VulkanState state, string? preferredDeviceType, List<string> errors)
        {
            uint deviceCount = 0;
            ThrowIfFailed(state.Vk.EnumeratePhysicalDevices(state.Instance, &deviceCount, null), "vkEnumeratePhysicalDevices");
            if (deviceCount == 0)
            {
                errors.Add("No Vulkan physical devices were found.");
                return;
            }

            var devices = stackalloc PhysicalDevice[checked((int)deviceCount)];
            ThrowIfFailed(state.Vk.EnumeratePhysicalDevices(state.Instance, &deviceCount, devices), "vkEnumeratePhysicalDevices");

            DeviceCandidate? selected = null;
            for (var i = 0; i < deviceCount; i++)
            {
                var candidate = ReadCandidate(state, devices[i]);
                if (candidate.QueueFamily is null)
                {
                    continue;
                }

                selected ??= candidate;
                if (MatchesPreference(candidate, preferredDeviceType))
                {
                    selected = candidate;
                    break;
                }
            }

            if (selected is null || selected.Value.QueueFamily is null)
            {
                errors.Add("No Vulkan physical device with a graphics queue was found.");
                return;
            }

            state.PhysicalDevice = selected.Value.Device;
            state.GraphicsQueueFamily = selected.Value.QueueFamily.Value;
            state.SelectedDevice = new RekallAgeVulkanSelectedDevice(
                selected.Value.Name,
                ToDeviceTypeName(selected.Value.DeviceType),
                FormatVulkanVersion(selected.Value.ApiVersion),
                new RekallAgeVulkanQueueFamilyInfo(state.GraphicsQueueFamily, ["graphics"], 1));
        }

        private static DeviceCandidate ReadCandidate(VulkanState state, PhysicalDevice physicalDevice)
        {
            state.Vk.GetPhysicalDeviceProperties(physicalDevice, out var properties);
            uint queueCount = 0;
            state.Vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueCount, null);
            var queueFamilies = stackalloc QueueFamilyProperties[checked((int)queueCount)];
            state.Vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueCount, queueFamilies);
            uint? graphicsFamily = null;
            for (uint index = 0; index < queueCount; index++)
            {
                if ((queueFamilies[index].QueueFlags & QueueFlags.GraphicsBit) != 0)
                {
                    graphicsFamily = index;
                    break;
                }
            }

            return new DeviceCandidate(
                physicalDevice,
                ReadDeviceName(properties),
                properties.DeviceType,
                properties.ApiVersion,
                graphicsFamily);
        }

        private static void CreateDevice(VulkanState state)
        {
            var priority = 1f;
            var queueCreateInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = state.GraphicsQueueFamily,
                QueueCount = 1,
                PQueuePriorities = &priority
            };
            var deviceCreateInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueCreateInfo
            };
            ThrowIfFailed(state.Vk.CreateDevice(state.PhysicalDevice, &deviceCreateInfo, null, out state.Device), "vkCreateDevice");
            state.Vk.GetDeviceQueue(state.Device, state.GraphicsQueueFamily, 0, out state.GraphicsQueue);
        }

        private static void CreateImage(
            VulkanState state,
            uint width,
            uint height,
            Format format,
            ImageUsageFlags usage,
            ImageAspectFlags aspect,
            out Image image,
            out DeviceMemory memory,
            out ImageView view)
        {
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(width, height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };
            ThrowIfFailed(state.Vk.CreateImage(state.Device, &imageInfo, null, out image), "vkCreateImage");
            state.Vk.GetImageMemoryRequirements(state.Device, image, out var requirements);
            AllocateAndBindImage(state, image, requirements, MemoryPropertyFlags.DeviceLocalBit, out memory);

            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type2D,
                Format = format,
                Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity),
                SubresourceRange = new ImageSubresourceRange(aspect, 0, 1, 0, 1)
            };
            ThrowIfFailed(state.Vk.CreateImageView(state.Device, &viewInfo, null, out view), "vkCreateImageView");
        }

        private static void CreateRenderPass(VulkanState state)
        {
            var colorAttachment = new AttachmentDescription
            {
                Format = Format.R8G8B8A8Unorm,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.TransferSrcOptimal
            };
            var depthAttachment = new AttachmentDescription
            {
                Format = Format.D32Sfloat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.DontCare,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
            };
            var attachments = stackalloc AttachmentDescription[] { colorAttachment, depthAttachment };
            var colorReference = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
            var depthReference = new AttachmentReference(1, ImageLayout.DepthStencilAttachmentOptimal);
            var subpass = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &colorReference,
                PDepthStencilAttachment = &depthReference
            };
            var dependency = new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
            };
            var renderPassInfo = new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = 2,
                PAttachments = attachments,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = 1,
                PDependencies = &dependency
            };
            ThrowIfFailed(state.Vk.CreateRenderPass(state.Device, &renderPassInfo, null, out state.RenderPass), "vkCreateRenderPass");
        }

        private static void CreateFramebuffer(VulkanState state, uint width, uint height)
        {
            var attachments = stackalloc ImageView[] { state.ColorView, state.DepthView };
            var createInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = state.RenderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = width,
                Height = height,
                Layers = 1
            };
            ThrowIfFailed(state.Vk.CreateFramebuffer(state.Device, &createInfo, null, out state.Framebuffer), "vkCreateFramebuffer");
        }

        private static void CreateBuffers(
            VulkanState state,
            RekallAgeVulkanSceneFrameUniform frameUniform,
            GpuSceneVertex[] vertices,
            uint[] indices,
            ulong readbackBytes)
        {
            CreateHostBuffer(state, MemoryMarshal.AsBytes(vertices.AsSpan()), BufferUsageFlags.VertexBufferBit, out state.VertexBuffer, out state.VertexMemory);
            CreateHostBuffer(state, MemoryMarshal.AsBytes(indices.AsSpan()), BufferUsageFlags.IndexBufferBit, out state.IndexBuffer, out state.IndexMemory);

            var uniform = ToGpuFrameUniform(frameUniform);
            CreateHostBuffer(state, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref uniform, 1)), BufferUsageFlags.UniformBufferBit, out state.UniformBuffer, out state.UniformMemory);
            CreateHostBuffer(state, new byte[checked((int)readbackBytes)], BufferUsageFlags.TransferDstBit, out state.ReadbackBuffer, out state.ReadbackMemory);
        }

        private static void CreateTextures(
            VulkanState state,
            IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
        {
            var textures = meshes
                .SelectMany(mesh => new[]
                {
                    mesh.BaseColorTexture,
                    mesh.MetallicRoughnessTexture,
                    mesh.NormalTexture,
                    mesh.OcclusionTexture
                })
                .OfType<RekallAgeVulkanSceneTexture>()
                .Where(texture => texture.RuntimeTexture is null && texture.Rgba.Length > 0)
                .GroupBy(texture => texture.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .Concat(CreateDefaultTextures())
                .ToArray();

            foreach (var texture in textures)
            {
                CreateHostBuffer(state, texture.Rgba, BufferUsageFlags.TransferSrcBit, out var stagingBuffer, out var stagingMemory);
                CreateImage(
                    state,
                    checked((uint)texture.Width),
                    checked((uint)texture.Height),
                    Format.R8G8B8A8Unorm,
                    ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                    ImageAspectFlags.ColorBit,
                    out var image,
                    out var memory,
                    out var view);
                var sampler = CreateSampler(state, texture.Sampler);
                var resource = new VulkanTextureResource(texture.Id, checked((uint)texture.Width), checked((uint)texture.Height), stagingBuffer, stagingMemory, image, memory, view, sampler);
                state.Textures.Add(resource);
                if (texture.Id.Equals("__rekall_white", StringComparison.Ordinal))
                {
                    state.WhiteTexture = resource;
                }
                else if (texture.Id.Equals("__rekall_flat_normal", StringComparison.Ordinal))
                {
                    state.FlatNormalTexture = resource;
                }
                else if (texture.Id.Equals("__rekall_default_metallic_roughness", StringComparison.Ordinal))
                {
                    state.DefaultMetallicRoughnessTexture = resource;
                }
                else
                {
                    state.TextureById[texture.Id] = resource;
                }
            }
        }

        private static IEnumerable<RekallAgeVulkanSceneTexture> CreateDefaultTextures()
        {
            var sampler = new RekallAgeVulkanSceneSampler(
                RekallAgeVulkanSceneFilter.Linear,
                RekallAgeVulkanSceneFilter.Linear,
                RekallAgeVulkanSceneWrapMode.Repeat,
                RekallAgeVulkanSceneWrapMode.Repeat);
            yield return new RekallAgeVulkanSceneTexture("__rekall_white", 1, 1, [255, 255, 255, 255], sampler);
            yield return new RekallAgeVulkanSceneTexture("__rekall_flat_normal", 1, 1, [128, 128, 255, 255], sampler);
            yield return new RekallAgeVulkanSceneTexture("__rekall_default_metallic_roughness", 1, 1, [0, 255, 0, 255], sampler);
        }

        private static void CreateDescriptors(VulkanState state, IReadOnlyList<DrawRange> drawRanges)
        {
            var bindings = stackalloc DescriptorSetLayoutBinding[5];
            bindings[0] = new DescriptorSetLayoutBinding
            {
                Binding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.UniformBuffer,
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit
            };
            for (var i = 1u; i <= 4; i++)
            {
                bindings[i] = new DescriptorSetLayoutBinding
                {
                    Binding = i,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    StageFlags = ShaderStageFlags.FragmentBit
                };
            }

            var layoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 5,
                PBindings = bindings
            };
            ThrowIfFailed(state.Vk.CreateDescriptorSetLayout(state.Device, &layoutInfo, null, out state.DescriptorSetLayout), "vkCreateDescriptorSetLayout");

            var materialKeys = drawRanges
                .Select(range => range.MaterialKey)
                .Append(MaterialKey.Default)
                .Distinct()
                .ToArray();
            var descriptorSetCount = checked((uint)Math.Max(1, materialKeys.Length));
            var poolSizes = stackalloc DescriptorPoolSize[]
            {
                new(DescriptorType.UniformBuffer, descriptorSetCount),
                new(DescriptorType.CombinedImageSampler, checked(descriptorSetCount * 4))
            };
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = descriptorSetCount,
                PoolSizeCount = 2,
                PPoolSizes = poolSizes
            };
            ThrowIfFailed(state.Vk.CreateDescriptorPool(state.Device, &poolInfo, null, out state.DescriptorPool), "vkCreateDescriptorPool");

            foreach (var key in materialKeys)
            {
                var setLayout = state.DescriptorSetLayout;
                var allocateInfo = new DescriptorSetAllocateInfo
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = state.DescriptorPool,
                    DescriptorSetCount = 1,
                    PSetLayouts = &setLayout
                };
                ThrowIfFailed(state.Vk.AllocateDescriptorSets(state.Device, &allocateInfo, out var descriptorSet), "vkAllocateDescriptorSets");

                var bufferInfo = new DescriptorBufferInfo(state.UniformBuffer, 0, (ulong)Marshal.SizeOf<FrameUniform>());
                var baseColor = ResolveTextureResource(state, key.BaseColorTextureId, state.WhiteTexture!);
                var normal = ResolveTextureResource(state, key.NormalTextureId, state.FlatNormalTexture!);
                var metallicRoughness = ResolveTextureResource(state, key.MetallicRoughnessTextureId, state.DefaultMetallicRoughnessTexture!);
                var occlusion = ResolveTextureResource(state, key.OcclusionTextureId, state.WhiteTexture!);
                var baseColorInfo = new DescriptorImageInfo(baseColor.Sampler, baseColor.View, ImageLayout.ShaderReadOnlyOptimal);
                var normalInfo = new DescriptorImageInfo(normal.Sampler, normal.View, ImageLayout.ShaderReadOnlyOptimal);
                var metallicRoughnessInfo = new DescriptorImageInfo(metallicRoughness.Sampler, metallicRoughness.View, ImageLayout.ShaderReadOnlyOptimal);
                var occlusionInfo = new DescriptorImageInfo(occlusion.Sampler, occlusion.View, ImageLayout.ShaderReadOnlyOptimal);
                var writes = new WriteDescriptorSet[5];
                writes[0] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSet,
                    DstBinding = 0,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.UniformBuffer,
                    PBufferInfo = &bufferInfo
                };
                writes[1] = ImageWrite(descriptorSet, 1, &baseColorInfo);
                writes[2] = ImageWrite(descriptorSet, 2, &normalInfo);
                writes[3] = ImageWrite(descriptorSet, 3, &metallicRoughnessInfo);
                writes[4] = ImageWrite(descriptorSet, 4, &occlusionInfo);
                fixed (WriteDescriptorSet* writesPtr = writes)
                {
                    state.Vk.UpdateDescriptorSets(state.Device, 5, writesPtr, 0, null);
                }
                state.MaterialDescriptorSets[key] = descriptorSet;
                if (key.Equals(MaterialKey.Default))
                {
                    state.DescriptorSet = descriptorSet;
                }
            }
        }

        private static unsafe WriteDescriptorSet ImageWrite(DescriptorSet descriptorSet, uint binding, DescriptorImageInfo* imageInfo)
        {
            return new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = binding,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = imageInfo
            };
        }

        private static VulkanTextureResource ResolveTextureResource(
            VulkanState state,
            string? textureId,
            VulkanTextureResource fallback)
        {
            return textureId is not null && state.TextureById.TryGetValue(textureId, out var texture)
                ? texture
                : fallback;
        }

        private static bool TryCompileSceneShaders(
            List<string> errors,
            out RekallAgeVulkanSceneShaderCompilationResult shaders)
        {
            shaders = new RekallAgeVulkanShaderCompiler().CompileScenePipeline(RekallAgeVulkanScenePipelineDescription.Default);
            if (shaders.Compiled)
            {
                return true;
            }

            errors.AddRange(shaders.Errors);
            if (errors.Count == 0)
            {
                errors.Add("Vulkan scene shader compilation failed.");
            }

            return false;
        }

        private static void CreatePipeline(
            VulkanState state,
            RekallAgeRuntimeViewportFrame frame,
            RekallAgeVulkanSceneShaderCompilationResult shaders)
        {
            var vertexShader = shaders.Vertex.Spirv;
            var fragmentShader = shaders.Fragment.Spirv;

            fixed (byte* vertexCode = vertexShader)
            fixed (byte* fragmentCode = fragmentShader)
            {
                state.VertexShader = CreateShaderModule(state, vertexCode, vertexShader.Length);
                state.FragmentShader = CreateShaderModule(state, fragmentCode, fragmentShader.Length);
            }

            var entry = "main\0"u8.ToArray();
            fixed (byte* entryName = entry)
            {
                var shaderStages = stackalloc PipelineShaderStageCreateInfo[]
                {
                    new()
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.VertexBit,
                        Module = state.VertexShader,
                        PName = entryName
                    },
                    new()
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.FragmentBit,
                        Module = state.FragmentShader,
                        PName = entryName
                    }
                };

                var bindingDescription = new VertexInputBindingDescription(0, (uint)Marshal.SizeOf<GpuSceneVertex>(), VertexInputRate.Vertex);
                var attributes = stackalloc VertexInputAttributeDescription[]
                {
                    new(0, 0, Format.R32G32B32Sfloat, 0),
                    new(1, 0, Format.R32G32B32Sfloat, 12),
                    new(2, 0, Format.R32G32B32A32Sfloat, 24),
                    new(3, 0, Format.R32G32Sfloat, 40)
                };
                var vertexInput = new PipelineVertexInputStateCreateInfo
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = 1,
                    PVertexBindingDescriptions = &bindingDescription,
                    VertexAttributeDescriptionCount = 4,
                    PVertexAttributeDescriptions = attributes
                };
                var inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList
                };
                var viewport = new Viewport(0, 0, frame.Width, frame.Height, 0, 1);
                var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)frame.Width, (uint)frame.Height));
                var viewportState = new PipelineViewportStateCreateInfo
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    PViewports = &viewport,
                    ScissorCount = 1,
                    PScissors = &scissor
                };
                var rasterization = new PipelineRasterizationStateCreateInfo
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    PolygonMode = PolygonMode.Fill,
                    CullMode = CullModeFlags.None,
                    FrontFace = FrontFace.Clockwise,
                    LineWidth = 1
                };
                var multisample = new PipelineMultisampleStateCreateInfo
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    RasterizationSamples = SampleCountFlags.Count1Bit
                };
                var depth = new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = true,
                    DepthCompareOp = CompareOp.LessOrEqual
                };
                var colorBlendAttachment = new PipelineColorBlendAttachmentState
                {
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
                };
                var colorBlend = new PipelineColorBlendStateCreateInfo
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    AttachmentCount = 1,
                    PAttachments = &colorBlendAttachment
                };
                var setLayout = state.DescriptorSetLayout;
                var pushConstant = new PushConstantRange
                {
                    StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                    Offset = 0,
                    Size = (uint)Marshal.SizeOf<GpuDrawPushConstants>()
                };
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = &setLayout,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges = &pushConstant
                };
                ThrowIfFailed(state.Vk.CreatePipelineLayout(state.Device, &layoutInfo, null, out state.PipelineLayout), "vkCreatePipelineLayout");

                var pipelineInfo = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = shaderStages,
                    PVertexInputState = &vertexInput,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterization,
                    PMultisampleState = &multisample,
                    PDepthStencilState = &depth,
                    PColorBlendState = &colorBlend,
                    Layout = state.PipelineLayout,
                    RenderPass = state.RenderPass
                };
                ThrowIfFailed(state.Vk.CreateGraphicsPipelines(state.Device, default, 1, &pipelineInfo, null, out state.Pipeline), "vkCreateGraphicsPipelines");
            }
        }

        private static ShaderModule CreateShaderModule(VulkanState state, byte* code, int length)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)length,
                PCode = (uint*)code
            };
            ThrowIfFailed(state.Vk.CreateShaderModule(state.Device, &createInfo, null, out var module), "vkCreateShaderModule");
            return module;
        }

        private static Sampler CreateSampler(VulkanState state, RekallAgeVulkanSceneSampler sampler)
        {
            var createInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = ToVkFilter(sampler.MagFilter),
                MinFilter = ToVkFilter(sampler.MinFilter),
                MipmapMode = SamplerMipmapMode.Linear,
                AddressModeU = ToVkSamplerAddressMode(sampler.WrapS),
                AddressModeV = ToVkSamplerAddressMode(sampler.WrapT),
                AddressModeW = SamplerAddressMode.Repeat,
                MaxLod = 0,
                BorderColor = BorderColor.FloatTransparentBlack
            };
            ThrowIfFailed(state.Vk.CreateSampler(state.Device, &createInfo, null, out var handle), "vkCreateSampler");
            return handle;
        }

        private static Filter ToVkFilter(RekallAgeVulkanSceneFilter filter)
        {
            return filter == RekallAgeVulkanSceneFilter.Nearest ? Filter.Nearest : Filter.Linear;
        }

        private static SamplerAddressMode ToVkSamplerAddressMode(RekallAgeVulkanSceneWrapMode mode)
        {
            return mode switch
            {
                RekallAgeVulkanSceneWrapMode.ClampToEdge => SamplerAddressMode.ClampToEdge,
                RekallAgeVulkanSceneWrapMode.MirroredRepeat => SamplerAddressMode.MirroredRepeat,
                _ => SamplerAddressMode.Repeat
            };
        }

        private static void CreateCommandPoolAndBuffer(VulkanState state)
        {
            var poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = state.GraphicsQueueFamily
            };
            ThrowIfFailed(state.Vk.CreateCommandPool(state.Device, &poolInfo, null, out state.CommandPool), "vkCreateCommandPool");

            var allocateInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = state.CommandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1
            };
            ThrowIfFailed(state.Vk.AllocateCommandBuffers(state.Device, &allocateInfo, out state.CommandBuffer), "vkAllocateCommandBuffers");
        }

        private static void RecordCommands(VulkanState state, uint width, uint height, DrawRange[] drawRanges)
        {
            var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
            ThrowIfFailed(state.Vk.BeginCommandBuffer(state.CommandBuffer, &beginInfo), "vkBeginCommandBuffer");
            RecordTextureUploads(state);

            var clearValues = stackalloc ClearValue[2];
            clearValues[0].Color = new ClearColorValue(0.08f, 0.10f, 0.14f, 1f);
            clearValues[1].DepthStencil = new ClearDepthStencilValue(1f, 0);
            var renderPassBegin = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = state.RenderPass,
                Framebuffer = state.Framebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(width, height)),
                ClearValueCount = 2,
                PClearValues = clearValues
            };

            state.Vk.CmdBeginRenderPass(state.CommandBuffer, &renderPassBegin, SubpassContents.Inline);
            state.Vk.CmdBindPipeline(state.CommandBuffer, PipelineBindPoint.Graphics, state.Pipeline);
            var vertexBuffer = state.VertexBuffer;
            var offset = 0UL;
            state.Vk.CmdBindVertexBuffers(state.CommandBuffer, 0, 1, &vertexBuffer, &offset);
            state.Vk.CmdBindIndexBuffer(state.CommandBuffer, state.IndexBuffer, 0, IndexType.Uint32);
            foreach (var range in drawRanges)
            {
                var descriptorSet = ResolveDescriptorSet(state, range.MaterialKey);
                state.Vk.CmdBindDescriptorSets(state.CommandBuffer, PipelineBindPoint.Graphics, state.PipelineLayout, 0, 1, &descriptorSet, 0, null);
                var draw = range.Draw;
                state.Vk.CmdPushConstants(
                    state.CommandBuffer,
                    state.PipelineLayout,
                    ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                    0,
                    (uint)Marshal.SizeOf<GpuDrawPushConstants>(),
                    &draw);
                state.Vk.CmdDrawIndexed(state.CommandBuffer, range.IndexCount, 1, range.FirstIndex, range.VertexOffset, 0);
            }

            state.Vk.CmdEndRenderPass(state.CommandBuffer);
            var copy = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(width, height, 1)
            };
            state.Vk.CmdCopyImageToBuffer(state.CommandBuffer, state.ColorImage, ImageLayout.TransferSrcOptimal, state.ReadbackBuffer, 1, &copy);
            ThrowIfFailed(state.Vk.EndCommandBuffer(state.CommandBuffer), "vkEndCommandBuffer");
        }

        private static DescriptorSet ResolveDescriptorSet(VulkanState state, MaterialKey key)
        {
            if (state.MaterialDescriptorSets.TryGetValue(key, out var descriptorSet)
                && descriptorSet.Handle != 0)
            {
                return descriptorSet;
            }

            return state.DescriptorSet;
        }

        private static void RecordTextureUploads(VulkanState state)
        {
            foreach (var texture in state.Textures)
            {
                TransitionImage(
                    state,
                    texture.Image,
                    ImageLayout.Undefined,
                    ImageLayout.TransferDstOptimal,
                    0,
                    AccessFlags.TransferWriteBit,
                    PipelineStageFlags.TopOfPipeBit,
                    PipelineStageFlags.TransferBit);
                var copy = new BufferImageCopy
                {
                    BufferOffset = 0,
                    BufferRowLength = 0,
                    BufferImageHeight = 0,
                    ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    ImageOffset = new Offset3D(0, 0, 0),
                    ImageExtent = new Extent3D(texture.Width, texture.Height, 1)
                };
                state.Vk.CmdCopyBufferToImage(state.CommandBuffer, texture.StagingBuffer, texture.Image, ImageLayout.TransferDstOptimal, 1, &copy);
                TransitionImage(
                    state,
                    texture.Image,
                    ImageLayout.TransferDstOptimal,
                    ImageLayout.ShaderReadOnlyOptimal,
                    AccessFlags.TransferWriteBit,
                    AccessFlags.ShaderReadBit,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.FragmentShaderBit);
            }
        }

        private static void TransitionImage(
            VulkanState state,
            Image image,
            ImageLayout oldLayout,
            ImageLayout newLayout,
            AccessFlags srcAccess,
            AccessFlags dstAccess,
            PipelineStageFlags srcStage,
            PipelineStageFlags dstStage)
        {
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SrcAccessMask = srcAccess,
                DstAccessMask = dstAccess,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            state.Vk.CmdPipelineBarrier(
                state.CommandBuffer,
                srcStage,
                dstStage,
                0,
                0,
                null,
                0,
                null,
                1,
                &barrier);
        }

        private static void SubmitAndWait(VulkanState state)
        {
            var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
            ThrowIfFailed(state.Vk.CreateFence(state.Device, &fenceInfo, null, out state.Fence), "vkCreateFence");
            var commandBuffer = state.CommandBuffer;
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer
            };
            ThrowIfFailed(state.Vk.QueueSubmit(state.GraphicsQueue, 1, &submitInfo, state.Fence), "vkQueueSubmit");
            var fence = state.Fence;
            ThrowIfFailed(state.Vk.WaitForFences(state.Device, 1, &fence, true, FenceTimeoutNanoseconds), "vkWaitForFences");
        }

        private static byte[] ReadBack(VulkanState state, ulong byteCount)
        {
            void* mapped;
            ThrowIfFailed(state.Vk.MapMemory(state.Device, state.ReadbackMemory, 0, byteCount, 0, &mapped), "vkMapMemory");
            try
            {
                var bytes = new byte[checked((int)byteCount)];
                Marshal.Copy((nint)mapped, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                state.Vk.UnmapMemory(state.Device, state.ReadbackMemory);
            }
        }

        private static void CreateHostBuffer(VulkanState state, ReadOnlySpan<byte> source, BufferUsageFlags usage, out Buffer buffer, out DeviceMemory memory)
        {
            var createInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = (ulong)source.Length,
                Usage = usage,
                SharingMode = SharingMode.Exclusive
            };
            ThrowIfFailed(state.Vk.CreateBuffer(state.Device, &createInfo, null, out buffer), "vkCreateBuffer");
            state.Vk.GetBufferMemoryRequirements(state.Device, buffer, out var requirements);
            var memoryInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindMemoryType(state, requirements.MemoryTypeBits, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
            };
            ThrowIfFailed(state.Vk.AllocateMemory(state.Device, &memoryInfo, null, out memory), "vkAllocateMemory");
            ThrowIfFailed(state.Vk.BindBufferMemory(state.Device, buffer, memory, 0), "vkBindBufferMemory");

            if (source.Length > 0)
            {
                void* mapped;
                ThrowIfFailed(state.Vk.MapMemory(state.Device, memory, 0, (ulong)source.Length, 0, &mapped), "vkMapMemory");
                try
                {
                    fixed (byte* sourcePointer = source)
                    {
                        System.Buffer.MemoryCopy(sourcePointer, mapped, source.Length, source.Length);
                    }
                }
                finally
                {
                    state.Vk.UnmapMemory(state.Device, memory);
                }
            }
        }

        private static void AllocateAndBindImage(VulkanState state, Image image, MemoryRequirements requirements, MemoryPropertyFlags flags, out DeviceMemory memory)
        {
            var memoryInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindMemoryType(state, requirements.MemoryTypeBits, flags)
            };
            ThrowIfFailed(state.Vk.AllocateMemory(state.Device, &memoryInfo, null, out memory), "vkAllocateMemory");
            ThrowIfFailed(state.Vk.BindImageMemory(state.Device, image, memory, 0), "vkBindImageMemory");
        }

        private static uint FindMemoryType(VulkanState state, uint memoryTypeBits, MemoryPropertyFlags requiredFlags)
        {
            state.Vk.GetPhysicalDeviceMemoryProperties(state.PhysicalDevice, out var properties);
            for (uint i = 0; i < properties.MemoryTypeCount; i++)
            {
                if ((memoryTypeBits & (1u << (int)i)) == 0)
                {
                    continue;
                }

                var flags = properties.MemoryTypes[(int)i].PropertyFlags;
                if ((flags & requiredFlags) == requiredFlags)
                {
                    return i;
                }
            }

            throw new InvalidOperationException($"No Vulkan memory type satisfied flags '{requiredFlags}'.");
        }

        private static GpuSceneVertex[] BuildGpuVertices(IReadOnlyList<RekallAgeVulkanSceneVertex> vertices)
        {
            return vertices
                .Select(vertex => new GpuSceneVertex(
                    vertex.X,
                    vertex.Y,
                    vertex.Z,
                    vertex.NormalX,
                    vertex.NormalY,
                    vertex.NormalZ,
                    vertex.R,
                    vertex.G,
                    vertex.B,
                    vertex.A,
                    vertex.U,
                    vertex.V))
                .ToArray();
        }

        private static FrameUniform ToGpuFrameUniform(RekallAgeVulkanSceneFrameUniform frame)
        {
            return new FrameUniform(
                ToGpuMatrix(frame.ViewProjection),
                frame.LightDirection.X,
                frame.LightDirection.Y,
                frame.LightDirection.Z,
                0,
                frame.LightColor.X,
                frame.LightColor.Y,
                frame.LightColor.Z,
                frame.LightColor.W);
        }

        private static GpuMatrix4x4 ToGpuMatrix(Matrix4x4 matrix)
        {
            return new GpuMatrix4x4(
                matrix.M11,
                matrix.M12,
                matrix.M13,
                matrix.M14,
                matrix.M21,
                matrix.M22,
                matrix.M23,
                matrix.M24,
                matrix.M31,
                matrix.M32,
                matrix.M33,
                matrix.M34,
                matrix.M41,
                matrix.M42,
                matrix.M43,
                matrix.M44);
        }

        private static GpuDrawPushConstants ToGpuDrawPushConstants(Matrix4x4 model, Vector4 materialFactors)
        {
            return new GpuDrawPushConstants(
                ToGpuMatrix(model),
                materialFactors.X,
                materialFactors.Y,
                materialFactors.Z,
                materialFactors.W);
        }

        private static string ReadDeviceName(PhysicalDeviceProperties properties)
        {
            var deviceName = properties.DeviceName;
            return Marshal.PtrToStringUTF8((nint)deviceName) ?? "<unnamed Vulkan device>";
        }

        private static bool MatchesPreference(DeviceCandidate candidate, string? preferredDeviceType)
        {
            return preferredDeviceType?.Trim().ToLowerInvariant() switch
            {
                "discrete-gpu" or "discrete" => candidate.DeviceType == PhysicalDeviceType.DiscreteGpu,
                "integrated-gpu" or "integrated" => candidate.DeviceType == PhysicalDeviceType.IntegratedGpu,
                "cpu" => candidate.DeviceType == PhysicalDeviceType.Cpu,
                _ => true
            };
        }

        private static string ToDeviceTypeName(PhysicalDeviceType deviceType)
        {
            return deviceType switch
            {
                PhysicalDeviceType.DiscreteGpu => "discrete-gpu",
                PhysicalDeviceType.IntegratedGpu => "integrated-gpu",
                PhysicalDeviceType.VirtualGpu => "virtual-gpu",
                PhysicalDeviceType.Cpu => "cpu",
                _ => "other"
            };
        }

        private static string FormatVulkanVersion(uint version)
        {
            var major = version >> 22;
            var minor = (version >> 12) & 0x3ff;
            var patch = version & 0xfff;
            return $"{major}.{minor}.{patch}";
        }

        private static (ulong NonZero, RekallAgeVulkanReadbackPixel FirstPixel, ulong Checksum) Analyze(byte[] rgba)
        {
            ulong nonZero = 0;
            ulong checksum = 0;
            foreach (var value in rgba)
            {
                if (value != 0)
                {
                    nonZero++;
                }

                checksum = unchecked((checksum * 16777619) ^ value);
            }

            var firstPixel = rgba.Length >= 4
                ? new RekallAgeVulkanReadbackPixel(rgba[0], rgba[1], rgba[2], rgba[3])
                : default;
            return (nonZero, firstPixel, checksum);
        }

        private static void ThrowIfFailed(Result result, string operation)
        {
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"{operation} failed with VkResult {result}.");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly record struct GpuSceneVertex(float X, float Y, float Z, float NormalX, float NormalY, float NormalZ, float R, float G, float B, float A, float U, float V);

        [StructLayout(LayoutKind.Sequential)]
        private readonly record struct GpuMatrix4x4(
            float M11,
            float M12,
            float M13,
            float M14,
            float M21,
            float M22,
            float M23,
            float M24,
            float M31,
            float M32,
            float M33,
            float M34,
            float M41,
            float M42,
            float M43,
            float M44);

        [StructLayout(LayoutKind.Sequential)]
        private readonly record struct FrameUniform(GpuMatrix4x4 ViewProjection, float LightX, float LightY, float LightZ, float LightPad, float LightR, float LightG, float LightB, float LightA);

        [StructLayout(LayoutKind.Sequential)]
        private readonly record struct GpuDrawPushConstants(GpuMatrix4x4 Model, float MetallicFactor, float RoughnessFactor, float NormalScale, float OcclusionStrength);

        private readonly record struct MaterialKey(
            string? BaseColorTextureId,
            string? NormalTextureId,
            string? MetallicRoughnessTextureId,
            string? OcclusionTextureId)
        {
            public static MaterialKey Default { get; } = new(null, null, null, null);
        }

        private readonly record struct DrawRange(
            uint FirstIndex,
            uint IndexCount,
            int VertexOffset,
            GpuDrawPushConstants Draw,
            string? BaseColorTextureId,
            string? MetallicRoughnessTextureId,
            string? NormalTextureId,
            string? OcclusionTextureId)
        {
            public MaterialKey MaterialKey => new(BaseColorTextureId, NormalTextureId, MetallicRoughnessTextureId, OcclusionTextureId);
        }

        private readonly record struct DeviceCandidate(PhysicalDevice Device, string Name, PhysicalDeviceType DeviceType, uint ApiVersion, uint? QueueFamily);

        private sealed class VulkanState : IDisposable
        {
            public VulkanState(Vk vk)
            {
                Vk = vk;
            }

            public Vk Vk { get; }
            public Instance Instance;
            public PhysicalDevice PhysicalDevice;
            public Device Device;
            public Queue GraphicsQueue;
            public uint GraphicsQueueFamily;
            public RekallAgeVulkanSelectedDevice? SelectedDevice;
            public Image ColorImage;
            public DeviceMemory ColorMemory;
            public ImageView ColorView;
            public Image DepthImage;
            public DeviceMemory DepthMemory;
            public ImageView DepthView;
            public RenderPass RenderPass;
            public Framebuffer Framebuffer;
            public Buffer VertexBuffer;
            public DeviceMemory VertexMemory;
            public Buffer IndexBuffer;
            public DeviceMemory IndexMemory;
            public Buffer UniformBuffer;
            public DeviceMemory UniformMemory;
            public Buffer ReadbackBuffer;
            public DeviceMemory ReadbackMemory;
            public DescriptorSetLayout DescriptorSetLayout;
            public DescriptorPool DescriptorPool;
            public DescriptorSet DescriptorSet;
            public readonly List<VulkanTextureResource> Textures = [];
            public readonly Dictionary<string, VulkanTextureResource> TextureById = new(StringComparer.Ordinal);
            public readonly Dictionary<MaterialKey, DescriptorSet> MaterialDescriptorSets = [];
            public VulkanTextureResource? WhiteTexture;
            public VulkanTextureResource? FlatNormalTexture;
            public VulkanTextureResource? DefaultMetallicRoughnessTexture;
            public PipelineLayout PipelineLayout;
            public Pipeline Pipeline;
            public ShaderModule VertexShader;
            public ShaderModule FragmentShader;
            public CommandPool CommandPool;
            public CommandBuffer CommandBuffer;
            public Fence Fence;

            public void Dispose()
            {
                if (Device.Handle != 0)
                {
                    Vk.DeviceWaitIdle(Device);
                    if (Fence.Handle != 0)
                    {
                        Vk.DestroyFence(Device, Fence, null);
                    }

                    if (CommandPool.Handle != 0)
                    {
                        Vk.DestroyCommandPool(Device, CommandPool, null);
                    }

                    if (Pipeline.Handle != 0)
                    {
                        Vk.DestroyPipeline(Device, Pipeline, null);
                    }

                    if (PipelineLayout.Handle != 0)
                    {
                        Vk.DestroyPipelineLayout(Device, PipelineLayout, null);
                    }

                    if (FragmentShader.Handle != 0)
                    {
                        Vk.DestroyShaderModule(Device, FragmentShader, null);
                    }

                    if (VertexShader.Handle != 0)
                    {
                        Vk.DestroyShaderModule(Device, VertexShader, null);
                    }

                    if (DescriptorPool.Handle != 0)
                    {
                        Vk.DestroyDescriptorPool(Device, DescriptorPool, null);
                    }

                    if (DescriptorSetLayout.Handle != 0)
                    {
                        Vk.DestroyDescriptorSetLayout(Device, DescriptorSetLayout, null);
                    }

                    foreach (var texture in Textures)
                    {
                        texture.Dispose(Vk, Device);
                    }

                    DestroyBuffer(VertexBuffer, VertexMemory);
                    DestroyBuffer(IndexBuffer, IndexMemory);
                    DestroyBuffer(UniformBuffer, UniformMemory);
                    DestroyBuffer(ReadbackBuffer, ReadbackMemory);

                    if (Framebuffer.Handle != 0)
                    {
                        Vk.DestroyFramebuffer(Device, Framebuffer, null);
                    }

                    if (RenderPass.Handle != 0)
                    {
                        Vk.DestroyRenderPass(Device, RenderPass, null);
                    }

                    DestroyImage(ColorImage, ColorView, ColorMemory);
                    DestroyImage(DepthImage, DepthView, DepthMemory);
                    Vk.DestroyDevice(Device, null);
                }

                if (Instance.Handle != 0)
                {
                    Vk.DestroyInstance(Instance, null);
                }
            }

            private void DestroyBuffer(Buffer buffer, DeviceMemory memory)
            {
                if (buffer.Handle != 0)
                {
                    Vk.DestroyBuffer(Device, buffer, null);
                }

                if (memory.Handle != 0)
                {
                    Vk.FreeMemory(Device, memory, null);
                }
            }

            private void DestroyImage(Image image, ImageView view, DeviceMemory memory)
            {
                if (view.Handle != 0)
                {
                    Vk.DestroyImageView(Device, view, null);
                }

                if (image.Handle != 0)
                {
                    Vk.DestroyImage(Device, image, null);
                }

                if (memory.Handle != 0)
                {
                    Vk.FreeMemory(Device, memory, null);
                }
            }
        }

        private sealed class VulkanTextureResource
        {
            public VulkanTextureResource(
                string id,
                uint width,
                uint height,
                Buffer stagingBuffer,
                DeviceMemory stagingMemory,
                Image image,
                DeviceMemory memory,
                ImageView view,
                Sampler sampler)
            {
                Id = id;
                Width = width;
                Height = height;
                StagingBuffer = stagingBuffer;
                StagingMemory = stagingMemory;
                Image = image;
                Memory = memory;
                View = view;
                Sampler = sampler;
            }

            public string Id { get; }
            public uint Width { get; }
            public uint Height { get; }
            public Buffer StagingBuffer { get; }
            public DeviceMemory StagingMemory { get; }
            public Image Image { get; }
            public DeviceMemory Memory { get; }
            public ImageView View { get; }
            public Sampler Sampler { get; }
            public DescriptorSet DescriptorSet { get; set; }

            public void Dispose(Vk vk, Device device)
            {
                if (Sampler.Handle != 0)
                {
                    vk.DestroySampler(device, Sampler, null);
                }

                if (View.Handle != 0)
                {
                    vk.DestroyImageView(device, View, null);
                }

                if (Image.Handle != 0)
                {
                    vk.DestroyImage(device, Image, null);
                }

                if (Memory.Handle != 0)
                {
                    vk.FreeMemory(device, Memory, null);
                }

                if (StagingBuffer.Handle != 0)
                {
                    vk.DestroyBuffer(device, StagingBuffer, null);
                }

                if (StagingMemory.Handle != 0)
                {
                    vk.FreeMemory(device, StagingMemory, null);
                }
            }
        }
    }
}
