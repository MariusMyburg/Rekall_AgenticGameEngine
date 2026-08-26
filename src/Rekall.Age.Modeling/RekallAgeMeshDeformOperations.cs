using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private static RekallAgeMeshOperationResult BendPoints(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Point);
        var pointIndices = ResolveIndices(source.Topology.PointIds, request.ElementIds, "point");
        var axis = ReadBoundedString(request.Parameters, "axis", "y").ToLowerInvariant();
        var bendAxis = ReadBoundedString(request.Parameters, "bendAxis", "z").ToLowerInvariant();
        if (axis is not ("x" or "y" or "z") || bendAxis is not ("x" or "y" or "z") || axis == bendAxis)
            throw Failure("REKALL_MESH_BEND_AXES_INVALID", "Bend axis and bendAxis must be distinct x, y, or z axes.");
        var minimum = ReadFiniteDouble(request.Parameters, "minimum");
        var maximum = ReadFiniteDouble(request.Parameters, "maximum", 1);
        if (maximum <= minimum)
            throw Failure("REKALL_MESH_BEND_RANGE_INVALID", "Bend maximum must be greater than minimum.");
        var angleDegrees = ReadFiniteDouble(request.Parameters, "angleDegrees", 45);
        if (Math.Abs(angleDegrees) > 36_000)
            throw Failure("REKALL_MESH_BEND_ANGLE_INVALID", "Bend angle must be between -36000 and 36000 degrees.");
        var centers = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["x"] = ReadFiniteDouble(request.Parameters, "centerX"),
            ["y"] = ReadFiniteDouble(request.Parameters, "centerY"),
            ["z"] = ReadFiniteDouble(request.Parameters, "centerZ")
        };

        var positions = source.Topology.Positions.ToArray();
        var affected = new List<RekallAgeGeometryVector3>(pointIndices.Count * 2);
        var angle = angleDegrees * Math.PI / 180.0;
        foreach (var index in pointIndices)
        {
            var before = positions[index];
            var after = before;
            if (Math.Abs(angle) > 1e-12)
            {
                var axial = Coordinate(before, axis);
                var cross = Coordinate(before, bendAxis);
                var clamped = Math.Clamp(axial, minimum, maximum);
                var theta = angle * ((clamped - minimum) / (maximum - minimum));
                var radius = (maximum - minimum) / angle;
                var crossOffset = cross - centers[bendAxis];
                var radial = radius - crossOffset;
                var residual = axial - clamped;
                var bentAxial = minimum + Math.Sin(theta) * radial + Math.Cos(theta) * residual;
                var bentCross = centers[bendAxis] + radius - Math.Cos(theta) * radial + Math.Sin(theta) * residual;
                after = WithCoordinate(WithCoordinate(before, axis, bentAxial), bendAxis, bentCross);
            }
            if (!IsFinite(after.X) || !IsFinite(after.Y) || !IsFinite(after.Z))
                throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Bend parameters produce a non-finite position.");
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
        return Result(source, mesh,
            ChangeSet(RekallAgeMeshChangeKind.Positions, modifiedPoints: ids, affectedBounds: Bounds(affected)),
            ids.Select(id => Preserve(RekallAgeGeometryDomain.Point, id)).ToArray());

        static double Coordinate(RekallAgeGeometryVector3 point, string coordinateAxis) => coordinateAxis switch
        {
            "x" => point.X,
            "y" => point.Y,
            _ => point.Z
        };

        static RekallAgeGeometryVector3 WithCoordinate(RekallAgeGeometryVector3 point, string coordinateAxis, double value) =>
            coordinateAxis switch
            {
                "x" => point with { X = value },
                "y" => point with { Y = value },
                _ => point with { Z = value }
            };
    }

    private static RekallAgeMeshOperationResult TaperPoints(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Point);
        var pointIndices = ResolveIndices(source.Topology.PointIds, request.ElementIds, "point");
        var axis = ReadBoundedString(request.Parameters, "axis", "y").ToLowerInvariant();
        if (axis is not ("x" or "y" or "z"))
            throw Failure("REKALL_MESH_TAPER_AXIS_INVALID", "Taper axis must be x, y, or z.");
        var minimum = ReadFiniteDouble(request.Parameters, "minimum");
        var maximum = ReadFiniteDouble(request.Parameters, "maximum", 1);
        if (maximum <= minimum)
            throw Failure("REKALL_MESH_TAPER_RANGE_INVALID", "Taper maximum must be greater than minimum.");
        var startScale = ReadFiniteDouble(request.Parameters, "startScale", 1);
        var endScale = ReadFiniteDouble(request.Parameters, "endScale", 0.5);
        if (startScale <= 0 || endScale <= 0)
            throw Failure("REKALL_MESH_TAPER_SCALE_INVALID", "Taper endpoint scales must be positive.");
        var centerX = ReadFiniteDouble(request.Parameters, "centerX");
        var centerY = ReadFiniteDouble(request.Parameters, "centerY");
        var centerZ = ReadFiniteDouble(request.Parameters, "centerZ");

        var positions = source.Topology.Positions.ToArray();
        var affected = new List<RekallAgeGeometryVector3>(pointIndices.Count * 2);
        foreach (var index in pointIndices)
        {
            var before = positions[index];
            var coordinate = axis switch { "x" => before.X, "y" => before.Y, _ => before.Z };
            var t = Math.Clamp((coordinate - minimum) / (maximum - minimum), 0, 1);
            var scale = startScale + (endScale - startScale) * t;
            var after = axis switch
            {
                "x" => before with
                {
                    Y = centerY + (before.Y - centerY) * scale,
                    Z = centerZ + (before.Z - centerZ) * scale
                },
                "y" => before with
                {
                    X = centerX + (before.X - centerX) * scale,
                    Z = centerZ + (before.Z - centerZ) * scale
                },
                _ => before with
                {
                    X = centerX + (before.X - centerX) * scale,
                    Y = centerY + (before.Y - centerY) * scale
                }
            };
            if (!IsFinite(after.X) || !IsFinite(after.Y) || !IsFinite(after.Z))
                throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Taper parameters produce a non-finite position.");
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
        return Result(source, mesh,
            ChangeSet(RekallAgeMeshChangeKind.Positions, modifiedPoints: ids, affectedBounds: Bounds(affected)),
            ids.Select(id => Preserve(RekallAgeGeometryDomain.Point, id)).ToArray());
    }
}
