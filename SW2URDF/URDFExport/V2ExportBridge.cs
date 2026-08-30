using OSURDF.Core.Bundle;
using OSURDF.Core.Export;
using OSURDF.Core.Model;
using OSURDF.Core.Serialization;
using OSURDF.Core.Urdf;
using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LegacyJoint = SW2URDF.URDF.Joint;
using LegacyLink = SW2URDF.URDF.Link;
using LegacyRobot = SW2URDF.URDF.Robot;

namespace SW2URDF.URDFExport
{
    internal sealed class V2ExportResult
    {
        public string BundleDirectory { get; set; }
        public string RetainedPreviousBundleDirectory { get; set; }
        public string Ros1Directory { get; set; }
        public string Ros2Directory { get; set; }
        public IList<ExportHelper.MeshExportRecord> DeliveryMeshRecords { get; set; } =
            new List<ExportHelper.MeshExportRecord>();
    }

    internal static class V2ExportBridge
    {
        public static V2ExportResult Export(
            URDFPackage sourcePackage,
            URDFPackage outputPackage,
            string sourceUrdf,
            LegacyRobot legacyRobot,
            IEnumerable<ExportHelper.MeshExportRecord> meshRecords,
            ExportTargetOptions options)
        {
            if (sourcePackage == null) throw new ArgumentNullException("sourcePackage");
            if (outputPackage == null) throw new ArgumentNullException("outputPackage");
            if (legacyRobot == null) throw new ArgumentNullException("legacyRobot");
            if (options == null) throw new ArgumentNullException("options");

            IList<string> optionErrors = options.Validate();
            if (optionErrors.Count > 0)
            {
                throw new InvalidDataException(string.Join(Environment.NewLine, optionErrors));
            }

            RobotDocument robot = UrdfCodec.Read(sourceUrdf);
            robot.Metadata.Generator = "SolidWorks URDF Exporter Pro";
            robot.Metadata.GeneratorVersion = Versioning.Version.GetPluginVersion();
            robot.Metadata.Commit = Versioning.Version.GetCommitHash();
            robot.Metadata.SourceFormat = "solidworks-assembly";
            robot.Metadata.ModelLicense = options.ModelLicense;
            robot.Metadata.ModelAuthor = options.ModelAuthor;
            ApplyProvenance(robot, legacyRobot);

            robot.Profiles.Package = new PackageMetadataProfile
            {
                PackageName = outputPackage.PackageName,
                Version = options.PackageVersion,
                Description = options.Description,
                MaintainerName = options.MaintainerName,
                MaintainerEmail = options.MaintainerEmail,
                License = options.ModelLicense
            };
            robot.Profiles.Ros1.Enabled = options.ExportRos1Legacy;
            robot.Profiles.Ros2.Enabled = options.ExportRos2;
            robot.Profiles.Ros2.Distribution = options.Ros2Distribution;
            robot.Profiles.Ros2.GazeboDistribution = options.GazeboDistribution;
            robot.Profiles.Ros2.ModernGazebo = true;
            if (!string.IsNullOrWhiteSpace(options.Ros2ControlProfileFile))
            {
                Ros2ControlProfile control = ReadStrictProfile<Ros2ControlProfile>(
                    options.Ros2ControlProfileFile,
                    "ros2_control");
                if (control == null)
                {
                    throw new InvalidDataException("ros2_control profile JSON is empty.");
                }
                control.Enabled = true;
                robot.Profiles.Ros2.Ros2Control = control;
            }
            robot.Profiles.Isaac.Enabled = options.ExportIsaacSim;
            robot.Profiles.Isaac.IsaacSimVersion = NullIfBlank(options.IsaacSimVersion);
            robot.Profiles.IsaacLab.Enabled = options.ExportIsaacLab;
            robot.Profiles.IsaacLab.IsaacLabVersion = NullIfBlank(options.IsaacLabVersion);
            if (options.ExportIsaacLab)
            {
                IsaacLabProfile profile = ReadStrictProfile<IsaacLabProfile>(
                    options.IsaacLabProfileFile,
                    "Isaac Lab actuator");
                if (profile == null)
                {
                    throw new InvalidDataException("Isaac Lab actuator profile JSON is empty.");
                }
                profile.Enabled = true;
                profile.IsaacLabVersion = options.IsaacLabVersion;
                robot.Profiles.IsaacLab = profile;
            }

            Dictionary<string, string> packageMappings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { sourcePackage.PackageName, sourcePackage.WindowsPackageDirectory }
            };
            List<BundleAdditionalFile> supplementalFiles = new List<BundleAdditionalFile>();
            AddSupplementalFile(
                supplementalFiles,
                Path.Combine(sourcePackage.WindowsConfigDirectory, "inertial_validation.csv"),
                "reports/cad/inertial_validation.csv");
            string portableMeshManifest = Path.Combine(
                sourcePackage.WindowsConfigDirectory,
                ".osurdf-portable-mesh-manifest-" + Guid.NewGuid().ToString("N") + ".csv");
            BundleBuildResult bundle;
            Exception buildFailure = null;
            try
            {
                File.WriteAllText(
                    portableMeshManifest,
                    BuildPortableMeshManifest(meshRecords, sourcePackage),
                    new UTF8Encoding(false));
                AddSupplementalFile(
                    supplementalFiles,
                    portableMeshManifest,
                    "reports/cad/mesh_manifest.csv");

                bundle = new RobotBundleBuilder().Build(
                    robot,
                    new BundleBuildOptions
                    {
                        SourceUrdfPath = sourceUrdf,
                        OutputDirectory = outputPackage.WindowsBundleDirectory,
                        Overwrite = true,
                        PackageMappings = packageMappings,
                        AdditionalFiles = supplementalFiles
                    });
            }
            catch (Exception exception)
            {
                buildFailure = exception;
                throw;
            }
            finally
            {
                if (File.Exists(portableMeshManifest))
                {
                    try
                    {
                        File.Delete(portableMeshManifest);
                    }
                    catch (Exception cleanupFailure) when (
                        buildFailure != null &&
                        (cleanupFailure is IOException || cleanupFailure is UnauthorizedAccessException))
                    {
                        buildFailure.Data["portableMeshManifestCleanup"] = cleanupFailure.Message;
                    }
                }
            }

