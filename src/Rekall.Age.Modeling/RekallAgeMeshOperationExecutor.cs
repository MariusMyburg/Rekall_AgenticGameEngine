using System.Text.Json.Nodes;
using System.Text.Json;
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
    private static readonly IReadOnlyList<RekallAgeMeshOperationDescriptor> OperationDescriptors =
    [
        new(
            "transform",
            "Translates selected mesh points by a finite XYZ offset without changing their stable IDs.",
            RekallAgeGeometryDomain.Point,
            RekallAgeMeshChangeKind.Positions,
            [NumberParameter("x"), NumberParameter("y"), NumberParameter("z")]),
        new(
            "reverse_faces",
            "Reverses selected face winding while preserving stable face/corner identity and corner attributes.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes,
            []),
        new(
            "triangulate_faces",
            "Triangulates selected polygon faces with derived diagonal edges and source-element provenance.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes,
            []),
        new(
            "extrude_faces",
            "Extrudes a selected face region by a finite XYZ offset and creates side faces only on its boundary.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
            [NumberParameter("x"), NumberParameter("y"), NumberParameter("z")]),
        new(
            "delete",
            "Deletes selected faces and their corners while preserving now-loose points and edges for explicit subsequent editing.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
            [])
    ];
    private readonly RekallAgeMeshValidator _validator = new();

    public IReadOnlyList<RekallAgeMeshOperationDescriptor> Descriptors => OperationDescriptors;

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
            "triangulate_faces" => TriangulateFaces(source, request),
            "extrude_faces" => ExtrudeFaces(source, request),
            "delete" => DeleteFaces(source, request),
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

    private RekallAgeMeshOperationResult TriangulateFaces(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        _ = ResolveIndices(source.Topology.FaceIds, request.ElementIds, "face");
        var selected = request.ElementIds.ToHashSet();
        var topology = source.Topology;
        var edgeIds = topology.EdgeIds.ToList();
        var edgePoints = topology.EdgePointIndices.ToList();
        var edgeSourceIndices = Enumerable.Range(0, edgeIds.Count).Select<int, int?>(index => index).ToList();
        var edgeLookup = edgePoints
            .Select((edge, index) => (Key: EdgeKey(edge.A, edge.B), Index: index))
            .ToDictionary(item => item.Key, item => item.Index);
        var faceIds = new List<ulong>();
        var faceOffsets = new List<int> { 0 };
        var faceSourceIndices = new List<int>();
        var cornerIds = new List<ulong>();
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var cornerSourceIndices = new List<int>();
        var createdEdgeIds = new List<ulong>();
        var createdFaceIds = new List<ulong>();
        var createdCornerIds = new List<ulong>();
        var modifiedFaceIds = new List<ulong>();
        var modifiedCornerIds = new HashSet<ulong>();
        var provenance = new List<RekallAgeMeshElementProvenance>();
        var affectedPointIndices = new HashSet<int>();
        var nextEdgeId = NextId(edgeIds);
        var nextFaceId = NextId(topology.FaceIds);
        var nextCornerId = NextId(topology.CornerIds);

        for (var faceIndex = 0; faceIndex < topology.FaceIds.Count; faceIndex++)
        {
            var faceId = topology.FaceIds[faceIndex];
            var start = topology.FaceOffsets[faceIndex];
            var end = topology.FaceOffsets[faceIndex + 1];
            var cornerCount = end - start;
            if (!selected.Contains(faceId) || cornerCount == 3)
            {
                faceIds.Add(faceId);
                faceSourceIndices.Add(faceIndex);
                for (var cornerIndex = start; cornerIndex < end; cornerIndex++)
                {
                    cornerIds.Add(topology.CornerIds[cornerIndex]);
                    cornerPoints.Add(topology.CornerPointIndices[cornerIndex]);
                    cornerEdges.Add(topology.CornerEdgeIndices[cornerIndex]);
                    cornerSourceIndices.Add(cornerIndex);
                }
                faceOffsets.Add(cornerIds.Count);
                if (selected.Contains(faceId))
                {
                    provenance.Add(Preserve(RekallAgeGeometryDomain.Face, faceId));
                }
                continue;
            }

            var originalCorners = Enumerable.Range(start, cornerCount).ToArray();
            var usedOriginalCorners = new HashSet<int>();
            var outputFaceIds = new List<ulong>();
            modifiedFaceIds.Add(faceId);
            foreach (var cornerIndex in originalCorners)
            {
                modifiedCornerIds.Add(topology.CornerIds[cornerIndex]);
                affectedPointIndices.Add(topology.CornerPointIndices[cornerIndex]);
            }

            for (var triangle = 1; triangle < cornerCount - 1; triangle++)
            {
                var triangleFaceId = triangle == 1 ? faceId : nextFaceId++;
                if (triangleFaceId != faceId)
                {
                    createdFaceIds.Add(triangleFaceId);
                }
                outputFaceIds.Add(triangleFaceId);
                faceIds.Add(triangleFaceId);
                faceSourceIndices.Add(faceIndex);
                var localCorners = new[] { 0, triangle, triangle + 1 };
                for (var triangleCorner = 0; triangleCorner < 3; triangleCorner++)
                {
                    var local = localCorners[triangleCorner];
                    var nextLocal = localCorners[(triangleCorner + 1) % 3];
                    var sourceCornerIndex = start + local;
                    var pointIndex = topology.CornerPointIndices[sourceCornerIndex];
                    var nextPointIndex = topology.CornerPointIndices[start + nextLocal];
                    var key = EdgeKey(pointIndex, nextPointIndex);
                    if (!edgeLookup.TryGetValue(key, out var edgeIndex))
                    {
                        edgeIndex = edgeIds.Count;
                        var edgeId = nextEdgeId++;
                        edgeLookup.Add(key, edgeIndex);
                        edgeIds.Add(edgeId);
                        edgePoints.Add(new(pointIndex, nextPointIndex));
                        edgeSourceIndices.Add(null);
                        createdEdgeIds.Add(edgeId);
                    }

                    var canReuse = topology.CornerEdgeIndices[sourceCornerIndex] == edgeIndex
                                   && usedOriginalCorners.Add(sourceCornerIndex);
                    var cornerId = canReuse ? topology.CornerIds[sourceCornerIndex] : nextCornerId++;
                    if (!canReuse)
                    {
                        createdCornerIds.Add(cornerId);
                    }
                    cornerIds.Add(cornerId);
                    cornerPoints.Add(pointIndex);
                    cornerEdges.Add(edgeIndex);
                    cornerSourceIndices.Add(sourceCornerIndex);
                }
                faceOffsets.Add(cornerIds.Count);
            }
            provenance.Add(new(
                RekallAgeGeometryDomain.Face,
                faceId,
                outputFaceIds));
        }

        var attributes = source.Attributes.Select(attribute => attribute.Domain switch
        {
            RekallAgeGeometryDomain.Edge => attribute with
            {
                Values = edgeSourceIndices.Select(index =>
                    index.HasValue ? attribute.Values[index.Value] : DefaultValue(attribute)).ToArray()
            },
            RekallAgeGeometryDomain.Face => attribute with
            {
                Values = faceSourceIndices.Select(index => attribute.Values[index]).ToArray()
            },
            RekallAgeGeometryDomain.Corner => attribute with
            {
                Values = cornerSourceIndices.Select(index => attribute.Values[index]).ToArray()
            },
            _ => attribute
        }).ToArray();
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Topology = topology with
            {
                EdgeIds = edgeIds,
                EdgePointIndices = edgePoints,
                FaceIds = faceIds,
                FaceOffsets = faceOffsets,
                CornerIds = cornerIds,
                CornerPointIndices = cornerPoints,
                CornerEdgeIndices = cornerEdges
            },
            Attributes = attributes
        };
        return Result(
            source,
            mesh,
            ChangeSet(
                RekallAgeMeshChangeKind.Topology | (attributes.Length > 0 ? RekallAgeMeshChangeKind.Attributes : RekallAgeMeshChangeKind.None),
                createdEdges: createdEdgeIds,
                createdFaces: createdFaceIds,
                createdCorners: createdCornerIds,
                modifiedFaces: modifiedFaceIds,
                modifiedCorners: modifiedCornerIds.Order().ToArray(),
                changedAttributes: attributes.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray(),
                affectedBounds: Bounds(affectedPointIndices.Select(index => topology.Positions[index]))),
            provenance);
    }

    private RekallAgeMeshOperationResult ExtrudeFaces(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        var selectedFaceIndices = ResolveIndices(source.Topology.FaceIds, request.ElementIds, "face").ToHashSet();
        var offset = new RekallAgeGeometryVector3(
            ReadFiniteDouble(request.Parameters, "x"),
            ReadFiniteDouble(request.Parameters, "y"),
            ReadFiniteDouble(request.Parameters, "z"));
        var topology = source.Topology;
        var selectedPointIndices = new HashSet<int>();
        var selectedEdgeUse = new Dictionary<int, int>();
        var boundaryCornerByEdge = new Dictionary<int, (int FaceIndex, int CornerIndex)>();
        foreach (var faceIndex in selectedFaceIndices)
        {
            var start = topology.FaceOffsets[faceIndex];
            var end = topology.FaceOffsets[faceIndex + 1];
            for (var cornerIndex = start; cornerIndex < end; cornerIndex++)
            {
                selectedPointIndices.Add(topology.CornerPointIndices[cornerIndex]);
                var edgeIndex = topology.CornerEdgeIndices[cornerIndex];
                selectedEdgeUse[edgeIndex] = selectedEdgeUse.GetValueOrDefault(edgeIndex) + 1;
                boundaryCornerByEdge.TryAdd(edgeIndex, (faceIndex, cornerIndex));
            }
        }

        var boundaryEdges = selectedEdgeUse
            .Where(pair => pair.Value == 1)
            .Select(pair => pair.Key)
            .OrderBy(index => topology.EdgeIds[index])
            .ToArray();
        var boundaryPoints = boundaryEdges
            .SelectMany(index =>
            {
                var edge = topology.EdgePointIndices[index];
                return new[] { edge.A, edge.B };
            })
            .Distinct()
            .OrderBy(index => topology.PointIds[index])
            .ToArray();

        var pointIds = topology.PointIds.ToList();
        var positions = topology.Positions.ToList();
        var pointSourceIndices = Enumerable.Range(0, pointIds.Count).ToList();
        var duplicatePointBySource = new Dictionary<int, int>();
        var createdPointIds = new List<ulong>();
        var nextPointId = NextId(pointIds);
        foreach (var sourcePointIndex in selectedPointIndices.OrderBy(index => topology.PointIds[index]))
        {
            var sourcePosition = topology.Positions[sourcePointIndex];
            var position = new RekallAgeGeometryVector3(
                sourcePosition.X + offset.X,
                sourcePosition.Y + offset.Y,
                sourcePosition.Z + offset.Z);
            if (!IsFinite(position.X) || !IsFinite(position.Y) || !IsFinite(position.Z))
            {
                throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Extrusion offset produces a non-finite position.");
            }
            var pointIndex = pointIds.Count;
            var pointId = nextPointId++;
            duplicatePointBySource.Add(sourcePointIndex, pointIndex);
            pointIds.Add(pointId);
            positions.Add(position);
            pointSourceIndices.Add(sourcePointIndex);
            createdPointIds.Add(pointId);
        }

        var edgeIds = topology.EdgeIds.ToList();
        var edgePoints = topology.EdgePointIndices.ToList();
        var edgeSourceIndices = Enumerable.Range(0, edgeIds.Count).Select<int, int?>(index => index).ToList();
        var topEdgeBySource = new Dictionary<int, int>();
        var verticalEdgeByPoint = new Dictionary<int, int>();
        var createdEdgeIds = new List<ulong>();
        var nextEdgeId = NextId(edgeIds);
        foreach (var sourceEdgeIndex in selectedEdgeUse.Keys.OrderBy(index => topology.EdgeIds[index]))
        {
            var sourceEdge = topology.EdgePointIndices[sourceEdgeIndex];
            var edgeIndex = edgeIds.Count;
            var edgeId = nextEdgeId++;
            topEdgeBySource.Add(sourceEdgeIndex, edgeIndex);
            edgeIds.Add(edgeId);
            edgePoints.Add(new(duplicatePointBySource[sourceEdge.A], duplicatePointBySource[sourceEdge.B]));
            edgeSourceIndices.Add(sourceEdgeIndex);
            createdEdgeIds.Add(edgeId);
        }
        foreach (var sourcePointIndex in boundaryPoints)
        {
            var edgeIndex = edgeIds.Count;
            var edgeId = nextEdgeId++;
            verticalEdgeByPoint.Add(sourcePointIndex, edgeIndex);
            edgeIds.Add(edgeId);
            edgePoints.Add(new(sourcePointIndex, duplicatePointBySource[sourcePointIndex]));
            edgeSourceIndices.Add(null);
            createdEdgeIds.Add(edgeId);
        }

        var faceIds = new List<ulong>();
        var faceOffsets = new List<int> { 0 };
        var faceSourceIndices = new List<int>();
        var cornerIds = new List<ulong>();
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var cornerSourceIndices = new List<int>();
        var createdFaceIds = new List<ulong>();
        var createdCornerIds = new List<ulong>();
        var nextFaceId = NextId(topology.FaceIds);
        var nextCornerId = NextId(topology.CornerIds);
        var faceProvenance = selectedFaceIndices.ToDictionary(
            index => topology.FaceIds[index],
            index => new List<ulong> { topology.FaceIds[index] });

        for (var faceIndex = 0; faceIndex < topology.FaceIds.Count; faceIndex++)
        {
            var selected = selectedFaceIndices.Contains(faceIndex);
            faceIds.Add(topology.FaceIds[faceIndex]);
            faceSourceIndices.Add(faceIndex);
            for (var cornerIndex = topology.FaceOffsets[faceIndex]; cornerIndex < topology.FaceOffsets[faceIndex + 1]; cornerIndex++)
            {
                cornerIds.Add(topology.CornerIds[cornerIndex]);
                cornerPoints.Add(selected
                    ? duplicatePointBySource[topology.CornerPointIndices[cornerIndex]]
                    : topology.CornerPointIndices[cornerIndex]);
                cornerEdges.Add(selected
                    ? topEdgeBySource[topology.CornerEdgeIndices[cornerIndex]]
                    : topology.CornerEdgeIndices[cornerIndex]);
                cornerSourceIndices.Add(cornerIndex);
            }
            faceOffsets.Add(cornerIds.Count);
        }

        foreach (var boundaryEdgeIndex in boundaryEdges)
        {
            var (sourceFaceIndex, sourceCornerIndex) = boundaryCornerByEdge[boundaryEdgeIndex];
            var sourceFaceStart = topology.FaceOffsets[sourceFaceIndex];
            var sourceFaceEnd = topology.FaceOffsets[sourceFaceIndex + 1];
            var nextSourceCornerIndex = sourceCornerIndex + 1 == sourceFaceEnd
                ? sourceFaceStart
                : sourceCornerIndex + 1;
            var firstPoint = topology.CornerPointIndices[sourceCornerIndex];
            var secondPoint = topology.CornerPointIndices[nextSourceCornerIndex];
            var sideFaceId = nextFaceId++;
            createdFaceIds.Add(sideFaceId);
            faceProvenance[topology.FaceIds[sourceFaceIndex]].Add(sideFaceId);
            faceIds.Add(sideFaceId);
            faceSourceIndices.Add(sourceFaceIndex);
            var sidePoints = new[]
            {
                firstPoint,
                secondPoint,
                duplicatePointBySource[secondPoint],
                duplicatePointBySource[firstPoint]
            };
            var sideEdges = new[]
            {
                boundaryEdgeIndex,
                verticalEdgeByPoint[secondPoint],
                topEdgeBySource[boundaryEdgeIndex],
                verticalEdgeByPoint[firstPoint]
            };
            var sideSources = new[]
            {
                sourceCornerIndex,
                nextSourceCornerIndex,
                nextSourceCornerIndex,
                sourceCornerIndex
            };
            for (var i = 0; i < 4; i++)
            {
                var cornerId = nextCornerId++;
                createdCornerIds.Add(cornerId);
                cornerIds.Add(cornerId);
                cornerPoints.Add(sidePoints[i]);
                cornerEdges.Add(sideEdges[i]);
                cornerSourceIndices.Add(sideSources[i]);
            }
            faceOffsets.Add(cornerIds.Count);
        }

        var attributes = source.Attributes.Select(attribute => attribute.Domain switch
        {
            RekallAgeGeometryDomain.Point => attribute with
            {
                Values = pointSourceIndices.Select(index => attribute.Values[index]).ToArray()
            },
            RekallAgeGeometryDomain.Edge => attribute with
            {
                Values = edgeSourceIndices.Select(index =>
                    index.HasValue ? attribute.Values[index.Value] : DefaultValue(attribute)).ToArray()
            },
            RekallAgeGeometryDomain.Face => attribute with
            {
                Values = faceSourceIndices.Select(index => attribute.Values[index]).ToArray()
            },
            RekallAgeGeometryDomain.Corner => attribute with
            {
                Values = cornerSourceIndices.Select(index => attribute.Values[index]).ToArray()
            },
            _ => attribute
        }).ToArray();
        var pointProvenance = selectedPointIndices
            .OrderBy(index => topology.PointIds[index])
            .Select(index => new RekallAgeMeshElementProvenance(
                RekallAgeGeometryDomain.Point,
                topology.PointIds[index],
                [topology.PointIds[index], pointIds[duplicatePointBySource[index]]]))
            .ToArray();
        var provenance = faceProvenance
            .OrderBy(pair => pair.Key)
            .Select(pair => new RekallAgeMeshElementProvenance(RekallAgeGeometryDomain.Face, pair.Key, pair.Value))
            .Concat(pointProvenance)
            .ToArray();
        var selectionSets = PropagateExtrusionSelections(source.SelectionSets, faceProvenance, pointProvenance);
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Topology = topology with
            {
                PointIds = pointIds,
                Positions = positions,
                EdgeIds = edgeIds,
                EdgePointIndices = edgePoints,
                FaceIds = faceIds,
                FaceOffsets = faceOffsets,
                CornerIds = cornerIds,
                CornerPointIndices = cornerPoints,
                CornerEdgeIndices = cornerEdges
            },
            Attributes = attributes,
            SelectionSets = selectionSets
        };
        var affectedPositions = selectedPointIndices
            .SelectMany(index => new[] { topology.Positions[index], positions[duplicatePointBySource[index]] });
        return Result(
            source,
            mesh,
            ChangeSet(
                RekallAgeMeshChangeKind.Topology
                | RekallAgeMeshChangeKind.Positions
                | (attributes.Length > 0 ? RekallAgeMeshChangeKind.Attributes : RekallAgeMeshChangeKind.None)
                | (selectionSets.Count > 0 ? RekallAgeMeshChangeKind.Selection : RekallAgeMeshChangeKind.None),
                createdPoints: createdPointIds,
                createdEdges: createdEdgeIds,
                createdFaces: createdFaceIds,
                createdCorners: createdCornerIds,
                modifiedFaces: request.ElementIds.Order().ToArray(),
                modifiedCorners: selectedFaceIndices
                    .SelectMany(index => Enumerable.Range(topology.FaceOffsets[index], topology.FaceOffsets[index + 1] - topology.FaceOffsets[index]))
                    .Select(index => topology.CornerIds[index])
                    .Order()
                    .ToArray(),
                changedAttributes: attributes.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray(),
                affectedBounds: Bounds(affectedPositions)),
            provenance);
    }

    private RekallAgeMeshOperationResult DeleteFaces(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        var selectedFaceIndices = ResolveIndices(source.Topology.FaceIds, request.ElementIds, "face").ToHashSet();
        var topology = source.Topology;
        var faceIds = new List<ulong>();
        var faceOffsets = new List<int> { 0 };
        var faceSourceIndices = new List<int>();
        var cornerIds = new List<ulong>();
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var cornerSourceIndices = new List<int>();
        var deletedFaceIds = new List<ulong>();
        var deletedCornerIds = new List<ulong>();
        var affectedPointIndices = new HashSet<int>();

        for (var faceIndex = 0; faceIndex < topology.FaceIds.Count; faceIndex++)
        {
            var start = topology.FaceOffsets[faceIndex];
            var end = topology.FaceOffsets[faceIndex + 1];
            if (selectedFaceIndices.Contains(faceIndex))
            {
                deletedFaceIds.Add(topology.FaceIds[faceIndex]);
                for (var cornerIndex = start; cornerIndex < end; cornerIndex++)
                {
                    deletedCornerIds.Add(topology.CornerIds[cornerIndex]);
                    affectedPointIndices.Add(topology.CornerPointIndices[cornerIndex]);
                }
                continue;
            }

            faceIds.Add(topology.FaceIds[faceIndex]);
            faceSourceIndices.Add(faceIndex);
            for (var cornerIndex = start; cornerIndex < end; cornerIndex++)
            {
                cornerIds.Add(topology.CornerIds[cornerIndex]);
                cornerPoints.Add(topology.CornerPointIndices[cornerIndex]);
                cornerEdges.Add(topology.CornerEdgeIndices[cornerIndex]);
                cornerSourceIndices.Add(cornerIndex);
            }
            faceOffsets.Add(cornerIds.Count);
        }

        var attributes = source.Attributes.Select(attribute => attribute.Domain switch
        {
            RekallAgeGeometryDomain.Face => attribute with
            {
                Values = faceSourceIndices.Select(index => attribute.Values[index]).ToArray()
            },
            RekallAgeGeometryDomain.Corner => attribute with
            {
                Values = cornerSourceIndices.Select(index => attribute.Values[index]).ToArray()
            },
            _ => attribute
        }).ToArray();
        var deletedFaceSet = deletedFaceIds.ToHashSet();
        var selections = source.SelectionSets.Select(selection =>
        {
            if (selection.Domain != RekallAgeGeometryDomain.Face)
            {
                return selection;
            }
            return selection with
            {
                ElementIds = selection.ElementIds.Where(id => !deletedFaceSet.Contains(id)).ToArray(),
                ActiveElementId = selection.ActiveElementId.HasValue && deletedFaceSet.Contains(selection.ActiveElementId.Value)
                    ? null
                    : selection.ActiveElementId,
                OrderedHistory = selection.OrderedHistory?
                    .Where(id => !deletedFaceSet.Contains(id))
                    .ToArray()
            };
        }).ToArray();
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Topology = topology with
            {
                FaceIds = faceIds,
                FaceOffsets = faceOffsets,
                CornerIds = cornerIds,
                CornerPointIndices = cornerPoints,
                CornerEdgeIndices = cornerEdges
            },
            Attributes = attributes,
            SelectionSets = selections
        };
        return Result(
            source,
            mesh,
            ChangeSet(
                RekallAgeMeshChangeKind.Topology
                | (attributes.Any(item => item.Domain is RekallAgeGeometryDomain.Face or RekallAgeGeometryDomain.Corner)
                    ? RekallAgeMeshChangeKind.Attributes
                    : RekallAgeMeshChangeKind.None)
                | (source.SelectionSets.Count > 0 ? RekallAgeMeshChangeKind.Selection : RekallAgeMeshChangeKind.None),
                deletedFaces: deletedFaceIds.Order().ToArray(),
                deletedCorners: deletedCornerIds.Order().ToArray(),
                changedAttributes: attributes
                    .Where(item => item.Domain is RekallAgeGeometryDomain.Face or RekallAgeGeometryDomain.Corner)
                    .Select(item => item.Name)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                affectedBounds: Bounds(affectedPointIndices.Select(index => topology.Positions[index]))),
            deletedFaceIds
                .Order()
                .Select(id => new RekallAgeMeshElementProvenance(RekallAgeGeometryDomain.Face, id, []))
                .ToArray());
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

    private static (int A, int B) EdgeKey(int first, int second) =>
        first < second ? (first, second) : (second, first);

    private static ulong NextId(IReadOnlyCollection<ulong> ids)
    {
        return ids.Count == 0 ? 1 : checked(ids.Max() + 1);
    }

    private static JsonElement DefaultValue(RekallAgeGeometryAttribute attribute)
    {
        if (attribute.DefaultValue.HasValue)
        {
            return attribute.DefaultValue.Value;
        }
        return attribute.ValueType switch
        {
            RekallAgeGeometryValueType.Bool => JsonSerializer.SerializeToElement(false),
            RekallAgeGeometryValueType.Int32 => JsonSerializer.SerializeToElement(0),
            RekallAgeGeometryValueType.Float => JsonSerializer.SerializeToElement(0.0),
            RekallAgeGeometryValueType.Float2 => JsonSerializer.SerializeToElement(new double[2]),
            RekallAgeGeometryValueType.Float3 => JsonSerializer.SerializeToElement(new double[3]),
            RekallAgeGeometryValueType.Float4 or RekallAgeGeometryValueType.ColorLinear or RekallAgeGeometryValueType.Quaternion => JsonSerializer.SerializeToElement(new double[4]),
            RekallAgeGeometryValueType.Matrix4x4 => JsonSerializer.SerializeToElement(new double[16]),
            RekallAgeGeometryValueType.String => JsonSerializer.SerializeToElement(string.Empty),
            _ => throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_DEFAULT_INVALID", $"Attribute '{attribute.Name}' has no default value.")
        };
    }

    private static RekallAgeMeshOperationParameterDescriptor NumberParameter(string name) =>
        new(
            name,
            RekallAgeGeometryValueType.Float,
            false,
            JsonSerializer.SerializeToElement(0.0),
            $"Finite {name.ToUpperInvariant()} offset in mesh-local units.");

    private static IReadOnlyList<RekallAgeMeshSelection> PropagateExtrusionSelections(
        IReadOnlyList<RekallAgeMeshSelection> selections,
        IReadOnlyDictionary<ulong, List<ulong>> faceProvenance,
        IReadOnlyList<RekallAgeMeshElementProvenance> pointProvenance)
    {
        var pointMap = pointProvenance.ToDictionary(item => item.InputElementId, item => item.OutputElementIds);
        return selections.Select(selection =>
        {
            IReadOnlyDictionary<ulong, IReadOnlyList<ulong>>? map = selection.Domain switch
            {
                RekallAgeGeometryDomain.Face => faceProvenance.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<ulong>)pair.Value),
                RekallAgeGeometryDomain.Point => pointMap,
                _ => null
            };
            if (map is null)
            {
                return selection;
            }
            return selection with
            {
                ElementIds = Expand(selection.ElementIds, map),
                OrderedHistory = selection.OrderedHistory is null
                    ? null
                    : Expand(selection.OrderedHistory, map)
            };
        }).ToArray();
    }

    private static IReadOnlyList<ulong> Expand(
        IReadOnlyList<ulong> source,
        IReadOnlyDictionary<ulong, IReadOnlyList<ulong>> map)
    {
        var result = new List<ulong>();
        var seen = new HashSet<ulong>();
        foreach (var id in source)
        {
            var outputs = map.TryGetValue(id, out var mapped) ? mapped : [id];
            foreach (var output in outputs)
            {
                if (seen.Add(output))
                {
                    result.Add(output);
                }
            }
        }
        return result;
    }

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
