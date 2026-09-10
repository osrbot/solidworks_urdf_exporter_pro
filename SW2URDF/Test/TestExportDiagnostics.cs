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
