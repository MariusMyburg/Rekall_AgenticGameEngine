using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private sealed record SkinEnvelope(
        int JointIndex,
        RekallAgeGeometryVector3 Head,
        RekallAgeGeometryVector3 Tail,
        double HeadRadius,
        double TailRadius,
        double Falloff,
        double Weight);

    private static RekallAgeMeshOperationResult AssignEnvelopeSkinWeights(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Point);
        var selectedIndices = ResolveIndices(source.Topology.PointIds, request.ElementIds, "point");
        var maximumInfluences = ReadBoundedInt(request.Parameters, "maximumInfluences", 4, 1, 4);
        var fallbackToNearest = ReadBoolean(request.Parameters, "fallbackToNearest", true);
        var envelopes = ReadEnvelopes(request.Parameters);

        var existingJoints = FindSkinAttribute("joint-indices-0", RekallAgeGeometryValueType.Int4);
        var existingWeights = FindSkinAttribute("joint-weights-0", RekallAgeGeometryValueType.Float4);
        if ((existingJoints is null) != (existingWeights is null))
            throw Failure(
                "REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT",
                "Skin joint indices and weights must either both exist or both be absent.");
        EnsureCanonicalOutputNameIsAvailable("skin.joints", existingJoints);
        EnsureCanonicalOutputNameIsAvailable("skin.weights", existingWeights);
        var jointValues = existingJoints?.Values.ToArray()
            ?? source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(new[] { 0, 0, 0, 0 })).ToArray();
        var weightValues = existingWeights?.Values.ToArray()
            ?? source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(new[] { 1d, 0d, 0d, 0d })).ToArray();

        foreach (var index in selectedIndices)
        {
            var position = source.Topology.Positions[index];
            var byJoint = new Dictionary<int, double>();
            SkinEnvelope? nearest = null;
            var nearestDistance = double.PositiveInfinity;
            foreach (var envelope in envelopes)
            {
                var (influence, surfaceDistance) = Influence(position, envelope);
                if (surfaceDistance < nearestDistance
                    || (surfaceDistance == nearestDistance && envelope.JointIndex < nearest!.JointIndex))
                {
                    nearest = envelope;
                    nearestDistance = surfaceDistance;
                }
                if (influence > 0
                    && (!byJoint.TryGetValue(envelope.JointIndex, out var current) || influence > current))
                {
                    byJoint[envelope.JointIndex] = influence;
                }
            }

            if (byJoint.Count == 0)
            {
                if (!fallbackToNearest)
                    throw Failure(
                        "REKALL_MESH_SKIN_POINT_UNBOUND",
                        $"Point {source.Topology.PointIds[index]} has no positive envelope influence.");
                byJoint.Add(nearest!.JointIndex, 1);
            }

            var selected = byJoint
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key)
                .Take(maximumInfluences)
                .ToArray();
            var total = selected.Sum(item => item.Value);
            var joints = new int[4];
            var weights = new double[4];
            for (var influenceIndex = 0; influenceIndex < selected.Length; influenceIndex++)
            {
                joints[influenceIndex] = selected[influenceIndex].Key;
                weights[influenceIndex] = selected[influenceIndex].Value / total;
            }
            jointValues[index] = JsonSerializer.SerializeToElement(joints);
            weightValues[index] = JsonSerializer.SerializeToElement(weights);
        }

        var jointsAttribute = new RekallAgeGeometryAttribute(
            existingJoints?.Name ?? "skin.joints", RekallAgeGeometryDomain.Point,
            RekallAgeGeometryValueType.Int4, jointValues, "joint-indices-0",
            RekallAgeGeometryInterpolation.Nearest);
        var weightsAttribute = new RekallAgeGeometryAttribute(
            existingWeights?.Name ?? "skin.weights", RekallAgeGeometryDomain.Point,
            RekallAgeGeometryValueType.Float4, weightValues, "joint-weights-0",
            RekallAgeGeometryInterpolation.NormalizedLinear);
        var replacedNames = new HashSet<string>([jointsAttribute.Name, weightsAttribute.Name], StringComparer.Ordinal);
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Attributes = source.Attributes.Where(item => !replacedNames.Contains(item.Name))
                .Append(jointsAttribute).Append(weightsAttribute).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray()
        };
        return Result(source, mesh,
            ChangeSet(RekallAgeMeshChangeKind.Attributes,
                modifiedPoints: request.ElementIds.Order().ToArray(),
                changedAttributes: [jointsAttribute.Name, weightsAttribute.Name],
                affectedBounds: Bounds(selectedIndices.Select(index => source.Topology.Positions[index]))),
            request.ElementIds.Order().Select(id => Preserve(RekallAgeGeometryDomain.Point, id)).ToArray());

        RekallAgeGeometryAttribute? FindSkinAttribute(string semantic, RekallAgeGeometryValueType type)
        {
            var matches = source.Attributes.Where(item =>
                string.Equals(item.Semantic, semantic, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1)
                throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Multiple attributes publish skin semantic '{semantic}'.");
            var found = matches.SingleOrDefault();
            if (found is not null && (found.Domain != RekallAgeGeometryDomain.Point || found.ValueType != type))
                throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Skin semantic '{semantic}' has an incompatible domain or type.");
            return found;
        }

        void EnsureCanonicalOutputNameIsAvailable(string canonicalName, RekallAgeGeometryAttribute? existingSemanticAttribute)
        {
            if (existingSemanticAttribute is not null) return;
            if (source.Attributes.Any(item => string.Equals(item.Name, canonicalName, StringComparison.OrdinalIgnoreCase)))
                throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Attribute '{canonicalName}' is already used by an unrelated attribute.");
        }
    }

    private static IReadOnlyList<SkinEnvelope> ReadEnvelopes(JsonObject parameters)
    {
        if (parameters["envelopes"] is not JsonArray { Count: >= 1 and <= 256 } array)
            throw Failure("REKALL_MESH_SKIN_ENVELOPES_INVALID", "Parameter 'envelopes' must contain from 1 through 256 envelope objects.");
        var envelopes = new List<SkinEnvelope>(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is not JsonObject item)
                throw Invalid(index, "must be an object");
            var jointIndex = EnvelopeInt(item, "jointIndex", index, 0, int.MaxValue);
            var head = EnvelopeVector(item, "head", index);
            var tail = EnvelopeVector(item, "tail", index);
            var headRadius = EnvelopeNumber(item, "headRadius", index);
            var tailRadius = EnvelopeNumber(item, "tailRadius", index);
            var falloff = EnvelopeNumber(item, "falloff", index, 0);
            var weight = EnvelopeNumber(item, "weight", index, double.Epsilon, 1);
            if (headRadius <= 0 || tailRadius <= 0)
                throw Invalid(index, "headRadius and tailRadius must be positive");
            envelopes.Add(new(jointIndex, head, tail, headRadius, tailRadius, falloff, weight));
        }
        return envelopes;

        static RekallAgeMeshOperationException Invalid(int index, string message) =>
            Failure("REKALL_MESH_SKIN_ENVELOPES_INVALID", $"Envelope {index} {message}.");

        static int EnvelopeInt(JsonObject item, string name, int index, int minimum, int maximum)
        {
            if (item[name] is not JsonValue value || !value.TryGetValue<int>(out var result)
                || result < minimum || result > maximum)
                throw Invalid(index, $"'{name}' must be an integer from {minimum} through {maximum}");
            return result;
        }

        static double EnvelopeNumber(JsonObject item, string name, int index, double minimum = double.NegativeInfinity, double defaultValue = double.NaN)
        {
            if (item[name] is null && double.IsFinite(defaultValue)) return defaultValue;
            if (item[name] is not JsonValue value || !TryReadNumber(value, out var result)
                || !double.IsFinite(result) || result < minimum)
                throw Invalid(index, $"'{name}' must be a finite number no smaller than {minimum}");
            return result;
        }

        static RekallAgeGeometryVector3 EnvelopeVector(JsonObject item, string name, int index)
        {
            if (item[name] is not JsonArray { Count: 3 } values)
                throw Invalid(index, $"'{name}' must be a three-number array");
            var result = new double[3];
            for (var component = 0; component < 3; component++)
            {
                if (values[component] is not JsonValue value || !TryReadNumber(value, out result[component])
                    || !double.IsFinite(result[component]))
                    throw Invalid(index, $"'{name}' must contain only finite numbers");
            }
            return new(result[0], result[1], result[2]);
        }
    }

    private static (double Influence, double SurfaceDistance) Influence(
        RekallAgeGeometryVector3 point,
        SkinEnvelope envelope)
    {
        var axisX = envelope.Tail.X - envelope.Head.X;
        var axisY = envelope.Tail.Y - envelope.Head.Y;
        var axisZ = envelope.Tail.Z - envelope.Head.Z;
        var lengthSquared = axisX * axisX + axisY * axisY + axisZ * axisZ;
        var projection = lengthSquared <= 1e-20 ? 0 : Math.Clamp(
            ((point.X - envelope.Head.X) * axisX
             + (point.Y - envelope.Head.Y) * axisY
             + (point.Z - envelope.Head.Z) * axisZ) / lengthSquared,
            0,
            1);
        var closestX = envelope.Head.X + axisX * projection;
        var closestY = envelope.Head.Y + axisY * projection;
        var closestZ = envelope.Head.Z + axisZ * projection;
        var dx = point.X - closestX;
        var dy = point.Y - closestY;
        var dz = point.Z - closestZ;
        var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        var radius = envelope.HeadRadius + (envelope.TailRadius - envelope.HeadRadius) * projection;
        var surfaceDistance = Math.Max(0, distance - radius);
        if (surfaceDistance <= 0) return (envelope.Weight, 0);
        if (envelope.Falloff <= 0 || surfaceDistance >= envelope.Falloff) return (0, surfaceDistance);
        var ratio = surfaceDistance / envelope.Falloff;
        return ((1 - ratio * ratio) * envelope.Weight, surfaceDistance);
    }

    private static RekallAgeMeshOperationResult AssignLinearSkinWeights(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Point);
        var selectedIndices = ResolveIndices(source.Topology.PointIds, request.ElementIds, "point");
        var axis = ReadBoundedString(request.Parameters, "axis", "y").ToLowerInvariant();
        if (axis is not ("x" or "y" or "z"))
            throw Failure("REKALL_MESH_SKIN_AXIS_INVALID", "Skin weight axis must be x, y, or z.");
        var minimum = ReadFiniteDouble(request.Parameters, "minimum");
        var maximum = ReadFiniteDouble(request.Parameters, "maximum", 1);
        if (maximum <= minimum)
            throw Failure("REKALL_MESH_SKIN_RANGE_INVALID", "Skin weight maximum must be greater than minimum.");
        var jointA = ReadBoundedInt(request.Parameters, "jointA", 0, 0, int.MaxValue);
        var jointB = ReadBoundedInt(request.Parameters, "jointB", 1, 0, int.MaxValue);

        var existingJoints = FindSkinAttribute("joint-indices-0", RekallAgeGeometryValueType.Int4);
        var existingWeights = FindSkinAttribute("joint-weights-0", RekallAgeGeometryValueType.Float4);
        if ((existingJoints is null) != (existingWeights is null))
            throw Failure(
                "REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT",
                "Skin joint indices and weights must either both exist or both be absent.");
        EnsureCanonicalOutputNameIsAvailable("skin.joints", existingJoints);
        EnsureCanonicalOutputNameIsAvailable("skin.weights", existingWeights);
        var jointValues = existingJoints?.Values.ToArray()
            ?? source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(new[] { jointA, jointB, 0, 0 })).ToArray();
        var weightValues = existingWeights?.Values.ToArray()
            ?? source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(new[] { 1d, 0d, 0d, 0d })).ToArray();
        foreach (var index in selectedIndices)
        {
            var position = source.Topology.Positions[index];
            var coordinate = axis switch { "x" => position.X, "y" => position.Y, _ => position.Z };
            var blend = Math.Clamp((coordinate - minimum) / (maximum - minimum), 0, 1);
            jointValues[index] = JsonSerializer.SerializeToElement(new[] { jointA, jointB, 0, 0 });
            weightValues[index] = JsonSerializer.SerializeToElement(new[] { 1 - blend, blend, 0d, 0d });
        }

        var joints = new RekallAgeGeometryAttribute(
            existingJoints?.Name ?? "skin.joints", RekallAgeGeometryDomain.Point,
            RekallAgeGeometryValueType.Int4, jointValues, "joint-indices-0",
            RekallAgeGeometryInterpolation.Nearest);
        var weights = new RekallAgeGeometryAttribute(
            existingWeights?.Name ?? "skin.weights", RekallAgeGeometryDomain.Point,
            RekallAgeGeometryValueType.Float4, weightValues, "joint-weights-0",
            RekallAgeGeometryInterpolation.NormalizedLinear);
        var replacedNames = new HashSet<string>([joints.Name, weights.Name], StringComparer.Ordinal);
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Attributes = source.Attributes.Where(item => !replacedNames.Contains(item.Name))
                .Append(joints).Append(weights).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray()
        };
        return Result(source, mesh,
            ChangeSet(RekallAgeMeshChangeKind.Attributes,
                modifiedPoints: request.ElementIds.Order().ToArray(),
                changedAttributes: [joints.Name, weights.Name],
                affectedBounds: Bounds(selectedIndices.Select(index => source.Topology.Positions[index]))),
            request.ElementIds.Order().Select(id => Preserve(RekallAgeGeometryDomain.Point, id)).ToArray());

        RekallAgeGeometryAttribute? FindSkinAttribute(string semantic, RekallAgeGeometryValueType type)
        {
            var matches = source.Attributes.Where(item =>
                string.Equals(item.Semantic, semantic, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1)
                throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Multiple attributes publish skin semantic '{semantic}'.");
            var found = matches.SingleOrDefault();
            if (found is not null && (found.Domain != RekallAgeGeometryDomain.Point || found.ValueType != type))
                throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Skin semantic '{semantic}' has an incompatible domain or type.");
            return found;
        }

        void EnsureCanonicalOutputNameIsAvailable(
            string canonicalName,
            RekallAgeGeometryAttribute? existingSemanticAttribute)
        {
            if (existingSemanticAttribute is not null)
                return;
            if (source.Attributes.Any(item => string.Equals(item.Name, canonicalName, StringComparison.OrdinalIgnoreCase)))
                throw Failure(
                    "REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT",
                    $"Attribute '{canonicalName}' is already used by an unrelated attribute.");
        }
    }
}
