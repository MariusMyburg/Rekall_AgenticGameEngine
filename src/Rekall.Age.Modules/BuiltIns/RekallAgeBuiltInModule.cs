using System.Text.Json.Nodes;

namespace Rekall.Age.Modules.BuiltIns;

[RekallAgeModule("rekall.builtins", "Rekall Built-ins")]
[RekallAgeRequiresCapability("world")]
public sealed class RekallAgeBuiltInModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<RekallAgeTransform2DComponent>();
        builder.RegisterComponent<RekallAgeTransform3DComponent>();
        builder.RegisterComponent<RekallAgeInputActionMapComponent>();
        builder.RegisterComponent<RekallAgeEventBindingsComponent>();
        builder.RegisterComponent<RekallAgePointerRayComponent>();
        builder.RegisterComponent<RekallAgeTimerComponent>();
        builder.RegisterComponent<RekallAgeCamera2DComponent>();
        builder.RegisterComponent<RekallAgeCamera3DComponent>();
        builder.RegisterComponent<RekallAgeCameraZoomInputComponent>();
        builder.RegisterComponent<RekallAgeCameraTarget3DComponent>();
        builder.RegisterComponent<RekallAgeCameraTargetCycleInputComponent>();
        builder.RegisterComponent<RekallAgeRenderLayerComponent>();
        builder.RegisterComponent<RekallAgeRenderQualityProfileComponent>();
        builder.RegisterComponent<RekallAgeEnvironment3DComponent>();
        builder.RegisterComponent<RekallAgeSceneTransitionComponent>();
        builder.RegisterComponent<RekallAgePersistentStateComponent>();
        builder.RegisterComponent<RekallAgeShadowSettingsComponent>();
        builder.RegisterComponent<RekallAgeFogVolumeComponent>();
        builder.RegisterComponent<RekallAgeParticleEmitter3DComponent>();
        builder.RegisterComponent<RekallAgeSpriteRendererComponent>();
        builder.RegisterComponent<RekallAgeMeshRendererComponent>();
        builder.RegisterComponent<RekallAgeXrRigComponent>();
        builder.RegisterComponent<RekallAgeXrPoseSourceComponent>();
        builder.RegisterComponent<RekallAgeXrControllerComponent>();
        builder.RegisterComponent<RekallAgeDirectionalLightComponent>();
        builder.RegisterComponent<RekallAgePointLightComponent>();
        builder.RegisterComponent<RekallAgeSpotLightComponent>();
        builder.RegisterComponent<RekallAgeMultiplayerSessionComponent>();
        builder.RegisterComponent<RekallAgeNetworkIdentityComponent>();
        builder.RegisterComponent<RekallAgeNetworkTransformComponent>();
        builder.RegisterComponent<RekallAgeGeometryPrimitiveComponent>();
        builder.RegisterComponent<RekallAgeGeometryMeshComponent>();
        builder.RegisterComponent<RekallAgeMeshAssetReferenceComponent>();
        builder.RegisterComponent<RekallAgeModelAssetReferenceComponent>();
        builder.RegisterComponent<RekallAgeLineSegmentsComponent>();
        builder.RegisterComponent<RekallAgeGeometryExtrusionComponent>();
        builder.RegisterComponent<RekallAgeMaterialComponent>();
        builder.RegisterComponent<RekallAgeProceduralMaterialComponent>();
        builder.RegisterComponent<RekallAgeLodGroupComponent>();
        builder.RegisterComponent<RekallAgeVirtualGeometryComponent>();
        builder.RegisterComponent<RekallAgePhysicsWorld3DComponent>();
        builder.RegisterComponent<RekallAgePhysicsMaterial3DComponent>();
        builder.RegisterComponent<RekallAgePhysicsMaterial2DComponent>();
        builder.RegisterComponent<RekallAgeBallSocketJointComponent>();
        builder.RegisterComponent<RekallAgeHingeJointComponent>();
        builder.RegisterComponent<RekallAgeDistanceJointComponent>();
        builder.RegisterComponent<RekallAgeWeldJointComponent>();
        builder.RegisterComponent<RekallAgeFixedJointComponent>();
        builder.RegisterComponent<RekallAgeRigidbody2DComponent>();
        builder.RegisterComponent<RekallAgeRigidbody3DComponent>();
        builder.RegisterComponent<RekallAgeTriggerComponent>();
        builder.RegisterComponent<RekallAgeCollisionFilterComponent>();
        builder.RegisterComponent<RekallAgeBoxCollider2DComponent>();
        builder.RegisterComponent<RekallAgeCircleCollider2DComponent>();
        builder.RegisterComponent<RekallAgeBoxCollider3DComponent>();
        builder.RegisterComponent<RekallAgeSphereCollider3DComponent>();
        builder.RegisterComponent<RekallAgeCapsuleCollider3DComponent>();
        builder.RegisterComponent<RekallAgeMeshColliderComponent>();
        builder.RegisterComponent<RekallAgeDestructibleComponent>();
        builder.RegisterComponent<RekallAgePlanetRendererComponent>();
        builder.RegisterComponent<RekallAgeCloudLayerRendererComponent>();
        builder.RegisterComponent<RekallAgeAtmosphereRendererComponent>();
        builder.RegisterComponent<RekallAgeCelestialBodyComponent>();
        builder.RegisterComponent<RekallAgeKeplerOrbitComponent>();
        builder.RegisterComponent<RekallAgeCelestialRotationComponent>();
        builder.RegisterComponent<RekallAgeOrbitPathRendererComponent>();
        builder.RegisterComponent<RekallAgeRingRendererComponent>();
        builder.RegisterComponent<RekallAgeStarfieldRendererComponent>();
        builder.RegisterComponent<RekallAgeGrassRendererComponent>();
        builder.RegisterComponent<RekallAgeMarkerRendererComponent>();
        builder.RegisterComponent<RekallAgeHaloRendererComponent>();
        builder.RegisterComponent<RekallAgePostProcessStackComponent>();
        builder.RegisterComponent<RekallAgeTextLabelRendererComponent>();
        builder.RegisterComponent<RekallAgeAudioListenerComponent>();
        builder.RegisterComponent<RekallAgeAudioEmitterComponent>();
        builder.RegisterComponent<RekallAgeAudioBusComponent>();
        builder.RegisterComponent<RekallAgeAnimationClipComponent>();
        builder.RegisterComponent<RekallAgeAnimationPlayerComponent>();
        builder.RegisterComponent<RekallAgeAnimationMixerComponent>();
        builder.RegisterComponent<RekallAgeAnimationStateGraphComponent>();
        builder.RegisterComponent<RekallAgeSkeletalAnimatorComponent>();
        builder.RegisterComponent<RekallAgeSkeletonPoseComponent>();
        builder.RegisterComponent<RekallAgeRigPoseComponent>();
        builder.RegisterComponent<RekallAgeRigAttachmentComponent>();
        builder.RegisterComponent<RekallAgeMorphWeightsComponent>();
        builder.RegisterComponent<RekallAgeUiCanvasComponent>();
        builder.RegisterComponent<RekallAgeUiElementComponent>();
        builder.RegisterComponent<RekallAgePanelComponent>();
        builder.RegisterComponent<RekallAgeLabelComponent>();
        builder.RegisterComponent<RekallAgeImageComponent>();
        builder.RegisterComponent<RekallAgeButtonComponent>();
    }
}

[RekallAgeComponent("Transform")]
public sealed class RekallAgeTransformComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public double X { get; init; }

    [RekallAgeProperty]
    public double Y { get; init; }

    [RekallAgeProperty]
    public double Z { get; init; }
}

[RekallAgeComponent("Input Action Map")]
public sealed class RekallAgeInputActionMapComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty(
        Kind = "inputActions",
        Description = "Native JSON array of semantic bindings. Each object requires name and may combine keyboard/mouse fields with controllerButton/controllerAxis/controllerHat, signed variants, deadzone/saturation/invert/responseExponent, and deviceKind/deviceId/playerIndex filters. gamepad* and joystick* aliases are accepted. Pass native JSON, never an encoded string; runtime value/isDown samples are not bindings.")]
    public RekallAgeInputActionBinding[] Actions { get; init; } =
    [
        new("primary", Key: "Space")
    ];
}

public sealed record RekallAgeInputActionBinding(
    string Name,
    string? Key = null,
    string? Button = null,
    string? PositiveKey = null,
    string? NegativeKey = null,
    string? PositiveButton = null,
    string? NegativeButton = null,
    double MouseWheelScale = 0,
    string? MouseAxis = null,
    double MouseScale = 1,
    string? ControllerButton = null,
    string? PositiveControllerButton = null,
    string? NegativeControllerButton = null,
    string? ControllerAxis = null,
    double ControllerAxisScale = 1,
    double Deadzone = 0,
    double Saturation = 1,
    bool Invert = false,
    double ResponseExponent = 1,
    string? ControllerHat = null,
    string? ControllerHatDirection = null,
    string? DeviceId = null,
    string? DeviceKind = null,
    int? PlayerIndex = null);

