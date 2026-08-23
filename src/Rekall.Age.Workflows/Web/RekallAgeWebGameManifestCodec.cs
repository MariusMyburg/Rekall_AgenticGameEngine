using System.Text.Json;
using System.Text.Json.Serialization;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Product;
using Rekall.Age.Project;

namespace Rekall.Age.Workflows.Web;

public static class RekallAgeWebGameManifestCodec
{
    public const string Kind = "rekall.age.web.game";
    public const int SchemaVersion = 1;
    public const int MaximumManifestBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
        MaxDepth = 32
    };

    private static readonly JsonSerializerOptions FileOptions = new(CanonicalOptions)
    {
        WriteIndented = true
    };

    public static RekallAgeWebGameManifest Create(
        RekallAgeWebProjectIdentity project,
        string entryScenePath,
        RekallAgeWebViewportPolicy viewport,
        IReadOnlyList<RekallAgeWebModuleIdentity> modules,
        IReadOnlyList<string> requiredRenderingCapabilities,
        IReadOnlyList<RekallAgeWebContentEntry> content)
    {
        var product = RekallAgeProductInfo.Current;
        var manifest = new RekallAgeWebGameManifest(
            Kind,
            SchemaVersion,
            new RekallAgeWebEngineIdentity(
                product.Version,
                product.ProjectSchemaVersion,
                product.ModuleSdkCompatibilityVersion),
            project,
            entryScenePath,
            viewport,
            modules,
            requiredRenderingCapabilities,
            content,
            string.Empty);
        var normalized = NormalizeStructure(manifest);
        return normalized with { BuildIdentity = ComputeBuildIdentity(normalized) };
    }

    public static byte[] EncodeCanonical(RekallAgeWebGameManifest manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(ValidateCanonical(manifest), CanonicalOptions);

    public static byte[] EncodeForFile(RekallAgeWebGameManifest manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(ValidateCanonical(manifest), FileOptions);

    public static RekallAgeWebGameManifest DecodeAndValidate(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException($"Web game manifest exceeds the {MaximumManifestBytes}-byte limit.");
        }

        RekallAgeWebGameManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RekallAgeWebGameManifest>(bytes.Span, CanonicalOptions)
                ?? throw new InvalidDataException("Web game manifest did not contain a document.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Web game manifest is not valid bounded JSON.", error);
        }

        return ValidateCanonical(manifest);
    }

    public static string ComputeBuildIdentity(RekallAgeWebGameManifest manifest)
    {
        var normalized = NormalizeStructure(manifest) with { BuildIdentity = string.Empty };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized, CanonicalOptions);
        return RekallAgeDocumentRevision.Compute(bytes);
    }

    private static RekallAgeWebGameManifest ValidateCanonical(RekallAgeWebGameManifest manifest)
    {
        var normalized = NormalizeStructure(manifest);
        if (!manifest.Modules.SequenceEqual(normalized.Modules)
            || !manifest.RequiredRenderingCapabilities.SequenceEqual(normalized.RequiredRenderingCapabilities)
            || !manifest.Content.SequenceEqual(normalized.Content))
        {
            throw new InvalidDataException("Web game manifest arrays must use canonical ordinal ordering without duplicates.");
        }

        var expectedIdentity = ComputeBuildIdentity(normalized);
        if (!string.Equals(manifest.BuildIdentity, expectedIdentity, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Web game manifest buildIdentity does not match its canonical logical content.");
        }

        return normalized;
    }

    private static RekallAgeWebGameManifest NormalizeStructure(RekallAgeWebGameManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!string.Equals(manifest.Kind, Kind, StringComparison.Ordinal)
            || manifest.SchemaVersion != SchemaVersion)
        {
            throw new InvalidDataException($"Web game manifest must declare kind '{Kind}' and schema {SchemaVersion}.");
        }

        var product = RekallAgeProductInfo.Current;
        if (manifest.Engine is null
            || !string.Equals(manifest.Engine.ProductVersion, product.Version, StringComparison.Ordinal)
            || manifest.Engine.ProjectSchemaVersion != product.ProjectSchemaVersion
            || manifest.Engine.ModuleSdkCompatibilityVersion != product.ModuleSdkCompatibilityVersion)
        {
            throw new InvalidDataException("Web game manifest engine, project-schema, or module-SDK identity is incompatible.");
        }

        if (manifest.Project is null)
        {
            throw new InvalidDataException("Web game manifest project identity is required.");
        }
        RequireExactText(manifest.Project.Name, "project name");
        RequireSha256(manifest.Project.ProjectManifestSha256, "project manifest");
        ValidateViewport(manifest.Viewport);

        if (manifest.Modules is null || manifest.RequiredRenderingCapabilities is null || manifest.Content is null)
        {
            throw new InvalidDataException("Web game manifest module, capability, and content arrays are required.");
        }

        var modules = manifest.Modules
            .Select(ValidateModule)
            .OrderBy(module => module.Id, StringComparer.Ordinal)
            .ThenBy(module => module.ModuleName, StringComparer.Ordinal)
            .ToArray();
        RequireUnique(modules.Select(module => module.Id), "module ID");
        RequireUnique(modules.Select(module => module.ModuleName), "module name");

        var capabilities = manifest.RequiredRenderingCapabilities
            .Select(capability => RequireExactText(capability, "rendering capability"))
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();
        RequireUnique(capabilities, "rendering capability");

        var content = manifest.Content
            .Select(ValidateContent)
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();
        RequireUnique(content.Select(entry => entry.Path), "content path");
        RequireUnique(content.Select(entry => entry.Path.ToUpperInvariant()), "portable content path");

        var entryScenePath = RequireCanonicalPath(manifest.EntryScenePath, "entry scene");
        if (!content.Any(entry => entry.Path.Equals(entryScenePath, StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"Entry scene '{entryScenePath}' is not present in web game content.");
        }
        if (!content.Any(entry => entry.Path.Equals("rekall.project.json", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Web game content must include 'rekall.project.json'.");
        }

        return manifest with
        {
            EntryScenePath = entryScenePath,
            Modules = modules,
            RequiredRenderingCapabilities = capabilities,
            Content = content
        };
    }

    private static RekallAgeWebModuleIdentity ValidateModule(RekallAgeWebModuleIdentity module)
    {
        if (module is null)
        {
            throw new InvalidDataException("Web game module entries cannot be null.");
        }
        RequireExactText(module.Id, "module ID");
        RequireExactText(module.ModuleName, "module name");
        RequireExactText(module.AssemblyIdentity, "module assembly identity");
        RequireSha256(module.SourceFingerprint, $"module '{module.Id}' source fingerprint");
        return module;
    }

    private static RekallAgeWebContentEntry ValidateContent(RekallAgeWebContentEntry entry)
    {
        if (entry is null)
        {
            throw new InvalidDataException("Web game content entries cannot be null.");
        }
        var path = RequireCanonicalPath(entry.Path, "content");
        RequireExactText(entry.MediaType, $"content media type for '{path}'");
        if (entry.SizeBytes < 0)
        {
            throw new InvalidDataException($"Web game content '{path}' has a negative size.");
        }
        RequireSha256(entry.Sha256, $"content '{path}'");
        return entry with { Path = path };
    }

    private static void ValidateViewport(RekallAgeWebViewportPolicy viewport)
    {
        if (viewport is null
            || viewport.ReferenceWidth is < 1 or > 16384
            || viewport.ReferenceHeight is < 1 or > 16384
            || !viewport.ResizeMode.Equals("fit", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Web game viewport must use bounded positive dimensions and resizeMode 'fit'.");
        }
    }

    private static string RequireCanonicalPath(string path, string description)
    {
        string normalized;
        try
        {
            normalized = RekallAgeGameContentPath.Normalize(path);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException($"Web game {description} path '{path}' is unsafe.", error);
        }

        if (!string.Equals(path, normalized, StringComparison.Ordinal) || path.Contains(':'))
        {
            throw new InvalidDataException($"Web game {description} path '{path}' is not canonical.");
        }
        return normalized;
    }

    private static string RequireExactText(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Web game {description} must be nonempty and canonically trimmed.");
        }
        return value;
    }

    private static void RequireSha256(string value, string description)
    {
        if (value is null || value.Equals(RekallAgeDocumentRevision.Missing, StringComparison.Ordinal)
            || !RekallAgeDocumentRevision.IsValid(value))
        {
            throw new InvalidDataException($"Web game {description} must declare a lowercase SHA-256 hash.");
        }
    }

    private static void RequireUnique(IEnumerable<string> values, string description)
    {
        var duplicate = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Web game manifest contains duplicate {description} '{duplicate.Key}'.");
        }
    }
}
