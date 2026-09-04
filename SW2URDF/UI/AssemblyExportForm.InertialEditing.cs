using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SW2URDF.URDF;
using SW2URDF.URDFExport;

namespace SW2URDF.UI
{
    public partial class AssemblyExportForm
    {
        private CheckBox checkBoxCalibrateInertia;
        private Button buttonResetInertia;
        private ErrorProvider inertialInputErrors;
        private ToolTip inertialEditingToolTip;
        private bool updatingInertialInputs;
        private bool refreshInertiaAfterEdit;

        private Control InitializeInertialEditingControls()
        {
            var row = new FlowLayoutPanel
            {
                Name = "inertialEditingActions", AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                WrapContents = true, Margin = new Padding(0, 4, 0, 4)
            };
            checkBoxCalibrateInertia = new CheckBox
            {
                Name = "checkBoxCalibrateInertia", AutoSize = true,
                Text = ChineseUiText.Translate("Calibrate inertia with measured mass", "按实测质量校准惯性"),
                Margin = new Padding(0, 6, 12, 6)
            };
            buttonResetInertia = new Button
            {
                Name = "buttonResetInertia", AutoSize = true,
                Text = ChineseUiText.Translate("Restore SW values", "恢复 SW 值"),
                Margin = new Padding(0), Padding = new Padding(8, 2, 8, 2)
            };
            inertialInputErrors = new ErrorProvider { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.NeverBlink };
            inertialEditingToolTip = new ToolTip();
            row.Controls.Add(checkBoxCalibrateInertia);
            row.Controls.Add(buttonResetInertia);
            checkBoxCalibrateInertia.CheckedChanged += CalibrateInertiaCheckedChanged;
            buttonResetInertia.Click += ResetInertiaClick;
            foreach (var input in InertialInputs())
            {
                input.TextChanged += InertialInputTextChanged;
                input.Leave += InertialInputLeave;
            }
            row.Disposed += (sender, args) =>
            {
                inertialInputErrors.Dispose();
                inertialEditingToolTip.Dispose();
            };
            return row;
        }

        private TextBox[] InertialInputs()
        {
            return new[] { textBoxMass, textBoxInertialOriginX, textBoxInertialOriginY,
                textBoxInertialOriginZ, textBoxInertialOriginRoll, textBoxInertialOriginPitch,
                textBoxInertialOriginYaw, textBoxIxx, textBoxIxy, textBoxIxz, textBoxIyy,
                textBoxIyz, textBoxIzz };
        }

        private bool CommitInertialInputs(Link link)
        {
            if (link == null || link.isFixedFrame || updatingInertialInputs) return false;
            TextBox[] inputs = InertialInputs();
            var values = new double[inputs.Length];
            bool valid = true;
            for (int index = 0; index < inputs.Length; index++)
            {
                bool parsed = Double.TryParse(inputs[index].Text, URDFAttribute.URDFNumberStyle,
                    URDFAttribute.URDFNumberFormat, out values[index]) &&
                    !Double.IsInfinity(values[index]) && !Double.IsNaN(values[index]) &&
                    (index != 0 || values[index] > 0);
                if (!parsed)
                {
                    valid = false;
                    values[index] = Double.NaN;
                }
                inertialInputErrors?.SetError(inputs[index], parsed ? "" : ChineseUiText.Translate(
                    index == 0 ? "Enter a positive mass in kg." : "Enter a finite number.",
                    index == 0 ? "请输入大于零的质量，单位 kg。" : "请输入有效数值。"));
            }
            var edited = InertialEditingPolicy.Copy(link.Inertial);
            edited.Mass.Value = values[0];
            edited.Origin.SetXYZ(values.Skip(1).Take(3).ToArray());
            edited.Origin.SetRPY(values.Skip(4).Take(3).ToArray());
            edited.Inertia.SetUrdfMomentMatrix(new[] { values[7], values[8], values[9],
                values[8], values[10], values[11], values[9], values[11], values[12] });
            InertialEditingPolicy.ApplyEdits(link, edited);
            // Invalid edits stay invalid in the model, rather than exporting the previous value.
            if (valid) FillEffectiveInertialInputs(link);
            else RefreshValidTensorInputs(link);
            return valid;
        }

        private void RefreshValidTensorInputs(Link link)
        {
            // A bad COM/angle must not leave a newly calibrated tensor stale on screen.
            // Keep invalid text editable; synchronize every finite, resolved tensor entry.
            updatingInertialInputs = true;
            try
            {
                var boxes = new[] { textBoxIxx, textBoxIxy, textBoxIxz, textBoxIyy, textBoxIyz, textBoxIzz };
                var tensor = link.Inertial.Inertia;
                var values = new[] { tensor.Ixx, tensor.Ixy, tensor.Ixz, tensor.Iyy, tensor.Iyz, tensor.Izz };
                for (int i = 0; i < boxes.Length; i++)
                    if (!Double.IsNaN(values[i]) && !Double.IsInfinity(values[i]))
                        boxes[i].Text = values[i].ToString(InertiaDisplayFormat, URDFAttribute.URDFNumberFormat);
                UpdateInertiaMatrixMirrorBoxes();
                UpdateInertialEditingControls(link);
            }
            finally { updatingInertialInputs = false; }
        }

