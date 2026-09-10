using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using g3;
using SW2URDF.UI;

namespace SW2URDF.URDFExport
{
    internal static class StlMeshReducer
    {
        internal sealed class Result
        {
            public uint OriginalTriangles;
            public uint FinalTriangles;
            public uint TargetTriangles;
            public long OriginalBytes;
            public long FinalBytes;
            public string Status;
            public string Warning;
        }

        // A conservative working-set allowance (topology, copies, quadrics and trees).
        // The 389k-triangle reference model fits; larger files are still fully validated.
        private const long WorkingSetBudget = 2L * 1024 * 1024 * 1024;
        private const long EstimatedBytesPerTriangle = 2048;
        private const int SmallShellTriangles = 16;
        // Try the exact request first. Only failed guards incur shared conservative
        // fallbacks (at most five), retaining the smallest validated fallback.
        private static readonly double[] FallbackReductions = { 0.90, 0.75, 0.50, 0.25, 0.10 };
        private const double RelativeSampleTolerance = 0.005;
        private const double SharpEdgeAngleDegrees = 30;
        private const double TimeBudgetSeconds = 120;

        internal static Result ReduceFile(string path, double reductionRatio)
        {
            if (!Finite(reductionRatio) || reductionRatio < 0 || reductionRatio > 1)
                throw new ArgumentOutOfRangeException(nameof(reductionRatio));
            path = Path.GetFullPath(path);
            string temporary = null;
            Result result;
            // Deny writers throughout parsing/reduction. Close only for the atomic replace.
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                SourceInfo info = ReadStl(source, null);
                result = new Result
                {
                    OriginalTriangles = info.Count,
                    FinalTriangles = info.Count,
                    TargetTriangles = Math.Max(1U, (uint)Math.Floor(info.Count * (1 - reductionRatio))),
                    OriginalBytes = source.Length,
                    FinalBytes = source.Length,
                    Status = "unchanged",
                    Warning = ""
                };
                if (reductionRatio == 0)
                    return result;
                try
                {
                    if (info.Degenerate || info.HasAttributes)
                        throw new UnsafeMeshException(info.Degenerate && info.HasAttributes
                            ? "Degenerate triangles and per-face attributes require the original STL."
                            : info.Degenerate ? "Degenerate triangles require the original STL."
                            : "Per-face attributes require the original STL.");
                    if ((long)info.Count * EstimatedBytesPerTriangle > WorkingSetBudget)
                        throw new UnsafeMeshException("The mesh exceeds the reduction working-set budget.");

                    var clock = Stopwatch.StartNew();
                    Action check = () =>
                    {
                        if (clock.Elapsed.TotalSeconds > TimeBudgetSeconds)
                            throw new UnsafeMeshException("The cooperative reduction time budget was reached.");
                    };
                    DMesh3 mesh = BuildTopology(source, check);
                    if (!mesh.CheckValidity(false, FailMode.ReturnOnly))
                        throw new UnsafeMeshException("The STL is not an oriented manifold mesh.");
                    check();
                    DMesh3[] shells = MeshConnectedComponents.Separate(mesh);
                    mesh = null;
                    long total = 0;
                    bool limited = false;
                    foreach (DMesh3 shell in shells)
                        total += shell.TriangleCount;
                    if (total != info.Count)
                        throw new UnsafeMeshException("Shell reconstruction did not preserve every input triangle.");

                    for (int i = 0; i < shells.Length; ++i)
                    {
                        check();
                        DMesh3 original = shells[i];
                        int requested = Math.Max(1, (int)Math.Floor(original.TriangleCount * (1 - reductionRatio)));
                        if (original.TriangleCount <= SmallShellTriangles)
                        {
                            limited |= requested < original.TriangleCount;
                            continue;
                        }
                        DMesh3 reduced = ReduceShell(original, requested, reductionRatio, check, clock);
                        shells[i] = reduced;
                        limited |= reduced.TriangleCount > requested;
                        total += reduced.TriangleCount - original.TriangleCount;
                    }
                    if (total >= info.Count)
                    {
                        result.Warning = GuardWarning();
                        return result;
                    }

                    temporary = Path.Combine(Path.GetDirectoryName(path),
                        "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                    WriteBinary(temporary, shells, checked((uint)total), check);
                    using (var candidate = File.OpenRead(temporary))
                    {
                        SourceInfo written = ReadStl(candidate, null);
                        if (written.Count != total || written.Degenerate || candidate.Length >= source.Length)
                            throw new UnsafeMeshException("The serialized candidate is invalid or not smaller.");
                        DMesh3 roundTrip = BuildTopology(candidate, check);
                        var roundTripShells = new MeshConnectedComponents(roundTrip);
                        roundTripShells.FindConnectedT();
                        if (roundTrip.TriangleCount != total || roundTripShells.Count != shells.Length ||
                            !roundTrip.CheckValidity(false, FailMode.ReturnOnly))
                            throw new UnsafeMeshException("Serialized STL topology or shell count changed.");
                        result.FinalBytes = candidate.Length;
                    }
                    check();
                    result.FinalTriangles = checked((uint)total);
                    result.Warning = limited ? GuardWarning() : "";
                }
                catch (Exception ex) when (Recoverable(ex))
                {
                    DeleteTemporary(temporary);
                    return Failure(result, ex);
                }
            }
            try
            {
                // No delete-then-move fallback: unsupported/failed replacement keeps the source.
                File.Replace(temporary, path, null);
                result.Status = "reduced";
                return result;
            }
            catch (Exception ex) when (Recoverable(ex))
            {
                return Failure(result, ex);
            }
            finally
            {
                DeleteTemporary(temporary);
            }
        }

