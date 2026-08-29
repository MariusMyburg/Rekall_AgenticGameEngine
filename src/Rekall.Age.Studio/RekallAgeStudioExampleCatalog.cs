using System.IO;
using System.Text.Json;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioExample(
    string FolderName,
    string DisplayName,
    string SourceRoot,
    IReadOnlyList<string> Capabilities);

internal sealed record RekallAgeStudioExampleCatalogIssue(
    string FolderName,
    string ManifestPath,
    string Message);

internal sealed record RekallAgeStudioExampleCatalogResult(
    IReadOnlyList<RekallAgeStudioExample> Examples,
    IReadOnlyList<RekallAgeStudioExampleCatalogIssue> Issues);

internal sealed class RekallAgeStudioExampleCatalog
{
    private const string ProjectManifestFileName = "rekall.project.json";
    private readonly IReadOnlyList<string> _searchRoots;

    public RekallAgeStudioExampleCatalog(IEnumerable<string> searchRoots)
    {
        ArgumentNullException.ThrowIfNull(searchRoots);
        _searchRoots = searchRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .ToArray();
    }

    public static RekallAgeStudioExampleCatalog CreateDefault()
    {
        var applicationRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var roots = new List<string>
        {
            Path.Combine(applicationRoot, "Examples")
        };

        for (var ancestor = new DirectoryInfo(applicationRoot); ancestor is not null; ancestor = ancestor.Parent)
        {
            if (!File.Exists(Path.Combine(ancestor.FullName, "Rekall.AGE.sln"))) continue;
            roots.Add(Path.Combine(ancestor.FullName, "Examples"));
            break;
        }

        return new RekallAgeStudioExampleCatalog(roots);
    }

    public RekallAgeStudioExampleCatalogResult Discover()
    {
        var examples = new Dictionary<string, RekallAgeStudioExample>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<RekallAgeStudioExampleCatalogIssue>();
        foreach (var root in _searchRoots.Where(Directory.Exists))
        {
            string[] directories;
            try
            {
                directories = Directory.GetDirectories(root);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add(new RekallAgeStudioExampleCatalogIssue(
                    Path.GetFileName(root),
                    root,
                    exception.Message));
                continue;
            }

            foreach (var directory in directories
                         .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                var folderName = Path.GetFileName(directory);
                if (examples.ContainsKey(folderName))
                {
                    continue;
                }

                var manifestPath = Path.Combine(directory, ProjectManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                try
                {
                    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    var rootElement = manifest.RootElement;
                    if (rootElement.ValueKind != JsonValueKind.Object ||
                        !rootElement.TryGetProperty("name", out var nameElement) ||
                        nameElement.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(nameElement.GetString()))
                    {
                        throw new InvalidDataException("The project manifest must contain a non-empty string 'name'.");
                    }

                    var capabilities = ReadCapabilities(rootElement);
                    examples.Add(
                        folderName,
                        new RekallAgeStudioExample(
                            folderName,
                            nameElement.GetString()!.Trim(),
                            Path.GetFullPath(directory),
                            capabilities));
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    issues.Add(new RekallAgeStudioExampleCatalogIssue(
                        folderName,
                        manifestPath,
                        exception.Message));
                }
            }
        }

        return new RekallAgeStudioExampleCatalogResult(
            examples.Values
                .OrderBy(example => example.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(example => example.FolderName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            issues);
    }

    private static IReadOnlyList<string> ReadCapabilities(JsonElement manifest)
    {
        if (!manifest.TryGetProperty("capabilities", out var capabilitiesElement) ||
            capabilitiesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return capabilitiesElement
            .EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            .Select(value => value.GetString()!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
