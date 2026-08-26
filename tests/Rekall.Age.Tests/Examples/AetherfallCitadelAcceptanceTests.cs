using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Sdk;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Examples;

[CollectionDefinition("Aetherfall Citadel acceptance", DisableParallelization = true)]
public sealed class AetherfallCitadelAcceptanceCollection;

[Collection("Aetherfall Citadel acceptance")]
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
        var names = entities.Select(entity => ReadString(entity, "name")).ToArray();

        Assert.True(entities.Count >= 60, $"Expected at least 60 stable entities, found {entities.Count}.");
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
    public void ResonanceEncounterConfiguresTwelveHostilesAcrossThreeArchetypes()
    {
        var scene = LoadMainScene();
        var enemyComponents = scene["entities"]!.AsArray()
            .SelectMany(entity => entity!["components"]!.AsArray())
            .Where(component => ReadString(component, "type") == "Game.Modules.AetherfallRules.EnemyState")
            .ToArray();
        var archetypes = enemyComponents
            .Select(component => ReadString(component?["properties"], "archetype"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(enemyComponents.Length >= 12, $"Expected at least 12 configured hostile actors, found {enemyComponents.Length}.");
        Assert.Contains("sentinel", archetypes);
        Assert.Contains("orbiter", archetypes);
        Assert.Contains("lancer", archetypes);
    }

    [Fact]
    public void PublishedModelsAndPresentationResolveAsAnInspectableContract()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var catalogPath = Path.Combine(projectRoot, "Assets", "assets.age.catalog.json");
        Assert.True(File.Exists(catalogPath), $"Missing model-asset catalog: {catalogPath}");

        var catalog = JsonNode.Parse(File.ReadAllText(catalogPath))!.AsObject();
        var assetIds = catalog["assets"]!.AsArray()
            .Where(asset => ReadString(asset, "kind") == "model")
            .Select(asset => ReadString(asset, "id"))
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);
        var scene = LoadMainScene();
        var entities = scene["entities"]!.AsArray();
        var components = entities.SelectMany(entity => entity!["components"]!.AsArray()).ToArray();
        var modelReferences = components
            .Where(component => ReadString(component, "type") == "Rekall.ModelAssetReference")
            .Select(component => ReadString(component?["properties"], "assetId"))
            .Where(id => id is not null)
            .ToArray();
        var hudText = components
            .Where(component => ReadString(component, "type") == "Rekall.Label")
            .Select(component => ReadString(component?["properties"], "text") ?? string.Empty)
            .ToArray();

        Assert.True(assetIds.Count >= 10, $"Expected at least 10 published models, found {assetIds.Count}.");
        Assert.NotEmpty(modelReferences);
        Assert.True(modelReferences.Distinct(StringComparer.Ordinal).Count() >= 10);
        Assert.All(modelReferences, assetId => Assert.Contains(assetId, assetIds));
        Assert.Single(components, component =>
            ReadString(component, "type") == "Rekall.Camera3D"
            && ReadBoolean(component, "active")
            && ReadString(component?["properties"], "projectionMode") == "perspective");
        Assert.True(entities.Count(entity => entity?["visible"]?.GetValue<bool>() == true) >= 60);
        Assert.Contains(hudText, text => text.Contains("OBJECTIVE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hudText, text => text.Contains("INTEGRITY", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hudText, text => text.Contains("AETHER", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hudText, text => text.Contains("SHARDS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hudText, text => text.Contains("SCORE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hudText, text => text.Contains("COMBO", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hudText, text => text.Contains("GUARDIAN", StringComparison.OrdinalIgnoreCase));
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
        Assert.Contains(
            build.Value.Modules,
            module => module.ModuleName == "AetherfallPlayable" && module.Succeeded);

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
        var warden = world.Entities.Single(entity => entity.Name == "AetherWarden");
        var health = sentinel.Components
            .Single(component => component.Type == "Game.Modules.AetherfallRules.EnemyState")
            .Properties["health"]!
            .GetValue<double>();
        var wardenState = warden.Components
            .Single(component => component.Type == "Game.Modules.AetherfallRules.WardenState");

        Assert.True(health < 60, $"Expected pulse damage below 60 health, found {health}.");
        Assert.True(wardenState.Properties["score"]!.GetValue<double>() > 0);
        Assert.True(wardenState.Properties["combo"]!.GetValue<double>() > 0);
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

    [Fact]
    public async Task PresentationTracksWardenAndSynchronizesHudState()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        await BuildRulesAsync(projectRoot);
        var inputs = MovementFrames(30, -1, 1).ToArray();

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            inputs.Length,
            inputs,
            CancellationToken.None);
        var camera = world.Entities.Single(entity => entity.Name == "CitadelCamera");
        var playerLight = world.Entities.Single(entity => entity.Name == "Warden Softbox");
        var status = world.Entities.Single(entity => entity.Name == "HudStatus");
        var guardianHud = world.Entities.Single(entity => entity.Name == "HudGuardian");
        var dormantEnemy = world.Entities.Single(entity => entity.Name == "CourtLancer");

        Assert.True(camera.Transform.Position3D.Z > -27);
        Assert.Equal(17.5, camera.Transform.Position3D.Y, precision: 3);
        Assert.Equal(42, camera.Transform.Rotation3D.X, precision: 3);
        Assert.Equal(
            40,
            camera.Components.Single(component => component.Type == "Rekall.Camera3D")
                .Properties["fieldOfView"]!.GetValue<double>(),
            precision: 3);
        Assert.True(playerLight.Transform.Position3D.Z > -7);
        Assert.Contains(
            "SHARDS 1",
            status.Components.Single(c => c.Type == "Rekall.Label").Properties["text"]!.GetValue<string>());
        Assert.Contains(
            "GUARDIAN: SEALED",
            guardianHud.Components.Single(c => c.Type == "Rekall.Label").Properties["text"]!.GetValue<string>());
        Assert.False(dormantEnemy.Visible);
    }

    [Fact]
    public async Task TwoShardsAndInteractionActivateArrivalConduitAndResonanceEncounter()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        await BuildRulesAsync(projectRoot);
        var inputs = new List<RekallAgeRuntimeInputFrame>();
        inputs.AddRange(MovementFrames(5, -1, 1));
        inputs.AddRange(MovementFrames(10, 1, 1));
        inputs.AddRange(MovementFrames(5, -1, 1));
        inputs.Add(new RekallAgeRuntimeInputFrame(
            SemanticActions: [new("interact", 1, IsDown: true, WasPressed: true)]) { DeltaSeconds = 0.1 });

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            inputs.Count,
            inputs,
            CancellationToken.None);
        var warden = world.Entities.Single(entity => entity.Name == "AetherWarden");
        var conduit = world.Entities.Single(entity => entity.Name == "ArrivalConduit");
        var gate = world.Entities.Single(entity => entity.Name == "ArrivalGate");
        var encounter = world.Entities.Single(entity => entity.Name == "CitadelEncounter");

        Assert.True(conduit.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.ConduitState").Properties["active"]!.GetValue<bool>());
        Assert.False(gate.Visible);
        Assert.Equal("resonance", warden.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.WardenState").Properties["objectivePhase"]!.GetValue<string>());
        Assert.Equal("resonance", encounter.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.EncounterState").Properties["activeZone"]!.GetValue<string>());
        Assert.Contains(world.Entities, entity => entity.Name == "CourtLancer" && entity.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.EnemyState").Properties["active"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ResetRestoresWardenAndRemovesDynamicEntities()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        await BuildRulesAsync(projectRoot);
        var inputs = new[]
        {
            new RekallAgeRuntimeInputFrame(SemanticActions:
            [
                new("move.vertical", 1, IsDown: true),
                new("ability.pulse", 1, IsDown: true, WasPressed: true)
            ]) { DeltaSeconds = 0.1 },
            new RekallAgeRuntimeInputFrame(SemanticActions:
            [
                new("reset", 1, IsDown: true, WasPressed: true)
            ]) { DeltaSeconds = 0.1 }
        };

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            inputs.Length,
            inputs,
            CancellationToken.None);
        var warden = world.Entities.Single(entity => entity.Name == "AetherWarden");
        var sentinel = world.Entities.Single(entity => entity.Name == "CourtSentinel02");
        var state = warden.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.WardenState");

        Assert.Equal(0, warden.Transform.Position3D.X, precision: 3);
        Assert.Equal(-12, warden.Transform.Position3D.Z, precision: 3);
        Assert.Equal(100, state.Properties["integrity"]!.GetValue<double>());
        Assert.Equal(100, state.Properties["aether"]!.GetValue<double>());
        Assert.Equal("arrival", state.Properties["objectivePhase"]!.GetValue<string>());
        Assert.Equal(
            70,
            sentinel.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.EnemyState").Properties["health"]!.GetValue<double>());
        Assert.DoesNotContain(world.Entities, entity => entity.Tags.Contains("projectile"));
        Assert.DoesNotContain(world.Entities, entity => entity.Tags.Contains("effect"));
    }

    [Fact]
    public async Task PauseActionFreezesAuthoredSimulation()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        await BuildRulesAsync(projectRoot);
        var input = new RekallAgeRuntimeInputFrame(SemanticActions:
        [
            new("move.vertical", 1, IsDown: true),
            new("pause", 1, IsDown: true, WasPressed: true)
        ]) { DeltaSeconds = 0.1 };

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            1,
            [input],
            CancellationToken.None);
        var warden = world.Entities.Single(entity => entity.Name == "AetherWarden");
        var state = warden.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.WardenState");

        Assert.Equal(-12, warden.Transform.Position3D.Z, precision: 3);
        Assert.Equal("paused", state.Properties["phase"]!.GetValue<string>());
    }

    [Fact]
    public async Task ActivatedHostileArchetypesAdvanceAndAttack()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        await BuildRulesAsync(projectRoot);
        var inputs = new List<RekallAgeRuntimeInputFrame>();
        inputs.AddRange(MovementFrames(5, -1, 1));
        inputs.AddRange(MovementFrames(10, 1, 1));
        inputs.AddRange(MovementFrames(5, -1, 1));
        inputs.Add(new RekallAgeRuntimeInputFrame(
            SemanticActions: [new("interact", 1, IsDown: true, WasPressed: true)]) { DeltaSeconds = 0.1 });
        inputs.AddRange(Enumerable.Range(0, 30).Select(_ => new RekallAgeRuntimeInputFrame { DeltaSeconds = 0.1 }));

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            inputs.Count,
            inputs,
            CancellationToken.None);
        var lancer = world.Entities.Single(entity => entity.Name == "CourtLancer");

        Assert.True(
            Math.Abs(lancer.Transform.Position3D.X - 5) > 0.01
            || Math.Abs(lancer.Transform.Position3D.Z - 15) > 0.01,
            $"Expected active lancer movement, found ({lancer.Transform.Position3D.X}, {lancer.Transform.Position3D.Z}).");
        Assert.Contains(world.Entities, entity => entity.Tags.Contains("hostile.projectile"));
        var hostilePulse = world.Entities.First(entity => entity.Tags.Contains("hostile.projectile"));
        Assert.Equal(
            "#a76a45",
            hostilePulse.Components.Single(component => component.Type == "Rekall.GeometryPrimitive")
                .Properties["color"]!.GetValue<string>());
    }

    [Fact]
    public async Task ArrivalSentinelWaitsUntilThePlayerEngages()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        await BuildRulesAsync(projectRoot);
        var inputs = Enumerable.Range(0, 720)
            .Select(_ => new RekallAgeRuntimeInputFrame { DeltaSeconds = 1.0 / 60.0 })
            .ToArray();

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            inputs.Length,
            inputs,
            CancellationToken.None);
        var warden = world.Entities.Single(entity => entity.Name == "AetherWarden");
        var state = warden.Components.Single(component =>
            component.Type == "Game.Modules.AetherfallRules.WardenState");

        Assert.Equal(100, state.Properties["integrity"]!.GetValue<double>());
        Assert.DoesNotContain(world.Entities, entity => entity.Tags.Contains("hostile.projectile"));
    }

    [Fact]
    public async Task ClearingResonanceEncounterOpensObservatoryAndActivatesGuardian()
    {
        var projectRoot = CreateScenarioProject(scene =>
        {
            var entities = scene["entities"]!.AsArray();
            SetComponentProperties(entities, "AetherWarden", "Game.Modules.AetherfallRules.WardenState", properties =>
                properties["objectivePhase"] = "resonance");
            SetComponentProperties(entities, "CitadelEncounter", "Game.Modules.AetherfallRules.EncounterState", properties =>
                properties["activeZone"] = "resonance");
            foreach (var enemy in entities.Where(entity => HasTag(entity, "zone.resonance")))
            {
                var enemyState = enemy?["components"]?.AsArray()
                    .FirstOrDefault(component => ReadString(component, "type") == "Game.Modules.AetherfallRules.EnemyState");
                if (enemyState is not null)
                {
                    enemyState["properties"]!["health"] = 0;
                    enemyState["properties"]!["active"] = false;
                    enemyState["properties"]!["phase"] = "defeated";
                }
            }
        });
        await BuildRulesAsync(projectRoot);

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            1,
            [new RekallAgeRuntimeInputFrame { DeltaSeconds = 0.1 }],
            CancellationToken.None);
        var warden = world.Entities.Single(entity => entity.Name == "AetherWarden");
        var encounter = world.Entities.Single(entity => entity.Name == "CitadelEncounter");
        var guardian = world.Entities.Single(entity => entity.Name == "CitadelGuardian");
        var observatoryGate = world.Entities.Single(entity => entity.Name == "ObservatoryGate");

        var encounterState = encounter.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.EncounterState");
        Assert.Equal(0, encounterState.Properties["remainingEnemies"]!.GetValue<double>());
        Assert.Equal("observatory", warden.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.WardenState").Properties["objectivePhase"]!.GetValue<string>());
        Assert.Equal("observatory", encounterState.Properties["activeZone"]!.GetValue<string>());
        Assert.Equal("shielded", guardian.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.GuardianState").Properties["stage"]!.GetValue<string>());
        Assert.False(observatoryGate.Visible);
    }

    [Fact]
    public async Task WardenPulseBreaksGuardianShieldIntoVulnerableStage()
    {
        var projectRoot = CreateScenarioProject(scene =>
        {
            var entities = scene["entities"]!.AsArray();
            SetComponentProperties(entities, "AetherWarden", "Rekall.Transform3D", properties => properties["z"] = 32);
            SetComponentProperties(entities, "AetherWarden", "Game.Modules.AetherfallRules.WardenState", properties =>
            {
                properties["spawnZ"] = 32;
                properties["objectivePhase"] = "observatory";
            });
            SetComponentProperties(entities, "CitadelEncounter", "Game.Modules.AetherfallRules.EncounterState", properties =>
                properties["activeZone"] = "observatory");
            SetComponentProperties(entities, "CitadelGuardian", "Game.Modules.AetherfallRules.GuardianState", properties =>
            {
                properties["stage"] = "shielded";
                properties["shield"] = 20;
                properties["vulnerable"] = false;
            });
        });
        await BuildRulesAsync(projectRoot);
        var inputs = Enumerable.Range(0, 6)
            .Select(frame => new RekallAgeRuntimeInputFrame(
                SemanticActions: frame == 0
                    ? [new("ability.pulse", 1, IsDown: true, WasPressed: true)]
                    : []) { DeltaSeconds = 0.1 })
            .ToArray();

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            inputs.Length,
            inputs,
            CancellationToken.None);
        var guardian = world.Entities.Single(entity => entity.Name == "CitadelGuardian");
        var state = guardian.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.GuardianState");

        Assert.Equal(0, state.Properties["shield"]!.GetValue<double>());
        Assert.True(state.Properties["vulnerable"]!.GetValue<bool>());
        Assert.Equal("vulnerable", state.Properties["stage"]!.GetValue<string>());
    }

    [Fact]
    public async Task DefeatingVulnerableGuardianCompletesRunAndOpensCore()
    {
        var projectRoot = CreateScenarioProject(scene =>
        {
            var entities = scene["entities"]!.AsArray();
            SetComponentProperties(entities, "AetherWarden", "Rekall.Transform3D", properties => properties["z"] = 32);
            SetComponentProperties(entities, "AetherWarden", "Game.Modules.AetherfallRules.WardenState", properties =>
            {
                properties["spawnZ"] = 32;
                properties["objectivePhase"] = "observatory";
            });
            SetComponentProperties(entities, "CitadelEncounter", "Game.Modules.AetherfallRules.EncounterState", properties =>
                properties["activeZone"] = "observatory");
            SetComponentProperties(entities, "CitadelGuardian", "Game.Modules.AetherfallRules.GuardianState", properties =>
            {
                properties["stage"] = "vulnerable";
                properties["shield"] = 0;
                properties["health"] = 20;
                properties["vulnerable"] = true;
            });
        });
        await BuildRulesAsync(projectRoot);
        var inputs = Enumerable.Range(0, 6)
            .Select(frame => new RekallAgeRuntimeInputFrame(
                SemanticActions: frame == 0
                    ? [new("ability.pulse", 1, IsDown: true, WasPressed: true)]
                    : []) { DeltaSeconds = 0.1 })
            .ToArray();

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            inputs.Length,
            inputs,
            CancellationToken.None);
        var warden = world.Entities.Single(entity => entity.Name == "AetherWarden");
        var encounter = world.Entities.Single(entity => entity.Name == "CitadelEncounter");
        var coreGate = world.Entities.Single(entity => entity.Name == "ObservatoryCoreGate");

        Assert.Equal("victory", warden.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.WardenState").Properties["phase"]!.GetValue<string>());
        Assert.True(encounter.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.EncounterState").Properties["completed"]!.GetValue<bool>());
        Assert.False(coreGate.Visible);
    }

    [Fact]
    public async Task ActiveGuardianEmitsRadialAttackPattern()
    {
        var projectRoot = CreateScenarioProject(scene =>
        {
            var entities = scene["entities"]!.AsArray();
            SetComponentProperties(entities, "AetherWarden", "Game.Modules.AetherfallRules.WardenState", properties =>
                properties["objectivePhase"] = "observatory");
            SetComponentProperties(entities, "CitadelGuardian", "Game.Modules.AetherfallRules.GuardianState", properties =>
            {
                properties["stage"] = "shielded";
                properties["attackClock"] = 0;
            });
        });
        await BuildRulesAsync(projectRoot);

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            1,
            [new RekallAgeRuntimeInputFrame { DeltaSeconds = 0.1 }],
            CancellationToken.None);

        Assert.Equal(8, world.Entities.Count(entity => entity.Tags.Contains("guardian.projectile")));
    }

    [Fact]
    public async Task ZeroIntegrityTransitionsRunToDefeat()
    {
        var projectRoot = CreateScenarioProject(scene =>
        {
            var entities = scene["entities"]!.AsArray();
            SetComponentProperties(entities, "AetherWarden", "Rekall.Transform3D", properties =>
            {
                properties["x"] = 7;
                properties["z"] = 13;
            });
            SetComponentProperties(entities, "AetherWarden", "Game.Modules.AetherfallRules.WardenState", properties =>
            {
                properties["integrity"] = 10;
                properties["objectivePhase"] = "resonance";
            });
            SetComponentProperties(entities, "CourtOrbiter01", "Game.Modules.AetherfallRules.HazardState", properties =>
            {
                properties["motionKind"] = "linear";
                properties["originX"] = 7;
                properties["originZ"] = 13;
                properties["amplitude"] = 0;
                properties["speed"] = 0;
            });
        });
        await BuildRulesAsync(projectRoot);

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            1,
            [new RekallAgeRuntimeInputFrame { DeltaSeconds = 0.1 }],
            CancellationToken.None);
        var warden = world.Entities.Single(entity => entity.Name == "AetherWarden");
        var state = warden.Components.Single(c => c.Type == "Game.Modules.AetherfallRules.WardenState");

        Assert.Equal(0, state.Properties["integrity"]!.GetValue<double>());
        Assert.Equal("defeat", state.Properties["phase"]!.GetValue<string>());
    }

    private static bool HasTag(JsonNode? entity, string expected) =>
        entity?["tags"] is JsonArray tags
        && tags.Any(tag => string.Equals(tag?.GetValue<string>(), expected, StringComparison.Ordinal));

    private static string? ReadString(JsonNode? node, string propertyName) =>
        node?[propertyName]?.GetValue<string>();

    private static bool ReadBoolean(JsonNode? component, string propertyName) =>
        component?["properties"]?[propertyName]?.GetValue<bool>() == true;

    private static JsonObject LoadMainScene() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Examples",
            "AetherfallCitadel",
            "Scenes",
            "Main.age.scene.json")))!.AsObject();

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

    private static IEnumerable<RekallAgeRuntimeInputFrame> MovementFrames(
        int count,
        double horizontal,
        double vertical) =>
        Enumerable.Range(0, count).Select(_ => new RekallAgeRuntimeInputFrame(
            SemanticActions:
            [
                new("move.horizontal", horizontal, IsDown: true),
                new("move.vertical", vertical, IsDown: true)
            ]) { DeltaSeconds = 0.1 });

    private static string CreateScenarioProject(Action<JsonObject> mutateScene)
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var destinationRoot = TestPaths.CreateTempDirectory();
        File.Copy(Path.Combine(sourceRoot, "rekall.project.json"), Path.Combine(destinationRoot, "rekall.project.json"));
        Directory.CreateDirectory(Path.Combine(destinationRoot, "Scenes"));
        Directory.CreateDirectory(Path.Combine(destinationRoot, "Modules", "AetherfallRules"));
        foreach (var source in Directory.GetFiles(Path.Combine(sourceRoot, "Modules", "AetherfallRules")))
        {
            if (Path.GetExtension(source) is ".cs" or ".csproj")
            {
                File.Copy(source, Path.Combine(destinationRoot, "Modules", "AetherfallRules", Path.GetFileName(source)));
            }
        }

        var scene = LoadMainScene();
        mutateScene(scene);
        File.WriteAllText(
            Path.Combine(destinationRoot, "Scenes", "Main.age.scene.json"),
            scene.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return destinationRoot;
    }

    private static void SetComponentProperties(
        JsonArray entities,
        string entityName,
        string componentType,
        Action<JsonObject> mutate)
    {
        var entity = entities.Single(node => ReadString(node, "name") == entityName)!;
        var component = entity["components"]!.AsArray()
            .Single(node => ReadString(node, "type") == componentType)!;
        mutate(component["properties"]!.AsObject());
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
