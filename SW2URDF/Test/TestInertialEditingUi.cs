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
        public void ManualTensorKeepsTypedValueButAllowsExplicitCalibration()
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
                Assert.True(checkbox.Enabled);
                Assert.False(checkbox.Checked);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExplicitCalibrationKeepsEnteredMassAndUpdatesVisibleTensor(bool legacy)
        {
            using (var form = (AssemblyExportForm)Activator.CreateInstance(typeof(AssemblyExportForm), true))
            {
                var link = TestInertialEditing.SourceLink();
                if (legacy)
                {
                    link.InertialEditing = null;
                    InertialEditingPolicy.EnsureSource(link);
                    InertialEditingPolicy.ApplySource(link, TestInertialEditing.SourceLink().Inertial, false);
                }
                Invoke(form, "FillEffectiveInertialInputs", link);
                Input(form, "textBoxMass").Text = "5";
                Input(form, "textBoxIxx").Text = "0.2";
                Assert.True((bool)Invoke(form, "CommitInertialInputs", link));
                int confirmations = 0;
                Assert.True(AssemblyExportForm.TryChangeInertiaCalibration(link, true,
                    () => { confirmations++; return true; }, _ => throw new Exception("Unnecessary CAD access")));
                Invoke(form, "FillEffectiveInertialInputs", link);
                Assert.Equal(1, confirmations);
                Assert.Equal(5, link.Inertial.Mass.Value);
                Assert.Equal(.45, link.Inertial.Inertia.Ixx, 12);
                Assert.Equal(.45, Double.Parse(Input(form, "textBoxIxx").Text, URDFAttribute.URDFNumberFormat), 12);
                Assert.True(((CheckBox)form.Controls.Find("checkBoxCalibrateInertia", true).Single()).Checked);
                Assert.False(link.InertialEditing.LegacyValuesPreserved);
                Assert.Equal(2, link.InertialEditing.Source.Mass.Value);
                Assert.Equal(.18, link.InertialEditing.Source.Inertia.Ixx);
            }
        }

        [Fact]
        public void DecliningCalibrationPreservesAllEditsWithoutReadingCad()
        {
            var link = TestInertialEditing.SourceLink();
            var edits = InertialEditingPolicy.Copy(link.Inertial);
            edits.Mass.Value = 5;
            edits.Inertia.Ixx = .2;
            InertialEditingPolicy.ApplyExplicitValues(link, edits);
            var state = link.InertialEditing;
            Assert.False(AssemblyExportForm.TryChangeInertiaCalibration(link, true,
                () => false, _ => throw new Exception("Must not read CAD after cancel")));
            Assert.Same(state, link.InertialEditing);
            Assert.Equal(5, link.Inertial.Mass.Value);
            Assert.Equal(.2, link.Inertial.Inertia.Ixx);
        }

        [Fact]
        public void LegacyWithoutBaselineReadsDetachedSourceAndKeepsMeasuredMass()
        {
            var link = new Link();
            link.Inertial.SetElement(TestInertialEditing.SourceLink().Inertial);
            link.Inertial.Mass.Value = 5;
            int reads = 0;
            Assert.True(AssemblyExportForm.TryChangeInertiaCalibration(link, true, () => true, draft =>
            {
                reads++;
                Assert.NotSame(link, draft);
                InertialEditingPolicy.ApplySource(draft, TestInertialEditing.SourceLink().Inertial, true);
            }));
            Assert.Equal(1, reads);
            Assert.Equal(5, link.Inertial.Mass.Value);
            Assert.Equal(.45, link.Inertial.Inertia.Ixx, 12);
            Assert.True(link.InertialEditing.CalibrateExplicitSource);
        }

        [Fact]
        public void FailedSourceReadLeavesLegacyMassAndTensorUntouched()
        {
            var link = new Link();
            link.Inertial.SetElement(TestInertialEditing.SourceLink().Inertial);
            link.Inertial.Mass.Value = 5;
            Assert.Throws<InvalidOperationException>(() => AssemblyExportForm.TryChangeInertiaCalibration(
                link, true, () => true, draft =>
                {
                    draft.Inertial.Mass.Value = 99;
                    throw new InvalidOperationException("read failed");
                }));
            Assert.Null(link.InertialEditing);
            Assert.Equal(5, link.Inertial.Mass.Value);
            Assert.Equal(.18, link.Inertial.Inertia.Ixx);
        }

        [Fact]
        public void OrdinaryMassCalibrationDoesNotRequireResetConfirmationOrCadAccess()
        {
            var link = TestInertialEditing.SourceLink();
            InertialEditingPolicy.SetCalibration(link, false);
            var edits = InertialEditingPolicy.Copy(link.Inertial);
            edits.Mass.Value = 5;
            InertialEditingPolicy.ApplyEdits(link, edits);
            Assert.True(AssemblyExportForm.TryChangeInertiaCalibration(link, true,
                () => throw new Exception("No confirmation needed"), _ => throw new Exception("No CAD access needed")));
            Assert.Equal(5, link.Inertial.Mass.Value);
            Assert.Equal(.45, link.Inertial.Inertia.Ixx, 12);
        }

        [Theory]
        [InlineData("5", true)]
        [InlineData("", false)]
        public void CheckboxEventCommitsPendingMassAndReflectsActualCalibrationState(string mass, bool valid)
        {
            using (var form = (AssemblyExportForm)Activator.CreateInstance(typeof(AssemblyExportForm), true))
            {
                var link = TestInertialEditing.SourceLink();
                InertialEditingPolicy.SetCalibration(link, false);
                var tree = (TreeView)typeof(AssemblyExportForm).GetField("treeViewLinkProperties",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                tree.AfterSelect -= (TreeViewEventHandler)Delegate.CreateDelegate(typeof(TreeViewEventHandler),
                    form, "TreeViewLinkPropertiesAfterSelect");
                var node = new LinkNode(link);
                tree.Nodes.Add(node);
                tree.SelectedNode = node;
                Invoke(form, "FillEffectiveInertialInputs", link);
                Input(form, "textBoxMass").Text = mass;
                var checkbox = (CheckBox)form.Controls.Find("checkBoxCalibrateInertia", true).Single();
                checkbox.Checked = true;
                Assert.Equal(valid, checkbox.Checked);
                Assert.Equal(!valid, link.InertialEditing.CalibrationDisabled);
                if (valid)
                {
                    Assert.Equal(5, link.Inertial.Mass.Value);
                    Assert.Equal(.45, link.Inertial.Inertia.Ixx, 12);
                    Assert.Equal(.45, Double.Parse(Input(form, "textBoxIxx").Text, URDFAttribute.URDFNumberFormat), 12);
                }
                else Assert.True(Double.IsNaN(link.Inertial.Mass.Value));
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
