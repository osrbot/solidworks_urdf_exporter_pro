using System;
using System.Collections.Generic;
using System.Linq;
using OSURDF.Core.Model;

namespace OSURDF.Core.Validation
{
    internal static class SimulationProfileValidator
    {
        private const string Path = "$.profiles.simulation";
        private const string MjcfPath = Path + ".mjcf";

        public static void ValidateCommon(RobotDocument robot, ValidationReport report)
        {
            SimulationProfile profile = robot.Profiles?.Simulation;
            if (profile == null) return;
            if (!new[] { "source", "fixed", "floating" }.Contains(profile.BaseMode, StringComparer.Ordinal))
                Error(report, "SIMULATION_BASE_MODE", Path + ".baseMode", "Base mode must be source, fixed, or floating.");
            if (profile.JointDrives == null)
                Error(report, "SIMULATION_DRIVES_NULL", Path + ".jointDrives", "Joint drives must be an array.");
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<JointDriveIntent> drives = profile.JointDrives ?? new List<JointDriveIntent>();
            for (int index = 0; index < drives.Count; index++)
            {
                JointDriveIntent drive = drives[index];
                string path = Path + ".jointDrives[" + index + "]";
                if (drive == null)
                {
                    Error(report, "SIMULATION_DRIVE_NULL", path, "Joint drive intent must be an object.");
                    continue;
                }
                JointDocument joint = FindJoint(robot, drive.Joint);
                if (string.IsNullOrWhiteSpace(drive.Joint) || joint == null)
                    Error(report, "SIMULATION_DRIVE_JOINT", path + ".joint", "Drive intent must reference an existing Joint.");
                if (!seen.Add(drive.Joint ?? string.Empty))
                    Error(report, "SIMULATION_DRIVE_DUPLICATE", path + ".joint", "Each Joint may have at most one drive intent.");
                if (!new[] { "passive", "position", "velocity", "effort" }.Contains(drive.Mode, StringComparer.Ordinal))
                    Error(report, "SIMULATION_DRIVE_MODE", path + ".mode", "Drive mode must be passive, position, velocity, or effort.");
                if (IsActive(drive) && joint != null)
                {
                    if (!IsScalar(joint))
                        Error(report, "SIMULATION_DRIVE_JOINT_TYPE", path + ".joint", "Active drives require a revolute, continuous, or prismatic Joint.");
                    if (joint.Mimic != null)
                        Error(report, "SIMULATION_DRIVE_MIMIC", path + ".joint", "Mimic Joints cannot have active drive intents.");
                }
            }
        }

