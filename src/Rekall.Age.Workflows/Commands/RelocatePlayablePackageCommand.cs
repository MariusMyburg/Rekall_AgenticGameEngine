using Rekall.Age.Core.Commands;

namespace Rekall.Age.Workflows.Commands;

public sealed record RelocatePlayablePackageRequest(
    string PackagePath,
    string DestinationDirectory);

public sealed record RelocatePlayablePackageResult(
    bool Ready,
    string SourcePackagePath,
    string PackagePath,
    string ManifestPath,
    int FileCount);

public sealed class RelocatePlayablePackageCommand
    : IRekallAgeCommand<RelocatePlayablePackageRequest, RelocatePlayablePackageResult>
{
    private readonly InspectPlayablePackageCommand _inspect = new();
    private readonly Func<string, long> _availableFreeSpace;

    public RelocatePlayablePackageCommand()
        : this(GetAvailableFreeSpace)
    {
    }

    public RelocatePlayablePackageCommand(Func<string, long> availableFreeSpace)
    {
        _availableFreeSpace = availableFreeSpace ?? throw new ArgumentNullException(nameof(availableFreeSpace));
    }

    public string Name => "rekall.workflow.relocate_playable_package";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Copies a validated playable package directory, manifest, or zip archive to a new package directory, then verifies the relocated copy. Pass PackagePlayableGameResult.OutputDirectory or ArchivePath as PackagePath; never pass LaunchPath.",
        typeof(RelocatePlayablePackageRequest).FullName!,
        typeof(RelocatePlayablePackageResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RelocatePlayablePackageResult>> ExecuteAsync(
        RelocatePlayablePackageRequest request,
        RekallAgeCommandContext context)
    {
        var source = Path.GetFullPath(request.PackagePath);
        var destination = Path.GetFullPath(request.DestinationDirectory);
        var empty = new RelocatePlayablePackageResult(false, source, destination, string.Empty, 0);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            var error = new RekallAgeCommandError(
                "REKALL_PACKAGE_RELOCATION_DESTINATION_EXISTS",
                "Package relocation requires a destination path that does not already exist.",
                destination);
            return RekallAgeCommandResult<RelocatePlayablePackageResult>.Failure(empty, error.Message, [error]);
        }

        var sourceRoot = Directory.Exists(source)
            ? source
            : Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                ? null
                : Path.GetDirectoryName(source);
        if (sourceRoot is not null && IsWithin(destination, sourceRoot))
        {
            var error = new RekallAgeCommandError(
                "REKALL_PACKAGE_RELOCATION_DESTINATION_UNSAFE",
                "Package relocation destination must not be inside the source package directory.",
                destination);
            return RekallAgeCommandResult<RelocatePlayablePackageResult>.Failure(empty, error.Message, [error]);
        }

        var sourceInspection = await _inspect.ExecuteAsync(
            new InspectPlayablePackageRequest(source),
            context);
        if (!sourceInspection.Ok || !sourceInspection.Value.Ready)
        {
            return RekallAgeCommandResult<RelocatePlayablePackageResult>.Failure(
                empty,
                "Source playable package is not ready for relocation.",
                sourceInspection.Errors);
        }

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException($"Destination '{destination}' has no parent directory.");
        var requiredBytes = sourceInspection.Value.Files.Sum(file => file.SizeBytes);
        var availableBytes = _availableFreeSpace(parent);
        if (availableBytes < requiredBytes)
        {
            var message = $"Package relocation requires {requiredBytes} bytes but destination volume has only {availableBytes} bytes available. Choose a destination on a volume with sufficient free space; do not retry the same destination.";
            var error = new RekallAgeCommandError(
                "REKALL_PACKAGE_RELOCATION_SPACE_INSUFFICIENT",
                message,
                destination);
            return RekallAgeCommandResult<RelocatePlayablePackageResult>.Failure(empty, message, [error]);
        }

        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".rekall-relocate-{Guid.NewGuid():N}");
        try
        {
            if (File.Exists(source) && Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    RekallAgeSafePackageExtraction.Extract(source, staging);
                }
                catch (Exception exception) when (
                    exception is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    var message = "Package archive changed after integrity inspection or could not be extracted safely. Recreate or revalidate the source package before relocation.";
                    var error = new RekallAgeCommandError(
                        "REKALL_PACKAGE_RELOCATION_SOURCE_CHANGED",
                        message,
                        source);
                    return RekallAgeCommandResult<RelocatePlayablePackageResult>.Failure(
                        empty,
                        $"{message} {exception.Message}",
                        [error]);
                }
            }
            else
            {
                CopyDirectory(sourceRoot!, staging, context.CancellationToken);
            }

            var relocatedInspection = await _inspect.ExecuteAsync(
                new InspectPlayablePackageRequest(staging),
                context);
            if (!relocatedInspection.Ok || !relocatedInspection.Value.Ready)
            {
                return RekallAgeCommandResult<RelocatePlayablePackageResult>.Failure(
                    empty,
                    "Relocated playable package failed integrity verification.",
                    relocatedInspection.Errors);
            }

            Directory.Move(staging, destination);
            var manifestPath = Path.Combine(destination, "rekall.package.json");
            context.Transaction.RecordChangedResource(manifestPath);
            return RekallAgeCommandResult<RelocatePlayablePackageResult>.Success(
                new RelocatePlayablePackageResult(
                    true,
                    source,
                    destination,
                    manifestPath,
                    relocatedInspection.Value.FileCount),
                $"Relocated and verified playable package at '{destination}'.");
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static bool IsWithin(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(prefix, comparison);
    }

    private static long GetAvailableFreeSpace(string destinationParent)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(destinationParent));
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException($"Destination '{destinationParent}' has no storage volume root.");
        }

        return new DriveInfo(root).AvailableFreeSpace;
    }
}
