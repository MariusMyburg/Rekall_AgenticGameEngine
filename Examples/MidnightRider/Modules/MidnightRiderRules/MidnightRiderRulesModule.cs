using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.MidnightRiderRules;

[RekallAgeModule("Game.Modules.MidnightRiderRules", "Midnight Rider Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class MidnightRiderRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<RunState>();
        builder.RegisterRuntimeSystem<MidnightRiderSystem>();
    }
}

[RekallAgeComponent("Run State", Description = "Drives the motorbike infinite-runner: throttle/steering control, a rider-like balance controller on the front fork, and infinite procedural road/streetlight/hazard spawning ahead of the bike.")]
public sealed class RunState : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty]
    public bool Crashed { get; init; }

    [RekallAgeProperty]
    public double TargetSpeed { get; init; }

    [RekallAgeProperty]
    public double PreviousRoll { get; init; }

    [RekallAgeProperty]
    public double PreviousYaw { get; init; }

    [RekallAgeProperty]
    public double PreviousPitch { get; init; }

    [RekallAgeProperty]
    public double DistanceTraveled { get; init; }

    [RekallAgeProperty]
    public double NextSpawnX { get; init; }

    [RekallAgeProperty]
    public int NextChunkIndex { get; init; }

    [RekallAgeProperty]
    public int Seed { get; init; } = 1;

    [RekallAgeProperty]
    public string ChassisId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string ForkId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string RearWheelId { get; init; } = string.Empty;
}

/// <summary>
/// Priority -10 (before RekallAgeBepuPhysicsSystem's 0) so a motor target set this frame is
/// consumed by physics this same frame, the same convention Ridgebreaker's own vehicle system
/// established.
/// </summary>
public sealed class MidnightRiderSystem : IRekallAgeRuntimeModuleSystem
{
    private const string RunStateType = "Game.Modules.MidnightRiderRules.RunState";
    private const string HingeJointType = "Rekall.HingeJoint";

