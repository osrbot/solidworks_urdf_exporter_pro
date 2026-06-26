/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.ROS;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport.CSV;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Xml.Serialization;

namespace SW2URDF.URDFExport
{
    // This class contains a long list of methods that are used throughout the export process.
    // Methods for building links and joints are contained in here.
    // Many of the methods are overloaded, but seek to reduce repeated code as much as possible
    // (i.e. the overloaded methods call eachother).
    // These methods are used by the PartExportForm, the AssemblyExportForm and the PropertyManager Page
    public partial class ExportHelper
    {
        #region class variables

        private static readonly log4net.ILog logger = Logger.GetLogger();

        [XmlIgnore]
        public ISldWorks iSwApp = null;

        [XmlIgnore]
        private bool mBinary;

        private bool mshowInfo;
        private bool mSTLPreview;
        private bool mTranslateToPositive;
        private bool mSaveComponentsIntoOneFile;
        private int mSTLUnits;
        private int mSTLQuality;
        private double mSTLDeviation;
        private double mSTLAngleTolerance;
        private double mHideTransitionSpeed;

        private UserProgressBar progressBar;
        private Stopwatch exportStopwatch;
        private int exportStageNumber;

        [XmlIgnore]
        public ModelDoc2 ActiveSWModel;

        [XmlIgnore]
        public MathUtility swMath;

        [XmlIgnore]
        public Object SWMathPID
        { get; set; }

        public Robot URDFRobot
        { get; set; }

        public string PackageName
        { get; set; }

        public string RosPackageName
        { get; set; }

        public string SavePath
        { get; set; }

        public readonly List<Link> Links;

        private readonly List<string> ReferenceCoordinateSystemNames;
        private readonly List<string> ReferenceAxesNames;

        private const double MinimumCustomStlDeviation = 0.001;
        private const double MaximumCustomStlDeviation = 0.02;
        private const double MinimumCustomStlAngleTolerance = Math.PI / 6.0;
        private const double MaximumCustomStlAngleTolerance = 2.0 * Math.PI / 3.0;

        private bool ComputeInertialValues;
        private bool ComputeVisualCollision;
        private bool ComputeJointKinematics;
        private bool ComputeJointLimits;

        #endregion class variables

        // Constructor for SW2URDF Exporter class
        public ExportHelper(SldWorks iSldWorksApp)
        {
            ConstructExporter(iSldWorksApp);
            iSwApp.GetUserProgressBar(out progressBar);

            SavePath = System.Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
            PackageName = ActiveSWModel.GetTitle();
            RosPackageName = URDFPackage.SanitizePackageName(PackageName);

            ReferenceCoordinateSystemNames = FindRefGeoNames("CoordSys");
            ReferenceAxesNames = FindRefGeoNames("RefAxis");

            ComputeInertialValues = true;
            ComputeVisualCollision = true;
            ComputeJointKinematics = true;
            ComputeJointLimits = true;
        }

        public void SetComputeInertial(bool computeInertial)
        {
            ComputeInertialValues = computeInertial;
        }

        public void SetComputeVisualCollision(bool computeVisual)
        {
            ComputeVisualCollision = computeVisual;
        }

        public void SetComputeJointKinematics(bool computeKinematics)
        {
            ComputeJointKinematics = computeKinematics;
        }

        public void SetComputeJointLimits(bool computeJointLimits)
        {
            ComputeJointLimits = computeJointLimits;
        }

        private void ConstructExporter(SldWorks iSldWorksApp)
        {
            iSwApp = iSldWorksApp;
            ActiveSWModel = (ModelDoc2)iSwApp.ActiveDoc;
            swMath = iSwApp.GetMathUtility();
        }

        #region Export Methods

        // Beginning method for exporting the full package
        public void ExportRobot(bool exportSTL = true, MeshExportFormat meshFormat = MeshExportFormat.STL)
        {
            //Setting up the progress bar
            exportStopwatch = Stopwatch.StartNew();
            exportStageNumber = 0;
            logger.Info("Beginning the export process");
            logger.Info("Export metadata: commit version " + Versioning.Version.GetCommitVersion() +
                ", build version " + Versioning.Version.GetBuildVersion() +
                ", started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") +
                ", robot " + PackageName +
                ", ROS package " + RosPackageName +
                ", save path " + SavePath +
                ", export meshes " + exportSTL +
                ", mesh format " + meshFormat);
            int progressBarBound = CommonSwOperations.GetCount(URDFRobot.BaseLink);
            iSwApp.GetUserProgressBar(out progressBar);
            progressBar.Start(0, progressBarBound,
                ChineseUiText.Translate("Creating package directories", "\u6b63\u5728\u521b\u5efa\u529f\u80fd\u5305\u76ee\u5f55"));

            //Creating package directories
            PackageName = URDFPackage.SanitizePackageName(PackageName);
            RosPackageName = URDFPackage.SanitizePackageName(RosPackageName);
            logger.Info("Creating package directories with ROS package name " + RosPackageName +
                ", robot name " + PackageName + " and save path " + SavePath);
            URDFPackage package = new URDFPackage(PackageName, RosPackageName, SavePath);
            package.CreateDirectories();
            URDFRobot.Name = PackageName;
            string windowsURDFFileName = package.WindowsRobotsDirectory + URDFRobot.Name + ".urdf";
            string windowsCSVFileName = package.WindowsRobotsDirectory + URDFRobot.Name + ".csv";
            string windowsInertialValidationCsvFileName =
                Path.Combine(package.WindowsConfigDirectory, "inertial_validation.csv");
            string windowsMeshManifestCsvFileName =
                Path.Combine(package.WindowsConfigDirectory, "mesh_manifest.csv");
            string windowsPackageXMLFileName = package.WindowsPackageDirectory + "package.xml";

            //Create CMakeLists
            UpdateProgressTitle("Creating ROS package metadata", "\u6b63\u5728\u521b\u5efa ROS \u529f\u80fd\u5305\u5143\u6570\u636e");
            logger.Info("Creating CMakeLists.txt at " + package.WindowsCMakeLists);
            package.CreateCMakeLists();

            //Create Config joint names, not sure how this is used...
            logger.Info("Creating joint names config at " + package.WindowsConfigYAML);
            package.CreateConfigYAML(URDFRobot.GetJointNames(false));

            //Creating package.xml file
            logger.Info("Creating package.xml at " + windowsPackageXMLFileName);
            PackageXMLWriter packageXMLWriter = new PackageXMLWriter(windowsPackageXMLFileName);
            PackageXML packageXML = new PackageXML(RosPackageName);
            packageXML.WriteElement(packageXMLWriter);

