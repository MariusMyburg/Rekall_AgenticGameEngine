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
        source.AppendLine("        state.Numbers[\"lane\"] = 0;");
        source.AppendLine("        state.Numbers[\"phase\"] = 0;");
        source.AppendLine("        state.Numbers[\"score\"] = 0;");
        source.AppendLine("        state.Numbers[\"action\"] = 0;");
        source.AppendLine("        state.Text[\"scene\"] = context.SceneName;");
        source.AppendLine("        return state;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    public void Tick(RekallAgePlayableModuleState state, RekallAgePlayableModuleInput input)");
        source.AppendLine("    {");
        source.AppendLine("        state.Numbers[\"frame\"] += 1;");
        source.AppendLine("        state.Numbers[\"lane\"] = Math.Clamp(state.Numbers[\"lane\"] + input.VerticalAxis, -4, 4);");
        source.AppendLine("        state.Numbers[\"phase\"] = (state.Numbers[\"phase\"] + 1) % 16;");
        source.AppendLine("        if (input.PrimaryAction)");
        source.AppendLine("        {");
        source.AppendLine("            state.Numbers[\"action\"] += 1;");
        source.AppendLine("            state.Numbers[\"score\"] += 10;");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state)");
        source.AppendLine("    {");
        source.AppendLine("        var frame = (int)state.Numbers[\"frame\"];");
        source.AppendLine("        var lane = (int)state.Numbers[\"lane\"];");
        source.AppendLine("        var phase = (int)state.Numbers[\"phase\"];");
        source.AppendLine("        var score = (int)state.Numbers[\"score\"];");
        source.AppendLine("        var action = (int)state.Numbers[\"action\"];");
        AppendRenderReturn(source, kind);
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void AppendRenderReturn(StringBuilder source, string kind)
    {
        switch (kind.Trim().ToLowerInvariant())
        {
            case "pong":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"PONG\\nFrame {frame}  Score {score}\\nLeft paddle lane {lane}\\nBall phase {phase}\");");
                break;
            case "breakout":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"BREAKOUT\\nFrame {frame}  Score {score}\\nPaddle lane {lane}\\nBricks remaining {Math.Max(0, 50 - action)}\");");
                break;
            case "asteroids":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"ASTEROIDS\\nFrame {frame}  Score {score}\\nShip heading phase {phase}\\nAsteroids active {Math.Max(1, 12 - action)}\");");
                break;
            case "top-down-shooter":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"TOP-DOWN SHOOTER\\nFrame {frame}  Score {score}\\nPlayer lane {lane}\\nWave pressure {phase}\");");
                break;
            case "platformer-2d":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"PLATFORMER\\nFrame {frame}  Score {score}\\nRunner lane {lane}\\nJump charges {action}\");");
                break;
            case "tower-defense":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"TOWER DEFENSE\\nFrame {frame}  Score {score}\\nBuild cursor {lane}\\nWave timer {phase}\");");
                break;
            case "visual-novel":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"VISUAL NOVEL\\nFrame {frame}\\nScene {state.Text[\"scene\"]}\\nChoice cursor {lane}\\nChoices made {action}\");");
                break;
            case "first-person-exploration":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"FIRST-PERSON EXPLORATION\\nFrame {frame}\\nLook sweep {lane}\\nRoom clue phase {phase}\\nInteractions {action}\");");
                break;
            case "collectathon-3d":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"COLLECTATHON\\nFrame {frame}  Score {score}\\nCamera orbit {lane}\\nCollected {action}/30\");");
                break;
            case "puzzle":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"PUZZLE\\nFrame {frame}  Score {score}\\nCursor lane {lane}\\nMoves {action}\");");
                break;
            default:
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"PLAYABLE MODULE\\nKind {Kind}\\nScene {state.Text[\"scene\"]}\\nFrame {frame}\");");
                break;
        }
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
