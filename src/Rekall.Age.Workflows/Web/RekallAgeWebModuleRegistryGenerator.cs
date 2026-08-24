using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rekall.Age.Modules;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.Workflows.Web;

public sealed record RekallAgeWebDiscoveredModule(
    RekallAgeWebModuleIdentity Identity,
    string ProjectPath,
    string ModuleTypeName,
    IReadOnlyList<string> RuntimeSystemTypeNames);

public sealed record RekallAgeWebModuleSourceSnapshot(
    string Path,
    string Sha256);

public sealed record RekallAgeWebModuleRegistryPlan(
    string ProjectRoot,
    IReadOnlyList<RekallAgeWebDiscoveredModule> Modules,
    IReadOnlyList<string> ModuleProjectPaths,
    IReadOnlyList<RekallAgeWebModuleSourceSnapshot> ModuleSources,
    string RegistrySource,
    string MsBuildInputs);

public sealed record RekallAgeWebModuleRegistryGeneration(
    IReadOnlyList<RekallAgeWebDiscoveredModule> Modules,
    IReadOnlyList<string> ModuleProjectPaths,
    IReadOnlyList<RekallAgeWebModuleSourceSnapshot> ModuleSources,
    string RegistrySourcePath,
    string MsBuildInputsPath);

public sealed class RekallAgeWebModuleRegistryGenerator
{
    public const string RegistrySourceFileName = "RekallAgePublishedModules.g.cs";
    public const string MsBuildInputsFileName = "RekallAgeWebPublishInputs.props";

