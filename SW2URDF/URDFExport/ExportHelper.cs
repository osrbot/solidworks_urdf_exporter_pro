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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
        public event EventHandler<ExportProgressEventArgs> ExportProgressChanged;

        [XmlIgnore]
        public ExportResultSummary LastExportSummary { get; private set; }
        private static readonly int[][] BoxTriangleIndices = new[]
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

        [XmlIgnore]
        public ExportTargetOptions ExportTargets
        { get; set; }

        public readonly List<Link> Links;

        private readonly ReferenceGeometryCatalog referenceGeometryCatalog;
        private readonly ReferenceGeometryResolver referenceGeometryResolver;

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

            referenceGeometryCatalog = new ReferenceGeometryCatalog(ActiveSWModel);
            referenceGeometryResolver = new ReferenceGeometryResolver(ActiveSWModel);

            ComputeInertialValues = true;
            ComputeVisualCollision = true;
            ComputeJointKinematics = true;
            ComputeJointLimits = true;
            ExportTargets = ExportTargetOptions.LegacyCompatibilityDefaults();
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
        public bool ExportRobot(bool exportSTL = true, MeshExportFormat meshFormat = MeshExportFormat.STL)
        {
            ExportErrorWhy = "";
            LastExportSummary = null;
            exportStopwatch = Stopwatch.StartNew();
            exportStageNumber = 0;
            logger.Info("Beginning the export process");
            logger.Info("Export metadata: plugin version " + Versioning.Version.GetPluginVersion() +
                ", commit version " + Versioning.Version.GetCommitVersion() +
                ", commit hash " + Versioning.Version.GetCommitHash() +
                ", build version " + Versioning.Version.GetBuildVersion() +
                ", build time UTC " + Versioning.Version.GetBuildTimeUtc() +
                ", dirty state " + Versioning.Version.GetDirtyState() +
                ", started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") +
                ", robot " + PackageName +
                ", ROS package " + RosPackageName +
                ", save path " + SavePath +
                ", export meshes " + exportSTL +
                ", mesh format " + meshFormat);
            bool success = false;
            bool progressStarted = false;
            bool preferencesSaved = false;
            bool visibilityMayHaveChanged = false;
            List<ComponentVisibilityState> assemblyVisibility = null;
            List<MeshExportRecord> meshRecords = new List<MeshExportRecord>();
            URDFPackage exportedPackage = null;
            ExportOutputSnapshot outputBeforeExport = null;
            string stagingDirectory = null;
            V2ExportResult v2Result = null;
            try
            {
                int progressBarBound = GetMeshExportLinks(URDFRobot.BaseLink).Count;
                iSwApp.GetUserProgressBar(out progressBar);
                progressStarted = true;
                progressBar.Start(0, progressBarBound,
                    ChineseUiText.Translate("Creating package directories", "\u6b63\u5728\u521b\u5efa\u529f\u80fd\u5305\u76ee\u5f55"));

                PackageName = URDFPackage.SanitizePackageName(PackageName);
                RosPackageName = URDFPackage.SanitizePackageName(RosPackageName);
                logger.Info("Creating package directories with ROS package name " + RosPackageName +
                    ", robot name " + PackageName + " and save path " + SavePath);
                URDFPackage deliveryPackage = new URDFPackage(PackageName, RosPackageName, SavePath);
                bool requiresV2Staging = ExportTargets != null &&
                    ExportTargets.UseV2Pipeline;
                URDFPackage package = deliveryPackage;
                if (requiresV2Staging)
                {
                    stagingDirectory = CreateV2ExportStagingDirectory();
                    package = new URDFPackage(PackageName, RosPackageName, stagingDirectory);
                    logger.Info("Using isolated source staging for the v2 export pipeline at " +
                        stagingDirectory + ".");
                }
                exportedPackage = deliveryPackage;
                outputBeforeExport = ExportOutputSnapshot.Capture(deliveryPackage);
                package.CreateDirectories();
                URDFRobot.Name = PackageName;
                string windowsURDFFileName = package.WindowsRobotsDirectory + URDFRobot.Name + ".urdf";
                string windowsCSVFileName = package.WindowsRobotsDirectory + URDFRobot.Name + ".csv";
                string windowsInertialValidationCsvFileName =
                    Path.Combine(package.WindowsConfigDirectory, "inertial_validation.csv");
                string windowsMeshManifestCsvFileName =
                    Path.Combine(package.WindowsConfigDirectory, "mesh_manifest.csv");
                string windowsPackageXMLFileName = package.WindowsPackageDirectory + "package.xml";

                UpdateProgressTitle("Creating ROS package metadata", "\u6b63\u5728\u521b\u5efa ROS \u529f\u80fd\u5305\u5143\u6570\u636e");
                logger.Info("Creating CMakeLists.txt at " + package.WindowsCMakeLists);
                package.CreateCMakeLists();

                logger.Info("Creating joint names config at " + package.WindowsConfigYAML);
                package.CreateConfigYAML(URDFRobot.GetJointNames(false));

                logger.Info("Creating package.xml at " + windowsPackageXMLFileName);
                PackageXMLWriter packageXMLWriter = new PackageXMLWriter(windowsPackageXMLFileName);
                PackageXML packageXML = new PackageXML(RosPackageName);
                packageXML.WriteElement(packageXMLWriter);

                Rviz rviz = new Rviz(RosPackageName, URDFRobot.Name + ".urdf");
                logger.Info("Creating RVIZ launch file in " + package.WindowsLaunchDirectory);
                rviz.WriteFiles(package.WindowsLaunchDirectory);

                Gazebo gazebo = new Gazebo(URDFRobot.Name, RosPackageName, URDFRobot.Name + ".urdf");
                logger.Info("Creating Gazebo launch file in " + package.WindowsLaunchDirectory);
                gazebo.WriteFile(package.WindowsLaunchDirectory);

                List<InertialValidationRecord> inertialRecords =
                    new List<InertialValidationRecord>();
                if (ComputeInertialValues)
                {
                    inertialRecords = LogInertialValidation(
                        URDFRobot.BaseLink,
                        windowsInertialValidationCsvFileName);
                    EnsureNoBlockingInertialFailures(inertialRecords);
                }
                else
                {
                    inertialRecords = BuildPhysicalInertialValidationRecords(URDFRobot.BaseLink);
                    WriteInertialValidationCsv(
                        windowsInertialValidationCsvFileName,
                        inertialRecords);
                    EnsureNoBlockingInertialFailures(inertialRecords);
                    logger.Info("Skipped only the SW comparison; explicitly supplied inertial values still passed physical checks.");
                }

                logger.Info("Saving existing STL preferences");
                SaveUserPreferences();
                preferencesSaved = true;

                logger.Info("Modifying STL preferences");
                SetSTLExportPreferences();

                AssemblyDoc assyDoc = ActiveSWModel as AssemblyDoc;
                if (assyDoc == null)
                {
                    throw new InvalidOperationException("The active SolidWorks document is not an assembly.");
                }
                assemblyVisibility = CaptureComponentVisibility(
                    CommonSwOperations.EnumerateComObjects<Component2>(assyDoc.GetComponents(false),
                        "capturing assembly visibility before export"));
                logger.Info("Captured local visibility for " + assemblyVisibility.Count + " assembly components.");
                logger.Info("Hiding all components");
                UpdateProgressTitle("Preparing SolidWorks components", "\u6b63\u5728\u51c6\u5907 SolidWorks \u7ec4\u4ef6");
                visibilityMayHaveChanged = true;
                CommonSwOperations.SetComponentVisibility(ActiveSWModel,
                    assemblyVisibility.Select(state => state.Component), false);

                logger.Info("Beginning individual files export");
                ExportFiles(URDFRobot.BaseLink, package, exportSTL, meshFormat, meshRecords);

                WriteMeshManifestCsv(windowsMeshManifestCsvFileName, meshRecords);
                logger.Info("Wrote mesh manifest CSV with " + meshRecords.Count + " rows to " +
                    windowsMeshManifestCsvFileName);
                logger.Info("Export parameter summary: " +
                    BuildExportParameterSummary(inertialRecords, meshRecords, exportSTL, meshFormat));

                Robot outputRobot = CreateMeshOutputRobot(meshFormat);
                UpdateProgressTitle("Writing URDF file", "\u6b63\u5728\u5199\u5165 URDF \u6587\u4ef6");
                logger.Info("Writing URDF file to " + windowsURDFFileName);
                URDFWriter uWriter = new URDFWriter(windowsURDFFileName);
                outputRobot.WriteURDF(uWriter.writer);

                UpdateProgressTitle("Writing CSV file", "\u6b63\u5728\u5199\u5165 CSV \u6587\u4ef6");
                ImportExport.WriteRobotToCSV(outputRobot, windowsCSVFileName);

                if (ExportTargets != null && ExportTargets.UseV2Pipeline)
                {
                    UpdateProgressTitle("Creating selected output targets", "\u6b63\u5728\u521b\u5efa\u5df2\u9009\u5bfc\u51fa\u76ee\u6807");
                    logger.Info("Creating selected outputs through the canonical v2 pipeline");
                    v2Result = V2ExportBridge.Export(
                        package,
                        deliveryPackage,
                        windowsURDFFileName,
                        outputRobot,
                        meshRecords,
                        ExportTargets,
                        (target, selected) => ValidateAndWriteTargetReport(
                            deliveryPackage, target, selected, inertialRecords,
                            exportSTL, meshFormat, exportStopwatch.Elapsed),
                        partial => v2Result = partial);
                }
                else
                {
                    UpdateProgressTitle("Creating ROS 2 package", "\u6b63\u5728\u521b\u5efa ROS 2 \u529f\u80fd\u5305");
                    logger.Info("Creating ROS 2 package at " + package.WindowsRos2PackageDirectory);
                    package.CreateRos2Package(windowsURDFFileName);
                }

                if (v2Result == null)
                {
                    UpdateProgressTitle("Writing export report", "\u6b63\u5728\u5199\u5165\u5bfc\u51fa\u62a5\u544a");
                    WriteExportReport(
                        deliveryPackage,
                        Path.Combine(deliveryPackage.WindowsRobotsDirectory, URDFRobot.Name + ".urdf"),
                        inertialRecords,
                        meshRecords,
                        exportSTL,
                        meshFormat,
                        exportStopwatch.Elapsed,
                        ExportTargets);
                    CopyLogFile(deliveryPackage);
                }
                success = true;
            }
            catch (Exception e)
            {
                ExportErrorWhy = "URDF export failed: " + e.Message +
                    ". See the UTF-8 export log at " + Logger.GetFileName();
                logger.Error("An exception was thrown attempting to export the URDF", e);
            }
            finally
            {
                bool environmentRestored = RestoreExportEnvironment(
                    assemblyVisibility,
                    visibilityMayHaveChanged,
                    preferencesSaved,
                    progressStarted);
                if (!environmentRestored && v2Result != null)
                {
                    v2Result.Warnings.Add("SolidWorks environment restoration needs attention: " + ExportErrorWhy);
                }
                else
                {
                    success = environmentRestored && success;
                }
                bool stagingDeleted = DeleteV2ExportStagingDirectory(stagingDirectory);
                if (!stagingDeleted)
                {
                    logger.Warn(
                        "Export outputs are complete, but temporary v2 staging was retained. " +
                        "The path is recorded earlier in this log for maintenance cleanup.");
                }
                exportStopwatch.Stop();
            }

            if (v2Result != null)
            {
                success = v2Result.Targets.Any(target => target.Succeeded);
                TryWriteIndependentExportReport(exportedPackage, v2Result, exportStopwatch.Elapsed);
                try
                {
                    CopyLogFile(exportedPackage);
                }
                catch (Exception exception) when (IndependentTargetExport.IsTargetFailure(exception))
                {
                    v2Result.Warnings.Add("Could not copy the export log: " + exception.Message);
                    logger.Warn("Output results are unchanged; auxiliary log copy failed.", exception);
                }
                try
                {
                    LastExportSummary = ExportResultSummary.Create(
                        exportedPackage, outputBeforeExport, exportStopwatch.Elapsed,
                        v2Result.Targets, v2Result.Warnings);
                }
                catch (Exception exception) when (IndependentTargetExport.IsTargetFailure(exception))
                {
                    v2Result.Warnings.Add("File statistics are unavailable: " + exception.Message);
                    LastExportSummary = new ExportResultSummary(
                        exportedPackage.WindowsExportRootDirectory, 0, 0, exportStopwatch.Elapsed,
                        v2Result.Targets, v2Result.Warnings);
                }
                if (!success)
                    ExportErrorWhy = String.Join(System.Environment.NewLine,
                        v2Result.Targets.Select(target => target.TargetName + ": " + target.ErrorMessage));
                logger.Info("Export finished: " + v2Result.Targets.Count(target => target.Succeeded) +
                    " succeeded, " + v2Result.Targets.Count(target => !target.Succeeded) +
                    " failed; elapsed " + OperationHeartbeat.FormatElapsed(exportStopwatch.Elapsed));
                return success;
            }
            if (!success)
            {
                logger.Error("Export process failed after " +
                    OperationHeartbeat.FormatElapsed(exportStopwatch.Elapsed));
                return false;
            }
            try
            {
                LastExportSummary = ExportResultSummary.Create(
                    exportedPackage,
                    outputBeforeExport,
                    exportStopwatch.Elapsed);
            }
            catch (Exception summaryException)
            {
                // Reporting is auxiliary. It must never turn a completed export into
                // a failure after the ROS packages have already been written.
                logger.Warn("Could not calculate the export result summary", summaryException);
            }
            logger.Info("Export process completed successfully for ROS package " + RosPackageName +
                " and robot " + PackageName + "; elapsed " +
                OperationHeartbeat.FormatElapsed(exportStopwatch.Elapsed));
            return true;
        }

        private static string CreateV2ExportStagingDirectory()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "OSURDF",
                "export-staging",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static bool DeleteV2ExportStagingDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return true;
            }
            Exception lastFailure = null;
            for (int attempt = 0; attempt < 3; ++attempt)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }
                    logger.Info("Removed temporary v2 export staging at " + directory);
                    return true;
                }
                catch (Exception exception) when (
                    exception is IOException || exception is UnauthorizedAccessException)
                {
                    lastFailure = exception;
                    if (attempt < 2)
                    {
                        Thread.Sleep(75 * (attempt + 1));
                    }
                }
            }
            logger.Warn(
                "Could not remove temporary v2 export staging at " + directory,
                lastFailure);
            return false;
        }

        private bool RestoreExportEnvironment(
            List<ComponentVisibilityState> assemblyVisibility,
            bool restoreVisibility,
            bool restorePreferences,
            bool endProgress)
        {
            bool restored = true;

            if (restoreVisibility)
            {
                try
                {
                    UpdateProgressTitle("Restoring SolidWorks component visibility",
                        "\u6b63\u5728\u6062\u590d SolidWorks \u7ec4\u4ef6\u53ef\u89c1\u6027");
                }
                catch (Exception e)
                {
                    logger.Warn("Updating the progress title during visibility restoration failed", e);
                }

                try
                {
                    logger.Info("Restoring the original local visibility of all assembly components");
                    RestoreComponentVisibility(ActiveSWModel, assemblyVisibility);
                }
                catch (Exception e)
                {
                    restored = false;
                    logger.Error("Restoring SolidWorks component visibility failed", e);
                    ExportErrorWhy = (String.IsNullOrWhiteSpace(ExportErrorWhy) ? String.Empty : ExportErrorWhy + System.Environment.NewLine) +
                        "ERROR COMPONENT_VISIBILITY: The original assembly visibility could not be restored. " + e.Message;
                }
            }

            if (restorePreferences)
            {
                try
                {
                    logger.Info("Resetting STL preferences");
                    ResetUserPreferences();
                }
                catch (Exception e)
                {
                    restored = false;
                    logger.Error("Restoring STL preferences failed", e);
                }
            }

            if (endProgress)
            {
                try
                {
                    progressBar.End();
                }
                catch (Exception e)
                {
                    restored = false;
                    logger.Error("Ending the SolidWorks export progress bar failed", e);
                }
            }

            return restored;
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

        // Export every mesh-bearing Link while traversing through fixed-frame nodes.
        private void ExportFiles(
            Link root,
            URDFPackage package,
            bool exportSTL = true,
            MeshExportFormat meshFormat = MeshExportFormat.STL,
            List<MeshExportRecord> meshRecords = null)
        {
            int count = 0;
            foreach (Link link in GetMeshExportLinks(root))
            {
                ExportLinkFiles(link, package, count, exportSTL, meshFormat, meshRecords);
                count++;
            }
        }

        internal static IList<Link> GetMeshExportLinks(Link root)
        {
            List<Link> links = new List<Link>();
            AddMeshExportLinks(root, links);
            return links;
        }

        private static void AddMeshExportLinks(Link link, ICollection<Link> links)
        {
            if (link == null)
            {
                return;
            }
            if (!link.isFixedFrame)
            {
                links.Add(link);
            }
            foreach (Link child in link.Children)
            {
                AddMeshExportLinks(child, links);
            }
        }

        private Robot CreateMeshOutputRobot(MeshExportFormat meshFormat)
        {
            Robot output = new Robot
            {
                Name = URDFRobot.Name
            };
            output.SetBaseLink(URDFRobot.BaseLink.Clone());
            if (meshFormat == MeshExportFormat.THREEDXML)
            {
                Localize3dxmlOutputOrigins(output.BaseLink);
            }
            return output;
        }

        private void Localize3dxmlOutputOrigins(Link link)
        {
            if (link == null)
            {
                return;
            }
            if (!link.isFixedFrame)
            {
                MathTransform frameTransform = GetCoordinateSystemTransform(
                    link.FrameReference);
                if (frameTransform == null)
                {
                    throw new InvalidOperationException(
                        "Cannot localize 3DXML output because the Link frame cannot be resolved: " +
                        GetReferenceDisplayLabel(link.FrameReference));
                }
                LocalizeVisualAndCollision(
                    link,
                    MathOps.GetTransformation(frameTransform));
            }
            foreach (Link child in link.Children)
            {
                Localize3dxmlOutputOrigins(child);
            }
        }

        private void ExportLinkFiles(
            Link link,
            URDFPackage package,
            int count,
            bool exportSTL,
            MeshExportFormat meshFormat,
            List<MeshExportRecord> meshRecords)
        {
            progressBar.UpdateProgress(count);
            progressBar.UpdateTitle(ChineseUiText.Translate(
                "Exporting mesh: " + link.Name,
                "\u6b63\u5728\u5bfc\u51fa\u7f51\u683c: " + link.Name));
            logger.Info("Exporting link: " + link.Name);
            logger.Info("Link " + link.Name + " has " + link.Children.Count + " children");

            // Copy the texture file (if it was specified) to the textures directory
            if (!String.IsNullOrWhiteSpace(link.Visual.Material.Texture.wFilename))
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
            StlExportStats visualStlStats = StlExportStats.NotExported();

            // Export STL
            if (exportSTL)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(meshFiles.WindowsVisualMeshFilename));
                Directory.CreateDirectory(Path.GetDirectoryName(meshFiles.WindowsCollisionMeshFilename));
                switch (meshFormat)
                {
                    case MeshExportFormat.STL:
                        visualStlStats = SaveSTL(link, meshFiles.WindowsVisualMeshFilename);
                        break;

                    case MeshExportFormat.THREEDXML:
                        Save3dxml(link, meshFiles.WindowsVisualMeshFilename);
                        break;

                    default:
                        visualStlStats = SaveSTL(link, meshFiles.WindowsVisualMeshFilename);
                        break;
                }
                collisionExport = ExportCollisionMesh(link, meshFiles, meshFormat);
            }
            link.Visual.Geometry.UseMesh(meshFiles.VisualMeshFilename);
            if (!UsesUrdfPrimitiveCollision(collisionExport))
            {
                link.ClearAdditionalCollisions();
                link.Collision.Geometry.UseMesh(meshFiles.CollisionMeshFilename);
            }
            if (meshRecords != null)
            {
                meshRecords.Add(CreateMeshExportRecord(link, meshFiles, meshFormat, collisionExport, visualStlStats));
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
            if (linkName.StartsWith("!sim_", StringComparison.OrdinalIgnoreCase) &&
                linkName.Length > "!sim_".Length)
            {
                strategy = CollisionMeshStrategy.SimplifiedMesh;
                return linkName.Substring("!sim_".Length);
            }
            if (linkName.StartsWith("!box_", StringComparison.OrdinalIgnoreCase) &&
                linkName.Length > "!box_".Length)
            {
                strategy = CollisionMeshStrategy.BoxPrimitive;
                return linkName.Substring("!box_".Length);
            }
            if (linkName.StartsWith("!pri_", StringComparison.OrdinalIgnoreCase) &&
                linkName.Length > "!pri_".Length)
            {
                strategy = CollisionMeshStrategy.Primitive;
                return linkName.Substring("!pri_".Length);
            }
            if (linkName.StartsWith("!cyl_", StringComparison.OrdinalIgnoreCase) &&
                linkName.Length > "!cyl_".Length)
            {
                strategy = CollisionMeshStrategy.CylinderPrimitive;
                return linkName.Substring("!cyl_".Length);
            }
            if (linkName.StartsWith("!sph_", StringComparison.OrdinalIgnoreCase) &&
                linkName.Length > "!sph_".Length)
            {
                strategy = CollisionMeshStrategy.SpherePrimitive;
                return linkName.Substring("!sph_".Length);
            }
            if (linkName.StartsWith("!cxh_", StringComparison.OrdinalIgnoreCase) &&
                linkName.Length > "!cxh_".Length)
            {
                strategy = CollisionMeshStrategy.ConvexHull;
                return linkName.Substring("!cxh_".Length);
            }
            if (linkName.StartsWith("!cbb_", StringComparison.OrdinalIgnoreCase) &&
                linkName.Length > "!cbb_".Length)
            {
                strategy = CollisionMeshStrategy.ComponentBoxes;
                return linkName.Substring("!cbb_".Length);
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
                "link,collision_strategy,collision_effective_strategy,collision_geometry,collision_notes,mesh_format,stl_quality,mesh_reduction_ratio,stl_custom,deviation_m,angle_tolerance_rad,baseline_estimated_visual_bytes,baseline_estimated_visual_triangles,estimated_visual_bytes,estimated_visual_triangles,estimate_error_percent,estimated_reduction_percent,actual_reduction_percent,visual_uri,collision_uri,collision_urdf_reference,visual_windows_path,collision_windows_path,visual_exists,collision_exists,visual_bytes,collision_bytes,visual_triangles,collision_triangles,collision_vs_visual_bytes_reduction_percent,collision_vs_visual_triangles_reduction_percent");
            foreach (MeshExportRecord record in records)
            {
                StlExportStats stats = record.StlStats ?? StlExportStats.NotExported();
                builder.AppendLine(String.Join(",", new[]
                {
                    CsvField(record.LinkName),
                    record.CollisionStrategy,
                    record.CollisionEffectiveStrategy,
                    record.CollisionGeometryType,
                    CsvField(record.CollisionNotes),
                    record.MeshFormat,
                    CsvField(stats.QualityLabel),
                    FormatNullableDouble(stats.ReductionRatio),
                    FormatNullableBool(stats.CustomSettings),
                    FormatNullableDouble(stats.Deviation),
                    FormatNullableDouble(stats.AngleTolerance),
                    FormatNullableLong(stats.BaselineEstimatedBytes),
                    FormatNullableInt(stats.BaselineEstimatedTriangles),
                    FormatNullableLong(stats.EstimatedBytes),
                    FormatNullableInt(stats.EstimatedTriangles),
                    FormatNullableDouble(stats.EstimateErrorPercent),
                    FormatNullableDouble(stats.EstimatedReductionPercent),
                    FormatNullableDouble(stats.ActualReductionPercent),
                    CsvField(record.VisualUri),
                    CsvField(record.CollisionUri),
                    CsvField(record.CollisionUrdfReference),
                    CsvField(record.VisualWindowsPath),
                    CsvField(record.CollisionWindowsPath),
                    record.VisualExists ? "true" : "false",
                    record.CollisionExists ? "true" : "false",
                    FormatNullableLong(record.VisualBytes),
                    FormatNullableLong(record.CollisionBytes),
                    FormatNullableUInt(record.VisualTriangles),
                    FormatNullableUInt(record.CollisionTriangles),
                    FormatNullableDouble(CalculateCollisionBytesReductionPercent(record)),
                    FormatNullableDouble(CalculateCollisionTrianglesReductionPercent(record))
                }));
            }

            return builder.ToString();
        }

        private static double? CalculateCollisionBytesReductionPercent(MeshExportRecord record)
        {
            if (record == null || !record.CollisionBytes.HasValue || !record.VisualBytes.HasValue)
            {
                return null;
            }

            return CalculateReductionPercent(record.CollisionBytes.Value, record.VisualBytes.Value);
        }

        private static double? CalculateCollisionTrianglesReductionPercent(MeshExportRecord record)
        {
            if (record == null || !record.CollisionTriangles.HasValue || !record.VisualTriangles.HasValue)
            {
                return null;
            }

            return CalculateReductionPercent(record.CollisionTriangles.Value, record.VisualTriangles.Value);
        }

        private static MeshExportRecord CreateMeshExportRecord(
            Link link,
            MeshFileNames meshFiles,
            MeshExportFormat meshFormat,
            CollisionMeshExportResult collisionExport,
            StlExportStats visualStlStats)
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
                isStl && visualExists ? TryReadStlTriangleCount(meshFiles.WindowsVisualMeshFilename) : null,
                isStl && collisionExists ? TryReadStlTriangleCount(meshFiles.WindowsCollisionMeshFilename) : null,
                visualStlStats,
                BuildCollisionUrdfReference(safeCollisionExport, meshFiles.CollisionMeshFilename));
        }

        private static string BuildCollisionUrdfReference(
            CollisionMeshExportResult collisionExport,
            string meshCollisionUri)
        {
            if (UsesUrdfPrimitiveCollision(collisionExport))
            {
                switch (collisionExport.EffectiveStrategy)
                {
                    case CollisionMeshStrategy.BoxPrimitive:
                        return "native:box";

                    case CollisionMeshStrategy.CylinderPrimitive:
                        return "native:cylinder";

                    case CollisionMeshStrategy.SpherePrimitive:
                        return "native:sphere";

                    case CollisionMeshStrategy.ComponentBoxes:
                        return "native:box_set";
                }
            }

            return meshCollisionUri;
        }

        internal static uint? TryReadStlTriangleCount(string filename)
        {
            try
            {
                return ReadStlTriangleCount(filename);
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

        private static string FormatNullableInt(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
        }

        private static string FormatNullableUInt(uint? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
        }

        private static string FormatNullableDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("G15", CultureInfo.InvariantCulture) : "";
        }

        private static string FormatNullableBool(bool? value)
        {
            return value.HasValue ? (value.Value ? "true" : "false") : "";
        }

        private CollisionMeshExportResult ExportCollisionMesh(
            Link link,
            MeshFileNames meshFiles,
            MeshExportFormat meshFormat)
        {
            LinkLocalBoundingBox primitiveBox;
            switch (link.CollisionMeshStrategy)
            {
                case CollisionMeshStrategy.Primitive:
                case CollisionMeshStrategy.BoxPrimitive:
                    if (meshFormat == MeshExportFormat.STL &&
                        TryWriteBoxPrimitiveCollisionMesh(
                            link,
                            meshFiles.WindowsCollisionMeshFilename,
                            out primitiveBox))
                    {
                        UseBoxCollisionGeometry(link, primitiveBox);
                        return new CollisionMeshExportResult(
                            link.CollisionMeshStrategy,
                            CollisionMeshStrategy.BoxPrimitive,
                            "urdf_box_primitive",
                            "ok");
                    }
                    logger.Warn(link.Name + ": primitive collision mesh failed; falling back to visual mesh copy");
                    CopyVisualMeshToCollisionMesh(link, meshFiles);
                    return new CollisionMeshExportResult(
                        link.CollisionMeshStrategy,
                        CollisionMeshStrategy.VisualMesh,
                        "visual_mesh_copy",
                        meshFormat == MeshExportFormat.STL
                            ? "primitive_failed_visual_mesh_fallback"
                            : "primitive_requires_stl_visual_mesh_fallback");

                case CollisionMeshStrategy.CylinderPrimitive:
                    if (meshFormat == MeshExportFormat.STL &&
                        TryWriteCylinderPrimitiveCollisionMesh(
                            link,
                            meshFiles.WindowsCollisionMeshFilename,
                            out primitiveBox))
                    {
                        UseCylinderCollisionGeometry(link, primitiveBox);
                        return new CollisionMeshExportResult(
                            CollisionMeshStrategy.CylinderPrimitive,
                            CollisionMeshStrategy.CylinderPrimitive,
                            "urdf_cylinder_primitive",
                            "ok");
                    }
                    logger.Warn(link.Name + ": cylinder primitive collision mesh failed; falling back to visual mesh copy");
                    CopyVisualMeshToCollisionMesh(link, meshFiles);
                    return new CollisionMeshExportResult(
                        CollisionMeshStrategy.CylinderPrimitive,
                        CollisionMeshStrategy.VisualMesh,
                        "visual_mesh_copy",
                        meshFormat == MeshExportFormat.STL
                            ? "cylinder_primitive_failed_visual_mesh_fallback"
                            : "cylinder_primitive_requires_stl_visual_mesh_fallback");

                case CollisionMeshStrategy.SpherePrimitive:
                    if (meshFormat == MeshExportFormat.STL &&
                        TryWriteSpherePrimitiveCollisionMesh(
                            link,
                            meshFiles.WindowsCollisionMeshFilename,
                            out primitiveBox))
                    {
                        UseSphereCollisionGeometry(link, primitiveBox);
                        return new CollisionMeshExportResult(
                            CollisionMeshStrategy.SpherePrimitive,
                            CollisionMeshStrategy.SpherePrimitive,
                            "urdf_sphere_primitive",
                            "ok");
                    }
                    logger.Warn(link.Name + ": sphere primitive collision mesh failed; falling back to visual mesh copy");
                    CopyVisualMeshToCollisionMesh(link, meshFiles);
                    return new CollisionMeshExportResult(
                        CollisionMeshStrategy.SpherePrimitive,
                        CollisionMeshStrategy.VisualMesh,
                        "visual_mesh_copy",
                        meshFormat == MeshExportFormat.STL
                            ? "sphere_primitive_failed_visual_mesh_fallback"
                            : "sphere_primitive_requires_stl_visual_mesh_fallback");

                case CollisionMeshStrategy.ComponentBoxes:
                    IList<LinkLocalBoundingBox> componentBoxes;
                    if (meshFormat == MeshExportFormat.STL &&
                        TryWriteComponentBoxCollisionMesh(
                            link,
                            meshFiles.WindowsCollisionMeshFilename,
                            out componentBoxes))
                    {
                        UseComponentBoxCollisionGeometry(link, componentBoxes);
                        return new CollisionMeshExportResult(
                            CollisionMeshStrategy.ComponentBoxes,
                            CollisionMeshStrategy.ComponentBoxes,
                            "urdf_component_box_set",
                            "ok");
                    }
                    logger.Warn(link.Name + ": component box collision mesh failed; falling back to visual mesh copy");
                    CopyVisualMeshToCollisionMesh(link, meshFiles);
                    return new CollisionMeshExportResult(
                        CollisionMeshStrategy.ComponentBoxes,
                        CollisionMeshStrategy.VisualMesh,
                        "visual_mesh_copy",
                        meshFormat == MeshExportFormat.STL
                            ? "component_boxes_failed_visual_mesh_fallback"
                            : "component_boxes_requires_stl_visual_mesh_fallback");

                case CollisionMeshStrategy.ConvexHull:
                    if (meshFormat == MeshExportFormat.STL &&
                        TryWriteConvexHullCollisionMesh(link, meshFiles.WindowsCollisionMeshFilename))
                    {
                        return new CollisionMeshExportResult(
                            CollisionMeshStrategy.ConvexHull,
                            CollisionMeshStrategy.ConvexHull,
                            "convex_hull",
                            "ok");
                    }
                    logger.Warn(link.Name + ": convex hull fallback failed; falling back to visual mesh copy");
                    CopyVisualMeshToCollisionMesh(link, meshFiles);
                    return new CollisionMeshExportResult(
                        CollisionMeshStrategy.ConvexHull,
                        CollisionMeshStrategy.VisualMesh,
                        "visual_mesh_copy",
                        meshFormat == MeshExportFormat.STL
                            ? "convex_hull_failed_visual_mesh_fallback"
                            : "convex_hull_requires_stl_visual_mesh_fallback");

                case CollisionMeshStrategy.SimplifiedMesh:
                    if (meshFormat == MeshExportFormat.STL &&
                        TrySaveCollisionStl(link, meshFiles.WindowsCollisionMeshFilename, 1.0))
                    {
                        return new CollisionMeshExportResult(
                            CollisionMeshStrategy.SimplifiedMesh,
                            CollisionMeshStrategy.SimplifiedMesh,
                            "simplified_stl",
                            "ok");
                    }
                    logger.Warn(link.Name + ": simplified collision STL failed; falling back to visual mesh copy");
                    CopyVisualMeshToCollisionMesh(link, meshFiles);
                    return new CollisionMeshExportResult(
                        CollisionMeshStrategy.SimplifiedMesh,
                        CollisionMeshStrategy.VisualMesh,
                        "visual_mesh_copy",
                        meshFormat == MeshExportFormat.STL
                            ? "simplified_stl_failed_visual_mesh_fallback"
                            : "simplified_stl_requires_stl_visual_mesh_fallback");

                case CollisionMeshStrategy.AccurateMesh:
                    if (meshFormat == MeshExportFormat.STL &&
                        TrySaveCollisionStl(link, meshFiles.WindowsCollisionMeshFilename, 0.0))
                    {
                        return new CollisionMeshExportResult(
                            CollisionMeshStrategy.AccurateMesh,
                            CollisionMeshStrategy.AccurateMesh,
                            "accurate_stl",
                            "ok");
                    }
                    logger.Warn(link.Name + ": accurate collision STL failed; falling back to visual mesh copy");
                    CopyVisualMeshToCollisionMesh(link, meshFiles);
                    return new CollisionMeshExportResult(
                        CollisionMeshStrategy.AccurateMesh,
                        CollisionMeshStrategy.VisualMesh,
                        "visual_mesh_copy",
                        meshFormat == MeshExportFormat.STL
                            ? "accurate_stl_failed_visual_mesh_fallback"
                            : "accurate_stl_requires_stl_visual_mesh_fallback");

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

        private bool TrySaveCollisionStl(Link link, string windowsCollisionMeshFilename, double reductionRatio)
        {
            try
            {
                SaveSTL(link, windowsCollisionMeshFilename, reductionRatio);
                return File.Exists(windowsCollisionMeshFilename);
            }
            catch (Exception e)
            {
                logger.Warn(link.Name + ": collision STL export failed: " + e.Message);
                return false;
            }
        }

        private static bool UsesUrdfPrimitiveCollision(CollisionMeshExportResult result)
        {
            if (result == null)
            {
                return false;
            }

            switch (result.EffectiveStrategy)
            {
                case CollisionMeshStrategy.BoxPrimitive:
                case CollisionMeshStrategy.CylinderPrimitive:
                case CollisionMeshStrategy.SpherePrimitive:
                case CollisionMeshStrategy.ComponentBoxes:
                    return result.Notes == "ok";

                default:
                    return false;
            }
        }

        private static void UseBoxCollisionGeometry(Link link, LinkLocalBoundingBox box)
        {
            link.ClearAdditionalCollisions();
            link.Collision.Geometry.UseBox(box.Width, box.Depth, box.Height);
            SetCollisionPrimitiveOrigin(link, box.Center, new[] { 0.0, 0.0, 0.0 });
        }

        private static void UseCylinderCollisionGeometry(Link link, LinkLocalBoundingBox box)
        {
            int axis = box.CylinderAxisIndex;
            int uAxis = (axis + 1) % 3;
            int vAxis = (axis + 2) % 3;
            double radius = Math.Max(box.GetDimension(uAxis), box.GetDimension(vAxis)) / 2.0;
            double length = box.GetDimension(axis);

            link.Collision.Geometry.UseCylinder(radius, length);
            link.ClearAdditionalCollisions();
            SetCollisionPrimitiveOrigin(link, box.Center, GetCylinderPrimitiveRpy(axis));
        }

        private static void UseSphereCollisionGeometry(Link link, LinkLocalBoundingBox box)
        {
            double radius = Math.Max(box.Width, Math.Max(box.Depth, box.Height)) / 2.0;
            link.ClearAdditionalCollisions();
            link.Collision.Geometry.UseSphere(radius);
            SetCollisionPrimitiveOrigin(link, box.Center, new[] { 0.0, 0.0, 0.0 });
        }

        private static void UseComponentBoxCollisionGeometry(Link link, IList<LinkLocalBoundingBox> boxes)
        {
            link.ClearAdditionalCollisions();
            for (int i = 0; i < boxes.Count; i++)
            {
                SW2URDF.URDF.Collision collision = i == 0
                    ? link.Collision
                    : new SW2URDF.URDF.Collision();
                collision.Geometry.UseBox(boxes[i].Width, boxes[i].Depth, boxes[i].Height);
                collision.Origin.SetXYZ(boxes[i].Center);
                collision.Origin.SetRPY(new[] { 0.0, 0.0, 0.0 });

                if (i > 0)
                {
                    link.AddAdditionalCollision(collision);
                }
            }
        }

        private static void SetCollisionPrimitiveOrigin(Link link, double[] center, double[] rpy)
        {
            link.Collision.Origin.SetXYZ(new[] { center[0], center[1], center[2] });
            link.Collision.Origin.SetRPY(rpy);
        }

        internal static double[] GetCylinderPrimitiveRpy(int axis)
        {
            switch (axis)
            {
                case 0:
                    return new[] { 0.0, Math.PI / 2.0, 0.0 };

                case 1:
                    return new[] { -Math.PI / 2.0, 0.0, 0.0 };

                default:
                    return new[] { 0.0, 0.0, 0.0 };
            }
        }

        private bool TryWriteBoxPrimitiveCollisionMesh(
            Link link,
            string windowsCollisionMeshFilename,
            out LinkLocalBoundingBox box)
        {
            box = null;
            try
            {
                box = CreateLinkLocalBoundingBox(link);
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

        private bool TryWriteCylinderPrimitiveCollisionMesh(
            Link link,
            string windowsCollisionMeshFilename,
            out LinkLocalBoundingBox box)
        {
            box = null;
            try
            {
                box = CreateLinkLocalBoundingBox(link);
                if (!box.IsUsable)
                {
                    logger.Warn(link.Name + ": could not create cylinder primitive collision mesh");
                    return false;
                }

                WriteCylinderPrimitiveStl(windowsCollisionMeshFilename, box);
                logger.Info(link.Name + ": wrote cylinder primitive collision mesh " +
                    windowsCollisionMeshFilename + " with bounding dimensions " +
                    box.Width + " x " + box.Depth + " x " + box.Height + " m");
                return true;
            }
            catch (Exception e)
            {
                logger.Warn(link.Name + ": cylinder primitive collision mesh export failed: " + e.Message);
                return false;
            }
        }

        private bool TryWriteSpherePrimitiveCollisionMesh(
            Link link,
            string windowsCollisionMeshFilename,
            out LinkLocalBoundingBox box)
        {
            box = null;
            try
            {
                box = CreateLinkLocalBoundingBox(link);
                if (!box.IsUsable)
                {
                    logger.Warn(link.Name + ": could not create sphere primitive collision mesh");
                    return false;
                }

                WriteSpherePrimitiveStl(windowsCollisionMeshFilename, box);
                logger.Info(link.Name + ": wrote sphere primitive collision mesh " +
                    windowsCollisionMeshFilename + " with bounding dimensions " +
                    box.Width + " x " + box.Depth + " x " + box.Height + " m");
                return true;
            }
            catch (Exception e)
            {
                logger.Warn(link.Name + ": sphere primitive collision mesh export failed: " + e.Message);
                return false;
            }
        }

        private bool TryWriteConvexHullCollisionMesh(Link link, string windowsCollisionMeshFilename)
        {
            try
            {
                LinkLocalBoundingBox box = CreateLinkLocalBoundingBox(link);
                if (!box.IsUsable)
                {
                    logger.Warn(link.Name + ": could not create convex hull collision mesh");
                    return false;
                }

                WriteConvexHullPrimitiveStl(windowsCollisionMeshFilename, box);
                logger.Info(link.Name + ": wrote convex hull collision mesh " +
                    windowsCollisionMeshFilename + " from " +
                    box.Points.Count.ToString(CultureInfo.InvariantCulture) + " local bounding points");
                return true;
            }
            catch (Exception e)
            {
                logger.Warn(link.Name + ": convex hull collision mesh export failed: " + e.Message);
                return false;
            }
        }

        private bool TryWriteComponentBoxCollisionMesh(
            Link link,
            string windowsCollisionMeshFilename,
            out IList<LinkLocalBoundingBox> boxes)
        {
            boxes = new List<LinkLocalBoundingBox>();
            try
            {
                boxes = CreateComponentLocalBoundingBoxes(link);
                if (boxes.Count == 0)
                {
                    logger.Warn(link.Name + ": could not create component box collision set");
                    return false;
                }

                WriteComponentBoxPrimitiveStl(windowsCollisionMeshFilename, boxes);
                logger.Info(link.Name + ": wrote component box collision set " +
                    windowsCollisionMeshFilename + " with " +
                    boxes.Count.ToString(CultureInfo.InvariantCulture) + " boxes");
                return true;
            }
            catch (Exception e)
            {
                logger.Warn(link.Name + ": component box collision mesh export failed: " + e.Message);
                return false;
            }
        }

        internal LinkLocalBoundingBox CreateLinkLocalBoundingBox(Link link)
        {
            LinkLocalBoundingBox box = new LinkLocalBoundingBox();
            if (link == null || link.SWComponents == null || link.SWComponents.Count == 0 ||
                link.FrameReference == null || !link.FrameReference.IsExplicit)
            {
                return box;
            }

            MathTransform linkTransform =
                GetCoordinateSystemTransform(link.FrameReference);
            if (linkTransform == null)
            {
                return box;
            }

            Matrix<double> globalToLink = MathOps.GetTransformation(linkTransform).Inverse();
            HashSet<string> visitedComponents =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Component2 comp in link.SWComponents)
            {
                IncludeComponentGeometryRecursive(
                    box,
                    globalToLink,
                    comp,
                    visitedComponents);
            }

            return box;
        }

        internal IList<LinkLocalBoundingBox> CreateComponentLocalBoundingBoxes(Link link)
        {
            List<LinkLocalBoundingBox> boxes = new List<LinkLocalBoundingBox>();
            if (link == null || link.SWComponents == null || link.SWComponents.Count == 0 ||
                link.FrameReference == null || !link.FrameReference.IsExplicit)
            {
                return boxes;
            }

            MathTransform linkTransform =
                GetCoordinateSystemTransform(link.FrameReference);
            if (linkTransform == null)
            {
                return boxes;
            }

            Matrix<double> globalToLink = MathOps.GetTransformation(linkTransform).Inverse();
            HashSet<string> visitedComponents =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Component2 comp in link.SWComponents)
            {
                AddComponentLocalBoundingBoxesRecursive(
                    boxes,
                    globalToLink,
                    comp,
                    visitedComponents);
            }

            return boxes;
        }

        private bool IncludeComponentGeometryRecursive(
            LinkLocalBoundingBox targetBox,
            Matrix<double> globalToLink,
            Component2 component,
            ISet<string> visitedComponents)
        {
            if (component == null ||
                !visitedComponents.Add(GetComponentGeometryIdentity(component)))
            {
                return false;
            }

            LinkLocalBoundingBox componentGeometry = new LinkLocalBoundingBox();
            bool includedBranch = IncludeComponentBodyTessellation(
                componentGeometry,
                globalToLink,
                component);
            targetBox.Include(componentGeometry);

            object[] children = GetComponentChildren(component);
            if (children != null)
            {
                foreach (Component2 child in children)
                {
                    includedBranch = IncludeComponentGeometryRecursive(
                        targetBox,
                        globalToLink,
                        child,
                        visitedComponents) || includedBranch;
                }
            }

            if (!includedBranch)
            {
                LinkLocalBoundingBox fallback = new LinkLocalBoundingBox();
                IncludeTransformedBoxCorners(
                    fallback,
                    globalToLink,
                    GetComponentBox(component));
                targetBox.Include(fallback);
                includedBranch = fallback.IsUsable;
            }

            return includedBranch;
        }

        private bool AddComponentLocalBoundingBoxesRecursive(
            ICollection<LinkLocalBoundingBox> boxes,
            Matrix<double> globalToLink,
            Component2 component,
            ISet<string> visitedComponents)
        {
            if (component == null ||
                !visitedComponents.Add(GetComponentGeometryIdentity(component)))
            {
                return false;
            }

            LinkLocalBoundingBox box = new LinkLocalBoundingBox();
            bool includedBranch = IncludeComponentBodyTessellation(
                box,
                globalToLink,
                component);
            if (box.IsUsable)
            {
                boxes.Add(box);
            }

            object[] children = GetComponentChildren(component);
            if (children != null)
            {
                foreach (Component2 child in children)
                {
                    includedBranch = AddComponentLocalBoundingBoxesRecursive(
                        boxes,
                        globalToLink,
                        child,
                        visitedComponents) || includedBranch;
                }
            }

            if (!includedBranch)
            {
                LinkLocalBoundingBox fallback = new LinkLocalBoundingBox();
                IncludeTransformedBoxCorners(
                    fallback,
                    globalToLink,
                    GetComponentBox(component));
                if (fallback.IsUsable)
                {
                    boxes.Add(fallback);
                    includedBranch = true;
                }
            }

            return includedBranch;
        }

        private bool IncludeComponentBodyTessellation(
            LinkLocalBoundingBox targetBox,
            Matrix<double> globalToLink,
            Component2 component)
        {
            object[] bodies;
            try
            {
                object bodyInfo;
                bodies = component.GetBodies3(
                    (int)swBodyType_e.swSolidBody,
                    out bodyInfo) as object[];
            }
            catch (Exception e)
            {
                logger.Debug("Body tessellation is unavailable for component " +
                    GetComponentGeometryIdentity(component), e);
                return false;
            }
            if (bodies == null || bodies.Length == 0)
            {
                return false;
            }

            Matrix<double> componentToLink;
            try
            {
                MathTransform componentTransform =
                    ReferenceGeometryResolver.GetComponentToRootTransform(component);
                if (componentTransform == null)
                {
                    logger.Warn("Component transform is unavailable for " +
                        GetComponentGeometryIdentity(component) +
                        "; body tessellation was not used.");
                    return false;
                }
                Matrix<double> componentToGlobal =
                    MathOps.GetTransformation(componentTransform);
                componentToLink = globalToLink * componentToGlobal;
            }
            catch (Exception e)
            {
                logger.Debug("Component transform is unavailable for " +
                    GetComponentGeometryIdentity(component), e);
                return false;
            }
            bool includedPoint = false;

            foreach (Body2 body in bodies)
            {
                if (body == null)
                {
                    continue;
                }

                try
                {
                    Tessellation tessellation = body.GetTessellation(null) as Tessellation;
                    if (tessellation == null)
                    {
                        continue;
                    }
                    tessellation.ImprovedQuality = true;
                    if (!tessellation.Tessellate())
                    {
                        continue;
                    }

                    int vertexCount = tessellation.GetVertexCount();
                    for (int index = 0; index < vertexCount; index++)
                    {
                        double[] point = tessellation.GetVertexPoint(index) as double[];
                        if (point == null || point.Length < 3)
                        {
                            continue;
                        }
                        IncludeTransformedPoint(
                            targetBox,
                            componentToLink,
                            point[0],
                            point[1],
                            point[2]);
                        includedPoint = true;
                    }
                }
                catch (Exception e)
                {
                    logger.Debug("Body tessellation failed for component " +
                        GetComponentGeometryIdentity(component), e);
                }
            }

            return includedPoint;
        }

        private object[] GetComponentChildren(Component2 component)
        {
            try
            {
                return component.GetChildren() as object[];
            }
            catch (Exception e)
            {
                logger.Debug("Could not enumerate child components for " +
                    GetComponentGeometryIdentity(component), e);
                return null;
            }
        }

        private double[] GetComponentBox(Component2 component)
        {
            try
            {
                return component.GetBox(false, false);
            }
            catch (Exception e)
            {
                logger.Debug("Component bounding box is unavailable for " +
                    GetComponentGeometryIdentity(component), e);
                return null;
            }
        }

        private static string GetComponentGeometryIdentity(Component2 component)
        {
            try
            {
                string name = component.Name2;
                return String.IsNullOrWhiteSpace(name)
                    ? "<component-id:" + component.GetID().ToString(CultureInfo.InvariantCulture) + ">"
                    : name;
            }
            catch
            {
                return "<unresolved-component:" +
                    component.GetHashCode().ToString(CultureInfo.InvariantCulture) + ">";
            }
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

            using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create, FileAccess.Write)))
            {
                byte[] header = new byte[80];
                writer.Write(header);
                writer.Write((uint)BoxTriangleIndices.Length);
                foreach (int[] triangle in BoxTriangleIndices)
                {
                    WriteBinaryStlTriangle(
                        writer,
                        vertices[triangle[0]],
                        vertices[triangle[1]],
                        vertices[triangle[2]]);
                }
            }
        }

        internal static void WriteComponentBoxPrimitiveStl(
            string filename,
            IEnumerable<LinkLocalBoundingBox> boxes)
        {
            if (boxes == null)
            {
                throw new InvalidOperationException("Component box collision set is invalid");
            }

            List<double[]> vertices = new List<double[]>();
            List<int[]> triangles = new List<int[]>();
            foreach (LinkLocalBoundingBox box in boxes)
            {
                if (box == null || !box.IsUsable)
                {
                    continue;
                }

                int offset = vertices.Count;
                vertices.AddRange(box.CreateCornerVertices());
                foreach (int[] triangle in BoxTriangleIndices)
                {
                    triangles.Add(new[]
                    {
                        triangle[0] + offset,
                        triangle[1] + offset,
                        triangle[2] + offset
                    });
                }
            }

            if (triangles.Count == 0)
            {
                throw new InvalidOperationException("Component box collision set has no usable boxes");
            }

            WriteBinaryStl(filename, vertices, triangles);
        }

        internal static void WriteCylinderPrimitiveStl(string filename, LinkLocalBoundingBox box)
        {
            if (box == null || !box.IsUsable)
            {
                throw new InvalidOperationException("Primitive collision cylinder is invalid");
            }

            const int segments = 24;
            Directory.CreateDirectory(Path.GetDirectoryName(filename));
            int axis = box.CylinderAxisIndex;
            int uAxis = (axis + 1) % 3;
            int vAxis = (axis + 2) % 3;
            double[] center = box.Center;
            double halfHeight = box.GetDimension(axis) / 2.0;
            double radius = Math.Max(box.GetDimension(uAxis), box.GetDimension(vAxis)) / 2.0;

            using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create, FileAccess.Write)))
            {
                byte[] header = new byte[80];
                writer.Write(header);
                writer.Write((uint)(segments * 4));
                for (int i = 0; i < segments; i++)
                {
                    double a0 = 2.0 * Math.PI * i / segments;
                    double a1 = 2.0 * Math.PI * (i + 1) / segments;
                    double[] bottom0 = CreateAxisPoint(center, axis, uAxis, vAxis, -halfHeight, radius, a0);
                    double[] bottom1 = CreateAxisPoint(center, axis, uAxis, vAxis, -halfHeight, radius, a1);
                    double[] top0 = CreateAxisPoint(center, axis, uAxis, vAxis, halfHeight, radius, a0);
                    double[] top1 = CreateAxisPoint(center, axis, uAxis, vAxis, halfHeight, radius, a1);
                    double[] bottomCenter = CreateAxisPoint(center, axis, uAxis, vAxis, -halfHeight, 0.0, 0.0);
                    double[] topCenter = CreateAxisPoint(center, axis, uAxis, vAxis, halfHeight, 0.0, 0.0);

                    WriteBinaryStlTriangle(writer, bottom0, bottom1, top1);
                    WriteBinaryStlTriangle(writer, bottom0, top1, top0);
                    WriteBinaryStlTriangle(writer, bottomCenter, bottom1, bottom0);
                    WriteBinaryStlTriangle(writer, topCenter, top0, top1);
                }
            }
        }

        internal static void WriteSpherePrimitiveStl(string filename, LinkLocalBoundingBox box)
        {
            if (box == null || !box.IsUsable)
            {
                throw new InvalidOperationException("Primitive collision sphere is invalid");
            }

            const int latitudeBands = 8;
            const int longitudeBands = 16;
            Directory.CreateDirectory(Path.GetDirectoryName(filename));
            double[] center = box.Center;
            double radius = Math.Max(box.Width, Math.Max(box.Depth, box.Height)) / 2.0;
            List<double[]> vertices = new List<double[]>();
            for (int lat = 0; lat <= latitudeBands; lat++)
            {
                double theta = Math.PI * lat / latitudeBands;
                double sinTheta = Math.Sin(theta);
                double cosTheta = Math.Cos(theta);
                for (int lon = 0; lon < longitudeBands; lon++)
                {
                    double phi = 2.0 * Math.PI * lon / longitudeBands;
                    vertices.Add(new[]
                    {
                        center[0] + radius * sinTheta * Math.Cos(phi),
                        center[1] + radius * sinTheta * Math.Sin(phi),
                        center[2] + radius * cosTheta
                    });
                }
            }

            List<int[]> triangles = new List<int[]>();
            for (int lat = 0; lat < latitudeBands; lat++)
            {
                for (int lon = 0; lon < longitudeBands; lon++)
                {
                    int nextLon = (lon + 1) % longitudeBands;
                    int current = lat * longitudeBands + lon;
                    int currentNext = lat * longitudeBands + nextLon;
                    int below = (lat + 1) * longitudeBands + lon;
                    int belowNext = (lat + 1) * longitudeBands + nextLon;
                    if (lat > 0)
                    {
                        triangles.Add(new[] { current, below, currentNext });
                    }
                    if (lat < latitudeBands - 1)
                    {
                        triangles.Add(new[] { currentNext, below, belowNext });
                    }
                }
            }

            WriteBinaryStl(filename, vertices, triangles);
        }

        internal static void WriteConvexHullPrimitiveStl(string filename, LinkLocalBoundingBox box)
        {
            ConvexHullGeometry geometry = BuildConvexHullGeometry(box);
            WriteBinaryStl(filename, geometry.Vertices, geometry.Triangles);
        }

        internal static ConvexHullGeometry BuildConvexHullGeometry(LinkLocalBoundingBox box)
        {
            if (box == null || !box.IsUsable)
            {
                throw new InvalidOperationException("Convex hull collision mesh is invalid");
            }

            IEnumerable<double[]> hullSource = box.Points.Count >= 4
                ? (IEnumerable<double[]>)box.Points
                : box.CreateCornerVertices();
            List<double[]> sourcePoints = UniquePoints(hullSource);
            if (sourcePoints.Count < 4)
            {
                sourcePoints = UniquePoints(box.CreateCornerVertices());
            }

            List<int[]> triangles = BuildConvexHullTriangles(sourcePoints);
            if (triangles.Count == 0)
            {
                sourcePoints = UniquePoints(box.CreateCornerVertices());
                triangles = BuildConvexHullTriangles(sourcePoints);
            }

            return new ConvexHullGeometry(sourcePoints, triangles);
        }

        private static double[] CreateAxisPoint(
            double[] center,
            int axis,
            int uAxis,
            int vAxis,
            double axisOffset,
            double radius,
            double angle)
        {
            double[] point = new[] { center[0], center[1], center[2] };
            point[axis] += axisOffset;
            point[uAxis] += radius * Math.Cos(angle);
            point[vAxis] += radius * Math.Sin(angle);
            return point;
        }

        private static void WriteBinaryStl(string filename, IList<double[]> vertices, IList<int[]> triangles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filename));
            using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create, FileAccess.Write)))
            {
                byte[] header = new byte[80];
                writer.Write(header);
                writer.Write((uint)triangles.Count);
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

        private static List<double[]> UniquePoints(IEnumerable<double[]> points)
        {
            List<double[]> unique = new List<double[]>();
            foreach (double[] point in points)
            {
                if (point == null || point.Length < 3)
                {
                    continue;
                }
                if (!unique.Any(existing => DistanceSquared(existing, point) < 1e-18))
                {
                    unique.Add(new[] { point[0], point[1], point[2] });
                }
            }
            return unique;
        }

        private static List<int[]> BuildConvexHullTriangles(IList<double[]> points)
        {
            List<int[]> triangles = new List<int[]>();
            if (points == null || points.Count < 4)
            {
                return triangles;
            }

            double tolerance = EstimatePointTolerance(points);
            Dictionary<string, HullPlane> planes = new Dictionary<string, HullPlane>();
            for (int i = 0; i < points.Count - 2; i++)
            {
                for (int j = i + 1; j < points.Count - 1; j++)
                {
                    for (int k = j + 1; k < points.Count; k++)
                    {
                        double[] normal = CalculateTriangleNormal(points[i], points[j], points[k]);
                        if (VectorLengthSquared(normal) <= 0.0)
                        {
                            continue;
                        }

                        int positive = 0;
                        int negative = 0;
                        for (int p = 0; p < points.Count; p++)
                        {
                            double signed = SignedDistance(normal, points[i], points[p]);
                            if (signed > tolerance)
                            {
                                positive++;
                            }
                            else if (signed < -tolerance)
                            {
                                negative++;
                            }
                            if (positive > 0 && negative > 0)
                            {
                                break;
                            }
                        }

                        if (positive > 0 && negative > 0)
                        {
                            continue;
                        }
                        if (positive > 0)
                        {
                            normal = new[] { -normal[0], -normal[1], -normal[2] };
                        }

                        double offset = -Dot(normal, points[i]);
                        string key = BuildPlaneKey(normal, offset, tolerance);
                        if (!planes.ContainsKey(key))
                        {
                            planes.Add(key, new HullPlane(normal, offset));
                        }
                    }
                }
            }

            foreach (HullPlane plane in planes.Values)
            {
                List<int> planePoints = new List<int>();
                for (int i = 0; i < points.Count; i++)
                {
                    if (Math.Abs(Dot(plane.Normal, points[i]) + plane.Offset) <= tolerance * 2.0)
                    {
                        planePoints.Add(i);
                    }
                }

                triangles.AddRange(TriangulateHullPlane(points, planePoints, plane.Normal));
            }

            return triangles;
        }

        private static IEnumerable<int[]> TriangulateHullPlane(
            IList<double[]> points,
            IList<int> planePointIndexes,
            double[] normal)
        {
            if (planePointIndexes.Count < 3)
            {
                yield break;
            }

            double[] centroid = new[] { 0.0, 0.0, 0.0 };
            foreach (int index in planePointIndexes)
            {
                centroid[0] += points[index][0];
                centroid[1] += points[index][1];
                centroid[2] += points[index][2];
            }
            centroid[0] /= planePointIndexes.Count;
            centroid[1] /= planePointIndexes.Count;
            centroid[2] /= planePointIndexes.Count;

            double[] u = FindPlaneBasisU(points, planePointIndexes, centroid);
            double[] v = Cross(normal, u);
            NormalizeInPlace(v);

            List<int> ordered = planePointIndexes
                .OrderBy(index =>
                {
                    double[] delta = Subtract(points[index], centroid);
                    return Math.Atan2(Dot(delta, v), Dot(delta, u));
                })
                .ToList();

            for (int i = 1; i < ordered.Count - 1; i++)
            {
                int[] triangle = new[] { ordered[0], ordered[i], ordered[i + 1] };
                double[] triangleNormal = CalculateTriangleNormal(
                    points[triangle[0]],
                    points[triangle[1]],
                    points[triangle[2]]);
                if (Dot(triangleNormal, normal) < 0.0)
                {
                    triangle = new[] { ordered[0], ordered[i + 1], ordered[i] };
                }
                yield return triangle;
            }
        }

        private static double[] FindPlaneBasisU(
            IList<double[]> points,
            IEnumerable<int> planePointIndexes,
            double[] centroid)
        {
            foreach (int index in planePointIndexes)
            {
                double[] candidate = Subtract(points[index], centroid);
                if (VectorLengthSquared(candidate) > 1e-18)
                {
                    NormalizeInPlace(candidate);
                    return candidate;
                }
            }
            return new[] { 1.0, 0.0, 0.0 };
        }

        private static double EstimatePointTolerance(IList<double[]> points)
        {
            double maxDistance = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    maxDistance = Math.Max(maxDistance, Math.Sqrt(DistanceSquared(points[i], points[j])));
                }
            }
            return Math.Max(maxDistance, 1e-9) * 1e-8;
        }

        private static string BuildPlaneKey(double[] normal, double offset, double tolerance)
        {
            double scale = Math.Max(tolerance * 10.0, 1e-9);
            return RoundForKey(normal[0], scale) + "|" +
                RoundForKey(normal[1], scale) + "|" +
                RoundForKey(normal[2], scale) + "|" +
                RoundForKey(offset, scale);
        }

        private static string RoundForKey(double value, double scale)
        {
            return Math.Round(value / scale).ToString(CultureInfo.InvariantCulture);
        }

        private static double SignedDistance(double[] normal, double[] pointOnPlane, double[] point)
        {
            return Dot(normal, Subtract(point, pointOnPlane));
        }

        private static double[] Subtract(double[] lhs, double[] rhs)
        {
            return new[] { lhs[0] - rhs[0], lhs[1] - rhs[1], lhs[2] - rhs[2] };
        }

        private static double Dot(double[] lhs, double[] rhs)
        {
            return lhs[0] * rhs[0] + lhs[1] * rhs[1] + lhs[2] * rhs[2];
        }

        private static double[] Cross(double[] lhs, double[] rhs)
        {
            return new[]
            {
                lhs[1] * rhs[2] - lhs[2] * rhs[1],
                lhs[2] * rhs[0] - lhs[0] * rhs[2],
                lhs[0] * rhs[1] - lhs[1] * rhs[0]
            };
        }

        private static void NormalizeInPlace(double[] vector)
        {
            double length = Math.Sqrt(VectorLengthSquared(vector));
            if (length <= 0.0)
            {
                return;
            }

            vector[0] /= length;
            vector[1] /= length;
            vector[2] /= length;
        }

        private static double DistanceSquared(double[] lhs, double[] rhs)
        {
            double dx = lhs[0] - rhs[0];
            double dy = lhs[1] - rhs[1];
            double dz = lhs[2] - rhs[2];
            return dx * dx + dy * dy + dz * dz;
        }

        private static double VectorLengthSquared(double[] vector)
        {
            return vector[0] * vector[0] + vector[1] * vector[1] + vector[2] * vector[2];
        }

        private sealed class HullPlane
        {
            public HullPlane(double[] normal, double offset)
            {
                Normal = normal;
                Offset = offset;
            }

            public double[] Normal { get; private set; }

            public double Offset { get; private set; }
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

        internal sealed class ConvexHullGeometry
        {
            public ConvexHullGeometry(IList<double[]> vertices, IList<int[]> triangles)
            {
                Vertices = vertices;
                Triangles = triangles;
            }

            public IList<double[]> Vertices { get; private set; }

            public IList<int[]> Triangles { get; private set; }
        }

        internal class LinkLocalBoundingBox
        {
            private const double MinimumDimension = 1e-9;
            private const double DuplicatePointTolerance = 1e-12;
            private static readonly double[][] SupportDirections = CreateSupportDirections();
            private bool hasPoint;
            private readonly double[][] supportPoints = new double[SupportDirections.Length][];
            private readonly double[] supportScores = CreateInitialSupportScores();

            public double MinX { get; private set; }
            public double MinY { get; private set; }
            public double MinZ { get; private set; }
            public double MaxX { get; private set; }
            public double MaxY { get; private set; }
            public double MaxZ { get; private set; }

            public double Width => MaxX - MinX;
            public double Depth => MaxY - MinY;
            public double Height => MaxZ - MinZ;
            public IReadOnlyList<double[]> Points => GetDistinctSupportPoints();
            public double[] Center => new[]
            {
                (MinX + MaxX) / 2.0,
                (MinY + MaxY) / 2.0,
                (MinZ + MaxZ) / 2.0
            };
            public int LongestAxisIndex
            {
                get
                {
                    if (Width >= Depth && Width >= Height)
                    {
                        return 0;
                    }
                    return Depth >= Height ? 1 : 2;
                }
            }

            public int CylinderAxisIndex
            {
                get
                {
                    int bestAxis = 0;
                    double bestRadialMismatch = Double.PositiveInfinity;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        int firstRadialAxis = (axis + 1) % 3;
                        int secondRadialAxis = (axis + 2) % 3;
                        double firstRadius = GetDimension(firstRadialAxis);
                        double secondRadius = GetDimension(secondRadialAxis);
                        double scale = Math.Max(firstRadius, secondRadius);
                        double mismatch = scale > MinimumDimension
                            ? Math.Abs(firstRadius - secondRadius) / scale
                            : Double.PositiveInfinity;
                        if (mismatch < bestRadialMismatch)
                        {
                            bestAxis = axis;
                            bestRadialMismatch = mismatch;
                        }
                    }
                    return bestAxis;
                }
            }

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

                for (int index = 0; index < SupportDirections.Length; index++)
                {
                    double[] direction = SupportDirections[index];
                    double score = direction[0] * x + direction[1] * y + direction[2] * z;
                    if (score > supportScores[index])
                    {
                        supportScores[index] = score;
                        supportPoints[index] = new[] { x, y, z };
                    }
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

            public void Include(LinkLocalBoundingBox other)
            {
                if (other == null || !other.hasPoint)
                {
                    return;
                }
                foreach (double[] point in other.Points)
                {
                    Include(point[0], point[1], point[2]);
                }
            }

            private IReadOnlyList<double[]> GetDistinctSupportPoints()
            {
                List<double[]> result = new List<double[]>();
                foreach (double[] point in supportPoints)
                {
                    if (point == null || ContainsPoint(result, point))
                    {
                        continue;
                    }
                    result.Add(point);
                }
                return result;
            }

            private static bool ContainsPoint(IEnumerable<double[]> points, double[] candidate)
            {
                foreach (double[] point in points)
                {
                    if (Math.Abs(point[0] - candidate[0]) <= DuplicatePointTolerance &&
                        Math.Abs(point[1] - candidate[1]) <= DuplicatePointTolerance &&
                        Math.Abs(point[2] - candidate[2]) <= DuplicatePointTolerance)
                    {
                        return true;
                    }
                }
                return false;
            }

            private static double[][] CreateSupportDirections()
            {
                List<double[]> directions = new List<double[]>();
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            if (x == 0 && y == 0 && z == 0)
                            {
                                continue;
                            }
                            directions.Add(new[] { (double)x, (double)y, (double)z });
                        }
                    }
                }
                return directions.ToArray();
            }

            private static double[] CreateInitialSupportScores()
            {
                double[] scores = new double[SupportDirections.Length];
                for (int index = 0; index < scores.Length; index++)
                {
                    scores[index] = Double.NegativeInfinity;
                }
                return scores;
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

            public double GetDimension(int axis)
            {
                switch (axis)
                {
                    case 0:
                        return Width;

                    case 1:
                        return Depth;

                    case 2:
                        return Height;

                    default:
                        throw new ArgumentOutOfRangeException("axis");
                }
            }

            private static bool IsFinite(double value)
            {
                return !Double.IsNaN(value) && !Double.IsInfinity(value);
            }
        }

        private void Save3dxml(Link link, string windowsMeshFilename)
        {
            ExecuteWithVisibleLinkComponents(link, () =>
            {
                Save3dxmlWithVisibleComponents(link, windowsMeshFilename);
                return true;
            });
        }

        private void Save3dxmlWithVisibleComponents(Link link, string windowsMeshFilename)
        {
            int errors = 0;
            int warnings = 0;

            MathTransform frameTransform = GetCoordinateSystemTransform(link.FrameReference);
            if (frameTransform == null)
            {
                throw new InvalidOperationException(
                    "Cannot export 3DXML because the Link frame cannot be resolved: " +
                    GetReferenceDisplayLabel(link.FrameReference));
            }
            logger.Info(link.Name + ": Exporting 3DXML in assembly coordinates and " +
                "localizing its URDF origin from persistent Link frame " +
                GetReferenceDisplayLabel(link.FrameReference));

            int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent |
                (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
            SetLinkSpecificSTLPreferences(link, ActiveSWModel);

            logger.Info("Saving 3dxml to " + windowsMeshFilename);

            bool saved = ActiveSWModel.Extension.SaveAs(
                windowsMeshFilename,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                saveOptions,
                null,
                ref errors,
                ref warnings);

            if (!saved || errors != 0 || !File.Exists(windowsMeshFilename))
            {
                throw new InvalidOperationException(
                    "Exporting 3DXML for Link " + link.Name + " failed with error " + errors +
                    " and warnings " + warnings + ".");
            }
            if (warnings != 0)
            {
                logger.Warn("Exporting 3DXML for Link " + link.Name +
                    " completed with warnings " + warnings + ".");
            }
        }

        private StlExportStats SaveSTL(Link link, string windowsMeshFilename)
        {
            return SaveSTL(link, windowsMeshFilename, null);
        }

        private StlExportStats SaveSTL(Link link, string windowsMeshFilename, double? reductionRatioOverride)
        {
            using (OperationHeartbeat.Start(logger, "STL export for link " + link.Name))
            {
                return ExecuteWithVisibleLinkComponents(
                    link,
                    () => SaveStlWithVisibleComponents(
                        link,
                        windowsMeshFilename,
                        reductionRatioOverride));
            }
        }

        private StlExportStats SaveStlWithVisibleComponents(
            Link link,
            string windowsMeshFilename,
            double? reductionRatioOverride)
        {
            int errors = 0;
            int warnings = 0;

            UpdateProgressTitle("Preparing STL: " + link.Name,
                "\u6b63\u5728\u51c6\u5907 STL: " + link.Name);

            MathTransform frameTransform = GetCoordinateSystemTransform(link.FrameReference);
            if (frameTransform == null)
            {
                throw new InvalidOperationException(
                    "Cannot export STL because the Link frame cannot be resolved: " +
                    GetReferenceDisplayLabel(link.FrameReference));
            }
            logger.Info(link.Name + ": Exporting STL in assembly coordinates, then transforming " +
                "vertices to persistent Link frame " +
                GetReferenceDisplayLabel(link.FrameReference));

            ModelDoc2 activeDoc = ActiveSWModel;

            int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent |
                (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
            StlMeshSettings meshSettings =
                SetLinkSpecificSTLPreferences(link, activeDoc, reductionRatioOverride);
            StlExportStats stlStats = CreateStlExportStats(link, meshSettings);

            logger.Info("Saving STL to " + windowsMeshFilename);
            UpdateProgressTitle("SolidWorks is saving STL: " + link.Name,
                "SolidWorks \u6b63\u5728\u4fdd\u5b58 STL: " + link.Name);
            bool saved = activeDoc.Extension.SaveAs(windowsMeshFilename,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion, saveOptions, null,
                ref errors, ref warnings);
            if (!saved || errors != 0 || !File.Exists(windowsMeshFilename))
            {
                throw new InvalidOperationException(
                    "Exporting STL for Link " + link.Name + " failed with error " + errors +
                    " and warnings " + warnings + ".");
            }
            if (warnings != 0)
            {
                logger.Warn("Exporting STL for Link " + link.Name +
                    " completed with warnings " + warnings + ".");
            }

            UpdateProgressTitle("Finalizing STL: " + link.Name,
                "\u6b63\u5728\u6574\u7406 STL: " + link.Name);
            bool success = TransformBinaryStlToFrame(windowsMeshFilename, frameTransform);
            if (!success)
            {
                throw new InvalidOperationException(
                    "The STL for Link " + link.Name +
                    " could not be transformed into its Link coordinate frame.");
            }
            LogActualBinaryStlSize(link, windowsMeshFilename, stlStats);
            return stlStats;
        }

        private T ExecuteWithVisibleLinkComponents<T>(Link link, Func<T> operation)
        {
            UpdateProgressTitle("Reading component visibility: " + link.Name,
                "\u6b63\u5728\u8bfb\u53d6\u7ec4\u4ef6\u663e\u793a\u72b6\u6001: " + link.Name);
            List<ComponentVisibilityState> visibilityBeforeExport =
                CaptureComponentVisibility(CommonSwOperations.EnumerateComObjects<Component2>(
                    ((AssemblyDoc)ActiveSWModel).GetComponents(false), "capturing mesh isolation visibility"));
            List<ComponentVisibilityState> meshVisibility =
                CreateIsolatedVisibilityPlan(visibilityBeforeExport, link.SWComponents);
            bool visibilityMayHaveChanged = false;
            Exception operationFailure = null;
            try
            {
                visibilityMayHaveChanged = true;
                UpdateProgressTitle("Showing mesh components: " + link.Name,
                    "\u6b63\u5728\u663e\u793a\u7f51\u683c\u7ec4\u4ef6: " + link.Name);
                RestoreComponentVisibility(ActiveSWModel, meshVisibility);
                return operation();
            }
            catch (Exception exception)
            {
                operationFailure = exception;
                throw;
            }
            finally
            {
                if (visibilityMayHaveChanged)
                {
                    try
                    {
                        try
                        {
                            UpdateProgressTitle("Restoring mesh component visibility: " + link.Name,
                                "\u6b63\u5728\u6062\u590d\u7f51\u683c\u7ec4\u4ef6\u663e\u793a\u72b6\u6001: " + link.Name);
                        }
                        catch (Exception progressException)
                        {
                            logger.Warn("Updating cleanup progress failed; visibility restoration will continue.",
                                progressException);
                        }
                        RestoreComponentVisibility(ActiveSWModel, visibilityBeforeExport);
                    }
                    catch (Exception cleanupException)
                    {
                        if (operationFailure != null)
                        {
                            logger.Error(
                                "Restoring component visibility after a mesh export failure also failed.",
                                cleanupException);
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                "ERROR COMPONENT_VISIBILITY: SolidWorks component visibility could not be restored after mesh export. " + cleanupException.Message,
                                cleanupException);
                        }
                    }
                }
            }
        }

        internal static List<ComponentVisibilityState> CreateIsolatedVisibilityPlan(
            IList<ComponentVisibilityState> assemblyStates, IEnumerable<Component2> requested)
        {
            List<Component2> requestedRoots = (requested ?? Enumerable.Empty<Component2>())
                .Where(component => component != null).ToList();
            foreach (Component2 component in requestedRoots)
            {
                if (component.IsSuppressed())
                    throw new InvalidOperationException(
                        "ERROR COMPONENT_VISIBILITY: A component selected for this Link is suppressed: " + component.Name2 + ".");
            }
            Dictionary<string, Component2> assembly = assemblyStates.ToDictionary(
                state => state.Component.Name2, state => state.Component, StringComparer.OrdinalIgnoreCase);
            HashSet<string> visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> resolvedAncestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ComponentVisibilityState requestedState in CaptureComponentVisibility(requestedRoots))
            {
                Component2 current = requestedState.Component;
                if (current.IsSuppressed()) continue;
                HashSet<string> chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (current != null)
                {
                    string identity = current.Name2;
                    if (String.IsNullOrWhiteSpace(identity) || !assembly.ContainsKey(identity))
                        throw new InvalidOperationException(
                            "ERROR COMPONENT_VISIBILITY: Link component or ancestor is not in the active assembly: " + identity + ".");
                    if (!chain.Add(identity))
                        throw new InvalidOperationException(
                            "ERROR COMPONENT_VISIBILITY: Cyclic component parent reference: " + identity + ".");
                    if (current.IsSuppressed())
                        throw new InvalidOperationException(
                            "ERROR COMPONENT_VISIBILITY: A required Link ancestor is suppressed: " + identity + ".");
                    visible.Add(identity);
                    if (resolvedAncestors.Contains(identity)) break;
                    current = current.GetParent();
                }
                resolvedAncestors.UnionWith(chain);
            }
            // Ancestors provide the path to a nested Link, not ownership of their other children.
            return assemblyStates.Select(state => new ComponentVisibilityState(state.Component,
                visible.Contains(state.Component.Name2)
                    ? (int)swComponentVisibilityState_e.swComponentVisible
                    : (int)swComponentVisibilityState_e.swComponentHidden)).ToList();
        }

        internal static List<ComponentVisibilityState> CaptureComponentVisibility(
            IEnumerable<Component2> rootComponents)
        {
            List<ComponentVisibilityState> states = new List<ComponentVisibilityState>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Component2 component in rootComponents ?? new Component2[0])
            {
                CaptureComponentVisibilityRecursive(component, states, visited);
            }
            return states;
        }

        private static void CaptureComponentVisibilityRecursive(
            Component2 component,
            ICollection<ComponentVisibilityState> states,
            ISet<string> visited)
        {
            if (component == null)
            {
                return;
            }
            string identity = component.Name2;
            if (String.IsNullOrWhiteSpace(identity))
            {
                throw new InvalidOperationException("A component has no assembly-instance identity.");
            }
            if (!visited.Add(identity ?? String.Empty))
            {
                return;
            }

            states.Add(new ComponentVisibilityState(component, component.Visible));
            // A partial snapshot cannot safely drive mesh visibility or restore the assembly.
            object[] children = component.GetChildren() as object[];
            foreach (object child in children ?? new object[0])
            {
                CaptureComponentVisibilityRecursive(child as Component2, states, visited);
            }
        }

        internal static void RestoreComponentVisibility(
            ModelDoc2 model,
            IList<ComponentVisibilityState> states)
        {
            RestoreComponentVisibility(model, states, component => new DispatchWrapper(component));
        }

        internal static void RestoreComponentVisibility(ModelDoc2 model,
            IList<ComponentVisibilityState> states, Func<Component2, object> prepareSelection)
        {
            if (model == null || states == null)
            {
                return;
            }
            Exception operationFailure = null;
            try
            {
                List<Component2> visibleComponents = states
                    .Where(state => state.Visibility ==
                        (int)swComponentVisibilityState_e.swComponentVisible)
                    .Select(state => state.Component)
                    .ToList();
                List<Component2> hiddenComponents = states
                    .Where(state => state.Visibility ==
                        (int)swComponentVisibilityState_e.swComponentHidden)
                    .Select(state => state.Component)
                    .ToList();
                List<Exception> failures = new List<Exception>();
                if (visibleComponents.Count > 0)
                {
                    try { CommonSwOperations.SetComponentVisibility(model, visibleComponents, true, prepareSelection); }
                    catch (Exception exception) { failures.Add(exception); }
                }
                if (hiddenComponents.Count > 0)
                {
                    try { CommonSwOperations.SetComponentVisibility(model, hiddenComponents, false, prepareSelection); }
                    catch (Exception exception) { failures.Add(exception); }
                }
                // Showing a child can unhide its parent. Restore descendants first and
                // parent states last, then verify the entire snapshot before discarding it.
                List<ComponentVisibilityState> orderedStates = states.OrderByDescending(state =>
                    state.Component.Name2.Count(character => character == '/')).ToList();
                foreach (ComponentVisibilityState state in orderedStates)
                {
                    try
                    {
                        if (!state.Component.IsSuppressed() && state.Component.Visible != state.Visibility)
                            state.Component.Visible = state.Visibility;
                    }
                    catch (Exception exception)
                    {
                        failures.Add(new InvalidOperationException(
                            "Could not restore component " + state.Component.Name2 + ".", exception));
                    }
                }
                foreach (ComponentVisibilityState state in orderedStates)
                {
                    try
                    {
                        if (!state.Component.IsSuppressed() && state.Component.Visible != state.Visibility)
                            throw new InvalidOperationException("Component visibility was not restored: " + state.Component.Name2 + ".");
                    }
                    catch (Exception exception) { failures.Add(exception); }
                }
                // State-only failures may have been repaired above; cleanup and unexpected
                // API failures still block, even when visibility now matches.
                foreach (CommonSwOperations.ComponentVisibilityException recovered in failures
                    .OfType<CommonSwOperations.ComponentVisibilityException>()
                    .Where(failure => !failure.SelectionCleanupFailed).ToList())
                {
                    logger.Warn("A visibility group required the final snapshot repair.", recovered);
                    failures.Remove(recovered);
                }
                if (failures.Count > 0)
                    throw new AggregateException("ERROR COMPONENT_VISIBILITY: Restoring component visibility failed. " +
                        String.Join(" ", failures.Select(failure => failure.Message)), failures);
            }
            catch (Exception exception)
            {
                operationFailure = exception;
                throw;
            }
            finally
            {
                try { model.ClearSelection2(true); }
                catch (Exception cleanupException)
                {
                    if (operationFailure == null)
                        throw new InvalidOperationException(
                            "ERROR COMPONENT_VISIBILITY: Component states were applied, but the final selection could not be cleared.",
                            cleanupException);
                    logger.Error("Clearing the selection after visibility restoration also failed.", cleanupException);
                }
            }
            states.Clear();
        }

        internal sealed class ComponentVisibilityState
        {
            internal ComponentVisibilityState(Component2 component, int visibility)
            {
                Component = component;
                Visibility = visibility;
            }

            internal Component2 Component { get; private set; }
            internal int Visibility { get; private set; }
        }

        public void ExportLink(bool zIsUp)
        {
            logger.Info("Beginning part export for package " + PackageName +
                ", save path " + SavePath +
                ", z is up " + zIsUp +
                ", started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            URDFRobot.BaseLink.FrameReference = CreateBaseRefOrigin(zIsUp);
            MathTransform coordSysTransform =
                GetCoordinateSystemTransform(URDFRobot.BaseLink.FrameReference);
            if (coordSysTransform == null)
            {
                throw new InvalidOperationException(
                    "SolidWorks could not resolve the generated part Link coordinate system.");
            }

            MassPropertySnapshot massProperty = ReadLinkLocalMassProperty(
                null,
                coordSysTransform);
            ApplyMassPropertyToLink(URDFRobot.BaseLink, massProperty);
            EnsureNoBlockingInertialFailures(BuildPhysicalInertialValidationRecords(URDFRobot.BaseLink));

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

            bool preferencesSaved = false;
            bool exportCompleted = false;
            try
            {
                SaveUserPreferences();
                preferencesSaved = true;
                SetSTLExportPreferences();
                SetLinkSpecificSTLPreferences(URDFRobot.BaseLink, ActiveSWModel);
                int errors = 0;
                int warnings = 0;

                logger.Info("Saving part STL to " + windowsMeshFileName);
                bool saved = ActiveSWModel.Extension.SaveAs(windowsMeshFileName, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
                if (!saved || errors != 0 || !File.Exists(windowsMeshFileName))
                {
                    throw new InvalidOperationException(
                        "Exporting part STL failed with error " + errors +
                        " and warnings " + warnings + ".");
                }
                if (!TransformBinaryStlToFrame(windowsMeshFileName, coordSysTransform))
                {
                    throw new InvalidOperationException(
                        "The part STL could not be transformed into its Link coordinate frame.");
                }
                if (warnings != 0)
                {
                    logger.Warn("Exporting part STL completed with warnings " + warnings + ".");
                }
                URDFRobot.BaseLink.Visual.Geometry.UseMesh(meshFileName);
                URDFRobot.BaseLink.Collision.Geometry.UseMesh(meshFileName);

                URDFRobot.BaseLink.Visual.Material.Texture.Filename =
                    package.TexturesDirectory + Path.GetFileName(URDFRobot.BaseLink.Visual.Material.Texture.wFilename);
                string textureSavePath =
                    package.WindowsTexturesDirectory + Path.GetFileName(URDFRobot.BaseLink.Visual.Material.Texture.wFilename);
                if (!String.IsNullOrWhiteSpace(URDFRobot.BaseLink.Visual.Material.Texture.wFilename))
                {
                    File.Copy(URDFRobot.BaseLink.Visual.Material.Texture.wFilename, textureSavePath, true);
                }

                logger.Info("Writing part URDF file to " + windowsURDFFileName);
                URDFWriter uWriter = new URDFWriter(windowsURDFFileName);
                URDFRobot.WriteURDF(uWriter.writer);
                exportCompleted = true;
            }
            finally
            {
                if (preferencesSaved &&
                    !RestoreExportEnvironment(null, false, true, false) &&
                    exportCompleted)
                {
                    throw new InvalidOperationException("Part export completed, but SolidWorks STL preferences could not be restored.");
                }
            }
            logger.Info("Part export completed successfully for package " + PackageName);
        }

        internal static bool TransformBinaryStlToFrame(
            string filename,
            MathTransform frameTransform)
        {
            if (frameTransform == null)
            {
                throw new ArgumentNullException("frameTransform");
            }
            Matrix<double> rootToFrame =
                MathOps.GetTransformation(frameTransform).Inverse();
            return TransformBinaryStl(filename, rootToFrame);
        }

        internal static bool TransformBinaryStl(
            string filename,
            Matrix<double> rootToFrame)
        {
            if (rootToFrame == null || rootToFrame.RowCount != 4 || rootToFrame.ColumnCount != 4)
            {
                throw new ArgumentException(
                    "The STL coordinate transform must be a 4x4 matrix.",
                    "rootToFrame");
            }

            string temporaryFilename = filename + ".sw2urdf-transforming";
            try
            {
                using (FileStream source = OpenFileWithRetry(
                    filename,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                using (BinaryReader reader = new BinaryReader(source, Encoding.ASCII, true))
                using (FileStream destination = new FileStream(
                    temporaryFilename,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                using (BinaryWriter writer = new BinaryWriter(destination, Encoding.ASCII, true))
                {
                    if (source.Length < 84)
                    {
                        throw new InvalidDataException("The STL is too small to contain binary facets.");
                    }
                    reader.ReadBytes(80);
                    uint triangleCount = reader.ReadUInt32();
                    long expectedLength = 84L + 50L * triangleCount;
                    if (triangleCount == 0 || source.Length != expectedLength)
                    {
                        throw new InvalidDataException(
                            "The exported STL is not a complete binary STL.");
                    }

                    writer.Write(new byte[80]);
                    writer.Write(triangleCount);
                    for (uint triangleIndex = 0;
                        triangleIndex < triangleCount;
                        triangleIndex++)
                    {
                        ReadBinaryStlVector(reader);
                        double[] p0 = TransformStlPoint(
                            ReadBinaryStlVector(reader),
                            rootToFrame);
                        double[] p1 = TransformStlPoint(
                            ReadBinaryStlVector(reader),
                            rootToFrame);
                        double[] p2 = TransformStlPoint(
                            ReadBinaryStlVector(reader),
                            rootToFrame);
                        ushort attribute = reader.ReadUInt16();

                        WriteBinaryStlVector(writer, CalculateTriangleNormal(p0, p1, p2));
                        WriteBinaryStlVector(writer, p0);
                        WriteBinaryStlVector(writer, p1);
                        WriteBinaryStlVector(writer, p2);
                        writer.Write(attribute);
                    }
                }

                File.Replace(temporaryFilename, filename, null);
                return true;
            }
            catch (Exception exception)
            {
                logger.Warn(
                    "Transforming the binary STL into its Link coordinate frame failed.",
                    exception);
                try
                {
                    if (File.Exists(temporaryFilename))
                    {
                        File.Delete(temporaryFilename);
                    }
                }
                catch (IOException cleanupException)
                {
                    logger.Warn("Removing the incomplete transformed STL failed.", cleanupException);
                }
                return false;
            }
        }

        private static double[] ReadBinaryStlVector(BinaryReader reader)
        {
            return new[]
            {
                (double)reader.ReadSingle(),
                (double)reader.ReadSingle(),
                (double)reader.ReadSingle()
            };
        }

        private static double[] TransformStlPoint(
            double[] point,
            Matrix<double> transform)
        {
            Vector<double> source = Vector<double>.Build.Dense(new[]
            {
                point[0],
                point[1],
                point[2],
                1.0
            });
            Vector<double> result = transform * source;
            double w = Math.Abs(result[3]) < 1e-12 ? 1.0 : result[3];
            double[] transformed = new[]
            {
                result[0] / w,
                result[1] / w,
                result[2] / w
            };
            if (transformed.Any(value => Double.IsNaN(value) || Double.IsInfinity(value)))
            {
                throw new InvalidDataException(
                    "The Link coordinate transform produced a non-finite STL vertex.");
            }
            return transformed;
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
                    logger.Warn("The export log was expected at " + log_filename +
                        " but was not found, so it could not be copied into the export root.");
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
            iSwApp.SetUserPreferenceIntegerValue(
                (int)swUserPreferenceIntegerValue_e.swExportStlUnits,
                (int)swLengthUnit_e.swMETER);
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
        private StlMeshSettings SetLinkSpecificSTLPreferences(Link link, ModelDoc2 doc)
        {
            return SetLinkSpecificSTLPreferences(link, doc, null);
        }

        //If the user selected something specific for a particular link, that is handled here.
        private StlMeshSettings SetLinkSpecificSTLPreferences(
            Link link,
            ModelDoc2 doc,
            double? reductionRatioOverride)
        {
            bool modernPreferenceSet = doc.Extension.SetUserPreferenceString(
                (int)swUserPreferenceStringValue_e.swExportOutputCoordinateSystem,
                (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,
                string.Empty);
            bool legacyPreferenceSet = doc.Extension.SetUserPreferenceString(
                (int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem,
                (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,
                string.Empty);
            if (!modernPreferenceSet && !legacyPreferenceSet)
            {
                throw new InvalidOperationException(
                    "SolidWorks refused to reset the mesh export coordinate system to the assembly frame.");
            }
            double reductionRatio = reductionRatioOverride.HasValue
                ? reductionRatioOverride.Value
                : link.MeshReductionRatio;
            StlMeshSettings settings = CreateStlMeshSettings(link.STLQualityFine, reductionRatio);
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

        private StlExportStats CreateStlExportStats(Link link, StlMeshSettings settings)
        {
            StlExportStats stats = StlExportStats.FromSettings(settings);
            try
            {
                StlMeshSettings baselineSettings = CreateStlMeshSettings(link.STLQualityFine, 0.0);
                int baselineTriangleCount = EstimateStlTriangleCount(link, baselineSettings);
                if (baselineTriangleCount > 0)
                {
                    stats.BaselineEstimatedTriangles = baselineTriangleCount;
                    stats.BaselineEstimatedBytes = EstimateBinaryStlSizeBytes(baselineTriangleCount);
                }

                int triangleCount = EstimateStlTriangleCount(link, settings);
                if (triangleCount <= 0)
                {
                    logger.Info(link.Name + ": STL size estimate unavailable because tessellation returned no facets");
                    return stats;
                }

                long estimatedBytes = EstimateBinaryStlSizeBytes(triangleCount);
                stats.EstimatedTriangles = triangleCount;
                stats.EstimatedBytes = estimatedBytes;
                stats.EstimatedReductionPercent =
                    CalculateReductionPercent(triangleCount, baselineTriangleCount);
                logger.Info(string.Format(
                    "{0}: SolidWorks API rough STL estimate {1} ({2} triangles) before export; " +
                    "the final SaveAs tessellation can differ",
                    link.Name, FormatByteSize(estimatedBytes), triangleCount));
                if (stats.EstimatedReductionPercent.HasValue)
                {
                    logger.Info(string.Format(
                        "{0}: Rough STL estimated triangle reduction {1:+0.##;-0.##;0}% " +
                        "against baseline estimate {2} triangles",
                        link.Name,
                        stats.EstimatedReductionPercent.Value,
                        baselineTriangleCount));
                }
                return stats;
            }
            catch (Exception e)
            {
                logger.Warn("Could not estimate STL size for link " + link.Name, e);
                return stats;
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

        private void LogActualBinaryStlSize(Link link, string filename, StlExportStats stats)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(filename);
                uint? triangleCount = TryReadStlTriangleCount(filename);
                if (!triangleCount.HasValue)
                {
                    logger.Warn(link.Name + ": Could not read exported STL triangle count at " + filename);
                    return;
                }

                if (stats != null)
                {
                    stats.ActualBytes = fileInfo.Length;
                    stats.ActualTriangles = triangleCount.Value;
                    stats.ActualReductionPercent = CalculateReductionPercent(
                        triangleCount.Value,
                        stats.BaselineEstimatedTriangles.GetValueOrDefault());
                }

                logger.Info(string.Format("{0}: Actual STL size {1} ({2} triangles) at {3}",
                    link.Name, FormatByteSize(fileInfo.Length), triangleCount.Value, filename));

                int estimatedTriangleCount = stats == null
                    ? 0
                    : stats.EstimatedTriangles.GetValueOrDefault();
                if (estimatedTriangleCount > 0)
                {
                    double errorPercent = CalculateEstimateErrorPercent(estimatedTriangleCount, triangleCount.Value);
                    if (stats != null)
                    {
                        stats.EstimateErrorPercent = errorPercent;
                    }
                    string comparison = string.Format(
                        "{0}: Rough STL estimate error {1:+0.##;-0.##;0}% " +
                        "(estimated {2} triangles, actual {3} triangles)",
                        link.Name, errorPercent, estimatedTriangleCount, triangleCount.Value);
                    if (Math.Abs(errorPercent) > 50.0)
                    {
                        logger.Warn(comparison);
                    }
                    else
                    {
                        logger.Info(comparison);
                    }
                }
                if (stats != null && stats.ActualReductionPercent.HasValue)
                {
                    logger.Info(string.Format(
                        "{0}: Actual STL triangle reduction {1:+0.##;-0.##;0}% " +
                        "against baseline estimate {2} triangles",
                        link.Name,
                        stats.ActualReductionPercent.Value,
                        stats.BaselineEstimatedTriangles.GetValueOrDefault()));
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

        private static uint ReadStlTriangleCount(string filename)
        {
            uint? binaryTriangles = TryReadValidatedBinaryStlTriangleCount(filename);
            if (binaryTriangles.HasValue)
            {
                return binaryTriangles.Value;
            }

            uint? asciiTriangles = TryReadAsciiStlTriangleCount(filename);
            if (asciiTriangles.HasValue)
            {
                return asciiTriangles.Value;
            }

            throw new InvalidDataException("STL is neither valid binary STL nor recognizable ASCII STL.");
        }

        private static uint? TryReadValidatedBinaryStlTriangleCount(string filename)
        {
            using (FileStream fileStream = OpenFileWithRetry(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fileStream.Length < 84)
                {
                    return null;
                }

                fileStream.Seek(80, SeekOrigin.Begin);
                byte[] triangleCountBytes = new byte[4];
                if (fileStream.Read(triangleCountBytes, 0, triangleCountBytes.Length) != triangleCountBytes.Length)
                {
                    return null;
                }

                uint triangleCount = BitConverter.ToUInt32(triangleCountBytes, 0);
                long expectedLength = 84L + 50L * triangleCount;
                return expectedLength == fileStream.Length ? (uint?)triangleCount : null;
            }
        }

        private static uint? TryReadAsciiStlTriangleCount(string filename)
        {
            bool sawSolid = false;
            bool sawEndSolid = false;
            uint facets = 0;

            using (StreamReader reader = new StreamReader(filename, Encoding.ASCII, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.TrimStart();
                    if (!sawSolid &&
                        trimmed.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
                    {
                        sawSolid = true;
                    }

                    if (trimmed.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase))
                    {
                        if (facets == UInt32.MaxValue)
                        {
                            throw new InvalidDataException("ASCII STL facet count exceeds UInt32.MaxValue.");
                        }
                        facets++;
                    }

                    if (trimmed.StartsWith("endsolid", StringComparison.OrdinalIgnoreCase))
                    {
                        sawEndSolid = true;
                    }
                }
            }

            if (sawSolid && (facets > 0 || sawEndSolid))
            {
                return facets;
            }

            return null;
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

        internal static double? CalculateReductionPercent(long reducedTriangleCount, long baselineTriangleCount)
        {
            if (baselineTriangleCount <= 0)
            {
                return null;
            }

            return (baselineTriangleCount - reducedTriangleCount) * 100.0 / baselineTriangleCount;
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
            EventHandler<ExportProgressEventArgs> handler = ExportProgressChanged;
            if (handler != null)
            {
                ExportProgressEventArgs eventArgs = new ExportProgressEventArgs(
                    title,
                    exportStopwatch == null ? TimeSpan.Zero : exportStopwatch.Elapsed);
                foreach (EventHandler<ExportProgressEventArgs> subscriber in
                    handler.GetInvocationList())
                {
                    try
                    {
                        subscriber(this, eventArgs);
                    }
                    catch (Exception exception)
                    {
                        logger.Warn(
                            "An export progress observer failed; the export will continue.",
                            exception);
                    }
                }
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
