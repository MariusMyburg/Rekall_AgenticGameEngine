using System.Text.Json.Nodes;
using Rekall.Age.Agent.Commands;
using Rekall.Age.Project.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Mcp;

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

        Assert.Equal(["rekall.context.engine_status", "rekall.tools.search"], executor.Tools.Select(tool => tool.Name));

        var result = await executor.ExecuteAsync(
            "rekall.tools.search",
            new JsonObject { ["query"] = "create project" },
            CancellationToken.None);

        Assert.True(result["ok"]!.GetValue<bool>());
        Assert.Contains(executor.Tools, tool => tool.Name == "rekall.project.create");
        Assert.Equal(3, executor.Tools.Count);
    }
}
