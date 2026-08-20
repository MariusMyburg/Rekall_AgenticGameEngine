using System.Text;
using Rekall.Age.Core.Commands;
using Rekall.Age.Modules.Sdk;

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
        "Scaffolds a compilable agent-owned C# component and IRekallAgeRuntimeModuleSystem without overwriting an existing module. Exact compact shape: {\"projectRoot\":\"...\",\"moduleId\":\"game.rules\",\"displayName\":\"Game Rules\",\"moduleName\":\"GameRules\",\"componentName\":\"GameState\",\"systemName\":\"GameRulesSystem\"}. Call rekall.module.inspect_runtime_sdk for exact signatures and source topology. After scaffolding, call rekall.module.list_sources and rekall.module.read_source, preserve the real SDK types/helpers, make targeted edits without duplicate definitions, then call rekall.module.write_source and rekall.build.modules. If the module exists, read and edit it; never scaffold it again.",
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
        var sourcePath = Path.Combine(directory, $"{moduleClass}.cs");
        var projectPath = Path.Combine(directory, $"{moduleName}.csproj");
        var result = new ScaffoldRuntimeSystemModuleResult(
            sourcePath,
            projectPath,
            namespaceName,
            moduleClass,
            componentName,
            systemClass);
        if (File.Exists(sourcePath) || File.Exists(projectPath))
        {
            var error = new RekallAgeCommandError(
                "REKALL_MODULE_SCAFFOLD_ALREADY_EXISTS",
                $"Runtime module '{moduleName}' already exists. Existing agent-authored source was preserved; use module source inspection and targeted writes instead of scaffolding again.",
                sourcePath,
                [
                    new RekallAgeSuggestedCommand(
                        "rekall.module.read_source",
                        new Dictionary<string, object?>
                        {
                            ["projectRoot"] = request.ProjectRoot,
                            ["moduleName"] = moduleName,
                            ["fileName"] = $"{moduleClass}.cs"
                        }),
                    new RekallAgeSuggestedCommand(
                        "rekall.module.write_source",
                        new Dictionary<string, object?>
                        {
                            ["projectRoot"] = request.ProjectRoot,
                            ["moduleName"] = moduleName,
                            ["fileName"] = $"{moduleClass}.cs"
                        })
                ]);
            return RekallAgeCommandResult<ScaffoldRuntimeSystemModuleResult>.Failure(
                result,
                error.Message,
                [error]);
        }

        Directory.CreateDirectory(directory);
        var sdk = await new RekallAgeModuleSdkInstaller().InstallAsync(request.ProjectRoot, context.CancellationToken);
        await File.WriteAllTextAsync(
            sourcePath,
            CreateSource(request.ModuleId, request.DisplayName, namespaceName, moduleClass, componentName, systemClass),
            context.CancellationToken);
        await File.WriteAllTextAsync(projectPath, RekallAgeModuleProjectFile.Create(moduleName), context.CancellationToken);
        context.Transaction.RecordChangedResource(sourcePath);
        context.Transaction.RecordChangedResource(projectPath);
        foreach (var resource in sdk.Resources)
        {
            context.Transaction.RecordChangedResource(resource);
        }

        return RekallAgeCommandResult<ScaffoldRuntimeSystemModuleResult>.Success(
            result,
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
        source.AppendLine();
        source.AppendLine("        // Generic SDK patterns for agent-authored rules:");
        source.AppendLine("        // Register every agent-owned component declared below and attached/read/written by this module with builder.RegisterComponent<T>().");
        source.AppendLine("        // InputActionValue returns double, not a vector; two-axis movement uses separate semantic actions:");
        source.AppendLine("        // Calling an input helper does not create bindings; attach Rekall.InputActionMap with an Actions entry for every consumed semantic name.");
        source.AppendLine("        // var horizontal = world.InputActionValue(\"move.horizontal\");");
        source.AppendLine("        // var vertical = world.InputActionValue(\"move.vertical\");");
        source.AppendLine("        // var held = world.IsInputActionDown(\"agent.authored.action\");");
        source.AppendLine("        // var pressed = world.WasInputActionPressed(\"agent.authored.reset\");");
        source.AppendLine("        // Runtime vectors are immutable records: create new RekallAgeRuntimeVector3(x, y, z).");
        source.AppendLine("        // world = world.UpdateEntity(entity.Id, current => current.WithPosition3D(position));");
        source.AppendLine("        // entity = entity.WithComponentNumber(componentType, \"valuePerSecond\", 2);");
        source.AppendLine("        var updatedWorld = world.UpdateEntitiesWithComponent(componentType, entity =>");
        source.AppendLine("        {");
        source.AppendLine("            if (!entity.ComponentBoolean(componentType, \"enabled\", true))");
        source.AppendLine("            {");
        source.AppendLine("                return entity;");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            var valuePerSecond = entity.ComponentNumber(componentType, \"valuePerSecond\", 1);");
        source.AppendLine("            var position = entity.Transform.Position3D;");
        source.AppendLine("            return entity.WithPosition3D(new RekallAgeRuntimeVector3(");
        source.AppendLine("                position.X + valuePerSecond * seconds,");
        source.AppendLine("                position.Y,");
        source.AppendLine("                position.Z));");
        source.AppendLine("        });");
        source.AppendLine();
        source.AppendLine("        return ValueTask.FromResult(updatedWorld);");
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
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
