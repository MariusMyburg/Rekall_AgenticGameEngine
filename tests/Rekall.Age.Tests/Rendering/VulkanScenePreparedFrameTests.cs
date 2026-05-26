using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanScenePreparedFrameTests
{
    [Fact]
    public void PreparedFramePreservesBatchAndTargetMetadata()
    {
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            160,
            90,
            null,
            [],
            [
                new RekallAgeRuntimeViewportRenderable(
                    "cube",
                    "Cube",
                    "mesh",
                    "rekall.primitive.cube",
                    0,
                    0,
                    0,
                    0,
                    Variant: "rekall.geometry.cube")
            ],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);
        var target = RekallAgeVulkanSceneRenderTarget.OffscreenCapture(160, 90);

        var prepared = RekallAgeVulkanScenePreparedFrameBuilder.Build(frame, meshes, target);

        Assert.True(prepared.HasDrawableGeometry);
        Assert.Equal(160u * 90u * 4u, prepared.ReadbackByteCount);
        Assert.Equal(target, prepared.Target);
        Assert.Equal(meshes.Count, prepared.Meshes.Count);
        Assert.Equal(prepared.Batch.Vertices.Count, prepared.VertexCount);
        Assert.Equal(prepared.Batch.Indices.Count, prepared.IndexCount);
        Assert.Equal(prepared.Batch.Draws.Count, prepared.DrawCount);
        Assert.Equal(prepared.Batch.Draws.Count, prepared.DrawPlan.Draws.Count);
        Assert.Contains(RekallAgeVulkanSceneMaterialKey.Default, prepared.DrawPlan.MaterialKeys);
        Assert.True(prepared.GeometryUpload.HasGeometry);
        Assert.Equal(prepared.VertexCount, prepared.GeometryUpload.VertexCount);
        Assert.Equal(prepared.IndexCount, prepared.GeometryUpload.IndexCount);
        Assert.Equal(prepared.VertexCount * System.Runtime.InteropServices.Marshal.SizeOf<RekallAgeVulkanSceneGpuVertex>(), prepared.GeometryUpload.VertexBytes.Length);
        Assert.Equal(prepared.IndexCount * sizeof(uint), prepared.GeometryUpload.IndexBytes.Length);
    }

    [Fact]
    public void PreparedOpenXrFrameDoesNotAllocateReadbackBytes()
    {
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            160,
            90,
            null,
            [],
            [],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
        var target = RekallAgeVulkanSceneRenderTarget.OpenXrStereoSwapchain(
            160,
            90,
            2,
            Silk.NET.Vulkan.Format.R8G8B8A8Srgb,
            Silk.NET.Vulkan.Format.D32Sfloat);

        var prepared = RekallAgeVulkanScenePreparedFrameBuilder.Build(frame, [], target);

        Assert.False(prepared.HasDrawableGeometry);
        Assert.False(prepared.GeometryUpload.HasGeometry);
        Assert.Equal(0UL, prepared.ReadbackByteCount);
        Assert.Equal(target, prepared.Target);
    }

    [Fact]
    public void DrawPlanPreservesMaterialTextureBindings()
    {
        var batch = new RekallAgeVulkanSceneBatch(
            [],
            [],
            [
                new RekallAgeVulkanSceneDraw(
                    0,
                    3,
                    0,
                    3,
                    System.Numerics.Matrix4x4.Identity,
                    TextureId: "base",
                    MetallicRoughnessTextureId: "mr",
                    NormalTextureId: "normal",
                    OcclusionTextureId: "occ",
                    EmissiveTextureId: "emissive")
            ],
            new RekallAgeVulkanSceneFrameUniform(
                System.Numerics.Matrix4x4.Identity,
                System.Numerics.Vector3.UnitZ,
                System.Numerics.Vector4.One,
                System.Numerics.Vector4.Zero));

        var plan = RekallAgeVulkanSceneDrawPlanBuilder.Build(batch);

        var draw = Assert.Single(plan.Draws);
        Assert.Equal("base", draw.BaseColorTextureId);
        Assert.Equal("mr", draw.MetallicRoughnessTextureId);
        Assert.Equal("normal", draw.NormalTextureId);
        Assert.Equal("occ", draw.OcclusionTextureId);
        Assert.Equal("emissive", draw.EmissiveTextureId);
        Assert.Contains(new RekallAgeVulkanSceneMaterialKey("base", "normal", "mr", "occ", "emissive"), plan.MaterialKeys);
        Assert.Contains(RekallAgeVulkanSceneMaterialKey.Default, plan.MaterialKeys);
    }

    [Fact]
    public void GeometryUploadUsesPackedGpuVertexAndUintIndexBytes()
    {
        var batch = new RekallAgeVulkanSceneBatch(
            [
                new RekallAgeVulkanSceneVertex(1, 2, 3, 0, 1, 0, 0.25f, 0.5f, 0.75f, 1, 0.125f, 0.875f)
            ],
            [7],
            [],
            new RekallAgeVulkanSceneFrameUniform(
                System.Numerics.Matrix4x4.Identity,
                System.Numerics.Vector3.UnitZ,
                System.Numerics.Vector4.One,
                System.Numerics.Vector4.Zero));

        var upload = RekallAgeVulkanSceneGeometryUploadBuilder.Build(batch);

        Assert.Equal(1, upload.VertexCount);
        Assert.Equal(1, upload.IndexCount);
        Assert.Equal(System.Runtime.InteropServices.Marshal.SizeOf<RekallAgeVulkanSceneGpuVertex>(), upload.VertexBytes.Length);
        Assert.Equal(sizeof(uint), upload.IndexBytes.Length);
        Assert.Contains((byte)7, upload.IndexBytes);
    }

    [Fact]
    public void UniformUploadPreservesFrameAndDrawConstants()
    {
        var frame = new RekallAgeVulkanSceneFrameUniform(
            System.Numerics.Matrix4x4.CreateTranslation(1, 2, 3),
            new System.Numerics.Vector3(0.1f, 0.2f, 0.3f),
            new System.Numerics.Vector4(0.4f, 0.5f, 0.6f, 0.7f),
            new System.Numerics.Vector4(8, 9, 10, 1));

        var uniform = RekallAgeVulkanSceneUniformUploadBuilder.BuildFrameUniform(frame);
        var push = RekallAgeVulkanSceneUniformUploadBuilder.BuildDrawPushConstants(
            System.Numerics.Matrix4x4.Identity,
            new System.Numerics.Vector4(0.2f, 0.8f, 1.1f, 0.6f),
            new System.Numerics.Vector4(1, 0.5f, 0.25f, 3));

        Assert.Equal(1, uniform.ViewProjection.M41);
        Assert.Equal(2, uniform.ViewProjection.M42);
        Assert.Equal(3, uniform.ViewProjection.M43);
        Assert.Equal(0.2f, uniform.LightY);
        Assert.Equal(0.6f, uniform.LightB);
        Assert.Equal(10, uniform.LightPositionZ);
        Assert.Equal(0.8f, push.RoughnessFactor);
        Assert.Equal(3, push.EmissiveStrength);
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf<RekallAgeVulkanSceneGpuDrawPushConstants>() > System.Runtime.InteropServices.Marshal.SizeOf<RekallAgeVulkanSceneGpuMatrix4x4>());
    }
}
