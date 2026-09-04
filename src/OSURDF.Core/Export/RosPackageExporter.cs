using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OSURDF.Core.Bundle;
using OSURDF.Core.Model;
using OSURDF.Core.Serialization;
using OSURDF.Core.Urdf;
using OSURDF.Core.Validation;

namespace OSURDF.Core.Export
{
    public sealed class RosExportOptions
    {
        public string BundleDirectory { get; set; }
        public string OutputDirectory { get; set; }
        public bool Overwrite { get; set; }
    }

    public sealed class RosPackageExporter
    {
        private static readonly Regex PackageName = new Regex(
            "^[a-z][a-z0-9_]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public string ExportRos2(RosExportOptions options)
        {
            return Export(options, true);
        }

        public string ExportRos1(RosExportOptions options)
        {
            return Export(options, false);
        }

        public static void RefreshChecksums(string packageDirectory)
        {
            if (string.IsNullOrWhiteSpace(packageDirectory))
            {
                throw new ArgumentException("A ROS package directory is required.", nameof(packageDirectory));
            }
            string root = Path.GetFullPath(packageDirectory);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("ROS package directory does not exist: " + root);
            }
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("ROS package directory must not be a symbolic link or reparse point: " + root);
            }
            if (!File.Exists(Path.Combine(root, "package.xml")))
            {
                throw new InvalidDataException("ROS package metadata is missing: " + root);
            }
            RobotBundleBuilder.WriteChecksums(root);
        }

