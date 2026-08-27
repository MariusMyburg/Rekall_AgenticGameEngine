using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modules;

public sealed class RekallAgeModuleBuilder
{
    private readonly List<Type> _componentTypes = [];
    private readonly List<Type> _runtimeSystemTypes = [];
    private readonly List<Type> _meshOperationTypes = [];
    private readonly List<Type> _fractureAlgorithmTypes = [];

    public IReadOnlyList<Type> ComponentTypes => _componentTypes;

    public IReadOnlyList<Type> RuntimeSystemTypes => _runtimeSystemTypes;

    public IReadOnlyList<Type> MeshOperationTypes => _meshOperationTypes;

    public IReadOnlyList<Type> FractureAlgorithmTypes => _fractureAlgorithmTypes;

    public void RegisterComponent<TComponent>()
        where TComponent : RekallAgeComponent
    {
        var type = typeof(TComponent);
        if (!_componentTypes.Contains(type))
        {
            _componentTypes.Add(type);
        }
    }

    public void RegisterRuntimeSystem<TSystem>()
        where TSystem : IRekallAgeRuntimeModuleSystem
    {
        var type = typeof(TSystem);
        if (!_runtimeSystemTypes.Contains(type))
        {
            _runtimeSystemTypes.Add(type);
        }
    }

    public void RegisterMeshOperation<TOperation>()
        where TOperation : IRekallAgeMeshOperationPlugin
    {
        var type = typeof(TOperation);
        if (!_meshOperationTypes.Contains(type))
        {
            _meshOperationTypes.Add(type);
        }
    }

    public void RegisterFractureAlgorithm<TAlgorithm>()
        where TAlgorithm : IRekallAgeFractureAlgorithmPlugin
    {
        var type = typeof(TAlgorithm);
        if (!_fractureAlgorithmTypes.Contains(type))
        {
            _fractureAlgorithmTypes.Add(type);
        }
    }
}
