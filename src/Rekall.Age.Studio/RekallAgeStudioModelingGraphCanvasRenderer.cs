using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio;

internal readonly record struct RekallAgeStudioModelingGraphPortKey(string NodeId, string PortId, bool IsOutput);

internal sealed record RekallAgeStudioModelingGraphCanvasLink(string LinkId, Point From, Point To);

internal sealed record RekallAgeStudioModelingGraphCanvasFrame(
    BitmapSource Image,
    IReadOnlyDictionary<string, Rect> NodeHitRegions,
    IReadOnlyDictionary<RekallAgeStudioModelingGraphPortKey, Point> PortPoints,
    IReadOnlyList<RekallAgeStudioModelingGraphCanvasLink> Links);

/// <summary>
/// Renders the procedural node graph's actual topology — node boxes at their (session-local, not
/// persisted) canvas positions, port dots, and real link lines — following the same raster
/// renderer + normalized hit-testing shape as <see cref="RekallAgeStudioMeshViewportRenderer"/>.
/// </summary>
internal sealed class RekallAgeStudioModelingGraphCanvasRenderer
{
    private const double NodeWidth = 180;
    private const double NodeHeight = 76;
    private const double PortHitRadius = 8;

    public RekallAgeStudioModelingGraphCanvasFrame Render(
        IReadOnlyList<RekallAgeModelingGraphNode> nodes,
        IReadOnlyList<RekallAgeModelingGraphLink> links,
        IReadOnlyDictionary<string, Point> positions,
        RekallAgeModelingNodeCatalog catalog,
        string? selectedNodeId,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(nodes); ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(positions); ArgumentNullException.ThrowIfNull(catalog);
        if (width < 64 || width > 4096 || height < 64 || height > 4096)
            throw new ArgumentOutOfRangeException(nameof(width), "Graph canvas dimensions must be 64-4096 pixels.");

        var nodeRegions = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var portPoints = new Dictionary<RekallAgeStudioModelingGraphPortKey, Point>();
        var descriptors = new Dictionary<string, RekallAgeModelingNodeDescriptor>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var origin = positions.GetValueOrDefault(node.NodeId);
            var region = new Rect(origin.X, origin.Y, NodeWidth, NodeHeight);
            nodeRegions[node.NodeId] = region;
            var descriptor = catalog.Find(node.TypeId, node.TypeVersion);
            if (descriptor is null) continue;
            descriptors[node.NodeId] = descriptor;
            var inputs = descriptor.Ports.Where(port => port.Direction == RekallAgeModelingPortDirection.Input).ToArray();
            var outputs = descriptor.Ports.Where(port => port.Direction == RekallAgeModelingPortDirection.Output).ToArray();
            for (var index = 0; index < inputs.Length; index++)
                portPoints[new(node.NodeId, inputs[index].PortId, false)] =
                    new Point(region.Left, region.Top + region.Height * (index + 1) / (inputs.Length + 1));
            for (var index = 0; index < outputs.Length; index++)
                portPoints[new(node.NodeId, outputs[index].PortId, true)] =
                    new Point(region.Right, region.Top + region.Height * (index + 1) / (outputs.Length + 1));
        }

        var canvasLinks = new List<RekallAgeStudioModelingGraphCanvasLink>();
        foreach (var link in links)
        {
            if (portPoints.TryGetValue(new(link.FromNodeId, link.FromPortId, true), out var from)
                && portPoints.TryGetValue(new(link.ToNodeId, link.ToPortId, false), out var to))
                canvasLinks.Add(new(link.LinkId, from, to));
        }

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 18, 24)), null, new Rect(0, 0, width, height));
            var linkPen = new Pen(new SolidColorBrush(Color.FromRgb(97, 130, 158)), 2);
            foreach (var link in canvasLinks)
                drawing.DrawLine(linkPen, link.From, link.To);
            foreach (var node in nodes)
            {
                var region = nodeRegions[node.NodeId];
                var selected = string.Equals(node.NodeId, selectedNodeId, StringComparison.Ordinal);
                var fill = new SolidColorBrush(selected ? Color.FromRgb(38, 92, 110) : Color.FromRgb(30, 36, 44));
                var outline = new Pen(new SolidColorBrush(selected ? Color.FromRgb(85, 214, 229) : Color.FromRgb(70, 82, 96)), selected ? 2 : 1);
                drawing.DrawRoundedRectangle(fill, outline, region, 6, 6);
                var displayName = descriptors.TryGetValue(node.NodeId, out var descriptor) ? descriptor.DisplayName : node.TypeId;
                drawing.DrawText(new FormattedText(displayName, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), 11, new SolidColorBrush(Color.FromRgb(224, 229, 236)), 1),
                    new Point(region.Left + 8, region.Top + 8));
                drawing.DrawText(new FormattedText(node.NodeId, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 9, new SolidColorBrush(Color.FromRgb(150, 160, 172)), 1),
                    new Point(region.Left + 8, region.Top + 26));
            }
            var portBrush = new SolidColorBrush(Color.FromRgb(255, 190, 72));
            foreach (var point in portPoints.Values)
                drawing.DrawEllipse(portBrush, null, point, 4, 4);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return new(bitmap, nodeRegions, portPoints, canvasLinks);
    }

    public string? PickNode(RekallAgeStudioModelingGraphCanvasFrame frame, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var point = new Point(x, y);
        return frame.NodeHitRegions.FirstOrDefault(entry => entry.Value.Contains(point)).Key;
    }

    public RekallAgeStudioModelingGraphPortKey? PickPort(RekallAgeStudioModelingGraphCanvasFrame frame, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var point = new Point(x, y);
        return frame.PortPoints
            .Select(entry => (entry.Key, Distance: (entry.Value - point).Length))
            .Where(entry => entry.Distance <= PortHitRadius)
            .OrderBy(entry => entry.Distance)
            .Select(entry => (RekallAgeStudioModelingGraphPortKey?)entry.Key)
            .FirstOrDefault();
    }
}
