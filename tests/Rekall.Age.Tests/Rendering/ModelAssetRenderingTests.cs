using System.Text.Json.Nodes;
using System.Text.Json;
using Rekall.Age.Assets;
using Rekall.Age.AssetPipeline;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

/// <summary>
/// Before this fix, an entity placed by <c>rekall.scene.instantiate_asset</c> -- Transform3D,
/// Rekall.ModelAssetReference, and a bare Rekall.MeshRenderer with no "mesh" property, exactly what
/// InstantiateModelAssetCommand actually produces -- had nothing in the render pipeline that knew to
/// resolve Rekall.ModelAssetReference at all, and rendered as an unresolved fallback shape. Confirmed
/// empirically before fixing it: publishing a real box Model Asset, placing it, and capturing a real
/// frame reported Asset-backed: 0, Fallback: 1. These tests mirror MeshAssetRenderingTests' coverage
/// of the older Rekall.MeshAssetReference path for the newer published Model Asset path.
/// </summary>
public sealed class ModelAssetRenderingTests
{
    [Fact]
    public async Task RuntimeFramePreservesCompiledProceduralSkinBindings()
    {
        var root = TestPaths.CreateTempDirectory();
        var mesh = await new RekallAgeMeshPrimitiveFactory().CreateAsync("box", "weighted-mesh", "Weighted Mesh", default);
        var pointCount = mesh.Topology.PointIds.Count;
        mesh = mesh with
        {
            Attributes = mesh.Attributes.Concat(
            [
                new RekallAgeGeometryAttribute(
                    "skin.joints", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Int4,
                    Enumerable.Range(0, pointCount).Select(_ => JsonSerializer.SerializeToElement(new[] { 0, 1, 0, 0 }, RekallAgeModelingJson.Options)).ToArray(),
                    "joint-indices-0", RekallAgeGeometryInterpolation.Nearest),
                new RekallAgeGeometryAttribute(
                    "skin.weights", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float4,
                    Enumerable.Range(0, pointCount).Select(_ => JsonSerializer.SerializeToElement(new[] { 0.25, 0.75, 0d, 0d }, RekallAgeModelingJson.Options)).ToArray(),
                    "joint-weights-0", RekallAgeGeometryInterpolation.NormalizedLinear)
            ]).ToArray()
        };
        await new RekallAgeMeshAssetStore().SaveAsync(root, mesh, default);
        await new RekallAgeModelPublishingService().PublishAsync(
            root,
            new("weighted-model", "Weighted Model", new(RekallAgeModelSourceKind.Mesh, mesh.AssetId), RekallAgeDocumentRevision.Missing),
            RekallAgeTransaction.Begin("publish weighted model"),
            default);
        var entity = RekallAgeEntityDocument.Create("Weighted Instance", ["model-asset"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.ModelAssetReference", new JsonObject { ["assetId"] = "weighted-model" }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"));

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            new RekallAgeRuntimeWorldBuilder().Build(RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity), root),
            320, 180, false);

        var geometry = Assert.Single(frame.Renderables).GeometryMesh;
        var property = geometry!.GetType().GetProperty("SkinBindings");
        Assert.NotNull(property);
        var bindings = Assert.IsAssignableFrom<System.Collections.IEnumerable>(property.GetValue(geometry)).Cast<object>().ToArray();
        Assert.Equal(geometry.Vertices.Count, bindings.Length);
        var first = Assert.IsType<JsonObject>(JsonSerializer.SerializeToNode(bindings[0], RekallAgeModelingJson.Options));
        Assert.Equal(1, first["joint1"]!.GetValue<int>());
        Assert.Equal(0.75, first["weight1"]!.GetValue<double>());
    }

    [Fact]
    public async Task RuntimeFrameResolvesPlacedModelAssetThroughCompiledSnapshot()
    {
        var root = TestPaths.CreateTempDirectory();
        await PublishBoxModelAssetAsync(root, "hero-model");
        var entity = RekallAgeEntityDocument.Create("Hero Instance", ["model-asset"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.ModelAssetReference",
                new JsonObject { ["assetId"] = "hero-model" }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(entity);

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene, root);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, false);

        var renderable = Assert.Single(frame.Renderables);
        Assert.Empty(frame.Observations);
        var geometry = Assert.IsType<RekallAgeRuntimeViewportGeometryMesh>(renderable.GeometryMesh);
        // A box primitive from RekallAgeMeshPrimitiveFactory has 24 vertices (6 faces * 4 corners
        // each, unshared per-face for correct flat-shaded normals), not the topologically-minimal 8.
        Assert.Equal(24, geometry.Vertices.Count);
        Assert.NotEmpty(geometry.Indices);
    }

    [Fact]
    public async Task RuntimeFrameRebuildsFromEditableMeshWhenPublishedOutputIsMissing()
    {
        var root = TestPaths.CreateTempDirectory();
        await PublishBoxModelAssetAsync(root, "recoverable-model");
        var model = await new RekallAgeModelAssetStore().LoadVersionedAsync(root, "recoverable-model", default);
        File.Delete(Path.Combine(root, model.Value.LastSuccessfulBuild!.CompiledMeshPath));
        var entity = RekallAgeEntityDocument.Create("Recoverable Instance", ["model-asset"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.ModelAssetReference",
                new JsonObject { ["assetId"] = "recoverable-model" }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"));

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(entity),
                root),
            320,
            180,
            false);

        Assert.Empty(frame.Observations);
        var geometry = Assert.IsType<RekallAgeRuntimeViewportGeometryMesh>(Assert.Single(frame.Renderables).GeometryMesh);
        Assert.Equal(24, geometry.Vertices.Count);
    }

    [Fact]
    public async Task RebuiltFramesReuseUnchangedCompiledModelGeometry()
    {
        var root = TestPaths.CreateTempDirectory();
        await PublishBoxModelAssetAsync(root, "stable-model");
        var entity = RekallAgeEntityDocument.Create("Stable Instance", ["model-asset"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.ModelAssetReference",
                new JsonObject { ["assetId"] = "stable-model" }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(entity),
            root);
        var builder = new RekallAgeRuntimeRenderFrameBuilder();

        var first = Assert.Single(builder.Build(world, 320, 180, false).Renderables).GeometryMesh;
        var second = Assert.Single(builder.Build(world, 320, 180, false).Renderables).GeometryMesh;

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task MissingModelAssetProducesStructuredViewportEvidenceInsteadOfSilentFallback()
    {
        var root = TestPaths.CreateTempDirectory();
        var entity = RekallAgeEntityDocument.Create("Missing Model", ["model-asset"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.ModelAssetReference",
                new JsonObject { ["assetId"] = "not-present" }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(entity);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            new RekallAgeRuntimeWorldBuilder().Build(scene, root),
            320,
            180,
            false);

        var issue = Assert.Single(frame.Observations, observation =>
            observation.Code == "REKALL_MODEL_ASSET_NOT_FOUND");
        Assert.Equal("rendering", issue.Subsystem);
        Assert.Equal("Missing Model", issue.Target);
    }

    internal static async Task PublishBoxModelAssetAsync(string root, string modelAssetId)
    {
        var meshAssetId = modelAssetId + "-mesh";
        var mesh = await new RekallAgeMeshPrimitiveFactory().CreateAsync("box", meshAssetId, "Box Mesh", default);
        await new RekallAgeMeshAssetStore().SaveAsync(root, mesh, default);
        await new RekallAgeModelPublishingService().PublishAsync(
            root,
            new(
                modelAssetId,
                "Box Model",
                new(RekallAgeModelSourceKind.Mesh, meshAssetId),
                RekallAgeDocumentRevision.Missing),
            RekallAgeTransaction.Begin("publish box model"),
            default);
    }
}
