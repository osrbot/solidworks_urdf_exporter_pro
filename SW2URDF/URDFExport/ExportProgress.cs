using SW2URDF.UI;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SW2URDF.URDFExport
{
    public sealed class ExportProgressEventArgs : EventArgs
    {
        public ExportProgressEventArgs(string stage, TimeSpan elapsed)
        {
            Stage = stage ?? String.Empty;
            Elapsed = elapsed;
        }

        public string Stage { get; private set; }
        public TimeSpan Elapsed { get; private set; }
    }

    public sealed class ExportTargetResult
    {
        public ExportTargetResult(string targetName, string outputDirectory, bool succeeded,
            string errorMessage, bool previousOutputRetained)
            : this(targetName, outputDirectory, succeeded, errorMessage, previousOutputRetained, null)
        {
        }

        public ExportTargetResult(string targetName, string outputDirectory, bool succeeded,
            string errorMessage, bool previousOutputRetained, string phase)
        {
            TargetName = targetName ?? String.Empty;
            OutputDirectory = outputDirectory ?? String.Empty;
            Succeeded = succeeded;
            ErrorMessage = errorMessage ?? String.Empty;
            PreviousOutputRetained = previousOutputRetained;
            Phase = phase ?? String.Empty;
        }

        public string TargetName { get; private set; }
        public string OutputDirectory { get; private set; }
        public bool Succeeded { get; private set; }
        public string ErrorMessage { get; private set; }
        public bool PreviousOutputRetained { get; private set; }
        public string Phase { get; private set; }

        internal string FormatStatus(bool chinese)
        {
            if (Succeeded)
            {
                return chinese ? "成功" : "Succeeded";
            }
            return PreviousOutputRetained
                ? (chinese ? "失败；旧输出本次未更新" : "Failed; old output not updated this run")
                : (chinese ? "失败" : "Failed");
        }
    }

    public sealed class ExportResultSummary
    {
        internal ExportResultSummary(
            string outputRoot,
            int fileCount,
            long totalBytes,
            TimeSpan elapsed,
            IEnumerable<ExportTargetResult> targets = null,
            IEnumerable<string> warnings = null)
        {
            OutputRoot = outputRoot ?? String.Empty;
            FileCount = fileCount;
            TotalBytes = totalBytes;
            Elapsed = elapsed;
            Targets = (targets ?? Enumerable.Empty<ExportTargetResult>())
                .Where(target => target != null).ToList().AsReadOnly();
            Warnings = (warnings ?? Enumerable.Empty<string>())
                .Where(warning => !String.IsNullOrWhiteSpace(warning)).ToList().AsReadOnly();
            SucceededCount = Targets.Count(target => target.Succeeded);
            FailedCount = Targets.Count - SucceededCount;
        }

        public string OutputRoot { get; private set; }
        public int FileCount { get; private set; }
        public long TotalBytes { get; private set; }
        public TimeSpan Elapsed { get; private set; }
        public IList<ExportTargetResult> Targets { get; private set; }
        public IList<string> Warnings { get; private set; }
        public int SucceededCount { get; private set; }
        public int FailedCount { get; private set; }
        public bool HasFailures { get { return FailedCount > 0; } }
        public bool HasPartialSuccess { get { return SucceededCount > 0 && HasFailures; } }

        internal static ExportResultSummary Create(
            URDFPackage package,
            ExportOutputSnapshot outputBeforeExport,
            TimeSpan elapsed,
            IEnumerable<ExportTargetResult> targets = null,
            IEnumerable<string> warnings = null)
        {
            if (package == null)
            {
                throw new ArgumentNullException("package");
            }

            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IList<ExportTargetResult> results = targets == null ? null :
                targets.Where(target => target != null).ToList();
            if (results == null)
            {
                AddFiles(files, package.WindowsPackageDirectory, outputBeforeExport);
                AddFiles(files, package.WindowsRos2PackageDirectory, outputBeforeExport);
                AddFiles(files, package.WindowsUsdAssetDirectory, outputBeforeExport);
                AddFiles(files, package.WindowsMjcfAssetDirectory, outputBeforeExport);
                AddFile(files, package.WindowsExportReportFile, outputBeforeExport);
                AddFile(files, package.WindowsExportLogFile, outputBeforeExport);
            }
            else
            {
                // A successful target was published as a whole, even if file stamps are unchanged.
                // Failed/retained directories and export-root reports are not current target output.
                foreach (ExportTargetResult target in results.Where(target => target.Succeeded))
                {
                    AddFiles(files, target.OutputDirectory, null);
                }
            }

            long totalBytes = files.Sum(path => GetFileLengthOrZero(path));
            return new ExportResultSummary(
                package.WindowsExportRootDirectory,
                files.Count,
                totalBytes,
                elapsed,
                results,
                warnings);
        }

        public string FormatDetails()
        {
            return FormatDetails(ChineseUiText.ShouldUseChinese());
        }

        internal string FormatDetails(bool chinese)
        {
            StringBuilder builder = new StringBuilder(String.Format(
                CultureInfo.CurrentCulture,
                "{0}: {1}\r\n{2}: {3}\r\n{4}: {5}\r\n{6}: {7}",
                chinese ? "已导出文件" : "Files exported",
                FileCount,
                chinese ? "总大小" : "Total size",
                FormatBytes(TotalBytes),
                chinese ? "用时" : "Elapsed",
                OperationHeartbeat.FormatElapsed(Elapsed),
                chinese ? "输出根目录" : "Output root",
                OutputRoot));
            if (Targets.Count > 0)
            {
                builder.AppendLine().AppendLine();
                builder.AppendLine(String.Format(CultureInfo.CurrentCulture,
                    chinese ? "目标：{0} 成功，{1} 失败" : "Targets: {0} succeeded, {1} failed",
                    SucceededCount, FailedCount));
                foreach (ExportTargetResult target in Targets)
                {
                    builder.AppendLine();
                    builder.Append(target.Succeeded ? "[SUCCEEDED] " : "[FAILED] ")
                        .Append(target.TargetName).Append(": ").AppendLine(target.FormatStatus(chinese));
                    builder.Append(chinese ? "输出目录: " : "Output directory: ").AppendLine(target.OutputDirectory);
                    if (!target.Succeeded && target.PreviousOutputRetained)
                    {
                        builder.AppendLine(chinese
                            ? "已保留旧输出；本次未更新 (not updated this run)。"
                            : "Previous output retained; not updated this run.");
                    }
                    if (!String.IsNullOrWhiteSpace(target.Phase))
                    {
                        builder.Append(chinese ? "阶段: " : "Phase: ").AppendLine(target.Phase);
                    }
                    if (!String.IsNullOrWhiteSpace(target.ErrorMessage))
                    {
                        builder.AppendLine(chinese ? "原始错误:" : "Raw error:").AppendLine(target.ErrorMessage);
                    }
                }
            }
            if (Warnings.Count > 0)
            {
                builder.AppendLine().AppendLine(chinese ? "警告:" : "Warnings:");
                foreach (string warning in Warnings)
                {
                    builder.AppendLine(warning);
                }
            }
            return builder.ToString().TrimEnd();
        }

        internal static string FormatBytes(long bytes)
        {
            const double scale = 1024.0;
            if (bytes < scale)
            {
                return bytes.ToString(CultureInfo.CurrentCulture) + " B";
            }
            double kib = bytes / scale;
            if (kib < scale)
            {
                return kib.ToString("0.##", CultureInfo.CurrentCulture) + " KiB";
            }
            return (kib / scale).ToString("0.##", CultureInfo.CurrentCulture) + " MiB";
        }

        private static void AddFiles(
            ISet<string> files,
            string directory,
            ExportOutputSnapshot outputBeforeExport)
        {
            if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }
            try
            {
                foreach (string path in Directory.EnumerateFiles(
                    directory,
                    "*",
                    SearchOption.AllDirectories))
                {
                    AddFile(files, path, outputBeforeExport);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void AddFile(
            ISet<string> files,
            string path,
            ExportOutputSnapshot outputBeforeExport)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                if (outputBeforeExport == null || outputBeforeExport.IsNewOrChanged(fullPath))
                {
                    files.Add(fullPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        private static long GetFileLengthOrZero(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }

    internal sealed class ExportOutputSnapshot
    {
        private readonly Dictionary<string, FileStamp> files;

        private ExportOutputSnapshot(Dictionary<string, FileStamp> files)
        {
            this.files = files;
        }

        internal static ExportOutputSnapshot Capture(URDFPackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException("package");
            }

            Dictionary<string, FileStamp> files =
                new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
            CaptureDirectory(files, package.WindowsPackageDirectory);
            CaptureDirectory(files, package.WindowsRos2PackageDirectory);
            CaptureDirectory(files, package.WindowsUsdAssetDirectory);
            CaptureDirectory(files, package.WindowsMjcfAssetDirectory);
            CaptureFile(files, package.WindowsExportReportFile);
            CaptureFile(files, package.WindowsExportLogFile);
            return new ExportOutputSnapshot(files);
        }

        internal bool IsNewOrChanged(string path)
        {
            FileStamp before;
            FileStamp after;
            return !files.TryGetValue(path, out before) ||
                !TryReadStamp(path, out after) ||
                !before.Matches(after);
        }

        private static void CaptureDirectory(
            IDictionary<string, FileStamp> files,
            string directory)
        {
            if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }
            try
            {
                foreach (string path in Directory.EnumerateFiles(
                    directory,
                    "*",
                    SearchOption.AllDirectories))
                {
                    CaptureFile(files, path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void CaptureFile(IDictionary<string, FileStamp> files, string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return;
            }
            try
            {
                string fullPath = Path.GetFullPath(path);
                FileStamp stamp;
                if (TryReadStamp(fullPath, out stamp))
                {
                    files[fullPath] = stamp;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        private static bool TryReadStamp(string path, out FileStamp stamp)
        {
            try
            {
                FileInfo file = new FileInfo(path);
                if (!file.Exists)
                {
                    stamp = default(FileStamp);
                    return false;
                }
                stamp = new FileStamp(file.Length, file.LastWriteTimeUtc.Ticks);
                return true;
            }
            catch (IOException)
            {
                stamp = default(FileStamp);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                stamp = default(FileStamp);
                return false;
            }
        }

        private struct FileStamp
        {
            private readonly long length;
            private readonly long lastWriteTicks;

            internal FileStamp(long length, long lastWriteTicks)
            {
                this.length = length;
                this.lastWriteTicks = lastWriteTicks;
            }

            internal bool Matches(FileStamp other)
            {
                return length == other.length && lastWriteTicks == other.lastWriteTicks;
            }
        }
    }
}
