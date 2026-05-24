using Rekall.Age.Playback;
using Rekall.Age.World;

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
var game = RekallAgePlayableGameFactory.Create(projectRoot, scene);

if (frames is not null)
{
    for (var i = 0; i < frames.Value; i++)
    {
        game.Tick(RekallAgePlaybackInput.None);
        Console.WriteLine($"FRAME {i + 1}");
        Console.Write(game.RenderAscii());
    }

    return 0;
}

if (useGraphics)
{
    Console.Error.WriteLine("Graphical backends are internal Rekall renderer targets; this build has no native window backend yet.");
    return 3;
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

static RekallAgePlaybackInput? ReadInput()
{
    if (!Console.KeyAvailable)
    {
        return RekallAgePlaybackInput.None;
    }

    var key = Console.ReadKey(intercept: true).Key;
    return key switch
    {
        ConsoleKey.Q or ConsoleKey.Escape => null,
        ConsoleKey.W or ConsoleKey.UpArrow => RekallAgePlaybackInput.Up,
        ConsoleKey.S or ConsoleKey.DownArrow => RekallAgePlaybackInput.Down,
        _ => RekallAgePlaybackInput.None
    };
}