    // Tuned against a physics probe: chassis (mass 180) + fork (mass 4) + two capsule-tired
    // wheels (mass 3 each, radius 0.32) driven by real BEPU hinge motors - see
    // docs/production/PROGRESS.md's HingeJoint-axis-fix checkpoint for how this assembly was
    // arrived at, and docs/superpowers/specs for the balance-controller rationale.
    private const double WheelRadius = 0.32;
    private const double MaxSpeed = 22;
    private const double AccelerationPerSecond = 9;
    private const double MaxSteerAngleDegrees = 24;
    private const double MaxForkAngleDegrees = 30;
    private const double SteerAngleGain = 10;
    private const double MaxForkMotorDegreesPerSecond = 160;
    private const double BalanceProportionalGain = 2.4;
    private const double BalanceDerivativeGain = 0.35;
    // The straight-line version of this crash (chassis roll runs away to ~104 degrees around
    // frame 250-300 of a long unattended throttle-only run) is FIXED - see the Fork's HingeJoint
    // axis in Main.age.scene.json, raked 25 degrees off vertical instead of the perfectly
    // vertical axis it originally had (see docs/production/PROGRESS.md's final 2026-08-28
    // checkpoint). Straight-line travel now reaches ~101m before an eventual much-later crash,
    // versus ~75m with an early crash before the fix.
    //
    // Still open: turning destabilizes fast regardless of the rake fix - a throttle+steer test
    // crashes by frame ~180 (roll ~101-108 degrees), almost unchanged from before the rake fix.
    // Reducing MaxSteerAngleDegrees from 24 to 10 (a gentler commanded turn) did not help, ruling
    // out "the steer angle is too aggressive for the new geometry." This is a separate problem
    // from the straight-line weave and needs its own investigation, not an assumption it shares
    // the same cause. Everything below this point in the comment is the pre-rake-fix elimination
    // record, kept because most of it (balance controller, per-axis correction, angularDrag,
    // motor softness, MaxSpeed) was about the crash mode the rake fix addressed and remains
    // useful context for the turning-crash investigation, which hasn't ruled any of it back in or
    // out yet: the balance controller at five gain/sign combinations (zero, committed 2.4/0.35,
    // 24/3.5, and the negated sign of both - all reproduced the same crash timing within noise);
    // direct per-axis angular correction via Rekall.Rigidbody3D.angularCorrectionX/Y (adds
    // velocity directly rather than routing through the body's inertia tensor, so it relocates
    // the disturbance between axes instead of removing it); chassis angularDrag up to 25x its
    // committed value (no effect, because ApplyDrag runs before Simulation.Timestep and the
    // solver re-derives the disturbance within its own step regardless); rear-wheel motor
    // softness (either collapsed propulsion or changed nothing); MaxSpeed lowered from 22 to 14
    // (crash timing unchanged, ruling out a speed threshold, at least for the straight-line
    // mode). An earlier claim that the rear wheel was slipping at ~6x v/r was itself re-measured
    // and found wrong - the wheel tracks v/r almost exactly; that was never the cause. A coast
    // test (both motors at zero torque, an initial velocity authored directly onto every body)
    // was attempted to isolate whether the motor's reaction torque is the driver at all, but the
    // test method itself was invalid - authoring a large instantaneous velocity teleports every
    // body through the ground plane on the very first physics step, and the resulting
    // penetration-resolution transient is indistinguishable from genuine instability. Whether the
    // propulsion motor's reaction torque plays any role remains genuinely untested.
    private const double YawDampingGain = 0;
    private const double CrashRollDegrees = 62;
    private const double CrashPitchDegrees = 70;
    // Anti-wheelie stabilizer: measured directly (a zero-torque/zero-input control run holds the
    // chassis dead still, while a full-throttle run spins it to 200+ deg/s about the wheel's own
    // spin axis - see the roll/pitch axis note above), the rear wheel's motor reaction plus the
    // ground-friction reaction at the contact patch (a real wheelie/traction-torque effect, not a
    // scene-authoring bug) drive the chassis's pitch with nothing in the control loop to check it
    // - the balance controller only ever acted on steering/lean, never on pitch. Scaling the
    // motor's own torque down as pitch grew was tried and did not arrest it (the ground-friction
    // component keeps driving the spin independently of motor torque). This instead applies a
    // direct PD correction via Rekall.Rigidbody3D.angularCorrectionZ (see
    // RekallAgeBepuPhysicsSystem.ApplyAngularCorrection) - the same continuous-micro-correction
    // idiom the roll/steering balance controller already uses, just acting directly on the axis
    // nothing else constrains instead of indirectly through steering.
    private const double PitchStabilizerProportionalGain = 150;
    private const double PitchStabilizerDerivativeGain = 15;
    // A direct roll-axis analog (mirroring the pitch stabilizer above, layered on top of the
    // existing steering-mediated balance controller below) was tried as a fix for the slow roll
    // divergence documented on long unattended throttle-only runs, at both a stronger and a
    // gentler gain. Neither prevented the eventual crash - one produced a later but more violent
    // snap (peak angular speed roughly tripled), the other converged no better than the
    // steering-only path already in place. Left out: this remains the same open, previously
    // documented gap (rear-wheel spin momentum coupling into a gradual precession - see
    // docs/production/PROGRESS.md's 2026-08-28 checkpoint), now confirmed NOT to be a simple
    // missing-direct-correction problem the way the pitch/wheelie instability was.

    private const double ChunkLength = 40;
    private const double SpawnAheadDistance = 160;
    private const double DespawnBehindDistance = 60;
    private const double RoadHalfWidth = 4.5;
    private const double StreetLightSpacing = 24;
    private const double HazardMinimumSpacing = 70;
    private const double HazardMaximumSpacing = 160;

    public string Id => nameof(MidnightRiderSystem);

    public int Priority => -10;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var runStateEntity = world.EntitiesWithComponent(RunStateType).FirstOrDefault();
        if (runStateEntity is null)
        {
            return ValueTask.FromResult(world);
        }

        var run = runStateEntity.FindComponent(RunStateType)!;
        var seconds = context.DeltaTime.TotalSeconds;
        var chassisId = run.Properties.ReadString("chassisId") ?? string.Empty;
        var forkId = run.Properties.ReadString("forkId") ?? string.Empty;
        var rearWheelId = run.Properties.ReadString("rearWheelId") ?? string.Empty;
        var chassis = world.FindEntity(chassisId);
        var fork = world.FindEntity(forkId);
        if (chassis is null || fork is null)
        {
            return ValueTask.FromResult(world);
        }

