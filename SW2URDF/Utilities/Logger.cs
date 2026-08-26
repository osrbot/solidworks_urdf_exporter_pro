using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Layout.Pattern;
using log4net.Repository.Hierarchy;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SW2URDF.Utilities
{
    public class FileNamePatternConverter : PatternLayoutConverter
    {
        protected override void Convert(TextWriter writer, LoggingEvent loggingEvent)
        {
            writer.Write(Path.GetFileName(loggingEvent.LocationInformation.FileName));
        }
    }

    public static class Logger
    {
        internal const string LogFileEnvironmentVariable = "SW2URDF_LOG_FILE";
        private static readonly object InitializationLock = new object();
        private static volatile bool Initialized = false;

        public static void Setup()
        {
            if (Initialized)
            {
                return;
            }

            lock (InitializationLock)
            {
                if (Initialized)
                {
                    return;
                }

                SetupCore();
                Initialized = true;
            }
        }

        private static void SetupCore()
        {
            Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository();

            // This ConversionPattern is slow because any location-based parameter in log4net is
            // slow. If it becomes an issue this might have to be wrapped into a compile time macro
            PatternLayout patternLayout = new PatternLayout()
            {
                ConversionPattern = "%date %-5level %filename: %line - %message%newline"
            };

            patternLayout.AddConverter("filename", typeof(FileNamePatternConverter));
            patternLayout.ActivateOptions();

            string logFileName = GetConfiguredLogFileName();
            PrepareFreshLogFile(logFileName);
            RollingFileAppender roller = new RollingFileAppender
            {
                AppendToFile = true,
                File = logFileName,
                ImmediateFlush = true,
                Layout = patternLayout,
                Encoding = new UTF8Encoding(false),
                MaxSizeRollBackups = 5,
                MaximumFileSize = "10MB",
                RollingStyle = RollingFileAppender.RollingMode.Size,
                StaticLogFileName = true
            };

            roller.ActivateOptions();
            hierarchy.Root.AddAppender(roller);

            MemoryAppender memory = new MemoryAppender();
            memory.ActivateOptions();
            hierarchy.Root.AddAppender(memory);

            hierarchy.Root.Level = Level.Info;
            hierarchy.Configured = true;
            ILog logger = LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);
            logger.Info("\n" + String.Concat(Enumerable.Repeat("-", 80)));
            logger.Info("Logging commencing for SW2URDF exporter");

            logger.Info("Plugin version " + Versioning.Version.GetPluginVersion());
            logger.Info("Commit version " + Versioning.Version.GetCommitVersion());
            logger.Info("Commit hash " + Versioning.Version.GetCommitHash());
            logger.Info("Build version " + Versioning.Version.GetBuildVersion());
            logger.Info("Build time UTC " + Versioning.Version.GetBuildTimeUtc());
            logger.Info("Dirty state " + Versioning.Version.GetDirtyState());
        }

        private static string GetConfiguredLogFileName()
        {
            string configuredPath = Environment.GetEnvironmentVariable(
                LogFileEnvironmentVariable);
            if (!String.IsNullOrWhiteSpace(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }

            string homeDir = Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
            return Path.Combine(homeDir, "sw2urdf_logs", "sw2urdf.log");
        }

        private static void PrepareFreshLogFile(string filename)
        {
            try
            {
                string directory = Path.GetDirectoryName(filename);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                if (File.Exists(filename))
                {
                    File.Delete(filename);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static ILog GetLogger()
        {
            Setup();
            return LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);
        }

        public static string GetFileName()
        {
            RollingFileAppender rootAppender =
                LogManager.GetRepository().GetAppenders().OfType<RollingFileAppender>()
                                         .FirstOrDefault();
            if (rootAppender != null)
            {
                return rootAppender.File;
            }
            else
            {
                return null;
            }
        }
    }
}
