using SW2URDF.UI;
using SW2URDF.URDFExport;
using OSURDF.Core.Model;
using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Xunit;
using UrdfJoint = SW2URDF.URDF.Joint;

namespace SW2URDF.Test
{
    public class TestAssemblyExportLayout
    {
        [Theory]
        [InlineData("", "请选择 Joint 类型（必填）", "Select joint type (required)")]
        [InlineData(
            "Automatically Detect",
            "Automatically Detect / 尝试从 SolidWorks Mate 识别（仅原生可动装配）",
            "Try SolidWorks Mate detection (native movable assemblies only)")]
        [InlineData("revolute", "revolute / 有限角度转动", "revolute")]
        [InlineData("continuous", "continuous / 无约束连续转动", "continuous")]
        [InlineData("prismatic", "prismatic / 直线滑动", "prismatic")]
        [InlineData("fixed", "fixed / 固定连接", "fixed")]
        [InlineData("floating", "floating / 六自由度运动", "floating")]
        [InlineData("planar", "planar / 平面运动", "planar")]
        public void TestChineseJointTypeDisplayRoundTripsToUrdfValue(
            string jointType,
            string chineseDisplay,
            string englishDisplay)
        {
            Assert.Equal(chineseDisplay, ChineseUiText.JointTypeDisplay(jointType, true));
            Assert.Equal(englishDisplay, ChineseUiText.JointTypeDisplay(jointType, false));
            Assert.Equal(jointType, ChineseUiText.JointTypeValue(chineseDisplay));
            Assert.Equal(jointType, ChineseUiText.JointTypeValue(englishDisplay));
        }

        [Fact]
        public void TestUnknownJointTypeDisplayIsPreserved()
        {
            Assert.Equal("custom", ChineseUiText.JointTypeDisplay("custom", true));
            Assert.Equal("custom", ChineseUiText.JointTypeValue("custom"));
        }

