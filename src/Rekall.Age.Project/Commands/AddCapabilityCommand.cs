using Rekall.Age.Core.Commands;

namespace Rekall.Age.Project.Commands;

public sealed record AddCapabilityRequest(
    string ProjectRoot,
    string Capability,
    string? ExpectedRevision = null);

public sealed record AddCapabilityResult(RekallAgeProjectManifest Manifest);

public sealed class AddCapabilityCommand : IRekallAgeCommand<AddCapabilityRequest, AddCapabilityResult>
{
    private readonly RekallAgeProjectStore _store = new();

    public string Name => "rekall.capability.add";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Adds a capability to a Rekall AGE project.",
        typeof(AddCapabilityRequest).FullName!,
        typeof(AddCapabilityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<AddCapabilityResult>> ExecuteAsync(
        AddCapabilityRequest request,
        RekallAgeCommandContext context)
    {
        var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, context.CancellationToken);
        var manifest = loaded.Value;
        var updated = manifest.AddCapability(request.Capability);
        await _store.SaveIfRevisionAsync(
            request.ProjectRoot,
            updated,
            request.ExpectedRevision ?? loaded.Revision,
            context.CancellationToken);
        context.Transaction.RecordChangedResource(Path.Combine(request.ProjectRoot, RekallAgeProjectStore.ManifestFileName));

        return RekallAgeCommandResult<AddCapabilityResult>.Success(
            new AddCapabilityResult(updated),
            $"Added capability '{RekallAgeCapability.Create(request.Capability).Id}'.");
    }
}
