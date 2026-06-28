using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SW2URDF.URDFExport
{
    public partial class ExportHelper
    {
        private const string ExportReportFileName = "export_report.md";

        internal static void WriteExportReport(
            URDFPackage package,
            string ros1UrdfFileName,
            IEnumerable<InertialValidationRecord> inertialRecords,
            IEnumerable<MeshExportRecord> meshRecords,
            bool exportMeshes,
            MeshExportFormat meshFormat,
            TimeSpan elapsed)
        {
            string report = BuildExportReport(
                package,
                ros1UrdfFileName,
                inertialRecords,
                meshRecords,
                exportMeshes,
                meshFormat,
                elapsed);

            string ros1ReportFileName = Path.Combine(package.WindowsConfigDirectory, ExportReportFileName);
            Directory.CreateDirectory(package.WindowsConfigDirectory);
            File.WriteAllText(ros1ReportFileName, report, new UTF8Encoding(false));
            logger.Info("Wrote ROS 1 export report to " + ros1ReportFileName);

            string ros2ReportFileName = Path.Combine(package.WindowsRos2ConfigDirectory, ExportReportFileName);
            Directory.CreateDirectory(package.WindowsRos2ConfigDirectory);
            File.WriteAllText(ros2ReportFileName, report, new UTF8Encoding(false));
            logger.Info("Wrote ROS 2 export report to " + ros2ReportFileName);
        }

        internal static string BuildExportReport(
            URDFPackage package,
            string ros1UrdfFileName,
            IEnumerable<InertialValidationRecord> inertialRecords,
            IEnumerable<MeshExportRecord> meshRecords,
            bool exportMeshes,
            MeshExportFormat meshFormat,
            TimeSpan elapsed)
        {
            List<InertialValidationRecord> inertialRows =
                inertialRecords == null ? new List<InertialValidationRecord>() : inertialRecords.ToList();
            List<MeshExportRecord> meshRows =
                meshRecords == null ? new List<MeshExportRecord>() : meshRecords.ToList();

            string ros2UrdfFileName = Path.Combine(package.WindowsRos2RobotsDirectory, package.RobotName + ".urdf");
            UrdfInspection ros1Urdf = InspectUrdfFile(
                "ROS 1",
                ros1UrdfFileName,
                package.PackageName,
                package.WindowsPackageDirectory);
            UrdfInspection ros2Urdf = InspectUrdfFile(
                "ROS 2",
                ros2UrdfFileName,
                package.Ros2PackageName,
                package.WindowsRos2PackageDirectory);
            List<PackageCheck> packageChecks = BuildPackageChecks(package, ros1UrdfFileName, ros2UrdfFileName, exportMeshes);
            List<PackageParityCheck> packageParityChecks = BuildPackageParityChecks(package, exportMeshes);
            List<string> findings = BuildExportFindings(
                ros1Urdf,
                ros2Urdf,
                packageChecks,
                packageParityChecks,
                inertialRows,
                meshRows,
                exportMeshes);

            bool hasFailure = findings.Any(f => f.StartsWith("FAIL:", StringComparison.Ordinal));
            bool hasWarning = findings.Any(f => f.StartsWith("WARN:", StringComparison.Ordinal));
            string status = hasFailure ? "FAIL" : hasWarning ? "WARN" : "PASS";

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# SW2URDF Export Report");
            builder.AppendLine();
            builder.AppendLine("Status: " + status);
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            builder.AppendLine("Plugin version: " + Versioning.Version.GetPluginVersion());
            builder.AppendLine("Commit version: " + Versioning.Version.GetCommitVersion());
            builder.AppendLine("Commit hash: " + Versioning.Version.GetCommitHash());
            builder.AppendLine("Build version: " + Versioning.Version.GetBuildVersion());
            builder.AppendLine("Build time UTC: " + Versioning.Version.GetBuildTimeUtc());
            builder.AppendLine("Dirty state: " + Versioning.Version.GetDirtyState());
            builder.AppendLine("Robot name: " + package.RobotName);
            builder.AppendLine("ROS package: " + package.PackageName);
            builder.AppendLine("Export meshes: " + (exportMeshes ? "true" : "false"));
            builder.AppendLine("Mesh format: " + meshFormat);
            builder.AppendLine("Export parameters: " +
                BuildExportParameterSummary(inertialRows, meshRows, exportMeshes, meshFormat));
            builder.AppendLine("Elapsed: " + Utilities.OperationHeartbeat.FormatElapsed(elapsed));
            builder.AppendLine();

            AppendHealthSummarySection(
                builder,
                status,
                ros1Urdf,
                ros2Urdf,
                packageChecks,
                packageParityChecks,
                inertialRows,
                meshRows,
                findings,
                exportMeshes,
                meshFormat);
            AppendExportParametersSection(
                builder,
                package,
                ros1UrdfFileName,
                ros2UrdfFileName,
                inertialRows,
                meshRows,
                exportMeshes,
                meshFormat);
            AppendUrdfSection(builder, ros1Urdf);
            AppendUrdfSection(builder, ros2Urdf);
            AppendPackageSection(builder, packageChecks);
            AppendPackageParitySection(builder, packageParityChecks);
            AppendInertialSection(builder, inertialRows);
            AppendMeshSection(builder, meshRows);
            AppendStlReductionSection(builder, meshRows);
            AppendCollisionStrategySection(builder, meshRows);
            AppendFindingsSection(builder, findings);

            return builder.ToString();
        }

        private static List<string> BuildExportFindings(
            UrdfInspection ros1Urdf,
            UrdfInspection ros2Urdf,
            IEnumerable<PackageCheck> packageChecks,
            IEnumerable<PackageParityCheck> packageParityChecks,
            IEnumerable<InertialValidationRecord> inertialRows,
            IEnumerable<MeshExportRecord> meshRows,
            bool exportMeshes)
        {
            List<string> findings = new List<string>();
            AddUrdfFindings(findings, ros1Urdf, exportMeshes);
            AddUrdfFindings(findings, ros2Urdf, exportMeshes);

            foreach (PackageCheck check in packageChecks)
            {
                if (!check.Exists)
                {
                    findings.Add((check.Critical ? "FAIL: " : "WARN: ") + check.Name + " is missing at " + check.Path);
                }
            }
            foreach (PackageParityCheck check in packageParityChecks)
            {
                if (!check.Matches)
                {
                    findings.Add((check.Critical ? "FAIL: " : "WARN: ") +
                        "ROS package parity mismatch for " + check.Category + "/" + check.RelativePath +
                        ": ROS1=" + FormatBool(check.Ros1Exists) +
                        ", ROS2=" + FormatBool(check.Ros2Exists));
                }
            }

            List<InertialValidationRecord> inertialList = inertialRows.ToList();
            if (inertialList.Count == 0)
            {
                findings.Add("WARN: No inertial validation rows were produced.");
            }
            foreach (IGrouping<string, InertialValidationRecord> linkGroup in
                inertialList.Where(r => String.Equals(r.Row.Status, "FAIL", StringComparison.Ordinal))
                    .GroupBy(r => r.LinkName))
            {
                findings.Add("FAIL: Inertial validation failed for link " + linkGroup.Key +
                    " (" + linkGroup.Count() + " rows).");
            }
            foreach (IGrouping<string, InertialValidationRecord> linkGroup in
                inertialList.Where(r => r.Row.IsWarning).GroupBy(r => r.LinkName))
            {
                findings.Add("WARN: Inertial validation warning for link " + linkGroup.Key +
                    " (" + linkGroup.Count() + " rows).");
            }

            List<MeshExportRecord> meshList = meshRows.ToList();
            if (meshList.Count == 0)
            {
                findings.Add(exportMeshes
                    ? "WARN: No mesh manifest rows were produced."
                    : "WARN: Mesh export was disabled; mesh files were not expected.");
            }
            foreach (MeshExportRecord record in meshList)
            {
                if (CollisionStrategyChanged(record))
                {
                    findings.Add("WARN: Collision strategy for link " + record.LinkName +
                        " requested " + record.CollisionStrategy +
                        " but exported " + record.CollisionEffectiveStrategy +
                        " (" + record.CollisionNotes + ").");
                }
                if (!record.VisualExists)
                {
                    findings.Add((exportMeshes ? "FAIL: " : "WARN: ") +
                        "Visual mesh for link " + record.LinkName + " is missing at " +
                        record.VisualWindowsPath);
                }
                if (!record.CollisionExists)
                {
                    findings.Add((exportMeshes ? "FAIL: " : "WARN: ") +
                        "Collision mesh for link " + record.LinkName + " is missing at " +
                        record.CollisionWindowsPath);
                }
                if (String.Equals(record.MeshFormat, MeshExportFormat.STL.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    if (record.VisualExists && !record.VisualTriangles.HasValue)
                    {
                        findings.Add("WARN: Visual STL triangle count for link " + record.LinkName +
                            " could not be read at " + record.VisualWindowsPath + ".");
                    }
                    if (record.CollisionExists && !record.CollisionTriangles.HasValue)
                    {
                        findings.Add("WARN: Collision STL triangle count for link " + record.LinkName +
                            " could not be read at " + record.CollisionWindowsPath + ".");
                    }
                }
                if (record.StlStats != null &&
                    record.StlStats.EstimateErrorPercent.HasValue &&
                    Math.Abs(record.StlStats.EstimateErrorPercent.Value) > 50.0)
                {
                    findings.Add("WARN: STL estimate error for link " + record.LinkName +
                        " is " + record.StlStats.EstimateErrorPercent.Value.ToString("0.##", CultureInfo.InvariantCulture) +
                        "%.");
                }
            }

            return findings;
        }

        private static void AddUrdfFindings(List<string> findings, UrdfInspection inspection, bool exportMeshes)
        {
            if (!inspection.Exists)
            {
                findings.Add("FAIL: " + inspection.Label + " URDF is missing at " + inspection.FileName);
                return;
            }
            if (!inspection.XmlValid)
            {
                findings.Add("FAIL: " + inspection.Label + " URDF XML could not be parsed: " +
                    inspection.ParseError);
                return;
            }
            if (!inspection.RootIsRobot)
            {
                findings.Add("FAIL: " + inspection.Label + " URDF root element is not robot.");
            }
            foreach (string name in inspection.DuplicateLinkNames)
            {
                findings.Add("FAIL: " + inspection.Label + " URDF contains duplicate link name: " + name);
            }
            foreach (string name in inspection.DuplicateJointNames)
            {
                findings.Add("FAIL: " + inspection.Label + " URDF contains duplicate joint name: " + name);
            }

            foreach (MeshReference reference in inspection.MeshReferences.Where(r => !r.Exists))
            {
                findings.Add((exportMeshes ? "FAIL: " : "WARN: ") +
                    inspection.Label + " mesh reference is unresolved: " + reference.Uri);
            }
        }

        private static List<PackageCheck> BuildPackageChecks(
            URDFPackage package,
            string ros1UrdfFileName,
            string ros2UrdfFileName,
            bool exportMeshes)
        {
            List<PackageCheck> checks = new List<PackageCheck>();
            AddDirectoryCheck(checks, "ROS 1 package directory", package.WindowsPackageDirectory, true);
            AddFileCheck(checks, "ROS 1 CMakeLists.txt", package.WindowsCMakeLists, true);
            AddFileCheck(checks, "ROS 1 package.xml", Path.Combine(package.WindowsPackageDirectory, "package.xml"), true);
            AddFileCheck(checks, "ROS 1 URDF", ros1UrdfFileName, true);
            AddDirectoryCheck(checks, "ROS 1 config directory", package.WindowsConfigDirectory, true);
            AddDirectoryCheck(checks, "ROS 1 launch directory", package.WindowsLaunchDirectory, true);
            AddFileCheck(checks, "ROS 1 display.launch", Path.Combine(package.WindowsLaunchDirectory, "display.launch"), true);
            AddFileCheck(checks, "ROS 1 gazebo.launch", Path.Combine(package.WindowsLaunchDirectory, "gazebo.launch"), true);
            AddDirectoryCheck(checks, "ROS 1 meshes directory", package.WindowsMeshesDirectory, exportMeshes);
            AddDirectoryCheck(checks, "ROS 1 visual meshes directory", Path.Combine(package.WindowsMeshesDirectory, "visual"), exportMeshes);
            AddDirectoryFilesCheck(checks, "ROS 1 visual mesh files", Path.Combine(package.WindowsMeshesDirectory, "visual"), exportMeshes);
            AddDirectoryCheck(checks, "ROS 1 collision meshes directory", Path.Combine(package.WindowsMeshesDirectory, "collision"), exportMeshes);
            AddDirectoryFilesCheck(checks, "ROS 1 collision mesh files", Path.Combine(package.WindowsMeshesDirectory, "collision"), exportMeshes);
            AddFileCheck(checks, "ROS 1 inertial validation CSV",
                Path.Combine(package.WindowsConfigDirectory, "inertial_validation.csv"), true);
            AddFileCheck(checks, "ROS 1 mesh manifest CSV",
                Path.Combine(package.WindowsConfigDirectory, "mesh_manifest.csv"), true);

            AddDirectoryCheck(checks, "ROS 2 package directory", package.WindowsRos2PackageDirectory, true);
            AddFileCheck(checks, "ROS 2 package.xml", Path.Combine(package.WindowsRos2PackageDirectory, "package.xml"), true);
            AddFileCheck(checks, "ROS 2 setup.py", Path.Combine(package.WindowsRos2PackageDirectory, "setup.py"), true);
            AddFileCheck(checks, "ROS 2 resource marker",
                Path.Combine(package.WindowsRos2ResourceDirectory, package.Ros2PackageName), true);
            AddFileCheck(checks, "ROS 2 URDF", ros2UrdfFileName, true);
            AddDirectoryCheck(checks, "ROS 2 config directory", package.WindowsRos2ConfigDirectory, true);
            AddFileCheck(checks, "ROS 2 display.launch.py", Path.Combine(package.WindowsRos2LaunchDirectory, "display.launch.py"), true);
            AddFileCheck(checks, "ROS 2 gazebo.launch.py", Path.Combine(package.WindowsRos2LaunchDirectory, "gazebo.launch.py"), true);
            AddDirectoryCheck(checks, "ROS 2 meshes directory", package.WindowsRos2MeshesDirectory, exportMeshes);
            AddDirectoryCheck(checks, "ROS 2 visual meshes directory", Path.Combine(package.WindowsRos2MeshesDirectory, "visual"), exportMeshes);
            AddDirectoryFilesCheck(checks, "ROS 2 visual mesh files", Path.Combine(package.WindowsRos2MeshesDirectory, "visual"), exportMeshes);
            AddDirectoryCheck(checks, "ROS 2 collision meshes directory", Path.Combine(package.WindowsRos2MeshesDirectory, "collision"), exportMeshes);
            AddDirectoryFilesCheck(checks, "ROS 2 collision mesh files", Path.Combine(package.WindowsRos2MeshesDirectory, "collision"), exportMeshes);
            AddFileCheck(checks, "ROS 2 inertial validation CSV",
                Path.Combine(package.WindowsRos2ConfigDirectory, "inertial_validation.csv"), true);
            AddFileCheck(checks, "ROS 2 mesh manifest CSV",
                Path.Combine(package.WindowsRos2ConfigDirectory, "mesh_manifest.csv"), true);
            return checks;
        }

        private static List<PackageParityCheck> BuildPackageParityChecks(
            URDFPackage package,
            bool exportMeshes)
        {
            List<PackageParityCheck> checks = new List<PackageParityCheck>();
            AddPackageParityFileCheck(
                checks,
                "package",
                "package.xml",
                package.WindowsPackageDirectory,
                package.WindowsRos2PackageDirectory,
                true);
            AddPackageParityFileCheck(
                checks,
                "build",
                "ROS1 CMakeLists.txt / ROS2 setup.py",
                package.WindowsPackageDirectory,
                "CMakeLists.txt",
                package.WindowsRos2PackageDirectory,
                "setup.py",
                true);
            AddPackageParityFileCheck(
                checks,
                "urdf",
                package.RobotName + ".urdf",
                package.WindowsRobotsDirectory,
                package.WindowsRos2RobotsDirectory,
                true);
            AddPackageParityFileCheck(
                checks,
                "launch",
                "ROS1 display.launch / ROS2 display.launch.py",
                package.WindowsLaunchDirectory,
                "display.launch",
                package.WindowsRos2LaunchDirectory,
                "display.launch.py",
                true);
            AddPackageParityFileCheck(
                checks,
                "launch",
                "ROS1 gazebo.launch / ROS2 gazebo.launch.py",
                package.WindowsLaunchDirectory,
                "gazebo.launch",
                package.WindowsRos2LaunchDirectory,
                "gazebo.launch.py",
                true);
            AddPackageParityFileCheck(
                checks,
                "config",
                "inertial_validation.csv",
                package.WindowsConfigDirectory,
                package.WindowsRos2ConfigDirectory,
                true);
            AddPackageParityFileCheck(
                checks,
                "config",
                "mesh_manifest.csv",
                package.WindowsConfigDirectory,
                package.WindowsRos2ConfigDirectory,
                true);
            if (exportMeshes)
            {
                AddPackageParityDirectoryChecks(
                    checks,
                    "meshes/visual",
                    Path.Combine(package.WindowsMeshesDirectory, "visual"),
                    Path.Combine(package.WindowsRos2MeshesDirectory, "visual"),
                    true);
                AddPackageParityDirectoryChecks(
                    checks,
                    "meshes/collision",
                    Path.Combine(package.WindowsMeshesDirectory, "collision"),
                    Path.Combine(package.WindowsRos2MeshesDirectory, "collision"),
                    true);
            }
            AddPackageParityDirectoryChecks(
                checks,
                "textures",
                package.WindowsTexturesDirectory,
                package.WindowsRos2TexturesDirectory,
                false);
            return checks;
        }

        private static void AddPackageParityFileCheck(
            List<PackageParityCheck> checks,
            string category,
            string relativePath,
            string ros1Root,
            string ros2Root,
            bool critical)
        {
            AddPackageParityFileCheck(
                checks,
                category,
                relativePath,
                ros1Root,
                relativePath,
                ros2Root,
                relativePath,
                critical);
        }

        private static void AddPackageParityFileCheck(
            List<PackageParityCheck> checks,
            string category,
            string reportRelativePath,
            string ros1Root,
            string ros1RelativePath,
            string ros2Root,
            string ros2RelativePath,
            bool critical)
        {
            string ros1Path = Path.Combine(ros1Root, ros1RelativePath);
            string ros2Path = Path.Combine(ros2Root, ros2RelativePath);
            checks.Add(new PackageParityCheck(
                category,
                NormalizeReportPath(reportRelativePath),
                ros1Path,
                ros2Path,
                File.Exists(ros1Path),
                File.Exists(ros2Path),
                critical));
        }

        private static void AddPackageParityDirectoryChecks(
            List<PackageParityCheck> checks,
            string category,
            string ros1Root,
            string ros2Root,
            bool critical)
        {
            SortedSet<string> relativePaths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string relativePath in EnumerateRelativeFiles(ros1Root))
            {
                relativePaths.Add(relativePath);
            }
            foreach (string relativePath in EnumerateRelativeFiles(ros2Root))
            {
                relativePaths.Add(relativePath);
            }

            foreach (string relativePath in relativePaths)
            {
                string ros1Path = Path.Combine(ros1Root, relativePath);
                string ros2Path = Path.Combine(ros2Root, relativePath);
                checks.Add(new PackageParityCheck(
                    category,
                    NormalizeReportPath(relativePath),
                    ros1Path,
                    ros2Path,
                    File.Exists(ros1Path),
                    File.Exists(ros2Path),
                    critical));
            }
        }

        private static IEnumerable<string> EnumerateRelativeFiles(string root)
        {
            if (!Directory.Exists(root))
            {
                return Enumerable.Empty<string>();
            }

            return Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private static string NormalizeReportPath(string path)
        {
            return (path ?? "").Replace('\\', '/');
        }

        private static void AppendHealthSummarySection(
            StringBuilder builder,
            string overallStatus,
            UrdfInspection ros1Urdf,
            UrdfInspection ros2Urdf,
            IEnumerable<PackageCheck> packageChecks,
            IEnumerable<PackageParityCheck> packageParityChecks,
            IEnumerable<InertialValidationRecord> inertialRecords,
            IEnumerable<MeshExportRecord> meshRecords,
            IEnumerable<string> findings,
            bool exportMeshes,
            MeshExportFormat meshFormat)
        {
            List<PackageCheck> packageRows = packageChecks.ToList();
            List<PackageParityCheck> parityRows = packageParityChecks.ToList();
            List<InertialValidationRecord> inertialRows = inertialRecords.ToList();
            List<MeshExportRecord> meshRows = meshRecords.ToList();
            List<string> findingRows = findings.ToList();

            builder.AppendLine("## Health Summary");
            builder.AppendLine();
            builder.AppendLine("| Check | Status | Detail |");
            builder.AppendLine("| --- | --- | --- |");
            AppendHealthRow(
                builder,
                "Overall",
                overallStatus,
                "failures=" + CountFindings(findingRows, "FAIL:").ToString(CultureInfo.InvariantCulture) +
                ", warnings=" + CountFindings(findingRows, "WARN:").ToString(CultureInfo.InvariantCulture));
            AppendUrdfHealthRow(builder, ros1Urdf, exportMeshes);
            AppendUrdfHealthRow(builder, ros2Urdf, exportMeshes);
            AppendPackageCompletenessHealthRow(builder, packageRows);
            AppendPackageParityHealthRow(builder, parityRows);
            AppendInertialHealthRow(builder, inertialRows);
            AppendMeshHealthRow(builder, meshRows, exportMeshes);
            AppendCollisionStrategyHealthRow(builder, meshRows, exportMeshes);
            AppendStlReductionHealthRow(builder, meshRows, exportMeshes, meshFormat);
            builder.AppendLine();
        }

        private static int CountFindings(IEnumerable<string> findings, string prefix)
        {
            return findings.Count(f => f.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static void AppendUrdfHealthRow(
            StringBuilder builder,
            UrdfInspection inspection,
            bool exportMeshes)
        {
            int missingMeshReferences = inspection.MeshReferences.Count(r => !r.Exists);
            string status;
            if (!inspection.Exists || !inspection.XmlValid || !inspection.RootIsRobot)
            {
                status = "FAIL";
            }
            else if (inspection.DuplicateLinkNames.Count > 0 || inspection.DuplicateJointNames.Count > 0)
            {
                status = "FAIL";
            }
            else if (missingMeshReferences > 0)
            {
                status = exportMeshes ? "FAIL" : "WARN";
            }
            else
            {
                status = "PASS";
            }

            AppendHealthRow(
                builder,
                inspection.Label + " URDF",
                status,
                "links=" + inspection.LinkCount.ToString(CultureInfo.InvariantCulture) +
                ", joints=" + inspection.JointCount.ToString(CultureInfo.InvariantCulture) +
                ", mesh_refs=" + inspection.MeshReferences.Count.ToString(CultureInfo.InvariantCulture) +
                ", missing_mesh_refs=" + missingMeshReferences.ToString(CultureInfo.InvariantCulture) +
                ", duplicate_links=" + inspection.DuplicateLinkNames.Count.ToString(CultureInfo.InvariantCulture) +
                ", duplicate_joints=" + inspection.DuplicateJointNames.Count.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendPackageCompletenessHealthRow(
            StringBuilder builder,
            IEnumerable<PackageCheck> checks)
        {
            List<PackageCheck> rows = checks.ToList();
            int missingCritical = rows.Count(r => r.Critical && !r.Exists);
            int missingOptional = rows.Count(r => !r.Critical && !r.Exists);
            string status = missingCritical > 0 ? "FAIL" : missingOptional > 0 ? "WARN" : "PASS";
            AppendHealthRow(
                builder,
                "ROS package completeness",
                status,
                "critical_missing=" + missingCritical.ToString(CultureInfo.InvariantCulture) +
                ", optional_missing=" + missingOptional.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendPackageParityHealthRow(
            StringBuilder builder,
            IEnumerable<PackageParityCheck> checks)
        {
            List<PackageParityCheck> rows = checks.ToList();
            int criticalMismatches = rows.Count(r => r.Critical && !r.Matches);
            int optionalMismatches = rows.Count(r => !r.Critical && !r.Matches);
            string status = criticalMismatches > 0 ? "FAIL" : optionalMismatches > 0 ? "WARN" : "PASS";
            AppendHealthRow(
                builder,
                "ROS package parity",
                status,
                "critical_mismatches=" + criticalMismatches.ToString(CultureInfo.InvariantCulture) +
                ", optional_mismatches=" + optionalMismatches.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendInertialHealthRow(
            StringBuilder builder,
            IEnumerable<InertialValidationRecord> records)
        {
            List<InertialValidationRecord> rows = records.ToList();
            int failures = rows.Count(r => String.Equals(r.Row.Status, "FAIL", StringComparison.Ordinal));
            int warnings = rows.Count(r => r.Row.IsWarning);
            string status = failures > 0 ? "FAIL" : warnings > 0 || rows.Count == 0 ? "WARN" : "PASS";
            AppendHealthRow(
                builder,
                "Inertial validation",
                status,
                "rows=" + rows.Count.ToString(CultureInfo.InvariantCulture) +
                ", failures=" + failures.ToString(CultureInfo.InvariantCulture) +
                ", warnings=" + warnings.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendMeshHealthRow(
            StringBuilder builder,
            IEnumerable<MeshExportRecord> records,
            bool exportMeshes)
        {
            List<MeshExportRecord> rows = records.ToList();
            int missingVisual = rows.Count(r => !r.VisualExists);
            int missingCollision = rows.Count(r => !r.CollisionExists);
            string status;
            if (!exportMeshes)
            {
                status = "SKIP";
            }
            else if (missingVisual > 0 || missingCollision > 0)
            {
                status = "FAIL";
            }
            else if (rows.Count == 0)
            {
                status = "WARN";
            }
            else
            {
                status = "PASS";
            }

            AppendHealthRow(
                builder,
                "Mesh manifest paths",
                status,
                "rows=" + rows.Count.ToString(CultureInfo.InvariantCulture) +
                ", missing_visual=" + missingVisual.ToString(CultureInfo.InvariantCulture) +
                ", missing_collision=" + missingCollision.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendCollisionStrategyHealthRow(
            StringBuilder builder,
            IEnumerable<MeshExportRecord> records,
            bool exportMeshes)
        {
            List<MeshExportRecord> rows = records.ToList();
            int fallbacks = rows.Count(CollisionStrategyChanged);
            string status = !exportMeshes ? "SKIP" : fallbacks > 0 ? "WARN" : "PASS";
            AppendHealthRow(
                builder,
                "Collision strategy",
                status,
                "fallbacks=" + fallbacks.ToString(CultureInfo.InvariantCulture) +
                ", requested=" + FormatRequestedCollisionStrategies(rows) +
                ", effective=" + FormatEffectiveCollisionStrategies(rows) +
                ", urdf_refs=" + FormatCollisionUrdfReferenceKinds(rows));
        }

        private static void AppendStlReductionHealthRow(
            StringBuilder builder,
            IEnumerable<MeshExportRecord> records,
            bool exportMeshes,
            MeshExportFormat meshFormat)
        {
            List<MeshExportRecord> rows = records.ToList();
            int statsRows = rows.Count(r => r.StlStats != null && r.StlStats.ReductionRatio.HasValue);
            int highEstimateErrors = rows.Count(r =>
                r.StlStats != null &&
                r.StlStats.EstimateErrorPercent.HasValue &&
                Math.Abs(r.StlStats.EstimateErrorPercent.Value) > 50.0);
            string status;
            if (!exportMeshes || meshFormat != MeshExportFormat.STL)
            {
                status = "SKIP";
            }
            else if (highEstimateErrors > 0 || statsRows == 0)
            {
                status = "WARN";
            }
            else
            {
                status = "PASS";
            }

            AppendHealthRow(
                builder,
                "STL reduction",
                status,
                "stats_rows=" + statsRows.ToString(CultureInfo.InvariantCulture) +
                ", high_estimate_errors=" + highEstimateErrors.ToString(CultureInfo.InvariantCulture) +
                ", ratios=" + FormatStlReductionRatios(rows));
        }

        private static void AppendHealthRow(
            StringBuilder builder,
            string check,
            string status,
            string detail)
        {
            builder.AppendLine("| " + MarkdownCell(check) +
                " | " + MarkdownCell(status) +
                " | " + MarkdownCell(detail) + " |");
        }

        private static void AddFileCheck(List<PackageCheck> checks, string name, string path, bool critical)
        {
            checks.Add(new PackageCheck(name, path, File.Exists(path), critical));
        }

        private static void AddDirectoryCheck(List<PackageCheck> checks, string name, string path, bool critical)
        {
            checks.Add(new PackageCheck(name, path, Directory.Exists(path), critical));
        }

        private static void AddDirectoryFilesCheck(List<PackageCheck> checks, string name, string path, bool critical)
        {
            bool hasFiles = Directory.Exists(path) &&
                Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();
            checks.Add(new PackageCheck(name, path, hasFiles, critical));
        }

        private static UrdfInspection InspectUrdfFile(
            string label,
            string fileName,
            string packageName,
            string packageRootDirectory)
        {
            UrdfInspection inspection = new UrdfInspection(label, fileName);
            inspection.Exists = File.Exists(fileName);
            if (!inspection.Exists)
            {
                return inspection;
            }

            try
            {
                XDocument document = XDocument.Load(fileName);
                XElement root = document.Root;
                inspection.XmlValid = true;
                inspection.RootIsRobot = root != null && root.Name.LocalName == "robot";
                inspection.RobotName = root == null ? "" : (string)root.Attribute("name") ?? "";
                List<string> linkNames = document.Descendants()
                    .Where(e => e.Name.LocalName == "link")
                    .Select(e => (string)e.Attribute("name"))
                    .Where(v => !String.IsNullOrWhiteSpace(v))
                    .ToList();
                List<string> jointNames = document.Descendants()
                    .Where(e => e.Name.LocalName == "joint")
                    .Select(e => (string)e.Attribute("name"))
                    .Where(v => !String.IsNullOrWhiteSpace(v))
                    .ToList();
                inspection.LinkCount = document.Descendants().Count(e => e.Name.LocalName == "link");
                inspection.JointCount = document.Descendants().Count(e => e.Name.LocalName == "joint");
                inspection.DuplicateLinkNames.AddRange(FindDuplicateNames(linkNames));
                inspection.DuplicateJointNames.AddRange(FindDuplicateNames(jointNames));
                foreach (string meshUri in document.Descendants()
                    .Where(e => e.Name.LocalName == "mesh")
                    .Select(e => (string)e.Attribute("filename"))
                    .Where(v => !String.IsNullOrWhiteSpace(v)))
                {
                    string resolvedPath = ResolvePackageUri(meshUri, packageName, packageRootDirectory);
                    inspection.MeshReferences.Add(new MeshReference(
                        meshUri,
                        resolvedPath,
                        !String.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath)));
                }
            }
            catch (Exception e)
            {
                inspection.XmlValid = false;
                inspection.ParseError = e.Message;
            }

            return inspection;
        }

        private static IEnumerable<string> FindDuplicateNames(IEnumerable<string> names)
        {
            return names
                .GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }

        private static string ResolvePackageUri(string uri, string packageName, string packageRootDirectory)
        {
            string prefix = "package://" + packageName + "/";
            if (String.IsNullOrWhiteSpace(uri) ||
                !uri.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }

            string relativePath = uri.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(packageRootDirectory, relativePath);
        }

        private static void AppendExportParametersSection(
            StringBuilder builder,
            URDFPackage package,
            string ros1UrdfFileName,
            string ros2UrdfFileName,
            IEnumerable<InertialValidationRecord> inertialRecords,
            IEnumerable<MeshExportRecord> meshRecords,
            bool exportMeshes,
            MeshExportFormat meshFormat)
        {
            List<InertialValidationRecord> inertialRows = inertialRecords.ToList();
            List<MeshExportRecord> meshRows = meshRecords.ToList();

            builder.AppendLine("## Export Parameters");
            builder.AppendLine();
            builder.AppendLine("| Parameter | Value |");
            builder.AppendLine("| --- | --- |");
            AppendParameterRow(builder, "output_root", package.WindowsExportRootDirectory);
            AppendParameterRow(builder, "robot_name", package.RobotName);
            AppendParameterRow(builder, "ros1_package_name", package.PackageName);
            AppendParameterRow(builder, "ros2_package_name", package.Ros2PackageName);
            AppendParameterRow(builder, "ros1_package_directory", package.WindowsPackageDirectory);
            AppendParameterRow(builder, "ros2_package_directory", package.WindowsRos2PackageDirectory);
            AppendParameterRow(builder, "ros1_urdf", ros1UrdfFileName);
            AppendParameterRow(builder, "ros2_urdf", ros2UrdfFileName);
            AppendParameterRow(builder, "export_meshes", exportMeshes ? "true" : "false");
            AppendParameterRow(builder, "mesh_format", meshFormat.ToString());
            AppendParameterRow(builder, "inertial_validation_rows",
                inertialRows.Count.ToString(CultureInfo.InvariantCulture));
            AppendParameterRow(builder, "mesh_manifest_rows",
                meshRows.Count.ToString(CultureInfo.InvariantCulture));
            AppendParameterRow(builder, "requested_collision_strategies",
                FormatRequestedCollisionStrategies(meshRows));
            AppendParameterRow(builder, "effective_collision_strategies",
                FormatEffectiveCollisionStrategies(meshRows));
            AppendParameterRow(builder, "collision_urdf_refs",
                FormatCollisionUrdfReferenceKinds(meshRows));
            AppendParameterRow(builder, "stl_reduction_ratios",
                FormatStlReductionRatios(meshRows));
            AppendParameterRow(builder, "stl_quality_settings",
                FormatStlQualitySettings(meshRows));
            builder.AppendLine();
        }

        private static void AppendParameterRow(StringBuilder builder, string name, string value)
        {
            builder.AppendLine("| " + MarkdownCell(name) + " | " + MarkdownCell(value) + " |");
        }

        private static void AppendUrdfSection(StringBuilder builder, UrdfInspection inspection)
        {
            builder.AppendLine("## " + inspection.Label + " URDF");
            builder.AppendLine();
            builder.AppendLine("- File: " + inspection.FileName);
            builder.AppendLine("- Exists: " + FormatBool(inspection.Exists));
            builder.AppendLine("- XML parse: " + (inspection.XmlValid ? "OK" : "FAIL"));
            if (!inspection.XmlValid && !String.IsNullOrWhiteSpace(inspection.ParseError))
            {
                builder.AppendLine("- Parse error: " + inspection.ParseError);
            }
            builder.AppendLine("- Root is robot: " + FormatBool(inspection.RootIsRobot));
            builder.AppendLine("- Robot name: " + inspection.RobotName);
            builder.AppendLine("- Links: " + inspection.LinkCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Joints: " + inspection.JointCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Duplicate link names: " +
                FormatNameList(inspection.DuplicateLinkNames));
            builder.AppendLine("- Duplicate joint names: " +
                FormatNameList(inspection.DuplicateJointNames));
            builder.AppendLine("- Mesh references: " +
                inspection.MeshReferences.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Missing mesh references: " +
                inspection.MeshReferences.Count(r => !r.Exists).ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
        }

        private static void AppendPackageSection(StringBuilder builder, IEnumerable<PackageCheck> checks)
        {
            builder.AppendLine("## Package Files");
            builder.AppendLine();
            builder.AppendLine("| Item | Status | Required | Path |");
            builder.AppendLine("| --- | --- | --- | --- |");
            foreach (PackageCheck check in checks)
            {
                builder.AppendLine("| " + MarkdownCell(check.Name) +
                    " | " + (check.Exists ? "OK" : "MISSING") +
                    " | " + (check.Critical ? "yes" : "no") +
                    " | " + MarkdownCell(check.Path) + " |");
            }
            builder.AppendLine();
        }

        private static void AppendPackageParitySection(
            StringBuilder builder,
            IEnumerable<PackageParityCheck> checks)
        {
            List<PackageParityCheck> rows = checks.ToList();
            int mismatches = rows.Count(r => !r.Matches);
            builder.AppendLine("## ROS Package Parity");
            builder.AppendLine();
            builder.AppendLine("- Parity rows: " + rows.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Parity mismatches: " + mismatches.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("| Category | Relative path | ROS 1 | ROS 2 | Required |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (PackageParityCheck check in rows)
            {
                builder.AppendLine("| " + MarkdownCell(check.Category) +
                    " | " + MarkdownCell(check.RelativePath) +
                    " | " + (check.Ros1Exists ? "yes" : "no") +
                    " | " + (check.Ros2Exists ? "yes" : "no") +
                    " | " + (check.Critical ? "yes" : "no") + " |");
            }
            builder.AppendLine();
        }

        private static void AppendInertialSection(
            StringBuilder builder,
            IEnumerable<InertialValidationRecord> records)
        {
            List<InertialValidationRecord> rows = records.ToList();
            int failedRows = rows.Count(r => String.Equals(r.Row.Status, "FAIL", StringComparison.Ordinal));
            int warningRows = rows.Count(r => r.Row.IsWarning);
            int physicalFailures = rows.Count(r =>
                String.Equals(r.Row.CheckType, "physical", StringComparison.Ordinal) &&
                String.Equals(r.Row.Status, "FAIL", StringComparison.Ordinal));
            int displayWarnings = rows.Count(r =>
                String.Equals(r.Row.CheckType, "display", StringComparison.Ordinal) &&
                !String.Equals(r.Row.Status, "PASS", StringComparison.Ordinal));
            int magnitudeWarnings = rows.Count(r =>
                String.Equals(r.Row.CheckType, "magnitude", StringComparison.Ordinal) &&
                r.Row.IsWarning);
            int displayBlockedByInvalidPhysics = rows.Count(r =>
                String.Equals(r.Row.Quantity, "ellipsoid.display", StringComparison.Ordinal) &&
                r.Row.Message.IndexOf("physical inertia is invalid", StringComparison.OrdinalIgnoreCase) >= 0);
            int displayFailedAfterValidPhysics = rows.Count(r =>
                String.Equals(r.Row.Quantity, "ellipsoid.display", StringComparison.Ordinal) &&
                r.Row.Message.IndexOf("although physical checks passed", StringComparison.OrdinalIgnoreCase) >= 0);
            string failedLinks = String.Join(", ",
                rows.Where(r => String.Equals(r.Row.Status, "FAIL", StringComparison.Ordinal))
                    .Select(r => r.LinkName)
                    .Distinct()
                    .OrderBy(v => v));
            string warningLinks = String.Join(", ",
                rows.Where(r => r.Row.IsWarning)
                    .Select(r => r.LinkName)
                    .Distinct()
                    .OrderBy(v => v));
            string magnitudeWarningLinks = String.Join(", ",
                rows.Where(r =>
                        String.Equals(r.Row.CheckType, "magnitude", StringComparison.Ordinal) &&
                        r.Row.IsWarning)
                    .Select(r => r.LinkName)
                    .Distinct()
                    .OrderBy(v => v));
            string displayFailureLinks = String.Join(", ",
                rows.Where(r =>
                        String.Equals(r.Row.Quantity, "ellipsoid.display", StringComparison.Ordinal) &&
                        r.Row.Message.IndexOf("although physical checks passed", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(r => r.LinkName)
                    .Distinct()
                    .OrderBy(v => v));

            builder.AppendLine("## Inertial Validation");
            builder.AppendLine();
            builder.AppendLine("- Inertial validation rows: " + rows.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Failed rows: " + failedRows.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Warning rows: " + warningRows.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Physical inertia failures: " + physicalFailures.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Magnitude warnings: " + magnitudeWarnings.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Inertia display warnings: " + displayWarnings.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Display blocked by invalid physics: " +
                displayBlockedByInvalidPhysics.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Display failed after valid physics: " +
                displayFailedAfterValidPhysics.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Failed links: " + (String.IsNullOrWhiteSpace(failedLinks) ? "none" : failedLinks));
            builder.AppendLine("- Warning links: " + (String.IsNullOrWhiteSpace(warningLinks) ? "none" : warningLinks));
            builder.AppendLine("- Magnitude warning links: " +
                (String.IsNullOrWhiteSpace(magnitudeWarningLinks) ? "none" : magnitudeWarningLinks));
            builder.AppendLine("- Display failure links: " +
                (String.IsNullOrWhiteSpace(displayFailureLinks) ? "none" : displayFailureLinks));
            builder.AppendLine("- CSV: config/inertial_validation.csv");
            builder.AppendLine();

            builder.AppendLine("### Inertial Link Summary");
            builder.AppendLine();
            builder.AppendLine("| Link | Coordinate system | Status | Rows | Numeric | Physical failures | Magnitude warnings | Display warnings | Max abs error | Max relative error | Failed quantities | Warning quantities |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (IGrouping<string, InertialValidationRecord> linkGroup in
                rows.GroupBy(r => r.LinkName).OrderBy(group => group.Key))
            {
                List<InertialValidationRecord> linkRows = linkGroup.ToList();
                int linkFailures = linkRows.Count(r => String.Equals(r.Row.Status, "FAIL", StringComparison.Ordinal));
                int linkWarnings = linkRows.Count(r => r.Row.IsWarning);
                string linkStatus = linkFailures > 0 ? "FAIL" : linkWarnings > 0 ? "WARN" : "PASS";
                int numericRows = linkRows.Count(r => r.Row.HasNumericComparison);
                int linkPhysicalFailures = linkRows.Count(r =>
                    String.Equals(r.Row.CheckType, "physical", StringComparison.Ordinal) &&
                    String.Equals(r.Row.Status, "FAIL", StringComparison.Ordinal));
                int linkMagnitudeWarnings = linkRows.Count(r =>
                    String.Equals(r.Row.CheckType, "magnitude", StringComparison.Ordinal) &&
                    r.Row.IsWarning);
                int linkDisplayWarnings = linkRows.Count(r =>
                    String.Equals(r.Row.CheckType, "display", StringComparison.Ordinal) &&
                    !String.Equals(r.Row.Status, "PASS", StringComparison.Ordinal));
                double? maxAbsError = MaxNullableDouble(linkRows
                    .Where(r => r.Row.HasNumericComparison)
                    .Select(r => Math.Abs(r.Row.AbsoluteError)));
                double? maxRelativeError = MaxNullableDouble(linkRows
                    .Select(r => r.Row.RelativeErrorPercent));
                string failedQuantities = FormatQuantityList(linkRows
                    .Where(r => String.Equals(r.Row.Status, "FAIL", StringComparison.Ordinal))
                    .Select(r => r.Row.Quantity));
                string warningQuantities = FormatQuantityList(linkRows
                    .Where(r => r.Row.IsWarning)
                    .Select(r => r.Row.Quantity));
                string coordinateSystems = FormatQuantityList(linkRows.Select(r => r.CoordinateSystemName));

                builder.AppendLine("| " + MarkdownCell(linkGroup.Key) +
                    " | " + MarkdownCell(coordinateSystems) +
                    " | " + MarkdownCell(linkStatus) +
                    " | " + linkRows.Count.ToString(CultureInfo.InvariantCulture) +
                    " | " + numericRows.ToString(CultureInfo.InvariantCulture) +
                    " | " + linkPhysicalFailures.ToString(CultureInfo.InvariantCulture) +
                    " | " + linkMagnitudeWarnings.ToString(CultureInfo.InvariantCulture) +
                    " | " + linkDisplayWarnings.ToString(CultureInfo.InvariantCulture) +
                    " | " + FormatNullableNumber(maxAbsError) +
                    " | " + FormatNullablePercent(maxRelativeError) +
                    " | " + MarkdownCell(failedQuantities) +
                    " | " + MarkdownCell(warningQuantities) + " |");
            }
            if (rows.Count == 0)
            {
                builder.AppendLine("| none | none | WARN | 0 | 0 | 0 | 0 | 0 | none | none | none | none |");
            }
            builder.AppendLine();
        }

        private static void AppendMeshSection(StringBuilder builder, IEnumerable<MeshExportRecord> records)
        {
            List<MeshExportRecord> rows = records.ToList();
            int visualPresent = rows.Count(r => r.VisualExists);
            int collisionPresent = rows.Count(r => r.CollisionExists);
            long visualBytes = rows.Where(r => r.VisualBytes.HasValue).Sum(r => r.VisualBytes.Value);
            long collisionBytes = rows.Where(r => r.CollisionBytes.HasValue).Sum(r => r.CollisionBytes.Value);
            ulong visualTriangles = SumNullableUInt(rows.Select(r => r.VisualTriangles));
            ulong collisionTriangles = SumNullableUInt(rows.Select(r => r.CollisionTriangles));
            long estimatedVisualBytes = rows
                .Where(r => r.StlStats != null && r.StlStats.EstimatedBytes.HasValue)
                .Sum(r => r.StlStats.EstimatedBytes.Value);
            long baselineEstimatedVisualBytes = rows
                .Where(r => r.StlStats != null && r.StlStats.BaselineEstimatedBytes.HasValue)
                .Sum(r => r.StlStats.BaselineEstimatedBytes.Value);
            long estimatedVisualTriangles = rows
                .Where(r => r.StlStats != null && r.StlStats.EstimatedTriangles.HasValue)
                .Sum(r => (long)r.StlStats.EstimatedTriangles.Value);
            long baselineEstimatedVisualTriangles = rows
                .Where(r => r.StlStats != null && r.StlStats.BaselineEstimatedTriangles.HasValue)
                .Sum(r => (long)r.StlStats.BaselineEstimatedTriangles.Value);

            builder.AppendLine("## Mesh Manifest");
            builder.AppendLine();
            builder.AppendLine("- Mesh manifest rows: " + rows.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Visual meshes present: " + visualPresent + "/" + rows.Count);
            builder.AppendLine("- Collision meshes present: " + collisionPresent + "/" + rows.Count);
            builder.AppendLine("- Visual mesh bytes: " + visualBytes.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Collision mesh bytes: " + collisionBytes.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Visual STL triangles: " + visualTriangles.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Collision STL triangles: " + collisionTriangles.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Average collision mesh byte reduction vs visual: " +
                FormatNullablePercent(AverageNullableDouble(rows.Select(CalculateCollisionBytesReductionPercent))));
            builder.AppendLine("- Average collision mesh triangle reduction vs visual: " +
                FormatNullablePercent(AverageNullableDouble(rows.Select(CalculateCollisionTrianglesReductionPercent))));
            builder.AppendLine("- Baseline estimated visual STL bytes: " +
                baselineEstimatedVisualBytes.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Baseline estimated visual STL triangles: " +
                baselineEstimatedVisualTriangles.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Estimated visual STL bytes: " +
                estimatedVisualBytes.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Estimated visual STL triangles: " +
                estimatedVisualTriangles.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Requested STL reduction ratios: " +
                FormatStlReductionRatios(rows));
            builder.AppendLine("- STL quality settings: " +
                FormatStlQualitySettings(rows));
            builder.AppendLine("- Average estimated STL reduction: " +
                FormatNullablePercent(AverageNullableDouble(rows
                    .Where(r => r.StlStats != null)
                    .Select(r => r.StlStats.EstimatedReductionPercent))));
            builder.AppendLine("- Average actual STL reduction: " +
                FormatNullablePercent(AverageNullableDouble(rows
                    .Where(r => r.StlStats != null)
                    .Select(r => r.StlStats.ActualReductionPercent))));
            builder.AppendLine("- Requested collision strategies: " +
                FormatRequestedCollisionStrategies(rows));
            builder.AppendLine("- Effective collision strategies: " +
                FormatEffectiveCollisionStrategies(rows));
            builder.AppendLine("- Collision URDF refs: " +
                FormatCollisionUrdfReferenceKinds(rows));
            builder.AppendLine("- Collision strategy fallbacks: " +
                rows.Count(CollisionStrategyChanged).ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- CSV: config/mesh_manifest.csv");
            builder.AppendLine();
        }

        private static void AppendStlReductionSection(
            StringBuilder builder,
            IEnumerable<MeshExportRecord> records)
        {
            List<MeshExportRecord> rows = records.ToList();
            builder.AppendLine("## STL Reduction Details");
            builder.AppendLine();
            builder.AppendLine("| Link | Quality | Ratio | Custom | Deviation (m) | Angle tolerance (rad) | Baseline est. bytes | Baseline est. triangles | Estimated bytes | Estimated triangles | Actual visual bytes | Actual visual triangles | Estimate error | Estimated reduction | Actual reduction |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (MeshExportRecord row in rows)
            {
                StlExportStats stats = row.StlStats ?? StlExportStats.NotExported();
                builder.AppendLine("| " + MarkdownCell(row.LinkName) +
                    " | " + MarkdownCell(stats.QualityLabel) +
                    " | " + FormatNullableDouble(stats.ReductionRatio) +
                    " | " + FormatNullableBool(stats.CustomSettings) +
                    " | " + FormatNullableDouble(stats.Deviation) +
                    " | " + FormatNullableDouble(stats.AngleTolerance) +
                    " | " + FormatNullableLong(stats.BaselineEstimatedBytes) +
                    " | " + FormatNullableInt(stats.BaselineEstimatedTriangles) +
                    " | " + FormatNullableLong(stats.EstimatedBytes) +
                    " | " + FormatNullableInt(stats.EstimatedTriangles) +
                    " | " + FormatNullableLong(row.VisualBytes) +
                    " | " + FormatNullableUInt(row.VisualTriangles) +
                    " | " + FormatNullablePercent(stats.EstimateErrorPercent) +
                    " | " + FormatNullablePercent(stats.EstimatedReductionPercent) +
                    " | " + FormatNullablePercent(stats.ActualReductionPercent) + " |");
            }
            if (rows.Count == 0)
            {
                builder.AppendLine("| none |  |  |  |  |  |  |  |  |  |  |  | none | none | none |");
            }
            builder.AppendLine();
        }

        private static void AppendCollisionStrategySection(
            StringBuilder builder,
            IEnumerable<MeshExportRecord> records)
        {
            List<MeshExportRecord> rows = records.ToList();
            builder.AppendLine("## Collision Strategies");
            builder.AppendLine();
            builder.AppendLine("| Link | Requested | Effective | Geometry | URDF collision ref | Notes | Collision artifact exists | Collision artifact bytes | Collision artifact triangles | Byte reduction vs visual | Triangle reduction vs visual | Collision artifact URI |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (MeshExportRecord row in rows)
            {
                builder.AppendLine("| " + MarkdownCell(row.LinkName) +
                    " | " + MarkdownCell(row.CollisionStrategy) +
                    " | " + MarkdownCell(row.CollisionEffectiveStrategy) +
                    " | " + MarkdownCell(row.CollisionGeometryType) +
                    " | " + MarkdownCell(row.CollisionUrdfReference) +
                    " | " + MarkdownCell(row.CollisionNotes) +
                    " | " + FormatBool(row.CollisionExists) +
                    " | " + FormatNullableLong(row.CollisionBytes) +
                    " | " + FormatNullableUInt(row.CollisionTriangles) +
                    " | " + FormatNullablePercent(CalculateCollisionBytesReductionPercent(row)) +
                    " | " + FormatNullablePercent(CalculateCollisionTrianglesReductionPercent(row)) +
                    " | " + MarkdownCell(row.CollisionUri) + " |");
            }
            if (rows.Count == 0)
            {
                builder.AppendLine("| none | none | none | none | none | none | false |  |  | none | none |  |");
            }
            builder.AppendLine();
        }

        private static void AppendFindingsSection(StringBuilder builder, IEnumerable<string> findings)
        {
            List<string> list = findings.ToList();
            builder.AppendLine("## Findings");
            builder.AppendLine();
            if (list.Count == 0)
            {
                builder.AppendLine("- None");
            }
            else
            {
                foreach (string finding in list)
                {
                    builder.AppendLine("- " + finding);
                }
            }
            builder.AppendLine();
        }

        private static string BuildExportParameterSummary(
            IEnumerable<InertialValidationRecord> inertialRecords,
            IEnumerable<MeshExportRecord> meshRecords,
            bool exportMeshes,
            MeshExportFormat meshFormat)
        {
            List<InertialValidationRecord> inertialRows = inertialRecords.ToList();
            List<MeshExportRecord> meshRows = meshRecords.ToList();
            return "export_meshes=" + (exportMeshes ? "true" : "false") +
                ", mesh_format=" + meshFormat +
                ", inertial_validation_rows=" + inertialRows.Count.ToString(CultureInfo.InvariantCulture) +
                ", mesh_manifest_rows=" + meshRows.Count.ToString(CultureInfo.InvariantCulture) +
                ", requested_collision_strategies=" + FormatRequestedCollisionStrategies(meshRows) +
                ", effective_collision_strategies=" + FormatEffectiveCollisionStrategies(meshRows) +
                ", collision_urdf_refs=" + FormatCollisionUrdfReferenceKinds(meshRows) +
                ", stl_reduction_ratios=" + FormatStlReductionRatios(meshRows) +
                ", stl_quality_settings=" + FormatStlQualitySettings(meshRows);
        }

        private static string FormatRequestedCollisionStrategies(IEnumerable<MeshExportRecord> records)
        {
            return FormatGroupCounts(records.Select(r => r.CollisionStrategy));
        }

        private static string FormatEffectiveCollisionStrategies(IEnumerable<MeshExportRecord> records)
        {
            return FormatGroupCounts(records.Select(r => r.CollisionEffectiveStrategy));
        }

        private static bool CollisionStrategyChanged(MeshExportRecord record)
        {
            if (record == null)
            {
                return false;
            }
            if (String.Equals(record.CollisionStrategy, record.CollisionEffectiveStrategy, StringComparison.Ordinal))
            {
                return false;
            }

            return !IsLegacyPrimitiveAlias(record.CollisionStrategy, record.CollisionEffectiveStrategy);
        }

        private static bool IsLegacyPrimitiveAlias(string requested, string effective)
        {
            return String.Equals(requested, "Primitive", StringComparison.Ordinal) &&
                String.Equals(effective, "BoxPrimitive", StringComparison.Ordinal);
        }

        private static string FormatCollisionUrdfReferenceKinds(IEnumerable<MeshExportRecord> records)
        {
            return FormatGroupCounts(records.Select(r => ClassifyCollisionUrdfReference(r.CollisionUrdfReference)));
        }

        private static string ClassifyCollisionUrdfReference(string reference)
        {
            if (String.IsNullOrWhiteSpace(reference))
            {
                return "";
            }

            return reference.StartsWith("native:", StringComparison.Ordinal) ? reference : "mesh";
        }

        private static string FormatStlReductionRatios(IEnumerable<MeshExportRecord> records)
        {
            return FormatGroupCounts(records
                .Where(r => r.StlStats != null && r.StlStats.ReductionRatio.HasValue)
                .Select(r => FormatNullableDouble(r.StlStats.ReductionRatio)));
        }

        private static string FormatStlQualitySettings(IEnumerable<MeshExportRecord> records)
        {
            return FormatGroupCounts(records
                .Where(r => r.StlStats != null)
                .Select(r => r.StlStats.QualityLabel));
        }

        private static ulong SumNullableUInt(IEnumerable<uint?> values)
        {
            ulong sum = 0;
            foreach (uint? value in values)
            {
                if (value.HasValue)
                {
                    sum += value.Value;
                }
            }
            return sum;
        }

        private static string FormatGroupCounts(IEnumerable<string> values)
        {
            List<string> counts = values
                .Where(v => !String.IsNullOrWhiteSpace(v))
                .GroupBy(v => v)
                .OrderBy(group => group.Key)
                .Select(group => group.Key + "=" + group.Count().ToString(CultureInfo.InvariantCulture))
                .ToList();
            return counts.Count == 0 ? "none" : String.Join(", ", counts.ToArray());
        }

        private static double? AverageNullableDouble(IEnumerable<double?> values)
        {
            List<double> validValues = values.Where(v => v.HasValue).Select(v => v.Value).ToList();
            if (validValues.Count == 0)
            {
                return null;
            }

            return validValues.Average();
        }

        private static double? MaxNullableDouble(IEnumerable<double?> values)
        {
            List<double> validValues = values.Where(v => v.HasValue).Select(v => v.Value).ToList();
            if (validValues.Count == 0)
            {
                return null;
            }

            return validValues.Max();
        }

        private static double? MaxNullableDouble(IEnumerable<double> values)
        {
            List<double> validValues = values.ToList();
            if (validValues.Count == 0)
            {
                return null;
            }

            return validValues.Max();
        }

        private static string FormatNullableNumber(double? value)
        {
            return value.HasValue ? FormatNullableDouble(value) : "none";
        }

        private static string FormatQuantityList(IEnumerable<string> values)
        {
            List<string> distinctValues = values
                .Where(v => !String.IsNullOrWhiteSpace(v))
                .Distinct()
                .OrderBy(v => v)
                .ToList();
            return distinctValues.Count == 0 ? "none" : String.Join(", ", distinctValues.ToArray());
        }

        private static string FormatNameList(IEnumerable<string> names)
        {
            List<string> values = names
                .Where(v => !String.IsNullOrWhiteSpace(v))
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
            return values.Count == 0 ? "none" : String.Join(", ", values.ToArray());
        }

        private static string FormatNullablePercent(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%"
                : "none";
        }

        private static string FormatBool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string MarkdownCell(string value)
        {
            if (String.IsNullOrEmpty(value))
            {
                return "";
            }

            return value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        private class UrdfInspection
        {
            public UrdfInspection(string label, string fileName)
            {
                Label = label;
                FileName = fileName;
                MeshReferences = new List<MeshReference>();
                DuplicateLinkNames = new List<string>();
                DuplicateJointNames = new List<string>();
                RobotName = "";
                ParseError = "";
            }

            public string Label { get; private set; }

            public string FileName { get; private set; }

            public bool Exists { get; set; }

            public bool XmlValid { get; set; }

            public bool RootIsRobot { get; set; }

            public string RobotName { get; set; }

            public int LinkCount { get; set; }

            public int JointCount { get; set; }

            public string ParseError { get; set; }

            public List<MeshReference> MeshReferences { get; private set; }

            public List<string> DuplicateLinkNames { get; private set; }

            public List<string> DuplicateJointNames { get; private set; }
        }

        private class MeshReference
        {
            public MeshReference(string uri, string resolvedPath, bool exists)
            {
                Uri = uri;
                ResolvedPath = resolvedPath;
                Exists = exists;
            }

            public string Uri { get; private set; }

            public string ResolvedPath { get; private set; }

            public bool Exists { get; private set; }
        }

        private class PackageCheck
        {
            public PackageCheck(string name, string path, bool exists, bool critical)
            {
                Name = name;
                Path = path;
                Exists = exists;
                Critical = critical;
            }

            public string Name { get; private set; }

            public string Path { get; private set; }

            public bool Exists { get; private set; }

            public bool Critical { get; private set; }
        }

        private class PackageParityCheck
        {
            public PackageParityCheck(
                string category,
                string relativePath,
                string ros1Path,
                string ros2Path,
                bool ros1Exists,
                bool ros2Exists,
                bool critical)
            {
                Category = category;
                RelativePath = relativePath;
                Ros1Path = ros1Path;
                Ros2Path = ros2Path;
                Ros1Exists = ros1Exists;
                Ros2Exists = ros2Exists;
                Critical = critical;
            }

            public string Category { get; private set; }

            public string RelativePath { get; private set; }

            public string Ros1Path { get; private set; }

            public string Ros2Path { get; private set; }

            public bool Ros1Exists { get; private set; }

            public bool Ros2Exists { get; private set; }

            public bool Critical { get; private set; }

            public bool Matches
            {
                get { return Ros1Exists == Ros2Exists; }
            }
        }
    }
}
