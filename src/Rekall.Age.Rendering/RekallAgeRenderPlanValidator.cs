namespace Rekall.Age.Rendering;

public sealed class RekallAgeRenderPlanValidator
{
    public RekallAgeRenderPlanValidationReport Validate(RekallAgeRenderPlanDocument plan)
    {
        var issues = new List<RekallAgeRenderPlanIssue>();
        var backendIds = RekallAgeRenderBackendCatalog.CreateDefault()
            .Backends
            .Select(backend => backend.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (!backendIds.Contains(plan.BackendId))
        {
            issues.Add(new RekallAgeRenderPlanIssue(
                "REKALL_RENDER_BACKEND_UNKNOWN",
                $"Render backend '{plan.BackendId}' is not registered.",
                plan.BackendId));
        }

        var resourceIds = plan.Resources
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var commandBuffer in plan.CommandBuffers)
        {
            foreach (var command in commandBuffer.Commands)
            {
                if (command.Arguments.TryGetValue("target", out var target)
                    && !resourceIds.Contains(target))
                {
                    issues.Add(new RekallAgeRenderPlanIssue(
                        "REKALL_RENDER_RESOURCE_MISSING",
                        $"Command '{command.Op}' references missing render resource '{target}'.",
                        target));
                }
            }
        }

        return new RekallAgeRenderPlanValidationReport(issues.Count == 0, issues);
    }
}

public sealed record RekallAgeRenderPlanValidationReport(
    bool Valid,
    IReadOnlyList<RekallAgeRenderPlanIssue> Issues);

public sealed record RekallAgeRenderPlanIssue(
    string Code,
    string Message,
    string Target);
