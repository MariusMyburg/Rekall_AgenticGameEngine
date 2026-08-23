using System.Text.Json.Nodes;
using Rekall.Age.AssetPipeline;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Modeling;
using Rekall.Age.World;

namespace Rekall.Age.Tests.LevelDesign;

public sealed class ModelAssetPlacementTests
{
    [Fact]
    public async Task CurrentModelAssetPlacementRetainsExactTransformParentAndStableReference()
    {
        var fixture = await CreatePublishedFixtureAsync();
        var parent = RekallAgeEntityDocument.Create("Display Root", ["display"]);
        await fixture.SceneStore.SaveAsync(
            fixture.Root,
            (await fixture.SceneStore.LoadAsync(fixture.Root, "Main", default)).AddEntity(parent),
            default);
        var context = Context("place current model");
        var registry = Registry();

        var result = await registry.ExecuteAsync<InstantiateModelAssetRequest, InstantiateModelAssetResult>(
            "rekall.scene.instantiate_asset",
            new(
                fixture.Root,
                "Main",
                "hero-model",
                "Hero Instance",
                new(1.25, -2.5, 3.75),
                new(-15, 45, 90),
                new(0.5, 2, -3),
                parent.Id),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Value.Warnings);
        Assert.Equal(RekallAgeModelBuildState.Current, result.Value.BuildState);
        Assert.Equal("Assets/Models/Compiled/hero-model.age.compiled-mesh.json", result.Value.CompiledMeshPath);
        var scene = Assert.IsType<RekallAgeSceneDocument>(result.Value.Scene);
        var entity = scene.GetRequiredEntity(result.Value.EntityId);
        Assert.Equal("Hero Instance", entity.Name);
        Assert.Equal(parent.Id, entity.ParentId);
        Assert.True(entity.Visible);
        Assert.False(entity.Locked);
        Assert.Equal(3, entity.Components.Count);

        var transform = Assert.Single(entity.Components, component => component.Type == "Rekall.Transform3D");
        Assert.Equal(1.25, transform.Properties["x"]!.GetValue<double>());
        Assert.Equal(-2.5, transform.Properties["y"]!.GetValue<double>());
        Assert.Equal(3.75, transform.Properties["z"]!.GetValue<double>());
        Assert.Equal(-15, transform.Properties["pitch"]!.GetValue<double>());
        Assert.Equal(45, transform.Properties["yaw"]!.GetValue<double>());
        Assert.Equal(90, transform.Properties["roll"]!.GetValue<double>());
        Assert.Equal(0.5, transform.Properties["scaleX"]!.GetValue<double>());
        Assert.Equal(2, transform.Properties["scaleY"]!.GetValue<double>());
        Assert.Equal(-3, transform.Properties["scaleZ"]!.GetValue<double>());

        var reference = Assert.Single(entity.Components, component => component.Type == "Rekall.ModelAssetReference");
        Assert.Equal("hero-model", reference.Properties["assetId"]!.GetValue<string>());
        Assert.Single(reference.Properties);
        var renderer = Assert.Single(entity.Components, component => component.Type == "Rekall.MeshRenderer");
        Assert.Empty(renderer.Properties);
        Assert.DoesNotContain(entity.Components, component => component.Type == "Rekall.GeometryMesh");
        Assert.DoesNotContain("positions", fixture.SceneStore.Serialize(scene), StringComparison.OrdinalIgnoreCase);
        Assert.True(RekallAgeBuiltInComponentTypeCatalog.IsKnown("Rekall.ModelAssetReference"));
    }

    [Fact]
    public async Task PlacementCapturesExactScenePreimageAndSupportsTransactionUndo()
    {
        var fixture = await CreatePublishedFixtureAsync();
        var scenePath = fixture.SceneStore.GetScenePath(fixture.Root, "Main");
        var before = await File.ReadAllBytesAsync(scenePath);
        var context = Context("undoable model placement");

        var placed = await new InstantiateModelAssetCommand().ExecuteAsync(
            Request(fixture.Root),
            context);

        Assert.True(placed.Ok, placed.Summary);
        Assert.Equal([scenePath], context.Transaction.ChangedResources);
        var preimage = Assert.Single(context.Transaction.ResourcePreimages);
        Assert.Equal(scenePath, preimage.Resource);
        Assert.True(preimage.ExistedBefore);
        Assert.Equal(before, preimage.Content);
        Assert.NotEqual(before, await File.ReadAllBytesAsync(scenePath));

        var history = new RekallAgeTransactionLogStore();
        await history.AppendAsync(fixture.Root, context.Transaction, context.Actor, default);
        var undo = await new RestoreTransactionPreimageCommand(history).ExecuteAsync(
            new(fixture.Root, context.Transaction.Id, "Scenes/Main.age.scene.json"),
            Context("undo placement"));

        Assert.True(undo.Ok, undo.Summary);
        Assert.Equal(before, await File.ReadAllBytesAsync(scenePath));
        Assert.Empty((await fixture.SceneStore.LoadAsync(fixture.Root, "Main", default)).Entities);
    }