            //Creating RVIZ launch file
            Rviz rviz = new Rviz(RosPackageName, URDFRobot.Name + ".urdf");
            logger.Info("Creating RVIZ launch file in " + package.WindowsLaunchDirectory);
            rviz.WriteFiles(package.WindowsLaunchDirectory);

            //Creating Gazebo launch file
            Gazebo gazebo = new Gazebo(URDFRobot.Name, RosPackageName, URDFRobot.Name + ".urdf");
            logger.Info("Creating Gazebo launch file in " + package.WindowsLaunchDirectory);

            gazebo.WriteFile(package.WindowsLaunchDirectory);

            //Customizing STL preferences to how I want them
            logger.Info("Saving existing STL preferences");
            SaveUserPreferences();

            logger.Info("Modifying STL preferences");
            SetSTLExportPreferences();

            //Saving part as STL mesh
            AssemblyDoc assyDoc = (AssemblyDoc)ActiveSWModel;
            List<string> hiddenComponents = CommonSwOperations.FindHiddenComponents(assyDoc.GetComponents(false));
            logger.Info("Found " + hiddenComponents.Count + " hidden components " + String.Join(", ", hiddenComponents));
            logger.Info("Hiding all components");
            UpdateProgressTitle("Preparing SolidWorks components", "\u6b63\u5728\u51c6\u5907 SolidWorks \u7ec4\u4ef6");
            ActiveSWModel.Extension.SelectAll();
            ActiveSWModel.HideComponent2();

            bool success = false;
            List<MeshExportRecord> meshRecords = new List<MeshExportRecord>();
            try
            {
                logger.Info("Beginning individual files export");
                ExportFiles(URDFRobot.BaseLink, package, 0, exportSTL, meshFormat, meshRecords);
                success = true;
            }
            catch (Exception e)
            {
                logger.Error("An exception was thrown attempting to export the URDF", e);
            }
            finally
            {
                logger.Info("Showing all components except previously hidden components");
                UpdateProgressTitle("Restoring SolidWorks component visibility",
                    "\u6b63\u5728\u6062\u590d SolidWorks \u7ec4\u4ef6\u53ef\u89c1\u6027");
                CommonSwOperations.ShowAllComponents(ActiveSWModel, hiddenComponents);

                logger.Info("Resetting STL preferences");
                ResetUserPreferences();
            }

            if (!success)
            {
                progressBar.End();
                exportStopwatch.Stop();
                logger.Error("Export process failed after " +
                    OperationHeartbeat.FormatElapsed(exportStopwatch.Elapsed));
                MessageBox.Show("Exporting the URDF failed unexpectedly. Email your maintainer " +
                    "with the log file found at " + Logger.GetFileName());
                return;
            }

            List<InertialValidationRecord> inertialRecords =
                LogInertialValidation(URDFRobot.BaseLink, windowsInertialValidationCsvFileName);
            WriteMeshManifestCsv(windowsMeshManifestCsvFileName, meshRecords);
            logger.Info("Wrote mesh manifest CSV with " + meshRecords.Count + " rows to " +
                windowsMeshManifestCsvFileName);

            UpdateProgressTitle("Writing URDF file", "\u6b63\u5728\u5199\u5165 URDF \u6587\u4ef6");
            logger.Info("Writing URDF file to " + windowsURDFFileName);
            URDFWriter uWriter = new URDFWriter(windowsURDFFileName);
            URDFRobot.WriteURDF(uWriter.writer);

            UpdateProgressTitle("Writing CSV file", "\u6b63\u5728\u5199\u5165 CSV \u6587\u4ef6");
            ImportExport.WriteRobotToCSV(URDFRobot, windowsCSVFileName);

            UpdateProgressTitle("Creating ROS 2 package", "\u6b63\u5728\u521b\u5efa ROS 2 \u529f\u80fd\u5305");
            logger.Info("Creating ROS 2 package at " + package.WindowsRos2PackageDirectory);
            package.CreateRos2Package(windowsURDFFileName);

            UpdateProgressTitle("Writing export report", "\u6b63\u5728\u5199\u5165\u5bfc\u51fa\u4f53\u68c0\u62a5\u544a");
            WriteExportReport(
                package,
                windowsURDFFileName,
                inertialRecords,
                meshRecords,
                exportSTL,
                meshFormat,
                exportStopwatch.Elapsed);

            UpdateProgressTitle("Copying export log", "\u6b63\u5728\u590d\u5236\u5bfc\u51fa\u65e5\u5fd7");
            logger.Info("Copying log file");
            CopyLogFile(package);

            logger.Info("Resetting STL preferences");
            ResetUserPreferences();
            progressBar.End();
            exportStopwatch.Stop();
            logger.Info("Export process completed successfully for ROS package " + RosPackageName +
                " and robot " + PackageName + "; elapsed " +
                OperationHeartbeat.FormatElapsed(exportStopwatch.Elapsed));
        }

        public List<string> GetJointNames()
        {
            List<string> jointNames = new List<string>();

            Queue<Link> queue = new Queue<Link>();
            queue.Enqueue(URDFRobot.BaseLink);
            while (queue.Count > 0)
            {
                Link current = queue.Dequeue();
                if (current.Parent != null)
                {
                    jointNames.Add(current.Joint.Name);
                }

                foreach (Link child in current.Children)
                {
                    queue.Enqueue(child);
                }
            }

            return jointNames;
        }

