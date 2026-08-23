namespace Rekall.Age.Rendering;

public enum RekallAgePlayerContentMode
{
    RuntimeScene,
    LegacyProofAdapter
}

public sealed record RekallAgePlayerContentModePlan(
    RekallAgePlayerContentMode Mode,
    bool ObsoletePlayableFlagPresent);

public static class RekallAgePlayerContentModePlanner
{
    private const string LegacyProofAdapterOption = "--legacy-playable-adapter";
    private const string ObsoletePlayableOption = "--playable";

    public static RekallAgePlayerContentModePlan Plan(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var legacyProofAdapter = arguments.Any(argument =>
            argument.Equals(LegacyProofAdapterOption, StringComparison.OrdinalIgnoreCase));
        var obsoletePlayable = arguments.Any(argument =>
            argument.Equals(ObsoletePlayableOption, StringComparison.OrdinalIgnoreCase));
        return new RekallAgePlayerContentModePlan(
            legacyProofAdapter
                ? RekallAgePlayerContentMode.LegacyProofAdapter
                : RekallAgePlayerContentMode.RuntimeScene,
            obsoletePlayable);
    }
}