        var enabled = run.Properties.ReadBoolean("enabled", true);
        var crashed = run.Properties.ReadBoolean("crashed", false);
        var targetSpeed = run.Properties.ReadNumber("targetSpeed", 0);
        var previousRoll = run.Properties.ReadNumber("previousRoll", 0);
        var previousYaw = run.Properties.ReadNumber("previousYaw", 0);
        var previousPitch = run.Properties.ReadNumber("previousPitch", 0);
        var distanceTraveled = run.Properties.ReadNumber("distanceTraveled", 0);
        var nextSpawnX = run.Properties.ReadNumber("nextSpawnX", 0);
        var nextChunkIndex = (int)run.Properties.ReadNumber("nextChunkIndex", 0);
        var seed = (int)run.Properties.ReadNumber("seed", 1);
        var startX = 0.0;

        // The wheels' spin axis is Z (Rekall.HingeJoint.axisZ=1 on Front/Rear Wheel), and the
        // bike travels along X with Y up - so rotation about Z is the wheelie/pitch axis, and
        // rotation about X (leaning the chassis toward +Z/-Z) is the true roll/lean axis. This
        // was previously swapped: `roll` read Z (wheelie angle) and `pitch` read X (true lean),
        // which fed the balance controller's PD correction the wrong physical quantity - no
        // gain on the wrongly-wired axis could ever stabilize actual side-to-side lean.
        var roll = Normalize180(chassis.Transform.Rotation3D.X);
        var pitch = Normalize180(chassis.Transform.Rotation3D.Z);
        var chassisYaw = chassis.Transform.Rotation3D.Y;
        var chassisX = chassis.Transform.Position3D.X;

        if (!crashed && (Math.Abs(roll) > CrashRollDegrees || Math.Abs(pitch) > CrashPitchDegrees))
        {
            crashed = true;
            world = world.EmitObservation(
                chassis, "midnight_rider.crashed", "warning", "gameplay", Id,
                $"The bike went down at distance {chassisX - startX:0.0}m.");
        }

        var running = enabled && !crashed;

        // --- Throttle: Up/Down set a target speed; the rear wheel's hinge motor chases it. ---
        var throttle = running ? Math.Clamp(world.InputActionValue("throttle"), -1, 1) : 0;
        targetSpeed = Math.Clamp(targetSpeed + (throttle * AccelerationPerSecond * seconds), 0, MaxSpeed);
        if (!running)
        {
            targetSpeed = Math.Max(0, targetSpeed - (AccelerationPerSecond * seconds));
        }

        if (!string.IsNullOrEmpty(rearWheelId))
        {
            var motorTarget = -(targetSpeed / WheelRadius) * (180 / Math.PI);
            world = world.UpdateEntity(rearWheelId, wheel => wheel.WithComponentNumber(HingeJointType, "motorTargetVelocity", motorTarget));
        }

        // --- Anti-wheelie stabilizer: a direct PD correction on the chassis's own pitch, applied
        // as a continuous angular-velocity nudge via Rekall.Rigidbody3D.angularCorrectionZ (see
        // RekallAgeBepuPhysicsSystem.ApplyAngularCorrection) rather than through any joint/motor -
        // pitch is an axis nothing else in this rig constrains. ---
        var pitchRate = seconds > 0 ? (pitch - previousPitch) / seconds : 0;
        var pitchCorrection = -(PitchStabilizerProportionalGain * pitch) - (PitchStabilizerDerivativeGain * pitchRate);
        var rollRate = seconds > 0 ? (roll - previousRoll) / seconds : 0;
        world = world.UpdateEntity(chassisId, c => c.WithComponentNumber("Rekall.Rigidbody3D", "angularCorrectionZ", pitchCorrection));