    public RekallAgeWebModuleRegistryPlan Discover(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var root = Path.GetFullPath(projectRoot);
        var policy = new RekallAgeModuleBuildPolicy().Inspect(root);
        if (!policy.Ready)
        {
            throw new InvalidDataException(string.Join(
                Environment.NewLine,
                policy.Issues.Select(issue => issue.Message)));
        }

        var trust = new RekallAgeProjectModuleTrustInspector().Inspect(root);
        if (!trust.Ready)
        {
            throw new InvalidDataException(string.Join(
                Environment.NewLine,
                trust.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        }

        var candidates = policy.Candidates.ToDictionary(
            candidate => candidate.ModuleName,
            StringComparer.Ordinal);
        var inspections = trust.Modules.ToDictionary(
            module => module.ModuleName,
            StringComparer.Ordinal);
        var discovered = new List<RekallAgeWebDiscoveredModule>();
        foreach (var assembly in RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(root)
            .OrderBy(item => item.GetName().Name, StringComparer.Ordinal))
        {
            var assemblyName = assembly.GetName().Name
                ?? throw new InvalidDataException("A verified module assembly has no simple name.");
            if (!candidates.TryGetValue(assemblyName, out var candidate)
                || !inspections.TryGetValue(assemblyName, out var inspection))
            {
                throw new InvalidDataException(
                    $"Verified module assembly '{assemblyName}' has no matching authored module project.");
            }

            var artifact = inspection.OutputFiles.SingleOrDefault(file =>
                string.Equals(file.Path, $"{assemblyName}.dll", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(artifact?.AssemblyIdentity))
            {
                throw new InvalidDataException(
                    $"Verified module assembly '{assemblyName}' has no recorded assembly identity.");
            }

            var moduleTypes = assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(RekallAgeModule).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            if (moduleTypes.Length == 0)
            {
                throw new InvalidDataException(
                    $"Authored module project '{assemblyName}' contains no concrete Rekall AGE module.");
            }
            if (moduleTypes.Length != 1)
            {
                throw new InvalidDataException(
                    $"Authored module project '{assemblyName}' must contain exactly one concrete Rekall AGE module for canonical web publication.");
            }

            foreach (var moduleType in moduleTypes)
            {
                var attribute = moduleType.GetCustomAttribute<RekallAgeModuleAttribute>()
                    ?? throw new InvalidDataException(
                        $"Module type '{moduleType.FullName}' has no Rekall AGE module identity.");
                if (string.IsNullOrWhiteSpace(attribute.Id)
                    || !string.Equals(attribute.Id, attribute.Id.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Module type '{moduleType.FullName}' must declare a nonempty canonically trimmed identity.");
                }
                RequireExportable(moduleType, "module");
                var module = (RekallAgeModule)Activator.CreateInstance(moduleType)!;
                var builder = new RekallAgeModuleBuilder();
                module.Configure(builder);
                var systemTypes = builder.RuntimeSystemTypes
                    .Distinct()
                    .OrderBy(type => type.FullName, StringComparer.Ordinal)
                    .ToArray();
                foreach (var systemType in systemTypes)
                {
                    RequireExportable(systemType, "runtime system");
                }

                discovered.Add(new RekallAgeWebDiscoveredModule(
                    new RekallAgeWebModuleIdentity(
                        attribute.Id,
                        assemblyName,
                        artifact.AssemblyIdentity,
                        inspection.SourceFingerprint),
                    candidate.ProjectPath,
                    CSharpTypeName(moduleType),
                    systemTypes.Select(CSharpTypeName).ToArray()));
            }
        }

        var ordered = discovered.OrderBy(module => module.Identity.Id, StringComparer.Ordinal).ToArray();
        var duplicate = ordered.GroupBy(module => module.Identity.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Web module identity '{duplicate.Key}' is declared more than once.");
        }

        var projects = policy.Candidates
            .OrderBy(candidate => candidate.ModuleName, StringComparer.Ordinal)
            .Select(candidate => candidate.ProjectPath)
            .ToArray();
        var sources = policy.Candidates
            .SelectMany(candidate => candidate.SourcePaths.Prepend(candidate.ProjectPath))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new RekallAgeWebModuleSourceSnapshot(
                Path.GetFullPath(path),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .ToArray();
        var stableTrust = new RekallAgeProjectModuleTrustInspector().Inspect(root);
        if (!stableTrust.Ready
            || !stableTrust.Modules
                .OrderBy(module => module.ModuleName, StringComparer.Ordinal)
                .Select(module => (module.ModuleName, module.SourceFingerprint))
                .SequenceEqual(trust.Modules
                    .OrderBy(module => module.ModuleName, StringComparer.Ordinal)
                    .Select(module => (module.ModuleName, module.SourceFingerprint))))
        {
            throw new InvalidDataException(
                "Authored module source changed while web publication discovery was running; rebuild and retry.");
        }
        return new RekallAgeWebModuleRegistryPlan(
            root,
            ordered,
            projects,
            sources,
            CreateRegistrySource(ordered),
            CreateMsBuildInputs(projects, sources));
    }

    public RekallAgeWebModuleRegistryGeneration Generate(string projectRoot, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var root = Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output) || File.Exists(output))
        {
            throw new InvalidOperationException("Web module build-input output must not already exist.");
        }
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (output.Equals(root, comparison)
            || output.StartsWith(root + Path.DirectorySeparatorChar, comparison)
            || root.StartsWith(output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException(
                "Web module build-input output must not overlap the authored project root.");
        }

        var plan = Discover(root);
        var outputParent = Path.GetDirectoryName(output)
            ?? throw new InvalidOperationException("Web module build-input output has no parent directory.");
        Directory.CreateDirectory(outputParent);
        var temporaryOutput = output + $".tmp-{Guid.NewGuid():N}";
        Directory.CreateDirectory(temporaryOutput);
        try
        {
            WriteNew(Path.Combine(temporaryOutput, RegistrySourceFileName), plan.RegistrySource);
            WriteNew(Path.Combine(temporaryOutput, MsBuildInputsFileName), plan.MsBuildInputs);
            Directory.Move(temporaryOutput, output);
        }
        finally
        {
            if (Directory.Exists(temporaryOutput))
            {
                Directory.Delete(temporaryOutput, recursive: true);
            }
        }
        var sourcePath = Path.Combine(output, RegistrySourceFileName);
        var inputsPath = Path.Combine(output, MsBuildInputsFileName);
        return new RekallAgeWebModuleRegistryGeneration(
            plan.Modules,
            plan.ModuleProjectPaths,
            plan.ModuleSources,
            sourcePath,
            inputsPath);
    }

    private static void WriteNew(string path, string contents)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(contents);
    }

    private static void RequireExportable(Type type, string kind)
    {
        if (!type.IsVisible
            || type.ContainsGenericParameters
            || type.IsArray
            || type.IsPointer
            || type.IsByRef
            || type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidDataException(
                $"Web {kind} type '{type.FullName}' must be public with a public parameterless constructor for static browser publication.");
        }
    }

    private static string CSharpTypeName(Type type)
    {
        var name = type.FullName
            ?? throw new InvalidDataException("A web module type has no stable full name.");
        return string.Join('.', name.Split(['.', '+']).Select(EscapeIdentifier));
    }

    private static string EscapeIdentifier(string identifier) => CSharpKeywords.Contains(identifier)
        ? "@" + identifier
        : identifier;

    private static string EscapeCSharpString(string value)
    {
        var jsonString = JsonSerializer.Serialize(value);
        return jsonString[1..^1];
    }

    private static string CreateRegistrySource(IReadOnlyList<RekallAgeWebDiscoveredModule> modules)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("using Rekall.Age.Runtime;");
        source.AppendLine();
        source.AppendLine("namespace Rekall.Age.Player.Web;");
        source.AppendLine();
        source.AppendLine("internal static partial class RekallAgePublishedModules");
        source.AppendLine("{");
        source.AppendLine("    static partial void Add(List<RekallAgeRuntimeModuleRegistration> registrations)");
        source.AppendLine("    {");
        foreach (var module in modules)
        {
            source.AppendLine("        registrations.Add(new RekallAgeRuntimeModuleRegistration(");
            source.AppendLine($"            typeof(global::{module.ModuleTypeName}),");
            source.AppendLine($"            static () => new global::{module.ModuleTypeName}(),");
            source.AppendLine("            [");
            foreach (var systemType in module.RuntimeSystemTypeNames)
            {
                source.AppendLine("                new RekallAgeRuntimeSystemRegistration(");
                source.AppendLine($"                    typeof(global::{systemType}),");
                source.AppendLine($"                    static () => new global::{systemType}()),");
            }
            source.AppendLine("            ])");
            source.AppendLine("        {");
            source.AppendLine($"            ModuleId = \"{EscapeCSharpString(module.Identity.Id)}\",");
            source.AppendLine($"            ModuleName = \"{EscapeCSharpString(module.Identity.ModuleName)}\",");
            source.AppendLine($"            AssemblyIdentity = \"{EscapeCSharpString(module.Identity.AssemblyIdentity)}\",");
            source.AppendLine($"            SourceFingerprint = \"{EscapeCSharpString(module.Identity.SourceFingerprint)}\"");
            source.AppendLine("        });");
        }
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string CreateMsBuildInputs(
        IReadOnlyList<string> projectPaths,
        IReadOnlyList<RekallAgeWebModuleSourceSnapshot> sources)
    {
        var source = new StringBuilder();
        source.AppendLine("<Project>");
        source.AppendLine("  <ItemGroup>");
        foreach (var projectPath in projectPaths)
        {
            source.Append("    <ProjectReference Include=\"")
                .Append(SecurityElement.Escape(Path.GetFullPath(projectPath)))
                .AppendLine("\" />");
        }
        source.Append("    <Compile Include=\"$(MSBuildThisFileDirectory)")
            .Append(RegistrySourceFileName)
            .AppendLine("\" Link=\"Generated\\RekallAgePublishedModules.g.cs\" />");
        foreach (var item in sources)
        {
            source.Append("    <RekallAgeWebModuleSource Include=\"")
                .Append(SecurityElement.Escape(item.Path))
                .Append("\" ExpectedSha256=\"")
                .Append(item.Sha256)
                .AppendLine("\" />");
        }
        source.AppendLine("  </ItemGroup>");
        source.AppendLine("  <Target Name=\"VerifyRekallAgeWebModuleSourceSnapshotBeforeBuild\" BeforeTargets=\"PrepareForBuild\">");
        source.AppendLine("    <GetFileHash Files=\"@(RekallAgeWebModuleSource)\" Algorithm=\"SHA256\">");
        source.AppendLine("      <Output TaskParameter=\"Items\" ItemName=\"_RekallAgeWebModuleSourceBeforeBuild\" />");
        source.AppendLine("    </GetFileHash>");
        source.AppendLine("    <Error Condition=\"'%(_RekallAgeWebModuleSourceBeforeBuild.FileHash)' != '%(_RekallAgeWebModuleSourceBeforeBuild.ExpectedSha256)'\" Text=\"Authored Rekall AGE module source changed after web publication discovery: %(_RekallAgeWebModuleSourceBeforeBuild.Identity)\" />");
        source.AppendLine("  </Target>");
        source.AppendLine("  <Target Name=\"VerifyRekallAgeWebModuleSourceSnapshotAfterPublish\" AfterTargets=\"Publish\">");
        source.AppendLine("    <GetFileHash Files=\"@(RekallAgeWebModuleSource)\" Algorithm=\"SHA256\">");
        source.AppendLine("      <Output TaskParameter=\"Items\" ItemName=\"_RekallAgeWebModuleSourceAfterPublish\" />");
        source.AppendLine("    </GetFileHash>");
        source.AppendLine("    <Error Condition=\"'%(_RekallAgeWebModuleSourceAfterPublish.FileHash)' != '%(_RekallAgeWebModuleSourceAfterPublish.ExpectedSha256)'\" Text=\"Authored Rekall AGE module source changed while web publication was running: %(_RekallAgeWebModuleSourceAfterPublish.Identity)\" />");
        source.AppendLine("  </Target>");
        source.AppendLine("</Project>");
        return source.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while"
    };
}
