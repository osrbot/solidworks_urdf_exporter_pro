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
using OSURDF.Core.Model;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
        private Button buttonAutomaticLinkColors;
        private bool collisionPreviewEnabled;
        private bool ownedResourcesDisposed;
        private Font treeNodeRegularFont;
        private Font treeNodeBoldFont;
        private ErrorProvider jointLimitErrorProvider;
        private ErrorProvider materialColorErrorProvider;

        private AssemblyExportForm()
        {
            // Keep the designer and runtime-created controls in the same initial DPI pass.
            SuspendLayout();
            try
            {
                exportSessionDraftStore = new FileExportSessionDraftStore();
                treeSelectionUpdateGuard = new TreeSelectionUpdateGuard();
                InitializeComponent();
                ChineseUiText.Apply(this);
                InitializeLinkCoordinateSystemControls();
                InitializeUsageGuideButton();
                InitializeMaterialIdentityControls();
                InitializeAutomaticLinkColorControls();
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
                InitializeModernUi();
                InitializeJointLimitValidation();
            }
            finally
            {
                ResumeLayout(true);
            }
            ApplyModernInitialScaleBounds();
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

        private void InitializeMaterialIdentityControls()
        {
            comboBoxMaterials.Items.Clear();
            comboBoxMaterials.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxMaterials.Enabled = false;
            comboBoxMaterials.Visible = false;
            label28.Text = ChineseUiText.Translate(
                "URDF material ID (generated from RGBA)",
                "URDF 材质 ID（由 RGBA 自动生成）");
            label29.Text = ChineseUiText.Translate(
                "Appearance color (RGBA)",
                "外观颜色（RGBA）");
            materialColorErrorProvider = new ErrorProvider(components)
            {
                BlinkStyle = ErrorBlinkStyle.NeverBlink,
                ContainerControl = this
            };
            foreach (DomainUpDown input in GetMaterialColorInputs())
            {
                materialColorErrorProvider.SetIconAlignment(
                    input,
                    ErrorIconAlignment.MiddleRight);
                materialColorErrorProvider.SetIconPadding(input, 2);
            }
            UpdateMaterialColorPreview();
        }

        private void SynchronizeMaterialIdFromRgba()
        {
            string materialId = BuildMaterialIdFromRgba(
                domainUpDownRed.Text,
                domainUpDownGreen.Text,
                domainUpDownBlue.Text,
                domainUpDownAlpha.Text);
            comboBoxMaterials.Items.Clear();
            if (!String.IsNullOrEmpty(materialId))
            {
                comboBoxMaterials.Items.Add(materialId);
                comboBoxMaterials.SelectedIndex = 0;
            }
            if (modernMaterialIdTextBox != null)
            {
                modernMaterialIdTextBox.Text = materialId;
            }
        }

        internal static string BuildMaterialIdFromRgba(
            string redText,
            string greenText,
            string blueText,
            string alphaText)
        {
            if (!TryParseRgba(
                redText,
                greenText,
                blueText,
                alphaText,
                out double[] rgba))
            {
                return String.Empty;
            }

            int red = ToColorByte(rgba[0]);
            int green = ToColorByte(rgba[1]);
            int blue = ToColorByte(rgba[2]);
            int alpha = ToColorByte(rgba[3]);
            string canonical = String.Join(
                ",",
                rgba.Select(value => value.ToString(
                    "G17",
                    URDFAttribute.URDFNumberFormat)));
            string digest = BuildShortSha256(canonical);

            return String.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "material_{0:x2}{1:x2}{2:x2}{3:x2}_{4}",
                red,
                green,
                blue,
                alpha,
                digest);
        }

        private static string BuildMaterialIdFromRgba(double[] rgba)
        {
            if (rgba == null || rgba.Length != 4)
            {
                return String.Empty;
            }
            return BuildMaterialIdFromRgba(
                rgba[0].ToString("G17", URDFAttribute.URDFNumberFormat),
                rgba[1].ToString("G17", URDFAttribute.URDFNumberFormat),
                rgba[2].ToString("G17", URDFAttribute.URDFNumberFormat),
                rgba[3].ToString("G17", URDFAttribute.URDFNumberFormat));
        }

        private static string BuildShortSha256(string value)
        {
            byte[] hash;
            using (SHA256 algorithm = SHA256.Create())
            {
                hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
            }
            StringBuilder result = new StringBuilder(12);
            for (int index = 0; index < 6; index++)
            {
                result.Append(hash[index].ToString("x2",
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }

        private void InitializeAutomaticLinkColorControls()
        {
            buttonMaterialColorPick.Left = 478;
            buttonMaterialColorPick.Width = 76;
            buttonAutomaticLinkColors = new Button
            {
                Name = "buttonAutomaticLinkColors",
                Location = new Point(buttonMaterialColorPick.Left, domainUpDownAlpha.Top + 2),
                Size = new Size(buttonMaterialColorPick.Width, buttonMaterialColorPick.Height),
                Text = ChineseUiText.Translate("Auto Links", "自动配色"),
                UseVisualStyleBackColor = true,
                TabIndex = buttonMaterialColorPick.TabIndex + 1
            };
            packagePathToolTip.SetToolTip(
                buttonAutomaticLinkColors,
                ChineseUiText.Translate(
                    "Color every Link deterministically. Link level moves from cool to warm colors; left/right counterparts share a color. Manual RGBA edits remain available.",
                    "按稳定规则为整棵 Link 树配色：层级由冷色过渡到暖色，左右对应 Link 使用相同颜色；之后仍可手动修改 RGBA。"));
            buttonAutomaticLinkColors.Click += ButtonAutomaticLinkColorsClick;
            groupBox4.Controls.Add(buttonAutomaticLinkColors);
            buttonAutomaticLinkColors.BringToFront();
        }

        private void ButtonAutomaticLinkColorsClick(object sender, EventArgs e)
        {
            if (BaseNode == null)
            {
                return;
            }

            LinkNode selectedNode = treeViewLinkProperties.SelectedNode as LinkNode ??
                previouslySelectedNode;
            if (previouslySelectedNode != null)
            {
                SaveLinkDataFromPropertyBoxes(previouslySelectedNode.Link);
            }

            int coloredLinks = ApplyAutomaticLinkColors(BaseNode);
            if (selectedNode != null)
            {
                FillLinkPropertyBoxes(selectedNode.Link);
            }

            if (ActiveSWModel != null && !SaveConfigTree(ActiveSWModel, BaseNode, false))
            {
                logger.Warn("Automatic Link colors were applied in memory but the configuration was not saved.");
                return;
            }
            logger.Info("Applied and saved automatic colors for " + coloredLinks + " Links.");
        }

        internal static int ApplyAutomaticLinkColors(LinkNode root)
        {
            if (root == null)
            {
                return 0;
            }

            int maximumDepth = GetMaximumLinkDepth(root, 0);
            return ApplyAutomaticLinkColors(root, 0, maximumDepth);
        }

        private static int ApplyAutomaticLinkColors(
            LinkNode node,
            int depth,
            int maximumDepth)
        {
            AutomaticLinkColorScheme.Apply(
                node.Link,
                AutomaticLinkColorScheme.GetAssignment(
                    node.Link == null ? String.Empty : node.Link.Name,
                    depth,
                    maximumDepth));
            int count = node.Link == null ? 0 : 1;
            foreach (LinkNode child in node.Nodes)
            {
                count += ApplyAutomaticLinkColors(child, depth + 1, maximumDepth);
            }
            return count;
        }

        private static int GetMaximumLinkDepth(LinkNode node, int depth)
        {
            int maximum = depth;
            foreach (LinkNode child in node.Nodes)
            {
                maximum = Math.Max(maximum, GetMaximumLinkDepth(child, depth + 1));
            }
            return maximum;
        }

        private static void NormalizeGeneratedMaterialIds(LinkNode node)
        {
            if (node == null)
            {
                return;
            }
            if (node.Link != null && node.Link.Visual != null &&
                node.Link.Visual.Material != null)
            {
                node.Link.Visual.Material.Name = BuildMaterialIdFromRgba(
                    node.Link.Visual.Material.Color.GetColor());
            }
            foreach (LinkNode child in node.Nodes)
            {
                NormalizeGeneratedMaterialIds(child);
            }
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
                comboBoxMaterials
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
            InitializeExportTargetControls();
            UpdateRosPackageNameHint();
            Exporter.UpdateReferenceGeometries();
            FillJointTree();
            ApplyMissingRequiredJointLimitDefaultsToTree();
            SelectFirstJointNodeForEditing();
            PrimeModernTabLayoutCaches();
        }

        private void ButtonJointNextClick(object sender, EventArgs e)
        {
            if (!ValidateJointLimitInputs())
            {
                if (modernJointSections != null)
                {
                    modernJointSections.SelectedIndex = 1;
                }
                MessageBox.Show(
                    ChineseUiText.Translate(
                        "Correct the highlighted Joint limit fields before continuing.",
                        "请先修正高亮的 Joint 限位字段，再继续。"),
                    ChineseUiText.Translate(
                        "Joint limit validation",
                        "Joint 限位校验"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            if (!(previouslySelectedNode == null || previouslySelectedNode.Link.Joint == null))
            {
                SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link);
            }
            if (ResolveAutomaticJointSuggestions())
            {
                return;
            }
            string errors = CheckJointsForErrors();
            if (!string.IsNullOrWhiteSpace(errors))
            {
                string message = "The following joints contain invalid or missing fields, please " +
                    "address them before continuing\r\n\r\n" + errors;
                ExportDiagnosticsDialog.ShowFailure(
                    this,
                    ChineseUiText.Translate("URDF Joint errors", "URDF Joint 配置错误"),
                    "ERROR UI_JOINT_CONFIG $.joints: " + message,
                    Logger.GetFileName());
                return;
            }

            ClearPreviousTreeNodeSelection();
            MoveJointTreeNodesToBaseNode();

            using (treeSelectionUpdateGuard.Suppress())
            {
                FillLinkTree();
                treeViewLinkProperties.SelectedNode = BaseNode;
            }
            DisplayLinkNode(BaseNode);
            ResetLinkPanelScroll();
            ShowModernAssemblyPage(ModernAssemblyPage.Link);
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

        private bool ResolveAutomaticJointSuggestions()
        {
            List<string> suggestions = new List<string>();
            List<string> failures = new List<string>();
            foreach (LinkNode child in treeViewJointTree.Nodes)
            {
                ResolveAutomaticJointSuggestions(
                    BaseNode.Link,
                    child,
                    suggestions,
                    failures);
            }
            if (suggestions.Count == 0 && failures.Count == 0)
            {
                return false;
            }

            if (previouslySelectedNode != null)
            {
                FillJointPropertyBoxes(previouslySelectedNode.Link);
            }
            StringBuilder message = new StringBuilder();
            if (suggestions.Count > 0)
            {
                message.AppendLine(ChineseUiText.Translate(
                    "Mate assist produced provisional suggestions:",
                    "Mate 辅助已生成待确认建议："));
                foreach (string suggestion in suggestions)
                {
                    message.AppendLine("  " + suggestion);
                }
                message.AppendLine();
                message.AppendLine(ChineseUiText.Translate(
                    "Review every listed Joint before continuing. A rotational DOF is proposed as continuous because CAD motion alone cannot prove whether a bounded revolute limit is intended. Open each suggested Joint, choose the explicit URDF type, add limits where required, and then click Next again.",
                    "继续前请逐个检查上述 Joint。旋转自由度暂按 continuous 建议，因为 CAD 运动本身无法证明是否需要有界 revolute 限位。请打开每个建议 Joint，明确选择 URDF 类型，按需填写限位，然后再次点击下一步。"));
            }
            if (failures.Count > 0)
            {
                if (message.Length > 0)
                {
                    message.AppendLine();
                }
                message.AppendLine(ChineseUiText.Translate(
                    "Mate assist could not classify these Joints; select their types and reference geometry manually:",
                    "Mate 辅助无法识别以下 Joint；请手动选择类型和参考几何："));
                foreach (string failure in failures)
                {
                    message.AppendLine("  " + failure);
                }
            }
            MessageBox.Show(
                message.ToString().TrimEnd(),
                ChineseUiText.Translate("Review Mate suggestions", "检查 Mate 建议"),
                MessageBoxButtons.OK,
                failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            return true;
        }

        private void ResolveAutomaticJointSuggestions(
            Link parent,
            LinkNode node,
            ICollection<string> suggestions,
            ICollection<string> failures)
        {
            if (node == null || node.Link == null)
            {
                return;
            }
            Joint joint = node.Link.Joint;
            if (joint != null && Joint.IsAutomaticType(joint.Type))
            {
                Joint snapshot = new Joint();
                snapshot.SetElement(joint);
                try
                {
                    if (Exporter.EstimateGlobalJointFromComponents(parent, node.Link))
                    {
                        suggestions.Add(joint.Name + ": " + joint.Type);
                    }
                    else
                    {
                        joint.SetElement(snapshot);
                        failures.Add(joint.Name);
                    }
                }
                catch (Exception exception)
                {
                    joint.SetElement(snapshot);
                    failures.Add(joint.Name);
                    logger.Warn("Mate suggestion failed for Joint " + joint.Name, exception);
                }
            }
            foreach (LinkNode child in node.Nodes)
            {
                ResolveAutomaticJointSuggestions(
                    node.Link,
                    child,
                    suggestions,
                    failures);
            }
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
            AppendJointLimitSemanticErrors(node.Link.Joint, builder);
            if (JointConfigurationPolicy.RequiresUserConfirmation(node.Link.Joint))
            {
                builder.Append(node.Link.Joint.ConfigurationSource == "solidworks_mate_suggestion"
                        ? "Unconfirmed Mate suggestion: "
                        : "Unconfirmed Joint configuration: ")
                    .Append(node.Link.Joint.Name)
                    .Append(" (open this Joint and explicitly select its URDF type)\r\n");
            }

            foreach (LinkNode child in node.Nodes)
            {
                CheckJointsForErrors(child, builder);
            }
            return builder;
        }

        private void ApplyMissingRequiredJointLimitDefaultsToTree()
        {
            foreach (LinkNode node in treeViewJointTree.Nodes)
            {
                ApplyMissingRequiredJointLimitDefaultsToTree(node);
            }
        }

        private static void ApplyMissingRequiredJointLimitDefaultsToTree(LinkNode node)
        {
            if (node == null)
            {
                return;
            }

            ApplyMissingRequiredJointLimitDefaults(node.Link == null ? null : node.Link.Joint);
            foreach (LinkNode child in node.Nodes)
            {
                ApplyMissingRequiredJointLimitDefaultsToTree(child);
            }
        }

        private static void ApplyMissingRequiredJointLimitDefaults(Joint joint)
        {
            if (joint == null || joint.Limit == null ||
                !IsMovingOneAxisJoint(JointConfigurationPolicy.Normalize(joint.Type)))
            {
                return;
            }

            if (IsJointValueMissing(() => joint.Limit.Effort))
            {
                joint.Limit.Effort = 1.0;
            }
            if (IsJointValueMissing(() => joint.Limit.Velocity))
            {
                joint.Limit.Velocity = 1.0;
            }
        }

        private static bool IsJointValueMissing(Func<double> valueAccessor)
        {
            try
            {
                valueAccessor();
                return false;
            }
            catch (InvalidCastException)
            {
                return true;
            }
            catch (NullReferenceException)
            {
                return true;
            }
        }

        private static void AppendJointLimitSemanticErrors(
            Joint joint,
            StringBuilder builder)
        {
            string jointType = JointConfigurationPolicy.Normalize(joint.Type);
            double effort;
            double velocity;
            bool hasEffort = TryReadFiniteJointValue(() => joint.Limit.Effort, out effort);
            bool hasVelocity = TryReadFiniteJointValue(() => joint.Limit.Velocity, out velocity);
            if (IsMovingOneAxisJoint(jointType))
            {
                if (!hasEffort || effort <= 0.0)
                {
                    AppendJointFieldError(
                        builder,
                        joint,
                        ChineseUiText.Translate(
                            "effort must be a finite value greater than 0",
                            "effort 必须是大于 0 的有限数值"));
                }
                if (!hasVelocity || velocity <= 0.0)
                {
                    AppendJointFieldError(
                        builder,
                        joint,
                        ChineseUiText.Translate(
                            "velocity must be a finite value greater than 0",
                            "velocity 必须是大于 0 的有限数值"));
                }
            }

            double lower;
            double upper;
            bool hasLower = TryReadFiniteJointValue(() => joint.Limit.Lower, out lower);
            bool hasUpper = TryReadFiniteJointValue(() => joint.Limit.Upper, out upper);
            if (IsBoundedOneAxisJoint(jointType) &&
                (!hasLower || !hasUpper || lower >= upper))
            {
                AppendJointFieldError(
                    builder,
                    joint,
                    ChineseUiText.Translate(
                        "bounded lower/upper limits must be finite and lower must be smaller than upper",
                        "有限位 Joint 的 lower/upper 必须为有限数值，且 lower 必须小于 upper"));
            }

            double softLower;
            double softUpper;
            bool hasSoftLower = TryReadFiniteJointValue(
                () => joint.Safety.SoftLower,
                out softLower);
            bool hasSoftUpper = TryReadFiniteJointValue(
                () => joint.Safety.SoftUpper,
                out softUpper);
            if (hasSoftLower &&
                (!hasLower || !hasUpper || softLower < lower || softLower > upper))
            {
                AppendJointFieldError(
                    builder,
                    joint,
                    ChineseUiText.Translate(
                        "soft lower limit must stay within the hard limits",
                        "软下限必须位于硬限位范围内"));
            }
            if (hasSoftUpper &&
                (!hasLower || !hasUpper || softUpper < lower || softUpper > upper))
            {
                AppendJointFieldError(
                    builder,
                    joint,
                    ChineseUiText.Translate(
                        "soft upper limit must stay within the hard limits",
                        "软上限必须位于硬限位范围内"));
            }
            if (hasSoftLower && hasSoftUpper && softLower > softUpper)
            {
                AppendJointFieldError(
                    builder,
                    joint,
                    ChineseUiText.Translate(
                        "soft lower limit must not exceed soft upper limit",
                        "软下限不能大于软上限"));
            }
        }

        private static bool TryReadFiniteJointValue(
            Func<double> valueAccessor,
            out double value)
        {
            value = 0.0;
            try
            {
                value = valueAccessor();
                return !Double.IsNaN(value) && !Double.IsInfinity(value);
            }
            catch (InvalidCastException)
            {
                return false;
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        private static void AppendJointFieldError(
            StringBuilder builder,
            Joint joint,
            string message)
        {
            builder.Append(String.IsNullOrWhiteSpace(joint.Name) ? "<unnamed>" : joint.Name)
                .Append(": ")
                .Append(message)
                .Append(".\r\n");
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
                SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link);
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
            ClearPreviousTreeNodeSelection();
            FillJointTree();
            ShowModernAssemblyPage(ModernAssemblyPage.Joint);
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
            ClearPreviews();
            logger.Info("Completing URDF export");
            Exporter.RosPackageName = URDFPackage.SanitizePackageName(textBoxRosPackageName.Text);
            textBoxRosPackageName.Text = Exporter.RosPackageName;
            UpdateRosPackageNameHint();
            Exporter.ExportTargets = exportSTL
                ? CaptureExportTargetOptions()
                : ExportTargetOptions.LegacyCompatibilityDefaults();
            if (!exportSTL)
            {
                logger.Info("Using the lightweight URDF-only compatibility path; derived target packages require a complete mesh export.");
            }
            IList<ExportTargetValidationFinding> targetErrors =
                Exporter.ExportTargets.ValidateFindings();
            if (targetErrors.Count > 0)
            {
                ExportDiagnosticsDialog.ShowValidation(
                    this,
                    ChineseUiText.Translate("Output profile errors", "输出配置错误"),
                    targetErrors,
                    Logger.GetFileName());
                return;
            }

            // Saving selected node
            LinkNode node = (LinkNode)treeViewLinkProperties.SelectedNode;
            if (node != null)
            {
                SaveLinkDataFromPropertyBoxes(node.Link);
            }
            NormalizeGeneratedMaterialIds(BaseNode);

            ApplyEditedMeshReductionToExportTree();
            string jointErrors = CheckJointsForErrors(BaseNode);
            if (!string.IsNullOrWhiteSpace(jointErrors))
            {
                logger.Info("Joint errors encountered:\n " + jointErrors);

                string message = "The following joints contain invalid or duplicate " +
                    "properties. Please address before continuing\r\n\r\n" + jointErrors;
                ExportDiagnosticsDialog.ShowFailure(
                    this,
                    ChineseUiText.Translate("URDF Joint errors", "URDF Joint 配置错误"),
                    "ERROR UI_JOINT_CONFIG $.joints: " + message,
                    Logger.GetFileName());
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
                ExportDiagnosticsDialog.ShowFailure(
                    this,
                    ChineseUiText.Translate("URDF Link errors", "URDF Link 配置错误"),
                    "ERROR UI_LINK_CONFIG $.links: " + message,
                    Logger.GetFileName());
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
                    "Select the export root for the selected target packages",
                    "\u9009\u62e9\u5df2\u9009\u76ee\u6807\u529f\u80fd\u5305\u7684\u5bfc\u51fa\u6839\u76ee\u5f55")
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
                using (ExportProgressSession progressSession = new ExportProgressSession(this))
                {
                    EventHandler<ExportProgressEventArgs> progressHandler =
                        (progressSender, progress) => progressSession.UpdateProgress(progress);
                    try
                    {
                        Enabled = false;
                        progressSession.Start();
                        Exporter.ExportProgressChanged += progressHandler;
                        exportSucceeded = Exporter.ExportRobot(exportSTL, meshFormat);
                    }
                    finally
                    {
                        Enabled = true;
                        Exporter.ExportProgressChanged -= progressHandler;
                    }
                }

                if (Exporter.LastExportSummary != null && Exporter.LastExportSummary.Targets.Count > 0)
                {
                    ExportResultSummary summary = Exporter.LastExportSummary;
                    ExportResultsDialog.ShowResults(this, summary, Logger.GetFileName());
                    if (!summary.HasFailures)
                    {
                        CloseWithoutRecoveryDraft();
                    }
                    return;
                }

                if (!exportSucceeded)
                {
                    logger.Error(Exporter.ExportErrorWhy);
                    ExportDiagnosticsDialog.ShowFailure(
                        this,
                        ChineseUiText.Translate("URDF export failed", "URDF 导出失败"),
                        Exporter.ExportErrorWhy,
                        Logger.GetFileName());
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
            UpdateRosPackageNameHintCore(true);
        }

        private void UpdateRosPackageNameHintForTargetChange()
        {
            // Target choices only change text in the fixed-height, ellipsized hint.
            UpdateRosPackageNameHintCore(false);
        }

        private void UpdateRosPackageNameHintCore(bool updateLayout)
        {
            string sanitized = URDFPackage.SanitizePackageName(textBoxRosPackageName.Text);
            string robotName = Exporter == null
                ? sanitized
                : URDFPackage.SanitizePackageName(Exporter.PackageName);
            if (string.IsNullOrWhiteSpace(robotName))
            {
                robotName = sanitized;
            }
            List<string> paths = new List<string>();
            if (modernRos1CheckBox == null || modernRos1CheckBox.Checked)
            {
                paths.Add("ROS1/" + sanitized);
            }
            if (modernRos2CheckBox == null || modernRos2CheckBox.Checked)
            {
                paths.Add("ROS2/" + sanitized);
            }
            if (modernUsdAssetCheckBox != null && modernUsdAssetCheckBox.Checked)
            {
                paths.Add("USD/" + sanitized);
            }
            if (modernMjcfAssetCheckBox != null && modernMjcfAssetCheckBox.Checked)
            {
                paths.Add("MuJoCo/" + robotName);
            }
            string hint = paths.Count == 0
                ? ChineseUiText.Translate("No target selected", "未选择输出目标")
                : string.Join(" | ", paths);
            string toolTip = paths.Count == 0
                ? ChineseUiText.Translate(
                    "Select an output target to see its destination.",
                    "请选择输出目标以查看对应目录。")
                : string.Join(" | ", paths);
            bool rebuildLayout = updateLayout && !String.Equals(
                labelRosPackageNameHint.Text,
                hint,
                StringComparison.Ordinal);
            if (rebuildLayout)
            {
                InvalidateModernModelPageLayout();
            }
            try
            {
                labelRosPackageNameHint.Text = hint;
                packagePathToolTip.SetToolTip(labelRosPackageNameHint, toolTip);
            }
            finally
            {
                if (rebuildLayout)
                {
                    RebuildModernModelPageLayout();
                }
            }
        }

        private void InitializeExportTargetControls()
        {
            if (modernRos2CheckBox == null)
            {
                return;
            }
            ExportTargetOptions existing = Exporter.ExportTargets;
            bool restore = existing != null && existing.UseV2Pipeline;
            ExportTargetOptions options = restore
                ? existing
                : ExportTargetOptions.RecommendedDefaults(Exporter.RosPackageName);
            loadingModernExportTargets = true;
            try
            {
                modernRos1CheckBox.Checked = options.ExportRos1Legacy;
                modernRos2CheckBox.Checked = options.ExportRos2;
                modernUsdAssetCheckBox.Checked = options.ExportUsdAsset;
                modernMjcfAssetCheckBox.Checked = options.ExportMjcfAsset;
                modernUsdSimulationProfile =
                    ExportTargetOptions.CloneUsdSimulation(options.UsdSimulation);
                if (modernUsdSettingsButton != null)
                {
                    modernUsdSettingsButton.Enabled = options.ExportUsdAsset;
                }
                modernPackageVersionTextBox.Text = options.PackageVersion;
                modernPackageDescriptionTextBox.Text = options.Description;
                modernMaintainerNameTextBox.Text = options.MaintainerName;
                modernMaintainerEmailTextBox.Text = options.MaintainerEmail;
                modernModelLicenseTextBox.Text = options.ModelLicense;
                modernModelAuthorTextBox.Text = options.ModelAuthor;
            }
            finally
            {
                loadingModernExportTargets = false;
            }
            if (modernUsdSettingsButton != null)
            {
                packagePathToolTip.SetToolTip(
                    modernUsdSettingsButton,
                    ChineseUiText.Translate(
                        "Configure base semantics, self-collision, robot type, and explicit one-DOF Joint drive intent. No Isaac Sim version is required.",
                        "配置基座语义、自碰撞、机器人类型及单自由度 Joint 的显式驱动意图；无需填写 Isaac Sim 版本。"));
            }
            packagePathToolTip.SetToolTip(
                modernModelLicenseTextBox,
                ChineseUiText.Translate(
                    "NOASSERTION means the model license has not been confirmed. Review it before publishing.",
                    "NOASSERTION 表示模型许可证尚未确认；公开发布前必须审核。"));
            SynchronizeAssetMeshFormatControls();
            UpdateRosPackageNameHintForTargetChange();
        }

        private ExportTargetOptions CaptureExportTargetOptions()
        {
            ExportTargetOptions options = new ExportTargetOptions
            {
                UseV2Pipeline = true,
                ExportRos1Legacy = modernRos1CheckBox.Checked,
                ExportRos2 = modernRos2CheckBox.Checked,
                ExportUsdAsset = modernUsdAssetCheckBox.Checked,
                ExportMjcfAsset = modernMjcfAssetCheckBox.Checked,
                PackageVersion = modernPackageVersionTextBox.Text.Trim(),
                Description = modernPackageDescriptionTextBox.Text.Trim(),
                MaintainerName = modernMaintainerNameTextBox.Text.Trim(),
                MaintainerEmail = modernMaintainerEmailTextBox.Text.Trim(),
                ModelLicense = modernModelLicenseTextBox.Text.Trim(),
                ModelAuthor = modernModelAuthorTextBox.Text.Trim(),
                UsdSimulation = ExportTargetOptions.CloneUsdSimulation(
                    modernUsdSimulationProfile)
            };
            return options;
        }

        private void ModernUsdSettingsButtonClick(object sender, EventArgs e)
        {
            if (openUsdSettingsDialog == null || openUsdSettingsDialog.IsDisposed)
            {
                openUsdSettingsDialog = new OpenUsdSettingsDialog();
            }
            openUsdSettingsDialog.LoadSettings(
                modernUsdSimulationProfile,
                BuildOpenUsdJointDescriptors(BaseNode));
            openUsdSettingsDialog.PrepareForOwner(this);
            if (openUsdSettingsDialog.ShowDialog(this) == DialogResult.OK)
            {
                modernUsdSimulationProfile = ExportTargetOptions.CloneUsdSimulation(
                    openUsdSettingsDialog.Settings);
            }
        }

        internal static IList<OpenUsdJointDescriptor> BuildOpenUsdJointDescriptors(
            LinkNode root)
        {
            List<OpenUsdJointDescriptor> result =
                new List<OpenUsdJointDescriptor>();
            if (root == null)
            {
                return result;
            }

            Queue<LinkNode> pending = new Queue<LinkNode>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                LinkNode node = pending.Dequeue();
                foreach (LinkNode child in node.Nodes)
                {
                    pending.Enqueue(child);
                }
                if (node.IsBaseNode || node.Link == null || node.Link.Joint == null)
                {
                    continue;
                }
                Joint joint = node.Link.Joint;
                if (!(String.Equals(joint.Type, "continuous", StringComparison.Ordinal) ||
                      String.Equals(joint.Type, "revolute", StringComparison.Ordinal) ||
                      String.Equals(joint.Type, "prismatic", StringComparison.Ordinal)))
                {
                    continue;
                }
                result.Add(new OpenUsdJointDescriptor
                {
                    Name = joint.Name,
                    Type = joint.Type,
                    EffortLimit = ReadPositiveJointLimit(
                        delegate { return joint.Limit.Effort; }),
                    VelocityLimit = ReadPositiveJointLimit(
                        delegate { return joint.Limit.Velocity; })
                });
            }
            return result;
        }

        private static double? ReadPositiveJointLimit(Func<double> readValue)
        {
            try
            {
                double value = readValue();
                return !Double.IsNaN(value) && !Double.IsInfinity(value) && value > 0.0
                    ? (double?)value
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void LinkCoordinateSystemSelectionChangeCommitted(object sender, EventArgs e)
        {
            LinkNode node = treeViewLinkProperties.SelectedNode as LinkNode;
            if (node == null || node.Link == null || node.Link.isFixedFrame)
            {
                return;
            }

            CadFeatureReference previousCoordinateSystem =
                node.Link.FrameReference == null
                    ? CadFeatureReference.Automatic(ReferenceGeometryKind.CoordinateSystem)
                    : node.Link.FrameReference.Clone();
            CadFeatureReference selectedCoordinateSystem = ReadReferenceComboBox(
                comboBoxLinkCoordinateSystem,
                previousCoordinateSystem,
                ReferenceGeometryKind.CoordinateSystem);
            if (previousCoordinateSystem.Equals(selectedCoordinateSystem))
            {
                return;
            }

            bool refreshPreview = inertiaPreview != null && inertiaPreview.IsVisible;
            if (refreshPreview) ClearInertiaPreview();
            try
            {
                SaveLinkDataFromPropertyBoxes(node.Link);
                // Save the other edited fields, but let the exporter change the frame
                // transactionally so a failed SolidWorks recomputation can roll back.
                node.Link.FrameReference = previousCoordinateSystem;
                Exporter.RecomputeLinkCoordinateSystem(
                    node,
                    selectedCoordinateSystem);
                FillLinkPropertyBoxes(node.Link);
                if (refreshPreview) ButtonShowInertiaPreviewClick(this, EventArgs.Empty);
                logger.Info("Changed Link coordinate system for " + node.Link.Name +
                    " from " + Exporter.GetReferenceDisplayLabel(previousCoordinateSystem) +
                    " to " + Exporter.GetReferenceDisplayLabel(selectedCoordinateSystem));
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
            ClearPreviews();
            if (previouslySelectedNode != null)
            {
                SaveLinkDataFromPropertyBoxes(previouslySelectedNode.Link);
                previouslySelectedNode.NodeFont = GetTreeNodeFont(false);
            }
            node.NodeFont = GetTreeNodeFont(true);
            node.Text = node.Text;
            SelectLinkComponents(ActiveSWModel, node.Link.SWComponents,
                component => new DispatchWrapper(component));
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

        private void InitializeJointLimitValidation()
        {
            jointLimitErrorProvider = new ErrorProvider(components)
            {
                BlinkStyle = ErrorBlinkStyle.NeverBlink
            };
            jointLimitErrorProvider.ContainerControl = this;

            TextBox[] inputs = new TextBox[]
            {
                textBoxLimitLower,
                textBoxLimitUpper,
                textBoxLimitEffort,
                textBoxLimitVelocity,
                textBoxSoftLower,
                textBoxSoftUpper,
                textBoxKPosition,
                textBoxKVelocity
            };
            foreach (TextBox input in inputs)
            {
                input.TextChanged += JointLimitInputChanged;
                jointLimitErrorProvider.SetIconAlignment(input, ErrorIconAlignment.MiddleRight);
                jointLimitErrorProvider.SetIconPadding(input, 2);
            }
            textBoxLimitEffort.Leave += JointRequiredLimitInputLeave;
            textBoxLimitVelocity.Leave += JointRequiredLimitInputLeave;
            ValidateJointLimitInputs();
        }

        private void JointLimitInputChanged(object sender, EventArgs e)
        {
            if (!AutoUpdatingForm)
            {
                ValidateJointLimitInputs();
            }
        }

        private void JointRequiredLimitInputLeave(object sender, EventArgs e)
        {
            ValidateJointLimitInputs();
        }

        private bool ValidateJointLimitInputs()
        {
            if (jointLimitErrorProvider == null)
            {
                return true;
            }

            TextBox[] inputs = new TextBox[]
            {
                textBoxLimitLower,
                textBoxLimitUpper,
                textBoxLimitEffort,
                textBoxLimitVelocity,
                textBoxSoftLower,
                textBoxSoftUpper,
                textBoxKPosition,
                textBoxKVelocity
            };
            foreach (TextBox input in inputs)
            {
                jointLimitErrorProvider.SetError(input, String.Empty);
            }

            string jointType = JointConfigurationPolicy.Normalize(
                ChineseUiText.JointTypeValue(comboBoxJointType.Text));
            bool moving = IsMovingOneAxisJoint(jointType);
            bool bounded = IsBoundedOneAxisJoint(jointType);
            double lower;
            double upper;
            double effort;
            double velocity;
            double softLower;
            double softUpper;

            bool hasLower = ValidateFiniteInput(textBoxLimitLower, out lower);
            bool hasUpper = ValidateFiniteInput(textBoxLimitUpper, out upper);
            bool hasEffort = ValidateFiniteInput(textBoxLimitEffort, out effort);
            bool hasVelocity = ValidateFiniteInput(textBoxLimitVelocity, out velocity);
            bool hasSoftLower = ValidateFiniteInput(textBoxSoftLower, out softLower);
            bool hasSoftUpper = ValidateFiniteInput(textBoxSoftUpper, out softUpper);
            ValidateFiniteInput(textBoxKPosition, out _);
            ValidateFiniteInput(textBoxKVelocity, out _);

            if (moving)
            {
                ValidateRequiredPositiveInput(textBoxLimitEffort, hasEffort, effort);
                ValidateRequiredPositiveInput(textBoxLimitVelocity, hasVelocity, velocity);
            }
            if (bounded)
            {
                ValidateRequiredFiniteInput(textBoxLimitLower, hasLower);
                ValidateRequiredFiniteInput(textBoxLimitUpper, hasUpper);
                if (hasLower && hasUpper && lower >= upper)
                {
                    string error = ChineseUiText.Translate(
                        "Lower limit must be smaller than upper limit.",
                        "下限必须小于上限。");
                    SetJointLimitError(textBoxLimitLower, error);
                    SetJointLimitError(textBoxLimitUpper, error);
                }
            }

            if (hasSoftLower)
            {
                if (!hasLower || !hasUpper)
                {
                    SetJointLimitError(
                        textBoxSoftLower,
                        ChineseUiText.Translate(
                            "Set valid hard lower and upper limits first.",
                            "请先填写有效的硬下限和硬上限。"));
                }
                else if (softLower < lower || softLower > upper)
                {
                    SetJointLimitError(
                        textBoxSoftLower,
                        ChineseUiText.Translate(
                            "Soft lower limit must stay within the hard limits.",
                            "软下限必须位于硬限位范围内。"));
                }
            }
            if (hasSoftUpper)
            {
                if (!hasLower || !hasUpper)
                {
                    SetJointLimitError(
                        textBoxSoftUpper,
                        ChineseUiText.Translate(
                            "Set valid hard lower and upper limits first.",
                            "请先填写有效的硬下限和硬上限。"));
                }
                else if (softUpper < lower || softUpper > upper)
                {
                    SetJointLimitError(
                        textBoxSoftUpper,
                        ChineseUiText.Translate(
                            "Soft upper limit must stay within the hard limits.",
                            "软上限必须位于硬限位范围内。"));
                }
            }
            if (hasSoftLower && hasSoftUpper && softLower > softUpper)
            {
                string error = ChineseUiText.Translate(
                    "Soft lower limit must not exceed soft upper limit.",
                    "软下限不能大于软上限。");
                SetJointLimitError(textBoxSoftLower, error);
                SetJointLimitError(textBoxSoftUpper, error);
            }

            return inputs.All(input =>
                String.IsNullOrEmpty(jointLimitErrorProvider.GetError(input)));
        }

        private bool ValidateFiniteInput(TextBox input, out double value)
        {
            value = 0.0;
            if (String.IsNullOrWhiteSpace(input.Text))
            {
                return false;
            }
            if (!Double.TryParse(
                    input.Text,
                    URDFAttribute.URDFNumberStyle,
                    URDFAttribute.URDFNumberFormat,
                    out value) ||
                Double.IsNaN(value) ||
                Double.IsInfinity(value))
            {
                SetJointLimitError(
                    input,
                    ChineseUiText.Translate(
                        "Enter a finite number.",
                        "请输入有限数值。"));
                return false;
            }
            return true;
        }

        private void ValidateRequiredFiniteInput(TextBox input, bool hasFiniteValue)
        {
            if (!hasFiniteValue && String.IsNullOrWhiteSpace(input.Text))
            {
                SetJointLimitError(
                    input,
                    ChineseUiText.Translate(
                        "This bounded Joint requires a finite limit.",
                        "有限位 Joint 必须填写有限数值。"));
            }
        }

        private void ValidateRequiredPositiveInput(
            TextBox input,
            bool hasFiniteValue,
            double value)
        {
            if (!hasFiniteValue && String.IsNullOrWhiteSpace(input.Text))
            {
                SetJointLimitError(
                    input,
                    ChineseUiText.Translate(
                        "Enter a finite value greater than 0.",
                        "请输入大于 0 的有限数值。"));
            }
            else if (hasFiniteValue && value <= 0.0)
            {
                SetJointLimitError(
                    input,
                    ChineseUiText.Translate(
                        "Value must be greater than 0.",
                        "数值必须大于 0。"));
            }
        }

        private void SetJointLimitError(Control input, string message)
        {
            if (String.IsNullOrEmpty(jointLimitErrorProvider.GetError(input)))
            {
                jointLimitErrorProvider.SetError(input, message);
            }
        }

        private static bool IsMovingOneAxisJoint(string jointType)
        {
            return jointType == "revolute" ||
                jointType == "continuous" ||
                jointType == "prismatic";
        }

        private static bool IsBoundedOneAxisJoint(string jointType)
        {
            return jointType == "revolute" || jointType == "prismatic";
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
            if (!AutoUpdatingForm && !updatingMaterialColorControls)
            {
                ValidateMaterialColorInputs();
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
            SynchronizeMaterialIdFromRgba();
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
            if (!TryParseNormalizedColorChannel(text, out double normalized))
            {
                return false;
            }
            channel = ToColorByte(normalized);
            return true;
        }

        private bool ValidateMaterialColorInputs()
        {
            if (materialColorErrorProvider == null)
            {
                return true;
            }

            string message = ChineseUiText.Translate(
                "RGBA channels must be finite values from 0 through 1.",
                "RGBA 通道必须是 0 到 1 之间的有限数值。");
            bool valid = true;
            foreach (DomainUpDown input in GetMaterialColorInputs())
            {
                bool inputValid = TryParseNormalizedColorChannel(
                    input.Text,
                    out _);
                materialColorErrorProvider.SetError(
                    input,
                    inputValid ? String.Empty : message);
                valid &= inputValid;
            }
            return valid;
        }

        private DomainUpDown[] GetMaterialColorInputs()
        {
            return new[]
            {
                domainUpDownRed,
                domainUpDownGreen,
                domainUpDownBlue,
                domainUpDownAlpha
            };
        }

        private bool TryReadMaterialRgba(out double[] rgba)
        {
            return TryParseRgba(
                domainUpDownRed.Text,
                domainUpDownGreen.Text,
                domainUpDownBlue.Text,
                domainUpDownAlpha.Text,
                out rgba);
        }

        private static bool TryParseRgba(
            string redText,
            string greenText,
            string blueText,
            string alphaText,
            out double[] rgba)
        {
            rgba = null;
            if (!TryParseNormalizedColorChannel(redText, out double red) ||
                !TryParseNormalizedColorChannel(greenText, out double green) ||
                !TryParseNormalizedColorChannel(blueText, out double blue) ||
                !TryParseNormalizedColorChannel(alphaText, out double alpha))
            {
                return false;
            }
            rgba = new[] { red, green, blue, alpha };
            return true;
        }

        private static bool TryParseNormalizedColorChannel(
            string text,
            out double normalized)
        {
            if (!Double.TryParse(
                text,
                URDFAttribute.URDFNumberStyle,
                URDFAttribute.URDFNumberFormat,
                out normalized) ||
                Double.IsNaN(normalized) ||
                Double.IsInfinity(normalized) ||
                normalized < 0.0 ||
                normalized > 1.0)
            {
                normalized = 0.0;
                return false;
            }
            return true;
        }

        private static int ToColorByte(double normalized)
        {
            return (int)Math.Round(normalized * 255.0);
        }

        private static string ColorChannelToText(int channel)
        {
            double normalized = channel / 255.0;
            return normalized.ToString("G5", URDFAttribute.URDFNumberFormat);
        }

        private void InertiaMatrixOffDiagonalTextChanged(object sender, EventArgs e)
        {
            if (!AutoUpdatingForm) UpdateInertiaMatrixMirrorBoxes();
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
                    Exporter.GetCoordinateSystemTransform(node.Link.FrameReference);
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
                    SetModernLinkStatusText(
                        labelInertiaPreviewStatus,
                        String.Format(
                            ChineseUiText.Translate(
                                "Equivalent cuboid X / Y / Z: {0:0.#}/{1:0.#}/{2:0.#} mm",
                                "等效长方体 X / Y / Z：{0:0.#}/{1:0.#}/{2:0.#} mm"),
                            ellipsoid.EquivalentBoxDimensions[0] * 1000.0,
                            ellipsoid.EquivalentBoxDimensions[1] * 1000.0,
                            ellipsoid.EquivalentBoxDimensions[2] * 1000.0));
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
                    SetModernLinkStatusText(
                        labelInertiaPreviewStatus,
                        physicalInertiaInvalid
                            ? ChineseUiText.Translate(
                                "Invalid physical inertia",
                                "\u7269\u7406\u60ef\u6027\u975e\u6cd5")
                            : ChineseUiText.Translate(
                                "Inertia overlay display failed",
                                "\u60ef\u6027\u53e0\u52a0\u5c42\u663e\u793a\u5931\u8d25"));
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
                    Exporter.GetCoordinateSystemTransform(node.Link.FrameReference);
                if (collisionPreview.Show(
                    node.Link,
                    GetSelectedCollisionStrategy(),
                    coordinateTransform,
                    out string status,
                    out string error))
                {
                    buttonShowCollisionPreview.Text = collisionPreview.IsVisible
                        ? ChineseUiText.Translate("Hide collision overlay", "隐藏碰撞体")
                        : ChineseUiText.Translate("Refresh collision", "刷新碰撞体");
                    SetModernLinkStatusText(labelCollisionPreviewStatus, status);
                    return;
                }

                if (!String.IsNullOrWhiteSpace(error))
                {
                    logger.Warn("Collision preview failed for link " + node.Link.Name + ": " + error);
                }
                buttonShowCollisionPreview.Text = ChineseUiText.Translate(
                    "Refresh collision",
                    "刷新碰撞体");
                SetModernLinkStatusText(
                    labelCollisionPreviewStatus,
                    String.IsNullOrWhiteSpace(error) ? status : error);
            }
            catch (Exception exception)
            {
                collisionPreview.Hide();
                SetModernLinkStatusText(
                    labelCollisionPreviewStatus,
                    ChineseUiText.Translate(
                        "Collision preview failed",
                        "碰撞体预览失败"));
                logger.Warn("Could not refresh collision preview for link " + node.Link.Name, exception);
            }
        }

        private void ClearPreviews()
        {
            bool needsRedraw = (inertiaPreview != null && inertiaPreview.IsVisible) ||
                (collisionPreview != null && collisionPreview.IsVisible);
            ClearPreviews(needsRedraw,
                () => ClearInertiaPreview(false),
                () => ClearCollisionPreview(false),
                () => { if (ActiveSWModel != null) ActiveSWModel.GraphicsRedraw2(); });
        }

        internal static void ClearPreviews(bool needsRedraw,
            Action clearInertia, Action clearCollision, Action redraw)
        {
            // Always attempt both cleanups before the shared redraw, even on failure.
            try
            {
                clearInertia();
            }
            finally
            {
                try
                {
                    clearCollision();
                }
                finally
                {
                    if (needsRedraw) redraw();
                }
            }
        }

        private void ClearCollisionPreview(bool redraw = true)
        {
            collisionPreviewEnabled = false;
            if (collisionPreview != null)
            {
                collisionPreview.Hide(redraw);
            }
            if (buttonShowCollisionPreview != null)
            {
                buttonShowCollisionPreview.Text = ChineseUiText.Translate(
                    "Preview collision",
                    "预览碰撞体");
            }
            if (labelCollisionPreviewStatus != null)
            {
                SetModernLinkStatusText(
                    labelCollisionPreviewStatus,
                    ChineseUiText.Translate(
                        "Overlay is not displayed",
                        "未显示碰撞体叠加层"));
            }
        }

        private void ClearInertiaPreview(bool redraw = true)
        {
            inertiaPreview.Hide(redraw);
            buttonShowInertiaPreview.Text = ChineseUiText.Translate(
                "Show equivalent inertia cuboid",
                "显示惯性等效长方体");
            SetModernLinkStatusText(
                labelInertiaPreviewStatus,
                ChineseUiText.Translate(
                    "Equivalent cuboid X / Y / Z dimensions (mm)",
                    "惯性等效长方体 X / Y / Z 尺寸 (mm)"));
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
            if (openUsdSettingsDialog != null)
            {
                openUsdSettingsDialog.Dispose();
                openUsdSettingsDialog = null;
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
            if (buttonAutomaticLinkColors != null)
            {
                buttonAutomaticLinkColors.Dispose();
            }
            if (treeNodeRegularFont != null)
            {
                treeNodeRegularFont.Dispose();
                treeNodeRegularFont = null;
            }
            if (treeNodeBoldFont != null)
            {
                treeNodeBoldFont.Dispose();
                treeNodeBoldFont = null;
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
            bool editingLink = modernUiInitialized
                ? modernActivePage != ModernAssemblyPage.Joint
                : panelLinkProperties.Visible;
            if (editingLink)
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
                    SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link);
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
            SetModernLinkStatusText(
                labelEstimatedMeshSize,
                ChineseUiText.Translate(
                    "Rough STL estimate: logged on export",
                    "\u7c97\u7565 STL \u4f30\u7b97\uff1a\u5bfc\u51fa\u65f6\u5199\u5165\u65e5\u5fd7"));
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
            if (previouslySelectedNode != null && !previouslySelectedNode.IsBaseNode)
            {
                SaveJointDataFromPropertyBoxes(previouslySelectedNode.Link);
            }
            if (previouslySelectedNode != null)
            {
                previouslySelectedNode.NodeFont = GetTreeNodeFont(false);
            }
            SelectLinkComponents(ActiveSWModel, node.Link.SWComponents,
                component => new DispatchWrapper(component));
            node.NodeFont = GetTreeNodeFont(true);
            node.Text = node.Text;
            FillJointPropertyBoxes(node.Link);
            previouslySelectedNode = node;
        }

        internal static void SelectLinkComponents(
            ModelDoc2 model,
            IList<Component2> components,
            Func<Component2, object> prepareSelection)
        {
            if (components.Count == 0)
            {
                model.ClearSelection2(true);
                return;
            }
            try
            {
                object[] selection = components.Select(prepareSelection).ToArray();
                if (model.Extension.MultiSelect2(selection, false, null) == selection.Length)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                logger.Warn("Bulk Link selection failed; retrying individual components.", exception);
            }

            // A partial batch must be replaced, not appended to or toggled by the fallback.
            model.ClearSelection2(true);
            SelectionMgr manager = model.SelectionManager;
            SelectData data = manager.CreateSelectData();
            data.Mark = -1;
            foreach (Component2 component in components)
            {
                component.Select4(true, data, false);
            }
        }

        private Font GetTreeNodeFont(bool bold)
        {
            if (bold)
            {
                if (treeNodeBoldFont == null)
                {
                    treeNodeBoldFont = new Font(treeViewJointTree.Font, FontStyle.Bold);
                }
                return treeNodeBoldFont;
            }

            if (treeNodeRegularFont == null)
            {
                treeNodeRegularFont = new Font(treeViewJointTree.Font, FontStyle.Regular);
            }
            return treeNodeRegularFont;
        }

        private void ClearPreviousTreeNodeSelection()
        {
            if (previouslySelectedNode != null)
            {
                previouslySelectedNode.NodeFont = GetTreeNodeFont(false);
                previouslySelectedNode = null;
            }
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
                CadFeatureReference frameReference = ReadReferenceComboBox(
                    comboBoxOrigin,
                    null,
                    ReferenceGeometryKind.CoordinateSystem);
                CadFeatureReference axisReference = ReadReferenceComboBox(
                    comboBoxAxis,
                    null,
                    ReferenceGeometryKind.Axis);
                if (frameReference.IsExplicit && axisReference.IsExplicit)
                {
                    double[] Axis = Exporter.EstimateAxis(axisReference);
                    Axis = Exporter.LocalizeAxis(Axis, frameReference);
                    textBoxAxisX.Text = Axis[0].ToString("G5");
                    textBoxAxisY.Text = Axis[1].ToString("G5");
                    textBoxAxisZ.Text = Axis[2].ToString("G5");
                }
            }
        }

        private void ComboBoxOriginSelectionChangeCommitted(object sender, EventArgs e)
        {
            if (AutoUpdatingForm)
            {
                return;
            }

            LinkNode node = treeViewJointTree.SelectedNode as LinkNode;
            if (node == null || node.Link == null || node.Link.isFixedFrame)
            {
                return;
            }

            CadFeatureReference previousCoordinateSystem =
                node.Link.FrameReference == null
                    ? CadFeatureReference.Automatic(ReferenceGeometryKind.CoordinateSystem)
                    : node.Link.FrameReference.Clone();
            CadFeatureReference selectedCoordinateSystem = ReadReferenceComboBox(
                comboBoxOrigin,
                previousCoordinateSystem,
                ReferenceGeometryKind.CoordinateSystem);
            if (previousCoordinateSystem.Equals(selectedCoordinateSystem))
            {
                return;
            }

            try
            {
                SaveJointDataFromPropertyBoxes(node.Link);
                Exporter.RecomputeLinkCoordinateSystem(
                    node,
                    selectedCoordinateSystem);
                FillJointPropertyBoxes(node.Link);
                logger.Info("Changed Link coordinate system from the Joint page for " +
                    node.Link.Name + " from " +
                    Exporter.GetReferenceDisplayLabel(previousCoordinateSystem) +
                    " to " +
                    Exporter.GetReferenceDisplayLabel(selectedCoordinateSystem));
            }
            catch (Exception exception)
            {
                FillJointPropertyBoxes(node.Link);
                logger.Warn(
                    "Could not change Link coordinate system from the Joint page for " +
                    node.Link.Name,
                    exception);
                MessageBox.Show(
                    ChineseUiText.Translate(
                        "The Link coordinate system could not be changed:\r\n",
                        "\u65e0\u6cd5\u4fee\u6539 Link \u5750\u6807\u7cfb\uff1a\r\n") +
                    exception.Message,
                    ChineseUiText.Translate(
                        "Link coordinate system",
                        "Link \u5750\u6807\u7cfb"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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
            if (modernUiInitialized)
            {
                SynchronizeModernMimicLayout();
                return;
            }
            PositionJointFooterControls();
        }

        private void MimicCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            if (modernUiInitialized)
            {
                return;
            }

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
                Math.Max(
                    button.Width,
                    Math.Max(
                        designSize.Width,
                        Math.Max(controlPreferred.Width, preferred.Width + 18))),
                Math.Max(
                    button.Height,
                    Math.Max(designSize.Height, Math.Max(controlPreferred.Height, preferred.Height + 10))));
        }

        private void PositionJointFooterControls()
        {
            if (modernUiInitialized)
            {
                return;
            }

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
            if (modernUiInitialized)
            {
                return;
            }
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
