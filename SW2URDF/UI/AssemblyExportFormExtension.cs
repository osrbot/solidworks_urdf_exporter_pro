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
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    //This source file contains all the non-handler methods for the assembly export form,
    // the ones that are helpers.
    public partial class AssemblyExportForm : Form
    {
        private const string GeneralDisplayFormat = "G5";
        // The preview button saves textbox values before display, so inertia values must round-trip.
        private const string InertiaDisplayFormat = "R";

        private void FillReferenceComboBox(
            ComboBox comboBox,
            ReferenceGeometryKind kind,
            CadFeatureReference selectedReference,
            bool includeAutomatic,
            bool includeNone)
        {
            var choices = new List<CadFeatureReferenceChoice>();
            if (includeAutomatic)
            {
                choices.Add(new CadFeatureReferenceChoice(
                    CadFeatureReference.Automatic(kind),
                    ChineseUiText.Translate("Automatically generate", "自动生成")));
            }

            // The exporter catalog is already cached. Read its current snapshot so a
            // deliberate catalog refresh cannot leave a second, stale UI cache behind.
            IList<ReferenceGeometryEntry> entries = kind == ReferenceGeometryKind.CoordinateSystem
                ? Exporter.GetRefCoordinateSystems()
                : Exporter.GetRefAxes();
            bool selectedReferenceAvailable = false;
            foreach (ReferenceGeometryEntry entry in entries)
            {
                choices.Add(new CadFeatureReferenceChoice(
                    entry.Reference,
                    entry.DisplayLabel));
                selectedReferenceAvailable = selectedReference != null &&
                    entry.Reference.Equals(selectedReference) || selectedReferenceAvailable;
            }

            if (selectedReference != null &&
                selectedReference.IsExplicit &&
                !selectedReferenceAvailable)
            {
                choices.Add(new CadFeatureReferenceChoice(
                    selectedReference,
                    ChineseUiText.Translate("Unavailable reference", "引用不可用")));
            }
            if (includeNone)
            {
                choices.Add(new CadFeatureReferenceChoice(
                    CadFeatureReference.None(kind),
                    ChineseUiText.Translate("None", "无")));
            }

            BindReferenceChoices(comboBox, choices, selectedReference);
        }

        internal static void BindReferenceChoices(
            ComboBox comboBox,
            IList<CadFeatureReferenceChoice> choices,
            CadFeatureReference selectedReference)
        {
            bool unchanged = comboBox.Items.Count == choices.Count;
            for (int index = 0; unchanged && index < choices.Count; index++)
            {
                var current = comboBox.Items[index] as CadFeatureReferenceChoice;
                unchanged = current != null && current.Reference.Equals(choices[index].Reference) &&
                    String.Equals(current.DisplayText, choices[index].DisplayText, StringComparison.Ordinal);
            }

            int selectedIndex = choices.Count == 0 ? -1 : 0;
            for (int index = 0; index < choices.Count; index++)
            {
                if (selectedReference != null && choices[index].Reference.Equals(selectedReference))
                {
                    selectedIndex = index;
                    break;
                }
            }
            if (unchanged)
            {
                if (comboBox.SelectedIndex != selectedIndex) comboBox.SelectedIndex = selectedIndex;
                return;
            }
            comboBox.BeginUpdate();
            try
            {
                comboBox.Items.Clear();
                var items = new object[choices.Count];
                for (int index = 0; index < choices.Count; index++) items[index] = choices[index];
                comboBox.Items.AddRange(items);
                if (comboBox.SelectedIndex != selectedIndex) comboBox.SelectedIndex = selectedIndex;
            }
            finally
            {
                comboBox.EndUpdate();
            }
        }

        private void BindPropertyControls(Control root, Action fill)
        {
            bool wasUpdating = AutoUpdatingForm;
            AutoUpdatingForm = true;
            var layouts = new List<Control>();
            try
            {
                SuspendPropertyLayouts(root, layouts);
                fill();
            }
            finally
            {
                try
                {
                    for (int index = layouts.Count - 1; index >= 0; index--)
                    {
                        layouts[index].ResumeLayout(true);
                    }
                }
                finally
                {
                    AutoUpdatingForm = wasUpdating;
                }
            }
        }

        private static void SuspendPropertyLayouts(Control root, IList<Control> layouts)
        {
            if (root == null) return;
            root.SuspendLayout();
            layouts.Add(root);
            foreach (Control child in root.Controls)
            {
                if (child.HasChildren) SuspendPropertyLayouts(child, layouts);
            }
        }

        private static CadFeatureReference ReadReferenceComboBox(
            ComboBox comboBox,
            CadFeatureReference fallback,
            ReferenceGeometryKind kind)
        {
            CadFeatureReferenceChoice choice =
                comboBox.SelectedItem as CadFeatureReferenceChoice;
            if (choice != null)
            {
                return choice.Reference.Clone();
            }
            return fallback == null
                ? CadFeatureReference.Automatic(kind)
                : fallback.Clone();
        }

        //From the link, this method fills the property boxes on the Link Properties page
        public void FillLinkPropertyBoxes(Link Link)
        {
            BindPropertyControls(panelLinkProperties, () => FillLinkPropertyBoxesCore(Link));
            refreshInertiaAfterEdit = false;
            inertialInputErrors?.Clear();
            UpdateInertialEditingControls(Link);
            UpdateInertiaMatrixMirrorBoxes();
            ValidateMaterialColorInputs();
            UpdateMaterialColorPreview();
        }

        private void FillLinkPropertyBoxesCore(Link Link)
        {
            if (Link.isFixedFrame)
            {
                FillBlank(linkBoxes);
                comboBoxLinkCoordinateSystem.Items.Clear();
            }
            comboBoxLinkCoordinateSystem.Enabled = !Link.isFixedFrame;
            if (!Link.isFixedFrame)
            {
                FillReferenceComboBox(
                    comboBoxLinkCoordinateSystem,
                    ReferenceGeometryKind.CoordinateSystem,
                    Link.FrameReference,
                    true,
                    false);

                //G5: Maximum decimal places to use (not counting exponential notation) is 5
                Link.Visual.Origin.FillBoxes(textBoxVisualOriginX,
                                             textBoxVisualOriginY,
                                             textBoxVisualOriginZ,
                                             textBoxVisualOriginRoll,
                                             textBoxVisualOriginPitch,
                                             textBoxVisualOriginYaw,
                                             GeneralDisplayFormat);

                Link.Inertial.Origin.FillBoxes(textBoxInertialOriginX,
                                               textBoxInertialOriginY,
                                               textBoxInertialOriginZ,
                                               textBoxInertialOriginRoll,
                                               textBoxInertialOriginPitch,
                                               textBoxInertialOriginYaw,
                                               InertiaDisplayFormat);

                Link.Inertial.Mass.FillBoxes(textBoxMass, InertiaDisplayFormat);

                Link.Inertial.Inertia.FillBoxes(textBoxIxx,
                                                textBoxIxy,
                                                textBoxIxz,
                                                textBoxIyy,
                                                textBoxIyz,
                                                textBoxIzz,
                                                InertiaDisplayFormat);

                Link.Visual.Material.FillBoxes(comboBoxMaterials);

                Link.Visual.Material.Color.FillBoxes(domainUpDownRed,
                                                     domainUpDownGreen,
                                                     domainUpDownBlue,
                                                     domainUpDownAlpha,
                                                     GeneralDisplayFormat);

                radioButtonFine.Checked = Link.STLQualityFine;
                radioButtonCourse.Checked = !Link.STLQualityFine;
                double meshReductionRatio = meshReductionRatioEdited
                    ? meshReductionRatioForExport
                    : Link.MeshReductionRatio;
                trackBarMeshReduction.Value = MeshReductionRatioToTrackBarValue(meshReductionRatio);
                UpdateMeshReductionLabel();
                SelectCollisionStrategy(Link.CollisionMeshStrategy);
            }
        }

        //Fills the property boxes on the joint properties page
        public void FillJointPropertyBoxes(Link link)
        {
            BindPropertyControls(modernJointRoot, () => FillJointPropertyBoxesCore(link));
            ValidateJointLimitInputs();
        }

        private void FillJointPropertyBoxesCore(Link link)
        {
            Joint joint = link == null ? null : link.Joint;
            foreach (Control box in jointBoxes)
            {
                if (box != comboBoxAxis) box.Text = String.Empty;
            }
            if (joint == null)
            {
                comboBoxAxis.SelectedIndex = -1;
                comboBoxOrigin.SelectedIndex = -1;
                LimitRequiredLabel.Visible = false;
                AxisRequiredLabel.Visible = false;
                UpdateJointUnitLabels(string.Empty);
                displayedJointType = string.Empty;
                jointUnitInputsResetForCurrentChange = false;
                return;
            }
            if (joint != null) //For the base_link or if none is selected
            {
                LimitRequiredLabel.Visible = IsMovingOneAxisJoint(joint.Type);

                AxisRequiredLabel.Visible = JointConfigurationPolicy.RequiresMotionAxis(joint.Type);

                joint.FillBoxes(textBoxJointName, comboBoxJointType);
                joint.Parent.FillBoxes(labelParent);
                joint.Child.FillBoxes(labelChild);

                //G5: Maximum decimal places to use (not counting exponential notation) is 5

                joint.Origin.FillBoxes(textBoxJointX,
                                       textBoxJointY,
                                       textBoxJointZ,
                                       textBoxJointRoll,
                                       textBoxJointPitch,
                                       textBoxJointYaw,
                                       GeneralDisplayFormat);

                if (JointConfigurationPolicy.RequiresMotionAxis(joint.Type))
                {
                    joint.Axis.FillBoxes(textBoxAxisX, textBoxAxisY, textBoxAxisZ, GeneralDisplayFormat);
                }

                if (joint.Limit != null && joint.Type != "fixed")
                {
                    joint.Limit.FillBoxes(textBoxLimitLower,
                                          textBoxLimitUpper,
                                          textBoxLimitEffort,
                                          textBoxLimitVelocity,
                                          GeneralDisplayFormat);
                }
                if (joint.Calibration != null)
                {
                    joint.Calibration.FillBoxes(textBoxCalibrationRising,
                                                textBoxCalibrationFalling,
                                                GeneralDisplayFormat);
                }

                if (joint.Dynamics != null)
                {
                    joint.Dynamics.FillBoxes(textBoxDamping,
                                             textBoxFriction,
                                             GeneralDisplayFormat);
                }

                if (joint.Safety != null)
                {
                    joint.Safety.FillBoxes(textBoxSoftLower,
                                           textBoxSoftUpper,
                                           textBoxKPosition,
                                           textBoxKVelocity,
                                           GeneralDisplayFormat);
                }
            }

            UpdateJointUnitLabels(joint.Type);
            FillReferenceComboBox(
                comboBoxOrigin,
                ReferenceGeometryKind.CoordinateSystem,
                link.FrameReference,
                true,
                false);
            FillReferenceComboBox(
                comboBoxAxis,
                ReferenceGeometryKind.Axis,
                joint.AxisReference,
                JointConfigurationPolicy.RequiresMotionAxis(joint.Type),
                true);

            // Updating Mimic Element Fields
            List<string> jointNames = Exporter.GetJointNames();
            jointNames.RemoveAll(name => string.Equals(
                name,
                joint.Name,
                StringComparison.Ordinal));

            // We'll be setting this automatically, so unsubscribe callback
            MimicCheckBox.CheckedChanged -= MimicCheckBoxCheckedChanged;
            MimicCheckBox.CheckedChanged -= ModernMimicCheckBoxCheckedChanged;

            MimicJointComboBox.BeginUpdate();
            try
            {
                MimicJointComboBox.Items.Clear();
                MimicJointComboBox.Items.AddRange(jointNames.ToArray());
            }
            finally
            {
                MimicJointComboBox.EndUpdate();
            }
            if (joint.Mimic != null && joint.Mimic.AreRequiredFieldsSatisfied())
            {
                MimicCheckBox.Checked = true;
                ShowMimicControls(true);
                MimicJointComboBox.SelectedIndex =
                    MimicJointComboBox.FindStringExact(joint.Mimic.JointName);
                joint.Mimic.FillBoxes(textBoxMimicMultiplier, textBoxMimicOffset);
            }
            else
            {
                MimicCheckBox.Checked = false;
                ShowMimicControls(false);
            }
            if (modernUiInitialized)
            {
                MimicCheckBox.CheckedChanged += ModernMimicCheckBoxCheckedChanged;
                SynchronizeModernMimicLayout();
            }
            else
            {
                MimicCheckBox.CheckedChanged += MimicCheckBoxCheckedChanged;
            }

            displayedJointType = JointConfigurationPolicy.Normalize(joint.Type);
            jointUnitInputsResetForCurrentChange = false;
        }

        private void UpdateJointUnitLabels(string jointType)
        {
            if (jointType == "revolute" || jointType == "continuous")
            {
                labelLowerLimit.Text = "lower (rad)";
                labelLimitUpper.Text = "upper (rad)";
                labelEffort.Text = "effort (N-m)";
                labelVelocity.Text = "velocity (rad/s)";
                labelFriction.Text = "friction (N-m)";
                labelDamping.Text = "damping (N-m-s/rad)";
                labelSoftLower.Text = "soft lower limit (rad)";
                labelSoftUpper.Text = "soft upper limit (rad)";
                labelKPosition.Text = "k position";
                labelKVelocity.Text = "k velocity";
            }
            else if (jointType == "prismatic")
            {
                labelLowerLimit.Text = "lower (m)";
                labelLimitUpper.Text = "upper (m)";
                labelEffort.Text = "effort (N)";
                labelVelocity.Text = "velocity (m/s)";
                labelFriction.Text = "friction (N)";
                labelDamping.Text = "damping (N-s/m)";
                labelSoftLower.Text = "soft lower limit (m)";
                labelSoftUpper.Text = "soft upper limit (m)";
                labelKPosition.Text = "k position";
                labelKVelocity.Text = "k velocity";
            }
            else
            {
                labelLowerLimit.Text = "lower";
                labelLimitUpper.Text = "upper";
                labelEffort.Text = "effort";
                labelVelocity.Text = "velocity";
                labelFriction.Text = "friction";
                labelDamping.Text = "damping";
                labelSoftLower.Text = "soft lower limit";
                labelSoftUpper.Text = "soft upper limit";
                labelKPosition.Text = "k position";
                labelKVelocity.Text = "k velocity";
            }
            LocalizeDynamicJointLabels();
        }

        private void ComboBoxJointTypeTextChanged(object sender, EventArgs e)
        {
            if (AutoUpdatingForm)
            {
                return;
            }

            string nextType = JointConfigurationPolicy.Normalize(
                ChineseUiText.JointTypeValue(comboBoxJointType.Text));
            if (JointConfigurationPolicy.ChangesMotionUnits(displayedJointType, nextType))
            {
                FillBlank(new Control[]
                {
                    textBoxLimitLower, textBoxLimitUpper,
                    textBoxLimitEffort, textBoxLimitVelocity,
                    textBoxCalibrationRising, textBoxCalibrationFalling,
                    textBoxDamping, textBoxFriction,
                    textBoxSoftLower, textBoxSoftUpper,
                    textBoxKPosition, textBoxKVelocity,
                    textBoxMimicOffset
                });
                jointUnitInputsResetForCurrentChange = true;
            }
            displayedJointType = nextType;
            LimitRequiredLabel.Visible = IsMovingOneAxisJoint(nextType);
            AxisRequiredLabel.Visible = JointConfigurationPolicy.RequiresMotionAxis(nextType);
            UpdateJointUnitLabels(nextType);
            ValidateJointLimitInputs();
        }

        public static void FillBlank(Control[] boxes)
        {
            foreach (Control box in boxes)
            {
                box.Text = "";
            }
        }

        //Converts the text boxes back into values for the link
        public void SaveLinkDataFromPropertyBoxes(Link Link)
        {
            if (!Link.isFixedFrame)
            {
                string previousMaterialName = Link.Visual.Material.Name;
                double[] previousColor = Link.Visual.Material.Color.GetColor();

                CommitInertialInputs(Link);

                Link.Visual.Origin.Update(textBoxVisualOriginX,
                                          textBoxVisualOriginY,
                                          textBoxVisualOriginZ,
                                          textBoxVisualOriginRoll,
                                          textBoxVisualOriginPitch,
                                          textBoxVisualOriginYaw);

                if (TryReadMaterialRgba(out double[] rgba))
                {
                    Link.Visual.Material.Color.SetColor(rgba);
                }
                else
                {
                    Link.Visual.Material.Color.SetColor(previousColor);
                    updatingMaterialColorControls = true;
                    try
                    {
                        Link.Visual.Material.Color.FillBoxes(
                            domainUpDownRed,
                            domainUpDownGreen,
                            domainUpDownBlue,
                            domainUpDownAlpha,
                            "G17");
                    }
                    finally
                    {
                        updatingMaterialColorControls = false;
                    }
                    ValidateMaterialColorInputs();
                }
                SynchronizeMaterialIdFromRgba();
                Link.Visual.Material.Name = comboBoxMaterials.Text;

                if (!String.Equals(previousMaterialName, Link.Visual.Material.Name,
                        StringComparison.Ordinal) ||
                    !ColorsEqual(previousColor, Link.Visual.Material.Color.GetColor()))
                {
                    Link.Visual.Material.AppearanceAutomaticallyResolved = false;
                }

                Link.STLQualityFine = radioButtonFine.Checked;
                Link.MeshReductionRatio = TrackBarValueToMeshReductionRatio(trackBarMeshReduction.Value);
                Link.CollisionMeshStrategy = GetSelectedCollisionStrategy();
                Link.FrameReference = ReadReferenceComboBox(
                    comboBoxLinkCoordinateSystem,
                    Link.FrameReference,
                    ReferenceGeometryKind.CoordinateSystem);
            }
        }

        private static bool ColorsEqual(double[] left, double[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int index = 0; index < left.Length; index++)
            {
                if (Math.Abs(left[index] - right[index]) > 1e-12)
                {
                    return false;
                }
            }
            return true;
        }

        //Saves data from text boxes back into a joint
        public void SaveJointDataFromPropertyBoxes(Link link)
        {
            Joint Joint = link.Joint;
            string previousType = JointConfigurationPolicy.Normalize(Joint.Type);
            string selectedType = JointConfigurationPolicy.Normalize(
                ChineseUiText.JointTypeValue(comboBoxJointType.Text));
            if (JointConfigurationPolicy.ChangesMotionUnits(previousType, selectedType) &&
                !jointUnitInputsResetForCurrentChange)
            {
                FillBlank(new Control[]
                {
                    textBoxLimitLower, textBoxLimitUpper,
                    textBoxLimitEffort, textBoxLimitVelocity,
                    textBoxCalibrationRising, textBoxCalibrationFalling,
                    textBoxDamping, textBoxFriction,
                    textBoxSoftLower, textBoxSoftUpper,
                    textBoxKPosition, textBoxKVelocity,
                    textBoxMimicOffset
                });
            }
            Joint.Name = textBoxJointName.Text;

            Joint.Parent.Update(labelParent);
            Joint.Child.Update(labelChild);

            Joint.AxisReference = ReadReferenceComboBox(
                comboBoxAxis,
                Joint.AxisReference,
                ReferenceGeometryKind.Axis);

            Joint.Origin.Update(textBoxJointX,
                                textBoxJointY,
                                textBoxJointZ,
                                textBoxJointRoll,
                                textBoxJointPitch,
                                textBoxJointYaw);

            Joint.Axis.Update(textBoxAxisX,
                              textBoxAxisY,
                              textBoxAxisZ);

            Joint.Limit.SetRequired(selectedType == "revolute" || selectedType == "prismatic");
            Joint.Limit.SetValues(textBoxLimitLower,
                                  textBoxLimitUpper,
                                  textBoxLimitEffort,
                                  textBoxLimitVelocity);

            if (String.IsNullOrWhiteSpace(textBoxCalibrationRising.Text) &&
                String.IsNullOrWhiteSpace(textBoxCalibrationFalling.Text))
            {
                Joint.Calibration.Unset();
            }
            else
            {
                Joint.Calibration.SetValues(textBoxCalibrationRising,
                                         textBoxCalibrationFalling);
            }

            if (String.IsNullOrWhiteSpace(textBoxFriction.Text) &&
                String.IsNullOrWhiteSpace(textBoxDamping.Text))
            {
                Joint.Dynamics.Unset();
            }
            else
            {
                Joint.Dynamics.SetValues(textBoxDamping,
                                      textBoxFriction);
            }

            if (String.IsNullOrWhiteSpace(textBoxSoftLower.Text) &&
                String.IsNullOrWhiteSpace(textBoxSoftUpper.Text) &&
                String.IsNullOrWhiteSpace(textBoxKPosition.Text) &&
                String.IsNullOrWhiteSpace(textBoxKVelocity.Text))
            {
                Joint.Safety.Unset();
            }
            else
            {
                Joint.Safety.SetValues(textBoxSoftLower,
                                    textBoxSoftUpper,
                                    textBoxKPosition,
                                    textBoxKVelocity);
            }

            if (MimicCheckBox.Checked)
            {
                Joint.Mimic.Update(MimicJointComboBox.Text, textBoxMimicMultiplier.Text, textBoxMimicOffset.Text);
            }
            else
            {
                Joint.Mimic.Clear();
            }

            JointConfigurationPolicy.ApplyUserSelection(Joint, selectedType);
            displayedJointType = Joint.Type;
            jointUnitInputsResetForCurrentChange = false;
        }

        private void LocalizeDynamicJointLabels()
        {
            labelLowerLimit.Text = ChineseUiText.DynamicJointLabel(labelLowerLimit.Text);
            labelLimitUpper.Text = ChineseUiText.DynamicJointLabel(labelLimitUpper.Text);
            labelEffort.Text = ChineseUiText.DynamicJointLabel(labelEffort.Text);
            labelVelocity.Text = ChineseUiText.DynamicJointLabel(labelVelocity.Text);
            labelFriction.Text = ChineseUiText.DynamicJointLabel(labelFriction.Text);
            labelDamping.Text = ChineseUiText.DynamicJointLabel(labelDamping.Text);
            labelSoftLower.Text = ChineseUiText.DynamicJointLabel(labelSoftLower.Text);
            labelSoftUpper.Text = ChineseUiText.DynamicJointLabel(labelSoftUpper.Text);
            labelKPosition.Text = ChineseUiText.DynamicJointLabel(labelKPosition.Text);
            labelKVelocity.Text = ChineseUiText.DynamicJointLabel(labelKVelocity.Text);
        }

        private void InitializeCollisionStrategyComboBox()
        {
            comboBoxCollisionStrategy.Items.Clear();
            comboBoxCollisionStrategy.Items.Add(new CollisionStrategyChoice(
                CollisionMeshStrategy.VisualMesh,
                ChineseUiText.Translate("Visual STL copy", "\u590d\u7528\u53ef\u89c6 STL")));
            comboBoxCollisionStrategy.Items.Add(new CollisionStrategyChoice(
                CollisionMeshStrategy.SimplifiedMesh,
                ChineseUiText.Translate("Simplified collision STL", "\u7b80\u5316 collision STL")));
            comboBoxCollisionStrategy.Items.Add(new CollisionStrategyChoice(
                CollisionMeshStrategy.AccurateMesh,
                ChineseUiText.Translate("Accurate collision STL", "\u7cbe\u51c6 collision STL")));
            comboBoxCollisionStrategy.Items.Add(new CollisionStrategyChoice(
                CollisionMeshStrategy.BoxPrimitive,
                ChineseUiText.Translate("Box primitive", "\u5305\u56f4\u76d2 primitive")));
            comboBoxCollisionStrategy.Items.Add(new CollisionStrategyChoice(
                CollisionMeshStrategy.CylinderPrimitive,
                ChineseUiText.Translate("Cylinder primitive", "\u5706\u67f1 primitive")));
            comboBoxCollisionStrategy.Items.Add(new CollisionStrategyChoice(
                CollisionMeshStrategy.SpherePrimitive,
                ChineseUiText.Translate("Sphere primitive", "\u7403\u4f53 primitive")));
            comboBoxCollisionStrategy.Items.Add(new CollisionStrategyChoice(
                CollisionMeshStrategy.ConvexHull,
                ChineseUiText.Translate("Convex hull STL", "\u51f8\u5305 STL")));
            comboBoxCollisionStrategy.Items.Add(new CollisionStrategyChoice(
                CollisionMeshStrategy.ComponentBoxes,
                ChineseUiText.Translate("Component box set", "\u7ec4\u4ef6\u5305\u56f4\u76d2\u7ec4\u5408")));
            comboBoxCollisionStrategy.SelectedIndex = 0;
        }

        private void SelectCollisionStrategy(CollisionMeshStrategy strategy)
        {
            if (strategy == CollisionMeshStrategy.Primitive)
            {
                strategy = CollisionMeshStrategy.BoxPrimitive;
            }

            foreach (object item in comboBoxCollisionStrategy.Items)
            {
                CollisionStrategyChoice choice = item as CollisionStrategyChoice;
                if (choice != null && choice.Strategy == strategy)
                {
                    comboBoxCollisionStrategy.SelectedItem = choice;
                    return;
                }
            }

            comboBoxCollisionStrategy.SelectedIndex = 0;
        }

        private CollisionMeshStrategy GetSelectedCollisionStrategy()
        {
            CollisionStrategyChoice choice =
                comboBoxCollisionStrategy.SelectedItem as CollisionStrategyChoice;
            return choice == null ? CollisionMeshStrategy.VisualMesh : choice.Strategy;
        }

        private sealed class CollisionStrategyChoice
        {
            private readonly string text;

            public CollisionStrategyChoice(CollisionMeshStrategy strategy, string text)
            {
                Strategy = strategy;
                this.text = text;
            }

            public CollisionMeshStrategy Strategy { get; private set; }

            public override string ToString()
            {
                return text;
            }
        }

        //Fills specifically the joint TreeView
        public void FillJointTree()
        {
            treeViewJointTree.BeginUpdate();
            try
            {
                using (treeSelectionUpdateGuard.Suppress())
                {
                    treeViewJointTree.Nodes.Clear();

                    while (BaseNode.Nodes.Count > 0)
                    {
                        LinkNode node = (LinkNode)BaseNode.FirstNode;
                        BaseNode.Nodes.Remove(node);
                        treeViewJointTree.Nodes.Add(node);
                        UpdateNodeText(node, true);
                    }
                    if (!modernJointTreeExpandedOnce)
                    {
                        treeViewJointTree.ExpandAll();
                        modernJointTreeExpandedOnce = true;
                    }
                    previouslySelectedNode = null;
                }
            }
            finally
            {
                treeViewJointTree.EndUpdate();
            }
        }

        public void FillLinkTree()
        {
            treeViewLinkProperties.BeginUpdate();
            try
            {
                using (treeSelectionUpdateGuard.Suppress())
                {
                    treeViewLinkProperties.Nodes.Clear();
                    treeViewLinkProperties.Nodes.Add(BaseNode);
                    UpdateNodeText(BaseNode, false);
                    if (!modernLinkTreeExpandedOnce)
                    {
                        treeViewLinkProperties.ExpandAll();
                        modernLinkTreeExpandedOnce = true;
                    }
                }
            }
            finally
            {
                treeViewLinkProperties.EndUpdate();
            }
        }

        public void UpdateNodeText(LinkNode node, bool useJointName)
        {
            Stack<LinkNode> pending = new Stack<LinkNode>();
            pending.Push(node);
            while (pending.Count > 0)
            {
                LinkNode current = pending.Pop();
                current.Text = useJointName
                    ? current.Link.Joint.Name
                    : current.Link.Name;
                for (int index = current.Nodes.Count - 1; index >= 0; index--)
                {
                    pending.Push((LinkNode)current.Nodes[index]);
                }
            }
        }

        //Converts a Link to a LinkNode
        public static LinkNode CreateLinkNodeFromLink(Link Link)
        {
            LinkNode node = new LinkNode(Link);
            node.Link.Children.Clear();
            return node;
        }

        //Converts a TreeView back into a robot
        public Robot CreateRobotFromTreeView(TreeView tree)
        {
            Robot Robot = Exporter.URDFRobot;
            Link baseLink = CreateLinkFromLinkNode((LinkNode)tree.Nodes[0]);
            Robot.SetBaseLink(baseLink);
            Robot.Name = Exporter.URDFRobot.Name;
            return Robot;
        }

        //Converts a LinkNode into a Link
        public Link CreateLinkFromLinkNode(LinkNode node)
        {
            Link Link = node.Link;
            Link.Children.Clear();
            foreach (LinkNode child in node.Nodes)
            {
                Link childLink = CreateLinkFromLinkNode(child);
                Link.Children.Add(childLink); // Recreates the children of each embedded link
            }
            return Link;
        }

        private void CheckLinksForWarnings(Link node, StringBuilder builder)
        {
            string msg = "";

            if (!string.IsNullOrWhiteSpace(msg))
            {
                builder.Append(node.Name + " - " + msg + "\r\n");
            }
            foreach (Link child in node.Children)
            {
                CheckLinksForWarnings(child, builder);
            }
        }

        private string CheckLinksForWarnings(Link baseNode)
        {
            StringBuilder builder = new StringBuilder();
            CheckLinksForWarnings(baseNode, builder);
            return builder.ToString();
        }

        public bool SaveConfigTree(ModelDoc2 model, LinkNode BaseNode, bool warnUser)
        {
            List<Joint> joints = new List<Joint>();
            CollectJoints(BaseNode, joints);
            IList<string> mimicErrors = MimicGraphValidator.Validate(joints);
            if (mimicErrors.Count > 0)
            {
                string message = "The URDF configuration was not saved because the Mimic Joint graph is invalid:\r\n\r\n" +
                    string.Join("\r\n", mimicErrors);
                logger.Warn(message);
                MessageBox.Show(message, "URDF Joint Errors");
                return false;
            }
            CommonSwOperations.RetrieveSWComponentPIDs(model, BaseNode);
            bool saved = ConfigurationSaveInteraction.Save(
                allowOverwrite => ConfigurationSerialization.SaveConfigTreeXML(
                    swApp,
                    model,
                    BaseNode,
                    allowOverwrite),
                warnUser, out bool persisted);
            if (persisted)
            {
                ClearExportSessionDraft();
            }
            return saved;
        }

    }
}
