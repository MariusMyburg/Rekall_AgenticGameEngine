using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.FleetRules;

[RekallAgeComponent("Shell Transition", Description =
    "Drives screen flow: fades the screen and the music in when a scene starts, and fades them " +
    "back out before handing over to the next scene. Attach one per screen.")]
public sealed class ShellTransition : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    /// <summary>fadingIn, idle, or fadingOut.</summary>
    [RekallAgeProperty(AllowedValues = ["fadingIn", "idle", "fadingOut"])]
    public string Phase { get; init; } = "fadingIn";

    [RekallAgeProperty(Minimum = 0, Maximum = 60)]
    public double Elapsed { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 30)]
    public double FadeInSeconds { get; init; } = 2.0;

    [RekallAgeProperty(Minimum = 0, Maximum = 30)]
    public double FadeOutSeconds { get; init; } = 1.5;

    /// <summary>Scene to load once the outward fade finishes.</summary>
    [RekallAgeProperty]
    public string TargetScene { get; init; } = string.Empty;

    /// <summary>Entity carrying the full-screen Rekall.UiElement used as the fade curtain.</summary>
    [RekallAgeProperty]
    public string OverlayEntityName { get; init; } = string.Empty;

    /// <summary>Entity carrying the Rekall.AudioEmitter whose gain follows the fade.</summary>
    [RekallAgeProperty]
    public string MusicEntityName { get; init; } = string.Empty;

    [RekallAgeProperty(Minimum = 0, Maximum = 4)]
    public double MusicGain { get; init; } = 0.8;
}

[RekallAgeComponent("Menu Action", Description =
    "What a menu button does when clicked. Attach beside a Rekall.UiElement with " +
    "interactive set true.")]
public sealed class MenuAction : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty(AllowedValues = ["loadScene", "quit"])]
    public string Action { get; init; } = "loadScene";

    [RekallAgeProperty]
    public string TargetScene { get; init; } = string.Empty;
}

/// <summary>
/// Screen flow for the shell: fade in on arrival, fade out on departure, then hand over.
///
/// The outward fade is why this system owns the scene change rather than a button writing
/// Rekall.SceneTransition directly: the engine honours a transition request as soon as it sees
/// one, so a button that requested the next scene immediately would cut the music and the
/// picture instantly. The request is written only once the fade has finished.
/// </summary>
public sealed class ShellSystem : IRekallAgeRuntimeModuleSystem
{
    private const string TransitionType = "Game.Modules.FleetRules.ShellTransition";
    private const string MenuActionType = "Game.Modules.FleetRules.MenuAction";
    private const string SceneTransitionType = "Rekall.SceneTransition";
    private const string UiElementType = "Rekall.UiElement";
    private const string AudioEmitterType = "Rekall.AudioEmitter";

    public string Id => "game.shell";

    // After the UI interaction system (priority 20) so this step's pointer.click is visible;
    // event facts are cleared at the start of each frame.
    public int Priority => 31;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var shell = world.Entities.FirstOrDefault(entity => entity.FindComponent(TransitionType) is not null);
        if (shell is null || !shell.ComponentBoolean(TransitionType, "enabled", true))
        {
            return ValueTask.FromResult(world);
        }

        var phase = shell.ComponentString(TransitionType, "phase", "fadingIn") ?? "fadingIn";
        var elapsed = shell.ComponentNumber(TransitionType, "elapsed");
        var fadeIn = Math.Max(0.0001, shell.ComponentNumber(TransitionType, "fadeInSeconds", 2.0));
        var fadeOut = Math.Max(0.0001, shell.ComponentNumber(TransitionType, "fadeOutSeconds", 1.5));
        var target = shell.ComponentString(TransitionType, "targetScene", string.Empty) ?? string.Empty;
        var musicGain = shell.ComponentNumber(TransitionType, "musicGain", 0.8);

        // A click on a button starts the outward fade; it does not change scene itself.
        if (phase != "fadingOut")
        {
            foreach (var runtimeEvent in world.Subsystems.Events.Events)
            {
                if (!runtimeEvent.Type.Equals("pointer.click", StringComparison.Ordinal))
                {
                    continue;
                }

                var button = world.Entities.FirstOrDefault(entity =>
                    entity.Id.Equals(runtimeEvent.EntityId, StringComparison.Ordinal));
                if (button?.FindComponent(MenuActionType) is null
                    || !button.ComponentBoolean(MenuActionType, "enabled", true))
                {
                    continue;
                }

                target = button.ComponentString(MenuActionType, "targetScene", string.Empty) ?? string.Empty;
                if (target.Length > 0)
                {
                    phase = "fadingOut";
                    elapsed = 0;
                }

                break;
            }
        }

        elapsed += context.DeltaTime.TotalSeconds;

        // curtain: 1 is fully black, 0 is fully clear. music follows the inverse.
        double curtain;
        var requestScene = string.Empty;
        switch (phase)
        {
            case "fadingIn":
                curtain = Math.Clamp(1.0 - (elapsed / fadeIn), 0, 1);
                if (elapsed >= fadeIn)
                {
                    phase = "idle";
                    elapsed = 0;
                    curtain = 0;
                }

                break;

            case "fadingOut":
                curtain = Math.Clamp(elapsed / fadeOut, 0, 1);
                if (elapsed >= fadeOut)
                {
                    curtain = 1;
                    requestScene = target;
                }

                break;

            default:
                curtain = 0;
                break;
        }

        var overlayName = shell.ComponentString(TransitionType, "overlayEntityName", string.Empty) ?? string.Empty;
        var musicName = shell.ComponentString(TransitionType, "musicEntityName", string.Empty) ?? string.Empty;
        var curtainColor = "#000000" + ((int)Math.Round(Math.Clamp(curtain, 0, 1) * 255)).ToString("x2");
        var gain = Math.Clamp((1.0 - curtain) * musicGain, 0, 4);

        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);
        foreach (var entity in world.Entities)
        {
            var next = entity;

            if (next.Id.Equals(shell.Id, StringComparison.Ordinal))
            {
                next = next
                    .WithComponentString(TransitionType, "phase", phase)
                    .WithComponentNumber(TransitionType, "elapsed", elapsed)
                    .WithComponentString(TransitionType, "targetScene", target);
                if (requestScene.Length > 0)
                {
                    next = next.WithComponentString(SceneTransitionType, "requestedScene", requestScene);
                }
            }

            if (overlayName.Length > 0
                && next.Name.Equals(overlayName, StringComparison.Ordinal)
                && next.FindComponent(UiElementType) is not null)
            {
                next = next.WithComponentString(UiElementType, "backgroundColor", curtainColor);
            }

            if (musicName.Length > 0
                && next.Name.Equals(musicName, StringComparison.Ordinal)
                && next.FindComponent(AudioEmitterType) is not null)
            {
                next = next.WithComponentNumber(AudioEmitterType, "gain", gain);
            }

            entities.Add(next);
        }

        return ValueTask.FromResult(world with { Entities = entities });
    }
}
