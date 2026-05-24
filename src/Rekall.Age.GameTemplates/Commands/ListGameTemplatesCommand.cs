using Rekall.Age.Core.Commands;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record ListGameTemplatesRequest;

public sealed record ListGameTemplatesResult(IReadOnlyList<RekallAgeGameTemplate> Templates);

public sealed class ListGameTemplatesCommand
    : IRekallAgeCommand<ListGameTemplatesRequest, ListGameTemplatesResult>
{
    private readonly RekallAgeGameTemplateCatalog _catalog;

    public ListGameTemplatesCommand()
        : this(RekallAgeGameTemplateCatalog.CreateDefault())
    {
    }

    public ListGameTemplatesCommand(RekallAgeGameTemplateCatalog catalog)
    {
        _catalog = catalog;
    }

    public string Name => "rekall.templates.list";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Lists built-in Rekall AGE starter game templates.",
        typeof(ListGameTemplatesRequest).FullName!,
        typeof(ListGameTemplatesResult).FullName!);

    public ValueTask<RekallAgeCommandResult<ListGameTemplatesResult>> ExecuteAsync(
        ListGameTemplatesRequest request,
        RekallAgeCommandContext context)
    {
        var templates = _catalog.Templates;
        return ValueTask.FromResult(RekallAgeCommandResult<ListGameTemplatesResult>.Success(
            new ListGameTemplatesResult(templates),
            $"Loaded {templates.Count} game templates."));
    }
}
