using Rekall.Age.Playback;
using Rekall.Age.World;
using Raylib_cs;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: rekall-age-player <project-root> <scene-name> [--frames <count>] [--graphics]");
    return 2;
}

var projectRoot = args[0];
var sceneName = args[1];
var frames = TryReadFrameCount(args);
var useGraphics = args.Any(arg => arg.Equals("--graphics", StringComparison.Ordinal));
var scene = await new RekallAgeSceneStore().LoadAsync(projectRoot, sceneName, CancellationToken.None);
var game = RekallAgePlayableGameFactory.Create(scene);

if (frames is not null)
{
    for (var i = 0; i < frames.Value; i++)
    {
        game.Tick(RekallAgePongInput.None);
        Console.WriteLine($"FRAME {i + 1}");
        Console.Write(game.RenderAscii());
    }

    return 0;
}

if (useGraphics)
{
    RunGraphics(game);
    return 0;
}

Console.CursorVisible = false;
try
{
    while (true)
    {
        var input = ReadInput();
        if (input is null)
        {
            break;
        }

        game.Tick(input.Value);
        Console.SetCursorPosition(0, 0);
        Console.Write(game.RenderAscii());
        Console.WriteLine("W/S move left paddle. Q quits.");
        await Task.Delay(33);
    }
}
finally
{
    Console.CursorVisible = true;
}

return 0;

static int? TryReadFrameCount(string[] args)
{
    for (var i = 2; i < args.Length - 1; i++)
    {
        if (args[i].Equals("--frames", StringComparison.Ordinal) && int.TryParse(args[i + 1], out var frames))
        {
            return Math.Max(1, frames);
        }
    }

    return null;
}

static RekallAgePongInput? ReadInput()
{
    if (!Console.KeyAvailable)
    {
        return RekallAgePongInput.None;
    }

    var key = Console.ReadKey(intercept: true).Key;
    return key switch
    {
        ConsoleKey.Q or ConsoleKey.Escape => null,
        ConsoleKey.W or ConsoleKey.UpArrow => RekallAgePongInput.Up,
        ConsoleKey.S or ConsoleKey.DownArrow => RekallAgePongInput.Down,
        _ => RekallAgePongInput.None
    };
}

static void RunGraphics(IRekallAgePlayableGame game)
{
    const int width = 960;
    const int height = 540;
    Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
    Raylib.InitWindow(width, height, "Rekall AGE Player");
    Raylib.SetTargetFPS(60);

    try
    {
        while (!Raylib.WindowShouldClose())
        {
            var input = ReadRaylibInput();
            game.Tick(input);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(8, 18, 28, 255));
            DrawGame(game, Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
            Raylib.EndDrawing();
        }
    }
    finally
    {
        Raylib.CloseWindow();
    }
}

static RekallAgePongInput ReadRaylibInput()
{
    if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up))
    {
        return RekallAgePongInput.Up;
    }

    if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down))
    {
        return RekallAgePongInput.Down;
    }

    return RekallAgePongInput.None;
}

static void DrawGame(IRekallAgePlayableGame game, int width, int height)
{
    if (game is not RekallAgePongGame pong)
    {
        Raylib.DrawText($"Unsupported playable game: {game.Kind}", 32, 32, 24, Color.White);
        return;
    }

    var frame = RekallAgePongRenderFrame.FromGame(pong, width, height);
    Raylib.DrawRectangle(0, 0, frame.Width, frame.Height, new Color(8, 18, 28, 255));
    Raylib.DrawLine(0, 0, frame.Width, 0, new Color(180, 220, 230, 255));
    Raylib.DrawLine(0, frame.Height - 1, frame.Width, frame.Height - 1, new Color(180, 220, 230, 255));

    foreach (var rectangle in frame.Rectangles)
    {
        Raylib.DrawRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, Color.White);
    }

    foreach (var circle in frame.Circles)
    {
        Raylib.DrawCircle(circle.CenterX, circle.CenterY, circle.Radius, new Color(255, 220, 90, 255));
    }

    var textWidth = Raylib.MeasureText(frame.ScoreText.Text, frame.ScoreText.FontSize);
    Raylib.DrawText(
        frame.ScoreText.Text,
        frame.ScoreText.CenterX - textWidth / 2,
        frame.ScoreText.BaselineY,
        frame.ScoreText.FontSize,
        Color.White);
    Raylib.DrawText("W/S or arrows move. Esc closes.", 24, frame.Height - 40, 20, new Color(140, 180, 190, 255));
}