        private static DMesh3 ReduceShell(DMesh3 original, int requested, double reductionRatio, Action check, Stopwatch clock)
        {
            Bounds bounds = GetBounds(original);
            double tolerance = bounds.Diagonal * RelativeSampleTolerance;
            if (!(tolerance > 0) || !Finite(tolerance))
                return original;
            FeatureGraph features = BuildFeatures(original, tolerance, check);
            var referenceTree = new DMeshAABBTree3(original, true);
            DMesh3 best = original;
            bool firstAttempt = true;
            foreach (int target in CandidateTargets(original.TriangleCount, requested, reductionRatio))
            {
                check();
                var candidate = new DMesh3(original);
                MeshConstraints constraints = ConstrainFeatures(candidate, features, check);
                var reducer = new Reducer(candidate)
                {
                    // In g3 1.0.324 this option overrides the constrained collapse position.
                    // Explicit feature targets below protect boundaries without bypassing projection.
                    PreserveBoundaryShape = false,
                    EdgeFlipTolerance = 0,
                    AllowCollapseFixedVertsWithSameSetID = false,
                    Progress = new ProgressCancel(() => clock.Elapsed.TotalSeconds > TimeBudgetSeconds)
                };
                reducer.SetExternalConstraints(constraints);
                reducer.ReduceToTriangleCount(target);
                check();
                // Validate exactly the float coordinates that will be stored in binary STL.
                foreach (int vertex in candidate.VertexIndices())
                {
                    Vector3d p = candidate.GetVertex(vertex);
                    candidate.SetVertex(vertex, new Vector3d((float)p.x, (float)p.y, (float)p.z));
                }
                if (candidate.TriangleCount > 0 && candidate.TriangleCount < best.TriangleCount &&
                    ValidateCandidate(original, candidate, bounds, referenceTree, tolerance, features, constraints, check))
                {
                    best = candidate;
                    if (firstAttempt)
                        return best;
                }
                firstAttempt = false;
            }
            return best;
        }

        private static IEnumerable<int> CandidateTargets(int count, int requested, double reductionRatio)
        {
            int previous = Math.Max(4, requested);
            yield return previous;
            foreach (double reduction in FallbackReductions)
            {
                if (reduction >= reductionRatio)
                    continue;
                int target = Math.Max(4, (int)Math.Floor(count * (1 - reduction)));
                if (target <= previous || target >= count)
                    continue;
                yield return target;
                previous = target;
            }
        }

