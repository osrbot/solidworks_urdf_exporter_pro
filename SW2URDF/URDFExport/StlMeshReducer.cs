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
                        throw new UnsafeMeshException("Degenerate triangles or per-face attributes require the original STL.");
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
            var referenceTree = new DMeshAABBTree3(original, true);
            DMesh3 best = original;
            bool firstAttempt = true;
            foreach (int target in CandidateTargets(original.TriangleCount, requested, reductionRatio))
            {
                check();
                var candidate = new DMesh3(original);
                var constraints = new MeshConstraints();
                // Lock CAD creases as well as open boundaries. Otherwise near-zero-area
                // slivers along a crease can pass double-precision QEM and collapse at STL precision.
                foreach (int edge in ProtectedEdges(candidate))
                {
                    constraints.SetOrUpdateEdgeConstraint(edge, EdgeConstraint.FullyConstrained);
                    Index2i vertices = candidate.GetEdgeV(edge);
                    constraints.SetOrUpdateVertexConstraint(vertices.a, VertexConstraint.Pinned);
                    constraints.SetOrUpdateVertexConstraint(vertices.b, VertexConstraint.Pinned);
                }
                var reducer = new Reducer(candidate)
                {
                    PreserveBoundaryShape = true,
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
                    ValidateCandidate(original, candidate, bounds, referenceTree, tolerance, check))
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

        private static IEnumerable<int> ProtectedEdges(DMesh3 mesh)
        {
            double sharpDot = Math.Cos(SharpEdgeAngleDegrees * Math.PI / 180);
            foreach (int edge in mesh.EdgeIndices())
            {
                Index2i adjacent = mesh.GetEdgeT(edge);
                if (adjacent.b < 0 || mesh.GetTriNormal(adjacent.a).Dot(mesh.GetTriNormal(adjacent.b)) < sharpDot)
                    yield return edge;
            }
        }

        private static bool ValidateCandidate(DMesh3 original, DMesh3 candidate, Bounds bounds,
            DMeshAABBTree3 referenceTree, double tolerance, Action check)
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
            foreach (int edge in ProtectedEdges(original))
            {
                Index2i v = original.GetEdgeV(edge);
                if (!candidate.IsVertex(v.a) || !candidate.IsVertex(v.b) ||
                    candidate.GetVertex(v.a) != original.GetVertex(v.a) ||
                    candidate.GetVertex(v.b) != original.GetVertex(v.b))
                    return false;
                int kept = candidate.FindEdge(v.a, v.b);
                if (kept < 0 || candidate.IsBoundaryEdge(kept) != original.IsBoundaryEdge(edge))
                    return false;
            }
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
