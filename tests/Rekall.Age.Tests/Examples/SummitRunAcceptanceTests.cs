using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Sdk;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Examples;

[CollectionDefinition("Summit Run acceptance", DisableParallelization = true)]
public sealed class SummitRunAcceptanceCollection;

[Collection("Summit Run acceptance")]
public sealed class SummitRunAcceptanceTests
{
    [Fact]
    public void MainSceneExposesACompleteInspectable2DVehicleContract()
    {
        var scene = LoadScene();
        var entities = scene["entities"]!.AsArray();
        var components = entities.SelectMany(entity => entity!["components"]!.AsArray()).ToArray();

        Assert.True(entities.Count >= 22);
        Assert.Contains(components, component => Type(component) == "Rekall.Camera2D");
        Assert.Contains(components, component => Type(component) == "Rekall.UiCanvas");
        Assert.Contains(components, component => Type(component) == "Rekall.InputActionMap");
        Assert.Contains(components, component => Type(component) == "Game.Modules.SummitRun.SummitRunState");
        Assert.Equal(3, components.Count(component => Type(component) == "Rekall.Rigidbody2D"));
        Assert.Equal(2, components.Count(component => Type(component) == "Rekall.HingeJoint"));
        Assert.Equal(6, components.Count(component => Type(component) == "Rekall.BoxCollider2D"
            && entities.Any(entity => entity!["components"]!.AsArray().Contains(component)
                && entity!["name"]!.GetValue<string>().StartsWith("Terrain", StringComparison.Ordinal))));
    }

    [Fact]
    public async Task SemanticDriveInputPropelsThePhysicsVehicleAndUpdatesGameState()
    {
        var projectRoot = ProjectRoot();
        await new RekallAgeModuleSdkInstaller().InstallAsync(projectRoot, CancellationToken.None);
        var build = await new BuildModulesCommand().ExecuteAsync(
            new BuildModulesRequest(projectRoot),
            new RekallAgeCommandContext(
                "test",
                RekallAgeTransaction.Begin("build Summit Run rules"),
                CancellationToken.None));
        Assert.True(build.Ok, build.Summary);

        var inputs = Enumerable.Range(0, 180)
            .Select(_ => new RekallAgeRuntimeInputFrame(
                SemanticActions: [new("drive", 1, IsDown: true)])
            {
                DeltaSeconds = 1.0 / 60.0
            })
            .ToArray();
        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            inputs.Length,
            inputs,
            CancellationToken.None);
        var rover = world.Entities.Single(entity => entity.Name == "Rover");
        var state = rover.Components.Single(component =>
            component.Type == "Game.Modules.SummitRun.SummitRunState");

