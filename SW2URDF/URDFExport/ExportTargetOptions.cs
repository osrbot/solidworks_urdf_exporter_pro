using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using OSURDF.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        public UsdSimulationProfile UsdSimulation { get; set; }
        public SimulationProfile Simulation { get; set; }
        public string UsdSimulationRestoreError { get; set; }
        public string MjcfSimulationRestoreError { get; set; }

        private sealed class SavedModelSettings
        {
            public int Version { get; set; }
            public string OutputName { get; set; }
            public bool? ExportRos1Legacy { get; set; }
            public bool? ExportRos2 { get; set; }
            public bool? ExportUsdAsset { get; set; }
            public bool? ExportMjcfAsset { get; set; }
            public string PackageVersion { get; set; }
            public string Description { get; set; }
            public string MaintainerName { get; set; }
            public string MaintainerEmail { get; set; }
            public string ModelLicense { get; set; }
            public string ModelAuthor { get; set; }
        }

        public void SaveModelSettings(SW2URDF.URDF.Link root, string outputName)
        {
            if (root == null || root.Parent != null)
                throw new ArgumentException("Model settings belong to the root Link.");
            root.ModelSettingsJson = JsonConvert.SerializeObject(new SavedModelSettings
            {
                Version = 1,
                OutputName = outputName ?? string.Empty,
                ExportRos1Legacy = ExportRos1Legacy,
                ExportRos2 = ExportRos2,
                ExportUsdAsset = ExportUsdAsset,
                ExportMjcfAsset = ExportMjcfAsset,
                PackageVersion = PackageVersion ?? string.Empty,
                Description = Description ?? string.Empty,
                MaintainerName = MaintainerName ?? string.Empty,
                MaintainerEmail = MaintainerEmail ?? string.Empty,
                ModelLicense = ModelLicense ?? string.Empty,
                ModelAuthor = ModelAuthor ?? string.Empty,
            });
        }

        public static string RestoreModelSettings(SW2URDF.URDF.Link root, ExportTargetOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (root == null || string.IsNullOrWhiteSpace(root.ModelSettingsJson)) return null;
            var saved = JsonConvert.DeserializeObject<SavedModelSettings>(root.ModelSettingsJson);
            if (saved == null || saved.Version != 1)
                throw new InvalidDataException("Unsupported saved model settings version.");
            if (saved.ExportRos1Legacy.HasValue) options.ExportRos1Legacy = saved.ExportRos1Legacy.Value;
            if (saved.ExportRos2.HasValue) options.ExportRos2 = saved.ExportRos2.Value;
            if (saved.ExportUsdAsset.HasValue) options.ExportUsdAsset = saved.ExportUsdAsset.Value;
            if (saved.ExportMjcfAsset.HasValue) options.ExportMjcfAsset = saved.ExportMjcfAsset.Value;
            if (saved.PackageVersion != null) options.PackageVersion = saved.PackageVersion;
            if (saved.Description != null) options.Description = saved.Description;
            if (saved.MaintainerName != null) options.MaintainerName = saved.MaintainerName;
            if (saved.MaintainerEmail != null) options.MaintainerEmail = saved.MaintainerEmail;
            if (saved.ModelLicense != null) options.ModelLicense = saved.ModelLicense;
            if (saved.ModelAuthor != null) options.ModelAuthor = saved.ModelAuthor;
            return saved.OutputName;
        }

        public static SimulationProfile CloneSimulation(SimulationProfile source)
        {
            return source == null ? null : JsonConvert.DeserializeObject<SimulationProfile>(
                JsonConvert.SerializeObject(source));
        }

        public void SaveSimulationSettings(SW2URDF.URDF.Link root)
        {
            if (root == null || root.Parent != null)
                throw new ArgumentException("Simulation settings belong to the root Link.");
            root.SimulationSettingsJson = JsonConvert.SerializeObject(new
            {
                version = 1,
                simulation = Simulation,
                usdSimulation = UsdSimulation
            });
        }

        public static void RestoreSimulationSettings(SW2URDF.URDF.Link root, ExportTargetOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (root == null || string.IsNullOrWhiteSpace(root.SimulationSettingsJson)) return;
            JObject stored = JObject.Parse(root.SimulationSettingsJson);
            if ((int?)stored["version"] != 1)
                throw new InvalidDataException("Unsupported saved simulation settings version.");
            SimulationProfile simulation = null;
            JToken mjcfToken = null;
            JToken commonToken = stored["simulation"];
            if (commonToken != null && commonToken.Type != JTokenType.Null)
            {
                if (commonToken.Type != JTokenType.Object)
                    throw new InvalidDataException("Saved common simulation settings must be an object.");
                // Target tuning must not prevent restoration of shared intent or the other target.
                JObject common = (JObject)commonToken.DeepClone();
                mjcfToken = common["mjcf"];
                common.Remove("mjcf");
                simulation = common.ToObject<SimulationProfile>();
            }

            string usdError;
            UsdSimulationProfile usd = RestoreTargetSimulation<UsdSimulationProfile>(
                stored["usdSimulation"], "OpenUSD", out usdError);
            string mjcfError = null;
            if (simulation != null && mjcfToken != null)
                simulation.Mjcf = mjcfToken.Type == JTokenType.Null ? null :
                    RestoreTargetSimulation<MjcfSimulationProfile>(mjcfToken, "MuJoCo MJCF", out mjcfError);
            options.UsdSimulation = usd;
            options.Simulation = simulation;
            options.UsdSimulationRestoreError = options.UsdSimulationRestoreError ?? usdError;
            options.MjcfSimulationRestoreError = options.MjcfSimulationRestoreError ?? mjcfError;
        }

        private static T RestoreTargetSimulation<T>(JToken token, string target, out string error)
            where T : class
        {
            try
            {
                if (token == null || token.Type != JTokenType.Object)
                    throw new InvalidDataException("Saved " + target + " simulation settings must be an object.");
                T profile = token.ToObject<T>();
                error = null;
                return profile;
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidDataException)
            {
                error = "Saved " + target + " simulation settings require explicit repair: " + exception.Message;
                return null;
            }
        }

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
            UsdSimulation = new UsdSimulationProfile();
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
                ExportRos1Legacy = true,
                ExportRos2 = true,
                ExportUsdAsset = true,
                ExportMjcfAsset = true,
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

        public IList<ExportTargetValidationFinding> ValidateSharedFindings()
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
            return errors;
        }

        public IList<ExportTargetValidationFinding> ValidateRosMetadataFindings()
        {
            var errors = new List<ExportTargetValidationFinding>();
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
            return errors;
        }

        public IList<ExportTargetValidationFinding> ValidateFindings()
        {
            var errors = new List<ExportTargetValidationFinding>(ValidateSharedFindings());
            if (!UseV2Pipeline) return errors;
            if (ExportUsdAsset && !string.IsNullOrEmpty(UsdSimulationRestoreError))
                Add(errors, "USD_SIMULATION_RESTORE", "UsdSimulation", UsdSimulationRestoreError);
            if (ExportMjcfAsset && !string.IsNullOrEmpty(MjcfSimulationRestoreError))
                Add(errors, "MJCF_SIMULATION_RESTORE", "Simulation.Mjcf", MjcfSimulationRestoreError);
            if (ExportRos1Legacy || ExportRos2)
            {
                errors.AddRange(ValidateRosMetadataFindings());
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

        public static UsdSimulationProfile CloneUsdSimulation(
            UsdSimulationProfile source)
        {
            UsdSimulationProfile clone = new UsdSimulationProfile();
            if (source == null)
            {
                return clone;
            }
            clone.BaseMode = source.BaseMode;
            clone.RobotType = source.RobotType;
            clone.AllowSelfCollision = source.AllowSelfCollision;
            clone.GainUnits = source.GainUnits;
            clone.JointDrives.Clear();
            foreach (UsdJointDriveProfile drive in
                source.JointDrives ?? new List<UsdJointDriveProfile>())
            {
                if (drive == null)
                {
                    continue;
                }
                clone.JointDrives.Add(new UsdJointDriveProfile
                {
                    Joint = drive.Joint,
                    Mode = drive.Mode,
                    Stiffness = drive.Stiffness,
                    Damping = drive.Damping
                });
            }
            return clone;
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
