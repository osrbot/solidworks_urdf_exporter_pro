using MathNet.Numerics.LinearAlgebra;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Linq;
using Xunit;

namespace SW2URDF.Test
{
    public class TestMassPropertyFrameConverter
    {
        [Fact]
        public void TestConvertsTranslatedRotatedComAndTensorToLinkFrame()
        {
            MassPropertySnapshot source = new MassPropertySnapshot(
                2.0,
                new[] { 1.0, 0.0, 0.0 },
                new[]
                {
                    1.0, 0.0, 0.0,
                    0.0, 2.0, 0.0,
                    0.0, 0.0, 3.0
                });
            Matrix<double> globalFrameToDocument = MathOps.GetTransformation(
                new[] { 10.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, Math.PI / 2.0 });
            Matrix<double> linkFrameToDocument = MathOps.GetTransformation(
                new[] { 10.0, 2.0, 0.0 },
                new[] { 0.0, 0.0, Math.PI });

            MassPropertySnapshot converted = MassPropertyFrameConverter.Convert(
                source,
                globalFrameToDocument,
                linkFrameToDocument);

            Assert.Equal(2.0, converted.Mass, 12);
            Assert.Equal(0.0, converted.CenterOfMass[0], 12);
            Assert.Equal(1.0, converted.CenterOfMass[1], 12);
            Assert.Equal(0.0, converted.CenterOfMass[2], 12);
            Assert.Equal(2.0, converted.Moment[0], 12);
            Assert.Equal(1.0, converted.Moment[4], 12);
            Assert.Equal(3.0, converted.Moment[8], 12);
            Assert.Equal(0.0, converted.Moment[1], 12);
            Assert.Equal(0.0, converted.Moment[2], 12);
            Assert.Equal(0.0, converted.Moment[5], 12);
        }

        [Fact]
        public void TestGlobalComReconstructionDoesNotCollapseDistinctLinks()
        {
            Matrix<double> globalFrame = Matrix<double>.Build.DenseIdentity(4);
            Matrix<double> firstLink = MathOps.GetTransformation(
                new[] { 0.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, 0.0 });
            Matrix<double> secondLink = MathOps.GetTransformation(
                new[] { 0.0, 0.0, 0.1 },
                new[] { 0.0, 0.0, 0.0 });
            MassPropertySnapshot firstGlobal = SnapshotAt(0.015, 0.0, 0.0);
            MassPropertySnapshot secondGlobal = SnapshotAt(0.015, 0.0, 0.1);

            MassPropertySnapshot firstLocal = MassPropertyFrameConverter.Convert(
                firstGlobal, globalFrame, firstLink);
            MassPropertySnapshot secondLocal = MassPropertyFrameConverter.Convert(
                secondGlobal, globalFrame, secondLink);

            Assert.Equal(0.015, firstLocal.CenterOfMass[0], 12);
            Assert.Equal(0.015, secondLocal.CenterOfMass[0], 12);
            Assert.Equal(0.0, firstLocal.CenterOfMass[2], 12);
            Assert.Equal(0.0, secondLocal.CenterOfMass[2], 12);
        }

        [Fact]
        public void TestTranslationChangesComButNotTensorAboutCom()
        {
            MassPropertySnapshot source = new MassPropertySnapshot(
                3.0,
                new[] { 0.4, -0.2, 0.7 },
                new[]
                {
                    0.12, -0.01, 0.02,
                    -0.01, 0.18, 0.03,
                    0.02, 0.03, 0.21
                });
            Matrix<double> sourceFrame = Matrix<double>.Build.DenseIdentity(4);
            Matrix<double> translatedTarget = MathOps.GetTransformation(
                new[] { 0.1, 0.2, 0.3 },
                new[] { 0.0, 0.0, 0.0 });

            MassPropertySnapshot converted = MassPropertyFrameConverter.Convert(
                source,
                sourceFrame,
                translatedTarget);

            Assert.Equal(0.3, converted.CenterOfMass[0], 12);
            Assert.Equal(-0.4, converted.CenterOfMass[1], 12);
            Assert.Equal(0.4, converted.CenterOfMass[2], 12);
            for (int i = 0; i < source.Moment.Length; i++)
            {
                Assert.Equal(source.Moment[i], converted.Moment[i], 12);
            }
        }

        [Fact]
        public void TestRotationPreservesPrincipalMoments()
        {
            MassPropertySnapshot source = new MassPropertySnapshot(
                2.5,
                new[] { 0.0, 0.0, 0.0 },
                new[]
                {
                    0.11, 0.0, 0.0,
                    0.0, 0.17, 0.0,
                    0.0, 0.0, 0.23
                });
            Matrix<double> sourceFrame = Matrix<double>.Build.DenseIdentity(4);
            Matrix<double> rotatedTarget = MathOps.GetTransformation(
                new[] { 0.0, 0.0, 0.0 },
                new[] { 0.37, -0.51, 0.82 });

            MassPropertySnapshot converted = MassPropertyFrameConverter.Convert(
                source,
                sourceFrame,
                rotatedTarget);
            Matrix<double> convertedTensor = Matrix<double>.Build.DenseOfRowMajor(
                3,
                3,
                converted.Moment);
            double[] eigenvalues = convertedTensor.Evd().EigenValues
                .Select(value => value.Real)
                .OrderBy(value => value)
                .ToArray();

            Assert.Equal(0.11, eigenvalues[0], 10);
            Assert.Equal(0.17, eigenvalues[1], 10);
            Assert.Equal(0.23, eigenvalues[2], 10);
        }

        [Fact]
        public void TestRepairsRootFrameOverwrittenByChildJointFrame()
        {
            LinkNode root = new LinkNode { IsBaseNode = true };
            root.Link.Joint.CoordinateSystemName = "Origin_back_left_joint";
            LinkNode child = new LinkNode { IsBaseNode = false };
            child.Link.Joint.CoordinateSystemName = "Origin_back_left_joint";
            root.Nodes.Add(child);

            string resolved = LinkTreeGlobalFramePolicy.Resolve(
                root,
                new[] { "Origin_global", "Origin_back_left_joint" });

            Assert.Equal("Origin_global", resolved);
        }

        [Fact]
        public void TestKeepsExplicitCustomRootFrameWhenItDoesNotMatchChild()
        {
            LinkNode root = new LinkNode { IsBaseNode = true };
            root.Link.Joint.CoordinateSystemName = "robot_root";
            LinkNode child = new LinkNode { IsBaseNode = false };
            child.Link.Joint.CoordinateSystemName = "Origin_wheel_joint";
            root.Nodes.Add(child);

            string resolved = LinkTreeGlobalFramePolicy.Resolve(
                root,
                new[] { "Origin_global", "robot_root", "Origin_wheel_joint" });

            Assert.Equal("robot_root", resolved);
        }

        private static MassPropertySnapshot SnapshotAt(double x, double y, double z)
        {
            return new MassPropertySnapshot(
                1.0,
                new[] { x, y, z },
                new[]
                {
                    1.0, 0.0, 0.0,
                    0.0, 1.0, 0.0,
                    0.0, 0.0, 1.0
                });
        }
    }
}
