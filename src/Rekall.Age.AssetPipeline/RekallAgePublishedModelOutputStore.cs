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

    public string GetFinalPath(string projectRoot, string assetId)
    {
        ValidateAssetId(assetId);
        var modelAssetRoot = GetModelAssetRoot(projectRoot);
        var finalPath = Path.Combine(modelAssetRoot, "Compiled", assetId + CompiledFileSuffix);
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
            $"{RelativeCompiledDirectory}/{assetId}{CompiledFileSuffix}",
            ComputeHash(bytes),
            snapshot);
    }

    public async ValueTask CommitStagedAsync(
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

        await RekallAgeAtomicFile.WriteAllTextAsync(
            GetFinalPath(projectRoot, assetId),
            Encoding.UTF8.GetString(stagedFile.Bytes),
            RekallAgePersistedJson.MaximumDocumentBytes,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RekallAgeCompiledMeshSnapshot> LoadAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken)
    {
        var snapshot = await RekallAgePersistedJson.ReadAsync<RekallAgeCompiledMeshSnapshot>(
            GetFinalPath(projectRoot, assetId),
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(snapshot);
        return snapshot;
    }

    public async ValueTask<string> HashAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken)
    {
        var snapshot = await RekallAgeBoundedFileSnapshot.ReadAsync(
            GetFinalPath(projectRoot, assetId),
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

        return ValueTask.CompletedTask;
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
            || relativeFinalPath[prefix.Length..^CompiledFileSuffix.Length].Contains('/'))
        {
            throw new ArgumentException("Staged model output final path must be the canonical project-relative compiled-output path.", nameof(relativeFinalPath));
        }

        return relativeFinalPath[prefix.Length..^CompiledFileSuffix.Length];
    }

    private static string GetModelAssetRoot(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var fullProjectRoot = Path.GetFullPath(projectRoot);
        var modelAssetRoot = Path.Combine(fullProjectRoot, "Assets", "Models");
        RejectReparsePointTraversal(fullProjectRoot, modelAssetRoot, "Model Asset root path");
        return modelAssetRoot;
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

        RejectReparsePointTraversal(fullRoot, fullCandidate, description);
    }

    private static void RejectReparsePointTraversal(string trustedRoot, string candidate, string description)
    {
        var fullRoot = Path.GetFullPath(trustedRoot);
        var fullCandidate = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(fullRoot, fullCandidate);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new ArgumentException($"{description} must remain inside '{fullRoot}'.", nameof(candidate));
        }

        var current = fullRoot;
        if (IsReparsePoint(current))
        {
            throw new InvalidDataException($"REKALL_MODEL_OUTPUT_PATH_REPARSE_REJECTED: {description} cannot traverse a filesystem link or junction.");
        }

        if (relative == ".")
        {
            return;
        }

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (IsReparsePoint(current))
            {
                throw new InvalidDataException($"REKALL_MODEL_OUTPUT_PATH_REPARSE_REJECTED: {description} cannot traverse a filesystem link or junction.");
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
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
}

public sealed record RekallAgeStagedModelOutput(
    string Path,
    string RelativeFinalPath,
    string ContentHash,
    RekallAgeCompiledMeshSnapshot Snapshot);
