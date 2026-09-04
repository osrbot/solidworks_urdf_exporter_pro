using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OSURDF.Core.Bundle;
using OSURDF.Core.Model;
using OSURDF.Core.Serialization;
using OSURDF.Core.Validation;

namespace OSURDF.Core.Export
{
    public sealed class MjcfAssetExporter
    {
        private static readonly ISet<string> SupportedMeshExtensions = new HashSet<string>(
            new[] { ".msh", ".obj", ".stl" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly ISet<string> SupportedTextureExtensions = new HashSet<string>(
            new[] { ".ktx", ".png" },
            StringComparer.OrdinalIgnoreCase);

        public MjcfExportResult Export(MjcfExportOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.BundleDirectory) ||
                string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                throw new ArgumentException("Bundle and output directories are required.", nameof(options));
            }

            string bundleRoot = Path.GetFullPath(options.BundleDirectory);
            BundleVerificationResult bundleVerification = new RobotBundleVerifier().Verify(bundleRoot);
            if (!bundleVerification.IsValid)
            {
                throw new InvalidDataException(
                    "Robot Bundle verification failed: " + string.Join("; ", bundleVerification.Errors));
            }

            RobotDocument robot = RobotJson.Read(Path.Combine(bundleRoot, RobotBundleLayout.RobotJsonFile));
            ValidationReport validation = new RobotValidator().Validate(robot);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(
                    "Robot validation failed: " + string.Join(
                        "; ",
                        validation.Findings.Where(item => item.Severity == ValidationSeverity.Error)));
            }
            JointDocument planar = (robot.Joints ?? new List<JointDocument>())
                .FirstOrDefault(joint => joint != null && string.Equals(joint.Type, "planar", StringComparison.Ordinal));
            if (planar != null)
            {
                throw new NotSupportedException(
                    "MJCF export does not silently approximate planar Joint '" + planar.Name + "'.");
            }

            NameContext names = NameContext.Create(robot);
            string outputRoot = Path.GetFullPath(options.OutputDirectory);
            string mujocoRoot = Path.Combine(outputRoot, "MuJoCo");
            string destination = Path.GetFullPath(Path.Combine(mujocoRoot, names.RobotName));
            if (PathsOverlap(destination, bundleRoot))
            {
                throw new InvalidDataException(
                    "MuJoCo output and source Robot Bundle directories must not contain one another.");
            }
            Directory.CreateDirectory(mujocoRoot);
            EnsureSafeExistingDirectory(mujocoRoot, "MuJoCo output root");
            if (Directory.Exists(destination))
            {
                EnsureSafeExistingDirectory(destination, "MuJoCo robot output");
                if (!options.Overwrite)
                {
                    throw new IOException(
                        "MuJoCo robot output exists. Pass overwrite explicitly: " + destination);
                }
            }

            string staging = Path.Combine(mujocoRoot, ".osurdf-mjcf-" + Guid.NewGuid().ToString("N"));
            string previous = null;
            Directory.CreateDirectory(staging);
            try
            {
                Directory.CreateDirectory(Path.Combine(staging, "assets", "visual"));
                Directory.CreateDirectory(Path.Combine(staging, "assets", "collision"));
                ExportBuildContext context = new ExportBuildContext(bundleRoot, staging, robot, names);
                XDocument robotXml = BuildRobotXml(context);
                XDocument sceneXml = BuildSceneXml(names.RobotName);
                WriteXml(Path.Combine(staging, "robot.xml"), robotXml);
                WriteXml(Path.Combine(staging, "scene.xml"), sceneXml);
                WriteJson(Path.Combine(staging, "name_map.json"), BuildNameMap(robot, names));

                OfficialCompilationReport compilation = ValidateOfficialCompiler(
                    options.CompilerValidator,
                    staging);
                if (!string.Equals(compilation.Status, "passed", StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Official MuJoCo validation failed: " + compilation.Message);
                }
                WriteJson(
                    Path.Combine(staging, "export_report.json"),
                    BuildExportReport(context, compilation));

                if (Directory.Exists(destination))
                {
                    previous = destination + ".previous-" + Guid.NewGuid().ToString("N");
                    Directory.Move(destination, previous);
                }
                Directory.Move(staging, destination);
                staging = null;

                string retainedPrevious = null;
                if (previous != null)
                {
                    try
                    {
                        Directory.Delete(previous, true);
                        previous = null;
                    }
                    catch (Exception exception) when (
                        exception is IOException || exception is UnauthorizedAccessException)
                    {
                        retainedPrevious = previous;
                        previous = null;
                    }
                }

                return new MjcfExportResult
                {
                    OutputDirectory = destination,
                    RobotXmlPath = Path.Combine(destination, "robot.xml"),
                    SceneXmlPath = Path.Combine(destination, "scene.xml"),
                    NameMapPath = Path.Combine(destination, "name_map.json"),
                    ExportReportPath = Path.Combine(destination, "export_report.json"),
                    StructuralGenerationStatus = "passed",
                    OfficialCompilationStatus = compilation.Status,
                    RetainedPreviousDirectory = retainedPrevious
                };
            }
            catch
            {
                if (previous != null && !Directory.Exists(destination) && Directory.Exists(previous))
                {
                    Directory.Move(previous, destination);
                    previous = null;
                }
                throw;
            }
            finally
            {
                if (staging != null && Directory.Exists(staging))
                {
                    try
                    {
                        Directory.Delete(staging, true);
                    }
                    catch (Exception exception) when (
                        exception is IOException || exception is UnauthorizedAccessException)
                    {
                        // Keep the primary export exception; staging has a unique, non-user path.
                    }
                }
            }
        }

