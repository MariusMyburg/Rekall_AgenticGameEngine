using System.Text.Json;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshTopologyValidationTests
{
    [Fact]
    public void ValidatesNgonLooseEdgeCornerUvAndStableRoundTrip()
    {
        var mesh = CreateQuadWithLooseEdge();

        var report = new RekallAgeMeshValidator().Validate(mesh);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        Assert.Equal(5, report.Summary.PointCount);
        Assert.Equal(5, report.Summary.EdgeCount);
        Assert.Equal(1, report.Summary.FaceCount);
        Assert.Equal(4, report.Summary.CornerCount);
        Assert.Equal(1, report.Summary.LooseEdgeCount);
        Assert.Equal(4, report.Summary.BoundaryEdgeCount);
        Assert.Equal(0, report.Summary.NonManifoldEdgeCount);
        Assert.Equal(new RekallAgeGeometryVector3(0, 0, 0), report.Summary.Bounds.Min);
        Assert.Equal(new RekallAgeGeometryVector3(2, 2, 0), report.Summary.Bounds.Max);

        var json = JsonSerializer.Serialize(mesh, RekallAgeModelingJson.Options);
        var restored = JsonSerializer.Deserialize<RekallAgeMeshAsset>(json, RekallAgeModelingJson.Options);

        Assert.NotNull(restored);
        Assert.Equal(mesh.AssetId, restored.AssetId);
        Assert.Equal(mesh.Topology.PointIds, restored.Topology.PointIds);
        var uv = Assert.Single(restored.Attributes, item => item.Name == "uv.main");
        Assert.Equal(RekallAgeGeometryDomain.Corner, uv.Domain);
        Assert.Equal(RekallAgeGeometryValueType.Float2, uv.ValueType);
        Assert.Equal(4, uv.Values.Count);
    }

    [Fact]
    public void ReportsInvalidReferencesDuplicateEdgesAndDegenerateFaces()
    {
        var mesh = CreateQuadWithLooseEdge() with
        {
            Topology = CreateQuadWithLooseEdge().Topology with
            {
                EdgeIds = [21, 22, 23, 24, 25, 26],
                EdgePointIndices = [
                    new(0, 1), new(1, 2), new(2, 3), new(3, 0), new(0, 4), new(1, 0)
                ],
                FaceOffsets = [0, 3],
                CornerIds = [31, 32, 33],
                CornerPointIndices = [0, 1, 1],
                CornerEdgeIndices = [0, 1, 99]
            },
            Attributes = []
        };

        var report = new RekallAgeMeshValidator().Validate(mesh);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_MESH_EDGE_DUPLICATE");
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_MESH_CORNER_EDGE_REFERENCE_INVALID");
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_MESH_FACE_POINT_REPEATED");
        Assert.Contains(report.Diagnostics, item => item.ElementIds.Contains(26UL));
    }

    [Fact]
    public void ReportsNonManifoldEdgesWithoutRejectingStructurallyValidMesh()
    {
        var mesh = RekallAgeMeshAsset.Create(
            "non-manifold",
            "Three Faces On One Edge",
            new RekallAgeMeshTopology(
                PointIds: [1, 2, 3, 4, 5],
                Positions: [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1)],
                EdgeIds: [11, 12, 13, 14, 15, 16, 17],
                EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0), new(1, 3), new(3, 0), new(1, 4), new(4, 0)],
                FaceIds: [21, 22, 23],
                FaceOffsets: [0, 3, 6, 9],
                CornerIds: [31, 32, 33, 34, 35, 36, 37, 38, 39],
                CornerPointIndices: [0, 1, 2, 1, 0, 3, 0, 1, 4],
                CornerEdgeIndices: [0, 1, 2, 0, 4, 3, 0, 5, 6]));

        var report = new RekallAgeMeshValidator().Validate(mesh);

        Assert.True(report.IsValid);
        Assert.Equal(1, report.Summary.NonManifoldEdgeCount);
        Assert.Contains(report.Diagnostics, item =>
            item.Code == "REKALL_MESH_EDGE_NON_MANIFOLD"
            && item.Severity == RekallAgeMeshDiagnosticSeverity.Warning
            && item.ElementIds.Contains(11UL));
    }

    [Fact]
    public void RejectsAttributeLengthAndNonFinitePosition()
    {
        var mesh = CreateQuadWithLooseEdge() with
        {
            Topology = CreateQuadWithLooseEdge().Topology with
            {
                Positions = [new(double.NaN, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(2, 2, 0)]
            },
            Attributes =
            [
                new RekallAgeGeometryAttribute(
                    "weight",
                    RekallAgeGeometryDomain.Point,
                    RekallAgeGeometryValueType.Float,
                    [JsonSerializer.SerializeToElement(1.0)])
            ]
        };

        var report = new RekallAgeMeshValidator().Validate(mesh);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_MESH_POSITION_NONFINITE");
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_MESH_ATTRIBUTE_LENGTH_INVALID");
    }

    [Fact]
    public void ReportsDuplicateFacesAndZeroAreaFaces()
    {
        var mesh = RekallAgeMeshAsset.Create(
            "duplicate-face",
            "Duplicate Face",
            new RekallAgeMeshTopology(
                PointIds: [1, 2, 3],
                Positions: [new(0, 0, 0), new(1, 0, 0), new(2, 0, 0)],
                EdgeIds: [11, 12, 13],
                EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0)],
                FaceIds: [21, 22],
                FaceOffsets: [0, 3, 6],
                CornerIds: [31, 32, 33, 34, 35, 36],
                CornerPointIndices: [0, 1, 2, 2, 1, 0],
                CornerEdgeIndices: [0, 1, 2, 1, 0, 2]));

        var report = new RekallAgeMeshValidator().Validate(mesh);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_MESH_FACE_DUPLICATE");
        Assert.Equal(2, report.Diagnostics.Count(item => item.Code == "REKALL_MESH_FACE_ZERO_AREA"));
    }

    [Fact]
    public void SelectionUsesStableIdsAndMaterialIndicesAreRangeChecked()
    {
        var mesh = CreateQuadWithLooseEdge() with
        {
            Attributes =
            [
                new RekallAgeGeometryAttribute(
                    "material.index",
                    RekallAgeGeometryDomain.Face,
                    RekallAgeGeometryValueType.Int32,
                    [JsonSerializer.SerializeToElement(4)],
                    Semantic: "material-index")
            ]
        };
        var selection = new RekallAgeMeshSelection(
            "top",
            RekallAgeGeometryDomain.Face,
            [41],
            ActiveElementId: 41,
            OrderedHistory: [41]);

        var report = new RekallAgeMeshValidator().Validate(mesh);
        var json = JsonSerializer.Serialize(selection, RekallAgeModelingJson.Options);
        var restored = JsonSerializer.Deserialize<RekallAgeMeshSelection>(json, RekallAgeModelingJson.Options);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, item => item.Code == "REKALL_MESH_MATERIAL_INDEX_INVALID");
        Assert.NotNull(restored);
        Assert.Equal(41UL, restored.ActiveElementId);
        Assert.Equal([41UL], restored.ElementIds);
    }

    [Fact]
    public void RejectsSelectionReferencesOutsideItsStableIdDomain()
    {
        var mesh = CreateQuadWithLooseEdge() with
        {
            SelectionSets =
            [
                new RekallAgeMeshSelection(
                    "invalid-face-selection",
                    RekallAgeGeometryDomain.Face,
                    [41, 999],
                    ActiveElementId: 999,
                    OrderedHistory: [41, 999])
            ]
        };

        var report = new RekallAgeMeshValidator().Validate(mesh);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, item =>
            item.Code == "REKALL_MESH_SELECTION_ELEMENT_INVALID"
            && item.ElementIds.Contains(999UL));
    }

    private static RekallAgeMeshAsset CreateQuadWithLooseEdge()
    {
        return RekallAgeMeshAsset.Create(
            "quad-loose",
            "Quad With Loose Edge",
            new RekallAgeMeshTopology(
                PointIds: [1, 2, 3, 4, 5],
                Positions: [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(2, 2, 0)],
                EdgeIds: [21, 22, 23, 24, 25],
                EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0), new(0, 4)],
                FaceIds: [41],
                FaceOffsets: [0, 4],
                CornerIds: [61, 62, 63, 64],
                CornerPointIndices: [0, 1, 2, 3],
                CornerEdgeIndices: [0, 1, 2, 3]),
            attributes:
            [
                new RekallAgeGeometryAttribute(
                    "uv.main",
                    RekallAgeGeometryDomain.Corner,
                    RekallAgeGeometryValueType.Float2,
                    [
                        JsonSerializer.SerializeToElement(new[] { 0.0, 0.0 }),
                        JsonSerializer.SerializeToElement(new[] { 1.0, 0.0 }),
                        JsonSerializer.SerializeToElement(new[] { 1.0, 1.0 }),
                        JsonSerializer.SerializeToElement(new[] { 0.0, 1.0 })
                    ],
                    Semantic: "texcoord")
            ],
            materialSlots: [new("default", null)]);
    }
}