[RekallAgeComponent("Event Bindings", Description = "Binds generic runtime event facts to optional handler names. Agent-authored modules consume the facts and decide game behavior; the engine does not attach genre-specific consequences.")]
public sealed class RekallAgeEventBindingsComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty(
        Kind = "runtimeEvents",
        Description = "Array of { event, handler, active } objects. Built-in facts include entity.begin, entity.tick, timer.elapsed, pointer.enter/leave/hit/down/up/click, collision.begin/stay/end for 2D or 3D colliders, and trigger.enter/stay/exit. Agent-authored modules may emit and bind custom event names through EmitEvent and EmitBoundEvents.")]
    public RekallAgeEventBinding[] Events { get; init; } =
    [
        new("entity.tick")
    ];
}

public sealed record RekallAgeEventBinding(
    string Event,
    string? Handler = null,
    bool Active = true);

[RekallAgeComponent("Pointer Ray")]
public sealed class RekallAgePointerRayComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string PointerId { get; init; } = "primary";

    [RekallAgeProperty]
    public double OriginX { get; init; }

    [RekallAgeProperty]
    public double OriginY { get; init; }

    [RekallAgeProperty]
    public double OriginZ { get; init; }

    [RekallAgeProperty]
    public double DirectionX { get; init; }

    [RekallAgeProperty]
    public double DirectionY { get; init; }

    [RekallAgeProperty]
    public double DirectionZ { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0)]
    public double Range { get; init; } = 100;

    [RekallAgeProperty]
    public string Button { get; init; } = "Left";

    [RekallAgeProperty]
    public string TargetTag { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string TargetComponentType { get; init; } = string.Empty;
}

[RekallAgeComponent("Timer")]
public sealed class RekallAgeTimerComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string TimerId { get; init; } = "timer";

    [RekallAgeProperty(Minimum = 0.000001)]
    public double DurationSeconds { get; init; } = 1;

    [RekallAgeProperty]
    public bool Repeat { get; init; }
}

[RekallAgeComponent("Camera 2D", Description = "Projects a 2D view. Put position, rotation, and scale on a separate Rekall.Transform2D component on the same entity; Camera2D properties configure projection, clipping, viewport, and render layers only.")]
public sealed class RekallAgeCamera2DComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string ClearColor { get; init; } = "#102030";

    [RekallAgeProperty(Description = "Comma-separated named render layers included by this camera. Use * to include every layer and !name to exclude a layer, for example '*, !helpers'. This is a layer-name expression, not a numeric bitmask.")]
    public string CullingMask { get; init; } = "*";

    [RekallAgeProperty]
    public double RenderOrder { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double ViewportX { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double ViewportY { get; init; }

    [RekallAgeProperty(Minimum = 0.001, Maximum = 1)]
    public double ViewportWidth { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.001, Maximum = 1)]
    public double ViewportHeight { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.001)]
    public double OrthographicSize { get; init; } = 10;

    [RekallAgeProperty]
    public double NearClip { get; init; } = -1000;

    [RekallAgeProperty]
    public double FarClip { get; init; } = 1000;
}

[RekallAgeComponent("Camera 3D", Description = "Projects a 3D view. Put position, rotation, and scale on a separate Rekall.Transform3D component on the same entity; an unrotated camera faces +Z, so use the Transform3D Euler convention to aim it. Camera3D properties configure projection, clipping, viewport, render layers, and stereo behavior only.")]
public sealed class RekallAgeCamera3DComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string ProjectionMode { get; init; } = "perspective";

    [RekallAgeProperty(Minimum = 1, Maximum = 179)]
    public double FieldOfView { get; init; } = 65;

    [RekallAgeProperty(Minimum = 0.001)]
    public double OrthographicSize { get; init; } = 10;

    [RekallAgeProperty(Minimum = 0.001)]
    public double NearClip { get; init; } = 0.05;

    [RekallAgeProperty(Minimum = 0.001)]
    public double FarClip { get; init; } = 1000;

    [RekallAgeProperty]
    public string ClearColor { get; init; } = "#101820";

    [RekallAgeProperty(Description = "Comma-separated named render layers included by this camera. Use * to include every layer and !name to exclude a layer, for example '*, !helpers'. This is a layer-name expression, not a numeric bitmask.")]
    public string CullingMask { get; init; } = "*";

    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public double RenderOrder { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double ViewportX { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double ViewportY { get; init; }

    [RekallAgeProperty(Minimum = 0.001, Maximum = 1)]
    public double ViewportWidth { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.001, Maximum = 1)]
    public double ViewportHeight { get; init; } = 1;

    [RekallAgeProperty]
    public string StereoMode { get; init; } = "mono";

    [RekallAgeProperty]
    public string StereoRenderMode { get; init; } = "single-pass-multiview";

    [RekallAgeProperty(Minimum = 0)]
    public double InterpupillaryDistance { get; init; } = 0.064;

    [RekallAgeProperty(Minimum = 0.001)]
    public double StereoConvergenceDistance { get; init; } = 10;

    [RekallAgeProperty]
    public string XrViewConfiguration { get; init; } = "primary-stereo";

    [RekallAgeProperty]
    public bool FoveatedRendering { get; init; }
}

[RekallAgeComponent("Camera Zoom Input")]
public sealed class RekallAgeCameraZoomInputComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double WheelZoomSpeed { get; init; } = 0.12;

    [RekallAgeProperty(Minimum = 0.001)]
    public double MinimumOrthographicSize { get; init; } = 0.1;

    [RekallAgeProperty(Minimum = 0.001)]
    public double MaximumOrthographicSize { get; init; } = 100000;

    [RekallAgeProperty(Minimum = 1, Maximum = 179)]
    public double MinimumFieldOfView { get; init; } = 15;

    [RekallAgeProperty(Minimum = 1, Maximum = 179)]
    public double MaximumFieldOfView { get; init; } = 120;

    [RekallAgeProperty]
    public bool InvertWheel { get; init; }
}

[RekallAgeComponent(
    "Camera Target 3D",
    Description = "Attaches this entity (typically a camera) to a target entity with an authored offset and optional look-at, similar to Unreal's SpringArmComponent. FollowPosition alone snaps instantly every frame; enable PositionLagEnabled/RotationLagEnabled for the smoothed, trailing-behind motion an actual spring arm has, tuned by PositionLagSpeed/RotationLagSpeed and optionally bounded by MaximumPositionLagDistance. Enable CollisionAvoidanceEnabled for the other classic spring-arm feature: a sphere of CollisionProbeRadius sweeps from the target out to the desired camera position and pulls the camera in to CollisionMinimumDistance short of anything in the way, so it does not clip through geometry.")]
public sealed class RekallAgeCameraTarget3DComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string TargetEntityId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string TargetName { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string TargetTag { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string OffsetReferenceEntityId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string OffsetReferenceName { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string OffsetReferenceTag { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string OffsetReferenceMode { get; init; } = "toward";

    [RekallAgeProperty(Minimum = 0)]
    public double OffsetDistance { get; init; }

    [RekallAgeProperty]
    public double OffsetVertical { get; init; }

    [RekallAgeProperty]
    public double OffsetLateral { get; init; }

    [RekallAgeProperty]
    public double OffsetX { get; init; }

    [RekallAgeProperty]
    public double OffsetY { get; init; } = 2;

    [RekallAgeProperty]
    public double OffsetZ { get; init; } = 6;

    [RekallAgeProperty]
    public double TargetOffsetX { get; init; }

    [RekallAgeProperty]
    public double TargetOffsetY { get; init; }

    [RekallAgeProperty]
    public double TargetOffsetZ { get; init; }

    [RekallAgeProperty]
    public bool FollowPosition { get; init; } = true;

    [RekallAgeProperty]
    public bool LookAt { get; init; } = true;

    [RekallAgeProperty(Description = "When true, the camera's position smoothly trails the instant target+offset position instead of snapping to it every frame - the actual spring-arm \"delayed motion\" behavior. Off by default so existing scenes authored before this property existed keep their exact prior instant-follow behavior.")]
    public bool PositionLagEnabled { get; init; }

    [RekallAgeProperty(Minimum = 0.0001, Description = "Exponential-decay catch-up rate in 1/seconds for position lag: higher values catch up to the target faster (snappier), lower values trail further behind (looser). Matches Unreal's SpringArmComponent CameraLagSpeed in spirit. Ignored unless PositionLagEnabled is true.")]
    public double PositionLagSpeed { get; init; } = 10;

    [RekallAgeProperty(Minimum = 0, Description = "Caps how far the smoothed camera position may lag behind the instant target+offset position, in world units. 0 (the default) means unbounded lag distance. Prevents the camera falling arbitrarily far behind during a sudden fast target movement.")]
    public double MaximumPositionLagDistance { get; init; }

