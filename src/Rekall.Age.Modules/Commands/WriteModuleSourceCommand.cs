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
        var modulesRoot = Path.GetFullPath(Path.Combine(request.ProjectRoot, "Modules"));
        var sourcePath = Path.GetFullPath(Path.Combine(modulesRoot, request.ModuleName, request.FileName));
        var emptyResult = new WriteModuleSourceResult(sourcePath, 0);

        if (!IsSimplePathSegment(request.ModuleName) ||
            !IsSimplePathSegment(request.FileName) ||
            !IsInsideDirectory(sourcePath, modulesRoot))
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

    private static bool IsInsideDirectory(string path, string directory)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var root = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, comparison);
    }

    private static bool IsSimplePathSegment(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !Path.IsPathRooted(value) &&
            value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0 &&
            !value.Equals(".", StringComparison.Ordinal) &&
            !value.Equals("..", StringComparison.Ordinal);
    }
}
