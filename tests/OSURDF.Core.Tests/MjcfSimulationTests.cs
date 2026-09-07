using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using OSURDF.Core.Export;
using OSURDF.Core.Model;
using OSURDF.Core.Serialization;
using OSURDF.Core.Validation;
using Xunit;

namespace OSURDF.Core.Tests;

public sealed partial class MjcfAssetExporterTests
{
    [Fact]
    public void SimulationIsOptionalAndRoundTripsWithoutSchemaVersionChange()
    {
        RobotDocument robot = LoadFixtureRobot();
        string legacy = RobotJson.Serialize(robot);
        Assert.Null(JObject.Parse(legacy)["profiles"]!["simulation"]);
        Assert.Null(RobotJson.Deserialize(legacy).Profiles.Simulation);
        JObject nullable = JObject.Parse(legacy);
        nullable["profiles"]!["simulation"] = JValue.CreateNull();
        Assert.Null(RobotJson.Deserialize(nullable.ToString()).Profiles.Simulation);

        robot.Profiles.Simulation = new SimulationProfile();
        Assert.Equal("source", robot.Profiles.Simulation.BaseMode);
        Assert.Empty(robot.Profiles.Simulation.JointDrives);
        Assert.Empty(robot.Profiles.Simulation.Mjcf.JointDrives);
        ConfigureDrive(robot, "position", 12.5, 0.75, 4.25);
        RobotDocument actual = RobotJson.Deserialize(RobotJson.Serialize(robot));
        Assert.Equal(robot.SchemaVersion, actual.SchemaVersion);
        Assert.Equal("position", actual.Profiles.Simulation.JointDrives[0].Mode);
        Assert.Equal(12.5, actual.Profiles.Simulation.Mjcf.JointDrives[0].Stiffness);
        Assert.Equal(0.75, actual.Profiles.Simulation.Mjcf.JointDrives[0].Damping);
        Assert.Equal(4.25, actual.Profiles.Simulation.Mjcf.JointDrives[0].MaxForce);
    }

