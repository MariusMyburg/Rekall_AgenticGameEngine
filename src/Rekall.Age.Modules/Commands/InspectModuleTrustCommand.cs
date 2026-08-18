using Rekall.Age.Core.Commands;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.Modules.Commands;

public sealed record InspectModuleTrustRequest(string ProjectRoot);

public sealed record InspectModuleTrustResult(
    bool Ready,
    string TrustPosture,
    IReadOnlyList<RekallAgeModuleTrustInspection> Modules,
    IReadOnlyList<RekallAgeModuleTrustIssue> Issues,
    IReadOnlyList<RekallAgeSuggestedCommand> NextActions);

public sealed class InspectModuleTrustCommand
    : IRekallAgeCommand<InspectModuleTrustRequest, InspectModuleTrustResult>
{
    private readonly RekallAgeProjectModuleTrustInspector _inspector;

    public InspectModuleTrustCommand()
        : this(new RekallAgeProjectModuleTrustInspector())
    {
    }

    internal InspectModuleTrustCommand(RekallAgeProjectModuleTrustInspector inspector)
    {
        _inspector = inspector;
    }

    public string Name => "rekall.module.inspect_trust";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects bounded build receipts and module artifacts without loading code. Reports the honest in-process full-trust posture; receipts prove integrity consistency, not sandboxing or publisher identity.",
        typeof(InspectModuleTrustRequest).FullName!,
        typeof(InspectModuleTrustResult).FullName!);

    public ValueTask<RekallAgeCommandResult<InspectModuleTrustResult>> ExecuteAsync(
        InspectModuleTrustRequest request,
        RekallAgeCommandContext context)
    {
        var inspection = _inspector.Inspect(request.ProjectRoot);
        var nextActions = inspection.Ready
            ? Array.Empty<RekallAgeSuggestedCommand>()
            : new[]
            {
                new RekallAgeSuggestedCommand(
                    "rekall.build.modules",
                    new Dictionary<string, object?> { ["projectRoot"] = request.ProjectRoot })
            };
        var result = new InspectModuleTrustResult(
            inspection.Ready,
            inspection.TrustPosture,
            inspection.Modules,
            inspection.Issues,
            nextActions);
        if (!inspection.Ready)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<InspectModuleTrustResult>.Failure(
                result,
                "One or more project modules failed trust inspection.",
                inspection.Issues.Select(issue => new RekallAgeCommandError(
                    issue.Code,
                    issue.Message,
                    issue.Target,
                    nextActions)).ToArray()));
        }

        return ValueTask.FromResult(RekallAgeCommandResult<InspectModuleTrustResult>.Success(
            result,
            $"Module trust inspection passed for {inspection.Modules.Count} module(s) as {inspection.TrustPosture}."));
    }
}