        private void FillEffectiveInertialInputs(Link link)
        {
            updatingInertialInputs = true;
            try
            {
                link.Inertial.Mass.FillBoxes(textBoxMass, InertiaDisplayFormat);
                link.Inertial.Origin.FillBoxes(textBoxInertialOriginX, textBoxInertialOriginY,
                    textBoxInertialOriginZ, textBoxInertialOriginRoll, textBoxInertialOriginPitch,
                    textBoxInertialOriginYaw, InertiaDisplayFormat);
                link.Inertial.Inertia.FillBoxes(textBoxIxx, textBoxIxy, textBoxIxz,
                    textBoxIyy, textBoxIyz, textBoxIzz, InertiaDisplayFormat);
                UpdateInertiaMatrixMirrorBoxes();
                UpdateInertialEditingControls(link);
            }
            finally { updatingInertialInputs = false; }
        }

        private void UpdateInertialEditingControls(Link link)
        {
            if (checkBoxCalibrateInertia == null) return;
            bool previous = updatingInertialInputs;
            updatingInertialInputs = true;
            try
            {
                bool available = link != null && !link.isFixedFrame;
                var state = available ? InertialEditingPolicy.EnsureSource(link) : null;
                checkBoxCalibrateInertia.Enabled = available && InertialEditingPolicy.CanCalibrate(link);
                checkBoxCalibrateInertia.Checked = checkBoxCalibrateInertia.Enabled && !state.CalibrationDisabled;
                buttonResetInertia.Enabled = available;
                inertialEditingToolTip.SetToolTip(checkBoxCalibrateInertia,
                    state != null && state.LegacyValuesPreserved
                    ? ChineseUiText.Translate("This configuration did not record the inertia source. Existing values are preserved. Restore SW values before using automatic calibration.",
                        "旧配置未记录惯性来源，现保留原数值。点击恢复 SW 值后，可使用自动质量校准。")
                    : state != null && (state.TensorEdited || state.SourceHasInertiaOverride)
                    ? ChineseUiText.Translate("Explicit inertia is preserved. Restore SW values to discard manual edits.",
                        "保留已指定的惯性矩阵，不自动缩放。恢复 SW 值可撤销插件中的手动修改。")
                    : ChineseUiText.Translate("Keep the source mass distribution; scale the full tensor by measured/source mass. COM and equivalent cuboid dimensions stay unchanged.",
                        "保留原质量分布，完整惯性矩阵按实测质量/原质量同比缩放，质心和等效长方体尺寸不变。"));
            }
            finally { updatingInertialInputs = previous; }
        }

        private void InertialInputTextChanged(object sender, EventArgs args)
        {
            if (AutoUpdatingForm || updatingInertialInputs) return;
            if (inertiaPreview != null && inertiaPreview.IsVisible)
            {
                refreshInertiaAfterEdit = true;
                ClearInertiaPreview();
            }
        }

        private void InertialInputLeave(object sender, EventArgs args)
        {
            if (AutoUpdatingForm || updatingInertialInputs) return;
            var node = treeViewLinkProperties.SelectedNode as LinkNode;
            if (node != null && CommitInertialInputs(node.Link)) RefreshEditedInertiaPreview();
        }

        private void RefreshEditedInertiaPreview()
        {
            if (!refreshInertiaAfterEdit || inertiaPreview == null) return;
            refreshInertiaAfterEdit = false;
            ClearInertiaPreview();
            ButtonShowInertiaPreviewClick(this, EventArgs.Empty);
        }

        private void CalibrateInertiaCheckedChanged(object sender, EventArgs args)
        {
            if (updatingInertialInputs || AutoUpdatingForm) return;
            var node = treeViewLinkProperties.SelectedNode as LinkNode;
            if (node == null) return;
            bool enabled = checkBoxCalibrateInertia.Checked;
            if (!CommitInertialInputs(node.Link)) return;
            refreshInertiaAfterEdit |= inertiaPreview != null && inertiaPreview.IsVisible;
            InertialEditingPolicy.SetCalibration(node.Link, enabled);
            FillEffectiveInertialInputs(node.Link);
            RefreshEditedInertiaPreview();
        }

        private void ResetInertiaClick(object sender, EventArgs args)
        {
            var node = treeViewLinkProperties.SelectedNode as LinkNode;
            if (node == null || Exporter == null) return;
            var snapshot = node.Link.Clone();
            try
            {
                refreshInertiaAfterEdit |= inertiaPreview != null && inertiaPreview.IsVisible;
                node.Link.InertialEditing = new InertialEditingState
                    { Source = InertialEditingPolicy.Copy(node.Link.Inertial) };
                Exporter.ComputeInertialProperties(node.Link);
                inertialInputErrors.Clear();
                FillEffectiveInertialInputs(node.Link);
                RefreshEditedInertiaPreview();
            }
            catch (Exception error)
            {
                node.Link.SetElement(snapshot);
                MessageBox.Show(this, error.Message, ChineseUiText.Translate("Restore SW values", "恢复 SW 值"));
            }
        }
    }
}
