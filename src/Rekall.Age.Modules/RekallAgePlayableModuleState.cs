namespace Rekall.Age.Modules;

public sealed class RekallAgePlayableModuleState
{
    public Dictionary<string, double> Numbers { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
}
