using System.Text.Json.Nodes;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Mcp;
using Rekall.Age.Modeling.Commands;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Workflows;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Assets;

public sealed class ModelAssetCommandContractTests
{
    [Fact]
    public async Task DefaultRegistryCompletesStableModelAssetLifecycleAndMcpDiscoversEveryCommand()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = RekallAgeDefaultCommandRegistry.Create();
        var expectedCommands = new[]
        {
            "rekall.asset.model.publish",
            "rekall.asset.model.rebuild",
            "rekall.asset.model.inspect",
            "rekall.asset.model.list",
            "rekall.asset.model.freeze",
            "rekall.asset.model.unfreeze",
            "rekall.scene.instantiate_asset"
        };

        var registryNames = registry.Schemas.Select(schema => schema.Name).ToHashSet(StringComparer.Ordinal);
        var mcpNames = RekallAgeMcpCatalog.FromRegistry(registry).Tools
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(expectedCommands, command => Assert.Contains(command, registryNames));
        Assert.All(expectedCommands, command => Assert.Contains(command, mcpNames));

        var mesh = await Execute<CreateMeshAssetRequest, CreateMeshAssetResult>(
            registry,
            "rekall.mesh.create_asset",
            new(root, "hero-mesh", "Hero Mesh", Triangle()),
            "create editable mesh");
        Assert.True(mesh.Ok, mesh.Summary);
        var sourceRevision = Assert.IsType<RekallAgeMeshAssetSummary>(mesh.Value.Mesh).FileRevision;

        var published = await Execute<PublishModelAssetRequest, ModelAssetMutationCommandResult>(
            registry,
            "rekall.asset.model.publish",
            new(
                root,
                "hero-model",
                "Hero Model",
                new(RekallAgeModelSourceKind.Mesh, "hero-mesh"),
                RekallAgeDocumentRevision.Missing),
            "publish model asset");
        Assert.True(published.Ok, published.Summary);
        var publication = Assert.IsType<Rekall.Age.AssetPipeline.RekallAgePublishModelResult>(published.Value.Publication);
        Assert.Equal("hero-model", publication.Asset.AssetId);

        var frozen = await Execute<SetModelAssetFreezeRequest, ModelAssetFreezeCommandResult>(
            registry,
            "rekall.asset.model.freeze",
            new(root, "hero-model", publication.ModelFileRevision),
            "freeze model asset");
        Assert.True(frozen.Ok, frozen.Summary);
        Assert.Equal(RekallAgeModelBuildState.Frozen, frozen.Value.Asset!.BuildState);
        var unfrozen = await Execute<SetModelAssetFreezeRequest, ModelAssetFreezeCommandResult>(
            registry,
            "rekall.asset.model.unfreeze",
            new(root, "hero-model", frozen.Value.ModelFileRevision!),
            "unfreeze model asset");
        Assert.True(unfrozen.Ok, unfrozen.Summary);
        Assert.Equal(RekallAgeModelBuildState.Current, unfrozen.Value.Asset!.BuildState);
        publication = publication with { ModelFileRevision = unfrozen.Value.ModelFileRevision! };

        var listed = await Execute<ListModelAssetsRequest, ListModelAssetsResult>(
            registry,
            "rekall.asset.model.list",
            new(root),
            "list model assets");
        Assert.True(listed.Ok, listed.Summary);
        Assert.Equal(RekallAgeModelBuildState.Current, Assert.Single(listed.Value.Assets).BuildState);

        var createdScene = await Execute<CreateSceneRequest, CreateSceneResult>(
            registry,
            "rekall.scene.create",
            new(root, "Main", ["world", "rendering3d"]),
            "create scene");
        Assert.True(createdScene.Ok, createdScene.Summary);

        var placed = await Execute<InstantiateModelAssetRequest, InstantiateModelAssetResult>(
            registry,
            "rekall.scene.instantiate_asset",
            new(
                root,
                "Main",
                "hero-model",
                "Hero Instance",
                new(1, 2, 3),
                new(0, 30, 0),
                new(1, 1, 1)),
            "place model asset");
        Assert.True(placed.Ok, placed.Summary);
        Assert.Equal(RekallAgeModelBuildState.Current, placed.Value.BuildState);

