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
using System.Drawing;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    public partial class AssemblyExportForm
    {
        private bool modernUiInitialized;
        private Panel modernJointRoot;
        private Panel modernJointScrollPanel;
        private Panel modernLinkScrollPanel;
        private TableLayoutPanel modernMimicDetails;
        private Button modernLinkUsageGuideButton;
        private Size modernMinimumSizeAfterInitialScale;
        private Size modernClientSizeAfterInitialScale;
        private CheckBox modernBundleCheckBox;
        private CheckBox modernRos2CheckBox;
        private CheckBox modernRos1CheckBox;
        private CheckBox modernIsaacCheckBox;
        private CheckBox modernIsaacLabCheckBox;
        private TextBox modernPackageVersionTextBox;
        private TextBox modernPackageDescriptionTextBox;
        private TextBox modernMaintainerNameTextBox;
        private TextBox modernMaintainerEmailTextBox;
        private TextBox modernModelLicenseTextBox;
        private TextBox modernModelAuthorTextBox;
        private ComboBox modernRos2PairComboBox;
        private TextBox modernIsaacVersionTextBox;
        private TextBox modernIsaacLabVersionTextBox;
        private TextBox modernRos2ControlProfileTextBox;
        private Button modernRos2ControlProfileButton;
        private TextBox modernIsaacLabProfileTextBox;
        private Button modernIsaacLabProfileButton;

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
                ApplyModernAssemblySpecificStyles();
                WireModernAssemblyLayoutEvents();
                ActivateModernAssemblyPage();
            }
            finally
            {
                ResumeLayout(true);
            }
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
                label7,
                ChineseUiText.Translate(
                    "Define joint identity, reference geometry, motion axis and constraints.",
                    "配置关节标识、参考几何、运动轴及约束参数。"),
                ChineseUiText.Translate("Step 1 of 2 · Joint properties", "第 1/2 步 · Joint 属性"),
                buttonUsageGuide);
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
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320F));
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

            modernJointScrollPanel = new Panel
            {
                Name = "modernJointScrollPanel",
                AutoScroll = true,
                BackColor = ModernWinFormsTheme.Background,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0, 0, 8, 0)
            };
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

            label69.Text = ChineseUiText.Translate(
                "Coordinate systems and axes come from SolidWorks reference geometry. Edit the model to change them.",
                "坐标系与轴来自 SolidWorks 参考几何；如需调整，请返回模型中修改。");
            stack.Controls.Add(CreateModernInfoBanner(label69));
            stack.Controls.Add(CreateModernJointIdentityCard());
            stack.Controls.Add(CreateModernReferenceGeometryCard());
            stack.Controls.Add(CreateModernOriginAndAxisGrid());
            stack.Controls.Add(CreateModernAdvancedJointGrid());
            stack.Controls.Add(CreateModernMimicCard());
            modernJointScrollPanel.Controls.Add(stack);
            body.Controls.Add(modernJointScrollPanel, 1, 0);

            modernJointRoot.Controls.Add(body);
            modernJointRoot.Controls.Add(footer);
            modernJointRoot.Controls.Add(header);
            Controls.Add(modernJointRoot);
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
                    label5,
                    label2.Text,
                    ChineseUiText.Translate("Step 2 of 2 · Link properties", "第 2/2 步 · Link 属性"),
                    modernLinkUsageGuideButton);
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
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320F));
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

                modernLinkScrollPanel = new Panel
                {
                    Name = "modernLinkScrollPanel",
                    AutoScroll = true,
                    BackColor = ModernWinFormsTheme.Background,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    Padding = new Padding(0, 0, 8, 0)
                };
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
                stack.Controls.Add(CreateModernPackageCard());

                label15.Visible = false;
                groupBox5.Text = ChineseUiText.Translate("Inertial properties", "惯性属性");
                groupBox5.Dock = DockStyle.Top;
                groupBox5.Margin = new Padding(0, 0, 0, 12);
                groupBox5.MinimumSize = new Size(590, groupBox5.Height);
                stack.Controls.Add(groupBox5);

                label19.Visible = false;
                groupBox4.Text = ChineseUiText.Translate(
                    "Visual and collision geometry",
                    "可视与碰撞几何");
                groupBox4.Dock = DockStyle.Top;
                groupBox4.Margin = new Padding(0, 0, 0, 12);
                groupBox4.MinimumSize = new Size(590, groupBox4.Height);
                stack.Controls.Add(groupBox4);

                modernLinkScrollPanel.Controls.Add(stack);
                body.Controls.Add(modernLinkScrollPanel, 1, 0);

                panelLinkProperties.Controls.Add(body);
                panelLinkProperties.Controls.Add(footer);
                panelLinkProperties.Controls.Add(header);
            }
            finally
            {
                panelLinkProperties.ResumeLayout(true);
            }
        }

        private Control CreateModernHeader(
            Label titleLabel,
            string subtitle,
            string stepText,
            Button guideButton)
        {
            string controlPrefix = titleLabel == label7
                ? "modernJoint"
                : "modernLink";
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
            Label subtitleLabel = ModernWinFormsTheme.CreateTextLabel(
                subtitle,
                9F,
                FontStyle.Regular);
            subtitleLabel.Name = controlPrefix + "Subtitle";
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
            hintLabel.MaximumSize = new Size(270, 0);

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

        private Control CreateModernJointIdentityCard()
        {
            TableLayoutPanel card = ModernWinFormsTheme.CreateCard("modernJointIdentityCard");
            card.Controls.Add(CreateModernCardTitle(
                ChineseUiText.Translate("Joint identity", "Joint 基本信息"),
                null));

            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 4,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 12, 0, 0),
                RowCount = 2
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            AddModernField(grid, label64, labelParent, 0, 0, 1);
            AddModernField(grid, label65, labelChild, 0, 2, 3);
            AddModernField(grid, label63, textBoxJointName, 1, 0, 1);
            AddModernField(grid, label62, comboBoxJointType, 1, 2, 3);
            ModernWinFormsTheme.StyleReadoutLabel(labelParent);
            ModernWinFormsTheme.StyleReadoutLabel(labelChild);
            card.Controls.Add(grid);
            return card;
        }

        private Control CreateModernReferenceGeometryCard()
        {
            TableLayoutPanel card = ModernWinFormsTheme.CreateCard("modernReferenceGeometryCard");
            card.Controls.Add(CreateModernCardTitle(
                ChineseUiText.Translate("Reference geometry", "参考几何"),
                null));

            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 12, 0, 0),
                RowCount = 2
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AddModernField(grid, label66, comboBoxOrigin, 0, 0, 1);
            AddModernField(grid, label67, comboBoxAxis, 1, 0, 1);
            card.Controls.Add(grid);
            return card;
        }

        private Control CreateModernOriginAndAxisGrid()
        {
            TableLayoutPanel grid = new TableLayoutPanel
            {
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
            originCard.Margin = new Padding(0, 0, 6, 12);
            Control axisCard = CreateModernAxisCard();
            axisCard.Margin = new Padding(6, 0, 0, 12);
            grid.Controls.Add(originCard, 0, 0);
            grid.Controls.Add(axisCard, 1, 0);
            return grid;
        }

        private Control CreateModernOriginCard()
        {
            TableLayoutPanel card = ModernWinFormsTheme.CreateCard("modernOriginCard");
            card.Margin = new Padding(0);
            card.Controls.Add(CreateModernCardTitle(label54, label1));

            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 4,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 12, 0, 0),
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
            card.Controls.Add(CreateModernCardTitle(label60, AxisRequiredLabel));

            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 12, 0, 0),
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
            limits.Margin = new Padding(0, 0, 6, 12);
            Control calibration = CreateModernTwoColumnFieldCard(
                "modernCalibrationCard",
                label74,
                null,
                new Label[] { label7CalibrationRising, label73 },
                new Control[] { textBoxCalibrationRising, textBoxCalibrationFalling },
                112F);
            calibration.Margin = new Padding(6, 0, 0, 12);
            Control dynamics = CreateModernTwoColumnFieldCard(
                "modernDynamicsCard",
                label76,
                null,
                new Label[] { labelFriction, labelDamping },
                new Control[] { textBoxFriction, textBoxDamping },
                168F);
            dynamics.Margin = new Padding(0, 0, 6, 12);
            Control safety = CreateModernTwoColumnFieldCard(
                "modernSafetyCard",
                label80,
                null,
                new Label[] { labelSoftLower, labelSoftUpper, labelKPosition, labelKVelocity },
                new Control[] { textBoxSoftLower, textBoxSoftUpper, textBoxKPosition, textBoxKVelocity },
                152F);
            safety.Margin = new Padding(6, 0, 0, 12);

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

            AddModernField(modernMimicDetails, MimicJointLabel, MimicJointComboBox, 0, 0, 1);
            modernMimicDetails.SetColumnSpan(MimicJointComboBox, 3);
            AddModernField(modernMimicDetails, MimicMultiplierLabel, textBoxMimicMultiplier, 1, 0, 1);
            AddModernField(modernMimicDetails, MimicOffsetLabel, textBoxMimicOffset, 1, 2, 3);
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

        private Control CreateModernPackageCard()
        {
            TableLayoutPanel card = ModernWinFormsTheme.CreateCard("modernPackageCard");
            card.Controls.Add(CreateModernCardTitle(
                ChineseUiText.Translate("Package output", "功能包输出"),
                null));

            FlowLayoutPanel targets = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 10, 0, 8),
                WrapContents = true
            };
            modernBundleCheckBox = CreateTargetCheckBox("Robot Bundle", true);
            modernRos2CheckBox = CreateTargetCheckBox(
                ChineseUiText.Translate("ROS 2 + modern Gazebo", "ROS 2 + 现代 Gazebo"),
                true);
            modernRos1CheckBox = CreateTargetCheckBox("ROS 1 legacy", true);
            modernIsaacCheckBox = CreateTargetCheckBox("Isaac Sim USD profile", false);
            modernIsaacLabCheckBox = CreateTargetCheckBox("Isaac Lab RL profile", false);
            modernBundleCheckBox.Enabled = false;
            modernIsaacCheckBox.CheckedChanged += ModernIsaacSelectionChanged;
            modernRos1CheckBox.CheckedChanged += ModernTargetSelectionChanged;
            modernRos2CheckBox.CheckedChanged += ModernTargetSelectionChanged;
            modernIsaacLabCheckBox.CheckedChanged += ModernTargetSelectionChanged;
            targets.Controls.Add(modernBundleCheckBox);
            targets.Controls.Add(modernRos2CheckBox);
            targets.Controls.Add(modernRos1CheckBox);
            targets.Controls.Add(modernIsaacCheckBox);
            targets.Controls.Add(modernIsaacLabCheckBox);
            card.Controls.Add(targets);

            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 4,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 4, 0, 0),
                RowCount = 9
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
            labelRosPackageNameHint.AutoSize = true;
            labelRosPackageNameHint.Dock = DockStyle.Fill;
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
            AddModernField(grid, CreateTargetLabel("Model author", "模型作者"), modernModelAuthorTextBox, 4, 2, 3);

            modernRos2PairComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 2, 8, 6)
            };
            modernRos2PairComboBox.Items.Add("ROS 2 Lyrical + Gazebo Jetty (recommended)");
            modernRos2PairComboBox.Items.Add("ROS 2 Jazzy + Gazebo Harmonic (compatibility)");
            modernRos2PairComboBox.SelectedIndex = 0;
            AddModernField(
                grid,
                CreateTargetLabel("ROS / Gazebo pair", "ROS / Gazebo 版本组合"),
                modernRos2PairComboBox,
                5,
                0,
                1);
            grid.SetColumnSpan(modernRos2PairComboBox, 3);

            modernIsaacVersionTextBox = CreateTargetTextBox(string.Empty);
            modernIsaacLabVersionTextBox = CreateTargetTextBox(string.Empty);
            AddModernField(grid, CreateTargetLabel("Isaac Sim", "Isaac Sim 版本"), modernIsaacVersionTextBox, 6, 0, 1);
            AddModernField(grid, CreateTargetLabel("Isaac Lab", "Isaac Lab 版本"), modernIsaacLabVersionTextBox, 6, 2, 3);
            modernRos2ControlProfileTextBox = CreateTargetTextBox(string.Empty);
            modernRos2ControlProfileButton = new Button
            {
                AutoSize = true,
                Text = ChineseUiText.Translate("Browse control profile...", "选择 ros2_control 配置...")
            };
            modernRos2ControlProfileButton.Click += ModernRos2ControlProfileBrowseClick;
            FlowLayoutPanel controlProfilePicker = CreateProfilePicker(
                modernRos2ControlProfileTextBox,
                modernRos2ControlProfileButton);
            AddModernField(grid, CreateTargetLabel("ros2_control", "ros2_control 配置"), controlProfilePicker, 7, 0, 1);
            grid.SetColumnSpan(controlProfilePicker, 3);

            modernIsaacLabProfileTextBox = CreateTargetTextBox(string.Empty);
            modernIsaacLabProfileButton = new Button
            {
                AutoSize = true,
                Text = ChineseUiText.Translate("Browse actuator profile...", "选择 actuator 配置...")
            };
            modernIsaacLabProfileButton.Click += ModernIsaacLabProfileBrowseClick;
            FlowLayoutPanel profilePicker = CreateProfilePicker(
                modernIsaacLabProfileTextBox,
                modernIsaacLabProfileButton);
            AddModernField(grid, CreateTargetLabel("Actuators", "Actuator 配置"), profilePicker, 8, 0, 1);
            grid.SetColumnSpan(profilePicker, 3);
            card.Controls.Add(grid);
            SynchronizeIsaacTargetControls();
            return card;
        }

        private static FlowLayoutPanel CreateProfilePicker(TextBox textBox, Button button)
        {
            FlowLayoutPanel picker = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                WrapContents = false
            };
            textBox.Width = 280;
            textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            picker.Controls.Add(textBox);
            picker.Controls.Add(button);
            return picker;
        }

        private CheckBox CreateTargetCheckBox(string text, bool isChecked)
        {
            return new CheckBox
            {
                AutoSize = true,
                Checked = isChecked,
                Margin = new Padding(0, 0, 16, 4),
                Text = text,
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

        private void ModernIsaacSelectionChanged(object sender, EventArgs e)
        {
            if (!modernIsaacCheckBox.Checked)
            {
                modernIsaacLabCheckBox.Checked = false;
            }
            SynchronizeIsaacTargetControls();
            UpdateRosPackageNameHint();
        }

        private void ModernTargetSelectionChanged(object sender, EventArgs e)
        {
            SynchronizeIsaacTargetControls();
            UpdateRosPackageNameHint();
        }

        private void SynchronizeIsaacTargetControls()
        {
            if (modernIsaacCheckBox == null || modernIsaacLabCheckBox == null ||
                modernIsaacVersionTextBox == null || modernIsaacLabVersionTextBox == null ||
                modernIsaacLabProfileTextBox == null || modernIsaacLabProfileButton == null ||
                modernRos2CheckBox == null || modernRos2PairComboBox == null ||
                modernRos2ControlProfileTextBox == null || modernRos2ControlProfileButton == null)
            {
                return;
            }
            modernIsaacLabCheckBox.Enabled = modernIsaacCheckBox.Checked;
            modernIsaacVersionTextBox.Enabled = modernIsaacCheckBox.Checked;
            modernIsaacLabVersionTextBox.Enabled = modernIsaacLabCheckBox.Checked;
            modernIsaacLabProfileTextBox.Enabled = modernIsaacLabCheckBox.Checked;
            modernIsaacLabProfileButton.Enabled = modernIsaacLabCheckBox.Checked;
            modernRos2PairComboBox.Enabled = modernRos2CheckBox.Checked;
            modernRos2ControlProfileTextBox.Enabled = modernRos2CheckBox.Checked;
            modernRos2ControlProfileButton.Enabled = modernRos2CheckBox.Checked;
        }

        private void ModernRos2ControlProfileBrowseClick(object sender, EventArgs e)
        {
            BrowseProfileFile(
                modernRos2ControlProfileTextBox,
                ChineseUiText.Translate("Select a ros2_control profile", "选择 ros2_control 配置"));
        }

        private void ModernIsaacLabProfileBrowseClick(object sender, EventArgs e)
        {
            BrowseProfileFile(
                modernIsaacLabProfileTextBox,
                ChineseUiText.Translate(
                    "Select an Isaac Lab actuator profile",
                    "选择 Isaac Lab actuator 配置"));
        }

        private void BrowseProfileFile(TextBox target, string title)
        {
            using (OpenFileDialog dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = title
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    target.Text = dialog.FileName;
                }
            }
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
                Height = 74,
                MinimumSize = new Size(0, 74),
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
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

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
            ModernWinFormsTheme.SetFont(label4, 8.5F, FontStyle.Regular);
            label4.ForeColor = ModernWinFormsTheme.MutedText;
            label4.Margin = new Padding(0, 0, 0, 2);
            label4.MaximumSize = new Size(720, 0);
            label27.AutoSize = true;
            ModernWinFormsTheme.SetFont(label27, 8.5F, FontStyle.Regular);
            label27.ForeColor = ModernWinFormsTheme.Accent;
            label27.Margin = new Padding(0);
            label27.MaximumSize = new Size(720, 0);
            notes.Controls.Add(label4, 0, 0);
            notes.Controls.Add(label27, 0, 1);

            buttonJointCancel.Size = new Size(92, 36);
            buttonJointCancel.Margin = new Padding(0, 4, 0, 0);
            buttonJointNext.Size = new Size(104, 36);
            buttonJointNext.Margin = new Padding(0, 4, 0, 0);
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
                ColumnCount = 5,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            buttonLinksCancel.Size = new Size(92, 36);
            buttonLinksPrevious.Size = new Size(92, 36);
            buttonLinksExportUrdfOnly.Size = new Size(150, 36);
            buttonLinksFinish.Size = new Size(176, 36);
            buttonLinksPrevious.Margin = new Padding(0, 0, 8, 0);
            buttonLinksExportUrdfOnly.Margin = new Padding(0, 0, 8, 0);

            layout.Controls.Add(buttonLinksCancel, 0, 0);
            layout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 0);
            layout.Controls.Add(buttonLinksPrevious, 2, 0);
            layout.Controls.Add(buttonLinksExportUrdfOnly, 3, 0);
            layout.Controls.Add(buttonLinksFinish, 4, 0);
            footer.Controls.Add(layout);
            return footer;
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
            card.Controls.Add(CreateModernCardTitle(title, required));

            TableLayoutPanel grid = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 12, 0, 0),
                RowCount = labels.Length
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int row = 0; row < labels.Length; row++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                AddModernField(grid, labels[row], controls[row], row, 0, 1);
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
            control.Dock = DockStyle.Fill;
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
            ModernWinFormsTheme.StyleSecondaryButton(buttonJointCancel);
            ModernWinFormsTheme.StylePrimaryButton(buttonJointNext);
            ModernWinFormsTheme.StyleSecondaryButton(buttonLinksCancel);
            ModernWinFormsTheme.StyleSecondaryButton(buttonLinksPrevious);
            ModernWinFormsTheme.StyleSecondaryButton(buttonLinksExportUrdfOnly);
            ModernWinFormsTheme.StylePrimaryButton(buttonLinksFinish);

            label1.ForeColor = ModernWinFormsTheme.Accent;
            AxisRequiredLabel.ForeColor = ModernWinFormsTheme.Accent;
            LimitRequiredLabel.ForeColor = ModernWinFormsTheme.Accent;
            treeViewJointTree.ShowNodeToolTips = true;
            treeViewLinkProperties.ShowNodeToolTips = true;
        }

        private void WireModernAssemblyLayoutEvents()
        {
            panelLinkProperties.VisibleChanged += delegate
            {
                ActivateModernAssemblyPage();
                if (!panelLinkProperties.Visible)
                {
                    EnsureModernMimicHandler();
                }
            };
            treeViewJointTree.AfterSelect += delegate
            {
                EnsureModernMimicHandler();
            };
            EnsureModernMimicHandler();
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
            MimicCheckBox.AutoSize = true;
            MimicCheckBox.Dock = DockStyle.Top;
            MimicCheckBox.Margin = new Padding(0);

            MimicJointLabel.Visible = show;
            MimicJointComboBox.Visible = show;
            MimicMultiplierLabel.Visible = show;
            textBoxMimicMultiplier.Visible = show;
            MimicOffsetLabel.Visible = show;
            textBoxMimicOffset.Visible = show;
            MimicEquationLabel.Visible = show;
            modernMimicDetails.Visible = show;

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
            MimicEquationLabel.MaximumSize = new Size(700, 0);
            MimicEquationLabel.Dock = DockStyle.Fill;

            label4.AutoSize = true;
            label4.MaximumSize = new Size(720, 0);
            label4.Dock = DockStyle.Fill;
            label4.Margin = new Padding(0, 0, 0, 2);
            label27.AutoSize = true;
            label27.MaximumSize = new Size(720, 0);
            label27.Dock = DockStyle.Fill;
            label27.Margin = new Padding(0);

            modernMimicDetails.PerformLayout();
            if (modernMimicDetails.Parent != null)
            {
                modernMimicDetails.Parent.PerformLayout();
            }
            if (modernJointRoot != null)
            {
                modernJointRoot.PerformLayout();
            }
        }

        private void ActivateModernAssemblyPage()
        {
            if (panelLinkProperties.Visible)
            {
                panelLinkProperties.BringToFront();
            }
            else if (modernJointRoot != null)
            {
                modernJointRoot.BringToFront();
            }
        }
    }
}