            V2ExportResult result = new V2ExportResult
            {
                BundleDirectory = bundle.OutputDirectory,
                RetainedPreviousBundleDirectory = bundle.RetainedPreviousDirectory
            };
            RosPackageExporter exporter = new RosPackageExporter();
            if (options.ExportRos1Legacy)
            {
                result.Ros1Directory = exporter.ExportRos1(new RosExportOptions
                {
                    BundleDirectory = bundle.OutputDirectory,
                    OutputDirectory = Path.Combine(outputPackage.WindowsExportRootDirectory, "ROS1"),
                    Overwrite = true
                });
            }
            if (options.ExportRos2)
            {
                result.Ros2Directory = exporter.ExportRos2(new RosExportOptions
                {
                    BundleDirectory = bundle.OutputDirectory,
                    OutputDirectory = Path.Combine(outputPackage.WindowsExportRootDirectory, "ROS2"),
                    Overwrite = true
                });
            }
            result.DeliveryMeshRecords = BuildDeliveryMeshRecords(
                bundle.OutputDirectory,
                result,
                meshRecords,
                outputPackage.PackageName);
            return result;
        }

        public static void RefreshRosChecksums(V2ExportResult result)
        {
            if (result == null)
            {
                return;
            }
            if (!string.IsNullOrWhiteSpace(result.Ros1Directory))
            {
                RosPackageExporter.RefreshChecksums(result.Ros1Directory);
            }
            if (!string.IsNullOrWhiteSpace(result.Ros2Directory))
            {
                RosPackageExporter.RefreshChecksums(result.Ros2Directory);
            }
        }