        var attached = await Execute<AddComponentRequest, AddComponentResult>(
            registry,
            "rekall.component.add",
            new(
                root,
                "Main",
                placed.Value.EntityId,
                "Game.HeroState",
                new JsonObject { ["health"] = 125, ["role"] = "guardian" }),
            "attach gameplay component");
        Assert.True(attached.Ok, attached.Summary);

        var beforeRebuild = await Execute<InspectEntityRequest, InspectEntityResult>(
            registry,
            "rekall.entity.inspect",
            new(root, "Main", placed.Value.EntityId),
            "inspect placed entity");
        Assert.True(beforeRebuild.Ok, beforeRebuild.Summary);
        AssertStableReferenceAndGameplayData(beforeRebuild.Value.Entity);

        var mutated = await Execute<ApplyMeshOperationRequest, ApplyMeshOperationResult>(
            registry,
            "rekall.mesh.operation.apply",
            new(
                root,
                "hero-mesh",
                sourceRevision,
                new(
                    "transform",
                    RekallAgeGeometryDomain.Point,
                    [1],
                    new JsonObject { ["x"] = 0.25 })),
            "edit source mesh");
        Assert.True(mutated.Ok, mutated.Summary);

        var stale = await Execute<InspectModelAssetRequest, ModelAssetInspectionCommandResult>(
            registry,
            "rekall.asset.model.inspect",
            new(root, "hero-model"),
            "inspect stale model asset");
        Assert.True(stale.Ok, stale.Summary);
        Assert.Equal(RekallAgeModelBuildState.Stale, stale.Value.Inspection!.BuildState);

        var rebuilt = await Execute<RebuildModelAssetRequest, ModelAssetMutationCommandResult>(
            registry,
            "rekall.asset.model.rebuild",
            new(root, "hero-model", publication.ModelFileRevision),
            "rebuild model asset");
        Assert.True(rebuilt.Ok, rebuilt.Summary);
        Assert.Equal("hero-model", rebuilt.Value.Publication!.Asset.AssetId);
        Assert.NotEqual(publication.ModelFileRevision, rebuilt.Value.Publication.ModelFileRevision);

        var current = await Execute<InspectModelAssetRequest, ModelAssetInspectionCommandResult>(
            registry,
            "rekall.asset.model.inspect",
            new(root, "hero-model"),
            "inspect rebuilt model asset");
        Assert.True(current.Ok, current.Summary);
        Assert.Equal(RekallAgeModelBuildState.Current, current.Value.Inspection!.BuildState);

        var afterRebuild = await Execute<InspectEntityRequest, InspectEntityResult>(
            registry,
            "rekall.entity.inspect",
            new(root, "Main", placed.Value.EntityId),
            "inspect preserved entity");
        Assert.True(afterRebuild.Ok, afterRebuild.Summary);
        Assert.Equal(placed.Value.EntityId, afterRebuild.Value.Entity.Id);
        AssertStableReferenceAndGameplayData(afterRebuild.Value.Entity);
    }

    private static async ValueTask<RekallAgeCommandResult<TResult>> Execute<TRequest, TResult>(
        RekallAgeCommandRegistry registry,
        string command,
        TRequest request,
        string transactionName) =>
        await registry.ExecuteAsync<TRequest, TResult>(
            command,
            request,
            new RekallAgeCommandContext(
                "model-asset-contract",
                RekallAgeTransaction.Begin(transactionName),
                CancellationToken.None));

    private static void AssertStableReferenceAndGameplayData(Rekall.Age.World.RekallAgeEntityDocument entity)
    {
        var reference = Assert.Single(entity.Components, component => component.Type == "Rekall.ModelAssetReference");
        Assert.Equal("hero-model", reference.Properties["assetId"]!.GetValue<string>());
        var gameplay = Assert.Single(entity.Components, component => component.Type == "Game.HeroState");
        Assert.Equal(125, gameplay.Properties["health"]!.GetValue<int>());
        Assert.Equal("guardian", gameplay.Properties["role"]!.GetValue<string>());
    }

    private static RekallAgeMeshTopology Triangle() => new(
        PointIds: [1, 2, 3],
        Positions: [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
        EdgeIds: [11, 12, 13],
        EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0)],
        FaceIds: [21],
        FaceOffsets: [0, 3],
        CornerIds: [31, 32, 33],
        CornerPointIndices: [0, 1, 2],
        CornerEdgeIndices: [0, 1, 2]);
}
