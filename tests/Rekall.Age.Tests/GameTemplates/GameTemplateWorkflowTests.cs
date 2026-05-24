using Rekall.Age.Agent;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Project;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Tests.GameTemplates;

public sealed class GameTemplateWorkflowTests
{
    public static TheoryData<string> RequiredTemplates => new()
    {
        "pong",
        "breakout",
        "asteroids",
        "top-down-shooter",
        "platformer-2d",
        "tower-defense",
        "visual-novel",
        "first-person-exploration",
        "collectathon-3d",
        "puzzle"
    };

    [Theory]
    [MemberData(nameof(RequiredTemplates))]
    public async Task TemplateCreatesInspectableRunnableProject(string templateId)
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateGameFromTemplateCommand());
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin($"create {templateId}"), CancellationToken.None);

        var result = await registry.ExecuteAsync<CreateGameFromTemplateRequest, CreateGameFromTemplateResult>(
            "rekall.workflow.create_game_from_template",
            new CreateGameFromTemplateRequest(root, $"Game {templateId}", templateId),
            context);

        Assert.True(result.Ok);
        Assert.Equal(templateId, result.Value.Template.Id);
        Assert.True(File.Exists(Path.Combine(root, "rekall.project.json")));
        Assert.True(File.Exists(Path.Combine(root, "Scenes", "Main.age.scene.json")));

        var sceneStore = new RekallAgeSceneStore();
        var validator = new RekallAgeProjectValidator(sceneStore);
        var summary = await new RekallAgeContextBuilder(new RekallAgeProjectStore(), sceneStore, validator)
            .BuildProjectSummaryAsync(root, CancellationToken.None);
        Assert.Equal("ok", summary.Health.Status);

        var runtime = await new RekallAgeHeadlessRuntime(sceneStore, validator)
            .RunAsync(root, "Main", TimeSpan.FromMilliseconds(20), CancellationToken.None);
        Assert.True(runtime.Ok);

        var screenshot = await new RekallAgeSoftwarePreview(sceneStore)
            .CaptureAsync(root, "Main", Path.Combine(root, "Artifacts", "Screenshots"), CancellationToken.None);
        Assert.True(screenshot.NonBlank);
    }

    [Fact]
    public void TemplateCatalogDescribesAgentRelevantCapabilities()
    {
        var catalog = RekallAgeGameTemplateCatalog.CreateDefault();

        Assert.Contains(catalog.Templates, template => template.Id == "first-person-exploration" && template.Capabilities.Contains("rendering3d"));
        Assert.Contains(catalog.Templates, template => template.Id == "visual-novel" && template.Capabilities.Contains("ui"));
    }
}
