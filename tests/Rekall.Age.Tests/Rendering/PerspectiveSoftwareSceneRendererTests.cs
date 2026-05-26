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
}
