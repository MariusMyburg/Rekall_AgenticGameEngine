using Rekall.Age.Core.Commands;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record InspectGameTemplateRequest(string TemplateId);

public sealed record InspectGameTemplateResult(
    RekallAgeGameTemplate Template,
    IReadOnlyList<RekallAgeSuggestedCommand> SuggestedCommands);

public sealed class InspectGameTemplateCommand
    : IRekallAgeCommand<InspectGameTemplateRequest, InspectGameTemplateResult>
{
    private readonly RekallAgeGameTemplateCatalog _catalog;

    public InspectGameTemplateCommand()
        : this(RekallAgeGameTemplateCatalog.CreateDefault())
    {
    }

    public InspectGameTemplateCommand(RekallAgeGameTemplateCatalog catalog)
    {
        _catalog = catalog;
    }

    public string Name => "rekall.templates.inspect";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects one built-in Rekall AGE game template and returns agent-ready workflow suggestions.",
        typeof(InspectGameTemplateRequest).FullName!,
        typeof(InspectGameTemplateResult).FullName!);

    public ValueTask<RekallAgeCommandResult<InspectGameTemplateResult>> ExecuteAsync(
        InspectGameTemplateRequest request,
        RekallAgeCommandContext context)
    {
        var template = _catalog.GetRequired(request.TemplateId);
        var suggestedCommands = new[]
        {
            new RekallAgeSuggestedCommand(
                "rekall.workflow.create_playable_package_from_template",
                new Dictionary<string, object?>
                {
                    ["projectRoot"] = "<project-root>",
                    ["projectName"] = template.DisplayName,
                    ["templateId"] = template.Id,
                    ["sceneName"] = "Main",
                    ["frames"] = 1
                }),
            new RekallAgeSuggestedCommand(
                "rekall.workflow.create_playable_game_from_template",
                new Dictionary<string, object?>
                {
                    ["projectRoot"] = "<project-root>",
                    ["projectName"] = template.DisplayName,
                    ["templateId"] = template.Id
                }),
            new RekallAgeSuggestedCommand(
                "rekall.workflow.create_game_from_template",
                new Dictionary<string, object?>
                {
                    ["projectRoot"] = "<project-root>",
                    ["projectName"] = template.DisplayName,
                    ["templateId"] = template.Id
                })
        };
        return ValueTask.FromResult(RekallAgeCommandResult<InspectGameTemplateResult>.Success(
            new InspectGameTemplateResult(template, suggestedCommands),
            $"Inspected template '{template.Id}'."));
    }
}
