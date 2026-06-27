using Moq;
using SW2URDF.UI;
using SW2URDF.URDFExport;
using System;
using System.IO;
using System.Text;
using System.Windows;
using Xunit;

namespace SW2URDF.Test
{
    public class TestURDFPackage
    {
        private static string CreateRandomTempDirectory()
        {
            string name = Path.GetRandomFileName();
            string tempDirectory = Path.Combine(Path.GetTempPath(), name);
            Assert.True(Directory.CreateDirectory(tempDirectory).Exists);
            return tempDirectory;
        }

        [Fact]
        public void TestRos1AndRos2PackageDirectories()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("Robot Name.SLDASM", tempDirectory);

            Assert.Equal(Path.Combine(tempDirectory, "ROS1", "robot_name") + Path.DirectorySeparatorChar,
                pkg.WindowsPackageDirectory);
            Assert.Equal(Path.Combine(tempDirectory, "ROS2", "robot_name") + Path.DirectorySeparatorChar,
                pkg.WindowsRos2PackageDirectory);
            Assert.Equal(Path.Combine(tempDirectory, "export.log"), pkg.WindowsExportLogFile);
            Assert.Equal("robot_name", pkg.RobotName);
            Assert.Equal("robot_name", pkg.PackageName);
            Assert.Equal("robot_name", pkg.Ros2PackageName);
        }

        [Fact]
        public void TestRobotAndRosPackageNamesCanDiffer()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage(
                "osracer_blue",
                "osracer_description",
                tempDirectory);

