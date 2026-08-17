using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Workflows.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Workflows;

public sealed class VerifyPlayableGameCommandTests
{
    [Fact]
    public async Task MissingPlayableModulePreservesExecutableScaffoldSuggestion()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("diagnose missing playable"),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]),
            CancellationToken.None);
        var scaffold = await new ScaffoldRuntimeSystemModuleCommand().ExecuteAsync(
            new ScaffoldRuntimeSystemModuleRequest(
                root,
                "game.rules",
                "Game Rules",
                "GameRules",
                "GameState",
                "GameRulesSystem"),
            context);
        Assert.True(scaffold.Ok, scaffold.Summary);

        var result = await new VerifyPlayableGameCommand().ExecuteAsync(
            new VerifyPlayableGameRequest(root),
            context);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors, item => item.Code == "REKALL_PLAYABLE_MODULE_MISSING");
        var suggestion = Assert.Single(error.SuggestedCommands!);
        Assert.Equal("rekall.module.scaffold_playable", suggestion.Tool);
        Assert.Equal(root, suggestion.Arguments["projectRoot"]);
    }
}
