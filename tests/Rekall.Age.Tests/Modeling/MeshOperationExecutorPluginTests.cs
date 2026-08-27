using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshOperationExecutorPluginTests
{
    [Fact]
    public void ExecuteDispatchesToARegisteredPluginOperation()
    {
        var plugin = new DoublingPlugin();
        var executor = new RekallAgeMeshOperationExecutor([plugin]);
        var source = Box();
        var request = new RekallAgeMeshOperationRequest(
            plugin.OperationId,
            RekallAgeGeometryDomain.Point,
            source.Topology.PointIds,
            new JsonObject());

        var result = executor.Execute(source, request);

        Assert.True(plugin.WasCalled);
        Assert.Equal(source.Topology.Positions.Count, result.Mesh.Topology.Positions.Count);
    }

    [Fact]
    public void DescriptorsIncludesPluginDescriptorsAlongsideBuiltIns()
    {
        var plugin = new DoublingPlugin();
        var executor = new RekallAgeMeshOperationExecutor([plugin]);

        Assert.Contains(executor.Descriptors, item => item.OperationId == plugin.OperationId);
        Assert.Contains(executor.Descriptors, item => item.OperationId == "transform");
    }

    [Fact]
    public void ExecuteStillThrowsForATrulyUnknownOperationId()
    {
        var executor = new RekallAgeMeshOperationExecutor([new DoublingPlugin()]);
        var source = Box();
        var request = new RekallAgeMeshOperationRequest(
            "test.no_such_operation",
            RekallAgeGeometryDomain.Point,
            source.Topology.PointIds,
            new JsonObject());

        var error = Assert.Throws<RekallAgeMeshOperationException>(() => executor.Execute(source, request));
        Assert.Equal("REKALL_MESH_OPERATION_UNKNOWN", error.Code);
    }

    private static RekallAgeMeshAsset Box() => RekallAgeMeshAsset.Create(
        "box",
        "Box",
        new(
            PointIds: [1, 2, 3, 4, 5, 6, 7, 8],
            Positions:
            [
                new(-0.5, -0.5, -0.5), new(0.5, -0.5, -0.5), new(0.5, 0.5, -0.5), new(-0.5, 0.5, -0.5),
                new(-0.5, -0.5, 0.5), new(0.5, -0.5, 0.5), new(0.5, 0.5, 0.5), new(-0.5, 0.5, 0.5)
            ],
            EdgeIds: Enumerable.Range(0, 12).Select(value => (ulong)(11 + value)).ToArray(),
            EdgePointIndices:
            [
                new(0, 1), new(1, 2), new(2, 3), new(3, 0),
                new(4, 5), new(5, 6), new(6, 7), new(7, 4),
                new(0, 4), new(1, 5), new(2, 6), new(3, 7)
            ],
            FaceIds: [31, 32, 33, 34, 35, 36],
            FaceOffsets: [0, 4, 8, 12, 16, 20, 24],
            CornerIds: Enumerable.Range(0, 24).Select(value => (ulong)(41 + value)).ToArray(),
            CornerPointIndices: [0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7],
            CornerEdgeIndices: [3, 2, 1, 0, 4, 5, 6, 7, 0, 9, 4, 8, 1, 10, 5, 9, 2, 11, 6, 10, 3, 8, 7, 11]));

    private sealed class DoublingPlugin : IRekallAgeMeshOperationPlugin
    {
        public bool WasCalled { get; private set; }

        public string OperationId => "test.double_positions";

        public RekallAgeMeshOperationDescriptor Descriptor => new(
            OperationId, "Doubles selected point positions (test plugin).",
            RekallAgeGeometryDomain.Point, RekallAgeMeshChangeKind.Positions, []);

        public RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
        {
            WasCalled = true;
            var doubled = source.Topology.Positions
                .Select(position => new RekallAgeGeometryVector3(position.X * 2, position.Y * 2, position.Z * 2))
                .ToArray();
            var mesh = source with { Topology = source.Topology with { Positions = doubled }, Revision = source.Revision + 1 };
            var zeroBounds = new RekallAgeMeshBounds(new(0, 0, 0), new(0, 0, 0));
            // Execute()'s caller (RekallAgeMeshOperationExecutor.Execute) re-validates the
            // returned Mesh and overwrites Validation with fresh output before returning to the
            // caller, so the placeholder RekallAgeMeshValidationReport/Summary below never needs
            // to reflect the real mesh -- only its shape needs to be correct C#.
            return new RekallAgeMeshOperationResult(
                mesh, source.Revision, mesh.Revision,
                new RekallAgeMeshChangeSet(
                    RekallAgeMeshChangeKind.Positions,
                    [], [], [], [],
                    [], [], [], [],
                    [], [], [], [],
                    [],
                    zeroBounds),
                [],
                new RekallAgeMeshValidationReport(
                    true,
                    new RekallAgeMeshValidationSummary(0, 0, 0, 0, 0, 0, 0, zeroBounds),
                    []));
        }
    }
}
