using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Sdk;
using Rekall.Age.Runtime;

namespace Rekall.Age.Tests.Examples;

[CollectionDefinition("Crater Field acceptance", DisableParallelization = true)]
public sealed class CraterFieldAcceptanceCollection;

[Collection("Crater Field acceptance")]
public sealed class CraterFieldAcceptanceTests
{
    [Fact]
    public void MainSceneExposesTheAuthoredDestructionRig()
    {
        var scene = LoadMainScene();
        var entities = scene["entities"]!.AsArray();
        var components = entities.SelectMany(entity => entity!["components"]!.AsArray()).ToArray();

        Assert.Contains(entities, entity => ReadString(entity, "name") == "Spawner");
        Assert.Contains(entities, entity => ReadString(entity, "name") == "Terrain");
        Assert.Contains(
            components,
            component => ReadString(component, "type") == "Game.Modules.CraterFieldRules.SpawnerState");
        Assert.Single(
            components,
            component => ReadString(component, "type") == "Rekall.Camera3D" && ReadBoolean(component, "active"));
        Assert.Contains(components, component => ReadString(component, "type") == "Rekall.DirectionalLight");

        var terrain = entities.Single(entity => ReadString(entity, "name") == "Terrain")!;
        Assert.NotNull(terrain["id"]);

        var spawner = entities.Single(entity => ReadString(entity, "name") == "Spawner")!;
        var spawnerState = spawner["components"]!.AsArray()
            .Single(component => ReadString(component, "type") == "Game.Modules.CraterFieldRules.SpawnerState")!;
        Assert.Equal(
            ReadString(terrain, "id"),
            ReadString(spawnerState["properties"], "terrainEntityId"));
    }

    [Fact]
    public void FiveChunkMeshAssetsAndAGrenadeBodyArePublished()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "CraterField");
        var meshesDirectory = Path.Combine(projectRoot, "Modeling", "Meshes");

        Assert.True(File.Exists(Path.Combine(meshesDirectory, "grenade-body.age.mesh.json")));
        Assert.True(File.Exists(Path.Combine(meshesDirectory, "terrain-ground.age.mesh.json")));
        for (var index = 0; index < 5; index++)
        {
            Assert.True(
                File.Exists(Path.Combine(meshesDirectory, $"grenade-chunk-{index}.age.mesh.json")),
                $"Missing grenade-chunk-{index} mesh asset.");
        }
    }

    [Fact]
    public async Task RulesModuleBuildsAndSpawnsAFuseCountedGrenade()
    {
        var projectRoot = await CreateScenarioProjectAsync();

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            1,
            CancellationToken.None);

        Assert.Contains("CraterFieldRulesSystem", world.SystemsRun);

        var spawner = world.Entities.Single(entity => entity.Name == "Spawner");
        var spawnerState = spawner.Components.Single(
            component => component.Type == "Game.Modules.CraterFieldRules.SpawnerState");
        Assert.True(spawnerState.Properties["elapsed"]!.GetValue<double>() > 0);
    }

    [Fact]
    public async Task GrenadeSpawnsDetonatesScattersChunksAndCratersTheTerrain()
    {
        var projectRoot = await CreateScenarioProjectAsync();

        // 3s spawn interval + 1.5s fuse at 60fps: run well past the first detonation.
        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            300,
            CancellationToken.None);

        var chunks = world.Entities.Where(entity => entity.Name.Contains("-chunk-", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            var rigidbody = chunk.Components.Single(component => component.Type == "Rekall.Rigidbody3D");
            var vx = rigidbody.Properties["linearVelocityX"]!.GetValue<double>();
            var vy = rigidbody.Properties["linearVelocityY"]!.GetValue<double>();
            var vz = rigidbody.Properties["linearVelocityZ"]!.GetValue<double>();
            Assert.True(Math.Sqrt(vx * vx + vy * vy + vz * vz) > 0);
            Assert.Single(chunk.Components, component => component.Type == "Rekall.Transform3D");
        });

        var terrainMeshPath = Path.Combine(
            projectRoot,
            "Modeling",
            "Meshes",
            "terrain-ground.age.mesh.json");
        var terrainMesh = JsonNode.Parse(await File.ReadAllTextAsync(terrainMeshPath))!.AsObject();
        var positions = terrainMesh["topology"]!["positions"]!.AsArray();
        Assert.Contains(positions, position => position!["y"]!.GetValue<double>() < 0);
    }

    private static bool ReadBoolean(JsonNode? component, string propertyName) =>
        component?["properties"]?[propertyName]?.GetValue<bool>() == true;

    private static string? ReadString(JsonNode? node, string propertyName) =>
        node?[propertyName]?.GetValue<string>();

    private static JsonObject LoadMainScene() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Examples",
            "CraterField",
            "Scenes",
            "Main.age.scene.json")))!.AsObject();

    private static async Task<string> CreateScenarioProjectAsync()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "Examples", "CraterField");
        var destinationRoot = TestPaths.CreateTempDirectory();

        File.Copy(
            Path.Combine(sourceRoot, "rekall.project.json"),
            Path.Combine(destinationRoot, "rekall.project.json"));
        CopyDirectory(Path.Combine(sourceRoot, "Scenes"), Path.Combine(destinationRoot, "Scenes"));
        CopyDirectory(Path.Combine(sourceRoot, "Modeling"), Path.Combine(destinationRoot, "Modeling"));
        Directory.CreateDirectory(Path.Combine(destinationRoot, "Modules", "CraterFieldRules"));
        foreach (var source in Directory.GetFiles(Path.Combine(sourceRoot, "Modules", "CraterFieldRules")))
        {
            if (Path.GetExtension(source) is ".cs" or ".csproj")
            {
                File.Copy(source, Path.Combine(destinationRoot, "Modules", "CraterFieldRules", Path.GetFileName(source)));
            }
        }

        await new RekallAgeModuleSdkInstaller().InstallAsync(destinationRoot, CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("build Crater Field rules"),
            CancellationToken.None);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(destinationRoot), context);
        Assert.True(build.Ok, build.Summary);
        Assert.Single(
            build.Value.Modules,
            module => module.ModuleName == "CraterFieldRules" && module.Succeeded);

        return destinationRoot;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
