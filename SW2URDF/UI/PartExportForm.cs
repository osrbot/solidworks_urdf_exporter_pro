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
using System.IO;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    public partial class PartExportForm : Form
    {
        private const string GeneralDisplayFormat = "G5";
        // Keep SolidWorks mass properties intact when the textbox values are saved back.
        private const string InertiaDisplayFormat = "R";

        public ExportHelper Exporter;

        private PartExportForm()
        {
            SuspendLayout();
            try
            {
                InitializeComponent();
                ChineseUiText.Apply(this);
                InitializeMaterialIdControl();
                ModernWinFormsTheme.Apply(this);
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        public PartExportForm(SldWorks iSwApp)
            : this()
        {
            Exporter = new ExportHelper(iSwApp);
        }

        private void InitializeMaterialIdControl()
        {
            label28.Text = ChineseUiText.Translate(
                "URDF material ID (preset updates RGBA)",
                "URDF 材质 ID（选择预设会同步 RGBA）");
            foreach (string materialName in UsageGuideForm.CommonMaterialNames)
            {
                if (!comboBox_materials.Items.Contains(materialName))
                {
                    comboBox_materials.Items.Add(materialName);
                }
            }
            comboBox_materials.SelectionChangeCommitted += MaterialPresetSelectionChangeCommitted;
        }

        private void MaterialPresetSelectionChangeCommitted(object sender, EventArgs e)
        {
            if (!MaterialAppearancePresets.TryGet(comboBox_materials.Text, out double[] rgba))
            {
                return;
            }

            domainUpDown_red.Text = rgba[0].ToString(
                GeneralDisplayFormat,
                URDFAttribute.URDFNumberFormat);
            domainUpDown_green.Text = rgba[1].ToString(
                GeneralDisplayFormat,
                URDFAttribute.URDFNumberFormat);
            domainUpDown_blue.Text = rgba[2].ToString(
                GeneralDisplayFormat,
                URDFAttribute.URDFNumberFormat);
            domainUpDown_alpha.Text = rgba[3].ToString(
                GeneralDisplayFormat,
                URDFAttribute.URDFNumberFormat);
        }

        #region Basic event handelers

        private void ButtonSaveNameBrowseClick(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog1 = new SaveFileDialog
            {
                RestoreDirectory = true,
                InitialDirectory = Path.GetDirectoryName(textBox_save_as.Text),
                FileName = URDFPackage.SanitizePackageName(Path.GetFileName(textBox_save_as.Text))
            };

            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                textBox_save_as.Text = saveFileDialog1.FileName;
            }
            saveFileDialog1.Dispose();
        }

        #endregion Basic event handelers

        #region Form event handlers

        private void ButtonFinishClick(object sender, EventArgs e)
        {
            Exporter.PackageName = URDFPackage.SanitizePackageName(Path.GetFileName(textBox_save_as.Text));
            Exporter.SavePath = Path.GetDirectoryName(textBox_save_as.Text);
            Exporter.URDFRobot.BaseLink.Name = Exporter.PackageName;

            Exporter.URDFRobot.BaseLink.Inertial.Origin.Update(textBox_inertial_origin_x,
                                                            textBox_inertial_origin_y,
                                                            textBox_inertial_origin_z,
                                                            textBox_inertial_origin_roll,
                                                            textBox_inertial_origin_pitch,
                                                            textBox_inertial_origin_yaw);

            Exporter.URDFRobot.BaseLink.Inertial.Inertia.Update(textBox_ixx,
                                                             textBox_ixy,
                                                             textBox_ixz,
                                                             textBox_iyy,
                                                             textBox_iyz,
                                                             textBox_izz);

            Exporter.URDFRobot.BaseLink.Inertial.Mass.Update(textBox_mass);

            Exporter.URDFRobot.BaseLink.Visual.Origin.Update(textBox_visual_origin_x,
                                                          textBox_visual_origin_y,
                                                          textBox_visual_origin_z,
                                                          textBox_visual_origin_roll,
                                                          textBox_visual_origin_pitch,
                                                          textBox_visual_origin_yaw);

            Exporter.URDFRobot.BaseLink.Visual.Material.Name = comboBox_materials.Text;

            Exporter.URDFRobot.BaseLink.Visual.Material.Color.Update(domainUpDown_red,
                                                                  domainUpDown_green,
                                                                  domainUpDown_blue,
                                                                  domainUpDown_alpha);

            Exporter.URDFRobot.BaseLink.Collision.Origin.Update(textBox_collision_origin_x,
                                                             textBox_collision_origin_y,
                                                             textBox_collision_origin_z,
                                                             textBox_collision_origin_roll,
                                                             textBox_collision_origin_pitch,
                                                             textBox_collision_origin_yaw);

            Exporter.URDFRobot.BaseLink.STLQualityFine = radioButton_fine.Checked;

            Exporter.ExportLink(checkBox_rotate.Checked);
            Close();
        }

        private void ButtonCancelClick(object sender, EventArgs e)
        {
            Close();
        }

        private void PartExportFormLoad(object sender, EventArgs e)
        {
            Exporter.CreateRobotFromActiveModel();
            textBox_save_as.Text = Exporter.SavePath + "\\" + Exporter.PackageName;

            Exporter.URDFRobot.BaseLink.Visual.Origin.FillBoxes(textBox_collision_origin_x,
                                                             textBox_collision_origin_y,
                                                             textBox_collision_origin_z,
                                                             textBox_collision_origin_roll,
                                                             textBox_collision_origin_pitch,
                                                             textBox_collision_origin_yaw,
                                                             GeneralDisplayFormat);

            Exporter.URDFRobot.BaseLink.Visual.Origin.FillBoxes(textBox_visual_origin_x,
                                                             textBox_visual_origin_y,
                                                             textBox_visual_origin_z,
                                                             textBox_visual_origin_roll,
                                                             textBox_visual_origin_pitch,
                                                             textBox_visual_origin_yaw,
                                                             GeneralDisplayFormat);

            Exporter.URDFRobot.BaseLink.Visual.Material.Color.FillBoxes(domainUpDown_red,
                                                                     domainUpDown_green,
                                                                     domainUpDown_blue,
                                                                     domainUpDown_alpha,
                                                                     GeneralDisplayFormat);

            Exporter.URDFRobot.BaseLink.Inertial.Origin.FillBoxes(textBox_inertial_origin_x,
                                                               textBox_inertial_origin_y,
                                                               textBox_inertial_origin_z,
                                                               textBox_inertial_origin_roll,
                                                               textBox_inertial_origin_pitch,
                                                               textBox_inertial_origin_yaw,
                                                               InertiaDisplayFormat);

            Exporter.URDFRobot.BaseLink.Inertial.Mass.FillBoxes(textBox_mass, InertiaDisplayFormat);

            Exporter.URDFRobot.BaseLink.Inertial.Inertia.FillBoxes(textBox_ixx,
                                                                textBox_ixy,
                                                                textBox_ixz,
                                                                textBox_iyy,
                                                                textBox_iyz,
                                                                textBox_izz,
                                                                InertiaDisplayFormat);
        }

        #endregion Form event handlers
    }
}