        private static IList<ExportHelper.MeshExportRecord> BuildDeliveryMeshRecords(
            string bundleDirectory,
            V2ExportResult result,
            IEnumerable<ExportHelper.MeshExportRecord> records,
            string packageName)
        {
            RobotDocument bundledRobot = RobotJson.Read(
                Path.Combine(bundleDirectory, RobotBundleLayout.RobotJsonFile));
            Dictionary<string, LinkDocument> links = bundledRobot.Links
                .Where(link => link != null)
                .ToDictionary(link => link.Name, StringComparer.Ordinal);
            bool packageUri = !string.IsNullOrWhiteSpace(result.Ros1Directory) ||
                !string.IsNullOrWhiteSpace(result.Ros2Directory);
            string deliveryRoot = !string.IsNullOrWhiteSpace(result.Ros1Directory)
                ? result.Ros1Directory
                : !string.IsNullOrWhiteSpace(result.Ros2Directory)
                    ? result.Ros2Directory
                    : bundleDirectory;
            List<ExportHelper.MeshExportRecord> mapped =
                new List<ExportHelper.MeshExportRecord>();
            foreach (ExportHelper.MeshExportRecord record in
                records ?? Enumerable.Empty<ExportHelper.MeshExportRecord>())
            {
                LinkDocument link;
                if (!links.TryGetValue(record.LinkName, out link))
                {
                    throw new InvalidDataException(
                        "The canonical Robot Bundle has no link for exported mesh evidence: " +
                        record.LinkName);
                }
                GeometryDocument visual = (link.Visuals ?? new List<VisualDocument>())
                    .Where(item => item != null && item.Geometry != null)
                    .Select(item => item.Geometry)
                    .FirstOrDefault(IsMeshGeometry);
                GeometryDocument collision = (link.Collisions ?? new List<CollisionDocument>())
                    .Where(item => item != null && item.Geometry != null)
                    .Select(item => item.Geometry)
                    .FirstOrDefault(IsMeshGeometry);
                string visualRelative = visual == null ? null : visual.Uri;
                string collisionRelative = collision == null ? null : collision.Uri;
                string visualPath = DeliveryAssetPath(deliveryRoot, visualRelative);
                string collisionPath = DeliveryAssetPath(deliveryRoot, collisionRelative);
                bool visualExists = !string.IsNullOrWhiteSpace(visualPath) && File.Exists(visualPath);
                bool collisionExists = !string.IsNullOrWhiteSpace(collisionPath) && File.Exists(collisionPath);
                mapped.Add(new ExportHelper.MeshExportRecord(
                    record.LinkName,
                    record.CollisionStrategy,
                    record.CollisionEffectiveStrategy,
                    record.CollisionGeometryType,
                    record.CollisionNotes,
                    record.MeshFormat,
                    DeliveryAssetUri(visualRelative, packageName, packageUri),
                    DeliveryAssetUri(collisionRelative, packageName, packageUri),
                    visualPath,
                    collisionPath,
                    visualExists,
                    collisionExists,
                    visualExists ? (long?)new FileInfo(visualPath).Length : null,
                    collisionExists ? (long?)new FileInfo(collisionPath).Length : null,
                    record.VisualTriangles,
                    record.CollisionTriangles,
                    record.StlStats,
                    record.CollisionUrdfReference));
            }
            return mapped;
        }

