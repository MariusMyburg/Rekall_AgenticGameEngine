using System.Text;

namespace Rekall.Age.Playback;

public sealed class RekallAgeBreakoutGame : IRekallAgePlayableGame
{
    private const int PaddleWidth = 9;
    private const double PaddleSpeed = 1.4;

    private readonly bool[,] _bricks;

    public RekallAgeBreakoutGame(
        RekallAgeBreakoutState state,
        IReadOnlyList<string> entityNames,
        bool[,] bricks)
    {
        State = state;
        EntityNames = entityNames;
        _bricks = bricks;
    }

    public string Kind => "breakout";

    public IReadOnlyList<string> EntityNames { get; }

    public RekallAgeBreakoutState State { get; private set; }

    public static RekallAgeBreakoutGame CreateDefault()
    {
        const int columns = 10;
        const int rows = 5;
        var bricks = new bool[rows, columns];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                bricks[row, column] = true;
            }
        }

        return new RekallAgeBreakoutGame(
            new RekallAgeBreakoutState(
                48,
                22,
                24,
                24,
                16,
                0.65,
                -0.55,
                columns,
                rows,
                columns * rows,
                0),
            ["Paddle", "Ball", "BrickField", "Score"],
            bricks);
    }

    public static RekallAgeBreakoutGame FromEntities(IReadOnlyList<string> entityNames)
    {
        var game = CreateDefault();
        return new RekallAgeBreakoutGame(game.State, entityNames, game._bricks);
    }

    public void Tick(RekallAgePongInput input)
    {
        var paddleX = Math.Clamp(
            State.PaddleX + input.LeftPaddleDirection * PaddleSpeed,
            PaddleWidth / 2.0 + 1,
            State.Width - PaddleWidth / 2.0 - 2);
        var ballX = State.BallX + State.BallVelocityX;
        var ballY = State.BallY + State.BallVelocityY;
        var vx = State.BallVelocityX;
        var vy = State.BallVelocityY;
        var score = State.Score;
        var bricksRemaining = State.BricksRemaining;

        if (ballX <= 1 || ballX >= State.Width - 2)
        {
            vx *= -1;
            ballX = Math.Clamp(ballX, 1, State.Width - 2);
        }

        if (ballY <= 1)
        {
            vy = Math.Abs(vy);
            ballY = 1;
        }

        var paddleY = State.Height - 3;
        if (ballY >= paddleY - 1
            && ballY <= paddleY + 1
            && Math.Abs(ballX - paddleX) <= PaddleWidth / 2.0)
        {
            vy = -Math.Abs(vy);
            ballY = paddleY - 1;
        }

        var hit = TryHitBrick(ballX, ballY);
        if (hit is not null)
        {
            var (row, column) = hit.Value;
            _bricks[row, column] = false;
            bricksRemaining--;
            score += 10;
            vy *= -1;
        }

        if (ballY > State.Height - 1)
        {
            ballX = State.Width / 2.0;
            ballY = State.Height - 6;
            vy = -Math.Abs(vy);
        }

        State = State with
        {
            PaddleX = paddleX,
            BallX = ballX,
            BallY = ballY,
            BallVelocityX = vx,
            BallVelocityY = vy,
            BricksRemaining = bricksRemaining,
            Score = score
        };
    }

    public string RenderAscii()
    {
        var buffer = new char[State.Height, State.Width];
        for (var y = 0; y < State.Height; y++)
        {
            for (var x = 0; x < State.Width; x++)
            {
                buffer[y, x] = ' ';
            }
        }

        for (var x = 0; x < State.Width; x++)
        {
            buffer[0, x] = '-';
            buffer[State.Height - 1, x] = '-';
        }

        DrawText(buffer, 1, 2, $"Score: {State.Score}");
        DrawBricks(buffer);
        DrawPaddle(buffer);
        buffer[(int)Math.Round(State.BallY), (int)Math.Round(State.BallX)] = 'O';

        var builder = new StringBuilder();
        for (var y = 0; y < State.Height; y++)
        {
            for (var x = 0; x < State.Width; x++)
            {
                builder.Append(buffer[y, x]);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private (int Row, int Column)? TryHitBrick(double ballX, double ballY)
    {
        for (var row = 0; row < State.Rows; row++)
        {
            for (var column = 0; column < State.Columns; column++)
            {
                if (!_bricks[row, column])
                {
                    continue;
                }

                var x = 4 + column * 4;
                var y = 3 + row;
                if (Math.Abs(ballX - x) <= 1.5 && Math.Abs(ballY - y) <= 0.5)
                {
                    return (row, column);
                }
            }
        }

        return null;
    }

    private void DrawBricks(char[,] buffer)
    {
        for (var row = 0; row < State.Rows; row++)
        {
            for (var column = 0; column < State.Columns; column++)
            {
                if (!_bricks[row, column])
                {
                    continue;
                }

                var x = 3 + column * 4;
                var y = 3 + row;
                buffer[y, x] = '#';
                buffer[y, x + 1] = '#';
                buffer[y, x + 2] = '#';
            }
        }
    }

    private void DrawPaddle(char[,] buffer)
    {
        var y = State.Height - 3;
        var startX = (int)Math.Round(State.PaddleX) - PaddleWidth / 2;
        for (var x = startX; x < startX + PaddleWidth; x++)
        {
            if (x > 0 && x < State.Width - 1)
            {
                buffer[y, x] = '_';
            }
        }
    }

    private static void DrawText(char[,] buffer, int y, int x, string text)
    {
        for (var i = 0; i < text.Length && x + i < buffer.GetLength(1); i++)
        {
            buffer[y, x + i] = text[i];
        }
    }
}
