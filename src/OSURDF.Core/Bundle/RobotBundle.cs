using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OSURDF.Core.Model;
using OSURDF.Core.Serialization;
using OSURDF.Core.Urdf;
using OSURDF.Core.Validation;

namespace OSURDF.Core.Bundle
{
    public static class RobotBundleLayout
    {
        public const int ManifestSchemaVersion = 1;
        public const string ManifestFile = "manifest.json";
        public const string ChecksumsFile = "checksums.sha256";
        public const string RobotJsonFile = "robot.json";
        public const string PortableUrdfFile = "robot.urdf";
        public const string ValidationJsonFile = "reports/validation.json";
        public const string ValidationMarkdownFile = "reports/validation.md";
    }

    public sealed class BundleBuildOptions
    {
        public string SourceUrdfPath { get; set; }
        public string OutputDirectory { get; set; }
        public bool Overwrite { get; set; }
        public bool AllowAbsoluteAssetPaths { get; set; }
        public IDictionary<string, string> PackageMappings { get; set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public IList<BundleAdditionalFile> AdditionalFiles { get; set; } = new List<BundleAdditionalFile>();
    }

    public sealed class BundleAdditionalFile
    {
        public string SourcePath { get; set; }
        public string BundlePath { get; set; }
        public string Role { get; set; } = "source-report";
    }

    public sealed class BundleBuildResult
    {
        public string OutputDirectory { get; set; }
        public string RetainedPreviousDirectory { get; set; }
        public RobotBundleManifest Manifest { get; set; }
        public ValidationReport Validation { get; set; }
    }

    public sealed class RobotBundleManifest
    {
        [JsonProperty("schemaVersion", Order = 0)]
        public int SchemaVersion { get; set; } = RobotBundleLayout.ManifestSchemaVersion;

        [JsonProperty("bundleFormat", Order = 1)]
        public string BundleFormat { get; set; } = "osurdf-robot-bundle";

        [JsonProperty("robotSchemaVersion", Order = 2)]
        public int RobotSchemaVersion { get; set; } = RobotSchema.CurrentVersion;

        [JsonProperty("robotName", Order = 3)]
        public string RobotName { get; set; }

        [JsonProperty("createdUtc", Order = 4)]
        public string CreatedUtc { get; set; }

        [JsonProperty("reproducibleTimestamp", Order = 5)]
        public bool ReproducibleTimestamp { get; set; }

        [JsonProperty("generator", Order = 6)]
        public BundleGeneratorInfo Generator { get; set; } = new BundleGeneratorInfo();

        [JsonProperty("entrypoints", Order = 7)]
        public BundleEntrypoints Entrypoints { get; set; } = new BundleEntrypoints();

        [JsonProperty("profiles", Order = 8)]
        public BundleProfileStatus Profiles { get; set; } = new BundleProfileStatus();

        [JsonProperty("validation", Order = 9)]
        public BundleValidationSummary Validation { get; set; } = new BundleValidationSummary();

        [JsonProperty("files", Order = 10)]
        public List<BundleFileEntry> Files { get; set; } = new List<BundleFileEntry>();
    }

    public sealed class BundleGeneratorInfo
    {
        [JsonProperty("name", Order = 0)] public string Name { get; set; }
        [JsonProperty("version", Order = 1)] public string Version { get; set; }
        [JsonProperty("commit", Order = 2)] public string Commit { get; set; }
    }

    public sealed class BundleEntrypoints
    {
        [JsonProperty("robotJson", Order = 0)] public string RobotJson { get; set; } = RobotBundleLayout.RobotJsonFile;
        [JsonProperty("portableUrdf", Order = 1)] public string PortableUrdf { get; set; } = RobotBundleLayout.PortableUrdfFile;
        [JsonProperty("isaacProfile", Order = 2)] public string IsaacProfile { get; set; } = "profiles/isaac.json";
        [JsonProperty("isaacLabProfile", Order = 3)] public string IsaacLabProfile { get; set; } = "profiles/isaaclab.json";
    }

    public sealed class BundleProfileStatus
    {
        [JsonProperty("ros1", Order = 0)] public bool Ros1 { get; set; }
        [JsonProperty("ros2", Order = 1)] public bool Ros2 { get; set; }
        [JsonProperty("isaac", Order = 2)] public bool Isaac { get; set; }
        [JsonProperty("isaacLab", Order = 3)] public bool IsaacLab { get; set; }
    }

    public sealed class BundleValidationSummary
    {
        [JsonProperty("valid", Order = 0)] public bool Valid { get; set; }
        [JsonProperty("errors", Order = 1)] public int Errors { get; set; }
        [JsonProperty("warnings", Order = 2)] public int Warnings { get; set; }
        [JsonProperty("report", Order = 3)] public string Report { get; set; } = RobotBundleLayout.ValidationJsonFile;
    }

    public sealed class BundleFileEntry
    {
        [JsonProperty("path", Order = 0)] public string Path { get; set; }
        [JsonProperty("role", Order = 1)] public string Role { get; set; }
        [JsonProperty("sha256", Order = 2)] public string Sha256 { get; set; }
        [JsonProperty("bytes", Order = 3)] public long Bytes { get; set; }
        [JsonProperty("sourceUri", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
        public string SourceUri { get; set; }
    }

    public sealed class BundleVerificationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0;
        public RobotBundleManifest Manifest { get; set; }
    }

    public sealed class RobotBundleBuilder
    {
        public BundleBuildResult Build(RobotDocument input, BundleBuildOptions options)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (string.IsNullOrWhiteSpace(options.SourceUrdfPath) || !File.Exists(options.SourceUrdfPath))
            {
                throw new FileNotFoundException("The source URDF is required to resolve bundle assets.", options.SourceUrdfPath);
            }
            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                throw new ArgumentException("An output directory is required.", nameof(options));
            }

