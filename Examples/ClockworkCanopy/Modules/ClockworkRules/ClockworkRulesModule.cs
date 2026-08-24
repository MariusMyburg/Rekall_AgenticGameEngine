using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.ClockworkRules;

[RekallAgeModule("clockwork.rules", "Clockwork Canopy Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class ClockworkRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<PlayerState>();
        builder.RegisterComponent<HazardState>();
        builder.RegisterComponent<CollectibleState>();
        builder.RegisterComponent<GoalState>();
        builder.RegisterComponent<HudState>();
        builder.RegisterComponent<PlatformState>();
        builder.RegisterRuntimeSystem<ClockworkRulesSystem>();
    }
}

[RekallAgeComponent("Player State")]
public sealed class PlayerState : RekallAgeComponent
{
    [RekallAgeProperty] public bool Enabled { get; init; } = true;
    [RekallAgeProperty] public double X { get; init; } = -4;
    [RekallAgeProperty] public double Y { get; init; } = 0.5;
    [RekallAgeProperty] public double VX { get; init; } = 0;
    [RekallAgeProperty] public double VY { get; init; } = 0;
    [RekallAgeProperty] public bool Grounded { get; init; } = false;
    [RekallAgeProperty] public double Lives { get; init; } = 3;
    [RekallAgeProperty] public double Score { get; init; } = 0;
    [RekallAgeProperty] public double GameClock { get; init; } = 0;
    [RekallAgeProperty] public double SpawnX { get; init; } = -4;
    [RekallAgeProperty] public double SpawnY { get; init; } = 0.5;
    [RekallAgeProperty] public double DeathPlane { get; init; } = -6;
    [RekallAgeProperty] public double PlayerHalfX { get; init; } = 0.5;
    [RekallAgeProperty] public double PlayerHalfY { get; init; } = 0.5;
    [RekallAgeProperty] public string Phase { get; init; } = "playing";
}

[RekallAgeComponent("Hazard State")]
public sealed class HazardState : RekallAgeComponent
{
    [RekallAgeProperty] public double BaseX { get; init; }
    [RekallAgeProperty] public double BaseY { get; init; }
    [RekallAgeProperty] public double Range { get; init; } = 2;
    [RekallAgeProperty] public double Speed { get; init; } = 2;
    [RekallAgeProperty] public double PhaseOffset { get; init; } = 0;
    [RekallAgeProperty] public double HalfSize { get; init; } = 0.9;
    [RekallAgeProperty] public string Axis { get; init; } = "x";
}

[RekallAgeComponent("Collectible State")]
public sealed class CollectibleState : RekallAgeComponent
{
    [RekallAgeProperty] public bool Collected { get; init; } = false;
    [RekallAgeProperty] public double Points { get; init; } = 25;
    [RekallAgeProperty] public double Radius { get; init; } = 0.9;
}

[RekallAgeComponent("Goal State")]
public sealed class GoalState : RekallAgeComponent
{
    [RekallAgeProperty] public double Points { get; init; } = 100;
    [RekallAgeProperty] public double HalfSize { get; init; } = 1.5;
}

[RekallAgeComponent("HUD State")]
public sealed class HudState : RekallAgeComponent
{
    [RekallAgeProperty] public string StatusText { get; init; } = "PLAYING";
}

[RekallAgeComponent("Platform Body")]
public sealed class PlatformState : RekallAgeComponent
{
    [RekallAgeProperty] public double HalfX { get; init; } = 0.5;
    [RekallAgeProperty] public double HalfY { get; init; } = 0.5;
}

public sealed class ClockworkRulesSystem : IRekallAgeRuntimeModuleSystem
{
    public string Id => nameof(ClockworkRulesSystem);
    public int Priority => 10;

    const double Gravity = 22.0;
    const double MaxSpeed = 8.0;
    const double Accel = 42.0;
    const double JumpSpeed = 11.0;
    const double CameraMinX = -6.0;
    const double CameraMaxX = 44.0;
    const double CameraOffsetX = 1.5;

