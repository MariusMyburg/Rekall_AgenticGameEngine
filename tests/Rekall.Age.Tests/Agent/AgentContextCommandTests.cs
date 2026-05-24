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
        Assert.Equal("puzzle", project.Value.Summary.SourceTemplateId);
        Assert.True(scene.Ok);
        Assert.Equal("Main", scene.Value.Summary.Scene);
        Assert.Contains(scene.Value.Summary.Entities, entity => entity.Name == "PuzzleGrid");
        Assert.Contains("GridBoard", scene.Value.Summary.ComponentTypes);
    }

    [Fact]
    public void ContextCommandsAreVisibleToMcpCatalog()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new GetEngineStatusCommand());
        registry.Register(new GetProjectSummaryCommand());
        registry.Register(new GetSceneSummaryCommand());
        registry.Register(new ListGameTemplatesCommand());

        var catalog = RekallAgeMcpCatalog.FromRegistry(registry);

        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.context.engine_status");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.context.project_summary");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.context.scene_summary");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.templates.list");
    }

    [Fact]
    public async Task EngineStatusReturnsAgentFirstMvpWorkflowMap()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("engine status"), CancellationToken.None);

        var result = await new GetEngineStatusCommand().ExecuteAsync(new GetEngineStatusRequest(), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("Rekall AGE", result.Value.EngineName);
        Assert.True(result.Value.AgentFirst);
        Assert.Contains("pong", result.Value.MvpTemplateIds);
        Assert.Contains("first-person-exploration", result.Value.MvpTemplateIds);
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.templates.inspect" && workflow.Recommended);
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.workflow.create_playable_package_from_template" && workflow.Recommended);
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.templates.verify_mvp");
        Assert.Contains("vulkan", result.Value.RenderingPosture, StringComparison.OrdinalIgnoreCase);
    }
}
