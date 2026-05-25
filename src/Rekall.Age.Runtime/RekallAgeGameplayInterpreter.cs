using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeGameplayInterpreter
{
    public IReadOnlyList<RekallAgeRuntimeObservation> Observe(
        RekallAgeSceneDocument scene,
        int frame)
    {
        var observations = new List<RekallAgeRuntimeObservation>();

        foreach (var entity in scene.Entities)
        {
            foreach (var component in entity.Components)
            {
                var system = GetSystemName(component.Type);
                if (system is null)
                {
                    continue;
                }

                observations.Add(new RekallAgeRuntimeObservation(
                    frame,
                    "REKALL_RUNTIME_SYSTEM_EVALUATED",
                    "info",
                    GetSubsystemName(system),
                    entity.Id,
                    entity.Name,
                    system,
                    CreateMessage(system, entity.Name),
                    Array.Empty<string>()));
            }
        }

        return observations
            .OrderBy(observation => observation.EntityName, StringComparer.Ordinal)
            .ThenBy(observation => observation.System, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? GetSystemName(string componentType)
    {
        const string prefix = "Rekall.";
        if (!componentType.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var system = componentType[prefix.Length..];
        return IsCoreEngineSystem(system) ? system : null;
    }

    private static bool IsCoreEngineSystem(string system)
    {
        return system is
            "Camera2D" or
            "Camera3D" or
            "SpriteRenderer" or
            "MeshRenderer" or
            "MeshSet" or
            "GeometryPrimitive" or
            "GeometryMesh" or
            "GeometryExtrusion" or
            "PlanetRenderer" or
            "AtmosphereRenderer" or
            "DirectionalLight" or
            "PointLight" or
            "SpotLight" or
            "Rigidbody2D" or
            "Rigidbody3D" or
            "BoxCollider2D" or
            "CircleCollider2D" or
            "BoxCollider3D" or
            "SphereCollider3D" or
            "CapsuleCollider3D" or
            "MeshCollider" or
            "Trigger" or
            "PhysicsWorld3D" or
            "PhysicsMaterial3D" or
            "PhysicsState3D" or
            "AudioListener" or
            "AudioEmitter" or
            "AnimationPlayer" or
            "SpriteAnimator" or
            "TransformAnimation" or
            "UiCanvas" or
            "UiElement" or
            "Button" or
            "Label" or
            "Panel";
    }

    private static string CreateMessage(string system, string entityName)
    {
        return system switch
        {
            "Camera2D" or "Camera3D" => $"Camera '{entityName}' is available for capture.",
            _ => $"{system} evaluated."
        };
    }

    private static string GetSubsystemName(string system)
    {
        return system switch
        {
            "Camera2D" or "Camera3D" or "SpriteRenderer" or "MeshRenderer" or "MeshSet" or "PlanetRenderer" or "AtmosphereRenderer" => "rendering",
            "Rigidbody2D" or "Rigidbody3D" or "BoxCollider2D" or "CircleCollider2D" or "BoxCollider3D" or "SphereCollider3D" or "CapsuleCollider3D" or "MeshCollider" or "Trigger" => "physics",
            "AudioListener" or "AudioEmitter" => "audio",
            "AnimationPlayer" or "SpriteAnimator" => "animation",
            "UiCanvas" or "UiElement" or "Button" or "Label" or "Panel" => "ui",
            _ => "gameplay"
        };
    }
}
