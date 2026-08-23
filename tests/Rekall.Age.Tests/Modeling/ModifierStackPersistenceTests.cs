using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class ModifierStackPersistenceTests
{
    [Fact]
    public async Task PatchAtomicallyConfiguresAndReordersAndBakePublishesOrdinaryMesh()
    {
        var root = TestPaths.CreateTempDirectory();
        var meshStore = new RekallAgeMeshAssetStore();
        var source = await Box();
        var sourceRevision = await meshStore.SaveIfRevisionAsync(root, source with { AssetId = "source", Name = "Source" }, RekallAgeDocumentRevision.Missing, CancellationToken.None);
        var stackStore = new RekallAgeModifierStackAssetStore();
        var stack = RekallAgeModifierStackAsset.Create("stack", "Stack", "source", sourceRevision,
        [
            new("move", "rekall.modifier.transform", 1, true, new JsonObject { ["x"] = 1.0 }),
            new("triangulate", "rekall.modifier.triangulate", 1, true, new JsonObject())
        ]);
        var firstRevision = await stackStore.SaveIfRevisionAsync(root, stack, RekallAgeDocumentRevision.Missing, CancellationToken.None);
        var patchTransaction = RekallAgeTransaction.Begin("patch stack");

        var patched = await new RekallAgeModifierStackPatchService().ApplyAsync(root, "stack", firstRevision,
            new([
                new(RekallAgeModifierStackPatchKind.Configure, TargetId: "move", Parameters: new JsonObject { ["x"] = 4.0 }),
                new(RekallAgeModifierStackPatchKind.Move, TargetId: "triangulate", NewIndex: 0)
            ]), patchTransaction, CancellationToken.None);

        Assert.Equal(["triangulate", "move"], patched.Stack.Modifiers.Select(item => item.ModifierId));
        Assert.Single(patchTransaction.ResourcePreimages);
        var preview = await new RekallAgeModifierStackEvaluationService().EvaluateAsync(root, "stack", RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);
        Assert.Equal(4.5, preview.Mesh!.Topology.Positions.Max(item => item.X));
        Assert.False(File.Exists(meshStore.GetMeshPath(root, "baked")));

        var bakeTransaction = RekallAgeTransaction.Begin("bake stack");
        var baked = await new RekallAgeModifierStackBakeService().BakeAsync(root, "stack", "baked", RekallAgeDocumentRevision.Missing,
            RekallAgeModelingEvaluationBudget.Default, bakeTransaction, CancellationToken.None);
        var compiled = new RekallAgeMeshCompiler().Compile(await meshStore.LoadAsync(root, "baked", CancellationToken.None));
        Assert.Equal(12, compiled.Triangles.Count);
        Assert.Single(bakeTransaction.ChangedResources);
        Assert.Equal(patched.Stack.Revision, baked.StackLogicalRevision);
    }

    [Fact]
    public async Task StalePatchLeavesCanonicalStackBytesUnchanged()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeModifierStackAssetStore();
        var stack = RekallAgeModifierStackAsset.Create("stack", "Stack", "source", new string('a', 64),
            [new("move", "rekall.modifier.transform", 1, true, new JsonObject())]);
        await store.SaveIfRevisionAsync(root, stack, RekallAgeDocumentRevision.Missing, CancellationToken.None);
        var before = await File.ReadAllBytesAsync(store.GetStackPath(root, "stack"));

        await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(async () => await new RekallAgeModifierStackPatchService().ApplyAsync(
            root, "stack", new string('0', 64), new([new(RekallAgeModifierStackPatchKind.SetEnabled, TargetId: "move", Enabled: false)]),
            RekallAgeTransaction.Begin("stale"), CancellationToken.None));

        Assert.Equal(before, await File.ReadAllBytesAsync(store.GetStackPath(root, "stack")));
    }

    private static async ValueTask<RekallAgeMeshAsset> Box()
    {
        var graph = RekallAgeModelingGraphAsset.Create("box", "Box",
            [new("box", "rekall.modeling.primitive.box", 1, new JsonObject()), new("output", "rekall.modeling.output.mesh", 1, new JsonObject())],
            [new("link", "box", "geometry", "output", "input")], [new("mesh", "output", "geometry")]);
        return (await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None)).Outputs["mesh"];
    }
}
