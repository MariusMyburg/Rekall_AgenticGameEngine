using System.Text.Json;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Core.Product;

public static class RekallAgePackagedLaunchResolver
{
    public const string ManifestFileName = "rekall.package.json";
    public const string ManifestKind = "rekall.age.playable.package";

    public static string[] Resolve(string executablePath, IReadOnlyList<string> suppliedArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(suppliedArguments);
        if (suppliedArguments.Count > 0)
        {
            return suppliedArguments.ToArray();
        }

        var packageRoot = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            throw new InvalidDataException("The packaged player executable has no containing directory.");
        }

        var manifestPath = Path.Combine(packageRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException($"Packaged player manifest '{ManifestFileName}' was not found beside the executable.");
        }

        PackagedLaunchManifest manifest;
        try
        {
            using var stream = File.OpenRead(manifestPath);
            manifest = JsonSerializer.Deserialize<PackagedLaunchManifest>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("The packaged player manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The packaged player manifest is invalid JSON.", exception);
        }

        if (!string.Equals(manifest.Kind, ManifestKind, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The packaged player manifest kind must be '{ManifestKind}'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.GameRoot) || Path.IsPathFullyQualified(manifest.GameRoot))
        {
            throw new InvalidDataException("The packaged game root must be a relative path.");
        }

        if (manifest.GameRoot.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException("The packaged game root must not contain traversal segments.");
        }

        if (string.IsNullOrWhiteSpace(manifest.SceneName))
        {
            throw new InvalidDataException("The packaged scene name is missing.");
        }

        if (manifest.Arguments is null || manifest.Arguments.Count < 2 ||
            !PathsEqual(manifest.Arguments[0], manifest.GameRoot) ||
            !string.Equals(manifest.Arguments[1], manifest.SceneName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The packaged launch arguments do not match the manifest game root and scene name.");
        }

        var resolvedGameRoot = Path.GetFullPath(Path.Combine(packageRoot, manifest.GameRoot));
        var normalizedPackageRoot = Path.GetFullPath(packageRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!resolvedGameRoot.StartsWith(
                normalizedPackageRoot + Path.DirectorySeparatorChar,
                PathComparison))
        {
            throw new InvalidDataException("The packaged game root escapes the package directory.");
        }

        try
        {
            resolvedGameRoot = RekallAgeConfinedPath.Resolve(
                normalizedPackageRoot,
                resolvedGameRoot,
                "Packaged game root");
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The packaged game root escapes the package directory.", exception);
        }

        return [resolvedGameRoot, manifest.SceneName, .. manifest.Arguments.Skip(2)];
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
            right.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
            PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record PackagedLaunchManifest(
        string? Kind,
        string? GameRoot,
        string? SceneName,
        IReadOnlyList<string>? Arguments);
}
