using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeModelingGraphValidator
{
    public const int MaximumNodes = 4_096;
    public const int MaximumLinks = 16_384;
    public const int MaximumOutputs = 256;

    private readonly RekallAgeModelingNodeCatalog _catalog;

    public RekallAgeModelingGraphValidator(RekallAgeModelingNodeCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public RekallAgeModelingGraphValidationReport Validate(RekallAgeModelingGraphAsset graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var diagnostics = new List<RekallAgeModelingGraphDiagnostic>();
        if (graph.SchemaVersion != RekallAgeModelingGraphAsset.CurrentSchemaVersion)
        {
            Error(diagnostics, "REKALL_MODELING_GRAPH_SCHEMA_UNSUPPORTED", $"Graph schema {graph.SchemaVersion} is unsupported.");
        }
        if (!ValidId(graph.AssetId))
        {
            Error(diagnostics, "REKALL_MODELING_GRAPH_ASSET_ID_INVALID", "Graph assetId must be a 1-128 character stable token.");
        }
        if (string.IsNullOrWhiteSpace(graph.Name) || graph.Name.Length > 256)
        {
            Error(diagnostics, "REKALL_MODELING_GRAPH_NAME_INVALID", "Graph name must contain 1-256 characters.");
        }
        if (graph.Revision < 1)
        {
            Error(diagnostics, "REKALL_MODELING_GRAPH_REVISION_INVALID", "Graph logical revision must be positive.");
        }
        if (graph.Nodes.Count > MaximumNodes || graph.Links.Count > MaximumLinks || graph.Outputs.Count > MaximumOutputs)
        {
            Error(diagnostics, "REKALL_MODELING_GRAPH_BOUNDS_EXCEEDED", $"Graphs support at most {MaximumNodes} nodes, {MaximumLinks} links, and {MaximumOutputs} outputs.");
        }

        var nodes = UniqueNodes(graph.Nodes, diagnostics);
        var descriptors = new Dictionary<string, RekallAgeModelingNodeDescriptor>(StringComparer.Ordinal);
        foreach (var node in nodes.Values)
        {
            var descriptor = _catalog.Find(node.TypeId, node.TypeVersion);
            if (descriptor is null)
            {
                Error(diagnostics, "REKALL_MODELING_GRAPH_NODE_TYPE_UNKNOWN", $"Node type '{node.TypeId}@{node.TypeVersion}' is not registered.", node.NodeId);
                continue;
            }
            descriptors[node.NodeId] = descriptor;
            ValidateParameters(node, descriptor, diagnostics);
        }

        var validLinks = new List<RekallAgeModelingGraphLink>();
        var linkIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in graph.Links)
        {
            if (!ValidId(link.LinkId) || !linkIds.Add(link.LinkId))
            {
                Error(diagnostics, "REKALL_MODELING_GRAPH_LINK_ID_DUPLICATE", $"Link ID '{link.LinkId}' is invalid or duplicated.", linkId: link.LinkId);
                continue;
            }
            if (!nodes.TryGetValue(link.FromNodeId, out _) || !nodes.TryGetValue(link.ToNodeId, out _))
            {
                Error(diagnostics, "REKALL_MODELING_GRAPH_LINK_NODE_UNKNOWN", "A graph link references a missing node.", linkId: link.LinkId);
                continue;
            }
            if (!descriptors.TryGetValue(link.FromNodeId, out var sourceDescriptor)
                || !descriptors.TryGetValue(link.ToNodeId, out var targetDescriptor))
            {
                continue;
            }
            var sourcePort = sourceDescriptor.Ports.FirstOrDefault(port =>
                port.Direction == RekallAgeModelingPortDirection.Output && port.PortId.Equals(link.FromPortId, StringComparison.Ordinal));
            var targetPort = targetDescriptor.Ports.FirstOrDefault(port =>
                port.Direction == RekallAgeModelingPortDirection.Input && port.PortId.Equals(link.ToPortId, StringComparison.Ordinal));
            if (sourcePort is null || targetPort is null)
            {
                Error(diagnostics, "REKALL_MODELING_GRAPH_PORT_UNKNOWN", "A graph link references a missing directional port.", linkId: link.LinkId,
                    portId: sourcePort is null ? link.FromPortId : link.ToPortId);
                continue;
            }
            if (sourcePort.ValueType != targetPort.ValueType
                || sourcePort.Domain is not null && targetPort.Domain is not null && sourcePort.Domain != targetPort.Domain)
            {
                Error(diagnostics, "REKALL_MODELING_GRAPH_LINK_TYPE_MISMATCH",
                    $"Link '{link.LinkId}' cannot connect {sourcePort.ValueType} to {targetPort.ValueType}.",
                    linkId: link.LinkId, portId: link.ToPortId);
                continue;
            }
            validLinks.Add(link);
        }

        ValidateInputCardinality(nodes, descriptors, validLinks, diagnostics);
        var validOutputs = ValidateOutputs(graph.Outputs, nodes, descriptors, diagnostics);
        if (HasCycle(nodes.Keys, validLinks))
        {
            Error(diagnostics, "REKALL_MODELING_GRAPH_CYCLE", "Modelling graphs must be acyclic.");
        }

        var hasErrors = diagnostics.Any(item => item.Severity == RekallAgeModelingDiagnosticSeverity.Error);
        if (hasErrors)
        {
            return new(false, null, [], diagnostics);
        }

        var reachable = ReachableNodes(validOutputs, validLinks);
        var ordered = TopologicalOrder(reachable, validLinks);
        var unreachable = nodes.Keys.Where(nodeId => !reachable.Contains(nodeId)).Order(StringComparer.Ordinal).ToArray();
        var outputNodes = validOutputs.ToDictionary(
            output => output.Name,
            output => (IReadOnlyList<string>)TopologicalOrder(ReachableNodes([output], validLinks), validLinks),
            StringComparer.Ordinal);
        return new(
            true,
            new(graph.AssetId, graph.Revision, ordered, outputNodes),
            unreachable,
            diagnostics);
    }

    private static Dictionary<string, RekallAgeModelingGraphNode> UniqueNodes(
        IReadOnlyList<RekallAgeModelingGraphNode> source,
        List<RekallAgeModelingGraphDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, RekallAgeModelingGraphNode>(StringComparer.Ordinal);
        foreach (var node in source)
        {
            if (!ValidId(node.NodeId) || !result.TryAdd(node.NodeId, node))
            {
                Error(diagnostics, "REKALL_MODELING_GRAPH_NODE_ID_DUPLICATE", $"Node ID '{node.NodeId}' is invalid or duplicated.", node.NodeId);
            }
        }
        return result;
    }

    private static void ValidateParameters(
        RekallAgeModelingGraphNode node,
        RekallAgeModelingNodeDescriptor descriptor,
        List<RekallAgeModelingGraphDiagnostic> diagnostics)
    {
        var known = descriptor.Parameters.Select(parameter => parameter.ParameterId).ToHashSet(StringComparer.Ordinal);
        foreach (var parameter in node.Parameters)
        {
            if (!known.Contains(parameter.Key))
            {
                Error(diagnostics, "REKALL_MODELING_GRAPH_PARAMETER_UNKNOWN", $"Parameter '{parameter.Key}' is not declared by '{descriptor.TypeId}'.", node.NodeId);
            }
        }
    }

    private static void ValidateInputCardinality(
        IReadOnlyDictionary<string, RekallAgeModelingGraphNode> nodes,
        IReadOnlyDictionary<string, RekallAgeModelingNodeDescriptor> descriptors,
        IReadOnlyList<RekallAgeModelingGraphLink> links,
        List<RekallAgeModelingGraphDiagnostic> diagnostics)
    {
        foreach (var node in nodes.Values)
        {
            if (!descriptors.TryGetValue(node.NodeId, out var descriptor)) continue;
            foreach (var port in descriptor.Ports.Where(port => port.Direction == RekallAgeModelingPortDirection.Input))
            {
                var count = links.Count(link => link.ToNodeId == node.NodeId && link.ToPortId == port.PortId);
                if (port.Required && count == 0)
                {
                    Error(diagnostics, "REKALL_MODELING_GRAPH_REQUIRED_INPUT_MISSING", $"Required input '{port.PortId}' is not connected.", node.NodeId, portId: port.PortId);
                }
                if (!port.AllowsMultipleLinks && count > 1)
                {
                    Error(diagnostics, "REKALL_MODELING_GRAPH_INPUT_CARDINALITY", $"Input '{port.PortId}' accepts at most one link.", node.NodeId, portId: port.PortId);
                }
            }
        }
    }

    private static IReadOnlyList<RekallAgeModelingGraphOutput> ValidateOutputs(
        IReadOnlyList<RekallAgeModelingGraphOutput> outputs,
        IReadOnlyDictionary<string, RekallAgeModelingGraphNode> nodes,
        IReadOnlyDictionary<string, RekallAgeModelingNodeDescriptor> descriptors,
        List<RekallAgeModelingGraphDiagnostic> diagnostics)
    {
        var result = new List<RekallAgeModelingGraphOutput>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var output in outputs)
        {
            if (!ValidId(output.Name) || !names.Add(output.Name))
            {
                Error(diagnostics, "REKALL_MODELING_GRAPH_OUTPUT_DUPLICATE", $"Output name '{output.Name}' is invalid or duplicated.", output.NodeId, portId: output.PortId);
                continue;
            }
            if (!nodes.ContainsKey(output.NodeId) || !descriptors.TryGetValue(output.NodeId, out var descriptor))
            {
                Error(diagnostics, "REKALL_MODELING_GRAPH_OUTPUT_NODE_UNKNOWN", "A graph output references a missing node.", output.NodeId, portId: output.PortId);
                continue;
            }
            if (!descriptor.Ports.Any(port => port.Direction == RekallAgeModelingPortDirection.Output && port.PortId == output.PortId))
            {
                Error(diagnostics, "REKALL_MODELING_GRAPH_OUTPUT_PORT_UNKNOWN", "A graph output references a missing output port.", output.NodeId, portId: output.PortId);
                continue;
            }
            result.Add(output);
        }
        if (outputs.Count == 0)
        {
            Error(diagnostics, "REKALL_MODELING_GRAPH_OUTPUT_MISSING", "A modelling graph requires at least one named output.");
        }
        return result;
    }

    private static bool HasCycle(IEnumerable<string> nodeIds, IReadOnlyList<RekallAgeModelingGraphLink> links) =>
        TopologicalOrder(nodeIds.ToHashSet(StringComparer.Ordinal), links).Count != nodeIds.Count();

    private static HashSet<string> ReachableNodes(
        IReadOnlyList<RekallAgeModelingGraphOutput> outputs,
        IReadOnlyList<RekallAgeModelingGraphLink> links)
    {
        var reachable = outputs.Select(output => output.NodeId).ToHashSet(StringComparer.Ordinal);
        var pending = new Stack<string>(reachable);
        while (pending.TryPop(out var nodeId))
        {
            foreach (var dependency in links.Where(link => link.ToNodeId == nodeId).Select(link => link.FromNodeId))
            {
                if (reachable.Add(dependency)) pending.Push(dependency);
            }
        }
        return reachable;
    }

    private static IReadOnlyList<string> TopologicalOrder(
        IReadOnlySet<string> nodeIds,
        IReadOnlyList<RekallAgeModelingGraphLink> links)
    {
        var indegrees = nodeIds.ToDictionary(nodeId => nodeId, _ => 0, StringComparer.Ordinal);
        foreach (var link in links.Where(link => nodeIds.Contains(link.FromNodeId) && nodeIds.Contains(link.ToNodeId)))
        {
            indegrees[link.ToNodeId]++;
        }
        var ready = new SortedSet<string>(indegrees.Where(item => item.Value == 0).Select(item => item.Key), StringComparer.Ordinal);
        var result = new List<string>(nodeIds.Count);
        while (ready.Count > 0)
        {
            var nodeId = ready.Min!;
            ready.Remove(nodeId);
            result.Add(nodeId);
            foreach (var target in links.Where(link => link.FromNodeId == nodeId && nodeIds.Contains(link.ToNodeId)).Select(link => link.ToNodeId))
            {
                indegrees[target]--;
                if (indegrees[target] == 0) ready.Add(target);
            }
        }
        return result;
    }

    private static bool ValidId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static void Error(
        List<RekallAgeModelingGraphDiagnostic> diagnostics,
        string code,
        string message,
        string? nodeId = null,
        string? linkId = null,
        string? portId = null) =>
        diagnostics.Add(new(code, RekallAgeModelingDiagnosticSeverity.Error, message, nodeId, linkId, portId));
}
