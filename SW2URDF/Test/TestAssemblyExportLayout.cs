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
        [Theory]
        [InlineData("Automatically Detect", "自动识别")]
        [InlineData("revolute", "有限角度转动")]
        [InlineData("continuous", "无约束连续转动")]
        [InlineData("prismatic", "直线滑动")]
        [InlineData("fixed", "固定连接")]
        [InlineData("floating", "六自由度运动")]
        [InlineData("planar", "平面运动")]
        public void TestChineseJointTypeDisplayRoundTripsToUrdfValue(
            string jointType,
            string description)
        {
            string localized = jointType + " / " + description;
            Assert.Equal(localized, ChineseUiText.JointTypeDisplay(jointType, true));
            Assert.Equal(jointType, ChineseUiText.JointTypeDisplay(jointType, false));
            Assert.Equal(jointType, ChineseUiText.JointTypeValue(localized));
        }

        [Fact]
        public void TestUnknownJointTypeDisplayIsPreserved()
        {
            Assert.Equal("custom", ChineseUiText.JointTypeDisplay("custom", true));
            Assert.Equal("custom", ChineseUiText.JointTypeValue("custom"));
        }

        [Fact]
        public void TestMaterialPresetUpdatesRgbaAndClearsOldTexture()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                ComboBox materials = GetControl<ComboBox>(form, "comboBoxMaterials");
                TextBox texture = GetControl<TextBox>(form, "textBoxTexture");
                texture.Text = "old-texture.png";
                materials.Text = "green";

                MethodInfo applyPreset = typeof(AssemblyExportForm).GetMethod(
                    "MaterialPresetSelectionChangeCommitted",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(applyPreset);
                applyPreset.Invoke(form, new object[] { materials, EventArgs.Empty });

                Assert.Equal(String.Empty, texture.Text);
                Assert.Equal("0.05", GetControl<DomainUpDown>(form, "domainUpDownRed").Text);
                Assert.Equal("0.6", GetControl<DomainUpDown>(form, "domainUpDownGreen").Text);
                Assert.Equal("0.1", GetControl<DomainUpDown>(form, "domainUpDownBlue").Text);
                Assert.Equal("1", GetControl<DomainUpDown>(form, "domainUpDownAlpha").Text);
            }
            finally
            {
                form.Dispose();
            }
        }

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
                AssertCollisionPreviewDoesNotCoverColorControls(form);

                form.ClientSize = new Size(1600, 900);
                form.PerformLayout();
                AssertLinkPageGeometry(form);
                AssertInertiaMatrixMirrors(form);
                AssertCollisionPreviewDoesNotCoverColorControls(form);
                AssertJointFooterGeometry(form);
                AssertJointFooterTextFits(form);
                AssertMimicControlsDoNotOverlapFooter(form);
                AssertFooterButtonsFitText(form);

                form.ClientSize = new Size(2560, 1440);
                form.PerformLayout();
                AssertLinkPageGeometry(form);
                AssertInertiaMatrixMirrors(form);
                AssertCollisionPreviewDoesNotCoverColorControls(form);
                AssertJointFooterGeometry(form);
                AssertJointFooterTextFits(form);
                AssertMimicControlsDoNotOverlapFooter(form);
                AssertFooterButtonsFitText(form);

                form.Scale(new SizeF(1.5F, 1.5F));
                form.PerformLayout();
                AssertLinkPageGeometry(form);
                AssertInertiaMatrixMirrors(form);
                AssertCollisionPreviewDoesNotCoverColorControls(form);
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
                firstNote.Text =
                    "\u7a7a\u767d\u9879\u4e0d\u4f1a\u5199\u5165 URDF\u3002" +
                    "\u8bf7\u786e\u8ba4\u5750\u6807\u7cfb\u3001\u8f74\u548c\u60ef\u6027\u53c2\u6570\u5df2\u7ecf\u5728 SolidWorks \u4e2d\u6b63\u786e\u914d\u7f6e\u3002";
                secondNote.Text =
                    "* \u5b57\u6bb5\u7ec4\u4e3a\u5fc5\u586b\uff1b\u5982\u679c\u5b57\u6bb5\u8fc7\u957f\uff0c" +
                    "\u754c\u9762\u5fc5\u987b\u6362\u884c\u663e\u793a\u800c\u4e0d\u662f\u88c1\u5207\u3002";

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

        [Fact]
        public void TestLinkFooterScrollsBelowExpandedLocalizedContent()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                Panel panel = GetControl<Panel>(form, "panelLinkProperties");
                GroupBox meshGroup = GetControl<GroupBox>(form, "groupBox4");

                form.ClientSize = new Size(1073, 634);
                meshGroup.Height += 90;
                form.PerformLayout();

                AssertLinkPageGeometry(form);
                AssertLinkFooterBelowContent(form);
                Assert.True(
                    panel.AutoScrollMinSize.Height >= GetControl<Button>(form, "buttonLinksFinish").Bottom + 4,
                    String.Format("Link panel scroll height {0} does not include the footer button row ending at {1}.",
                        panel.AutoScrollMinSize.Height,
                        GetControl<Button>(form, "buttonLinksFinish").Bottom));
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestLinkFooterLayoutDoesNotCreateStaleScrollBar()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                Panel panel = GetControl<Panel>(form, "panelLinkProperties");
                Button finishButton = GetControl<Button>(form, "buttonLinksFinish");
                Label packageLabel = GetControl<Label>(form, "labelRosPackageName");
                Label packageHint = GetControl<Label>(form, "labelRosPackageNameHint");
                TextBox packageName = GetControl<TextBox>(form, "textBoxRosPackageName");

                form.ClientSize = new Size(1344, 812);
                form.PerformLayout();
                int buttonTop = finishButton.Top;
                Size scrollMinSize = panel.AutoScrollMinSize;

                for (int i = 0; i < 20; i++)
                {
                    form.PerformLayout();
                }

                Assert.Equal(buttonTop, finishButton.Top);
                Assert.Equal(scrollMinSize, panel.AutoScrollMinSize);
                Assert.True(
                    panel.AutoScrollMinSize.Height == 0 ||
                    panel.AutoScrollMinSize.Height <= panel.ClientSize.Height,
                    String.Format("Link page kept stale scroll height {0} for client height {1}.",
                        panel.AutoScrollMinSize.Height,
                        panel.ClientSize.Height));

                packageName.Text = "rover_description";
                Assert.Equal("ROS1/2: rover_description", packageHint.Text);
                Assert.DoesNotContain("and", packageHint.Text);
                Assert.DoesNotContain("\u548c", packageHint.Text);
                Assert.True(
                    packageLabel.Text == "ROS package" ||
                    packageLabel.Text == "ROS \u5305\u540d");
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestMimicToggleLayoutIsIdempotent()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                form.ClientSize = new Size(1344, 812);
                form.PerformLayout();

                CheckBox mimicCheckBox = GetControl<CheckBox>(form, "MimicCheckBox");
                ComboBox mimicJointComboBox = GetControl<ComboBox>(form, "MimicJointComboBox");
                Label mimicJointLabel = GetControl<Label>(form, "MimicJointLabel");
                Button cancelButton = GetControl<Button>(form, "buttonJointCancel");
                Button nextButton = GetControl<Button>(form, "buttonJointNext");

                mimicCheckBox.Checked = true;
                form.PerformLayout();
                Rectangle comboBounds = mimicJointComboBox.Bounds;
                Rectangle labelBounds = mimicJointLabel.Bounds;
                Rectangle cancelBounds = cancelButton.Bounds;
                Rectangle nextBounds = nextButton.Bounds;
                TextBox multiplier = GetControl<TextBox>(form, "textBoxMimicMultiplier");
                TextBox offset = GetControl<TextBox>(form, "textBoxMimicOffset");
                multiplier.Text = "-1.25";
                offset.Text = "0.4";

                for (int i = 0; i < 20; i++)
                {
                    mimicCheckBox.Checked = false;
                    form.PerformLayout();
                    mimicCheckBox.Checked = true;
                    form.PerformLayout();
                }

                Assert.Equal(comboBounds, mimicJointComboBox.Bounds);
                Assert.Equal(labelBounds, mimicJointLabel.Bounds);
                Assert.Equal(cancelBounds.Width, cancelButton.Width);
                Assert.Equal(cancelBounds.Left, cancelButton.Left);
                Assert.Equal(nextBounds.Width, nextButton.Width);
                Assert.Equal(nextBounds.Right, nextButton.Right);
                Assert.Equal("-1.25", multiplier.Text);
                Assert.Equal("0.4", offset.Text);
                AssertMimicControlsDoNotOverlapFooter(form);
                AssertFooterButtonsFitText(form);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestUsageGuideButtonAndMaterialNamesAreAvailable()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                form.ClientSize = new Size(1344, 812);
                form.PerformLayout();

                Button guideButton = GetControl<Button>(form, "buttonUsageGuide");
                ComboBox materials = GetControl<ComboBox>(form, "comboBoxMaterials");

                Assert.True(form.Controls.Contains(guideButton));
                Assert.True(guideButton.Enabled);
                Assert.True(guideButton.Right <= form.ClientSize.Width - 12);
                Assert.True(guideButton.Top >= 8);
                Assert.True(guideButton.Text == "Guide" || guideButton.Text == "使用说明");
                Assert.True(materials.Items.Contains("aluminum"));
                Assert.True(materials.Items.Contains("rubber_black"));
                Assert.True(materials.Items.Contains("transparent_blue"));
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestUsageGuideDocumentsMvpChoices()
        {
            string guide = UsageGuideForm.BuildGuideText(false);

            Assert.Contains("Recommended default: ComponentBoxes", guide);
            Assert.Contains("Tools > URDF Export Tutorial", guide);
            Assert.Contains("eight-step companion tutorial", guide);
            Assert.Contains("Automatic Link tree loading", guide);
            Assert.Contains("URDF Export Configuration (v1.5)", guide);
            Assert.Contains("Link tree outline editing", guide);
            Assert.Contains("Link frames and inertia", guide);
            Assert.Contains("without a parallel-axis shift", guide);
            Assert.Contains("#/##/###", guide);
            Assert.Contains("config/inertial_validation.csv", guide);
            Assert.Contains("CylinderPrimitive", guide);
            Assert.Contains("export_report.md", guide);
            Assert.Contains("mesh_manifest.csv", guide);
            Assert.Contains("aluminum", guide);
            Assert.Contains("rubber_black", guide);
            Assert.Equal(
                "https://github.com/osrbot/solidworks_urdf_exporter_pro",
                UsageGuideForm.ProjectUrl);
            Assert.Equal("kitso666 <kitso@osrbot.com>", UsageGuideForm.VersionMaintainer);
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

        private static void AssertCollisionPreviewDoesNotCoverColorControls(
            AssemblyExportForm form)
        {
            Button previewButton = GetControl<Button>(form, "buttonShowCollisionPreview");
            Label previewStatus = GetControl<Label>(form, "labelCollisionPreviewStatus");
            DomainUpDown red = GetControl<DomainUpDown>(form, "domainUpDownRed");
            DomainUpDown blue = GetControl<DomainUpDown>(form, "domainUpDownBlue");
            DomainUpDown alpha = GetControl<DomainUpDown>(form, "domainUpDownAlpha");

            Assert.False(previewButton.Bounds.IntersectsWith(red.Bounds));
            Assert.False(previewStatus.Bounds.IntersectsWith(blue.Bounds));
            Assert.False(previewStatus.Bounds.IntersectsWith(alpha.Bounds));
        }

        private static void AssertLinkPageGeometry(AssemblyExportForm form)
        {
            Panel panel = GetControl<Panel>(form, "panelLinkProperties");
            Label packageLabel = GetControl<Label>(form, "labelRosPackageName");
            Label packageHint = GetControl<Label>(form, "labelRosPackageNameHint");
            GroupBox inertiaGroup = GetControl<GroupBox>(form, "groupBox5");
            GroupBox meshGroup = GetControl<GroupBox>(form, "groupBox4");
            TreeView tree = GetControl<TreeView>(form, "treeViewLinkProperties");
            Button cancelButton = GetControl<Button>(form, "buttonLinksCancel");
            Button previousButton = GetControl<Button>(form, "buttonLinksPrevious");
            Button exportUrdfOnlyButton = GetControl<Button>(form, "buttonLinksExportUrdfOnly");
            Button finishButton = GetControl<Button>(form, "buttonLinksFinish");
            ComboBox collisionStrategy = GetControl<ComboBox>(form, "comboBoxCollisionStrategy");
            Label collisionStrategyLabel = GetControl<Label>(form, "labelCollisionStrategy");
            Button collisionPreviewButton =
                GetControl<Button>(form, "buttonShowCollisionPreview");
            Label collisionPreviewStatus =
                GetControl<Label>(form, "labelCollisionPreviewStatus");
            GroupBox meshFormatGroup = GetControl<GroupBox>(form, "groupBox1");
            ComboBox linkCoordinateSystem =
                GetControl<ComboBox>(form, "comboBoxLinkCoordinateSystem");
            Label linkCoordinateSystemLabel =
                GetControl<Label>(form, "labelLinkCoordinateSystem");
            Label inertialOriginLabel = GetControl<Label>(form, "label36");
            Label inertiaMatrixLabel = GetControl<Label>(form, "label44");
            Label inertiaPreviewStatus =
                GetControl<Label>(form, "labelInertiaPreviewStatus");
            TextBox visualYaw = GetControl<TextBox>(form, "textBoxVisualOriginYaw");
            DomainUpDown colorRed = GetControl<DomainUpDown>(form, "domainUpDownRed");
            int scrollBottom = Math.Max(panel.ClientSize.Height, panel.AutoScrollMinSize.Height);

            Assert.Equal(inertiaGroup.Left, packageLabel.Left);
            Assert.True(packageHint.Right <= inertiaGroup.Right);
            Assert.True(tree.Right < inertiaGroup.Left);
            Assert.True(meshGroup.Right <= panel.ClientSize.Width);
            Assert.True(finishButton.Right <= panel.ClientSize.Width);
            Assert.True(finishButton.Bottom <= scrollBottom);
            Assert.Equal(cancelButton.Top, previousButton.Top);
            Assert.Equal(cancelButton.Top, exportUrdfOnlyButton.Top);
            Assert.Equal(cancelButton.Top, finishButton.Top);
            AssertLinkFooterBelowContent(form);
            Assert.Equal(ComboBoxStyle.DropDownList, collisionStrategy.DropDownStyle);
            Assert.Equal(ComboBoxStyle.DropDownList, linkCoordinateSystem.DropDownStyle);
            Assert.True(linkCoordinateSystemLabel.Right < linkCoordinateSystem.Left);
            Assert.True(linkCoordinateSystem.Right <= inertiaGroup.ClientSize.Width);
            AssertControlRowsDoNotOverlap(
                linkCoordinateSystem,
                inertialOriginLabel,
                "Link frame selector overlaps the inertial origin heading.");
            AssertControlRowsDoNotOverlap(
                linkCoordinateSystem,
                inertiaMatrixLabel,
                "Link frame selector overlaps the inertia matrix heading.");
            Assert.Contains("mm", inertiaPreviewStatus.Text);
            Assert.True(collisionStrategy.Left > visualYaw.Right);
            Assert.True(collisionStrategy.Right < colorRed.Left);
            Assert.True(collisionStrategyLabel.Bottom <= collisionStrategy.Top);
            Assert.Equal(collisionStrategy.Left, collisionPreviewButton.Left);
            Assert.True(collisionPreviewButton.Bottom <= collisionPreviewStatus.Top);
            Assert.True(collisionPreviewStatus.Bottom < meshFormatGroup.Top);
            Assert.True(collisionPreviewStatus.Right < meshGroup.ClientSize.Width);
        }

        private static void AssertControlRowsDoNotOverlap(
            Control upperControl,
            Control lowerControl,
            string message)
        {
            int actualGap = lowerControl.Top - upperControl.Bottom;
            Assert.True(
                actualGap >= 4,
                String.Format("{0} Actual gap: {1}px.", message, actualGap));
        }

        private static void AssertLinkFooterBelowContent(AssemblyExportForm form)
        {
            GroupBox inertiaGroup = GetControl<GroupBox>(form, "groupBox5");
            GroupBox meshGroup = GetControl<GroupBox>(form, "groupBox4");
            TreeView tree = GetControl<TreeView>(form, "treeViewLinkProperties");
            Button finishButton = GetControl<Button>(form, "buttonLinksFinish");
            int contentBottom = Math.Max(inertiaGroup.Bottom, meshGroup.Bottom);

            Assert.True(
                contentBottom + 8 <= finishButton.Top,
                String.Format("Link footer overlaps content: content bottom {0}, footer top {1}.",
                    contentBottom, finishButton.Top));
            Assert.True(
                tree.Bottom + 8 <= finishButton.Top,
                String.Format("Link footer overlaps tree: tree bottom {0}, footer top {1}.",
                    tree.Bottom, finishButton.Top));
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
                GetControl<Button>(form, "buttonLinksCancel"),
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