    [RekallAgeProperty(Description = "Same idea as PositionLagEnabled but for the look-at rotation: when true, rotation smoothly trails the instant look-at orientation using shortest-path angle interpolation on each axis, instead of snapping. Off by default for the same backward-compatibility reason as PositionLagEnabled. Ignored when LookAt is false.")]
    public bool RotationLagEnabled { get; init; }

    [RekallAgeProperty(Minimum = 0.0001, Description = "Exponential-decay catch-up rate in 1/seconds for rotation lag, same semantics as PositionLagSpeed. Ignored unless RotationLagEnabled is true.")]
    public double RotationLagSpeed { get; init; } = 10;

    [RekallAgeProperty(Description = "When true, sweeps a sphere (radius CollisionProbeRadius) from the target's position out toward the desired camera position every frame and pulls the camera in if something (other than the target or camera entities themselves) is in the way, so it never clips through geometry - the same purpose a real spring arm's own collision channel serves. Obstructions are approximated as bounding spheres around their colliders (matching how Rekall.Trigger already approximates collider overlap), so it is not pixel-exact against a box or mesh's true corners. Off by default.")]
    public bool CollisionAvoidanceEnabled { get; init; }

    [RekallAgeProperty(Minimum = 0, Description = "How close, in world units, the camera is allowed to sit in front of whatever the collision probe hit. Also the minimum possible distance from the target when an obstruction is found extremely close. Ignored unless CollisionAvoidanceEnabled is true.")]
    public double CollisionMinimumDistance { get; init; } = 0.1;

    [RekallAgeProperty(Minimum = 0, Description = "Radius of the sphere swept along the arm for collision avoidance, in world units - roughly how large the camera itself is treated as being for clipping purposes. 0 degrades to a thin ray. Ignored unless CollisionAvoidanceEnabled is true.")]
    public double CollisionProbeRadius { get; init; } = 0.15;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Camera Target Cycle Input")]
public sealed class RekallAgeCameraTargetCycleInputComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string NextAction { get; init; } = "nextTarget";

    [RekallAgeProperty]
    public string PreviousAction { get; init; } = "previousTarget";

    [RekallAgeProperty(Minimum = 0)]
    public int CurrentIndex { get; init; }

    [RekallAgeProperty]
    public object[] Targets { get; init; } = [];
}

[RekallAgeComponent("Render Layer")]
public sealed class RekallAgeRenderLayerComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Layer { get; init; } = "default";
}

[RekallAgeComponent(
    "Render Quality Profile",
    Description = "Backend-neutral authored rendering-quality intent. The renderer resolves these requests against device capabilities and reports requested, resolved, and degradation facts without changing gameplay simulation.")]
public sealed class RekallAgeRenderQualityProfileComponent : RekallAgeComponent
{
    [RekallAgeProperty(AllowedValues = ["Performance", "Low", "Medium", "High", "Ultra", "Epic"])]
    public string Preset { get; init; } = "High";

    [RekallAgeProperty]
    public double ResolutionScale { get; init; } = 1;

    [RekallAgeProperty]
    public int ShadowCascadeCount { get; init; } = 3;

    [RekallAgeProperty]
    public int ShadowResolution { get; init; } = 2048;

    [RekallAgeProperty(AllowedValues = ["analytic", "froxel-low", "froxel", "froxel-high", "froxel-epic"])]
    public string FogMode { get; init; } = "froxel";

    [RekallAgeProperty]
    public bool Bloom { get; init; } = true;

    [RekallAgeProperty]
    public bool Ssao { get; init; } = true;

    [RekallAgeProperty]
    public int MaximumActiveParticles { get; init; } = 64_000;

    [RekallAgeProperty]
    public bool AutomaticScaling { get; init; }

    [RekallAgeProperty]
    public double TargetFramesPerSecond { get; init; } = 60;

    [RekallAgeProperty]
    public bool EnableGpuTimestamps { get; init; }
}

[RekallAgeComponent("Persistent State", Description =
    "Project-scoped state that survives a restart: settings, campaign progress, save slots. " +
    "The runtime loads the named slot into Document when the scene starts, and writes Document " +
    "back whenever an authored module changes it. Without this a game cannot remember anything " +
    "a player does between sessions.")]
public sealed class RekallAgePersistentStateComponent : RekallAgeComponent
{
    /// <summary>Slot name: letters, digits, '-', '_' and '.' only. Not a path.</summary>
    [RekallAgeProperty]
    public string Slot { get; init; } = "settings";

    /// <summary>The stored document. Loaded by the runtime, written back when modules change it.</summary>
    [RekallAgeProperty]
    public object? Document { get; init; }
}

[RekallAgeComponent("Scene Transition", Description =
    "Requests that the runtime load a different scene. Authored modules set RequestedScene to " +
    "move between levels, menus and missions; the request is satisfied by loading that scene, " +
    "which necessarily replaces this component's world. Without this a game can only be moved " +
    "between scenes from outside the running player, so it cannot drive its own level flow.")]
public sealed class RekallAgeSceneTransitionComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string RequestedScene { get; init; } = string.Empty;

    /// <summary>Free-text note carried into diagnostics, e.g. "player pressed Deploy".</summary>
    [RekallAgeProperty]
    public string Reason { get; init; } = string.Empty;
}

[RekallAgeComponent(
    "Environment 3D",
    Description = "Authors backend-neutral sky, ambient-light, exposure, tone-map, grade, and background intent. A background color is the deterministic fallback when a requested sky asset is absent or unsupported by the active renderer.")]
public sealed class RekallAgeEnvironment3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "assetRef", AssetKind = "environment")]
    public string? SkyAsset { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "environment")]
    public string? EnvironmentAsset { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 16)]
    public double AmbientEnergy { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string AmbientSkyColor { get; init; } = "#ffffff";

    [RekallAgeProperty(Kind = "color")]
    public string AmbientGroundColor { get; init; } = "#ffffff";

    [RekallAgeProperty(Kind = "color")]
    public string? BackgroundColor { get; init; }

    [RekallAgeProperty(Minimum = -8, Maximum = 8)]
    public double Exposure { get; init; }

    [RekallAgeProperty(AllowedValues = ["agx", "aces", "linear"])]
    public string ToneMapper { get; init; } = "agx";

    [RekallAgeProperty(Minimum = 0.1, Maximum = 64)]
    public double WhitePoint { get; init; } = 11.2;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "colorGrade")]
    public string? ColorGrade { get; init; }

    [RekallAgeProperty(AllowedValues = ["skybox", "color", "camera", "clear"])]
    public string BackgroundPolicy { get; init; } = "skybox";
}

[RekallAgeComponent(
    "Shadow Settings",
    Description = "Authors backend-neutral directional-shadow quality and stability intent for the scene.")]
public sealed class RekallAgeShadowSettingsComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 1, Maximum = 4)]
    public int CascadeCount { get; init; } = 3;

    [RekallAgeProperty(Minimum = 128, Maximum = 16384)]
    public int AtlasResolution { get; init; } = 2048;

    [RekallAgeProperty(Minimum = 0.01)]
    public double MaximumDistance { get; init; } = 100;

    [RekallAgeProperty(AllowedValues = ["uniform", "logarithmic", "practical"])]
    public string SplitPolicy { get; init; } = "practical";

    [RekallAgeProperty(Minimum = 0)]
    public double Bias { get; init; } = 0.001;

    [RekallAgeProperty(Minimum = 0)]
    public double NormalBias { get; init; } = 0.01;

    [RekallAgeProperty(AllowedValues = ["hard", "pcf", "pcss"])]
    public string Filter { get; init; } = "pcf";

    [RekallAgeProperty]
    public bool Stabilization { get; init; } = true;
}

[RekallAgeComponent(
    "Fog Volume",
    Description = "Authors a global or transform-bounded participating-media volume. Transform3D supplies the box or sphere position, orientation, and extents.")]
public sealed class RekallAgeFogVolumeComponent : RekallAgeComponent
{
    [RekallAgeProperty(AllowedValues = ["global", "box", "sphere"])]
    public string Shape { get; init; } = "global";

