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

[RekallAgeComponent("Transform 3D", Description = "Generic three-dimensional position, Euler rotation in degrees, and scale.")]
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
    [RekallAgeProperty(Kind = "animationTracks", Description = "Array of {component, property, interpolation, keys:[{time,value}]} objects. Component must be the exact fully qualified runtime type, for example {component:\"Rekall.Transform3D\", property:\"X\", interpolation:\"linear\", keys:[{time:0,value:0},{time:1,value:6}]}. Interpolation is step, linear, or smoothstep; values may be scalar, vector-array, color, or string.")]
    public object[] Tracks { get; init; } = [];
    [RekallAgeProperty(Kind = "animationEvents", Description = "Array of {time,name,payload?} marker objects emitted as animation.event facts.")]
    public object[] Events { get; init; } = [];
}

[RekallAgeComponent("Animation Player", Description = "Executes an inline Rekall.AnimationClip or reusable animation catalog asset.")]
public sealed class RekallAgeAnimationPlayerComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "assetRef", AssetKind = "animation", Description = "Optional catalog id; omit when an inline Rekall.AnimationClip is present.")]
    public string Clip { get; init; } = string.Empty;
    [RekallAgeProperty] public bool Playing { get; init; } = true;
    [RekallAgeProperty] public double Speed { get; init; } = 1;
    [RekallAgeProperty(AllowedValues = ["clamp", "loop", "pingpong"])]
    public string LoopMode { get; init; } = "loop";
    [RekallAgeProperty(Minimum = 0)] public double StartTimeSeconds { get; init; }
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
