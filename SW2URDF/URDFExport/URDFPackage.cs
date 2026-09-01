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

using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SW2URDF.URDFExport
{
    public class URDFPackage
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        public string PackageName { get; }
        public string RobotName { get; }

        public string PackageDirectory { get; }
        public string MeshesDirectory { get; }
        public string TexturesDirectory { get; }
        public string RobotsDirectory { get; }
        public string ConfigDirectory { get; }
        public string LaunchDirectory { get; }

        public string WindowsPackageDirectory { get; }
        public string WindowsExportRootDirectory { get; }
        public string WindowsExportLogFile { get; }
        public string WindowsExportReportFile { get; }
        public string WindowsMeshesDirectory { get; }
        public string WindowsTexturesDirectory { get; }
        public string WindowsRobotsDirectory { get; }
        public string WindowsLaunchDirectory { get; }
        public string WindowsConfigDirectory { get; }
        public string WindowsCMakeLists { get; }
        public string WindowsConfigYAML { get; }
        public string Ros2PackageName { get; }
        public string WindowsRos2PackageDirectory { get; }
        public string WindowsRos2MeshesDirectory { get; }
        public string WindowsRos2RobotsDirectory { get; }
        public string WindowsRos2TexturesDirectory { get; }
        public string WindowsRos2LaunchDirectory { get; }
        public string WindowsRos2ConfigDirectory { get; }
        public string WindowsRos2ResourceDirectory { get; }
        public string WindowsUsdAssetDirectory { get; }
        public string WindowsMjcfAssetDirectory { get; }

        public URDFPackage(string name, string dir)
            : this(name, name, dir)
        {
        }

        public URDFPackage(string robotName, string packageName, string dir)
        {
            RobotName = SanitizePackageName(robotName);
            PackageName = SanitizePackageName(packageName);
            Ros2PackageName = PackageName;
            PackageDirectory = @"package://" + PackageName + @"/";
            MeshesDirectory = PackageDirectory + @"meshes/";
            RobotsDirectory = PackageDirectory + @"urdf/";
            TexturesDirectory = PackageDirectory + @"textures/";
            LaunchDirectory = PackageDirectory + @"launch/";
            ConfigDirectory = PackageDirectory + @"config/";

            char last = dir[dir.Length - 1];
            dir = (last == '\\') ? dir : dir + @"\";
            WindowsExportRootDirectory = dir;
            WindowsExportLogFile = WindowsExportRootDirectory + "export.log";
            WindowsExportReportFile = WindowsExportRootDirectory + "export_report.md";
            WindowsPackageDirectory = dir + @"ROS1\" + PackageName + @"\";
            WindowsMeshesDirectory = WindowsPackageDirectory + @"meshes\";
            WindowsRobotsDirectory = WindowsPackageDirectory + @"urdf\";
            WindowsTexturesDirectory = WindowsPackageDirectory + @"textures\";
            WindowsLaunchDirectory = WindowsPackageDirectory + @"launch\";
            WindowsConfigDirectory = WindowsPackageDirectory + @"config\";
            WindowsCMakeLists = WindowsPackageDirectory + @"CMakeLists.txt";
            WindowsConfigYAML = WindowsConfigDirectory + @"joint_names_" + PackageName + ".yaml";

            WindowsRos2PackageDirectory = dir + @"ROS2\" + Ros2PackageName + @"\";
            WindowsRos2MeshesDirectory = WindowsRos2PackageDirectory + @"meshes\";
            WindowsRos2RobotsDirectory = WindowsRos2PackageDirectory + @"urdf\";
            WindowsRos2TexturesDirectory = WindowsRos2PackageDirectory + @"textures\";
            WindowsRos2LaunchDirectory = WindowsRos2PackageDirectory + @"launch\";
            WindowsRos2ConfigDirectory = WindowsRos2PackageDirectory + @"config\";
            WindowsRos2ResourceDirectory = WindowsRos2PackageDirectory + @"resource\";
            WindowsUsdAssetDirectory = dir + @"USD\" + PackageName + @"\";
            WindowsMjcfAssetDirectory = dir + @"MuJoCo\";
        }

        public void CreateDirectories()
        {
            logger.Info("Creating ROS 1 package directories at " + WindowsPackageDirectory);
            if (!Directory.Exists(WindowsPackageDirectory))
            {
                Directory.CreateDirectory(WindowsPackageDirectory);
            }
            if (!Directory.Exists(WindowsMeshesDirectory))
            {
                Directory.CreateDirectory(WindowsMeshesDirectory);
            }
            if (!Directory.Exists(WindowsRobotsDirectory))
            {
                Directory.CreateDirectory(WindowsRobotsDirectory);
            }
            if (!Directory.Exists(WindowsTexturesDirectory))
            {
                Directory.CreateDirectory(WindowsTexturesDirectory);
            }
            if (!Directory.Exists(WindowsLaunchDirectory))
            {
                Directory.CreateDirectory(WindowsLaunchDirectory);
            }
            if (!Directory.Exists(WindowsConfigDirectory))
            {
                Directory.CreateDirectory(WindowsConfigDirectory);
            }
        }

        public void CreateCMakeLists()
        {
            logger.Info("Creating ROS 1 CMakeLists.txt at " + WindowsCMakeLists);
            using (StreamWriter file = new StreamWriter(WindowsCMakeLists))
            {
                file.WriteLine("cmake_minimum_required(VERSION 2.8.3)\r\n");
                file.WriteLine("project(" + PackageName + ")\r\n");
                file.WriteLine("find_package(catkin REQUIRED)\r\n");
                file.WriteLine("catkin_package()\r\n");
                file.WriteLine("find_package(roslaunch)\r\n");
                file.WriteLine("foreach(dir config launch meshes urdf)");
                file.WriteLine("\tinstall(DIRECTORY ${dir}/");
                file.WriteLine("\t\tDESTINATION ${CATKIN_PACKAGE_SHARE_DESTINATION}/${dir})");
                file.WriteLine("endforeach(dir)");
            }
        }

        public void CreateConfigYAML(String[] jointNames)
        {
            logger.Info("Creating ROS 1 joint config at " + WindowsConfigYAML +
                " with " + jointNames.Length + " joints");
            using (StreamWriter file = new StreamWriter(WindowsConfigYAML))
            {
                file.Write("controller_joint_names: " + "[");

                foreach (String name in jointNames)
                {
                    file.Write("'" + name + "', ");
                }

                file.WriteLine("]");
            }
        }

        public void CreateRos2Package(string windowsURDFFileName)
        {
            logger.Info("Creating ROS 2 package at " + WindowsRos2PackageDirectory);
            CreateRos2Directories();
            logger.Info("Copying ROS 2 meshes from " + WindowsMeshesDirectory + " to " + WindowsRos2MeshesDirectory);
            int copiedMeshFiles = CopyDirectory(WindowsMeshesDirectory, WindowsRos2MeshesDirectory);
            if (copiedMeshFiles == 0)
            {
                logger.Warn("ROS 2 mesh copy produced no files from " + WindowsMeshesDirectory);
            }
            logger.Info("Copying ROS 2 textures from " + WindowsTexturesDirectory + " to " + WindowsRos2TexturesDirectory);
            CopyDirectory(WindowsTexturesDirectory, WindowsRos2TexturesDirectory);
            logger.Info("Copying ROS 2 config from " + WindowsConfigDirectory + " to " + WindowsRos2ConfigDirectory);
            int copiedConfigFiles = CopyDirectory(WindowsConfigDirectory, WindowsRos2ConfigDirectory);
            if (copiedConfigFiles == 0)
            {
                logger.Warn("ROS 2 config copy produced no files from " + WindowsConfigDirectory);
            }

            string ros2URDFFileName = WindowsRos2RobotsDirectory + RobotName + ".urdf";
            logger.Info("Creating ROS 2 URDF at " + ros2URDFFileName);
            string urdf = File.ReadAllText(windowsURDFFileName, Encoding.UTF8);
            urdf = urdf.Replace("package://" + PackageName + "/", "package://" + Ros2PackageName + "/");
            File.WriteAllText(ros2URDFFileName, urdf, new UTF8Encoding(false));

            CreateRos2PackageXml();
            CreateRos2SetupPy();
            CreateRos2ResourceMarker();
            CreateRos2DisplayLaunch();
            CreateRos2GazeboLaunch();
            logger.Info("Finished creating ROS 2 package at " + WindowsRos2PackageDirectory);
        }

        private void CreateRos2Directories()
        {
            logger.Info("Creating ROS 2 package directories");
            Directory.CreateDirectory(WindowsRos2PackageDirectory);
            Directory.CreateDirectory(WindowsRos2MeshesDirectory);
            Directory.CreateDirectory(WindowsRos2RobotsDirectory);
            Directory.CreateDirectory(WindowsRos2TexturesDirectory);
            Directory.CreateDirectory(WindowsRos2LaunchDirectory);
            Directory.CreateDirectory(WindowsRos2ConfigDirectory);
            Directory.CreateDirectory(WindowsRos2ResourceDirectory);
        }

        private void CreateRos2PackageXml()
        {
            string path = WindowsRos2PackageDirectory + "package.xml";
            logger.Info("Creating ROS 2 package.xml at " + path);
            using (StreamWriter file = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                file.WriteLine("<?xml version=\"1.0\"?>");
                file.WriteLine("<package format=\"3\">");
                file.WriteLine("  <name>" + Ros2PackageName + "</name>");
                file.WriteLine("  <version>1.0.0</version>");
                file.WriteLine("  <description>ROS 2 URDF description package for " + PackageName + "</description>");
                file.WriteLine("  <maintainer email=\"" + PackageXML.DefaultMaintainerEmail + "\">" +
                    PackageXML.DefaultMaintainerName + "</maintainer>");
                file.WriteLine("  <license>BSD</license>");
                file.WriteLine("  <buildtool_depend>ament_python</buildtool_depend>");
                file.WriteLine("  <exec_depend>joint_state_publisher_gui</exec_depend>");
                file.WriteLine("  <exec_depend>robot_state_publisher</exec_depend>");
                file.WriteLine("  <exec_depend>rviz2</exec_depend>");
                file.WriteLine("  <exec_depend>xacro</exec_depend>");
                file.WriteLine("  <export>");
                file.WriteLine("    <build_type>ament_python</build_type>");
                file.WriteLine("  </export>");
                file.WriteLine("</package>");
            }
        }

        private void CreateRos2SetupPy()
        {
            string ros2Name = Ros2PackageName;
            string path = WindowsRos2PackageDirectory + "setup.py";
            logger.Info("Creating ROS 2 setup.py at " + path);
            using (StreamWriter file = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                file.WriteLine("from glob import glob");
                file.WriteLine("import os");
                file.WriteLine("from setuptools import setup");
                file.WriteLine("");
                file.WriteLine("package_name = '" + ros2Name + "'");
                file.WriteLine("");
                file.WriteLine("def package_files(directory):");
                file.WriteLine("    data_files = []");
                file.WriteLine("    for path in glob(os.path.join(directory, '**', '*'), recursive=True):");
                file.WriteLine("        if os.path.isfile(path):");
                file.WriteLine("            install_dir = os.path.join('share', package_name, os.path.dirname(path))");
                file.WriteLine("            data_files.append((install_dir, [path]))");
                file.WriteLine("    return data_files");
                file.WriteLine("");
                file.WriteLine("setup(");
                file.WriteLine("    name=package_name,");
                file.WriteLine("    version='1.0.0',");
                file.WriteLine("    packages=[],");
                file.WriteLine("    data_files=[");
                file.WriteLine("        ('share/ament_index/resource_index/packages', ['resource/' + package_name]),");
                file.WriteLine("        ('share/' + package_name, ['package.xml']),");
                file.WriteLine("        ('share/' + package_name + '/launch', glob('launch/*.py')),");
                file.WriteLine("        ('share/' + package_name + '/urdf', glob('urdf/*')),");
                file.WriteLine("    ] + package_files('meshes') + package_files('textures') + package_files('config'),");
                file.WriteLine("    install_requires=['setuptools'],");
                file.WriteLine("    zip_safe=True,");
                file.WriteLine("    maintainer='" + PackageXML.DefaultMaintainerName + "',");
                file.WriteLine("    maintainer_email='" + PackageXML.DefaultMaintainerEmail + "',");
                file.WriteLine("    description='ROS 2 URDF description package for " + PackageName + "',");
                file.WriteLine("    license='BSD',");
                file.WriteLine(")");
            }
        }

        private void CreateRos2ResourceMarker()
        {
            logger.Info("Creating ROS 2 resource marker at " + WindowsRos2ResourceDirectory + Ros2PackageName);
            File.WriteAllText(WindowsRos2ResourceDirectory + Ros2PackageName, "");
        }

        private void CreateRos2DisplayLaunch()
        {
            string ros2Name = Ros2PackageName;
            string path = WindowsRos2LaunchDirectory + "display.launch.py";
            logger.Info("Creating ROS 2 display launch file at " + path);
            using (StreamWriter file = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                file.WriteLine("from launch import LaunchDescription");
                file.WriteLine("from launch_ros.actions import Node");
                file.WriteLine("from ament_index_python.packages import get_package_share_directory");
                file.WriteLine("import os");
                file.WriteLine("");
                file.WriteLine("def generate_launch_description():");
                file.WriteLine("    package_share = get_package_share_directory('" + ros2Name + "')");
                file.WriteLine("    urdf_file = os.path.join(package_share, 'urdf', '" + RobotName + ".urdf')");
                file.WriteLine("    with open(urdf_file, 'r', encoding='utf-8') as f:");
                file.WriteLine("        robot_description = f.read()");
                file.WriteLine("    return LaunchDescription([");
                file.WriteLine("        Node(package='robot_state_publisher', executable='robot_state_publisher',");
                file.WriteLine("             parameters=[{'robot_description': robot_description}]),");
                file.WriteLine("        Node(package='joint_state_publisher_gui', executable='joint_state_publisher_gui'),");
                file.WriteLine("        Node(package='rviz2', executable='rviz2', output='screen'),");
                file.WriteLine("    ])");
            }
        }

        private void CreateRos2GazeboLaunch()
        {
            string ros2Name = Ros2PackageName;
            string path = WindowsRos2LaunchDirectory + "gazebo.launch.py";
            logger.Info("Creating ROS 2 gazebo launch file at " + path);
            using (StreamWriter file = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                file.WriteLine("from launch import LaunchDescription");
                file.WriteLine("from launch.actions import ExecuteProcess");
                file.WriteLine("from ament_index_python.packages import get_package_share_directory");
                file.WriteLine("import os");
                file.WriteLine("");
                file.WriteLine("def generate_launch_description():");
                file.WriteLine("    package_share = get_package_share_directory('" + ros2Name + "')");
                file.WriteLine("    urdf_file = os.path.join(package_share, 'urdf', '" + RobotName + ".urdf')");
                file.WriteLine("    return LaunchDescription([");
                file.WriteLine("        ExecuteProcess(cmd=['ros2', 'run', 'gazebo_ros', 'spawn_entity.py',");
                file.WriteLine("                            '-entity', '" + RobotName + "', '-file', urdf_file],");
                file.WriteLine("                       output='screen'),");
                file.WriteLine("    ])");
            }
        }

        private static int CopyDirectory(string source, string destination)
        {
            if (!Directory.Exists(source))
            {
                logger.Warn("Directory copy skipped because source does not exist: " + source);
                return 0;
            }

            int copiedFiles = 0;
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                CopyFileWithRetry(file, Path.Combine(destination, Path.GetFileName(file)));
                copiedFiles += 1;
            }
            foreach (string directory in Directory.GetDirectories(source))
            {
                copiedFiles += CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }

            return copiedFiles;
        }

        private static void CopyFileWithRetry(string source, string destination)
        {
            const int timeoutMilliseconds = 15000;
            const int sleepMilliseconds = 250;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            Exception lastException = null;

            while (DateTime.UtcNow <= deadline)
            {
                try
                {
                    File.Copy(source, destination, true);
                    return;
                }
                catch (IOException e)
                {
                    lastException = e;
                }
                catch (UnauthorizedAccessException e)
                {
                    lastException = e;
                }

                Thread.Sleep(sleepMilliseconds);
            }

            throw new IOException("Timed out copying file: " + source, lastException);
        }

        public static string SanitizePackageName(string name)
        {
            string packageName = string.IsNullOrWhiteSpace(name) ? "robot" : Path.GetFileName(name);
            string extension = Path.GetExtension(packageName);
            if (String.Equals(extension, ".sldasm", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(extension, ".sldprt", StringComparison.OrdinalIgnoreCase))
            {
                packageName = Path.GetFileNameWithoutExtension(packageName);
            }

            string sanitized = Regex.Replace(packageName.ToLowerInvariant(), "[^a-z0-9_]", "_");
            sanitized = Regex.Replace(sanitized, "_+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(sanitized) || !Regex.IsMatch(sanitized.Substring(0, 1), "[a-z]"))
            {
                sanitized = "robot_" + sanitized;
            }
            return sanitized;
        }
    }
}
