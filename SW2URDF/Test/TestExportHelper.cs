using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SW2URDF.Test
{
    [Collection("Requires SW Test Collection")]
    public class TestExportHelper : SW2URDFTest
    {
        public TestExportHelper(SWTestFixture fixture) : base(fixture)
        {
        }

        private static LinkNode LoadConfiguredBaseNode(ModelDoc2 doc, string modelName)
        {
            LinkNode baseNode = TestConfigurationFactory.CreateConfiguredBaseNode(
                doc,
                GetCSVPath(modelName));

            List<string> problemLinks = new List<string>();
            CommonSwOperations.LoadSWComponents(doc, baseNode, problemLinks);
            Assert.Empty(problemLinks);
            return baseNode;
        }

        private static bool ExportToTemporaryRoot(
            ExportHelper helper,
            bool exportMeshes,
            MeshExportFormat meshFormat = MeshExportFormat.STL)
        {
            string exportRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(exportRoot);
            helper.SavePath = exportRoot;
            try
            {
                return helper.ExportRobot(exportMeshes, meshFormat);
            }
            finally
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, true);
                }
            }
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4, MeshExportFormat.STL)]
        [InlineData("4_WHEELER", 5, MeshExportFormat.STL)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4, MeshExportFormat.STL)]
        [InlineData("3_DOF_ARM", 4, MeshExportFormat.THREEDXML)]
        [InlineData("4_WHEELER", 5, MeshExportFormat.THREEDXML)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4, MeshExportFormat.THREEDXML)]
        public void TestExportRobot(string modelName, int expNumLinks, MeshExportFormat meshExportFormat)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = LoadConfiguredBaseNode(doc, modelName);
            Assert.True(helper.CreateRobotFromTreeView(baseNode), helper.ExportErrorWhy);
            Assert.True(
                ExportToTemporaryRoot(helper, true, meshExportFormat),
                helper.ExportErrorWhy);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4)]
        public void TestExportRobotNoSTL(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = LoadConfiguredBaseNode(doc, modelName);
            Assert.True(helper.CreateRobotFromTreeView(baseNode), helper.ExportErrorWhy);
            Assert.True(ExportToTemporaryRoot(helper, false), helper.ExportErrorWhy);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4)]
        public void TestExportRobotSkipInertial(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(false);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = LoadConfiguredBaseNode(doc, modelName);
            Assert.True(helper.CreateRobotFromTreeView(baseNode), helper.ExportErrorWhy);
            Assert.True(ExportToTemporaryRoot(helper, true), helper.ExportErrorWhy);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4)]
        public void TestExportRobotSkipVisual(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(false);
            LinkNode baseNode = LoadConfiguredBaseNode(doc, modelName);
            Assert.True(helper.CreateRobotFromTreeView(baseNode), helper.ExportErrorWhy);
            Assert.True(ExportToTemporaryRoot(helper, true), helper.ExportErrorWhy);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4)]
        public void TestExportRobotSkipKinematics(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(false);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = LoadConfiguredBaseNode(doc, modelName);
            Assert.True(helper.CreateRobotFromTreeView(baseNode), helper.ExportErrorWhy);
            Assert.True(ExportToTemporaryRoot(helper, true), helper.ExportErrorWhy);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 4)]
        [InlineData("4_WHEELER", 5)]
        [InlineData("ORIGINAL_3_DOF_ARM", 4)]
        public void TestExportRobotSkipLimits(string modelName, int expNumLinks)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(false);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = LoadConfiguredBaseNode(doc, modelName);
            Assert.True(helper.CreateRobotFromTreeView(baseNode), helper.ExportErrorWhy);
            Assert.True(ExportToTemporaryRoot(helper, true), helper.ExportErrorWhy);
            Assert.NotNull(helper.URDFRobot);
            Assert.Equal(expNumLinks, CommonSwOperations.GetCount(helper.URDFRobot.BaseLink));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", 3)]
        [InlineData("4_WHEELER", 4)]
        [InlineData("ORIGINAL_3_DOF_ARM", 3)]
        public void TestGetJointNames(string modelName, int expNumJoints)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            LinkNode baseNode = LoadConfiguredBaseNode(doc, modelName);
            Assert.True(helper.CreateRobotFromTreeView(baseNode), helper.ExportErrorWhy);
            Assert.True(ExportToTemporaryRoot(helper, true), helper.ExportErrorWhy);
            List<string> jointNames = helper.GetJointNames();
            Assert.NotNull(jointNames);
            Assert.Equal(jointNames.Count, expNumJoints);
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        /*
         * TODO(SIMINT-164) Part document tests not working (OpenSWPartDocument)
        [Theory]
        [InlineData("TOY_BLOCK")]
        public void TestExportLink(string modelName)
        {
            ModelDoc2 doc = OpenSWPartDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.ExportLink(true);
            Assert.True(true, "Part export failed");
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("TOY_BLOCK")]
        public void TestCreateRobotFromActiveModel(string modelName)
        {
            ModelDoc2 doc = OpenSWPartDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            helper.CreateRobotFromActiveModel();
            Assert.NotNull(helper.URDFRobot);
            Assert.True(SwApp.CloseAllDocuments(true));
        }
        */

        [Theory]
        [InlineData("3_DOF_ARM")]
        public void TestNameBasedFixtureRequiresNewVersionTwoConfiguration(string modelName)
        {
            ModelDoc2 doc = OpenSWDocument(modelName);
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(
                doc,
                out string errorMessage);

            Assert.Null(baseNode);
            Assert.False(String.IsNullOrWhiteSpace(errorMessage));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Fact]
        public void ThreeDxmlOutputProjectionDoesNotMutateTheActiveRobot()
        {
            ModelDoc2 doc = OpenSWDocument("3_DOF_ARM");
            ExportHelper helper = new ExportHelper(SwApp);
            helper.SetComputeInertial(true);
            helper.SetComputeJointKinematics(true);
            helper.SetComputeJointLimits(true);
            helper.SetComputeVisualCollision(true);
            LinkNode baseNode = LoadConfiguredBaseNode(doc, "3_DOF_ARM");
            Assert.True(helper.CreateRobotFromTreeView(baseNode), helper.ExportErrorWhy);

            Dictionary<string, double[]> activeBefore = CaptureMeshOrigins(
                helper.URDFRobot);
            MethodInfo createProjection = typeof(ExportHelper).GetMethod(
                "CreateMeshOutputRobot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(createProjection);

            Robot firstProjection = (Robot)createProjection.Invoke(
                helper,
                new object[] { MeshExportFormat.THREEDXML });
            Robot secondProjection = (Robot)createProjection.Invoke(
                helper,
                new object[] { MeshExportFormat.THREEDXML });

            AssertMeshOriginsEqual(
                activeBefore,
                CaptureMeshOrigins(helper.URDFRobot));
            AssertMeshOriginsEqual(
                CaptureMeshOrigins(firstProjection),
                CaptureMeshOrigins(secondProjection));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", new double[] { 0, 0, 1 }, "Origin_global", new double[] { 0, -1, 0 })]
        public void TestLocalizeAxis(string modelName, double[] axis, string coordSys, double[] expected)
        {
            OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            ReferenceGeometryEntry frame = helper.GetRefCoordinateSystems()
                .First(entry =>
                    entry.DisplayName == coordSys &&
                    String.IsNullOrWhiteSpace(entry.ComponentPath));
            Assert.Equal(expected, helper.LocalizeAxis(axis, frame.Reference));
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", new string[] {
            "Origin_global",
            "Origin_prox_joint",
            "Origin_dist_joint",
            "Origin_effector_joint" })]
        public void TestGetRefCoordinateSystems(string modelName, string[] expected)
        {
            OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            List<string> actual = helper.GetRefCoordinateSystems()
                .Where(entry => String.IsNullOrWhiteSpace(entry.ComponentPath))
                .Select(entry => entry.DisplayName)
                .ToList();
            foreach (string expectedName in expected)
            {
                Assert.Contains(expectedName, actual);
            }
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Theory]
        [InlineData("3_DOF_ARM", new string[] {
            "Axis_prox_joint",
            "Axis_dist_joint",
            "Axis_effector_joint" })]
        public void TestGetRefAxes(string modelName, string[] expected)
        {
            OpenSWDocument(modelName);
            ExportHelper helper = new ExportHelper(SwApp);
            List<string> actual = helper.GetRefAxes()
                .Where(entry => String.IsNullOrWhiteSpace(entry.ComponentPath))
                .Select(entry => entry.DisplayName)
                .ToList();
            foreach (string expectedName in expected)
            {
                Assert.Contains(expectedName, actual);
            }
            Assert.True(SwApp.CloseAllDocuments(true));
        }

        [Fact]
        public void RepeatedComponentInstancesResolveTheirOwnReferenceGeometryContext()
        {
            ModelDoc2 model = OpenSWDocument("ORIGINAL_3_DOF_ARM");
            ReferenceGeometryEntry[] repeatedFrames =
                new ReferenceGeometryCatalog(model).CoordinateSystems
                    .Where(entry =>
                        string.Equals(
                            entry.DisplayName,
                            "Joint Origin",
                            StringComparison.Ordinal) &&
                        new[]
                        {
                            "Arm_link-1",
                            "Arm_link-2",
                            "Arm_link-3"
                        }.Contains(
                            (entry.ComponentPath ?? string.Empty)
                                .Split('/')
                                .Last(),
                            StringComparer.Ordinal) &&
                        entry.Reference.OwnerScope ==
                            ReferenceGeometryOwnerScope.ComponentInstance)
                    .ToArray();
            Assert.Equal(3, repeatedFrames.Length);
            Assert.Equal(
                repeatedFrames.Length,
                repeatedFrames.Select(entry => entry.Reference.IdentityKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count());

            ReferenceGeometryResolver resolver =
                new ReferenceGeometryResolver(model);
            List<double[]> resolvedOrigins = new List<double[]>();
            foreach (ReferenceGeometryEntry entry in repeatedFrames)
            {
                MathTransform transform = resolver.ResolveCoordinateSystemTransform(
                    entry.Reference,
                    out ReferenceGeometryResolution resolution);
                Assert.True(
                    resolution.IsResolved,
                    entry.DisplayLabel + ": " + resolution.Message);
                Assert.NotNull(transform);
                resolvedOrigins.Add(MathOps.GetXYZ(transform));
            }
            for (int left = 0; left < resolvedOrigins.Count; left++)
            {
                for (int right = left + 1; right < resolvedOrigins.Count; right++)
                {
                    Assert.True(
                        resolvedOrigins[left]
                            .Zip(
                                resolvedOrigins[right],
                                (first, second) => Math.Abs(first - second))
                            .Any(delta => delta > 1e-9),
                        "Repeated component instances resolved to the same root-frame origin.");
                }
            }

            Assert.True(SwApp.CloseAllDocuments(true));
        }

        private static Dictionary<string, double[]> CaptureMeshOrigins(Robot robot)
        {
            Dictionary<string, double[]> origins =
                new Dictionary<string, double[]>(StringComparer.Ordinal);
            Queue<Link> links = new Queue<Link>();
            links.Enqueue(robot.BaseLink);
            while (links.Count > 0)
            {
                Link link = links.Dequeue();
                origins.Add(
                    link.Name,
                    link.Visual.Origin.GetXYZ()
                        .Concat(link.Visual.Origin.GetRPY())
                        .Concat(link.Collision.Origin.GetXYZ())
                        .Concat(link.Collision.Origin.GetRPY())
                        .ToArray());
                foreach (Link child in link.Children)
                {
                    links.Enqueue(child);
                }
            }
            return origins;
        }

        private static void AssertMeshOriginsEqual(
            IDictionary<string, double[]> expected,
            IDictionary<string, double[]> actual)
        {
            Assert.Equal(expected.Keys.OrderBy(key => key), actual.Keys.OrderBy(key => key));
            foreach (string linkName in expected.Keys)
            {
                Assert.Equal(expected[linkName], actual[linkName]);
            }
        }
    }
}
