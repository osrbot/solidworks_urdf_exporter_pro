using SW2URDF.UI;
using SW2URDF.URDFExport;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace SW2URDF.Test
{
    [Collection("WinForms layout")]
    public class TestExportDiagnostics
    {
        [Theory]
        [InlineData("en-US", 0)]
        [InlineData("en-US", 1)]
        [InlineData("en-US", 2)]
        [InlineData("zh-CN", 0)]
        [InlineData("zh-CN", 1)]
        [InlineData("zh-CN", 2)]
        public void PerLinkMeshReductionDetailsRemainVisibleWithoutBecomingWarnings(string culture, int warningCount)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                    bool chinese = culture == "zh-CN";
                    string[] reductions = {
                        "base_link [visual STL]: 2.00 MB -> 1.00 MB",
                        "arm_link [visual STL]: 4.00 MB -> 1.50 MB",
                        "arm_link [collision STL]: 3.00 MB -> 0.75 MB"
                    };
                    string[] warnings = new[] { "base_link: reduction limited", "arm_link: collision fallback" }
                        .Take(warningCount).ToArray();
                    var targets = new[] { new ExportTargetResult("ROS 2", "output", true, String.Empty, false) };
                    var summary = new ExportResultSummary("output", 3, 3407872, TimeSpan.Zero,
                        targets, warnings: warnings, meshReductionDetails: reductions);
                    Assert.Equal(reductions, summary.MeshReductionDetails.ToArray());
                    Assert.Equal(warnings, summary.Warnings.ToArray());
                    string sectionHeading = chinese
                        ? "STL \u51cf\u9762\u5b9e\u9645\u7ed3\u679c\uff08\u539f\u59cb -> \u5bfc\u51fa\uff09:"
                        : "Measured STL reduction (original -> exported):";
                    string reductionSection = sectionHeading + Environment.NewLine +
                        String.Join(Environment.NewLine, reductions);
                    Assert.Contains(reductionSection, summary.FormatDetails());

                    using (var dialog = new ExportResultsDialog(summary, null, path =>
                        { throw new InvalidOperationException("Test must not open paths."); }))
                    {
                        var details = (TextBox)dialog.Controls.Find("exportResultsDetails", true).Single();
                        Assert.Contains(reductionSection, details.Text);
                        foreach (string reduction in reductions)
                            Assert.Equal(2, details.Text.Split(new[] { reduction }, StringSplitOptions.None).Length);
                        string warningHeading = chinese ? "\u8b66\u544a:" : "Warnings:";
                        if (warningCount == 0)
                        {
                            Assert.Equal(summary.FormatDetails(), details.Text);
                            Assert.DoesNotContain(warningHeading, details.Text);
                        }
                        else
                        {
                            Assert.StartsWith(warningHeading + Environment.NewLine +
                                String.Join(Environment.NewLine, warnings) + Environment.NewLine + Environment.NewLine,
                                details.Text);
                            foreach (string warning in warnings)
                                Assert.Equal(2, details.Text.Split(new[] { warning }, StringSplitOptions.None).Length);
                        }
                        string title = (chinese ? "\u5bfc\u51fa\u5b8c\u6210" : "Export completed") +
                            (warningCount == 0 ? String.Empty : chinese
                                ? "\uff08\u6709" + warningCount + "\u9879\u8b66\u544a\uff09"
                                : " (" + warningCount + " warnings)");
                        Assert.Equal(title, dialog.Text);
                        var heading = (Label)dialog.Controls.Find("exportResultsHeading", true).Single();
                        Assert.StartsWith(title, heading.Text);
                        Assert.Equal(warningCount == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning,
                            dialog.ResultIcon);
                        Assert.Equal(warnings, summary.Warnings.ToArray());
                        Assert.Equal(reductions, summary.MeshReductionDetails.ToArray());
                    }
                }
                catch (Exception exception) { failure = exception; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Dialog test timed out.");
            if (failure != null) throw new AggregateException(failure);
        }

        [Theory]
        [InlineData("en-US", 0)]
        [InlineData("en-US", 1)]
        [InlineData("en-US", 2)]
        [InlineData("zh-CN", 0)]
        [InlineData("zh-CN", 1)]
        [InlineData("zh-CN", 2)]
        public void CompletionTitleAndHeadingExposeWarningsWithoutHidingFailures(string culture, int failed)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                    bool chinese = culture == "zh-CN";
                    string title = failed == 0 ? (chinese ? "\u5bfc\u51fa\u5b8c\u6210" : "Export completed")
                        : failed == 1 ? (chinese ? "\u5bfc\u51fa\u90e8\u5206\u5b8c\u6210" : "Export partially completed")
                        : (chinese ? "\u5bfc\u51fa\u5931\u8d25" : "Export failed");
                    var targets = new[] { "ROS 1", "ROS 2" }.Select((name, index) =>
                        new ExportTargetResult(name, "output", index >= failed,
                            index < failed ? "isolated-failure" : String.Empty, false)).ToArray();
                    foreach (int count in new[] { 0, 1, 2 })
                    {
                        string[] warnings = new[] { "base_link: reduction limited", "arm_link: reduction failed" }.Take(count).ToArray();
                        var summary = new ExportResultSummary("output", 0, 0, TimeSpan.Zero, targets, warnings);
                        using (var dialog = new ExportResultsDialog(summary, null, path =>
                            { throw new InvalidOperationException("Test must not open paths."); }))
                        {
                            string expected = title + (count == 0 ? String.Empty : chinese
                                ? "\uff08\u6709" + count + "\u9879\u8b66\u544a\uff09" : " (" + count + " warnings)");
                            Assert.Equal(expected, dialog.Text);
                            var heading = (Label)dialog.Controls.Find("exportResultsHeading", true).Single();
                            Assert.StartsWith(expected, heading.Text);
                            var details = (TextBox)dialog.Controls.Find("exportResultsDetails", true).Single();
                            if (count > 0)
                            {
                                Assert.StartsWith((chinese ? "\u8b66\u544a:" : "Warnings:") + Environment.NewLine +
                                    String.Join(Environment.NewLine, warnings) + Environment.NewLine + Environment.NewLine,
                                    details.Text);
                                foreach (string warning in warnings)
                                    Assert.Equal(2, details.Text.Split(new[] { warning }, StringSplitOptions.None).Length);
                            }
                            else Assert.Equal(summary.FormatDetails(), details.Text);
                            if (failed > 0) Assert.Contains("isolated-failure", details.Text);
                            Assert.Equal(failed == 2 ? MessageBoxIcon.Error : failed == 1 || count > 0
                                ? MessageBoxIcon.Warning : MessageBoxIcon.Information, dialog.ResultIcon);
                            Assert.Equal(2 - failed, summary.SucceededCount);
                        }
                    }
                }
                catch (Exception exception) { failure = exception; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Dialog test timed out.");
            if (failure != null) throw new AggregateException(failure);
        }
    }
}
