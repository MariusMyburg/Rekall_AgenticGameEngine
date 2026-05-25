using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime;

namespace Rekall.Age.Rendering.Commands;

public sealed record ExportSceneGlbRequest(
    string ProjectRoot,
    string SceneName,
    string OutputPath,
    int Frames = 0);

public sealed record ExportSceneGlbResult(
    bool Exported,
    string OutputPath,
    int FrameIndex,
    int NodeCount,
    int MeshCount,
    int MaterialCount,
    int ImageCount,
    long BytesWritten,
    IReadOnlyList<string> Warnings);

public sealed class ExportSceneGlbCommand
    : IRekallAgeCommand<ExportSceneGlbRequest, ExportSceneGlbResult>
{
    public string Name => "rekall.render.export_scene_glb";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Exports the runtime-renderable 3D scene geometry to a binary glTF/GLB file with transforms, normals, UVs, vertex colors, and PBR materials.",
        typeof(ExportSceneGlbRequest).FullName!,
        typeof(ExportSceneGlbResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ExportSceneGlbResult>> ExecuteAsync(
        ExportSceneGlbRequest request,
        RekallAgeCommandContext context)
    {
        var validation = Validate(request);
        if (validation.Count > 0)
        {
            return RekallAgeCommandResult<ExportSceneGlbResult>.Failure(
                Empty(request),
                "GLB export requires a project root, scene name, non-negative frame count, and .glb output path.",
                validation);
        }

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            request.Frames,
            context.CancellationToken);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            world,
            width: 320,
            height: 180,
            debugOverlay: false);
        var textureAssets = await LoadTextureAssetsAsync(request.ProjectRoot, context.CancellationToken);
        var modelAssets = await LoadModelAssetsAsync(request.ProjectRoot, context.CancellationToken);
        var export = await new RekallAgeGlbSceneExporter().ExportAsync(
            frame,
            request.OutputPath,
            textureAssets,
            modelAssets,
            context.CancellationToken);

        var result = new ExportSceneGlbResult(
            export.MeshCount > 0 && export.BytesWritten > 0,
            export.OutputPath,
            frame.FrameIndex,
            export.NodeCount,
            export.MeshCount,
            export.MaterialCount,
            export.ImageCount,
            export.BytesWritten,
            export.Warnings);
        if (!result.Exported)
        {
            var error = new RekallAgeCommandError(
                "REKALL_GLTF_EXPORT_NO_MESHES",
                "Scene does not contain any supported mesh renderables to export.",
                request.SceneName);
            return RekallAgeCommandResult<ExportSceneGlbResult>.Failure(result, error.Message, [error]);
        }

        context.Transaction.RecordChangedResource(export.OutputPath);
        return RekallAgeCommandResult<ExportSceneGlbResult>.Success(
            result,
            $"Exported GLB scene '{request.SceneName}' with {result.MeshCount} mesh(es) to '{export.OutputPath}'.");
    }

    private static IReadOnlyList<RekallAgeCommandError> Validate(ExportSceneGlbRequest request)
    {
        var errors = new List<RekallAgeCommandError>();
        if (string.IsNullOrWhiteSpace(request.ProjectRoot))
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_GLTF_EXPORT_INVALID_REQUEST",
                "Project root is required.",
                request.ProjectRoot));
        }

        if (string.IsNullOrWhiteSpace(request.SceneName))
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_GLTF_EXPORT_INVALID_REQUEST",
                "Scene name is required.",
                request.SceneName));
        }

        if (request.Frames < 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_GLTF_EXPORT_INVALID_REQUEST",
                "Frame count cannot be negative.",
                request.SceneName));
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath)
            || !Path.GetExtension(request.OutputPath).Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_GLTF_EXPORT_INVALID_REQUEST",
                "Output path must end in .glb.",
                request.OutputPath));
        }

        return errors;
    }

    private static ExportSceneGlbResult Empty(ExportSceneGlbRequest request)
    {
        return new ExportSceneGlbResult(
            false,
            request.OutputPath,
            Math.Max(0, request.Frames),
            0,
            0,
            0,
            0,
            0,
            Array.Empty<string>());
    }

    private static async ValueTask<IReadOnlyDictionary<string, RekallAgeGlbTextureAsset>> LoadTextureAssetsAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(projectRoot, cancellationToken);
        var textures = new Dictionary<string, RekallAgeGlbTextureAsset>(StringComparer.Ordinal);
        foreach (var asset in catalog.Assets)
        {
            var mimeType = GetImageMimeType(asset.ImportedPath);
            if (mimeType is null || !File.Exists(asset.ImportedPath))
            {
                continue;
            }

            textures[asset.Id] = new RekallAgeGlbTextureAsset(
                asset.Id,
                asset.DisplayName,
                mimeType,
                await File.ReadAllBytesAsync(asset.ImportedPath, cancellationToken));
        }

        return textures;
    }

    private static async ValueTask<IReadOnlyDictionary<string, RekallAgeGlbModelAsset>> LoadModelAssetsAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(projectRoot, cancellationToken);
        var models = new Dictionary<string, RekallAgeGlbModelAsset>(StringComparer.Ordinal);
        foreach (var asset in catalog.Assets)
        {
            if (!Path.GetExtension(asset.ImportedPath).Equals(".glb", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(asset.ImportedPath))
            {
                continue;
            }

            models[asset.Id] = new RekallAgeGlbModelAsset(
                asset.Id,
                asset.DisplayName,
                asset.ImportedPath,
                await File.ReadAllBytesAsync(asset.ImportedPath, cancellationToken));
        }

        return models;
    }

    private static string? GetImageMimeType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".ktx2" => "image/ktx2",
            _ => null
        };
    }
}
