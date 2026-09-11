using SW2URDF.URDFExport;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    internal sealed class LegacyConfigurationMigrationDialog : Form
    {
        private readonly DataGridView grid;
        private readonly Button apply;
        private readonly LegacyConfigurationMigration plan;

        internal LegacyConfigurationMigrationDialog(LegacyConfigurationMigration plan)
        {
            this.plan = plan;
            Text = ChineseUiText.Translate("Migrate export configuration", "迁移旧版导出配置");
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(940, 540);
            MinimumSize = new Size(740, 420);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.Controls.Add(new Label
            {
                AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12),
                Text = string.Format(ChineseUiText.Translate(
                    "{0} links found. Connections, names and component/reference bindings will be restored. Review missing matches below.\r\nMass, inertia, poses and meshes will be recalculated; old manual values and mesh settings will be reset. Joint design settings are retained.\r\nThe old configuration is retained. Nothing is written until you save the export configuration.",
                    "已读取 {0} 个 Link，将恢复连接、命名及组件和参考几何绑定。请补选下方未匹配项。\r\n质量、惯量、位姿及网格将重新计算，旧手填值和网格设置将重置；关节设计参数保留。\r\n旧配置会保留；只有正式保存导出配置时，才会写入新版配置。"), plan.LinkCount)
            }, 0, 0);
            grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, AutoGenerateColumns = false, BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Link", ReadOnly = true, FillWeight = 22 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = ChineseUiText.Translate("Kind", "类型"), ReadOnly = true, FillWeight = 12 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = ChineseUiText.Translate("Old reference", "原引用"), ReadOnly = true, FillWeight = 26 });
            grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = ChineseUiText.Translate("Reference in this assembly", "当前装配体中的对象"), FillWeight = 40 });
            foreach (var item in plan.References)
            {
                int index = grid.Rows.Add(item.LinkName,
                    item.Kind == ReferenceGeometryKind.Axis ? ChineseUiText.Translate("Axis", "轴") : ChineseUiText.Translate("Frame", "坐标系"),
                    item.LegacyName);
                var cell = (DataGridViewComboBoxCell)grid.Rows[index].Cells[3];
                foreach (var choice in item.Choices)
                    cell.Items.Add(choice);
                cell.Value = item.Selected;
                cell.ToolTipText = item.Selected == null ? ChineseUiText.Translate("Select a reference", "请选择引用对象") : item.Selected.DisplayLabel;
                grid.Rows[index].Tag = item;
            }
            grid.CurrentCellDirtyStateChanged += (sender, args) =>
            {
                if (grid.IsCurrentCellDirty)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += (sender, args) =>
            {
                if (args.RowIndex < 0 || args.ColumnIndex != 3)
                    return;
                var row = grid.Rows[args.RowIndex];
                ((LegacyReferenceSelection)row.Tag).Selected = row.Cells[3].Value as ReferenceGeometryEntry;
                apply.Enabled = plan.IsResolved;
            };
            layout.Controls.Add(grid, 0, 1);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            apply = new Button { Text = ChineseUiText.Translate("Migrate", "确认迁移"), Width = 120, Height = 34, Enabled = plan.IsResolved };
            apply.Click += (sender, args) =>
            {
                grid.EndEdit();
                if (plan.IsResolved)
                    DialogResult = DialogResult.OK;
            };
            var cancel = new Button { Text = ChineseUiText.Translate("Cancel", "取消"), DialogResult = DialogResult.Cancel, Width = 96, Height = 34 };
            footer.Controls.Add(apply);
            footer.Controls.Add(cancel);
            layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);
            CancelButton = cancel;
            ModernWinFormsTheme.Apply(this);
            ModernWinFormsTheme.StylePrimaryButton(apply);
        }
    }
}
