using System.Windows;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio;

/// <summary>
/// Pure auto-layout for the node-graph canvas: lays nodes out in columns by dependency depth
/// (a node with no incoming links sits in column 0; every other node sits one column right of the
/// deepest node feeding it), so the drawn graph always reads left-to-right in data-flow order.
/// Positions are a Studio-session-only view concern — they are never persisted to the graph asset.
/// </summary>
internal static class RekallAgeStudioModelingGraphLayout
{
    public static IReadOnlyDictionary<string, Point> ComputeDefaultPositions(
        IReadOnlyList<RekallAgeModelingGraphNode> nodes,
        IReadOnlyList<RekallAgeModelingGraphLink> links,
        double columnWidth = 220,
        double rowHeight = 110)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(links);

        var nodeIds = nodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
        var incomingByTarget = links
            .Where(link => nodeIds.Contains(link.FromNodeId) && nodeIds.Contains(link.ToNodeId))
            .ToLookup(link => link.ToNodeId, link => link.FromNodeId, StringComparer.Ordinal);

        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in nodes) ResolveColumn(node.NodeId, incomingByTarget, columns, []);

        var rowByColumn = new Dictionary<int, int>();
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var column = columns[node.NodeId];
            var row = rowByColumn.GetValueOrDefault(column);
            rowByColumn[column] = row + 1;
            positions[node.NodeId] = new Point(column * columnWidth, row * rowHeight);
        }
        return positions;
    }

    private static int ResolveColumn(
        string nodeId,
        ILookup<string, string> incomingByTarget,
        Dictionary<string, int> resolved,
        HashSet<string> visiting)
    {
        if (resolved.TryGetValue(nodeId, out var existing)) return existing;
        // A link cycle (which the evaluator itself would reject as unreachable/invalid) must not
        // recurse forever here; treat a node revisited mid-resolution as column 0 and move on.
        if (!visiting.Add(nodeId)) return 0;
        var upstream = incomingByTarget[nodeId].ToArray();
        var column = upstream.Length == 0
            ? 0
            : 1 + upstream.Max(id => ResolveColumn(id, incomingByTarget, resolved, visiting));
        visiting.Remove(nodeId);
        resolved[nodeId] = column;
        return column;
    }
}
