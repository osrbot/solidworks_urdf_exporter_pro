using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SW2URDF.URDFExport
{
    public sealed class ExportTargetOptions
    {
        private static readonly Regex ExactVersion = new Regex(
            "^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$",
            RegexOptions.CultureInvariant);
        private static readonly Regex EmailAddress = new Regex(
            "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$",
            RegexOptions.CultureInvariant);

        public bool UseV2Pipeline { get; set; }
        public bool CreateRobotBundle { get; set; }
        public bool ExportRos1Legacy { get; set; }
        public bool ExportRos2 { get; set; }
        public bool ExportIsaacSim { get; set; }
        public bool ExportIsaacLab { get; set; }

        public string PackageVersion { get; set; }
        public string Description { get; set; }
        public string MaintainerName { get; set; }
        public string MaintainerEmail { get; set; }
        public string ModelLicense { get; set; }
        public string ModelAuthor { get; set; }

        public string Ros2Distribution { get; set; }
        public string GazeboDistribution { get; set; }
        public string Ros2ControlProfileFile { get; set; }
        public string IsaacSimVersion { get; set; }
        public string IsaacLabVersion { get; set; }
        public string IsaacLabProfileFile { get; set; }

        public ExportTargetOptions()
        {
            PackageVersion = "0.1.0";
            Ros2Distribution = "lyrical";
            GazeboDistribution = "jetty";
            Ros2ControlProfileFile = string.Empty;
            Description = string.Empty;
            MaintainerName = string.Empty;
            MaintainerEmail = string.Empty;
            ModelLicense = string.Empty;
            ModelAuthor = string.Empty;
            IsaacSimVersion = string.Empty;
            IsaacLabVersion = string.Empty;
            IsaacLabProfileFile = string.Empty;
        }

        public static ExportTargetOptions LegacyCompatibilityDefaults()
        {
            return new ExportTargetOptions
            {
                UseV2Pipeline = false,
                CreateRobotBundle = false,
                ExportRos1Legacy = true,
                ExportRos2 = true
            };
        }

        public IList<string> Validate()
        {
            List<string> errors = new List<string>();
            if (!UseV2Pipeline)
            {
                return errors;
            }
            if (!CreateRobotBundle)
            {
                errors.Add("The v2 pipeline requires a Robot Bundle as its canonical output.");
            }
            if (ExportIsaacLab && !ExportIsaacSim)
            {
                errors.Add("Isaac Lab output requires the Isaac Sim USD profile.");
            }
            if (!ExactVersion.IsMatch(PackageVersion ?? string.Empty))
            {
                errors.Add("Package version must be an exact semantic version, for example 0.1.0.");
            }
            Require(errors, Description, "Package description");
            Require(errors, ModelLicense, "Model license");
            if (ExportRos1Legacy || ExportRos2)
            {
                Require(errors, MaintainerName, "Maintainer name");
                Require(errors, MaintainerEmail, "Maintainer email");
                if (!string.IsNullOrWhiteSpace(MaintainerEmail) &&
                    !EmailAddress.IsMatch(MaintainerEmail))
                {
                    errors.Add("Maintainer email is not a valid email address.");
                }
            }
            if (ExportRos2 &&
                !(string.Equals(Ros2Distribution, "lyrical", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(GazeboDistribution, "jetty", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(Ros2Distribution, "jazzy", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(GazeboDistribution, "harmonic", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("Supported ROS 2 / Gazebo pairs are Lyrical / Jetty and Jazzy / Harmonic.");
            }
            if (!string.IsNullOrWhiteSpace(Ros2ControlProfileFile) &&
                (!ExportRos2 || !File.Exists(Ros2ControlProfileFile)))
            {
                errors.Add("A ros2_control profile must be an existing JSON file and requires ROS 2 output.");
            }
            if (ExportIsaacSim)
            {
                if (!ExactVersion.IsMatch(IsaacSimVersion ?? string.Empty))
                {
                    errors.Add("Pin an exact Isaac Sim version, for example 6.0.0.");
                }
                Require(errors, ModelLicense, "Model license");
            }
            if (ExportIsaacLab)
            {
                if (!ExactVersion.IsMatch(IsaacLabVersion ?? string.Empty))
                {
                    errors.Add("Pin an exact Isaac Lab version, for example 2.3.2.");
                }
                if (string.IsNullOrWhiteSpace(IsaacLabProfileFile) ||
                    !File.Exists(IsaacLabProfileFile))
                {
                    errors.Add("Select an Isaac Lab actuator profile JSON file. Gains are never guessed from CAD.");
                }
            }
            return errors;
        }

        private static void Require(ICollection<string> errors, string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(label + " is required for the selected output profiles.");
            }
        }
    }
}
