using System.Globalization;
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
        CancellationToken cancellationToken) =>
        await CaptureSceneCoreAsync(
            null,
            frame,
            assets,
            outputDirectory,
            preferredDeviceType,
            cancellationToken);

    public async ValueTask<RekallAgeVulkanSceneCaptureResult> CaptureProjectSceneAsync(
        string projectRoot,
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        string outputDirectory,
        string? preferredDeviceType,
        CancellationToken cancellationToken) =>
        await CaptureSceneCoreAsync(
            projectRoot,
            frame,
            assets,
            outputDirectory,
            preferredDeviceType,
            cancellationToken);

    private async ValueTask<RekallAgeVulkanSceneCaptureResult> CaptureSceneCoreAsync(
        string? projectRoot,
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
            return await CompositeUiOverlayAsync(
                FromClearCapture(frame, assets, clear),
                frame,
                assets,
                cancellationToken);
        }

        var (resolvedPipelines, pipelineUses) = await ResolveProjectPipelinesAsync(
            projectRoot,
            frame,
            cancellationToken);
        var invalidUses = pipelineUses.Where(use => !use.Valid).ToArray();
        if (invalidUses.Length > 0)
        {
            var pipelineErrors = invalidUses
                .SelectMany(use => use.Diagnostics.Select(diagnostic =>
                    $"Entity '{use.EntityName}' ({use.EntityId}) shader pipeline failed: {diagnostic}"))
                .Take(128)
                .ToArray();
            return Unavailable(
                frame,
                string.Empty,
                null,
                null,
                assets,
                meshes.Count,
                0,
                0,
                [],
                pipelineErrors) with
            {
                ShaderPipelines = pipelineUses
            };
        }

        var capture = VulkanSceneRenderer.TryCapture(
            frame,
            assets,
            meshes,
            outputDirectory,
            preferredDeviceType,
            cancellationToken,
            resolvedPipelines) with
        {
            ShaderPipelines = pipelineUses
        };
        return await CompositeUiOverlayAsync(capture, frame, assets, cancellationToken);
    }

    internal static async ValueTask<RekallAgeVulkanSceneCaptureResult> CompositeUiOverlayAsync(
        RekallAgeVulkanSceneCaptureResult capture,
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        CancellationToken cancellationToken)
    {
        if (!capture.Captured
            || string.IsNullOrWhiteSpace(capture.OutputPath)
            || !frame.Renderables.Any(renderable => renderable.UiVisual is not null))
        {
            return capture;
        }

        var image = await RekallAgePngReader.ReadRgbaAsync(capture.OutputPath, cancellationToken);
        if (image.Width != frame.Width || image.Height != frame.Height)
        {
            return capture with
            {
                Errors = capture.Errors
                    .Append("Vulkan UI composition skipped because the captured image dimensions do not match the runtime frame.")
                    .ToArray()
            };
        }

        var overlay = new RekallAgeRuntimeSoftwareRenderer().RenderUiOverlayRgba(frame, assets);
        for (var index = 0; index + 3 < image.Rgba.Length; index += 4)
        {
            var sourceAlpha = overlay[index + 3];
            if (sourceAlpha == 0)
            {
                continue;
            }

            var inverseAlpha = 255 - sourceAlpha;
            image.Rgba[index] = BlendChannel(overlay[index], image.Rgba[index], sourceAlpha, inverseAlpha);
            image.Rgba[index + 1] = BlendChannel(overlay[index + 1], image.Rgba[index + 1], sourceAlpha, inverseAlpha);
            image.Rgba[index + 2] = BlendChannel(overlay[index + 2], image.Rgba[index + 2], sourceAlpha, inverseAlpha);
            image.Rgba[index + 3] = (byte)Math.Min(255, sourceAlpha + image.Rgba[index + 3] * inverseAlpha / 255);
        }

        await RekallAgePngWriter.WriteRgbaAsync(
            capture.OutputPath,
            image.Width,
            image.Height,
            image.Rgba,
            cancellationToken);
        var (nonZero, firstPixel, checksum) = AnalyzeCompositedRgba(image.Rgba);
        return capture with
        {
            BytesRead = checked((ulong)image.Rgba.Length),
            NonZeroBytes = nonZero,
            FirstPixel = firstPixel,
            ByteChecksum = checksum,
            HighFidelityFrame = capture.HighFidelityFrame is not { } report
                ? null
                : report with
                {
                    Passes = report.Passes
                        .Append(new RekallAgeHighFidelityFramePassReport(
                            "ui",
                            "cpu-composite",
                            ["ldr-color"],
                            ["ldr-color"],
                            true,
                            0,
                            1))
                        .ToArray()
                }
        };
    }

    private static byte BlendChannel(byte source, byte destination, int sourceAlpha, int inverseAlpha) =>
        (byte)Math.Clamp((source * sourceAlpha + destination * inverseAlpha + 127) / 255, 0, 255);

    private static (ulong NonZero, RekallAgeVulkanReadbackPixel FirstPixel, ulong Checksum) AnalyzeCompositedRgba(
        byte[] rgba)
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

    private static async ValueTask<(
        IReadOnlyDictionary<RekallAgeRuntimeViewportShaderPipeline, RekallAgeResolvedShaderPipeline> Resolved,
        IReadOnlyList<RekallAgeVulkanShaderPipelineUse> Uses)> ResolveProjectPipelinesAsync(
        string? projectRoot,
        RekallAgeRuntimeViewportFrame frame,
        CancellationToken cancellationToken)
    {
        var authored = frame.Renderables
            .Where(renderable => renderable.ShaderPipeline is not null)
            .Take(128)
            .ToArray();
        if (authored.Length == 0)
        {
            return (new Dictionary<RekallAgeRuntimeViewportShaderPipeline, RekallAgeResolvedShaderPipeline>(), []);
        }

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            var unavailable = authored.Select(renderable => new RekallAgeVulkanShaderPipelineUse(
                renderable.EntityId,
                renderable.EntityName,
                renderable.ShaderPipeline!.VertexShader,
                renderable.ShaderPipeline.FragmentShader,
                "project",
                string.Empty,
                false,
                false,
                ["REKALL_SHADER_PROJECT_ROOT_REQUIRED: Project-root context is required to execute an authored shader pipeline."]))
                .ToArray();
            return (new Dictionary<RekallAgeRuntimeViewportShaderPipeline, RekallAgeResolvedShaderPipeline>(), unavailable);
        }

        var resolver = new RekallAgeProjectShaderPipelineResolver();
        var resolved = new Dictionary<RekallAgeRuntimeViewportShaderPipeline, RekallAgeResolvedShaderPipeline>();
        foreach (var pipeline in authored.Select(renderable => renderable.ShaderPipeline!).Distinct())
        {
            resolved[pipeline] = await resolver.ResolveAsync(projectRoot, pipeline, cancellationToken);
        }

        var uses = authored.Select(renderable =>
        {
            var pipeline = renderable.ShaderPipeline!;
            var asset = resolved[pipeline];
            return new RekallAgeVulkanShaderPipelineUse(
                renderable.EntityId,
                renderable.EntityName,
                pipeline.VertexShader,
                pipeline.FragmentShader,
                "project",
                asset.Key.ContentHash,
                asset.Valid,
                false,
                asset.Errors);
        }).ToArray();
        return (resolved, uses);
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

    internal static unsafe class VulkanSceneRenderer
    {
        private const ulong FenceTimeoutNanoseconds = 5_000_000_000;

        public static RekallAgeVulkanSceneCaptureResult TryCapture(
            RekallAgeRuntimeViewportFrame frame,
            RekallAgeRuntimeViewportAssetSet assets,
            IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
            string outputDirectory,
            string? preferredDeviceType,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<RekallAgeRuntimeViewportShaderPipeline, RekallAgeResolvedShaderPipeline>? resolvedPipelines = null)
        {
            var errors = new List<string>();
            var state = new VulkanState(Vk.GetApi());
            var highFidelityPlan = new RekallAgeVulkanHighFidelityFrameRenderer().Plan(frame, meshes);
            if (highFidelityPlan is { Ready: false })
            {
                errors.AddRange(highFidelityPlan.Graph.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message}"));
                return Unavailable(frame, string.Empty, null, null, assets, meshes.Count, 0, 0, [], errors) with
                {
                    HighFidelityFrame = CreateHighFidelityReport(highFidelityPlan, executed: false, [], errors)
                };
            }

            var target = highFidelityPlan is null
                ? RekallAgeVulkanSceneRenderTarget.OffscreenCapture(
                    checked((uint)frame.Width),
                    checked((uint)frame.Height))
                : RekallAgeVulkanSceneRenderTarget.HighFidelityOffscreenCapture(
                    checked((uint)highFidelityPlan!.Graph.Resources.Single(resource => resource.Name == "scene-hdr").Width),
                    checked((uint)highFidelityPlan.Graph.Resources.Single(resource => resource.Name == "scene-hdr").Height),
                    checked((uint)highFidelityPlan.Graph.Resources.Single(resource => resource.Name == "ldr-color").Width),
                    checked((uint)highFidelityPlan.Graph.Resources.Single(resource => resource.Name == "ldr-color").Height));
            var backendPlan = RekallAgeVulkanSceneRenderBackendPlanner.Plan(target);
            state.Ownership = backendPlan.Ownership;
            var prepared = RekallAgeVulkanScenePreparedFrameBuilder.Build(
                frame,
                meshes,
                target,
                highFidelityPlan?.ShadowPlan.LightEntityId);
            var commandPlan = RekallAgeVulkanSceneCommandPlanBuilder.BuildOffscreen(
                prepared,
                highFidelityPlan?.Graph,
                highFidelityPlan?.ShadowPlan);

            if (!prepared.HasDrawableGeometry)
            {
                errors.Add("Vulkan scene capture could not build drawable mesh buffers.");
                return Unavailable(frame, string.Empty, null, null, assets, meshes.Count, 0, 0, [], errors);
            }

            if (!commandPlan.Ready)
            {
                errors.AddRange(commandPlan.Blockers);
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
                if (highFidelityPlan is not null)
                {
                    ValidateHighFidelityFormats(state, commandPlan, highFidelityPlan.ShadowPlan, highFidelityPlan.FogPlan, errors);
                    if (errors.Count > 0)
                    {
                        return Unavailable(frame, string.Empty, "Silk.NET Vulkan", state.SelectedDevice, assets, meshes.Count, 0, 0, [], errors) with
                        {
                            HighFidelityFrame = CreateHighFidelityReport(highFidelityPlan, executed: false, [], errors)
                        };
                    }
                }

                var colorUsage = ImageUsageFlags.ColorAttachmentBit
                    | (highFidelityPlan is null ? ImageUsageFlags.TransferSrcBit : ImageUsageFlags.SampledBit)
                    | (highFidelityPlan?.FogPlan.UsesFroxelGrid == true ? ImageUsageFlags.StorageBit : 0);
                CreateImage(state, target.Width, target.Height, target.ColorFormat, colorUsage, ImageAspectFlags.ColorBit, 1, out state.ColorImage, out state.ColorMemory, out state.ColorView);
                CreateImage(state, target.Width, target.Height, target.DepthFormat, ImageUsageFlags.DepthStencilAttachmentBit, ImageAspectFlags.DepthBit, 1, out state.DepthImage, out state.DepthMemory, out state.DepthView);
                CreateRenderPass(state, target);
                CreateFramebuffer(state, target);
                if (highFidelityPlan is not null)
                {
                    CreateHighFidelityImages(state, target, highFidelityPlan, commandPlan);
                    if (highFidelityPlan.ShadowPlan.Enabled)
                    {
                        CreateShadowResources(state, highFidelityPlan.ShadowPlan);
                    }
                }
                CreateBuffers(
                    state,
                    commandPlan.RenderPasses.Select(pass => pass.FrameUniform).ToArray(),
                    commandPlan.RenderPasses[0].Draws.Select(draw => draw.PushConstants).ToArray(),
                    prepared.GeometryUpload,
                    prepared.ReadbackByteCount,
                    highFidelityPlan?.ShadowPlan);
                CreateTextures(state, meshes);
                CreateDescriptors(state, prepared.DrawPlan.MaterialKeys, highFidelityPlan?.ShadowPlan);
                if (!TryCompileSceneShaders(
                    errors,
                    out var shaders,
                    highFidelityPlan is not null,
                    highFidelityPlan?.ShadowPlan.Enabled == true))
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
                        DescriptorSetLayoutCreated = state.DescriptorSetLayout.Handle != 0,
                        HighFidelityFrame = highFidelityPlan is null
                            ? null
                            : CreateHighFidelityReport(highFidelityPlan, executed: false, [], errors)
                    };
                }

                CreatePipeline(state, frame, target, shaders);
                if (highFidelityPlan?.ShadowPlan.Enabled == true)
                {
                    CreateShadowPipeline(state, highFidelityPlan.ShadowPlan, errors);
                    if (errors.Count > 0)
                    {
                        return Unavailable(frame, string.Empty, "Silk.NET Vulkan", state.SelectedDevice, assets, meshes.Count, 0, 0, [], errors) with
                        {
                            HighFidelityFrame = CreateHighFidelityReport(highFidelityPlan, executed: false, [], errors)
                        };
                    }
                }
                CreateProjectPipelines(state, frame, target, resolvedPipelines);
                if (highFidelityPlan is not null)
                {
                    CreateHighFidelityPostPipeline(state, target, highFidelityPlan, commandPlan, errors);
                    if (errors.Count > 0)
                    {
                        return Unavailable(frame, string.Empty, "Silk.NET Vulkan", state.SelectedDevice, assets, meshes.Count, 0, 0, [], errors) with
                        {
                            HighFidelityFrame = CreateHighFidelityReport(highFidelityPlan, executed: false, [], errors)
                        };
                    }
                }
                CreateCommandPoolAndBuffer(state);
                if (highFidelityPlan is null)
                {
                    RecordCommands(state, commandPlan);
                }
                else
                {
                    RecordHighFidelityCommands(state, commandPlan, highFidelityPlan);
                }
                SubmitAndWait(state);

                Directory.CreateDirectory(outputDirectory);
                var shadowDebugCaptures = highFidelityPlan?.ShadowPlan.Enabled == true
                    ? WriteShadowDebugCaptures(state, highFidelityPlan.ShadowPlan, outputDirectory, cancellationToken)
                    : [];
                var fogDebugCaptures = highFidelityPlan?.FogPlan is { UsesFroxelGrid: true, Enabled: true }
                    ? WriteFogDebugCaptures(highFidelityPlan.FogPlan, outputDirectory, cancellationToken)
                    : [];
                var rgba = ReadBack(state, checked((ulong)target.EffectiveOutputWidth * target.EffectiveOutputHeight * 4));
                var outputPath = Path.Combine(outputDirectory, $"vulkan-scene-{target.EffectiveOutputWidth}x{target.EffectiveOutputHeight}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png");
                RekallAgePngWriter.WriteRgbaAsync(
                    outputPath,
                    checked((int)target.EffectiveOutputWidth),
                    checked((int)target.EffectiveOutputHeight),
                    rgba,
                    cancellationToken).AsTask().GetAwaiter().GetResult();

                var (nonZero, firstPixel, checksum) = Analyze(rgba);
                return new RekallAgeVulkanSceneCaptureResult(
                    true,
                    outputPath,
                    "Silk.NET Vulkan",
                    state.SelectedDevice,
                    target.EffectiveOutputWidth,
                    target.EffectiveOutputHeight,
                    ToFormatName(target.EffectiveOutputColorFormat),
                    checked((ulong)rgba.Length),
                    nonZero,
                    firstPixel,
                    checksum,
                    commandPlan.RenderPasses[0].Draws.Count,
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
                    Errors: []) with
                {
                    HighFidelityFrame = highFidelityPlan is null
                        ? null
                        : CreateHighFidelityReport(
                            highFidelityPlan,
                            executed: true,
                            CreateExecutedHighFidelityPassReports(commandPlan, highFidelityPlan),
                            [],
                            commandPlan,
                            shadowDebugCaptures,
                            fogDebugCaptures)
                };
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
                    GraphicsPipelineCreated = state.Pipeline.Handle != 0,
                    HighFidelityFrame = highFidelityPlan is null
                        ? null
                        : CreateHighFidelityReport(highFidelityPlan, executed: false, [], errors)
                };
            }
            finally
            {
                state.Dispose();
            }
        }

        public static RekallAgeOpenXrNativeVulkanSceneRenderResult TryRenderOpenXrFrame(
            Vk vk,
            Instance instance,
            PhysicalDevice physicalDevice,
            Device device,
            Queue queue,
            uint graphicsQueueFamily,
            RekallAgeOpenXrPerspectiveSceneFrame sceneFrame,
            RekallAgeVulkanSceneCommandPlan commandPlan,
            Image colorImage,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var errors = new List<string>();
            if (!commandPlan.Ready)
            {
                errors.AddRange(commandPlan.Blockers);
                return new RekallAgeOpenXrNativeVulkanSceneRenderResult(false, errors);
            }

            var prepared = commandPlan.PreparedFrame;
            if (!prepared.Target.IsOpenXrStereoSwapchain)
            {
                return new RekallAgeOpenXrNativeVulkanSceneRenderResult(
                    false,
                    ["Native OpenXR Vulkan frame rendering requires an OpenXR stereo swapchain target."]);
            }

            var state = new VulkanState(vk)
            {
                Instance = instance,
                PhysicalDevice = physicalDevice,
                Device = device,
                GraphicsQueue = queue,
                GraphicsQueueFamily = graphicsQueueFamily,
                Ownership = RekallAgeVulkanSceneRenderBackendPlanner.Plan(prepared.Target).Ownership,
                ColorImage = colorImage
            };

            try
            {
                var target = prepared.Target;
                state.ColorViews = CreateLayerImageViews(
                    state,
                    colorImage,
                    target.ColorFormat,
                    ImageAspectFlags.ColorBit,
                    target.EyeCount);
                state.ColorView = state.ColorViews[0];
                CreateImage(
                    state,
                    target.Width,
                    target.Height,
                    target.DepthFormat,
                    ImageUsageFlags.DepthStencilAttachmentBit,
                    ImageAspectFlags.DepthBit,
                    1,
                    target.EyeCount,
                    out state.DepthImage,
                    out state.DepthMemory,
                    out state.DepthView);
                state.DepthViews = CreateLayerImageViews(
                    state,
                    state.DepthImage,
                    target.DepthFormat,
                    ImageAspectFlags.DepthBit,
                    target.EyeCount);
                CreateRenderPass(state, target);
                CreateLayerFramebuffers(state, target);
                CreateBuffers(
                    state,
                    commandPlan.RenderPasses.Select(pass => pass.FrameUniform).ToArray(),
                    commandPlan.RenderPasses[0].Draws.Select(draw => draw.PushConstants).ToArray(),
                    prepared.GeometryUpload,
                    0);
                CreateTextures(state, prepared.Meshes);
                CreateDescriptors(state, prepared.DrawPlan.MaterialKeys);
                if (!TryCompileSceneShaders(errors, out var shaders))
                {
                    return new RekallAgeOpenXrNativeVulkanSceneRenderResult(false, errors);
                }

                CreatePipeline(state, sceneFrame.Frame, target, shaders);
                CreateCommandPoolAndBuffer(state);
                RecordCommands(state, commandPlan);
                SubmitAndWait(state);
                return new RekallAgeOpenXrNativeVulkanSceneRenderResult(true, Array.Empty<string>());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(ex.Message);
                return new RekallAgeOpenXrNativeVulkanSceneRenderResult(false, errors);
            }
            finally
            {
                state.Dispose();
            }
        }

        public sealed class OpenXrSwapchainImageRenderer : IDisposable
        {
            private readonly VulkanState _state;
            private readonly uint _targetWidth;
            private readonly uint _targetHeight;
            private readonly int _vertexByteCount;
            private readonly int _indexByteCount;
            private readonly int _drawCount;
            private readonly string _materialSignature;
            private bool _texturesUploaded;
            private bool _disposed;

            private OpenXrSwapchainImageRenderer(
                VulkanState state,
                RekallAgeVulkanSceneCommandPlan commandPlan)
            {
                _state = state;
                _targetWidth = commandPlan.PreparedFrame.Target.Width;
                _targetHeight = commandPlan.PreparedFrame.Target.Height;
                _vertexByteCount = commandPlan.PreparedFrame.GeometryUpload.VertexBytes.Length;
                _indexByteCount = commandPlan.PreparedFrame.GeometryUpload.IndexBytes.Length;
                _drawCount = commandPlan.RenderPasses[0].Draws.Count;
                _materialSignature = BuildMaterialSignature(commandPlan.PreparedFrame.DrawPlan.MaterialKeys);
            }

            public ulong ColorImageHandle => _state.ColorImage.Handle;

            public bool CanRender(RekallAgeVulkanSceneCommandPlan commandPlan)
            {
                return !_disposed
                    && commandPlan.PreparedFrame.Target.IsOpenXrStereoSwapchain
                    && commandPlan.PreparedFrame.Target.Width == _targetWidth
                    && commandPlan.PreparedFrame.Target.Height == _targetHeight
                    && commandPlan.PreparedFrame.GeometryUpload.VertexBytes.Length == _vertexByteCount
                    && commandPlan.PreparedFrame.GeometryUpload.IndexBytes.Length == _indexByteCount
                    && commandPlan.RenderPasses[0].Draws.Count == _drawCount
                    && string.Equals(BuildMaterialSignature(commandPlan.PreparedFrame.DrawPlan.MaterialKeys), _materialSignature, StringComparison.Ordinal);
            }

            public static RekallAgeOpenXrNativeVulkanSceneRenderResult TryCreate(
                Vk vk,
                Instance instance,
                PhysicalDevice physicalDevice,
                Device device,
                Queue queue,
                uint graphicsQueueFamily,
                RekallAgeOpenXrPerspectiveSceneFrame sceneFrame,
                RekallAgeVulkanSceneCommandPlan commandPlan,
                Image colorImage,
                out OpenXrSwapchainImageRenderer? renderer)
            {
                renderer = null;
                var errors = new List<string>();
                if (!commandPlan.Ready)
                {
                    errors.AddRange(commandPlan.Blockers);
                    return new RekallAgeOpenXrNativeVulkanSceneRenderResult(false, errors);
                }

                var prepared = commandPlan.PreparedFrame;
                if (!prepared.Target.IsOpenXrStereoSwapchain)
                {
                    return new RekallAgeOpenXrNativeVulkanSceneRenderResult(
                        false,
                        ["Native OpenXR Vulkan frame rendering requires an OpenXR stereo swapchain target."]);
                }

                var state = new VulkanState(vk)
                {
                    Instance = instance,
                    PhysicalDevice = physicalDevice,
                    Device = device,
                    GraphicsQueue = queue,
                    GraphicsQueueFamily = graphicsQueueFamily,
                    Ownership = RekallAgeVulkanSceneRenderBackendPlanner.Plan(prepared.Target).Ownership,
                    ColorImage = colorImage
                };

                try
                {
                    var target = prepared.Target;
                    state.ColorViews = CreateLayerImageViews(
                        state,
                        colorImage,
                        target.ColorFormat,
                        ImageAspectFlags.ColorBit,
                        target.EyeCount);
                    state.ColorView = state.ColorViews[0];
                    CreateImage(
                        state,
                        target.Width,
                        target.Height,
                        target.DepthFormat,
                        ImageUsageFlags.DepthStencilAttachmentBit,
                        ImageAspectFlags.DepthBit,
                        1,
                        target.EyeCount,
                        out state.DepthImage,
                        out state.DepthMemory,
                        out state.DepthView);
                    state.DepthViews = CreateLayerImageViews(
                        state,
                        state.DepthImage,
                        target.DepthFormat,
                        ImageAspectFlags.DepthBit,
                        target.EyeCount);
                    CreateRenderPass(state, target);
                    CreateLayerFramebuffers(state, target);
                    CreateBuffers(
                        state,
                        commandPlan.RenderPasses.Select(pass => pass.FrameUniform).ToArray(),
                        commandPlan.RenderPasses[0].Draws.Select(draw => draw.PushConstants).ToArray(),
                        prepared.GeometryUpload,
                        0);
                    CreateTextures(state, prepared.Meshes);
                    CreateDescriptors(state, prepared.DrawPlan.MaterialKeys);
                    if (!TryCompileSceneShaders(errors, out var shaders))
                    {
                        state.Dispose();
                        return new RekallAgeOpenXrNativeVulkanSceneRenderResult(false, errors);
                    }

                    CreatePipeline(state, sceneFrame.Frame, target, shaders);
                    CreateCommandPoolAndBuffer(state);
                    renderer = new OpenXrSwapchainImageRenderer(state, commandPlan);
                    return new RekallAgeOpenXrNativeVulkanSceneRenderResult(true, Array.Empty<string>());
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    state.Dispose();
                    errors.Add(ex.Message);
                    return new RekallAgeOpenXrNativeVulkanSceneRenderResult(false, errors);
                }
            }

            public RekallAgeOpenXrNativeVulkanSceneRenderResult Render(RekallAgeVulkanSceneCommandPlan commandPlan)
            {
                if (_disposed)
                {
                    return new RekallAgeOpenXrNativeVulkanSceneRenderResult(false, ["OpenXR swapchain image renderer has already been disposed."]);
                }

                if (!CanRender(commandPlan))
                {
                    return new RekallAgeOpenXrNativeVulkanSceneRenderResult(false, ["OpenXR swapchain image renderer resources do not match the current frame geometry or target."]);
                }

                try
                {
                    UpdateFrameUniformBuffers(_state, commandPlan.RenderPasses.Select(pass => pass.FrameUniform).ToArray());
                    UpdateDrawUniformBuffer(
                        _state,
                        commandPlan.RenderPasses[0].Draws.Select(draw => draw.PushConstants).ToArray());
                    RecordCommands(_state, commandPlan, uploadTextures: !_texturesUploaded);
                    _texturesUploaded = true;
                    SubmitAndWait(_state);
                    return new RekallAgeOpenXrNativeVulkanSceneRenderResult(true, Array.Empty<string>());
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return new RekallAgeOpenXrNativeVulkanSceneRenderResult(false, [ex.Message]);
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _state.Dispose();
            }

            private static string BuildMaterialSignature(IReadOnlyList<RekallAgeVulkanSceneMaterialKey> materialKeys)
            {
                return string.Join(
                    "|",
                    materialKeys.Select(key => string.Join(
                        ",",
                        key.BaseColorTextureId ?? string.Empty,
                        key.MetallicRoughnessTextureId ?? string.Empty,
                        key.NormalTextureId ?? string.Empty,
                        key.OcclusionTextureId ?? string.Empty,
                        key.EmissiveTextureId ?? string.Empty)));
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
            uint mipLevels,
            uint arrayLayers,
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
                MipLevels = mipLevels,
                ArrayLayers = arrayLayers,
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
                ViewType = arrayLayers > 1 ? ImageViewType.Type2DArray : ImageViewType.Type2D,
                Format = format,
                Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity),
                SubresourceRange = new ImageSubresourceRange(aspect, 0, mipLevels, 0, arrayLayers)
            };
            ThrowIfFailed(state.Vk.CreateImageView(state.Device, &viewInfo, null, out view), "vkCreateImageView");
        }

        private static void CreateImage(
            VulkanState state,
            uint width,
            uint height,
            Format format,
            ImageUsageFlags usage,
            ImageAspectFlags aspect,
            uint mipLevels,
            out Image image,
            out DeviceMemory memory,
            out ImageView view)
        {
            CreateImage(state, width, height, format, usage, aspect, mipLevels, 1, out image, out memory, out view);
        }

        private static void CreateImage3D(
            VulkanState state,
            uint width,
            uint height,
            uint depth,
            Format format,
            ImageUsageFlags usage,
            out Image image,
            out DeviceMemory memory,
            out ImageView view)
        {
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type3D,
                Format = format,
                Extent = new Extent3D(width, height, depth),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined
            };
            ThrowIfFailed(state.Vk.CreateImage(state.Device, &imageInfo, null, out image), "vkCreateImage fog 3D");
            state.Vk.GetImageMemoryRequirements(state.Device, image, out var requirements);
            AllocateAndBindImage(state, image, requirements, MemoryPropertyFlags.DeviceLocalBit, out memory);
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type3D,
                Format = format,
                Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity),
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            ThrowIfFailed(state.Vk.CreateImageView(state.Device, &viewInfo, null, out view), "vkCreateImageView fog 3D");
        }

        private static ImageView[] CreateLayerImageViews(
            VulkanState state,
            Image image,
            Format format,
            ImageAspectFlags aspect,
            uint layerCount)
        {
            var views = new ImageView[checked((int)Math.Max(1, layerCount))];
            for (uint layer = 0; layer < views.Length; layer++)
            {
                var viewInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = image,
                    ViewType = ImageViewType.Type2D,
                    Format = format,
                    Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity),
                    SubresourceRange = new ImageSubresourceRange(aspect, 0, 1, layer, 1)
                };
                ThrowIfFailed(state.Vk.CreateImageView(state.Device, &viewInfo, null, out views[layer]), "vkCreateImageView");
            }

            return views;
        }

        private static void CreateRenderPass(VulkanState state, RekallAgeVulkanSceneRenderTarget target)
        {
            var colorAttachment = new AttachmentDescription
            {
                Format = target.ColorFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = target.InitialColorLayout,
                FinalLayout = target.FinalColorLayout
            };
            var depthAttachment = new AttachmentDescription
            {
                Format = target.DepthFormat,
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

        private static void CreateFramebuffer(VulkanState state, RekallAgeVulkanSceneRenderTarget target)
        {
            var attachments = stackalloc ImageView[] { state.ColorView, state.DepthView };
            var createInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = state.RenderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = target.Width,
                Height = target.Height,
                Layers = 1
            };
            ThrowIfFailed(state.Vk.CreateFramebuffer(state.Device, &createInfo, null, out state.Framebuffer), "vkCreateFramebuffer");
            state.Framebuffers = [state.Framebuffer];
        }

        private static void CreateShadowResources(
            VulkanState state,
            RekallAgeVulkanShadowPlan plan)
        {
            var layerCount = checked((uint)plan.Cascades.Count);
            var resolution = checked((uint)plan.Resolution);
            CreateImage(
                state,
                resolution,
                resolution,
                Format.D32Sfloat,
                ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit,
                ImageAspectFlags.DepthBit,
                1,
                layerCount,
                out state.ShadowImage,
                out state.ShadowMemory,
                out state.ShadowView);
            state.ShadowLayerViews = CreateLayerImageViews(
                state,
                state.ShadowImage,
                Format.D32Sfloat,
                ImageAspectFlags.DepthBit,
                layerCount);

            var depthAttachment = new AttachmentDescription
            {
                Format = Format.D32Sfloat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.ShaderReadOnlyOptimal
            };
            var depthReference = new AttachmentReference(0, ImageLayout.DepthStencilAttachmentOptimal);
            var subpass = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                PDepthStencilAttachment = &depthReference
            };
            var dependencies = stackalloc SubpassDependency[]
            {
                new()
                {
                    SrcSubpass = Vk.SubpassExternal,
                    DstSubpass = 0,
                    SrcStageMask = PipelineStageFlags.FragmentShaderBit,
                    DstStageMask = PipelineStageFlags.EarlyFragmentTestsBit,
                    SrcAccessMask = AccessFlags.ShaderReadBit,
                    DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit
                },
                new()
                {
                    SrcSubpass = 0,
                    DstSubpass = Vk.SubpassExternal,
                    SrcStageMask = PipelineStageFlags.LateFragmentTestsBit,
                    DstStageMask = PipelineStageFlags.FragmentShaderBit,
                    SrcAccessMask = AccessFlags.DepthStencilAttachmentWriteBit,
                    DstAccessMask = AccessFlags.ShaderReadBit
                }
            };
            var renderPassInfo = new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = 1,
                PAttachments = &depthAttachment,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = 2,
                PDependencies = dependencies
            };
            ThrowIfFailed(state.Vk.CreateRenderPass(state.Device, &renderPassInfo, null, out state.ShadowRenderPass), "vkCreateRenderPass shadow");

            state.ShadowFramebuffers = new Framebuffer[state.ShadowLayerViews.Length];
            for (var index = 0; index < state.ShadowLayerViews.Length; index++)
            {
                var view = state.ShadowLayerViews[index];
                var framebufferInfo = new FramebufferCreateInfo
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = state.ShadowRenderPass,
                    AttachmentCount = 1,
                    PAttachments = &view,
                    Width = resolution,
                    Height = resolution,
                    Layers = 1
                };
                ThrowIfFailed(
                    state.Vk.CreateFramebuffer(state.Device, &framebufferInfo, null, out state.ShadowFramebuffers[index]),
                    "vkCreateFramebuffer shadow");
            }
        }

        private static void CreateLayerFramebuffers(VulkanState state, RekallAgeVulkanSceneRenderTarget target)
        {
            var framebufferCount = checked((int)Math.Max(target.EyeCount, 1));
            state.Framebuffers = new Framebuffer[framebufferCount];
            var attachments = stackalloc ImageView[2];
            for (var index = 0; index < framebufferCount; index++)
            {
                var colorView = state.ColorViews[Math.Min(index, state.ColorViews.Length - 1)];
                var depthView = state.DepthViews[Math.Min(index, state.DepthViews.Length - 1)];
                attachments[0] = colorView;
                attachments[1] = depthView;
                var createInfo = new FramebufferCreateInfo
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = state.RenderPass,
                    AttachmentCount = 2,
                    PAttachments = attachments,
                    Width = target.Width,
                    Height = target.Height,
                    Layers = 1
                };
                ThrowIfFailed(state.Vk.CreateFramebuffer(state.Device, &createInfo, null, out state.Framebuffers[index]), "vkCreateFramebuffer");
            }

            state.Framebuffer = state.Framebuffers[0];
        }

        private static void CreateBuffers(
            VulkanState state,
            IReadOnlyList<RekallAgeVulkanSceneGpuFrameUniform> frameUniforms,
            IReadOnlyList<RekallAgeVulkanSceneGpuDrawPushConstants> drawUniforms,
            RekallAgeVulkanSceneGeometryUpload geometryUpload,
            ulong readbackBytes,
            RekallAgeVulkanShadowPlan? shadowPlan = null)
        {
            CreateHostBuffer(state, geometryUpload.VertexBytes, BufferUsageFlags.VertexBufferBit, out state.VertexBuffer, out state.VertexMemory);
            CreateHostBuffer(state, geometryUpload.IndexBytes, BufferUsageFlags.IndexBufferBit, out state.IndexBuffer, out state.IndexMemory);

            var uniformCount = Math.Max(1, frameUniforms.Count);
            state.UniformBuffers = new Buffer[uniformCount];
            state.UniformMemories = new DeviceMemory[uniformCount];
            for (var index = 0; index < uniformCount; index++)
            {
                var frameUniform = frameUniforms.Count > 0
                    ? frameUniforms[index]
                    : default;
                CreateHostBuffer(
                    state,
                    MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref frameUniform, 1)),
                    BufferUsageFlags.UniformBufferBit,
                    out state.UniformBuffers[index],
                    out state.UniformMemories[index]);
            }

            state.UniformBuffer = state.UniformBuffers[0];
            state.UniformMemory = state.UniformMemories[0];
            if (shadowPlan is { Enabled: true })
            {
                state.ShadowUniformBuffers = new Buffer[shadowPlan.Cascades.Count];
                state.ShadowUniformMemories = new DeviceMemory[shadowPlan.Cascades.Count];
                for (var index = 0; index < shadowPlan.Cascades.Count; index++)
                {
                    var cascadeUniform = new ShadowCascadeGpuUniform(
                        RekallAgeVulkanSceneUniformUploadBuilder.ToGpuMatrix(shadowPlan.Cascades[index].ViewProjection),
                        shadowPlan.DepthBias,
                        shadowPlan.NormalBias,
                        shadowPlan.Resolution,
                        index);
                    CreateHostBuffer(
                        state,
                        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref cascadeUniform, 1)),
                        BufferUsageFlags.UniformBufferBit,
                        out state.ShadowUniformBuffers[index],
                        out state.ShadowUniformMemories[index]);
                }

                state.ShadowReadbackByteCount = checked(
                    (ulong)shadowPlan.Resolution
                    * (ulong)shadowPlan.Resolution
                    * sizeof(float)
                    * (ulong)shadowPlan.Cascades.Count);
                CreateHostBuffer(
                    state,
                    new byte[checked((int)state.ShadowReadbackByteCount)],
                    BufferUsageFlags.TransferDstBit,
                    out state.ShadowReadbackBuffer,
                    out state.ShadowReadbackMemory);
            }
            state.Vk.GetPhysicalDeviceProperties(state.PhysicalDevice, out var physicalDeviceProperties);
            var drawUniformBytes = checked((uint)Marshal.SizeOf<RekallAgeVulkanSceneGpuDrawPushConstants>());
            var minimumAlignment = Math.Max(1UL, physicalDeviceProperties.Limits.MinUniformBufferOffsetAlignment);
            var alignedStride = checked((uint)(((ulong)drawUniformBytes + minimumAlignment - 1) / minimumAlignment * minimumAlignment));
            var drawCount = Math.Max(1, drawUniforms.Count);
            var packedDrawUniforms = new byte[checked((int)alignedStride * drawCount)];
            for (var index = 0; index < drawUniforms.Count; index++)
            {
                var drawUniform = drawUniforms[index];
                MemoryMarshal.Write(
                    packedDrawUniforms.AsSpan(checked((int)alignedStride * index), checked((int)drawUniformBytes)),
                    in drawUniform);
            }

            CreateHostBuffer(
                state,
                packedDrawUniforms,
                BufferUsageFlags.UniformBufferBit,
                out state.DrawUniformBuffer,
                out state.DrawUniformMemory);
            state.DrawUniformStrideBytes = alignedStride;

            if (readbackBytes > 0)
            {
                CreateHostBuffer(state, new byte[checked((int)readbackBytes)], BufferUsageFlags.TransferDstBit, out state.ReadbackBuffer, out state.ReadbackMemory);
            }
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
                    mesh.OcclusionTexture,
                    mesh.EmissiveTexture,
                    mesh.SurfaceWaterTexture
                })
                .OfType<RekallAgeVulkanSceneTexture>()
                .GroupBy(texture => texture.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .Concat(CreateDefaultTextures())
                .ToArray();

            foreach (var texture in textures)
            {
                if (!TryCreateTextureResource(state, texture, out var resource))
                {
                    continue;
                }

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

        private static bool TryCreateTextureResource(
            VulkanState state,
            RekallAgeVulkanSceneTexture texture,
            out VulkanTextureResource resource)
        {
            resource = default!;
            if (texture.RuntimeTexture is { } runtimeTexture
                && RekallAgeVulkanTextureFormatMapper.TryMapBlockCompressedFormat(runtimeTexture.Format, out var compressedFormat)
                && runtimeTexture.MipLevels.Count > 0
                && IsSampledTransferDestinationFormatSupported(state, compressedFormat))
            {
                return TryCreateRuntimeTextureResource(state, texture, runtimeTexture, compressedFormat, out resource);
            }

            if (texture.Rgba.Length == 0)
            {
                return false;
            }

            var upload = new VulkanTextureMipUpload(
                0,
                0,
                checked((uint)texture.Width),
                checked((uint)texture.Height));
            return TryCreateTextureResource(
                state,
                texture.Id,
                texture.Sampler,
                Format.R8G8B8A8Unorm,
                checked((uint)texture.Width),
                checked((uint)texture.Height),
                [upload],
                texture.Rgba,
                out resource);
        }

        private static bool TryCreateRuntimeTextureResource(
            VulkanState state,
            RekallAgeVulkanSceneTexture texture,
            RekallAgeRuntimeTextureAsset runtimeTexture,
            Format format,
            out VulkanTextureResource resource)
        {
            var mips = runtimeTexture.MipLevels
                .OrderBy(level => level.Level)
                .ToArray();
            var uploadBytes = new byte[mips.Sum(level => level.Bytes.Length)];
            var uploads = new VulkanTextureMipUpload[mips.Length];
            var offset = 0;
            for (var index = 0; index < mips.Length; index++)
            {
                var mip = mips[index];
                mip.Bytes.CopyTo(uploadBytes, offset);
                uploads[index] = new VulkanTextureMipUpload(
                    checked((ulong)offset),
                    checked((uint)mip.Level),
                    checked((uint)mip.Width),
                    checked((uint)mip.Height));
                offset += mip.Bytes.Length;
            }

            return TryCreateTextureResource(
                state,
                texture.Id,
                texture.Sampler,
                format,
                checked((uint)runtimeTexture.Width),
                checked((uint)runtimeTexture.Height),
                uploads,
                uploadBytes,
                out resource);
        }

        private static bool TryCreateTextureResource(
            VulkanState state,
            string id,
            RekallAgeVulkanSceneSampler samplerDescription,
            Format format,
            uint width,
            uint height,
            IReadOnlyList<VulkanTextureMipUpload> uploads,
            byte[] uploadBytes,
            out VulkanTextureResource resource)
        {
            resource = default!;
            if (uploads.Count == 0 || uploadBytes.Length == 0)
            {
                return false;
            }

            CreateHostBuffer(state, uploadBytes, BufferUsageFlags.TransferSrcBit, out var stagingBuffer, out var stagingMemory);
            CreateImage(
                state,
                width,
                height,
                format,
                ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                ImageAspectFlags.ColorBit,
                checked((uint)uploads.Count),
                out var image,
                out var memory,
                out var view);
            var sampler = CreateSampler(state, samplerDescription);
            resource = new VulkanTextureResource(id, width, height, uploads, stagingBuffer, stagingMemory, image, memory, view, sampler);
            return true;
        }

        private static bool IsSampledTransferDestinationFormatSupported(VulkanState state, Format format)
        {
            state.Vk.GetPhysicalDeviceFormatProperties(state.PhysicalDevice, format, out var properties);
            const FormatFeatureFlags required =
                FormatFeatureFlags.SampledImageBit
                | FormatFeatureFlags.TransferDstBit;
            return (properties.OptimalTilingFeatures & required) == required;
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

        private static void CreateDescriptors(
            VulkanState state,
            IReadOnlyList<RekallAgeVulkanSceneMaterialKey> materialKeys,
            RekallAgeVulkanShadowPlan? shadowPlan = null)
        {
            var uniformBinding = new DescriptorSetLayoutBinding
            {
                Binding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.UniformBuffer,
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit
            };
            var uniformLayoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 1,
                PBindings = &uniformBinding
            };
            ThrowIfFailed(
                state.Vk.CreateDescriptorSetLayout(state.Device, &uniformLayoutInfo, null, out state.DescriptorSetLayout),
                "vkCreateDescriptorSetLayout frame");
            var drawBinding = uniformBinding;
            drawBinding.DescriptorType = DescriptorType.UniformBufferDynamic;
            var drawLayoutInfo = uniformLayoutInfo;
            drawLayoutInfo.PBindings = &drawBinding;
            ThrowIfFailed(
                state.Vk.CreateDescriptorSetLayout(state.Device, &drawLayoutInfo, null, out state.DrawDescriptorSetLayout),
                "vkCreateDescriptorSetLayout draw");

            var materialBindings = stackalloc DescriptorSetLayoutBinding[14];
            for (var binding = 0u; binding < 14; binding++)
            {
                materialBindings[binding] = new DescriptorSetLayoutBinding
                {
                    Binding = binding,
                    DescriptorCount = 1,
                    DescriptorType = binding % 2 == 0
                        ? DescriptorType.SampledImage
                        : DescriptorType.Sampler,
                    StageFlags = ShaderStageFlags.FragmentBit
                };
            }

            var materialLayoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 14,
                PBindings = materialBindings
            };
            ThrowIfFailed(
                state.Vk.CreateDescriptorSetLayout(state.Device, &materialLayoutInfo, null, out state.MaterialDescriptorSetLayout),
                "vkCreateDescriptorSetLayout material");

            if (shadowPlan is { Enabled: true })
            {
                var shadowSampleBinding = new DescriptorSetLayoutBinding(
                    0,
                    DescriptorType.CombinedImageSampler,
                    1,
                    ShaderStageFlags.FragmentBit);
                var shadowSampleLayoutInfo = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = 1,
                    PBindings = &shadowSampleBinding
                };
                ThrowIfFailed(
                    state.Vk.CreateDescriptorSetLayout(state.Device, &shadowSampleLayoutInfo, null, out state.ShadowSampleDescriptorSetLayout),
                    "vkCreateDescriptorSetLayout shadow sample");
            }

            var frameSetCount = checked((uint)state.UniformBuffers.Length);
            var shadowSetCount = checked((uint)state.ShadowUniformBuffers.Length);
            var materialSetCount = checked((uint)Math.Max(1, materialKeys.Count));
            var poolSizes = stackalloc DescriptorPoolSize[]
            {
                new(DescriptorType.UniformBuffer, checked(frameSetCount + shadowSetCount)),
                new(DescriptorType.UniformBufferDynamic, 1),
                new(DescriptorType.SampledImage, checked(materialSetCount * 7)),
                new(DescriptorType.Sampler, checked(materialSetCount * 7)),
                new(DescriptorType.CombinedImageSampler, 1)
            };
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = checked(frameSetCount + shadowSetCount + 1 + materialSetCount + (shadowSetCount > 0 ? 1u : 0u)),
                PoolSizeCount = 5,
                PPoolSizes = poolSizes
            };
            ThrowIfFailed(state.Vk.CreateDescriptorPool(state.Device, &poolInfo, null, out state.DescriptorPool), "vkCreateDescriptorPool");

            state.FrameDescriptorSets = new DescriptorSet[state.UniformBuffers.Length];
            for (var index = 0; index < state.UniformBuffers.Length; index++)
            {
                var descriptorSet = AllocateDescriptorSet(state, state.DescriptorSetLayout);
                var bufferInfo = new DescriptorBufferInfo(
                    state.UniformBuffers[index],
                    0,
                    (ulong)Marshal.SizeOf<RekallAgeVulkanSceneGpuFrameUniform>());
                var write = UniformWrite(descriptorSet, &bufferInfo);
                state.Vk.UpdateDescriptorSets(state.Device, 1, &write, 0, null);
                state.FrameDescriptorSets[index] = descriptorSet;
                if (index == 0)
                {
                    state.DescriptorSet = descriptorSet;
                }
            }

            state.DrawDescriptorSet = AllocateDescriptorSet(state, state.DrawDescriptorSetLayout);
            var drawBufferInfo = new DescriptorBufferInfo(
                state.DrawUniformBuffer,
                0,
                (ulong)Marshal.SizeOf<RekallAgeVulkanSceneGpuDrawPushConstants>());
            var drawWrite = UniformWrite(
                state.DrawDescriptorSet,
                &drawBufferInfo,
                DescriptorType.UniformBufferDynamic);
            state.Vk.UpdateDescriptorSets(state.Device, 1, &drawWrite, 0, null);

            if (shadowSetCount > 0)
            {
                state.ShadowDescriptorSets = new DescriptorSet[state.ShadowUniformBuffers.Length];
                for (var index = 0; index < state.ShadowUniformBuffers.Length; index++)
                {
                    var descriptorSet = AllocateDescriptorSet(state, state.DescriptorSetLayout);
                    var shadowBufferInfo = new DescriptorBufferInfo(
                        state.ShadowUniformBuffers[index],
                        0,
                        (ulong)Marshal.SizeOf<ShadowCascadeGpuUniform>());
                    var shadowWrite = UniformWrite(descriptorSet, &shadowBufferInfo);
                    state.Vk.UpdateDescriptorSets(state.Device, 1, &shadowWrite, 0, null);
                    state.ShadowDescriptorSets[index] = descriptorSet;
                }

                state.ShadowSampler = CreateShadowSampler(state);
                state.ShadowSampleDescriptorSet = AllocateDescriptorSet(state, state.ShadowSampleDescriptorSetLayout);
                var shadowImageInfo = new DescriptorImageInfo(
                    state.ShadowSampler,
                    state.ShadowView,
                    ImageLayout.ShaderReadOnlyOptimal);
                var shadowImageWrite = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = state.ShadowSampleDescriptorSet,
                    DstBinding = 0,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    PImageInfo = &shadowImageInfo
                };
                state.Vk.UpdateDescriptorSets(state.Device, 1, &shadowImageWrite, 0, null);
            }

            foreach (var key in materialKeys)
            {
                var descriptorSet = AllocateDescriptorSet(state, state.MaterialDescriptorSetLayout);
                var textures = new[]
                {
                    ResolveTextureResource(state, key.BaseColorTextureId, state.WhiteTexture!),
                    ResolveTextureResource(state, key.NormalTextureId, state.FlatNormalTexture!),
                    ResolveTextureResource(state, key.MetallicRoughnessTextureId, state.DefaultMetallicRoughnessTexture!),
                    ResolveTextureResource(state, key.OcclusionTextureId, state.WhiteTexture!),
                    ResolveTextureResource(state, key.EmissiveTextureId, state.WhiteTexture!),
                    ResolveTextureResource(state, key.CloudShadowTextureId, state.WhiteTexture!),
                    ResolveTextureResource(state, key.SurfaceWaterTextureId, state.WhiteTexture!)
                };
                var imageInfos = new DescriptorImageInfo[14];
                var writes = new WriteDescriptorSet[14];
                for (var textureIndex = 0; textureIndex < textures.Length; textureIndex++)
                {
                    imageInfos[textureIndex * 2] = new DescriptorImageInfo(
                        default,
                        textures[textureIndex].View,
                        ImageLayout.ShaderReadOnlyOptimal);
                    imageInfos[textureIndex * 2 + 1] = new DescriptorImageInfo(
                        textures[textureIndex].Sampler,
                        default,
                        ImageLayout.Undefined);
                }

                fixed (DescriptorImageInfo* imageInfosPtr = imageInfos)
                fixed (WriteDescriptorSet* writesPtr = writes)
                {
                    for (var binding = 0; binding < writes.Length; binding++)
                    {
                        writes[binding] = new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = descriptorSet,
                            DstBinding = checked((uint)binding),
                            DescriptorCount = 1,
                            DescriptorType = binding % 2 == 0
                                ? DescriptorType.SampledImage
                                : DescriptorType.Sampler,
                            PImageInfo = &imageInfosPtr[binding]
                        };
                    }

                    state.Vk.UpdateDescriptorSets(state.Device, 14, writesPtr, 0, null);
                }

                state.MaterialDescriptorSets[key] = descriptorSet;
            }
        }

        private static DescriptorSet AllocateDescriptorSet(VulkanState state, DescriptorSetLayout setLayout)
        {
            var allocateInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = state.DescriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &setLayout
            };
            ThrowIfFailed(state.Vk.AllocateDescriptorSets(state.Device, &allocateInfo, out var descriptorSet), "vkAllocateDescriptorSets");
            return descriptorSet;
        }

        private static unsafe WriteDescriptorSet UniformWrite(
            DescriptorSet descriptorSet,
            DescriptorBufferInfo* bufferInfo,
            DescriptorType descriptorType = DescriptorType.UniformBuffer)
        {
            return new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = 0,
                DescriptorCount = 1,
                DescriptorType = descriptorType,
                PBufferInfo = bufferInfo
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
            out RekallAgeVulkanSceneShaderCompilationResult shaders,
            bool highDynamicRangeOutput = false,
            bool directionalShadows = false)
        {
            shaders = new RekallAgeVulkanShaderCompiler().CompileScenePipeline(
                RekallAgeVulkanScenePipelineDescription.Default,
                highDynamicRangeOutput,
                directionalShadows);
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

        private static void ValidateHighFidelityFormats(
            VulkanState state,
            RekallAgeVulkanSceneCommandPlan commandPlan,
            RekallAgeVulkanShadowPlan shadowPlan,
            RekallAgeVulkanFogPlan fogPlan,
            ICollection<string> errors)
        {
            if (HasPostPass(commandPlan, "bloom") || fogPlan.UsesFroxelGrid)
            {
                uint queueFamilyCount = 0;
                state.Vk.GetPhysicalDeviceQueueFamilyProperties(state.PhysicalDevice, &queueFamilyCount, null);
                var queueFamilies = stackalloc QueueFamilyProperties[checked((int)queueFamilyCount)];
                state.Vk.GetPhysicalDeviceQueueFamilyProperties(state.PhysicalDevice, &queueFamilyCount, queueFamilies);
                if (state.GraphicsQueueFamily >= queueFamilyCount
                    || (queueFamilies[state.GraphicsQueueFamily].QueueFlags & QueueFlags.ComputeBit) == 0)
                {
                    errors.Add(
                        $"REKALL_RENDER_COMPUTE_QUEUE_UNSUPPORTED: Vulkan graphics queue family {state.GraphicsQueueFamily} "
                        + $"on device '{state.SelectedDevice?.Name}' cannot execute the resolved compute passes.");
                }
            }

            ValidateFormat(
                state,
                Format.R16G16B16A16Sfloat,
                FormatFeatureFlags.ColorAttachmentBit
                    | FormatFeatureFlags.SampledImageBit
                    | FormatFeatureFlags.SampledImageFilterLinearBit
                    | (fogPlan.UsesFroxelGrid ? FormatFeatureFlags.StorageImageBit : 0),
                "scene-hdr",
                errors);
            if (fogPlan.UsesFroxelGrid)
            {
                ValidateFormat(
                    state,
                    Format.R16G16B16A16Sfloat,
                    FormatFeatureFlags.StorageImageBit | FormatFeatureFlags.SampledImageBit,
                    "fog-froxel",
                    errors);
                state.Vk.GetPhysicalDeviceProperties(state.PhysicalDevice, out var fogDeviceProperties);
                var maximumDimension = fogDeviceProperties.Limits.MaxImageDimension3D;
                if ((uint)fogPlan.Grid.Width > maximumDimension
                    || (uint)fogPlan.Grid.Height > maximumDimension
                    || (uint)fogPlan.Grid.Depth > maximumDimension)
                {
                    errors.Add(
                        $"REKALL_FOG_GRID_DEVICE_LIMIT_EXCEEDED: Resolved fog grid {fogPlan.Grid.Width}x{fogPlan.Grid.Height}x{fogPlan.Grid.Depth} "
                        + $"exceeds Vulkan maxImageDimension3D {maximumDimension} on device '{state.SelectedDevice?.Name}'.");
                }
            }
            if (HasPostPass(commandPlan, "bloom"))
            {
                ValidateFormat(
                    state,
                    Format.R16G16B16A16Sfloat,
                    FormatFeatureFlags.StorageImageBit
                        | FormatFeatureFlags.SampledImageBit
                        | FormatFeatureFlags.SampledImageFilterLinearBit,
                    "bloom-pyramid",
                    errors);
            }

            ValidateFormat(
                state,
                Format.R8G8B8A8Unorm,
                FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.TransferSrcBit,
                "ldr-color",
                errors);

            if (shadowPlan.Enabled)
            {
                state.Vk.GetPhysicalDeviceFormatProperties(state.PhysicalDevice, Format.D32Sfloat, out var properties);
                var formatDiagnostic = RekallAgeVulkanHighFidelityFormatValidator.ValidateShadowDepthFormat(
                    properties.OptimalTilingFeatures);
                if (formatDiagnostic is not null)
                {
                    errors.Add($"{formatDiagnostic} Vulkan device: '{state.SelectedDevice?.Name}'.");
                }

                state.Vk.GetPhysicalDeviceProperties(state.PhysicalDevice, out var physicalDeviceProperties);
                var limitDiagnostic = RekallAgeVulkanHighFidelityFormatValidator.ValidateShadowAtlasLimits(
                    checked((uint)shadowPlan.Resolution),
                    checked((uint)shadowPlan.Cascades.Count),
                    physicalDeviceProperties.Limits.MaxImageDimension2D,
                    physicalDeviceProperties.Limits.MaxImageArrayLayers);
                if (limitDiagnostic is not null)
                {
                    errors.Add($"{limitDiagnostic} Vulkan device: '{state.SelectedDevice?.Name}'.");
                }
            }
        }

        private static void ValidateFormat(
            VulkanState state,
            Format format,
            FormatFeatureFlags required,
            string resource,
            ICollection<string> errors)
        {
            state.Vk.GetPhysicalDeviceFormatProperties(state.PhysicalDevice, format, out var properties);
            var diagnostic = RekallAgeVulkanHighFidelityFormatValidator.ValidateOptimalTilingFeatures(
                format,
                properties.OptimalTilingFeatures,
                required,
                resource);
            if (diagnostic is not null)
            {
                errors.Add($"{diagnostic} Vulkan device: '{state.SelectedDevice?.Name}'.");
            }
        }

        private static void CreateHighFidelityImages(
            VulkanState state,
            RekallAgeVulkanSceneRenderTarget target,
            RekallAgeVulkanHighFidelityFramePlan plan,
            RekallAgeVulkanSceneCommandPlan commandPlan)
        {
            if (plan.FogPlan.UsesFroxelGrid)
            {
                state.FogWidth = checked((uint)plan.FogPlan.Grid.Width);
                state.FogHeight = checked((uint)plan.FogPlan.Grid.Height);
                state.FogDepth = checked((uint)plan.FogPlan.Grid.Depth);
                CreateImage3D(
                    state,
                    state.FogWidth,
                    state.FogHeight,
                    state.FogDepth,
                    Format.R16G16B16A16Sfloat,
                    ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit,
                    out state.FogImage,
                    out state.FogMemory,
                    out state.FogView);
                var fogBytes = PackFogVolumes(plan.FogPlan.Volumes);
                CreateHostBuffer(
                    state,
                    fogBytes,
                    BufferUsageFlags.StorageBufferBit,
                    out state.FogVolumeBuffer,
                    out state.FogVolumeMemory);
            }

            if (HasPostPass(commandPlan, "bloom"))
            {
                var bloom = plan.Graph.Resources.Single(resource => resource.Name == "bloom-pyramid");
                state.BloomWidth = checked((uint)bloom.Width);
                state.BloomHeight = checked((uint)bloom.Height);
                CreateImage(
                    state,
                    state.BloomWidth,
                    state.BloomHeight,
                    Format.R16G16B16A16Sfloat,
                    ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit,
                    ImageAspectFlags.ColorBit,
                    1,
                    out state.BloomImage,
                    out state.BloomMemory,
                    out state.BloomView);
            }
            CreateFogAndTransparentRenderPasses(state, target);
            CreateImage(
                state,
                target.EffectiveOutputWidth,
                target.EffectiveOutputHeight,
                target.EffectiveOutputColorFormat,
                ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
                ImageAspectFlags.ColorBit,
                1,
                out state.OutputImage,
                out state.OutputMemory,
                out state.OutputView);

            var outputAttachment = new AttachmentDescription
            {
                Format = target.EffectiveOutputColorFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.TransferSrcOptimal
            };
            var outputReference = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
            var subpass = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &outputReference
            };
            var dependency = new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.FragmentShaderBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = AccessFlags.ShaderReadBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit
            };
            var renderPassInfo = new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = 1,
                PAttachments = &outputAttachment,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = 1,
                PDependencies = &dependency
            };
            ThrowIfFailed(state.Vk.CreateRenderPass(state.Device, &renderPassInfo, null, out state.OutputRenderPass), "vkCreateRenderPass tone-map");

            var outputView = state.OutputView;
            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = state.OutputRenderPass,
                AttachmentCount = 1,
                PAttachments = &outputView,
                Width = target.EffectiveOutputWidth,
                Height = target.EffectiveOutputHeight,
                Layers = 1
            };
            ThrowIfFailed(state.Vk.CreateFramebuffer(state.Device, &framebufferInfo, null, out state.OutputFramebuffer), "vkCreateFramebuffer tone-map");
        }

        private static void CreateFogAndTransparentRenderPasses(
            VulkanState state,
            RekallAgeVulkanSceneRenderTarget target)
        {
            var fogColor = new AttachmentDescription
            {
                Format = target.ColorFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Load,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.ShaderReadOnlyOptimal,
                FinalLayout = ImageLayout.ShaderReadOnlyOptimal
            };
            var fogColorReference = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
            var fogSubpass = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &fogColorReference
            };
            var fogDependency = new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit
            };
            var fogPassInfo = new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = 1,
                PAttachments = &fogColor,
                SubpassCount = 1,
                PSubpasses = &fogSubpass,
                DependencyCount = 1,
                PDependencies = &fogDependency
            };
            ThrowIfFailed(state.Vk.CreateRenderPass(state.Device, &fogPassInfo, null, out state.FogCompositeRenderPass), "vkCreateRenderPass analytic fog");
            var colorView = state.ColorView;
            var fogFramebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = state.FogCompositeRenderPass,
                AttachmentCount = 1,
                PAttachments = &colorView,
                Width = target.Width,
                Height = target.Height,
                Layers = 1
            };
            ThrowIfFailed(state.Vk.CreateFramebuffer(state.Device, &fogFramebufferInfo, null, out state.FogCompositeFramebuffer), "vkCreateFramebuffer analytic fog");

            var transparentColor = fogColor;
            var transparentDepth = new AttachmentDescription
            {
                Format = target.DepthFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Load,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.DepthStencilAttachmentOptimal,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
            };
            var transparentAttachments = stackalloc AttachmentDescription[] { transparentColor, transparentDepth };
            var transparentColorReference = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
            var transparentDepthReference = new AttachmentReference(1, ImageLayout.DepthStencilAttachmentOptimal);
            var transparentSubpass = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &transparentColorReference,
                PDepthStencilAttachment = &transparentDepthReference
            };
            var transparentPassInfo = new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = 2,
                PAttachments = transparentAttachments,
                SubpassCount = 1,
                PSubpasses = &transparentSubpass,
                DependencyCount = 1,
                PDependencies = &fogDependency
            };
            ThrowIfFailed(state.Vk.CreateRenderPass(state.Device, &transparentPassInfo, null, out state.TransparentRenderPass), "vkCreateRenderPass transparent");
            var transparentViews = stackalloc ImageView[] { state.ColorView, state.DepthView };
            var transparentFramebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = state.TransparentRenderPass,
                AttachmentCount = 2,
                PAttachments = transparentViews,
                Width = target.Width,
                Height = target.Height,
                Layers = 1
            };
            ThrowIfFailed(state.Vk.CreateFramebuffer(state.Device, &transparentFramebufferInfo, null, out state.TransparentFramebuffer), "vkCreateFramebuffer transparent");
        }

        private static void CreateHighFidelityPostPipeline(
            VulkanState state,
            RekallAgeVulkanSceneRenderTarget target,
            RekallAgeVulkanHighFidelityFramePlan plan,
            RekallAgeVulkanSceneCommandPlan commandPlan,
            ICollection<string> errors)
        {
            var hasBloom = HasPostPass(commandPlan, "bloom");
            var hasFog = plan.FogPlan.UsesFroxelGrid;
            var compiled = new RekallAgeVulkanShaderCompiler().CompileHighFidelityPostPipeline();
            if (!compiled.Compiled)
            {
                foreach (var error in compiled.Errors)
                {
                    errors.Add(error);
                }
                return;
            }

            fixed (byte* fogCode = compiled.Fog.Spirv)
            fixed (byte* analyticFogCode = compiled.AnalyticFog.Spirv)
            fixed (byte* bloomCode = compiled.Bloom.Spirv)
            fixed (byte* vertexCode = compiled.FullscreenVertex.Spirv)
            fixed (byte* toneCode = compiled.ToneMap.Spirv)
            {
                if (hasFog)
                {
                    state.FogShader = CreateShaderModule(state, fogCode, compiled.Fog.Spirv.Length);
                }

                if (hasBloom)
                {
                    state.BloomShader = CreateShaderModule(state, bloomCode, compiled.Bloom.Spirv.Length);
                }

                state.FullscreenVertexShader = CreateShaderModule(state, vertexCode, compiled.FullscreenVertex.Spirv.Length);
                state.AnalyticFogShader = CreateShaderModule(state, analyticFogCode, compiled.AnalyticFog.Spirv.Length);
                state.ToneMapShader = CreateShaderModule(state, toneCode, compiled.ToneMap.Spirv.Length);
            }

            var samplerInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                MipmapMode = SamplerMipmapMode.Linear,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
                MaxLod = 0
            };
            ThrowIfFailed(state.Vk.CreateSampler(state.Device, &samplerInfo, null, out state.PostSampler), "vkCreateSampler post");

            if (hasFog)
            {
                var fogBindings = stackalloc DescriptorSetLayoutBinding[]
                {
                    new(0, DescriptorType.StorageImage, 1, ShaderStageFlags.ComputeBit),
                    new(1, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
                    new(2, DescriptorType.StorageImage, 1, ShaderStageFlags.ComputeBit)
                };
                var fogLayoutInfo = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = 3,
                    PBindings = fogBindings
                };
                ThrowIfFailed(state.Vk.CreateDescriptorSetLayout(state.Device, &fogLayoutInfo, null, out state.FogDescriptorSetLayout), "vkCreateDescriptorSetLayout fog");
            }

            if (hasBloom)
            {
                var bloomBindings = stackalloc DescriptorSetLayoutBinding[]
                {
                    new(0, DescriptorType.CombinedImageSampler, 1, ShaderStageFlags.ComputeBit),
                    new(1, DescriptorType.StorageImage, 1, ShaderStageFlags.ComputeBit)
                };
                var bloomLayoutInfo = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = 2,
                    PBindings = bloomBindings
                };
                ThrowIfFailed(state.Vk.CreateDescriptorSetLayout(state.Device, &bloomLayoutInfo, null, out state.BloomDescriptorSetLayout), "vkCreateDescriptorSetLayout bloom");
            }

            var toneBindings = stackalloc DescriptorSetLayoutBinding[]
            {
                new(0, DescriptorType.CombinedImageSampler, 1, ShaderStageFlags.FragmentBit),
                new(1, DescriptorType.CombinedImageSampler, 1, ShaderStageFlags.FragmentBit)
            };
            var toneLayoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 2,
                PBindings = toneBindings
            };
            ThrowIfFailed(state.Vk.CreateDescriptorSetLayout(state.Device, &toneLayoutInfo, null, out state.ToneMapDescriptorSetLayout), "vkCreateDescriptorSetLayout tone-map");

            var poolSizes = stackalloc DescriptorPoolSize[]
            {
                new(DescriptorType.CombinedImageSampler, 3),
                new(DescriptorType.StorageImage, 3),
                new(DescriptorType.StorageBuffer, 1)
            };
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = 3,
                PoolSizeCount = 3,
                PPoolSizes = poolSizes
            };
            ThrowIfFailed(state.Vk.CreateDescriptorPool(state.Device, &poolInfo, null, out state.PostDescriptorPool), "vkCreateDescriptorPool post");
            if (hasFog)
            {
                state.FogDescriptorSet = AllocateDescriptorSet(state, state.PostDescriptorPool, state.FogDescriptorSetLayout);
            }
            if (hasBloom)
            {
                state.BloomDescriptorSet = AllocateDescriptorSet(state, state.PostDescriptorPool, state.BloomDescriptorSetLayout);
            }

            state.ToneMapDescriptorSet = AllocateDescriptorSet(state, state.PostDescriptorPool, state.ToneMapDescriptorSetLayout);

            if (hasFog)
            {
                var fogImages = stackalloc DescriptorImageInfo[]
                {
                    new(default, state.FogView, ImageLayout.General),
                    new(default, state.ColorView, ImageLayout.General)
                };
                var fogBuffer = new DescriptorBufferInfo(state.FogVolumeBuffer, 0, Vk.WholeSize);
                var fogWrites = stackalloc WriteDescriptorSet[]
                {
                    ImageWrite(state.FogDescriptorSet, 0, DescriptorType.StorageImage, &fogImages[0]),
                    new()
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = state.FogDescriptorSet,
                        DstBinding = 1,
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.StorageBuffer,
                        PBufferInfo = &fogBuffer
                    },
                    ImageWrite(state.FogDescriptorSet, 2, DescriptorType.StorageImage, &fogImages[1])
                };
                state.Vk.UpdateDescriptorSets(state.Device, 3, fogWrites, 0, null);
            }

            if (hasBloom)
            {
                var bloomImages = stackalloc DescriptorImageInfo[]
                {
                    new(state.PostSampler, state.ColorView, ImageLayout.ShaderReadOnlyOptimal),
                    new(default, state.BloomView, ImageLayout.General)
                };
                var bloomWrites = stackalloc WriteDescriptorSet[]
                {
                    ImageWrite(state.BloomDescriptorSet, 0, DescriptorType.CombinedImageSampler, &bloomImages[0]),
                    ImageWrite(state.BloomDescriptorSet, 1, DescriptorType.StorageImage, &bloomImages[1])
                };
                state.Vk.UpdateDescriptorSets(state.Device, 2, bloomWrites, 0, null);
            }

            var toneImages = stackalloc DescriptorImageInfo[]
            {
                new(state.PostSampler, state.ColorView, ImageLayout.ShaderReadOnlyOptimal),
                new(state.PostSampler, hasBloom ? state.BloomView : state.ColorView, ImageLayout.ShaderReadOnlyOptimal)
            };
            var toneWrites = stackalloc WriteDescriptorSet[]
            {
                ImageWrite(state.ToneMapDescriptorSet, 0, DescriptorType.CombinedImageSampler, &toneImages[0]),
                ImageWrite(state.ToneMapDescriptorSet, 1, DescriptorType.CombinedImageSampler, &toneImages[1])
            };
            state.Vk.UpdateDescriptorSets(state.Device, 2, toneWrites, 0, null);

            if (hasBloom)
            {
                var bloomPushRange = new PushConstantRange(ShaderStageFlags.ComputeBit, 0, (uint)Marshal.SizeOf<BloomPushConstants>());
                var bloomSetLayout = state.BloomDescriptorSetLayout;
                var bloomPipelineLayoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = &bloomSetLayout,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges = &bloomPushRange
                };
                ThrowIfFailed(state.Vk.CreatePipelineLayout(state.Device, &bloomPipelineLayoutInfo, null, out state.BloomPipelineLayout), "vkCreatePipelineLayout bloom");
            }

            if (hasFog)
            {
                var fogPushRange = new PushConstantRange(ShaderStageFlags.ComputeBit, 0, (uint)Marshal.SizeOf<FogPushConstants>());
                var fogSetLayout = state.FogDescriptorSetLayout;
                var fogPipelineLayoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = &fogSetLayout,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges = &fogPushRange
                };
                ThrowIfFailed(state.Vk.CreatePipelineLayout(state.Device, &fogPipelineLayoutInfo, null, out state.FogPipelineLayout), "vkCreatePipelineLayout fog");
            }

            var entry = "main\0"u8.ToArray();
            fixed (byte* entryName = entry)
            {
                CreateAnalyticFogPipeline(state, target, entryName);

                if (hasFog)
                {
                    var fogStage = new PipelineShaderStageCreateInfo
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.ComputeBit,
                        Module = state.FogShader,
                        PName = entryName
                    };
                    var fogInfo = new ComputePipelineCreateInfo
                    {
                        SType = StructureType.ComputePipelineCreateInfo,
                        Stage = fogStage,
                        Layout = state.FogPipelineLayout
                    };
                    ThrowIfFailed(state.Vk.CreateComputePipelines(state.Device, default, 1, &fogInfo, null, out state.FogPipeline), "vkCreateComputePipelines fog");
                }

                if (hasBloom)
                {
                    var computeStage = new PipelineShaderStageCreateInfo
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.ComputeBit,
                        Module = state.BloomShader,
                        PName = entryName
                    };
                    var computeInfo = new ComputePipelineCreateInfo
                    {
                        SType = StructureType.ComputePipelineCreateInfo,
                        Stage = computeStage,
                        Layout = state.BloomPipelineLayout
                    };
                    ThrowIfFailed(state.Vk.CreateComputePipelines(state.Device, default, 1, &computeInfo, null, out state.BloomPipeline), "vkCreateComputePipelines bloom");
                }

                var tonePushRange = new PushConstantRange(ShaderStageFlags.FragmentBit, 0, (uint)Marshal.SizeOf<ToneMapPushConstants>());
                var toneSetLayout = state.ToneMapDescriptorSetLayout;
                var tonePipelineLayoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = &toneSetLayout,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges = &tonePushRange
                };
                ThrowIfFailed(state.Vk.CreatePipelineLayout(state.Device, &tonePipelineLayoutInfo, null, out state.ToneMapPipelineLayout), "vkCreatePipelineLayout tone-map");

                var stages = stackalloc PipelineShaderStageCreateInfo[]
                {
                    new()
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.VertexBit,
                        Module = state.FullscreenVertexShader,
                        PName = entryName
                    },
                    new()
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.FragmentBit,
                        Module = state.ToneMapShader,
                        PName = entryName
                    }
                };
                var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
                var inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList
                };
                var viewport = new Viewport(0, 0, target.EffectiveOutputWidth, target.EffectiveOutputHeight, 0, 1);
                var scissor = new Rect2D(
                    new Offset2D(0, 0),
                    new Extent2D(target.EffectiveOutputWidth, target.EffectiveOutputHeight));
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
                var blendAttachment = new PipelineColorBlendAttachmentState
                {
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
                };
                var blend = new PipelineColorBlendStateCreateInfo
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    AttachmentCount = 1,
                    PAttachments = &blendAttachment
                };
                var pipelineInfo = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = stages,
                    PVertexInputState = &vertexInput,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterization,
                    PMultisampleState = &multisample,
                    PColorBlendState = &blend,
                    Layout = state.ToneMapPipelineLayout,
                    RenderPass = state.OutputRenderPass
                };
                ThrowIfFailed(state.Vk.CreateGraphicsPipelines(state.Device, default, 1, &pipelineInfo, null, out state.ToneMapPipeline), "vkCreateGraphicsPipelines tone-map");
            }
        }

        private static void CreateAnalyticFogPipeline(
            VulkanState state,
            RekallAgeVulkanSceneRenderTarget target,
            byte* entryName)
        {
            var pushRange = new PushConstantRange(
                ShaderStageFlags.FragmentBit,
                0,
                (uint)Marshal.SizeOf<AnalyticFogPushConstants>());
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushRange
            };
            ThrowIfFailed(
                state.Vk.CreatePipelineLayout(state.Device, &layoutInfo, null, out state.AnalyticFogPipelineLayout),
                "vkCreatePipelineLayout analytic fog");
            var stages = stackalloc PipelineShaderStageCreateInfo[]
            {
                new()
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.VertexBit,
                    Module = state.FullscreenVertexShader,
                    PName = entryName
                },
                new()
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.FragmentBit,
                    Module = state.AnalyticFogShader,
                    PName = entryName
                }
            };
            var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList
            };
            var viewport = new Viewport(0, 0, target.Width, target.Height, 0, 1);
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(target.Width, target.Height));
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
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
            };
            var blend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &blendAttachment
            };
            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterization,
                PMultisampleState = &multisample,
                PColorBlendState = &blend,
                Layout = state.AnalyticFogPipelineLayout,
                RenderPass = state.FogCompositeRenderPass
            };
            ThrowIfFailed(
                state.Vk.CreateGraphicsPipelines(state.Device, default, 1, &pipelineInfo, null, out state.AnalyticFogPipeline),
                "vkCreateGraphicsPipelines analytic fog");
        }

        private static DescriptorSet AllocateDescriptorSet(
            VulkanState state,
            DescriptorPool pool,
            DescriptorSetLayout layout)
        {
            var allocateInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = pool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout
            };
            ThrowIfFailed(state.Vk.AllocateDescriptorSets(state.Device, &allocateInfo, out var descriptorSet), "vkAllocateDescriptorSets post");
            return descriptorSet;
        }

        private static WriteDescriptorSet ImageWrite(
            DescriptorSet descriptorSet,
            uint binding,
            DescriptorType type,
            DescriptorImageInfo* imageInfo) =>
            new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = binding,
                DescriptorCount = 1,
                DescriptorType = type,
                PImageInfo = imageInfo
            };

        private static void CreatePipeline(
            VulkanState state,
            RekallAgeRuntimeViewportFrame frame,
            RekallAgeVulkanSceneRenderTarget target,
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

                var bindingDescription = new VertexInputBindingDescription(0, (uint)Marshal.SizeOf<RekallAgeVulkanSceneGpuVertex>(), VertexInputRate.Vertex);
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
                var cameraRect = RekallAgeRuntimeViewportCameraRect.FromFrame(frame);
                var renderScaleX = (double)target.Width / Math.Max(1, frame.Width);
                var renderScaleY = (double)target.Height / Math.Max(1, frame.Height);
                var renderX = checked((int)Math.Round(cameraRect.X * renderScaleX, MidpointRounding.AwayFromZero));
                var renderY = checked((int)Math.Round(cameraRect.Y * renderScaleY, MidpointRounding.AwayFromZero));
                var renderWidth = checked((uint)Math.Max(1, Math.Round(cameraRect.Width * renderScaleX, MidpointRounding.AwayFromZero)));
                var renderHeight = checked((uint)Math.Max(1, Math.Round(cameraRect.Height * renderScaleY, MidpointRounding.AwayFromZero)));
                var viewport = new Viewport(renderX, renderY, renderWidth, renderHeight, 0, 1);
                var scissor = new Rect2D(new Offset2D(renderX, renderY), new Extent2D(renderWidth, renderHeight));
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
                    BlendEnable = true,
                    SrcColorBlendFactor = BlendFactor.SrcAlpha,
                    DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                    ColorBlendOp = BlendOp.Add,
                    SrcAlphaBlendFactor = BlendFactor.One,
                    DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                    AlphaBlendOp = BlendOp.Add,
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
                };
                var colorBlend = new PipelineColorBlendStateCreateInfo
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    AttachmentCount = 1,
                    PAttachments = &colorBlendAttachment
                };
                var setLayouts = stackalloc DescriptorSetLayout[]
                {
                    state.DescriptorSetLayout,
                    state.DrawDescriptorSetLayout,
                    state.MaterialDescriptorSetLayout,
                    state.ShadowSampleDescriptorSetLayout
                };
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = state.ShadowSampleDescriptorSetLayout.Handle != 0 ? 4u : 3u,
                    PSetLayouts = setLayouts
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
                depth.DepthWriteEnable = false;
                ThrowIfFailed(state.Vk.CreateGraphicsPipelines(state.Device, default, 1, &pipelineInfo, null, out state.TransparentPipeline), "vkCreateGraphicsPipelines transparent");
            }
        }

        private static void CreateProjectPipelines(
            VulkanState state,
            RekallAgeRuntimeViewportFrame frame,
            RekallAgeVulkanSceneRenderTarget target,
            IReadOnlyDictionary<RekallAgeRuntimeViewportShaderPipeline, RekallAgeResolvedShaderPipeline>? resolvedPipelines)
        {
            if (resolvedPipelines is null || resolvedPipelines.Count == 0)
            {
                return;
            }

            foreach (var (reference, asset) in resolvedPipelines)
            {
                if (!asset.Valid)
                {
                    throw new InvalidOperationException(
                        $"Cannot create invalid project shader pipeline '{reference.VertexShader}' + '{reference.FragmentShader}'.");
                }

                if (state.ProjectPipelineByContentHash.TryGetValue(asset.Key.ContentHash, out var cached))
                {
                    state.ProjectPipelines.Add(reference, cached);
                    continue;
                }

                var defaultLayout = state.PipelineLayout;
                var defaultOpaque = state.Pipeline;
                var defaultTransparent = state.TransparentPipeline;
                var defaultVertex = state.VertexShader;
                var defaultFragment = state.FragmentShader;
                state.PipelineLayout = default;
                state.Pipeline = default;
                state.TransparentPipeline = default;
                state.VertexShader = default;
                state.FragmentShader = default;
                var transferred = false;
                try
                {
                    CreatePipeline(
                        state,
                        frame,
                        target,
                        new RekallAgeVulkanSceneShaderCompilationResult(
                            true,
                            new RekallAgeVulkanCompiledShader(
                                RekallAgeVulkanShaderStage.Vertex,
                                asset.VertexName,
                                asset.VertexSpirv),
                            new RekallAgeVulkanCompiledShader(
                                RekallAgeVulkanShaderStage.Fragment,
                                asset.FragmentName,
                                asset.FragmentSpirv),
                            []));
                    var resource = new VulkanProjectPipelineResource(
                        state.PipelineLayout,
                        state.Pipeline,
                        state.TransparentPipeline,
                        state.VertexShader,
                        state.FragmentShader);
                    state.ProjectPipelineResources.Add(resource);
                    state.ProjectPipelineByContentHash.Add(asset.Key.ContentHash, resource);
                    state.ProjectPipelines.Add(reference, resource);
                    transferred = true;
                }
                finally
                {
                    if (!transferred)
                    {
                        new VulkanProjectPipelineResource(
                            state.PipelineLayout,
                            state.Pipeline,
                            state.TransparentPipeline,
                            state.VertexShader,
                            state.FragmentShader).Dispose(state.Vk, state.Device);
                    }

                    state.PipelineLayout = defaultLayout;
                    state.Pipeline = defaultOpaque;
                    state.TransparentPipeline = defaultTransparent;
                    state.VertexShader = defaultVertex;
                    state.FragmentShader = defaultFragment;
                }
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

        private static void CreateShadowPipeline(
            VulkanState state,
            RekallAgeVulkanShadowPlan plan,
            ICollection<string> errors)
        {
            var shaders = new RekallAgeVulkanShaderCompiler().CompileScenePipeline(
                RekallAgeVulkanScenePipelineDescription.Shadow);
            if (!shaders.Compiled)
            {
                foreach (var error in shaders.Errors)
                {
                    errors.Add(error);
                }
                return;
            }

            fixed (byte* vertexCode = shaders.Vertex.Spirv)
            fixed (byte* fragmentCode = shaders.Fragment.Spirv)
            {
                state.ShadowVertexShader = CreateShaderModule(state, vertexCode, shaders.Vertex.Spirv.Length);
                state.ShadowFragmentShader = CreateShaderModule(state, fragmentCode, shaders.Fragment.Spirv.Length);
            }

            var entry = "main\0"u8.ToArray();
            fixed (byte* entryName = entry)
            {
                var stages = stackalloc PipelineShaderStageCreateInfo[]
                {
                    new()
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.VertexBit,
                        Module = state.ShadowVertexShader,
                        PName = entryName
                    },
                    new()
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.FragmentBit,
                        Module = state.ShadowFragmentShader,
                        PName = entryName
                    }
                };
                var binding = new VertexInputBindingDescription(
                    0,
                    (uint)Marshal.SizeOf<RekallAgeVulkanSceneGpuVertex>(),
                    VertexInputRate.Vertex);
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
                    PVertexBindingDescriptions = &binding,
                    VertexAttributeDescriptionCount = 4,
                    PVertexAttributeDescriptions = attributes
                };
                var inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList
                };
                var resolution = checked((uint)plan.Resolution);
                var viewport = new Viewport(0, 0, resolution, resolution, 0, 1);
                var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(resolution, resolution));
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
                var colorBlend = new PipelineColorBlendStateCreateInfo
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    AttachmentCount = 0
                };
                var setLayouts = stackalloc DescriptorSetLayout[]
                {
                    state.DescriptorSetLayout,
                    state.DrawDescriptorSetLayout,
                    state.MaterialDescriptorSetLayout
                };
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 3,
                    PSetLayouts = setLayouts
                };
                ThrowIfFailed(
                    state.Vk.CreatePipelineLayout(state.Device, &layoutInfo, null, out state.ShadowPipelineLayout),
                    "vkCreatePipelineLayout shadow");
                var pipelineInfo = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = stages,
                    PVertexInputState = &vertexInput,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterization,
                    PMultisampleState = &multisample,
                    PDepthStencilState = &depth,
                    PColorBlendState = &colorBlend,
                    Layout = state.ShadowPipelineLayout,
                    RenderPass = state.ShadowRenderPass
                };
                ThrowIfFailed(
                    state.Vk.CreateGraphicsPipelines(state.Device, default, 1, &pipelineInfo, null, out state.ShadowPipeline),
                    "vkCreateGraphicsPipelines shadow");
            }
        }

        private static Sampler CreateShadowSampler(VulkanState state)
        {
            var createInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                MipmapMode = SamplerMipmapMode.Nearest,
                AddressModeU = SamplerAddressMode.ClampToBorder,
                AddressModeV = SamplerAddressMode.ClampToBorder,
                AddressModeW = SamplerAddressMode.ClampToBorder,
                CompareEnable = true,
                CompareOp = CompareOp.LessOrEqual,
                MinLod = 0,
                MaxLod = 0,
                BorderColor = BorderColor.FloatOpaqueWhite
            };
            ThrowIfFailed(state.Vk.CreateSampler(state.Device, &createInfo, null, out var sampler), "vkCreateSampler shadow");
            return sampler;
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
                QueueFamilyIndex = state.GraphicsQueueFamily,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit
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

        private static void RecordCommands(
            VulkanState state,
            RekallAgeVulkanSceneCommandPlan commandPlan,
            bool uploadTextures = true)
        {
            var target = commandPlan.PreparedFrame.Target;
            var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
            state.Vk.ResetCommandBuffer(state.CommandBuffer, 0);
            ThrowIfFailed(state.Vk.BeginCommandBuffer(state.CommandBuffer, &beginInfo), "vkBeginCommandBuffer");
            if (uploadTextures)
            {
                RecordTextureUploads(state);
            }

            var clearValues = stackalloc ClearValue[2];
            clearValues[0].Color = new ClearColorValue(0.08f, 0.10f, 0.14f, 1f);
            clearValues[1].DepthStencil = new ClearDepthStencilValue(1f, 0);
            for (var passIndex = 0; passIndex < commandPlan.RenderPasses.Count; passIndex++)
            {
                var pass = commandPlan.RenderPasses[passIndex];
                var framebufferIndex = checked((int)Math.Min(pass.FramebufferIndex, (uint)Math.Max(0, state.Framebuffers.Length - 1)));
                var renderPassBegin = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = state.RenderPass,
                    Framebuffer = state.Framebuffers.Length > 0 ? state.Framebuffers[framebufferIndex] : state.Framebuffer,
                    RenderArea = new Rect2D(
                        new Offset2D((int)pass.Viewport.X, (int)pass.Viewport.Y),
                        new Extent2D((uint)pass.Viewport.Z, (uint)pass.Viewport.W)),
                    ClearValueCount = 2,
                    PClearValues = clearValues
                };

                state.Vk.CmdBeginRenderPass(state.CommandBuffer, &renderPassBegin, SubpassContents.Inline);
                var vertexBuffer = state.VertexBuffer;
                var offset = 0UL;
                state.Vk.CmdBindVertexBuffers(state.CommandBuffer, 0, 1, &vertexBuffer, &offset);
                state.Vk.CmdBindIndexBuffer(state.CommandBuffer, state.IndexBuffer, 0, IndexType.Uint32);
                DrawPassRanges(state, passIndex, pass.Draws, transparent: false);
                DrawPassRanges(state, passIndex, pass.Draws, transparent: true);

                state.Vk.CmdEndRenderPass(state.CommandBuffer);
            }

            if (commandPlan.CopiesColorToReadback && state.ReadbackBuffer.Handle != 0)
            {
                var copy = new BufferImageCopy
                {
                    BufferOffset = 0,
                    BufferRowLength = 0,
                    BufferImageHeight = 0,
                    ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    ImageOffset = new Offset3D(0, 0, 0),
                    ImageExtent = new Extent3D(target.Width, target.Height, 1)
                };
                state.Vk.CmdCopyImageToBuffer(state.CommandBuffer, state.ColorImage, ImageLayout.TransferSrcOptimal, state.ReadbackBuffer, 1, &copy);
            }

            ThrowIfFailed(state.Vk.EndCommandBuffer(state.CommandBuffer), "vkEndCommandBuffer");
        }

        private static void RecordHighFidelityCommands(
            VulkanState state,
            RekallAgeVulkanSceneCommandPlan commandPlan,
            RekallAgeVulkanHighFidelityFramePlan highFidelityPlan)
        {
            var target = commandPlan.PreparedFrame.Target;
            var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
            state.Vk.ResetCommandBuffer(state.CommandBuffer, 0);
            ThrowIfFailed(state.Vk.BeginCommandBuffer(state.CommandBuffer, &beginInfo), "vkBeginCommandBuffer HDR");
            RecordTextureUploads(state);
            if (highFidelityPlan.ShadowPlan.Enabled)
            {
                RecordShadowCommands(state, commandPlan, highFidelityPlan.ShadowPlan);
            }

            var sceneClears = stackalloc ClearValue[2];
            sceneClears[0].Color = new ClearColorValue(0.006f, 0.01f, 0.018f, 1f);
            sceneClears[1].DepthStencil = new ClearDepthStencilValue(1f, 0);
            var pass = commandPlan.RenderPasses[0];
            var scenePass = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = state.RenderPass,
                Framebuffer = state.Framebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(target.Width, target.Height)),
                ClearValueCount = 2,
                PClearValues = sceneClears
            };
            state.Vk.CmdBeginRenderPass(state.CommandBuffer, &scenePass, SubpassContents.Inline);
            var vertexBuffer = state.VertexBuffer;
            var vertexOffset = 0UL;
            state.Vk.CmdBindVertexBuffers(state.CommandBuffer, 0, 1, &vertexBuffer, &vertexOffset);
            state.Vk.CmdBindIndexBuffer(state.CommandBuffer, state.IndexBuffer, 0, IndexType.Uint32);
            DrawPassRanges(state, 0, pass.Draws, transparent: false);
            state.Vk.CmdEndRenderPass(state.CommandBuffer);

            if (highFidelityPlan.ShadowPlan.Enabled && state.ShadowReadbackBuffer.Handle != 0)
            {
                RecordShadowDebugCopies(state, highFidelityPlan.ShadowPlan);
            }

            if (highFidelityPlan.FogPlan.UsesFroxelGrid)
            {
                RecordFroxelFogCommands(state, commandPlan, highFidelityPlan.FogPlan);
            }
            else if (highFidelityPlan.FogPlan.Enabled)
            {
                RecordAnalyticFogCommands(state, target, highFidelityPlan.FogPlan);
            }
            else
            {
                TransitionImage(
                    state,
                    state.ColorImage,
                    ImageLayout.ShaderReadOnlyOptimal,
                    ImageLayout.ShaderReadOnlyOptimal,
                    AccessFlags.ColorAttachmentWriteBit,
                    AccessFlags.ShaderReadBit,
                    PipelineStageFlags.ColorAttachmentOutputBit,
                    PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit);
            }

            RecordTransparentCommands(state, commandPlan);

            if (HasPostPass(commandPlan, "bloom"))
            {
                TransitionImage(
                    state,
                    state.BloomImage,
                    ImageLayout.Undefined,
                    ImageLayout.General,
                    0,
                    AccessFlags.ShaderWriteBit,
                    PipelineStageFlags.TopOfPipeBit,
                    PipelineStageFlags.ComputeShaderBit);
                state.Vk.CmdBindPipeline(state.CommandBuffer, PipelineBindPoint.Compute, state.BloomPipeline);
                var bloomSet = state.BloomDescriptorSet;
                state.Vk.CmdBindDescriptorSets(
                    state.CommandBuffer,
                    PipelineBindPoint.Compute,
                    state.BloomPipelineLayout,
                    0,
                    1,
                    &bloomSet,
                    0,
                    null);
                var bloomParameters = new BloomPushConstants(
                    checked((float)highFidelityPlan.PostSettings.BloomThreshold),
                    checked((float)highFidelityPlan.PostSettings.BloomIntensity),
                    target.Width,
                    target.Height);
                state.Vk.CmdPushConstants(
                    state.CommandBuffer,
                    state.BloomPipelineLayout,
                    ShaderStageFlags.ComputeBit,
                    0,
                    (uint)Marshal.SizeOf<BloomPushConstants>(),
                    &bloomParameters);
                state.Vk.CmdDispatch(
                    state.CommandBuffer,
                    (state.BloomWidth + 7) / 8,
                    (state.BloomHeight + 7) / 8,
                    1);
                TransitionImage(
                    state,
                    state.BloomImage,
                    ImageLayout.General,
                    ImageLayout.ShaderReadOnlyOptimal,
                    AccessFlags.ShaderWriteBit,
                    AccessFlags.ShaderReadBit,
                    PipelineStageFlags.ComputeShaderBit,
                    PipelineStageFlags.FragmentShaderBit);
            }

            var outputClear = new ClearValue { Color = new ClearColorValue(0, 0, 0, 1) };
            var outputPass = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = state.OutputRenderPass,
                Framebuffer = state.OutputFramebuffer,
                RenderArea = new Rect2D(
                    new Offset2D(0, 0),
                    new Extent2D(target.EffectiveOutputWidth, target.EffectiveOutputHeight)),
                ClearValueCount = 1,
                PClearValues = &outputClear
            };
            state.Vk.CmdBeginRenderPass(state.CommandBuffer, &outputPass, SubpassContents.Inline);
            state.Vk.CmdBindPipeline(state.CommandBuffer, PipelineBindPoint.Graphics, state.ToneMapPipeline);
            var toneSet = state.ToneMapDescriptorSet;
            state.Vk.CmdBindDescriptorSets(
                state.CommandBuffer,
                PipelineBindPoint.Graphics,
                state.ToneMapPipelineLayout,
                0,
                1,
                &toneSet,
                0,
                null);
            var toneParameters = new ToneMapPushConstants(
                checked((float)highFidelityPlan.PostSettings.Exposure),
                checked((float)highFidelityPlan.PostSettings.WhitePoint),
                checked((float)highFidelityPlan.PostSettings.Saturation),
                checked((float)highFidelityPlan.PostSettings.Contrast),
                checked((float)highFidelityPlan.PostSettings.GradeStrength),
                checked((float)highFidelityPlan.PostSettings.BloomIntensity),
                checked((float)highFidelityPlan.PostSettings.BloomRadius),
                0);
            state.Vk.CmdPushConstants(
                state.CommandBuffer,
                state.ToneMapPipelineLayout,
                ShaderStageFlags.FragmentBit,
                0,
                (uint)Marshal.SizeOf<ToneMapPushConstants>(),
                &toneParameters);
            state.Vk.CmdDraw(state.CommandBuffer, 3, 1, 0, 0);
            state.Vk.CmdEndRenderPass(state.CommandBuffer);

            TransitionImage(
                state,
                state.OutputImage,
                ImageLayout.TransferSrcOptimal,
                ImageLayout.TransferSrcOptimal,
                AccessFlags.ColorAttachmentWriteBit,
                AccessFlags.TransferReadBit,
                PipelineStageFlags.ColorAttachmentOutputBit,
                PipelineStageFlags.TransferBit);

            var copy = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(target.EffectiveOutputWidth, target.EffectiveOutputHeight, 1)
            };
            state.Vk.CmdCopyImageToBuffer(
                state.CommandBuffer,
                state.OutputImage,
                ImageLayout.TransferSrcOptimal,
                state.ReadbackBuffer,
                1,
                &copy);
            ThrowIfFailed(state.Vk.EndCommandBuffer(state.CommandBuffer), "vkEndCommandBuffer HDR");
        }

        private static void RecordAnalyticFogCommands(
            VulkanState state,
            RekallAgeVulkanSceneRenderTarget target,
            RekallAgeVulkanFogPlan fogPlan)
        {
            var begin = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = state.FogCompositeRenderPass,
                Framebuffer = state.FogCompositeFramebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(target.Width, target.Height))
            };
            state.Vk.CmdBeginRenderPass(state.CommandBuffer, &begin, SubpassContents.Inline);
            state.Vk.CmdBindPipeline(state.CommandBuffer, PipelineBindPoint.Graphics, state.AnalyticFogPipeline);
            var resolved = ResolveAnalyticFogParameters(fogPlan);
            var parameters = new AnalyticFogPushConstants(resolved.Color, resolved.Parameters);
            state.Vk.CmdPushConstants(
                state.CommandBuffer,
                state.AnalyticFogPipelineLayout,
                ShaderStageFlags.FragmentBit,
                0,
                (uint)Marshal.SizeOf<AnalyticFogPushConstants>(),
                &parameters);
            state.Vk.CmdDraw(state.CommandBuffer, 3, 1, 0, 0);
            state.Vk.CmdEndRenderPass(state.CommandBuffer);
        }

        private static void RecordTransparentCommands(
            VulkanState state,
            RekallAgeVulkanSceneCommandPlan commandPlan)
        {
            var draws = commandPlan.RenderPasses[0].Draws;
            if (!draws.Any(draw => draw.Transparent))
            {
                return;
            }

            var target = commandPlan.PreparedFrame.Target;
            var begin = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = state.TransparentRenderPass,
                Framebuffer = state.TransparentFramebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(target.Width, target.Height))
            };
            state.Vk.CmdBeginRenderPass(state.CommandBuffer, &begin, SubpassContents.Inline);
            var vertexBuffer = state.VertexBuffer;
            var vertexOffset = 0UL;
            state.Vk.CmdBindVertexBuffers(state.CommandBuffer, 0, 1, &vertexBuffer, &vertexOffset);
            state.Vk.CmdBindIndexBuffer(state.CommandBuffer, state.IndexBuffer, 0, IndexType.Uint32);
            DrawPassRanges(state, 0, draws, transparent: true);
            state.Vk.CmdEndRenderPass(state.CommandBuffer);
        }

        private static (Vector4 Color, Vector4 Parameters) ResolveAnalyticFogParameters(
            RekallAgeVulkanFogPlan fogPlan)
        {
            if (fogPlan.UsesFroxelGrid || !fogPlan.Enabled)
            {
                return (Vector4.Zero, Vector4.Zero);
            }

            var volumes = fogPlan.Volumes
                .Where(volume => volume.Shape.Equals("global", StringComparison.Ordinal))
                .DefaultIfEmpty(fogPlan.Volumes.First())
                .ToArray();
            var density = Math.Clamp(volumes.Sum(volume => volume.Density), 0, 64);
            var totalWeight = Math.Max(0.000001f, volumes.Sum(volume => Math.Max(volume.Density, 0.000001f)));
            var color = volumes.Aggregate(Vector3.Zero, (sum, volume) =>
                sum + (volume.Albedo + volume.Emission) * Math.Max(volume.Density, 0.000001f)) / totalWeight;
            var heightFalloff = volumes.Sum(volume => volume.HeightFalloff * Math.Max(volume.Density, 0.000001f)) / totalWeight;
            return (
                new Vector4(color, 1),
                new Vector4(density, heightFalloff, 12, 1));
        }

        private static void RecordFroxelFogCommands(
            VulkanState state,
            RekallAgeVulkanSceneCommandPlan commandPlan,
            RekallAgeVulkanFogPlan fogPlan)
        {
            TransitionImage(
                state,
                state.FogImage,
                ImageLayout.Undefined,
                ImageLayout.General,
                0,
                AccessFlags.ShaderWriteBit,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.ComputeShaderBit);
            TransitionImage(
                state,
                state.ColorImage,
                ImageLayout.ShaderReadOnlyOptimal,
                ImageLayout.General,
                AccessFlags.ColorAttachmentWriteBit,
                AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ColorAttachmentOutputBit,
                PipelineStageFlags.ComputeShaderBit);
            state.Vk.CmdBindPipeline(state.CommandBuffer, PipelineBindPoint.Compute, state.FogPipeline);
            var fogSet = state.FogDescriptorSet;
            state.Vk.CmdBindDescriptorSets(
                state.CommandBuffer,
                PipelineBindPoint.Compute,
                state.FogPipelineLayout,
                0,
                1,
                &fogSet,
                0,
                null);
            var frame = commandPlan.PreparedFrame.Frame;
            var camera = frame.ActiveCamera;
            var lightColor = commandPlan.PreparedFrame.Batch.Frame.LightColor;
            var lightScale = fogPlan.DirectLightAvailable ? Math.Max(0.1f, lightColor.W) : 0f;
            if (fogPlan.ShadowAvailable)
            {
                lightScale *= 0.72f;
            }

            var inject = new FogPushConstants(
                new Vector4(
                    checked((float)(camera?.X ?? 0)),
                    checked((float)(camera?.Y ?? 0)),
                    checked((float)(camera?.Z ?? 0)),
                    checked((float)Math.Max(0.001, camera?.NearClip ?? 0.05))),
                new Vector4(lightColor.X, lightColor.Y, lightColor.Z, lightScale),
                new Vector4(
                    fogPlan.Volumes.Count,
                    0,
                    checked((float)Math.Min(camera?.FarClip ?? 100, 500)),
                    fogPlan.TemporalReprojection ? 0.9f : 0));
            state.Vk.CmdPushConstants(
                state.CommandBuffer,
                state.FogPipelineLayout,
                ShaderStageFlags.ComputeBit,
                0,
                (uint)Marshal.SizeOf<FogPushConstants>(),
                &inject);
            state.Vk.CmdDispatch(
                state.CommandBuffer,
                checked((uint)fogPlan.Dispatch.GroupCountX),
                checked((uint)fogPlan.Dispatch.GroupCountY),
                checked((uint)fogPlan.Dispatch.GroupCountZ));
            TransitionImage(
                state,
                state.FogImage,
                ImageLayout.General,
                ImageLayout.General,
                AccessFlags.ShaderWriteBit,
                AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.ComputeShaderBit);
            var composite = inject with { Execution = inject.Execution with { Y = 1 } };
            state.Vk.CmdPushConstants(
                state.CommandBuffer,
                state.FogPipelineLayout,
                ShaderStageFlags.ComputeBit,
                0,
                (uint)Marshal.SizeOf<FogPushConstants>(),
                &composite);
            state.Vk.CmdDispatch(
                state.CommandBuffer,
                (commandPlan.PreparedFrame.Target.Width + 3) / 4,
                (commandPlan.PreparedFrame.Target.Height + 3) / 4,
                1);
            TransitionImage(
                state,
                state.ColorImage,
                ImageLayout.General,
                ImageLayout.ShaderReadOnlyOptimal,
                AccessFlags.ShaderWriteBit,
                AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit);
        }

        private static void RecordShadowDebugCopies(
            VulkanState state,
            RekallAgeVulkanShadowPlan shadowPlan)
        {
            var layerCount = checked((uint)shadowPlan.Cascades.Count);
            TransitionImage(
                state,
                state.ShadowImage,
                ImageLayout.ShaderReadOnlyOptimal,
                ImageLayout.TransferSrcOptimal,
                AccessFlags.ShaderReadBit,
                AccessFlags.TransferReadBit,
                PipelineStageFlags.FragmentShaderBit,
                PipelineStageFlags.TransferBit,
                aspectMask: ImageAspectFlags.DepthBit,
                layerCount: layerCount);

            var resolution = checked((uint)shadowPlan.Resolution);
            var layerBytes = checked((ulong)resolution * resolution * sizeof(float));
            var copies = shadowPlan.Cascades
                .Select(cascade => new BufferImageCopy
                {
                    BufferOffset = checked(layerBytes * (ulong)cascade.Index),
                    BufferRowLength = 0,
                    BufferImageHeight = 0,
                    ImageSubresource = new ImageSubresourceLayers(
                        ImageAspectFlags.DepthBit,
                        0,
                        checked((uint)cascade.Index),
                        1),
                    ImageOffset = new Offset3D(0, 0, 0),
                    ImageExtent = new Extent3D(resolution, resolution, 1)
                })
                .ToArray();
            fixed (BufferImageCopy* copy = copies)
            {
                state.Vk.CmdCopyImageToBuffer(
                    state.CommandBuffer,
                    state.ShadowImage,
                    ImageLayout.TransferSrcOptimal,
                    state.ShadowReadbackBuffer,
                    checked((uint)copies.Length),
                    copy);
            }
        }

        private static RekallAgeHighFidelityFrameReport CreateHighFidelityReport(
            RekallAgeVulkanHighFidelityFramePlan plan,
            bool executed,
            IReadOnlyList<RekallAgeHighFidelityFramePassReport> passes,
            IReadOnlyList<string> diagnostics,
            RekallAgeVulkanSceneCommandPlan? commandPlan = null,
            IReadOnlyList<RekallAgeHighFidelityShadowDebugCapture>? shadowDebugCaptures = null,
            IReadOnlyList<RekallAgeHighFidelityFogDebugCapture>? fogDebugCaptures = null)
        {
            var allocated = new HashSet<string>(StringComparer.Ordinal)
            {
                "scene-hdr",
                "bloom-pyramid",
                "ldr-color"
            };
            if (plan.ShadowPlan.Enabled)
            {
                allocated.Add("shadow-directional");
            }
            if (plan.FogPlan.UsesFroxelGrid)
            {
                allocated.Add("fog-froxel");
            }
            return new RekallAgeHighFidelityFrameReport(
                executed,
                "R16G16B16A16_SFloat",
                "R8G8B8A8_UNorm",
                plan.Graph.Resources
                    .Where(resource => allocated.Contains(resource.Name))
                    .Select(resource => new RekallAgeHighFidelityFrameResourceReport(
                        resource.Name,
                        resource.Format,
                        resource.Width,
                        resource.Height,
                        executed))
                    .ToArray(),
                passes,
                plan.Graph.Diagnostics
                    .Select(diagnostic => $"{diagnostic.Code} [{diagnostic.Target}]: {diagnostic.Message}")
                    .Concat(diagnostics)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray())
            {
                ShadowCascades = plan.ShadowPlan.Cascades
                    .Select(cascade => new RekallAgeHighFidelityShadowCascadeReport(
                        cascade.Index,
                        cascade.SplitNear,
                        cascade.SplitFar,
                        plan.ShadowPlan.Resolution,
                        cascade.CasterIds.Count,
                        executed && commandPlan is not null ? CountShadowDraws(commandPlan, cascade) : 0,
                        cascade.CulledCasterCount,
                        plan.ShadowPlan.FilterTapCount,
                        cascade.AtlasBytes,
                        plan.ShadowPlan.DepthBias,
                        plan.ShadowPlan.NormalBias))
                    .ToArray(),
                ShadowDebugCaptures = shadowDebugCaptures ?? [],
                Fog = new RekallAgeHighFidelityFogReport(
                    plan.FogPlan.Mode,
                    plan.FogPlan.Enabled,
                    plan.FogPlan.Grid,
                    plan.FogPlan.Dispatch,
                    executed && plan.FogPlan.UsesFroxelGrid ? 1 : 0,
                    plan.FogPlan.Volumes.Count,
                    plan.FogPlan.DroppedEntityIds,
                    executed && plan.FogPlan.Enabled && plan.FogPlan.DirectLightAvailable,
                    executed && plan.FogPlan.Enabled && plan.FogPlan.ShadowAvailable,
                    plan.FogPlan.HistoryReset,
                    plan.FogPlan.TemporalReprojection),
                FogDebugCaptures = fogDebugCaptures ?? []
            };
        }

        private static void RecordShadowCommands(
            VulkanState state,
            RekallAgeVulkanSceneCommandPlan commandPlan,
            RekallAgeVulkanShadowPlan shadowPlan)
        {
            var draws = commandPlan.RenderPasses[0].Draws;
            var vertexBuffer = state.VertexBuffer;
            var vertexOffset = 0UL;
            var clear = new ClearValue { DepthStencil = new ClearDepthStencilValue(1f, 0) };
            for (var cascadeIndex = 0; cascadeIndex < shadowPlan.Cascades.Count; cascadeIndex++)
            {
                var cascade = shadowPlan.Cascades[cascadeIndex];
                var renderPassBegin = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = state.ShadowRenderPass,
                    Framebuffer = state.ShadowFramebuffers[cascadeIndex],
                    RenderArea = new Rect2D(
                        new Offset2D(cascade.AtlasViewport.X, cascade.AtlasViewport.Y),
                        new Extent2D(
                            checked((uint)cascade.AtlasViewport.Width),
                            checked((uint)cascade.AtlasViewport.Height))),
                    ClearValueCount = 1,
                    PClearValues = &clear
                };
                state.Vk.CmdBeginRenderPass(state.CommandBuffer, &renderPassBegin, SubpassContents.Inline);
                state.Vk.CmdBindPipeline(state.CommandBuffer, PipelineBindPoint.Graphics, state.ShadowPipeline);
                state.Vk.CmdBindVertexBuffers(state.CommandBuffer, 0, 1, &vertexBuffer, &vertexOffset);
                state.Vk.CmdBindIndexBuffer(state.CommandBuffer, state.IndexBuffer, 0, IndexType.Uint32);
                for (var drawIndex = 0; drawIndex < draws.Count; drawIndex++)
                {
                    var draw = draws[drawIndex];
                    if (!IsShadowDrawEligible(draw, cascade))
                    {
                        continue;
                    }

                    var descriptorSets = new[]
                    {
                        state.ShadowDescriptorSets[cascadeIndex],
                        state.DrawDescriptorSet,
                        ResolveMaterialDescriptorSet(state, draw.MaterialKey)
                    };
                    var dynamicOffset = checked(state.DrawUniformStrideBytes * (uint)drawIndex);
                    fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
                    {
                        state.Vk.CmdBindDescriptorSets(
                            state.CommandBuffer,
                            PipelineBindPoint.Graphics,
                            state.ShadowPipelineLayout,
                            0,
                            3,
                            descriptorSetsPtr,
                            1,
                            &dynamicOffset);
                    }
                    state.Vk.CmdDrawIndexed(
                        state.CommandBuffer,
                        draw.IndexCount,
                        1,
                        draw.FirstIndex,
                        draw.VertexOffset,
                        0);
                }
                state.Vk.CmdEndRenderPass(state.CommandBuffer);
            }
        }

        private static IReadOnlyList<RekallAgeHighFidelityFramePassReport> CreateExecutedHighFidelityPassReports(
            RekallAgeVulkanSceneCommandPlan commandPlan,
            RekallAgeVulkanHighFidelityFramePlan highFidelityPlan)
        {
            var reports = new List<RekallAgeHighFidelityFramePassReport>();
            if (highFidelityPlan.ShadowPlan.Enabled)
            {
                reports.Add(new RekallAgeHighFidelityFramePassReport(
                    "shadow-directional",
                    "graphics",
                    [],
                    ["shadow-directional"],
                    true,
                    0,
                    highFidelityPlan.ShadowPlan.Cascades.Sum(cascade => CountShadowDraws(commandPlan, cascade))));
            }
            reports.Add(new RekallAgeHighFidelityFramePassReport(
                "opaque-hdr",
                "graphics",
                highFidelityPlan.ShadowPlan.Enabled ? ["shadow-directional"] : [],
                ["scene-hdr"],
                true,
                0,
                commandPlan.RenderPasses[0].Draws.Count(draw => !draw.Transparent)));
            foreach (var pass in commandPlan.PostPasses)
            {
                if (pass.Name.Equals("ui", StringComparison.Ordinal))
                {
                    continue;
                }

                reports.Add(pass.Name switch
                {
                    "fog-integrate" => new(
                        pass.Name,
                        pass.Kind,
                        pass.Reads,
                        pass.Writes,
                        true,
                        highFidelityPlan.FogPlan.UsesFroxelGrid ? 1 : 0,
                        !highFidelityPlan.FogPlan.UsesFroxelGrid && highFidelityPlan.FogPlan.Enabled ? 1 : 0),
                    "transparent-particles" => new(
                        pass.Name,
                        pass.Kind,
                        pass.Reads,
                        pass.Writes,
                        true,
                        0,
                        commandPlan.RenderPasses[0].Draws.Count(draw => draw.Transparent)),
                    "bloom" => new(pass.Name, "compute", pass.Reads, pass.Writes, true, 1, 0),
                    "tone-map" => new(pass.Name, "graphics", pass.Reads, pass.Writes, true, 0, 1),
                    "present" => new(pass.Name, "copy-readback", pass.Reads, pass.Writes, true, 0, 0),
                    _ => new(pass.Name, pass.Kind, pass.Reads, pass.Writes, false, 0, 0)
                });
            }

            return reports;
        }

        private static int CountShadowDraws(
            RekallAgeVulkanSceneCommandPlan commandPlan,
            RekallAgeVulkanShadowCascade cascade) =>
            commandPlan.RenderPasses[0].Draws.Count(draw => IsShadowDrawEligible(draw, cascade));

        private static bool IsShadowDrawEligible(
            RekallAgeVulkanSceneCommandDraw draw,
            RekallAgeVulkanShadowCascade cascade) =>
            draw.PushConstants.CastShadows >= 0.5f
            && !draw.Transparent
            && cascade.CasterIds.Contains(draw.EntityId, StringComparer.Ordinal);

        private static bool HasPostPass(RekallAgeVulkanSceneCommandPlan commandPlan, string name) =>
            commandPlan.PostPasses.Any(pass => pass.Enabled && pass.Name.Equals(name, StringComparison.Ordinal));

        private static void DrawPassRanges(
            VulkanState state,
            int passIndex,
            IReadOnlyList<RekallAgeVulkanSceneCommandDraw> ranges,
            bool transparent)
        {
            for (var drawIndex = 0; drawIndex < ranges.Count; drawIndex++)
            {
                var range = ranges[drawIndex];
                if (range.Transparent != transparent)
                {
                    continue;
                }

                var pipeline = range.ShaderPipeline is not null
                    ? state.ProjectPipelines.TryGetValue(range.ShaderPipeline, out var projectPipeline)
                        ? projectPipeline
                        : throw new InvalidOperationException(
                            $"Resolved Vulkan pipeline was not created for '{range.ShaderPipeline.VertexShader}' + '{range.ShaderPipeline.FragmentShader}'.")
                    : new VulkanProjectPipelineResource(
                        state.PipelineLayout,
                        state.Pipeline,
                        state.TransparentPipeline,
                        state.VertexShader,
                        state.FragmentShader);
                state.Vk.CmdBindPipeline(
                    state.CommandBuffer,
                    PipelineBindPoint.Graphics,
                    transparent ? pipeline.TransparentPipeline : pipeline.OpaquePipeline);

                var descriptorSets = new DescriptorSet[]
                {
                    state.FrameDescriptorSets[Math.Min(passIndex, state.FrameDescriptorSets.Length - 1)],
                    state.DrawDescriptorSet,
                    ResolveMaterialDescriptorSet(state, range.MaterialKey),
                    state.ShadowSampleDescriptorSet
                };
                var descriptorSetCount = state.ShadowSampleDescriptorSet.Handle != 0 ? 4u : 3u;
                var dynamicOffset = checked(state.DrawUniformStrideBytes * (uint)drawIndex);
                fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
                {
                    state.Vk.CmdBindDescriptorSets(
                        state.CommandBuffer,
                        PipelineBindPoint.Graphics,
                        pipeline.Layout,
                        0,
                        descriptorSetCount,
                        descriptorSetsPtr,
                        1,
                        &dynamicOffset);
                }
                state.Vk.CmdDrawIndexed(state.CommandBuffer, range.IndexCount, 1, range.FirstIndex, range.VertexOffset, 0);
            }
        }

        private static DescriptorSet ResolveMaterialDescriptorSet(
            VulkanState state,
            RekallAgeVulkanSceneMaterialKey key)
        {
            if (state.MaterialDescriptorSets.TryGetValue(key, out var descriptorSet)
                && descriptorSet.Handle != 0)
            {
                return descriptorSet;
            }

            return state.MaterialDescriptorSets[RekallAgeVulkanSceneMaterialKey.Default];
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
                    PipelineStageFlags.TransferBit,
                    checked((uint)texture.MipUploads.Count));
                var copies = texture.MipUploads
                    .Select(upload => new BufferImageCopy
                    {
                        BufferOffset = upload.BufferOffset,
                        BufferRowLength = 0,
                        BufferImageHeight = 0,
                        ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, upload.MipLevel, 0, 1),
                        ImageOffset = new Offset3D(0, 0, 0),
                        ImageExtent = new Extent3D(upload.Width, upload.Height, 1)
                    })
                    .ToArray();
                fixed (BufferImageCopy* copy = copies)
                {
                    state.Vk.CmdCopyBufferToImage(
                        state.CommandBuffer,
                        texture.StagingBuffer,
                        texture.Image,
                        ImageLayout.TransferDstOptimal,
                        checked((uint)copies.Length),
                        copy);
                }

                TransitionImage(
                    state,
                    texture.Image,
                    ImageLayout.TransferDstOptimal,
                    ImageLayout.ShaderReadOnlyOptimal,
                    AccessFlags.TransferWriteBit,
                    AccessFlags.ShaderReadBit,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.FragmentShaderBit,
                    checked((uint)texture.MipUploads.Count));
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
            PipelineStageFlags dstStage,
            uint mipLevels = 1,
            ImageAspectFlags aspectMask = ImageAspectFlags.ColorBit,
            uint layerCount = 1)
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
                SubresourceRange = new ImageSubresourceRange(aspectMask, 0, mipLevels, 0, layerCount)
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
            if (state.Fence.Handle == 0)
            {
                var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
                ThrowIfFailed(state.Vk.CreateFence(state.Device, &fenceInfo, null, out state.Fence), "vkCreateFence");
            }
            else
            {
                var existingFence = state.Fence;
                ThrowIfFailed(state.Vk.ResetFences(state.Device, 1, &existingFence), "vkResetFences");
            }

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

        private static void UpdateFrameUniformBuffers(
            VulkanState state,
            IReadOnlyList<RekallAgeVulkanSceneGpuFrameUniform> frameUniforms)
        {
            var count = Math.Min(state.UniformBuffers.Length, frameUniforms.Count);
            for (var index = 0; index < count; index++)
            {
                var frameUniform = frameUniforms[index];
                var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref frameUniform, 1));
                void* mapped;
                ThrowIfFailed(state.Vk.MapMemory(state.Device, state.UniformMemories[index], 0, (ulong)bytes.Length, 0, &mapped), "vkMapMemory");
                try
                {
                    fixed (byte* sourcePointer = bytes)
                    {
                        System.Buffer.MemoryCopy(sourcePointer, mapped, bytes.Length, bytes.Length);
                    }
                }
                finally
                {
                    state.Vk.UnmapMemory(state.Device, state.UniformMemories[index]);
                }
            }
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

        private static string ToFormatName(Format format)
        {
            return format switch
            {
                Format.R8G8B8A8Unorm => "R8G8B8A8_UNorm",
                Format.R8G8B8A8Srgb => "R8G8B8A8_SRGB",
                Format.B8G8R8A8Unorm => "B8G8R8A8_UNorm",
                Format.B8G8R8A8Srgb => "B8G8R8A8_SRGB",
                Format.D32Sfloat => "D32_SFLOAT",
                _ => format.ToString()
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

        private static IReadOnlyList<RekallAgeHighFidelityShadowDebugCapture> WriteShadowDebugCaptures(
            VulkanState state,
            RekallAgeVulkanShadowPlan shadowPlan,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            var bytes = ReadBackMemory(state, state.ShadowReadbackMemory, state.ShadowReadbackByteCount);
            var depth = MemoryMarshal.Cast<byte, float>(bytes);
            var resolution = shadowPlan.Resolution;
            var pixelsPerLayer = checked(resolution * resolution);
            var captures = new List<RekallAgeHighFidelityShadowDebugCapture>(shadowPlan.Cascades.Count);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            foreach (var cascade in shadowPlan.Cascades)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var layer = depth.Slice(checked(cascade.Index * pixelsPerLayer), pixelsPerLayer);
                var occupiedCount = 0;
                var minimum = float.MaxValue;
                var maximum = float.MinValue;
                foreach (var value in layer)
                {
                    if (!float.IsFinite(value) || value < 0 || value >= 0.999999f)
                    {
                        continue;
                    }

                    occupiedCount++;
                    minimum = MathF.Min(minimum, value);
                    maximum = MathF.Max(maximum, value);
                }
                var range = maximum - minimum;
                var rgba = new byte[checked(pixelsPerLayer * 4)];
                for (var pixel = 0; pixel < pixelsPerLayer; pixel++)
                {
                    var value = layer[pixel];
                    byte intensity = 0;
                    if (float.IsFinite(value) && value >= 0 && value < 0.999999f)
                    {
                        intensity = range < 0.000001f
                            ? byte.MaxValue
                            : checked((byte)Math.Clamp(
                                Math.Round(32 + (1 - (value - minimum) / range) * 223),
                                0,
                                255));
                    }

                    var offset = checked(pixel * 4);
                    rgba[offset] = intensity;
                    rgba[offset + 1] = intensity;
                    rgba[offset + 2] = intensity;
                    rgba[offset + 3] = byte.MaxValue;
                }

                var path = Path.Combine(
                    outputDirectory,
                    $"vulkan-shadow-cascade-{cascade.Index}-{timestamp}.png");
                RekallAgePngWriter.WriteRgbaAsync(path, resolution, resolution, rgba, cancellationToken)
                    .AsTask().GetAwaiter().GetResult();
                var analysis = Analyze(rgba);
                captures.Add(new RekallAgeHighFidelityShadowDebugCapture(
                    cascade.Index,
                    cascade.SplitNear,
                    cascade.SplitFar,
                    path,
                    occupiedCount > 0,
                    analysis.Checksum));
            }

            return captures;
        }

        private static IReadOnlyList<RekallAgeHighFidelityFogDebugCapture> WriteFogDebugCaptures(
            RekallAgeVulkanFogPlan fogPlan,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            var width = fogPlan.Grid.Width;
            var height = fogPlan.Grid.Height;
            var slice = Math.Clamp(fogPlan.Grid.Depth / 2, 0, Math.Max(0, fogPlan.Grid.Depth - 1));
            var densityRgba = new byte[checked(width * height * 4)];
            var lightingRgba = new byte[densityRgba.Length];
            var transmittanceRgba = new byte[densityRgba.Length];
            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var uvw = new Vector3(
                        (x + 0.5f) / width,
                        (y + 0.5f) / height,
                        (slice + 0.5f) / fogPlan.Grid.Depth);
                    var viewDepth = 100f * uvw.Z * uvw.Z;
                    var position = fogPlan.NextHistory.CameraPosition
                        + new Vector3((uvw.X - 0.5f) * viewDepth * 2, (uvw.Y - 0.5f) * viewDepth * 1.2f, viewDepth);
                    var density = 0f;
                    var lighting = Vector3.Zero;
                    foreach (var volume in fogPlan.Volumes)
                    {
                        var influence = FogVolumeInfluence(volume, position);
                        var heightAttenuation = MathF.Exp(-volume.HeightFalloff * Math.Max(position.Y, 0));
                        var localDensity = volume.Density * influence * heightAttenuation;
                        density += localDensity;
                        lighting += (volume.Scattering * (fogPlan.DirectLightAvailable ? 1f : 0f) + volume.Emission) * influence;
                    }

                    var transmittance = MathF.Exp(-density * 2f);
                    var densityValue = ToDebugByte(1f - MathF.Exp(-density));
                    var lightingValue = ToDebugByte(Math.Max(lighting.X, Math.Max(lighting.Y, lighting.Z)) * (1f - transmittance));
                    var transmittanceValue = ToDebugByte(1f - transmittance);
                    var offset = checked((y * width + x) * 4);
                    WriteDebugPixel(densityRgba, offset, densityValue, densityValue, densityValue);
                    WriteDebugPixel(lightingRgba, offset, lightingValue, checked((byte)(lightingValue * 3 / 4)), checked((byte)(lightingValue / 2)));
                    WriteDebugPixel(transmittanceRgba, offset, transmittanceValue, transmittanceValue, transmittanceValue);
                }
            }

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            var captures = new List<RekallAgeHighFidelityFogDebugCapture>(3);
            foreach (var (kind, rgba) in new[]
            {
                ("density", densityRgba),
                ("lighting", lightingRgba),
                ("integrated-transmittance", transmittanceRgba)
            })
            {
                var path = Path.Combine(outputDirectory, $"vulkan-fog-{kind}-slice-{slice}-{timestamp}.png");
                RekallAgePngWriter.WriteRgbaAsync(path, width, height, rgba, cancellationToken)
                    .AsTask().GetAwaiter().GetResult();
                var analysis = Analyze(rgba);
                captures.Add(new RekallAgeHighFidelityFogDebugCapture(
                    kind,
                    slice,
                    path,
                    rgba.Where((_, index) => index % 4 != 3).Any(value => value > 0),
                    analysis.Checksum));
            }

            return captures;
        }

        private static float FogVolumeInfluence(RekallAgeVulkanFogVolume volume, Vector3 position)
        {
            if (volume.Shape.Equals("global", StringComparison.Ordinal))
            {
                return 1;
            }

            var local = position - volume.Position;
            float signedDistance;
            if (volume.Shape.Equals("sphere", StringComparison.Ordinal))
            {
                signedDistance = (local / volume.HalfExtents).Length() - 1;
                signedDistance *= volume.HalfExtents.MaxComponent();
            }
            else
            {
                var q = Vector3.Abs(local) - volume.HalfExtents;
                signedDistance = Vector3.Max(q, Vector3.Zero).Length() + Math.Min(Math.Max(q.X, Math.Max(q.Y, q.Z)), 0);
            }

            if (signedDistance <= -volume.BlendDistance || signedDistance <= 0 && volume.BlendDistance <= 0.0001f)
            {
                return 1;
            }

            if (signedDistance >= 0)
            {
                return 0;
            }

            return Math.Clamp(-signedDistance / Math.Max(volume.BlendDistance, 0.0001f), 0, 1);
        }

        private static byte ToDebugByte(float value) =>
            checked((byte)Math.Clamp(Math.Round(Math.Clamp(value, 0, 1) * 255), 0, 255));

        private static void WriteDebugPixel(byte[] rgba, int offset, byte red, byte green, byte blue)
        {
            rgba[offset] = red;
            rgba[offset + 1] = green;
            rgba[offset + 2] = blue;
            rgba[offset + 3] = byte.MaxValue;
        }

        private static byte[] PackFogVolumes(IReadOnlyList<RekallAgeVulkanFogVolume> volumes)
        {
            var packed = volumes
                .Select(volume => new FogVolumeGpu(
                    new Vector4(volume.Position, volume.Shape switch
                    {
                        "box" => 1,
                        "sphere" => 2,
                        _ => 0
                    }),
                    new Vector4(volume.HalfExtents, volume.Density),
                    new Vector4(volume.Albedo, volume.Anisotropy),
                    new Vector4(volume.Emission, volume.HeightFalloff),
                    new Vector4(volume.BlendDistance, volume.Priority, 0, 0)))
                .ToArray();
            if (packed.Length == 0)
            {
                packed = [default];
            }

            return MemoryMarshal.AsBytes(packed.AsSpan()).ToArray();
        }

        private static byte[] ReadBackMemory(VulkanState state, DeviceMemory memory, ulong byteCount)
        {
            void* mapped;
            ThrowIfFailed(state.Vk.MapMemory(state.Device, memory, 0, byteCount, 0, &mapped), "vkMapMemory debug readback");
            try
            {
                var bytes = new byte[checked((int)byteCount)];
                Marshal.Copy((nint)mapped, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                state.Vk.UnmapMemory(state.Device, memory);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly record struct BloomPushConstants(
            float Threshold,
            float Intensity,
            float SourceWidth,
            float SourceHeight);

        [StructLayout(LayoutKind.Sequential)]
        private readonly record struct ToneMapPushConstants(
            float Exposure,
            float WhitePoint,
            float Saturation,
            float Contrast,
            float GradeStrength,
            float BloomIntensity,
            float BloomRadius,
            float Padding);

        [StructLayout(LayoutKind.Sequential)]
        private readonly record struct AnalyticFogPushConstants(
            Vector4 Color,
            Vector4 Optical);

        [StructLayout(LayoutKind.Sequential)]
        private readonly record struct FogPushConstants(
            Vector4 CameraNearFar,
            Vector4 LightColorIntensity,
            Vector4 Execution);

        [StructLayout(LayoutKind.Sequential)]
        private readonly record struct FogVolumeGpu(
            Vector4 PositionShape,
            Vector4 ExtentsDensity,
            Vector4 AlbedoAnisotropy,
            Vector4 EmissionHeightFalloff,
            Vector4 BlendPriorityPadding);

        [StructLayout(LayoutKind.Sequential)]
        private readonly record struct ShadowCascadeGpuUniform(
            RekallAgeVulkanSceneGpuMatrix4x4 ViewProjection,
            float DepthBias,
            float NormalBias,
            float Resolution,
            float CascadeIndex);

        private readonly record struct DeviceCandidate(PhysicalDevice Device, string Name, PhysicalDeviceType DeviceType, uint ApiVersion, uint? QueueFamily);

        private readonly record struct VulkanTextureMipUpload(
            ulong BufferOffset,
            uint MipLevel,
            uint Width,
            uint Height);

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
            public RekallAgeVulkanSceneResourceOwnershipPlan Ownership = RekallAgeVulkanSceneResourceOwnershipPlan.ForTarget(
                RekallAgeVulkanSceneRenderTarget.OffscreenCapture(1, 1));
            public Image ColorImage;
            public DeviceMemory ColorMemory;
            public ImageView ColorView;
            public ImageView[] ColorViews = [];
            public Image DepthImage;
            public DeviceMemory DepthMemory;
            public ImageView DepthView;
            public ImageView[] DepthViews = [];
            public Image ShadowImage;
            public DeviceMemory ShadowMemory;
            public ImageView ShadowView;
            public ImageView[] ShadowLayerViews = [];
            public Image BloomImage;
            public DeviceMemory BloomMemory;
            public ImageView BloomView;
            public uint BloomWidth;
            public uint BloomHeight;
            public Image FogImage;
            public DeviceMemory FogMemory;
            public ImageView FogView;
            public uint FogWidth;
            public uint FogHeight;
            public uint FogDepth;
            public Image OutputImage;
            public DeviceMemory OutputMemory;
            public ImageView OutputView;
            public RenderPass RenderPass;
            public RenderPass OutputRenderPass;
            public RenderPass FogCompositeRenderPass;
            public RenderPass TransparentRenderPass;
            public RenderPass ShadowRenderPass;
            public Framebuffer Framebuffer;
            public Framebuffer[] Framebuffers = [];
            public Framebuffer OutputFramebuffer;
            public Framebuffer FogCompositeFramebuffer;
            public Framebuffer TransparentFramebuffer;
            public Framebuffer[] ShadowFramebuffers = [];
            public Buffer VertexBuffer;
            public DeviceMemory VertexMemory;
            public Buffer IndexBuffer;
            public DeviceMemory IndexMemory;
            public Buffer UniformBuffer;
            public DeviceMemory UniformMemory;
            public Buffer[] UniformBuffers = [];
            public DeviceMemory[] UniformMemories = [];
            public Buffer[] ShadowUniformBuffers = [];
            public DeviceMemory[] ShadowUniformMemories = [];
            public Buffer DrawUniformBuffer;
            public DeviceMemory DrawUniformMemory;
            public uint DrawUniformStrideBytes;
            public Buffer ReadbackBuffer;
            public DeviceMemory ReadbackMemory;
            public Buffer ShadowReadbackBuffer;
            public DeviceMemory ShadowReadbackMemory;
            public ulong ShadowReadbackByteCount;
            public Buffer FogVolumeBuffer;
            public DeviceMemory FogVolumeMemory;
            public DescriptorSetLayout DescriptorSetLayout;
            public DescriptorSetLayout DrawDescriptorSetLayout;
            public DescriptorSetLayout MaterialDescriptorSetLayout;
            public DescriptorSetLayout ShadowSampleDescriptorSetLayout;
            public DescriptorPool DescriptorPool;
            public DescriptorPool PostDescriptorPool;
            public DescriptorSet DescriptorSet;
            public DescriptorSet[] FrameDescriptorSets = [];
            public DescriptorSet DrawDescriptorSet;
            public DescriptorSet[] ShadowDescriptorSets = [];
            public DescriptorSet ShadowSampleDescriptorSet;
            public DescriptorSetLayout BloomDescriptorSetLayout;
            public DescriptorSetLayout FogDescriptorSetLayout;
            public DescriptorSetLayout ToneMapDescriptorSetLayout;
            public DescriptorSet BloomDescriptorSet;
            public DescriptorSet FogDescriptorSet;
            public DescriptorSet ToneMapDescriptorSet;
            public readonly List<VulkanTextureResource> Textures = [];
            public readonly Dictionary<string, VulkanTextureResource> TextureById = new(StringComparer.Ordinal);
            public readonly Dictionary<RekallAgeVulkanSceneMaterialKey, DescriptorSet> MaterialDescriptorSets = [];
            public readonly Dictionary<RekallAgeRuntimeViewportShaderPipeline, VulkanProjectPipelineResource> ProjectPipelines = [];
            public readonly Dictionary<string, VulkanProjectPipelineResource> ProjectPipelineByContentHash = new(StringComparer.Ordinal);
            public readonly List<VulkanProjectPipelineResource> ProjectPipelineResources = [];
            public VulkanTextureResource? WhiteTexture;
            public VulkanTextureResource? FlatNormalTexture;
            public VulkanTextureResource? DefaultMetallicRoughnessTexture;
            public PipelineLayout PipelineLayout;
            public Pipeline Pipeline;
            public Pipeline TransparentPipeline;
            public ShaderModule VertexShader;
            public ShaderModule FragmentShader;
            public Sampler ShadowSampler;
            public PipelineLayout ShadowPipelineLayout;
            public Pipeline ShadowPipeline;
            public ShaderModule ShadowVertexShader;
            public ShaderModule ShadowFragmentShader;
            public Sampler PostSampler;
            public PipelineLayout BloomPipelineLayout;
            public PipelineLayout FogPipelineLayout;
            public PipelineLayout AnalyticFogPipelineLayout;
            public PipelineLayout ToneMapPipelineLayout;
            public Pipeline BloomPipeline;
            public Pipeline FogPipeline;
            public Pipeline AnalyticFogPipeline;
            public Pipeline ToneMapPipeline;
            public ShaderModule BloomShader;
            public ShaderModule FogShader;
            public ShaderModule AnalyticFogShader;
            public ShaderModule FullscreenVertexShader;
            public ShaderModule ToneMapShader;
            public CommandPool CommandPool;
            public CommandBuffer CommandBuffer;
            public Fence Fence;

            public void Dispose()
            {
                if (Device.Handle != 0)
                {
                    if (Ownership.OwnsVulkanDevice)
                    {
                        Vk.DeviceWaitIdle(Device);
                    }

                    if (Fence.Handle != 0)
                    {
                        Vk.DestroyFence(Device, Fence, null);
                    }

                    if (CommandPool.Handle != 0)
                    {
                        Vk.DestroyCommandPool(Device, CommandPool, null);
                    }

                    foreach (var projectPipeline in ProjectPipelineResources.AsEnumerable().Reverse())
                    {
                        projectPipeline.Dispose(Vk, Device);
                    }

                    if (ShadowPipeline.Handle != 0) Vk.DestroyPipeline(Device, ShadowPipeline, null);
                    if (ShadowPipelineLayout.Handle != 0) Vk.DestroyPipelineLayout(Device, ShadowPipelineLayout, null);
                    if (ShadowFragmentShader.Handle != 0) Vk.DestroyShaderModule(Device, ShadowFragmentShader, null);
                    if (ShadowVertexShader.Handle != 0) Vk.DestroyShaderModule(Device, ShadowVertexShader, null);

                    if (ToneMapPipeline.Handle != 0)
                    {
                        Vk.DestroyPipeline(Device, ToneMapPipeline, null);
                    }

                    if (BloomPipeline.Handle != 0)
                    {
                        Vk.DestroyPipeline(Device, BloomPipeline, null);
                    }

                    if (FogPipeline.Handle != 0) Vk.DestroyPipeline(Device, FogPipeline, null);
                    if (AnalyticFogPipeline.Handle != 0) Vk.DestroyPipeline(Device, AnalyticFogPipeline, null);

                    if (ToneMapPipelineLayout.Handle != 0)
                    {
                        Vk.DestroyPipelineLayout(Device, ToneMapPipelineLayout, null);
                    }

                    if (BloomPipelineLayout.Handle != 0)
                    {
                        Vk.DestroyPipelineLayout(Device, BloomPipelineLayout, null);
                    }

                    if (FogPipelineLayout.Handle != 0) Vk.DestroyPipelineLayout(Device, FogPipelineLayout, null);
                    if (AnalyticFogPipelineLayout.Handle != 0) Vk.DestroyPipelineLayout(Device, AnalyticFogPipelineLayout, null);

                    if (ToneMapShader.Handle != 0)
                    {
                        Vk.DestroyShaderModule(Device, ToneMapShader, null);
                    }

                    if (FullscreenVertexShader.Handle != 0)
                    {
                        Vk.DestroyShaderModule(Device, FullscreenVertexShader, null);
                    }

                    if (BloomShader.Handle != 0)
                    {
                        Vk.DestroyShaderModule(Device, BloomShader, null);
                    }

                    if (FogShader.Handle != 0) Vk.DestroyShaderModule(Device, FogShader, null);
                    if (AnalyticFogShader.Handle != 0) Vk.DestroyShaderModule(Device, AnalyticFogShader, null);

                    if (PostDescriptorPool.Handle != 0)
                    {
                        Vk.DestroyDescriptorPool(Device, PostDescriptorPool, null);
                    }

                    if (PostSampler.Handle != 0)
                    {
                        Vk.DestroySampler(Device, PostSampler, null);
                    }

                    if (ShadowSampler.Handle != 0)
                    {
                        Vk.DestroySampler(Device, ShadowSampler, null);
                    }

                    if (ToneMapDescriptorSetLayout.Handle != 0)
                    {
                        Vk.DestroyDescriptorSetLayout(Device, ToneMapDescriptorSetLayout, null);
                    }

                    if (BloomDescriptorSetLayout.Handle != 0)
                    {
                        Vk.DestroyDescriptorSetLayout(Device, BloomDescriptorSetLayout, null);
                    }

                    if (FogDescriptorSetLayout.Handle != 0) Vk.DestroyDescriptorSetLayout(Device, FogDescriptorSetLayout, null);

                    if (Pipeline.Handle != 0)
                    {
                        Vk.DestroyPipeline(Device, Pipeline, null);
                    }

                    if (TransparentPipeline.Handle != 0)
                    {
                        Vk.DestroyPipeline(Device, TransparentPipeline, null);
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

                    if (DrawDescriptorSetLayout.Handle != 0)
                    {
                        Vk.DestroyDescriptorSetLayout(Device, DrawDescriptorSetLayout, null);
                    }

                    if (MaterialDescriptorSetLayout.Handle != 0)
                    {
                        Vk.DestroyDescriptorSetLayout(Device, MaterialDescriptorSetLayout, null);
                    }

                    if (ShadowSampleDescriptorSetLayout.Handle != 0)
                    {
                        Vk.DestroyDescriptorSetLayout(Device, ShadowSampleDescriptorSetLayout, null);
                    }

                    foreach (var texture in Textures)
                    {
                        texture.Dispose(Vk, Device);
                    }

                    DestroyBuffer(VertexBuffer, VertexMemory);
                    DestroyBuffer(IndexBuffer, IndexMemory);
                    if (UniformBuffers.Length > 0)
                    {
                        for (var index = 0; index < UniformBuffers.Length; index++)
                        {
                            var memory = index < UniformMemories.Length ? UniformMemories[index] : default;
                            DestroyBuffer(UniformBuffers[index], memory);
                        }
                    }
                    else
                    {
                        DestroyBuffer(UniformBuffer, UniformMemory);
                    }

                    DestroyBuffer(DrawUniformBuffer, DrawUniformMemory);

                    for (var index = 0; index < ShadowUniformBuffers.Length; index++)
                    {
                        var memory = index < ShadowUniformMemories.Length ? ShadowUniformMemories[index] : default;
                        DestroyBuffer(ShadowUniformBuffers[index], memory);
                    }

                    DestroyBuffer(ReadbackBuffer, ReadbackMemory);
                    DestroyBuffer(ShadowReadbackBuffer, ShadowReadbackMemory);
                    DestroyBuffer(FogVolumeBuffer, FogVolumeMemory);

                    if (Framebuffers.Length > 0)
                    {
                        foreach (var framebuffer in Framebuffers)
                        {
                            if (framebuffer.Handle != 0)
                            {
                                Vk.DestroyFramebuffer(Device, framebuffer, null);
                            }
                        }
                    }
                    else if (Framebuffer.Handle != 0)
                    {
                        Vk.DestroyFramebuffer(Device, Framebuffer, null);
                    }

                    if (RenderPass.Handle != 0)
                    {
                        Vk.DestroyRenderPass(Device, RenderPass, null);
                    }

                    if (OutputFramebuffer.Handle != 0)
                    {
                        Vk.DestroyFramebuffer(Device, OutputFramebuffer, null);
                    }

                    if (OutputRenderPass.Handle != 0)
                    {
                        Vk.DestroyRenderPass(Device, OutputRenderPass, null);
                    }

                    if (FogCompositeFramebuffer.Handle != 0) Vk.DestroyFramebuffer(Device, FogCompositeFramebuffer, null);
                    if (FogCompositeRenderPass.Handle != 0) Vk.DestroyRenderPass(Device, FogCompositeRenderPass, null);
                    if (TransparentFramebuffer.Handle != 0) Vk.DestroyFramebuffer(Device, TransparentFramebuffer, null);
                    if (TransparentRenderPass.Handle != 0) Vk.DestroyRenderPass(Device, TransparentRenderPass, null);

                    foreach (var framebuffer in ShadowFramebuffers)
                    {
                        if (framebuffer.Handle != 0)
                        {
                            Vk.DestroyFramebuffer(Device, framebuffer, null);
                        }
                    }

                    if (ShadowRenderPass.Handle != 0)
                    {
                        Vk.DestroyRenderPass(Device, ShadowRenderPass, null);
                    }

                    DestroyImage(ColorImage, ColorView, ColorViews, ColorMemory, Ownership.OwnsImageViews, Ownership.OwnsColorImages);
                    DestroyImage(DepthImage, DepthView, DepthViews, DepthMemory, Ownership.OwnsImageViews, Ownership.OwnsDepthImages);
                    DestroyImage(BloomImage, BloomView, [], BloomMemory, ownsView: true, ownsImageAndMemory: true);
                    DestroyImage(FogImage, FogView, [], FogMemory, ownsView: true, ownsImageAndMemory: true);
                    DestroyImage(OutputImage, OutputView, [], OutputMemory, ownsView: true, ownsImageAndMemory: true);
                    DestroyImage(ShadowImage, ShadowView, ShadowLayerViews, ShadowMemory, ownsView: true, ownsImageAndMemory: true);
                    if (Ownership.OwnsVulkanDevice)
                    {
                        Vk.DestroyDevice(Device, null);
                    }
                }

                if (Instance.Handle != 0 && Ownership.OwnsVulkanInstance)
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

            private void DestroyImage(
                Image image,
                ImageView view,
                IReadOnlyList<ImageView> views,
                DeviceMemory memory,
                bool ownsView,
                bool ownsImageAndMemory)
            {
                if (views.Count > 0 && ownsView)
                {
                    foreach (var layerView in views)
                    {
                        if (layerView.Handle != 0)
                        {
                            Vk.DestroyImageView(Device, layerView, null);
                        }
                    }

                    if (view.Handle != 0 && !views.Any(layerView => layerView.Handle == view.Handle))
                    {
                        Vk.DestroyImageView(Device, view, null);
                    }
                }
                else if (view.Handle != 0 && ownsView)
                {
                    Vk.DestroyImageView(Device, view, null);
                }

                if (image.Handle != 0 && ownsImageAndMemory)
                {
                    Vk.DestroyImage(Device, image, null);
                }

                if (memory.Handle != 0 && ownsImageAndMemory)
                {
                    Vk.FreeMemory(Device, memory, null);
                }
            }
        }

        private static void UpdateDrawUniformBuffer(
            VulkanState state,
            IReadOnlyList<RekallAgeVulkanSceneGpuDrawPushConstants> drawUniforms)
        {
            var drawUniformBytes = checked((int)Marshal.SizeOf<RekallAgeVulkanSceneGpuDrawPushConstants>());
            var packedBytes = checked((ulong)state.DrawUniformStrideBytes * (ulong)Math.Max(1, drawUniforms.Count));
            void* mapped;
            ThrowIfFailed(
                state.Vk.MapMemory(state.Device, state.DrawUniformMemory, 0, packedBytes, 0, &mapped),
                "vkMapMemory draw uniforms");
            try
            {
                for (var index = 0; index < drawUniforms.Count; index++)
                {
                    var drawUniform = drawUniforms[index];
                    var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in drawUniform, 1));
                    fixed (byte* sourcePointer = bytes)
                    {
                        System.Buffer.MemoryCopy(
                            sourcePointer,
                            (byte*)mapped + checked((nint)(state.DrawUniformStrideBytes * (uint)index)),
                            drawUniformBytes,
                            drawUniformBytes);
                    }
                }
            }
            finally
            {
                state.Vk.UnmapMemory(state.Device, state.DrawUniformMemory);
            }
        }

        private sealed record VulkanProjectPipelineResource(
            PipelineLayout Layout,
            Pipeline OpaquePipeline,
            Pipeline TransparentPipeline,
            ShaderModule VertexShader,
            ShaderModule FragmentShader)
        {
            public unsafe void Dispose(Vk vk, Device device)
            {
                if (OpaquePipeline.Handle != 0) vk.DestroyPipeline(device, OpaquePipeline, null);
                if (TransparentPipeline.Handle != 0) vk.DestroyPipeline(device, TransparentPipeline, null);
                if (Layout.Handle != 0) vk.DestroyPipelineLayout(device, Layout, null);
                if (FragmentShader.Handle != 0) vk.DestroyShaderModule(device, FragmentShader, null);
                if (VertexShader.Handle != 0) vk.DestroyShaderModule(device, VertexShader, null);
            }
        }

        private sealed class VulkanTextureResource
        {
            public VulkanTextureResource(
                string id,
                uint width,
                uint height,
                IReadOnlyList<VulkanTextureMipUpload> mipUploads,
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
                MipUploads = mipUploads;
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
            public IReadOnlyList<VulkanTextureMipUpload> MipUploads { get; }
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
