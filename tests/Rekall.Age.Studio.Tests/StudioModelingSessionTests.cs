using System.Text.Json.Nodes;
using System.IO;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioModelingSessionTests
{
    [Fact]
    public void TypedParameterEditorUsesDescriptorDefaultsAndRejectsInvalidNumbers()
    {
        var descriptor = new RekallAgeMeshOperationExecutor().Descriptors.Single(item => item.OperationId == "extrude_faces");
        var z = new RekallAgeStudioMeshParameterModel(descriptor.Parameters.Single(item => item.Name == "z"));

        Assert.Equal("1", z.ValueText);
        Assert.True(z.TryGetValue(out var defaultValue));
        Assert.Equal(1, defaultValue!.GetValue<double>());
        z.ValueText = "not-a-number";
        Assert.False(z.IsValid);
        Assert.False(z.TryGetValue(out _));
    }

    [Fact]
    public async Task StudioMeshSessionPreviewsWithoutMutationThenAppliesThroughTransactionHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-modeling-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RekallAgeMeshAssetStore();
            await store.SaveAsync(root, Quad(), CancellationToken.None);
            var session = new RekallAgeStudioModelingSession();

            Assert.Equal(["quad"], session.ListAssets(root));
            await session.OpenAsync(root, "quad", CancellationToken.None);
            session.Select(21);
            Assert.Equal(21UL, session.ActiveElementId);
            Assert.Contains(session.AvailableOperations, item => item.OperationId == "extrude_faces");

            var preview = await session.PreviewAsync("extrude_faces",
                new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 1.0 }, CancellationToken.None);
            Assert.Equal(5, preview.Mesh.Topology.FaceIds.Count);
            Assert.Single((await store.LoadAsync(root, "quad", CancellationToken.None)).Topology.FaceIds);

            var applied = await session.ApplyAsync("extrude_faces",
                new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 1.0 }, "studio", CancellationToken.None);
            Assert.Equal(5, applied.Mesh.Topology.FaceIds.Count);
            Assert.Equal(5, session.Mesh!.Topology.FaceIds.Count);
            Assert.Null(session.Preview);
            Assert.Equal([21UL], session.SelectedElementIds);
            Assert.Single((await new RekallAgeTransactionLogStore().LoadAsync(root, CancellationToken.None)).Transactions);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyingStableIdOperationKeepsSurvivingSelectionForContinuedManipulation()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-transform-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new RekallAgeMeshAssetStore().SaveAsync(root, Quad(), CancellationToken.None);
            var session = new RekallAgeStudioModelingSession();
            await session.OpenAsync(root, "quad", CancellationToken.None);
            session.SetDomain(RekallAgeGeometryDomain.Point);
            session.Select(1);
            session.Select(2, extend: true);

            await session.ApplyAsync("transform", new JsonObject { ["x"] = 2.0, ["y"] = 0.0, ["z"] = 0.0 }, "studio-gizmo", CancellationToken.None);

            Assert.Equal([1UL, 2UL], session.SelectedElementIds);
            Assert.Equal(2UL, session.ActiveElementId);
            Assert.Equal(2, session.Mesh!.Topology.Positions[0].X);
            Assert.Equal(3, session.Mesh.Topology.Positions[1].X);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpeningAProjectWithARegisteredMeshOperationPluginAddsItToAvailableOperations()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-modeling-plugin-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new RekallAgeCommandContext("studio-tests", RekallAgeTransaction.Begin("scaffold"), CancellationToken.None);
            var scaffold = await new ScaffoldModuleCommand().ExecuteAsync(
                new ScaffoldModuleRequest(root, "TestStudioMeshPlugin", "TestStudioMeshPlugin", "TestStudioMeshPlugin", "PluginState"),
                context);
            Assert.True(scaffold.Ok, scaffold.Summary);
            var write = await new WriteModuleSourceCommand().ExecuteAsync(
                new WriteModuleSourceRequest(root, "TestStudioMeshPlugin", "TestStudioMeshPluginModule.cs", TestModuleSource),
                context);
            Assert.True(write.Ok, write.Summary);
            var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
            Assert.True(build.Ok, build.Summary);

            var store = new RekallAgeMeshAssetStore();
            await store.SaveAsync(root, Quad(), CancellationToken.None);
            var session = new RekallAgeStudioModelingSession();

            await session.OpenAsync(root, "quad", CancellationToken.None);

            Assert.Contains(session.AvailableOperations, item => item.OperationId == "teststudiomeshplugin.fake_operation");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // The fake plugin's Descriptor.Domain must be RekallAgeGeometryDomain.Face to match Quad()'s
    // default session Domain (RekallAgeStudioModelingSession.Domain defaults to Face), since
    // AvailableOperations filters by the session's current Domain.
    private const string TestModuleSource = """
        using Rekall.Age.Modeling;
        using Rekall.Age.Modeling.Contracts;
        using Rekall.Age.Modules;

        namespace Game.Modules.TestStudioMeshPlugin;

        [RekallAgeModule("TestStudioMeshPlugin", "Test Studio Mesh Plugin")]
        public sealed class TestStudioMeshPluginModule : RekallAgeModule
        {
            public override void Configure(RekallAgeModuleBuilder builder)
            {
                builder.RegisterMeshOperation<FakeOperation>();
            }
        }

        public sealed class FakeOperation : IRekallAgeMeshOperationPlugin
        {
            public string OperationId => "teststudiomeshplugin.fake_operation";
            public RekallAgeMeshOperationDescriptor Descriptor => new(
                OperationId, "A fake test operation.", RekallAgeGeometryDomain.Face,
                RekallAgeMeshChangeKind.None, []);
            public RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request) =>
                throw new System.NotSupportedException();
        }
        """;

    private static RekallAgeMeshAsset Quad() => RekallAgeMeshAsset.Create("quad", "Quad",
        new(
            PointIds: [1, 2, 3, 4], Positions: [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
            EdgeIds: [11, 12, 13, 14], EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
            FaceIds: [21], FaceOffsets: [0, 4], CornerIds: [31, 32, 33, 34],
            CornerPointIndices: [0, 1, 2, 3], CornerEdgeIndices: [0, 1, 2, 3]));
}
