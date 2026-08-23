using System.Security.Cryptography;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;

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
    private readonly IRekallAgeResourceRestorationPolicy _restorationPolicy;

    public RestoreTransactionPreimageCommand(IRekallAgeResourceRestorationPolicy restorationPolicy)
        : this(new RekallAgeTransactionLogStore(), restorationPolicy)
    {
    }

    public RestoreTransactionPreimageCommand(
        RekallAgeTransactionLogStore store,
        IRekallAgeResourceRestorationPolicy restorationPolicy)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(restorationPolicy);
        _store = store;
        _restorationPolicy = restorationPolicy;
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

        RekallAgeResourceRestorationAdmission admission;
        try
        {
            admission = _restorationPolicy.Admit(request.ProjectRoot, preimage.RelativePath);
        }
        catch (RekallAgeResourceRestorationException error)
        {
            return Rejected(request, error);
        }
        var targetPath = admission.Path;
        context.Transaction.CaptureResourcePreimage(targetPath);

        if (!preimage.ExistedBefore)
        {
            _restorationPolicy.Revalidate(admission);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            context.Transaction.RecordChangedResource(targetPath);
            return RekallAgeCommandResult<RestoreTransactionPreimageResult>.Success(
                new RestoreTransactionPreimageResult(transaction.Id, preimage.RelativePath, targetPath, 0),
                $"Restored deleted preimage for '{preimage.RelativePath}'.");
        }

        var delta = transaction.ResourceDeltas.FirstOrDefault(item =>
            item.RelativePath.Equals(preimage.RelativePath, StringComparison.Ordinal));
        if (delta is not null && File.Exists(targetPath))
        {
            var currentBytes = await File.ReadAllBytesAsync(targetPath, context.CancellationToken);
            var currentSha256 = Convert.ToHexString(SHA256.HashData(currentBytes)).ToLowerInvariant();
            if (currentSha256.Equals(delta.AfterSha256, StringComparison.Ordinal))
            {
                var restoredBytes = RekallAgeReversibleJsonDelta.ApplyInverse(delta, currentBytes);
                _restorationPolicy.Revalidate(admission);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                await File.WriteAllBytesAsync(targetPath, restoredBytes, context.CancellationToken);
                context.Transaction.RecordChangedResource(targetPath);
                return RekallAgeCommandResult<RestoreTransactionPreimageResult>.Success(
                    new RestoreTransactionPreimageResult(transaction.Id, preimage.RelativePath, targetPath, restoredBytes.LongLength),
                    $"Restored '{preimage.RelativePath}' from reversible transaction delta '{transaction.Id}'.");
            }
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
        _restorationPolicy.Revalidate(admission);
        await File.WriteAllBytesAsync(targetPath, bytes, context.CancellationToken);
        context.Transaction.RecordChangedResource(targetPath);

        return RekallAgeCommandResult<RestoreTransactionPreimageResult>.Success(
            new RestoreTransactionPreimageResult(transaction.Id, preimage.RelativePath, targetPath, bytes.LongLength),
            $"Restored '{preimage.RelativePath}' from transaction '{transaction.Id}'.");
    }

    private static string ResolveProjectPath(string projectRoot, string relativePath)
    {
        var root = Path.GetFullPath(projectRoot);
        return RekallAgeConfinedPath.Resolve(
            root,
            Path.Combine(root, NormalizeRelativePath(relativePath)),
            "Transaction preimage snapshot path");
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static RekallAgeCommandResult<RestoreTransactionPreimageResult> Rejected(
        RestoreTransactionPreimageRequest request,
        RekallAgeResourceRestorationException error) =>
        RekallAgeCommandResult<RestoreTransactionPreimageResult>.Failure(
            new(request.TransactionId, request.RelativePath, error.Target, 0),
            error.Message,
            [new(error.Code, error.Message, error.Target)]);
}