    [RekallAgeProperty(Minimum = 0, Maximum = 64)]
    public double Density { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Albedo { get; init; } = "#ffffff";

    [RekallAgeProperty(Kind = "color")]
    public string Emission { get; init; } = "#000000";

    [RekallAgeProperty(Minimum = -0.95, Maximum = 0.95)]
    public double Anisotropy { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 64)]
    public double HeightFalloff { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double BlendDistance { get; init; }

    [RekallAgeProperty]
    public int Priority { get; init; }
}

[RekallAgeComponent(
    "Particle Emitter 3D",
    Description = "Authors a deterministic generic 3D particle emitter. Transform3D supplies its pose; curves, simulation, rendering, and quality-priority facts remain inspectable scene data.")]
public sealed class RekallAgeParticleEmitter3DComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty(Description = "Optional semantic role used by agents and diagnostics; it does not create engine-owned behavior.")]
    public string? Role { get; init; }

    [RekallAgeProperty(AllowedValues = ["world", "local"])]
    public string SimulationSpace { get; init; } = "world";

    [RekallAgeProperty(Minimum = 1)]
    public int Capacity { get; init; } = 1024;

    [RekallAgeProperty(Minimum = 0)]
    public double SpawnRate { get; init; }

    [RekallAgeProperty]
    public object[] Bursts { get; init; } = [];

    [RekallAgeProperty(Minimum = 0.001)]
    public double Lifetime { get; init; } = 1;

    [RekallAgeProperty]
    public uint Seed { get; init; } = 1;

    [RekallAgeProperty]
    public JsonObject VelocityDirection { get; init; } = new();

    [RekallAgeProperty(Minimum = 0, Maximum = 180)]
    public double VelocityConeDegrees { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double MinimumSpeed { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double MaximumSpeed { get; init; }

    [RekallAgeProperty]
    public JsonObject Gravity { get; init; } = new();

    [RekallAgeProperty(Minimum = 0)]
    public double Drag { get; init; }

    [RekallAgeProperty]
    public object[] SizeCurve { get; init; } = [];

    [RekallAgeProperty]
    public object[] ColorCurve { get; init; } = [];

    [RekallAgeProperty(AllowedValues = ["quad", "mesh", "ribbon"])]
    public string DrawMode { get; init; } = "quad";

    [RekallAgeProperty]
    public bool Lit { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double EmissiveIntensity { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0)]
    public double SoftParticleFade { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? Texture { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? TextureAssetId { get; init; }

    [RekallAgeProperty(Minimum = 1)]
    public int FlipbookColumns { get; init; } = 1;

    [RekallAgeProperty(Minimum = 1)]
    public int FlipbookRows { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0)]
    public double FlipbookFramesPerSecond { get; init; }

    [RekallAgeProperty(AllowedValues = ["alpha", "add", "premultiplied"])]
    public string BlendMode { get; init; } = "alpha";

    [RekallAgeProperty]
    public int Priority { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double VisibilityDistance { get; init; } = double.MaxValue;
}

[RekallAgeComponent("XR Rig")]
public sealed class RekallAgeXrRigComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string TrackingSpace { get; init; } = "local-floor";

    [RekallAgeProperty]
    public string ViewConfiguration { get; init; } = "primary-stereo";
}

[RekallAgeComponent("XR Pose Source")]
public sealed class RekallAgeXrPoseSourceComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string Source { get; init; } = "head";

    [RekallAgeProperty]
    public bool ApplyPosition { get; init; } = true;

    [RekallAgeProperty]
    public bool ApplyRotation { get; init; } = true;
}

[RekallAgeComponent("XR Controller")]
public sealed class RekallAgeXrControllerComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string Hand { get; init; } = "left";

    [RekallAgeProperty]
    public string PoseSource { get; init; } = "left-hand";
}

[RekallAgeComponent("Directional Light", Description = "Emits directional light. Put its orientation on a separate Rekall.Transform3D component on the same entity; this component configures light intensity and color only.")]
public sealed class RekallAgeDirectionalLightComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double Intensity { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffff";
}

[RekallAgeComponent("Point Light", Description = "Emits a point light. Put its position on a separate Rekall.Transform3D component on the same entity; this component configures light intensity and color only.")]
public sealed class RekallAgePointLightComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double Intensity { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffff";

    [RekallAgeProperty(Minimum = 0.001)]
    public double Range { get; init; } = 10;

    [RekallAgeProperty]
    public int Priority { get; init; }

    [RekallAgeProperty]
    public bool CastShadows { get; init; }

    [RekallAgeProperty]
    public int ShadowPriority { get; init; }
}

[RekallAgeComponent("Spot Light", Description = "Emits a cone-shaped light. Put its position and facing direction on a separate Rekall.Transform3D component on the same entity (the cone points along the transform's forward axis, the same convention Rekall.DirectionalLight uses); this component configures intensity, color, range, and cone angles only.")]
public sealed class RekallAgeSpotLightComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double Intensity { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffff";

    [RekallAgeProperty(Minimum = 0.001)]
    public double Range { get; init; } = 10;

    [RekallAgeProperty(Minimum = 0, Maximum = 89)]
    public double InnerConeAngle { get; init; } = 20;

    [RekallAgeProperty(Minimum = 0.001, Maximum = 89)]
    public double OuterConeAngle { get; init; } = 30;

    [RekallAgeProperty]
    public int Priority { get; init; }

    [RekallAgeProperty]
    public bool CastShadows { get; init; }

    [RekallAgeProperty]
    public int ShadowPriority { get; init; }
}

[RekallAgeComponent("Multiplayer Session")]
public sealed class RekallAgeMultiplayerSessionComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Role { get; init; } = "server";

    [RekallAgeProperty]
    public string Authority { get; init; } = "server";

    [RekallAgeProperty(Minimum = 1, Maximum = 240)]
    public int TickRate { get; init; } = 60;

    [RekallAgeProperty(Minimum = 1, Maximum = 240)]
    public int SnapshotRate { get; init; } = 20;

    [RekallAgeProperty(Minimum = 1)]
    public int MaxPlayers { get; init; } = 8;

    [RekallAgeProperty]
    public string Transport { get; init; } = "loopback";

    [RekallAgeProperty]
    public string Address { get; init; } = "127.0.0.1";

    [RekallAgeProperty(Minimum = 1, Maximum = 65535)]
    public int Port { get; init; } = 7777;

    [RekallAgeProperty]
    public bool ClientPrediction { get; init; } = true;

    [RekallAgeProperty(Minimum = 0)]
    public int InterpolationDelayMilliseconds { get; init; } = 100;
}

[RekallAgeComponent("Network Identity")]
public sealed class RekallAgeNetworkIdentityComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string NetworkId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string OwnerClientId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string Authority { get; init; } = "server";
}

[RekallAgeComponent("Network Transform")]
public sealed class RekallAgeNetworkTransformComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool ReplicatePosition { get; init; } = true;

    [RekallAgeProperty]
    public bool ReplicateRotation { get; init; } = true;

    [RekallAgeProperty]
    public bool ReplicateScale { get; init; } = true;

    [RekallAgeProperty]
    public string Prediction { get; init; } = "interpolated";

    [RekallAgeProperty(Minimum = 0)]
    public int Priority { get; init; } = 0;
}

[RekallAgeComponent("Geometry Primitive", Description = "Engine-local 3D primitive geometry centered at the entity transform. Cube, sphere, cylinder, and cone use their conventional local axes. The plane primitive lies on the local XZ plane and its normal points toward +Y; rotate it 90 degrees around local X for an XY backdrop facing a camera along the Z axis, or use a camera-plane facing mode where supported.")]
public sealed class RekallAgeGeometryPrimitiveComponent : RekallAgeComponent
{
    [RekallAgeProperty(Description = "Primitive kind. A plane is centered on local XZ with +Y normal; Transform3D ScaleX and ScaleZ size its surface, while rotation controls which direction it faces.")]
    public string Primitive { get; init; } = "cube";

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#8ab4f8";
}

[RekallAgeComponent("Geometry Mesh")]
public sealed class RekallAgeGeometryMeshComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "geometryVertices")]
    public RekallAgeGeometryMeshVertex[] Vertices { get; init; } =
    [
        new(0, 0, 0),
        new(1, 0, 0),
        new(0, 1, 0)
    ];

    [RekallAgeProperty(Kind = "geometryIndices")]
    public ushort[] Indices { get; init; } = [0, 1, 2];

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#8ab4f8";
}

[RekallAgeComponent("Mesh Asset Reference", Description = "References a persistent editable mesh asset by stable logical ID. Rendering, picking, export, and physics consume the same compiled snapshot; expectedRevision optionally pins an exact immutable file revision.")]
public sealed class RekallAgeMeshAssetReferenceComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "assetRef", AssetKind = "mesh")]
    public string AssetId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string? ExpectedRevision { get; init; }
}

[RekallAgeComponent("Model Asset Reference", Description = "References a published Model Asset by stable logical ID. The asset manifest selects an immutable compiled revision so scene entities keep a durable authoring link while runtime systems resolve validated geometry.")]
public sealed class RekallAgeModelAssetReferenceComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "assetRef", AssetKind = "model")]
    public string AssetId { get; init; } = string.Empty;
}

public sealed record RekallAgeGeometryMeshVertex(
    double X,
    double Y,
    double Z,
    double NormalX = 0,
    double NormalY = 1,
    double NormalZ = 0,
    double R = double.NaN,
    double G = double.NaN,
    double B = double.NaN,
    double A = double.NaN,
    double U = 0,
    double V = 0);

[RekallAgeComponent("Line Segments")]
public sealed class RekallAgeLineSegmentsComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "lineSegments")]
    public RekallAgeLineSegment[] Segments { get; init; } =
    [
        new(0, 0, 0, 1, 0, 0)
    ];

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Thickness { get; init; } = 0.02;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#33ddff";
}

public sealed record RekallAgeLineSegment(
    double FromX,
    double FromY,
    double FromZ,
    double ToX,
    double ToY,
    double ToZ);

