using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

/// <summary>
/// Allocation-free presence checks over a world's entities.
///
/// Runtime systems run every fixed step regardless of whether the scene contains
/// anything they act on, and the usual shape - <c>world.Entities.Select(...).ToArray()</c>
/// - allocates a fresh entity array and a new world record every time. On a 5,000-entity
/// scene that is ~350,000 entity visits per frame across all systems, most of them for
/// components the scene does not contain.
///
/// A system can guard its pass with one of these checks and return the world untouched.
/// The scan is O(entities x components) but allocates nothing and runs no per-entity
/// logic, so it is orders of magnitude cheaper than the pass it replaces.
///
/// Deliberately hand-rolled loops rather than LINQ: these run on the hot path for every
/// system, every step, and <c>Any(...)</c> with a closure allocates.
/// </summary>
public static class RekallAgeRuntimeComponentPresence
{
    /// <summary>True when any entity carries a component of the given type.</summary>
    public static bool AnyEntityHas(RekallAgeRuntimeWorld world, string componentType)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var entity in world.Entities)
        {
            foreach (var component in entity.Components)
            {
                if (component.Type.Equals(componentType, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>True when any entity carries a component of any of the given types.</summary>
    public static bool AnyEntityHasAny(RekallAgeRuntimeWorld world, params string[] componentTypes)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(componentTypes);
        foreach (var entity in world.Entities)
        {
            foreach (var component in entity.Components)
            {
                foreach (var candidate in componentTypes)
                {
                    if (component.Type.Equals(candidate, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// True when any entity carries a component whose type contains the given fragment.
    ///
    /// Matching loosely is deliberate for component *families* such as colliders: an
    /// unexpected match only means a system declines to skip its pass, which is merely
    /// slower, while a missed match would wrongly skip real work. Erring toward false
    /// positives keeps the guard safe as new component types are added.
    /// </summary>
    public static bool AnyEntityHasContaining(RekallAgeRuntimeWorld world, string componentTypeFragment)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var entity in world.Entities)
        {
            foreach (var component in entity.Components)
            {
                if (component.Type.Contains(componentTypeFragment, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// True when any entity carries a collider of any dimension or shape. Shared by the
    /// physics, collision-event, and trigger-event systems, which all derive their bodies
    /// from collider components and have nothing to do without one.
    /// </summary>
    public static bool AnyEntityHasCollider(RekallAgeRuntimeWorld world) =>
        AnyEntityHasContaining(world, "Collider");

    /// <summary>
    /// True when any entity carries a component whose type starts with the given prefix.
    /// Useful for families such as "Rekall.Orbit" / "Rekall.OrbitPath".
    /// </summary>
    public static bool AnyEntityHasPrefixed(RekallAgeRuntimeWorld world, string componentTypePrefix)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var entity in world.Entities)
        {
            foreach (var component in entity.Components)
            {
                if (component.Type.StartsWith(componentTypePrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
