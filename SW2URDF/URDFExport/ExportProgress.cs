using SW2URDF.UI;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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

    public sealed class ExportResultSummary
    {
        internal ExportResultSummary(
            string outputRoot,
            int fileCount,
            long totalBytes,
            TimeSpan elapsed)
        {
            OutputRoot = outputRoot ?? String.Empty;
            FileCount = fileCount;
            TotalBytes = totalBytes;
            Elapsed = elapsed;
        }

        public string OutputRoot { get; private set; }
        public int FileCount { get; private set; }
        public long TotalBytes { get; private set; }
        public TimeSpan Elapsed { get; private set; }

        internal static ExportResultSummary Create(
            URDFPackage package,
            ExportOutputSnapshot outputBeforeExport,
            TimeSpan elapsed)
        {
            if (package == null)
            {
                throw new ArgumentNullException("package");
            }

            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddFiles(files, package.WindowsPackageDirectory, outputBeforeExport);
            AddFiles(files, package.WindowsRos2PackageDirectory, outputBeforeExport);
            AddFile(files, package.WindowsExportLogFile, outputBeforeExport);

            long totalBytes = files.Sum(path => GetFileLengthOrZero(path));
            return new ExportResultSummary(
                package.WindowsExportRootDirectory,
                files.Count,
                totalBytes,
                elapsed);
        }

        public string FormatDetails()
        {
            return String.Format(
                CultureInfo.CurrentCulture,
                "{0}: {1}\r\n{2}: {3}\r\n{4}: {5}\r\n{6}: {7}",
                ChineseUiText.Translate("Files exported", "已导出文件"),
                FileCount,
                ChineseUiText.Translate("Total size", "总大小"),
                FormatBytes(TotalBytes),
                ChineseUiText.Translate("Elapsed", "用时"),
                OperationHeartbeat.FormatElapsed(Elapsed),
                ChineseUiText.Translate("Output root", "输出根目录"),
                OutputRoot);
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
