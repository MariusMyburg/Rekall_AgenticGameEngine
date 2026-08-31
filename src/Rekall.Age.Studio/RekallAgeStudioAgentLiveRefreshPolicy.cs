namespace Rekall.Age.Studio;

internal static class RekallAgeStudioAgentLiveRefreshPolicy
{
    private static readonly string[] MutationPrefixes =
    [
        "rekall.entity.",
        "rekall.component.",
        "rekall.scene.apply_",
        "rekall.geometry.",
        "rekall.level.",
        "rekall.mesh.",
        "rekall.modeling.",
        "rekall.material.",
        "rekall.module."
    ];

    internal static bool ShouldRefresh(string toolName, bool succeeded) =>
        succeeded
        && MutationPrefixes.Any(prefix => toolName.StartsWith(prefix, StringComparison.Ordinal));
}
