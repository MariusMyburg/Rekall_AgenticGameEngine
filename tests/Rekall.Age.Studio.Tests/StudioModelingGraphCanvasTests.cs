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

    private static string OutputPort(RekallAgeModelingNodeDescriptor descriptor) =>
        descriptor.Ports.First(port => port.Direction == RekallAgeModelingPortDirection.Output).PortId;

    private static string InputPort(RekallAgeModelingNodeDescriptor descriptor) =>
        descriptor.Ports.First(port => port.Direction == RekallAgeModelingPortDirection.Input).PortId;
}