        private static bool IsMeshGeometry(GeometryDocument geometry)
        {
            return geometry != null &&
                string.Equals(geometry.Type, "mesh", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(geometry.Uri);
        }

        private static string DeliveryAssetUri(string relative, string packageName, bool packageUri)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                return string.Empty;
            }
            return packageUri ? "package://" + packageName + "/" + relative : relative;
        }

        private static string DeliveryAssetPath(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                return string.Empty;
            }
            string normalized = relative.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.Split('/').Any(segment =>
                    segment.Length == 0 || segment == "." || segment == ".."))
            {
                throw new InvalidDataException("Unsafe canonical bundle asset path: " + relative);
            }
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(
                fullRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Canonical bundle asset escapes its delivery root: " + relative);
            }
            return path;
        }

        private static string BuildPortableMeshManifest(
            IEnumerable<ExportHelper.MeshExportRecord> records,
            URDFPackage sourcePackage)
        {
            List<ExportHelper.MeshExportRecord> portableRecords =
                new List<ExportHelper.MeshExportRecord>();
            foreach (ExportHelper.MeshExportRecord record in
                records ?? Enumerable.Empty<ExportHelper.MeshExportRecord>())
            {
                portableRecords.Add(new ExportHelper.MeshExportRecord(
                    record.LinkName,
                    record.CollisionStrategy,
                    record.CollisionEffectiveStrategy,
                    record.CollisionGeometryType,
                    record.CollisionNotes,
                    record.MeshFormat,
                    record.VisualUri,
                    record.CollisionUri,
                    PortableEvidencePath(record.VisualWindowsPath, sourcePackage),
                    PortableEvidencePath(record.CollisionWindowsPath, sourcePackage),
                    record.VisualExists,
                    record.CollisionExists,
                    record.VisualBytes,
                    record.CollisionBytes,
                    record.VisualTriangles,
                    record.CollisionTriangles,
                    record.StlStats,
                    record.CollisionUrdfReference));
            }
            return ExportHelper.BuildMeshManifestCsv(portableRecords);
        }

        private static string PortableEvidencePath(string path, URDFPackage sourcePackage)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            string root = Path.GetFullPath(sourcePackage.WindowsPackageDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return "external-path-redacted";
            }
            string relative = fullPath.Substring(root.Length).Replace('\\', '/');
            return "package://" + sourcePackage.PackageName + "/" + relative;
        }

        private static void AddSupplementalFile(
            ICollection<BundleAdditionalFile> files,
            string sourcePath,
            string bundlePath)
        {
            if (File.Exists(sourcePath))
            {
                files.Add(new BundleAdditionalFile
                {
                    SourcePath = sourcePath,
                    BundlePath = bundlePath,
                    Role = "cad-validation-report"
                });
            }
        }

        private static void ApplyProvenance(RobotDocument robot, LegacyRobot legacyRobot)
        {
            Dictionary<string, LegacyJoint> joints = new Dictionary<string, LegacyJoint>(StringComparer.Ordinal);
            CollectJoints(legacyRobot.BaseLink, joints);
            foreach (JointDocument target in robot.Joints)
            {
                LegacyJoint source;
                if (!joints.TryGetValue(target.Name, out source))
                {
                    target.Source = new SourceProvenance
                    {
                        Kind = "legacy_configuration",
                        Evidence = "No matching SolidWorks joint metadata was found.",
                        UserConfirmed = false
                    };
                    continue;
                }
                target.Source = new SourceProvenance
                {
                    Kind = string.IsNullOrWhiteSpace(source.ConfigurationSource)
                        ? "legacy_configuration"
                        : source.ConfigurationSource,
                    Evidence = source.ConfigurationEvidence,
                    Reference = BuildReference(source),
                    UserConfirmed = source.ConfigurationUserConfirmed
                };
            }
            foreach (LinkDocument link in robot.Links)
            {
                link.Source = new SourceProvenance
                {
                    Kind = "solidworks_components",
                    Evidence = "Geometry and mass properties were exported from the configured SolidWorks Link.",
                    UserConfirmed = true
                };
            }
        }

        private static void CollectJoints(LegacyLink link, IDictionary<string, LegacyJoint> joints)
        {
            if (link == null) return;
            if (link.Parent != null && link.Joint != null && !string.IsNullOrWhiteSpace(link.Joint.Name))
            {
                joints[link.Joint.Name] = link.Joint;
            }
            foreach (LegacyLink child in link.Children)
            {
                CollectJoints(child, joints);
            }
        }

        private static string BuildReference(LegacyJoint joint)
        {
            List<string> references = new List<string>();
            if (!string.IsNullOrWhiteSpace(joint.CoordinateSystemName))
            {
                references.Add("coordinateSystem=" + joint.CoordinateSystemName);
            }
            if (!string.IsNullOrWhiteSpace(joint.AxisName))
            {
                references.Add("axis=" + joint.AxisName);
            }
            return references.Count == 0 ? null : string.Join(";", references);
        }

        private static string NullIfBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static T ReadStrictProfile<T>(string path, string label) where T : class
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                object profile;
                if (typeof(T) == typeof(Ros2ControlProfile))
                {
                    profile = RobotJson.DeserializeRos2ControlProfile(json);
                }
                else if (typeof(T) == typeof(IsaacLabProfile))
                {
                    profile = RobotJson.DeserializeIsaacLabProfile(json);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Unsupported strict profile type: " + typeof(T).FullName);
                }
                return (T)profile;
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(label + " profile JSON does not match its schema.", exception);
            }
        }
    }
}
