using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.FleetRules;

[RekallAgeComponent("Intro Sequence", Description =
    "Reveals narrative text a character at a time, then hands over to the next scene. Lines is " +
    "an array of strings; a blank string is a paragraph break.")]
public sealed class IntroSequence : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    /// <summary>Array of lines. Blank entries read as paragraph breaks.</summary>
    [RekallAgeProperty]
    public object? Lines { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 600)]
    public double Elapsed { get; init; }

    [RekallAgeProperty(Minimum = 1, Maximum = 400)]
    public double CharactersPerSecond { get; init; } = 42;

    /// <summary>How long the finished text stays up before handing over.</summary>
    [RekallAgeProperty(Minimum = 0, Maximum = 60)]
    public double HoldSeconds { get; init; } = 4;

    [RekallAgeProperty]
    public string TargetScene { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string TextEntityName { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string PromptEntityName { get; init; } = string.Empty;

    [RekallAgeProperty]
    public bool Finished { get; init; }
}

/// <summary>
/// Types the prologue onto the screen and then moves on.
///
/// The reveal is derived from elapsed time rather than accumulated per step, so the text
/// appears at the authored rate no matter how many fixed steps a frame happens to run - the
/// same reason the fighter orbits are solved absolutely.
///
/// A click or a key press skips ahead: first to the full text, then past the hold. Making the
/// player sit through prose they have already read is the fastest way to have them resent it.
/// </summary>
public sealed class IntroSystem : IRekallAgeRuntimeModuleSystem
{
    private const string IntroType = "Game.Modules.FleetRules.IntroSequence";
    private const string UiElementType = "Rekall.UiElement";
    private const string SceneTransitionType = "Rekall.SceneTransition";
    private const string ShellTransitionType = "Game.Modules.FleetRules.ShellTransition";

    public string Id => "game.intro";

    public int Priority => -60;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var host = world.Entities.FirstOrDefault(entity => entity.FindComponent(IntroType) is not null);
        if (host is null || !host.ComponentBoolean(IntroType, "enabled", true))
        {
            return ValueTask.FromResult(world);
        }

        var component = host.FindComponent(IntroType)!;
        var lines = ReadLines(component.Properties);
        var full = string.Join("\n", lines);
        var charactersPerSecond = Math.Max(1, host.ComponentNumber(IntroType, "charactersPerSecond", 42));
        var holdSeconds = Math.Max(0, host.ComponentNumber(IntroType, "holdSeconds", 4));
        var elapsed = host.ComponentNumber(IntroType, "elapsed") + context.DeltaTime.TotalSeconds;
        var finished = host.ComponentBoolean(IntroType, "finished");

        var revealSeconds = full.Length / charactersPerSecond;
        var skipped = context.Input.PressedButtonsThisFrame?.Count > 0
            || context.Input.PressedKeysThisFrame?.Count > 0;
        if (skipped)
        {
            // First skip completes the text; a second one moves on.
            elapsed = elapsed < revealSeconds ? revealSeconds : revealSeconds + holdSeconds;
        }

        var visibleCount = (int)Math.Clamp(elapsed * charactersPerSecond, 0, full.Length);
        var visible = full[..visibleCount];
        var complete = visibleCount >= full.Length;
        var handOver = complete && elapsed >= revealSeconds + holdSeconds;

        var textName = host.ComponentString(IntroType, "textEntityName", string.Empty) ?? string.Empty;
        var promptName = host.ComponentString(IntroType, "promptEntityName", string.Empty) ?? string.Empty;
        var target = host.ComponentString(IntroType, "targetScene", string.Empty) ?? string.Empty;

        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);
        foreach (var entity in world.Entities)
        {
            var next = entity;

            if (next.Id.Equals(host.Id, StringComparison.Ordinal))
            {
                next = next.WithComponentNumber(IntroType, "elapsed", elapsed);
                if (handOver && !finished && target.Length > 0)
                {
                    // Hand to the shell if one is present so the screen fades out; otherwise
                    // request the scene directly.
                    next = next.FindComponent(ShellTransitionType) is not null
                        ? next
                            .WithComponentString(ShellTransitionType, "targetScene", target)
                            .WithComponentString(ShellTransitionType, "phase", "fadingOut")
                            .WithComponentNumber(ShellTransitionType, "elapsed", 0)
                        : next.WithComponentString(SceneTransitionType, "requestedScene", target);
                    next = next.WithComponentBoolean(IntroType, "finished", true);
                }
            }

            if (textName.Length > 0
                && next.Name.Equals(textName, StringComparison.Ordinal)
                && next.FindComponent(UiElementType) is not null
                && !string.Equals(
                    next.ComponentString(UiElementType, "text", string.Empty),
                    visible,
                    StringComparison.Ordinal))
            {
                next = next.WithComponentString(UiElementType, "text", visible);
            }

            if (promptName.Length > 0 && next.Name.Equals(promptName, StringComparison.Ordinal)
                && next.FindComponent(UiElementType) is not null)
            {
                var prompt = complete ? "PRESS ANY KEY" : "PRESS ANY KEY TO SKIP";
                if (!string.Equals(
                        next.ComponentString(UiElementType, "text", string.Empty),
                        prompt,
                        StringComparison.Ordinal))
                {
                    next = next.WithComponentString(UiElementType, "text", prompt);
                }
            }

            entities.Add(next);
        }

        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static IReadOnlyList<string> ReadLines(JsonObject properties)
    {
        if (properties["lines"] is not JsonArray array)
        {
            return [];
        }

        var lines = new List<string>(array.Count);
        foreach (var node in array)
        {
            lines.Add(node?.GetValue<string>() ?? string.Empty);
        }

        return lines;
    }
}
