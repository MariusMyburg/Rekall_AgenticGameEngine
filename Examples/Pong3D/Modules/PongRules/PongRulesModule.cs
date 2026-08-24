using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.PongRules;

[RekallAgeModule("pong.rules", "Pong Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class PongRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<PongBall>();
        builder.RegisterComponent<PongPaddle>();
        builder.RegisterRuntimeSystem<PongRulesSystem>();
    }
}

[RekallAgeComponent("Pong Ball", Description = "Ball kinematics and match state: velocity, score, and serve phase.")]
public sealed class PongBall : RekallAgeComponent
{
    [RekallAgeProperty] public double VelocityX { get; init; }
    [RekallAgeProperty] public double VelocityY { get; init; }
    [RekallAgeProperty] public double ScoreLeft { get; init; }
    [RekallAgeProperty] public double ScoreRight { get; init; }
    [RekallAgeProperty] public string Phase { get; init; } = "serving";
    [RekallAgeProperty] public double ServeTimer { get; init; } = 1;
    [RekallAgeProperty] public double ServeDirection { get; init; } = 1;
}

[RekallAgeComponent("Pong Paddle", Description = "Marks an entity as a player- or AI-controlled paddle.")]
public sealed class PongPaddle : RekallAgeComponent
{
    [RekallAgeProperty] public string Side { get; init; } = "left";
    [RekallAgeProperty] public double Speed { get; init; } = 6;
}

public sealed class PongRulesSystem : IRekallAgeRuntimeModuleSystem
{
    private const string BallComponentType = "Game.Modules.PongRules.PongBall";
    private const string PaddleComponentType = "Game.Modules.PongRules.PongPaddle";
    private const string LabelComponentType = "Rekall.Label";

    private const double ArenaHalfWidth = 9.0;
    private const double ArenaHalfHeight = 4.3;
    private const double BallRadius = 0.35;
    private const double WallBounceMargin = 0.45;
    private const double PaddleHalfHeight = 1.1;
    private const double PaddleHalfDepth = 0.175;
    private const double PaddleXLeft = -7.0;
    private const double PaddleXRight = 7.0;
    private const double PaddleClampY = 3.2;
    private const double ServeSpeed = 4.5;
    private const double ServeVerticalSpeed = 2.2;
    private const double MaxBallSpeed = 9.0;
    private const double SpeedRampPerHit = 1.05;
    private const double PaddleAngleFactor = 3.0;
    private const int WinningScore = 5;
    private const double AiDeadzone = 0.15;

    public string Id => nameof(PongRulesSystem);

    public int Priority => 0;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var seconds = context.DeltaTime.TotalSeconds;
        var resetPressed = world.WasInputActionPressed("reset");

        var ballEntity = world.Entities.FirstOrDefault(entity =>
            entity.Components.Any(component => component.Type == BallComponentType));
        var ballY = ballEntity?.Transform.Position3D.Y ?? 0;
        var phase = ballEntity?.ComponentString(BallComponentType, "phase", "serving") ?? "serving";

        // Paddles move first so a serve launched this same frame reflects their latest position.
        var worldAfterPaddles = world.UpdateEntitiesWithComponent(PaddleComponentType, entity =>
        {
            var side = entity.ComponentString(PaddleComponentType, "side", "left");
            var speed = entity.ComponentNumber(PaddleComponentType, "speed", 6);
            var position = entity.Transform.Position3D;
            double targetDeltaY;
            if (side == "left")
            {
                var axis = world.InputActionValue("paddle.move");
                targetDeltaY = axis * speed * seconds;
            }
            else
            {
                var offset = ballY - position.Y;
                if (Math.Abs(offset) <= AiDeadzone)
                {
                    targetDeltaY = 0;
                }
                else
                {
                    var direction = Math.Sign(offset);
                    var step = Math.Min(Math.Abs(offset), speed * seconds);
                    targetDeltaY = direction * step;
                }
            }

            var newY = Math.Clamp(position.Y + targetDeltaY, -PaddleClampY, PaddleClampY);
            return entity.WithPosition3D(new RekallAgeRuntimeVector3(position.X, newY, position.Z));
        });

        var leftPaddleY = worldAfterPaddles.Entities
            .FirstOrDefault(entity => entity.ComponentString(PaddleComponentType, "side", null) == "left")
            ?.Transform.Position3D.Y ?? 0;
        var rightPaddleY = worldAfterPaddles.Entities
            .FirstOrDefault(entity => entity.ComponentString(PaddleComponentType, "side", null) == "right")
            ?.Transform.Position3D.Y ?? 0;

