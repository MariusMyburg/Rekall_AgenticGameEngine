using System.IO;
using Rekall.Age.Editor;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeCreateProjectRequest(
    string ProjectRoot,
    string ProjectName,
    string SceneName)
{
    public static bool TryCreate(
        string parentFolder,
        string projectName,
        string sceneName,
        out RekallAgeCreateProjectRequest? request,
        out string error)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(parentFolder))
        {
            error = "Choose a folder for the new project.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(projectName))
        {
            error = "Enter a project name.";
            return false;
        }
        if (!RekallAgeWorkbenchSession.IsSafeNameSegment(projectName.Trim()))
        {
            error = "The project name must be a single safe folder-name segment.";
            return false;
        }
        if (!RekallAgeWorkbenchSession.IsSafeNameSegment(sceneName.Trim()))
        {
            error = "The initial scene name must be a single safe file-name segment.";
            return false;
        }

        try
        {
            request = new RekallAgeCreateProjectRequest(
                Path.GetFullPath(Path.Combine(parentFolder.Trim(), projectName.Trim())),
                projectName.Trim(),
                sceneName.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "Choose a valid project location.";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