            string destination = Path.GetFullPath(options.OutputDirectory);
            string parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(parent))
            {
                throw new InvalidOperationException("Bundle output directory has no parent.");
            }
            if (File.Exists(destination))
            {
                throw new IOException("Bundle output path is an existing file: " + destination);
            }
            EnsureInputOutsideOutput(options.SourceUrdfPath, destination, "source URDF");
            Directory.CreateDirectory(parent);
            if (Directory.Exists(destination) &&
                (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Bundle output must not be a symbolic link or reparse point: " + destination);
            }
            if (Directory.Exists(destination))
            {
                EnsureNoReparsePoints(destination, "Bundle output");
            }
            if (Directory.Exists(destination) && !options.Overwrite)
            {
                throw new IOException("Bundle output already exists. Pass overwrite explicitly: " + destination);
            }

            string staging = Path.Combine(parent, ".osurdf-bundle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            string previous = null;
            string retainedPrevious = null;
            Exception buildFailure = null;
            try
            {
                RobotDocument robot = RobotJson.Clone(input);
                NormalizeJointAxes(robot);
                List<BundleFileEntry> assetEntries = MaterializeAssets(robot, options, staging);
                MaterializeAdditionalFiles(options, staging, assetEntries);
                ValidatePayloadPathInventory(assetEntries);
                ValidationReport validation = new RobotValidator().Validate(robot);
                if (!validation.IsValid)
                {
                    throw new InvalidDataException(
                        "Robot validation failed; the staged bundle was not published: " +
                        string.Join("; ", validation.Findings
                            .Where(item => item.Severity == ValidationSeverity.Error)
                            .Select(item => item.ToString())));
                }
                WritePayload(staging, robot, validation);

                RobotBundleManifest manifest = CreateManifest(robot, validation, assetEntries);
                AddGeneratedPayloadEntries(staging, manifest);
                WriteJson(Path.Combine(staging, RobotBundleLayout.ManifestFile), manifest);
                WriteChecksums(staging);

                BundleVerificationResult stagedVerification = new RobotBundleVerifier().Verify(staging);
                if (!stagedVerification.IsValid)
                {
                    throw new InvalidDataException(
                        "Generated Robot Bundle did not verify: " + string.Join("; ", stagedVerification.Errors));
                }

                if (Directory.Exists(destination))
                {
                    previous = destination + ".previous-" + Guid.NewGuid().ToString("N");
                    Directory.Move(destination, previous);
                }
                Directory.Move(staging, destination);
                staging = null;
                if (previous != null)
                {
                    try
                    {
                        Directory.Delete(previous, true);
                    }
                    catch (Exception cleanupFailure) when (
                        cleanupFailure is IOException || cleanupFailure is UnauthorizedAccessException)
                    {
                        // Publication has already succeeded. Preserve the prior verified bundle as
                        // a recovery copy and surface its exact location to the caller.
                        retainedPrevious = Directory.Exists(previous) ? previous : null;
                    }
                    previous = null;
                }
                return new BundleBuildResult
                {
                    OutputDirectory = destination,
                    RetainedPreviousDirectory = retainedPrevious,
                    Manifest = manifest,
                    Validation = validation
                };
            }
            catch (Exception exception)
            {
                buildFailure = exception;
                if (previous != null && !Directory.Exists(destination) && Directory.Exists(previous))
                {
                    try
                    {
                        Directory.Move(previous, destination);
                        previous = null;
                    }
                    catch (Exception recoveryFailure) when (
                        recoveryFailure is IOException || recoveryFailure is UnauthorizedAccessException)
                    {
                        exception.Data["bundleRecoveryFailure"] = recoveryFailure.Message;
                        exception.Data["bundleRecoveryDirectory"] = previous;
                    }
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
                    catch (Exception cleanupFailure) when (
                        buildFailure != null &&
                        (cleanupFailure is IOException || cleanupFailure is UnauthorizedAccessException))
                    {
                        buildFailure.Data["bundleStagingCleanupFailure"] = cleanupFailure.Message;
                        buildFailure.Data["bundleStagingDirectory"] = staging;
                    }
                }
                // Never delete a retained previous directory from the failure path. If publication
                // or cleanup failed, it is the only recovery copy of the caller's prior Bundle.
            }
        }

        private static List<BundleFileEntry> MaterializeAssets(
            RobotDocument robot,
            BundleBuildOptions options,
            string staging)
        {
            List<BundleFileEntry> entries = new List<BundleFileEntry>();
            Dictionary<string, string> copiedByRoleAndDigest = new Dictionary<string, string>(StringComparer.Ordinal);
            string sourceUrdf = Path.GetFullPath(options.SourceUrdfPath);
            foreach (LinkDocument link in robot.Links ?? new List<LinkDocument>())
            {
                if (link == null)
                {
                    continue;
                }
                foreach (VisualDocument visual in link.Visuals ?? Enumerable.Empty<VisualDocument>())
                {
                    if (visual == null)
                    {
                        continue;
                    }
                    RewriteGeometryAsset(visual.Geometry, "visual-mesh", "meshes/visual", sourceUrdf, options, staging, entries, copiedByRoleAndDigest);
                    if (visual.Material != null && !string.IsNullOrWhiteSpace(visual.Material.TextureUri))
                    {
                        visual.Material.TextureUri = CopyAsset(
                            visual.Material.TextureUri,
                            "texture",
                            "textures",
                            sourceUrdf,
                            options,
                            staging,
                            entries,
                            copiedByRoleAndDigest);
                    }
                }
                foreach (CollisionDocument collision in link.Collisions ?? Enumerable.Empty<CollisionDocument>())
                {
                    if (collision == null)
                    {
                        continue;
                    }
                    RewriteGeometryAsset(collision.Geometry, "collision-mesh", "meshes/collision", sourceUrdf, options, staging, entries, copiedByRoleAndDigest);
                }
            }
            return entries;
        }

        private static void NormalizeJointAxes(RobotDocument robot)
        {
            foreach (JointDocument joint in robot.Joints ?? new List<JointDocument>())
            {
                if (joint == null || joint.Axis == null ||
                    !RobotSchema.MovingJointTypes.Contains(joint.Type ?? string.Empty))
                {
                    continue;
                }
                double magnitude = Math.Sqrt(joint.Axis.SquaredMagnitude());
                if (double.IsNaN(magnitude) || double.IsInfinity(magnitude) || magnitude <= 1e-12)
                {
                    continue;
                }
                joint.Axis = new Vector3Document
                {
                    X = joint.Axis.X / magnitude,
                    Y = joint.Axis.Y / magnitude,
                    Z = joint.Axis.Z / magnitude
                };
            }
        }

        private static void MaterializeAdditionalFiles(
            BundleBuildOptions options,
            string staging,
            ICollection<BundleFileEntry> entries)
        {
            foreach (BundleAdditionalFile item in options.AdditionalFiles ?? new List<BundleAdditionalFile>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.SourcePath) ||
                    string.IsNullOrWhiteSpace(item.BundlePath))
                {
                    throw new InvalidDataException("Every additional Bundle file needs a source and destination path.");
                }
                string source = Path.GetFullPath(item.SourcePath);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException("Additional Bundle file does not exist.", source);
                }
                EnsureInputOutsideOutput(source, options.OutputDirectory, "additional input");
                if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Symbolic-link additional files are not accepted: " + source);
                }
                string relative = NormalizeRelativePath(item.BundlePath);
                string destination = SafeBundlePath(staging, relative);
                if (File.Exists(destination))
                {
                    throw new InvalidDataException("Additional Bundle file collides with an existing payload: " + relative);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(source, destination, false);
                entries.Add(new BundleFileEntry
                {
                    Path = relative,
                    Role = string.IsNullOrWhiteSpace(item.Role) ? "source-report" : item.Role,
                    Sha256 = Sha256File(destination),
                    Bytes = new FileInfo(destination).Length,
                    SourceUri = "generated:" + Path.GetFileName(source)
                });
            }
        }

        private static void RewriteGeometryAsset(
            GeometryDocument geometry,
            string role,
            string targetDirectory,
            string sourceUrdf,
            BundleBuildOptions options,
            string staging,
            ICollection<BundleFileEntry> entries,
            IDictionary<string, string> copiedByRoleAndDigest)
        {
            if (geometry == null || !string.Equals(geometry.Type, "mesh", StringComparison.Ordinal))
            {
                return;
            }
            geometry.Uri = CopyAsset(
                geometry.Uri,
                role,
                targetDirectory,
                sourceUrdf,
                options,
                staging,
                entries,
                copiedByRoleAndDigest);
        }

        private static string CopyAsset(
            string uri,
            string role,
            string targetDirectory,
            string sourceUrdf,
            BundleBuildOptions options,
            string staging,
            ICollection<BundleFileEntry> entries,
            IDictionary<string, string> copiedByRoleAndDigest)
        {
            string sourcePath = ResolveAsset(uri, sourceUrdf, options);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Referenced asset does not exist: " + uri, sourcePath);
            }
            EnsureInputOutsideOutput(sourcePath, options.OutputDirectory, "referenced asset");
            if ((File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Symbolic-link assets are not accepted in Robot Bundles: " + uri);
            }

            string digest = Sha256File(sourcePath);
            string dedupeKey = role + "\n" + digest;
            string existing;
            if (copiedByRoleAndDigest.TryGetValue(dedupeKey, out existing))
            {
                return existing;
            }

            string fileName = SanitizeFileName(Path.GetFileName(sourcePath));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "asset";
            }
            string relativePath = NormalizeRelativePath(Path.Combine(targetDirectory, fileName));
            string destination = SafeBundlePath(staging, relativePath);
            if (File.Exists(destination))
            {
                string name = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                relativePath = NormalizeRelativePath(Path.Combine(targetDirectory, name + "-" + digest.Substring(0, 12) + extension));
                destination = SafeBundlePath(staging, relativePath);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(sourcePath, destination, false);

            copiedByRoleAndDigest[dedupeKey] = relativePath;
            entries.Add(new BundleFileEntry
            {
                Path = relativePath,
                Role = role,
                Sha256 = digest,
                Bytes = new FileInfo(destination).Length,
                SourceUri = RedactSourceUri(uri)
            });
            return relativePath;
        }

        private static string ResolveAsset(string uri, string sourceUrdf, BundleBuildOptions options)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                throw new InvalidDataException("Asset URI is empty.");
            }
            string normalized = uri.Replace('\\', '/');
            if (normalized.StartsWith("package://", StringComparison.OrdinalIgnoreCase))
            {
                string packageValue = normalized.Substring("package://".Length);
                int slash = packageValue.IndexOf('/');
                if (slash <= 0 || slash == packageValue.Length - 1)
                {
                    throw new InvalidDataException("Invalid package URI: " + uri);
                }
                string packageName = packageValue.Substring(0, slash);
                string packageRelative = packageValue.Substring(slash + 1);
                EnsureSafeRelative(packageRelative, uri);
                string packageRoot;
                if (!TryGetPackageRoot(options.PackageMappings, packageName, out packageRoot))
                {
                    packageRoot = InferPackageRoot(sourceUrdf, packageName, packageRelative);
                }
                if (packageRoot == null)
                {
                    throw new InvalidDataException(
                        "No package mapping for '" + packageName + "'. Add --package-map " + packageName + "=<path>.");
                }
                return ResolveInsideRoot(packageRoot, packageRelative, uri);
            }
            if (normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || IsAbsolutePathSyntax(normalized))
            {
                if (!options.AllowAbsoluteAssetPaths)
                {
                    throw new InvalidDataException("Absolute asset paths require explicit opt-in: " + RedactSourceUri(uri));
                }
                Uri fileUri;
                string value = Uri.TryCreate(uri, UriKind.Absolute, out fileUri) && fileUri.IsFile
                    ? fileUri.LocalPath
                    : uri;
                if (!Path.IsPathRooted(value))
                {
                    throw new InvalidDataException(
                        "Absolute asset path uses syntax that is not native to this host: " + RedactSourceUri(uri));
                }
                return Path.GetFullPath(value);
            }

            EnsureSafeRelative(normalized, uri, allowParentSegments: options.AllowAbsoluteAssetPaths);
            string sourceRoot = AppendDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(sourceUrdf)));
            string relativePath = Path.GetFullPath(Path.Combine(sourceRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!options.AllowAbsoluteAssetPaths && !relativePath.StartsWith(sourceRoot, PathComparison))
            {
                throw new InvalidDataException("Asset escapes the source URDF directory: " + uri);
            }
            if (relativePath.StartsWith(sourceRoot, PathComparison))
            {
                EnsureNoReparsePointInPath(sourceRoot, relativePath, uri);
            }
            return relativePath;
        }

        private static bool TryGetPackageRoot(IDictionary<string, string> mappings, string packageName, out string root)
        {
            root = null;
            if (mappings == null)
            {
                return false;
            }
            foreach (KeyValuePair<string, string> pair in mappings)
            {
                if (string.Equals(pair.Key, packageName, StringComparison.Ordinal))
                {
                    root = Path.GetFullPath(pair.Value);
                    return true;
                }
            }
            return false;
        }

        private static string InferPackageRoot(string sourceUrdf, string packageName, string packageRelative)
        {
            DirectoryInfo cursor = new DirectoryInfo(Path.GetDirectoryName(sourceUrdf));
            for (int depth = 0; cursor != null && depth < 4; depth++, cursor = cursor.Parent)
            {
                string candidate = Path.Combine(cursor.FullName, packageRelative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate) &&
                    (string.Equals(cursor.Name, packageName, StringComparison.Ordinal) || depth <= 1))
                {
                    return cursor.FullName;
                }
            }
            return null;
        }

        private static string ResolveInsideRoot(string root, string relative, string displayUri)
        {
            string fullRoot = AppendDirectorySeparator(Path.GetFullPath(root));
            string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(fullRoot, PathComparison))
            {
                throw new InvalidDataException("Asset escapes its configured package root: " + displayUri);
            }
            EnsureNoReparsePointInPath(fullRoot, fullPath, displayUri);
            return fullPath;
        }

        private static void EnsureNoReparsePointInPath(string fullRoot, string fullPath, string displayUri)
        {
            string root = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Asset package root must not be a symbolic link or reparse point: " + displayUri);
            }
            string relative = fullPath.Substring(fullRoot.Length);
            string current = root;
            foreach (string segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Asset path contains a symbolic link or reparse point: " + displayUri);
                }
            }
        }

        private static void EnsureSafeRelative(string value, string displayUri, bool allowParentSegments = false)
        {
            string normalized = value.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.IndexOf('\0') >= 0)
            {
                throw new InvalidDataException("Unsafe asset URI: " + displayUri);
            }
            if (!allowParentSegments && normalized.Split('/').Any(segment => segment == ".."))
            {
                throw new InvalidDataException("Asset URI traverses outside its package: " + displayUri);
            }
        }

        private static bool IsAbsolutePathSyntax(string value)
        {
            return Path.IsPathRooted(value) ||
                value.StartsWith("//", StringComparison.Ordinal) ||
                (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':');
        }

        private static void EnsureInputOutsideOutput(
            string inputPath,
            string outputDirectory,
            string inputDescription)
        {
            string input = Path.GetFullPath(inputPath);
            string output = Path.GetFullPath(outputDirectory);
            string outputPrefix = AppendDirectorySeparator(output);
            if (string.Equals(input, output, PathComparison) ||
                input.StartsWith(outputPrefix, PathComparison))
            {
                throw new InvalidDataException(
                    "Bundle output directory must not contain the " +
                    inputDescription + ": " + input);
            }
        }

        private static void WritePayload(string staging, RobotDocument robot, ValidationReport validation)
        {
            RobotJson.Write(Path.Combine(staging, RobotBundleLayout.RobotJsonFile), robot, false);
            UrdfCodec.Write(Path.Combine(staging, RobotBundleLayout.PortableUrdfFile), robot, true);
            JObject canonicalRobot = JObject.Parse(RobotJson.Serialize(robot));
            JObject profiles = (JObject)canonicalRobot["profiles"];
            WriteJson(Path.Combine(staging, "profiles/package.json"), profiles["package"]);
            WriteJson(Path.Combine(staging, "profiles/ros1.json"), profiles["ros1"]);
            WriteJson(Path.Combine(staging, "profiles/ros2.json"), profiles["ros2"]);
            WriteJson(Path.Combine(staging, "profiles/isaac.json"), profiles["isaac"]);
            WriteJson(Path.Combine(staging, "profiles/isaaclab.json"), profiles["isaacLab"]);
            WriteJson(Path.Combine(staging, RobotBundleLayout.ValidationJsonFile), new
            {
                valid = validation.IsValid,
                errors = validation.ErrorCount,
                warnings = validation.WarningCount,
                findings = validation.Findings.Select(item => new
                {
                    severity = item.Severity.ToString().ToLowerInvariant(),
                    code = item.Code,
                    path = item.Path,
                    message = item.Message
                }).ToList()
            });
            WriteUtf8(Path.Combine(staging, RobotBundleLayout.ValidationMarkdownFile), ValidationMarkdown(validation));
        }

        private static RobotBundleManifest CreateManifest(
            RobotDocument robot,
            ValidationReport validation,
            IEnumerable<BundleFileEntry> assets)
        {
            bool reproducible;
            DateTimeOffset timestamp = BuildTimestamp(out reproducible);
            RobotBundleManifest manifest = new RobotBundleManifest
            {
                RobotName = robot.Name,
                CreatedUtc = timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ReproducibleTimestamp = reproducible,
                Generator = new BundleGeneratorInfo
                {
                    Name = robot.Metadata?.Generator ?? "OSURDF",
                    Version = robot.Metadata?.GeneratorVersion ?? "unknown",
                    Commit = robot.Metadata?.Commit ?? "unknown"
                },
                Profiles = new BundleProfileStatus
                {
                    Ros1 = robot.Profiles?.Ros1?.Enabled == true,
                    Ros2 = robot.Profiles?.Ros2?.Enabled == true,
                    Isaac = robot.Profiles?.Isaac?.Enabled == true,
                    IsaacLab = robot.Profiles?.IsaacLab?.Enabled == true
                },
                Validation = new BundleValidationSummary
                {
                    Valid = validation.IsValid,
                    Errors = validation.ErrorCount,
                    Warnings = validation.WarningCount
                }
            };
            manifest.Files.AddRange(assets.OrderBy(item => item.Path, StringComparer.Ordinal));
            return manifest;
        }

        private static void ValidatePayloadPathInventory(IEnumerable<BundleFileEntry> entries)
        {
            HashSet<string> reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                RobotBundleLayout.ManifestFile,
                RobotBundleLayout.ChecksumsFile,
                RobotBundleLayout.RobotJsonFile,
                RobotBundleLayout.PortableUrdfFile,
                "profiles/package.json",
                "profiles/ros1.json",
                "profiles/ros2.json",
                "profiles/isaac.json",
                "profiles/isaaclab.json",
                RobotBundleLayout.ValidationJsonFile,
                RobotBundleLayout.ValidationMarkdownFile
            };
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BundleFileEntry entry in entries ?? Enumerable.Empty<BundleFileEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path))
                {
                    throw new InvalidDataException("Bundle payload entries require a path.");
                }
                ValidatePortableBundleRelativePath(entry.Path);
                if (reserved.Contains(entry.Path))
                {
                    throw new InvalidDataException("Bundle payload collides with a reserved generated path: " + entry.Path);
                }
                if (!used.Add(entry.Path))
                {
                    throw new InvalidDataException("Bundle payload paths must be unique across case-insensitive filesystems: " + entry.Path);
                }
            }
        }

        private static void AddGeneratedPayloadEntries(string staging, RobotBundleManifest manifest)
        {
            string[] generated =
            {
                RobotBundleLayout.RobotJsonFile,
                RobotBundleLayout.PortableUrdfFile,
                "profiles/package.json",
                "profiles/ros1.json",
                "profiles/ros2.json",
                "profiles/isaac.json",
                "profiles/isaaclab.json",
                RobotBundleLayout.ValidationJsonFile,
                RobotBundleLayout.ValidationMarkdownFile
            };
            foreach (string relative in generated)
            {
                string fullPath = SafeBundlePath(staging, relative);
                manifest.Files.Add(new BundleFileEntry
                {
                    Path = relative,
                    Role = RoleForGenerated(relative),
                    Sha256 = Sha256File(fullPath),
                    Bytes = new FileInfo(fullPath).Length
                });
            }
            manifest.Files = manifest.Files.OrderBy(item => item.Path, StringComparer.Ordinal).ToList();
        }

        private static string RoleForGenerated(string path)
        {
            if (path == RobotBundleLayout.RobotJsonFile) return "robot-model";
            if (path == RobotBundleLayout.PortableUrdfFile) return "portable-urdf";
            if (path.StartsWith("profiles/", StringComparison.Ordinal)) return "target-profile";
            return "validation-report";
        }

        private static string ValidationMarkdown(ValidationReport validation)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# OSURDF validation report");
            builder.AppendLine();
            builder.AppendLine(validation.IsValid ? "Result: PASS" : "Result: FAIL");
            builder.AppendLine();
            builder.AppendLine("Errors: " + validation.ErrorCount + "; warnings: " + validation.WarningCount + ".");
            builder.AppendLine();
            foreach (ValidationFinding finding in validation.Findings)
            {
                builder.Append("- ").AppendLine(finding.ToString());
            }
            return builder.ToString().Replace("\r\n", "\n");
        }

        private static DateTimeOffset BuildTimestamp(out bool reproducible)
        {
            string epoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
            if (!string.IsNullOrWhiteSpace(epoch))
            {
                long seconds;
                if (!long.TryParse(epoch, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
                {
                    throw new InvalidDataException("SOURCE_DATE_EPOCH must be an integer Unix timestamp.");
                }
                try
                {
                    reproducible = true;
                    return DateTimeOffset.FromUnixTimeSeconds(seconds);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new InvalidDataException("SOURCE_DATE_EPOCH is outside the supported timestamp range.", exception);
                }
            }
            reproducible = false;
            return DateTimeOffset.UtcNow;
        }

        internal static void WriteJson(string path, object value)
        {
            JToken token = value == null ? JValue.CreateNull() : JToken.FromObject(value);
            if (value is IsaacExportProfile)
            {
                SortJsonObjectProperty(token as JObject, "packageMappings");
            }
            if (value is IsaacLabProfile)
            {
                SortJsonObjectProperty(token as JObject, "jointPositions");
                SortJsonObjectProperty(token as JObject, "jointVelocities");
            }
            WriteUtf8(path, token.ToString(Formatting.Indented) + "\n");
        }

        private static void SortJsonObjectProperty(JObject parent, string propertyName)
        {
            JObject source = parent?[propertyName] as JObject;
            if (source == null)
            {
                return;
            }
            parent[propertyName] = new JObject(
                source.Properties()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => new JProperty(property.Name, property.Value.DeepClone())));
        }

        internal static void WriteUtf8(string path, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, value.Replace("\r\n", "\n"), new UTF8Encoding(false));
        }

        internal static void WriteChecksums(string root)
        {
            EnsureNoReparsePoints(root, "Checksum input");
            IEnumerable<string> files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(RelativePath(root, path), RobotBundleLayout.ChecksumsFile, StringComparison.Ordinal))
                .OrderBy(path => RelativePath(root, path), StringComparer.Ordinal);
            StringBuilder builder = new StringBuilder();
            foreach (string file in files)
            {
                builder.Append(Sha256File(file)).Append("  ").Append(RelativePath(root, file)).Append('\n');
            }
            WriteUtf8(Path.Combine(root, RobotBundleLayout.ChecksumsFile), builder.ToString());
        }

        internal static void EnsureNoReparsePoints(string root, string label)
        {
            string fullRoot = Path.GetFullPath(root);
            if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(label + " must not be a symbolic link or reparse point: " + fullRoot);
            }

            Stack<string> directories = new Stack<string>();
            directories.Push(fullRoot);
            while (directories.Count > 0)
            {
                string directory = directories.Pop();
                foreach (string entry in Directory.GetFileSystemEntries(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException(
                            label + " contains symbolic links or reparse points: " + entry);
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        directories.Push(entry);
                    }
                }
            }
        }

        internal static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] digest = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }

        internal static string RelativePath(string root, string path)
        {
            Uri rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
            Uri pathUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('\\', '/');
        }

        internal static string SafeBundlePath(string root, string relative)
        {
            ValidatePortableBundleRelativePath(relative);
            string fullRoot = AppendDirectorySeparator(Path.GetFullPath(root));
            string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(fullRoot, PathComparison))
            {
                throw new InvalidDataException("Bundle path escapes the bundle root: " + relative);
            }
            return fullPath;
        }

        internal static void ValidatePortableBundleRelativePath(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.IndexOf('\0') >= 0 ||
                relative.IndexOf('\\') >= 0 || Path.IsPathRooted(relative) ||
                (relative.Length >= 2 && char.IsLetter(relative[0]) && relative[1] == ':'))
            {
                throw new InvalidDataException("Unsafe bundle path: " + relative);
            }
            string normalized = relative;
            if (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unsafe bundle path: " + relative);
            }
            foreach (string segment in normalized.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment.EndsWith(" ", StringComparison.Ordinal) ||
                    segment.EndsWith(".", StringComparison.Ordinal) ||
                    IsWindowsReservedSegment(segment) ||
                    segment.Any(character => character < 32 || "<>:\"|?*".IndexOf(character) >= 0))
                {
                    throw new InvalidDataException("Unsafe or non-portable bundle path: " + relative);
                }
            }
        }

        private static bool IsWindowsReservedSegment(string segment)
        {
            string stem = segment.Split('.')[0];
            if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
            if (stem.Length == 4 &&
                (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                stem[3] >= '1' && stem[3] <= '9')
            {
                return true;
            }
            return false;
        }

        private static string SanitizeFileName(string value)
        {
            StringBuilder builder = new StringBuilder(value?.Length ?? 0);
            foreach (char character in value ?? string.Empty)
            {
                bool invalid = character < 32 || "<>:\"/\\|?*".IndexOf(character) >= 0;
                builder.Append(invalid ? '_' : character);
            }
            return builder.ToString().TrimEnd(' ', '.');
        }

        private static string RedactSourceUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return uri;
            if (Path.IsPathRooted(uri) || uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return "absolute:" + Path.GetFileName(uri.Replace('\\', '/'));
            }
            return uri.Replace('\\', '/');
        }

        private static string NormalizeRelativePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static string AppendDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public sealed class RobotBundleVerifier
    {
        public BundleVerificationResult Verify(string directory)
        {
            BundleVerificationResult result = new BundleVerificationResult();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                result.Errors.Add("Bundle directory does not exist: " + directory);
                return result;
            }
            string root = Path.GetFullPath(directory);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                result.Errors.Add("The Robot Bundle root must not be a symbolic link or reparse point.");
                return result;
            }
            string manifestPath = Path.Combine(root, RobotBundleLayout.ManifestFile);
            string checksumsPath = Path.Combine(root, RobotBundleLayout.ChecksumsFile);
            if (!File.Exists(manifestPath)) result.Errors.Add("Missing " + RobotBundleLayout.ManifestFile + ".");
            if (!File.Exists(checksumsPath)) result.Errors.Add("Missing " + RobotBundleLayout.ChecksumsFile + ".");
            if (result.Errors.Count > 0) return result;

            // Inventory first so an untrusted manifest or checksum symlink is rejected before
            // either path is opened. Enumeration never follows reparse-point directories.
            HashSet<string> actualFiles = EnumerateBundleFiles(root, result);
            actualFiles.Remove(RobotBundleLayout.ChecksumsFile);
            if (result.Errors.Count > 0)
            {
                return result;
            }

            try
            {
                JObject rawManifest;
                using (StringReader text = new StringReader(File.ReadAllText(manifestPath, Encoding.UTF8)))
                using (JsonTextReader reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
                {
                    rawManifest = JObject.Load(
                        reader,
                        new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                            LineInfoHandling = LineInfoHandling.Load
                        });
                    if (reader.Read())
                    {
                        throw new JsonReaderException("Additional JSON content follows the manifest object.");
                    }
                }
                ValidateManifestJsonShape(rawManifest);
                result.Manifest = rawManifest.ToObject<RobotBundleManifest>(JsonSerializer.Create(
                    new JsonSerializerSettings
                    {
                        Culture = CultureInfo.InvariantCulture,
                        DateParseHandling = DateParseHandling.None,
                        MissingMemberHandling = MissingMemberHandling.Error
                    }));
            }
            catch (JsonException exception)
            {
                result.Errors.Add("Manifest JSON is invalid: " + exception.Message);
                return result;
            }
            if (result.Manifest == null || result.Manifest.SchemaVersion != RobotBundleLayout.ManifestSchemaVersion)
            {
                result.Errors.Add("Unsupported or missing bundle manifest schema version.");
                return result;
            }
            VerifyManifestHeader(result);

            Dictionary<string, string> checksums = ReadChecksums(checksumsPath, result);
            HashSet<string> portableActualFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string actual in actualFiles)
            {
                try
                {
                    RobotBundleBuilder.ValidatePortableBundleRelativePath(actual);
                }
                catch (InvalidDataException exception)
                {
                    result.Errors.Add(exception.Message);
                }
                if (!portableActualFiles.Add(actual))
                {
                    result.Errors.Add("Bundle paths collide on case-insensitive filesystems: " + actual);
                }
            }
            foreach (string actual in actualFiles)
            {
                if (!checksums.ContainsKey(actual)) result.Errors.Add("File is not checksummed: " + actual);
            }
            foreach (KeyValuePair<string, string> item in checksums)
            {
                string fullPath;
                try
                {
                    fullPath = RobotBundleBuilder.SafeBundlePath(root, item.Key);
                }
                catch (InvalidDataException exception)
                {
                    result.Errors.Add(exception.Message);
                    continue;
                }
                if (!File.Exists(fullPath))
                {
                    result.Errors.Add("Checksummed file is missing: " + item.Key);
                }
                else if (!string.Equals(RobotBundleBuilder.Sha256File(fullPath), item.Value, StringComparison.Ordinal))
                {
                    result.Errors.Add("Checksum mismatch: " + item.Key);
                }
            }

            VerifyManifestEntries(root, checksums, actualFiles, result);
            VerifyRobotModel(root, result);
            return result;
        }

        private static void ValidateManifestJsonShape(JObject manifest)
        {
            RequireExactProperties(
                manifest,
                "manifest",
                "schemaVersion", "bundleFormat", "robotSchemaVersion", "robotName", "createdUtc",
                "reproducibleTimestamp", "generator", "entrypoints", "profiles", "validation", "files");
            RequireExactProperties(manifest["generator"] as JObject, "manifest.generator", "name", "version", "commit");
            RequireExactProperties(
                manifest["entrypoints"] as JObject,
                "manifest.entrypoints",
                "robotJson", "portableUrdf", "isaacProfile", "isaacLabProfile");
            RequireExactProperties(manifest["profiles"] as JObject, "manifest.profiles", "ros1", "ros2", "isaac", "isaacLab");
            RequireExactProperties(
                manifest["validation"] as JObject,
                "manifest.validation",
                "valid", "errors", "warnings", "report");
            RequireType(manifest["schemaVersion"], JTokenType.Integer, "manifest.schemaVersion");
            RequireType(manifest["bundleFormat"], JTokenType.String, "manifest.bundleFormat");
            RequireType(manifest["robotSchemaVersion"], JTokenType.Integer, "manifest.robotSchemaVersion");
            RequireType(manifest["robotName"], JTokenType.String, "manifest.robotName");
            RequireType(manifest["createdUtc"], JTokenType.String, "manifest.createdUtc");
            RequireType(manifest["reproducibleTimestamp"], JTokenType.Boolean, "manifest.reproducibleTimestamp");
            foreach (string name in new[] { "name", "version", "commit" })
            {
                RequireType(manifest["generator"]?[name], JTokenType.String, "manifest.generator." + name);
            }
            foreach (string name in new[] { "robotJson", "portableUrdf", "isaacProfile", "isaacLabProfile" })
            {
                RequireType(manifest["entrypoints"]?[name], JTokenType.String, "manifest.entrypoints." + name);
            }
            foreach (string name in new[] { "ros1", "ros2", "isaac", "isaacLab" })
            {
                RequireType(manifest["profiles"]?[name], JTokenType.Boolean, "manifest.profiles." + name);
            }
            RequireType(manifest["validation"]?["valid"], JTokenType.Boolean, "manifest.validation.valid");
            RequireType(manifest["validation"]?["errors"], JTokenType.Integer, "manifest.validation.errors");
            RequireType(manifest["validation"]?["warnings"], JTokenType.Integer, "manifest.validation.warnings");
            RequireType(manifest["validation"]?["report"], JTokenType.String, "manifest.validation.report");
            JArray files = manifest["files"] as JArray;
            if (files == null)
            {
                throw new JsonSerializationException("manifest.files must be an array.");
            }
            for (int index = 0; index < files.Count; index++)
            {
                JObject entry = files[index] as JObject;
                if (entry == null)
                {
                    throw new JsonSerializationException("manifest.files[" + index + "] must be an object.");
                }
                HashSet<string> names = new HashSet<string>(
                    entry.Properties().Select(property => property.Name),
                    StringComparer.Ordinal);
                string[] required = { "path", "role", "sha256", "bytes" };
                if (required.Any(name => !names.Contains(name)) ||
                    names.Any(name => !required.Contains(name, StringComparer.Ordinal) && name != "sourceUri"))
                {
                    throw new JsonSerializationException("manifest.files[" + index + "] fields do not match schema v1.");
                }
                RequireType(entry["path"], JTokenType.String, "manifest.files[" + index + "].path");
                RequireType(entry["role"], JTokenType.String, "manifest.files[" + index + "].role");
                RequireType(entry["sha256"], JTokenType.String, "manifest.files[" + index + "].sha256");
                RequireType(entry["bytes"], JTokenType.Integer, "manifest.files[" + index + "].bytes");
                if (entry["sourceUri"] != null)
                {
                    RequireType(entry["sourceUri"], JTokenType.String, "manifest.files[" + index + "].sourceUri");
                }
            }
        }

        private static void RequireExactProperties(JObject value, string path, params string[] expected)
        {
            if (value == null)
            {
                throw new JsonSerializationException(path + " must be an object.");
            }
            HashSet<string> actual = new HashSet<string>(
                value.Properties().Select(property => property.Name),
                StringComparer.Ordinal);
            if (!actual.SetEquals(expected))
            {
                throw new JsonSerializationException(path + " fields do not match schema v1.");
            }
        }

        private static void RequireType(JToken value, JTokenType type, string path)
        {
            if (value == null || value.Type != type)
            {
                throw new JsonSerializationException(path + " must be " + type.ToString().ToLowerInvariant() + ".");
            }
        }

        private static HashSet<string> EnumerateBundleFiles(string root, BundleVerificationResult result)
        {
            HashSet<string> files = new HashSet<string>(StringComparer.Ordinal);
            Stack<string> directories = new Stack<string>();
            directories.Push(root);
            while (directories.Count > 0)
            {
                string current = directories.Pop();
                string[] entries;
                try
                {
                    entries = Directory.GetFileSystemEntries(current, "*", SearchOption.TopDirectoryOnly);
                }
                catch (Exception exception) when (
                    exception is IOException || exception is UnauthorizedAccessException)
                {
                    result.Errors.Add("Bundle inventory could not be read: " + exception.Message);
                    continue;
                }
                foreach (string entry in entries)
                {
                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (Exception exception) when (
                        exception is IOException || exception is UnauthorizedAccessException)
                    {
                        result.Errors.Add("Bundle entry could not be inspected: " + exception.Message);
                        continue;
                    }
                    string relative = RobotBundleBuilder.RelativePath(root, entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        result.Errors.Add("Symbolic links and reparse points are not allowed in a Robot Bundle: " + relative);
                        continue;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        directories.Push(entry);
                    }
                    else
                    {
                        files.Add(relative);
                    }
                }
            }
            return files;
        }

        private static void VerifyManifestHeader(BundleVerificationResult result)
        {
            RobotBundleManifest manifest = result.Manifest;
            if (!string.Equals(manifest.BundleFormat, "osurdf-robot-bundle", StringComparison.Ordinal))
            {
                result.Errors.Add("Unexpected Robot Bundle format identifier.");
            }
            if (manifest.RobotSchemaVersion != RobotSchema.CurrentVersion)
            {
                result.Errors.Add("Unsupported robot schema version recorded by the manifest.");
            }
            if (string.IsNullOrWhiteSpace(manifest.RobotName) ||
                manifest.Generator == null || string.IsNullOrWhiteSpace(manifest.Generator.Name) ||
                string.IsNullOrWhiteSpace(manifest.Generator.Version) ||
                string.IsNullOrWhiteSpace(manifest.Generator.Commit))
            {
                result.Errors.Add("Manifest robot and generator identity must be explicit.");
            }
            DateTimeOffset created;
            if (string.IsNullOrWhiteSpace(manifest.CreatedUtc) ||
                !DateTimeOffset.TryParseExact(
                    manifest.CreatedUtc,
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out created))
            {
                result.Errors.Add("Manifest createdUtc must be an exact UTC timestamp.");
            }
            if (manifest.Entrypoints == null ||
                !string.Equals(manifest.Entrypoints.RobotJson, RobotBundleLayout.RobotJsonFile, StringComparison.Ordinal) ||
                !string.Equals(manifest.Entrypoints.PortableUrdf, RobotBundleLayout.PortableUrdfFile, StringComparison.Ordinal) ||
                !string.Equals(manifest.Entrypoints.IsaacProfile, "profiles/isaac.json", StringComparison.Ordinal) ||
                !string.Equals(manifest.Entrypoints.IsaacLabProfile, "profiles/isaaclab.json", StringComparison.Ordinal))
            {
                result.Errors.Add("Manifest entrypoints do not match the canonical Robot Bundle layout.");
            }
            if (manifest.Profiles == null || manifest.Validation == null || manifest.Files == null)
            {
                result.Errors.Add("Manifest profiles, validation summary and file inventory are required.");
            }
            else if (!manifest.Validation.Valid || manifest.Validation.Errors != 0 ||
                manifest.Validation.Warnings < 0 ||
                !string.Equals(manifest.Validation.Report, RobotBundleLayout.ValidationJsonFile, StringComparison.Ordinal))
            {
                result.Errors.Add("A distributable Robot Bundle must record a passing canonical validation report.");
            }
        }

        private static Dictionary<string, string> ReadChecksums(string path, BundleVerificationResult result)
        {
            Dictionary<string, string> checksums = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> portablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = rawLine;
                if (line.Length == 0) continue;
                if (line.Length < 67 || line[64] != ' ' || line[65] != ' ')
                {
                    result.Errors.Add("Malformed checksum line: " + rawLine);
                    continue;
                }
                string digest = line.Substring(0, 64);
                string relative = line.Substring(66);
                if (digest.Any(character => !Uri.IsHexDigit(character)))
                {
                    result.Errors.Add("Malformed SHA-256 digest for " + relative + ".");
                    continue;
                }
                try
                {
                    RobotBundleBuilder.ValidatePortableBundleRelativePath(relative);
                }
                catch (InvalidDataException exception)
                {
                    result.Errors.Add(exception.Message);
                    continue;
                }
                if (!portablePaths.Add(relative))
                {
                    result.Errors.Add("Checksum paths collide on case-insensitive filesystems: " + relative);
                    continue;
                }
                if (checksums.ContainsKey(relative))
                {
                    result.Errors.Add("Duplicate checksum path: " + relative);
                }
                else
                {
                    checksums.Add(relative, digest.ToLowerInvariant());
                }
            }
            return checksums;
        }

        private static void VerifyManifestEntries(
            string root,
            IDictionary<string, string> checksums,
            ISet<string> actualFiles,
            BundleVerificationResult result)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> portablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BundleFileEntry entry in result.Manifest.Files ?? new List<BundleFileEntry>())
            {
                if (entry == null)
                {
                    result.Errors.Add("Manifest contains a null file entry.");
                    continue;
                }
                if (!paths.Add(entry.Path ?? string.Empty))
                {
                    result.Errors.Add("Duplicate manifest path: " + entry.Path);
                    continue;
                }
                if (!portablePaths.Add(entry.Path ?? string.Empty))
                {
                    result.Errors.Add("Manifest paths collide on case-insensitive filesystems: " + entry.Path);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(entry.Role) || entry.Bytes < 0 ||
                    string.IsNullOrWhiteSpace(entry.Sha256) || entry.Sha256.Length != 64 ||
                    entry.Sha256.Any(character => !Uri.IsHexDigit(character)))
                {
                    result.Errors.Add("Manifest entry metadata is invalid: " + entry.Path);
                    continue;
                }
                string fullPath;
                try
                {
                    fullPath = RobotBundleBuilder.SafeBundlePath(root, entry.Path);
                }
                catch (Exception exception) when (exception is InvalidDataException || exception is ArgumentException)
                {
                    result.Errors.Add(exception.Message);
                    continue;
                }
                if (!File.Exists(fullPath))
                {
                    result.Errors.Add("Manifest payload is missing: " + entry.Path);
                    continue;
                }
                if (new FileInfo(fullPath).Length != entry.Bytes)
                {
                    result.Errors.Add("Manifest byte count mismatch: " + entry.Path);
                }
                if (!string.Equals(RobotBundleBuilder.Sha256File(fullPath), entry.Sha256, StringComparison.Ordinal))
                {
                    result.Errors.Add("Manifest checksum mismatch: " + entry.Path);
                }
                string checksum;
                if (!checksums.TryGetValue(entry.Path, out checksum) ||
                    !string.Equals(checksum, entry.Sha256, StringComparison.Ordinal))
                {
                    result.Errors.Add("Manifest and checksum inventory disagree: " + entry.Path);
                }
            }
            HashSet<string> expectedManifestFiles = new HashSet<string>(actualFiles, StringComparer.Ordinal);
            expectedManifestFiles.Remove(RobotBundleLayout.ManifestFile);
            if (!expectedManifestFiles.SetEquals(paths))
            {
                IEnumerable<string> missing = expectedManifestFiles.Except(paths, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal);
                IEnumerable<string> extra = paths.Except(expectedManifestFiles, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal);
                result.Errors.Add("Manifest payload inventory mismatch; missing=[" + string.Join(", ", missing) + "]; extra=[" + string.Join(", ", extra) + "].");
            }
        }

        private static void VerifyRobotModel(string root, BundleVerificationResult result)
        {
            string jsonPath = Path.Combine(root, RobotBundleLayout.RobotJsonFile);
            string urdfPath = Path.Combine(root, RobotBundleLayout.PortableUrdfFile);
            if (!File.Exists(jsonPath) || !File.Exists(urdfPath))
            {
                result.Errors.Add("Robot Bundle entrypoints are incomplete.");
                return;
            }
            try
            {
                JObject rawRobot = ReadStrictJson(jsonPath) as JObject;
                if (rawRobot == null)
                {
                    throw new InvalidDataException("robot.json must contain an object.");
                }
                if ((int?)rawRobot["schemaVersion"] != RobotSchema.CurrentVersion)
                {
                    result.Errors.Add("Robot Bundles must contain canonical robot.json schema " + RobotSchema.CurrentVersion + "; migration is not performed during verification.");
                    return;
                }
                RobotDocument robot = RobotJson.Read(jsonPath);
                ValidationReport validation = new RobotValidator().Validate(robot);
                foreach (ValidationFinding error in validation.Findings.Where(item => item.Severity == ValidationSeverity.Error))
                {
                    result.Errors.Add("robot.json: " + error);
                }
                RobotDocument urdf = UrdfCodec.Read(urdfPath);
                if (!JToken.DeepEquals(CreateUrdfProjection(robot), CreateUrdfProjection(urdf)))
                {
                    result.Errors.Add("robot.json and robot.urdf canonical model data do not match.");
                }
                JObject embeddedProfiles = rawRobot["profiles"] as JObject;
                VerifyProfileFile(root, "profiles/package.json", embeddedProfiles?["package"], result);
                VerifyProfileFile(root, "profiles/ros1.json", embeddedProfiles?["ros1"], result);
                VerifyProfileFile(root, "profiles/ros2.json", embeddedProfiles?["ros2"], result);
                VerifyProfileFile(root, "profiles/isaac.json", embeddedProfiles?["isaac"], result);
                VerifyProfileFile(root, "profiles/isaaclab.json", embeddedProfiles?["isaacLab"], result);
                RobotBundleManifest manifest = result.Manifest;
                if (!string.Equals(manifest.RobotName, robot.Name, StringComparison.Ordinal) ||
                    manifest.RobotSchemaVersion != robot.SchemaVersion)
                {
                    result.Errors.Add("Manifest robot identity does not match robot.json.");
                }
                if (manifest.Profiles != null && robot.Profiles != null &&
                    (manifest.Profiles.Ros1 != (robot.Profiles.Ros1?.Enabled == true) ||
                     manifest.Profiles.Ros2 != (robot.Profiles.Ros2?.Enabled == true) ||
                     manifest.Profiles.Isaac != (robot.Profiles.Isaac?.Enabled == true) ||
                     manifest.Profiles.IsaacLab != (robot.Profiles.IsaacLab?.Enabled == true)))
                {
                    result.Errors.Add("Manifest profile flags do not match robot.json.");
                }
                if (manifest.Validation != null &&
                    (manifest.Validation.Valid != validation.IsValid ||
                     manifest.Validation.Errors != validation.ErrorCount ||
                     manifest.Validation.Warnings != validation.WarningCount))
                {
                    result.Errors.Add("Manifest validation summary does not match current robot validation.");
                }

                JObject validationJson = ReadStrictJson(
                    Path.Combine(root, RobotBundleLayout.ValidationJsonFile)) as JObject;
                if (validationJson == null)
                {
                    throw new InvalidDataException("Stored validation report must contain an object.");
                }
                JObject expectedValidation = JObject.FromObject(new
                {
                    valid = validation.IsValid,
                    errors = validation.ErrorCount,
                    warnings = validation.WarningCount,
                    findings = validation.Findings.Select(item => new
                    {
                        severity = item.Severity.ToString().ToLowerInvariant(),
                        code = item.Code,
                        path = item.Path,
                        message = item.Message
                    }).ToList()
                });
                if (!JToken.DeepEquals(validationJson, expectedValidation))
                {
                    result.Errors.Add("Stored validation report does not exactly match current robot validation.");
                }
            }
            catch (Exception exception) when (
                exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException ||
                exception is JsonException)
            {
                result.Errors.Add("Robot model verification failed: " + exception.Message);
            }
        }

        private static JObject CreateUrdfProjection(RobotDocument robot)
        {
            JObject projection = JObject.Parse(RobotJson.Serialize(robot));
            projection.Remove("metadata");
            projection.Remove("profiles");
            foreach (JObject link in (projection["links"] as JArray ?? new JArray()).OfType<JObject>())
            {
                link.Remove("id");
                link.Remove("source");
            }
            foreach (JObject joint in (projection["joints"] as JArray ?? new JArray()).OfType<JObject>())
            {
                joint.Remove("id");
                joint.Remove("source");
            }
            return projection;
        }

        private static void VerifyProfileFile(
            string root,
            string relative,
            JToken expected,
            BundleVerificationResult result)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            JToken actual = ReadStrictJson(path);
            JToken expectedToken = expected ?? JValue.CreateNull();
            if (!JToken.DeepEquals(actual, expectedToken))
            {
                result.Errors.Add(relative + " does not match the corresponding robot.json profile.");
            }
        }

        private static JToken ReadStrictJson(string path)
        {
            using (StreamReader text = new StreamReader(path, Encoding.UTF8, true))
            using (JsonTextReader reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
            {
                JToken value = JToken.ReadFrom(
                    reader,
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Load
                    });
                if (reader.Read())
                {
                    throw new JsonReaderException("Additional JSON content follows the document: " + path);
                }
                return value;
            }
        }
    }
}