    [Fact]
    public async Task StaleModelAssetUsesLastSuccessfulOutputWithBoundedWarning()
    {
        var fixture = await CreatePublishedFixtureAsync();
        await ReplaceMeshAsync(fixture, "sphere");

        var result = await new InstantiateModelAssetCommand().ExecuteAsync(
            Request(fixture.Root),
            Context("place stale model"));

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(RekallAgeModelBuildState.Stale, result.Value.BuildState);
        Assert.Equal("Assets/Models/Compiled/hero-model.age.compiled-mesh.json", result.Value.CompiledMeshPath);
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Equal("REKALL_MODEL_SOURCE_STALE", warning.Code);
        Assert.Equal("Warning", warning.Severity);
        Assert.InRange(result.Value.Warnings.Count, 1, InstantiateModelAssetCommand.MaximumWarnings);
        var entity = Assert.IsType<RekallAgeSceneDocument>(result.Value.Scene).GetRequiredEntity(result.Value.EntityId);
        Assert.Equal(
            "hero-model",
            entity.Components.Single(component => component.Type == "Rekall.ModelAssetReference")
                .Properties["assetId"]!.GetValue<string>());
    }

    [Fact]
    public async Task FrozenModelAssetWithSuccessfulOutputRemainsPlaceable()
    {
        var fixture = await CreatePublishedFixtureAsync();
        var loaded = await fixture.ModelStore.LoadVersionedAsync(fixture.Root, "hero-model", default);
        await fixture.ModelStore.SaveIfRevisionAsync(
            fixture.Root,
            loaded.Value with
            {
                Revision = loaded.Value.Revision + 1,
                BuildState = RekallAgeModelBuildState.Frozen,
                Frozen = true
            },
            loaded.Revision,
            default);

        var result = await new InstantiateModelAssetCommand().ExecuteAsync(
            Request(fixture.Root),
            Context("place frozen model"));

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(RekallAgeModelBuildState.Frozen, result.Value.BuildState);
        Assert.Empty(result.Value.Warnings);
        Assert.NotNull(result.Value.Scene);
    }

    [Fact]
    public async Task MissingModelAssetFailsWithoutSceneOrTransactionMutation()
    {
        var fixture = await CreatePublishedFixtureAsync();
        await AssertFailureWithoutMutationAsync(
            fixture,
            Request(fixture.Root) with { ModelAssetId = "missing-model" },
            "REKALL_MODEL_ASSET_MISSING");
    }

    [Fact]
    public async Task UnbuiltModelAssetFailsWithoutSceneOrTransactionMutation()
    {
        var fixture = await CreatePublishedFixtureAsync();
        await fixture.ModelStore.SaveAsync(
            fixture.Root,
            new(
                RekallAgeModelAssetDocument.CurrentSchemaVersion,
                "unbuilt-model",
                "Unbuilt",
                1,
                new(RekallAgeModelSourceKind.Mesh, "hero-mesh"),
                RekallAgeModelBuildState.Failed,
                null,
                Frozen: false),
            default);

        await AssertFailureWithoutMutationAsync(
            fixture,
            Request(fixture.Root) with { ModelAssetId = "unbuilt-model" },
            "REKALL_MODEL_NOT_PLACEABLE");
    }

    [Fact]
    public async Task PhysicallyMissingCompiledOutputFailsWithoutSceneOrTransactionMutation()
    {
        var fixture = await CreatePublishedFixtureAsync();
        File.Delete(Path.Combine(fixture.Root, fixture.Publication.Asset.LastSuccessfulBuild!.CompiledMeshPath));

        await AssertFailureWithoutMutationAsync(
            fixture,
            Request(fixture.Root),
            "REKALL_MODEL_OUTPUT_MISSING");
    }

    [Fact]
    public async Task FailedModelAssetWithRetainedOutputFailsWithoutSceneOrTransactionMutation()
    {
        var fixture = await CreatePublishedFixtureAsync();
        File.Delete(fixture.MeshStore.GetMeshPath(fixture.Root, "hero-mesh"));
        Assert.True(File.Exists(fixture.Publication.CompiledOutputPath));

        await AssertFailureWithoutMutationAsync(
            fixture,
            Request(fixture.Root),
            "REKALL_MODEL_SOURCE_MISSING");
    }

    [Fact]
    public async Task InvalidParentFailsWithoutSceneOrTransactionMutation()
    {
        var fixture = await CreatePublishedFixtureAsync();

        await AssertFailureWithoutMutationAsync(
            fixture,
            Request(fixture.Root) with { ParentEntityId = "ent_missing" },
            "REKALL_MODEL_PARENT_MISSING");
    }

