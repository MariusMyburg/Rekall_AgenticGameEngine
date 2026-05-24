using Rekall.Age.Agent;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Mcp;
using Rekall.Age.Project;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Tests.VerticalSlice;

public sealed class AgenticVerticalSliceTests
{
    [Fact]
    public async Task AgentCanCreateInspectRunCaptureAndAdvertiseToolsForStarterGame()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateGameFromTemplateCommand());
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vertical slice"), CancellationToken.None);

        var game = await registry.ExecuteAsync<CreateGameFromTemplateRequest, CreateGameFromTemplateResult>(
            "rekall.workflow.create_game_from_template",
            new CreateGameFromTemplateRequest(root, "Asteroid Signal", "asteroids"),
            context);

        Assert.True(game.Ok);
        Assert.Contains(context.Transaction.ChangedResources, resource => resource.EndsWith("rekall.project.json", StringComparison.Ordinal));

        var sceneStore = new RekallAgeSceneStore();
        var validator = new RekallAgeProjectValidator(sceneStore);
        var summary = await new RekallAgeContextBuilder(new RekallAgeProjectStore(), sceneStore, validator)
            .BuildProjectSummaryAsync(root, CancellationToken.None);

        Assert.Equal("Asteroid Signal", summary.Project);
        Assert.Equal("ok", summary.Health.Status);

        var run = await new RekallAgeHeadlessRuntime(sceneStore, validator)
            .RunAsync(root, "Main", TimeSpan.FromMilliseconds(33), CancellationToken.None);
        Assert.True(run.Ok);
        Assert.True(run.FramesSimulated >= 2);

        var screenshot = await new RekallAgeSoftwarePreview(sceneStore)
            .CaptureAsync(root, "Main", Path.Combine(root, "Artifacts", "Screenshots"), CancellationToken.None);
        Assert.True(screenshot.NonBlank);

        var catalog = RekallAgeMcpCatalog.FromRegistry(registry);
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.workflow.create_game_from_template");
    }
}
