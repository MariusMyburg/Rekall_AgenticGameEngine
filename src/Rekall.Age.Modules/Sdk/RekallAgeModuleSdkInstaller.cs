using System.Text.Json;
using Rekall.Age.Core.Product;

namespace Rekall.Age.Modules.Sdk;

public sealed record RekallAgeModuleSdkInstallation(
    int CompatibilityVersion,
    string SdkRoot,
    string PropsPath,
    string ManifestPath,
    IReadOnlyList<string> Assemblies,
    IReadOnlyList<string> Resources);

public sealed record RekallAgeModuleSdkManifest(
    string ProductVersion,
    int CompatibilityVersion,
    IReadOnlyList<string> Assemblies)
{
    public int SchemaVersion { get; init; } = 1;

    public IReadOnlyList<RekallAgeModuleSdkFileIntegrity> Files { get; init; } =
        Array.Empty<RekallAgeModuleSdkFileIntegrity>();
}

public sealed record RekallAgeModuleSdkFileIntegrity(
    string Path,
    long SizeBytes,
    string Sha256);

public sealed class RekallAgeModuleSdkInstaller
{
    private static readonly string[] AssemblyNames =
    [
        "Rekall.Age.Core.dll",
        "Rekall.Age.World.dll",
        "Rekall.Age.Runtime.Abstractions.dll",
        "Rekall.Age.Modules.dll",
        "Rekall.Age.Modeling.Contracts.dll",
        "Rekall.Age.Modeling.dll"
    ];

    public async ValueTask<RekallAgeModuleSdkInstallation> InstallAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var compatibilityVersion = RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion;
        var projectFullPath = Path.GetFullPath(projectRoot);
        var sdkRoot = Path.Combine(
            projectFullPath,
            ".rekall",
            "sdk",
            compatibilityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        EnsureDirectoryChainIsConfined(projectFullPath, sdkRoot, createMissing: true);

        var installedAssemblies = new List<string>();
        foreach (var assemblyName in AssemblyNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDirectoryChainIsConfined(projectFullPath, sdkRoot, createMissing: false);
            var source = ResolveAssembly(assemblyName);
            var destination = Path.Combine(sdkRoot, assemblyName);
            File.Copy(source, destination, overwrite: true);
            installedAssemblies.Add(destination);
        }

        var propsPath = Path.Combine(sdkRoot, "Rekall.Age.Sdk.props");
        EnsureDirectoryChainIsConfined(projectFullPath, sdkRoot, createMissing: false);
        await File.WriteAllTextAsync(propsPath, CreatePropsFile(), cancellationToken);

        var manifestPath = Path.Combine(sdkRoot, "rekall.sdk.json");
        var integrity = installedAssemblies
            .Append(propsPath)
            .Select(path => new RekallAgeModuleSdkFileIntegrity(
                Path.GetFileName(path),
                new FileInfo(path).Length,
                ComputeSha256(path)))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
        var manifest = new RekallAgeModuleSdkManifest(
            RekallAgeProductInfo.Current.Version,
            compatibilityVersion,
            AssemblyNames)
        {
            Files = integrity
        };
        var manifestJson = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        var temporaryManifest = manifestPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            EnsureDirectoryChainIsConfined(projectFullPath, sdkRoot, createMissing: false);
            await File.WriteAllTextAsync(temporaryManifest, manifestJson, cancellationToken);
            File.Move(temporaryManifest, manifestPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryManifest))
            {
                File.Delete(temporaryManifest);
            }
        }

        var resources = installedAssemblies
            .Append(propsPath)
            .Append(manifestPath)
            .ToArray();
        return new RekallAgeModuleSdkInstallation(
            compatibilityVersion,
            sdkRoot,
            propsPath,
            manifestPath,
            installedAssemblies,
            resources);
    }

    internal static string ResolveAssembly(string assemblyName)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
                !assembly.IsDynamic &&
                string.Equals(Path.GetFileName(assembly.Location), assemblyName, StringComparison.OrdinalIgnoreCase));
        if (loaded is not null && File.Exists(loaded.Location))
        {
            return loaded.Location;
        }

        var moduleDirectory = Path.GetDirectoryName(typeof(RekallAgeModule).Assembly.Location)!;
        var candidate = Path.Combine(moduleDirectory, assemblyName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException(
            $"Required Rekall AGE module SDK assembly '{assemblyName}' was not found.",
            candidate);
    }

    internal static string CreatePropsFile()
    {
        return """
            <Project>
              <ItemGroup>
                <Reference Include="Rekall.Age.Core" HintPath="$(MSBuildThisFileDirectory)Rekall.Age.Core.dll" Private="false" />
                <Reference Include="Rekall.Age.World" HintPath="$(MSBuildThisFileDirectory)Rekall.Age.World.dll" Private="false" />
                <Reference Include="Rekall.Age.Runtime.Abstractions" HintPath="$(MSBuildThisFileDirectory)Rekall.Age.Runtime.Abstractions.dll" Private="false" />
                <Reference Include="Rekall.Age.Modules" HintPath="$(MSBuildThisFileDirectory)Rekall.Age.Modules.dll" Private="false" />
                <Reference Include="Rekall.Age.Modeling.Contracts" HintPath="$(MSBuildThisFileDirectory)Rekall.Age.Modeling.Contracts.dll" Private="false" />
                <Reference Include="Rekall.Age.Modeling" HintPath="$(MSBuildThisFileDirectory)Rekall.Age.Modeling.dll" Private="false" />
              </ItemGroup>
            </Project>

            """;
    }

    internal static IReadOnlyList<string> RequiredAssemblyNames => AssemblyNames;

    private static void EnsureDirectoryChainIsConfined(
        string projectRoot,
        string sdkRoot,
        bool createMissing)
    {
        var chain = new[]
        {
            projectRoot,
            Path.Combine(projectRoot, ".rekall"),
            Path.Combine(projectRoot, ".rekall", "sdk"),
            sdkRoot
        };
        foreach (var path in chain)
        {
            if (!Directory.Exists(path))
            {
                if (!createMissing) throw new IOException($"Project-local module SDK directory '{path}' disappeared during installation.");
                Directory.CreateDirectory(path);
            }
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Project-local module SDK path cannot contain a reparse point: '{path}'.");
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }
}
