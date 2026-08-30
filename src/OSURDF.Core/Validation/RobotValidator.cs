using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OSURDF.Core.Model;

namespace OSURDF.Core.Validation
{
    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class ValidationFinding
    {
        public ValidationSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Path { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            return Severity.ToString().ToUpperInvariant() + " " + Code + " " + Path + ": " + Message;
        }
    }

    public sealed class ValidationReport
    {
        public List<ValidationFinding> Findings { get; } = new List<ValidationFinding>();
        public bool IsValid => Findings.All(finding => finding.Severity != ValidationSeverity.Error);
        public int ErrorCount => Findings.Count(finding => finding.Severity == ValidationSeverity.Error);
        public int WarningCount => Findings.Count(finding => finding.Severity == ValidationSeverity.Warning);

        public void Add(ValidationSeverity severity, string code, string path, string message)
        {
            Findings.Add(new ValidationFinding
            {
                Severity = severity,
                Code = code,
                Path = path,
                Message = message
            });
        }
    }

    public sealed class RobotValidator
    {
        private static readonly Regex PackageName = new Regex(
            "^[a-z][a-z0-9_]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex PackageVersion = new Regex(
            "^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex EmailAddress = new Regex(
            "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex IsaacUnsafeName = new Regex(
            "(^[^A-Za-z_])|([^A-Za-z0-9_])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex RosName = new Regex(
            "^[A-Za-z_][A-Za-z0-9_]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> IsaacCollisionTypes = new HashSet<string>(
            new[] { "convex_hull", "convex_decomposition", "bounding_sphere", "bounding_cube" },
            StringComparer.Ordinal);

        public ValidationReport Validate(RobotDocument robot)
        {
            ValidationReport report = new ValidationReport();
            if (robot == null)
            {
                report.Add(ValidationSeverity.Error, "ROBOT_NULL", "$", "Robot document is null.");
                return report;
            }

            ValidateHeader(robot, report);
            ValidateLinks(robot, report);
            ValidateJoints(robot, report);
            if (robot.Links != null && robot.Joints != null && robot.Links.Count > 0)
            {
                ValidateGraph(robot, report);
                ValidateMimicGraph(robot, report);
            }
            ValidateProfiles(robot, report);
            return report;
        }

        private static void ValidateHeader(RobotDocument robot, ValidationReport report)
        {
            if (robot.SchemaVersion != RobotSchema.CurrentVersion)
            {
                report.Add(
                    ValidationSeverity.Error,
                    "SCHEMA_VERSION",
                    "$.schemaVersion",
                    "Expected schema " + RobotSchema.CurrentVersion + " but found " + robot.SchemaVersion + ".");
            }
            if (string.IsNullOrWhiteSpace(robot.Name))
            {
                report.Add(ValidationSeverity.Error, "ROBOT_NAME", "$.name", "Robot name is required.");
            }
            if (!string.Equals(robot.Units, RobotSchema.UnitSystem, StringComparison.Ordinal))
            {
                report.Add(
                    ValidationSeverity.Error,
                    "UNIT_SYSTEM",
                    "$.units",
                    "Robot core data must use SI units.");
            }
            if (robot.Metadata == null ||
                string.IsNullOrWhiteSpace(robot.Metadata.Generator) ||
                string.IsNullOrWhiteSpace(robot.Metadata.GeneratorVersion) ||
                string.IsNullOrWhiteSpace(robot.Metadata.Commit) ||
                string.IsNullOrWhiteSpace(robot.Metadata.SourceFormat))
            {
                report.Add(
                    ValidationSeverity.Error,
                    "METADATA_IDENTITY",
                    "$.metadata",
                    "Generator, generator version, commit, and source format must be explicit.");
            }
        }

