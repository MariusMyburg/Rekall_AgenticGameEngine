using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.AssetPipeline;

public sealed class RekallAgePublishedModelOutputStore
{
    private const string CompiledFileSuffix = ".age.compiled-mesh.json";
    private const string RelativeCompiledDirectory = "Assets/Models/Compiled";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = RekallAgePersistedJson.MaximumDocumentDepth
    };

    public string GetFinalPath(string projectRoot, string assetId, string contentHash)
    {
        ValidateAssetId(assetId);
        if (!IsLowercaseSha256(contentHash))
        {
            throw new ArgumentException("Compiled model output hash must be a lowercase SHA-256 token.", nameof(contentHash));
        }
        var modelAssetRoot = GetModelAssetRoot(projectRoot);
        var finalPath = Path.Combine(modelAssetRoot, "Compiled", assetId, contentHash + CompiledFileSuffix);
        EnsurePathWithin(modelAssetRoot, finalPath, "Compiled model output path");
        return finalPath;
    }

    public async ValueTask<RekallAgeStagedModelOutput> WriteStagedAsync(
        string projectRoot,
        string assetId,
        RekallAgeCompiledMeshSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ValidateAssetId(assetId);
        ValidateSnapshot(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        var contents = Serialize(snapshot);
        var bytes = Encoding.UTF8.GetBytes(contents);
        EnsureWithinMaximumSize(bytes);
        _ = DeserializeAndValidate(bytes);

        var transactionId = Guid.NewGuid().ToString("N");
        var stagedPath = Path.Combine(
            GetModelAssetRoot(projectRoot),
            ".staging",
            transactionId,
            assetId + CompiledFileSuffix);
        EnsurePathWithin(GetStagingRoot(projectRoot), stagedPath, "Staged model output path");
        await RekallAgeAtomicFile.WriteAllTextAsync(
            stagedPath,
            contents,
            RekallAgePersistedJson.MaximumDocumentBytes,
            cancellationToken).ConfigureAwait(false);

        return new RekallAgeStagedModelOutput(
            stagedPath,
            $"{RelativeCompiledDirectory}/{assetId}/{ComputeHash(bytes)}{CompiledFileSuffix}",
            ComputeHash(bytes),
            snapshot);
    }

    public async ValueTask CommitStagedAsync(
        string projectRoot,
        RekallAgeStagedModelOutput staged,
        CancellationToken cancellationToken)
    {
        _ = await CommitStagedImmutableAsync(projectRoot, staged, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RekallAgeImmutableModelOutputCommit> CommitStagedImmutableAsync(
        string projectRoot,
        RekallAgeStagedModelOutput staged,
        CancellationToken cancellationToken)
    {
        var validated = await ReadValidatedStagedAsync(projectRoot, staged, cancellationToken).ConfigureAwait(false);
        var finalPath = GetFinalPath(projectRoot, validated.AssetId, staged.ContentHash);
        if (File.Exists(finalPath))
        {
            return await ValidateExistingCommitAsync(finalPath, staged.ContentHash, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var revision = await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
                finalPath,
                validated.Contents,
                RekallAgePersistedJson.MaximumDocumentBytes,
                RekallAgeDocumentRevision.Missing,
                cancellationToken).ConfigureAwait(false);
            return new(revision, true);
        }
        catch (RekallAgeDocumentRevisionException)
        {
            // Another publisher may have won the immutable create race. Exact bytes are reusable;
            // differing bytes at a content-addressed path are corruption and must never be replaced.
            return await ValidateExistingCommitAsync(finalPath, staged.ContentHash, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<RekallAgeImmutableModelOutputCommit> ValidateExistingCommitAsync(
        string finalPath,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        var existing = await RekallAgeBoundedFileSnapshot.ReadAsync(
            finalPath, RekallAgePersistedJson.MaximumDocumentBytes, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(existing.Revision, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "REKALL_MODEL_OUTPUT_HASH_COLLISION: Existing immutable output bytes do not match their content-addressed path.");
        }
        _ = DeserializeAndValidate(existing.Bytes);
        return new(existing.Revision, false);
    }

    public async ValueTask<RekallAgeCompiledMeshSnapshot> LoadAsync(
        string projectRoot,
        string assetId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var snapshot = await RekallAgePersistedJson.ReadAsync<RekallAgeCompiledMeshSnapshot>(
            GetFinalPath(projectRoot, assetId, contentHash),
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(snapshot);
        return snapshot;
    }

    public async ValueTask<string> HashAsync(
        string projectRoot,
        string assetId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var snapshot = await RekallAgeBoundedFileSnapshot.ReadAsync(
            GetFinalPath(projectRoot, assetId, contentHash),
            RekallAgePersistedJson.MaximumDocumentBytes,
            cancellationToken).ConfigureAwait(false);
        return ComputeHash(snapshot.Bytes);
    }

    public ValueTask DeleteStagedAsync(
        string projectRoot,
        RekallAgeStagedModelOutput staged,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staged);
        cancellationToken.ThrowIfCancellationRequested();
        _ = ValidateStagedOutput(projectRoot, staged);
        if (File.Exists(staged.Path))
        {
            File.Delete(staged.Path);
        }

        var transactionDirectory = Path.GetDirectoryName(staged.Path);
        if (transactionDirectory is not null && Directory.Exists(transactionDirectory)
            && !Directory.EnumerateFileSystemEntries(transactionDirectory).Any())
        {
            Directory.Delete(transactionDirectory);
        }

        return ValueTask.CompletedTask;
    }

    private static async ValueTask<ValidatedStagedOutput> ReadValidatedStagedAsync(
        string projectRoot,
        RekallAgeStagedModelOutput staged,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staged);
        cancellationToken.ThrowIfCancellationRequested();

        var assetId = ValidateStagedOutput(projectRoot, staged);
        var stagedFile = await RekallAgeBoundedFileSnapshot.ReadAsync(
            staged.Path,
            RekallAgePersistedJson.MaximumDocumentBytes,
            cancellationToken).ConfigureAwait(false);
        var snapshot = DeserializeAndValidate(stagedFile.Bytes);
        if (!string.Equals(ComputeHash(stagedFile.Bytes), staged.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("REKALL_MODEL_OUTPUT_STAGE_HASH_INVALID: Staged model output content does not match its declared SHA-256 hash.");
        }
        if (!stagedFile.Bytes.SequenceEqual(Encoding.UTF8.GetBytes(Serialize(snapshot))))
        {
            throw new InvalidDataException("REKALL_MODEL_OUTPUT_STAGE_DOCUMENT_INVALID: Staged model output is not canonical JSON.");
        }

        ValidateSnapshot(staged.Snapshot);
        if (!string.Equals(Serialize(snapshot), Serialize(staged.Snapshot), StringComparison.Ordinal))
        {
            throw new InvalidDataException("REKALL_MODEL_OUTPUT_STAGE_SNAPSHOT_INVALID: Staged model output content does not match its declared snapshot.");
        }

        return new(assetId, Encoding.UTF8.GetString(stagedFile.Bytes));
    }

    private static string Serialize(RekallAgeCompiledMeshSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions) + "\n";

    private static RekallAgeCompiledMeshSnapshot DeserializeAndValidate(ReadOnlySpan<byte> bytes)
    {
        var snapshot = JsonSerializer.Deserialize<RekallAgeCompiledMeshSnapshot>(bytes, JsonOptions)
            ?? throw new InvalidDataException("REKALL_MODEL_OUTPUT_DOCUMENT_INVALID: Compiled model output could not be deserialized.");
        ValidateSnapshot(snapshot);
        return snapshot;
    }

    private static string ComputeHash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void EnsureWithinMaximumSize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > RekallAgePersistedJson.MaximumDocumentBytes)
        {
            throw new InvalidDataException(
                $"REKALL_MODEL_OUTPUT_TOO_LARGE: Compiled model output is {bytes.Length} bytes; the maximum is {RekallAgePersistedJson.MaximumDocumentBytes} bytes.");
        }
    }

    private static void ValidateSnapshot(RekallAgeCompiledMeshSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.SourceAssetId))
        {
            throw new InvalidDataException("REKALL_MODEL_OUTPUT_SOURCE_REQUIRED: Compiled model output requires a source asset ID.");
        }

        if (snapshot.SourceLogicalRevision < 1)
        {
            throw new InvalidDataException("REKALL_MODEL_OUTPUT_SOURCE_REVISION_INVALID: Compiled model output requires a positive source logical revision.");
        }

        ArgumentNullException.ThrowIfNull(snapshot.Vertices);
        ArgumentNullException.ThrowIfNull(snapshot.Indices);
        ArgumentNullException.ThrowIfNull(snapshot.Triangles);
        ArgumentNullException.ThrowIfNull(snapshot.Surfaces);
        ValidateFinite(snapshot.Bounds.Min, "bounds minimum");
        ValidateFinite(snapshot.Bounds.Max, "bounds maximum");

        foreach (var vertex in snapshot.Vertices)
        {
            ArgumentNullException.ThrowIfNull(vertex);
            ValidateFinite(vertex.Position, "vertex position");
            ValidateFinite(vertex.Normal, "vertex normal");
            ValidateFinite(vertex.Tangent, "vertex tangent");
            ValidateFinite(vertex.Uv, "vertex UV");
            ValidateFinite(vertex.Color, "vertex color");
        }

        if (snapshot.Indices.Count % 3 != 0)
        {
            throw new InvalidDataException("REKALL_MODEL_OUTPUT_INDICES_INVALID: Compiled model output indices must contain complete triangles.");
        }

        if (snapshot.Indices.Any(index => index >= snapshot.Vertices.Count))
        {
            throw new InvalidDataException("REKALL_MODEL_OUTPUT_INDICES_INVALID: Compiled model output contains an index outside its vertex buffer.");
        }

        var expectedTriangleCount = snapshot.Indices.Count / 3;
        if (snapshot.Triangles.Count != expectedTriangleCount)
        {
            throw new InvalidDataException("REKALL_MODEL_OUTPUT_TRIANGLES_INVALID: Compiled model output triangle metadata must match its index buffer.");
        }

        var expectedFirstIndex = 0;
        for (var surfacePosition = 0; surfacePosition < snapshot.Surfaces.Count; surfacePosition++)
        {
            var surface = snapshot.Surfaces[surfacePosition]
                ?? throw new InvalidDataException("REKALL_MODEL_OUTPUT_SURFACES_INVALID: Compiled model output surfaces cannot contain null entries.");
            if (surface.SurfaceIndex != surfacePosition || surface.MaterialSlotIndex < 0)
            {
                throw new InvalidDataException("REKALL_MODEL_OUTPUT_SURFACES_INVALID: Compiled model output surfaces must have sequential nonnegative indices and material slots.");
            }

            if (surface.SourceFaceIds is null || surface.SourceFaceIds.Count == 0
                || surface.FirstIndex != expectedFirstIndex
                || surface.FirstIndex % 3 != 0
                || surface.IndexCount <= 0
                || surface.IndexCount % 3 != 0
                || surface.IndexCount > snapshot.Indices.Count - expectedFirstIndex)
            {
                throw new InvalidDataException("REKALL_MODEL_OUTPUT_SURFACES_INVALID: Compiled model output surface ranges must be nonempty, aligned, contiguous, and bounded by the index buffer.");
            }

            expectedFirstIndex += surface.IndexCount;
        }

        if (expectedFirstIndex != snapshot.Indices.Count)
        {
            throw new InvalidDataException("REKALL_MODEL_OUTPUT_SURFACES_INVALID: Compiled model output surface ranges must cover the complete index buffer.");
        }

        for (var trianglePosition = 0; trianglePosition < snapshot.Triangles.Count; trianglePosition++)
        {
            var triangle = snapshot.Triangles[trianglePosition]
                ?? throw new InvalidDataException("REKALL_MODEL_OUTPUT_TRIANGLES_INVALID: Compiled model output triangles cannot contain null entries.");
            if (triangle.TriangleIndex != trianglePosition
                || triangle.SourceCornerIds is null
                || triangle.SourceCornerIds.Count != 3
                || triangle.SourcePointIds is null
                || triangle.SourcePointIds.Count != 3
                || triangle.SurfaceIndex < 0
                || triangle.SurfaceIndex >= snapshot.Surfaces.Count)
            {
                throw new InvalidDataException("REKALL_MODEL_OUTPUT_TRIANGLES_INVALID: Compiled model output triangles must have sequential indices, three source corners and points, and a valid surface index.");
            }

            var surface = snapshot.Surfaces[triangle.SurfaceIndex];
            var triangleFirstIndex = trianglePosition * 3;
            if (triangleFirstIndex < surface.FirstIndex || triangleFirstIndex >= surface.FirstIndex + surface.IndexCount)
            {
                throw new InvalidDataException("REKALL_MODEL_OUTPUT_TRIANGLES_INVALID: Compiled model output triangle surface metadata must agree with surface ranges.");
            }

            var firstVertex = snapshot.Vertices[checked((int)snapshot.Indices[triangleFirstIndex])];
            var secondVertex = snapshot.Vertices[checked((int)snapshot.Indices[triangleFirstIndex + 1])];
            var thirdVertex = snapshot.Vertices[checked((int)snapshot.Indices[triangleFirstIndex + 2])];
            if (triangle.SourcePointIds[0] != firstVertex.SourcePointId
                || triangle.SourcePointIds[1] != secondVertex.SourcePointId
                || triangle.SourcePointIds[2] != thirdVertex.SourcePointId
                || triangle.SourceCornerIds[0] != firstVertex.SourceCornerId
                || triangle.SourceCornerIds[1] != secondVertex.SourceCornerId
                || triangle.SourceCornerIds[2] != thirdVertex.SourceCornerId
                || !surface.SourceFaceIds.Contains(triangle.SourceFaceId))
            {
                throw new InvalidDataException("REKALL_MODEL_OUTPUT_TRIANGLES_INVALID: Compiled model output triangle provenance must match its indexed vertices and referenced surface face IDs.");
            }
        }
    }

    private static void ValidateFinite(RekallAgeGeometryVector2 value, string description)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y))
        {
            throw new InvalidDataException($"REKALL_MODEL_OUTPUT_NONFINITE_VERTEX_DATA: Compiled model output contains nonfinite {description} data.");
        }
    }

    private static void ValidateFinite(RekallAgeGeometryVector3 value, string description)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z))
        {
            throw new InvalidDataException($"REKALL_MODEL_OUTPUT_NONFINITE_VERTEX_DATA: Compiled model output contains nonfinite {description} data.");
        }
    }

    private static void ValidateFinite(RekallAgeGeometryVector4 value, string description)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z) || !double.IsFinite(value.W))
        {
            throw new InvalidDataException($"REKALL_MODEL_OUTPUT_NONFINITE_VERTEX_DATA: Compiled model output contains nonfinite {description} data.");
        }
    }

    private static string ValidateStagedOutput(string projectRoot, RekallAgeStagedModelOutput staged)
    {
        var assetId = AssetIdFromRelativeFinalPath(staged.RelativeFinalPath);
        ValidateAssetId(assetId);
        if (!IsLowercaseSha256(staged.ContentHash))
        {
            throw new ArgumentException("Staged model output content hash must be a lowercase SHA-256 token.", nameof(staged));
        }

        var pathParts = staged.RelativeFinalPath[(RelativeCompiledDirectory.Length + 1)..^CompiledFileSuffix.Length]
            .Split('/');
        if (pathParts.Length != 2 || !string.Equals(pathParts[1], staged.ContentHash, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Staged model output final path must contain its exact content hash.",
                nameof(staged));
        }

        var expectedStagedPath = Path.Combine(
            GetStagingRoot(projectRoot),
            Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(staged.Path))!),
            assetId + CompiledFileSuffix);
        EnsurePathWithin(GetStagingRoot(projectRoot), staged.Path, "Staged model output path");
        if (!string.Equals(Path.GetFullPath(staged.Path), expectedStagedPath, PathComparison))
        {
            throw new ArgumentException("Staged model output path must be contained in a single model-output staging transaction.", nameof(staged));
        }

        return assetId;
    }

    private static string AssetIdFromRelativeFinalPath(string relativeFinalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFinalPath);
        var prefix = RelativeCompiledDirectory + "/";
        if (!relativeFinalPath.StartsWith(prefix, StringComparison.Ordinal)
            || !relativeFinalPath.EndsWith(CompiledFileSuffix, StringComparison.Ordinal)
            || relativeFinalPath[prefix.Length..^CompiledFileSuffix.Length].Split('/').Length != 2)
        {
            throw new ArgumentException("Staged model output final path must be the canonical project-relative compiled-output path.", nameof(relativeFinalPath));
        }

        return relativeFinalPath[prefix.Length..^CompiledFileSuffix.Length].Split('/')[0];
    }

    private static string GetModelAssetRoot(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var fullProjectRoot = Path.GetFullPath(projectRoot);
        var modelAssetRoot = Path.Combine(fullProjectRoot, "Assets", "Models");
        return RekallAgeConfinedPath.Resolve(fullProjectRoot, modelAssetRoot, "Model Asset root path");
    }

    private static string GetStagingRoot(string projectRoot) =>
        Path.Combine(GetModelAssetRoot(projectRoot), ".staging");

    private static void EnsurePathWithin(string root, string candidate, string description)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullCandidate = Path.GetFullPath(candidate);
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!fullCandidate.StartsWith(rootWithSeparator, PathComparison))
        {
            throw new ArgumentException($"{description} must remain inside '{fullRoot}'.", nameof(candidate));
        }

        _ = RekallAgeConfinedPath.Resolve(fullRoot, fullCandidate, description);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

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

    private static bool IsLowercaseSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record ValidatedStagedOutput(string AssetId, string Contents);
}

public sealed record RekallAgeStagedModelOutput(
    string Path,
    string RelativeFinalPath,
    string ContentHash,
    RekallAgeCompiledMeshSnapshot Snapshot);

public sealed record RekallAgeImmutableModelOutputCommit(string Revision, bool Created);
