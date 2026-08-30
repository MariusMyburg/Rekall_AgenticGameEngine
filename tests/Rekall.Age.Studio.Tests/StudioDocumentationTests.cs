using System.IO;
using System.Text.RegularExpressions;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioDocumentationTests
{
    [Fact]
    public void DocumentationLauncherResolvesBundledFileAndUsesAssociatedApplication()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rekall-docs-{Guid.NewGuid():N}");
        var documentationDirectory = Path.Combine(root, "Documentation");
        Directory.CreateDirectory(documentationDirectory);
        var expected = Path.Combine(documentationDirectory, "Rekall-AGE-Documentation.html");
        File.WriteAllText(expected, "<!doctype html><title>Documentation</title>");
        string? opened = null;

        try
        {
            RekallAgeStudioDocumentation.Open(root, path => opened = path);

            Assert.Equal(expected, opened);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DocumentationLauncherReportsAUsefulErrorWhenBundleIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rekall-docs-{Guid.NewGuid():N}");

        var exception = Assert.Throws<FileNotFoundException>(
            () => RekallAgeStudioDocumentation.Open(root, _ => { }));

        Assert.Contains("Studio documentation is missing", exception.Message, StringComparison.Ordinal);
        Assert.EndsWith(
            Path.Combine("Documentation", "Rekall-AGE-Documentation.html"),
            exception.FileName,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpMenuExposesDocumentationWithF1AndPublishesTheHtmlFile()
    {
        var root = FindRepositoryRoot();
        var studioRoot = Path.Combine(root, "src", "Rekall.Age.Studio");
        var xaml = await File.ReadAllTextAsync(Path.Combine(studioRoot, "MainWindow.xaml"));
        var project = await File.ReadAllTextAsync(Path.Combine(studioRoot, "Rekall.Age.Studio.csproj"));

        Assert.Contains("Header=\"_Help\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Documentation\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"F1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenDocumentationCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Documentation\\Rekall-AGE-Documentation.html", project, StringComparison.Ordinal);
        Assert.Contains("CopyToOutputDirectory", project, StringComparison.Ordinal);
        Assert.Contains("CopyToPublishDirectory", project, StringComparison.Ordinal);

        var copiedDocumentation = Path.Combine(
            AppContext.BaseDirectory,
            "Documentation",
            "Rekall-AGE-Documentation.html");
        Assert.True(File.Exists(copiedDocumentation), $"Bundled documentation was not copied to '{copiedDocumentation}'.");
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(studioRoot, "Documentation", "Rekall-AGE-Documentation.html")),
            await File.ReadAllBytesAsync(copiedDocumentation));
    }

    [Fact]
    public async Task DocumentationIsSelfContainedSearchableAndCoversBeginnerThroughAdvancedAuthoring()
    {
        var root = FindRepositoryRoot();
        var documentation = Path.Combine(
            root,
            "src",
            "Rekall.Age.Studio",
            "Documentation",
            "Rekall-AGE-Documentation.html");
        var html = await File.ReadAllTextAsync(documentation);

        Assert.Contains("<title>Rekall AGE Documentation", html, StringComparison.Ordinal);
        Assert.Contains("id=\"doc-search\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Documentation navigation\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", html, StringComparison.Ordinal);
        Assert.Contains("IRekallAgeRuntimeModuleSystem", html, StringComparison.Ordinal);
        Assert.Contains("module scaffold-runtime-system", html, StringComparison.Ordinal);

        string[] requiredSections =
        [
            "getting-started", "studio-tour", "agent-authoring", "projects-scenes-entities",
            "inspector-components", "csharp-modules", "rendering", "input", "physics",
            "audio-ui-animation", "multiplayer", "virtual-reality", "testing-verification",
            "building-shipping", "cli-agent-tools", "troubleshooting", "advanced-architecture",
            "reference"
        ];
        foreach (var section in requiredSections)
        {
            Assert.Contains($"id=\"{section}\"", html, StringComparison.Ordinal);
        }

        var catalogSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "Rekall.Age.World",
            "RekallAgeBuiltInComponentTypeCatalog.cs"));
        var componentTypes = Regex.Matches(catalogSource, "\"(Rekall\\.[A-Za-z0-9]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(93, componentTypes.Length);
        foreach (var componentType in componentTypes)
        {
            Assert.Contains(componentType, html, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(new Regex("(?:src|href)=\\\"https?://", RegexOptions.IgnoreCase), html);
        Assert.DoesNotContain("TODO", html, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Rekall AGE repository root.");
    }
}
