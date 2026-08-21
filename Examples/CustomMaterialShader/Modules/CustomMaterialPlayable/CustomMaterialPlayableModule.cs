using Rekall.Age.Modules;

namespace Game.Modules.CustomMaterialPlayable;

[RekallAgeModule("example.custom_material_playable", "Custom Material Playable")]
[RekallAgeRequiresCapability("world")]
public sealed class CustomMaterialPlayableModule : RekallAgeModule, IRekallAgePlayableModule
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
        return new RekallAgePlayableModuleFrame($"AGENT PLAYABLE MODULE\nScene {state.Text["scene"]}\nFrame {frame}");
    }
}