[RekallAgeComponent("Geometry Extrusion")]
public sealed class RekallAgeGeometryExtrusionComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "geometryProfile")]
    public RekallAgeGeometryProfilePoint[] Profile { get; init; } =
    [
        new(-0.5, -0.5),
        new(0.5, -0.5),
        new(0.5, 0.5),
        new(-0.5, 0.5)
    ];

    [RekallAgeProperty(Minimum = 0.001)]
    public double Depth { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#8ab4f8";
}

public sealed record RekallAgeGeometryProfilePoint(double X, double Y);

[RekallAgeComponent("Material")]
public sealed class RekallAgeMaterialComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "color")]
    public string BaseColor { get; init; } = "#ffffff";

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? BaseColorTexture { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double MetallicFactor { get; init; }

    [RekallAgeProperty(Minimum = 0.04, Maximum = 1)]
    public double RoughnessFactor { get; init; } = 1;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? MetallicRoughnessTexture { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? NormalTexture { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 4)]
    public double NormalScale { get; init; } = 1;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? OcclusionTexture { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double OcclusionStrength { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string EmissiveColor { get; init; } = "#000000";

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? EmissiveTexture { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double EmissiveStrength { get; init; }

    [RekallAgeProperty(AllowedValues = ["opaque", "blend"], Description = "\"opaque\" (the default) draws this surface fully solid. \"blend\" makes it translucent: the surface's fragment shader controls per-pixel opacity through its own output alpha (an unmodified built-in shader reads BaseColorTexture/BaseColor's own alpha channel), and the surface draws through the engine's real alpha-blended transparent render pass instead of the opaque one - use this for glass, water, or any custom shader that needs to see through to what is behind it.")]
    public string AlphaMode { get; init; } = "opaque";

    [RekallAgeProperty(Minimum = 0, Maximum = 1, Description = "Alpha threshold used only in a future \"mask\" alpha mode; currently unused by \"opaque\"/\"blend\". Reserved for cutout materials (foliage, chain-link) so authoring it now doesn't require a schema change later.")]
    public double AlphaCutoff { get; init; } = 0.5;
}

[RekallAgeComponent("Procedural Material")]
public sealed class RekallAgeProceduralMaterialComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Generator { get; init; } = "checker";

    [RekallAgeProperty(Minimum = 2, Maximum = 2048)]
    public int Resolution { get; init; } = 128;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Scale { get; init; } = 8;

    [RekallAgeProperty]
    public int Seed { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string BaseColorA { get; init; } = "#ffffff";

    [RekallAgeProperty(Kind = "color")]
    public string BaseColorB { get; init; } = "#202020";

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double MetallicFactor { get; init; }

    [RekallAgeProperty(Minimum = 0.04, Maximum = 1)]
    public double RoughnessA { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.04, Maximum = 1)]
    public double RoughnessB { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0, Maximum = 4)]
    public double NormalStrength { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double EmissiveStrength { get; init; }
}

[RekallAgeComponent("LOD Group")]
public sealed class RekallAgeLodGroupComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty(Kind = "lodLevels")]
    public RekallAgeLodLevel[] Levels { get; init; } =
    [
        new(0, 50, Primitive: "cube"),
        new(50, null, Primitive: "plane")
    ];
}

public sealed record RekallAgeLodLevel(
    double MinDistance = 0,
    double? MaxDistance = null,
    string? Mesh = null,
    string? AssetId = null,
    string? Primitive = null,
    string? TextureAssetId = null,
    string? MaterialColor = null,
    double ScaleMultiplier = 1);

[RekallAgeComponent("Virtual Geometry")]
public sealed class RekallAgeVirtualGeometryComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty(Minimum = 0.001)]
    public double TargetPixelError { get; init; } = 1;

    [RekallAgeProperty(Minimum = 1)]
    public int ClusterTriangleCount { get; init; } = 128;

    [RekallAgeProperty(Minimum = 0)]
    public int MaxSelectedTriangles { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public int MaxLodLevel { get; init; } = 8;

    [RekallAgeProperty]
    public string DebugMode { get; init; } = "off";
}

[RekallAgeComponent("Physics World 3D", Description = "Configures the BEPU rigid-body world. Solver iteration and substep counts trade CPU cost for contact, stack, and bouncy-material stability.")]
public sealed class RekallAgePhysicsWorld3DComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public double GravityX { get; init; }

    [RekallAgeProperty]
    public double GravityY { get; init; } = -9.81;

    [RekallAgeProperty]
    public double GravityZ { get; init; }

    [RekallAgeProperty(Minimum = 1, Maximum = 32, Description = "Velocity solver iterations per substep. Increase for difficult constraints or deep stacks.")]
    public int VelocityIterationCount { get; init; } = 4;

    [RekallAgeProperty(Minimum = 1, Maximum = 16, Description = "BEPU solver/integration substeps per fixed runtime tick. Higher values improve fast contacts and contact-spring bounce at additional CPU cost.")]
    public int SubstepCount { get; init; } = 4;
}

[RekallAgeComponent("Physics Material 3D", Description = "Defines per-collidable BEPU contact response. Restitution is implemented with BEPU contact springs rather than post-solve velocity or position correction.")]
public sealed class RekallAgePhysicsMaterial3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double Friction { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0, Maximum = 1, Description = "Requested impact bounciness from 0 to 1. AGE maps this to BEPU contact-spring damping for impacts above MinimumBounceSpeed.")]
    public double Restitution { get; init; }

    [RekallAgeProperty(Minimum = 0, Description = "Minimum relative contact speed that activates the restitution response. Slower resting contacts use DampingRatio so stacks can settle.")]
    public double MinimumBounceSpeed { get; init; } = 0.5;

    [RekallAgeProperty(Minimum = 0, Description = "Maximum BEPU penetration-recovery speed in world units per second.")]
    public double MaximumRecoveryVelocity { get; init; } = 2;

    [RekallAgeProperty(Minimum = 0.0001, Description = "BEPU contact-spring frequency. Lower frequencies preserve more bounce; higher frequencies make contacts firmer but require more substeps.")]
    public double SpringFrequency { get; init; } = 30;

    [RekallAgeProperty(Minimum = 0, Description = "BEPU damping ratio used for non-bouncy and resting contacts: 0 is undamped, 1 is critically damped, and values above 1 are overdamped.")]
    public double DampingRatio { get; init; } = 1;
}

[RekallAgeComponent("Physics Material 2D", Description = "Defines per-collidable BEPU contact response for a planar 2D body. Restitution is implemented with BEPU contact springs rather than post-solve velocity or position correction.")]
public sealed class RekallAgePhysicsMaterial2DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double Friction { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0, Maximum = 1, Description = "Requested impact bounciness from 0 to 1. AGE maps this to BEPU contact-spring damping for impacts above MinimumBounceSpeed.")]
    public double Restitution { get; init; }

    [RekallAgeProperty(Minimum = 0, Description = "Minimum relative contact speed that activates the restitution response. Slower resting contacts use DampingRatio so stacks can settle.")]
    public double MinimumBounceSpeed { get; init; } = 0.5;

    [RekallAgeProperty(Minimum = 0, Description = "Maximum BEPU penetration-recovery speed in world units per second.")]
    public double MaximumRecoveryVelocity { get; init; } = 2;

    [RekallAgeProperty(Minimum = 0.0001, Description = "BEPU contact-spring frequency. Lower frequencies preserve more bounce; higher frequencies make contacts firmer but require more substeps.")]
    public double SpringFrequency { get; init; } = 30;

    [RekallAgeProperty(Minimum = 0, Description = "BEPU damping ratio used for non-bouncy and resting contacts: 0 is undamped, 1 is critically damped, and values above 1 are overdamped.")]
    public double DampingRatio { get; init; } = 1;
}

[RekallAgeComponent("Ball Socket Joint", Description = "Pins one local anchor point on this dynamic body to one local anchor point on another dynamic body, free to rotate. Both entities must have a rigid body and collider. ConnectedEntityId must reference a different, existing, dynamic entity.")]
public sealed class RekallAgeBallSocketJointComponent : RekallAgeComponent
{
    [RekallAgeProperty(Description = "Entity ID of the other dynamic body this joint connects to.")]
    public string ConnectedEntityId { get; init; } = string.Empty;

    [RekallAgeProperty(Description = "Anchor point X, in this entity's own local space.")]
    public double AnchorAX { get; init; }

    [RekallAgeProperty(Description = "Anchor point Y, in this entity's own local space.")]
    public double AnchorAY { get; init; }

    [RekallAgeProperty(Description = "Anchor point Z, in this entity's own local space.")]
    public double AnchorAZ { get; init; }

    [RekallAgeProperty(Description = "Anchor point X, in the connected entity's own local space.")]
    public double AnchorBX { get; init; }

    [RekallAgeProperty(Description = "Anchor point Y, in the connected entity's own local space.")]
    public double AnchorBY { get; init; }

