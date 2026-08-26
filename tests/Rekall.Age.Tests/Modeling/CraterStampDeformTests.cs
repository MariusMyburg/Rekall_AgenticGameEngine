using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class CraterStampDeformTests
{
    [Fact]
    public async Task PointsBeyondRadiusAreUntouchedAndTheCenterPointDropsByTheFullDepth()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync("box", "crater-box", "Crater Box", CancellationToken.None);
        // Center the crater on the box's own (-0.5, *, -0.5) top corner, radius 0.5: only that
        // corner (distance 0) is within radius, every other corner (distance >= 1) is untouched.
        var result = new RekallAgeMeshOperationExecutor().Execute(source,
            new("crater_stamp", RekallAgeGeometryDomain.Point, source.Topology.PointIds, new JsonObject
            {
                ["axis"] = "y",
                ["centerX"] = -0.5, ["centerY"] = 0.0, ["centerZ"] = -0.5,
                ["radius"] = 0.5,
                ["depth"] = 1.0
            }));

        Assert.True(result.Validation.IsValid);
        var centerIndex = source.Topology.Positions.ToList().FindIndex(point => point.X == -0.5 && point.Y == 0.5 && point.Z == -0.5);
        var farIndex = source.Topology.Positions.ToList().FindIndex(point => point.X == 0.5 && point.Y == 0.5 && point.Z == 0.5);
        Assert.True(centerIndex >= 0); Assert.True(farIndex >= 0);

        var centerAfter = result.Mesh.Topology.Positions[centerIndex];
        var farAfter = result.Mesh.Topology.Positions[farIndex];
        Assert.Equal(0.5 - 1.0, centerAfter.Y, 8); // full depth at the crater center
        Assert.Equal(0.5, farAfter.Y, 8); // beyond the radius: untouched
        Assert.Equal(-0.5, centerAfter.X, 8); // only the axis coordinate moves
        Assert.Equal(-0.5, centerAfter.Z, 8);
    }

    [Fact]
    public async Task DisplacementFallsOffMonotonicallyWithDistanceFromCenter()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync("box", "crater-falloff-box", "Crater Falloff Box", CancellationToken.None);
        var result = new RekallAgeMeshOperationExecutor().Execute(source,
            new("crater_stamp", RekallAgeGeometryDomain.Point, source.Topology.PointIds, new JsonObject
            {
                ["axis"] = "y",
                ["centerX"] = -0.5, ["centerY"] = 0.0, ["centerZ"] = -0.5,
                ["radius"] = 2.0,
                ["depth"] = 1.0
            }));

        Assert.True(result.Validation.IsValid);
        double DropAt(double x, double z)
        {
            var index = source.Topology.Positions.ToList().FindIndex(point => point.X == x && point.Y == 0.5 && point.Z == z);
            Assert.True(index >= 0);
            return 0.5 - result.Mesh.Topology.Positions[index].Y;
        }

        var atCenter = DropAt(-0.5, -0.5);        // distance 0
        var oneAway = DropAt(0.5, -0.5);           // distance 1
        var diagonal = DropAt(0.5, 0.5);           // distance sqrt(2)

        Assert.True(atCenter > oneAway);
        Assert.True(oneAway > diagonal);
        Assert.True(diagonal > 0); // still inside the radius (sqrt(2) < 2.0)
    }

    [Fact]
    public async Task RejectsANonPositiveRadius()
    {
        var source = await new RekallAgeMeshPrimitiveFactory().CreateAsync("box", "crater-invalid-box", "Crater Invalid Box", CancellationToken.None);

        var exception = Assert.Throws<RekallAgeMeshOperationException>(() => new RekallAgeMeshOperationExecutor().Execute(source,
            new("crater_stamp", RekallAgeGeometryDomain.Point, source.Topology.PointIds, new JsonObject { ["radius"] = 0.0 })));

        Assert.Equal("REKALL_MESH_CRATER_RADIUS_INVALID", exception.Code);
    }
}
