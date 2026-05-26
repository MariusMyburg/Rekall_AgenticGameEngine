using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules;
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

    public static TheoryData<string, string[]> GenreDrawCommandIds => new()
    {
        { "pong", ["left-paddle", "right-paddle", "ball", "hud"] },
        { "breakout", ["paddle", "ball", "brick-field", "hud"] },
        { "asteroids", ["ship", "asteroid-alpha", "projectile", "hud"] },
        { "top-down-shooter", ["player", "enemy-wave", "projectile", "hud"] },
        { "platformer-2d", ["runner", "platform-ground", "collectible", "hud"] },
        { "tower-defense", ["enemy-path", "tower", "enemy-wave", "base-health"] },
        { "visual-novel", ["background-panel", "portrait-left", "dialogue-box", "choice-cursor"] },
        { "first-person-exploration", ["corridor", "reticle", "interaction-hotspot", "objective"] },
        { "collectathon-3d", ["avatar", "collectible", "camera-orbit", "goal-gate"] },
        { "puzzle", ["grid", "tile-active", "cursor", "objective"] }
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

    [Theory]
    [MemberData(nameof(GenreDrawCommandIds))]
    public async Task ScaffoldPlayableModuleCreatesGenreSpecificDrawCommandIds(string kind, string[] expectedIds)
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin($"playable scaffold draws {kind}"), CancellationToken.None);

        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, $"module.{kind}", $"Module {kind}", $"{kind}Module", kind),
            context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        var source = await File.ReadAllTextAsync(scaffold.Value.SourcePath);
        foreach (var expectedId in expectedIds)
        {
            Assert.Contains($"\"{expectedId}\"", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ScaffoldPlayableModuleGuidesAgentsToUseDeltaSeconds()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("playable scaffold delta"), CancellationToken.None);

        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "module.pong", "Module Pong", "ModulePong", "pong"),
            context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        var source = await File.ReadAllTextAsync(scaffold.Value.SourcePath);
        Assert.Contains("input.DeltaSeconds", source, StringComparison.Ordinal);
        Assert.Contains("seconds *", source, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(input.DeltaSeconds", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectModuleLoaderCanLoadSameModuleNameFromDifferentProjects()
    {
        var firstRoot = TestPaths.CreateTempDirectory();
        var secondRoot = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("duplicate module names"), CancellationToken.None);

        foreach (var root in new[] { firstRoot, secondRoot })
        {
            await new ScaffoldPlayableModuleCommand().ExecuteAsync(
                new ScaffoldPlayableModuleRequest(root, "module.pong", "Module Pong", "PongPlayable", "pong"),
                context);
            var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
            Assert.True(build.Ok, build.Summary);
        }

        var firstAssemblies = RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(firstRoot);
        var secondAssemblies = RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(secondRoot);

        Assert.Single(firstAssemblies);
        Assert.Single(secondAssemblies);
        Assert.Contains(firstAssemblies[0].GetTypes(), type => typeof(IRekallAgePlayableModule).IsAssignableFrom(type));
        Assert.Contains(secondAssemblies[0].GetTypes(), type => typeof(IRekallAgePlayableModule).IsAssignableFrom(type));
    }
}
