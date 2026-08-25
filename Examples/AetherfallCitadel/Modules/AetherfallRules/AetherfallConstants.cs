namespace Game.Modules.AetherfallRules;

internal static class AetherfallConstants
{
    public const string WardenName = "AetherWarden";
    public const string WardenStateType = "Game.Modules.AetherfallRules.WardenState";
    public const string MoveHorizontalAction = "move.horizontal";
    public const string MoveVerticalAction = "move.vertical";
    public const string PulseAction = "ability.pulse";
    public const string DashAction = "ability.dash";
    public const string EnemyStateType = "Game.Modules.AetherfallRules.EnemyState";
    public const string ProjectileStateType = "Game.Modules.AetherfallRules.ProjectileState";
    public const string PickupStateType = "Game.Modules.AetherfallRules.PickupState";
    public const string EffectStateType = "Game.Modules.AetherfallRules.EffectState";
    public const string HazardStateType = "Game.Modules.AetherfallRules.HazardState";
    public const string ConduitStateType = "Game.Modules.AetherfallRules.ConduitState";
    public const string EncounterStateType = "Game.Modules.AetherfallRules.EncounterState";
    public const string GuardianStateType = "Game.Modules.AetherfallRules.GuardianState";
    public const string InteractAction = "interact";
    public const double WardenSpeed = 8.5;
    public const double PulseSpeed = 18;
    public const double PulseDamage = 24;
    public const double PulseCost = 8;
    public const double PulseCooldownSeconds = 0.22;
    public const double PulseLifetimeSeconds = 1.4;
    public const double PulseRadius = 0.55;
    public const double DashDistance = 2.8;
    public const double DashCost = 18;
    public const double DashCooldownSeconds = 0.85;
    public const double DashInvulnerabilitySeconds = 0.22;
    public const double ConduitInteractionRadius = 4.0;
    public const double MaximumDeltaSeconds = 0.1;
    public const double ArrivalMinimumX = -6.2;
    public const double ArrivalMaximumX = 6.2;
    public const double ArrivalMinimumZ = -14.8;
    public const double ArrivalMaximumZ = 4.0;
    public const double CitadelMinimumX = -12;
    public const double CitadelMaximumX = 12;
    public const double CitadelMinimumZ = -14.8;
    public const double CitadelMaximumZ = 50;
}
