namespace Rekall.Age.Rendering;

public sealed record RekallAgePlayerPresentationPlan(
    bool AuthoredUiEnabled,
    bool DebugHudEnabled);

public static class RekallAgePlayerPresentationPolicy
{
    public static RekallAgePlayerPresentationPlan Plan(IReadOnlyList<string> arguments) =>
        new(
            AuthoredUiEnabled: true,
            DebugHudEnabled: arguments.Skip(2).Any(argument =>
                argument.Equals("--debug-hud", StringComparison.OrdinalIgnoreCase)));
}
