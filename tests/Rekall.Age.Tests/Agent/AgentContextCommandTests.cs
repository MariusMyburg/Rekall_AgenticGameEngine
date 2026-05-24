using Rekall.Age.Agent.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Mcp;

namespace Rekall.Age.Tests.Agent;

public sealed class AgentContextCommandTests
{
    [Fact]
    public async Task ProjectAndSceneSummaryCommandsReturnCompactInspectableContext()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("context"), CancellationToken.None);
        await new CreateGameFromTemplateCommand()
            .ExecuteAsync(new CreateGameFromTemplateRequest(root, "Context Puzzle", "puzzle"), context);

        var project = await new GetProjectSummaryCommand()
            .ExecuteAsync(new GetProjectSummaryRequest(root), context);
        var scene = await new GetSceneSummaryCommand()
            .ExecuteAsync(new GetSceneSummaryRequest(root, "Main"), context);

        Assert.True(project.Ok);
        Assert.Equal("ok", project.Value.Summary.Health.Status);
        Assert.True(scene.Ok);
        Assert.Equal("Main", scene.Value.Summary.Scene);
        Assert.Contains(scene.Value.Summary.Entities, entity => entity.Name == "PuzzleGrid");
        Assert.Contains("GridBoard", scene.Value.Summary.ComponentTypes);
    }

    [Fact]
    public void ContextCommandsAreVisibleToMcpCatalog()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new GetProjectSummaryCommand());
        registry.Register(new GetSceneSummaryCommand());
        registry.Register(new ListGameTemplatesCommand());

        var catalog = RekallAgeMcpCatalog.FromRegistry(registry);

        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.context.project_summary");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.context.scene_summary");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.templates.list");
    }
}
