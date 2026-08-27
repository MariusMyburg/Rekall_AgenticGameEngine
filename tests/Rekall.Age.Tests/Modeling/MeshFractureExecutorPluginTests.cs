using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshFractureExecutorPluginTests
{
    [Fact]
    public async Task DefaultAlgorithmIdCallsTheExistingBuiltInVoronoiFracture()
    {
        var source = await Primitive("rekall.modeling.primitive.box");
        var executor = new RekallAgeMeshFractureExecutor();

        var direct = RekallAgeMeshFracture.Fracture(source, 4, seed: 7);
        var viaExecutor = executor.Fracture(source, 4, seed: 7);
        var viaExplicitId = executor.Fracture(source, 4, seed: 7, RekallAgeMeshFractureExecutor.BuiltInVoronoiAlgorithmId);

        Assert.Equal(direct.Count, viaExecutor.Count);
        for (var i = 0; i < direct.Count; i++)
        {
            Assert.Equal(MeshVolume(direct[i]), MeshVolume(viaExecutor[i]), precision: 6);
            Assert.Equal(MeshVolume(direct[i]), MeshVolume(viaExplicitId[i]), precision: 6);
        }
    }

    [Fact]
    public async Task RegisteredPluginAlgorithmIsDispatchedById()
    {
        var source = await Primitive("rekall.modeling.primitive.box");
        var plugin = new SingleChunkAlgorithm();
        var executor = new RekallAgeMeshFractureExecutor([plugin]);

        var chunks = executor.Fracture(source, 1, seed: 0, plugin.AlgorithmId);

        Assert.True(plugin.WasCalled);
        Assert.Single(chunks);
    }

    [Fact]
    public void UnknownAlgorithmIdThrows()
    {
        var executor = new RekallAgeMeshFractureExecutor();
        var source = SimpleTriangleMesh();

        Assert.Throws<ArgumentException>(() => executor.Fracture(source, 2, seed: 0, "test.no_such_algorithm"));
    }

    private static RekallAgeMeshAsset SimpleTriangleMesh() => RekallAgeMeshAsset.Create(
        "triangle", "Triangle",
        new(
            PointIds: [1, 2, 3],
            Positions: [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            EdgeIds: [11, 12, 13],
            EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0)],
            FaceIds: [21],
            FaceOffsets: [0, 3],
            CornerIds: [31, 32, 33],
            CornerPointIndices: [0, 1, 2],
            CornerEdgeIndices: [0, 1, 2]));

    private sealed class SingleChunkAlgorithm : IRekallAgeFractureAlgorithmPlugin
    {
        public bool WasCalled { get; private set; }
        public string AlgorithmId => "test.single_chunk";
        public IReadOnlyList<RekallAgeMeshAsset> Fracture(RekallAgeMeshAsset source, int chunkCount, long seed)
        {
            WasCalled = true;
            return [source with { AssetId = $"{source.AssetId}-chunk-0" }];
        }
    }

    private static double MeshVolume(RekallAgeMeshAsset mesh)
    {
        var compiled = new RekallAgeMeshCompiler().Compile(mesh);
        double volume = 0;
        for (var triangle = 0; triangle < compiled.Triangles.Count; triangle++)
        {
            var indices = compiled.Indices.Skip(triangle * 3).Take(3).Select(index => checked((int)index)).ToArray();
            var p0 = compiled.Vertices[indices[0]].Position;
            var p1 = compiled.Vertices[indices[1]].Position;
            var p2 = compiled.Vertices[indices[2]].Position;
            volume += (p0.X * (p1.Y * p2.Z - p2.Y * p1.Z)
                     - p0.Y * (p1.X * p2.Z - p2.X * p1.Z)
                     + p0.Z * (p1.X * p2.Y - p2.X * p1.Y)) / 6.0;
        }
        return Math.Abs(volume);
    }

    private static async ValueTask<RekallAgeMeshAsset> Primitive(string typeId)
    {
        var graph = RekallAgeModelingGraphAsset.Create("source", "Source", [new("source", typeId, 1, new())], [], [new("mesh", "source", "geometry")]);
        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "tests", "desktop"), default);
        return result.Outputs["mesh"];
    }
}
