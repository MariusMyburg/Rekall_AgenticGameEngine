using System.Text;
using Rekall.Age.Core.Commands;
using Rekall.Age.Modules.Sdk;

namespace Rekall.Age.Modules.Commands;

public sealed record ScaffoldPlayableModuleRequest(
    string ProjectRoot,
    string ModuleId,
    string DisplayName,
    string ModuleName);

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
        "Scaffolds an agent-editable C# playable module shell without engine-authored game behavior and refuses to overwrite an existing module. If the module exists, read and edit it; never scaffold it again. This is the module type rekall.build.player's native player entrypoint requires (IRekallAgePlayableModule) -- a project with only a rekall.module.scaffold_runtime_system module cannot be launched natively without also scaffolding one of these. It is a minimal Tick/Render state-and-text loop, separate from the full 3D world/physics/rendering simulation driven by IRekallAgeRuntimeModuleSystem; real visual/gameplay proof of a 3D scene comes from rekall.game.publish_web + rekall.game.audit_web plus a real browser session, not from this native entrypoint.",
        typeof(ScaffoldPlayableModuleRequest).FullName!,
        typeof(ScaffoldPlayableModuleResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ScaffoldPlayableModuleResult>> ExecuteAsync(
        ScaffoldPlayableModuleRequest request,
        RekallAgeCommandContext context)
    {
        var moduleName = RekallAgeModuleIdentifiers.ToIdentifier(request.ModuleName, "PlayableModule");
        var moduleClass = moduleName.EndsWith("Module", StringComparison.Ordinal)
            ? moduleName
            : $"{moduleName}Module";
        var namespaceName = $"Game.Modules.{moduleName}";
        var directory = Path.Combine(request.ProjectRoot, "Modules", moduleName);
        var sourcePath = Path.Combine(directory, $"{moduleClass}.cs");
        var projectPath = Path.Combine(directory, $"{moduleName}.csproj");
        var result = new ScaffoldPlayableModuleResult(sourcePath, projectPath, namespaceName, moduleClass);
        if (File.Exists(sourcePath) || File.Exists(projectPath))
        {
            var error = new RekallAgeCommandError(
                "REKALL_MODULE_SCAFFOLD_ALREADY_EXISTS",
                $"Playable module '{moduleName}' already exists. Existing agent-authored source was preserved; use module source inspection and targeted writes instead of scaffolding again.",
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
            return RekallAgeCommandResult<ScaffoldPlayableModuleResult>.Failure(
                result,
                error.Message,
                [error]);
        }

        Directory.CreateDirectory(directory);
        var sdk = await new RekallAgeModuleSdkInstaller().InstallAsync(request.ProjectRoot, context.CancellationToken);
        await File.WriteAllTextAsync(
            sourcePath,
            CreateSource(request.ModuleId, request.DisplayName, namespaceName, moduleClass),
            context.CancellationToken);
        await File.WriteAllTextAsync(projectPath, RekallAgeModuleProjectFile.Create(moduleName), context.CancellationToken);

        context.Transaction.RecordChangedResource(sourcePath);
        context.Transaction.RecordChangedResource(projectPath);
        foreach (var resource in sdk.Resources)
        {
            context.Transaction.RecordChangedResource(resource);
        }

        return RekallAgeCommandResult<ScaffoldPlayableModuleResult>.Success(
            result,
            $"Scaffolded playable module shell '{request.ModuleId}'.");
    }

    private static string CreateSource(
        string moduleId,
        string displayName,
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
        source.AppendLine("    public string Kind => \"agent-authored\";");
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
        source.AppendLine("        if (input.DeltaSeconds > 0)");
        source.AppendLine("        {");
        source.AppendLine("            state.Numbers[\"frame\"] += 1;");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state)");
        source.AppendLine("    {");
        source.AppendLine("        var frame = (int)state.Numbers[\"frame\"];");
        source.AppendLine("        return new RekallAgePlayableModuleFrame($\"AGENT PLAYABLE MODULE\\nScene {state.Text[\"scene\"]}\\nFrame {frame}\");");
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
