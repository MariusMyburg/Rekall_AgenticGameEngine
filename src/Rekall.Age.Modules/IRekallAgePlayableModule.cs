namespace Rekall.Age.Modules;

public interface IRekallAgePlayableModule
{
    string Kind { get; }

    RekallAgePlayableModuleState CreateInitialState(RekallAgePlayableModuleContext context);

    void Tick(RekallAgePlayableModuleState state, RekallAgePlayableModuleInput input);

    RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state);
}
