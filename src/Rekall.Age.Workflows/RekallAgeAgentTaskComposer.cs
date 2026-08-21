namespace Rekall.Age.Workflows;

public static class RekallAgeAgentTaskComposer
{
    public static string Compose(string projectRoot, string sceneName, string userRequest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userRequest);

        return $"""
            Open project root: {Path.GetFullPath(projectRoot)}
            Active scene: {sceneName.Trim()}

            <user-request>
            {userRequest.Trim()}
            </user-request>

            The user-request block is the authoritative product intent, written in ordinary user language. Preserve its scope and make the technical authoring, rights/provenance, validation, runtime testing, evidence-driven revision, and delivery decisions required to finish it without expecting the user to name engine tools or internal proof operations.
            Work only inside the open project root above. Use canonical Rekall AGE tools to inspect and author the game; do not invent engine operations or author outside that root. Preserve generic engine architecture and put game-specific behavior in project modules or authored scene content.
            """;
    }
}
