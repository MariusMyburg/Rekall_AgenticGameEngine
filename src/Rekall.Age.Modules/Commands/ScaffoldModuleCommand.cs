using System.Text;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Modules.Commands;

public sealed record ScaffoldModuleRequest(
    string ProjectRoot,
    string ModuleId,
    string DisplayName,
    string ModuleName,
    string ComponentName);

public sealed record ScaffoldModuleResult(
    string SourcePath,
    string ProjectPath,
    string Namespace,
    string ModuleClass,
    string ComponentClass);

public sealed class ScaffoldModuleCommand
    : IRekallAgeCommand<ScaffoldModuleRequest, ScaffoldModuleResult>
{
    public string Name => "rekall.module.scaffold";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Scaffolds a human-editable C# Rekall AGE module skeleton.",
        typeof(ScaffoldModuleRequest).FullName!,
        typeof(ScaffoldModuleResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ScaffoldModuleResult>> ExecuteAsync(
        ScaffoldModuleRequest request,
        RekallAgeCommandContext context)
    {
        var moduleName = ToIdentifier(request.ModuleName, "GameModule");
        var componentName = ToIdentifier(request.ComponentName, "GameComponent");
        var moduleClass = moduleName.EndsWith("Module", StringComparison.Ordinal)
            ? moduleName
            : $"{moduleName}Module";
        var namespaceName = $"Game.Modules.{moduleName}";
        var directory = Path.Combine(request.ProjectRoot, "Modules", moduleName);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{moduleClass}.cs");
        var projectPath = Path.Combine(directory, $"{moduleName}.csproj");
        var source = CreateSource(request.ModuleId, request.DisplayName, namespaceName, moduleClass, componentName);
        await File.WriteAllTextAsync(path, source, context.CancellationToken);
        await File.WriteAllTextAsync(projectPath, CreateProjectFile(), context.CancellationToken);
        context.Transaction.RecordChangedResource(path);
        context.Transaction.RecordChangedResource(projectPath);

        return RekallAgeCommandResult<ScaffoldModuleResult>.Success(
            new ScaffoldModuleResult(path, projectPath, namespaceName, moduleClass, componentName),
            $"Scaffolded module '{request.ModuleId}'.");
    }

    private static string CreateSource(
        string moduleId,
        string displayName,
        string namespaceName,
        string moduleClass,
        string componentClass)
    {
        var source = new StringBuilder();
        source.AppendLine("using Rekall.Age.Modules;");
        source.AppendLine();
        source.AppendLine($"namespace {namespaceName};");
        source.AppendLine();
        source.AppendLine($"[RekallAgeModule(\"{Escape(moduleId)}\", \"{Escape(displayName)}\")]");
        source.AppendLine("[RekallAgeRequiresCapability(\"world\")]");
        source.AppendLine($"public sealed class {moduleClass} : RekallAgeModule");
        source.AppendLine("{");
        source.AppendLine("    public override void Configure(RekallAgeModuleBuilder builder)");
        source.AppendLine("    {");
        source.AppendLine($"        builder.RegisterComponent<{componentClass}>();");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine($"[RekallAgeComponent(\"{ToDisplayName(componentClass)}\")]");
        source.AppendLine($"public sealed class {componentClass} : RekallAgeComponent");
        source.AppendLine("{");
        source.AppendLine("    [RekallAgeProperty]");
        source.AppendLine("    public bool Enabled { get; init; } = true;");
        source.AppendLine("}");
        return source.ToString();
    }

    private static string CreateProjectFile()
    {
        var modulesProjectPath = FindModulesProjectPath();

        return string.Join(Environment.NewLine,
        [
            "<Project Sdk=\"Microsoft.NET.Sdk\">",
            "  <PropertyGroup>",
            "    <TargetFramework>net10.0</TargetFramework>",
            "    <Nullable>enable</Nullable>",
            "    <ImplicitUsings>enable</ImplicitUsings>",
            "  </PropertyGroup>",
            "  <ItemGroup>",
            $"    <ProjectReference Include=\"{modulesProjectPath}\" />",
            "  </ItemGroup>",
            "</Project>",
            string.Empty
        ]);
    }

    private static string FindModulesProjectPath()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "src",
                    "Rekall.Age.Modules",
                    "Rekall.Age.Modules.csproj");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate src/Rekall.Age.Modules/Rekall.Age.Modules.csproj.");
    }

    private static string ToIdentifier(string value, string fallback)
    {
        var parts = value.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var identifier = string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return fallback;
        }

        return char.IsLetter(identifier[0]) || identifier[0] == '_'
            ? identifier
            : $"{fallback}{identifier}";
    }

    private static string ToDisplayName(string identifier)
    {
        var chars = new List<char>();
        for (var i = 0; i < identifier.Length; i++)
        {
            if (i > 0 && char.IsUpper(identifier[i]))
            {
                chars.Add(' ');
            }

            chars.Add(identifier[i]);
        }

        return new string(chars.ToArray());
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
