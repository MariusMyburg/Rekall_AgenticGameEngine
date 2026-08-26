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

public sealed partial class RekallAgeMeshOperationExecutor
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
            [NumberParameter("x"), NumberParameter("y"), NumberParameter("z", 1)]),
        new(
            "delete",
            "Deletes selected faces and their corners while preserving now-loose points and edges for explicit subsequent editing.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
            []),
        new(
            "generate_normals",
            "Generates finite face normals into a named corner-domain Float3 attribute for selected faces.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Attributes,
            [StringParameter("attribute", "normal.generated", "Destination corner normal attribute name.")]),
        new(
            "shade_faces",
            "Authors smooth or flat shading policy on selected stable faces without changing topology.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Attributes,
            [
                new("smooth", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(true), "Whether selected faces participate in smooth normal fans."),
                StringParameter("attribute", "normal.smooth", "Destination face-domain smoothing policy attribute.")
            ]),
        new(
            "mark_sharp",
            "Authors sharp or smooth fan boundaries on selected stable edges without changing topology.",
            RekallAgeGeometryDomain.Edge,
            RekallAgeMeshChangeKind.Attributes,
            [
                new("sharp", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(true), "Whether selected edges split adjacent normal fans."),
                StringParameter("attribute", "normal.sharp", "Destination edge-domain sharpness policy attribute.")
            ]),
        new(
            "auto_smooth",
            "Classifies mesh edges as normal-fan boundaries from adjacent face angle and topology.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Attributes,
            [
                new("angleDegrees", RekallAgeGeometryValueType.Float, false, JsonSerializer.SerializeToElement(60.0), "Maximum smooth angle in degrees from zero through 180."),
                StringParameter("sharpAttribute", "normal.sharp", "Destination edge-domain sharpness policy attribute.")
            ]),
        new(
            "project_uv",
            "Projects selected face corners onto XY, XZ, or YZ and writes a named corner-domain Float2 texture-coordinate attribute.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Attributes,
            [
                StringParameter("attribute", "uv.generated", "Destination corner UV attribute name."),
                StringParameter("projection", "planar", "Projection mode: planar, box, cylindrical, or spherical."),
                StringParameter("axis", "xy", "Projection axis: xy, xz, or yz."),
                NumberParameter("scaleU", 1), NumberParameter("scaleV", 1),
                NumberParameter("offsetU"), NumberParameter("offsetV")
            ]),
        new(
            "subdivide_faces",
            "Subdivides selected polygon faces into centroid triangle fans with stable source provenance and domain-attribute propagation.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
            []),
        new(
            "subdivide_smooth",
            "Applies one crease-aware Catmull-Clark-style smooth subdivision level to a complete manifold or boundary surface.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
            [StringParameter("creaseAttribute", "crease.edge", "Optional edge-domain Float crease weight attribute; values are clamped to zero through one.")]),
        new(
            "set_edge_crease",
            "Authors bounded subdivision crease weights on selected stable edges.",
            RekallAgeGeometryDomain.Edge,
            RekallAgeMeshChangeKind.Attributes,
            [new("weight", RekallAgeGeometryValueType.Float, true, null, "Finite crease weight from zero (smooth) through one (sharp)."),
             StringParameter("attribute", "crease.edge", "Destination edge-domain Float crease attribute.")]),
        new(
            "merge_by_distance",
            "Welds selected points within a finite distance using deterministic spatial hashing, deduplicates resulting edges, and preserves stable provenance.",
            RekallAgeGeometryDomain.Point,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
            [new("distance", RekallAgeGeometryValueType.Float, true, null, "Positive finite weld distance in mesh-local units.")]),
        new(
            "bevel_edges",
            "Rounds every selected manifold edge with bounded profile-controlled segments, inset faces, edge strips, and vertex caps with stable provenance.",
            RekallAgeGeometryDomain.Edge,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
            [
                new("width", RekallAgeGeometryValueType.Float, true, null, "Positive finite bevel width in mesh-local units."),
                new("segments", RekallAgeGeometryValueType.Int32, false, JsonSerializer.SerializeToElement(1), "Bounded transition segment count from 1 through 64."),
                new("profile", RekallAgeGeometryValueType.Float, false, JsonSerializer.SerializeToElement(0.5), "Transition profile from 0.01 through 0.99; 0.5 is circular."),
                new("clampOverlap", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(true), "Clamp width locally before inset regions overlap."),
                new("hardenNormals", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(false), "Preserve hard face transitions for the normal-authoring stage."),
                StringParameter("weightAttribute", "", "Optional edge-domain Float attribute that scales selected bevel widths from zero through one."),
                new("materialIndex", RekallAgeGeometryValueType.Int32, false, JsonSerializer.SerializeToElement(-1), "Material slot for generated bevel faces, or -1 to inherit adjacent source faces.")
            ]),
        new(
            "select_edges_by_angle",
            "Creates or replaces a named edge selection from adjacent-face angle without changing geometry.",
            RekallAgeGeometryDomain.Edge,
            RekallAgeMeshChangeKind.Selection,
            [
                StringParameter("name", "angle-edges", "Stable selection-set name."),
                new("minimumAngleDegrees", RekallAgeGeometryValueType.Float, false, JsonSerializer.SerializeToElement(30.0), "Inclusive minimum adjacent-face angle from zero through 180 degrees."),
                new("maximumAngleDegrees", RekallAgeGeometryValueType.Float, false, JsonSerializer.SerializeToElement(180.0), "Inclusive maximum adjacent-face angle from zero through 180 degrees."),
                new("includeBoundary", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(false), "Include one-face boundary edges.")
            ]),
        new(
            "mark_uv_seams",
            "Marks selected stable edges as UV chart seams in a named edge-domain Bool attribute.",
            RekallAgeGeometryDomain.Edge,
            RekallAgeMeshChangeKind.Attributes,
            [StringParameter("attribute", "uv.seam", "Destination edge seam attribute."), new("marked", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(true), "Whether selected edges are marked as seams.")]),
        new(
            "unwrap_pack_uv",
            "Builds deterministic seam-bounded UV islands, planar-parameterizes them, and packs them into a named corner map.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Attributes,
            [StringParameter("attribute", "uv.lightmap", "Destination corner UV attribute."), StringParameter("seamAttribute", "uv.seam", "Edge seam attribute used to split charts."), NumberParameter("margin", 0.01), StringParameter("semantic", "texcoord-1", "Texture-coordinate semantic for the generated map.")]),
        new(
            "inset_faces",
            "Insets selected polygon faces by a bounded thickness with optional normal-axis depth and explicit border faces.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
            [
                new("thickness", RekallAgeGeometryValueType.Float, true, null, "Positive finite inset thickness in mesh-local units."),
                new("depth", RekallAgeGeometryValueType.Float, false, JsonSerializer.SerializeToElement(0.0), "Signed offset along each source face normal."),
                new("individual", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(false), "Inset faces independently instead of as a connected region."),
                new("boundary", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(true), "Create explicit border faces between source and inset boundaries.")
            ]),
        new(
            "solidify",
            "Gives an open or closed surface deterministic thickness with reversed inner faces and optional boundary rims.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
            [
                new("thickness", RekallAgeGeometryValueType.Float, true, null, "Non-zero finite shell thickness."),
                new("offset", RekallAgeGeometryValueType.Float, false, JsonSerializer.SerializeToElement(0.0), "Shell placement from -1 (inside) through 1 (outside)."),
                new("rim", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(true), "Close open boundary edges with rim faces."),
                new("evenThickness", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(true), "Use normalized averaged point normals for consistent thickness.")
            ]),
        new(
            "weighted_normals",
            "Authors finite split corner normals from smooth-face and sharp-edge policies without changing source topology.",
            RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Attributes,
            [
                new("attribute", RekallAgeGeometryValueType.String, false, JsonSerializer.SerializeToElement("normal.authored"), "Destination corner normal attribute."),
                new("faceAreaWeight", RekallAgeGeometryValueType.Float, false, JsonSerializer.SerializeToElement(1.0), "Face-area weighting exponent from 0 through 4."),
                new("cornerAngleWeight", RekallAgeGeometryValueType.Float, false, JsonSerializer.SerializeToElement(1.0), "Corner-angle weighting exponent from 0 through 4."),
                StringParameter("smoothAttribute", "normal.smooth", "Optional face-domain smoothing policy attribute."),
                StringParameter("sharpAttribute", "normal.sharp", "Optional edge-domain sharpness policy attribute.")
            ]),
        new("fill_holes", "Fills selected simple boundary loops with deterministic polygon faces.", RekallAgeGeometryDomain.Edge,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes,
            [new("materialIndex", RekallAgeGeometryValueType.Int32, false, JsonSerializer.SerializeToElement(0), "Material slot assigned to created fill faces.")]),
        new("bridge_edge_loops", "Bridges exactly two equal-cardinality simple boundary loops with deterministic quad faces.", RekallAgeGeometryDomain.Edge,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes,
            [new("materialIndex", RekallAgeGeometryValueType.Int32, false, JsonSerializer.SerializeToElement(0), "Material slot assigned to created bridge faces.")]),
        new("poke_faces", "Pokes selected faces into centroid triangle fans while preserving source attributes and provenance.", RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection, []),
        new("dissolve_edges", "Dissolves one selected two-face manifold edge into a single polygon while preserving compatible face material data.", RekallAgeGeometryDomain.Edge,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection, []),
        new("bisect_plane", "Clips a complete mesh against an authored plane with deterministic edge intersections and interpolated point attributes.", RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | RekallAgeMeshChangeKind.Attributes | RekallAgeMeshChangeKind.Selection,
            [NumberParameter("planeX"), NumberParameter("planeY"), NumberParameter("planeZ"), NumberParameter("normalX", 1), NumberParameter("normalY"), NumberParameter("normalZ"),
             new("clearPositive", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(true), "Remove geometry on the positive plane side."),
             new("clearNegative", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(false), "Remove geometry on the negative plane side."),
             new("fill", RekallAgeGeometryValueType.Bool, false, JsonSerializer.SerializeToElement(false), "Cap the cut boundary (not yet supported).")])
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
            "generate_normals" => GenerateNormals(source, request),
            "shade_faces" => ShadeFaces(source, request),
            "mark_sharp" => MarkSharp(source, request),
            "auto_smooth" => AutoSmooth(source, request),
            "project_uv" => ProjectUv(source, request),
            "mark_uv_seams" => MarkUvSeams(source, request),
            "unwrap_pack_uv" => UnwrapPackUv(source, request),
            "subdivide_faces" => SubdivideFaces(source, request),
            "subdivide_smooth" => SubdivideSmooth(source, request),
            "set_edge_crease" => SetEdgeCrease(source, request),
            "merge_by_distance" => MergeByDistance(source, request),
            "bevel_edges" => BevelEdges(source, request),
            "select_edges_by_angle" => SelectEdgesByAngle(source, request),
            "inset_faces" => InsetFaces(source, request),
            "solidify" => Solidify(source, request),
            "weighted_normals" => WeightedNormals(source, request),
            "fill_holes" => FillHoles(source, request),
            "bridge_edge_loops" => BridgeEdgeLoops(source, request),
            "poke_faces" => SubdivideFaces(source, request),
            "dissolve_edges" => DissolveEdges(source, request),
            "bisect_plane" => BisectPlane(source, request),
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

    private static RekallAgeMeshOperationResult GenerateNormals(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        var faceIndices = ResolveIndices(source.Topology.FaceIds, request.ElementIds, "face");
        var attributeName = ReadBoundedString(request.Parameters, "attribute", "normal.generated");
        var existing = source.Attributes.FirstOrDefault(item => item.Name == attributeName);
        if (existing is not null && (existing.Domain != RekallAgeGeometryDomain.Corner || existing.ValueType != RekallAgeGeometryValueType.Float3))
            throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Attribute '{attributeName}' exists with an incompatible domain or type.");
        var values = existing?.Values.ToArray() ?? Enumerable.Repeat(JsonSerializer.SerializeToElement(new[] { 0d, 0d, 1d }), source.Topology.CornerIds.Count).ToArray();
        var modifiedCorners = new List<ulong>();
        foreach (var faceIndex in faceIndices)
        {
            var start = source.Topology.FaceOffsets[faceIndex]; var end = source.Topology.FaceOffsets[faceIndex + 1];
            double x = 0, y = 0, z = 0;
            for (var corner = start; corner < end; corner++)
            {
                var next = corner + 1 == end ? start : corner + 1;
                var currentPoint = source.Topology.Positions[source.Topology.CornerPointIndices[corner]];
                var nextPoint = source.Topology.Positions[source.Topology.CornerPointIndices[next]];
                x += (currentPoint.Y - nextPoint.Y) * (currentPoint.Z + nextPoint.Z);
                y += (currentPoint.Z - nextPoint.Z) * (currentPoint.X + nextPoint.X);
                z += (currentPoint.X - nextPoint.X) * (currentPoint.Y + nextPoint.Y);
            }
            var length = Math.Sqrt(x * x + y * y + z * z);
            if (!double.IsFinite(length) || length <= 1e-12) throw Failure("REKALL_MESH_OPERATION_NORMAL_DEGENERATE", $"Face '{source.Topology.FaceIds[faceIndex]}' has no finite normal.");
            var encoded = JsonSerializer.SerializeToElement(new[] { x / length, y / length, z / length });
            for (var corner = start; corner < end; corner++) { values[corner] = encoded; modifiedCorners.Add(source.Topology.CornerIds[corner]); }
        }
        var attribute = new RekallAgeGeometryAttribute(attributeName, RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float3, values, "normal", RekallAgeGeometryInterpolation.NormalizedLinear);
        var attributes = source.Attributes.Where(item => item.Name != attributeName).Append(attribute).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        var mesh = source with { Revision = checked(source.Revision + 1), Attributes = attributes };
        return Result(source, mesh, ChangeSet(RekallAgeMeshChangeKind.Attributes, modifiedFaces: request.ElementIds.Order().ToArray(), modifiedCorners: modifiedCorners.Order().ToArray(), changedAttributes: [attributeName], affectedBounds: Bounds(faceIndices.SelectMany(index => Enumerable.Range(source.Topology.FaceOffsets[index], source.Topology.FaceOffsets[index + 1] - source.Topology.FaceOffsets[index])).Select(index => source.Topology.Positions[source.Topology.CornerPointIndices[index]]))), request.ElementIds.Order().Select(id => Preserve(RekallAgeGeometryDomain.Face, id)).ToArray());
    }

    private static RekallAgeMeshOperationResult ProjectUv(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        var faceIndices = ResolveIndices(source.Topology.FaceIds, request.ElementIds, "face");
        var attributeName = ReadBoundedString(request.Parameters, "attribute", "uv.generated"); var axis = ReadBoundedString(request.Parameters, "axis", "xy").ToLowerInvariant();
        var projection = ReadBoundedString(request.Parameters, "projection", "planar").ToLowerInvariant();
        if (axis is not ("xy" or "xz" or "yz")) throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "UV projection axis must be 'xy', 'xz', or 'yz'.");
        if (projection is not ("planar" or "box" or "cylindrical" or "spherical")) throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "UV projection mode must be planar, box, cylindrical, or spherical.");
        var existing = source.Attributes.FirstOrDefault(item => item.Name == attributeName);
        if (existing is not null && (existing.Domain != RekallAgeGeometryDomain.Corner || existing.ValueType != RekallAgeGeometryValueType.Float2)) throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Attribute '{attributeName}' exists with an incompatible domain or type.");
        var scaleU = ReadFiniteDouble(request.Parameters, "scaleU", 1); var scaleV = ReadFiniteDouble(request.Parameters, "scaleV", 1); var offsetU = ReadFiniteDouble(request.Parameters, "offsetU"); var offsetV = ReadFiniteDouble(request.Parameters, "offsetV");
        var values = existing?.Values.ToArray() ?? Enumerable.Repeat(JsonSerializer.SerializeToElement(new[] { 0d, 0d }), source.Topology.CornerIds.Count).ToArray(); var modifiedCorners = new List<ulong>();
        foreach (var faceIndex in faceIndices)
        {
            var normal = UvFaceNormal(source.Topology, faceIndex);
            for (var corner = source.Topology.FaceOffsets[faceIndex]; corner < source.Topology.FaceOffsets[faceIndex + 1]; corner++)
            {
                var point = source.Topology.Positions[source.Topology.CornerPointIndices[corner]];
                var projected = RekallAgeUvProjection.Project(point, projection, axis, normal);
                var u = projected.X * scaleU + offsetU; var v = projected.Y * scaleV + offsetV;
                if (!double.IsFinite(u) || !double.IsFinite(v)) throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "UV projection produced a non-finite coordinate.");
                values[corner] = JsonSerializer.SerializeToElement(new[] { u, v }); modifiedCorners.Add(source.Topology.CornerIds[corner]);
            }
        }
        var attribute = new RekallAgeGeometryAttribute(attributeName, RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2, values, "texcoord-0");
        var attributes = source.Attributes.Where(item => item.Name != attributeName).Append(attribute).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        var mesh = source with { Revision = checked(source.Revision + 1), Attributes = attributes };
        return Result(source, mesh, ChangeSet(RekallAgeMeshChangeKind.Attributes, modifiedFaces: request.ElementIds.Order().ToArray(), modifiedCorners: modifiedCorners.Order().ToArray(), changedAttributes: [attributeName], affectedBounds: Bounds(faceIndices.SelectMany(index => Enumerable.Range(source.Topology.FaceOffsets[index], source.Topology.FaceOffsets[index + 1] - source.Topology.FaceOffsets[index])).Select(index => source.Topology.Positions[source.Topology.CornerPointIndices[index]]))), request.ElementIds.Order().Select(id => Preserve(RekallAgeGeometryDomain.Face, id)).ToArray());
    }

    private static RekallAgeMeshOperationResult SubdivideFaces(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        var selectedIndices = ResolveIndices(source.Topology.FaceIds, request.ElementIds, "face").ToHashSet();
        var topology = source.Topology;
        var pointIds = topology.PointIds.ToList(); var positions = topology.Positions.ToList();
        var edges = topology.EdgePointIndices.ToList(); var edgeIds = topology.EdgeIds.ToList();
        var faceIds = new List<ulong>(); var faceOffsets = new List<int> { 0 }; var cornerIds = new List<ulong>(); var cornerPoints = new List<int>(); var cornerEdges = new List<int>();
        var createdPoints = new List<ulong>(); var createdEdges = new List<ulong>(); var createdFaces = new List<ulong>(); var createdCorners = new List<ulong>();
        var faceSources = new List<int>(); var cornerSources = new List<int?>(); var pointCentroidSources = new List<int[]>();
        var provenance = new List<RekallAgeMeshElementProvenance>(); var faceMap = new Dictionary<ulong, IReadOnlyList<ulong>>();
        var nextPointId = NextId(pointIds); var nextEdgeId = NextId(edgeIds); var nextFaceId = NextId(topology.FaceIds); var nextCornerId = NextId(topology.CornerIds);
        for (var faceIndex = 0; faceIndex < topology.FaceIds.Count; faceIndex++)
        {
            var start = topology.FaceOffsets[faceIndex]; var end = topology.FaceOffsets[faceIndex + 1];
            if (!selectedIndices.Contains(faceIndex))
            {
                faceIds.Add(topology.FaceIds[faceIndex]); faceSources.Add(faceIndex);
                for (var corner = start; corner < end; corner++) { cornerIds.Add(topology.CornerIds[corner]); cornerPoints.Add(topology.CornerPointIndices[corner]); cornerEdges.Add(topology.CornerEdgeIndices[corner]); cornerSources.Add(corner); }
                faceOffsets.Add(cornerIds.Count); continue;
            }
            var sourcePointIndices = Enumerable.Range(start, end - start).Select(corner => topology.CornerPointIndices[corner]).ToArray();
            var centroid = new RekallAgeGeometryVector3(sourcePointIndices.Average(index => topology.Positions[index].X), sourcePointIndices.Average(index => topology.Positions[index].Y), sourcePointIndices.Average(index => topology.Positions[index].Z));
            var centroidIndex = pointIds.Count; var centroidId = nextPointId++; pointIds.Add(centroidId); positions.Add(centroid); createdPoints.Add(centroidId); pointCentroidSources.Add(sourcePointIndices);
            var radialEdges = new int[sourcePointIndices.Length];
            for (var i = 0; i < sourcePointIndices.Length; i++) { radialEdges[i] = edges.Count; edges.Add(new(sourcePointIndices[i], centroidIndex)); var id = nextEdgeId++; edgeIds.Add(id); createdEdges.Add(id); }
            var outputs = new List<ulong>();
            for (var i = 0; i < sourcePointIndices.Length; i++)
            {
                var sourceCorner = start + i; var nextCorner = start + ((i + 1) % sourcePointIndices.Length);
                var outputFaceId = i == 0 ? topology.FaceIds[faceIndex] : nextFaceId++;
                if (i > 0) createdFaces.Add(outputFaceId); outputs.Add(outputFaceId); faceIds.Add(outputFaceId); faceSources.Add(faceIndex);
                cornerIds.Add(topology.CornerIds[sourceCorner]); cornerPoints.Add(sourcePointIndices[i]); cornerEdges.Add(topology.CornerEdgeIndices[sourceCorner]); cornerSources.Add(sourceCorner);
                var nextVertexCornerId = nextCornerId++; cornerIds.Add(nextVertexCornerId); createdCorners.Add(nextVertexCornerId); cornerPoints.Add(sourcePointIndices[(i + 1) % sourcePointIndices.Length]); cornerEdges.Add(radialEdges[(i + 1) % sourcePointIndices.Length]); cornerSources.Add(nextCorner);
                var centroidCornerId = nextCornerId++; cornerIds.Add(centroidCornerId); createdCorners.Add(centroidCornerId); cornerPoints.Add(centroidIndex); cornerEdges.Add(radialEdges[i]); cornerSources.Add(null);
                faceOffsets.Add(cornerIds.Count);
            }
            faceMap[topology.FaceIds[faceIndex]] = outputs; provenance.Add(new(RekallAgeGeometryDomain.Face, topology.FaceIds[faceIndex], outputs));
        }
        var attributes = source.Attributes.Select(attribute => attribute.Domain switch
        {
            RekallAgeGeometryDomain.Point => attribute with { Values = attribute.Values.Concat(pointCentroidSources.Select(indices => Average(attribute, indices))).ToArray() },
            RekallAgeGeometryDomain.Edge => attribute with { Values = attribute.Values.Concat(Enumerable.Repeat(DefaultValue(attribute), createdEdges.Count)).ToArray() },
            RekallAgeGeometryDomain.Face => attribute with { Values = faceSources.Select(index => attribute.Values[index]).ToArray() },
            RekallAgeGeometryDomain.Corner => attribute with { Values = cornerSources.Select((index, outputIndex) => index.HasValue ? attribute.Values[index.Value] : Average(attribute, FaceCornerSourceIndices(faceSources[FaceIndexForCorner(faceOffsets, outputIndex)], topology))).ToArray() },
            _ => attribute
        }).ToArray();
        var newTopology = topology with { PointIds = pointIds, Positions = positions, EdgeIds = edgeIds, EdgePointIndices = edges, FaceIds = faceIds, FaceOffsets = faceOffsets, CornerIds = cornerIds, CornerPointIndices = cornerPoints, CornerEdgeIndices = cornerEdges };
        var mesh = source with { Revision = checked(source.Revision + 1), Topology = newTopology, Attributes = attributes, SelectionSets = PropagateSubdivisionSelections(source.SelectionSets, faceMap) };
        return Result(source, mesh, ChangeSet(RekallAgeMeshChangeKind.Topology | (source.Attributes.Count > 0 ? RekallAgeMeshChangeKind.Attributes : RekallAgeMeshChangeKind.None) | (source.SelectionSets.Count > 0 ? RekallAgeMeshChangeKind.Selection : RekallAgeMeshChangeKind.None), createdPoints: createdPoints, createdEdges: createdEdges, createdFaces: createdFaces, createdCorners: createdCorners, modifiedFaces: request.ElementIds.Order().ToArray(), changedAttributes: source.Attributes.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray(), affectedBounds: Bounds(selectedIndices.SelectMany(index => FaceCornerSourceIndices(index, topology)).Select(index => topology.Positions[topology.CornerPointIndices[index]]))), provenance);
    }

    private static RekallAgeMeshOperationResult MergeByDistance(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Point);
        var selected = ResolveIndices(source.Topology.PointIds, request.ElementIds, "point").Order().ToArray();
        var distance = ReadFiniteDouble(request.Parameters, "distance");
        if (distance <= 0) throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Merge distance must be positive.");
        var parent = Enumerable.Range(0, source.Topology.PointIds.Count).ToArray();
        int Find(int index) { while (parent[index] != index) { parent[index] = parent[parent[index]]; index = parent[index]; } return index; }
        void Union(int first, int second)
        {
            var a = Find(first); var b = Find(second); if (a == b) return;
            if (source.Topology.PointIds[a] <= source.Topology.PointIds[b]) parent[b] = a; else parent[a] = b;
        }
        var cells = new Dictionary<(long X, long Y, long Z), List<int>>(); var distanceSquared = distance * distance;
        if (!double.IsFinite(distanceSquared)) throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Merge distance is too large for bounded spatial evaluation.");
        foreach (var pointIndex in selected)
        {
            var point = source.Topology.Positions[pointIndex]; var cell = SpatialCell(point, distance);
            for (var x = -1; x <= 1; x++) for (var y = -1; y <= 1; y++) for (var z = -1; z <= 1; z++)
            {
                var neighbor = (X: checked(cell.X + x), Y: checked(cell.Y + y), Z: checked(cell.Z + z));
                if (!cells.TryGetValue(neighbor, out var candidates)) continue;
                foreach (var candidate in candidates)
                {
                    var other = source.Topology.Positions[candidate]; var dx = point.X - other.X; var dy = point.Y - other.Y; var dz = point.Z - other.Z;
                    if (dx * dx + dy * dy + dz * dz <= distanceSquared) Union(pointIndex, candidate);
                }
            }
            if (!cells.TryGetValue(cell, out var bucket)) cells[cell] = bucket = []; bucket.Add(pointIndex);
        }
        for (var i = 0; i < parent.Length; i++) parent[i] = Find(i);
        var groups = Enumerable.Range(0, parent.Length).GroupBy(index => parent[index]).ToDictionary(group => group.Key, group => group.ToArray());
        var pointIds = new List<ulong>(); var positions = new List<RekallAgeGeometryVector3>(); var rootToNew = new Dictionary<int, int>();
        foreach (var root in groups.Keys.Order())
        {
            rootToNew[root] = pointIds.Count; var members = groups[root]; pointIds.Add(source.Topology.PointIds[root]);
            positions.Add(new(members.Average(index => source.Topology.Positions[index].X), members.Average(index => source.Topology.Positions[index].Y), members.Average(index => source.Topology.Positions[index].Z)));
        }
        var oldToNew = Enumerable.Range(0, parent.Length).Select(index => rootToNew[parent[index]]).ToArray();
        var edgeIds = new List<ulong>(); var edges = new List<RekallAgeMeshEdgePointIndices>(); var edgeSources = new List<int>(); var edgeRemap = new int[source.Topology.EdgeIds.Count];
        var edgeByPoints = new Dictionary<(int A, int B), int>(); var deletedEdges = new List<ulong>(); var edgeProvenance = new List<RekallAgeMeshElementProvenance>();
        for (var index = 0; index < source.Topology.EdgeIds.Count; index++)
        {
            var edge = source.Topology.EdgePointIndices[index]; var a = oldToNew[edge.A]; var b = oldToNew[edge.B];
            if (a == b) { edgeRemap[index] = -1; deletedEdges.Add(source.Topology.EdgeIds[index]); edgeProvenance.Add(new(RekallAgeGeometryDomain.Edge, source.Topology.EdgeIds[index], [])); continue; }
            var key = EdgeKey(a, b);
            if (edgeByPoints.TryGetValue(key, out var existing)) { edgeRemap[index] = existing; deletedEdges.Add(source.Topology.EdgeIds[index]); edgeProvenance.Add(new(RekallAgeGeometryDomain.Edge, source.Topology.EdgeIds[index], [edgeIds[existing]])); continue; }
            edgeRemap[index] = edges.Count; edgeByPoints[key] = edges.Count; edgeIds.Add(source.Topology.EdgeIds[index]); edges.Add(new(a, b)); edgeSources.Add(index);
        }
        if (source.Topology.CornerEdgeIndices.Any(index => edgeRemap[index] < 0)) throw Failure("REKALL_MESH_OPERATION_WELD_COLLAPSES_FACE_EDGE", "Merge would collapse a face boundary edge; use a smaller distance or remesh explicitly.");
        var attributes = source.Attributes.Select(attribute => attribute.Domain switch
        {
            RekallAgeGeometryDomain.Point => attribute with { Values = groups.Keys.Order().Select(root => Average(attribute, groups[root])).ToArray() },
            RekallAgeGeometryDomain.Edge => attribute with { Values = edgeSources.Select(index => attribute.Values[index]).ToArray() },
            _ => attribute
        }).ToArray();
        var pointOutputIds = oldToNew.Select(index => pointIds[index]).ToArray(); var nullablePointOutputIds = pointOutputIds.Select(id => (ulong?)id).ToArray(); var edgeOutputIds = edgeRemap.Select(index => index < 0 ? (ulong?)null : edgeIds[index]).ToArray();
        var selections = source.SelectionSets.Select(selection => selection.Domain switch
        {
            RekallAgeGeometryDomain.Point => RemapSelection(selection, source.Topology.PointIds, nullablePointOutputIds),
            RekallAgeGeometryDomain.Edge => RemapSelection(selection, source.Topology.EdgeIds, edgeOutputIds),
            _ => selection
        }).ToArray();
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Topology = source.Topology with { PointIds = pointIds, Positions = positions, EdgeIds = edgeIds, EdgePointIndices = edges, CornerPointIndices = source.Topology.CornerPointIndices.Select(index => oldToNew[index]).ToArray(), CornerEdgeIndices = source.Topology.CornerEdgeIndices.Select(index => edgeRemap[index]).ToArray() },
            Attributes = attributes, SelectionSets = selections
        };
        var pointIndexById = source.Topology.PointIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
        var pointProvenance = request.ElementIds.Select(id => new RekallAgeMeshElementProvenance(RekallAgeGeometryDomain.Point, id, [pointOutputIds[pointIndexById[id]]]));
        var deletedPoints = Enumerable.Range(0, parent.Length).Where(index => parent[index] != index).Select(index => source.Topology.PointIds[index]).Order().ToArray();
        var modifiedPoints = groups.Where(item => item.Value.Length > 1).Select(item => source.Topology.PointIds[item.Key]).Order().ToArray();
        return Result(source, mesh, ChangeSet(RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | (source.Attributes.Any(item => item.Domain is RekallAgeGeometryDomain.Point or RekallAgeGeometryDomain.Edge) ? RekallAgeMeshChangeKind.Attributes : RekallAgeMeshChangeKind.None) | (source.SelectionSets.Count > 0 ? RekallAgeMeshChangeKind.Selection : RekallAgeMeshChangeKind.None), deletedPoints: deletedPoints, deletedEdges: deletedEdges.Order().ToArray(), modifiedPoints: modifiedPoints, modifiedEdges: edgeIds.Where(id => edgeProvenance.Any(item => item.OutputElementIds.Contains(id))).Order().ToArray(), changedAttributes: attributes.Where(item => item.Domain is RekallAgeGeometryDomain.Point or RekallAgeGeometryDomain.Edge).Select(item => item.Name).Order(StringComparer.Ordinal).ToArray(), affectedBounds: Bounds(selected.Select(index => source.Topology.Positions[index]).Concat(positions))), pointProvenance.Concat(edgeProvenance).ToArray());
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

    private static (long X, long Y, long Z) SpatialCell(
        RekallAgeGeometryVector3 point,
        double distance)
    {
        const double cellLimit = 9_000_000_000_000_000_000d;
        static long Coordinate(double value, double cellSize)
        {
            var coordinate = Math.Floor(value / cellSize);
            if (!double.IsFinite(coordinate) || Math.Abs(coordinate) > cellLimit)
            {
                throw Failure(
                    "REKALL_MESH_OPERATION_PARAMETER_INVALID",
                    "Merge distance is too small for the mesh coordinate range.");
            }
            return checked((long)coordinate);
        }

        return (
            Coordinate(point.X, distance),
            Coordinate(point.Y, distance),
            Coordinate(point.Z, distance));
    }

    private static RekallAgeMeshSelection RemapSelection(
        RekallAgeMeshSelection selection,
        IReadOnlyList<ulong> sourceIds,
        IReadOnlyList<ulong?> outputIds)
    {
        var map = sourceIds
            .Select((id, index) => (id, outputId: outputIds[index]))
            .ToDictionary(item => item.id, item => item.outputId);

        static IReadOnlyList<ulong> Remap(
            IReadOnlyList<ulong> ids,
            IReadOnlyDictionary<ulong, ulong?> mapping)
        {
            var remapped = new List<ulong>();
            var seen = new HashSet<ulong>();
            foreach (var id in ids)
            {
                var outputId = mapping.TryGetValue(id, out var mapped) ? mapped : id;
                if (outputId.HasValue && seen.Add(outputId.Value)) remapped.Add(outputId.Value);
            }
            return remapped;
        }

        ulong? active = selection.ActiveElementId;
        if (active.HasValue && map.TryGetValue(active.Value, out var mappedActive)) active = mappedActive;
        return selection with
        {
            ElementIds = Remap(selection.ElementIds, map),
            ActiveElementId = active,
            OrderedHistory = selection.OrderedHistory is null ? null : Remap(selection.OrderedHistory, map)
        };
    }

    private static ulong NextId(IReadOnlyCollection<ulong> ids)
    {
        return ids.Count == 0 ? 1 : checked(ids.Max() + 1);
    }

    private static IReadOnlyList<int> FaceCornerSourceIndices(int faceIndex, RekallAgeMeshTopology topology) =>
        Enumerable.Range(topology.FaceOffsets[faceIndex], topology.FaceOffsets[faceIndex + 1] - topology.FaceOffsets[faceIndex]).ToArray();

    private static int FaceIndexForCorner(IReadOnlyList<int> faceOffsets, int cornerIndex)
    {
        var low = 0; var high = faceOffsets.Count - 2;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (cornerIndex < faceOffsets[middle]) high = middle - 1;
            else if (cornerIndex >= faceOffsets[middle + 1]) low = middle + 1;
            else return middle;
        }
        throw Failure("REKALL_MESH_OPERATION_INTERNAL", "Corner does not belong to an output face.");
    }

    private static JsonElement Average(RekallAgeGeometryAttribute attribute, IReadOnlyList<int> indices)
    {
        if (indices.Count == 0) return DefaultValue(attribute);
        if (attribute.Interpolation is RekallAgeGeometryInterpolation.Constant or RekallAgeGeometryInterpolation.Nearest
            || attribute.ValueType is RekallAgeGeometryValueType.Bool or RekallAgeGeometryValueType.Int4 or RekallAgeGeometryValueType.String
                or RekallAgeGeometryValueType.Quaternion or RekallAgeGeometryValueType.Matrix4x4)
            return attribute.Values[indices[0]];
        if (attribute.ValueType == RekallAgeGeometryValueType.Int32)
            return JsonSerializer.SerializeToElement((int)Math.Round(indices.Average(index => attribute.Values[index].GetInt32()), MidpointRounding.AwayFromZero));
        if (attribute.ValueType == RekallAgeGeometryValueType.Float)
            return JsonSerializer.SerializeToElement(indices.Average(index => attribute.Values[index].GetDouble()));
        var length = attribute.ValueType switch
        {
            RekallAgeGeometryValueType.Float2 => 2,
            RekallAgeGeometryValueType.Float3 => 3,
            RekallAgeGeometryValueType.Float4 or RekallAgeGeometryValueType.ColorLinear => 4,
            _ => 0
        };
        if (length == 0) return attribute.Values[indices[0]];
        var sums = new double[length];
        foreach (var index in indices)
        {
            var values = attribute.Values[index].EnumerateArray().Select(item => item.GetDouble()).ToArray();
            for (var component = 0; component < length; component++) sums[component] += values[component];
        }
        for (var component = 0; component < length; component++) sums[component] /= indices.Count;
        return JsonSerializer.SerializeToElement(sums);
    }

    private static IReadOnlyList<RekallAgeMeshSelection> PropagateSubdivisionSelections(
        IReadOnlyList<RekallAgeMeshSelection> selections,
        IReadOnlyDictionary<ulong, IReadOnlyList<ulong>> faceMap) =>
        selections.Select(selection => selection.Domain != RekallAgeGeometryDomain.Face
            ? selection
            : selection with
            {
                ElementIds = Expand(selection.ElementIds, faceMap),
                OrderedHistory = selection.OrderedHistory is null ? null : Expand(selection.OrderedHistory, faceMap),
                ActiveElementId = selection.ActiveElementId.HasValue && faceMap.TryGetValue(selection.ActiveElementId.Value, out var outputs)
                    ? outputs[0]
                    : selection.ActiveElementId
            }).ToArray();

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
            RekallAgeGeometryValueType.Int4 => JsonSerializer.SerializeToElement(new int[4]),
            RekallAgeGeometryValueType.Float => JsonSerializer.SerializeToElement(0.0),
            RekallAgeGeometryValueType.Float2 => JsonSerializer.SerializeToElement(new double[2]),
            RekallAgeGeometryValueType.Float3 => JsonSerializer.SerializeToElement(new double[3]),
            RekallAgeGeometryValueType.Float4 or RekallAgeGeometryValueType.ColorLinear or RekallAgeGeometryValueType.Quaternion => JsonSerializer.SerializeToElement(new double[4]),
            RekallAgeGeometryValueType.Matrix4x4 => JsonSerializer.SerializeToElement(new double[16]),
            RekallAgeGeometryValueType.String => JsonSerializer.SerializeToElement(string.Empty),
            _ => throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_DEFAULT_INVALID", $"Attribute '{attribute.Name}' has no default value.")
        };
    }

    private static RekallAgeMeshOperationParameterDescriptor NumberParameter(string name, double defaultValue = 0) =>
        new(
            name,
            RekallAgeGeometryValueType.Float,
            false,
            JsonSerializer.SerializeToElement(defaultValue),
            $"Finite {name.ToUpperInvariant()} offset in mesh-local units.");

    private static RekallAgeMeshOperationParameterDescriptor StringParameter(string name, string defaultValue, string description) =>
        new(name, RekallAgeGeometryValueType.String, false, JsonSerializer.SerializeToElement(defaultValue), description);

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
        => ReadFiniteDouble(parameters, name, 0);

    private static double ReadFiniteDouble(JsonObject parameters, string name, double defaultValue)
    {
        if (!parameters.TryGetPropertyValue(name, out var node) || node is null)
        {
            return defaultValue;
        }
        if (node is not JsonValue value || !TryReadNumber(value, out var number) || !IsFinite(number))
        {
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", $"Parameter '{name}' must be a finite number.");
        }
        return number;
    }

    private static string ReadBoundedString(JsonObject parameters, string name, string defaultValue)
    {
        if (!parameters.TryGetPropertyValue(name, out var node) || node is null) return defaultValue;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text) || text.Length > 128)
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", $"Parameter '{name}' must be a bounded non-empty string.");
        return text;
    }

    private static int ReadBoundedInt(JsonObject parameters, string name, int defaultValue, int minimum, int maximum)
    {
        if (!parameters.TryGetPropertyValue(name, out var node) || node is null) return defaultValue;
        if (node is not JsonValue value || !value.TryGetValue<int>(out var number) || number < minimum || number > maximum)
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", $"Parameter '{name}' must be an integer between {minimum} and {maximum}.");
        return number;
    }

    private static bool ReadBoolean(JsonObject parameters, string name, bool defaultValue)
    {
        if (!parameters.TryGetPropertyValue(name, out var node) || node is null) return defaultValue;
        if (node is not JsonValue value || !value.TryGetValue<bool>(out var result))
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", $"Parameter '{name}' must be boolean.");
        return result;
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
