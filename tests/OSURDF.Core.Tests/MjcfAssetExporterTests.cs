using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using OSURDF.Core.Bundle;
using OSURDF.Core.Export;
using OSURDF.Core.Model;
using OSURDF.Core.Urdf;
using Xunit;

namespace OSURDF.Core.Tests;

public sealed class MjcfAssetExporterTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "osurdf-mjcf-tests-" + Guid.NewGuid().ToString("N"));

    public MjcfAssetExporterTests()
    {
        Directory.CreateDirectory(temporaryDirectory);
    }

    [Fact]
    public void ExportPreservesHierarchyGeometryInertiaAndCanonicalMeshes()
    {
        string bundle = BuildBundle(LoadFixtureRobot(), "minimal-bundle");
        MjcfExportResult result = new MjcfAssetExporter().Export(new MjcfExportOptions
        {
            BundleDirectory = bundle,
            OutputDirectory = Path.Combine(temporaryDirectory, "delivery"),
            CompilerValidator = new RecordingValidator()
        });

        Assert.Equal(
            Path.Combine(temporaryDirectory, "delivery", "MuJoCo", "minimal_robot"),
            result.OutputDirectory);
        Assert.True(File.Exists(result.RobotXmlPath));
        Assert.True(File.Exists(result.SceneXmlPath));
        Assert.True(File.Exists(result.NameMapPath));
        Assert.True(File.Exists(result.ExportReportPath));
        Assert.True(Directory.Exists(Path.Combine(result.OutputDirectory, "assets", "visual")));
        Assert.True(Directory.Exists(Path.Combine(result.OutputDirectory, "assets", "collision")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "assets", "visual", "base.stl")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "meshes", "visual", "base.stl")));

        XDocument document = XDocument.Load(result.RobotXmlPath);
        XElement root = Assert.IsType<XElement>(document.Root);
        Assert.Equal("radian", (string?)root.Element("compiler")?.Attribute("angle"));
        Assert.Equal("false", (string?)root.Element("compiler")?.Attribute("inertiafromgeom"));
        XElement baseBody = Assert.Single(root.Element("worldbody")!.Elements("body"));
        Assert.Equal("base_link", (string?)baseBody.Attribute("name"));
        XElement armBody = Assert.Single(baseBody.Elements("body"));
        Assert.Equal("arm_link", (string?)armBody.Attribute("name"));
        Assert.Equal(new[] { 0.0, 0.0, 0.1 }, Numbers(armBody.Attribute("pos")));

        XElement shoulder = Assert.Single(armBody.Elements("joint"));
        Assert.Equal("hinge", (string?)shoulder.Attribute("type"));
        Assert.Equal("true", (string?)shoulder.Attribute("limited"));
        Assert.Equal(new[] { -1.5, 1.5 }, Numbers(shoulder.Attribute("range")));
        Assert.Equal(new[] { 0.0, 0.0, 1.0 }, Numbers(shoulder.Attribute("axis")));
        Assert.Equal("0.10000000000000001", (string?)shoulder.Attribute("damping"));
        Assert.Equal("0.01", (string?)shoulder.Attribute("frictionloss"));

        XElement armInertial = Assert.IsType<XElement>(armBody.Element("inertial"));
        Assert.Equal(new[] { 0.0, 0.0, 0.25 }, Numbers(armInertial.Attribute("pos")));
        Assert.Equal(
            new[] { 0.04, 0.04, 0.02, 0.0, 0.0, 0.0 },
            Numbers(armInertial.Attribute("fullinertia")));

        XElement meshAsset = Assert.Single(root.Element("asset")!.Elements("mesh"));
        Assert.Equal("assets/visual/base.stl", (string?)meshAsset.Attribute("file"));
        XElement visual = baseBody.Elements("geom").Single(item => (string?)item.Attribute("group") == "2");
        Assert.Equal("0", (string?)visual.Attribute("contype"));
        Assert.Equal("0", (string?)visual.Attribute("conaffinity"));
        XElement collision = baseBody.Elements("geom").Single(item => (string?)item.Attribute("group") == "3");
        Assert.Equal("box", (string?)collision.Attribute("type"));
        Assert.Equal(new[] { 0.25, 0.2, 0.1 }, Numbers(collision.Attribute("size")));

        XDocument scene = XDocument.Load(result.SceneXmlPath);
        Assert.Equal("robot.xml", (string?)scene.Root?.Element("include")?.Attribute("file"));
        JObject nameMap = JObject.Parse(File.ReadAllText(result.NameMapPath));
        Assert.Equal("base_link", (string?)nameMap["links"]?["base_link"]);
        Assert.Equal("shoulder_joint", (string?)nameMap["joints"]?["shoulder_joint"]?[0]);
        JObject report = JObject.Parse(File.ReadAllText(result.ExportReportPath));
        Assert.Equal("passed", (string?)report["structuralGeneration"]?["status"]);
        Assert.Equal("passed", (string?)report["officialCompilation"]?["status"]);
        Assert.Contains("meshes/visual/base.stl", report["canonicalMeshUris"]!.Values<string>());
        Assert.Empty(root.Elements("actuator"));
    }

    [Fact]
    public void ExportMapsAllContractJointTypesWithoutInventingActuators()
    {
        RobotDocument robot = LoadFixtureRobot();
        robot.Links[0].Inertial!.Origin.Rpy.Z = Math.PI / 2.0;
        AddLinkAndJoint(robot, "arm_link", "spin_link", "spin_joint", "continuous");
        AddLinkAndJoint(robot, "spin_link", "slide_link", "slide_joint", "prismatic", -0.2, 0.4);
        AddLinkAndJoint(robot, "slide_link", "floating_link", "floating_joint", "floating");
        AddLinkAndJoint(robot, "floating_link", "fixed_link", "fixed_joint", "fixed");

        string bundle = BuildBundle(robot, "joint-bundle");
        MjcfExportResult result = new MjcfAssetExporter().Export(new MjcfExportOptions
        {
            BundleDirectory = bundle,
            OutputDirectory = Path.Combine(temporaryDirectory, "joint-delivery"),
            CompilerValidator = new RecordingValidator()
        });
        XDocument document = XDocument.Load(result.RobotXmlPath);
        Dictionary<string, XElement> joints = document.Descendants("joint")
            .ToDictionary(item => (string)item.Attribute("name")!, StringComparer.Ordinal);

        Assert.Equal("hinge", (string?)joints["shoulder_joint"].Attribute("type"));
        Assert.Equal("false", (string?)joints["spin_joint"].Attribute("limited"));
        Assert.Equal("slide", (string?)joints["slide_joint"].Attribute("type"));
        Assert.Equal(new[] { -0.2, 0.4 }, Numbers(joints["slide_joint"].Attribute("range")));
        Assert.Equal("slide", (string?)joints["floating_joint_tx"].Attribute("type"));
        Assert.Equal("slide", (string?)joints["floating_joint_ty"].Attribute("type"));
        Assert.Equal("slide", (string?)joints["floating_joint_tz"].Attribute("type"));
        Assert.Equal("ball", (string?)joints["floating_joint_rotation"].Attribute("type"));
        Assert.DoesNotContain("fixed_joint", joints.Keys);
        Assert.Empty(document.Descendants("actuator"));

        JObject nameMap = JObject.Parse(File.ReadAllText(result.NameMapPath));
        Assert.Equal(4, nameMap["joints"]?["floating_joint"]?.Count());
        Assert.Empty(nameMap["joints"]?["fixed_joint"]!);

        XElement baseInertial = document.Descendants("body")
            .Single(item => (string?)item.Attribute("name") == "base_link")
            .Element("inertial")!;
        Assert.Equal(
            new[] { 0.3, 0.2, 0.4, 0.0, 0.0, 0.0 },
            Numbers(baseInertial.Attribute("fullinertia")),
            new DoubleArrayComparer(1e-12));
    }

    [Fact]
    public void ExportRecordsInjectedOfficialCompilationAndIsByteDeterministic()
    {
        string bundle = BuildBundle(LoadFixtureRobot(), "deterministic-bundle");
        RecordingValidator validator = new();
        MjcfAssetExporter exporter = new();
        MjcfExportResult first = exporter.Export(new MjcfExportOptions
        {
            BundleDirectory = bundle,
            OutputDirectory = Path.Combine(temporaryDirectory, "first"),
            CompilerValidator = validator
        });
        MjcfExportResult second = exporter.Export(new MjcfExportOptions
        {
            BundleDirectory = bundle,
            OutputDirectory = Path.Combine(temporaryDirectory, "second"),
            CompilerValidator = new RecordingValidator()
        });

        Assert.Equal("passed", first.OfficialCompilationStatus);
        Assert.Equal(2, validator.Paths.Count);
        Assert.True(validator.AllPathsExisted);
        JObject report = JObject.Parse(File.ReadAllText(first.ExportReportPath));
        Assert.Equal("official-test-double", (string?)report["officialCompilation"]?["validator"]);
        Assert.Equal("test-version", (string?)report["officialCompilation"]?["muJoCoVersion"]);

        string[] firstFiles = Directory.GetFiles(first.OutputDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(first.OutputDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] secondFiles = Directory.GetFiles(second.OutputDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(second.OutputDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(firstFiles, secondFiles);
        foreach (string relative in firstFiles)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(first.OutputDirectory, relative)),
                File.ReadAllBytes(Path.Combine(second.OutputDirectory, relative)));
        }
    }

    [Fact]
    public void ExportWithoutValidatorDoesNotPublish()
    {
        string bundle = BuildBundle(LoadFixtureRobot(), "missing-validator-bundle");
        AssertValidationFailureDoesNotPublish(bundle, "missing-validator-delivery", null, "required");
    }

    [Fact]
    public void ExportWhenValidatorReturnsNoResultDoesNotPublish()
    {
        string bundle = BuildBundle(LoadFixtureRobot(), "no-result-validator-bundle");
        AssertValidationFailureDoesNotPublish(
            bundle,
            "no-result-validator-delivery",
            new NoResultValidator(),
            "no result");
    }

    [Fact]
    public void ExportRejectsIncompleteSuccessEvidenceWithoutPublishing()
    {
        string bundle = BuildBundle(LoadFixtureRobot(), "incomplete-validator-bundle");
        AssertValidationFailureDoesNotPublish(
            bundle,
            "incomplete-validator-delivery",
            new IncompleteSuccessValidator(),
            "incomplete success evidence");
    }

    [Theory]
    [InlineData("compile")]
    [InlineData("save")]
    [InlineData("reload")]
    [InlineData("zero-control step")]
    public void ExportWhenValidationPhaseFailsDoesNotPublish(string phase)
    {
        string bundle = BuildBundle(LoadFixtureRobot(), "failed-" + phase.Replace(' ', '-') + "-bundle");
        AssertValidationFailureDoesNotPublish(
            bundle,
            "failed-" + phase.Replace(' ', '-') + "-delivery",
            new FailedPhaseValidator(phase),
            phase);
    }

    [Fact]
    [Trait("Category", "PinnedMuJoCoRuntime")]
    public void ExportPassesBundledOfficialMuJoCoWhenRuntimeIsProvided()
    {
        string runtime = Environment.GetEnvironmentVariable("SW2URDF_MUJOCO_BIN")
            ?? throw new InvalidOperationException(
                "SW2URDF_MUJOCO_BIN is required by the pinned MuJoCo runtime gate.");
        string version = Environment.GetEnvironmentVariable("SW2URDF_MUJOCO_VERSION")
            ?? throw new InvalidOperationException(
                "SW2URDF_MUJOCO_VERSION is required by the pinned MuJoCo runtime gate.");
        if (string.IsNullOrWhiteSpace(runtime) || string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException(
                "Pinned MuJoCo runtime variables must not be blank.");
        }

        RobotDocument robot = LoadFixtureRobot();
        robot.Links[0].Visuals[0].Geometry = new GeometryDocument
        {
            Type = "box",
            Size = new Vector3Document { X = 0.5, Y = 0.4, Z = 0.2 }
        };
        string bundle = BuildBundle(robot, "official-runtime-bundle");
        MjcfExportResult result = new MjcfAssetExporter().Export(new MjcfExportOptions
        {
            BundleDirectory = bundle,
            OutputDirectory = Path.Combine(temporaryDirectory, "official-runtime-delivery"),
            CompilerValidator = new BundledMjcfCompilerValidator(
                Path.Combine(runtime, "compile.exe"),
                Path.Combine(runtime, "testspeed.exe"),
                version)
        });

        Assert.Equal("passed", result.OfficialCompilationStatus);
        JObject report = JObject.Parse(File.ReadAllText(result.ExportReportPath));
        Assert.Equal("bundled-official-mujoco-tools", (string?)report["officialCompilation"]?["validator"]);
        Assert.Equal(version, (string?)report["officialCompilation"]?["muJoCoVersion"]);
        Assert.Contains("zero-control", (string?)report["officialCompilation"]?["message"]);
    }

    private string BuildBundle(RobotDocument robot, string name)
    {
        return new RobotBundleBuilder().Build(robot, new BundleBuildOptions
        {
            SourceUrdfPath = Fixture("minimal_robot.urdf"),
            OutputDirectory = Path.Combine(temporaryDirectory, name)
        }).OutputDirectory;
    }

    private void AssertValidationFailureDoesNotPublish(
        string bundle,
        string deliveryName,
        IMjcfCompilerValidator? validator,
        string expectedMessage)
    {
        string output = Path.Combine(temporaryDirectory, deliveryName);
        string destination = Path.Combine(output, "MuJoCo", "minimal_robot");
        Directory.CreateDirectory(destination);
        string sentinel = Path.Combine(destination, "existing.txt");
        File.WriteAllText(sentinel, "existing");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new MjcfAssetExporter().Export(new MjcfExportOptions
            {
                BundleDirectory = bundle,
                OutputDirectory = output,
                Overwrite = true,
                CompilerValidator = validator!
            }));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("existing", File.ReadAllText(sentinel));
        Assert.False(File.Exists(Path.Combine(destination, "robot.xml")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(output, "MuJoCo"), ".osurdf-mjcf-*"));
    }

    private static void AddLinkAndJoint(
        RobotDocument robot,
        string parent,
        string child,
        string jointName,
        string type,
        double? lower = null,
        double? upper = null)
    {
        bool scalar = type is "continuous" or "revolute" or "prismatic";
        robot.Links.Add(new LinkDocument
        {
            Id = StableId.Create("link", child),
            Name = child,
            Inertial = new InertialDocument
            {
                Mass = 1.0,
                Inertia = new InertiaTensorDocument { Ixx = 0.1, Iyy = 0.1, Izz = 0.1 }
            },
            Source = SourceProvenance.ImportedUrdf()
        });
        robot.Joints.Add(new JointDocument
        {
            Id = StableId.Create("joint", jointName),
            Name = jointName,
            Type = type,
            Parent = parent,
            Child = child,
            Origin = PoseDocument.Zero(),
            Axis = scalar ? Vector3Document.UnitX() : null,
            Limit = scalar
                ? new JointLimitDocument
                {
                    Lower = lower,
                    Upper = upper,
                    Effort = 10.0,
                    Velocity = 2.0
                }
                : null,
            Source = SourceProvenance.ImportedUrdf()
        });
    }

    private static double[] Numbers(XAttribute? attribute)
    {
        return (attribute?.Value ?? string.Empty)
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static RobotDocument LoadFixtureRobot()
    {
        return UrdfCodec.Read(Fixture("minimal_robot.urdf"));
    }

    private static string Fixture(string relative)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", relative);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
    }

    private sealed class RecordingValidator : IMjcfCompilerValidator
    {
        public IReadOnlyList<string> Paths { get; private set; } = Array.Empty<string>();

        public bool AllPathsExisted { get; private set; }

        public MjcfCompilerValidationResult Validate(MjcfCompilerValidationRequest request)
        {
            Paths = request.ModelPaths;
            AllPathsExisted = request.ModelPaths.All(File.Exists);
            return new MjcfCompilerValidationResult
            {
                Succeeded = true,
                Validator = "official-test-double",
                MuJoCoVersion = "test-version",
                Message = "Both models compiled."
            };
        }
    }

    private sealed class NoResultValidator : IMjcfCompilerValidator
    {
        public MjcfCompilerValidationResult Validate(MjcfCompilerValidationRequest request)
        {
            return null!;
        }
    }

    private sealed class IncompleteSuccessValidator : IMjcfCompilerValidator
    {
        public MjcfCompilerValidationResult Validate(MjcfCompilerValidationRequest request)
        {
            return new MjcfCompilerValidationResult { Succeeded = true };
        }
    }

    private sealed class FailedPhaseValidator : IMjcfCompilerValidator
    {
        private readonly string phase;

        public FailedPhaseValidator(string phase)
        {
            this.phase = phase;
        }

        public MjcfCompilerValidationResult Validate(MjcfCompilerValidationRequest request)
        {
            Assert.All(request.ModelPaths, path => Assert.True(File.Exists(path)));
            return new MjcfCompilerValidationResult
            {
                Succeeded = false,
                Validator = "official-phase-test-double",
                MuJoCoVersion = "test-version",
                Message = "Injected " + phase + " failure."
            };
        }
    }

    private sealed class DoubleArrayComparer : IEqualityComparer<double>
    {
        private readonly double tolerance;

        public DoubleArrayComparer(double tolerance)
        {
            this.tolerance = tolerance;
        }

        public bool Equals(double x, double y)
        {
            return Math.Abs(x - y) <= tolerance;
        }

        public int GetHashCode(double obj)
        {
            return 0;
        }
    }
}
