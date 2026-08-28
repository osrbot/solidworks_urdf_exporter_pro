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
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DrawingColor = System.Drawing.Color;

namespace SW2URDF.UI
{
    public partial class AssemblyExportForm : Form
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        public ExportHelper Exporter;
        public bool AutoUpdatingForm;

        private readonly SldWorks swApp;
        private readonly ModelDoc2 ActiveSWModel;
        private LinkNode previouslySelectedNode;
        private readonly Control[] jointBoxes;
        private readonly Control[] linkBoxes;
        private readonly LinkNode BaseNode;
        private readonly InertiaPreview inertiaPreview;
        private readonly CollisionPreview collisionPreview;
        private bool updatingMaterialColorControls;
        private bool meshReductionRatioEdited;
        private double meshReductionRatioForExport;
        private bool enableLayoutFixes;
        private bool applyingLayoutFixes;
        private string displayedJointType;
        private bool jointUnitInputsResetForCurrentChange;
        private readonly Dictionary<string, Size> buttonDesignSizes;
        private readonly IExportSessionDraftStore exportSessionDraftStore;
        private readonly TreeSelectionUpdateGuard treeSelectionUpdateGuard;
        private Button buttonUsageGuide;
        private Label labelLinkCoordinateSystem;
        private ComboBox comboBoxLinkCoordinateSystem;
        private ToolTip packagePathToolTip;
        private bool suppressRecoveryDraftOnClose;
        private Button buttonShowCollisionPreview;
        private Label labelCollisionPreviewStatus;
        private bool collisionPreviewEnabled;
        private bool ownedResourcesDisposed;

        private AssemblyExportForm()
        {
            exportSessionDraftStore = new FileExportSessionDraftStore();
            treeSelectionUpdateGuard = new TreeSelectionUpdateGuard();
            InitializeComponent();
            ChineseUiText.Apply(this);
            InitializeLinkCoordinateSystemControls();
            InitializeUsageGuideButton();
            InitializeCommonMaterialNames();
            buttonDesignSizes = CaptureButtonDesignSizes();
            enableLayoutFixes = true;
            ApplyHighDpiLayoutFixes();
            InitializeCollisionStrategyComboBox();
            InitializeCollisionPreviewControls();
            textBoxIxy.TextChanged += InertiaMatrixOffDiagonalTextChanged;
            textBoxIxz.TextChanged += InertiaMatrixOffDiagonalTextChanged;
            textBoxIyz.TextChanged += InertiaMatrixOffDiagonalTextChanged;
            comboBoxJointType.TextChanged += ComboBoxJointTypeTextChanged;
            UpdateInertiaMatrixMirrorBoxes();
        }

        private void InitializeLinkCoordinateSystemControls()
        {
            Control[] existingContentControls = groupBox5.Controls
                .Cast<Control>()
                .Where(control => control != label15)
                .ToArray();

            labelLinkCoordinateSystem = new Label
            {
                Name = "labelLinkCoordinateSystem",
                AutoSize = true,
                Text = ChineseUiText.Translate("Link frame", "Link 坐标系")
            };
            comboBoxLinkCoordinateSystem = new ComboBox
            {
                Name = "comboBoxLinkCoordinateSystem",
                DropDownStyle = ComboBoxStyle.DropDownList,
                Size = new Size(groupBox5.ClientSize.Width - 120, 21),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                TabIndex = 2
            };
            int frameRowTop = Math.Max(16, label15.Bottom + 4);
            comboBoxLinkCoordinateSystem.Location = new Point(110, frameRowTop);
            labelLinkCoordinateSystem.Location = new Point(
                8,
                frameRowTop + Math.Max(
                    0,
                    (comboBoxLinkCoordinateSystem.Height -
                        labelLinkCoordinateSystem.PreferredHeight) / 2));
            comboBoxLinkCoordinateSystem.SelectionChangeCommitted +=
                LinkCoordinateSystemSelectionChangeCommitted;
            groupBox5.Controls.Add(labelLinkCoordinateSystem);
            groupBox5.Controls.Add(comboBoxLinkCoordinateSystem);

            const int minimumRowGap = 4;
            int firstContentTop = existingContentControls.Min(control => control.Top);
            int contentOffset = Math.Max(
                0,
                comboBoxLinkCoordinateSystem.Bottom + minimumRowGap - firstContentTop);
            foreach (Control control in existingContentControls)
            {
                control.Top += contentOffset;
            }

            groupBox5.Height += contentOffset;
            groupBox4.Top = groupBox5.Bottom + 3;

            if (components == null)
            {
                components = new System.ComponentModel.Container();
            }
            packagePathToolTip = new ToolTip(components);
            labelRosPackageNameHint.AutoSize = false;
            labelRosPackageNameHint.AutoEllipsis = true;
        }

        private void InitializeUsageGuideButton()
        {
            buttonUsageGuide = new Button
            {
                Name = "buttonUsageGuide",
                Text = ChineseUiText.Translate("Guide", "使用说明"),
                UseVisualStyleBackColor = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TabIndex = 300
            };
            buttonUsageGuide.Font = Font;
            buttonUsageGuide.Click += ButtonUsageGuideClick;
            Controls.Add(buttonUsageGuide);
        }

        private void InitializeCommonMaterialNames()
        {
            foreach (string materialName in UsageGuideForm.CommonMaterialNames)
            {
                if (!comboBoxMaterials.Items.Contains(materialName))
                {
                    comboBoxMaterials.Items.Add(materialName);
                }
            }
            label34.Text = ChineseUiText.Translate(
                "Texture image (optional)",
                "纹理图片（可选）");
            label28.Text = ChineseUiText.Translate(
                "Appearance preset / URDF material ID",
                "外观预设 / URDF 材质 ID");
            label29.Text = ChineseUiText.Translate(
                "Appearance color (RGBA)",
                "外观颜色（RGBA）");
            packagePathToolTip.SetToolTip(
                comboBoxMaterials,
                ChineseUiText.Translate(
                    "SolidWorks appearance is loaded first. Choosing a built-in preset applies its RGBA, clears the old texture, and uses the same text as the URDF material ID; typing a custom ID does not change color.",
                    "首次读取 SolidWorks 外观。选择内置预设会同步应用 RGBA、清除旧纹理，并将同名文本作为 URDF 材质 ID；手输自定义 ID 不会改色。"));
            packagePathToolTip.SetToolTip(
                textBoxTexture,
                ChineseUiText.Translate(
                    "Only an existing image file is exported. STL has no UV coordinates; use 3DXML when appearance fidelity matters.",
                    "仅会导出实际存在的图片文件。STL 不含 UV 坐标，重视外观时请使用 3DXML。"));
            comboBoxMaterials.SelectionChangeCommitted +=
                MaterialPresetSelectionChangeCommitted;
        }

        private void MaterialPresetSelectionChangeCommitted(object sender, EventArgs e)
        {
            if (!MaterialAppearancePresets.TryGet(comboBoxMaterials.Text, out double[] rgba))
            {
                return;
            }

            updatingMaterialColorControls = true;
            try
            {
                domainUpDownRed.Text = rgba[0].ToString("G5", URDFAttribute.URDFNumberFormat);
                domainUpDownGreen.Text = rgba[1].ToString("G5", URDFAttribute.URDFNumberFormat);
                domainUpDownBlue.Text = rgba[2].ToString("G5", URDFAttribute.URDFNumberFormat);
                domainUpDownAlpha.Text = rgba[3].ToString("G5", URDFAttribute.URDFNumberFormat);
            }
            finally
            {
                updatingMaterialColorControls = false;
            }
            textBoxTexture.Text = String.Empty;
            UpdateMaterialColorPreview();
        }

