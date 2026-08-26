using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeModelingNodeCatalog
{
    private readonly IReadOnlyDictionary<string, RekallAgeModelingNodeDescriptor> _byIdentity;

    private RekallAgeModelingNodeCatalog(IReadOnlyList<RekallAgeModelingNodeDescriptor> descriptors)
    {
        Descriptors = descriptors;
        _byIdentity = descriptors.ToDictionary(Identity, StringComparer.Ordinal);
    }

    public IReadOnlyList<RekallAgeModelingNodeDescriptor> Descriptors { get; }

    public RekallAgeModelingNodeDescriptor? Find(string typeId, int typeVersion) =>
        _byIdentity.GetValueOrDefault(Identity(typeId, typeVersion));

    public static RekallAgeModelingNodeCatalog CreateDefault() => new(
    [
        Primitive("rekall.modeling.primitive.box", "Box", [
            Number("sizeX", "Size X", 1, 0.0001, 1_000_000, "world-unit"),
            Number("sizeY", "Size Y", 1, 0.0001, 1_000_000, "world-unit"),
            Number("sizeZ", "Size Z", 1, 0.0001, 1_000_000, "world-unit")]),
        Primitive("rekall.modeling.primitive.grid", "Grid", [
            Number("sizeX", "Size X", 1, 0.0001, 1_000_000, "world-unit"),
            Number("sizeY", "Size Y", 1, 0.0001, 1_000_000, "world-unit"),
            Integer("segmentsX", "Segments X", 1, 1, 4_096),
            Integer("segmentsY", "Segments Y", 1, 1, 4_096)]),
        Primitive("rekall.modeling.primitive.sphere", "Sphere", [
            Number("radius", "Radius", 0.5, 0.0001, 1_000_000, "world-unit"),
            Integer("segments", "Segments", 16, 3, 4_096),
            Integer("rings", "Rings", 8, 2, 4_096)]),
        Primitive("rekall.modeling.primitive.frustum", "Frustum", [
            Number("radiusBottom", "Bottom Radius", 0.5, 0, 1_000_000, "world-unit"),
            Number("radiusTop", "Top Radius", 0.5, 0, 1_000_000, "world-unit"),
            Number("depth", "Depth", 1, 0.0001, 1_000_000, "world-unit"),
            Integer("segments", "Segments", 16, 3, 4_096),
            Boolean("capBottom", "Cap Bottom", true),
            Boolean("capTop", "Cap Top", true)]),
        Primitive("rekall.modeling.primitive.torus", "Torus", [
            Number("majorRadius", "Major Radius", 1, 0.0001, 1_000_000, "world-unit"),
            Number("minorRadius", "Minor Radius", 0.25, 0.0001, 1_000_000, "world-unit"),
            Integer("majorSegments", "Major Segments", 24, 3, 4_096),
            Integer("minorSegments", "Minor Segments", 12, 3, 4_096)]),
        Primitive("rekall.modeling.primitive.plane", "Plane", [Number("sizeX", "Size X", 1, 0.0001, 1_000_000, "world-unit"), Number("sizeY", "Size Y", 1, 0.0001, 1_000_000, "world-unit")]),
        Primitive("rekall.modeling.primitive.disc", "Disc", [Number("radius", "Radius", 0.5, 0.0001, 1_000_000, "world-unit"), Integer("segments", "Segments", 16, 3, 4_096)]),
        Primitive("rekall.modeling.primitive.cylinder", "Cylinder", [Number("radius", "Radius", 0.5, 0.0001, 1_000_000, "world-unit"), Number("depth", "Depth", 1, 0.0001, 1_000_000, "world-unit"), Integer("segments", "Segments", 16, 3, 4_096), Boolean("capBottom", "Cap Bottom", true), Boolean("capTop", "Cap Top", true)]),
        Primitive("rekall.modeling.primitive.cone", "Cone", [Number("radius", "Radius", 0.5, 0.0001, 1_000_000, "world-unit"), Number("depth", "Depth", 1, 0.0001, 1_000_000, "world-unit"), Integer("segments", "Segments", 16, 3, 4_096), Boolean("capBottom", "Cap Bottom", true)]),
        Primitive("rekall.modeling.primitive.ico_sphere", "Ico Sphere", [Number("radius", "Radius", 0.5, 0.0001, 1_000_000, "world-unit"), Integer("subdivisions", "Subdivisions", 0, 0, 6)]),
        Primitive("rekall.modeling.primitive.capsule", "Capsule", [Number("radius", "Radius", 0.5, 0.0001, 1_000_000, "world-unit"), Number("depth", "Total Depth", 2, 0.0001, 1_000_000, "world-unit"), Integer("segments", "Segments", 16, 3, 4_096), Integer("hemisphereRings", "Hemisphere Rings", 4, 2, 1_024)]),
        Node("rekall.modeling.curve.source", "Curve Resource", "Evaluates a versioned poly or cubic-Bezier curve document with stable spline/control-point IDs, radius, tilt, and provenance.",
            [Output("curve", RekallAgeModelingValueType.Curve)],
            [Structured("document", "Curve Document", new JsonObject()), Integer("resolution", "Resolution Per Segment", 8, 1, 4096)]),
        Node("rekall.modeling.curve.line", "Curve Line", "Builds a straight evaluated curve with deterministic source IDs and independently authored endpoint radius and tilt.",
            [Output("curve", RekallAgeModelingValueType.Curve)],
            [Vector3("start", "Start"), Vector3("end", "End", defaultValue: 1), Number("startRadius", "Start Radius", 1, 0.0001, 1_000_000), Number("endRadius", "End Radius", 1, 0.0001, 1_000_000), Number("startTilt", "Start Tilt", 0, -1_000_000, 1_000_000, "radian"), Number("endTilt", "End Tilt", 0, -1_000_000, 1_000_000, "radian")]),
        Node("rekall.modeling.curve.circle", "Curve Circle", "Builds a deterministic cyclic curve in an authored principal plane.",
            [Output("curve", RekallAgeModelingValueType.Curve)],
            [Vector3("center", "Center"), Number("radius", "Radius", 1, 0.0001, 1_000_000, "world-unit"), Integer("segments", "Segments", 32, 3, 100_000), Text("plane", "Plane", "xy", ["xy", "xz", "yz"])]),
        Node("rekall.modeling.curve.reverse", "Reverse Curve", "Reverses evaluated spline direction while preserving source-span provenance.",
            [Input("curve", RekallAgeModelingValueType.Curve, required: true), Output("curve", RekallAgeModelingValueType.Curve)]),
        Node("rekall.modeling.curve.resample", "Resample Curve", "Resamples every evaluated spline to evenly spaced arc-length points while interpolating radius, tilt, and source provenance.",
            [Input("curve", RekallAgeModelingValueType.Curve, required: true), Output("curve", RekallAgeModelingValueType.Curve)],
            [Integer("count", "Point Count", 32, 2, 100_000)]),
        Node("rekall.modeling.curve.trim", "Trim Curve", "Extracts an open normalized arc-length range from every evaluated spline.",
            [Input("curve", RekallAgeModelingValueType.Curve, required: true), Output("curve", RekallAgeModelingValueType.Curve)],
            [Number("start", "Start", 0, 0, 1), Number("end", "End", 1, 0, 1)]),
        Node("rekall.modeling.curve.fillet", "Fillet Curve", "Rounds evaluated spline corners with a bounded multi-segment curve while preserving radius, tilt, and provenance.",
            [Input("curve", RekallAgeModelingValueType.Curve, required: true), Output("curve", RekallAgeModelingValueType.Curve)],
            [Number("radius", "Radius", 0.1, 0.0001, 1_000_000, "world-unit"), Integer("segments", "Segments", 4, 1, 256)]),
        Node("rekall.modeling.curve.join", "Join Curves", "Joins ordered open curve inputs at matching endpoints, automatically reversing an input when its far endpoint is the valid match.",
            [Input("curve", RekallAgeModelingValueType.Curve, required: true, multiple: true), Output("curve", RekallAgeModelingValueType.Curve)],
            [Number("tolerance", "Endpoint Tolerance", 0.0001, 0, 1_000_000, "world-unit")]),
        Node("rekall.modeling.curve.profile_sweep", "Profile Sweep", "Sweeps a closed circle or rectangle profile along a curve resource or legacy point path using deterministic parallel-transport frames with cap, UV, material, and source-span output.",
            [Input("curve", RekallAgeModelingValueType.Curve), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("pathPoints", "Path Points", "[[0,0,0],[0,1,0]]"), Text("profile", "Profile", "circle", ["circle", "rectangle"]), Integer("profileSegments", "Profile Segments", 8, 3, 4_096), Number("radius", "Radius", 0.25, 0.0001, 1_000_000, "world-unit"), Number("profileWidth", "Profile Width", 0.5, 0.0001, 1_000_000, "world-unit"), Number("profileHeight", "Profile Height", 0.5, 0.0001, 1_000_000, "world-unit"), Boolean("capStart", "Cap Start", true), Boolean("capEnd", "Cap End", true), Text("materialAssetId", "Material Asset ID", "material.default"), Text("slotName", "Slot Name", "Sweep")]),
        Node("rekall.modeling.curve.revolve", "Curve Revolve / Screw", "Revolves or helically screws one evaluated curve profile around an authored axis into deterministic UV/material/provenance-aware mesh topology.",
            [Input("curve", RekallAgeModelingValueType.Curve, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("axis", "Axis", "y", ["x", "y", "z"]), Vector3("origin", "Origin", "world-unit"), Number("angleDegrees", "Angle", 360, double.Epsilon, 36_000, "degree"), Number("pitchPerTurn", "Pitch Per Turn", 0, -1_000_000, 1_000_000, "world-unit"), Integer("segments", "Segments", 32, 3, 4_096), Number("weldDistance", "Weld Distance", 0.000001, 0, 1, "world-unit"), Text("materialAssetId", "Material Asset ID", "material.default"), Text("slotName", "Slot Name", "Revolved Surface")]),
        Node("rekall.modeling.transform", "Transform", "Transforms geometry without mutating its upstream snapshot.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Vector3("translation", "Translation"), Vector3("rotation", "Rotation", "degree"), Vector3("scale", "Scale", defaultValue: 1)]),
        Node("rekall.modeling.deform.noise", "Noise Deform", "Offsets points with deterministic smooth positional noise for terrain, rock, ruin, and organic surface breakup.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [
                Number("amplitude", "Amplitude", 0.25, 0, 1_000_000, "world-unit"),
                Number("frequency", "Frequency", 1, 0.000001, 1_000_000),
                Integer("seed", "Seed", 0, int.MinValue, int.MaxValue),
                Text("axis", "Axis", "z", ["x", "y", "z"])
            ]),
        Node("rekall.modeling.deform.taper", "Taper Deform", "Linearly scales point planes perpendicular to an axis for cloth, foliage, props, characters, and architecture.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [
                Text("axis", "Axis", "y", ["x", "y", "z"]),
                Number("minimum", "Minimum", 0, -1_000_000, 1_000_000, "world-unit"),
                Number("maximum", "Maximum", 1, -1_000_000, 1_000_000, "world-unit"),
                Number("startScale", "Start Scale", 1, 0.000001, 1_000_000),
                Number("endScale", "End Scale", 0.5, 0.000001, 1_000_000),
                Vector3("center", "Center", "world-unit"),
                Text("selectionSet", "Selection Set", "")
            ]),
        Node("rekall.modeling.scatter.area", "Scatter Area", "Creates deterministic transformed copies across a bounded horizontal area for reusable environmental dressing.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [
                Integer("count", "Count", 8, 1, 4_096),
                Number("sizeX", "Area Size X", 10, 0, 1_000_000, "world-unit"),
                Number("sizeZ", "Area Size Z", 10, 0, 1_000_000, "world-unit"),
                Integer("seed", "Seed", 0, int.MinValue, int.MaxValue),
                Number("minimumScale", "Minimum Scale", 0.8, 0.0001, 1_000_000),
                Number("maximumScale", "Maximum Scale", 1.2, 0.0001, 1_000_000),
                Number("minimumYaw", "Minimum Yaw", -180, -1_000_000, 1_000_000, "degree"),
                Number("maximumYaw", "Maximum Yaw", 180, -1_000_000, 1_000_000, "degree")
            ]),
        Node("rekall.modeling.join", "Join Geometry", "Combines one or more immutable geometry inputs.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true, multiple: true), Output("geometry", RekallAgeModelingValueType.Geometry)]),
        Node("rekall.modeling.boolean", "Boolean", "Computes union, intersection, or ordered A-minus-B difference between two closed manifold geometry inputs.",
            [Input("a", RekallAgeModelingValueType.Geometry, required: true), Input("b", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("operation", "Operation", "union", ["union", "intersect", "difference"])]),
        Node("rekall.modeling.extrude", "Extrude", "Extrudes a selected geometry region through the semantic mesh operation contract.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Vector3("offset", "Offset")]),
        Node("rekall.modeling.triangulate", "Triangulate", "Triangulates selected polygon faces with source provenance.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Output("geometry", RekallAgeModelingValueType.Geometry)]),
        Node("rekall.modeling.project_uv", "Project UV", "Projects selected or complete face corners into a named texture-coordinate attribute.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [
                Text("attribute", "Attribute", "uv.generated"),
                Text("projection", "Projection", "planar", ["planar", "box", "cylindrical", "spherical"]),
                Text("axis", "Axis", "xy", ["xy", "xz", "yz"]),
                Number("scaleU", "Scale U", 1, -1_000_000, 1_000_000),
                Number("scaleV", "Scale V", 1, -1_000_000, 1_000_000),
                Number("offsetU", "Offset U", 0, -1_000_000, 1_000_000),
                Number("offsetV", "Offset V", 0, -1_000_000, 1_000_000)
            ]),
        Node("rekall.modeling.uv.unwrap_pack", "Unwrap and Pack UV", "Builds deterministic seam-bounded charts, planar-parameterizes them, and packs them into a named corner UV map.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("attribute", "Attribute", "uv.generated"), Text("seamAttribute", "Seam Attribute", "uv.seam"), Number("margin", "Margin", 0.01, 0, 0.249), Text("semantic", "Semantic", "texcoord-0")]),
        Node("rekall.modeling.uv.lightmap", "Generate Lightmap UV", "Generates a separate deterministic packed lightmap UV channel without replacing material UVs.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("attribute", "Attribute", "uv.lightmap"), Text("seamAttribute", "Seam Attribute", "uv.seam"), Number("margin", "Margin", 0.02, 0, 0.249)]),
        Node("rekall.modeling.subdivide", "Subdivide", "Subdivides selected polygon faces into centroid triangle fans with source provenance.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Output("geometry", RekallAgeModelingValueType.Geometry)]),
        Node("rekall.modeling.edge_crease", "Edge Crease", "Authors bounded subdivision crease weights on all edges or a named edge selection.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Number("weight", "Weight", 1, 0, 1), Text("attribute", "Attribute", "crease.edge"), Text("selectionSet", "Selection Set", "")]),
        Node("rekall.modeling.subdivide_smooth", "Smooth Subdivision", "Applies bounded crease-aware Catmull-Clark-style levels to a complete manifold or boundary surface.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Integer("levels", "Levels", 1, 1, 6), Text("creaseAttribute", "Crease Attribute", "crease.edge")]),
        Node("rekall.modeling.bevel", "Bevel", "Rounds selected two-face manifold edges with deterministic weighted profile rings, neighboring transition faces, vertex caps, and generated-face material policy.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [
                Number("width", "Width", 0.05, 0.000001, 1_000_000, "world-unit"),
                Integer("segments", "Segments", 1, 1, 64),
                Number("profile", "Profile", 0.5, 0.01, 0.99),
                Boolean("clampOverlap", "Clamp Overlap", true),
                Boolean("hardenNormals", "Harden Normals", false),
                Text("selectionSet", "Selection Set", ""),
                Text("weightAttribute", "Weight Attribute", ""),
                Integer("materialIndex", "Generated Material Index", -1, -1, 65_535)
            ]),
        Node("rekall.modeling.selection.edge_angle", "Select Edges by Angle", "Creates a reusable named edge selection from adjacent-face angle for bevel, sharp-edge, crease, UV, and other generic consumers.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [
                Text("name", "Selection Name", "angle-edges"),
                Number("minimumAngleDegrees", "Minimum Angle", 30, 0, 180, "degree"),
                Number("maximumAngleDegrees", "Maximum Angle", 180, 0, 180, "degree"),
                Boolean("includeBoundary", "Include Boundary", false)
            ]),
        Node("rekall.modeling.skin.linear_weights", "Linear Skin Weights", "Authors normalized two-joint skin bindings from point position along an axis for deformable meshes.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [
                Text("axis", "Axis", "y", ["x", "y", "z"]),
                Number("minimum", "Minimum", 0, -1_000_000, 1_000_000, "world-unit"),
                Number("maximum", "Maximum", 1, -1_000_000, 1_000_000, "world-unit"),
                Integer("jointA", "Joint A", 0, 0, int.MaxValue),
                Integer("jointB", "Joint B", 1, 0, int.MaxValue),
                Text("selectionSet", "Selection Set", "")
            ]),
        Node("rekall.modeling.inset", "Inset Faces", "Builds deterministic recessed or raised face panels with explicit border topology.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [
                Number("thickness", "Thickness", 0.05, 0.000001, 1_000_000, "world-unit"),
                Number("depth", "Depth", 0, -1_000_000, 1_000_000, "world-unit"),
                Boolean("individual", "Individual Faces", false),
                Boolean("boundary", "Boundary", true)
            ]),
        Node("rekall.modeling.solidify", "Solidify", "Gives surfaces authored thickness with reversed inner faces and boundary rims.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Number("thickness", "Thickness", 0.05, -1_000_000, 1_000_000, "world-unit"), Number("offset", "Offset", 0, -1, 1), Boolean("rim", "Rim", true), Boolean("evenThickness", "Even Thickness", true)]),
        Node("rekall.modeling.mirror", "Mirror", "Creates a winding-correct mirrored copy around an authored local axis and origin.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("axis", "Axis", "x", ["x", "y", "z"]), Number("origin", "Origin", 0, -1_000_000, 1_000_000, "world-unit"), Number("mergeDistance", "Merge Distance", 0, 0, 1_000_000, "world-unit"), Boolean("bisect", "Bisect", false)]),
        Node("rekall.modeling.array", "Array", "Creates deterministic transformed geometry copies without scene-specific repetition logic.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Integer("count", "Count", 2, 1, 4_096), Vector3("offset", "Offset", "world-unit", 1), Boolean("relativeOffset", "Relative Offset", false), Boolean("instanceMode", "Instance Mode", false)]),
        Node("rekall.modeling.shade_faces", "Shade Faces", "Authors smooth or flat shading policy on the complete face domain.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Boolean("smooth", "Smooth", true), Text("attribute", "Attribute", "normal.smooth")]),
        Node("rekall.modeling.mark_sharp", "Mark Sharp", "Authors sharp or smooth normal-fan boundaries on the complete edge domain.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Boolean("sharp", "Sharp", true), Text("attribute", "Attribute", "normal.sharp")]),
        Node("rekall.modeling.auto_smooth", "Auto Smooth", "Classifies normal-fan boundaries from adjacent face angles and topology.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Number("angleDegrees", "Angle", 60, 0, 180, "degree"), Text("sharpAttribute", "Sharp Attribute", "normal.sharp")]),
        Node("rekall.modeling.weighted_normals", "Weighted Normals", "Authors split area- and corner-angle-weighted normals for stable hard-surface shading.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [
                Text("attribute", "Attribute", "normal.authored"),
                Number("faceAreaWeight", "Face Area Weight", 1, 0, 4),
                Number("cornerAngleWeight", "Corner Angle Weight", 1, 0, 4),
                Text("smoothAttribute", "Smooth Attribute", "normal.smooth"),
                Text("sharpAttribute", "Sharp Attribute", "normal.sharp")
            ]),
        Node("rekall.modeling.merge_by_distance", "Merge by Distance", "Welds selected points using deterministic spatial hashing and stable provenance.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Number("distance", "Distance", 0.0001, 0.000000001, 1_000_000, "world-unit")]),
        Node("rekall.modeling.fill_holes", "Fill Holes", "Fills simple boundary loops selected by a named edge selection set.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("selectionSet", "Selection Set", ""), Integer("materialIndex", "Material Index", 0, 0, 65_535)]),
        Node("rekall.modeling.bridge_edge_loops", "Bridge Edge Loops", "Bridges two equal-cardinality boundary loops selected by a named edge selection set.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("selectionSet", "Selection Set", ""), Integer("materialIndex", "Material Index", 0, 0, 65_535)]),
        Node("rekall.modeling.poke_faces", "Poke Faces", "Pokes selected faces into centroid triangle fans with propagated attributes.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("selectionSet", "Selection Set", "")]),
        Node("rekall.modeling.dissolve_edges", "Dissolve Edges", "Dissolves a selected two-face manifold edge into one polygon.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("selectionSet", "Selection Set", "")]),
        Node("rekall.modeling.bisect_plane", "Bisect Plane", "Clips complete geometry against a deterministic authored plane.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Vector3("planePoint", "Plane Point", "world-unit"), Vector3Components("planeNormal", "Plane Normal", 1, 0, 0),
             Boolean("clearPositive", "Clear Positive", true), Boolean("clearNegative", "Clear Negative", false), Boolean("fill", "Fill Cut", false)]),
        Node("rekall.modeling.attribute.capture", "Capture Attribute", "Captures a field into a named typed geometry attribute.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("value", RekallAgeModelingValueType.Scalar, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("name", "Name", "attribute"), Text("domain", "Domain", "point")]),
        Node("rekall.modeling.attribute.named", "Named Attribute", "Reads a named scalar attribute as a graph field.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Output("value", RekallAgeModelingValueType.Scalar)],
            [Text("name", "Name", "attribute")]),
        Node("rekall.modeling.field.math", "Field Math", "Applies deterministic scalar field arithmetic.",
            [Input("a", RekallAgeModelingValueType.Scalar), Input("b", RekallAgeModelingValueType.Scalar), Output("value", RekallAgeModelingValueType.Scalar)],
            [
                Text("operation", "Operation", "add", ["add", "subtract", "multiply", "divide", "minimum", "maximum"]),
                Number("a", "A", 0, -1_000_000_000, 1_000_000_000),
                Number("b", "B", 0, -1_000_000_000, 1_000_000_000)
            ]),
        Node("rekall.modeling.material.assign", "Assign Material", "Assigns a material slot to a selected geometry region.",
            [Input("geometry", RekallAgeModelingValueType.Geometry, required: true), Input("selection", RekallAgeModelingValueType.Selection), Input("material", RekallAgeModelingValueType.Material), Output("geometry", RekallAgeModelingValueType.Geometry)],
            [Text("materialAssetId", "Material Asset ID", "material.default"), Text("slotName", "Slot Name", "material")]),
        Node("rekall.modeling.output.mesh", "Mesh Output", "Publishes evaluated geometry as a named graph output.",
            [Input("input", RekallAgeModelingValueType.Geometry, required: true), Output("geometry", RekallAgeModelingValueType.Geometry)])
    ]);

    private static RekallAgeModelingNodeDescriptor Primitive(
        string typeId,
        string displayName,
        IReadOnlyList<RekallAgeModelingParameterDescriptor> parameters) =>
        Node(typeId, displayName, $"Creates deterministic {displayName.ToLowerInvariant()} geometry.",
            [Output("geometry", RekallAgeModelingValueType.Geometry)], parameters);

    private static RekallAgeModelingNodeDescriptor Node(
        string typeId,
        string displayName,
        string description,
        IReadOnlyList<RekallAgeModelingPortDescriptor> ports,
        IReadOnlyList<RekallAgeModelingParameterDescriptor>? parameters = null) =>
        new(typeId, 1, displayName, description, ports, parameters ?? []);

    private static RekallAgeModelingPortDescriptor Input(
        string id,
        RekallAgeModelingValueType type,
        bool required = false,
        bool multiple = false) =>
        new(id, Display(id), RekallAgeModelingPortDirection.Input, type, required, multiple);

    private static RekallAgeModelingPortDescriptor Output(string id, RekallAgeModelingValueType type) =>
        new(id, Display(id), RekallAgeModelingPortDirection.Output, type);

    private static RekallAgeModelingParameterDescriptor Number(
        string id, string name, double value, double minimum, double maximum, string? unit = null) =>
        new(id, name, RekallAgeModelingValueType.Scalar, JsonValue.Create(value), minimum, maximum, unit);

    private static RekallAgeModelingParameterDescriptor Integer(
        string id, string name, int value, int minimum, int maximum) =>
        new(id, name, RekallAgeModelingValueType.Integer, JsonValue.Create(value), minimum, maximum);

    private static RekallAgeModelingParameterDescriptor Boolean(string id, string name, bool value) =>
        new(id, name, RekallAgeModelingValueType.Boolean, JsonValue.Create(value));

    private static RekallAgeModelingParameterDescriptor Vector3(
        string id, string name, string? unit = null, double defaultValue = 0) =>
        new(id, name, RekallAgeModelingValueType.Vector3,
            new JsonArray(defaultValue, defaultValue, defaultValue), Unit: unit);

    private static RekallAgeModelingParameterDescriptor Vector3Components(
        string id, string name, double x, double y, double z, string? unit = null) =>
        new(id, name, RekallAgeModelingValueType.Vector3, new JsonArray(x, y, z), Unit: unit);

    private static RekallAgeModelingParameterDescriptor Text(
        string id, string name, string value, IReadOnlyList<string>? choices = null) =>
        new(id, name, RekallAgeModelingValueType.String, JsonValue.Create(value), EnumChoices: choices);

    private static RekallAgeModelingParameterDescriptor Structured(
        string id, string name, JsonNode value) =>
        new(id, name, RekallAgeModelingValueType.Json, value);

    private static string Identity(RekallAgeModelingNodeDescriptor descriptor) =>
        Identity(descriptor.TypeId, descriptor.TypeVersion);

    private static string Identity(string typeId, int typeVersion) => $"{typeId}@{typeVersion}";

    private static string Display(string id) => char.ToUpperInvariant(id[0]) + id[1..];
}
