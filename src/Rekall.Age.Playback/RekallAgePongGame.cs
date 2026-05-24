using System.Text;

namespace Rekall.Age.Playback;

public sealed class RekallAgePongGame : IRekallAgePlayableGame
{
    private const int PaddleHeight = 5;
    private const double PaddleSpeed = 1.2;
    private const double AiSpeed = 0.8;

    public RekallAgePongGame(RekallAgePongState state, IReadOnlyList<string> entityNames)
    {
        State = state;
        EntityNames = entityNames;
    }

    public string Kind => "pong";

    public IReadOnlyList<string> EntityNames { get; }

    public RekallAgePongState State { get; private set; }

    public static RekallAgePongGame CreateDefault()
    {
        const int width = 48;
        const int height = 18;
        return new RekallAgePongGame(
            new RekallAgePongState(
                width,
                height,
                height / 2.0,
                height / 2.0,
                width / 2.0,
                height / 2.0,
                0.75,
                0.35,
                0,
                0),
            ["LeftPaddle", "RightPaddle", "Ball", "Score"]);
    }

    public static RekallAgePongGame FromEntities(IReadOnlyList<string> entityNames)
    {
        return new RekallAgePongGame(CreateDefault().State, entityNames);
    }

    public void Tick(RekallAgePongInput input)
    {
        var leftY = ClampPaddle(State.LeftPaddleY + input.LeftPaddleDirection * PaddleSpeed, State.Height);
        var aiDirection = Math.Sign(State.BallY - State.RightPaddleY);
        var rightY = ClampPaddle(State.RightPaddleY + aiDirection * AiSpeed, State.Height);
        var ballX = State.BallX + State.BallVelocityX;
        var ballY = State.BallY + State.BallVelocityY;
        var vx = State.BallVelocityX;
        var vy = State.BallVelocityY;
        var leftScore = State.LeftScore;
        var rightScore = State.RightScore;

        if (ballY <= 1 || ballY >= State.Height - 2)
        {
            vy *= -1;
            ballY = Math.Clamp(ballY, 1, State.Height - 2);
        }

        if (ballX <= 3 && Math.Abs(ballY - leftY) <= PaddleHeight / 2.0)
        {
            vx = Math.Abs(vx);
            ballX = 3;
        }

        if (ballX >= State.Width - 4 && Math.Abs(ballY - rightY) <= PaddleHeight / 2.0)
        {
            vx = -Math.Abs(vx);
            ballX = State.Width - 4;
        }

        if (ballX < 0)
        {
            rightScore++;
            ballX = State.Width / 2.0;
            ballY = State.Height / 2.0;
            vx = Math.Abs(vx);
        }
        else if (ballX > State.Width - 1)
        {
            leftScore++;
            ballX = State.Width / 2.0;
            ballY = State.Height / 2.0;
            vx = -Math.Abs(vx);
        }

        State = State with
        {
            LeftPaddleY = leftY,
            RightPaddleY = rightY,
            BallX = ballX,
            BallY = ballY,
            BallVelocityX = vx,
            BallVelocityY = vy,
            LeftScore = leftScore,
            RightScore = rightScore
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

        DrawPaddle(buffer, 2, (int)Math.Round(State.LeftPaddleY));
        DrawPaddle(buffer, State.Width - 3, (int)Math.Round(State.RightPaddleY));
        buffer[(int)Math.Round(State.BallY), (int)Math.Round(State.BallX)] = 'O';

        var score = $"{State.LeftScore} : {State.RightScore}";
        var scoreX = Math.Max(0, (State.Width - score.Length) / 2);
        for (var i = 0; i < score.Length; i++)
        {
            buffer[1, scoreX + i] = score[i];
        }

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

    private static void DrawPaddle(char[,] buffer, int x, int centerY)
    {
        var height = buffer.GetLength(0);
        for (var y = centerY - PaddleHeight / 2; y <= centerY + PaddleHeight / 2; y++)
        {
            if (y > 0 && y < height - 1)
            {
                buffer[y, x] = '|';
            }
        }
    }

    private static double ClampPaddle(double y, int height)
    {
        return Math.Clamp(y, 1 + PaddleHeight / 2.0, height - 2 - PaddleHeight / 2.0);
    }
}