        private static void ValidateLinks(RobotDocument robot, ValidationReport report)
        {
            if (robot.Links == null || robot.Links.Count == 0)
            {
                report.Add(ValidationSeverity.Error, "NO_LINKS", "$.links", "At least one Link is required.");
                return;
            }

            AddDuplicateErrors(robot.Links.Where(link => link != null).Select(link => link.Name), "LINK_NAME_DUPLICATE", "$.links", report);
            AddDuplicateErrors(robot.Links.Where(link => link != null).Select(link => link.Id), "LINK_ID_DUPLICATE", "$.links", report);

            for (int index = 0; index < robot.Links.Count; index++)
            {
                LinkDocument link = robot.Links[index];
                string path = "$.links[" + index + "]";
                if (link == null)
                {
                    report.Add(ValidationSeverity.Error, "LINK_NULL", path, "Link entry must not be null.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(link.Id))
                {
                    report.Add(ValidationSeverity.Error, "LINK_ID", path + ".id", "Stable Link ID is required.");
                }
                if (string.IsNullOrWhiteSpace(link.Name))
                {
                    report.Add(ValidationSeverity.Error, "LINK_NAME", path + ".name", "Link name is required.");
                }
                ValidateIsaacName(link.Name, path + ".name", report);
                ValidateInertial(link.Inertial, path + ".inertial", report);
                ValidateGeometryInstances(link.Visuals, path + ".visuals", report);
                ValidateGeometryInstances(link.Collisions, path + ".collisions", report);
                if (link.Source == null || string.IsNullOrWhiteSpace(link.Source.Kind) ||
                    string.Equals(link.Source.Kind, "unknown", StringComparison.Ordinal))
                {
                    report.Add(ValidationSeverity.Error, "LINK_SOURCE", path + ".source", "Explicit Link source provenance is required.");
                }
            }
        }

        private static void ValidateJoints(RobotDocument robot, ValidationReport report)
        {
            if (robot.Joints == null)
            {
                report.Add(ValidationSeverity.Error, "JOINTS_NULL", "$.joints", "Joint list must not be null.");
                return;
            }

            AddDuplicateErrors(robot.Joints.Where(joint => joint != null).Select(joint => joint.Name), "JOINT_NAME_DUPLICATE", "$.joints", report);
            AddDuplicateErrors(robot.Joints.Where(joint => joint != null).Select(joint => joint.Id), "JOINT_ID_DUPLICATE", "$.joints", report);

            HashSet<string> linkNames = new HashSet<string>(
                robot.Links.Where(link => link != null).Select(link => link.Name),
                StringComparer.Ordinal);
            for (int index = 0; index < robot.Joints.Count; index++)
            {
                JointDocument joint = robot.Joints[index];
                string path = "$.joints[" + index + "]";
                if (joint == null)
                {
                    report.Add(ValidationSeverity.Error, "JOINT_NULL", path, "Joint entry must not be null.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(joint.Id))
                {
                    report.Add(ValidationSeverity.Error, "JOINT_ID", path + ".id", "Stable Joint ID is required.");
                }
                if (string.IsNullOrWhiteSpace(joint.Name))
                {
                    report.Add(ValidationSeverity.Error, "JOINT_NAME", path + ".name", "Joint name is required.");
                }
                ValidateIsaacName(joint.Name, path + ".name", report);
                if (!RobotSchema.JointTypes.Contains(joint.Type ?? string.Empty))
                {
                    report.Add(
                        ValidationSeverity.Error,
                        "JOINT_TYPE",
                        path + ".type",
                        "Joint type must be explicitly configured; automatic or blank types are not exportable.");
                }
                if (!linkNames.Contains(joint.Parent ?? string.Empty))
                {
                    report.Add(ValidationSeverity.Error, "JOINT_PARENT", path + ".parent", "Parent Link does not exist.");
                }
                if (!linkNames.Contains(joint.Child ?? string.Empty))
                {
                    report.Add(ValidationSeverity.Error, "JOINT_CHILD", path + ".child", "Child Link does not exist.");
                }
                if (string.Equals(joint.Parent, joint.Child, StringComparison.Ordinal))
                {
                    report.Add(ValidationSeverity.Error, "JOINT_SELF", path, "A Joint cannot connect a Link to itself.");
                }

                ValidatePose(joint.Origin, path + ".origin", report);
                if (RobotSchema.MovingJointTypes.Contains(joint.Type ?? string.Empty))
                {
                    ValidateAxis(joint.Axis, path + ".axis", report);
                }
                ValidateJointLimits(joint, path, report);
                ValidateJointDynamics(joint.Dynamics, path + ".dynamics", report);
                ValidateMimicValues(joint.Mimic, path + ".mimic", report);
                ValidateJointSource(joint, path, report);
            }
        }

        private static void ValidateGraph(RobotDocument robot, ValidationReport report)
        {
            List<LinkDocument> links = robot.Links
                .Where(link => link != null && !string.IsNullOrWhiteSpace(link.Name))
                .ToList();
            if (links.Count != robot.Links.Count ||
                links.Select(link => link.Name).Distinct(StringComparer.Ordinal).Count() != links.Count)
            {
                return;
            }
            Dictionary<string, int> parentCounts = links.ToDictionary(
                link => link.Name,
                link => 0,
                StringComparer.Ordinal);
            Dictionary<string, List<string>> children = links.ToDictionary(
                link => link.Name,
                link => new List<string>(),
                StringComparer.Ordinal);

            foreach (JointDocument joint in robot.Joints)
            {
                if (joint == null)
                {
                    continue;
                }
                if (!parentCounts.ContainsKey(joint.Child ?? string.Empty) ||
                    !children.ContainsKey(joint.Parent ?? string.Empty))
                {
                    continue;
                }
                parentCounts[joint.Child]++;
                children[joint.Parent].Add(joint.Child);
            }

            foreach (KeyValuePair<string, int> pair in parentCounts.Where(pair => pair.Value > 1))
            {
                report.Add(
                    ValidationSeverity.Error,
                    "MULTIPLE_PARENTS",
                    "$.links",
                    "Link '" + pair.Key + "' has " + pair.Value + " parent Joints; URDF requires a tree.");
            }
            List<string> roots = parentCounts.Where(pair => pair.Value == 0).Select(pair => pair.Key).ToList();
            if (roots.Count != 1)
            {
                report.Add(
                    ValidationSeverity.Error,
                    "ROOT_COUNT",
                    "$.links",
                    "Expected exactly one root Link but found " + roots.Count + ".");
                return;
            }

            HashSet<string> visiting = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            if (HasCycle(roots[0], children, visiting, visited))
            {
                report.Add(ValidationSeverity.Error, "JOINT_CYCLE", "$.joints", "Joint graph contains a cycle.");
            }
            if (visited.Count != links.Count)
            {
                IEnumerable<string> disconnected = links.Select(link => link.Name).Where(name => !visited.Contains(name));
                report.Add(
                    ValidationSeverity.Error,
                    "DISCONNECTED_LINKS",
                    "$.links",
                    "Disconnected Links: " + string.Join(", ", disconnected) + ".");
            }
        }

        private static void ValidateMimicGraph(RobotDocument robot, ValidationReport report)
        {
            Dictionary<string, string> edges = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> jointNames = new HashSet<string>(
                robot.Joints.Where(joint => joint != null).Select(joint => joint.Name),
                StringComparer.Ordinal);
            foreach (JointDocument joint in robot.Joints.Where(joint => joint != null && joint.Mimic != null))
            {
                if (!jointNames.Contains(joint.Mimic.Joint ?? string.Empty))
                {
                    report.Add(
                        ValidationSeverity.Error,
                        "MIMIC_TARGET",
                        "$.joints." + joint.Name + ".mimic",
                        "Mimic source Joint does not exist.");
                    continue;
                }
                edges[joint.Name] = joint.Mimic.Joint;
            }

            foreach (string start in edges.Keys)
            {
                HashSet<string> path = new HashSet<string>(StringComparer.Ordinal);
                string current = start;
                while (edges.ContainsKey(current))
                {
                    if (!path.Add(current))
                    {
                        report.Add(
                            ValidationSeverity.Error,
                            "MIMIC_CYCLE",
                            "$.joints." + start + ".mimic",
                            "Mimic relationship contains a cycle.");
                        break;
                    }
                    current = edges[current];
                }
            }
        }

        private static void ValidateProfiles(RobotDocument robot, ValidationReport report)
        {
            if (robot.Profiles == null)
            {
                report.Add(ValidationSeverity.Error, "PROFILES_NULL", "$.profiles", "Profiles are required.");
                return;
            }

            PackageMetadataProfile package = robot.Profiles.Package;
            bool rosEnabled = (robot.Profiles.Ros1 != null && robot.Profiles.Ros1.Enabled) ||
                (robot.Profiles.Ros2 != null && robot.Profiles.Ros2.Enabled);
            if (rosEnabled)
            {
                if (package == null || string.IsNullOrWhiteSpace(package.PackageName))
                {
                    report.Add(ValidationSeverity.Error, "PACKAGE_NAME", "$.profiles.package.packageName", "ROS package name is required.");
                }
                else if (!PackageName.IsMatch(package.PackageName))
                {
                    report.Add(ValidationSeverity.Error, "PACKAGE_NAME_FORMAT", "$.profiles.package.packageName", "ROS package name must match ^[a-z][a-z0-9_]*$.");
                }
                if (package == null || !PackageVersion.IsMatch(package.Version ?? string.Empty))
                {
                    report.Add(ValidationSeverity.Error, "PACKAGE_VERSION", "$.profiles.package.version", "Package version must be an exact semantic version.");
                }
                if (package == null || string.IsNullOrWhiteSpace(package.Description))
                {
                    report.Add(ValidationSeverity.Error, "PACKAGE_DESCRIPTION", "$.profiles.package.description", "Package description must be explicit.");
                }
                if (package == null || string.IsNullOrWhiteSpace(package.MaintainerName) ||
                    string.IsNullOrWhiteSpace(package.MaintainerEmail))
                {
                    report.Add(ValidationSeverity.Error, "PACKAGE_MAINTAINER", "$.profiles.package", "Maintainer name and email must be explicit.");
                }
                else if (!EmailAddress.IsMatch(package.MaintainerEmail))
                {
                    report.Add(ValidationSeverity.Error, "PACKAGE_EMAIL", "$.profiles.package.maintainerEmail", "Maintainer email is invalid.");
                }
                if (package == null || string.IsNullOrWhiteSpace(package.License))
                {
                    report.Add(ValidationSeverity.Error, "PACKAGE_LICENSE", "$.profiles.package.license", "The exported model license must be explicit.");
                }
                if (!RosName.IsMatch(robot.Name ?? string.Empty))
                {
                    report.Add(
                        ValidationSeverity.Error,
                        "ROS_ROBOT_NAME",
                        "$.name",
                        "ROS package generation requires a portable robot/entity name using letters, digits and underscores.");
                }
            }

            bool isaacEnabled = robot.Profiles.Isaac != null && robot.Profiles.Isaac.Enabled;
            bool isaacLabEnabled = robot.Profiles.IsaacLab != null && robot.Profiles.IsaacLab.Enabled;
            if ((isaacEnabled || isaacLabEnabled) &&
                (robot.Metadata == null || string.IsNullOrWhiteSpace(robot.Metadata.ModelLicense)))
            {
                report.Add(
                    ValidationSeverity.Error,
                    "MODEL_LICENSE",
                    "$.metadata.modelLicense",
                    "An explicit model license is required before creating Isaac assets.");
            }

            ValidateRosProfile(robot, robot.Profiles.Ros2, report);
            if (robot.Profiles.Ros1 != null && robot.Profiles.Ros1.Enabled && !robot.Profiles.Ros1.Legacy)
            {
                report.Add(
                    ValidationSeverity.Error,
                    "ROS1_LEGACY_PROFILE",
                    "$.profiles.ros1.legacy",
                    "The built-in ROS 1 target is explicitly a legacy compatibility profile.");
            }
            ValidateIsaacProfile(robot, robot.Profiles.Isaac, report);
            ValidateIsaacLabProfile(robot, report);
        }

        private static void ValidateRosProfile(RobotDocument robot, Ros2ExportProfile profile, ValidationReport report)
        {
            if (profile == null || !profile.Enabled)
            {
                return;
            }
            if (!profile.ModernGazebo)
            {
                report.Add(
                    ValidationSeverity.Error,
                    "MODERN_GAZEBO_REQUIRED",
                    "$.profiles.ros2.modernGazebo",
                    "The ROS 2 target supports modern Gazebo through ros_gz; classic Gazebo is not emitted.");
            }
            bool supported =
                string.Equals(profile.Distribution, "lyrical", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(profile.GazeboDistribution, "jetty", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.Distribution, "jazzy", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(profile.GazeboDistribution, "harmonic", StringComparison.OrdinalIgnoreCase);
            if (!supported)
            {
                report.Add(
                    ValidationSeverity.Error,
                    "ROS_GAZEBO_PAIR",
                    "$.profiles.ros2",
                    "Supported pairs are Lyrical/Jetty and Jazzy/Harmonic.");
            }
            if (profile.Ros2Control != null && profile.Ros2Control.Enabled)
            {
                Ros2ControlProfile control = profile.Ros2Control;
                if (string.IsNullOrWhiteSpace(control.Name) || string.IsNullOrWhiteSpace(control.Type))
                {
                    report.Add(ValidationSeverity.Error, "ROS2_CONTROL_IDENTITY", "$.profiles.ros2.ros2Control", "ros2_control name and type are required.");
                }
                else
                {
                    if (!RosName.IsMatch(control.Name))
                    {
                        report.Add(ValidationSeverity.Error, "ROS2_CONTROL_NAME", "$.profiles.ros2.ros2Control.name", "ros2_control name must be a portable ROS identifier using letters, digits and underscores.");
                    }
                    if (!new[] { "system", "actuator", "sensor" }.Contains(control.Type, StringComparer.Ordinal))
                    {
                        report.Add(ValidationSeverity.Error, "ROS2_CONTROL_TYPE", "$.profiles.ros2.ros2Control.type", "ros2_control type must be system, actuator, or sensor.");
                    }
                }
                if (string.IsNullOrWhiteSpace(control.Plugin))
                {
                    report.Add(ValidationSeverity.Error, "ROS2_CONTROL_PLUGIN", "$.profiles.ros2.ros2Control.plugin", "Hardware plugin is required.");
                }
                List<Ros2ControlJointProfile> controlJoints = control.Joints ?? new List<Ros2ControlJointProfile>();
                List<Ros2ControllerProfile> controllers = control.Controllers ?? new List<Ros2ControllerProfile>();
                if (controlJoints.Count == 0)
                {
                    report.Add(ValidationSeverity.Error, "ROS2_CONTROL_JOINTS", "$.profiles.ros2.ros2Control.joints", "Enabled ros2_control needs at least one explicitly configured moving Joint.");
                }
                AddDuplicateErrors(
                    controlJoints.Where(joint => joint != null).Select(joint => joint.Joint),
                    "ROS2_CONTROL_JOINT_DUPLICATE",
                    "$.profiles.ros2.ros2Control.joints",
                    report);
                HashSet<string> configuredControlJoints = new HashSet<string>(
                    controlJoints.Where(joint => joint != null && !string.IsNullOrWhiteSpace(joint.Joint))
                        .Select(joint => joint.Joint),
                    StringComparer.Ordinal);
                foreach (Ros2ControlJointProfile joint in controlJoints)
                {
                    if (joint == null)
                    {
                        report.Add(ValidationSeverity.Error, "ROS2_CONTROL_JOINT_NULL", "$.profiles.ros2.ros2Control.joints", "ros2_control Joint entry must not be null.");
                        continue;
                    }
                    JointDocument modelJoint = robot.FindJoint(joint.Joint);
                    if (modelJoint == null || !RobotSchema.MovingJointTypes.Contains(modelJoint.Type ?? string.Empty))
                    {
                        report.Add(ValidationSeverity.Error, "ROS2_CONTROL_JOINT", "$.profiles.ros2.ros2Control.joints", "ros2_control refers to unknown or non-moving Joint '" + joint.Joint + "'.");
                    }
                    if (joint.CommandInterfaces == null || joint.StateInterfaces == null ||
                        joint.CommandInterfaces.Count == 0 || joint.StateInterfaces.Count == 0 ||
                        joint.CommandInterfaces.Any(string.IsNullOrWhiteSpace) ||
                        joint.StateInterfaces.Any(string.IsNullOrWhiteSpace))
                    {
                        report.Add(
                            ValidationSeverity.Error,
                            "ROS2_CONTROL_INTERFACES",
                            "$.profiles.ros2.ros2Control.joints",
                            "Each configured ros2_control Joint needs command and state interfaces.");
                    }
                    AddDuplicateErrors(joint.CommandInterfaces ?? new List<string>(), "ROS2_COMMAND_INTERFACE_DUPLICATE", "$.profiles.ros2.ros2Control.joints." + joint.Joint, report);
                    AddDuplicateErrors(joint.StateInterfaces ?? new List<string>(), "ROS2_STATE_INTERFACE_DUPLICATE", "$.profiles.ros2.ros2Control.joints." + joint.Joint, report);
                }
                if (control.ControllerManagerUpdateRate <= 0)
                {
                    report.Add(ValidationSeverity.Error, "ROS2_CONTROL_RATE", "$.profiles.ros2.ros2Control.controllerManagerUpdateRate", "Controller manager update rate must be positive.");
                }
                if (control.GazeboPluginEnabled &&
                    (string.IsNullOrWhiteSpace(control.GazeboPluginFilename) ||
                     string.IsNullOrWhiteSpace(control.GazeboPluginClass)))
                {
                    report.Add(ValidationSeverity.Error, "GZ_ROS2_CONTROL_PLUGIN", "$.profiles.ros2.ros2Control", "Gazebo plugin filename and class must be explicit when simulator control is enabled.");
                }
                AddDuplicateErrors(
                    controllers.Where(controller => controller != null).Select(controller => controller.Name),
                    "ROS2_CONTROLLER_DUPLICATE",
                    "$.profiles.ros2.ros2Control.controllers",
                    report);
                foreach (Ros2ControllerProfile controller in controllers)
                {
                    if (controller == null)
                    {
                        report.Add(ValidationSeverity.Error, "ROS2_CONTROLLER", "$.profiles.ros2.ros2Control.controllers", "Controller entry must not be null.");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(controller.Name) || string.IsNullOrWhiteSpace(controller.Type))
                    {
                        report.Add(ValidationSeverity.Error, "ROS2_CONTROLLER", "$.profiles.ros2.ros2Control.controllers", "Controller name and plugin type must be explicit.");
                    }
                    else if (!RosName.IsMatch(controller.Name) ||
                        string.Equals(controller.Name, "controller_manager", StringComparison.Ordinal) ||
                        string.Equals(controller.Name, "joint_state_broadcaster", StringComparison.Ordinal))
                    {
                        report.Add(ValidationSeverity.Error, "ROS2_CONTROLLER_NAME", "$.profiles.ros2.ros2Control.controllers." + controller.Name, "Controller name must be a non-reserved portable ROS identifier using letters, digits and underscores.");
                    }
                    bool trajectoryController = string.Equals(
                        controller.Type,
                        "joint_trajectory_controller/JointTrajectoryController",
                        StringComparison.Ordinal);
                    bool forwardController = string.Equals(
                        controller.Type,
                        "forward_command_controller/ForwardCommandController",
                        StringComparison.Ordinal);
                    if (!trajectoryController && !forwardController)
                    {
                        report.Add(ValidationSeverity.Error, "ROS2_CONTROLLER_TYPE", "$.profiles.ros2.ros2Control.controllers." + controller.Name, "Built-in generation supports JointTrajectoryController and ForwardCommandController; use an external reviewed package for other controller-specific parameter schemas.");
                    }
                    if (controller.Joints == null || controller.Joints.Count == 0)
                    {
                        report.Add(ValidationSeverity.Error, "ROS2_CONTROLLER_JOINTS", "$.profiles.ros2.ros2Control.controllers." + controller.Name, "Controller must select at least one Joint.");
                    }
                    if (trajectoryController &&
                        ((controller.CommandInterfaces?.Count ?? 0) == 0 ||
                         (controller.StateInterfaces?.Count ?? 0) == 0))
                    {
                        report.Add(ValidationSeverity.Error, "ROS2_TRAJECTORY_INTERFACES", "$.profiles.ros2.ros2Control.controllers." + controller.Name, "JointTrajectoryController requires command and state interface lists.");
                    }
                    if (forwardController && (controller.CommandInterfaces?.Count ?? 0) != 1)
                    {
                        report.Add(ValidationSeverity.Error, "ROS2_FORWARD_INTERFACE", "$.profiles.ros2.ros2Control.controllers." + controller.Name, "ForwardCommandController requires exactly one command interface.");
                    }
                    foreach (string jointName in controller.Joints ?? new List<string>())
                    {
                        if (robot.FindJoint(jointName) == null)
                        {
                            report.Add(ValidationSeverity.Error, "ROS2_CONTROLLER_JOINT", "$.profiles.ros2.ros2Control.controllers." + controller.Name, "Controller refers to unknown Joint '" + jointName + "'.");
                        }
                        else if (!configuredControlJoints.Contains(jointName))
                        {
                            report.Add(ValidationSeverity.Error, "ROS2_CONTROLLER_UNCONFIGURED_JOINT", "$.profiles.ros2.ros2Control.controllers." + controller.Name, "Controller Joint '" + jointName + "' is not configured in ros2_control.");
                        }
                        else
                        {
                            Ros2ControlJointProfile hardwareJoint = controlJoints.FirstOrDefault(
                                item => item != null && string.Equals(item.Joint, jointName, StringComparison.Ordinal));
                            if (hardwareJoint != null)
                            {
                                IEnumerable<string> missingCommands =
                                    (controller.CommandInterfaces ?? new List<string>()).Except(
                                        hardwareJoint.CommandInterfaces ?? new List<string>(),
                                        StringComparer.Ordinal);
                                IEnumerable<string> missingStates =
                                    (controller.StateInterfaces ?? new List<string>()).Except(
                                        hardwareJoint.StateInterfaces ?? new List<string>(),
                                        StringComparer.Ordinal);
                                if (missingCommands.Any() || missingStates.Any())
                                {
                                    report.Add(
                                        ValidationSeverity.Error,
                                        "ROS2_CONTROLLER_INTERFACE_MISMATCH",
                                        "$.profiles.ros2.ros2Control.controllers." + controller.Name,
                                        "Controller interfaces for Joint '" + jointName +
                                        "' must be provided by its ros2_control hardware declaration; missing command=[" +
                                        string.Join(", ", missingCommands) + "], state=[" + string.Join(", ", missingStates) + "].");
                                }
                            }
                        }
                    }
                    AddDuplicateErrors(controller.Joints ?? new List<string>(), "ROS2_CONTROLLER_JOINT_DUPLICATE", "$.profiles.ros2.ros2Control.controllers." + controller.Name, report);
                    ValidateStringList(controller.CommandInterfaces, "ROS2_CONTROLLER_COMMAND_INTERFACES", "$.profiles.ros2.ros2Control.controllers." + controller.Name + ".commandInterfaces", report);
                    ValidateStringList(controller.StateInterfaces, "ROS2_CONTROLLER_STATE_INTERFACES", "$.profiles.ros2.ros2Control.controllers." + controller.Name + ".stateInterfaces", report);
                }
            }
        }

        private static void ValidateIsaacProfile(
            RobotDocument robot,
            IsaacExportProfile profile,
            ValidationReport report)
        {
            if (profile == null || !profile.Enabled)
            {
                return;
            }
            if (profile.SchemaVersion != 1)
            {
                report.Add(ValidationSeverity.Error, "ISAAC_PROFILE_SCHEMA", "$.profiles.isaac.schemaVersion", "Isaac profile schema version must be 1.");
            }
            if (!IsExactVersion(profile.IsaacSimVersion))
            {
                report.Add(
                    ValidationSeverity.Error,
                    "ISAAC_VERSION",
                    "$.profiles.isaac.isaacSimVersion",
                    "Pin an exact tested Isaac Sim version (for example 6.0.0) before conversion.");
            }
            if (!new[] { "Source", "Fixed", "Mobile" }.Contains(profile.BaseType, StringComparer.Ordinal))
            {
                report.Add(ValidationSeverity.Error, "ISAAC_BASE_TYPE", "$.profiles.isaac.baseType", "Base type must be Source, Fixed, or Mobile.");
            }
            if (!IsaacCollisionTypes.Contains(profile.CollisionType ?? string.Empty))
            {
                report.Add(ValidationSeverity.Error, "ISAAC_COLLISION_TYPE", "$.profiles.isaac.collisionType", "Collision type must be convex_hull, convex_decomposition, bounding_sphere, or bounding_cube.");
            }
            string[] robotTypes =
            {
                "Default", "End Effector", "Manipulator", "Humanoid", "Wheeled", "Holonomic",
                "Quadruped", "Mobile Manipulators", "Aerial"
            };
            if (!robotTypes.Contains(profile.RobotType, StringComparer.Ordinal))
            {
                report.Add(ValidationSeverity.Error, "ISAAC_ROBOT_TYPE", "$.profiles.isaac.robotType", "Robot type is not supported by the pinned Isaac URDF importer.");
            }
            foreach (KeyValuePair<string, string> mapping in profile.PackageMappings ?? new Dictionary<string, string>())
            {
                string normalized = mapping.Value ?? string.Empty;
                if (!PackageName.IsMatch(mapping.Key ?? string.Empty) ||
                    !IsPortableRelativePath(normalized))
                {
                    report.Add(ValidationSeverity.Error, "ISAAC_PACKAGE_MAPPING", "$.profiles.isaac.packageMappings", "Isaac package mappings stored in a Bundle must be portable relative paths; local resolution paths are CLI-only inputs.");
                }
            }
            foreach (LinkDocument link in (robot.Links ?? new List<LinkDocument>()).Where(item => item != null))
            {
                if (IsaacUnsafeName.IsMatch(link.Name ?? string.Empty))
                {
                    report.Add(ValidationSeverity.Error, "ISAAC_LINK_NAME", "$.links." + link.Name + ".name", "Isaac exports require USD-safe Link names using letters, digits and underscores.");
                }
            }
            foreach (JointDocument joint in (robot.Joints ?? new List<JointDocument>()).Where(item => item != null))
            {
                if (IsaacUnsafeName.IsMatch(joint.Name ?? string.Empty))
                {
                    report.Add(ValidationSeverity.Error, "ISAAC_JOINT_NAME", "$.joints." + joint.Name + ".name", "Isaac exports require USD-safe Joint names using letters, digits and underscores.");
                }
            }
        }

        private static void ValidateIsaacLabProfile(RobotDocument robot, ValidationReport report)
        {
            IsaacLabProfile profile = robot.Profiles.IsaacLab;
            if (profile == null || !profile.Enabled)
            {
                return;
            }
            if (robot.Profiles.Isaac == null || !robot.Profiles.Isaac.Enabled)
            {
                report.Add(ValidationSeverity.Error, "ISAACLAB_REQUIRES_ISAAC", "$.profiles.isaacLab.enabled", "Isaac Lab output requires an enabled Isaac Sim profile.");
            }
            if (profile.SchemaVersion != 1)
            {
                report.Add(ValidationSeverity.Error, "ISAACLAB_PROFILE_SCHEMA", "$.profiles.isaacLab.schemaVersion", "Isaac Lab profile schema version must be 1.");
            }
            if (!IsExactVersion(profile.IsaacLabVersion))
            {
                report.Add(ValidationSeverity.Error, "ISAACLAB_VERSION", "$.profiles.isaacLab.isaacLabVersion", "Pin an exact tested Isaac Lab version (for example 2.3.2).");
            }
            if (!string.Equals(profile.Backend, "physx", StringComparison.Ordinal))
            {
                report.Add(ValidationSeverity.Error, "ISAACLAB_BACKEND", "$.profiles.isaacLab.backend", "This adapter currently emits a PhysX articulation profile; backend must be 'physx'.");
            }
            List<string> unsupportedMultiDof = (robot.Joints ?? new List<JointDocument>())
                .Where(joint => joint != null &&
                    (string.Equals(joint.Type, "planar", StringComparison.Ordinal) ||
                     string.Equals(joint.Type, "floating", StringComparison.Ordinal)))
                .Select(joint => joint.Name)
                .ToList();
            if (unsupportedMultiDof.Count > 0)
            {
                report.Add(
                    ValidationSeverity.Error,
                    "ISAACLAB_MULTI_DOF_JOINT",
                    "$.joints",
                    "Isaac Lab actuator generation supports one-DOF revolute, continuous, and prismatic Joints; add a project adapter for multi-DOF Joints: " +
                    string.Join(", ", unsupportedMultiDof) + ".");
            }
            if (string.IsNullOrWhiteSpace(profile.PrimPath))
            {
                report.Add(ValidationSeverity.Error, "ISAACLAB_PRIM_PATH", "$.profiles.isaacLab.primPath", "Isaac Lab prim path is required.");
            }
            ValidateVector(profile.RootPosition, "$.profiles.isaacLab.rootPosition", report);
            ValidateQuaternion(profile.RootRotationWxyz, "$.profiles.isaacLab.rootRotationWxyz", report);
            if (profile.SmokeEnvironmentCount <= 0 || profile.SmokeStepCount <= 0)
            {
                report.Add(ValidationSeverity.Error, "ISAACLAB_SMOKE_BOUNDS", "$.profiles.isaacLab", "Smoke environment and step counts must be positive.");
            }
            ValidateIsaacPhysics(profile.Physics, report);
            HashSet<string> movable = new HashSet<string>(
                (robot.Joints ?? new List<JointDocument>())
                    .Where(joint => joint != null &&
                        new[] { "continuous", "revolute", "prismatic" }.Contains(
                            joint.Type ?? string.Empty,
                            StringComparer.Ordinal))
                    .Select(joint => joint.Name),
                StringComparer.Ordinal);
            Dictionary<string, int> coverage = movable.ToDictionary(name => name, name => 0, StringComparer.Ordinal);
            AddDuplicateErrors(
                (profile.ActuatorGroups ?? new List<ActuatorGroupProfile>()).Where(group => group != null).Select(group => group.Name),
                "ACTUATOR_GROUP_DUPLICATE",
                "$.profiles.isaacLab.actuatorGroups",
                report);
            foreach (ActuatorGroupProfile group in profile.ActuatorGroups ?? new List<ActuatorGroupProfile>())
            {
                if (group == null)
                {
                    report.Add(ValidationSeverity.Error, "ACTUATOR_GROUP_NULL", "$.profiles.isaacLab.actuatorGroups", "Actuator group entry must not be null.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(group.Name))
                {
                    report.Add(ValidationSeverity.Error, "ACTUATOR_GROUP_NAME", "$.profiles.isaacLab.actuatorGroups", "Actuator group name is required.");
                }
                if (!new[] { "position", "velocity", "effort", "passive" }.Contains(group.ControlMode, StringComparer.Ordinal))
                {
                    report.Add(ValidationSeverity.Error, "ACTUATOR_MODE", "$.profiles.isaacLab.actuatorGroups." + group.Name, "Control mode must be position, velocity, effort, or passive.");
                }
                if (group.Joints == null || group.Joints.Count == 0)
                {
                    report.Add(ValidationSeverity.Error, "ACTUATOR_GROUP_JOINTS", "$.profiles.isaacLab.actuatorGroups." + group.Name, "Each actuator group must select at least one Joint.");
                }
                AddDuplicateErrors(group.Joints ?? new List<string>(), "ACTUATOR_GROUP_JOINT_DUPLICATE", "$.profiles.isaacLab.actuatorGroups." + group.Name, report);
                foreach (string jointName in group.Joints ?? new List<string>())
                {
                    if (!coverage.ContainsKey(jointName))
                    {
                        report.Add(ValidationSeverity.Error, "ACTUATOR_JOINT", "$.profiles.isaacLab.actuatorGroups." + group.Name, "Joint '" + jointName + "' is not a movable Joint.");
                    }
                    else
                    {
                        coverage[jointName]++;
                    }
                }
                if (group.ControlMode == "position" && (!group.Stiffness.HasValue || !group.Damping.HasValue))
                {
                    report.Add(ValidationSeverity.Error, "ACTUATOR_GAINS", "$.profiles.isaacLab.actuatorGroups." + group.Name, "Position actuator groups require explicit stiffness and damping.");
                }
                if (group.ControlMode == "velocity" && !group.Damping.HasValue)
                {
                    report.Add(ValidationSeverity.Error, "ACTUATOR_DAMPING", "$.profiles.isaacLab.actuatorGroups." + group.Name, "Velocity actuator groups require explicit damping.");
                }
                ValidateOptionalNonNegative(group.Stiffness, "ACTUATOR_STIFFNESS", "$.profiles.isaacLab.actuatorGroups." + group.Name + ".stiffness", report);
                ValidateOptionalNonNegative(group.Damping, "ACTUATOR_DAMPING_VALUE", "$.profiles.isaacLab.actuatorGroups." + group.Name + ".damping", report);
                ValidateOptionalPositive(group.EffortLimit, "ACTUATOR_EFFORT", "$.profiles.isaacLab.actuatorGroups." + group.Name + ".effortLimit", report);
                ValidateOptionalPositive(group.VelocityLimit, "ACTUATOR_VELOCITY", "$.profiles.isaacLab.actuatorGroups." + group.Name + ".velocityLimit", report);
                ValidateOptionalNonNegative(group.Armature, "ACTUATOR_ARMATURE", "$.profiles.isaacLab.actuatorGroups." + group.Name + ".armature", report);
                ValidateOptionalNonNegative(group.Friction, "ACTUATOR_FRICTION", "$.profiles.isaacLab.actuatorGroups." + group.Name + ".friction", report);
            }
            foreach (KeyValuePair<string, int> pair in coverage)
            {
                if (pair.Value != 1)
                {
                    report.Add(
                        ValidationSeverity.Error,
                        "ACTUATOR_COVERAGE",
                        "$.profiles.isaacLab.actuatorGroups",
                        "Movable Joint '" + pair.Key + "' must belong to exactly one actuator group; found " + pair.Value + ".");
                }
            }

            foreach (KeyValuePair<string, double> pair in profile.JointPositions ?? new Dictionary<string, double>())
            {
                JointDocument joint = robot.FindJoint(pair.Key);
                if (joint == null || !movable.Contains(pair.Key))
                {
                    report.Add(ValidationSeverity.Error, "INITIAL_JOINT", "$.profiles.isaacLab.jointPositions", "Initial state refers to unknown or non-movable Joint '" + pair.Key + "'.");
                    continue;
                }
                if (!IsFinite(pair.Value))
                {
                    report.Add(ValidationSeverity.Error, "INITIAL_JOINT_FINITE", "$.profiles.isaacLab.jointPositions." + pair.Key, "Initial joint position must be finite.");
                    continue;
                }
                if (joint.Limit != null && joint.Limit.Lower.HasValue && pair.Value < joint.Limit.Lower.Value ||
                    joint.Limit != null && joint.Limit.Upper.HasValue && pair.Value > joint.Limit.Upper.Value)
                {
                    report.Add(ValidationSeverity.Error, "INITIAL_JOINT_LIMIT", "$.profiles.isaacLab.jointPositions." + pair.Key, "Initial position is outside the Joint limit.");
                }
            }
            foreach (KeyValuePair<string, double> pair in profile.JointVelocities ?? new Dictionary<string, double>())
            {
                if (!movable.Contains(pair.Key) || !IsFinite(pair.Value))
                {
                    report.Add(ValidationSeverity.Error, "INITIAL_JOINT_VELOCITY", "$.profiles.isaacLab.jointVelocities." + pair.Key, "Initial joint velocity must refer to a movable Joint and be finite.");
                }
            }
        }

        private static void ValidateInertial(InertialDocument inertial, string path, ValidationReport report)
        {
            if (inertial == null)
            {
                report.Add(ValidationSeverity.Warning, "INERTIAL_MISSING", path, "Link has no inertial data.");
                return;
            }
            ValidatePose(inertial.Origin, path + ".origin", report);
            if (!IsFinite(inertial.Mass) || inertial.Mass <= 0.0)
            {
                report.Add(ValidationSeverity.Error, "MASS", path + ".mass", "Mass must be finite and positive.");
            }
            InertiaTensorDocument tensor = inertial.Inertia;
            if (tensor == null || !AllFinite(tensor.Ixx, tensor.Ixy, tensor.Ixz, tensor.Iyy, tensor.Iyz, tensor.Izz))
            {
                report.Add(ValidationSeverity.Error, "INERTIA_FINITE", path + ".inertia", "Inertia tensor must contain finite values.");
                return;
            }
            double minor2 = tensor.Ixx * tensor.Iyy - tensor.Ixy * tensor.Ixy;
            double determinant =
                tensor.Ixx * tensor.Iyy * tensor.Izz + 2.0 * tensor.Ixy * tensor.Ixz * tensor.Iyz -
                tensor.Ixx * tensor.Iyz * tensor.Iyz - tensor.Iyy * tensor.Ixz * tensor.Ixz -
                tensor.Izz * tensor.Ixy * tensor.Ixy;
            if (tensor.Ixx <= 0.0 || minor2 <= 0.0 || determinant <= 0.0)
            {
                report.Add(ValidationSeverity.Error, "INERTIA_POSITIVE_DEFINITE", path + ".inertia", "Inertia tensor must be positive definite.");
            }
            if (tensor.Ixx + tensor.Iyy < tensor.Izz - 1e-12 ||
                tensor.Ixx + tensor.Izz < tensor.Iyy - 1e-12 ||
                tensor.Iyy + tensor.Izz < tensor.Ixx - 1e-12)
            {
                report.Add(ValidationSeverity.Error, "INERTIA_TRIANGLE", path + ".inertia", "Principal-axis triangle inequality is violated.");
            }
        }

        private static void ValidateGeometryInstances<T>(IEnumerable<T> instances, string path, ValidationReport report)
            where T : GeometryInstanceDocument
        {
            if (instances == null)
            {
                return;
            }
            int index = 0;
            foreach (T instance in instances)
            {
                string itemPath = path + "[" + index + "]";
                if (instance == null)
                {
                    report.Add(ValidationSeverity.Error, "GEOMETRY_INSTANCE_NULL", itemPath, "Geometry instance must not be null.");
                    index++;
                    continue;
                }
                ValidatePose(instance.Origin, itemPath + ".origin", report);
                GeometryDocument geometry = instance.Geometry;
                if (geometry == null)
                {
                    report.Add(ValidationSeverity.Error, "GEOMETRY_MISSING", itemPath + ".geometry", "Geometry is required.");
                    index++;
                    continue;
                }
                switch (geometry.Type)
                {
                    case "mesh":
                        ValidateAssetUri(geometry.Uri, itemPath + ".geometry.uri", report);
                        if (geometry.Scale != null &&
                            (!AllFinite(geometry.Scale.X, geometry.Scale.Y, geometry.Scale.Z) ||
                             geometry.Scale.X <= 0.0 || geometry.Scale.Y <= 0.0 || geometry.Scale.Z <= 0.0))
                        {
                            report.Add(ValidationSeverity.Error, "MESH_SCALE", itemPath + ".geometry.scale", "Mesh scale must be finite and positive.");
                        }
                        break;
                    case "box":
                        if (geometry.Size == null || !AllFinite(geometry.Size.X, geometry.Size.Y, geometry.Size.Z) ||
                            geometry.Size.X <= 0 || geometry.Size.Y <= 0 || geometry.Size.Z <= 0)
                        {
                            report.Add(ValidationSeverity.Error, "BOX_SIZE", itemPath + ".geometry.size", "Box size must be positive.");
                        }
                        break;
                    case "cylinder":
                        if (!geometry.Radius.HasValue || !IsFinite(geometry.Radius.Value) || geometry.Radius <= 0 ||
                            !geometry.Length.HasValue || !IsFinite(geometry.Length.Value) || geometry.Length <= 0)
                        {
                            report.Add(ValidationSeverity.Error, "CYLINDER_SIZE", itemPath + ".geometry", "Cylinder radius and length must be positive.");
                        }
                        break;
                    case "sphere":
                        if (!geometry.Radius.HasValue || !IsFinite(geometry.Radius.Value) || geometry.Radius <= 0)
                        {
                            report.Add(ValidationSeverity.Error, "SPHERE_SIZE", itemPath + ".geometry.radius", "Sphere radius must be positive.");
                        }
                        break;
                    default:
                        report.Add(ValidationSeverity.Error, "GEOMETRY_TYPE", itemPath + ".geometry.type", "Unsupported geometry type.");
                        break;
                }
                index++;
            }
        }

        private static void ValidateAssetUri(string uri, string path, ValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                report.Add(ValidationSeverity.Error, "ASSET_URI", path, "Mesh URI is required.");
                return;
            }
            string normalized = uri.Replace('\\', '/');
            if (uri.IndexOf('\\') >= 0)
            {
                report.Add(ValidationSeverity.Error, "ASSET_BACKSLASH", path, "Portable asset URIs must use forward slashes.");
            }
            if (HasAbsolutePathSyntax(normalized) || normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                report.Add(ValidationSeverity.Error, "ASSET_ABSOLUTE_PATH", path, "Bundle assets must not use absolute or file:// paths.");
            }
            if (normalized.Split('/').Any(part => part == ".."))
            {
                report.Add(ValidationSeverity.Error, "ASSET_TRAVERSAL", path, "Asset URI must not traverse outside the bundle.");
            }
        }

        private static void ValidateJointLimits(JointDocument joint, string path, ValidationReport report)
        {
            if (joint.Type == "fixed" || joint.Type == "floating" || joint.Type == "planar")
            {
                return;
            }
            if (joint.Limit == null)
            {
                report.Add(ValidationSeverity.Error, "JOINT_LIMIT", path + ".limit", "Moving one-axis Joint requires effort and velocity limits.");
                return;
            }
            if (!joint.Limit.Effort.HasValue || !IsFinite(joint.Limit.Effort.Value) || joint.Limit.Effort <= 0)
            {
                report.Add(ValidationSeverity.Error, "JOINT_EFFORT", path + ".limit.effort", "Effort limit must be finite and positive.");
            }
            if (!joint.Limit.Velocity.HasValue || !IsFinite(joint.Limit.Velocity.Value) || joint.Limit.Velocity <= 0)
            {
                report.Add(ValidationSeverity.Error, "JOINT_VELOCITY", path + ".limit.velocity", "Velocity limit must be finite and positive.");
            }
            if (joint.Type == "continuous" && (joint.Limit.Lower.HasValue || joint.Limit.Upper.HasValue))
            {
                report.Add(ValidationSeverity.Error, "CONTINUOUS_BOUNDS", path + ".limit", "Continuous Joint must not have position bounds.");
            }
            if ((joint.Type == "revolute" || joint.Type == "prismatic") &&
                (!joint.Limit.Lower.HasValue || !joint.Limit.Upper.HasValue))
            {
                report.Add(ValidationSeverity.Error, "FINITE_JOINT_BOUNDS", path + ".limit", "Revolute and prismatic Joints require lower and upper bounds.");
            }
            if (joint.Limit.Lower.HasValue && !IsFinite(joint.Limit.Lower.Value) ||
                joint.Limit.Upper.HasValue && !IsFinite(joint.Limit.Upper.Value))
            {
                report.Add(ValidationSeverity.Error, "JOINT_BOUNDS_FINITE", path + ".limit", "Position bounds must be finite.");
            }
            if (joint.Limit.Lower.HasValue && joint.Limit.Upper.HasValue && joint.Limit.Lower > joint.Limit.Upper)
            {
                report.Add(ValidationSeverity.Error, "JOINT_BOUNDS_ORDER", path + ".limit", "Lower limit exceeds upper limit.");
            }
        }

        private static bool HasAbsolutePathSyntax(string value)
        {
            return Path.IsPathRooted(value) ||
                value.StartsWith("//", StringComparison.Ordinal) ||
                (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':');
        }

        private static bool IsPortableRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\\') >= 0 ||
                value.IndexOf('\0') >= 0 || HasAbsolutePathSyntax(value) ||
                value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            foreach (string segment in value.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment.EndsWith(" ", StringComparison.Ordinal) ||
                    segment.EndsWith(".", StringComparison.Ordinal) ||
                    segment.Any(character => character < 32 || "<>:\"|?*".IndexOf(character) >= 0))
                {
                    return false;
                }
                string stem = segment.Split('.')[0];
                if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
                    stem.Length == 4 &&
                    (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                     stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                    stem[3] >= '1' && stem[3] <= '9')
                {
                    return false;
                }
            }
            return true;
        }

        private static void ValidateJointDynamics(
            JointDynamicsDocument dynamics,
            string path,
            ValidationReport report)
        {
            if (dynamics == null)
            {
                return;
            }
            ValidateOptionalNonNegative(dynamics.Damping, "JOINT_DAMPING", path + ".damping", report);
            ValidateOptionalNonNegative(dynamics.Friction, "JOINT_FRICTION", path + ".friction", report);
        }

        private static void ValidateMimicValues(
            MimicDocument mimic,
            string path,
            ValidationReport report)
        {
            if (mimic == null)
            {
                return;
            }
            if (mimic.Multiplier.HasValue && !IsFinite(mimic.Multiplier.Value))
            {
                report.Add(ValidationSeverity.Error, "MIMIC_MULTIPLIER", path + ".multiplier", "Mimic multiplier must be finite.");
            }
            if (mimic.Offset.HasValue && !IsFinite(mimic.Offset.Value))
            {
                report.Add(ValidationSeverity.Error, "MIMIC_OFFSET", path + ".offset", "Mimic offset must be finite.");
            }
        }

        private static void ValidateJointSource(JointDocument joint, string path, ValidationReport report)
        {
            if (joint.Source == null || string.IsNullOrWhiteSpace(joint.Source.Kind) ||
                string.Equals(joint.Source.Kind, "unknown", StringComparison.Ordinal))
            {
                report.Add(ValidationSeverity.Error, "JOINT_SOURCE", path + ".source", "Explicit Joint source provenance is required.");
                return;
            }
            if (string.Equals(joint.Source.Kind, "solidworks_mate_suggestion", StringComparison.Ordinal) && !joint.Source.UserConfirmed)
            {
                report.Add(ValidationSeverity.Error, "MATE_UNCONFIRMED", path + ".source", "A Mate suggestion must be explicitly confirmed before export.");
            }
            else if (!joint.Source.UserConfirmed &&
                !string.Equals(joint.Source.Kind, "imported_urdf", StringComparison.Ordinal))
            {
                report.Add(
                    ValidationSeverity.Error,
                    "JOINT_SOURCE_UNCONFIRMED",
                    path + ".source",
                    "Joint semantics from this source must be explicitly reviewed and confirmed before export.");
            }
        }

        private static void ValidateAxis(Vector3Document axis, string path, ValidationReport report)
        {
            if (axis == null || !AllFinite(axis.X, axis.Y, axis.Z) || axis.SquaredMagnitude() <= 1e-24)
            {
                report.Add(ValidationSeverity.Error, "JOINT_AXIS", path, "Moving Joint requires a finite non-zero axis.");
                return;
            }
            double magnitude = Math.Sqrt(axis.SquaredMagnitude());
            if (Math.Abs(magnitude - 1.0) > 1e-6)
            {
                report.Add(ValidationSeverity.Warning, "JOINT_AXIS_NORMALIZATION", path, "Axis is not unit length and will be normalized by the URDF writer.");
            }
        }

        private static void ValidatePose(PoseDocument pose, string path, ValidationReport report)
        {
            if (pose == null || pose.Xyz == null || pose.Rpy == null ||
                !AllFinite(pose.Xyz.X, pose.Xyz.Y, pose.Xyz.Z, pose.Rpy.X, pose.Rpy.Y, pose.Rpy.Z))
            {
                report.Add(ValidationSeverity.Error, "POSE_FINITE", path, "Pose must contain finite xyz and rpy values.");
            }
        }

        private static void ValidateIsaacName(string name, string path, ValidationReport report)
        {
            if (!string.IsNullOrWhiteSpace(name) && IsaacUnsafeName.IsMatch(name))
            {
                report.Add(
                    ValidationSeverity.Warning,
                    "ISAAC_NAME_REMAP",
                    path,
                    "Name is not a safe USD prim identifier. Rename it explicitly before enabling Isaac; the adapter will not silently change control identities.");
            }
        }

        private static void ValidateStringList(
            IList<string> values,
            string code,
            string path,
            ValidationReport report)
        {
            if (values == null)
            {
                return;
            }
            if (values.Any(string.IsNullOrWhiteSpace))
            {
                report.Add(ValidationSeverity.Error, code, path, "Interface names must not be blank.");
            }
            AddDuplicateErrors(values, code + "_DUPLICATE", path, report);
        }

        private static void ValidateVector(Vector3Document value, string path, ValidationReport report)
        {
            if (value == null || !AllFinite(value.X, value.Y, value.Z))
            {
                report.Add(ValidationSeverity.Error, "VECTOR_FINITE", path, "Vector values must be finite.");
            }
        }

        private static void ValidateQuaternion(
            QuaternionWxyzDocument value,
            string path,
            ValidationReport report)
        {
            if (value == null || !AllFinite(value.W, value.X, value.Y, value.Z))
            {
                report.Add(ValidationSeverity.Error, "QUATERNION_FINITE", path, "Quaternion values must be finite.");
                return;
            }
            double magnitudeSquared = value.W * value.W + value.X * value.X + value.Y * value.Y + value.Z * value.Z;
            if (magnitudeSquared <= 1e-24)
            {
                report.Add(ValidationSeverity.Error, "QUATERNION_ZERO", path, "Quaternion must be non-zero.");
            }
            else if (Math.Abs(Math.Sqrt(magnitudeSquared) - 1.0) > 1e-6)
            {
                report.Add(ValidationSeverity.Error, "QUATERNION_NORMALIZATION", path, "Quaternion must be unit length.");
            }
        }

        private static void ValidateIsaacPhysics(IsaacPhysicsProfile physics, ValidationReport report)
        {
            if (physics == null)
            {
                report.Add(ValidationSeverity.Error, "ISAACLAB_PHYSICS", "$.profiles.isaacLab.physics", "Isaac Lab physics settings are required.");
                return;
            }
            if (physics.SolverPositionIterationCount < 1 || physics.SolverVelocityIterationCount < 0)
            {
                report.Add(ValidationSeverity.Error, "ISAACLAB_SOLVER_ITERATIONS", "$.profiles.isaacLab.physics", "Position iterations must be positive and velocity iterations must be non-negative.");
            }
            if (!IsFinite(physics.MaxDepenetrationVelocity) || physics.MaxDepenetrationVelocity <= 0.0)
            {
                report.Add(ValidationSeverity.Error, "ISAACLAB_DEPENETRATION", "$.profiles.isaacLab.physics.maxDepenetrationVelocity", "Maximum depenetration velocity must be finite and positive.");
            }
        }

        private static void ValidateOptionalNonNegative(
            double? value,
            string code,
            string path,
            ValidationReport report)
        {
            if (value.HasValue && (!IsFinite(value.Value) || value.Value < 0.0))
            {
                report.Add(ValidationSeverity.Error, code, path, "Value must be finite and non-negative.");
            }
        }

        private static void ValidateOptionalPositive(
            double? value,
            string code,
            string path,
            ValidationReport report)
        {
            if (value.HasValue && (!IsFinite(value.Value) || value.Value <= 0.0))
            {
                report.Add(ValidationSeverity.Error, code, path, "Value must be finite and positive.");
            }
        }

        private static void AddDuplicateErrors(
            IEnumerable<string> values,
            string code,
            string path,
            ValidationReport report)
        {
            foreach (IGrouping<string, string> duplicate in values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1))
            {
                report.Add(ValidationSeverity.Error, code, path, "Duplicate value '" + duplicate.Key + "'.");
            }
        }

        private static bool HasCycle(
            string node,
            IDictionary<string, List<string>> children,
            ISet<string> visiting,
            ISet<string> visited)
        {
            if (visiting.Contains(node))
            {
                return true;
            }
            if (visited.Contains(node))
            {
                return false;
            }
            visiting.Add(node);
            foreach (string child in children[node])
            {
                if (HasCycle(child, children, visiting, visited))
                {
                    return true;
                }
            }
            visiting.Remove(node);
            visited.Add(node);
            return false;
        }

        private static bool AllFinite(params double[] values)
        {
            return values.All(IsFinite);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsExactVersion(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                Regex.IsMatch(value, "^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant);
        }
    }
}
