using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OSURDF.Core.Model;
using OSURDF.Core.Validation;

namespace SW2URDF.URDFExport
{
    internal static partial class V2ExportBridge
    {
        internal static ExportTargetOptions ForTarget(ExportTargetOptions source, string targetName)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (targetName != "ROS 1" && targetName != "ROS 2" &&
                targetName != "OpenUSD" && targetName != "MuJoCo MJCF")
            {
                throw new ArgumentException("Unknown export target: " + targetName, nameof(targetName));
            }
            return new ExportTargetOptions
            {
                UseV2Pipeline = source.UseV2Pipeline,
                ExportRos1Legacy = targetName == "ROS 1" && source.ExportRos1Legacy,
                ExportRos2 = targetName == "ROS 2" && source.ExportRos2,
                ExportUsdAsset = targetName == "OpenUSD" && source.ExportUsdAsset,
                ExportMjcfAsset = targetName == "MuJoCo MJCF" && source.ExportMjcfAsset,
                PackageVersion = source.PackageVersion,
                Description = source.Description,
                MaintainerName = source.MaintainerName,
                MaintainerEmail = source.MaintainerEmail,
                ModelLicense = source.ModelLicense,
                ModelAuthor = source.ModelAuthor,
                Ros2Distribution = source.Ros2Distribution,
                GazeboDistribution = source.GazeboDistribution,
                Ros2ControlProfileFile = targetName == "ROS 2" && source.ExportRos2
                    ? source.Ros2ControlProfileFile : string.Empty,
                UsdSimulation = targetName == "OpenUSD" && source.ExportUsdAsset
                    ? CloneTargetUsdSimulation(source.UsdSimulation) : new UsdSimulationProfile()
            };
        }

        internal static IDictionary<string, string> PrepareTargetProfiles(
            RobotDocument robot,
            ExportTargetOptions options)
        {
            if (robot == null) throw new ArgumentNullException(nameof(robot));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (robot.Profiles == null) throw new InvalidDataException("Robot profiles are required.");

            RobotProfiles profiles = robot.Profiles;
            profiles.Ros1 = new Ros1ExportProfile { Enabled = options.ExportRos1Legacy };
            profiles.Ros2 = new Ros2ExportProfile
            {
                Enabled = options.ExportRos2,
                Distribution = options.Ros2Distribution,
                GazeboDistribution = options.GazeboDistribution,
                ModernGazebo = true
            };
            profiles.Isaac = profiles.Isaac ?? new IsaacExportProfile();
            profiles.Isaac.Enabled = false;
            profiles.IsaacLab = profiles.IsaacLab ?? new IsaacLabProfile();
            profiles.IsaacLab.Enabled = false;
            profiles.UsdSimulation = options.ExportUsdAsset
                ? CloneTargetUsdSimulation(options.UsdSimulation) : new UsdSimulationProfile();

            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string target in new[] { "ROS 1", "ROS 2", "OpenUSD", "MuJoCo MJCF" })
            {
                ExportTargetOptions single = ForTarget(options, target);
                if (!(single.ExportRos1Legacy || single.ExportRos2 || single.ExportUsdAsset || single.ExportMjcfAsset))
                {
                    continue;
                }
                foreach (ExportTargetValidationFinding finding in single.ValidateFindings())
                {
                    AddTargetProfileError(errors, target, finding.Code + ": " + finding.Message);
                }
            }
            ResetFailedTargetProfiles(profiles, errors);

            if (profiles.Ros2.Enabled && !string.IsNullOrWhiteSpace(options.Ros2ControlProfileFile))
            {
                try
                {
                    Ros2ControlProfile control = ReadStrictProfile<Ros2ControlProfile>(
                        options.Ros2ControlProfileFile, "ros2_control");
                    if (control == null) throw new InvalidDataException("ros2_control profile JSON is empty.");
                    control.Enabled = true;
                    profiles.Ros2.Ros2Control = control;
                }
                catch (Exception exception) when (
                    exception is IOException || exception is InvalidDataException ||
                    exception is UnauthorizedAccessException)
                {
                    AddTargetProfileError(errors, "ROS 2", exception.Message);
                }
            }
            ResetFailedTargetProfiles(profiles, errors);

            // Inspect the real shared model, but isolate only target-owned profile findings.
            foreach (ValidationFinding finding in new RobotValidator().Validate(robot).Findings)
            {
                if (finding.Severity != ValidationSeverity.Error) continue;
                if (options.ExportUsdAsset && IsTargetProfilePath(finding.Path, "$.profiles.usdSimulation"))
                {
                    AddTargetProfileError(errors, "OpenUSD", finding.ToString());
                }
                else if (profiles.Ros2.Enabled && IsTargetProfilePath(finding.Path, "$.profiles.ros2"))
                {
                    AddTargetProfileError(errors, "ROS 2", finding.ToString());
                }
                else if (profiles.Ros1.Enabled && IsTargetProfilePath(finding.Path, "$.profiles.ros1"))
                {
                    AddTargetProfileError(errors, "ROS 1", finding.ToString());
                }
                else if (IsTargetProfilePath(finding.Path, "$.profiles.package"))
                {
                    if (profiles.Ros1.Enabled) AddTargetProfileError(errors, "ROS 1", finding.ToString());
                    if (profiles.Ros2.Enabled) AddTargetProfileError(errors, "ROS 2", finding.ToString());
                }
            }
            ResetFailedTargetProfiles(profiles, errors);
            return errors;
        }

        private static bool IsTargetProfilePath(string path, string prefix)
        {
            return path != null && (path == prefix ||
                path.StartsWith(prefix + ".", StringComparison.Ordinal) ||
                path.StartsWith(prefix + "[", StringComparison.Ordinal));
        }

        private static void AddTargetProfileError(IDictionary<string, string> errors, string target, string message)
        {
            string existing;
            errors[target] = errors.TryGetValue(target, out existing)
                ? existing + Environment.NewLine + message : message;
        }

        private static void ResetFailedTargetProfiles(RobotProfiles profiles, IDictionary<string, string> errors)
        {
            if (errors.ContainsKey("ROS 1")) profiles.Ros1 = new Ros1ExportProfile();
            if (errors.ContainsKey("ROS 2")) profiles.Ros2 = new Ros2ExportProfile();
            if (errors.ContainsKey("OpenUSD")) profiles.UsdSimulation = new UsdSimulationProfile();
        }

        private static UsdSimulationProfile CloneTargetUsdSimulation(UsdSimulationProfile source)
        {
            if (source == null) return null;
            // Keep malformed structure visible to RobotValidator instead of silently dropping it.
            return new UsdSimulationProfile
            {
                BaseMode = source.BaseMode,
                RobotType = source.RobotType,
                AllowSelfCollision = source.AllowSelfCollision,
                GainUnits = source.GainUnits,
                JointDrives = source.JointDrives == null ? null : source.JointDrives.Select(drive =>
                    drive == null ? null : new UsdJointDriveProfile
                    {
                        Joint = drive.Joint,
                        Mode = drive.Mode,
                        Stiffness = drive.Stiffness,
                        Damping = drive.Damping
                    }).ToList()
            };
        }
    }
}
