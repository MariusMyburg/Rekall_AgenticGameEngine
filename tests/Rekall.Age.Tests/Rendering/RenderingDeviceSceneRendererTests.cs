using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class RenderingDeviceSceneRendererTests
{
    [Fact]
    public void RendersOneTriangleMeshThroughTheGenericDevice()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var colorTarget = CreateColorTarget(device, 64, 64);
        var renderer = new RekallAgeRenderingDeviceSceneRenderer(device);

        var frame = TriangleFrame();
        var meshes = new[] { TriangleMesh() };

        var result = renderer.RenderFrame(frame, meshes, colorTarget, RekallAgeTextureFormat.Rgba8Unorm);

        Assert.True(result.Rendered, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(1, result.DrawCount);
        Assert.Equal(3, result.VertexCount);
        Assert.Equal(3, result.IndexCount);
        Assert.Equal(1, device.SubmissionCount);

        var resources = device.InspectResources();
        Assert.Contains(resources, item => item.Descriptor is RekallAgeGraphicsPipelineDescriptor);
        Assert.Contains(resources, item => item.Descriptor is RekallAgeBufferDescriptor buffer
            && buffer.Usage.HasFlag(RekallAgeBufferUsage.Vertex));
        Assert.Contains(resources, item => item.Descriptor is RekallAgeBufferDescriptor buffer
            && buffer.Usage.HasFlag(RekallAgeBufferUsage.Index));
        Assert.Contains(resources, item => item.Descriptor is RekallAgeBufferDescriptor buffer
            && buffer.Usage.HasFlag(RekallAgeBufferUsage.Uniform));
    }

    [Fact]
    public void RendersTwoDrawsWithDistinctModelMatricesAndOneBindingSetEach()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var colorTarget = CreateColorTarget(device, 32, 32);
        var renderer = new RekallAgeRenderingDeviceSceneRenderer(device);

        var frame = TriangleFrame() with
        {
            Renderables =
            [
                TriangleFrame().Renderables[0],
                TriangleFrame().Renderables[0] with { EntityId = "tri-2", EntityName = "Triangle Two", X = 3 }
            ]
        };
        var meshes = new[] { TriangleMesh(), TriangleMesh() with { EntityId = "tri-2", EntityName = "Triangle Two" } };

        var result = renderer.RenderFrame(frame, meshes, colorTarget, RekallAgeTextureFormat.Rgba8Unorm);

        Assert.True(result.Rendered, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(2, result.DrawCount);

        var bindingSets = device.InspectResources().Where(item => item.Handle.Kind == RekallAgeGraphicsResourceKind.BindingSet).ToArray();
        Assert.Equal(2, bindingSets.Length);
    }

    [Fact]
    public void ReusesGeometryAndBindingResourcesAcrossIdenticalFrames()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var colorTarget = CreateColorTarget(device, 64, 64);
        var renderer = new RekallAgeRenderingDeviceSceneRenderer(device);

        var frame = TriangleFrame();
        var meshes = new[] { TriangleMesh() };

        renderer.RenderFrame(frame, meshes, colorTarget, RekallAgeTextureFormat.Rgba8Unorm);
        var afterFirstFrame = device.InspectResources().Count;

        var result = renderer.RenderFrame(frame, meshes, colorTarget, RekallAgeTextureFormat.Rgba8Unorm);
        var afterSecondFrame = device.InspectResources().Count;

        Assert.True(result.Rendered);
        Assert.Equal(afterFirstFrame, afterSecondFrame);
        Assert.Equal(2, device.SubmissionCount);
    }

    [Fact]
    public void ComposesADepthAttachmentSoOverlappingGeometryOccludesCorrectly()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var colorTarget = CreateColorTarget(device, 64, 64);
        var renderer = new RekallAgeRenderingDeviceSceneRenderer(device);

        var result = renderer.RenderFrame(TriangleFrame(), [TriangleMesh()], colorTarget, RekallAgeTextureFormat.Rgba8Unorm);

        Assert.True(result.Rendered, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        var resources = device.InspectResources();
        Assert.Contains(resources, item => item.Descriptor is RekallAgeTextureDescriptor texture
            && texture.Format == RekallAgeTextureFormat.Depth32Float);
        Assert.Contains(resources, item => item.Descriptor is RekallAgeRenderTargetDescriptor target
            && target.DepthStencilAttachment is not null);
        Assert.Contains(resources, item => item.Descriptor is RekallAgeGraphicsPipelineDescriptor pipeline
            && pipeline.DepthStencil is not null);
    }

    [Fact]
    public void RecomposesTheDepthTargetWhenTheColorTargetIsRecreatedAtANewSize()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var renderer = new RekallAgeRenderingDeviceSceneRenderer(device);

        var firstTarget = CreateColorTarget(device, 64, 64);
        var firstResult = renderer.RenderFrame(TriangleFrame(), [TriangleMesh()], firstTarget, RekallAgeTextureFormat.Rgba8Unorm);
        Assert.True(firstResult.Rendered, string.Join(Environment.NewLine, firstResult.Diagnostics.Select(item => item.Message)));
        var depthAfterFirst = device.InspectResources()
            .Single(item => item.Descriptor is RekallAgeTextureDescriptor texture && texture.Format == RekallAgeTextureFormat.Depth32Float);
        Assert.Equal(64, ((RekallAgeTextureDescriptor)depthAfterFirst.Descriptor).Width);

        // A resize replaces the color target with a differently-sized handle (the browser cannot resize a canvas
        // texture in place); the composed depth target must follow the new size, not silently keep serving 64x64.
        var secondTarget = CreateColorTarget(device, 128, 96);
        var secondResult = renderer.RenderFrame(TriangleFrame(), [TriangleMesh()], secondTarget, RekallAgeTextureFormat.Rgba8Unorm);
        Assert.True(secondResult.Rendered, string.Join(Environment.NewLine, secondResult.Diagnostics.Select(item => item.Message)));

        var depthTextures = device.InspectResources()
            .Where(item => item.Descriptor is RekallAgeTextureDescriptor texture && texture.Format == RekallAgeTextureFormat.Depth32Float)
            .ToArray();
        var liveDepth = Assert.Single(depthTextures);
        var liveDescriptor = (RekallAgeTextureDescriptor)liveDepth.Descriptor;
        Assert.Equal(128, liveDescriptor.Width);
        Assert.Equal(96, liveDescriptor.Height);
    }

    [Fact]
    public void ReportsADiagnosticInsteadOfRenderingAnEmptyFrame()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities.DesktopBaseline("in-memory"));
        var colorTarget = CreateColorTarget(device, 64, 64);
        var renderer = new RekallAgeRenderingDeviceSceneRenderer(device);

        var frame = TriangleFrame() with { Renderables = Array.Empty<RekallAgeRuntimeViewportRenderable>() };

        var result = renderer.RenderFrame(frame, Array.Empty<RekallAgeVulkanSceneMesh>(), colorTarget, RekallAgeTextureFormat.Rgba8Unorm);

        Assert.False(result.Rendered);
        Assert.Equal(0, result.DrawCount);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_RENDERING_DEVICE_SCENE_EMPTY");
        Assert.Equal(0, device.SubmissionCount);
    }

    private static RekallAgeGraphicsResourceHandle CreateColorTarget(IRekallAgeRenderingDevice device, int width, int height)
    {
        var texture = device.CreateTexture(new RekallAgeTextureDescriptor(
            RekallAgeTextureDimension.Texture2D, width, height, 1, 1, 1, 1,
            RekallAgeTextureFormat.Rgba8Unorm, RekallAgeTextureUsage.ColorAttachment, "scene-color"));
        Assert.True(texture.Created, string.Join(Environment.NewLine, texture.Diagnostics.Select(item => item.Message)));

        var target = device.CreateRenderTarget(new RekallAgeRenderTargetDescriptor(
            [new RekallAgeRenderTargetAttachment(texture.Handle)],
            null,
            width,
            height,
            "scene-target"));
        Assert.True(target.Created, string.Join(Environment.NewLine, target.Diagnostics.Select(item => item.Message)));
        return target.Handle;
    }

    private static RekallAgeRuntimeViewportFrame TriangleFrame()
    {
        var camera = new RekallAgeRuntimeViewportCamera("cam-1", "Main Camera", "perspective", true, X: 0, Y: 0, Z: 5);
        return new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            64,
            64,
            camera,
            [camera],
            [
                new RekallAgeRuntimeViewportRenderable(
                    "tri-1",
                    "Triangle One",
                    "mesh",
                    null,
                    0, 0, 0,
                    0,
                    MaterialColor: "#ff8040",
                    GeometryMesh: TriangleGeometry())
            ],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            Array.Empty<RekallAgeRuntimeViewportObservation>());
    }

    private static RekallAgeVulkanSceneMesh TriangleMesh()
    {
        return new RekallAgeVulkanSceneMesh(
            "tri-1",
            "Triangle One",
            "triangle",
            [
                new(-0.5f, -0.5f, 0f, 0, 0, 1, 1, 0.5f, 0.25f, 1, 0, 0),
                new(0.5f, -0.5f, 0f, 0, 0, 1, 1, 0.5f, 0.25f, 1, 1, 0),
                new(0f, 0.5f, 0f, 0, 0, 1, 1, 0.5f, 0.25f, 1, 0.5f, 1)
            ],
            [0u, 1u, 2u]);
    }

    private static RekallAgeRuntimeViewportGeometryMesh TriangleGeometry()
    {
        return new RekallAgeRuntimeViewportGeometryMesh(
            [
                new(-0.5, -0.5, 0),
                new(0.5, -0.5, 0),
                new(0, 0.5, 0)
            ],
            [0u, 1u, 2u]);
    }
}
