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
            List<string> findings = BuildExportFindings(ros1Urdf, ros2Urdf, packageChecks, inertialRows, meshRows, exportMeshes);

            bool hasFailure = findings.Any(f => f.StartsWith("FAIL:", StringComparison.Ordinal));
            bool hasWarning = findings.Any(f => f.StartsWith("WARN:", StringComparison.Ordinal));
            string status = hasFailure ? "FAIL" : hasWarning ? "WARN" : "PASS";

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# SW2URDF Export Report");
            builder.AppendLine();
            builder.AppendLine("Status: " + status);
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            builder.AppendLine("Commit version: " + Versioning.Version.GetCommitVersion());
            builder.AppendLine("Build version: " + Versioning.Version.GetBuildVersion());
            builder.AppendLine("Robot name: " + package.RobotName);
            builder.AppendLine("ROS package: " + package.PackageName);
            builder.AppendLine("Export meshes: " + (exportMeshes ? "true" : "false"));
            builder.AppendLine("Mesh format: " + meshFormat);
            builder.AppendLine("Elapsed: " + Utilities.OperationHeartbeat.FormatElapsed(elapsed));
            builder.AppendLine();

            AppendUrdfSection(builder, ros1Urdf);
            AppendUrdfSection(builder, ros2Urdf);
            AppendPackageSection(builder, packageChecks);
            AppendInertialSection(builder, inertialRows);
            AppendMeshSection(builder, meshRows);
            AppendFindingsSection(builder, findings);

            return builder.ToString();
        }

        private static List<string> BuildExportFindings(
            UrdfInspection ros1Urdf,
            UrdfInspection ros2Urdf,
            IEnumerable<PackageCheck> packageChecks,
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

            List<InertialValidationRecord> inertialList = inertialRows.ToList();
            if (inertialList.Count == 0)
            {
                findings.Add("WARN: No inertial validation rows were produced.");
            }
            foreach (IGrouping<string, InertialValidationRecord> linkGroup in
                inertialList.Where(r => !r.Row.Passed).GroupBy(r => r.LinkName))
            {
                findings.Add("FAIL: Inertial validation failed for link " + linkGroup.Key +
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
            AddDirectoryCheck(checks, "ROS 1 meshes directory", package.WindowsMeshesDirectory, exportMeshes);
            AddDirectoryCheck(checks, "ROS 1 visual meshes directory", Path.Combine(package.WindowsMeshesDirectory, "visual"), exportMeshes);
            AddDirectoryCheck(checks, "ROS 1 collision meshes directory", Path.Combine(package.WindowsMeshesDirectory, "collision"), exportMeshes);
            AddFileCheck(checks, "ROS 1 inertial validation CSV",
                Path.Combine(package.WindowsConfigDirectory, "inertial_validation.csv"), false);
            AddFileCheck(checks, "ROS 1 mesh manifest CSV",
                Path.Combine(package.WindowsConfigDirectory, "mesh_manifest.csv"), false);

            AddDirectoryCheck(checks, "ROS 2 package directory", package.WindowsRos2PackageDirectory, true);
            AddFileCheck(checks, "ROS 2 package.xml", Path.Combine(package.WindowsRos2PackageDirectory, "package.xml"), true);
            AddFileCheck(checks, "ROS 2 setup.py", Path.Combine(package.WindowsRos2PackageDirectory, "setup.py"), true);
            AddFileCheck(checks, "ROS 2 resource marker",
                Path.Combine(package.WindowsRos2ResourceDirectory, package.Ros2PackageName), true);
            AddFileCheck(checks, "ROS 2 URDF", ros2UrdfFileName, true);
            AddDirectoryCheck(checks, "ROS 2 meshes directory", package.WindowsRos2MeshesDirectory, exportMeshes);
            AddDirectoryCheck(checks, "ROS 2 visual meshes directory", Path.Combine(package.WindowsRos2MeshesDirectory, "visual"), exportMeshes);
            AddDirectoryCheck(checks, "ROS 2 collision meshes directory", Path.Combine(package.WindowsRos2MeshesDirectory, "collision"), exportMeshes);
            AddFileCheck(checks, "ROS 2 inertial validation CSV",
                Path.Combine(package.WindowsRos2ConfigDirectory, "inertial_validation.csv"), false);
            AddFileCheck(checks, "ROS 2 mesh manifest CSV",
                Path.Combine(package.WindowsRos2ConfigDirectory, "mesh_manifest.csv"), false);
            return checks;
        }

        private static void AddFileCheck(List<PackageCheck> checks, string name, string path, bool critical)
        {
            checks.Add(new PackageCheck(name, path, File.Exists(path), critical));
        }

        private static void AddDirectoryCheck(List<PackageCheck> checks, string name, string path, bool critical)
        {
            checks.Add(new PackageCheck(name, path, Directory.Exists(path), critical));
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
                inspection.LinkCount = document.Descendants().Count(e => e.Name.LocalName == "link");
                inspection.JointCount = document.Descendants().Count(e => e.Name.LocalName == "joint");
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

        private static void AppendInertialSection(
            StringBuilder builder,
            IEnumerable<InertialValidationRecord> records)
        {
            List<InertialValidationRecord> rows = records.ToList();
            int failedRows = rows.Count(r => !r.Row.Passed);
            string failedLinks = String.Join(", ",
                rows.Where(r => !r.Row.Passed)
                    .Select(r => r.LinkName)
                    .Distinct()
                    .OrderBy(v => v));

            builder.AppendLine("## Inertial Validation");
            builder.AppendLine();
            builder.AppendLine("- Inertial validation rows: " + rows.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Failed rows: " + failedRows.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Failed links: " + (String.IsNullOrWhiteSpace(failedLinks) ? "none" : failedLinks));
            builder.AppendLine("- CSV: config/inertial_validation.csv");
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

            builder.AppendLine("## Mesh Manifest");
            builder.AppendLine();
            builder.AppendLine("- Mesh manifest rows: " + rows.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Visual meshes present: " + visualPresent + "/" + rows.Count);
            builder.AppendLine("- Collision meshes present: " + collisionPresent + "/" + rows.Count);
            builder.AppendLine("- Visual mesh bytes: " + visualBytes.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Collision mesh bytes: " + collisionBytes.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Visual STL triangles: " + visualTriangles.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Collision STL triangles: " + collisionTriangles.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- CSV: config/mesh_manifest.csv");
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
    }
}