        private static XDocument BuildRobotXml(ExportBuildContext context)
        {
            RobotDocument robot = context.Robot;
            Dictionary<string, JointDocument> parentJointByChild = (robot.Joints ?? new List<JointDocument>())
                .Where(joint => joint != null)
                .ToDictionary(joint => joint.Child, joint => joint, StringComparer.Ordinal);
            Dictionary<string, List<JointDocument>> childJointsByParent = (robot.Links ?? new List<LinkDocument>())
                .Where(link => link != null)
                .ToDictionary(link => link.Name, link => new List<JointDocument>(), StringComparer.Ordinal);
            foreach (JointDocument joint in robot.Joints ?? new List<JointDocument>())
            {
                if (joint != null) childJointsByParent[joint.Parent].Add(joint);
            }
            LinkDocument root = (robot.Links ?? new List<LinkDocument>())
                .Single(link => link != null && !parentJointByChild.ContainsKey(link.Name));

            XElement worldBody = new XElement(
                "worldbody",
                BuildBody(context, root, null, childJointsByParent));
            XElement rootElement = new XElement(
                "mujoco",
                new XAttribute("model", context.Names.RobotName),
                new XElement(
                    "compiler",
                    new XAttribute("angle", "radian"),
                    new XAttribute("autolimits", "false"),
                    new XAttribute("inertiafromgeom", "false")),
                context.Asset.HasElements ? context.Asset : null,
                worldBody);
            return new XDocument(new XDeclaration("1.0", "utf-8", null), rootElement);
        }

        private static XElement BuildBody(
            ExportBuildContext context,
            LinkDocument link,
            JointDocument parentJoint,
            IDictionary<string, List<JointDocument>> childJointsByParent)
        {
            XElement body = new XElement("body", new XAttribute("name", context.Names.LinkNames[link.Name]));
            AddPose(body, parentJoint?.Origin ?? PoseDocument.Zero());
            if (link.Inertial != null)
            {
                body.Add(BuildInertial(link.Inertial));
            }
            if (parentJoint != null)
            {
                foreach (XElement jointElement in BuildJointElements(context, parentJoint))
                {
                    body.Add(jointElement);
                }
            }

            int visualIndex = 0;
            foreach (VisualDocument visual in link.Visuals ?? Enumerable.Empty<VisualDocument>())
            {
                body.Add(BuildGeom(context, link.Name, visual, true, visualIndex));
                visualIndex++;
            }
            int collisionIndex = 0;
            foreach (CollisionDocument collision in link.Collisions ?? Enumerable.Empty<CollisionDocument>())
            {
                body.Add(BuildGeom(context, link.Name, collision, false, collisionIndex));
                collisionIndex++;
            }
            foreach (JointDocument childJoint in childJointsByParent[link.Name])
            {
                body.Add(BuildBody(
                    context,
                    context.Robot.FindLink(childJoint.Child),
                    childJoint,
                    childJointsByParent));
            }
            return body;
        }

        private static XElement BuildInertial(InertialDocument inertial)
        {
            InertiaTensorDocument tensor = RotateInertiaToBodyFrame(
                inertial.Inertia,
                inertial.Origin?.Rpy);
            XElement result = new XElement(
                "inertial",
                new XAttribute("pos", Vector(inertial.Origin?.Xyz)),
                new XAttribute("mass", Number(inertial.Mass)),
                new XAttribute(
                    "fullinertia",
                    Numbers(tensor.Ixx, tensor.Iyy, tensor.Izz, tensor.Ixy, tensor.Ixz, tensor.Iyz)));
            return result;
        }

        private static IEnumerable<XElement> BuildJointElements(
            ExportBuildContext context,
            JointDocument joint)
        {
            IReadOnlyList<string> mappedNames = context.Names.JointNames[joint.Name];
            switch (joint.Type)
            {
                case "fixed":
                    yield break;
                case "continuous":
                    yield return BuildScalarJoint(joint, mappedNames[0], "hinge", false);
                    yield break;
                case "revolute":
                    yield return BuildScalarJoint(joint, mappedNames[0], "hinge", true);
                    yield break;
                case "prismatic":
                    yield return BuildScalarJoint(joint, mappedNames[0], "slide", true);
                    yield break;
                case "floating":
                    yield return BuildFloatingTranslationJoint(joint, mappedNames[0], "1 0 0");
                    yield return BuildFloatingTranslationJoint(joint, mappedNames[1], "0 1 0");
                    yield return BuildFloatingTranslationJoint(joint, mappedNames[2], "0 0 1");
                    yield return BuildFloatingRotationJoint(joint, mappedNames[3]);
                    yield break;
                default:
                    throw new NotSupportedException("Unsupported MJCF Joint mapping: " + joint.Type);
            }
        }

        private static XElement BuildScalarJoint(
            JointDocument source,
            string name,
            string type,
            bool limited)
        {
            XElement joint = new XElement(
                "joint",
                new XAttribute("name", name),
                new XAttribute("type", type),
                new XAttribute("pos", "0 0 0"),
                new XAttribute("axis", Vector(source.Axis)),
                new XAttribute("limited", limited ? "true" : "false"));
            if (limited)
            {
                joint.Add(new XAttribute(
                    "range",
                    Numbers(source.Limit.Lower.Value, source.Limit.Upper.Value)));
            }
            AddJointDynamics(joint, source.Dynamics);
            return joint;
        }

