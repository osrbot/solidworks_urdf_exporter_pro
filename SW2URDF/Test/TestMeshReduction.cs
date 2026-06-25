using System;
using System.IO;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.UI;
using Xunit;

namespace SW2URDF.Test
{
    public class TestMeshReduction
    {
        [Theory]
        [InlineData(true, 0.0, false, "fine", 0.0)]
        [InlineData(false, 0.0, false, "coarse", 0.0)]
        [InlineData(true, -1.0, false, "fine", 0.0)]
        [InlineData(false, 0.5, true, "custom", 0.5)]
        [InlineData(true, 2.0, true, "custom", 1.0)]
        public void TestStlMeshReductionSettings(
            bool qualityFine,
            double reductionRatio,
            bool expectedCustom,
            string expectedQuality,
            double expectedReduction)
        {
            ExportHelper.StlMeshSettings settings =
                ExportHelper.CreateStlMeshSettings(qualityFine, reductionRatio);

            Assert.Equal(expectedCustom, settings.UseCustom);
            Assert.Equal(expectedQuality, settings.QualityLabel);
            Assert.Equal(expectedReduction, settings.ReductionRatio, 5);
            Assert.InRange(settings.Deviation, 0.001, 0.02);
            Assert.InRange(settings.AngleTolerance, 0.52359, 2.0944);
        }

        [Fact]
        public void TestHigherReductionUsesLooserStlTolerances()
        {
            ExportHelper.StlMeshSettings lowReduction =
                ExportHelper.CreateStlMeshSettings(true, 0.25);
            ExportHelper.StlMeshSettings highReduction =
                ExportHelper.CreateStlMeshSettings(true, 0.75);

            Assert.True(highReduction.Deviation > lowReduction.Deviation);
            Assert.True(highReduction.AngleTolerance > lowReduction.AngleTolerance);
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(0, 0)]
        [InlineData(1, 134)]
        [InlineData(100, 5084)]
        public void TestBinaryStlSizeEstimate(int triangleCount, long expectedBytes)
        {
            Assert.Equal(expectedBytes, ExportHelper.EstimateBinaryStlSizeBytes(triangleCount));
        }

        [Theory]
        [InlineData(100, 100, 0.0)]
        [InlineData(50, 100, -50.0)]
        [InlineData(150, 100, 50.0)]
        [InlineData(100, 0, 0.0)]
        public void TestStlEstimateErrorPercent(long estimated, long actual, double expectedPercent)
        {
            Assert.Equal(
                expectedPercent,
                ExportHelper.CalculateEstimateErrorPercent(estimated, actual),
                5);
        }

        [Fact]
        public void TestLinkCopyPreservesMeshReductionRatio()
        {
            Link source = new Link
            {
                STLQualityFine = true,
                MeshReductionRatio = 0.65,
                CollisionMeshStrategy = CollisionMeshStrategy.Primitive
            };
            Link target = new Link();

            target.SetSWComponents(source);

            Assert.True(target.STLQualityFine);
            Assert.Equal(0.65, target.MeshReductionRatio, 5);
            Assert.Equal(CollisionMeshStrategy.Primitive, target.CollisionMeshStrategy);
        }

        [Fact]
        public void TestEditedMeshReductionAppliesToEveryMeshLink()
        {
            Link baseLink = new Link { Name = "base_link", MeshReductionRatio = 0.0 };
            Link childLink = new Link { Name = "sensor_link", MeshReductionRatio = 0.0 };
            Link fixedFrame = new Link
            {
                Name = "fixed_frame",
                MeshReductionRatio = 0.0,
                isFixedFrame = true
            };

            LinkNode root = new LinkNode(baseLink);
            root.Nodes.Add(new LinkNode(childLink));
            root.Nodes.Add(new LinkNode(fixedFrame));

            AssemblyExportForm.ApplyMeshReductionToTree(root, 0.35);

            Assert.Equal(0.35, baseLink.MeshReductionRatio, 5);
            Assert.Equal(0.35, childLink.MeshReductionRatio, 5);
            Assert.Equal(0.0, fixedFrame.MeshReductionRatio, 5);
        }

