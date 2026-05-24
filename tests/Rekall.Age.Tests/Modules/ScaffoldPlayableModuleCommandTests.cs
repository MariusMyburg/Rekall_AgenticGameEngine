using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modules;

public sealed class ScaffoldPlayableModuleCommandTests
{
    public static TheoryData<string, string> GenreMarkers => new()
    {
        { "pong", "PONG" },
        { "breakout", "BREAKOUT" },
        { "asteroids", "ASTEROIDS" },
        { "top-down-shooter", "TOP-DOWN SHOOTER" },
        { "platformer-2d", "PLATFORMER" },
        { "tower-defense", "TOWER DEFENSE" },
        { "visual-novel", "VISUAL NOVEL" },
        { "first-person-exploration", "FIRST-PERSON EXPLORATION" },
        { "collectathon-3d", "COLLECTATHON" },
        { "puzzle", "PUZZLE" }
    };

    [Fact]
    public async Task ScaffoldPlayableModuleCreatesBuildableGameplayModule()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("playable scaffold"), CancellationToken.None);

        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "module.pong", "Module Pong", "ModulePong", "module-pong"),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(File.Exists(scaffold.Value.SourcePath));
        Assert.True(build.Ok, build.Summary);
        Assert.Contains(build.Value.Modules, module => module.ModuleName == "ModulePong");
    }

    [Theory]
    [MemberData(nameof(GenreMarkers))]
    public async Task ScaffoldPlayableModuleCreatesGenreAwareStarterLoop(string kind, string marker)
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin($"playable scaffold {kind}"), CancellationToken.None);

        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, $"module.{kind}", $"Module {kind}", $"{kind}Module", kind),
            context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        var source = await File.ReadAllTextAsync(scaffold.Value.SourcePath);
        Assert.Contains(marker, source, StringComparison.Ordinal);
        Assert.DoesNotContain("Module-authored", source, StringComparison.Ordinal);
    }
}
