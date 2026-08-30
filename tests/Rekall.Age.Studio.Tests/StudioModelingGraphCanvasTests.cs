using System.Windows;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioModelingGraphLayoutTests
{
    [Fact]
    public void LinearChainGetsStrictlyIncreasingColumns()
    {
        var nodes = new[] { Node("a"), Node("b"), Node("c") };
        var links = new[] { Link("a", "b"), Link("b", "c") };

        var positions = RekallAgeStudioModelingGraphLayout.ComputeDefaultPositions(nodes, links);

        Assert.True(positions["a"].X < positions["b"].X);
        Assert.True(positions["b"].X < positions["c"].X);
    }

    [Fact]
    public void IndependentNodesWithNoLinksShareTheFirstColumn()
    {
        var nodes = new[] { Node("a"), Node("b") };

        var positions = RekallAgeStudioModelingGraphLayout.ComputeDefaultPositions(nodes, []);

        Assert.Equal(positions["a"].X, positions["b"].X);
        Assert.NotEqual(positions["a"].Y, positions["b"].Y);
    }

    [Fact]
    public void DiamondDependencyPlacesTheJoinNodeAfterBothBranches()
    {
        var nodes = new[] { Node("a"), Node("b"), Node("c"), Node("d") };
        var links = new[] { Link("a", "b"), Link("a", "c"), Link("b", "d"), Link("c", "d") };

        var positions = RekallAgeStudioModelingGraphLayout.ComputeDefaultPositions(nodes, links);

        Assert.True(positions["d"].X > positions["b"].X);
        Assert.True(positions["d"].X > positions["c"].X);
        Assert.True(positions["b"].X > positions["a"].X);
        Assert.True(positions["c"].X > positions["a"].X);
    }

    private static RekallAgeModelingGraphNode Node(string id) => new(id, "rekall.modeling.transform", 1, []);
    private static RekallAgeModelingGraphLink Link(string from, string to) => new($"{from}-{to}", from, "out", to, "in");
}

public sealed class StudioModelingGraphCanvasRendererTests
{
    [Fact]
    public void ViewTransformKeepsPointerAnchoredWhileZoomingAndSupportsPanning()
    {
        var view = RekallAgeStudioModelingGraphCanvasView.Identity;
        var anchor = new Point(320, 180);
        var worldAtAnchor = view.ScreenToWorld(anchor);

        var zoomed = view.ZoomAt(anchor, 1.5);

        Assert.Equal(worldAtAnchor, zoomed.ScreenToWorld(anchor));
        Assert.Equal(1.5, zoomed.Zoom);

        var panned = zoomed.PanBy(new Vector(24, -12));
        Assert.Equal(zoomed.Pan + new Vector(24, -12), panned.Pan);
        Assert.Equal(new Point(anchor.X + 24, anchor.Y - 12), panned.WorldToScreen(worldAtAnchor));
    }

    [Theory]
    [InlineData(0.01, 0.35)]
    [InlineData(20.0, 3.0)]
    public void ViewTransformClampsZoomToAUsableRange(double requestedZoom, double expectedZoom)
    {
        var changed = RekallAgeStudioModelingGraphCanvasView.Identity.ZoomAt(new Point(0, 0), requestedZoom);

        Assert.Equal(expectedZoom, changed.Zoom);
    }

    [Fact]
    public void FitBoundsCentersTheWholeGraphInsideTheCanvas()
    {
        var bounds = new Rect(0, 0, 1100, 260);

        var fitted = RekallAgeStudioModelingGraphCanvasView.FitBounds(bounds, 640, 360, 18);
        var topLeft = fitted.WorldToScreen(bounds.TopLeft);
        var bottomRight = fitted.WorldToScreen(bounds.BottomRight);

        Assert.InRange(topLeft.X, 17.9, 100);
        Assert.InRange(topLeft.Y, 17.9, 150);
        Assert.InRange(bottomRight.X, 540, 622.1);
        Assert.InRange(bottomRight.Y, 210, 342.1);
    }