        // --- Steering + balance: the player's Left/Right input sets a desired lean/turn angle;
        // a small proportional-derivative correction on the chassis's own measured roll and
        // roll rate is added on top, exactly the kind of continuous micro-correction a real
        // rider supplies to stay upright - it nudges the STEERING target, never the lean itself,
        // so the resulting lean stays a genuine consequence of real contact/inertia physics. ---
        var steerInput = running ? Math.Clamp(world.InputActionValue("steer"), -1, 1) : 0;
        var balanceCorrection = (BalanceProportionalGain * roll) + (BalanceDerivativeGain * rollRate);
        var yawRate = seconds > 0 ? Normalize180(chassisYaw - previousYaw) / seconds : 0;
        var yawDamping = -YawDampingGain * yawRate;
        var targetSteerAngle = Math.Clamp(
            (steerInput * MaxSteerAngleDegrees) + balanceCorrection + yawDamping,
            -MaxForkAngleDegrees,
            MaxForkAngleDegrees);

        var forkYaw = fork.Transform.Rotation3D.Y;
        var currentSteerAngle = Normalize180(forkYaw - chassisYaw);
        var steerError = targetSteerAngle - currentSteerAngle;
        var forkMotorVelocity = Math.Clamp(steerError * SteerAngleGain, -MaxForkMotorDegreesPerSecond, MaxForkMotorDegreesPerSecond);
        if (!string.IsNullOrEmpty(forkId))
        {
            world = world.UpdateEntity(forkId, forkEntity => forkEntity.WithComponentNumber(HingeJointType, "motorTargetVelocity", forkMotorVelocity));
        }

        distanceTraveled = Math.Max(distanceTraveled, chassisX - startX);

        // --- Infinite road: spawn chunks/streetlights/hazards ahead, remove them once well
        // behind the bike. Deterministic per-chunk seeding keeps hazard placement reproducible
        // for a given seed even though chunks are streamed in and out over time. ---
        while (chassisX + SpawnAheadDistance > nextSpawnX)
        {
            world = SpawnChunk(world, nextChunkIndex, nextSpawnX, seed);
            nextChunkIndex++;
            nextSpawnX += ChunkLength;
        }

        var despawnBeforeX = chassisX - DespawnBehindDistance;
        var toRemove = world.Entities
            .Where(entity => entity.HasTag("road-chunk") && entity.Transform.Position3D.X + ChunkLength < despawnBeforeX)
            .Select(entity => entity.Id)
            .ToArray();
        foreach (var id in toRemove)
        {
            world = world.RemoveEntity(id);
        }

        world = world.UpdateEntity(runStateEntity.Id, entity => entity
            .WithComponentBoolean(RunStateType, "crashed", crashed)
            .WithComponentNumber(RunStateType, "targetSpeed", targetSpeed)
            .WithComponentNumber(RunStateType, "previousRoll", roll)
            .WithComponentNumber(RunStateType, "previousYaw", chassisYaw)
            .WithComponentNumber(RunStateType, "previousPitch", pitch)
            .WithComponentNumber(RunStateType, "distanceTraveled", distanceTraveled)
            .WithComponentNumber(RunStateType, "nextSpawnX", nextSpawnX)
            .WithComponentNumber(RunStateType, "nextChunkIndex", nextChunkIndex));

