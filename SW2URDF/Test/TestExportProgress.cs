using SW2URDF.URDFExport;
using System;
using System.IO;
using Xunit;

namespace SW2URDF.Test
{
    public class TestExportProgress
    {
        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(1024, "1 KiB")]
        [InlineData(1048576, "1 MiB")]
        public void TestExportSummaryFormatsBinaryFileSize(long bytes, string expected)
        {
            Assert.Equal(expected, ExportResultSummary.FormatBytes(bytes));
        }

        [Fact]
        public void TestExportSummaryCountsOnlyFilesWrittenByCurrentExport()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-export-summary-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            URDFPackage package = new URDFPackage("robot", "robot_description", root);
            package.CreateDirectories();
            string staleFile = Path.Combine(package.WindowsMeshesDirectory, "stale.stl");
            File.WriteAllBytes(staleFile, new byte[11]);
            File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow.AddMinutes(-5));
            ExportOutputSnapshot beforeExport = ExportOutputSnapshot.Capture(package);
            string currentFile = Path.Combine(package.WindowsRobotsDirectory, "robot.urdf");
            File.WriteAllBytes(currentFile, new byte[23]);

            try
            {
                ExportResultSummary summary = ExportResultSummary.Create(
                    package,
                    beforeExport,
                    TimeSpan.FromSeconds(2));

                Assert.Equal(1, summary.FileCount);
                Assert.Equal(23, summary.TotalBytes);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void TestExportSummaryCountsOverwrittenFilesWithoutTimestampHeuristics()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-export-summary-overwrite-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            URDFPackage package = new URDFPackage("robot", "robot_description", root);
            package.CreateDirectories();
            string output = Path.Combine(package.WindowsRobotsDirectory, "robot.urdf");
            File.WriteAllBytes(output, new byte[11]);
            ExportOutputSnapshot beforeExport = ExportOutputSnapshot.Capture(package);
            File.WriteAllBytes(output, new byte[29]);

            try
            {
                ExportResultSummary summary = ExportResultSummary.Create(
                    package,
                    beforeExport,
                    TimeSpan.FromSeconds(1));

                Assert.Equal(1, summary.FileCount);
                Assert.Equal(29, summary.TotalBytes);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
