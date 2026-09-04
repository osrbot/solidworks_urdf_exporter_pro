using OSURDF.Core.Bundle;
using OSURDF.Core.Export;
using OSURDF.Core.Model;
using OSURDF.Core.Serialization;
using OSURDF.Core.Urdf;
using SW2URDF.URDF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW2URDF.Utilities;
using LegacyJoint = SW2URDF.URDF.Joint;
using LegacyLink = SW2URDF.URDF.Link;
using LegacyRobot = SW2URDF.URDF.Robot;

namespace SW2URDF.URDFExport
{
    internal sealed class V2ExportResult
    {
        public string Ros1Directory { get; set; }
        public string Ros2Directory { get; set; }
        public string UsdDirectory { get; set; }
        public string MjcfDirectory { get; set; }
        public IList<string> Warnings { get; set; } = new List<string>();
        public IList<ExportHelper.MeshExportRecord> DeliveryMeshRecords { get; set; } =
            new List<ExportHelper.MeshExportRecord>();
        public IList<ExportTargetResult> Targets { get; } = new List<ExportTargetResult>();
        public IList<string> Reports { get; } = new List<string>();
    }

    internal sealed class DirectoryPublishRequest
    {
        public string Label { get; set; }
        public string StagingDirectory { get; set; }
        public string DestinationDirectory { get; set; }
        public string PreviousDirectory { get; set; }
        public bool HadPreviousDirectory { get; set; }
        public bool Published { get; set; }
        public string PublicationState { get; set; }
    }

    internal sealed class DirectoryPublicationJournal
    {
        public int SchemaVersion { get; set; }
        public string TransactionId { get; set; }
        public string PublicationRoot { get; set; }
        public string CreatedUtc { get; set; }
        public string Phase { get; set; }
        public IList<DirectoryPublishRequest> Requests { get; set; }

        [JsonIgnore]
        public string FilePath { get; set; }
    }

    internal sealed class AtomicDirectoryPublication
    {
        private readonly IList<DirectoryPublishRequest> requests;
        private readonly DirectoryPublicationJournal journal;
        private readonly IList<string> recoveryWarnings;
        private FileStream publicationLock;
        private bool finished;

        internal AtomicDirectoryPublication(
            IList<DirectoryPublishRequest> requests,
            DirectoryPublicationJournal journal,
            IList<string> recoveryWarnings,
            FileStream publicationLock)
        {
            this.requests = requests ?? throw new ArgumentNullException("requests");
            this.journal = journal ?? throw new ArgumentNullException("journal");
            this.recoveryWarnings = recoveryWarnings ?? new List<string>();
            this.publicationLock = publicationLock ??
                throw new ArgumentNullException("publicationLock");
        }

        public IList<string> Commit()
        {
            return Commit(null);
        }

        internal IList<string> Commit(Action beforeFinalizationUnderLock)
        {
            if (finished)
            {
                throw new InvalidOperationException("The output publication transaction is already finished.");
            }

            IList<string> warnings = AtomicDirectoryPublisher.Commit(
                requests,
                journal,
                beforeFinalizationUnderLock);
            finished = true;
            try
            {
                return recoveryWarnings.Concat(warnings).ToList();
            }
            finally
            {
                ReleaseLock();
            }
        }

        public IList<string> RollBack()
        {
            if (finished)
            {
                return new List<string>();
            }

            try
            {
                return AtomicDirectoryPublisher.RollBack(requests, journal);
            }
            finally
            {
                finished = true;
                ReleaseLock();
            }
        }

        private void ReleaseLock()
        {
            if (publicationLock == null)
            {
                return;
            }
            AtomicDirectoryPublisher.ReleasePublicationLock(publicationLock);
            publicationLock = null;
        }
    }

    internal static class AtomicDirectoryPublisher
    {
        internal static void WithOutputRootLock(string root, Action action)
        {
            string fullRoot = Path.GetFullPath(root);
            EnsurePlainDirectory(fullRoot, "Output root");
            FileStream publicationLock = AcquirePublicationLock(fullRoot);
            try { action(); }
            finally { ReleasePublicationLock(publicationLock); }
        }

        public static IList<string> Publish(IList<DirectoryPublishRequest> requests)
        {
            AtomicDirectoryPublication publication = Begin(requests);
            try
            {
                return publication.Commit();
            }
            catch (Exception publishFailure)
            {
                IList<string> rollbackFailures = publication.RollBack();
                if (rollbackFailures.Count > 0)
                {
                    publishFailure.Data["directoryPublishRollback"] =
                        string.Join(" | ", rollbackFailures);
                }
                throw;
            }
        }

        public static AtomicDirectoryPublication Begin(IList<DirectoryPublishRequest> requests)
        {
            return Begin(requests, null);
        }

        public static AtomicDirectoryPublication Begin(
            IList<DirectoryPublishRequest> requests,
            string publicationRoot)
        {
            if (requests == null) throw new ArgumentNullException("requests");
            ValidateRequestSet(requests);
            string resolvedPublicationRoot = ResolvePublicationRoot(requests, publicationRoot);
            FileStream publicationLock = AcquirePublicationLock(resolvedPublicationRoot);
            try
            {
                IList<string> recoveryWarnings;
                try
                {
                    recoveryWarnings = RecoverInterruptedPublicationsUnderLock(resolvedPublicationRoot);
                }
                catch (Exception exception)
                {
                    exception.Data["directoryPublishRecovery"] = true;
                    throw;
                }
                string transactionId = Guid.NewGuid().ToString("N");
                PrepareRequests(requests, transactionId);
                ValidateNonOverlappingPublicationPaths(
                    requests,
                    "Output publication recovery paths overlap another selected target.");
                DirectoryPublicationJournal journal = new DirectoryPublicationJournal
                {
                    SchemaVersion = 1,
                    TransactionId = transactionId,
                    PublicationRoot = resolvedPublicationRoot,
                    CreatedUtc = DateTime.UtcNow.ToString("o"),
                    Phase = "publishing",
                    Requests = requests,
                    FilePath = Path.Combine(
                        resolvedPublicationRoot,
                        ".sw2urdf-publication-" + transactionId + ".json")
                };
                PersistJournal(journal);
                try
                {
                    foreach (DirectoryPublishRequest request in requests)
                    {
                        if (request.HadPreviousDirectory)
                        {
                            Directory.Move(
                                request.DestinationDirectory,
                                request.PreviousDirectory);
                            request.PublicationState = "previous_moved";
                            PersistJournal(journal);
                        }
                        Directory.Move(
                            request.StagingDirectory,
                            request.DestinationDirectory);
                        request.Published = true;
                        request.PublicationState = "published";
                        PersistJournal(journal);
                    }
                    journal.Phase = "published";
                    PersistJournal(journal);
                }
                catch (Exception publishFailure)
                {
                    List<string> rollbackFailures = RollBack(requests, journal).ToList();
                    if (rollbackFailures.Count > 0)
                    {
                        publishFailure.Data["directoryPublishRollback"] =
                            string.Join(" | ", rollbackFailures);
                    }
                    throw;
                }

                AtomicDirectoryPublication publication = new AtomicDirectoryPublication(
                    requests,
                    journal,
                    recoveryWarnings,
                    publicationLock);
                publicationLock = null;
                return publication;
            }
            finally
            {
                if (publicationLock != null)
                {
                    ReleasePublicationLock(publicationLock);
                }
            }
        }

