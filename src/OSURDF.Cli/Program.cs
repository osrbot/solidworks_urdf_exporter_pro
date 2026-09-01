using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using OSURDF.Core.Bundle;
using OSURDF.Core.Export;
using OSURDF.Core.Model;
using OSURDF.Core.Serialization;
using OSURDF.Core.Urdf;
using OSURDF.Core.Validation;

namespace OSURDF.Cli;

internal static class Program
{
    private const int UsageError = 64;
    private const int ValidationError = 2;
    private const int OperationError = 1;
    private static readonly Regex RosPackageName = new(
        "^[a-z][a-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] ProfileOptions =
    {
        "package-name", "package-version", "description", "maintainer-name",
        "maintainer-email", "model-license", "model-author", "ros1", "ros2",
        "ros-distro", "gazebo-distro"
    };

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine(HelpText());
            return 0;
        }

        try
        {
            Arguments options = Arguments.Parse(args.Skip(1));
            return args[0] switch
            {
                "import-urdf" => ImportUrdf(options),
                "upgrade" => Upgrade(options),
                "validate" => Validate(options),
                "bundle" => BuildBundle(options),
                "verify-bundle" => VerifyBundle(options),
                "inspect" => Inspect(options),
                "export-urdf" => ExportUrdf(options),
                "export-ros2" => ExportRos(options, ros2: true),
                "export-ros1" => ExportRos(options, ros2: false),
                "export-usd" => ExportUsd(options),
                "export-mjcf" => ExportMjcf(options),
                "version" => Version(options),
                _ => Fail(UsageError, "UNKNOWN_COMMAND", "Unknown command: " + args[0])
            };
        }
        catch (ArgumentException exception)
        {
            return Fail(UsageError, "USAGE", exception.Message);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            return Fail(OperationError, "OPERATION_FAILED", exception.Message);
        }
    }

    private static int ImportUrdf(Arguments options)
    {
        options.AssertOnly(ProfileOptions.Concat(new[] { "input", "output", "backup" }).ToArray());
        string input = options.Required("input");
        string output = options.Required("output");
        RobotDocument robot = UrdfCodec.Read(input);
        ApplyMetadata(robot, options);
        ApplyProfileOverrides(robot, options);
        RobotJson.Write(output, robot, options.Flag("backup"));
        ValidationReport report = new RobotValidator().Validate(robot);
        WriteResult(new
        {
            ok = report.IsValid,
            command = "import-urdf",
            output = Path.GetFullPath(output),
            written = true,
            valid = report.IsValid,
            errors = report.ErrorCount,
            warnings = report.WarningCount
        });
        return report.IsValid ? 0 : ValidationError;
    }

    private static int Upgrade(Arguments options)
    {
        options.AssertOnly("input", "output");
        string input = options.Required("input");
        string output = options.Value("output") ?? input;
        RobotDocument robot = RobotJson.Read(input);
        RobotJson.Write(output, robot, createBackup: true);
        WriteResult(new { ok = true, command = "upgrade", output = Path.GetFullPath(output), schemaVersion = robot.SchemaVersion });
        return 0;
    }

    private static int Validate(Arguments options)
    {
        options.AssertOnly("input");
        string input = options.Required("input");
        if (Directory.Exists(input))
        {
            BundleVerificationResult verification = new RobotBundleVerifier().Verify(input);
            WriteResult(new
            {
                ok = verification.IsValid,
                kind = "robot-bundle",
                errors = verification.Errors,
                warnings = verification.Warnings
            });
            return verification.IsValid ? 0 : ValidationError;
        }

        RobotDocument robot = ReadRobot(input);
        ValidationReport report = new RobotValidator().Validate(robot);
        WriteValidation(report, Path.GetFullPath(input));
        return report.IsValid ? 0 : ValidationError;
    }

    private static int BuildBundle(Arguments options)
    {
        options.AssertOnly(ProfileOptions.Concat(new[]
        {
            "source-urdf", "urdf", "robot", "output", "package-map",
            "overwrite", "allow-absolute-assets"
        }).ToArray());
        string? sourceUrdfOption = options.Value("source-urdf");
        string? urdfAlias = options.Value("urdf");
        if (sourceUrdfOption != null && urdfAlias != null)
        {
            throw new ArgumentException("Use either --source-urdf or its --urdf alias, not both.");
        }
        string sourceUrdf = sourceUrdfOption ?? urdfAlias ??
            throw new ArgumentException("--source-urdf (or --urdf) is required.");
        string? robotJson = options.Value("robot");
        RobotDocument robot = robotJson == null ? UrdfCodec.Read(sourceUrdf) : RobotJson.Read(robotJson);
        ApplyMetadata(robot, options);
        ApplyProfileOverrides(robot, options);

        Dictionary<string, string> mappings = new(StringComparer.Ordinal);
        foreach (string mapping in options.Values("package-map"))
        {
            int split = mapping.IndexOf('=');
            if (split <= 0 || split == mapping.Length - 1)
            {
                throw new ArgumentException("--package-map must use package=/absolute/path.");
            }
            string packageName = mapping[..split];
            string packagePath = mapping[(split + 1)..];
            if (!RosPackageName.IsMatch(packageName))
            {
                throw new ArgumentException("--package-map package names must match ^[a-z][a-z0-9_]*$: " + packageName);
            }
            if (!Path.IsPathRooted(packagePath))
            {
                throw new ArgumentException("--package-map paths must be absolute: " + packageName);
            }
            packagePath = Path.GetFullPath(packagePath);
            if (!mappings.TryAdd(packageName, packagePath))
            {
                throw new ArgumentException("--package-map package names must be unique: " + packageName);
            }
        }
        BundleBuildResult result = new RobotBundleBuilder().Build(robot, new BundleBuildOptions
        {
            SourceUrdfPath = sourceUrdf,
            OutputDirectory = options.Required("output"),
            Overwrite = options.Flag("overwrite"),
            AllowAbsoluteAssetPaths = options.Flag("allow-absolute-assets"),
            PackageMappings = mappings
        });
        WriteResult(new
        {
            ok = true,
            command = "bundle",
            output = result.OutputDirectory,
            retainedPrevious = result.RetainedPreviousDirectory,
            robot = result.Manifest.RobotName,
            files = result.Manifest.Files.Count,
            errors = result.Validation.ErrorCount,
            warnings = result.Validation.WarningCount,
            reproducibleTimestamp = result.Manifest.ReproducibleTimestamp
        });
        return 0;
    }

    private static int VerifyBundle(Arguments options)
    {
        options.AssertOnly("bundle");
        BundleVerificationResult result = new RobotBundleVerifier().Verify(options.Required("bundle"));
        WriteResult(new
        {
            ok = result.IsValid,
            command = "verify-bundle",
            robot = result.Manifest?.RobotName,
            errors = result.Errors,
            warnings = result.Warnings
        });
        return result.IsValid ? 0 : ValidationError;
    }

    private static int Inspect(Arguments options)
    {
        options.AssertOnly("input");
        string input = options.Required("input");
        RobotDocument robot;
        if (Directory.Exists(input))
        {
            BundleVerificationResult verification = new RobotBundleVerifier().Verify(input);
            if (!verification.IsValid)
            {
                WriteResult(new
                {
                    ok = false,
                    command = "inspect",
                    kind = "robot-bundle",
                    errors = verification.Errors,
                    warnings = verification.Warnings
                });
                return ValidationError;
            }
            robot = RobotJson.Read(Path.Combine(input, RobotBundleLayout.RobotJsonFile));
        }
        else
        {
            robot = ReadRobot(input);
        }
        WriteResult(new
        {
            ok = true,
            schemaVersion = robot.SchemaVersion,
            robot = robot.Name,
            links = robot.Links?.Count ?? 0,
            joints = robot.Joints?.Count ?? 0,
            roots = RootLinks(robot),
            jointTypes = (robot.Joints ?? new List<JointDocument>()).Where(joint => joint != null)
                .GroupBy(joint => joint.Type ?? "blank", StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            profiles = new
            {
                ros1 = robot.Profiles?.Ros1?.Enabled == true,
                ros2 = robot.Profiles?.Ros2?.Enabled == true,
                isaac = robot.Profiles?.Isaac?.Enabled == true,
                isaacLab = robot.Profiles?.IsaacLab?.Enabled == true
            }
        });
        return 0;
    }

    private static int ExportUrdf(Arguments options)
    {
        options.AssertOnly("input", "output");
        RobotDocument robot = ReadRobot(options.Required("input"));
        ValidationReport report = new RobotValidator().Validate(robot);
        if (!report.IsValid)
        {
            WriteValidation(report, options.Required("input"));
            return ValidationError;
        }
        string output = options.Required("output");
        UrdfCodec.Write(output, robot, portableAssetPaths: true);
        WriteResult(new { ok = true, command = "export-urdf", output = Path.GetFullPath(output) });
        return 0;
    }

    private static int ExportRos(Arguments options, bool ros2)
    {
        options.AssertOnly("bundle", "output", "overwrite");
        RosExportOptions exportOptions = new()
        {
            BundleDirectory = options.Required("bundle"),
            OutputDirectory = options.Required("output"),
            Overwrite = options.Flag("overwrite")
        };
        RosPackageExporter exporter = new();
        string output = ros2 ? exporter.ExportRos2(exportOptions) : exporter.ExportRos1(exportOptions);
        WriteResult(new { ok = true, command = ros2 ? "export-ros2" : "export-ros1", output });
        return 0;
    }

    private static int ExportUsd(Arguments options)
    {
        options.AssertOnly("bundle", "output", "python", "adapter", "overwrite");
        UsdAssetExportResult result = new UsdAssetExporter().Export(new UsdAssetExportOptions
        {
            BundleDirectory = options.Required("bundle"),
            OutputDirectory = options.Required("output"),
            PythonExecutable = options.Required("python"),
            AdapterScript = options.Required("adapter"),
            Overwrite = options.Flag("overwrite")
        });
        WriteResult(new
        {
            ok = true,
            command = "export-usd",
            output = result.OutputDirectory,
            usd = result.UsdFile,
            report = result.ReportFile,
            openUsdVersion = result.OpenUsdVersion,
            validationScope = "OpenUSD structural validation; Isaac Sim and Isaac Lab were not executed."
        });
        return 0;
    }

    private static int ExportMjcf(Arguments options)
    {
        options.AssertOnly("bundle", "output", "mujoco-bin", "mujoco-version", "overwrite");
        string runtime = Path.GetFullPath(options.Required("mujoco-bin"));
        BundledMjcfCompilerValidator validator = new(
            Path.Combine(runtime, "compile.exe"),
            Path.Combine(runtime, "testspeed.exe"),
            options.Required("mujoco-version"));
        MjcfExportResult result = new MjcfAssetExporter().Export(new MjcfExportOptions
        {
            BundleDirectory = options.Required("bundle"),
            OutputDirectory = options.Required("output"),
            Overwrite = options.Flag("overwrite"),
            CompilerValidator = validator
        });
        if (!string.Equals(result.OfficialCompilationStatus, "passed", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The generated MJCF did not pass the bundled official MuJoCo validation.");
        }
        WriteResult(new
        {
            ok = true,
            command = "export-mjcf",
            output = result.OutputDirectory,
            robot = result.RobotXmlPath,
            scene = result.SceneXmlPath,
            report = result.ExportReportPath,
            officialCompilation = result.OfficialCompilationStatus
        });
        return 0;
    }

    private static int Version(Arguments options)
    {
        options.AssertOnly();
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        WriteResult(new { ok = true, version });
        return 0;
    }

    private static RobotDocument ReadRobot(string path)
    {
        return string.Equals(Path.GetExtension(path), ".urdf", StringComparison.OrdinalIgnoreCase)
            ? UrdfCodec.Read(path)
            : RobotJson.Read(path);
    }

    private static void ApplyMetadata(RobotDocument robot, Arguments options)
    {
        robot.Metadata ??= new RobotMetadata();
        robot.Metadata.Generator = "OSURDF CLI";
        robot.Metadata.GeneratorVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        robot.Metadata.Commit = Environment.GetEnvironmentVariable("OSURDF_GIT_COMMIT") ?? robot.Metadata.Commit ?? "unknown";
        robot.Metadata.ModelLicense = options.Value("model-license") ?? robot.Metadata.ModelLicense;
        robot.Metadata.ModelAuthor = options.Value("model-author") ?? robot.Metadata.ModelAuthor;
    }

    private static void ApplyProfileOverrides(RobotDocument robot, Arguments options)
    {
        robot.Profiles ??= new RobotProfiles();
        robot.Profiles.Package ??= new PackageMetadataProfile();
        robot.Profiles.Ros1 ??= new Ros1ExportProfile();
        robot.Profiles.Ros2 ??= new Ros2ExportProfile();
        robot.Profiles.Isaac ??= new IsaacExportProfile();
        robot.Profiles.IsaacLab ??= new IsaacLabProfile();

        PackageMetadataProfile package = robot.Profiles.Package;
        package.PackageName = options.Value("package-name") ?? package.PackageName;
        package.Version = options.Value("package-version") ?? package.Version;
        package.Description = options.Value("description") ?? package.Description;
        package.MaintainerName = options.Value("maintainer-name") ?? package.MaintainerName;
        package.MaintainerEmail = options.Value("maintainer-email") ?? package.MaintainerEmail;
        package.License = options.Value("model-license") ?? package.License ?? robot.Metadata.ModelLicense;

        if (options.Flag("ros1")) robot.Profiles.Ros1.Enabled = true;
        if (options.Flag("ros2")) robot.Profiles.Ros2.Enabled = true;
        robot.Profiles.Ros2.Distribution = options.Value("ros-distro") ?? robot.Profiles.Ros2.Distribution;
        robot.Profiles.Ros2.GazeboDistribution = options.Value("gazebo-distro") ?? robot.Profiles.Ros2.GazeboDistribution;

    }

    private static IReadOnlyList<string> RootLinks(RobotDocument robot)
    {
        HashSet<string> children = (robot.Joints ?? new List<JointDocument>())
            .Where(joint => joint != null)
            .Select(joint => joint.Child)
            .ToHashSet(StringComparer.Ordinal);
        return (robot.Links ?? new List<LinkDocument>())
            .Where(link => link != null)
            .Select(link => link.Name)
            .Where(name => !children.Contains(name))
            .ToList();
    }

    private static void WriteValidation(ValidationReport report, string input)
    {
        WriteResult(new
        {
            ok = report.IsValid,
            command = "validate",
            input,
            errors = report.ErrorCount,
            warnings = report.WarningCount,
            findings = report.Findings.Select(finding => new
            {
                severity = finding.Severity.ToString().ToLowerInvariant(),
                finding.Code,
                finding.Path,
                finding.Message
            })
        });
    }

    private static int Fail(int exitCode, string code, string message)
    {
        Console.Error.WriteLine(JsonConvert.SerializeObject(new { ok = false, code, message }, Formatting.Indented));
        return exitCode;
    }

    private static void WriteResult(object value)
    {
        Console.WriteLine(JsonConvert.SerializeObject(value, Formatting.Indented));
    }

    private static string HelpText()
    {
        return """
            OSURDF 2 - portable robot asset pipeline

            Commands:
              import-urdf  --input robot.urdf --output robot.json
              upgrade      --input robot.json [--output robot-v2.json]
              validate     --input robot.json|robot.urdf|bundle-directory
              bundle       --source-urdf robot.urdf [--robot robot.json] --output bundle
              verify-bundle --bundle bundle
              inspect      --input robot.json|robot.urdf|bundle-directory
              export-urdf  --input robot.json --output robot.urdf
              export-ros2  --bundle bundle --output packages [--overwrite]
              export-ros1  --bundle bundle --output packages [--overwrite]
              export-usd   --bundle bundle --output asset --python python.exe --adapter osurdf_usd_adapter.py [--overwrite]
              export-mjcf  --bundle bundle --output assets --mujoco-bin DIR --mujoco-version X.Y.Z [--overwrite]
              version

            Bundle configuration:
              --package-map name=/absolute/package/root   repeatable
              --allow-absolute-assets                     explicit local-path opt-in
              --overwrite                                 atomic replacement
              --package-name NAME --package-version X.Y.Z
              --description TEXT --maintainer-name NAME --maintainer-email EMAIL
              --model-license SPDX-OR-TEXT --model-author NAME
              --ros2 [--ros-distro lyrical --gazebo-distro jetty]
              --ros1

            The CLI never guesses joint types, actuator gains, package license, or maintainer identity.
            USD validation proves OpenUSD structure only. MJCF validation uses the official MuJoCo tools.
            """;
    }

    private sealed class Arguments
    {
        private readonly Dictionary<string, List<string?>> values = new(StringComparer.Ordinal);

        public static Arguments Parse(IEnumerable<string> arguments)
        {
            Arguments result = new();
            string[] items = arguments.ToArray();
            for (int index = 0; index < items.Length; index++)
            {
                string token = items[index];
                if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2)
                {
                    throw new ArgumentException("Unexpected argument: " + token);
                }
                string key = token[2..];
                string? value = null;
                if (index + 1 < items.Length && !items[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = items[++index];
                }
                if (!result.values.TryGetValue(key, out List<string?>? entries))
                {
                    entries = new List<string?>();
                    result.values.Add(key, entries);
                }
                entries.Add(value);
            }
            return result;
        }

        public string Required(string key)
        {
            return Value(key) ?? throw new ArgumentException("--" + key + " is required.");
        }

        public string? Value(string key)
        {
            if (!values.TryGetValue(key, out List<string?>? entries)) return null;
            if (entries.Count != 1 || entries[0] == null)
            {
                throw new ArgumentException("--" + key + " requires exactly one value.");
            }
            return entries[0];
        }

        public IReadOnlyList<string> Values(string key)
        {
            if (!values.TryGetValue(key, out List<string?>? entries)) return Array.Empty<string>();
            if (entries.Any(value => value == null)) throw new ArgumentException("--" + key + " requires a value.");
            return entries.Select(value => value!).ToList();
        }

        public bool Flag(string key)
        {
            if (!values.TryGetValue(key, out List<string?>? entries)) return false;
            if (entries.Count != 1 || entries[0] != null)
            {
                throw new ArgumentException("--" + key + " is a flag, takes no value, and may be specified once.");
            }
            return true;
        }

        public void AssertOnly(params string[] allowed)
        {
            HashSet<string> accepted = new(allowed, StringComparer.Ordinal);
            string[] unknown = values.Keys
                .Where(key => !accepted.Contains(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            if (unknown.Length > 0)
            {
                throw new ArgumentException(
                    "Unknown option" + (unknown.Length == 1 ? ": --" : "s: --") +
                    string.Join(", --", unknown));
            }
        }
    }
}