    [Fact]
    public void SimulationJsonSortsBothDriveListsWithoutMutatingInput()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Profiles.Simulation = new SimulationProfile
        {
            JointDrives = new List<JointDriveIntent> { new() { Joint = "z" }, new() { Joint = "a" } },
            Mjcf = new MjcfSimulationProfile
            {
                JointDrives = new List<MjcfJointDriveProfile> { new() { Joint = "z" }, new() { Joint = "a" } }
            }
        };
        string first = RobotJson.Serialize(robot);
        JObject simulation = (JObject)JObject.Parse(first)["profiles"]!["simulation"]!;
        Assert.Equal(new[] { "a", "z" }, simulation["jointDrives"]!.Select(item => (string?)item["joint"]));
        Assert.Equal(new[] { "a", "z" }, simulation["mjcf"]!["jointDrives"]!.Select(item => (string?)item["joint"]));
        Assert.Equal("z", robot.Profiles.Simulation.JointDrives[0].Joint);
        Assert.Equal("z", robot.Profiles.Simulation.Mjcf.JointDrives[0].Joint);
        Assert.Equal(first, RobotJson.Serialize(RobotJson.Deserialize(first)));
    }

    [Fact]
    public void SimulationJsonRequiresMjcfDriveArrayWhenTargetIsPresent()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Profiles.Simulation = new SimulationProfile();
        JObject json = JObject.Parse(RobotJson.Serialize(robot));
        ((JObject)json["profiles"]!["simulation"]!["mjcf"]!).Remove("jointDrives");
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(json.ToString()));
        Assert.Contains("$.profiles.simulation.mjcf.jointDrives", error.Message);
    }

    [Fact]
    public void NullMjcfSurvivesSerializationAndSkipsOnlyTargetValidation()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Links[0].Visuals.Clear();
        ConfigureDrive(robot, "position");
        robot.Profiles.Simulation.Mjcf = null!;
        RobotDocument actual = RobotJson.Deserialize(RobotJson.Serialize(robot));
        Assert.Null(actual.Profiles.Simulation.Mjcf);
        Assert.True(new RobotValidator().Validate(actual).IsValid);
        Assert.Contains(new RobotValidator().ValidateMjcfSimulation(actual).Findings,
            item => item.Code == "MJCF_PROFILE_REQUIRED" && item.Path == "$.profiles.simulation.mjcf");
        Assert.Throws<InvalidDataException>(() => ExportSimulation(robot));
        robot.Profiles.Simulation.JointDrives[0].Mode = "bad";
        Assert.Contains(new RobotValidator().Validate(robot).Findings, item => item.Code == "SIMULATION_DRIVE_MODE");
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("source", 0)]
    [InlineData("fixed", 0)]
    [InlineData("floating", 1)]
    public void SimulationBaseControlsOnlyRootFreedom(string? mode, int expected)
    {
        RobotDocument robot = LoadFixtureRobot();
        if (mode != null) robot.Profiles.Simulation = new SimulationProfile { BaseMode = mode };
        XDocument xml = XDocument.Load(ExportSimulation(robot).RobotXmlPath);
        Assert.Equal(expected, xml.Descendants("freejoint").Count());
        Assert.Equal(expected, xml.Root!.Element("worldbody")!.Element("body")!.Elements("freejoint").Count());
        Assert.Single(xml.Descendants("joint"));
        Assert.Empty(xml.Descendants("actuator"));
    }

    [Theory]
    [InlineData(null, 4, 0)]
    [InlineData("source", 4, 0)]
    [InlineData("fixed", 0, 0)]
    [InlineData("floating", 0, 1)]
    public void ExplicitBaseReplacesSourceBaseFloatingButPreservesInternalFloating(string? mode, int sourceCount, int freeCount)
    {
        RobotDocument robot = LoadFixtureRobot();
        AddLinkAndJoint(robot, "world", "mount", "world_mount", "fixed");
        robot.Links.Add(new LinkDocument { Name = "world", Id = "world", Source = SourceProvenance.ImportedUrdf() });
        robot.Joints.Add(new JointDocument
        {
            Name = "source_base", Id = "source_base", Parent = "mount", Child = "base_link",
            Type = "floating", Origin = PoseDocument.Zero(), Source = SourceProvenance.ImportedUrdf()
        });
        // The root must carry valid inertia when made free, just like any MuJoCo moving body.
        robot.Links.Single(link => link.Name == "world").Inertial = robot.Links[0].Inertial;
        AddLinkAndJoint(robot, "arm_link", "tool", "internal_float", "floating");
        if (mode != null) robot.Profiles.Simulation = new SimulationProfile { BaseMode = mode };
        MjcfExportResult exported = ExportSimulation(robot);
        XDocument xml = XDocument.Load(exported.RobotXmlPath);
        Assert.Equal(freeCount, xml.Descendants("freejoint").Count());
        Assert.Equal(sourceCount, xml.Descendants("joint").Count(item => ((string)item.Attribute("name")!).StartsWith("source_base_", StringComparison.Ordinal)));
        Assert.Equal(4, xml.Descendants("joint").Count(item => ((string)item.Attribute("name")!).StartsWith("internal_float_", StringComparison.Ordinal)));
        JObject map = JObject.Parse(File.ReadAllText(exported.NameMapPath));
        Assert.Equal(sourceCount, map["joints"]!["source_base"]!.Count());
    }

    [Theory]
    [InlineData("position", "revolute")]
    [InlineData("position", "prismatic")]
    [InlineData("velocity", "continuous")]
    [InlineData("effort", "revolute")]
    public void ActuatorsUseExactSiGainsGearOneAndExplicitForceClamping(string mode, string jointType)
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Joints[0].Type = jointType;
        if (jointType == "continuous")
        {
            robot.Joints[0].Limit.Lower = null;
            robot.Joints[0].Limit.Upper = null;
        }
        ConfigureDrive(robot, mode, mode == "position" ? 12.5 : null, mode != "effort" ? 0.75 : null, 4.25);
        MjcfExportResult exported = ExportSimulation(robot);
        XElement actuator = Assert.Single(XDocument.Load(exported.RobotXmlPath).Root!.Element("actuator")!.Elements());
        Assert.Equal(mode == "effort" ? "motor" : mode, actuator.Name.LocalName);
        Assert.Equal("shoulder_joint", (string?)actuator.Attribute("joint"));
        Assert.Equal("1", (string?)actuator.Attribute("gear"));
        Assert.Equal("true", (string?)actuator.Attribute("forcelimited"));
        Assert.Equal("-4.25 4.25", (string?)actuator.Attribute("forcerange"));
        Assert.Equal(mode == "position" ? "12.5" : null, (string?)actuator.Attribute("kp"));
        Assert.Equal(mode != "effort" ? "0.75" : null, (string?)actuator.Attribute("kv"));
        Assert.Null(actuator.Attribute("ctrlrange"));
        JObject report = JObject.Parse(File.ReadAllText(exported.ExportReportPath));
        Assert.Equal(1, (int?)report["counts"]!["actuators"]);
        Assert.DoesNotContain("actuators", report["intentionallyNotGenerated"]!.Values<string>());
        Assert.DoesNotContain(report["warnings"]!.Values<string>(), item => item!.Contains("not converted into an actuator", StringComparison.Ordinal));
        Assert.DoesNotContain("Actuators, transmissions", report["notGeneratedCapabilities"]!.ToString());
        Assert.DoesNotContain("执行器、传动", report["notGeneratedCapabilities"]!.ToString());
    }

    [Fact]
    public void MotorUsesSourceEffortWhenTargetForceIsOmitted()
    {
        RobotDocument robot = LoadFixtureRobot();
        ConfigureDrive(robot, "effort");
        robot.Profiles.Simulation.Mjcf.JointDrives.Clear();
        robot.Joints[0].Limit.Effort = 7.5;
        XElement motor = Assert.Single(XDocument.Load(ExportSimulation(robot).RobotXmlPath).Descendants("motor"));
        Assert.Equal("-7.5 7.5", (string?)motor.Attribute("forcerange"));
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("revolute")]
    public void PassiveJointsNeverGenerateActuators(string type)
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Joints[0].Type = type;
        if (type == "fixed")
        {
            robot.Joints[0].Axis = null!;
            robot.Joints[0].Limit = null!;
            robot.Joints[0].Dynamics = null!;
        }
        ConfigureDrive(robot, "passive");
        Assert.Empty(XDocument.Load(ExportSimulation(robot).RobotXmlPath).Descendants("actuator"));
    }

    [Theory]
    [InlineData("position", null, 1.0, 1.0)]
    [InlineData("position", 1.0, null, 1.0)]
    [InlineData("position", 0.0, 1.0, 1.0)]
    [InlineData("position", -1.0, 1.0, 1.0)]
    [InlineData("position", double.NaN, 1.0, 1.0)]
    [InlineData("position", 1.0, double.PositiveInfinity, 1.0)]
    [InlineData("velocity", null, null, 1.0)]
    [InlineData("velocity", null, 0.0, 1.0)]
    [InlineData("velocity", null, -1.0, 1.0)]
    [InlineData("velocity", 1.0, 1.0, 1.0)]
    [InlineData("effort", 0.0, null, 1.0)]
    [InlineData("effort", null, 0.0, 1.0)]
    [InlineData("effort", null, null, 0.0)]
    [InlineData("effort", null, null, -1.0)]
    [InlineData("effort", null, null, double.PositiveInfinity)]
    [InlineData("passive", null, null, 1.0)]
    public void InvalidTargetTuningIsScopedOnlyToMjcf(string mode, double? stiffness, double? damping, double? maxForce)
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Links[0].Visuals.Clear();
        ConfigureDrive(robot, mode, stiffness, damping, maxForce);
        ValidationFinding[] errors = new RobotValidator().Validate(robot).Findings.Where(item => item.Severity == ValidationSeverity.Error).ToArray();
        Assert.NotEmpty(errors);
        Assert.All(errors, item => Assert.StartsWith("$.profiles.simulation.mjcf", item.Path));
    }

    [Fact]
    public void MissingSourceEffortCannotInventMotorLimit()
    {
        RobotDocument robot = LoadFixtureRobot();
        ConfigureDrive(robot, "effort");
        robot.Joints[0].Limit.Effort = null;
        Assert.Contains(new RobotValidator().ValidateMjcfSimulation(robot).Findings, item => item.Code == "MJCF_DRIVE_EFFORT_LIMIT");
        robot.Profiles.Simulation.Mjcf.JointDrives[0].MaxForce = 3.0;
        Assert.True(new RobotValidator().ValidateMjcfSimulation(robot).IsValid);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("base")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("null-entry")]
    [InlineData("null-list")]
    [InlineData("fixed")]
    [InlineData("floating")]
    [InlineData("mimic")]
    public void InvalidCommonIntentUsesSharedErrorPath(string invalid)
    {
        RobotDocument robot = LoadFixtureRobot();
        ConfigureDrive(robot, "effort");
        robot.Profiles.Simulation.Mjcf = null!;
        JointDriveIntent intent = robot.Profiles.Simulation.JointDrives[0];
        switch (invalid)
        {
            case "mode": intent.Mode = "automatic"; break;
            case "base": robot.Profiles.Simulation.BaseMode = "anchored"; break;
            case "unknown": intent.Joint = "absent"; break;
            case "duplicate": robot.Profiles.Simulation.JointDrives.Add(intent); break;
            case "null-entry": robot.Profiles.Simulation.JointDrives.Add(null!); break;
            case "null-list": robot.Profiles.Simulation.JointDrives = null!; break;
            case "fixed": robot.Joints[0].Type = "fixed"; break;
            case "floating": robot.Joints[0].Type = "floating"; break;
            case "mimic":
                AddLinkAndJoint(robot, "arm_link", "leader", "leader_joint", "continuous");
                robot.Joints[0].Mimic = new MimicDocument { Joint = "leader_joint" };
                break;
        }
        ValidationFinding[] errors = new RobotValidator().Validate(robot).Findings.Where(item => item.Code.StartsWith("SIMULATION_", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(errors);
        Assert.All(errors, item =>
        {
            Assert.StartsWith("$.profiles.simulation", item.Path);
            Assert.DoesNotContain(".mjcf", item.Path);
        });
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("missing-intent")]
    [InlineData("null-entry")]
    [InlineData("null-list")]
    public void MalformedMjcfTuningUsesTargetErrorPath(string invalid)
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Links[0].Visuals.Clear();
        ConfigureDrive(robot, "effort");
        List<MjcfJointDriveProfile> drives = robot.Profiles.Simulation.Mjcf.JointDrives;
        switch (invalid)
        {
            case "duplicate": drives.Add(drives[0]); break;
            case "unknown": drives[0].Joint = "absent"; break;
            case "missing-intent": robot.Profiles.Simulation.JointDrives.Clear(); break;
            case "null-entry": drives.Add(null!); break;
            case "null-list": robot.Profiles.Simulation.Mjcf.JointDrives = null!; break;
        }
        ValidationReport report = new RobotValidator().Validate(robot);
        Assert.False(report.IsValid);
        Assert.All(report.Findings.Where(item => item.Severity == ValidationSeverity.Error),
            item => Assert.StartsWith("$.profiles.simulation.mjcf", item.Path));
    }

    [Theory]
    [InlineData("baseMode", "3")]
    [InlineData("jointDrives", "{}")]
    [InlineData("mjcf", "[]")]
    public void SimulationJsonRejectsWrongTypes(string property, string value)
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Profiles.Simulation = new SimulationProfile();
        JObject json = JObject.Parse(RobotJson.Serialize(robot));
        json["profiles"]!["simulation"]![property] = JToken.Parse(value);
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(json.ToString()));
    }

    [Theory]
    [InlineData("position")]
    [InlineData("velocity")]
    [InlineData("effort")]
    [Trait("Category", "PinnedMuJoCoRuntime")]
    public void OfficialRuntimeCompilesFreeBaseAndConfiguredActuator(string mode)
    {
        RobotDocument robot = LoadFixtureRobot();
        ConfigureDrive(robot, mode, mode == "position" ? 12.5 : null, mode != "effort" ? 0.75 : null, 4.25);
        robot.Profiles.Simulation.BaseMode = "floating";
        MjcfExportResult result = ExportSimulation(robot, PinnedValidator());
        Assert.Equal("passed", result.OfficialCompilationStatus);
        Assert.Single(XDocument.Load(result.RobotXmlPath).Descendants("freejoint"));
    }

    private static void ConfigureDrive(RobotDocument robot, string mode, double? stiffness = null, double? damping = null, double? maxForce = null)
    {
        robot.Profiles.Simulation = new SimulationProfile
        {
            JointDrives = new List<JointDriveIntent> { new() { Joint = robot.Joints[0].Name, Mode = mode } },
            Mjcf = new MjcfSimulationProfile
            {
                JointDrives = new List<MjcfJointDriveProfile>
                {
                    new() { Joint = robot.Joints[0].Name, Stiffness = stiffness, Damping = damping, MaxForce = maxForce }
                }
            }
        };
    }

    private MjcfExportResult ExportSimulation(RobotDocument robot, IMjcfCompilerValidator? validator = null) =>
        new MjcfAssetExporter().Export(new MjcfExportOptions
        {
            BundleDirectory = BuildBundle(robot, Guid.NewGuid().ToString("N")),
            OutputDirectory = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N")),
            CompilerValidator = validator ?? new RecordingValidator()
        });
}