        private static string Export(RosExportOptions options, bool ros2)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.BundleDirectory) ||
                string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                throw new ArgumentException("Bundle and output directories are required.", nameof(options));
            }
            BundleVerificationResult verification = new RobotBundleVerifier().Verify(options.BundleDirectory);
            if (!verification.IsValid)
            {
                throw new InvalidDataException("Robot Bundle verification failed: " + string.Join("; ", verification.Errors));
            }
            string bundleRoot = Path.GetFullPath(options.BundleDirectory);
            RobotDocument robot = RobotJson.Read(Path.Combine(bundleRoot, RobotBundleLayout.RobotJsonFile));
            if (ros2 && robot.Profiles?.Ros2?.Enabled != true)
            {
                throw new InvalidOperationException("The Robot Bundle does not enable the ROS 2 profile.");
            }
            if (!ros2 && robot.Profiles?.Ros1?.Enabled != true)
            {
                throw new InvalidOperationException("The Robot Bundle does not enable the ROS 1 legacy profile.");
            }
            ValidationReport report = new RobotValidator().Validate(robot);
            if (!report.IsValid)
            {
                throw new InvalidDataException("Robot validation failed: " + string.Join("; ", report.Findings.Where(item => item.Severity == ValidationSeverity.Error)));
            }

            PackageMetadataProfile package = robot.Profiles.Package;
            ValidatePackageMetadata(package);
            string destinationParent = Path.GetFullPath(options.OutputDirectory);
            Directory.CreateDirectory(destinationParent);
            string destination = Path.GetFullPath(Path.Combine(destinationParent, package.PackageName));
            if (PathsOverlap(destination, bundleRoot))
            {
                throw new InvalidDataException(
                    "ROS output and source Robot Bundle directories must not contain one another.");
            }
            if (Directory.Exists(destination) &&
                (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("ROS package output must not be a symbolic link or reparse point: " + destination);
            }
            if (Directory.Exists(destination))
            {
                RobotBundleBuilder.EnsureNoReparsePoints(destination, "ROS package output");
            }
            if (Directory.Exists(destination) && !options.Overwrite)
            {
                throw new IOException("ROS package output exists. Pass overwrite explicitly: " + destination);
            }

            string staging = Path.Combine(destinationParent, ".osurdf-ros-" + Guid.NewGuid().ToString("N"));
            string previous = null;
            Directory.CreateDirectory(staging);
            try
            {
                RobotDocument packageRobot = RobotJson.Clone(robot);
                CopyAndRewriteAssets(packageRobot, bundleRoot, staging, package.PackageName);
                string robotUrdfFile = GetRobotUrdfFileName(packageRobot.Name);
                string urdfPath = Path.Combine(staging, "urdf", robotUrdfFile);
                UrdfCodec.Write(urdfPath, packageRobot, false);
                CopySourceReports(bundleRoot, staging);

                if (ros2)
                {
                    WriteRos2Files(staging, packageRobot, package, urdfPath, robotUrdfFile);
                }
                else
                {
                    WriteRos1Files(staging, packageRobot, package, robotUrdfFile);
                }
                RobotBundleBuilder.WriteChecksums(staging);

                if (Directory.Exists(destination))
                {
                    previous = destination + ".previous-" + Guid.NewGuid().ToString("N");
                    Directory.Move(destination, previous);
                }
                Directory.Move(staging, destination);
                staging = null;
                if (previous != null)
                {
                    try
                    {
                        Directory.Delete(previous, true);
                        previous = null;
                    }
                    catch (Exception exception) when (
                        exception is IOException || exception is UnauthorizedAccessException)
                    {
                        throw new IOException(
                            "The new ROS package was published, but the previous package was retained for recovery at " + previous + ".",
                            exception);
                    }
                }
                return destination;
            }
            catch
            {
                if (previous != null && !Directory.Exists(destination) && Directory.Exists(previous))
                {
                    Directory.Move(previous, destination);
                    previous = null;
                }
                throw;
            }
            finally
            {
                if (staging != null && Directory.Exists(staging))
                {
                    try
                    {
                        Directory.Delete(staging, true);
                    }
                    catch (Exception exception) when (
                        exception is IOException || exception is UnauthorizedAccessException)
                    {
                        // Preserve the primary export failure. The uniquely named staging directory
                        // is safe to inspect or remove on the next maintenance pass.
                    }
                }
            }
        }

        private static void ValidatePackageMetadata(PackageMetadataProfile package)
        {
            if (package == null) throw new InvalidDataException("Package metadata is missing.");
            if (!PackageName.IsMatch(package.PackageName ?? string.Empty))
            {
                throw new InvalidDataException("ROS package name must match ^[a-z][a-z0-9_]*$: " + package.PackageName);
            }
            if (string.IsNullOrWhiteSpace(package.Version) ||
                string.IsNullOrWhiteSpace(package.Description) ||
                string.IsNullOrWhiteSpace(package.MaintainerName) ||
                string.IsNullOrWhiteSpace(package.MaintainerEmail) ||
                string.IsNullOrWhiteSpace(package.License))
            {
                throw new InvalidDataException("ROS package version, description, maintainer and model license must be explicit.");
            }
        }

        private static void CopyAndRewriteAssets(
            RobotDocument robot,
            string bundleRoot,
            string packageRoot,
            string packageName)
        {
            HashSet<string> copied = new HashSet<string>(StringComparer.Ordinal);
            foreach (LinkDocument link in robot.Links)
            {
                foreach (VisualDocument visual in link.Visuals ?? Enumerable.Empty<VisualDocument>())
                {
                    RewriteAsset(visual.Geometry, bundleRoot, packageRoot, packageName, copied);
                    if (visual.Material != null && !string.IsNullOrWhiteSpace(visual.Material.TextureUri))
                    {
                        visual.Material.TextureUri = CopyAsset(visual.Material.TextureUri, bundleRoot, packageRoot, packageName, copied);
                    }
                }
                foreach (CollisionDocument collision in link.Collisions ?? Enumerable.Empty<CollisionDocument>())
                {
                    RewriteAsset(collision.Geometry, bundleRoot, packageRoot, packageName, copied);
                }
            }
        }

        private static void RewriteAsset(
            GeometryDocument geometry,
            string bundleRoot,
            string packageRoot,
            string packageName,
            ISet<string> copied)
        {
            if (geometry != null && string.Equals(geometry.Type, "mesh", StringComparison.Ordinal))
            {
                geometry.Uri = CopyAsset(geometry.Uri, bundleRoot, packageRoot, packageName, copied);
            }
        }

        private static string CopyAsset(
            string relative,
            string bundleRoot,
            string packageRoot,
            string packageName,
            ISet<string> copied)
        {
            string normalized = (relative ?? string.Empty).Replace('\\', '/');
            string source = RobotBundleBuilder.SafeBundlePath(bundleRoot, normalized);
            if (!File.Exists(source)) throw new FileNotFoundException("Bundle asset is missing.", normalized);
            string destination = RobotBundleBuilder.SafeBundlePath(packageRoot, normalized);
            if (copied.Add(normalized))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(source, destination, false);
            }
            return "package://" + packageName + "/" + normalized;
        }

        private static void WriteRos2Files(
            string root,
            RobotDocument robot,
            PackageMetadataProfile package,
            string urdfPath,
            string baseRobotFile)
        {
            Ros2ControlProfile control = robot.Profiles.Ros2.Ros2Control;
            bool controlEnabled = control != null && control.Enabled;
            string robotFile = baseRobotFile;
            if (controlEnabled)
            {
                robotFile = Path.GetFileNameWithoutExtension(baseRobotFile) + ".ros2_control.urdf";
                WriteRos2ControlUrdf(urdfPath, Path.Combine(root, "urdf", robotFile), control, package.PackageName);
                WriteControllerYaml(Path.Combine(root, "config", "controllers.yaml"), control);
            }

            WriteXml(Path.Combine(root, "package.xml"), Ros2PackageXml(package, control));
            RobotBundleBuilder.WriteUtf8(Path.Combine(root, "CMakeLists.txt"), Ros2CMake(package.PackageName));
            RobotBundleBuilder.WriteUtf8(Path.Combine(root, "launch", "display.launch.py"), DisplayLaunch(package.PackageName, robotFile));
            RobotBundleBuilder.WriteUtf8(Path.Combine(root, "launch", "gazebo.launch.py"), GazeboLaunch(package.PackageName, robot.Name, robotFile, control));
            RobotBundleBuilder.WriteUtf8(Path.Combine(root, "README.md"), RosReadme(robot, true));
        }

        private static void WriteRos1Files(
            string root,
            RobotDocument robot,
            PackageMetadataProfile package,
            string robotFile)
        {
            WriteXml(Path.Combine(root, "package.xml"), Ros1PackageXml(package));
            RobotBundleBuilder.WriteUtf8(Path.Combine(root, "CMakeLists.txt"), Ros1CMake(package.PackageName));
            RobotBundleBuilder.WriteUtf8(Path.Combine(root, "launch", "display.launch"), Ros1DisplayLaunch(package.PackageName, robotFile));
            RobotBundleBuilder.WriteUtf8(Path.Combine(root, "launch", "gazebo.launch"), Ros1GazeboLaunch(package.PackageName, robot.Name, robotFile));
            RobotBundleBuilder.WriteUtf8(Path.Combine(root, "README.md"), RosReadme(robot, false));
        }

        private static XDocument Ros2PackageXml(PackageMetadataProfile package, Ros2ControlProfile control)
        {
            bool controlEnabled = control != null && control.Enabled;
            XElement root = new XElement("package",
                new XAttribute("format", "3"),
                new XElement("name", package.PackageName),
                new XElement("version", package.Version),
                new XElement("description", package.Description),
                new XElement("maintainer", new XAttribute("email", package.MaintainerEmail), package.MaintainerName),
                new XElement("license", package.License),
                new XElement("buildtool_depend", "ament_cmake"),
                new XElement("exec_depend", "ament_index_python"),
                new XElement("exec_depend", "joint_state_publisher_gui"),
                new XElement("exec_depend", "launch"),
                new XElement("exec_depend", "launch_ros"),
                new XElement("exec_depend", "robot_state_publisher"),
                new XElement("exec_depend", "ros_gz_sim"),
                new XElement("exec_depend", "rviz2"),
                new XElement("exec_depend", "xacro"));
            if (controlEnabled)
            {
                root.Add(new XElement("exec_depend", "controller_manager"));
                root.Add(new XElement("exec_depend", "ros2_control"));
                root.Add(new XElement("exec_depend", "ros2_controllers"));
            }
            if (controlEnabled && control.GazeboPluginEnabled)
            {
                root.Add(new XElement("exec_depend", "gz_ros2_control"));
            }
            root.Add(new XElement("export", new XElement("build_type", "ament_cmake")));
            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        private static XDocument Ros1PackageXml(PackageMetadataProfile package)
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("package",
                    new XAttribute("format", "2"),
                    new XElement("name", package.PackageName),
                    new XElement("version", package.Version),
                    new XElement("description", package.Description),
                    new XElement("maintainer", new XAttribute("email", package.MaintainerEmail), package.MaintainerName),
                    new XElement("license", package.License),
                    new XElement("buildtool_depend", "catkin"),
                    new XElement("exec_depend", "gazebo_ros"),
                    new XElement("exec_depend", "joint_state_publisher_gui"),
                    new XElement("exec_depend", "robot_state_publisher"),
                    new XElement("exec_depend", "rviz")));
        }

        private static string Ros2CMake(string packageName)
        {
            return "cmake_minimum_required(VERSION 3.16)\n" +
                "project(" + packageName + ")\n\n" +
                "find_package(ament_cmake REQUIRED)\n\n" +
                "install(DIRECTORY config launch meshes textures urdf\n" +
                "  DESTINATION share/${PROJECT_NAME}\n" +
                "  OPTIONAL\n" +
                ")\n\n" +
                "ament_package()\n";
        }

        private static string Ros1CMake(string packageName)
        {
            return "cmake_minimum_required(VERSION 3.0.2)\n" +
                "project(" + packageName + ")\n\n" +
                "find_package(catkin REQUIRED)\n" +
                "catkin_package()\n\n" +
                "install(DIRECTORY config launch meshes textures urdf\n" +
                "  DESTINATION ${CATKIN_PACKAGE_SHARE_DESTINATION}\n" +
                "  OPTIONAL\n" +
                ")\n";
        }

        private static string DisplayLaunch(string packageName, string robotFile)
        {
            return "from pathlib import Path\n\n" +
                "from ament_index_python.packages import get_package_share_directory\n" +
                "from launch import LaunchDescription\n" +
                "from launch_ros.actions import Node\n" +
                "from xacro import process_file\n\n\n" +
                "def generate_launch_description():\n" +
                "    urdf = Path(get_package_share_directory('" + PythonQuote(packageName) + "')) / 'urdf' / '" + PythonQuote(robotFile) + "'\n" +
                "    description = process_file(str(urdf)).toxml()\n" +
                "    return LaunchDescription([\n" +
                "        Node(package='robot_state_publisher', executable='robot_state_publisher', parameters=[{'robot_description': description}]),\n" +
                "        Node(package='joint_state_publisher_gui', executable='joint_state_publisher_gui'),\n" +
                "        Node(package='rviz2', executable='rviz2'),\n" +
                "    ])\n";
        }

        private static string GazeboLaunch(string packageName, string robotName, string robotFile, Ros2ControlProfile control)
        {
            bool controlEnabled = control != null && control.Enabled && control.GazeboPluginEnabled;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("from pathlib import Path");
            builder.AppendLine();
            builder.AppendLine("from ament_index_python.packages import get_package_share_directory");
            builder.AppendLine("from launch import LaunchDescription");
            builder.AppendLine("from launch.actions import DeclareLaunchArgument, IncludeLaunchDescription");
            builder.AppendLine("from launch.substitutions import LaunchConfiguration");
            builder.AppendLine("from launch.launch_description_sources import PythonLaunchDescriptionSource");
            builder.AppendLine("from launch_ros.actions import Node");
            builder.AppendLine("from xacro import process_file");
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("def generate_launch_description():");
            builder.AppendLine("    package_share = Path(get_package_share_directory('" + PythonQuote(packageName) + "'))");
            builder.AppendLine("    urdf = package_share / 'urdf' / '" + PythonQuote(robotFile) + "'");
            builder.AppendLine("    description = process_file(str(urdf)).toxml()");
            builder.AppendLine("    gz_launch = Path(get_package_share_directory('ros_gz_sim')) / 'launch' / 'gz_sim.launch.py'");
            builder.AppendLine("    actions = [");
            builder.AppendLine("        DeclareLaunchArgument('gz_args', default_value='-r empty.sdf'),");
            builder.AppendLine("        IncludeLaunchDescription(PythonLaunchDescriptionSource(str(gz_launch)), launch_arguments={'gz_args': LaunchConfiguration('gz_args')}.items()),");
            builder.AppendLine("        Node(package='robot_state_publisher', executable='robot_state_publisher', parameters=[{'robot_description': description, 'use_sim_time': True}]),");
            builder.AppendLine("        Node(package='ros_gz_sim', executable='create', arguments=['-name', '" + PythonQuote(robotName) + "', '-topic', 'robot_description'], output='screen'),");
            if (controlEnabled)
            {
                builder.AppendLine("        Node(package='controller_manager', executable='spawner', arguments=['joint_state_broadcaster', '--controller-manager', '/controller_manager', '--controller-manager-timeout', '60']),");
                foreach (Ros2ControllerProfile controller in control.Controllers)
                {
                    builder.AppendLine("        Node(package='controller_manager', executable='spawner', arguments=['" + PythonQuote(controller.Name) + "', '--controller-manager', '/controller_manager', '--controller-manager-timeout', '60']),");
                }
            }
            builder.AppendLine("    ]");
            builder.AppendLine("    return LaunchDescription(actions)");
            return builder.ToString();
        }

        private static string Ros1DisplayLaunch(string packageName, string robotFile)
        {
            return "<launch>\n" +
                "  <param name=\"robot_description\" textfile=\"$(find " + packageName + ")/urdf/" + XmlAttribute(robotFile) + "\"/>\n" +
                "  <node name=\"joint_state_publisher_gui\" pkg=\"joint_state_publisher_gui\" type=\"joint_state_publisher_gui\"/>\n" +
                "  <node name=\"robot_state_publisher\" pkg=\"robot_state_publisher\" type=\"robot_state_publisher\"/>\n" +
                "  <node name=\"rviz\" pkg=\"rviz\" type=\"rviz\"/>\n" +
                "</launch>\n";
        }

        private static string Ros1GazeboLaunch(string packageName, string robotName, string robotFile)
        {
            return "<launch>\n" +
                "  <include file=\"$(find gazebo_ros)/launch/empty_world.launch\"/>\n" +
                "  <param name=\"robot_description\" textfile=\"$(find " + packageName + ")/urdf/" + XmlAttribute(robotFile) + "\"/>\n" +
                "  <node name=\"spawn_urdf\" pkg=\"gazebo_ros\" type=\"spawn_model\" args=\"-urdf -param robot_description -model " + XmlAttribute(robotName) + "\"/>\n" +
                "</launch>\n";
        }

        private static void WriteRos2ControlUrdf(
            string sourcePath,
            string destinationPath,
            Ros2ControlProfile profile,
            string packageName)
        {
            XDocument document = XDocument.Load(sourcePath, LoadOptions.PreserveWhitespace);
            XElement root = document.Root ?? throw new InvalidDataException("URDF has no root element.");
            XElement control = new XElement("ros2_control",
                new XAttribute("name", profile.Name),
                new XAttribute("type", profile.Type),
                new XElement("hardware", new XElement("plugin", profile.Plugin)));
            foreach (Ros2ControlJointProfile joint in profile.Joints)
            {
                XElement item = new XElement("joint", new XAttribute("name", joint.Joint));
                foreach (string command in joint.CommandInterfaces)
                {
                    item.Add(new XElement("command_interface", new XAttribute("name", command)));
                }
                foreach (string state in joint.StateInterfaces)
                {
                    item.Add(new XElement("state_interface", new XAttribute("name", state)));
                }
                control.Add(item);
            }
            root.Add(control);
            if (profile.GazeboPluginEnabled)
            {
                root.Add(new XElement("gazebo",
                    new XElement("plugin",
                        new XAttribute("filename", profile.GazeboPluginFilename),
                        new XAttribute("name", profile.GazeboPluginClass),
                        new XElement("parameters", "$(find " + packageName + ")/config/controllers.yaml"))));
            }
            WriteXml(destinationPath, document);
        }

        private static void WriteControllerYaml(string path, Ros2ControlProfile control)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("controller_manager:");
            builder.AppendLine("  ros__parameters:");
            builder.AppendLine("    update_rate: " + control.ControllerManagerUpdateRate.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("    joint_state_broadcaster:");
            builder.AppendLine("      type: joint_state_broadcaster/JointStateBroadcaster");
            foreach (Ros2ControllerProfile controller in control.Controllers)
            {
                builder.AppendLine("    " + YamlKey(controller.Name) + ":");
                builder.AppendLine("      type: " + YamlScalar(controller.Type));
            }
            foreach (Ros2ControllerProfile controller in control.Controllers)
            {
                builder.AppendLine();
                builder.AppendLine(YamlKey(controller.Name) + ":");
                builder.AppendLine("  ros__parameters:");
                WriteYamlList(builder, "    joints", controller.Joints);
                if (string.Equals(
                    controller.Type,
                    "forward_command_controller/ForwardCommandController",
                    StringComparison.Ordinal))
                {
                    builder.AppendLine("    interface_name: " + YamlScalar(controller.CommandInterfaces.Single()));
                }
                else
                {
                    WriteYamlList(builder, "    command_interfaces", controller.CommandInterfaces);
                    WriteYamlList(builder, "    state_interfaces", controller.StateInterfaces);
                }
            }
            RobotBundleBuilder.WriteUtf8(path, builder.ToString());
        }

        private static void WriteYamlList(StringBuilder builder, string key, IEnumerable<string> values)
        {
            List<string> items = (values ?? Enumerable.Empty<string>()).ToList();
            if (items.Count == 0)
            {
                builder.AppendLine(key + ": []");
                return;
            }
            builder.AppendLine(key + ":");
            foreach (string item in items) builder.AppendLine("      - " + YamlScalar(item));
        }

        private static string RosReadme(RobotDocument robot, bool ros2)
        {
            return "# " + robot.Profiles.Package.PackageName + "\n\n" +
                "Generated from an OSURDF Robot Bundle for " + (ros2 ? "ROS 2" : "ROS 1 legacy") + ".\n\n" +
                "Model license: " + robot.Profiles.Package.License + "\n\n" +
                "The source Bundle checksum manifest is not a substitute for simulator or hardware validation.\n";
        }

        private static void CopySourceReports(string bundleRoot, string packageRoot)
        {
            string source = Path.Combine(bundleRoot, "reports", "cad");
            if (!Directory.Exists(source))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            {
                string destination = Path.Combine(packageRoot, "config", Path.GetFileName(file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, false);
            }
        }

        public static string GetRobotUrdfFileName(string robotName)
        {
            return SafeRobotFileName(robotName) + ".urdf";
        }

        private static string SafeRobotFileName(string value)
        {
            string result = Regex.Replace((value ?? "robot").ToLowerInvariant(), "[^a-z0-9_]", "_");
            result = Regex.Replace(result, "_+", "_").Trim('_');
            return string.IsNullOrWhiteSpace(result) ? "robot" : result;
        }

        private static void WriteXml(string path, XDocument document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                document.Save(writer, SaveOptions.None);
            }
        }

        private static string PythonQuote(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private static string XmlAttribute(string value)
        {
            return (value ?? string.Empty).Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string YamlKey(string value)
        {
            return Regex.IsMatch(value ?? string.Empty, "^[A-Za-z_][A-Za-z0-9_]*$") ? value : YamlScalar(value);
        }

        private static string YamlScalar(string value)
        {
            return "'" + (value ?? string.Empty)
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("'", "''") + "'";
        }

        private static bool PathsOverlap(string first, string second)
        {
            string fullFirst = Path.GetFullPath(first).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string fullSecond = Path.GetFullPath(second).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(fullFirst, fullSecond, comparison))
            {
                return true;
            }
            string firstPrefix = fullFirst + Path.DirectorySeparatorChar;
            string secondPrefix = fullSecond + Path.DirectorySeparatorChar;
            return fullFirst.StartsWith(secondPrefix, comparison) ||
                fullSecond.StartsWith(firstPrefix, comparison);
        }
    }
}
