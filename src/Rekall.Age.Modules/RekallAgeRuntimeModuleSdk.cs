using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Modules;

public sealed record RekallAgeRuntimeRaycastHit(
    RekallAgeRuntimeEntity Entity,
    double Distance,
    RekallAgeRuntimeVector3 Point,
    string ColliderType);

public static class RekallAgeRuntimeModuleSdk
{
    public static RekallAgeRuntimeComponent? FindComponent(
        this RekallAgeRuntimeEntity entity,
        string componentType)
    {
        return entity.Components.FirstOrDefault(component =>
            component.Type.Equals(componentType, StringComparison.Ordinal));
    }

    public static bool HasTag(this RekallAgeRuntimeEntity entity, string tag)
    {
        return entity.Tags.Any(item => item.Equals(tag, StringComparison.OrdinalIgnoreCase));
    }

    public static RekallAgeRuntimeEntity WithTag(this RekallAgeRuntimeEntity entity, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return entity;
        }

        return entity with
        {
            Tags = NormalizeTags(entity.Tags.Append(tag))
        };
    }

    public static RekallAgeRuntimeEntity WithoutTag(this RekallAgeRuntimeEntity entity, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return entity;
        }

        var trimmed = tag.Trim();
        return entity with
        {
            Tags = entity.Tags
                .Where(item => !item.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                .ToArray()
        };
    }

    public static RekallAgeRuntimeEntity WithVisible(
        this RekallAgeRuntimeEntity entity,
        bool visible)
    {
        return entity with { Visible = visible };
    }

    public static RekallAgeRuntimeEntity? FindEntity(
        this RekallAgeRuntimeWorld world,
        string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        var id = entityId.Trim();
        return world.Entities.FirstOrDefault(entity =>
            entity.Id.Equals(id, StringComparison.Ordinal));
    }

    public static IReadOnlyList<RekallAgeRuntimeEntity> EntitiesNamed(
        this RekallAgeRuntimeWorld world,
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Array.Empty<RekallAgeRuntimeEntity>();
        }

        var trimmed = name.Trim();
        return StableEntities(world)
            .Where(entity => entity.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static IReadOnlyList<RekallAgeRuntimeEntity> EntitiesWithTag(
        this RekallAgeRuntimeWorld world,
        string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Array.Empty<RekallAgeRuntimeEntity>();
        }

        var trimmed = tag.Trim();
        return StableEntities(world)
            .Where(entity => entity.HasTag(trimmed))
            .ToArray();
    }

    public static IReadOnlyList<RekallAgeRuntimeEntity> EntitiesWithComponent(
        this RekallAgeRuntimeWorld world,
        string componentType)
    {
        if (string.IsNullOrWhiteSpace(componentType))
        {
            return Array.Empty<RekallAgeRuntimeEntity>();
        }

        var trimmed = componentType.Trim();
        return StableEntities(world)
            .Where(entity => entity.FindComponent(trimmed) is not null)
            .ToArray();
    }

    public static IReadOnlyList<RekallAgeRuntimeEntity> EntitiesWithTagAndComponent(
        this RekallAgeRuntimeWorld world,
        string tag,
        string componentType)
    {
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(componentType))
        {
            return Array.Empty<RekallAgeRuntimeEntity>();
        }

        var trimmedTag = tag.Trim();
        var trimmedComponentType = componentType.Trim();
        return StableEntities(world)
            .Where(entity => entity.HasTag(trimmedTag) && entity.FindComponent(trimmedComponentType) is not null)
            .ToArray();
    }

    public static RekallAgeRuntimeWorld ReplaceEntity(
        this RekallAgeRuntimeWorld world,
        RekallAgeRuntimeEntity replacement)
    {
        var found = false;
        var entities = world.Entities.Select(entity =>
        {
            if (!entity.Id.Equals(replacement.Id, StringComparison.Ordinal))
            {
                return entity;
            }

            found = true;
            return replacement;
        }).ToArray();

        return found ? world with { Entities = entities } : world;
    }

    public static RekallAgeRuntimeWorld UpdateEntity(
        this RekallAgeRuntimeWorld world,
        string entityId,
        Func<RekallAgeRuntimeEntity, RekallAgeRuntimeEntity> update)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return world;
        }

        var id = entityId.Trim();
        return world.UpdateEntities(entity => entity.Id.Equals(id, StringComparison.Ordinal), update);
    }

    public static RekallAgeRuntimeWorld UpdateEntitiesWithTag(
        this RekallAgeRuntimeWorld world,
        string tag,
        Func<RekallAgeRuntimeEntity, RekallAgeRuntimeEntity> update)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return world;
        }

        var trimmed = tag.Trim();
        return world.UpdateEntities(entity => entity.HasTag(trimmed), update);
    }

    public static RekallAgeRuntimeWorld UpdateEntitiesWithComponent(
        this RekallAgeRuntimeWorld world,
        string componentType,
        Func<RekallAgeRuntimeEntity, RekallAgeRuntimeEntity> update)
    {
        if (string.IsNullOrWhiteSpace(componentType))
        {
            return world;
        }

        var trimmed = componentType.Trim();
        return world.UpdateEntities(entity => entity.FindComponent(trimmed) is not null, update);
    }

    public static RekallAgeRuntimeWorld UpdateEntitiesWithTagAndComponent(
        this RekallAgeRuntimeWorld world,
        string tag,
        string componentType,
        Func<RekallAgeRuntimeEntity, RekallAgeRuntimeEntity> update)
    {
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(componentType))
        {
            return world;
        }

        var trimmedTag = tag.Trim();
        var trimmedComponentType = componentType.Trim();
        return world.UpdateEntities(
            entity => entity.HasTag(trimmedTag) && entity.FindComponent(trimmedComponentType) is not null,
            update);
    }

    public static IReadOnlyList<RekallAgeRuntimeNetworkSession> NetworkSessions(
        this RekallAgeRuntimeWorld world)
    {
        return world.Subsystems.Multiplayer.Sessions
            .OrderBy(session => session.EntityName, StringComparer.Ordinal)
            .ThenBy(session => session.EntityId, StringComparer.Ordinal)
            .ToArray();
    }

    public static RekallAgeRuntimeNetworkSession? PrimaryNetworkSession(
        this RekallAgeRuntimeWorld world)
    {
        return world.NetworkSessions().FirstOrDefault();
    }

    public static IReadOnlyList<RekallAgeRuntimeNetworkEntity> NetworkEntities(
        this RekallAgeRuntimeWorld world)
    {
        return StableNetworkEntities(world.Subsystems.Multiplayer.Entities)
            .ToArray();
    }

    public static RekallAgeRuntimeNetworkEntity? NetworkEntityForEntity(
        this RekallAgeRuntimeWorld world,
        string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        var trimmed = entityId.Trim();
        return world.NetworkEntities()
            .FirstOrDefault(entity => entity.EntityId.Equals(trimmed, StringComparison.Ordinal));
    }

    public static RekallAgeRuntimeNetworkEntity? NetworkEntityByNetworkId(
        this RekallAgeRuntimeWorld world,
        string networkId)
    {
        if (string.IsNullOrWhiteSpace(networkId))
        {
            return null;
        }

        var trimmed = networkId.Trim();
        return world.NetworkEntities()
            .FirstOrDefault(entity => entity.NetworkId.Equals(trimmed, StringComparison.Ordinal));
    }

    public static IReadOnlyList<RekallAgeRuntimeNetworkEntity> NetworkEntitiesOwnedBy(
        this RekallAgeRuntimeWorld world,
        string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Array.Empty<RekallAgeRuntimeNetworkEntity>();
        }

        var trimmed = clientId.Trim();
        return world.NetworkEntities()
            .Where(entity => entity.OwnerClientId?.Equals(trimmed, StringComparison.Ordinal) == true)
            .ToArray();
    }

    public static IReadOnlyList<RekallAgeRuntimeEntity> RuntimeEntitiesOwnedBy(
        this RekallAgeRuntimeWorld world,
        string clientId)
    {
        var ownedIds = world.NetworkEntitiesOwnedBy(clientId)
            .Select(entity => entity.EntityId)
            .ToHashSet(StringComparer.Ordinal);
        return StableEntities(world)
            .Where(entity => ownedIds.Contains(entity.Id))
            .ToArray();
    }

    public static IReadOnlyList<RekallAgeRuntimeEntity> ReplicatedRuntimeEntities(
        this RekallAgeRuntimeWorld world)
    {
        var replicatedIds = world.NetworkEntities()
            .Where(entity => entity.IsReplicated())
            .Select(entity => entity.EntityId)
            .ToHashSet(StringComparer.Ordinal);
        return StableEntities(world)
            .Where(entity => replicatedIds.Contains(entity.Id))
            .ToArray();
    }

    public static bool IsNetworkOwner(
        this RekallAgeRuntimeWorld world,
        RekallAgeRuntimeEntity entity,
        string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        return world.NetworkEntityForEntity(entity.Id)?.OwnerClientId?.Equals(
            clientId.Trim(),
            StringComparison.Ordinal) == true;
    }

    public static bool IsReplicated(this RekallAgeRuntimeNetworkEntity entity)
    {
        return entity.ReplicatePosition || entity.ReplicateRotation || entity.ReplicateScale;
    }

    public static RekallAgeRuntimeEntity WithPosition3D(
        this RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeVector3 position)
    {
        return entity with
        {
            Transform = entity.Transform with { Position3D = position }
        };
    }

    public static RekallAgeRuntimeEntity WithRotation3D(
        this RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeVector3 rotation)
    {
        return entity with
        {
            Transform = entity.Transform with { Rotation3D = rotation }
        };
    }

    public static RekallAgeRuntimeEntity WithScale3D(
        this RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeVector3 scale)
    {
        return entity with
        {
            Transform = entity.Transform with { Scale3D = scale }
        };
    }

    public static RekallAgeRuntimeEntity UpsertComponent(
        this RekallAgeRuntimeEntity entity,
        string componentType,
        JsonObject properties)
    {
        var replaced = false;
        var components = entity.Components.Select(component =>
        {
            if (!component.Type.Equals(componentType, StringComparison.Ordinal))
            {
                return component;
            }

            replaced = true;
            return new RekallAgeRuntimeComponent(componentType, properties.DeepClone().AsObject());
        }).ToList();

        if (!replaced)
        {
            components.Add(new RekallAgeRuntimeComponent(componentType, properties.DeepClone().AsObject()));
        }

        return entity with
        {
            Components = components
                .OrderBy(component => component.Type, StringComparer.Ordinal)
                .ToArray()
        };
    }

    public static RekallAgeRuntimeEntity UpdateComponent(
        this RekallAgeRuntimeEntity entity,
        string componentType,
        Func<JsonObject, JsonObject> update)
    {
        return entity.UpsertComponent(
            componentType,
            update((entity.FindComponent(componentType)?.Properties.DeepClone() as JsonObject) ?? new JsonObject()));
    }

    public static IReadOnlyList<RekallAgeRuntimeInputAction> InputActions(
        this RekallAgeRuntimeWorld world,
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Array.Empty<RekallAgeRuntimeInputAction>();
        }

        return world.Subsystems.Input.Actions
            .Where(action => action.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static double InputActionValue(
        this RekallAgeRuntimeWorld world,
        string name,
        double fallback = 0)
    {
        var actions = world.InputActions(name);
        return actions.Count == 0
            ? fallback
            : actions.Sum(action => action.Value);
    }

    public static bool IsInputActionDown(this RekallAgeRuntimeWorld world, string name)
    {
        return world.InputActions(name).Any(action => action.IsDown);
    }

    public static bool WasInputActionPressed(this RekallAgeRuntimeWorld world, string name)
    {
        return world.InputActions(name).Any(action => action.WasPressed);
    }

    public static bool WasInputActionReleased(this RekallAgeRuntimeWorld world, string name)
    {
        return world.InputActions(name).Any(action => action.WasReleased);
    }

    public static IReadOnlyList<RekallAgeRuntimeEvent> EventsOfType(
        this RekallAgeRuntimeWorld world,
        string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return Array.Empty<RekallAgeRuntimeEvent>();
        }

        return world.Subsystems.Events.Events
            .Where(runtimeEvent => runtimeEvent.Type.Equals(type.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static IReadOnlyList<RekallAgeRuntimeEvent> EventsFor(
        this RekallAgeRuntimeWorld world,
        string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return Array.Empty<RekallAgeRuntimeEvent>();
        }

        return world.Subsystems.Events.Events
            .Where(runtimeEvent => runtimeEvent.EntityId.Equals(entityId.Trim(), StringComparison.Ordinal))
            .ToArray();
    }

    public static bool WasEventRaised(
        this RekallAgeRuntimeWorld world,
        string entityId,
        string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        return world.EventsFor(entityId)
            .Any(runtimeEvent => runtimeEvent.Type.Equals(type.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<RekallAgeRuntimeObservation> ObservationsWithCode(
        this RekallAgeRuntimeWorld world,
        string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Array.Empty<RekallAgeRuntimeObservation>();
        }

        var trimmed = code.Trim();
        return world.Observations
            .Where(observation => observation.Code.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static IReadOnlyList<RekallAgeRuntimeObservation> ObservationsWithSeverity(
        this RekallAgeRuntimeWorld world,
        string severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
        {
            return Array.Empty<RekallAgeRuntimeObservation>();
        }

        var trimmed = severity.Trim();
        return world.Observations
            .Where(observation => observation.Severity.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static IReadOnlyList<RekallAgeRuntimeObservation> ObservationsFor(
        this RekallAgeRuntimeWorld world,
        string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return Array.Empty<RekallAgeRuntimeObservation>();
        }

        var trimmed = entityId.Trim();
        return world.Observations
            .Where(observation => observation.TargetId.Equals(trimmed, StringComparison.Ordinal))
            .ToArray();
    }

    public static IReadOnlyList<RekallAgeRuntimeObservation> ObservationsForScene(
        this RekallAgeRuntimeWorld world)
    {
        return world.ObservationsFor(world.SceneId);
    }

    public static bool HasBlockingObservations(this RekallAgeRuntimeWorld world)
    {
        return world.ObservationsWithSeverity("blocking").Count > 0;
    }

    public static RekallAgeRuntimeWorld EmitObservation(
        this RekallAgeRuntimeWorld world,
        RekallAgeRuntimeEntity entity,
        string code,
        string severity,
        string subsystem,
        string system,
        string message,
        IReadOnlyList<string>? suggestedCommands = null)
    {
        return world.WithObservation(
            code,
            severity,
            subsystem,
            entity.Id,
            entity.Name,
            system,
            message,
            suggestedCommands);
    }

    public static RekallAgeRuntimeWorld EmitSceneObservation(
        this RekallAgeRuntimeWorld world,
        string code,
        string severity,
        string subsystem,
        string system,
        string message,
        IReadOnlyList<string>? suggestedCommands = null)
    {
        return world.WithObservation(
            code,
            severity,
            subsystem,
            world.SceneId,
            world.SceneName,
            system,
            message,
            suggestedCommands);
    }

    public static RekallAgeRuntimeWorld EmitEvent(
        this RekallAgeRuntimeWorld world,
        RekallAgeRuntimeEntity entity,
        string type,
        string source,
        string? handler = null,
        JsonObject? payload = null)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(source))
        {
            return world;
        }

        return world.WithEvents(
        [
            new RekallAgeRuntimeEvent(
                world.FrameIndex,
                type.Trim(),
                entity.Id,
                entity.Name,
                source.Trim(),
                string.IsNullOrWhiteSpace(handler) ? null : handler.Trim(),
                ClonePayload(payload))
        ]);
    }

    public static RekallAgeRuntimeWorld EmitBoundEvents(
        this RekallAgeRuntimeWorld world,
        RekallAgeRuntimeEntity entity,
        string type,
        string source,
        JsonObject? payload = null)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(source))
        {
            return world;
        }

        var events = EventBindingsFor(entity, type.Trim())
            .Select(binding => new RekallAgeRuntimeEvent(
                world.FrameIndex,
                type.Trim(),
                entity.Id,
                entity.Name,
                source.Trim(),
                binding.Handler,
                ClonePayload(payload)))
            .ToArray();
        return world.WithEvents(events);
    }

    public static IReadOnlyList<RekallAgeRuntimeRaycastHit> Raycast3D(
        this RekallAgeRuntimeWorld world,
        RekallAgeRuntimeVector3 origin,
        RekallAgeRuntimeVector3 direction,
        double range,
        string? tag = null,
        string? componentType = null)
    {
        if (range <= 0)
        {
            return Array.Empty<RekallAgeRuntimeRaycastHit>();
        }

        var normalized = Normalize(direction);
        if (LengthSquared(normalized) <= 0.000001)
        {
            return Array.Empty<RekallAgeRuntimeRaycastHit>();
        }

        var hits = new List<RekallAgeRuntimeRaycastHit>();
        foreach (var entity in world.Entities)
        {
            if (!entity.Visible)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(tag) && !entity.HasTag(tag))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(componentType) && entity.FindComponent(componentType) is null)
            {
                continue;
            }

            foreach (var collider in entity.Components.Where(Is3DCollider))
            {
                if (TryIntersectCollider(origin, normalized, range, entity, collider, out var distance, out var point))
                {
                    hits.Add(new RekallAgeRuntimeRaycastHit(entity, distance, point, collider.Type));
                    break;
                }
            }
        }

        return hits
            .OrderBy(hit => hit.Distance)
            .ThenBy(hit => hit.Entity.Name, StringComparer.Ordinal)
            .ThenBy(hit => hit.Entity.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static double ReadNumber(this JsonObject properties, string name, double fallback)
    {
        if (!properties.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        return value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
    }

    public static bool ReadBoolean(this JsonObject properties, string name, bool fallback)
    {
        if (!properties.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var boolValue))
        {
            return boolValue;
        }

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    public static string? ReadString(this JsonObject properties, string name, string? fallback = null)
    {
        if (!properties.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        return value.TryGetValue<string>(out var text) ? text : fallback;
    }

    private static RekallAgeRuntimeWorld WithEvents(
        this RekallAgeRuntimeWorld world,
        IReadOnlyList<RekallAgeRuntimeEvent> events)
    {
        if (events.Count == 0)
        {
            return world;
        }

        return world with
        {
            Subsystems = world.Subsystems with
            {
                Events = new RekallAgeRuntimeEventView(
                    world.Subsystems.Events.Events
                        .Concat(events)
                        .OrderBy(runtimeEvent => runtimeEvent.Frame)
                        .ThenBy(runtimeEvent => runtimeEvent.EntityName, StringComparer.Ordinal)
                        .ThenBy(runtimeEvent => runtimeEvent.Type, StringComparer.Ordinal)
                        .ThenBy(runtimeEvent => runtimeEvent.Handler, StringComparer.Ordinal)
                        .ToArray())
            }
        };
    }

    private static RekallAgeRuntimeWorld WithObservation(
        this RekallAgeRuntimeWorld world,
        string code,
        string severity,
        string subsystem,
        string targetId,
        string targetName,
        string system,
        string message,
        IReadOnlyList<string>? suggestedCommands)
    {
        if (string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(severity)
            || string.IsNullOrWhiteSpace(subsystem)
            || string.IsNullOrWhiteSpace(system)
            || string.IsNullOrWhiteSpace(message))
        {
            return world;
        }

        var observation = new RekallAgeRuntimeObservation(
            world.FrameIndex,
            code.Trim(),
            severity.Trim().ToLowerInvariant(),
            subsystem.Trim(),
            targetId,
            targetName,
            system.Trim(),
            message.Trim(),
            NormalizeSuggestedCommands(suggestedCommands));

        return world with
        {
            Observations = world.Observations
                .Concat([observation])
                .ToArray()
        };
    }

    private static IEnumerable<RekallAgeRuntimeEntity> StableEntities(RekallAgeRuntimeWorld world)
    {
        return world.Entities
            .OrderBy(entity => entity.Name, StringComparer.Ordinal)
            .ThenBy(entity => entity.Id, StringComparer.Ordinal);
    }

    private static IEnumerable<RekallAgeRuntimeNetworkEntity> StableNetworkEntities(
        IEnumerable<RekallAgeRuntimeNetworkEntity> entities)
    {
        return entities
            .OrderByDescending(entity => entity.Priority)
            .ThenBy(entity => entity.NetworkId, StringComparer.Ordinal)
            .ThenBy(entity => entity.EntityId, StringComparer.Ordinal);
    }

    private static RekallAgeRuntimeWorld UpdateEntities(
        this RekallAgeRuntimeWorld world,
        Func<RekallAgeRuntimeEntity, bool> predicate,
        Func<RekallAgeRuntimeEntity, RekallAgeRuntimeEntity> update)
    {
        var changed = false;
        var entities = world.Entities.Select(entity =>
        {
            if (!predicate(entity))
            {
                return entity;
            }

            changed = true;
            return update(entity);
        }).ToArray();

        return changed ? world with { Entities = entities } : world;
    }

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags)
    {
        return tags
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeSuggestedCommands(IReadOnlyList<string>? suggestedCommands)
    {
        return suggestedCommands is null
            ? Array.Empty<string>()
            : suggestedCommands
                .Select(command => command.Trim())
                .Where(command => command.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(command => command, StringComparer.Ordinal)
                .ToArray();
    }

    private static IReadOnlyList<EventBinding> EventBindingsFor(
        RekallAgeRuntimeEntity entity,
        string type)
    {
        var bindings = new List<EventBinding>();
        foreach (var component in entity.Components.Where(component =>
                     component.Type.Equals("Rekall.EventBindings", StringComparison.Ordinal)))
        {
            if (!ReadBooleanCaseInsensitive(component.Properties, "active", true)
                || !TryGetPropertyValueCaseInsensitive(component.Properties, "events", out var eventNodes)
                || eventNodes is not JsonArray eventArray)
            {
                continue;
            }

            foreach (var eventNode in eventArray.OfType<JsonObject>())
            {
                if (!ReadBooleanCaseInsensitive(eventNode, "active", true))
                {
                    continue;
                }

                var eventType = ReadStringCaseInsensitive(eventNode, "event")
                    ?? ReadStringCaseInsensitive(eventNode, "type");
                if (eventType?.Equals(type, StringComparison.OrdinalIgnoreCase) == true)
                {
                    bindings.Add(new EventBinding(Clean(ReadStringCaseInsensitive(eventNode, "handler"))));
                }
            }
        }

        return bindings;
    }

    private static JsonObject ClonePayload(JsonObject? payload)
    {
        return payload is null
            ? new JsonObject()
            : payload.DeepClone().AsObject();
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ReadStringCaseInsensitive(JsonObject properties, string name)
    {
        return TryGetPropertyValueCaseInsensitive(properties, name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static bool ReadBooleanCaseInsensitive(JsonObject properties, string name, bool fallback)
    {
        if (!TryGetPropertyValueCaseInsensitive(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryGetPropertyValueCaseInsensitive(
        JsonObject properties,
        string name,
        out JsonNode? node)
    {
        if (properties.TryGetPropertyValue(name, out node))
        {
            return true;
        }

        var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
        if (properties.TryGetPropertyValue(pascalName, out node))
        {
            return true;
        }

        var match = properties.FirstOrDefault(property =>
            property.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        node = match.Value;
        return !string.IsNullOrEmpty(match.Key);
    }

    private static bool Is3DCollider(RekallAgeRuntimeComponent component)
    {
        return component.Type is
            "Rekall.BoxCollider3D" or
            "Rekall.SphereCollider3D" or
            "Rekall.CapsuleCollider3D" or
            "Rekall.MeshCollider";
    }

    private static bool TryIntersectCollider(
        RekallAgeRuntimeVector3 origin,
        RekallAgeRuntimeVector3 direction,
        double range,
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent collider,
        out double distance,
        out RekallAgeRuntimeVector3 point)
    {
        return collider.Type switch
        {
            "Rekall.SphereCollider3D" => TryIntersectSphere(
                origin,
                direction,
                range,
                entity.Transform.Position3D,
                Math.Max(0.0001, collider.Properties.ReadNumber("radius", collider.Properties.ReadNumber("Radius", 0.5))),
                out distance,
                out point),
            "Rekall.CapsuleCollider3D" => TryIntersectSphere(
                origin,
                direction,
                range,
                entity.Transform.Position3D,
                Math.Max(0.0001, collider.Properties.ReadNumber("radius", collider.Properties.ReadNumber("Radius", 0.5))),
                out distance,
                out point),
            "Rekall.BoxCollider3D" => TryIntersectSphere(
                origin,
                direction,
                range,
                entity.Transform.Position3D,
                EstimateBoxBoundingRadius(entity, collider),
                out distance,
                out point),
            "Rekall.MeshCollider" => TryIntersectSphere(
                origin,
                direction,
                range,
                entity.Transform.Position3D,
                1,
                out distance,
                out point),
            _ => NoHit(out distance, out point)
        };
    }

    private static bool TryIntersectSphere(
        RekallAgeRuntimeVector3 origin,
        RekallAgeRuntimeVector3 direction,
        double range,
        RekallAgeRuntimeVector3 center,
        double radius,
        out double distance,
        out RekallAgeRuntimeVector3 point)
    {
        var oc = Subtract(origin, center);
        var b = 2 * Dot(oc, direction);
        var c = Dot(oc, oc) - radius * radius;
        var discriminant = b * b - 4 * c;
        if (discriminant < 0)
        {
            return NoHit(out distance, out point);
        }

        var sqrt = Math.Sqrt(discriminant);
        var t0 = (-b - sqrt) * 0.5;
        var t1 = (-b + sqrt) * 0.5;
        distance = t0 >= 0 ? t0 : t1;
        if (distance < 0 || distance > range)
        {
            return NoHit(out distance, out point);
        }

        point = Add(origin, Multiply(direction, distance));
        return true;
    }

    private static double EstimateBoxBoundingRadius(RekallAgeRuntimeEntity entity, RekallAgeRuntimeComponent collider)
    {
        var width = collider.Properties.ReadNumber("width", collider.Properties.ReadNumber("Width", 1)) * entity.Transform.Scale3D.X;
        var height = collider.Properties.ReadNumber("height", collider.Properties.ReadNumber("Height", 1)) * entity.Transform.Scale3D.Y;
        var depth = collider.Properties.ReadNumber("depth", collider.Properties.ReadNumber("Depth", 1)) * entity.Transform.Scale3D.Z;
        return Math.Sqrt(width * width + height * height + depth * depth) * 0.5;
    }

    private static bool NoHit(out double distance, out RekallAgeRuntimeVector3 point)
    {
        distance = 0;
        point = new RekallAgeRuntimeVector3(0, 0, 0);
        return false;
    }

    private static RekallAgeRuntimeVector3 Normalize(RekallAgeRuntimeVector3 value)
    {
        var length = Math.Sqrt(LengthSquared(value));
        return length <= 0.000001
            ? new RekallAgeRuntimeVector3(0, 0, 0)
            : new RekallAgeRuntimeVector3(value.X / length, value.Y / length, value.Z / length);
    }

    private static double LengthSquared(RekallAgeRuntimeVector3 value)
    {
        return Dot(value, value);
    }

    private static double Dot(RekallAgeRuntimeVector3 left, RekallAgeRuntimeVector3 right)
    {
        return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
    }

    private static RekallAgeRuntimeVector3 Add(RekallAgeRuntimeVector3 left, RekallAgeRuntimeVector3 right)
    {
        return new RekallAgeRuntimeVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static RekallAgeRuntimeVector3 Subtract(RekallAgeRuntimeVector3 left, RekallAgeRuntimeVector3 right)
    {
        return new RekallAgeRuntimeVector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private static RekallAgeRuntimeVector3 Multiply(RekallAgeRuntimeVector3 value, double scalar)
    {
        return new RekallAgeRuntimeVector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
    }

    private sealed record EventBinding(string? Handler);
}