        Assert.Contains("SummitRunSystem", world.SystemsRun);
        Assert.True(rover.Transform.Position2D.X > 3, $"Expected the rover to climb rightward, found X={rover.Transform.Position2D.X:0.000}.");
        Assert.True(state.Properties["Distance"]!.GetValue<double>() > 2);
        Assert.Equal("driving", state.Properties["Status"]!.GetValue<string>());
        Assert.Contains(rover.Components, component => component.Type == "Rekall.PhysicsState2D");
        Assert.DoesNotContain(world.Observations, observation => observation.Code == "runtime.physics.joint_unresolved");
    }

    [Fact]
    public async Task ResetRestoresVehicleMotionGameStateAndCollectedCellVisibility()
    {
        var projectRoot = ProjectRoot();
        await new RekallAgeModuleSdkInstaller().InstallAsync(projectRoot, CancellationToken.None);
        var build = await new BuildModulesCommand().ExecuteAsync(
            new BuildModulesRequest(projectRoot),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("build Summit Run reset"), CancellationToken.None));
        Assert.True(build.Ok, build.Summary);

        var inputs = Enumerable.Range(0, 360)
            .Select(_ => new RekallAgeRuntimeInputFrame(SemanticActions: [new("drive", 1, IsDown: true)]) { DeltaSeconds = 1.0 / 60.0 })
            .Append(new RekallAgeRuntimeInputFrame(SemanticActions: [new("reset", 1, IsDown: true, WasPressed: true)]) { DeltaSeconds = 1.0 / 60.0 })
            .ToArray();
        var drivenWorld = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot, "Main", inputs.Length - 1, inputs[..^1], CancellationToken.None);
        Assert.Contains(drivenWorld.Entities.Where(entity => entity.Tags.Contains("cell")), cell =>
            !cell.Visible
            && cell.Components.Single(component => component.Type == "Game.Modules.SummitRun.CellState")
                .Properties["Collected"]!.GetValue<bool>());
        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot, "Main", inputs.Length, inputs, CancellationToken.None);
        var rover = world.Entities.Single(entity => entity.Name == "Rover");
        var wheels = world.Entities.Where(entity => entity.Tags.Contains("wheel")).ToArray();
        var state = rover.Components.Single(component => component.Type == "Game.Modules.SummitRun.SummitRunState");
        var physics = rover.Components.Single(component => component.Type == "Rekall.PhysicsState2D");
        var linearVelocity = physics.Properties["linearVelocity"]!.AsObject();
        var angularVelocity = physics.Properties["angularVelocity"]!.AsObject();

        Assert.InRange(rover.Transform.Position2D.X, 0.7, 1.3);
        Assert.InRange(rover.Transform.Position2D.Y, 5.5, 6.3);
        Assert.All(wheels, wheel => Assert.InRange(wheel.Transform.Position2D.Y, 4.8, 5.6));
        Assert.InRange(Math.Abs(ReadNumber(linearVelocity["x"])), 0, 3);
        Assert.InRange(Math.Abs(ReadNumber(linearVelocity["y"])), 0, 3);
        Assert.InRange(Math.Abs(ReadNumber(angularVelocity["z"])), 0, 30);
        Assert.All(wheels, wheel =>
        {
            var wheelPhysics = wheel.Components.Single(component => component.Type == "Rekall.PhysicsState2D").Properties;
            Assert.InRange(Math.Abs(ReadNumber(wheelPhysics["linearVelocity"]!["x"])), 0, 3);
            Assert.InRange(Math.Abs(ReadNumber(wheelPhysics["linearVelocity"]!["y"])), 0, 3);
            Assert.InRange(Math.Abs(ReadNumber(wheelPhysics["angularVelocity"]!["z"])), 0, 30);
        });
        Assert.Equal(1, state.Properties["Fuel"]!.GetValue<double>(), 6);
        Assert.Equal(0, state.Properties["RoverVel"]!.GetValue<double>(), 6);
        Assert.Equal(0, state.Properties["Cells"]!.GetValue<double>(), 6);
        Assert.Equal(0, state.Properties["Distance"]!.GetValue<double>(), 6);
        Assert.Equal("ready", state.Properties["Status"]!.GetValue<string>());
        Assert.Equal(1, state.Properties["ResetCount"]!.GetValue<double>());
        Assert.All(world.Entities.Where(entity => entity.Tags.Contains("cell")), cell =>
        {
            Assert.True(cell.Visible);
            Assert.False(cell.Components.Single(component => component.Type == "Game.Modules.SummitRun.CellState")
                .Properties["Collected"]!.GetValue<bool>());
        });
    }

    [Fact]
    public async Task CellAtTheSameXButDistantYIsNotCollected()
    {
        var projectRoot = CopyProjectSource();
        var scenePath = Path.Combine(projectRoot, "Scenes", "Main.age.scene.json");
        var scene = JsonNode.Parse(await File.ReadAllTextAsync(scenePath))!.AsObject();
        var cell = scene["entities"]!.AsArray().Single(entity => entity!["name"]!.GetValue<string>() == "CellA")!;
        var transform = cell["components"]!.AsArray().Single(component => Type(component) == "Rekall.Transform2D")!;
        transform["properties"]!["X"] = 1;
        transform["properties"]!["Y"] = 100;
        await File.WriteAllTextAsync(scenePath, scene.ToJsonString(new() { WriteIndented = true }));
        await new RekallAgeModuleSdkInstaller().InstallAsync(projectRoot, CancellationToken.None);
        var build = await new BuildModulesCommand().ExecuteAsync(
            new BuildModulesRequest(projectRoot),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("build vertical collection proof"), CancellationToken.None));
        Assert.True(build.Ok, build.Summary);

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot,
            "Main",
            2,
            [
                new RekallAgeRuntimeInputFrame { DeltaSeconds = 1.0 / 60.0 },
                new RekallAgeRuntimeInputFrame { DeltaSeconds = 1.0 / 60.0 }
            ],
            CancellationToken.None);
        var runtimeCell = world.Entities.Single(entity => entity.Name == "CellA");
        var roverState = world.Entities.Single(entity => entity.Name == "Rover").Components
            .Single(component => component.Type == "Game.Modules.SummitRun.SummitRunState");

        Assert.True(runtimeCell.Visible);
        Assert.False(runtimeCell.Components.Single(component => component.Type == "Game.Modules.SummitRun.CellState")
            .Properties["Collected"]!.GetValue<bool>());
        Assert.Equal(0, roverState.Properties["Cells"]!.GetValue<double>());
    }

    private static JsonObject LoadScene() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectRoot(), "Scenes", "Main.age.scene.json")))!.AsObject();

    private static string? Type(JsonNode? component) => component?["type"]?.GetValue<string>();

    private static double ReadNumber(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<double>(out var number)) return number;
        if (node is JsonValue single && single.TryGetValue<float>(out var floatNumber)) return floatNumber;
        throw new InvalidOperationException($"Expected a numeric JSON value, found {node?.ToJsonString() ?? "null"}.");
    }

    private static string ProjectRoot() => Path.Combine(FindRepositoryRoot(), "Examples", "SummitRun");

    private static string CopyProjectSource()
    {
        var destination = TestPaths.CreateTempDirectory();
        foreach (var sourcePath in Directory.EnumerateFiles(ProjectRoot(), "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(ProjectRoot(), sourcePath);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(segment => segment is "bin" or "obj" or ".rekall" or "Transactions" or "Artifacts"))
            {
                continue;
            }
            var targetPath = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath);
        }
        return destination;
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
