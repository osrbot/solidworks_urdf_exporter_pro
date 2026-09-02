using OSURDF.Core.Model;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    internal sealed class OpenUsdJointDescriptor
    {
        internal string Name { get; set; }
        internal string Type { get; set; }
        internal double? EffortLimit { get; set; }
        internal double? VelocityLimit { get; set; }
    }

    internal sealed class OpenUsdSettingsDialog : Form
    {
        private sealed class Choice
        {
            internal Choice(string value, string text)
            {
                Value = value;
                Text = text;
            }

            internal string Value { get; private set; }
            internal string Text { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }

        private readonly ComboBox baseModeComboBox;
        private readonly ComboBox robotTypeComboBox;
        private readonly CheckBox selfCollisionCheckBox;
        private readonly DataGridView jointDriveGrid;
        private readonly Button confirmButton;
        private readonly Button cancelButton;
        private bool loadingSettings;

        internal OpenUsdSettingsDialog()
        {
            Name = "openUsdSettingsDialog";
            Text = ChineseUiText.Translate(
                "OpenUSD simulation settings",
                "OpenUSD 仿真设置");
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(900, 560);
            MinimumSize = new Size(900, 500);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = ModernWinFormsTheme.Background,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(18)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            TableLayoutPanel general = new ModernCardPanel
            {
                Name = "openUsdGeneralSettings",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Surface,
                ColumnCount = 4,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(14, 12, 14, 12),
                RowCount = 3
            };
            general.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            general.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            Label title = ModernWinFormsTheme.CreateTextLabel(
                ChineseUiText.Translate(
                    "Asset-level physics intent",
                    "资产级物理意图"),
                10F,
                FontStyle.Bold);
            title.Margin = new Padding(0, 0, 0, 8);
            general.Controls.Add(title, 0, 0);
            general.SetColumnSpan(title, 4);

            baseModeComboBox = CreateChoiceComboBox("openUsdBaseModeComboBox");
            AddChoices(
                baseModeComboBox,
                new Choice("source", ChineseUiText.Translate("Keep source semantics", "保持源语义")),
                new Choice("fixed", ChineseUiText.Translate("Fixed base", "固定基座")),
                new Choice("floating", ChineseUiText.Translate("Floating base", "浮动基座")));
            robotTypeComboBox = CreateChoiceComboBox("openUsdRobotTypeComboBox");
            AddChoices(
                robotTypeComboBox,
                new Choice("default", ChineseUiText.Translate("Default", "默认")),
                new Choice("manipulator", ChineseUiText.Translate("Manipulator", "机械臂")),
                new Choice("wheeled", ChineseUiText.Translate("Wheeled", "轮式机器人")),
                new Choice("quadruped", ChineseUiText.Translate("Quadruped", "四足机器人")),
                new Choice("humanoid", ChineseUiText.Translate("Humanoid", "人形机器人")),
                new Choice("aerial", ChineseUiText.Translate("Aerial", "飞行机器人")),
                new Choice("mobile_manipulator", ChineseUiText.Translate("Mobile manipulator", "移动机械臂")),
                new Choice("end_effector", ChineseUiText.Translate("End effector", "末端执行器")),
                new Choice("holonomic", ChineseUiText.Translate("Holonomic mobile", "全向移动机器人")));

            general.Controls.Add(CreateLabel("Base mode", "基座模式"), 0, 1);
            general.Controls.Add(baseModeComboBox, 1, 1);
            general.Controls.Add(CreateLabel("Robot type", "机器人类型"), 2, 1);
            general.Controls.Add(robotTypeComboBox, 3, 1);

            selfCollisionCheckBox = new CheckBox
            {
                Name = "openUsdSelfCollisionCheckBox",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0),
                Text = ChineseUiText.Translate(
                    "Allow self-collision (off by default)",
                    "允许自碰撞（默认关闭）")
            };
            general.Controls.Add(selfCollisionCheckBox, 1, 2);
            general.SetColumnSpan(selfCollisionCheckBox, 3);
            root.Controls.Add(general, 0, 0);

            TableLayoutPanel driveCard = new ModernCardPanel
            {
                Name = "openUsdJointDriveSettings",
                BackColor = ModernWinFormsTheme.Surface,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(14, 12, 14, 14),
                RowCount = 3
            };
            driveCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            driveCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            driveCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            driveCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label driveTitle = ModernWinFormsTheme.CreateTextLabel(
                ChineseUiText.Translate("Single-DOF Joint intent", "单自由度 Joint 驱动意图"),
                10F,
                FontStyle.Bold);
            driveTitle.Margin = new Padding(0, 0, 0, 4);
            driveCard.Controls.Add(driveTitle, 0, 0);
            Label driveHint = ModernWinFormsTheme.CreateTextLabel(
                ChineseUiText.Translate(
                    "Passive is the safe default. Position and velocity author DriveAPI; effort records runtime intent only. CAD limits are read-only.",
                    "被动模式是安全默认值。位置和速度会写入 DriveAPI；effort 仅记录运行时控制意图。CAD 限值只读。"),
                8.5F,
                FontStyle.Regular);
            driveHint.ForeColor = ModernWinFormsTheme.MutedText;
            driveHint.Margin = new Padding(0, 0, 0, 8);
            driveCard.Controls.Add(driveHint, 0, 1);

            jointDriveGrid = CreateJointDriveGrid();
            driveCard.Controls.Add(jointDriveGrid, 0, 2);
            root.Controls.Add(driveCard, 0, 1);

            FlowLayoutPanel footer = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 14, 0, 0),
                WrapContents = false
            };
            confirmButton = new Button
            {
                Name = "openUsdConfirmButton",
                DialogResult = DialogResult.None,
                Size = new Size(110, 34),
                Margin = new Padding(8, 0, 0, 0),
                Text = ChineseUiText.Translate("Apply", "应用")
            };
            cancelButton = new Button
            {
                Name = "openUsdCancelButton",
                DialogResult = DialogResult.Cancel,
                Size = new Size(110, 34),
                Margin = new Padding(0),
                Text = ChineseUiText.Translate("Cancel", "取消")
            };
            confirmButton.Click += ConfirmButtonClick;
            footer.Controls.Add(confirmButton);
            footer.Controls.Add(cancelButton);
            root.Controls.Add(footer, 0, 2);

            Controls.Add(root);
            AcceptButton = confirmButton;
            CancelButton = cancelButton;
            ModernWinFormsTheme.Apply(this);
            ModernWinFormsTheme.StylePrimaryButton(confirmButton);
            Settings = new UsdSimulationProfile();
        }

        internal UsdSimulationProfile Settings { get; private set; }

        internal void LoadSettings(
            UsdSimulationProfile settings,
            IEnumerable<OpenUsdJointDescriptor> joints)
        {
            loadingSettings = true;
            jointDriveGrid.SuspendLayout();
            try
            {
                Settings = ExportTargetOptions.CloneUsdSimulation(settings);
                SelectChoice(baseModeComboBox, Settings.BaseMode, "source");
                SelectChoice(robotTypeComboBox, Settings.RobotType, "default");
                selfCollisionCheckBox.Checked = Settings.AllowSelfCollision;
                Dictionary<string, UsdJointDriveProfile> configured =
                    (Settings.JointDrives ?? new List<UsdJointDriveProfile>())
                        .Where(item => item != null && !String.IsNullOrWhiteSpace(item.Joint))
                        .GroupBy(item => item.Joint, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

                List<DataGridViewRow> rows = new List<DataGridViewRow>();
                foreach (OpenUsdJointDescriptor joint in
                    joints ?? Enumerable.Empty<OpenUsdJointDescriptor>())
                {
                    UsdJointDriveProfile drive;
                    configured.TryGetValue(joint.Name ?? String.Empty, out drive);
                    DataGridViewRow row = new DataGridViewRow();
                    row.CreateCells(
                        jointDriveGrid,
                        joint.Name,
                        joint.Type,
                        DriveModeDisplay(drive == null ? "passive" : drive.Mode),
                        FormatOptional(drive == null ? null : drive.Stiffness),
                        FormatOptional(drive == null ? null : drive.Damping),
                        FormatOptional(joint.EffortLimit),
                        FormatOptional(joint.VelocityLimit));
                    rows.Add(row);
                }
                jointDriveGrid.Rows.Clear();
                if (rows.Count > 0)
                {
                    jointDriveGrid.Rows.AddRange(rows.ToArray());
                }
                foreach (DataGridViewRow row in jointDriveGrid.Rows)
                {
                    UpdateGainCellState(row);
                }
            }
            finally
            {
                loadingSettings = false;
                jointDriveGrid.ResumeLayout(false);
            }
            DialogResult = DialogResult.None;
        }

        internal bool TryCaptureSettings(out UsdSimulationProfile settings)
        {
            settings = new UsdSimulationProfile
            {
                BaseMode = SelectedValue(baseModeComboBox, "source"),
                RobotType = SelectedValue(robotTypeComboBox, "default"),
                AllowSelfCollision = selfCollisionCheckBox.Checked
            };
            foreach (DataGridViewRow row in jointDriveGrid.Rows)
            {
                string mode = DriveModeValue(Convert.ToString(
                    row.Cells["driveModeColumn"].Value,
                    CultureInfo.CurrentCulture));
                if (mode == "passive")
                {
                    continue;
                }
                double? stiffness = null;
                double? damping = null;
                bool hasActiveDrive = mode == "position" || mode == "velocity";
                if (hasActiveDrive &&
                    (!TryReadGain(row, "stiffnessColumn", out stiffness) ||
                     !TryReadGain(row, "dampingColumn", out damping)))
                {
                    return false;
                }
                settings.JointDrives.Add(new UsdJointDriveProfile
                {
                    Joint = Convert.ToString(
                        row.Cells["jointNameColumn"].Value,
                        CultureInfo.CurrentCulture),
                    Mode = mode,
                    Stiffness = stiffness,
                    Damping = damping
                });
            }
            return true;
        }

        private void ConfirmButtonClick(object sender, EventArgs e)
        {
            UsdSimulationProfile settings;
            if (!TryCaptureSettings(out settings))
            {
                MessageBox.Show(
                    this,
                    ChineseUiText.Translate(
                        "Stiffness and damping must be blank or finite non-negative numbers.",
                        "刚度和阻尼必须留空，或填写有限的非负数。"),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            Settings = settings;
            DialogResult = DialogResult.OK;
        }

        private DataGridView CreateJointDriveGrid()
        {
            DataGridView grid = new DataGridView
            {
                Name = "openUsdJointDriveGrid",
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                BackgroundColor = ModernWinFormsTheme.Surface,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                Dock = DockStyle.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter,
                EnableHeadersVisualStyles = false,
                MultiSelect = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = ModernWinFormsTheme.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ModernWinFormsTheme.Text;
            grid.ColumnHeadersHeight = 30;
            grid.RowTemplate.Height = 28;
            grid.Columns.Add(CreateTextColumn(
                "jointNameColumn", ChineseUiText.Translate("Joint", "关节"), 180, 150, true));
            grid.Columns.Add(CreateTextColumn(
                "jointTypeColumn", ChineseUiText.Translate("Type", "类型"), 90, 75, true));
            DataGridViewComboBoxColumn mode = new DataGridViewComboBoxColumn
            {
                Name = "driveModeColumn",
                HeaderText = ChineseUiText.Translate("Intent", "驱动意图"),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FlatStyle = FlatStyle.Flat,
                FillWeight = 145F,
                MinimumWidth = 125
            };
            mode.Items.AddRange(DriveModeDisplays());
            grid.Columns.Add(mode);
            grid.Columns.Add(CreateTextColumn(
                "stiffnessColumn", ChineseUiText.Translate("Stiffness", "刚度"), 95, 80, false));
            grid.Columns.Add(CreateTextColumn(
                "dampingColumn", ChineseUiText.Translate("Damping", "阻尼"), 95, 80, false));
            grid.Columns.Add(CreateTextColumn(
                "effortLimitColumn", ChineseUiText.Translate("Effort limit", "力矩/力限值"), 105, 90, true));
            grid.Columns.Add(CreateTextColumn(
                "velocityLimitColumn", ChineseUiText.Translate("Velocity limit", "速度限值"), 105, 90, true));
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty)
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (!loadingSettings && args.RowIndex >= 0 &&
                    args.ColumnIndex == grid.Columns["driveModeColumn"].Index)
                {
                    UpdateGainCellState(grid.Rows[args.RowIndex]);
                }
            };
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs args)
            {
                args.ThrowException = false;
            };
            return grid;
        }

        private static DataGridViewTextBoxColumn CreateTextColumn(
            string name,
            string header,
            int fillWeight,
            int minimumWidth,
            bool readOnly)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = fillWeight,
                MinimumWidth = minimumWidth,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private static ComboBox CreateChoiceComboBox(string name)
        {
            return new ComboBox
            {
                Name = name,
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 2, 12, 6)
            };
        }

        private static Label CreateLabel(string english, string chinese)
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

        private static void AddChoices(ComboBox comboBox, params Choice[] choices)
        {
            comboBox.Items.AddRange(choices.Cast<object>().ToArray());
        }

        private static void SelectChoice(ComboBox comboBox, string value, string fallback)
        {
            int fallbackIndex = -1;
            for (int index = 0; index < comboBox.Items.Count; index++)
            {
                Choice choice = comboBox.Items[index] as Choice;
                if (choice != null && String.Equals(choice.Value, value, StringComparison.Ordinal))
                {
                    comboBox.SelectedIndex = index;
                    return;
                }
                if (choice != null &&
                    String.Equals(choice.Value, fallback, StringComparison.Ordinal))
                {
                    fallbackIndex = index;
                }
            }
            comboBox.SelectedIndex = fallbackIndex >= 0
                ? fallbackIndex
                : (comboBox.Items.Count > 0 ? 0 : -1);
        }

        private static string SelectedValue(ComboBox comboBox, string fallback)
        {
            Choice choice = comboBox.SelectedItem as Choice;
            return choice == null ? fallback : choice.Value;
        }

        private void UpdateGainCellState(DataGridViewRow row)
        {
            string mode = DriveModeValue(Convert.ToString(
                row.Cells["driveModeColumn"].Value,
                CultureInfo.CurrentCulture));
            bool editable = mode == "position" || mode == "velocity";
            SetCellEditable(row.Cells["stiffnessColumn"], editable);
            SetCellEditable(row.Cells["dampingColumn"], editable);
        }

        private static void SetCellEditable(DataGridViewCell cell, bool editable)
        {
            cell.ReadOnly = !editable;
            cell.Style.BackColor = editable
                ? ModernWinFormsTheme.Surface
                : ModernWinFormsTheme.SurfaceAlt;
        }

        private static string[] DriveModeDisplays()
        {
            return new[]
            {
                DriveModeDisplay("passive"),
                DriveModeDisplay("position"),
                DriveModeDisplay("velocity"),
                DriveModeDisplay("effort")
            };
        }

        private static string DriveModeDisplay(string mode)
        {
            if (String.Equals(mode, "position", StringComparison.Ordinal))
            {
                return ChineseUiText.Translate("position / Position drive", "position / 位置驱动");
            }
            if (String.Equals(mode, "velocity", StringComparison.Ordinal))
            {
                return ChineseUiText.Translate("velocity / Velocity drive", "velocity / 速度驱动");
            }
            if (String.Equals(mode, "effort", StringComparison.Ordinal))
            {
                return ChineseUiText.Translate("effort / Effort intent", "effort / 力矩或力意图");
            }
            return ChineseUiText.Translate("passive / No active drive", "passive / 被动（无主动驱动）");
        }

        private static string DriveModeValue(string display)
        {
            foreach (string value in new[] { "position", "velocity", "effort", "passive" })
            {
                if ((display ?? String.Empty).StartsWith(value, StringComparison.Ordinal))
                {
                    return value;
                }
            }
            return "passive";
        }

        private static string FormatOptional(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("G17", CultureInfo.InvariantCulture)
                : String.Empty;
        }

        private static bool TryReadGain(
            DataGridViewRow row,
            string columnName,
            out double? value)
        {
            string text = Convert.ToString(
                row.Cells[columnName].Value,
                CultureInfo.CurrentCulture).Trim();
            if (text.Length == 0)
            {
                value = null;
                row.Cells[columnName].ErrorText = String.Empty;
                return true;
            }
            double parsed;
            bool valid =
                (Double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed) ||
                 Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) &&
                !Double.IsNaN(parsed) &&
                !Double.IsInfinity(parsed) &&
                parsed >= 0.0;
            row.Cells[columnName].ErrorText = valid
                ? String.Empty
                : ChineseUiText.Translate("Enter a finite non-negative number.", "请输入有限的非负数。");
            value = valid ? (double?)parsed : null;
            return valid;
        }
    }
}