    const string ps = "Game.Modules.ClockworkRules.PlayerState";
    const string hz = "Game.Modules.ClockworkRules.HazardState";
    const string co = "Game.Modules.ClockworkRules.CollectibleState";
    const string gl = "Game.Modules.ClockworkRules.GoalState";
    const string hud = "Game.Modules.ClockworkRules.HudState";
    const string pt = "Game.Modules.ClockworkRules.PlatformState";
    const string label = "Rekall.Label";

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world, RekallAgeRuntimeModuleFrameContext context)
    {
        var player = world.FindEntity("Pip");
        if (player == null) return ValueTask.FromResult(world);

        double seconds = context.DeltaTime.TotalSeconds;
        if (seconds < 0) seconds = 0;
        if (seconds > 0.1) seconds = 0.1;

        double px = player.ComponentNumber(ps, "X", -4);
        double py = player.ComponentNumber(ps, "Y", 0.5);
        double vx = player.ComponentNumber(ps, "VX", 0);
        double vy = player.ComponentNumber(ps, "VY", 0);
        bool grounded = player.ComponentBoolean(ps, "Grounded", false);
        double lives = player.ComponentNumber(ps, "Lives", 3);
        double score = player.ComponentNumber(ps, "Score", 0);
        double clock = player.ComponentNumber(ps, "GameClock", 0) + seconds;
        double spawnX = player.ComponentNumber(ps, "SpawnX", -4);
        double spawnY = player.ComponentNumber(ps, "SpawnY", 0.5);
        double deathPlane = player.ComponentNumber(ps, "DeathPlane", -6);
        double phx = player.ComponentNumber(ps, "PlayerHalfX", 0.5);
        double phy = player.ComponentNumber(ps, "PlayerHalfY", 0.5);
        string phase = player.ComponentString(ps, "Phase", "playing") ?? "playing";

        var platforms = world.EntitiesWithTag("platform");
        var hazards = world.EntitiesWithTag("hazard");
        var collectibles = world.EntitiesWithTag("collectible");
        var goal = world.FindEntity("Spire");

        world = MoveHazards(world, hazards, clock);
        hazards = world.EntitiesWithTag("hazard");

        // --- Reset ---
        if (world.WasInputActionPressed("reset"))
        {
            px = spawnX; py = spawnY; vx = 0; vy = 0;
            grounded = false; lives = 3; score = 0; clock = 0; phase = "playing";
            world = ApplyPlayer(world, player, px, py, vx, vy, grounded, lives, score, clock, phase);
            world = MoveHazards(world, hazards, clock);
            foreach (var c in collectibles)
            {
                world = world.UpdateEntity(c.Id, e => e
                    .WithComponentBoolean(co, "Collected", false)
                    .WithVisible(true));
            }
            return ValueTask.FromResult(ApplyPresentation(world, px, lives, score, phase));
        }

        if (phase != "playing")
        {
            world = ApplyPlayer(world, player, px, py, vx, vy, grounded, lives, score, clock, phase);
            return ValueTask.FromResult(ApplyPresentation(world, px, lives, score, phase));
        }

        // --- Horizontal movement with accel ---
        double input = world.InputActionValue("move.horizontal", 0);
        double targetVX = input * MaxSpeed;
        double maxDelta = Accel * seconds;
        if (Math.Abs(targetVX - vx) <= maxDelta) vx = targetVX;
        else vx += Math.Sign(targetVX - vx) * maxDelta;

        // --- Vertical / gravity ---
        vy += -Gravity * seconds;
        if (world.WasInputActionPressed("jump") && grounded)
        {
            vy = JumpSpeed;
            grounded = false;
        }

        px += vx * seconds;
        py += vy * seconds;

        // --- Platform collision (AABB) ---
        foreach (var plat in platforms)
        {
            double hx = plat.Transform.Position3D.X;
            double hy = plat.Transform.Position3D.Y;
            double platHalfX = plat.ComponentNumber(pt, "HalfX", 0.5);
            double platHalfY = plat.ComponentNumber(pt, "HalfY", 0.5);
            double dx = px - hx;
            double dy = py - hy;
            double overlapX = (phx + platHalfX) - Math.Abs(dx);
            double overlapY = (phy + platHalfY) - Math.Abs(dy);
            if (overlapX <= 0 || overlapY <= 0) continue;
            if (overlapX < overlapY)
            {
                px = hx + (dx >= 0 ? 1 : -1) * (phx + platHalfX);
                vx = 0;
            }
            else
            {
                if (dy > 0)
                {
                    py = hy + (phy + platHalfY);
                    if (vy < 0) vy = 0;
                    grounded = true;
                }
                else
                {
                    py = hy - (phy + platHalfY);
                    if (vy > 0) vy = 0;
                }
            }
        }

        // --- Fall off world / death plane ---
        if (py < deathPlane)
        {
            lives -= 1;
            if (lives <= 0) { lives = 0; phase = "dead"; }
            else { px = spawnX; py = spawnY; vx = 0; vy = 0; grounded = false; }
        }

        // --- Collectibles ---
        foreach (var c in collectibles)
        {
            if (c.ComponentBoolean(co, "Collected", false)) continue;
            double cx = c.Transform.Position3D.X;
            double cy = c.Transform.Position3D.Y;
            double rad = c.ComponentNumber(co, "Radius", 0.9);
            double dx = px - cx;
            double dy = py - cy;
            if (dx * dx + dy * dy <= rad * rad)
            {
                score += c.ComponentNumber(co, "Points", 25);
                world = world.UpdateEntity(c.Id, e => e
                    .WithComponentBoolean(co, "Collected", true)
                    .WithVisible(false));
            }
        }

        // --- Hazards (kill + respawn) ---
        foreach (var h in hazards)
        {
            double hx = h.Transform.Position3D.X;
            double hy = h.Transform.Position3D.Y;
            double hh = h.ComponentNumber(hz, "HalfSize", 0.9);
            double overlapX = (phx + hh) - Math.Abs(px - hx);
            double overlapY = (phy + hh) - Math.Abs(py - hy);
            if (overlapX > 0 && overlapY > 0)
            {
                lives -= 1;
                if (lives <= 0) { lives = 0; phase = "dead"; }
                else { px = spawnX; py = spawnY; vx = 0; vy = 0; grounded = false; }
            }
        }

        // --- Goal / win ---
        if (goal != null && phase == "playing")
        {
            double gx = goal.Transform.Position3D.X;
            double gy = goal.Transform.Position3D.Y;
            double gh = goal.ComponentNumber(gl, "HalfSize", 1.5);
            double overlapX = (phx + gh) - Math.Abs(px - gx);
            double overlapY = (phy + gh) - Math.Abs(py - gy);
            if (overlapX > 0 && overlapY > 0)
            {
                score += goal.ComponentNumber(gl, "Points", 100);
                phase = "won";
            }
        }

        world = ApplyPlayer(world, player, px, py, vx, vy, grounded, lives, score, clock, phase);
        return ValueTask.FromResult(ApplyPresentation(world, px, lives, score, phase));
    }

    static RekallAgeRuntimeWorld MoveHazards(
        RekallAgeRuntimeWorld world,
        IReadOnlyList<RekallAgeRuntimeEntity> hazards,
        double clock)
    {
        foreach (var h in hazards)
        {
            double bx = h.ComponentNumber(hz, "BaseX", h.Transform.Position3D.X);
            double by = h.ComponentNumber(hz, "BaseY", h.Transform.Position3D.Y);
            double range = h.ComponentNumber(hz, "Range", 2);
            double speed = h.ComponentNumber(hz, "Speed", 2);
            double poff = h.ComponentNumber(hz, "PhaseOffset", 0);
            string axis = h.ComponentString(hz, "Axis", "x") ?? "x";
            double offset = Math.Sin(clock * speed + poff) * range;
            double nx = bx + (axis.Equals("x", StringComparison.OrdinalIgnoreCase) ? offset : 0);
            double ny = by + (axis.Equals("y", StringComparison.OrdinalIgnoreCase) ? offset : 0);
            world = world.UpdateEntity(h.Id, e => e.WithPosition3D(new RekallAgeRuntimeVector3(nx, ny, 0)));
        }
        return world;
    }

    static RekallAgeRuntimeWorld ApplyPresentation(
        RekallAgeRuntimeWorld world,
        double playerX,
        double lives,
        double score,
        string phase)
    {
        string status = phase == "won" ? "YOU WIN" : phase == "dead" ? "GAME OVER" : "PLAYING";
        string text = $"SCORE {score:0}    LIVES {lives:0}    {status}";
        var hudEntity = world.FindEntity("HUDRoot");
        if (hudEntity != null)
        {
            world = world.UpdateEntity(hudEntity.Id, e => e
                .WithComponentString(hud, "StatusText", status)
                .WithComponentString(label, "Text", text));
        }

        var camera = world.FindEntity("CameraRig");
        if (camera != null)
        {
            double cameraX = Math.Clamp(playerX + CameraOffsetX, CameraMinX, CameraMaxX);
            world = world.UpdateEntity(camera.Id, e =>
                e.WithPosition3D(new RekallAgeRuntimeVector3(cameraX, 1, 8)));
        }
        return world;
    }

    static RekallAgeRuntimeWorld ApplyPlayer(
        RekallAgeRuntimeWorld world, RekallAgeRuntimeEntity player,
        double x, double y, double vx, double vy, bool grounded,
        double lives, double score, double clock, string phase)
    {
        world = world.UpdateEntity(player.Id, e =>
            e.WithPosition3D(new RekallAgeRuntimeVector3(x, y, 0))
             .WithComponentNumber(ps, "X", x)
             .WithComponentNumber(ps, "Y", y)
             .WithComponentNumber(ps, "VX", vx)
             .WithComponentNumber(ps, "VY", vy)
             .WithComponentBoolean(ps, "Grounded", grounded)
             .WithComponentNumber(ps, "Lives", lives)
             .WithComponentNumber(ps, "Score", score)
             .WithComponentNumber(ps, "GameClock", clock)
             .WithComponentString(ps, "Phase", phase));
        return world;
    }
}