        private static bool IsFeatureEdge(DMesh3 mesh, int edge)
        {
            double sharpDot = Math.Cos(SharpEdgeAngleDegrees * Math.PI / 180);
            Index2i adjacent = mesh.GetEdgeT(edge);
            return adjacent.b < 0 || mesh.GetTriNormal(adjacent.a).Dot(mesh.GetTriNormal(adjacent.b)) < sharpDot;
        }

        private sealed class FeatureGraph
        {
            public readonly List<FeatureChain> Chains = new List<FeatureChain>();
            public int BoundaryLoops;
        }

        private sealed class FeatureChain
        {
            public List<int> Vertices;
            public bool Boundary;
            public FeatureSegment Target;
        }

        // A separate target per chain prevents shortcuts between different creases/loops.
        // FixedSetID alone permits unconstrained QEM positions, including off-curve ones.
        private sealed class FeatureSegment : IProjectionTarget
        {
            public readonly Vector3d Start, End, Direction;
            public readonly double LengthSquared, EpsilonSquared;
            private readonly double tolerance;

            public FeatureSegment(Vector3d start, Vector3d end, double tolerance)
            {
                Start = start;
                End = end;
                Direction = end - start;
                LengthSquared = Direction.LengthSquared;
                this.tolerance = tolerance;
                double epsilon = Math.Min(Math.Sqrt(LengthSquared) * 1e-7, tolerance * 1e-3);
                EpsilonSquared = epsilon * epsilon;
            }

            public double Parameter(Vector3d point) => (point - Start).Dot(Direction) / LengthSquared;

            public Vector3d Project(Vector3d point, int identifier = -1)
            {
                double t = Math.Max(0, Math.Min(1, Parameter(point)));
                return t == 0 ? Start : t == 1 ? End : Start + t * Direction;
            }

            public bool Contains(Vector3d point) => Finite(point) &&
                (point - Project(point)).LengthSquared <= EpsilonSquared;

            public bool ContainsSerialized(Vector3d point)
            {
                // IEEE binary32 rounding: |fl(x)-x| <= |fl(x)|/(2^24-1), with
                // half a subnormal ULP as the absolute floor. Input chain detection
                // stays strict; only the already-quantized output gets this allowance.
                double roundoff = point.Length / 16777215.0 + Math.Sqrt(3) * ((double)float.Epsilon * 0.5);
                double epsilon = Math.Min(tolerance, Math.Sqrt(EpsilonSquared) + roundoff);
                return Finite(point) && (point - Project(point)).LengthSquared <= epsilon * epsilon;
            }
        }

        private static void AddNeighbor(Dictionary<int, List<int>> graph, int vertex, int neighbor)
        {
            List<int> neighbors;
            if (!graph.TryGetValue(vertex, out neighbors))
                graph.Add(vertex, neighbors = new List<int>(2));
            neighbors.Add(neighbor);
        }

