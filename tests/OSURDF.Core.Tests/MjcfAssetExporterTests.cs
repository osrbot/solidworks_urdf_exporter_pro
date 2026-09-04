using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

    [Theory]
    [InlineData(200000, ".STL")]
    [InlineData(200001, ".obj")]
    [InlineData(642074, ".obj")]
    public void ExportConvertsOnlyOversizedStlAndPreservesEveryTriangle(int triangles, string extension)
    {
        RobotDocument robot = LoadFixtureRobot();
        string source = Path.Combine(temporaryDirectory, "base_link.STL");
        WriteBinaryStl(source, triangles);
        GeometryDocument geometry = robot.Links[0].Visuals[0].Geometry;
        geometry.Uri = source;
        geometry.Scale = new Vector3Document { X = 0.001, Y = 2.5, Z = 0.75 };
        string bundle = BuildBundle(robot, "large-bundle");
        string canonical = Path.Combine(bundle, "meshes", "visual", "base_link.STL");
        string digest = FileDigest(source);
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        MjcfExportResult result;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            result = new MjcfAssetExporter().Export(new MjcfExportOptions
            {
                BundleDirectory = bundle,
                OutputDirectory = Path.Combine(temporaryDirectory, "large-delivery"),
                CompilerValidator = new RecordingValidator()
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        XElement mesh = Assert.Single(XDocument.Load(result.RobotXmlPath).Descendants("mesh"));
        string relative = (string)mesh.Attribute("file")!;
        Assert.Equal("assets/visual/base_link" + extension, relative);
        Assert.False(Path.IsPathRooted(relative));
        Assert.Equal(new[] { 0.001, 2.5, 0.75 }, Numbers(mesh.Attribute("scale")));
        Assert.Equal(digest, FileDigest(source));
        Assert.Equal(digest, FileDigest(canonical));
        Assert.Equal(digest, FileDigest(Path.Combine(result.OutputDirectory, "meshes", "visual", "base_link.STL")));
        Assert.True(new RobotBundleVerifier().Verify(bundle).IsValid);
        string asset = Path.Combine(result.OutputDirectory, relative);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(asset)!));
        if (extension == ".obj")
        {
            AssertObjMatchesBinaryStl(source, asset, triangles);
        }
        else
        {
            Assert.Equal(digest, FileDigest(asset));
            Assert.Empty(Directory.GetFiles(result.OutputDirectory, "*.obj", SearchOption.AllDirectories));
        }
    }

    [Fact]
    public void ExportReusesConvertedAssetsWithoutClobberingNativeObjAndIsDeterministic()
    {
        RobotDocument robot = LoadFixtureRobot();
        string source = Path.Combine(temporaryDirectory, "shared.STL");
        WriteBinaryStl(source, 200001);
        string nativeObj = Path.Combine(temporaryDirectory, "shared.obj");
        File.WriteAllText(nativeObj, "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 0 1\nf 1 3 2\nf 1 2 4\nf 1 4 3\nf 2 3 4\n");
        robot.Links[0].Visuals[0].Geometry = MeshGeometry(nativeObj);
        for (int index = 0; index < 3; index++)
        {
            GeometryDocument geometry = MeshGeometry(source);
            if (index == 2) geometry.Scale = new Vector3Document { X = 2, Y = 3, Z = 4 };
            robot.Links[0].Visuals.Add(new VisualDocument { Name = "shared_" + index, Geometry = geometry });
        }
        robot.Links[0].Collisions[0].Geometry = MeshGeometry(source);
        string bundle = BuildBundle(robot, "reuse-bundle");
        MjcfAssetExporter exporter = new();
        MjcfExportResult first = exporter.Export(new MjcfExportOptions
        {
            BundleDirectory = bundle,
            OutputDirectory = Path.Combine(temporaryDirectory, "reuse-first"),
            CompilerValidator = new RecordingValidator()
        });
        MjcfExportResult second = exporter.Export(new MjcfExportOptions
        {
            BundleDirectory = bundle,
            OutputDirectory = Path.Combine(temporaryDirectory, "reuse-second"),
            CompilerValidator = new RecordingValidator()
        });

        XElement[] meshes = XDocument.Load(first.RobotXmlPath).Descendants("mesh").ToArray();
        Assert.Equal(4, meshes.Length);
        Assert.Equal("assets/visual/shared.obj", (string?)meshes[0].Attribute("file"));
        string converted = (string)meshes[1].Attribute("file")!;
        Assert.StartsWith("assets/visual/shared-", converted);
        Assert.EndsWith(".obj", converted);
        Assert.Equal(converted, (string?)meshes[2].Attribute("file"));
        Assert.Equal(new[] { 2.0, 3.0, 4.0 }, Numbers(meshes[2].Attribute("scale")));
        Assert.Equal("assets/collision/shared.obj", (string?)meshes[3].Attribute("file"));
        Assert.Equal(FileDigest(nativeObj), FileDigest(Path.Combine(first.OutputDirectory, "assets/visual/shared.obj")));
        Assert.Equal(FileDigest(Path.Combine(first.OutputDirectory, converted)),
            FileDigest(Path.Combine(first.OutputDirectory, "assets/collision/shared.obj")));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(first.OutputDirectory, "assets/visual")).Length);
        string[] files = Directory.GetFiles(first.OutputDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(first.OutputDirectory, path)).OrderBy(path => path).ToArray();
        Assert.Equal(files, Directory.GetFiles(second.OutputDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(second.OutputDirectory, path)).OrderBy(path => path));
        foreach (string file in files)
        {
            Assert.Equal(FileDigest(Path.Combine(first.OutputDirectory, file)),
                FileDigest(Path.Combine(second.OutputDirectory, file)));
        }
    }

    [Theory]
    [InlineData("short-header")]
    [InlineData("truncated")]
    [InlineData("trailing-data")]
    [InlineData("wrong-count")]
    [InlineData("overflow-count")]
    [InlineData("empty")]
    [InlineData("nan-coordinate")]
    [InlineData("infinite-coordinate")]
    [InlineData("nan-normal")]
    public void ExportRejectsMalformedBinaryStlWithoutPublishing(string corruption)
    {
        RobotDocument robot = LoadFixtureRobot();
        string source = Path.Combine(temporaryDirectory, "malformed.STL");
        WriteBinaryStl(source, 200001);
        using (FileStream stream = File.Open(source, FileMode.Open, FileAccess.Write))
        using (BinaryWriter writer = new(stream))
        {
            switch (corruption)
            {
                case "short-header": stream.SetLength(83); break;
                case "truncated": stream.SetLength(stream.Length - 1); break;
                case "trailing-data": stream.SetLength(stream.Length + 1); break;
                case "wrong-count": stream.Position = 80; writer.Write(200000u); break;
                case "overflow-count": stream.Position = 80; writer.Write(uint.MaxValue); break;
                case "empty": stream.SetLength(84); stream.Position = 80; writer.Write(0u); break;
                case "nan-coordinate": stream.Position = stream.Length - 14; writer.Write(float.NaN); break;
                case "infinite-coordinate": stream.Position = 96; writer.Write(float.PositiveInfinity); break;
                case "nan-normal": stream.Position = 84; writer.Write(float.NaN); break;
            }
        }
        robot.Links[0].Visuals[0].Geometry.Uri = source;
        string bundle = BuildBundle(robot, "malformed-bundle");
        RecordingValidator validator = new();
        AssertValidationFailureDoesNotPublish(bundle, "malformed-delivery", validator, "Binary STL");
        Assert.Empty(validator.Paths);
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

    [Fact]
    [Trait("Category", "PinnedMuJoCoRuntime")]
    public void ExportLargeStlPassesBundledOfficialMuJoCoWhenRuntimeIsProvided()
    {
        string runtime = Environment.GetEnvironmentVariable("SW2URDF_MUJOCO_BIN")
            ?? throw new InvalidOperationException("SW2URDF_MUJOCO_BIN is required by the pinned MuJoCo runtime gate.");
        string version = Environment.GetEnvironmentVariable("SW2URDF_MUJOCO_VERSION")
            ?? throw new InvalidOperationException("SW2URDF_MUJOCO_VERSION is required by the pinned MuJoCo runtime gate.");
        RobotDocument robot = LoadFixtureRobot();
        string source = Path.Combine(temporaryDirectory, "dense.STL");
        const int triangles = 4 * 224 * 224;
        WriteBinaryStl(source, triangles);
        BundledMjcfCompilerValidator validator = new(
            Path.Combine(runtime, "compile.exe"), Path.Combine(runtime, "testspeed.exe"), version);
        string unconverted = Path.Combine(temporaryDirectory, "unconverted.xml");
        File.WriteAllText(unconverted,
            "<mujoco><asset><mesh name=\"dense\" file=\"dense.STL\"/></asset>" +
            "<worldbody><geom type=\"mesh\" mesh=\"dense\"/></worldbody></mujoco>");
        MjcfCompilerValidationResult rejected = validator.Validate(new MjcfCompilerValidationRequest
        {
            WorkingDirectory = temporaryDirectory,
            ModelPaths = new[] { unconverted }
        });
        Assert.False(rejected.Succeeded);
        Assert.Contains("200000", rejected.Message);
        robot.Links[0].Visuals[0].Geometry = MeshGeometry(source);
        string bundle = BuildBundle(robot, "official-large-bundle");
        MjcfExportResult result = new MjcfAssetExporter().Export(new MjcfExportOptions
        {
            BundleDirectory = bundle,
            OutputDirectory = Path.Combine(temporaryDirectory, "official-large-delivery"),
            CompilerValidator = validator
        });

        Assert.Equal("passed", result.OfficialCompilationStatus);
        AssertObjMatchesBinaryStl(source, Path.Combine(result.OutputDirectory, "assets/visual/dense.obj"), triangles);
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
            AllowAbsoluteAssetPaths = true,
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

    private RobotDocument LoadFixtureRobot()
    {
        RobotDocument robot = UrdfCodec.Read(Fixture("minimal_robot.urdf"));
        string mesh = Path.Combine(temporaryDirectory, "base.stl");
        WriteBinaryStl(mesh, 4);
        robot.Links[0].Visuals[0].Geometry.Uri = mesh;
        return robot;
    }

    private static GeometryDocument MeshGeometry(string path)
    {
        return new GeometryDocument { Type = "mesh", Uri = path };
    }

    private static string FileDigest(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteBinaryStl(string path, int triangles)
    {
        using BinaryWriter writer = new(File.Create(path));
        byte[] header = new byte[80];
        Encoding.ASCII.GetBytes("solid binary STL, not ASCII").CopyTo(header, 0);
        writer.Write(header);
        writer.Write((uint)triangles);
        int subdivisions = (int)Math.Ceiling(Math.Sqrt(triangles / 4.0));
        int[][] vertices = { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, 0, 1 } };
        int[][] faces = { new[] { 0, 2, 1 }, new[] { 0, 1, 3 }, new[] { 0, 3, 2 }, new[] { 1, 2, 3 } };
        int written = 0;
        foreach (int[] face in faces)
        {
            for (int row = 0; row < subdivisions && written < triangles; row++)
            {
                for (int column = 0; column < subdivisions - row && written < triangles; column++)
                {
                    WriteTriangle(Point(row, column), Point(row + 1, column), Point(row, column + 1));
                    if (column < subdivisions - row - 1 && written < triangles)
                    {
                        WriteTriangle(Point(row + 1, column), Point(row + 1, column + 1), Point(row, column + 1));
                    }
                }
            }

            (float X, float Y, float Z) Point(int row, int column)
            {
                float Coordinate(int axis) =>
                    (vertices[face[0]][axis] * (subdivisions - row - column) +
                     vertices[face[1]][axis] * row + vertices[face[2]][axis] * column) / (float)subdivisions;
                return (Coordinate(0) * 1.234567f - 0.125f,
                    Coordinate(1) * 2.1234567f + 0.25f, Coordinate(2) * 0.456789f - 0.375f);
            }
        }
        Assert.Equal(triangles, written);

        void WriteTriangle((float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c)
        {
            writer.Write(0f); writer.Write(0f); writer.Write(0f);
            WriteVertex(a); WriteVertex(b); WriteVertex(c);
            writer.Write((ushort)0);
            written++;
        }

        void WriteVertex((float X, float Y, float Z) vertex)
        {
            writer.Write(vertex.X); writer.Write(vertex.Y); writer.Write(vertex.Z);
        }
    }

    private static void AssertObjMatchesBinaryStl(string source, string obj, int triangles)
    {
        using BinaryReader reader = new(File.OpenRead(source));
        reader.BaseStream.Position = 84;
        List<(float X, float Y, float Z)> vertices = new();
        int faces = 0;
        foreach (string line in File.ReadLines(obj))
        {
            string[] fields = line.Split(' ');
            Assert.Equal(4, fields.Length);
            if (fields[0] == "v")
            {
                Assert.Equal(0, faces);
                vertices.Add((float.Parse(fields[1], CultureInfo.InvariantCulture),
                    float.Parse(fields[2], CultureInfo.InvariantCulture),
                    float.Parse(fields[3], CultureInfo.InvariantCulture)));
            }
            else
            {
                Assert.Equal("f", fields[0]);
                reader.ReadSingle(); reader.ReadSingle(); reader.ReadSingle();
                for (int corner = 1; corner <= 3; corner++)
                {
                    int index = int.Parse(fields[corner], CultureInfo.InvariantCulture);
                    Assert.InRange(index, 1, vertices.Count);
                    Assert.Equal((reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()), vertices[index - 1]);
                }
                reader.ReadUInt16();
                faces++;
            }
        }
        Assert.Equal(triangles, faces);
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        Assert.Equal(vertices.Count, vertices.Distinct().Count());
        Assert.True(vertices.Count < triangles * 3);
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
