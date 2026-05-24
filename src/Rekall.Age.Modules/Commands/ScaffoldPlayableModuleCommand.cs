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
        AppendDrawCommands(source, kind);
        AppendRenderReturn(source, kind);
        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void AppendDrawCommands(StringBuilder source, string kind)
    {
        source.AppendLine("        var drawCommands = new RekallAgePlayableDrawCommand[]");
        source.AppendLine("        {");
        source.AppendLine("            new RekallAgePlayableDrawCommand(\"clear\", \"background\", 0, 0, 320, 180, \"#101820\"),");
        switch (kind.Trim().ToLowerInvariant())
        {
            case "pong":
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"left-paddle\", 16, 70 + (lane * 8), 8, 40, \"#f2aa4c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"right-paddle\", 296, 70 - (lane * 6), 8, 40, \"#4b8bbe\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"circle\", \"ball\", 140 + (phase * 4), 86 + (lane * 2), 10, 10, \"#f6f7f8\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"text\", \"hud\", 8, 8, 0, 0, \"#ffffff\", $\"Score {score}\")");
                break;
            case "breakout":
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"brick-field\", 38, 22, 244, Math.Max(8, 44 - action), \"#d94f45\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"paddle\", 126 + (lane * 8), 154, 68, 8, \"#f2aa4c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"circle\", \"ball\", 154 + (phase * 3), 112 - (lane * 2), 9, 9, \"#f6f7f8\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"text\", \"hud\", 8, 8, 0, 0, \"#ffffff\", $\"Score {score}\")");
                break;
            case "asteroids":
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"ship\", 152 + (lane * 5), 86, 18, 14, \"#f2aa4c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"circle\", \"asteroid-alpha\", 56 + (phase * 3), 38, 24, 24, \"#8c8c8c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"projectile\", 176 + (phase * 5), 90, 14, 3, \"#f6f7f8\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"text\", \"hud\", 8, 8, 0, 0, \"#ffffff\", $\"Score {score}\")");
                break;
            case "top-down-shooter":
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"player\", 150 + (lane * 8), 132, 20, 20, \"#4b8bbe\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"enemy-wave\", 42 + (phase * 6), 34, 92, 18, \"#d94f45\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"projectile\", 158 + (lane * 8), 86 - (action * 2), 5, 18, \"#f2aa4c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"text\", \"hud\", 8, 8, 0, 0, \"#ffffff\", $\"Score {score}\")");
                break;
            case "platformer-2d":
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"platform-ground\", 0, 158, 320, 22, \"#4b8bbe\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"runner\", 48 + (phase * 5), 132 - (action * 3), 18, 26, \"#f2aa4c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"circle\", \"collectible\", 220, 112 + (lane * 3), 12, 12, \"#f6f7f8\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"text\", \"hud\", 8, 8, 0, 0, \"#ffffff\", $\"Score {score}\")");
                break;
            case "tower-defense":
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"enemy-path\", 0, 86, 320, 18, \"#4b8bbe\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"tower\", 96 + (lane * 8), 52, 22, 42, \"#f2aa4c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"circle\", \"enemy-wave\", 184 + (phase * 4), 86, 18, 18, \"#d94f45\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"text\", \"base-health\", 8, 8, 0, 0, \"#ffffff\", $\"Base {Math.Max(0, 20 - action)}\")");
                break;
            case "visual-novel":
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"background-panel\", 0, 0, 320, 180, \"#243b55\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"portrait-left\", 32, 28, 72, 104, \"#f2aa4c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"dialogue-box\", 16, 128, 288, 40, \"#101820\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"text\", \"choice-cursor\", 26 + (lane * 4), 138, 0, 0, \"#ffffff\", $\"Choice {action}\")");
                break;
            case "first-person-exploration":
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"corridor\", 72, 28, 176, 124, \"#243b55\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"reticle\", 156 + (lane * 2), 88, 8, 8, \"#f6f7f8\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"circle\", \"interaction-hotspot\", 216 + (phase * 2), 76, 16, 16, \"#f2aa4c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"text\", \"objective\", 8, 8, 0, 0, \"#ffffff\", $\"Clues {action}\")");
                break;
            case "collectathon-3d":
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"camera-orbit\", 24, 24, 272, 132, \"#243b55\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"avatar\", 142 + (lane * 6), 108, 24, 32, \"#4b8bbe\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"circle\", \"collectible\", 222 + (phase * 3), 74, 14, 14, \"#f2aa4c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"text\", \"goal-gate\", 8, 8, 0, 0, \"#ffffff\", $\"Collected {action}/30\")");
                break;
            case "puzzle":
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"grid\", 80, 20, 160, 140, \"#243b55\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"tile-active\", 112 + (phase * 3), 54, 24, 24, \"#f2aa4c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"cursor\", 150 + (lane * 8), 96, 28, 28, \"#f6f7f8\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"text\", \"objective\", 8, 8, 0, 0, \"#ffffff\", $\"Moves {action}\")");
                break;
            default:
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"actor\", 128 + (lane * 8), 74, 40, 40, \"#f2aa4c\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"circle\", \"focus\", 184 + (phase * 3), 86, 16, 16, \"#f6f7f8\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"rect\", \"boundary\", 24, 150, 272, 8, \"#4b8bbe\"),");
                source.AppendLine("            new RekallAgePlayableDrawCommand(\"text\", \"hud\", 8, 8, 0, 0, \"#ffffff\", $\"Frame {frame}\")");
                break;
        }

        source.AppendLine("        };");
    }

    private static void AppendRenderReturn(StringBuilder source, string kind)
    {
        switch (kind.Trim().ToLowerInvariant())
        {
            case "pong":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"PONG\\nFrame {frame}  Score {score}\\nLeft paddle lane {lane}\\nBall phase {phase}\", drawCommands);");
                break;
            case "breakout":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"BREAKOUT\\nFrame {frame}  Score {score}\\nPaddle lane {lane}\\nBricks remaining {Math.Max(0, 50 - action)}\", drawCommands);");
                break;
            case "asteroids":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"ASTEROIDS\\nFrame {frame}  Score {score}\\nShip heading phase {phase}\\nAsteroids active {Math.Max(1, 12 - action)}\", drawCommands);");
                break;
            case "top-down-shooter":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"TOP-DOWN SHOOTER\\nFrame {frame}  Score {score}\\nPlayer lane {lane}\\nWave pressure {phase}\", drawCommands);");
                break;
            case "platformer-2d":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"PLATFORMER\\nFrame {frame}  Score {score}\\nRunner lane {lane}\\nJump charges {action}\", drawCommands);");
                break;
            case "tower-defense":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"TOWER DEFENSE\\nFrame {frame}  Score {score}\\nBuild cursor {lane}\\nWave timer {phase}\", drawCommands);");
                break;
            case "visual-novel":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"VISUAL NOVEL\\nFrame {frame}\\nScene {state.Text[\"scene\"]}\\nChoice cursor {lane}\\nChoices made {action}\", drawCommands);");
                break;
            case "first-person-exploration":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"FIRST-PERSON EXPLORATION\\nFrame {frame}\\nLook sweep {lane}\\nRoom clue phase {phase}\\nInteractions {action}\", drawCommands);");
                break;
            case "collectathon-3d":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"COLLECTATHON\\nFrame {frame}  Score {score}\\nCamera orbit {lane}\\nCollected {action}/30\", drawCommands);");
                break;
            case "puzzle":
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"PUZZLE\\nFrame {frame}  Score {score}\\nCursor lane {lane}\\nMoves {action}\", drawCommands);");
                break;
            default:
                source.AppendLine("        return new RekallAgePlayableModuleFrame($\"PLAYABLE MODULE\\nKind {Kind}\\nScene {state.Text[\"scene\"]}\\nFrame {frame}\", drawCommands);");
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
