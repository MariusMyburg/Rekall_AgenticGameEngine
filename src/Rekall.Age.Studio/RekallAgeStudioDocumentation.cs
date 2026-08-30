using System.Diagnostics;
using System.IO;

namespace Rekall.Age.Studio;

internal static class RekallAgeStudioDocumentation
{
    internal const string FileName = "Rekall-AGE-Documentation.html";

    internal static string ResolvePath(string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        return Path.GetFullPath(Path.Combine(applicationBaseDirectory, "Documentation", FileName));
    }

    internal static void Open(string applicationBaseDirectory, Action<string>? openAssociated = null)
    {
        var path = ResolvePath(applicationBaseDirectory);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Studio documentation is missing from this installation. Repair or reinstall Rekall AGE Studio to restore it.",
                path);
        }

        (openAssociated ?? OpenWithShell)(path);
    }

    private static void OpenWithShell(string path)
    {
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true
        });
    }
}
