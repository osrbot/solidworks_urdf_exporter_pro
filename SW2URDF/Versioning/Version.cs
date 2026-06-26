using SW2URDF.Utilities;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace SW2URDF.Versioning
{
    internal class Version
    {
        private const string MetadataPrefix = "SW2URDF.";

        public static string GetPluginVersion()
        {
            System.Version version = typeof(Logger).Assembly.GetName().Version;
            if (version == null)
            {
                return VersionInfo.Unknown;
            }

            return version.Major + "." + version.Minor;
        }

        public static string GetCommitVersion()
        {
            string productVersion = FileVersionInfo.GetVersionInfo(typeof(Logger).Assembly.Location).ProductVersion;
            return String.IsNullOrWhiteSpace(productVersion) ? VersionInfo.Unknown : productVersion;
        }

        public static string GetCommitHash()
        {
            return GetAssemblyMetadata("CommitHash");
        }

        public static string GetBuildTimeUtc()
        {
            return GetAssemblyMetadata("BuildTimeUtc");
        }

        public static string GetDirtyState()
        {
            return GetAssemblyMetadata("Dirty");
        }

        public static string GetBuildVersion()
        {
            return typeof(Logger).Assembly.GetName().Version.ToString();
        }

        private static string GetAssemblyMetadata(string key)
        {
            string fullKey = MetadataPrefix + key;
            AssemblyMetadataAttribute metadata = typeof(Logger).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => String.Equals(attribute.Key, fullKey, StringComparison.Ordinal));
            return metadata == null || String.IsNullOrWhiteSpace(metadata.Value)
                ? VersionInfo.Unknown
                : metadata.Value;
        }
    }
}
