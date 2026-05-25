using System.Text;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Modules.Commands;

public sealed record ScaffoldRuntimeSystemModuleRequest(
    string ProjectRoot,
    string ModuleId,
    string DisplayName,
    string ModuleName,
    string ComponentName,
    string SystemName);

public sealed record ScaffoldRuntimeSystemModuleResult(
    string SourcePath,
    string ProjectPath,
    string Namespace,
    string ModuleClass,
    string ComponentClass,
    string SystemClass);

public sealed class ScaffoldRuntimeSystemModuleCommand
    : IRekallAgeCommand<ScaffoldRuntimeSystemModuleRequest, ScaffoldRuntimeSystemModuleResult>
{
    public string Name => "rekall.module.scaffold_runtime_system";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Scaffolds a C# module with an editable component and runtime system.",
        typeof(ScaffoldRuntimeSystemModuleRequest).FullName!,
        typeof(ScaffoldRuntimeSystemModuleResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ScaffoldRuntimeSystemModuleResult>> ExecuteAsync(
        ScaffoldRuntimeSystemModuleRequest request,
        RekallAgeCommandContext context)
    {
        var moduleName = ToIdentifier(request.ModuleName, "RuntimeModule");
        var componentName = ToIdentifier(request.ComponentName, "RuntimeComponent");
        var systemName = ToIdentifier(request.SystemName, "RuntimeSystem");
        var moduleClass = moduleName.EndsWith("Module", StringComparison.Ordinal)
            ? moduleName
            : $"{moduleName}Module";
        var systemClass = systemName.EndsWith("System", StringComparison.Ordinal)
            ? systemName
            : $"{systemName}System";
        var namespaceName = $"Game.Modules.{moduleName}";
        var directory = Path.Combine(request.ProjectRoot, "Modules", moduleName);
        Directory.CreateDirectory(directory);

        var sourcePath = Path.Combine(directory, $"{moduleClass}.cs");
        var projectPath = Path.Combine(directory, $"{moduleName}.csproj");
        await File.WriteAllTextAsync(
            sourcePath,
            CreateSource(request.ModuleId, request.DisplayName, namespaceName, moduleClass, componentName, systemClass),
            context.CancellationToken);
        await File.WriteAllTextAsync(projectPath, CreateProjectFile(), context.CancellationToken);
        context.Transaction.RecordChangedResource(sourcePath);
        context.Transaction.RecordChangedResource(projectPath);

        return RekallAgeCommandResult<ScaffoldRuntimeSystemModuleResult>.Success(
            new ScaffoldRuntimeSystemModuleResult(
                sourcePath,
                projectPath,
                namespaceName,
                moduleClass,
                componentName,
                systemClass),
            $"Scaffolded runtime system module '{request.ModuleId}'.");
    }

    private static string CreateSource(
        string moduleId,
        string displayName,
        string namespaceName,
        string moduleClass,
        string componentClass,
        string systemClass)
    {
        var source = new StringBuilder();
        source.AppendLine("using Rekall.Age.Modules;");
        source.AppendLine("using Rekall.Age.Runtime.Abstractions;");
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
        source.AppendLine($"        builder.RegisterRuntimeSystem<{systemClass}>();");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine($"[RekallAgeComponent(\"{ToDisplayName(componentClass)}\")]");
        source.AppendLine($"public sealed class {componentClass} : RekallAgeComponent");
        source.AppendLine("{");
        source.AppendLine("    [RekallAgeProperty]");
        source.AppendLine("    public bool Enabled { get; init; } = true;");
        source.AppendLine();
        source.AppendLine("    [RekallAgeProperty]");
        source.AppendLine("    public double ValuePerSecond { get; init; } = 1;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine($"public sealed class {systemClass} : IRekallAgeRuntimeModuleSystem");
        source.AppendLine("{");
        source.AppendLine($"    public string Id => nameof({systemClass});");
        source.AppendLine();
        source.AppendLine("    public int Priority => 0;");
        source.AppendLine();
        source.AppendLine("    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(");
        source.AppendLine("        RekallAgeRuntimeWorld world,");
        source.AppendLine("        RekallAgeRuntimeModuleFrameContext context)");
        source.AppendLine("    {");
        source.AppendLine($"        var componentType = \"{namespaceName}.{componentClass}\";");
        source.AppendLine("        var seconds = context.DeltaTime.TotalSeconds;");
        source.AppendLine("        var entities = world.Entities.Select(entity =>");
        source.AppendLine("        {");
        source.AppendLine("            var component = entity.FindComponent(componentType);");
        source.AppendLine("            if (component is null || !component.Properties.ReadBoolean(\"enabled\", true))");
        source.AppendLine("            {");
        source.AppendLine("                return entity;");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            var valuePerSecond = component.Properties.ReadNumber(\"valuePerSecond\", 1);");
        source.AppendLine("            var transform = entity.Transform;");
        source.AppendLine("            return entity.WithPosition3D(new RekallAgeRuntimeVector3(");
        source.AppendLine("                transform.Position3D.X + valuePerSecond * seconds,");
        source.AppendLine("                transform.Position3D.Y,");
        source.AppendLine("                transform.Position3D.Z));");
        source.AppendLine("        }).ToArray();");
        source.AppendLine();
        source.AppendLine("        return ValueTask.FromResult(world with { Entities = entities });");
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
