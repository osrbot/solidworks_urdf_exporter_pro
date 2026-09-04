using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SW2URDF.Utilities;

namespace SW2URDF.URDFExport
{
    internal sealed class ExportTargetJob
    {
        public string Name { get; set; }
        public string OutputDirectory { get; set; }
        public Func<string> Build { get; set; }
        public Action<V2ExportResult> Validate { get; set; }
    }

    internal static class IndependentTargetExport
    {
        internal static V2ExportResult Run(string outputRoot, IList<ExportTargetJob> jobs,
            Action<V2ExportResult> onAborted = null)
        {
            ValidateDestinations(outputRoot, jobs);
            V2ExportResult result = new V2ExportResult();
            foreach (ExportTargetJob job in jobs)
            {
                AtomicDirectoryPublication publication = null;
                bool previousExisted = Directory.Exists(job.OutputDirectory);
                string phase = "generation";
                try
                {
                    string staging = job.Build();
                    V2ExportResult target = new V2ExportResult();
                    SetDirectory(target, job.Name, job.OutputDirectory);
                    phase = "publication";
                    publication = AtomicDirectoryPublisher.Begin(new[]
                    {
                        new DirectoryPublishRequest
                        {
                            Label = job.Name,
                            StagingDirectory = staging,
                            DestinationDirectory = job.OutputDirectory
                        }
                    }, outputRoot);
                    phase = "validation";
                    job.Validate?.Invoke(target);
                    phase = "commit";
                    foreach (string warning in publication.Commit()) result.Warnings.Add(warning);
                    publication = null;
                    SetDirectory(result, job.Name, job.OutputDirectory);
                    foreach (string report in target.Reports) result.Reports.Add(report);
                    foreach (string warning in target.Warnings) result.Warnings.Add(warning);
                    if (result.DeliveryMeshRecords.Count == 0)
                        result.DeliveryMeshRecords = target.DeliveryMeshRecords;
                    result.Targets.Add(new ExportTargetResult(
                        job.Name, job.OutputDirectory, true, String.Empty, false));
                }
                catch (Exception exception)
                {
                    List<string> rollbackFailures = new List<string>();
                    if (exception.Data.Contains("directoryPublishRollback"))
                        rollbackFailures.Add(Convert.ToString(exception.Data["directoryPublishRollback"]));
                    if (publication != null)
                    {
                        try { rollbackFailures.AddRange(publication.RollBack()); }
                        catch (Exception rollbackException) when (IsTargetFailure(rollbackException))
                        {
                            rollbackFailures.Add(rollbackException.Message);
                        }
                    }
                    string error = "[" + phase + "] " + exception.Message;
                    bool recoveryRequired = exception.Data.Contains("directoryPublishRecovery");
                    if (recoveryRequired) error += " | Recovery required; previous output state is not confirmed.";
                    if (rollbackFailures.Count > 0)
                        error += " | Recovery required: " + String.Join(" | ", rollbackFailures);
                    result.Targets.Add(new ExportTargetResult(
                        job.Name, job.OutputDirectory, false, error,
                        previousExisted && !recoveryRequired && rollbackFailures.Count == 0 &&
                        Directory.Exists(job.OutputDirectory)));
                    if (!IsTargetFailure(exception))
                    {
                        foreach (ExportTargetJob pending in jobs.Skip(result.Targets.Count))
                            result.Targets.Add(new ExportTargetResult(pending.Name, pending.OutputDirectory,
                                false, "Not attempted because the export was interrupted.", false));
                        result.Warnings.Add("Export interrupted; already committed targets were retained.");
                        onAborted?.Invoke(result);
                        throw;
                    }
                    Logger.GetLogger().Error(job.Name + " export failed; continuing other targets.", exception);
                }
            }
            return result;
        }

        internal static bool IsTargetFailure(Exception exception)
        {
            return !(exception is OperationCanceledException || exception is OutOfMemoryException ||
                exception is AccessViolationException);
        }

        internal static void SetDirectory(V2ExportResult result, string target, string directory)
        {
            switch (target)
            {
                case "ROS 1": result.Ros1Directory = directory; break;
                case "ROS 2": result.Ros2Directory = directory; break;
                case "OpenUSD": result.UsdDirectory = directory; break;
                case "MuJoCo MJCF": result.MjcfDirectory = directory; break;
                default: throw new ArgumentException("Unknown export target: " + target);
            }
        }

        private static void ValidateDestinations(string outputRoot, IList<ExportTargetJob> jobs)
        {
            if (jobs == null || jobs.Count == 0)
                throw new ArgumentException("At least one export job is required.");
            string root = Path.GetFullPath(outputRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            List<string> paths = new List<string>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ExportTargetJob job in jobs)
            {
                if (job == null || job.Build == null || !names.Add(job.Name))
                    throw new ArgumentException("Export jobs must have unique names and builders.");
                SetDirectory(new V2ExportResult(), job.Name, job.OutputDirectory);
                string path = Path.GetFullPath(job.OutputDirectory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
                    paths.Any(other => path.StartsWith(other, StringComparison.OrdinalIgnoreCase) ||
                        other.StartsWith(path, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Target output directories must be distinct children of the output root.");
                paths.Add(path);
            }
        }
    }
}
