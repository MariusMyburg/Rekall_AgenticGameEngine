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
        return system is "Transform" or "Transform2D" or "Transform3D"
            ? null
            : system;
    }

    private static string CreateMessage(string system, string entityName)
    {
        return system switch
        {
            "PlayableLoop" => "Playable loop advanced.",
            "Camera2D" or "Camera3D" => $"Camera '{entityName}' is available for capture.",
            "DialogueGraph" => "Dialogue graph is ready for branching choices.",
            "WaveSpawner" => "Wave spawning schedule advanced.",
            "AsteroidSpawner" => "Asteroid field schedule advanced.",
            "BrickGrid" => "Brick grid state evaluated.",
            "GridBoard" => "Puzzle grid state evaluated.",
            "FirstPersonController" or "ThirdPersonController" => "3D controller intent evaluated.",
            "PlatformerController2D" => "Platformer movement intent evaluated.",
            _ => $"{system} evaluated."
        };
    }

    private static string GetSubsystemName(string system)
    {
        return system switch
        {
            "Camera2D" or "Camera3D" or "SpriteRenderer" or "MeshRenderer" or "MeshSet" => "rendering",
            "Rigidbody2D" or "Rigidbody3D" or "BoxCollider2D" or "CircleCollider2D" or "BoxCollider3D" or "MeshCollider" or "Trigger" => "physics",
            "AudioListener" or "AudioEmitter" => "audio",
            "AnimationPlayer" or "SpriteAnimator" => "animation",
            "UiCanvas" or "UiElement" or "Button" or "Label" or "Panel" => "ui",
            _ => "gameplay"
        };
    }
}
