using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using g3;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public sealed class TestStlMeshReducer : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "osurdf-reducer-" + Guid.NewGuid().ToString("N"));
        private string MeshPath => Path.Combine(directory, "mesh.stl");

        public TestStlMeshReducer()
        {
            Directory.CreateDirectory(directory);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(0.6)]
        [InlineData(1.0)]
        public void TestSubdividedPlaneActuallyReducesAndPreservesBoundary(double ratio)
        {
            List<Vector3d[]> source = Plane(24, 0);
            Write(source);
            byte[] bytes = File.ReadAllBytes(MeshPath);
            var result = StlMeshReducer.ReduceFile(MeshPath, ratio);
            Assert.Equal((uint)source.Count, result.OriginalTriangles);
            Assert.Equal((long)bytes.Length, result.OriginalBytes);
            Assert.Equal(Math.Max(1U, (uint)Math.Floor(source.Count * (1 - ratio))), result.TargetTriangles);
            Assert.Equal(new FileInfo(MeshPath).Length, result.FinalBytes);
            List<Vector3d[]> actual = Read();
            Assert.Equal((uint)actual.Count, result.FinalTriangles);
            if (ratio == 0)
            {
                Assert.Equal("unchanged", result.Status);
                Assert.Equal(bytes, File.ReadAllBytes(MeshPath));
            }
            else
            {
                Assert.Equal("reduced", result.Status);
                Assert.InRange(result.FinalTriangles, 1U, result.OriginalTriangles - 1);
                Assert.True(result.FinalBytes < result.OriginalBytes);
                Assert.Equal(84L + 50L * result.FinalTriangles, result.FinalBytes);
                if (ratio < 1)
                    Assert.InRange(result.FinalTriangles, result.TargetTriangles - 1, result.TargetTriangles);
                foreach (Vector3d point in source.SelectMany(t => t).Where(p => p.x == 0 || p.x == 1 || p.y == 0 || p.y == 1))
                    Assert.Contains(actual.SelectMany(t => t), p => p == point);
            }
            foreach (Vector3d[] t in actual)
            {
                Assert.True((t[1] - t[0]).Cross(t[2] - t[0]).z > 0);
                foreach (Vector3d p in t)
                {
                    Assert.InRange(p.x, 0, 1);
                    Assert.InRange(p.y, 0, 1);
                    Assert.Equal(0, p.z);
                }
            }
            AssertNoTemporaryFiles();
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(1.0)]
        public void TestClosedSubdividedCubeRemainsClosedAndNonempty(double ratio)
        {
            Write(Cube(8));
            var result = StlMeshReducer.ReduceFile(MeshPath, ratio);
            Assert.Equal("reduced", result.Status);
            Assert.InRange(result.FinalTriangles, 4U, result.OriginalTriangles - 1);
            DMesh3 mesh = Mesh(Read());
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Empty(mesh.BoundaryEdgeIndices());
            Assert.Single(MeshConnectedComponents.Separate(mesh));
            DMesh3 source = Mesh(Cube(8));
            foreach (int edge in source.EdgeIndices())
            {
                Index2i adjacent = source.GetEdgeT(edge);
                if (source.GetTriNormal(adjacent.a).Dot(source.GetTriNormal(adjacent.b)) > 0.5)
                    continue;
                Index2i endpoints = source.GetEdgeV(edge);
                Vector3d a = source.GetVertex(endpoints.a), b = source.GetVertex(endpoints.b);
                Assert.Contains(mesh.EdgeIndices(), id =>
                {
                    Index2i pair = mesh.GetEdgeV(id);
                    Vector3d x = mesh.GetVertex(pair.a), y = mesh.GetVertex(pair.b);
                    return (x == a && y == b) || (x == b && y == a);
                });
            }
            foreach (Vector3d p in Read().SelectMany(t => t))
            {
                Assert.InRange(p.x, -0.009, 1.009);
                Assert.InRange(p.y, -0.009, 1.009);
                Assert.InRange(p.z, -0.009, 1.009);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TestMaximumReductionRetainsBestValidatedFallback(bool curved)
        {
            List<Vector3d[]> source = curved ? Sphere(3) : Cube(8);
            uint best = (uint)source.Count;
            foreach (double ratio in new[] { 0.9, 0.75, 0.5, 0.25, 0.1 })
            {
                Write(source);
                var result = StlMeshReducer.ReduceFile(MeshPath, ratio);
                Assert.NotEqual("failed", result.Status);
                best = Math.Min(best, result.FinalTriangles);
            }
            Write(source);
            var maximum = StlMeshReducer.ReduceFile(MeshPath, 1);
            Assert.NotEqual("failed", maximum.Status);
            Assert.InRange(maximum.FinalTriangles, 1U, best);
        }

        [Fact]
        public void TestCurvedSurfaceGuardIsBidirectionalAndUsesShellScale()
        {
            List<Vector3d[]> sphere = Sphere(3);
            Write(sphere);
            var result = StlMeshReducer.ReduceFile(MeshPath, 1);
            Assert.NotEqual("failed", result.Status);
            Assert.InRange(result.FinalTriangles, 4U, result.OriginalTriangles);
            Assert.False(string.IsNullOrWhiteSpace(result.Warning));
            DMesh3 original = Mesh(sphere.Select(t => t.Select(FloatPoint).ToArray()).ToList());
            DMesh3 final = Mesh(Read());
            Assert.Empty(final.BoundaryEdgeIndices());
            Assert.Single(MeshConnectedComponents.Separate(final));
            double tolerance = Math.Sqrt(12) * 0.005;
            AssertSampleDistances(original, final, tolerance);
            AssertSampleDistances(final, original, tolerance);
        }

        [Fact]
        public void TestAllDisconnectedShellsAndTinyTetrahedronAreRetained()
        {
            List<Vector3d[]> source = Plane(16, 0);
            source.AddRange(Plane(16, 5));
            List<Vector3d[]> tiny = Tetrahedron();
            source.AddRange(tiny);
            Write(source);
            var result = StlMeshReducer.ReduceFile(MeshPath, 1);
            Assert.Equal("reduced", result.Status);
            Assert.False(string.IsNullOrWhiteSpace(result.Warning));
            List<Vector3d[]> actual = Read();
            Assert.Equal(3, MeshConnectedComponents.Separate(Mesh(actual)).Length);
            Assert.Equal(4, actual.Count(t => t.All(p => p.x > 19)));
            foreach (Vector3d[] triangle in tiny)
                Assert.Contains(actual, t => t.SequenceEqual(triangle.Select(FloatPoint)));
        }

        [Fact]
        public void TestTinyShellUnchangedAtMaximumReduction()
        {
            Write(Tetrahedron());
            byte[] original = File.ReadAllBytes(MeshPath);
            var result = StlMeshReducer.ReduceFile(MeshPath, 1);
            Assert.Equal("unchanged", result.Status);
            Assert.Equal(1U, result.TargetTriangles);
            Assert.Equal(4U, result.FinalTriangles);
            Assert.Equal(original, File.ReadAllBytes(MeshPath));
            AssertNoTemporaryFiles();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TestRejectedTopologyNeverSilentlyDropsTriangles(bool duplicate)
        {
            List<Vector3d[]> source = Plane(12, 0);
            if (duplicate)
                source.Add(source[0]);
            else
            {
                // A third face incident on an existing interior edge.
                Vector3d[] first = source[0];
                source.Add(new[] { first[2], first[0], new Vector3d(0, 0, 1) });
            }
            Write(source);
            byte[] original = File.ReadAllBytes(MeshPath);
            var result = StlMeshReducer.ReduceFile(MeshPath, 0.5);
            Assert.Equal("failed", result.Status);
            Assert.Equal((uint)source.Count, result.FinalTriangles);
            Assert.Equal(original, File.ReadAllBytes(MeshPath));
            Assert.False(string.IsNullOrWhiteSpace(result.Warning));
            AssertNoTemporaryFiles();
        }

        [Fact]
        public void TestDegenerateButWellFormedSourceIsPreserved()
        {
            List<Vector3d[]> source = Plane(12, 0);
            source.Add(new[] { Vector3d.Zero, Vector3d.Zero, Vector3d.Zero });
            Write(source);
            byte[] original = File.ReadAllBytes(MeshPath);
            var result = StlMeshReducer.ReduceFile(MeshPath, 0.5);
            Assert.Equal("failed", result.Status);
            Assert.Equal(original, File.ReadAllBytes(MeshPath));
            var zero = StlMeshReducer.ReduceFile(MeshPath, 0);
            Assert.Equal((uint)source.Count, zero.FinalTriangles);
            Assert.Equal(original, File.ReadAllBytes(MeshPath));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(1.0)]
        public void TestTruncatedBinaryThrowsEvenForZeroRatio(double ratio)
        {
            Write(Plane(2, 0));
            using (var file = File.OpenWrite(MeshPath))
                file.SetLength(file.Length - 1);
            byte[] original = File.ReadAllBytes(MeshPath);
            Assert.Throws<InvalidDataException>(() => StlMeshReducer.ReduceFile(MeshPath, ratio));
            Assert.Equal(original, File.ReadAllBytes(MeshPath));
            AssertNoTemporaryFiles();
        }

        [Theory]
        [InlineData("")]
        [InlineData("solid empty\nendsolid empty\n")]
        [InlineData("solid x\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\n")]
        [InlineData("solid x\nfacet normal 0 0 1\nouter loop\nvertex NaN 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid x\n")]
        public void TestCorruptAsciiThrows(string text)
        {
            File.WriteAllText(MeshPath, text);
            byte[] original = File.ReadAllBytes(MeshPath);
            Assert.Throws<InvalidDataException>(() => StlMeshReducer.ReduceFile(MeshPath, 0));
            Assert.Equal(original, File.ReadAllBytes(MeshPath));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void TestNonFiniteBinaryCoordinatesThrow(float value)
        {
            Write(Plane(2, 0));
            using (var file = File.OpenWrite(MeshPath))
            using (var writer = new BinaryWriter(file))
            {
                file.Position = 96;
                writer.Write(value);
            }
            Assert.Throws<InvalidDataException>(() => StlMeshReducer.ReduceFile(MeshPath, 0.5));
        }

        [Fact]
        public void TestAsciiZeroRatioLeavesExactOriginalBytesAndCountsMultipleSolids()
        {
            string facet = "facet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\n";
            File.WriteAllText(MeshPath, "solid a\n" + facet + "endsolid a\nsolid b\n" + facet + "endsolid b\n");
            byte[] original = File.ReadAllBytes(MeshPath);
            var result = StlMeshReducer.ReduceFile(MeshPath, 0);
            Assert.Equal(2U, result.OriginalTriangles);
            Assert.Equal("unchanged", result.Status);
            Assert.Equal(original, File.ReadAllBytes(MeshPath));
        }

        [Fact]
        public void TestAsciiReductionUsesInvariantNumbers()
        {
            var text = new StringBuilder("solid grid\n");
            foreach (Vector3d[] t in Plane(12, 0))
            {
                text.Append("facet normal 0 0 1\nouter loop\n");
                foreach (Vector3d p in t)
                    text.AppendFormat(CultureInfo.InvariantCulture, "vertex {0:R} {1:R} {2:R}\n", (float)p.x, (float)p.y, (float)p.z);
                text.Append("endloop\nendfacet\n");
            }
            text.Append("endsolid grid\n");
            File.WriteAllText(MeshPath, text.ToString());
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.Equal("reduced", StlMeshReducer.ReduceFile(MeshPath, 0.5).Status);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Fact]
        public void TestFailedAtomicReplacementLeavesOriginalAndCleansCandidate()
        {
            Write(Plane(16, 0));
            byte[] original = File.ReadAllBytes(MeshPath);
            using (var locked = new FileStream(MeshPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = StlMeshReducer.ReduceFile(MeshPath, 0.5);
                Assert.Equal("failed", result.Status);
                Assert.Equal(result.OriginalTriangles, result.FinalTriangles);
                Assert.Equal(result.OriginalBytes, result.FinalBytes);
                Assert.False(string.IsNullOrWhiteSpace(result.Warning));
            }
            Assert.Equal(original, File.ReadAllBytes(MeshPath));
            AssertNoTemporaryFiles();
        }

        [Fact]
        public void TestPerFacetAttributesAreNotSilentlyRemoved()
        {
            Write(Plane(12, 0));
            using (var file = File.OpenWrite(MeshPath))
            using (var writer = new BinaryWriter(file))
            {
                file.Position = 132;
                writer.Write((ushort)0x8001);
            }
            byte[] original = File.ReadAllBytes(MeshPath);
            Assert.Equal("failed", StlMeshReducer.ReduceFile(MeshPath, 0.5).Status);
            Assert.Equal(original, File.ReadAllBytes(MeshPath));
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(1.1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void TestInvalidRatioRejectedWithoutChangingFile(double ratio)
        {
            Write(Plane(2, 0));
            byte[] original = File.ReadAllBytes(MeshPath);
            Assert.Throws<ArgumentOutOfRangeException>(() => StlMeshReducer.ReduceFile(MeshPath, ratio));
            Assert.Equal(original, File.ReadAllBytes(MeshPath));
        }

        private static List<Vector3d[]> Plane(int divisions, double offset)
        {
            var triangles = new List<Vector3d[]>();
            for (int x = 0; x < divisions; ++x)
                for (int y = 0; y < divisions; ++y)
                {
                    var a = FloatPoint(new Vector3d(offset + (double)x / divisions, (double)y / divisions, 0));
                    var b = FloatPoint(new Vector3d(offset + (double)(x + 1) / divisions, (double)y / divisions, 0));
                    var c = FloatPoint(new Vector3d(offset + (double)(x + 1) / divisions, (double)(y + 1) / divisions, 0));
                    var d = FloatPoint(new Vector3d(offset + (double)x / divisions, (double)(y + 1) / divisions, 0));
                    triangles.Add(new[] { a, b, c });
                    triangles.Add(new[] { a, c, d });
                }
            return triangles;
        }

        private static List<Vector3d[]> Cube(int divisions)
        {
            var triangles = new List<Vector3d[]>();
            for (int axis = 0; axis < 3; ++axis)
                for (int side = 0; side < 2; ++side)
                    foreach (Vector3d[] planar in Plane(divisions, 0))
                    {
                        Vector3d[] face = planar.Select(p => axis == 0 ? new Vector3d(side, p.x, p.y) :
                            axis == 1 ? new Vector3d(p.x, side, p.y) : new Vector3d(p.x, p.y, side)).ToArray();
                        if ((axis == 1 && side == 1) || (axis != 1 && side == 0))
                            Array.Reverse(face);
                        triangles.Add(face);
                    }
            return triangles;
        }

        private static List<Vector3d[]> Tetrahedron()
        {
            var a = new Vector3d(20, 0, 0);
            var b = new Vector3d(20.001, 0, 0);
            var c = new Vector3d(20, 0.001, 0);
            var d = new Vector3d(20, 0, 0.001);
            return new List<Vector3d[]> { new[] { a, c, b }, new[] { a, b, d }, new[] { a, d, c }, new[] { b, c, d } };
        }

        private static List<Vector3d[]> Sphere(int subdivisions)
        {
            var triangles = new List<Vector3d[]>();
            var ring = new[] { new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0) };
            for (int i = 0; i < 4; ++i)
            {
                triangles.Add(new[] { new Vector3d(0, 0, 1), ring[i], ring[(i + 1) % 4] });
                triangles.Add(new[] { new Vector3d(0, 0, -1), ring[(i + 1) % 4], ring[i] });
            }
            for (int level = 0; level < subdivisions; ++level)
            {
                var split = new List<Vector3d[]>();
                foreach (Vector3d[] t in triangles)
                {
                    Vector3d ab = (t[0] + t[1]).Normalized;
                    Vector3d bc = (t[1] + t[2]).Normalized;
                    Vector3d ca = (t[2] + t[0]).Normalized;
                    split.Add(new[] { t[0], ab, ca });
                    split.Add(new[] { ab, t[1], bc });
                    split.Add(new[] { ca, bc, t[2] });
                    split.Add(new[] { ab, bc, ca });
                }
                triangles = split;
            }
            return triangles;
        }

        private static void AssertSampleDistances(DMesh3 from, DMesh3 to, double tolerance)
        {
            var tree = new DMeshAABBTree3(to, true);
            foreach (int id in from.TriangleIndices())
            {
                Index3i t = from.GetTriangle(id);
                Vector3d a = from.GetVertex(t.a), b = from.GetVertex(t.b), c = from.GetVertex(t.c);
                foreach (Vector3d p in new[] { a, b, c, (a + b) / 2, (b + c) / 2, (a + c) / 2, (a + b + c) / 3 })
                {
                    double distance;
                    Assert.True(tree.FindNearestTriangle(p, out distance) >= 0);
                    Assert.InRange(distance, 0, tolerance * tolerance);
                }
            }
        }

        private void Write(List<Vector3d[]> triangles)
        {
            using (var writer = new BinaryWriter(File.Create(MeshPath)))
            {
                writer.Write(new byte[80]);
                writer.Write((uint)triangles.Count);
                foreach (Vector3d[] t in triangles)
                {
                    for (int i = 0; i < 3; ++i)
                        writer.Write(0f);
                    foreach (Vector3d p in t)
                    {
                        writer.Write((float)p.x); writer.Write((float)p.y); writer.Write((float)p.z);
                    }
                    writer.Write((ushort)0);
                }
            }
        }

        private List<Vector3d[]> Read()
        {
            var triangles = new List<Vector3d[]>();
            using (var reader = new BinaryReader(File.OpenRead(MeshPath)))
            {
                reader.ReadBytes(80);
                uint count = reader.ReadUInt32();
                for (uint i = 0; i < count; ++i)
                {
                    reader.ReadBytes(12);
                    var t = new Vector3d[3];
                    for (int vertex = 0; vertex < 3; ++vertex)
                        t[vertex] = new Vector3d(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    reader.ReadUInt16();
                    triangles.Add(t);
                }
                Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
            }
            return triangles;
        }

        private static DMesh3 Mesh(List<Vector3d[]> triangles)
        {
            var mesh = new DMesh3(false);
            var vertices = new Dictionary<Vector3d, int>();
            foreach (Vector3d[] t in triangles)
            {
                var ids = new int[3];
                for (int i = 0; i < 3; ++i)
                {
                    int id;
                    if (!vertices.TryGetValue(t[i], out id))
                        vertices.Add(t[i], id = mesh.AppendVertex(t[i]));
                    ids[i] = id;
                }
                Assert.True(mesh.AppendTriangle(ids[0], ids[1], ids[2]) >= 0);
            }
            return mesh;
        }

        private static Vector3d FloatPoint(Vector3d p) => new Vector3d((float)p.x, (float)p.y, (float)p.z);
        private void AssertNoTemporaryFiles() => Assert.Single(Directory.GetFiles(directory));
        public void Dispose() => Directory.Delete(directory, true);
    }
}
