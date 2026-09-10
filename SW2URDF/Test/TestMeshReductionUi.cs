using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace SW2URDF.Test
{
    [Collection("WinForms layout")]
    public class TestMeshReductionUi
    {
        [Theory]
        [InlineData(0.0, 0, "0%")]
        [InlineData(0.5, 50, "50%")]
        [InlineData(1.0, 100, "100%")]
        public void SavedRatioDisplaysTargetRemovalPercentage(double ratio, int value, string text)
        {
            RunOnSta("en-US", () =>
            {
                using (AssemblyExportForm form = CreateForm())
                {
                    var link = new Link { MeshReductionRatio = ratio };
                    form.FillLinkPropertyBoxes(link);
                    Assert.Equal(value, Field<TrackBar>(form, "trackBarMeshReduction").Value);
                    Assert.Equal(text, Field<Label>(form, "labelMeshReductionValue").Text);
                    Assert.Equal("Target triangle reduction (%)", Field<Label>(form, "labelMeshReduction").Text);
                    form.SaveLinkDataFromPropertyBoxes(link);
                    Assert.Equal(ratio, link.MeshReductionRatio);
                    Assert.False(Field<bool>(form, "meshReductionRatioEdited"));
                }
            });
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("zh-CN")]
        public void SavedConfigurationRetainsRatiosAcrossLinkAndPageSelection(string culture)
        {
            RunOnSta(culture, () =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "sw2urdf-reduction-ui-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var root = new LinkNode { IsBaseNode = true };
                    root.Link.Name = "base_link";
                    root.Link.MeshReductionRatio = 0;
                    foreach (double ratio in new[] { 0.5, 1.0 })
                    {
                        var child = new LinkNode();
                        child.Link.Name = ratio == 0.5 ? "half_link" : "maximum_link";
                        child.Link.Joint.Name = child.Link.Name + "_joint";
                        child.Link.Joint.Type = "fixed";
                        child.Link.MeshReductionRatio = ratio;
                        root.Nodes.Add(child);
                    }
                    var store = new FileExportSessionDraftStore(directory);
                    string modelPath = Path.Combine(directory, "robot.SLDASM");
                    Assert.True(store.Save(modelPath, root, "robot_description", directory));
                    Assert.True(store.TryLoad(modelPath, out ExportSessionDraft saved));

                    using (AssemblyExportForm form = CreateForm())
                    {
                        Link[] links = new[] { saved.Root.Link,
                            ((LinkNode)saved.Root.Nodes[0]).Link, ((LinkNode)saved.Root.Nodes[1]).Link };
                        double[] ratios = { 0, 0.5, 1 };
                        string[] labels = { "0%", "50%", "100%" };
                        TabControl pages = Field<TabControl>(form, "modernLinkSections");
                        // Exercise the same binding/save boundary as selection, without CAD selection calls.
                        foreach (int index in new[] { 0, 1, 2, 1, 0, 2 })
                        {
                            form.FillLinkPropertyBoxes(links[index]);
                            foreach (int page in new[] { 1, 2, 0, 1 })
                            {
                                pages.SelectedIndex = page;
                                pages.PerformLayout();
                                Assert.Equal(labels[index], Field<Label>(form, "labelMeshReductionValue").Text);
                                form.SaveLinkDataFromPropertyBoxes(links[index]);
                                Assert.Equal(ratios[index], links[index].MeshReductionRatio);
                            }
                            Assert.False(Field<bool>(form, "meshReductionRatioEdited"));
                        }
                        Assert.True(store.Save(modelPath, saved.Root, "robot_description", directory));
                        Assert.True(store.TryLoad(modelPath, out ExportSessionDraft resaved));
                        Assert.Equal(0.0, resaved.Root.Link.MeshReductionRatio);
                        Assert.Equal(0.5, ((LinkNode)resaved.Root.Nodes[0]).Link.MeshReductionRatio);
                        Assert.Equal(1.0, ((LinkNode)resaved.Root.Nodes[1]).Link.MeshReductionRatio);
                    }
                }
                finally
                {
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                }
            });
        }

        [Theory]
        [InlineData(0, 0.0, "0%")]
        [InlineData(50, 0.5, "50%")]
        [InlineData(100, 1.0, "100%")]
        public void EditedTargetSurvivesSelectingAnotherLink(int value, double ratio, string text)
        {
            RunOnSta("en-US", () =>
            {
                using (AssemblyExportForm form = CreateForm())
                {
                    form.FillLinkPropertyBoxes(new Link { MeshReductionRatio = 0.25 });
                    TrackBar slider = Field<TrackBar>(form, "trackBarMeshReduction");
                    slider.Value = value;
                    typeof(AssemblyExportForm).GetMethod("TrackBarMeshReductionScroll",
                        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { slider, EventArgs.Empty });
                    Assert.True(Field<bool>(form, "meshReductionRatioEdited"));
                    Assert.Equal(ratio, Field<double>(form, "meshReductionRatioForExport"));
                    var next = new Link { MeshReductionRatio = 0.75 };
                    form.FillLinkPropertyBoxes(next);
                    Assert.Equal(text, Field<Label>(form, "labelMeshReductionValue").Text);
                    Assert.Equal(value, slider.Value);
                    form.SaveLinkDataFromPropertyBoxes(next);
                    Assert.Equal(ratio, next.MeshReductionRatio);
                }
            });
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("zh-CN")]
        public void ConciseReductionStatusFitsItsExistingWrappedRow(string culture)
        {
            RunOnSta(culture, () =>
            {
                using (AssemblyExportForm form = CreateForm())
                {
                    form.FillLinkPropertyBoxes(new Link { MeshReductionRatio = 1 });
                    TabControl pages = Field<TabControl>(form, "modernLinkSections");
                    pages.SelectedIndex = 1;
                    pages.PerformLayout();
                    Label note = Field<Label>(form, "labelEstimatedMeshSize");
                    TrackBar slider = Field<TrackBar>(form, "trackBarMeshReduction");
                    note.Parent.PerformLayout();
                    Assert.Same(slider.Parent, note.Parent);
                    Assert.True(note.Width > 0);
                    Assert.True(note.MaximumSize.Width > 0);
                    Assert.Equal(0, note.MaximumSize.Height);
                    Assert.True(note.Top >= slider.Bottom, "Reduction explanation overlaps the slider.");
                    Assert.True(note.Parent.ClientRectangle.Contains(note.Bounds), "Reduction explanation exceeds its row container.");
                    Size measured = TextRenderer.MeasureText(note.Text, note.Font,
                        new Size(note.Width, Int32.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                    Assert.True(measured.Height <= note.Height,
                        String.Format("Explanation is clipped: measured {0}, actual {1}, width {2}.",
                            measured.Height, note.Height, note.Width));
                    Assert.Equal(culture == "en-US"
                        ? "Reduce as much as possible; actual size is reported after export"
                        : "\u5c3d\u53ef\u80fd\u7cbe\u7b80\uff1b\u5b9e\u9645\u5927\u5c0f\u4ee5\u5bfc\u51fa\u7ed3\u679c\u4e3a\u51c6", note.Text);
                    Assert.Equal(culture == "en-US"
                        ? "Target triangle reduction (%)"
                        : "\u76ee\u6807\u51cf\u9762\u6bd4\u4f8b (%)",
                        Field<Label>(form, "labelMeshReduction").Text);
                    Assert.DoesNotContain("100%", note.Text);
                    Assert.DoesNotContain("Rough STL estimate", note.Text);
                }
            });
        }

        private static AssemblyExportForm CreateForm()
        {
            var form = (AssemblyExportForm)Activator.CreateInstance(typeof(AssemblyExportForm), true);
            // Match the lightweight layout fixture: no SolidWorks instance or COM calls.
            var exporter = (ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));
            var catalog = (ReferenceGeometryCatalog)FormatterServices.GetUninitializedObject(typeof(ReferenceGeometryCatalog));
            SetField(catalog, "entries", new List<ReferenceGeometryEntry>());
            SetField(exporter, "referenceGeometryCatalog", catalog);
            exporter.URDFRobot = new Robot();
            form.Exporter = exporter;
            string[] fields = {
                "textBoxInertialOriginX", "textBoxInertialOriginY", "textBoxInertialOriginZ",
                "textBoxInertialOriginRoll", "textBoxInertialOriginPitch", "textBoxInertialOriginYaw",
                "textBoxVisualOriginX", "textBoxVisualOriginY", "textBoxVisualOriginZ",
                "textBoxVisualOriginRoll", "textBoxVisualOriginPitch", "textBoxVisualOriginYaw",
                "textBoxIxx", "textBoxIxy", "textBoxIxz", "textBoxIyy", "textBoxIyz", "textBoxIzz",
                "textBoxMass", "domainUpDownRed", "domainUpDownGreen", "domainUpDownBlue",
                "domainUpDownAlpha", "comboBoxMaterials"
            };
            SetField(form, "linkBoxes", fields.Select(name => Field<Control>(form, name)).ToArray());
            SetField(form, "jointBoxes", new Control[] {
                Field<ComboBox>(form, "comboBoxJointType"), Field<ComboBox>(form, "comboBoxAxis"),
                Field<TextBox>(form, "textBoxLimitLower"), Field<TextBox>(form, "textBoxLimitUpper")
            });
            return form;
        }

        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static void RunOnSta(string culture, Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true);
                    test();
                }
                catch (Exception error) { failure = error; }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Reduction UI test timed out on its STA thread.");
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
