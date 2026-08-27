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
    public async Task FractureOfASphereSucceedsForASeedThatDegeneratesTheUnfixedCsgKernel()
    {
        // Reproduces a real crash found while authoring an example game: fracturing this exact
        // sphere (radius 0.5, 10 segments, 7 rings) with seed 2 or seed 7 threw an unhandled
        // NullReferenceException from inside CSG.Sharp's Node.Invert(), called from CSG.Intersect,
        // for a random seed-pair whose perpendicular-bisector slab produces a numerically
        // degenerate cut against this mesh's geometry. Seeds 1, 3, and 42 succeeded on the
        // identical source, so this is seed-specific, not a general sphere-fracture failure.
        var source = await Sphere(radius: 0.5, segments: 10, rings: 7);

        var chunks = RekallAgeMeshFracture.Fracture(source, 5, seed: 2);

        Assert.Equal(5, chunks.Count);
        var validator = new RekallAgeMeshValidator();
        foreach (var chunk in chunks)
        {
            var validation = validator.Validate(chunk);
            Assert.True(validation.IsValid, string.Join(", ", validation.Diagnostics.Select(item => item.Message)));
        }
    }

    [Fact]
    public async Task RejectsAnOutOfRangeChunkCount()
    {
        var source = await Primitive("rekall.modeling.primitive.box");

        Assert.Throws<ArgumentOutOfRangeException>(() => RekallAgeMeshFracture.Fracture(source, 1, seed: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RekallAgeMeshFracture.Fracture(source, 65, seed: 1));
    }

    [Fact]
    public async Task FractureOfAThinElongatedSlabStaysWithinTheSourceBounds()
    {
        // A wall-shaped source (much wider/taller than it is thick) reproduces a real failure:
        // the CSG slab used to carve each chunk was sized off the source's single largest axis
        // span, so for a thin slab the cutting box was wildly oversized relative to the source's
        // thin dimension, and the CSG intersection leaked slab-boundary vertices far outside the
        // source's own bounds instead of clipping cleanly to it.
        var source = await Primitive("rekall.modeling.primitive.box", sizeX: 4, sizeY: 2.4, sizeZ: 0.4);
        var (sourceMin, sourceMax) = Bounds(source.Topology.Positions);
        const double epsilon = 0.01;

        var chunks = RekallAgeMeshFracture.Fracture(source, 6, seed: 31);

        Assert.Equal(6, chunks.Count);
        foreach (var chunk in chunks)
        {
            Assert.All(chunk.Topology.Positions, position =>
            {
                Assert.InRange(position.X, sourceMin.X - epsilon, sourceMax.X + epsilon);
                Assert.InRange(position.Y, sourceMin.Y - epsilon, sourceMax.Y + epsilon);
                Assert.InRange(position.Z, sourceMin.Z - epsilon, sourceMax.Z + epsilon);
            });
        }

        var sourceVolume = MeshVolume(source);
        var chunkVolumeSum = chunks.Sum(MeshVolume);
        Assert.InRange(chunkVolumeSum, sourceVolume * 0.9, sourceVolume * 1.1);
    }

    private static (RekallAgeGeometryVector3 Min, RekallAgeGeometryVector3 Max) Bounds(
        IReadOnlyList<RekallAgeGeometryVector3> positions)
    {
        var min = positions[0];
        var max = positions[0];
        foreach (var position in positions)
        {
            min = new(Math.Min(min.X, position.X), Math.Min(min.Y, position.Y), Math.Min(min.Z, position.Z));
            max = new(Math.Max(max.X, position.X), Math.Max(max.Y, position.Y), Math.Max(max.Z, position.Z));
        }
        return (min, max);
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

    private static async ValueTask<RekallAgeMeshAsset> Sphere(double radius, int segments, int rings)
    {
        var parameters = new System.Text.Json.Nodes.JsonObject
        {
            ["radius"] = radius,
            ["segments"] = segments,
            ["rings"] = rings
        };
        var graph = RekallAgeModelingGraphAsset.Create("source", "Source", [new("source", "rekall.modeling.primitive.sphere", 1, parameters)], [], [new("mesh", "source", "geometry")]);
        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "tests", "desktop"), default);
        return result.Outputs["mesh"];
    }

    private static async ValueTask<RekallAgeMeshAsset> Primitive(
        string typeId,
        double? sizeX = null,
        double? sizeY = null,
        double? sizeZ = null)
    {
        var parameters = new System.Text.Json.Nodes.JsonObject();
        if (sizeX.HasValue) parameters["sizeX"] = sizeX.Value;
        if (sizeY.HasValue) parameters["sizeY"] = sizeY.Value;
        if (sizeZ.HasValue) parameters["sizeZ"] = sizeZ.Value;
        var graph = RekallAgeModelingGraphAsset.Create("source", "Source", [new("source", typeId, 1, parameters)], [], [new("mesh", "source", "geometry")]);
        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "tests", "desktop"), default);
        return result.Outputs["mesh"];
    }
}
