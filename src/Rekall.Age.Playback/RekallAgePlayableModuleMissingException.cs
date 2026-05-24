namespace Rekall.Age.Playback;

public sealed class RekallAgePlayableModuleMissingException : InvalidOperationException
{
    public RekallAgePlayableModuleMissingException(string projectRoot, string sceneName)
        : base($"Project '{projectRoot}' scene '{sceneName}' has no compiled IRekallAgePlayableModule.")
    {
        ProjectRoot = projectRoot;
        SceneName = sceneName;
    }

    public string ProjectRoot { get; }

    public string SceneName { get; }
}