        private static FeatureGraph BuildFeatures(DMesh3 mesh, double tolerance, Action check)
        {
            var result = new FeatureGraph { BoundaryLoops = CountBoundaryLoops(mesh, check) };
            if (result.BoundaryLoops < 0)
                throw new UnsafeMeshException("The boundary is not a set of manifold loops; original retained.");
            var adjacency = new Dictionary<int, List<int>>();
            var edges = new HashSet<int>();
            foreach (int edge in mesh.EdgeIndices())
            {
                check();
                if (!IsFeatureEdge(mesh, edge))
                    continue;
                edges.Add(edge);
                Index2i v = mesh.GetEdgeV(edge);
                AddNeighbor(adjacency, v.a, v.b);
                AddNeighbor(adjacency, v.b, v.a);
            }
            var pins = new HashSet<int>();
            foreach (var pair in adjacency)
            {
                check();
                List<int> n = pair.Value;
                if (n.Count != 2)
                {
                    pins.Add(pair.Key);
                    continue;
                }
                Vector3d p = mesh.GetVertex(pair.Key);
                Vector3d a = (mesh.GetVertex(n[0]) - p).Normalized;
                Vector3d b = (mesh.GetVertex(n[1]) - p).Normalized;
                // Only redundant, nearly collinear vertices qualify. Curved chains stay pinned.
                if (a.Dot(b) >= 0 || a.Cross(b).LengthSquared > 1e-12 ||
                    mesh.IsBoundaryEdge(mesh.FindEdge(pair.Key, n[0])) !=
                    mesh.IsBoundaryEdge(mesh.FindEdge(pair.Key, n[1])))
                    pins.Add(pair.Key);
            }
            var visited = new HashSet<int>();
            foreach (int pin in pins)
                foreach (int neighbor in adjacency[pin])
                {
                    int edge = mesh.FindEdge(pin, neighbor);
                    if (!visited.Add(edge))
                        continue;
                    var vertices = new List<int> { pin, neighbor };
                    int previous = pin, current = neighbor;
                    while (!pins.Contains(current))
                    {
                        check();
                        List<int> next = adjacency[current];
                        int vertex = next[0] == previous ? next[1] : next[0];
                        if (!visited.Add(mesh.FindEdge(current, vertex)))
                            throw new UnsafeMeshException("The feature chain could not be reconstructed; original retained.");
                        vertices.Add(vertex);
                        previous = current;
                        current = vertex;
                    }
                    AddFeatureChain(result, mesh, vertices, tolerance, check);
                }
            // A cycle without a true corner cannot be a straight span. Keep it exactly.
            foreach (int edge in edges)
                if (!visited.Contains(edge))
                {
                    check();
                    Index2i v = mesh.GetEdgeV(edge);
                    AddFeatureChain(result, mesh, new List<int> { v.a, v.b }, tolerance, check);
                }
            return result;
        }

        private static void AddFeatureChain(FeatureGraph graph, DMesh3 mesh, List<int> vertices,
            double tolerance, Action check)
        {
            var target = new FeatureSegment(mesh.GetVertex(vertices[0]),
                mesh.GetVertex(vertices[vertices.Count - 1]), tolerance);
            bool straight = target.LengthSquared > 0;
            double previous = -1;
            foreach (int vertex in vertices)
            {
                check();
                Vector3d p = mesh.GetVertex(vertex);
                double t = target.Parameter(p);
                straight &= target.Contains(p) && t > previous;
                previous = t;
            }
            if (!straight && vertices.Count > 2)
            {
                // Local near-collinearity must not accumulate into an appreciably bent curve.
                for (int i = 1; i < vertices.Count; ++i)
                    AddFeatureChain(graph, mesh, new List<int> { vertices[i - 1], vertices[i] }, tolerance, check);
                return;
            }
            graph.Chains.Add(new FeatureChain
            {
                Vertices = vertices,
                Boundary = mesh.IsBoundaryEdge(mesh.FindEdge(vertices[0], vertices[1])),
                Target = target
            });
        }

        private static MeshConstraints ConstrainFeatures(DMesh3 mesh, FeatureGraph features, Action check)
        {
            var constraints = new MeshConstraints();
            for (int i = 0; i < features.Chains.Count; ++i)
            {
                check();
                FeatureChain chain = features.Chains[i];
                // Verified against 1.0.324: pins span endpoints, projects interiors, tags edges.
                // Pinned-to-target collapses are disallowed, leaving at least one interior vertex.
                MeshConstraintUtil.ConstrainVtxSpanTo(constraints, mesh, chain.Vertices, chain.Target, i);
            }
            return constraints;
        }

