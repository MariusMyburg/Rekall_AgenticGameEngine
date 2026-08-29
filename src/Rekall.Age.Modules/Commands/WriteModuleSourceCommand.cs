using System.Text;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Modules.Commands;

public sealed record WriteModuleSourceRequest(
    string ProjectRoot,
    string ModuleName,
    string FileName,
    string Source,
    string? ExpectedSourceSha256 = null);

public sealed record WriteModuleSourceResult(
    string SourcePath,
    int BytesWritten);

public sealed class WriteModuleSourceCommand
    : IRekallAgeCommand<WriteModuleSourceRequest, WriteModuleSourceResult>
{
    public string Name => "rekall.module.write_source";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Writes C# source into a project module directory for agent-authored gameplay code.",
        typeof(WriteModuleSourceRequest).FullName!,
        typeof(WriteModuleSourceResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<WriteModuleSourceResult>> ExecuteAsync(
        WriteModuleSourceRequest request,
        RekallAgeCommandContext context)
    {
        var sourcePath = RekallAgeModuleSourcePaths.GetSourcePath(request.ProjectRoot, request.ModuleName, request.FileName);
        var emptyResult = new WriteModuleSourceResult(sourcePath, 0);

        if (!RekallAgeModuleSourcePaths.IsSafeModuleSourcePath(request.ProjectRoot, request.ModuleName, request.FileName, sourcePath))
        {
            var error = new RekallAgeCommandError(
                "REKALL_MODULE_SOURCE_PATH_OUTSIDE_PROJECT",
                "Module source paths must stay under their project module directory.",
                sourcePath);
            return RekallAgeCommandResult<WriteModuleSourceResult>.Failure(emptyResult, error.Message, [error]);
        }

        if (!Path.GetExtension(sourcePath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var error = new RekallAgeCommandError(
                "REKALL_MODULE_SOURCE_NOT_CSHARP",
                "Module source files must use the .cs extension.",
                sourcePath);
            return RekallAgeCommandResult<WriteModuleSourceResult>.Failure(emptyResult, error.Message, [error]);
        }

        if (request.ExpectedSourceSha256 is not null)
        {
            try
            {
                await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
                    sourcePath,
                    request.Source,
                    4 * 1024 * 1024,
                    request.ExpectedSourceSha256,
                    context.CancellationToken).ConfigureAwait(false);
            }
            catch (RekallAgeDocumentRevisionException)
            {
                var conflict = new RekallAgeCommandError(
                    "REKALL_MODULE_SOURCE_CHANGED",
                    "Module source changed outside this editing session. Reload it before saving.",
                    sourcePath);
                return RekallAgeCommandResult<WriteModuleSourceResult>.Failure(emptyResult, conflict.Message, [conflict]);
            }
        }
        else
        {
            await RekallAgeAtomicFile.WriteAllTextAsync(
                sourcePath,
                request.Source,
                4 * 1024 * 1024,
                context.CancellationToken).ConfigureAwait(false);
        }
        context.Transaction.RecordChangedResource(sourcePath);

        return RekallAgeCommandResult<WriteModuleSourceResult>.Success(
            new WriteModuleSourceResult(sourcePath, Encoding.UTF8.GetByteCount(request.Source)),
            $"Wrote module source '{sourcePath}'.");
    }
}