        //Recursive method for exporting each link (and writing it to the URDF)
        private void ExportFiles(
            Link link,
            URDFPackage package,
            int count,
            bool exportSTL = true,
            MeshExportFormat meshFormat = MeshExportFormat.STL,
            List<MeshExportRecord> meshRecords = null)
        {
            progressBar.UpdateProgress(count);
            progressBar.UpdateTitle(ChineseUiText.Translate(
                "Exporting mesh: " + link.Name,
                "\u6b63\u5728\u5bfc\u51fa\u7f51\u683c: " + link.Name));
            logger.Info("Exporting link: " + link.Name);
            // Iterate through each child and export its files
            logger.Info("Link " + link.Name + " has " + link.Children.Count + " children");
            foreach (Link child in link.Children)
            {
                count += 1;
                if (!child.isFixedFrame)
                {
                    ExportFiles(child, package, count, exportSTL, meshFormat, meshRecords);
                }
            }

            // Copy the texture file (if it was specified) to the textures directory
            if (!link.isFixedFrame && !String.IsNullOrWhiteSpace(link.Visual.Material.Texture.wFilename))
            {
                if (File.Exists(link.Visual.Material.Texture.wFilename))
                {
                    link.Visual.Material.Texture.Filename =

                        package.TexturesDirectory + Path.GetFileName(link.Visual.Material.Texture.wFilename);
                    string textureSavePath =
                        package.WindowsTexturesDirectory + Path.GetFileName(link.Visual.Material.Texture.wFilename);
                    File.Copy(link.Visual.Material.Texture.wFilename, textureSavePath, true);
                }
            }

            MeshFileNames meshFiles = CreateLinkMeshFileNames(package, link, meshFormat);
            CollisionMeshExportResult collisionExport =
                CollisionMeshExportResult.NotExported(link.CollisionMeshStrategy);

            // Export STL
            if (exportSTL)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(meshFiles.WindowsVisualMeshFilename));
                Directory.CreateDirectory(Path.GetDirectoryName(meshFiles.WindowsCollisionMeshFilename));
                switch (meshFormat)
                {
                    case MeshExportFormat.STL:
                        SaveSTL(link, meshFiles.WindowsVisualMeshFilename);
                        break;

                    case MeshExportFormat.THREEDXML:
                        Save3dxml(link, meshFiles.WindowsVisualMeshFilename);
                        break;

                    default:
                        SaveSTL(link, meshFiles.WindowsVisualMeshFilename);
                        break;
                }
                collisionExport = ExportCollisionMesh(link, meshFiles, meshFormat);
            }
            link.Visual.Geometry.Mesh.Filename = meshFiles.VisualMeshFilename;
            link.Collision.Geometry.Mesh.Filename = meshFiles.CollisionMeshFilename;
            if (meshRecords != null)
            {
                meshRecords.Add(CreateMeshExportRecord(link, meshFiles, meshFormat, collisionExport));
            }
        }

        internal static void ApplyCollisionStrategyPrefix(Link link)
        {
            if (link == null)
            {
                return;
            }

            CollisionMeshStrategy strategy;
            string cleanName = StripCollisionStrategyPrefix(link.Name, out strategy);
            if (!String.Equals(cleanName, link.Name, StringComparison.Ordinal))
            {
                logger.Info(link.Name + ": collision strategy tag parsed as " + strategy +
                    "; exported link name is " + cleanName);
                link.Name = cleanName;
                link.CollisionMeshStrategy = strategy;
            }
        }

        internal static string StripCollisionStrategyPrefix(
            string linkName,
            out CollisionMeshStrategy strategy)
        {
            strategy = CollisionMeshStrategy.VisualMesh;
            if (String.IsNullOrEmpty(linkName))
            {
                return linkName;
            }

            if (linkName.StartsWith("!acc_", StringComparison.OrdinalIgnoreCase) &&
                linkName.Length > "!acc_".Length)
            {
                strategy = CollisionMeshStrategy.AccurateMesh;
                return linkName.Substring("!acc_".Length);
            }
            if (linkName.StartsWith("!pri_", StringComparison.OrdinalIgnoreCase) &&
                linkName.Length > "!pri_".Length)
            {
                strategy = CollisionMeshStrategy.Primitive;
                return linkName.Substring("!pri_".Length);
            }
            if (linkName.StartsWith("!cxh_", StringComparison.OrdinalIgnoreCase) &&
                linkName.Length > "!cxh_".Length)
            {
                strategy = CollisionMeshStrategy.ConvexHull;
                return linkName.Substring("!cxh_".Length);
            }

            return linkName;
        }

        internal static MeshFileNames CreateLinkMeshFileNames(
            URDFPackage package,
            Link link,
            MeshExportFormat meshFormat)
        {
            string linkName = link.Name.Replace('/', '_');
            string extension = GetMeshFileExtension(meshFormat);
            return new MeshFileNames
            {
                VisualMeshFilename = package.MeshesDirectory + "visual/" + linkName + extension,
                WindowsVisualMeshFilename = Path.Combine(
                    package.WindowsMeshesDirectory, "visual", linkName + extension),
                CollisionMeshFilename = package.MeshesDirectory + "collision/" + linkName + extension,
                WindowsCollisionMeshFilename = Path.Combine(
                    package.WindowsMeshesDirectory, "collision", linkName + extension)
            };
        }

        private static string GetMeshFileExtension(MeshExportFormat meshFormat)
        {
            switch (meshFormat)
            {
                case MeshExportFormat.THREEDXML:
                    return ".3dxml";

                case MeshExportFormat.STL:
                default:
                    return ".STL";
            }
        }

        private static void WriteMeshManifestCsv(
            string csvFileName,
            IEnumerable<MeshExportRecord> records)
        {
            string directory = Path.GetDirectoryName(csvFileName);
            if (!String.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                csvFileName,
                BuildMeshManifestCsv(records),
                new UTF8Encoding(false));
        }

        internal static string BuildMeshManifestCsv(IEnumerable<MeshExportRecord> records)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(
                "link,collision_strategy,collision_effective_strategy,collision_geometry,collision_notes,mesh_format,visual_uri,collision_uri,visual_windows_path,collision_windows_path,visual_exists,collision_exists,visual_bytes,collision_bytes,visual_triangles,collision_triangles");
            foreach (MeshExportRecord record in records)
            {
                builder.AppendLine(String.Join(",", new[]
                {
                    CsvField(record.LinkName),
                    record.CollisionStrategy,
                    record.CollisionEffectiveStrategy,
                    record.CollisionGeometryType,
                    CsvField(record.CollisionNotes),
                    record.MeshFormat,
                    CsvField(record.VisualUri),
                    CsvField(record.CollisionUri),
                    CsvField(record.VisualWindowsPath),
                    CsvField(record.CollisionWindowsPath),
                    record.VisualExists ? "true" : "false",
                    record.CollisionExists ? "true" : "false",
                    FormatNullableLong(record.VisualBytes),
                    FormatNullableLong(record.CollisionBytes),
                    FormatNullableUInt(record.VisualTriangles),
                    FormatNullableUInt(record.CollisionTriangles)
                }));
            }

            return builder.ToString();
        }

        private static MeshExportRecord CreateMeshExportRecord(
            Link link,
            MeshFileNames meshFiles,
            MeshExportFormat meshFormat,
            CollisionMeshExportResult collisionExport)
        {
            bool visualExists = File.Exists(meshFiles.WindowsVisualMeshFilename);
            bool collisionExists = File.Exists(meshFiles.WindowsCollisionMeshFilename);
            bool isStl = meshFormat == MeshExportFormat.STL;
            CollisionMeshExportResult safeCollisionExport =
                collisionExport ?? CollisionMeshExportResult.NotExported(link.CollisionMeshStrategy);
            return new MeshExportRecord(
                link.Name,
                link.CollisionMeshStrategy.ToString(),
                safeCollisionExport.EffectiveStrategy.ToString(),
                safeCollisionExport.GeometryType,
                safeCollisionExport.Notes,
                meshFormat.ToString(),
                meshFiles.VisualMeshFilename,
                meshFiles.CollisionMeshFilename,
                meshFiles.WindowsVisualMeshFilename,
                meshFiles.WindowsCollisionMeshFilename,
                visualExists,
                collisionExists,
                visualExists ? (long?)new FileInfo(meshFiles.WindowsVisualMeshFilename).Length : null,
                collisionExists ? (long?)new FileInfo(meshFiles.WindowsCollisionMeshFilename).Length : null,
                isStl && visualExists ? TryReadBinaryStlTriangleCount(meshFiles.WindowsVisualMeshFilename) : null,
                isStl && collisionExists ? TryReadBinaryStlTriangleCount(meshFiles.WindowsCollisionMeshFilename) : null);
        }

        private static uint? TryReadBinaryStlTriangleCount(string filename)
        {
            try
            {
                return ReadBinaryStlTriangleCount(filename);
            }
            catch
            {
                return null;
            }
        }

        private static string FormatNullableLong(long? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
        }

        private static string FormatNullableUInt(uint? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
        }

        private CollisionMeshExportResult ExportCollisionMesh(
            Link link,
            MeshFileNames meshFiles,
            MeshExportFormat meshFormat)
        {
            switch (link.CollisionMeshStrategy)
            {
                case CollisionMeshStrategy.Primitive:
                    if (meshFormat == MeshExportFormat.STL &&
                        TryWritePrimitiveCollisionMesh(link, meshFiles.WindowsCollisionMeshFilename))
                    {
                        return new CollisionMeshExportResult(
                            CollisionMeshStrategy.Primitive,
                            CollisionMeshStrategy.Primitive,
                            "box_primitive",
                            "ok");
                    }
                    logger.Warn(link.Name + ": primitive collision mesh failed; falling back to visual mesh copy");
                    CopyVisualMeshToCollisionMesh(link, meshFiles);
                    return new CollisionMeshExportResult(
                        CollisionMeshStrategy.Primitive,
                        CollisionMeshStrategy.VisualMesh,
                        "visual_mesh_copy",
                        meshFormat == MeshExportFormat.STL
                            ? "primitive_failed_visual_mesh_fallback"
                            : "primitive_requires_stl_visual_mesh_fallback");

                case CollisionMeshStrategy.ConvexHull:
                    logger.Warn(link.Name + ": convex hull collision mesh is not implemented yet; " +
                        "falling back to primitive box collision");
                    if (meshFormat == MeshExportFormat.STL &&
                        TryWritePrimitiveCollisionMesh(link, meshFiles.WindowsCollisionMeshFilename))
                    {
                        return new CollisionMeshExportResult(
                            CollisionMeshStrategy.ConvexHull,
                            CollisionMeshStrategy.Primitive,
                            "box_primitive",
                            "convex_hull_not_implemented_box_fallback");
                    }
                    logger.Warn(link.Name + ": convex hull fallback failed; falling back to visual mesh copy");
                    CopyVisualMeshToCollisionMesh(link, meshFiles);
                    return new CollisionMeshExportResult(
                        CollisionMeshStrategy.ConvexHull,
                        CollisionMeshStrategy.VisualMesh,
                        "visual_mesh_copy",
                        "convex_hull_not_implemented_visual_mesh_fallback");

                case CollisionMeshStrategy.AccurateMesh:
                    CopyVisualMeshToCollisionMesh(link, meshFiles);
                    return new CollisionMeshExportResult(
                        CollisionMeshStrategy.AccurateMesh,
                        CollisionMeshStrategy.VisualMesh,
                        "visual_mesh_copy",
                        "accurate_collision_mesh_not_implemented_visual_mesh_copy");

                case CollisionMeshStrategy.VisualMesh:
                default:
                    CopyVisualMeshToCollisionMesh(link, meshFiles);
                    return new CollisionMeshExportResult(
                        link.CollisionMeshStrategy,
                        CollisionMeshStrategy.VisualMesh,
                        "visual_mesh_copy",
                        "ok");
            }
        }

        private static void CopyVisualMeshToCollisionMesh(Link link, MeshFileNames meshFiles)
        {
            if (meshFiles.WindowsVisualMeshFilename == meshFiles.WindowsCollisionMeshFilename)
            {
                return;
            }

            if (!File.Exists(meshFiles.WindowsVisualMeshFilename))
            {
                logger.Warn(link.Name + ": visual mesh was not found, collision mesh copy skipped: " +
                    meshFiles.WindowsVisualMeshFilename);
                return;
            }

            File.Copy(meshFiles.WindowsVisualMeshFilename, meshFiles.WindowsCollisionMeshFilename, true);
            logger.Info(link.Name + ": copied visual mesh to collision mesh " +
                meshFiles.WindowsCollisionMeshFilename);
        }

        private bool TryWritePrimitiveCollisionMesh(Link link, string windowsCollisionMeshFilename)
        {
            try
            {
                LinkLocalBoundingBox box = CreateLinkLocalBoundingBox(link);
                if (!box.IsUsable)
                {
                    logger.Warn(link.Name + ": could not create primitive collision box");
                    return false;
                }

                WriteBoxPrimitiveStl(windowsCollisionMeshFilename, box);
                logger.Info(link.Name + ": wrote primitive collision box " +
                    windowsCollisionMeshFilename + " with dimensions " +
                    box.Width + " x " + box.Depth + " x " + box.Height + " m");
                return true;
            }
            catch (Exception e)
            {
                logger.Warn(link.Name + ": primitive collision mesh export failed: " + e.Message);
                return false;
            }
        }

        private LinkLocalBoundingBox CreateLinkLocalBoundingBox(Link link)
        {
            LinkLocalBoundingBox box = new LinkLocalBoundingBox();
            if (link == null || link.SWComponents == null || link.SWComponents.Count == 0 ||
                link.Joint == null || String.IsNullOrWhiteSpace(link.Joint.CoordinateSystemName))
            {
                return box;
            }

            MathTransform linkTransform = GetCoordinateSystemTransform(link.Joint.CoordinateSystemName);
            if (linkTransform == null)
            {
                return box;
            }

            Matrix<double> globalToLink = MathOps.GetTransformation(linkTransform).Inverse();
            foreach (Component2 comp in link.SWComponents)
            {
                if (comp == null)
                {
                    continue;
                }

                double[] componentBox = comp.GetBox(false, false);
                IncludeTransformedBoxCorners(box, globalToLink, componentBox);
            }

            return box;
        }

        private static void IncludeTransformedBoxCorners(
            LinkLocalBoundingBox targetBox,
            Matrix<double> globalToLink,
            double[] componentBox)
        {
            if (componentBox == null || componentBox.Length < 6)
            {
                return;
            }

            double x0 = componentBox[0];
            double y0 = componentBox[1];
            double z0 = componentBox[2];
            double x1 = componentBox[3];
            double y1 = componentBox[4];
            double z1 = componentBox[5];

            IncludeTransformedPoint(targetBox, globalToLink, x0, y0, z0);
            IncludeTransformedPoint(targetBox, globalToLink, x0, y0, z1);
            IncludeTransformedPoint(targetBox, globalToLink, x0, y1, z0);
            IncludeTransformedPoint(targetBox, globalToLink, x0, y1, z1);
            IncludeTransformedPoint(targetBox, globalToLink, x1, y0, z0);
            IncludeTransformedPoint(targetBox, globalToLink, x1, y0, z1);
            IncludeTransformedPoint(targetBox, globalToLink, x1, y1, z0);
            IncludeTransformedPoint(targetBox, globalToLink, x1, y1, z1);
        }

        private static void IncludeTransformedPoint(
            LinkLocalBoundingBox targetBox,
            Matrix<double> transform,
            double x,
            double y,
            double z)
        {
            targetBox.Include(
                transform[0, 0] * x + transform[0, 1] * y + transform[0, 2] * z + transform[0, 3],
                transform[1, 0] * x + transform[1, 1] * y + transform[1, 2] * z + transform[1, 3],
                transform[2, 0] * x + transform[2, 1] * y + transform[2, 2] * z + transform[2, 3]);
        }

        internal static void WriteBoxPrimitiveStl(string filename, LinkLocalBoundingBox box)
        {
            if (box == null || !box.IsUsable)
            {
                throw new InvalidOperationException("Primitive collision box is invalid");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filename));
            double[][] vertices = box.CreateCornerVertices();
            int[][] triangles = new[]
            {
                new[] { 0, 2, 1 },
                new[] { 0, 3, 2 },
                new[] { 4, 5, 6 },
                new[] { 4, 6, 7 },
                new[] { 0, 1, 5 },
                new[] { 0, 5, 4 },
                new[] { 3, 6, 2 },
                new[] { 3, 7, 6 },
                new[] { 0, 4, 7 },
                new[] { 0, 7, 3 },
                new[] { 1, 2, 6 },
                new[] { 1, 6, 5 }
            };

            using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create, FileAccess.Write)))
            {
                byte[] header = new byte[80];
                writer.Write(header);
                writer.Write((uint)triangles.Length);
                foreach (int[] triangle in triangles)
                {
                    WriteBinaryStlTriangle(
                        writer,
                        vertices[triangle[0]],
                        vertices[triangle[1]],
                        vertices[triangle[2]]);
                }
            }
        }

        private static void WriteBinaryStlTriangle(
            BinaryWriter writer,
            double[] p0,
            double[] p1,
            double[] p2)
        {
            double[] normal = CalculateTriangleNormal(p0, p1, p2);
            WriteBinaryStlVector(writer, normal);
            WriteBinaryStlVector(writer, p0);
            WriteBinaryStlVector(writer, p1);
            WriteBinaryStlVector(writer, p2);
            writer.Write((ushort)0);
        }

        private static double[] CalculateTriangleNormal(double[] p0, double[] p1, double[] p2)
        {
            double ux = p1[0] - p0[0];
            double uy = p1[1] - p0[1];
            double uz = p1[2] - p0[2];
            double vx = p2[0] - p0[0];
            double vy = p2[1] - p0[1];
            double vz = p2[2] - p0[2];

            double nx = uy * vz - uz * vy;
            double ny = uz * vx - ux * vz;
            double nz = ux * vy - uy * vx;
            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length <= 0)
            {
                return new[] { 0.0, 0.0, 0.0 };
            }

            return new[] { nx / length, ny / length, nz / length };
        }

        private static void WriteBinaryStlVector(BinaryWriter writer, double[] point)
        {
            writer.Write((float)point[0]);
            writer.Write((float)point[1]);
            writer.Write((float)point[2]);
        }

        internal class LinkLocalBoundingBox
        {
            private const double MinimumDimension = 1e-9;
            private bool hasPoint;

            public double MinX { get; private set; }
            public double MinY { get; private set; }
            public double MinZ { get; private set; }
            public double MaxX { get; private set; }
            public double MaxY { get; private set; }
            public double MaxZ { get; private set; }

            public double Width => MaxX - MinX;
            public double Depth => MaxY - MinY;
            public double Height => MaxZ - MinZ;

            public bool IsUsable =>
                hasPoint &&
                IsFinite(MinX) &&
                IsFinite(MinY) &&
                IsFinite(MinZ) &&
                IsFinite(MaxX) &&
                IsFinite(MaxY) &&
                IsFinite(MaxZ) &&
                Width > MinimumDimension &&
                Depth > MinimumDimension &&
                Height > MinimumDimension;

            public void Include(double x, double y, double z)
            {
                if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
                {
                    return;
                }

                if (!hasPoint)
                {
                    MinX = MaxX = x;
                    MinY = MaxY = y;
                    MinZ = MaxZ = z;
                    hasPoint = true;
                    return;
                }

                MinX = Math.Min(MinX, x);
                MinY = Math.Min(MinY, y);
                MinZ = Math.Min(MinZ, z);
                MaxX = Math.Max(MaxX, x);
                MaxY = Math.Max(MaxY, y);
                MaxZ = Math.Max(MaxZ, z);
            }

            public double[][] CreateCornerVertices()
            {
                return new[]
                {
                    new[] { MinX, MinY, MinZ },
                    new[] { MaxX, MinY, MinZ },
                    new[] { MaxX, MaxY, MinZ },
                    new[] { MinX, MaxY, MinZ },
                    new[] { MinX, MinY, MaxZ },
                    new[] { MaxX, MinY, MaxZ },
                    new[] { MaxX, MaxY, MaxZ },
                    new[] { MinX, MaxY, MaxZ }
                };
            }

            private static bool IsFinite(double value)
            {
                return !Double.IsNaN(value) && !Double.IsInfinity(value);
            }
        }

        private void Save3dxml(Link link, string windowsMeshFilename)
        {
            int errors = 0;
            int warnings = 0;

            string coordsysName = link.Joint.CoordinateSystemName;

            logger.Info(link.Name + ": Exporting 3dxml with coordinate frame " + coordsysName);

            Dictionary<string, string> names = GetComponentRefGeoNames(coordsysName);
            ModelDoc2 ActiveDoc = ActiveSWModel;

            logger.Info(link.Name + ": Reference geometry name " + names["component"]);

            CommonSwOperations.ShowComponents(ActiveSWModel, link.SWComponents);

            int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent |
                (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
            SetLinkSpecificSTLPreferences(names["geo"], link, ActiveDoc);

            logger.Info("Saving 3dxml to " + windowsMeshFilename);

            // === 3dxml Localize Link === //

            // Remove suffix from coordinate-system name.
            // ex. "Joint Origin <Arm_link-1>" -> "Joint Origin"
            // Suffix is included when coordinate is inside sub-assembly.
            string linkModelName = names["component"];
            string linkModelSuffix = " <" + linkModelName + ">";
            if(coordsysName.Contains(linkModelSuffix))
            {
                coordsysName = coordsysName.Replace(linkModelSuffix, "");
                logger.Info($"Suffix of {linkModelName} was removed from coordsysName : {coordsysName}");
            }

            // Get the model document of the link.
            ModelDoc2 linkModel;
            bool isBaseLink = linkModelName == "";
            if (isBaseLink)
            {
                linkModel = ActiveDoc;
            }
            else
            {
                if (link.SWMainComponent != null)
                {
                    linkModel = link.SWMainComponent.GetModelDoc2();
                }
                else
                {
                    logger.Warn("Could not get linkModel because SWMainComponent was null");
                    linkModel = null;
                }
            }

            // Localize the link to the certain place.
            if (linkModel != null)
            {
                MathTransform coordSysTransform =
                    linkModel.Extension.GetCoordinateSystemTransformByName(coordsysName);
                if (coordSysTransform != null)
                {
                    logger.Info("Localizing Link : " + coordsysName);
                    Matrix<double> GlobalTransform = MathOps.GetTransformation(coordSysTransform);
                    LocalizeLink(link, GlobalTransform);
                }
                else
                {
                    logger.Warn("coordSysTransform was null : " + coordsysName);
                }
            }
            else
            { 
                logger.Warn("Link model was null.");
            }
            // === 3dxml Localize Link === //

            ActiveDoc.Extension.SaveAs(windowsMeshFilename,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion, saveOptions, null, ref errors, ref warnings);

            if (errors + warnings != 0)
            {
                logger.Warn("Exporting 3dxml for link " + link.Name + " failed with error " + errors +
                    " or warnings " + warnings);
            }
            CommonSwOperations.HideComponents(ActiveSWModel, link.SWComponents);
        }

        private bool SaveSTL(Link link, string windowsMeshFilename)
        {
            using (OperationHeartbeat.Start(logger, "STL export for link " + link.Name))
            {
                int errors = 0;
                int warnings = 0;

                UpdateProgressTitle("Preparing STL: " + link.Name,
                    "\u6b63\u5728\u51c6\u5907 STL: " + link.Name);

                string coordsysName = link.Joint.CoordinateSystemName;

                logger.Info(link.Name + ": Exporting STL with coordinate frame " + coordsysName);

                Dictionary<string, string> names = GetComponentRefGeoNames(coordsysName);
                ModelDoc2 ActiveDoc = ActiveSWModel;

                logger.Info(link.Name + ": Reference geometry name " + names["component"]);

                CommonSwOperations.ShowComponents(ActiveSWModel, link.SWComponents);

                int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent |
                    (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
                StlMeshSettings meshSettings =
                    SetLinkSpecificSTLPreferences(names["geo"], link, ActiveDoc);
                int estimatedTriangleCount = LogEstimatedBinaryStlSize(link, meshSettings);

                logger.Info("Saving STL to " + windowsMeshFilename);
                UpdateProgressTitle("SolidWorks is saving STL: " + link.Name,
                    "SolidWorks \u6b63\u5728\u4fdd\u5b58 STL: " + link.Name);
                ActiveDoc.Extension.SaveAs(windowsMeshFilename,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion, saveOptions, null,
                    ref errors, ref warnings);
                if (errors + warnings != 0)
                {
                    logger.Warn("Exporting STL for link " + link.Name + " failed with error " +
                        errors + " or warnings " + warnings);
                }
                CommonSwOperations.HideComponents(ActiveSWModel, link.SWComponents);

                UpdateProgressTitle("Finalizing STL: " + link.Name,
                    "\u6b63\u5728\u6574\u7406 STL: " + link.Name);
                bool success = CorrectSTLMesh(windowsMeshFilename);
                LogActualBinaryStlSize(link, windowsMeshFilename, estimatedTriangleCount);
                if (!success)
                {
                    logger.Warn("There was an issue exporting the STL for " + link.Name + ". It " +
                        "may not be readable by CAD programs that aren't SolidWorks");
                }
                return success;
            }
        }

        public void ExportLink(bool zIsUp)
        {
            logger.Info("Beginning part export for package " + PackageName +
                ", save path " + SavePath +
                ", z is up " + zIsUp +
                ", started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            CreateBaseRefOrigin(zIsUp);
            MathTransform coordSysTransform =
                ActiveSWModel.Extension.GetCoordinateSystemTransformByName("Origin_global");
            Matrix<double> GlobalTransform = MathOps.GetTransformation(coordSysTransform);

            LocalizeLink(URDFRobot.BaseLink, GlobalTransform);

            //Creating package directories
            PackageName = URDFPackage.SanitizePackageName(PackageName);
            URDFRobot.Name = PackageName;
            URDFRobot.BaseLink.Name = PackageName;
            URDFPackage package = new URDFPackage(PackageName, SavePath);
            package.CreateDirectories();
            string meshFileName = package.MeshesDirectory + URDFRobot.BaseLink.Name + ".STL";
            string windowsMeshFileName = package.WindowsMeshesDirectory + URDFRobot.BaseLink.Name + ".STL";
            string windowsURDFFileName = package.WindowsRobotsDirectory + URDFRobot.Name + ".urdf";
            string windowsManifestFileName = package.WindowsPackageDirectory + "manifest.xml";

            //Creating manifest file
            logger.Info("Creating part manifest at " + windowsManifestFileName);
            PackageXMLWriter manifestWriter = new PackageXMLWriter(windowsManifestFileName);
            PackageXML Manifest = new PackageXML(URDFRobot.Name);
            Manifest.WriteElement(manifestWriter);

            //Customizing STL preferences to how I want them
            SaveUserPreferences();
            SetSTLExportPreferences();
            SetLinkSpecificSTLPreferences("", URDFRobot.BaseLink, ActiveSWModel);
            int errors = 0;
            int warnings = 0;

            //Saving part as STL mesh

            logger.Info("Saving part STL to " + windowsMeshFileName);
            ActiveSWModel.Extension.SaveAs(windowsMeshFileName, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
            if (errors + warnings != 0)
            {
                logger.Warn("Exporting part STL failed with error " + errors + " or warnings " + warnings);
            }
            URDFRobot.BaseLink.Visual.Geometry.Mesh.Filename = meshFileName;
            URDFRobot.BaseLink.Collision.Geometry.Mesh.Filename = meshFileName;

            URDFRobot.BaseLink.Visual.Material.Texture.Filename =
                package.TexturesDirectory + Path.GetFileName(URDFRobot.BaseLink.Visual.Material.Texture.wFilename);
            string textureSavePath =
                package.WindowsTexturesDirectory + Path.GetFileName(URDFRobot.BaseLink.Visual.Material.Texture.wFilename);
            if (!String.IsNullOrWhiteSpace(URDFRobot.BaseLink.Visual.Material.Texture.wFilename))
            {
                File.Copy(URDFRobot.BaseLink.Visual.Material.Texture.wFilename, textureSavePath, true);
            }

            //Writing URDF to file
            logger.Info("Writing part URDF file to " + windowsURDFFileName);
            URDFWriter uWriter = new URDFWriter(windowsURDFFileName);
            //mRobot.addLink(mLink);
            URDFRobot.WriteURDF(uWriter.writer);

            ResetUserPreferences();
            logger.Info("Part export completed successfully for package " + PackageName);
        }

        //Writes an empty header to the STL to get rid of the BS that SolidWorks adds to a binary STL file
        public static bool CorrectSTLMesh(string filename)
        {
            logger.Info("Removing SW header in STL file");
            try
            {
                using (FileStream fileStream = OpenFileWithRetry(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    if (fileStream.Length < 84)
                    {
                        logger.Warn("STL " + filename + " is too small to contain triangle data");
                        return false;
                    }

                    fileStream.Seek(80, SeekOrigin.Begin);
                    byte[] triangleCountBytes = new byte[4];
                    fileStream.Read(triangleCountBytes, 0, triangleCountBytes.Length);
                    uint triangleCount = BitConverter.ToUInt32(triangleCountBytes, 0);
                    if (triangleCount == 0)
                    {
                        logger.Warn("STL " + filename + " contains zero triangles");
                        return false;
                    }

                    fileStream.Seek(0, SeekOrigin.Begin);
                    byte[] emptyHeader = new byte[80];
                    fileStream.Write(emptyHeader, 0, emptyHeader.Length);
                }
            }
            catch (Exception e)
            {
                logger.Warn("Correcting the STL " + filename + " failed. This STL may not be " +
                    "readable by ROS or other CAD programs", e);
                return false;
            }
            return true;
        }

        private static FileStream OpenFileWithRetry(string filename, FileMode mode, FileAccess access, FileShare share)
        {
            const int timeoutMilliseconds = 15000;
            const int sleepMilliseconds = 250;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            Exception lastException = null;

            while (DateTime.UtcNow <= deadline)
            {
                try
                {
                    return new FileStream(filename, mode, access, share);
                }
                catch (IOException e)
                {
                    lastException = e;
                }
                catch (UnauthorizedAccessException e)
                {
                    lastException = e;
                }

                Thread.Sleep(sleepMilliseconds);
                System.Windows.Forms.Application.DoEvents();
            }

            throw new IOException("Timed out waiting for file access: " + filename, lastException);
        }

        #endregion Export Methods

        private static void CopyLogFile(URDFPackage package)
        {
            string destination = package.WindowsExportLogFile;
            string log_filename = Logger.GetFileName();

            if (log_filename != null)
            {
                if (!File.Exists(log_filename))
                {
                    System.Windows.Forms.MessageBox.Show("The log file was expected to be located at " + log_filename +
                        ", but it was not found. Please contact your maintainer with this error message.");
                }
                else
                {
                    logger.Info("Copying " + log_filename + " to " + destination);
                    CopyLogFileWithSharedRead(log_filename, destination);
                }
            }
        }

        private static void CopyLogFileWithSharedRead(string source, string destination)
        {
            using (FileStream sourceStream = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (FileStream destinationStream = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                sourceStream.CopyTo(destinationStream);
            }
        }

        #region STL Preference shuffling

        //Saves the preferences that the user had setup so that I can change them and revert back to their configuration
        private void SaveUserPreferences()
        {
            logger.Info("Saving users preferences");
            mBinary = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat);
            mTranslateToPositive = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive);
            mSTLUnits = iSwApp.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits);
            mSTLQuality = iSwApp.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality);
            mSTLDeviation = iSwApp.GetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swSTLDeviation);
            mSTLAngleTolerance = iSwApp.GetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swSTLAngleTolerance);
            mshowInfo = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave);
            mSTLPreview = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview);
            mHideTransitionSpeed = iSwApp.GetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent);
            mSaveComponentsIntoOneFile = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile);
        }

        //This is how the STL export preferences need to be to properly export
        private void SetSTLExportPreferences()
        {
            logger.Info("Setting STL preferences");
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, true);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, true);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits, 2);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, (int)swSTLQuality_e.swSTLQuality_Coarse);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, false);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview, false);
            iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent, 0);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile, true);
        }

        //This resets the user preferences back to what they were.
        private void ResetUserPreferences()
        {
            logger.Info("Returning STL preferences to user preferences");
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, mBinary);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, mTranslateToPositive);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits, mSTLUnits);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, mSTLQuality);
            iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swSTLDeviation, mSTLDeviation);
            iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swSTLAngleTolerance, mSTLAngleTolerance);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, mshowInfo);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview, mSTLPreview);
            iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent, mHideTransitionSpeed);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile, mSaveComponentsIntoOneFile);
        }

        //If the user selected something specific for a particular link, that is handled here.
        private StlMeshSettings SetLinkSpecificSTLPreferences(string CoordinateSystemName, Link link, ModelDoc2 doc)
        {
            doc.Extension.SetUserPreferenceString((int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem,
                (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified, CoordinateSystemName);
            StlMeshSettings settings = CreateStlMeshSettings(link.STLQualityFine, link.MeshReductionRatio);
            if (settings.UseCustom)
            {
                iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality,
                    (int)swSTLQuality_e.swSTLQuality_Custom);
                iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swSTLDeviation,
                    settings.Deviation);
                iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swSTLAngleTolerance,
                    settings.AngleTolerance);
            }
            else if (link.STLQualityFine)
            {
                iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, (int)swSTLQuality_e.swSTLQuality_Fine);
            }
            else
            {
                iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, (int)swSTLQuality_e.swSTLQuality_Coarse);
            }

            logger.Info(string.Format(
                "{0}: STL mesh settings quality={1}, reduction={2:0.00}, custom={3}, deviation={4:G5} m, angle={5:G5} rad",
                link.Name,
                settings.QualityLabel,
                settings.ReductionRatio,
                settings.UseCustom,
                settings.Deviation,
                settings.AngleTolerance));
            return settings;
        }

        internal static StlMeshSettings CreateStlMeshSettings(bool qualityFine, double reductionRatio)
        {
            reductionRatio = Math.Max(0.0, Math.Min(1.0, reductionRatio));
            double estimateRatio = reductionRatio > 0 ? reductionRatio : (qualityFine ? 0.25 : 0.75);
            double curvedRatio = estimateRatio * estimateRatio;
            return new StlMeshSettings
            {
                UseCustom = reductionRatio > 0,
                QualityLabel = reductionRatio > 0 ? "custom" : (qualityFine ? "fine" : "coarse"),
                ReductionRatio = reductionRatio,
                Deviation = MinimumCustomStlDeviation +
                    (MaximumCustomStlDeviation - MinimumCustomStlDeviation) * curvedRatio,
                AngleTolerance = MinimumCustomStlAngleTolerance +
                    (MaximumCustomStlAngleTolerance - MinimumCustomStlAngleTolerance) * estimateRatio
            };
        }

        private int LogEstimatedBinaryStlSize(Link link, StlMeshSettings settings)
        {
            try
            {
                int triangleCount = EstimateStlTriangleCount(link, settings);
                if (triangleCount <= 0)
                {
                    logger.Info(link.Name + ": STL size estimate unavailable because tessellation returned no facets");
                    return 0;
                }

                long estimatedBytes = EstimateBinaryStlSizeBytes(triangleCount);
                logger.Info(string.Format(
                    "{0}: SolidWorks API rough STL estimate {1} ({2} triangles) before export; " +
                    "the final SaveAs tessellation can differ",
                    link.Name, FormatByteSize(estimatedBytes), triangleCount));
                return triangleCount;
            }
            catch (Exception e)
            {
                logger.Warn("Could not estimate STL size for link " + link.Name, e);
                return 0;
            }
        }

        private int EstimateStlTriangleCount(Link link, StlMeshSettings settings)
        {
            int totalFacetCount = 0;
            List<Body2> bodies = GetBodies(link.SWComponents);
            foreach (Body2 body in bodies)
            {
                Tessellation tessellation = body.GetTessellation(null) as Tessellation;
                if (tessellation == null)
                {
                    continue;
                }

                tessellation.SurfacePlaneTolerance = settings.Deviation;
                tessellation.SurfacePlaneAngleTolerance = settings.AngleTolerance;
                tessellation.CurveChordTolerance = settings.Deviation;
                tessellation.CurveChordAngleTolerance = settings.AngleTolerance;
                tessellation.ImprovedQuality = !settings.UseCustom;
                if (tessellation.Tessellate())
                {
                    totalFacetCount += tessellation.GetFacetCount();
                }
            }

            return totalFacetCount;
        }

        private void LogActualBinaryStlSize(Link link, string filename, int estimatedTriangleCount)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(filename);
                uint triangleCount = ReadBinaryStlTriangleCount(filename);
                logger.Info(string.Format("{0}: Actual binary STL size {1} ({2} triangles) at {3}",
                    link.Name, FormatByteSize(fileInfo.Length), triangleCount, filename));

                if (estimatedTriangleCount > 0)
                {
                    double errorPercent = CalculateEstimateErrorPercent(estimatedTriangleCount, triangleCount);
                    string comparison = string.Format(
                        "{0}: Rough STL estimate error {1:+0.##;-0.##;0}% " +
                        "(estimated {2} triangles, actual {3} triangles)",
                        link.Name, errorPercent, estimatedTriangleCount, triangleCount);
                    if (Math.Abs(errorPercent) > 50.0)
                    {
                        logger.Warn(comparison);
                    }
                    else
                    {
                        logger.Info(comparison);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Warn("Could not read exported STL size for link " + link.Name, e);
            }
        }

        private static uint ReadBinaryStlTriangleCount(string filename)
        {
            using (FileStream fileStream = OpenFileWithRetry(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fileStream.Length < 84)
                {
                    return 0;
                }

                fileStream.Seek(80, SeekOrigin.Begin);
                byte[] triangleCountBytes = new byte[4];
                fileStream.Read(triangleCountBytes, 0, triangleCountBytes.Length);
                return BitConverter.ToUInt32(triangleCountBytes, 0);
            }
        }

        internal static long EstimateBinaryStlSizeBytes(int triangleCount)
        {
            return triangleCount > 0 ? 84L + 50L * triangleCount : 0;
        }

        internal static double CalculateEstimateErrorPercent(long estimatedTriangleCount, long actualTriangleCount)
        {
            if (actualTriangleCount <= 0)
            {
                return 0.0;
            }

            return (estimatedTriangleCount - actualTriangleCount) * 100.0 / actualTriangleCount;
        }

        private static string FormatByteSize(long bytes)
        {
            const double scale = 1024.0;
            if (bytes < scale)
            {
                return bytes + " B";
            }

            double kib = bytes / scale;
            if (kib < scale)
            {
                return kib.ToString("0.##") + " KiB";
            }

            double mib = kib / scale;
            return mib.ToString("0.##") + " MiB";
        }

        private void UpdateProgressTitle(string english, string chinese)
        {
            string title = ChineseUiText.Translate(english, chinese);
            exportStageNumber++;
            string elapsed = exportStopwatch == null
                ? "not available"
                : OperationHeartbeat.FormatElapsed(exportStopwatch.Elapsed);
            logger.Info("Export stage " + exportStageNumber + ": " + title +
                "; elapsed " + elapsed);

            if (progressBar != null)
            {
                progressBar.UpdateTitle(title);
            }
        }

        internal class StlMeshSettings
        {
            public bool UseCustom { get; set; }

            public string QualityLabel { get; set; }

            public double ReductionRatio { get; set; }

            public double Deviation { get; set; }

            public double AngleTolerance { get; set; }
        }

        internal class MeshFileNames
        {
            public string VisualMeshFilename { get; set; }

            public string WindowsVisualMeshFilename { get; set; }

            public string CollisionMeshFilename { get; set; }

            public string WindowsCollisionMeshFilename { get; set; }
        }

        #endregion STL Preference shuffling
    }
}
