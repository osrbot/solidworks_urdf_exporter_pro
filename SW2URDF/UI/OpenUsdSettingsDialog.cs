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
        internal bool IsMimic { get; set; }
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
        private readonly DataGridView jointIntentGrid;
        private readonly DataGridView mjcfDriveGrid;
        private readonly TabControl targetTabs;
        private readonly Button confirmButton;
        private readonly Button cancelButton;
        private bool loadingSettings;
        private string appliedSettingsKey;
        private readonly Dictionary<string, string[]> usdGainDrafts = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> mjcfGainDrafts = new Dictionary<string, string[]>(StringComparer.Ordinal);

        internal OpenUsdSettingsDialog()
        {
            SuspendLayout();
            Name = "openUsdSettingsDialog";
            Text = ChineseUiText.Translate(
                "Simulation settings",
                "仿真设置");
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(960, 720);
            MinimumSize = new Size(900, 500);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            TableLayoutPanel root = new TableLayoutPanel
            {
                Name = "openUsdRoot",
                AutoScroll = true,
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

            TableLayoutPanel general = new TableLayoutPanel
            {
                Name = "openUsdGeneralSettings",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = ModernWinFormsTheme.Surface,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(14, 12, 14, 12),
                RowCount = 2
            };
            general.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            Label title = ModernWinFormsTheme.CreateTextLabel(
                ChineseUiText.Translate(
                    "Base settings",
                    "基座设置"),
                10F,
                FontStyle.Bold);
            title.Margin = new Padding(0, 0, 0, 8);
            general.Controls.Add(title, 0, 0);
            general.SetColumnSpan(title, 2);

            baseModeComboBox = CreateChoiceComboBox("openUsdBaseModeComboBox");
            AddChoices(
                baseModeComboBox,
                new Choice("source", ChineseUiText.Translate("Keep existing target behavior", "保持各目标原有行为")),
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

            selfCollisionCheckBox = new CheckBox
            {
                Name = "openUsdSelfCollisionCheckBox",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0),
                Text = ChineseUiText.Translate(
                    "Allow self-collision (off by default)",
                    "允许自碰撞（默认关闭）")
            };
            root.Controls.Add(general, 0, 0);

            TableLayoutPanel driveCard = new TableLayoutPanel
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
            driveCard.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
            driveCard.RowStyles.Add(new RowStyle(SizeType.Percent, 62F));
            Label driveTitle = ModernWinFormsTheme.CreateTextLabel(
                ChineseUiText.Translate("Joint control", "关节控制"),
                10F,
                FontStyle.Bold);
            driveTitle.Margin = new Padding(0, 0, 0, 4);
            driveCard.Controls.Add(driveTitle, 0, 0);
            jointIntentGrid = CreateJointDriveGrid();
            jointIntentGrid.Name = "simulationJointIntentGrid";
            jointIntentGrid.Columns["stiffnessColumn"].Visible = false;
            jointIntentGrid.Columns["dampingColumn"].Visible = false;
            driveCard.Controls.Add(jointIntentGrid, 0, 1);
            jointDriveGrid = CreateJointDriveGrid();
            mjcfDriveGrid = CreateJointDriveGrid();
            mjcfDriveGrid.Name = "mjcfJointDriveGrid";
            mjcfDriveGrid.Columns.Add(CreateTextColumn(
                "maxForceColumn", ChineseUiText.Translate("Max force (N*m / N)", "最大力矩/力 (N*m / N)"), 130, 115, false));
            foreach (DataGridView grid in new[] { jointDriveGrid, mjcfDriveGrid })
            {
                grid.Columns["driveModeColumn"].Visible = false;
                grid.Columns["effortLimitColumn"].Visible = false;
                grid.Columns["velocityLimitColumn"].Visible = false;
            }
            targetTabs = new ModernTabControl { Name = "simulationTargetTabs", Dock = DockStyle.Fill };
            TabPage usdPage = CreateTargetPage("OpenUSD", jointDriveGrid,
                "SI gains: rotary N*m/rad, N*m*s/rad; linear N/m, N*s/m. Effort: runtime intent only, no active USD drive.",
                "SI 增益：旋转 N*m/rad、N*m*s/rad；直线 N/m、N*s/m。effort 仅为运行时意图，不创建 USD 主动驱动。");
            TableLayoutPanel usdOptions = new TableLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2
            };
            usdOptions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            usdOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            usdOptions.Controls.Add(CreateLabel("Robot type", "机器人类型"), 0, 0);
            usdOptions.Controls.Add(robotTypeComboBox, 1, 0);
            usdOptions.Controls.Add(selfCollisionCheckBox, 1, 1);
            TableLayoutPanel usdLayout = (TableLayoutPanel)usdPage.Controls[0];
            usdLayout.Controls.Add(usdOptions, 0, 0);
            targetTabs.TabPages.Add(usdPage);
            targetTabs.TabPages.Add(CreateTargetPage("MuJoCo / MJCF", mjcfDriveGrid,
                "SI gains: rotary N*m/rad, N*m*s/rad; linear N/m, N*s/m. Actuator gear = 1; max force in N*m / N.",
                "SI 增益：旋转 N*m/rad、N*m*s/rad；直线 N/m、N*s/m。执行器 gear = 1；最大力矩/力 N*m / N。"));
            driveCard.Controls.Add(targetTabs, 0, 2);
            root.Controls.Add(driveCard, 0, 1);

            FlowLayoutPanel footer = new FlowLayoutPanel
            {
                Name = "openUsdFooter",
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
            ResumeLayout(true);
        }

        internal UsdSimulationProfile Settings { get; private set; }
        internal SimulationProfile SimulationSettings { get; private set; }

        private static TabPage CreateTargetPage(string title, DataGridView grid, string english, string chinese)
        {
            TabPage page = new TabPage(title) { Padding = new Padding(8), AutoScroll = true };
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label units = CreateLabel(english, chinese);
            units.ForeColor = ModernWinFormsTheme.MutedText;
            layout.Controls.Add(units, 0, 1);
            layout.Controls.Add(grid, 0, 2);
            page.Controls.Add(layout);
            return page;
        }

        internal void PrepareForOwner(Control owner)
        {
            Screen screen = owner == null
                ? Screen.FromControl(this)
                : Screen.FromControl(owner);
            ConstrainToWorkingArea(screen.WorkingArea);
        }

        internal void ConstrainToWorkingArea(Rectangle workingArea)
        {
            if (workingArea.Width <= 0 || workingArea.Height <= 0)
            {
                return;
            }

            Size requestedMinimum = MinimumSize;
            MinimumSize = Size.Empty;
            // Bound the outer window directly; scaled non-client metrics can change after a handle is created.
            Size = new Size(Math.Min(Width, workingArea.Width), Math.Min(Height, workingArea.Height));
            MinimumSize = new Size(
                Math.Min(requestedMinimum.Width, workingArea.Width),
                Math.Min(requestedMinimum.Height, workingArea.Height));

            int left = Math.Max(
                workingArea.Left,
                Math.Min(Left, workingArea.Right - Width));
            int top = Math.Max(
                workingArea.Top,
                Math.Min(Top, workingArea.Bottom - Height));
            Location = new Point(left, top);
            PerformLayout();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (Visible)
            {
                Screen screen = Owner == null
                    ? Screen.FromControl(this)
                    : Screen.FromControl(Owner);
                ConstrainToWorkingArea(screen.WorkingArea);
            }
            base.OnVisibleChanged(e);
        }

        internal void LoadSettings(
            UsdSimulationProfile settings,
            IEnumerable<OpenUsdJointDescriptor> joints,
            SimulationProfile simulation = null)
        {
            loadingSettings = true;
            jointDriveGrid.SuspendLayout();
            try
            {
                bool restoreDrafts = appliedSettingsKey != null && appliedSettingsKey == SettingsKey(settings, simulation);
                Settings = ExportTargetOptions.CloneUsdSimulation(settings);
                SimulationSettings = ExportTargetOptions.CloneSimulation(simulation);
                SelectChoice(baseModeComboBox, simulation == null ? "source" : simulation.BaseMode, "source");
                SelectChoice(robotTypeComboBox, Settings.RobotType, "default");
                selfCollisionCheckBox.Checked = Settings.AllowSelfCollision;
                Dictionary<string, UsdJointDriveProfile> configured =
                    (Settings.JointDrives ?? new List<UsdJointDriveProfile>())
                        .Where(item => item != null && !String.IsNullOrWhiteSpace(item.Joint))
                        .GroupBy(item => item.Joint, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

                Dictionary<string, JointDriveIntent> intents =
                    (simulation == null ? new List<JointDriveIntent>() : simulation.JointDrives ?? new List<JointDriveIntent>())
                        .Where(item => item != null && !String.IsNullOrWhiteSpace(item.Joint))
                        .GroupBy(item => item.Joint, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                Dictionary<string, MjcfJointDriveProfile> mjcf =
                    (simulation == null || simulation.Mjcf == null ? new List<MjcfJointDriveProfile>() :
                        simulation.Mjcf.JointDrives ?? new List<MjcfJointDriveProfile>())
                        .Where(item => item != null && !String.IsNullOrWhiteSpace(item.Joint))
                        .GroupBy(item => item.Joint, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                jointDriveGrid.Rows.Clear();
                jointIntentGrid.Rows.Clear();
                mjcfDriveGrid.Rows.Clear();
                foreach (OpenUsdJointDescriptor joint in
                    (joints ?? Enumerable.Empty<OpenUsdJointDescriptor>())
                        .Where(item => item != null && !String.IsNullOrWhiteSpace(item.Name))
                        .GroupBy(item => item.Name, StringComparer.Ordinal).Select(group => group.First()))
                {
                    UsdJointDriveProfile drive;
                    configured.TryGetValue(joint.Name ?? String.Empty, out drive);
                    JointDriveIntent intent;
                    intents.TryGetValue(joint.Name, out intent);
                    MjcfJointDriveProfile mjcfDrive;
                    mjcf.TryGetValue(joint.Name, out mjcfDrive);
                    string mode = !CanDrive(joint) ? "passive" : intent != null ? intent.Mode :
                        simulation == null && drive != null ? drive.Mode : "passive";
                    foreach (DataGridView grid in new[] { jointIntentGrid, jointDriveGrid, mjcfDriveGrid })
                    {
                        bool isMjcf = grid == mjcfDriveGrid;
                        int index = grid.Rows.Add(joint.Name, joint.Type, DriveModeDisplay(mode),
                            FormatOptional(isMjcf ? (mjcfDrive == null ? null : mjcfDrive.Stiffness) : (drive == null ? null : drive.Stiffness)),
                            FormatOptional(isMjcf ? (mjcfDrive == null ? null : mjcfDrive.Damping) : (drive == null ? null : drive.Damping)),
                            FormatOptional(joint.EffortLimit), FormatOptional(joint.VelocityLimit));
                        DataGridViewRow row = grid.Rows[index];
                        row.Tag = joint;
                        if (isMjcf)
                        {
                            row.Cells["maxForceColumn"].Value = FormatOptional(mjcfDrive == null ? null : mjcfDrive.MaxForce);
                        }
                        string[] draft;
                        if (restoreDrafts && grid != jointIntentGrid &&
                            (isMjcf ? mjcfGainDrafts : usdGainDrafts).TryGetValue(joint.Name, out draft))
                        {
                            row.Cells["stiffnessColumn"].Value = draft[0];
                            row.Cells["dampingColumn"].Value = draft[1];
                            if (isMjcf) row.Cells["maxForceColumn"].Value = draft[2];
                        }
                        SetCellEditable(row.Cells["driveModeColumn"], grid == jointIntentGrid && CanDrive(joint));
                        UpdateGainCellState(row);
                    }
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
            SimulationProfile simulation;
            return TryCaptureSettings(out settings, out simulation);
        }

        internal bool TryCaptureSettings(out UsdSimulationProfile settings, out SimulationProfile simulation)
        {
            jointIntentGrid.EndEdit();
            jointDriveGrid.EndEdit();
            mjcfDriveGrid.EndEdit();
            simulation = new SimulationProfile
            {
                BaseMode = SelectedValue(baseModeComboBox, "source"),
                JointDrives = new List<JointDriveIntent>(),
                Mjcf = new MjcfSimulationProfile { JointDrives = new List<MjcfJointDriveProfile>() }
            };
            settings = new UsdSimulationProfile
            {
                // A shared source choice must not rewrite the legacy target's base behavior.
                BaseMode = Settings.BaseMode,
                RobotType = SelectedValue(robotTypeComboBox, "default"),
                AllowSelfCollision = selfCollisionCheckBox.Checked,
                GainUnits = Settings.GainUnits
            };
            bool valid = true;
            foreach (DataGridViewRow row in jointDriveGrid.Rows)
            {
                string mode = DriveModeValue(Convert.ToString(
                    row.Cells["driveModeColumn"].Value,
                    CultureInfo.CurrentCulture));
                if (!CanDrive(row.Tag as OpenUsdJointDescriptor)) mode = "passive";
                string jointName = Convert.ToString(row.Cells["jointNameColumn"].Value, CultureInfo.CurrentCulture);
                simulation.JointDrives.Add(new JointDriveIntent { Joint = jointName, Mode = mode });
                if (mode == "passive")
                {
                    ClearGainErrors(row);
                    continue;
                }
                double? stiffness = null;
                double? damping = null;
                bool rowValid = true;
                if (mode == "position")
                {
                    bool stiffnessValid = TryReadGain(
                        row,
                        "stiffnessColumn",
                        out stiffness, true);
                    bool dampingValid = TryReadGain(
                        row,
                        "dampingColumn",
                        out damping);
                    rowValid = stiffnessValid && dampingValid;
                }
                else if (mode == "velocity")
                {
                    DataGridViewCell stiffnessCell = row.Cells["stiffnessColumn"];
                    stiffnessCell.ErrorText = String.Empty;
                    stiffness = 0.0;
                    rowValid = TryReadGain(
                        row,
                        "dampingColumn",
                        out damping, true);
                }
                else
                {
                    ClearGainErrors(row);
                }
                valid &= rowValid;
                if (!rowValid)
                {
                    continue;
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
            foreach (DataGridViewRow row in mjcfDriveGrid.Rows)
            {
                string mode = DriveModeValue(Convert.ToString(row.Cells["driveModeColumn"].Value));
                if (!CanDrive(row.Tag as OpenUsdJointDescriptor) || mode == "passive")
                {
                    ClearGainErrors(row);
                    row.Cells["maxForceColumn"].ErrorText = String.Empty;
                    continue;
                }
                double? stiffness = null;
                double? damping = null;
                double? maxForce;
                bool rowValid = true;
                ClearGainErrors(row);
                if (mode == "position")
                {
                    rowValid &= TryReadGain(row, "stiffnessColumn", out stiffness, true);
                    rowValid &= TryReadGain(row, "dampingColumn", out damping);
                }
                else if (mode == "velocity")
                {
                    stiffness = 0.0;
                    rowValid &= TryReadGain(row, "dampingColumn", out damping, true);
                }
                rowValid &= TryReadGain(row, "maxForceColumn", out maxForce, true);
                valid &= rowValid;
                if (rowValid)
                {
                    simulation.Mjcf.JointDrives.Add(new MjcfJointDriveProfile
                    {
                        Joint = Convert.ToString(row.Cells["jointNameColumn"].Value),
                        Stiffness = stiffness, Damping = damping, MaxForce = maxForce
                    });
                }
            }
            return valid;
        }

        private void ConfirmButtonClick(object sender, EventArgs e)
        {
            UsdSimulationProfile settings;
            SimulationProfile simulation;
            if (!TryCaptureSettings(out settings, out simulation))
            {
                targetTabs.SelectedIndex = jointDriveGrid.Rows.Cast<DataGridViewRow>().Any(
                    row => row.Cells.Cast<DataGridViewCell>().Any(cell => !String.IsNullOrEmpty(cell.ErrorText))) ? 0 : 1;
                MessageBox.Show(
                    this,
                    ChineseUiText.Translate(
                        "Use blank or finite SI values. Position stiffness, velocity damping and max force must be positive; position damping may be zero. Check both target tabs.",
                        "可留空或填写有限 SI 数值。位置刚度、速度阻尼及最大力矩/力须大于零；位置阻尼可为零。请检查两个目标页。"),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            Settings = settings;
            SimulationSettings = simulation;
            appliedSettingsKey = SettingsKey(settings, simulation);
            RememberGainDrafts(jointDriveGrid, usdGainDrafts);
            RememberGainDrafts(mjcfDriveGrid, mjcfGainDrafts);
            DialogResult = DialogResult.OK;
        }

        private static string SettingsKey(UsdSimulationProfile usd, SimulationProfile simulation)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(new object[] { usd, simulation });
        }

        private static void RememberGainDrafts(DataGridView grid, Dictionary<string, string[]> drafts)
        {
            drafts.Clear();
            foreach (DataGridViewRow row in grid.Rows)
            {
                drafts[Convert.ToString(row.Cells["jointNameColumn"].Value)] = new[]
                {
                    Convert.ToString(row.Cells["stiffnessColumn"].Value),
                    Convert.ToString(row.Cells["dampingColumn"].Value),
                    grid.Columns.Contains("maxForceColumn") ? Convert.ToString(row.Cells["maxForceColumn"].Value) : null
                };
            }
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
                "stiffnessColumn", ChineseUiText.Translate("Stiffness (SI)", "刚度 (SI)"), 120, 105, false));
            grid.Columns.Add(CreateTextColumn(
                "dampingColumn", ChineseUiText.Translate("Damping (SI)", "阻尼 (SI)"), 120, 105, false));
            grid.Columns.Add(CreateTextColumn(
                "effortLimitColumn", ChineseUiText.Translate("Effort (N*m / N)", "力矩/力 (N*m / N)"), 140, 125, true));
            grid.Columns.Add(CreateTextColumn(
                "velocityLimitColumn", ChineseUiText.Translate("Speed (rad/s / m/s)", "速度 (rad/s / m/s)"), 145, 130, true));
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
                    SynchronizeDriveMode(grid.Rows[args.RowIndex]);
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
            bool active = CanDrive(row.Tag as OpenUsdJointDescriptor);
            bool position = active && mode == "position";
            bool velocity = active && mode == "velocity";
            DataGridViewCell stiffness = row.Cells["stiffnessColumn"];
            DataGridViewCell damping = row.Cells["dampingColumn"];
            SetCellEditable(stiffness, position);
            SetCellEditable(damping, position || velocity);
            if (row.DataGridView.Columns.Contains("maxForceColumn"))
            {
                SetCellEditable(row.Cells["maxForceColumn"], active && mode != "passive");
            }
            OpenUsdJointDescriptor joint = row.Tag as OpenUsdJointDescriptor;
            bool linear = joint != null && joint.Type == "prismatic";
            stiffness.ToolTipText = linear ? "N/m" : "N*m/rad";
            damping.ToolTipText = linear ? "N*s/m" : "N*m*s/rad";
            row.Cells["driveModeColumn"].ToolTipText = joint != null && joint.IsMimic
                ? ChineseUiText.Translate("Mimic joint: no active drive", "Mimic 关节：无主动驱动") : String.Empty;
        }

        private static bool CanDrive(OpenUsdJointDescriptor joint)
        {
            return joint != null && !joint.IsMimic &&
                (joint.Type == "revolute" || joint.Type == "continuous" || joint.Type == "prismatic");
        }

        private void SynchronizeDriveMode(DataGridViewRow source)
        {
            string name = Convert.ToString(source.Cells["jointNameColumn"].Value);
            string mode = CanDrive(source.Tag as OpenUsdJointDescriptor)
                ? DriveModeValue(Convert.ToString(source.Cells["driveModeColumn"].Value)) : "passive";
            loadingSettings = true;
            try
            {
                foreach (DataGridView grid in new[] { jointIntentGrid, jointDriveGrid, mjcfDriveGrid })
                {
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        if (String.Equals(name, Convert.ToString(row.Cells["jointNameColumn"].Value), StringComparison.Ordinal))
                        {
                            row.Cells["driveModeColumn"].Value = DriveModeDisplay(mode);
                            UpdateGainCellState(row);
                        }
                    }
                }
            }
            finally
            {
                loadingSettings = false;
            }
        }

        private static void SetCellEditable(DataGridViewCell cell, bool editable)
        {
            cell.ReadOnly = !editable;
            if (!editable)
            {
                cell.ErrorText = String.Empty;
            }
            cell.Style.BackColor = editable
                ? ModernWinFormsTheme.Surface
                : ModernWinFormsTheme.SurfaceAlt;
        }

        private static void ClearGainErrors(DataGridViewRow row)
        {
            row.Cells["stiffnessColumn"].ErrorText = String.Empty;
            row.Cells["dampingColumn"].ErrorText = String.Empty;
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
            return ChineseUiText.Translate("passive / Passive", "passive / 被动");
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
            out double? value,
            bool positive = false)
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
                (positive ? parsed > 0.0 : parsed >= 0.0);
            row.Cells[columnName].ErrorText = valid
                ? String.Empty
                : positive
                    ? ChineseUiText.Translate("Enter a finite positive number.", "请输入有限的正数。")
                    : ChineseUiText.Translate("Enter a finite non-negative number.", "请输入有限的非负数。");
            value = valid ? (double?)parsed : null;
            return valid;
        }
    }
}
