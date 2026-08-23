using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMeshPrimitiveFactory
{
    private static readonly IReadOnlyDictionary<string, string> TypeIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["box"] = "rekall.modeling.primitive.box",
            ["grid"] = "rekall.modeling.primitive.grid",
            ["sphere"] = "rekall.modeling.primitive.sphere",
            ["cylinder"] = "rekall.modeling.primitive.frustum",
            ["cone"] = "rekall.modeling.primitive.frustum",
            ["torus"] = "rekall.modeling.primitive.torus"
        };

    private readonly RekallAgeModelingGraphEvaluator _evaluator;

    public RekallAgeMeshPrimitiveFactory(RekallAgeModelingGraphEvaluator? evaluator = null) =>
        _evaluator = evaluator ?? new RekallAgeModelingGraphEvaluator();

    public IReadOnlyList<string> SupportedPrimitives => TypeIds.Keys
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    public async ValueTask<RekallAgeMeshAsset> CreateAsync(
        string primitive,
        string assetId,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primitive);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = primitive.Trim().ToLowerInvariant();
        if (!TypeIds.TryGetValue(normalized, out var typeId))
        {
            throw new ArgumentException(
                $"Unsupported editable mesh primitive '{primitive}'. Supported: {string.Join(", ", SupportedPrimitives)}.",
                nameof(primitive));
        }

        var parameters = new JsonObject();
        if (normalized == "cone") parameters["radiusTop"] = 0;
        var node = new RekallAgeModelingGraphNode("primitive", typeId, 1, parameters);
        var graph = RekallAgeModelingGraphAsset.Create(
            $"factory.{normalized}",
            $"{name} primitive factory",
            [node],
            [],
            [new RekallAgeModelingGraphOutput("mesh", node.NodeId, "geometry")]);
        var result = await _evaluator.EvaluateAsync(
            graph,
            ["mesh"],
            RekallAgeModelingEvaluationBudget.Default,
            new RekallAgeModelingEvaluationContext(0, 0, "studio", "editable-mesh"),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || !result.Outputs.TryGetValue("mesh", out var mesh))
        {
            var diagnostics = string.Join("; ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
            throw new InvalidOperationException($"Editable {normalized} primitive evaluation failed. {diagnostics}".Trim());
        }

        return mesh with { AssetId = assetId.Trim(), Name = name.Trim(), Revision = 1 };
    }
}
