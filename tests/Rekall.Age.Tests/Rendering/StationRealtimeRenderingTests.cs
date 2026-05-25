using Rekall.Age.Rendering;
using Rekall.Age.Runtime;

namespace Rekall.Age.Tests.Rendering;

public sealed class StationRealtimeRenderingTests
{
    private const string StationAssetId = "asset_space-station-megastructure_9533247a";

    [Fact]
    public async Task StationExampleResolvesTexturedGlbAndRendersThroughRealtimeVulkanPath()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectRoot = Path.Combine(repositoryRoot, "Examples", "GlbStationTest");
        var stationGlb = Path.Combine(projectRoot, "Assets", "model", $"{StationAssetId}.glb");
        Assert.True(File.Exists(stationGlb), $"Station GLB fixture is missing at '{stationGlb}'.");

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            1,
            CancellationToken.None);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        Assert.Contains(frame.Renderables, renderable => renderable.Kind == "light");
        Assert.Contains(frame.Renderables, renderable => renderable.Kind == "mesh" && renderable.AssetId == StationAssetId);

        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            projectRoot,
            frame,
            CancellationToken.None);

        Assert.Empty(assets.Issues);
        Assert.True(assets.Models.ContainsKey(StationAssetId));
        var stationMeshes = assets.Models[StationAssetId];
        Assert.NotEmpty(stationMeshes);

        var texturedMeshes = stationMeshes
            .Where(mesh => mesh.BaseColorTexture is not null)
            .ToArray();
        Assert.NotEmpty(texturedMeshes);
        var baseColorTexture = texturedMeshes[0].BaseColorTexture!;
        Assert.True(baseColorTexture.Width > 1);
        Assert.True(baseColorTexture.Height > 1);
        Assert.NotEmpty(baseColorTexture.Rgba);
        Assert.Contains(stationMeshes, mesh => mesh.MetallicRoughnessTexture is not null);
        Assert.Contains(stationMeshes, mesh => mesh.NormalTexture is not null);

        var vertexCount = stationMeshes.Sum(mesh => mesh.Vertices.Count);
        var indexCount = stationMeshes.Sum(mesh => mesh.Indices.Count);
        Assert.True(vertexCount > 1_000_000);
        Assert.True(indexCount > 5_000_000);
        Assert.True(vertexCount < indexCount);

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, stationMeshes);
        Assert.Equal(vertexCount, batch.Vertices.Count);
        Assert.Equal(indexCount, batch.Indices.Count);
        Assert.Equal(stationMeshes.Count, batch.Draws.Count);
        Assert.Contains(batch.Draws, draw => draw.TextureId == baseColorTexture.Id);

        var capture = await new RekallAgeNativeVulkanSceneCapture().CaptureSceneAsync(
            frame,
            assets,
            Path.Combine(TestPaths.CreateTempDirectory(), "station-vulkan"),
            "discrete-gpu",
            CancellationToken.None);

        Assert.True(capture.Captured, string.Join(Environment.NewLine, capture.Errors));
        Assert.True(capture.VertexBufferCreated);
        Assert.True(capture.IndexBufferCreated);
        Assert.True(capture.TextureResourcesCreated);
        Assert.True(capture.GraphicsPipelineCreated);
        Assert.Equal(stationMeshes.Count, capture.MeshCount);
        Assert.Equal(batch.Draws.Count, capture.DrawCallCount);
        Assert.True(capture.NonZeroBytes > 0);

        var image = await RekallAgePngReader.ReadRgbaAsync(capture.OutputPath, CancellationToken.None);
        Assert.True(CountPixelsDifferentFromClear(image) > 0);
    }

    private static int CountPixelsDifferentFromClear(RekallAgeRgbaImage image)
    {
        var changed = 0;
        for (var offset = 0; offset + 3 < image.Rgba.Length; offset += 4)
        {
            if (Math.Abs(image.Rgba[offset + 0] - 20) > 2
                || Math.Abs(image.Rgba[offset + 1] - 26) > 2
                || Math.Abs(image.Rgba[offset + 2] - 36) > 2)
            {
                changed++;
            }
        }

        return changed;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Rekall.AGE.sln from the test output directory.");
    }
}
