using Rekall.Age.Modules;

namespace Game.Modules.ClockworkPlayable;

[RekallAgeModule("clockwork.playable", "Clockwork Canopy Playable")]
[RekallAgeRequiresCapability("world")]
public sealed class ClockworkPlayableModule : RekallAgeModule, IRekallAgePlayableModule
{
    public string Kind => "agent-authored";

    public override void Configure(RekallAgeModuleBuilder builder)
    {
    }

    public RekallAgePlayableModuleState CreateInitialState(RekallAgePlayableModuleContext context)
    {
        var state = new RekallAgePlayableModuleState();
        state.Numbers["frame"] = 0;
        state.Text["scene"] = context.SceneName;
        return state;
    }

    public void Tick(RekallAgePlayableModuleState state, RekallAgePlayableModuleInput input)
    {
        if (input.DeltaSeconds > 0)
        {
            state.Numbers["frame"] += 1;
        }
    }

    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state)
    {
        var frame = (int)state.Numbers["frame"];
        return new RekallAgePlayableModuleFrame(
            "CLOCKWORK CANOPY  -  a side-scrolling platform adventure\n"
            + $"Scene {state.Text["scene"]}\n"
            + $"Frame {frame}\n"
            + "Pip the clockwork sprite runs, jumps, collects glows, and dodges sentries.\n"
            + "A / D move  -  W jump  -  R reset  -  reach the Spire to win.");
    }
}