        private static int CountBoundaryLoops(DMesh3 mesh, Action check)
        {
            var adjacency = new Dictionary<int, List<int>>();
            foreach (int edge in mesh.BoundaryEdgeIndices())
            {
                check();
                Index2i v = mesh.GetEdgeV(edge);
                AddNeighbor(adjacency, v.a, v.b);
                AddNeighbor(adjacency, v.b, v.a);
            }
            var visited = new HashSet<int>();
            var pending = new Stack<int>();
            int loops = 0;
            foreach (var pair in adjacency)
            {
                if (pair.Value.Count != 2)
                    return -1;
                if (!visited.Add(pair.Key))
                    continue;
                ++loops;
                pending.Push(pair.Key);
                while (pending.Count > 0)
                {
                    check();
                    foreach (int neighbor in adjacency[pending.Pop()])
                        if (visited.Add(neighbor))
                            pending.Push(neighbor);
                }
            }
            return loops;
        }

        private static bool ValidateFeatures(DMesh3 original, DMesh3 candidate, FeatureGraph features,
            MeshConstraints constraints, Action check)
        {
            if (CountBoundaryLoops(candidate, check) != features.BoundaryLoops ||
                candidate.VertexCount - candidate.EdgeCount + candidate.TriangleCount !=
                original.VertexCount - original.EdgeCount + original.TriangleCount)
                return false;
            var chains = new List<int>[features.Chains.Count];
            // One edge pass, not FindConstrainedEdgesBySetID once per chain (quadratic).
            foreach (int edge in candidate.EdgeIndices())
            {
                check();
                int id = constraints.GetEdgeConstraint(edge).TrackingSetID;
                if (id < 0)
                {
                    // Every boundary must still belong to an original chain. Dihedral angles,
                    // however, change with triangulation: 30 degrees identifies INPUT creases,
                    // not an invariant of their adjacent output faces. The tracked curve stays
                    // protected even if its output dihedral crosses that threshold.
                    if (candidate.IsBoundaryEdge(edge))
                        return false;
                    continue;
                }
                if (id >= chains.Length ||
                    candidate.IsBoundaryEdge(edge) != features.Chains[id].Boundary)
                    return false;
                if (chains[id] == null)
                    chains[id] = new List<int>();
                chains[id].Add(edge);
            }
            for (int i = 0; i < chains.Length; ++i)
            {
                check();
                if (chains[i] == null)
                    return false;
                FeatureChain chain = features.Chains[i];
                int start = chain.Vertices[0], end = chain.Vertices[chain.Vertices.Count - 1];
                if (!candidate.IsVertex(start) || !candidate.IsVertex(end) ||
                    candidate.GetVertex(start) != chain.Target.Start || candidate.GetVertex(end) != chain.Target.End)
                    return false;
                var adjacency = new Dictionary<int, List<int>>();
                foreach (int edge in chains[i])
                {
                    check();
                    Index2i v = candidate.GetEdgeV(edge);
                    AddNeighbor(adjacency, v.a, v.b);
                    AddNeighbor(adjacency, v.b, v.a);
                }
                foreach (var pair in adjacency)
                    if (pair.Value.Count != (pair.Key == start || pair.Key == end ? 1 : 2) ||
                        !chain.Target.ContainsSerialized(candidate.GetVertex(pair.Key)))
                        return false;
                // A connected, strictly ordered span covers the entire original feature curve.
                // Per-chain identity also prevents substituting a nearby hole or crease.
                int current = start, previous = -1, traversed = 0;
                while (current != end)
                {
                    check();
                    List<int> next;
                    if (!adjacency.TryGetValue(current, out next) || ++traversed > chains[i].Count)
                        return false;
                    int vertex = next[0] == previous ? (next.Count == 2 ? next[1] : -1) : next[0];
                    if (vertex < 0 || chain.Target.Parameter(candidate.GetVertex(vertex)) <=
                        chain.Target.Parameter(candidate.GetVertex(current)))
                        return false;
                    previous = current;
                    current = vertex;
                }
                if (traversed != chains[i].Count)
                    return false;
            }
            return true;
        }