    [RekallAgeProperty(Description = "Anchor point Z, in the connected entity's own local space.")]
    public double AnchorBZ { get; init; }

    [RekallAgeProperty(Minimum = 0.0001, Description = "BEPU joint-spring frequency. Lower frequencies allow more stretch before correction; higher frequencies hold the anchor points together more rigidly.")]
    public double SpringFrequency { get; init; } = 30;

    [RekallAgeProperty(Minimum = 0, Description = "BEPU damping ratio: 0 is undamped, 1 is critically damped, values above 1 are overdamped.")]
    public double DampingRatio { get; init; } = 1;
}

[RekallAgeComponent("Hinge Joint", Description = "Pins one local anchor point on this dynamic body to one local anchor point on another dynamic body, and constrains relative rotation to one shared axis. The authored axis is a world-space direction at bind time, converted into each body's own local frame automatically - the two bodies do not need to share an orientation (a wheel rotated to align its own collider shape with the spin axis works correctly). Both entities must have a rigid body and collider. ConnectedEntityId must reference a different, existing, dynamic entity.")]
public sealed class RekallAgeHingeJointComponent : RekallAgeComponent
{
    [RekallAgeProperty(Description = "Entity ID of the other dynamic body this joint connects to.")]
    public string ConnectedEntityId { get; init; } = string.Empty;

    [RekallAgeProperty(Description = "Anchor point X, in this entity's own local space.")]
    public double AnchorAX { get; init; }

    [RekallAgeProperty(Description = "Anchor point Y, in this entity's own local space.")]
    public double AnchorAY { get; init; }

    [RekallAgeProperty(Description = "Anchor point Z, in this entity's own local space.")]
    public double AnchorAZ { get; init; }

    [RekallAgeProperty(Description = "Anchor point X, in the connected entity's own local space.")]
    public double AnchorBX { get; init; }

    [RekallAgeProperty(Description = "Anchor point Y, in the connected entity's own local space.")]
    public double AnchorBY { get; init; }

    [RekallAgeProperty(Description = "Anchor point Z, in the connected entity's own local space.")]
    public double AnchorBZ { get; init; }

    [RekallAgeProperty(Description = "Hinge rotation axis X, a world-space direction at bind time - converted into each body's own local frame automatically, so the two bodies do not need to share an orientation.")]
    public double AxisX { get; init; }

    [RekallAgeProperty(Description = "Hinge rotation axis Y, a world-space direction at bind time - converted into each body's own local frame automatically, so the two bodies do not need to share an orientation.")]
    public double AxisY { get; init; } = 1;

    [RekallAgeProperty(Description = "Hinge rotation axis Z, a world-space direction at bind time - converted into each body's own local frame automatically, so the two bodies do not need to share an orientation.")]
    public double AxisZ { get; init; }

    [RekallAgeProperty(Minimum = 0.0001, Description = "BEPU joint-spring frequency for the pinned anchor point.")]
    public double SpringFrequency { get; init; } = 30;

    [RekallAgeProperty(Minimum = 0, Description = "BEPU damping ratio: 0 is undamped, 1 is critically damped, values above 1 are overdamped.")]
    public double DampingRatio { get; init; } = 1;

    [RekallAgeProperty(Description = "Optional continuous motor: target relative angular velocity around Axis, in authored degrees per second, driven by the solver every frame (not an external velocity override). Ignored when MotorMaximumTorque is 0.")]
    public double MotorTargetVelocity { get; init; }

    [RekallAgeProperty(Minimum = 0, Description = "Maximum torque the motor may apply to reach MotorTargetVelocity. 0 (the default) disables the motor entirely, leaving the hinge a passive pin+axis constraint - set this above 0 to drive a wheel, door, or turntable continuously without fighting the hinge's own constraint solving the way externally overwriting a body's angular velocity every frame does.")]
    public double MotorMaximumTorque { get; init; }

    [RekallAgeProperty(Description = "Optional lower bound, in authored degrees, on relative rotation around Axis from the bodies' bind-time relative orientation. Ignored unless AngleLimitMaximum is also greater than AngleLimitMinimum.")]
    public double AngleLimitMinimum { get; init; }

    [RekallAgeProperty(Description = "Optional upper bound, in authored degrees, on relative rotation around Axis from the bodies' bind-time relative orientation. Ignored unless greater than AngleLimitMinimum - a door, turret, or ragdoll joint that should only swing within a range, rather than spin freely.")]
    public double AngleLimitMaximum { get; init; }
}

[RekallAgeComponent("Distance Joint", Description = "Keeps this dynamic body's center and another dynamic body's center at an authored target distance apart, like a rigid rod or taut rope. Both entities must have a rigid body and collider. ConnectedEntityId must reference a different, existing, dynamic entity. When DistanceLimitMinimum/DistanceLimitMaximum are both authored (Maximum greater than Minimum), the joint instead allows any distance within that range, like a leash or slack chain, and TargetDistance is ignored.")]
public sealed class RekallAgeDistanceJointComponent : RekallAgeComponent
{
    [RekallAgeProperty(Description = "Entity ID of the other dynamic body this joint connects to.")]
    public string ConnectedEntityId { get; init; } = string.Empty;

    [RekallAgeProperty(Minimum = 0, Description = "Target distance in world units between the two entities' centers. Ignored when DistanceLimitMinimum/DistanceLimitMaximum author a valid range instead.")]
    public double TargetDistance { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001, Description = "BEPU joint-spring frequency. Lower frequencies allow more stretch before correction; higher frequencies hold the target distance (or, in range mode, the limit itself) more rigidly.")]
    public double SpringFrequency { get; init; } = 30;

    [RekallAgeProperty(Minimum = 0, Description = "BEPU damping ratio: 0 is undamped, 1 is critically damped, values above 1 are overdamped.")]
    public double DampingRatio { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0, Description = "Optional lower bound, in world units, on distance between the two entities' centers. Ignored unless DistanceLimitMaximum is also greater than this value.")]
    public double DistanceLimitMinimum { get; init; }

    [RekallAgeProperty(Minimum = 0, Description = "Optional upper bound, in world units, on distance between the two entities' centers. Ignored unless greater than DistanceLimitMinimum - when authored, switches this joint from a fixed TargetDistance to a free-within-range leash.")]
    public double DistanceLimitMaximum { get; init; }
}

[RekallAgeComponent("Weld Joint", Description = "Rigidly locks this dynamic body's position and orientation relative to another dynamic body, as if welded together - the two move as one rigid assembly. Both entities must have a rigid body and collider. ConnectedEntityId must reference a different, existing, dynamic entity.")]
public sealed class RekallAgeWeldJointComponent : RekallAgeComponent
{
    [RekallAgeProperty(Description = "Entity ID of the other dynamic body this joint connects to.")]
    public string ConnectedEntityId { get; init; } = string.Empty;

    [RekallAgeProperty(Description = "This entity's local-space offset from the connected entity's center, held fixed by the weld.")]
    public double LocalOffsetX { get; init; }

    [RekallAgeProperty(Description = "This entity's local-space offset from the connected entity's center, held fixed by the weld.")]
    public double LocalOffsetY { get; init; }

    [RekallAgeProperty(Description = "This entity's local-space offset from the connected entity's center, held fixed by the weld.")]
    public double LocalOffsetZ { get; init; }

    [RekallAgeProperty(Description = "Fixed relative orientation held by the weld, authored as degrees around X, applied X then Y then Z.")]
    public double LocalOrientationX { get; init; }

    [RekallAgeProperty(Description = "Fixed relative orientation held by the weld, authored as degrees around Y, applied X then Y then Z.")]
    public double LocalOrientationY { get; init; }

    [RekallAgeProperty(Description = "Fixed relative orientation held by the weld, authored as degrees around Z, applied X then Y then Z.")]
    public double LocalOrientationZ { get; init; }

    [RekallAgeProperty(Minimum = 0.0001, Description = "BEPU joint-spring frequency. Lower frequencies allow more give before correction; higher frequencies hold the weld more rigidly.")]
    public double SpringFrequency { get; init; } = 30;

    [RekallAgeProperty(Minimum = 0, Description = "BEPU damping ratio: 0 is undamped, 1 is critically damped, values above 1 are overdamped.")]
    public double DampingRatio { get; init; } = 1;
}

[RekallAgeComponent("Fixed Joint", Description = "Pins one local anchor point on this dynamic body to a fixed point in world space - not to another entity. Use this for a chain anchored to the world, a swinging sign bolted to a static wall position, or any joint that needs a truly immovable end rather than a second dynamic body. Requires a rigid body and collider on the same entity.")]
public sealed class RekallAgeFixedJointComponent : RekallAgeComponent
{
    [RekallAgeProperty(Description = "World-space X the anchor point is pinned to.")]
    public double AnchorX { get; init; }

    [RekallAgeProperty(Description = "World-space Y the anchor point is pinned to.")]
    public double AnchorY { get; init; }

    [RekallAgeProperty(Description = "World-space Z the anchor point is pinned to.")]
    public double AnchorZ { get; init; }