        private static XElement BuildFloatingTranslationJoint(
            JointDocument source,
            string name,
            string axis)
        {
            XElement joint = new XElement(
                "joint",
                new XAttribute("name", name),
                new XAttribute("type", "slide"),
                new XAttribute("pos", "0 0 0"),
                new XAttribute("axis", axis),
                new XAttribute("limited", "false"));
            AddJointDynamics(joint, source.Dynamics);
            return joint;
        }

        private static XElement BuildFloatingRotationJoint(JointDocument source, string name)
        {
            XElement joint = new XElement(
                "joint",
                new XAttribute("name", name),
                new XAttribute("type", "ball"),
                new XAttribute("pos", "0 0 0"),
                new XAttribute("limited", "false"));
            AddJointDynamics(joint, source.Dynamics);
            return joint;
        }

        private static void AddJointDynamics(XElement joint, JointDynamicsDocument dynamics)
        {
            if (dynamics?.Damping.HasValue == true)
            {
                joint.Add(new XAttribute("damping", Number(dynamics.Damping.Value)));
            }
            if (dynamics?.Friction.HasValue == true)
            {
                joint.Add(new XAttribute("frictionloss", Number(dynamics.Friction.Value)));
            }
        }

        private static XElement BuildGeom(
            ExportBuildContext context,
            string linkName,
            GeometryInstanceDocument instance,
            bool visual,
            int index)
        {
            string identity = GeometryIdentity(linkName, visual, instance.Name, index);
            string geomName = visual
                ? context.Names.VisualNames[identity]
                : context.Names.CollisionNames[identity];
            XElement geom = new XElement(
                "geom",
                new XAttribute("name", geomName),
                new XAttribute("group", visual ? "2" : "3"));
            AddPose(geom, instance.Origin);
            if (visual)
            {
                geom.Add(new XAttribute("contype", "0"));
                geom.Add(new XAttribute("conaffinity", "0"));
            }
            else
            {
                geom.Add(new XAttribute("rgba", "0.8 0.2 0.2 0.25"));
            }

            GeometryDocument geometry = instance.Geometry;
            switch (geometry.Type)
            {
                case "box":
                    geom.Add(new XAttribute("type", "box"));
                    geom.Add(new XAttribute(
                        "size",
                        Numbers(geometry.Size.X / 2.0, geometry.Size.Y / 2.0, geometry.Size.Z / 2.0)));
                    break;
                case "cylinder":
                    geom.Add(new XAttribute("type", "cylinder"));
                    geom.Add(new XAttribute(
                        "size",
                        Numbers(geometry.Radius.Value, geometry.Length.Value / 2.0)));
                    break;
                case "sphere":
                    geom.Add(new XAttribute("type", "sphere"));
                    geom.Add(new XAttribute("size", Number(geometry.Radius.Value)));
                    break;
                case "mesh":
                    geom.Add(new XAttribute("type", "mesh"));
                    geom.Add(new XAttribute(
                        "mesh",
                        context.AddMeshAsset(geometry, visual ? "visual" : "collision", geomName)));
                    break;
                default:
                    throw new NotSupportedException("Unsupported MJCF geometry mapping: " + geometry.Type);
            }

            VisualDocument visualInstance = instance as VisualDocument;
            if (visualInstance?.Material != null)
            {
                ApplyVisualMaterial(context, geom, visualInstance.Material, geomName);
            }
            return geom;
        }

        private static void ApplyVisualMaterial(
            ExportBuildContext context,
            XElement geom,
            MaterialDocument material,
            string geomName)
        {
            if (!string.IsNullOrWhiteSpace(material.TextureUri))
            {
                string textureName = context.AddTextureAsset(material.TextureUri, geomName);
                string materialName = context.Names.MaterialAllocator.Allocate(geomName + "_material", geomName);
                XElement materialElement = new XElement(
                    "material",
                    new XAttribute("name", materialName),
                    new XAttribute("texture", textureName));
                if (material.Rgba != null)
                {
                    materialElement.Add(new XAttribute("rgba", Vector(material.Rgba)));
                }
                context.Asset.Add(materialElement);
                geom.Add(new XAttribute("material", materialName));
            }
            else if (material.Rgba != null)
            {
                geom.Add(new XAttribute("rgba", Vector(material.Rgba)));
            }
        }

