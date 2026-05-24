using Rekall.Age.Playback;
using Rekall.Age.World;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: rekall-age-player <project-root> <scene-name> [--frames <count>]");
    return 2;
}

var projectRoot = args[0];
var sceneName = args[1];
var frames = TryReadFrameCount(args);
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
