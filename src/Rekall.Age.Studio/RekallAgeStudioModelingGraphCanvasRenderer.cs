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

internal readonly record struct RekallAgeStudioModelingGraphCanvasView(double Zoom, Vector Pan)
{
    public const double MinimumZoom = 0.35;
    public const double MaximumZoom = 3.0;

    public static RekallAgeStudioModelingGraphCanvasView Identity => new(1, new Vector(18, 18));

    public Point WorldToScreen(Point point) => new(point.X * Zoom + Pan.X, point.Y * Zoom + Pan.Y);

    public Point ScreenToWorld(Point point) => new((point.X - Pan.X) / Zoom, (point.Y - Pan.Y) / Zoom);

    public RekallAgeStudioModelingGraphCanvasView PanBy(Vector delta) => this with { Pan = Pan + delta };

    public RekallAgeStudioModelingGraphCanvasView ZoomAt(Point screenAnchor, double factor)
    {
        var worldAnchor = ScreenToWorld(screenAnchor);
        var nextZoom = Math.Clamp(Zoom * factor, MinimumZoom, MaximumZoom);
        return new(nextZoom, new Vector(
            screenAnchor.X - worldAnchor.X * nextZoom,
            screenAnchor.Y - worldAnchor.Y * nextZoom));
    }

    public static RekallAgeStudioModelingGraphCanvasView FitBounds(
        Rect worldBounds,
        double canvasWidth,
        double canvasHeight,
        double padding)
    {
        if (worldBounds.IsEmpty || worldBounds.Width <= 0 || worldBounds.Height <= 0
            || canvasWidth <= padding * 2 || canvasHeight <= padding * 2)
            return Identity;
        var zoom = Math.Clamp(Math.Min(
            (canvasWidth - padding * 2) / worldBounds.Width,
            (canvasHeight - padding * 2) / worldBounds.Height), MinimumZoom, MaximumZoom);
        var pan = new Vector(
            (canvasWidth - worldBounds.Width * zoom) / 2 - worldBounds.Left * zoom,
            (canvasHeight - worldBounds.Height * zoom) / 2 - worldBounds.Top * zoom);
        return new(zoom, pan);
    }
}

/// <summary>
/// Renders the procedural node graph's actual topology — node boxes at their (session-local, not
/// persisted) canvas positions, port dots, and real link lines — following the same raster
/// renderer + normalized hit-testing shape as <see cref="RekallAgeStudioMeshViewportRenderer"/>.
/// </summary>
internal sealed class RekallAgeStudioModelingGraphCanvasRenderer
{
    private const double NodeWidth = 220;
    private const double MinimumNodeHeight = 96;
    private const double PortHitRadius = 8;

