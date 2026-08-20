using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.TumblingCubes;

[RekallAgeModule("example.tumbling_cubes", "Tumbling Cubes")]
[RekallAgeRequiresCapability("world")]
[RekallAgeRequiresCapability("rendering3d")]
[RekallAgeRequiresCapability("physics3d")]
public sealed class TumblingCubesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<SpawnState>();
        builder.RegisterRuntimeSystem<FallingCubeSpawnerSystem>();
    }
}

[RekallAgeComponent("Spawn State")]
public sealed class SpawnState : RekallAgeComponent
{
    [RekallAgeProperty] public bool Enabled { get; init; } = true;
    [RekallAgeProperty(Minimum = 0.1)] public double IntervalSeconds { get; init; } = 2;
    [RekallAgeProperty] public int Seed { get; init; } = 4201;
}

public sealed class FallingCubeSpawnerSystem : IRekallAgeRuntimeModuleSystem
{
    private const string ComponentType = "Game.Modules.TumblingCubes.SpawnState";
    private static readonly string[] Colors = ["#ff6b6b", "#4ecdc4", "#ffe66d", "#5f8cff", "#c77dff"];
    private long _spawnIndex;
    private double _nextSpawnSeconds;

    public string Id => nameof(FallingCubeSpawnerSystem);
    public int Priority => -100;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var controller = world.EntitiesWithComponent(ComponentType).FirstOrDefault();
        if (controller is null || !controller.ComponentBoolean(ComponentType, "enabled", true))
        {
            return ValueTask.FromResult(world);
        }

        var interval = Math.Max(0.1, controller.ComponentNumber(ComponentType, "intervalSeconds", 2));
        var seed = (int)controller.ComponentNumber(ComponentType, "seed", 4201);
        if (context.ElapsedTime.TotalSeconds + 0.000001 < _nextSpawnSeconds)
        {
            return ValueTask.FromResult(world);
        }

        var index = _spawnIndex++;
        _nextSpawnSeconds = context.ElapsedTime.TotalSeconds + interval;
        var id = $"falling_cube_{index:D4}";
        var x = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, index * 7, -1.3, 1.3);
        var z = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, index * 7 + 1, -0.8, 0.8);
        var pitch = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, index * 7 + 2, -180, 180);
        var yaw = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, index * 7 + 3, -180, 180);
        var roll = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, index * 7 + 4, -180, 180);

        var cube = RekallAgeRuntimeModuleSdk.CreateEntity(id, $"Falling Cube {index + 1}")
            .WithTag("falling-block")
            .WithPosition3D(new RekallAgeRuntimeVector3(x, 7.5, z))
            .WithRotation3D(new RekallAgeRuntimeVector3(pitch, yaw, roll))
            .WithComponentString("Rekall.GeometryPrimitive", "primitive", "cube")
            .WithComponentString("Rekall.GeometryPrimitive", "color", Colors[index % Colors.Length])
            .WithComponentString("Rekall.MeshRenderer", "mesh", "rekall.geometry.cube")
            .WithComponentNumber("Rekall.Rigidbody3D", "mass", 1)
            .WithComponentNumber("Rekall.BoxCollider3D", "width", 1)
            .WithComponentNumber("Rekall.BoxCollider3D", "height", 1)
            .WithComponentNumber("Rekall.BoxCollider3D", "depth", 1)
            .WithComponentNumber("Rekall.PhysicsMaterial3D", "friction", 0.62)
            .WithComponentNumber("Rekall.PhysicsMaterial3D", "restitution", 0.18)
            .WithComponentNumber("Rekall.PhysicsMaterial3D", "springFrequency", 24)
            .WithComponentNumber("Rekall.PhysicsMaterial3D", "dampingRatio", 0.75);

        return ValueTask.FromResult(world.AddEntity(cube));
    }
}
