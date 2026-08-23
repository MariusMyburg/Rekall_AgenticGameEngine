using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMeshOperationException : InvalidOperationException
{
    public RekallAgeMeshOperationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class RekallAgeMeshOperationExecutor
{
    private readonly RekallAgeMeshValidator _validator = new();

    public RekallAgeMeshOperationResult Execute(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        var inputValidation = _validator.Validate(source);
        if (!inputValidation.IsValid)
        {
            throw Failure(
                "REKALL_MESH_OPERATION_SOURCE_INVALID",
                "Mesh operation source is invalid: " + ErrorCodes(inputValidation));
        }

        if (request.ElementIds.Count == 0 || request.ElementIds.Distinct().Count() != request.ElementIds.Count)
        {
            throw Failure("REKALL_MESH_OPERATION_SELECTION_INVALID", "Mesh operation selection must contain unique stable element IDs.");
        }

        var result = request.OperationId switch
        {
            "transform" => Transform(source, request),
            "reverse_faces" => ReverseFaces(source, request),
            _ => throw Failure("REKALL_MESH_OPERATION_UNKNOWN", $"Unknown mesh operation '{request.OperationId}'.")
        };
        var outputValidation = _validator.Validate(result.Mesh);
        if (!outputValidation.IsValid)
        {
            throw Failure(
                "REKALL_MESH_OPERATION_OUTPUT_INVALID",
                "Mesh operation produced invalid geometry: " + ErrorCodes(outputValidation));
        }

        return result with { Validation = outputValidation };
    }

    private RekallAgeMeshOperationResult Transform(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Point);
        var pointIndices = ResolveIndices(
            source.Topology.PointIds,
            request.ElementIds,
            "point");
        var x = ReadFiniteDouble(request.Parameters, "x");
        var y = ReadFiniteDouble(request.Parameters, "y");
        var z = ReadFiniteDouble(request.Parameters, "z");
        var positions = source.Topology.Positions.ToArray();
        var affected = new List<RekallAgeGeometryVector3>(pointIndices.Count * 2);
        foreach (var index in pointIndices)
        {
            var before = positions[index];
            var after = new RekallAgeGeometryVector3(before.X + x, before.Y + y, before.Z + z);
            if (!IsFinite(after.X) || !IsFinite(after.Y) || !IsFinite(after.Z))
            {
                throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Transform parameters produce a non-finite position.");
            }
            affected.Add(before);
            affected.Add(after);
            positions[index] = after;
        }

        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Topology = source.Topology with { Positions = positions }
        };
        var ids = request.ElementIds.Order().ToArray();
        return Result(
            source,
            mesh,
            ChangeSet(
                RekallAgeMeshChangeKind.Positions,
                modifiedPoints: ids,
                affectedBounds: Bounds(affected)),
            ids.Select(id => Preserve(RekallAgeGeometryDomain.Point, id)).ToArray());
    }

    private RekallAgeMeshOperationResult ReverseFaces(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        var faceIndices = ResolveIndices(source.Topology.FaceIds, request.ElementIds, "face");
        var topology = source.Topology;
        var cornerIds = topology.CornerIds.ToArray();
        var cornerPoints = topology.CornerPointIndices.ToArray();
        var cornerEdges = topology.CornerEdgeIndices.ToArray();
        var permutation = Enumerable.Range(0, topology.CornerIds.Count).ToArray();
        var affectedPoints = new HashSet<int>();

        foreach (var faceIndex in faceIndices)
        {
            var start = topology.FaceOffsets[faceIndex];
            var end = topology.FaceOffsets[faceIndex + 1];
            var order = new List<int>(end - start) { start };
            for (var sourceCorner = end - 1; sourceCorner > start; sourceCorner--)
            {
                order.Add(sourceCorner);
            }

            for (var offset = 0; offset < order.Count; offset++)
            {
                var sourceCorner = order[offset];
                var destination = start + offset;
                var previousSourceCorner = sourceCorner == start ? end - 1 : sourceCorner - 1;
                cornerIds[destination] = topology.CornerIds[sourceCorner];
                cornerPoints[destination] = topology.CornerPointIndices[sourceCorner];
                cornerEdges[destination] = topology.CornerEdgeIndices[previousSourceCorner];
                permutation[destination] = sourceCorner;
                affectedPoints.Add(topology.CornerPointIndices[sourceCorner]);
            }
        }

        var attributes = source.Attributes.Select(attribute =>
        {
            if (attribute.Domain != RekallAgeGeometryDomain.Corner)
            {
                return attribute;
            }

            var values = permutation.Select(index => attribute.Values[index]).ToArray();
            return attribute with { Values = values };
        }).ToArray();
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Topology = topology with
            {
                CornerIds = cornerIds,
                CornerPointIndices = cornerPoints,
                CornerEdgeIndices = cornerEdges
            },
            Attributes = attributes
        };
        var faceIds = request.ElementIds.Order().ToArray();
        var affected = affectedPoints.Select(index => topology.Positions[index]);
        return Result(
            source,
            mesh,
            ChangeSet(
                RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes,
                modifiedFaces: faceIds,
                modifiedCorners: faceIndices
                    .SelectMany(index => Enumerable.Range(topology.FaceOffsets[index], topology.FaceOffsets[index + 1] - topology.FaceOffsets[index]))
                    .Select(index => topology.CornerIds[index])
                    .Order()
                    .ToArray(),
                changedAttributes: attributes
                    .Where(item => item.Domain == RekallAgeGeometryDomain.Corner)
                    .Select(item => item.Name)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                affectedBounds: Bounds(affected)),
            faceIds.Select(id => Preserve(RekallAgeGeometryDomain.Face, id)).ToArray());
    }

    private static RekallAgeMeshOperationResult Result(
        RekallAgeMeshAsset source,
        RekallAgeMeshAsset mesh,
        RekallAgeMeshChangeSet changes,
        IReadOnlyList<RekallAgeMeshElementProvenance> provenance)
    {
        var placeholder = new RekallAgeMeshValidationReport(
            false,
            new(0, 0, 0, 0, 0, 0, 0, new(new(0, 0, 0), new(0, 0, 0))),
            []);
        return new RekallAgeMeshOperationResult(
            mesh,
            source.Revision,
            mesh.Revision,
            changes,
            provenance,
            placeholder);
    }

    private static RekallAgeMeshChangeSet ChangeSet(
        RekallAgeMeshChangeKind kind,
        IReadOnlyList<ulong>? createdPoints = null,
        IReadOnlyList<ulong>? createdEdges = null,
        IReadOnlyList<ulong>? createdFaces = null,
        IReadOnlyList<ulong>? createdCorners = null,
        IReadOnlyList<ulong>? deletedPoints = null,
        IReadOnlyList<ulong>? deletedEdges = null,
        IReadOnlyList<ulong>? deletedFaces = null,
        IReadOnlyList<ulong>? deletedCorners = null,
        IReadOnlyList<ulong>? modifiedPoints = null,
        IReadOnlyList<ulong>? modifiedEdges = null,
        IReadOnlyList<ulong>? modifiedFaces = null,
        IReadOnlyList<ulong>? modifiedCorners = null,
        IReadOnlyList<string>? changedAttributes = null,
        RekallAgeMeshBounds? affectedBounds = null) =>
        new(
            kind,
            createdPoints ?? [],
            createdEdges ?? [],
            createdFaces ?? [],
            createdCorners ?? [],
            deletedPoints ?? [],
            deletedEdges ?? [],
            deletedFaces ?? [],
            deletedCorners ?? [],
            modifiedPoints ?? [],
            modifiedEdges ?? [],
            modifiedFaces ?? [],
            modifiedCorners ?? [],
            changedAttributes ?? [],
            affectedBounds ?? new(new(0, 0, 0), new(0, 0, 0)));

    private static RekallAgeMeshElementProvenance Preserve(RekallAgeGeometryDomain domain, ulong id) =>
        new(domain, id, [id]);

    private static IReadOnlyList<int> ResolveIndices(
        IReadOnlyList<ulong> availableIds,
        IReadOnlyList<ulong> requestedIds,
        string domain)
    {
        var indices = availableIds
            .Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index);
        var result = new List<int>(requestedIds.Count);
        foreach (var id in requestedIds)
        {
            if (!indices.TryGetValue(id, out var index))
            {
                throw Failure("REKALL_MESH_OPERATION_SELECTION_INVALID", $"Selected {domain} ID '{id}' does not exist.");
            }
            result.Add(index);
        }
        return result;
    }

    private static void RequireDomain(RekallAgeMeshOperationRequest request, RekallAgeGeometryDomain expected)
    {
        if (request.Domain != expected)
        {
            throw Failure("REKALL_MESH_OPERATION_DOMAIN_INVALID", $"Operation '{request.OperationId}' requires the {expected} domain.");
        }
    }

    private static double ReadFiniteDouble(JsonObject parameters, string name)
    {
        if (!parameters.TryGetPropertyValue(name, out var node) || node is null)
        {
            return 0;
        }
        if (node is not JsonValue value || !TryReadNumber(value, out var number) || !IsFinite(number))
        {
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", $"Parameter '{name}' must be a finite number.");
        }
        return number;
    }

    private static bool TryReadNumber(JsonValue value, out double number)
    {
        if (value.TryGetValue<double>(out number))
        {
            return true;
        }
        if (value.TryGetValue<int>(out var intValue))
        {
            number = intValue;
            return true;
        }
        if (value.TryGetValue<long>(out var longValue))
        {
            number = longValue;
            return true;
        }
        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            number = (double)decimalValue;
            return true;
        }
        number = 0;
        return false;
    }

    private static RekallAgeMeshBounds Bounds(IEnumerable<RekallAgeGeometryVector3> values)
    {
        var points = values.ToArray();
        if (points.Length == 0)
        {
            return new(new(0, 0, 0), new(0, 0, 0));
        }
        return new(
            new(points.Min(item => item.X), points.Min(item => item.Y), points.Min(item => item.Z)),
            new(points.Max(item => item.X), points.Max(item => item.Y), points.Max(item => item.Z)));
    }

    private static string ErrorCodes(RekallAgeMeshValidationReport report) =>
        string.Join(", ", report.Diagnostics
            .Where(item => item.Severity == RekallAgeMeshDiagnosticSeverity.Error)
            .Select(item => item.Code)
            .Distinct(StringComparer.Ordinal));

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static RekallAgeMeshOperationException Failure(string code, string message) => new(code, message);
}
