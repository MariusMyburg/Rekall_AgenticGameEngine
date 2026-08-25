using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeModifierStackEvaluator
{
    private readonly RekallAgeModifierCatalog _catalog = RekallAgeModifierCatalog.CreateDefault();
    private readonly RekallAgeModifierStackValidator _validator = new();
    private readonly RekallAgeMeshOperationExecutor _executor = new();
    private readonly Dictionary<string, RekallAgeMeshAsset> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastKeys = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ValueTask<RekallAgeModifierStackEvaluationReport> EvaluateAsync(
        RekallAgeModifierStackAsset stack,
        RekallAgeMeshAsset source,
        RekallAgeModelingEvaluationBudget budget,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(Evaluate(stack, source, budget, cancellationToken));

    private RekallAgeModifierStackEvaluationReport Evaluate(RekallAgeModifierStackAsset stack, RekallAgeMeshAsset source,
        RekallAgeModelingEvaluationBudget budget, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(stack); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(budget);
        var stopwatch = Stopwatch.StartNew(); var reports = new List<RekallAgeModifierEvaluationItem>();
        var diagnostics = _validator.Validate(stack);
        if (diagnostics.Count > 0) return Failure(stack, stopwatch, reports, diagnostics);
        if (stack.Modifiers.Count(item => item.Enabled) > budget.MaximumEvaluatedNodes)
            return Failure(stack, stopwatch, reports, [Error("REKALL_MODIFIER_BUDGET_NODE_LIMIT", "Enabled modifier count exceeds the evaluation budget.")]);
        var current = source; var dependencyKey = Hash(JsonSerializer.Serialize(source, RekallAgeModelingJson.Options));
        foreach (var modifier in stack.Modifiers.Where(item => item.Enabled))
        {
            token.ThrowIfCancellationRequested();
            if (stopwatch.ElapsedMilliseconds > budget.MaximumMilliseconds)
                return Failure(stack, stopwatch, reports, [Error("REKALL_MODIFIER_BUDGET_TIME_LIMIT", "Modifier evaluation exceeded its wall-time budget.", modifier.ModifierId)]);
            var key = Hash(dependencyKey + "\0" + modifier.TypeId + "@" + modifier.TypeVersion + "\0" + Canonical(modifier.Parameters));
            var identity = stack.AssetId + "|" + modifier.ModifierId;
            bool invalidated; string? previous;
            lock (_gate) { _lastKeys.TryGetValue(identity, out previous); invalidated = previous is not null && previous != key; }
            var step = Stopwatch.StartNew(); RekallAgeMeshAsset output; bool hit;
            lock (_gate) hit = _cache.TryGetValue(key, out output!);
            if (!hit)
            {
                try { output = Execute(current, modifier); }
                catch (RekallAgeMeshOperationException error) { return Failure(stack, stopwatch, reports, [Error(error.Code, error.Message, modifier.ModifierId)]); }
                lock (_gate) _cache[key] = output;
            }
            lock (_gate) _lastKeys[identity] = key;
            var validation = new RekallAgeMeshValidator().Validate(output);
            if (validation.Summary.PointCount > budget.MaximumPoints || validation.Summary.FaceCount > budget.MaximumFaces)
                return Failure(stack, stopwatch, reports, [Error("REKALL_MODIFIER_BUDGET_GEOMETRY_LIMIT", "Modifier output exceeds point or face budget.", modifier.ModifierId)]);
            reports.Add(new(modifier.ModifierId, modifier.TypeId, key, hit, invalidated, step.Elapsed.TotalMilliseconds,
                validation.Summary.PointCount, validation.Summary.FaceCount, hit ? RekallAgeMeshChangeKind.None : _catalog.Find(modifier.TypeId, modifier.TypeVersion)!.PossibleChanges));
            current = output; dependencyKey = key;
        }
        return new(true, stack.AssetId, stack.Revision, current, reports.Count, reports.Count(item => item.CacheHit),
            reports.Count(item => item.Invalidated), reports, stopwatch.Elapsed.TotalMilliseconds, []);
    }

    private RekallAgeMeshAsset Execute(RekallAgeMeshAsset source, RekallAgeModifierInstance modifier)
    {
        var selection = ReadString(modifier.Parameters, "selection", string.Empty);
        return modifier.TypeId switch
        {
            "rekall.modifier.transform" => _executor.Execute(source, new("transform", RekallAgeGeometryDomain.Point,
                Select(source, RekallAgeGeometryDomain.Point, selection), VectorParameters(modifier.Parameters))).Mesh,
            "rekall.modifier.triangulate" => _executor.Execute(source, new("triangulate_faces", RekallAgeGeometryDomain.Face,
                Select(source, RekallAgeGeometryDomain.Face, selection), new JsonObject())).Mesh,
            "rekall.modifier.extrude" => _executor.Execute(source, new("extrude_faces", RekallAgeGeometryDomain.Face,
                Select(source, RekallAgeGeometryDomain.Face, selection), VectorParameters(modifier.Parameters))).Mesh,
            "rekall.modifier.subdivide" => _executor.Execute(source, new("subdivide_faces", RekallAgeGeometryDomain.Face,
                Select(source, RekallAgeGeometryDomain.Face, selection), new JsonObject())).Mesh,
            "rekall.modifier.subdivide_smooth" => ExecuteSmoothSubdivision(source, modifier.Parameters),
            "rekall.modifier.merge_by_distance" => _executor.Execute(source, new("merge_by_distance", RekallAgeGeometryDomain.Point,
                Select(source, RekallAgeGeometryDomain.Point, selection), new JsonObject { ["distance"] = ReadNumber(modifier.Parameters, "distance", 0.0001) })).Mesh,
            "rekall.modifier.bevel" => _executor.Execute(source, new("bevel_edges", RekallAgeGeometryDomain.Edge,
                Select(source, RekallAgeGeometryDomain.Edge, selection), new JsonObject
                {
                    ["width"] = ReadNumber(modifier.Parameters, "width", 0.05), ["segments"] = ReadInteger(modifier.Parameters, "segments", 1),
                    ["profile"] = ReadNumber(modifier.Parameters, "profile", 0.5), ["clampOverlap"] = ReadBoolean(modifier.Parameters, "clampOverlap", true),
                    ["hardenNormals"] = ReadBoolean(modifier.Parameters, "hardenNormals", false)
                })).Mesh,
            "rekall.modifier.solidify" => _executor.Execute(source, new("solidify", RekallAgeGeometryDomain.Face,
                Select(source, RekallAgeGeometryDomain.Face, selection), new JsonObject
                {
                    ["thickness"] = ReadNumber(modifier.Parameters, "thickness", 0.05), ["offset"] = ReadNumber(modifier.Parameters, "offset", 0),
                    ["rim"] = ReadBoolean(modifier.Parameters, "rim", true), ["evenThickness"] = ReadBoolean(modifier.Parameters, "evenThickness", true)
                })).Mesh,
            "rekall.modifier.mirror" => RekallAgeModelingGraphEvaluator.ExecuteMirrorModifier(source, new JsonObject
                {
                    ["axis"] = ReadString(modifier.Parameters, "axis", "x"), ["origin"] = ReadNumber(modifier.Parameters, "origin", 0),
                    ["mergeDistance"] = ReadNumber(modifier.Parameters, "mergeDistance", 0), ["bisect"] = ReadBoolean(modifier.Parameters, "bisect", false)
                }),
            "rekall.modifier.array" => RekallAgeModelingGraphEvaluator.ExecuteArrayModifier(source, new JsonObject
                {
                    ["count"] = ReadInteger(modifier.Parameters, "count", 2),
                    ["offset"] = new JsonArray(ReadNumber(modifier.Parameters, "x", 1), ReadNumber(modifier.Parameters, "y", 0), ReadNumber(modifier.Parameters, "z", 0)),
                    ["relativeOffset"] = ReadBoolean(modifier.Parameters, "relativeOffset", false), ["instanceMode"] = ReadBoolean(modifier.Parameters, "instanceMode", false)
                }),
            "rekall.modifier.weighted_normals" => _executor.Execute(source, new("weighted_normals", RekallAgeGeometryDomain.Face,
                Select(source, RekallAgeGeometryDomain.Face, selection), new JsonObject
                {
                    ["attribute"] = ReadString(modifier.Parameters, "attribute", "normal.weighted"), ["faceAreaWeight"] = ReadNumber(modifier.Parameters, "faceAreaWeight", 1)
                })).Mesh,
            _ => throw new RekallAgeMeshOperationException("REKALL_MODIFIER_TYPE_UNKNOWN", $"Modifier type '{modifier.TypeId}' is not executable.")
        };
    }

    private RekallAgeMeshAsset ExecuteSmoothSubdivision(RekallAgeMeshAsset source, JsonObject parameters)
    {
        var levels = ReadInteger(parameters, "levels", 1);
        if (levels < 1 || levels > 6)
            throw new RekallAgeMeshOperationException("REKALL_MODIFIER_PARAMETER_INVALID", "Smooth subdivision levels must be from 1 through 6.");
        var creaseAttribute = ReadString(parameters, "creaseAttribute", "crease.edge");
        var current = source;
        for (var level = 0; level < levels; level++)
            current = _executor.Execute(current, new("subdivide_smooth", RekallAgeGeometryDomain.Face, current.Topology.FaceIds,
                new JsonObject { ["creaseAttribute"] = creaseAttribute })).Mesh;
        return current;
    }

    private static IReadOnlyList<ulong> Select(RekallAgeMeshAsset mesh, RekallAgeGeometryDomain domain, string selection)
    {
        if (selection.Length == 0) return domain switch
        {
            RekallAgeGeometryDomain.Point => mesh.Topology.PointIds,
            RekallAgeGeometryDomain.Edge => mesh.Topology.EdgeIds,
            RekallAgeGeometryDomain.Face => mesh.Topology.FaceIds,
            RekallAgeGeometryDomain.Corner => mesh.Topology.CornerIds,
            _ => throw new RekallAgeMeshOperationException("REKALL_MODIFIER_DOMAIN_UNSUPPORTED", $"Modifier selection domain '{domain}' is unsupported.")
        };
        var found = mesh.SelectionSets.SingleOrDefault(item => item.Name == selection && item.Domain == domain);
        return found?.ElementIds is { Count: > 0 } ids ? ids : throw new RekallAgeMeshOperationException("REKALL_MODIFIER_SELECTION_MISSING", $"Selection '{selection}' has no {domain} elements.");
    }
    private static JsonObject VectorParameters(JsonObject parameters) => new()
    {
        ["x"] = ReadNumber(parameters, "x", 0), ["y"] = ReadNumber(parameters, "y", 0), ["z"] = ReadNumber(parameters, "z", 0)
    };

    private static string Canonical(JsonObject parameters) => "{" + string.Join(",", parameters.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => JsonSerializer.Serialize(item.Key) + ":" + (item.Value?.ToJsonString(RekallAgeModelingJson.Options) ?? "null"))) + "}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static double ReadNumber(JsonObject parameters, string key, double fallback) => parameters[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : fallback;
    private static int ReadInteger(JsonObject parameters, string key, int fallback) => parameters[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;
    private static bool ReadBoolean(JsonObject parameters, string key, bool fallback) => parameters[key] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;
    private static string ReadString(JsonObject parameters, string key, string fallback) => parameters[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : fallback;
    private static RekallAgeModelingGraphDiagnostic Error(string code, string message, string? id = null) => new(code, RekallAgeModelingDiagnosticSeverity.Error, message, id);
    private static RekallAgeModifierStackEvaluationReport Failure(RekallAgeModifierStackAsset stack, Stopwatch stopwatch,
        IReadOnlyList<RekallAgeModifierEvaluationItem> reports, IReadOnlyList<RekallAgeModelingGraphDiagnostic> diagnostics) =>
        new(false, stack.AssetId, stack.Revision, null, reports.Count, reports.Count(item => item.CacheHit), reports.Count(item => item.Invalidated), reports, stopwatch.Elapsed.TotalMilliseconds, diagnostics);
}
