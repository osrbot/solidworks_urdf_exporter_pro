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
        private bool updatingMaterialColorControls;
        private bool meshReductionRatioEdited;
        private double meshReductionRatioForExport;
        private bool enableLayoutFixes;
        private bool applyingLayoutFixes;
        private string displayedJointType;
        private bool jointUnitInputsResetForCurrentChange;
        private readonly Dictionary<string, Size> buttonDesignSizes;
        private Button buttonUsageGuide;

        private AssemblyExportForm()
        {
            InitializeComponent();
            ChineseUiText.Apply(this);
            InitializeUsageGuideButton();
            InitializeCommonMaterialNames();
            buttonDesignSizes = CaptureButtonDesignSizes();
            enableLayoutFixes = true;
            ApplyHighDpiLayoutFixes();
            InitializeCollisionStrategyComboBox();
            textBoxIxy.TextChanged += InertiaMatrixOffDiagonalTextChanged;
            textBoxIxz.TextChanged += InertiaMatrixOffDiagonalTextChanged;
            textBoxIyz.TextChanged += InertiaMatrixOffDiagonalTextChanged;
            comboBoxJointType.TextChanged += ComboBoxJointTypeTextChanged;
            UpdateInertiaMatrixMirrorBoxes();
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
            AutoUpdatingForm = false;
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
        }

        private void ButtonJointNextClick(object sender, EventArgs e)
        {
            if (!(previouslySelectedNode == null || previouslySelectedNode.Link.Joint == null))
            {
                SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link.Joint);
            }
            previouslySelectedNode = null; // Need to clear this for the link properties page

            string errors = CheckJointsForErrors();
            if (!string.IsNullOrWhiteSpace(errors))
            {
                string message = "The following joints contain invalid or missing fields, please " +
                    "address them before continuing\r\n\r\n" + errors;
                MessageBox.Show(message, "URDF Joint Errors");
                return;
            }

            while (treeViewJointTree.Nodes.Count > 0)
            {
                LinkNode node = (LinkNode)treeViewJointTree.Nodes[0];
                treeViewJointTree.Nodes.Remove(node);
                BaseNode.Nodes.Add(node);
            }
            ChangeAllNodeFont(BaseNode, new Font(treeViewJointTree.Font, FontStyle.Regular));

            FillLinkTree();
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
            while (treeViewJointTree.Nodes.Count > 0)
            {
                LinkNode node = (LinkNode)treeViewJointTree.Nodes[0];
                treeViewJointTree.Nodes.Remove(node);
                BaseNode.Nodes.Add(node);
            }
            if (SaveConfigTree(ActiveSWModel, BaseNode, true))
            {
                Close();
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
                Close();
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

            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
            {
                SelectedPath = Directory.Exists(Exporter.SavePath) ? Exporter.SavePath : "",
                Description = ChineseUiText.Translate(
                    "Select the export root directory for the ROS 1 and ROS 2 packages",
                    "\u9009\u62e9 ROS 1 \u548c ROS 2 \u529f\u80fd\u5305\u7684\u5bfc\u51fa\u6839\u76ee\u5f55")
            };

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
                if (!Exporter.ExportRobot(exportSTL, meshFormat))
                {
                    logger.Error(Exporter.ExportErrorWhy);
                    MessageBox.Show(
                        Exporter.ExportErrorWhy,
                        ChineseUiText.Translate("URDF export failed", "URDF \u5bfc\u51fa\u5931\u8d25"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                Close();
            }
            folderBrowserDialog.Dispose();
        }

        private void TextBoxRosPackageNameTextChanged(object sender, EventArgs e)
        {
            UpdateRosPackageNameHint();
        }

        private void UpdateRosPackageNameHint()
        {
            string sanitized = URDFPackage.SanitizePackageName(textBoxRosPackageName.Text);
            labelRosPackageNameHint.Text = "ROS1/" + sanitized + " | ROS2/" + sanitized;
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
            ClearInertiaPreview();
            Font fontRegular = new Font(treeViewJointTree.Font, FontStyle.Regular);
            Font fontBold = new Font(treeViewJointTree.Font, FontStyle.Bold);
            if (previouslySelectedNode != null)
            {
                SaveLinkDataFromPropertyBoxes(previouslySelectedNode.Link);
                previouslySelectedNode.NodeFont = fontRegular;
            }
            LinkNode node = (LinkNode)e.Node;
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
                        "Hide inertia ellipsoid",
                        "\u9690\u85cf\u60ef\u6027\u692d\u7403");
                    labelInertiaPreviewStatus.Text = String.Format(
                        ChineseUiText.Translate(
                            "Semi-axes R a / G b / B c: {0:0.#}/{1:0.#}/{2:0.#} mm",
                            "\u534a\u8f74 \u7ea2a/\u7effb/\u84ddc\uff1a{0:0.#}/{1:0.#}/{2:0.#} mm"),
                        ellipsoid.SemiAxes[0] * 1000.0,
                        ellipsoid.SemiAxes[1] * 1000.0,
                        ellipsoid.SemiAxes[2] * 1000.0);
                    logger.Info(String.Format(
                        "Displayed inertia ellipsoid for link {0}: semi-axes {1:G6}, {2:G6}, {3:G6} m",
                        node.Link.Name,
                        ellipsoid.SemiAxes[0],
                        ellipsoid.SemiAxes[1],
                        ellipsoid.SemiAxes[2]));
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
                                "The inertia values are physically invalid, so no ellipsoid can be computed:\r\n",
                                "\u60ef\u6027\u53c2\u6570\u672c\u8eab\u4e0d\u6ee1\u8db3\u7269\u7406\u6761\u4ef6\uff0c\u56e0\u6b64\u65e0\u6cd5\u8ba1\u7b97\u692d\u7403\uff1a\r\n")
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

        private void ClearInertiaPreview()
        {
            inertiaPreview.Hide();
            buttonShowInertiaPreview.Text = ChineseUiText.Translate(
                "Show inertia ellipsoid",
                "\u663e\u793a\u60ef\u6027\u692d\u7403");
            labelInertiaPreviewStatus.Text = ChineseUiText.Translate(
                "R a / G b / B c: principal semi-axes",
                "\u7ea2a / \u7effb / \u84ddc\uff1a\u4e3b\u60ef\u6027\u534a\u8f74");
        }

        private void AssemblyExportFormClosed(object sender, FormClosedEventArgs e)
        {
            inertiaPreview.Dispose();
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
            LinkNode node = (LinkNode)e.Node;
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
            }
            finally
            {
                applyingLayoutFixes = false;
            }
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
            const int verticalGap = 12;
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
