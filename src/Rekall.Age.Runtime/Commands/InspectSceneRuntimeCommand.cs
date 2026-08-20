using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Rendering;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime.Commands;

public sealed record InspectSceneRuntimeRequest(
    string ProjectRoot,
    string SceneName,
    int Frames = 1,
    IReadOnlyList<RekallAgeRuntimeInputFrame>? Inputs = null,
    IReadOnlyList<InspectSceneRuntimeAssertion>? Assertions = null);

public sealed record InspectSceneRuntimeAssertion(
    string EntityName,
    string Subject,
    string Operator = "exists")
{
    public string? ComponentType { get; init; }

    public string? PropertyName { get; init; }

    public JsonNode? Expected { get; init; }
}

public sealed record InspectSceneRuntimeAssertionResult(
    InspectSceneRuntimeAssertion Assertion,
    bool Passed,
    JsonNode? Actual,
    string Summary);

public sealed record InspectSceneRuntimeResult(
    string SceneName,
    int FrameIndex,
    double ElapsedSeconds,
    int EntityCount,
    int RenderableCount,
    int PhysicsBodyCount,
    int PhysicsColliderCount,
    int AudioListenerCount,
    int AudioEmitterCount,
    int AnimationPlayerCount,
    int UiElementCount,
    int InputActionCount,
    IReadOnlyList<RekallAgeRuntimeInputAction> InputActions,
    int EventCount,
    IReadOnlyList<RekallAgeRuntimeEvent> Events,
    int XrRigCount,
    int XrControllerCount,
    int XrPoseCount,
    int XrActionCount,
    IReadOnlyList<RekallAgeRuntimeXrAction> XrActions,
    IReadOnlyList<string> SystemsRun,
    IReadOnlyList<RekallAgeRuntimeObservation> Observations,
    int VisibleRenderableCount,
    int CulledRenderableCount,
    IReadOnlyList<InspectSceneRuntimeCulledRenderable> CulledRenderables)
{
    public int ActiveAudioVoiceCount { get; init; }

    public int AudioBusCount { get; init; }

    public double AudioPeakGain { get; init; }

    public int AudioMixedSampleCount { get; init; }

    public IReadOnlyList<RekallAgeRuntimeAudioVoice> AudioVoices { get; init; } =
        Array.Empty<RekallAgeRuntimeAudioVoice>();

    public bool AudioVoicesTruncated { get; init; }

    public IReadOnlyList<RekallAgeRuntimeAnimationPlayer> AnimationPlayers { get; init; } =
        Array.Empty<RekallAgeRuntimeAnimationPlayer>();

    public bool AnimationPlayersTruncated { get; init; }

    public IReadOnlyList<RekallAgeRuntimeMorphState> MorphStates { get; init; } =
        Array.Empty<RekallAgeRuntimeMorphState>();

    public bool MorphStatesTruncated { get; init; }

    public IReadOnlyList<InspectSceneRuntimeEntityState> EntityStates { get; init; } =
        Array.Empty<InspectSceneRuntimeEntityState>();

    public bool EntityStatesTruncated { get; init; }

    public int UiCanvasCount { get; init; }

    public int InteractiveUiElementCount { get; init; }

    public IReadOnlyList<RekallAgeRuntimeUiCanvas> UiCanvases { get; init; } =
        Array.Empty<RekallAgeRuntimeUiCanvas>();

    public IReadOnlyList<RekallAgeRuntimeUiElement> UiElements { get; init; } =
        Array.Empty<RekallAgeRuntimeUiElement>();

    public bool UiElementsTruncated { get; init; }

    public bool AssertionsPassed { get; init; } = true;

    public IReadOnlyList<InspectSceneRuntimeAssertionResult> AssertionResults { get; init; } =
        Array.Empty<InspectSceneRuntimeAssertionResult>();
}

public sealed record InspectSceneRuntimeEntityState(
    string EntityId,
    string EntityName,
    bool Visible,
    RekallAgeRuntimeTransform Transform,
    IReadOnlyList<string> ComponentTypes)
{
    public RekallAgeRuntimeTransform InitialTransform { get; init; } = RekallAgeRuntimeTransform.Identity;

    public RekallAgeRuntimeVector2 PositionDelta2D { get; init; } = new(0, 0);

    public RekallAgeRuntimeVector3 PositionDelta3D { get; init; } = new(0, 0, 0);

    public InspectSceneRuntimePhysicsBodyState? Physics { get; init; }
}

