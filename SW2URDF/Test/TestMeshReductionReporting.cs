using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.IO;
using System.Linq;
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
            foreach (bool ros2 in new[] { false, true })
            {
                ExportHelper.ExportReportBuildResult report = Report(stats, ros2);
                Assert.Equal(expected, report.Status);
                Assert.Contains("Status: " + expected, report.Content);
                Assert.Contains("| STL reduction | " + expected + " |", report.Content);
                Assert.False(report.HasBlockingFailures);
                Assert.Equal(String.Empty, report.BlockingFailureSummary());
                if (expected == "WARN")
                {
                    string finding = Assert.Single(report.Findings);
                    Assert.Contains("WARN: STL reduction for link base_link", finding);
                    Assert.Contains("status=" + result, finding);
                    Assert.Contains("target_triangles=" + target, finding);
                    Assert.Contains("actual_triangles=" + actual, finding);
                    if (!String.IsNullOrWhiteSpace(warning)) Assert.Contains("reason=" + warning, finding);
                    Assert.Contains(finding, report.Content.Split(new[] { "## Findings" }, StringSplitOptions.None).Last());
                }
                else Assert.Empty(report.Findings);
            }
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

        private static ExportHelper.ExportReportBuildResult Report(ExportHelper.StlExportStats stats, bool ros2)
        {
            string root = Path.Combine(Path.GetTempPath(), "sw2urdf-reduction-report-" + Guid.NewGuid());
            try
            {
                var package = new URDFPackage("robot", "robot_description", root);
                string directory = ros2 ? package.WindowsRos2PackageDirectory : package.WindowsPackageDirectory;
                string urdf = Path.Combine(ros2 ? package.WindowsRos2RobotsDirectory : package.WindowsRobotsDirectory,
                    OSURDF.Core.Export.RosPackageExporter.GetRobotUrdfFileName(package.RobotName));
                foreach (string relative in new[] { "CMakeLists.txt", "package.xml",
                    "config/inertial_validation.csv", "config/mesh_manifest.csv",
                    ros2 ? "launch/display.launch.py" : "launch/display.launch",
                    ros2 ? "launch/gazebo.launch.py" : "launch/gazebo.launch",
                    "meshes/visual/base_link.stl", "meshes/collision/base_link.stl" })
                {
                    string path = Path.Combine(directory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, "fixture");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(urdf));
                File.WriteAllText(urdf, "<robot name=\"robot\"><link name=\"base_link\" /></robot>");
                var record = new ExportHelper.MeshExportRecord(
                    "base_link", "VisualMesh", "VisualMesh", "visual_mesh_copy", "ok", "STL",
                    "visual.stl", "collision.stl", "visual.stl", "collision.stl", true, true,
                    stats.ActualBytes, stats.ActualBytes, stats.ActualTriangles, stats.ActualTriangles, stats);
                var inertial = new ExportHelper.InertialValidationRecord("base_link", "Origin_global",
                    new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0));
                return ExportHelper.BuildExportReportResult(package, urdf, new[] { inertial }, new[] { record },
                    true, MeshExportFormat.STL, TimeSpan.Zero, new ExportTargetOptions
                    {
                        UseV2Pipeline = true, ExportRos1Legacy = !ros2, ExportRos2 = ros2,
                        ExportUsdAsset = false, ExportMjcfAsset = false
                    });
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
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
