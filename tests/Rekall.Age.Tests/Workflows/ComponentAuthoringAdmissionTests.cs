using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Workflows;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Workflows;

public sealed class ComponentAuthoringAdmissionTests
{
    [Fact]
    public async Task DefaultRegistryRejectsUnknownBuiltInPropertyOnAddWithoutMutation()
    {
        var (root, entity) = await CreateSceneAsync();
        var registry = RekallAgeDefaultCommandRegistry.Create();
        var context = Context("invalid add");

        var result = await registry.ExecuteAsync<AddComponentRequest, AddComponentResult>(
            "rekall.component.add",
            new AddComponentRequest(
                root,
                "Main",
                entity.Id,
                "Rekall.MeshRenderer",
                new JsonObject { ["MeshAssetId"] = "geo:cube" }),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_COMPONENT_PROPERTY_UNKNOWN");
        Assert.Empty((await LoadAsync(root)).Entities.Single().Components);
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task DefaultRegistryRejectsUnknownBuiltInPropertyOnSetWithoutMutation()
    {
        var (root, entity) = await CreateSceneAsync(
            RekallAgeComponentDocument.Create("Rekall.MeshRenderer", new JsonObject { ["Mesh"] = "cube" }));
        var registry = RekallAgeDefaultCommandRegistry.Create();
        var context = Context("invalid set");

        var result = await registry.ExecuteAsync<SetComponentPropertyRequest, SetComponentPropertyResult>(
            "rekall.component.set_property",
            new SetComponentPropertyRequest(
                root, "Main", entity.Id, "Rekall.MeshRenderer", "MeshAssetId", JsonValue.Create("geo:cube")),
            context);

        Assert.False(result.Ok);
        var component = Assert.Single((await LoadAsync(root)).Entities.Single().Components);
        Assert.False(component.Properties.ContainsKey("MeshAssetId"));
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task DefaultRegistryRejectsInvalidBlueprintPropertiesAtIndexedTargets()
    {
        var (root, _) = await CreateSceneAsync();
        var registry = RekallAgeDefaultCommandRegistry.Create();
        var context = Context("invalid blueprint");
        var result = await registry.ExecuteAsync<ApplySceneBlueprintRequest, ApplySceneBlueprintResult>(
            "rekall.scene.apply_blueprint",
            new ApplySceneBlueprintRequest(
                root,
                "Main",
                [
                    new RekallAgeSceneBlueprintEntity(
                        "Floor",
                        Components:
                        [
                            new RekallAgeSceneBlueprintComponent(
                                "Rekall.GeometryPrimitive",
                                new JsonObject { ["PrimitiveType"] = "Cube", ["ScaleX"] = 4 })
                        ])
                ]),
            context);

        Assert.False(result.Ok);
        Assert.All(result.Errors, error =>
            Assert.StartsWith("Main.entities[0].components[0].properties.", error.Target, StringComparison.Ordinal));
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task DefaultRegistryAcceptsValidBuiltInAndArbitraryAgentProperties()
    {
        var (root, entity) = await CreateSceneAsync();
        var registry = RekallAgeDefaultCommandRegistry.Create();

        var builtIn = await registry.ExecuteAsync<AddComponentRequest, AddComponentResult>(
            "rekall.component.add",
            new AddComponentRequest(
                root,
                "Main",
                entity.Id,
                "Rekall.MeshRenderer",
                new JsonObject { ["Mesh"] = "cube", ["Active"] = true }),
            Context("valid built-in"));
        var agent = await registry.ExecuteAsync<AddComponentRequest, AddComponentResult>(
            "rekall.component.add",
            new AddComponentRequest(
                root,
                "Main",
                entity.Id,
                "Game.Rules.State",
                new JsonObject { ["Anything"] = new JsonArray(1, 2, 3) }),
            Context("valid agent"));

        Assert.True(builtIn.Ok, builtIn.Summary);
        Assert.True(agent.Ok, agent.Summary);
    }

    [Fact]
    public async Task DefaultRegistryRejectsStructuredAndRangeViolationsBeforeMutation()
    {
        var (root, entity) = await CreateSceneAsync();
        var registry = RekallAgeDefaultCommandRegistry.Create();

        var structured = await registry.ExecuteAsync<AddComponentRequest, AddComponentResult>(
            "rekall.component.add",
            new AddComponentRequest(
                root,
                "Main",
                entity.Id,
                "Rekall.InputActionMap",
                new JsonObject { ["Actions"] = "[{\"name\":\"move\"}]" }),
            Context("invalid structure"));
        var range = await registry.ExecuteAsync<AddComponentRequest, AddComponentResult>(
            "rekall.component.add",
            new AddComponentRequest(
                root,
                "Main",
                entity.Id,
                "Rekall.Rigidbody3D",
                new JsonObject { ["Mass"] = 0 }),
            Context("invalid range"));

        Assert.Contains(structured.Errors, error => error.Code == "REKALL_COMPONENT_PROPERTY_SHAPE_INVALID");
        Assert.Contains(range.Errors, error => error.Code == "REKALL_COMPONENT_PROPERTY_OUT_OF_RANGE");
        Assert.Empty((await LoadAsync(root)).Entities.Single().Components);
    }

    [Fact]
    public async Task DefaultRegistryRejectsCaseDuplicateBuiltInProperties()
    {
        var (root, entity) = await CreateSceneAsync();
        var result = await RekallAgeDefaultCommandRegistry.Create()
            .ExecuteAsync<AddComponentRequest, AddComponentResult>(
                "rekall.component.add",
                new AddComponentRequest(
                    root,
                    "Main",
                    entity.Id,
                    "Rekall.PointLight",
                    new JsonObject { ["Intensity"] = 1, ["intensity"] = 2 }),
                Context("duplicate property"));

        Assert.Contains(result.Errors, error => error.Code == "REKALL_COMPONENT_PROPERTY_DUPLICATE");
        Assert.Empty((await LoadAsync(root)).Entities.Single().Components);
    }

    private static async Task<(string Root, RekallAgeEntityDocument Entity)> CreateSceneAsync(
        RekallAgeComponentDocument? component = null)
    {
        var root = TestPaths.CreateTempDirectory();
        var entity = RekallAgeEntityDocument.Create("Entity", []);
        if (component is not null)
        {
            entity = entity.AddComponent(component);
        }
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity),
            CancellationToken.None);
        return (root, entity);
    }

    private static Task<RekallAgeSceneDocument> LoadAsync(string root) =>
        new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None).AsTask();

    private static RekallAgeCommandContext Context(string name) =>
        new("test", RekallAgeTransaction.Begin(name), CancellationToken.None);
}
