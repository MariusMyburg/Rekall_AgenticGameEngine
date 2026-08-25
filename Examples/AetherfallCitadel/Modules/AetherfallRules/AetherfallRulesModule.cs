using Rekall.Age.Modules;

namespace Game.Modules.AetherfallRules;

[RekallAgeModule("aetherfall.rules", "Aetherfall Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class AetherfallRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<WardenState>();
        builder.RegisterComponent<EnemyState>();
        builder.RegisterComponent<ProjectileState>();
        builder.RegisterComponent<PickupState>();
        builder.RegisterComponent<ConduitState>();
        builder.RegisterComponent<HazardState>();
        builder.RegisterComponent<GuardianState>();
        builder.RegisterComponent<EncounterState>();
        builder.RegisterComponent<EffectState>();
        builder.RegisterRuntimeSystem<AetherfallRulesSystem>();
    }
}

[RekallAgeComponent("Warden State", Description = "Player-authored movement, resources, combat, and objective state.")]
public sealed class WardenState : RekallAgeComponent
{
    [RekallAgeProperty] public double VelocityX { get; init; }
    [RekallAgeProperty] public double VelocityZ { get; init; }
    [RekallAgeProperty] public double Integrity { get; init; } = 100;
    [RekallAgeProperty] public double Aether { get; init; } = 100;
    [RekallAgeProperty] public double Score { get; init; }
    [RekallAgeProperty] public double Combo { get; init; }
    [RekallAgeProperty] public double DashCooldown { get; init; }
    [RekallAgeProperty] public double PulseCooldown { get; init; }
    [RekallAgeProperty] public double Invulnerability { get; init; }
    [RekallAgeProperty] public double ShardCount { get; init; }
    [RekallAgeProperty] public bool CombatStarted { get; init; }
    [RekallAgeProperty] public string ObjectivePhase { get; init; } = "arrival";
    [RekallAgeProperty] public string Phase { get; init; } = "playing";
    [RekallAgeProperty] public double FacingX { get; init; }
    [RekallAgeProperty] public double FacingZ { get; init; } = 1;
    [RekallAgeProperty] public double SpawnX { get; init; }
    [RekallAgeProperty] public double SpawnY { get; init; } = 0.8;
    [RekallAgeProperty] public double SpawnZ { get; init; } = -12;
}

[RekallAgeComponent("Enemy State")]
public sealed class EnemyState : RekallAgeComponent
{
    [RekallAgeProperty] public string Archetype { get; init; } = "sentinel";
    [RekallAgeProperty] public double Health { get; init; } = 60;
    [RekallAgeProperty] public double MaximumHealth { get; init; } = 60;
    [RekallAgeProperty] public double Speed { get; init; } = 2;
    [RekallAgeProperty] public double AttackCadence { get; init; } = 1.5;
    [RekallAgeProperty] public double PreferredRange { get; init; } = 6;
    [RekallAgeProperty] public double SpawnX { get; init; }
    [RekallAgeProperty] public double SpawnY { get; init; } = 0.8;
    [RekallAgeProperty] public double SpawnZ { get; init; }
    [RekallAgeProperty] public string Phase { get; init; } = "idle";
    [RekallAgeProperty] public bool Active { get; init; } = true;
    [RekallAgeProperty] public double AttackClock { get; init; }
}

[RekallAgeComponent("Projectile State")]
public sealed class ProjectileState : RekallAgeComponent
{
    [RekallAgeProperty] public string Faction { get; init; } = "warden";
    [RekallAgeProperty] public double Damage { get; init; } = 20;
    [RekallAgeProperty] public double VelocityX { get; init; }
    [RekallAgeProperty] public double VelocityZ { get; init; }
    [RekallAgeProperty] public double RemainingLifetime { get; init; } = 2;
    [RekallAgeProperty] public double Radius { get; init; } = 0.45;
    [RekallAgeProperty] public string VisualRole { get; init; } = "pulse";
}

[RekallAgeComponent("Pickup State")]
public sealed class PickupState : RekallAgeComponent
{
    [RekallAgeProperty] public string Kind { get; init; } = "shard";
    [RekallAgeProperty] public double Value { get; init; } = 1;
    [RekallAgeProperty] public bool Collected { get; init; }
    [RekallAgeProperty] public string RespawnPolicy { get; init; } = "reset";
}

[RekallAgeComponent("Conduit State")]
public sealed class ConduitState : RekallAgeComponent
{
    [RekallAgeProperty] public double RequiredShards { get; init; } = 2;
    [RekallAgeProperty] public double ActivationProgress { get; init; }
    [RekallAgeProperty] public bool Active { get; init; }
    [RekallAgeProperty] public string LinkedGate { get; init; } = string.Empty;
}

[RekallAgeComponent("Hazard State")]
public sealed class HazardState : RekallAgeComponent
{
    [RekallAgeProperty] public string MotionKind { get; init; } = "linear";
    [RekallAgeProperty] public double OriginX { get; init; }
    [RekallAgeProperty] public double OriginZ { get; init; }
    [RekallAgeProperty] public double Radius { get; init; }
    [RekallAgeProperty] public double Amplitude { get; init; }
    [RekallAgeProperty] public double Speed { get; init; } = 1;
    [RekallAgeProperty] public double Damage { get; init; } = 20;
    [RekallAgeProperty] public double PhaseOffset { get; init; }
}

[RekallAgeComponent("Guardian State")]
public sealed class GuardianState : RekallAgeComponent
{
    [RekallAgeProperty] public double Health { get; init; } = 500;
    [RekallAgeProperty] public double Shield { get; init; } = 100;
    [RekallAgeProperty] public string Stage { get; init; } = "sealed";
    [RekallAgeProperty] public double AttackClock { get; init; }
    [RekallAgeProperty] public bool Vulnerable { get; init; }
    [RekallAgeProperty] public bool Defeated { get; init; }
}

[RekallAgeComponent("Encounter State")]
public sealed class EncounterState : RekallAgeComponent
{
    [RekallAgeProperty] public string ActiveZone { get; init; } = "arrival";
    [RekallAgeProperty] public double Wave { get; init; }
    [RekallAgeProperty] public double RemainingEnemies { get; init; }
    [RekallAgeProperty] public string GateState { get; init; } = "sealed";
    [RekallAgeProperty] public double ElapsedTime { get; init; }
    [RekallAgeProperty] public bool Completed { get; init; }
}

[RekallAgeComponent("Effect State")]
public sealed class EffectState : RekallAgeComponent
{
    [RekallAgeProperty] public string Kind { get; init; } = "impact";
    [RekallAgeProperty] public double Age { get; init; }
    [RekallAgeProperty] public double Lifetime { get; init; } = 0.3;
    [RekallAgeProperty] public double StartScale { get; init; } = 0.3;
    [RekallAgeProperty] public double EndScale { get; init; } = 1.5;
    [RekallAgeProperty] public string ColorRole { get; init; } = "aether";
}
