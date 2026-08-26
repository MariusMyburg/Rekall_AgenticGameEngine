using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshFractureTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public async Task FractureProducesValidNonOverlappingChunksApproximatingSourceVolume(int chunkCount)
    {
        var source = await Primitive("rekall.modeling.primitive.box");

        var chunks = RekallAgeMeshFracture.Fracture(source, chunkCount, seed: 42);

        Assert.Equal(chunkCount, chunks.Count);
        var validator = new RekallAgeMeshValidator();
        foreach (var chunk in chunks)
        {
            var validation = validator.Validate(chunk);
            Assert.True(validation.IsValid, string.Join(", ", validation.Diagnostics.Select(item => item.Message)));
            Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
            Assert.Equal(0, validation.Summary.NonManifoldEdgeCount);
        }

        var sourceVolume = MeshVolume(source);
        var chunkVolumeSum = chunks.Sum(MeshVolume);
        Assert.InRange(chunkVolumeSum, sourceVolume * 0.9, sourceVolume * 1.1);
    }

    [Fact]
    public async Task FractureIsDeterministicForTheSameSeed()
    {
        var source = await Primitive("rekall.modeling.primitive.box");

        var first = RekallAgeMeshFracture.Fracture(source, 4, seed: 7);
        var second = RekallAgeMeshFracture.Fracture(source, 4, seed: 7);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
            Assert.Equal(MeshVolume(first[i]), MeshVolume(second[i]), precision: 6);
    }

    [Fact]
    public async Task RejectsAnOutOfRangeChunkCount()
    {
        var source = await Primitive("rekall.modeling.primitive.box");

        Assert.Throws<ArgumentOutOfRangeException>(() => RekallAgeMeshFracture.Fracture(source, 1, seed: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RekallAgeMeshFracture.Fracture(source, 65, seed: 1));
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