        [Fact]
        public void TestVisualAndCollisionMeshFilenamesAreSplit()
        {
            URDFPackage package = new URDFPackage("robot", @"C:\tmp\sw2urdf-test");
            Link link = new Link { Name = "arm/base" };

            ExportHelper.MeshFileNames names =
                ExportHelper.CreateLinkMeshFileNames(package, link, MeshExportFormat.STL);

            Assert.Equal("package://robot/meshes/visual/arm_base.STL",
                names.VisualMeshFilename);
            Assert.Equal("package://robot/meshes/collision/arm_base.STL",
                names.CollisionMeshFilename);
            Assert.NotEqual(names.VisualMeshFilename, names.CollisionMeshFilename);
            Assert.EndsWith(@"ROS1\robot\meshes\visual\arm_base.STL",
                names.WindowsVisualMeshFilename);
            Assert.EndsWith(@"ROS1\robot\meshes\collision\arm_base.STL",
                names.WindowsCollisionMeshFilename);
        }

        [Fact]
        public void TestSplitMeshFilenamesPreserve3dxmlExtension()
        {
            URDFPackage package = new URDFPackage("robot", @"C:\tmp\sw2urdf-test");
            Link link = new Link { Name = "base_link" };

            ExportHelper.MeshFileNames names =
                ExportHelper.CreateLinkMeshFileNames(package, link, MeshExportFormat.THREEDXML);

            Assert.EndsWith("/visual/base_link.3dxml", names.VisualMeshFilename);
            Assert.EndsWith("/collision/base_link.3dxml", names.CollisionMeshFilename);
        }

        [Theory]
        [InlineData("base_link", "base_link", CollisionMeshStrategy.VisualMesh)]
        [InlineData("!acc_base_link", "base_link", CollisionMeshStrategy.AccurateMesh)]
        [InlineData("!pri_sensor", "sensor", CollisionMeshStrategy.Primitive)]
        [InlineData("!cxh_chassis", "chassis", CollisionMeshStrategy.ConvexHull)]
        [InlineData("!PRI_uppercase", "uppercase", CollisionMeshStrategy.Primitive)]
        public void TestCollisionStrategyPrefixesAreParsed(
            string inputName,
            string expectedName,
            CollisionMeshStrategy expectedStrategy)
        {
            Link link = new Link { Name = inputName };

            ExportHelper.ApplyCollisionStrategyPrefix(link);

            Assert.Equal(expectedName, link.Name);
            Assert.Equal(expectedStrategy, link.CollisionMeshStrategy);
        }

        [Fact]
        public void TestMissingCollisionStrategyPrefixKeepsExistingStrategy()
        {
            Link link = new Link
            {
                Name = "base_link",
                CollisionMeshStrategy = CollisionMeshStrategy.Primitive
            };

            ExportHelper.ApplyCollisionStrategyPrefix(link);

            Assert.Equal("base_link", link.Name);
            Assert.Equal(CollisionMeshStrategy.Primitive, link.CollisionMeshStrategy);
        }

        [Fact]
        public void TestPrimitiveBoxStlWritesTwelveBinaryTriangles()
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-primitive-" + Guid.NewGuid() + ".STL");
            ExportHelper.LinkLocalBoundingBox box = new ExportHelper.LinkLocalBoundingBox();
            box.Include(-0.5, -1.0, -1.5);
            box.Include(0.5, 1.0, 1.5);

            try
            {
                ExportHelper.WriteBoxPrimitiveStl(tempFile, box);

                Assert.True(File.Exists(tempFile));
                Assert.Equal(ExportHelper.EstimateBinaryStlSizeBytes(12), new FileInfo(tempFile).Length);
                using (BinaryReader reader = new BinaryReader(File.OpenRead(tempFile)))
                {
                    reader.ReadBytes(80);
                    Assert.Equal((uint)12, reader.ReadUInt32());
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void TestOriginValidationAllowsUrdfRounding()
        {
            ExportHelper.InertialValidationRow row =
                new ExportHelper.InertialValidationRow(
                    "origin.y",
                    "m",
                    0.1234910739,
                    0.12349);

            Assert.True(row.Passed);
        }

        [Fact]
        public void TestOriginValidationRejectsMillimeterScaleOffset()
        {
            ExportHelper.InertialValidationRow row =
                new ExportHelper.InertialValidationRow(
                    "origin.y",
                    "m",
                    0.1234910739,
                    0.12449);

            Assert.False(row.Passed);
        }
    }
}
