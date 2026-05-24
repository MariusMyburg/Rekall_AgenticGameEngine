namespace Rekall.Age.Tests;

internal static class TestPaths
{
    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "rekall-age-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
