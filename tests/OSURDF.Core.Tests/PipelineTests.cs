using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using OSURDF.Core.Bundle;
using OSURDF.Core.Export;
using OSURDF.Core.Model;
using OSURDF.Core.Serialization;
using OSURDF.Core.Urdf;
using OSURDF.Core.Validation;
using Xunit;

namespace OSURDF.Core.Tests;

public sealed class PipelineTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), "osurdf-tests-" + Guid.NewGuid().ToString("N"));

    public PipelineTests()
    {
        Directory.CreateDirectory(temporaryDirectory);
    }

    [Fact]
    public void UrdfRoundTripNormalizesAxisAndPreservesStructure()
    {
        RobotDocument robot = LoadFixtureRobot();
        ValidationReport report = new RobotValidator().Validate(robot);
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Findings));
        Assert.Contains(report.Findings, finding => finding.Code == "JOINT_AXIS_NORMALIZATION");

        string output = Path.Combine(temporaryDirectory, "robot.urdf");
        UrdfCodec.Write(output, robot);
        RobotDocument roundTrip = UrdfCodec.Read(output);
        Assert.Equal(robot.Name, roundTrip.Name);
        Assert.Equal(robot.Links.Select(link => link.Name), roundTrip.Links.Select(link => link.Name));
        Assert.Equal(robot.Joints.Select(joint => joint.Name), roundTrip.Joints.Select(joint => joint.Name));
        Assert.Equal(1.0, roundTrip.Joints[0].Axis!.Z, 12);
    }

    [Fact]
    public void UrdfReaderRejectsDocumentTypeDeclarations()
    {
        string input = Path.Combine(temporaryDirectory, "external-entity.urdf");
        File.WriteAllText(
            input,
            "<!DOCTYPE robot [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]>" +
            "<robot name='unsafe'><link name='&xxe;'/></robot>");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => UrdfCodec.Read(input));
        Assert.IsType<System.Xml.XmlException>(exception.InnerException);
    }

    [Fact]
    public void UrdfReaderRejectsNonFiniteNumericValues()
    {
        foreach (string value in new[] { "NaN", "Infinity", "-Infinity" })
        {
            string input = Path.Combine(temporaryDirectory, "nonfinite-" + value.Replace("-", "negative-") + ".urdf");
            File.WriteAllText(
                input,
                "<robot name='unsafe'><link name='base'><inertial><mass value='" + value +
                "'/><inertia ixx='1' ixy='0' ixz='0' iyy='1' iyz='0' izz='1'/></inertial></link></robot>");
            Assert.Throws<InvalidDataException>(() => UrdfCodec.Read(input));
        }
    }

    [Fact]
    public void UrdfReaderResolvesTopLevelMaterialReferencesWithoutLosingAppearance()
    {
        string input = Path.Combine(temporaryDirectory, "global-material.urdf");
        File.WriteAllText(
            input,
            "<robot name='material_robot'>" +
            "<material name='body'><color rgba='0.1 0.2 0.3 1'/><texture filename='textures/body.png'/></material>" +
            "<link name='base'><visual><geometry><box size='1 1 1'/></geometry><material name='body'/></visual></link>" +
            "</robot>");

        RobotDocument robot = UrdfCodec.Read(input);
        MaterialDocument material = robot.Links.Single().Visuals.Single().Material!;
        Assert.Equal("body", material.Name);
        Assert.Equal(0.2, material.Rgba!.Y, 12);
        Assert.Equal("textures/body.png", material.TextureUri);

        string output = Path.Combine(temporaryDirectory, "global-material-roundtrip.urdf");
        UrdfCodec.Write(output, robot);
        MaterialDocument roundTrip = UrdfCodec.Read(output).Links.Single().Visuals.Single().Material!;
        Assert.Equal(0.3, roundTrip.Rgba!.Z, 12);
        Assert.Equal("textures/body.png", roundTrip.TextureUri);
    }

    [Fact]
    public void ValidatorChecksTheTriangleInequalityInThePrincipalFrame()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Links[0].Inertial!.Inertia = new InertiaTensorDocument
        {
            // I + 2 vv^T for v=(1,1,1)/sqrt(3): diagonal inspection passes,
            // while the true principal moments (1, 1, 3) are not physically realizable.
            Ixx = 5.0 / 3.0,
            Iyy = 5.0 / 3.0,
            Izz = 5.0 / 3.0,
            Ixy = 2.0 / 3.0,
            Ixz = 2.0 / 3.0,
            Iyz = 2.0 / 3.0
        };

        ValidationReport report = new RobotValidator().Validate(robot);

        Assert.Contains(report.Findings, finding => finding.Code == "INERTIA_TRIANGLE");
        Assert.DoesNotContain(report.Findings, finding => finding.Code == "INERTIA_POSITIVE_DEFINITE");
    }

    [Fact]
    public void ValidatorAcceptsARotatedPhysicalInertiaTensor()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Links[0].Inertial!.Inertia = new InertiaTensorDocument
        {
            // A 45-degree rotation of principal moments (1, 2, 2.5).
            Ixx = 1.5,
            Iyy = 1.5,
            Izz = 2.5,
            Ixy = 0.5,
            Ixz = 0.0,
            Iyz = 0.0
        };

        ValidationReport report = new RobotValidator().Validate(robot);

        Assert.DoesNotContain(report.Findings, finding => finding.Code.StartsWith("INERTIA_", StringComparison.Ordinal));
    }

    [Fact]
    public void InertiaTriangleToleranceScalesForSmallRobots()
    {
        RobotDocument robot = LoadFixtureRobot();
        InertiaTensorDocument tensor = robot.Links[0].Inertial!.Inertia;
        tensor.Ixx = 1e-14;
        tensor.Iyy = 1e-14;
        tensor.Izz = 3e-14;
        tensor.Ixy = 0.0;
        tensor.Ixz = 0.0;
        tensor.Iyz = 0.0;

        Assert.Contains(
            new RobotValidator().Validate(robot).Findings,
            finding => finding.Code == "INERTIA_TRIANGLE");

        tensor.Izz = 2e-14;
        Assert.DoesNotContain(
            new RobotValidator().Validate(robot).Findings,
            finding => finding.Code.StartsWith("INERTIA_", StringComparison.Ordinal));
    }

    [Fact]
    public void BundleStandaloneProfilesMatchSparseCanonicalRobotProfiles()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Metadata.ModelLicense = "MIT";
        robot.Profiles.Isaac.Enabled = true;
        robot.Profiles.Isaac.IsaacSimVersion = "6.0.0";
        string output = BuildBundle(robot, "sparse-profile-bundle");
        JObject canonical = JObject.Parse(File.ReadAllText(Path.Combine(output, RobotBundleLayout.RobotJsonFile)));
        foreach ((string file, string property) in new[]
        {
            ("package.json", "package"),
            ("ros1.json", "ros1"),
            ("ros2.json", "ros2"),
            ("isaac.json", "isaac"),
            ("isaaclab.json", "isaacLab")
        })
        {
            JToken standalone = JToken.Parse(File.ReadAllText(Path.Combine(output, "profiles", file)));
            Assert.True(JToken.DeepEquals(canonical["profiles"]![property], standalone));
        }
        Assert.True(new RobotBundleVerifier().Verify(output).IsValid);
    }

    [Fact]
    public void RobotJsonV2RejectsUnknownAndDuplicateProperties()
    {
        const string unknown = "{\"schemaVersion\":2,\"name\":\"robot\",\"units\":\"SI\",\"links\":[],\"joints\":[],\"profiles\":{},\"typo\":true}";
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(unknown));

        const string duplicate = "{\"schemaVersion\":2,\"name\":\"one\",\"name\":\"two\",\"units\":\"SI\",\"links\":[],\"joints\":[],\"profiles\":{}}";
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(duplicate));

        const string wrongVersionType = "{\"schemaVersion\":\"2\",\"name\":\"robot\",\"units\":\"SI\",\"metadata\":{},\"links\":[],\"joints\":[],\"profiles\":{}}";
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(wrongVersionType));

        const string missingMetadata = "{\"schemaVersion\":2,\"name\":\"robot\",\"units\":\"SI\",\"links\":[],\"joints\":[],\"profiles\":{}}";
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(missingMetadata));

        const string trailing = "{\"schemaVersion\":2,\"name\":\"robot\",\"units\":\"SI\",\"metadata\":{},\"links\":[],\"joints\":[],\"profiles\":{}} true";
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(trailing));

        JObject missingNested = JObject.Parse(RobotJson.Serialize(LoadFixtureRobot()));
        ((JObject)missingNested["links"]![0]!).Remove("source");
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(missingNested.ToString()));

        JObject missingProfileField = JObject.Parse(RobotJson.Serialize(LoadFixtureRobot()));
        ((JObject)missingProfileField["profiles"]!["isaac"]!).Remove("mergeMesh");
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(missingProfileField.ToString()));
    }

    [Fact]
    public void RobotJsonV2RejectsImplicitScalarTypeCoercion()
    {
        JObject stringMass = JObject.Parse(RobotJson.Serialize(LoadFixtureRobot()));
        stringMass["links"]![0]!["inertial"]!["mass"] = "1.0";
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(stringMass.ToString()));

        JObject stringBoolean = JObject.Parse(RobotJson.Serialize(LoadFixtureRobot()));
        stringBoolean["profiles"]!["ros2"]!["enabled"] = "true";
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(stringBoolean.ToString()));

        JObject floatInteger = JObject.Parse(RobotJson.Serialize(LoadFixtureRobot()));
        floatInteger["profiles"]!["isaacLab"]!["smokeStepCount"] = 1000.0;
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(floatInteger.ToString()));

        JObject nullListValue = JObject.Parse(RobotJson.Serialize(LoadFixtureRobot()));
        ((JArray)nullListValue["links"]!).Add(JValue.CreateNull());
        Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(nullListValue.ToString()));

        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            JObject nonfinite = JObject.Parse(RobotJson.Serialize(LoadFixtureRobot()));
            nonfinite["links"]![0]!["inertial"]!["mass"] = value;
            Assert.Throws<InvalidDataException>(() => RobotJson.Deserialize(nonfinite.ToString()));
        }

        RobotDocument programmaticNonfinite = LoadFixtureRobot();
        programmaticNonfinite.Links[0].Inertial!.Mass = double.NaN;
        Assert.Throws<InvalidDataException>(() => RobotJson.Serialize(programmaticNonfinite));
    }

    [Fact]
    public void StandaloneProfilesUseTheSameStrictV2JsonContract()
    {
        JObject control = JObject.Parse(File.ReadAllText(Fixture("minimal_ros2_control_profile.json")));
        Assert.Equal(
            "minimal_robot_system",
            RobotJson.DeserializeRos2ControlProfile(control.ToString()).Name);

        control["controllerManagerUpdateRate"] = "100";
        Assert.Throws<InvalidDataException>(() =>
            RobotJson.DeserializeRos2ControlProfile(control.ToString()));

        control = JObject.Parse(File.ReadAllText(Fixture("minimal_ros2_control_profile.json")));
        control.Remove("plugin");
        Assert.Throws<InvalidDataException>(() =>
            RobotJson.DeserializeRos2ControlProfile(control.ToString()));

        JObject lab = JObject.Parse(File.ReadAllText(Fixture("minimal_isaaclab_profile.json")));
        Assert.Equal("2.3.2", RobotJson.DeserializeIsaacLabProfile(lab.ToString()).IsaacLabVersion);
        lab.Remove("physics");
        Assert.Throws<InvalidDataException>(() =>
            RobotJson.DeserializeIsaacLabProfile(lab.ToString()));
    }

    [Fact]
    public void RobotJsonSortsMapKeysForCrossRuntimeReproducibility()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Profiles.Isaac.PackageMappings["zeta"] = "packages/zeta";
        robot.Profiles.Isaac.PackageMappings["alpha"] = "packages/alpha";
        robot.Profiles.IsaacLab.JointPositions["z_joint"] = 0.0;
        robot.Profiles.IsaacLab.JointPositions["a_joint"] = 0.0;

        JObject serialized = JObject.Parse(RobotJson.Serialize(robot));
        JObject mappings = (JObject)serialized["profiles"]!["isaac"]!["packageMappings"]!;
        JObject positions = (JObject)serialized["profiles"]!["isaacLab"]!["jointPositions"]!;
        Assert.Equal(new[] { "alpha", "zeta" }, mappings.Properties().Select(property => property.Name));
        Assert.Equal(new[] { "a_joint", "z_joint" }, positions.Properties().Select(property => property.Name));
    }

    [Fact]
    public void UsdSimulationSettingsAreConservativeAndRoundTripWithoutVersionPins()
    {
        RobotDocument robot = LoadFixtureRobot();
        UsdSimulationProfile defaults = robot.Profiles.UsdSimulation;

        Assert.Equal("source", defaults.BaseMode);
        Assert.Equal("default", defaults.RobotType);
        Assert.False(defaults.AllowSelfCollision);
        Assert.Empty(defaults.JointDrives);

        defaults.BaseMode = "fixed";
        defaults.RobotType = "wheeled";
        defaults.AllowSelfCollision = true;
        defaults.JointDrives.Add(new UsdJointDriveProfile
        {
            Joint = "shoulder_joint",
            Mode = "position",
            Stiffness = 120.0,
            Damping = 8.0
        });

        RobotDocument roundTrip = RobotJson.Deserialize(RobotJson.Serialize(robot));
        UsdSimulationProfile actual = roundTrip.Profiles.UsdSimulation;
        Assert.Equal("fixed", actual.BaseMode);
        Assert.Equal("wheeled", actual.RobotType);
        Assert.True(actual.AllowSelfCollision);
        UsdJointDriveProfile drive = Assert.Single(actual.JointDrives);
        Assert.Equal("shoulder_joint", drive.Joint);
        Assert.Equal("position", drive.Mode);
        Assert.Equal(120.0, drive.Stiffness);
        Assert.Equal(8.0, drive.Damping);
        JObject serialized = JObject.Parse(RobotJson.Serialize(robot));
        JObject usdSimulation = Assert.IsType<JObject>(serialized["profiles"]?["usdSimulation"]);
        Assert.Null(usdSimulation["isaacSimVersion"]);
        Assert.Null(usdSimulation["isaacLabVersion"]);
    }

    [Fact]
    public void ValidatorRejectsInvalidUsdSimulationIntentWithoutRequiringDriveCoverage()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Profiles.UsdSimulation.BaseMode = "anchored_by_guess";
        robot.Profiles.UsdSimulation.RobotType = "marketing_category";
        robot.Profiles.UsdSimulation.JointDrives.Add(new UsdJointDriveProfile
        {
            Joint = "missing_joint",
            Mode = "position",
            Stiffness = -1.0,
            Damping = double.NaN
        });
        robot.Profiles.UsdSimulation.JointDrives.Add(new UsdJointDriveProfile
        {
            Joint = "shoulder_joint",
            Mode = "effort",
            Stiffness = 10.0
        });

        ValidationReport report = new RobotValidator().Validate(robot);
        Assert.Contains(report.Findings, finding => finding.Code == "USD_BASE_MODE");
        Assert.Contains(report.Findings, finding => finding.Code == "USD_ROBOT_TYPE");
        Assert.Contains(report.Findings, finding => finding.Code == "USD_DRIVE_JOINT");
        Assert.Contains(report.Findings, finding => finding.Code == "USD_DRIVE_STIFFNESS");
        Assert.Contains(report.Findings, finding => finding.Code == "USD_DRIVE_DAMPING");
        Assert.Contains(report.Findings, finding => finding.Code == "USD_DRIVE_GAIN_MODE");

        robot.Profiles.UsdSimulation = new UsdSimulationProfile();
        Assert.DoesNotContain(
            new RobotValidator().Validate(robot).Findings,
            finding => finding.Code.StartsWith("USD_", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRequiresExplicitConfirmedJointConfiguration()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Joints[0].Type = string.Empty;
        ValidationReport blank = new RobotValidator().Validate(robot);
        Assert.Contains(blank.Findings, finding => finding.Code == "JOINT_TYPE");

        robot.Joints[0].Type = "revolute";
        robot.Joints[0].Source = new SourceProvenance
        {
            Kind = "solidworks_mate_suggestion",
            Evidence = "concentric mate",
            UserConfirmed = false
        };
        ValidationReport suggestion = new RobotValidator().Validate(robot);
        Assert.Contains(suggestion.Findings, finding => finding.Code == "MATE_UNCONFIRMED");
        robot.Joints[0].Source.UserConfirmed = true;
        Assert.DoesNotContain(new RobotValidator().Validate(robot).Findings, finding => finding.Code == "MATE_UNCONFIRMED");

        robot.Joints[0].Source = new SourceProvenance
        {
            Kind = "legacy_configuration",
            Evidence = "migrated from a pre-v2 configuration",
            UserConfirmed = false
        };
        Assert.Contains(
            new RobotValidator().Validate(robot).Findings,
            finding => finding.Code == "JOINT_SOURCE_UNCONFIRMED");

        robot.Links[0].Source = SourceProvenance.Unknown();
        robot.Joints[0].Source = SourceProvenance.Unknown();
        ValidationReport unknownSources = new RobotValidator().Validate(robot);
        Assert.Contains(unknownSources.Findings, finding => finding.Code == "LINK_SOURCE");
        Assert.Contains(unknownSources.Findings, finding => finding.Code == "JOINT_SOURCE");
    }

    [Fact]
    public void MigratorAddsStableIdsProfilesAndSources()
    {
        const string legacy = "{\"schemaVersion\":1,\"robotName\":\"legacy\",\"links\":[{\"name\":\"base\"}],\"joints\":[]}";
        JObject migrated = RobotSchemaMigrator.Migrate(JObject.Parse(legacy));
        Assert.Equal(2, (int)migrated["schemaVersion"]!);
        Assert.Equal("legacy", (string)migrated["name"]!);
        Assert.NotNull(migrated["profiles"]);
        Assert.StartsWith("link-", (string)migrated["links"]![0]!["id"]!);
        Assert.Equal("migrated_config", (string)migrated["links"]![0]!["source"]!["kind"]!);

        const string malformed = "{\"schemaVersion\":1,\"robotName\":\"legacy\",\"links\":[null],\"joints\":[]}";
        Assert.Throws<InvalidDataException>(() => RobotSchemaMigrator.Migrate(JObject.Parse(malformed)));
    }

    [Fact]
    public void BundleBuildsVerifiesAndDetectsTampering()
    {
        RobotDocument robot = LoadFixtureRobot();
        string output = Path.Combine(temporaryDirectory, "bundle");
        string? previousEpoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        try
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "1700000000");
            BundleBuildResult built = new RobotBundleBuilder().Build(robot, new BundleBuildOptions
            {
                SourceUrdfPath = Fixture("minimal_robot.urdf"),
                OutputDirectory = output
            });
            Assert.True(built.Manifest.ReproducibleTimestamp);
            Assert.Equal("meshes/visual/base.stl", RobotJson.Read(Path.Combine(output, "robot.json")).Links[0].Visuals[0].Geometry.Uri);
            Assert.True(new RobotBundleVerifier().Verify(output).IsValid);

            File.AppendAllText(Path.Combine(output, "meshes", "visual", "base.stl"), "tamper");
            BundleVerificationResult tampered = new RobotBundleVerifier().Verify(output);
            Assert.False(tampered.IsValid);
            Assert.Contains(tampered.Errors, error => error.Contains("Checksum mismatch", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", previousEpoch);
        }
    }

    [Fact]
    public void BundleRejectsPackageTraversal()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Links[0].Visuals[0].Geometry.Uri = "package://fixture/../secret.stl";
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => new RobotBundleBuilder().Build(robot, new BundleBuildOptions
        {
            SourceUrdfPath = Fixture("minimal_robot.urdf"),
            OutputDirectory = Path.Combine(temporaryDirectory, "bundle"),
            PackageMappings = new Dictionary<string, string> { ["fixture"] = Path.GetDirectoryName(Fixture("minimal_robot.urdf"))! }
        }));
        Assert.Contains("traverses", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BundleRejectsAssetsReachedThroughSymbolicLinkDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string external = Path.Combine(temporaryDirectory, "external-assets");
        string packageRoot = Path.Combine(temporaryDirectory, "fixture-package");
        Directory.CreateDirectory(external);
        Directory.CreateDirectory(packageRoot);
        File.WriteAllText(Path.Combine(external, "secret.stl"), "solid secret\nendsolid secret\n");
        Directory.CreateSymbolicLink(Path.Combine(packageRoot, "linked"), external);

        RobotDocument robot = LoadFixtureRobot();
        robot.Links[0].Visuals[0].Geometry.Uri = "package://fixture/linked/secret.stl";
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new RobotBundleBuilder().Build(robot, new BundleBuildOptions
            {
                SourceUrdfPath = Fixture("minimal_robot.urdf"),
                OutputDirectory = Path.Combine(temporaryDirectory, "symlink-source-bundle"),
                PackageMappings = new Dictionary<string, string> { ["fixture"] = packageRoot }
            }));
        Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BundleOverwriteRejectsSymbolicLinksInTheExistingOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        RobotDocument robot = LoadFixtureRobot();
        string output = BuildBundle(robot, "bundle-overwrite-symlink");
        string external = Path.Combine(temporaryDirectory, "bundle-overwrite-external.txt");
        File.WriteAllText(external, "must remain unchanged\n");
        File.CreateSymbolicLink(Path.Combine(output, "external-link"), external);

        IOException exception = Assert.Throws<IOException>(() =>
            new RobotBundleBuilder().Build(robot, new BundleBuildOptions
            {
                SourceUrdfPath = Fixture("minimal_robot.urdf"),
                OutputDirectory = output,
                Overwrite = true
            }));
        Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("must remain unchanged\n", File.ReadAllText(external));
    }

    [Fact]
    public void BundleRejectsRelativeTraversalUnlessLocalPathOptInIsExplicit()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Links[0].Visuals[0].Geometry.Uri = "../fixtures/meshes/base.stl";
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new RobotBundleBuilder().Build(robot, new BundleBuildOptions
            {
                SourceUrdfPath = Fixture("minimal_robot.urdf"),
                OutputDirectory = Path.Combine(temporaryDirectory, "bundle")
            }));
        Assert.Contains("traverses", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BundleRejectsOutputDirectoryContainingTheSourceUrdf()
    {
        string output = Path.Combine(temporaryDirectory, "source-containing-output");
        Directory.CreateDirectory(output);
        string source = Path.Combine(output, "source.urdf");
        File.Copy(Fixture("minimal_robot.urdf"), source);
        string before = File.ReadAllText(source);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new RobotBundleBuilder().Build(LoadFixtureRobot(), new BundleBuildOptions
            {
                SourceUrdfPath = source,
                OutputDirectory = output,
                Overwrite = true
            }));

        Assert.Contains("must not contain", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllText(source));
    }

    [Fact]
    public void BundleRejectsOutputDirectoryContainingAReferencedAsset()
    {
        string output = Path.Combine(temporaryDirectory, "asset-containing-output");
        Directory.CreateDirectory(output);
        string asset = Path.Combine(output, "source-mesh.stl");
        File.Copy(Fixture(Path.Combine("meshes", "base.stl")), asset);
        byte[] before = File.ReadAllBytes(asset);
        RobotDocument robot = LoadFixtureRobot();
        robot.Links[0].Visuals[0].Geometry.Uri = asset;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new RobotBundleBuilder().Build(robot, new BundleBuildOptions
            {
                SourceUrdfPath = Fixture("minimal_robot.urdf"),
                OutputDirectory = output,
                Overwrite = true,
                AllowAbsoluteAssetPaths = true
            }));

        Assert.Contains("must not contain", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(asset));
    }

    [Fact]
    public void BundleRejectsOutputDirectoryContainingAnAdditionalInput()
    {
        string output = Path.Combine(temporaryDirectory, "report-containing-output");
        Directory.CreateDirectory(output);
        string report = Path.Combine(output, "source-report.csv");
        File.WriteAllText(report, "link,status\nbase_link,PASS\n");
        string before = File.ReadAllText(report);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new RobotBundleBuilder().Build(LoadFixtureRobot(), new BundleBuildOptions
            {
                SourceUrdfPath = Fixture("minimal_robot.urdf"),
                OutputDirectory = output,
                Overwrite = true,
                AdditionalFiles =
                {
                    new BundleAdditionalFile
                    {
                        SourcePath = report,
                        BundlePath = "reports/cad/source-report.csv"
                    }
                }
            }));

        Assert.Contains("must not contain", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllText(report));
    }

    [Fact]
    public void BundleIncludesAndVerifiesSupplementalCadReports()
    {
        RobotDocument robot = LoadFixtureRobot();
        string report = Path.Combine(temporaryDirectory, "inertial_validation.csv");
        File.WriteAllText(report, "link,status\nbase_link,PASS\n");
        string output = new RobotBundleBuilder().Build(robot, new BundleBuildOptions
        {
            SourceUrdfPath = Fixture("minimal_robot.urdf"),
            OutputDirectory = Path.Combine(temporaryDirectory, "bundle"),
            AdditionalFiles =
            {
                new BundleAdditionalFile
                {
                    SourcePath = report,
                    BundlePath = "reports/cad/inertial_validation.csv",
                    Role = "cad-validation-report"
                }
            }
        }).OutputDirectory;

        Assert.True(File.Exists(Path.Combine(output, "reports", "cad", "inertial_validation.csv")));
        Assert.True(new RobotBundleVerifier().Verify(output).IsValid);
    }

    [Fact]
    public void BundleRejectsSupplementalFilesThatCollideWithCanonicalPayload()
    {
        RobotDocument robot = LoadFixtureRobot();
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new RobotBundleBuilder().Build(robot, new BundleBuildOptions
            {
                SourceUrdfPath = Fixture("minimal_robot.urdf"),
                OutputDirectory = Path.Combine(temporaryDirectory, "bundle"),
                AdditionalFiles =
                {
                    new BundleAdditionalFile
                    {
                        SourcePath = Fixture("minimal_robot.urdf"),
                        BundlePath = "robot.json"
                    }
                }
            }));
        Assert.Contains("reserved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BundleRejectsWindowsReservedPayloadNamesOnEveryHost()
    {
        RobotDocument robot = LoadFixtureRobot();
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new RobotBundleBuilder().Build(robot, new BundleBuildOptions
            {
                SourceUrdfPath = Fixture("minimal_robot.urdf"),
                OutputDirectory = Path.Combine(temporaryDirectory, "bundle"),
                AdditionalFiles =
                {
                    new BundleAdditionalFile
                    {
                        SourcePath = Fixture("minimal_robot.urdf"),
                        BundlePath = "reports/CON.txt"
                    }
                }
            }));
        Assert.Contains("non-portable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BundleVerifierRequiresManifestToInventoryEveryPayload()
    {
        string bundle = BuildBundle(LoadFixtureRobot(), "manifest-bundle");
        string manifestPath = Path.Combine(bundle, "manifest.json");
        JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
        ((JArray)manifest["files"]!).RemoveAt(0);
        File.WriteAllText(manifestPath, manifest.ToString());

        BundleVerificationResult result = new RobotBundleVerifier().Verify(bundle);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("inventory mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BundleVerifierRejectsDuplicateAndUnknownManifestFields()
    {
        string duplicateBundle = BuildBundle(LoadFixtureRobot(), "duplicate-manifest");
        string duplicatePath = Path.Combine(duplicateBundle, "manifest.json");
        string duplicateJson = File.ReadAllText(duplicatePath);
        duplicateJson = duplicateJson.Replace(
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1,\n  \"schemaVersion\": 1,",
            StringComparison.Ordinal);
        File.WriteAllText(duplicatePath, duplicateJson);
        BundleVerificationResult duplicate = new RobotBundleVerifier().Verify(duplicateBundle);
        Assert.False(duplicate.IsValid);
        Assert.Contains(duplicate.Errors, error => error.Contains("Manifest JSON is invalid", StringComparison.Ordinal));

        string unknownBundle = BuildBundle(LoadFixtureRobot(), "unknown-manifest");
        string unknownPath = Path.Combine(unknownBundle, "manifest.json");
        JObject unknownManifest = JObject.Parse(File.ReadAllText(unknownPath));
        unknownManifest["unexpected"] = true;
        File.WriteAllText(unknownPath, unknownManifest.ToString());
        BundleVerificationResult unknown = new RobotBundleVerifier().Verify(unknownBundle);
        Assert.False(unknown.IsValid);
        Assert.Contains(unknown.Errors, error => error.Contains("Manifest JSON is invalid", StringComparison.Ordinal));
    }

    [Fact]
    public void BundleManifestOmitsNullProvenanceAndRejectsWrongPrimitiveTypes()
    {
        string bundle = BuildBundle(LoadFixtureRobot(), "manifest-types");
        string manifestPath = Path.Combine(bundle, "manifest.json");
        JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
        JObject generated = ((JArray)manifest["files"]!)
            .OfType<JObject>()
            .Single(item => string.Equals((string?)item["path"], "robot.json", StringComparison.Ordinal));
        Assert.Null(generated.Property("sourceUri"));

        manifest["schemaVersion"] = "1";
        File.WriteAllText(manifestPath, manifest.ToString());
        BundleVerificationResult result = new RobotBundleVerifier().Verify(bundle);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Manifest JSON is invalid", StringComparison.Ordinal));
    }

    [Fact]
    public void BundleVerifierRejectsARewrittenValidationReportEvenWithFreshChecksums()
    {
        string bundle = BuildBundle(LoadFixtureRobot(), "validation-divergence");
        string relative = "reports/validation.json";
        string validationPath = Path.Combine(bundle, "reports", "validation.json");
        JObject validation = JObject.Parse(File.ReadAllText(validationPath));
        validation["warnings"] = (int)validation["warnings"]! + 1;
        File.WriteAllText(validationPath, validation.ToString());
        RefreshManifestEntryAndChecksums(bundle, relative);

        BundleVerificationResult result = new RobotBundleVerifier().Verify(bundle);
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("does not exactly match", StringComparison.Ordinal));
    }

    [Fact]
    public void BundleVerifierDoesNotFollowSymbolicLinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string bundle = BuildBundle(LoadFixtureRobot(), "symlink-verifier");
        File.CreateSymbolicLink(Path.Combine(bundle, "outside-link"), Fixture("minimal_robot.urdf"));
        BundleVerificationResult result = new RobotBundleVerifier().Verify(bundle);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Symbolic links", StringComparison.Ordinal));
    }

    [Fact]
    public void BundleVerifierRejectsSelfConsistentButDivergentProfileAndUrdfEntrypoints()
    {
        string profileBundle = BuildBundle(LoadFixtureRobot(), "profile-divergence");
        string isaacRelative = "profiles/isaac.json";
        string isaacPath = Path.Combine(profileBundle, "profiles", "isaac.json");
        JObject isaac = JObject.Parse(File.ReadAllText(isaacPath));
        isaac["robotType"] = "Humanoid";
        File.WriteAllText(isaacPath, isaac.ToString());
        RefreshManifestEntryAndChecksums(profileBundle, isaacRelative);
        BundleVerificationResult profileResult = new RobotBundleVerifier().Verify(profileBundle);
        Assert.Contains(
            profileResult.Errors,
            error => error.Contains("does not match the corresponding robot.json profile", StringComparison.Ordinal));

        string urdfBundle = BuildBundle(LoadFixtureRobot(), "urdf-divergence");
        string urdfPath = Path.Combine(urdfBundle, "robot.urdf");
        XDocument urdf = XDocument.Load(urdfPath);
        urdf.Descendants("axis").First().SetAttributeValue("xyz", "1 0 0");
        urdf.Save(urdfPath);
        RefreshManifestEntryAndChecksums(urdfBundle, "robot.urdf");
        BundleVerificationResult urdfResult = new RobotBundleVerifier().Verify(urdfBundle);
        Assert.Contains(
            urdfResult.Errors,
            error => error.Contains("canonical model data do not match", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorReportsNullEntriesWithoutThrowing()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Links.Add(null!);
        robot.Joints.Add(null!);
        ValidationReport report = new RobotValidator().Validate(robot);
        Assert.Contains(report.Findings, finding => finding.Code == "LINK_NULL");
        Assert.Contains(report.Findings, finding => finding.Code == "JOINT_NULL");
    }

    [Fact]
    public void Ros2ExportUsesModernGazeboAndExplicitControlConfiguration()
    {
        RobotDocument robot = LoadFixtureRobot();
        ConfigurePackage(robot);
        robot.Profiles.Ros2.Enabled = true;
        robot.Profiles.Ros2.Ros2Control = new Ros2ControlProfile
        {
            Enabled = true,
            Plugin = "gz_ros2_control/GazeboSimSystem",
            GazeboPluginEnabled = true,
            Joints =
            {
                new Ros2ControlJointProfile
                {
                    Joint = "shoulder_joint",
                    CommandInterfaces = { "position" },
                    StateInterfaces = { "position", "velocity" }
                }
            },
            Controllers =
            {
                new Ros2ControllerProfile
                {
                    Name = "arm_controller",
                    Type = "forward_command_controller/ForwardCommandController",
                    Joints = { "shoulder_joint" },
                    CommandInterfaces = { "position" },
                    StateInterfaces = { "position", "velocity" }
                }
            }
        };
        string bundle = BuildBundle(robot, "ros2-bundle");
        string package = new RosPackageExporter().ExportRos2(new RosExportOptions
        {
            BundleDirectory = bundle,
            OutputDirectory = Path.Combine(temporaryDirectory, "ros2")
        });
        string launch = File.ReadAllText(Path.Combine(package, "launch", "gazebo.launch.py"));
        string display = File.ReadAllText(Path.Combine(package, "launch", "display.launch.py"));
        string controlUrdf = File.ReadAllText(Path.Combine(package, "urdf", "minimal_robot.ros2_control.urdf"));
        Assert.Contains("ros_gz_sim", launch);
        Assert.DoesNotContain("gazebo_ros", launch);
        Assert.Contains("from xacro import process_file", launch);
        Assert.Contains("'-topic', 'robot_description'", launch);
        Assert.DoesNotContain("'-file'", launch);
        Assert.Contains("from xacro import process_file", display);
        Assert.Contains("<exec_depend>xacro</exec_depend>", File.ReadAllText(Path.Combine(package, "package.xml")));
        Assert.Contains("<ros2_control", controlUrdf);
        Assert.Contains("gz_ros2_control/GazeboSimSystem", controlUrdf);
        Assert.Contains("libgz_ros2_control-system.so", controlUrdf);
        Assert.Contains("gz_ros2_control::GazeboSimROS2ControlPlugin", controlUrdf);
        string controllerYaml = File.ReadAllText(Path.Combine(package, "config", "controllers.yaml"));
        Assert.Contains("interface_name: 'position'", controllerYaml);
        Assert.DoesNotContain("command_interfaces:", controllerYaml);

        string reportPath = Path.Combine(package, "config", "export_report.md");
        File.WriteAllText(reportPath, "# generated after package publication\n");
        RosPackageExporter.RefreshChecksums(package);
        Assert.Contains("config/export_report.md", File.ReadAllText(Path.Combine(package, "checksums.sha256")));
    }

    [Fact]
    public void RosExportRejectsOutputsInsideTheSourceBundle()
    {
        RobotDocument robot = LoadFixtureRobot();
        ConfigurePackage(robot);
        robot.Profiles.Ros2.Enabled = true;
        string bundle = BuildBundle(robot, "ros-overlap");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new RosPackageExporter().ExportRos2(new RosExportOptions
            {
                BundleDirectory = bundle,
                OutputDirectory = bundle
            }));
        Assert.Contains("must not contain", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RosChecksumRefreshRejectsSymbolicLinkPayloads()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        RobotDocument robot = LoadFixtureRobot();
        ConfigurePackage(robot);
        robot.Profiles.Ros2.Enabled = true;
        string bundle = BuildBundle(robot, "ros-checksum-symlink");
        string package = new RosPackageExporter().ExportRos2(new RosExportOptions
        {
            BundleDirectory = bundle,
            OutputDirectory = Path.Combine(temporaryDirectory, "ros-checksum-output")
        });
        string external = Path.Combine(temporaryDirectory, "external-report");
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "outside.txt"), "outside\n");
        Directory.CreateSymbolicLink(Path.Combine(package, "linked-report"), external);

        IOException exception = Assert.Throws<IOException>(() =>
            RosPackageExporter.RefreshChecksums(package));
        Assert.Contains("symbolic links", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RosOverwriteRejectsSymbolicLinksInTheExistingPackage()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        RobotDocument robot = LoadFixtureRobot();
        ConfigurePackage(robot);
        robot.Profiles.Ros2.Enabled = true;
        string bundle = BuildBundle(robot, "ros-overwrite-symlink-bundle");
        string output = Path.Combine(temporaryDirectory, "ros-overwrite-output");
        RosPackageExporter exporter = new RosPackageExporter();
        string package = exporter.ExportRos2(new RosExportOptions
        {
            BundleDirectory = bundle,
            OutputDirectory = output
        });
        string external = Path.Combine(temporaryDirectory, "ros-overwrite-external.txt");
        File.WriteAllText(external, "must remain unchanged\n");
        File.CreateSymbolicLink(Path.Combine(package, "external-link"), external);

        IOException exception = Assert.Throws<IOException>(() =>
            exporter.ExportRos2(new RosExportOptions
            {
                BundleDirectory = bundle,
                OutputDirectory = output,
                Overwrite = true
            }));
        Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("must remain unchanged\n", File.ReadAllText(external));
    }

    [Fact]
    public void IsaacLabRequiresExactVersionAndActuatorCoverage()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Metadata.ModelLicense = "Apache-2.0";
        robot.Profiles.Isaac.Enabled = true;
        robot.Profiles.Isaac.IsaacSimVersion = "6.0.0";
        robot.Profiles.IsaacLab.Enabled = true;
        robot.Profiles.IsaacLab.IsaacLabVersion = "2.3.0";
        ValidationReport missing = new RobotValidator().Validate(robot);
        Assert.Contains(missing.Findings, finding => finding.Code == "ACTUATOR_COVERAGE");
        robot.Profiles.IsaacLab.ActuatorGroups.Add(new ActuatorGroupProfile
        {
            Name = "shoulder",
            ControlMode = "position",
            Joints = { "shoulder_joint" },
            Stiffness = 100,
            Damping = 5,
            EffortLimit = 20,
            VelocityLimit = 3
        });
        Assert.True(new RobotValidator().Validate(robot).IsValid);

        robot.Profiles.IsaacLab.JointPositions["missing_joint"] = 0.0;
        robot.Profiles.IsaacLab.JointPositions["shoulder_joint"] = 2.0;
        ValidationReport initialState = new RobotValidator().Validate(robot);
        Assert.Contains(initialState.Findings, finding => finding.Code == "INITIAL_JOINT");
        Assert.Contains(initialState.Findings, finding => finding.Code == "INITIAL_JOINT_LIMIT");
    }

    [Fact]
    public void IsaacProfilesRejectImplicitDependenciesAndHostPaths()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Metadata.ModelLicense = "Apache-2.0";
        robot.Profiles.IsaacLab.Enabled = true;
        robot.Profiles.IsaacLab.IsaacLabVersion = "2.3.2";
        ValidationReport dependency = new RobotValidator().Validate(robot);
        Assert.Contains(dependency.Findings, finding => finding.Code == "ISAACLAB_REQUIRES_ISAAC");

        robot.Profiles.Isaac.Enabled = true;
        robot.Profiles.Isaac.IsaacSimVersion = "6.0.0";
        robot.Profiles.Isaac.PackageMappings["local"] = Path.GetFullPath(temporaryDirectory);
        ValidationReport hostPath = new RobotValidator().Validate(robot);
        Assert.Contains(hostPath.Findings, finding => finding.Code == "ISAAC_PACKAGE_MAPPING");

        robot.Profiles.Isaac.PackageMappings.Clear();
        robot.Profiles.Isaac.CollisionType = "guessed_shape";
        ValidationReport collision = new RobotValidator().Validate(robot);
        Assert.Contains(collision.Findings, finding => finding.Code == "ISAAC_COLLISION_TYPE");
    }

    [Fact]
    public void Ros2ControlRejectsNonPortableNamesAndUnknownHardwareType()
    {
        RobotDocument robot = LoadFixtureRobot();
        ConfigurePackage(robot);
        robot.Profiles.Ros2.Enabled = true;
        robot.Profiles.Ros2.Ros2Control = new Ros2ControlProfile
        {
            Enabled = true,
            Name = "bad control name",
            Type = "guess",
            Plugin = "gz_ros2_control/GazeboSimSystem",
            Joints =
            {
                new Ros2ControlJointProfile
                {
                    Joint = "shoulder_joint",
                    CommandInterfaces = { "position" },
                    StateInterfaces = { "position" }
                }
            },
            Controllers =
            {
                new Ros2ControllerProfile
                {
                    Name = "joint_state_broadcaster",
                    Type = "forward_command_controller/ForwardCommandController",
                    Joints = { "shoulder_joint" },
                    CommandInterfaces = { "position" }
                }
            }
        };
        ValidationReport report = new RobotValidator().Validate(robot);
        Assert.Contains(report.Findings, finding => finding.Code == "ROS2_CONTROL_NAME");
        Assert.Contains(report.Findings, finding => finding.Code == "ROS2_CONTROL_TYPE");
        Assert.Contains(report.Findings, finding => finding.Code == "ROS2_CONTROLLER_NAME");
    }

    [Fact]
    public void Ros2ControlRejectsControllerJointsOutsideHardwareConfiguration()
    {
        RobotDocument robot = LoadFixtureRobot();
        ConfigurePackage(robot);
        robot.Profiles.Ros2.Enabled = true;
        robot.Profiles.Ros2.Ros2Control = new Ros2ControlProfile
        {
            Enabled = true,
            Plugin = "gz_ros2_control/GazeboSimSystem",
            Controllers =
            {
                new Ros2ControllerProfile
                {
                    Name = "arm_controller",
                    Type = "forward_command_controller/ForwardCommandController",
                    Joints = { "shoulder_joint" }
                }
            }
        };
        ValidationReport report = new RobotValidator().Validate(robot);
        Assert.Contains(report.Findings, finding => finding.Code == "ROS2_CONTROLLER_UNCONFIGURED_JOINT");
    }

    [Fact]
    public void Ros2ControlRejectsControllerInterfacesOutsideHardwareConfiguration()
    {
        RobotDocument robot = LoadFixtureRobot();
        ConfigurePackage(robot);
        robot.Profiles.Ros2.Enabled = true;
        robot.Profiles.Ros2.Ros2Control = new Ros2ControlProfile
        {
            Enabled = true,
            Plugin = "gz_ros2_control/GazeboSimSystem",
            Joints =
            {
                new Ros2ControlJointProfile
                {
                    Joint = "shoulder_joint",
                    CommandInterfaces = { "position" },
                    StateInterfaces = { "position" }
                }
            },
            Controllers =
            {
                new Ros2ControllerProfile
                {
                    Name = "arm_controller",
                    Type = "forward_command_controller/ForwardCommandController",
                    Joints = { "shoulder_joint" },
                    CommandInterfaces = { "velocity" },
                    StateInterfaces = { "position", "velocity" }
                }
            }
        };

        ValidationReport report = new RobotValidator().Validate(robot);
        Assert.Contains(report.Findings, finding => finding.Code == "ROS2_CONTROLLER_INTERFACE_MISMATCH");
    }

    private string BuildBundle(RobotDocument robot, string name)
    {
        return new RobotBundleBuilder().Build(robot, new BundleBuildOptions
        {
            SourceUrdfPath = Fixture("minimal_robot.urdf"),
            OutputDirectory = Path.Combine(temporaryDirectory, name)
        }).OutputDirectory;
    }

    private static void ConfigurePackage(RobotDocument robot)
    {
        robot.Profiles.Package = new PackageMetadataProfile
        {
            PackageName = "minimal_robot_description",
            Version = "0.1.0",
            Description = "Minimal robot fixture",
            MaintainerName = "Fixture Maintainer",
            MaintainerEmail = "fixture@example.com",
            License = "Apache-2.0"
        };
    }

    private static RobotDocument LoadFixtureRobot()
    {
        return UrdfCodec.Read(Fixture("minimal_robot.urdf"));
    }

    private static void RefreshManifestEntryAndChecksums(string bundle, string relative)
    {
        string payload = Path.Combine(bundle, relative.Replace('/', Path.DirectorySeparatorChar));
        string manifestPath = Path.Combine(bundle, "manifest.json");
        JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
        JObject entry = ((JArray)manifest["files"]!)
            .OfType<JObject>()
            .Single(item => string.Equals((string?)item["path"], relative, StringComparison.Ordinal));
        entry["sha256"] = Sha256(payload);
        entry["bytes"] = new FileInfo(payload).Length;
        File.WriteAllText(manifestPath, manifest.ToString());

        StringBuilder checksums = new();
        foreach (string file in Directory.GetFiles(bundle, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFileName(path), "checksums.sha256", StringComparison.Ordinal))
            .OrderBy(path => Path.GetRelativePath(bundle, path), StringComparer.Ordinal))
        {
            checksums.Append(Sha256(file))
                .Append("  ")
                .Append(Path.GetRelativePath(bundle, file).Replace('\\', '/'))
                .Append('\n');
        }
        File.WriteAllText(Path.Combine(bundle, "checksums.sha256"), checksums.ToString());
    }

    private static string Sha256(string path)
    {
        using SHA256 sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    private static string Fixture(string relative)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", relative);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
    }
}
