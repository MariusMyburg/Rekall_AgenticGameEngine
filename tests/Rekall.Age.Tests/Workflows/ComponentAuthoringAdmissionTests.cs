using System.Text.Json.Nodes;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
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
    public async Task DefaultRegistrySetsExistingBuiltInPropertyCaseInsensitively()
    {
        var (root, entity) = await CreateSceneAsync(
            RekallAgeComponentDocument.Create(
                "Rekall.GeometryPrimitive",
                new JsonObject { ["Primitive"] = "sphere", ["Color"] = "#39ff14" }));
        var registry = RekallAgeDefaultCommandRegistry.Create();

        var result = await registry.ExecuteAsync<SetComponentPropertyRequest, SetComponentPropertyResult>(
            "rekall.component.set_property",
            new SetComponentPropertyRequest(
                root,
                "Main",
                entity.Id,
                "Rekall.GeometryPrimitive",
                "color",
                JsonValue.Create("#ff66cc")),
            Context("case-insensitive property update"));

        Assert.True(result.Ok, result.Summary);
        var component = Assert.Single((await LoadAsync(root)).Entities.Single().Components);
        Assert.Equal(["Primitive", "color"], component.Properties.Select(property => property.Key));
        Assert.Equal("#ff66cc", component.Properties["color"]!.GetValue<string>());
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
    public async Task DefaultRegistryRejectsStringForNumericProjectComponentPropertyWithoutMutation()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = Context("project component type admission");
        var scaffold = await new ScaffoldRuntimeSystemModuleCommand().ExecuteAsync(
            new ScaffoldRuntimeSystemModuleRequest(
                root,
                "game.state",
                "Game State",
                "GameState",
                "GameStateComponent",
                "GameStateSystem"),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(build.Ok, build.Summary);

        var componentType = $"{scaffold.Value.Namespace}.GameStateComponent";
        var entity = RekallAgeEntityDocument.Create("Entity", [])
            .AddComponent(RekallAgeComponentDocument.Create(
                componentType,
                new JsonObject { ["ValuePerSecond"] = 1.0 }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity),
            CancellationToken.None);

        var mutation = await RekallAgeDefaultCommandRegistry.Create()
            .ExecuteAsync<SetComponentPropertyRequest, SetComponentPropertyResult>(
                "rekall.component.set_property",
                new SetComponentPropertyRequest(
                    root,
                    "Main",
                    entity.Id,
                    componentType,
                    "ValuePerSecond",
                    JsonValue.Create("0")),
                Context("invalid project component scalar"));

        Assert.False(mutation.Ok);
        Assert.Contains(mutation.Errors, error => error.Code == "REKALL_COMPONENT_PROPERTY_TYPE_INVALID");
        var stored = Assert.Single((await LoadAsync(root)).Entities.Single().Components);
        Assert.Equal(1.0, stored.Properties["ValuePerSecond"]!.GetValue<double>());
    }

    [Fact]
    public async Task DefaultRegistryFailsClosedWhenProjectModuleSourceChangedAfterBuild()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = Context("stale project component schema");
        var scaffold = await new ScaffoldRuntimeSystemModuleCommand().ExecuteAsync(
            new ScaffoldRuntimeSystemModuleRequest(
                root, "game.state", "Game State", "GameState", "GameStateComponent", "GameStateSystem"),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(build.Ok, build.Summary);

        var sourcePath = Path.Combine(root, "Modules", "GameState", "GameStateModule.cs");
        await File.AppendAllTextAsync(sourcePath, Environment.NewLine + "// source changed after receipt");
        var componentType = $"{scaffold.Value.Namespace}.GameStateComponent";
        var entity = RekallAgeEntityDocument.Create("Entity", [])
            .AddComponent(RekallAgeComponentDocument.Create(componentType, new JsonObject { ["ValuePerSecond"] = 1.0 }));
        await new RekallAgeSceneStore().SaveAsync(
            root, RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity), CancellationToken.None);

        var mutation = await RekallAgeDefaultCommandRegistry.Create()
            .ExecuteAsync<SetComponentPropertyRequest, SetComponentPropertyResult>(
                "rekall.component.set_property",
                new SetComponentPropertyRequest(root, "Main", entity.Id, componentType, "Invented", JsonValue.Create(9)),
                Context("reject stale schema mutation"));

        Assert.False(mutation.Ok);
        Assert.Contains(mutation.Errors, error => error.Code == "REKALL_PROJECT_COMPONENT_SCHEMA_UNAVAILABLE");
        Assert.False(Assert.Single((await LoadAsync(root)).Entities.Single().Components).Properties.ContainsKey("Invented"));
    }

    [Fact]
    public async Task DefaultRegistryRejectsNullForNonNullablePrimitiveProperty()
    {
        var (root, entity) = await CreateSceneAsync();
        var result = await RekallAgeDefaultCommandRegistry.Create()
            .ExecuteAsync<AddComponentRequest, AddComponentResult>(
                "rekall.component.add",
                new AddComponentRequest(
                    root, "Main", entity.Id, "Rekall.Rigidbody2D", new JsonObject { ["Mass"] = null }),
                Context("null primitive"));

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_COMPONENT_PROPERTY_TYPE_INVALID");
    }

    [Fact]
    public async Task DefaultRegistryFailsClosedWithoutTraversingAReparseBackedModulesRoot()
    {
        var root = TestPaths.CreateTempDirectory();
        var outside = TestPaths.CreateTempDirectory();
        var modulesRoot = Path.Combine(root, "Modules");
        try
        {
            Directory.CreateSymbolicLink(modulesRoot, outside);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }
        var entity = RekallAgeEntityDocument.Create("Entity", [])
            .AddComponent(RekallAgeComponentDocument.Create("Game.Linked.State", new JsonObject { ["Score"] = 1 }));
        await new RekallAgeSceneStore().SaveAsync(
            root, RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity), CancellationToken.None);

        var mutation = await RekallAgeDefaultCommandRegistry.Create()
            .ExecuteAsync<SetComponentPropertyRequest, SetComponentPropertyResult>(
                "rekall.component.set_property",
                new SetComponentPropertyRequest(root, "Main", entity.Id, "Game.Linked.State", "Invented", JsonValue.Create(9)),
                Context("reject linked modules root"));

        Assert.False(mutation.Ok);
        Assert.Contains(mutation.Errors, error => error.Code == "REKALL_PROJECT_COMPONENT_SCHEMA_UNAVAILABLE");
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