        private void InitializeCollisionPreviewControls()
        {
            buttonShowCollisionPreview = new Button
            {
                Name = "buttonShowCollisionPreview",
                Location = new Point(comboBoxCollisionStrategy.Left, comboBoxCollisionStrategy.Bottom + 7),
                Size = new Size(comboBoxCollisionStrategy.Width, 24),
                Text = ChineseUiText.Translate("Preview collision", "预览碰撞体"),
                UseVisualStyleBackColor = true,
                TabIndex = comboBoxCollisionStrategy.TabIndex + 1
            };
            labelCollisionPreviewStatus = new Label
            {
                Name = "labelCollisionPreviewStatus",
                AutoEllipsis = true,
                Location = new Point(
                    comboBoxCollisionStrategy.Left,
                    buttonShowCollisionPreview.Bottom + 4),
                Size = new Size(comboBoxCollisionStrategy.Width, 38),
                Text = ChineseUiText.Translate(
                    "Overlay is not displayed",
                    "未显示碰撞体叠加层")
            };
            packagePathToolTip.SetToolTip(
                buttonShowCollisionPreview,
                ChineseUiText.Translate(
                    "Show this collision strategy over the SolidWorks geometry. The equivalent inertia cuboid can remain visible for comparison.",
                    "在 SolidWorks 几何体上叠加当前碰撞策略；可同时保留惯性等效长方体进行对照。"));
            packagePathToolTip.SetToolTip(
                comboBoxCollisionStrategy,
                ChineseUiText.Translate(
                    "Changing the strategy refreshes an active collision preview immediately.",
                    "碰撞预览开启时，切换策略会立即刷新叠加层。"));
            buttonShowCollisionPreview.Click += ButtonShowCollisionPreviewClick;
            comboBoxCollisionStrategy.SelectionChangeCommitted +=
                CollisionStrategySelectionChangeCommitted;
            groupBox4.Controls.Add(buttonShowCollisionPreview);
            groupBox4.Controls.Add(labelCollisionPreviewStatus);
            buttonShowCollisionPreview.BringToFront();
            labelCollisionPreviewStatus.BringToFront();
        }

        private void ButtonUsageGuideClick(object sender, EventArgs e)
        {
            using (UsageGuideForm guideForm = new UsageGuideForm())
            {
                guideForm.ShowDialog(this);
            }
        }

        public AssemblyExportForm(SldWorks SwApp, LinkNode node, ExportHelper exporter)
            : this()
        {
            Application.ThreadException +=
                new ThreadExceptionEventHandler(ExceptionHandler);
            AppDomain.CurrentDomain.UnhandledException +=
                new UnhandledExceptionEventHandler(UnhandledException);
            swApp = SwApp;
            BaseNode = node;
            ActiveSWModel = swApp.ActiveDoc;
            inertiaPreview = new InertiaPreview(swApp, ActiveSWModel);
            Exporter = exporter;
            collisionPreview = new CollisionPreview(swApp, ActiveSWModel, Exporter);
            AutoUpdatingForm = false;
            FormClosing += AssemblyExportFormClosing;
            FormClosed += AssemblyExportFormClosed;

            jointBoxes = new Control[] {
                textBoxJointName, comboBoxAxis, comboBoxJointType,
                textBoxAxisX, textBoxAxisY, textBoxAxisZ,
                textBoxJointX, textBoxJointY, textBoxJointZ,
                textBoxJointPitch, textBoxJointRoll, textBoxJointYaw,
                textBoxLimitLower, textBoxLimitUpper, textBoxLimitEffort, textBoxLimitVelocity,
                textBoxDamping, textBoxFriction,
                textBoxCalibrationFalling, textBoxCalibrationRising,
                textBoxSoftLower, textBoxSoftUpper, textBoxKPosition, textBoxKVelocity
            };
            linkBoxes = new Control[] {
                textBoxInertialOriginX, textBoxInertialOriginY, textBoxInertialOriginZ,
                textBoxInertialOriginRoll, textBoxInertialOriginPitch, textBoxInertialOriginYaw,
                textBoxVisualOriginX, textBoxVisualOriginY, textBoxVisualOriginZ,
                textBoxVisualOriginRoll, textBoxVisualOriginPitch, textBoxVisualOriginYaw,
                textBoxIxx, textBoxIxy, textBoxIxz, textBoxIyy, textBoxIyz, textBoxIzz,
                textBoxMass,
                domainUpDownRed, domainUpDownGreen, domainUpDownBlue, domainUpDownAlpha,
                comboBoxMaterials,
                textBoxTexture
            };

            List<TextBox> numericTextBoxes = new List<TextBox>() {
                textBoxAxisX, textBoxAxisY, textBoxAxisZ,
                textBoxJointX, textBoxJointY, textBoxJointZ,
                textBoxJointPitch, textBoxJointRoll, textBoxJointYaw,
                textBoxLimitLower, textBoxLimitUpper, textBoxLimitEffort, textBoxLimitVelocity,
                textBoxDamping, textBoxFriction,
                textBoxCalibrationFalling, textBoxCalibrationRising,
                textBoxSoftLower, textBoxSoftUpper, textBoxKPosition, textBoxKVelocity,
                textBoxInertialOriginX, textBoxInertialOriginY, textBoxInertialOriginZ,
                textBoxInertialOriginRoll, textBoxInertialOriginPitch, textBoxInertialOriginYaw,
                textBoxVisualOriginX, textBoxVisualOriginY, textBoxVisualOriginZ,
                textBoxVisualOriginRoll, textBoxVisualOriginPitch, textBoxVisualOriginYaw,
                textBoxIxx, textBoxIxy, textBoxIxz, textBoxIyy, textBoxIyz, textBoxIzz,
                textBoxMass,
                textBoxMimicMultiplier, textBoxMimicOffset,
            };

            foreach (TextBox textBox in numericTextBoxes)
            {
                textBox.KeyPress += NumericalTextBoxKeyPress;
            }

        }

        private void ExceptionHandler(object sender, ThreadExceptionEventArgs e)
        {
            logger.Error("Exception encountered in Assembly export form", e.Exception);
            MessageBox.Show("There was a problem with the export form: \n\"" +
                e.Exception.Message + "\"\nEmail your maintainer with the log file found at " +
                Logger.GetFileName());
        }

