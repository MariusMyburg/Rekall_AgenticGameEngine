using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Modules;

namespace Rekall.Age.Tests.Modules;

public sealed class MeshPluginRegistrationTests
{
    [Fact]
    public void RegisterMeshOperationAddsTheTypeOnceEvenIfRegisteredTwice()
    {
        var builder = new RekallAgeModuleBuilder();

        builder.RegisterMeshOperation<FakeMeshOperation>();
        builder.RegisterMeshOperation<FakeMeshOperation>();

        Assert.Single(builder.MeshOperationTypes, type => type == typeof(FakeMeshOperation));
    }

    [Fact]
    public void RegisterFractureAlgorithmAddsTheTypeOnceEvenIfRegisteredTwice()
    {
        var builder = new RekallAgeModuleBuilder();

        builder.RegisterFractureAlgorithm<FakeFractureAlgorithm>();
        builder.RegisterFractureAlgorithm<FakeFractureAlgorithm>();

        Assert.Single(builder.FractureAlgorithmTypes, type => type == typeof(FakeFractureAlgorithm));
    }

    private sealed class FakeMeshOperation : IRekallAgeMeshOperationPlugin
    {
        public string OperationId => "test.fake_operation";
        public RekallAgeMeshOperationDescriptor Descriptor => new(
            OperationId, "A fake test operation.", RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.None, []);
        public RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request) =>
            throw new NotSupportedException();
    }

    private sealed class FakeFractureAlgorithm : IRekallAgeFractureAlgorithmPlugin
    {
        public string AlgorithmId => "test.fake_algorithm";
        public IReadOnlyList<RekallAgeMeshAsset> Fracture(RekallAgeMeshAsset source, int chunkCount, long seed) =>
            throw new NotSupportedException();
    }
}
