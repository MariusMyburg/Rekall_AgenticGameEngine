namespace Rekall.Age.World;

public static class RekallAgeBuiltInComponentTypeCatalog
{
    public static IReadOnlySet<string> Types { get; } = new HashSet<string>(
    [
        "Rekall.Transform2D",
        "Rekall.Transform3D",
        "Rekall.InputActionMap",
        "Rekall.EventBindings",
        "Rekall.PointerRay",
        "Rekall.Timer",
        "Rekall.Camera2D",
        "Rekall.Camera3D",
        "Rekall.CameraZoomInput",
        "Rekall.CameraTarget3D",
        "Rekall.CameraTargetCycleInput",
        "Rekall.RenderLayer",
        "Rekall.RenderQualityProfile",
        "Rekall.Environment3D",
        "Rekall.ShadowSettings",
        "Rekall.FogVolume",
        "Rekall.ParticleEmitter3D",
        "Rekall.SpriteRenderer",
        "Rekall.MeshRenderer",
        "Rekall.XrRig",
        "Rekall.XrPoseSource",
        "Rekall.XrController",
        "Rekall.DirectionalLight",
        "Rekall.PointLight",
        "Rekall.MultiplayerSession",
        "Rekall.NetworkIdentity",
        "Rekall.NetworkTransform",
        "Rekall.GeometryPrimitive",
        "Rekall.GeometryMesh",
        "Rekall.MeshAssetReference",
        "Rekall.ModelAssetReference",
        "Rekall.LineSegments",
        "Rekall.GeometryExtrusion",
        "Rekall.Material",
        "Rekall.ProceduralMaterial",
        "Rekall.LodGroup",
        "Rekall.VirtualGeometry",
        "Rekall.PhysicsWorld3D",
        "Rekall.PhysicsMaterial3D",
        "Rekall.PhysicsMaterial2D",
        "Rekall.BallSocketJoint",
        "Rekall.HingeJoint",
        "Rekall.DistanceJoint",
        "Rekall.WeldJoint",
        "Rekall.FixedJoint",
        "Rekall.Rigidbody2D",
        "Rekall.Rigidbody3D",
        "Rekall.Trigger",
        "Rekall.CollisionFilter",
        "Rekall.BoxCollider2D",
        "Rekall.CircleCollider2D",
        "Rekall.BoxCollider3D",
        "Rekall.SphereCollider3D",
        "Rekall.CapsuleCollider3D",
        "Rekall.MeshCollider",
        "Rekall.Destructible",
        "Rekall.PlanetRenderer",
        "Rekall.CloudLayerRenderer",
        "Rekall.AtmosphereRenderer",
        "Rekall.CelestialBody",
        "Rekall.KeplerOrbit",
        "Rekall.CelestialRotation",
        "Rekall.OrbitPathRenderer",
        "Rekall.RingRenderer",
        "Rekall.StarfieldRenderer",
        "Rekall.GrassRenderer",
        "Rekall.MarkerRenderer",
        "Rekall.HaloRenderer",
        "Rekall.PostProcessStack",
        "Rekall.TextLabelRenderer",
        "Rekall.AudioListener",
        "Rekall.AudioEmitter",
        "Rekall.AudioBus",
        "Rekall.AnimationClip",
        "Rekall.AnimationPlayer",
        "Rekall.AnimationMixer",
        "Rekall.AnimationStateGraph",
        "Rekall.SkeletalAnimator",
        "Rekall.SkeletonPose",
        "Rekall.RigPose",
        "Rekall.RigAttachment",
        "Rekall.MorphWeights",
        "Rekall.UiCanvas",
        "Rekall.UiElement",
        "Rekall.Panel",
        "Rekall.Label",
        "Rekall.Image",
        "Rekall.Button"
    ], StringComparer.Ordinal);

    public static bool IsKnown(string componentType) => Types.Contains(componentType.Trim());

    public static bool IsUnknownReserved(string componentType)
    {
        var normalized = componentType.Trim();
        return normalized.StartsWith("Rekall.", StringComparison.OrdinalIgnoreCase)
            && !IsKnown(normalized);
    }

    public static string? FindSafeSuggestion(string componentType)
    {
        componentType = componentType.Trim();
        if (!componentType.StartsWith("Rekall.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        componentType = "Rekall." + componentType["Rekall.".Length..];

        var finalSegment = componentType[(componentType.LastIndexOf('.') + 1)..];
        var exactSuffixMatches = Types
            .Where(type => type[(type.LastIndexOf('.') + 1)..]
                .Equals(finalSegment, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (exactSuffixMatches.Length == 1)
        {
            return exactSuffixMatches[0];
        }

        var nearest = Types
            .Select(type => (Type: type, Distance: EditDistance(componentType, type)))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Type, StringComparer.Ordinal)
            .First();
        return nearest.Distance <= 3 ? nearest.Type : null;
    }

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            var current = new int[right.Length + 1];
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitution);
            }

            previous = current;
        }

        return previous[right.Length];
    }
}
