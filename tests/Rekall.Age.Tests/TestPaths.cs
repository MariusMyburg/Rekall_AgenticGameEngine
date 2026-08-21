namespace Rekall.Age.Tests;

internal static class TestPaths
{
    public static string CreateTempDirectory()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("REKALL_AGE_TEST_TEMP_ROOT");
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Path.GetTempPath(), "rekall-age-tests")
            : Path.GetFullPath(configuredRoot);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
