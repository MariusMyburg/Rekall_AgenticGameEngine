using System.Text.Json.Nodes;
using System.IO;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioModelingSessionTests
{
    [Fact]
    public void TypedParameterEditorUsesDescriptorDefaultsAndRejectsInvalidNumbers()
    {
        var descriptor = new RekallAgeMeshOperationExecutor().Descriptors.Single(item => item.OperationId == "extrude_faces");
        var z = new RekallAgeStudioMeshParameterModel(descriptor.Parameters.Single(item => item.Name == "z"));

        Assert.Equal("1", z.ValueText);
        Assert.True(z.TryGetValue(out var defaultValue));
        Assert.Equal(1, defaultValue!.GetValue<double>());
        z.ValueText = "not-a-number";
        Assert.False(z.IsValid);
        Assert.False(z.TryGetValue(out _));
    }

    [Fact]
    public async Task StudioMeshSessionPreviewsWithoutMutationThenAppliesThroughTransactionHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-modeling-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RekallAgeMeshAssetStore();
            await store.SaveAsync(root, Quad(), CancellationToken.None);
            var session = new RekallAgeStudioModelingSession();

            Assert.Equal(["quad"], session.ListAssets(root));
            await session.OpenAsync(root, "quad", CancellationToken.None);
            session.Select(21);
            Assert.Equal(21UL, session.ActiveElementId);
            Assert.Contains(session.AvailableOperations, item => item.OperationId == "extrude_faces");

            var preview = await session.PreviewAsync("extrude_faces",
                new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 1.0 }, CancellationToken.None);
            Assert.Equal(5, preview.Mesh.Topology.FaceIds.Count);
            Assert.Single((await store.LoadAsync(root, "quad", CancellationToken.None)).Topology.FaceIds);

            var applied = await session.ApplyAsync("extrude_faces",
                new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 1.0 }, "studio", CancellationToken.None);
            Assert.Equal(5, applied.Mesh.Topology.FaceIds.Count);
            Assert.Equal(5, session.Mesh!.Topology.FaceIds.Count);
            Assert.Null(session.Preview);
            Assert.Empty(session.SelectedElementIds);
            Assert.Single((await new RekallAgeTransactionLogStore().LoadAsync(root, CancellationToken.None)).Transactions);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static RekallAgeMeshAsset Quad() => RekallAgeMeshAsset.Create("quad", "Quad",
        new(
            PointIds: [1, 2, 3, 4], Positions: [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
            EdgeIds: [11, 12, 13, 14], EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
            FaceIds: [21], FaceOffsets: [0, 4], CornerIds: [31, 32, 33, 34],
            CornerPointIndices: [0, 1, 2, 3], CornerEdgeIndices: [0, 1, 2, 3]));
}
