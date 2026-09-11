using SW2URDF.UI;
using Xunit;

namespace SW2URDF.Test
{
    public class TestMeshReductionTargetUi
    {
        [Theory]
        [InlineData(0.0, false, "No reduction; target STL size: 100% of original")]
        [InlineData(0.0, true, "不减面；目标 STL 大小：原始的 100%")]
        [InlineData(0.01, false, "Target STL size: approx. 99% of original")]
        [InlineData(0.5, false, "Target STL size: approx. 50% of original")]
        [InlineData(0.67, true, "目标 STL 大小：约原始的 33%")]
        [InlineData(0.99, false, "Target STL size: approx. 1% of original")]
        [InlineData(0.99, true, "目标 STL 大小：约原始的 1%")]
        [InlineData(1.0, false, "Reduce as much as possible; actual size is reported after export")]
        [InlineData(1.0, true, "尽可能精简；实际大小以导出结果为准")]
        public void FormatsRemovalFractionWithoutInventingBytes(double ratio, bool chinese, string expected)
        {
            Assert.Equal(expected, AssemblyExportForm.FormatMeshReductionTarget(ratio, chinese));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EveryIntermediateSliderValueShowsRemainingPercentage(bool chinese)
        {
            for (int removed = 1; removed < 100; removed++)
            {
                string text = AssemblyExportForm.FormatMeshReductionTarget(removed / 100.0, chinese);
                Assert.Contains((100 - removed) + "%", text);
                Assert.DoesNotContain("MiB", text);
                Assert.DoesNotContain("KiB", text);
                Assert.DoesNotContain(" MB", text);
            }
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(-1L)]
        public void InvalidSourceStatisticsAreOmitted(long bytes)
        {
            foreach (bool chinese in new[] { false, true })
                Assert.Equal(AssemblyExportForm.FormatMeshReductionTarget(0.5, chinese),
                    AssemblyExportForm.FormatMeshReductionTarget(0.5, chinese, bytes));
        }

        [Theory]
        [InlineData(false, "Target STL size: approx. 50% of original (approx. 1 MiB)")]
        [InlineData(true, "目标 STL 大小：约原始的 50%（约 1 MiB）")]
        public void VerifiedStlBytesEnableExplicitApproximation(bool chinese, string expected)
        {
            Assert.Equal(expected, AssemblyExportForm.FormatMeshReductionTarget(0.5, chinese, 2097152));
        }

        [Theory]
        [InlineData(2048L, "1 KiB")]
        [InlineData(1000L, "500 B")]
        [InlineData(1L, "1 B")]
        public void SmallVerifiedSizesNeverRoundToZero(long bytes, string size)
        {
            Assert.EndsWith("(approx. " + size + ")",
                AssemblyExportForm.FormatMeshReductionTarget(0.5, false, bytes));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MaximumReductionDefersSizeToExportEvenWithStatistics(bool chinese)
        {
            Assert.Equal(AssemblyExportForm.FormatMeshReductionTarget(1.0, chinese),
                AssemblyExportForm.FormatMeshReductionTarget(1.0, chinese, 2097152));
        }

        [Theory]
        [InlineData(-1.0, 0.0)]
        [InlineData(2.0, 1.0)]
        [InlineData(double.NaN, 0.0)]
        [InlineData(double.NegativeInfinity, 0.0)]
        [InlineData(double.PositiveInfinity, 1.0)]
        public void InvalidRatiosAreNormalized(double ratio, double normalized)
        {
            Assert.Equal(AssemblyExportForm.FormatMeshReductionTarget(normalized, false),
                AssemblyExportForm.FormatMeshReductionTarget(ratio, false));
        }
    }
}
