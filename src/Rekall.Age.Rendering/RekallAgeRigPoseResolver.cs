using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeRigPoseResolution(
    RekallAgeRuntimeViewportSkin? Skin,
    string? IssueCode = null,
    string? IssueMessage = null);

public sealed class RekallAgeRigPoseResolver
{
    private readonly RekallAgeRigAssetStore _store = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, (long Length, long Timestamp, RekallAgeRigAsset Asset)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RekallAgeRigPoseResolution Resolve(string? projectRoot, RekallAgeRuntimeComponent? component)
    {
        if (component is null)
            return new(null);
        try
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                return new(null, "REKALL_RIG_PROJECT_ROOT_MISSING", "A project root is required to resolve a native rig pose.");
            var assetId = ReadString(component.Properties, "assetId");
            if (string.IsNullOrWhiteSpace(assetId))
                return new(null, "REKALL_RIG_ASSET_ID_MISSING", "Rekall.RigPose requires an assetId.");
            var rig = LoadCached(projectRoot, assetId);
            var deltas = ReadDeltas(component.Properties);
            var evaluated = new RekallAgeRigEvaluator().Evaluate(rig, deltas);
            var skinIndex = Math.Max(0, (int)Math.Round(ReadNumber(component.Properties, "skinIndex", 0)));
            return new(new(skinIndex, evaluated.JointMatrices));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Text.Json.JsonException)
        {
            var code = error.Message.Split(':', 2)[0];
            if (!code.StartsWith("REKALL_", StringComparison.Ordinal))
                code = "REKALL_RIG_POSE_RESOLUTION_FAILED";
            return new(null, code, error.Message);
        }
    }

    private RekallAgeRigAsset LoadCached(string projectRoot, string assetId)
    {
        var path = _store.GetRigPath(projectRoot, assetId);
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"REKALL_RIG_ASSET_NOT_FOUND: Rig asset '{assetId}' does not exist.", path);
        var timestamp = info.LastWriteTimeUtc.Ticks;
        lock (_gate)
        {
            if (_cache.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Timestamp == timestamp)
                return cached.Asset;
            var asset = _store.Load(projectRoot, assetId);
            _cache[path] = (info.Length, timestamp, asset);
            return asset;
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<double>> ReadDeltas(JsonObject properties)
    {
        var result = new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase);
        if (!TryGet(properties, "jointDeltas", out var value) || value is null)
            return result;
        if (value is not JsonArray { Count: <= 4_096 } items)
            throw new InvalidDataException("REKALL_RIG_POSE_DELTAS_INVALID: jointDeltas must be an array of at most 4096 objects.");
        foreach (var item in items)
        {
            if (item is not JsonObject joint)
                throw new InvalidDataException("REKALL_RIG_POSE_DELTAS_INVALID: Each pose delta must be an object.");
            var id = ReadString(joint, "jointId");
            if (string.IsNullOrWhiteSpace(id) || !TryGet(joint, "matrix", out var matrixNode) || matrixNode is not JsonArray { Count: 16 } matrix)
                throw new InvalidDataException("REKALL_RIG_POSE_DELTAS_INVALID: Each pose delta requires jointId and a 16-number matrix.");
            var values = matrix.Select(node => TryReadFiniteNumber(node, out var number) ? number : double.NaN).ToArray();
            if (values.Any(value => !double.IsFinite(value)) || !result.TryAdd(id, values))
                throw new InvalidDataException($"REKALL_RIG_POSE_DELTAS_INVALID: Pose delta '{id}' is duplicate or non-finite.");
        }
        return result;
    }

    private static string? ReadString(JsonObject properties, string name) =>
        TryGet(properties, name, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text) ? text?.Trim() : null;

    private static double ReadNumber(JsonObject properties, string name, double fallback) =>
        TryGet(properties, name, out var node) && TryReadFiniteNumber(node, out var number) ? number : fallback;

    private static bool TryReadFiniteNumber(JsonNode? node, out double number)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out number)) return double.IsFinite(number);
            if (value.TryGetValue<float>(out var single)) { number = single; return float.IsFinite(single); }
            if (value.TryGetValue<int>(out var integer)) { number = integer; return true; }
            if (value.TryGetValue<long>(out var longInteger)) { number = longInteger; return true; }
            if (value.TryGetValue<decimal>(out var decimalNumber)) { number = (double)decimalNumber; return double.IsFinite(number); }
        }
        number = 0;
        return false;
    }

    private static bool TryGet(JsonObject properties, string name, out JsonNode? value)
    {
        var match = properties.FirstOrDefault(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        value = match.Value;
        return match.Key is not null;
    }
}
