using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SW2URDF.URDFExport
{
    public sealed class ExportTargetValidationFinding
    {
        public ExportTargetValidationFinding(string code, string field, string message)
        {
            Code = code;
            Field = field;
            Message = message;
        }

        public string Code { get; }

        public string Field { get; }

        public string Message { get; }
    }

    public sealed class ExportTargetOptions
    {
        private static readonly Regex ExactVersion = new Regex(
            "^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$",
            RegexOptions.CultureInvariant);
        private static readonly Regex EmailAddress = new Regex(
            "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$",
            RegexOptions.CultureInvariant);

        public bool UseV2Pipeline { get; set; }
        public bool ExportRos1Legacy { get; set; }
        public bool ExportRos2 { get; set; }
        public bool ExportUsdAsset { get; set; }
        public bool ExportMjcfAsset { get; set; }

        public string PackageVersion { get; set; }
        public string Description { get; set; }
        public string MaintainerName { get; set; }
        public string MaintainerEmail { get; set; }
        public string ModelLicense { get; set; }
        public string ModelAuthor { get; set; }

        public string Ros2Distribution { get; set; }
        public string GazeboDistribution { get; set; }
        public string Ros2ControlProfileFile { get; set; }

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
        }

        public static ExportTargetOptions LegacyCompatibilityDefaults()
        {
            return new ExportTargetOptions
            {
                UseV2Pipeline = false,
                ExportRos1Legacy = true,
                ExportRos2 = true
            };
        }

        public static ExportTargetOptions RecommendedDefaults(string packageName)
        {
            string normalizedName = string.IsNullOrWhiteSpace(packageName)
                ? "robot_description"
                : packageName.Trim();
            return new ExportTargetOptions
            {
                UseV2Pipeline = true,
                ExportRos1Legacy = false,
                ExportRos2 = true,
                ExportUsdAsset = false,
                ExportMjcfAsset = false,
                PackageVersion = "0.1.0",
                Description = "Robot description package for " + normalizedName,
                MaintainerName = SW2URDF.URDF.PackageXML.DefaultMaintainerName,
                MaintainerEmail = SW2URDF.URDF.PackageXML.DefaultMaintainerEmail,
                ModelLicense = "NOASSERTION",
                ModelAuthor = string.Empty,
                Ros2Distribution = "lyrical",
                GazeboDistribution = "jetty"
            };
        }

        public IList<string> Validate()
        {
            List<string> errors = new List<string>();
            foreach (ExportTargetValidationFinding finding in ValidateFindings())
            {
                errors.Add(finding.Message);
            }
            return errors;
        }

        public IList<ExportTargetValidationFinding> ValidateFindings()
        {
            List<ExportTargetValidationFinding> errors =
                new List<ExportTargetValidationFinding>();
            if (!UseV2Pipeline)
            {
                return errors;
            }
            if (!(ExportRos1Legacy || ExportRos2 || ExportUsdAsset || ExportMjcfAsset))
            {
                Add(errors, "TARGET_REQUIRED", "Targets",
                    "Select at least one output target: ROS 1, ROS 2, OpenUSD, or MuJoCo MJCF.");
            }
            if (ExportRos1Legacy || ExportRos2)
            {
                if (!ExactVersion.IsMatch(PackageVersion ?? string.Empty))
                {
                    Add(errors, "PACKAGE_VERSION", "PackageVersion",
                        "Package version must be an exact semantic version, for example 0.1.0.");
                }
                Require(errors, Description, "PACKAGE_DESCRIPTION", "Description",
                    "Package description");
                Require(errors, ModelLicense, "MODEL_LICENSE", "ModelLicense",
                    "Model license");
                Require(errors, MaintainerName, "MAINTAINER_NAME", "MaintainerName",
                    "Maintainer name");
                Require(errors, MaintainerEmail, "MAINTAINER_EMAIL", "MaintainerEmail",
                    "Maintainer email");
                if (!string.IsNullOrWhiteSpace(MaintainerEmail) &&
                    !EmailAddress.IsMatch(MaintainerEmail))
                {
                    Add(errors, "MAINTAINER_EMAIL_FORMAT", "MaintainerEmail",
                        "Maintainer email is not a valid email address.");
                }
            }
            if (ExportRos2 &&
                !(string.Equals(Ros2Distribution, "lyrical", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(GazeboDistribution, "jetty", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(Ros2Distribution, "jazzy", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(GazeboDistribution, "harmonic", StringComparison.OrdinalIgnoreCase)))
            {
                Add(errors, "ROS2_GAZEBO_PAIR", "Ros2Distribution",
                    "Supported ROS 2 / Gazebo pairs are Lyrical / Jetty and Jazzy / Harmonic.");
            }
            if (!string.IsNullOrWhiteSpace(Ros2ControlProfileFile) &&
                (!ExportRos2 || !File.Exists(Ros2ControlProfileFile)))
            {
                Add(errors, "ROS2_CONTROL_PROFILE", "Ros2ControlProfileFile",
                    "A ros2_control profile must be an existing JSON file and requires ROS 2 output.");
            }
            return errors;
        }

        private static void Require(
            ICollection<ExportTargetValidationFinding> errors,
            string value,
            string code,
            string field,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Add(errors, code, field,
                    label + " is required for the selected output profiles.");
            }
        }

        private static void Add(
            ICollection<ExportTargetValidationFinding> errors,
            string code,
            string field,
            string message)
        {
            errors.Add(new ExportTargetValidationFinding(code, field, message));
        }
    }
}
