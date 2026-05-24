using System.Text;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Modules.Commands;

public sealed record WriteModuleSourceRequest(
    string ProjectRoot,
    string ModuleName,
    string FileName,
    string Source);

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

        if (!RekallAgeModuleSourcePaths.IsSafeDirectModuleSourcePath(request.ProjectRoot, request.ModuleName, request.FileName, sourcePath))
        {
            var error = new RekallAgeCommandError(
                "REKALL_MODULE_SOURCE_PATH_OUTSIDE_PROJECT",
                "Module source paths must stay under one direct project module directory.",
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

        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, request.Source, Encoding.UTF8, context.CancellationToken);
        context.Transaction.RecordChangedResource(sourcePath);

        return RekallAgeCommandResult<WriteModuleSourceResult>.Success(
            new WriteModuleSourceResult(sourcePath, Encoding.UTF8.GetByteCount(request.Source)),
            $"Wrote module source '{sourcePath}'.");
    }
}
