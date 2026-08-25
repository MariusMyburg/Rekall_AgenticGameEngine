using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeModelingGraphEvaluator
{
    private static NodeValue BisectGeometry(RekallAgeModelingGraphAsset graph, RekallAgeModelingGraphNode node, NodeValue input)
    {
        var point = ReadVector3(node, "planePoint", new(0, 0, 0));
        var normal = ReadVector3(node, "planeNormal", new(1, 0, 0));
        return ApplySemanticOperation(graph, node, input, "bisect_plane", new JsonObject
        {
            ["planeX"] = point.X, ["planeY"] = point.Y, ["planeZ"] = point.Z,
            ["normalX"] = normal.X, ["normalY"] = normal.Y, ["normalZ"] = normal.Z,
            ["clearPositive"] = ReadBoolean(node, "clearPositive", true),
            ["clearNegative"] = ReadBoolean(node, "clearNegative", false),
            ["fill"] = ReadBoolean(node, "fill", false)
        });
    }
}
