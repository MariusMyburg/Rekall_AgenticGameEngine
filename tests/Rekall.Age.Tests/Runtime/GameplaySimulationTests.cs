using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class GameplaySimulationTests
{
    public static TheoryData<string> Templates => new()
    {
        { "pong" },
        { "breakout" },
        { "asteroids" },
        { "top-down-shooter" },
        { "platformer-2d" },
        { "tower-defense" },
        { "visual-novel" },
        { "first-person-exploration" },
        { "collectathon-3d" },
        { "puzzle" }
    };

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task RunSceneReportsOnlyCoreRuntimeSystemsForTemplates(string templateId)
    {
        var root = TestPaths.CreateTempDirectory();
        var createGame = new CreateGameFromTemplateCommand();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("simulation"), CancellationToken.None);
        await createGame.ExecuteAsync(new CreateGameFromTemplateRequest(root, $"Game {templateId}", templateId), context);
        var runScene = new RunSceneCommand();

        var result = await runScene.ExecuteAsync(
            new RunSceneRequest(root, "Main", 0.1),
            context);

        Assert.True(result.Ok);
        Assert.True(result.Value.FramesSimulated >= 6);
        Assert.Contains(result.Value.Observations, observation => observation.System is "Camera2D" or "Camera3D");
        Assert.DoesNotContain(result.Value.Observations, observation =>
            observation.System is "PaddleController" or "BrickGrid" or "GridBoard" or "FirstPersonController" or "ThirdPersonController");
    }

    [Fact]
    public async Task RuntimeRefusesBlockedSceneWhenValidatorIsEnabled()
    {
        var root = TestPaths.CreateTempDirectory();
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]), CancellationToken.None);
        var runtime = new RekallAgeHeadlessRuntime(sceneStore, new RekallAgeProjectValidator(sceneStore));

        var result = await runtime.RunAsync(root, "Main", TimeSpan.FromMilliseconds(16), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("active camera", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.Observations);
    }
}
