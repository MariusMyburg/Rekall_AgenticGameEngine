using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.AssetPipeline;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
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