    [Theory]
    [MemberData(nameof(InvalidTransforms))]
    public async Task InvalidTransformFailsWithoutSceneOrTransactionMutation(
        RekallAgePlacementVector3 position,
        RekallAgePlacementVector3 rotation,
        RekallAgePlacementVector3 scale)
    {
        var fixture = await CreatePublishedFixtureAsync();

        await AssertFailureWithoutMutationAsync(
            fixture,
            Request(fixture.Root) with
            {
                Position = position,
                RotationDegrees = rotation,
                Scale = scale
            },
            "REKALL_MODEL_PLACEMENT_TRANSFORM_INVALID");
    }

    [Fact]
    public async Task ExplicitStaleSceneRevisionFailsWithoutSceneOrTransactionMutation()
    {
        var fixture = await CreatePublishedFixtureAsync();

        await AssertFailureWithoutMutationAsync(
            fixture,
            Request(fixture.Root) with { ExpectedSceneRevision = new string('0', 64) },
            "REKALL_DOCUMENT_REVISION_CONFLICT");
    }

    public static IEnumerable<object[]> InvalidTransforms()
    {
        yield return
        [
            new RekallAgePlacementVector3(double.NaN, 0, 0),
            new RekallAgePlacementVector3(0, 0, 0),
            new RekallAgePlacementVector3(1, 1, 1)
        ];
        yield return
        [
            new RekallAgePlacementVector3(0, 0, 0),
            new RekallAgePlacementVector3(double.PositiveInfinity, 0, 0),
            new RekallAgePlacementVector3(1, 1, 1)
        ];
        yield return
        [
            new RekallAgePlacementVector3(0, 0, 0),
            new RekallAgePlacementVector3(0, 0, 0),
            new RekallAgePlacementVector3(0, 1, 1)
        ];
        yield return
        [
            new RekallAgePlacementVector3(0, 0, 0),
            new RekallAgePlacementVector3(0, 0, 0),
            new RekallAgePlacementVector3(1_000_001, 1, 1)
        ];
    }

    private static InstantiateModelAssetRequest Request(string root) =>
        new(
            root,
            "Main",
            "hero-model",
            null,
            new(0, 0, 0),
            new(0, 0, 0),
            new(1, 1, 1));

    private static RekallAgeCommandRegistry Registry()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new InstantiateModelAssetCommand());
        return registry;
    }

    private static RekallAgeCommandContext Context(string transactionName) =>
        new("test", RekallAgeTransaction.Begin(transactionName), default);

    private static async Task AssertFailureWithoutMutationAsync(
        Fixture fixture,
        InstantiateModelAssetRequest request,
        string expectedCode)
    {
        var scenePath = fixture.SceneStore.GetScenePath(fixture.Root, "Main");
        var before = await File.ReadAllBytesAsync(scenePath);
        var context = Context("rejected model placement");

        var result = await new InstantiateModelAssetCommand().ExecuteAsync(request, context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == expectedCode);
        Assert.Null(result.Value.Scene);
        Assert.Equal(before, await File.ReadAllBytesAsync(scenePath));
        Assert.Empty(context.Transaction.ChangedResources);
        Assert.Empty(context.Transaction.ResourcePreimages);
    }

    private static async ValueTask<Fixture> CreatePublishedFixtureAsync()
    {
        var fixture = new Fixture(TestPaths.CreateTempDirectory());
        await fixture.SceneStore.SaveAsync(
            fixture.Root,
            RekallAgeSceneDocument.Create("Main", ["world"]),
            default);
        var mesh = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            "box",
            "hero-mesh",
            "Hero Mesh",
            default);
        await fixture.MeshStore.SaveAsync(fixture.Root, mesh, default);
        fixture.Publication = await fixture.PublishingService.PublishAsync(
            fixture.Root,
            new(
                "hero-model",
                "Hero Model",
                new(RekallAgeModelSourceKind.Mesh, "hero-mesh"),
                RekallAgeDocumentRevision.Missing),
            RekallAgeTransaction.Begin("publish model"),
            default);
        return fixture;
    }

    private static async ValueTask ReplaceMeshAsync(Fixture fixture, string primitive)
    {
        var current = await fixture.MeshStore.LoadVersionedAsync(fixture.Root, "hero-mesh", default);
        var replacement = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            primitive,
            "hero-mesh",
            "Hero Mesh",
            default);
        await fixture.MeshStore.SaveIfRevisionAsync(
            fixture.Root,
            replacement with { Revision = current.Value.Revision + 1 },
            current.Revision,
            default);
    }

    private sealed record Fixture(string Root)
    {
        public RekallAgeSceneStore SceneStore { get; } = new();

        public RekallAgeMeshAssetStore MeshStore { get; } = new();

        public RekallAgeModelAssetStore ModelStore { get; } = new();

        public RekallAgeModelPublishingService PublishingService { get; } = new();

        public RekallAgePublishModelResult Publication { get; set; } = null!;

    }
}