        private static XDocument BuildSceneXml(string robotName)
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    "mujoco",
                    new XAttribute("model", robotName + "_scene"),
                    new XElement("include", new XAttribute("file", "robot.xml"))));
        }

        private static JObject BuildNameMap(RobotDocument robot, NameContext names)
        {
            JObject links = new JObject();
            foreach (LinkDocument link in robot.Links ?? new List<LinkDocument>())
            {
                links.Add(link.Name, names.LinkNames[link.Name]);
            }
            JObject joints = new JObject();
            foreach (JointDocument joint in robot.Joints ?? new List<JointDocument>())
            {
                joints.Add(joint.Name, new JArray(names.JointNames[joint.Name]));
            }
            JObject visuals = new JObject();
            JObject collisions = new JObject();
            foreach (LinkDocument link in robot.Links ?? new List<LinkDocument>())
            {
                int index = 0;
                foreach (VisualDocument visual in link.Visuals ?? Enumerable.Empty<VisualDocument>())
                {
                    string identity = GeometryIdentity(link.Name, true, visual.Name, index++);
                    visuals.Add(identity, names.VisualNames[identity]);
                }
                index = 0;
                foreach (CollisionDocument collision in link.Collisions ?? Enumerable.Empty<CollisionDocument>())
                {
                    string identity = GeometryIdentity(link.Name, false, collision.Name, index++);
                    collisions.Add(identity, names.CollisionNames[identity]);
                }
            }
            return new JObject
            {
                ["schemaVersion"] = 1,
                ["robot"] = new JObject
                {
                    ["source"] = robot.Name,
                    ["mjcf"] = names.RobotName
                },
                ["links"] = links,
                ["joints"] = joints,
                ["visuals"] = visuals,
                ["collisions"] = collisions
            };
        }

        private static JObject BuildExportReport(
            ExportBuildContext context,
            OfficialCompilationReport compilation)
        {
            RobotDocument robot = context.Robot;
            JObject mappingCounts = new JObject();
            foreach (string type in new[] { "fixed", "revolute", "continuous", "prismatic", "floating" })
            {
                mappingCounts[type] = (robot.Joints ?? new List<JointDocument>())
                    .Count(joint => joint != null && string.Equals(joint.Type, type, StringComparison.Ordinal));
            }
            return new JObject
            {
                ["schemaVersion"] = 1,
                ["format"] = "osurdf-mjcf-export-report",
                ["robot"] = robot.Name,
                ["structuralGeneration"] = new JObject
                {
                    ["status"] = "passed",
                    ["xmlApi"] = "System.Xml.Linq",
                    ["inertiaSource"] = "CAD mass, COM, and full inertia from RobotDocument",
                    ["visualCollisionSeparated"] = true
                },
                ["officialCompilation"] = new JObject
                {
                    ["status"] = compilation.Status,
                    ["validator"] = compilation.Validator == null ? JValue.CreateNull() : new JValue(compilation.Validator),
                    ["muJoCoVersion"] = compilation.MuJoCoVersion == null ? JValue.CreateNull() : new JValue(compilation.MuJoCoVersion),
                    ["message"] = compilation.Message == null ? JValue.CreateNull() : new JValue(compilation.Message)
                },
                ["outputs"] = new JArray(
                    "robot.xml",
                    "scene.xml",
                    "assets/visual",
                    "assets/collision",
                    "name_map.json",
                    "export_report.json"),
                ["counts"] = new JObject
                {
                    ["links"] = (robot.Links ?? new List<LinkDocument>()).Count,
                    ["joints"] = (robot.Joints ?? new List<JointDocument>()).Count,
                    ["visuals"] = context.VisualCount,
                    ["collisions"] = context.CollisionCount,
                    ["meshAssets"] = context.MeshAssetCount,
                    ["textureAssets"] = context.TextureAssetCount
                },
                ["canonicalMeshUris"] = new JArray(
                    context.CanonicalMeshUris.OrderBy(item => item, StringComparer.Ordinal)),
                ["jointMappings"] = mappingCounts,
                ["warnings"] = new JArray(context.Warnings.OrderBy(item => item, StringComparer.Ordinal)),
                ["validationScope"] = new JObject
                {
                    ["en"] = new JObject
                    {
                        ["structuralGenerationPassed"] =
                            "The Robot Bundle manifest/checksums and canonical RobotDocument validation passed, and the exporter wrote structured MJCF XML, copied assets, and stable name mappings. This status does not mean MuJoCo parsed or simulated the model.",
                        ["officialCompilationPassed"] =
                            "A passed officialCompilation status means the injected validator reported success. For validator 'bundled-official-mujoco-tools', both robot.xml and scene.xml were compiled to MJB; the original XML and compiled MJB were independently loaded and advanced for one zero-control step with the reported MuJoCo version. Canonical XML rewriting is not required because it can round small nonzero inertia values to zero.",
                        ["notCovered"] = new JArray
                        {
                            "Source CAD/URDF engineering correctness or physical fidelity.",
                            "Long-horizon numerical stability, contact tuning, controller quality, task behavior, performance, or rendering fidelity."
                        }
                    },
                    ["zh-CN"] = new JObject
                    {
                        ["structuralGenerationPassed"] =
                            "Robot Bundle 清单/校验和及规范 RobotDocument 验证已通过，导出器已写入结构化 MJCF XML、复制资产并生成稳定名称映射；该状态不代表 MuJoCo 已解析或运行模型。",
                        ["officialCompilationPassed"] =
                            "officialCompilation 为 passed 仅表示注入的验证器报告成功。对于 bundled-official-mujoco-tools，robot.xml 与 scene.xml 均使用报告版本的 MuJoCo 编译为 MJB；原始 XML 和编译后的 MJB 均独立载入并执行一步零控制仿真。不要求重写规范 XML，因为该过程可能将非零的小惯性值舍入为零。",
                        ["notCovered"] = new JArray
                        {
                            "源 CAD/URDF 的工程正确性或物理保真度。",
                            "长时间数值稳定性、接触调参、控制器质量、任务行为、性能或渲染保真度。"
                        }
                    }
                },
                ["notGeneratedCapabilities"] = new JObject
                {
                    ["en"] = new JArray
                    {
                        "Actuators, transmissions, controllers, PID gains, or control policies.",
                        "Sensors, keyframes, explicit contact pairs, friction/contact-solver tuning, or simulation timestep tuning.",
                        "World geometry, ground plane, lights, cameras, task environments, rewards, observations, resets, or domain randomization.",
                        "RL training code or task definitions; scene.xml only includes robot.xml.",
                        "Unsupported mesh or texture format conversion."
                    },
                    ["zh-CN"] = new JArray
                    {
                        "执行器、传动、控制器、PID 增益或控制策略。",
                        "传感器、关键帧、显式接触对、摩擦/接触求解器调参或仿真步长调参。",
                        "世界几何体、地面、灯光、相机、任务环境、奖励、观测、重置或域随机化。",
                        "强化学习训练代码或任务定义；scene.xml 仅引用 robot.xml。",
                        "不受支持的网格或纹理格式转换。"
                    }
                },
                ["intentionallyNotGenerated"] = new JArray("actuators", "PID gains", "RL task definitions")
            };
        }

        private static OfficialCompilationReport ValidateOfficialCompiler(
            IMjcfCompilerValidator validator,
            string staging)
        {
            if (validator == null)
            {
                return new OfficialCompilationReport
                {
                    Status = "failed",
                    Message =
                        "An official MuJoCo validator is required before MJCF assets can be published."
                };
            }
            try
            {
                MjcfCompilerValidationResult result = validator.Validate(
                    new MjcfCompilerValidationRequest
                    {
                        WorkingDirectory = staging,
                        ModelPaths = new[]
                        {
                            Path.Combine(staging, "robot.xml"),
                            Path.Combine(staging, "scene.xml")
                        }
                    });
                if (result == null)
                {
                    return new OfficialCompilationReport
                    {
                        Status = "failed",
                        Message = "The injected MuJoCo validator returned no result."
                    };
                }
                if (result.Succeeded &&
                    (string.IsNullOrWhiteSpace(result.Validator) ||
                     string.IsNullOrWhiteSpace(result.MuJoCoVersion) ||
                     string.IsNullOrWhiteSpace(result.Message)))
                {
                    return new OfficialCompilationReport
                    {
                        Status = "failed",
                        Validator = result.Validator,
                        MuJoCoVersion = result.MuJoCoVersion,
                        Message =
                            "The injected MuJoCo validator returned incomplete success evidence."
                    };
                }
                return new OfficialCompilationReport
                {
                    Status = result.Succeeded ? "passed" : "failed",
                    Validator = result.Validator,
                    MuJoCoVersion = result.MuJoCoVersion,
                    Message = result.Message
                };
            }
            catch (Exception exception)
            {
                return new OfficialCompilationReport
                {
                    Status = "unavailable",
                    Validator = validator.GetType().FullName,
                    Message = exception.GetType().Name + ": " + exception.Message
                };
            }
        }

        private static InertiaTensorDocument RotateInertiaToBodyFrame(
            InertiaTensorDocument source,
            Vector3Document rpy)
        {
            double roll = rpy?.X ?? 0.0;
            double pitch = rpy?.Y ?? 0.0;
            double yaw = rpy?.Z ?? 0.0;
            double cr = Math.Cos(roll);
            double sr = Math.Sin(roll);
            double cp = Math.Cos(pitch);
            double sp = Math.Sin(pitch);
            double cy = Math.Cos(yaw);
            double sy = Math.Sin(yaw);
            double[,] rotation =
            {
                { cy * cp, cy * sp * sr - sy * cr, cy * sp * cr + sy * sr },
                { sy * cp, sy * sp * sr + cy * cr, sy * sp * cr - cy * sr },
                { -sp, cp * sr, cp * cr }
            };
            double[,] inertia =
            {
                { source.Ixx, source.Ixy, source.Ixz },
                { source.Ixy, source.Iyy, source.Iyz },
                { source.Ixz, source.Iyz, source.Izz }
            };
            double[,] rotated = new double[3, 3];
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    for (int left = 0; left < 3; left++)
                    {
                        for (int right = 0; right < 3; right++)
                        {
                            rotated[row, column] +=
                                rotation[row, left] * inertia[left, right] * rotation[column, right];
                        }
                    }
                }
            }
            return new InertiaTensorDocument
            {
                Ixx = rotated[0, 0],
                Iyy = rotated[1, 1],
                Izz = rotated[2, 2],
                Ixy = Symmetric(rotated[0, 1], rotated[1, 0]),
                Ixz = Symmetric(rotated[0, 2], rotated[2, 0]),
                Iyz = Symmetric(rotated[1, 2], rotated[2, 1])
            };
        }

        private static double Symmetric(double first, double second)
        {
            return (first + second) / 2.0;
        }

        private static void AddPose(XElement element, PoseDocument pose)
        {
            PoseDocument value = pose ?? PoseDocument.Zero();
            element.Add(new XAttribute("pos", Vector(value.Xyz)));
            element.Add(new XAttribute("quat", Quaternion(value.Rpy)));
        }

        private static string Quaternion(Vector3Document rpy)
        {
            double roll = rpy?.X ?? 0.0;
            double pitch = rpy?.Y ?? 0.0;
            double yaw = rpy?.Z ?? 0.0;
            double cr = Math.Cos(roll / 2.0);
            double sr = Math.Sin(roll / 2.0);
            double cp = Math.Cos(pitch / 2.0);
            double sp = Math.Sin(pitch / 2.0);
            double cy = Math.Cos(yaw / 2.0);
            double sy = Math.Sin(yaw / 2.0);
            return Numbers(
                cr * cp * cy + sr * sp * sy,
                sr * cp * cy - cr * sp * sy,
                cr * sp * cy + sr * cp * sy,
                cr * cp * sy - sr * sp * cy);
        }

        private static string Vector(Vector3Document value)
        {
            return Numbers(value?.X ?? 0.0, value?.Y ?? 0.0, value?.Z ?? 0.0);
        }

        private static string Vector(Vector4Document value)
        {
            return Numbers(value.X, value.Y, value.Z, value.W);
        }

        private static string Numbers(params double[] values)
        {
            return string.Join(" ", values.Select(Number));
        }

        private static string Number(double value)
        {
            if (value == 0.0) value = 0.0;
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string GeometryIdentity(
            string linkName,
            bool visual,
            string instanceName,
            int index)
        {
            return linkName + "/" + (visual ? "visual" : "collision") + "/" +
                (string.IsNullOrWhiteSpace(instanceName)
                    ? index.ToString(CultureInfo.InvariantCulture)
                    : instanceName);
        }

        private static void WriteXml(string path, XDocument document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                document.Save(writer, SaveOptions.None);
            }
        }

        private static void WriteJson(string path, JToken value)
        {
            RobotBundleBuilder.WriteUtf8(path, value.ToString(Formatting.Indented) + "\n");
        }

        private static void EnsureSafeExistingDirectory(string path, string label)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(label + " must not be a symbolic link or reparse point: " + path);
            }
            RobotBundleBuilder.EnsureNoReparsePoints(path, label);
        }

        private static bool PathsOverlap(string first, string second)
        {
            string fullFirst = Path.GetFullPath(first).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string fullSecond = Path.GetFullPath(second).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(fullFirst, fullSecond, comparison)) return true;
            return fullFirst.StartsWith(fullSecond + Path.DirectorySeparatorChar, comparison) ||
                fullSecond.StartsWith(fullFirst + Path.DirectorySeparatorChar, comparison);
        }

        private sealed class ExportBuildContext
        {
            // MuJoCo's binary STL reader has a face limit that does not apply to OBJ.
            private const uint MuJoCoStlTriangleLimit = 200000;
            private readonly string bundleRoot;
            private readonly string staging;
            private readonly IDictionary<string, string> copiedAssets =
                new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly IDictionary<string, string> copiedByRoleAndDigest =
                new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly IDictionary<string, string> meshNames =
                new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly IDictionary<string, string> textureNames =
                new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly ISet<string> canonicalMeshUris =
                new HashSet<string>(StringComparer.Ordinal);

            public ExportBuildContext(
                string bundleRoot,
                string staging,
                RobotDocument robot,
                NameContext names)
            {
                this.bundleRoot = bundleRoot;
                this.staging = staging;
                Robot = robot;
                Names = names;
                Asset = new XElement("asset");
                VisualCount = robot.Links.Sum(link => (link.Visuals ?? new List<VisualDocument>()).Count);
                CollisionCount = robot.Links.Sum(link => (link.Collisions ?? new List<CollisionDocument>()).Count);
                foreach (JointDocument joint in robot.Joints ?? new List<JointDocument>())
                {
                    if (joint.Limit?.Effort.HasValue == true || joint.Limit?.Velocity.HasValue == true)
                    {
                        Warnings.Add(
                            "Joint '" + joint.Name +
                            "' effort/velocity metadata was not converted into an actuator because actuator semantics were not provided.");
                    }
                    if (joint.Mimic != null)
                    {
                        Warnings.Add(
                            "Joint '" + joint.Name +
                            "' mimic metadata was not converted into a constraint because target semantics require explicit review.");
                    }
                }
            }

            public RobotDocument Robot { get; }

            public NameContext Names { get; }

            public XElement Asset { get; }

            public int VisualCount { get; }

            public int CollisionCount { get; }

            public int MeshAssetCount => meshNames.Count;

            public int TextureAssetCount => textureNames.Count;

            public ISet<string> Warnings { get; } = new HashSet<string>(StringComparer.Ordinal);

            public IEnumerable<string> CanonicalMeshUris => canonicalMeshUris;

            public string AddMeshAsset(GeometryDocument geometry, string role, string geomName)
            {
                string extension = Path.GetExtension(geometry.Uri ?? string.Empty);
                if (!SupportedMeshExtensions.Contains(extension))
                {
                    throw new NotSupportedException(
                        "MuJoCo officially loads STL, OBJ, and MSH mesh assets; unsupported asset: " + geometry.Uri);
                }
                PreserveCanonicalMesh(geometry.Uri);
                Vector3Document scale = geometry.Scale ?? new Vector3Document { X = 1.0, Y = 1.0, Z = 1.0 };
                string key = role + "\n" + geometry.Uri + "\n" + Vector(scale);
                string existing;
                if (meshNames.TryGetValue(key, out existing)) return existing;

                string relative = CopyAsset(geometry.Uri, role);
                string name = Names.MeshAllocator.Allocate(geomName + "_mesh", key);
                Asset.Add(new XElement(
                    "mesh",
                    new XAttribute("name", name),
                    new XAttribute("file", relative),
                    new XAttribute("scale", Vector(scale))));
                meshNames[key] = name;
                return name;
            }

            private void PreserveCanonicalMesh(string uri)
            {
                string normalized = (uri ?? string.Empty).Replace('\\', '/');
                if (!normalized.StartsWith("meshes/", StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Robot Bundle mesh URI is not in the canonical meshes/ tree: " + normalized);
                }
                if (!canonicalMeshUris.Add(normalized)) return;
                string source = RobotBundleBuilder.SafeBundlePath(bundleRoot, normalized);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException("Robot Bundle mesh is missing.", normalized);
                }
                string destination = RobotBundleBuilder.SafeBundlePath(staging, normalized);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(source, destination, false);
            }

            public string AddTextureAsset(string uri, string geomName)
            {
                string extension = Path.GetExtension(uri ?? string.Empty);
                if (!SupportedTextureExtensions.Contains(extension))
                {
                    throw new NotSupportedException(
                        "MuJoCo officially loads PNG and KTX texture assets; unsupported asset: " + uri);
                }
                string existing;
                if (textureNames.TryGetValue(uri, out existing)) return existing;

                string relative = CopyAsset(uri, "visual");
                string name = Names.TextureAllocator.Allocate(geomName + "_texture", uri);
                Asset.Add(new XElement(
                    "texture",
                    new XAttribute("name", name),
                    new XAttribute("type", "2d"),
                    new XAttribute("file", relative)));
                textureNames[uri] = name;
                return name;
            }

            private string CopyAsset(string uri, string role)
            {
                string normalized = (uri ?? string.Empty).Replace('\\', '/');
                string copyKey = role + "\n" + normalized;
                string existing;
                if (copiedAssets.TryGetValue(copyKey, out existing)) return existing;

                string source = RobotBundleBuilder.SafeBundlePath(bundleRoot, normalized);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException("Robot Bundle asset is missing.", normalized);
                }
                bool convertStl = false;
                if (string.Equals(Path.GetExtension(source), ".stl", StringComparison.OrdinalIgnoreCase))
                {
                    using (BinaryReader reader = new BinaryReader(File.OpenRead(source)))
                    {
                        convertStl = ReadBinaryStlTriangleCount(reader, normalized) > MuJoCoStlTriangleLimit;
                    }
                }
                string digest = RobotBundleBuilder.Sha256File(source);
                string digestKey = role + "\n" + digest;
                if (copiedByRoleAndDigest.TryGetValue(digestKey, out existing))
                {
                    copiedAssets[copyKey] = existing;
                    return existing;
                }

                string fileName = SafeFileName(Path.GetFileName(source));
                if (convertStl) fileName = Path.ChangeExtension(fileName, ".obj");
                string relative = "assets/" + role + "/" + fileName;
                string destination = RobotBundleBuilder.SafeBundlePath(staging, relative);
                if (File.Exists(destination))
                {
                    relative = "assets/" + role + "/" +
                        Path.GetFileNameWithoutExtension(fileName) + "-" + digest.Substring(0, 12) +
                        Path.GetExtension(fileName);
                    destination = RobotBundleBuilder.SafeBundlePath(staging, relative);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (convertStl)
                {
                    WriteBinaryStlAsObj(source, destination);
                }
                else
                {
                    File.Copy(source, destination, false);
                }
                copiedAssets[copyKey] = relative;
                copiedByRoleAndDigest[digestKey] = relative;
                return relative;
            }

            private static uint ReadBinaryStlTriangleCount(BinaryReader reader, string source)
            {
                if (reader.BaseStream.Length < 84)
                {
                    throw new InvalidDataException("Binary STL header is truncated: " + source);
                }
                reader.BaseStream.Position = 80;
                uint triangles = reader.ReadUInt32();
                if (triangles == 0 || reader.BaseStream.Length != 84L + 50L * triangles)
                {
                    throw new InvalidDataException("Binary STL triangle count does not match its length: " + source);
                }
                return triangles;
            }

            private static void WriteBinaryStlAsObj(string source, string destination)
            {
                using (BinaryReader reader = new BinaryReader(File.OpenRead(source)))
                using (StreamWriter writer = new StreamWriter(destination, false, new UTF8Encoding(false)))
                {
                    uint triangles = ReadBinaryStlTriangleCount(reader, source);
                    int[] faces = new int[checked((int)(3L * triangles))];
                    Dictionary<(float X, float Y, float Z), int> vertices =
                        new Dictionary<(float X, float Y, float Z), int>();
                    writer.NewLine = "\n";
                    for (int face = 0; face < faces.Length; face += 3)
                    {
                        // MuJoCo ignores STL normals and welds exactly equal vertices before computing normals.
                        for (int axis = 0; axis < 3; axis++) ReadFiniteStlValue(reader, source);
                        for (int corner = 0; corner < 3; corner++)
                        {
                            var vertex = (
                                X: ReadFiniteStlValue(reader, source),
                                Y: ReadFiniteStlValue(reader, source),
                                Z: ReadFiniteStlValue(reader, source));
                            int index;
                            if (!vertices.TryGetValue(vertex, out index))
                            {
                                index = vertices.Count + 1;
                                vertices.Add(vertex, index);
                                writer.WriteLine("v " +
                                    vertex.X.ToString("G9", CultureInfo.InvariantCulture) + " " +
                                    vertex.Y.ToString("G9", CultureInfo.InvariantCulture) + " " +
                                    vertex.Z.ToString("G9", CultureInfo.InvariantCulture));
                            }
                            faces[face + corner] = index;
                        }
                        reader.ReadUInt16();
                    }
                    for (int face = 0; face < faces.Length; face += 3)
                    {
                        writer.WriteLine("f " +
                            faces[face].ToString(CultureInfo.InvariantCulture) + " " +
                            faces[face + 1].ToString(CultureInfo.InvariantCulture) + " " +
                            faces[face + 2].ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            private static float ReadFiniteStlValue(BinaryReader reader, string source)
            {
                float value = reader.ReadSingle();
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new InvalidDataException("Binary STL contains a non-finite coordinate or normal: " + source);
                }
                return value;
            }
        }

        private sealed class NameContext
        {
            private NameContext()
            {
            }

            public string RobotName { get; private set; }

            public IDictionary<string, string> LinkNames { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public IDictionary<string, IReadOnlyList<string>> JointNames { get; } =
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

            public IDictionary<string, string> VisualNames { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public IDictionary<string, string> CollisionNames { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public NameAllocator MeshAllocator { get; } = new NameAllocator();

            public NameAllocator TextureAllocator { get; } = new NameAllocator();

            public NameAllocator MaterialAllocator { get; } = new NameAllocator();

            public static NameContext Create(RobotDocument robot)
            {
                NameContext result = new NameContext
                {
                    RobotName = GetRobotDirectoryName(robot.Name)
                };
                NameAllocator bodies = new NameAllocator();
                NameAllocator joints = new NameAllocator();
                NameAllocator visuals = new NameAllocator();
                NameAllocator collisions = new NameAllocator();
                foreach (LinkDocument link in robot.Links ?? new List<LinkDocument>())
                {
                    result.LinkNames.Add(link.Name, bodies.Allocate(link.Name, link.Id ?? link.Name));
                }
                foreach (JointDocument joint in robot.Joints ?? new List<JointDocument>())
                {
                    List<string> mapped = new List<string>();
                    if (string.Equals(joint.Type, "floating", StringComparison.Ordinal))
                    {
                        foreach (string suffix in new[] { "tx", "ty", "tz", "rotation" })
                        {
                            mapped.Add(joints.Allocate(
                                joint.Name + "_" + suffix,
                                (joint.Id ?? joint.Name) + "\n" + suffix));
                        }
                    }
                    else if (!string.Equals(joint.Type, "fixed", StringComparison.Ordinal))
                    {
                        mapped.Add(joints.Allocate(joint.Name, joint.Id ?? joint.Name));
                    }
                    result.JointNames.Add(joint.Name, mapped);
                }
                foreach (LinkDocument link in robot.Links ?? new List<LinkDocument>())
                {
                    int index = 0;
                    foreach (VisualDocument visual in link.Visuals ?? Enumerable.Empty<VisualDocument>())
                    {
                        string identity = GeometryIdentity(link.Name, true, visual.Name, index);
                        result.VisualNames.Add(
                            identity,
                            visuals.Allocate(
                                result.LinkNames[link.Name] + "_" +
                                    (string.IsNullOrWhiteSpace(visual.Name) ? "visual_" + index : visual.Name),
                                identity));
                        index++;
                    }
                    index = 0;
                    foreach (CollisionDocument collision in link.Collisions ?? Enumerable.Empty<CollisionDocument>())
                    {
                        string identity = GeometryIdentity(link.Name, false, collision.Name, index);
                        result.CollisionNames.Add(
                            identity,
                            collisions.Allocate(
                                result.LinkNames[link.Name] + "_" +
                                    (string.IsNullOrWhiteSpace(collision.Name) ? "collision_" + index : collision.Name),
                                identity));
                        index++;
                    }
                }
                return result;
            }
        }

        private sealed class NameAllocator
        {
            private readonly ISet<string> used = new HashSet<string>(StringComparer.Ordinal);

            public string Allocate(string preferred, string identity)
            {
                string candidate = SafeName(preferred, "item");
                if (used.Add(candidate)) return candidate;
                string suffix = ShortHash(identity ?? preferred);
                candidate += "_" + suffix;
                int counter = 2;
                while (!used.Add(candidate))
                {
                    candidate = SafeName(preferred, "item") + "_" + suffix + "_" +
                        counter.ToString(CultureInfo.InvariantCulture);
                    counter++;
                }
                return candidate;
            }
        }

        private sealed class OfficialCompilationReport
        {
            public string Status { get; set; }

            public string Validator { get; set; }

            public string MuJoCoVersion { get; set; }

            public string Message { get; set; }
        }

        public static string GetRobotDirectoryName(string robotName)
        {
            return SafeName(robotName, "robot");
        }

        private static string SafeName(string value, string fallback)
        {
            string result = Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9_.-]", "_");
            result = Regex.Replace(result, "_+", "_").Trim('_', '.', '-');
            if (string.IsNullOrWhiteSpace(result)) result = fallback;
            if (IsWindowsReservedName(result)) result += "_item";
            return result;
        }

        private static string SafeFileName(string value)
        {
            string extension = Path.GetExtension(value ?? string.Empty);
            string stem = Path.GetFileNameWithoutExtension(value ?? string.Empty);
            string result = SafeName(stem, "asset") + extension;
            if (result.EndsWith(" ", StringComparison.Ordinal) || result.EndsWith(".", StringComparison.Ordinal))
            {
                result = result.TrimEnd(' ', '.');
            }
            return result;
        }

        private static bool IsWindowsReservedName(string value)
        {
            string stem = (value ?? string.Empty).Split('.')[0];
            if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
            return stem.Length == 4 &&
                (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                stem[3] >= '1' && stem[3] <= '9';
        }

        private static string ShortHash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                StringBuilder builder = new StringBuilder(12);
                for (int index = 0; index < 6; index++)
                {
                    builder.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }
    }
}