        [Fact]
        public void TestFourExportTargetsAreExplicitAndCapturedWithoutLegacyProfiles()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);
            try
            {
                CheckBox ros1 = GetControl<CheckBox>(form, "modernRos1CheckBox");
                CheckBox ros2 = GetControl<CheckBox>(form, "modernRos2CheckBox");
                CheckBox usd = GetControl<CheckBox>(form, "modernUsdAssetCheckBox");
                CheckBox mjcf = GetControl<CheckBox>(form, "modernMjcfAssetCheckBox");
                Button usdSettings = GetControl<Button>(form, "modernUsdSettingsButton");
                Control targetRow = GetControl<Control>(form, "modernExportTargetRow");
                Control modelFooter = GetControl<Control>(form, "modernModelFooter");
                TableLayoutPanel footerLayout = GetControl<TableLayoutPanel>(
                    form,
                    "modernModelFooterLayout");
                Button previous = GetControl<Button>(form, "modernModelPreviousButton");
                Button urdfOnly = GetControl<Button>(form, "buttonLinksExportUrdfOnly");
                Button finish = GetControl<Button>(form, "buttonLinksFinish");
                Assert.Equal(
                    ChineseUiText.Translate("ROS 1 package", "ROS 1 功能包"),
                    ros1.Text);
                Assert.Equal(
                    ChineseUiText.Translate("ROS 2 package", "ROS 2 功能包"),
                    ros2.Text);
                Assert.Equal(
                    ChineseUiText.Translate(
                        "OpenUSD robot asset",
                        "OpenUSD 机器人资产"),
                    usd.Text);
                Assert.Equal(
                    ChineseUiText.Translate("MuJoCo MJCF asset", "MuJoCo MJCF 资产"),
                    mjcf.Text);
                Assert.Equal(
                    ChineseUiText.Translate("OpenUSD settings...", "OpenUSD 设置..."),
                    usdSettings.Text);
                Assert.Same(targetRow, ros1.Parent);
                Assert.Same(targetRow, ros2.Parent);
                Assert.Same(targetRow, usd.Parent);
                Assert.Same(targetRow, mjcf.Parent);
                Assert.Same(footerLayout, usdSettings.Parent);
                Assert.True(IsDescendantOf(usdSettings, modelFooter));
                Assert.Equal(ros1.Margin.Top, ros2.Margin.Top);
                Assert.Equal(ros1.Margin.Top, usd.Margin.Top);
                Assert.Equal(ros1.Margin.Top, mjcf.Margin.Top);
                form.PerformLayout();
                targetRow.PerformLayout();
                Assert.Equal(ros1.Top, ros2.Top);
                Assert.Equal(ros1.Top, usd.Top);
                Assert.Equal(ros1.Top, mjcf.Top);
                Assert.Equal(2, footerLayout.GetPositionFromControl(previous).Column);
                Assert.Equal(3, footerLayout.GetPositionFromControl(usdSettings).Column);
                Assert.Equal(4, footerLayout.GetPositionFromControl(urdfOnly).Column);
                Assert.Equal(5, footerLayout.GetPositionFromControl(finish).Column);
                Assert.Null(FindDescendant(form, "modernExportTargetActions"));
                Assert.Null(FindDescendant(form, "modernRos2PairComboBox"));
                Assert.Null(FindDescendant(form, "modernRos2ControlProfileButton"));
                Assert.Null(FindDescendant(form, "modernIsaacLabProfileButton"));
                Assert.Null(FindDescendant(form, "modernUsdProfilePage"));
                Assert.False(usdSettings.Enabled);

                ros1.Checked = false;
                ros2.Checked = true;
                usd.Checked = true;
                mjcf.Checked = true;
                Assert.True(usdSettings.Enabled);
                MethodInfo capture = typeof(AssemblyExportForm).GetMethod(
                    "CaptureExportTargetOptions",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(capture);
                ExportTargetOptions options = (ExportTargetOptions)capture.Invoke(form, null);
                Assert.False(ReadBooleanProperty(options, "ExportRos1Legacy"));
                Assert.True(ReadBooleanProperty(options, "ExportRos2"));
                AssertOptionalBooleanProperty(options, "ExportUsdAsset", true);
                AssertOptionalBooleanProperty(options, "ExportMjcfAsset", true);
                Assert.True(GetControl<RadioButton>(form, "radioButtonStl").Checked);
                Assert.False(GetControl<RadioButton>(form, "radioButton3dxml").Enabled);

                usd.Checked = false;
                mjcf.Checked = false;
                Assert.False(usdSettings.Enabled);
                Assert.True(GetControl<RadioButton>(form, "radioButton3dxml").Enabled);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestAssemblyMaterialIdIsReadOnlyAndGeneratedOnlyFromRgba()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                ComboBox legacyMaterials = GetControl<ComboBox>(form, "comboBoxMaterials");
                TextBox materialId = GetControl<TextBox>(form, "modernMaterialIdTextBox");
                Label materialLabel = GetControl<Label>(form, "label28");
                DomainUpDown red = GetControl<DomainUpDown>(form, "domainUpDownRed");
                DomainUpDown green = GetControl<DomainUpDown>(form, "domainUpDownGreen");
                DomainUpDown blue = GetControl<DomainUpDown>(form, "domainUpDownBlue");
                DomainUpDown alpha = GetControl<DomainUpDown>(form, "domainUpDownAlpha");

                red.Text = "0.05";
                green.Text = "0.6";
                blue.Text = "0.1";
                alpha.Text = "1";

                Assert.Null(typeof(AssemblyExportForm).GetField(
                    "textBoxTexture",
                    BindingFlags.Instance | BindingFlags.NonPublic));
                Assert.Null(typeof(AssemblyExportForm).GetField(
                    "buttonTextureBrowse",
                    BindingFlags.Instance | BindingFlags.NonPublic));
                Assert.False(legacyMaterials.Visible);
                Assert.Empty(legacyMaterials.Items.Cast<object>().Where(
                    item => !String.Equals(item.ToString(), materialId.Text, StringComparison.Ordinal)));
                Assert.True(materialId.ReadOnly);
                Assert.Matches(
                    "^material_0d991aff_[0-9a-f]{12}$",
                    materialId.Text);
                Assert.True(materialLabel.Bottom <= materialId.Top);

                string firstMaterialId = materialId.Text;
                red.Text = "0.0501";
                Assert.NotEqual(firstMaterialId, materialId.Text);
                Assert.Matches(
                    "^material_0d991aff_[0-9a-f]{12}$",
                    materialId.Text);

                red.Text = "1.1";
                Assert.Equal(String.Empty, materialId.Text);

                red.Text = "0.05";
                alpha.Text = "0.5";
                Assert.Matches(
                    "^material_0d991a80_[0-9a-f]{12}$",
                    materialId.Text);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestPartMaterialPresetUpdatesRgbaWithoutTextureEditor()
        {
            PartExportForm form = (PartExportForm)
                Activator.CreateInstance(typeof(PartExportForm), true);

            try
            {
                ComboBox materials = GetPrivateControl<ComboBox>(
                    form,
                    typeof(PartExportForm),
                    "comboBox_materials");
                materials.Text = "blue";

                MethodInfo applyPreset = typeof(PartExportForm).GetMethod(
                    "MaterialPresetSelectionChangeCommitted",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(applyPreset);
                applyPreset.Invoke(form, new object[] { materials, EventArgs.Empty });

                Assert.Null(typeof(PartExportForm).GetField(
                    "textBox_texture",
                    BindingFlags.Instance | BindingFlags.NonPublic));
                Assert.Null(typeof(PartExportForm).GetField(
                    "button_texturebrowse",
                    BindingFlags.Instance | BindingFlags.NonPublic));
                Assert.Equal("0.05", GetPrivateControl<DomainUpDown>(
                    form, typeof(PartExportForm), "domainUpDown_red").Text);
                Assert.Equal("0.2", GetPrivateControl<DomainUpDown>(
                    form, typeof(PartExportForm), "domainUpDown_green").Text);
                Assert.Equal("0.8", GetPrivateControl<DomainUpDown>(
                    form, typeof(PartExportForm), "domainUpDown_blue").Text);
                Assert.Equal("1", GetPrivateControl<DomainUpDown>(
                    form, typeof(PartExportForm), "domainUpDown_alpha").Text);

                GroupBox visual = GetPrivateControl<GroupBox>(
                    form, typeof(PartExportForm), "groupBox2");
                GroupBox collision = GetPrivateControl<GroupBox>(
                    form, typeof(PartExportForm), "groupBox3");
                Button finish = GetPrivateControl<Button>(
                    form, typeof(PartExportForm), "button_finish");
                Label materialIdLabel = GetPrivateControl<Label>(
                    form, typeof(PartExportForm), "label28");
                Assert.True(materialIdLabel.Bottom <= materials.Top);
                Assert.True(visual.Bottom < collision.Top);
                Assert.True(
                    collision.Bottom < finish.Top,
                    String.Format(
                        CultureInfo.InvariantCulture,
                        "Collision bottom {0} must remain above finish top {1}.",
                        collision.Bottom,
                        finish.Top));

                AssertInputBordersStayInside(GetPrivateControl<GroupBox>(
                    form, typeof(PartExportForm), "groupBox1"));
                AssertInputBordersStayInside(visual);
                AssertInputBordersStayInside(collision);

                TextBox saveAs = GetPrivateControl<TextBox>(
                    form, typeof(PartExportForm), "textBox_save_as");
                AssertControlBottomBorderStaysInside(saveAs, form);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestAutomaticLinkColorButtonIsAvailableWithoutCoveringColorControls()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                TabControl sections = GetControl<TabControl>(form, "modernLinkSections");
                sections.SelectedIndex = 2;
                sections.PerformLayout();
                Panel appearance = GetControl<Panel>(form, "modernAppearancePanel");
                appearance.PerformLayout();
                Button automaticColors = GetControl<Button>(
                    form,
                    "buttonAutomaticLinkColors");
                Button pickColor = GetControl<Button>(form, "buttonMaterialColorPick");
                DomainUpDown alpha = GetControl<DomainUpDown>(form, "domainUpDownAlpha");
                Label meshReduction = GetControl<Label>(form, "labelMeshReduction");

                Assert.True(IsDescendantOf(automaticColors, appearance));
                Assert.True(automaticColors.Enabled);
                Assert.False(BoundsRelativeTo(automaticColors, appearance).IntersectsWith(
                    BoundsRelativeTo(pickColor, appearance)));
                Assert.False(BoundsRelativeTo(automaticColors, appearance).IntersectsWith(
                    BoundsRelativeTo(alpha, appearance)));
                Assert.False(IsDescendantOf(meshReduction, appearance));
                Assert.True(BoundsRelativeTo(
                    automaticColors,
                    appearance).Right <= appearance.ClientSize.Width);
                Assert.False(String.IsNullOrWhiteSpace(automaticColors.Text));
                Assert.True(automaticColors.Width >= TextRenderer.MeasureText(
                    automaticColors.Text,
                    automaticColors.Font).Width + 8);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestModernLayoutParticipatesInDpiScaling()
        {
            float[] scaleFactors = new float[] { 1.25F, 1.5F, 2F };
            foreach (float scaleFactor in scaleFactors)
            {
                AssemblyExportForm form = (AssemblyExportForm)
                    Activator.CreateInstance(typeof(AssemblyExportForm), true);

                try
                {
                    Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
                    Panel jointRoot = GetControl<Panel>(form, "modernJointRoot");
                    Panel linkRoot = GetControl<Panel>(form, "panelLinkProperties");
                    Panel jointContent = GetControl<Panel>(form, "modernJointContentPanel");
                    Panel linkContent = GetControl<Panel>(form, "modernLinkContentPanel");
                    TableLayoutPanel jointBody = GetControl<TableLayoutPanel>(
                        form,
                        "modernJointBody");
                    Control jointHeader = GetControl<Control>(form, "modernJointHeader");
                    Button next = GetControl<Button>(form, "buttonJointNext");
                    Button finish = GetControl<Button>(form, "buttonLinksFinish");
                    TableLayoutPanel jointIdentityAndReference =
                        GetControl<TableLayoutPanel>(
                            form,
                            "modernJointIdentityAndReferenceGrid");
                    Control jointIdentity = GetControl<Control>(
                        form,
                        "modernJointIdentityCard");
                    Control referenceGeometry = GetControl<Control>(
                        form,
                        "modernReferenceGeometryCard");
                    int originalBodyPadding = jointBody.Padding.Left;
                    int originalHeaderPadding = jointHeader.Padding.Left;
                    float originalTreeColumnWidth = jointBody.ColumnStyles[0].Width;
                    Size originalButtonSize = next.Size;
                    Size originalFinishSize = finish.Size;

                    Assert.Equal(DockStyle.Fill, jointRoot.Dock);
                    Assert.False(linkRoot.AutoScroll);
                    Assert.False(jointContent.AutoScroll);
                    Assert.False(linkContent.AutoScroll);
                    Assert.Equal(
                        DockStyle.Fill,
                        GetControl<TreeView>(form, "treeViewJointTree").Dock);

                    form.Scale(new SizeF(scaleFactor, scaleFactor));
                    form.PerformLayout();

                    Assert.True(jointBody.Padding.Left > originalBodyPadding);
                    Assert.True(jointHeader.Padding.Left > originalHeaderPadding);
                    Assert.True(
                        jointBody.ColumnStyles[0].Width > originalTreeColumnWidth);
                    Assert.True(next.Width > originalButtonSize.Width);
                    Assert.True(next.Height > originalButtonSize.Height);
                    Assert.True(finish.Width > originalFinishSize.Width);
                    Assert.True(finish.Height > originalFinishSize.Height);
                    Assert.Equal(
                        DockStyle.Fill,
                        GetControl<TreeView>(form, "treeViewJointTree").Dock);
                    Assert.Equal(
                        DockStyle.Fill,
                        GetControl<TreeView>(form, "treeViewLinkProperties").Dock);
                    AssertContainedIn(
                        GetControl<Label>(form, "label7"),
                        jointHeader);
                    AssertContainedIn(
                        GetControl<Label>(form, "modernJointSubtitle"),
                        jointHeader);
                    AssertContainedIn(
                        GetControl<Label>(form, "modernJointStep"),
                        jointHeader);
                    AssertContainedIn(
                        GetControl<Button>(form, "buttonUsageGuide"),
                        jointHeader);
                    AssertContainedIn(jointIdentity, jointIdentityAndReference);
                    AssertContainedIn(referenceGeometry, jointIdentityAndReference);
                    Assert.Equal(
                        BoundsRelativeTo(jointIdentity, jointIdentityAndReference).Width,
                        BoundsRelativeTo(referenceGeometry, jointIdentityAndReference).Width);
                    AssertContainedIn(
                        GetControl<ComboBox>(form, "comboBoxOrigin"),
                        referenceGeometry);
                    AssertContainedIn(
                        GetControl<ComboBox>(form, "comboBoxAxis"),
                        referenceGeometry);
                    AssertFooterButtonsFitText(form);
                }
                finally
                {
                    form.Dispose();
                }
            }
        }

        [Theory]
        [InlineData(1560, 1020, 1920, 1040, 1560, 1020)]
        [InlineData(2240, 1400, 1904, 1001, 1904, 1001)]
        [InlineData(0, 0, 0, 0, 1, 1)]
        public void TestModernLayoutSizeIsConstrainedToWorkingArea(
            int desiredWidth,
            int desiredHeight,
            int maximumWidth,
            int maximumHeight,
            int expectedWidth,
            int expectedHeight)
        {
            Size constrained = AssemblyExportForm.ConstrainModernSize(
                new Size(desiredWidth, desiredHeight),
                new Size(maximumWidth, maximumHeight));

            Assert.Equal(new Size(expectedWidth, expectedHeight), constrained);
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
        public void TestModernPropertyPagesDoNotRequireWholePageScrolling()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                Panel outerPanel = GetControl<Panel>(form, "panelLinkProperties");
                Panel linkContent = GetControl<Panel>(form, "modernLinkContentPanel");
                Panel jointPanel = GetControl<Panel>(form, "modernJointContentPanel");
                TabControl jointSections = GetControl<TabControl>(
                    form,
                    "modernJointSections");
                TabControl linkSections = GetControl<TabControl>(
                    form,
                    "modernLinkSections");
                Control footer = GetControl<Control>(form, "modernLinkFooter");

                Assert.False(outerPanel.AutoScroll);
                Assert.False(jointPanel.AutoScroll);
                Assert.False(linkContent.AutoScroll);
                Assert.True(IsDescendantOf(linkContent, outerPanel));
                Assert.True(IsDescendantOf(footer, outerPanel));
                Assert.False(IsDescendantOf(footer, linkContent));
                Assert.Equal(3, jointSections.TabPages.Count);
                Assert.Equal(3, linkSections.TabPages.Count);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestModernLinkLayoutIsStableAcrossRepeatedLayout()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                Panel linkRoot = GetControl<Panel>(form, "panelLinkProperties");
                Panel linkContent = GetControl<Panel>(form, "modernLinkContentPanel");
                Control footer = GetControl<Control>(form, "modernLinkFooter");

                form.ClientSize = new Size(1344, 812);
                form.PerformLayout();
                linkRoot.PerformLayout();
                Rectangle footerBounds = footer.Bounds;
                Rectangle contentBounds = linkContent.Bounds;

                for (int i = 0; i < 20; i++)
                {
                    form.PerformLayout();
                    linkRoot.PerformLayout();
                }

                Assert.Equal(footerBounds, footer.Bounds);
                Assert.Equal(contentBounds, linkContent.Bounds);
                Assert.False(linkContent.AutoScroll);
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
        public void TestLegacyMimicLayoutCannotOverrideModernDocking()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                TreeView jointTree = GetControl<TreeView>(form, "treeViewJointTree");
                Panel jointRoot = GetControl<Panel>(form, "modernJointRoot");
                MethodInfo legacyPosition = typeof(AssemblyExportForm).GetMethod(
                    "PositionJointFooterControls",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo showMimic = typeof(AssemblyExportForm).GetMethod(
                    "ShowMimicControls",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(legacyPosition);
                Assert.NotNull(showMimic);

                Rectangle originalBounds = jointTree.Bounds;
                legacyPosition.Invoke(form, null);
                showMimic.Invoke(form, new object[] { true });
                GetControl<CheckBox>(form, "MimicCheckBox").Checked = true;
                form.PerformLayout();

                Assert.Equal(DockStyle.Fill, jointTree.Dock);
                Assert.True(IsDescendantOf(jointTree, jointRoot));
                Assert.Equal(originalBounds, jointTree.Bounds);
                Assert.True(IsDescendantOf(
                    GetControl<ComboBox>(form, "MimicJointComboBox"),
                    GetControl<TableLayoutPanel>(form, "modernMimicCard")));
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestModernAssemblyLayoutPreservesLatestFeatureControls()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                form.PerformLayout();

                Panel jointRoot = GetControl<Panel>(form, "modernJointRoot");
                Panel linkRoot = GetControl<Panel>(form, "panelLinkProperties");
                Panel modelRoot = GetControl<Panel>(form, "modernModelRoot");
                Label reusedLinkSubtitle = GetControl<Label>(form, "label2");
                GroupBox inertiaGroup = GetControl<GroupBox>(form, "groupBox5");
                GroupBox meshGroup = GetControl<GroupBox>(form, "groupBox4");
                Panel appearancePanel = GetControl<Panel>(
                    form,
                    "modernAppearancePanel");
                ComboBox linkFrame = GetControl<ComboBox>(
                    form,
                    "comboBoxLinkCoordinateSystem");
                Button automaticColors = GetControl<Button>(
                    form,
                    "buttonAutomaticLinkColors");
                Button collisionPreview = GetControl<Button>(
                    form,
                    "buttonShowCollisionPreview");
                Button inertiaPreview = GetControl<Button>(
                    form,
                    "buttonShowInertiaPreview");
                Button next = GetControl<Button>(form, "buttonJointNext");

                Assert.Equal(DockStyle.Fill, jointRoot.Dock);
                Assert.False(linkRoot.AutoScroll);
                Assert.True(IsDescendantOf(
                    GetControl<TreeView>(form, "treeViewJointTree"),
                    jointRoot));
                Assert.True(IsDescendantOf(
                    GetControl<TreeView>(form, "treeViewLinkProperties"),
                    linkRoot));
                Assert.True(IsDescendantOf(reusedLinkSubtitle, linkRoot));
                Assert.Equal("modernLinkSubtitle", reusedLinkSubtitle.Name);
                Assert.True(IsDescendantOf(linkFrame, inertiaGroup));
                Assert.True(IsDescendantOf(inertiaPreview, inertiaGroup));
                Assert.True(IsDescendantOf(automaticColors, appearancePanel));
                Assert.True(IsDescendantOf(collisionPreview, meshGroup));
                Assert.Null(FindDescendant(linkRoot, "modernPackageCard"));
                Assert.NotNull(FindDescendant(modelRoot, "modernPackageCard"));
                Assert.NotNull(FindDescendant(jointRoot, "modernMimicCard"));

                Assert.Equal(FlatStyle.Flat, next.FlatStyle);
                Assert.Equal(ModernWinFormsTheme.Accent, next.BackColor);
                Assert.True(ModernWinFormsTheme.Accent.B > ModernWinFormsTheme.Accent.R);
                Assert.True(
                    ModernWinFormsTheme.AccentHover.G >
                    ModernWinFormsTheme.AccentHover.R);
                Assert.True(ContrastRatio(
                    Color.White,
                    ModernWinFormsTheme.AccentHover) >= 4.5D);
                AssertInertiaMatrixMirrors(form);
                AssertCollisionPreviewDoesNotCoverColorControls(form);
                AssertEditorTabOrder(form);
                AssertFooterTabOrder(form);

                CheckBox mimic = GetControl<CheckBox>(form, "MimicCheckBox");
                TextBox multiplier = GetControl<TextBox>(
                    form,
                    "textBoxMimicMultiplier");
                TextBox offset = GetControl<TextBox>(form, "textBoxMimicOffset");
                mimic.Checked = false;
                multiplier.Text = "";
                offset.Text = "";
                mimic.Checked = true;
                Assert.Equal("1.0", multiplier.Text);
                Assert.Equal("0.0", offset.Text);

                int jointControlCount = jointRoot.Controls.Count;
                form.InitializeModernUi();
                Assert.Same(jointRoot, GetControl<Panel>(form, "modernJointRoot"));
                Assert.Equal(jointControlCount, jointRoot.Controls.Count);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestJointIdentityShowsParentToChildDirection()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                form.ClientSize = new Size(1344, 812);
                form.PerformLayout();

                Control card = GetControl<Control>(form, "modernJointIdentityCard");
                Label parent = GetControl<Label>(form, "labelParent");
                Label child = GetControl<Label>(form, "labelChild");
                Label arrow = GetControl<Label>(form, "modernJointRelationArrow");
                Rectangle parentBounds = BoundsRelativeTo(parent, card);
                Rectangle childBounds = BoundsRelativeTo(child, card);
                Rectangle arrowBounds = BoundsRelativeTo(arrow, card);

                Assert.Equal("\u2192", arrow.Text);
                Assert.False(String.IsNullOrWhiteSpace(arrow.AccessibleName));
                Assert.True(parent.AutoEllipsis);
                Assert.True(child.AutoEllipsis);
                Assert.True(parentBounds.Right <= arrowBounds.Left);
                Assert.True(arrowBounds.Right <= childBounds.Left);
                Assert.False(parentBounds.IntersectsWith(childBounds));
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestJointIdentityAndReferenceGeometryUseStableFullWidthRows()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                form.ClientSize = new Size(1344, 812);
                ShowModernAssemblyPage(form, "Joint");
                TabControl sections = GetControl<TabControl>(
                    form,
                    "modernJointSections");
                sections.SelectedIndex = 0;
                form.PerformLayout();

                TableLayoutPanel stack = GetControl<TableLayoutPanel>(
                    form,
                    "modernJointIdentityAndReferenceGrid");
                Control identity = GetControl<Control>(form, "modernJointIdentityCard");
                Control reference = GetControl<Control>(form, "modernReferenceGeometryCard");
                TableLayoutPanel referenceFields = GetControl<TableLayoutPanel>(
                    form,
                    "modernReferenceGeometryFields");
                Control originAndAxis = GetControl<Control>(form, "modernOriginAndAxisGrid");
                ComboBox origin = GetControl<ComboBox>(form, "comboBoxOrigin");
                ComboBox axis = GetControl<ComboBox>(form, "comboBoxAxis");

                Assert.Equal(1, stack.ColumnCount);
                Assert.Equal(2, stack.RowCount);
                Assert.Equal(0, stack.GetPositionFromControl(identity).Column);
                Assert.Equal(0, stack.GetPositionFromControl(identity).Row);
                Assert.Equal(0, stack.GetPositionFromControl(reference).Column);
                Assert.Equal(1, stack.GetPositionFromControl(reference).Row);

                Rectangle identityBounds = BoundsRelativeTo(identity, stack);
                Rectangle referenceBounds = BoundsRelativeTo(reference, stack);
                Assert.Equal(identityBounds.Left, referenceBounds.Left);
                Assert.Equal(identityBounds.Width, referenceBounds.Width);
                Assert.True(identityBounds.Bottom <= referenceBounds.Top);

                Assert.Equal(0, referenceFields.GetPositionFromControl(origin).Row);
                Assert.Equal(1, referenceFields.GetPositionFromControl(axis).Row);
                Rectangle originBounds = BoundsRelativeTo(origin, reference);
                Rectangle axisBounds = BoundsRelativeTo(axis, reference);
                Assert.Equal(originBounds.Left, axisBounds.Left);
                Assert.Equal(originBounds.Width, axisBounds.Width);
                Assert.True(originBounds.Width >= reference.ClientSize.Width / 2);
                Assert.False(BoundsRelativeTo(reference, stack.Parent).IntersectsWith(
                    BoundsRelativeTo(originAndAxis, stack.Parent)));

                string longReferenceName =
                    "Origin_dist_joint - level_4-1/level_5-2/level_6-3/" +
                    "unicode_deep_reference_link/drive_module/actuator/" +
                    "output_shaft/reference_geometry/coordinate_system_for_export";
                origin.Items.Add(longReferenceName);
                origin.SelectedItem = longReferenceName;
                ToolTip toolTip = GetPrivateField<ToolTip>(form, "packagePathToolTip");
                Assert.Equal(longReferenceName, toolTip.GetToolTip(origin));
                int maximumWidth = origin.Width + 240;
                int dropDownWidth = AssemblyExportForm.CalculateReferenceGeometryDropDownWidth(
                    origin,
                    maximumWidth);
                Assert.True(dropDownWidth > origin.Width);
                Assert.True(dropDownWidth <= maximumWidth);

                Rectangle initialIdentityBounds = BoundsRelativeTo(identity, stack);
                Rectangle initialReferenceBounds = BoundsRelativeTo(reference, stack);
                for (int iteration = 0; iteration < 6; iteration++)
                {
                    sections.SelectedIndex = 1;
                    sections.SelectedIndex = 2;
                    sections.SelectedIndex = 0;
                    form.PerformLayout();
                }
                Assert.Equal(initialIdentityBounds, BoundsRelativeTo(identity, stack));
                Assert.Equal(initialReferenceBounds, BoundsRelativeTo(reference, stack));
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestModelMetadataLivesOnDedicatedThirdPage()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                Panel linkRoot = GetControl<Panel>(form, "panelLinkProperties");
                Panel modelRoot = GetControl<Panel>(form, "modernModelRoot");
                TextBox maintainer = GetControl<TextBox>(
                    form,
                    "modernMaintainerNameTextBox");
                TextBox author = GetControl<TextBox>(form, "modernModelAuthorTextBox");
                Label packageLabel = GetControl<Label>(form, "labelRosPackageName");
                Label packageHint = GetControl<Label>(form, "labelRosPackageNameHint");
                TextBox packageName = GetControl<TextBox>(
                    form,
                    "textBoxRosPackageName");
                CheckBox usdTarget = GetControl<CheckBox>(
                    form,
                    "modernUsdAssetCheckBox");
                CheckBox mjcfTarget = GetControl<CheckBox>(
                    form,
                    "modernMjcfAssetCheckBox");
                CheckBox ros1Target = GetControl<CheckBox>(
                    form,
                    "modernRos1CheckBox");
                CheckBox ros2Target = GetControl<CheckBox>(
                    form,
                    "modernRos2CheckBox");

                Assert.False(IsDescendantOf(maintainer, linkRoot));
                Assert.False(IsDescendantOf(author, linkRoot));
                Assert.True(IsDescendantOf(maintainer, modelRoot));
                Assert.True(IsDescendantOf(author, modelRoot));
                Assert.True(IsDescendantOf(usdTarget, modelRoot));
                Assert.True(IsDescendantOf(mjcfTarget, modelRoot));
                Assert.Null(FindDescendant(modelRoot, "modernRos2ControlProfileTextBox"));
                Assert.Null(FindDescendant(modelRoot, "modernIsaacVersionTextBox"));
                Assert.Null(FindDescendant(modelRoot, "modernIsaacLabProfileTextBox"));
                Assert.True(IsDescendantOf(
                    GetControl<Button>(form, "modernLinkNextButton"),
                    linkRoot));
                Assert.True(IsDescendantOf(
                    GetControl<Button>(form, "modernModelPreviousButton"),
                    modelRoot));
                Assert.True(IsDescendantOf(
                    GetControl<Button>(form, "buttonLinksFinish"),
                    modelRoot));
                string jointStep = GetControl<Label>(form, "modernJointStep").Text;
                string linkStep = GetControl<Label>(form, "modernLinkStep").Text;
                string modelStep = GetControl<Label>(form, "modernModelStep").Text;
                Assert.True(jointStep.Contains("1/3") || jointStep.Contains("1 of 3"));
                Assert.True(linkStep.Contains("2/3") || linkStep.Contains("2 of 3"));
                Assert.True(modelStep.Contains("3/3") || modelStep.Contains("3 of 3"));

                packageName.Text = "rover_description";
                Assert.Equal(
                    "ROS1/rover_description | ROS2/rover_description",
                    packageHint.Text);
                Assert.DoesNotContain("and", packageHint.Text);
                Assert.DoesNotContain("\u548c", packageHint.Text);
                Assert.True(
                    packageLabel.Text == "ROS package" ||
                    packageLabel.Text == "ROS \u5305\u540d");

                form.Exporter = (ExportHelper)FormatterServices.GetUninitializedObject(
                    typeof(ExportHelper));
                form.Exporter.PackageName = "Robot Model";
                ros1Target.Checked = false;
                ros2Target.Checked = false;
                usdTarget.Checked = true;
                mjcfTarget.Checked = true;
                InvokePrivate(form, "UpdateRosPackageNameHint");
                Assert.Equal(
                    "USD/rover_description | MuJoCo/robot_model",
                    packageHint.Text);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestModernPageSwitcherKeepsOneExplicitActiveState()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                FieldInfo activePage = typeof(AssemblyExportForm).GetField(
                    "modernActivePage",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo showPage = typeof(AssemblyExportForm).GetMethod(
                    "ShowModernAssemblyPage",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(activePage);
                Assert.NotNull(showPage);
                Type pageType = showPage.GetParameters()[0].ParameterType;

                Assert.Equal("Joint", activePage.GetValue(form).ToString());
                foreach (string pageName in new string[] { "Link", "Model", "Joint" })
                {
                    object page = Enum.Parse(pageType, pageName);
                    showPage.Invoke(form, new object[] { page });
                    Assert.Equal(pageName, activePage.GetValue(form).ToString());
                }
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestRepeatedPageSwitchesDoNotInvalidateUnrelatedPageTrees()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                form.ClientSize = new Size(1344, 812);
                form.CreateControl();
                InvokePrivate(form, "PrimeModernPageLayouts");

                Panel modelRoot = GetControl<Panel>(form, "modernModelRoot");
                modelRoot.CreateControl();
                int modelInvalidations = 0;
                modelRoot.Invalidated += delegate { modelInvalidations++; };

                for (int iteration = 0; iteration < 12; iteration++)
                {
                    ShowModernAssemblyPage(
                        form,
                        iteration % 2 == 0 ? "Link" : "Joint");
                }

                Assert.Equal(0, modelInvalidations);

                TabControl jointSections = GetControl<TabControl>(
                    form,
                    "modernJointSections");
                TabPage unrelatedJointPage = jointSections.TabPages[2];
                unrelatedJointPage.CreateControl();
                int unrelatedTabInvalidations = 0;
                unrelatedJointPage.Invalidated += delegate
                {
                    unrelatedTabInvalidations++;
                };

                jointSections.SelectedIndex = 0;
                for (int iteration = 0; iteration < 12; iteration++)
                {
                    jointSections.SelectedIndex = iteration % 2;
                }

                Assert.Equal(0, unrelatedTabInvalidations);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestModernThemeUsesWindowsHostDialogFont()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                string hostFamily = SystemFonts.MessageBoxFont.FontFamily.Name;
                Assert.Equal(hostFamily, form.Font.FontFamily.Name);
                Assert.Equal(
                    hostFamily,
                    GetControl<Button>(form, "buttonJointNext").Font.FontFamily.Name);
                Assert.Equal(
                    hostFamily,
                    GetControl<Label>(form, "modernJointRelationArrow").Font.FontFamily.Name);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestModernThemeStylesPartExportWithoutChangingItsWorkflow()
        {
            PartExportForm form = (PartExportForm)
                Activator.CreateInstance(typeof(PartExportForm), true);

            try
            {
                Button finish = GetPrivateControl<Button>(
                    form,
                    typeof(PartExportForm),
                    "button_finish");
                ComboBox materials = GetPrivateControl<ComboBox>(
                    form,
                    typeof(PartExportForm),
                    "comboBox_materials");

                Assert.Equal(ModernWinFormsTheme.Background, form.BackColor);
                Assert.Equal(ModernWinFormsTheme.Accent, finish.BackColor);
                Assert.Equal(FlatStyle.Flat, finish.FlatStyle);
                Assert.Equal(FlatStyle.Flat, materials.FlatStyle);
                Assert.True(materials.Items.Contains("aluminum"));
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestModernThemeReleasesOwnedFontsWithDisposedControls()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);
            Button next = GetControl<Button>(form, "buttonJointNext");
            Label linkSubtitle = GetControl<Label>(form, "label2");

            try
            {
                Assert.True(UiFontResources.OwnsFont(form));
                Assert.True(UiFontResources.OwnsFont(next));
                Assert.True(UiFontResources.OwnsFont(linkSubtitle));
                form.Dispose();
                Assert.False(UiFontResources.OwnsFont(form));
                Assert.False(UiFontResources.OwnsFont(next));
                Assert.False(UiFontResources.OwnsFont(linkSubtitle));
            }
            finally
            {
                if (!form.IsDisposed)
                {
                    form.Dispose();
                }
            }
        }

        [Fact]
        public void TestEquivalentOwnedFontRemainsUsableAfterRepeatedStyling()
        {
            using (Label label = new Label())
            {
                UiFontResources.SetFont(
                    label,
                    "Segoe UI",
                    9F,
                    FontStyle.Regular);
                Font first = label.Font;

                UiFontResources.SetFont(
                    label,
                    "Segoe UI",
                    9F,
                    FontStyle.Regular);

                Assert.Same(first, label.Font);
                Assert.True(label.Font.Height > 0);
                Assert.True(UiFontResources.OwnsFont(label));
            }
        }

        [Fact]
        public void TestUsageGuideButtonAndGeneratedMaterialIdAreAvailable()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                form.ClientSize = new Size(1344, 812);
                form.PerformLayout();

                Button guideButton = GetControl<Button>(form, "buttonUsageGuide");
                TextBox materialId = GetControl<TextBox>(form, "modernMaterialIdTextBox");

                Panel jointRoot = GetControl<Panel>(form, "modernJointRoot");
                Assert.True(IsDescendantOf(guideButton, jointRoot));
                Assert.True(guideButton.Enabled);
                Assert.True(guideButton.Width >= TextRenderer.MeasureText(
                    guideButton.Text,
                    guideButton.Font).Width + guideButton.Padding.Horizontal);
                Assert.True(guideButton.Text == "Guide" || guideButton.Text == "使用说明");
                Assert.True(materialId.ReadOnly);
                Assert.StartsWith("material_", materialId.Text);
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
            Assert.Contains("URDF Export Configuration (v2)", guide);
            Assert.Contains("component-instance PID plus a reference-feature PID", guide);
            Assert.Contains("at any assembly depth", guide);
            Assert.Contains("Name-based v1.x configurations are not migrated automatically", guide);
            Assert.Contains("Link tree outline editing", guide);
            Assert.Contains("STEP, imported, or fixed assemblies", guide);
            Assert.Contains("SolidWorks Mate detection", guide);
            Assert.Contains("zero remaining DOFs is no longer treated as fixed", guide);
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
            Assert.DoesNotContain("Maintainer for this version", guide);
        }

        [Fact]
        public void TestOpenUsdSettingsAreVersionIndependentAndPreserveExplicitIntent()
        {
            using (OpenUsdSettingsDialog dialog = new OpenUsdSettingsDialog())
            {
                UsdSimulationProfile profile = new UsdSimulationProfile
                {
                    BaseMode = "fixed",
                    RobotType = "wheeled",
                    AllowSelfCollision = true
                };
                profile.JointDrives.Add(new UsdJointDriveProfile
                {
                    Joint = "wheel_joint",
                    Mode = "position",
                    Stiffness = 80.0,
                    Damping = 4.0
                });
                dialog.LoadSettings(
                    profile,
                    new[]
                    {
                        new OpenUsdJointDescriptor
                        {
                            Name = "wheel_joint",
                            Type = "continuous",
                            EffortLimit = 3.0,
                            VelocityLimit = 2.0
                        }
                    });

                Assert.Null(FindDescendant(dialog, "openUsdIsaacSimVersionTextBox"));
                Assert.Null(FindDescendant(dialog, "openUsdIsaacLabVersionTextBox"));
                DataGridView grid = Assert.IsType<DataGridView>(
                    FindDescendant(dialog, "openUsdJointDriveGrid"));
                Assert.Single(grid.Rows.Cast<DataGridViewRow>());
                Assert.True(grid.Rows[0].Cells["effortLimitColumn"].ReadOnly);
                Assert.True(grid.Rows[0].Cells["velocityLimitColumn"].ReadOnly);

                UsdSimulationProfile captured;
                Assert.True(dialog.TryCaptureSettings(out captured));
                Assert.Equal("fixed", captured.BaseMode);
                Assert.Equal("wheeled", captured.RobotType);
                Assert.True(captured.AllowSelfCollision);
                UsdJointDriveProfile drive = Assert.Single(captured.JointDrives);
                Assert.Equal("wheel_joint", drive.Joint);
                Assert.Equal("position", drive.Mode);
                Assert.Equal(80.0, drive.Stiffness);
                Assert.Equal(4.0, drive.Damping);

                AssertInputBordersStayInside(
                    FindDescendant(dialog, "openUsdGeneralSettings"));
                AssertInputBordersStayInside(
                    FindDescendant(dialog, "openUsdJointDriveSettings"));
            }
        }

        [Fact]
        public void TestOpenUsdSettingsFitAndPreserveInactiveGainDrafts()
        {
            using (OpenUsdSettingsDialog dialog = new OpenUsdSettingsDialog())
            {
                UsdSimulationProfile profile = new UsdSimulationProfile();
                profile.JointDrives.Add(new UsdJointDriveProfile
                {
                    Joint = "wheel_joint",
                    Mode = "position",
                    Stiffness = 80.0,
                    Damping = 4.0
                });
                dialog.LoadSettings(
                    profile,
                    new[]
                    {
                        new OpenUsdJointDescriptor
                        {
                            Name = "wheel_joint",
                            Type = "continuous",
                            EffortLimit = 3.0,
                            VelocityLimit = 2.0
                        }
                    });
                dialog.ClientSize = new Size(900, 560);
                dialog.PerformLayout();

                DataGridView grid = Assert.IsType<DataGridView>(
                    FindDescendant(dialog, "openUsdJointDriveGrid"));
                Assert.All(
                    grid.Columns.Cast<DataGridViewColumn>(),
                    column => Assert.Equal(
                        DataGridViewAutoSizeColumnMode.Fill,
                        column.AutoSizeMode));

                DataGridViewRow row = Assert.Single(grid.Rows.Cast<DataGridViewRow>());
                DataGridViewComboBoxCell modeCell =
                    Assert.IsType<DataGridViewComboBoxCell>(
                        row.Cells["driveModeColumn"]);
                object effort = modeCell.Items.Cast<object>().Single(
                    item => item.ToString().StartsWith("effort", StringComparison.Ordinal));
                modeCell.Value = effort;

                Assert.Equal("80", Convert.ToString(
                    row.Cells["stiffnessColumn"].Value,
                    CultureInfo.InvariantCulture));
                Assert.Equal("4", Convert.ToString(
                    row.Cells["dampingColumn"].Value,
                    CultureInfo.InvariantCulture));
                Assert.True(row.Cells["stiffnessColumn"].ReadOnly);
                Assert.True(row.Cells["dampingColumn"].ReadOnly);

                UsdSimulationProfile captured;
                Assert.True(dialog.TryCaptureSettings(out captured));
                UsdJointDriveProfile drive = Assert.Single(captured.JointDrives);
                Assert.Equal("effort", drive.Mode);
                Assert.Null(drive.Stiffness);
                Assert.Null(drive.Damping);
            }
        }

        [Theory]
        [InlineData("en-US", 1.5F, 1600, 900)]
        [InlineData("zh-CN", 1.5F, 1600, 900)]
        [InlineData("en-US", 2F, 1920, 1040)]
        [InlineData("zh-CN", 2F, 1920, 1040)]
        public void TestOpenUsdSettingsRemainReachableAtHighDpi(
            string cultureName,
            float scaleFactor,
            int workingWidth,
            int workingHeight)
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                using (OpenUsdSettingsDialog dialog = new OpenUsdSettingsDialog())
                {
                    dialog.Scale(new SizeF(scaleFactor, scaleFactor));
                    Rectangle workingArea = new Rectangle(
                        120,
                        80,
                        workingWidth,
                        workingHeight);
                    dialog.ConstrainToWorkingArea(workingArea);
                    dialog.PerformLayout();

                    Control root = FindDescendant(dialog, "openUsdRoot");
                    Control footer = FindDescendant(dialog, "openUsdFooter");
                    Button apply = Assert.IsType<Button>(
                        FindDescendant(dialog, "openUsdConfirmButton"));
                    Button cancel = Assert.IsType<Button>(
                        FindDescendant(dialog, "openUsdCancelButton"));

                    Assert.NotNull(root);
                    Assert.NotNull(footer);
                    Assert.True(((ScrollableControl)root).AutoScroll);
                    Assert.True(dialog.Width <= workingArea.Width);
                    Assert.True(dialog.Height <= workingArea.Height);
                    Assert.True(dialog.Left >= workingArea.Left);
                    Assert.True(dialog.Top >= workingArea.Top);
                    Assert.True(dialog.Right <= workingArea.Right);
                    Assert.True(dialog.Bottom <= workingArea.Bottom);
                    AssertContainedIn(footer, root);
                    AssertContainedIn(apply, footer);
                    AssertContainedIn(cancel, footer);
                    Assert.False(String.IsNullOrWhiteSpace(dialog.Text));
                    Assert.False(String.IsNullOrWhiteSpace(apply.Text));
                    Assert.False(String.IsNullOrWhiteSpace(cancel.Text));
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("zh-CN")]
        public void TestOpenUsdGainValidationReportsAllErrorsAndEnforcesModes(
            string cultureName)
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                using (OpenUsdSettingsDialog dialog = new OpenUsdSettingsDialog())
                {
                    string[] jointNames = new[] { "effort_joint", "passive_joint", "velocity_joint" };
                    UsdSimulationProfile profile = new UsdSimulationProfile();
                    foreach (string jointName in jointNames)
                    {
                        profile.JointDrives.Add(new UsdJointDriveProfile
                        {
                            Joint = jointName,
                            Mode = "position",
                            Stiffness = 10.0,
                            Damping = 1.0
                        });
                    }
                    dialog.LoadSettings(
                        profile,
                        jointNames.Select(name => new OpenUsdJointDescriptor
                        {
                            Name = name,
                            Type = "continuous",
                            EffortLimit = 3.0,
                            VelocityLimit = 2.0
                        }));

                    DataGridView grid = Assert.IsType<DataGridView>(
                        FindDescendant(dialog, "openUsdJointDriveGrid"));
                    Assert.Equal(3, grid.Rows.Count);
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        Assert.False(row.Cells["stiffnessColumn"].ReadOnly);
                        Assert.False(row.Cells["dampingColumn"].ReadOnly);
                        row.Cells["stiffnessColumn"].Value = "-1";
                        row.Cells["dampingColumn"].Value = "not-a-number";
                    }

                    UsdSimulationProfile ignored;
                    Assert.False(dialog.TryCaptureSettings(out ignored));
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        Assert.False(String.IsNullOrWhiteSpace(
                            row.Cells["stiffnessColumn"].ErrorText));
                        Assert.False(String.IsNullOrWhiteSpace(
                            row.Cells["dampingColumn"].ErrorText));
                    }

                    DataGridViewComboBoxCell effortMode =
                        Assert.IsType<DataGridViewComboBoxCell>(
                            grid.Rows[0].Cells["driveModeColumn"]);
                    effortMode.Value = effortMode.Items.Cast<object>().Single(
                        item => item.ToString().StartsWith(
                            "effort",
                            StringComparison.Ordinal));
                    Assert.True(grid.Rows[0].Cells["stiffnessColumn"].ReadOnly);
                    Assert.True(grid.Rows[0].Cells["dampingColumn"].ReadOnly);
                    Assert.Equal(String.Empty, grid.Rows[0].Cells["stiffnessColumn"].ErrorText);
                    Assert.Equal(String.Empty, grid.Rows[0].Cells["dampingColumn"].ErrorText);

                    DataGridViewComboBoxCell passiveMode =
                        Assert.IsType<DataGridViewComboBoxCell>(
                            grid.Rows[1].Cells["driveModeColumn"]);
                    passiveMode.Value = passiveMode.Items.Cast<object>().Single(
                        item => item.ToString().StartsWith(
                            "passive",
                            StringComparison.Ordinal));
                    Assert.True(grid.Rows[1].Cells["stiffnessColumn"].ReadOnly);
                    Assert.True(grid.Rows[1].Cells["dampingColumn"].ReadOnly);
                    Assert.Equal(String.Empty, grid.Rows[1].Cells["stiffnessColumn"].ErrorText);
                    Assert.Equal(String.Empty, grid.Rows[1].Cells["dampingColumn"].ErrorText);

                    DataGridViewComboBoxCell velocityMode =
                        Assert.IsType<DataGridViewComboBoxCell>(
                            grid.Rows[2].Cells["driveModeColumn"]);
                    velocityMode.Value = velocityMode.Items.Cast<object>().Single(
                        item => item.ToString().StartsWith(
                            "velocity",
                            StringComparison.Ordinal));
                    Assert.True(grid.Rows[2].Cells["stiffnessColumn"].ReadOnly);
                    Assert.False(grid.Rows[2].Cells["dampingColumn"].ReadOnly);
                    Assert.Equal(
                        "0",
                        Convert.ToString(
                            grid.Rows[2].Cells["stiffnessColumn"].Value,
                            CultureInfo.InvariantCulture));
                    Assert.Equal(String.Empty, grid.Rows[2].Cells["stiffnessColumn"].ErrorText);
                    grid.Rows[2].Cells["dampingColumn"].Value = "2.5";

                    UsdSimulationProfile captured;
                    Assert.True(dialog.TryCaptureSettings(out captured));
                    Assert.Equal(2, captured.JointDrives.Count);
                    UsdJointDriveProfile effortDrive = captured.JointDrives.Single(
                        drive => drive.Mode == "effort");
                    UsdJointDriveProfile velocityDrive = captured.JointDrives.Single(
                        drive => drive.Mode == "velocity");
                    Assert.Null(effortDrive.Stiffness);
                    Assert.Null(effortDrive.Damping);
                    Assert.Equal(0.0, velocityDrive.Stiffness);
                    Assert.Equal(2.5, velocityDrive.Damping);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [Fact]
        public void TestModernCardsTabsAndUsageGuideShareResponsiveTheme()
        {
            AssemblyExportForm exportForm = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);
            using (UsageGuideForm guide = new UsageGuideForm())
            {
                try
                {
                    Assert.IsType<ModernTabControl>(
                        GetControl<TabControl>(exportForm, "modernJointSections"));
                    Assert.IsType<ModernCardPanel>(
                        GetControl<TableLayoutPanel>(exportForm, "modernPackageCard"));
                    Assert.Equal(6, ModernCardPanel.CornerRadius);

                    guide.ClientSize = new Size(760, 560);
                    guide.PerformLayout();
                    RichTextBox guideText = Assert.IsType<RichTextBox>(
                        FindDescendant(guide, "usageGuideTextBox"));
                    TableLayoutPanel guideCard = Assert.IsType<ModernCardPanel>(
                        FindDescendant(guide, "usageGuideCard"));
                    TableLayoutPanel footer = Assert.IsType<TableLayoutPanel>(
                        FindDescendant(guide, "usageGuideFooter"));
                    Button close = Assert.IsType<Button>(
                        FindDescendant(guide, "usageGuideCloseButton"));

                    Assert.True(guideText.ReadOnly);
                    Assert.False(guideText.TabStop);
                    Assert.Equal(DockStyle.Fill, guideCard.Dock);
                    Assert.False(guideCard.Bounds.IntersectsWith(footer.Bounds));
                    AssertContainedIn(close, footer);
                    Assert.Equal(1, footer.RowCount);
                    Assert.DoesNotContain(
                        Descendants(footer).OfType<Label>(),
                        label => label.Text.IndexOf(
                            "maintainer",
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                            label.Text.Contains("维护作者"));

                    guideText.SelectAll();
                    MethodInfo onShown = typeof(UsageGuideForm).GetMethod(
                        "OnShown",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(onShown);
                    onShown.Invoke(guide, new object[] { EventArgs.Empty });
                    Assert.Equal(0, guideText.SelectionLength);
                    Assert.Same(close, guide.ActiveControl);

                    int firstLineLength = guideText.Text.IndexOf('\n');
                    guideText.Select(0, firstLineLength);
                    Assert.True(guideText.SelectionFont.Bold);
                    Assert.Equal(ModernWinFormsTheme.Accent, guideText.SelectionColor);
                }
                finally
                {
                    exportForm.Dispose();
                }
            }
        }

        [Fact]
        public void TestFirstModelPageSwitchReusesPrimedLayout()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                form.ClientSize = new Size(1344, 812);
                InvokePrivate(form, "PrimeModernPageLayouts");
                Control modelRoot = GetControl<Control>(form, "modernModelRoot");
                Control modelBody = GetControl<Control>(form, "modernModelBody");
                Control modelContent = GetControl<Control>(form, "modernModelContentPanel");
                int primedLayoutCount = (int)GetPrivateField<object>(
                    form,
                    "modernModelExplicitLayoutCount");

                ShowModernAssemblyPage(form, "Model");

                Assert.True((bool)GetPrivateField<object>(form, "modernPageShown"));
                Assert.Equal(
                    "Model",
                    GetPrivateField<object>(form, "modernActivePage").ToString());
                Assert.True(primedLayoutCount > 0);
                Assert.Equal(
                    primedLayoutCount,
                    (int)GetPrivateField<object>(
                        form,
                        "modernModelExplicitLayoutCount"));
                Assert.Equal(form.DisplayRectangle, modelRoot.Bounds);
                Assert.True(modelBody.ClientSize.Width > 0);
                Assert.True(modelBody.ClientSize.Height > 0);
                Assert.True(modelContent.ClientSize.Width > 0);
                Assert.True(modelContent.ClientSize.Height > 0);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestReturningToModelPageReusesTheCachedLayout()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                form.ClientSize = new Size(1344, 812);
                ShowModernAssemblyPage(form, "Model");
                int firstLayoutCount = (int)GetPrivateField<object>(
                    form,
                    "modernModelExplicitLayoutCount");
                ShowModernAssemblyPage(form, "Link");
                ShowModernAssemblyPage(form, "Model");
                int returnedLayoutCount = (int)GetPrivateField<object>(
                    form,
                    "modernModelExplicitLayoutCount");

                Assert.True(firstLayoutCount > 0);
                Assert.Equal(firstLayoutCount, returnedLayoutCount);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestModelPageResizeInvalidatesCachedLayoutOnce()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                form.ClientSize = new Size(1200, 720);
                InvokePrivate(form, "PrimeModernPageLayouts");
                ShowModernAssemblyPage(form, "Model");
                int primedLayoutCount = (int)GetPrivateField<object>(
                    form,
                    "modernModelExplicitLayoutCount");

                form.ClientSize = new Size(1344, 812);
                ShowModernAssemblyPage(form, "Link");
                ShowModernAssemblyPage(form, "Model");
                int resizedLayoutCount = (int)GetPrivateField<object>(
                    form,
                    "modernModelExplicitLayoutCount");
                ShowModernAssemblyPage(form, "Link");
                ShowModernAssemblyPage(form, "Model");

                Assert.Equal(primedLayoutCount + 1, resizedLayoutCount);
                Assert.Equal(
                    resizedLayoutCount,
                    (int)GetPrivateField<object>(
                        form,
                        "modernModelExplicitLayoutCount"));
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestDynamicLinkStatusInvalidatesOnlyItsOwningTabPage()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);
            try
            {
                form.ClientSize = new Size(1344, 812);
                InvokePrivate(form, "PrimeModernPageLayouts");
                ModernTabControl sections = Assert.IsType<ModernTabControl>(
                    GetControl<TabControl>(form, "modernLinkSections"));
                TableLayoutPanel inertiaSection = Assert.IsType<TableLayoutPanel>(
                    FindDescendant(sections, "modernInertiaMatrixSection"));
                TableLayoutPanel collisionSection = Assert.IsType<TableLayoutPanel>(
                    FindDescendant(sections, "modernCollisionStrategySection"));
                TableLayoutPanel appearanceSection = Assert.IsType<TableLayoutPanel>(
                    FindDescendant(sections, "modernAppearanceColorSection"));
                Label collisionStatus = GetControl<Label>(
                    form,
                    "labelCollisionPreviewStatus");

                Assert.False(inertiaSection.AutoSize);
                Assert.False(collisionSection.AutoSize);
                Assert.False(appearanceSection.AutoSize);

                sections.InvalidatePageLayout(collisionStatus);

                Assert.False(inertiaSection.AutoSize);
                Assert.True(collisionSection.AutoSize);
                Assert.False(appearanceSection.AutoSize);

                collisionStatus.Text =
                    "Collision preview status with a deliberately long localized detail " +
                    "that must grow only the collision page layout.";
                sections.RebuildPageLayout(collisionStatus);

                Assert.False(inertiaSection.AutoSize);
                Assert.False(collisionSection.AutoSize);
                Assert.False(appearanceSection.AutoSize);
                Size measured = TextRenderer.MeasureText(
                    collisionStatus.Text,
                    collisionStatus.Font,
                    new Size(collisionStatus.Width, Int32.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                Assert.True(measured.Height <= collisionStatus.Height);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestModelPathChangeRebuildsThenReusesCachedLayout()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);
            try
            {
                form.ClientSize = new Size(1344, 812);
                InvokePrivate(form, "PrimeModernPageLayouts");
                int initialLayoutCount = (int)GetPrivateField<object>(
                    form,
                    "modernModelExplicitLayoutCount");
                TextBox packageName = GetControl<TextBox>(
                    form,
                    "textBoxRosPackageName");
                Label pathHint = GetControl<Label>(
                    form,
                    "labelRosPackageNameHint");
                TableLayoutPanel metadataGrid = Assert.IsType<TableLayoutPanel>(
                    FindDescendant(form, "modernPackageMetadataGrid"));
                Control packageCard = GetControl<Control>(form, "modernPackageCard");

                packageName.Text = new String('a', 96);
                int changedLayoutCount = (int)GetPrivateField<object>(
                    form,
                    "modernModelExplicitLayoutCount");
                InvokePrivate(form, "UpdateRosPackageNameHint");
                int repeatedUpdateCount = (int)GetPrivateField<object>(
                    form,
                    "modernModelExplicitLayoutCount");
                ShowModernAssemblyPage(form, "Model");
                ShowModernAssemblyPage(form, "Link");
                ShowModernAssemblyPage(form, "Model");

                Assert.Equal(initialLayoutCount + 1, changedLayoutCount);
                Assert.Equal(changedLayoutCount, repeatedUpdateCount);
                Assert.Equal(
                    repeatedUpdateCount,
                    (int)GetPrivateField<object>(
                        form,
                        "modernModelExplicitLayoutCount"));
                Assert.False(pathHint.AutoSize);
                Assert.True(pathHint.AutoEllipsis);
                AssertContainedIn(pathHint, metadataGrid);
                AssertContainedIn(packageName, metadataGrid);
                AssertContainedIn(
                    GetControl<TextBox>(form, "modernModelAuthorTextBox"),
                    metadataGrid);
                AssertContainedIn(metadataGrid, packageCard);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestJointTabsReuseControlsAndFooterButtonsShareGeometry()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);
            try
            {
                form.ClientSize = new Size(1344, 812);
                form.PerformLayout();
                TabControl sections = GetControl<TabControl>(form, "modernJointSections");
                TextBox effort = GetControl<TextBox>(form, "textBoxLimitEffort");
                Font effortFont = effort.Font;
                int controlCount = CountDescendants(sections);

                for (int iteration = 0; iteration < 20; iteration++)
                {
                    sections.SelectedIndex = iteration % sections.TabPages.Count;
                    sections.PerformLayout();
                }

                Assert.Same(effortFont, effort.Font);
                Assert.Equal(controlCount, CountDescendants(sections));

                Button[] footerButtons = new Button[]
                {
                    GetControl<Button>(form, "buttonJointCancel"),
                    GetControl<Button>(form, "buttonJointNext"),
                    GetControl<Button>(form, "buttonLinksCancel"),
                    GetControl<Button>(form, "buttonLinksPrevious"),
                    GetControl<Button>(form, "modernLinkNextButton"),
                    GetControl<Button>(form, "modernModelCancelButton"),
                    GetControl<Button>(form, "modernModelPreviousButton"),
                    GetControl<Button>(form, "modernUsdSettingsButton"),
                    GetControl<Button>(form, "buttonLinksExportUrdfOnly"),
                    GetControl<Button>(form, "buttonLinksFinish")
                };
                int footerButtonHeight = footerButtons[0].Height;
                Assert.True(footerButtonHeight >= 36);
                Assert.All(footerButtons, button =>
                {
                    Assert.Equal(footerButtonHeight, button.Height);
                    Assert.Equal(0, button.Margin.Top);
                    Assert.Equal(0, button.Margin.Bottom);
                    Assert.NotNull(button.Region);
                });

                Control safetyCard = GetControl<Control>(form, "modernSafetyCard");
                TextBox lastInput = GetControl<TextBox>(form, "textBoxKVelocity");
                Rectangle lastBounds = BoundsRelativeTo(lastInput, safetyCard);
                Assert.True(
                    lastBounds.Bottom <= safetyCard.ClientSize.Height - safetyCard.Padding.Bottom,
                    String.Format(
                        CultureInfo.InvariantCulture,
                        "Last input bottom {0} exceeds card content bottom {1}.",
                        lastBounds.Bottom,
                        safetyCard.ClientSize.Height - safetyCard.Padding.Bottom));
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestFunctionalInputSectionsKeepBottomBordersInsideTheirContainers()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);
            try
            {
                form.ClientSize = new Size(1344, 812);
                form.PerformLayout();

                ShowModernAssemblyPage(form, "Joint");
                TabControl jointSections = GetControl<TabControl>(
                    form,
                    "modernJointSections");
                for (int index = 0; index < jointSections.TabPages.Count; index++)
                {
                    jointSections.SelectedIndex = index;
                    jointSections.PerformLayout();
                }
                GetControl<CheckBox>(form, "MimicCheckBox").Checked = true;
                form.PerformLayout();

                ShowModernAssemblyPage(form, "Link");
                TabControl linkSections = GetControl<TabControl>(
                    form,
                    "modernLinkSections");
                for (int index = 0; index < linkSections.TabPages.Count; index++)
                {
                    linkSections.SelectedIndex = index;
                    linkSections.PerformLayout();
                }

                ShowModernAssemblyPage(form, "Model");
                form.PerformLayout();

                string[] inputContainers = new string[]
                {
                    "modernJointIdentityCard",
                    "modernReferenceGeometryCard",
                    "modernOriginCard",
                    "modernAxisCard",
                    "modernLimitsCard",
                    "modernCalibrationCard",
                    "modernDynamicsCard",
                    "modernSafetyCard",
                    "modernMimicCard",
                    "modernInertialOriginSection",
                    "modernInertiaMatrixSection",
                    "groupBox5",
                    "modernVisualOriginSection",
                    "modernCollisionStrategySection",
                    "groupBox4",
                    "modernAppearanceMaterialSection",
                    "modernAppearanceColorSection",
                    "modernPackageCard"
                };

                foreach (string containerName in inputContainers)
                {
                    AssertInputBordersStayInside(
                        GetControl<Control>(form, containerName));
                }

                string[] lastInputs = new string[]
                {
                    "textBoxJointYaw",
                    "textBoxAxisZ",
                    "textBoxLimitVelocity",
                    "textBoxCalibrationFalling",
                    "textBoxDamping",
                    "textBoxKVelocity",
                    "textBoxMimicOffset",
                    "textBoxInertialOriginYaw",
                    "textBoxIzz",
                    "modernModelAuthorTextBox"
                };
                foreach (string inputName in lastInputs)
                {
                    Control input = GetControl<Control>(form, inputName);
                    Assert.IsType<TableLayoutPanel>(input.Parent);
                    AssertControlBottomBorderStaysInside(input, input.Parent);
                }
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestInertiaSymmetryLabelsIdentifyBothTensorTerms()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);
            try
            {
                Assert.Equal(
                    "iyx = ixy",
                    GetControl<Label>(form, "labelInertiaIyx").Text);
                Assert.Equal(
                    "izx = ixz",
                    GetControl<Label>(form, "labelInertiaIzx").Text);
                Assert.Equal(
                    "izy = iyz",
                    GetControl<Label>(form, "labelInertiaIzy").Text);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestMovingJointDefaultsApplyOnceAndClearedValuesRemainInvalid()
        {
            UrdfJoint joint = new UrdfJoint
            {
                Type = "continuous"
            };
            MethodInfo applyDefaults = typeof(AssemblyExportForm).GetMethod(
                "ApplyMissingRequiredJointLimitDefaults",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(applyDefaults);
            applyDefaults.Invoke(null, new object[] { joint });
            Assert.Equal(1.0, joint.Limit.Effort);
            Assert.Equal(1.0, joint.Limit.Velocity);

            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);
            try
            {
                ComboBox type = GetControl<ComboBox>(form, "comboBoxJointType");
                TextBox effort = GetControl<TextBox>(form, "textBoxLimitEffort");
                TextBox velocity = GetControl<TextBox>(form, "textBoxLimitVelocity");
                type.Text = ChineseUiText.JointTypeDisplay(
                    "continuous",
                    ChineseUiText.ShouldUseChinese());
                effort.Text = "1";
                velocity.Text = "1";

                effort.Text = String.Empty;
                velocity.Text = String.Empty;
                MethodInfo leave = typeof(AssemblyExportForm).GetMethod(
                    "JointRequiredLimitInputLeave",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(leave);
                leave.Invoke(form, new object[] { effort, EventArgs.Empty });
                leave.Invoke(form, new object[] { velocity, EventArgs.Empty });

                Assert.Equal(String.Empty, effort.Text);
                Assert.Equal(String.Empty, velocity.Text);
                Assert.False((bool)InvokePrivate(form, "ValidateJointLimitInputs"));
                ErrorProvider errors = GetPrivateField<ErrorProvider>(
                    form,
                    "jointLimitErrorProvider");
                Assert.False(String.IsNullOrWhiteSpace(errors.GetError(effort)));
                Assert.False(String.IsNullOrWhiteSpace(errors.GetError(velocity)));
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestUnvisitedMovingJointDefaultsMissingValuesWithoutMaskingInvalidValues()
        {
            UrdfJoint joint = new UrdfJoint
            {
                Type = "continuous"
            };
            MethodInfo applyDefaults = typeof(AssemblyExportForm).GetMethod(
                "ApplyMissingRequiredJointLimitDefaults",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(applyDefaults);

            applyDefaults.Invoke(null, new object[] { joint });
            Assert.Equal(1.0, joint.Limit.Effort);
            Assert.Equal(1.0, joint.Limit.Velocity);
            Assert.False(joint.Limit.HasPositionBounds());

            joint.Limit.Effort = Double.NaN;
            joint.Limit.Velocity = 2.5;
            applyDefaults.Invoke(null, new object[] { joint });
            Assert.True(Double.IsNaN(joint.Limit.Effort));
            Assert.Equal(2.5, joint.Limit.Velocity);
        }

        [Fact]
        public void TestJointLimitValidationIsImmediateAndActionable()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);
            try
            {
                GetControl<ComboBox>(form, "comboBoxJointType").Text =
                    ChineseUiText.JointTypeDisplay(
                        "revolute",
                        ChineseUiText.ShouldUseChinese());
                TextBox lower = GetControl<TextBox>(form, "textBoxLimitLower");
                TextBox upper = GetControl<TextBox>(form, "textBoxLimitUpper");
                TextBox effort = GetControl<TextBox>(form, "textBoxLimitEffort");
                TextBox velocity = GetControl<TextBox>(form, "textBoxLimitVelocity");
                TextBox softLower = GetControl<TextBox>(form, "textBoxSoftLower");
                TextBox softUpper = GetControl<TextBox>(form, "textBoxSoftUpper");
                TextBox kVelocity = GetControl<TextBox>(form, "textBoxKVelocity");
                ErrorProvider errors = GetPrivateField<ErrorProvider>(
                    form,
                    "jointLimitErrorProvider");

                lower.Text = "2";
                upper.Text = "1";
                effort.Text = "0";
                velocity.Text = "NaN";
                softLower.Text = "3";
                softUpper.Text = "-1";
                kVelocity.Text = "Infinity";

                Assert.False(String.IsNullOrWhiteSpace(errors.GetError(lower)));
                Assert.False(String.IsNullOrWhiteSpace(errors.GetError(upper)));
                Assert.False(String.IsNullOrWhiteSpace(errors.GetError(effort)));
                Assert.False(String.IsNullOrWhiteSpace(errors.GetError(velocity)));
                Assert.False(String.IsNullOrWhiteSpace(errors.GetError(softLower)));
                Assert.False(String.IsNullOrWhiteSpace(errors.GetError(softUpper)));
                Assert.False(String.IsNullOrWhiteSpace(errors.GetError(kVelocity)));

                lower.Text = "-2";
                upper.Text = "2";
                effort.Text = "3";
                velocity.Text = "4";
                softLower.Text = "-1";
                softUpper.Text = "1";
                kVelocity.Text = "8";

                Assert.True((bool)InvokePrivate(form, "ValidateJointLimitInputs"));
                Assert.All(
                    new Control[]
                    {
                        lower, upper, effort, velocity, softLower, softUpper, kVelocity
                    },
                    input => Assert.Equal(String.Empty, errors.GetError(input)));
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestExportDiagnosticsRemainSearchableAndActionable()
        {
            ExportTargetValidationFinding finding =
                new ExportTargetValidationFinding(
                    "MODEL_LICENSE",
                    "ModelLicense",
                    "Model license is required for the selected output profiles.");
            string validationReport = ExportDiagnosticsDialog.FormatValidationFindings(
                new[] { finding },
                true,
                @"C:\logs\sw2urdf.log");

            Assert.Contains("[MODEL_LICENSE] ModelLicense", validationReport);
            Assert.Contains(finding.Message, validationReport);
            Assert.Contains("NOASSERTION", validationReport);
            Assert.Contains(@"C:\logs\sw2urdf.log", validationReport);

            ExportTargetValidationFinding targetRequired =
                new ExportTargetValidationFinding(
                    "TARGET_REQUIRED",
                    "Targets",
                    "Select at least one output target.");
            string englishTargetReport = ExportDiagnosticsDialog.FormatValidationFindings(
                new[] { targetRequired },
                false,
                null);
            string chineseTargetReport = ExportDiagnosticsDialog.FormatValidationFindings(
                new[] { targetRequired },
                true,
                null);
            Assert.Contains("SELECT AT LEAST ONE", englishTargetReport.ToUpperInvariant());
            Assert.Contains("至少勾选", chineseTargetReport);

            ExportTargetValidationFinding internalRos2Profile =
                new ExportTargetValidationFinding(
                    "ROS2_CONTROL_PROFILE",
                    "Ros2ControlProfileFile",
                    "Invalid internal profile.");
            Assert.Contains(
                "existing ros2_control JSON file",
                ExportDiagnosticsDialog.FormatValidationFindings(
                    new[] { internalRos2Profile },
                    false,
                    null));

            string failureReport = ExportDiagnosticsDialog.FormatFailure(
                "URDF export failed: ERROR JOINT_LIMIT $.joints[0].limit: " +
                "Moving one-axis Joint requires effort and velocity limits.",
                true,
                @"C:\logs\sw2urdf.log");
            Assert.Contains("JOINT_LIMIT", failureReport);
            Assert.Contains("$.joints[0].limit", failureReport);
            Assert.Contains("effort", failureReport);
            Assert.Contains("velocity", failureReport);
        }

        private static object InvokePrivate(
            AssemblyExportForm form,
            string methodName)
        {
            MethodInfo method = typeof(AssemblyExportForm).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method.Invoke(form, null);
        }

        private static void ShowModernAssemblyPage(
            AssemblyExportForm form,
            string pageName)
        {
            MethodInfo method = typeof(AssemblyExportForm).GetMethod(
                "ShowModernAssemblyPage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            Type pageType = method.GetParameters()[0].ParameterType;
            method.Invoke(form, new object[] { Enum.Parse(pageType, pageName) });
        }

        private static T GetPrivateField<T>(
            AssemblyExportForm form,
            string fieldName)
            where T : class
        {
            FieldInfo field = typeof(AssemblyExportForm).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            T value = field.GetValue(form) as T;
            Assert.NotNull(value);
            return value;
        }

        private static bool ReadBooleanProperty(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(property);
            return (bool)property.GetValue(target, null);
        }

        private static void AssertOptionalBooleanProperty(
            object target,
            string propertyName,
            bool expected)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                Assert.Equal(expected, (bool)property.GetValue(target, null));
            }
        }

        private static int CountDescendants(Control root)
        {
            int count = 1;
            foreach (Control child in root.Controls)
            {
                count += CountDescendants(child);
            }
            return count;
        }

        private static void AssertInputBordersStayInside(Control container)
        {
            Control[] inputs = Descendants(container)
                .Where(control =>
                    control is TextBoxBase ||
                    control is UpDownBase ||
                    control is ComboBox ||
                    control is DataGridView)
                .ToArray();
            Assert.NotEmpty(inputs);

            foreach (Control input in inputs)
            {
                AssertControlBottomBorderStaysInside(input, container);
            }
        }

        private static void AssertControlBottomBorderStaysInside(
            Control input,
            Control container)
        {
            int contentBottom = container.ClientSize.Height -
                Math.Max(1, container.Padding.Bottom);
            Rectangle bounds = BoundsRelativeTo(input, container);
            Assert.True(
                bounds.Bottom < contentBottom,
                String.Format(
                    CultureInfo.InvariantCulture,
                    "{0} bottom border is clipped by {1}: " +
                    "inputBottom={2}, contentBottom={3}, container={4}.",
                    input.Name,
                    container.Name,
                    bounds.Bottom,
                    contentBottom,
                    container.ClientRectangle));
        }

        private static System.Collections.Generic.IEnumerable<Control> Descendants(
            Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }

        private static T GetControl<T>(AssemblyExportForm form, string fieldName)
            where T : Control
        {
            FieldInfo field = typeof(AssemblyExportForm).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                T fieldControl = field.GetValue(form) as T;
                Assert.NotNull(fieldControl);
                return fieldControl;
            }

            T descendant = FindDescendant(form, fieldName) as T;
            Assert.NotNull(descendant);
            return descendant;
        }

        private static T GetPrivateControl<T>(
            Form form,
            Type formType,
            string fieldName)
            where T : Control
        {
            FieldInfo field = formType.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            T control = field.GetValue(form) as T;
            Assert.NotNull(control);
            return control;
        }

        private static bool IsDescendantOf(Control control, Control expectedParent)
        {
            for (Control parent = control.Parent; parent != null; parent = parent.Parent)
            {
                if (parent == expectedParent)
                {
                    return true;
                }
            }

            return false;
        }

        private static Control FindDescendant(Control root, string name)
        {
            if (root.Name == name)
            {
                return root;
            }

            foreach (Control child in root.Controls)
            {
                Control match = FindDescendant(child, name);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static Rectangle BoundsRelativeTo(Control control, Control ancestor)
        {
            if (control == ancestor)
            {
                return new Rectangle(Point.Empty, control.Size);
            }

            Point location = control.Location;
            for (Control parent = control.Parent;
                parent != null;
                parent = parent.Parent)
            {
                if (parent == ancestor)
                {
                    return new Rectangle(location, control.Size);
                }
                location.Offset(parent.Location);
            }

            throw new InvalidOperationException(String.Format(
                CultureInfo.InvariantCulture,
                "{0} is not a descendant of {1}.",
                control.Name,
                ancestor.Name));
        }

        private static void AssertContainedIn(Control child, Control parent)
        {
            Rectangle bounds = BoundsRelativeTo(child, parent);
            Assert.True(
                parent.ClientRectangle.Contains(bounds),
                String.Format(
                    "{0} is outside {1}: child={2}, parent={3}.",
                    child.Name,
                    parent.Name,
                    bounds,
                    parent.ClientRectangle));
        }

        private static double ContrastRatio(Color first, Color second)
        {
            double firstLuminance = RelativeLuminance(first);
            double secondLuminance = RelativeLuminance(second);
            double lighter = Math.Max(firstLuminance, secondLuminance);
            double darker = Math.Min(firstLuminance, secondLuminance);
            return (lighter + 0.05D) / (darker + 0.05D);
        }

        private static double RelativeLuminance(Color color)
        {
            return 0.2126D * Linearize(color.R) +
                0.7152D * Linearize(color.G) +
                0.0722D * Linearize(color.B);
        }

        private static double Linearize(byte component)
        {
            double value = component / 255D;
            return value <= 0.04045D
                ? value / 12.92D
                : Math.Pow((value + 0.055D) / 1.055D, 2.4D);
        }

        private static void AssertCollisionPreviewDoesNotCoverColorControls(
            AssemblyExportForm form)
        {
            TabControl sections = GetControl<TabControl>(form, "modernLinkSections");
            sections.SelectedIndex = 1;
            sections.PerformLayout();
            Button previewButton = GetControl<Button>(form, "buttonShowCollisionPreview");
            Label previewStatus = GetControl<Label>(form, "labelCollisionPreviewStatus");
            TrackBar reduction = GetControl<TrackBar>(form, "trackBarMeshReduction");
            Label estimate = GetControl<Label>(form, "labelEstimatedMeshSize");
            GroupBox geometry = GetControl<GroupBox>(form, "groupBox4");
            geometry.PerformLayout();

            Assert.False(BoundsRelativeTo(previewButton, geometry).IntersectsWith(
                BoundsRelativeTo(previewStatus, geometry)));
            Assert.False(BoundsRelativeTo(reduction, geometry).IntersectsWith(
                BoundsRelativeTo(estimate, geometry)));

            sections.SelectedIndex = 2;
            sections.PerformLayout();
            DomainUpDown red = GetControl<DomainUpDown>(form, "domainUpDownRed");
            DomainUpDown blue = GetControl<DomainUpDown>(form, "domainUpDownBlue");
            DomainUpDown alpha = GetControl<DomainUpDown>(form, "domainUpDownAlpha");
            Panel appearance = GetControl<Panel>(form, "modernAppearancePanel");
            Panel colorPreview = GetControl<Panel>(form, "panelMaterialColorPreview");
            Button pickColor = GetControl<Button>(form, "buttonMaterialColorPick");
            Button automaticColors = GetControl<Button>(form, "buttonAutomaticLinkColors");
            appearance.PerformLayout();

            Assert.True(IsDescendantOf(red, appearance));
            Assert.True(IsDescendantOf(blue, appearance));
            Assert.True(IsDescendantOf(alpha, appearance));
            Assert.False(IsDescendantOf(previewButton, appearance));
            Assert.False(BoundsRelativeTo(colorPreview, appearance).IntersectsWith(
                BoundsRelativeTo(pickColor, appearance)));
            Assert.False(BoundsRelativeTo(pickColor, appearance).IntersectsWith(
                BoundsRelativeTo(automaticColors, appearance)));
        }

        private static void AssertJointFooterGeometry(AssemblyExportForm form)
        {
            Label firstNote = GetControl<Label>(form, "label4");
            Label secondNote = GetControl<Label>(form, "label27");
            Button nextButton = GetControl<Button>(form, "buttonJointNext");
            Button cancelButton = GetControl<Button>(form, "buttonJointCancel");
            TreeView jointTree = GetControl<TreeView>(form, "treeViewJointTree");
            Control footer = GetControl<Control>(form, "modernJointFooter");
            Rectangle firstNoteBounds = BoundsRelativeTo(firstNote, form);
            Rectangle secondNoteBounds = BoundsRelativeTo(secondNote, form);
            Rectangle nextBounds = BoundsRelativeTo(nextButton, form);
            Rectangle cancelBounds = BoundsRelativeTo(cancelButton, form);
            Rectangle treeBounds = BoundsRelativeTo(jointTree, form);
            Rectangle footerBounds = BoundsRelativeTo(footer, form);

            Assert.True(
                firstNoteBounds.Bottom <= secondNoteBounds.Top,
                String.Format("Joint footer lines overlap: {0} > {1}.",
                    firstNoteBounds.Bottom, secondNoteBounds.Top));
            Assert.False(firstNoteBounds.IntersectsWith(nextBounds));
            Assert.False(secondNoteBounds.IntersectsWith(nextBounds));
            Assert.False(firstNoteBounds.IntersectsWith(cancelBounds));
            Assert.False(secondNoteBounds.IntersectsWith(cancelBounds));
            Assert.True(
                treeBounds.Bottom <= footerBounds.Top,
                String.Format("Joint tree overlaps footer: {0} > {1}.",
                    treeBounds.Bottom, footerBounds.Top));
            Assert.True(
                footerBounds.Bottom <= form.ClientSize.Height,
                String.Format("Joint footer is clipped: {0} > {1}.",
                    footerBounds.Bottom, form.ClientSize.Height));
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
                BoundsRelativeTo(label, label.FindForm()).Right <= maxRight,
                String.Format("{0} is clipped horizontally: right {1}, max {2}.",
                    label.Name,
                    BoundsRelativeTo(label, label.FindForm()).Right,
                    maxRight));
            Assert.True(
                measured.Height <= label.Height,
                String.Format(
                    "{0} is clipped vertically: preferred {1}, actual {2}, " +
                    "width {3}, maximum {4}, control chain {5}.",
                    label.Name,
                    measured.Height,
                    label.Height,
                    label.Width,
                    label.MaximumSize,
                    DescribeControlChain(label)));
        }

        private static string DescribeControlChain(Control control)
        {
            string result = String.Empty;
            for (Control current = control; current != null; current = current.Parent)
            {
                if (result.Length > 0)
                {
                    result += " <- ";
                }
                result += String.Format(
                    CultureInfo.InvariantCulture,
                    "{0}[w={1},cw={2},dock={3},auto={4}]",
                    String.IsNullOrWhiteSpace(current.Name)
                        ? current.GetType().Name
                        : current.Name,
                    current.Width,
                    current.ClientSize.Width,
                    current.Dock,
                    current.AutoSize);
            }
            return result;
        }

        private static void AssertMimicControlsDoNotOverlapFooter(AssemblyExportForm form)
        {
            TabControl sections = GetControl<TabControl>(form, "modernJointSections");
            sections.SelectedIndex = 2;
            sections.PerformLayout();
            CheckBox mimicCheckBox = GetControl<CheckBox>(form, "MimicCheckBox");
            mimicCheckBox.Checked = true;
            form.PerformLayout();

            Control mimicCard = GetControl<Control>(form, "modernMimicCard");
            Panel jointContent = GetControl<Panel>(form, "modernJointContentPanel");
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
                Assert.True(IsDescendantOf(mimicControl, mimicCard));
            }
            Assert.True(IsDescendantOf(mimicCard, jointContent));

            TextBox offsetBox = GetControl<TextBox>(form, "textBoxMimicOffset");
            Label equation = GetControl<Label>(form, "MimicEquationLabel");
            Rectangle offsetBounds = BoundsRelativeTo(offsetBox, mimicCard);
            Rectangle equationBounds = BoundsRelativeTo(equation, mimicCard);
            Assert.True(
                !equationBounds.IntersectsWith(offsetBounds),
                String.Format(
                    "Mimic equation overlaps offset input: equation={0}, offset={1}.",
                    equationBounds,
                    offsetBounds));
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
                GetControl<Button>(form, "modernLinkNextButton"),
                GetControl<Button>(form, "modernModelCancelButton"),
                GetControl<Button>(form, "modernModelPreviousButton"),
                GetControl<Button>(form, "modernUsdSettingsButton"),
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

        private static void AssertFooterTabOrder(AssemblyExportForm form)
        {
            Assert.Equal(0, GetControl<Button>(form, "buttonJointCancel").TabIndex);
            Assert.Equal(1, GetControl<Button>(form, "buttonJointNext").TabIndex);
            Assert.Equal(0, GetControl<Button>(form, "buttonLinksCancel").TabIndex);
            Assert.Equal(1, GetControl<Button>(form, "buttonLinksPrevious").TabIndex);
            Assert.Equal(2, GetControl<Button>(form, "modernLinkNextButton").TabIndex);
            Assert.Equal(0, GetControl<Button>(form, "modernModelCancelButton").TabIndex);
            Assert.Equal(1, GetControl<Button>(form, "modernModelPreviousButton").TabIndex);
            Assert.Equal(2, GetControl<Button>(form, "modernUsdSettingsButton").TabIndex);
            Assert.Equal(3, GetControl<Button>(form, "buttonLinksExportUrdfOnly").TabIndex);
            Assert.Equal(4, GetControl<Button>(form, "buttonLinksFinish").TabIndex);
        }

        private static void AssertEditorTabOrder(AssemblyExportForm form)
        {
            Assert.True(
                GetControl<TextBox>(form, "textBoxJointName").TabIndex <
                GetControl<ComboBox>(form, "comboBoxJointType").TabIndex);
            Assert.True(
                GetControl<ComboBox>(form, "comboBoxOrigin").TabIndex <
                GetControl<ComboBox>(form, "comboBoxAxis").TabIndex);
            Assert.True(
                GetControl<TextBox>(form, "textBoxMass").TabIndex <
                GetControl<Button>(form, "buttonShowInertiaPreview").TabIndex);
            Assert.True(
                GetControl<ComboBox>(form, "comboBoxCollisionStrategy").TabIndex <
                GetControl<Button>(form, "buttonShowCollisionPreview").TabIndex);
        }

        private static void AssertInertiaMatrixMirrors(AssemblyExportForm form)
        {
            TabControl sections = GetControl<TabControl>(form, "modernLinkSections");
            sections.SelectedIndex = 0;
            sections.PerformLayout();
            TextBox ixy = GetControl<TextBox>(form, "textBoxIxy");
            TextBox ixz = GetControl<TextBox>(form, "textBoxIxz");
            TextBox iyz = GetControl<TextBox>(form, "textBoxIyz");
            TextBox iyxMirror = GetControl<TextBox>(form, "textBoxIyxMirror");
            TextBox izxMirror = GetControl<TextBox>(form, "textBoxIzxMirror");
            TextBox izyMirror = GetControl<TextBox>(form, "textBoxIzyMirror");
            TextBox iyy = GetControl<TextBox>(form, "textBoxIyy");
            TextBox izz = GetControl<TextBox>(form, "textBoxIzz");
            Button previewButton = GetControl<Button>(form, "buttonShowInertiaPreview");
            GroupBox inertia = GetControl<GroupBox>(form, "groupBox5");
            inertia.PerformLayout();

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
            Assert.True(
                BoundsRelativeTo(iyxMirror, inertia).Right <
                BoundsRelativeTo(iyy, inertia).Left);
            Assert.True(
                BoundsRelativeTo(izxMirror, inertia).Right <
                BoundsRelativeTo(izyMirror, inertia).Left);
            Assert.True(
                BoundsRelativeTo(izyMirror, inertia).Right <
                BoundsRelativeTo(izz, inertia).Left);
            Assert.True(
                BoundsRelativeTo(izz, inertia).Bottom <
                BoundsRelativeTo(previewButton, inertia).Top);
        }
    }
}
