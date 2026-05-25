using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class SceneDebugHudTests
{
    [Fact]
    public void FormatLinesIncludesScenePerformanceAndGeometryCounters()
    {
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            1280,
            720,
            null,
            [],
            [
                new RekallAgeRuntimeViewportRenderable("mesh-1", "Station", "mesh", "asset_station", 0, 0, 0, 1),
                new RekallAgeRuntimeViewportRenderable("mesh-1:collider", "Station Collider", "mesh", null, 0, 0, 0, 900),
                new RekallAgeRuntimeViewportRenderable("light-1", "Sun", "light", null, 0, 0, 0, 2)
            ],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
        var texture = new RekallAgeVulkanSceneTexture(
            "asset_station/texture/0",
            1,
            1,
            [255, 255, 255, 255],
            new RekallAgeVulkanSceneSampler(
                RekallAgeVulkanSceneFilter.Linear,
                RekallAgeVulkanSceneFilter.Linear,
                RekallAgeVulkanSceneWrapMode.Repeat,
                RekallAgeVulkanSceneWrapMode.Repeat));
        var mesh = new RekallAgeVulkanSceneMesh(
            "mesh-1",
            "Station",
            "glb",
            [],
            Enumerable.Range(0, 6_000_000).Select(index => (uint)(index % 3)).ToArray(),
            texture);
        var stats = RekallAgeSceneDebugHud.CreateStats(
            frame,
            [mesh],
            entityCount: 2,
            fps: 84,
            backend: "Vulkan",
            submittedVertexCount: 5_775_990,
            drawCallCount: 1);

        var lines = RekallAgeSceneDebugHud.FormatLines(stats);

        Assert.Contains("FPS 84", lines);
        Assert.Contains("ENTITIES 2", lines);
        Assert.Contains("RENDERABLES 3", lines);
        Assert.Contains("COLLIDERS 1", lines);
        Assert.Contains("TRIANGLES 2.0M", lines);
        Assert.Contains("VERTICES 5.8M", lines);
        Assert.Contains("TEXTURES 1", lines);
        Assert.Contains("DRAWS 1", lines);
        Assert.Contains("VULKAN", lines);
    }
}
