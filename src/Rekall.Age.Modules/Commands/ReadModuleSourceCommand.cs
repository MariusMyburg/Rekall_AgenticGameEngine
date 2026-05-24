using Rekall.Age.Core.Commands;

namespace Rekall.Age.Modules.Commands;

public sealed record ReadModuleSourceRequest(
    string ProjectRoot,
    string ModuleName,
    string FileName);

public sealed record ReadModuleSourceResult(
    string ModuleName,
    string FileName,
    string SourcePath,
    string Source);

public sealed class ReadModuleSourceCommand
    : IRekallAgeCommand<ReadModuleSourceRequest, ReadModuleSourceResult>
{
    public string Name => "rekall.module.read_source";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Reads a C# module source file from a Rekall AGE project.",
        typeof(ReadModuleSourceRequest).FullName!,
        typeof(ReadModuleSourceResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ReadModuleSourceResult>> ExecuteAsync(
        ReadModuleSourceRequest request,
        RekallAgeCommandContext context)
    {
        var sourcePath = RekallAgeModuleSourcePaths.GetSourcePath(request.ProjectRoot, request.ModuleName, request.FileName);
        var emptyResult = new ReadModuleSourceResult(request.ModuleName, request.FileName, sourcePath, string.Empty);

        if (!RekallAgeModuleSourcePaths.IsSafeDirectModuleSourcePath(request.ProjectRoot, request.ModuleName, request.FileName, sourcePath))
        {
            var error = new RekallAgeCommandError(
                "REKALL_MODULE_SOURCE_PATH_OUTSIDE_PROJECT",
                "Module source paths must stay under one direct project module directory.",
                sourcePath);
            return RekallAgeCommandResult<ReadModuleSourceResult>.Failure(emptyResult, error.Message, [error]);
        }

        if (!File.Exists(sourcePath))
        {
            var error = new RekallAgeCommandError(
                "REKALL_MODULE_SOURCE_MISSING",
                "Module source file does not exist.",
                sourcePath);
            return RekallAgeCommandResult<ReadModuleSourceResult>.Failure(emptyResult, error.Message, [error]);
        }

        var source = await File.ReadAllTextAsync(sourcePath, context.CancellationToken);
        return RekallAgeCommandResult<ReadModuleSourceResult>.Success(
            new ReadModuleSourceResult(request.ModuleName, request.FileName, sourcePath, source),
            $"Read module source '{sourcePath}'.");
    }
}
