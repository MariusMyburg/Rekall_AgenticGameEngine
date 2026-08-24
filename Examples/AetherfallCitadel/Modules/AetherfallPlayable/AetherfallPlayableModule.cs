using Rekall.Age.Modules;

namespace Game.Modules.AetherfallPlayable;

[RekallAgeModule("aetherfall.playable", "Aetherfall Playable")]
[RekallAgeRequiresCapability("world")]
public sealed class AetherfallPlayableModule : RekallAgeModule, IRekallAgePlayableModule
{
    public string Kind => "agent-authored";

    public override void Configure(RekallAgeModuleBuilder builder)
    {
    }

    public RekallAgePlayableModuleState CreateInitialState(RekallAgePlayableModuleContext context)
    {
        var state = new RekallAgePlayableModuleState();
        state.Text["scene"] = context.SceneName;
        state.Numbers["frame"] = 0;
        return state;
    }

    public void Tick(RekallAgePlayableModuleState state, RekallAgePlayableModuleInput input)
    {
        if (input.DeltaSeconds > 0)
        {
            state.Numbers["frame"] += 1;
        }
    }

    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state) => new(
        $"AETHERFALL: CITADEL OF ECHOES\n"
        + $"Scene {state.Text["scene"]} | Runtime ready | Frame {(int)state.Numbers["frame"]}\n"
        + "Objective: gather two Echo Shards and awaken the conduit.\n"
        + "Move WASD | Aim IJKL | Pulse Space | Dash Shift | Interact E | Pause Esc | Reset R");
}
