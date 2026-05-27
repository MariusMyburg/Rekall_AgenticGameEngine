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
        var assetIdsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.ImportDiffuseTextures)
        {
            foreach (var texture in bodies.SelectMany(body => body.GetTextureImports()).DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                var sourcePath = Path.Combine(ksaContentRoot, NormalizeKsaRelativePath(texture.Path));
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                var asset = await RekallAgeAssetImporter.ImportAsync(
                    request.ProjectRoot,
                    sourcePath,
                    "texture",
                    texture.DisplayName,
                    context.CancellationToken);
                imported.Add(asset);
                catalog = catalog.AddOrReplace(asset);
                assetIdsByPath[NormalizeKsaRelativePath(texture.Path)] = asset.Id;
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
            var entity = CreateEntity(body, bodyById, assetIdsByPath, request.DistanceScale, request.RadiusScale);
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
                    .Concat(CreateSceneSupportEntities(bodies, request.DistanceScale, request.RadiusScale))
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
        IReadOnlyDictionary<string, string> assetIdsByPath,
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
                CreatePlanetRendererProperties(body, assetIdsByPath, radiusScale)))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MarkerRenderer",
                CreateMarkerRendererProperties(body, bodyById, radiusScale)));
        if (IsPrimaryOrbit(body, bodyById))
        {
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.TextLabelRenderer",
                CreateTextLabelRendererProperties(body, radiusScale)));
        }

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
                    "Rekall.HaloRenderer",
                    CreateHaloRendererProperties(body, radiusScale)))
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
                    ["thickness"] = IsPrimaryOrbit(body, bodyById) ? 1.4 : 0.6,
                    ["verticalOffset"] = -0.05,
                    ["color"] = IsPrimaryOrbit(body, bodyById) ? "#64b7ff66" : "#b7d7ff55",
                    ["emissiveStrength"] = IsPrimaryOrbit(body, bodyById) ? 1.8 : 1.1,
                    ["layer"] = "orbit-guides",
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
                CreateAtmosphereProperties(body, radiusScale)));
        }

        if (body.CloudLayers.Count > 0)
        {
            var cloudLayers = new JsonArray();
            for (var cloudLayerIndex = 0; cloudLayerIndex < body.CloudLayers.Count; cloudLayerIndex++)
            {
                var cloudLayer = body.CloudLayers[cloudLayerIndex];
                if (TryResolveAssetId(assetIdsByPath, cloudLayer.TexturePath, out var cloudTextureId))
                {
                    var cloudHeight = Math.Max(
                        0.01 + (cloudLayerIndex * 0.002),
                        body.MeanRadiusKm * radiusScale * (0.0025 + (cloudLayerIndex * 0.00075)));
                    cloudLayers.Add(new JsonObject
                    {
                        ["height"] = cloudHeight,
                        ["texture"] = cloudTextureId,
                        ["color"] = cloudLayer.Color,
                        ["alphaFromTextureOnly"] = cloudLayer.AlphaFromTextureOnly,
                        ["coverage"] = cloudLayerIndex == 0 ? 0.32 : 0.22,
                        ["lambertianStrength"] = Math.Min(cloudLayer.LambertianStrength, cloudLayerIndex == 0 ? 0.72 : 0.68),
                        ["ambientStrength"] = cloudLayerIndex == 0 ? 0.035 : 0.045,
                        ["castShadows"] = cloudLayerIndex == body.CloudLayers.Count - 1,
                        ["shadowStrength"] = cloudLayerIndex == body.CloudLayers.Count - 1 ? 0.12 : 0
                    });
                }
            }

            if (cloudLayers.Count > 0)
            {
                entity = entity.AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CloudLayerRenderer",
                    new JsonObject
                    {
                        ["layers"] = cloudLayers
                    }));
            }
        }

        if (body.Ring is not null
            && TryResolveAssetId(assetIdsByPath, body.Ring.TexturePath, out var ringTextureId))
        {
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.RingRenderer",
                new JsonObject
                {
                    ["innerRadius"] = Math.Max(0.0001, body.Ring.InnerRadiusKm * radiusScale),
                    ["outerRadius"] = Math.Max(body.Ring.InnerRadiusKm * radiusScale + 0.0001, body.Ring.OuterRadiusKm * radiusScale),
                    ["texture"] = ringTextureId,
                    ["color"] = "#fff2d7cc",
                    ["segments"] = 256
                }));
        }

        return entity;
    }

    private static JsonObject CreatePlanetRendererProperties(
        KsaCelestialBody body,
        IReadOnlyDictionary<string, string> assetIdsByPath,
        double radiusScale)
    {
        var properties = new JsonObject
        {
            ["radius"] = Math.Max(0.0001, body.MeanRadiusKm * radiusScale),
            ["color"] = ResolveDisplayColor(body),
            ["meshSlices"] = 96,
            ["meshStacks"] = 48
        };
        if (TryResolveAssetId(assetIdsByPath, body.DiffuseTexturePath, out var textureId))
        {
            properties["surfaceTexture"] = textureId;
        }

        if (TryResolveAssetId(assetIdsByPath, body.NormalTexturePath, out var normalTextureId))
        {
            properties["normalTexture"] = normalTextureId;
        }

        if (TryResolveAssetId(assetIdsByPath, body.OceanColorTexturePath, out var waterTextureId))
        {
            properties["waterTexture"] = waterTextureId;
            properties["waterCoverage"] = 1.55;
            properties["waterSpecularStrength"] = 3.4;
            properties["waterRoughness"] = 0.055;
        }

        return properties;
    }

    private static JsonObject CreateMarkerRendererProperties(
        KsaCelestialBody body,
        IReadOnlyDictionary<string, KsaCelestialBody> bodyById,
        double radiusScale)
    {
        var renderRadius = Math.Max(0.0001, body.MeanRadiusKm * radiusScale);
        var isPrimary = IsPrimaryOrbit(body, bodyById);
        return new JsonObject
        {
            ["size"] = Math.Max(renderRadius * 1.8, isPrimary ? 80 : 42),
            ["color"] = ResolveDisplayColor(body),
            ["emissiveStrength"] = IsStellarBody(body) ? 4.5 : isPrimary ? 2.8 : 2.1,
            ["verticalOffset"] = 0.04,
            ["layer"] = "overview-markers",
            ["active"] = true
        };
    }

    private static JsonObject CreateTextLabelRendererProperties(KsaCelestialBody body, double radiusScale)
    {
        var renderRadius = Math.Max(0.0001, body.MeanRadiusKm * radiusScale);
        var labelSize = Math.Clamp(renderRadius * 0.62, IsStellarBody(body) ? 56 : 48, IsStellarBody(body) ? 72 : 62);
        return new JsonObject
        {
            ["text"] = body.Id,
            ["size"] = labelSize,
            ["minimumScreenHeightPixels"] = IsStellarBody(body) ? 0 : 2,
            ["thickness"] = Math.Max(0.25, labelSize * 0.022),
            ["color"] = IsStellarBody(body) ? "#ffe9aacc" : "#dce8ffff",
            ["offsetX"] = labelSize * 0.72,
            ["offsetY"] = 0.08,
            ["offsetZ"] = -labelSize * 0.55,
            ["facingMode"] = "camera-plane",
            ["layer"] = "overview-labels",
            ["active"] = true
        };
    }

    private static JsonObject CreateHaloRendererProperties(KsaCelestialBody body, double radiusScale)
    {
        var renderRadius = Math.Max(0.0001, body.MeanRadiusKm * radiusScale);
        return new JsonObject
        {
            ["radius"] = Math.Max(renderRadius * 2.8, 180),
            ["segments"] = 96,
            ["rings"] = 6,
            ["falloff"] = 2.4,
            ["color"] = $"{ResolveDisplayColor(body)}cc",
            ["intensity"] = 5.5,
            ["verticalOffset"] = 0.02,
            ["facingMode"] = "camera-plane",
            ["layer"] = "stellar-glow",
            ["active"] = true
        };
    }

    private static JsonObject CreateAtmosphereProperties(KsaCelestialBody body, double radiusScale)
    {
        var atmosphere = body.Atmosphere;
        return new JsonObject
        {
            ["height"] = Math.Max(0.01, body.MeanRadiusKm * radiusScale * 0.08),
            ["density"] = 1,
            ["densityFalloff"] = atmosphere is null
                ? 0.28
                : Math.Clamp(atmosphere.RayleighScaleHeightKm / Math.Max(body.MeanRadiusKm * 0.08, 0.0001), 0.05, 0.8),
            ["rayleighColor"] = atmosphere is null ? "#7fb6ff" : ColorFromCoefficients(atmosphere.Rayleigh),
            ["mieColor"] = atmosphere is null ? "#fff7e8" : ColorFromCoefficients(atmosphere.Mie),
            ["rayleighScattering"] = atmosphere is null ? 0.025 : Math.Max(0.001, MaxCoefficient(atmosphere.Rayleigh)),
            ["mieScattering"] = atmosphere is null ? 0.006 : Math.Max(0.001, MaxCoefficient(atmosphere.Mie)),
            ["mieAnisotropy"] = atmosphere is null ? 0.76 : Math.Clamp(atmosphere.MieAnisotropy, -0.99, 0.99),
            ["ozoneAbsorptionColor"] = atmosphere is null ? "#ffd199" : ColorFromCoefficients(atmosphere.Ozone),
            ["ozoneAbsorption"] = atmosphere is null ? 0 : Math.Max(0, MaxCoefficient(atmosphere.Ozone)),
            ["aerialPerspectiveStrength"] = 0.38,
            ["sunIntensity"] = 34,
            ["exposure"] = 2.4,
            ["viewSampleCount"] = 16,
            ["lightSampleCount"] = 8
        };
    }

    private static bool TryResolveAssetId(
        IReadOnlyDictionary<string, string> assetIdsByPath,
        string? path,
        out string assetId)
    {
        if (!string.IsNullOrWhiteSpace(path)
            && assetIdsByPath.TryGetValue(NormalizeKsaRelativePath(path), out var resolved))
        {
            assetId = resolved;
            return true;
        }

        assetId = string.Empty;
        return false;
    }

    private static string NormalizeKsaRelativePath(string path)
    {
        var normalized = path.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return normalized.StartsWith($"Textures{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : Path.Combine("Textures", normalized);
    }

    private static double MaxCoefficient(KsaColorCoefficients coefficients)
    {
        return Math.Max(coefficients.R, Math.Max(coefficients.G, coefficients.B));
    }

    private static string ColorFromCoefficients(KsaColorCoefficients coefficients)
    {
        var max = MaxCoefficient(coefficients);
        if (max <= 0)
        {
            return "#ffffff";
        }

        var r = (int)Math.Round(Math.Clamp(coefficients.R / max, 0, 1) * 255);
        var g = (int)Math.Round(Math.Clamp(coefficients.G / max, 0, 1) * 255);
        var b = (int)Math.Round(Math.Clamp(coefficients.B / max, 0, 1) * 255);
        return $"#{r:x2}{g:x2}{b:x2}";
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

    private static IReadOnlyList<RekallAgeEntityDocument> CreateSceneSupportEntities(
        IReadOnlyList<KsaCelestialBody> bodies,
        double distanceScale,
        double radiusScale)
    {
        var furthestOrbit = bodies
            .Select(body => body.Orbit?.SemiMajorAxisKm ?? 0)
            .DefaultIfEmpty(340_000_000)
            .Max();
        var cameraDistance = Math.Clamp(furthestOrbit * Math.Max(0.00000001, distanceScale) * 1.35, 80, 20000);
        var overviewOrthographicSize = Math.Max(120, cameraDistance * 1.25);
        var targetBody = bodies.FirstOrDefault(body => body.Type.Contains("Stellar", StringComparison.OrdinalIgnoreCase))
            ?? bodies.FirstOrDefault();
        var targetName = targetBody?.Id ?? "Sol";
        return
        [
            RekallAgeEntityDocument.Create("Main Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["z"] = 0, ["y"] = 0, ["pitch"] = 0, ["yaw"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Camera3D",
                    new JsonObject
                    {
                        ["active"] = true,
                        ["projectionMode"] = "orthographic",
                        ["orthographicSize"] = overviewOrthographicSize,
                        ["fieldOfView"] = 52,
                        ["farClip"] = cameraDistance * 20,
                        ["clearColor"] = "#000000"
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CameraTarget3D",
                    new JsonObject
                    {
                        ["targetName"] = targetName,
                        ["offsetX"] = 0,
                        ["offsetY"] = cameraDistance,
                        ["offsetZ"] = cameraDistance * 0.08,
                        ["targetOffsetX"] = 0,
                        ["targetOffsetY"] = 0,
                        ["targetOffsetZ"] = 0,
                        ["followPosition"] = true,
                        ["lookAt"] = true,
                        ["active"] = true
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CameraTargetCycleInput",
                    new JsonObject
                    {
                        ["active"] = true,
                        ["nextAction"] = "nextTarget",
                        ["previousAction"] = "previousTarget",
                        ["currentIndex"] = 0,
                        ["targets"] = CreateTourTargets(bodies, cameraDistance, overviewOrthographicSize, radiusScale)
                    }))
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
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.InputActionMap",
                    new JsonObject
                    {
                        ["actions"] = new JsonArray
                        {
                            new JsonObject { ["name"] = "nextTarget", ["key"] = "E" },
                            new JsonObject { ["name"] = "nextTarget", ["key"] = "Tab" },
                            new JsonObject { ["name"] = "previousTarget", ["key"] = "Q" }
                        }
                    })),
            RekallAgeEntityDocument.Create("Solar Key Light", ["light"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["pitch"] = -10, ["yaw"] = -20 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.DirectionalLight",
                    new JsonObject { ["intensity"] = 2.0 })),
            RekallAgeEntityDocument.Create("Deep Space Starfield", ["space", "backdrop"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.StarfieldRenderer",
                    new JsonObject
                    {
                        ["count"] = 1800,
                        ["radius"] = Math.Max(1000, cameraDistance * 0.82),
                        ["size"] = Math.Max(0.8, cameraDistance * 0.00022),
                        ["seed"] = 424242,
                        ["color"] = "#dce8ffff",
                        ["brightness"] = 3.2,
                        ["milkyWayStrength"] = 0.42,
                        ["active"] = true
                    })),
            RekallAgeEntityDocument.Create("Photographic Grade", ["rendering", "post-process"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PostProcessStack",
                    CreatePostProcessStackProperties()))
        ];
    }

    private static JsonObject CreatePostProcessStackProperties()
    {
        return new JsonObject
        {
            ["enabled"] = true,
            ["passes"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "sun-threshold",
                    ["type"] = "brightExtract",
                    ["input"] = "sceneColor",
                    ["output"] = "brightColor",
                    ["threshold"] = 0.86,
                    ["scale"] = 1.0
                },
                new JsonObject
                {
                    ["name"] = "soft-composite",
                    ["type"] = "composite",
                    ["input"] = "sceneColor",
                    ["source"] = "brightColor",
                    ["output"] = "swapchain",
                    ["intensity"] = 0.42,
                    ["blendMode"] = "add"
                }
            }
        };
    }

    private static JsonArray CreateTourTargets(
        IReadOnlyList<KsaCelestialBody> bodies,
        double cameraDistance,
        double overviewOrthographicSize,
        double radiusScale)
    {
        var targets = new JsonArray
        {
            new JsonObject
            {
                ["targetName"] = bodies.FirstOrDefault(body => body.Type.Contains("Stellar", StringComparison.OrdinalIgnoreCase))?.Id
                    ?? bodies.FirstOrDefault()?.Id
                    ?? "Sol",
                ["offsetX"] = 0,
                ["offsetY"] = cameraDistance,
                ["offsetZ"] = cameraDistance * 0.08,
                ["projectionMode"] = "orthographic",
                ["orthographicSize"] = overviewOrthographicSize,
                ["fieldOfView"] = 52,
                ["cullingMask"] = "*"
            }
        };

        foreach (var body in bodies.OrderBy(GetTourOrder).ThenBy(body => body.Id, StringComparer.OrdinalIgnoreCase))
        {
            var bodyRadius = Math.Max(0.01, body.MeanRadiusKm * radiusScale);
            var outerRadius = body.Ring is null
                ? bodyRadius
                : Math.Max(bodyRadius, body.Ring.OuterRadiusKm * radiusScale);
            var offsetZ = Math.Clamp(outerRadius * 4.2, 1.2, 500);
            targets.Add(new JsonObject
            {
                ["targetName"] = body.Id,
                ["offsetX"] = 0,
                ["offsetY"] = 0,
                ["offsetZ"] = body.Type.Contains("Stellar", StringComparison.OrdinalIgnoreCase) ? offsetZ : 0,
                ["offsetReferenceName"] = body.Type.Contains("Stellar", StringComparison.OrdinalIgnoreCase) ? string.Empty : "Sol",
                ["offsetReferenceMode"] = "toward",
                ["offsetDistance"] = body.Type.Contains("Stellar", StringComparison.OrdinalIgnoreCase) ? 0 : offsetZ,
                ["offsetVertical"] = Math.Clamp(outerRadius * 0.22, 0.08, 24),
                ["offsetLateral"] = body.Type.Contains("Stellar", StringComparison.OrdinalIgnoreCase) ? 0 : offsetZ * 0.24,
                ["targetOffsetX"] = 0,
                ["targetOffsetY"] = 0,
                ["targetOffsetZ"] = 0,
                ["projectionMode"] = "perspective",
                ["fieldOfView"] = body.Type.Contains("Stellar", StringComparison.OrdinalIgnoreCase) ? 45 : 38,
                ["cullingMask"] = "default"
            });
        }

        return targets;
    }

    private static double GetTourOrder(KsaCelestialBody body)
    {
        if (body.Type.Contains("Stellar", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        return body.Orbit?.SemiMajorAxisKm ?? double.MaxValue;
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
            ReadBodyColor(node.SelectSingleNode("./Color")),
            ParseOrbit(node.SelectSingleNode("./Orbit")),
            ParseRotation(node.SelectSingleNode("./Rotation")),
            ReadTexturePath(node.SelectSingleNode("./Diffuse")) ?? $"Textures/{id}_Diffuse.ktx2",
            ReadTexturePath(node.SelectSingleNode("./Normal")) ?? $"Textures/{id}_Normal.ktx2",
            ReadTexturePath(node.SelectSingleNode("./Ocean/ColorTexture")),
            ParseCloudLayers(node.SelectNodes("./Clouds/Layer")),
            ParseRing(node.SelectSingleNode("./Rings")),
            ParseAtmosphere(node.SelectSingleNode("./Atmosphere")));
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

    private static string? ReadTexturePath(XmlNode? node)
    {
        return node?.Attributes?["Path"]?.Value?.Trim();
    }

    private static IReadOnlyList<KsaCloudLayer> ParseCloudLayers(XmlNodeList? layers)
    {
        if (layers is null)
        {
            return [];
        }

        return layers
            .OfType<XmlNode>()
            .Select(layer => new KsaCloudLayer(
                layer.Attributes?["Id"]?.Value?.Trim() ?? "CloudLayer",
                ReadTexturePath(layer.SelectSingleNode("./Texture")),
                ReadCloudColor(layer),
                ReadBoolean(layer.SelectSingleNode("./TwoDimensionalCloud/UseAlphaOnly")?.Attributes?["Value"]?.Value, true),
                Math.Clamp(ReadDouble(layer.SelectSingleNode("./TwoDimensionalCloud/Lambertian")?.Attributes?["Value"]?.Value, 0.45), 0, 1)))
            .Where(layer => !string.IsNullOrWhiteSpace(layer.TexturePath))
            .ToArray();
    }

    private static string ReadCloudColor(XmlNode layer)
    {
        return ReadOptionalColor(layer.SelectSingleNode("./TwoDimensionalCloud/Color"))
            ?? ReadOptionalColor(layer.SelectSingleNode("./Color"))
            ?? "#ffffff";
    }

    private static string? ReadOptionalColor(XmlNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var r = ReadUnitColor(node.Attributes?["R"]?.Value);
        var g = ReadUnitColor(node.Attributes?["G"]?.Value);
        var b = ReadUnitColor(node.Attributes?["B"]?.Value);
        var a = ReadUnitColor(node.Attributes?["A"]?.Value ?? "1");
        return a >= 255 ? $"#{r:x2}{g:x2}{b:x2}" : $"#{r:x2}{g:x2}{b:x2}{a:x2}";
    }

    private static KsaRing? ParseRing(XmlNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var texture = ReadTexturePath(node.SelectSingleNode("./Texture"));
        if (string.IsNullOrWhiteSpace(texture))
        {
            return null;
        }

        return new KsaRing(
            ReadDistanceKm(node.SelectSingleNode("./InnerRadius"), 0),
            ReadDistanceKm(node.SelectSingleNode("./OuterRadius"), 0),
            texture);
    }

    private static KsaAtmosphereVisual? ParseAtmosphere(XmlNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var rayleigh = ReadCoefficients(node.SelectSingleNode("./Visual/RayleighScattering/Coefficients"));
        var mie = ReadCoefficients(node.SelectSingleNode("./Visual/MieScattering/Coefficients"));
        var ozone = ReadCoefficients(node.SelectSingleNode("./Visual/Ozone/Coefficients"));
        if (MaxCoefficient(rayleigh) <= 0 && MaxCoefficient(mie) <= 0 && MaxCoefficient(ozone) <= 0)
        {
            return null;
        }

        return new KsaAtmosphereVisual(
            rayleigh,
            mie,
            ozone,
            ReadDistanceKm(node.SelectSingleNode("./Visual/RayleighScattering/ScaleHeight"), 8),
            ReadDistanceKm(node.SelectSingleNode("./Visual/MieScattering/ScaleHeight"), 1.2),
            ReadDouble(node.SelectSingleNode("./Visual/MieScattering/PhaseFunctionAsymmetry")?.Attributes?["X"]?.Value, 0.76));
    }

    private static KsaColorCoefficients ReadCoefficients(XmlNode? node)
    {
        if (node is null)
        {
            return new KsaColorCoefficients(0, 0, 0);
        }

        return new KsaColorCoefficients(
            ReadDouble(node.Attributes?["R"]?.Value, 0),
            ReadDouble(node.Attributes?["G"]?.Value, 0),
            ReadDouble(node.Attributes?["B"]?.Value, 0));
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

    private static string ReadBodyColor(XmlNode? node)
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
        KsaRotation? Rotation,
        string? DiffuseTexturePath,
        string? NormalTexturePath,
        string? OceanColorTexturePath,
        IReadOnlyList<KsaCloudLayer> CloudLayers,
        KsaRing? Ring,
        KsaAtmosphereVisual? Atmosphere)
    {
        public IEnumerable<KsaTextureImport> GetTextureImports()
        {
            if (!string.IsNullOrWhiteSpace(DiffuseTexturePath))
            {
                yield return new KsaTextureImport(DiffuseTexturePath, $"{Id} Diffuse");
            }

            if (!string.IsNullOrWhiteSpace(NormalTexturePath))
            {
                yield return new KsaTextureImport(NormalTexturePath, $"{Id} Normal");
            }

            if (!string.IsNullOrWhiteSpace(OceanColorTexturePath))
            {
                yield return new KsaTextureImport(OceanColorTexturePath, $"{Id} Ocean Color");
            }

            foreach (var cloud in CloudLayers)
            {
                yield return new KsaTextureImport(cloud.TexturePath!, $"{Id} {cloud.Id}");
            }

            if (Ring is { TexturePath: not null } ring)
            {
                yield return new KsaTextureImport(ring.TexturePath, $"{Id} Rings");
            }
        }
    }

    private sealed record KsaTextureImport(string Path, string DisplayName);

    private sealed record KsaCloudLayer(
        string Id,
        string? TexturePath,
        string Color,
        bool AlphaFromTextureOnly,
        double LambertianStrength);

    private sealed record KsaRing(
        double InnerRadiusKm,
        double OuterRadiusKm,
        string TexturePath);

    private sealed record KsaColorCoefficients(double R, double G, double B);

    private sealed record KsaAtmosphereVisual(
        KsaColorCoefficients Rayleigh,
        KsaColorCoefficients Mie,
        KsaColorCoefficients Ozone,
        double RayleighScaleHeightKm,
        double MieScaleHeightKm,
        double MieAnisotropy);

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
