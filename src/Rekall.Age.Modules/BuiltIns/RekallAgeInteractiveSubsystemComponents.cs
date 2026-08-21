namespace Rekall.Age.Modules.BuiltIns;

[RekallAgeComponent("Transform 2D", Description = "Generic two-dimensional position, rotation, and scale.")]
public sealed class RekallAgeTransform2DComponent : RekallAgeComponent
{
    [RekallAgeProperty] public double X { get; init; }
    [RekallAgeProperty] public double Y { get; init; }
    [RekallAgeProperty] public double Rotation { get; init; }
    [RekallAgeProperty] public double ScaleX { get; init; } = 1;
    [RekallAgeProperty] public double ScaleY { get; init; } = 1;
}

[RekallAgeComponent("Transform 3D", Description = "Generic right-handed three-dimensional position, Euler rotation in degrees, and scale. Unrotated local forward is +Z, right is +X, and up is +Y. Pitch rotates around X, yaw around Y, and roll around Z; positive pitch looks downward from +Z. A camera positioned at positive Z and aimed toward lower Z normally needs yaw 180 degrees.")]
public sealed class RekallAgeTransform3DComponent : RekallAgeComponent
{
    [RekallAgeProperty] public double X { get; init; }
    [RekallAgeProperty] public double Y { get; init; }
    [RekallAgeProperty] public double Z { get; init; }
    [RekallAgeProperty] public double Pitch { get; init; }
    [RekallAgeProperty] public double Yaw { get; init; }
    [RekallAgeProperty] public double Roll { get; init; }
    [RekallAgeProperty] public double ScaleX { get; init; } = 1;
    [RekallAgeProperty] public double ScaleY { get; init; } = 1;
    [RekallAgeProperty] public double ScaleZ { get; init; } = 1;
}

[RekallAgeComponent("Audio Listener", Description = "Marks an entity transform as the active spatial-audio listener.")]
public sealed class RekallAgeAudioListenerComponent : RekallAgeComponent
{
    [RekallAgeProperty] public bool Active { get; init; } = true;
}

[RekallAgeComponent("Audio Emitter", Description = "Plays a catalog audio asset through a named bus with optional generic spatial attenuation.")]
public sealed class RekallAgeAudioEmitterComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "assetRef", AssetKind = "audio", Description = "Catalog id of the imported PCM WAV clip.")]
    public string Clip { get; init; } = string.Empty;

    [RekallAgeProperty] public bool Playing { get; init; }
    [RekallAgeProperty] public bool PlayOnStart { get; init; } = true;
    [RekallAgeProperty] public bool Loop { get; init; }
    [RekallAgeProperty(Minimum = 0, Maximum = 4)] public double Gain { get; init; } = 1;
    [RekallAgeProperty(Minimum = 0.01, Maximum = 4)] public double Pitch { get; init; } = 1;
    [RekallAgeProperty] public string Bus { get; init; } = "master";
    [RekallAgeProperty] public bool Spatial { get; init; }
    [RekallAgeProperty(Minimum = 0)] public double ReferenceDistance { get; init; } = 1;
    [RekallAgeProperty(Minimum = 0)] public double MaxDistance { get; init; } = 100;
}

[RekallAgeComponent("Audio Bus", Description = "Defines gain and mute state for a named audio mix bus.")]
public sealed class RekallAgeAudioBusComponent : RekallAgeComponent
{
    [RekallAgeProperty] public string Name { get; init; } = "master";
    [RekallAgeProperty(Minimum = 0, Maximum = 4)] public double Gain { get; init; } = 1;
    [RekallAgeProperty] public bool Muted { get; init; }
}

[RekallAgeComponent("Animation Clip", Description = "Versioned reusable timeline data. Tracks target component/property pairs and contain time/value keys.")]
public sealed class RekallAgeAnimationClipComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 1, Maximum = 1)] public int Version { get; init; } = 1;
    [RekallAgeProperty(Minimum = 0.00001)] public double DurationSeconds { get; init; } = 1;
    [RekallAgeProperty(Kind = "animationTracks", Description = "Array of {component, property, interpolation, keys} objects. Component is the exact fully qualified runtime type, for example {component:\"Rekall.Transform3D\", property:\"X\", interpolation:\"linear\", keys:[{time:0,value:0},{time:1,value:6}]}. Interpolation is step, linear, smooth/smoothstep, or cubic. Cubic keys have {time,value,inTangent,outTangent}; tangents are derivatives in value units per second. Cubic values are finite scalars, flat numeric arrays of 1 to 16 components, or RGB/RGBA colors; tangents must match the scalar/array/channel shape. Other modes also support strings through step fallback. Runtime limits are 1,024 tracks per clip and 4,096 keys per track. Unknown modes and malformed cubic data fail closed.")]
    public object[] Tracks { get; init; } = [];
    [RekallAgeProperty(Kind = "animationEvents", Description = "Array of {time,name,payload?} marker objects emitted as animation.event facts. Runtime limit is 4,096 markers per clip.")]
    public object[] Events { get; init; } = [];
}

