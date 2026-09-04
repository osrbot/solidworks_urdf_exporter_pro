/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OSURDF.Core.Model;
using SW2URDF.URDF;

namespace SW2URDF.UI
{
    public partial class AssemblyExportForm
    {
        private enum ModernAssemblyPage
        {
            Joint,
            Link,
            Model
        }

        private bool modernUiInitialized;
        private bool modernPageShown;
        // Keep workflow state explicit; visibility is a rendering detail and
        // must not decide which editor data is captured when the form closes.
        private ModernAssemblyPage modernActivePage;
        private Panel modernJointRoot;
        private Panel modernJointContentPanel;
        private Panel modernLinkContentPanel;
        private Panel modernAppearancePanel;
        private Panel modernModelRoot;
        private ModernTabControl modernJointSections;
        private ModernTabControl modernLinkSections;
        private TableLayoutPanel modernMimicDetails;
        private bool? modernMimicExpanded;
        private Button modernLinkUsageGuideButton;
        private Button modernModelUsageGuideButton;
        private Button modernLinkNextButton;
        private Button modernModelCancelButton;
        private Button modernModelPreviousButton;
        private Size modernMinimumSizeAfterInitialScale;
        private Size modernClientSizeAfterInitialScale;
        private CheckBox modernRos2CheckBox;
        private CheckBox modernRos1CheckBox;
        private CheckBox modernUsdAssetCheckBox;
        private CheckBox modernMjcfAssetCheckBox;
        private Button modernUsdSettingsButton;
        private OpenUsdSettingsDialog openUsdSettingsDialog;
        private UsdSimulationProfile modernUsdSimulationProfile =
            new UsdSimulationProfile();
        private TextBox modernMaterialIdTextBox;
        private TextBox modernPackageVersionTextBox;
        private TextBox modernPackageDescriptionTextBox;
        private TextBox modernMaintainerNameTextBox;
        private TextBox modernMaintainerEmailTextBox;
        private TextBox modernModelLicenseTextBox;
        private TextBox modernModelAuthorTextBox;
        private bool modernJointTreeExpandedOnce;
        private bool modernLinkTreeExpandedOnce;
        private Size modernJointExplicitLayoutSize;
        private Size modernLinkExplicitLayoutSize;
        private Size modernModelExplicitLayoutSize;
        private int modernModelExplicitLayoutCount;
        private readonly List<TableLayoutPanel> modernModelFrozenLayouts =
            new List<TableLayoutPanel>();
        private bool loadingModernExportTargets;

        internal void InitializeModernUi()
        {
            if (modernUiInitialized)
            {
                return;
            }

            modernUiInitialized = true;
            enableLayoutFixes = false;
            SizeF initialScaleFactor = AutoScaleFactor;
            modernMinimumSizeAfterInitialScale = ScaleModernSize(
                new Size(1040, 680),
                initialScaleFactor);
            modernClientSizeAfterInitialScale = ScaleModernSize(
                new Size(1120, 700),
                initialScaleFactor);
            SuspendLayout();
            try
            {
                ModernWinFormsTheme.Apply(this);
                MinimumSize = new Size(1040, 680);
                ClientSize = new Size(
                    Math.Max(ClientSize.Width, 1120),
                    Math.Max(ClientSize.Height, 700));
                BackColor = ModernWinFormsTheme.Background;
                DoubleBuffered = true;

                BuildModernJointPage();
                BuildModernLinkPage();
                BuildModernModelPage();
                ApplyModernAssemblySpecificStyles();
                WireModernAssemblyLayoutEvents();
                ShowModernAssemblyPage(ModernAssemblyPage.Joint);
            }
            finally
            {
                ResumeLayout(true);
            }

            // Page construction happens while the form is suspended. Any cache
            // captured during that phase contains pre-layout child heights.
            // Release it now; the load-time priming pass will cache settled grids.
            modernJointSections.ReleaseCachedPageLayouts();
            modernLinkSections.ReleaseCachedPageLayouts();
        }

        private void ApplyModernInitialScaleBounds()
        {
            if (!modernUiInitialized)
            {
                return;
            }

            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            Size workingAreaSize = workingArea.Size;
            int nonClientWidth = Math.Max(0, Width - ClientSize.Width);
            int nonClientHeight = Math.Max(0, Height - ClientSize.Height);
            Size maximumClientSize = new Size(
                Math.Max(1, workingAreaSize.Width - nonClientWidth),
                Math.Max(1, workingAreaSize.Height - nonClientHeight));

            MinimumSize = ConstrainModernSize(
                modernMinimumSizeAfterInitialScale,
                workingAreaSize);
            ClientSize = ConstrainModernSize(
                modernClientSizeAfterInitialScale,
                maximumClientSize);
            PrimeModernPageLayouts();
        }

        internal static Size ConstrainModernSize(Size desired, Size maximum)
        {
            return new Size(
                Math.Max(1, Math.Min(desired.Width, maximum.Width)),
                Math.Max(1, Math.Min(desired.Height, maximum.Height)));
        }

        private static Size ScaleModernSize(Size designSize, SizeF factor)
        {
            return new Size(
                Math.Max(1, (int)Math.Round(
                    designSize.Width * factor.Width,
                    MidpointRounding.AwayFromZero)),
                Math.Max(1, (int)Math.Round(
                    designSize.Height * factor.Height,
                    MidpointRounding.AwayFromZero)));
        }

