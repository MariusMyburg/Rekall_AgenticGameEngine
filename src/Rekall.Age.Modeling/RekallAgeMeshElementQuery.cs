using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMeshQueryException : InvalidOperationException
{
    public RekallAgeMeshQueryException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class RekallAgeMeshElementQuery
{
    public const int MaximumResults = 4096;

    public RekallAgeMeshElementQueryResult Resolve(
        RekallAgeMeshAsset mesh,
        RekallAgeMeshElementSelector selector,
        int maximumResults)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(selector);
        if (maximumResults < 1 || maximumResults > MaximumResults)
        {
            throw Failure("REKALL_MESH_QUERY_LIMIT_INVALID", $"Mesh query result limit must be between 1 and {MaximumResults}.");
        }

        var validation = new RekallAgeMeshValidator().Validate(mesh);
        if (!validation.IsValid)
        {
            throw Failure("REKALL_MESH_QUERY_SOURCE_INVALID", "Mesh query source must pass strict validation.");
        }

        var domainIds = DomainIds(mesh, selector.Domain);
        var validIds = domainIds.ToHashSet();
        var matches = validIds.ToHashSet();
        if (selector.ExplicitElementIds is { Count: > 0 })
        {
            EnsureIdsExist(selector.ExplicitElementIds, validIds);
            matches.IntersectWith(selector.ExplicitElementIds);
        }

        if (!string.IsNullOrWhiteSpace(selector.SelectionSetName))
        {
            var selection = mesh.SelectionSets.SingleOrDefault(item =>
                string.Equals(item.Name, selector.SelectionSetName, StringComparison.Ordinal));
            if (selection is null)
            {
                throw Failure("REKALL_MESH_QUERY_SELECTION_MISSING", $"Selection set '{selector.SelectionSetName}' does not exist.");
            }
            if (selection.Domain != selector.Domain)
            {
                throw Failure("REKALL_MESH_QUERY_SELECTION_DOMAIN_INVALID", $"Selection set '{selection.Name}' uses {selection.Domain}, not {selector.Domain}.");
            }
            matches.IntersectWith(selection.ElementIds);
        }

        if (selector.ConnectivitySeedIds is { Count: > 0 })
        {
            EnsureIdsExist(selector.ConnectivitySeedIds, validIds);
            var connected = Connected(mesh, selector.Domain, selector.ConnectivitySeedIds);
            if (selector.IncludeConnectivitySeeds)
            {
                connected.UnionWith(selector.ConnectivitySeedIds);
            }
            matches.IntersectWith(connected);
        }

        if (selector.WithinBounds is not null)
        {
            ValidateBounds(selector.WithinBounds);
            matches.IntersectWith(domainIds.Where(id =>
                Contains(selector.WithinBounds, PositionFor(mesh, selector.Domain, id))));
        }

        if (selector.AttributePredicate is not null)
        {
            var attribute = mesh.Attributes.SingleOrDefault(item =>
                string.Equals(item.Name, selector.AttributePredicate.AttributeName, StringComparison.Ordinal));
            if (attribute is null)
            {
                throw Failure("REKALL_MESH_QUERY_ATTRIBUTE_MISSING", $"Attribute '{selector.AttributePredicate.AttributeName}' does not exist.");
            }
            if (attribute.Domain != selector.Domain)
            {
                throw Failure("REKALL_MESH_QUERY_ATTRIBUTE_DOMAIN_INVALID", $"Attribute '{attribute.Name}' uses {attribute.Domain}, not {selector.Domain}.");
            }
            var attributeMatches = domainIds
                .Select((id, index) => (id, index))
                .Where(item => JsonElement.DeepEquals(attribute.Values[item.index], selector.AttributePredicate.EqualsValue))
                .Select(item => item.id);
            matches.IntersectWith(attributeMatches);
        }

        var ordered = matches.Order().ToArray();
        return new RekallAgeMeshElementQueryResult(
            selector.Domain,
            ordered.Take(maximumResults).ToArray(),
            ordered.Length,
            domainIds.Count,
            ordered.Length > maximumResults);
    }

    private static IReadOnlyList<ulong> DomainIds(RekallAgeMeshAsset mesh, RekallAgeGeometryDomain domain) =>
        domain switch
        {
            RekallAgeGeometryDomain.Point => mesh.Topology.PointIds,
            RekallAgeGeometryDomain.Edge => mesh.Topology.EdgeIds,
            RekallAgeGeometryDomain.Face => mesh.Topology.FaceIds,
            RekallAgeGeometryDomain.Corner => mesh.Topology.CornerIds,
            _ => throw Failure("REKALL_MESH_QUERY_DOMAIN_UNSUPPORTED", $"Mesh queries do not support the {domain} domain yet.")
        };

    private static HashSet<ulong> Connected(
        RekallAgeMeshAsset mesh,
        RekallAgeGeometryDomain domain,
        IReadOnlyList<ulong> seeds)
    {
        var adjacency = RekallAgeMeshAdjacency.Build(mesh);
        var seedSet = seeds.ToHashSet();
        var result = new HashSet<ulong>();
        switch (domain)
        {
            case RekallAgeGeometryDomain.Point:
                foreach (var seed in seeds)
                {
                    foreach (var edge in adjacency.EdgesForPoint(seed))
                    {
                        result.UnionWith(adjacency.PointsForEdge(edge));
                    }
                }
                break;
            case RekallAgeGeometryDomain.Edge:
                foreach (var seed in seeds)
                {
                    foreach (var point in adjacency.PointsForEdge(seed))
                    {
                        result.UnionWith(adjacency.EdgesForPoint(point));
                    }
                }
                break;
            case RekallAgeGeometryDomain.Face:
                foreach (var seed in seeds)
                {
                    result.UnionWith(adjacency.NeighborFaces(seed));
                }
                break;
            case RekallAgeGeometryDomain.Corner:
                var topology = mesh.Topology;
                var cornerIndexById = topology.CornerIds
                    .Select((id, index) => (id, index))
                    .ToDictionary(item => item.id, item => item.index);
                foreach (var seed in seeds)
                {
                    var index = cornerIndexById[seed];
                    var faceIndex = FaceForCorner(topology.FaceOffsets, index);
                    for (var corner = topology.FaceOffsets[faceIndex]; corner < topology.FaceOffsets[faceIndex + 1]; corner++)
                    {
                        result.Add(topology.CornerIds[corner]);
                    }
                    var pointIndex = topology.CornerPointIndices[index];
                    for (var corner = 0; corner < topology.CornerIds.Count; corner++)
                    {
                        if (topology.CornerPointIndices[corner] == pointIndex)
                        {
                            result.Add(topology.CornerIds[corner]);
                        }
                    }
                }
                break;
            default:
                throw Failure("REKALL_MESH_QUERY_DOMAIN_UNSUPPORTED", $"Connectivity does not support the {domain} domain.");
        }
        result.ExceptWith(seedSet);
        return result;
    }

    private static RekallAgeGeometryVector3 PositionFor(
        RekallAgeMeshAsset mesh,
        RekallAgeGeometryDomain domain,
        ulong id)
    {
        var topology = mesh.Topology;
        return domain switch
        {
            RekallAgeGeometryDomain.Point => topology.Positions[IndexOf(topology.PointIds, id)],
            RekallAgeGeometryDomain.Edge => EdgeMidpoint(topology, IndexOf(topology.EdgeIds, id)),
            RekallAgeGeometryDomain.Face => FaceCentroid(topology, IndexOf(topology.FaceIds, id)),
            RekallAgeGeometryDomain.Corner => topology.Positions[topology.CornerPointIndices[IndexOf(topology.CornerIds, id)]],
            _ => throw Failure("REKALL_MESH_QUERY_DOMAIN_UNSUPPORTED", $"Spatial queries do not support the {domain} domain.")
        };
    }

    private static RekallAgeGeometryVector3 EdgeMidpoint(RekallAgeMeshTopology topology, int edgeIndex)
    {
        var edge = topology.EdgePointIndices[edgeIndex];
        var first = topology.Positions[edge.A];
        var second = topology.Positions[edge.B];
        return new((first.X + second.X) / 2, (first.Y + second.Y) / 2, (first.Z + second.Z) / 2);
    }

    private static RekallAgeGeometryVector3 FaceCentroid(RekallAgeMeshTopology topology, int faceIndex)
    {
        var start = topology.FaceOffsets[faceIndex];
        var end = topology.FaceOffsets[faceIndex + 1];
        double x = 0;
        double y = 0;
        double z = 0;
        for (var corner = start; corner < end; corner++)
        {
            var point = topology.Positions[topology.CornerPointIndices[corner]];
            x += point.X;
            y += point.Y;
            z += point.Z;
        }
        var count = end - start;
        return new(x / count, y / count, z / count);
    }

    private static int FaceForCorner(IReadOnlyList<int> offsets, int cornerIndex)
    {
        for (var face = 0; face < offsets.Count - 1; face++)
        {
            if (cornerIndex >= offsets[face] && cornerIndex < offsets[face + 1])
            {
                return face;
            }
        }
        throw Failure("REKALL_MESH_QUERY_ELEMENT_INVALID", "Corner does not belong to a face.");
    }

    private static int IndexOf(IReadOnlyList<ulong> ids, ulong id)
    {
        for (var index = 0; index < ids.Count; index++)
        {
            if (ids[index] == id)
            {
                return index;
            }
        }
        throw Failure("REKALL_MESH_QUERY_ELEMENT_INVALID", $"Element '{id}' does not exist.");
    }

    private static void EnsureIdsExist(IEnumerable<ulong> ids, IReadOnlySet<ulong> validIds)
    {
        foreach (var id in ids)
        {
            if (!validIds.Contains(id))
            {
                throw Failure("REKALL_MESH_QUERY_ELEMENT_INVALID", $"Element '{id}' does not exist in the selected domain.");
            }
        }
    }

    private static void ValidateBounds(RekallAgeMeshBounds bounds)
    {
        if (!IsFinite(bounds.Min.X) || !IsFinite(bounds.Min.Y) || !IsFinite(bounds.Min.Z)
            || !IsFinite(bounds.Max.X) || !IsFinite(bounds.Max.Y) || !IsFinite(bounds.Max.Z)
            || bounds.Min.X > bounds.Max.X || bounds.Min.Y > bounds.Max.Y || bounds.Min.Z > bounds.Max.Z)
        {
            throw Failure("REKALL_MESH_QUERY_BOUNDS_INVALID", "Spatial query bounds must be finite and ordered.");
        }
    }

    private static bool Contains(RekallAgeMeshBounds bounds, RekallAgeGeometryVector3 point) =>
        point.X >= bounds.Min.X && point.X <= bounds.Max.X
        && point.Y >= bounds.Min.Y && point.Y <= bounds.Max.Y
        && point.Z >= bounds.Min.Z && point.Z <= bounds.Max.Z;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static RekallAgeMeshQueryException Failure(string code, string message) => new(code, message);
}