[RekallAgeComponent("Animation Player", Description = "Executes animation timeline data. For an inline clip, add a separate Rekall.AnimationClip component to the same entity and leave Clip empty; Clip itself accepts only a reusable animation catalog id string.")]
public sealed class RekallAgeAnimationPlayerComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "assetRef", AssetKind = "animation", Description = "Optional animation catalog id string. Never embed a clip object here; leave empty when a separate Rekall.AnimationClip component is present on the entity.")]
    public string Clip { get; init; } = string.Empty;
    [RekallAgeProperty] public bool Playing { get; init; } = true;
    [RekallAgeProperty] public double Speed { get; init; } = 1;
    [RekallAgeProperty(AllowedValues = ["clamp", "loop", "pingpong"])]
    public string LoopMode { get; init; } = "loop";
    [RekallAgeProperty(Minimum = 0)] public double StartTimeSeconds { get; init; }
}

[RekallAgeComponent("Animation Mixer", Description = "Blends reusable animation clips over generic component/property tracks. Layer weights can move toward targetWeight over fadeSeconds for deterministic cross-fades.")]
public sealed class RekallAgeAnimationMixerComponent : RekallAgeComponent
{
    [RekallAgeProperty] public bool Playing { get; init; } = true;
    [RekallAgeProperty(Kind = "animationLayers", Description = "Array of {name,clip,weight,targetWeight,fadeSeconds,playing,speed,loopMode,startTimeSeconds}. Clip is a reusable animation catalog id. Override layers are normalized per targeted property; non-interpolable values use the highest-weight layer. Runtime limit is 32 layers.")]
    public object[] Layers { get; init; } = [];
}

[RekallAgeComponent("Animation State Graph", Description = "Selects and cross-fades reusable animation clips through bounded, inspectable parameters and ordered transitions. Agent-authored modules own parameter meaning and updates; the engine never supplies game-specific states or decisions.")]
public sealed class RekallAgeAnimationStateGraphComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 1, Maximum = 1)] public int Version { get; init; } = 1;
    [RekallAgeProperty] public bool Playing { get; init; } = true;
    [RekallAgeProperty(Description = "Required declared state name used when no valid runtime state exists. Names are ordinal and at most 128 characters.")]
    public string InitialState { get; init; } = string.Empty;
    [RekallAgeProperty(Kind = "animationGraphParameters", Description = "Object of at most 128 agent-authored finite number, boolean, or string parameters. Strings are at most 1,024 characters. Modules update these generic values from game facts.")]
    public object Parameters { get; init; } = new();
    [RekallAgeProperty(Kind = "animationGraphStates", Description = "Array of at most 64 {name,clip,speed,loopMode,startTimeSeconds} states. Clip is a reusable animation catalog id; loopMode is clamp, loop, or pingpong.")]
    public object[] States { get; init; } = [];
    [RekallAgeProperty(Kind = "animationGraphTransitions", Description = "Ordered array of at most 256 {from,to,durationSeconds,resetTime,conditions} transitions with at most 16 conditions each. Conditions use equals, notEquals, greater, greaterOrEqual, less, or lessOrEqual. Exact-state transitions precede '*' any-state transitions.")]
    public object[] Transitions { get; init; } = [];
}

[RekallAgeComponent("Skeletal Animator", Description = "Samples a named animation and skin from an imported GLB model into an inspectable joint pose.")]
public sealed class RekallAgeSkeletalAnimatorComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "assetRef", AssetKind = "model", Description = "Catalog id of an imported GLB model containing a skin and animation channels.")]
    public string Model { get; init; } = string.Empty;
    [RekallAgeProperty(Description = "Animation name from the imported GLB metadata. Empty selects the first animation.")]
    public string Animation { get; init; } = string.Empty;
    [RekallAgeProperty(Minimum = 0)] public int SkinIndex { get; init; }
    [RekallAgeProperty] public bool Playing { get; init; } = true;
    [RekallAgeProperty] public double Speed { get; init; } = 1;
    [RekallAgeProperty(AllowedValues = ["clamp", "loop", "pingpong"])] public string LoopMode { get; init; } = "loop";
    [RekallAgeProperty(Minimum = 0)] public double StartTimeSeconds { get; init; }
}

[RekallAgeComponent("Morph Weights", Description = "Supplies a complete generic morph-target weight array for the Rekall.MeshRenderer on the same entity. The existing AnimationClip, AnimationMixer, cubic interpolation, and AnimationStateGraph contracts can animate Weights.")]
public sealed class RekallAgeMorphWeightsComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "morphWeights", Description = "Array of 1 to 64 finite numbers with absolute value at most 1,000,000. Values are not clamped to 0..1. The array is a complete override and must match the imported asset target count; omit this component to use imported defaults. AnimationClip and AnimationMixer tracks may target Rekall.MorphWeights.Weights.")]
    public double[] Weights { get; init; } = [];
}

