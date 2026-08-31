using System.Numerics;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioMeshVulkanFrame(
    RekallAgeRuntimeViewportFrame Frame,
    RekallAgeStudioMeshViewportInteractionSnapshot Interaction);

internal sealed record RekallAgeStudioMeshViewportElementRegion(
    RekallAgeGeometryDomain Domain,
    ulong Id,
    IReadOnlyList<RekallAgeStudioViewportPoint> Vertices,
    double Depth);

internal sealed record RekallAgeStudioMeshViewportInteractionSnapshot(
    int FrameWidth,
    int FrameHeight,
    bool IsPreview,
    IReadOnlyDictionary<(RekallAgeGeometryDomain Domain, ulong Id), RekallAgeStudioViewportPoint> ElementCenters,
    IReadOnlyList<RekallAgeStudioMeshViewportElementRegion> Regions,
    RekallAgeStudioMeshTransformGizmo? TransformGizmo = null)
{
    private const double PointRadius = 9;
    private const double EdgeRadius = 7;

    public ulong? Pick(RekallAgeGeometryDomain domain, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return null;
        var candidates = Regions.Where(region => region.Domain == domain);
        if (domain is RekallAgeGeometryDomain.Point or RekallAgeGeometryDomain.Corner)
        {
            return candidates
                .Select(region => (region.Id, Distance(region.Vertices[0], x, y), region.Depth))
                .Where(item => item.Item2 <= PointRadius)
                .OrderBy(item => item.Item2).ThenBy(item => item.Depth).ThenBy(item => item.Id)
                .Select(item => (ulong?)item.Id).FirstOrDefault();
        }
        if (domain is RekallAgeGeometryDomain.Edge)
        {
            return candidates
                .Select(region => (region.Id, DistanceToSegment(x, y, region.Vertices[0], region.Vertices[1]), region.Depth))
                .Where(item => item.Item2 <= EdgeRadius)
                .OrderBy(item => item.Item2).ThenBy(item => item.Depth).ThenBy(item => item.Id)
                .Select(item => (ulong?)item.Id).FirstOrDefault();
        }
        return candidates
            .Where(region => Contains(region.Vertices, x, y))
            .OrderBy(region => region.Depth).ThenBy(region => region.Id)
            .Select(region => (ulong?)region.Id).FirstOrDefault();
    }

    public RekallAgeStudioMeshTransformGesture? BeginTransform(double x, double y)
    {
        if (TransformGizmo is null) return null;
        var point = new System.Windows.Point(x, y);
        return TransformGizmo.Axes
            .Select(axis => (axis, distance: DistanceToSegment(x, y,
                new(TransformGizmo.Origin.X, TransformGizmo.Origin.Y), new(axis.End.X, axis.End.Y))))
            .Where(item => item.distance <= EdgeRadius)
            .OrderBy(item => item.distance).ThenBy(item => item.axis.Axis)
            .Select(item => new RekallAgeStudioMeshTransformGesture(item.axis.Axis, point))
            .FirstOrDefault();
    }

    public RekallAgeGeometryVector3 ResolveTranslation(
        RekallAgeStudioMeshTransformGesture gesture, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (TransformGizmo is null) return default;
        var axis = TransformGizmo.Axes.Single(item => item.Axis == gesture.Axis);
        var dx = axis.End.X - TransformGizmo.Origin.X;
        var dy = axis.End.Y - TransformGizmo.Origin.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0.000001) return default;
        var distance = ((x - gesture.Start.X) * dx + (y - gesture.Start.Y) * dy) / lengthSquared;
        return gesture.Axis switch
        {
            RekallAgeStudioMeshTransformAxis.X => new(distance, 0, 0),
            RekallAgeStudioMeshTransformAxis.Y => new(0, distance, 0),
            _ => new(0, 0, distance)
        };
    }

    private static double Distance(RekallAgeStudioViewportPoint point, double x, double y) =>
        Math.Sqrt(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2));

    private static double DistanceToSegment(
        double x, double y, RekallAgeStudioViewportPoint a, RekallAgeStudioViewportPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared <= double.Epsilon ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / lengthSquared, 0, 1);
        return Math.Sqrt(Math.Pow(x - (a.X + t * dx), 2) + Math.Pow(y - (a.Y + t * dy), 2));
    }

    private static bool Contains(IReadOnlyList<RekallAgeStudioViewportPoint> polygon, double x, double y)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var j = i == 0 ? polygon.Count - 1 : i - 1;
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > y) != (b.Y > y)
                && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
}

