using System.Numerics;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class PerspectiveSoftwareSceneRendererTests
{
    [Fact]
    public void RendererDrawsCenteredMeshThroughCameraProjection()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Camera",
            "Rekall.Camera3D",
            true,
            0,
            0,
            -4,
            0,
            0,
            0,
            FieldOfViewDegrees: 60,
            ClearColor: "#010203");
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            128,
            72,
            camera,
            [camera],
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
                    Variant: "rekall.geometry.cube",
                    MaterialColor: "#ff3030")
            ],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);
        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);
        var renderer = new RekallAgePerspectiveSoftwareSceneRenderer();
        var viewProjection = renderer.CreateCameraViewProjection(camera, frame.Width, frame.Height, Quaternion.Identity, Vector3.Zero);

        var pixels = renderer.Render(batch, frame.Width, frame.Height, viewProjection, camera.ClearColor);

        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), pixel =>
        {
            var offset = pixel * 4;
            return pixels[offset] > 30
                && pixels[offset + 1] < 80
                && pixels[offset + 2] < 80;
        });
    }

    [Fact]
    public void RendererSamplesBaseColorTextureUsingTriangleUvs()
    {
        var batch = new RekallAgeVulkanSceneBatch(
            [
                new RekallAgeVulkanSceneVertex(-0.75f, -0.75f, 0, 0, 0, 1, 1, 1, 1, 1, 0, 1),
                new RekallAgeVulkanSceneVertex(0.75f, -0.75f, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1),
                new RekallAgeVulkanSceneVertex(0, 0.75f, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0)
            ],
            [0, 1, 2],
            [
                new RekallAgeVulkanSceneDraw(
                    0,
                    3,
                    0,
                    3,
                    Matrix4x4.Identity,
                    TextureId: "test")
            ],
            new RekallAgeVulkanSceneFrameUniform(
                Matrix4x4.Identity,
                new Vector3(0, 0, -1),
                Vector4.One,
                Vector4.Zero));
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Camera",
            "Rekall.Camera3D",
            true,
            0,
            0,
            -3,
            0,
            0,
            0,
            FieldOfViewDegrees: 60);
        var texture = new RekallAgeRgbaImage(
            2,
            2,
            [
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 0, 255
            ]);
        var renderer = new RekallAgePerspectiveSoftwareSceneRenderer();
        var viewProjection = renderer.CreateCameraViewProjection(camera, 96, 64, Quaternion.Identity, Vector3.Zero);

        var pixels = renderer.Render(
            batch,
            96,
            64,
            viewProjection,
            "#000000",
            new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal)
            {
                ["test"] = texture
            });

        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), pixel =>
        {
            var offset = pixel * 4;
            return pixels[offset + 2] > 100
                && pixels[offset] < 100
                && pixels[offset + 1] < 100;
        });
    }

    [Fact]
    public void CameraViewProjectionCanUseOpenXrPerEyeFov()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Camera",
            "Rekall.Camera3D",
            true,
            0,
            0,
            0,
            0,
            0,
            0,
            FieldOfViewDegrees: 60);
        var renderer = new RekallAgePerspectiveSoftwareSceneRenderer();

        var symmetric = renderer.CreateCameraViewProjection(
            camera,
            100,
            100,
            Quaternion.Identity,
            Vector3.Zero);
        var perEye = renderer.CreateCameraViewProjection(
            camera,
            100,
            100,
            Quaternion.Identity,
            Vector3.Zero,
            -0.45f,
            0.75f,
            0.50f,
            -0.50f);

        Assert.NotEqual(symmetric.M31, perEye.M31);
        Assert.NotEqual(symmetric.M11, perEye.M11);
    }
}
