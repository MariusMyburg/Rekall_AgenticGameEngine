using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private static RekallAgeMeshOperationResult ShadeFaces(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        return SetBooleanPolicy(
            source,
            request,
            source.Topology.FaceIds,
            RekallAgeGeometryDomain.Face,
            "face",
            "normal.smooth",
            "smooth",
            defaultValue: true,
            semantic: "normal-smooth");
    }

    private static RekallAgeMeshOperationResult MarkSharp(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Edge);
        return SetBooleanPolicy(
            source,
            request,
            source.Topology.EdgeIds,
            RekallAgeGeometryDomain.Edge,
            "edge",
            "normal.sharp",
            "sharp",
            defaultValue: false,
            semantic: "normal-sharp");
    }

    private static RekallAgeMeshOperationResult SetBooleanPolicy(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request,
        IReadOnlyList<ulong> domainIds,
        RekallAgeGeometryDomain domain,
        string domainName,
        string defaultAttributeName,
        string valueParameter,
        bool defaultValue,
        string semantic)
    {
        var indices = ResolveIndices(domainIds, request.ElementIds, domainName);
        var attributeName = ReadBoundedString(request.Parameters, "attribute", defaultAttributeName);
        var value = ReadBoolean(request.Parameters, valueParameter, true);
        var existing = source.Attributes.FirstOrDefault(item => item.Name == attributeName);
        if (existing is not null
            && (existing.Domain != domain || existing.ValueType != RekallAgeGeometryValueType.Bool))
        {
            throw Failure(
                "REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT",
                $"Attribute '{attributeName}' exists with an incompatible domain or type.");
        }

        var values = existing?.Values.ToArray()
            ?? Enumerable.Repeat(JsonSerializer.SerializeToElement(defaultValue), domainIds.Count).ToArray();
        foreach (var index in indices)
        {
            values[index] = JsonSerializer.SerializeToElement(value);
        }

        var attribute = new RekallAgeGeometryAttribute(
            attributeName,
            domain,
            RekallAgeGeometryValueType.Bool,
            values,
            semantic,
            RekallAgeGeometryInterpolation.Nearest,
            JsonSerializer.SerializeToElement(defaultValue));
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Attributes = source.Attributes
                .Where(item => item.Name != attributeName)
                .Append(attribute)
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ToArray()
        };
        var ids = request.ElementIds.Order().ToArray();
        var changes = domain == RekallAgeGeometryDomain.Face
            ? ChangeSet(RekallAgeMeshChangeKind.Attributes, modifiedFaces: ids, changedAttributes: [attributeName])
            : ChangeSet(RekallAgeMeshChangeKind.Attributes, modifiedEdges: ids, changedAttributes: [attributeName]);
        return Result(source, mesh, changes, ids.Select(id => Preserve(domain, id)).ToArray());
    }

    private static RekallAgeMeshOperationResult AutoSmooth(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireCompleteFaceSelection(source, request, "Auto smooth");
        var angleDegrees = ReadFiniteDouble(request.Parameters, "angleDegrees", 60);
        if (angleDegrees is < 0 or > 180)
        {
            throw Failure(
                "REKALL_MESH_OPERATION_PARAMETER_INVALID",
                "angleDegrees must be between 0 and 180.");
        }

        var attributeName = ReadBoundedString(request.Parameters, "sharpAttribute", "normal.sharp");
        FindBooleanAttribute(source, attributeName, RekallAgeGeometryDomain.Edge);
        var topology = source.Topology;
        var faceData = BuildNormalFaceData(topology);
        var facesByEdge = BuildFacesByEdge(topology);
        var threshold = Math.Cos(angleDegrees * Math.PI / 180.0);
        var values = facesByEdge.Select(faces =>
        {
            var sharp = faces.Count != 2;
            if (!sharp)
            {
                var dot = Math.Clamp(Dot(faceData[faces[0]].Normal, faceData[faces[1]].Normal), -1, 1);
                sharp = dot < threshold - 1e-12;
            }
            return JsonSerializer.SerializeToElement(sharp);
        }).ToArray();
        var attribute = new RekallAgeGeometryAttribute(
            attributeName,
            RekallAgeGeometryDomain.Edge,
            RekallAgeGeometryValueType.Bool,
            values,
            "normal-sharp",
            RekallAgeGeometryInterpolation.Nearest,
            JsonSerializer.SerializeToElement(false));
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Attributes = source.Attributes
                .Where(item => item.Name != attributeName)
                .Append(attribute)
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ToArray()
        };
        return Result(
            source,
            mesh,
            ChangeSet(
                RekallAgeMeshChangeKind.Attributes,
                modifiedEdges: topology.EdgeIds,
                changedAttributes: [attributeName],
                affectedBounds: Bounds(topology.Positions)),
            topology.EdgeIds.Select(id => Preserve(RekallAgeGeometryDomain.Edge, id)).ToArray());
    }

    private static RekallAgeMeshOperationResult WeightedNormals(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireCompleteFaceSelection(source, request, "Weighted normals");
        var name = ReadBoundedString(request.Parameters, "attribute", "normal.authored");
        var faceAreaExponent = ReadFiniteDouble(request.Parameters, "faceAreaWeight", 1);
        var cornerAngleExponent = ReadFiniteDouble(request.Parameters, "cornerAngleWeight", 1);
        if (faceAreaExponent is < 0 or > 4 || cornerAngleExponent is < 0 or > 4)
        {
            throw Failure(
                "REKALL_MESH_OPERATION_PARAMETER_INVALID",
                "faceAreaWeight and cornerAngleWeight must be between 0 and 4.");
        }

        var smoothName = ReadBoundedString(request.Parameters, "smoothAttribute", "normal.smooth");
        var sharpName = ReadBoundedString(request.Parameters, "sharpAttribute", "normal.sharp");
        var existingOutput = source.Attributes.FirstOrDefault(item => item.Name == name);
        if (existingOutput is not null
            && (existingOutput.Domain != RekallAgeGeometryDomain.Corner
                || existingOutput.ValueType != RekallAgeGeometryValueType.Float3))
        {
            throw Failure(
                "REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT",
                $"Attribute '{name}' exists with an incompatible domain or type.");
        }

        var smoothAttribute = FindBooleanAttribute(
            source,
            smoothName,
            RekallAgeGeometryDomain.Face);
        var sharpAttribute = FindBooleanAttribute(
            source,
            sharpName,
            RekallAgeGeometryDomain.Edge);
        var topology = source.Topology;
        var faceData = BuildNormalFaceData(topology);
        var smoothFaces = Enumerable.Range(0, topology.FaceIds.Count)
            .Select(index => smoothAttribute?.Values[index].GetBoolean() ?? true)
            .ToArray();
        var sharpEdges = Enumerable.Range(0, topology.EdgeIds.Count)
            .Select(index => sharpAttribute?.Values[index].GetBoolean() ?? false)
            .ToArray();
        var cornerFaces = BuildCornerFaces(topology);
        var cornerAngles = BuildCornerAngles(topology);
        var facesByEdge = BuildFacesByEdge(topology);
        var cornerByFacePoint = Enumerable.Range(0, topology.CornerIds.Count)
            .ToDictionary(
                corner => (Face: cornerFaces[corner], Point: topology.CornerPointIndices[corner]),
                corner => corner);
        var adjacency = Enumerable.Range(0, topology.CornerIds.Count)
            .Select(_ => new List<int>())
            .ToArray();
        for (var edge = 0; edge < topology.EdgeIds.Count; edge++)
        {
            var incidentFaces = facesByEdge[edge];
            if (incidentFaces.Count != 2
                || sharpEdges[edge]
                || !smoothFaces[incidentFaces[0]]
                || !smoothFaces[incidentFaces[1]])
            {
                continue;
            }

            var endpoints = topology.EdgePointIndices[edge];
            foreach (var point in new[] { endpoints.A, endpoints.B })
            {
                if (!cornerByFacePoint.TryGetValue((incidentFaces[0], point), out var first)
                    || !cornerByFacePoint.TryGetValue((incidentFaces[1], point), out var second))
                {
                    throw Failure(
                        "REKALL_MESH_NORMAL_TOPOLOGY_INCONSISTENT",
                        $"Edge '{topology.EdgeIds[edge]}' cannot connect its incident corner normals.");
                }
                adjacency[first].Add(second);
                adjacency[second].Add(first);
            }
        }

        var values = new JsonElement[topology.CornerIds.Count];
        var visited = new bool[topology.CornerIds.Count];
        var cornersByPoint = Enumerable.Range(0, topology.CornerIds.Count)
            .GroupBy(corner => topology.CornerPointIndices[corner])
            .OrderBy(group => topology.PointIds[group.Key]);
        foreach (var pointCorners in cornersByPoint)
        {
            foreach (var seed in pointCorners.OrderBy(corner => topology.CornerIds[corner]))
            {
                if (visited[seed])
                {
                    continue;
                }

                var seedFace = cornerFaces[seed];
                if (!smoothFaces[seedFace])
                {
                    visited[seed] = true;
                    values[seed] = SerializeNormal(faceData[seedFace].Normal);
                    continue;
                }

                var component = new List<int>();
                var pending = new Stack<int>();
                pending.Push(seed);
                visited[seed] = true;
                while (pending.Count > 0)
                {
                    var corner = pending.Pop();
                    component.Add(corner);
                    foreach (var neighbor in adjacency[corner]
                        .OrderByDescending(index => topology.CornerIds[index]))
                    {
                        if (!visited[neighbor])
                        {
                            visited[neighbor] = true;
                            pending.Push(neighbor);
                        }
                    }
                }

                var sum = new RekallAgeGeometryVector3(0, 0, 0);
                foreach (var corner in component)
                {
                    var face = cornerFaces[corner];
                    var weight = Math.Pow(faceData[face].Area, faceAreaExponent)
                        * Math.Pow(cornerAngles[corner], cornerAngleExponent);
                    sum = NormalAdd(sum, NormalScale(faceData[face].Normal, weight));
                }
                var length = Math.Sqrt(Dot(sum, sum));
                if (!double.IsFinite(length) || length <= 1e-12)
                {
                    throw Failure(
                        "REKALL_MESH_NORMAL_VERTEX_DEGENERATE",
                        $"Point '{topology.PointIds[pointCorners.Key]}' has a cancelling or non-finite smooth normal fan.");
                }
                var normal = NormalScale(sum, 1.0 / length);
                var encoded = SerializeNormal(normal);
                foreach (var corner in component)
                {
                    values[corner] = encoded;
                }
            }
        }

        var attribute = new RekallAgeGeometryAttribute(
            name,
            RekallAgeGeometryDomain.Corner,
            RekallAgeGeometryValueType.Float3,
            values,
            "normal",
            RekallAgeGeometryInterpolation.NormalizedLinear);
        var attributes = source.Attributes
            .Where(item => !item.Name.Equals(name, StringComparison.Ordinal))
            .Append(attribute)
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var mesh = source with { Revision = checked(source.Revision + 1), Attributes = attributes };
        return Result(
            source,
            mesh,
            ChangeSet(
                RekallAgeMeshChangeKind.Attributes,
                modifiedCorners: topology.CornerIds,
                changedAttributes: [name],
                affectedBounds: Bounds(topology.Positions)),
            []);
    }

    private static void RequireCompleteFaceSelection(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request,
        string operationName)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        if (!request.ElementIds.Order().SequenceEqual(source.Topology.FaceIds.Order()))
        {
            throw Failure(
                "REKALL_MESH_NORMAL_PARTIAL_UNSUPPORTED",
                $"{operationName} currently requires the complete face selection.");
        }
    }

    private static RekallAgeGeometryAttribute? FindBooleanAttribute(
        RekallAgeMeshAsset source,
        string name,
        RekallAgeGeometryDomain domain)
    {
        var attribute = source.Attributes.FirstOrDefault(item => item.Name == name);
        if (attribute is null)
        {
            return null;
        }
        if (attribute.Domain != domain || attribute.ValueType != RekallAgeGeometryValueType.Bool)
        {
            throw Failure(
                "REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT",
                $"Attribute '{name}' exists with an incompatible domain or type.");
        }
        return attribute;
    }

    private static IReadOnlyList<List<int>> BuildFacesByEdge(RekallAgeMeshTopology topology)
    {
        var facesByEdge = Enumerable.Range(0, topology.EdgeIds.Count)
            .Select(_ => new List<int>())
            .ToArray();
        for (var face = 0; face < topology.FaceIds.Count; face++)
        {
            foreach (var corner in FaceCornerSourceIndices(face, topology))
            {
                var edge = topology.CornerEdgeIndices[corner];
                if (!facesByEdge[edge].Contains(face))
                {
                    facesByEdge[edge].Add(face);
                }
            }
        }
        foreach (var faces in facesByEdge)
        {
            faces.Sort();
        }
        return facesByEdge;
    }

    private static int[] BuildCornerFaces(RekallAgeMeshTopology topology)
    {
        var result = new int[topology.CornerIds.Count];
        for (var face = 0; face < topology.FaceIds.Count; face++)
        {
            for (var corner = topology.FaceOffsets[face]; corner < topology.FaceOffsets[face + 1]; corner++)
            {
                result[corner] = face;
            }
        }
        return result;
    }

    private static NormalFaceData[] BuildNormalFaceData(RekallAgeMeshTopology topology)
    {
        var result = new NormalFaceData[topology.FaceIds.Count];
        for (var face = 0; face < topology.FaceIds.Count; face++)
        {
            var vector = new RekallAgeGeometryVector3(0, 0, 0);
            var start = topology.FaceOffsets[face];
            var end = topology.FaceOffsets[face + 1];
            for (var corner = start; corner < end; corner++)
            {
                var next = corner + 1 == end ? start : corner + 1;
                var a = topology.Positions[topology.CornerPointIndices[corner]];
                var b = topology.Positions[topology.CornerPointIndices[next]];
                vector = new(
                    vector.X + (a.Y - b.Y) * (a.Z + b.Z),
                    vector.Y + (a.Z - b.Z) * (a.X + b.X),
                    vector.Z + (a.X - b.X) * (a.Y + b.Y));
            }
            var length = Math.Sqrt(Dot(vector, vector));
            if (!double.IsFinite(length) || length <= 1e-12)
            {
                throw Failure(
                    "REKALL_MESH_NORMAL_FACE_DEGENERATE",
                    $"Face '{topology.FaceIds[face]}' has no finite normal.");
            }
            result[face] = new(NormalScale(vector, 1.0 / length), length * 0.5);
        }
        return result;
    }

    private static double[] BuildCornerAngles(RekallAgeMeshTopology topology)
    {
        var result = new double[topology.CornerIds.Count];
        for (var face = 0; face < topology.FaceIds.Count; face++)
        {
            var start = topology.FaceOffsets[face];
            var end = topology.FaceOffsets[face + 1];
            for (var corner = start; corner < end; corner++)
            {
                var previous = corner == start ? end - 1 : corner - 1;
                var next = corner + 1 == end ? start : corner + 1;
                var origin = topology.Positions[topology.CornerPointIndices[corner]];
                var before = Subtract(topology.Positions[topology.CornerPointIndices[previous]], origin);
                var after = Subtract(topology.Positions[topology.CornerPointIndices[next]], origin);
                var beforeLength = Math.Sqrt(Dot(before, before));
                var afterLength = Math.Sqrt(Dot(after, after));
                if (!double.IsFinite(beforeLength)
                    || !double.IsFinite(afterLength)
                    || beforeLength <= 1e-12
                    || afterLength <= 1e-12)
                {
                    throw Failure(
                        "REKALL_MESH_NORMAL_FACE_DEGENERATE",
                        $"Corner '{topology.CornerIds[corner]}' has a zero-length incident edge.");
                }
                var cosine = Math.Clamp(Dot(before, after) / (beforeLength * afterLength), -1, 1);
                result[corner] = Math.Acos(cosine);
            }
        }
        return result;
    }

    private static JsonElement SerializeNormal(RekallAgeGeometryVector3 normal) =>
        JsonSerializer.SerializeToElement(new[] { normal.X, normal.Y, normal.Z });

    private static RekallAgeGeometryVector3 NormalAdd(
        RekallAgeGeometryVector3 left,
        RekallAgeGeometryVector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static RekallAgeGeometryVector3 NormalScale(
        RekallAgeGeometryVector3 value,
        double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    private readonly record struct NormalFaceData(RekallAgeGeometryVector3 Normal, double Area);
}