        internal static IList<string> Commit(
            IList<DirectoryPublishRequest> requests,
            DirectoryPublicationJournal journal)
        {
            return Commit(requests, journal, null);
        }

        internal static IList<string> Commit(
            IList<DirectoryPublishRequest> requests,
            DirectoryPublicationJournal journal,
            Action beforeFinalizationUnderLock)
        {
            List<string> warnings = new List<string>();
            bool previousCleanupIncomplete = false;
            string previousPhase = journal.Phase;
            journal.Phase = "committing";
            try
            {
                PersistJournal(journal);
            }
            catch
            {
                journal.Phase = previousPhase;
                throw;
            }

            // The final root-level manifest/report must be published while the
            // output lock is held and while every previous target directory is
            // still available for rollback. If it fails, the caller can invoke
            // RollBack and restore the complete prior export.
            if (beforeFinalizationUnderLock != null)
            {
                beforeFinalizationUnderLock();
            }

            foreach (DirectoryPublishRequest request in requests)
            {
                if (string.IsNullOrWhiteSpace(request.PreviousDirectory))
                {
                    continue;
                }
                Exception cleanupFailure;
                if (!TryDeleteDirectory(request.PreviousDirectory, out cleanupFailure))
                {
                    previousCleanupIncomplete = true;
                    warnings.Add(
                        request.Label + " was published, but the previous directory " +
                        "was retained for recovery at " + request.PreviousDirectory +
                        ": " + cleanupFailure.Message);
                }
                else
                {
                    request.PreviousDirectory = null;
                }
            }
            if (previousCleanupIncomplete)
            {
                warnings.Add(
                    "The committed output recovery journal was retained so cleanup can resume at " +
                    journal.FilePath + ".");
            }
            else
            {
                TryDeleteJournal(journal, warnings);
            }
            return warnings;
        }

        public static IList<string> RecoverInterruptedPublications(string publicationRoot)
        {
            string root = Path.GetFullPath(Require(publicationRoot, "Publication root"));
            if (!Directory.Exists(root))
            {
                return new List<string>();
            }
            EnsurePlainDirectory(root, "Publication root");
            FileStream publicationLock = AcquirePublicationLock(root);
            try
            {
                return RecoverInterruptedPublicationsUnderLock(root);
            }
            finally
            {
                ReleasePublicationLock(publicationLock);
            }
        }

        private static IList<string> RecoverInterruptedPublicationsUnderLock(string root)
        {
            List<string> warnings = new List<string>();
            foreach (string journalPath in Directory.GetFiles(
                root,
                ".sw2urdf-publication-*.json",
                SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(journalPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        "An output publication journal must not be a reparse point: " +
                        journalPath);
                }

                DirectoryPublicationJournal journal;
                try
                {
                    journal = JsonConvert.DeserializeObject<DirectoryPublicationJournal>(
                        File.ReadAllText(journalPath, Encoding.UTF8));
                }
                catch (Exception exception) when (
                    exception is JsonException ||
                    exception is IOException ||
                    exception is UnauthorizedAccessException)
                {
                    throw new InvalidDataException(
                        "The interrupted output publication journal is unreadable: " +
                        journalPath,
                        exception);
                }

                ValidateJournal(journal, root, journalPath);
                journal.FilePath = journalPath;
                if (String.Equals(journal.Phase, "committing", StringComparison.Ordinal))
                {
                    CompleteInterruptedCommit(journal);
                    warnings.Add(
                        "Completed an interrupted output commit from transaction " +
                        journal.TransactionId + ".");
                }
                else
                {
                    IList<string> retained = CompleteInterruptedRollback(journal);
                    warnings.Add(
                        "Rolled back an interrupted output publication from transaction " +
                        journal.TransactionId + ".");
                    warnings.AddRange(retained);
                }
            }
            return warnings;
        }

        private static FileStream AcquirePublicationLock(string publicationRoot)
        {
            string lockPath = Path.Combine(
                publicationRoot,
                ".sw2urdf-publication.lock");
            if (File.Exists(lockPath) &&
                (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "The output publication lock must not be a reparse point: " +
                    lockPath);
            }
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
            }
            catch (IOException exception)
            {
                throw new IOException(
                    "Another export is publishing to the selected output directory. " +
                    "Wait for it to finish before retrying: " + publicationRoot,
                    exception);
            }
        }

