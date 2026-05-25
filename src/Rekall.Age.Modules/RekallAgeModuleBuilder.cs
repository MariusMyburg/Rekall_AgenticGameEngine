namespace Rekall.Age.Modules;

public sealed class RekallAgeModuleBuilder
{
    private readonly List<Type> _componentTypes = [];
    private readonly List<Type> _runtimeSystemTypes = [];

    public IReadOnlyList<Type> ComponentTypes => _componentTypes;

    public IReadOnlyList<Type> RuntimeSystemTypes => _runtimeSystemTypes;

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
}
