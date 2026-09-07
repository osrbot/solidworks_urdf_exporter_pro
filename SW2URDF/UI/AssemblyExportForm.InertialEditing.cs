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
                Text = ChineseUiText.Translate("Clear Link calibration and edits", "清除 Link 校准与手动修改"),
                Margin = new Padding(0), Padding = new Padding(8, 2, 8, 2)
            };
            inertialInputErrors = new ErrorProvider { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.NeverBlink };
            inertialEditingToolTip = new ToolTip();
            inertialEditingToolTip.SetToolTip(buttonResetInertia, ChineseUiText.Translate(
                "Discard this Link's mass, COM and inertia edits and read SolidWorks effective properties. SolidWorks properties are not modified.",
                "清除此 Link 的质量、质心和惯性修改，重新读取 SolidWorks 有效属性；不会修改 SolidWorks 的质量属性。"));
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
                checkBoxCalibrateInertia.Enabled = available &&
                    (InertialEditingPolicy.CanEnableCalibration(link) || Exporter != null);
                checkBoxCalibrateInertia.Checked = available && InertialEditingPolicy.CanCalibrate(link) && !state.CalibrationDisabled;
                buttonResetInertia.Enabled = available;
                inertialEditingToolTip.SetToolTip(checkBoxCalibrateInertia,
                    available && InertialEditingPolicy.NeedsCalibrationConfirmation(link)
                    ? ChineseUiText.Translate("Enable to use SolidWorks inertia as the calibration source, keeping the entered Link mass. Replacing explicit inertia requires confirmation. SolidWorks properties are not modified.",
                        "勾选后使用 SolidWorks 惯性作为校准基准，保留输入的 Link 质量；替换已指定的惯性需确认，不修改 SolidWorks 属性。")
                    : ChineseUiText.Translate("Keep the source mass distribution; scale the full tensor by measured/source mass. COM and equivalent cuboid dimensions stay unchanged.",
                        "仅校准此 Link：惯性矩阵按实测质量/源质量同比缩放；保留质量分布，等效长方体尺寸不变，不修改 SolidWorks 属性。"));
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
            try
            {
                TryChangeInertiaCalibration(node.Link, enabled,
                    () => MessageBox.Show(this, ChineseUiText.Translate(
                        "Use SolidWorks inertia as the calibration source? This replaces the current Link inertia tensor and keeps the entered mass and COM. SolidWorks properties will not be modified.",
                        "使用 SolidWorks 惯性作为校准基准？这会替换当前 Link 的惯性矩阵，保留输入的质量和质心，不会修改 SolidWorks 的质量属性。"),
                        ChineseUiText.Translate("Calibrate Link inertia", "校准 Link 惯性"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes,
                    link => Exporter.ComputeInertialProperties(link));
                FillEffectiveInertialInputs(node.Link);
                RefreshEditedInertiaPreview();
            }
            catch (Exception error)
            {
                UpdateInertialEditingControls(node.Link);
                MessageBox.Show(this, error.Message, ChineseUiText.Translate("Calibrate Link inertia", "校准 Link 惯性"));
            }
        }

        internal static bool TryChangeInertiaCalibration(Link link, bool enabled,
            Func<bool> confirmReplacement, Action<Link> readSource)
        {
            // Read into a detached draft so a failed read cannot partially replace user edits.
            var candidate = link.Clone();
            if (enabled && InertialEditingPolicy.NeedsCalibrationConfirmation(candidate) && !confirmReplacement())
                return false;
            if (enabled && !InertialEditingPolicy.CanEnableCalibration(candidate))
            {
                var state = InertialEditingPolicy.EnsureSource(candidate);
                state.TensorEdited = state.LegacyValuesPreserved = false;
                state.MassEdited = true;
                state.CalibrationDisabled = true;
                readSource(candidate);
            }
            InertialEditingPolicy.SetCalibration(candidate, enabled);
            link.Inertial.SetElement(candidate.Inertial);
            link.InertialEditing = candidate.InertialEditing;
            return true;
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
                MessageBox.Show(this, error.Message, ChineseUiText.Translate("Clear Link calibration and edits", "清除 Link 校准与手动修改"));
            }
        }
    }
}