        return ValueTask.FromResult(world);
    }

    /// <summary>
    /// One 40-unit slice of infinite road: a tarmac slab, a streetlight (approximately every
    /// second chunk), and an occasional hazard - a raised speed-bump box the wheels physically
    /// collide with via BEPU's own contact resolution, not a scripted effect.
    /// </summary>
    private static RekallAgeRuntimeWorld SpawnChunk(RekallAgeRuntimeWorld world, int chunkIndex, double startX, int seed)
    {
        var centerX = startX + (ChunkLength / 2);
        var roadId = $"road-chunk-{chunkIndex}";
        var road = RekallAgeRuntimeModuleSdk.CreateEntity(roadId, $"Road {chunkIndex}")
            .WithTag("road-chunk")
            .WithPosition3D(new RekallAgeRuntimeVector3(centerX, -0.1, 0))
            .WithScale3D(new RekallAgeRuntimeVector3(ChunkLength, 0.2, RoadHalfWidth * 2))
            .UpsertComponent("Rekall.BoxCollider3D", new JsonObject { ["width"] = ChunkLength, ["height"] = 0.2, ["depth"] = RoadHalfWidth * 2 })
            .UpsertComponent("Rekall.PhysicsMaterial3D", new JsonObject { ["friction"] = 1.3, ["restitution"] = 0 })
            .UpsertComponent("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })
            .UpsertComponent("Rekall.Material", new JsonObject
            {
                ["baseColor"] = "#1c1d20",
                ["metallicFactor"] = 0.05,
                ["roughnessFactor"] = 0.92
            })
            .UpsertComponent("Rekall.MeshRenderer", new JsonObject { ["active"] = true, ["castShadows"] = true, ["receiveShadows"] = true });
        world = world.AddEntity(road);

        // Invisible guard rails along both edges of the road: found via a real sustained-throttle
        // run (60 real seconds, no steering input) that the bike's own small, physically genuine
        // yaw drift - the same reaction-torque/balance-correction coupling that makes it lean into
        // turns - compounds over a long enough run into a real lateral drift, eventually carrying
        // it past RoadHalfWidth and off the road into open space with nothing underneath. A real
        // rider corrects that drift continuously; this is a deliberate, low-risk gameplay
        // safeguard (invisible walls, a standard infinite-runner convention) rather than an
        // attempt to perfectly zero out a physically emergent drift, which is exactly the kind of
        // authored realism the balance controller is meant to keep organic.
        var railHeight = 1.5;
        var railThickness = 0.4;
        world = world.AddEntity(RekallAgeRuntimeModuleSdk.CreateEntity($"road-rail-left-{chunkIndex}", $"Road Rail Left {chunkIndex}")
            .WithTag("road-chunk")
            .WithPosition3D(new RekallAgeRuntimeVector3(centerX, railHeight / 2, RoadHalfWidth + (railThickness / 2)))
            .UpsertComponent("Rekall.BoxCollider3D", new JsonObject { ["width"] = ChunkLength, ["height"] = railHeight, ["depth"] = railThickness })
            .UpsertComponent("Rekall.PhysicsMaterial3D", new JsonObject { ["friction"] = 0.4, ["restitution"] = 0.1 }));
        world = world.AddEntity(RekallAgeRuntimeModuleSdk.CreateEntity($"road-rail-right-{chunkIndex}", $"Road Rail Right {chunkIndex}")
            .WithTag("road-chunk")
            .WithPosition3D(new RekallAgeRuntimeVector3(centerX, railHeight / 2, -RoadHalfWidth - (railThickness / 2)))
            .UpsertComponent("Rekall.BoxCollider3D", new JsonObject { ["width"] = ChunkLength, ["height"] = railHeight, ["depth"] = railThickness })
            .UpsertComponent("Rekall.PhysicsMaterial3D", new JsonObject { ["friction"] = 0.4, ["restitution"] = 0.1 }));

        if (chunkIndex % 2 == 0)
        {
            world = SpawnStreetLight(world, chunkIndex, startX + (StreetLightSpacing * 0.5), RoadHalfWidth + 1.2);
        }

        var hazardRoll = RekallAgeRuntimeModuleSdk.DeterministicUnit(seed, chunkIndex * 7919L + 13);
        if (chunkIndex > 2 && hazardRoll < 0.35)
        {
            var offset = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, (chunkIndex * 7919L) + 29, ChunkLength * 0.25, ChunkLength * 0.75);
            world = SpawnHazard(world, chunkIndex, startX + offset, seed, chunkIndex);
        }

        world = SpawnRoadsideTrees(world, chunkIndex, startX, seed);

        return world;
    }

    /// <summary>
    /// Scatters trees from AGE's generic realistic procedural-tree generator along both shoulders
    /// of the road, well outside the guard rails so they read as roadside dressing rather than a
    /// driving hazard. Every tree in a chunk shares the chunk's own deterministic seed sequence,
    /// so a given seed always regrows the identical forest - the same replay-stability guarantee
    /// the hazard and street-light spawns already rely on.
    /// </summary>
    private static RekallAgeRuntimeWorld SpawnRoadsideTrees(RekallAgeRuntimeWorld world, int chunkIndex, double startX, int seed)
    {
        const int treesPerSide = 1;
        const double spawnChance = 0.5;
        for (var side = -1; side <= 1; side += 2)
        {
            for (var slot = 0; slot < treesPerSide; slot++)
            {
                var sequenceBase = (chunkIndex * 7919L) + (side > 0 ? 1000L : 2000L) + (slot * 97L);
                if (RekallAgeRuntimeModuleSdk.DeterministicUnit(seed, sequenceBase) > spawnChance)
                {
                    continue;
                }

                var alongChunk = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, sequenceBase + 1, 0, ChunkLength);
                var lateral = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, sequenceBase + 2, RoadHalfWidth + 2.5, RoadHalfWidth + 9);
                var x = startX + alongChunk;
                var treeSeed = seed + chunkIndex + (side > 0 ? 500 : 0) + slot;
                var treeLod = RekallAgeProceduralTreeGenerator.GenerateLod(
                    $"road-tree-{treeSeed}-{sequenceBase}", "Roadside oak",
                    RekallAgeProceduralTreeSettings.TemperateOak(treeSeed), level: 1);
                // The moving road uses the balanced middle LOD. Static authored scenes can save
                // all three generated surfaces and connect them with the ordinary Rekall.LodGroup.
                var barkMesh = ToGeometryMesh(treeLod.Bark);
                var foliageMesh = ToGeometryMesh(treeLod.Foliage);
                var treeId = $"tree-{chunkIndex}-{side}-{slot}";
                var tree = RekallAgeRuntimeModuleSdk.CreateEntity(treeId, $"Tree {chunkIndex}/{side}/{slot}")
                    .WithTag("road-chunk")
                    .WithPosition3D(new RekallAgeRuntimeVector3(x, 0, lateral * side))
                    .UpsertComponent("Rekall.GeometryMesh", new JsonObject
                    {
                        ["vertices"] = barkMesh.Vertices,
                        ["indices"] = barkMesh.Indices
                    })
                    .UpsertComponent("Rekall.Material", new JsonObject
                    {
                        ["baseColor"] = "#3a2a18",
                        ["metallicFactor"] = 0.02,
                        ["roughnessFactor"] = 0.95
                    })
                    .UpsertComponent("Rekall.MeshRenderer", new JsonObject { ["active"] = true, ["castShadows"] = true, ["receiveShadows"] = true })
                    .UpsertComponent("Rekall.CapsuleCollider3D", new JsonObject { ["radius"] = 0.24, ["length"] = 1.6 })
                    .UpsertComponent("Rekall.PhysicsMaterial3D", new JsonObject { ["friction"] = 0.9, ["restitution"] = 0.05 });
                world = world.AddEntity(tree);

                var leaves = RekallAgeRuntimeModuleSdk.CreateEntity($"{treeId}-foliage", $"Tree {chunkIndex}/{side}/{slot} foliage")
                    .WithTag("road-chunk")
                    .WithTag("foliage")
                    .WithPosition3D(new RekallAgeRuntimeVector3(x, 0, lateral * side))
                    .UpsertComponent("Rekall.GeometryMesh", new JsonObject
                    {
                        ["vertices"] = foliageMesh.Vertices,
                        ["indices"] = foliageMesh.Indices
                    })
                    .UpsertComponent("Rekall.Material", new JsonObject
                    {
                        ["baseColor"] = "#315b24",
                        ["metallicFactor"] = 0.0,
                        ["roughnessFactor"] = 0.78,
                        ["alphaMode"] = "mask",
                        ["alphaCutoff"] = 0.5
                    })
                    .UpsertComponent("Rekall.MeshRenderer", new JsonObject { ["active"] = true, ["castShadows"] = true, ["receiveShadows"] = true });
                world = world.AddEntity(leaves);
            }
        }

        return world;
    }

    private static (JsonArray Vertices, JsonArray Indices) ToGeometryMesh(RekallAgeMeshAsset mesh)
    {
        var compiled = new RekallAgeMeshCompiler().Compile(mesh);
        var vertices = new JsonArray();
        foreach (var vertex in compiled.Vertices)
        {
            vertices.Add(new JsonObject
            {
                ["x"] = vertex.Position.X, ["y"] = vertex.Position.Y, ["z"] = vertex.Position.Z,
                ["nx"] = vertex.Normal.X, ["ny"] = vertex.Normal.Y, ["nz"] = vertex.Normal.Z,
                ["u"] = vertex.Uv.X, ["v"] = vertex.Uv.Y,
                ["r"] = vertex.Color.X, ["g"] = vertex.Color.Y, ["b"] = vertex.Color.Z, ["a"] = vertex.Color.W
            });
        }
        var indices = new JsonArray(compiled.Indices.Select(index => (JsonNode?)JsonValue.Create(index)).ToArray());
        return (vertices, indices);
    }

    private static RekallAgeRuntimeWorld SpawnStreetLight(RekallAgeRuntimeWorld world, int chunkIndex, double x, double lateralOffset)
    {
        var side = chunkIndex % 4 == 0 ? 1 : -1;
        var poleId = $"street-light-{chunkIndex}";
        var pole = RekallAgeRuntimeModuleSdk.CreateEntity(poleId, $"Street Light {chunkIndex}")
            .WithTag("road-chunk")
            .WithPosition3D(new RekallAgeRuntimeVector3(x, 0, lateralOffset * side))
            .WithScale3D(new RekallAgeRuntimeVector3(0.12, 4.2, 0.12))
            .UpsertComponent("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cylinder" })
            .UpsertComponent("Rekall.Material", new JsonObject { ["baseColor"] = "#2b2d31", ["metallicFactor"] = 0.8, ["roughnessFactor"] = 0.4 })
            .UpsertComponent("Rekall.MeshRenderer", new JsonObject { ["active"] = true, ["castShadows"] = true, ["receiveShadows"] = true });
        world = world.AddEntity(pole);

        var lampId = $"street-light-lamp-{chunkIndex}";
        var lamp = RekallAgeRuntimeModuleSdk.CreateEntity(lampId, $"Street Lamp {chunkIndex}")
            .WithTag("road-chunk")
            .WithTag("lighting")
            .WithPosition3D(new RekallAgeRuntimeVector3(x, 4.6, (lateralOffset - 0.6) * side))
            .WithRotation3D(new RekallAgeRuntimeVector3(90, 0, 0))
            .UpsertComponent("Rekall.SpotLight", new JsonObject
            {
                ["intensity"] = 7,
                ["range"] = 14,
                ["innerConeAngle"] = 18,
                ["outerConeAngle"] = 34,
                ["color"] = "#ffc98a",
                ["priority"] = 1
            });
        world = world.AddEntity(lamp);
        return world;
    }

    private static RekallAgeRuntimeWorld SpawnHazard(RekallAgeRuntimeWorld world, int chunkIndex, double x, int seed, long sequence)
    {
        var lateral = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, (sequence * 7919L) + 41, -RoadHalfWidth + 1, RoadHalfWidth - 1);
        var hazardId = $"hazard-{chunkIndex}";
        var hazard = RekallAgeRuntimeModuleSdk.CreateEntity(hazardId, $"Hazard {chunkIndex}")
            .WithTag("road-chunk")
            .WithPosition3D(new RekallAgeRuntimeVector3(x, 0.08, lateral))
            .WithScale3D(new RekallAgeRuntimeVector3(1.4, 0.16, 0.7))
            .UpsertComponent("Rekall.BoxCollider3D", new JsonObject { ["width"] = 1.4, ["height"] = 0.16, ["depth"] = 0.7 })
            .UpsertComponent("Rekall.PhysicsMaterial3D", new JsonObject { ["friction"] = 1.1, ["restitution"] = 0.05 })
            .UpsertComponent("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })
            .UpsertComponent("Rekall.Material", new JsonObject { ["baseColor"] = "#c98a2b", ["metallicFactor"] = 0.1, ["roughnessFactor"] = 0.8 })
            .UpsertComponent("Rekall.MeshRenderer", new JsonObject { ["active"] = true, ["castShadows"] = true, ["receiveShadows"] = true });
        return world.AddEntity(hazard);
    }

    private static double Normalize180(double degrees)
    {
        var wrapped = degrees % 360;
        if (wrapped > 180) wrapped -= 360;
        if (wrapped < -180) wrapped += 360;
        return wrapped;
    }
}
