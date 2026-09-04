using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;
using MathNet.Numerics.LinearAlgebra;
using SW2URDF.Utilities;
using OSURDF.Core.Urdf;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public class TestInertialEditing
    {
        internal static Link SourceLink(bool explicitInertia = false)
        {
            var link = new Link { Name = "base_link" };
            var source = new Inertial();
            source.Mass.Value = 2;
            source.Origin.SetXYZ(new[] { .1, -.2, .3 });
            source.Inertia.SetUrdfMomentMatrix(new[] { .18, .02, -.01, .02, .22, .015, -.01, .015, .26 });
            InertialEditingPolicy.ApplySource(link, source, explicitInertia);
            return link;
        }

        private static void ChangeMass(Link link, double mass)
        {
            var edited = InertialEditingPolicy.Copy(link.Inertial);
            edited.Mass.Value = mass;
            InertialEditingPolicy.ApplyEdits(link, edited);
        }

        [Fact]
        public void MeasuredMassScalesFullTensorAndKeepsComAndCuboid()
        {
            var link = SourceLink();
            Assert.True(InertiaEllipsoid.TryCreate(2, link.Inertial.Inertia, out var before, out var error), error);
            double[] source = link.Inertial.Inertia.GetMoment();
            ChangeMass(link, 5);
            Assert.Equal(source.Select(x => x * 2.5), link.Inertial.Inertia.GetMoment());
            Assert.Equal(new[] { .1, -.2, .3 }, link.Inertial.Origin.GetXYZ());
            Assert.True(InertiaEllipsoid.TryCreate(5, link.Inertial.Inertia, out var after, out error), error);
            for (int i = 0; i < 3; i++) Assert.Equal(before.EquivalentBoxDimensions[i], after.EquivalentBoxDimensions[i], 12);
        }

        [Fact]
        public void RepeatedSavesAndMassChangesDoNotAccumulateScaling()
        {
            var link = SourceLink();
            for (int i = 0; i < 30; i++)
            {
                ChangeMass(link, 7);
                InertialEditingPolicy.ApplyEdits(link, InertialEditingPolicy.Copy(link.Inertial));
                ChangeMass(link, 3);
            }
            Assert.Equal(.27, link.Inertial.Inertia.Ixx, 12);
            ChangeMass(link, 2);
            Assert.False(link.InertialEditing.MassEdited);
            Assert.Equal(.18, link.Inertial.Inertia.Ixx, 12);
        }

        [Fact]
        public void ExplicitManualTensorTakesPrecedenceOverCalibration()
        {
            var link = SourceLink();
            var edited = InertialEditingPolicy.Copy(link.Inertial);
            edited.Mass.Value = 4;
            edited.Inertia.Ixx = .2;
            InertialEditingPolicy.ApplyEdits(link, edited);
            ChangeMass(link, 6);
            Assert.Equal(.2, link.Inertial.Inertia.Ixx);
            Assert.Equal(.22, link.Inertial.Inertia.Iyy);
            Assert.False(InertialEditingPolicy.CanCalibrate(link));
        }

        [Fact]
        public void SolidWorksExplicitTensorIsNotRescaled()
        {
            var link = SourceLink(true);
            ChangeMass(link, 5);
            Assert.Equal(.18, link.Inertial.Inertia.Ixx);
            Assert.False(InertialEditingPolicy.CanCalibrate(link));
        }

        [Fact]
        public void CalibrationCanBeDisabledAndRestoredWithoutDrift()
        {
            var link = SourceLink();
            ChangeMass(link, 4);
            for (int i = 0; i < 10; i++)
            {
                InertialEditingPolicy.SetCalibration(link, false);
                Assert.Equal(.18, link.Inertial.Inertia.Ixx);
                InertialEditingPolicy.SetCalibration(link, true);
                Assert.Equal(.36, link.Inertial.Inertia.Ixx);
            }
            InertialEditingPolicy.Reset(link);
            Assert.Equal(2, link.Inertial.Mass.Value);
            Assert.Equal(.18, link.Inertial.Inertia.Ixx);
        }

        [Fact]
        public void FreshSwSourceKeepsMeasuredMassAndManualCom()
        {
            var link = SourceLink();
            var edited = InertialEditingPolicy.Copy(link.Inertial);
            edited.Mass.Value = 8;
            edited.Origin.SetXYZ(new[] { .4, .5, .6 });
            InertialEditingPolicy.ApplyEdits(link, edited);
            var newSource = InertialEditingPolicy.Copy(link.InertialEditing.Source);
            newSource.Mass.Value = 4;
            newSource.Inertia.Ixx = .19;
            InertialEditingPolicy.ApplySource(link, newSource, false);
            Assert.Equal(8, link.Inertial.Mass.Value);
            Assert.Equal(.38, link.Inertial.Inertia.Ixx, 12);
            Assert.Equal(new[] { .4, .5, .6 }, link.Inertial.Origin.GetXYZ());
        }

        [Fact]
        public void CloneAndSerializationPreservePolicyWithoutSharingBaseline()
        {
            var link = SourceLink();
            ChangeMass(link, 4);
            var cloned = link.Clone();
            cloned.InertialEditing.Source.Origin.X = 99;
            Assert.Equal(.1, link.InertialEditing.Source.Origin.X);
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractSerializer(typeof(Link));
                serializer.WriteObject(stream, link);
                stream.Position = 0;
                var restored = (Link)serializer.ReadObject(stream);
                ChangeMass(restored, 6);
                Assert.Equal(.54, restored.Inertial.Inertia.Ixx, 12);
                Assert.True(restored.InertialEditing.SourceIsSolidWorks);
            }
        }

        [Fact]
        public void LegacyConfigurationWithoutSourcePreservesPossiblyManualTensor()
        {
            var link = new Link();
            link.Inertial.SetElement(SourceLink().Inertial);
            Assert.Null(link.InertialEditing);
            ChangeMass(link, 4);
            Assert.Equal(.18, link.Inertial.Inertia.Ixx, 12);
            Assert.False(link.InertialEditing.SourceIsSolidWorks);
            Assert.False(InertialEditingPolicy.CanCalibrate(link));
            var source = InertialEditingPolicy.Copy(SourceLink().Inertial);
            source.Mass.Value = 7;
            InertialEditingPolicy.ApplySource(link, source, false);
            Assert.Equal(4, link.Inertial.Mass.Value);
            Assert.Equal(.18, link.Inertial.Inertia.Ixx, 12);
        }

        [Fact]
        public void ManualMassPassesEffectiveComparisonButInvalidPhysicsStillFails()
        {
            var link = SourceLink();
            var source = link.InertialEditing.Source;
            var snapshot = new MassPropertySnapshot(source.Mass.Value, source.Origin.GetXYZ(), source.Inertia.GetMoment());
            ChangeMass(link, 5);
            Assert.All(ExportHelper.BuildEffectiveInertiaComparisonRows(link, snapshot), row => Assert.True(row.Passed));
            Assert.All(ExportHelper.BuildPhysicalInertiaValidationRows(link), row => Assert.True(row.Passed));
            ChangeMass(link, 0);
            Assert.Contains(ExportHelper.BuildPhysicalInertiaValidationRows(link), row => row.Quantity == "mass.positive" && !row.Passed);
        }

        [Fact]
        public void UneditedMismatchIsNotSuppressedBySourceMetadata()
        {
            var link = SourceLink();
            var source = link.InertialEditing.Source;
            var changedSw = new MassPropertySnapshot(3, source.Origin.GetXYZ(), source.Inertia.GetMoment());
            Assert.Contains(ExportHelper.BuildEffectiveInertiaComparisonRows(link, changedSw),
                row => row.Quantity == "mass" && !row.Passed);
        }

        [Fact]
        public void InvalidManualTensorStillFailsPhysicalValidation()
        {
            var link = SourceLink();
            var edited = InertialEditingPolicy.Copy(link.Inertial);
            edited.Inertia.Ixx = 100;
            InertialEditingPolicy.ApplyEdits(link, edited);
            Assert.Contains(ExportHelper.BuildPhysicalInertiaValidationRows(link),
                row => row.Quantity == "principal_moments.triangle_inequality" && !row.Passed);
        }

        [Fact]
        public void UrdfAndAllTargetInputUseTheSameCalibratedValuesAsPreview()
        {
            var link = SourceLink();
            ChangeMass(link, 5);
            string path = Path.Combine(Path.GetTempPath(), "calibrated-" + Guid.NewGuid().ToString("N") + ".urdf");
            try
            {
                using (var writer = XmlWriter.Create(path))
                {
                    writer.WriteStartElement("robot");
                    writer.WriteAttributeString("name", "calibrated");
                    writer.WriteStartElement("link");
                    writer.WriteAttributeString("name", link.Name);
                    link.Inertial.WriteURDF(writer);
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                var exported = UrdfCodec.Read(path).Links.Single().Inertial;
                Assert.Equal(link.Inertial.Mass.Value, exported.Mass);
                Assert.Equal(link.Inertial.Inertia.Ixx, exported.Inertia.Ixx, 12);
                Assert.Equal(link.Inertial.Inertia.Ixy, exported.Inertia.Ixy, 12);
                Assert.Equal(link.Inertial.Inertia.Ixz, exported.Inertia.Ixz, 12);
                Assert.Equal(link.Inertial.Inertia.Iyy, exported.Inertia.Iyy, 12);
                Assert.Equal(link.Inertial.Inertia.Iyz, exported.Inertia.Iyz, 12);
                Assert.Equal(link.Inertial.Inertia.Izz, exported.Inertia.Izz, 12);
                Assert.DoesNotContain("InertialEditing", File.ReadAllText(path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LinkFrameChangePreservesEditedPhysicalInertia(bool manualTensor)
        {
            var link = SourceLink();
            var edited = InertialEditingPolicy.Copy(link.Inertial);
            edited.Mass.Value = 4;
            edited.Origin.SetXYZ(new[] { .6, .5, .4 });
            edited.Origin.SetRPY(new[] { .2, -.1, .3 });
            if (manualTensor) edited.Inertia.Ixx = .2;
            InertialEditingPolicy.ApplyEdits(link, edited);
            var oldOrigin = MathOps.GetTransformation(link.Inertial.Origin.GetXYZ(), link.Inertial.Origin.GetRPY());
            var oldRotation = oldOrigin.SubMatrix(0, 3, 0, 3);
            var oldTensor = Matrix<double>.Build.DenseOfRowMajor(3, 3, link.Inertial.Inertia.GetMoment());
            var oldPhysical = oldRotation * oldTensor * oldRotation.Transpose();
            var newFrame = MathOps.GetTransformation(new[] { .1, .2, .3 }, new[] { .4, .3, .2 });
            var source = link.InertialEditing.Source;
            var newSource = MassPropertyFrameConverter.Convert(new MassPropertySnapshot(
                source.Mass.Value, source.Origin.GetXYZ(), source.Inertia.GetMoment()),
                Matrix<double>.Build.DenseIdentity(4), newFrame);
            InertialEditingPolicy.ReexpressEdits(link, Matrix<double>.Build.DenseIdentity(4), newFrame);
            var baseline = new Inertial();
            baseline.Mass.Value = newSource.Mass;
            baseline.Origin.SetXYZ(newSource.CenterOfMass);
            baseline.Inertia.SetUrdfMomentMatrix(newSource.Moment);
            InertialEditingPolicy.ApplySource(link, baseline, false);
            var newOrigin = MathOps.GetTransformation(link.Inertial.Origin.GetXYZ(), link.Inertial.Origin.GetRPY());
            var worldRotation = (newFrame * newOrigin).SubMatrix(0, 3, 0, 3);
            var tensor = Matrix<double>.Build.DenseOfRowMajor(3, 3, link.Inertial.Inertia.GetMoment());
            var newPhysical = worldRotation * tensor * worldRotation.Transpose();
            Assert.True((oldPhysical - newPhysical).FrobeniusNorm() < 1e-12);
            Assert.True((oldOrigin.Column(3) - (newFrame * newOrigin).Column(3)).L2Norm() < 1e-12);
        }

        [Fact]
        public void ImportedExplicitValuesAreNeverRecalibrated()
        {
            var link = SourceLink();
            var imported = InertialEditingPolicy.Copy(link.Inertial);
            imported.Mass.Value = 9;
            imported.Inertia.Ixx = .2;
            InertialEditingPolicy.ApplyExplicitValues(link, imported);
            ChangeMass(link, 10);
            Assert.Equal(.2, link.Inertial.Inertia.Ixx);
            Assert.True(link.InertialEditing.TensorEdited);
        }

        [Fact]
        public void PhysicalOnlyValidationChecksChildLinksButSkipsFrameOnlyLinks()
        {
            var root = new Link { Name = "frame", isFixedFrame = true };
            var child = SourceLink();
            root.Children.Add(child);
            ExportHelper.EnsureNoBlockingInertialFailures(ExportHelper.BuildPhysicalInertialValidationRecords(root));
            ChangeMass(child, Double.NaN);
            var rows = ExportHelper.BuildPhysicalInertialValidationRecords(root);
            Assert.Throws<InvalidOperationException>(() => ExportHelper.EnsureNoBlockingInertialFailures(rows));
            Assert.All(rows, record => Assert.Equal("base_link", record.LinkName));
        }
    }
}
