using System.Text.Json.Nodes;
using System.IO;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioModelingGraphSessionTests
{
    [Fact]
    public async Task CreatesAndOpensAnImmediatelyEvaluatableStarterGeometryGraph()
    {
        var root = TemporaryRoot();
        try
        {
            var session = new RekallAgeStudioModelingGraphSession();

            await session.CreateStarterAsync(root, "geometry-1", "Geometry 1", "rekall.modeling.primitive.box", CancellationToken.None);
            var result = await session.EvaluateAsync("mesh", CancellationToken.None);

            Assert.Equal("geometry-1", session.Graph!.AssetId);
            Assert.Equal("Box", Assert.Single(session.Nodes).DisplayName);
            Assert.Equal(["mesh"], session.OutputNames);
            Assert.True(result.Succeeded);
            Assert.NotNull(session.OutputMesh);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpensPersistedGraphAndExposesCanonicalNodeContracts()
    {
        var root = TemporaryRoot();
        try
        {
            await new RekallAgeModelingGraphAssetStore().SaveAsync(root, BoxGraph(), CancellationToken.None);
            var session = new RekallAgeStudioModelingGraphSession();

            Assert.Equal(["box-graph"], session.ListAssets(root));
            await session.OpenAsync(root, "box-graph", CancellationToken.None);

            Assert.Equal("Box Graph", session.Graph!.Name);
            Assert.False(string.IsNullOrWhiteSpace(session.FileRevision));
            Assert.Equal(["mesh"], session.OutputNames);
            var box = Assert.Single(session.Nodes, item => item.NodeId == "box");
            Assert.Equal("Box", box.DisplayName);
            Assert.Contains(box.Parameters, item => item.ParameterId == "sizeX" && item.DisplayName == "Size X");
            Assert.Equal(0, box.IncomingLinkCount);
            Assert.Equal(1, box.OutgoingLinkCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReusesEvaluatorCacheAndPublishesOutputAndNodeEvidence()
    {
        var root = TemporaryRoot();
        try
        {
            await new RekallAgeModelingGraphAssetStore().SaveAsync(root, BoxGraph(), CancellationToken.None);
            var session = new RekallAgeStudioModelingGraphSession();
            await session.OpenAsync(root, "box-graph", CancellationToken.None);

            var first = await session.EvaluateAsync("mesh", CancellationToken.None);
            var cached = await session.EvaluateAsync("mesh", CancellationToken.None);

            Assert.True(first.Succeeded);
            Assert.Equal(0, first.CacheHitCount);
            Assert.True(cached.Succeeded);
            Assert.Equal(2, cached.CacheHitCount);
            Assert.Equal("mesh", session.SelectedOutputName);
            Assert.NotNull(session.OutputMesh);
            Assert.Equal(8, session.OutputMesh!.Topology.PointIds.Count);
            Assert.All(session.Nodes, item => Assert.True(item.LastEvaluation?.CacheHit));
            Assert.Contains("2 cache hit", session.EvaluationSummary, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsEvaluationBeforeOpeningAProjectGraph()
    {
        var session = new RekallAgeStudioModelingGraphSession();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.EvaluateAsync("mesh", CancellationToken.None));
        Assert.Contains("Open a procedural graph", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppliesRevisionSafePatchRecordsTransactionAndInvalidatesEvaluation()
    {
        var root = TemporaryRoot();
        try
        {
            await new RekallAgeModelingGraphAssetStore().SaveAsync(root, BoxGraph(), CancellationToken.None);
            var session = new RekallAgeStudioModelingGraphSession();
            await session.OpenAsync(root, "box-graph", CancellationToken.None);
            await session.EvaluateAsync("mesh", CancellationToken.None);
            var beforeRevision = session.FileRevision;

            var result = await session.ApplyPatchAsync(
                new([new(
                    RekallAgeModelingGraphPatchKind.SetParameter,
                    TargetId: "box",
                    ParameterId: "sizeX",
                    Value: JsonValue.Create(4.0))]),
                "studio",
                CancellationToken.None);
            var evaluated = await session.EvaluateAsync("mesh", CancellationToken.None);

            Assert.Equal(2, result.Graph.Revision);
            Assert.NotEqual(beforeRevision, session.FileRevision);
            Assert.Contains(session.Nodes.Single(item => item.NodeId == "box").Parameters,
                item => item.ParameterId == "sizeX" && item.Value == "4");
            Assert.Equal(-2, session.OutputMesh!.Topology.Positions.Min(item => item.X));
            Assert.Equal(2, session.OutputMesh.Topology.Positions.Max(item => item.X));
            Assert.Equal(2, evaluated.InvalidatedNodeCount);
            Assert.Single((await new RekallAgeTransactionLogStore().LoadAsync(root, CancellationToken.None)).Transactions);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TypedParameterEditorsApplyOnlyChangedValuesAsOnePatch()
    {
        var root = TemporaryRoot();
        try
        {
            await new RekallAgeModelingGraphAssetStore().SaveAsync(root, BoxGraph(), CancellationToken.None);
            var session = new RekallAgeStudioModelingGraphSession();
            await session.OpenAsync(root, "box-graph", CancellationToken.None);
            var editors = session.CreateParameterEditors("box");
            var sizeX = editors.Single(item => item.ParameterId == "sizeX");

            Assert.Equal("2", sizeX.ValueText);
            Assert.False(sizeX.IsModified);
            sizeX.ValueText = "not-a-number";
            Assert.False(sizeX.IsValid);
            sizeX.ValueText = "4";
            Assert.True(sizeX.IsValid);
            Assert.True(sizeX.IsModified);

            var result = await session.ApplyParameterEditsAsync("box", editors, "studio", CancellationToken.None);

            Assert.Equal(1, result.AppliedOperationCount);
            Assert.Equal(4, session.Graph!.Nodes.Single(item => item.NodeId == "box").Parameters["sizeX"]!.GetValue<double>());
            Assert.All(editors, item => Assert.False(item.IsModified));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TypedParameterEditorParsesFiniteVectorsAndProvidesEnumChoices()
    {
        var transform = RekallAgeModelingNodeCatalog.CreateDefault().Find("rekall.modeling.transform", 1)!;
        var translation = new RekallAgeStudioModelingGraphParameterModel(
            transform.Parameters.Single(item => item.ParameterId == "translation"),
            new JsonArray(1.0, 2.0, 3.0));

        translation.ValueText = "[1, 2]";
        Assert.False(translation.IsValid);
        translation.ValueText = "[4, 5, 6]";
        Assert.True(translation.TryGetValue(out var vector));
        Assert.Equal(6, vector![2]!.GetValue<double>());

        var boolean = RekallAgeModelingNodeCatalog.CreateDefault().Find("rekall.modeling.boolean", 1)!;
        var operation = new RekallAgeStudioModelingGraphParameterModel(
            boolean.Parameters.Single(item => item.ParameterId == "operation"),
            JsonValue.Create("union"));
        Assert.Equal(["union", "intersect", "difference"], operation.EnumChoices);
        operation.ValueText = "unsupported";
        Assert.False(operation.IsValid);
    }

    [Fact]
    public void StructuredParameterEditorRoundTripsCurveDocumentsAsJsonObjectsInsteadOfQuotedStrings()
    {
        var source = RekallAgeModelingNodeCatalog.CreateDefault().Find("rekall.modeling.curve.source", 1)!;
        var document = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["assetId"] = "curve.studio",
            ["name"] = "Studio Curve",
            ["revision"] = 1,
            ["splines"] = new JsonArray()
        };
        var editor = new RekallAgeStudioModelingGraphParameterModel(
            source.Parameters.Single(item => item.ParameterId == "document"), document);

        Assert.Equal(RekallAgeModelingValueType.Json, source.Parameters.Single(item => item.ParameterId == "document").ValueType);
        Assert.True(editor.TryGetValue(out var initial));
        Assert.IsType<JsonObject>(initial);
        editor.ValueText = "{\"schemaVersion\":1,\"assetId\":\"curve.changed\",\"splines\":[]}";
        Assert.True(editor.IsValid);
        Assert.True(editor.IsModified);
        Assert.Equal("curve.changed", Assert.IsType<JsonObject>(AssertParsed(editor))["assetId"]!.GetValue<string>());
        editor.ValueText = "not-json";
        Assert.False(editor.IsValid);

        static JsonNode AssertParsed(RekallAgeStudioModelingGraphParameterModel parameter)
        {
            Assert.True(parameter.TryGetValue(out var value));
            return value!;
        }
    }

    private static string TemporaryRoot() =>
        Path.Combine(Path.GetTempPath(), "rekall-age-studio-graph-" + Guid.NewGuid().ToString("N"));

    private static RekallAgeModelingGraphAsset BoxGraph() => RekallAgeModelingGraphAsset.Create(
        "box-graph",
        "Box Graph",
        [
            new("box", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = 2.0 }),
            new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
        ],
        [new("box-output", "box", "geometry", "output", "input")],
        [new("mesh", "output", "geometry")]);
}
