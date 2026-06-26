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
