using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMaterialGraphValidator
{
    public const int MaximumNodes = 2_048;
    public const int MaximumLinks = 8_192;
    public const int MaximumExposedParameters = 512;
    private readonly RekallAgeMaterialNodeCatalog _catalog;

    public RekallAgeMaterialGraphValidator(RekallAgeMaterialNodeCatalog catalog) => _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public RekallAgeMaterialGraphValidationReport Validate(RekallAgeMaterialGraphAsset graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var diagnostics = new List<RekallAgeModelingGraphDiagnostic>();
        if (graph.SchemaVersion != RekallAgeMaterialGraphAsset.CurrentSchemaVersion) Error(diagnostics, "REKALL_MATERIAL_GRAPH_SCHEMA_UNSUPPORTED", $"Material graph schema {graph.SchemaVersion} is unsupported.");
        if (!ValidId(graph.AssetId)) Error(diagnostics, "REKALL_MATERIAL_GRAPH_ASSET_ID_INVALID", "Material graph assetId must be a safe 1-128 character token.");
        if (string.IsNullOrWhiteSpace(graph.Name) || graph.Name.Length > 256) Error(diagnostics, "REKALL_MATERIAL_GRAPH_NAME_INVALID", "Material graph name must contain 1-256 characters.");
        if (graph.Revision < 1) Error(diagnostics, "REKALL_MATERIAL_GRAPH_REVISION_INVALID", "Material graph revision must be positive.");
        if (graph.Nodes.Count > MaximumNodes || graph.Links.Count > MaximumLinks || graph.ExposedParameters.Count > MaximumExposedParameters)
            Error(diagnostics, "REKALL_MATERIAL_GRAPH_BOUNDS_EXCEEDED", $"Material graphs support at most {MaximumNodes} nodes, {MaximumLinks} links, and {MaximumExposedParameters} exposed parameters.");

        var nodes = new Dictionary<string, RekallAgeMaterialGraphNode>(StringComparer.Ordinal);
        var descriptors = new Dictionary<string, RekallAgeMaterialNodeDescriptor>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            if (!ValidId(node.NodeId) || !nodes.TryAdd(node.NodeId, node)) { Error(diagnostics, "REKALL_MATERIAL_GRAPH_NODE_ID_DUPLICATE", $"Node ID '{node.NodeId}' is invalid or duplicated.", node.NodeId); continue; }
            var descriptor = _catalog.Find(node.TypeId, node.TypeVersion);
            if (descriptor is null) { Error(diagnostics, "REKALL_MATERIAL_GRAPH_NODE_TYPE_UNKNOWN", $"Node type '{node.TypeId}@{node.TypeVersion}' is not registered.", node.NodeId); continue; }
            descriptors[node.NodeId] = descriptor;
            ValidateParameters(node, descriptor, diagnostics);
        }

        var exposedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exposed in graph.ExposedParameters)
        {
            if (!ValidId(exposed.Name) || !exposedNames.Add(exposed.Name))
            {
                Error(diagnostics, "REKALL_MATERIAL_GRAPH_EXPOSED_ID_DUPLICATE", $"Exposed parameter name '{exposed.Name}' is invalid or duplicated.", exposed.NodeId);
                continue;
            }
            if (!descriptors.TryGetValue(exposed.NodeId, out var descriptor))
            {
                Error(diagnostics, "REKALL_MATERIAL_GRAPH_EXPOSED_NODE_UNKNOWN", $"Exposed parameter '{exposed.Name}' references an unknown node.", exposed.NodeId);
                continue;
            }
            var parameter = descriptor.Parameters.FirstOrDefault(item => item.ParameterId == exposed.ParameterId);
            if (parameter is null || parameter.ValueType != exposed.ValueType)
                Error(diagnostics, "REKALL_MATERIAL_GRAPH_EXPOSED_PARAMETER_INVALID", $"Exposed parameter '{exposed.Name}' does not match a typed node parameter.", exposed.NodeId);
        }

        var linkIds = new HashSet<string>(StringComparer.Ordinal);
        var validLinks = new List<RekallAgeMaterialGraphLink>();
        foreach (var link in graph.Links)
        {
            if (!ValidId(link.LinkId) || !linkIds.Add(link.LinkId)) { Error(diagnostics, "REKALL_MATERIAL_GRAPH_LINK_ID_DUPLICATE", $"Link ID '{link.LinkId}' is invalid or duplicated.", linkId: link.LinkId); continue; }
            if (!descriptors.TryGetValue(link.FromNodeId, out var source) || !descriptors.TryGetValue(link.ToNodeId, out var target)) { Error(diagnostics, "REKALL_MATERIAL_GRAPH_LINK_NODE_UNKNOWN", "A material graph link references a missing or invalid node.", linkId: link.LinkId); continue; }
            var output = source.Ports.FirstOrDefault(port => port.Direction == RekallAgeModelingPortDirection.Output && port.PortId == link.FromPortId);
            var input = target.Ports.FirstOrDefault(port => port.Direction == RekallAgeModelingPortDirection.Input && port.PortId == link.ToPortId);
            if (output is null || input is null) { Error(diagnostics, "REKALL_MATERIAL_GRAPH_LINK_PORT_UNKNOWN", "A material graph link references a missing or directionally invalid port.", linkId: link.LinkId); continue; }
            if (output.ValueType != input.ValueType) { Error(diagnostics, "REKALL_MATERIAL_GRAPH_LINK_TYPE_MISMATCH", $"Cannot link {output.ValueType} to {input.ValueType}.", linkId: link.LinkId); continue; }
            validLinks.Add(link);
        }

        foreach (var pair in descriptors)
        {
            foreach (var input in pair.Value.Ports.Where(port => port.Direction == RekallAgeModelingPortDirection.Input))
            {
                var count = validLinks.Count(link => link.ToNodeId == pair.Key && link.ToPortId == input.PortId);
                if (input.Required && count == 0) Error(diagnostics, "REKALL_MATERIAL_GRAPH_REQUIRED_INPUT_MISSING", $"Required input '{input.PortId}' is not connected.", pair.Key, portId: input.PortId);
                if (!input.AllowsMultipleLinks && count > 1) Error(diagnostics, "REKALL_MATERIAL_GRAPH_INPUT_CARDINALITY", $"Input '{input.PortId}' accepts at most one link.", pair.Key, portId: input.PortId);
            }
        }

        RekallAgeMaterialGraphExecutionPlan? plan = null;
        if (!nodes.TryGetValue(graph.Output.NodeId, out _) || !descriptors.TryGetValue(graph.Output.NodeId, out var outputDescriptor))
            Error(diagnostics, "REKALL_MATERIAL_GRAPH_OUTPUT_NODE_UNKNOWN", "Material output references a missing node.", graph.Output.NodeId);
        else
        {
            var port = outputDescriptor.Ports.FirstOrDefault(item => item.Direction == RekallAgeModelingPortDirection.Output && item.PortId == graph.Output.PortId);
            if (graph.Output.Name != "surface" || port?.ValueType != RekallAgeMaterialValueType.Surface)
                Error(diagnostics, "REKALL_MATERIAL_GRAPH_OUTPUT_INVALID", "Material graph output must be named 'surface' and reference a Surface output port.", graph.Output.NodeId, portId: graph.Output.PortId);
        }

        var ordered = Topological(nodes.Keys, validLinks, diagnostics);
        if (!diagnostics.Any(item => item.Severity == RekallAgeModelingDiagnosticSeverity.Error) && ordered is not null)
        {
            var reachable = Reachable(graph.Output.NodeId, validLinks);
            var execution = ordered.Where(reachable.Contains).ToArray();
            plan = new(graph.AssetId, graph.Revision, execution);
            var unreachable = ordered.Where(id => !reachable.Contains(id)).ToArray();
            return new(true, plan, unreachable, diagnostics);
        }
        return new(false, null, [], diagnostics);
    }

    private static void ValidateParameters(RekallAgeMaterialGraphNode node, RekallAgeMaterialNodeDescriptor descriptor, List<RekallAgeModelingGraphDiagnostic> diagnostics)
    {
        var allowed = descriptor.Parameters.ToDictionary(item => item.ParameterId, StringComparer.Ordinal);
        foreach (var parameter in node.Parameters)
        {
            if (!allowed.TryGetValue(parameter.Key, out var contract)) { Error(diagnostics, "REKALL_MATERIAL_GRAPH_PARAMETER_UNKNOWN", $"Parameter '{parameter.Key}' is not defined by '{descriptor.TypeId}'.", node.NodeId); continue; }
            if (parameter.Value is null) { Error(diagnostics, "REKALL_MATERIAL_GRAPH_PARAMETER_NULL", $"Parameter '{parameter.Key}' cannot be null.", node.NodeId); continue; }
            if (contract.ValueType == RekallAgeMaterialValueType.Float)
            {
                if (!parameter.Value.AsValue().TryGetValue<double>(out var number) || !double.IsFinite(number) || number < (contract.Minimum ?? double.NegativeInfinity) || number > (contract.Maximum ?? double.PositiveInfinity))
                    Error(diagnostics, "REKALL_MATERIAL_GRAPH_PARAMETER_RANGE", $"Parameter '{parameter.Key}' must be a finite number within its declared range.", node.NodeId);
            }
            else if (contract.ValueType is RekallAgeMaterialValueType.String or RekallAgeMaterialValueType.Texture2D or RekallAgeMaterialValueType.Color)
            {
                if (!parameter.Value.AsValue().TryGetValue<string>(out var text) || text.Length > 2_048 || contract.EnumChoices is { Count: > 0 } && !contract.EnumChoices.Contains(text, StringComparer.Ordinal))
                    Error(diagnostics, "REKALL_MATERIAL_GRAPH_PARAMETER_VALUE", $"Parameter '{parameter.Key}' is not a supported bounded string value.", node.NodeId);
            }
            else if (contract.ValueType == RekallAgeMaterialValueType.Vector2 && (parameter.Value is not JsonArray array || array.Count != 2 || array.Any(item => item is null || !item.AsValue().TryGetValue<double>(out var value) || !double.IsFinite(value))))
                Error(diagnostics, "REKALL_MATERIAL_GRAPH_PARAMETER_VALUE", $"Parameter '{parameter.Key}' must be a finite two-number array.", node.NodeId);
        }
    }

    private static IReadOnlyList<string>? Topological(IEnumerable<string> nodeIds, IReadOnlyList<RekallAgeMaterialGraphLink> links, List<RekallAgeModelingGraphDiagnostic> diagnostics)
    {
        var orderedIds = nodeIds.Order(StringComparer.Ordinal).ToArray();
        var indegree = orderedIds.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var outgoing = orderedIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var link in links) { indegree[link.ToNodeId]++; outgoing[link.FromNodeId].Add(link.ToNodeId); }
        var ready = new SortedSet<string>(indegree.Where(item => item.Value == 0).Select(item => item.Key), StringComparer.Ordinal);
        var result = new List<string>();
        while (ready.Count > 0) { var id = ready.Min!; ready.Remove(id); result.Add(id); foreach (var next in outgoing[id].Order(StringComparer.Ordinal)) if (--indegree[next] == 0) ready.Add(next); }
        if (result.Count != orderedIds.Length) { Error(diagnostics, "REKALL_MATERIAL_GRAPH_CYCLE", "Material graph contains a dependency cycle."); return null; }
        return result;
    }

    private static HashSet<string> Reachable(string outputNodeId, IReadOnlyList<RekallAgeMaterialGraphLink> links)
    {
        var result = new HashSet<string>(StringComparer.Ordinal); var stack = new Stack<string>(); stack.Push(outputNodeId);
        while (stack.TryPop(out var node) && result.Add(node)) foreach (var link in links.Where(item => item.ToNodeId == node)) stack.Push(link.FromNodeId);
        return result;
    }

    private static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 && char.IsAsciiLetterOrDigit(id[0]) && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    private static void Error(List<RekallAgeModelingGraphDiagnostic> diagnostics, string code, string message, string? nodeId = null, string? linkId = null, string? portId = null) => diagnostics.Add(new(code, RekallAgeModelingDiagnosticSeverity.Error, message, nodeId, linkId, portId));
}