    [RekallAgeProperty(Description = "Anchor point X, in this entity's own local space.")]
    public double LocalOffsetX { get; init; }

    [RekallAgeProperty(Description = "Anchor point Y, in this entity's own local space.")]
    public double LocalOffsetY { get; init; }

    [RekallAgeProperty(Description = "Anchor point Z, in this entity's own local space.")]
    public double LocalOffsetZ { get; init; }

    [RekallAgeProperty(Minimum = 0.0001, Description = "BEPU joint-spring frequency. Lower frequencies allow more stretch before correction; higher frequencies hold the anchor point more rigidly.")]
    public double SpringFrequency { get; init; } = 30;

    [RekallAgeProperty(Minimum = 0, Description = "BEPU damping ratio: 0 is undamped, 1 is critically damped, values above 1 are overdamped.")]
    public double DampingRatio { get; init; } = 1;
}

[RekallAgeComponent("Rigidbody 3D", Description = "Makes an entity a dynamic 3D physics body. Requires a matching Transform3D and 3D collider on the same entity. For static geometry, use a collider without a rigid body. Initial linear velocity is in world units per second; initial angular velocity is in authored degrees per second.")]
public sealed class RekallAgeRigidbody3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Mass { get; init; } = 1;

    [RekallAgeProperty]
    public double LinearVelocityX { get; init; }

    [RekallAgeProperty]
    public double LinearVelocityY { get; init; }

    [RekallAgeProperty]
    public double LinearVelocityZ { get; init; }

    [RekallAgeProperty]
    public double AngularVelocityX { get; init; }

    [RekallAgeProperty]
    public double AngularVelocityY { get; init; }

    [RekallAgeProperty]
    public double AngularVelocityZ { get; init; }

    [RekallAgeProperty(Minimum = 0, Description = "Linear drag coefficient: each frame, linear velocity is scaled by 1/(1 + LinearDrag * deltaSeconds) - a simple, framerate-stable air-resistance approximation applied on top of BEPU's own gravity/contact/joint solving, not a replacement for it. 0 (the default) disables drag entirely, matching prior behavior.")]
    public double LinearDrag { get; init; }

    [RekallAgeProperty(Minimum = 0, Description = "Angular drag coefficient: each frame, angular velocity is scaled by 1/(1 + AngularDrag * deltaSeconds), the same framerate-stable approximation LinearDrag uses. 0 (the default) disables it entirely, matching prior behavior.")]
    public double AngularDrag { get; init; }
}

[RekallAgeComponent("Sprite Renderer", Description = "Projects a texture or sprite asset as visible 2D runtime content.")]
public sealed class RekallAgeSpriteRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? Sprite { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? AssetId { get; init; }

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Mesh Renderer", Description = "Projects a mesh, model, or engine geometry identifier as visible 3D runtime content.")]
public sealed class RekallAgeMeshRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "assetRef", AssetKind = "model")]
    public string? Mesh { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "model")]
    public string? AssetId { get; init; }

    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string? VertexShader { get; init; }

    [RekallAgeProperty]
    public string? FragmentShader { get; init; }

    [RekallAgeProperty]
    public bool CastShadows { get; init; } = true;

    [RekallAgeProperty]
    public bool ReceiveShadows { get; init; } = true;
}

[RekallAgeComponent("Rigidbody 2D", Description = "Makes an entity a dynamic planar body simulated on the XY plane. Requires Transform2D and a 2D collider on the same entity. For static geometry, use a collider without a rigid body.")]
public sealed class RekallAgeRigidbody2DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Mass { get; init; } = 1;

    [RekallAgeProperty(Description = "Initial angular velocity around the 2D plane's normal axis, in authored degrees per second.")]
    public double AngularVelocityZ { get; init; }
}

[RekallAgeComponent("Trigger", Description = "A generic overlap volume whose radius or box dimensions are explicit world-unit values. Transform2D/3D supplies position and rotation; transform scale does not resize the trigger.")]
public sealed class RekallAgeTriggerComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string Shape { get; init; } = "sphere";

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Width { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Height { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Depth { get; init; } = 1;

    [RekallAgeProperty]
    public string TargetTag { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string TargetComponentType { get; init; } = string.Empty;
}

[RekallAgeComponent("Collision Filter", Description = "Restricts which collidables this entity's collider/trigger physically interacts with and generates collision/trigger events against. An entity with no Rekall.CollisionFilter, or an empty/absent collidesWith, interacts with every layer (default, zero-authoring-change behavior).")]
public sealed class RekallAgeCollisionFilterComponent : RekallAgeComponent
{
    [RekallAgeProperty(Description = "The layer name this entity's collidable belongs to.")]
    public string Layer { get; init; } = "default";

    [RekallAgeProperty(Description = "Native JSON array of layer names this entity's collidable is allowed to interact with. Pass a native array, never an encoded string. Absent/empty means it interacts with every layer.")]
    public string[]? CollidesWith { get; init; }
}

[RekallAgeComponent("Box Collider 2D", Description = "A planar box collision shape with explicit world-unit width and height. Transform2D supplies position and rotation; transform scale does not resize the collider. Add Rigidbody2D for a dynamic body or omit it for a static surface.")]
public sealed class RekallAgeBoxCollider2DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Width { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Height { get; init; } = 1;
}

[RekallAgeComponent("Circle Collider 2D", Description = "A planar circle collision shape with an explicit world-unit radius. Transform2D supplies position and rotation; transform scale does not resize the collider. Add Rigidbody2D for a dynamic body or omit it for a static surface.")]
public sealed class RekallAgeCircleCollider2DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 0.5;
}

[RekallAgeComponent("Box Collider 3D", Description = "A 3D box collision shape with explicit world-unit dimensions. Transform3D supplies position and orientation; transform scale does not resize the collider. Add Rigidbody3D for a dynamic body or omit it for static geometry.")]
public sealed class RekallAgeBoxCollider3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Width { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Height { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Depth { get; init; } = 1;
}

[RekallAgeComponent("Sphere Collider 3D", Description = "A 3D sphere collision shape with an explicit world-unit radius. Transform3D supplies position and orientation; transform scale does not resize the collider. Add Rigidbody3D for a dynamic body or omit it for static geometry.")]
public sealed class RekallAgeSphereCollider3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 0.5;
}

[RekallAgeComponent("Capsule Collider 3D", Description = "A 3D capsule collision shape with explicit world-unit radius and length. Transform3D supplies position and orientation; transform scale does not resize the collider. Add Rigidbody3D for a dynamic body or omit it for static geometry.")]
public sealed class RekallAgeCapsuleCollider3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 0.5;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Length { get; init; } = 1;
}

[RekallAgeComponent("Mesh Collider", Description = "Uses same-entity Rekall.GeometryMesh vertices as explicit local world-unit geometry. Transform3D supplies position and orientation; transform scale does not resize the collider. Static meshes are supported; dynamic meshes require Convex=true.")]
public sealed class RekallAgeMeshColliderComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Convex { get; init; }
}

[RekallAgeComponent("Destructible", Description = "Generic procedural destruction: setting Triggered removes this entity and replaces it with one dynamic rigid-body entity per pre-authored chunk mesh in ChunkMeshAssetIds, given an outward impulse from this entity's position. If TerrainEntityId references another entity with a mesh, a crater is stamped into that terrain's editable mesh asset. Set Triggered from any game module (a health check, a grenade timer, anything) - this component knows nothing about a specific game.")]
public sealed class RekallAgeDestructibleComponent : RekallAgeComponent
{
    [RekallAgeProperty(Description = "Set true to detonate this entity on the current frame.")]
    public bool Triggered { get; init; }

    [RekallAgeProperty(Description = "Native JSON array of pre-authored chunk mesh asset IDs; one dynamic rigid-body entity is spawned per entry.")]
    public string[] ChunkMeshAssetIds { get; init; } = [];

    [RekallAgeProperty(Minimum = 0, Description = "Outward impulse magnitude applied to each spawned chunk.")]
    public double ExplosionImpulse { get; init; } = 6;

    [RekallAgeProperty(Description = "Optional entity ID of a terrain entity whose editable mesh receives a crater stamp when this entity detonates.")]
    public string? TerrainEntityId { get; init; }

    [RekallAgeProperty(Minimum = 0.0001, Description = "Crater stamp radius in world units, used only when TerrainEntityId is set.")]
    public double CraterRadius { get; init; } = 2;

    [RekallAgeProperty(Minimum = 0.0001, Description = "Crater stamp depth in world units, used only when TerrainEntityId is set.")]
    public double CraterDepth { get; init; } = 1;
}

[RekallAgeComponent("Planet Renderer")]
public sealed class RekallAgePlanetRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 1;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? SurfaceTexture { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? HeightTexture { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? NormalTexture { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? WaterTexture { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 4)]
    public double WaterCoverage { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0, Maximum = 8)]
    public double WaterSpecularStrength { get; init; } = 2.5;

