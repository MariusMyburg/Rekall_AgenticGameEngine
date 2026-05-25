using System.Text.Json.Nodes;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class ProjectRuntimeSystemTests
{
    [Fact]
    public async Task RuntimeSnapshotRunsCompiledProjectRuntimeSystems()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("project runtime system"), CancellationToken.None);
        await CreateOrbitModuleAsync(root, context);
        await SaveOrbitSceneAsync(root, includeCameraAndLight: false);

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(root, "Main", 60, CancellationToken.None);
        var cube = world.Entities.Single(entity => entity.Name == "Cube");

        Assert.Equal(60, world.FrameIndex);
        Assert.Equal(2, cube.Transform.Position3D.X, precision: 3);
        Assert.Equal(90, cube.Transform.Rotation3D.Y, precision: 3);
        Assert.Equal("OrbitMotionSystem", Assert.Single(world.SystemsRun, system => system == "OrbitMotionSystem"));

        var inspect = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(root, "Main", 60),
            context);
        Assert.Contains("OrbitMotionSystem", inspect.Value.SystemsRun);
    }

    [Fact]
    public async Task RuntimeViewportCaptureUsesProjectRuntimeSystemOutput()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("project runtime capture"), CancellationToken.None);
        await CreateOrbitModuleAsync(root, context);
        await SaveOrbitSceneAsync(root, includeCameraAndLight: true);

        var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
            new CaptureRuntimeViewportRequest(root, "Main", 60, Path.Combine(root, "Viewport"), 220, 140, false),
            context);
        var output = await RekallAgePngReader.ReadRgbaAsync(result.Value.ScreenshotPath, CancellationToken.None);
        var cubePixelXs = Enumerable.Range(0, output.Rgba.Length / 4)
            .Where(pixel =>
            {
                var index = pixel * 4;
                return output.Rgba[index] >= 45
                    && output.Rgba[index + 1] >= 70
                    && output.Rgba[index + 2] >= 100;
            })
            .Select(pixel => pixel % output.Width)
            .ToArray();

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(0, result.Value.FallbackRenderableCount);
        Assert.True(cubePixelXs.Average() > output.Width / 2.0 + 18);
    }

    private static async Task CreateOrbitModuleAsync(string root, RekallAgeCommandContext context)
    {
        var scaffold = await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(root, "game.orbit", "Game Orbit", "GameOrbit", "OrbitMotion"),
            context);
        var write = await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, "GameOrbit", "GameOrbitModule.cs", CreateOrbitModuleSource()),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(write.Ok, write.Summary);
        Assert.True(build.Ok, build.Summary);
    }

    private static async Task SaveOrbitSceneAsync(string root, bool includeCameraAndLight)
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Game.Modules.GameOrbit.OrbitMotion",
                    new JsonObject { ["degreesPerSecond"] = 90, ["unitsPerSecond"] = 2 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["scaleX"] = 1.4, ["scaleY"] = 1.4, ["scaleZ"] = 1.4 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshRenderer",
                    new JsonObject { ["mesh"] = "rekall.primitive.cube" })));

        if (includeCameraAndLight)
        {
            scene = scene
                .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("KeyLight", ["light"])
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Rekall.Transform3D",
                        new JsonObject { ["pitch"] = -35, ["yaw"] = -45 }))
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Rekall.DirectionalLight",
                        new JsonObject { ["intensity"] = 1.0 })));
        }

        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
    }

    private static string CreateOrbitModuleSource()
    {
        return """
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;
using System.Text.Json.Nodes;

namespace Game.Modules.GameOrbit;

[RekallAgeModule("game.orbit", "Game Orbit")]
[RekallAgeRequiresCapability("world")]
public sealed class GameOrbitModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<OrbitMotion>();
        builder.RegisterRuntimeSystem<OrbitMotionSystem>();
    }
}

[RekallAgeComponent("Orbit Motion")]
public sealed class OrbitMotion : RekallAgeComponent
{
    [RekallAgeProperty]
    public double DegreesPerSecond { get; init; } = 90;

    [RekallAgeProperty]
    public double UnitsPerSecond { get; init; } = 2;
}

public sealed class OrbitMotionSystem : IRekallAgeRuntimeModuleSystem
{
    public string Id => nameof(OrbitMotionSystem);

    public int Priority => -10;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var seconds = context.DeltaTime.TotalSeconds;
        var entities = world.Entities.Select(entity =>
        {
            var orbit = entity.Components.FirstOrDefault(component =>
                component.Type == "Game.Modules.GameOrbit.OrbitMotion");
            if (orbit is null)
            {
                return entity;
            }

            var degrees = ReadNumber(orbit.Properties, "degreesPerSecond", 90);
            var units = ReadNumber(orbit.Properties, "unitsPerSecond", 2);
            var transform = entity.Transform;
            return entity with
            {
                Transform = transform with
                {
                    Position3D = new RekallAgeRuntimeVector3(
                        transform.Position3D.X + units * seconds,
                        transform.Position3D.Y,
                        transform.Position3D.Z),
                    Rotation3D = new RekallAgeRuntimeVector3(
                        transform.Rotation3D.X,
                        transform.Rotation3D.Y + degrees * seconds,
                        transform.Rotation3D.Z)
                }
            };
        }).ToArray();

        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        return properties.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<double>(out var number)
            ? number
            : fallback;
    }
}
""";
    }
}
