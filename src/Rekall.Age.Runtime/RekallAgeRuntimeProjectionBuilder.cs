using System.Text.Json.Nodes;
using Rekall.Age.Core.Rendering;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeRuntimeProjectionBuilder
{
    public RekallAgeRuntimeWorld Project(RekallAgeRuntimeWorld world)
    {
        var cameras = PreserveAuthored(world.Subsystems.Rendering.Cameras);
        var sprites = PreserveAuthored(world.Subsystems.Rendering.Sprites);
        var meshes = PreserveAuthored(world.Subsystems.Rendering.Meshes);
        var lights = PreserveAuthored(world.Subsystems.Rendering.Lights);
        var uiLayers = PreserveAuthored(world.Subsystems.Rendering.UiLayers);
        var bodies = new List<RekallAgeRuntimePhysicsBody>();
        var colliders = new List<RekallAgeRuntimePhysicsCollider>();
        var triggers = new List<RekallAgeRuntimePhysicsCollider>();
        var listeners = new List<RekallAgeRuntimeAudioListener>();
        var emitters = new List<RekallAgeRuntimeAudioEmitter>();
        var animationPlayers = new List<RekallAgeRuntimeAnimationPlayer>();
        var canvases = new List<RekallAgeRuntimeUiCanvas>();
        var elements = new List<RekallAgeRuntimeUiElement>();
        var networkSessions = new List<RekallAgeRuntimeNetworkSession>();
        var networkEntities = new List<RekallAgeRuntimeNetworkEntity>();
        var xrRigs = new List<RekallAgeRuntimeXrRig>();
        var xrControllers = new List<RekallAgeRuntimeXrController>();
        var observations = new List<RekallAgeRuntimeObservation>(world.Observations);

        foreach (var entity in world.Entities)
        {
            var hasTransform = entity.Components.Any(component =>
                component.Type is "Rekall.Transform2D" or "Rekall.Transform3D");
            var hasAuthoredLight = entity.Components.Any(component => IsLight(component.Type));
            var isStellarBody = IsStellarBody(entity);
            var stellarColor = ReadStellarColor(entity);
            var renderLayer = ReadRenderLayer(entity);

            foreach (var component in entity.Components)
            {
                if (!entity.Visible && IsVisibilityGatedComponent(component.Type))
                {
                    continue;
                }

                switch (component.Type)
                {
                    case "Rekall.Camera2D":
                    case "Rekall.Camera3D":
                        cameras.Add(new RekallAgeRuntimeRenderCamera(
                            entity.Id,
                            entity.Name,
                            component.Type["Rekall.".Length..],
                            ReadBoolean(component.Properties, "active", true),
                            RekallAgeRuntimeProjectionSources.BuiltIn,
                            NormalizeProjectionMode(
                                ReadString(component.Properties, "projectionMode")
                                    ?? (component.Type == "Rekall.Camera2D" ? "orthographic" : "perspective")),
                            Clamp(ReadNumber(component.Properties, "fieldOfView", 65), 1, 179),
                            Math.Max(0.001, ReadNumber(component.Properties, "orthographicSize", 10)),
                            ReadNumber(component.Properties, "nearClip", component.Type == "Rekall.Camera2D" ? -1000 : 0.05),
                            ReadNumber(component.Properties, "farClip", component.Type == "Rekall.Camera2D" ? 1000 : 1000),
                            ReadString(component.Properties, "clearColor")
                                ?? (component.Type == "Rekall.Camera2D" ? "#102030" : "#101820"),
                            NormalizeStereoMode(ReadString(component.Properties, "stereoMode")),
                            NormalizeStereoRenderMode(ReadString(component.Properties, "stereoRenderMode")),
                            Math.Clamp(ReadNumber(component.Properties, "interpupillaryDistance", 0.064), 0, 1),
                            Math.Max(0.001, ReadNumber(component.Properties, "stereoConvergenceDistance", 10)),
                            NormalizeXrViewConfiguration(ReadString(component.Properties, "xrViewConfiguration")),
                            ReadBoolean(component.Properties, "foveatedRendering", false),
                            RekallAgeRenderLayerMask.NormalizeCullingMask(ReadString(component.Properties, "cullingMask")),
                            ReadNumber(component.Properties, "renderOrder", 0),
                            Math.Clamp(ReadNumber(component.Properties, "viewportX", 0), 0, 1),
                            Math.Clamp(ReadNumber(component.Properties, "viewportY", 0), 0, 1),
                            Math.Clamp(ReadNumber(component.Properties, "viewportWidth", 1), 0.001, 1),
                            Math.Clamp(ReadNumber(component.Properties, "viewportHeight", 1), 0.001, 1)));
                        break;
                    case "Rekall.SpriteRenderer":
                        sprites.Add(new RekallAgeRuntimeRenderSprite(
                            entity.Id,
                            entity.Name,
                            ReadString(component.Properties, "sprite") ?? ReadString(component.Properties, "assetId"),
                            RekallAgeRuntimeProjectionSources.BuiltIn,
                            renderLayer));
                        break;
                    case "Rekall.MeshRenderer":
                    case "Rekall.MeshSet":
                        meshes.Add(new RekallAgeRuntimeRenderMesh(
                            entity.Id,
                            entity.Name,
                            ReadString(component.Properties, "mesh") ?? ReadString(component.Properties, "assetId"),
                            ProjectionSource: RekallAgeRuntimeProjectionSources.BuiltIn,
                            Layer: renderLayer));
                        break;
                    case "Rekall.GeometryPrimitive":
                        if (!HasMeshRenderer(entity))
                        {
                            var primitive = ReadString(component.Properties, "primitive");
                            meshes.Add(new RekallAgeRuntimeRenderMesh(
                                entity.Id,
                                entity.Name,
                                string.IsNullOrWhiteSpace(primitive)
                                    ? "rekall.geometry.cube"
                                    : $"rekall.geometry.{primitive.Trim().ToLowerInvariant()}",
                                ProjectionSource: RekallAgeRuntimeProjectionSources.BuiltIn,
                                Layer: renderLayer));
                        }

                        break;
                    case "Rekall.GeometryMesh":
                        if (!HasMeshRenderer(entity))
                        {
                            meshes.Add(new RekallAgeRuntimeRenderMesh(
                                entity.Id,
                                entity.Name,
                                "rekall.geometry.mesh",
                                ProjectionSource: RekallAgeRuntimeProjectionSources.BuiltIn,
                                Layer: renderLayer));
                        }

                        break;
                    case "Rekall.LineSegments":
                        meshes.Add(new RekallAgeRuntimeRenderMesh(
                            entity.Id,
                            entity.Name,
                            null,
                            Variant: "rekall.geometry.lines",
                            MaterialColor: ReadString(component.Properties, "color"),
                            SortKey: 150,
                            ProjectionSource: RekallAgeRuntimeProjectionSources.BuiltIn,
                            Layer: renderLayer));
                        break;
                    case "Rekall.MultiplayerSession":
                        networkSessions.Add(new RekallAgeRuntimeNetworkSession(
                            entity.Id,
                            entity.Name,
                            NormalizeNetworkMode(ReadString(component.Properties, "role"), "server"),
                            NormalizeNetworkMode(ReadString(component.Properties, "authority"), "server"),
                            ClampInt(ReadInt32(component.Properties, "tickRate", 60), 1, 240),
                            ClampInt(ReadInt32(component.Properties, "snapshotRate", 20), 1, 240),
                            Math.Max(1, ReadInt32(component.Properties, "maxPlayers", 8)),
                            NormalizeNetworkMode(ReadString(component.Properties, "transport"), "loopback"),
                            ReadString(component.Properties, "address") ?? "127.0.0.1",
                            ClampInt(ReadInt32(component.Properties, "port", 7777), 1, 65535),
                            ReadBoolean(component.Properties, "clientPrediction", true),
                            Math.Max(0, ReadInt32(component.Properties, "interpolationDelayMilliseconds", 100))));
                        break;
                    case "Rekall.NetworkIdentity":
                        var networkTransform = entity.Components.FirstOrDefault(candidate =>
                            candidate.Type.Equals("Rekall.NetworkTransform", StringComparison.Ordinal));
                        networkEntities.Add(new RekallAgeRuntimeNetworkEntity(
                            entity.Id,
                            entity.Name,
                            ReadString(component.Properties, "networkId") ?? entity.Id,
                            EmptyToNull(ReadString(component.Properties, "ownerClientId")),
                            NormalizeNetworkMode(ReadString(component.Properties, "authority"), "server"),
                            ReadBooleanOptional(networkTransform?.Properties, "replicatePosition", true),
                            ReadBooleanOptional(networkTransform?.Properties, "replicateRotation", true),
                            ReadBooleanOptional(networkTransform?.Properties, "replicateScale", true),
                            NormalizeNetworkMode(ReadStringOptional(networkTransform?.Properties, "prediction"), "interpolated"),
                            Math.Max(0, ReadInt32Optional(networkTransform?.Properties, "priority", 0))));
                        break;
                    case "Rekall.XrRig":
                        xrRigs.Add(new RekallAgeRuntimeXrRig(
                            entity.Id,
                            entity.Name,
                            NormalizeXrMode(ReadString(component.Properties, "trackingSpace"), "local-floor"),
                            NormalizeXrMode(ReadString(component.Properties, "viewConfiguration"), "primary-stereo"),
                            ReadBoolean(component.Properties, "active", true)));
                        break;
                    case "Rekall.XrController":
                        var hand = NormalizeXrMode(ReadString(component.Properties, "hand"), "unknown");
                        xrControllers.Add(new RekallAgeRuntimeXrController(
                            entity.Id,
                            entity.Name,
                            hand,
                            NormalizeXrMode(ReadString(component.Properties, "poseSource"), $"{hand}-hand"),
                            ReadBoolean(component.Properties, "active", true)));
                        break;
                    case "Rekall.PlanetRenderer":
                        meshes.Add(new RekallAgeRuntimeRenderMesh(
                            entity.Id,
                            entity.Name,
                            "rekall.planet.surface",
                            ProjectionSource: RekallAgeRuntimeProjectionSources.BuiltIn,
                            Layer: renderLayer));
                        break;
                    case "Rekall.OrbitPathRenderer":
                        meshes.Add(new RekallAgeRuntimeRenderMesh(
                            entity.Id,
                            entity.Name,
                            null,
                            Variant: "rekall.orbit.path",
                            MaterialColor: ReadString(component.Properties, "color"),
                            SortKey: 180,
                            ProjectionSource: RekallAgeRuntimeProjectionSources.BuiltIn,
                            Layer: renderLayer));
                        break;
                    case "Rekall.Rigidbody2D":
                    case "Rekall.Rigidbody3D":
                        bodies.Add(new RekallAgeRuntimePhysicsBody(
                            entity.Id,
                            entity.Name,
                            component.Type["Rekall.".Length..]));
                        if (!hasTransform)
                        {
                            observations.Add(CreateObservation(
                                world.FrameIndex,
                                "REKALL_PHYSICS_BODY_NO_TRANSFORM",
                                "warning",
                                "physics",
                                entity,
                                component,
                                "Physics body has no Rekall.Transform2D or Rekall.Transform3D component."));
                        }

                        break;
                    case "Rekall.BoxCollider2D":
                    case "Rekall.CircleCollider2D":
                    case "Rekall.BoxCollider3D":
                    case "Rekall.SphereCollider3D":
                    case "Rekall.CapsuleCollider3D":
                    case "Rekall.MeshCollider":
                        colliders.Add(new RekallAgeRuntimePhysicsCollider(
                            entity.Id,
                            entity.Name,
                            component.Type["Rekall.".Length..]));
                        break;
                    case "Rekall.Trigger":
                        var trigger = new RekallAgeRuntimePhysicsCollider(
                            entity.Id,
                            entity.Name,
                            component.Type["Rekall.".Length..]);
                        colliders.Add(trigger);
                        triggers.Add(trigger);
                        break;
                    case "Rekall.AudioListener":
                        listeners.Add(new RekallAgeRuntimeAudioListener(entity.Id, entity.Name));
                        break;
                    case "Rekall.AudioEmitter":
                        emitters.Add(new RekallAgeRuntimeAudioEmitter(
                            entity.Id,
                            entity.Name,
                            ReadString(component.Properties, "clip") ?? ReadString(component.Properties, "assetId"),
                            ReadString(component.Properties, "bus")));
                        break;
                    case "Rekall.AnimationPlayer":
                    case "Rekall.SpriteAnimator":
                        var clip = ReadString(component.Properties, "clip")
                            ?? ReadString(component.Properties, "clipId")
                            ?? ReadString(component.Properties, "animation")
                            ?? ReadString(component.Properties, "assetId");
                        animationPlayers.Add(new RekallAgeRuntimeAnimationPlayer(
                            entity.Id,
                            entity.Name,
                            component.Type["Rekall.".Length..],
                            clip));
                        if (string.IsNullOrWhiteSpace(clip))
                        {
                            observations.Add(CreateObservation(
                                world.FrameIndex,
                                "REKALL_ANIMATION_MISSING_CLIP",
                                "warning",
                                "animation",
                                entity,
                                component,
                                "Animation player has no clip asset reference."));
                        }

                        break;
                    case "Rekall.TransformAnimation":
                        animationPlayers.Add(new RekallAgeRuntimeAnimationPlayer(
                            entity.Id,
                            entity.Name,
                            component.Type["Rekall.".Length..],
                            null));
                        break;
                    case "Rekall.UiCanvas":
                        var layer = ReadInt32(component.Properties, "layer", 0);
                        canvases.Add(new RekallAgeRuntimeUiCanvas(entity.Id, entity.Name, layer));
                        uiLayers.Add(new RekallAgeRuntimeRenderUiLayer(
                            entity.Id,
                            entity.Name,
                            layer,
                            RekallAgeRuntimeProjectionSources.BuiltIn));
                        break;
                    case "Rekall.UiElement":
                    case "Rekall.Button":
                    case "Rekall.Label":
                    case "Rekall.Panel":
                        elements.Add(new RekallAgeRuntimeUiElement(
                            entity.Id,
                            entity.Name,
                            component.Type["Rekall.".Length..],
                            ReadBoolean(component.Properties, "interactive", component.Type == "Rekall.Button")));
                        break;
                    default:
                        if (IsLight(component.Type))
                        {
                            lights.Add(new RekallAgeRuntimeRenderLight(
                                entity.Id,
                                entity.Name,
                                component.Type["Rekall.".Length..],
                                ReadNumber(component.Properties, "intensity", 1),
                                RekallAgeRuntimeProjectionSources.BuiltIn,
                                ReadString(component.Properties, "color")
                                    ?? ReadString(component.Properties, "lightColor")
                                    ?? stellarColor,
                                renderLayer));
                        }

                        break;
                }
            }

            if (entity.Visible && isStellarBody && !hasAuthoredLight)
            {
                lights.Add(new RekallAgeRuntimeRenderLight(
                    entity.Id,
                    entity.Name,
                    "PointLight",
                    4,
                    RekallAgeRuntimeProjectionSources.BuiltIn,
                    stellarColor,
                    renderLayer));
            }
        }

        if (emitters.Count > 0 && listeners.Count == 0)
        {
            observations.AddRange(emitters.Select(emitter => new RekallAgeRuntimeObservation(
                world.FrameIndex,
                "REKALL_AUDIO_NO_LISTENER",
                "warning",
                "audio",
                emitter.EntityId,
                emitter.EntityName,
                "AudioEmitter",
                "Audio emitters exist but no Rekall.AudioListener is active.",
                Array.Empty<string>())));
        }

        if (elements.Count > 0 && canvases.Count == 0)
        {
            observations.AddRange(elements.Select(element => new RekallAgeRuntimeObservation(
                world.FrameIndex,
                "REKALL_UI_ELEMENT_NO_CANVAS",
                "warning",
                "ui",
                element.EntityId,
                element.EntityName,
                element.Kind,
                "UI elements exist but no Rekall.UiCanvas is active.",
                Array.Empty<string>())));
        }

        return world with
        {
            Subsystems = new RekallAgeRuntimeSubsystemViews(
                new RekallAgeRuntimeRenderView(
                    Sort(cameras),
                    Sort(sprites),
                    Sort(meshes),
                    Sort(lights),
                    Sort(uiLayers)),
                new RekallAgeRuntimePhysicsView(
                    Sort(bodies),
                    Sort(colliders),
                    Sort(triggers)),
                new RekallAgeRuntimeAudioView(
                    Sort(listeners),
                    Sort(emitters)),
                new RekallAgeRuntimeAnimationView(Sort(animationPlayers)),
                new RekallAgeRuntimeUiView(
                    Sort(canvases),
                    Sort(elements),
                    elements.Count(element => element.Interactive)))
                {
                    Input = world.Subsystems.Input,
                    Multiplayer = new RekallAgeRuntimeMultiplayerView(
                        Sort(networkSessions),
                        Sort(networkEntities)),
                    Xr = new RekallAgeRuntimeXrView(
                        Sort(xrRigs),
                        Sort(xrControllers),
                        world.Subsystems.Xr.Poses
                            .OrderBy(pose => pose.EntityName, StringComparer.Ordinal)
                            .ThenBy(pose => pose.EntityId, StringComparer.Ordinal)
                            .ToArray(),
                        world.Subsystems.Xr.Actions
                            .OrderBy(action => action.Hand, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
                            .ToArray())
                },
            Observations = observations
                .OrderBy(observation => observation.Severity, StringComparer.Ordinal)
                .ThenBy(observation => observation.Subsystem, StringComparer.Ordinal)
                .ThenBy(observation => observation.TargetName, StringComparer.Ordinal)
                .ThenBy(observation => observation.Code, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static List<T> PreserveAuthored<T>(IEnumerable<T> renderItems)
    {
        return renderItems
            .Where(IsAuthored)
            .ToList();
    }

    private static bool IsAuthored<T>(T item)
    {
        return item switch
        {
            RekallAgeRuntimeRenderCamera value => value.ProjectionSource != RekallAgeRuntimeProjectionSources.BuiltIn,
            RekallAgeRuntimeRenderSprite value => value.ProjectionSource != RekallAgeRuntimeProjectionSources.BuiltIn,
            RekallAgeRuntimeRenderMesh value => value.ProjectionSource != RekallAgeRuntimeProjectionSources.BuiltIn,
            RekallAgeRuntimeRenderLight value => value.ProjectionSource != RekallAgeRuntimeProjectionSources.BuiltIn,
            RekallAgeRuntimeRenderUiLayer value => value.ProjectionSource != RekallAgeRuntimeProjectionSources.BuiltIn,
            _ => true
        };
    }

    private static RekallAgeRuntimeObservation CreateObservation(
        int frame,
        string code,
        string severity,
        string subsystem,
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent component,
        string message)
    {
        return new RekallAgeRuntimeObservation(
            frame,
            code,
            severity,
            subsystem,
            entity.Id,
            entity.Name,
            component.Type["Rekall.".Length..],
            message,
            Array.Empty<string>());
    }

    private static bool IsLight(string type)
    {
        return type.StartsWith("Rekall.", StringComparison.Ordinal)
            && type.Contains("Light", StringComparison.Ordinal);
    }

    private static bool IsStellarBody(RekallAgeRuntimeEntity entity)
    {
        return entity.Components.Any(component =>
            component.Type == "Rekall.CelestialBody"
            && ReadString(component.Properties, "type")?.Contains("stellar", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string? ReadStellarColor(RekallAgeRuntimeEntity entity)
    {
        var celestial = entity.Components.FirstOrDefault(component =>
            component.Type == "Rekall.CelestialBody"
            && ReadString(component.Properties, "type")?.Contains("stellar", StringComparison.OrdinalIgnoreCase) == true);
        return celestial is null ? null : ReadString(celestial.Properties, "color");
    }

    private static bool HasMeshRenderer(RekallAgeRuntimeEntity entity)
    {
        return entity.Components.Any(component =>
            component.Type is "Rekall.MeshRenderer" or "Rekall.MeshSet");
    }

    private static bool IsVisibilityGatedComponent(string componentType)
    {
        return componentType is
            "Rekall.Camera2D" or
            "Rekall.Camera3D" or
            "Rekall.RenderLayer" or
            "Rekall.SpriteRenderer" or
            "Rekall.MeshRenderer" or
            "Rekall.MeshSet" or
            "Rekall.GeometryPrimitive" or
            "Rekall.GeometryMesh" or
            "Rekall.LineSegments" or
            "Rekall.MultiplayerSession" or
            "Rekall.NetworkIdentity" or
            "Rekall.NetworkTransform" or
            "Rekall.XrRig" or
            "Rekall.XrController" or
            "Rekall.XrPoseSource" or
            "Rekall.PlanetRenderer" or
            "Rekall.AtmosphereRenderer" or
            "Rekall.OrbitPathRenderer" or
            "Rekall.PointLight" or
            "Rekall.DirectionalLight" or
            "Rekall.UiCanvas" or
            "Rekall.UiElement";
    }

    private static string ReadRenderLayer(RekallAgeRuntimeEntity entity)
    {
        var component = entity.Components.FirstOrDefault(item =>
            item.Type.Equals("Rekall.RenderLayer", StringComparison.Ordinal));
        var layer = component is null ? null : ReadString(component.Properties, "layer");
        return RekallAgeRenderLayerMask.NormalizeLayer(layer);
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return TryGetPropertyValue(properties, name, out var node) && node is JsonValue value
            ? value.TryGetValue<string>(out var text) ? text : null
            : null;
    }

    private static string? ReadStringOptional(JsonObject? properties, string name)
    {
        return properties is null ? null : ReadString(properties, name);
    }

    private static string NormalizeNetworkMode(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().ToLowerInvariant();
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeXrMode(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().ToLowerInvariant();
    }

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        if (value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static bool ReadBooleanOptional(JsonObject? properties, string name, bool fallback)
    {
        return properties is null ? fallback : ReadBoolean(properties, name, fallback);
    }

    private static int ReadInt32(JsonObject properties, string name, int fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return (int)number;
        }

        return fallback;
    }

    private static int ReadInt32Optional(JsonObject? properties, string name, int fallback)
    {
        return properties is null ? fallback : ReadInt32(properties, name, fallback);
    }

    private static int ClampInt(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        return fallback;
    }

    private static bool TryGetPropertyValue(JsonObject properties, string name, out JsonNode? node)
    {
        if (properties.TryGetPropertyValue(name, out node))
        {
            return true;
        }

        if (name.Length > 0)
        {
            var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
            if (properties.TryGetPropertyValue(pascalName, out node))
            {
                return true;
            }
        }

        node = null;
        return false;
    }

    private static string NormalizeProjectionMode(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "orthographic" or "ortho" ? "orthographic" : "perspective";
    }

    private static string NormalizeStereoMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "stereo" or "vr" or "xr" => "stereo",
            _ => "mono"
        };
    }

    private static string NormalizeStereoRenderMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "side-by-side" or "side_by_side" or "sbs" => "side-by-side",
            "dual-pass" or "dual_pass" => "dual-pass",
            _ => "single-pass-multiview"
        };
    }

    private static string NormalizeXrViewConfiguration(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "primary-mono" or "mono" => "primary-mono",
            "primary-stereo-with-foveated-inset" or "foveated-stereo" => "primary-stereo-with-foveated-inset",
            _ => "primary-stereo"
        };
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static IReadOnlyList<T> Sort<T>(IEnumerable<T> items)
    {
        return items
            .OrderBy(item => GetEntityName(item), StringComparer.Ordinal)
            .ThenBy(item => GetEntityId(item), StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetEntityName<T>(T item)
    {
        return item switch
        {
            RekallAgeRuntimeRenderCamera value => value.EntityName,
            RekallAgeRuntimeRenderSprite value => value.EntityName,
            RekallAgeRuntimeRenderMesh value => value.EntityName,
            RekallAgeRuntimeRenderLight value => value.EntityName,
            RekallAgeRuntimeRenderUiLayer value => value.EntityName,
            RekallAgeRuntimePhysicsBody value => value.EntityName,
            RekallAgeRuntimePhysicsCollider value => value.EntityName,
            RekallAgeRuntimeAudioListener value => value.EntityName,
            RekallAgeRuntimeAudioEmitter value => value.EntityName,
            RekallAgeRuntimeAnimationPlayer value => value.EntityName,
            RekallAgeRuntimeUiCanvas value => value.EntityName,
            RekallAgeRuntimeUiElement value => value.EntityName,
            RekallAgeRuntimeNetworkSession value => value.EntityName,
            RekallAgeRuntimeNetworkEntity value => value.EntityName,
            RekallAgeRuntimeXrRig value => value.EntityName,
            RekallAgeRuntimeXrController value => value.EntityName,
            _ => string.Empty
        };
    }

    private static string GetEntityId<T>(T item)
    {
        return item switch
        {
            RekallAgeRuntimeRenderCamera value => value.EntityId,
            RekallAgeRuntimeRenderSprite value => value.EntityId,
            RekallAgeRuntimeRenderMesh value => value.EntityId,
            RekallAgeRuntimeRenderLight value => value.EntityId,
            RekallAgeRuntimeRenderUiLayer value => value.EntityId,
            RekallAgeRuntimePhysicsBody value => value.EntityId,
            RekallAgeRuntimePhysicsCollider value => value.EntityId,
            RekallAgeRuntimeAudioListener value => value.EntityId,
            RekallAgeRuntimeAudioEmitter value => value.EntityId,
            RekallAgeRuntimeAnimationPlayer value => value.EntityId,
            RekallAgeRuntimeUiCanvas value => value.EntityId,
            RekallAgeRuntimeUiElement value => value.EntityId,
            RekallAgeRuntimeNetworkSession value => value.EntityId,
            RekallAgeRuntimeNetworkEntity value => value.EntityId,
            RekallAgeRuntimeXrRig value => value.EntityId,
            RekallAgeRuntimeXrController value => value.EntityId,
            _ => string.Empty
        };
    }
}
