using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record RekallAgePlacementVector3(double X, double Y, double Z);

public sealed record InstantiateModelAssetRequest(
    string ProjectRoot,
    string SceneName,
    string ModelAssetId,
    string? Name,
    RekallAgePlacementVector3 Position,
    RekallAgePlacementVector3 RotationDegrees,
    RekallAgePlacementVector3 Scale,
    string? ParentEntityId = null,
    string? ExpectedSceneRevision = null,
    string? EntityId = null);

public sealed record InstantiateModelAssetResult(
    string EntityId,
    RekallAgeSceneDocument? Scene,
    RekallAgeModelBuildState? BuildState,
    string? CompiledMeshPath,
    IReadOnlyList<RekallAgeModelBuildDiagnostic> Warnings);

public sealed class InstantiateModelAssetCommand
    : IRekallAgeCommand<InstantiateModelAssetRequest, InstantiateModelAssetResult>
{
    public const int MaximumWarnings = 4;
    private const double MaximumTransformMagnitude = 1_000_000;
    private const double MinimumScaleMagnitude = 0.000001;
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeModelAssetStore _modelStore;
    private readonly IRekallAgeModelAssetHealthInspector _healthInspector;

    public InstantiateModelAssetCommand(
        RekallAgeSceneStore sceneStore,
        RekallAgeModelAssetStore modelStore,
        IRekallAgeModelAssetHealthInspector healthInspector)
    {
        _sceneStore = sceneStore ?? throw new ArgumentNullException(nameof(sceneStore));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _healthInspector = healthInspector ?? throw new ArgumentNullException(nameof(healthInspector));
    }

    public string Name => "rekall.scene.instantiate_asset";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Places one published Model Asset as a generic visible entity. Requires finite bounded position/rotation and finite nonzero scale components with absolute magnitude from 0.000001 through 1000000. Optional entityId accepts a portable stable identifier and rejects collisions. Current and Frozen outputs are placeable; Stale uses the last successful output and returns bounded warnings. The scene stores only the stable Model Asset ID, never copied geometry. Optional expectedSceneRevision provides optimistic concurrency.",
        typeof(InstantiateModelAssetRequest).FullName!,
        typeof(InstantiateModelAssetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InstantiateModelAssetResult>> ExecuteAsync(
        InstantiateModelAssetRequest request,
        RekallAgeCommandContext context)
    {
        var requestError = ValidateRequest(request);
        if (requestError is not null)
        {
            return Failure(requestError);
        }

        try
        {
            var loadedScene = await _sceneStore.LoadVersionedAsync(
                request.ProjectRoot,
                request.SceneName,
                context.CancellationToken).ConfigureAwait(false);
            var scene = loadedScene.Value;
            var parentId = NormalizeOptional(request.ParentEntityId);
            var entityId = NormalizeOptional(request.EntityId);
            if (entityId is not null
                && scene.Entities.Any(entity => entity.Id.Equals(entityId, StringComparison.Ordinal)))
            {
                return Failure(new(
                    "REKALL_MODEL_ENTITY_ID_DUPLICATE",
                    $"Entity ID '{entityId}' already exists in scene '{scene.Name}'.",
                    entityId));
            }
            if (parentId is not null
                && !scene.Entities.Any(entity => entity.Id.Equals(parentId, StringComparison.Ordinal)))
            {
                return Failure(new(
                    "REKALL_MODEL_PARENT_MISSING",
                    $"Parent entity '{parentId}' was not found in scene '{scene.Name}'.",
                    parentId));
            }

            if (request.ExpectedSceneRevision is not null
                && !string.Equals(request.ExpectedSceneRevision, loadedScene.Revision, StringComparison.Ordinal))
            {
                return Failure(new(
                    "REKALL_DOCUMENT_REVISION_CONFLICT",
                    $"Scene '{scene.Name}' changed after revision '{request.ExpectedSceneRevision}'. Current revision is '{loadedScene.Revision}'.",
                    _sceneStore.GetScenePath(request.ProjectRoot, request.SceneName)));
            }

            RekallAgeModelAssetDocument asset;
            try
            {
                asset = await _modelStore.LoadAsync(
                    request.ProjectRoot,
                    request.ModelAssetId,
                    context.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                return Failure(new(
                    "REKALL_MODEL_ASSET_MISSING",
                    $"Model Asset '{request.ModelAssetId}' was not found.",
                    request.ModelAssetId));
            }

            var inspection = await _healthInspector.InspectAsync(
                request.ProjectRoot,
                request.ModelAssetId,
                context.CancellationToken).ConfigureAwait(false);
            var manifest = asset.LastSuccessfulBuild;
            if (manifest is null)
            {
                return Failure(new(
                    "REKALL_MODEL_NOT_PLACEABLE",
                    $"Model Asset '{request.ModelAssetId}' has no successful compiled output to place.",
                    request.ModelAssetId));
            }

            if (!inspection.CompiledOutputExists
                || !File.Exists(Path.GetFullPath(Path.Combine(request.ProjectRoot, manifest.CompiledMeshPath))))
            {
                return Failure(InspectionErrorOrDefault(
                    inspection,
                    "REKALL_MODEL_OUTPUT_MISSING",
                    $"The last successful compiled output for Model Asset '{request.ModelAssetId}' is missing."));
            }

            if (inspection.BuildState is not RekallAgeModelBuildState.Current
                and not RekallAgeModelBuildState.Stale
                and not RekallAgeModelBuildState.Frozen)
            {
                return Failure(InspectionErrorOrDefault(
                    inspection,
                    "REKALL_MODEL_NOT_PLACEABLE",
                    $"Model Asset '{request.ModelAssetId}' is not placeable in build state {inspection.BuildState}."));
            }

            var warnings = inspection.BuildState == RekallAgeModelBuildState.Stale
                ? inspection.Diagnostics
                    .Where(diagnostic => diagnostic.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase))
                    .Take(MaximumWarnings)
                    .ToArray()
                : [];
            var entity = CreateEntity(request, asset, parentId, entityId);
            var updated = scene.AddEntity(entity);
            var scenePath = _sceneStore.GetScenePath(request.ProjectRoot, request.SceneName);
            var scenePreimage = await RekallAgeBoundedFileSnapshot.ReadAsync(
                scenePath,
                RekallAgePersistedJson.MaximumDocumentBytes,
                context.CancellationToken).ConfigureAwait(false);
            await _sceneStore.SaveIfRevisionAsync(
                request.ProjectRoot,
                updated,
                request.ExpectedSceneRevision ?? loadedScene.Revision,
                context.CancellationToken).ConfigureAwait(false);
            context.Transaction.RecordResourcePreimage(
                scenePath,
                existedBefore: true,
                scenePreimage.Bytes);
            context.Transaction.RecordChangedResource(scenePath);

            return RekallAgeCommandResult<InstantiateModelAssetResult>.Success(
                new(entity.Id, updated, inspection.BuildState, manifest.CompiledMeshPath, warnings),
                inspection.BuildState == RekallAgeModelBuildState.Stale
                    ? $"Placed stale Model Asset '{asset.AssetId}' using its last successful compiled output with {warnings.Length} warning(s)."
                    : $"Placed Model Asset '{asset.AssetId}' in scene '{scene.Name}'.");
        }
        catch (RekallAgeDocumentRevisionException error)
        {
            return Failure(new(error.Code, error.Message, error.Target));
        }
        catch (Exception error) when (error is JsonException
            or InvalidDataException
            or UnauthorizedAccessException
            or IOException
            or ArgumentException)
        {
            return Failure(new(
                "REKALL_MODEL_PLACEMENT_FAILED",
                error.Message,
                request.ModelAssetId));
        }
    }

    private static RekallAgeEntityDocument CreateEntity(
        InstantiateModelAssetRequest request,
        RekallAgeModelAssetDocument asset,
        string? parentId,
        string? entityId)
    {
        var entity = RekallAgeEntityDocument.Create(
                string.IsNullOrWhiteSpace(request.Name) ? asset.DisplayName : request.Name.Trim(),
                ["model-asset"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject
                {
                    ["x"] = request.Position.X,
                    ["y"] = request.Position.Y,
                    ["z"] = request.Position.Z,
                    ["pitch"] = request.RotationDegrees.X,
                    ["yaw"] = request.RotationDegrees.Y,
                    ["roll"] = request.RotationDegrees.Z,
                    ["scaleX"] = request.Scale.X,
                    ["scaleY"] = request.Scale.Y,
                    ["scaleZ"] = request.Scale.Z
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.ModelAssetReference",
                new JsonObject { ["assetId"] = asset.AssetId }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"));
        return entity with
        {
            Id = entityId ?? entity.Id,
            ParentId = parentId,
            Visible = true
        };
    }

    private static RekallAgeCommandError? ValidateRequest(InstantiateModelAssetRequest? request)
    {
        if (request is null)
        {
            return new("REKALL_MODEL_PLACEMENT_REQUEST_INVALID", "Model Asset placement request is required.", "request");
        }

        if (string.IsNullOrWhiteSpace(request.ProjectRoot)
            || string.IsNullOrWhiteSpace(request.SceneName)
            || string.IsNullOrWhiteSpace(request.ModelAssetId)
            || request.Position is null
            || request.RotationDegrees is null
            || request.Scale is null)
        {
            return new(
                "REKALL_MODEL_PLACEMENT_REQUEST_INVALID",
                "Project root, scene name, Model Asset ID, position, rotationDegrees, and scale are required.",
                request.ModelAssetId);
        }

        if (!IsBounded(request.Position)
            || !IsBounded(request.RotationDegrees)
            || !IsValidScale(request.Scale))
        {
            return new(
                "REKALL_MODEL_PLACEMENT_TRANSFORM_INVALID",
                "Placement transform components must be finite and bounded; every scale component must have absolute magnitude from 0.000001 through 1000000.",
                request.ModelAssetId);
        }

        var entityId = NormalizeOptional(request.EntityId);
        if (entityId is not null && !IsValidEntityId(entityId))
        {
            return new(
                "REKALL_MODEL_ENTITY_ID_INVALID",
                "Entity ID must be 1 to 128 characters, begin with a letter, digit, or underscore, and contain only letters, digits, underscore, hyphen, period, or colon.",
                "entityId");
        }

        if (request.ExpectedSceneRevision is not null
            && !RekallAgeDocumentRevision.IsValid(request.ExpectedSceneRevision))
        {
            return new(
                "REKALL_MODEL_PLACEMENT_REQUEST_INVALID",
                "Expected scene revision must be 'missing' or a lowercase SHA-256 token.",
                "expectedSceneRevision");
        }

        return null;
    }

    private static bool IsBounded(RekallAgePlacementVector3 value) =>
        IsBounded(value.X) && IsBounded(value.Y) && IsBounded(value.Z);

    private static bool IsValidScale(RekallAgePlacementVector3 value) =>
        IsScale(value.X) && IsScale(value.Y) && IsScale(value.Z);

    private static bool IsBounded(double value) =>
        double.IsFinite(value) && Math.Abs(value) <= MaximumTransformMagnitude;

    private static bool IsScale(double value) =>
        IsBounded(value) && Math.Abs(value) >= MinimumScaleMagnitude;

    public static bool IsValidEntityId(string value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= 128
        && (char.IsAsciiLetterOrDigit(value[0]) || value[0] == '_')
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-' or '.' or ':');

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RekallAgeCommandError InspectionErrorOrDefault(
        RekallAgeModelAssetInspection inspection,
        string defaultCode,
        string defaultMessage)
    {
        var diagnostic = inspection.Diagnostics.FirstOrDefault(item =>
            item.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
        return diagnostic is null
            ? new(defaultCode, defaultMessage, inspection.Asset?.AssetId)
            : new(diagnostic.Code, diagnostic.Message, diagnostic.Target);
    }

    private static RekallAgeCommandResult<InstantiateModelAssetResult> Failure(RekallAgeCommandError error) =>
        RekallAgeCommandResult<InstantiateModelAssetResult>.Failure(
            new(string.Empty, null, null, null, []),
            error.Message,
            [error]);
}
