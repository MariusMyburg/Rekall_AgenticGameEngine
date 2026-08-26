using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeModelingGraphEvaluator
{
    private static RekallAgeMeshAsset CreatePlane(RekallAgeModelingGraphAsset graph, RekallAgeModelingGraphNode node) =>
        CreateGrid(graph, node with { Parameters = new JsonObject { ["sizeX"] = ReadPositive(node, "sizeX", 1), ["sizeY"] = ReadPositive(node, "sizeY", 1), ["segmentsX"] = 1, ["segmentsY"] = 1 } });

    private static RekallAgeMeshAsset CreateCylinder(RekallAgeModelingGraphAsset graph, RekallAgeModelingGraphNode node) =>
        CreateFrustum(graph, node with { Parameters = new JsonObject { ["radiusBottom"] = ReadPositive(node, "radius", 0.5), ["radiusTop"] = ReadPositive(node, "radius", 0.5), ["depth"] = ReadPositive(node, "depth", 1), ["segments"] = ReadInteger(node, "segments", 16, 3, 4096), ["capBottom"] = ReadBoolean(node, "capBottom", true), ["capTop"] = ReadBoolean(node, "capTop", true) } });

    private static RekallAgeMeshAsset CreateCone(RekallAgeModelingGraphAsset graph, RekallAgeModelingGraphNode node) =>
        CreateFrustum(graph, node with { Parameters = new JsonObject { ["radiusBottom"] = ReadPositive(node, "radius", 0.5), ["radiusTop"] = 0.0, ["depth"] = ReadPositive(node, "depth", 1), ["segments"] = ReadInteger(node, "segments", 16, 3, 4096), ["capBottom"] = ReadBoolean(node, "capBottom", true), ["capTop"] = false } });

    private static RekallAgeMeshAsset CreateDisc(RekallAgeModelingGraphAsset graph, RekallAgeModelingGraphNode node)
    {
        var radius = ReadPositive(node, "radius", 0.5);
        var segments = ReadInteger(node, "segments", 16, 3, 4096);
        var positions = new List<RekallAgeGeometryVector3> { new(0, 0, 0) };
        for (var i = 0; i < segments; i++)
        {
            var angle = Math.PI * 2 * i / segments;
            positions.Add(new(radius * Math.Cos(angle), radius * Math.Sin(angle), 0));
        }
        var faces = Enumerable.Range(0, segments).Select(i => new[] { 0, 1 + i, 1 + (i + 1) % segments }).ToArray();
        return BuildMesh(graph, node, positions, faces);
    }

    private static RekallAgeMeshAsset CreateIcoSphere(RekallAgeModelingGraphAsset graph, RekallAgeModelingGraphNode node)
    {
        var radius = ReadPositive(node, "radius", 0.5);
        var subdivisions = ReadInteger(node, "subdivisions", 0, 0, 6);
        var phi = (1 + Math.Sqrt(5)) / 2;
        var vectors = new List<Vector3>
        {
            new(-1,(float)phi,0),new(1,(float)phi,0),new(-1,(float)-phi,0),new(1,(float)-phi,0),
            new(0,-1,(float)phi),new(0,1,(float)phi),new(0,-1,(float)-phi),new(0,1,(float)-phi),
            new((float)phi,0,-1),new((float)phi,0,1),new((float)-phi,0,-1),new((float)-phi,0,1)
        };
        var faces = new List<int[]>
        {
            new[]{0,11,5},new[]{0,5,1},new[]{0,1,7},new[]{0,7,10},new[]{0,10,11},new[]{1,5,9},new[]{5,11,4},new[]{11,10,2},new[]{10,7,6},new[]{7,1,8},
            new[]{3,9,4},new[]{3,4,2},new[]{3,2,6},new[]{3,6,8},new[]{3,8,9},new[]{4,9,5},new[]{2,4,11},new[]{6,2,10},new[]{8,6,7},new[]{9,8,1}
        };
        for (var level = 0; level < subdivisions; level++)
        {
            var cache = new Dictionary<(int, int), int>();
            var next = new List<int[]>(faces.Count * 4);
            foreach (var face in faces)
            {
                var a = Mid(face[0], face[1]); var b = Mid(face[1], face[2]); var c = Mid(face[2], face[0]);
                next.AddRange([new[] { face[0], a, c }, new[] { face[1], b, a }, new[] { face[2], c, b }, new[] { a, b, c }]);
            }
            faces = next;
            int Mid(int x, int y)
            {
                var key = x < y ? (x, y) : (y, x);
                if (cache.TryGetValue(key, out var found)) return found;
                var value = Vector3.Normalize(vectors[x] + vectors[y]);
                vectors.Add(value); return cache[key] = vectors.Count - 1;
            }
        }
        var positions = vectors.Select(value => { var n = Vector3.Normalize(value) * (float)radius; return new RekallAgeGeometryVector3(n.X, n.Y, n.Z); }).ToArray();
        return BuildMesh(graph, node, positions, faces);
    }

    private static RekallAgeMeshAsset CreateCapsule(RekallAgeModelingGraphAsset graph, RekallAgeModelingGraphNode node)
    {
        var radius = ReadPositive(node, "radius", 0.5);
        var depth = ReadPositive(node, "depth", 2);
        if (depth < radius * 2) throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Capsule depth must be at least its diameter.", node.NodeId);
        var segments = ReadInteger(node, "segments", 16, 3, 4096);
        var hemi = ReadInteger(node, "hemisphereRings", 4, 2, 1024);
        var halfBody = (depth - radius * 2) / 2;
        var positions = new List<RekallAgeGeometryVector3> { new(0, halfBody + radius, 0) };
        var ringData = new List<(double Radius, double Y)>();
        for (var ring = 1; ring < hemi; ring++) { var a = Math.PI * .5 * ring / hemi; ringData.Add((radius * Math.Sin(a), halfBody + radius * Math.Cos(a))); }
        ringData.Add((radius, halfBody)); ringData.Add((radius, -halfBody));
        for (var ring = hemi - 1; ring >= 1; ring--) { var a = Math.PI * .5 * ring / hemi; ringData.Add((radius * Math.Sin(a), -halfBody - radius * Math.Cos(a))); }
        foreach (var data in ringData)
        for (var segment = 0; segment < segments; segment++) { var a = Math.PI * 2 * segment / segments; positions.Add(new(data.Radius * Math.Cos(a), data.Y, data.Radius * Math.Sin(a))); }
        var bottom = positions.Count; positions.Add(new(0, -halfBody - radius, 0));
        var faces = new List<int[]>();
        for (var s = 0; s < segments; s++) faces.Add([0, Ring(0, (s + 1) % segments), Ring(0, s)]);
        for (var ring = 0; ring < ringData.Count - 1; ring++) for (var s = 0; s < segments; s++) faces.Add([Ring(ring, s), Ring(ring, (s + 1) % segments), Ring(ring + 1, (s + 1) % segments), Ring(ring + 1, s)]);
        var last = ringData.Count - 1;
        for (var s = 0; s < segments; s++) faces.Add([Ring(last, s), Ring(last, (s + 1) % segments), bottom]);
        return BuildMesh(graph, node, positions, faces);
        int Ring(int ring, int segment) => 1 + ring * segments + segment;
    }

    private static RekallAgeEvaluatedCurve CreateCurveSource(RekallAgeModelingGraphNode node)
    {
        if (node.Parameters["document"] is not JsonObject document)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Curve source requires a versioned object-valued 'document'.", node.NodeId);
        RekallAgeCurveAsset? curve;
        try { curve = document.Deserialize<RekallAgeCurveAsset>(RekallAgeModelingJson.Options); }
        catch (JsonException error) { throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", $"Curve document could not be read: {error.Message}", node.NodeId); }
        if (curve is null) throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Curve document is empty.", node.NodeId);
        try { return new RekallAgeCurveEvaluator().Evaluate(curve, ReadInteger(node, "resolution", 8, 1, 4096)); }
        catch (Exception error) when (error is InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new EvaluationException("REKALL_MODELING_EVALUATION_CURVE_INVALID", error.Message, node.NodeId);
        }
    }

    private static RekallAgeEvaluatedCurve CreateCurveLine(RekallAgeModelingGraphNode node)
    {
        try
        {
            return new RekallAgeCurveOperations().Line(
                ReadVector3(node, "start", new(0, 0, 0)),
                ReadVector3(node, "end", new(0, 1, 0)),
                ReadPositive(node, "startRadius", 1),
                ReadPositive(node, "endRadius", 1),
                ReadNumber(node, "startTilt", 0),
                ReadNumber(node, "endTilt", 0));
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        {
            throw new EvaluationException("REKALL_MODELING_EVALUATION_CURVE_INVALID", error.Message, node.NodeId);
        }
    }

    private static RekallAgeEvaluatedCurve CreateCurveCircle(RekallAgeModelingGraphNode node)
    {
        try
        {
            return new RekallAgeCurveOperations().Circle(
                ReadVector3(node, "center", new(0, 0, 0)),
                ReadPositive(node, "radius", 1),
                ReadInteger(node, "segments", 32, 3, 100_000),
                ReadString(node, "plane", "xy"));
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        {
            throw new EvaluationException("REKALL_MODELING_EVALUATION_CURVE_INVALID", error.Message, node.NodeId);
        }
    }

    private static RekallAgeEvaluatedCurve InputCurve(
        RekallAgeModelingGraphNode node,
        string portId,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, NodeValue> values)
    {
        var link = incoming.SingleOrDefault(item => item.ToPortId == portId)
            ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_MISSING", $"Input '{portId}' is missing.", node.NodeId);
        return values[link.FromNodeId].Curve
            ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", $"Input '{portId}' is not a curve.", node.NodeId);
    }

    private static IReadOnlyList<RekallAgeEvaluatedCurve> InputCurves(
        RekallAgeModelingGraphNode node,
        string portId,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, NodeValue> values)
    {
        var links = incoming.Where(item => item.ToPortId == portId).ToArray();
        if (links.Length == 0)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_MISSING", $"Input '{portId}' is missing.", node.NodeId);
        return links.Select(link => values[link.FromNodeId].Curve
            ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", $"Input '{portId}' is not a curve.", node.NodeId)).ToArray();
    }

    private static RekallAgeMeshAsset CreateProfileSweep(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, NodeValue> values)
    {
        var curveLink = incoming.SingleOrDefault(link => link.ToPortId == "curve");
        RekallAgeEvaluatedCurveSpline evaluatedSpline;
        if (curveLink is not null)
        {
            var curve = values[curveLink.FromNodeId].Curve
                ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", "Profile sweep curve input did not evaluate to a curve.", node.NodeId);
            if (curve.Splines.Count != 1)
                throw new EvaluationException("REKALL_MODELING_EVALUATION_CURVE_SPLINE_COUNT_UNSUPPORTED", "Profile sweep currently accepts exactly one evaluated spline per node; use one sweep node per spline and join the results.", node.NodeId);
            evaluatedSpline = curve.Splines[0];
        }
        else
        {
            var legacy = ReadPointArray(node, "pathPoints", minimum: 2);
            var points = legacy.Select((point, index) =>
            {
                var before = legacy[Math.Max(0, index - 1)]; var after = legacy[Math.Min(legacy.Count - 1, index + 1)];
                var delta = new RekallAgeGeometryVector3(after.X - before.X, after.Y - before.Y, after.Z - before.Z);
                var length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
                if (length <= 1e-12) throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Profile sweep path contains coincident spans.", node.NodeId);
                return new RekallAgeEvaluatedCurvePoint(point, new(delta.X / length, delta.Y / length, delta.Z / length), 1, 0, 0, (ulong)(index + 1), (ulong)(index + 1), 0);
            }).ToArray();
            evaluatedSpline = new(0, false, points);
        }
        var path = evaluatedSpline.Points;
        var profileKind = ReadString(node, "profile", "circle").ToLowerInvariant();
        var profileSegments = ReadInteger(node, "profileSegments", 8, profileKind == "rectangle" ? 4 : 3, 4096);
        var radius = ReadPositive(node, "radius", 0.25);
        var width = ReadPositive(node, "profileWidth", radius * 2);
        var height = ReadPositive(node, "profileHeight", radius * 2);
        var profile = profileKind switch
        {
            "circle" => Enumerable.Range(0, profileSegments).Select(i => new Vector2((float)(radius * Math.Cos(Math.PI * 2 * i / profileSegments)), (float)(radius * Math.Sin(Math.PI * 2 * i / profileSegments)))).ToArray(),
            "rectangle" => new[] { new Vector2((float)-width / 2, (float)-height / 2), new Vector2((float)width / 2, (float)-height / 2), new Vector2((float)width / 2, (float)height / 2), new Vector2((float)-width / 2, (float)height / 2) },
            _ => throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", $"Profile '{profileKind}' is unsupported.", node.NodeId)
        };
        profileSegments = profile.Length;
        var positions = new List<RekallAgeGeometryVector3>(path.Count * profileSegments);
        Vector3? previousNormal = null;
        for (var ring = 0; ring < path.Count; ring++)
        {
            var pathPoint = path[ring];
            var point = ToVector(pathPoint.Position);
            var tangent = ToVector(pathPoint.Tangent);
            if (!Finite(tangent)) throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Profile sweep path contains coincident spans.", node.NodeId);
            var normal = previousNormal is null ? InitialNormal(tangent) : Vector3.Normalize(previousNormal.Value - tangent * Vector3.Dot(previousNormal.Value, tangent));
            if (!Finite(normal)) normal = InitialNormal(tangent);
            var binormal = Vector3.Normalize(Vector3.Cross(tangent, normal));
            var cosine = (float)Math.Cos(pathPoint.TiltRadians); var sine = (float)Math.Sin(pathPoint.TiltRadians);
            var tiltedNormal = normal * cosine + binormal * sine;
            var tiltedBinormal = -normal * sine + binormal * cosine;
            previousNormal = normal;
            foreach (var sample in profile)
            {
                var value = point + tiltedNormal * sample.X * (float)pathPoint.Radius + tiltedBinormal * sample.Y * (float)pathPoint.Radius;
                positions.Add(new(value.X, value.Y, value.Z));
            }
        }
        var spanCount = evaluatedSpline.Cyclic ? path.Count : path.Count - 1;
        var faces = new List<int[]>(spanCount * profileSegments + 2);
        for (var ring = 0; ring < spanCount; ring++) for (var p = 0; p < profileSegments; p++) { var nextRing = (ring + 1) % path.Count; faces.Add([Point(ring, p), Point(nextRing, p), Point(nextRing, (p + 1) % profileSegments), Point(ring, (p + 1) % profileSegments)]); }
        if (!evaluatedSpline.Cyclic && ReadBoolean(node, "capStart", true)) faces.Add(Enumerable.Range(0, profileSegments).Reverse().Select(p => Point(0, p)).ToArray());
        if (!evaluatedSpline.Cyclic && ReadBoolean(node, "capEnd", true)) faces.Add(Enumerable.Range(0, profileSegments).Select(p => Point(path.Count - 1, p)).ToArray());
        var mesh = BuildMesh(graph, node, positions, faces);
        var uvValues = mesh.Topology.CornerPointIndices.Select(pointIndex =>
        {
            var ring = pointIndex / profileSegments; var profileIndex = pointIndex % profileSegments;
            return JsonSerializer.SerializeToElement(new[] { profileIndex / (double)profileSegments, ring / (double)Math.Max(1, path.Count - (evaluatedSpline.Cyclic ? 0 : 1)) });
        }).ToArray();
        var spans = path.SelectMany(point => Enumerable.Repeat(JsonSerializer.SerializeToElement($"{point.SourceSplineId}:{point.SourceStartControlPointId}:{point.SourceEndControlPointId}:{point.SegmentT:R}"), profileSegments)).ToArray();
        var materialId = ReadString(node, "materialAssetId", "material.default");
        var slotName = ReadString(node, "slotName", "Sweep");
        var materialValues = Enumerable.Range(0, mesh.Topology.FaceIds.Count).Select(_ => JsonSerializer.SerializeToElement(0)).ToArray();
        return mesh with { MaterialSlots = [new(slotName, materialId)], Attributes = [new("uv.generated", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2, uvValues, "texcoord-0"), new("curve.source.span", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.String, spans, "curve-source-span", RekallAgeGeometryInterpolation.Nearest), new("material.index", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.Int32, materialValues, "material-index", RekallAgeGeometryInterpolation.Nearest, JsonSerializer.SerializeToElement(0))] };
        int Point(int ring, int profileIndex) => ring * profileSegments + profileIndex;
    }

    private static RekallAgeMeshAsset CreateCurveRevolve(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        IReadOnlyList<RekallAgeModelingGraphLink> incoming,
        IReadOnlyDictionary<string, NodeValue> values)
    {
        var curve = InputCurve(node, "curve", incoming, values);
        if (curve.Splines.Count != 1)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_CURVE_SPLINE_COUNT_UNSUPPORTED", "Curve revolve accepts exactly one evaluated spline.", node.NodeId);
        var profile = curve.Splines[0];
        if (profile.Points.Count < 2)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_CURVE_PROFILE_INVALID", "Curve revolve requires at least two evaluated profile points.", node.NodeId);

        var axisName = ReadString(node, "axis", "y").ToLowerInvariant();
        var axis = axisName switch
        {
            "x" => Vector3.UnitX,
            "y" => Vector3.UnitY,
            "z" => Vector3.UnitZ,
            _ => throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", $"Curve revolve axis '{axisName}' is unsupported.", node.NodeId)
        };
        var origin = ToVector(ReadVector3(node, "origin", new(0, 0, 0)));
        var angleDegrees = ReadNumber(node, "angleDegrees", 360);
        if (!double.IsFinite(angleDegrees) || angleDegrees <= 0 || angleDegrees > 36_000)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Curve revolve angleDegrees must be finite, greater than zero, and at most 36000.", node.NodeId);
        var pitchPerTurn = ReadNumber(node, "pitchPerTurn", 0);
        if (!double.IsFinite(pitchPerTurn) || pitchPerTurn < -1_000_000 || pitchPerTurn > 1_000_000)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Curve revolve pitchPerTurn must be finite and from -1000000 through 1000000 world units.", node.NodeId);
        if (angleDegrees > 360 && pitchPerTurn == 0)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Curve revolve angleDegrees above 360 requires a nonzero pitchPerTurn to avoid overlapping revolutions.", node.NodeId);
        var segments = ReadInteger(node, "segments", 32, 3, 4096);
        var weldDistance = ReadNumber(node, "weldDistance", 0.000001);
        if (weldDistance < 0 || weldDistance > 1)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Curve revolve weldDistance must be from zero through one world unit.", node.NodeId);
        var pitched = pitchPerTurn != 0;
        var wraps = !pitched && Math.Abs(angleDegrees - 360) <= 1e-9;
        var ringCount = wraps ? segments : segments + 1;
        var profileSpanCount = profile.Cyclic ? profile.Points.Count : profile.Points.Count - 1;
        var maximumPointCount = checked((long)ringCount * profile.Points.Count);
        var maximumFaceCount = checked((long)segments * profileSpanCount);
        if (maximumPointCount > 2_000_000 || maximumFaceCount > 2_000_000)
            throw new EvaluationException("REKALL_MODELING_REVOLVE_OUTPUT_LIMIT_EXCEEDED", "Curve revolve would exceed the two-million-point or face output limit.", node.NodeId);

        var profileDistances = new double[profile.Points.Count];
        for (var profileIndex = 1; profileIndex < profile.Points.Count; profileIndex++)
            profileDistances[profileIndex] = profileDistances[profileIndex - 1] + Distance(profile.Points[profileIndex - 1].Position, profile.Points[profileIndex].Position);
        var totalProfileDistance = profileDistances[^1] + (profile.Cyclic ? Distance(profile.Points[^1].Position, profile.Points[0].Position) : 0);
        if (!double.IsFinite(totalProfileDistance) || totalProfileDistance <= 1e-12)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_CURVE_PROFILE_INVALID", "Curve revolve profile contains no finite nonzero spans.", node.NodeId);

        var positions = new List<RekallAgeGeometryVector3>((int)maximumPointCount);
        var pointProfileIndices = new List<int>((int)maximumPointCount);
        var pointAngles = new List<double>((int)maximumPointCount);
        var pointAxialOffsets = new List<double>((int)maximumPointCount);
        var pointLookup = new int[ringCount, profile.Points.Count];
        var weldedPointIndices = Enumerable.Repeat(-1, profile.Points.Count).ToArray();
        var onAxis = profile.Points.Select(point => RadialDistance(ToVector(point.Position), origin, axis) <= weldDistance).ToArray();
        for (var ring = 0; ring < ringCount; ring++)
        {
            var ringAngleDegrees = angleDegrees * ring / segments;
            var radians = (float)(Math.PI / 180 * ringAngleDegrees);
            var axialOffset = pitchPerTurn * ringAngleDegrees / 360;
            var rotation = Quaternion.CreateFromAxisAngle(axis, radians);
            for (var profileIndex = 0; profileIndex < profile.Points.Count; profileIndex++)
            {
                if (!pitched && onAxis[profileIndex] && weldedPointIndices[profileIndex] >= 0)
                {
                    pointLookup[ring, profileIndex] = weldedPointIndices[profileIndex];
                    continue;
                }
                var point = profile.Points[profileIndex];
                var rotated = origin
                    + Vector3.Transform(ToVector(point.Position) - origin, rotation)
                    + axis * (float)axialOffset;
                var pointIndex = positions.Count;
                positions.Add(new(rotated.X, rotated.Y, rotated.Z));
                pointProfileIndices.Add(profileIndex);
                pointAngles.Add(!pitched && onAxis[profileIndex] ? 0 : ringAngleDegrees);
                pointAxialOffsets.Add(axialOffset);
                pointLookup[ring, profileIndex] = pointIndex;
                if (!pitched && onAxis[profileIndex]) weldedPointIndices[profileIndex] = pointIndex;
            }
        }

        var faces = new List<int[]>(segments * profileSpanCount);
        var faceUvs = new List<RekallAgeGeometryVector2[]>(segments * profileSpanCount);
        for (var ring = 0; ring < segments; ring++)
        {
            var nextRing = wraps ? (ring + 1) % ringCount : ring + 1;
            for (var profileIndex = 0; profileIndex < profileSpanCount; profileIndex++)
            {
                var nextProfile = (profileIndex + 1) % profile.Points.Count;
                var rawPoints = new[]
                {
                    Point(ring, profileIndex), Point(nextRing, profileIndex),
                    Point(nextRing, nextProfile), Point(ring, nextProfile)
                };
                var currentU = ring / (double)segments;
                var nextU = (ring + 1) / (double)segments;
                var currentV = profileDistances[profileIndex] / totalProfileDistance;
                var nextV = profile.Cyclic && nextProfile == 0
                    ? 1
                    : profileDistances[nextProfile] / totalProfileDistance;
                var rawUvs = new[]
                {
                    new RekallAgeGeometryVector2(currentU, currentV),
                    new RekallAgeGeometryVector2(nextU, currentV),
                    new RekallAgeGeometryVector2(nextU, nextV),
                    new RekallAgeGeometryVector2(currentU, nextV)
                };
                var uniquePoints = new List<int>(4);
                var uniqueUvs = new List<RekallAgeGeometryVector2>(4);
                for (var corner = 0; corner < rawPoints.Length; corner++)
                {
                    if (uniquePoints.Contains(rawPoints[corner])) continue;
                    uniquePoints.Add(rawPoints[corner]);
                    uniqueUvs.Add(rawUvs[corner]);
                }
                if (uniquePoints.Count < 3) continue;
                faces.Add(uniquePoints.ToArray());
                faceUvs.Add(uniqueUvs.ToArray());
            }
        }

        var mesh = BuildMesh(graph, node, positions, faces);
        var uvValues = faceUvs.SelectMany(face => face)
            .Select(uv => JsonSerializer.SerializeToElement(new[] { uv.X, uv.Y })).ToArray();
        var provenanceValues = pointProfileIndices.Select(profileIndex =>
        {
            var point = profile.Points[profileIndex];
            return JsonSerializer.SerializeToElement($"{point.SourceSplineId}:{point.SourceStartControlPointId}:{point.SourceEndControlPointId}:{point.SegmentT:R}");
        }).ToArray();
        var angleValues = pointAngles.Select(value => JsonSerializer.SerializeToElement(value)).ToArray();
        var axialOffsetValues = pointAxialOffsets.Select(value => JsonSerializer.SerializeToElement(value)).ToArray();
        var materialValues = Enumerable.Range(0, mesh.Topology.FaceIds.Count)
            .Select(_ => JsonSerializer.SerializeToElement(0)).ToArray();
        var smoothValues = Enumerable.Range(0, mesh.Topology.FaceIds.Count)
            .Select(_ => JsonSerializer.SerializeToElement(true)).ToArray();
        return mesh with
        {
            MaterialSlots = [new(ReadString(node, "slotName", "Revolved Surface"), ReadString(node, "materialAssetId", "material.default"))],
            Attributes =
            [
                new("uv.generated", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2, uvValues, "texcoord-0"),
                new("curve.source.span", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.String, provenanceValues, "curve-source-span", RekallAgeGeometryInterpolation.Nearest),
                new("revolve.angle", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float, angleValues, "revolve-angle"),
                new("revolve.axial_offset", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float, axialOffsetValues, "revolve-axial-offset"),
                new("material.index", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.Int32, materialValues, "material-index", RekallAgeGeometryInterpolation.Nearest, JsonSerializer.SerializeToElement(0)),
                new("normal.smooth", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.Bool, smoothValues, "normal-smooth", RekallAgeGeometryInterpolation.Nearest, JsonSerializer.SerializeToElement(true))
            ]
        };

        int Point(int ring, int profileIndex) => pointLookup[ring, profileIndex];
        static double Distance(RekallAgeGeometryVector3 left, RekallAgeGeometryVector3 right)
        {
            var x = right.X - left.X; var y = right.Y - left.Y; var z = right.Z - left.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }
        static double RadialDistance(Vector3 point, Vector3 axisOrigin, Vector3 axisDirection)
        {
            var offset = point - axisOrigin;
            var radial = offset - axisDirection * Vector3.Dot(offset, axisDirection);
            return radial.Length();
        }
    }

    private static RekallAgeMeshAsset BuildMesh(RekallAgeModelingGraphAsset graph, RekallAgeModelingGraphNode node, IReadOnlyList<RekallAgeGeometryVector3> positions, IReadOnlyList<int[]> faces, bool createUvs = false)
    {
        var edgeMap = new Dictionary<(int, int), int>(); var edges = new List<RekallAgeMeshEdgePointIndices>(); var cornerPoints = new List<int>(); var cornerEdges = new List<int>(); var offsets = new List<int> { 0 };
        foreach (var face in faces)
        {
            for (var i = 0; i < face.Length; i++) { var a = face[i]; var b = face[(i + 1) % face.Length]; var key = a < b ? (a, b) : (b, a); if (!edgeMap.TryGetValue(key, out var edge)) { edge = edges.Count; edgeMap[key] = edge; edges.Add(new(a, b)); } cornerPoints.Add(a); cornerEdges.Add(edge); }
            offsets.Add(cornerPoints.Count);
        }
        var topology = new RekallAgeMeshTopology(Enumerable.Range(1, positions.Count).Select(i => (ulong)i).ToArray(), positions, Enumerable.Range(1, edges.Count).Select(i => (ulong)(10_000 + i)).ToArray(), edges, Enumerable.Range(1, faces.Count).Select(i => (ulong)(20_000 + i)).ToArray(), offsets, Enumerable.Range(1, cornerPoints.Count).Select(i => (ulong)(30_000 + i)).ToArray(), cornerPoints, cornerEdges);
        IReadOnlyList<RekallAgeGeometryAttribute> attributes = [];
        if (createUvs)
        {
            var minY = positions.Min(p => p.Y); var maxY = positions.Max(p => p.Y); var span = Math.Max(1e-9, maxY - minY);
            var uv = cornerPoints.Select(index => { var p = positions[index]; return JsonSerializer.SerializeToElement(new[] { 0.5 + Math.Atan2(p.Z, p.X) / (Math.PI * 2), (p.Y - minY) / span }); }).ToArray();
            attributes = [new("uv.generated", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2, uv, "texcoord-0")];
        }
        var mesh = RekallAgeMeshAsset.Create($"{graph.AssetId}.{node.NodeId}", node.NodeId, topology) with { Revision = graph.Revision, Attributes = attributes };
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        if (!validation.IsValid) throw new EvaluationException("REKALL_MODELING_EVALUATION_OUTPUT_INVALID", $"{node.TypeId} produced invalid topology.", node.NodeId);
        return mesh;
    }

    private static IReadOnlyList<RekallAgeGeometryVector3> ReadPointArray(RekallAgeModelingGraphNode node, string name, int minimum)
    {
        if (node.Parameters[name] is not JsonArray array || array.Count < minimum || array.Count > 4096) throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", $"Parameter '{name}' requires {minimum}-4096 points.", node.NodeId);
        return array.Select(item => item is JsonArray { Count: 3 } p && TryCoordinate(p[0], out var x) && TryCoordinate(p[1], out var y) && TryCoordinate(p[2], out var z) && double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z) ? new RekallAgeGeometryVector3(x, y, z) : throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", $"Parameter '{name}' contains an invalid point.", node.NodeId)).ToArray();
    }
    private static bool TryCoordinate(JsonNode? node, out double value)
    {
        value = default;
        if (node is not JsonValue json) return false;
        if (json.TryGetValue<double>(out value)) return true;
        if (json.TryGetValue<int>(out var integer)) { value = integer; return true; }
        if (json.TryGetValue<long>(out var longInteger)) { value = longInteger; return true; }
        return false;
    }
    private static Vector3 ToVector(RekallAgeGeometryVector3 p) => new((float)p.X, (float)p.Y, (float)p.Z);
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && v.LengthSquared() > 1e-12f;
    private static Vector3 InitialNormal(Vector3 tangent) { var axis = Math.Abs(tangent.Y) < .9f ? Vector3.UnitY : Vector3.UnitX; return Vector3.Normalize(Vector3.Cross(axis, tangent)); }
}