        private void BuildModernJointPage()
        {
            modernJointRoot = new Panel
            {
                Name = "modernJointRoot",
                BackColor = ModernWinFormsTheme.Background,
                Dock = DockStyle.Fill
            };

            Control header = CreateModernHeader(
                "modernJoint",
                label7,
                ChineseUiText.Translate(
                    "Define joint identity, reference geometry, motion axis and constraints.",
                    "配置关节标识、参考几何、运动轴及约束参数。"),
                ChineseUiText.Translate("Step 1 of 3 · Joint properties", "第 1/3 步 · Joint 属性"),
                buttonUsageGuide,
                null);
            header.Name = "modernJointHeader";
            Control footer = CreateModernJointFooter();

            TableLayoutPanel body = new TableLayoutPanel
            {
                Name = "modernJointBody",
                BackColor = ModernWinFormsTheme.Background,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(20, 16, 20, 16),
                RowCount = 1
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Control treeCard = CreateModernTreeCard(
                ChineseUiText.Translate("Joint hierarchy", "Joint 层级"),
                ChineseUiText.Translate(
                    "Select a joint to edit its URDF properties.",
                    "选择关节后编辑对应的 URDF 属性。"),
                treeViewJointTree);
            treeCard.Margin = new Padding(0, 0, 16, 0);
            body.Controls.Add(treeCard, 0, 0);

            modernJointContentPanel = new Panel
            {
                Name = "modernJointContentPanel",
                AutoScroll = false,
                BackColor = ModernWinFormsTheme.Background,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            modernJointSections = CreateModernSectionTabs("modernJointSections");
            TabPage basicsPage = CreateModernTabPage(
                "modernJointBasicsPage",
                ChineseUiText.Translate("Basics", "基本"));
            ConfigureCompactJointTabPage(basicsPage);
            TableLayoutPanel basicsStack = CreateModernStack();

            label69.Text = ChineseUiText.Translate(
                "Coordinate systems and axes come from SolidWorks reference geometry. Edit the model to change them.",
                "坐标系与轴来自 SolidWorks 参考几何；如需调整，请返回模型中修改。");
            basicsStack.Controls.Add(CreateModernJointIdentityAndReferenceGrid());
            basicsStack.Controls.Add(CreateModernOriginAndAxisGrid());
            basicsPage.Controls.Add(basicsStack);

            TabPage constraintsPage = CreateModernTabPage(
                "modernJointConstraintsPage",
                ChineseUiText.Translate("Constraints", "约束与安全"));
            ConfigureCompactJointTabPage(constraintsPage);
            TableLayoutPanel constraintsStack = CreateModernStack();
            constraintsStack.Controls.Add(CreateModernAdvancedJointGrid());
            constraintsPage.Controls.Add(constraintsStack);

            TabPage mimicPage = CreateModernTabPage(
                "modernJointMimicPage",
                "Mimic");
            ((ModernTabPage)mimicPage).CacheAutoSizeLayout = false;
            TableLayoutPanel mimicStack = CreateModernStack();
            mimicStack.AutoSize = false;
            mimicStack.Dock = DockStyle.Fill;
            mimicStack.RowCount = 1;
            mimicStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mimicStack.Controls.Add(CreateModernMimicCard());
            mimicPage.Controls.Add(mimicStack);

            modernJointSections.TabPages.Add(basicsPage);
            modernJointSections.TabPages.Add(constraintsPage);
            modernJointSections.TabPages.Add(mimicPage);
            modernJointContentPanel.Controls.Add(modernJointSections);
            body.Controls.Add(modernJointContentPanel, 1, 0);

            modernJointRoot.Controls.Add(body);
            modernJointRoot.Controls.Add(footer);
            modernJointRoot.Controls.Add(header);
            Controls.Add(modernJointRoot);
        }

        private static TableLayoutPanel CreateModernStack()
        {
            TableLayoutPanel stack = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Background,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                Padding = new Padding(0),
                RowCount = 0
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return stack;
        }

        private static void ConfigureCompactJointTabPage(TabPage page)
        {
            // The joint pages contain several dense field cards. Keep their
            // normal small-window scrolling fallback, but avoid a scrollbar at
            // the exporter's standard size by reclaiming unused bottom inset.
            page.Padding = new Padding(6, 6, 6, 0);
        }

        private void BuildModernLinkPage()
        {
            panelLinkProperties.SuspendLayout();
            try
            {
                panelLinkProperties.Controls.Clear();
                panelLinkProperties.AutoScroll = false;
                panelLinkProperties.BackColor = ModernWinFormsTheme.Background;
                panelLinkProperties.Padding = new Padding(0);

                modernLinkUsageGuideButton = new Button
                {
                    Name = "modernLinkUsageGuideButton",
                    Text = buttonUsageGuide.Text,
                    TabIndex = 301
                };
                modernLinkUsageGuideButton.Click += ButtonUsageGuideClick;

                Control header = CreateModernHeader(
                    "modernLink",
                    label5,
                    label2.Text,
                    ChineseUiText.Translate("Step 2 of 3 · Link properties", "第 2/3 步 · Link 属性"),
                    modernLinkUsageGuideButton,
                    label2);
                header.Name = "modernLinkHeader";
                Control footer = CreateModernLinkFooter();
                footer.Name = "modernLinkFooter";

                TableLayoutPanel body = new TableLayoutPanel
                {
                    Name = "modernLinkBody",
                    BackColor = ModernWinFormsTheme.Background,
                    ColumnCount = 2,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    Padding = new Padding(20, 16, 20, 16),
                    RowCount = 1
                };
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                Control treeCard = CreateModernTreeCard(
                    ChineseUiText.Translate("Link hierarchy", "Link 层级"),
                    ChineseUiText.Translate(
                        "Select a Link to edit inertia, appearance and collision settings.",
                        "选择 Link 后编辑惯性、外观及碰撞设置。"),
                    treeViewLinkProperties);
                treeCard.Margin = new Padding(0, 0, 16, 0);
                body.Controls.Add(treeCard, 0, 0);

                modernLinkContentPanel = new Panel
                {
                    Name = "modernLinkContentPanel",
                    AutoScroll = false,
                    BackColor = ModernWinFormsTheme.Background,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    Padding = new Padding(0)
                };

                label15.Visible = false;
                groupBox5.Text = ChineseUiText.Translate("Inertial properties", "惯性属性");
                RebuildModernInertialLayout();

                label19.Visible = false;
                groupBox4.Text = ChineseUiText.Translate(
                    "Visual and collision geometry",
                    "可视与碰撞几何");
                RebuildModernVisualCollisionLayout();
                RebuildModernAppearanceLayout();

                modernLinkSections = CreateModernSectionTabs("modernLinkSections");
                TabPage inertiaPage = CreateModernTabPage(
                    "modernLinkInertiaPage",
                    ChineseUiText.Translate("Inertia", "惯性"));
                groupBox5.Dock = DockStyle.Fill;
                groupBox5.Margin = new Padding(0);
                inertiaPage.Controls.Add(groupBox5);

                TabPage geometryPage = CreateModernTabPage(
                    "modernLinkGeometryPage",
                    ChineseUiText.Translate("Visual / Collision", "可视 / 碰撞"));
                groupBox4.Dock = DockStyle.Fill;
                groupBox4.Margin = new Padding(0);
                geometryPage.Controls.Add(groupBox4);

                TabPage appearancePage = CreateModernTabPage(
                    "modernLinkAppearancePage",
                    ChineseUiText.Translate("Appearance", "外观"));
                modernAppearancePanel.Dock = DockStyle.Fill;
                modernAppearancePanel.Margin = new Padding(0);
                appearancePage.Controls.Add(modernAppearancePanel);

                modernLinkSections.TabPages.Add(inertiaPage);
                modernLinkSections.TabPages.Add(geometryPage);
                modernLinkSections.TabPages.Add(appearancePage);
                modernLinkContentPanel.Controls.Add(modernLinkSections);
                body.Controls.Add(modernLinkContentPanel, 1, 0);

                panelLinkProperties.Controls.Add(body);
                panelLinkProperties.Controls.Add(footer);
                panelLinkProperties.Controls.Add(header);
            }
            finally
            {
                panelLinkProperties.ResumeLayout(true);
            }
        }

        private void BuildModernModelPage()
        {
            modernModelRoot = new Panel
            {
                Name = "modernModelRoot",
                BackColor = ModernWinFormsTheme.Background,
                Dock = DockStyle.Fill,
                Visible = false
            };

            modernModelUsageGuideButton = new Button
            {
                Name = "modernModelUsageGuideButton",
                Text = buttonUsageGuide.Text,
                TabIndex = 303
            };
            modernModelUsageGuideButton.Click += ButtonUsageGuideClick;

            Label title = new Label
            {
                Name = "modernModelTitle",
                Text = ChineseUiText.Translate("Model and export", "模型与导出")
            };
            Control header = CreateModernHeader(
                "modernModel",
                title,
                ChineseUiText.Translate(
                    "Set robot-wide metadata and output targets once for the complete model.",
                    "在这里统一设置整机模型信息与输出目标，无需逐个 Link 重复编辑。"),
                ChineseUiText.Translate("Step 3 of 3 · Model settings", "第 3/3 步 · 模型设置"),
                modernModelUsageGuideButton,
                null);
            header.Name = "modernModelHeader";
            Control footer = CreateModernModelFooter();

            TableLayoutPanel body = new TableLayoutPanel
            {
                Name = "modernModelBody",
                BackColor = ModernWinFormsTheme.Background,
                ColumnCount = 3,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(20, 16, 20, 16),
                RowCount = 1
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 84F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel stack = CreateModernStack();
            Label scopeMessage = new Label
            {
                Name = "modernModelScopeMessage",
                Text = ChineseUiText.Translate(
                    "These values belong to the robot package, not to an individual Link.",
                    "以下信息属于整机功能包，不属于任何单独 Link。")
            };
            Control scopeBanner = CreateModernInfoBanner(scopeMessage);
            scopeBanner.Margin = new Padding(0, 0, 0, 8);
            stack.Controls.Add(scopeBanner);
            Control packageCard = CreateModernPackageCard();
            packageCard.Margin = new Padding(0);
            stack.Controls.Add(packageCard);
            Panel modelContent = new Panel
            {
                Name = "modernModelContentPanel",
                AutoScroll = true,
                BackColor = ModernWinFormsTheme.Background,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0, 0, 8, 0)
            };
            modelContent.Controls.Add(stack);
            body.Controls.Add(modelContent, 1, 0);

            modernModelRoot.Controls.Add(body);
            modernModelRoot.Controls.Add(footer);
            modernModelRoot.Controls.Add(header);
            Controls.Add(modernModelRoot);
        }

        private static ModernTabControl CreateModernSectionTabs(string name)
        {
            ModernTabControl tabs = new ModernTabControl
            {
                Name = name,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Point(16, 6),
                SizeMode = TabSizeMode.Normal
            };
            return tabs;
        }

        private static TabPage CreateModernTabPage(string name, string text)
        {
            return new ModernTabPage
            {
                Name = name,
                Text = text,
                AutoScroll = true,
                BackColor = ModernWinFormsTheme.Background,
                Padding = new Padding(12)
            };
        }

        private Control CreateModernHeader(
            string controlPrefix,
            Label titleLabel,
            string subtitle,
            string stepText,
            Button guideButton,
            Label reusableSubtitleLabel)
        {
            Panel header = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Surface,
                Dock = DockStyle.Top,
                Height = 84,
                MinimumSize = new Size(0, 84),
                Padding = new Padding(22, 14, 22, 12)
            };
            header.Paint += ModernWinFormsTheme.DrawBottomBorder;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Surface,
                ColumnCount = 3,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel titleStack = new TableLayoutPanel
            {
                AutoSize = true,
                BackColor = ModernWinFormsTheme.Surface,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                RowCount = 2
            };
            titleStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            titleLabel.AutoSize = true;
            ModernWinFormsTheme.SetFont(titleLabel, 15F, FontStyle.Bold);
            titleLabel.ForeColor = ModernWinFormsTheme.Text;
            titleLabel.Margin = new Padding(0, 0, 0, 3);
            Label subtitleLabel = reusableSubtitleLabel ?? new Label();
            subtitleLabel.Name = controlPrefix + "Subtitle";
            subtitleLabel.AutoSize = true;
            subtitleLabel.Text = subtitle;
            subtitleLabel.Margin = new Padding(0);
            ModernWinFormsTheme.SetFont(
                subtitleLabel,
                9F,
                FontStyle.Regular);
            subtitleLabel.MaximumSize = new Size(620, 0);
            subtitleLabel.ForeColor = ModernWinFormsTheme.MutedText;
            titleStack.Controls.Add(titleLabel, 0, 0);
            titleStack.Controls.Add(subtitleLabel, 0, 1);

            Label stepLabel = new Label
            {
                Name = controlPrefix + "Step",
                AutoSize = true,
                BackColor = ModernWinFormsTheme.SurfaceAlt,
                BorderStyle = BorderStyle.FixedSingle,
                ForeColor = ModernWinFormsTheme.MutedText,
                Margin = new Padding(12, 11, 12, 0),
                Padding = new Padding(10, 6, 10, 6),
                Text = stepText
            };
            ModernWinFormsTheme.SetFont(stepLabel, 9F, FontStyle.Bold);

            guideButton.Margin = new Padding(0, 8, 0, 0);
            guideButton.Size = new Size(96, 34);

            layout.Controls.Add(titleStack, 0, 0);
            layout.Controls.Add(stepLabel, 1, 0);
            layout.Controls.Add(guideButton, 2, 0);
            header.Controls.Add(layout);
            return header;
        }

        private Control CreateModernTreeCard(string title, string hint, TreeView treeView)
        {
            TableLayoutPanel card = new TableLayoutPanel
            {
                BackColor = ModernWinFormsTheme.Surface,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(16, 14, 16, 16),
                RowCount = 3
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            card.Paint += ModernWinFormsTheme.DrawCardBorder;

            Label titleLabel = ModernWinFormsTheme.CreateTextLabel(title, 10.5F, FontStyle.Bold);
            titleLabel.Margin = new Padding(0, 0, 0, 4);
            Label hintLabel = ModernWinFormsTheme.CreateTextLabel(hint, 8.5F, FontStyle.Regular);
            hintLabel.ForeColor = ModernWinFormsTheme.MutedText;
            hintLabel.Margin = new Padding(0, 0, 0, 12);
            // The hierarchy column includes the card margin and padding, so keep
            // the hint inside its usable width after the compact 280 px split.
            hintLabel.MaximumSize = new Size(220, 0);

            treeView.Dock = DockStyle.Fill;
            treeView.Margin = new Padding(0);
            card.Controls.Add(titleLabel, 0, 0);
            card.Controls.Add(hintLabel, 0, 1);
            card.Controls.Add(treeView, 0, 2);
            return card;
        }

        private Control CreateModernInfoBanner(Label messageLabel)
        {
            Panel banner = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.AccentTint,
                Dock = DockStyle.Top,
                Height = 54,
                MinimumSize = new Size(0, 54),
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(14, 8, 14, 8)
            };
            banner.Paint += ModernWinFormsTheme.DrawCardBorder;
            messageLabel.AutoSize = true;
            messageLabel.Dock = DockStyle.Fill;
            messageLabel.MaximumSize = new Size(900, 0);
            ModernWinFormsTheme.SetFont(messageLabel, 8.75F, FontStyle.Regular);
            messageLabel.ForeColor = ModernWinFormsTheme.Text;
            messageLabel.TextAlign = ContentAlignment.MiddleLeft;
            banner.Controls.Add(messageLabel);
            return banner;
        }

        private Control CreateModernJointIdentityAndReferenceGrid()
        {
            TableLayoutPanel grid = new TableLayoutPanel
            {
                Name = "modernJointIdentityAndReferenceGrid",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Background,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                RowCount = 2
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Control identity = CreateModernJointIdentityCard();
            identity.Margin = new Padding(0, 0, 0, 4);
            Control reference = CreateModernReferenceGeometryCard();
            reference.Margin = new Padding(0, 0, 0, 4);
            grid.Controls.Add(identity, 0, 0);
            grid.Controls.Add(reference, 0, 1);
            return grid;
        }

        private Control CreateModernJointIdentityCard()
        {
            TableLayoutPanel card = ModernWinFormsTheme.CreateCard("modernJointIdentityCard");
            card.Padding = new Padding(16, 8, 16, 8);
            card.Controls.Add(CreateModernCardTitle(
                ChineseUiText.Translate("Joint identity", "Joint 基本信息"),
                null));

            TableLayoutPanel relationship = new TableLayoutPanel
            {
                Name = "modernJointRelationshipGrid",
                AutoSize = true,
                BackColor = ModernWinFormsTheme.Surface,
                ColumnCount = 3,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 2, 0, 2),
                RowCount = 2
            };
            relationship.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            relationship.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));
            relationship.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            relationship.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            relationship.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            ModernWinFormsTheme.StyleFieldLabel(label64);
            ModernWinFormsTheme.StyleFieldLabel(label65);
            ModernWinFormsTheme.StyleReadoutLabel(labelParent);
            ModernWinFormsTheme.StyleReadoutLabel(labelChild);
            label64.Margin = new Padding(0, 0, 0, 2);
            label65.Margin = new Padding(0, 0, 0, 2);
            labelParent.Margin = new Padding(0, 0, 0, 2);
            labelChild.Margin = new Padding(0, 0, 0, 2);
            Label arrow = ModernWinFormsTheme.CreateTextLabel(
                "\u2192",
                16F,
                FontStyle.Bold);
            arrow.Name = "modernJointRelationArrow";
            arrow.AccessibleName = ChineseUiText.Translate(
                "Parent Link to Child Link",
                "父 Link 指向子 Link");
            arrow.Dock = DockStyle.Fill;
            arrow.ForeColor = ModernWinFormsTheme.Accent;
            arrow.Margin = new Padding(4, 0, 4, 0);
            arrow.TextAlign = ContentAlignment.MiddleCenter;

