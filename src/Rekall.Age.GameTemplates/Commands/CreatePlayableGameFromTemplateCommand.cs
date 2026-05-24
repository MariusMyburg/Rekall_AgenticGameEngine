using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record CreatePlayableGameFromTemplateRequest(
    string ProjectRoot,
    string ProjectName,
    string TemplateId,
    string? ModuleId = null,
    string? ModuleName = null);

public sealed record CreatePlayableGameFromTemplateResult(
    RekallAgeGameTemplate Template,
    string ModuleSourcePath,
    string ModuleProjectPath,
    string ModuleAssemblyPath,
    string BuildOutput);

public sealed class CreatePlayableGameFromTemplateCommand
    : IRekallAgeCommand<CreatePlayableGameFromTemplateRequest, CreatePlayableGameFromTemplateResult>
{
    private readonly CreateGameFromTemplateCommand _createGame = new();
    private readonly ScaffoldPlayableModuleCommand _scaffoldModule = new();
    private readonly BuildModulesCommand _buildModules = new();

    public string Name => "rekall.workflow.create_playable_game_from_template";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a starter game, scaffolds a genre-aware playable C# module, and builds it.",
        typeof(CreatePlayableGameFromTemplateRequest).FullName!,
        typeof(CreatePlayableGameFromTemplateResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreatePlayableGameFromTemplateResult>> ExecuteAsync(
        CreatePlayableGameFromTemplateRequest request,
        RekallAgeCommandContext context)
    {
        RekallAgeCommandResult<CreateGameFromTemplateResult> created;
        try
        {
            created = await _createGame.ExecuteAsync(
                new CreateGameFromTemplateRequest(request.ProjectRoot, request.ProjectName, request.TemplateId),
                context);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(
                ex.Message,
                [new RekallAgeCommandError("REKALL_TEMPLATE_NOT_FOUND", ex.Message, request.TemplateId)]);
        }

        if (!created.Ok)
        {
            return Failure(
                created.Summary,
                created.Errors);
        }

        var template = created.Value.Template;
        var moduleName = ToIdentifier(request.ModuleName ?? $"{template.Id}Playable", "PlayableGame");
        var moduleId = string.IsNullOrWhiteSpace(request.ModuleId)
            ? $"game.{template.Id}.playable"
            : request.ModuleId.Trim();
        var scaffold = await _scaffoldModule.ExecuteAsync(
            new ScaffoldPlayableModuleRequest(
                request.ProjectRoot,
                moduleId,
                $"{template.DisplayName} Playable",
                moduleName,
                template.Id),
            context);
        if (!scaffold.Ok)
        {
            return Failure(scaffold.Summary, scaffold.Errors);
        }

        var build = await _buildModules.ExecuteAsync(new BuildModulesRequest(request.ProjectRoot), context);
        if (!build.Ok)
        {
            return Failure(build.Summary, build.Errors);
        }

        var builtModule = build.Value.Modules.FirstOrDefault(module =>
            module.ProjectPath.Equals(scaffold.Value.ProjectPath, StringComparison.Ordinal))
            ?? build.Value.Modules.First();
        return RekallAgeCommandResult<CreatePlayableGameFromTemplateResult>.Success(
            new CreatePlayableGameFromTemplateResult(
                template,
                scaffold.Value.SourcePath,
                scaffold.Value.ProjectPath,
                builtModule.AssemblyPath,
                builtModule.Output),
            $"Created playable {template.Id} game '{request.ProjectName}'.");
    }

    private static RekallAgeCommandResult<CreatePlayableGameFromTemplateResult> Failure(
        string summary,
        IReadOnlyList<RekallAgeCommandError> errors)
    {
        return RekallAgeCommandResult<CreatePlayableGameFromTemplateResult>.Failure(
            new CreatePlayableGameFromTemplateResult(
                new RekallAgeGameTemplate(string.Empty, string.Empty, string.Empty, [], []),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty),
            summary,
            errors);
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
}
