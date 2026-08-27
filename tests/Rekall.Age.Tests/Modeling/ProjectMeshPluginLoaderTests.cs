using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modeling;

public sealed class ProjectMeshPluginLoaderTests
{
    [Fact]
    public async Task LoadDiscoversARegisteredMeshOperationAndFractureAlgorithmFromABuiltProjectModule()
    {
        var root = await BuildScratchModuleProjectAsync("TestMeshPlugins", TestModuleSource);

        var plugins = new RekallAgeProjectMeshPluginLoader().Load(root);

        var operation = Assert.Single(plugins.Operations);
        Assert.Equal("testmeshplugins.fake_operation", operation.OperationId);
        var algorithm = Assert.Single(plugins.FractureAlgorithms);
        Assert.Equal("testmeshplugins.fake_algorithm", algorithm.AlgorithmId);
    }

    [Fact]
    public async Task LoadRejectsAPluginWithABareUndottedId()
    {
        var root = await BuildScratchModuleProjectAsync("TestBadMeshPlugin", BadIdModuleSource);

        var error = Assert.Throws<InvalidOperationException>(() => new RekallAgeProjectMeshPluginLoader().Load(root));
        Assert.Contains("bare_operation", error.Message, StringComparison.Ordinal);
    }

    private static async Task<string> BuildScratchModuleProjectAsync(string moduleId, string moduleSource)
    {
        var root = TestPaths.CreateTempDirectory();
        var context = Context("scaffold");
        var scaffold = await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(root, moduleId, moduleId, moduleId, "PluginState"),
            context);
        Assert.True(scaffold.Ok, scaffold.Summary);
        var write = await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, moduleId, $"{moduleId}Module.cs", moduleSource),
            context);
        Assert.True(write.Ok, write.Summary);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(build.Ok, build.Summary);
        return root;
    }

    private static RekallAgeCommandContext Context(string name) =>
        new("mesh-plugin-loader-tests", RekallAgeTransaction.Begin(name), CancellationToken.None);

    private const string TestModuleSource = """
        using Rekall.Age.Modeling;
        using Rekall.Age.Modeling.Contracts;
        using Rekall.Age.Modules;

        namespace Game.Modules.TestMeshPlugins;

        [RekallAgeModule("TestMeshPlugins", "Test Mesh Plugins")]
        public sealed class TestMeshPluginsModule : RekallAgeModule
        {
            public override void Configure(RekallAgeModuleBuilder builder)
            {
                builder.RegisterMeshOperation<FakeOperation>();
                builder.RegisterFractureAlgorithm<FakeAlgorithm>();
            }
        }

        public sealed class FakeOperation : IRekallAgeMeshOperationPlugin
        {
            public string OperationId => "testmeshplugins.fake_operation";
            public RekallAgeMeshOperationDescriptor Descriptor => new(
                OperationId, "A fake test operation.", RekallAgeGeometryDomain.Face,
                RekallAgeMeshChangeKind.None, []);
            public RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request) =>
                throw new System.NotSupportedException();
        }

        public sealed class FakeAlgorithm : IRekallAgeFractureAlgorithmPlugin
        {
            public string AlgorithmId => "testmeshplugins.fake_algorithm";
            public System.Collections.Generic.IReadOnlyList<RekallAgeMeshAsset> Fracture(RekallAgeMeshAsset source, int chunkCount, long seed) =>
                throw new System.NotSupportedException();
        }
        """;

    private const string BadIdModuleSource = """
        using Rekall.Age.Modeling;
        using Rekall.Age.Modeling.Contracts;
        using Rekall.Age.Modules;

        namespace Game.Modules.TestBadMeshPlugin;

        [RekallAgeModule("TestBadMeshPlugin", "Test Bad Mesh Plugin")]
        public sealed class TestBadMeshPluginModule : RekallAgeModule
        {
            public override void Configure(RekallAgeModuleBuilder builder)
            {
                builder.RegisterMeshOperation<BareIdOperation>();
            }
        }

        public sealed class BareIdOperation : IRekallAgeMeshOperationPlugin
        {
            public string OperationId => "bare_operation";
            public RekallAgeMeshOperationDescriptor Descriptor => new(
                OperationId, "A fake test operation with a bare id.", RekallAgeGeometryDomain.Face,
                RekallAgeMeshChangeKind.None, []);
            public RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request) =>
                throw new System.NotSupportedException();
        }
        """;
}
