using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class MeshAssetRenderingTests
{
    [Fact]
    public async Task RuntimeFrameResolvesEditableMeshReferenceThroughCompiledSnapshot()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeMeshAssetStore();
        await store.SaveAsync(root, Triangle(), CancellationToken.None);
        var revision = (await store.LoadVersionedAsync(root, "triangle", CancellationToken.None)).Revision;
        var entity = RekallAgeEntityDocument.Create("Authored Mesh", ["geometry"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshAssetReference",
                new JsonObject { ["assetId"] = "triangle", ["expectedRevision"] = revision }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject { ["mesh"] = "triangle" }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(entity);

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene, root);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, false);

        var renderable = Assert.Single(frame.Renderables);
        Assert.Equal("triangle", renderable.AssetId);
        var geometry = Assert.IsType<RekallAgeRuntimeViewportGeometryMesh>(renderable.GeometryMesh);
        Assert.Equal(3, geometry.Vertices.Count);
        Assert.Equal([0U, 1U, 2U], geometry.Indices);
        var provenance = Assert.Single(geometry.TriangleProvenance!);
        Assert.Equal(21UL, provenance.SourceFaceId);
        Assert.Equal([31UL, 32UL, 33UL], provenance.SourceCornerIds);
        Assert.Equal([1UL, 2UL, 3UL], provenance.SourcePointIds);
        var surface = Assert.Single(geometry.Surfaces!);
        Assert.Equal(3, surface.IndexCount);
        Assert.Equal([21UL], surface.SourceFaceIds);
    }

    [Fact]
    public void MeshAssetReferenceIsARegisteredGenericComponent()
    {
        Assert.True(RekallAgeBuiltInComponentTypeCatalog.IsKnown("Rekall.MeshAssetReference"));
        var modules = RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly);
        var builtIns = Assert.Single(modules.Modules, module => module.Id == "rekall.builtins");
        var schema = Assert.Single(builtIns.Components, component => component.DisplayName == "Mesh Asset Reference");
        Assert.Contains(schema.Properties, property => property.Name == "AssetId" && property.Kind == "assetRef");
        Assert.Contains(schema.Properties, property => property.Name == "ExpectedRevision");
    }

    [Fact]
    public void MissingMeshAssetProducesStructuredViewportEvidence()
    {
        var root = TestPaths.CreateTempDirectory();
        var entity = RekallAgeEntityDocument.Create("Missing Mesh", ["geometry"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshAssetReference",
                new JsonObject { ["assetId"] = "not-present" }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject { ["mesh"] = "not-present" }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(entity);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            new RekallAgeRuntimeWorldBuilder().Build(scene, root),
            320,
            180,
            false);

        var issue = Assert.Single(frame.Observations, observation =>
            observation.Code == "REKALL_MESH_ASSET_NOT_FOUND");
        Assert.Equal("rendering", issue.Subsystem);
        Assert.Equal("Missing Mesh", issue.Target);
    }

    private static RekallAgeMeshAsset Triangle() => RekallAgeMeshAsset.Create(
        "triangle",
        "Triangle",
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
}
