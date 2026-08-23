using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeModelingGraphEvaluator
{
    private readonly RekallAgeModelingGraphValidator _validator =
        new(RekallAgeModelingNodeCatalog.CreateDefault());
    private readonly Dictionary<string, NodeValue> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastNodeKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, RekallAgeMeshAsset>> _lastGoodOutputs = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ValueTask<RekallAgeModelingGraphEvaluationReport> EvaluateAsync(
        RekallAgeModelingGraphAsset graph,
        IReadOnlyList<string> requestedOutputs,
        RekallAgeModelingEvaluationBudget budget,
        RekallAgeModelingEvaluationContext evaluationContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(requestedOutputs);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(evaluationContext);
        return ValueTask.FromResult(Evaluate(graph, requestedOutputs, budget, evaluationContext, cancellationToken));
    }

    private RekallAgeModelingGraphEvaluationReport Evaluate(
        RekallAgeModelingGraphAsset graph,
        IReadOnlyList<string> requestedOutputs,
        RekallAgeModelingEvaluationBudget budget,
        RekallAgeModelingEvaluationContext evaluationContext,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var diagnostics = new List<RekallAgeModelingGraphDiagnostic>();
        var reports = new List<RekallAgeModelingNodeEvaluationReport>();
        var outputKey = LastGoodKey(graph.AssetId, requestedOutputs);
        try
        {
            ValidateInputs(requestedOutputs, budget, evaluationContext);
            var validation = _validator.Validate(graph);
            diagnostics.AddRange(validation.Diagnostics);
            if (!validation.IsValid || validation.ExecutionPlan is null)
            {
                throw new EvaluationException("REKALL_MODELING_EVALUATION_GRAPH_INVALID", "The modelling graph did not pass strict validation.");
            }

            var requested = requestedOutputs.Distinct(StringComparer.Ordinal).ToArray();
            var outputDefinitions = graph.Outputs.ToDictionary(output => output.Name, StringComparer.Ordinal);
            foreach (var name in requested)
            {
                if (!outputDefinitions.ContainsKey(name))
                {
                    throw new EvaluationException("REKALL_MODELING_EVALUATION_OUTPUT_UNKNOWN", $"Graph output '{name}' was not found.");
                }
            }
            var reachable = requested
                .SelectMany(name => validation.ExecutionPlan.OutputNodeIds[name])
                .ToHashSet(StringComparer.Ordinal);
            var orderedNodeIds = validation.ExecutionPlan.OrderedNodeIds.Where(reachable.Contains).ToArray();
            if (orderedNodeIds.Length > budget.MaximumEvaluatedNodes)
            {
                throw new EvaluationException("REKALL_MODELING_EVALUATION_NODE_BUDGET_EXCEEDED", "Reachable node count exceeds the evaluation budget.");
            }

            var nodes = graph.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
            var values = new Dictionary<string, NodeValue>(StringComparer.Ordinal);
            var outputHashes = new Dictionary<string, string>(StringComparer.Ordinal);
            var accountedMeshes = new HashSet<RekallAgeMeshAsset>(ReferenceEqualityComparer.Instance);
            var totalPoints = 0;
            var totalFaces = 0;
            long totalBytes = 0;
            foreach (var nodeId in orderedNodeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stopwatch.ElapsedMilliseconds > budget.MaximumMilliseconds)
                {
                    throw new EvaluationException("REKALL_MODELING_EVALUATION_TIME_BUDGET_EXCEEDED", "Evaluation exceeded its time budget.", nodeId);
                }
                var nodeStopwatch = Stopwatch.StartNew();
                var node = nodes[nodeId];
                var incoming = graph.Links
                    .Where(link => link.ToNodeId == nodeId && reachable.Contains(link.FromNodeId))
                    .OrderBy(link => link.ToPortId, StringComparer.Ordinal)
                    .ThenBy(link => link.LinkId, StringComparer.Ordinal)
                    .ToArray();
                var cacheKey = CacheKey(graph, node, incoming, outputHashes, evaluationContext);
                NodeValue value;
                bool cacheHit;
                lock (_gate)
                {
                    cacheHit = _cache.TryGetValue(cacheKey, out value!);
                }
                if (!cacheHit)
                {
                    value = EvaluateNode(graph, node, incoming, values);
                }
                var (points, faces, bytes) = Size(value);
                if (value.Mesh is not null && accountedMeshes.Add(value.Mesh))
                {
                    totalPoints = checked(totalPoints + points);
                    totalFaces = checked(totalFaces + faces);
                    totalBytes = checked(totalBytes + bytes);
                }
                EnforceGeometryBudgets(totalPoints, totalFaces, totalBytes, budget, nodeId);
                var nodeIdentity = $"{graph.AssetId}|{node.NodeId}|{evaluationContext.TargetProfile}";
                bool invalidated;
                lock (_gate)
                {
                    invalidated = _lastNodeKeys.TryGetValue(nodeIdentity, out var previousKey) && previousKey != cacheKey;
                    if (!cacheHit) _cache[cacheKey] = value;
                    _lastNodeKeys[nodeIdentity] = cacheKey;
                }
                values[nodeId] = value;
                outputHashes[nodeId] = cacheKey;
                nodeStopwatch.Stop();
                reports.Add(new(
                    node.NodeId,
                    node.TypeId,
                    cacheKey,
                    cacheHit,
                    invalidated,
                    nodeStopwatch.Elapsed.TotalMilliseconds,
                    points,
                    faces,
                    bytes));
            }

            var outputs = requested.ToDictionary(
                name => name,
                name => values[outputDefinitions[name].NodeId].Mesh
                    ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_OUTPUT_TYPE_INVALID", $"Output '{name}' did not evaluate to geometry."),
                StringComparer.Ordinal);
            lock (_gate)
            {
                _lastGoodOutputs[outputKey] = outputs;
            }
            stopwatch.Stop();
            return Report(true, graph, outputs, false, reports, stopwatch, diagnostics, budget.MaximumReportNodes);
        }
        catch (EvaluationException error)
        {
            diagnostics.Add(new(error.Code, RekallAgeModelingDiagnosticSeverity.Error, error.Message, error.NodeId));
            IReadOnlyDictionary<string, RekallAgeMeshAsset> fallback;
            lock (_gate)
            {
                fallback = _lastGoodOutputs.GetValueOrDefault(outputKey)
                    ?? new Dictionary<string, RekallAgeMeshAsset>(StringComparer.Ordinal);
            }
            stopwatch.Stop();
            return Report(false, graph, fallback, fallback.Count > 0, reports, stopwatch, diagnostics, budget.MaximumReportNodes);
        }
    }

    private static NodeValue EvaluateNode(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, NodeValue> values) =>
        node.TypeId switch
        {
            "rekall.modeling.primitive.box" => new(CreateBox(graph, node)),
            "rekall.modeling.primitive.grid" => new(CreateGrid(graph, node)),
            "rekall.modeling.transform" => TransformGeometry(graph, node, InputGeometry(node, "geometry", incoming, values)),
            "rekall.modeling.extrude" => ApplySemanticOperation(
                graph,
                node,
                InputGeometry(node, "geometry", incoming, values),
                "extrude_faces",
                new JsonObject
                {
                    ["x"] = ReadVector3(node, "offset", new(0, 0, 1)).X,
                    ["y"] = ReadVector3(node, "offset", new(0, 0, 1)).Y,
                    ["z"] = ReadVector3(node, "offset", new(0, 0, 1)).Z
                }),
            "rekall.modeling.triangulate" => ApplySemanticOperation(
                graph,
                node,
                InputGeometry(node, "geometry", incoming, values),
                "triangulate_faces",
                new JsonObject()),
            "rekall.modeling.output.mesh" => InputGeometry(node, "input", incoming, values),
            _ => throw new EvaluationException(
                "REKALL_MODELING_EVALUATION_NODE_NOT_IMPLEMENTED",
                $"Node type '{node.TypeId}@{node.TypeVersion}' has no evaluator implementation.",
                node.NodeId)
        };

    private static NodeValue ApplySemanticOperation(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        NodeValue input,
        string operationId,
        JsonObject parameters)
    {
        var source = input.Mesh
            ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", $"{node.TypeId} requires geometry input.", node.NodeId);
        try
        {
            var result = new RekallAgeMeshOperationExecutor().Execute(
                source,
                new(operationId, RekallAgeGeometryDomain.Face, source.Topology.FaceIds.ToArray(), parameters));
            return new(result.Mesh with
            {
                AssetId = $"{graph.AssetId}.{node.NodeId}",
                Name = node.NodeId,
                Revision = graph.Revision
            });
        }
        catch (RekallAgeMeshOperationException error)
        {
            throw new EvaluationException(error.Code, error.Message, node.NodeId);
        }
    }

    private static NodeValue TransformGeometry(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        NodeValue input)
    {
        var source = input.Mesh
            ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", "Transform requires geometry input.", node.NodeId);
        var translation = ReadVector3(node, "translation", new(0, 0, 0));
        var rotation = ReadVector3(node, "rotation", new(0, 0, 0));
        var scale = ReadVector3(node, "scale", new(1, 1, 1));
        if (Math.Abs(scale.X) < 1e-12 || Math.Abs(scale.Y) < 1e-12 || Math.Abs(scale.Z) < 1e-12)
        {
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Transform scale components must be nonzero.", node.NodeId);
        }
        var positions = source.Topology.Positions
            .Select(position => Add(Rotate(new(position.X * scale.X, position.Y * scale.Y, position.Z * scale.Z), rotation), translation))
            .ToArray();
        var mesh = source with
        {
            AssetId = $"{graph.AssetId}.{node.NodeId}",
            Name = node.NodeId,
            Revision = graph.Revision,
            Topology = source.Topology with { Positions = positions }
        };
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        if (!validation.IsValid)
        {
            throw new EvaluationException("REKALL_MODELING_EVALUATION_OUTPUT_INVALID", "Transform evaluator produced invalid topology.", node.NodeId);
        }
        return new(mesh);
    }

    private static NodeValue InputGeometry(
        RekallAgeModelingGraphNode node,
        string portId,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, NodeValue> values)
    {
        var link = incoming.SingleOrDefault(item => item.ToPortId == portId)
            ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_MISSING", $"Input '{portId}' is missing.", node.NodeId);
        return values[link.FromNodeId];
    }

    private static RekallAgeMeshAsset CreateBox(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node)
    {
        var halfX = ReadPositive(node, "sizeX", 1) / 2;
        var halfY = ReadPositive(node, "sizeY", 1) / 2;
        var halfZ = ReadPositive(node, "sizeZ", 1) / 2;
        var topology = new RekallAgeMeshTopology(
            PointIds: [1, 2, 3, 4, 5, 6, 7, 8],
            Positions:
            [
                new(-halfX, -halfY, -halfZ), new(halfX, -halfY, -halfZ),
                new(halfX, halfY, -halfZ), new(-halfX, halfY, -halfZ),
                new(-halfX, -halfY, halfZ), new(halfX, -halfY, halfZ),
                new(halfX, halfY, halfZ), new(-halfX, halfY, halfZ)
            ],
            EdgeIds: [11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22],
            EdgePointIndices:
            [
                new(0, 1), new(1, 2), new(2, 3), new(3, 0),
                new(4, 5), new(5, 6), new(6, 7), new(7, 4),
                new(0, 4), new(1, 5), new(2, 6), new(3, 7)
            ],
            FaceIds: [31, 32, 33, 34, 35, 36],
            FaceOffsets: [0, 4, 8, 12, 16, 20, 24],
            CornerIds: Enumerable.Range(41, 24).Select(value => (ulong)value).ToArray(),
            CornerPointIndices:
            [
                0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4,
                1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7
            ],
            CornerEdgeIndices:
            [
                3, 2, 1, 0, 4, 5, 6, 7, 0, 9, 4, 8,
                1, 10, 5, 9, 2, 11, 6, 10, 3, 8, 7, 11
            ]);
        var mesh = RekallAgeMeshAsset.Create(
            $"{graph.AssetId}.{node.NodeId}",
            node.NodeId,
            topology) with { Revision = graph.Revision };
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        if (!validation.IsValid)
        {
            throw new EvaluationException("REKALL_MODELING_EVALUATION_OUTPUT_INVALID", "Box evaluator produced invalid topology.", node.NodeId);
        }
        return mesh;
    }

    private static RekallAgeMeshAsset CreateGrid(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node)
    {
        var sizeX = ReadPositive(node, "sizeX", 1);
        var sizeY = ReadPositive(node, "sizeY", 1);
        var segmentsX = ReadInteger(node, "segmentsX", 1, 1, 4_096);
        var segmentsY = ReadInteger(node, "segmentsY", 1, 1, 4_096);
        var pointCount = checked((segmentsX + 1) * (segmentsY + 1));
        var faceCount = checked(segmentsX * segmentsY);
        if (pointCount > 2_000_000 || faceCount > 2_000_000)
        {
            throw new EvaluationException("REKALL_MODELING_EVALUATION_ELEMENT_BUDGET_EXCEEDED", "Grid parameters exceed the hard element ceiling.", node.NodeId);
        }
        var positions = new List<RekallAgeGeometryVector3>(pointCount);
        for (var y = 0; y <= segmentsY; y++)
        for (var x = 0; x <= segmentsX; x++)
            positions.Add(new(-sizeX / 2 + sizeX * x / segmentsX, -sizeY / 2 + sizeY * y / segmentsY, 0));

        var edgePoints = new List<RekallAgeMeshEdgePointIndices>();
        for (var y = 0; y <= segmentsY; y++)
        for (var x = 0; x < segmentsX; x++)
            edgePoints.Add(new(Point(x, y), Point(x + 1, y)));
        var verticalStart = edgePoints.Count;
        for (var y = 0; y < segmentsY; y++)
        for (var x = 0; x <= segmentsX; x++)
            edgePoints.Add(new(Point(x, y), Point(x, y + 1)));

        var cornerPoints = new List<int>(faceCount * 4);
        var cornerEdges = new List<int>(faceCount * 4);
        for (var y = 0; y < segmentsY; y++)
        for (var x = 0; x < segmentsX; x++)
        {
            cornerPoints.AddRange([Point(x, y), Point(x + 1, y), Point(x + 1, y + 1), Point(x, y + 1)]);
            cornerEdges.AddRange([
                Horizontal(x, y), Vertical(x + 1, y), Horizontal(x, y + 1), Vertical(x, y)]);
        }
        var topology = new RekallAgeMeshTopology(
            Enumerable.Range(1, pointCount).Select(value => (ulong)value).ToArray(),
            positions,
            Enumerable.Range(1, edgePoints.Count).Select(value => (ulong)(10_000 + value)).ToArray(),
            edgePoints,
            Enumerable.Range(1, faceCount).Select(value => (ulong)(20_000 + value)).ToArray(),
            Enumerable.Range(0, faceCount + 1).Select(value => value * 4).ToArray(),
            Enumerable.Range(1, faceCount * 4).Select(value => (ulong)(30_000 + value)).ToArray(),
            cornerPoints,
            cornerEdges);
        var mesh = RekallAgeMeshAsset.Create($"{graph.AssetId}.{node.NodeId}", node.NodeId, topology) with { Revision = graph.Revision };
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        if (!validation.IsValid)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_OUTPUT_INVALID", "Grid evaluator produced invalid topology.", node.NodeId);
        return mesh;

        int Point(int x, int y) => y * (segmentsX + 1) + x;
        int Horizontal(int x, int y) => y * segmentsX + x;
        int Vertical(int x, int y) => verticalStart + y * (segmentsX + 1) + x;
    }

    private static double ReadPositive(RekallAgeModelingGraphNode node, string name, double fallback)
    {
        var value = node.Parameters[name] is JsonValue json && json.TryGetValue<double>(out var number) ? number : fallback;
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", $"Parameter '{name}' must be positive and finite.", node.NodeId);
        }
        return value;
    }

    private static int ReadInteger(RekallAgeModelingGraphNode node, string name, int fallback, int minimum, int maximum)
    {
        var value = node.Parameters[name] is JsonValue json && json.TryGetValue<int>(out var number) ? number : fallback;
        if (value < minimum || value > maximum)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", $"Parameter '{name}' must be between {minimum} and {maximum}.", node.NodeId);
        return value;
    }

    private static RekallAgeGeometryVector3 ReadVector3(
        RekallAgeModelingGraphNode node,
        string name,
        RekallAgeGeometryVector3 fallback)
    {
        if (node.Parameters[name] is null) return fallback;
        if (node.Parameters[name] is not JsonArray { Count: 3 } array
            || !TryDouble(array[0], out var x)
            || !TryDouble(array[1], out var y)
            || !TryDouble(array[2], out var z)
            || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
        {
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", $"Parameter '{name}' must be a finite three-number array.", node.NodeId);
        }
        return new(x, y, z);
    }

    private static bool TryDouble(JsonNode? node, out double value)
    {
        value = default;
        return node is JsonValue json && json.TryGetValue(out value);
    }

    private static RekallAgeGeometryVector3 Rotate(RekallAgeGeometryVector3 value, RekallAgeGeometryVector3 degrees)
    {
        var x = degrees.X * Math.PI / 180;
        var y = degrees.Y * Math.PI / 180;
        var z = degrees.Z * Math.PI / 180;
        var afterX = new RekallAgeGeometryVector3(
            value.X,
            value.Y * Math.Cos(x) - value.Z * Math.Sin(x),
            value.Y * Math.Sin(x) + value.Z * Math.Cos(x));
        var afterY = new RekallAgeGeometryVector3(
            afterX.X * Math.Cos(y) + afterX.Z * Math.Sin(y),
            afterX.Y,
            -afterX.X * Math.Sin(y) + afterX.Z * Math.Cos(y));
        return new(
            afterY.X * Math.Cos(z) - afterY.Y * Math.Sin(z),
            afterY.X * Math.Sin(z) + afterY.Y * Math.Cos(z),
            afterY.Z);
    }

    private static RekallAgeGeometryVector3 Add(RekallAgeGeometryVector3 left, RekallAgeGeometryVector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static string CacheKey(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, string> hashes,
        RekallAgeModelingEvaluationContext context)
    {
        var builder = new StringBuilder();
        builder.Append(node.TypeId).Append('@').Append(node.TypeVersion).Append('|')
            .Append(Canonical(node.Parameters)).Append('|')
            .Append(context.Seed).Append('|')
            .Append(context.DeterministicTime.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(context.EngineVersion).Append('|')
            .Append(context.TargetProfile).Append('|')
            .Append(context.EvaluationSchemaVersion).Append('|')
            .Append(graph.SchemaVersion);
        foreach (var link in incoming)
        {
            builder.Append('|').Append(link.ToPortId).Append(':').Append(link.LinkId).Append(':').Append(hashes[link.FromNodeId]);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static string Canonical(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject obj => "{" + string.Join(",", obj.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => JsonValue.Create(item.Key)!.ToJsonString() + ":" + Canonical(item.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        _ => node.ToJsonString()
    };

    private static (int Points, int Faces, long Bytes) Size(NodeValue value)
    {
        if (value.Mesh is null) return (0, 0, 0);
        var topology = value.Mesh.Topology;
        return (topology.PointIds.Count, topology.FaceIds.Count,
            topology.PointIds.Count * 64L + topology.EdgeIds.Count * 40L + topology.FaceIds.Count * 48L + topology.CornerIds.Count * 32L);
    }

    private static void EnforceGeometryBudgets(
        int points,
        int faces,
        long bytes,
        RekallAgeModelingEvaluationBudget budget,
        string nodeId)
    {
        if (points > budget.MaximumPoints)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_POINT_BUDGET_EXCEEDED", "Evaluated geometry exceeds the point budget.", nodeId);
        if (faces > budget.MaximumFaces)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_FACE_BUDGET_EXCEEDED", "Evaluated geometry exceeds the face budget.", nodeId);
        if (bytes > budget.MaximumApproximateBytes)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_MEMORY_BUDGET_EXCEEDED", "Evaluated geometry exceeds the approximate memory budget.", nodeId);
    }

    private static void ValidateInputs(
        IReadOnlyList<string> requestedOutputs,
        RekallAgeModelingEvaluationBudget budget,
        RekallAgeModelingEvaluationContext context)
    {
        if (requestedOutputs.Count is < 1 or > RekallAgeModelingGraphValidator.MaximumOutputs)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_OUTPUT_BOUNDS", "Evaluation requires a bounded nonempty output selection.");
        if (budget.MaximumEvaluatedNodes < 1 || budget.MaximumPoints < 1 || budget.MaximumFaces < 1
            || budget.MaximumApproximateBytes < 1 || budget.MaximumMilliseconds < 1 || budget.MaximumReportNodes < 1)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_BUDGET_INVALID", "Every evaluation budget must be positive.");
        if (!double.IsFinite(context.DeterministicTime)
            || string.IsNullOrWhiteSpace(context.EngineVersion)
            || string.IsNullOrWhiteSpace(context.TargetProfile))
            throw new EvaluationException("REKALL_MODELING_EVALUATION_CONTEXT_INVALID", "Evaluation context must be finite and identify engine and target profile.");
    }

    private static RekallAgeModelingGraphEvaluationReport Report(
        bool succeeded,
        RekallAgeModelingGraphAsset graph,
        IReadOnlyDictionary<string, RekallAgeMeshAsset> outputs,
        bool retained,
        IReadOnlyList<RekallAgeModelingNodeEvaluationReport> reports,
        Stopwatch stopwatch,
        IReadOnlyList<RekallAgeModelingGraphDiagnostic> diagnostics,
        int maximumReports) =>
        new(
            succeeded,
            graph.AssetId,
            graph.Revision,
            outputs,
            retained,
            reports.Count,
            reports.Count(item => item.CacheHit),
            reports.Count(item => item.Invalidated),
            reports.Take(maximumReports).ToArray(),
            reports.Count > maximumReports,
            stopwatch.Elapsed.TotalMilliseconds,
            diagnostics);

    private static string LastGoodKey(string assetId, IReadOnlyList<string> outputs) =>
        assetId + "|" + string.Join(",", outputs.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private sealed record NodeValue(RekallAgeMeshAsset? Mesh);

    private sealed class EvaluationException : Exception
    {
        public EvaluationException(string code, string message, string? nodeId = null) : base(message)
        {
            Code = code;
            NodeId = nodeId;
        }

        public string Code { get; }
        public string? NodeId { get; }
    }
}
