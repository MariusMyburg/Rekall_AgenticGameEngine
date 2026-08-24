using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Sdk;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Examples;

public sealed class AetherfallCitadelAcceptanceTests
{
    [Fact]
    public void MainSceneExposesSubstantialInspectableWorldContract()
    {
        var scenePath = Path.Combine(
            FindRepositoryRoot(),
            "Examples",
            "AetherfallCitadel",
            "Scenes",
            "Main.age.scene.json");
        var scene = JsonNode.Parse(File.ReadAllText(scenePath))!.AsObject();
        var entities = scene["entities"]!.AsArray();
        var components = entities
            .SelectMany(entity => entity!["components"]!.AsArray())
            .ToArray();

        Assert.True(entities.Count >= 60, $"Expected at least 60 stable entities, found {entities.Count}.");
        Assert.Contains(entities, entity => ReadString(entity, "name") == "AetherWarden");
        Assert.Contains(components, component => ReadString(component, "type") == "Rekall.InputActionMap");
        Assert.Contains(
            components,
            component => ReadString(component, "type") == "Game.Modules.AetherfallRules.WardenState");
        Assert.Contains(entities, entity => HasTag(entity, "zone.arrival"));
        Assert.Contains(entities, entity => HasTag(entity, "zone.resonance"));
        Assert.Contains(entities, entity => HasTag(entity, "zone.observatory"));
        Assert.Single(
            components,
            component =>
                ReadString(component, "type") == "Rekall.Camera3D"
                && ReadBoolean(component, "active"));
        Assert.Contains(components, component => ReadString(component, "type") == "Rekall.UiCanvas");
    }

    [Fact]
    public async Task RulesModuleBuildsAndMovesWardenFromSemanticInput()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        await new RekallAgeModuleSdkInstaller().InstallAsync(projectRoot, CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("build Aetherfall rules"),
            CancellationToken.None);

        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(projectRoot), context);

        Assert.True(build.Ok, build.Summary);
        Assert.Single(
            build.Value.Modules,
            module => module.ModuleName == "AetherfallRules" && module.Succeeded);

        var inputs = Enumerable.Range(0, 4)
            .Select(_ => new RekallAgeRuntimeInputFrame(
                SemanticActions:
                [
                    new("move.horizontal", 1, IsDown: true),
                    new("move.vertical", 0.5, IsDown: true)
                ]))
            .ToArray();
        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            inputs.Length,
            inputs,
            CancellationToken.None);
        var warden = world.Entities.Single(entity => entity.Name == "AetherWarden");

        Assert.Contains("AetherfallRulesSystem", world.SystemsRun);
        Assert.True(warden.Transform.Position3D.X > 0, $"Expected positive X movement, found {warden.Transform.Position3D.X}.");
        Assert.True(warden.Transform.Position3D.Z > -12, $"Expected positive Z movement, found {warden.Transform.Position3D.Z}.");
    }

    [Fact]
    public async Task PulseProjectileDamagesConfiguredSentinel()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        await BuildRulesAsync(projectRoot);
        var inputs = Enumerable.Range(0, 45)
            .Select(frame => new RekallAgeRuntimeInputFrame(
                SemanticActions: frame == 0
                    ? [new("ability.pulse", 1, IsDown: true, WasPressed: true)]
                    : []))
            .ToArray();

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            inputs.Length,
            inputs,
            CancellationToken.None);
        var sentinel = world.Entities.Single(entity => entity.Name == "TrainingSentinel");
        var health = sentinel.Components
            .Single(component => component.Type == "Game.Modules.AetherfallRules.EnemyState")
            .Properties["health"]!
            .GetValue<double>();

        Assert.True(health < 60, $"Expected pulse damage below 60 health, found {health}.");
    }

    [Fact]
    public async Task DashProducesBurstMovementAndConsumesAether()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        await BuildRulesAsync(projectRoot);
        var input = new RekallAgeRuntimeInputFrame(
            SemanticActions:
            [
                new("move.vertical", 1, IsDown: true),
                new("ability.dash", 1, IsDown: true, WasPressed: true)
            ]);

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            1,
            [input],
            CancellationToken.None);
        var warden = world.Entities.Single(entity => entity.Name == "AetherWarden");
        var state = warden.Components.Single(
            component => component.Type == "Game.Modules.AetherfallRules.WardenState");

        Assert.True(warden.Transform.Position3D.Z > -10, $"Expected dash beyond Z=-10, found {warden.Transform.Position3D.Z}.");
        Assert.True(state.Properties["aether"]!.GetValue<double>() < 100);
        Assert.True(state.Properties["dashCooldown"]!.GetValue<double>() > 0);
        Assert.Contains(
            world.Entities,
            entity => entity.Components.Any(
                component => component.Type == "Game.Modules.AetherfallRules.EffectState"));
    }

    [Fact]
    public async Task CrossingEchoShardCollectsItIntoWardenState()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        await BuildRulesAsync(projectRoot);
        var inputs = Enumerable.Range(0, 30)
            .Select(_ => new RekallAgeRuntimeInputFrame(
                SemanticActions:
                [
                    new("move.horizontal", -1, IsDown: true),
                    new("move.vertical", 1, IsDown: true)
                ]))
            .ToArray();

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            inputs.Length,
            inputs,
            CancellationToken.None);
        var warden = world.Entities.Single(entity => entity.Name == "AetherWarden");
        var shard = world.Entities.Single(entity => entity.Name == "EchoShard01");
        var state = warden.Components.Single(
            component => component.Type == "Game.Modules.AetherfallRules.WardenState");

        Assert.Equal(1, state.Properties["shardCount"]!.GetValue<double>());
        Assert.False(shard.Visible);
    }

    [Fact]
    public async Task ConfiguredHazardsMoveFromDeltaTime()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        await BuildRulesAsync(projectRoot);

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            60,
            CancellationToken.None);
        var hazard = world.Entities.Single(entity => entity.Name == "CourtOrbiter01");

        Assert.NotEqual(7, hazard.Transform.Position3D.X, precision: 3);
        Assert.NotEqual(13, hazard.Transform.Position3D.Z, precision: 3);
    }

    private static bool HasTag(JsonNode? entity, string expected) =>
        entity?["tags"] is JsonArray tags
        && tags.Any(tag => string.Equals(tag?.GetValue<string>(), expected, StringComparison.Ordinal));

    private static string? ReadString(JsonNode? node, string propertyName) =>
        node?[propertyName]?.GetValue<string>();

    private static bool ReadBoolean(JsonNode? component, string propertyName) =>
        component?["properties"]?[propertyName]?.GetValue<bool>() == true;

    private static async Task BuildRulesAsync(string projectRoot)
    {
        await new RekallAgeModuleSdkInstaller().InstallAsync(projectRoot, CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("build Aetherfall rules"),
            CancellationToken.None);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(projectRoot), context);
        Assert.True(build.Ok, build.Summary);
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
