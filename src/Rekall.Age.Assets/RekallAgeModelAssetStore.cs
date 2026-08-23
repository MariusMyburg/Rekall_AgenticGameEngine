using System.Text.Json;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Assets;

public sealed class RekallAgeModelAssetStore
{
    private const string FileSuffix = ".age.model.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };

    public string GetModelPath(string projectRoot, string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ValidateAssetId(assetId);
        var root = Path.GetFullPath(projectRoot);
        return RekallAgeConfinedPath.Resolve(
            root,
            Path.Combine(root, "Assets", "Models", assetId + FileSuffix),
            "Model Asset document path");
    }

    public string GetRecoveryPath(string projectRoot, string assetId) =>
        RekallAgeDocumentRecoveryStore.GetPreviousPath(projectRoot, GetModelPath(projectRoot, assetId));

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeModelAssetDocument model,
        CancellationToken cancellationToken)
    {
        _ = await SaveIfRevisionAsync(
            projectRoot,
            model,
            RekallAgeDocumentRevision.Missing,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string> SaveIfRevisionAsync(
        string projectRoot,
        RekallAgeModelAssetDocument model,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        ValidateForPersistence(model, model.AssetId);
        var path = GetModelPath(projectRoot, model.AssetId);
        if (File.Exists(path))
        {
            var current = await LoadVersionedAsync(projectRoot, model.AssetId, cancellationToken).ConfigureAwait(false);
            if (string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
                && model.Revision != current.Value.Revision + 1)
            {
                throw new InvalidDataException(
                    $"REKALL_MODEL_LOGICAL_REVISION_INVALID: Model asset revision must advance from {current.Value.Revision} to {current.Value.Revision + 1}.");
            }
        }
        else if (model.Revision != 1)
        {
            throw new InvalidDataException(
                "REKALL_MODEL_LOGICAL_REVISION_INVALID: A new model asset must start at revision 1.");
        }

        return await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
            path,
            Serialize(model),
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            expectedRevision,
            GetRecoveryPath(projectRoot, model.AssetId),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RekallAgeModelAssetDocument> LoadAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken) =>
        (await LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false)).Value;

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeModelAssetDocument>> LoadVersionedAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken)
    {
        var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(
            GetModelPath(projectRoot, assetId),
            "model asset",
            RekallAgeModelAssetDocument.CurrentSchemaVersion,
            cancellationToken).ConfigureAwait(false);
        var model = snapshot.Deserialize<RekallAgeModelAssetDocument>(JsonOptions);
        ValidateForPersistence(model, assetId);
        return new RekallAgeVersionedDocument<RekallAgeModelAssetDocument>(
            model with { SchemaVersion = RekallAgeModelAssetDocument.CurrentSchemaVersion },
            snapshot.File.Revision);
    }

    public IReadOnlyList<string> ListAssetIds(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var root = Path.GetFullPath(projectRoot);
        var directory = RekallAgeConfinedPath.Resolve(
            root,
            Path.Combine(root, "Assets", "Models"),
            "Model Asset listing root");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*" + FileSuffix)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name![..^FileSuffix.Length])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public string Serialize(RekallAgeModelAssetDocument model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return JsonSerializer.Serialize(
                   model with { SchemaVersion = RekallAgeModelAssetDocument.CurrentSchemaVersion },
                   JsonOptions)
               + Environment.NewLine;
    }

    public ValueTask<RekallAgeDocumentRecoveryInspection> InspectRecoveryAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken) =>
        RekallAgeDocumentRecoveryStore.InspectAsync(
            projectRoot,
            GetModelPath(projectRoot, assetId),
            "model asset",
            RekallAgeModelAssetDocument.CurrentSchemaVersion,
            snapshot => ValidateForPersistence(snapshot.Deserialize<RekallAgeModelAssetDocument>(JsonOptions), assetId),
            cancellationToken);

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeModelAssetDocument>> RestorePreviousAsync(
        string projectRoot,
        string assetId,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        await RekallAgeDocumentRecoveryStore.RestorePreviousAsync(
            projectRoot,
            GetModelPath(projectRoot, assetId),
            "model asset",
            RekallAgeModelAssetDocument.CurrentSchemaVersion,
            expectedRevision,
            snapshot => ValidateForPersistence(snapshot.Deserialize<RekallAgeModelAssetDocument>(JsonOptions), assetId),
            cancellationToken).ConfigureAwait(false);
        return await LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateForPersistence(RekallAgeModelAssetDocument model, string expectedAssetId)
    {
        ArgumentNullException.ThrowIfNull(model);
        ValidateAssetId(model.AssetId);
        if (!string.Equals(model.AssetId, expectedAssetId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"REKALL_MODEL_ASSET_ID_MISMATCH: Document asset ID '{model.AssetId}' does not match requested ID '{expectedAssetId}'.");
        }

        if (model.SchemaVersion != RekallAgeModelAssetDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"REKALL_MODEL_SCHEMA_UNSUPPORTED: Schema {model.SchemaVersion} is unsupported.");
        }

        Require(model.DisplayName, "REKALL_MODEL_DISPLAY_NAME_REQUIRED", "Model asset display name is required.");
        if (model.Revision < 1)
        {
            throw new InvalidDataException("REKALL_MODEL_LOGICAL_REVISION_INVALID: Model asset revision must be positive.");
        }

        ArgumentNullException.ThrowIfNull(model.Source);
        if (!Enum.IsDefined(model.Source.Kind))
        {
            throw new InvalidDataException("REKALL_MODEL_SOURCE_KIND_INVALID: Model asset source kind is invalid.");
        }

        Require(model.Source.AssetId, "REKALL_MODEL_SOURCE_ASSET_ID_REQUIRED", "Model asset source asset ID is required.");
        if (model.Source.OutputName is not null)
        {
            Require(model.Source.OutputName, "REKALL_MODEL_SOURCE_OUTPUT_NAME_REQUIRED", "Model asset source output name cannot be empty.");
        }

        if (!Enum.IsDefined(model.BuildState))
        {
            throw new InvalidDataException("REKALL_MODEL_BUILD_STATE_INVALID: Model asset build state is invalid.");
        }

        if (model.BuildState is RekallAgeModelBuildState.Current or RekallAgeModelBuildState.Frozen
            && model.LastSuccessfulBuild is null)
        {
            throw new InvalidDataException(
                "REKALL_MODEL_BUILD_MANIFEST_REQUIRED: Current or frozen model assets require a successful build manifest.");
        }

        if (model.LastSuccessfulBuild is not null)
        {
            ValidateManifest(model.LastSuccessfulBuild);
        }
    }

    private static void ValidateManifest(RekallAgeModelBuildManifest manifest)
    {
        Require(manifest.SourceFileRevision, "REKALL_MODEL_SOURCE_FILE_REVISION_REQUIRED", "Model build source file revision is required.");
        if (manifest.SourceLogicalRevision < 1)
        {
            throw new InvalidDataException(
                "REKALL_MODEL_SOURCE_LOGICAL_REVISION_INVALID: Model build source logical revision must be positive.");
        }

        if (!IsSafeRelativePath(manifest.CompiledMeshPath))
        {
            throw new InvalidDataException(
                "REKALL_MODEL_COMPILED_MESH_PATH_INVALID: Compiled mesh path must be a confined project-relative path.");
        }

        if (!IsLowercaseSha256(manifest.CompiledContentHash))
        {
            throw new InvalidDataException(
                "REKALL_MODEL_COMPILED_CONTENT_HASH_INVALID: Compiled content hash must be a lowercase SHA-256 token.");
        }

        Require(manifest.CompilerVersion, "REKALL_MODEL_COMPILER_VERSION_REQUIRED", "Model build compiler version is required.");
        if (manifest.BuiltAtUtc == default)
        {
            throw new InvalidDataException("REKALL_MODEL_BUILD_TIME_REQUIRED: Model build time is required.");
        }

        ArgumentNullException.ThrowIfNull(manifest.Diagnostics);
        foreach (var diagnostic in manifest.Diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            Require(diagnostic.Code, "REKALL_MODEL_BUILD_DIAGNOSTIC_CODE_REQUIRED", "Model build diagnostic code is required.");
            Require(diagnostic.Severity, "REKALL_MODEL_BUILD_DIAGNOSTIC_SEVERITY_REQUIRED", "Model build diagnostic severity is required.");
            Require(diagnostic.Message, "REKALL_MODEL_BUILD_DIAGNOSTIC_MESSAGE_REQUIRED", "Model build diagnostic message is required.");
        }
    }

    private static void ValidateAssetId(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (assetId.Length > 128
            || assetId is "." or ".."
            || !char.IsAsciiLetterOrDigit(assetId[0])
            || assetId.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "Model asset ID must be a safe 1-128 character logical identifier using ASCII letters, digits, '.', '-', or '_'.",
                nameof(assetId));
        }
    }

    private static void Require(string? value, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{code}: {message}");
        }
    }

    private static bool IsLowercaseSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || Path.IsPathFullyQualified(path))
        {
            return false;
        }

        var segments = path.Replace('\\', '/').Split('/');
        return segments.Length > 0
            && segments.All(segment =>
                segment.Length > 0
                && segment is not "." and not ".."
                && segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }
}
