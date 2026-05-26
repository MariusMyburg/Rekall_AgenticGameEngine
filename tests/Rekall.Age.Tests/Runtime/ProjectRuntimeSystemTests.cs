using System.Text.Json.Nodes;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
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

    [Fact]
    public async Task AgentAuthoredModuleCanProjectCustomPlanetComponentIntoRenderablePlanet()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("agent planet module"), CancellationToken.None);
        var scaffold = await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(root, "agent.planets", "Agent Planets", "AgentPlanets", "PlanetSurface"),
            context);
        var write = await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, "AgentPlanets", "AgentPlanetsModule.cs", CreateAgentPlanetModuleSource()),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        await SaveAgentPlanetSceneAsync(root);

        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(write.Ok, write.Summary);
        Assert.True(build.Ok, build.Summary);

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(root, "Main", 1, CancellationToken.None);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        Assert.Contains("AgentPlanetProjectionSystem", world.SystemsRun);
        var renderable = Assert.Single(frame.Renderables, item => item.EntityName == "Authored Gaia");
        Assert.Equal("mesh", renderable.Kind);
        Assert.Equal("rekall.planet.surface", renderable.Variant);
        Assert.Equal("asset_agent_earth", renderable.TextureAssetId);
        Assert.Equal("#2277cc", renderable.MaterialColor);
        Assert.Equal(12.8, renderable.ScaleX);
        Assert.Equal(12.8, renderable.ScaleY);
        Assert.Equal(12.8, renderable.ScaleZ);
    }

    [Fact]
    public async Task AgentAuthoredRuntimeSystemCanUseModuleSdkForGenericMovementAndSpatialEffects()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("agent-authored gameplay sdk"), CancellationToken.None);
        var scaffold = await new ScaffoldRuntimeSystemModuleCommand().ExecuteAsync(
            new ScaffoldRuntimeSystemModuleRequest(
                root,
                "agent.authored-gameplay",
                "Agent Authored Gameplay",
                "AgentAuthoredGameplay",
                "AgentMotor",
                "AgentAuthoredGameplaySystem"),
            context);
        var write = await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(
                root,
                "AgentAuthoredGameplay",
                "AgentAuthoredGameplayModule.cs",
                CreateAgentAuthoredGameplayModuleSource()),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        await SaveAgentAuthoredGameplaySceneAsync(root);

        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(write.Ok, write.Summary);
        Assert.True(build.Ok, build.Summary);

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(root, "Main", 60, CancellationToken.None);
        var actor = world.Entities.Single(entity => entity.Name == "Agent Actor");
        var target = world.Entities.Single(entity => entity.Name == "Target Dummy");
        var resource = target.Components.Single(component =>
            component.Type == "Game.Modules.AgentAuthoredGameplay.VitalResource");

        Assert.Contains("AgentAuthoredGameplaySystem", world.SystemsRun);
        Assert.True(actor.Transform.Position3D.Z > 4.9);
        Assert.Equal(50, resource.Properties["current"]!.GetValue<double>());
        Assert.Contains(target.Components, component =>
            component.Type == "Game.Modules.AgentAuthoredGameplay.LastEffect"
            && component.Properties["source"]!.GetValue<string>() == "Agent Actor");
    }

    [Fact]
    public async Task ProjectRuntimeSystemReceivesRuntimeInputState()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("project runtime input"), CancellationToken.None);
        var scaffold = await new ScaffoldRuntimeSystemModuleCommand().ExecuteAsync(
            new ScaffoldRuntimeSystemModuleRequest(
                root,
                "agent.input-driven",
                "Agent Input Driven",
                "AgentInputDriven",
                "InputMotor",
                "InputDrivenSystem"),
            context);
        var write = await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(
                root,
                "AgentInputDriven",
                "AgentInputDrivenModule.cs",
                CreateInputDrivenModuleSource()),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        await SaveInputDrivenSceneAsync(root);

        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(write.Ok, write.Summary);
        Assert.True(build.Ok, build.Summary);

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            root,
            "Main",
            1,
            [
                new RekallAgeRuntimeInputFrame(
                    PressedKeys: ["W"],
                    PressedKeysThisFrame: ["W"])
            ],
            CancellationToken.None);
        var actor = world.Entities.Single(entity => entity.Name == "Input Actor");

        Assert.Contains("InputDrivenSystem", world.SystemsRun);
        Assert.Equal(1, actor.Transform.Position3D.Z, precision: 3);
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

    private static async Task SaveAgentPlanetSceneAsync(string root)
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Authored Gaia", ["planet"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Game.Modules.AgentPlanets.PlanetSurface",
                    new JsonObject
                    {
                        ["radius"] = 6.4,
                        ["surfaceTexture"] = "asset_agent_earth",
                        ["color"] = "#2277cc"
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 1, ["y"] = 2, ["z"] = 3 })))
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("KeyLight", ["light"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["pitch"] = -35, ["yaw"] = -45 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.DirectionalLight",
                    new JsonObject { ["intensity"] = 1.0 })));
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

    private static async Task SaveAgentAuthoredGameplaySceneAsync(string root)
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Agent Actor", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0, ["yaw"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Game.Modules.AgentAuthoredGameplay.AgentMotor",
                    new JsonObject { ["moveZPerSecond"] = 5, ["emitEffect"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Target Dummy", ["target"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 8 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["radius"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Game.Modules.AgentAuthoredGameplay.VitalResource",
                    new JsonObject { ["current"] = 100, ["max"] = 100 })))
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
    }

    private static async Task SaveInputDrivenSceneAsync(string root)
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "input"])
            .AddEntity(RekallAgeEntityDocument.Create("Input Actor", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Game.Modules.AgentInputDriven.InputMotor",
                    new JsonObject { ["moveZPerFrame"] = 1 })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
    }

    private static string CreateInputDrivenModuleSource()
    {
        return """
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.AgentInputDriven;

[RekallAgeModule("agent.input-driven", "Agent Input Driven")]
[RekallAgeRequiresCapability("world")]
public sealed class AgentInputDrivenModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<InputMotor>();
        builder.RegisterRuntimeSystem<InputDrivenSystem>();
    }
}

[RekallAgeComponent("Input Motor")]
public sealed class InputMotor : RekallAgeComponent
{
    [RekallAgeProperty]
    public double MoveZPerFrame { get; init; } = 1;
}

public sealed class InputDrivenSystem : IRekallAgeRuntimeModuleSystem
{
    private const string MotorType = "Game.Modules.AgentInputDriven.InputMotor";

    public string Id => nameof(InputDrivenSystem);

    public int Priority => -10;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        if (context.Input.PressedKeys?.Contains("W") != true)
        {
            return ValueTask.FromResult(world);
        }

        var entities = world.Entities.Select(entity =>
        {
            var motor = entity.FindComponent(MotorType);
            if (motor is null)
            {
                return entity;
            }

            var move = motor.Properties.ReadNumber("moveZPerFrame", 1);
            return entity.WithPosition3D(entity.Transform.Position3D with
            {
                Z = entity.Transform.Position3D.Z + move
            });
        }).ToArray();
        return ValueTask.FromResult(world with { Entities = entities });
    }
}
""";
    }

    private static string CreateAgentAuthoredGameplayModuleSource()
    {
        return """
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;
using System.Text.Json.Nodes;

namespace Game.Modules.AgentAuthoredGameplay;

[RekallAgeModule("agent.authored-gameplay", "Agent Authored Gameplay")]
[RekallAgeRequiresCapability("world")]
public sealed class AgentAuthoredGameplayModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<AgentMotor>();
        builder.RegisterComponent<VitalResource>();
        builder.RegisterComponent<LastEffect>();
        builder.RegisterRuntimeSystem<AgentAuthoredGameplaySystem>();
    }
}

[RekallAgeComponent("Agent Motor")]
public sealed class AgentMotor : RekallAgeComponent
{
    [RekallAgeProperty]
    public double MoveZPerSecond { get; init; } = 5;

    [RekallAgeProperty]
    public bool EmitEffect { get; init; } = true;
}

[RekallAgeComponent("Vital Resource")]
public sealed class VitalResource : RekallAgeComponent
{
    [RekallAgeProperty]
    public double Current { get; init; } = 100;

    [RekallAgeProperty]
    public double Max { get; init; } = 100;
}

[RekallAgeComponent("Last Effect")]
public sealed class LastEffect : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Source { get; init; } = "";
}

public sealed class AgentAuthoredGameplaySystem : IRekallAgeRuntimeModuleSystem
{
    private const string MotorType = "Game.Modules.AgentAuthoredGameplay.AgentMotor";
    private const string ResourceType = "Game.Modules.AgentAuthoredGameplay.VitalResource";

    public string Id => nameof(AgentAuthoredGameplaySystem);

    public int Priority => -10;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var seconds = context.DeltaTime.TotalSeconds;
        var entities = world.Entities.ToArray();
        for (var i = 0; i < entities.Length; i++)
        {
            var actor = entities[i];
            var motor = actor.FindComponent(MotorType);
            if (motor is null)
            {
                continue;
            }

            var move = motor.Properties.ReadNumber("moveZPerSecond", 0) * seconds;
            actor = actor.WithPosition3D(actor.Transform.Position3D with { Z = actor.Transform.Position3D.Z + move });
            entities[i] = actor;

            if (!motor.Properties.ReadBoolean("emitEffect", false))
            {
                continue;
            }

            var hit = (world with { Entities = entities })
                .Raycast3D(actor.Transform.Position3D, new RekallAgeRuntimeVector3(0, 0, 1), 20, tag: "target")
                .FirstOrDefault();
            if (hit is null)
            {
                continue;
            }

            var targetIndex = Array.FindIndex(entities, entity => entity.Id == hit.Entity.Id);
            if (targetIndex < 0)
            {
                continue;
            }

            entities[targetIndex] = entities[targetIndex]
                .UpdateComponent(ResourceType, properties =>
                {
                    properties["current"] = properties.ReadNumber("current", 0) - 50;
                    return properties;
                })
                .UpsertComponent(
                    "Game.Modules.AgentAuthoredGameplay.LastEffect",
                    new JsonObject { ["source"] = actor.Name });
            entities[i] = actor.UpdateComponent(MotorType, properties =>
            {
                properties["emitEffect"] = false;
                return properties;
            });
        }

        return ValueTask.FromResult(world with { Entities = entities });
    }
}
""";
    }

    private static string CreateAgentPlanetModuleSource()
    {
        return """
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;
using System.Text.Json.Nodes;

namespace Game.Modules.AgentPlanets;

[RekallAgeModule("agent.planets", "Agent Planets")]
[RekallAgeRequiresCapability("world")]
public sealed class AgentPlanetsModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<PlanetSurface>();
        builder.RegisterRuntimeSystem<AgentPlanetProjectionSystem>();
    }
}

[RekallAgeComponent("Planet Surface")]
public sealed class PlanetSurface : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 1;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? SurfaceTexture { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#4b86d8";
}

public sealed class AgentPlanetProjectionSystem : IRekallAgeRuntimeModuleSystem
{
    public string Id => nameof(AgentPlanetProjectionSystem);

    public int Priority => -20;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        const string componentType = "Game.Modules.AgentPlanets.PlanetSurface";
        var meshes = world.Subsystems.Rendering.Meshes.ToList();
        var entities = world.Entities.Select(entity =>
        {
            var component = entity.Components.FirstOrDefault(item => item.Type == componentType);
            if (component is null)
            {
                return entity;
            }

            var radius = Math.Max(0.0001, ReadNumber(component.Properties, "radius", 1));
            meshes.Add(new RekallAgeRuntimeRenderMesh(
                entity.Id,
                entity.Name,
                "rekall.planet.surface",
                Variant: "rekall.planet.surface",
                TextureAssetId: ReadString(component.Properties, "surfaceTexture"),
                MaterialColor: ReadString(component.Properties, "color")));
            var transform = entity.Transform;
            return entity with
            {
                Transform = transform with
                {
                    Scale3D = new RekallAgeRuntimeVector3(radius * 2, radius * 2, radius * 2)
                }
            };
        }).ToArray();

        return ValueTask.FromResult(world with
        {
            Entities = entities,
            Subsystems = world.Subsystems with
            {
                Rendering = world.Subsystems.Rendering with
                {
                    Meshes = meshes
                }
            }
        });
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return properties.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
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
