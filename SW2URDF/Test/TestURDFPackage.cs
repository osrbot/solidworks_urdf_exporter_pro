using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace SW2URDF.Test
{
    public class TestURDFPackage
    {
        private static string CreateRandomTempDirectory()
        {
            string name = Path.GetRandomFileName();
            string tempDirectory = Path.Combine(Path.GetTempPath(), name);
            Assert.True(Directory.CreateDirectory(tempDirectory).Exists);
            return tempDirectory;
        }

        private static void CreateRos1LaunchFiles(URDFPackage pkg)
        {
            Directory.CreateDirectory(pkg.WindowsLaunchDirectory);
            File.WriteAllText(
                Path.Combine(pkg.WindowsLaunchDirectory, "display.launch"),
                "<launch />",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsLaunchDirectory, "gazebo.launch"),
                "<launch />",
                new UTF8Encoding(false));
        }

        private static void CreateRos1ConfigCsvFiles(URDFPackage pkg)
        {
            Directory.CreateDirectory(pkg.WindowsConfigDirectory);
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\n",
                new UTF8Encoding(false));
        }

        [Fact]
        public void TestRos1AndRos2PackageDirectories()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("Robot Name.SLDASM", tempDirectory);

            Assert.Equal(Path.Combine(tempDirectory, "ROS1", "robot_name") + Path.DirectorySeparatorChar,
                pkg.WindowsPackageDirectory);
            Assert.Equal(Path.Combine(tempDirectory, "ROS2", "robot_name") + Path.DirectorySeparatorChar,
                pkg.WindowsRos2PackageDirectory);
            Assert.Equal(Path.Combine(tempDirectory, "export.log"), pkg.WindowsExportLogFile);
            Assert.Equal("robot_name", pkg.RobotName);
            Assert.Equal("robot_name", pkg.PackageName);
            Assert.Equal("robot_name", pkg.Ros2PackageName);
        }

        [Fact]
        public void TestRobotAndRosPackageNamesCanDiffer()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage(
                "osracer_blue",
                "osracer_description",
                tempDirectory);

            Assert.Equal("osracer_blue", pkg.RobotName);
            Assert.Equal("osracer_description", pkg.PackageName);
            Assert.Equal("osracer_description", pkg.Ros2PackageName);
            Assert.Equal(
                Path.Combine(tempDirectory, "ROS1", "osracer_description") + Path.DirectorySeparatorChar,
                pkg.WindowsPackageDirectory);
            Assert.Equal(
                Path.Combine(tempDirectory, "ROS2", "osracer_description") + Path.DirectorySeparatorChar,
                pkg.WindowsRos2PackageDirectory);

            pkg.CreateDirectories();
            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"osracer_blue\"><link name=\"base_link\" /></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            Assert.True(File.Exists(
                Path.Combine(pkg.WindowsRos2RobotsDirectory, "osracer_blue.urdf")));
            string displayLaunch = File.ReadAllText(
                Path.Combine(pkg.WindowsRos2LaunchDirectory, "display.launch.py"));
            Assert.Contains("get_package_share_directory('osracer_description')", displayLaunch);
            Assert.Contains("'osracer_blue.urdf'", displayLaunch);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestCreateDirectories()
        {
            string tempDirectory = CreateRandomTempDirectory();
            string name = Path.GetRandomFileName();
            URDFPackage pkg = new URDFPackage(name, tempDirectory);

            pkg.CreateDirectories();

            Assert.True(Directory.Exists(pkg.WindowsPackageDirectory));
            Assert.True(Directory.Exists(pkg.WindowsMeshesDirectory));
            Assert.True(Directory.Exists(pkg.WindowsRobotsDirectory));
            Assert.True(Directory.Exists(pkg.WindowsTexturesDirectory));
            Assert.True(Directory.Exists(pkg.WindowsLaunchDirectory));
            Assert.True(Directory.Exists(pkg.WindowsConfigDirectory));

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestGeneratedPackageMetadataHasMaintainer()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("metadata_robot", tempDirectory);

            pkg.CreateDirectories();

            string ros1PackageXml = Path.Combine(pkg.WindowsPackageDirectory, "package.xml");
            PackageXMLWriter packageXmlWriter = new PackageXMLWriter(ros1PackageXml);
            PackageXML packageXml = new PackageXML(pkg.PackageName);
            packageXml.WriteElement(packageXmlWriter);

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"metadata_robot\"><link name=\"base_link\" /></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            string ros1Package = File.ReadAllText(ros1PackageXml, Encoding.UTF8);
            string ros2Package = File.ReadAllText(
                Path.Combine(pkg.WindowsRos2PackageDirectory, "package.xml"),
                Encoding.UTF8);
            string ros2Setup = File.ReadAllText(
                Path.Combine(pkg.WindowsRos2PackageDirectory, "setup.py"),
                Encoding.UTF8);

            Assert.DoesNotContain("TODO", ros1Package);
            Assert.DoesNotContain("TODO", ros2Package);
            Assert.DoesNotContain("TODO", ros2Setup);
            Assert.Contains(
                "<maintainer email=\"" + PackageXML.DefaultMaintainerEmail + "\">" +
                PackageXML.DefaultMaintainerName + "</maintainer>",
                ros1Package);
            Assert.Contains(
                "<maintainer email=\"" + PackageXML.DefaultMaintainerEmail + "\">" +
                PackageXML.DefaultMaintainerName + "</maintainer>",
                ros2Package);
            Assert.Contains("maintainer='" + PackageXML.DefaultMaintainerName + "'", ros2Setup);
            Assert.Contains("maintainer_email='" + PackageXML.DefaultMaintainerEmail + "'", ros2Setup);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestCreateCMakeLists()
        {
            string tempDirectory = CreateRandomTempDirectory();
            string name = Path.GetRandomFileName();
            URDFPackage pkg = new URDFPackage(name, tempDirectory);
            pkg.CreateDirectories();
            pkg.CreateCMakeLists();

            Assert.True(File.Exists(pkg.WindowsCMakeLists));

            Console.WriteLine("Deleting directory " + tempDirectory);
            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestCreateConfigYAML()
        {
            string tempDirectory = CreateRandomTempDirectory();
            string name = Path.GetRandomFileName();
            Directory.CreateDirectory(tempDirectory);
            URDFPackage pkg = new URDFPackage(name, tempDirectory);
            pkg.CreateDirectories();
            pkg.CreateConfigYAML(new string[] {"a", "b", "c"});

            Assert.True(File.Exists(pkg.WindowsConfigYAML));

            Console.WriteLine("Deleting directory " + tempDirectory);
            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestBaseLinkEmptyJointNameIsNotExportedToControllerConfig()
        {
            Link baseLink = new Link(null);
            baseLink.Name = "base_link";
            baseLink.Joint.Name = "";
            baseLink.Joint.Type = "";

            Link childLink = new Link(baseLink);
            childLink.Name = "wheel";
            childLink.Joint.Name = "base_wheel";
            childLink.Joint.Type = "continuous";
            baseLink.Children.Add(childLink);

            Assert.Equal(new[] { "base_wheel" }, baseLink.GetJointNames(false));
        }

        [Fact]
        public void TestChinesePathPackageGeneration()
        {
            string tempDirectory = CreateRandomTempDirectory();
            string chineseDirectory = Path.Combine(tempDirectory, "中文 导出 路径");
            Directory.CreateDirectory(chineseDirectory);

            URDFPackage pkg = new URDFPackage("osracer_blue.SLDASM", chineseDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            pkg.CreateConfigYAML(new string[] { "joint_a" });

            string urdfPath = Path.Combine(pkg.WindowsRobotsDirectory, pkg.PackageName + ".urdf");
            File.WriteAllText(urdfPath,
                "<?xml version=\"1.0\"?><robot name=\"robot\"><link name=\"base_link\" /></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(urdfPath);

            Assert.Equal("osracer_blue", pkg.PackageName);
            Assert.True(Directory.Exists(pkg.WindowsPackageDirectory));
            Assert.True(Directory.Exists(pkg.WindowsRos2PackageDirectory));
            Assert.True(File.Exists(pkg.WindowsCMakeLists));
            Assert.True(File.Exists(pkg.WindowsConfigYAML));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2PackageDirectory, "package.xml")));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2PackageDirectory, "setup.py")));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2ResourceDirectory, pkg.Ros2PackageName)));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2RobotsDirectory, pkg.Ros2PackageName + ".urdf")));

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestRos2PackageCopiesNestedMeshDirectories()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            string visualMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "visual");
            string collisionMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "collision");
            Directory.CreateDirectory(visualMeshDirectory);
            Directory.CreateDirectory(collisionMeshDirectory);
            File.WriteAllText(Path.Combine(visualMeshDirectory, "base_link.STL"), "visual");
            File.WriteAllText(Path.Combine(collisionMeshDirectory, "base_link.STL"), "collision");
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\nbase_link,true,true\r\n",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\">" +
                "<link name=\"base_link\"><visual><geometry><mesh filename=\"package://rover_description/meshes/visual/base_link.STL\" /></geometry></visual>" +
                "<collision><geometry><mesh filename=\"package://rover_description/meshes/collision/base_link.STL\" /></geometry></collision></link></robot>",
                new UTF8Encoding(false));

            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2MeshesDirectory, "visual", "base_link.STL")));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2MeshesDirectory, "collision", "base_link.STL")));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2ConfigDirectory, "inertial_validation.csv")));
            Assert.True(File.Exists(Path.Combine(pkg.WindowsRos2ConfigDirectory, "mesh_manifest.csv")));
            string setupPy = File.ReadAllText(Path.Combine(pkg.WindowsRos2PackageDirectory, "setup.py"));
            Assert.Contains("package_files('meshes')", setupPy);
            Assert.Contains("glob(os.path.join(directory, '**', '*'), recursive=True)", setupPy);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportIsWrittenToRos1AndRos2Config()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));

            string visualMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "visual");
            string collisionMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "collision");
            Directory.CreateDirectory(visualMeshDirectory);
            Directory.CreateDirectory(collisionMeshDirectory);
            string visualMesh = Path.Combine(visualMeshDirectory, "base_link.STL");
            string collisionMesh = Path.Combine(collisionMeshDirectory, "base_link.STL");
            File.WriteAllText(visualMesh, "visual");
            File.WriteAllText(collisionMesh, "collision");
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\nbase_link,true,true\r\n",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\">" +
                "<link name=\"base_link\"><visual><geometry><mesh filename=\"package://rover_description/meshes/visual/base_link.STL\" /></geometry></visual>" +
                "<collision><geometry><mesh filename=\"package://rover_description/meshes/collision/base_link.STL\" /></geometry></collision></link></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            ExportHelper.InertialValidationRecord inertialRecord =
                new ExportHelper.InertialValidationRecord(
                    "base_link",
                    "Origin_global",
                    new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0));
            ExportHelper.MeshExportRecord meshRecord =
                new ExportHelper.MeshExportRecord(
                    "base_link",
                    "VisualMesh",
                    "VisualMesh",
                    "visual_mesh_copy",
                    "ok",
                    "STL",
                    "package://rover_description/meshes/visual/base_link.STL",
                    "package://rover_description/meshes/collision/base_link.STL",
                    visualMesh,
                    collisionMesh,
                    true,
                    true,
                    new FileInfo(visualMesh).Length,
                    new FileInfo(collisionMesh).Length,
                    0,
                    0,
                    new ExportHelper.StlExportStats
                    {
                        QualityLabel = "custom",
                        ReductionRatio = 0.5,
                        CustomSettings = true,
                        Deviation = 0.001,
                        AngleTolerance = 1.0,
                        BaselineEstimatedBytes = 5084,
                        BaselineEstimatedTriangles = 100,
                        EstimatedBytes = 2584,
                        EstimatedTriangles = 50,
                        EstimateErrorPercent = 0.0,
                        EstimatedReductionPercent = 50.0,
                        ActualReductionPercent = 50.0
                    });

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[] { inertialRecord },
                new[] { meshRecord },
                true,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string ros1Report = Path.Combine(pkg.WindowsConfigDirectory, "export_report.md");
            string ros2Report = Path.Combine(pkg.WindowsRos2ConfigDirectory, "export_report.md");
            Assert.True(File.Exists(ros1Report));
            Assert.True(File.Exists(ros2Report));

            string report = File.ReadAllText(ros1Report, Encoding.UTF8);
            Assert.Contains("Status: PASS", report);
            Assert.Contains("## Health Summary", report);
            Assert.Contains("| Check | Status | Detail |", report);
            Assert.Contains("| Overall | PASS | failures=0, warnings=0 |", report);
            Assert.Contains("| ROS 1 URDF | PASS | links=1, joints=0, mesh_refs=2, missing_mesh_refs=0, duplicate_links=0, duplicate_joints=0 |", report);
            Assert.Contains("| ROS 2 URDF | PASS | links=1, joints=0, mesh_refs=2, missing_mesh_refs=0, duplicate_links=0, duplicate_joints=0 |", report);
            Assert.Contains("| ROS package completeness | PASS | critical_missing=0, optional_missing=0 |", report);
            Assert.Contains("| ROS package parity | PASS | critical_mismatches=0, optional_mismatches=0 |", report);
            Assert.Contains("| Inertial validation | PASS | rows=1, failures=0, warnings=0 |", report);
            Assert.Contains("| Mesh manifest paths | PASS | rows=1, missing_visual=0, missing_collision=0 |", report);
            Assert.Contains("| Collision strategy | PASS | fallbacks=0, requested=VisualMesh=1, effective=VisualMesh=1, urdf_refs=mesh=1 |", report);
            Assert.Contains("| STL reduction | PASS | stats_rows=1, high_estimate_errors=0, ratios=0.5=1 |", report);
            Assert.Contains("Plugin version: ", report);
            Assert.Contains("Commit hash: ", report);
            Assert.Contains("Build time UTC: ", report);
            Assert.Contains("Dirty state: ", report);
            Assert.Contains("Export parameters: export_meshes=true, mesh_format=STL", report);
            Assert.Contains("inertial_validation_rows=1", report);
            Assert.Contains("mesh_manifest_rows=1", report);
            Assert.Contains("requested_collision_strategies=VisualMesh=1", report);
            Assert.Contains("collision_urdf_refs=mesh=1", report);
            Assert.Contains("stl_reduction_ratios=0.5=1", report);
            Assert.Contains("## Export Parameters", report);
            Assert.Contains("| output_root | " + pkg.WindowsExportRootDirectory + " |", report);
            Assert.Contains("| robot_name | robot_900001 |", report);
            Assert.Contains("| ros1_package_name | rover_description |", report);
            Assert.Contains("| ros2_package_name | rover_description |", report);
            Assert.Contains("| mesh_manifest_rows | 1 |", report);
            Assert.Contains("| requested_collision_strategies | VisualMesh=1 |", report);
            Assert.Contains("| collision_urdf_refs | mesh=1 |", report);
            Assert.Contains("| stl_reduction_ratios | 0.5=1 |", report);
            Assert.Contains("## ROS 1 URDF", report);
            Assert.Contains("## ROS 2 URDF", report);
            Assert.Contains("ROS 2 setup.py | OK", report);
            Assert.Contains("ROS 1 display.launch | OK", report);
            Assert.Contains("ROS 1 gazebo.launch | OK", report);
            Assert.Contains("ROS 2 display.launch.py | OK", report);
            Assert.Contains("ROS 2 gazebo.launch.py | OK", report);
            Assert.Contains("ROS 1 visual mesh files | OK", report);
            Assert.Contains("ROS 1 collision mesh files | OK", report);
            Assert.Contains("ROS 2 visual mesh files | OK", report);
            Assert.Contains("ROS 2 collision mesh files | OK", report);
            Assert.Contains("## ROS Package Parity", report);
            Assert.Contains("Parity mismatches: 0", report);
            Assert.Contains("| package | package.xml | yes | yes | yes |", report);
            Assert.Contains("| build | ROS1 CMakeLists.txt / ROS2 setup.py | yes | yes | yes |", report);
            Assert.Contains("| urdf | robot_900001.urdf | yes | yes | yes |", report);
            Assert.Contains("| launch | ROS1 display.launch / ROS2 display.launch.py | yes | yes | yes |", report);
            Assert.Contains("| launch | ROS1 gazebo.launch / ROS2 gazebo.launch.py | yes | yes | yes |", report);
            Assert.Contains("| config | inertial_validation.csv | yes | yes | yes |", report);
            Assert.Contains("| config | mesh_manifest.csv | yes | yes | yes |", report);
            Assert.Contains("| meshes/visual | base_link.STL | yes | yes | yes |", report);
            Assert.Contains("| meshes/collision | base_link.STL | yes | yes | yes |", report);
            Assert.Contains("Inertial validation rows: 1", report);
            Assert.Contains("Warning rows: 0", report);
            Assert.Contains("Physical inertia failures: 0", report);
            Assert.Contains("Magnitude warnings: 0", report);
            Assert.Contains("Inertia display warnings: 0", report);
            Assert.Contains("Display blocked by invalid physics: 0", report);
            Assert.Contains("Display failed after valid physics: 0", report);
            Assert.Contains("Magnitude warning links: none", report);
            Assert.Contains("Display failure links: none", report);
            Assert.Contains("### Inertial Link Summary", report);
            Assert.Contains("| Link | Coordinate system | Status | Rows | Numeric | Physical failures | Magnitude warnings | Display warnings | Max abs error | Max relative error | Failed quantities | Warning quantities |", report);
            Assert.Contains("| base_link | Origin_global | PASS | 1 | 1 | 0 | 0 | 0 | 0 | 0% | none | none |", report);
            Assert.Contains("Mesh manifest rows: 1", report);
            Assert.Contains("Average collision mesh byte reduction vs visual: ", report);
            Assert.Contains("Average collision mesh triangle reduction vs visual: none", report);
            Assert.Contains("Requested collision strategies: VisualMesh=1", report);
            Assert.Contains("Effective collision strategies: VisualMesh=1", report);
            Assert.Contains("Collision URDF refs: mesh=1", report);
            Assert.Contains("Collision strategy fallbacks: 0", report);
            Assert.Contains("## Collision Strategies", report);
            Assert.Contains("| Link | Requested | Effective | Geometry | URDF collision ref | Notes | Collision artifact exists | Collision artifact bytes | Collision artifact triangles | Byte reduction vs visual | Triangle reduction vs visual | Collision artifact URI |", report);
            Assert.Contains(
                "| base_link | VisualMesh | VisualMesh | visual_mesh_copy | package://rover_description/meshes/collision/base_link.STL | ok | true | " +
                new FileInfo(collisionMesh).Length.ToString() +
                " | 0 | ",
                report);
            Assert.Contains(" | none | package://rover_description/meshes/collision/base_link.STL |", report);
            Assert.Contains("Baseline estimated visual STL triangles: 100", report);
            Assert.Contains("Estimated visual STL triangles: 50", report);
            Assert.Contains("Requested STL reduction ratios: 0.5=1", report);
            Assert.Contains("STL quality settings: custom=1", report);
            Assert.Contains("Average estimated STL reduction: 50%", report);
            Assert.Contains("Average actual STL reduction: 50%", report);
            Assert.Contains("## STL Reduction Details", report);
            Assert.Contains("| Link | Quality | Ratio | Custom | Deviation (m) | Angle tolerance (rad) | Baseline est. bytes | Baseline est. triangles | Estimated bytes | Estimated triangles | Actual visual bytes | Actual visual triangles | Estimate error | Estimated reduction | Actual reduction |", report);
            Assert.Contains(
                "| base_link | custom | 0.5 | true | 0.001 | 1 | 5084 | 100 | 2584 | 50 | " +
                new FileInfo(visualMesh).Length.ToString() +
                " | 0 | 0% | 50% | 50% |",
                report);
            Assert.DoesNotContain("FAIL:", report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportHandlesRoverStyleMultiLinkPackage()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            pkg.CreateConfigYAML(new[] { "Body_LeftMainRocket", "BogieLF_WheelLF" });
            PackageXMLWriter packageXmlWriter =
                new PackageXMLWriter(Path.Combine(pkg.WindowsPackageDirectory, "package.xml"));
            new PackageXML(pkg.PackageName).WriteElement(packageXmlWriter);
            CreateRos1LaunchFiles(pkg);

            string[] linkNames =
            {
                "base_link",
                "WheelLF-1",
                "LiDAR-B",
                "IMU",
                "LeftMainRocket"
            };
            string visualMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "visual");
            string collisionMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "collision");
            Directory.CreateDirectory(visualMeshDirectory);
            Directory.CreateDirectory(collisionMeshDirectory);

            StringBuilder urdf = new StringBuilder();
            urdf.Append("<?xml version=\"1.0\"?><robot name=\"robot_900001\">");
            List<ExportHelper.InertialValidationRecord> inertialRecords =
                new List<ExportHelper.InertialValidationRecord>();
            List<ExportHelper.MeshExportRecord> meshRecords =
                new List<ExportHelper.MeshExportRecord>();
            for (int i = 0; i < linkNames.Length; i++)
            {
                string linkName = linkNames[i];
                string visualMesh = Path.Combine(visualMeshDirectory, linkName + ".STL");
                string collisionMesh = Path.Combine(collisionMeshDirectory, linkName + ".STL");
                File.WriteAllText(visualMesh, "visual-" + linkName, new UTF8Encoding(false));
                File.WriteAllText(collisionMesh, "collision-" + linkName, new UTF8Encoding(false));

                string visualUri = "package://rover_description/meshes/visual/" + linkName + ".STL";
                string collisionUri = "package://rover_description/meshes/collision/" + linkName + ".STL";
                urdf.Append("<link name=\"").Append(linkName).Append("\">")
                    .Append("<visual><geometry><mesh filename=\"").Append(visualUri)
                    .Append("\" /></geometry></visual>")
                    .Append("<collision><geometry><mesh filename=\"").Append(collisionUri)
                    .Append("\" /></geometry></collision></link>");

                inertialRecords.Add(new ExportHelper.InertialValidationRecord(
                    linkName,
                    "Origin_global",
                    new ExportHelper.InertialValidationRow("mass", "kg", 1.0 + i, 1.0 + i)));
                meshRecords.Add(new ExportHelper.MeshExportRecord(
                    linkName,
                    "VisualMesh",
                    "VisualMesh",
                    "visual_mesh_copy",
                    "ok",
                    "STL",
                    visualUri,
                    collisionUri,
                    visualMesh,
                    collisionMesh,
                    true,
                    true,
                    new FileInfo(visualMesh).Length,
                    new FileInfo(collisionMesh).Length,
                    (uint)(10 + i),
                    (uint)(10 + i),
                    new ExportHelper.StlExportStats
                    {
                        QualityLabel = "custom",
                        ReductionRatio = 0.35,
                        CustomSettings = true,
                        Deviation = 0.0005,
                        AngleTolerance = 0.5,
                        BaselineEstimatedBytes = 5084,
                        BaselineEstimatedTriangles = 100,
                        EstimatedBytes = 3334,
                        EstimatedTriangles = 65,
                        EstimateErrorPercent = 0.0,
                        EstimatedReductionPercent = 35.0,
                        ActualReductionPercent = 35.0
                    }));
            }
            urdf.Append("</robot>");

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(ros1Urdf, urdf.ToString(), new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                ExportHelper.BuildInertialValidationCsv(inertialRecords),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                ExportHelper.BuildMeshManifestCsv(meshRecords),
                new UTF8Encoding(false));

            pkg.CreateRos2Package(ros1Urdf);
            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                inertialRecords,
                meshRecords,
                true,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(2));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);
            Assert.Contains("Status: PASS", report);
            Assert.Contains("| ROS 1 URDF | PASS | links=5, joints=0, mesh_refs=10, missing_mesh_refs=0, duplicate_links=0, duplicate_joints=0 |", report);
            Assert.Contains("| ROS 2 URDF | PASS | links=5, joints=0, mesh_refs=10, missing_mesh_refs=0, duplicate_links=0, duplicate_joints=0 |", report);
            Assert.Contains("| ROS package parity | PASS | critical_mismatches=0, optional_mismatches=0 |", report);
            Assert.Contains("| Inertial validation | PASS | rows=5, failures=0, warnings=0 |", report);
            Assert.Contains("| Mesh manifest paths | PASS | rows=5, missing_visual=0, missing_collision=0 |", report);
            Assert.Contains("| Collision strategy | PASS | fallbacks=0, requested=VisualMesh=5, effective=VisualMesh=5, urdf_refs=mesh=5 |", report);
            Assert.Contains("| STL reduction | PASS | stats_rows=5, high_estimate_errors=0, ratios=0.35=5 |", report);
            Assert.Contains("| meshes/visual | WheelLF-1.STL | yes | yes | yes |", report);
            Assert.Contains("| meshes/collision | LiDAR-B.STL | yes | yes | yes |", report);
            Assert.Contains("| WheelLF-1 | Origin_global | PASS | 1 | 1 | 0 | 0 | 0 | 0 | 0% | none | none |", report);
            Assert.Contains("| LiDAR-B | VisualMesh | VisualMesh | visual_mesh_copy | package://rover_description/meshes/collision/LiDAR-B.STL | ok | true |", report);
            Assert.DoesNotContain("FAIL:", report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportFailsDuplicateJointNames()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            pkg.CreateConfigYAML(new[] { "dup", "dup" });
            PackageXMLWriter packageXmlWriter =
                new PackageXMLWriter(Path.Combine(pkg.WindowsPackageDirectory, "package.xml"));
            new PackageXML(pkg.PackageName).WriteElement(packageXmlWriter);
            CreateRos1LaunchFiles(pkg);

            string urdf =
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\">" +
                "<link name=\"base_link\" />" +
                "<link name=\"front_left_a\" />" +
                "<link name=\"front_left_b\" />" +
                "<joint name=\"dup\" type=\"fixed\"><parent link=\"base_link\" /><child link=\"front_left_a\" /></joint>" +
                "<joint name=\"dup\" type=\"fixed\"><parent link=\"front_left_a\" /><child link=\"front_left_b\" /></joint>" +
                "</robot>";
            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(ros1Urdf, urdf, new UTF8Encoding(false));
            pkg.CreateRos2Package(ros1Urdf);

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new ExportHelper.InertialValidationRecord[0],
                new ExportHelper.MeshExportRecord[0],
                false,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);
            Assert.Contains("Status: FAIL", report);
            Assert.Contains("| ROS 1 URDF | FAIL | links=3, joints=2, mesh_refs=0, missing_mesh_refs=0, duplicate_links=0, duplicate_joints=1 |", report);
            Assert.Contains("| ROS 2 URDF | FAIL | links=3, joints=2, mesh_refs=0, missing_mesh_refs=0, duplicate_links=0, duplicate_joints=1 |", report);
            Assert.Contains("- Duplicate joint names: dup", report);
            Assert.Contains("FAIL: ROS 1 URDF contains duplicate joint name: dup", report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportDocumentsNativeCollisionReference()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            pkg.CreateConfigYAML(new string[0]);
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<package><name>rover_description</name></package>",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\nbase_link,true,true\r\n",
                new UTF8Encoding(false));

            string visualMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "visual");
            string collisionMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "collision");
            Directory.CreateDirectory(visualMeshDirectory);
            Directory.CreateDirectory(collisionMeshDirectory);
            string visualMesh = Path.Combine(visualMeshDirectory, "base_link.STL");
            string collisionMesh = Path.Combine(collisionMeshDirectory, "base_link.STL");
            File.WriteAllText(visualMesh, "visual", new UTF8Encoding(false));
            File.WriteAllText(collisionMesh, "collision primitive artifact", new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\">" +
                "<link name=\"base_link\"><visual><geometry><mesh filename=\"package://rover_description/meshes/visual/base_link.STL\" /></geometry></visual>" +
                "<collision><geometry><box size=\"1 2 3\" /></geometry></collision></link></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            ExportHelper.InertialValidationRecord inertialRecord =
                new ExportHelper.InertialValidationRecord(
                    "base_link",
                    "Origin_global",
                    new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0));
            ExportHelper.MeshExportRecord meshRecord =
                new ExportHelper.MeshExportRecord(
                    "base_link",
                    "Primitive",
                    "BoxPrimitive",
                    "urdf_box_primitive",
                    "ok",
                    "STL",
                    "package://rover_description/meshes/visual/base_link.STL",
                    "package://rover_description/meshes/collision/base_link.STL",
                    visualMesh,
                    collisionMesh,
                    true,
                    true,
                    new FileInfo(visualMesh).Length,
                    new FileInfo(collisionMesh).Length,
                    0,
                    12,
                    ExportHelper.StlExportStats.NotExported(),
                    "native:box");

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[] { inertialRecord },
                new[] { meshRecord },
                true,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);

            Assert.Contains("Status: PASS", report);
            Assert.Contains("| ROS 1 URDF | PASS | links=1, joints=0, mesh_refs=1, missing_mesh_refs=0, duplicate_links=0, duplicate_joints=0 |", report);
            Assert.Contains("| ROS 2 URDF | PASS | links=1, joints=0, mesh_refs=1, missing_mesh_refs=0, duplicate_links=0, duplicate_joints=0 |", report);
            Assert.Contains("| Collision strategy | PASS | fallbacks=0, requested=Primitive=1, effective=BoxPrimitive=1, urdf_refs=native:box=1 |", report);
            Assert.Contains("collision_urdf_refs=native:box=1", report);
            Assert.Contains("| collision_urdf_refs | native:box=1 |", report);
            Assert.Contains("Collision URDF refs: native:box=1", report);
            Assert.Contains(
                "| base_link | Primitive | BoxPrimitive | urdf_box_primitive | native:box | ok | true | " +
                new FileInfo(collisionMesh).Length.ToString() +
                " | 12 | ",
                report);
            Assert.Contains(" | none | package://rover_description/meshes/collision/base_link.STL |", report);
            Assert.DoesNotContain("FAIL:", report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportWarnsWhenStlTriangleCountIsUnavailable()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            pkg.CreateConfigYAML(new string[0]);
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\nbase_link,true,true\r\n",
                new UTF8Encoding(false));

            string visualMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "visual");
            string collisionMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "collision");
            Directory.CreateDirectory(visualMeshDirectory);
            Directory.CreateDirectory(collisionMeshDirectory);
            string visualMesh = Path.Combine(visualMeshDirectory, "base_link.STL");
            string collisionMesh = Path.Combine(collisionMeshDirectory, "base_link.STL");
            File.WriteAllText(visualMesh, "not a valid stl", new UTF8Encoding(false));
            File.WriteAllText(collisionMesh, "also not a valid stl", new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\">" +
                "<link name=\"base_link\"><visual><geometry><mesh filename=\"package://rover_description/meshes/visual/base_link.STL\" /></geometry></visual>" +
                "<collision><geometry><mesh filename=\"package://rover_description/meshes/collision/base_link.STL\" /></geometry></collision></link></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            ExportHelper.MeshExportRecord meshRecord =
                new ExportHelper.MeshExportRecord(
                    "base_link",
                    "VisualMesh",
                    "VisualMesh",
                    "visual_collision_mesh",
                    "ok",
                    "STL",
                    "package://rover_description/meshes/visual/base_link.STL",
                    "package://rover_description/meshes/collision/base_link.STL",
                    visualMesh,
                    collisionMesh,
                    true,
                    true,
                    new FileInfo(visualMesh).Length,
                    new FileInfo(collisionMesh).Length,
                    null,
                    null,
                    ExportHelper.StlExportStats.NotExported());

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[]
                {
                    new ExportHelper.InertialValidationRecord(
                        "base_link",
                        "Origin_global",
                        new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0))
                },
                new[] { meshRecord },
                true,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);

            Assert.Contains("Status: WARN", report);
            Assert.Contains(
                "WARN: Visual STL triangle count for link base_link could not be read at " + visualMesh + ".",
                report);
            Assert.Contains(
                "WARN: Collision STL triangle count for link base_link could not be read at " + collisionMesh + ".",
                report);
            Assert.Contains("| base_link | VisualMesh | VisualMesh | visual_collision_mesh", report);
            Assert.DoesNotContain("FAIL:", report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportFailsWhenRos2MeshReferencesAreMissing()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));

            string visualMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "visual");
            string collisionMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "collision");
            Directory.CreateDirectory(visualMeshDirectory);
            Directory.CreateDirectory(collisionMeshDirectory);
            string visualMesh = Path.Combine(visualMeshDirectory, "base_link.STL");
            string collisionMesh = Path.Combine(collisionMeshDirectory, "base_link.STL");
            File.WriteAllText(visualMesh, "visual");
            File.WriteAllText(collisionMesh, "collision");
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\nbase_link,true,true\r\n",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\">" +
                "<link name=\"base_link\"><visual><geometry><mesh filename=\"package://rover_description/meshes/visual/base_link.STL\" /></geometry></visual>" +
                "<collision><geometry><mesh filename=\"package://rover_description/meshes/collision/base_link.STL\" /></geometry></collision></link></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            Directory.Delete(pkg.WindowsRos2MeshesDirectory, true);
            Directory.CreateDirectory(Path.Combine(pkg.WindowsRos2MeshesDirectory, "visual"));
            Directory.CreateDirectory(Path.Combine(pkg.WindowsRos2MeshesDirectory, "collision"));

            ExportHelper.MeshExportRecord meshRecord =
                new ExportHelper.MeshExportRecord(
                    "base_link",
                    "VisualMesh",
                    "VisualMesh",
                    "visual_mesh_copy",
                    "ok",
                    "STL",
                    "package://rover_description/meshes/visual/base_link.STL",
                    "package://rover_description/meshes/collision/base_link.STL",
                    visualMesh,
                    collisionMesh,
                    true,
                    true,
                    new FileInfo(visualMesh).Length,
                    new FileInfo(collisionMesh).Length,
                    0,
                    0,
                    ExportHelper.StlExportStats.NotExported());

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[]
                {
                    new ExportHelper.InertialValidationRecord(
                        "base_link",
                        "Origin_global",
                        new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0))
                },
                new[] { meshRecord },
                true,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);
            Assert.Contains("Status: FAIL", report);
            Assert.Contains("| ROS 2 URDF | FAIL | links=1, joints=0, mesh_refs=2, missing_mesh_refs=2, duplicate_links=0, duplicate_joints=0 |", report);
            Assert.Contains("| ROS package parity | FAIL | critical_mismatches=2, optional_mismatches=0 |", report);
            Assert.Contains("ROS 2 visual mesh files | MISSING", report);
            Assert.Contains("ROS 2 collision mesh files | MISSING", report);
            Assert.Contains("## ROS Package Parity", report);
            Assert.Contains("Parity mismatches: 2", report);
            Assert.Contains("| meshes/visual | base_link.STL | yes | no | yes |", report);
            Assert.Contains("| meshes/collision | base_link.STL | yes | no | yes |", report);
            Assert.Contains(
                "FAIL: ROS package parity mismatch for meshes/visual/base_link.STL: ROS1=true, ROS2=false",
                report);
            Assert.Contains(
                "FAIL: ROS package parity mismatch for meshes/collision/base_link.STL: ROS1=true, ROS2=false",
                report);
            Assert.Contains(
                "FAIL: ROS 2 mesh reference is unresolved: package://rover_description/meshes/visual/base_link.STL",
                report);
            Assert.Contains(
                "FAIL: ROS 2 mesh reference is unresolved: package://rover_description/meshes/collision/base_link.STL",
                report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportSummarizesInertialMagnitudeWarnings()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\"><link name=\"tiny_mass_link\" /></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            CreateRos1ConfigCsvFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            ExportHelper.InertialValidationRecord magnitudeWarning =
                new ExportHelper.InertialValidationRecord(
                    "tiny_mass_link",
                    "Origin_global",
                    ExportHelper.InertialValidationRow.Diagnostic(
                        "mass.magnitude",
                        "magnitude",
                        "WARN",
                        "Mass is outside the expected robotics export range [1e-9, 1e6] kg: 1e-12"));

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[] { magnitudeWarning },
                new ExportHelper.MeshExportRecord[0],
                false,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);
            Assert.Contains("Status: WARN", report);
            Assert.Contains("| Inertial validation | WARN | rows=1, failures=0, warnings=1 |", report);
            Assert.Contains("Warning rows: 1", report);
            Assert.Contains("Magnitude warnings: 1", report);
            Assert.Contains("Warning links: tiny_mass_link", report);
            Assert.Contains("Magnitude warning links: tiny_mass_link", report);
            Assert.Contains("| tiny_mass_link | Origin_global | WARN | 1 | 0 | 0 | 1 | 0 | none | none | none | mass.magnitude |", report);
            Assert.Contains("WARN: Inertial validation warning for link tiny_mass_link (1 rows).", report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportDistinguishesDisplayFailureFromInvalidPhysics()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\"><link name=\"display_link\" /></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            CreateRos1ConfigCsvFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            ExportHelper.InertialValidationRecord displayWarning =
                new ExportHelper.InertialValidationRecord(
                    "display_link",
                    "Origin_global",
                    ExportHelper.InertialValidationRow.Diagnostic(
                        "ellipsoid.display",
                        "display",
                        "WARN",
                        "Ellipsoid display failed although physical checks passed: SolidWorks could not display the inertia preview curve."));

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[] { displayWarning },
                new ExportHelper.MeshExportRecord[0],
                false,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);
            Assert.Contains("Status: WARN", report);
            Assert.Contains("Inertia display warnings: 1", report);
            Assert.Contains("Display blocked by invalid physics: 0", report);
            Assert.Contains("Display failed after valid physics: 1", report);
            Assert.Contains("Display failure links: display_link", report);
            Assert.Contains("| display_link | Origin_global | WARN | 1 | 0 | 0 | 0 | 1 | none | none | none | ellipsoid.display |", report);
            Assert.Contains("WARN: Inertial validation warning for link display_link (1 rows).", report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportFailsWhenRos2BuildFileIsMissing()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));

            string visualMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "visual");
            string collisionMeshDirectory = Path.Combine(pkg.WindowsMeshesDirectory, "collision");
            Directory.CreateDirectory(visualMeshDirectory);
            Directory.CreateDirectory(collisionMeshDirectory);
            string visualMesh = Path.Combine(visualMeshDirectory, "base_link.STL");
            string collisionMesh = Path.Combine(collisionMeshDirectory, "base_link.STL");
            File.WriteAllText(visualMesh, "visual");
            File.WriteAllText(collisionMesh, "collision");
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\nbase_link,true,true\r\n",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\">" +
                "<link name=\"base_link\"><visual><geometry><mesh filename=\"package://rover_description/meshes/visual/base_link.STL\" /></geometry></visual>" +
                "<collision><geometry><mesh filename=\"package://rover_description/meshes/collision/base_link.STL\" /></geometry></collision></link></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            File.Delete(Path.Combine(pkg.WindowsRos2PackageDirectory, "setup.py"));

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[]
                {
                    new ExportHelper.InertialValidationRecord(
                        "base_link",
                        "Origin_global",
                        new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0))
                },
                new ExportHelper.MeshExportRecord[0],
                true,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);
            Assert.Contains("Status: FAIL", report);
            Assert.Contains("ROS 2 setup.py | MISSING", report);
            Assert.Contains("Parity mismatches: 1", report);
            Assert.Contains("| build | ROS1 CMakeLists.txt / ROS2 setup.py | yes | no | yes |", report);
            Assert.Contains(
                "FAIL: ROS package parity mismatch for build/ROS1 CMakeLists.txt / ROS2 setup.py: ROS1=true, ROS2=false",
                report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportFailsWhenRos2LaunchFileIsMissing()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\n",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\"><link name=\"base_link\" /></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            File.Delete(Path.Combine(pkg.WindowsRos2LaunchDirectory, "display.launch.py"));

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[]
                {
                    new ExportHelper.InertialValidationRecord(
                        "base_link",
                        "Origin_global",
                        new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0))
                },
                new ExportHelper.MeshExportRecord[0],
                false,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);
            Assert.Contains("Status: FAIL", report);
            Assert.Contains("ROS 2 display.launch.py | MISSING", report);
            Assert.Contains("Parity mismatches: 1", report);
            Assert.Contains("| launch | ROS1 display.launch / ROS2 display.launch.py | yes | no | yes |", report);
            Assert.Contains(
                "FAIL: ROS package parity mismatch for launch/ROS1 display.launch / ROS2 display.launch.py: ROS1=true, ROS2=false",
                report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportFailsWhenRos2ConfigCsvIsMissing()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\n",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\"><link name=\"base_link\" /></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            File.Delete(Path.Combine(pkg.WindowsRos2ConfigDirectory, "mesh_manifest.csv"));

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[]
                {
                    new ExportHelper.InertialValidationRecord(
                        "base_link",
                        "Origin_global",
                        new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0))
                },
                new ExportHelper.MeshExportRecord[0],
                false,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);
            Assert.Contains("Status: FAIL", report);
            Assert.Contains("ROS 2 mesh manifest CSV | MISSING", report);
            Assert.Contains("| ROS package completeness | FAIL | critical_missing=1,", report);
            Assert.Contains("| ROS package parity | FAIL | critical_mismatches=1, optional_mismatches=0 |", report);
            Assert.Contains("| config | mesh_manifest.csv | yes | no | yes |", report);
            Assert.Contains(
                "FAIL: ROS package parity mismatch for config/mesh_manifest.csv: ROS1=true, ROS2=false",
                report);

            Directory.Delete(tempDirectory, true);
        }

        [Fact]
        public void TestExportReportFailsWhenMeshDirectoriesAreEmpty()
        {
            string tempDirectory = CreateRandomTempDirectory();
            URDFPackage pkg = new URDFPackage("robot_900001", "rover_description", tempDirectory);

            pkg.CreateDirectories();
            pkg.CreateCMakeLists();
            Directory.CreateDirectory(Path.Combine(pkg.WindowsMeshesDirectory, "visual"));
            Directory.CreateDirectory(Path.Combine(pkg.WindowsMeshesDirectory, "collision"));
            File.WriteAllText(
                Path.Combine(pkg.WindowsPackageDirectory, "package.xml"),
                "<?xml version=\"1.0\"?><package><name>rover_description</name></package>",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "inertial_validation.csv"),
                "link,status\r\nbase_link,PASS\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "mesh_manifest.csv"),
                "link,visual_exists,collision_exists\r\nbase_link,false,false\r\n",
                new UTF8Encoding(false));

            string ros1Urdf = Path.Combine(pkg.WindowsRobotsDirectory, pkg.RobotName + ".urdf");
            File.WriteAllText(
                ros1Urdf,
                "<?xml version=\"1.0\"?><robot name=\"robot_900001\"><link name=\"base_link\" /></robot>",
                new UTF8Encoding(false));
            CreateRos1LaunchFiles(pkg);
            pkg.CreateRos2Package(ros1Urdf);

            ExportHelper.WriteExportReport(
                pkg,
                ros1Urdf,
                new[]
                {
                    new ExportHelper.InertialValidationRecord(
                        "base_link",
                        "Origin_global",
                        new ExportHelper.InertialValidationRow("mass", "kg", 1.0, 1.0))
                },
                new ExportHelper.MeshExportRecord[0],
                true,
                MeshExportFormat.STL,
                TimeSpan.FromSeconds(1));

            string report = File.ReadAllText(
                Path.Combine(pkg.WindowsConfigDirectory, "export_report.md"),
                Encoding.UTF8);
            Assert.Contains("Status: FAIL", report);
            Assert.Contains("ROS 1 visual mesh files | MISSING", report);
            Assert.Contains("ROS 1 collision mesh files | MISSING", report);
            Assert.Contains("ROS 2 visual mesh files | MISSING", report);
            Assert.Contains("ROS 2 collision mesh files | MISSING", report);

            Directory.Delete(tempDirectory, true);
        }

        [Theory]
        [InlineData("osracer_blue.SLDASM", "osracer_blue")]
        [InlineData("OSRacer Blue.SLDPRT", "osracer_blue")]
        [InlineData("123 robot", "robot_123_robot")]
        public void TestSanitizePackageName(string input, string expected)
        {
            Assert.Equal(expected, URDFPackage.SanitizePackageName(input));
        }
    }
}
