using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OSURDF.Core.Bundle;

namespace OSURDF.Core.Export
{
    public sealed class UsdAssetExportOptions
    {
        public string BundleDirectory { get; set; }
        public string OutputDirectory { get; set; }
        public string PythonExecutable { get; set; }
        public string AdapterScript { get; set; }
        public bool Overwrite { get; set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5.0);
    }

    public sealed class UsdAssetExportResult
    {
        public string OutputDirectory { get; set; }
        public string UsdFile { get; set; }
        public string NameMapFile { get; set; }
        public string ReportFile { get; set; }
        public string OpenUsdVersion { get; set; }
        public string RetainedPreviousDirectory { get; set; }
    }

    public sealed class UsdAdapterInvocation
    {
        public string PythonExecutable { get; set; }
        public string AdapterScript { get; set; }
        public string BundleDirectory { get; set; }
        public string OutputDirectory { get; set; }
        public bool Overwrite { get; set; }
        public TimeSpan Timeout { get; set; }
    }

    public sealed class UsdAdapterRunResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
    }

    public interface IUsdAdapterRunner
    {
        UsdAdapterRunResult Run(UsdAdapterInvocation invocation);
    }

    public sealed class UsdAssetExporter
    {
        private readonly IUsdAdapterRunner runner;

        public UsdAssetExporter()
            : this(new ProcessUsdAdapterRunner())
        {
        }

        public UsdAssetExporter(IUsdAdapterRunner runner)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public UsdAssetExportResult Export(UsdAssetExportOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            string bundle = RequireDirectory(options.BundleDirectory, "OSURDF bundle");
            BundleVerificationResult verification = new RobotBundleVerifier().Verify(bundle);
            if (!verification.IsValid)
            {
                throw new InvalidDataException(
                    "USD export requires a verified OSURDF staging bundle: " +
                    string.Join("; ", verification.Errors));
            }

            string python = RequireFile(options.PythonExecutable, "bundled OpenUSD Python runtime");
            string adapter = RequireFile(options.AdapterScript, "OpenUSD adapter");
            string output = Path.GetFullPath(Require(options.OutputDirectory, "USD output directory"));
            EnsureSeparateTrees(bundle, output);
            string outputParent = Path.GetDirectoryName(output);
            if (string.IsNullOrWhiteSpace(outputParent))
            {
                throw new InvalidDataException("USD output has no parent directory: " + output);
            }
            Directory.CreateDirectory(outputParent);

            UsdAdapterRunResult run = runner.Run(new UsdAdapterInvocation
            {
                PythonExecutable = python,
                AdapterScript = adapter,
                BundleDirectory = bundle,
                OutputDirectory = output,
                Overwrite = options.Overwrite,
                Timeout = options.Timeout <= TimeSpan.Zero
                    ? TimeSpan.FromMinutes(5.0)
                    : options.Timeout
            });
            if (run == null)
            {
                throw new InvalidDataException("The OpenUSD adapter returned no process result.");
            }
            if (run.ExitCode != 0)
            {
                throw new InvalidDataException(
                    "OpenUSD asset generation failed with exit code " + run.ExitCode + ". " +
                    FirstUseful(run.StandardError, run.StandardOutput));
            }

            JObject response = ParseObject(run.StandardOutput, "OpenUSD adapter response");
            if (response.Value<bool?>("ok") != true)
            {
                throw new InvalidDataException(
                    "The OpenUSD adapter did not report a successful export.");
            }
            string usd = RequireOutputFile(output, response.Value<string>("usd"), "robot.usd");
            string nameMap = RequireOutputFile(
                output,
                response.Value<string>("nameMap"),
                "name_map.json");
            string report = RequireOutputFile(
                output,
                response.Value<string>("report"),
                "export_report.json");
            JObject reportObject = ParseObject(File.ReadAllText(report, Encoding.UTF8), "OpenUSD export report");
            JObject validation = reportObject["validation"] as JObject;
            if (reportObject.Value<bool?>("ok") != true ||
                validation == null ||
                validation.Value<bool?>("ok") != true ||
                validation.Value<bool?>("stageReopened") != true)
            {
                throw new InvalidDataException(
                    "The OpenUSD adapter did not prove that the generated stage can be reopened.");
            }
            string version = validation.Value<string>("openUsdVersion");
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidDataException(
                    "The OpenUSD export report does not identify its validation runtime.");
            }
            return new UsdAssetExportResult
            {
                OutputDirectory = output,
                UsdFile = usd,
                NameMapFile = nameMap,
                ReportFile = report,
                OpenUsdVersion = version,
                RetainedPreviousDirectory = NormalizeOptionalPath(
                    response.Value<string>("retainedPreviousDirectory"))
            };
        }

        private static string NormalizeOptionalPath(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : Path.GetFullPath(value.Trim());
        }

        private static string Require(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(label + " is required.");
            }
            return value.Trim();
        }

        private static string RequireFile(string value, string label)
        {
            string path = Path.GetFullPath(Require(value, label));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(label + " was not found.", path);
            }
            return path;
        }

        private static string RequireDirectory(string value, string label)
        {
            string path = Path.GetFullPath(Require(value, label));
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException(label + " was not found: " + path);
            }
            return path;
        }

        private static void EnsureSeparateTrees(string bundle, string output)
        {
            string bundleRoot = WithSeparator(bundle);
            string outputRoot = WithSeparator(output);
            if (string.Equals(bundleRoot, outputRoot, StringComparison.OrdinalIgnoreCase) ||
                outputRoot.StartsWith(bundleRoot, StringComparison.OrdinalIgnoreCase) ||
                bundleRoot.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "USD output and the private OSURDF staging bundle must be separate directory trees.");
            }
        }

        private static string WithSeparator(string value)
        {
            return Path.GetFullPath(value)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
        }

        private static JObject ParseObject(string json, string label)
        {
            try
            {
                return JObject.Parse((json ?? string.Empty).Trim());
            }
            catch (Exception exception) when (
                exception is Newtonsoft.Json.JsonException ||
                exception is ArgumentException)
            {
                throw new InvalidDataException(label + " is not valid JSON.", exception);
            }
        }

        private static string RequireOutputFile(string root, string reported, string fallback)
        {
            string path = string.IsNullOrWhiteSpace(reported)
                ? Path.Combine(root, fallback)
                : Path.GetFullPath(reported);
            string normalizedRoot = WithSeparator(root);
            if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
            {
                throw new InvalidDataException(
                    "OpenUSD adapter output is missing or outside the requested directory: " + path);
            }
            return path;
        }

        private static string FirstUseful(string first, string second)
        {
            string value = string.IsNullOrWhiteSpace(first) ? second : first;
            return string.IsNullOrWhiteSpace(value) ? "No adapter diagnostics were returned." : value.Trim();
        }

        private sealed class ProcessUsdAdapterRunner : IUsdAdapterRunner
        {
            public UsdAdapterRunResult Run(UsdAdapterInvocation invocation)
            {
                ProcessStartInfo start = new ProcessStartInfo
                {
                    FileName = invocation.PythonExecutable,
                    Arguments = Quote(invocation.AdapterScript) +
                        " export --bundle " + Quote(invocation.BundleDirectory) +
                        " --output " + Quote(invocation.OutputDirectory) +
                        (invocation.Overwrite ? " --overwrite" : string.Empty),
                    WorkingDirectory = Path.GetDirectoryName(invocation.AdapterScript),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (Process process = new Process { StartInfo = start })
                {
                    if (!process.Start())
                    {
                        throw new InvalidOperationException("The bundled OpenUSD runtime did not start.");
                    }
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> error = process.StandardError.ReadToEndAsync();
                    int timeoutMilliseconds = invocation.Timeout.TotalMilliseconds >= int.MaxValue
                        ? int.MaxValue
                        : Math.Max(1, (int)invocation.Timeout.TotalMilliseconds);
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                        }
                        throw new TimeoutException(
                            "OpenUSD asset generation exceeded " + invocation.Timeout + ".");
                    }
                    Task.WaitAll(new Task[] { output, error }, TimeSpan.FromSeconds(10.0));
                    return new UsdAdapterRunResult
                    {
                        ExitCode = process.ExitCode,
                        StandardOutput = output.Result,
                        StandardError = error.Result
                    };
                }
            }

            private static string Quote(string value)
            {
                return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
            }
        }
    }
}
