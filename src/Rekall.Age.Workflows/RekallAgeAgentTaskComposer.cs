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

            Studio has collected the user's final authoring intent. It is final and approved for immediate execution in this run. Do not run a brainstorming or approval-gathering step, do not present a proposed design for confirmation, and do not ask whether to proceed. Author the requested result now, verify it, and report what actually changed.
            The user-request block is the authoritative product intent, written in ordinary user language. Preserve its scope and make the technical authoring, rights/provenance, validation, runtime testing, evidence-driven revision, and delivery decisions required to finish it without expecting the user to name engine tools or internal proof operations.
            Work only inside the open project root above. Use canonical Rekall AGE tools to inspect and author the game; do not invent engine operations or author outside that root. Preserve generic engine architecture and put game-specific behavior in project modules or authored scene content.
            """;
    }
}