            Assert.Equal("osracer_blue", pkg.RobotName);
            Assert.Equal("osracer_description", pkg.PackageName);
            Assert.Equal("osracer_description", pkg.Ros2PackageName);
            Assert.Equal(
                Path.Combine(tempDirectory, "ROS1", "osracer_description") + Path.DirectorySeparatorChar,
                pkg.WindowsPackageDirectory);
            Assert.Equal(
                Path.Combine(tempDirectory, "ROS2", "osracer_description") + Path.DirectorySeparatorChar,
                pkg.WindowsRos2PackageDirectory);

            Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
            messageBoxMock.Setup(m => m.Show(It.IsAny<string>()))
                .Returns(MessageBoxResult.OK);
            URDFPackage.MessageBox = messageBoxMock.Object;
            pkg.CreateDirectories();
            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"osracer_blue\"><link name=\"base_link\" /></robot>",
                new UTF8Encoding(false));
            pkg.CreateRos2Package(ros1Urdf);

            Assert.True(File.Exists(
                Path.Combine(pkg.WindowsRos2RobotsDirectory, "osracer_blue.urdf")));
            string displayLaunch = File.ReadAllText(
                Path.Combine(pkg.WindowsRos2LaunchDirectory, "display.launch.py"));
            Assert.Contains("get_package_share_directory('osracer_description')", displayLaunch);
            Assert.Contains("'osracer_blue.urdf'", displayLaunch);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestCreateDirectories()
        {
            string tempDirectory = CreateRandomTempDirectory();
            string name = Path.GetRandomFileName();
            URDFPackage pkg = new URDFPackage(name, tempDirectory);

            Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
            messageBoxMock.Setup(m => m.Show(It.IsAny<string>()))
                .Returns(MessageBoxResult.OK); //can be whatever depends on test case
            URDFPackage.MessageBox = messageBoxMock.Object;
            pkg.CreateDirectories();

            Assert.True(Directory.Exists(pkg.WindowsPackageDirectory));
            Assert.True(Directory.Exists(pkg.WindowsMeshesDirectory));
            Assert.True(Directory.Exists(pkg.WindowsRobotsDirectory));
            Assert.True(Directory.Exists(pkg.WindowsTexturesDirectory));
            Assert.True(Directory.Exists(pkg.WindowsLaunchDirectory));
            Assert.True(Directory.Exists(pkg.WindowsConfigDirectory));

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestCreateCMakeLists()
        {
            string tempDirectory = CreateRandomTempDirectory();
            string name = Path.GetRandomFileName();
            URDFPackage pkg = new URDFPackage(name, tempDirectory);
            Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
            messageBoxMock.Setup(m => m.Show(It.IsAny<string>()))
                .Returns(MessageBoxResult.OK); //can be whatever depends on test case
            URDFPackage.MessageBox = messageBoxMock.Object;
            pkg.CreateDirectories();
            pkg.CreateCMakeLists();

            Assert.True(File.Exists(pkg.WindowsCMakeLists));

            Console.WriteLine("Deleting directory " + tempDirectory);
            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestCreateConfigYAML()
        {
            string tempDirectory = CreateRandomTempDirectory();
            string name = Path.GetRandomFileName();
            Directory.CreateDirectory(tempDirectory);
            URDFPackage pkg = new URDFPackage(name, tempDirectory);
            Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
            messageBoxMock.Setup(m => m.Show(It.IsAny<string>()))
                .Returns(MessageBoxResult.OK); //can be whatever depends on test case
            URDFPackage.MessageBox = messageBoxMock.Object;
            pkg.CreateDirectories();
            pkg.CreateConfigYAML(new string[] {"a", "b", "c"});

            Assert.True(File.Exists(pkg.WindowsConfigYAML));

            Console.WriteLine("Deleting directory " + tempDirectory);
            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestChinesePathPackageGeneration()
        {
            string tempDirectory = CreateRandomTempDirectory();
            string chineseDirectory = Path.Combine(tempDirectory, "中文 导出 路径");
            Directory.CreateDirectory(chineseDirectory);

            URDFPackage pkg = new URDFPackage("osracer_blue.SLDASM", chineseDirectory);
            Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
            messageBoxMock.Setup(m => m.Show(It.IsAny<string>()))
                .Returns(MessageBoxResult.OK);
            URDFPackage.MessageBox = messageBoxMock.Object;

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            pkg.CreateConfigYAML(new string[] { "joint_a" });

            string urdfPath = Path.Combine(pkg.WindowsRobotsDirectory, pkg.PackageName + ".urdf");
            File.WriteAllText(urdfPath,
                "<?xml version=\"1.0\"?><robot name=\"robot\"><link name=\"base_link\" /></robot>",
                new UTF8Encoding(false));
            pkg.CreateRos2Package(urdfPath);

            Assert.Equal("osracer_blue", pkg.PackageName);
            Assert.True(Directory.Exists(pkg.WindowsPackageDirectory));
            Assert.True(Directory.Exists(pkg.WindowsRos2PackageDirectory));
            Assert.True(File.Exists(pkg.WindowsCMakeLists));
            Assert.True(File.Exists(pkg.WindowsConfigYAML));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2PackageDirectory, "package.xml")));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2PackageDirectory, "setup.py")));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2ResourceDirectory, pkg.Ros2PackageName)));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2RobotsDirectory, pkg.Ros2PackageName + ".urdf")));

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestRos2PackageCopiesNestedMeshDirectories()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);
            Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
            messageBoxMock.Setup(m => m.Show(It.IsAny<string>()))
                .Returns(MessageBoxResult.OK);
            URDFPackage.MessageBox = messageBoxMock.Object;

            pkg.CreateDirectories();
            string visualMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "visual");
            string collisionMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "collision");
            Directory.CreateDirectory(visualMeshDirectory);
            Directory.CreateDirectory(collisionMeshDirectory);
            File.WriteAllText(Path.Combine(visualMeshDirectory, "base_link.STL"), "visual");
            File.WriteAllText(Path.Combine(collisionMeshDirectory, "base_link.STL"), "collision");
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\nbase_link,true,true\r\n",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\">" +
                "<link name=\"base_link\"><visual><geometry><mesh filename=\"package://rover_description/meshes/visual/base_link.STL\" /></geometry></visual>" +
                "<collision><geometry><mesh filename=\"package://rover_description/meshes/collision/base_link.STL\" /></geometry></collision></link></robot>",
                new UTF8Encoding(false));

            pkg.CreateRos2Package(ros1Urdf);

            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2MeshesDirectory, "visual", "base_link.STL")));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2MeshesDirectory, "collision", "base_link.STL")));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2ConfigDirectory, "inertial_validation.csv")));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2ConfigDirectory, "mesh_manifest.csv")));
            string setupPy = File.ReadAllText(Path.Combine(pkg.WindowsRos2PackageDirectory, "setup.py"));
            Assert.Contains("package_files('meshes')", setupPy);
            Assert.Contains("glob(os.path.join(directory, '**', '*'), recursive=True)", setupPy);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportIsWrittenToRos1AndRos2Config()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);
            Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
            messageBoxMock.Setup(m => m.Show(It.IsAny<string>()))
                .Returns(MessageBoxResult.OK);
            URDFPackage.MessageBox = messageBoxMock.Object;

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));

            string visualMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "visual");
            string collisionMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "collision");
            Directory.CreateDirectory(visualMeshDirectory);
            Directory.CreateDirectory(collisionMeshDirectory);
            string visualMesh = Path.Combine(visualMeshDirectory, "base_link.STL");
            string collisionMesh = Path.Combine(collisionMeshDirectory, "base_link.STL");
            File.WriteAllText(visualMesh, "visual");
            File.WriteAllText(collisionMesh, "collision");
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\nbase_link,true,true\r\n",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\">" +
                "<link name=\"base_link\"><visual><geometry><mesh filename=\"package://rover_description/meshes/visual/base_link.STL\" /></geometry></visual>" +
                "<collision><geometry><mesh filename=\"package://rover_description/meshes/collision/base_link.STL\" /></geometry></collision></link></robot>",
                new UTF8Encoding(false));
            pkg.CreateRos2Package(ros1Urdf);

            ExportHelper.InertialValidationRecord inertialRecord =
                new ExportHelper.InertialValidationRecord(
                    "base_link",
                    "Origin_global",
                    new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0));
            ExportHelper.MeshExportRecord meshRecord =
                new ExportHelper.MeshExportRecord(
                    "base_link",
                    "VisualMesh",
                    "VisualMesh",
                    "visual_mesh_copy",
                    "ok",
                    "STL",
                    "package://rover_description/meshes/visual/base_link.STL",
                    "package://rover_description/meshes/collision/base_link.STL",
                    visualMesh,
                    collisionMesh,
                    true,
                    true,
                    new FileInfo(visualMesh).Length,
                    new FileInfo(collisionMesh).Length,
                    0,
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
                        EstimateErrorPercent = 0.0,
                        EstimatedReductionPercent = 50.0,
                        ActualReductionPercent = 50.0
                    });

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[] { inertialRecord },
                new[] { meshRecord },
                true,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string ros1Report = Path.Combine(pkg.WindowsConfigDirectory, "export_report.md");
            string ros2Report = Path.Combine(pkg.WindowsRos2ConfigDirectory, "export_report.md");
            Assert.True(File.Exists(ros1Report));
            Assert.True(File.Exists(ros2Report));

            string report = File.ReadAllText(ros1Report, Encoding.UTF8);
            Assert.Contains("Status: PASS", report);
            Assert.Contains("Plugin version: ", report);
            Assert.Contains("Commit hash: ", report);
            Assert.Contains("Build time UTC: ", report);
            Assert.Contains("Dirty state: ", report);
            Assert.Contains("Export parameters: export_meshes=true, mesh_format=STL", report);
            Assert.Contains("inertial_validation_rows=1", report);
            Assert.Contains("mesh_manifest_rows=1", report);
            Assert.Contains("requested_collision_strategies=VisualMesh=1", report);
            Assert.Contains("stl_reduction_ratios=0.5=1", report);
            Assert.Contains("## Export Parameters", report);
            Assert.Contains("| output_root | " + pkg.WindowsExportRootDirectory + " |", report);
            Assert.Contains("| robot_name | robot_900001 |", report);
            Assert.Contains("| ros1_package_name | rover_description |", report);
            Assert.Contains("| ros2_package_name | rover_description |", report);
            Assert.Contains("| mesh_manifest_rows | 1 |", report);
            Assert.Contains("| requested_collision_strategies | VisualMesh=1 |", report);
            Assert.Contains("| stl_reduction_ratios | 0.5=1 |", report);
            Assert.Contains("## ROS 1 URDF", report);
            Assert.Contains("## ROS 2 URDF", report);
            Assert.Contains("ROS 2 setup.py | OK", report);
            Assert.Contains("ROS 2 display.launch.py | OK", report);
            Assert.Contains("ROS 2 gazebo.launch.py | OK", report);
            Assert.Contains("ROS 1 visual mesh files | OK", report);
            Assert.Contains("ROS 1 collision mesh files | OK", report);
            Assert.Contains("ROS 2 visual mesh files | OK", report);
            Assert.Contains("ROS 2 collision mesh files | OK", report);
            Assert.Contains("Inertial validation rows: 1", report);
            Assert.Contains("Warning rows: 0", report);
            Assert.Contains("Physical inertia failures: 0", report);
            Assert.Contains("Inertia display warnings: 0", report);
            Assert.Contains("Display blocked by invalid physics: 0", report);
            Assert.Contains("Mesh manifest rows: 1", report);
            Assert.Contains("Requested collision strategies: VisualMesh=1", report);
            Assert.Contains("Effective collision strategies: VisualMesh=1", report);
            Assert.Contains("Collision strategy fallbacks: 0", report);
            Assert.Contains("Baseline estimated visual STL triangles: 100", report);
            Assert.Contains("Estimated visual STL triangles: 50", report);
            Assert.Contains("Requested STL reduction ratios: 0.5=1", report);
            Assert.Contains("STL quality settings: custom=1", report);
            Assert.Contains("Average estimated STL reduction: 50%", report);
            Assert.Contains("Average actual STL reduction: 50%", report);
            Assert.DoesNotContain("FAIL:", report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportFailsWhenRos2MeshReferencesAreMissing()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);
            Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
            messageBoxMock.Setup(m => m.Show(It.IsAny<string>()))
                .Returns(MessageBoxResult.OK);
            URDFPackage.MessageBox = messageBoxMock.Object;

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));

            string visualMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "visual");
            string collisionMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "collision");
            Directory.CreateDirectory(visualMeshDirectory);
            Directory.CreateDirectory(collisionMeshDirectory);
            string visualMesh = Path.Combine(visualMeshDirectory, "base_link.STL");
            string collisionMesh = Path.Combine(collisionMeshDirectory, "base_link.STL");
            File.WriteAllText(visualMesh, "visual");
            File.WriteAllText(collisionMesh, "collision");
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\nbase_link,true,true\r\n",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\">" +
                "<link name=\"base_link\"><visual><geometry><mesh filename=\"package://rover_description/meshes/visual/base_link.STL\" /></geometry></visual>" +
                "<collision><geometry><mesh filename=\"package://rover_description/meshes/collision/base_link.STL\" /></geometry></collision></link></robot>",
                new UTF8Encoding(false));
            pkg.CreateRos2Package(ros1Urdf);

            Directory.Delete(pkg.WindowsRos2MeshesDirectory, true);
            Directory.CreateDirectory(Path.Combine(pkg.WindowsRos2MeshesDirectory, "visual"));
            Directory.CreateDirectory(Path.Combine(pkg.WindowsRos2MeshesDirectory, "collision"));

            ExportHelper.MeshExportRecord meshRecord =
                new ExportHelper.MeshExportRecord(
                    "base_link",
                    "VisualMesh",
                    "VisualMesh",
                    "visual_mesh_copy",
                    "ok",
                    "STL",
                    "package://rover_description/meshes/visual/base_link.STL",
                    "package://rover_description/meshes/collision/base_link.STL",
                    visualMesh,
                    collisionMesh,
                    true,
                    true,
                    new FileInfo(visualMesh).Length,
                    new FileInfo(collisionMesh).Length,
                    0,
                    0,
                    ExportHelper.StlExportStats.NotExported());

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[]
                {
                    new ExportHelper.InertialValidationRecord(
                        "base_link",
                        "Origin_global",
                        new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0))
                },
                new[] { meshRecord },
                true,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);
            Assert.Contains("Status: FAIL", report);
            Assert.Contains("ROS 2 visual mesh files | MISSING", report);
            Assert.Contains("ROS 2 collision mesh files | MISSING", report);
            Assert.Contains(
                "FAIL: ROS 2 mesh reference is unresolved: package://rover_description/meshes/visual/base_link.STL",
                report);
            Assert.Contains(
                "FAIL: ROS 2 mesh reference is unresolved: package://rover_description/meshes/collision/base_link.STL",
                report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportFailsWhenMeshDirectoriesAreEmpty()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);
            Mock<IMessageBox> messageBoxMock = new Mock<IMessageBox>();
            messageBoxMock.Setup(m => m.Show(It.IsAny<string>()))
                .Returns(MessageBoxResult.OK);
            URDFPackage.MessageBox = messageBoxMock.Object;

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            Directory.CreateDirectory(Path.Combine(pkg.WindowsMeshesDirectory, "visual"));
            Directory.CreateDirectory(Path.Combine(pkg.WindowsMeshesDirectory, "collision"));
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\nbase_link,false,false\r\n",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\"><link name=\"base_link\" /></robot>",
                new UTF8Encoding(false));
            pkg.CreateRos2Package(ros1Urdf);

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[]
                {
                    new ExportHelper.InertialValidationRecord(
                        "base_link",
                        "Origin_global",
                        new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0))
                },
                new ExportHelper.MeshExportRecord[0],
                true,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);
            Assert.Contains("Status: FAIL", report);
            Assert.Contains("ROS 1 visual mesh files | MISSING", report);
            Assert.Contains("ROS 1 collision mesh files | MISSING", report);
            Assert.Contains("ROS 2 visual mesh files | MISSING", report);
            Assert.Contains("ROS 2 collision mesh files | MISSING", report);

            Directory.Delete(tempDirectory, true);
        }

        [Theory]
        [InlineData("osracer_blue.SLDASM", "osracer_blue")]
        [InlineData("OSRacer Blue.SLDPRT", "osracer_blue")]
        [InlineData("123 robot", "robot_123_robot")]
        public void TestSanitizePackageName(string input, string expected)
        {
            Assert.Equal(expected, URDFPackage.SanitizePackageName(input));
        }
    }
}