        public static void ValidateMjcf(RobotDocument robot, ValidationReport report, bool requireTarget)
        {
            SimulationProfile profile = robot.Profiles?.Simulation;
            if (profile == null) return;
            List<JointDriveIntent> intents = profile.JointDrives ?? new List<JointDriveIntent>();
            if (profile.Mjcf == null)
            {
                if (requireTarget && intents.Any(IsActive))
                    Error(report, "MJCF_PROFILE_REQUIRED", MjcfPath, "MJCF export with active drive intents requires explicit MJCF settings.");
                return;
            }
            if (profile.Mjcf.JointDrives == null)
                Error(report, "MJCF_DRIVES_NULL", MjcfPath + ".jointDrives", "MJCF joint drives must be an array.");
            List<MjcfJointDriveProfile> drives = profile.Mjcf.JointDrives ?? new List<MjcfJointDriveProfile>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < drives.Count; index++)
            {
                MjcfJointDriveProfile drive = drives[index];
                string path = MjcfPath + ".jointDrives[" + index + "]";
                if (drive == null)
                {
                    Error(report, "MJCF_DRIVE_NULL", path, "MJCF joint drive must be an object.");
                    continue;
                }
                JointDriveIntent intent = intents.FirstOrDefault(item => item != null && item.Joint == drive.Joint);
                if (string.IsNullOrWhiteSpace(drive.Joint) || FindJoint(robot, drive.Joint) == null || intent == null)
                    Error(report, "MJCF_DRIVE_JOINT", path + ".joint", "MJCF tuning must reference an existing Joint with shared drive intent.");
                if (!seen.Add(drive.Joint ?? string.Empty))
                    Error(report, "MJCF_DRIVE_DUPLICATE", path + ".joint", "Each Joint may have at most one MJCF tuning entry.");
                if (drive.Stiffness.HasValue && (!Finite(drive.Stiffness.Value) || drive.Stiffness.Value < 0))
                    Error(report, "MJCF_DRIVE_STIFFNESS", path + ".stiffness", "Stiffness must be finite and non-negative in SI units.");
                if (drive.Damping.HasValue && (!Finite(drive.Damping.Value) || drive.Damping.Value < 0))
                    Error(report, "MJCF_DRIVE_DAMPING", path + ".damping", "Damping must be finite and non-negative in SI units.");
                if (drive.MaxForce.HasValue && !Positive(drive.MaxForce))
                    Error(report, "MJCF_DRIVE_MAX_FORCE", path + ".maxForce", "Max force must be finite and positive (N or N m); gear is fixed at 1.");
                if (intent != null)
                {
                    if ((intent.Mode == "passive" || intent.Mode == "effort") && (drive.Stiffness.HasValue || drive.Damping.HasValue))
                        Error(report, "MJCF_DRIVE_GAIN_MODE", path, "Passive and effort modes do not accept stiffness or damping gains.");
                    if (intent.Mode == "velocity" && drive.Stiffness.HasValue && drive.Stiffness.Value != 0)
                        Error(report, "MJCF_DRIVE_VELOCITY_STIFFNESS", path + ".stiffness", "Velocity mode stiffness must be omitted or zero.");
                    if (intent.Mode == "passive" && drive.MaxForce.HasValue)
                        Error(report, "MJCF_DRIVE_PASSIVE_FORCE", path + ".maxForce", "Passive mode does not generate an actuator or accept a force limit.");
                }
            }
            foreach (JointDriveIntent intent in intents.Where(IsActive))
            {
                JointDocument joint = FindJoint(robot, intent.Joint);
                if (joint == null || !IsScalar(joint) || joint.Mimic != null) continue;
                MjcfJointDriveProfile drive = drives.FirstOrDefault(item => item != null && item.Joint == intent.Joint);
                string path = drive == null ? MjcfPath : MjcfPath + ".jointDrives[" + drives.IndexOf(drive) + "]";
                if (intent.Mode == "position" && (!Positive(drive?.Stiffness) ||
                    !drive.Damping.HasValue || !Finite(drive.Damping.Value) || drive.Damping.Value < 0))
                    Error(report, "MJCF_POSITION_GAINS", path, "Position drive '" + intent.Joint + "' requires explicit positive stiffness and non-negative damping, both finite SI values.");
                if (intent.Mode == "velocity" && !Positive(drive?.Damping))
                    Error(report, "MJCF_VELOCITY_GAIN", path, "Velocity drive '" + intent.Joint + "' requires explicit finite positive damping in SI units.");
                if (!Positive(drive?.MaxForce ?? joint.Limit?.Effort))
                    Error(report, "MJCF_DRIVE_EFFORT_LIMIT", path, "Active drive '" + intent.Joint + "' requires finite positive maxForce or a valid source Joint effort limit.");
            }
        }

        private static JointDocument FindJoint(RobotDocument robot, string name) =>
            (robot.Joints ?? new List<JointDocument>()).FirstOrDefault(joint => joint != null && joint.Name == name);
        private static bool IsScalar(JointDocument joint) =>
            new[] { "revolute", "continuous", "prismatic" }.Contains(joint.Type, StringComparer.Ordinal);
        private static bool IsActive(JointDriveIntent intent) => intent != null &&
            new[] { "position", "velocity", "effort" }.Contains(intent.Mode, StringComparer.Ordinal);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Positive(double? value) => value.HasValue && Finite(value.Value) && value.Value > 0;
        private static void Error(ValidationReport report, string code, string path, string message) =>
            report.Add(ValidationSeverity.Error, code, path, message);
    }
}
