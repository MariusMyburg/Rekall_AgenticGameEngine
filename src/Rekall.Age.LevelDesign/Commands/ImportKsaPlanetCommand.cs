using System.Text.Json.Nodes;
using System.Xml;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record ImportKsaPlanetRequest(
    string ProjectRoot,
    string SceneName,
    string KsaRoot,
    string BodyId,
    string? EntityName = null);

public sealed record ImportKsaPlanetResult(
    string EntityId,
    string BodyId,
    string? SurfaceTextureAssetId,
    string? HeightTextureAssetId,
    string? NormalTextureAssetId,
    bool Atmosphere,
    int ImportedAssetCount,
    RekallAgeSceneDocument Scene);

public sealed class ImportKsaPlanetCommand
    : IRekallAgeCommand<ImportKsaPlanetRequest, ImportKsaPlanetResult>
{
    private readonly RekallAgeAssetCatalogStore _assetStore = new();
    private readonly RekallAgeSceneStore _sceneStore = new();

    public string Name => "rekall.planet.import_ksa";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Copies a Kitten Space Agency planet texture set into a Rekall project and creates a Rekall.PlanetRenderer scene entity for internal testing.",
        typeof(ImportKsaPlanetRequest).FullName!,
        typeof(ImportKsaPlanetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ImportKsaPlanetResult>> ExecuteAsync(
        ImportKsaPlanetRequest request,
        RekallAgeCommandContext context)
    {
        var ksaContentRoot = Path.Combine(request.KsaRoot, "Content", "Core");
        var astronomicalsPath = Path.Combine(ksaContentRoot, "Astronomicals.xml");
        if (!File.Exists(astronomicalsPath))
        {
            return Failure(request, "REKALL_KSA_CONTENT_MISSING", "KSA Content/Core/Astronomicals.xml was not found.", request.KsaRoot);
        }

        var body = LoadBody(astronomicalsPath, request.BodyId);
        if (body is null)
        {
            return Failure(request, "REKALL_KSA_BODY_MISSING", $"KSA body '{request.BodyId}' was not found in Astronomicals.xml.", request.BodyId);
        }

        var catalog = await _assetStore.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var imported = new List<RekallAgeAssetDocument>();
        async ValueTask<string?> ImportTextureAsync(string suffix, string displaySuffix)
        {
            var sourcePath = Path.Combine(ksaContentRoot, "Textures", $"{body.Id}_{suffix}.ktx2");
            if (!File.Exists(sourcePath))
            {
                return null;
            }

            var asset = await RekallAgeAssetImporter.ImportAsync(
                request.ProjectRoot,
                sourcePath,
                "texture",
                $"{body.Id} {displaySuffix}",
                context.CancellationToken);
            imported.Add(asset);
            catalog = catalog.AddOrReplace(asset);
            return asset.Id;
        }

        var surfaceTexture = await ImportTextureAsync("Diffuse", "Diffuse");
        var heightTexture = await ImportTextureAsync("Height", "Height");
        var normalTexture = await ImportTextureAsync("Normal", "Normal");
        await _assetStore.SaveAsync(request.ProjectRoot, catalog, context.CancellationToken);

        var scene = await LoadOrCreateSceneAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var planetProperties = new JsonObject
        {
            ["Radius"] = body.Radius,
            ["Color"] = body.Color
        };
        if (surfaceTexture is not null)
        {
            planetProperties["SurfaceTexture"] = surfaceTexture;
        }

        if (heightTexture is not null)
        {
            planetProperties["HeightTexture"] = heightTexture;
        }

        if (normalTexture is not null)
        {
            planetProperties["NormalTexture"] = normalTexture;
        }

        var entity = RekallAgeEntityDocument.Create(
                string.IsNullOrWhiteSpace(request.EntityName) ? body.Id : request.EntityName.Trim(),
                ["planet", "ksa", body.Id.ToLowerInvariant()])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["z"] = -8 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.PlanetRenderer",
                planetProperties));
        if (body.HasAtmosphere)
        {
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AtmosphereRenderer",
                new JsonObject
                {
                    ["Height"] = 0.08,
                    ["RayleighColor"] = "#7fb6ff",
                    ["Density"] = 1
                }));
        }

        var updatedScene = EnsureCameraAndLight(scene.AddEntity(entity));
        await _sceneStore.SaveAsync(request.ProjectRoot, updatedScene, context.CancellationToken);

        context.Transaction.RecordChangedResource(_assetStore.GetCatalogPath(request.ProjectRoot));
        context.Transaction.RecordChangedResource(_sceneStore.GetScenePath(request.ProjectRoot, updatedScene.Name));
        foreach (var asset in imported)
        {
            context.Transaction.RecordChangedResource(asset.ImportedPath);
        }

        return RekallAgeCommandResult<ImportKsaPlanetResult>.Success(
            new ImportKsaPlanetResult(
                entity.Id,
                body.Id,
                surfaceTexture,
                heightTexture,
                normalTexture,
                body.HasAtmosphere,
                imported.Count,
                updatedScene),
            $"Imported KSA planet '{body.Id}' with {imported.Count} texture asset(s).");
    }

    private async ValueTask<RekallAgeSceneDocument> LoadOrCreateSceneAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken)
    {
        var path = _sceneStore.GetScenePath(projectRoot, sceneName);
        return File.Exists(path)
            ? await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken)
            : RekallAgeSceneDocument.Create(sceneName, ["world", "rendering3d", "planet"]);
    }

    private static RekallAgeSceneDocument EnsureCameraAndLight(RekallAgeSceneDocument scene)
    {
        if (!scene.Entities.Any(entity => entity.Components.Any(component => component.Type == "Rekall.Camera3D")))
        {
            scene = scene.AddEntity(RekallAgeEntityDocument.Create("Main Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["z"] = 18, ["pitch"] = -8 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Camera3D",
                    new JsonObject { ["active"] = true })));
        }

        if (!scene.Entities.Any(entity => entity.Components.Any(component => component.Type == "Rekall.DirectionalLight")))
        {
            scene = scene.AddEntity(RekallAgeEntityDocument.Create("Sun Key Light", ["light"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["pitch"] = -25, ["yaw"] = -35 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.DirectionalLight",
                    new JsonObject { ["intensity"] = 1.4 })));
        }

        return scene;
    }

    private static KsaBody? LoadBody(string astronomicalsPath, string bodyId)
    {
        var document = new XmlDocument();
        document.Load(astronomicalsPath);
        var childNodes = document.DocumentElement?.ChildNodes;
        if (childNodes is null)
        {
            return null;
        }

        foreach (XmlNode node in childNodes)
        {
            if (node.Name is not ("PlanetaryBody" or "AtmosphericBody" or "MinorBody"))
            {
                continue;
            }

            var id = node.Attributes?["Id"]?.Value;
            if (!string.Equals(id, bodyId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var radius = ReadDouble(node.SelectSingleNode("./MeanRadius")?.Attributes?["Km"]?.Value, 1);
            var colorNode = node.SelectSingleNode("./Color");
            return new KsaBody(
                id ?? bodyId,
                Math.Max(0.0001, radius),
                node.Name == "AtmosphericBody" || node.SelectSingleNode("./Atmosphere") is not null,
                ReadColor(colorNode));
        }

        return null;
    }

    private static double ReadDouble(string? value, double fallback)
    {
        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static string ReadColor(XmlNode? node)
    {
        if (node is null)
        {
            return "#4b86d8";
        }

        var r = ReadUnitColor(node.Attributes?["R"]?.Value);
        var g = ReadUnitColor(node.Attributes?["G"]?.Value);
        var b = ReadUnitColor(node.Attributes?["B"]?.Value);
        return $"#{r:x2}{g:x2}{b:x2}";
    }

    private static int ReadUnitColor(string? value)
    {
        return (int)Math.Round(Math.Clamp(ReadDouble(value, 1), 0, 1) * 255);
    }

    private static RekallAgeCommandResult<ImportKsaPlanetResult> Failure(
        ImportKsaPlanetRequest request,
        string code,
        string message,
        string target)
    {
        var error = new RekallAgeCommandError(code, message, target);
        return RekallAgeCommandResult<ImportKsaPlanetResult>.Failure(
            new ImportKsaPlanetResult(string.Empty, request.BodyId, null, null, null, false, 0, RekallAgeSceneDocument.Create(request.SceneName, [])),
            message,
            [error]);
    }

    private sealed record KsaBody(string Id, double Radius, bool HasAtmosphere, string Color);
}
