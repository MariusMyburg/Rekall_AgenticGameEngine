using System.Security.Cryptography;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Core.Transactions;

public sealed record RestoreTransactionPreimageRequest(
    string ProjectRoot,
    string TransactionId,
    string RelativePath);

public sealed record RestoreTransactionPreimageResult(
    string TransactionId,
    string RelativePath,
    string RestoredPath,
    long BytesRestored);

public sealed class RestoreTransactionPreimageCommand
    : IRekallAgeCommand<RestoreTransactionPreimageRequest, RestoreTransactionPreimageResult>
{
    private readonly RekallAgeTransactionLogStore _store;

    public RestoreTransactionPreimageCommand()
        : this(new RekallAgeTransactionLogStore())
    {
    }

    public RestoreTransactionPreimageCommand(RekallAgeTransactionLogStore store)
    {
        _store = store;
    }

    public string Name => "rekall.transaction.restore_preimage";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Restores one project resource from a persisted transaction preimage snapshot.",
        typeof(RestoreTransactionPreimageRequest).FullName!,
        typeof(RestoreTransactionPreimageResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RestoreTransactionPreimageResult>> ExecuteAsync(
        RestoreTransactionPreimageRequest request,
        RekallAgeCommandContext context)
    {
        var document = await _store.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var transaction = document.Transactions.FirstOrDefault(item =>
            item.Id.Equals(request.TransactionId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Transaction '{request.TransactionId}' was not found.");
        var preimage = transaction.ResourcePreimages.FirstOrDefault(item =>
            item.RelativePath.Equals(request.RelativePath, StringComparison.Ordinal)
            || item.RelativePath.Equals(NormalizeRelativePath(request.RelativePath), StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Transaction '{request.TransactionId}' has no preimage for '{request.RelativePath}'.");

        var targetPath = ResolveProjectPath(request.ProjectRoot, preimage.RelativePath);
        context.Transaction.CaptureResourcePreimage(targetPath);

        if (!preimage.ExistedBefore)
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            context.Transaction.RecordChangedResource(targetPath);
            return RekallAgeCommandResult<RestoreTransactionPreimageResult>.Success(
                new RestoreTransactionPreimageResult(transaction.Id, preimage.RelativePath, targetPath, 0),
                $"Restored deleted preimage for '{preimage.RelativePath}'.");
        }

        if (preimage.SnapshotPath is null)
        {
            throw new InvalidOperationException($"Preimage for '{preimage.RelativePath}' has no snapshot path.");
        }

        var snapshotPath = ResolveProjectPath(request.ProjectRoot, preimage.SnapshotPath);
        if (!File.Exists(snapshotPath))
        {
            throw new InvalidOperationException($"Preimage snapshot '{preimage.SnapshotPath}' was not found.");
        }

        var bytes = await File.ReadAllBytesAsync(snapshotPath, context.CancellationToken);
        if (preimage.Sha256 is not null)
        {
            var actualSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!actualSha256.Equals(preimage.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Preimage snapshot '{preimage.SnapshotPath}' failed SHA-256 verification.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await File.WriteAllBytesAsync(targetPath, bytes, context.CancellationToken);
        context.Transaction.RecordChangedResource(targetPath);

        return RekallAgeCommandResult<RestoreTransactionPreimageResult>.Success(
            new RestoreTransactionPreimageResult(transaction.Id, preimage.RelativePath, targetPath, bytes.LongLength),
            $"Restored '{preimage.RelativePath}' from transaction '{transaction.Id}'.");
    }

    private static string ResolveProjectPath(string projectRoot, string relativePath)
    {
        var root = Path.GetFullPath(projectRoot);
        var path = Path.GetFullPath(Path.Combine(root, NormalizeRelativePath(relativePath)));
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path '{relativePath}' escapes the project root.");
        }

        return path;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
