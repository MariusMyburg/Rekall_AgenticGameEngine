using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            "rekall.modeling.primitive.sphere" => new(CreateSphere(graph, node)),
            "rekall.modeling.transform" => TransformGeometry(graph, node, InputGeometry(node, "geometry", incoming, values)),
            "rekall.modeling.join" => JoinGeometry(graph, node, incoming, values),
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
            "rekall.modeling.subdivide" => ApplySemanticOperation(
                graph,
                node,
                InputGeometry(node, "geometry", incoming, values),
                "subdivide_faces",
                new JsonObject()),
            "rekall.modeling.subdivide_smooth" => ApplySemanticOperation(
                graph,
                node,
                InputGeometry(node, "geometry", incoming, values),
                "subdivide_smooth",
                new JsonObject()),
            "rekall.modeling.merge_by_distance" => ApplySemanticOperation(
                graph,
                node,
                InputGeometry(node, "geometry", incoming, values),
                "merge_by_distance",
                new JsonObject { ["distance"] = ReadNumber(node, "distance", 0.0001) },
                RekallAgeGeometryDomain.Point),
            "rekall.modeling.field.math" => EvaluateFieldMath(node, incoming, values),
            "rekall.modeling.attribute.named" => ReadNamedAttribute(node, InputGeometry(node, "geometry", incoming, values)),
            "rekall.modeling.attribute.capture" => CaptureAttribute(
                graph, node, InputGeometry(node, "geometry", incoming, values), InputScalars(node, "value", incoming, values)),
            "rekall.modeling.material.assign" => AssignMaterial(
                graph, node, InputGeometry(node, "geometry", incoming, values), incoming, values),
            "rekall.modeling.output.mesh" => InputGeometry(node, "input", incoming, values),
            _ => throw new EvaluationException(
                "REKALL_MODELING_EVALUATION_NODE_NOT_IMPLEMENTED",
                $"Node type '{node.TypeId}@{node.TypeVersion}' has no evaluator implementation.",
                node.NodeId)
        };

    private static NodeValue EvaluateFieldMath(
        RekallAgeModelingGraphNode node,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, NodeValue> values)
    {
        var a = OptionalScalars(node, "a", incoming, values) ?? [ReadNumber(node, "a", 0)];
        var b = OptionalScalars(node, "b", incoming, values) ?? [ReadNumber(node, "b", 0)];
        var count = Math.Max(a.Count, b.Count);
        if (a.Count != 1 && a.Count != count || b.Count != 1 && b.Count != count)
            throw new EvaluationException("REKALL_MODELING_FIELD_LENGTH_MISMATCH", "Field math inputs must have equal lengths or be scalar-broadcastable.", node.NodeId);
        var operation = ReadString(node, "operation", "add");
        var result = new double[count];
        for (var index = 0; index < count; index++)
        {
            var left = a[a.Count == 1 ? 0 : index];
            var right = b[b.Count == 1 ? 0 : index];
            result[index] = operation switch
            {
                "add" => left + right,
                "subtract" => left - right,
                "multiply" => left * right,
                "divide" when Math.Abs(right) > 1e-15 => left / right,
                "divide" => throw new EvaluationException("REKALL_MODELING_FIELD_DIVIDE_BY_ZERO", "Field division encountered zero.", node.NodeId),
                "minimum" => Math.Min(left, right),
                "maximum" => Math.Max(left, right),
                _ => throw new EvaluationException("REKALL_MODELING_FIELD_OPERATION_UNKNOWN", $"Field operation '{operation}' is unsupported.", node.NodeId)
            };
            if (!double.IsFinite(result[index]))
                throw new EvaluationException("REKALL_MODELING_FIELD_NONFINITE", "Field math produced a non-finite value.", node.NodeId);
        }
        return new(Scalars: result);
    }

    private static NodeValue ReadNamedAttribute(RekallAgeModelingGraphNode node, NodeValue input)
    {
        var mesh = input.Mesh ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", "Named Attribute requires geometry.", node.NodeId);
        var name = ReadString(node, "name", "attribute");
        var attribute = mesh.Attributes.FirstOrDefault(item => item.Name == name)
            ?? throw new EvaluationException("REKALL_MODELING_NAMED_ATTRIBUTE_MISSING", $"Attribute '{name}' was not found.", node.NodeId);
        if (attribute.ValueType is not (RekallAgeGeometryValueType.Float or RekallAgeGeometryValueType.Int32))
            throw new EvaluationException("REKALL_MODELING_NAMED_ATTRIBUTE_TYPE_UNSUPPORTED", $"Attribute '{name}' is not scalar numeric.", node.NodeId);
        return new(Scalars: attribute.Values.Select(value => attribute.ValueType == RekallAgeGeometryValueType.Float
            ? value.GetDouble()
            : value.GetInt32()).ToArray());
    }

    private static NodeValue CaptureAttribute(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        NodeValue geometry,
        IReadOnlyList<double> scalars)
    {
        var mesh = geometry.Mesh ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", "Capture Attribute requires geometry.", node.NodeId);
        var name = ReadString(node, "name", "attribute");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
            throw new EvaluationException("REKALL_MODELING_ATTRIBUTE_NAME_INVALID", "Captured attribute name must contain 1-128 characters.", node.NodeId);
        var domainText = ReadString(node, "domain", "point");
        if (!Enum.TryParse<RekallAgeGeometryDomain>(domainText, true, out var domain) || domain == RekallAgeGeometryDomain.Instance)
            throw new EvaluationException("REKALL_MODELING_ATTRIBUTE_DOMAIN_INVALID", $"Attribute domain '{domainText}' is unsupported.", node.NodeId);
        var count = DomainCount(mesh.Topology, domain);
        if (scalars.Count != 1 && scalars.Count != count)
            throw new EvaluationException("REKALL_MODELING_FIELD_LENGTH_MISMATCH", $"Captured field has {scalars.Count} values for {count} {domain} elements.", node.NodeId);
        var values = Enumerable.Range(0, count)
            .Select(index => JsonSerializer.SerializeToElement(scalars[scalars.Count == 1 ? 0 : index]))
            .ToArray();
        var attributes = mesh.Attributes.Where(attribute => attribute.Name != name).Append(
            new RekallAgeGeometryAttribute(name, domain, RekallAgeGeometryValueType.Float, values, Interpolation: RekallAgeGeometryInterpolation.Linear,
                DefaultValue: JsonSerializer.SerializeToElement(0d))).ToArray();
        return new(mesh with
        {
            AssetId = $"{graph.AssetId}.{node.NodeId}", Name = node.NodeId, Revision = graph.Revision, Attributes = attributes
        });
    }

    private static NodeValue AssignMaterial(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        NodeValue geometry,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, NodeValue> values)
    {
        var mesh = geometry.Mesh ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", "Assign Material requires geometry.", node.NodeId);
        var linked = incoming.FirstOrDefault(link => link.ToPortId == "material");
        var assetId = linked is null ? ReadString(node, "materialAssetId", "material.default") : values[linked.FromNodeId].MaterialAssetId;
        if (string.IsNullOrWhiteSpace(assetId))
            throw new EvaluationException("REKALL_MODELING_MATERIAL_ASSET_ID_MISSING", "Material assignment requires a material asset ID.", node.NodeId);
        var slotName = ReadString(node, "slotName", "material");
        var slots = mesh.MaterialSlots.ToList();
        var slotIndex = slots.FindIndex(slot => slot.MaterialAssetId == assetId && slot.Name == slotName);
        if (slotIndex < 0) { slotIndex = slots.Count; slots.Add(new(slotName, assetId)); }
        var indices = Enumerable.Range(0, mesh.Topology.FaceIds.Count).Select(_ => JsonSerializer.SerializeToElement(slotIndex)).ToArray();
        var attributes = mesh.Attributes.Where(attribute => !string.Equals(attribute.Semantic, "material-index", StringComparison.OrdinalIgnoreCase))
            .Append(new RekallAgeGeometryAttribute(
                "material.index", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.Int32, indices,
                "material-index", RekallAgeGeometryInterpolation.Nearest, JsonSerializer.SerializeToElement(0)))
            .ToArray();
        return new(mesh with
        {
            AssetId = $"{graph.AssetId}.{node.NodeId}", Name = node.NodeId, Revision = graph.Revision,
            MaterialSlots = slots, Attributes = attributes
        });
    }

    private static IReadOnlyList<double> InputScalars(
        RekallAgeModelingGraphNode node,
        string portId,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, NodeValue> values) =>
        OptionalScalars(node, portId, incoming, values)
        ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_MISSING", $"Input '{portId}' is missing.", node.NodeId);

    private static IReadOnlyList<double>? OptionalScalars(
        RekallAgeModelingGraphNode node,
        string portId,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, NodeValue> values)
    {
        var link = incoming.FirstOrDefault(item => item.ToPortId == portId);
        if (link is null) return null;
        return values[link.FromNodeId].Scalars
            ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", $"Input '{portId}' is not a scalar field.", node.NodeId);
    }

    private static NodeValue JoinGeometry(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, NodeValue> values)
    {
        var inputs = incoming.Where(link => link.ToPortId == "geometry")
            .Select(link => values[link.FromNodeId].Mesh
                ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", "Join requires geometry inputs.", node.NodeId))
            .ToArray();
        if (inputs.Length == 0)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_MISSING", "Join requires at least one geometry input.", node.NodeId);

        var positions = new List<RekallAgeGeometryVector3>();
        var edgePoints = new List<RekallAgeMeshEdgePointIndices>();
        var faceOffsets = new List<int> { 0 };
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var pointMaps = new List<Dictionary<ulong, ulong>>();
        var edgeMaps = new List<Dictionary<ulong, ulong>>();
        var faceMaps = new List<Dictionary<ulong, ulong>>();
        var cornerMaps = new List<Dictionary<ulong, ulong>>();
        foreach (var mesh in inputs)
        {
            var pointOffset = positions.Count;
            var edgeOffset = edgePoints.Count;
            var cornerOffset = cornerPoints.Count;
            positions.AddRange(mesh.Topology.Positions);
            edgePoints.AddRange(mesh.Topology.EdgePointIndices.Select(edge => new RekallAgeMeshEdgePointIndices(edge.A + pointOffset, edge.B + pointOffset)));
            for (var face = 0; face < mesh.Topology.FaceIds.Count; face++)
                faceOffsets.Add(faceOffsets[^1] + mesh.Topology.FaceOffsets[face + 1] - mesh.Topology.FaceOffsets[face]);
            cornerPoints.AddRange(mesh.Topology.CornerPointIndices.Select(index => index + pointOffset));
            cornerEdges.AddRange(mesh.Topology.CornerEdgeIndices.Select(index => index + edgeOffset));
            pointMaps.Add(Map(mesh.Topology.PointIds, pointOffset, 1));
            edgeMaps.Add(Map(mesh.Topology.EdgeIds, edgeOffset, 10_000));
            faceMaps.Add(Map(mesh.Topology.FaceIds, faceOffsets.Count - mesh.Topology.FaceIds.Count - 1, 20_000));
            cornerMaps.Add(Map(mesh.Topology.CornerIds, cornerOffset, 30_000));
        }
        var topology = new RekallAgeMeshTopology(
            Enumerable.Range(1, positions.Count).Select(value => (ulong)value).ToArray(),
            positions,
            Enumerable.Range(1, edgePoints.Count).Select(value => (ulong)(10_000 + value)).ToArray(),
            edgePoints,
            Enumerable.Range(1, faceOffsets.Count - 1).Select(value => (ulong)(20_000 + value)).ToArray(),
            faceOffsets,
            Enumerable.Range(1, cornerPoints.Count).Select(value => (ulong)(30_000 + value)).ToArray(),
            cornerPoints,
            cornerEdges);
        var (materialSlots, slotMaps) = MergeMaterialSlots(inputs);
        var attributes = MergeAttributes(inputs, slotMaps);
        var selections = MergeSelections(inputs, pointMaps, edgeMaps, faceMaps, cornerMaps);
        var result = RekallAgeMeshAsset.Create(
            $"{graph.AssetId}.{node.NodeId}", node.NodeId, topology, attributes, materialSlots, selections) with { Revision = graph.Revision };
        var validation = new RekallAgeMeshValidator().Validate(result);
        if (!validation.IsValid)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_OUTPUT_INVALID", "Join evaluator produced invalid topology.", node.NodeId);
        return new(result);
    }

    private static Dictionary<ulong, ulong> Map(IReadOnlyList<ulong> source, int offset, int idBase) =>
        source.Select((id, index) => (id, mapped: (ulong)(idBase + offset + index + 1)))
            .ToDictionary(item => item.id, item => item.mapped);

    private static (IReadOnlyList<RekallAgeMaterialSlot> Slots, IReadOnlyList<int[]> Maps) MergeMaterialSlots(
        IReadOnlyList<RekallAgeMeshAsset> inputs)
    {
        var slots = new List<RekallAgeMaterialSlot>();
        var maps = new List<int[]>();
        foreach (var mesh in inputs)
        {
            var map = new int[mesh.MaterialSlots.Count];
            for (var index = 0; index < mesh.MaterialSlots.Count; index++)
            {
                var slot = mesh.MaterialSlots[index];
                var merged = slots.FindIndex(item => item.Name == slot.Name && item.MaterialAssetId == slot.MaterialAssetId);
                if (merged < 0) { merged = slots.Count; slots.Add(slot); }
                map[index] = merged;
            }
            maps.Add(map);
        }
        return (slots, maps);
    }

    private static IReadOnlyList<RekallAgeGeometryAttribute> MergeAttributes(
        IReadOnlyList<RekallAgeMeshAsset> inputs,
        IReadOnlyList<int[]> slotMaps)
    {
        var schemas = inputs.SelectMany(mesh => mesh.Attributes)
            .GroupBy(attribute => attribute.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(attribute => attribute.Name, StringComparer.Ordinal)
            .ToArray();
        var result = new List<RekallAgeGeometryAttribute>();
        foreach (var schema in schemas)
        {
            var values = new List<JsonElement>();
            for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                var mesh = inputs[inputIndex];
                var source = mesh.Attributes.FirstOrDefault(attribute => attribute.Name == schema.Name);
                if (source is not null && (source.Domain != schema.Domain || source.ValueType != schema.ValueType || source.Semantic != schema.Semantic))
                    throw new EvaluationException("REKALL_MODELING_JOIN_ATTRIBUTE_SCHEMA_CONFLICT", $"Attribute '{schema.Name}' has incompatible schemas.");
                var count = DomainCount(mesh.Topology, schema.Domain);
                for (var index = 0; index < count; index++)
                {
                    var value = source is null ? DefaultValue(schema) : source.Values[index];
                    if (schema.Domain == RekallAgeGeometryDomain.Face
                        && schema.ValueType == RekallAgeGeometryValueType.Int32
                        && schema.Semantic?.Equals("material-index", StringComparison.OrdinalIgnoreCase) == true
                        && value.TryGetInt32(out var materialIndex)
                        && materialIndex >= 0 && materialIndex < slotMaps[inputIndex].Length)
                        value = JsonSerializer.SerializeToElement(slotMaps[inputIndex][materialIndex]);
                    values.Add(value.Clone());
                }
            }
            result.Add(schema with { Values = values });
        }
        return result;
    }

    private static IReadOnlyList<RekallAgeMeshSelection> MergeSelections(
        IReadOnlyList<RekallAgeMeshAsset> inputs,
        IReadOnlyList<Dictionary<ulong, ulong>> pointMaps,
        IReadOnlyList<Dictionary<ulong, ulong>> edgeMaps,
        IReadOnlyList<Dictionary<ulong, ulong>> faceMaps,
        IReadOnlyList<Dictionary<ulong, ulong>> cornerMaps)
    {
        var result = new List<RekallAgeMeshSelection>();
        for (var index = 0; index < inputs.Count; index++)
        foreach (var selection in inputs[index].SelectionSets)
        {
            var map = selection.Domain switch
            {
                RekallAgeGeometryDomain.Point => pointMaps[index],
                RekallAgeGeometryDomain.Edge => edgeMaps[index],
                RekallAgeGeometryDomain.Face => faceMaps[index],
                RekallAgeGeometryDomain.Corner => cornerMaps[index],
                _ => throw new EvaluationException("REKALL_MODELING_JOIN_SELECTION_DOMAIN_UNSUPPORTED", $"Selection domain '{selection.Domain}' cannot be joined.")
            };
            result.Add(new(
                $"input-{index}.{selection.Name}",
                selection.Domain,
                selection.ElementIds.Select(id => map[id]).ToArray(),
                selection.ActiveElementId is { } active ? map[active] : null,
                selection.OrderedHistory?.Select(id => map[id]).ToArray()));
        }
        return result;
    }

    private static int DomainCount(RekallAgeMeshTopology topology, RekallAgeGeometryDomain domain) => domain switch
    {
        RekallAgeGeometryDomain.Point => topology.PointIds.Count,
        RekallAgeGeometryDomain.Edge => topology.EdgeIds.Count,
        RekallAgeGeometryDomain.Face => topology.FaceIds.Count,
        RekallAgeGeometryDomain.Corner => topology.CornerIds.Count,
        _ => 0
    };

    private static JsonElement DefaultValue(RekallAgeGeometryAttribute attribute) => attribute.DefaultValue ?? attribute.ValueType switch
    {
        RekallAgeGeometryValueType.Bool => JsonSerializer.SerializeToElement(false),
        RekallAgeGeometryValueType.Int32 => JsonSerializer.SerializeToElement(0),
        RekallAgeGeometryValueType.Float => JsonSerializer.SerializeToElement(0d),
        RekallAgeGeometryValueType.Float2 => JsonSerializer.SerializeToElement(new double[2]),
        RekallAgeGeometryValueType.Float3 => JsonSerializer.SerializeToElement(new double[3]),
        RekallAgeGeometryValueType.Float4 or RekallAgeGeometryValueType.ColorLinear or RekallAgeGeometryValueType.Quaternion => JsonSerializer.SerializeToElement(new double[4]),
        RekallAgeGeometryValueType.Matrix4x4 => JsonSerializer.SerializeToElement(new double[16]),
        RekallAgeGeometryValueType.String => JsonSerializer.SerializeToElement(string.Empty),
        _ => JsonSerializer.SerializeToElement(0d)
    };

    private static NodeValue ApplySemanticOperation(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        NodeValue input,
        string operationId,
        JsonObject parameters,
        RekallAgeGeometryDomain domain = RekallAgeGeometryDomain.Face)
    {
        var source = input.Mesh
            ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", $"{node.TypeId} requires geometry input.", node.NodeId);
        try
        {
            var result = new RekallAgeMeshOperationExecutor().Execute(
                source,
                new(operationId, domain, domain == RekallAgeGeometryDomain.Point
                    ? source.Topology.PointIds.ToArray()
                    : source.Topology.FaceIds.ToArray(), parameters));
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

    private static RekallAgeMeshAsset CreateSphere(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node)
    {
        var radius = ReadPositive(node, "radius", 0.5);
        var segments = ReadInteger(node, "segments", 16, 3, 4_096);
        var rings = ReadInteger(node, "rings", 8, 2, 4_096);
        var vertexCount = checked((segments + 1) * (rings + 1));
        var triangleCount = checked(segments * 2 * (rings - 1));
        if (vertexCount > 2_000_000 || triangleCount > 2_000_000)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_ELEMENT_BUDGET_EXCEEDED", "Sphere parameters exceed the hard element ceiling.", node.NodeId);
        var vertices = new List<RekallAgeLegacyGeometryVertex>(vertexCount);
        for (var ring = 0; ring <= rings; ring++)
        {
            var v = ring / (double)rings;
            var theta = Math.PI * v;
            for (var segment = 0; segment <= segments; segment++)
            {
                var u = segment / (double)segments;
                var phi = Math.PI * 2 * u;
                var normal = new RekallAgeGeometryVector3(
                    Math.Sin(theta) * Math.Cos(phi),
                    Math.Cos(theta),
                    Math.Sin(theta) * Math.Sin(phi));
                vertices.Add(new(
                    new(normal.X * radius, normal.Y * radius, normal.Z * radius),
                    normal,
                    new(u, v)));
            }
        }
        var indices = new List<uint>(triangleCount * 3);
        var stride = segments + 1;
        for (var ring = 0; ring < rings; ring++)
        for (var segment = 0; segment < segments; segment++)
        {
            var a = checked((uint)(ring * stride + segment));
            var b = a + 1;
            var c = checked((uint)((ring + 1) * stride + segment));
            var d = c + 1;
            if (ring > 0) indices.AddRange([a, c, b]);
            if (ring < rings - 1) indices.AddRange([b, c, d]);
        }
        return new RekallAgeLegacyGeometryMeshAdapter().Convert(
            $"{graph.AssetId}.{node.NodeId}", node.NodeId, vertices, indices) with { Revision = graph.Revision };
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

    private static double ReadNumber(RekallAgeModelingGraphNode node, string name, double fallback)
    {
        var value = node.Parameters[name] is JsonValue json && json.TryGetValue<double>(out var number) ? number : fallback;
        if (!double.IsFinite(value))
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", $"Parameter '{name}' must be finite.", node.NodeId);
        return value;
    }

    private static string ReadString(RekallAgeModelingGraphNode node, string name, string fallback) =>
        node.Parameters[name] is JsonValue json && json.TryGetValue<string>(out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;

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

    private sealed record NodeValue(
        RekallAgeMeshAsset? Mesh = null,
        IReadOnlyList<double>? Scalars = null,
        string? MaterialAssetId = null);

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
