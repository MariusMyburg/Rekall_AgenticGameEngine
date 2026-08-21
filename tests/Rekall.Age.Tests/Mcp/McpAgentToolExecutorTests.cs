using System.Text.Json.Nodes;
using Rekall.Age.Agent.Commands;
using Rekall.Age.Project.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Mcp;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Validation.Commands;

namespace Rekall.Age.Tests.Mcp;

public sealed class McpAgentToolExecutorTests
{
    [Fact]
    public async Task ExecutorProjectsCommandSchemasAndExecutesStructuredResults()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new GetEngineStatusCommand());
        var executor = new RekallAgeMcpAgentToolExecutor(registry, "benchmark-agent");

        var tool = Assert.Single(executor.Tools);
        Assert.Equal("rekall.context.engine_status", tool.Name);
        Assert.Equal("object", tool.Parameters["type"]!.GetValue<string>());

        var result = await executor.ExecuteAsync(tool.Name, new JsonObject(), CancellationToken.None);

        Assert.True(result["ok"]!.GetValue<bool>());
        Assert.Contains("Rekall AGE", result["summary"]!.GetValue<string>());
        Assert.NotNull(result["value"]);
    }

    [Fact]
    public async Task ProgressiveDiscoveryExposesOnlyMatchedNativeTools()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new GetEngineStatusCommand());
        registry.Register(new CreateProjectCommand());
        var executor = new RekallAgeMcpAgentToolExecutor(registry, progressiveDiscovery: true);

        Assert.Equal(
            ["rekall.context.engine_status", "rekall.tools.execute", "rekall.tools.search"],
            executor.Tools.Select(tool => tool.Name));

        var result = await executor.ExecuteAsync(
            "rekall.tools.search",
            new JsonObject { ["query"] = "create project" },
            CancellationToken.None);

        Assert.True(result["ok"]!.GetValue<bool>());
        Assert.Contains("parameters", result["tools"]![0]!.AsObject());
        Assert.Contains(executor.Tools, tool => tool.Name == "rekall.project.create");
        Assert.Contains("call the matched native tool directly", result["instruction"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);

        var executed = await executor.ExecuteAsync(
            "rekall.tools.execute",
            new JsonObject
            {
                ["name"] = "rekall.project.create",
                ["arguments"] = new JsonObject
                {
                    ["projectRoot"] = TestPaths.CreateTempDirectory(),
                    ["name"] = "Discovered",
                    ["capabilities"] = new JsonArray("world")
                }
            },
            CancellationToken.None);
        Assert.True(executed["ok"]!.GetValue<bool>(), executed.ToJsonString());

        var directDiscoveredCall = await executor.ExecuteAsync(
            "rekall.project.create",
            new JsonObject
            {
                ["projectRoot"] = TestPaths.CreateTempDirectory(),
                ["name"] = "Direct discovered",
                ["capabilities"] = new JsonArray("world")
            },
            CancellationToken.None);
        Assert.True(directDiscoveredCall["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ProgressiveDiscoveryCanRediscoverAndExecuteToolsAfterDirectExposureBudgetIsFull()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new GetEngineStatusCommand());
        for (var index = 0; index < 30; index++)
        {
            registry.Register(new TestCommand($"rekall.test.command_{index:D2}", $"unique-capability-{index:D2}"));
        }
        var executor = new RekallAgeMcpAgentToolExecutor(registry, progressiveDiscovery: true);
        for (var index = 0; index < 23; index++)
        {
            await executor.ExecuteAsync(
                "rekall.tools.search",
                new JsonObject { ["query"] = $"unique-capability-{index:D2}", ["maxResults"] = 1 },
                CancellationToken.None);
        }

        var search = await executor.ExecuteAsync(
            "rekall.tools.search",
            new JsonObject { ["query"] = "unique-capability-29", ["maxResults"] = 1 },
            CancellationToken.None);

        Assert.Equal(1, search["matched"]!.GetValue<int>());
        Assert.False(search["tools"]![0]!["directlyExposed"]!.GetValue<bool>());
        Assert.Contains("rekall.tools.execute", search["instruction"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains(executor.Tools, tool => tool.Name == "rekall.tools.execute");
        var executed = await executor.ExecuteAsync(
            "rekall.tools.execute",
            new JsonObject { ["name"] = "rekall.test.command_29", ["arguments"] = new JsonObject() },
            CancellationToken.None);
        Assert.True(executed["ok"]!.GetValue<bool>(), executed.ToJsonString());
    }

    [Fact]
    public async Task ProgressiveDiscoveryCanonicalizesUniqueSingleEditToolName()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new GetEngineStatusCommand());
        registry.Register(new CreateProjectCommand());
        var executor = new RekallAgeMcpAgentToolExecutor(registry, progressiveDiscovery: true);

        var result = await executor.ExecuteAsync(
            "rekal.tools.search",
            new JsonObject { ["query"] = "create project" },
            CancellationToken.None);

        Assert.True(result["ok"]!.GetValue<bool>());
        Assert.Equal("rekal.tools.search", result["toolNameCorrection"]!["attempted"]!.GetValue<string>());
        Assert.Equal("rekall.tools.search", result["toolNameCorrection"]!["canonical"]!.GetValue<string>());
        Assert.Contains(executor.Tools, tool => tool.Name == "rekall.project.create");
    }

    [Fact]
    public async Task ToolCanonicalizationRejectsNamesMoreThanOneEditAway()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new GetEngineStatusCommand());
        var executor = new RekallAgeMcpAgentToolExecutor(registry, progressiveDiscovery: true);

        var result = await executor.ExecuteAsync(
            "rek.context.engine_status",
            new JsonObject(),
            CancellationToken.None);

        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.Null(result["toolNameCorrection"]);
    }

    [Fact]
    public async Task BroadComponentSearchFitsTheAgentToolBudgetWithoutLosingRequestedContracts()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new SearchComponentSchemasCommand(typeof(RekallAgeBuiltInModule).Assembly));
        var executor = new RekallAgeMcpAgentToolExecutor(registry, progressiveDiscovery: true);

        var result = await executor.ExecuteAsync(
            "rekall.tools.execute",
            new JsonObject
            {
                ["name"] = "rekall.module.search_component_schemas",
                ["arguments"] = new JsonObject
                {
                    ["query"] = "ui canvas button panel animation clip animation player audio listener audio emitter transform3d",
                    ["limit"] = 12
                }
            },
            CancellationToken.None);

        Assert.True(result["ok"]!.GetValue<bool>());
        Assert.Null(result["valueTruncated"]);
        Assert.InRange(result.ToJsonString().Length, 1, 12_000);
        var types = result["value"]!["components"]!.AsArray()
            .Select(component => component!["typeName"]!.GetValue<string>())
            .ToArray();
        Assert.Contains("Rekall.AnimationClip", types);
        Assert.Contains("Rekall.AnimationPlayer", types);
        Assert.Contains("Rekall.AudioEmitter", types);
        Assert.Contains("Rekall.AudioListener", types);
        Assert.Contains("Rekall.Button", types);
        Assert.Contains("Rekall.Panel", types);
        Assert.Contains("Rekall.UiCanvas", types);
    }

    [Fact]
    public async Task ExecuteGatewayAcceptsModelEncodedJsonStringArguments()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        var executor = new RekallAgeMcpAgentToolExecutor(registry, progressiveDiscovery: true);
        var root = TestPaths.CreateTempDirectory();
        var encodedArguments = new JsonObject
        {
            ["projectRoot"] = root,
            ["name"] = "String Encoded",
            ["capabilities"] = "[\"world\"]"
        }.ToJsonString();

        var result = await executor.ExecuteAsync(
            "rekall.tools.execute",
            new JsonObject
            {
                ["name"] = "rekall.project.create",
                ["arguments"] = encodedArguments
            },
            CancellationToken.None);

        Assert.True(result["ok"]!.GetValue<bool>(), result.ToJsonString());
        Assert.True(File.Exists(Path.Combine(root, "rekall.project.json")));
    }

    [Fact]
    public async Task ExecuteGatewayRejectsOversizedEncodedArgumentsBeforeParsing()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        var executor = new RekallAgeMcpAgentToolExecutor(registry, progressiveDiscovery: true);
        var oversizedArguments = new JsonObject
        {
            ["projectRoot"] = TestPaths.CreateTempDirectory(),
            ["name"] = "Oversized",
            ["padding"] = new string('x', 1_000_001)
        }.ToJsonString();

        var result = await executor.ExecuteAsync(
            "rekall.tools.execute",
            new JsonObject
            {
                ["name"] = "rekall.project.create",
                ["arguments"] = oversizedArguments
            },
            CancellationToken.None);

        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.Contains("REKALL_AGENT_ARGUMENTS_TOO_LARGE", result.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoveredNativeToolAcceptsEquivalentGatewayArgumentEnvelope()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        var executor = new RekallAgeMcpAgentToolExecutor(registry, progressiveDiscovery: true);
        await executor.ExecuteAsync(
            "rekall.tools.search",
            new JsonObject { ["query"] = "create project" },
            CancellationToken.None);
        var root = TestPaths.CreateTempDirectory();
        var encodedArguments = new JsonObject
        {
            ["projectRoot"] = root,
            ["name"] = "Native Envelope",
            ["capabilities"] = new JsonArray("world")
        }.ToJsonString();

        var result = await executor.ExecuteAsync(
            "rekall.project.create",
            new JsonObject
            {
                ["name"] = "rekall.project.create",
                ["arguments"] = encodedArguments
            },
            CancellationToken.None);

        Assert.True(result["ok"]!.GetValue<bool>(), result.ToJsonString());
        Assert.True(File.Exists(Path.Combine(root, "rekall.project.json")));
    }

    [Fact]
    public async Task UnknownToolReturnsNearestRegisteredNamesForRecovery()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ValidateProjectCommand());
        registry.Register(new CreateProjectCommand());
        var executor = new RekallAgeMcpAgentToolExecutor(registry, progressiveDiscovery: true);

        var result = await executor.ExecuteAsync(
            "rekall.tools.execute",
            new JsonObject
            {
                ["name"] = "rekall.project.validate",
                ["arguments"] = new JsonObject()
            },
            CancellationToken.None);

        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.Equal(
            "rekall.validation.project",
            result["suggestedTools"]![0]!["name"]!.GetValue<string>());
        Assert.Contains("exact suggested name", result["instruction"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    private sealed class TestCommand(string name, string description)
        : IRekallAgeCommand<TestRequest, JsonObject>
    {
        public string Name => name;

        public RekallAgeCommandSchema Schema => new(Name, description, typeof(TestRequest).FullName!, typeof(JsonObject).FullName!);

        public ValueTask<RekallAgeCommandResult<JsonObject>> ExecuteAsync(
            TestRequest request,
            RekallAgeCommandContext context) => ValueTask.FromResult(
                RekallAgeCommandResult<JsonObject>.Success(new JsonObject { ["executed"] = Name }));
    }

    private sealed record TestRequest;
}
