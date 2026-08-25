using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SW2URDF.UI
{
    internal enum UrdfExportTutorialStatus
    {
        NotStarted,
        InProgress,
        Completed,
        Dismissed
    }

    internal sealed class UrdfExportTutorialProgress
    {
        public UrdfExportTutorialStatus Status { get; private set; }
        public int StepIndex { get; private set; }

        public UrdfExportTutorialProgress(UrdfExportTutorialStatus status, int stepIndex)
        {
            Status = status;
            StepIndex = Math.Max(0, stepIndex);
        }

        public static UrdfExportTutorialProgress NotStarted()
        {
            return new UrdfExportTutorialProgress(UrdfExportTutorialStatus.NotStarted, 0);
        }
    }

    internal interface IUrdfExportTutorialStateStore
    {
        UrdfExportTutorialProgress Load();
        bool Save(UrdfExportTutorialProgress progress);
    }

    internal sealed class FileUrdfExportTutorialStateStore : IUrdfExportTutorialStateStore
    {
        internal const int StateVersion = 1;
        private readonly string filePath;

        public FileUrdfExportTutorialStateStore()
            : this(GetDefaultFilePath())
        {
        }

        internal FileUrdfExportTutorialStateStore(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A tutorial state file path is required.", "filePath");
            }

            this.filePath = Path.GetFullPath(filePath);
        }

        internal string FilePath
        {
            get { return filePath; }
        }

        public UrdfExportTutorialProgress Load()
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return UrdfExportTutorialProgress.NotStarted();
                }

                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                int version = -1;
                int stepIndex = 0;
                UrdfExportTutorialStatus status = UrdfExportTutorialStatus.NotStarted;
                bool hasStatus = false;

                foreach (string line in lines)
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (String.Equals(key, "version", StringComparison.OrdinalIgnoreCase))
                    {
                        Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
                    }
                    else if (String.Equals(key, "status", StringComparison.OrdinalIgnoreCase))
                    {
                        hasStatus = Enum.TryParse(value, true, out status);
                    }
                    else if (String.Equals(key, "step", StringComparison.OrdinalIgnoreCase))
                    {
                        Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out stepIndex);
                    }
                }

                if (version != StateVersion || !hasStatus || stepIndex < 0)
                {
                    return UrdfExportTutorialProgress.NotStarted();
                }

                return new UrdfExportTutorialProgress(status, stepIndex);
            }
            catch
            {
                return UrdfExportTutorialProgress.NotStarted();
            }
        }

        public bool Save(UrdfExportTutorialProgress progress)
        {
            if (progress == null)
            {
                throw new ArgumentNullException("progress");
            }

            string temporaryPath = filePath + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string content = String.Format(
                    CultureInfo.InvariantCulture,
                    "version={0}\r\nstatus={1}\r\nstep={2}\r\n",
                    StateVersion,
                    progress.Status,
                    progress.StepIndex);
                File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));

                if (File.Exists(filePath))
                {
                    File.Replace(temporaryPath, filePath, null);
                }
                else
                {
                    File.Move(temporaryPath, filePath);
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch
                {
                    // Tutorial state cleanup must never block the exporter.
                }
            }
        }

        private static string GetDefaultFilePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OSRBot",
                "SW2URDF",
                "urdf-export-tutorial-v1.state");
        }
    }
}
