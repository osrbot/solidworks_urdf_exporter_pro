using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OSURDF.Core.Model;
using OSURDF.Core.Urdf;
using OSURDF.Core.Validation;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public sealed class TestExportTargetProfiles : IDisposable
    {
        private readonly string temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "sw2urdf-target-profiles-" + Guid.NewGuid().ToString("N"));

        public TestExportTargetProfiles()
        {
            Directory.CreateDirectory(temporaryDirectory);
        }

        [Theory]
        [InlineData("ROS 1", true, false, false, false)]
        [InlineData("ROS 2", false, true, false, false)]
        [InlineData("OpenUSD", false, false, true, false)]
        [InlineData("MuJoCo MJCF", false, false, false, true)]
        public void ForTargetClonesMetadataAndOnlyTheSelectedTargetWithoutMutation(
            string target, bool ros1, bool ros2, bool usd, bool mjcf)
        {
            ExportTargetOptions source = Options();
            source.Ros2ControlProfileFile = Fixture("minimal_ros2_control_profile.json");
            string before = JsonConvert.SerializeObject(source);

            ExportTargetOptions clone = V2ExportBridge.ForTarget(source, target);

            Assert.NotSame(source, clone);
            Assert.Equal(ros1, clone.ExportRos1Legacy);
            Assert.Equal(ros2, clone.ExportRos2);
            Assert.Equal(usd, clone.ExportUsdAsset);
            Assert.Equal(mjcf, clone.ExportMjcfAsset);
            Assert.Equal(source.UseV2Pipeline, clone.UseV2Pipeline);
            Assert.Equal(source.PackageVersion, clone.PackageVersion);
            Assert.Equal(source.Description, clone.Description);
            Assert.Equal(source.MaintainerName, clone.MaintainerName);
            Assert.Equal(source.MaintainerEmail, clone.MaintainerEmail);
            Assert.Equal(source.ModelLicense, clone.ModelLicense);
            Assert.Equal(source.ModelAuthor, clone.ModelAuthor);
            Assert.Equal(source.Ros2Distribution, clone.Ros2Distribution);
            Assert.Equal(source.GazeboDistribution, clone.GazeboDistribution);
            Assert.Equal(ros2 ? source.Ros2ControlProfileFile : string.Empty, clone.Ros2ControlProfileFile);
            Assert.NotSame(source.UsdSimulation, clone.UsdSimulation);
            if (usd)
            {
                Assert.Equal(JsonConvert.SerializeObject(source.UsdSimulation), JsonConvert.SerializeObject(clone.UsdSimulation));
                Assert.NotSame(source.UsdSimulation.JointDrives[0], clone.UsdSimulation.JointDrives[0]);
                clone.UsdSimulation.JointDrives[0].Stiffness = 999;
            }
            else
            {
                Assert.Equal("source", clone.UsdSimulation.BaseMode);
                Assert.Empty(clone.UsdSimulation.JointDrives);
            }
            clone.Description = "changed";
            Assert.Equal(before, JsonConvert.SerializeObject(source));
        }

        [Fact]
        public void ForTargetDoesNotEnableUnselectedTargetsOrAcceptUnknownNames()
        {
            ExportTargetOptions source = Options();
            source.ExportRos2 = false;
            source.Ros2ControlProfileFile = "stale.json";
            ExportTargetOptions clone = V2ExportBridge.ForTarget(source, "ROS 2");
            Assert.False(clone.ExportRos2);
            Assert.Empty(clone.Ros2ControlProfileFile);
            Assert.Throws<ArgumentException>(() => V2ExportBridge.ForTarget(source, "ros2"));
            Assert.Throws<ArgumentNullException>(() => V2ExportBridge.ForTarget(null, "ROS 2"));
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("malformed")]
        [InlineData("empty")]
        [InlineData("unknown-property")]
        [InlineData("duplicate-property")]
        [InlineData("trailing-json")]
        public void InvalidRos2FileFailsOnlyRos2(string kind)
        {
            ExportTargetOptions options = Options();
            options.Ros2ControlProfileFile = Path.Combine(temporaryDirectory, "control.json");
            Dictionary<string, string> contents = new Dictionary<string, string>
            {
                { "malformed", "{broken" }, { "empty", "" },
                { "unknown-property", "{\"unknown\":true}" },
                { "duplicate-property", "{\"enabled\":true,\"enabled\":false}" },
                { "trailing-json", "{} {}" }
            };
            if (kind != "missing") File.WriteAllText(options.Ros2ControlProfileFile, contents[kind]);
            RobotDocument robot = Robot(options);
            string shared = SharedModel(robot);
            string originalOptions = JsonConvert.SerializeObject(options);

            IDictionary<string, string> errors = V2ExportBridge.PrepareTargetProfiles(robot, options);

            Assert.Equal("ROS 2", Assert.Single(errors).Key);
            Assert.True(robot.Profiles.Ros1.Enabled);
            Assert.False(robot.Profiles.Ros2.Enabled);
            Assert.False(robot.Profiles.Ros2.Ros2Control.Enabled);
            Assert.Empty(robot.Profiles.Ros2.Ros2Control.Joints);
            Assert.Equal(JsonConvert.SerializeObject(options.UsdSimulation), JsonConvert.SerializeObject(robot.Profiles.UsdSimulation));
            Assert.Equal(shared, SharedModel(robot));
            Assert.Equal(originalOptions, JsonConvert.SerializeObject(options));
            AssertValid(robot);
        }

        [Fact]
        public void ValidSelectedProfilesArePreservedAndCloned()
        {
            ExportTargetOptions options = Options();
            options.Ros2ControlProfileFile = Fixture("minimal_ros2_control_profile.json");
            RobotDocument robot = Robot(options);
            PackageMetadataProfile package = robot.Profiles.Package;
            string shared = SharedModel(robot);
            robot.Profiles.Isaac.Enabled = true;
            robot.Profiles.IsaacLab.Enabled = true;

            Assert.Empty(V2ExportBridge.PrepareTargetProfiles(robot, options));

            Assert.True(robot.Profiles.Ros1.Enabled);
            Assert.True(robot.Profiles.Ros1.Legacy);
            Assert.True(robot.Profiles.Ros2.Enabled);
            Assert.True(robot.Profiles.Ros2.ModernGazebo);
            Assert.Equal("jazzy", robot.Profiles.Ros2.Distribution);
            Assert.Equal("harmonic", robot.Profiles.Ros2.GazeboDistribution);
            Assert.True(robot.Profiles.Ros2.Ros2Control.Enabled);
            Assert.Equal(JObject.Parse(File.ReadAllText(options.Ros2ControlProfileFile)).ToString(Formatting.None),
                JObject.FromObject(robot.Profiles.Ros2.Ros2Control).ToString(Formatting.None));
            Assert.False(robot.Profiles.Isaac.Enabled);
            Assert.False(robot.Profiles.IsaacLab.Enabled);
            Assert.Same(package, robot.Profiles.Package);
            Assert.NotSame(options.UsdSimulation, robot.Profiles.UsdSimulation);
            Assert.NotSame(options.UsdSimulation.JointDrives[0], robot.Profiles.UsdSimulation.JointDrives[0]);
            Assert.Equal(shared, SharedModel(robot));
            AssertValid(robot);
        }

        [Fact]
        public void SemanticRos2ControlErrorsAreIsolatedAndReset()
        {
            ExportTargetOptions options = Options();
            JObject control = JObject.Parse(File.ReadAllText(Fixture("minimal_ros2_control_profile.json")));
            control["joints"][0]["joint"] = "missing_joint";
            options.Ros2ControlProfileFile = Path.Combine(temporaryDirectory, "invalid-control.json");
            File.WriteAllText(options.Ros2ControlProfileFile, control.ToString());
            RobotDocument robot = Robot(options);

            IDictionary<string, string> errors = V2ExportBridge.PrepareTargetProfiles(robot, options);

            Assert.Equal("ROS 2", Assert.Single(errors).Key);
            Assert.Contains("$.profiles.ros2.ros2Control", errors["ROS 2"]);
            Assert.True(robot.Profiles.Ros1.Enabled);
            Assert.False(robot.Profiles.Ros2.Enabled);
            Assert.Empty(robot.Profiles.Ros2.Ros2Control.Controllers);
            AssertValid(robot);
        }

        [Theory]
        [InlineData("base-mode")]
        [InlineData("gain-units")]
        [InlineData("null-profile")]
        [InlineData("null-drives")]
        [InlineData("null-drive")]
        public void InvalidUsdProfileLeavesRosEnabledAndResetsOnlyUsd(string kind)
        {
            ExportTargetOptions options = Options();
            if (kind == "base-mode") options.UsdSimulation.BaseMode = "broken";
            if (kind == "gain-units") options.UsdSimulation.GainUnits = "degrees";
            if (kind == "null-profile") options.UsdSimulation = null;
            if (kind == "null-drives") options.UsdSimulation.JointDrives = null;
            if (kind == "null-drive") options.UsdSimulation.JointDrives.Add(null);
            RobotDocument robot = Robot(options);
            string before = JsonConvert.SerializeObject(options);

            IDictionary<string, string> errors = V2ExportBridge.PrepareTargetProfiles(robot, options);

            Assert.Equal("OpenUSD", Assert.Single(errors).Key);
            Assert.Contains("$.profiles.usdSimulation", errors["OpenUSD"]);
            Assert.True(robot.Profiles.Ros1.Enabled);
            Assert.True(robot.Profiles.Ros2.Enabled);
            Assert.Equal(JsonConvert.SerializeObject(new UsdSimulationProfile()), JsonConvert.SerializeObject(robot.Profiles.UsdSimulation));
            Assert.Equal(before, JsonConvert.SerializeObject(options));
            AssertValid(robot);
        }

        [Fact]
        public void InvalidJointGeometryAndInertiaRemainCommonValidationErrors()
        {
            ExportTargetOptions options = Options();
            options.UsdSimulation.BaseMode = "broken";
            RobotDocument robot = Robot(options);
            robot.Joints[0].Limit.Upper = robot.Joints[0].Limit.Lower - 1;
            robot.Links[0].Visuals[0].Geometry.Scale.X = -1;
            robot.Links[0].Inertial.Mass = -1;
            string shared = SharedModel(robot);
            string[] originalErrors = new RobotValidator().Validate(robot).Findings
                .Where(finding => finding.Severity == ValidationSeverity.Error)
                .Select(finding => finding.ToString()).ToArray();
            Assert.NotEmpty(originalErrors);

            IDictionary<string, string> errors = V2ExportBridge.PrepareTargetProfiles(robot, options);

            Assert.Equal("OpenUSD", Assert.Single(errors).Key);
            ValidationReport report = new RobotValidator().Validate(robot);
            Assert.False(report.IsValid);
            Assert.Contains(report.Findings, finding => finding.Path.StartsWith("$.joints", StringComparison.Ordinal));
            Assert.Contains(report.Findings, finding => finding.Path.Contains("geometry.scale"));
            Assert.Contains(report.Findings, finding => finding.Path.Contains("inertial.mass"));
            Assert.Equal(originalErrors, report.Findings.Where(finding => finding.Severity == ValidationSeverity.Error)
                .Select(finding => finding.ToString()).ToArray());
            Assert.Equal(shared, SharedModel(robot));
        }

        [Fact]
        public void UnselectedUsdAndRos2IgnoreStaleProfilesAndControlFiles()
        {
            ExportTargetOptions options = Options();
            options.ExportUsdAsset = false;
            options.ExportRos2 = false;
            options.UsdSimulation = null;
            options.Ros2ControlProfileFile = Path.Combine(temporaryDirectory, "missing.json");
            RobotDocument robot = Robot(options);
            robot.Profiles.UsdSimulation = new UsdSimulationProfile { BaseMode = "broken" };
            robot.Profiles.Ros2.Ros2Control.Enabled = true;

            Assert.Empty(V2ExportBridge.PrepareTargetProfiles(robot, options));

            Assert.True(robot.Profiles.Ros1.Enabled);
            Assert.False(robot.Profiles.Ros2.Enabled);
            Assert.False(robot.Profiles.Ros2.Ros2Control.Enabled);
            Assert.Equal("source", robot.Profiles.UsdSimulation.BaseMode);
            AssertValid(robot);
        }

        [Fact]
        public void InvalidRos2DistributionDoesNotDisableOtherTargets()
        {
            ExportTargetOptions options = Options();
            options.GazeboDistribution = "unsupported";
            RobotDocument robot = Robot(options);

            IDictionary<string, string> errors = V2ExportBridge.PrepareTargetProfiles(robot, options);

            Assert.Equal("ROS 2", Assert.Single(errors).Key);
            Assert.Contains("ROS2_GAZEBO_PAIR", errors["ROS 2"]);
            Assert.True(robot.Profiles.Ros1.Enabled);
            Assert.False(robot.Profiles.Ros2.Enabled);
            Assert.Equal("jetty", robot.Profiles.Ros2.GazeboDistribution);
            AssertValid(robot);
        }

        [Fact]
        public void InvalidPackageProfileFailsRosTargetsWithoutChangingPackageOrSharedModel()
        {
            ExportTargetOptions options = Options();
            RobotDocument robot = Robot(options);
            robot.Profiles.Package.PackageName = "not a package name";
            PackageMetadataProfile package = robot.Profiles.Package;
            string shared = SharedModel(robot);

            IDictionary<string, string> errors = V2ExportBridge.PrepareTargetProfiles(robot, options);

            Assert.Equal(new[] { "ROS 1", "ROS 2" }, errors.Keys.OrderBy(key => key).ToArray());
            Assert.False(robot.Profiles.Ros1.Enabled);
            Assert.False(robot.Profiles.Ros2.Enabled);
            Assert.Same(package, robot.Profiles.Package);
            Assert.Equal("not a package name", package.PackageName);
            Assert.Equal(shared, SharedModel(robot));
            AssertValid(robot);
        }

        [Fact]
        public void AllSelectedConfigurationsCanFailWithoutInvalidatingTheSharedModel()
        {
            ExportTargetOptions options = Options();
            options.ExportMjcfAsset = false;
            options.MaintainerEmail = "invalid-email";
            options.Ros2ControlProfileFile = Path.Combine(temporaryDirectory, "missing.json");
            options.UsdSimulation.GainUnits = "degrees";
            RobotDocument robot = Robot(options);
            string shared = SharedModel(robot);

            IDictionary<string, string> errors = V2ExportBridge.PrepareTargetProfiles(robot, options);

            Assert.Equal(new[] { "OpenUSD", "ROS 1", "ROS 2" }, errors.Keys.OrderBy(key => key).ToArray());
            Assert.False(robot.Profiles.Ros1.Enabled);
            Assert.False(robot.Profiles.Ros2.Enabled);
            Assert.False(robot.Profiles.Ros2.Ros2Control.Enabled);
            Assert.Equal("SI", robot.Profiles.UsdSimulation.GainUnits);
            Assert.Equal(shared, SharedModel(robot));
            AssertValid(robot);
        }

        private static ExportTargetOptions Options()
        {
            ExportTargetOptions options = ExportTargetOptions.RecommendedDefaults("minimal_robot");
            options.ExportRos1Legacy = options.ExportRos2 = options.ExportUsdAsset = options.ExportMjcfAsset = true;
            options.ModelAuthor = "Model author";
            options.Ros2Distribution = "jazzy";
            options.GazeboDistribution = "harmonic";
            options.UsdSimulation = new UsdSimulationProfile
            {
                BaseMode = "fixed", RobotType = "default", AllowSelfCollision = true,
                JointDrives = new List<UsdJointDriveProfile>
                {
                    new UsdJointDriveProfile { Joint = "shoulder_joint", Mode = "position", Stiffness = 10, Damping = 1 }
                }
            };
            return options;
        }

        private static RobotDocument Robot(ExportTargetOptions options)
        {
            RobotDocument robot = UrdfCodec.Read(Fixture("minimal_robot.urdf"));
            robot.Profiles.Package = new PackageMetadataProfile
            {
                PackageName = "minimal_robot", Version = options.PackageVersion, Description = options.Description,
                MaintainerName = options.MaintainerName, MaintainerEmail = options.MaintainerEmail, License = options.ModelLicense
            };
            return robot;
        }

        private static string SharedModel(RobotDocument robot)
        {
            JObject model = JObject.FromObject(robot);
            model.Remove("profiles");
            return model.ToString(Formatting.None);
        }

        private static void AssertValid(RobotDocument robot)
        {
            ValidationReport report = new RobotValidator().Validate(robot);
            Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Findings.Select(finding => finding.ToString())));
        }

        private static string Fixture(string name)
        {
            foreach (string start in new[] { AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                for (DirectoryInfo directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    string path = Path.Combine(directory.FullName, "tests", "fixtures", name);
                    if (File.Exists(path)) return path;
                }
            }
            throw new FileNotFoundException("Repository fixture was not found.", name);
        }

        public void Dispose()
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }
    }
}
