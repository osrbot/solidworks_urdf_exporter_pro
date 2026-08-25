using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace SW2URDF.URDFExport
{
    internal sealed class ExportSessionDraft
    {
        public LinkNode Root { get; private set; }
        public string RosPackageName { get; private set; }
        public string SavePath { get; private set; }
        public DateTime SavedUtc { get; private set; }

        public ExportSessionDraft(
            LinkNode root,
            string rosPackageName,
            string savePath,
            DateTime savedUtc)
        {
            Root = root;
            RosPackageName = rosPackageName ?? string.Empty;
            SavePath = savePath ?? string.Empty;
            SavedUtc = savedUtc;
        }
    }

    internal interface IExportSessionDraftStore
    {
        bool TryLoad(string modelPath, out ExportSessionDraft draft);
        bool Save(string modelPath, LinkNode root, string rosPackageName, string savePath);
        bool Delete(string modelPath);
    }

    internal sealed class FileExportSessionDraftStore : IExportSessionDraftStore
    {
        internal const int DraftVersion = 1;
        private static readonly log4net.ILog logger = Logger.GetLogger();
        private readonly string rootDirectory;

        [DataContract(Name = "SW2URDFExportSessionDraft")]
        private sealed class DraftEnvelope
        {
            [DataMember(Order = 1)]
            public int Version { get; set; }

            [DataMember(Order = 2)]
            public string ModelPath { get; set; }

            [DataMember(Order = 3)]
            public DateTime SavedUtc { get; set; }

            [DataMember(Order = 4)]
            public string RosPackageName { get; set; }

            [DataMember(Order = 5)]
            public string SavePath { get; set; }

            [DataMember(Order = 6)]
            public string ConfigurationPayload { get; set; }
        }

        public FileExportSessionDraftStore()
            : this(GetDefaultRootDirectory())
        {
        }

        internal FileExportSessionDraftStore(string rootDirectory)
        {
            if (String.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("A draft root directory is required.", nameof(rootDirectory));
            }
            this.rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public bool TryLoad(string modelPath, out ExportSessionDraft draft)
        {
            draft = null;
            if (!TryNormalizeModelPath(modelPath, out string normalizedModelPath))
            {
                return false;
            }

            string draftPath = GetDraftFilePath(normalizedModelPath);
            if (!File.Exists(draftPath))
            {
                return false;
            }

            try
            {
                DraftEnvelope envelope;
                DataContractSerializer serializer = new DataContractSerializer(typeof(DraftEnvelope));
                using (FileStream stream = new FileStream(
                    draftPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                {
                    envelope = (DraftEnvelope)serializer.ReadObject(stream);
                }

                if (envelope == null ||
                    envelope.Version != DraftVersion ||
                    !String.Equals(
                        normalizedModelPath,
                        envelope.ModelPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                LinkNode root = ConfigurationSerialization.DeserializeDraftPayload(
                    envelope.ConfigurationPayload);
                if (root == null)
                {
                    return false;
                }

                draft = new ExportSessionDraft(
                    root,
                    envelope.RosPackageName,
                    envelope.SavePath,
                    envelope.SavedUtc);
                return true;
            }
            catch (Exception exception)
            {
                logger.Warn("The URDF export recovery draft could not be loaded.", exception);
                return false;
            }
        }

        public bool Save(
            string modelPath,
            LinkNode root,
            string rosPackageName,
            string savePath)
        {
            if (root == null ||
                !TryNormalizeModelPath(modelPath, out string normalizedModelPath))
            {
                return false;
            }

            string payload = ConfigurationSerialization.SerializeDraftPayload(root);
            if (String.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            string draftPath = GetDraftFilePath(normalizedModelPath);
            string temporaryPath = draftPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(rootDirectory);
                DraftEnvelope envelope = new DraftEnvelope
                {
                    Version = DraftVersion,
                    ModelPath = normalizedModelPath,
                    SavedUtc = DateTime.UtcNow,
                    RosPackageName = rosPackageName ?? string.Empty,
                    SavePath = savePath ?? string.Empty,
                    ConfigurationPayload = payload
                };

                DataContractSerializer serializer = new DataContractSerializer(typeof(DraftEnvelope));
                XmlWriterSettings settings = new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false),
                    Indent = true,
                    CloseOutput = false
                };
                using (XmlWriter writer = XmlWriter.Create(temporaryPath, settings))
                {
                    serializer.WriteObject(writer, envelope);
                }

                if (File.Exists(draftPath))
                {
                    File.Replace(temporaryPath, draftPath, null);
                }
                else
                {
                    File.Move(temporaryPath, draftPath);
                }
                return true;
            }
            catch (Exception exception)
            {
                logger.Warn("The URDF export recovery draft could not be saved.", exception);
                return false;
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }

        public bool Delete(string modelPath)
        {
            if (!TryNormalizeModelPath(modelPath, out string normalizedModelPath))
            {
                return false;
            }

            string draftPath = GetDraftFilePath(normalizedModelPath);
            try
            {
                if (File.Exists(draftPath))
                {
                    File.Delete(draftPath);
                }
                return true;
            }
            catch (Exception exception)
            {
                logger.Warn("The URDF export recovery draft could not be deleted.", exception);
                return false;
            }
        }

        internal string GetDraftFilePath(string modelPath)
        {
            if (!TryNormalizeModelPath(modelPath, out string normalizedModelPath))
            {
                throw new ArgumentException(
                    "A saved SolidWorks model path is required.",
                    nameof(modelPath));
            }

            byte[] bytes = Encoding.UTF8.GetBytes(normalizedModelPath.ToUpperInvariant());
            byte[] digest;
            using (SHA256 sha = SHA256.Create())
            {
                digest = sha.ComputeHash(bytes);
            }

            StringBuilder name = new StringBuilder("export-session-v1-");
            foreach (byte value in digest)
            {
                name.Append(value.ToString("x2"));
            }
            name.Append(".xml");
            return Path.Combine(rootDirectory, name.ToString());
        }

        private static bool TryNormalizeModelPath(string modelPath, out string normalizedModelPath)
        {
            normalizedModelPath = string.Empty;
            if (String.IsNullOrWhiteSpace(modelPath))
            {
                return false;
            }

            try
            {
                normalizedModelPath = Path.GetFullPath(modelPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetDefaultRootDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OSRBot",
                "SW2URDF",
                "export-drafts");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Recovery cleanup must never block SolidWorks from closing the exporter.
            }
        }
    }
}