    [RekallAgeProperty(Minimum = 0.01, Maximum = 1)]
    public double WaterRoughness { get; init; } = 0.06;

    [RekallAgeProperty(Minimum = 0, Maximum = 512)]
    public int MeshSlices { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 256)]
    public int MeshStacks { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#4b86d8";
}

[RekallAgeComponent("Cloud Layer Renderer")]
public sealed class RekallAgeCloudLayerRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "cloudLayers")]
    public object? Layers { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double Height { get; init; } = 0.02;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? Texture { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffff";

    [RekallAgeProperty(Kind = "boolean")]
    public bool AlphaFromTextureOnly { get; init; } = true;

    [RekallAgeProperty(Minimum = 0, Maximum = 4)]
    public double Coverage { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double LambertianStrength { get; init; } = 0.45;

    [RekallAgeProperty(Minimum = 0, Maximum = 2)]
    public double AmbientStrength { get; init; } = 0.18;

    [RekallAgeProperty(Kind = "boolean")]
    public bool CastShadows { get; init; } = true;

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double ShadowStrength { get; init; } = 0.35;
}

[RekallAgeComponent("Atmosphere Renderer")]
public sealed class RekallAgeAtmosphereRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double Height { get; init; } = 0.08;

    [RekallAgeProperty(Kind = "boolean")]
    public bool RenderShell { get; init; } = true;

    [RekallAgeProperty(Kind = "color")]
    public string RayleighColor { get; init; } = "#7fb6ff";

    [RekallAgeProperty(Minimum = 0)]
    public double Density { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.001)]
    public double DensityFalloff { get; init; } = 0.18;

    [RekallAgeProperty(Minimum = 0)]
    public double RayleighScattering { get; init; } = 0.006;

    [RekallAgeProperty(Minimum = 0)]
    public double MieScattering { get; init; } = 0.002;

    [RekallAgeProperty(Minimum = -0.99, Maximum = 0.99)]
    public double MieAnisotropy { get; init; } = 0.76;

    [RekallAgeProperty(Kind = "color")]
    public string MieColor { get; init; } = "#ffffff";

    [RekallAgeProperty(Kind = "color")]
    public string OzoneAbsorptionColor { get; init; } = "#ffd199";

    [RekallAgeProperty(Minimum = 0)]
    public double OzoneAbsorption { get; init; } = 0;

    [RekallAgeProperty(Minimum = 0, Maximum = 2)]
    public double AerialPerspectiveStrength { get; init; } = 0.38;

    [RekallAgeProperty(Minimum = 0)]
    public double SunIntensity { get; init; } = 22;

    [RekallAgeProperty(Minimum = 0)]
    public double Exposure { get; init; } = 1.2;

    [RekallAgeProperty(Minimum = 4, Maximum = 32)]
    public int ViewSampleCount { get; init; } = 16;

    [RekallAgeProperty(Minimum = 2, Maximum = 16)]
    public int LightSampleCount { get; init; } = 8;
}

[RekallAgeComponent("Celestial Body")]
public sealed class RekallAgeCelestialBodyComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string BodyId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string Type { get; init; } = "PlanetaryBody";

    [RekallAgeProperty]
    public string? ParentBodyId { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double MeanRadiusKm { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double MassKg { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#8f98a8";
}

[RekallAgeComponent("Kepler Orbit")]
public sealed class RekallAgeKeplerOrbitComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string ParentBodyId { get; init; } = string.Empty;

    [RekallAgeProperty(Minimum = 0)]
    public double SemiMajorAxisKm { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 0.999999)]
    public double Eccentricity { get; init; }

    [RekallAgeProperty]
    public double InclinationDegrees { get; init; }

    [RekallAgeProperty]
    public double LongitudeOfAscendingNodeDegrees { get; init; }

    [RekallAgeProperty]
    public double ArgumentOfPeriapsisDegrees { get; init; }

    [RekallAgeProperty]
    public double TimeAtPeriapsisSeconds { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double PeriodSeconds { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double DistanceScale { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0)]
    public double TimeScale { get; init; } = 1;
}

[RekallAgeComponent("Celestial Rotation")]
public sealed class RekallAgeCelestialRotationComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double SiderealPeriodSeconds { get; init; }

    [RekallAgeProperty]
    public bool TidallyLocked { get; init; }

    [RekallAgeProperty]
    public double TiltDegrees { get; init; }

    [RekallAgeProperty]
    public double AzimuthDegrees { get; init; }

    [RekallAgeProperty]
    public double InitialLongitudeDegrees { get; init; }

    [RekallAgeProperty]
    public bool Retrograde { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double TimeScale { get; init; } = 1;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Orbit Path Renderer")]
public sealed class RekallAgeOrbitPathRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 8, Maximum = 512)]
    public int Segments { get; init; } = 128;

    [RekallAgeProperty(Minimum = 0.001)]
    public double Thickness { get; init; } = 0.035;

    [RekallAgeProperty]
    public double VerticalOffset { get; init; } = -0.05;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#88aaff";

    [RekallAgeProperty(Minimum = 0)]
    public double EmissiveStrength { get; init; } = 1.4;

    [RekallAgeProperty]
    public string Layer { get; init; } = string.Empty;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Ring Renderer")]
public sealed class RekallAgeRingRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double InnerRadius { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double OuterRadius { get; init; } = 2;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? Texture { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffffcc";

    [RekallAgeProperty(Minimum = 16, Maximum = 512)]
    public int Segments { get; init; } = 192;
}

[RekallAgeComponent("Starfield Renderer")]
public sealed class RekallAgeStarfieldRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 1, Maximum = 8000)]
    public int Count { get; init; } = 1200;

    [RekallAgeProperty(Minimum = 1)]
    public double Radius { get; init; } = 18000;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Size { get; init; } = 2.5;

    [RekallAgeProperty]
    public int Seed { get; init; } = 1337;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#dce8ffff";

    [RekallAgeProperty(Minimum = 0, Maximum = 16)]
    public double Brightness { get; init; } = 2.2;

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double MilkyWayStrength { get; init; } = 0.35;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Grass Renderer")]
public sealed class RekallAgeGrassRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 1, Maximum = 20000)]
    public int BladeCount { get; init; } = 4000;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double BladeHeight { get; init; } = 0.35;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double BladeWidth { get; init; } = 0.05;

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double HeightJitter { get; init; } = 0.35;

    [RekallAgeProperty(Minimum = 0, Maximum = 90)]
    public double MaxSlopeDegrees { get; init; } = 35;

    [RekallAgeProperty(Minimum = 0)]
    public double WindStrength { get; init; } = 0.12;

    [RekallAgeProperty(Minimum = 0)]
    public double WindSpeed { get; init; } = 1.6;

    [RekallAgeProperty]
    public double WindDirectionX { get; init; } = 1;

    [RekallAgeProperty]
    public double WindDirectionZ { get; init; } = 0.3;

    [RekallAgeProperty]
    public int Seed { get; init; } = 4242;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#3f6a2eff";

    [RekallAgeProperty(Kind = "color")]
    public string TipColor { get; init; } = "#8fbf52ff";
}

[RekallAgeComponent("Marker Renderer")]
public sealed class RekallAgeMarkerRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Size { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffffcc";

    [RekallAgeProperty(Minimum = 0)]
    public double EmissiveStrength { get; init; } = 2;

    [RekallAgeProperty]
    public double VerticalOffset { get; init; }

    [RekallAgeProperty]
    public string Layer { get; init; } = string.Empty;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Halo Renderer")]
public sealed class RekallAgeHaloRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 1;

    [RekallAgeProperty(Minimum = 8, Maximum = 256)]
    public int Segments { get; init; } = 48;

    [RekallAgeProperty(Minimum = 1, Maximum = 16)]
    public int Rings { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.1, Maximum = 8)]
    public double Falloff { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffff88";

    [RekallAgeProperty(Minimum = 0)]
    public double Intensity { get; init; } = 1;

    [RekallAgeProperty]
    public double VerticalOffset { get; init; }

    [RekallAgeProperty]
    public string FacingMode { get; init; } = "world";

    [RekallAgeProperty]
    public string Layer { get; init; } = string.Empty;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Post Process Stack")]
public sealed class RekallAgePostProcessStackComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "boolean")]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty(Kind = "postProcessPasses")]
    public object? Passes { get; init; }
}

[RekallAgeComponent("Text Label Renderer")]
public sealed class RekallAgeTextLabelRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Text { get; init; } = "Label";

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Size { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Thickness { get; init; } = 0.02;

    [RekallAgeProperty(Minimum = 0)]
    public double MinimumScreenHeightPixels { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#dce8ffff";

    [RekallAgeProperty]
    public double OffsetX { get; init; }

    [RekallAgeProperty]
    public double OffsetY { get; init; }

    [RekallAgeProperty]
    public double OffsetZ { get; init; }

    [RekallAgeProperty]
    public string FacingMode { get; init; } = "world";

    [RekallAgeProperty]
    public string Layer { get; init; } = string.Empty;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}