internal sealed class RekallAgeStudioMeshVulkanFrameBuilder
{
    private const string MeshId = "__studio_edit_mesh";
    private const string SelectionColor = RekallAgeStudioViewportOverlayRenderables.SelectionColor;

    public RekallAgeStudioMeshVulkanFrame BuildEmpty(
        int width,
        int height,
        RekallAgeStudioViewportCamera camera)
    {
        if (width < 64 || width > 7680) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 64 || height > 4320) throw new ArgumentOutOfRangeException(nameof(height));
        var runtimeCamera = Camera(Vector3.Zero, 2, camera, width, height);
        var frame = new RekallAgeRuntimeViewportFrame(
            "Mesh:Empty", 0, 0, width, height, runtimeCamera, [runtimeCamera],
            RekallAgeStudioViewportOverlayRenderables.CreateGrid(), 0,
            new RekallAgeRuntimeViewportOverlay(false, 0), []);
        return new(frame, new(width, height, false,
            new Dictionary<(RekallAgeGeometryDomain, ulong), RekallAgeStudioViewportPoint>(), []));
    }

    public RekallAgeStudioMeshVulkanFrame Build(
        RekallAgeMeshAsset mesh,
        RekallAgeGeometryDomain activeDomain,
        IReadOnlyCollection<ulong> selectedIds,
        int width,
        int height,
        bool preview,
        RekallAgeStudioViewportCamera camera,
        RekallAgeStudioViewportRenderStyle style)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(selectedIds);
        if (width < 64 || width > 7680) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 64 || height > 4320) throw new ArgumentOutOfRangeException(nameof(height));

        var geometry = Geometry(mesh);
        var baseRenderable = new RekallAgeRuntimeViewportRenderable(
            MeshId, mesh.Name, "mesh", null, 0, 0, 0, 100,
            MaterialColor: "#48627a", GeometryMesh: geometry, RoughnessFactor: 0.82);
        var runtimeCamera = Camera(mesh, camera, width, height);
        var baseFrame = new RekallAgeRuntimeViewportFrame(
            $"Mesh:{mesh.Name}", 0, 0, width, height, runtimeCamera, [runtimeCamera], [baseRenderable], 0,
            new RekallAgeRuntimeViewportOverlay(false, 0), []);
        var styled = RekallAgeStudioViewportStyleAdapter.Apply(baseFrame, style);
        var overlays = new List<RekallAgeRuntimeViewportRenderable>();
        overlays.AddRange(RekallAgeStudioViewportOverlayRenderables.CreateGrid());
        overlays.AddRange(EditOverlays(mesh, activeDomain, selectedIds));
        if (activeDomain == RekallAgeGeometryDomain.Point && selectedIds.Count > 0)
        {
            var selectedPositions = mesh.Topology.PointIds.Select((id, index) => (id, index))
                .Where(item => selectedIds.Contains(item.id)).Select(item => mesh.Topology.Positions[item.index]).ToArray();
            if (selectedPositions.Length > 0)
            {
                var x = selectedPositions.Average(point => point.X);
                var y = selectedPositions.Average(point => point.Y);
                var z = selectedPositions.Average(point => point.Z);
                overlays.AddRange(RekallAgeStudioSceneGizmoRenderables.Create(
                    RekallAgeStudioTransformTool.Move, RekallAgeStudioTransformSpace.World, x, y, z, 0, 0, 0)
                    .Select(item => item with { EntityId = item.EntityId.Replace("__studio_gizmo_", "__studio_mesh_gizmo_", StringComparison.Ordinal) }));
            }
        }
        var frame = styled with { Renderables = styled.Renderables.Concat(overlays).ToArray() };
        return new(frame, Interaction(mesh, frame, preview, activeDomain, selectedIds));
    }

    private static RekallAgeRuntimeViewportGeometryMesh Geometry(RekallAgeMeshAsset mesh)
    {
        var vertices = mesh.Topology.Positions.Select(point =>
            new RekallAgeRuntimeViewportGeometryVertex(point.X, point.Y, point.Z)).ToArray();
        var indices = new List<uint>();
        for (var face = 0; face < mesh.Topology.FaceIds.Count; face++)
        {
            var start = mesh.Topology.FaceOffsets[face];
            var count = mesh.Topology.FaceOffsets[face + 1] - start;
            for (var corner = 1; corner + 1 < count; corner++)
            {
                indices.Add(checked((uint)mesh.Topology.CornerPointIndices[start]));
                indices.Add(checked((uint)mesh.Topology.CornerPointIndices[start + corner]));
                indices.Add(checked((uint)mesh.Topology.CornerPointIndices[start + corner + 1]));
            }
        }
        return new(vertices, indices);
    }

    private static RekallAgeRuntimeViewportCamera Camera(
        RekallAgeMeshAsset mesh, RekallAgeStudioViewportCamera camera, int width, int height)
    {
        var positions = mesh.Topology.Positions;
        var center = new Vector3(
            (float)((positions.Min(point => point.X) + positions.Max(point => point.X)) * 0.5),
            (float)((positions.Min(point => point.Y) + positions.Max(point => point.Y)) * 0.5),
            (float)((positions.Min(point => point.Z) + positions.Max(point => point.Z)) * 0.5));
        var extent = Math.Max(1, Math.Max(
            positions.Max(point => point.X) - positions.Min(point => point.X),
            Math.Max(positions.Max(point => point.Y) - positions.Min(point => point.Y),
                positions.Max(point => point.Z) - positions.Min(point => point.Z))));
        return Camera(center, extent, camera, width, height);
    }

    private static RekallAgeRuntimeViewportCamera Camera(
        Vector3 center, double extent, RekallAgeStudioViewportCamera camera, int width, int height)
    {
        // Identity is the editor's useful three-quarter view, matching the legacy
        // axonometric framing and Blender's default readability. Camera deltas orbit
        // relative to this basis; a zero pitch must never collapse the XZ grid edge-on.
        const double basePitchDegrees = 28;
        const double baseYawDegrees = 225;
        var pitch = basePitchDegrees + camera.Pitch * 180 / Math.PI;
        var yaw = baseYawDegrees + camera.Yaw * 180 / Math.PI;
        var rx = pitch * Math.PI / 180;
        var ry = yaw * Math.PI / 180;
        var forward = new Vector3(
            (float)(Math.Sin(ry) * Math.Cos(rx)),
            (float)-Math.Sin(rx),
            (float)(Math.Cos(ry) * Math.Cos(rx)));
        var distance = extent * 3 / Math.Max(0.05, camera.Zoom);
        var eye = center - forward * (float)distance;
        var panScale = extent / Math.Max(64, Math.Min(width, height));
        eye.X -= (float)(camera.PanX * panScale);
        eye.Y += (float)(camera.PanY * panScale);
        return new("__studio_mesh_camera", "Mesh Camera", "Camera3D", true,
            eye.X, eye.Y, eye.Z, pitch, yaw, 0,
            camera.Orthographic ? "orthographic" : "perspective",
            50, Math.Max(0.1, extent * 1.8 / Math.Max(0.05, camera.Zoom)), 0.01, Math.Max(100, extent * 20), "#0c1016");
    }

    private static IReadOnlyList<RekallAgeRuntimeViewportRenderable> EditOverlays(
        RekallAgeMeshAsset mesh, RekallAgeGeometryDomain domain, IReadOnlyCollection<ulong> selectedIds)
    {
        var result = new List<RekallAgeRuntimeViewportRenderable>();
        if (domain is RekallAgeGeometryDomain.Point)
        {
            foreach (var (id, index) in mesh.Topology.PointIds.Select((id, index) => (id, index)))
                result.Add(Marker($"__studio_edit_{(selectedIds.Contains(id) ? "selected_" : "")}point_{id}", mesh.Topology.Positions[index], selectedIds.Contains(id), "#d3deea"));
        }
        else if (domain is RekallAgeGeometryDomain.Corner)
        {
            foreach (var (id, index) in mesh.Topology.CornerIds.Select((id, index) => (id, index)))
                result.Add(Marker($"__studio_edit_{(selectedIds.Contains(id) ? "selected_" : "")}corner_{id}", mesh.Topology.Positions[mesh.Topology.CornerPointIndices[index]], selectedIds.Contains(id), "#b77eff"));
        }
        else if (domain is RekallAgeGeometryDomain.Edge)
        {
            foreach (var (id, index) in mesh.Topology.EdgeIds.Select((id, index) => (id, index)))
            {
                var edge = mesh.Topology.EdgePointIndices[index];
                result.Add(Lines($"__studio_edit_{(selectedIds.Contains(id) ? "selected_" : "")}edge_{id}",
                    [Segment(mesh.Topology.Positions[edge.A], mesh.Topology.Positions[edge.B])], selectedIds.Contains(id), "#97a9bd"));
            }
        }
        else if (domain is RekallAgeGeometryDomain.Face)
        {
            for (var face = 0; face < mesh.Topology.FaceIds.Count; face++)
            {
                var id = mesh.Topology.FaceIds[face];
                if (!selectedIds.Contains(id)) continue;
                var start = mesh.Topology.FaceOffsets[face];
                var count = mesh.Topology.FaceOffsets[face + 1] - start;
                var segments = Enumerable.Range(0, count).Select(offset =>
                {
                    var a = mesh.Topology.Positions[mesh.Topology.CornerPointIndices[start + offset]];
                    var b = mesh.Topology.Positions[mesh.Topology.CornerPointIndices[start + (offset + 1) % count]];
                    return Segment(a, b);
                }).ToArray();
                result.Add(Lines($"__studio_edit_selected_face_{id}", segments, true, SelectionColor));
            }
        }
        return result;
    }

    private static RekallAgeRuntimeViewportRenderable Marker(string id, RekallAgeGeometryVector3 point, bool selected, string color) =>
        new(id, id, "mesh", null, point.X, point.Y, point.Z, int.MaxValue - 20,
            Variant: "sphere", ScaleX: selected ? 0.12 : 0.075, ScaleY: selected ? 0.12 : 0.075, ScaleZ: selected ? 0.12 : 0.075,
            MaterialColor: selected ? SelectionColor : color, EmissiveColor: selected ? SelectionColor : color,
            EmissiveStrength: selected ? 4 : 1, Layer: "studio-editor") { CastShadows = false, ReceiveShadows = false };

    private static RekallAgeRuntimeViewportRenderable Lines(
        string id, IReadOnlyList<RekallAgeRuntimeViewportLineSegment> segments, bool selected, string color) =>
        new(id, id, "mesh", null, 0, 0, 0, int.MaxValue - 30,
            MaterialColor: selected ? SelectionColor : color, EmissiveColor: selected ? SelectionColor : color,
            EmissiveStrength: selected ? 4 : 1,
            LineSegments: new(segments, selected ? 0.045 : 0.025), Layer: "studio-editor")
        { CastShadows = false, ReceiveShadows = false };

    private static RekallAgeRuntimeViewportLineSegment Segment(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) =>
        new(a.X, a.Y, a.Z, b.X, b.Y, b.Z);

    private static RekallAgeStudioMeshViewportInteractionSnapshot Interaction(
        RekallAgeMeshAsset mesh,
        RekallAgeRuntimeViewportFrame frame,
        bool preview,
        RekallAgeGeometryDomain activeDomain,
        IReadOnlyCollection<ulong> selectedIds)
    {
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);
        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);
        var draw = batch.Draws.First(item => item.EntityId == MeshId);
        var matrix = draw.Model * batch.Frame.SoftwareViewProjection;
        var projected = mesh.Topology.Positions.Select(point => Project(point, matrix, frame.Width, frame.Height)).ToArray();
        var regions = new List<RekallAgeStudioMeshViewportElementRegion>();
        var centers = new Dictionary<(RekallAgeGeometryDomain, ulong), RekallAgeStudioViewportPoint>();
        foreach (var (id, index) in mesh.Topology.PointIds.Select((id, index) => (id, index)))
        {
            var point = projected[index];
            regions.Add(new(RekallAgeGeometryDomain.Point, id, [point.Screen], point.Depth));
            centers[(RekallAgeGeometryDomain.Point, id)] = point.Screen;
        }
        foreach (var (id, index) in mesh.Topology.EdgeIds.Select((id, index) => (id, index)))
        {
            var edge = mesh.Topology.EdgePointIndices[index];
            var a = projected[edge.A]; var b = projected[edge.B];
            regions.Add(new(RekallAgeGeometryDomain.Edge, id, [a.Screen, b.Screen], (a.Depth + b.Depth) * 0.5));
            centers[(RekallAgeGeometryDomain.Edge, id)] = new((a.Screen.X + b.Screen.X) * 0.5, (a.Screen.Y + b.Screen.Y) * 0.5);
        }
        for (var face = 0; face < mesh.Topology.FaceIds.Count; face++)
        {
            var start = mesh.Topology.FaceOffsets[face];
            var count = mesh.Topology.FaceOffsets[face + 1] - start;
            var values = Enumerable.Range(start, count).Select(corner => projected[mesh.Topology.CornerPointIndices[corner]]).ToArray();
            var polygon = values.Select(item => item.Screen).ToArray();
            var id = mesh.Topology.FaceIds[face];
            regions.Add(new(RekallAgeGeometryDomain.Face, id, polygon, values.Average(item => item.Depth)));
            centers[(RekallAgeGeometryDomain.Face, id)] = new(polygon.Average(item => item.X), polygon.Average(item => item.Y));
        }
        foreach (var (id, index) in mesh.Topology.CornerIds.Select((id, index) => (id, index)))
        {
            var point = projected[mesh.Topology.CornerPointIndices[index]];
            regions.Add(new(RekallAgeGeometryDomain.Corner, id, [point.Screen], point.Depth));
            centers[(RekallAgeGeometryDomain.Corner, id)] = point.Screen;
        }
        RekallAgeStudioMeshTransformGizmo? gizmo = null;
        if (activeDomain == RekallAgeGeometryDomain.Point)
        {
            var selected = mesh.Topology.PointIds.Select((id, index) => (id, index))
                .Where(item => selectedIds.Contains(item.id)).Select(item => mesh.Topology.Positions[item.index]).ToArray();
            if (selected.Length > 0)
            {
                var origin3 = new RekallAgeGeometryVector3(
                    selected.Average(point => point.X), selected.Average(point => point.Y), selected.Average(point => point.Z));
                var origin = Project(origin3, matrix, frame.Width, frame.Height).Screen;
                System.Windows.Point End(double x, double y, double z)
                {
                    var projectedAxis = Project(new(origin3.X + x, origin3.Y + y, origin3.Z + z), matrix, frame.Width, frame.Height).Screen;
                    return new(projectedAxis.X, projectedAxis.Y);
                }
                gizmo = new(new(origin.X, origin.Y),
                [
                    new(RekallAgeStudioMeshTransformAxis.X, End(1, 0, 0)),
                    new(RekallAgeStudioMeshTransformAxis.Y, End(0, 1, 0)),
                    new(RekallAgeStudioMeshTransformAxis.Z, End(0, 0, 1))
                ]);
            }
        }
        return new(frame.Width, frame.Height, preview, centers, regions, gizmo);
    }

    private static (RekallAgeStudioViewportPoint Screen, double Depth) Project(
        RekallAgeGeometryVector3 point, Matrix4x4 matrix, int width, int height)
    {
        var clip = Vector4.Transform(new Vector4((float)point.X, (float)point.Y, (float)point.Z, 1), matrix);
        var inverseW = Math.Abs(clip.W) < 0.000001f ? 1 : 1 / clip.W;
        var ndc = new Vector3(clip.X * inverseW, clip.Y * inverseW, clip.Z * inverseW);
        return (new((ndc.X + 1) * width * 0.5, (1 - ndc.Y) * height * 0.5), ndc.Z);
    }
}
