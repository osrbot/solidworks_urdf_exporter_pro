using SW2URDF.UI;
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace SW2URDF.Test
{
    public class TestAssemblyExportLayout
    {
        [Fact]
        public void TestLinkPageUsesHighDpiSafeAnchoring()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
                Assert.True(GetControl<Panel>(form, "panelLinkProperties").AutoScroll);
                AssertRightAnchored(GetControl<Label>(form, "labelRosPackageName"));
                AssertRightAnchored(GetControl<TextBox>(form, "textBoxRosPackageName"));
                AssertRightAnchored(GetControl<Label>(form, "labelRosPackageNameHint"));
                AssertBottomAnchored(GetControl<Label>(form, "label4"));
                AssertBottomAnchored(GetControl<Label>(form, "label27"));
                form.PerformLayout();
                AssertJointFooterGeometry(form);
                AssertJointFooterTextFits(form);
                AssertMimicControlsDoNotOverlapFooter(form);
                AssertFooterButtonsFitText(form);
                AssertInertiaMatrixMirrors(form);

                form.ClientSize = new Size(1600, 900);
                form.PerformLayout();
                AssertLinkPageGeometry(form);
                AssertInertiaMatrixMirrors(form);
                AssertJointFooterGeometry(form);
                AssertJointFooterTextFits(form);
                AssertMimicControlsDoNotOverlapFooter(form);
                AssertFooterButtonsFitText(form);

                form.ClientSize = new Size(2560, 1440);
                form.PerformLayout();
                AssertLinkPageGeometry(form);
                AssertInertiaMatrixMirrors(form);
                AssertJointFooterGeometry(form);
                AssertJointFooterTextFits(form);
                AssertMimicControlsDoNotOverlapFooter(form);
                AssertFooterButtonsFitText(form);

                form.Scale(new SizeF(1.5F, 1.5F));
                form.PerformLayout();
                AssertLinkPageGeometry(form);
                AssertInertiaMatrixMirrors(form);
                AssertJointFooterGeometry(form);
                AssertJointFooterTextFits(form);
                AssertMimicControlsDoNotOverlapFooter(form);
                AssertFooterButtonsFitText(form);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestJointFooterWrapsLongLocalizedText()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                Label firstNote = GetControl<Label>(form, "label4");
                Label secondNote = GetControl<Label>(form, "label27");
                Label mimicEquation = GetControl<Label>(form, "MimicEquationLabel");
                CheckBox mimicCheckBox = GetControl<CheckBox>(form, "MimicCheckBox");
                firstNote.Text = "空白项不会写入 URDF。请确认坐标系、轴和惯性参数已经在 SolidWorks 中正确配置。";
                secondNote.Text = "* 字段组为必填；如果字段过长，界面必须换行显示而不是裁切。";
                mimicEquation.Text =
                    "pos = multiplier * pos_other + offset; long localized mimic formula should wrap instead of covering adjacent controls";

                form.ClientSize = new Size(1073, 634);
                mimicCheckBox.Checked = true;
                form.PerformLayout();

                AssertJointFooterGeometry(form);
                AssertJointFooterTextFits(form);
                AssertMimicControlsDoNotOverlapFooter(form);
            }
            finally
            {
                form.Dispose();
            }
        }

        private static T GetControl<T>(AssemblyExportForm form, string fieldName)
            where T : Control
        {
            FieldInfo field = typeof(AssemblyExportForm).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            T control = field.GetValue(form) as T;
            Assert.NotNull(control);
            return control;
        }

        private static void AssertRightAnchored(Control control)
        {
            Assert.True((control.Anchor & AnchorStyles.Right) == AnchorStyles.Right);
        }

        private static void AssertBottomAnchored(Control control)
        {
            Assert.True((control.Anchor & AnchorStyles.Bottom) == AnchorStyles.Bottom);
        }

        private static void AssertLinkPageGeometry(AssemblyExportForm form)
        {
            Panel panel = GetControl<Panel>(form, "panelLinkProperties");
            Label packageLabel = GetControl<Label>(form, "labelRosPackageName");
            Label packageHint = GetControl<Label>(form, "labelRosPackageNameHint");
            GroupBox inertiaGroup = GetControl<GroupBox>(form, "groupBox5");
            GroupBox meshGroup = GetControl<GroupBox>(form, "groupBox4");
            TreeView tree = GetControl<TreeView>(form, "treeViewLinkProperties");
            Button finishButton = GetControl<Button>(form, "buttonLinksFinish");
            ComboBox collisionStrategy = GetControl<ComboBox>(form, "comboBoxCollisionStrategy");
            Label collisionStrategyLabel = GetControl<Label>(form, "labelCollisionStrategy");
            TextBox visualYaw = GetControl<TextBox>(form, "textBoxVisualOriginYaw");
            DomainUpDown colorRed = GetControl<DomainUpDown>(form, "domainUpDownRed");

            Assert.Equal(inertiaGroup.Left, packageLabel.Left);
            Assert.True(packageHint.Right <= inertiaGroup.Right);
            Assert.True(tree.Right < inertiaGroup.Left);
            Assert.True(meshGroup.Right <= panel.ClientSize.Width);
            Assert.True(finishButton.Right <= panel.ClientSize.Width);
            Assert.True(finishButton.Bottom <= panel.ClientSize.Height);
            Assert.Equal(ComboBoxStyle.DropDownList, collisionStrategy.DropDownStyle);
            Assert.True(collisionStrategy.Left > visualYaw.Right);
            Assert.True(collisionStrategy.Right < colorRed.Left);
            Assert.True(collisionStrategyLabel.Bottom <= collisionStrategy.Top);
        }

        private static void AssertJointFooterGeometry(AssemblyExportForm form)
        {
            Label firstNote = GetControl<Label>(form, "label4");
            Label secondNote = GetControl<Label>(form, "label27");
            Button nextButton = GetControl<Button>(form, "buttonJointNext");
            Button cancelButton = GetControl<Button>(form, "buttonJointCancel");
            TreeView jointTree = GetControl<TreeView>(form, "treeViewJointTree");

            Assert.True(
                firstNote.Bottom <= secondNote.Top,
                String.Format("Joint footer lines overlap: {0} > {1}.",
                    firstNote.Bottom, secondNote.Top));
            Assert.True(
                secondNote.Bottom < nextButton.Top,
                String.Format("Joint footer overlaps Next: {0} >= {1}.",
                    secondNote.Bottom, nextButton.Top));
            Assert.True(
                jointTree.Bottom < firstNote.Top,
                String.Format("Joint footer overlaps joint tree: {0} >= {1}.",
                    jointTree.Bottom, firstNote.Top));
            Assert.True(
                secondNote.Bottom < cancelButton.Top,
                String.Format("Joint footer overlaps Cancel: {0} >= {1}.",
                    secondNote.Bottom, cancelButton.Top));
            Assert.True(
                secondNote.Bottom <= form.ClientSize.Height,
                String.Format("Joint footer is clipped: {0} > {1}.",
                    secondNote.Bottom, form.ClientSize.Height));
        }

        private static void AssertJointFooterTextFits(AssemblyExportForm form)
        {
            Label firstNote = GetControl<Label>(form, "label4");
            Label secondNote = GetControl<Label>(form, "label27");

            AssertWrappedLabelFits(firstNote, form.ClientSize.Width - 12);
            AssertWrappedLabelFits(secondNote, form.ClientSize.Width - 12);
        }

        private static void AssertWrappedLabelFits(Label label, int maxRight)
        {
            Size measured = TextRenderer.MeasureText(
                label.Text ?? "",
                label.Font,
                new Size(label.Width, Int32.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            Assert.True(
                label.Right <= maxRight,
                String.Format("{0} is clipped horizontally: right {1}, max {2}.",
                    label.Name, label.Right, maxRight));
            Assert.True(
                measured.Height <= label.Height,
                String.Format("{0} is clipped vertically: preferred {1}, actual {2}.",
                    label.Name, measured.Height, label.Height));
        }

        private static void AssertMimicControlsDoNotOverlapFooter(AssemblyExportForm form)
        {
            CheckBox mimicCheckBox = GetControl<CheckBox>(form, "MimicCheckBox");
            mimicCheckBox.Checked = true;
            form.PerformLayout();

            Label firstNote = GetControl<Label>(form, "label4");
            Label secondNote = GetControl<Label>(form, "label27");
            Control[] mimicControls = new Control[]
            {
                mimicCheckBox,
                GetControl<Label>(form, "MimicJointLabel"),
                GetControl<ComboBox>(form, "MimicJointComboBox"),
                GetControl<Label>(form, "MimicMultiplierLabel"),
                GetControl<TextBox>(form, "textBoxMimicMultiplier"),
                GetControl<Label>(form, "MimicOffsetLabel"),
                GetControl<TextBox>(form, "textBoxMimicOffset"),
                GetControl<Label>(form, "MimicEquationLabel")
            };

            foreach (Control mimicControl in mimicControls)
            {
                Rectangle mimicBounds = mimicControl.Bounds;
                Assert.False(
                    mimicBounds.IntersectsWith(firstNote.Bounds),
                    String.Format("{0} overlaps the first footer note: visible={1}, mimic={2}, footer={3}.",
                        mimicControl.Name,
                        mimicControl.Visible,
                        mimicBounds,
                        firstNote.Bounds));
                Assert.False(
                    mimicBounds.IntersectsWith(secondNote.Bounds),
                    String.Format("{0} overlaps the second footer note: visible={1}, mimic={2}, footer={3}.",
                        mimicControl.Name,
                        mimicControl.Visible,
                        mimicBounds,
                        secondNote.Bounds));
                Assert.True(
                    mimicBounds.Bottom < firstNote.Top,
                    String.Format("{0} should stay above the footer note: {1} >= {2}.",
                        mimicControl.Name, mimicBounds.Bottom, firstNote.Top));
            }

            TextBox offsetBox = GetControl<TextBox>(form, "textBoxMimicOffset");
            Label equation = GetControl<Label>(form, "MimicEquationLabel");
            Assert.True(
                equation.Left >= offsetBox.Right || equation.Top >= offsetBox.Bottom,
                String.Format(
                    "Mimic equation overlaps offset input: equation={0}, offset={1}.",
                    equation.Bounds,
                    offsetBox.Bounds));
            AssertWrappedLabelFits(equation, form.ClientSize.Width - 12);
        }

        private static void AssertFooterButtonsFitText(AssemblyExportForm form)
        {
            Button[] buttons = new Button[]
            {
                GetControl<Button>(form, "buttonJointCancel"),
                GetControl<Button>(form, "buttonJointNext"),
                GetControl<Button>(form, "buttonLinksPrevious"),
                GetControl<Button>(form, "buttonLinksExportUrdfOnly"),
                GetControl<Button>(form, "buttonLinksFinish")
            };

            foreach (Button button in buttons)
            {
                Size preferred = button.GetPreferredSize(Size.Empty);
                Assert.True(
                    preferred.Width <= button.Width,
                    String.Format("{0} text is clipped horizontally: preferred {1}, actual {2}.",
                        button.Name, preferred.Width, button.Width));
                Assert.True(
                    preferred.Height <= button.Height,
                    String.Format("{0} text is clipped vertically: preferred {1}, actual {2}.",
                        button.Name, preferred.Height, button.Height));
            }
        }

        private static void AssertInertiaMatrixMirrors(AssemblyExportForm form)
        {
            TextBox ixy = GetControl<TextBox>(form, "textBoxIxy");
            TextBox ixz = GetControl<TextBox>(form, "textBoxIxz");
            TextBox iyz = GetControl<TextBox>(form, "textBoxIyz");
            TextBox iyxMirror = GetControl<TextBox>(form, "textBoxIyxMirror");
            TextBox izxMirror = GetControl<TextBox>(form, "textBoxIzxMirror");
            TextBox izyMirror = GetControl<TextBox>(form, "textBoxIzyMirror");
            TextBox iyy = GetControl<TextBox>(form, "textBoxIyy");
            TextBox izz = GetControl<TextBox>(form, "textBoxIzz");
            Button previewButton = GetControl<Button>(form, "buttonShowInertiaPreview");

            ixy.Text = "1.2e-6";
            ixz.Text = "-2.3e-6";
            iyz.Text = "3.4e-6";

            Assert.True(iyxMirror.ReadOnly);
            Assert.True(izxMirror.ReadOnly);
            Assert.True(izyMirror.ReadOnly);
            Assert.False(iyxMirror.TabStop);
            Assert.False(izxMirror.TabStop);
            Assert.False(izyMirror.TabStop);
            Assert.Equal(ixy.Text, iyxMirror.Text);
            Assert.Equal(ixz.Text, izxMirror.Text);
            Assert.Equal(iyz.Text, izyMirror.Text);
            Assert.True(iyxMirror.Right < iyy.Left);
            Assert.True(izxMirror.Right < izyMirror.Left);
            Assert.True(izyMirror.Right < izz.Left);
            Assert.True(izz.Bottom < previewButton.Top);
        }
    }
}