    public RekallAgeStudioModelingGraphCanvasFrame Render(
        IReadOnlyList<RekallAgeModelingGraphNode> nodes,
        IReadOnlyList<RekallAgeModelingGraphLink> links,
        IReadOnlyDictionary<string, Point> positions,
        RekallAgeModelingNodeCatalog catalog,
        string? selectedNodeId,
        int width,
        int height,
        RekallAgeStudioModelingGraphCanvasView? canvasView = null)
    {
        ArgumentNullException.ThrowIfNull(nodes); ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(positions); ArgumentNullException.ThrowIfNull(catalog);
        if (width < 64 || width > 4096 || height < 64 || height > 4096)
            throw new ArgumentOutOfRangeException(nameof(width), "Graph canvas dimensions must be 64-4096 pixels.");

        var view = canvasView ?? RekallAgeStudioModelingGraphCanvasView.Identity;
        var nodeRegions = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var portPoints = new Dictionary<RekallAgeStudioModelingGraphPortKey, Point>();
        var descriptors = new Dictionary<string, RekallAgeModelingNodeDescriptor>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var origin = positions.GetValueOrDefault(node.NodeId);
            var descriptor = catalog.Find(node.TypeId, node.TypeVersion);
            var inputCount = descriptor?.Ports.Count(port => port.Direction == RekallAgeModelingPortDirection.Input) ?? 0;
            var outputCount = descriptor?.Ports.Count(port => port.Direction == RekallAgeModelingPortDirection.Output) ?? 0;
            var worldHeight = Math.Max(MinimumNodeHeight, 62 + Math.Max(inputCount, outputCount) * 22);
            var screenOrigin = view.WorldToScreen(origin);
            var region = new Rect(screenOrigin.X, screenOrigin.Y, NodeWidth * view.Zoom, worldHeight * view.Zoom);
            nodeRegions[node.NodeId] = region;
            if (descriptor is null) continue;
            descriptors[node.NodeId] = descriptor;
            var inputs = descriptor.Ports.Where(port => port.Direction == RekallAgeModelingPortDirection.Input).ToArray();
            var outputs = descriptor.Ports.Where(port => port.Direction == RekallAgeModelingPortDirection.Output).ToArray();
            for (var index = 0; index < inputs.Length; index++)
                portPoints[new(node.NodeId, inputs[index].PortId, false)] =
                    view.WorldToScreen(new Point(origin.X, origin.Y + 58 + index * 22));
            for (var index = 0; index < outputs.Length; index++)
                portPoints[new(node.NodeId, outputs[index].PortId, true)] =
                    view.WorldToScreen(new Point(origin.X + NodeWidth, origin.Y + 58 + index * 22));
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
            DrawGrid(drawing, width, height, view);
            var linkPen = new Pen(new SolidColorBrush(Color.FromRgb(97, 154, 190)), Math.Clamp(2 * view.Zoom, 1, 4));
            foreach (var link in canvasLinks)
                DrawLink(drawing, linkPen, link.From, link.To);
            foreach (var node in nodes)
            {
                var region = nodeRegions[node.NodeId];
                var selected = string.Equals(node.NodeId, selectedNodeId, StringComparison.Ordinal);
                var fill = new SolidColorBrush(selected ? Color.FromRgb(38, 92, 110) : Color.FromRgb(30, 36, 44));
                var outline = new Pen(new SolidColorBrush(selected ? Color.FromRgb(85, 214, 229) : Color.FromRgb(70, 82, 96)), selected ? 2 : 1);
                drawing.DrawRoundedRectangle(fill, outline, region, 6, 6);
                drawing.DrawRoundedRectangle(new SolidColorBrush(selected ? Color.FromRgb(31, 116, 137) : Color.FromRgb(39, 47, 57)), null,
                    new Rect(region.Left, region.Top, region.Width, 43 * view.Zoom), 6, 6);
                var displayName = descriptors.TryGetValue(node.NodeId, out var descriptor) ? descriptor.DisplayName : node.TypeId;
                drawing.DrawText(new FormattedText(displayName, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), Math.Clamp(12 * view.Zoom, 8, 24), new SolidColorBrush(Color.FromRgb(238, 242, 247)), 1),
                    new Point(region.Left + 9 * view.Zoom, region.Top + 5 * view.Zoom));
                drawing.DrawText(new FormattedText(node.NodeId, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), Math.Clamp(9 * view.Zoom, 7, 18), new SolidColorBrush(Color.FromRgb(166, 180, 194)), 1),
                    new Point(region.Left + 9 * view.Zoom, region.Top + 24 * view.Zoom));

                if (descriptor is not null)
                {
                    var inputs = descriptor.Ports.Where(port => port.Direction == RekallAgeModelingPortDirection.Input).ToArray();
                    var outputs = descriptor.Ports.Where(port => port.Direction == RekallAgeModelingPortDirection.Output).ToArray();
                    for (var index = 0; index < inputs.Length; index++)
                        DrawPortLabel(drawing, inputs[index].PortId, region.Left + 10 * view.Zoom,
                            region.Top + (49 + index * 22) * view.Zoom, view.Zoom, TextAlignment.Left);
                    for (var index = 0; index < outputs.Length; index++)
                        DrawPortLabel(drawing, outputs[index].PortId, region.Right - 104 * view.Zoom,
                            region.Top + (49 + index * 22) * view.Zoom, view.Zoom, TextAlignment.Right);
                }
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

    private static void DrawGrid(DrawingContext drawing, int width, int height, RekallAgeStudioModelingGraphCanvasView view)
    {
        var spacing = 32 * view.Zoom;
        if (spacing < 10) return;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(70, 66, 78, 92)), 1);
        var startX = ((view.Pan.X % spacing) + spacing) % spacing;
        var startY = ((view.Pan.Y % spacing) + spacing) % spacing;
        for (var x = startX; x < width; x += spacing) drawing.DrawLine(pen, new Point(x, 0), new Point(x, height));
        for (var y = startY; y < height; y += spacing) drawing.DrawLine(pen, new Point(0, y), new Point(width, y));
    }

    private static void DrawLink(DrawingContext drawing, Pen pen, Point from, Point to)
    {
        var bend = Math.Max(34, Math.Abs(to.X - from.X) * 0.45);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(from, false, false);
            context.BezierTo(new Point(from.X + bend, from.Y), new Point(to.X - bend, to.Y), to, true, false);
        }
        geometry.Freeze();
        drawing.DrawGeometry(null, pen, geometry);
    }

    private static void DrawPortLabel(DrawingContext drawing, string text, double x, double y, double zoom, TextAlignment alignment)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), Math.Clamp(9.5 * zoom, 7, 18), new SolidColorBrush(Color.FromRgb(195, 205, 216)), 1)
        {
            MaxTextWidth = 94 * zoom,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment
        };
        drawing.DrawText(formatted, new Point(x, y));
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
