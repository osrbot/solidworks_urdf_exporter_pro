using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Xml;
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

        [Theory]
        [InlineData(100, 100, 0.0)]
        [InlineData(50, 100, 50.0)]
        [InlineData(150, 100, -50.0)]
        public void TestStlReductionPercent(long reduced, long baseline, double expectedPercent)
        {
            Assert.Equal(
                expectedPercent,
                ExportHelper.CalculateReductionPercent(reduced, baseline).Value,
                5);
        }

        [Fact]
        public void TestStlReductionPercentReturnsNullWithoutBaseline()
        {
            Assert.Null(ExportHelper.CalculateReductionPercent(100, 0));
        }

        [Fact]
        public void TestLinkConfigurationCopyPreservesExportSettingsWithoutCadBindings()
        {
            Link source = new Link
            {
                STLQualityFine = true,
                MeshReductionRatio = 0.65,
                CollisionMeshStrategy = CollisionMeshStrategy.Primitive
            };
            source.SWComponents.Add(null);
            Link target = new Link();

            target.SetElement(source);

            Assert.True(target.STLQualityFine);
            Assert.Equal(0.65, target.MeshReductionRatio, 5);
            Assert.Equal(CollisionMeshStrategy.Primitive, target.CollisionMeshStrategy);
            Assert.Empty(target.SWComponents);

            target.SetSWComponents(source);

            Assert.Single(target.SWComponents);
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
        public void TestFixedFrameDoesNotHideMeshBearingDescendantsFromExport()
        {
            Link baseLink = new Link { Name = "base_link" };
            Link fixedFrame = new Link { Name = "fixed_frame", isFixedFrame = true };
            Link sensorLink = new Link { Name = "sensor_link" };
            baseLink.Children.Add(fixedFrame);
            fixedFrame.Children.Add(sensorLink);

            IList<Link> links = ExportHelper.GetMeshExportLinks(baseLink);

            Assert.Equal(2, links.Count);
            Assert.Same(baseLink, links[0]);
            Assert.Same(sensorLink, links[1]);
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
        [InlineData("!sim_base_link", "base_link", CollisionMeshStrategy.SimplifiedMesh)]
        [InlineData("!box_chassis", "chassis", CollisionMeshStrategy.BoxPrimitive)]
        [InlineData("!pri_sensor", "sensor", CollisionMeshStrategy.Primitive)]
        [InlineData("!cyl_lidar", "lidar", CollisionMeshStrategy.CylinderPrimitive)]
        [InlineData("!sph_ball", "ball", CollisionMeshStrategy.SpherePrimitive)]
        [InlineData("!cxh_chassis", "chassis", CollisionMeshStrategy.ConvexHull)]
        [InlineData("!cbb_chassis", "chassis", CollisionMeshStrategy.ComponentBoxes)]
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
        public void TestCollisionStrategyWritesToCsvDictionary()
        {
            Link link = new Link
            {
                Name = "base_link",
                CollisionMeshStrategy = CollisionMeshStrategy.Primitive
            };
            OrderedDictionary dictionary = new OrderedDictionary();

            link.AppendToCSVDictionary(new List<string>(), dictionary);

            Assert.Equal("Primitive", dictionary["Link.CollisionMeshStrategy"]);
        }

        [Fact]
        public void TestCollisionStrategyLoadsFromCsvDictionary()
        {
            Link link = new Link();
            StringDictionary dictionary = new StringDictionary();
            dictionary["Link.CollisionMeshStrategy"] = "ConvexHull";

            link.SetElementFromData(new List<string>(), dictionary);

            Assert.Equal(CollisionMeshStrategy.ConvexHull, link.CollisionMeshStrategy);
        }

        [Fact]
        public void TestGeometryCanWriteNativeBoxPrimitive()
        {
            Geometry geometry = new Geometry();

            geometry.UseBox(1.0, 2.0, 3.0);

            string xml = WriteGeometryXml(geometry);
            Assert.Contains("<box", xml);
            Assert.Contains("size=\"1 2 3\"", xml);
            Assert.DoesNotContain("<mesh", xml);
        }

        [Fact]
        public void TestGeometryCanSwitchPrimitiveBackToMesh()
        {
            Geometry geometry = new Geometry();

            geometry.UseSphere(0.25);
            geometry.UseMesh("package://robot/meshes/collision/base_link.STL");

            string xml = WriteGeometryXml(geometry);
            Assert.Contains("<mesh", xml);
            Assert.Contains("filename=\"package://robot/meshes/collision/base_link.STL\"", xml);
            Assert.DoesNotContain("<sphere", xml);
        }

        [Fact]
        public void TestLinkCanWriteMultipleBoxCollisionElements()
        {
            Link link = new Link { Name = "base_link" };
            link.Inertial = null;
            link.Visual = null;
            link.Collision.Geometry.UseBox(1.0, 2.0, 3.0);

            Collision extraCollision = new Collision();
            extraCollision.Origin.SetXYZ(new[] { 1.0, 2.0, 3.0 });
            extraCollision.Geometry.UseBox(0.1, 0.2, 0.3);
            link.AdditionalCollisions.Add(extraCollision);

            string xml = WriteLinkXml(link);

            Assert.Equal(2, CountOccurrences(xml, "<collision>"));
            Assert.Contains("size=\"1 2 3\"", xml);
            Assert.Contains("xyz=\"1 2 3\"", xml);
            Assert.Contains("size=\"0.10000000000000001 0.20000000000000001 0.29999999999999999\"", xml);
        }

        [Theory]
        [InlineData(0, 0.0, 1.5707963267948966, 0.0)]
        [InlineData(1, -1.5707963267948966, 0.0, 0.0)]
        [InlineData(2, 0.0, 0.0, 0.0)]
        public void TestCylinderPrimitiveRpyAlignsUrdfZToBoundingAxis(
            int axis,
            double expectedRoll,
            double expectedPitch,
            double expectedYaw)
        {
            double[] rpy = ExportHelper.GetCylinderPrimitiveRpy(axis);

            Assert.Equal(expectedRoll, rpy[0], 12);
            Assert.Equal(expectedPitch, rpy[1], 12);
            Assert.Equal(expectedYaw, rpy[2], 12);
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
        public void TestComponentBoxStlWritesOneBoxPerComponent()
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-component-boxes-" + Guid.NewGuid() + ".STL");
            ExportHelper.LinkLocalBoundingBox firstBox = new ExportHelper.LinkLocalBoundingBox();
            firstBox.Include(-0.5, -1.0, -1.5);
            firstBox.Include(0.5, 1.0, 1.5);
            ExportHelper.LinkLocalBoundingBox secondBox = new ExportHelper.LinkLocalBoundingBox();
            secondBox.Include(1.0, 2.0, 3.0);
            secondBox.Include(1.5, 2.5, 3.5);

            try
            {
                ExportHelper.WriteComponentBoxPrimitiveStl(
                    tempFile,
                    new[] { firstBox, secondBox });

                Assert.True(File.Exists(tempFile));
                Assert.Equal((uint)24, ReadBinaryStlTriangleCount(tempFile));
                Assert.Equal(ExportHelper.EstimateBinaryStlSizeBytes(24), new FileInfo(tempFile).Length);
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
        public void TestPrimitiveCylinderStlWritesExpectedBinaryTriangles()
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-cylinder-" + Guid.NewGuid() + ".STL");
            ExportHelper.LinkLocalBoundingBox box = new ExportHelper.LinkLocalBoundingBox();
            box.Include(-0.5, -0.25, -1.5);
            box.Include(0.5, 0.25, 1.5);

            try
            {
                ExportHelper.WriteCylinderPrimitiveStl(tempFile, box);

                Assert.True(File.Exists(tempFile));
                Assert.Equal((uint)96, ReadBinaryStlTriangleCount(tempFile));
                Assert.Equal(ExportHelper.EstimateBinaryStlSizeBytes(96), new FileInfo(tempFile).Length);
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
        public void TestPrimitiveSphereStlWritesExpectedBinaryTriangles()
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-sphere-" + Guid.NewGuid() + ".STL");
            ExportHelper.LinkLocalBoundingBox box = new ExportHelper.LinkLocalBoundingBox();
            box.Include(-0.5, -0.25, -1.5);
            box.Include(0.5, 0.25, 1.5);

            try
            {
                ExportHelper.WriteSpherePrimitiveStl(tempFile, box);

                Assert.True(File.Exists(tempFile));
                Assert.Equal((uint)224, ReadBinaryStlTriangleCount(tempFile));
                Assert.Equal(ExportHelper.EstimateBinaryStlSizeBytes(224), new FileInfo(tempFile).Length);
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
        public void TestConvexHullStlTriangulatesBoundingBoxHull()
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-convex-" + Guid.NewGuid() + ".STL");
            ExportHelper.LinkLocalBoundingBox box = new ExportHelper.LinkLocalBoundingBox();
            box.Include(-0.5, -1.0, -1.5);
            box.Include(0.5, 1.0, 1.5);

            try
            {
                ExportHelper.WriteConvexHullPrimitiveStl(tempFile, box);

                Assert.True(File.Exists(tempFile));
                Assert.Equal((uint)12, ReadBinaryStlTriangleCount(tempFile));
                Assert.Equal(ExportHelper.EstimateBinaryStlSizeBytes(12), new FileInfo(tempFile).Length);
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

        [Fact]
        public void TestMeshManifestCsvEscapesPathsAndKeepsSizes()
        {
            ExportHelper.MeshExportRecord record =
                new ExportHelper.MeshExportRecord(
                    "base,link",
                    "Primitive",
                    "BoxPrimitive",
                    "urdf_box_primitive",
                    "ok",
                    "STL",
                    "package://robot/meshes/visual/base_link.STL",
                    "package://robot/meshes/collision/base_link.STL",
                    @"C:\robot export\visual\base_link.STL",
                    @"C:\robot export\collision\base_link.STL",
                    true,
                    true,
                    184,
                    84,
                    2,
                    0,
                    new ExportHelper.StlExportStats
                    {
                        QualityLabel = "custom",
                        ReductionRatio = 0.5,
                        CustomSettings = true,
                        Deviation = 0.001,
                        AngleTolerance = 1.0,
                        BaselineEstimatedBytes = 5084,
                        BaselineEstimatedTriangles = 100,
                        EstimatedBytes = 2584,
                        EstimatedTriangles = 50,
                        EstimateErrorPercent = 10.5,
                        EstimatedReductionPercent = 50.0,
                        ActualReductionPercent = 98.0
                    },
                    "native:box");

            string csv = ExportHelper.BuildMeshManifestCsv(new[] { record });

            Assert.Contains("link,collision_strategy,collision_effective_strategy,collision_geometry,collision_notes,mesh_format,stl_quality,mesh_reduction_ratio", csv);
            Assert.Contains("visual_uri,collision_uri,collision_urdf_reference,visual_windows_path", csv);
            Assert.Contains("collision_vs_visual_bytes_reduction_percent,collision_vs_visual_triangles_reduction_percent", csv);
            Assert.Contains(
                "\"base,link\",Primitive,BoxPrimitive,urdf_box_primitive,ok,STL,custom,0.5,true,0.001,1,5084,100,2584,50,10.5,50,98,package://robot/meshes/visual/base_link.STL,package://robot/meshes/collision/base_link.STL,native:box",
                csv);
            Assert.Contains(",true,true,184,84,2,0,54.3478260869565,100", csv);
        }

        [Fact]
        public void TestStlTriangleCounterAcceptsValidatedBinaryStl()
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-binary-count-" + Guid.NewGuid() + ".STL");
            ExportHelper.LinkLocalBoundingBox box = new ExportHelper.LinkLocalBoundingBox();
            box.Include(-0.5, -0.5, -0.5);
            box.Include(0.5, 0.5, 0.5);

            try
            {
                ExportHelper.WriteBoxPrimitiveStl(tempFile, box);

                uint? triangleCount = ExportHelper.TryReadStlTriangleCount(tempFile);

                Assert.True(triangleCount.HasValue);
                Assert.Equal((uint)12, triangleCount.Value);
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
        public void TestStlTriangleCounterAcceptsAsciiStl()
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-ascii-count-" + Guid.NewGuid() + ".STL");
            string asciiStl =
                "solid ascii_test\n" +
                "facet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\n" +
                "facet normal 0 0 1\nouter loop\nvertex 1 0 0\nvertex 1 1 0\nvertex 0 1 0\nendloop\nendfacet\n" +
                "endsolid ascii_test\n";

            try
            {
                File.WriteAllText(tempFile, asciiStl, Encoding.ASCII);

                uint? triangleCount = ExportHelper.TryReadStlTriangleCount(tempFile);

                Assert.True(triangleCount.HasValue);
                Assert.Equal((uint)2, triangleCount.Value);
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
        public void TestStlTriangleCounterRejectsInvalidBinaryLength()
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-invalid-count-" + Guid.NewGuid() + ".STL");

            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(tempFile)))
                {
                    writer.Write(new byte[80]);
                    writer.Write((uint)12);
                    writer.Write(new byte[10]);
                }

                Assert.Null(ExportHelper.TryReadStlTriangleCount(tempFile));
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        private static string WriteGeometryXml(Geometry geometry)
        {
            StringBuilder builder = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Indent = false
            };

            using (XmlWriter writer = XmlWriter.Create(builder, settings))
            {
                geometry.WriteURDF(writer);
            }

            return builder.ToString();
        }

        private static string WriteLinkXml(Link link)
        {
            StringBuilder builder = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Indent = false
            };

            using (XmlWriter writer = XmlWriter.Create(builder, settings))
            {
                link.WriteURDF(writer);
            }

            return builder.ToString();
        }

        private static int CountOccurrences(string value, string pattern)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += pattern.Length;
            }

            return count;
        }

        private static uint ReadBinaryStlTriangleCount(string filename)
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(filename)))
            {
                reader.ReadBytes(80);
                return reader.ReadUInt32();
            }
        }
    }
}
