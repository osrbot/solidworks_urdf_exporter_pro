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
        public AttributeDef saveConfigurationAttributeDef;

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

        private AssemblyExportForm()
        {
            InitializeComponent();
            ChineseUiText.Apply(this);
            InitializeCollisionStrategyComboBox();
            textBoxIxy.TextChanged += InertiaMatrixOffDiagonalTextChanged;
            textBoxIxz.TextChanged += InertiaMatrixOffDiagonalTextChanged;
            textBoxIyz.TextChanged += InertiaMatrixOffDiagonalTextChanged;
            UpdateInertiaMatrixMirrorBoxes();
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

            saveConfigurationAttributeDef = SwApp.DefineAttribute(ConfigurationSerialization.UrdfConfigurationSwAttributeName);
            int Options = 0;

            saveConfigurationAttributeDef.AddParameter(
                "data", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "name", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "date", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "exporterVersion", (int)swParamType_e.swParamTypeDouble, 1.0, Options);
            saveConfigurationAttributeDef.Register();
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
                string message = "The following joints are missing required fields, please " +
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
            Focus();
        }

        private string CheckJointsForErrors()
        {
            StringBuilder builder = new StringBuilder();
            foreach (LinkNode child in treeViewJointTree.Nodes)
            {
                CheckJointsForErrors(child, builder);
            }
            return builder.ToString();
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
            SaveConfigTree(ActiveSWModel, BaseNode, true);
            Close();
        }

        private void ButtonLinksCancelClick(object sender, EventArgs e)
        {
            if (previouslySelectedNode != null)
            {
                SaveLinkDataFromPropertyBoxes(previouslySelectedNode.Link);
            }
            SaveConfigTree(ActiveSWModel, BaseNode, true);
            Close();
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
            SaveConfigTree(ActiveSWModel, BaseNode, false);

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
                Exporter.ExportRobot(exportSTL, meshFormat);

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
            labelRosPackageNameHint.Text = ChineseUiText.Translate(
                "Output: ROS1/" + sanitized + " and ROS2/" + sanitized,
                "\u8f93\u51fa\uff1aROS1/" + sanitized + " \u548c ROS2/" + sanitized);
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
                    out string error))
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
                    logger.Warn("Could not display inertia preview for link " +
                        node.Link.Name + ": " + error);
                    labelInertiaPreviewStatus.Text = ChineseUiText.Translate(
                        "Invalid inertia tensor",
                        "\u65e0\u6548\u7684\u60ef\u6027\u5f20\u91cf");
                    MessageBox.Show(
                        ChineseUiText.Translate(
                            "The inertia overlay cannot be displayed:\r\n",
                            "\u65e0\u6cd5\u663e\u793a\u60ef\u6027\u53e0\u52a0\u5c42\uff1a\r\n") + error,
                        ChineseUiText.Translate(
                            "Inertia validation",
                            "\u60ef\u6027\u6821\u9a8c"));
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
        }

        private void MimicCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            bool showControls = (sender as CheckBox).Checked;
            ShowMimicControls(showControls);
            textBoxMimicMultiplier.Text = "1.0";
            textBoxMimicOffset.Text = "0.0";
        }
    }
}
