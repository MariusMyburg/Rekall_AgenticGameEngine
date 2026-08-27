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
        Assert.Contains(new RekallAgeVulkanSceneMaterialKey("base", "normal", "mr", "occ", "emissive", null, null), plan.MaterialKeys);
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
            new System.Numerics.Vector4(8, 9, 10, 1),
            AdditionalLightColor: new System.Numerics.Vector4(2, 1, 0, 1),
            AdditionalLightPosition: new System.Numerics.Vector4(1, 2, 3, 1),
            AdditionalLightParameters: new System.Numerics.Vector4(7, 4, 0, 0),
            EnvironmentParameters: new System.Numerics.Vector4(0.55f, -0.35f, 11.2f, 1))
        {
            EnvironmentAmbientSkyColor = new(0.35f, 0.5f, 0.8f, 1),
            EnvironmentAmbientGroundColor = new(0.3f, 0.2f, 0.1f, 1),
            PointLights =
            [
                new("one", new(2, 1, 0, 1), new(1, 2, 3, 1), new(7, 4, 0, 0)),
                new("two", new(0, 3, 1, 1), new(4, 5, 6, 1), new(8, 3, 0, 0)),
                new("three", new(1, 0, 4, 1), new(7, 8, 9, 1), new(9, 2, 0, 0)),
                new("four", new(2, 2, 2, 1), new(10, 11, 12, 1), new(10, 1, 0, 0)),
                new("five", new(5, 4, 3, 1), new(13, 14, 15, 1), new(11, 0, 0, 0))
            ]
        };

        var uniform = RekallAgeVulkanSceneUniformUploadBuilder.BuildFrameUniform(frame);
        var push = RekallAgeVulkanSceneUniformUploadBuilder.BuildDrawPushConstants(
            System.Numerics.Matrix4x4.Identity,
            new System.Numerics.Vector4(0.2f, 0.8f, 1.1f, 0.6f),
            new System.Numerics.Vector4(1, 0.5f, 0.25f, 3),
            atmosphereColor0: new System.Numerics.Vector4(0.1f, 0.2f, 0.3f, 0.4f),
            atmosphereColor1: new System.Numerics.Vector4(0.5f, 0.6f, 0.7f, 0.8f),
            atmosphereColor2: new System.Numerics.Vector4(0.9f, 1.0f, 1.1f, 1.2f),
            cloudFactors: new System.Numerics.Vector4(1.3f, 1.4f, 1.5f, 1.6f),
            cloudColor: new System.Numerics.Vector4(0.7f, 0.8f, 0.9f, 1.0f),
            cloudShadowFactors: new System.Numerics.Vector4(2.1f, 2.2f, 2.3f, 2.4f),
            surfaceWaterFactors: new System.Numerics.Vector4(3.1f, 3.2f, 3.3f, 3.4f));

        Assert.Equal(1, uniform.ViewProjection.M41);
        Assert.Equal(2, uniform.ViewProjection.M42);
        Assert.Equal(3, uniform.ViewProjection.M43);
        Assert.Equal(0.2f, uniform.LightY);
        Assert.Equal(0.6f, uniform.LightB);
        Assert.Equal(10, uniform.LightPositionZ);
        Assert.Equal(5, uniform.AdditionalLightDirectionPad);
        Assert.Equal(7, uniform.AdditionalLightRange);
        Assert.Equal(3, uniform.AdditionalLight2G);
        Assert.Equal(6, uniform.AdditionalLight2PositionZ);
        Assert.Equal(9, uniform.AdditionalLight3Range);
        Assert.Equal(12, uniform.AdditionalLight4PositionZ);
        var uniformBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
            System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in uniform, 1));
        Assert.Equal(1_280, uniformBytes.Length);
        Assert.Equal(5, System.BitConverter.ToSingle(uniformBytes.Slice(656, sizeof(float))));
        Assert.Equal(0.55f, uniform.EnvironmentAmbientEnergy);
        Assert.Equal(-0.35f, uniform.EnvironmentExposure);
        Assert.Equal(11.2f, uniform.EnvironmentWhitePoint);
        Assert.Equal(1, uniform.EnvironmentToneMapper);
        Assert.Equal(0.35f, uniform.EnvironmentAmbientSkyR);
        Assert.Equal(0.8f, uniform.EnvironmentAmbientSkyB);
        Assert.Equal(0.3f, uniform.EnvironmentAmbientGroundR);
        Assert.Equal(0.1f, uniform.EnvironmentAmbientGroundB);
        Assert.Equal(0.8f, push.RoughnessFactor);
        Assert.Equal(3, push.EmissiveStrength);
        Assert.Equal(0.1f, push.AtmosphereRayleighR);
        Assert.Equal(0.8f, push.AtmosphereAerialPerspectiveStrength);
        Assert.Equal(0.9f, push.AtmosphereOzoneR);
        Assert.Equal(1.2f, push.AtmosphereOzoneAbsorption);
        Assert.Equal(1.4f, push.CloudCoverage);
        Assert.Equal(0.8f, push.CloudColorG);
        Assert.Equal(2.2f, push.CloudShadowRadius);
        Assert.Equal(2.3f, push.CloudShadowStrength);
        Assert.Equal(3.2f, push.SurfaceWaterCoverage);
        Assert.Equal(3.3f, push.SurfaceWaterSpecularStrength);
        Assert.Equal(3.4f, push.SurfaceWaterRoughness);
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf<RekallAgeVulkanSceneGpuDrawPushConstants>() > System.Runtime.InteropServices.Marshal.SizeOf<RekallAgeVulkanSceneGpuMatrix4x4>());
    }

    [Theory]
    [InlineData("mask", 0.35, 1, 0.35f)]
    [InlineData("blend", 0.35, 0, 0.35f)]
    [InlineData("opaque", 0.35, 0, 0.35f)]
    [InlineData("MASK", 0.9, 1, 0.9f)]
    public void BuildDrawPushConstantsEncodesAlphaModeAndCutoff(
        string alphaMode,
        double alphaCutoff,
        float expectedAlphaMask,
        float expectedAlphaCutoff)
    {
        // Only "mask" (case-insensitively) should ever set AlphaMask - the shader's discard logic
        // gates entirely on this flag, so an authoring typo or a different mode string must not
        // accidentally enable cutout behavior for an ordinary opaque/blend material.
        var push = RekallAgeVulkanSceneUniformUploadBuilder.BuildDrawPushConstants(
            System.Numerics.Matrix4x4.Identity,
            System.Numerics.Vector4.Zero,
            System.Numerics.Vector4.Zero,
            alphaMode: alphaMode,
            alphaCutoff: (float)alphaCutoff);

        Assert.Equal(expectedAlphaMask, push.AlphaMask);
        Assert.Equal(expectedAlphaCutoff, push.AlphaCutoff);
    }

    [Fact]
    public void BuildDrawPushConstantsClampsAlphaCutoffToTheValidZeroToOneRange()
    {
        var tooLow = RekallAgeVulkanSceneUniformUploadBuilder.BuildDrawPushConstants(
            System.Numerics.Matrix4x4.Identity,
            System.Numerics.Vector4.Zero,
            System.Numerics.Vector4.Zero,
            alphaMode: "mask",
            alphaCutoff: -2);
        var tooHigh = RekallAgeVulkanSceneUniformUploadBuilder.BuildDrawPushConstants(
            System.Numerics.Matrix4x4.Identity,
            System.Numerics.Vector4.Zero,
            System.Numerics.Vector4.Zero,
            alphaMode: "mask",
            alphaCutoff: 5);

        Assert.Equal(0, tooLow.AlphaCutoff);
        Assert.Equal(1, tooHigh.AlphaCutoff);
    }
}
