using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record ImportKsaSolarSystemRequest(
    string ProjectRoot,
    string SceneName,
    string KsaRoot,
    string SystemFileName = "SolSystem.xml",
    bool ImportDiffuseTextures = true,
    double DistanceScale = 0.000001,
    double RadiusScale = 0.00002);

public sealed record ImportKsaSolarSystemResult(
    int BodyCount,
    int ImportedAssetCount,
    IReadOnlyList<string> BodyIds,
    RekallAgeSceneDocument Scene);

public sealed class ImportKsaSolarSystemCommand
    : IRekallAgeCommand<ImportKsaSolarSystemRequest, ImportKsaSolarSystemResult>
{
    private const double AstronomicalUnitKm = 149_597_870.7;
    private const double DefaultVisualRotationTimeScale = 86_400;
    private const double DefaultVisualOrbitTimeScale = 2_592_000;
    private const string SolDisplayColor = "#ffb347";
    private readonly RekallAgeAssetCatalogStore _assetStore = new();
    private readonly RekallAgeSceneStore _sceneStore = new();

    public string Name => "rekall.solar.import_ksa_system";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Imports Kitten Space Agency solar-system XML into generic Rekall celestial body and Kepler orbit entities.",
        typeof(ImportKsaSolarSystemRequest).FullName!,
        typeof(ImportKsaSolarSystemResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ImportKsaSolarSystemResult>> ExecuteAsync(
        ImportKsaSolarSystemRequest request,
        RekallAgeCommandContext context)
    {
        var ksaContentRoot = Path.Combine(request.KsaRoot, "Content", "Core");
        var astronomicalsPath = Path.Combine(ksaContentRoot, "Astronomicals.xml");
        var systemPath = Path.Combine(ksaContentRoot, request.SystemFileName);
        if (!File.Exists(astronomicalsPath))
        {
            return Failure(request, "REKALL_KSA_CONTENT_MISSING", "KSA Content/Core/Astronomicals.xml was not found.", request.KsaRoot);
        }

        if (!File.Exists(systemPath))
        {
            return Failure(request, "REKALL_KSA_SYSTEM_MISSING", $"KSA system file '{request.SystemFileName}' was not found.", systemPath);
        }

        var library = LoadBodyLibrary(astronomicalsPath);
        var bodies = LoadSystemBodies(systemPath, library);
        if (bodies.Count == 0)
        {
            return Failure(request, "REKALL_KSA_SYSTEM_EMPTY", $"KSA system file '{request.SystemFileName}' did not contain importable bodies.", systemPath);
        }

        var catalog = await _assetStore.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var imported = new List<RekallAgeAssetDocument>();
        var diffuseAssetIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.ImportDiffuseTextures)
        {
            foreach (var body in bodies)
            {
                var sourcePath = Path.Combine(ksaContentRoot, "Textures", $"{body.Id}_Diffuse.ktx2");
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                var asset = await RekallAgeAssetImporter.ImportAsync(
                    request.ProjectRoot,
                    sourcePath,
                    "texture",
                    $"{body.Id} Diffuse",
                    context.CancellationToken);
                imported.Add(asset);
                catalog = catalog.AddOrReplace(asset);
                diffuseAssetIds[body.Id] = asset.Id;
            }

            await _assetStore.SaveAsync(request.ProjectRoot, catalog, context.CancellationToken);
            context.Transaction.RecordChangedResource(_assetStore.GetCatalogPath(request.ProjectRoot));
            foreach (var asset in imported)
            {
                context.Transaction.RecordChangedResource(asset.ImportedPath);
            }
        }

        var entityIdByBodyId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bodyById = bodies.ToDictionary(body => body.Id, StringComparer.OrdinalIgnoreCase);
        var entities = bodies.Select(body =>
        {
            var entity = CreateEntity(body, bodyById, diffuseAssetIds, request.DistanceScale, request.RadiusScale);
            entityIdByBodyId[body.Id] = entity.Id;
            return entity;
        }).ToArray();
        entities = entities
            .Select(entity =>
            {
                var celestial = entity.Components.Single(component => component.Type == "Rekall.CelestialBody");
                var parentBodyId = ReadString(celestial.Properties, "parentBodyId");
                return !string.IsNullOrWhiteSpace(parentBodyId) && entityIdByBodyId.TryGetValue(parentBodyId, out var parentEntityId)
                    ? entity with { ParentId = parentEntityId }
                    : entity;
            })
            .ToArray();

        var scene = RekallAgeSceneDocument.Create(
            request.SceneName,
            ["world", "rendering3d", "celestial", "orbital"])
            with
            {
                Entities = entities
                    .Concat(CreateSceneSupportEntities(request.DistanceScale))
                    .OrderBy(entity => entity.Name, StringComparer.Ordinal)
                    .ThenBy(entity => entity.Id, StringComparer.Ordinal)
                    .ToArray()
            };
        await _sceneStore.SaveAsync(request.ProjectRoot, scene, context.CancellationToken);
        context.Transaction.RecordChangedResource(_sceneStore.GetScenePath(request.ProjectRoot, scene.Name));

        return RekallAgeCommandResult<ImportKsaSolarSystemResult>.Success(
            new ImportKsaSolarSystemResult(
                bodies.Count,
                imported.Count,
                bodies.Select(body => body.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                scene),
            $"Imported KSA solar system '{request.SystemFileName}' with {bodies.Count} celestial body entity/entities.");
    }

    private static RekallAgeEntityDocument CreateEntity(
        KsaCelestialBody body,
        IReadOnlyDictionary<string, KsaCelestialBody> bodyById,
        IReadOnlyDictionary<string, string> diffuseAssetIds,
        double distanceScale,
        double radiusScale)
    {
        var displayColor = ResolveDisplayColor(body);
        var celestial = new JsonObject
        {
            ["bodyId"] = body.Id,
            ["type"] = body.Type,
            ["meanRadiusKm"] = body.MeanRadiusKm,
            ["radiusScale"] = radiusScale,
            ["renderRadius"] = Math.Max(0.0001, body.MeanRadiusKm * radiusScale),
            ["massKg"] = body.MassKg,
            ["color"] = displayColor
        };
        if (!string.IsNullOrWhiteSpace(body.ParentId))
        {
            celestial["parentBodyId"] = body.ParentId;
        }

        var transform = new JsonObject();
        if (body.Orbit is not null)
        {
            transform["x"] = body.Orbit.SemiMajorAxisKm * distanceScale;
        }

        var entity = RekallAgeEntityDocument.Create(
                body.Id,
                ["celestial", "ksa", body.Type.ToLowerInvariant(), body.Id.ToLowerInvariant()])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", transform))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.CelestialBody", celestial))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.PlanetRenderer",
                CreatePlanetRendererProperties(body, diffuseAssetIds, radiusScale)));

        if (IsStellarBody(body))
        {
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Material",
                new JsonObject
                {
                    ["baseColor"] = displayColor,
                    ["roughnessFactor"] = 1,
                    ["emissiveColor"] = displayColor,
                    ["emissiveStrength"] = 8
                }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PointLight",
                    new JsonObject
                    {
                        ["intensity"] = 4,
                        ["color"] = displayColor
                    }));
        }

        if (body.Orbit is not null)
        {
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.KeplerOrbit",
                new JsonObject
                {
                    ["parentBodyId"] = body.ParentId,
                    ["semiMajorAxisKm"] = body.Orbit.SemiMajorAxisKm,
                    ["eccentricity"] = body.Orbit.Eccentricity,
                    ["inclinationDegrees"] = body.Orbit.InclinationDegrees,
                    ["longitudeOfAscendingNodeDegrees"] = body.Orbit.LongitudeOfAscendingNodeDegrees,
                    ["argumentOfPeriapsisDegrees"] = body.Orbit.ArgumentOfPeriapsisDegrees,
                    ["timeAtPeriapsisSeconds"] = body.Orbit.TimeAtPeriapsisSeconds,
                    ["timeScale"] = DefaultVisualOrbitTimeScale,
                    ["distanceScale"] = ResolveOrbitDistanceScale(body, bodyById, distanceScale, radiusScale)
                }));
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.OrbitPathRenderer",
                new JsonObject
                {
                    ["segments"] = IsPrimaryOrbit(body, bodyById) ? 192 : 96,
                    ["thickness"] = IsPrimaryOrbit(body, bodyById) ? 8 : 2.5,
                    ["verticalOffset"] = -0.05,
                    ["color"] = IsPrimaryOrbit(body, bodyById) ? "#64b7ff" : "#b7d7ff",
                    ["emissiveStrength"] = IsPrimaryOrbit(body, bodyById) ? 4 : 2.5,
                    ["active"] = true
                }));
        }

        if (body.Rotation is not null)
        {
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.CelestialRotation",
                CreateCelestialRotationProperties(body.Rotation)));
        }

        if (body.HasAtmosphere)
        {
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AtmosphereRenderer",
                new JsonObject
                {
                    ["height"] = Math.Max(0.01, body.MeanRadiusKm * radiusScale * 0.08),
                    ["density"] = 1,
                    ["rayleighColor"] = "#7fb6ff"
                }));
        }

        return entity;
    }

    private static JsonObject CreatePlanetRendererProperties(
        KsaCelestialBody body,
        IReadOnlyDictionary<string, string> diffuseAssetIds,
        double radiusScale)
    {
        var properties = new JsonObject
        {
            ["radius"] = Math.Max(0.0001, body.MeanRadiusKm * radiusScale),
            ["color"] = ResolveDisplayColor(body)
        };
        if (diffuseAssetIds.TryGetValue(body.Id, out var textureId))
        {
            properties["surfaceTexture"] = textureId;
        }

        return properties;
    }

    private static bool IsStellarBody(KsaCelestialBody body)
    {
        return body.Type.Contains("stellar", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrimaryOrbit(KsaCelestialBody body, IReadOnlyDictionary<string, KsaCelestialBody> bodyById)
    {
        return string.IsNullOrWhiteSpace(body.ParentId)
            || !bodyById.TryGetValue(body.ParentId, out var parent)
            || IsStellarBody(parent);
    }

    private static double ResolveOrbitDistanceScale(
        KsaCelestialBody body,
        IReadOnlyDictionary<string, KsaCelestialBody> bodyById,
        double distanceScale,
        double radiusScale)
    {
        if (body.Orbit is null
            || string.IsNullOrWhiteSpace(body.ParentId)
            || !bodyById.TryGetValue(body.ParentId, out var parent)
            || IsStellarBody(parent))
        {
            return distanceScale;
        }

        var parentRadius = Math.Max(0.0001, parent.MeanRadiusKm * radiusScale);
        var childRadius = Math.Max(0.0001, body.MeanRadiusKm * radiusScale);
        var minimumReadableOrbit = Math.Max(
            parentRadius + childRadius + 0.75,
            parentRadius * 1.8);
        return Math.Max(distanceScale, minimumReadableOrbit / Math.Max(1, body.Orbit.SemiMajorAxisKm));
    }

    private static string ResolveDisplayColor(KsaCelestialBody body)
    {
        return IsStellarBody(body) && body.Id.Equals("Sol", StringComparison.OrdinalIgnoreCase)
            ? SolDisplayColor
            : body.Color;
    }

    private static JsonObject CreateCelestialRotationProperties(KsaRotation rotation)
    {
        var properties = new JsonObject
        {
            ["siderealPeriodSeconds"] = rotation.SiderealPeriodSeconds,
            ["tidallyLocked"] = rotation.TidallyLocked,
            ["tiltDegrees"] = rotation.TiltDegrees,
            ["azimuthDegrees"] = rotation.AzimuthDegrees,
            ["initialLongitudeDegrees"] = rotation.InitialLongitudeDegrees,
            ["timeScale"] = DefaultVisualRotationTimeScale,
            ["active"] = true
        };
        return properties;
    }

    private static IReadOnlyList<RekallAgeEntityDocument> CreateSceneSupportEntities(double distanceScale)
    {
        var cameraDistance = Math.Clamp(340_000_000 * Math.Max(0.00000001, distanceScale), 80, 5000);
        return
        [
            RekallAgeEntityDocument.Create("Main Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["z"] = cameraDistance, ["y"] = cameraDistance * 0.2, ["pitch"] = -12 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Camera3D",
                    new JsonObject { ["active"] = true, ["farClip"] = cameraDistance * 20, ["clearColor"] = "#000000" }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CameraZoomInput",
                    new JsonObject
                    {
                        ["active"] = true,
                        ["wheelZoomSpeed"] = 0.12,
                        ["minimumOrthographicSize"] = 1,
                        ["maximumOrthographicSize"] = 10000,
                        ["minimumFieldOfView"] = 15,
                        ["maximumFieldOfView"] = 120
                    })),
            RekallAgeEntityDocument.Create("Solar Key Light", ["light"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["pitch"] = -10, ["yaw"] = -20 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.DirectionalLight",
                    new JsonObject { ["intensity"] = 2.0 }))
        ];
    }

    private static IReadOnlyDictionary<string, KsaCelestialBody> LoadBodyLibrary(string path)
    {
        var document = new XmlDocument();
        document.Load(path);
        return document.DocumentElement?.ChildNodes
            .OfType<XmlNode>()
            .Where(IsBodyNode)
            .Select(ParseBody)
            .ToDictionary(body => body.Id, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, KsaCelestialBody>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<KsaCelestialBody> LoadSystemBodies(
        string path,
        IReadOnlyDictionary<string, KsaCelestialBody> library)
    {
        var document = new XmlDocument();
        document.Load(path);
        var bodies = new List<KsaCelestialBody>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in document.DocumentElement?.ChildNodes.OfType<XmlNode>() ?? [])
        {
            KsaCelestialBody? body = null;
            if (node.Name == "LoadFromLibrary")
            {
                var id = node.Attributes?["Id"]?.Value;
                if (!string.IsNullOrWhiteSpace(id) && library.TryGetValue(id, out var libraryBody))
                {
                    var parent = node.Attributes?["Parent"]?.Value;
                    body = string.IsNullOrWhiteSpace(parent)
                        ? libraryBody
                        : libraryBody with { ParentId = parent.Trim() };
                }
            }
            else if (IsBodyNode(node))
            {
                body = ParseBody(node);
            }

            if (body is null || !seen.Add(body.Id))
            {
                continue;
            }

            bodies.Add(body);
        }

        return bodies;
    }

    private static bool IsBodyNode(XmlNode node)
    {
        return node.Name is "StellarBody" or "PlanetaryBody" or "AtmosphericBody" or "MinorBody" or "PeriodicComet" or "InterstellarComet";
    }

    private static KsaCelestialBody ParseBody(XmlNode node)
    {
        var id = node.Attributes?["Id"]?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"KSA body node '{node.Name}' did not have an Id attribute.");
        }

        return new KsaCelestialBody(
            id,
            node.Name,
            node.Attributes?["Parent"]?.Value?.Trim(),
            ReadDistanceKm(node.SelectSingleNode("./MeanRadius"), 1),
            ReadMassKg(node.SelectSingleNode("./Mass")),
            node.Name == "AtmosphericBody" || node.SelectSingleNode("./Atmosphere") is not null,
            ReadColor(node.SelectSingleNode("./Color")),
            ParseOrbit(node.SelectSingleNode("./Orbit")),
            ParseRotation(node.SelectSingleNode("./Rotation")));
    }

    private static KsaOrbit? ParseOrbit(XmlNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return new KsaOrbit(
            Math.Max(0, ReadDistanceKm(node.SelectSingleNode("./SemiMajorAxis"), 0)),
            Math.Clamp(ReadDouble(node.SelectSingleNode("./Eccentricity")?.Attributes?["Value"]?.Value, 0), 0, 0.999999),
            ReadDouble(node.SelectSingleNode("./Inclination")?.Attributes?["Degrees"]?.Value, 0),
            ReadDouble(node.SelectSingleNode("./LongitudeOfAscendingNode")?.Attributes?["Degrees"]?.Value, 0),
            ReadDouble(node.SelectSingleNode("./ArgumentOfPeriapsis")?.Attributes?["Degrees"]?.Value, 0),
            ReadDurationSeconds(node.SelectSingleNode("./TimeAtPeriapsis")));
    }

    private static KsaRotation? ParseRotation(XmlNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var siderealPeriodSeconds = ReadDurationSeconds(node.SelectSingleNode("./SiderealPeriod"));
        var tidallyLocked = ReadBoolean(node.SelectSingleNode("./IsTidallyLocked")?.Attributes?["Value"]?.Value, false);
        if (siderealPeriodSeconds <= 0 && !tidallyLocked)
        {
            return null;
        }

        return new KsaRotation(
            siderealPeriodSeconds,
            tidallyLocked,
            ReadDouble(node.SelectSingleNode("./Tilt")?.Attributes?["Degrees"]?.Value, 0),
            ReadDouble(node.SelectSingleNode("./Azimuth")?.Attributes?["Degrees"]?.Value, 0),
            ReadDouble(node.SelectSingleNode("./InitialParentFacingLongitude")?.Attributes?["Degrees"]?.Value, 0));
    }

    private static double ReadDistanceKm(XmlNode? node, double fallback)
    {
        if (node is null)
        {
            return fallback;
        }

        if (TryReadAttribute(node, "Km", out var km))
        {
            return km;
        }

        return TryReadAttribute(node, "Au", out var au) ? au * AstronomicalUnitKm : fallback;
    }

    private static double ReadMassKg(XmlNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        if (TryReadAttribute(node, "Kg", out var kg))
        {
            return kg;
        }

        if (TryReadAttribute(node, "Suns", out var suns))
        {
            return suns * 1.98847e30;
        }

        if (TryReadAttribute(node, "Earths", out var earths))
        {
            return earths * 5.9722e24;
        }

        if (TryReadAttribute(node, "Lunars", out var lunars))
        {
            return lunars * 7.342e22;
        }

        if (TryReadAttribute(node, "Jupiters", out var jupiters))
        {
            return jupiters * 1.89813e27;
        }

        if (TryReadAttribute(node, "Yg", out var yg))
        {
            return yg * 1e21;
        }

        if (TryReadAttribute(node, "Zg", out var zg))
        {
            return zg * 1e18;
        }

        if (TryReadAttribute(node, "Eg", out var eg))
        {
            return eg * 1e15;
        }

        if (TryReadAttribute(node, "Pg", out var pg))
        {
            return pg * 1e12;
        }

        return TryReadAttribute(node, "Tg", out var tg) ? tg * 1e9 : 0;
    }

    private static double ReadDurationSeconds(XmlNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        var seconds = 0.0;
        if (TryReadAttribute(node, "Seconds", out var secondsValue))
        {
            seconds += secondsValue;
        }

        if (TryReadAttribute(node, "Minutes", out var minutes))
        {
            seconds += minutes * 60;
        }

        if (TryReadAttribute(node, "Hours", out var hours))
        {
            seconds += hours * 3600;
        }

        if (TryReadAttribute(node, "Days", out var days))
        {
            seconds += days * 86400;
        }

        return seconds;
    }

    private static bool TryReadAttribute(XmlNode node, string name, out double value)
    {
        return double.TryParse(
            node.Attributes?[name]?.Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static double ReadDouble(string? value, double fallback)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return properties.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static string ReadColor(XmlNode? node)
    {
        if (node is null)
        {
            return "#8f98a8";
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

    private static RekallAgeCommandResult<ImportKsaSolarSystemResult> Failure(
        ImportKsaSolarSystemRequest request,
        string code,
        string message,
        string target)
    {
        var error = new RekallAgeCommandError(code, message, target);
        return RekallAgeCommandResult<ImportKsaSolarSystemResult>.Failure(
            new ImportKsaSolarSystemResult(0, 0, [], RekallAgeSceneDocument.Create(request.SceneName, [])),
            message,
            [error]);
    }

    private sealed record KsaCelestialBody(
        string Id,
        string Type,
        string? ParentId,
        double MeanRadiusKm,
        double MassKg,
        bool HasAtmosphere,
        string Color,
        KsaOrbit? Orbit,
        KsaRotation? Rotation);

    private sealed record KsaOrbit(
        double SemiMajorAxisKm,
        double Eccentricity,
        double InclinationDegrees,
        double LongitudeOfAscendingNodeDegrees,
        double ArgumentOfPeriapsisDegrees,
        double TimeAtPeriapsisSeconds);

    private sealed record KsaRotation(
        double SiderealPeriodSeconds,
        bool TidallyLocked,
        double TiltDegrees,
        double AzimuthDegrees,
        double InitialLongitudeDegrees);
}
