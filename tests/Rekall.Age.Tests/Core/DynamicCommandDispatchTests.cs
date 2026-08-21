using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.World;
using Rekall.Age.World.Commands;
using System.Text.Json.Nodes;

namespace Rekall.Age.Tests.Core;

public sealed class DynamicCommandDispatchTests
{
    [Fact]
    public async Task RegistryExecutesRegisteredCommandFromJsonArguments()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());
        var context = new RekallAgeCommandContext("mcp", RekallAgeTransaction.Begin("dynamic"), CancellationToken.None);

        var result = await registry.ExecuteJsonAsync("rekall.test.echo", """{"message":"hello"}""", context);

        Assert.True(result.Ok, result.Summary);
        var value = Assert.IsType<EchoResult>(result.Value);
        Assert.Equal("hello from command", value.Message);
        Assert.Equal(context.Transaction.Id, result.Transaction.Id);
        Assert.Equal("dynamic", result.Transaction.Name);
        Assert.Equal("mcp", result.Transaction.Actor);
        Assert.Contains("echo:hello", result.Transaction.ChangedResources);
    }

    [Fact]
    public void RegistryExposesRegisteredCommandTypesForProtocolAdapters()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());

        var command = Assert.Single(registry.RegisteredCommands);

        Assert.Equal("rekall.test.echo", command.Schema.Name);
        Assert.Equal(typeof(EchoRequest), command.RequestType);
        Assert.Equal(typeof(EchoResult), command.ResultType);
    }

    [Fact]
    public async Task RegistryReportsMissingRequiredJsonFieldsBeforeCommandExecution()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());
        var context = new RekallAgeCommandContext("mcp", RekallAgeTransaction.Begin("missing argument"), CancellationToken.None);

        var result = await registry.ExecuteJsonAsync("rekall.test.echo", "{}", context);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.Equal("REKALL_COMMAND_ARGUMENT_REQUIRED", error.Code);
        Assert.Contains("'message'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'Message'", error.Message, StringComparison.Ordinal);
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task RegistryRejectsUnknownTopLevelFieldsWithAllowedContractBeforeExecution()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("unknown argument"),
            CancellationToken.None);

        var result = await registry.ExecuteJsonAsync(
            "rekall.test.echo",
            """{"message":"hello","inputFrames":[]}""",
            context);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.Equal("REKALL_COMMAND_ARGUMENT_UNKNOWN", error.Code);
        Assert.Contains("inputFrames", error.Message, StringComparison.Ordinal);
        Assert.Contains("message", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Expected command contract: Echoes a message.", error.Message, StringComparison.Ordinal);
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task RegistryNormalizesModelEncodedTypedFieldsWithoutRewritingStrings()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new NormalizeCommand());
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("normalize arguments"), CancellationToken.None);

        var result = await registry.ExecuteJsonAsync(
            "rekall.test.normalize",
            """{"limit":"50","active":"true","tags":"[\"ui\",\"audio\"]","entities":"[{\"name\":\"HUD\"}]","settings":"{\"speed\":7}","points":"[1,2,3]","literal":"[\"must-remain-a-string\"]"}""",
            context);

        Assert.True(result.Ok, result.Summary);
        var value = Assert.IsType<NormalizeResult>(result.Value);
        Assert.Equal(50, value.Limit);
        Assert.True(value.Active);
        Assert.Equal(["ui", "audio"], value.Tags);
        Assert.Equal("HUD", Assert.Single(value.Entities).Name);
        Assert.Equal(7, value.Settings["speed"]!.GetValue<int>());
        Assert.Equal(3, value.Points.Count);
        Assert.Equal("[\"must-remain-a-string\"]", value.Literal);
    }

    [Fact]
    public async Task RegistryNormalizesCommonTypeDirectedArgumentAliases()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new AliasCommand());
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("normalize aliases"),
            CancellationToken.None);

        var result = await registry.ExecuteJsonAsync(
            "rekall.test.aliases",
            """{"frame":"30","packageDirectory":"F:\\Builds\\Game"}""",
            context);

        Assert.True(result.Ok, result.Summary);
        var value = Assert.IsType<AliasResult>(result.Value);
        Assert.Equal(30, value.Frames);
        Assert.Equal("F:\\Builds\\Game", value.PackagePath);

        var priorAliases = await registry.ExecuteJsonAsync(
            "rekall.test.aliases",
            """{"frameCount":"12","archivePath":"F:\\Builds\\Game.zip"}""",
            context);

        Assert.True(priorAliases.Ok, priorAliases.Summary);
        var priorValue = Assert.IsType<AliasResult>(priorAliases.Value);
        Assert.Equal(12, priorValue.Frames);
        Assert.Equal("F:\\Builds\\Game.zip", priorValue.PackagePath);

        var wrapperPrefixedAliases = await registry.ExecuteJsonAsync(
            "rekall.test.aliases",
            """{"fabricFrameCount":"30","packagePath":"F:\\Builds\\Wrapped"}""",
            context);

        Assert.True(wrapperPrefixedAliases.Ok, wrapperPrefixedAliases.Summary);
        var wrapperPrefixedValue = Assert.IsType<AliasResult>(wrapperPrefixedAliases.Value);
        Assert.Equal(30, wrapperPrefixedValue.Frames);
    }

    [Fact]
    public async Task RegistryAllowsComponentAddToOmitEmptyProperties()
    {
        var root = TestPaths.CreateTempDirectory();
        var entity = RekallAgeEntityDocument.Create("Body", []);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity),
            CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new AddComponentCommand());
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("add default component"),
            CancellationToken.None);

        var result = await registry.ExecuteJsonAsync(
            "rekall.component.add",
            $$"""{"projectRoot":"{{root.Replace("\\", "\\\\")}}","sceneName":"Main","entityId":"{{entity.Id}}","componentType":"Rekall.Transform2D"}""",
            context);

        Assert.True(result.Ok, result.Summary);
        var saved = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var component = Assert.Single(saved.GetRequiredEntity(entity.Id).Components);
        Assert.Equal("Rekall.Transform2D", component.Type);
        Assert.Empty(component.Properties);
    }

    [Fact]
    public async Task RegistryNormalizesUnambiguousSceneBlueprintComponentShapes()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]),
            CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ApplySceneBlueprintCommand());
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("normalize blueprint components"),
            CancellationToken.None);
        var escapedRoot = root.Replace("\\", "\\\\", StringComparison.Ordinal);

        var result = await registry.ExecuteJsonAsync(
            "rekall.scene.apply_blueprint",
            $$$"""
              {
                "projectRoot":"{{{escapedRoot}}}",
                "sceneName":"Main",
                "clearExisting":true,
                "entities":[{
                  "name":"Body",
                  "components":[
                    {"type":"Rekall.Transform3D","X":2,"Y":3},
                    {"type":"Rekall.Rigidbody3D","Type":"Dynamic","properties":{"Mass":4}},
                    {"typeName":"Rekall.PointLight","Intensity":2},
                    {"Type":"Game.State","Properties":[
                      {"Name":"Score","Value":1},
                      {"Name":"Complete","Value":false}
                    ]}
                  ]
                }]
              }
              """,
            context);

        Assert.True(result.Ok, result.Summary);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var components = Assert.Single(scene.Entities).Components;
        var transform = Assert.Single(components, component => component.Type == "Rekall.Transform3D");
        Assert.Equal(2, transform.Properties["X"]!.GetValue<int>());
        Assert.Equal(3, transform.Properties["Y"]!.GetValue<int>());
        var body = Assert.Single(components, component => component.Type == "Rekall.Rigidbody3D");
        Assert.Equal("Dynamic", body.Properties["Type"]!.GetValue<string>());
        Assert.Equal(4, body.Properties["Mass"]!.GetValue<int>());
        var light = Assert.Single(components, component => component.Type == "Rekall.PointLight");
        Assert.Equal(2, light.Properties["Intensity"]!.GetValue<int>());
        var state = Assert.Single(components, component => component.Type == "Game.State");
        Assert.Equal(1, state.Properties["Score"]!.GetValue<int>());
        Assert.False(state.Properties["Complete"]!.GetValue<bool>());
    }

    [Fact]
    public async Task RegistryRejectsConflictingBlueprintPropertiesWithIndexedPath()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ApplySceneBlueprintCommand());
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("reject ambiguous blueprint component"),
            CancellationToken.None);

        var result = await registry.ExecuteJsonAsync(
            "rekall.scene.apply_blueprint",
            """
              {
                "projectRoot":"F:\\Game",
                "sceneName":"Main",
                "entities":[{
                  "name":"Body",
                  "components":[{
                    "Type":"Rekall.Transform3D",
                    "properties":{"X":1},
                    "x":2
                  }]
                }]
              }
              """,
            context);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.Equal("REKALL_COMMAND_ARGUMENTS_INVALID", error.Code);
        Assert.Contains("conflicting property 'x'", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$.entities[0].components[0]", error.Message, StringComparison.Ordinal);
        Assert.Contains("Expected command contract", error.Message, StringComparison.Ordinal);
        Assert.Contains("entities and components are JSON arrays", error.Message, StringComparison.Ordinal);
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task RegistryPreservesStructuredBlueprintValidationForMissingComponentType()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ApplySceneBlueprintCommand());
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("reject missing blueprint type"),
            CancellationToken.None);

        var result = await registry.ExecuteJsonAsync(
            "rekall.scene.apply_blueprint",
            """
              {
                "projectRoot":"F:\\Game",
                "sceneName":"Main",
                "entities":[{"name":"Body","components":[{"X":1}]}]
              }
              """,
            context);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.Equal("REKALL_SCENE_BLUEPRINT_COMPONENT_TYPE_REQUIRED", error.Code);
        Assert.Equal("Main.entities[0].components[0]", error.Target);
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task RegistryRejectsUnknownReservedBlueprintComponentAtIndexedTarget()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ApplySceneBlueprintCommand());
        var arguments = new JsonObject
        {
            ["projectRoot"] = "unused",
            ["sceneName"] = "Main",
            ["entities"] = new JsonArray(new JsonObject
            {
                ["name"] = "Player",
                ["components"] = new JsonArray(new JsonObject
                {
                    ["type"] = "Rekall.Collider3D"
                })
            })
        };
        var result = await registry.ExecuteJsonAsync(
            "rekall.scene.apply_blueprint",
            arguments.ToJsonString(),
            new RekallAgeCommandContext(
                "test",
                RekallAgeTransaction.Begin("blueprint"),
                CancellationToken.None));

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.Equal("REKALL_COMPONENT_RESERVED_TYPE_UNKNOWN", error.Code);
        Assert.Equal("Main.entities[0].components[0]", error.Target);
        Assert.Contains(error.SuggestedCommands!, command =>
            command.Tool == "rekall.module.search_component_schemas");
    }

    [Fact]
    public async Task RegistryNormalizesFlatComponentsInsideEncodedBlueprintArray()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]),
            CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ApplySceneBlueprintCommand());
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("normalize encoded blueprint"),
            CancellationToken.None);
        var encodedEntities = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "Encoded Body",
                ["components"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "Rekall.Transform3D",
                        ["X"] = 7
                    }
                }
            }
        }.ToJsonString();
        var arguments = new JsonObject
        {
            ["projectRoot"] = root,
            ["sceneName"] = "Main",
            ["entities"] = encodedEntities
        }.ToJsonString();

        var result = await registry.ExecuteJsonAsync(
            "rekall.scene.apply_blueprint",
            arguments,
            context);

        Assert.True(result.Ok, result.Summary);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var component = Assert.Single(Assert.Single(scene.Entities).Components);
        Assert.Equal("Rekall.Transform3D", component.Type);
        Assert.Equal(7, component.Properties["X"]!.GetValue<int>());
    }

    private sealed record EchoRequest(string Message);

    private sealed record EchoResult(string Message);

    private sealed record NormalizeRequest(
        int Limit,
        bool Active,
        IReadOnlyList<string> Tags,
        IReadOnlyList<NormalizeEntity> Entities,
        JsonObject Settings,
        JsonArray Points,
        string Literal);

    private sealed record NormalizeEntity(string Name);

    private sealed record NormalizeResult(
        int Limit,
        bool Active,
        IReadOnlyList<string> Tags,
        IReadOnlyList<NormalizeEntity> Entities,
        JsonObject Settings,
        JsonArray Points,
        string Literal);

    private sealed record AliasRequest(int Frames, string PackagePath);

    private sealed record AliasResult(int Frames, string PackagePath);

    private sealed class EchoCommand : IRekallAgeCommand<EchoRequest, EchoResult>
    {
        public string Name => "rekall.test.echo";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Echoes a message.",
            typeof(EchoRequest).FullName!,
            typeof(EchoResult).FullName!);

        public ValueTask<RekallAgeCommandResult<EchoResult>> ExecuteAsync(
            EchoRequest request,
            RekallAgeCommandContext context)
        {
            context.Transaction.RecordChangedResource($"echo:{request.Message}");
            return ValueTask.FromResult(RekallAgeCommandResult<EchoResult>.Success(
                new EchoResult($"{request.Message} from command"),
                "Echoed message."));
        }
    }

    private sealed class NormalizeCommand : IRekallAgeCommand<NormalizeRequest, NormalizeResult>
    {
        public string Name => "rekall.test.normalize";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Normalizes model arguments.",
            typeof(NormalizeRequest).FullName!,
            typeof(NormalizeResult).FullName!);

        public ValueTask<RekallAgeCommandResult<NormalizeResult>> ExecuteAsync(
            NormalizeRequest request,
            RekallAgeCommandContext context)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<NormalizeResult>.Success(
                new NormalizeResult(
                    request.Limit,
                    request.Active,
                    request.Tags,
                    request.Entities,
                    request.Settings,
                    request.Points,
                    request.Literal),
                "Normalized arguments."));
        }
    }

    private sealed class AliasCommand : IRekallAgeCommand<AliasRequest, AliasResult>
    {
        public string Name => "rekall.test.aliases";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Tests type-directed aliases.",
            typeof(AliasRequest).FullName!,
            typeof(AliasResult).FullName!);

        public ValueTask<RekallAgeCommandResult<AliasResult>> ExecuteAsync(
            AliasRequest request,
            RekallAgeCommandContext context)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<AliasResult>.Success(
                new AliasResult(request.Frames, request.PackagePath)));
        }
    }
}
