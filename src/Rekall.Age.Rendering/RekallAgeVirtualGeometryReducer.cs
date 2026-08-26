using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public static class RekallAgeVirtualGeometryReducer
{
    public static RekallAgeVulkanSceneMesh Reduce(
        RekallAgeVulkanSceneMesh mesh,
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRuntimeViewportCamera? camera)
    {
        var settings = renderable.VirtualGeometry;
        var sourceTriangles = mesh.Indices.Count / 3;
        if (settings is not { Enabled: true }
            || sourceTriangles <= 1
            || (settings.MaxSelectedTriangles <= 0 && camera is null))
        {
            return mesh;
        }

        if (settings.MaxLodLevel <= 0)
        {
            return mesh with
            {
                VirtualGeometrySourceTriangleCount = sourceTriangles,
                VirtualGeometryBudgetSatisfied = settings.MaxSelectedTriangles <= 0
                    || sourceTriangles <= settings.MaxSelectedTriangles
            };
        }

        if (mesh.SkinBindings.Count > 0 || mesh.MorphTargets.Count > 0)
        {
            return mesh with
            {
                VirtualGeometrySourceTriangleCount = sourceTriangles,
                VirtualGeometryBudgetSatisfied = settings.MaxSelectedTriangles <= 0
                    || sourceTriangles <= settings.MaxSelectedTriangles
            };
        }

        var budgetLevel = ResolveBudgetLevel(sourceTriangles, settings);
        var distanceLevel = ResolveDistanceLodLevel(renderable, camera);
        var requestedLevel = Math.Clamp(
            Math.Max(budgetLevel, distanceLevel),
            0,
            settings.MaxLodLevel);
        if (requestedLevel <= 0)
        {
            return mesh;
        }

        var workingMesh = CompactToReferencedVertices(mesh);
        var connectivity = AnalyzeConnectivity(workingMesh);
        var sourceTopology = AnalyzeGeometricTopology(workingMesh, connectivity.VertexComponents);
        if (sourceTopology.BoundaryEdges == workingMesh.Indices.Count
            && settings.MaxSelectedTriangles > 0
            && sourceTriangles > settings.MaxSelectedTriangles)
        {
            return SelectDisconnectedTriangles(workingMesh, settings.MaxSelectedTriangles) with
            {
                VirtualGeometrySourceTriangleCount = sourceTriangles,
                VirtualGeometryLodLevel = requestedLevel
            };
        }

        var clusterScale = Math.Sqrt(128.0 / Math.Clamp(settings.ClusterTriangleCount, 1, sourceTriangles));
        var baseResolution = Math.Clamp(
            checked((int)Math.Ceiling(Math.Cbrt(workingMesh.Vertices.Count) * 2 * clusterScale)),
            2,
            512);
        var candidates = Enumerable.Range(0, settings.MaxLodLevel + 1)
            .Select(level => new
            {
                Level = level,
                Resolution = Math.Max(2, checked((int)Math.Round(baseResolution / Math.Pow(2, level / 2.0))))
            })
            .Where(candidate => candidate.Level >= distanceLevel)
            .GroupBy(candidate => candidate.Resolution)
            .Select(group => group.OrderBy(candidate => candidate.Level).First())
            .OrderBy(candidate => candidate.Level)
            .ToArray();
        RekallAgeVulkanSceneMesh? bestWithinBudget = null;
        var bestWithinBudgetLevel = 0;
        RekallAgeVulkanSceneMesh? bestAboveBudget = null;
        var bestAboveBudgetLevel = 0;
        foreach (var candidateSpec in candidates)
        {
            var candidate = Cluster(workingMesh, candidateSpec.Resolution, connectivity);
            if (candidate is not null)
            {
                if (candidate.ComponentCount == connectivity.ComponentCount
                    && candidate.Topology.BoundaryEdges <= sourceTopology.BoundaryEdges
                    && candidate.Topology.MaximumEdgeUses <= Math.Max(2, sourceTopology.MaximumEdgeUses))
                {
                    var candidateTriangles = candidate.Mesh.Indices.Count / 3;
                    if (settings.MaxSelectedTriangles <= 0)
                    {
                        if (candidateSpec.Level >= requestedLevel)
                        {
                            if (bestWithinBudget is null)
                            {
                                bestWithinBudget = candidate.Mesh;
                                bestWithinBudgetLevel = candidateSpec.Level;
                            }
                        }
                    }
                    else if (candidateTriangles <= settings.MaxSelectedTriangles)
                    {
                        if (bestWithinBudget is null
                            || candidateTriangles > bestWithinBudget.Indices.Count / 3)
                        {
                            bestWithinBudget = candidate.Mesh;
                            bestWithinBudgetLevel = candidateSpec.Level;
                        }
                    }
                    else if (bestAboveBudget is null
                        || candidateTriangles < bestAboveBudget.Indices.Count / 3)
                    {
                        bestAboveBudget = candidate.Mesh;
                        bestAboveBudgetLevel = candidateSpec.Level;
                    }
                }
            }
        }

        var best = bestWithinBudget ?? bestAboveBudget;
        if (best is null)
        {
            return mesh with
            {
                VirtualGeometrySourceTriangleCount = sourceTriangles,
                VirtualGeometryLodLevel = requestedLevel,
                VirtualGeometryBudgetSatisfied = settings.MaxSelectedTriangles <= 0
                    || sourceTriangles <= settings.MaxSelectedTriangles
            };
        }

        return best with
        {
            VirtualGeometrySourceTriangleCount = sourceTriangles,
            VirtualGeometryLodLevel = bestWithinBudget is null ? bestAboveBudgetLevel : bestWithinBudgetLevel,
            VirtualGeometryBudgetSatisfied = settings.MaxSelectedTriangles <= 0
                || best.Indices.Count / 3 <= settings.MaxSelectedTriangles
        };
    }

    private static ClusterCandidate? Cluster(
        RekallAgeVulkanSceneMesh mesh,
        int resolution,
        Connectivity connectivity)
    {
        if (mesh.Vertices.Count == 0)
        {
            return null;
        }

        var minimumX = mesh.Vertices.Min(vertex => vertex.X);
        var minimumY = mesh.Vertices.Min(vertex => vertex.Y);
        var minimumZ = mesh.Vertices.Min(vertex => vertex.Z);
        var extentX = mesh.Vertices.Max(vertex => vertex.X) - minimumX;
        var extentY = mesh.Vertices.Max(vertex => vertex.Y) - minimumY;
        var extentZ = mesh.Vertices.Max(vertex => vertex.Z) - minimumZ;
        var cells = new (int X, int Y, int Z)[mesh.Vertices.Count];
        for (var index = 0; index < mesh.Vertices.Count; index++)
        {
            var vertex = mesh.Vertices[index];
            cells[index] = (
                Quantize(vertex.X, minimumX, extentX, resolution),
                Quantize(vertex.Y, minimumY, extentY, resolution),
                Quantize(vertex.Z, minimumZ, extentZ, resolution));
        }

        var parents = Enumerable.Range(0, mesh.Vertices.Count).ToArray();
        var firstVertexByPosition = new Dictionary<(int Component, float X, float Y, float Z), int>();
        for (var index = 0; index < mesh.Vertices.Count; index++)
        {
            var vertex = mesh.Vertices[index];
            var position = (connectivity.VertexComponents[index], vertex.X, vertex.Y, vertex.Z);
            if (firstVertexByPosition.TryGetValue(position, out var first))
            {
                Union(first, index);
            }
            else
            {
                firstVertexByPosition[position] = index;
            }
        }

        for (var offset = 0; offset < mesh.Indices.Count; offset += 3)
        {
            var a = checked((int)mesh.Indices[offset]);
            var b = checked((int)mesh.Indices[offset + 1]);
            var c = checked((int)mesh.Indices[offset + 2]);
            UnionWhenCellMatches(a, b);
            UnionWhenCellMatches(b, c);
            UnionWhenCellMatches(c, a);
        }

        var accumulatorByRoot = new Dictionary<int, VertexAccumulator>();
        var accumulatedPositions = new HashSet<(int Root, float X, float Y, float Z)>();
        var sourceToCluster = new int[mesh.Vertices.Count];
        for (var index = 0; index < mesh.Vertices.Count; index++)
        {
            var root = Find(index);
            if (!accumulatorByRoot.TryGetValue(root, out var accumulator))
            {
                accumulator = new VertexAccumulator();
                accumulatorByRoot[root] = accumulator;
            }

            var vertex = mesh.Vertices[index];
            if (accumulatedPositions.Add((root, vertex.X, vertex.Y, vertex.Z)))
            {
                accumulator.Add(vertex);
            }

            sourceToCluster[index] = root;
        }

        var uniqueTriangles = new HashSet<(int Component, int A, int B, int C)>();
        var triangles = new List<(uint A, uint B, uint C, int Component)>();
        for (var offset = 0; offset < mesh.Indices.Count; offset += 3)
        {
            var sourceA = mesh.Indices[offset];
            var sourceB = mesh.Indices[offset + 1];
            var sourceC = mesh.Indices[offset + 2];
            if (sourceA >= mesh.Vertices.Count || sourceB >= mesh.Vertices.Count || sourceC >= mesh.Vertices.Count)
            {
                return null;
            }

            var a = sourceToCluster[sourceA];
            var b = sourceToCluster[sourceB];
            var c = sourceToCluster[sourceC];
            if (a == b || b == c || c == a)
            {
                continue;
            }

            var sorted = new[] { a, b, c };
            Array.Sort(sorted);
            var component = connectivity.TriangleComponents[offset / 3];
            if (uniqueTriangles.Add((component, sorted[0], sorted[1], sorted[2])))
            {
                triangles.Add((sourceA, sourceB, sourceC, component));
            }
        }

        if (triangles.Count == 0 || triangles.Count * 3 >= mesh.Indices.Count)
        {
            return null;
        }

        var usedSourceVertices = triangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Distinct()
            .Order()
            .ToArray();
        var compactIndexBySource = usedSourceVertices
            .Select((source, compact) => (source, compact))
            .ToDictionary(item => item.source, item => checked((uint)item.compact));
        var vertices = usedSourceVertices.Select(source =>
        {
            var original = mesh.Vertices[(int)source];
            var clustered = accumulatorByRoot[sourceToCluster[source]].Build();
            return original with { X = clustered.X, Y = clustered.Y, Z = clustered.Z };
        }).ToArray();
        var indices = triangles.SelectMany(triangle => new[]
        {
            compactIndexBySource[triangle.A],
            compactIndexBySource[triangle.B],
            compactIndexBySource[triangle.C]
        }).ToArray();
        var selectedMesh = mesh with { Vertices = vertices, Indices = indices };
        var selectedVertexComponents = usedSourceVertices
            .Select(source => connectivity.VertexComponents[(int)source])
            .ToArray();
        return new ClusterCandidate(
            selectedMesh,
            AnalyzeGeometricTopology(selectedMesh, selectedVertexComponents),
            triangles.Select(triangle => triangle.Component).Distinct().Count());

        int Find(int item)
        {
            while (parents[item] != item)
            {
                parents[item] = parents[parents[item]];
                item = parents[item];
            }

            return item;
        }

        void UnionWhenCellMatches(int first, int second)
        {
            if (cells[first] != cells[second])
            {
                return;
            }

            Union(first, second);
        }

        void Union(int first, int second)
        {
            var firstRoot = Find(first);
            var secondRoot = Find(second);
            if (firstRoot != secondRoot)
            {
                parents[secondRoot] = firstRoot;
            }
        }
    }

    private static RekallAgeVulkanSceneMesh SelectDisconnectedTriangles(
        RekallAgeVulkanSceneMesh mesh,
        int maximumTriangles)
    {
        var selectedSourceIndices = mesh.Indices.Take(maximumTriangles * 3).ToArray();
        var usedSourceVertices = selectedSourceIndices.Distinct().Order().ToArray();
        var compactBySource = usedSourceVertices
            .Select((source, compact) => (source, compact))
            .ToDictionary(item => item.source, item => checked((uint)item.compact));
        return mesh with
        {
            Vertices = usedSourceVertices.Select(index => mesh.Vertices[(int)index]).ToArray(),
            Indices = selectedSourceIndices.Select(index => compactBySource[index]).ToArray()
        };
    }

    private static RekallAgeVulkanSceneMesh CompactToReferencedVertices(RekallAgeVulkanSceneMesh mesh)
    {
        if (mesh.Indices.Any(index => index >= mesh.Vertices.Count))
        {
            return mesh;
        }

        var referenced = mesh.Indices.Distinct().Order().ToArray();
        if (referenced.Length == mesh.Vertices.Count
            && referenced.Select((value, index) => value == index).All(value => value))
        {
            return mesh;
        }

        var compactBySource = referenced
            .Select((source, compact) => (source, compact))
            .ToDictionary(item => item.source, item => checked((uint)item.compact));
        return mesh with
        {
            Vertices = referenced.Select(index => mesh.Vertices[(int)index]).ToArray(),
            Indices = mesh.Indices.Select(index => compactBySource[index]).ToArray()
        };
    }

    private static int Quantize(float value, float minimum, float extent, int resolution)
    {
        if (!float.IsFinite(value) || !float.IsFinite(minimum) || !float.IsFinite(extent) || extent <= 1e-12f)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Floor((value - minimum) / extent * resolution), 0, resolution - 1);
    }

    private static TopologySummary AnalyzeTopology(IReadOnlyList<uint> indices)
    {
        var edgeUses = new Dictionary<(uint A, uint B), int>();
        for (var offset = 0; offset < indices.Count; offset += 3)
        {
            Add(indices[offset], indices[offset + 1]);
            Add(indices[offset + 1], indices[offset + 2]);
            Add(indices[offset + 2], indices[offset]);
        }

        return new(
            edgeUses.Count(item => item.Value == 1),
            edgeUses.Count == 0 ? 0 : edgeUses.Values.Max());

        void Add(uint first, uint second)
        {
            var edge = first < second ? (first, second) : (second, first);
            edgeUses[edge] = edgeUses.GetValueOrDefault(edge) + 1;
        }
    }

    private static Connectivity AnalyzeConnectivity(RekallAgeVulkanSceneMesh mesh)
    {
        var triangleCount = mesh.Indices.Count / 3;
        var parents = Enumerable.Range(0, triangleCount).ToArray();
        var trianglesByIndexedEdge = new Dictionary<(uint A, uint B), List<int>>();
        var trianglesByGeometricEdge = new Dictionary<(Position A, Position B), List<int>>();
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            var offset = triangle * 3;
            var a = mesh.Indices[offset];
            var b = mesh.Indices[offset + 1];
            var c = mesh.Indices[offset + 2];
            if (a >= mesh.Vertices.Count || b >= mesh.Vertices.Count || c >= mesh.Vertices.Count)
            {
                return new(new int[triangleCount], new int[mesh.Vertices.Count], triangleCount);
            }

            AddIndexed(a, b, triangle);
            AddIndexed(b, c, triangle);
            AddIndexed(c, a, triangle);
            AddGeometric(a, b, triangle);
            AddGeometric(b, c, triangle);
            AddGeometric(c, a, triangle);
        }

        foreach (var connected in trianglesByIndexedEdge.Values)
        {
            UnionAll(connected);
        }
        var indexedRootByTriangle = Enumerable.Range(0, triangleCount).Select(Find).ToArray();
        var geometryByIndexedRoot = new Dictionary<int, HashSet<TriangleGeometry>>();
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            var root = indexedRootByTriangle[triangle];
            if (!geometryByIndexedRoot.TryGetValue(root, out var geometry))
            {
                geometry = [];
                geometryByIndexedRoot[root] = geometry;
            }
            geometry.Add(ReadTriangleGeometry(triangle));
        }
        foreach (var connected in trianglesByGeometricEdge.Values)
        {
            // Exactly two uses is an ordinary render seam unless the indexed
            // components have identical complete geometric triangle sets. The
            // latter are coincident duplicate open components and must remain
            // distinct. Four or more uses can likewise be coincident closed shells.
            if (connected.Count == 2 && !AreCoincidentIndexedComponents(connected[0], connected[1]))
            {
                Union(connected[0], connected[1]);
            }
        }

        var componentByRoot = new Dictionary<int, int>();
        var triangleComponents = new int[triangleCount];
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            var root = Find(triangle);
            if (!componentByRoot.TryGetValue(root, out var component))
            {
                component = componentByRoot.Count;
                componentByRoot[root] = component;
            }
            triangleComponents[triangle] = component;
        }

        var vertexComponents = Enumerable.Repeat(-1, mesh.Vertices.Count).ToArray();
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            var component = triangleComponents[triangle];
            var offset = triangle * 3;
            vertexComponents[(int)mesh.Indices[offset]] = component;
            vertexComponents[(int)mesh.Indices[offset + 1]] = component;
            vertexComponents[(int)mesh.Indices[offset + 2]] = component;
        }

        return new Connectivity(triangleComponents, vertexComponents, componentByRoot.Count);

        int Find(int item)
        {
            while (parents[item] != item)
            {
                parents[item] = parents[parents[item]];
                item = parents[item];
            }
            return item;
        }

        void UnionAll(IReadOnlyList<int> connected)
        {
            for (var index = 1; index < connected.Count; index++)
            {
                var firstRoot = Find(connected[0]);
                var secondRoot = Find(connected[index]);
                if (firstRoot != secondRoot) parents[secondRoot] = firstRoot;
            }
        }

        void Union(int first, int second)
        {
            var firstRoot = Find(first);
            var secondRoot = Find(second);
            if (firstRoot != secondRoot) parents[secondRoot] = firstRoot;
        }

        void AddIndexed(uint first, uint second, int triangle)
        {
            var edge = first < second ? (first, second) : (second, first);
            if (!trianglesByIndexedEdge.TryGetValue(edge, out var connected))
            {
                connected = [];
                trianglesByIndexedEdge[edge] = connected;
            }
            connected.Add(triangle);
        }

        void AddGeometric(uint first, uint second, int triangle)
        {
            var firstVertex = mesh.Vertices[(int)first];
            var secondVertex = mesh.Vertices[(int)second];
            var firstPosition = new Position(firstVertex.X, firstVertex.Y, firstVertex.Z);
            var secondPosition = new Position(secondVertex.X, secondVertex.Y, secondVertex.Z);
            var edge = Compare(firstPosition, secondPosition) <= 0
                ? (firstPosition, secondPosition)
                : (secondPosition, firstPosition);
            if (!trianglesByGeometricEdge.TryGetValue(edge, out var connected))
            {
                connected = [];
                trianglesByGeometricEdge[edge] = connected;
            }
            connected.Add(triangle);
        }

        bool AreCoincidentIndexedComponents(int firstTriangle, int secondTriangle)
        {
            var firstRoot = indexedRootByTriangle[firstTriangle];
            var secondRoot = indexedRootByTriangle[secondTriangle];
            if (firstRoot == secondRoot)
            {
                return false;
            }

            var firstGeometry = geometryByIndexedRoot[firstRoot];
            var secondGeometry = geometryByIndexedRoot[secondRoot];
            return firstGeometry.Count == secondGeometry.Count
                && firstGeometry.SetEquals(secondGeometry);
        }

        TriangleGeometry ReadTriangleGeometry(int triangle)
        {
            var offset = triangle * 3;
            var positions = new[]
            {
                ReadPosition(mesh.Indices[offset]),
                ReadPosition(mesh.Indices[offset + 1]),
                ReadPosition(mesh.Indices[offset + 2])
            };
            Array.Sort(positions, Compare);
            return new TriangleGeometry(positions[0], positions[1], positions[2]);
        }

        Position ReadPosition(uint vertexIndex)
        {
            var vertex = mesh.Vertices[(int)vertexIndex];
            return new Position(vertex.X, vertex.Y, vertex.Z);
        }
    }

    private static TopologySummary AnalyzeGeometricTopology(
        RekallAgeVulkanSceneMesh mesh,
        IReadOnlyList<int> vertexComponents)
    {
        var weldedByPosition = new Dictionary<(int Component, float X, float Y, float Z), uint>();
        var next = 0u;
        var weldedIndices = new uint[mesh.Indices.Count];
        for (var offset = 0; offset < mesh.Indices.Count; offset++)
        {
            var sourceIndex = mesh.Indices[offset];
            if (sourceIndex >= mesh.Vertices.Count)
            {
                return new(int.MaxValue, int.MaxValue);
            }

            var vertex = mesh.Vertices[(int)sourceIndex];
            var position = (vertexComponents[(int)sourceIndex], vertex.X, vertex.Y, vertex.Z);
            if (!weldedByPosition.TryGetValue(position, out var welded))
            {
                welded = next++;
                weldedByPosition[position] = welded;
            }

            weldedIndices[offset] = welded;
        }

        return AnalyzeTopology(weldedIndices);
    }

    private static int Compare(Position first, Position second)
    {
        var x = first.X.CompareTo(second.X);
        if (x != 0) return x;
        var y = first.Y.CompareTo(second.Y);
        return y != 0 ? y : first.Z.CompareTo(second.Z);
    }

    private readonly record struct Position(float X, float Y, float Z);
    private readonly record struct TriangleGeometry(Position A, Position B, Position C);
    private sealed record Connectivity(int[] TriangleComponents, int[] VertexComponents, int ComponentCount);
    private sealed record ClusterCandidate(RekallAgeVulkanSceneMesh Mesh, TopologySummary Topology, int ComponentCount);
    private readonly record struct TopologySummary(int BoundaryEdges, int MaximumEdgeUses);

    private sealed class VertexAccumulator
    {
        private double _x;
        private double _y;
        private double _z;
        private double _normalX;
        private double _normalY;
        private double _normalZ;
        private double _r;
        private double _g;
        private double _b;
        private double _a;
        private double _u;
        private double _v;
        private int _count;

        public void Add(RekallAgeVulkanSceneVertex vertex)
        {
            _x += vertex.X; _y += vertex.Y; _z += vertex.Z;
            _normalX += vertex.NormalX; _normalY += vertex.NormalY; _normalZ += vertex.NormalZ;
            _r += vertex.R; _g += vertex.G; _b += vertex.B; _a += vertex.A;
            _u += vertex.U; _v += vertex.V;
            _count++;
        }

        public RekallAgeVulkanSceneVertex Build()
        {
            var inverse = 1.0 / _count;
            var normalLength = Math.Sqrt(_normalX * _normalX + _normalY * _normalY + _normalZ * _normalZ);
            var normalScale = normalLength > 1e-12 ? 1.0 / normalLength : 0;
            return new(
                (float)(_x * inverse), (float)(_y * inverse), (float)(_z * inverse),
                (float)(_normalX * normalScale), (float)(_normalY * normalScale), (float)(_normalZ * normalScale),
                (float)(_r * inverse), (float)(_g * inverse), (float)(_b * inverse), (float)(_a * inverse),
                (float)(_u * inverse), (float)(_v * inverse));
        }
    }

    private static int ResolveBudgetLevel(
        int sourceTriangles,
        RekallAgeRuntimeViewportVirtualGeometry settings)
    {
        if (settings.MaxSelectedTriangles <= 0 || sourceTriangles <= settings.MaxSelectedTriangles)
        {
            return 0;
        }

        var level = 0;
        var stride = 1;
        while (level < settings.MaxLodLevel
            && (sourceTriangles + stride - 1) / stride > settings.MaxSelectedTriangles)
        {
            level++;
            stride <<= 1;
        }

        return level;
    }

    public static int ResolveDistanceLodLevel(
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRuntimeViewportCamera? camera)
    {
        var settings = renderable.VirtualGeometry;
        if (camera is null || settings is not { Enabled: true } || settings.TargetPixelError <= 0)
        {
            return 0;
        }

        var dx = renderable.X - camera.X;
        var dy = renderable.Y - camera.Y;
        var dz = renderable.Z - camera.Z;
        var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        var distancePerLevel = Math.Max(4, 32 / settings.TargetPixelError);
        return Math.Clamp((int)Math.Floor(distance / distancePerLevel), 0, settings.MaxLodLevel);
    }
}
