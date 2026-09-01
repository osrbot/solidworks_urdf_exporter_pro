using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OSURDF.Core.Export
{
    public sealed class BundledMjcfCompilerValidator : IMjcfCompilerValidator
    {
        private readonly string compileExecutable;
        private readonly string testSpeedExecutable;
        private readonly string version;
        private readonly TimeSpan timeout;

        public BundledMjcfCompilerValidator(
            string compileExecutable,
            string testSpeedExecutable,
            string version,
            TimeSpan? timeout = null)
        {
            this.compileExecutable = RequireFile(compileExecutable, "MuJoCo compile executable");
            this.testSpeedExecutable = RequireFile(testSpeedExecutable, "MuJoCo testspeed executable");
            this.version = string.IsNullOrWhiteSpace(version)
                ? throw new ArgumentException("MuJoCo runtime version is required.", nameof(version))
                : version.Trim();
            this.timeout = timeout.GetValueOrDefault(TimeSpan.FromMinutes(2.0));
            if (this.timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
        }

        public MjcfCompilerValidationResult Validate(MjcfCompilerValidationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string working = RequireDirectory(request.WorkingDirectory, "MJCF working directory");
            IReadOnlyList<string> models = request.ModelPaths ?? Array.Empty<string>();
            if (models.Count == 0)
            {
                throw new InvalidDataException("At least one MJCF model is required for official validation.");
            }
            string validation = Path.Combine(
                working,
                ".osurdf-mujoco-validation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(validation);
            List<string> completed = new List<string>();
            try
            {
                foreach (string requestedModel in models)
                {
                    string model = RequireChildFile(working, requestedModel, "MJCF model");
                    string stem = Path.GetFileNameWithoutExtension(model);
                    string binary = Path.Combine(validation, stem + ".mjb");
                    string canonical = Path.Combine(
                        working,
                        ".osurdf-compiled-" + Guid.NewGuid().ToString("N") + "-" + stem + ".xml");
                    try
                    {
                        RunChecked(
                            compileExecutable,
                            new[] { model, binary },
                            working,
                            "compile " + Path.GetFileName(model));
                        RunChecked(
                            compileExecutable,
                            new[] { model, canonical },
                            working,
                            "save canonical " + Path.GetFileName(model));
                        RunChecked(
                            compileExecutable,
                            new[] { canonical },
                            working,
                            "reload canonical " + Path.GetFileName(model));
                        RunChecked(
                            testSpeedExecutable,
                            new[]
                            {
                                "--nstep=1",
                                "--nthread=1",
                                "--noisestd=0",
                                "--noiserate=0",
                                canonical
                            },
                            working,
                            "one zero-control simulation step " + Path.GetFileName(model));
                        completed.Add(Path.GetFileName(model));
                    }
                    finally
                    {
                        if (File.Exists(canonical))
                        {
                            File.Delete(canonical);
                        }
                    }
                }
                return new MjcfCompilerValidationResult
                {
                    Succeeded = true,
                    Validator = "bundled-official-mujoco-tools",
                    MuJoCoVersion = version,
                    Message =
                        "Official MuJoCo compiled, saved, reloaded, and advanced one zero-control step for: " +
                        string.Join(", ", completed) +
                        ". / MuJoCo 官方工具已完成编译、规范化保存、重新载入和一步零控制仿真。"
                };
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is InvalidDataException ||
                exception is TimeoutException ||
                exception is UnauthorizedAccessException)
            {
                return new MjcfCompilerValidationResult
                {
                    Succeeded = false,
                    Validator = "bundled-official-mujoco-tools",
                    MuJoCoVersion = version,
                    Message = exception.Message
                };
            }
            finally
            {
                if (Directory.Exists(validation))
                {
                    Directory.Delete(validation, true);
                }
            }
        }

        private void RunChecked(
            string executable,
            IEnumerable<string> arguments,
            string workingDirectory,
            string operation)
        {
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = string.Join(" ", arguments.Select(Quote)),
                WorkingDirectory = workingDirectory,
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
                    throw new IOException("MuJoCo could not start: " + operation);
                }
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                int milliseconds = timeout.TotalMilliseconds >= int.MaxValue
                    ? int.MaxValue
                    : Math.Max(1, (int)timeout.TotalMilliseconds);
                if (!process.WaitForExit(milliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    throw new TimeoutException("MuJoCo timed out while attempting to " + operation + ".");
                }
                Task.WaitAll(new Task[] { output, error }, TimeSpan.FromSeconds(10.0));
                if (process.ExitCode != 0)
                {
                    string diagnostics = string.IsNullOrWhiteSpace(error.Result)
                        ? output.Result
                        : error.Result;
                    throw new InvalidDataException(
                        "MuJoCo failed to " + operation + " (exit " + process.ExitCode + "): " +
                        (string.IsNullOrWhiteSpace(diagnostics)
                            ? "no compiler diagnostics"
                            : diagnostics.Trim()));
                }
            }
        }

        private static string RequireFile(string path, string label)
        {
            string result = Path.GetFullPath(path ?? string.Empty);
            if (!File.Exists(result))
            {
                throw new FileNotFoundException(label + " was not found.", result);
            }
            return result;
        }

        private static string RequireDirectory(string path, string label)
        {
            string result = Path.GetFullPath(path ?? string.Empty);
            if (!Directory.Exists(result))
            {
                throw new DirectoryNotFoundException(label + " was not found: " + result);
            }
            return result;
        }

        private static string RequireChildFile(string root, string path, string label)
        {
            string result = RequireFile(path, label);
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!result.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(label + " is outside its working directory: " + result);
            }
            return result;
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }
}