    [Fact]
    public void NodeHitRegionsArePickableAtTheirCenterAndPortPointsResolveToTheOwningPort()
    {
        var catalog = RekallAgeModelingNodeCatalog.CreateDefault();
        var boxDescriptor = catalog.Find("rekall.modeling.primitive.box", 1)!;
        var transformDescriptor = catalog.Find("rekall.modeling.transform", 1)!;
        var nodes = new[]
        {
            new RekallAgeModelingGraphNode("box", boxDescriptor.TypeId, boxDescriptor.TypeVersion, []),
            new RekallAgeModelingGraphNode("xform", transformDescriptor.TypeId, transformDescriptor.TypeVersion, [])
        };
        var links = new[] { new RekallAgeModelingGraphLink("box-xform", "box", OutputPort(boxDescriptor), "xform", InputPort(transformDescriptor)) };
        var positions = RekallAgeStudioModelingGraphLayout.ComputeDefaultPositions(nodes, links);

        var renderer = new RekallAgeStudioModelingGraphCanvasRenderer();
        var frame = renderer.Render(nodes, links, positions, catalog, selectedNodeId: "box", 800, 600);

        Assert.Equal(800, frame.Image.PixelWidth);
        Assert.Equal(600, frame.Image.PixelHeight);
        var boxRegion = Assert.Contains("box", (IReadOnlyDictionary<string, Rect>)frame.NodeHitRegions);
        var boxCenter = new Point(boxRegion.X + boxRegion.Width / 2, boxRegion.Y + boxRegion.Height / 2);
        Assert.Equal("box", renderer.PickNode(frame, boxCenter.X, boxCenter.Y));
        Assert.Null(renderer.PickNode(frame, -50, -50));

        var outputPortKey = new RekallAgeStudioModelingGraphPortKey("box", OutputPort(boxDescriptor), true);
        var inputPortKey = new RekallAgeStudioModelingGraphPortKey("xform", InputPort(transformDescriptor), false);
        Assert.Contains(outputPortKey, (IReadOnlyDictionary<RekallAgeStudioModelingGraphPortKey, Point>)frame.PortPoints);
        Assert.Contains(inputPortKey, (IReadOnlyDictionary<RekallAgeStudioModelingGraphPortKey, Point>)frame.PortPoints);

        var pickedOutput = renderer.PickPort(frame, frame.PortPoints[outputPortKey].X, frame.PortPoints[outputPortKey].Y);
        Assert.Equal(outputPortKey, pickedOutput);

        var link = Assert.Single(frame.Links);
        Assert.Equal(frame.PortPoints[outputPortKey], link.From);
        Assert.Equal(frame.PortPoints[inputPortKey], link.To);
    }

    [Fact]
    public void RendererAppliesTheCanvasViewToNodesPortsAndPicking()
    {
        var catalog = RekallAgeModelingNodeCatalog.CreateDefault();
        var descriptor = catalog.Find("rekall.modeling.primitive.box", 1)!;
        var nodes = new[]
        {
            new RekallAgeModelingGraphNode("box", descriptor.TypeId, descriptor.TypeVersion, [])
        };
        var positions = new Dictionary<string, Point> { ["box"] = new(40, 30) };
        var view = new RekallAgeStudioModelingGraphCanvasView(2, new Vector(15, 25));

        var renderer = new RekallAgeStudioModelingGraphCanvasRenderer();
        var frame = renderer.Render(nodes, [], positions, catalog, "box", 800, 600, view);

        var region = frame.NodeHitRegions["box"];
        Assert.Equal(view.WorldToScreen(positions["box"]), region.TopLeft);
        Assert.True(region.Width >= 400, "Node contracts should remain legible when zoomed.");
        Assert.Equal("box", renderer.PickNode(frame, region.X + region.Width / 2, region.Y + region.Height / 2));
    }

    private static string OutputPort(RekallAgeModelingNodeDescriptor descriptor) =>
        descriptor.Ports.First(port => port.Direction == RekallAgeModelingPortDirection.Output).PortId;

    private static string InputPort(RekallAgeModelingNodeDescriptor descriptor) =>
        descriptor.Ports.First(port => port.Direction == RekallAgeModelingPortDirection.Input).PortId;
}