        var updatedWorld = worldAfterPaddles.UpdateEntitiesWithComponent(BallComponentType, entity =>
        {
            var currentPhase = entity.ComponentString(BallComponentType, "phase", "serving");
            var scoreLeft = entity.ComponentNumber(BallComponentType, "scoreLeft", 0);
            var scoreRight = entity.ComponentNumber(BallComponentType, "scoreRight", 0);

            if (resetPressed)
            {
                entity = entity
                    .WithComponentNumber(BallComponentType, "scoreLeft", 0)
                    .WithComponentNumber(BallComponentType, "scoreRight", 0)
                    .WithComponentNumber(BallComponentType, "velocityX", 0)
                    .WithComponentNumber(BallComponentType, "velocityY", 0)
                    .WithComponentNumber(BallComponentType, "serveTimer", 1)
                    .WithComponentString(BallComponentType, "phase", "serving");
                return entity.WithPosition3D(new RekallAgeRuntimeVector3(0, 0, entity.Transform.Position3D.Z));
            }

            if (currentPhase == "gameover")
            {
                return entity;
            }

            if (currentPhase == "serving")
            {
                var serveTimer = entity.ComponentNumber(BallComponentType, "serveTimer", 1) - seconds;
                if (serveTimer > 0)
                {
                    return entity.WithComponentNumber(BallComponentType, "serveTimer", serveTimer);
                }

                var serveDirection = entity.ComponentNumber(BallComponentType, "serveDirection", 1);
                // A dead-center vertical launch makes the AI's perfect Y-tracking rally forever
                // in a straight line, so each serve alternates a deterministic vertical component
                // (paired with serveDirection) instead of leaving velocityY at zero.
                return entity
                    .WithComponentNumber(BallComponentType, "serveTimer", 0)
                    .WithComponentString(BallComponentType, "phase", "playing")
                    .WithComponentNumber(BallComponentType, "velocityX", serveDirection * ServeSpeed)
                    .WithComponentNumber(BallComponentType, "velocityY", serveDirection * ServeVerticalSpeed);
            }

            // currentPhase == "playing"
            var velocityX = entity.ComponentNumber(BallComponentType, "velocityX", 0);
            var velocityY = entity.ComponentNumber(BallComponentType, "velocityY", 0);
            var position = entity.Transform.Position3D;
            var newX = position.X + velocityX * seconds;
            var newY = position.Y + velocityY * seconds;

            if (newY >= ArenaHalfHeight - WallBounceMargin)
            {
                newY = ArenaHalfHeight - WallBounceMargin;
                velocityY = -Math.Abs(velocityY);
            }
            else if (newY <= -(ArenaHalfHeight - WallBounceMargin))
            {
                newY = -(ArenaHalfHeight - WallBounceMargin);
                velocityY = Math.Abs(velocityY);
            }

            var paddleReach = PaddleHalfDepth + BallRadius;
            if (velocityX < 0
                && newX <= PaddleXLeft + paddleReach
                && position.X > PaddleXLeft + paddleReach
                && Math.Abs(newY - leftPaddleY) <= PaddleHalfHeight + BallRadius)
            {
                newX = PaddleXLeft + paddleReach;
                velocityX = Math.Min(Math.Abs(velocityX) * SpeedRampPerHit, MaxBallSpeed);
                velocityY = Math.Clamp((newY - leftPaddleY) * PaddleAngleFactor, -MaxBallSpeed, MaxBallSpeed);
            }
            else if (velocityX > 0
                && newX >= PaddleXRight - paddleReach
                && position.X < PaddleXRight - paddleReach
                && Math.Abs(newY - rightPaddleY) <= PaddleHalfHeight + BallRadius)
            {
                newX = PaddleXRight - paddleReach;
                velocityX = -Math.Min(Math.Abs(velocityX) * SpeedRampPerHit, MaxBallSpeed);
                velocityY = Math.Clamp((newY - rightPaddleY) * PaddleAngleFactor, -MaxBallSpeed, MaxBallSpeed);
            }

            if (newX < -ArenaHalfWidth)
            {
                return StartServe(entity, scoreLeft, scoreRight + 1, direction: -1);
            }

            if (newX > ArenaHalfWidth)
            {
                return StartServe(entity, scoreLeft + 1, scoreRight, direction: 1);
            }

            entity = entity
                .WithComponentNumber(BallComponentType, "velocityX", velocityX)
                .WithComponentNumber(BallComponentType, "velocityY", velocityY);
            return entity.WithPosition3D(new RekallAgeRuntimeVector3(newX, newY, position.Z));
        });

        var refreshedBall = updatedWorld.Entities.FirstOrDefault(entity =>
            entity.Components.Any(component => component.Type == BallComponentType));
        var scoreLeftText = refreshedBall?.ComponentNumber(BallComponentType, "scoreLeft", 0).ToString("0") ?? "0";
        var scoreRightText = refreshedBall?.ComponentNumber(BallComponentType, "scoreRight", 0).ToString("0") ?? "0";

        var worldWithHud = updatedWorld.UpdateEntitiesWithComponent(LabelComponentType, entity => entity.Name switch
        {
            "ScoreLeftLabel" => entity.WithComponentString(LabelComponentType, "text", scoreLeftText),
            "ScoreRightLabel" => entity.WithComponentString(LabelComponentType, "text", scoreRightText),
            _ => entity,
        });

        return ValueTask.FromResult(worldWithHud);
    }

    private static RekallAgeRuntimeEntity StartServe(
        RekallAgeRuntimeEntity entity,
        double scoreLeft,
        double scoreRight,
        double direction)
    {
        var phase = scoreLeft >= WinningScore || scoreRight >= WinningScore ? "gameover" : "serving";
        entity = entity
            .WithComponentNumber(BallComponentType, "scoreLeft", scoreLeft)
            .WithComponentNumber(BallComponentType, "scoreRight", scoreRight)
            .WithComponentNumber(BallComponentType, "velocityX", 0)
            .WithComponentNumber(BallComponentType, "velocityY", 0)
            .WithComponentNumber(BallComponentType, "serveTimer", 1)
            .WithComponentNumber(BallComponentType, "serveDirection", direction)
            .WithComponentString(BallComponentType, "phase", phase);
        return entity.WithPosition3D(new RekallAgeRuntimeVector3(0, 0, entity.Transform.Position3D.Z));
    }
}
