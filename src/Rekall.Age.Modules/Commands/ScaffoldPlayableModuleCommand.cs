using System.Text;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Modules.Commands;

public sealed record ScaffoldPlayableModuleRequest(
    string ProjectRoot,
    string ModuleId,
    string DisplayName,
    string ModuleName,
    string Kind);

public sealed record ScaffoldPlayableModuleResult(
    string SourcePath,
    string ProjectPath,
    string Namespace,
    string ModuleClass);

public sealed class ScaffoldPlayableModuleCommand
    : IRekallAgeCommand<ScaffoldPlayableModuleRequest, ScaffoldPlayableModuleResult>
{
    public string Name => "rekall.module.scaffold_playable";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Scaffolds an agent-editable C# gameplay module that owns update and render behavior.",
        typeof(ScaffoldPlayableModuleRequest).FullName!,
        typeof(ScaffoldPlayableModuleResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ScaffoldPlayableModuleResult>> ExecuteAsync(
        ScaffoldPlayableModuleRequest request,
        RekallAgeCommandContext context)
    {
        var moduleName = ToIdentifier(request.ModuleName, "PlayableModule");
        var moduleClass = moduleName.EndsWith("Module", StringComparison.Ordinal)
            ? moduleName
            : $"{moduleName}Module";
        var namespaceName = $"Game.Modules.{moduleName}";
        var directory = Path.Combine(request.ProjectRoot, "Modules", moduleName);
        Directory.CreateDirectory(directory);

        var sourcePath = Path.Combine(directory, $"{moduleClass}.cs");
        var projectPath = Path.Combine(directory, $"{moduleName}.csproj");
        await File.WriteAllTextAsync(
            sourcePath,
            CreateSource(request.ModuleId, request.DisplayName, request.Kind, namespaceName, moduleClass),
            context.CancellationToken);
        await File.WriteAllTextAsync(projectPath, CreateProjectFile(), context.CancellationToken);

        context.Transaction.RecordChangedResource(sourcePath);
        context.Transaction.RecordChangedResource(projectPath);

        return RekallAgeCommandResult<ScaffoldPlayableModuleResult>.Success(
            new ScaffoldPlayableModuleResult(sourcePath, projectPath, namespaceName, moduleClass),
            $"Scaffolded playable module '{request.ModuleId}'.");
    }

    private static string CreateSource(
        string moduleId,
        string displayName,
        string kind,
        string namespaceName,
        string moduleClass)
    {
        var source = new StringBuilder();
        source.AppendLine("using Rekall.Age.Modules;");
        source.AppendLine();
        source.AppendLine($"namespace {namespaceName};");
        source.AppendLine();
        source.AppendLine($"[RekallAgeModule(\"{Escape(moduleId)}\", \"{Escape(displayName)}\")]");
        source.AppendLine("[RekallAgeRequiresCapability(\"world\")]");
        source.AppendLine($"public sealed class {moduleClass} : RekallAgeModule, IRekallAgePlayableModule");
        source.AppendLine("{");
        source.AppendLine($"    public string Kind => \"{Escape(kind)}\";");
        source.AppendLine();
        source.AppendLine("    public override void Configure(RekallAgeModuleBuilder builder)");
        source.AppendLine("    {");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    public RekallAgePlayableModuleState CreateInitialState(RekallAgePlayableModuleContext context)");
        source.AppendLine("    {");
        source.AppendLine("        var state = new RekallAgePlayableModuleState();");
        source.AppendLine("        state.Numbers[\"frame\"] = 0;");
        source.AppendLine("        state.Text[\"scene\"] = context.SceneName;");
        source.AppendLine("        return state;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    public void Tick(RekallAgePlayableModuleState state, RekallAgePlayableModuleInput input)");
        source.AppendLine("    {");
        source.AppendLine("        state.Numbers[\"frame\"] += 1;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state)");
        source.AppendLine("    {");
        source.AppendLine("        var frame = (int)state.Numbers[\"frame\"];");
        source.AppendLine("        return new RekallAgePlayableModuleFrame($\"Module-authored {Kind} in {state.Text[\"scene\"]} frame {frame}\");");
        source.AppendLine("    }");
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

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