        private void UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;
            logger.Error("Unhandled exception in Assembly Export form", ex);
            MessageBox.Show("There was a problem with the export form: \n\"" +
                ex.Message + "\"\nEmail your maintainer with the log file found at " +
                Logger.GetFileName());
        }

        //Joint form configuration controls
        private void AssemblyExportFormLoad(object sender, EventArgs e)
        {
            textBoxRosPackageName.Text = URDFPackage.SanitizePackageName(Exporter.RosPackageName);
            UpdateRosPackageNameHint();
            Exporter.UpdateReferenceGeometries();
            FillJointTree();
            SelectFirstJointNodeForEditing();
        }

        private void ButtonJointNextClick(object sender, EventArgs e)
        {
            if (!(previouslySelectedNode == null || previouslySelectedNode.Link.Joint == null))
            {
                SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link.Joint);
            }
            string errors = CheckJointsForErrors();
            if (!string.IsNullOrWhiteSpace(errors))
            {
                string message = "The following joints contain invalid or missing fields, please " +
                    "address them before continuing\r\n\r\n" + errors;
                MessageBox.Show(message, "URDF Joint Errors");
                return;
            }

            MoveJointTreeNodesToBaseNode();
            ChangeAllNodeFont(BaseNode, new Font(treeViewJointTree.Font, FontStyle.Regular));

            using (treeSelectionUpdateGuard.Suppress())
            {
                FillLinkTree();
                treeViewLinkProperties.SelectedNode = BaseNode;
            }
            DisplayLinkNode(BaseNode);
            panelLinkProperties.Visible = true;
            ResetLinkPanelScroll();
            Focus();
        }

        private string CheckJointsForErrors()
        {
            StringBuilder builder = new StringBuilder();
            List<Joint> joints = new List<Joint>();
            foreach (LinkNode child in treeViewJointTree.Nodes)
            {
                CheckJointsForErrors(child, builder);
                CollectJoints(child, joints);
            }
            AppendDuplicateJointNameErrors(treeViewJointTree.Nodes, builder);
            AppendMimicReferenceErrors(joints, builder);
            return builder.ToString();
        }

        private string CheckJointsForErrors(LinkNode root)
        {
            StringBuilder builder = new StringBuilder();
            List<Joint> joints = new List<Joint>();
            foreach (LinkNode child in root.Nodes)
            {
                CheckJointsForErrors(child, builder);
                CollectJoints(child, joints);
            }
            AppendDuplicateJointNameErrors(root.Nodes, builder);
            AppendMimicReferenceErrors(joints, builder);
            return builder.ToString();
        }

        private static void CollectJoints(LinkNode node, ICollection<Joint> joints)
        {
            if (node == null)
            {
                return;
            }
            if (!node.IsBaseNode && node.Link != null && node.Link.Joint != null)
            {
                joints.Add(node.Link.Joint);
            }
            foreach (LinkNode child in node.Nodes)
            {
                CollectJoints(child, joints);
            }
        }

        private static void AppendMimicReferenceErrors(
            IEnumerable<Joint> joints,
            StringBuilder builder)
        {
            foreach (string error in MimicGraphValidator.Validate(joints))
            {
                builder.Append(error).Append("\r\n");
            }
        }

        private StringBuilder CheckJointsForErrors(LinkNode node, StringBuilder builder)
        {
            if (!node.Link.Joint.AreRequiredFieldsSatisfied())
            {
                builder.Append(node.Link.Joint.Name).Append("\r\n");
            }

            foreach (LinkNode child in node.Nodes)
            {
                CheckJointsForErrors(child, builder);
            }
            return builder;
        }

        private void AppendDuplicateJointNameErrors(TreeNodeCollection nodes, StringBuilder builder)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (LinkNode child in nodes)
            {
                CollectDuplicateJointNames(child, seen, duplicates);
            }

            foreach (string duplicate in duplicates.OrderBy(n => n, StringComparer.Ordinal))
            {
                builder.Append("Duplicate joint name: ").Append(duplicate).Append("\r\n");
            }
        }

        private void CollectDuplicateJointNames(
            LinkNode node,
            HashSet<string> seen,
            HashSet<string> duplicates)
        {
            string jointName = node.Link == null || node.Link.Joint == null
                ? ""
                : node.Link.Joint.Name;
            if (!String.IsNullOrWhiteSpace(jointName) && !seen.Add(jointName))
            {
                duplicates.Add(jointName);
            }

            foreach (LinkNode child in node.Nodes)
            {
                CollectDuplicateJointNames(child, seen, duplicates);
            }
        }

        private void Button_Joint_Cancel_Click(object sender, EventArgs e)
        {
            if (previouslySelectedNode != null)
            {
                SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link.Joint);
            }
            MoveJointTreeNodesToBaseNode();
            if (SaveConfigTree(ActiveSWModel, BaseNode, true))
            {
                CloseWithoutRecoveryDraft();
            }
        }

        private void ButtonLinksCancelClick(object sender, EventArgs e)
        {
            if (previouslySelectedNode != null)
            {
                SaveLinkDataFromPropertyBoxes(previouslySelectedNode.Link);
            }
            if (SaveConfigTree(ActiveSWModel, BaseNode, true))
            {
                CloseWithoutRecoveryDraft();
            }
        }

        private void ButtonLinksPreviousClick(object sender, EventArgs e)
        {
            LinkNode node = (LinkNode)treeViewLinkProperties.SelectedNode;
            if (node != null)
            {
                SaveLinkDataFromPropertyBoxes(node.Link);
            }
            previouslySelectedNode = null;
            ChangeAllNodeFont(BaseNode, new Font(treeViewJointTree.Font, FontStyle.Regular));
            FillJointTree();
            panelLinkProperties.Visible = false;
            SelectFirstJointNodeForEditing();
        }

        private void ButtonLinksFinishClick(object sender, EventArgs e)
        {
            FinishExport(true);
        }

        private void ButtonLinksExportUrdfOnlyClick(object sender, EventArgs e)
        {
            FinishExport(false);
        }

        private void FinishExport(bool exportSTL)
        {
            ClearInertiaPreview();
            ClearCollisionPreview();
            logger.Info("Completing URDF export");
            Exporter.RosPackageName = URDFPackage.SanitizePackageName(textBoxRosPackageName.Text);
            textBoxRosPackageName.Text = Exporter.RosPackageName;
            UpdateRosPackageNameHint();

            // Saving selected node
            LinkNode node = (LinkNode)treeViewLinkProperties.SelectedNode;
            if (node != null)
            {
                SaveLinkDataFromPropertyBoxes(node.Link);
            }

            ApplyEditedMeshReductionToExportTree();
            string jointErrors = CheckJointsForErrors(BaseNode);
            if (!string.IsNullOrWhiteSpace(jointErrors))
            {
                logger.Info("Joint errors encountered:\n " + jointErrors);

                string message = "The following joints contain invalid or duplicate " +
                    "properties. Please address before continuing\r\n\r\n" + jointErrors;
                MessageBox.Show(message, "URDF Joint Errors");
                return;
            }

            Exporter.URDFRobot = CreateRobotFromTreeView(treeViewLinkProperties);

            // The UI should prevent these sorts of errors, but just in case
            string errors = CheckLinksForErrors(Exporter.URDFRobot.BaseLink);
            if (!string.IsNullOrWhiteSpace(errors))
            {
                logger.Info("Link errors encountered:\n " + errors);

                string message = "The following links contained errors in either their link or joint " +
                    "properties. Please address before continuing\r\n\r\n" + errors;
                MessageBox.Show(message, "URDF Errors");
                return;
            }

            if (!SaveConfigTree(ActiveSWModel, BaseNode, false))
            {
                return;
            }

            string warnings = CheckLinksForWarnings(Exporter.URDFRobot.BaseLink);

            if (!string.IsNullOrWhiteSpace(warnings))
            {
                logger.Info("Link warnings encountered:\r\n" + warnings);

                string message = "The following links contained issues that may cause problems. " +
                "Do you wish to proceed?\r\n\r\n" + warnings;
                DialogResult result =
                    MessageBox.Show(message, "URDF Warnings", MessageBoxButtons.YesNo);

                if (result == DialogResult.No)
                {
                    logger.Info("Export canceled for user to review warnings");
                    return;
                }
            }

            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
            {
                SelectedPath = Directory.Exists(Exporter.SavePath) ? Exporter.SavePath : "",
                Description = ChineseUiText.Translate(
                    "Select the export root directory for the ROS 1 and ROS 2 packages",
                    "\u9009\u62e9 ROS 1 \u548c ROS 2 \u529f\u80fd\u5305\u7684\u5bfc\u51fa\u6839\u76ee\u5f55")
            })
            {

            bool saveResult = DialogResult.OK == folderBrowserDialog.ShowDialog();
            if (saveResult)
            {
                Exporter.SavePath = folderBrowserDialog.SelectedPath;

                logger.Info("Saving ROS package " + Exporter.RosPackageName +
                    " to export root " + Exporter.SavePath);

                MeshExportFormat meshFormat;
                if(radioButtonStl.Checked)
                {
                    meshFormat = MeshExportFormat.STL;
                }
                else if(radioButton3dxml.Checked)
                {
                    meshFormat = MeshExportFormat.THREEDXML;
                }
                else
                {
                    meshFormat = MeshExportFormat.STL;
                }
                bool exportSucceeded;
                using (ExportProgressForm progressForm = new ExportProgressForm())
                {
                    EventHandler<ExportProgressEventArgs> progressHandler =
                        (progressSender, progress) => progressForm.UpdateProgress(progress);
                    Exporter.ExportProgressChanged += progressHandler;
                    progressForm.Show(this);
                    progressForm.Refresh();
                    Enabled = false;
                    try
                    {
                        exportSucceeded = Exporter.ExportRobot(exportSTL, meshFormat);
                    }
                    finally
                    {
                        Enabled = true;
                        Exporter.ExportProgressChanged -= progressHandler;
                        progressForm.Close();
                    }
                }

                if (!exportSucceeded)
                {
                    logger.Error(Exporter.ExportErrorWhy);
                    MessageBox.Show(
                        Exporter.ExportErrorWhy,
                        ChineseUiText.Translate("URDF export failed", "URDF \u5bfc\u51fa\u5931\u8d25"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                if (Exporter.LastExportSummary != null)
                {
                    MessageBox.Show(
                        Exporter.LastExportSummary.FormatDetails(),
                        ChineseUiText.Translate(
                            "URDF export completed",
                            "URDF 导出完成"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                CloseWithoutRecoveryDraft();
            }
            }
        }

        private void TextBoxRosPackageNameTextChanged(object sender, EventArgs e)
        {
            UpdateRosPackageNameHint();
        }

        private void UpdateRosPackageNameHint()
        {
            string sanitized = URDFPackage.SanitizePackageName(textBoxRosPackageName.Text);
            labelRosPackageNameHint.Text = "ROS1/2: " + sanitized;
            packagePathToolTip.SetToolTip(
                labelRosPackageNameHint,
                "ROS1/" + sanitized + " | ROS2/" + sanitized);
        }

        private void LinkCoordinateSystemSelectionChangeCommitted(object sender, EventArgs e)
        {
            LinkNode node = treeViewLinkProperties.SelectedNode as LinkNode;
            if (node == null || node.Link == null || node.Link.isFixedFrame)
            {
                return;
            }

            string previousCoordinateSystem = node.Link.Joint.CoordinateSystemName;
            string selectedCoordinateSystem = comboBoxLinkCoordinateSystem.Text;
            if (string.Equals(
                previousCoordinateSystem,
                selectedCoordinateSystem,
                StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                SaveLinkDataFromPropertyBoxes(node.Link);
                // Save the other edited fields, but let the exporter change the frame
                // transactionally so a failed SolidWorks recomputation can roll back.
                node.Link.Joint.CoordinateSystemName = previousCoordinateSystem;
                Exporter.RecomputeLinkCoordinateSystem(
                    node,
                    selectedCoordinateSystem);
                FillLinkPropertyBoxes(node.Link);
                logger.Info("Changed Link coordinate system for " + node.Link.Name +
                    " from " + previousCoordinateSystem + " to " + selectedCoordinateSystem);
                if (collisionPreviewEnabled)
                {
                    RefreshCollisionPreview();
                }
            }
            catch (Exception exception)
            {
                FillLinkPropertyBoxes(node.Link);
                logger.Warn("Could not change Link coordinate system for " + node.Link.Name, exception);
                MessageBox.Show(
                    ChineseUiText.Translate(
                        "The Link coordinate system could not be changed:\r\n",
                        "无法修改 Link 坐标系：\r\n") + exception.Message,
                    ChineseUiText.Translate("Link coordinate system", "Link 坐标系"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private string CheckLinksForErrors(Link baseLink)
        {
            StringBuilder builder = new StringBuilder();
            CheckLinkForErrors(baseLink, builder);
            return builder.ToString();
        }

        private StringBuilder CheckLinkForErrors(Link link, StringBuilder builder)
        {
            if (!link.AreRequiredFieldsSatisfied())
            {
                builder.Append(link.Name).Append("\r\n");
            }
            foreach (Link child in link.Children)
            {
                CheckLinkForErrors(child, builder);
            }
            return builder;
        }

        private void TreeViewLinkPropertiesAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (treeSelectionUpdateGuard.IsSuppressed)
            {
                return;
            }

            DisplayLinkNode((LinkNode)e.Node);
        }

        private void DisplayLinkNode(LinkNode node)
        {
            ClearInertiaPreview();
            ClearCollisionPreview();
            Font fontRegular = new Font(treeViewJointTree.Font, FontStyle.Regular);
            Font fontBold = new Font(treeViewJointTree.Font, FontStyle.Bold);
            if (previouslySelectedNode != null)
            {
                SaveLinkDataFromPropertyBoxes(previouslySelectedNode.Link);
                previouslySelectedNode.NodeFont = fontRegular;
            }
            node.NodeFont = fontBold;
            node.Text = node.Text;
            ActiveSWModel.ClearSelection2(true);
            SelectionMgr manager = ActiveSWModel.SelectionManager;

            SelectData data = manager.CreateSelectData();
            data.Mark = -1;
            foreach (Component2 component in node.Link.SWComponents)
            {
                component.Select4(true, data, false);
            }
            FillLinkPropertyBoxes(node.Link);
            treeViewLinkProperties.Focus();
            previouslySelectedNode = node;
        }

        /// <summary>
        /// Validates text entry for numerical text boxes to limit improper input. It's not perfect
        /// because you can still copy and paste bad input into the fields, but that's addressed
        /// elsewhere
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NumericalTextBoxKeyPress(object sender, KeyPressEventArgs e)
        {
            // In most cases, if we can't parse what they are trying to type, then it's not
            // valid input.
            TextBox textBox = (TextBox)sender;
            string potentialText = textBox.Text + e.KeyChar;
            bool parseSuccess =
                double.TryParse(potentialText,
                    URDFAttribute.URDFNumberStyle,
                    URDFAttribute.URDFNumberFormat,
                    out _);

            // If the key pressed is not a digit, +/- sign or the decimal separator than ignore it (e.Handled = true)
            e.Handled = (!parseSuccess &&
                         !char.IsControl(e.KeyChar) &&
                         !char.IsDigit(e.KeyChar) &&
                         potentialText != "-" &&
                         potentialText != "+");
        }

        #region Link Properties Controls Handlers

        private void ButtonMaterialColorPickClick(object sender, EventArgs e)
        {
            if (TryGetMaterialColor(out DrawingColor currentColor))
            {
                colorDialogMaterial.Color = currentColor;
            }

            if (colorDialogMaterial.ShowDialog() == DialogResult.OK)
            {
                SetMaterialColorBoxesFromColor(colorDialogMaterial.Color);
                UpdateMaterialColorPreview();
            }
        }

        private void MaterialColorPreviewClick(object sender, EventArgs e)
        {
            ButtonMaterialColorPickClick(sender, e);
        }

        private void MaterialColorValueChanged(object sender, EventArgs e)
        {
            if (!updatingMaterialColorControls)
            {
                UpdateMaterialColorPreview();
            }
        }

        private void SetMaterialColorBoxesFromColor(DrawingColor color)
        {
            updatingMaterialColorControls = true;
            try
            {
                domainUpDownRed.Text = ColorChannelToText(color.R);
                domainUpDownGreen.Text = ColorChannelToText(color.G);
                domainUpDownBlue.Text = ColorChannelToText(color.B);
            }
            finally
            {
                updatingMaterialColorControls = false;
            }
        }

        private void UpdateMaterialColorPreview()
        {
            if (TryGetMaterialColor(out DrawingColor color))
            {
                panelMaterialColorPreview.BackColor = color;
            }
        }

        private bool TryGetMaterialColor(out DrawingColor color)
        {
            color = DrawingColor.White;
            if (!TryGetColorChannel(domainUpDownRed.Text, out int red) ||
                !TryGetColorChannel(domainUpDownGreen.Text, out int green) ||
                !TryGetColorChannel(domainUpDownBlue.Text, out int blue))
            {
                return false;
            }

            color = DrawingColor.FromArgb(red, green, blue);
            return true;
        }

        private static bool TryGetColorChannel(string text, out int channel)
        {
            channel = 0;
            if (!double.TryParse(text, URDFAttribute.URDFNumberStyle,
                URDFAttribute.URDFNumberFormat, out double normalized))
            {
                return false;
            }

            normalized = Math.Max(0.0, Math.Min(1.0, normalized));
            channel = (int)Math.Round(normalized * 255);
            return true;
        }

        private static string ColorChannelToText(int channel)
        {
            double normalized = channel / 255.0;
            return normalized.ToString("G5", URDFAttribute.URDFNumberFormat);
        }

        private void InertiaMatrixOffDiagonalTextChanged(object sender, EventArgs e)
        {
            UpdateInertiaMatrixMirrorBoxes();
        }

        private void UpdateInertiaMatrixMirrorBoxes()
        {
            textBoxIyxMirror.Text = textBoxIxy.Text;
            textBoxIzxMirror.Text = textBoxIxz.Text;
            textBoxIzyMirror.Text = textBoxIyz.Text;
        }

        private void TrackBarMeshReductionScroll(object sender, EventArgs e)
        {
            meshReductionRatioEdited = true;
            meshReductionRatioForExport = TrackBarValueToMeshReductionRatio(trackBarMeshReduction.Value);
            UpdateMeshReductionLabel();
        }

        private void ButtonShowInertiaPreviewClick(object sender, EventArgs e)
        {
            if (inertiaPreview.IsVisible)
            {
                ClearInertiaPreview();
                return;
            }

            LinkNode node = treeViewLinkProperties.SelectedNode as LinkNode;
            if (node == null || node.Link == null || node.Link.isFixedFrame)
            {
                return;
            }

            try
            {
                SaveLinkDataFromPropertyBoxes(node.Link);
                MathTransform coordinateTransform =
                    Exporter.GetCoordinateSystemTransform(node.Link.Joint.CoordinateSystemName);
                if (inertiaPreview.Show(
                    node.Link,
                    coordinateTransform,
                    out InertiaEllipsoid ellipsoid,
                    out string error,
                    out InertiaPreviewFailureKind failureKind))
                {
                    buttonShowInertiaPreview.Text = ChineseUiText.Translate(
                        "Hide equivalent inertia cuboid",
                        "隐藏惯性等效长方体");
                    labelInertiaPreviewStatus.Text = String.Format(
                        ChineseUiText.Translate(
                            "Equivalent cuboid X / Y / Z: {0:0.#}/{1:0.#}/{2:0.#} mm",
                            "等效长方体 X / Y / Z：{0:0.#}/{1:0.#}/{2:0.#} mm"),
                        ellipsoid.EquivalentBoxDimensions[0] * 1000.0,
                        ellipsoid.EquivalentBoxDimensions[1] * 1000.0,
                        ellipsoid.EquivalentBoxDimensions[2] * 1000.0);
                    logger.Info(String.Format(
                        "Displayed equivalent inertia cuboid for link {0}: dimensions {1:G6}, {2:G6}, {3:G6} m",
                        node.Link.Name,
                        ellipsoid.EquivalentBoxDimensions[0],
                        ellipsoid.EquivalentBoxDimensions[1],
                        ellipsoid.EquivalentBoxDimensions[2]));
                }
                else
                {
                    logger.Warn("Inertia preview failed for link " +
                        node.Link.Name + " [" + failureKind + "]: " + error);
                    bool physicalInertiaInvalid =
                        failureKind == InertiaPreviewFailureKind.InvalidPhysicalInertia;
                    labelInertiaPreviewStatus.Text = physicalInertiaInvalid
                        ? ChineseUiText.Translate(
                            "Invalid physical inertia",
                            "\u7269\u7406\u60ef\u6027\u975e\u6cd5")
                        : ChineseUiText.Translate(
                            "Inertia overlay display failed",
                            "\u60ef\u6027\u53e0\u52a0\u5c42\u663e\u793a\u5931\u8d25");
                    MessageBox.Show(
                        (physicalInertiaInvalid
                            ? ChineseUiText.Translate(
                                "The inertia values are physically invalid, so no equivalent cuboid can be computed:\r\n",
                                "惯性参数不满足物理条件，无法计算惯性等效长方体：\r\n")
                            : ChineseUiText.Translate(
                                "The inertia values passed physical checks, but SolidWorks could not display the overlay:\r\n",
                                "\u60ef\u6027\u53c2\u6570\u5df2\u901a\u8fc7\u7269\u7406\u68c0\u67e5\uff0c\u4f46 SolidWorks \u65e0\u6cd5\u663e\u793a\u53e0\u52a0\u5c42\uff1a\r\n")) + error,
                        physicalInertiaInvalid
                            ? ChineseUiText.Translate(
                                "Inertia validation",
                                "\u60ef\u6027\u6821\u9a8c")
                            : ChineseUiText.Translate(
                                "Inertia preview",
                                "\u60ef\u6027\u9884\u89c8"));
                }
            }
            catch (Exception ex)
            {
                ClearInertiaPreview();
                logger.Warn("Could not display inertia preview for link " + node.Link.Name, ex);
                MessageBox.Show(
                    ChineseUiText.Translate(
                        "SolidWorks could not display the inertia overlay:\r\n",
                        "SolidWorks \u65e0\u6cd5\u663e\u793a\u60ef\u6027\u53e0\u52a0\u5c42\uff1a\r\n") + ex.Message,
                    ChineseUiText.Translate(
                        "Inertia preview",
                        "\u60ef\u6027\u9884\u89c8"));
            }
        }

        private void ButtonShowCollisionPreviewClick(object sender, EventArgs e)
        {
            if (collisionPreviewEnabled)
            {
                ClearCollisionPreview();
                return;
            }

            collisionPreviewEnabled = true;
            RefreshCollisionPreview();
        }

        private void CollisionStrategySelectionChangeCommitted(object sender, EventArgs e)
        {
            LinkNode node = treeViewLinkProperties.SelectedNode as LinkNode;
            if (node != null && node.Link != null && !node.Link.isFixedFrame)
            {
                node.Link.CollisionMeshStrategy = GetSelectedCollisionStrategy();
            }
            if (collisionPreviewEnabled)
            {
                RefreshCollisionPreview();
            }
        }

        private void RefreshCollisionPreview()
        {
            LinkNode node = treeViewLinkProperties.SelectedNode as LinkNode;
            if (node == null || node.Link == null || node.Link.isFixedFrame)
            {
                ClearCollisionPreview();
                return;
            }

            try
            {
                SaveLinkDataFromPropertyBoxes(node.Link);
                MathTransform coordinateTransform =
                    Exporter.GetCoordinateSystemTransform(node.Link.Joint.CoordinateSystemName);
                if (collisionPreview.Show(
                    node.Link,
                    GetSelectedCollisionStrategy(),
                    coordinateTransform,
                    out string status,
                    out string error))
                {
                    labelCollisionPreviewStatus.Text = status;
                    buttonShowCollisionPreview.Text = collisionPreview.IsVisible
                        ? ChineseUiText.Translate("Hide collision overlay", "隐藏碰撞体")
                        : ChineseUiText.Translate("Refresh collision", "刷新碰撞体");
                    return;
                }

                labelCollisionPreviewStatus.Text = String.IsNullOrWhiteSpace(error)
                    ? status
                    : error;
                if (!String.IsNullOrWhiteSpace(error))
                {
                    logger.Warn("Collision preview failed for link " + node.Link.Name + ": " + error);
                }
                buttonShowCollisionPreview.Text = ChineseUiText.Translate(
                    "Refresh collision",
                    "刷新碰撞体");
            }
            catch (Exception exception)
            {
                collisionPreview.Hide();
                labelCollisionPreviewStatus.Text = ChineseUiText.Translate(
                    "Collision preview failed",
                    "碰撞体预览失败");
                logger.Warn("Could not refresh collision preview for link " + node.Link.Name, exception);
            }
        }

        private void ClearCollisionPreview()
        {
            collisionPreviewEnabled = false;
            if (collisionPreview != null)
            {
                collisionPreview.Hide();
            }
            if (buttonShowCollisionPreview != null)
            {
                buttonShowCollisionPreview.Text = ChineseUiText.Translate(
                    "Preview collision",
                    "预览碰撞体");
            }
            if (labelCollisionPreviewStatus != null)
            {
                labelCollisionPreviewStatus.Text = ChineseUiText.Translate(
                    "Overlay is not displayed",
                    "未显示碰撞体叠加层");
            }
        }

        private void ClearInertiaPreview()
        {
            inertiaPreview.Hide();
            buttonShowInertiaPreview.Text = ChineseUiText.Translate(
                "Show equivalent inertia cuboid",
                "显示惯性等效长方体");
            labelInertiaPreviewStatus.Text = ChineseUiText.Translate(
                "Equivalent cuboid X / Y / Z dimensions (mm)",
                "惯性等效长方体 X / Y / Z 尺寸 (mm)");
        }

        private void AssemblyExportFormClosed(object sender, FormClosedEventArgs e)
        {
            DisposeOwnedResources();
        }

        private void DisposeOwnedResources()
        {
            if (ownedResourcesDisposed)
            {
                return;
            }
            ownedResourcesDisposed = true;
            Application.ThreadException -= ExceptionHandler;
            AppDomain.CurrentDomain.UnhandledException -= UnhandledException;
            if (inertiaPreview != null)
            {
                inertiaPreview.Dispose();
            }
            if (collisionPreview != null)
            {
                collisionPreview.Dispose();
            }
            if (packagePathToolTip != null)
            {
                packagePathToolTip.Dispose();
            }
            if (buttonUsageGuide != null)
            {
                buttonUsageGuide.Dispose();
            }
            if (labelLinkCoordinateSystem != null)
            {
                labelLinkCoordinateSystem.Dispose();
            }
            if (comboBoxLinkCoordinateSystem != null)
            {
                comboBoxLinkCoordinateSystem.Dispose();
            }
            if (buttonShowCollisionPreview != null)
            {
                buttonShowCollisionPreview.Dispose();
            }
            if (labelCollisionPreviewStatus != null)
            {
                labelCollisionPreviewStatus.Dispose();
            }
        }

        private void AssemblyExportFormClosing(object sender, FormClosingEventArgs e)
        {
            if (suppressRecoveryDraftOnClose)
            {
                return;
            }

            try
            {
                CaptureCurrentExportSession();
                CommonSwOperations.RetrieveSWComponentPIDs(ActiveSWModel, BaseNode);
                Exporter.RosPackageName = URDFPackage.SanitizePackageName(
                    textBoxRosPackageName.Text);
                if (exportSessionDraftStore.Save(
                    ActiveSWModel.GetPathName(),
                    BaseNode,
                    Exporter.RosPackageName,
                    Exporter.SavePath))
                {
                    logger.Info("Saved the URDF export recovery draft before closing the export window.");
                }
            }
            catch (Exception exception)
            {
                logger.Warn("The URDF export recovery draft could not be captured while closing.", exception);
            }
        }

        private void CaptureCurrentExportSession()
        {
            if (panelLinkProperties.Visible)
            {
                LinkNode selectedLinkNode = (LinkNode)treeViewLinkProperties.SelectedNode;
                if (selectedLinkNode != null)
                {
                    SaveLinkDataFromPropertyBoxes(selectedLinkNode.Link);
                }
            }
            else
            {
                if (previouslySelectedNode != null && previouslySelectedNode.Link.Joint != null)
                {
                    SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link.Joint);
                }
                MoveJointTreeNodesToBaseNode();
            }

            ApplyEditedMeshReductionToExportTree();
        }

        private void CloseWithoutRecoveryDraft()
        {
            suppressRecoveryDraftOnClose = true;
            Close();
        }

        private void ClearExportSessionDraft()
        {
            try
            {
                exportSessionDraftStore.Delete(ActiveSWModel.GetPathName());
            }
            catch (Exception exception)
            {
                logger.Warn("The URDF export recovery draft could not be cleared.", exception);
            }
        }

        private void UpdateMeshReductionLabel()
        {
            double ratio = TrackBarValueToMeshReductionRatio(trackBarMeshReduction.Value);
            labelMeshReductionValue.Text = ratio.ToString("0.00", URDFAttribute.URDFNumberFormat);
            labelEstimatedMeshSize.Text = ChineseUiText.Translate(
                "Rough STL estimate: logged on export",
                "\u7c97\u7565 STL \u4f30\u7b97\uff1a\u5bfc\u51fa\u65f6\u5199\u5165\u65e5\u5fd7");
        }

        private static int MeshReductionRatioToTrackBarValue(double ratio)
        {
            ratio = Math.Max(0.0, Math.Min(1.0, ratio));
            return (int)Math.Round(ratio * 100.0);
        }

        private static double TrackBarValueToMeshReductionRatio(int value)
        {
            return Math.Max(0.0, Math.Min(1.0, value / 100.0));
        }

        private void ApplyEditedMeshReductionToExportTree()
        {
            if (!meshReductionRatioEdited)
            {
                return;
            }

            ApplyMeshReductionToTree(BaseNode, meshReductionRatioForExport);
            logger.Info(String.Format(
                "Applying STL mesh reduction ratio {0:0.00} to every link for this export",
                meshReductionRatioForExport));
        }

        internal static void ApplyMeshReductionToTree(LinkNode node, double ratio)
        {
            if (node == null)
            {
                return;
            }

            ratio = Math.Max(0.0, Math.Min(1.0, ratio));
            if (node.Link != null && !node.Link.isFixedFrame)
            {
                node.Link.MeshReductionRatio = ratio;
            }

            foreach (LinkNode child in node.Nodes)
            {
                ApplyMeshReductionToTree(child, ratio);
            }
        }

        private void ButtonTextureBrowseClick(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog
            {
                RestoreDirectory = true
            };

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                textBoxTexture.Text = openFileDialog1.FileName;
            }
        }

        #endregion Link Properties Controls Handlers

        #region Joint Properties Controls Handlers

        private void TreeViewJointtreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (treeSelectionUpdateGuard.IsSuppressed)
            {
                return;
            }

            DisplayJointNode((LinkNode)e.Node);
        }

        private void DisplayJointNode(LinkNode node)
        {
            Font fontRegular = new Font(treeViewJointTree.Font, FontStyle.Regular);
            Font fontBold = new Font(treeViewJointTree.Font, FontStyle.Bold);
            if (previouslySelectedNode != null && !previouslySelectedNode.IsBaseNode)
            {
                SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link.Joint);
            }
            if (previouslySelectedNode != null)
            {
                previouslySelectedNode.NodeFont = fontRegular;
            }
            ActiveSWModel.ClearSelection2(true);
            SelectionMgr manager = ActiveSWModel.SelectionManager;

            SelectData data = manager.CreateSelectData();
            data.Mark = -1;
            foreach (Component2 component in node.Link.SWComponents)
            {
                component.Select4(true, data, false);
            }
            node.NodeFont = fontBold;
            node.Text = node.Text;
            FillJointPropertyBoxes(node.Link.Joint);
            previouslySelectedNode = node;
        }

        private void SelectFirstJointNodeForEditing()
        {
            if (treeViewJointTree.Nodes.Count == 0)
            {
                previouslySelectedNode = null;
                FillJointPropertyBoxes(null);
                return;
            }

            LinkNode node = (LinkNode)treeViewJointTree.Nodes[0];
            using (treeSelectionUpdateGuard.Suppress())
            {
                treeViewJointTree.SelectedNode = node;
            }
            DisplayJointNode(node);
        }

        private void MoveJointTreeNodesToBaseNode()
        {
            using (treeSelectionUpdateGuard.Suppress())
            {
                while (treeViewJointTree.Nodes.Count > 0)
                {
                    LinkNode node = (LinkNode)treeViewJointTree.Nodes[0];
                    treeViewJointTree.Nodes.Remove(node);
                    BaseNode.Nodes.Add(node);
                }
                previouslySelectedNode = null;
            }
        }

        private void ComboBoxAxisSelectedIndexChanged(object sender, EventArgs e)
        {
            if (!AutoUpdatingForm)
            {
                if (!(String.IsNullOrWhiteSpace(comboBoxOrigin.Text) ||
                    String.IsNullOrWhiteSpace(comboBoxAxis.Text)))
                {
                    double[] Axis = Exporter.EstimateAxis(comboBoxAxis.Text);
                    Axis = Exporter.LocalizeAxis(Axis, comboBoxOrigin.Text);
                    textBoxAxisX.Text = Axis[0].ToString("G5");
                    textBoxAxisY.Text = Axis[1].ToString("G5");
                    textBoxAxisZ.Text = Axis[2].ToString("G5");
                }
            }
        }

        #endregion Joint Properties Controls Handlers

        private void ShowMimicControls(bool showControls)
        {
            MimicEquationLabel.Visible = showControls;
            MimicJointComboBox.Visible = showControls;
            MimicJointLabel.Visible = showControls;
            MimicMultiplierLabel.Visible = showControls;
            textBoxMimicMultiplier.Visible = showControls;
            MimicOffsetLabel.Visible = showControls;
            textBoxMimicOffset.Visible = showControls;
            PositionJointFooterControls();
        }

        private void MimicCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            bool showControls = (sender as CheckBox).Checked;
            ShowMimicControls(showControls);
            if (showControls && string.IsNullOrWhiteSpace(textBoxMimicMultiplier.Text))
            {
                textBoxMimicMultiplier.Text = "1.0";
            }
            if (showControls && string.IsNullOrWhiteSpace(textBoxMimicOffset.Text))
            {
                textBoxMimicOffset.Text = "0.0";
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (enableLayoutFixes)
            {
                ApplyHighDpiLayoutFixes();
            }
        }

        private void ApplyHighDpiLayoutFixes()
        {
            if (applyingLayoutFixes)
            {
                return;
            }

            applyingLayoutFixes = true;
            try
            {
                EnsureMinimumClientArea();
                ResizeButtonToText(buttonJointCancel);
                ResizeButtonToText(buttonJointNext);
                ResizeButtonToText(buttonLinksCancel);
                ResizeButtonToText(buttonLinksPrevious);
                ResizeButtonToText(buttonLinksExportUrdfOnly);
                ResizeButtonToText(buttonLinksFinish);

                PositionJointFooterControls();
                PositionLinkFooterButtons();
                PositionUsageGuideButton();
                PositionRosPackageNameHint();
            }
            finally
            {
                applyingLayoutFixes = false;
            }
        }

        private void PositionRosPackageNameHint()
        {
            int right = buttonUsageGuide == null
                ? ClientSize.Width - 12
                : buttonUsageGuide.Left - 8;
            right = Math.Min(right, groupBox5.Right);
            labelRosPackageNameHint.Width = Math.Max(
                80,
                right - labelRosPackageNameHint.Left);
        }

        private void PositionUsageGuideButton()
        {
            if (buttonUsageGuide == null)
            {
                return;
            }

            const int rightMargin = 12;
            const int topMargin = 8;
            Size preferred = TextRenderer.MeasureText(buttonUsageGuide.Text ?? "", buttonUsageGuide.Font);
            buttonUsageGuide.Size = new Size(
                Math.Max(86, preferred.Width + 18),
                Math.Max(26, preferred.Height + 10));
            buttonUsageGuide.Left = ClientSize.Width - rightMargin - buttonUsageGuide.Width;
            buttonUsageGuide.Top = topMargin;
            buttonUsageGuide.BringToFront();
        }

        private void EnsureMinimumClientArea()
        {
            const int minimumClientHeight = 660;
            if (ClientSize.Height < minimumClientHeight)
            {
                ClientSize = new Size(ClientSize.Width, minimumClientHeight);
            }

            MinimumSize = new Size(
                Math.Max(MinimumSize.Width, 1089),
                Math.Max(MinimumSize.Height, 700));
        }

        private Dictionary<string, Size> CaptureButtonDesignSizes()
        {
            Button[] buttons = new Button[]
            {
                buttonJointCancel,
                buttonJointNext,
                buttonLinksCancel,
                buttonLinksPrevious,
                buttonLinksExportUrdfOnly,
                buttonLinksFinish
            };
            Dictionary<string, Size> sizes = new Dictionary<string, Size>(StringComparer.Ordinal);
            foreach (Button button in buttons)
            {
                sizes[button.Name] = button.Size;
            }
            return sizes;
        }

        private void ResizeButtonToText(Button button)
        {
            Size designSize;
            if (!buttonDesignSizes.TryGetValue(button.Name, out designSize))
            {
                designSize = button.Size;
            }

            Size preferred = TextRenderer.MeasureText(button.Text ?? "", button.Font);
            Size controlPreferred = button.GetPreferredSize(Size.Empty);
            button.Size = new Size(
                Math.Max(designSize.Width, preferred.Width + 18),
                Math.Max(designSize.Height, Math.Max(controlPreferred.Height, preferred.Height + 10)));
        }

        private void PositionJointFooterControls()
        {
            const int rightMargin = 12;
            const int bottomMargin = 4;
            const int verticalGap = 4;
            int footerLeft = treeViewJointTree.Right + 24;
            int buttonTop = 0;
            int label4Top = 0;
            int label27Top = 0;

            ResizeButtonToText(buttonJointCancel);
            ResizeButtonToText(buttonJointNext);

            for (int attempts = 0; attempts < 4; attempts++)
            {
                buttonTop = ClientSize.Height - bottomMargin - buttonJointNext.Height;
                buttonJointCancel.Top = buttonTop;
                buttonJointCancel.Left = rightMargin;
                buttonJointNext.Top = buttonTop;
                buttonJointNext.Left = ClientSize.Width - rightMargin - buttonJointNext.Width;

                int footerWidth = Math.Max(120, ClientSize.Width - rightMargin - footerLeft);
                FitLabelToWidth(label4, footerWidth);
                FitLabelToWidth(label27, footerWidth);

                label4Top = buttonTop - label27.Height - label4.Height - verticalGap;
                label27Top = label4Top + label4.Height + 1;
                PositionJointMimicControls(label4Top - verticalGap);

                int overflow = GetMimicControlsBottom() + verticalGap - label4Top;
                if (overflow <= 0)
                {
                    break;
                }

                ClientSize = new Size(ClientSize.Width, ClientSize.Height + overflow);
            }

            int requiredLabelTop = GetMimicControlsBottom() + verticalGap;
            if (requiredLabelTop > label4Top)
            {
                int footerShift = requiredLabelTop - label4Top;
                label4Top += footerShift;
                label27Top += footerShift;

                int footerBottom = label27Top + label27.Height;
                int requiredButtonTop = footerBottom + verticalGap;
                if (requiredButtonTop > buttonTop)
                {
                    int growth = requiredButtonTop - buttonTop;
                    ClientSize = new Size(ClientSize.Width, ClientSize.Height + growth);
                    buttonTop += growth;
                    buttonJointCancel.Top = buttonTop;
                    buttonJointCancel.Left = rightMargin;
                    buttonJointNext.Top = buttonTop;
                    buttonJointNext.Left = ClientSize.Width - rightMargin - buttonJointNext.Width;
                }
            }

            label27.Left = footerLeft;
            label4.Left = label27.Left;
            label4.Top = label4Top;
            label27.Top = label27Top;

            treeViewJointTree.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            treeViewJointTree.Height = Math.Max(
                200,
                label4.Top - treeViewJointTree.Top - verticalGap);
        }

        private void PositionLinkFooterButtons()
        {
            const int rightMargin = 12;
            const int bottomMargin = 4;
            const int horizontalGap = 8;
            const int verticalGap = 8;
            ResizeButtonToText(buttonLinksCancel);
            ResizeButtonToText(buttonLinksPrevious);
            ResizeButtonToText(buttonLinksExportUrdfOnly);
            ResizeButtonToText(buttonLinksFinish);

            int maxHeight = Math.Max(
                Math.Max(buttonLinksCancel.Height, buttonLinksPrevious.Height),
                Math.Max(buttonLinksExportUrdfOnly.Height, buttonLinksFinish.Height));
            int contentBottom = Math.Max(groupBox5.Bottom, groupBox4.Bottom);
            int visibleButtonTop = panelLinkProperties.ClientSize.Height - bottomMargin - maxHeight;
            int buttonTop = Math.Max(visibleButtonTop, contentBottom + verticalGap);
            int requiredScrollHeight = buttonTop + maxHeight + bottomMargin;
            int scrollHeight = requiredScrollHeight > panelLinkProperties.ClientSize.Height
                ? requiredScrollHeight
                : 0;
            panelLinkProperties.AutoScrollMinSize = new Size(0, scrollHeight);
            if (scrollHeight == 0 && panelLinkProperties.AutoScrollPosition != Point.Empty)
            {
                ResetLinkPanelScroll();
            }

            buttonLinksCancel.Top = buttonTop;
            buttonLinksFinish.Top = buttonTop;
            buttonLinksExportUrdfOnly.Top = buttonTop;
            buttonLinksPrevious.Top = buttonTop;
            buttonLinksCancel.Left = rightMargin;
            treeViewLinkProperties.Height = Math.Max(
                200,
                buttonTop - treeViewLinkProperties.Top - 8);
            buttonLinksFinish.Left = panelLinkProperties.ClientSize.Width - rightMargin - buttonLinksFinish.Width;
            buttonLinksExportUrdfOnly.Left = buttonLinksFinish.Left - horizontalGap - buttonLinksExportUrdfOnly.Width;
            buttonLinksPrevious.Left = buttonLinksExportUrdfOnly.Left - horizontalGap - buttonLinksPrevious.Width;
        }

        private void ResetLinkPanelScroll()
        {
            panelLinkProperties.AutoScrollPosition = Point.Empty;
        }

        private int GetMimicControlsBottom()
        {
            int bottom = MimicCheckBox.Bottom;
            if (!ShouldLayoutMimicDetails())
            {
                return bottom;
            }

            bottom = Math.Max(bottom, MimicJointComboBox.Bottom);
            bottom = Math.Max(bottom, MimicJointLabel.Bottom);
            bottom = Math.Max(bottom, MimicMultiplierLabel.Bottom);
            bottom = Math.Max(bottom, textBoxMimicMultiplier.Bottom);
            bottom = Math.Max(bottom, MimicOffsetLabel.Bottom);
            bottom = Math.Max(bottom, textBoxMimicOffset.Bottom);
            return Math.Max(bottom, MimicEquationLabel.Bottom);
        }

        private bool ShouldLayoutMimicDetails()
        {
            return MimicCheckBox.Checked ||
                MimicJointComboBox.Visible ||
                MimicJointLabel.Visible ||
                MimicMultiplierLabel.Visible ||
                textBoxMimicMultiplier.Visible ||
                MimicOffsetLabel.Visible ||
                textBoxMimicOffset.Visible ||
                MimicEquationLabel.Visible;
        }

        private void PositionJointMimicControls(int maxBottom)
        {
            const int horizontalGap = 8;
            int left = textBoxCalibrationRising.Left;
            bool showDetails = ShouldLayoutMimicDetails();
            int maxRight = ClientSize.Width - 12;
            int mimicCheckWidth = Math.Min(170, MimicCheckBox.GetPreferredSize(Size.Empty).Width);
            int mimicJointLabelWidth = Math.Min(180, MimicJointLabel.GetPreferredSize(Size.Empty).Width);
            MimicCheckBox.AutoSize = false;
            MimicJointLabel.AutoSize = false;
            MimicCheckBox.Size = new Size(mimicCheckWidth, MimicCheckBox.Height);
            MimicJointLabel.Size = new Size(mimicJointLabelWidth, MimicJointLabel.Height);
            int mimicJointLabelLeft = left + Math.Max(150, mimicCheckWidth + 18);
            int mimicJointComboLeft = Math.Min(
                mimicJointLabelLeft + mimicJointLabelWidth + horizontalGap,
                maxRight - MimicJointComboBox.Width);

            int rowOneHeight = Math.Max(
                Math.Max(MimicJointComboBox.Height, MimicCheckBox.Height),
                MimicJointLabel.Height);
            int rowTwoControlHeight = Math.Max(
                Math.Max(textBoxMimicMultiplier.Height, textBoxMimicOffset.Height),
                Math.Max(MimicMultiplierLabel.Height, MimicOffsetLabel.Height));
            int multiplierTextLeft = left + MimicMultiplierLabel.Width + horizontalGap;
            int offsetLabelLeft = multiplierTextLeft + textBoxMimicMultiplier.Width + 16;
            int offsetTextLeft = offsetLabelLeft + MimicOffsetLabel.Width + horizontalGap;
            int inlineEquationLeft = offsetTextLeft + textBoxMimicOffset.Width + 18;

            MimicEquationLabel.AutoSize = true;
            MimicEquationLabel.MaximumSize = Size.Empty;
            int preferredEquationWidth = MimicEquationLabel.GetPreferredSize(Size.Empty).Width;
            bool stackEquation = showDetails &&
                inlineEquationLeft + preferredEquationWidth > maxRight;
            int equationLeft = stackEquation ? left : inlineEquationLeft;
            int equationWidth = Math.Max(1, maxRight - equationLeft);
            FitLabelToWidth(MimicEquationLabel, equationWidth);

            int rowTwoHeight = rowTwoControlHeight;
            if (showDetails)
            {
                rowTwoHeight = stackEquation
                    ? rowTwoControlHeight + 4 + MimicEquationLabel.Height
                    : Math.Max(rowTwoControlHeight, MimicEquationLabel.Height);
            }

            int mimicHeight = rowOneHeight + (showDetails ? rowTwoHeight + 4 : 0);
            int rowOneTop = Math.Min(textBoxKVelocity.Bottom + 8, maxBottom - mimicHeight);
            rowOneTop = Math.Max(textBoxKVelocity.Bottom + 2, rowOneTop);
            int rowTwoTop = rowOneTop + rowOneHeight + 4;

            MimicCheckBox.Left = left;
            MimicCheckBox.Top = rowOneTop + Math.Max(0, (MimicJointComboBox.Height - MimicCheckBox.Height) / 2);
            MimicJointLabel.Left = mimicJointLabelLeft;
            MimicJointLabel.Top = rowOneTop + 3;
            MimicJointComboBox.Left = mimicJointComboLeft;
            MimicJointComboBox.Top = rowOneTop;

            MimicMultiplierLabel.Left = left;
            MimicMultiplierLabel.Top = rowTwoTop + 3;
            textBoxMimicMultiplier.Left = multiplierTextLeft;
            textBoxMimicMultiplier.Top = rowTwoTop;
            MimicOffsetLabel.Left = offsetLabelLeft;
            MimicOffsetLabel.Top = rowTwoTop + 3;
            textBoxMimicOffset.Left = offsetTextLeft;
            textBoxMimicOffset.Top = rowTwoTop;
            MimicEquationLabel.Left = equationLeft;
            MimicEquationLabel.Top = stackEquation
                ? rowTwoTop + rowTwoControlHeight + 4
                : rowTwoTop + 3;
        }

        private static void FitLabelToWidth(Label label, int maxWidth)
        {
            int width = Math.Max(1, maxWidth);
            label.AutoSize = false;
            label.MaximumSize = new Size(width, 0);
            Size measured = TextRenderer.MeasureText(
                label.Text ?? "",
                label.Font,
                new Size(width, Int32.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            label.Size = new Size(
                width,
                Math.Max(label.Font.Height + 2, measured.Height + 2));
        }
    }
}
