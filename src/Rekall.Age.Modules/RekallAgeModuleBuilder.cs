namespace Rekall.Age.Modules;

public sealed class RekallAgeModuleBuilder
{
    private readonly List<Type> _componentTypes = [];

    public IReadOnlyList<Type> ComponentTypes => _componentTypes;

    public void RegisterComponent<TComponent>()
        where TComponent : RekallAgeComponent
    {
        var type = typeof(TComponent);
        if (!_componentTypes.Contains(type))
        {
            _componentTypes.Add(type);
        }
    }
}
