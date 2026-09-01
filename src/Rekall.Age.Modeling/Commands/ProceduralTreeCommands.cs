using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Assets;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.World;

namespace Rekall.Age.Modeling.Commands;

public sealed record CreateProceduralTreeRequest(
    string ProjectRoot,
    string SceneName,
    string Name,
    int Seed,
    double X = 0,
    double Y = 0,
    double Z = 0,
    double? Height = null,
    double? TrunkRadius = null,
    double? CrownRadius = null,
    int? PrimaryBranchCount = null,
    string BarkColor = "#FFFFFF",
    string FoliageColor = "#FFFFFF");

public sealed record CreateProceduralTreeResult(
    string BarkEntityId,
    string FoliageEntityId,
    IReadOnlyList<string> AssetIds,
    int LodCount,
    int NearBranchCount,
    int NearLeafCardCount);

public sealed class CreateProceduralTreeCommand
    : IRekallAgeCommand<CreateProceduralTreeRequest, CreateProceduralTreeResult>
{
    private readonly RekallAgeMeshAssetStore _meshStore = new();
    private readonly RekallAgeSceneStore _sceneStore = new();
    private readonly RekallAgeAssetCatalogStore _assetStore = new();

    public string Name => "rekall.geometry.create_procedural_tree";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Generates and persists a realistic deterministic broadleaf tree as separate bark and alpha-ready foliage entities, including three mesh LODs, natural branch hierarchy, leaf cards, materials, and scene hierarchy. Use this instead of approximating trees with spheres, cylinders, primitive recipes, or embedded mesh blueprints.",
        typeof(CreateProceduralTreeRequest).FullName!,
        typeof(CreateProceduralTreeResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateProceduralTreeResult>> ExecuteAsync(
        CreateProceduralTreeRequest request,
        RekallAgeCommandContext context)
    {
        try
        {
            var loaded = await _sceneStore.LoadVersionedAsync(
                request.ProjectRoot, request.SceneName, context.CancellationToken).ConfigureAwait(false);
            var assetId = CreateAssetId(request.Name, request.Seed);
            var settings = RekallAgeProceduralTreeSettings.TemperateOak(request.Seed);
            settings = settings with
            {
                Height = request.Height ?? settings.Height,
                TrunkRadius = request.TrunkRadius ?? settings.TrunkRadius,
                CrownRadius = request.CrownRadius ?? settings.CrownRadius,
                PrimaryBranchCount = request.PrimaryBranchCount ?? settings.PrimaryBranchCount
            };
            var tree = RekallAgeProceduralTreeGenerator.Generate(assetId, request.Name, settings);
            var meshes = tree.Lods.SelectMany(lod => new[] { lod.Bark, lod.Foliage }).ToArray();
            foreach (var mesh in meshes)
            {
                var path = _meshStore.GetMeshPath(request.ProjectRoot, mesh.AssetId);
                if (File.Exists(path))
                    return Failure("REKALL_PROCEDURAL_TREE_ASSET_EXISTS", $"Tree asset '{mesh.AssetId}' already exists.");
                context.Transaction.CaptureResourcePreimage(path);
                await _meshStore.SaveIfRevisionAsync(
                    request.ProjectRoot, mesh, RekallAgeDocumentRevision.Missing, context.CancellationToken).ConfigureAwait(false);
                context.Transaction.RecordChangedResource(path);
            }

            var barkTexture = await ImportBuiltInTextureAsync(
                request.ProjectRoot, "temperate-oak-bark-basecolor.png", "Temperate Oak Bark", context).ConfigureAwait(false);
            var foliageTexture = await ImportBuiltInTextureAsync(
                request.ProjectRoot, "temperate-oak-foliage-basecolor.png", "Temperate Oak Foliage", context).ConfigureAwait(false);

            var bark = CreateEntity(request, tree, foliage: false, parentId: null, textureAssetId: barkTexture.Id);
            var foliage = CreateEntity(request, tree, foliage: true, parentId: bark.Id, textureAssetId: foliageTexture.Id);
            var updated = loaded.Value.AddEntity(bark).AddEntity(foliage);
            var scenePath = _sceneStore.GetScenePath(request.ProjectRoot, request.SceneName);
            context.Transaction.CaptureResourcePreimage(scenePath);
            await _sceneStore.SaveIfRevisionAsync(
                request.ProjectRoot, updated, loaded.Revision, context.CancellationToken).ConfigureAwait(false);
            context.Transaction.RecordChangedResource(scenePath);

            return RekallAgeCommandResult<CreateProceduralTreeResult>.Success(
                new(bark.Id, foliage.Id, meshes.Select(mesh => mesh.AssetId).ToArray(), tree.Lods.Count,
                    tree.Lods[0].BranchCount, tree.Lods[0].LeafCardCount),
                $"Created realistic procedural tree '{request.Name}' with bark and foliage entities, {tree.Lods.Count} LODs, {tree.Lods[0].BranchCount} near branches, and {tree.Lods[0].LeafCardCount} near leaf cards.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException)
        {
            return Failure("REKALL_PROCEDURAL_TREE_CREATE_FAILED", exception.Message);
        }
    }

    private static RekallAgeEntityDocument CreateEntity(
        CreateProceduralTreeRequest request,
        RekallAgeGeneratedTree tree,
        bool foliage,
        string? parentId,
        string textureAssetId)
    {
        var lods = new JsonArray();
        var previousDistance = 0d;
        foreach (var lod in tree.Lods)
        {
            var mesh = foliage ? lod.Foliage : lod.Bark;
            lods.Add(new JsonObject
            {
                ["minDistance"] = previousDistance,
                ["maxDistance"] = lod.MaximumDistance,
                ["assetId"] = mesh.AssetId
            });
            previousDistance = lod.MaximumDistance;
        }
        var nearMesh = foliage ? tree.Lods[0].Foliage : tree.Lods[0].Bark;
        var entity = RekallAgeEntityDocument.Create(
                foliage ? $"{request.Name} Foliage" : request.Name,
                foliage ? ["procedural", "tree", "foliage", "realistic"] : ["procedural", "tree", "bark", "realistic"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject
            {
                ["x"] = request.X, ["y"] = request.Y, ["z"] = request.Z,
                ["scaleX"] = 1, ["scaleY"] = 1, ["scaleZ"] = 1
            }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshAssetReference", new JsonObject
            {
                ["assetId"] = nearMesh.AssetId
            }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer", new JsonObject
            {
                ["mesh"] = nearMesh.AssetId
            }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.LodGroup", new JsonObject
            {
                ["levels"] = lods
            }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Material", new JsonObject
            {
                ["baseColor"] = foliage ? request.FoliageColor : request.BarkColor,
                ["baseColorTexture"] = textureAssetId,
                ["roughnessFactor"] = foliage ? 0.72 : 0.93,
                ["metallicFactor"] = 0,
                ["alphaMode"] = foliage ? "mask" : "opaque",
                ["alphaCutoff"] = foliage ? 0.42 : 0
            }));
        return entity with { ParentId = parentId };
    }

    private async ValueTask<RekallAgeAssetDocument> ImportBuiltInTextureAsync(
        string projectRoot,
        string fileName,
        string displayName,
        RekallAgeCommandContext context)
    {
        var resourceName = $"Rekall.Age.Modeling.Resources.Textures.{fileName}";
        await using var resource = typeof(CreateProceduralTreeCommand).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Built-in tree texture resource '{resourceName}' was not found.");
        var stagingDirectory = Path.Combine(Path.GetTempPath(), "rekall-age-tree-textures");
        Directory.CreateDirectory(stagingDirectory);
        var stagingPath = Path.Combine(stagingDirectory, fileName);
        try
        {
            await using (var output = File.Create(stagingPath))
                await resource.CopyToAsync(output, context.CancellationToken).ConfigureAwait(false);
            var asset = await RekallAgeAssetImporter.ImportAsync(
                projectRoot, stagingPath, "texture", displayName, context.CancellationToken).ConfigureAwait(false);
            context.Transaction.CaptureResourcePreimage(_assetStore.GetCatalogPath(projectRoot));
            await _assetStore.AddOrReplaceAsync(projectRoot, asset, context.CancellationToken).ConfigureAwait(false);
            context.Transaction.RecordChangedResource(asset.ImportedPath);
            context.Transaction.RecordChangedResource(_assetStore.GetCatalogPath(projectRoot));
            return asset;
        }
        finally
        {
            if (File.Exists(stagingPath)) File.Delete(stagingPath);
        }
    }

    private static string CreateAssetId(string name, int seed)
    {
        var slug = new string(name.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        if (slug.Length == 0) slug = "procedural-tree";
        return $"{slug}-{seed}";
    }

    private static RekallAgeCommandResult<CreateProceduralTreeResult> Failure(string code, string message) =>
        RekallAgeCommandResult<CreateProceduralTreeResult>.Failure(
            new(string.Empty, string.Empty, [], 0, 0, 0),
            message,
            [new RekallAgeCommandError(code, message, "Use a unique tree name or seed and valid oak dimensions, then retry.")]);
}
