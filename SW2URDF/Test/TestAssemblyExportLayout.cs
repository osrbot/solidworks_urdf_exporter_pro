using SW2URDF.UI;
using SW2URDF.URDFExport;
using System;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

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
        public void TestRos2CompatibilityPairIsCapturedAsOneAtomicSelection()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);
            try
            {
                ComboBox pair = GetControl<ComboBox>(form, "modernRos2PairComboBox");
                pair.SelectedIndex = 1;
                MethodInfo capture = typeof(AssemblyExportForm).GetMethod(
                    "CaptureExportTargetOptions",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(capture);
                ExportTargetOptions options = (ExportTargetOptions)capture.Invoke(form, null);
                Assert.Equal("jazzy", options.Ros2Distribution);
                Assert.Equal("harmonic", options.GazeboDistribution);

                pair.SelectedIndex = 0;
                options = (ExportTargetOptions)capture.Invoke(form, null);
                Assert.Equal("lyrical", options.Ros2Distribution);
                Assert.Equal("jetty", options.GazeboDistribution);
            }
            finally
            {
                form.Dispose();
            }
        }

        [Fact]
        public void TestMaterialPresetUpdatesRgbaWithoutTextureEditor()
        {
            AssemblyExportForm form = (AssemblyExportForm)
                Activator.CreateInstance(typeof(AssemblyExportForm), true);

            try
            {
                ComboBox materials = GetControl<ComboBox>(form, "comboBoxMaterials");
                materials.Text = "green";

                MethodInfo applyPreset = typeof(AssemblyExportForm).GetMethod(
                    "MaterialPresetSelectionChangeCommitted",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(applyPreset);
                applyPreset.Invoke(form, new object[] { materials, EventArgs.Empty });

                Assert.Null(typeof(AssemblyExportForm).GetField(
                    "textBoxTexture",
                    BindingFlags.Instance | BindingFlags.NonPublic));
                Assert.Null(typeof(AssemblyExportForm).GetField(
                    "buttonTextureBrowse",
                    BindingFlags.Instance | BindingFlags.NonPublic));
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
                sections.SelectedIndex = 1;
                sections.PerformLayout();
                GroupBox meshGroup = GetControl<GroupBox>(form, "groupBox4");
                meshGroup.PerformLayout();
                Button automaticColors = GetControl<Button>(
                    form,
                    "buttonAutomaticLinkColors");
                Button pickColor = GetControl<Button>(form, "buttonMaterialColorPick");
                DomainUpDown alpha = GetControl<DomainUpDown>(form, "domainUpDownAlpha");
                Label meshReduction = GetControl<Label>(form, "labelMeshReduction");

                Assert.True(IsDescendantOf(automaticColors, meshGroup));
                Assert.True(automaticColors.Enabled);
                Assert.False(BoundsRelativeTo(automaticColors, meshGroup).IntersectsWith(
                    BoundsRelativeTo(pickColor, meshGroup)));
                Assert.False(BoundsRelativeTo(automaticColors, meshGroup).IntersectsWith(
                    BoundsRelativeTo(alpha, meshGroup)));
                Assert.False(BoundsRelativeTo(automaticColors, meshGroup).IntersectsWith(
                    BoundsRelativeTo(meshReduction, meshGroup)));
                Assert.True(BoundsRelativeTo(
                    automaticColors,
                    meshGroup).Right <= meshGroup.ClientSize.Width);
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
                Assert.Equal(2, linkSections.TabPages.Count);
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
                Assert.True(IsDescendantOf(automaticColors, meshGroup));
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
                Button ros2Profile = GetControl<Button>(
                    form,
                    "modernRos2ControlProfileButton");
                Button actuatorProfile = GetControl<Button>(
                    form,
                    "modernIsaacLabProfileButton");

                Assert.False(IsDescendantOf(maintainer, linkRoot));
                Assert.False(IsDescendantOf(author, linkRoot));
                Assert.True(IsDescendantOf(maintainer, modelRoot));
                Assert.True(IsDescendantOf(author, modelRoot));
                Assert.True(IsDescendantOf(ros2Profile, modelRoot));
                Assert.True(IsDescendantOf(actuatorProfile, modelRoot));
                Assert.Equal(FlatStyle.Flat, ros2Profile.FlatStyle);
                Assert.Equal(FlatStyle.Flat, actuatorProfile.FlatStyle);
                Assert.True(ros2Profile.AutoSize);
                Assert.True(actuatorProfile.AutoSize);
                Assert.Equal(0, GetControl<TextBox>(
                    form,
                    "modernRos2ControlProfileTextBox").TabIndex);
                Assert.Equal(1, ros2Profile.TabIndex);
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
                    "Bundle | ROS2 | ROS1 legacy: rover_description",
                    packageHint.Text);
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

                Panel jointRoot = GetControl<Panel>(form, "modernJointRoot");
                Assert.True(IsDescendantOf(guideButton, jointRoot));
                Assert.True(guideButton.Enabled);
                Assert.True(guideButton.Width >= TextRenderer.MeasureText(
                    guideButton.Text,
                    guideButton.Font).Width + guideButton.Padding.Horizontal);
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
            Assert.Equal("kitso666 <kitso@osrbot.com>", UsageGuideForm.VersionMaintainer);
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
                    TextBox guideText = Assert.IsType<TextBox>(
                        FindDescendant(guide, "usageGuideTextBox"));
                    TableLayoutPanel guideCard = Assert.IsType<ModernCardPanel>(
                        FindDescendant(guide, "usageGuideCard"));
                    TableLayoutPanel footer = Assert.IsType<TableLayoutPanel>(
                        FindDescendant(guide, "usageGuideFooter"));
                    Button close = Assert.IsType<Button>(
                        FindDescendant(guide, "usageGuideCloseButton"));

                    Assert.True(guideText.ReadOnly);
                    Assert.Equal(DockStyle.Fill, guideCard.Dock);
                    Assert.False(guideCard.Bounds.IntersectsWith(footer.Bounds));
                    AssertContainedIn(close, footer);
                }
                finally
                {
                    exportForm.Dispose();
                }
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
            DomainUpDown red = GetControl<DomainUpDown>(form, "domainUpDownRed");
            DomainUpDown blue = GetControl<DomainUpDown>(form, "domainUpDownBlue");
            DomainUpDown alpha = GetControl<DomainUpDown>(form, "domainUpDownAlpha");
            GroupBox geometry = GetControl<GroupBox>(form, "groupBox4");
            Panel colorPreview = GetControl<Panel>(form, "panelMaterialColorPreview");
            Button pickColor = GetControl<Button>(form, "buttonMaterialColorPick");
            Button automaticColors = GetControl<Button>(form, "buttonAutomaticLinkColors");
            TrackBar reduction = GetControl<TrackBar>(form, "trackBarMeshReduction");
            Label estimate = GetControl<Label>(form, "labelEstimatedMeshSize");
            geometry.PerformLayout();

            Assert.False(BoundsRelativeTo(previewButton, geometry).IntersectsWith(
                BoundsRelativeTo(red, geometry)));
            Assert.False(BoundsRelativeTo(previewStatus, geometry).IntersectsWith(
                BoundsRelativeTo(blue, geometry)));
            Assert.False(BoundsRelativeTo(previewStatus, geometry).IntersectsWith(
                BoundsRelativeTo(alpha, geometry)));
            Assert.False(BoundsRelativeTo(previewButton, geometry).IntersectsWith(
                BoundsRelativeTo(previewStatus, geometry)));
            Assert.False(BoundsRelativeTo(colorPreview, geometry).IntersectsWith(
                BoundsRelativeTo(pickColor, geometry)));
            Assert.False(BoundsRelativeTo(pickColor, geometry).IntersectsWith(
                BoundsRelativeTo(automaticColors, geometry)));
            Assert.False(BoundsRelativeTo(reduction, geometry).IntersectsWith(
                BoundsRelativeTo(estimate, geometry)));
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
            Assert.Equal(2, GetControl<Button>(form, "buttonLinksExportUrdfOnly").TabIndex);
            Assert.Equal(3, GetControl<Button>(form, "buttonLinksFinish").TabIndex);
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
