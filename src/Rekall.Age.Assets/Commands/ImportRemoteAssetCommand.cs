using Rekall.Age.Assets.Remote;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Assets.Commands;

public sealed record ImportRemoteAssetRequest(
    string ProjectRoot,
    string SourceUrl,
    string Kind,
    string? DisplayName = null,
    string? ExpectedSha256 = null,
    string? Attribution = null,
    string? License = null,
    string? LicenseUrl = null,
    string? OperatorContact = null);

public sealed record ImportRemoteAssetResult(
    RekallAgeAssetDocument Asset,
    string FinalUrl,
    string? MediaType,
    long ByteCount,
    string Sha256);

public sealed class ImportRemoteAssetCommand : IRekallAgeCommand<ImportRemoteAssetRequest, ImportRemoteAssetResult>
{
    private readonly RekallAgeRemoteAssetAcquisition _acquisition;
    private readonly RekallAgeAssetCatalogStore _store = new();

    public ImportRemoteAssetCommand() : this(new RekallAgeRemoteAssetAcquisition())
    {
    }

    public ImportRemoteAssetCommand(RekallAgeRemoteAssetAcquisition acquisition)
    {
        _acquisition = acquisition ?? throw new ArgumentNullException(nameof(acquisition));
    }

    public string Name => "rekall.asset.import_remote";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Downloads and imports one exact remote asset from a public HTTPS URL with bounded size/time, bounded Retry-After handling, optional expected SHA-256 integrity, attribution, license, license URL, operator contact for a policy-compliant User-Agent, and durable provenance. This tool does not search for or select content.",
        typeof(ImportRemoteAssetRequest).FullName!,
        typeof(ImportRemoteAssetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ImportRemoteAssetResult>> ExecuteAsync(
        ImportRemoteAssetRequest request,
        RekallAgeCommandContext context)
    {
        if (!Uri.TryCreate(request.SourceUrl, UriKind.Absolute, out var source))
        {
            return Failure("REKALL_ASSET_REMOTE_URL_INVALID", "Remote asset sourceUrl must be an absolute HTTPS URL.", request.SourceUrl);
        }

        RekallAgeRemoteAssetReceipt? receipt = null;
        try
        {
            receipt = await _acquisition.AcquireAsync(
                request.ProjectRoot,
                source,
                context.CancellationToken,
                request.OperatorContact);
            if (!string.IsNullOrWhiteSpace(request.ExpectedSha256)
                && !FixedTimeDigestEquals(request.ExpectedSha256, receipt.Sha256))
            {
                return Failure(
                    "REKALL_ASSET_REMOTE_DIGEST_MISMATCH",
                    $"Remote asset SHA-256 '{receipt.Sha256}' did not match expected digest.",
                    request.SourceUrl);
            }

            var imported = await RekallAgeAssetImporter.ImportAsync(
                request.ProjectRoot,
                receipt.StagedPath,
                request.Kind,
                request.DisplayName,
                context.CancellationToken);
            var asset = imported with
            {
                SourcePath = receipt.OriginalUrl,
                Provenance = new RekallAgeAssetProvenance(
                    receipt.OriginalUrl,
                    receipt.FinalUrl,
                    DateTimeOffset.UtcNow,
                    receipt.MediaType,
                    receipt.ByteCount,
                    receipt.Sha256,
                    NormalizeOptional(request.Attribution),
                    NormalizeOptional(request.License),
                    NormalizeOptional(request.LicenseUrl))
            };
            await _store.AddOrReplaceAsync(request.ProjectRoot, asset, context.CancellationToken);
            context.Transaction.RecordChangedResource(asset.ImportedPath);
            context.Transaction.RecordChangedResource(_store.GetCatalogPath(request.ProjectRoot));

            return RekallAgeCommandResult<ImportRemoteAssetResult>.Success(
                new ImportRemoteAssetResult(asset, receipt.FinalUrl, receipt.MediaType, receipt.ByteCount, receipt.Sha256),
                $"Downloaded and imported remote asset '{asset.Id}' with SHA-256 {receipt.Sha256}.");
        }
        catch (RekallAgeRemoteAssetException error)
        {
            return Failure(error.Code, error.Message, error.Target);
        }
        finally
        {
            receipt?.DeleteStagedFile();
        }
    }

    private static bool FixedTimeDigestEquals(string expected, string actual)
    {
        try
        {
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected.Trim()),
                Convert.FromHexString(actual));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RekallAgeCommandResult<ImportRemoteAssetResult> Failure(string code, string message, string target)
    {
        var error = new RekallAgeCommandError(code, message, target);
        return RekallAgeCommandResult<ImportRemoteAssetResult>.Failure(default!, message, [error]);
    }
}
