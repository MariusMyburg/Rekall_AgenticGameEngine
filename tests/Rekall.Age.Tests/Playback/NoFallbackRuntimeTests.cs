using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Playback.Commands;

namespace Rekall.Age.Tests.Playback;

public sealed class NoFallbackRuntimeTests
{
    [Fact]
    public async Task PlaySceneFailsWhenProjectHasNoCompiledPlayableModule()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("no fallback"), CancellationToken.None);
        await new CreateGameFromTemplateCommand().ExecuteAsync(
            new CreateGameFromTemplateRequest(root, "Data Only Pong", "pong"),
            context);

        var result = await new PlaySceneCommand().ExecuteAsync(new PlaySceneRequest(root, "Main", 1), context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_PLAYABLE_MODULE_MISSING");
    }
}