            relationship.Controls.Add(label64, 0, 0);
            relationship.Controls.Add(label65, 2, 0);
            relationship.Controls.Add(labelParent, 0, 1);
            relationship.Controls.Add(arrow, 1, 0);
            relationship.SetRowSpan(arrow, 2);
            relationship.Controls.Add(labelChild, 2, 1);
            card.Controls.Add(relationship);

            TableLayoutPanel fields = new TableLayoutPanel
            {
                Name = "modernJointIdentityFields",
                AutoSize = true,
                ColumnCount = 4,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 2, 0, 0),
                RowCount = 1
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AddModernField(fields, label63, textBoxJointName, 0, 0, 1);
            AddModernField(fields, label62, comboBoxJointType, 0, 2, 3);
            card.Controls.Add(fields);
            return card;
        }

        private Control CreateModernReferenceGeometryCard()
        {
            TableLayoutPanel card = ModernWinFormsTheme.CreateCard("modernReferenceGeometryCard");
            card.Margin = new Padding(0);
            card.Padding = new Padding(16, 8, 16, 8);
            Control title = CreateModernCardTitle(
                ChineseUiText.Translate("Reference geometry", "参考几何"),
                null);
            title.Name = "modernReferenceGeometryTitle";
            card.Controls.Add(title);
            packagePathToolTip.SetToolTip(title, label69.Text);

            label69.AutoSize = true;
            label69.Dock = DockStyle.Top;
            label69.ForeColor = ModernWinFormsTheme.MutedText;
            label69.Margin = new Padding(0, 2, 0, 0);
            ModernWinFormsTheme.SetFont(label69, 8.5F, FontStyle.Regular);
            card.Controls.Add(label69);

            TableLayoutPanel grid = new TableLayoutPanel
            {
                Name = "modernReferenceGeometryFields",
                AutoSize = true,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 4, 0, 0),
                RowCount = 2
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AddModernField(grid, label66, comboBoxOrigin, 0, 0, 1);
            AddModernField(grid, label67, comboBoxAxis, 1, 0, 1);
            ConfigureModernReferenceGeometrySelector(comboBoxOrigin);
            ConfigureModernReferenceGeometrySelector(comboBoxAxis);
            card.Controls.Add(grid);
            return card;
        }

        private void ConfigureModernReferenceGeometrySelector(ComboBox selector)
        {
            selector.DropDown -= ModernReferenceGeometrySelectorDropDown;
            selector.DropDown += ModernReferenceGeometrySelectorDropDown;
            selector.SelectedIndexChanged -= ModernReferenceGeometrySelectorSelectedIndexChanged;
            selector.SelectedIndexChanged += ModernReferenceGeometrySelectorSelectedIndexChanged;
            UpdateModernReferenceGeometrySelectorToolTip(selector);
        }

        private void ModernReferenceGeometrySelectorDropDown(object sender, EventArgs e)
        {
            ComboBox selector = sender as ComboBox;
            UpdateModernReferenceGeometrySelectorDropDownWidth(selector);
            UpdateModernReferenceGeometrySelectorToolTip(selector);
        }

        private void ModernReferenceGeometrySelectorSelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            UpdateModernReferenceGeometrySelectorToolTip(sender as ComboBox);
        }

        private void UpdateModernReferenceGeometrySelectorDropDownWidth(ComboBox selector)
        {
            if (selector == null)
            {
                return;
            }

            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            selector.DropDownWidth = CalculateReferenceGeometryDropDownWidth(
                selector,
                Math.Max(selector.Width, workingArea.Width - 48));
        }

        private void UpdateModernReferenceGeometrySelectorToolTip(ComboBox selector)
        {
            if (selector != null && packagePathToolTip != null)
            {
                string selectedText = selector.SelectedItem == null
                    ? selector.Text
                    : selector.GetItemText(selector.SelectedItem);
                packagePathToolTip.SetToolTip(selector, selectedText ?? String.Empty);
            }
        }

        internal static int CalculateReferenceGeometryDropDownWidth(
            ComboBox selector,
            int maximumWidth)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            int width = selector.Width;
            foreach (object item in selector.Items)
            {
                string text = selector.GetItemText(item);
                int itemWidth = TextRenderer.MeasureText(
                    text ?? String.Empty,
                    selector.Font).Width +
                    SystemInformation.VerticalScrollBarWidth + 24;
                width = Math.Max(width, itemWidth);
            }

            int effectiveMaximum = Math.Max(selector.Width, maximumWidth);
            return Math.Min(width, effectiveMaximum);
        }

        private Control CreateModernOriginAndAxisGrid()
        {
            TableLayoutPanel grid = new TableLayoutPanel
            {
                Name = "modernOriginAndAxisGrid",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Background,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                RowCount = 1
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Control originCard = CreateModernOriginCard();
            originCard.Margin = new Padding(0, 0, 6, 0);
            Control axisCard = CreateModernAxisCard();
            axisCard.Margin = new Padding(6, 0, 0, 0);
            grid.Controls.Add(originCard, 0, 0);
            grid.Controls.Add(axisCard, 1, 0);
            return grid;
        }

        private Control CreateModernOriginCard()
        {
            TableLayoutPanel card = ModernWinFormsTheme.CreateCard("modernOriginCard");
            card.Margin = new Padding(0);
            card.Padding = new Padding(16, 10, 16, 12);
            card.Controls.Add(CreateModernCardTitle(label54, label1));

            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 4,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 8, 0, 0),
                RowCount = 4
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            for (int row = 0; row < 4; row++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            PrepareModernColumnHeader(label3);
            PrepareModernColumnHeader(label6);
            grid.Controls.Add(label3, 1, 0);
            grid.Controls.Add(label6, 3, 0);
            AddModernValueRow(grid, label52, textBoxJointX, label51, textBoxJointRoll, 1);
            AddModernValueRow(grid, label53, textBoxJointY, label55, textBoxJointPitch, 2);
            AddModernValueRow(grid, label57, textBoxJointZ, label56, textBoxJointYaw, 3);
            card.Controls.Add(grid);
            return card;
        }

        private Control CreateModernAxisCard()
        {
            TableLayoutPanel card = ModernWinFormsTheme.CreateCard("modernAxisCard");
            card.Margin = new Padding(0);
            card.Padding = new Padding(16, 10, 16, 12);
            card.Controls.Add(CreateModernCardTitle(label60, AxisRequiredLabel));

            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 8, 0, 0),
                RowCount = 3
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int row = 0; row < 3; row++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            AddModernField(grid, label58, textBoxAxisX, 0, 0, 1);
            AddModernField(grid, label59, textBoxAxisY, 1, 0, 1);
            AddModernField(grid, label61, textBoxAxisZ, 2, 0, 1);
            card.Controls.Add(grid);
            return card;
        }

        private Control CreateModernAdvancedJointGrid()
        {
            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Background,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                RowCount = 2
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Control limits = CreateModernTwoColumnFieldCard(
                "modernLimitsCard",
                label68,
                LimitRequiredLabel,
                new Label[] { labelLowerLimit, labelLimitUpper, labelEffort, labelVelocity },
                new Control[] { textBoxLimitLower, textBoxLimitUpper, textBoxLimitEffort, textBoxLimitVelocity },
                152F);
            limits.Margin = new Padding(0, 0, 6, 6);
            Control calibration = CreateModernTwoColumnFieldCard(
                "modernCalibrationCard",
                label74,
                null,
                new Label[] { label7CalibrationRising, label73 },
                new Control[] { textBoxCalibrationRising, textBoxCalibrationFalling },
                112F);
            calibration.Margin = new Padding(6, 0, 0, 6);
            Control dynamics = CreateModernTwoColumnFieldCard(
                "modernDynamicsCard",
                label76,
                null,
                new Label[] { labelFriction, labelDamping },
                new Control[] { textBoxFriction, textBoxDamping },
                168F);
            dynamics.Margin = new Padding(0, 0, 6, 0);
            Control safety = CreateModernTwoColumnFieldCard(
                "modernSafetyCard",
                label80,
                null,
                new Label[] { labelSoftLower, labelSoftUpper, labelKPosition, labelKVelocity },
                new Control[] { textBoxSoftLower, textBoxSoftUpper, textBoxKPosition, textBoxKVelocity },
                152F);
            safety.Margin = new Padding(6, 0, 0, 0);

            grid.Controls.Add(limits, 0, 0);
            grid.Controls.Add(calibration, 1, 0);
            grid.Controls.Add(dynamics, 0, 1);
            grid.Controls.Add(safety, 1, 1);
            return grid;
        }

        private Control CreateModernMimicCard()
        {
            TableLayoutPanel card = ModernWinFormsTheme.CreateCard("modernMimicCard");
            MimicCheckBox.AutoSize = true;
            MimicCheckBox.Dock = DockStyle.Top;
            ModernWinFormsTheme.SetFont(MimicCheckBox, 10.5F, FontStyle.Bold);
            MimicCheckBox.ForeColor = ModernWinFormsTheme.Text;
            MimicCheckBox.Margin = new Padding(0);
            card.Controls.Add(MimicCheckBox);

            modernMimicDetails = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 4,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 12, 0, 0),
                RowCount = 3
            };
            modernMimicDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122F));
            modernMimicDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            modernMimicDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            modernMimicDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            modernMimicDetails.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            modernMimicDetails.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            modernMimicDetails.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            modernMimicDetails.SizeChanged += delegate
            {
                UpdateModernMimicEquationWrapWidth();
            };

            AddModernField(modernMimicDetails, MimicJointLabel, MimicJointComboBox, 0, 0, 1);
            modernMimicDetails.SetColumnSpan(MimicJointComboBox, 3);
            AddModernField(modernMimicDetails, MimicMultiplierLabel, textBoxMimicMultiplier, 1, 0, 1);
            AddModernField(modernMimicDetails, MimicOffsetLabel, textBoxMimicOffset, 1, 2, 3);
            MimicJointLabel.AutoSize = false;
            MimicJointLabel.Dock = DockStyle.Fill;
            MimicJointComboBox.Dock = DockStyle.Fill;
            MimicMultiplierLabel.AutoSize = false;
            MimicMultiplierLabel.Dock = DockStyle.Fill;
            textBoxMimicMultiplier.Dock = DockStyle.Fill;
            MimicOffsetLabel.AutoSize = false;
            MimicOffsetLabel.Dock = DockStyle.Fill;
            textBoxMimicOffset.Dock = DockStyle.Fill;
            MimicEquationLabel.AutoSize = true;
            MimicEquationLabel.Dock = DockStyle.Fill;
            ModernWinFormsTheme.SetFont(MimicEquationLabel, 8.5F, FontStyle.Italic);
            MimicEquationLabel.ForeColor = ModernWinFormsTheme.MutedText;
            MimicEquationLabel.Margin = new Padding(0, 6, 0, 0);
            MimicEquationLabel.MaximumSize = new Size(700, 0);
            modernMimicDetails.Controls.Add(MimicEquationLabel, 0, 2);
            modernMimicDetails.SetColumnSpan(MimicEquationLabel, 4);
            card.Controls.Add(modernMimicDetails);
            SynchronizeModernMimicLayout();
            return card;
        }

        private void RebuildModernInertialLayout()
        {
            groupBox5.SuspendLayout();
            try
            {
                groupBox5.Controls.Clear();
                groupBox5.BackColor = ModernWinFormsTheme.Surface;
                groupBox5.Padding = new Padding(12, 20, 12, 12);

                TableLayoutPanel root = new TableLayoutPanel
                {
                    Name = "modernInertialLayout",
                    BackColor = ModernWinFormsTheme.Surface,
                    ColumnCount = 1,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    Padding = new Padding(0),
                    RowCount = 3
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                TableLayoutPanel frame = new TableLayoutPanel
                {
                    AutoSize = true,
                    ColumnCount = 2,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0, 0, 0, 8),
                    RowCount = 1
                };
                frame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
                frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                AddModernField(
                    frame,
                    labelLinkCoordinateSystem,
                    comboBoxLinkCoordinateSystem,
                    0,
                    0,
                    1);

                TableLayoutPanel body = new TableLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    BackColor = ModernWinFormsTheme.Surface,
                    ColumnCount = 2,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0, 0, 0, 8),
                    RowCount = 1
                };
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                TableLayoutPanel originGrid = new TableLayoutPanel
                {
                    AutoSize = true,
                    ColumnCount = 4,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0),
                    RowCount = 3
                };
                originGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24F));
                originGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                originGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54F));
                originGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                for (int row = 0; row < 3; row++)
                {
                    originGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                }
                AddModernValueRow(
                    originGrid,
                    label13,
                    textBoxInertialOriginX,
                    label16,
                    textBoxInertialOriginRoll,
                    0);
                AddModernValueRow(
                    originGrid,
                    label17,
                    textBoxInertialOriginY,
                    label45,
                    textBoxInertialOriginPitch,
                    1);
                AddModernValueRow(
                    originGrid,
                    label47,
                    textBoxInertialOriginZ,
                    label46,
                    textBoxInertialOriginYaw,
                    2);
                Control origin = CreateModernSubsection(
                    "modernInertialOriginSection",
                    label36,
                    originGrid);
                origin.Margin = new Padding(0, 0, 6, 0);

                TableLayoutPanel matrixGrid = new TableLayoutPanel
                {
                    AutoSize = true,
                    ColumnCount = 6,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0),
                    RowCount = 3
                };
                for (int pair = 0; pair < 3; pair++)
                {
                    matrixGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
                    matrixGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
                }
                for (int row = 0; row < 3; row++)
                {
                    matrixGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                }
                AddModernField(matrixGrid, label14, textBoxIxx, 0, 0, 1);
                AddModernField(matrixGrid, label50, textBoxIxy, 0, 2, 3);
                AddModernField(matrixGrid, label11, textBoxIxz, 0, 4, 5);
                AddModernField(matrixGrid, labelInertiaIyx, textBoxIyxMirror, 1, 0, 1);
                AddModernField(matrixGrid, label49, textBoxIyy, 1, 2, 3);
                AddModernField(matrixGrid, label48, textBoxIyz, 1, 4, 5);
                AddModernField(matrixGrid, labelInertiaIzx, textBoxIzxMirror, 2, 0, 1);
                AddModernField(matrixGrid, labelInertiaIzy, textBoxIzyMirror, 2, 2, 3);
                AddModernField(matrixGrid, label18, textBoxIzz, 2, 4, 5);
                Control matrix = CreateModernSubsection(
                    "modernInertiaMatrixSection",
                    label44,
                    matrixGrid);
                matrix.Margin = new Padding(6, 0, 0, 0);

                body.Controls.Add(origin, 0, 0);
                body.Controls.Add(matrix, 1, 0);

                TableLayoutPanel actions = new TableLayoutPanel
                {
                    AutoSize = true,
                    ColumnCount = 4,
                    Dock = DockStyle.Bottom,
                    Margin = new Padding(0),
                    RowCount = 1
                };
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176F));
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                AddModernField(actions, label12, textBoxMass, 0, 0, 1);
                buttonShowInertiaPreview.Dock = DockStyle.Fill;
                buttonShowInertiaPreview.Margin = new Padding(8, 4, 8, 4);
                buttonShowInertiaPreview.MinimumSize = new Size(0, 28);
                buttonShowInertiaPreview.TabIndex = 2;
                labelInertiaPreviewStatus.AutoSize = false;
                labelInertiaPreviewStatus.AutoEllipsis = true;
                labelInertiaPreviewStatus.Dock = DockStyle.Fill;
                labelInertiaPreviewStatus.ForeColor = ModernWinFormsTheme.MutedText;
                labelInertiaPreviewStatus.Margin = new Padding(0, 4, 0, 4);
                labelInertiaPreviewStatus.TextAlign = ContentAlignment.MiddleLeft;
                ModernWinFormsTheme.SetFont(
                    labelInertiaPreviewStatus,
                    8.5F,
                    FontStyle.Regular);
                actions.Controls.Add(buttonShowInertiaPreview, 2, 0);
                actions.Controls.Add(labelInertiaPreviewStatus, 3, 0);

                root.Controls.Add(frame, 0, 0);
                root.Controls.Add(body, 0, 1);
                root.Controls.Add(actions, 0, 2);
                groupBox5.Controls.Add(root);
                groupBox5.Controls.Add(label15);
                label15.Visible = false;
            }
            finally
            {
                groupBox5.ResumeLayout(true);
            }
        }

        private void RebuildModernVisualCollisionLayout()
        {
            groupBox4.SuspendLayout();
            try
            {
                groupBox4.Controls.Clear();
                groupBox4.BackColor = ModernWinFormsTheme.Surface;
                groupBox4.Padding = new Padding(12, 20, 12, 12);

                TableLayoutPanel root = new TableLayoutPanel
                {
                    Name = "modernVisualCollisionLayout",
                    BackColor = ModernWinFormsTheme.Surface,
                    ColumnCount = 2,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    Padding = new Padding(0),
                    RowCount = 1
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                TableLayoutPanel left = new TableLayoutPanel
                {
                    AutoSize = true,
                    BackColor = ModernWinFormsTheme.Surface,
                    ColumnCount = 1,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0, 0, 6, 0),
                    RowCount = 2
                };
                left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                TableLayoutPanel visualOriginGrid = new TableLayoutPanel
                {
                    AutoSize = true,
                    ColumnCount = 4,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0),
                    RowCount = 3
                };
                visualOriginGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24F));
                visualOriginGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                visualOriginGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54F));
                visualOriginGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                for (int row = 0; row < 3; row++)
                {
                    visualOriginGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                }
                AddModernValueRow(
                    visualOriginGrid,
                    label26,
                    textBoxVisualOriginX,
                    label22,
                    textBoxVisualOriginRoll,
                    0);
                AddModernValueRow(
                    visualOriginGrid,
                    label25,
                    textBoxVisualOriginY,
                    label21,
                    textBoxVisualOriginPitch,
                    1);
                AddModernValueRow(
                    visualOriginGrid,
                    label24,
                    textBoxVisualOriginZ,
                    label20,
                    textBoxVisualOriginYaw,
                    2);
                Control visualOrigin = CreateModernSubsection(
                    "modernVisualOriginSection",
                    label23,
                    visualOriginGrid);
                visualOrigin.Margin = new Padding(0, 0, 0, 8);
                left.Controls.Add(visualOrigin, 0, 0);

                TableLayoutPanel meshOptions = new TableLayoutPanel
                {
                    AutoSize = true,
                    ColumnCount = 2,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0),
                    RowCount = 1
                };
                meshOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                meshOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

                FlowLayoutPanel detailChoices = new FlowLayoutPanel
                {
                    AutoSize = true,
                    Dock = DockStyle.Top,
                    FlowDirection = FlowDirection.LeftToRight,
                    Margin = new Padding(0),
                    WrapContents = true
                };
                radioButtonCourse.AutoSize = true;
                radioButtonFine.AutoSize = true;
                radioButtonCourse.Margin = new Padding(0, 0, 12, 0);
                radioButtonFine.Margin = new Padding(0);
                detailChoices.Controls.Add(radioButtonCourse);
                detailChoices.Controls.Add(radioButtonFine);
                Control detail = CreateModernSubsection(
                    "modernMeshDetailSection",
                    label10,
                    detailChoices);
                detail.Margin = new Padding(0, 0, 6, 0);

                groupBox1.SuspendLayout();
                groupBox1.Controls.Clear();
                groupBox1.Text = ChineseUiText.Translate("Mesh format", "网格格式");
                groupBox1.Dock = DockStyle.Fill;
                groupBox1.Margin = new Padding(6, 0, 0, 0);
                groupBox1.Padding = new Padding(8, 18, 8, 8);
                FlowLayoutPanel formats = new FlowLayoutPanel
                {
                    AutoSize = true,
                    Dock = DockStyle.Top,
                    FlowDirection = FlowDirection.TopDown,
                    Margin = new Padding(0),
                    WrapContents = false
                };
                radioButtonStl.AutoSize = true;
                radioButton3dxml.AutoSize = true;
                radioButtonStl.Margin = new Padding(0, 0, 0, 4);
                radioButton3dxml.Margin = new Padding(0);
                formats.Controls.Add(radioButtonStl);
                formats.Controls.Add(radioButton3dxml);
                groupBox1.Controls.Add(formats);
                groupBox1.ResumeLayout(true);

                meshOptions.Controls.Add(detail, 0, 0);
                meshOptions.Controls.Add(groupBox1, 1, 0);
                left.Controls.Add(meshOptions, 0, 1);

                TableLayoutPanel right = new TableLayoutPanel
                {
                    BackColor = ModernWinFormsTheme.Surface,
                    ColumnCount = 1,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(6, 0, 0, 0),
                    RowCount = 2
                };
                right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                TableLayoutPanel collisionGrid = new TableLayoutPanel
                {
                    AutoSize = true,
                    ColumnCount = 2,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0),
                    RowCount = 3
                };
                collisionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
                collisionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                collisionGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                collisionGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                collisionGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                AddModernField(
                    collisionGrid,
                    labelCollisionStrategy,
                    comboBoxCollisionStrategy,
                    0,
                    0,
                    1);
                buttonShowCollisionPreview.Dock = DockStyle.Fill;
                buttonShowCollisionPreview.Margin = new Padding(0, 4, 0, 4);
                buttonShowCollisionPreview.MinimumSize = new Size(0, 28);
                buttonShowCollisionPreview.TabIndex = 2;
                collisionGrid.Controls.Add(buttonShowCollisionPreview, 0, 1);
                collisionGrid.SetColumnSpan(buttonShowCollisionPreview, 2);
                labelCollisionPreviewStatus.AutoSize = true;
                labelCollisionPreviewStatus.AutoEllipsis = false;
                labelCollisionPreviewStatus.Dock = DockStyle.Fill;
                labelCollisionPreviewStatus.ForeColor = ModernWinFormsTheme.MutedText;
                labelCollisionPreviewStatus.Margin = new Padding(0, 0, 0, 4);
                labelCollisionPreviewStatus.MaximumSize = new Size(420, 0);
                ModernWinFormsTheme.SetFont(
                    labelCollisionPreviewStatus,
                    8.5F,
                    FontStyle.Regular);
                collisionGrid.Controls.Add(labelCollisionPreviewStatus, 0, 2);
                collisionGrid.SetColumnSpan(labelCollisionPreviewStatus, 2);
                Control collision = CreateModernSubsection(
                    "modernCollisionStrategySection",
                    ChineseUiText.Translate("Collision", "碰撞体"),
                    collisionGrid);
                collision.Margin = new Padding(0, 0, 0, 8);
                right.Controls.Add(collision, 0, 0);

                TableLayoutPanel reduction = new TableLayoutPanel
                {
                    AutoSize = true,
                    ColumnCount = 2,
                    Dock = DockStyle.Bottom,
                    Margin = new Padding(0),
                    RowCount = 3
                };
                reduction.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                reduction.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                reduction.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                reduction.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                reduction.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                PrepareModernColumnHeader(labelMeshReduction);
                PrepareModernColumnHeader(labelMeshReductionValue);
                labelMeshReductionValue.TextAlign = ContentAlignment.MiddleRight;
                reduction.Controls.Add(labelMeshReduction, 0, 0);
                reduction.Controls.Add(labelMeshReductionValue, 1, 0);
                trackBarMeshReduction.Dock = DockStyle.Fill;
                trackBarMeshReduction.Margin = new Padding(0, 2, 0, 0);
                reduction.Controls.Add(trackBarMeshReduction, 0, 1);
                reduction.SetColumnSpan(trackBarMeshReduction, 2);
                labelEstimatedMeshSize.AutoSize = true;
                labelEstimatedMeshSize.Dock = DockStyle.Fill;
                labelEstimatedMeshSize.ForeColor = ModernWinFormsTheme.MutedText;
                labelEstimatedMeshSize.Margin = new Padding(0, 0, 0, 0);
                labelEstimatedMeshSize.MaximumSize = new Size(420, 0);
                ModernWinFormsTheme.SetFont(
                    labelEstimatedMeshSize,
                    8.5F,
                    FontStyle.Regular);
                reduction.Controls.Add(labelEstimatedMeshSize, 0, 2);
                reduction.SetColumnSpan(labelEstimatedMeshSize, 2);
                Control reductionSection = CreateModernSubsection(
                    "modernMeshReductionSection",
                    ChineseUiText.Translate("Mesh export", "网格导出"),
                    reduction);
                right.Controls.Add(reductionSection, 0, 1);

                root.Controls.Add(left, 0, 0);
                root.Controls.Add(right, 1, 0);
                groupBox4.Controls.Add(root);
                groupBox4.Controls.Add(label19);
                label19.Visible = false;
            }
            finally
            {
                groupBox4.ResumeLayout(true);
            }
        }

        private void RebuildModernAppearanceLayout()
        {
            modernAppearancePanel = new Panel
            {
                Name = "modernAppearancePanel",
                AutoScroll = true,
                BackColor = ModernWinFormsTheme.Surface,
                Padding = new Padding(12)
            };

            TableLayoutPanel root = new TableLayoutPanel
            {
                Name = "modernAppearanceLayout",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Surface,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                Padding = new Padding(0),
                RowCount = 2
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            modernMaterialIdTextBox = new TextBox
            {
                Name = "modernMaterialIdTextBox",
                ReadOnly = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                TabStop = false
            };
            ModernWinFormsTheme.StyleInput(modernMaterialIdTextBox);
            label28.AutoSize = true;
            label28.AutoEllipsis = false;
            Control material = CreateModernSubsection(
                "modernAppearanceMaterialSection",
                label28,
                modernMaterialIdTextBox);
            material.Margin = new Padding(0, 0, 0, 10);
            root.Controls.Add(material, 0, 0);

            TableLayoutPanel rgbaGrid = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 4,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                RowCount = 2
            };
            rgbaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
            rgbaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rgbaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
            rgbaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rgbaGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rgbaGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AddModernField(rgbaGrid, label30, domainUpDownRed, 0, 0, 1);
            AddModernField(rgbaGrid, label31, domainUpDownGreen, 0, 2, 3);
            AddModernField(rgbaGrid, label32, domainUpDownBlue, 1, 0, 1);
            AddModernField(rgbaGrid, label33, domainUpDownAlpha, 1, 2, 3);

            TableLayoutPanel colorActions = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(12, 0, 0, 0),
                MinimumSize = new Size(150, 0),
                RowCount = 3
            };
            colorActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            colorActions.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            colorActions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            colorActions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panelMaterialColorPreview.Dock = DockStyle.Fill;
            panelMaterialColorPreview.Margin = new Padding(0, 0, 0, 6);
            panelMaterialColorPreview.MinimumSize = new Size(150, 48);
            buttonMaterialColorPick.Dock = DockStyle.Fill;
            buttonMaterialColorPick.Margin = new Padding(0, 0, 0, 6);
            buttonMaterialColorPick.MinimumSize = new Size(150, 30);
            buttonAutomaticLinkColors.Dock = DockStyle.Fill;
            buttonAutomaticLinkColors.Margin = new Padding(0);
            ResizeButtonToText(buttonAutomaticLinkColors);
            buttonAutomaticLinkColors.MinimumSize = new Size(
                Math.Max(150, buttonAutomaticLinkColors.Width),
                Math.Max(30, buttonAutomaticLinkColors.Height));
            colorActions.Controls.Add(panelMaterialColorPreview, 0, 0);
            colorActions.Controls.Add(buttonMaterialColorPick, 0, 1);
            colorActions.Controls.Add(buttonAutomaticLinkColors, 0, 2);

            TableLayoutPanel colorGrid = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                RowCount = 1
            };
            colorGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            colorGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            colorGrid.Controls.Add(rgbaGrid, 0, 0);
            colorGrid.Controls.Add(colorActions, 1, 0);
            label29.AutoSize = true;
            label29.AutoEllipsis = false;
            Control color = CreateModernSubsection(
                "modernAppearanceColorSection",
                label29,
                colorGrid);
            root.Controls.Add(color, 0, 1);

            modernAppearancePanel.Controls.Add(root);
            SynchronizeMaterialIdFromRgba();
        }

        private static Control CreateModernSubsection(
            string name,
            string title,
            Control content)
        {
            return CreateModernSubsection(
                name,
                ModernWinFormsTheme.CreateTextLabel(title, 9F, FontStyle.Bold),
                content);
        }

        private static Control CreateModernSubsection(
            string name,
            Label title,
            Control content)
        {
            TableLayoutPanel section = new TableLayoutPanel
            {
                Name = name,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.SurfaceAlt,
                ColumnCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0),
                Padding = new Padding(10, 8, 10, 10),
                RowCount = 2
            };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.Paint += ModernWinFormsTheme.DrawCardBorder;

            title.AutoSize = true;
            title.Dock = DockStyle.Top;
            title.ForeColor = ModernWinFormsTheme.Text;
            title.Margin = new Padding(0, 0, 0, 6);
            ModernWinFormsTheme.SetFont(title, 9F, FontStyle.Bold);
            content.Dock = DockStyle.Top;
            content.Margin = new Padding(0, 0, 0, 2);
            section.Controls.Add(title, 0, 0);
            section.Controls.Add(content, 0, 1);
            return section;
        }

        private Control CreateModernPackageCard()
        {
            TableLayoutPanel card = ModernWinFormsTheme.CreateCard("modernPackageCard");
            card.Controls.Add(CreateModernCardTitle(
                ChineseUiText.Translate(
                    "Model metadata and output targets",
                    "模型信息与输出目标"),
                null));

            FlowLayoutPanel targets = new FlowLayoutPanel
            {
                Name = "modernExportTargetRow",
                AutoSize = true,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 10, 0, 8),
                WrapContents = true
            };
            modernRos1CheckBox = CreateTargetCheckBox(
                "ROS 1 package",
                "ROS 1 功能包",
                true);
            modernRos1CheckBox.Name = "modernRos1CheckBox";
            modernRos2CheckBox = CreateTargetCheckBox(
                "ROS 2 package",
                "ROS 2 功能包",
                true);
            modernRos2CheckBox.Name = "modernRos2CheckBox";
            modernUsdAssetCheckBox = CreateTargetCheckBox(
                "OpenUSD robot asset",
                "OpenUSD 机器人资产",
                false);
            modernUsdAssetCheckBox.Name = "modernUsdAssetCheckBox";
            modernMjcfAssetCheckBox = CreateTargetCheckBox(
                "MuJoCo MJCF asset",
                "MuJoCo MJCF 资产",
                false);
            modernMjcfAssetCheckBox.Name = "modernMjcfAssetCheckBox";
            modernRos1CheckBox.CheckedChanged += ModernTargetSelectionChanged;
            modernRos2CheckBox.CheckedChanged += ModernTargetSelectionChanged;
            modernUsdAssetCheckBox.CheckedChanged += ModernTargetSelectionChanged;
            modernMjcfAssetCheckBox.CheckedChanged += ModernTargetSelectionChanged;
            targets.Controls.Add(modernRos1CheckBox);
            targets.Controls.Add(modernRos2CheckBox);
            targets.Controls.Add(modernUsdAssetCheckBox);
            targets.Controls.Add(modernMjcfAssetCheckBox);
            card.Controls.Add(targets);

            TableLayoutPanel grid = new TableLayoutPanel
            {
                Name = "modernPackageMetadataGrid",
                AutoSize = true,
                ColumnCount = 4,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 4, 0, 0),
                Padding = new Padding(0, 0, 0, 2),
                RowCount = 5
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            for (int row = 0; row < grid.RowCount; row++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            AddModernField(grid, labelRosPackageName, textBoxRosPackageName, 0, 0, 1);
            modernPackageVersionTextBox = CreateTargetTextBox("0.1.0");
            AddModernField(grid, CreateTargetLabel("Package version", "功能包版本"), modernPackageVersionTextBox, 0, 2, 3);
            labelRosPackageNameHint.AutoSize = false;
            labelRosPackageNameHint.AutoEllipsis = true;
            labelRosPackageNameHint.Dock = DockStyle.Fill;
            labelRosPackageNameHint.MinimumSize = new Size(0, 20);
            ModernWinFormsTheme.SetFont(labelRosPackageNameHint, 8.5F, FontStyle.Regular);
            labelRosPackageNameHint.ForeColor = ModernWinFormsTheme.MutedText;
            labelRosPackageNameHint.Margin = new Padding(0, 2, 0, 0);
            grid.Controls.Add(labelRosPackageNameHint, 1, 1);
            grid.SetColumnSpan(labelRosPackageNameHint, 3);

            modernPackageDescriptionTextBox = CreateTargetTextBox(string.Empty);
            AddModernField(grid, CreateTargetLabel("Description", "功能包说明"), modernPackageDescriptionTextBox, 2, 0, 1);
            grid.SetColumnSpan(modernPackageDescriptionTextBox, 3);
            modernMaintainerNameTextBox = CreateTargetTextBox(string.Empty);
            modernMaintainerEmailTextBox = CreateTargetTextBox(string.Empty);
            AddModernField(grid, CreateTargetLabel("Maintainer", "维护者"), modernMaintainerNameTextBox, 3, 0, 1);
            AddModernField(grid, CreateTargetLabel("Email", "维护者邮箱"), modernMaintainerEmailTextBox, 3, 2, 3);
            modernModelLicenseTextBox = CreateTargetTextBox(string.Empty);
            modernModelAuthorTextBox = CreateTargetTextBox(string.Empty);
            AddModernField(grid, CreateTargetLabel("Model license", "模型许可证"), modernModelLicenseTextBox, 4, 0, 1);
            AddModernField(
                grid,
                CreateTargetLabel(
                    "Model author / configurator",
                    "模型作者 / 配置者"),
                modernModelAuthorTextBox,
                4,
                2,
                3);

            card.Controls.Add(grid);
            ModernWinFormsTheme.ApplyControlTree(card);
            SynchronizeAssetMeshFormatControls();
            return card;
        }

        private CheckBox CreateTargetCheckBox(
            string english,
            string chinese,
            bool isChecked)
        {
            return new CheckBox
            {
                AutoSize = true,
                Checked = isChecked,
                Margin = new Padding(0, 0, 16, 4),
                Text = ChineseUiText.Translate(english, chinese),
                UseVisualStyleBackColor = true
            };
        }

        private static TextBox CreateTargetTextBox(string value)
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 8, 6),
                Text = value
            };
        }

        private static Label CreateTargetLabel(string english, string chinese)
        {
            return new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 4),
                Text = ChineseUiText.Translate(english, chinese),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private void ModernTargetSelectionChanged(object sender, EventArgs e)
        {
            if (modernUsdSettingsButton != null)
            {
                modernUsdSettingsButton.Enabled = modernUsdAssetCheckBox.Checked;
            }
            SynchronizeAssetMeshFormatControls();
            if (!loadingModernExportTargets)
            {
                UpdateRosPackageNameHintForTargetChange();
            }
        }

        private void SynchronizeAssetMeshFormatControls()
        {
            if (modernUsdAssetCheckBox == null || modernMjcfAssetCheckBox == null ||
                radioButtonStl == null || radioButton3dxml == null)
            {
                return;
            }
            bool requiresCanonicalStl =
                modernUsdAssetCheckBox.Checked || modernMjcfAssetCheckBox.Checked;
            if (requiresCanonicalStl)
            {
                radioButtonStl.Checked = true;
            }
            radioButton3dxml.Enabled = !requiresCanonicalStl;
            packagePathToolTip.SetToolTip(
                radioButton3dxml,
                requiresCanonicalStl
                    ? ChineseUiText.Translate(
                        "USD and MJCF assets require canonical STL meshes.",
                        "USD 与 MJCF 资产需要规范 STL 网格。")
                    : ChineseUiText.Translate(
                        "Available when USD and MJCF asset export are both off.",
                        "仅在未导出 USD/MJCF 资产时可选。"));
        }

        private Control CreateModernJointFooter()
        {
            Panel footer = new Panel
            {
                Name = "modernJointFooter",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Surface,
                Dock = DockStyle.Bottom,
                Height = 92,
                MinimumSize = new Size(0, 92),
                Padding = new Padding(20, 14, 20, 12)
            };
            footer.Paint += ModernWinFormsTheme.DrawTopBorder;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            TableLayoutPanel notes = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(18, 0, 18, 0),
                RowCount = 2
            };
            notes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            notes.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            notes.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            label4.AutoSize = true;
            label4.Dock = DockStyle.Fill;
            ModernWinFormsTheme.SetFont(label4, 8.5F, FontStyle.Regular);
            label4.ForeColor = ModernWinFormsTheme.MutedText;
            label4.Margin = new Padding(0, 0, 0, 2);
            label4.MaximumSize = Size.Empty;
            label27.AutoSize = true;
            label27.Dock = DockStyle.Fill;
            ModernWinFormsTheme.SetFont(label27, 8.5F, FontStyle.Regular);
            label27.ForeColor = ModernWinFormsTheme.Accent;
            label27.Margin = new Padding(0);
            label27.MaximumSize = Size.Empty;
            notes.Controls.Add(label4, 0, 0);
            notes.Controls.Add(label27, 0, 1);

            ConfigureModernFooterButton(
                buttonJointCancel,
                92,
                0,
                new Padding(0));
            ConfigureModernFooterButton(
                buttonJointNext,
                104,
                1,
                new Padding(0));
            layout.Controls.Add(buttonJointCancel, 0, 0);
            layout.Controls.Add(notes, 1, 0);
            layout.Controls.Add(buttonJointNext, 2, 0);
            footer.Controls.Add(layout);
            return footer;
        }

        private Control CreateModernLinkFooter()
        {
            Panel footer = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Surface,
                Dock = DockStyle.Bottom,
                Height = 68,
                MinimumSize = new Size(0, 68),
                Padding = new Padding(20, 14, 20, 12)
            };
            footer.Paint += ModernWinFormsTheme.DrawTopBorder;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            ConfigureModernFooterButton(
                buttonLinksCancel,
                92,
                0,
                new Padding(0));
            ConfigureModernFooterButton(
                buttonLinksPrevious,
                92,
                1,
                new Padding(0, 0, 8, 0));
            modernLinkNextButton = new Button
            {
                Name = "modernLinkNextButton",
                Text = ChineseUiText.Translate(
                    "Next: Model settings",
                    "下一步：模型设置")
            };
            ConfigureModernFooterButton(
                modernLinkNextButton,
                156,
                2,
                new Padding(0));
            modernLinkNextButton.Click += ModernLinkNextClick;

            layout.Controls.Add(buttonLinksCancel, 0, 0);
            layout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 0);
            layout.Controls.Add(buttonLinksPrevious, 2, 0);
            layout.Controls.Add(modernLinkNextButton, 3, 0);
            footer.Controls.Add(layout);
            return footer;
        }

        private Control CreateModernModelFooter()
        {
            Panel footer = new Panel
            {
                Name = "modernModelFooter",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Surface,
                Dock = DockStyle.Bottom,
                Height = 68,
                MinimumSize = new Size(0, 68),
                Padding = new Padding(20, 14, 20, 12)
            };
            footer.Paint += ModernWinFormsTheme.DrawTopBorder;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Name = "modernModelFooterLayout",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 6,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            modernModelCancelButton = new Button
            {
                Name = "modernModelCancelButton",
                Text = buttonLinksCancel.Text
            };
            ConfigureModernFooterButton(
                modernModelCancelButton,
                92,
                0,
                new Padding(0));
            modernModelCancelButton.Click += ButtonLinksCancelClick;
            modernModelPreviousButton = new Button
            {
                Name = "modernModelPreviousButton",
                Text = buttonLinksPrevious.Text
            };
            ConfigureModernFooterButton(
                modernModelPreviousButton,
                92,
                1,
                new Padding(0, 0, 8, 0));
            modernModelPreviousButton.Click += ModernModelPreviousClick;
            modernUsdSettingsButton = new Button
            {
                Name = "modernUsdSettingsButton",
                Enabled = false,
                Text = ChineseUiText.Translate(
                    "OpenUSD settings...",
                    "OpenUSD 设置...")
            };
            ConfigureModernFooterButton(
                modernUsdSettingsButton,
                140,
                2,
                new Padding(0, 0, 8, 0));
            modernUsdSettingsButton.Click += ModernUsdSettingsButtonClick;
            ConfigureModernFooterButton(
                buttonLinksExportUrdfOnly,
                150,
                3,
                new Padding(0, 0, 8, 0));
            ConfigureModernFooterButton(
                buttonLinksFinish,
                176,
                4,
                new Padding(0));

            layout.Controls.Add(modernModelCancelButton, 0, 0);
            layout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 0);
            layout.Controls.Add(modernModelPreviousButton, 2, 0);
            layout.Controls.Add(modernUsdSettingsButton, 3, 0);
            layout.Controls.Add(buttonLinksExportUrdfOnly, 4, 0);
            layout.Controls.Add(buttonLinksFinish, 5, 0);
            footer.Controls.Add(layout);
            return footer;
        }

        private static void ConfigureModernFooterButton(
            Button button,
            int minimumWidth,
            int tabIndex,
            Padding margin)
        {
            button.Anchor = AnchorStyles.None;
            button.AutoSize = false;
            button.Margin = margin;
            button.MinimumSize = new Size(minimumWidth, 36);
            button.Size = new Size(Math.Max(button.Width, minimumWidth), 36);
            button.TabIndex = tabIndex;
        }

        private Control CreateModernTwoColumnFieldCard(
            string name,
            Label title,
            Label required,
            Label[] labels,
            Control[] controls,
            float labelWidth)
        {
            TableLayoutPanel card = ModernWinFormsTheme.CreateCard(name);
            card.Margin = new Padding(0);
            card.Padding = new Padding(16, 10, 16, 12);
            card.Controls.Add(CreateModernCardTitle(title, required));

            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 8, 0, 2),
                RowCount = labels.Length
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int row = 0; row < labels.Length; row++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                AddModernField(grid, labels[row], controls[row], row, 0, 1);
            }
            if (controls.Length > 0)
            {
                controls[controls.Length - 1].Margin = new Padding(0, 4, 0, 6);
                labels[labels.Length - 1].Margin = new Padding(0, 4, 8, 6);
            }
            card.Controls.Add(grid);
            return card;
        }

        private Control CreateModernCardTitle(string title, Label required)
        {
            return CreateModernCardTitle(
                ModernWinFormsTheme.CreateTextLabel(title, 10.5F, FontStyle.Bold),
                required);
        }

        private Control CreateModernCardTitle(Label title, Label required)
        {
            FlowLayoutPanel header = new FlowLayoutPanel
            {
                AutoSize = true,
                BackColor = ModernWinFormsTheme.Surface,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                Padding = new Padding(0),
                WrapContents = false
            };
            title.AutoSize = true;
            ModernWinFormsTheme.SetFont(title, 10.5F, FontStyle.Bold);
            title.ForeColor = ModernWinFormsTheme.Text;
            title.Margin = new Padding(0, 0, 4, 0);
            header.Controls.Add(title);
            if (required != null)
            {
                required.AutoSize = true;
                ModernWinFormsTheme.SetFont(required, 10.5F, FontStyle.Bold);
                required.ForeColor = ModernWinFormsTheme.Accent;
                required.Margin = new Padding(0);
                header.Controls.Add(required);
            }
            return header;
        }

        private static void AddModernField(
            TableLayoutPanel grid,
            Label label,
            Control control,
            int row,
            int labelColumn,
            int controlColumn)
        {
            ModernWinFormsTheme.StyleFieldLabel(label);
            ModernWinFormsTheme.StyleInput(control);
            control.MinimumSize = new Size(control.MinimumSize.Width, 28);
            TextBox textBox = control as TextBox;
            if (textBox != null && !textBox.Multiline)
            {
                // A single-line TextBox otherwise keeps its font-derived auto
                // height while TableLayoutPanel measures the requested 28 px.
                // That mismatch is what lets the last row paint past the grid.
                textBox.AutoSize = false;
                textBox.Height = Math.Max(textBox.Height, 28);
            }
            control.Dock = DockStyle.Fill;
            // Reparented designer controls retain their legacy absolute-page
            // TabIndex values. Derive the order from the new grid so keyboard
            // navigation follows the same left-to-right, top-to-bottom flow.
            control.TabIndex = row * grid.ColumnCount + controlColumn;
            grid.Controls.Add(label, labelColumn, row);
            grid.Controls.Add(control, controlColumn, row);
        }

        private static void AddModernValueRow(
            TableLayoutPanel grid,
            Label leftLabel,
            Control leftControl,
            Label rightLabel,
            Control rightControl,
            int row)
        {
            AddModernField(grid, leftLabel, leftControl, row, 0, 1);
            AddModernField(grid, rightLabel, rightControl, row, 2, 3);
        }

        private static void PrepareModernColumnHeader(Label label)
        {
            label.AutoSize = true;
            label.Dock = DockStyle.Fill;
            ModernWinFormsTheme.SetFont(label, 8.5F, FontStyle.Bold);
            label.ForeColor = ModernWinFormsTheme.MutedText;
            label.Margin = new Padding(0, 0, 0, 4);
            label.TextAlign = ContentAlignment.MiddleLeft;
        }

        private void ApplyModernAssemblySpecificStyles()
        {
            ModernWinFormsTheme.StyleSecondaryButton(buttonUsageGuide);
            ModernWinFormsTheme.StyleSecondaryButton(modernLinkUsageGuideButton);
            ModernWinFormsTheme.StyleSecondaryButton(modernModelUsageGuideButton);
            ModernWinFormsTheme.StyleSecondaryButton(buttonJointCancel);
            ModernWinFormsTheme.StylePrimaryButton(buttonJointNext);
            ModernWinFormsTheme.StyleSecondaryButton(buttonLinksCancel);
            ModernWinFormsTheme.StyleSecondaryButton(buttonLinksPrevious);
            ModernWinFormsTheme.StylePrimaryButton(modernLinkNextButton);
            ModernWinFormsTheme.StyleSecondaryButton(modernModelCancelButton);
            ModernWinFormsTheme.StyleSecondaryButton(modernModelPreviousButton);
            ModernWinFormsTheme.StyleSecondaryButton(modernUsdSettingsButton);
            ModernWinFormsTheme.StyleSecondaryButton(buttonLinksExportUrdfOnly);
            ModernWinFormsTheme.StylePrimaryButton(buttonLinksFinish);
            ResizeButtonToText(buttonJointCancel);
            ResizeButtonToText(buttonJointNext);
            ResizeButtonToText(buttonLinksCancel);
            ResizeButtonToText(buttonLinksPrevious);
            ResizeButtonToText(modernLinkNextButton);
            ResizeButtonToText(modernModelCancelButton);
            ResizeButtonToText(modernModelPreviousButton);
            ResizeButtonToText(modernUsdSettingsButton);
            ResizeButtonToText(buttonLinksExportUrdfOnly);
            ResizeButtonToText(buttonLinksFinish);

            label1.ForeColor = ModernWinFormsTheme.Accent;
            AxisRequiredLabel.ForeColor = ModernWinFormsTheme.Accent;
            LimitRequiredLabel.ForeColor = ModernWinFormsTheme.Accent;
            treeViewJointTree.ShowNodeToolTips = true;
            treeViewLinkProperties.ShowNodeToolTips = true;
        }

        private void WireModernAssemblyLayoutEvents()
        {
            treeViewJointTree.AfterSelect += delegate
            {
                EnsureModernMimicHandler();
            };
            ClientSizeChanged += ModernClientSizeChanged;
        }

        private void ModernClientSizeChanged(object sender, EventArgs e)
        {
            if (modernModelExplicitLayoutSize != Size.Empty &&
                modernModelExplicitLayoutSize != DisplayRectangle.Size)
            {
                RestoreModernModelAutoSizeLayouts();
                modernModelExplicitLayoutSize = Size.Empty;
            }
        }

        private void PrimeModernTabLayoutCaches()
        {
            if (modernJointSections == null)
            {
                return;
            }
            using (ModernWinFormsTheme.SuspendRedraw(modernJointSections))
            {
                PrimeModernPageLayouts();
            }
        }

        private void PrimeModernPageLayouts()
        {
            using (ModernWinFormsTheme.SuspendRedraw(this))
            {
                EnsureModernPageLayout(ModernAssemblyPage.Joint, modernJointRoot);
                EnsureModernPageLayout(ModernAssemblyPage.Link, panelLinkProperties);
                EnsureModernPageLayout(ModernAssemblyPage.Model, modernModelRoot);
            }
        }

        private void ModernLinkNextClick(object sender, EventArgs e)
        {
            LinkNode node = treeViewLinkProperties.SelectedNode as LinkNode;
            if (node != null)
            {
                SaveLinkDataFromPropertyBoxes(node.Link);
            }
            ShowModernAssemblyPage(ModernAssemblyPage.Model);
            Focus();
        }

        private void ModernModelPreviousClick(object sender, EventArgs e)
        {
            ResetLinkPanelScroll();
            ShowModernAssemblyPage(ModernAssemblyPage.Link);
            Focus();
        }

        private void EnsureModernMimicHandler()
        {
            MimicCheckBox.CheckedChanged -= MimicCheckBoxCheckedChanged;
            MimicCheckBox.CheckedChanged -= ModernMimicCheckBoxCheckedChanged;
            MimicCheckBox.CheckedChanged += ModernMimicCheckBoxCheckedChanged;
            SynchronizeModernMimicLayout();
        }

        private void ModernMimicCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            bool showControls = MimicCheckBox.Checked;
            if (!AutoUpdatingForm && showControls &&
                String.IsNullOrWhiteSpace(textBoxMimicMultiplier.Text))
            {
                textBoxMimicMultiplier.Text = "1.0";
            }
            if (!AutoUpdatingForm && showControls &&
                String.IsNullOrWhiteSpace(textBoxMimicOffset.Text))
            {
                textBoxMimicOffset.Text = "0.0";
            }
            SynchronizeModernMimicLayout();
        }

        private void SynchronizeModernMimicLayout()
        {
            if (modernMimicDetails == null)
            {
                return;
            }

            bool show = MimicCheckBox.Checked;
            // Visible includes ancestor visibility; hidden pages must not look dirty.
            if (modernMimicExpanded == show)
            {
                UpdateModernMimicEquationWrapWidth();
                return;
            }

            Control layoutRoot = modernMimicDetails.Parent ?? modernMimicDetails;
            using (ModernWinFormsTheme.SuspendRedraw(layoutRoot))
            {
                layoutRoot.SuspendLayout();
                modernMimicDetails.SuspendLayout();
                try
                {
                    MimicJointLabel.Visible = show;
                    MimicJointComboBox.Visible = show;
                    MimicMultiplierLabel.Visible = show;
                    textBoxMimicMultiplier.Visible = show;
                    MimicOffsetLabel.Visible = show;
                    textBoxMimicOffset.Visible = show;
                    MimicEquationLabel.Visible = show;
                    modernMimicDetails.Visible = show;
                    modernMimicExpanded = show;
                    UpdateModernMimicEquationWrapWidth();
                }
                finally
                {
                    modernMimicDetails.ResumeLayout(false);
                    layoutRoot.ResumeLayout(true);
                }
            }
        }

        private void UpdateModernMimicEquationWrapWidth()
        {
            if (modernMimicDetails == null || MimicEquationLabel == null ||
                modernMimicDetails.ClientSize.Width <= 0)
            {
                return;
            }

            int availableWidth = Math.Max(
                1,
                modernMimicDetails.ClientSize.Width -
                MimicEquationLabel.Margin.Horizontal);
            if (MimicEquationLabel.MaximumSize.Width != availableWidth)
            {
                MimicEquationLabel.MaximumSize = new Size(availableWidth, 0);
            }
            int requiredHeight = TextRenderer.MeasureText(
                MimicEquationLabel.Text ?? String.Empty,
                MimicEquationLabel.Font,
                new Size(availableWidth, Int32.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
            if (MimicEquationLabel.MinimumSize.Height != requiredHeight)
            {
                MimicEquationLabel.MinimumSize = new Size(0, requiredHeight);
            }
        }

        private void ShowModernAssemblyPage(ModernAssemblyPage page)
        {
            Control activePage;
            switch (page)
            {
                case ModernAssemblyPage.Joint:
                    activePage = modernJointRoot;
                    break;
                case ModernAssemblyPage.Link:
                    activePage = panelLinkProperties;
                    break;
                case ModernAssemblyPage.Model:
                    activePage = modernModelRoot;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("page");
            }

            if (modernPageShown && modernActivePage == page && activePage.Visible)
            {
                return;
            }

            SuspendLayout();
            try
            {
                if (!modernPageShown)
                {
                    modernJointRoot.Visible = false;
                    panelLinkProperties.Visible = false;
                    modernModelRoot.Visible = false;
                }
                else
                {
                    Control previousPage;
                    switch (modernActivePage)
                    {
                        case ModernAssemblyPage.Joint:
                            previousPage = modernJointRoot;
                            break;
                        case ModernAssemblyPage.Link:
                            previousPage = panelLinkProperties;
                            break;
                        case ModernAssemblyPage.Model:
                            previousPage = modernModelRoot;
                            break;
                        default:
                            previousPage = null;
                            break;
                    }
                    if (previousPage != null && previousPage != activePage)
                    {
                        previousPage.Visible = false;
                    }
                }

                activePage.Visible = true;
                activePage.BringToFront();
                modernActivePage = page;
                modernPageShown = true;
            }
            finally
            {
                ResumeLayout(false);
            }
            EnsureModernPageLayout(page, activePage);

            if (page == ModernAssemblyPage.Joint)
            {
                EnsureModernMimicHandler();
            }
        }

        private void EnsureModernPageLayout(
            ModernAssemblyPage page,
            Control activePage)
        {
            Rectangle targetBounds = DisplayRectangle;
            if (targetBounds.Width <= 0 || targetBounds.Height <= 0)
            {
                return;
            }

            Size cachedSize;
            switch (page)
            {
                case ModernAssemblyPage.Joint:
                    cachedSize = modernJointExplicitLayoutSize;
                    break;
                case ModernAssemblyPage.Link:
                    cachedSize = modernLinkExplicitLayoutSize;
                    break;
                case ModernAssemblyPage.Model:
                    cachedSize = modernModelExplicitLayoutSize;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("page");
            }

            if (cachedSize == targetBounds.Size && activePage.Bounds == targetBounds)
            {
                return;
            }

            if (page == ModernAssemblyPage.Model)
            {
                RestoreModernModelAutoSizeLayouts();
            }

            activePage.Bounds = targetBounds;
            activePage.PerformLayout();
            switch (page)
            {
                case ModernAssemblyPage.Joint:
                    modernJointExplicitLayoutSize = targetBounds.Size;
                    break;
                case ModernAssemblyPage.Link:
                    modernLinkExplicitLayoutSize = targetBounds.Size;
                    break;
                case ModernAssemblyPage.Model:
                    modernModelExplicitLayoutSize = targetBounds.Size;
                    modernModelExplicitLayoutCount++;
                    FreezeModernModelAutoSizeLayouts(modernModelRoot);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("page");
            }

            if (page == ModernAssemblyPage.Joint)
            {
                modernJointSections.CacheAllPageLayouts();
            }
            else if (page == ModernAssemblyPage.Link)
            {
                modernLinkSections.CacheAllPageLayouts();
            }
        }

        private void SetModernLinkStatusText(Label label, string text)
        {
            if (label == null)
            {
                return;
            }

            string value = text ?? String.Empty;
            if (String.Equals(label.Text, value, StringComparison.Ordinal))
            {
                return;
            }

            if (modernLinkSections != null)
            {
                modernLinkSections.InvalidatePageLayout(label);
            }
            label.Text = value;
            if (modernLinkSections != null)
            {
                modernLinkSections.RebuildPageLayout(label);
            }
        }

        private void InvalidateModernModelPageLayout()
        {
            if (modernModelRoot == null)
            {
                return;
            }

            RestoreModernModelAutoSizeLayouts();
            modernModelExplicitLayoutSize = Size.Empty;
        }

        private void RebuildModernModelPageLayout()
        {
            if (modernModelRoot == null)
            {
                return;
            }

            EnsureModernPageLayout(ModernAssemblyPage.Model, modernModelRoot);
        }

        private void FreezeModernModelAutoSizeLayouts(Control root)
        {
            foreach (Control child in root.Controls)
            {
                FreezeModernModelAutoSizeLayouts(child);
            }
            TableLayoutPanel layout = root as TableLayoutPanel;
            if (layout == null || !layout.AutoSize)
            {
                return;
            }
            Size size = ModernWinFormsTheme.GetStableAutoSizeLayoutSize(layout);
            layout.AutoSize = false;
            layout.Size = size;
            modernModelFrozenLayouts.Add(layout);
        }

        private void RestoreModernModelAutoSizeLayouts()
        {
            for (int index = 0; index < modernModelFrozenLayouts.Count; index++)
            {
                TableLayoutPanel layout = modernModelFrozenLayouts[index];
                if (!layout.IsDisposed)
                {
                    layout.AutoSize = true;
                }
            }
            modernModelFrozenLayouts.Clear();
        }
    }
}
