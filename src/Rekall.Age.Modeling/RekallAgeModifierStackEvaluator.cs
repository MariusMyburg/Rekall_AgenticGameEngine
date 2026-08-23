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
            _ => throw new RekallAgeMeshOperationException("REKALL_MODIFIER_TYPE_UNKNOWN", $"Modifier type '{modifier.TypeId}' is not executable.")
        };
    }

    private static IReadOnlyList<ulong> Select(RekallAgeMeshAsset mesh, RekallAgeGeometryDomain domain, string selection)
    {
        if (selection.Length == 0) return domain == RekallAgeGeometryDomain.Point ? mesh.Topology.PointIds : mesh.Topology.FaceIds;
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
    private static string ReadString(JsonObject parameters, string key, string fallback) => parameters[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : fallback;
    private static RekallAgeModelingGraphDiagnostic Error(string code, string message, string? id = null) => new(code, RekallAgeModelingDiagnosticSeverity.Error, message, id);
    private static RekallAgeModifierStackEvaluationReport Failure(RekallAgeModifierStackAsset stack, Stopwatch stopwatch,
        IReadOnlyList<RekallAgeModifierEvaluationItem> reports, IReadOnlyList<RekallAgeModelingGraphDiagnostic> diagnostics) =>
        new(false, stack.AssetId, stack.Revision, null, reports.Count, reports.Count(item => item.CacheHit), reports.Count(item => item.Invalidated), reports, stopwatch.Elapsed.TotalMilliseconds, diagnostics);
}