        internal static void ReleasePublicationLock(FileStream publicationLock)
        {
            if (publicationLock == null)
            {
                return;
            }
            string lockPath = publicationLock.Name;
            publicationLock.Dispose();
            try
            {
                File.Delete(lockPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void PrepareRequests(
            IList<DirectoryPublishRequest> requests,
            string transactionId)
        {
            for (int index = 0; index < requests.Count; ++index)
            {
                DirectoryPublishRequest request = requests[index];
                string parent = Path.GetDirectoryName(request.DestinationDirectory);
                Directory.CreateDirectory(parent);
                EnsurePlainDirectory(parent, request.Label + " destination parent");
                if (File.Exists(request.DestinationDirectory))
                {
                    throw new IOException(
                        request.Label + " destination is a file: " +
                        request.DestinationDirectory);
                }

                request.HadPreviousDirectory = Directory.Exists(request.DestinationDirectory);
                request.Published = false;
                request.PublicationState = "planned";
                request.PreviousDirectory = null;
                if (!request.HadPreviousDirectory)
                {
                    continue;
                }

                EnsurePlainDirectory(
                    request.DestinationDirectory,
                    request.Label + " destination");
                request.PreviousDirectory = request.DestinationDirectory +
                    ".previous-transaction-" + transactionId + "-" +
                    index.ToString("D2");
                if (File.Exists(request.PreviousDirectory) ||
                    Directory.Exists(request.PreviousDirectory))
                {
                    throw new IOException(
                        request.Label + " recovery directory already exists: " +
                        request.PreviousDirectory);
                }
            }
        }

        private static string ResolvePublicationRoot(
            IList<DirectoryPublishRequest> requests,
            string publicationRoot)
        {
            string root;
            if (!String.IsNullOrWhiteSpace(publicationRoot))
            {
                root = Path.GetFullPath(publicationRoot.Trim());
            }
            else
            {
                List<string> paths = requests
                    .SelectMany(request => new[]
                    {
                        request.StagingDirectory,
                        request.DestinationDirectory
                    })
                    .ToList();
                root = Path.GetDirectoryName(paths[0]);
                while (!String.IsNullOrWhiteSpace(root) &&
                    paths.Any(path => !IsPathInsideOrEqual(path, root)))
                {
                    root = Path.GetDirectoryName(root);
                }
                if (String.IsNullOrWhiteSpace(root) ||
                    String.Equals(
                        root.TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetPathRoot(root).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Output publication requires a common non-root directory.");
                }
            }

            Directory.CreateDirectory(root);
            EnsurePlainDirectory(root, "Publication root");
            foreach (DirectoryPublishRequest request in requests)
            {
                if (!IsPathInsideOrEqual(request.StagingDirectory, root) ||
                    !IsPathInsideOrEqual(request.DestinationDirectory, root))
                {
                    throw new InvalidDataException(
                        request.Label +
                        " staging and destination must stay inside the publication root: " +
                        root);
                }
                EnsurePathHasNoReparsePointAncestor(
                    root,
                    request.StagingDirectory,
                    request.Label + " staging directory");
                EnsurePathHasNoReparsePointAncestor(
                    root,
                    request.DestinationDirectory,
                    request.Label + " destination directory");
            }
            return root;
        }

        private static void PersistJournal(DirectoryPublicationJournal journal)
        {
            if (journal == null) throw new ArgumentNullException("journal");
            string temporaryPath = journal.FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                string json = JsonConvert.SerializeObject(journal, Formatting.Indented);
                using (FileStream stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                using (StreamWriter writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(false),
                    4096,
                    true))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(journal.FilePath))
                {
                    File.Replace(temporaryPath, journal.FilePath, null, true);
                }
                else
                {
                    File.Move(temporaryPath, journal.FilePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void ValidateJournal(
            DirectoryPublicationJournal journal,
            string expectedRoot,
            string journalPath)
        {
            if (journal == null || journal.SchemaVersion != 1 ||
                String.IsNullOrWhiteSpace(journal.TransactionId) ||
                journal.Requests == null || journal.Requests.Count == 0)
            {
                throw new InvalidDataException(
                    "The interrupted output publication journal is invalid: " +
                    journalPath);
            }
            Guid transactionGuid;
            if (!Guid.TryParseExact(journal.TransactionId, "N", out transactionGuid) ||
                !String.Equals(
                    Path.GetFileName(journalPath),
                    ".sw2urdf-publication-" + journal.TransactionId + ".json",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The interrupted output publication journal has an invalid transaction identity: " +
                    journalPath);
            }

            string recordedRoot = Path.GetFullPath(
                Require(journal.PublicationRoot, "Journal publication root"));
            if (!String.Equals(
                recordedRoot.TrimEnd(Path.DirectorySeparatorChar),
                expectedRoot.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The interrupted output publication journal belongs to another root: " +
                    journalPath);
            }
            if (!IsSupportedJournalPhase(journal.Phase))
            {
                throw new InvalidDataException(
                    "The interrupted output publication journal has an unsupported phase: " +
                    journal.Phase);
            }

            for (int index = 0; index < journal.Requests.Count; ++index)
            {
                DirectoryPublishRequest request = journal.Requests[index];
                if (request == null)
                {
                    throw new InvalidDataException(
                        "The interrupted output publication journal contains a null request.");
                }
                request.Label = String.IsNullOrWhiteSpace(request.Label)
                    ? "Output"
                    : request.Label.Trim();
                request.StagingDirectory = Path.GetFullPath(
                    Require(request.StagingDirectory, request.Label + " staging directory"));
                request.DestinationDirectory = Path.GetFullPath(
                    Require(request.DestinationDirectory, request.Label + " destination directory"));
                if (!IsPathInsideOrEqual(request.StagingDirectory, expectedRoot) ||
                    !IsPathInsideOrEqual(request.DestinationDirectory, expectedRoot))
                {
                    throw new InvalidDataException(
                        "The interrupted output publication journal escapes its publication root.");
                }
                EnsurePathHasNoReparsePointAncestor(
                    expectedRoot,
                    request.StagingDirectory,
                    request.Label + " journal staging directory");
                EnsurePathHasNoReparsePointAncestor(
                    expectedRoot,
                    request.DestinationDirectory,
                    request.Label + " journal destination directory");
                if (File.Exists(request.StagingDirectory) ||
                    File.Exists(request.DestinationDirectory))
                {
                    throw new InvalidDataException(
                        "The interrupted output publication journal points to a file where a directory is required.");
                }
                if (!IsSupportedPublicationState(request.PublicationState))
                {
                    throw new InvalidDataException(
                        "The interrupted output publication journal has an unsupported request state: " +
                        request.PublicationState);
                }
                if (request.HadPreviousDirectory)
                {
                    request.PreviousDirectory = Path.GetFullPath(
                        Require(request.PreviousDirectory, request.Label + " previous directory"));
                    string expectedPrevious = request.DestinationDirectory +
                        ".previous-transaction-" + journal.TransactionId + "-" +
                        index.ToString("D2");
                    if (!String.Equals(
                            request.PreviousDirectory,
                            expectedPrevious,
                            StringComparison.OrdinalIgnoreCase) ||
                        !IsPathInsideOrEqual(request.PreviousDirectory, expectedRoot))
                    {
                        throw new InvalidDataException(
                            "The interrupted output publication journal contains an unsafe recovery path.");
                    }
                    EnsurePathHasNoReparsePointAncestor(
                        expectedRoot,
                        request.PreviousDirectory,
                        request.Label + " journal previous directory");
                    if (File.Exists(request.PreviousDirectory))
                    {
                        throw new InvalidDataException(
                            "The interrupted output publication recovery path is a file.");
                    }
                }
                else if (!String.IsNullOrWhiteSpace(request.PreviousDirectory))
                {
                    throw new InvalidDataException(
                        "The interrupted output publication journal has an unexpected recovery path.");
                }
            }
            ValidateNonOverlappingPublicationPaths(
                journal.Requests,
                "The interrupted output publication journal contains overlapping paths.");
            if ((String.Equals(journal.Phase, "published", StringComparison.Ordinal) ||
                String.Equals(journal.Phase, "committing", StringComparison.Ordinal)) &&
                journal.Requests.Any(request => !String.Equals(
                    request.PublicationState,
                    "published",
                    StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "The interrupted output publication journal phase is inconsistent with its request states.");
            }
        }

        private static void CompleteInterruptedCommit(DirectoryPublicationJournal journal)
        {
            foreach (DirectoryPublishRequest request in journal.Requests)
            {
                if (!Directory.Exists(request.DestinationDirectory) ||
                    File.Exists(request.DestinationDirectory))
                {
                    throw new InvalidDataException(
                        "Cannot finish interrupted commit because the published destination is missing: " +
                        request.DestinationDirectory);
                }
                if (Directory.Exists(request.StagingDirectory) ||
                    File.Exists(request.StagingDirectory))
                {
                    throw new InvalidDataException(
                        "Cannot finish interrupted commit because its staging directory still exists: " +
                        request.StagingDirectory);
                }
                EnsurePlainDirectory(
                    request.DestinationDirectory,
                    request.Label + " published destination");
                if (!String.IsNullOrWhiteSpace(request.PreviousDirectory) &&
                    Directory.Exists(request.PreviousDirectory))
                {
                    EnsurePlainDirectory(
                        request.PreviousDirectory,
                        request.Label + " previous directory");
                    Exception cleanupFailure;
                    if (!TryDeleteDirectory(request.PreviousDirectory, out cleanupFailure))
                    {
                        throw new IOException(
                            "Cannot finish interrupted commit cleanup at " +
                            request.PreviousDirectory,
                            cleanupFailure);
                    }
                }
            }
            File.Delete(journal.FilePath);
        }

        private static IList<string> CompleteInterruptedRollback(
            DirectoryPublicationJournal journal)
        {
            List<string> warnings = new List<string>();
            journal.Phase = "rolling_back";
            PersistJournal(journal);
            for (int index = journal.Requests.Count - 1; index >= 0; --index)
            {
                DirectoryPublishRequest request = journal.Requests[index];
                bool destinationExists = Directory.Exists(request.DestinationDirectory);
                bool stagingExists = Directory.Exists(request.StagingDirectory);
                bool previousExists = request.HadPreviousDirectory &&
                    Directory.Exists(request.PreviousDirectory);
                bool destinationAlreadyContainsPreviousOutput =
                    String.Equals(journal.Phase, "rolling_back", StringComparison.Ordinal) &&
                    request.HadPreviousDirectory && destinationExists && stagingExists &&
                    !previousExists;
                if (destinationExists && stagingExists &&
                    !destinationAlreadyContainsPreviousOutput)
                {
                    throw new InvalidDataException(
                        "Interrupted output recovery is ambiguous because both generated locations exist for " +
                        request.Label + ": destination=" + request.DestinationDirectory +
                        "; staging=" + request.StagingDirectory);
                }
                if (!destinationExists && !stagingExists && !previousExists)
                {
                    throw new InvalidDataException(
                        "Interrupted output recovery cannot continue because all transaction directories are missing for " +
                        request.Label + ".");
                }

                if (request.HadPreviousDirectory)
                {
                    if (previousExists)
                    {
                        if (destinationExists)
                        {
                            string retained = MoveInterruptedGeneratedDirectory(
                                request,
                                journal,
                                index);
                            warnings.Add(
                                request.Label +
                                " interrupted generated output was retained at " + retained + ".");
                        }
                        Directory.Move(
                            request.PreviousDirectory,
                            request.DestinationDirectory);
                    }
                    else if (!destinationAlreadyContainsPreviousOutput)
                    {
                        throw new InvalidDataException(
                            "Cannot restore the previous output directory for " +
                            request.Label + ": " + request.PreviousDirectory);
                    }
                }
                else if (destinationExists)
                {
                    string retained = MoveInterruptedGeneratedDirectory(
                        request,
                        journal,
                        index);
                    warnings.Add(
                        request.Label +
                        " interrupted generated output was retained at " + retained + ".");
                }

                request.Published = false;
                request.PublicationState = "rolled_back";
                PersistJournal(journal);
            }
            File.Delete(journal.FilePath);
            return warnings;
        }

        private static string MoveInterruptedGeneratedDirectory(
            DirectoryPublishRequest request,
            DirectoryPublicationJournal journal,
            int index)
        {
            EnsurePlainDirectory(
                request.DestinationDirectory,
                request.Label + " interrupted destination");
            string retainedPath = request.StagingDirectory;
            if (Directory.Exists(retainedPath) || File.Exists(retainedPath))
            {
                retainedPath = Path.Combine(
                    journal.PublicationRoot,
                    ".sw2urdf-interrupted-" + journal.TransactionId + "-" +
                    index.ToString("D2"));
            }
            if (File.Exists(retainedPath) || Directory.Exists(retainedPath))
            {
                throw new IOException(
                    "Cannot retain interrupted generated output because the recovery path exists: " +
                    retainedPath);
            }
            string parent = Path.GetDirectoryName(retainedPath);
            Directory.CreateDirectory(parent);
            EnsurePlainDirectory(parent, request.Label + " interrupted output parent");
            Directory.Move(request.DestinationDirectory, retainedPath);
            return retainedPath;
        }

        private static void TryDeleteJournal(
            DirectoryPublicationJournal journal,
            ICollection<string> warnings)
        {
            try
            {
                File.Delete(journal.FilePath);
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException)
            {
                warnings.Add(
                    "Output commit completed, but its recovery journal was retained at " +
                    journal.FilePath + ": " + exception.Message);
            }
        }

        internal static bool TryDeleteUnreferencedTransactionRoot(
            string publicationRoot,
            string transactionRoot,
            out Exception failure)
        {
            failure = null;
            string referencedBy;
            if (IsDirectoryReferencedByRecoveryJournal(
                    publicationRoot,
                    transactionRoot,
                    out referencedBy))
            {
                failure = new IOException(
                    "Transaction staging is still referenced by recovery journal " +
                    referencedBy + ".");
                return false;
            }

            return TryDeleteDirectory(transactionRoot, out failure);
        }

        private static bool IsDirectoryReferencedByRecoveryJournal(
            string publicationRoot,
            string directory,
            out string referencedBy)
        {
            referencedBy = null;
            string root = Path.GetFullPath(Require(publicationRoot, "Publication root"));
            string candidate = Path.GetFullPath(Require(directory, "Transaction root"));
            if (!Directory.Exists(root))
            {
                return false;
            }

            foreach (string journalPath in Directory.GetFiles(
                root,
                ".sw2urdf-publication-*.json",
                SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if ((File.GetAttributes(journalPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        referencedBy = journalPath;
                        return true;
                    }
                    DirectoryPublicationJournal journal =
                        JsonConvert.DeserializeObject<DirectoryPublicationJournal>(
                            File.ReadAllText(journalPath, Encoding.UTF8));
                    if (journal == null || journal.Requests == null)
                    {
                        referencedBy = journalPath;
                        return true;
                    }
                    foreach (DirectoryPublishRequest request in journal.Requests)
                    {
                        if (request == null)
                        {
                            referencedBy = journalPath;
                            return true;
                        }
                        foreach (string recoveryPath in new[]
                        {
                            request.StagingDirectory,
                            request.DestinationDirectory,
                            request.PreviousDirectory
                        })
                        {
                            if (!String.IsNullOrWhiteSpace(recoveryPath) &&
                                IsPathInsideOrEqual(Path.GetFullPath(recoveryPath), candidate))
                            {
                                referencedBy = journalPath;
                                return true;
                            }
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException ||
                    exception is JsonException ||
                    exception is ArgumentException ||
                    exception is NotSupportedException)
                {
                    // An unreadable journal means cleanup cannot prove this directory is disposable.
                    referencedBy = journalPath;
                    return true;
                }
            }
            return false;
        }

        private static bool IsSupportedJournalPhase(string phase)
        {
            return String.Equals(phase, "publishing", StringComparison.Ordinal) ||
                String.Equals(phase, "published", StringComparison.Ordinal) ||
                String.Equals(phase, "rolling_back", StringComparison.Ordinal) ||
                String.Equals(phase, "committing", StringComparison.Ordinal);
        }

        private static bool IsSupportedPublicationState(string state)
        {
            return String.Equals(state, "planned", StringComparison.Ordinal) ||
                String.Equals(state, "previous_moved", StringComparison.Ordinal) ||
                String.Equals(state, "published", StringComparison.Ordinal) ||
                String.Equals(state, "rolled_back", StringComparison.Ordinal);
        }

        private static void ValidateNonOverlappingPublicationPaths(
            IList<DirectoryPublishRequest> requests,
            string message)
        {
            for (int left = 0; left < requests.Count; ++left)
            {
                IList<string> firstPaths = PublicationPaths(requests[left]);
                for (int first = 0; first < firstPaths.Count; ++first)
                {
                    for (int second = first + 1; second < firstPaths.Count; ++second)
                    {
                        if (PathsOverlap(firstPaths[first], firstPaths[second]))
                        {
                            throw new InvalidDataException(message);
                        }
                    }
                }
                for (int right = left + 1; right < requests.Count; ++right)
                {
                    IList<string> secondPaths = PublicationPaths(requests[right]);
                    if (firstPaths.Any(first =>
                        secondPaths.Any(second => PathsOverlap(first, second))))
                    {
                        throw new InvalidDataException(message);
                    }
                }
            }
        }

        private static IList<string> PublicationPaths(DirectoryPublishRequest request)
        {
            List<string> paths = new List<string>
            {
                request.StagingDirectory,
                request.DestinationDirectory
            };
            if (!String.IsNullOrWhiteSpace(request.PreviousDirectory))
            {
                paths.Add(request.PreviousDirectory);
            }
            return paths;
        }

        private static bool IsPathInsideOrEqual(string path, string root)
        {
            string fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return String.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(
                    fullRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsurePathHasNoReparsePointAncestor(
            string root,
            string path,
            string label)
        {
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!IsPathInsideOrEqual(fullPath, fullRoot))
            {
                throw new InvalidDataException(
                    label + " escapes the publication root.");
            }

            string current = fullRoot;
            string relative = fullPath.Substring(fullRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string segment in relative.Split(new[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (File.Exists(current))
                {
                    throw new IOException(
                        label + " contains a file in its directory path: " + current);
                }
                if (!Directory.Exists(current))
                {
                    break;
                }
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        label + " must not pass through a reparse point: " + current);
                }
            }
        }

        private static void ValidateRequestSet(IList<DirectoryPublishRequest> requests)
        {
            for (int index = 0; index < requests.Count; ++index)
            {
                ValidateRequest(requests[index]);
            }

            for (int left = 0; left < requests.Count; ++left)
            {
                for (int right = left + 1; right < requests.Count; ++right)
                {
                    DirectoryPublishRequest first = requests[left];
                    DirectoryPublishRequest second = requests[right];
                    if (PathsOverlap(
                        first.DestinationDirectory,
                        second.DestinationDirectory))
                    {
                        throw new InvalidDataException(
                            "Output destinations overlap: " + first.Label + "=" +
                            first.DestinationDirectory + "; " + second.Label + "=" +
                            second.DestinationDirectory);
                    }
                    if (PathsOverlap(first.StagingDirectory, second.StagingDirectory) ||
                        PathsOverlap(first.DestinationDirectory, second.StagingDirectory) ||
                        PathsOverlap(second.DestinationDirectory, first.StagingDirectory))
                    {
                        throw new InvalidDataException(
                            "Output staging directories overlap another selected target: " +
                            first.Label + "; " + second.Label);
                    }
                }
            }
        }

        public static bool TryDeleteDirectory(string path, out Exception failure)
        {
            failure = null;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return true;
            }
            for (int attempt = 0; attempt < 3; ++attempt)
            {
                try
                {
                    Directory.Delete(path, true);
                    return true;
                }
                catch (Exception exception) when (
                    exception is IOException || exception is UnauthorizedAccessException)
                {
                    failure = exception;
                    if (attempt < 2)
                    {
                        Thread.Sleep(75 * (attempt + 1));
                    }
                }
            }
            return false;
        }

        private static void ValidateRequest(DirectoryPublishRequest request)
        {
            if (request == null) throw new ArgumentException("A publication request is null.");
            request.Label = string.IsNullOrWhiteSpace(request.Label)
                ? "Output"
                : request.Label.Trim();
            request.StagingDirectory = Path.GetFullPath(
                Require(request.StagingDirectory, request.Label + " staging directory"));
            request.DestinationDirectory = Path.GetFullPath(
                Require(request.DestinationDirectory, request.Label + " destination directory"));
            if (!Directory.Exists(request.StagingDirectory))
            {
                throw new DirectoryNotFoundException(
                    request.Label + " staging directory was not generated: " +
                    request.StagingDirectory);
            }
            EnsurePlainDirectory(request.StagingDirectory, request.Label + " staging directory");
            string stagingRoot = WithSeparator(request.StagingDirectory);
            string destinationRoot = WithSeparator(request.DestinationDirectory);
            if (string.Equals(stagingRoot, destinationRoot, StringComparison.OrdinalIgnoreCase) ||
                stagingRoot.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase) ||
                destinationRoot.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    request.Label + " staging and destination directories overlap.");
            }
            if (!string.Equals(
                Path.GetPathRoot(request.StagingDirectory),
                Path.GetPathRoot(request.DestinationDirectory),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    request.Label + " staging and destination must be on the same volume.");
            }
        }

        internal static IList<string> RollBack(
            IList<DirectoryPublishRequest> requests,
            DirectoryPublicationJournal journal)
        {
            List<string> failures = new List<string>();
            if (journal != null)
            {
                try
                {
                    journal.Phase = "rolling_back";
                    PersistJournal(journal);
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException)
                {
                    failures.Add("Could not persist rollback intent: " + exception.Message);
                }
            }
            for (int index = requests.Count - 1; index >= 0; --index)
            {
                DirectoryPublishRequest request = requests[index];
                try
                {
                    if (request.Published && Directory.Exists(request.DestinationDirectory))
                    {
                        Directory.CreateDirectory(
                            Path.GetDirectoryName(request.StagingDirectory));
                        Directory.Move(
                            request.DestinationDirectory,
                            request.StagingDirectory);
                        request.Published = false;
                    }
                    if (!string.IsNullOrWhiteSpace(request.PreviousDirectory) &&
                        Directory.Exists(request.PreviousDirectory) &&
                        !Directory.Exists(request.DestinationDirectory))
                    {
                        Directory.Move(
                            request.PreviousDirectory,
                            request.DestinationDirectory);
                    }
                    request.Published = false;
                    request.PublicationState = "rolled_back";
                    if (journal != null)
                    {
                        PersistJournal(journal);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException)
                {
                    failures.Add(
                        request.Label + ": " + exception.Message +
                        "; generated=" + request.DestinationDirectory +
                        "; previous=" + (request.PreviousDirectory ?? "<none>"));
                }
            }
            if (failures.Count == 0 && journal != null)
            {
                try
                {
                    File.Delete(journal.FilePath);
                }
                catch (Exception exception) when (
                    exception is IOException || exception is UnauthorizedAccessException)
                {
                    failures.Add(
                        "Could not remove rollback recovery journal " +
                        journal.FilePath + ": " + exception.Message);
                }
            }
            return failures;
        }

        private static void EnsurePlainDirectory(string path, string label)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(label + " must not be a reparse point: " + path);
            }
        }

        private static string Require(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(label + " is required.");
            }
            return value.Trim();
        }

        private static string WithSeparator(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
        }

        private static bool PathsOverlap(string left, string right)
        {
            string leftRoot = WithSeparator(left);
            string rightRoot = WithSeparator(right);
            return string.Equals(leftRoot, rightRoot, StringComparison.OrdinalIgnoreCase) ||
                leftRoot.StartsWith(rightRoot, StringComparison.OrdinalIgnoreCase) ||
                rightRoot.StartsWith(leftRoot, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static partial class V2ExportBridge
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();
        public static V2ExportResult Export(
            URDFPackage sourcePackage,
            URDFPackage outputPackage,
            string sourceUrdf,
            LegacyRobot legacyRobot,
            IEnumerable<ExportHelper.MeshExportRecord> meshRecords,
            ExportTargetOptions options,
            Action<V2ExportResult, ExportTargetOptions> validateTarget = null,
            Action<V2ExportResult> onAborted = null)
        {
            if (sourcePackage == null) throw new ArgumentNullException("sourcePackage");
            if (outputPackage == null) throw new ArgumentNullException("outputPackage");
            if (legacyRobot == null) throw new ArgumentNullException("legacyRobot");
            if (options == null) throw new ArgumentNullException("options");

            if (!(options.ExportRos1Legacy || options.ExportRos2 ||
                options.ExportUsdAsset || options.ExportMjcfAsset))
            {
                throw new InvalidDataException("Select at least one output target.");
            }

            RobotDocument robot = UrdfCodec.Read(sourceUrdf);
            robot.Metadata.Generator = "SolidWorks URDF Exporter Pro";
            robot.Metadata.GeneratorVersion = Versioning.Version.GetPluginVersion();
            robot.Metadata.Commit = Versioning.Version.GetCommitHash();
            robot.Metadata.SourceFormat = "solidworks-assembly";
            robot.Metadata.ModelLicense = options.ModelLicense;
            robot.Metadata.ModelAuthor = options.ModelAuthor;
            ApplyProvenance(robot, legacyRobot);

            robot.Profiles.Package = new PackageMetadataProfile
            {
                PackageName = outputPackage.PackageName,
                Version = options.PackageVersion,
                Description = options.Description,
                MaintainerName = options.MaintainerName,
                MaintainerEmail = options.MaintainerEmail,
                License = options.ModelLicense
            };
            IDictionary<string, string> targetErrors = PrepareTargetProfiles(robot, options);

            Dictionary<string, string> packageMappings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { sourcePackage.PackageName, sourcePackage.WindowsPackageDirectory }
            };
            List<BundleAdditionalFile> supplementalFiles = new List<BundleAdditionalFile>();
            AddSupplementalFile(
                supplementalFiles,
                Path.Combine(sourcePackage.WindowsConfigDirectory, "inertial_validation.csv"),
                "reports/cad/inertial_validation.csv");
            string portableMeshManifest = Path.Combine(
                sourcePackage.WindowsConfigDirectory,
                ".osurdf-portable-mesh-manifest-" + Guid.NewGuid().ToString("N") + ".csv");
            string privateRoot = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-canonical-" + Guid.NewGuid().ToString("N"));
            string privateBundle = Path.Combine(privateRoot, outputPackage.PackageName + ".osurdf");
            BundleBuildResult bundle = null;
            Exception buildFailure = null;
            try
            {
                File.WriteAllText(
                    portableMeshManifest,
                    BuildPortableMeshManifest(meshRecords, sourcePackage),
                    new UTF8Encoding(false));
                AddSupplementalFile(
                    supplementalFiles,
                    portableMeshManifest,
                    "reports/cad/mesh_manifest.csv");

                bundle = new RobotBundleBuilder().Build(
                    robot,
                    new BundleBuildOptions
                    {
                        SourceUrdfPath = sourceUrdf,
                        OutputDirectory = privateBundle,
                        Overwrite = true,
                        PackageMappings = packageMappings,
                        AdditionalFiles = supplementalFiles
                    });
            }
            catch (Exception exception)
            {
                buildFailure = exception;
                throw;
            }
            finally
            {
                if (File.Exists(portableMeshManifest))
                {
                    try
                    {
                        File.Delete(portableMeshManifest);
                    }
                    catch (Exception cleanupFailure) when (
                        (cleanupFailure is IOException || cleanupFailure is UnauthorizedAccessException))
                    {
                        if (buildFailure != null)
                        {
                            buildFailure.Data["portableMeshManifestCleanup"] = cleanupFailure.Message;
                        }
                        else
                        {
                            logger.Warn(
                                "The private mesh manifest was retained after bundle generation: " +
                                portableMeshManifest,
                                cleanupFailure);
                        }
                    }
                }
                if (buildFailure != null)
                {
                    try
                    {
                        DeletePrivateStaging(privateRoot);
                    }
                    catch (Exception cleanupFailure) when (
                        cleanupFailure is IOException ||
                        cleanupFailure is UnauthorizedAccessException)
                    {
                        buildFailure.Data["privateBundleCleanup"] = cleanupFailure.Message;
                    }
                }
            }

            Exception exportFailure = null;
            V2ExportResult result = null;
            try
            {
                result = BuildAndPublishTargets(
                    bundle.OutputDirectory,
                    outputPackage,
                    meshRecords,
                    options,
                    targetErrors,
                    validateTarget,
                    partial => { result = partial; onAborted?.Invoke(partial); });
                return result;
            }
            catch (Exception exception)
            {
                exportFailure = exception;
                throw;
            }
            finally
            {
                try
                {
                    DeletePrivateStaging(privateRoot);
                }
                catch (Exception cleanupFailure) when (
                    (cleanupFailure is IOException || cleanupFailure is UnauthorizedAccessException))
                {
                    if (exportFailure != null)
                    {
                        exportFailure.Data["privateBundleCleanup"] = cleanupFailure.Message;
                    }
                    else
                    {
                        string warning =
                            "Selected outputs were published, but private staging cleanup failed: " +
                            privateRoot + ". " + cleanupFailure.Message;
                        if (result != null)
                        {
                            result.Warnings.Add(warning);
                        }
                        logger.Warn(warning, cleanupFailure);
                    }
                }
            }
        }

        private static V2ExportResult BuildAndPublishTargets(
            string bundleDirectory,
            URDFPackage outputPackage,
            IEnumerable<ExportHelper.MeshExportRecord> meshRecords,
            ExportTargetOptions options,
            IDictionary<string, string> targetErrors,
            Action<V2ExportResult, ExportTargetOptions> validateTarget,
            Action<V2ExportResult> onAborted)
        {
            string deliveryRoot = Path.GetFullPath(outputPackage.WindowsExportRootDirectory);
            Directory.CreateDirectory(deliveryRoot);
            if ((File.GetAttributes(deliveryRoot) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The output directory must not be a reparse point: " + deliveryRoot);
            string transactionRoot = Path.Combine(deliveryRoot, ".sw2urdf-targets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(transactionRoot);
            V2ExportResult result = new V2ExportResult();
            try
            {
                List<ExportTargetJob> jobs = new List<ExportTargetJob>();
                Action<string, string, Func<string>> addJob = (name, destination, build) =>
                    jobs.Add(new ExportTargetJob
                    {
                        Name = name,
                        OutputDirectory = TrimDirectory(destination),
                        Build = () =>
                        {
                            string error;
                            if (targetErrors.TryGetValue(name, out error))
                                throw new InvalidDataException(error);
                            return build();
                        },
                        Validate = target =>
                        {
                            ExportTargetOptions selected = ForTarget(options, name);
                            // Native assets validate their converted geometry through their own adapters.
                            // ROS reports validate URDF references against this target's published files.
                            if (selected.ExportRos1Legacy || selected.ExportRos2)
                                target.DeliveryMeshRecords = BuildDeliveryMeshRecords(
                                    bundleDirectory, target, target, meshRecords, outputPackage.PackageName);
                            validateTarget?.Invoke(target, selected);
                            RefreshRosChecksums(target);
                        }
                    });
                RosPackageExporter exporter = new RosPackageExporter();
                if (options.ExportRos1Legacy)
                    addJob("ROS 1", outputPackage.WindowsPackageDirectory,
                        () => exporter.ExportRos1(new RosExportOptions
                        {
                            BundleDirectory = bundleDirectory,
                            OutputDirectory = Path.Combine(transactionRoot, "ROS1"),
                            Overwrite = true
                        }));
                if (options.ExportRos2)
                    addJob("ROS 2", outputPackage.WindowsRos2PackageDirectory,
                        () => exporter.ExportRos2(new RosExportOptions
                        {
                            BundleDirectory = bundleDirectory,
                            OutputDirectory = Path.Combine(transactionRoot, "ROS2"),
                            Overwrite = true
                        }));
                if (options.ExportUsdAsset)
                    addJob("OpenUSD", outputPackage.WindowsUsdAssetDirectory, () =>
                    {
                        string applicationRoot = ApplicationRoot();
                        UsdAssetExportResult usd = new UsdAssetExporter().Export(new UsdAssetExportOptions
                        {
                            BundleDirectory = bundleDirectory,
                            OutputDirectory = Path.Combine(transactionRoot, "USD", outputPackage.PackageName),
                            PythonExecutable = Path.Combine(applicationRoot, "tools", "openusd_runtime", "python.exe"),
                            AdapterScript = Path.Combine(applicationRoot, "tools", "usd_adapter", "osurdf_usd_adapter.py"),
                            Overwrite = true
                        });
                        return usd.OutputDirectory;
                    });
                if (options.ExportMjcfAsset)
                    addJob("MuJoCo MJCF",
                        Path.Combine(outputPackage.WindowsMjcfAssetDirectory,
                            MjcfAssetExporter.GetRobotDirectoryName(outputPackage.RobotName)), () =>
                    {
                        string applicationRoot = ApplicationRoot();
                        string lockPath = Path.Combine(applicationRoot, "tools", "mujoco_runtime.lock.json");
                        JObject runtimeLock = JObject.Parse(File.ReadAllText(lockPath, Encoding.UTF8));
                        string version = runtimeLock.Value<string>("version");
                        if (runtimeLock.Value<int?>("schemaVersion") != 1 || string.IsNullOrWhiteSpace(version))
                            throw new InvalidDataException("The bundled MuJoCo runtime lock is invalid: " + lockPath);
                        string runtime = Path.Combine(applicationRoot, "tools", "mujoco_runtime");
                        MjcfExportResult mjcf = new MjcfAssetExporter().Export(new MjcfExportOptions
                        {
                            BundleDirectory = bundleDirectory,
                            OutputDirectory = transactionRoot,
                            Overwrite = true,
                            CompilerValidator = new BundledMjcfCompilerValidator(
                                Path.Combine(runtime, "compile.exe"), Path.Combine(runtime, "testspeed.exe"), version)
                        });
                        if (!string.Equals(mjcf.OfficialCompilationStatus, "passed", StringComparison.Ordinal))
                            throw new InvalidDataException("The MJCF asset did not pass official MuJoCo validation.");
                        return mjcf.OutputDirectory;
                    });
                result = IndependentTargetExport.Run(deliveryRoot, jobs,
                    partial => { result = partial; onAborted?.Invoke(partial); });
                return result;
            }
            finally
            {
                Exception cleanupFailure;
                if (!AtomicDirectoryPublisher.TryDeleteUnreferencedTransactionRoot(
                    deliveryRoot, transactionRoot, out cleanupFailure))
                {
                    string warning = "Temporary output files were retained at " + transactionRoot + ": " +
                        cleanupFailure.Message;
                    result.Warnings.Add(warning);
                    logger.Warn(warning, cleanupFailure);
                }
            }
        }

        private static string TrimDirectory(string path)
        {
            return Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        public static void RefreshRosChecksums(V2ExportResult result)
        {
            if (result == null)
            {
                return;
            }
            if (!string.IsNullOrWhiteSpace(result.Ros1Directory))
            {
                RosPackageExporter.RefreshChecksums(result.Ros1Directory);
            }
            if (!string.IsNullOrWhiteSpace(result.Ros2Directory))
            {
                RosPackageExporter.RefreshChecksums(result.Ros2Directory);
            }
        }


        private static IList<ExportHelper.MeshExportRecord> BuildDeliveryMeshRecords(
            string bundleDirectory,
            V2ExportResult stagingResult,
            V2ExportResult deliveryResult,
            IEnumerable<ExportHelper.MeshExportRecord> records,
            string packageName)
        {
            RobotDocument bundledRobot = RobotJson.Read(
                Path.Combine(bundleDirectory, RobotBundleLayout.RobotJsonFile));
            Dictionary<string, LinkDocument> links = bundledRobot.Links
                .Where(link => link != null)
                .ToDictionary(link => link.Name, StringComparer.Ordinal);
            bool packageUri = !string.IsNullOrWhiteSpace(deliveryResult.Ros1Directory) ||
                !string.IsNullOrWhiteSpace(deliveryResult.Ros2Directory);
            string stagingRoot = !string.IsNullOrWhiteSpace(stagingResult.Ros1Directory)
                ? stagingResult.Ros1Directory
                : !string.IsNullOrWhiteSpace(stagingResult.Ros2Directory)
                    ? stagingResult.Ros2Directory
                    : !string.IsNullOrWhiteSpace(stagingResult.UsdDirectory)
                        ? stagingResult.UsdDirectory
                        : !string.IsNullOrWhiteSpace(stagingResult.MjcfDirectory)
                            ? stagingResult.MjcfDirectory
                            : bundleDirectory;
            string deliveryRoot = !string.IsNullOrWhiteSpace(deliveryResult.Ros1Directory)
                ? deliveryResult.Ros1Directory
                : !string.IsNullOrWhiteSpace(deliveryResult.Ros2Directory)
                    ? deliveryResult.Ros2Directory
                    : !string.IsNullOrWhiteSpace(deliveryResult.UsdDirectory)
                        ? deliveryResult.UsdDirectory
                        : !string.IsNullOrWhiteSpace(deliveryResult.MjcfDirectory)
                            ? deliveryResult.MjcfDirectory
                            : bundleDirectory;
            List<ExportHelper.MeshExportRecord> mapped =
                new List<ExportHelper.MeshExportRecord>();
            foreach (ExportHelper.MeshExportRecord record in
                records ?? Enumerable.Empty<ExportHelper.MeshExportRecord>())
            {
                LinkDocument link;
                if (!links.TryGetValue(record.LinkName, out link))
                {
                    throw new InvalidDataException(
                        "The canonical Robot Bundle has no link for exported mesh evidence: " +
                        record.LinkName);
                }
                GeometryDocument visual = (link.Visuals ?? new List<VisualDocument>())
                    .Where(item => item != null && item.Geometry != null)
                    .Select(item => item.Geometry)
                    .FirstOrDefault(IsMeshGeometry);
                GeometryDocument collision = (link.Collisions ?? new List<CollisionDocument>())
                    .Where(item => item != null && item.Geometry != null)
                    .Select(item => item.Geometry)
                    .FirstOrDefault(IsMeshGeometry);
                string visualRelative = visual == null ? null : visual.Uri;
                string collisionRelative = collision == null ? null : collision.Uri;
                string visualStagingPath = DeliveryAssetPath(stagingRoot, visualRelative);
                string collisionStagingPath = DeliveryAssetPath(stagingRoot, collisionRelative);
                string visualPath = DeliveryAssetPath(deliveryRoot, visualRelative);
                string collisionPath = DeliveryAssetPath(deliveryRoot, collisionRelative);
                bool visualExists = !string.IsNullOrWhiteSpace(visualStagingPath) &&
                    File.Exists(visualStagingPath);
                bool collisionExists = !string.IsNullOrWhiteSpace(collisionStagingPath) &&
                    File.Exists(collisionStagingPath);
                mapped.Add(new ExportHelper.MeshExportRecord(
                    record.LinkName,
                    record.CollisionStrategy,
                    record.CollisionEffectiveStrategy,
                    record.CollisionGeometryType,
                    record.CollisionNotes,
                    record.MeshFormat,
                    DeliveryAssetUri(visualRelative, packageName, packageUri),
                    DeliveryAssetUri(collisionRelative, packageName, packageUri),
                    visualPath,
                    collisionPath,
                    visualExists,
                    collisionExists,
                    visualExists ? (long?)new FileInfo(visualStagingPath).Length : null,
                    collisionExists ? (long?)new FileInfo(collisionStagingPath).Length : null,
                    record.VisualTriangles,
                    record.CollisionTriangles,
                    record.StlStats,
                    record.CollisionUrdfReference));
            }
            return mapped;
        }

        private static bool IsMeshGeometry(GeometryDocument geometry)
        {
            return geometry != null &&
                string.Equals(geometry.Type, "mesh", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(geometry.Uri);
        }

        private static string DeliveryAssetUri(string relative, string packageName, bool packageUri)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                return string.Empty;
            }
            return packageUri ? "package://" + packageName + "/" + relative : relative;
        }

        private static string DeliveryAssetPath(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                return string.Empty;
            }
            string normalized = relative.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.Split('/').Any(segment =>
                    segment.Length == 0 || segment == "." || segment == ".."))
            {
                throw new InvalidDataException("Unsafe canonical bundle asset path: " + relative);
            }
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(
                fullRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Canonical bundle asset escapes its delivery root: " + relative);
            }
            return path;
        }

        private static string BuildPortableMeshManifest(
            IEnumerable<ExportHelper.MeshExportRecord> records,
            URDFPackage sourcePackage)
        {
            List<ExportHelper.MeshExportRecord> portableRecords =
                new List<ExportHelper.MeshExportRecord>();
            foreach (ExportHelper.MeshExportRecord record in
                records ?? Enumerable.Empty<ExportHelper.MeshExportRecord>())
            {
                portableRecords.Add(new ExportHelper.MeshExportRecord(
                    record.LinkName,
                    record.CollisionStrategy,
                    record.CollisionEffectiveStrategy,
                    record.CollisionGeometryType,
                    record.CollisionNotes,
                    record.MeshFormat,
                    record.VisualUri,
                    record.CollisionUri,
                    PortableEvidencePath(record.VisualWindowsPath, sourcePackage),
                    PortableEvidencePath(record.CollisionWindowsPath, sourcePackage),
                    record.VisualExists,
                    record.CollisionExists,
                    record.VisualBytes,
                    record.CollisionBytes,
                    record.VisualTriangles,
                    record.CollisionTriangles,
                    record.StlStats,
                    record.CollisionUrdfReference));
            }
            return ExportHelper.BuildMeshManifestCsv(portableRecords);
        }

        private static string PortableEvidencePath(string path, URDFPackage sourcePackage)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            string root = Path.GetFullPath(sourcePackage.WindowsPackageDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return "external-path-redacted";
            }
            string relative = fullPath.Substring(root.Length).Replace('\\', '/');
            return "package://" + sourcePackage.PackageName + "/" + relative;
        }

        private static void AddSupplementalFile(
            ICollection<BundleAdditionalFile> files,
            string sourcePath,
            string bundlePath)
        {
            if (File.Exists(sourcePath))
            {
                files.Add(new BundleAdditionalFile
                {
                    SourcePath = sourcePath,
                    BundlePath = bundlePath,
                    Role = "cad-validation-report"
                });
            }
        }

        private static void ApplyProvenance(RobotDocument robot, LegacyRobot legacyRobot)
        {
            Dictionary<string, LegacyJoint> joints = new Dictionary<string, LegacyJoint>(StringComparer.Ordinal);
            CollectJoints(legacyRobot.BaseLink, joints);
            foreach (JointDocument target in robot.Joints)
            {
                LegacyJoint source;
                if (!joints.TryGetValue(target.Name, out source))
                {
                    target.Source = new SourceProvenance
                    {
                        Kind = "legacy_configuration",
                        Evidence = "No matching SolidWorks joint metadata was found.",
                        UserConfirmed = false
                    };
                    continue;
                }
                target.Source = new SourceProvenance
                {
                    Kind = string.IsNullOrWhiteSpace(source.ConfigurationSource)
                        ? "legacy_configuration"
                        : source.ConfigurationSource,
                    Evidence = source.ConfigurationEvidence,
                    Reference = BuildReference(source),
                    UserConfirmed = source.ConfigurationUserConfirmed
                };
            }
            foreach (LinkDocument link in robot.Links)
            {
                link.Source = new SourceProvenance
                {
                    Kind = "solidworks_components",
                    Evidence = "Geometry and mass properties were exported from the configured SolidWorks Link.",
                    UserConfirmed = true
                };
            }
        }

        private static void CollectJoints(LegacyLink link, IDictionary<string, LegacyJoint> joints)
        {
            if (link == null) return;
            if (link.Parent != null && link.Joint != null && !string.IsNullOrWhiteSpace(link.Joint.Name))
            {
                joints[link.Joint.Name] = link.Joint;
            }
            foreach (LegacyLink child in link.Children)
            {
                CollectJoints(child, joints);
            }
        }

        private static string BuildReference(LegacyJoint joint)
        {
            return joint.AxisReference == null
                ? null
                : "axisReference=" + joint.AxisReference.IdentityKey;
        }

        private static void DeletePrivateStaging(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }
            string temp = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
                target.Length <= temp.Length)
            {
                throw new InvalidDataException(
                    "Refusing to delete a private staging directory outside the system temp root: " + path);
            }
            Exception failure;
            if (!AtomicDirectoryPublisher.TryDeleteDirectory(path, out failure))
            {
                throw new IOException(
                    "Could not remove private canonical staging after three attempts: " + path,
                    failure);
            }
        }

        private static string ApplicationRoot()
        {
            string result = Path.GetDirectoryName(typeof(V2ExportBridge).Assembly.Location);
            if (string.IsNullOrWhiteSpace(result))
            {
                throw new InvalidDataException(
                    "The plugin installation directory could not be resolved for asset export.");
            }
            return result;
        }

        private static T ReadStrictProfile<T>(string path, string label) where T : class
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                object profile;
                if (typeof(T) == typeof(Ros2ControlProfile))
                {
                    profile = RobotJson.DeserializeRos2ControlProfile(json);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Unsupported strict profile type: " + typeof(T).FullName);
                }
                return (T)profile;
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(label + " profile JSON does not match its schema.", exception);
            }
        }
    }
}