[RekallAgeComponent("UI Canvas", Description = "Defines a resolution-independent reference canvas and draw layer.")]
public sealed class RekallAgeUiCanvasComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 1)] public double ReferenceWidth { get; init; } = 1920;
    [RekallAgeProperty(Minimum = 1)] public double ReferenceHeight { get; init; } = 1080;
    [RekallAgeProperty] public int Layer { get; init; }
    [RekallAgeProperty(AllowedValues = ["none", "horizontal", "vertical"])] public string LayoutDirection { get; init; } = "none";
    [RekallAgeProperty(Minimum = 0)] public double Gap { get; init; }
    [RekallAgeProperty(Minimum = 0)] public double PaddingLeft { get; init; }
    [RekallAgeProperty(Minimum = 0)] public double PaddingTop { get; init; }
    [RekallAgeProperty(Minimum = 0)] public double PaddingRight { get; init; }
    [RekallAgeProperty(Minimum = 0)] public double PaddingBottom { get; init; }
}

public abstract class RekallAgeUiElementBase : RekallAgeComponent
{
    [RekallAgeProperty] public double X { get; init; }
    [RekallAgeProperty] public double Y { get; init; }
    [RekallAgeProperty(Minimum = 0)] public double Width { get; init; } = 100;
    [RekallAgeProperty(Minimum = 0)] public double Height { get; init; } = 30;
    [RekallAgeProperty] public double AnchorMinX { get; init; }
    [RekallAgeProperty] public double AnchorMinY { get; init; }
    [RekallAgeProperty] public double AnchorMaxX { get; init; }
    [RekallAgeProperty] public double AnchorMaxY { get; init; }
    [RekallAgeProperty] public double PivotX { get; init; }
    [RekallAgeProperty] public double PivotY { get; init; }
    [RekallAgeProperty] public string Text { get; init; } = string.Empty;
    [RekallAgeProperty(Kind = "color")] public string BackgroundColor { get; init; } = "#00000000";
    [RekallAgeProperty(Kind = "color")] public string ForegroundColor { get; init; } = "#ffffff";
    [RekallAgeProperty(Kind = "color")] public string BorderColor { get; init; } = "#00000000";
    [RekallAgeProperty(Minimum = 0)] public double BorderWidth { get; init; }
    [RekallAgeProperty(Minimum = 1)] public double FontSize { get; init; } = 16;
    [RekallAgeProperty(Kind = "assetRef", AssetKind = "image")] public string AssetId { get; init; } = string.Empty;
    [RekallAgeProperty] public bool Interactive { get; init; }
    [RekallAgeProperty(AllowedValues = ["none", "horizontal", "vertical"])] public string LayoutDirection { get; init; } = "none";
    [RekallAgeProperty] public int LayoutOrder { get; init; }
    [RekallAgeProperty(Minimum = 0)] public double Gap { get; init; }
    [RekallAgeProperty(Minimum = 0)] public double PaddingLeft { get; init; }
    [RekallAgeProperty(Minimum = 0)] public double PaddingTop { get; init; }
    [RekallAgeProperty(Minimum = 0)] public double PaddingRight { get; init; }
    [RekallAgeProperty(Minimum = 0)] public double PaddingBottom { get; init; }
    [RekallAgeProperty(AllowedValues = ["start", "center", "end", "stretch"])] public string HorizontalAlignment { get; init; } = "start";
    [RekallAgeProperty(AllowedValues = ["start", "center", "end", "stretch"])] public string VerticalAlignment { get; init; } = "start";
    [RekallAgeProperty(Description = "Deterministic semantic focus order; lower values focus first.")]
    public int NavigationOrder { get; init; }
}

[RekallAgeComponent("UI Element", Description = "Generic layout, visual, and optional interaction primitive.")]
public sealed class RekallAgeUiElementComponent : RekallAgeUiElementBase
{
}

[RekallAgeComponent("Panel", Description = "UI container or background visual.")]
public sealed class RekallAgePanelComponent : RekallAgeUiElementBase
{
}

[RekallAgeComponent("Label", Description = "UI text visual using deterministic engine text metrics.")]
public sealed class RekallAgeLabelComponent : RekallAgeUiElementBase
{
}

[RekallAgeComponent("Image", Description = "UI image visual backed by an imported image asset.")]
public sealed class RekallAgeImageComponent : RekallAgeUiElementBase
{
}

[RekallAgeComponent("Button", Description = "Focusable pointer-interactive UI element; bind pointer.click and ui.focus using Rekall.EventBindings.")]
public sealed class RekallAgeButtonComponent : RekallAgeUiElementBase
{
}