public sealed record InspectSceneRuntimeQuaternion(double X, double Y, double Z, double W);

public sealed record InspectSceneRuntimePhysicsBodyState(
    string Backend,
    bool Awake,
    RekallAgeRuntimeVector3 LinearVelocity,
    double LinearSpeed,
    RekallAgeRuntimeVector3 AngularVelocityDegrees,
    double AngularSpeedDegrees,
    InspectSceneRuntimeQuaternion Orientation,
    double PeakLinearSpeed,
    int PeakLinearSpeedFrame,
    RekallAgeRuntimeVector3 PeakLinearVelocity,
    double PeakAngularSpeedDegrees,
    int PeakAngularSpeedFrame,
    RekallAgeRuntimeVector3 PeakAngularVelocityDegrees);

public sealed record InspectSceneRuntimeCulledRenderable(
    string EntityId,
    string EntityName,
    string Kind,
    string Layer,
    string Reason,
    string? CameraEntityName,
    string CullingMask);

public sealed class InspectSceneRuntimeCommand : IRekallAgeCommand<InspectSceneRuntimeRequest, InspectSceneRuntimeResult>
{
    public string Name => "rekall.runtime.inspect_scene";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects deterministic scene simulation after a requested frame count without requiring a compiled playable adapter; reports physics, animation, UI, audio, events, systems, and bounded entity states. For executable behavior proof, pass representative input frames plus 1-64 assertions. Assertion shape: {\"entityName\":\"Player\",\"subject\":\"component.property\",\"operator\":\"greater-than-or-equal\",\"componentType\":\"Game.Modules.Rules.PlayerState\",\"propertyName\":\"Score\",\"expected\":1}. Subjects: entity, visible, component, component.property, delta.component.property, changed.component.property, transform.position2d.x/y, transform.position3d.x/y/z, delta.position2d.x/y, delta.position3d.x/y/z. delta.component.property reports numeric final-minus-initial state; changed.component.property reports whether the bounded JSON value changed. Operators: exists, not-exists, equals, not-equals, contains, greater-than, greater-than-or-equal, less-than, less-than-or-equal. Any failed assertion fails the command with actual bounded values.",
        typeof(InspectSceneRuntimeRequest).FullName!,
        typeof(InspectSceneRuntimeResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectSceneRuntimeResult>> ExecuteAsync(
        InspectSceneRuntimeRequest request,
        RekallAgeCommandContext context)
    {
        if (request.Frames < 0)
        {
            var empty = new InspectSceneRuntimeResult(
                request.SceneName,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                Array.Empty<RekallAgeRuntimeInputAction>(),
                0,
                Array.Empty<RekallAgeRuntimeEvent>(),
                0,
                0,
                0,
                0,
                Array.Empty<RekallAgeRuntimeXrAction>(),
                Array.Empty<string>(),
                Array.Empty<RekallAgeRuntimeObservation>(),
                0,
                0,
                Array.Empty<InspectSceneRuntimeCulledRenderable>());
            return RekallAgeCommandResult<InspectSceneRuntimeResult>.Failure(
                empty,
                "Runtime inspection requires a non-negative frame count.",
                [
                    new RekallAgeCommandError(
                        "REKALL_RUNTIME_INVALID_FRAMES",
                        "Frame count cannot be negative.",
                        request.SceneName)
                ]);
        }

        var snapshotService = new RekallAgeRuntimeSnapshotService();
        var initialWorld = await snapshotService.InspectSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            0,
            null,
            context.CancellationToken);
        var physicsTelemetry = new PhysicsTelemetryAccumulator();
        var world = await snapshotService.InspectSceneTimelineAsync(
            request.ProjectRoot,
            request.SceneName,
            Math.Max(0, request.Frames),
            request.Inputs,
            physicsTelemetry.Observe,
            context.CancellationToken);
        var assertionResults = EvaluateAssertions(world, initialWorld, request.Assertions ?? []);
        var assertionsPassed = assertionResults.All(assertion => assertion.Passed);
        var result = ToResult(world, initialWorld, physicsTelemetry.Peaks) with
        {
            AssertionsPassed = assertionsPassed,
            AssertionResults = assertionResults
        };
        if (!assertionsPassed)
        {
            var failedCount = assertionResults.Count(assertion => !assertion.Passed);
            return RekallAgeCommandResult<InspectSceneRuntimeResult>.Failure(
                result,
                $"Runtime inspection completed, but {failedCount} behavior assertion(s) failed.",
                [
                    new RekallAgeCommandError(
                        "REKALL_RUNTIME_ASSERTION_FAILED",
                        $"{failedCount} runtime behavior assertion(s) failed. Inspect the bounded assertion results and repair authored content.",
                        request.SceneName)
                ]);
        }

        return RekallAgeCommandResult<InspectSceneRuntimeResult>.Success(
            result,
            $"Runtime {result.SceneName} frame {result.FrameIndex}: {result.EntityCount} entities, {result.RenderableCount} renderable, {assertionResults.Count} behavior assertion(s) passed.");
    }

    private static IReadOnlyList<InspectSceneRuntimeAssertionResult> EvaluateAssertions(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorld initialWorld,
        IReadOnlyList<InspectSceneRuntimeAssertion> assertions)
    {
        const int maximumAssertions = 64;
        var bounded = assertions.Take(maximumAssertions).ToArray();
        var initialEntities = initialWorld.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var results = new List<InspectSceneRuntimeAssertionResult>(bounded.Length + (assertions.Count > maximumAssertions ? 1 : 0));
        foreach (var assertion in bounded)
        {
            var entities = world.Entities
                .Where(entity => entity.Name.Equals(assertion.EntityName, StringComparison.Ordinal))
                .ToArray();
            if (entities.Length != 1)
            {
                var missingEntityExpected = entities.Length == 0
                    && assertion.Subject.Equals("entity", StringComparison.OrdinalIgnoreCase)
                    && assertion.Operator.Equals("not-exists", StringComparison.OrdinalIgnoreCase);
                results.Add(new InspectSceneRuntimeAssertionResult(
                    assertion,
                    missingEntityExpected,
                    null,
                    missingEntityExpected
                        ? $"entity not-exists assertion passed for '{assertion.EntityName}'."
                        : entities.Length == 0
                        ? $"Entity '{assertion.EntityName}' was not found."
                        : $"Entity name '{assertion.EntityName}' is ambiguous ({entities.Length} matches)."));
                continue;
            }

            var entity = entities[0];
            initialEntities.TryGetValue(entity.Id, out var initialEntity);
            var resolved = ResolveAssertionSubject(entity, initialEntity, assertion);
            if (!resolved.Valid)
            {
                results.Add(new InspectSceneRuntimeAssertionResult(assertion, false, resolved.Actual, resolved.Summary));
                continue;
            }

            var passed = Compare(resolved.Actual, assertion.Expected, assertion.Operator, out var comparisonSummary);
            results.Add(new InspectSceneRuntimeAssertionResult(
                assertion,
                passed,
                resolved.Actual,
                passed
                    ? $"{assertion.Subject} {assertion.Operator} assertion passed for '{assertion.EntityName}'."
                    : $"{resolved.Summary} {comparisonSummary} Entity '{assertion.EntityName}', subject '{assertion.Subject}'.".Trim()));
        }

        if (assertions.Count > maximumAssertions)
        {
            results.Add(new InspectSceneRuntimeAssertionResult(
                new InspectSceneRuntimeAssertion("*", "assertion.limit", "less-than-or-equal")
                {
                    Expected = JsonValue.Create(maximumAssertions)
                },
                false,
                JsonValue.Create(assertions.Count),
                $"Runtime inspection accepts at most {maximumAssertions} assertions; received {assertions.Count}."));
        }

        return results;
    }

    private static (bool Valid, JsonNode? Actual, string Summary) ResolveAssertionSubject(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeEntity? initialEntity,
        InspectSceneRuntimeAssertion assertion)
    {
        var subject = RekallAgeRuntimeAssertionSubjects.Normalize(assertion.Subject);
        if (subject == "entity") return (true, JsonValue.Create(entity.Id), string.Empty);
        if (subject == "visible") return (true, JsonValue.Create(entity.Visible), string.Empty);
        if (subject is "component" or "component.property" or "delta.component.property" or "changed.component.property")
        {
            if (string.IsNullOrWhiteSpace(assertion.ComponentType))
            {
                return (false, null, "Component assertions require componentType.");
            }

            var component = FindComponent(entity, assertion.ComponentType);
            if (subject == "component")
            {
                return component is null
                    ? (true, null, $"Component '{assertion.ComponentType}' was not attached to '{entity.Name}'.")
                    : (true, JsonValue.Create(component.Type), string.Empty);
            }

            if (component is null)
            {
                return (true, null, $"Component '{assertion.ComponentType}' was not attached to '{entity.Name}'.");
            }
            if (string.IsNullOrWhiteSpace(assertion.PropertyName))
            {
                return (false, null, "component.property assertions require propertyName.");
            }

            var finalProperty = FindProperty(component, assertion.PropertyName);
            if (subject == "component.property")
            {
                return (true, finalProperty?.DeepClone(), string.Empty);
            }

            var initialComponent = initialEntity is null
                ? null
                : FindComponent(initialEntity, assertion.ComponentType);
            var initialProperty = initialComponent is null
                ? null
                : FindProperty(initialComponent, assertion.PropertyName);
            if (subject == "changed.component.property")
            {
                return (true, JsonValue.Create(!JsonNode.DeepEquals(finalProperty, initialProperty)), string.Empty);
            }

            if (finalProperty is null || initialProperty is null
                || !TryNumber(finalProperty, out var finalNumber)
                || !TryNumber(initialProperty, out var initialNumber))
            {
                return (false, null,
                    $"delta.component.property requires numeric initial and final values for '{assertion.ComponentType}.{assertion.PropertyName}'.");
            }

            return (true, JsonValue.Create(finalNumber - initialNumber), string.Empty);
        }

        var initial = initialEntity?.Transform ?? entity.Transform;
        return subject switch
        {
            "transform.position2d.x" => Number(entity.Transform.Position2D.X),
            "transform.position2d.y" => Number(entity.Transform.Position2D.Y),
            "transform.position3d.x" => Number(entity.Transform.Position3D.X),
            "transform.position3d.y" => Number(entity.Transform.Position3D.Y),
            "transform.position3d.z" => Number(entity.Transform.Position3D.Z),
            "delta.position2d.x" => Number(entity.Transform.Position2D.X - initial.Position2D.X),
            "delta.position2d.y" => Number(entity.Transform.Position2D.Y - initial.Position2D.Y),
            "delta.position3d.x" => Number(entity.Transform.Position3D.X - initial.Position3D.X),
            "delta.position3d.y" => Number(entity.Transform.Position3D.Y - initial.Position3D.Y),
            "delta.position3d.z" => Number(entity.Transform.Position3D.Z - initial.Position3D.Z),
            _ => (false, null, $"Unknown runtime assertion subject '{assertion.Subject}'.")
        };

        static (bool Valid, JsonNode? Actual, string Summary) Number(double value) =>
            (true, JsonValue.Create(value), string.Empty);

        static RekallAgeRuntimeComponent? FindComponent(RekallAgeRuntimeEntity candidate, string componentType) =>
            candidate.Components.FirstOrDefault(component =>
                component.Type.Equals(componentType, StringComparison.Ordinal));

        static JsonNode? FindProperty(RekallAgeRuntimeComponent component, string propertyName) =>
            component.Properties.FirstOrDefault(property =>
                property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static bool Compare(JsonNode? actual, JsonNode? expected, string comparisonOperator, out string summary)
    {
        var normalized = comparisonOperator.Trim().ToLowerInvariant();
        if (normalized == "exists")
        {
            summary = actual is null ? "Expected a value to exist, but it was missing." : string.Empty;
            return actual is not null;
        }
        if (normalized == "not-exists")
        {
            summary = actual is null ? string.Empty : $"Expected no value, but found {actual.ToJsonString()}.";
            return actual is null;
        }
        if (actual is null || expected is null)
        {
            summary = $"Operator '{comparisonOperator}' requires both actual and expected values.";
            return false;
        }

        if (normalized is "equals" or "not-equals")
        {
            var equal = TryNumber(actual, out var actualNumber) && TryNumber(expected, out var expectedNumber)
                ? Math.Abs(actualNumber - expectedNumber) <= 1e-9
                : JsonNode.DeepEquals(actual, expected);
            summary = equal == (normalized == "equals")
                ? string.Empty
                : $"Expected {normalized} {expected.ToJsonString()}, actual {actual.ToJsonString()}.";
            return normalized == "equals" ? equal : !equal;
        }

        if (normalized == "contains")
        {
            var actualText = TryString(actual);
            var expectedText = TryString(expected);
            var contains = actualText is not null && expectedText is not null
                && actualText.Contains(expectedText, StringComparison.Ordinal);
            summary = contains ? string.Empty : $"Expected {actual.ToJsonString()} to contain {expected.ToJsonString()}.";
            return contains;
        }

        if (!TryNumber(actual, out var left) || !TryNumber(expected, out var right))
        {
            summary = $"Numeric operator '{comparisonOperator}' requires numeric actual and expected values.";
            return false;
        }

        var passed = normalized switch
        {
            "greater-than" => left > right,
            "greater-than-or-equal" => left >= right,
            "less-than" => left < right,
            "less-than-or-equal" => left <= right,
            _ => false
        };
        summary = passed
            ? string.Empty
            : normalized is "greater-than" or "greater-than-or-equal" or "less-than" or "less-than-or-equal"
                ? $"Numeric comparison failed: actual {left.ToString("R", CultureInfo.InvariantCulture)}, expected {comparisonOperator} {right.ToString("R", CultureInfo.InvariantCulture)}."
                : $"Unknown runtime assertion operator '{comparisonOperator}'.";
        return passed;
    }

    private static bool TryNumber(JsonNode node, out double value) =>
        double.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    private static string? TryString(JsonNode node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static InspectSceneRuntimeResult ToResult(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorld initialWorld,
        IReadOnlyDictionary<string, PhysicsTelemetryPeak> physicsPeaks)
    {
        var rendering = world.Subsystems.Rendering;
        var physics = world.Subsystems.Physics;
        var audio = world.Subsystems.Audio;
        var animation = world.Subsystems.Animation;
        var ui = world.Subsystems.Ui;
        var xr = world.Subsystems.Xr;
        var culling = BuildCullingSummary(rendering);
        const int maximumEntityStates = 32;
        const int maximumSubsystemItems = 32;
        const int maximumUiElements = 32;
        var initialEntities = initialWorld.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var entityStates = world.Entities
            .OrderBy(entity => entity.Name, StringComparer.Ordinal)
            .ThenBy(entity => entity.Id, StringComparer.Ordinal)
            .Take(maximumEntityStates)
            .Select(entity =>
            {
                var initial = initialEntities.TryGetValue(entity.Id, out var initialEntity)
                    ? initialEntity.Transform
                    : entity.Transform;
                return new InspectSceneRuntimeEntityState(
                    entity.Id,
                    entity.Name,
                    entity.Visible,
                    entity.Transform,
                    entity.Components
                        .Select(component => component.Type)
                        .OrderBy(type => type, StringComparer.Ordinal)
                        .ToArray())
                {
                    InitialTransform = initial,
                    PositionDelta2D = new RekallAgeRuntimeVector2(
                        entity.Transform.Position2D.X - initial.Position2D.X,
                        entity.Transform.Position2D.Y - initial.Position2D.Y),
                    PositionDelta3D = new RekallAgeRuntimeVector3(
                        entity.Transform.Position3D.X - initial.Position3D.X,
                        entity.Transform.Position3D.Y - initial.Position3D.Y,
                        entity.Transform.Position3D.Z - initial.Position3D.Z),
                    Physics = BuildPhysicsBodyState(entity, physicsPeaks)
                };
            })
            .ToArray();

        return new InspectSceneRuntimeResult(
            world.SceneName,
            world.FrameIndex,
            world.ElapsedTime.TotalSeconds,
            world.Entities.Count,
            rendering.Cameras.Count + rendering.Sprites.Count + rendering.Meshes.Count + rendering.Lights.Count + rendering.UiLayers.Count,
            physics.RigidBodies.Count,
            physics.Colliders.Count,
            audio.Listeners.Count,
            audio.Emitters.Count,
            animation.Players.Count,
            ui.Elements.Count,
            world.Subsystems.Input.Actions.Count,
            world.Subsystems.Input.Actions,
            world.Subsystems.Events.Events.Count,
            world.Subsystems.Events.Events,
            xr.Rigs.Count,
            xr.Controllers.Count,
            xr.Poses.Count,
            xr.Actions.Count,
            xr.Actions,
            world.SystemsRun,
            world.Observations,
            culling.VisibleRenderableCount,
            culling.CulledRenderables.Count,
            culling.CulledRenderables)
        {
            ActiveAudioVoiceCount = audio.MixFrame.ActiveVoiceCount,
            AudioBusCount = audio.Buses.Count,
            AudioPeakGain = audio.MixFrame.PeakGain,
            AudioMixedSampleCount = audio.MixFrame.Samples?.Count ?? 0,
            AudioVoices = audio.Voices.Take(maximumSubsystemItems).ToArray(),
            AudioVoicesTruncated = audio.Voices.Count > maximumSubsystemItems,
            AnimationPlayers = animation.Players.Take(maximumSubsystemItems).ToArray(),
            AnimationPlayersTruncated = animation.Players.Count > maximumSubsystemItems,
            MorphStates = animation.MorphStates.Take(maximumSubsystemItems).ToArray(),
            MorphStatesTruncated = animation.MorphStates.Count > maximumSubsystemItems,
            EntityStates = entityStates,
            EntityStatesTruncated = world.Entities.Count > maximumEntityStates,
            UiCanvasCount = ui.Canvases.Count,
            InteractiveUiElementCount = ui.InteractiveElementCount,
            UiCanvases = ui.Canvases.Take(maximumSubsystemItems).ToArray(),
            UiElements = ui.Elements
                .OrderBy(element => element.EntityName, StringComparer.Ordinal)
                .ThenBy(element => element.EntityId, StringComparer.Ordinal)
                .Take(maximumUiElements)
                .ToArray(),
            UiElementsTruncated = ui.Elements.Count > maximumUiElements
        };
    }

    private static InspectSceneRuntimePhysicsBodyState? BuildPhysicsBodyState(
        RekallAgeRuntimeEntity entity,
        IReadOnlyDictionary<string, PhysicsTelemetryPeak> peaks)
    {
        var component = entity.Components.FirstOrDefault(candidate =>
            candidate.Type is "Rekall.PhysicsState2D" or "Rekall.PhysicsState3D");
        if (component is null)
        {
            return null;
        }

        var linear = ReadTelemetryVector(component.Properties, "linearVelocity");
        var angular = ReadTelemetryVector(component.Properties, "angularVelocity");
        var orientation = component.Properties["orientation"] as JsonObject;
        var peak = peaks.TryGetValue(entity.Id, out var observed)
            ? observed
            : new PhysicsTelemetryPeak(
                Magnitude(linear),
                0,
                linear,
                Magnitude(angular),
                0,
                angular);
        return new InspectSceneRuntimePhysicsBodyState(
            ReadTelemetryString(component.Properties, "backend", "unknown"),
            ReadTelemetryBoolean(component.Properties, "awake", false),
            linear,
            Magnitude(linear),
            angular,
            Magnitude(angular),
            new InspectSceneRuntimeQuaternion(
                ReadTelemetryNumber(orientation, "x", 0),
                ReadTelemetryNumber(orientation, "y", 0),
                ReadTelemetryNumber(orientation, "z", 0),
                ReadTelemetryNumber(orientation, "w", 1)),
            peak.PeakLinearSpeed,
            peak.PeakLinearSpeedFrame,
            peak.PeakLinearVelocity,
            peak.PeakAngularSpeedDegrees,
            peak.PeakAngularSpeedFrame,
            peak.PeakAngularVelocityDegrees);
    }

    private static RekallAgeRuntimeVector3 ReadTelemetryVector(JsonObject properties, string name)
    {
        var value = properties[name] as JsonObject;
        return new RekallAgeRuntimeVector3(
            ReadTelemetryNumber(value, "x", 0),
            ReadTelemetryNumber(value, "y", 0),
            ReadTelemetryNumber(value, "z", 0));
    }

    private static double ReadTelemetryNumber(JsonObject? properties, string name, double fallback)
    {
        if (properties?[name] is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        return value.TryGetValue<float>(out var singleValue) ? singleValue : fallback;
    }

    private static bool ReadTelemetryBoolean(JsonObject properties, string name, bool fallback) =>
        properties[name] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;

    private static string ReadTelemetryString(JsonObject properties, string name, string fallback) =>
        properties[name] is JsonValue value && value.TryGetValue<string>(out var result) ? result : fallback;

    private static double Magnitude(RekallAgeRuntimeVector3 value) =>
        Math.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));

    private sealed class PhysicsTelemetryAccumulator
    {
        private const int MaximumTrackedBodies = 128;
        private readonly Dictionary<string, PhysicsTelemetryPeak> _peaks = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, PhysicsTelemetryPeak> Peaks => _peaks;

        public void Observe(RekallAgeRuntimeWorld world)
        {
            foreach (var entity in world.Entities)
            {
                var component = entity.Components.FirstOrDefault(candidate =>
                    candidate.Type is "Rekall.PhysicsState2D" or "Rekall.PhysicsState3D");
                if (component is null
                    || (!_peaks.ContainsKey(entity.Id) && _peaks.Count >= MaximumTrackedBodies))
                {
                    continue;
                }

                var linear = ReadTelemetryVector(component.Properties, "linearVelocity");
                var angular = ReadTelemetryVector(component.Properties, "angularVelocity");
                var linearSpeed = Magnitude(linear);
                var angularSpeed = Magnitude(angular);
                if (!_peaks.TryGetValue(entity.Id, out var peak))
                {
                    _peaks[entity.Id] = new PhysicsTelemetryPeak(
                        linearSpeed,
                        world.FrameIndex,
                        linear,
                        angularSpeed,
                        world.FrameIndex,
                        angular);
                    continue;
                }

                if (linearSpeed > peak.PeakLinearSpeed)
                {
                    peak = peak with
                    {
                        PeakLinearSpeed = linearSpeed,
                        PeakLinearSpeedFrame = world.FrameIndex,
                        PeakLinearVelocity = linear
                    };
                }

                if (angularSpeed > peak.PeakAngularSpeedDegrees)
                {
                    peak = peak with
                    {
                        PeakAngularSpeedDegrees = angularSpeed,
                        PeakAngularSpeedFrame = world.FrameIndex,
                        PeakAngularVelocityDegrees = angular
                    };
                }

                _peaks[entity.Id] = peak;
            }
        }
    }

    private sealed record PhysicsTelemetryPeak(
        double PeakLinearSpeed,
        int PeakLinearSpeedFrame,
        RekallAgeRuntimeVector3 PeakLinearVelocity,
        double PeakAngularSpeedDegrees,
        int PeakAngularSpeedFrame,
        RekallAgeRuntimeVector3 PeakAngularVelocityDegrees);

    private static RuntimeCullingSummary BuildCullingSummary(RekallAgeRuntimeRenderView rendering)
    {
        var activeCamera = rendering.Cameras
            .OrderByDescending(camera => camera.Active)
            .ThenBy(camera => camera.EntityName, StringComparer.Ordinal)
            .ThenBy(camera => camera.EntityId, StringComparer.Ordinal)
            .FirstOrDefault();
        var candidates = EnumerateRenderableCandidates(rendering).ToArray();
        var culled = candidates
            .Where(candidate => !RekallAgeRenderLayerMask.IncludesLayer(candidate.Layer, activeCamera?.CullingMask))
            .Select(candidate => new InspectSceneRuntimeCulledRenderable(
                candidate.EntityId,
                candidate.EntityName,
                candidate.Kind,
                candidate.Layer,
                "camera-culling-mask",
                activeCamera?.EntityName,
                activeCamera?.CullingMask ?? "*"))
            .OrderBy(candidate => candidate.EntityName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.EntityId, StringComparer.Ordinal)
            .ToArray();

        return new RuntimeCullingSummary(candidates.Length - culled.Length, culled);
    }

    private static IEnumerable<RuntimeRenderableCandidate> EnumerateRenderableCandidates(
        RekallAgeRuntimeRenderView rendering)
    {
        foreach (var sprite in rendering.Sprites)
        {
            yield return new RuntimeRenderableCandidate(sprite.EntityId, sprite.EntityName, "sprite", RekallAgeRenderLayerMask.NormalizeLayer(sprite.Layer));
        }

        foreach (var mesh in rendering.Meshes)
        {
            yield return new RuntimeRenderableCandidate(mesh.EntityId, mesh.EntityName, "mesh", RekallAgeRenderLayerMask.NormalizeLayer(mesh.Layer));
        }

        foreach (var light in rendering.Lights)
        {
            yield return new RuntimeRenderableCandidate(light.EntityId, light.EntityName, "light", RekallAgeRenderLayerMask.NormalizeLayer(light.Layer));
        }

        foreach (var uiLayer in rendering.UiLayers)
        {
            yield return new RuntimeRenderableCandidate(uiLayer.EntityId, uiLayer.EntityName, "ui", "default");
        }
    }

    private sealed record RuntimeCullingSummary(
        int VisibleRenderableCount,
        IReadOnlyList<InspectSceneRuntimeCulledRenderable> CulledRenderables);

    private sealed record RuntimeRenderableCandidate(
        string EntityId,
        string EntityName,
        string Kind,
        string Layer);
}
