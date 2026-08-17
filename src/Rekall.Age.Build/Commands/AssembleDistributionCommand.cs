using Rekall.Age.Build.Distribution;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Build.Commands;

public sealed record AssembleDistributionCommandResult(
    string Root,
    string ManifestPath,
    string ArchivePath,
    RekallAgeDistributionManifest? Manifest);

public sealed class AssembleDistributionCommand
    : IRekallAgeCommand<AssembleDistributionRequest, AssembleDistributionCommandResult>
{
    public string Name => "rekall.distribution.assemble";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Assembles published Rekall AGE tools into a verified proprietary Windows distribution.",
        typeof(AssembleDistributionRequest).FullName!,
        typeof(AssembleDistributionCommandResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<AssembleDistributionCommandResult>> ExecuteAsync(
        AssembleDistributionRequest request,
        RekallAgeCommandContext context)
    {
        try
        {
            var assembled = await new RekallAgeDistributionAssembler().AssembleAsync(
                request,
                context.CancellationToken);
            context.Transaction.RecordChangedResource(assembled.Root);
            context.Transaction.RecordChangedResource(assembled.ManifestPath);
            context.Transaction.RecordChangedResource(assembled.ArchivePath);
            return RekallAgeCommandResult<AssembleDistributionCommandResult>.Success(
                new AssembleDistributionCommandResult(
                    assembled.Root,
                    assembled.ManifestPath,
                    assembled.ArchivePath,
                    assembled.Manifest),
                $"Assembled Rekall AGE {assembled.Manifest.ProductVersion} distribution.");
        }
        catch (RekallAgeDistributionAssemblyException exception)
        {
            return RekallAgeCommandResult<AssembleDistributionCommandResult>.Failure(
                new AssembleDistributionCommandResult(
                    Path.GetFullPath(request.OutputRoot),
                    string.Empty,
                    string.Empty,
                    null),
                "Rekall AGE distribution assembly failed.",
                [new RekallAgeCommandError(exception.Code, exception.Message, exception.Target)]);
        }
    }
}
