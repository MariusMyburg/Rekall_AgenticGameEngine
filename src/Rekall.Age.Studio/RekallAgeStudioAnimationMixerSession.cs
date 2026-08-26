using System.Text.Json.Nodes;
using Rekall.Age.World;

namespace Rekall.Age.Studio;

/// <summary>One <c>Rekall.AnimationMixer</c> layer, editable as plain text fields for Studio binding.</summary>
public sealed class RekallAgeStudioAnimationMixerLayerModel
{
    public RekallAgeStudioAnimationMixerLayerModel(string name, string clip, string weight, string loopMode, string speed)
    {
        Name = name; Clip = clip; Weight = weight; LoopMode = loopMode; Speed = speed;
    }

    public string Name { get; set; }
    public string Clip { get; set; }
    public string Weight { get; set; }
    public string LoopMode { get; set; }
    public string Speed { get; set; }

    public bool TryBuildJson(out JsonObject? layer, out string? error)
    {
        layer = null; error = null;
        if (string.IsNullOrWhiteSpace(Name)) { error = "Layer name is required."; return false; }
        if (string.IsNullOrWhiteSpace(Clip)) { error = $"Layer '{Name}' requires a clip asset id."; return false; }
        if (!double.TryParse(Weight, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var weight) || !double.IsFinite(weight))
        {
            error = $"Layer '{Name}' weight must be a finite number."; return false;
        }
        var speedText = string.IsNullOrWhiteSpace(Speed) ? "1" : Speed;
        if (!double.TryParse(speedText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speed) || !double.IsFinite(speed) || speed <= 0)
        {
            error = $"Layer '{Name}' speed must be a finite positive number."; return false;
        }
        layer = new JsonObject
        {
            ["name"] = Name.Trim(),
            ["clip"] = Clip.Trim(),
            ["weight"] = weight,
            ["loopMode"] = string.IsNullOrWhiteSpace(LoopMode) ? "loop" : LoopMode.Trim(),
            ["speed"] = speed
        };
        return true;
    }
}

/// <summary>
/// Opens the authored <c>Rekall.AnimationMixer</c> component on one scene entity and applies edits
/// to its layer list through the same revision-safe scene document pipeline every other Studio
/// scene edit uses. This edits the entity's *authored initial state* (what the scene starts with);
/// gameplay modules may still overwrite the mixer every frame at runtime, exactly as authored
/// Transform/other component values can be overwritten by gameplay logic once play starts.
/// </summary>
public sealed class RekallAgeStudioAnimationMixerSession
{
    private const string ComponentType = "Rekall.AnimationMixer";
    private readonly RekallAgeSceneStore _store;

    public RekallAgeStudioAnimationMixerSession(RekallAgeSceneStore? store = null) => _store = store ?? new RekallAgeSceneStore();

    public string? ProjectRoot { get; private set; }
    public string? SceneName { get; private set; }
    public string? EntityId { get; private set; }
    public string? EntityName { get; private set; }
    public string? FileRevision { get; private set; }
    public bool HasMixer { get; private set; }
    public IReadOnlyList<RekallAgeStudioAnimationMixerLayerModel> Layers { get; private set; } = [];

    public async ValueTask OpenAsync(string projectRoot, string sceneName, string entityId, CancellationToken cancellationToken)
    {
        var loaded = await _store.LoadVersionedAsync(projectRoot, sceneName, cancellationToken).ConfigureAwait(false);
        var entity = loaded.Value.Entities.SingleOrDefault(item => item.Id.Equals(entityId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Entity '{entityId}' does not exist in scene '{sceneName}'.", nameof(entityId));
        ProjectRoot = System.IO.Path.GetFullPath(projectRoot);
        SceneName = sceneName;
        EntityId = entityId;
        EntityName = entity.Name;
        FileRevision = loaded.Revision;
        var component = entity.Components.FirstOrDefault(item => item.Type.Equals(ComponentType, StringComparison.Ordinal));
        HasMixer = component is not null;
        Layers = component is null ? [] : ParseLayers(component.Properties);
        _scene = loaded.Value;
    }

    private RekallAgeSceneDocument? _scene;

    public async ValueTask ApplyAsync(
        IReadOnlyList<RekallAgeStudioAnimationMixerLayerModel> editedLayers,
        CancellationToken cancellationToken)
    {
        if (_scene is null || ProjectRoot is null || SceneName is null || EntityId is null || FileRevision is null || !HasMixer)
            throw new InvalidOperationException("Open an entity with an existing Rekall.AnimationMixer component before applying layer edits.");
        ArgumentNullException.ThrowIfNull(editedLayers);
        if (editedLayers.Count == 0)
            throw new InvalidOperationException("An animation mixer requires at least one layer.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var jsonLayers = new JsonArray();
        foreach (var layer in editedLayers)
        {
            if (!layer.TryBuildJson(out var json, out var error)) throw new InvalidOperationException(error);
            if (!names.Add(json!["name"]!.GetValue<string>()))
                throw new InvalidOperationException($"Layer name '{layer.Name}' is duplicated; layer names must be unique.");
            jsonLayers.Add(json);
        }

        var updatedScene = _scene.UpdateEntity(EntityId, entity => entity.UpdateComponent(ComponentType, component =>
        {
            var properties = (JsonObject)component.Properties.DeepClone();
            properties["layers"] = jsonLayers;
            return component with { Properties = properties };
        }));
        var afterRevision = await _store.SaveIfRevisionAsync(ProjectRoot, updatedScene, FileRevision, cancellationToken).ConfigureAwait(false);
        _scene = updatedScene;
        FileRevision = afterRevision;
        Layers = editedLayers;
    }

    private static IReadOnlyList<RekallAgeStudioAnimationMixerLayerModel> ParseLayers(JsonObject properties)
    {
        if (properties["layers"] is not JsonArray array) return [];
        var result = new List<RekallAgeStudioAnimationMixerLayerModel>();
        foreach (var node in array)
        {
            if (node is not JsonObject layer) continue;
            result.Add(new RekallAgeStudioAnimationMixerLayerModel(
                layer["name"]?.GetValue<string>() ?? string.Empty,
                layer["clip"]?.GetValue<string>() ?? string.Empty,
                (layer["weight"]?.GetValue<double>() ?? 1).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                layer["loopMode"]?.GetValue<string>() ?? "loop",
                (layer["speed"]?.GetValue<double>() ?? 1).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        }
        return result;
    }
}
