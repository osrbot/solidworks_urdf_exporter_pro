using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Xunit;

namespace SW2URDF.Test
{
    public class TestMeshReductionReporting
    {
        [Theory]
        [InlineData(0.0, "unchanged", 100, 100, "", "PASS")]
        [InlineData(0.5, "reduced", 50, 50, "", "PASS")]
        [InlineData(0.5, "failed", 100, 50, "write failed", "WARN")]
        [InlineData(0.5, "failed", 100, 50, "", "WARN")]
        [InlineData(0.5, "unchanged", 100, 50, "", "WARN")]
        [InlineData(0.5, "reduced", 80, 50, "", "WARN")]
        [InlineData(0.5, "limited", 50, 50, "", "WARN")]
        [InlineData(0.5, "reduced", 50, 50, "shape safeguard", "WARN")]
        public void HealthReflectsMeasuredReductionWithoutFailingExport(
            double ratio, string result, int actual, int target, string warning, string expected)
        {
            var stats = Stats(ratio, result, (uint)actual, (uint)target, warning);
            string health = Health(stats, true, MeshExportFormat.STL);
            Assert.Contains("| STL reduction | " + expected + " |", health);
            Assert.Contains("reduction_warnings=" + (expected == "WARN" ? "1" : "0"), health);
            Assert.DoesNotContain("| FAIL |", health);
        }

        [Fact]
        public void UnrequestedMeshExportAndNonStlRemainSkipped()
        {
            var stats = Stats(1, "failed", 100, 1, "failure");
            Assert.Contains("| SKIP |", Health(stats, false, MeshExportFormat.STL));
            Assert.Contains("| SKIP |", Health(stats, true, MeshExportFormat.THREEDXML));
        }

        [Theory]
        [InlineData(1.0, "reduced", 40, 1, "shape safeguard", true, true)]
        [InlineData(1.0, "unchanged", 100, 1, "shape safeguard", false, true)]
        [InlineData(1.0, "failed", 100, 1, "replacement failed", false, true)]
        [InlineData(0.0, "unchanged", 100, 100, "", true, false)]
        public void CollisionAttemptKeepsCountsAndReasonEvenWhenFallbackIsRequired(
            double ratio, string result, int actual, int target, string warning, bool accepted, bool limited)
        {
            string path = Path.GetTempFileName();
            try
            {
                var stats = Stats(ratio, result, (uint)actual, (uint)target, warning);
                string notes;
                Assert.Equal(accepted, TryCollision(path, ratio, () => stats, out notes));
                Assert.Contains("triangles=100->" + actual, notes);
                Assert.Contains("bytes=5084->" + stats.ActualBytes, notes);
                Assert.Contains("status=" + result, notes);
                Assert.Contains("limited=" + limited, notes);
                Assert.Contains("reason=" + warning, notes);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void CollisionExceptionRetainsFailureReasonWithoutCom()
        {
            string notes;
            Assert.False(TryCollision("unused.stl", 1,
                () => { throw new IOException("SaveAs failed"); }, out notes));
            Assert.Contains("status=failed", notes);
            Assert.Contains("SaveAs failed", notes);
        }

        [Fact]
        public void MissingCollisionFileCannotReportSuccess()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".stl");
            string notes;
            Assert.False(TryCollision(path, 0, () => Stats(0, "unchanged", 100, 100, ""), out notes));
            Assert.Contains("collision STL file missing", notes);
            Assert.Contains("triangles=100->100", notes);
        }

        private static ExportHelper.StlExportStats Stats(
            double ratio, string result, uint actual, uint target, string warning)
        {
            return new ExportHelper.StlExportStats
            {
                ReductionRatio = ratio,
                OriginalTriangles = 100,
                ActualTriangles = actual,
                TargetTriangles = target,
                OriginalBytes = 5084,
                ActualBytes = 84L + 50L * actual,
                ReductionStatus = result,
                ReductionWarning = warning
            };
        }

        private static string Health(ExportHelper.StlExportStats stats, bool exportMeshes, MeshExportFormat format)
        {
            var record = new ExportHelper.MeshExportRecord(
                "base_link", "VisualMesh", "VisualMesh", "visual_mesh_copy", "ok", "STL",
                "visual.stl", "collision.stl", "visual.stl", "collision.stl", true, true,
                stats.ActualBytes, stats.ActualBytes, stats.ActualTriangles, stats.ActualTriangles, stats);
            var builder = new StringBuilder();
            typeof(ExportHelper).GetMethod("AppendStlReductionHealthRow", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { builder, new[] { record }, exportMeshes, format });
            return builder.ToString();
        }

        private static bool TryCollision(string path, double ratio, Func<ExportHelper.StlExportStats> save, out string notes)
        {
            // Inject only the SaveSTL operation so the production decision/notes run without CAD.
            var exporter = (ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));
            object[] arguments = { new Link { Name = "base_link" }, path, ratio, null, save };
            bool accepted = (bool)typeof(ExportHelper).GetMethod("TrySaveCollisionStl",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(exporter, arguments);
            notes = (string)arguments[3];
            return accepted;
        }
    }
}
