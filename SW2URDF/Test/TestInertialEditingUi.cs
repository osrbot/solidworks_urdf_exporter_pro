using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public class TestInertialEditingUi
    {
        [Fact]
        public void MassEditUpdatesVisibleTensorAndRepeatedCommitIsStable()
        {
            using (var form = (AssemblyExportForm)Activator.CreateInstance(typeof(AssemblyExportForm), true))
            {
                var link = TestInertialEditing.SourceLink();
                Invoke(form, "FillEffectiveInertialInputs", link);
                Input(form, "textBoxMass").Text = "5";
                for (int i = 0; i < 10; i++) Assert.True((bool)Invoke(form, "CommitInertialInputs", link));
                Assert.Equal(.45, link.Inertial.Inertia.Ixx, 12);
                Assert.Equal(.05, Double.Parse(Input(form, "textBoxIxy").Text, URDFAttribute.URDFNumberFormat), 12);
                Assert.Equal(.45, Double.Parse(Input(form, "textBoxIxx").Text, URDFAttribute.URDFNumberFormat), 12);
                var checkbox = (CheckBox)form.Controls.Find("checkBoxCalibrateInertia", true).Single();
                Assert.True(checkbox.Checked);
                Assert.True(checkbox.Enabled);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        public void InvalidMassDoesNotSilentlyExportPreviousValue(string value)
        {
            using (var form = (AssemblyExportForm)Activator.CreateInstance(typeof(AssemblyExportForm), true))
            {
                var link = TestInertialEditing.SourceLink();
                Invoke(form, "FillEffectiveInertialInputs", link);
                Input(form, "textBoxMass").Text = value;
                Assert.False((bool)Invoke(form, "CommitInertialInputs", link));
                Assert.True(Double.IsNaN(link.Inertial.Mass.Value));
                Assert.Contains(ExportHelper.BuildPhysicalInertiaValidationRows(link),
                    row => row.Quantity == "mass.positive" && !row.Passed);
                Input(form, "textBoxMass").Text = "4";
                Assert.True((bool)Invoke(form, "CommitInertialInputs", link));
                Assert.Equal(.36, link.Inertial.Inertia.Ixx, 12);
            }
        }

        [Fact]
        public void ManualTensorDisablesAutomaticCalibrationAndKeepsTypedValue()
        {
            using (var form = (AssemblyExportForm)Activator.CreateInstance(typeof(AssemblyExportForm), true))
            {
                var link = TestInertialEditing.SourceLink();
                Invoke(form, "FillEffectiveInertialInputs", link);
                Input(form, "textBoxIxx").Text = "0.2";
                Input(form, "textBoxMass").Text = "5";
                Assert.True((bool)Invoke(form, "CommitInertialInputs", link));
                Assert.Equal(.2, link.Inertial.Inertia.Ixx);
                var checkbox = (CheckBox)form.Controls.Find("checkBoxCalibrateInertia", true).Single();
                Assert.False(checkbox.Enabled);
                Assert.False(checkbox.Checked);
            }
        }

        [Fact]
        public void CorrectingInvalidMassAfterCalibrationDoesNotCreateManualTensorOverride()
        {
            using (var form = (AssemblyExportForm)Activator.CreateInstance(typeof(AssemblyExportForm), true))
            {
                var link = TestInertialEditing.SourceLink();
                Invoke(form, "FillEffectiveInertialInputs", link);
                Input(form, "textBoxMass").Text = "5";
                Assert.True((bool)Invoke(form, "CommitInertialInputs", link));
                Input(form, "textBoxMass").Text = "";
                Assert.False((bool)Invoke(form, "CommitInertialInputs", link));
                Input(form, "textBoxMass").Text = "4";
                Assert.True((bool)Invoke(form, "CommitInertialInputs", link));
                Assert.False(link.InertialEditing.TensorEdited);
                Assert.Equal(.36, link.Inertial.Inertia.Ixx, 12);
            }
        }

        private static TextBox Input(AssemblyExportForm form, string name)
        {
            return (TextBox)typeof(AssemblyExportForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }

        [Theory]
        [InlineData("textBoxInertialOriginX")]
        [InlineData("textBoxInertialOriginYaw")]
        public void CalibratingWhileOriginIsInvalidKeepsVisibleTensorSynchronized(string invalidField)
        {
            using (var form = (AssemblyExportForm)Activator.CreateInstance(typeof(AssemblyExportForm), true))
            {
                var link = TestInertialEditing.SourceLink();
                Invoke(form, "FillEffectiveInertialInputs", link);
                Input(form, "textBoxMass").Text = "5";
                Assert.True((bool)Invoke(form, "CommitInertialInputs", link));
                Input(form, invalidField).Text = "";
                Assert.False((bool)Invoke(form, "CommitInertialInputs", link));
                Input(form, "textBoxMass").Text = "4";
                Assert.False((bool)Invoke(form, "CommitInertialInputs", link));
                Assert.Equal("", Input(form, invalidField).Text);
                Assert.Equal(.36, Double.Parse(Input(form, "textBoxIxx").Text, URDFAttribute.URDFNumberFormat), 12);
                Input(form, invalidField).Text = "0";
                Assert.True((bool)Invoke(form, "CommitInertialInputs", link));
                Assert.False(link.InertialEditing.TensorEdited);
                Assert.Equal(.36, link.Inertial.Inertia.Ixx, 12);
            }
        }

        private static object Invoke(AssemblyExportForm form, string name, params object[] args)
        {
            return typeof(AssemblyExportForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InvalidOriginNeverReachesSolidWorksPreviewServices(bool orientation)
        {
            var link = TestInertialEditing.SourceLink();
            if (orientation) link.Inertial.Origin.Yaw = Double.NaN;
            else link.Inertial.Origin.X = Double.NaN;
            using (var preview = new InertiaPreview(null, null))
            {
                Assert.False(preview.Show(link, null, out _, out _, out var failure));
                Assert.Equal(InertiaPreviewFailureKind.InvalidPhysicalInertia, failure);
            }
        }
    }
}