        private static bool ValidateCandidate(DMesh3 original, DMesh3 candidate, Bounds bounds,
            DMeshAABBTree3 referenceTree, double tolerance, FeatureGraph features, MeshConstraints constraints, Action check)
        {
            if (!candidate.CheckValidity(false, FailMode.ReturnOnly))
                return false;
            var components = new MeshConnectedComponents(candidate);
            components.FindConnectedT();
            if (components.Count != 1)
                return false;
            Bounds actual = GetBounds(candidate);
            for (int axis = 0; axis < 3; ++axis)
                if (!Finite(actual.Min[axis]) || !Finite(actual.Max[axis]) ||
                    Math.Abs(actual.Min[axis] - bounds.Min[axis]) > tolerance ||
                    Math.Abs(actual.Max[axis] - bounds.Max[axis]) > tolerance)
                    return false;
            // IDs are stable under edge collapse. Surviving faces must retain orientation
            // relative to the original too, including after float quantization.
            foreach (int triangle in candidate.TriangleIndices())
            {
                check();
                Vector3d a, b, c, oa, ob, oc;
                Vertices(candidate, triangle, out a, out b, out c);
                Vertices(original, triangle, out oa, out ob, out oc);
                Vector3d n = (b - a).Cross(c - a);
                if (!Finite(a) || !Finite(b) || !Finite(c) || !(n.LengthSquared > 0) ||
                    n.Dot((ob - oa).Cross(oc - oa)) <= 0)
                    return false;
            }
            if (!ValidateFeatures(original, candidate, features, constraints, check))
                return false;
            check();
            var candidateTree = new DMeshAABBTree3(candidate, true);
            // Seven samples per face, in BOTH directions and within the same shell.
            // This is a sampled-distance guard, NOT a Hausdorff-distance guarantee.
            return SamplesWithin(original, candidateTree, tolerance, check) &&
                SamplesWithin(candidate, referenceTree, tolerance, check);
        }

        private static bool SamplesWithin(DMesh3 mesh, DMeshAABBTree3 target, double tolerance, Action check)
        {
            double squared = tolerance * tolerance;
            foreach (int triangle in mesh.TriangleIndices())
            {
                check();
                Vector3d a, b, c;
                Vertices(mesh, triangle, out a, out b, out c);
                if (!Near(a, target, squared) || !Near(b, target, squared) || !Near(c, target, squared) ||
                    !Near((a + b) * 0.5, target, squared) || !Near((a + c) * 0.5, target, squared) ||
                    !Near((b + c) * 0.5, target, squared) || !Near((a + b + c) / 3, target, squared))
                    return false;
            }
            return true;
        }

        private static bool Near(Vector3d p, DMeshAABBTree3 tree, double squared)
        {
            double distance;
            int triangle = tree.FindNearestTriangle(p, out distance);
            return triangle >= 0 && Finite(distance) && distance <= squared;
        }

        private static DMesh3 BuildTopology(Stream source, Action check)
        {
            var mesh = new DMesh3(false);
            var vertices = new Dictionary<Vector3d, int>();
            ReadStl(source, (a, b, c) =>
            {
                check();
                int ia = Vertex(mesh, vertices, a);
                int ib = Vertex(mesh, vertices, b);
                int ic = Vertex(mesh, vertices, c);
                if (mesh.AppendTriangle(ia, ib, ic) < 0)
                    throw new UnsafeMeshException("A duplicate or non-manifold triangle was rejected; original retained.");
            });
            return mesh;
        }

        private static int Vertex(DMesh3 mesh, Dictionary<Vector3d, int> vertices, Vector3d p)
        {
            int id;
            // Exact STL float equality only: never weld nearby but separate thin walls.
            if (!vertices.TryGetValue(p, out id))
            {
                id = mesh.AppendVertex(p);
                vertices.Add(p, id);
            }
            return id;
        }

        private sealed class SourceInfo
        {
            public uint Count;
            public bool Degenerate;
            public bool HasAttributes;
        }

