using System.Text.Json.Nodes;
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
        var observations = new List<RekallAgeRuntimeObservation>(world.Observations);

        foreach (var entity in world.Entities)
        {
            var hasTransform = entity.Components.Any(component =>
                component.Type is "Rekall.Transform2D" or "Rekall.Transform3D");

            foreach (var component in entity.Components)
            {
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
                                ?? (component.Type == "Rekall.Camera2D" ? "#102030" : "#101820")));
                        break;
                    case "Rekall.SpriteRenderer":
                        sprites.Add(new RekallAgeRuntimeRenderSprite(
                            entity.Id,
                            entity.Name,
                            ReadString(component.Properties, "sprite") ?? ReadString(component.Properties, "assetId"),
                            RekallAgeRuntimeProjectionSources.BuiltIn));
                        break;
                    case "Rekall.MeshRenderer":
                    case "Rekall.MeshSet":
                        meshes.Add(new RekallAgeRuntimeRenderMesh(
                            entity.Id,
                            entity.Name,
                            ReadString(component.Properties, "mesh") ?? ReadString(component.Properties, "assetId"),
                            ProjectionSource: RekallAgeRuntimeProjectionSources.BuiltIn));
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
                                ProjectionSource: RekallAgeRuntimeProjectionSources.BuiltIn));
                        }

                        break;
                    case "Rekall.GeometryMesh":
                        if (!HasMeshRenderer(entity))
                        {
                            meshes.Add(new RekallAgeRuntimeRenderMesh(
                                entity.Id,
                                entity.Name,
                                "rekall.geometry.mesh",
                                ProjectionSource: RekallAgeRuntimeProjectionSources.BuiltIn));
                        }

                        break;
                    case "Rekall.PlanetRenderer":
                        meshes.Add(new RekallAgeRuntimeRenderMesh(
                            entity.Id,
                            entity.Name,
                            "rekall.planet.surface",
                            ProjectionSource: RekallAgeRuntimeProjectionSources.BuiltIn));
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
                                RekallAgeRuntimeProjectionSources.BuiltIn));
                        }

                        break;
                }
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
                    elements.Count(element => element.Interactive))),
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

    private static bool HasMeshRenderer(RekallAgeRuntimeEntity entity)
    {
        return entity.Components.Any(component =>
            component.Type is "Rekall.MeshRenderer" or "Rekall.MeshSet");
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return TryGetPropertyValue(properties, name, out var node) && node is JsonValue value
            ? value.TryGetValue<string>(out var text) ? text : null
            : null;
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
            _ => string.Empty
        };
    }
}