        private static SourceInfo ReadStl(Stream stream, Action<Vector3d, Vector3d, Vector3d> triangle)
        {
            stream.Position = 0;
            var info = new SourceInfo();
            if (stream.Length >= 84)
            {
                using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
                {
                    stream.Position = 80;
                    uint count = reader.ReadUInt32();
                    if (84L + 50L * count == stream.Length)
                    {
                        for (uint i = 0; i < count; ++i)
                        {
                            ReadVector(reader);
                            Vector3d a = ReadVector(reader), b = ReadVector(reader), c = ReadVector(reader);
                            info.HasAttributes |= reader.ReadUInt16() != 0;
                            Accept(info, a, b, c, triangle);
                        }
                        if (info.Count == 0)
                            throw new InvalidDataException("STL contains no triangles.");
                        return info;
                    }
                }
            }
            stream.Position = 0;
            // Strict grammar prevents truncated binary/ASCII from becoming partial valid meshes.
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, true))
            {
                try
                {
                    string line;
                    while ((line = Line(reader)) != null)
                    {
                        if (!Keyword(line, "solid"))
                            throw new InvalidDataException("Invalid STL header or binary triangle count.");
                        while (true)
                        {
                            line = RequiredLine(reader);
                            if (Keyword(line, "endsolid"))
                                break;
                            string[] normal = Tokens(line);
                            if (normal.Length != 5 || normal[0] != "facet" || normal[1] != "normal")
                                throw new InvalidDataException("Expected STL facet normal.");
                            Number(normal[2]); Number(normal[3]); Number(normal[4]);
                            Expect(reader, "outer loop");
                            Vector3d a = TextVertex(reader), b = TextVertex(reader), c = TextVertex(reader);
                            Expect(reader, "endloop");
                            Expect(reader, "endfacet");
                            Accept(info, a, b, c, triangle);
                        }
                    }
                }
                catch (DecoderFallbackException ex)
                {
                    throw new InvalidDataException("Invalid STL encoding or binary length.", ex);
                }
            }
            if (info.Count == 0)
                throw new InvalidDataException("STL contains no triangles.");
            return info;
        }

        private static void Accept(SourceInfo info, Vector3d a, Vector3d b, Vector3d c,
            Action<Vector3d, Vector3d, Vector3d> triangle)
        {
            if (info.Count == uint.MaxValue)
                throw new InvalidDataException("STL triangle count overflow.");
            ++info.Count;
            info.Degenerate |= !((b - a).Cross(c - a).LengthSquared > 0);
            if (triangle != null)
                triangle(a, b, c);
        }

        private static Vector3d ReadVector(BinaryReader reader)
        {
            var p = new Vector3d(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            if (!Finite(p))
                throw new InvalidDataException("STL contains non-finite coordinates or normals.");
            return p;
        }

        private static Vector3d TextVertex(StreamReader reader)
        {
            string[] tokens = Tokens(RequiredLine(reader));
            if (tokens.Length != 4 || tokens[0] != "vertex")
                throw new InvalidDataException("Expected three STL vertex coordinates.");
            return new Vector3d(Number(tokens[1]), Number(tokens[2]), Number(tokens[3]));
        }

        private static float Number(string text)
        {
            float value;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !Finite(value))
                throw new InvalidDataException("STL contains invalid or non-finite numbers.");
            return value;
        }

        private static string[] Tokens(string line)
        {
            return line.ToLowerInvariant().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool Keyword(string line, string word)
        {
            return line.Equals(word, StringComparison.OrdinalIgnoreCase) ||
                (line.Length > word.Length && line.StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
                    char.IsWhiteSpace(line[word.Length]));
        }

        private static void Expect(StreamReader reader, string text)
        {
            if (string.Join(" ", Tokens(RequiredLine(reader))) != text)
                throw new InvalidDataException("Expected STL " + text + ".");
        }

        private static string RequiredLine(StreamReader reader)
        {
            return Line(reader) ?? throw new InvalidDataException("Truncated ASCII STL.");
        }

        private static string Line(StreamReader reader)
        {
            // Bound malformed ASCII line allocation without imposing a small file-size cap.
            while (true)
            {
                var text = new StringBuilder();
                int c;
                while ((c = reader.Read()) >= 0 && c != '\n')
                {
                    if (text.Length >= 4096)
                        throw new InvalidDataException("STL line is too long.");
                    text.Append((char)c);
                }
                string line = text.ToString().Trim();
                if (line.Length != 0)
                    return line;
                if (c < 0)
                    return null;
            }
        }

        private static void WriteBinary(string path, DMesh3[] shells, uint count, Action check)
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(file, Encoding.ASCII, true))
            {
                writer.Write(new byte[80]);
                writer.Write(count);
                foreach (DMesh3 shell in shells)
                    foreach (int triangle in shell.TriangleIndices())
                    {
                        check();
                        Vector3d a, b, c;
                        Vertices(shell, triangle, out a, out b, out c);
                        Vector3d n = (b - a).Cross(c - a).Normalized;
                        WriteVector(writer, n);
                        WriteVector(writer, a); WriteVector(writer, b); WriteVector(writer, c);
                        writer.Write((ushort)0);
                    }
                writer.Flush();
                file.Flush(true);
            }
        }

        private static void WriteVector(BinaryWriter writer, Vector3d p)
        {
            writer.Write((float)p.x); writer.Write((float)p.y); writer.Write((float)p.z);
        }

        private static void Vertices(DMesh3 mesh, int triangle, out Vector3d a, out Vector3d b, out Vector3d c)
        {
            Index3i t = mesh.GetTriangle(triangle);
            a = mesh.GetVertex(t.a); b = mesh.GetVertex(t.b); c = mesh.GetVertex(t.c);
        }

        private sealed class Bounds
        {
            public readonly double[] Min = { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
            public readonly double[] Max = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            public double Diagonal => new Vector3d(Max[0] - Min[0], Max[1] - Min[1], Max[2] - Min[2]).Length;
        }

        private static Bounds GetBounds(DMesh3 mesh)
        {
            var bounds = new Bounds();
            foreach (int id in mesh.VertexIndices())
            {
                Vector3d p = mesh.GetVertex(id);
                for (int axis = 0; axis < 3; ++axis)
                {
                    bounds.Min[axis] = Math.Min(bounds.Min[axis], p[axis]);
                    bounds.Max[axis] = Math.Max(bounds.Max[axis], p[axis]);
                }
            }
            return bounds;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Finite(Vector3d p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        private static bool Recoverable(Exception ex) =>
            !(ex is OutOfMemoryException) && !(ex is StackOverflowException) &&
            !(ex is System.Threading.ThreadAbortException) && !(ex is AccessViolationException);

        private static string GuardWarning() => ChineseUiText.Translate(
            "Preserving shape and separate parts limited reduction. Inspect the exported mesh before use.",
            "\u4e3a\u4fdd\u7559\u5f62\u72b6\u548c\u72ec\u7acb\u90e8\u4ef6\uff0c\u5b9e\u9645\u51cf\u9762\u53d7\u5230\u9650\u5236\u3002\u8bf7\u5728\u4f7f\u7528\u524d\u68c0\u67e5\u5bfc\u51fa\u7684\u7f51\u683c\u3002");

        private static Result Failure(Result result, Exception error)
        {
            result.Status = "failed";
            result.FinalTriangles = result.OriginalTriangles;
            result.FinalBytes = result.OriginalBytes;
            result.Warning = ChineseUiText.Translate(
                "Mesh reduction was not applied; the original STL was retained. ",
                "\u672a\u5e94\u7528\u7f51\u683c\u51cf\u9762\uff0c\u5df2\u4fdd\u7559\u539f\u59cb STL\u3002 ") + error.Message;
            return result;
        }

        private static void DeleteTemporary(string path)
        {
            if (path == null)
                return;
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private sealed class UnsafeMeshException : Exception
        {
            public UnsafeMeshException(string message) : base(message) { }
        }
    }
}
